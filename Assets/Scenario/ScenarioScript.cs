using System;
using System.Collections.Generic;
using Defective.JSON;
using UnityEngine;

/// <summary>
/// A scenario's script: its triggers (when this happens, do these things), run while the game is played. One per game
/// scene (made when the scene opens). Its triggers are part of the scenario file ("script"): loading a scenario or a
/// save replaces them, and saving writes them (a save with their progress). Edit them in the inspector: open the
/// scenario in the map editor, select "Scenario Script" in the Hierarchy, and save the scenario as usual.
///
/// How it runs (every frame of play, after the pre-game screen, never while a menu is open):
/// <list type="bullet">
/// <item>Facts that happen and are gone (a unit killed, a building taken, a turn over, a unit damaged) are counted, as
///   they happen, on every trigger that asks about them (its Events), including triggers switched off (parts).</item>
/// <item>Then every trigger switched on is evaluated in order: by default it fires once; Delay is how many turns (of
///   TurnsOf) its condition must hold without a break; Repeat fires it again each time it is met anew, at most once in
///   RepeatEvery turns.</item>
/// <item>A trigger that fires runs its actions in order. A Dialogue action's lines are queued (ScenarioDialogue) and
///   the next action waits until they are closed; a Wait action waits that long. Win or Lose ends the game, and
///   evaluation stops for the frame.</item>
/// </list>
/// Everything it reads of the game and does to it goes through ScenarioGame.
/// </summary>
public class ScenarioScript : MonoBehaviour {

    [Tooltip("The scenario's triggers, evaluated in this order.")]
    public List<ScenarioTrigger> triggers = new List<ScenarioTrigger>();

    [Header("The scenario's words")]
    [Tooltip("What the scenario is about: shown when it is picked in the menus (saved in the file as \"description\").")]
    [TextArea(2, 6)] public string description = "";
    [Tooltip("Shown on the post-game screen, above the game's findings, when the player wins.")]
    [TextArea(2, 6)] public string winText = "";
    [Tooltip("Shown on the post-game screen, above the game's findings, when the player loses.")]
    [TextArea(2, 6)] public string lossText = "";

    [HideInInspector, Tooltip("The scenario file these triggers were loaded from (for the log).")]
    public string scenarioPath = "";

    /// <summary>A side of the scenario, for the inspector's side choices (from the file's players, or the game's).</summary>
    [Serializable] public class KnownSide { public long id; public string name; }
    [HideInInspector] public List<KnownSide> sides = new List<KnownSide>();

    // ---- progress in a game (saved in a game: ScenarioScriptData) ----
    [HideInInspector] public bool started;
    [HideInInspector] public float clock;
    [HideInInspector] public List<ShownMarker> markers = new List<ShownMarker>();
    [HideInInspector] public List<RunningActions> running = new List<RunningActions>();

    public static ScenarioScript Instance { get; private set; }

    /// <summary>The scene's script, made if there is none yet.</summary>
    public static ScenarioScript Current {
        get {
            if (Instance == null) {
                ScenarioScript found = FindAnyObjectByType<ScenarioScript>();
                if (found != null) found.Register();
                else new GameObject("Scenario Script").AddComponent<ScenarioScript>().Register();
            }
            return Instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook() { GameScene.WhenLoaded(() => { ScenarioScript s = Current; }); }

    bool registered;

    void Awake() { Register(); }

    void Register() {
        if (Instance != null && Instance != this) return;
        Instance = this;
        if (registered) return;
        registered = true;
        Unit.Died += OnDied;
        Building.OwnerChanged += OnOwnerChanged;
        Building.BuildingDestroyed += OnBuildingDestroyed;
        BuildingController.Produced += OnProduced;
        GameController.TurnEnding += OnTurnEnding;
        GameController.TurnStarted += OnTurnStarted;
        HexTile.UnitSelected += OnUnitSelected;
        HexTile.BuildingOpened += OnBuildingOpened;
        HexTile.MoveProposed += OnMoveProposed;
        HexTile.EnemyInspected += OnEnemyInspected;
        HexTile.AttackProposed += OnAttackProposed;
        Unit.Attacked += OnAttacked;
        Transporting.Boarded += OnBoarded;
        Transporting.Unloaded += OnUnloaded;
        Strikes.Fired += OnStrikeFired;
        Strikes.Landed += OnStrikeLanded;
    }

    void OnDestroy() {
        if (registered) {
            Unit.Died -= OnDied;
            Building.OwnerChanged -= OnOwnerChanged;
            Building.BuildingDestroyed -= OnBuildingDestroyed;
            BuildingController.Produced -= OnProduced;
            GameController.TurnEnding -= OnTurnEnding;
            GameController.TurnStarted -= OnTurnStarted;
            HexTile.UnitSelected -= OnUnitSelected;
            HexTile.BuildingOpened -= OnBuildingOpened;
            HexTile.MoveProposed -= OnMoveProposed;
            HexTile.EnemyInspected -= OnEnemyInspected;
            HexTile.AttackProposed -= OnAttackProposed;
            Unit.Attacked -= OnAttacked;
            Transporting.Boarded -= OnBoarded;
            Transporting.Unloaded -= OnUnloaded;
            Strikes.Fired -= OnStrikeFired;
            Strikes.Landed -= OnStrikeLanded;
            registered = false;
        }
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ every frame

    void Update() {
        GameController gc = GameController.gc;
        if (gc == null || gc.headless) return;   // the headless harness calls Tick itself
        Tick(Time.unscaledDeltaTime);
    }

    /// <summary>The script plays: a game is on, no menu is open over it (the pre-game screen, the map editor, the in-game menu), and it is not a replay.</summary>
    public static bool Playing {
        get {
            GameController gc = GameController.gc;
            return gc != null && !GameController.MenuHeld && !HexTactica.Harness.Replay.Active;
        }
    }

    /// <summary>One frame of play: <paramref name="dt"/> seconds more on the level clock, what happened since, the actions under way, then every trigger.</summary>
    public void Tick(float dt) {
        if (!Playing) return;
        GameController gc = GameController.gc;
        if (!started) {
            started = true; clock = 0f;
            // The game opens in the first side's turn without a turn start: it is counted here, so turn numbers
            // (Turn Started, Delay) count that first turn too.
            if (gc.activePlayer != null) OnTurnStarted(gc.activePlayer);
        }
        else clock += Mathf.Max(0f, dt);
        if (!snapshotTaken) Snapshot(); else Observe();
        FollowMarkers();
        RunActions();
        if (gc.gameOver) return;
        Evaluate();
    }

    /// <summary>The level clock: seconds of play since the scenario started (not while a menu is open).</summary>
    public float Clock { get { return clock; } }

    // The board has been animating since (level clock); -1: it is still.
    float busySince = -1f;
    bool busy;
    int busySignature;

    /// <summary>Which units are animating, and what each is doing (its reason's first word): changes as an exchange goes on.</summary>
    static int AnimatingSignature() {
        int h = Strikes.Showing ? 17 : 0;
        if (UnitController.uc == null) return h;
        foreach (Unit u in UnitController.uc.units) {
            if (u == null) continue;
            string r = u.AnimatingReason;
            if (r == null) continue;
            int sp = r.IndexOf(' ');
            h = h * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(u) * 7 + (sp < 0 ? r : r.Substring(0, sp)).GetHashCode();
        }
        return h;
    }

    void Evaluate() {
        GameController gc = GameController.gc;
        // Still animating? (Never for more than a few seconds: an animation that never ends must not hold the script.)
        // (Headless, nothing plays: a unit's animation flags are never cleared, so they are not waited for.)
        // The cap counts from the last change in what is playing (a walk, then the shots, then a retreat is one long
        // exchange, not a stuck animation): only the same thing playing for 6 s is given up on.
        if (!gc.headless && Unit.AnyAnimating()) {
            int now = AnimatingSignature();
            if (busySince < 0f || now != busySignature) { busySince = clock; busySignature = now; }
            busy = clock - busySince < 6f;
        }
        else { busySince = -1f; busy = false; }
        for (int i = 0; i < triggers.Count; i++) {
            ScenarioTrigger t = triggers[i];
            if (t == null || !t.Enabled) continue;
            if (!t.Repeat && t.TimesFired > 0) continue;                                          // once by default
            int turns = TurnsOf(t.TurnsOf);
            if (t.Repeat && t.TimesFired > 0 && turns - t.LastFiredTurn < t.RepeatEvery) continue;
            if (!IsMet(t, 0)) { t.ConditionMetTurn = -1; continue; }                               // Delay counts unbroken holding
            if (t.ConditionMetTurn < 0) t.ConditionMetTurn = turns;
            if (turns - t.ConditionMetTurn < t.Delay) continue;
            if (t.WaitForAnimations && busy) continue;                                            // after the shot lands, the retreat ends ...
            Fire(i);
            if (gc.gameOver) return;                                                              // a Win / Lose ends evaluation
        }
    }

    // ------------------------------------------------------------------ conditions

    const int MaxDepth = 8;

    /// <summary>Is <paramref name="t"/>'s condition met now? (Parts naming each other in a loop are not met, past <see cref="MaxDepth"/>.)</summary>
    public bool IsMet(ScenarioTrigger t, int depth) {
        if (t == null) return false;
        int count = Mathf.Max(1, t.Count);
        UnitFilter uf = UnitFilter.Of(t);
        switch (t.Condition) {
            case TriggerCondition.LevelStart: return started;
            case TriggerCondition.UnitEnters:
            case TriggerCondition.UnitKilled:
            case TriggerCondition.BuildingCaptured:
            case TriggerCondition.BuildingDestroyed:
            case TriggerCondition.UnitBuilt:
            case TriggerCondition.UnitSelected:
            case TriggerCondition.MoveProposed:
            case TriggerCondition.EnemyInspected:
            case TriggerCondition.AttackProposed:
            case TriggerCondition.Ambush:
            case TriggerCondition.UnitMoved:
            case TriggerCondition.BuildingSelected:
            case TriggerCondition.UnitSpotted:
            case TriggerCondition.UnitBoarded:
            case TriggerCondition.UnitUnloaded:
            case TriggerCondition.StrikeLaunched:
            case TriggerCondition.StrikeLanded:
            case TriggerCondition.UnitDamaged:
            case TriggerCondition.UnitHealed:
            case TriggerCondition.TurnOver:
            case TriggerCondition.TurnStarted:
                return t.Events >= count;
            case TriggerCondition.DayReached:
                return ScenarioGame.Day >= t.Value && (t.Side == TriggerSide.Any || (GameController.gc != null && ScenarioGame.SideIs(GameController.gc.activePlayer, t.Side)));
            case TriggerCondition.UnitsInArea: return ScenarioGame.CountUnits(uf) >= count;
            case TriggerCondition.MoneyAtLeast:
                foreach (Player p in ScenarioGame.Players) if (ScenarioGame.SideIs(p, t.Side) && !p.eliminated && p.money >= t.Value) return true;
                return false;
            case TriggerCondition.MoneyAtMost:
                foreach (Player p in ScenarioGame.Players) if (ScenarioGame.SideIs(p, t.Side) && !p.eliminated && p.money <= t.Value) return true;
                return false;
            case TriggerCondition.UnitStarving: return ScenarioGame.CountUnits(uf, ScenarioGame.Starving) >= count;
            case TriggerCondition.UnitOutOfAmmo: return ScenarioGame.CountUnits(uf, u => u.OutOfAmmo) >= count;
            case TriggerCondition.UnitOutOfFuel: return ScenarioGame.CountUnits(uf, u => u.OutOfFuel) >= count;
            case TriggerCondition.UnitLowOnFuel: { int pc = t.Value; return ScenarioGame.CountUnits(uf, u => ScenarioGame.LowOnFuel(u, pc)) >= count; }
            case TriggerCondition.UnitCutOff: return ScenarioGame.CountUnits(uf, ScenarioGame.CutOff) >= count;
            case TriggerCondition.UnitsAtMost: return ScenarioGame.CountUnits(uf) <= t.Value;
            case TriggerCondition.UnitsAtLeast: return ScenarioGame.CountUnits(uf) >= t.Value;
            case TriggerCondition.UnitSpent: return ScenarioGame.CountUnits(uf, u => !u.active) >= count;
            case TriggerCondition.BuildingOwned: {
                BuildingFilter bf = BuildingFilter.Of(t);
                int n = 0;
                foreach (Building b in Building.all) if (ScenarioGame.Matches(b, bf, false) && !b.Destroyed && ScenarioGame.SideIs(b.RealOwner, t.Side)) n++;
                return n >= count;
            }
            case TriggerCondition.AfterTrigger: {
                ScenarioTrigger other = Find(t.AfterTrigger);
                if (other == null) { Warn(t, "After Trigger names '" + t.AfterTrigger + "', which this scenario does not have: it is never met."); return false; }
                return other.TimesFired > 0;
            }
            case TriggerCondition.AllOf:
            case TriggerCondition.AnyOf:
            case TriggerCondition.NoneOf: {
                if (depth >= MaxDepth) { Warn(t, "its parts name each other in a loop (or nest deeper than " + MaxDepth + "): not met."); return false; }
                if (t.Parts == null || t.Parts.Count == 0) { Warn(t, "it has no parts: never met."); return false; }
                bool any = false, all = true;
                foreach (string name in t.Parts) {
                    ScenarioTrigger part = Find(name);
                    if (part == null) { Warn(t, "its part '" + name + "' is not a trigger of this scenario: counted as not met."); all = false; continue; }
                    if (part == t) { Warn(t, "it names itself as a part: counted as not met."); all = false; continue; }
                    bool met = IsMet(part, depth + 1);
                    any |= met; all &= met;
                }
                return t.Condition == TriggerCondition.AllOf ? all : t.Condition == TriggerCondition.AnyOf ? any : !any;
            }
        }
        return false;
    }

    /// <summary>The trigger named <paramref name="name"/> (the first of that name), or null.</summary>
    public ScenarioTrigger Find(string name) {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (ScenarioTrigger t in triggers) if (t != null && t.Name == name) return t;
        return null;
    }

    readonly HashSet<string> warned = new HashSet<string>();

    /// <summary>Say once (per trigger and message) why a trigger does not do what it looks like it should: a trigger that silently never fires is the worst failure.</summary>
    void Warn(ScenarioTrigger t, string why) {
        string key = (t != null ? t.Name : "") + "|" + why;
        if (!warned.Add(key)) return;
        Debug.LogWarning("Scenario trigger '" + (t != null ? t.Name : "?") + "': " + why);
    }

    // ------------------------------------------------------------------ facts as they happen (latched on each trigger)

    /// <summary>A unit as the last frame saw it; <c>seenBy</c>: which players (by their place in GameController.players) had it in sight.</summary>
    struct Seen { public int strength; public HexTile tile; public int pass; public long seenBy; }
    readonly Dictionary<Unit, Seen> seen = new Dictionary<Unit, Seen>();
    readonly List<Unit> gone = new List<Unit>();
    bool snapshotTaken;
    int pass;

    /// <summary>Facts are counted only in play (not while loading, previewing or editing).</summary>
    bool Counting { get { return started && Playing; } }

    void Count(TriggerCondition kind, Func<ScenarioTrigger, bool> matches) { Count(kind, matches, null); }

    /// <summary>One more event of <paramref name="kind"/> for every trigger it matches, about <paramref name="subject"/> (what "{unit}" names in their actions; null: no unit).</summary>
    void Count(TriggerCondition kind, Func<ScenarioTrigger, bool> matches, Unit subject) {
        for (int i = 0; i < triggers.Count; i++) {
            ScenarioTrigger t = triggers[i];
            if (t != null && t.Condition == kind && matches(t)) { t.Events++; if (subject != null) t.Subject = subject; }
        }
    }

    /// <summary>
    /// The unit <paramref name="t"/> is about: the subject of its last counted event, or (All Of / Any Of) of the first
    /// of its parts that is met and has one. Null: none, or it has died.
    /// </summary>
    Unit SubjectOf(ScenarioTrigger t, int depth) {
        if (t == null || depth > MaxDepth) return null;
        if (t.Subject != null && t.Subject.RealStrength > 0) return t.Subject;
        if ((t.Condition == TriggerCondition.AllOf || t.Condition == TriggerCondition.AnyOf) && t.Parts != null)
            foreach (string name in t.Parts) {
                ScenarioTrigger part = Find(name);
                if (part == null || part == t || !IsMet(part, depth + 1)) continue;
                Unit u = SubjectOf(part, depth + 1);
                if (u != null) return u;
            }
        return null;
    }

    /// <summary>What a unit name in an action means: "{unit}" is the unit the trigger fired about (null if none); any other name, that unit.</summary>
    public const string SubjectName = "{unit}";
    static bool IsSubject(string name) { return name != null && name.Trim() == SubjectName; }
    static Unit NamedFor(string name, RunningActions r) { return IsSubject(name) ? (r != null && r.Subject != null && r.Subject.RealStrength > 0 ? r.Subject : null) : ScenarioGame.Named(name); }

    /// <summary>Every unit as it stands, without counting anything: the start of play, or just after a load.</summary>
    void Snapshot() {
        seen.Clear();
        pass++;
        if (UnitController.uc != null && UnitController.uc.units != null)
            foreach (Unit u in UnitController.uc.units) if (u != null) seen[u] = new Seen { strength = u.RealStrength, tile = u.RealTile, pass = pass, seenBy = SeenBy(u, u.RealTile) };
        snapshotTaken = true;
    }

    /// <summary>
    /// What changed on the board since the last frame: units that lost or gained strength (Unit Damaged, Unit Healed),
    /// and units that moved or appeared into a trigger's area (Unit Enters). Compared with what was seen, so every way
    /// it can happen counts (a shot, starving, a heal at a city, a move, a ride, a purchase, a spawn).
    /// </summary>
    void Observe() {
        if (UnitController.uc == null || UnitController.uc.units == null) return;
        pass++;
        List<Unit> all = UnitController.uc.units;
        bool spotting = Uses(TriggerCondition.UnitSpotted);
        for (int i = 0; i < all.Count; i++) {
            Unit u = all[i];
            if (u == null) continue;
            int s = u.RealStrength;
            HexTile at = u.RealTile;
            long sight = spotting ? SeenBy(u, at) : 0;
            Seen was;
            if (seen.TryGetValue(u, out was)) {
                if (s < was.strength) Damaged(u, s, at);
                else if (s > was.strength) Healed(u, s, at);
                if (at != was.tile && at != null) Entered(u, was.tile, at);
                if (spotting) Spotted(u, at, sight & ~was.seenBy);
            } else if (at != null) { Entered(u, null, at); if (spotting) Spotted(u, at, sight); }
            seen[u] = new Seen { strength = s, tile = at, pass = pass, seenBy = sight };
        }
        gone.Clear();
        foreach (KeyValuePair<Unit, Seen> kv in seen) if (kv.Value.pass != pass) gone.Add(kv.Key);
        for (int i = 0; i < gone.Count; i++) seen.Remove(gone[i]);
    }

    bool Uses(TriggerCondition c) { for (int i = 0; i < triggers.Count; i++) if (triggers[i] != null && triggers[i].Condition == c) return true; return false; }

    /// <summary>Which players (bits by their place in GameController.players; not its own side) have <paramref name="u"/> in sight on <paramref name="at"/>.</summary>
    static long SeenBy(Unit u, HexTile at) {
        GameController gc = GameController.gc;
        if (gc == null || gc.players == null || at == null || u.RealStrength <= 0) return 0;
        long bits = 0;
        for (int i = 0; i < gc.players.Count && i < 63; i++) {
            Player p = gc.players[i];
            if (p == null || !Player.Hostile(p, u.owner)) continue;   // spotted by its enemies (allies always see it)
            if (FogOfWar.SeesAt(p, u, at)) bits |= 1L << i;
        }
        return bits;
    }

    /// <summary><paramref name="u"/> has come into sight of the players in <paramref name="newly"/> (Unit Spotted).</summary>
    void Spotted(Unit u, HexTile at, long newly) {
        if (newly == 0) return;
        GameController gc = GameController.gc;
        for (int i = 0; i < gc.players.Count && i < 63; i++) {
            if ((newly & (1L << i)) == 0) continue;
            Player p = gc.players[i];
            Count(TriggerCondition.UnitSpotted, t => ScenarioGame.SideIs(p, t.SeenBy) && ScenarioGame.MatchesAt(u, at, UnitFilter.Of(t)), u);
        }
    }

    void Damaged(Unit u, int strength, HexTile at) {
        Count(TriggerCondition.UnitDamaged, t => strength <= t.Value && ScenarioGame.MatchesAt(u, at, UnitFilter.Of(t)), u);
    }

    void Healed(Unit u, int strength, HexTile at) {
        Count(TriggerCondition.UnitHealed, t => strength >= t.Value && ScenarioGame.MatchesAt(u, at, UnitFilter.Of(t)), u);
    }

    void Entered(Unit u, HexTile from, HexTile to) {
        Count(TriggerCondition.UnitEnters, t => {
            UnitFilter f = UnitFilter.Of(t);
            return ScenarioGame.MatchesAt(u, to, f) && (from == null || !ScenarioGame.MatchesAt(u, from, f));
        }, u);
        if (from != null) Count(TriggerCondition.UnitMoved, t => ScenarioGame.MatchesAt(u, to, UnitFilter.Of(t)), u);
    }

    void OnDied(Unit u) {
        if (!Counting || u == null) return;
        // Expended by its own attack (a missile launched): gone from the board, not killed.
        if (Unit.Expending) { seen.Remove(u); return; }
        HexTile at = u.RealTile;
        Count(TriggerCondition.UnitKilled, t => ScenarioGame.MatchesAt(u, at, UnitFilter.Of(t)), u);
        // Dying is being damaged down to nothing (unless the frame already saw it at 0).
        Seen was;
        if (!seen.TryGetValue(u, out was) || was.strength > 0) Damaged(u, 0, at);
        seen[u] = new Seen { strength = 0, tile = at, pass = pass };
    }

    void OnOwnerChanged(Building b, Player previous) {
        if (!Counting || b == null || b.RealOwner == null) return;   // gone neutral (destroyed, decayed) is not taken
        Count(TriggerCondition.BuildingCaptured, t => ScenarioGame.SideIs(b.RealOwner, t.Side) && ScenarioGame.Matches(b, BuildingFilter.Of(t), false));
    }

    void OnBuildingDestroyed(Building b) {
        if (!Counting || b == null) return;
        Count(TriggerCondition.BuildingDestroyed, t => ScenarioGame.Matches(b, BuildingFilter.Of(t), false));
    }

    void OnProduced(Building b, Unit u) {
        if (!Counting || u == null) return;
        Count(TriggerCondition.UnitBuilt, t => ScenarioGame.MatchesAt(u, u.RealTile, UnitFilter.Of(t)), u);
    }

    void OnUnitSelected(Unit u) {
        if (!Counting || u == null) return;
        Count(TriggerCondition.UnitSelected, t => ScenarioGame.MatchesAt(u, u.RealTile, UnitFilter.Of(t)), u);
    }

    void OnMoveProposed(Unit u, HexTile to) {
        if (!Counting || u == null || to == null) return;
        Count(TriggerCondition.MoveProposed, t => ScenarioGame.MatchesAt(u, to, UnitFilter.Of(t)), u);
    }

    void OnEnemyInspected(Unit u) {
        if (!Counting || u == null) return;
        Count(TriggerCondition.EnemyInspected, t => ScenarioGame.MatchesAt(u, u.RealTile, UnitFilter.Of(t)), u);
    }

    void OnAttackProposed(Unit attacker, Unit defender, bool defenderFirst) {
        if (!Counting || attacker == null) return;
        Count(TriggerCondition.AttackProposed, t => (t.Value != 1 || defenderFirst) && ScenarioGame.MatchesAt(attacker, attacker.RealTile, UnitFilter.Of(t)), attacker);
    }

    void OnAttacked(Unit attacker, Unit defender, HexTile from, HexTile fought, CombatOutcome o) {
        if (!Counting || defender == null || !o.defenderFiresFirst || BoardView.current != null) return;
        Count(TriggerCondition.Ambush, t => ScenarioGame.MatchesAt(defender, fought, UnitFilter.Of(t)), defender);
    }

    void OnBoarded(Unit transport, Unit cargo, HexTile from) {
        if (!Counting || cargo == null || transport == null) return;
        Count(TriggerCondition.UnitBoarded, t => ScenarioGame.MatchesAt(cargo, transport.RealTile, UnitFilter.Of(t)), cargo);
    }

    void OnUnloaded(Unit transport, Unit cargo, HexTile onto) {
        if (!Counting || cargo == null) return;
        Count(TriggerCondition.UnitUnloaded, t => ScenarioGame.MatchesAt(cargo, onto, UnitFilter.Of(t)), cargo);
    }

    void OnStrikeFired(Unit u, Strike s, bool delayed) {
        if (!Counting || s == null) return;
        Count(TriggerCondition.StrikeLaunched, t => ScenarioGame.MatchesStrike(s, UnitFilter.Of(t)));
    }

    void OnStrikeLanded(StrikeResult r) {
        if (!Counting || r == null || r.strike == null || r.strike.splash) return;   // (the splash of a shot is that shot's)
        Count(TriggerCondition.StrikeLanded, t => ScenarioGame.MatchesStrike(r.strike, UnitFilter.Of(t)));
    }

    void OnBuildingOpened(Building b) {
        if (!Counting || b == null) return;
        Count(TriggerCondition.BuildingSelected, t => ScenarioGame.SideIs(b.RealOwner, t.Side) && ScenarioGame.Matches(b, BuildingFilter.Of(t), false));
    }

    void OnTurnEnding(Player p) {
        if (!Counting) return;
        Count(TriggerCondition.TurnOver, t => ScenarioGame.SideIs(p, t.Side));
    }

    void OnTurnStarted(Player p) {
        if (!Counting || GameController.gc == null || GameController.gc.gameOver) return;
        if (p != null) {
            SideTurns st = turnStarts.Find(x => x.Id == p.id);
            if (st == null) turnStarts.Add(st = new SideTurns { Id = p.id });
            st.Count++;
        }
        Count(TriggerCondition.TurnStarted, t => ScenarioGame.SideIs(p, t.Side));
    }

    /// <summary>Markers on units: moved with them; hidden while the player cannot see the unit (it would give it away), and gone with it.</summary>
    void FollowMarkers() {
        for (int i = 0; i < markers.Count; i++) {
            ShownMarker m = markers[i];
            if (string.IsNullOrEmpty(m.Unit)) continue;
            Unit u = ScenarioGame.Named(m.Unit);
            if (u == null || u.RealTile == null) {
                markers.RemoveAt(i--);
                ScenarioGame.HideMarker(m.Id);
                continue;
            }
            bool seen = !m.HideInFog || FogOfWar.ShownToViewer(u);
            if (!seen) { if (!m.hidden) { m.hidden = true; ScenarioGame.HideMarker(m.Id); } continue; }
            if (m.hidden || m.TileX != u.RealTile.x || m.TileY != u.RealTile.y) {
                m.hidden = false; m.TileX = u.RealTile.x; m.TileY = u.RealTile.y;
                ScenarioGame.ShowMarker(m);
            }
        }
    }

    /// <summary>Turns begun in this game by every side; Delay and Repeat Every count them (TurnsOf).</summary>
    [HideInInspector] public List<SideTurns> turnStarts = new List<SideTurns>();

    /// <summary>How many turns the sides <paramref name="side"/> means have begun since play started (Any: every turn).</summary>
    public int TurnsOf(long side) {
        int n = 0;
        foreach (SideTurns st in turnStarts) {
            if (side == TriggerSide.Any) { n += st.Count; continue; }
            Player p = null;
            foreach (Player q in ScenarioGame.Players) if (q.id == st.Id) p = q;
            if (p != null ? ScenarioGame.SideIs(p, side) : st.Id == side) n += st.Count;
        }
        return n;
    }

    // ------------------------------------------------------------------ actions

    /// <summary>
    /// Fire trigger <paramref name="index"/>: counted as fired, and its actions run (as far as the first that waits). A
    /// repeating trigger's counts start again (so it fires on the next one); a trigger that fires once keeps its count, so
    /// it still reads as met where another trigger uses it as a part ("None Of: the HQ was opened").
    /// </summary>
    public void Fire(int index) {
        ScenarioTrigger t = triggers[index];
        t.TimesFired++;
        t.LastFiredTurn = TurnsOf(t.TurnsOf);
        if (t.Repeat) t.Events = 0;
        t.ConditionMetTurn = -1;
        if (t.Actions == null || t.Actions.Count == 0) { Warn(t, "it fired, but it has no actions."); return; }
        RunningActions r = new RunningActions { Trigger = index, Next = 0, Subject = SubjectOf(t, 0) };
        if (!Run(r)) running.Add(r);
    }

    void RunActions() {
        for (int i = 0; i < running.Count; i++) {
            if (Run(running[i])) { running.RemoveAt(i); i--; }
            if (GameController.gc.gameOver) { running.Clear(); return; }
        }
    }

    /// <summary>Carry on with a trigger's actions until one must wait. True when they are all done.</summary>
    bool Run(RunningActions r) {
        if (r.Trigger < 0 || r.Trigger >= triggers.Count || triggers[r.Trigger] == null) return true;
        ScenarioTrigger t = triggers[r.Trigger];
        while (true) {
            if (r.WaitLine > 0 && !ScenarioDialogue.Closed(r.WaitLine)) return false;
            r.WaitLine = 0;
            if (r.WaitUntil > 0f && clock < r.WaitUntil) return false;
            r.WaitUntil = 0f;
            if (t.Actions == null || r.Next >= t.Actions.Count) return true;
            TriggerAction a = t.Actions[r.Next++];
            if (a == null) continue;
            Do(t, a, r);
            if (GameController.gc != null && GameController.gc.gameOver) return true;
        }
    }

    int markerSerial;

    void Do(ScenarioTrigger t, TriggerAction a, RunningActions r) {
        switch (a.Type) {
            case TriggerActionType.Dialogue:
                if (a.Lines == null || a.Lines.Count == 0) { Warn(t, "a Dialogue action has no lines."); return; }
                r.WaitLine = ScenarioDialogue.Enqueue(a.Lines);
                return;
            case TriggerActionType.Wait:
                if (a.Seconds > 0f) r.WaitUntil = clock + a.Seconds;
                return;
            case TriggerActionType.CameraFocus: {
                int x = a.TileX, y = a.TileY;
                if (!string.IsNullOrEmpty(a.AtUnit)) {
                    Unit u = NamedFor(a.AtUnit, r);
                    if (u == null || u.RealTile == null) { Warn(t, "Camera Focus: no unit named '" + a.AtUnit + "' on the board."); return; }
                    x = u.RealTile.x; y = u.RealTile.y;
                }
                ScenarioGame.FocusCamera(x, y, a.Zoom, a.Seconds);
                return;
            }
            case TriggerActionType.OrderUnits: {
                List<Unit> movers = UnitsFor(t, a, r);
                foreach (Unit u in movers) {
                    Unit target = null;
                    if (!string.IsNullOrEmpty(a.Target)) target = NamedFor(a.Target, r);
                    if (target == null && a.TargetSide != TriggerSide.Any) {
                        // The strongest it can reach (a fresh squad), or the nearest of Target Side (in Value cover or more, if any is: the squad in the trees).
                        if (a.TargetFresh) target = ScenarioGame.StrongestInReach(u, a.TargetSide, a.Value);
                        if (target == null && a.Value > 0) target = ScenarioGame.NearestOf(u, a.TargetSide, a.Value);
                        if (target == null) target = ScenarioGame.NearestOf(u, a.TargetSide);
                    }
                    if (target == null && !string.IsNullOrEmpty(a.Target)) { Warn(t, "Order Units: no unit named '" + a.Target + "' to attack."); continue; }
                    if (!ScenarioGame.Order(u, target, ScenarioGame.Tile(a.TileX, a.TileY)))
                        Warn(t, "Order Units: " + u.name + " could not move or attack (it has used its turn, or nothing is in reach).");
                }
                BoardChanged();
                return;
            }
            case TriggerActionType.StrikeTile: {
                // (The tile is the target here, not an area the units must be in.)
                UnitFilter who = UnitFilter.Of(a); who.inArea = false;
                List<Unit> strikers = ScenarioGame.Units(who);
                HexTile aim = ScenarioGame.Tile(a.TileX, a.TileY);
                if (aim == null) { Warn(t, "Strike Tile: no tile (" + a.TileX + "," + a.TileY + ")."); return; }
                int struck = 0;
                foreach (Unit u in strikers) if (u.StrikesArea && ScenarioGame.Strike(u, aim)) struck++;
                if (struck == 0) Warn(t, "Strike Tile: no unit of " + ScenarioGame.SideName(a.UnitSide) + (string.IsNullOrEmpty(a.UnitKind) ? "" : " (" + a.UnitKind + ")")
                    + " could strike (" + a.TileX + "," + a.TileY + "): none that strikes ground or from afar has the tile in range and its turn left.");
                BoardChanged();
                return;
            }
            case TriggerActionType.CameraFrameBoard: ScenarioGame.FrameBoard(); return;
            case TriggerActionType.ShowMarker: {
                if (string.IsNullOrEmpty(a.Marker)) { Warn(t, "a Show Marker action names no marker prefab."); return; }
                string id = string.IsNullOrEmpty(a.MarkerId) ? "#" + (++markerSerial) : a.MarkerId;
                markers.RemoveAll(m => m.Id == id);
                ShownMarker shown = new ShownMarker { Id = id, Marker = a.Marker, Text = a.Text, TileX = a.TileX, TileY = a.TileY, Unit = a.AtUnit ?? "", HideInFog = a.HideInFog };
                if (!string.IsNullOrEmpty(shown.Unit) && ScenarioGame.Named(shown.Unit) == null) { Warn(t, "Show Marker: no unit named '" + shown.Unit + "' to put it on."); return; }
                markers.Add(shown);
                shown.hidden = true;   // (FollowMarkers puts it on its unit, if the player can see it)
                if (string.IsNullOrEmpty(shown.Unit)) { shown.hidden = false; ScenarioGame.ShowMarker(shown); }
                else FollowMarkers();
                return;
            }
            case TriggerActionType.HideMarker:
                if (!string.IsNullOrEmpty(a.MarkerId) && a.MarkerId.EndsWith("*")) {
                    // Every marker whose name starts so (Mark Reach's marks).
                    string prefix = a.MarkerId.Substring(0, a.MarkerId.Length - 1);
                    for (int i = 0; i < markers.Count; i++)
                        if (markers[i].Id.StartsWith(prefix, StringComparison.Ordinal)) { ScenarioGame.HideMarker(markers[i].Id); markers.RemoveAt(i--); }
                    return;
                }
                if (string.IsNullOrEmpty(a.MarkerId)) markers.Clear(); else markers.RemoveAll(m => m.Id == a.MarkerId);
                ScenarioGame.HideMarker(a.MarkerId);
                return;
            case TriggerActionType.MarkReach: {
                if (string.IsNullOrEmpty(a.Marker)) { Warn(t, "a Mark Reach action names no marker prefab."); return; }
                string id = string.IsNullOrEmpty(a.MarkerId) ? "reach" : a.MarkerId;
                foreach (Unit u in UnitsFor(t, a, r))
                    foreach (HexTile tile in ScenarioGame.Reach(u, a.Value)) {
                        string mid = id + ":" + tile.x + "," + tile.y;
                        markers.RemoveAll(m => m.Id == mid);
                        ShownMarker shown = new ShownMarker { Id = mid, Marker = a.Marker, Text = "", TileX = tile.x, TileY = tile.y };
                        markers.Add(shown);
                        ScenarioGame.ShowMarker(shown);
                    }
                return;
            }
            case TriggerActionType.PlayMusic:
                if (!ScenarioGame.PlayMusic(a.Track, a.Loop)) Warn(t, "Play Music: no track " + (string.IsNullOrEmpty(a.Track) ? "at all" : "'" + a.Track + "'") + " in Resources/Music/Special.");
                return;
            case TriggerActionType.StopMusic: ScenarioGame.StopMusic(); return;
            case TriggerActionType.SetPurchases: ScenarioGame.SetPurchases(a.On); return;
            case TriggerActionType.SetFog: ScenarioGame.SetFog(a.On); return;
            case TriggerActionType.PlaySound: ScenarioGame.PlaySound(a.Sound); return;
            case TriggerActionType.Win: {
                Player p = ScenarioGame.Resolve(a.Side);
                if (p == null) Warn(t, "Win: no side is " + ScenarioGame.SideName(a.Side) + ": the game ends in a draw.");
                ScenarioGame.Win(p);
                return;
            }
            case TriggerActionType.Lose: {
                Player p = ScenarioGame.Resolve(a.Side);
                if (p == null) Warn(t, "Lose: no side is " + ScenarioGame.SideName(a.Side) + ".");
                ScenarioGame.Lose(p);
                return;
            }
            case TriggerActionType.SpawnUnit: {
                Player p = ScenarioGame.Resolve(a.Side);
                if (p == null) { Warn(t, "Spawn Unit: no side is " + ScenarioGame.SideName(a.Side) + ": nothing made."); return; }
                ScenarioGame.Spawn(p, a.UnitKind, a.TileX, a.TileY, a.Count, a.Value, a.Veterancy, a.UnitTag, a.ReadyToAct, a.OutOfSight);
                BoardChanged();
                return;
            }
            case TriggerActionType.GiveUnits: {
                Player p = ScenarioGame.Resolve(a.Side);
                if (p == null) { Warn(t, "Give Units: no side is " + ScenarioGame.SideName(a.Side) + ": nothing given."); return; }
                ScenarioGame.GiveUnits(UnitsFor(t, a, r), p);
                BoardChanged();
                return;
            }
            case TriggerActionType.GiveBuilding: {
                Player p = ScenarioGame.Resolve(a.Side);
                if (p == null && a.Side != TriggerSide.Neutral) { Warn(t, "Give Building: no side is " + ScenarioGame.SideName(a.Side) + ": nothing given."); return; }
                ScenarioGame.GiveBuildings(BuildingsFor(t, a), p);
                BoardChanged();
                return;
            }
            case TriggerActionType.DestroyBuilding: ScenarioGame.DestroyBuildings(BuildingsFor(t, a)); BoardChanged(); return;
            case TriggerActionType.RepairBuilding: ScenarioGame.RepairBuildings(BuildingsFor(t, a)); BoardChanged(); return;
            case TriggerActionType.DestroyTile:
                if (ScenarioGame.DestroyTiles(a.TileX, a.TileY, a.Radius) == 0) Warn(t, "Destroy Tile: nothing within " + a.Radius + " of (" + a.TileX + "," + a.TileY + ") can be destroyed.");
                BoardChanged();
                return;
            case TriggerActionType.RepairTile: ScenarioGame.RepairTiles(a.TileX, a.TileY, a.Radius); BoardChanged(); return;
            case TriggerActionType.SetVeterancy: ScenarioGame.SetVeterancy(UnitsFor(t, a, r), a.Value, a.Mode); return;
            case TriggerActionType.SetStat: ScenarioGame.SetStat(UnitsFor(t, a, r), a.Stat, a.Value, a.Mode); BoardChanged(); return;
            case TriggerActionType.DestroyUnits: ScenarioGame.DestroyUnits(UnitsFor(t, a, r)); BoardChanged(); return;
            case TriggerActionType.ReadyUnits: ScenarioGame.ReadyUnits(UnitsFor(t, a, r), a.Value != 0); return;
            case TriggerActionType.PlaceUnits: {
                // (The tile is where they go, not an area they must be in.)
                UnitFilter who = UnitFilter.Of(a); who.inArea = false;
                List<Unit> placed = ScenarioGame.Units(who);
                if (placed.Count == 0) { Warn(t, "Place Units: no unit matches."); return; }
                if (ScenarioGame.Tile(a.TileX, a.TileY) == null) { Warn(t, "Place Units: no tile (" + a.TileX + "," + a.TileY + ")."); return; }
                int n = ScenarioGame.Place(placed, a.TileX, a.TileY, a.ReadyToAct);
                if (n < placed.Count) Warn(t, "Place Units: no room near (" + a.TileX + "," + a.TileY + ") for " + (placed.Count - n) + " of them.");
                BoardChanged();
                return;
            }
            case TriggerActionType.GiveMoney: {
                Player p = ScenarioGame.Resolve(a.Side);
                if (p == null) { Warn(t, "Give Money: no side is " + ScenarioGame.SideName(a.Side) + "."); return; }
                ScenarioGame.GiveMoney(p, a.Value, a.Mode);
                return;
            }
            case TriggerActionType.EnableTrigger:
            case TriggerActionType.DisableTrigger:
            case TriggerActionType.ResetTrigger: {
                ScenarioTrigger other = string.IsNullOrEmpty(a.Trigger) ? t : Find(a.Trigger);
                if (other == null) { Warn(t, a.Type + ": no trigger named '" + a.Trigger + "'."); return; }
                other.ConditionMetTurn = -1;
                if (a.Type == TriggerActionType.DisableTrigger) { other.Enabled = false; return; }
                other.Events = 0;   // counts start from now
                if (a.Type == TriggerActionType.EnableTrigger) other.Enabled = true;
                else other.TimesFired = 0;
                return;
            }
        }
    }

    List<Unit> UnitsFor(ScenarioTrigger t, TriggerAction a) { return UnitsFor(t, a, null); }

    /// <summary>The units an action's filter means; Unit Name "{unit}": the unit the trigger fired about (if it is alive, and of the side and kind asked).</summary>
    List<Unit> UnitsFor(ScenarioTrigger t, TriggerAction a, RunningActions run) {
        if (IsSubject(a.UnitTag)) {
            UnitFilter f = UnitFilter.Of(a); f.tag = "";
            Unit s = NamedFor(a.UnitTag, run);
            List<Unit> one = new List<Unit>();
            if (s != null && ScenarioGame.MatchesAt(s, s.RealTile, f)) one.Add(s);
            else Warn(t, a.Type + ": the unit it fired about is gone (or not of the side and kind asked).");
            return one;
        }
        List<Unit> r = ScenarioGame.Units(UnitFilter.Of(a));
        if (r.Count == 0) Warn(t, a.Type + ": no unit matches (" + ScenarioGame.SideName(a.UnitSide) + (string.IsNullOrEmpty(a.UnitKind) ? "" : ", " + a.UnitKind)
            + (string.IsNullOrEmpty(a.UnitTag) ? "" : ", named " + a.UnitTag) + (a.InArea ? ", within " + a.Radius + " of (" + a.TileX + "," + a.TileY + ")" : "") + ").");
        return r;
    }

    List<Building> BuildingsFor(ScenarioTrigger t, TriggerAction a) {
        List<Building> r = ScenarioGame.Buildings(BuildingFilter.Of(a));
        if (r.Count == 0) Warn(t, a.Type + ": no building matches (" + ScenarioGame.SideName(a.BuildingSide) + (string.IsNullOrEmpty(a.BuildingKind) ? "" : ", " + a.BuildingKind)
            + (a.InArea ? ", within " + a.Radius + " of (" + a.TileX + "," + a.TileY + ")" : "") + ").");
        return r;
    }

    /// <summary>The board changed outside any side's action: sight, and any AI's thought under way (made on the board as it was).</summary>
    static void BoardChanged() {
        FogOfWar.Changed();
        foreach (Player p in ScenarioGame.Players) {
            AIPlayer ai = p.GetComponent<AIPlayer>();
            if (ai != null) ai.BoardChangedByScript();
        }
    }

    // ------------------------------------------------------------------ save and load

    /// <summary>The script as the scenario file keeps it ("script"); with its progress in this game when <paramref name="withState"/> (a save).</summary>
    public JSONObject ToJson(bool withState) {
        ScenarioScriptData d = new ScenarioScriptData { Triggers = triggers };
        if (withState) {
            d.Started = started;
            d.Clock = clock;
            d.Markers = markers;
            d.Running = running;
            d.TurnStarts = turnStarts;
            d.PendingLines = ScenarioDialogue.Pending();
        }
        return ScenarioJson.Write(d, withState);
    }

    /// <summary>Anything to write? (A scenario with no triggers writes no "script".)</summary>
    public bool HasContent { get { return triggers.Count > 0 || markers.Count > 0 || Words; } }

    /// <summary>Has a description, win or loss text (written at the top of the file: WordsJson).</summary>
    public bool Words { get { return !string.IsNullOrEmpty(description) || !string.IsNullOrEmpty(winText) || !string.IsNullOrEmpty(lossText); } }

    /// <summary>The scenario's words as top-level fields of its file (",\n"key": "text"" each), for LevelEditor.BoardJson.</summary>
    public string WordsJson() {
        string s = "";
        if (!string.IsNullOrEmpty(description)) s += ",\n\"description\":" + JSONObject.CreateStringObject(description).Print();
        if (!string.IsNullOrEmpty(winText)) s += ",\n\"winText\":" + JSONObject.CreateStringObject(winText).Print();
        if (!string.IsNullOrEmpty(lossText)) s += ",\n\"lossText\":" + JSONObject.CreateStringObject(lossText).Print();
        return s;
    }

    /// <summary>The text the post-game screen shows above the findings for <paramref name="viewerWon"/>: the win or the loss text ("" if none).</summary>
    public static string EndText(bool viewerWon) {
        ScenarioScript s = Instance;
        return s == null ? "" : viewerWon ? s.winText ?? "" : s.lossText ?? "";
    }

    /// <summary>
    /// The script of a scenario file or save just loaded: <paramref name="script"/> is its "script" (null: none, and
    /// the scenario has no triggers). Its progress comes with it (a save goes on where it was; a scenario starts from
    /// nothing), the dialogue and markers of the last board are cleared, and the next frame looks at the board afresh.
    /// </summary>
    public void Load(JSONObject script, string path) {
        ScenarioScriptData d = script != null && script.isObject ? ScenarioJson.Read<ScenarioScriptData>(script) : new ScenarioScriptData();
        triggers = d.Triggers ?? new List<ScenarioTrigger>();
        scenarioPath = path ?? "";
        started = d.Started;
        clock = d.Clock;
        running = d.Running ?? new List<RunningActions>();
        turnStarts = d.TurnStarts ?? new List<SideTurns>();
        warned.Clear();
        seen.Clear();
        snapshotTaken = false;
        ScenarioDialogue.Clear();
        ScenarioGame.HideMarker("");
        markers = d.Markers ?? new List<ShownMarker>();
        foreach (ShownMarker m in markers) { m.hidden = !string.IsNullOrEmpty(m.Unit); if (!m.hidden) ScenarioGame.ShowMarker(m); }
        // A conversation that was on screen when the game was saved: shown again, and what was waiting on it waits on it.
        int last = ScenarioDialogue.Enqueue(d.PendingLines);
        foreach (RunningActions r in running) if (r.WaitLine > 0) r.WaitLine = last;
        sides.Clear();
        foreach (Player p in ScenarioGame.Players) sides.Add(new KnownSide { id = p.id, name = p.personal_name });
        Validate();
    }

    /// <summary>Load the script of the board just loaded from <paramref name="obj"/> (ScenarioLoader).</summary>
    public static void LoadFromBoard(JSONObject obj, string path) {
        ScenarioScript s = Current;
        s.Load(obj != null ? obj.GetField(FileKey) : null, path);
        s.description = Text(obj, "description");
        s.winText = Text(obj, "winText");
        s.lossText = Text(obj, "lossText");
    }

    static string Text(JSONObject obj, string key) {
        JSONObject f = obj != null ? obj.GetField(key) : null;
        return f != null && f.isString ? f.stringValue : "";
    }

    /// <summary>The key a scenario file keeps its script under.</summary>
    public const string FileKey = "script";

    /// <summary>Problems a designer should hear about at once (logged): names that point nowhere.</summary>
    public List<string> Validate(bool log = true) {
        List<string> problems = ScenarioValidation.Check(triggers);
        if (log) foreach (string p in problems) Debug.LogWarning("Scenario script" + (string.IsNullOrEmpty(scenarioPath) ? "" : " (" + scenarioPath + ")") + ": " + p);
        return problems;
    }
}

/// <summary>What is wrong with a list of triggers, said in words (the inspector shows it; loading logs it).</summary>
public static class ScenarioValidation {
    public static List<string> Check(List<ScenarioTrigger> triggers) {
        List<string> r = new List<string>();
        if (triggers == null) return r;
        HashSet<string> names = new HashSet<string>(), twice = new HashSet<string>();
        foreach (ScenarioTrigger t in triggers) if (t != null && !names.Add(t.Name ?? "")) twice.Add(t.Name ?? "");
        foreach (string n in twice) r.Add("two triggers are named '" + n + "': other triggers find only the first.");
        foreach (ScenarioTrigger t in triggers) {
            if (t == null) continue;
            string who = "'" + t.Name + "': ";
            if (t.Condition == TriggerCondition.AfterTrigger && !names.Contains(t.AfterTrigger ?? "")) r.Add(who + "After Trigger names '" + t.AfterTrigger + "', which is not a trigger here.");
            if (t.Condition == TriggerCondition.AllOf || t.Condition == TriggerCondition.AnyOf || t.Condition == TriggerCondition.NoneOf) {
                if (t.Parts == null || t.Parts.Count == 0) r.Add(who + "it combines no parts.");
                else foreach (string p in t.Parts) if (!names.Contains(p ?? "")) r.Add(who + "part '" + p + "' is not a trigger here.");
            }
            if (t.Actions != null) foreach (TriggerAction a in t.Actions) {
                if (a == null) continue;
                if ((a.Type == TriggerActionType.EnableTrigger || a.Type == TriggerActionType.DisableTrigger || a.Type == TriggerActionType.ResetTrigger)
                    && !string.IsNullOrEmpty(a.Trigger) && !names.Contains(a.Trigger)) r.Add(who + a.Type + " names '" + a.Trigger + "', which is not a trigger here.");
                if (a.Type == TriggerActionType.Dialogue && (a.Lines == null || a.Lines.Count == 0)) r.Add(who + "a Dialogue action has no lines.");
                if (a.Type == TriggerActionType.SpawnUnit && string.IsNullOrEmpty(a.UnitKind)) r.Add(who + "a Spawn Unit action names no unit kind.");
                if (a.Type == TriggerActionType.ShowMarker && string.IsNullOrEmpty(a.Marker)) r.Add(who + "a Show Marker action names no marker prefab.");
            }
        }
        return r;
    }
}
