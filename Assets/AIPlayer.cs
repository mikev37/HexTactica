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
public class AIPlayer : MonoBehaviour {

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
    public float ThinkingFor { get { return thinking != null && !thinking.IsCompleted ? Time.unscaledTime - thinkingSince : -1f; } }
    int thinkingGeneration, generation;
    /// <summary>The last action changed the board: the next thought reads it again first (AIMap.RefreshAfterAction).</summary>
    bool refreshPending;

    void Update() {
        if (GameController.gc == null || GameController.gc.headless) return;
        if (GameController.gc.gameOver) return;
        if (LevelEditor.singleton != null &&
            (LevelEditor.singleton.editing || LevelEditor.singleton.unit_placement)) return;
        if (!GameController.gc.activePlayer.Equals(player)) { turnPrepared = false; Abandon(); return; }

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
        SupplyNetwork.PutDownHypothetical = Profile.hubLinkPenalty > 0f;
        using (v.Enter()) {
            if (prepare) PrepareTurnOnBoard();
            if (refresh && map != null) {
                long __f = AIPerf.Begin();
                map.RefreshAfterAction();
                AIPerf.End("act: board re-read (RefreshAfterAction)", __f);
            }
            while (queue.Count > 0) {
                Unit unit = queue[0];
                queue.RemoveAt(0);
                if (unit == null || unit.Strength <= 0 || unit.tile == null) continue;
                if (!unit.active) continue;
                long __d = AIPerf.Begin();
                AIAction action = DecideFor(unit);
                AIPerf.End("decide: " + (unit.Hub != null ? "supply unit" : Transporting.IsTransport(unit) && unit.Firepower <= 1 ? "transport" : "fighting unit"), __d);
                action = KeepLinks(action);
                // Aboard and nothing to do but ride: the turn is not spent. The unit is set aside,
                // still fresh, and decides again if its transport puts it down this turn (Run).
                if (Profile.freshUnload && unit.trasnporter != null && action.kind == AIActionKind.Wait) {
                    if (onDecision != null) onDecision(unit, action, false);
                    if (Trace != null) Trace("   " + player.personal_name + " " + unit.name.Replace("(Clone)", "") + ": aboard, decides again if put down — " + action.reason);
                    continue;
                }
                return new Thought { unit = unit, action = action };
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
            int lost = AISupplyChain.Harm(mv, map);
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
                if (logistics == null) BuySupply();
                if (Profile.buyUnits && map != null) {
                    List<Unit> bought = AIProduction.Buy(player, map, Profile, Trace, logistics);
                    if (onBuy != null) foreach (Unit u in bought) onBuy(u);
                }
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
            AIPerf.End("act: execute", __x);
            // Cargo put down that has not acted this turn: it decides next, from where it landed
            // (getting off costs nothing: it moves, attacks or captures as any fresh unit would).
            if (Profile.freshUnload && did && action.kind == AIActionKind.Unload && action.target != null && action.target.active
                && action.target.trasnporter == null && action.target.tile != null && !queue.Contains(action.target))
                queue.Insert(0, action.target);
            if (did) {
                // Any action can change what every other unit can reach, where supply
                // flows, and who the enemy can fire on: a tile was vacated or filled, a unit
                // died in a corridor, a hub moved. Conflict and distress are recomputed too.
                long __f = AIPerf.Begin();
                unit.FinishActive();
                AIPerf.End("act: FinishActive (threat, resupply)", __f);
                // The next thought reads the board again first (on its own copy).
                refreshPending = true;
                // The game's supply borders and alarms read the published supply: the AI's thoughts
                // publish only into their copies, so publish the real fields after its action.
                if (!headless && GameController.gc != null) foreach (Player p in GameController.gc.players) if (p != null) p.Resupply();
            }

            Annotate(unit, action);
            if (onDecision != null) onDecision(unit, action, did);
            if (Trace != null)
                Trace("   " + player.personal_name + " " + unit.name.Replace("(Clone)", "") + (wasAt != null ? " (" + wasAt.x + "," + wasAt.y + ")" + (unit.tile != null && unit.tile != wasAt ? "->(" + unit.tile.x + "," + unit.tile.y + ")" : "") : "") + ": " + action.kind + (did ? "" : " (not executed)") + " — " + action.reason);

            // A unit that waited changed nothing, so there is nothing to recalculate.
            unit.active = false;
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
        foreach (Unit b in hq.buildable) {
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
    BoardView NewView() {
        long __v = AIPerf.Begin();
        BoardView v = FogOfWar.Conceal(player, true, Profile.fogMemoryTurns) ?? BoardView.Snapshot();
        AIPerf.End("main thread: copy the board", __v);
        return v;
    }

    void PrepareTurnOnBoard() {
        { long __p = AIPerf.Begin();
        map = new AIMap(player, Profile);
        AIPerf.End("prepare: board read (AIMap)", __p); }

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
        logistics = campaign != null && Profile.campaignLogistics && !Profile.supplyFrontFirst ? HexTactica.AI.AILogistics.Make(map, campaign, null, fronts) : null;
        if (logistics != null && Trace != null) Trace("   " + logistics);
        HexTactica.AI.AILogistics.Protect = logistics != null ? logistics.dutied : null;
        AISupplyChain.ProtectOnly = HexTactica.AI.AILogistics.Protect;
        HexTactica.AI.AILogistics.ProtectUnitsNow = logistics != null ? logistics.protectUnits : null;
        AISupplyChain.ProtectUnits = HexTactica.AI.AILogistics.ProtectUnitsNow;
        { long __p = AIPerf.Begin();
        objectives = AIObjectives.Build(map, Profile);
        AIPerf.End("prepare: objectives", __p); }
        assignments = Profile.useObjectives ? AIObjectives.Assign(objectives, map, Profile) : new Dictionary<Unit, Objective>();
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
        // The trucks the logistics claimed for the fronts' own supply units: theirs before any fighting
        // unit picks a ride (a fighting unit may ride along, below).
        if (logistics != null) foreach (HexTactica.AI.AILogistics.Ride r in logistics.rides.Values) reservedTransports.Add(r.truck);

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
        if (Profile.combatRides) foreach (Unit u in queue) if (MayRide(u)) { anyRider = true; break; }
        if (anyRider) {
            List<Unit> carriers = new List<Unit>();
            foreach (Unit u in queue) if (Transporting.IsTransport(u) && u.Firepower <= 1) carriers.Add(u);
            foreach (Unit u in carriers) { queue.Remove(u); queue.Add(u); }
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
        long __c = AIPerf.Begin();
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
        AIPerf.End("prepare: coordinated plans", __c);
        // Aircraft landing this turn act last (after any coordinated plan too), once whatever was on their tiles has left.
        List<Unit> landingNow = new List<Unit>();
        foreach (AIAir.Landing l in map.landings.Values) if (l.turn == 1 && queue.Contains(l.unit)) landingNow.Add(l.unit);
        landingNow.Sort(AIMap.CompareUnits);
        foreach (Unit u in landingNow) { queue.Remove(u); queue.Add(u); }
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
        AIAction a = DecideForUnit(unit);
        // A decision path that does not go through the tactical choice (which already leaves out tiles
        // reserved for a landing this turn) chose one: the unit decides again the ordinary way, so an
        // aircraft parked there lifts off to whatever mission it would pick anyway.
        if (map != null && unit != null && unit.tile != null && !AIAir.MayEndOn(unit, AIAir.EndOf(unit, a), map)) {
            Front f = AIStrategic.FrontFor(unit, fronts);
            AIWorld.NoAssist = noAssist.Count > 0 ? noAssist : null;
            a = AITactical.Decide(unit, map, Profile, f != null ? f.posture : Posture.Defensive, f, objective);
        }
        // Aircraft and ships never end a turn where they cannot get home from.
        return map != null && unit != null ? AIAir.Guard(unit, a, map) : a;
    }

    AIAction DecideForUnit(Unit unit) {
        if (unit == null) return AIAction.Wait(null, "null");
        AIWorld.NoAssist = noAssist.Count > 0 ? noAssist : null;
        AITactical.Intents = intents;
        AITactical.Assignments = assignments;

        Front front = AIStrategic.FrontFor(unit, fronts);
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
        // a front's own supply units to its front, with room for both, gets in if the truck sets down
        // nearer its own goal than it could walk; the truck lets it out where it stops.
        if (logistics != null && unit.Firepower > 0 && unit.Hub == null && unit.trasnporter == null
            && unit.locomotion == Locomotion.Boots && map.TurnsToContact(unit) > Profile.objectiveContactTurns) {
            HexTile goal = front != null && front.objectiveTile != null ? front.objectiveTile : AISupplyChain.Objective(map);
            Dictionary<HexTile, int> toGoal = goal != null ? map.LandSteps(goal) : null;
            int mine;
            if (toGoal != null && toGoal.TryGetValue(unit.tile, out mine))
                foreach (KeyValuePair<SupplyPoint, HexTactica.AI.AILogistics.Ride> kv in logistics.rides) {
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

        // A fighting unit out of contact (an objective's or not): ride a transport toward the
        // objective if that is quicker than walking.
        if (Profile.combatRides && unit.Firepower > 0 && unit.Hub == null) {
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
                        if (n == null || n.occupier != null || !walk.ReachableThisTurn(n) || !Transporting.CanBoardOnto(unit, n, truck.tile, truck)) continue;
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
                    if (n == null || n.occupier != null || !walk.ReachableThisTurn(n) || !Transporting.CanBoardOnto(unit, n, t.tile, t)) continue;
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
                    if (n == null || n.occupier != null || !walk.ReachableThisTurn(n) || !Transporting.CanBoardOnto(unit, n, t.tile, t)) continue;
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
        TransportPlan plan = AITransportPlanner.Plan(unit, map, Profile, valueOf, candidates, reservedTransports);
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
    public AICampaign CurrentCampaign { get { return campaign; } }

    /// <summary>
    /// A supply unit's move under the campaign's logistics (AILogistics): a link of a front's chain
    /// goes to its link (walking, or by truck on a leap), planned again with the links that have
    /// already moved where they are; one of a front's own heads to the front, and once within two of
    /// its moves of the front's units is placed as any supply unit is, to serve the units around it.
    /// False: not one of the logistics' (spare, or no fronts).
    /// </summary>
    /// <summary>Would <paramref name="hub"/> moving to <paramref name="to"/> cut off another supply unit joined to the network now?</summary>
    bool CutsOff(SupplyPoint hub, HexTile to) {
        if (to == null || to == hub.myUnit.tile) return false;
        return AISupplyChain.Harm(new Dictionary<SupplyPoint, HexTile> { { hub, to } }, map) > 0;
    }

    bool LogisticsAction(SupplyPoint hub, out AIAction action) {
        action = default(AIAction);
        Unit unit = hub.myUnit;
        HexTactica.AI.AILogistics.Task t;
        if (logistics.chainOf.TryGetValue(hub, out t)) {
            List<Unit> moved = new List<Unit>();
            foreach (SupplyPoint h in t.chain) if (h != null && h.myUnit != null && !h.myUnit.active) moved.Add(h.myUnit);
            HashSet<Unit> busy = new HashSet<Unit>(reservedTransports);
            foreach (Unit tr in transportAssignments.Keys) busy.Add(tr);
            AISupplyChain.Plan now = HexTactica.AI.AILogistics.Chain(map, t, t.chain, moved, busy, new HashSet<SupplyPoint>(t.chain));
            AISupplyChain.Leap ride;
            if (now.leaps.TryGetValue(hub, out ride)) {
                transportAssignments[ride.truck] = new TransportPlan { cargo = unit, transport = ride.truck, rendezvous = ride.truck.tile, dropoff = ride.tile, destination = ride.tile, stayAboard = true };
                reservedTransports.Add(ride.truck);
                if (unit.trasnporter == ride.truck) { action = AIAction.Wait(unit, "logistics for " + t.front.Id + ": rides its truck to (" + ride.tile.x + "," + ride.tile.y + ")"); return true; }
                action = AIAction.Board(unit, ride.truck, ride.board, "logistics for " + t.front.Id + ": boards a truck to (" + ride.tile.x + "," + ride.tile.y + ")");
                return true;
            }
            HexTile link;
            if (unit.trasnporter == null && now.links.TryGetValue(hub, out link)) {
                action = link == unit.tile ? AIAction.Wait(unit, "logistics for " + t.front.Id + ": holds this link")
                                           : AIAction.Move(unit, link, 0f, "logistics for " + t.front.Id + ": link moves up (" + now.reachSteps + " steps short)");
                return true;
            }
            // Not a link after all this turn (the others moved first): one of the front's own instead.
        }
        if (t == null) logistics.tacticalOf.TryGetValue(hub, out t);
        if (t == null) return false;
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
                    if (n == null || n.occupier != null || !walk.ReachableThisTurn(n) || !Transporting.CanBoardOnto(unit, n, truck.tile, truck)) continue;
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
        // The front's chain short: to the back of the chain (AILogistics.DropNear), to be its next rear link.
        if (t.gap && unit.trasnporter == null) {
            HexTile back = HexTactica.AI.AILogistics.DropNear(t, unit, map);
            if (back != null) {
                if (back == unit.tile) { action = AIAction.Wait(unit, "logistics for " + t.front.Id + ": at the back of the chain, to join it"); return true; }
                action = AISupplyTree.Step(unit, back, map);
                if (!action.IsNothing && !CutsOff(hub, action.destination)) { action.reason = "logistics for " + t.front.Id + ": to the back of the chain at (" + back.x + "," + back.y + ")"; return true; }
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
        }
        HexTile near;
        int d = HexTactica.AI.AILogistics.NearestOf(t, unit, map, out near);
        if (near != null && d > 2 * Mathf.Max(1, unit.moveRange)) {
            if (unit.trasnporter != null) {
                TransportPlan off = UnloadHere(unit);
                if (off != null) { transportAssignments[off.transport] = off; reservedTransports.Add(off.transport); }
                action = AIAction.Wait(unit, "logistics for " + t.front.Id + ": off the truck to walk to the front");
                return true;
            }
            action = AISupplyTree.Step(unit, near, map);
            if (!action.IsNothing && !CutsOff(hub, action.destination)) { action.reason = "logistics for " + t.front.Id + ": heads to the front (" + d + " away)"; return true; }
        }
        TransportPlan plan = AISupplyPlanner.PlanHub(hub, map, Profile, reservedTransports);
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
