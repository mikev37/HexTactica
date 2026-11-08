using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using HexTactica.AI;

/// <summary>
/// Orchestrates one AI player's turn.
///
/// Each decision is thought on a copy of the board (BoardView: as this side knows it under fog of
/// war), on a worker thread in the game (ThinkOnThread), and carried out on the real board on the
/// main thread when it is ready: the game keeps drawing while the AI thinks.
///
/// The structure is: build a read of the board once, segment it into fronts, order
/// the units by how constrained they are, then ask each one's role-specific decider
/// for an action and execute it. The timer only paces *visibility* — every decision
/// is made by DecideNext(), which is pure with respect to frames and can therefore
/// be driven flat out by the batch harness.
/// </summary>
public class AIPlayer : MonoBehaviour, IAIController {

    public Player player;
    public AIMissionObjective objective;
    public HexTactica.AI.AIProfile profile;

    [Tooltip("Seconds between visible actions. Ignored in headless mode.")]
    public float actionInterval = 0.5f;
    [Tooltip("Longest the AI holds an action that follows up on a retreat (a shot at the retreating unit, a move onto the tile it left) for the retreat to play, in seconds. Other actions only wait actionInterval.")]
    public float maxAnimationWait = 3f;
    [Tooltip("Hold the AI's next action while a retreat it follows up on is still playing. Off: the action goes ahead and the units' own animations wait instead (a shot is fired only once its target's sprite has reached its retreat tile; a move onto a tile being vacated starts once the retreating sprite has left it).")]
    public bool holdForRetreats = false;
    float animationWait;

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
    /// <summary>This turn's campaign (AIProfile.campaign), kept for the next turn's.</summary>
    AICampaign campaign;
    List<Unit> queue = new List<Unit>();
    readonly Dictionary<Unit, TransportPlan> transportAssignments = new Dictionary<Unit, TransportPlan>();
    readonly HashSet<Unit> reservedTransports = new HashSet<Unit>();
    /// <summary>This turn's supply backbone (supplyBackbone), planned once the army has moved.</summary>
    AISupplyBackbone.Plan backbone, backbonePlanned;
    /// <summary>This turn's supply for the campaign's fronts (AIProfile.campaignLogistics): each front's chain, then its own supply units.</summary>
    HexTactica.AI.AILogistics logistics;
    /// <summary>This turn's front-line supply units (supplyFrontFirst).</summary>
    readonly HashSet<SupplyPoint> frontHubs = new HashSet<SupplyPoint>();
    AIMap backboneTurn;

    /// <summary>Each unit's destination from its last decision. Persists across turns.</summary>
    readonly Dictionary<Unit, HexTile> intents = new Dictionary<Unit, HexTile>();

    /// <summary>Last posture of each objective's front. Persists across turns for hysteresis.</summary>
    readonly Dictionary<HexTile, Posture> postureMemory = new Dictionary<HexTile, Posture>();
    readonly Dictionary<HexTile, Posture> releasedMemory = new Dictionary<HexTile, Posture>();
    List<Front> releasedFronts;

    /// <summary>Is <paramref name="u"/> in the fight (within campaignReleaseTurns of contact), and so off its task force?</summary>
    bool Released(Unit u) {
        return campaign != null && releasedFronts != null && Profile.campaignReleaseTurns > 0 && u != null && u.tile != null && map != null && map.TurnsToContact(u) <= Profile.campaignReleaseTurns;
    }

    /// <summary>The front <paramref name="u"/> fights on: its task force's, or, in the fight, the ordinary front it falls in.</summary>
    Front FrontOf(Unit u) {
        if (Released(u)) { Front r = AIStrategic.FrontFor(u, releasedFronts); if (r != null) return r; }
        return AIStrategic.FrontFor(u, fronts);
    }
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

    /// <summary>
    /// Think on another thread (in the game; the headless harness always thinks in line). The AI
    /// plans on its own copy of the board (BoardView), so it never touches the real one while it
    /// thinks: the game keeps drawing, animating and answering clicks, and each decision is carried
    /// out on the main thread once it is ready. Off: every thought happens in the frame that asks.
    /// </summary>
#if UNITY_WEBGL && !UNITY_EDITOR
    public static bool ThinkOnThread = false;   // WebGL has no threads
#else
    public static bool ThinkOnThread = true;
#endif

    /// <summary>The headless harness: think each thought on a worker thread too, and run this on the calling thread while waiting (to check that thinking elsewhere changes nothing).</summary>
    public static bool HarnessThreads;
    public static System.Action WhileThinking;

    /// <summary>What one thought decided: the next unit to act and how, or that the turn is over.</summary>
    struct Thought { public Unit unit; public AIAction action; public bool endTurn; }

    enum Outcome { Did, NotDone, Held, TurnOver }

    Task<Thought> thinking;
    float thinkingSince;
    bool thinkingPrepares;

    /// <summary>Seconds the thought under way has been running, or -1 when none is (the AI is acting, or it is not its turn). For the screen (TurnBanner).</summary>
    public bool IsPlaying { get { return enabled; } }
    public bool FogFair { get { return Profile != null && Profile.fogFair; } }
    public float ThinkingFor { get { return thinking != null && !thinking.IsCompleted ? Time.unscaledTime - thinkingSince : -1f; } }
    int thinkingGeneration, generation;
    /// <summary>The last action changed the board: the next thought reads it again first (AIMap.RefreshAfterAction).</summary>
    bool refreshPending;

    void Update() {
        if (GameController.gc == null || GameController.gc.headless) return;
        if (GameController.gc.gameOver) return;
        if (LevelEditor.singleton != null &&
            (LevelEditor.singleton.editing || LevelEditor.singleton.unit_placement)) return;
        if (!GameController.gc.activePlayer.Equals(player)) { turnPrepared = false; Abandon(); MaybeSpeculate(); return; }
        // Our turn is over and waits for what is still playing on screen (GameController.EndTurnWhenStill).
        if (GameController.gc.EndTurnPending) return;

        // A thought under way: act on it once it is ready (the frame is free meanwhile).
        if (thinking != null) {
            if (!thinking.IsCompleted) return;
            Thought t;
            if (!Collect(out t)) return;
            Outcome o = Act(t, false);
            if (o == Outcome.NotDone) { Dispatch(); return; }   // nothing was done: the next unit thinks at once
            timer = actionInterval;
            return;
        }

        // An action held back until a retreat it follows up on has played (see WaitsOnRetreat).
        if (holding) {
            if (WaitsOnRetreat(held) && animationWait < maxAnimationWait) { animationWait += Time.deltaTime; return; }
            animationWait = 0f;
            AIAction a = held; holding = false;
            Run(a.unit, a, false);
            timer = actionInterval;
            return;
        }

        timer -= Time.deltaTime;
        if (timer > 0) return;
        timer = actionInterval;
        if (player == null || player.eliminated) return;
        Dispatch();
    }

    /// <summary>Start the next thought (preparing the turn first, if it is new), on its own thread when ThinkOnThread.</summary>
    void Dispatch() {
        bool prepare = !turnPrepared;
        if (prepare) FogOfWar.Changed();
        bool refresh = refreshPending;
        refreshPending = false;
        BoardView v = NewView();   // made here, on the main thread: it reads the real board
        thinkingPrepares = prepare;
        thinkingGeneration = generation;
        thinkingSince = Time.unscaledTime;
        if (ThinkOnThread) thinking = Task.Run(() => Think(v, prepare, refresh));
        else thinking = Task.FromResult(Think(v, prepare, refresh));
    }

    /// <summary>
    /// Take a finished thought. False if it belongs to a turn that is gone (a board was loaded).
    /// A thought that failed is logged and thought again here, on the main thread, on a fresh copy
    /// of the board; if that fails too, the turn ends rather than the game hanging.
    /// </summary>
    bool Collect(out Thought t) {
        Task<Thought> job = thinking;
        thinking = null;
        t = default(Thought);
        if (thinkingGeneration != generation) return false;
        if (job.IsFaulted || job.IsCanceled) {
            Debug.LogException(job.Exception != null ? job.Exception.GetBaseException() : new System.Exception("AI thought cancelled"));
            try { t = Think(NewView(), !turnPrepared || thinkingPrepares, true); }
            catch (System.Exception e) { Debug.LogException(e); t = new Thought { endTurn = true }; }
        } else t = job.Result;
        if (thinkingPrepares) { turnPrepared = true; if (map != null) map.ClearAnnotations(); }
        return true;
    }

    /// <summary>Drop any thought under way (it is not this AI's turn any more, or a board was loaded).</summary>
    void Abandon() {
        if (thinking == null) return;
        thinking = null;
        generation++;
    }

    /// <summary>
    /// Advance the AI by one action. Returns false when the turn is finished. The harness drives it
    /// flat out (each thought in line, on a copy of the board, exactly as the game's thread does).
    /// </summary>
    public bool StepOnce(bool headless) {
        if (player == null || player.eliminated) return false;
        while (true) {
            bool prepare = !turnPrepared;
            if (prepare) FogOfWar.Changed();
            bool refresh = refreshPending;
            refreshPending = false;
            BoardView v = NewView();
            Thought t;
            if (HarnessThreads) {
                Task<Thought> job = Task.Run(() => Think(v, prepare, refresh));
                while (!job.IsCompleted) { if (WhileThinking != null) WhileThinking(); }
                t = job.Result;
            } else t = Think(v, prepare, refresh);
            if (prepare) { turnPrepared = true; if (map != null) map.ClearAnnotations(); }
            Outcome o = Act(t, headless);
            if (o == Outcome.NotDone) continue;
            return o != Outcome.TurnOver && !GameController.gc.gameOver;
        }
    }

    /// <summary>
    /// One thought, on the copy of the board <paramref name="v"/> (any thread): prepare the turn if it
    /// is new, read the board again after the last action, then let the units in order decide until
    /// one has something to do.
    /// </summary>
    Thought Think(BoardView v, bool prepare, bool refresh) {
        // The supply solver's per-thread what-if settings are this thought's only (put back after: a thought redone on
        // the main thread must not leave them there).
        using (SupplyNetwork.AsPlayed()) return ThinkInner(v, prepare, refresh);
    }

    Thought ThinkInner(BoardView v, bool prepare, bool refresh) {
        using (v.Enter()) {
            if (prepare) PrepareTurnOnBoard();
            // Each thought may run on another thread: the turn's thread-static settings go with it.
            ApplyTurnStatics();
            if (refresh && map != null) {
                long __f = AIPerf.Begin();
                map.RefreshAfterAction();
                AIPerf.End("act: board re-read (RefreshAfterAction)", __f);
            }
            decide:
            while (queue.Count > 0) {
                Unit unit = queue[0];
                queue.RemoveAt(0);
                if (unit == null || unit.Strength <= 0 || unit.tile == null) continue;
                if (!unit.active) continue;
                long __d = AIPerf.Begin();
                AIAction action = DecideFor(unit);
                AIPerf.End("decide: " + (unit.Hub != null ? "supply unit" : Transporting.IsTransport(unit) && unit.Firepower <= 1 ? "transport"
                    : unit.locomotion == Locomotion.Jet ? "jet" : unit.locomotion == Locomotion.Rotor ? "helicopter" : "fighting unit"), __d);
                action = KeepLinks(action);
                action = SupplyStaysAboard(unit, action);
                // Never an attack that deals nothing (noZeroDamageAttacks): move to the tile it would fire from, or hold.
                if (Profile.noZeroDamageAttacks && (action.kind == AIActionKind.Attack || action.kind == AIActionKind.MoveAndAttack) && action.target != null && action.target.tile != null) {
                    HexTile from = action.kind == AIActionKind.MoveAndAttack && action.destination != null ? action.destination : unit.tile;
                    if (Unit.calculateDamage(unit, action.target, action.target.tile, 0) <= 0)
                        action = from != unit.tile ? AIAction.Move(unit, from, action.score, "no damage to " + action.target.name.Replace("(Clone)", "") + ": moves, does not attack")
                                                    : AIAction.Wait(unit, "no damage to " + action.target.name.Replace("(Clone)", "") + ": holds");
                }
                // No hopping on and off (noHopOff): a transport that has not moved this turn does not put down a rider that got
                // in this turn (a Grunt walked into a Recon and was put down beside it: a tile more than it could walk).
                if (Profile.noHopOff && action.kind == AIActionKind.Unload && action.unit != null && action.unit != action.target && action.target != null
                    && boardedThisTurn.Contains(action.target) && !transportMoved.Contains(action.unit) && turnStart.ContainsKey(action.unit) && turnStart[action.unit] == action.unit.tile
                    && (action.destination == null || action.destination == action.unit.tile))
                    action = AIAction.Wait(action.unit, "no hop off: " + action.target.name.Replace("(Clone)", "") + " got in this turn and the transport has not moved");
                // Aboard and nothing to do but ride: the turn is not spent. The unit is set aside,
                // still fresh, and decides again if its transport puts it down this turn (Run).
                if (Profile.freshUnload && unit.trasnporter != null && action.kind == AIActionKind.Wait) {
                    if (onDecision != null) onDecision(unit, action, false);
                    if (Trace != null) Trace("   " + player.personal_name + " " + unit.name.Replace("(Clone)", "") + ": aboard, decides again if put down — " + action.reason);
                    continue;
                }
                // Waiting (waitTwice): the first time, set aside to decide again once everything else has moved (what it
                // waits on — a supply unit, a rider, a gap — may have come about); waiting again, it skips its turn.
                if (Profile.waitTwice && action.kind == AIActionKind.Wait && unit.trasnporter == null && !waitedOnce.Contains(unit)) {
                    waitedOnce.Add(unit); SetAside(unit);
                    if (Trace != null) Trace("   " + player.personal_name + " " + unit.name.Replace("(Clone)", "") + D(unit.tile) + ": would wait (" + action.reason + "): decides again after the rest");
                    continue;
                }
                // Artillery placed first to fire (artilleryFireOrDefer) that would do anything else: set aside, it decides
                // again after the line has moved (it moved ahead of the line, onto ground the line had not yet taken).
                if (Profile.artilleryFireOrDefer && firesFirst.Contains(unit) && action.kind != AIActionKind.Attack && action.kind != AIActionKind.MoveAndAttack
                    && unit.trasnporter == null && !waitedOnce.Contains(unit)) {
                    waitedOnce.Add(unit); SetAside(unit);
                    if (Trace != null) Trace("   " + player.personal_name + " " + unit.name.Replace("(Clone)", "") + D(unit.tile) + ": artillery first to fire would " + action.kind + " (" + action.reason + "): decides again after the rest");
                    continue;
                }
                return new Thought { unit = unit, action = action };
            }
            // Everything else has moved: those that would have waited decide again.
            if (waitLater.Count > 0) {
                queue.AddRange(waitLater); waitLater.Clear();
                goto decide;
            }
            return new Thought { endTurn = true };
        }
    }

    /// <summary>
    /// A transport carrying supply units (profile.hubLinkPenalty) does not move, or put one down,
    /// where fewer of our supply units would be connected than now: it is kept where it is.
    /// Aboard, a supply unit links while its transport is within a move of supplied ground, so the
    /// root of a whole network could ride in a truck — and a truck that put it down one tile off
    /// supplied ground, or drove off with it, cut off every supply unit beyond it.
    /// Worked out on the thinking copy of the board (the transport moved there and put back).
    /// </summary>
    AIAction KeepLinks(AIAction a) {
        if (Profile.hubLinkPenalty <= 0f || a.unit == null || a.unit.tile == null) return a;
        if (a.kind != AIActionKind.Unload && a.kind != AIActionKind.Move && a.kind != AIActionKind.MoveAndAttack) return a;
        Unit tr = a.unit;
        bool carriesHub = false;
        foreach (Unit c in tr.transported) if (c != null && c.Hub != null && !c.Hub.producer) carriesHub = true;
        if (!carriesHub) return a;
        // Under the logistics only supply units with a duty are kept connected (AILogistics.duty): a tail
        // supplying nothing held a loaded truck where it stood for eight turns.
        if (logistics != null) {
            Dictionary<SupplyPoint, HexTile> mv = new Dictionary<SupplyPoint, HexTile>();
            HexTile dest = a.kind == AIActionKind.Unload ? (a.via ?? tr.tile) : a.destination;
            foreach (Unit c in tr.transported) if (c != null && c.Hub != null && dest != null) mv[c.Hub] = dest;
            if (a.kind == AIActionKind.Unload && a.target != null && a.target.Hub != null && a.destination != null) mv[a.target.Hub] = a.destination;
            // Those still aboard at the destination are linked as cargo is (within the truck's move of supplied
            // ground), not as if set down there: the chain's leap was planned that way.
            Dictionary<SupplyPoint, Unit> aboard = new Dictionary<SupplyPoint, Unit>();
            foreach (Unit c in tr.transported) if (c != null && c.Hub != null && !(a.kind == AIActionKind.Unload && a.target == c)) aboard[c.Hub] = tr;
            Dictionary<SupplyPoint, Unit> wasAboard = SupplyNetwork.PlannedAboard;
            SupplyNetwork.PlannedAboard = aboard;
            int lost;
            try { lost = AISupplyChain.Harm(mv, map); }
            finally { SupplyNetwork.PlannedAboard = wasAboard; }
            return lost <= 0 ? a : AIAction.Wait(tr, a.reason + "; held: that would cut off " + lost + " supply units with a duty");
        }
        int before = Connected(SupplyNetwork.SolveMoved(player, new Dictionary<SupplyPoint, HexTile>()));
        HexTile from = tr.tile;
        HexTile to = a.kind == AIActionKind.Unload ? (a.via ?? from) : a.destination;
        Dictionary<SupplyPoint, HexTile> moved = new Dictionary<SupplyPoint, HexTile>();
        if (a.kind == AIActionKind.Unload && a.target != null && a.target.Hub != null && a.destination != null) moved[a.target.Hub] = a.destination;
        int after;
        bool shift = to != null && to != from && to.occupier == null;
        if (shift) { from.occupier = null; to.occupier = tr; tr.tile = to; foreach (Unit c in tr.transported) if (c != null) c.tile = to; }
        try { after = Connected(SupplyNetwork.SolveMoved(player, moved)); }
        finally { if (shift) { to.occupier = null; tr.tile = from; from.occupier = tr; foreach (Unit c in tr.transported) if (c != null) c.tile = from; } }
        if (after >= before) return a;
        return AIAction.Wait(tr, a.reason + "; held: that would cut off " + (before - after) + " supply units");
    }

    int Connected(SupplyNetwork.Result r) {
        int n = 0;
        foreach (Unit u in player.units) if (u != null && u.Hub != null && !u.Hub.producer && u.tile != null && r.connectedHubs.Contains(u.Hub)) n++;
        return n;
    }

    /// <summary>Carry out a thought on the real board (main thread).</summary>
    Outcome Act(Thought t, bool headless) {
        if (t.endTurn) {
            using (NewView().Enter()) {
                // With the campaign's logistics, supply and transports are bought on the fronts' pressure (AIProduction).
                long __p = HexTactica.AI.AIPerf.Begin();
                if (logistics == null) BuySupply();
                if (Profile.buyUnits && map != null) {
                    SetReserveDemand();
                    AIProduction.GroundOf = Profile.seaLift ? (System.Func<Unit, HexTile>)GroundOf : null;
                    List<Unit> bought = AIProduction.Buy(player, map, Profile, Trace, logistics);
                    AIProduction.ReserveRally = null; AIProduction.ReserveShort = 0f; AIProduction.GroundOf = null;
                    if (AIProduction.ReserveBought != null) { foreach (Unit u in AIProduction.ReserveBought) if (u != null) HexTactica.AI.AICampaign.ReserveHeld.Add(u); AIProduction.ReserveBought.Clear(); }
                    if (onBuy != null) foreach (Unit u in bought) onBuy(u);
                }
                HexTactica.AI.AIPerf.End("production (main thread, end of turn)", __p);
            }
            EndTurn();
            return Outcome.TurnOver;
        }
        Unit unit = t.unit;
        if (unit == null || unit.Strength <= 0 || unit.tile == null) return Outcome.NotDone;
        GameController.gc.activeUnit = unit;
        // Following up on a unit still sliding back from a retreat (shooting it, or moving onto
        // the tile it is leaving): wait for the slide to show first. Anything else goes ahead.
        if (!headless && holdForRetreats && WaitsOnRetreat(t.action)) { held = t.action; held.unit = unit; holding = true; animationWait = 0f; return Outcome.Held; }
        bool did = Run(unit, t.action, headless);
        if (GameController.gc.gameOver) return Outcome.TurnOver;
        return did ? Outcome.Did : Outcome.NotDone;
    }

    /// <summary>An action decided but held until a retreat it follows up on has played.</summary>
    AIAction held;
    bool holding;

    /// <summary>
    /// Does <paramref name="a"/> follow up on a retreat still playing on screen: fire at a unit
    /// that is sliding back, or move onto (or through the end of) the tile it is leaving?
    /// </summary>
    static bool WaitsOnRetreat(AIAction a) {
        if (a.target != null && a.target.RetreatPlaying) return true;
        if (a.destination != null && UnitController.uc != null)
            foreach (Unit x in UnitController.uc.units)
                if (x != null && x.RetreatPlaying && (x.retreatFrom == a.destination || x.tile == a.destination)) return true;
        return false;
    }

    /// <summary>Carry out one decided action, with the bookkeeping after it. True if it did something.</summary>
    bool Run(Unit unit, AIAction action, bool headless) {
        {
            long __x = AIPerf.Begin();
            HexTile wasAt = unit.tile;
            bool did = HexTactica.AI.AIExecutor.Execute(action, map, headless);
            // A rider that got off by itself (its transport had already moved): getting off costs nothing, it still acts.
            bool selfDrop = did && action.kind == AIActionKind.Unload && action.target == unit && action.unit != unit;
            AIPerf.End("act: execute", __x);
            // Cargo put down that has not acted this turn: it decides next, from where it landed
            // (getting off costs nothing: it moves, attacks or captures as any fresh unit would).
            // Put down this turn: it does not get back in this turn (it was put down to strike).
            if (did && action.kind == AIActionKind.Unload && action.target != null) putDownThisTurn.Add(action.target);
            if (did && action.kind == AIActionKind.Load && action.unit != null) {
                boardedThisTurn.Add(action.unit);
                // Into a supply transport's spare seat: the ride it got in for, as planned now (kept for its supply unit later).
                Unit into = action.transport;
                if (into != null && action.unit.Hub == null && into.transported.Exists(c => c != null && c.Hub != null)) {
                    HexTile to; bool has = SupplyGoing().TryGetValue(into, out to) && to != null;
                    if (has) riderRide[into] = to;
                    if (Trace != null) Trace("   rider into supply transport at (" + into.tile.x + "," + into.tile.y + "): planned ride " + (has ? "(" + to.x + "," + to.y + ")" : "none"));
                }
            }
            if (did && (action.kind == AIActionKind.Move || action.kind == AIActionKind.MoveAndAttack || action.kind == AIActionKind.Unload) && action.unit != null && Transporting.IsTransport(action.unit) && action.unit.tile != null && turnStart.ContainsKey(action.unit) && turnStart[action.unit] != action.unit.tile) transportMoved.Add(action.unit);
            // A transport with riders has acted: its riders decide next (they get off to strike, at the front line, or ride on).
            if (Profile.assaultRides && did && action.unit != null && Transporting.IsTransport(action.unit) && action.unit.tile != null) {
                List<Unit> still = AIAssault.Riders(action.unit);
                for (int i = still.Count - 1; i >= 0; i--) {
                    Unit r = still[i];
                    if (r == null || !r.active || r == action.target) continue;
                    queue.Remove(r); queue.Insert(0, r);
                }
            }
            if (Profile.freshUnload && did && action.kind == AIActionKind.Unload && action.target != null && action.target.active
                && action.target.trasnporter == null && action.target.tile != null
                && (!queue.Contains(action.target) || Profile.assaultRides)) {
                // (Already further down the queue, as riders are: it is moved up, so nothing acts between its landing and its move.)
                queue.Remove(action.target);
                queue.Insert(0, action.target);
            }
            if (did) {
                // Any action can change what every other unit can reach, where supply
                // flows, and who the enemy can fire on: a tile was vacated or filled, a unit
                // died in a corridor, a hub moved. Conflict and distress are recomputed too.
                long __f = AIPerf.Begin();
                if (selfDrop) { GameController.gc.activeUnit = null; FogOfWar.Refresh(); GameController.RecalculateThreat(); }
                else unit.FinishActive();
                AIPerf.End("act: FinishActive (threat, resupply)", __f);
                // The next thought reads the board again first (on its own copy).
                refreshPending = true;
                // The game's supply borders and alarms read the published supply: the AI's thoughts
                // publish only into their copies, so publish the real fields after its action.
                // (FinishActive already does so in play; a rider that got off by itself did not go through it.)
                if (!headless && selfDrop && GameController.gc != null) foreach (Player p in GameController.gc.players) if (p != null) p.Resupply();
            }

            Annotate(unit, action);
            if (onDecision != null) onDecision(unit, action, did);
            if (Trace != null)
                Trace("   " + player.personal_name + " " + unit.name.Replace("(Clone)", "") + (wasAt != null ? " (" + wasAt.x + "," + wasAt.y + ")" + (unit.tile != null && unit.tile != wasAt ? "->(" + unit.tile.x + "," + unit.tile.y + ")" : "") : "") + ": " + action.kind + (did ? "" : " (not executed)") + " — " + action.reason);

            // A unit that waited changed nothing, so there is nothing to recalculate.
            // (A rider that got off by itself has not spent its turn: getting off costs nothing, and it acts next.)
            if (!selfDrop) unit.active = false;
            if (!did) GameController.gc.activeUnit = null;
            return did;
        }
    }

    /// <summary>The supply chain as planned at the start of this turn (profile.supplyChain).</summary>
    AISupplyChain.Plan chainPlan;
    AISupplyTree.Plan treePlan;
    public AISupplyTree.Plan CurrentTree { get { return treePlan; } }
    public AISupplyChain.Plan CurrentChain { get { return chainPlan; } }
    /// <summary>Called when the AI buys a unit: the unit and where.</summary>
    public System.Action<Unit> onBuy;

    /// <summary>
    /// profile.buySupply: at the end of the turn, if supply was stretched this turn (the tree: every
    /// supply unit has a link and fighting units are still out of supply; the chain: no link could
    /// move up, and none was waiting on the army), buy a supply unit at our HQ when we can afford
    /// one — never on a tile reserved for a landing.
    /// </summary>
    void BuySupply() {
        bool stretched = Profile.supplyTree ? treePlan != null && treePlan.short_ : chainPlan != null && chainPlan.short_;
        if (!Profile.buySupply || !stretched || map == null) return;
        Building hq = GameController.HQOf(player);
        if (hq == null || BuildingController.bc == null || hq.buildable == null) return;
        Unit pick = null;
        foreach (Unit b in hq.Offered(player)) {
            if (b == null) continue;
            SupplyPoint sp = b.Hub;
            if (sp == null || sp.producer || LocomotionRules.NeedsBase(b.locomotion) || !hq.CanBuild(b)) continue;
            if (pick == null || b.cost < pick.cost) pick = b;
        }
        if (pick == null || player.money < pick.cost) { if (Trace != null) Trace("   no supply bought: " + (pick == null ? "the HQ builds none" : "money " + player.money + " of " + pick.cost)); return; }
        AIMap m = map;
        Unit made;
        using (BoardView.Outside()) made = BuildingController.bc.Produce(hq, pick, t => AIAir.MayBuildOn(t, m));
        if (made != null && made.tile != null) made.tile.occupier = made;
        if (made == null) { if (Trace != null) Trace("   no supply bought: no free tile at the HQ"); return; }
        if (onBuy != null) onBuy(made);
        if (Trace != null) Trace("   " + player.personal_name + " buys " + pick.name.Replace("(Clone)", "") + " at the HQ: the supply chain is stretched (" + (Profile.supplyTree ? treePlan.ToString() : chainPlan.ToString()) + ")");
    }

    /// <summary>Forget the turn in progress and everything remembered between turns: a new board was loaded.</summary>
    public void ResetTurn() {
        Abandon();
        refreshPending = false;
        turnPrepared = false;
        holding = false;
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

    /// <summary>
    /// Fog of war, playing by it (AIProfile.fogFair): every decision is made on the board as this
    /// side knows it — enemies it does not see are off the board, those seen lately stand where they
    /// were last seen (FogOfWar.Conceal). Actions are carried out on the real board, outside it.
    /// </summary>
    /// <summary>
    /// The board this AI thinks on, copied now (main thread): as this side knows it under fog of war
    /// (AIProfile.fogFair), else as it is. Its simulations change only the copy.
    /// </summary>
    /// <summary>Fighting units an empty transport has set out to fetch this turn (AIAssault.Fetch): one transport each.</summary>
    readonly HashSet<Unit> soughtRiders = new HashSet<Unit>();
    /// <summary>This turn's transports earmarked for supply (AIAssault.SupplyTransports).</summary>
    HashSet<Unit> supplyTransports = new HashSet<Unit>();
    /// <summary>Units that would have waited this turn, and those set aside to decide again after the rest (waitTwice).</summary>
    readonly HashSet<Unit> waitedOnce = new HashSet<Unit>();
    readonly List<Unit> waitLater = new List<Unit>();
    /// <summary>Artillery with a target in range when the turn's order was made (artilleryFireOrDefer).</summary>
    readonly HashSet<Unit> firesFirst = new HashSet<Unit>();
    /// <summary>Units put down from a transport this turn: they do not get back in until next turn.</summary>
    readonly HashSet<Unit> putDownThisTurn = new HashSet<Unit>();
    /// <summary>noHopOff: riders that got in this turn, transports that have moved this turn, and where each unit began it.</summary>
    readonly HashSet<Unit> boardedThisTurn = new HashSet<Unit>(), transportMoved = new HashSet<Unit>();
    /// <summary>supplySpareSeat: where each supply transport was to go when a rider got into its spare seat this turn.</summary>
    readonly Dictionary<Unit, HexTile> riderRide = new Dictionary<Unit, HexTile>();
    /// <summary>The goal each rider got in for this turn (AIAssault.Board's ground): judged against it when let out.</summary>
    readonly Dictionary<Unit, HexTile> riderGoal = new Dictionary<Unit, HexTile>();
    readonly Dictionary<Unit, HexTile> turnStart = new Dictionary<Unit, HexTile>();

    /// <summary>Is <paramref name="t"/> one of the campaign's supply trucks this turn (a chain truck, or a ride)?</summary>
    /// <summary>
    /// Riding to the assault, the turn order (assaultOrderWithLine): the coordinated plans as they are; then artillery
    /// with something in range and the fighting units other than infantry with something in reach (tanks, gunships: they
    /// take less back), in their order; then each transport with riders, its riders straight after it; then the rest.
    /// </summary>
    void OrderWithLine() {
        int lead = 0;
        while (lead < queue.Count && planned.ContainsKey(queue[lead])) lead++;
        List<Unit> pre = new List<Unit>(), loaded = new List<Unit>(), rest = new List<Unit>();
        HashSet<Unit> riding = new HashSet<Unit>();
        for (int i = lead; i < queue.Count; i++) { Unit u = queue[i]; if (Transporting.IsTransport(u) && AIAssault.Riders(u).Count > 0) { loaded.Add(u); foreach (Unit r in AIAssault.Riders(u)) riding.Add(r); } }
        for (int i = lead; i < queue.Count; i++) {
            Unit u = queue[i];
            if (loaded.Contains(u) || riding.Contains(u)) continue;
            bool firesFirst = u.Hub == null && u.Firepower > 0 && !Transporting.IsTransport(u)
                && (AIRoles.Classify(u, Profile) == AIRole.Artillery ? AIRoles.HasTargetInRange(u) : u.locomotion != Locomotion.Boots && InReach(u));
            (firesFirst ? pre : rest).Add(u);
        }
        List<Unit> order = new List<Unit>(queue.GetRange(0, lead));
        order.AddRange(pre);
        foreach (Unit t in loaded) { order.Add(t); foreach (Unit r in AIAssault.Riders(t)) if (queue.Contains(r)) order.Add(r); }
        order.AddRange(rest);
        queue.Clear(); queue.AddRange(order);
    }

    /// <summary>The units that move after everyone on foot this turn (TurnOrder steps 4 to 7): a unit set aside to decide again goes back in before them.</summary>
    readonly HashSet<Unit> afterFoot = new HashSet<Unit>();

    /// <summary>
    /// The turn order (AIProfile.turnOrderSteps), each step keeping the order the units already had (coordinated plans
    /// first, the supply chain's order):
    ///   1. artillery that can fire (one that then would not is put back in at the end of 3);
    ///   2. full transports;
    ///   3. the riders of those, every other unit on foot, artillery with nothing in range, and supply units that are not
    ///      links of the logistics chain;
    ///   4. transports with room that carry someone: everyone who gets in this turn is in before they go;
    ///   5. the chain's supply units; then the transports chosen for supply (one a supply unit rides, its chain truck, the
    ///      ride its plan takes, the ship it is to cross in), empty or not, once every supply unit has decided;
    ///   then empty transports nobody chose: after 5, whose supply units choose their transport only as they decide;
    ///   6. the riders of the transports that moved in 4: out if they can strike, reach their goal, or are safer off;
    ///   7. aircraft landing this turn.
    /// (8, buying, is at the end of the turn.)
    /// </summary>
    void TurnOrder() {
        List<Unit> landing = new List<Unit>();
        foreach (AIAir.Landing l in map.landings.Values) if (l.turn == 1 && queue.Contains(l.unit)) landing.Add(l.unit);
        landing.Sort(AIMap.CompareUnits);
        HashSet<SupplyPoint> chain = new HashSet<SupplyPoint>();
        if (logistics != null) foreach (SupplyPoint h in logistics.chainOf.Keys) chain.Add(h);
        if (treePlan != null) foreach (SupplyPoint h in treePlan.links.Keys) chain.Add(h);
        if (chainPlan != null) foreach (SupplyPoint h in chainPlan.links.Keys) chain.Add(h);
        // The transport each supply unit has chosen: the one it is aboard, its chain truck, the ship it is to cross in.
        Dictionary<Unit, Unit> truckOf = new Dictionary<Unit, Unit>();   // supply unit -> transport
        HashSet<Unit> forSupply = new HashSet<Unit>();
        foreach (Unit u in queue) {
            SupplyPoint h = u.Hub;
            if (h == null || h.producer) continue;
            Unit t = null;
            HexTactica.AI.AILogistics.Ride ride;
            if (u.trasnporter != null && !AIAssault.SeaLiftShip(u.trasnporter, map)) t = u.trasnporter;
            else if (logistics != null && logistics.chainTrucks.TryGetValue(h, out t) && t != null) { }
            else if (logistics != null && logistics.rides.TryGetValue(h, out ride) && ride.truck != null) t = ride.truck;
            else if (u.trasnporter == null && ghostRides.TryGetValue(u, out t) && t != null) { }
            else if (u.trasnporter == null && Profile.seaLift) t = SeaLiftShipFor(u);
            if (t != null && queue.Contains(t) && !forSupply.Contains(t)) { truckOf[u] = t; forSupply.Add(t); }
        }
        // Chosen for supply: riders may take only a seat the supply unit leaves free (AIAssault.Board, SpareSlot).
        foreach (Unit t in forSupply) if (!AIAssault.SeaLiftShip(t, map)) supplyTransports.Add(t);
        // Others chosen for supply this turn without a supply unit matched to them yet: with the chain, at the end of 5.
        List<Unit> supplyTrucks = new List<Unit>();
        foreach (Unit t in queue)
            if (Transporting.IsTransport(t) && t.Firepower <= 1 && !forSupply.Contains(t) && !AIAssault.SeaLiftShip(t, map) && (supplyTransports.Contains(t) || IsLogisticsTruck(t) || AIAssault.CarriesHub(t))) supplyTrucks.Add(t);

        List<Unit> s1 = new List<Unit>(), s2 = new List<Unit>(), s3 = new List<Unit>(), s4 = new List<Unit>(), s5 = new List<Unit>(), s6 = new List<Unit>();
        HashSet<Unit> placed = new HashSet<Unit>(landing);
        foreach (Unit t in supplyTrucks) placed.Add(t);
        foreach (Unit t in forSupply) placed.Add(t);
        System.Func<Unit, bool> carrier = t => Transporting.IsTransport(t) && t.Firepower <= 1;
        // 2 and 4: transports not chosen for supply, by whether they are full.
        foreach (Unit t in queue) {
            if (placed.Contains(t) || !carrier(t) || t.trasnporter != null) continue;
            (Transporting.FreeWeight(t) <= 0 ? s2 : s4).Add(t); placed.Add(t);
        }
        // Riders: of a full transport in 3 (first), of one with room in 6, of a supply transport straight after it.
        List<Unit> riders3 = new List<Unit>();
        foreach (Unit u in queue) {
            if (placed.Contains(u) || u.trasnporter == null) continue;
            if (s2.Contains(u.trasnporter)) { riders3.Add(u); placed.Add(u); }
            else if (s4.Contains(u.trasnporter)) { s6.Add(u); placed.Add(u); }
        }
        // 1: artillery that can fire, on foot.
        foreach (Unit u in queue) if (!placed.Contains(u) && u.trasnporter == null && firesFirst.Contains(u)) { s1.Add(u); placed.Add(u); }
        // 3 and 5: the rest. Supply units: in 3 if they are not links of the chain, else in 5; the transports chosen for
        // supply (and their riders) after every supply unit has decided — after its own, and while another may still take
        // one (a supply unit's choice is made only as it decides: the plan at the start of the turn is a guess).
        List<Unit> s5t = new List<Unit>();
        System.Action<Unit> truckAfter = t => {
            if (s5t.Contains(t)) return;
            s5t.Add(t);
            foreach (Unit r in queue) if (r.trasnporter == t && !placed.Contains(r)) { s5t.Add(r); placed.Add(r); }
        };
        s3.AddRange(riders3);
        foreach (Unit u in queue) {
            if (placed.Contains(u)) continue;
            SupplyPoint h = u.Hub;
            if (h != null && !h.producer && chain.Contains(h)) continue;   // 5
            s3.Add(u); placed.Add(u);
        }
        foreach (Unit u in queue) if (!placed.Contains(u) && u.Hub != null && !u.Hub.producer) { s5.Add(u); placed.Add(u); }
        foreach (Unit u in queue) { Unit t; if (truckOf.TryGetValue(u, out t)) truckAfter(t); }
        foreach (Unit t in supplyTrucks) truckAfter(t);
        s5.AddRange(s5t);
        foreach (Unit u in queue) if (!placed.Contains(u)) { s3.Add(u); placed.Add(u); }   // anything left over: with everyone
        // Supply ships first among those in 3: ships coming to them to be restocked go to where they end up (AIAir.Refit).
        List<Unit> tenders = s3.FindAll(Player.IsSupplyShip);
        foreach (Unit t in tenders) s3.Remove(t);
        s3.InsertRange(0, tenders);

        // Empty transports nobody has chosen yet: after the chain's supply units (5), which choose their transport only when
        // they decide (one gone at 4 could not carry them). Anyone who gets into one in 3 rides it from there.
        List<Unit> empties = s4.FindAll(t => t.transported.Count == 0);
        foreach (Unit t in empties) s4.Remove(t);
        List<Unit> order = new List<Unit>();
        order.AddRange(s1); order.AddRange(s2); order.AddRange(s3); order.AddRange(s4); order.AddRange(s5); order.AddRange(empties); order.AddRange(s6); order.AddRange(landing);
        afterFoot.Clear();
        foreach (Unit u in s4) afterFoot.Add(u); foreach (Unit u in s5) afterFoot.Add(u); foreach (Unit u in empties) afterFoot.Add(u); foreach (Unit u in s6) afterFoot.Add(u); foreach (Unit u in landing) afterFoot.Add(u);
        queue.Clear(); queue.AddRange(order);
        if (Trace != null) Trace("   turn order by steps: 1 [" + Names(s1) + "] 2 [" + Names(s2) + "] 3 [" + Names(s3) + "] 4 [" + Names(s4) + "] 5 [" + Names(s5) + "] empty [" + Names(empties) + "] 6 [" + Names(s6) + "] 7 [" + Names(landing) + "]");
    }

    static string Names(List<Unit> l) { return string.Join(", ", l.Select(u => u.name.Replace("(Clone)", "")).ToArray()); }

    /// <summary>A unit set aside to decide again (waitTwice, artilleryFireOrDefer): under the turn order (turnOrderSteps), back in before the transports with room (step 4), else after everyone.</summary>
    void SetAside(Unit unit) {
        if (Profile.turnOrderSteps) {
            int at = queue.FindIndex(x => afterFoot.Contains(x));
            if (at >= 0) { queue.Insert(at, unit); return; }
        }
        waitLater.Add(unit);
    }

    /// <summary>An enemy within <paramref name="u"/>'s move and fire, as the crow flies.</summary>
    bool InReach(Unit u) {
        int reach = (u.moveAndAttack ? u.moveRange : 0) + Mathf.Max(1, u.AttackRange);
        foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && u.tile.Distance(e.tile) <= reach && u.CanTarget(e)) return true;
        return false;
    }

    /// <summary>Where each transport on a supply ride this turn is taking its supply unit (the logistics' rides and the chain links' places).</summary>
    Dictionary<Unit, HexTile> SupplyGoing() {
        Dictionary<Unit, HexTile> d = new Dictionary<Unit, HexTile>();
        if (logistics == null) return d;
        foreach (KeyValuePair<SupplyPoint, HexTactica.AI.AILogistics.Ride> kv in RidesAlong()) if (kv.Value.truck != null && kv.Value.to != null) d[kv.Value.truck] = kv.Value.to;
        return d;
    }

    bool IsLogisticsTruck(Unit t) {
        if (logistics == null || t == null) return false;
        foreach (Unit x in logistics.chainTrucks.Values) if (x == t) return true;
        foreach (HexTactica.AI.AILogistics.Ride r in logistics.rides.Values) if (r.truck == t) return true;
        return false;
    }

    /// <summary>The nearest enemy unit we know of, from <paramref name="u"/>.</summary>
    HexTile NearestEnemyTile(Unit u) {
        HexTile best = null; int bd = int.MaxValue;
        foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null) { int d = u.tile.Distance(e.tile); if (d < bd) { bd = d; best = e.tile; } }
        return best;
    }

    /// <summary>
    /// A supply unit aboard a transport is not put down (AIProfile.supplyStaysAboard): an unloading of one becomes the
    /// transport's move without it (or a wait), unless the transport would be destroyed where it stops. A transport
    /// carried by another (with a supply unit inside) may still be put down: only supply units themselves stay in.
    /// </summary>
    AIAction SupplyStaysAboard(Unit deciding, AIAction a) {
        if (!Profile.supplyStaysAboard || a.kind != AIActionKind.Unload || a.target == null || a.target.Hub == null || a.target.Hub.producer) return a;
        Unit transport = a.unit;
        if (transport == null || transport.tile == null) return a;
        HexTile stop = a.via ?? transport.tile;
        if (map != null && map.ExpectedLoss(transport, stop) >= transport.Strength - 0.01f) return a;   // it would go down with its transport
        string why = "supply stays aboard (was: " + a.reason + ")";
        if (a.via != null && a.via != transport.tile && deciding == transport) return AIAction.Move(transport, a.via, a.score, why);
        return AIAction.Wait(deciding, why);
    }

    BoardView NewView() {
        long __v = AIPerf.Begin();
        BoardView v = FogOfWar.Conceal(player, true, Profile.fogMemoryTurns) ?? BoardView.Snapshot();
        AIPerf.End("main thread: copy the board", __v);
        return v;
    }

    // ------------------------------------------------------------------ speculation

    /// <summary>
    /// Speculation (profile.speculate): while the opponent plays, once it has moved most of its units
    /// (speculateAfterShare) and the board has stood still a moment (speculateQuietFrames), the AI reads the board as it
    /// stands on a worker thread at low priority and warms its map's caches (every enemy's reach, every unit's move field
    /// and space, the supply networks). Its turn's map takes them over and keeps only what still stands once its turn
    /// has started (AIMapCache: the same answers as working everything out then). The real board is never written; the
    /// main thread only copies the board (as a thought does, well under a millisecond); a speculation still running when
    /// the turn comes is dropped, nothing waits for it; a newer still board starts a newer one.
    /// </summary>
    void MaybeSpeculate() {
        if (!Profile.speculate || !Profile.keepCaches) return;
        GameController gc = GameController.gc;
        Player opponent = gc != null ? gc.activePlayer : null;
        if (opponent == null || opponent == player) return;
        // The board as it stands: if anything moved, wait for it to be still.
        long mark = 17; int units = 0, acted = 0;
        foreach (Unit u in UnitController.uc.units) {
            if (u == null || u.Strength <= 0) continue;
            mark = mark * 31 + ((u.tile != null ? u.tile.GetHashCode() : 7) ^ (u.Strength * 131));
            if (u.owner == opponent && u.tile != null) { units++; if (!u.active) acted++; }
        }
        if (mark != stillMark) { stillMark = mark; stillSince = Time.frameCount; return; }
        if (Time.frameCount - stillSince < Profile.speculateQuietFrames) return;
        if (units > 0 && acted < Profile.speculateAfterShare * units) return;
        if (speculating != null && !speculating.IsCompleted) return;
        if (speculatedMark == mark) return;
        StartSpeculation(mark, false);
    }

    void StartSpeculation(long mark, bool inLine) {
        BoardView v = NewView();
        AIMap carry = map;
        int generation = ++speculationGeneration;
        Action work = () => {
            using (v.Enter()) {
                AIMap m = new AIMap(player, Profile, carry, true);
                m.Warm();
                lock (speculationLock) if (generation == speculationGeneration) { speculation = m; speculatedMark = mark; }
            }
        };
        if (inLine) { work(); return; }
        speculating = Task.Run(() => {
            System.Threading.Thread t = System.Threading.Thread.CurrentThread;
            System.Threading.ThreadPriority was = t.Priority;
            try { t.Priority = System.Threading.ThreadPriority.BelowNormal; work(); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("AI speculation dropped: " + e.Message); }
            finally { t.Priority = was; }
        });
    }

    /// <summary>The finished speculation for this turn, if any (taken once); one still running is dropped.</summary>
    AIMap TakeSpeculation() {
        lock (speculationLock) {
            AIMap m = speculation;
            speculation = null;
            speculationGeneration++;   // a speculation still running finds itself out of date and is not kept
            speculatedMark = 0;
            if (m != null) speculationsUsed++;
            return m;
        }
    }

    /// <summary>Harness: speculate now, in line, on the board as it stands (the opponent's turn is ending).</summary>
    public void SpeculateNow() { if (Profile.speculate && Profile.keepCaches) StartSpeculation(1, true); }

    readonly object speculationLock = new object();
    AIMap speculation;
    Task speculating;
    int speculationGeneration, stillSince;
    long stillMark, speculatedMark;
    /// <summary>For the harness: speculations taken over by a turn.</summary>
    public static int speculationsUsed;

    void PrepareTurnOnBoard() {
        { long __p = AIPerf.Begin();
        map = new AIMap(player, Profile, TakeSpeculation() ?? map);
        AIPerf.End("prepare: board read (AIMap)", __p); }
        // What the fog may hide, as this turn prices it (AISigint): every enemy unit lost from sight, where it could be.
        if (Trace != null && map.sigint != null) foreach (string line in map.sigint.Describe()) Trace("   fog: " + line);
        // How well we know the enemy, and how bold that lets us be (AIProfile.campaignKnowledge).
        bool knows = Profile.campaign && Profile.campaignKnowledge;
        if (knows || Profile.fogUncertainty) {
            knowledge.Update(map, Profile);
            if (knows) map.knowledge = knowledge;
            // How much of the enemy we cannot place: the fog's unknown ground is feared by that share (AISigint).
            if (Profile.fogUncertainty) map.fogUncertainty = knowledge.estimate > 0f ? Mathf.Clamp01(1f - knowledge.known / knowledge.estimate) : 0f;
            map.fogUnplacedPower = Mathf.Max(0f, knowledge.estimate - knowledge.known);
            map.fogKnownAtStart = new HashSet<Unit>(map.enemyUnits);
            if (map.sigint != null) { string note = map.sigint.Prime(); if (Trace != null && note != null) Trace("   fog: " + note); }
            if (Trace != null) Trace("   " + knowledge + (Profile.fogUncertainty ? "; unknown ground feared at " + map.fogUncertainty.ToString("0.00") : ""));
        }
        // What each unit does for its side (forecastRoleWorth), for this turn's forecasts.
        roleWorth = Profile.forecastRoleWorth ? AIWorld.RoleWorth(map, Profile) : null;
        AIWorld.Roles = roleWorth;

        // Forget the intentions of units that no longer exist.
        List<Unit> stale = new List<Unit>();
        foreach (KeyValuePair<Unit, HexTile> kv in intents) {
            if (kv.Key == null || kv.Key.Strength <= 0 || kv.Key.tile == null) stale.Add(kv.Key);
        }
        for (int i = 0; i < stale.Count; i++) intents.Remove(stale[i]);
        { long __p = AIPerf.Begin();
        if (Profile.campaign) {
            campaign = AICampaign.Plan(map, Profile, campaign);
            fronts = campaign.Fronts(map, Profile, postureMemory, assaultMemory);
            if (Trace != null) foreach (string line in campaign.Describe()) Trace("   " + line);
        } else {
            campaign = null;
            fronts = AIStrategic.BuildFronts(map, Profile, postureMemory, assaultMemory);
        }
        // In the fight (AIProfile.campaignReleaseTurns): a unit that close to the enemy is on no task force — it
        // fights as the ordinary AI would, on the ordinary front it falls in, whatever front it was given.
        releasedFronts = null;
        if (campaign != null && Profile.campaignReleaseTurns > 0) {
            HashSet<Unit> was = AIStrategic.Exclude;
            AIStrategic.Exclude = null;
            try { releasedFronts = AIStrategic.BuildFronts(map, Profile, releasedMemory, assaultMemory); } finally { AIStrategic.Exclude = was; }
        }
        AIPerf.End("prepare: fronts", __p); }
        if (Trace != null) foreach (Front f in fronts)
            Trace("   front " + f + (f.objectiveTile != null ? " objective (" + f.objectiveTile.x + "," + f.objectiveTile.y + ") value " + f.objectiveValue.ToString("0") : "")
                  + (f.staging && f.stagingTile != null ? " staging at (" + f.stagingTile.x + "," + f.stagingTile.y + ")" : "") + ", " + f.assigned.Count + " units");
        // The supply backbone as the turn starts (for infantry to ride along with it); planned again
        // for the supply units themselves once the army has moved.
        backbone = null; backboneTurn = null; backbonePlanned = null;
        // Front first (supplyFrontFirst): for each group of the army, the supply unit nearest it.
        frontHubs.Clear();
        if (Profile.supplyFrontFirst)
            foreach (List<Unit> g in AISupplyBackbone.Groups(map)) {
                SupplyPoint best = null; int bd = int.MaxValue;
                foreach (SupplyPoint h in map.ownHubs) {
                    if (h == null || h.producer || h.myUnit == null || h.myUnit.tile == null || frontHubs.Contains(h)) continue;
                    if (LocomotionRules.IsNaval(h.myUnit.locomotion) || LocomotionRules.IsAirborne(h.myUnit.locomotion)) continue;
                    foreach (Unit u in g) { int d = map.Travel(h.myUnit, h.myUnit.tile, u.tile); if (d < bd || (d == bd && best != null && AIMap.CompareUnits(h.myUnit, best.myUnit) < 0)) { bd = d; best = h; } }
                }
                if (best != null && bd < 99) frontHubs.Add(best);
            }
        if (Profile.supplyBackbone) backbonePlanned = AISupplyBackbone.Make(map, null);
        HexTactica.AI.AILogistics.PreviousChainTrucks = lastChainTrucks;
        logistics = campaign != null && Profile.campaignLogistics && !Profile.supplyFrontFirst ? HexTactica.AI.AILogistics.Make(map, campaign, null, fronts) : null;
        HexTactica.AI.AILogistics.PreviousChainTrucks = null;
        lastChainTrucks = logistics != null ? new Dictionary<SupplyPoint, Unit>(logistics.chainTrucks) : null;
        AITransportPlanner.ChainLinks = logistics != null ? new HashSet<SupplyPoint>(logistics.chainOf.Keys) : null;
        relieved.Clear(); sought.Clear();
        if (logistics != null && Trace != null) Trace("   " + logistics);
        if (campaign != null && campaign.reserveNote != null && Trace != null) Trace("   " + campaign.reserveNote);
        HexTactica.AI.AILogistics.Protect = logistics != null ? logistics.dutied : null;
        AISupplyChain.ProtectOnly = HexTactica.AI.AILogistics.Protect;
        HexTactica.AI.AILogistics.ProtectUnitsNow = logistics != null ? logistics.protectUnits : null;
        AISupplyChain.ProtectUnits = HexTactica.AI.AILogistics.ProtectUnitsNow;
        { long __p = AIPerf.Begin();
        objectives = AIObjectives.Build(map, Profile);
        AIPerf.End("prepare: objectives", __p); }
        // A unit on a campaign task force is its front's: the ordinary objectives do not take it.
        HashSet<Unit> onTaskForce = new HashSet<Unit>();
        if (campaign != null) foreach (CampaignObjective o in campaign.objectives) foreach (Unit u in o.units) if (u != null && !Released(u)) onTaskForce.Add(u);
        AIObjectives.Exclude = onTaskForce;
        try { assignments = Profile.useObjectives ? AIObjectives.Assign(objectives, map, Profile) : new Dictionary<Unit, Objective>(); }
        finally { AIObjectives.Exclude = null; }
        if (campaign != null) {
            // The campaign's scouts are pulled to the ground they are to look at.
            foreach (CampaignObjective o in campaign.objectives)
                if (o.kind == CampaignKind.Scout) foreach (Unit u in o.units) assignments[u] = new Objective { kind = ObjectiveKind.Scout, tile = o.target, value = o.value, committed = true };
        } else AIObjectives.AssignScouts(assignments, map, Profile);
        if (Trace != null) foreach (KeyValuePair<Unit, Objective> kv in assignments)
            if (kv.Value.kind == ObjectiveKind.Scout) Trace("   scout " + kv.Key.name.Replace("(Clone)", "") + " (" + kv.Key.tile.x + "," + kv.Key.tile.y + ") -> (" + kv.Value.tile.x + "," + kv.Value.tile.y + ") sees " + kv.Value.value.ToString("0.0"));
        objectivePulls += assignments.Count;
        turnsPlayed++;
        // Objectives are structure: they add a pull to units out of contact and nothing else.
        map.SetCommitted(AIStrategic.AssaultUnits(fronts));
        map.SetAssaultObjectives(AIStrategic.AssaultObjectives(fronts));
        map.intents = intents;
        transportAssignments.Clear();
        reservedTransports.Clear();
        soughtRiders.Clear();
        putDownThisTurn.Clear();
        boardedThisTurn.Clear(); transportMoved.Clear(); turnStart.Clear(); riderRide.Clear(); riderGoal.Clear();
        foreach (Unit u in player.units) if (u != null && u.tile != null) turnStart[u] = u.tile;
        waitedOnce.Clear(); waitLater.Clear();
        AIAssault.Planned.Clear();
        AIAssault.FellBack.Clear();
        // The trucks the logistics claimed for the fronts' own supply units: theirs before any fighting
        // unit picks a ride (a fighting unit may ride along, below).
        if (logistics != null) foreach (HexTactica.AI.AILogistics.Ride r in logistics.rides.Values) reservedTransports.Add(r.truck);
        // And the chain links' trucks: each kept for its link (AILogistics.chainTrucks).
        if (logistics != null) foreach (Unit tr in logistics.chainTrucks.Values) reservedTransports.Add(tr);
        // Transports earmarked for supply this turn (by what they carry, AIAssault.SupplyTransports): supply first, troops after.
        supplyTransports = Profile.assaultRides ? AIAssault.SupplyTransports(player, map, reservedTransports) : new HashSet<Unit>();
        if (Trace != null && Profile.assaultRides) {
            List<string> st = new List<string>(); foreach (Unit t in supplyTransports) st.Add(t.name.Replace("(Clone)", "") + D(t.tile));
            st.Sort(string.CompareOrdinal);
            Trace("   transports for supply: " + (st.Count == 0 ? "none" : string.Join(", ", st.ToArray())) + (AIAssault.NoBuildMap() ? " (nothing is built on this map: every one that can carry supply)" : ""));
        }

        // Units riding a transport stay in the queue: a hub aboard is the one that tells
        // its transport where to go. Excluding them meant a loaded hub was never planned
        // for, so its transport had no orders and the cargo was never delivered.
        List<Unit> actionable = new List<Unit>();
        for (int i = 0; i < map.ownUnits.Count; i++) {
            Unit u = map.ownUnits[i];
            if (u != null && u.active) actionable.Add(u);
        }

        { long __p = AIPerf.Begin();
        queue = AIRoles.Order(actionable, map, Profile);
        firesFirst.Clear();
        foreach (Unit u in queue) if (AIRoles.Classify(u, Profile) == AIRole.Artillery && AIRoles.HasTargetInRange(u)) firesFirst.Add(u);
        AIPerf.End("prepare: roles", __p); }
        // Supply under the logistics moves tails first: those no other supply unit hangs on before those
        // others hang on, so a chain whose front has gone draws in from its far end without cutting itself.
        if (logistics != null) {
            List<int> slots = new List<int>();
            List<Unit> hubs = new List<Unit>();
            for (int i = 0; i < queue.Count; i++) if (queue[i] != null && queue[i].Hub != null && !queue[i].Hub.producer) { slots.Add(i); hubs.Add(queue[i]); }
            SupplyNetwork.Result all = SupplyNetwork.SolveAll(player, new Dictionary<SupplyPoint, HexTile>());
            Dictionary<Unit, int> hanging = new Dictionary<Unit, int>();
            foreach (Unit h in hubs) {
                SupplyNetwork.Result w = SupplyNetwork.SolveWithout(player, h.Hub);
                int n = 0; foreach (SupplyPoint x in all.connectedHubs) if (x != h.Hub && !x.producer && !w.connectedHubs.Contains(x)) n++;
                hanging[h] = n;
            }
            hubs.Sort((a, b) => { int c = hanging[a].CompareTo(hanging[b]); return c != 0 ? c : AIMap.CompareUnits(a, b); });
            for (int i = 0; i < slots.Count; i++) queue[slots[i]] = hubs[i];
        }
        // (Clearing last turn's notes off the tiles is done on the main thread once this is done: Collect / StepOnce.)
        chainPlan = null;
        treePlan = null;
        if (Profile.supplyTree) {
            { long __p = AIPerf.Begin();
            treePlan = AISupplyTree.Make(map, null);
            AIPerf.End("prepare: supply tree", __p); }
            if (Trace != null) Trace("   " + treePlan);
            map.chainTiles.Clear();
            foreach (KeyValuePair<SupplyPoint, HexTile> kv in treePlan.links) if (kv.Value != kv.Key.myUnit.tile) map.chainTiles[kv.Value] = kv.Key.myUnit;
            // Supply units move after everyone else, the links in the order placed (each into the supply of those before it).
            List<Unit> hubsLast = new List<Unit>();
            foreach (SupplyPoint h in treePlan.order) if (queue.Contains(h.myUnit)) hubsLast.Add(h.myUnit);
            foreach (Unit u in queue) { SupplyPoint sp = u.Hub; if (sp != null && !sp.producer && !hubsLast.Contains(u)) hubsLast.Add(u); }
            foreach (Unit u in hubsLast) { queue.Remove(u); queue.Add(u); }
        } else if (Profile.supplyChain) {
            { long __p = AIPerf.Begin();
            chainPlan = AISupplyChain.Make(map, null);
            AIPerf.End("prepare: supply chain", __p); }
            if (Trace != null) Trace("   " + chainPlan);
            map.chainTiles.Clear();
            foreach (KeyValuePair<SupplyPoint, HexTile> kv in chainPlan.links) if (kv.Value != kv.Key.myUnit.tile) map.chainTiles[kv.Value] = kv.Key.myUnit;
            // Supply units move after everyone else, rearmost first: the army clears the road and
            // goes ahead, and each link moves up into the supply of the links behind it.
            Dictionary<HexTile, int> toObj = chainPlan.objective != null ? map.LandSteps(chainPlan.objective) : null;
            List<Unit> hubsLast = new List<Unit>();
            foreach (Unit u in queue) { SupplyPoint sp = u.Hub; if (sp != null && !sp.producer) hubsLast.Add(u); }
            System.Func<Unit, int> st = u => { int s; return toObj != null && u.tile != null && toObj.TryGetValue(u.tile, out s) ? s : int.MaxValue; };
            hubsLast.Sort((a, b) => { int c = st(b).CompareTo(st(a)); return c != 0 ? c : AIMap.CompareUnits(a, b); });
            foreach (Unit u in hubsLast) { queue.Remove(u); queue.Add(u); }
        }
        // Transports act after everyone else, so the units they carry (or are to pick up) have
        // decided first and handed them their plans: only when some fighting unit could ride this
        // turn, so with nobody to carry the turn order (and the game) is what it was without rides.
        bool anyRider = false;
        if (Profile.combatRides && (!Profile.assaultRides || Profile.assaultArmedRides)) foreach (Unit u in queue) if (MayRide(u)) { anyRider = true; break; }
        if (Profile.assaultRides) {
            // Riding to the assault: a transport with troops aboard goes before what it carries (they get off where it
            // stops); an empty one after everyone else, so all who get in this turn are in before it leaves.
            List<Unit> loaded = new List<Unit>(), empty = new List<Unit>();
            foreach (Unit u in queue) {
                if (!Transporting.IsTransport(u)) continue;
                if (AIAssault.Riders(u).Count > 0) loaded.Add(u);
                else if (u.transported.Count == 0 && u.Firepower <= 1 && !supplyTransports.Contains(u)) empty.Add(u);   // (supply's trucks keep the supply's order)
            }
            if (!Profile.assaultOrderWithLine) for (int i = loaded.Count - 1; i >= 0; i--) { queue.Remove(loaded[i]); queue.Insert(0, loaded[i]); }
            foreach (Unit u in empty) { queue.Remove(u); queue.Add(u); }
        }
        if (anyRider) {
            List<Unit> carriers = new List<Unit>();
            foreach (Unit u in queue) if (Transporting.IsTransport(u) && u.Firepower <= 1) carriers.Add(u);
            foreach (Unit u in carriers) { queue.Remove(u); queue.Add(u); }
        }
        // A chain link's truck acts after its link: the link boards (or says where to meet it) first.
        if (logistics != null)
            foreach (KeyValuePair<SupplyPoint, Unit> kv in logistics.chainTrucks) {
                int ti = queue.IndexOf(kv.Value), hi = kv.Key.myUnit != null ? queue.IndexOf(kv.Key.myUnit) : -1;
                if (ti < 0 || hi < 0 || ti > hi) continue;
                queue.RemoveAt(ti);
                queue.Insert(queue.IndexOf(kv.Key.myUnit) + 1, kv.Value);
            }
        { long __p = AIPerf.Begin();
        if (Profile.ghostSupply) PlanGhosts();
        AIPerf.End("prepare: ghosts", __p); }
        // Aircraft reserve their landings before anyone moves.
        { long __p = AIPerf.Begin();
        map.landings = AIAir.PlanLandings(map);
        AIPerf.End("prepare: landings", __p); }
        if (Trace != null) foreach (AIAir.Landing l in map.landings.Values) Trace("   landing reserved: " + l);
        planned.Clear();
        noAssist.Clear();
        firstLook.Clear(); firstLookLog.Clear(); firstBoard = null;
        long __c = AIPerf.Begin();
        if (Profile.coordination || Profile.coordinationPasses) {
            Action<string> realLog = AITactical.DecisionLog;
            List<PlanStep> steps = Profile.coordinationPasses
                ? AICoordinator.Plan(map, Profile, u => {
                      AIWorld.NoAssist = null;
                      AITactical.Assignments = assignments;
                      Front f = FrontOf(u);
                      // Kept (with its reasoning, when logging) for when the unit's turn comes (Profile.coordinationReuse).
                      System.Text.StringBuilder said = realLog != null ? new System.Text.StringBuilder() : null;
                      Action<string> was = AITactical.DecisionLog;
                      if (said != null) AITactical.DecisionLog = x => said.AppendLine(x);
                      AIAction act;
                      try { act = AITactical.Decide(u, map, Profile, f != null ? f.posture : Posture.Defensive, f, objective); }
                      finally { AITactical.DecisionLog = was; }
                      firstLook[u] = act;
                      if (said != null) firstLookLog[u] = said.ToString().TrimEnd();
                      return act;
                  }, noAssist, Trace)
                : AIPlan.Choose(map, Profile, Trace);
            if (firstLook.Count > 0) firstBoard = BoardMarks();
            // Planned units act first, in plan order; everyone else keeps the usual order after them.
            List<Unit> first = new List<Unit>();
            planOrder.Clear();
            planOrder.AddRange(steps);
            foreach (PlanStep s in steps) if (s.unit != null && queue.Contains(s.unit) && !planned.ContainsKey(s.unit)) { planned[s.unit] = s; first.Add(s.unit); }
            foreach (Unit u in first) queue.Remove(u);
            queue.InsertRange(0, first);
        }
        AIPerf.End("prepare: coordinated plans", __c);
        if (Profile.turnOrderSteps) TurnOrder();
        else {
        if (Profile.assaultRides && Profile.assaultOrderWithLine) OrderWithLine();
        // Aircraft landing this turn act last (after any coordinated plan too), once whatever was on their tiles has left.
        List<Unit> landingNow = new List<Unit>();
        foreach (AIAir.Landing l in map.landings.Values) if (l.turn == 1 && queue.Contains(l.unit)) landingNow.Add(l.unit);
        landingNow.Sort(AIMap.CompareUnits);
        foreach (Unit u in landingNow) { queue.Remove(u); queue.Add(u); }
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
    /// <summary>The transport each supply unit's ghost plan rides this turn (PlanGhosts): chosen for supply, it goes with it (TurnOrder).</summary>
    readonly Dictionary<Unit, Unit> ghostRides = new Dictionary<Unit, Unit>();

    void PlanGhosts() {
        ghostRides.Clear();
        for (int i = 0; i < map.ownHubs.Count; i++) {
            SupplyPoint hub = map.ownHubs[i];
            if (hub == null || hub.producer || hub.myUnit == null || !hub.myUnit.active) continue;
            HexTile link;
            if (treePlan != null && treePlan.links.TryGetValue(hub, out link)) {
                // Where it gets to tonight on the way to its link.
                AIAction step = AISupplyTree.Step(hub.myUnit, link, map);
                HexTile tonight = step.kind == AIActionKind.Move ? step.destination : null;
                if (tonight != null) map.SetGhost(hub, tonight);
                if (onGhost != null) onGhost(hub.myUnit, tonight);
                continue;
            }
            if (chainPlan != null && chainPlan.links.TryGetValue(hub, out link)) {
                map.SetGhost(hub, link);
                if (onGhost != null) onGhost(hub.myUnit, link == hub.myUnit.tile ? null : link);
                continue;
            }
            AISupplyPlanner.Ghost = true;
            TransportPlan plan;
            try { plan = AISupplyPlanner.PlanHub(hub, map, Profile, new HashSet<Unit>(reservedTransports)); }
            finally { AISupplyPlanner.Ghost = false; }
            HexTile at = GhostTile(plan);
            if (plan != null && plan.usesTransport && plan.transport != null) ghostRides[hub.myUnit] = plan.transport;
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
        // A ride lands it there tonight only if it arrives this turn (eta 0). (This said eta 1,
        // a turn early: the line planned with supply that was not there yet and over-reached.)
        if (plan.usesTransport) return plan.eta <= 0 ? plan.destination : null;
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
        // A supply unit cut off from the network (cutOffSeeksSupply) first heads back toward supplied ground or a transport.
        if (Profile.cutOffSeeksSupply && map != null && unit.Hub != null && !unit.Hub.producer && unit.trasnporter == null && unit.tile != null) {
            AIAction? back = SeekSupply(unit);
            if (back.HasValue) return back.Value;
        }
        // A transport with a supply unit aboard is that supply unit's (campaignLogistics): the supply logic drives it.
        AIAction carried;
        if (CarriesSupply(unit, out carried)) {
            // Ending on a building slot of ours (keepBuildSlotsClear): off it, to the nearest free tile beside it.
            if (map != null && AIAir.BlocksBuildSlot(unit, AIAir.EndOf(unit, carried), map)) {
                HexTile off = null; float ol = float.MaxValue;
                MoveField near = map.ReachThisTurn(unit);
                foreach (HexTile x in near.Tiles) {
                    if (x == unit.tile || !near.ReachableThisTurn(x) || x.occupier != null || !unit.CanEnter(x) || !AIAir.MayEndOn(unit, x, map)) continue;
                    float l = map.ExpectedLoss(unit, x) + 0.01f * unit.tile.Distance(x);
                    if (l < ol || (Mathf.Abs(l - ol) <= 0.001f && CombatMath.Precedes(x, off))) { ol = l; off = x; }
                }
                if (off != null) {
                    if (Trace != null) Trace("   " + unit.name.Replace("(Clone)", "") + D(unit.tile) + ": " + carried.reason + " would end on a building slot of ours: moves off to " + D(off));
                    carried = AIAction.Move(unit, off, 0f, "keeps the building slot clear (" + carried.reason + ")");
                }
            }
            return map != null ? AIAir.Guard(unit, carried, map) : carried;
        }
        // Put down this turn to strike (assaultRides): it makes the strike its transport planned for it.
        if (Profile.assaultRides && Profile.assaultCarryOut) { AIAction? done = AIAssault.CarryOut(unit, map); if (done.HasValue) return done.Value; }
        AIAction a = DecideForUnit(unit);
        // Riding to the assault: getting in a transport is weighed against what the unit would do on foot (assaultBoardWeighs).
        if (Profile.assaultRides && Profile.assaultBoardWeighs) a = BoardOrNot(unit, a);
        // A decision path that does not go through the tactical choice (which already leaves out tiles
        // reserved for a landing this turn) chose one: the unit decides again the ordinary way, so an
        // aircraft parked there lifts off to whatever mission it would pick anyway.
        if (map != null && unit != null && unit.tile != null && !AIAir.MayEndOn(unit, AIAir.EndOf(unit, a), map)) {
            Front f = FrontOf(unit);
            AIWorld.NoAssist = noAssist.Count > 0 ? noAssist : null;
            a = AITactical.Decide(unit, map, Profile, f != null ? f.posture : Posture.Defensive, f, objective);
        }
        // Aircraft and ships never end a turn where they cannot get home from.
        a = map != null && unit != null ? AIAir.Guard(unit, a, map) : a;
        // A transport with riders whose assault move was set aside for the ordinary one (a tile held for a supply link or a
        // landing; the guard): that is a fallback, and its riders decide for themselves (assaultFrontlineOut).
        if (Profile.assaultRides && unit != null && Transporting.IsTransport(unit) && AIAssault.Riders(unit).Count > 0
            && (a.reason == null || !a.reason.StartsWith("assault") && !a.reason.StartsWith("medevac") && !a.reason.StartsWith("holds the line")  && !a.reason.StartsWith("sea lift"))) {
            AIAssault.FellBack.Add(unit);
            if (Trace != null) Trace("   " + unit.name.Replace("(Clone)", "") + D(unit.tile) + ": its move is not its assault ride (" + a.reason + "): its riders decide for themselves");
        }
        return a;
    }

    /// <summary>
    /// Boarding weighed against the unit's own move (assaultBoardWeighs): it gets in only when what it would do on foot
    /// is a walk (no strike, no capture, no hold on a building), the ride is for it (its front attacks, its ground a long
    /// walk off, or it is wounded and a transport can take it to mend), and the transport takes it nearer its ground
    /// than its walk would. Otherwise its own move stands.
    /// </summary>
    AIAction BoardOrNot(Unit unit, AIAction onFoot) {
        if (unit == null || unit.tile == null || unit.trasnporter != null || unit.Hub != null || unit.Firepower <= 0) return onFoot;
        if (putDownThisTurn.Contains(unit) || AIAssault.Capturing(unit)) return onFoot;
        bool hurt = AIAssault.Wounded(unit, Profile, map);
        // Its own move is worth more than any ride: a strike, a capture, a load it already chose.
        if (onFoot.kind == AIActionKind.Attack || onFoot.kind == AIActionKind.MoveAndAttack || onFoot.kind == AIActionKind.Capture || onFoot.kind == AIActionKind.Load || onFoot.kind == AIActionKind.Unload) return onFoot;
        Front bf = FrontOf(unit);
        HexTile ground = bf == null ? null : bf.staging && bf.stagingTile != null ? bf.stagingTile : bf.objectiveTile;
        // Its ground across water from it: no walk gets it there, so holding where it is is no task (a ship takes it).
        HexTile wants = ground ?? GroundOf(unit);
        bool overWater = Profile.seaLift && wants != null && !LocomotionRules.IsAirborne(unit.locomotion) && !LocomotionRules.IsNaval(unit.locomotion) && map.AcrossWater(unit.tile, wants);
        if (overWater && ground == null) ground = wants;
        if (!hurt && onFoot.kind != AIActionKind.Move && !overWater) return onFoot;   // holding where it is (its objective, its line) is its task
        // Walking onto a building to take: its task, if it lives to take it (else a wounded one rides to mend).
        if (onFoot.kind == AIActionKind.Move && onFoot.destination != null && onFoot.destination.building != null && onFoot.destination.building.owner != player && Building.CanCapture(unit)) {
            int take = AIAssault.TurnsToTake(unit, onFoot.destination.building, map);
            if (take >= 0 || !hurt) return onFoot;
            if (Trace != null) Trace("   " + unit.name.Replace("(Clone)", "") + D(unit.tile) + ": would go to take " + onFoot.destination.building.name.Replace("(Clone)", "") + D(onFoot.destination) + " but would be gone before it fell: rides to mend instead");
        }
        bool goes = bf == null || (bf.posture == Posture.Aggressive && !bf.staging) || overWater
                 || (ground != null && map.Travel(unit, unit.tile, ground) > Profile.assaultBoardTurns * Mathf.Max(1, unit.moveRange));
        if (!goes && !hurt) return onFoot;
        HashSet<Unit> busy = new HashSet<Unit>(reservedTransports);
        foreach (Unit t in transportAssignments.Keys) busy.Add(t);
        if (logistics != null) foreach (Unit t in Transporting.AvailableFor(player, unit)) if (IsLogisticsTruck(t)) busy.Add(t);
        long __ab = AIPerf.Begin();
        AIAction? ba = AIAssault.Board(unit, map, Profile, busy, supplyTransports, SupplyGoing(), ground);
        if (ba.HasValue && ground != null) riderGoal[unit] = ground;
        AIPerf.End("assault: boarding", __ab);
        // Across water and no ship to get into here: to the naval yard, where ships take troops on (while we have a ship).
        if (!ba.HasValue && overWater) {
            AIAction? walk = SeaLiftWalk(unit);
            if (walk.HasValue) return walk.Value;
        }
        if (!ba.HasValue) return onFoot;
        // A ride for the fight: only if the walk does not already get it there (its ground, else the nearest enemy).
        if (!hurt) {
            HexTile goal = ground ?? NearestEnemyTile(unit);
            HexTile walkTo = onFoot.destination ?? unit.tile;
            if (goal != null && ba.Value.transport != null && ba.Value.transport.tile != null) {
                int walked = map.Travel(unit, walkTo, goal);
                // Aboard it goes as the transport goes: a move of the transport from where it is now.
                int rode = Mathf.Max(0, map.Travel(ba.Value.transport, ba.Value.transport.tile, goal) - Mathf.Max(1, ba.Value.transport.moveRange));
                if (rode >= walked) { if (Trace != null) Trace("   " + unit.name.Replace("(Clone)", "") + D(unit.tile) + ": would board, but its walk (" + onFoot.reason + ") gets it as near"); return onFoot; }
            }
        }
        if (Trace != null) Trace("   " + unit.name.Replace("(Clone)", "") + D(unit.tile) + ": boards rather than " + onFoot.kind + " (" + onFoot.reason + ")");
        return ba.Value;
    }

    /// <summary>
    /// A transport that has a supply unit aboard belongs to it (campaignLogistics): its move is the supply
    /// logic's. The ride the supply unit planned (a chain leap, a ride to its front) if it has one; else,
    /// one of a front's own moves with its front (toward the front's nearest unit), and a link of a chain
    /// holds where it is (the chain's network moves it, by a leap). A rider that would not get nearer its
    /// goal is let out first.
    /// </summary>
    /// <summary>
    /// The trucks a fighting unit may ride along on with a supply unit this turn: those taking a front's own
    /// supply unit to its front (AILogistics.rides), and each chain link's truck, going where its link is
    /// planned to stand (AILogistics.linkTiles).
    /// </summary>
    List<KeyValuePair<SupplyPoint, HexTactica.AI.AILogistics.Ride>> RidesAlong() {
        List<KeyValuePair<SupplyPoint, HexTactica.AI.AILogistics.Ride>> all = new List<KeyValuePair<SupplyPoint, HexTactica.AI.AILogistics.Ride>>(logistics.rides);
        foreach (KeyValuePair<SupplyPoint, Unit> kv in logistics.chainTrucks) {
            HexTile to;
            if (kv.Value == null || !logistics.linkTiles.TryGetValue(kv.Key, out to) || to == null) continue;
            all.Add(new KeyValuePair<SupplyPoint, HexTactica.AI.AILogistics.Ride>(kv.Key, new HexTactica.AI.AILogistics.Ride { truck = kv.Value, to = to, task = null }));
        }
        return all;
    }

    /// <summary>What we know of the enemy's forces, kept from turn to turn (AIKnowledge).</summary>
    readonly AIKnowledge knowledge = new AIKnowledge();

    /// <summary>Last turn's chain trucks (AILogistics.chainTrucks): a truck keeps the link it was coming for.</summary>
    Dictionary<SupplyPoint, Unit> lastChainTrucks;

    // ------------------------------------------------------------------ sea lift (AIProfile.seaLift)

    /// <summary>
    /// The objective (AISupplyChain.Objective: the enemy HQ, else its nearest unit) when it lies across water from
    /// <paramref name="from"/>: no land unit walks there, so the army and its supply go by ship. Null otherwise.
    /// </summary>
    HexTile SeaLiftGoal(HexTile from) {
        HexTile goal = AISupplyChain.Objective(map);
        return goal != null && from != null && map.AcrossWater(from, goal) ? goal : null;
    }

    /// <summary>Where <paramref name="u"/> wants to be: its front's ground (its staging tile while it gathers, else its objective), else the campaign's objective.</summary>
    HexTile GroundOf(Unit u) {
        Front f = FrontOf(u);
        if (f != null) { HexTile g = f.staging && f.stagingTile != null ? f.stagingTile : f.objectiveTile; if (g != null) return g; }
        return AISupplyChain.Objective(map);
    }

    /// <summary>
    /// A land unit whose ground is across water, with no ship to get into where it stands: a step toward the naval yard it
    /// walks to soonest, while we have a ship to take it over (or one being built). Null: beside the yard already, or no ship.
    /// </summary>
    AIAction? SeaLiftWalk(Unit unit) {
        bool ships = false;
        foreach (Unit t in map.ownUnits) if (AIAssault.SeaLiftShip(t, map) && Transporting.CanCarry(t, unit)) { ships = true; break; }
        if (!ships) return null;
        Building yard = AIAssault.NearestYard(unit, map);
        if (yard == null || unit.tile.Distance(yard.tile) <= 1) return null;
        HexTile step = AITransportPlanner.SafeStepToward(unit, yard.tile, map.ReachHorizon(unit), map, false);
        if (step == null || step == unit.tile || step == yard.tile) return null;
        return AIAction.Move(unit, step, 0f, "sea lift: walks to the naval yard (" + yard.tile.x + "," + yard.tile.y + ") to cross");
    }

    /// <summary>Our supply already reaches the land <paramref name="ground"/> stands on (a landing made before, a building taken there).</summary>
    bool LandingSupplied(HexTile ground) {
        Dictionary<HexTile, int> land = ground != null ? map.LandSteps(ground) : null;
        if (land == null) return false;
        foreach (HexTile t in land.Keys) if (map.SupplyDepth(t) > 0) return true;
        return false;
    }

    /// <summary>
    /// Where a ship's troops are going over the water: the ground of the first of them whose ground is across water from
    /// the ship's side; for an empty ship, the campaign's objective when that is across water from our HQ. Null: none.
    /// </summary>
    HexTile SeaLiftLanding(Unit ship) {
        foreach (Unit r in AIAssault.Riders(ship)) { HexTile g = GroundOf(r); if (g != null) return g; }
        Building home = GameController.HQOf(player);
        return SeaLiftGoal(home != null ? home.tile : null);
    }

    /// <summary>
    /// A ship (troops aboard or none) the supply unit <paramref name="unit"/> should get into to go with them: one with room
    /// for it and no supply aboard, going over water to land our supply does not reach yet; of our supply units on foot
    /// that could, the nearest to it goes. Null: none.
    /// </summary>
    Unit SeaLiftShipFor(Unit unit) {
        Unit best = null; int bestD = int.MaxValue;
        foreach (Unit t in Transporting.AvailableFor(player, unit)) {
            if (t == null || t.tile == null || !AIAssault.SeaLiftShip(t, map) || AIAssault.CarriesHub(t)) continue;
            HexTile landing = SeaLiftLanding(t);
            if (landing == null || !map.AcrossWater(unit.tile, landing) || LandingSupplied(landing)) continue;
            // The nearest of our supply units on foot to this ship goes (one per ship).
            int d = map.Travel(unit, unit.tile, t.tile);
            bool nearer = false;
            foreach (SupplyPoint h in map.ownHubs) {
                Unit o = h != null ? h.myUnit : null;
                if (o == null || o == unit || h.producer || o.tile == null || o.trasnporter != null || LocomotionRules.IsNaval(o.locomotion) || !Transporting.CanCarry(t, o)) continue;
                int od = map.Travel(o, o.tile, t.tile);
                if (od < d || (od == d && CombatMath.Precedes(o.tile, unit.tile))) { nearer = true; break; }
            }
            if (nearer) continue;
            if (best == null || d < bestD || (d == bestD && CombatMath.Precedes(t.tile, best.tile))) { best = t; bestD = d; }
        }
        return best;
    }

    /// <summary>
    /// A supply unit on the shore: into the ship SeaLiftShipFor gives it — from where it stands, or from a tile beside the
    /// ship it can walk to this turn; else a step toward it. Null: no ship for it.
    /// </summary>
    AIAction? SeaLiftBoard(Unit unit) {
        Unit ship = SeaLiftShipFor(unit);
        if (ship == null) return null;
        HexTile at = null;
        if (Transporting.CanBoardFrom(ship, unit)) at = unit.tile;
        else {
            MoveField now = map.ReachThisTurn(unit);
            foreach (HexTile n in ship.tile.neighbors)
                if (n != null && now.ReachableThisTurn(n) && (n.occupier == null || n.occupier == unit) && Transporting.CanBoardFromTile(ship, unit, n) && (at == null || CombatMath.Precedes(n, at))) at = n;
        }
        if (at != null) return AIAction.Board(unit, ship, at, "sea lift: boards the ship to carry supply over with its troops");
        HexTile step = AITransportPlanner.SafeStepToward(unit, ship.tile, map.ReachHorizon(unit), map, true);
        if (step == null || step == unit.tile) return null;
        return AIAction.Move(unit, step, 0f, "sea lift: goes to the ship that will carry it over");
    }

    /// <summary>
    /// A ship carrying a supply unit, the objective across water: to the landing — beside the land nearest our troops
    /// across the water (else the objective), on a tile where its supply is linked (SupplyNetwork: within a move of
    /// supplied shore, or through another carrier) and so gives supply ashore. Where nothing linked is in reach, it holds
    /// where it is linked, or heads for the landing. Null: not a sea lift.
    /// </summary>
    AIAction? SeaLiftSupply(Unit ship, SupplyPoint hub) {
        Building home = GameController.HQOf(player);
        HexTile goal = SeaLiftGoal(home != null ? home.tile : null);
        if (goal == null) return null;
        // The landing: our troops already across (nearest the objective), else the objective itself.
        HexTile land = null; int ld = int.MaxValue;
        foreach (Unit u in map.ownUnits) {
            if (u == null || u.tile == null || u.trasnporter != null || u.Hub != null || u.Firepower <= 0 || LocomotionRules.IsNaval(u.locomotion) || LocomotionRules.IsAirborne(u.locomotion)) continue;
            if (map.AcrossWater(u.tile, goal)) continue;   // still on our side
            int d = u.tile.Distance(goal);
            if (d < ld || (d == ld && CombatMath.Precedes(u.tile, land))) { land = u.tile; ld = d; }
        }
        // Nobody across yet: it does not go alone; it waits for troops to take over with it.
        if (land == null) return AIAction.Wait(ship, "sea lift: holds with its supply for troops to take across");
        MoveField f = map.ReachThisTurn(ship);
        Dictionary<SupplyPoint, Unit> wasAboard = SupplyNetwork.PlannedAboard;
        HexTile best = null; bool bestLinked = false, bestShore = false; int bestD = int.MaxValue;
        try {
            SupplyNetwork.PlannedAboard = new Dictionary<SupplyPoint, Unit> { { hub, ship } };
            foreach (HexTile t in f.Tiles) {
                if (t != ship.tile && (!f.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(ship, t) || !AIAir.MayEndOn(ship, t, map))) continue;
                bool shore = Unit.BordersLand(t);
                bool linked = SupplyNetwork.SolvePlanned(player, new Dictionary<SupplyPoint, HexTile> { { hub, t } }).connectedHubs.Contains(hub);
                int d = t.Distance(land);
                bool better;
                if (best == null) better = true;
                else if (linked != bestLinked) better = linked;
                else if (linked && shore != bestShore) better = shore;
                else better = d < bestD || (d == bestD && CombatMath.Precedes(t, best));
                if (better) { best = t; bestLinked = linked; bestShore = shore; bestD = d; }
            }
        } finally { SupplyNetwork.PlannedAboard = wasAboard; SupplyNetwork.Solve(player, false); }
        if (best == null || best == ship.tile) return AIAction.Wait(ship, "sea lift: holds with its supply" + (bestLinked ? " (linked)" : ""));
        return AIAction.Move(ship, best, 0f, "sea lift: takes its supply toward the landing at (" + land.x + "," + land.y + ")" + (bestLinked ? ", linked" : ", not linked"));
    }

    bool CarriesSupply(Unit truck, out AIAction action) {
        action = default(AIAction);
        if (logistics == null || truck == null || truck.tile == null || !Transporting.IsTransport(truck)) return false;
        Unit cargo = null;
        foreach (Unit c in truck.transported) if (c != null && c.Hub != null && !c.Hub.producer) { cargo = c; break; }
        if (cargo == null) return false;
        SupplyPoint hub = cargo.Hub;
        if (Profile.seaLift && LocomotionRules.IsNaval(truck.locomotion)) {
            // Troops aboard too (a convoy): it goes as their ride goes (the assault ride), its supply aboard throughout.
            if (AIAssault.Riders(truck).Count > 0) return false;
            AIAction? sl = SeaLiftSupply(truck, hub);
            if (sl.HasValue) { action = sl.Value; return true; }
        }
        // Relieved (Relieve): the step it was relieved to make.
        HexTile upTo;
        if (relieved.TryGetValue(hub, out upTo) && upTo.occupier == null && !CutsOff(hub, upTo)) {
            action = AIAction.Move(truck, upTo, 0f, "carries its supply unit: relieved, moves up");
            return true;
        }
        TransportPlan plan;
        if (!transportAssignments.TryGetValue(truck, out plan) || plan == null || plan.cargo != cargo) {
            plan = null;
            // Its supply unit has not decided yet this turn: it decides now (the plan it hands the truck).
            AIAction ignored;
            if (cargo.active && LogisticsAction(hub, out ignored)) transportAssignments.TryGetValue(truck, out plan);
            if (plan != null && plan.cargo != cargo) plan = null;
        }
        HexTile to = null;
        string why;
        if (plan != null) { to = plan.dropoff; why = "carries its supply unit: "; }
        else {
            HexTactica.AI.AILogistics.Task t;
            if (logistics.tacticalOf.TryGetValue(hub, out t)) {
                HexTile near; HexTactica.AI.AILogistics.NearestOf(t, cargo, map, out near);
                if (near != null) {
                    HexTile step = AITransportPlanner.SafeStepToward(truck, near, map.ReachHorizon(truck), map, true);
                    if (step != null && step != truck.tile && !CutsOff(hub, step)) to = step;
                }
                why = "carries its front's supply unit with the front";
            } else why = "carries a link of the chain: holds with the chain";
            plan = new TransportPlan { cargo = cargo, transport = truck, rendezvous = truck.tile, dropoff = to ?? truck.tile, destination = to ?? truck.tile, stayAboard = true };
            transportAssignments[truck] = plan;
        }
        // Riders who would not get nearer their goal get out here.
        HexTile end = plan.dropoff ?? truck.tile;
        foreach (Unit r in truck.transported) {
            if (r == null || r == cargo || r.Hub != null) continue;
            // It got in this turn for this ride (judged then, from where it stood): not judged again from the truck's tile.
            if (boardedThisTurn.Contains(r) && riderGoal.ContainsKey(r)) continue;
            Front f = FrontOf(r);
            // The same goal it got in for (AIAssault.Board's ground): its staging tile while its front gathers, else the objective.
            HexTile goal;
            if (!riderGoal.TryGetValue(r, out goal) || goal == null)
                goal = f != null && f.staging && f.stagingTile != null ? f.stagingTile : f != null && f.objectiveTile != null ? f.objectiveTile : AISupplyChain.Objective(map);
            if (goal == null) continue;
            Dictionary<HexTile, int> steps = map.LandSteps(goal);
            int now, then;
            if (steps == null || !steps.TryGetValue(truck.tile, out now)) continue;
            if (!steps.TryGetValue(end, out then)) then = int.MaxValue;
            if (then < now) continue;
            List<HexTile> off = Transporting.UnloadOptions(truck, r);
            if (off.Count == 0) continue;
            off.Sort((x, y) => { int a = (steps.ContainsKey(x) ? steps[x] : int.MaxValue).CompareTo(steps.ContainsKey(y) ? steps[y] : int.MaxValue); return a != 0 ? a : CombatMath.CompareTiles(x, y); });
            action = AIAction.Disembark(truck, r, off[0], "lets out a rider the supply's ride takes no nearer its goal");
            return true;
        }
        action = AITransportPlanner.TransportAction(truck, plan, map);
        // Others hang from it: it holds as a link where it is until another supply unit relieves it (Relieve).
        // (Others hang from it: KeepLinks holds it where it is, as a link, until another supply unit relieves it.)
        if (!string.IsNullOrEmpty(why) && action.reason != null && !action.reason.StartsWith(why)) action.reason = why + " — " + action.reason;
        return true;
    }

    AIAction DecideForUnit(Unit unit) {
        if (unit == null) return AIAction.Wait(null, "null");
        AIWorld.NoAssist = noAssist.Count > 0 ? noAssist : null;
        AITactical.Intents = intents;
        AITactical.Assignments = assignments;

        Front front = FrontOf(unit);
        Posture posture = front != null ? front.posture : Posture.Defensive;


        // Riding with the supply (supplyBackbone): infantry out of contact beside a backbone truck that
        // is going forward, with room for it as well as its supply unit, gets in; the truck lets it out
        // where its link stops. The army and its supply move up together.
        if (Profile.supplyBackbone && backbonePlanned != null && unit.Firepower > 0 && unit.Hub == null && unit.trasnporter == null
            && unit.locomotion == Locomotion.Boots && map.TurnsToContact(unit) > Profile.objectiveContactTurns) {
            HexTile goal = AISupplyChain.Objective(map);
            Dictionary<HexTile, int> toObj = goal != null ? map.LandSteps(goal) : null;
            int mine;
            if (toObj != null && toObj.TryGetValue(unit.tile, out mine))
                foreach (AISupplyBackbone.Link link in backbonePlanned.links.Values) {
                    Unit truck = link.transport;
                    int there;
                    if (truck == null || truck.tile == null || !toObj.TryGetValue(link.tile, out there) || there > mine - 3) continue;
                    if (!Transporting.CanBoardFrom(truck, unit)) continue;
                    // Room for this unit and for its supply unit, if that is not aboard yet.
                    int need = Mathf.Max(1, unit.weight) + (link.hub.myUnit.trasnporter == truck ? 0 : Mathf.Max(1, link.hub.myUnit.weight));
                    if (Transporting.FreeWeight(truck) < need || truck.transported.Count + (link.hub.myUnit.trasnporter == truck ? 1 : 2) > truck.transportSlots) continue;
                    return AIAction.Board(unit, truck, unit.tile, "rides with the supply to (" + link.tile.x + "," + link.tile.y + ")");
                }
        }

        // Riding along (campaignLogistics): a fighting unit out of contact beside a truck carrying one of
        // a front's own supply units to its front, or a chain link to its place (RidesAlong), with room for
        // both, gets in if the truck sets down nearer its own goal than it could walk; the truck lets it out
        // where it stops.
        if (logistics != null && unit.Firepower > 0 && unit.Hub == null && unit.trasnporter == null
            && unit.locomotion == Locomotion.Boots && map.TurnsToContact(unit) > Profile.objectiveContactTurns) {
            HexTile goal = front != null && front.objectiveTile != null ? front.objectiveTile : AISupplyChain.Objective(map);
            Dictionary<HexTile, int> toGoal = goal != null ? map.LandSteps(goal) : null;
            int mine;
            if (toGoal != null && toGoal.TryGetValue(unit.tile, out mine))
                foreach (KeyValuePair<SupplyPoint, HexTactica.AI.AILogistics.Ride> kv in RidesAlong()) {
                    Unit truck = kv.Value.truck; Unit cargo = kv.Key.myUnit;
                    int there;
                    if (truck == null || truck.tile == null || cargo == null || !toGoal.TryGetValue(kv.Value.to, out there) || there > mine - Mathf.Max(1, unit.moveRange)) continue;
                    if (!Transporting.CanBoardFrom(truck, unit)) continue;
                    // Only if its supply unit gets in this turn too (already aboard, or beside it).
                    bool cargoAboard = cargo.trasnporter == truck;
                    if (!cargoAboard && !Transporting.CanBoardFrom(truck, cargo)) continue;
                    int need = Mathf.Max(1, unit.weight) + (cargoAboard ? 0 : Mathf.Max(1, cargo.weight));
                    if (Transporting.FreeWeight(truck) < need || truck.transported.Count + (cargoAboard ? 1 : 2) > truck.transportSlots) continue;
                    return AIAction.Board(unit, truck, unit.tile, "rides along with the supply to (" + kv.Value.to.x + "," + kv.Value.to.y + ")");
                }
        }

        // Riding to the assault (assaultRides): aboard, it stays in until it is put down to strike; far from the fight,
        // it gets in a transport.
        if (Profile.assaultRides && unit.Hub == null) {
            if (unit.trasnporter != null && !AIAssault.CarriesHub(unit.trasnporter)) {
                Front rf = FrontOf(unit);
                AIAssault.HasFront = rf != null;
                HexTile rground = rf == null ? null : rf.staging && rf.stagingTile != null ? rf.stagingTile : rf.objectiveTile;
                AIAction? ra = AIAssault.RiderAction(unit, rf != null && rf.posture == Posture.Aggressive && !rf.staging, map, Profile, rf != null && (rf.posture != Posture.Aggressive || rf.staging) ? rground : null, GroundOf(unit));
                if (ra.HasValue) return ra.Value;
            } else if (unit.Firepower > 0 && !Profile.assaultBoardWeighs) {
                HashSet<Unit> busy = new HashSet<Unit>(reservedTransports);
                foreach (Unit t in transportAssignments.Keys) busy.Add(t);
                if (logistics != null) foreach (Unit t in Transporting.AvailableFor(player, unit)) if (IsLogisticsTruck(t)) busy.Add(t);
                // Into a transport when its front attacks, or when its front's ground is a long walk off.
                Front bf = FrontOf(unit);
                HexTile ground = bf == null ? null : bf.staging && bf.stagingTile != null ? bf.stagingTile : bf.objectiveTile;
                bool goes = bf == null || (bf.posture == Posture.Aggressive && !bf.staging)
                         || (ground != null && map.Travel(unit, unit.tile, ground) > Profile.assaultBoardTurns * Mathf.Max(1, unit.moveRange));
                long __ab = AIPerf.Begin();
                AIAction? ba = !putDownThisTurn.Contains(unit) && (goes || AIAssault.Wounded(unit, Profile, map)) ? AIAssault.Board(unit, map, Profile, busy, supplyTransports, SupplyGoing(), ground) : null;
                if (ba.HasValue && ground != null) riderGoal[unit] = ground;
                AIPerf.End("assault: boarding", __ab);
                if (ba.HasValue) return ba.Value;
            }
        }

        // A fighting unit out of contact (an objective's or not): ride a transport toward the
        // objective if that is quicker than walking.
        // With assaultRides on, the old ride stays for armed transports (a recon taxi) when assaultArmedRides is on.
        if (Profile.combatRides && (!Profile.assaultRides || Profile.assaultArmedRides) && unit.Firepower > 0 && unit.Hub == null
            && (!Profile.assaultRides || unit.trasnporter == null || unit.trasnporter.Firepower > 1)) {
            AIAction ride;
            if (TryRide(unit, front, out ride)) return ride;
        }

        // Mission objectives narrow the possibility space rather than commanding units,
        // which is what lets them compose with tactics instead of overriding them.
        if (objective != null && objective.OverridesTactics(unit, map)) {
            AIAction directed = objective.Decide(unit, map, Profile, posture);
            if (!directed.IsNothing) return directed;
        }

        SupplyPoint hub = unit.Hub;
        if (hub != null) {
            if (hub.producer) return AIAction.Wait(unit, "HQ holds");
            // The real move is planned on the real board: the ghost has served its purpose.
            map.ClearGhost(hub);

            // Sea lift: the objective across water. Aboard a ship it rides it (the ship takes it to the landing,
            // SeaLiftSupply); on the shore, it gets into a ship with room for it.
            if (Profile.seaLift && unit.trasnporter == null && !LocomotionRules.IsNaval(unit.locomotion)) {
                AIAction? sb = SeaLiftBoard(unit);
                if (sb.HasValue) return sb.Value;
            }
            if (Profile.seaLift && AIAssault.SeaLiftShip(unit.trasnporter, map))
                return AIAction.Wait(unit, "sea lift: rides its ship to the landing");

            // A link of the supply backbone (supplyBackbone): aboard its transport, on the way to its place or holding it.
            if (Profile.supplyBackbone) {
                if (backboneTurn != map || backbone == null) {
                    HashSet<Unit> busy = new HashSet<Unit>(reservedTransports);
                    foreach (KeyValuePair<Unit, TransportPlan> kv in transportAssignments) if (kv.Value == null || kv.Value.cargo == null || kv.Value.cargo.Hub == null) busy.Add(kv.Key);
                    AISupplyBackbone.DebugLog = Trace;
                    backbone = AISupplyBackbone.Make(map, busy);
                    AISupplyBackbone.DebugLog = null;
                    backboneTurn = map;
                }
                AISupplyBackbone.Link link;
                if (backbone.links.TryGetValue(hub, out link) && link.transport != null && link.transport.tile != null) {
                    Unit truck = link.transport;
                    reservedTransports.Add(truck);
                    if (unit.trasnporter == truck) {
                        transportAssignments[truck] = new TransportPlan { cargo = unit, transport = truck, rendezvous = truck.tile, dropoff = link.tile, destination = link.tile, stayAboard = true };
                        return AIAction.Wait(unit, "supply backbone: rides to (" + link.tile.x + "," + link.tile.y + ")");
                    }
                    if (unit.trasnporter != null) {
                        TransportPlan off = UnloadHere(unit);
                        if (off != null) { transportAssignments[off.transport] = off; reservedTransports.Add(off.transport); }
                        return AIAction.Wait(unit, "supply backbone: off this one first");
                    }
                    // Board now if it can (beside it, or a walk away), else wait for the truck to come.
                    MoveField walk = map.ReachThisTurn(unit);
                    HexTile beside = null;
                    if (Transporting.CanBoardFrom(truck, unit)) beside = unit.tile;
                    else foreach (HexTile n in truck.tile.neighbors) {
                        if (n == null || !walk.ReachableThisTurn(n) || !Transporting.CanBoardFromTile(truck, unit, n)) continue;
                        if (beside == null || CombatMath.Precedes(n, beside)) beside = n;
                    }
                    if (beside != null) {
                        transportAssignments[truck] = new TransportPlan { cargo = unit, transport = truck, rendezvous = truck.tile, dropoff = link.tile, destination = link.tile, stayAboard = true };
                        return AIAction.Board(unit, truck, beside, "supply backbone: boards for (" + link.tile.x + "," + link.tile.y + ")");
                    }
                    HexTile meet = null;
                    foreach (HexTile n in unit.tile.neighbors) if (n != null && (n.occupier == null || n.occupier == truck) && truck.CanEnter(n) && (meet == null || map.Travel(truck, truck.tile, n) < map.Travel(truck, truck.tile, meet))) meet = n;
                    if (meet != null) transportAssignments[truck] = new TransportPlan { cargo = unit, transport = truck, rendezvous = meet, dropoff = link.tile, destination = link.tile, stayAboard = true };
                    return AIAction.Wait(unit, "supply backbone: waits for its transport");
                }
            }

            // A link of the supply tree: planned again now, with the supply units that have already
            // moved where they are and the army where it went.
            if (Profile.supplyTree && unit.trasnporter == null) {
                List<Unit> moved = new List<Unit>();
                foreach (SupplyPoint h in map.ownHubs) if (h != null && h.myUnit != null && !h.myUnit.active) moved.Add(h.myUnit);
                AISupplyTree.Plan now = AISupplyTree.Make(map, moved);
                HexTile link;
                if (now.links.TryGetValue(hub, out link)) return AISupplyTree.Step(unit, link, map);
            }
            // A link of the supply chain: planned again now, with the supply units that have
            // already moved where they are and the army where it went.
            // Supply front first serves the whole army, the campaign's task forces included.
            if (logistics != null) {
                AIAction la;
                if (LogisticsAction(hub, out la)) {
                    float dty; logistics.duty.TryGetValue(hub, out dty);
                    string role = logistics.chainOf.ContainsKey(hub) ? "chain" : logistics.tacticalOf.ContainsKey(hub) ? "own" : "none";
                    if (Trace != null) Trace("   supply at (" + unit.tile.x + "," + unit.tile.y + ")" + (unit.trasnporter != null ? " aboard" : "") + " [" + role + ", duty " + dty.ToString("0") + (logistics.dutied.Contains(hub) ? "+" : "") + "]: " + la.kind + (la.destination != null ? " to (" + la.destination.x + "," + la.destination.y + ")" : "") + " - " + la.reason);
                    return la;
                }
            }
            CampaignObjective job = campaign != null && !Profile.supplyFrontFirst && logistics == null ? campaign.ObjectiveOf(hub) : null;
            if (job != null && unit.trasnporter == null) {
                // A task force's supply: its own chain toward where it gathers, and once it has gone in, on
                // toward its objective — never ahead of its army either way. (Aimed at the staging ground
                // for good, the supply stopped there, the army would not leave its supply, and a task force
                // on an undefended HQ stood 10 tiles short of it for 11 turns.)
                List<Unit> moved = new List<Unit>();
                foreach (SupplyPoint h in job.hubs) if (h != null && h.myUnit != null && !h.myUnit.active) moved.Add(h.myUnit);
                AISupplyChain.Plan now = AISupplyChain.Make(map, moved, job.went || job.staging == null ? job.target : job.staging, job.hubs, job.units);
                HexTile link;
                if (now.links.TryGetValue(hub, out link)) {
                    if (link == unit.tile) return AIAction.Wait(unit, "supply for " + job.Id + ": holds this link");
                    return AIAction.Move(unit, link, 0f, "supply for " + job.Id + ": link moves up (" + now.reachSteps + " steps short)");
                }
            }
            if (Profile.supplyFrontFirst && job == null) {
                if (frontHubs.Contains(hub)) {
                    // Front line: aboard a truck (its own, or a free one it can board now), the truck goes
                    // where the supply unit supplies the army most for what it risks there (the world's
                    // reply) — wherever the truck keeps it linked; the rest line up behind to link it.
                    AIAction fa;
                    if (FrontLineLeg(hub, out fa)) return fa;
                    // No truck to hand: placed on foot as any supply unit is.
                    TransportPlan walkPlan = AISupplyPlanner.PlanHub(hub, map, Profile, reservedTransports);
                    if (walkPlan != null) {
                        if (walkPlan.transport != null) { transportAssignments[walkPlan.transport] = walkPlan; reservedTransports.Add(walkPlan.transport); }
                        return AITransportPlanner.NextAction(walkPlan, map, Profile);
                    }
                    return AIAction.Wait(unit, "front-line supply: holds");
                }
                // Behind: a chain to the nearest front-line supply unit, of the others.
                HexTile to = null; int td = int.MaxValue;
                foreach (SupplyPoint f in frontHubs) if (f != null && f.myUnit != null && f.myUnit.tile != null) { int d = map.Travel(unit, unit.tile, f.myUnit.tile); if (d < td) { td = d; to = f.myUnit.tile; } }
                if (to != null) {
                    List<Unit> moved = new List<Unit>();
                    foreach (SupplyPoint h in map.ownHubs) if (h != null && h.myUnit != null && !h.myUnit.active) moved.Add(h.myUnit);
                    List<SupplyPoint> rear = new List<SupplyPoint>();
                    foreach (SupplyPoint h in map.ownHubs) if (h != null && !frontHubs.Contains(h)) rear.Add(h);
                    AISupplyChain.ReservedTrucks = new HashSet<Unit>(reservedTransports);
                    foreach (Unit t in transportAssignments.Keys) AISupplyChain.ReservedTrucks.Add(t);
                    AISupplyChain.Plan now;
                    try { now = AISupplyChain.Make(map, moved, to, rear, null); } finally { AISupplyChain.ReservedTrucks = null; }
                    AISupplyChain.Leap ride;
                    if (now.leaps.TryGetValue(hub, out ride)) {
                        transportAssignments[ride.truck] = new TransportPlan { cargo = unit, transport = ride.truck, rendezvous = ride.truck.tile, dropoff = ride.tile, destination = ride.tile, stayAboard = true };
                        reservedTransports.Add(ride.truck);
                        if (unit.trasnporter == ride.truck) return AIAction.Wait(unit, "supply to the front line: rides its truck to (" + ride.tile.x + "," + ride.tile.y + ")");
                        return AIAction.Board(unit, ride.truck, ride.board, "supply to the front line: boards a truck");
                    }
                    HexTile link;
                    if (unit.trasnporter == null && now.links.TryGetValue(hub, out link)) {
                        if (link == unit.tile) return AIAction.Wait(unit, "supply to the front line: holds this link");
                        return AIAction.Move(unit, link, 0f, "supply to the front line: link moves up (" + now.reachSteps + " steps short)");
                    }
                }
                // Not needed in the chain: another front-line supply unit.
                AIAction xa;
                if (FrontLineLeg(hub, out xa)) return xa;
                TransportPlan extra = AISupplyPlanner.PlanHub(hub, map, Profile, reservedTransports);
                if (extra != null) {
                    if (extra.transport != null) { transportAssignments[extra.transport] = extra; reservedTransports.Add(extra.transport); }
                    return AITransportPlanner.NextAction(extra, map, Profile);
                }
                return AIAction.Wait(unit, "supply: no better position");
            }
            if (Profile.supplyChain && !Profile.supplyTree && (unit.trasnporter == null || Profile.supplyLeaps) && job == null && logistics == null) {
                List<Unit> moved = new List<Unit>();
                foreach (SupplyPoint h in map.ownHubs) if (h != null && h.myUnit != null && !h.myUnit.active) moved.Add(h.myUnit);
                List<SupplyPoint> free = null;
                if (campaign != null) { free = new List<SupplyPoint>(); foreach (SupplyPoint h in map.ownHubs) if (campaign.ObjectiveOf(h) == null) free.Add(h); }
                if (Profile.supplyBackbone && backbone != null) { List<SupplyPoint> rest = new List<SupplyPoint>(); foreach (SupplyPoint h in (IEnumerable<SupplyPoint>)free ?? map.ownHubs) if (!backbone.links.ContainsKey(h)) rest.Add(h); free = rest; }
                AISupplyChain.ReservedTrucks = new HashSet<Unit>(reservedTransports);
                foreach (Unit t in transportAssignments.Keys) AISupplyChain.ReservedTrucks.Add(t);
                AISupplyChain.Plan now;
                try { now = AISupplyChain.Make(map, moved, null, free, null); } finally { AISupplyChain.ReservedTrucks = null; }
                AISupplyChain.Leap ride;
                if (now.leaps.TryGetValue(hub, out ride)) {
                    // A supply leap: into the truck (or staying in it), and the truck takes it to the far edge of supply.
                    transportAssignments[ride.truck] = new TransportPlan { cargo = unit, transport = ride.truck, rendezvous = ride.truck.tile, dropoff = ride.tile, destination = ride.tile, stayAboard = true };
                    reservedTransports.Add(ride.truck);
                    if (Trace != null) Trace("   supply leap: " + unit.name.Replace("(Clone)", "") + " with the truck at (" + ride.truck.tile.x + "," + ride.truck.tile.y + ") to (" + ride.tile.x + "," + ride.tile.y + "); supply " + now.reachSteps + " steps from the objective");
                    if (unit.trasnporter == ride.truck) return AIAction.Wait(unit, "supply chain: rides its truck to (" + ride.tile.x + "," + ride.tile.y + ")");
                    return AIAction.Board(unit, ride.truck, ride.board, "supply chain: boards a truck for a leap");
                }
                HexTile link;
                if (unit.trasnporter == null && now.links.TryGetValue(hub, out link)) {
                    if (link == unit.tile) return AIAction.Wait(unit, "supply chain: holds this link");
                    return AIAction.Move(unit, link, 0f, "supply chain: link moves up (supply " + now.reachSteps + " steps from the objective)");
                }
            }

            AIAction leap;
            if (Profile.supplyLeaps && SupplyLeap(hub, out leap)) return leap;

            TransportPlan plan = KeepAboard(AISupplyPlanner.PlanHub(hub, map, Profile, reservedTransports), unit);
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

        if (unit.trasnporter != null) {
            // Aboard with nowhere better to be carried: get off here.
            TransportPlan off = UnloadHere(unit);
            if (off != null) { transportAssignments[off.transport] = off; reservedTransports.Add(off.transport); }
            return AIAction.Wait(unit, "aboard transport");
        }

        PlanStep step;
        if (planned.TryGetValue(unit, out step) && Profile.coordinationPasses && step.group > 0 && step.kind != null && step.kind.StartsWith("battle")) {
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

        // A rider that can take a building beside its transport: put down onto it (the enemy HQ first).
        if (Transporting.IsTransport(unit) && unit.transported.Count > 0 && unit.tile != null) {
            foreach (Unit r in unit.transported) {
                if (r == null || r.Hub != null || !Building.CanCapture(r)) continue;
                HexTile best = null; bool bestHQ = false;
                foreach (HexTile n in Transporting.UnloadOptions(unit, r)) {
                    Building b = n.building;
                    if (b == null || b.owner == player || !b.Capturable) continue;
                    if (best == null || (b.IsHQ && !bestHQ) || (b.IsHQ == bestHQ && CombatMath.Precedes(n, best))) { best = n; bestHQ = b.IsHQ; }
                }
                if (best != null) return AIAction.Disembark(unit, r, best, "lets its rider off onto " + best.building.name.Replace("(Clone)", "") + " (" + best.x + "," + best.y + ") to take it");
            }
        }
        // An armed transport (a recon carrying a marine) whose rider asked to be put down here: it lets it off
        // first, then fights as it will. (A marine rode beside the enemy HQ for four turns, never let off.)
        // Riding to the assault (assaultRides): a transport with fighting units aboard and no supply duty.
        if (Profile.assaultRides && Transporting.IsTransport(unit) && !transportAssignments.ContainsKey(unit)) {
            List<Unit> riders = AIAssault.Riders(unit);
            if (riders.Count > 0) {
                Front rf = FrontOf(riders[0]);
                AIAssault.HasFront = rf != null;
                if (Trace != null) Trace("   assault ride: " + unit.name.Replace("(Clone)", "") + " with " + riders.Count + " aboard, front " + (rf != null ? rf.posture + " objective " + D(rf.objectiveTile) : "none"));
                // Toward the fight when their front attacks (the nearest enemy we know of); a front holding its ground
                // (or gathering): to its ground (its staging tile, else its objective), not out to the enemy.
                bool attacking = rf == null || (rf.posture == Posture.Aggressive && !rf.staging);
                HexTile goal = attacking ? (NearestEnemyTile(unit) ?? (rf != null ? rf.objectiveTile : null))
                                         : (rf.staging && rf.stagingTile != null ? rf.stagingTile : rf.objectiveTile);
                long __as = AIPerf.Begin();
                AIAction? ra = AIAssault.TransportAction(unit, goal, rf != null && rf.posture == Posture.Aggressive, map, Profile, !attacking);
                AIPerf.End("assault: transport with riders", __as);
                if (Trace != null) Trace("   assault ride: " + unit.name.Replace("(Clone)", "") + " goal " + D(goal) + (ra.HasValue ? " -> " + ra.Value.kind + " (" + ra.Value.reason + ")" : " -> nothing: decides as it would empty"));
                if (ra.HasValue) return ra.Value;
            }
        }
        if (Transporting.IsTransport(unit) && unit.Firepower > 1) {
            TransportPlan off;
            if (transportAssignments.TryGetValue(unit, out off) && off != null && off.cargo != null && off.cargo.trasnporter == unit && off.dropoff == unit.tile && !off.stayAboard)
                return AITransportPlanner.TransportAction(unit, off, map);
        }
        if (Transporting.IsTransport(unit) && unit.Firepower <= 1) {
            TransportPlan assignment;
            // A chain link's truck (AILogistics.chainTrucks) that comes before its link this turn: its link
            // decides now, which tells the truck where to meet it.
            if (!transportAssignments.ContainsKey(unit) && logistics != null && unit.transported.Count == 0)
                foreach (KeyValuePair<SupplyPoint, Unit> kv in logistics.chainTrucks) {
                    if (kv.Value != unit || kv.Key.myUnit == null || !kv.Key.myUnit.active || kv.Key.myUnit.tile == null) continue;
                    AIAction ignored;
                    LogisticsAction(kv.Key, out ignored);
                    break;
                }
            if (transportAssignments.TryGetValue(unit, out assignment)) {
                return AITransportPlanner.TransportAction(unit, assignment, map);
            }
            if (unit.transported.Count > 0) {
                // Carrying someone nobody planned for: put them down.
                TransportPlan drop = UnloadHere(unit.transported[0]);
                if (drop != null) return AITransportPlanner.TransportAction(unit, drop, map);
            }
            // An empty transport not held for supply goes back for more riders.
            if (Profile.assaultRides && unit.transported.Count == 0 && !supplyTransports.Contains(unit) && (logistics == null || !IsLogisticsTruck(unit))) {
                long __af = AIPerf.Begin();
                AIAction? fa = AIAssault.Fetch(unit, map, Profile, soughtRiders, Profile.seaLift ? (System.Func<Unit, HexTile>)GroundOf : null);
                AIPerf.End("assault: empty transport fetches", __af);
                if (fa.HasValue) return fa.Value;
            }
            // Held for supply with no ride planned: it keeps within a move of the supply units it could carry.
            if (Profile.assaultRides && unit.transported.Count == 0 && supplyTransports.Contains(unit)) {
                AIAction? keep = AIAssault.StayWithSupply(unit, map);
                if (keep.HasValue) return keep.Value;
            }
            return AITactical.DecideIdleTransport(unit, map, Profile);
        }

        // Its decision from the coordination pass, if nothing near it has changed since (Profile.coordinationReuse).
        AIAction early;
        if (firstLook.TryGetValue(unit, out early)) {
            firstLook.Remove(unit);
            string why = !Profile.coordinationReuse ? "reuse off" : noAssist.Contains(unit) ? "deciding again without assist credit" : planned.ContainsKey(unit) ? "in a battle plan" : null;
            if (Profile.coordinationReuse && !noAssist.Contains(unit) && !planned.ContainsKey(unit) && NothingNearChanged(unit, early, out why)) {
                if (VerifyReuse) {
                    AIAction fresh = AITactical.Decide(unit, map, Profile, posture, front, objective);
                    bool same = fresh.kind == early.kind && fresh.destination == early.destination && fresh.target == early.target;
                    reuseChecked++; if (!same) reuseDiffered++;
                    if (Trace != null && !same) Trace("   reuse check: " + unit.name.Replace("(Clone)", "") + D(unit.tile) + " pass 1 " + early.kind + " " + D(early.destination) + " vs now " + fresh.kind + " " + D(fresh.destination));
                }
                reused++;
                string said;
                if (AITactical.DecisionLog != null && firstLookLog.TryGetValue(unit, out said)) AITactical.DecisionLog(said + "\n      (reused from the coordination pass: nothing within its reach or the enemy's changed since)");
                if (early.destination != null && early.destination != unit.tile) intents[unit] = early.destination; else intents.Remove(unit);
                return early;
            }
            notReused++;
            string kind = why == null ? "?" : why.Contains(" near it changed") ? "a unit within reach changed" : why;
            int nk; notReusedWhy.TryGetValue(kind, out nk); notReusedWhy[kind] = nk + 1;
            if (Trace != null) Trace("   " + unit.name.Replace("(Clone)", "") + D(unit.tile) + " decides again: " + why);
        }
        return AITactical.Decide(unit, map, Profile, posture, front, objective);
    }

    /// <summary>
    /// The per-turn settings the AI's helpers read from thread-static fields (who the logistics protects, the chain's
    /// links), set again at the start of every thought: in the game each thought is a task of its own, and the thread
    /// that prepared the turn need not be the one that thinks next. (With the campaign on, a threaded game differed
    /// from an unthreaded one from turn 3: the later thoughts had none of these.)
    /// </summary>
    void ApplyTurnStatics() {
        HexTactica.AI.AILogistics.Protect = logistics != null ? logistics.dutied : null;
        AISupplyChain.ProtectOnly = HexTactica.AI.AILogistics.Protect;
        HexTactica.AI.AILogistics.ProtectUnitsNow = logistics != null ? logistics.protectUnits : null;
        AISupplyChain.ProtectUnits = HexTactica.AI.AILogistics.ProtectUnitsNow;
        AITransportPlanner.ChainLinks = logistics != null ? new HashSet<SupplyPoint>(logistics.chainOf.Keys) : null;
        AIWorld.Roles = roleWorth;
    }
    Dictionary<Unit, float> roleWorth;

    /// <summary>Decisions made in the coordination pass (AICoordinator), kept for the unit's turn, and their logged reasoning.</summary>
    readonly Dictionary<Unit, AIAction> firstLook = new Dictionary<Unit, AIAction>();
    readonly Dictionary<Unit, string> firstLookLog = new Dictionary<Unit, string>();
    /// <summary>Where every unit on the board stood, and its strength, when those decisions were made.</summary>
    Dictionary<Unit, KeyValuePair<HexTile, int>> firstBoard;
    /// <summary>Counts for the harness: decisions reused, made again, and (VerifyReuse) checked against a fresh one and found different.</summary>
    public static int reused, notReused, reuseChecked, reuseDiffered;
    public static readonly Dictionary<string, int> notReusedWhy = new Dictionary<string, int>();
    /// <summary>Harness: each reused decision is also made afresh and compared (costs the time reuse saves).</summary>
    public static bool VerifyReuse;

    static string D(HexTile t) { return t == null ? "-" : "(" + t.x + "," + t.y + ")"; }

    Dictionary<Unit, KeyValuePair<HexTile, int>> BoardMarks() {
        Dictionary<Unit, KeyValuePair<HexTile, int>> d = new Dictionary<Unit, KeyValuePair<HexTile, int>>();
        foreach (Unit u in map.ownUnits) if (u != null) d[u] = new KeyValuePair<HexTile, int>(u.tile, u.Strength);
        foreach (Unit u in map.enemyUnits) if (u != null) d[u] = new KeyValuePair<HexTile, int>(u.tile, u.Strength);
        return d;
    }

    /// <summary>
    /// Has nothing changed, since the coordination pass, within reach of <paramref name="unit"/>'s decision — its
    /// own move and fire, plus the farthest any enemy can move and fire — and is that decision still open (its
    /// tile free, its target where it was)?
    /// </summary>
    bool NothingNearChanged(Unit unit, AIAction early, out string why) {
        why = null;
        if (firstBoard == null) { why = "no record of the board"; return false; }
        KeyValuePair<HexTile, int> me;
        if (!firstBoard.TryGetValue(unit, out me) || me.Key != unit.tile || me.Value != unit.Strength) { why = "it has moved or been hurt"; return false; }
        int enemyReach = 0;
        foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null) enemyReach = Mathf.Max(enemyReach, (e.moveAndAttack ? e.moveRange : 0) + e.AttackRange);
        int r = unit.moveRange + Mathf.Max(1, unit.AttackRange) + enemyReach + 1;
        // reuseRadiusCap: only changes this close count (the unit's own reach plus one, at most the cap). Not the same
        // answers: a change farther off that the full radius would have caught is missed (VERIFYREUSE counts those).
        if (Profile.reuseRadiusCap > 0) r = Mathf.Min(Profile.reuseRadiusCap, unit.moveRange + Mathf.Max(1, unit.AttackRange) + 1);
        HexTile at = unit.tile;
        Func<HexTile, bool> near = t => t != null && t.Distance(at) <= r;
        foreach (KeyValuePair<Unit, KeyValuePair<HexTile, int>> kv in firstBoard) {
            Unit u = kv.Key;
            HexTile now = u != null && u.Strength > 0 ? u.tile : null;
            int str = u != null ? u.Strength : 0;
            if (now == kv.Value.Key && str == kv.Value.Value) continue;
            if (near(kv.Value.Key) || near(now)) { why = (u != null ? u.name.Replace("(Clone)", "") : "a unit") + " near it changed (" + D(kv.Value.Key) + " -> " + D(now) + ")"; return false; }
        }
        foreach (Unit u in map.enemyUnits) if (u != null && u.tile != null && !firstBoard.ContainsKey(u) && near(u.tile)) { why = "an enemy appeared near it at " + D(u.tile); return false; }
        foreach (Unit u in map.ownUnits) if (u != null && u.tile != null && !firstBoard.ContainsKey(u) && near(u.tile)) { why = "a new unit of ours near it at " + D(u.tile); return false; }
        if (early.destination != null && early.destination != unit.tile && early.destination.occupier != null) { why = "its tile is taken"; return false; }
        if (early.target != null && (early.target.Strength <= 0 || early.target.tile == null)) { why = "its target is gone"; return false; }
        return true;
    }

    /// <summary>
    /// A front-line supply unit (supplyFrontFirst) with a truck: the one it is aboard, or a free one it
    /// can board this turn. Of the tiles the truck can reach this turn (and stop on safely), the one
    /// where the supply unit aboard adds most — the army units it supplies, heals and spares, less what
    /// it would lose in the enemy's reply (AISupplyPlanner.ExactValue) — counting it linked wherever the
    /// truck could reach supplied ground in one move. It stays aboard there.
    /// </summary>
    bool FrontLineLeg(SupplyPoint hub, out AIAction action) {
        action = default(AIAction);
        Unit unit = hub.myUnit;
        Unit truck = unit.trasnporter;
        HexTile board = null;
        if (truck != null) { if (!Transporting.IsTransport(truck) || truck.Firepower > 1 || LocomotionRules.IsAirborne(truck.locomotion) || LocomotionRules.IsNaval(truck.locomotion)) return false; }
        else {
            MoveField walk = map.ReachThisTurn(unit);
            int bd = int.MaxValue;
            foreach (Unit t in map.ownUnits) {
                if (t == null || t.tile == null || !Transporting.IsTransport(t) || t.Firepower > 1 || t.transported.Count > 0 || !t.active) continue;
                if (LocomotionRules.IsAirborne(t.locomotion) || LocomotionRules.IsNaval(t.locomotion)) continue;
                if (reservedTransports.Contains(t) || transportAssignments.ContainsKey(t) || !Transporting.CanCarry(t, unit)) continue;
                HexTile b = null;
                if (Transporting.CanBoardFrom(t, unit)) b = unit.tile;
                else foreach (HexTile n in t.tile.neighbors) {
                    if (n == null || !walk.ReachableThisTurn(n) || !Transporting.CanBoardFromTile(t, unit, n)) continue;
                    if (b == null || CombatMath.Precedes(n, b)) b = n;
                }
                if (b == null) continue;
                int d = unit.tile.Distance(t.tile);
                if (d < bd) { bd = d; truck = t; board = b; }
            }
            if (truck == null) return false;
        }
        MoveField drive = map.ReachThisTurn(truck);
        float caution = Mathf.Max(1f, Profile.loadedTransportRisk);
        HexTile best = null; float bestV = float.NegativeInfinity;
        SupplyPoint wasA = SupplyNetwork.AssumeLinked; Unit wasV = SupplyNetwork.AssumeLinkedVia;
        SupplyNetwork.AssumeLinked = hub; SupplyNetwork.AssumeLinkedVia = truck;
        try {
            foreach (HexTile x in drive.Tiles) {
                if (x != truck.tile && (!drive.ReachableThisTurn(x) || !AIPath.CanFinishTurnOn(truck, x) || x.occupier != null)) continue;
                if (map.ExpectedLoss(truck, x) * caution >= truck.Strength) continue;
                float v = AISupplyPlanner.ExactValue(hub, x, map, Profile) - AIScore.Risk(truck, x, map, Profile) * caution;
                if (v > bestV || (v == bestV && best != null && CombatMath.Precedes(x, best))) { bestV = v; best = x; }
            }
        } finally { SupplyNetwork.AssumeLinked = wasA; SupplyNetwork.AssumeLinkedVia = wasV; }
        if (best == null) return false;
        transportAssignments[truck] = new TransportPlan { cargo = unit, transport = truck, rendezvous = truck.tile, dropoff = best, destination = best, stayAboard = true };
        reservedTransports.Add(truck);
        if (unit.trasnporter == truck) { action = AIAction.Wait(unit, "front-line supply: its truck to (" + best.x + "," + best.y + ")"); return true; }
        action = AIAction.Board(unit, truck, board, "front-line supply: boards a truck for (" + best.x + "," + best.y + ")");
        return true;
    }

    /// <summary>
    /// A supply leap (profile.supplyLeaps): a supply unit the chain has no place for crosses the
    /// edge of supply in a truck and stays aboard, linked as long as the truck could drive back
    /// onto supplied ground in one move. Its own supply then spreads from the truck, and the chain's
    /// next links are put down at its edge. On the zigzag, five supply units on foot reached 54 steps
    /// short of the enemy HQ and stopped; a truck carries the network a truck's move further each time.
    ///
    /// Aboard already: the truck goes to the tile furthest toward the chain's objective that keeps it
    /// linked, safe tonight (loadedTransportRisk), and not ahead of the army. On foot: if a free truck is where it can
    /// board this turn, and such a tile adds to the chain's reach, it boards and the truck drives.
    /// </summary>
    bool SupplyLeap(SupplyPoint hub, out AIAction action) {
        action = default(AIAction);
        Unit unit = hub.myUnit;
        if (unit == null || unit.tile == null || hub.producer) return false;
        HexTile objective = AISupplyChain.Objective(map);
        if (objective == null) return false;
        Dictionary<HexTile, int> toObj = map.LandSteps(objective);
        System.Func<HexTile, int> steps = t => { int v; return t != null && toObj.TryGetValue(t, out v) ? v : int.MaxValue; };
        SupplyNetwork.Result without = SupplyNetwork.SolveWithout(player, hub);
        int reach = int.MaxValue;
        foreach (KeyValuePair<HexTile, int> kv in without.depth) if (kv.Value > 0) { int v = steps(kv.Key); if (v < reach) reach = v; }
        if (reach == 0 || reach == int.MaxValue) return false;
        int front = int.MaxValue;
        foreach (Unit u in map.ownUnits) if (u != null && u.tile != null && u.Firepower > 0 && u.Hub == null && !LocomotionRules.NeedsBase(u.locomotion)) front = Mathf.Min(front, steps(u.tile));

        Unit truck = unit.trasnporter;
        HexTile beside = null;
        if (truck == null) {
            // A free truck it can reach and board this turn.
            MoveField walk = map.ReachThisTurn(unit);
            int bestD = int.MaxValue;
            foreach (Unit t in map.ownUnits) {
                if (t == null || t.tile == null || !Transporting.IsTransport(t) || t.Firepower > 1 || t.transported.Count > 0 || !t.active) continue;
                if (reservedTransports.Contains(t) || transportAssignments.ContainsKey(t) || !Transporting.CanCarry(t, unit)) continue;
                HexTile b = null;
                if (Transporting.CanBoardFrom(t, unit)) b = unit.tile;
                else foreach (HexTile n in t.tile.neighbors) {
                    if (n == null || !walk.ReachableThisTurn(n) || !Transporting.CanBoardFromTile(t, unit, n)) continue;
                    if (b == null || CombatMath.Precedes(n, b)) b = n;
                }
                if (b == null) continue;
                int d = unit.tile.Distance(t.tile);
                if (d < bestD) { bestD = d; truck = t; beside = b; }
            }
            if (truck == null) return false;
        } else if (!Transporting.IsTransport(truck) || truck.Firepower > 1 || truck.transported.Count > 1) return false;

        // Where the truck can be tonight with the supply unit aboard and still linked.
        MoveField drive = map.ReachThisTurn(truck);
        float caution = Mathf.Max(1f, Profile.loadedTransportRisk);
        HexTile best = null; int bestSteps = int.MaxValue;
        foreach (HexTile x in drive.Tiles) {
            if (x != truck.tile && (!drive.ReachableThisTurn(x) || !AIPath.CanFinishTurnOn(truck, x))) continue;
            if (x != truck.tile && x.occupier != null) continue;
            int sx = steps(x);
            if (sx == int.MaxValue || sx >= bestSteps) continue;
            // Supply serves the army: never ahead of it.
            if (sx < front) continue;
            if (map.ExpectedLoss(truck, x) * caution >= truck.Strength || map.ExpectedLoss(truck, x) > Mathf.Max(0f, Profile.chainRiskTolerance)) continue;
            // Linked: the truck could reach supplied ground from there in one move.
            bool linked = false;
            foreach (HexTile s in AIPath.TilesInRangeShared(x, 0, Mathf.Max(0, truck.moveRange))) {
                if (without.DepthAt(s) <= 0) continue;
                if (map.Travel(truck, x, s) <= truck.moveRange) { linked = true; break; }
            }
            if (!linked) continue;
            best = x; bestSteps = sx;
        }
        // Worth it only if its supply from there reaches further than the network does without it.
        if (best == null || bestSteps - Mathf.Max(0, hub.range - 1) >= reach) return false;
        if (unit.trasnporter == truck && best == truck.tile) {
            transportAssignments[truck] = new TransportPlan { cargo = unit, transport = truck, dropoff = best, rendezvous = truck.tile, stayAboard = true };
            reservedTransports.Add(truck);
            action = AIAction.Wait(unit, "supply leap: aboard at the far edge of supply");
            return true;
        }
        TransportPlan plan = new TransportPlan { cargo = unit, transport = truck, rendezvous = truck.tile, dropoff = best, destination = best, stayAboard = true, eta = 0 };
        transportAssignments[truck] = plan;
        reservedTransports.Add(truck);
        if (Trace != null) Trace("   supply leap: " + unit.name.Replace("(Clone)", "") + " in the truck at (" + truck.tile.x + "," + truck.tile.y + ") to (" + best.x + "," + best.y + "), " + bestSteps + " steps from the objective (supply reaches " + reach + " without it)");
        if (unit.trasnporter == truck) { action = AIAction.Wait(unit, "supply leap: riding"); return true; }
        action = AIAction.Board(unit, truck, beside, "supply leap: board");
        return true;
    }

    /// <summary>
    /// profile.combatRides: a fighting unit out of contact (or already riding) asks the walk-or-ride
    /// planner for the quickest way to the supplied ground nearest its objective. When the answer is
    /// a ride, the unit heads for the rendezvous (or boards, or stays aboard) and the transport is
    /// given the plan; when it is a walk and the unit is aboard, it gets off. Otherwise the unit
    /// decides as usual.
    /// </summary>
    /// <summary>Could <paramref name="unit"/> be offered a ride this turn (TryRide's first checks)?</summary>
    bool MayRide(Unit unit) {
        if (unit == null || unit.tile == null || unit.Firepower <= 0 || unit.Hub != null) return false;
        if (unit.trasnporter != null) return true;
        return map.TurnsToContact(unit) > Profile.objectiveContactTurns && Transporting.AvailableFor(player, unit).Count > 0;
    }

    bool TryRide(Unit unit, Front front, out AIAction action) {
        action = default(AIAction);
        if (unit.tile == null || unit.Hub != null) return false;
        bool aboard = unit.trasnporter != null;
        string who = unit.name.Replace("(Clone)", "");
        if (!aboard && map.TurnsToContact(unit) <= Profile.objectiveContactTurns) return false;
        if (!aboard && Transporting.AvailableFor(player, unit).Count == 0) { if (Trace != null) Trace("   " + who + " ride: no transport can take it"); return false; }
        // Where it is going: its own errand (a building to take, ground to look at) if it has one, else its front's objective.
        Objective errand;
        HexTile objectiveTile = assignments != null && assignments.TryGetValue(unit, out errand) && errand.tile != null && (errand.kind == ObjectiveKind.Capture || errand.kind == ObjectiveKind.Scout) ? errand.tile
                              : front != null && front.objectiveTile != null ? front.objectiveTile : AISupplyChain.Objective(map);
        if (objectiveTile == null) return false;
        int from = map.Travel(unit, unit.tile, objectiveTile);
        float worth = AIScore.Worth(unit);
        float per = worth * Mathf.Max(0.05f, Profile.marchWeight);
        List<HexTile> candidates = new List<HexTile>();
        foreach (HexTile t in HexGenerator.hg.tiles) if (t != null && (map.SupplyDepth(t) > 0 || t == unit.tile) && unit.CanEnter(t)) candidates.Add(t);
        System.Func<HexTile, float> valueOf = t => per * (from - map.Travel(unit, t, objectiveTile)) - AIScore.Risk(unit, t, map, Profile);
        // Assault rides own the unarmed transports: this ride uses only armed ones.
        HashSet<Unit> barred = reservedTransports;
        if (Profile.assaultRides) {
            barred = new HashSet<Unit>(reservedTransports);
            foreach (Unit t in player.units) if (t != null && Transporting.IsTransport(t) && t.Firepower <= 1) barred.Add(t);
        }
        TransportPlan plan = AITransportPlanner.Plan(unit, map, Profile, valueOf, candidates, barred);
        if (plan == null) { if (Trace != null) Trace("   " + unit.name.Replace("(Clone)", "") + " ride: no plan (" + candidates.Count + " supplied tiles)"); return false; }
        if (!plan.usesTransport) {
            if (Trace != null && !aboard) Trace("   " + unit.name.Replace("(Clone)", "") + " ride: walking is as good: " + plan);
            if (!aboard) return false;   // walking: the ordinary decision does that better
            action = AITransportPlanner.NextAction(plan, map, Profile);
            return true;
        }
        transportAssignments[plan.transport] = plan;
        reservedTransports.Add(plan.transport);
        action = AITransportPlanner.NextAction(plan, map, Profile);
        if (Trace != null) Trace("   " + unit.name.Replace("(Clone)", "") + " rides: " + plan);
        return true;
    }

    /// <summary>
    /// A chain link aboard a transport is not set down (AITransportPlanner.KeepsAboard): whatever
    /// <paramref name="plan"/> wanted for it, its transport drives it there and it stays aboard (the transport
    /// stops on the destination, or the free tile beside it nearest its way). Other cargo: the plan as it was.
    /// </summary>
    TransportPlan KeepAboard(TransportPlan plan, Unit cargo) {
        if (!AITransportPlanner.KeepsAboard(cargo)) return plan;
        Unit truck = cargo.trasnporter;
        HexTile want = plan != null ? (plan.transport == truck && plan.dropoff != null ? plan.dropoff : plan.destination) : null;
        HexTile stop = truck.tile;
        if (want != null && want != truck.tile) {
            if (truck.CanEnter(want) && (want.occupier == null || want.occupier == truck)) stop = want;
            else {
                HexTile best = null;
                foreach (HexTile n in want.neighbors) {
                    if (n == null || (n.occupier != null && n.occupier != truck) || !truck.CanEnter(n)) continue;
                    if (best == null || map.Travel(truck, truck.tile, n) < map.Travel(truck, truck.tile, best) || (map.Travel(truck, truck.tile, n) == map.Travel(truck, truck.tile, best) && CombatMath.Precedes(n, best))) best = n;
                }
                if (best != null) stop = best;
            }
        }
        return new TransportPlan { cargo = cargo, transport = truck, rendezvous = truck.tile, dropoff = stop, destination = stop, stayAboard = true, value = plan != null ? plan.value : 0f, score = plan != null ? plan.score : 0f };
    }

    /// <summary>A plan that drops cargo next to wherever its transport is now (never a chain link: AITransportPlanner.KeepsAboard).</summary>
    TransportPlan UnloadHere(Unit cargo) {
        Unit transport = cargo != null ? cargo.trasnporter : null;
        if (transport == null || transport.tile == null) return null;
        if (AITransportPlanner.KeepsAboard(cargo)) return null;
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
        // In play, the next side starts once our last moves and shots have played (headless: at once).
        GameController.gc.EndTurnWhenStill();
    }

    /// <summary>Exposed for tests and the harness: the AI's current read of the board.</summary>
    public AIMap CurrentMap { get { return map; } }
    public List<Front> CurrentFronts { get { return fronts; } }
    public List<Objective> CurrentObjectives { get { return objectives; } }
    public AICampaign CurrentCampaign { get { return campaign; } }
    public HexTactica.AI.AILogistics CurrentLogistics { get { return logistics; } }

    /// <summary>
    /// A supply unit's move under the campaign's logistics (AILogistics): a link of a front's chain
    /// goes to its link (walking, or by truck on a leap), planned again with the links that have
    /// already moved where they are; one of a front's own heads to the front, and once within two of
    /// its moves of the front's units is placed as any supply unit is, to serve the units around it.
    /// False: not one of the logistics' (spare, or no fronts).
    /// </summary>
    /// <summary>
    /// cutOffSeeksSupply. A supply unit on foot not joined to the network: into an empty transport beside it that can carry it,
    /// else to the tile it can reach this turn nearest supplied ground (on it, if one is in reach), avoiding known fire. Null:
    /// joined, or no tile gets it nearer. (Cut-off supply units were marked spare and sent toward their front; nothing walked
    /// them back first, and they stayed cut off.)
    /// </summary>
    AIAction? SeekSupply(Unit unit) {
        SupplyNetwork.Result r = SupplyNetwork.SolveAll(player, new Dictionary<SupplyPoint, HexTile>());
        if (r.connectedHubs.Contains(unit.Hub)) return null;
        // Into any transport with room it can reach this turn: aboard, a supply unit counts as linked within a move of
        // supplied ground, two to four times its reach on foot. (ChanceMeeting: every supply unit starts cut off; walked
        // back toward the HQ, none got into the trucks waiting a move away.)
        MoveField reach = map.ReachThisTurn(unit);
        Unit ride = null; HexTile from = null; int rideD = int.MaxValue;
        foreach (Unit t in player.units) {
            if (t == null || t.tile == null || t == unit || !Transporting.IsTransport(t)) continue;
            if (!Transporting.CarriesKind(t, unit) || Transporting.FreeWeight(t) < Mathf.Max(1, unit.weight)) continue;
            HexTile at = null;
            if (Transporting.CanBoardFrom(t, unit)) at = unit.tile;
            else foreach (HexTile n in t.tile.neighbors) {
                if (n == null || !reach.ReachableThisTurn(n) || !Transporting.CanBoardFromTile(t, unit, n)) continue;
                if (at == null || CombatMath.Precedes(n, at)) at = n;
            }
            if (at == null) continue;
            int d = unit.tile.Distance(t.tile);
            if (d < rideD || (d == rideD && ride != null && AIMap.CompareUnits(t, ride) < 0)) { rideD = d; ride = t; from = at; }
        }
        if (ride != null) return AIAction.Board(unit, ride, from, "cut off: boards " + ride.name.Replace("(Clone)", "") + " (" + ride.tile.x + "," + ride.tile.y + ") to get back to supply");
        List<HexTile> supplied = new List<HexTile>();
        foreach (HexTile x in HexGenerator.hg.tiles) if (x != null && map.SupplyDepth(x) > 0) supplied.Add(x);
        if (supplied.Count == 0) return null;
        System.Func<HexTile, int> gap = x => { if (map.SupplyDepth(x) > 0) return 0; int m = int.MaxValue; foreach (HexTile y in supplied) m = Mathf.Min(m, x.Distance(y)); return m; };
        int now = gap(unit.tile);
        MoveField field = reach;
        HexTile best = null; int bg = now;
        foreach (HexTile x in field.Tiles) {
            if (x == unit.tile || !field.ReachableThisTurn(x) || (x.occupier != null && x.occupier != unit) || !unit.CanEnter(x) || !AIAir.MayEndOn(unit, x, map)) continue;
            if (map.KnownLoss(unit, x) > Mathf.Max(0f, Profile.chainRiskTolerance)) continue;
            int g = gap(x);
            if (g < bg || (g == bg && best != null && CombatMath.Precedes(x, best))) { bg = g; best = x; }
        }
        if (best == null) return null;
        return AIAction.Move(unit, best, 0f, "cut off: back toward supplied ground (" + now + " tiles away, now " + bg + ")");
    }

    /// <summary>Would <paramref name="hub"/> moving to <paramref name="to"/> cut off another supply unit joined to the network now?</summary>
    bool CutsOff(SupplyPoint hub, HexTile to) {
        if (to == null || to == hub.myUnit.tile) return false;
        return AISupplyChain.Harm(new Dictionary<SupplyPoint, HexTile> { { hub, to } }, map) > 0;
    }

    /// <summary>Supply units relieved this turn by an idle one that took over their job (AIPlayer.Relieve): where each moves up to.</summary>
    readonly Dictionary<SupplyPoint, HexTile> relieved = new Dictionary<SupplyPoint, HexTile>();
    /// <summary>Chain links another supply unit is on its way to relieve this turn (one each).</summary>
    readonly HashSet<SupplyPoint> sought = new HashSet<SupplyPoint>();

    /// <summary>
    /// An idle supply unit (no duty) beside a busy one takes over its job: a tile it can reach this turn
    /// from which, with the busy one stepping toward its front, nobody is cut off (AISupplyChain.Harm);
    /// the busy one is then relieved to make that step. False: no such relay.
    /// </summary>
    bool Relieve(SupplyPoint hub, out AIAction action) {
        List<SupplyPoint> busy = new List<SupplyPoint>();
        foreach (SupplyPoint x in logistics.dutied) if (!logistics.chainOf.ContainsKey(x) && logistics.tacticalOf.ContainsKey(x)) busy.Add(x);
        return Relieve(hub, busy, out action);
    }

    /// <summary>
    /// Relieve one of <paramref name="busy"/> (the nearest that can be): a tile <paramref name="hub"/> can reach this
    /// turn from which, with that one making its next step up, nobody is cut off (AISupplyChain.Harm). A front's
    /// own supply unit is then told to make that step (relieved); a link of a chain moves up when the chain is
    /// planned again with this one in place.
    /// </summary>
    bool Relieve(SupplyPoint hub, List<SupplyPoint> busy, out AIAction action) {
        action = default(AIAction);
        Unit unit = hub.myUnit;
        List<SupplyPoint> near = new List<SupplyPoint>();
        foreach (SupplyPoint x in busy)
            if (x != hub && x.myUnit != null && x.myUnit.tile != null && x.myUnit.active && !relieved.ContainsKey(x)
                && x.myUnit.tile.Distance(unit.tile) <= 2 * Mathf.Max(1, unit.moveRange)) near.Add(x);
        near.Sort((a, b) => { int c = a.myUnit.tile.Distance(unit.tile).CompareTo(b.myUnit.tile.Distance(unit.tile)); return c != 0 ? c : AIMap.CompareUnits(a.myUnit, b.myUnit); });
        MoveField walk = map.ReachThisTurn(unit);
        foreach (SupplyPoint x in near) {
            HexTile up = NextStepUp(x);
            if (up == null) continue;
            HexTile from = x.myUnit.trasnporter != null ? x.myUnit.trasnporter.tile : x.myUnit.tile;
            List<HexTile> spots = new List<HexTile>();
            foreach (HexTile s in AIPath.TilesInRangeShared(from, 0, Mathf.Max(1, hub.range)))
                if (s != null && (s == unit.tile || (s.occupier == null && s != up && walk.ReachableThisTurn(s)))) spots.Add(s);
            spots.Sort((a, b) => { int c = a.Distance(from).CompareTo(b.Distance(from)); return c != 0 ? c : CombatMath.CompareTiles(a, b); });
            foreach (HexTile s in spots) {
                if (AISupplyChain.Harm(new Dictionary<SupplyPoint, HexTile> { { hub, s }, { x, up } }, map) > 0) continue;
                relieved[x] = up;   // one relief each; it makes that step when it moves
                string why = "logistics: takes over from the supply unit at (" + from.x + "," + from.y + "), which moves up";
                action = s == unit.tile ? AIAction.Wait(unit, why) : AIAction.Move(unit, s, 0f, why);
                return true;
            }
        }
        return false;
    }

    /// <summary>The next step up for supply unit <paramref name="x"/>: toward its front's goal; by its truck if aboard.</summary>
    HexTile NextStepUp(SupplyPoint x) {
        HexTactica.AI.AILogistics.Task t;
        HexTile goal = null;
        // Up: toward where its front is going (its nearest unit may be behind it: one relieved walked home).
        if (logistics.chainOf.TryGetValue(x, out t) || logistics.tacticalOf.TryGetValue(x, out t)) goal = t.goal;
        if (goal == null) return null;
        Unit truck = x.myUnit.trasnporter;
        if (truck != null) {
            HexTile step = AITransportPlanner.SafeStepToward(truck, goal, map.ReachHorizon(truck), map, true);
            return step != null && step != truck.tile ? step : null;
        }
        AIAction a = AISupplyTree.Step(x.myUnit, goal, map);
        return a.destination != null && a.destination != x.myUnit.tile ? a.destination : null;
    }

    /// <summary>reserveDemand: how short the central reserve is of campaignReserveShare of the army, when no task force has engaged units.</summary>
    void SetReserveDemand() {
        AIProduction.ReserveRally = null; AIProduction.ReserveShort = 0f;
        if (!Profile.reserveDemand || campaign == null) return;
        foreach (HexTactica.AI.CampaignObjective o in campaign.objectives)
            if (o.kind != HexTactica.AI.CampaignKind.Scout && o.kind != HexTactica.AI.CampaignKind.DefendHQ && o.engaged.Count > 0) return;   // active opposition: the fronts come first
        if (logistics != null && logistics.tasks.Exists(x => x.short_)) return;   // a front short of supply comes first
        float army = 0f, reserve = 0f;
        foreach (Unit u in map.ownUnits) if (u != null && u.tile != null && u.Hub == null && u.Firepower > 0) army += HexTactica.AI.AIScore.Worth(u) * u.Strength;
        Front r = campaign.reserveFront;
        if (r != null) foreach (Unit u in r.assigned) if (u != null && u.tile != null && u.Hub == null && u.Firepower > 0) reserve += HexTactica.AI.AIScore.Worth(u) * u.Strength;
        float want = army * Mathf.Clamp01(Profile.campaignReserveShare);
        if (want <= 0f || reserve >= want) return;
        Building hq = GameController.HQOf(player);
        AIProduction.ReserveRally = r != null && r.objectiveTile != null ? r.objectiveTile : hq != null ? hq.tile : null;
        AIProduction.ReserveShort = (want - reserve) / want;
    }

    bool LogisticsAction(SupplyPoint hub, out AIAction action) {
        action = default(AIAction);
        Unit unit = hub.myUnit;
        // A rider got in this turn for the ride planned when the turn began (supplySpareSeat): that ride is kept, not re-planned
        // into a hold (the rider was let out again having gone nowhere).
        if (Profile.supplySpareSeat && unit.trasnporter != null && unit.trasnporter.transported.Exists(c => c != null && c.Hub == null && boardedThisTurn.Contains(c))) {
            HexTile to;
            Unit truck = unit.trasnporter;
            if ((riderRide.TryGetValue(truck, out to) || SupplyGoing().TryGetValue(truck, out to)) && to != null && to != truck.tile) {
                transportAssignments[truck] = new TransportPlan { cargo = unit, transport = truck, rendezvous = truck.tile, dropoff = to, destination = to, stayAboard = true };
                reservedTransports.Add(truck);
                action = AIAction.Wait(unit, "logistics: rides its truck to (" + to.x + "," + to.y + ") as planned, with the rider that got in for it");
                return true;
            }
        }
        // Relieved by an idle one that took over its job: it moves up toward its front.
        HexTile upTo;
        if (unit.trasnporter == null && relieved.TryGetValue(hub, out upTo) && upTo.occupier == null && !CutsOff(hub, upTo)) {
            action = AIAction.Move(unit, upTo, 0f, "logistics: relieved, moves up toward its front");
            return true;
        }
        HexTactica.AI.AILogistics.Task t;
        if (logistics.chainOf.TryGetValue(hub, out t)) {
            List<Unit> moved = new List<Unit>();
            foreach (SupplyPoint h in t.chain) if (h != null && h.myUnit != null && !h.myUnit.active) moved.Add(h.myUnit);
            HashSet<Unit> busy = new HashSet<Unit>(reservedTransports);
            foreach (Unit tr in transportAssignments.Keys) busy.Add(tr);
            AISupplyChain.Plan now = HexTactica.AI.AILogistics.Chain(map, t, t.chain, moved, busy, new HashSet<SupplyPoint>(t.chain));
            AISupplyChain.Leap ride;
            // A rider took this transport's spare seat this turn (supplySpareSeat): it is not used to hold where it stands
            // (the rider was let out again having gone nowhere).
            if (now.leaps.TryGetValue(hub, out ride) && ride.truck != null && ride.tile == ride.truck.tile
                && ride.truck.transported.Exists(c => c != null && c.Hub == null && boardedThisTurn.Contains(c))) ride = null;
            if (ride != null) {
                transportAssignments[ride.truck] = new TransportPlan { cargo = unit, transport = ride.truck, rendezvous = ride.truck.tile, dropoff = ride.tile, destination = ride.tile, stayAboard = true };
                reservedTransports.Add(ride.truck);
                if (unit.trasnporter == ride.truck) { action = AIAction.Wait(unit, "logistics for " + t.front.Id + ": rides its truck to (" + ride.tile.x + "," + ride.tile.y + ")"); return true; }
                action = AIAction.Board(unit, ride.truck, ride.board, "logistics for " + t.front.Id + ": boards a truck to (" + ride.tile.x + "," + ride.tile.y + ")");
                return true;
            }
            HexTile link;
            bool isLink = now.links.TryGetValue(hub, out link);
            // Its own truck (AILogistics.chainTrucks): on board as soon as it can reach it, the truck taking it
            // where it would have walked (further next turn, as a leap); else the truck comes to meet it.
            Unit mine;
            if (unit.trasnporter == null && logistics.chainTrucks.TryGetValue(hub, out mine) && mine != null && mine.tile != null && !mine.transported.Exists(c => c != null && c.Hub != null)) {
                reservedTransports.Add(mine);
                MoveField walk = map.ReachThisTurn(unit);
                HexTile beside = null;
                if (Transporting.CanBoardFrom(mine, unit)) beside = unit.tile;
                else foreach (HexTile n in mine.tile.neighbors) {
                    if (n == null || !walk.ReachableThisTurn(n) || !Transporting.CanBoardFromTile(mine, unit, n)) continue;
                    if (beside == null || CombatMath.Precedes(n, beside)) beside = n;
                }
                HexTile drop0 = isLink && link != unit.tile && mine.CanEnter(link) && link.occupier == null ? link : mine.tile;
                // A rider took its spare seat this turn: not boarded to hold where it stands (the rider would be let out again).
                if (beside != null && drop0 == mine.tile && mine.transported.Exists(c => c != null && c.Hub == null && boardedThisTurn.Contains(c))) beside = null;
                if (beside != null) {
                    HexTile drop = drop0;
                    transportAssignments[mine] = new TransportPlan { cargo = unit, transport = mine, rendezvous = mine.tile, dropoff = drop, destination = drop, stayAboard = true };
                    action = AIAction.Board(unit, mine, beside, "logistics for " + t.front.Id + ": chain link boards its truck (to (" + drop.x + "," + drop.y + "))");
                    return true;
                }
                HexTile end = isLink ? link : unit.tile;
                HexTile meet = null;
                foreach (HexTile n in end.neighbors) {
                    if (n == null || (n.occupier != null && n.occupier != mine) || !mine.CanEnter(n)) continue;
                    if (meet == null || map.Travel(mine, mine.tile, n) < map.Travel(mine, mine.tile, meet) || (map.Travel(mine, mine.tile, n) == map.Travel(mine, mine.tile, meet) && CombatMath.Precedes(n, meet))) meet = n;
                }
                if (meet != null) transportAssignments[mine] = new TransportPlan { cargo = unit, transport = mine, rendezvous = meet, dropoff = meet, destination = meet, stayAboard = true };
                if (Trace != null) Trace("   chain link at (" + unit.tile.x + "," + unit.tile.y + "): its truck at (" + mine.tile.x + "," + mine.tile.y + ") comes to meet it" + (meet != null ? " at (" + meet.x + "," + meet.y + ")" : " (no tile beside it)"));
            }
            if (unit.trasnporter == null && isLink) {
                action = link == unit.tile ? AIAction.Wait(unit, "logistics for " + t.front.Id + ": holds this link")
                                           : AIAction.Move(unit, link, 0f, "logistics for " + t.front.Id + ": link moves up (" + now.reachSteps + " steps short)");
                return true;
            }
            // Aboard its truck, the chain gave it no leap: it holds aboard, as a link where its truck stands.
            if (unit.trasnporter != null && HexTactica.AI.AITransportPlanner.KeepsAboard(unit)) {
                transportAssignments[unit.trasnporter] = new TransportPlan { cargo = unit, transport = unit.trasnporter, rendezvous = unit.trasnporter.tile, dropoff = unit.trasnporter.tile, destination = unit.trasnporter.tile, stayAboard = true };
                reservedTransports.Add(unit.trasnporter);
                action = AIAction.Wait(unit, "logistics for " + t.front.Id + ": chain link holds aboard its truck");
                return true;
            }
            // Not a link after all this turn (the others moved first): one of the front's own instead.
        }
        if (t == null) logistics.tacticalOf.TryGetValue(hub, out t);
        if (t == null) return false;
        // A front's own supply unit with its front in the fight (campaignReleaseTurns): it moves as it will —
        // placed by what it would keep supplied (AISupplyPlanner) — not by the logistics. The chain keeps its
        // own logic; the front only tells it where its units need supply (AILogistics.Goal).
        if (!logistics.chainOf.ContainsKey(hub) && Profile.campaignReleaseTurns > 0 && t.front.units.Exists(u => u != null && u.tile != null && map.TurnsToContact(u) <= Profile.campaignReleaseTurns)) return false;
        // Far from its front with a truck claimed for it: on board, and the truck takes it there.
        HexTactica.AI.AILogistics.Ride claim;
        if (logistics.rides.TryGetValue(hub, out claim) && claim.truck != null && claim.truck.tile != null && claim.truck.tile != claim.to) {
            Unit truck = claim.truck;
            TransportPlan go = new TransportPlan { cargo = unit, transport = truck, rendezvous = truck.tile, dropoff = claim.to, destination = claim.to, stayAboard = true };
            if (unit.trasnporter == truck) { transportAssignments[truck] = go; action = AIAction.Wait(unit, "logistics for " + t.front.Id + ": rides its truck to (" + claim.to.x + "," + claim.to.y + ")"); return true; }
            if (unit.trasnporter == null) {
                MoveField walk = map.ReachThisTurn(unit);
                HexTile beside = null;
                if (Transporting.CanBoardFrom(truck, unit)) beside = unit.tile;
                else foreach (HexTile n in truck.tile.neighbors) {
                    if (n == null || !walk.ReachableThisTurn(n) || !Transporting.CanBoardFromTile(truck, unit, n)) continue;
                    if (beside == null || CombatMath.Precedes(n, beside)) beside = n;
                }
                if (beside != null) { transportAssignments[truck] = go; action = AIAction.Board(unit, truck, beside, "logistics for " + t.front.Id + ": boards a truck to (" + claim.to.x + "," + claim.to.y + ")"); return true; }
                // The truck comes to it; meanwhile it does not wait, it walks that way (the truck meets it).
                HexTile meet = null;
                foreach (HexTile n in unit.tile.neighbors) if (n != null && (n.occupier == null || n.occupier == truck) && truck.CanEnter(n) && (meet == null || map.Travel(truck, truck.tile, n) < map.Travel(truck, truck.tile, meet))) meet = n;
                if (meet != null) transportAssignments[truck] = new TransportPlan { cargo = unit, transport = truck, rendezvous = meet, dropoff = claim.to, destination = claim.to, stayAboard = true };
                AIAction toward = AISupplyTree.Step(unit, claim.to, map);
                if (!toward.IsNothing && !CutsOff(hub, toward.destination)) { toward.reason = "logistics for " + t.front.Id + ": walks toward (" + claim.to.x + "," + claim.to.y + "), its truck coming"; action = toward; return true; }
            }
        }
        // The front's chain short: to the chain's supply unit nearest it, to relieve that one of its place so it
        // can move up. (Sent to "the back of the chain", supply units went to whichever end that was that turn.)
        if (t.gap && unit.trasnporter == null && t.chain.Count > 0) {
            // Only a middle link is relieved (one with a link nearer the front beyond it: HQ > link > link > end):
            // the chain's end is joined, by going on past it to supply the front's units as the new end.
            Dictionary<HexTile, int> toGoal = t.goal != null ? map.LandSteps(t.goal) : null;
            System.Func<SupplyPoint, HexTile> at = x => x.myUnit.trasnporter != null ? x.myUnit.trasnporter.tile : x.myUnit.tile;
            System.Func<SupplyPoint, int> ahead = x => { int v; return toGoal != null && toGoal.TryGetValue(at(x), out v) ? v : int.MaxValue; };
            List<SupplyPoint> links = new List<SupplyPoint>();
            foreach (SupplyPoint x in t.chain) if (x != hub && x.myUnit != null && x.myUnit.tile != null) links.Add(x);
            SupplyPoint end = null;
            foreach (SupplyPoint x in links) if (end == null || ahead(x) < ahead(end) || (ahead(x) == ahead(end) && AIMap.CompareUnits(x.myUnit, end.myUnit) < 0)) end = x;
            List<SupplyPoint> middle = links.FindAll(x => x != end);
            SupplyPoint closest = null; int cd = int.MaxValue;
            foreach (SupplyPoint x in links) {
                if (x != end && (sought.Contains(x) || relieved.ContainsKey(x))) continue;   // one on its way to relieve it already
                int dd = map.Travel(unit, unit.tile, at(x));
                if (dd < cd || (dd == cd && closest != null && AIMap.CompareUnits(x.myUnit, closest.myUnit) < 0)) { cd = dd; closest = x; }
            }
            if (closest != null && closest == end) {
                HexTile lead;
                int fd = HexTactica.AI.AILogistics.NearestOf(t, unit, map, out lead);
                if (lead != null && fd > Mathf.Max(1, hub.range)) {
                    AIAction go = AISupplyTree.Step(unit, lead, map);
                    if (go.destination != null && go.destination != unit.tile && !CutsOff(hub, go.destination)) { go.reason = "logistics for " + t.front.Id + ": past the chain's end at (" + at(end).x + "," + at(end).y + ") to supply the front's units, its new end"; action = go; return true; }
                }
            } else if (closest != null) {
                if (Relieve(hub, middle, out action)) { action.reason += " (" + t.front.Id + "'s chain)"; return true; }
                if (cd > Mathf.Max(1, hub.range)) {
                    AIAction go = AISupplyTree.Step(unit, at(closest), map);
                    if (go.destination != null && go.destination != unit.tile && !CutsOff(hub, go.destination)) { sought.Add(closest); go.reason = "logistics for " + t.front.Id + ": to relieve the chain's supply unit at (" + at(closest).x + "," + at(closest).y + ")"; action = go; return true; }
                }
            }
        }
        // A tail: near its front but supplying nothing of any front's or in contact (no duty) — it pulls back
        // toward the nearest supply unit that does, where the chain takes it in next turn and that one is
        // free to move up.
        float myDuty;
        logistics.duty.TryGetValue(hub, out myDuty);
        if (myDuty <= 0f && unit.trasnporter == null && !logistics.dutied.Contains(hub) && !HexTactica.AI.AILogistics.Far(t, unit, map)) {
            HexTile to = null; int td = int.MaxValue;
            foreach (SupplyPoint x in logistics.dutied) {
                if (x == hub || x.myUnit == null || x.myUnit.tile == null) continue;
                int dd = map.Travel(unit, unit.tile, x.myUnit.tile);
                if (dd < td || (dd == td && to != null && CombatMath.Precedes(x.myUnit.tile, to))) { td = dd; to = x.myUnit.tile; }
            }
            if (to != null && td > Mathf.Max(1, hub.range)) {
                AIAction back = AISupplyTree.Step(unit, to, map);
                if (!back.IsNothing && !CutsOff(hub, back.destination)) { back.reason = "logistics for " + t.front.Id + ": supplies no one, pulls back to the supply unit at (" + to.x + "," + to.y + ")"; action = back; return true; }
            }
            // Beside a busy one: it takes over that one's job and the busy one moves up.
            if (Relieve(hub, out action)) return true;
            // None to take over from: up to its front's units, as front-line supply.
            HexTile front;
            int fd = HexTactica.AI.AILogistics.NearestOf(t, unit, map, out front);
            if (front != null && fd > Mathf.Max(1, hub.range)) {
                AIAction up = AISupplyTree.Step(unit, front, map);
                if (up.destination != null && up.destination != unit.tile && !CutsOff(hub, up.destination)) { up.reason = "logistics for " + t.front.Id + ": supplies no one, up to the front as its supply (" + fd + " away)"; action = up; return true; }
            }
        }
        HexTile near;
        int d = HexTactica.AI.AILogistics.NearestOf(t, unit, map, out near);
        if (near != null && d > 2 * Mathf.Max(1, unit.moveRange)) {
            if (unit.trasnporter != null) {
                if (AITransportPlanner.KeepsAboard(unit)) {
                    // A chain link aboard: its truck takes it toward the front.
                    TransportPlan go = KeepAboard(new TransportPlan { cargo = unit, destination = near }, unit);
                    transportAssignments[go.transport] = go; reservedTransports.Add(go.transport);
                    action = AIAction.Wait(unit, "logistics for " + t.front.Id + ": its truck takes it to the front (" + d + " away)");
                    return true;
                }
                TransportPlan off = UnloadHere(unit);
                if (off != null) { transportAssignments[off.transport] = off; reservedTransports.Add(off.transport); }
                action = AIAction.Wait(unit, "logistics for " + t.front.Id + ": off the truck to walk to the front");
                return true;
            }
            action = AISupplyTree.Step(unit, near, map);
            if (!action.IsNothing && !CutsOff(hub, action.destination)) { action.reason = "logistics for " + t.front.Id + ": heads to the front (" + d + " away)"; return true; }
        }
        TransportPlan plan = KeepAboard(AISupplyPlanner.PlanHub(hub, map, Profile, reservedTransports), unit);
        if (plan == null && unit.trasnporter != null) plan = UnloadHere(unit);
        if (plan != null) {
            if (plan.transport != null) { transportAssignments[plan.transport] = plan; reservedTransports.Add(plan.transport); }
            action = AITransportPlanner.NextAction(plan, map, Profile);
            return true;
        }
        action = AIAction.Wait(unit, "logistics for " + t.front.Id + ": its own supply, no better position");
        return true;
    }
    public Dictionary<Unit, Objective> CurrentAssignments { get { return assignments; } }
}
