using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HexTactica.AI;

/// <summary>
/// Orchestrates one AI player's turn.
///
/// The structure is: build a read of the board once, segment it into fronts, order
/// the units by how constrained they are, then ask each one's role-specific decider
/// for an action and execute it. The timer only paces *visibility* — every decision
/// is made by DecideNext(), which is pure with respect to frames and can therefore
/// be driven flat out by the batch harness.
/// </summary>
public class AIPlayer : MonoBehaviour {

    public Player player;
    public AIMissionObjective objective;
    public HexTactica.AI.AIProfile profile;

    [Tooltip("Seconds between visible actions. Ignored in headless mode.")]
    public float actionInterval = 0.5f;

    float timer;

    /// <summary>
    /// Called with every decision the AI makes, executed or not. Hook it to log or
    /// visualise reasoning; the batch harness and the headless tests use it to explain
    /// why a game went the way it did.
    /// </summary>
    public System.Action<Unit, AIAction, bool> onDecision;

    /// <summary>Text trace of every AI player's turn (board read, objectives, decisions). Set by GameLog.</summary>
    public static System.Action<string> Trace;

    AIMap map;
    List<Front> fronts = new List<Front>();
    List<Unit> queue = new List<Unit>();
    readonly Dictionary<Unit, TransportPlan> transportAssignments = new Dictionary<Unit, TransportPlan>();
    readonly HashSet<Unit> reservedTransports = new HashSet<Unit>();

    /// <summary>Each unit's destination from its last decision. Persists across turns.</summary>
    readonly Dictionary<Unit, HexTile> intents = new Dictionary<Unit, HexTile>();

    /// <summary>Last posture of each objective's front. Persists across turns for hysteresis.</summary>
    readonly Dictionary<HexTile, Posture> postureMemory = new Dictionary<HexTile, Posture>();
    readonly HashSet<HexTile> assaultMemory = new HashSet<HexTile>();
    List<Objective> objectives = new List<Objective>();
    /// <summary>This turn's group-plan steps (AIPlan), by unit, consumed as each unit acts.</summary>
    readonly Dictionary<Unit, PlanStep> planned = new Dictionary<Unit, PlanStep>();
    readonly HashSet<Unit> noAssist = new HashSet<Unit>();
    readonly List<PlanStep> planOrder = new List<PlanStep>();
    Dictionary<Unit, Objective> assignments = new Dictionary<Unit, Objective>();
    bool turnPrepared;

    /// <summary>Unit-turns that got a strategic-objective pull, and turns played. For the harness.</summary>
    public int objectivePulls, turnsPlayed;

    public HexTactica.AI.AIProfile Profile {
        get { return profile != null ? profile : HexTactica.AI.AIProfile.Default; }
    }

    void Start() {
        if (player == null) player = GetComponent<Player>();
    }

    void Update() {
        if (GameController.gc == null || GameController.gc.headless) return;
        if (GameController.gc.gameOver) return;
        if (LevelEditor.singleton != null &&
            (LevelEditor.singleton.editing || LevelEditor.singleton.unit_placement)) return;
        if (!GameController.gc.activePlayer.Equals(player)) { turnPrepared = false; return; }

        timer -= Time.deltaTime;
        if (timer > 0) return;
        timer = actionInterval;

        StepOnce(false);
    }

    /// <summary>
    /// Advance the AI by one action. Returns false when the turn is finished.
    /// This is the single entry point for both the live game and the harness.
    /// </summary>
    public bool StepOnce(bool headless) {
        if (player == null || player.eliminated) return false;

        if (!turnPrepared) {
            PrepareTurn();
            turnPrepared = true;
        }

        // Drain the queue until somebody actually does something.
        while (queue.Count > 0) {
            Unit unit = queue[0];
            queue.RemoveAt(0);

            if (unit == null || unit.Strength <= 0 || unit.tile == null) continue;
            if (!unit.active) continue;

            GameController.gc.activeUnit = unit;

            AIAction action = DecideFor(unit);
            bool did = HexTactica.AI.AIExecutor.Execute(action, map, headless);
            if (did) {
                // Any action can change what every other unit can reach, where supply
                // flows, and who the enemy can fire on: a tile was vacated or filled, a unit
                // died in a corridor, a hub moved. Conflict and distress are recomputed too.
                unit.FinishActive();
                map.RefreshAfterAction();
            }

            Annotate(unit, action);
            if (onDecision != null) onDecision(unit, action, did);
            if (Trace != null && (did || action.kind != AIActionKind.Wait))
                Trace("   " + player.personal_name + " " + unit.name.Replace("(Clone)", "") + ": " + action.kind + (did ? "" : " (not executed)") + " — " + action.reason);

            // A unit that waited changed nothing, so there is nothing to recalculate.
            unit.active = false;
            if (!did) GameController.gc.activeUnit = null;

            if (GameController.gc.gameOver) return false;
            if (did) return true;
        }

        EndTurn();
        return false;
    }

    /// <summary>Forget the turn in progress and everything remembered between turns: a new board was loaded.</summary>
    public void ResetTurn() {
        turnPrepared = false;
        queue.Clear();
        intents.Clear();
        postureMemory.Clear();
        assaultMemory.Clear();
        timer = actionInterval;
    }

    /// <summary>Run the whole turn without pacing. Used by the harness.</summary>
    public void PlayWholeTurn() {
        int guard = 0;
        while (StepOnce(true) && guard++ < 4096) { }
        if (guard >= 4096) UnityEngine.Debug.LogWarning("AIPlayer: turn guard tripped");
    }

    void PrepareTurn() {
        map = new AIMap(player, Profile);

        // Forget the intentions of units that no longer exist.
        List<Unit> stale = new List<Unit>();
        foreach (KeyValuePair<Unit, HexTile> kv in intents) {
            if (kv.Key == null || kv.Key.Strength <= 0 || kv.Key.tile == null) stale.Add(kv.Key);
        }
        for (int i = 0; i < stale.Count; i++) intents.Remove(stale[i]);
        fronts = AIStrategic.BuildFronts(map, Profile, postureMemory, assaultMemory);
        objectives = AIObjectives.Build(map, Profile);
        assignments = Profile.useObjectives ? AIObjectives.Assign(objectives, map, Profile) : new Dictionary<Unit, Objective>();
        objectivePulls += assignments.Count;
        turnsPlayed++;
        // Objectives are structure: they add a pull to units out of contact and nothing else.
        map.SetCommitted(AIStrategic.AssaultUnits(fronts));
        map.SetAssaultObjectives(AIStrategic.AssaultObjectives(fronts));
        map.intents = intents;
        transportAssignments.Clear();
        reservedTransports.Clear();

        // Units riding a transport stay in the queue: a hub aboard is the one that tells
        // its transport where to go. Excluding them meant a loaded hub was never planned
        // for, so its transport had no orders and the cargo was never delivered.
        List<Unit> actionable = new List<Unit>();
        for (int i = 0; i < map.ownUnits.Count; i++) {
            Unit u = map.ownUnits[i];
            if (u != null && u.active) actionable.Add(u);
        }

        queue = AIRoles.Order(actionable, map, Profile);
        map.ClearAnnotations();
        if (Profile.ghostSupply) PlanGhosts();
        planned.Clear();
        noAssist.Clear();
        if (Profile.coordination || Profile.coordinationPasses) {
            List<PlanStep> steps = Profile.coordinationPasses
                ? AICoordinator.Plan(map, Profile, u => {
                      AIWorld.NoAssist = null;
                      AITactical.Assignments = assignments;
                      Front f = AIStrategic.FrontFor(u, fronts);
                      return AITactical.Decide(u, map, Profile, f != null ? f.posture : Posture.Defensive, f, objective);
                  }, noAssist, Trace)
                : AIPlan.Choose(map, Profile, Trace);
            // Planned units act first, in plan order; everyone else keeps the usual order after them.
            List<Unit> first = new List<Unit>();
            planOrder.Clear();
            planOrder.AddRange(steps);
            foreach (PlanStep s in steps) if (s.unit != null && queue.Contains(s.unit) && !planned.ContainsKey(s.unit)) { planned[s.unit] = s; first.Add(s.unit); }
            foreach (Unit u in first) queue.Remove(u);
            queue.InsertRange(0, first);
        }
        if (Trace != null) {
            Trace("   " + player.personal_name + " (this AI) reads the board: " + map.SupplyReport());
            string contact = "";
            foreach (Unit u in map.ownUnits) contact += " " + u.name.Replace("(Clone)", "").Substring(0, 2) + "(" + u.tile.x + "," + u.tile.y + ")=" + map.TurnsToContact(u);
            Trace("   turns to contact:" + contact);
            foreach (Objective o in objectives) Trace("   objective " + o);
            Trace("   objective pulls: " + assignments.Count + "; turn order: " + string.Join(", ", queue.Select(q => q.name.Replace("(Clone)", "")).ToArray()));
        }
    }

    /// <summary>
    /// Supply goes first in thought and last in deed: each supply unit plans its move now,
    /// before the line, and the board read treats it as made, so the line can attack into
    /// supply that is coming. The supply unit still acts last, re-planning on the real board,
    /// so it can follow whatever the line actually did.
    /// </summary>
    void PlanGhosts() {
        for (int i = 0; i < map.ownHubs.Count; i++) {
            SupplyPoint hub = map.ownHubs[i];
            if (hub == null || hub.producer || hub.myUnit == null || !hub.myUnit.active) continue;
            AISupplyPlanner.Ghost = true;
            TransportPlan plan;
            try { plan = AISupplyPlanner.PlanHub(hub, map, Profile, new HashSet<Unit>(reservedTransports)); }
            finally { AISupplyPlanner.Ghost = false; }
            HexTile at = GhostTile(plan);
            if (at != null) map.SetGhost(hub, at);
            if (AISupplyPlanner.DebugLog != null) AISupplyPlanner.DebugLog("   ghost plan: " + (plan != null ? plan.ToString() : "none"));
            if (onGhost != null) onGhost(hub.myUnit, at);
            if (Trace != null) Trace("   ghost: " + hub.myUnit.name.Replace("(Clone)", "") + (at == null ? " stays" : " will move to (" + at.x + "," + at.y + ")") + "; the line plans with that supply");
        }
    }

    /// <summary>Where a plan would leave the supply unit at the end of this turn, if it can tell.</summary>
    HexTile GhostTile(TransportPlan plan) {
        if (plan == null || plan.cargo == null || plan.destination == null) return null;
        Unit cargo = plan.cargo;
        if (plan.usesTransport) return plan.eta <= 1 ? plan.destination : null;
        if (cargo.trasnporter != null) return null;
        HexTile step = map.ReachHorizon(cargo).StepToward(plan.destination);
        return step != null && step != cargo.tile ? step : null;
    }

    /// <summary>Called with each supply unit's planned (ghost) tile at the start of a turn; null = stays.</summary>
    public System.Action<Unit, HexTile> onGhost;

    /// <summary>
    /// Pick an action for one unit. Supply hubs and transports get the joint planner;
    /// everything else goes to its role-specific tactical decider.
    /// </summary>
    public AIAction DecideFor(Unit unit) {
        if (unit == null) return AIAction.Wait(null, "null");
        AIWorld.NoAssist = noAssist.Count > 0 ? noAssist : null;
        AITactical.Intents = intents;
        AITactical.Assignments = assignments;

        Front front = AIStrategic.FrontFor(unit, fronts);
        Posture posture = front != null ? front.posture : Posture.Defensive;


        // Mission objectives narrow the possibility space rather than commanding units,
        // which is what lets them compose with tactics instead of overriding them.
        if (objective != null && objective.OverridesTactics(unit, map)) {
            AIAction directed = objective.Decide(unit, map, Profile, posture);
            if (!directed.IsNothing) return directed;
        }

        SupplyPoint hub = unit.GetComponent<SupplyPoint>();
        if (hub != null) {
            if (hub.producer) return AIAction.Wait(unit, "HQ holds");
            // The real move is planned on the real board: the ghost has served its purpose.
            map.ClearGhost(hub);

            TransportPlan plan = AISupplyPlanner.PlanHub(hub, map, Profile, reservedTransports);
            if (plan == null && unit.trasnporter != null) plan = UnloadHere(unit);
            if (plan != null) {
                if (plan.transport != null) {
                    transportAssignments[plan.transport] = plan;
                    reservedTransports.Add(plan.transport);
                }
                return AITransportPlanner.NextAction(plan, map, Profile);
            }
            return AIAction.Wait(unit, "hub: no better position");
        }

        if (unit.trasnporter != null) return AIAction.Wait(unit, "aboard transport");

        PlanStep step;
        if (planned.TryGetValue(unit, out step) && Profile.coordinationPasses && step.group > 0) {
            // A battle plan runs only while it can still make its kill: earlier blows may have
            // driven the target out of reach or done less than forecast. Then the rest of the plan
            // is dropped and those units decide alone.
            List<PlanStep> rest = new List<PlanStep>();
            foreach (PlanStep s in planned.Values) if (s.group == step.group) rest.Add(s);
            rest.Sort((a, b) => planOrder.IndexOf(a).CompareTo(planOrder.IndexOf(b)));
            if (!AICoordinator.StillKills(rest, map)) {
                foreach (PlanStep s in rest) planned.Remove(s.unit);
                if (Trace != null) Trace("   battle plan on " + step.target.name.Replace("(Clone)", "") + " can no longer kill; " + rest.Count + " units decide alone");
            }
        }
        if (planned.TryGetValue(unit, out step)) {
            planned.Remove(unit);
            if (!AIPlan.StillValid(step, map) && Profile.coordinationPasses) {
                PlanStep fresh = AICoordinator.Refresh(step, map);
                if (fresh != null && AIPlan.StillValid(fresh, map)) step = fresh;
            }
            if (AIPlan.StillValid(step, map)) {
                string why = "group " + step.kind + ": " + step;
                if (step.target != null && (step.stop == null || step.stop == unit.tile)) return AIAction.Strike(unit, step.target, 0f, why);
                if (step.target != null) return AIAction.MoveStrike(unit, step.stop, step.target, 0f, why);
                if (step.stop != null && step.stop != unit.tile) return AIAction.Move(unit, step.stop, 0f, why);
                return AIAction.Wait(unit, why + " (hold)");
            }
            if (Trace != null) Trace("   " + unit.name.Replace("(Clone)", "") + ": planned step no longer possible (" + step + "); deciding alone");
        }

        if (Transporting.IsTransport(unit) && unit.Firepower <= 1) {
            TransportPlan assignment;
            if (transportAssignments.TryGetValue(unit, out assignment)) {
                return AITransportPlanner.TransportAction(unit, assignment, map);
            }
            if (unit.transported.Count > 0) {
                // Carrying someone nobody planned for: put them down.
                TransportPlan drop = UnloadHere(unit.transported[0]);
                if (drop != null) return AITransportPlanner.TransportAction(unit, drop, map);
            }
            return AITactical.DecideIdleTransport(unit, map, Profile);
        }

        return AITactical.Decide(unit, map, Profile, posture, front, objective);
    }

    /// <summary>A plan that drops cargo next to wherever its transport is now.</summary>
    TransportPlan UnloadHere(Unit cargo) {
        Unit transport = cargo != null ? cargo.trasnporter : null;
        if (transport == null || transport.tile == null) return null;
        List<HexTile> options = Transporting.UnloadOptions(transport, cargo);
        if (options.Count == 0) return null;
        options.Sort(CombatMath.CompareTiles);
        return new TransportPlan {
            cargo = cargo, transport = transport,
            destination = options[0], dropoff = transport.tile, rendezvous = transport.tile
        };
    }

    void Annotate(Unit unit, AIAction action) {
        if (!Profile.annotateBoard || unit == null) return;
        HexTile where = action.destination != null ? action.destination : unit.tile;
        if (where != null && !string.IsNullOrEmpty(action.reason)) {
            map.Annotate(where, action.reason);
        }
    }

    void EndTurn() {
        turnPrepared = false;
        GameController.gc.activeUnit = null;
        if (objective != null && objective.MissionFinished()) {
            UnityEngine.Debug.Log("AIPlayer: objective complete for " + player.personal_name);
        }
        GameController.gc.EndTurn();
    }

    /// <summary>Exposed for tests and the harness: the AI's current read of the board.</summary>
    public AIMap CurrentMap { get { return map; } }
    public List<Front> CurrentFronts { get { return fronts; } }
    public List<Objective> CurrentObjectives { get { return objectives; } }
    public Dictionary<Unit, Objective> CurrentAssignments { get { return assignments; } }
}
