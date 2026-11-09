using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Standard: destroy or capture the enemy HQ.
/// Attrition (a test variant): HQs cannot be attacked; a side is out when it has no units but its
/// HQ; after roundLimit rounds the side with the most strength x cost left (HQs not counted) wins.
/// Sandbox: as Standard on the board (HQs can be attacked and captured), but the game never ends:
/// nobody is eliminated, there is no winner and no round limit.
/// </summary>
public enum RuleSet { Standard, Attrition, Sandbox }

public class GameController : MonoBehaviour
{
    [Header("Rules")]
    public RuleSet rules = RuleSet.Standard;
    [Tooltip("Attrition: rounds before the game is decided on what is left.")]
    public int roundLimit = 30;

    [Header("Fog of war")]
    [Tooltip("Fog of war: each side sees only what its units and buildings see (FogOfWar). A scenario sets this (\"fog\": \"on\").")]
    public bool fogOfWar;
    [Tooltip("Whose view the screen shows under fog of war: -1 the human player (the one whose turn it is when several share the screen); 0, 1, ... always that player's (to watch an AI game through one side's eyes).")]
    public int fogViewer = -1;
    [Tooltip("Show the fog of war on screen. Off: the screen shows the whole board (every unit, every building's real owner) while each side, AI or human, still plays by what it sees — to watch how the AIs play under fog, turn by turn.")]
    public bool fogShownOnScreen = true;

    /// <summary>"on" / "off" from a scenario (anything else: off).</summary>
    public static bool ParseFog(string s) {
        string v = (s ?? "").Trim().ToLowerInvariant();
        return v == "on" || v == "true" || v == "yes";
    }

    public string FogSetting { get { return fogOfWar ? "on" : "off"; } }

    [Header("Factions")]
    [Tooltip("Factions field their special units: versions of a base unit that play differently (other attributes on the unit), units only they have, and the units they lack. Off: a faction only changes how units look — its versions that differ from the base unit in nothing but looks, and every base unit. A scenario or the pre-game screen sets this (\"factionSpecialUnits\": \"on\"/\"off\").")]
    public bool factionSpecialUnits = true;

    public string SpecialUnitsSetting { get { return factionSpecialUnits ? "on" : "off"; } }

    /// <summary>
    /// The game is held: no side plays (AIs do not think or act, the board takes no clicks, no turn ends), while a
    /// menu is open over it (the pre-game screen, the in-game menu, the map editor), or while a scenario's dialogue is
    /// on screen (<see cref="ScriptHold"/>). Not a rule: nothing on the board changes while it is set. Setting it sets
    /// the menus' hold (<see cref="MenuHeld"/>); the dialogue's is its own, so closing a menu does not end it.
    /// </summary>
    public static bool Paused { get { return menuHeld || ScriptHold; } set { menuHeld = value; } }
    static bool menuHeld;

    /// <summary>A menu holds the game (the pre-game screen, the in-game menu, the map editor): the scenario's script does not run either.</summary>
    public static bool MenuHeld { get { return menuHeld; } }

    /// <summary>A scenario's dialogue is on screen (ScenarioDialogue): the game is held until it is closed.</summary>
    public static bool ScriptHold;

    [Header("Purchases")]
    [Tooltip("Can units be bought at buildings? (A scenario sets this; when it does not, Standard and Sandbox allow purchases and Attrition does not.)")]
    public bool purchasesAllowed = true;
    [Tooltip("The units that may be bought, by prefab name (Grunt, Tank, Supplies ...). Empty: any unit a building can build.")]
    public List<string> allowedUnits = new List<string>();

    /// <summary>A unit's prefab name ("Tank" for "Tank(Clone)").</summary>
    public static string UnitName(Unit u) {
        return u == null ? "" : u.name.Replace("(Clone)", "").Trim();
    }

    /// <summary>
    /// The name a player sees: a faction's version of a unit goes by the unit it stands in for ("Grunt Federation
    /// Variant" → "Grunt"; Faction.BaseOf, <paramref name="owner"/>'s faction, else any faction that lists it); a unit
    /// no faction lists by its own name, less a trailing " Variant".
    /// </summary>
    public static string DisplayName(Unit u, Player owner) {
        if (u == null) return "";
        Faction f = owner != null ? owner.faction : null;
        Unit b = f != null ? f.BaseOf(u) : u;
        if (b == u) foreach (Faction any in GameAssets.AllFactions) { Unit x = any.BaseOf(u); if (x != u) { b = x; break; } }
        string n = UnitName(b);
        return n.EndsWith(" Variant") ? n.Substring(0, n.Length - " Variant".Length).Trim() : n;
    }

    /// <summary>May this unit be bought under the current rules and scenario?</summary>
    public static bool MayPurchase(Unit prefab) {
        if (prefab == null) return false;
        if (gc == null) return true;
        if (!gc.purchasesAllowed) return false;
        if (gc.allowedUnits == null || gc.allowedUnits.Count == 0) return true;
        string n = UnitName(prefab);
        foreach (string a in gc.allowedUnits) if (string.Equals((a ?? "").Trim(), n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Set purchases from a scenario: <paramref name="purchases"/> "on" / "off" (empty: the rule
    /// set's default, on except under Attrition), <paramref name="allowed"/> a comma-separated list
    /// of unit names (empty: all).
    /// </summary>
    public void ApplyPurchaseRules(string purchases, string allowed) {
        string p = (purchases ?? "").Trim().ToLowerInvariant();
        purchasesAllowed = p == "on" || p == "true" || p == "yes" ? true
                         : p == "off" || p == "false" || p == "no" ? false
                         : rules != RuleSet.Attrition;
        allowedUnits = new List<string>();
        foreach (string a in (allowed ?? "").Split(',')) if (a.Trim().Length > 0) allowedUnits.Add(a.Trim());
    }

    /// <summary>The scenario's purchase settings, as a save writes them ("on"/"off", comma-separated names).</summary>
    public string PurchasesSetting { get { return purchasesAllowed ? "on" : "off"; } }
    public string AllowedUnitsSetting { get { return allowedUnits == null ? "" : string.Join(",", allowedUnits.ToArray()); } }

    /// <summary>"standard", "attrition" or "sandbox" (any case); anything else is Standard.</summary>
    public static RuleSet ParseRules(string s) {
        switch ((s ?? "").Trim().ToLowerInvariant()) {
            case "attrition": return RuleSet.Attrition;
            case "sandbox": return RuleSet.Sandbox;
            default: return RuleSet.Standard;
        }
    }

    /// <summary>Can this unit be attacked under the current rules? (Attrition: HQs cannot.)</summary>
    public static bool Targetable(Unit u) {
        if (u == null) return false;
        // Thinking on a concealed board (fog of war): an enemy only believed to be somewhere, not
        // seen, cannot be fired on.
        FogOfWar.Knowledge fog = FogOfWar.current;
        if (fog != null && u.owner != fog.player && !fog.visible.Contains(u)) return false;
        if (gc != null && gc.rules == RuleSet.Attrition) {
            SupplyPoint sp = u.Hub;
            if (sp != null && sp.producer) return false;
        }
        return true;
    }

    /// <summary>The player's HQ building, if it still has one.</summary>
    public static Building HQOf(Player p) {
        if (p == null) return null;
        for (int i = 0; i < Building.all.Count; i++) {
            Building b = Building.all[i];
            if (b != null && b.IsHQ && b.owner == p) return b;
        }
        return null;
    }

    /// <summary>Strength x cost of a player's units, HQs excluded: the attrition score.</summary>
    public static float Remaining(Player p) {
        float m = 0f;
        if (p == null) return m;
        for (int i = 0; i < p.units.Count; i++) {
            Unit u = p.units[i];
            if (u == null || u.Strength <= 0) continue;
            SupplyPoint sp = u.Hub;
            if (sp != null && sp.producer) continue;
            m += u.Strength * (float)u.cost;
        }
        return m;
    }

    static int CombatUnits(Player p) {
        int n = 0;
        for (int i = 0; i < p.units.Count; i++) {
            Unit u = p.units[i];
            if (u == null || u.Strength <= 0) continue;
            SupplyPoint sp = u.Hub;
            if (sp != null && sp.producer) continue;
            n++;
        }
        return n;
    }

    [Tooltip("Players in the Current Game")]
    public List<Player> players;

    public int days;

    public Player activePlayer;

    public Unit activeUnit;

    public static GameController gc;

    [Header("Outcome")]
    public bool gameOver;
    public Player winner;

    [Tooltip("Suppress input handling and animation gating; set by the headless harness")]
    public bool headless;

    private void Awake() {
        gc = this;
    }

    private void Start() {
        gc = this;
        for (int i = 0; i < players.Count; i++) {
            Player p = players[i];
            if (p == null) continue;
            if (p.HasHQ) p.hadHQ = true;
            p.Resupply();
        }
        if (!headless && HexTactica.Harness.ScenarioLoader.RecordGames) StartCoroutine(RecordFromStart());
    }

    /// <summary>
    /// Every game played in play mode is recorded (GameLog) without asking: the board in the
    /// scene from the first frame, unless a scenario or a save was loaded meanwhile (which
    /// starts its own recording). HexTactica ▸ Game Log ▸ Record Loaded Scenarios turns it off.
    /// </summary>
    System.Collections.IEnumerator RecordFromStart() {
        yield return null;
        if (HexTactica.Harness.GameLog.Recording) yield break;
        HexTactica.Harness.GameLog.Start("board in the scene", null, null);
        Debug.Log("Recording this game to " + HexTactica.Harness.GameLog.Folder);
    }

    [Tooltip("Longest a turn's end waits for the moves and shots still playing on screen, in seconds (then it ends anyway).")]
    public float maxEndTurnWait = 10f;
    bool endTurnPending;
    float endTurnAskedAt;

    /// <summary>A turn's end has been asked for and waits for the animations still playing (EndTurnWhenStill).</summary>
    public bool EndTurnPending { get { return endTurnPending; } }

    /// <summary>
    /// End the turn once nothing is moving on screen: every walk, retreat, boarding and shot already
    /// decided has played (Unit.AnyAnimating), so the next side does not start while the last one's
    /// units are still on their way (at most maxEndTurnWait). The rules are not held up: the board is
    /// already as it will be. Headless: at once.
    /// </summary>
    public void EndTurnWhenStill() {
        if (headless || gameOver) { EndTurn(); return; }
        if (endTurnPending) return;
        endTurnPending = true;
        endTurnAskedAt = Time.unscaledTime;
    }

    /// <summary>Drop a turn's end still waiting (a board was loaded).</summary>
    public void CancelPendingEndTurn() { endTurnPending = false; }

    public void Update() {
        if (headless) return;
        if (Paused) return;   // a menu is open over the game: nothing plays, no keys
        if (endTurnPending && (!Unit.AnyAnimating() || Time.unscaledTime - endTurnAskedAt > maxEndTurnWait)) {
            endTurnPending = false;
            EndTurn();
        }
        if (HexTactica.Harness.Replay.Active) return;   // a replay: nobody plays
        if (Input.GetKeyDown("t") && !fogOfWar) {   // a debug read-out of every unit's reach: not under fog
            RecalculateThreat();
            HexGenerator.forAllTiles(x => {
                x.textField.text = x.threat[GameController.gc.activePlayer].firepower + "";
                return 0;
            });
        }
        // Space ends the turn of a human player only: an AI ends its own (and may be thinking on another thread).
        if (Input.GetKeyDown(KeyCode.Space)) HumanEndTurn();
    }

    /// <summary>
    /// The player at the screen ends their turn: the Space key and the HUD's End turn button (TurnControls) both come
    /// here. Only a human player's turn, not while one is already ending, not in a replay.
    /// </summary>
    public void HumanEndTurn() {
        if (headless || gameOver || endTurnPending || Paused || HexTactica.Harness.Replay.Active || !FogOfWar.IsHuman(activePlayer)) return;
        EndTurnWhenStill();
    }

    Unit lastIdle;

    /// <summary>
    /// The HUD's Next unit button (TurnControls): the human player's next unit that has not acted this turn, after the
    /// one selected (or the last one picked this way), in roster order; selected by a click on its tile
    /// (HexTile.ClickBoard), as the player would. Null if none is left, or it is not a human player's turn.
    /// </summary>
    public Unit SelectNextIdle() {
        if (gameOver || endTurnPending || Paused || HexTactica.Harness.Replay.Active || !FogOfWar.IsHuman(activePlayer)) return null;
        List<Unit> idle = new List<Unit>();
        foreach (Unit u in activePlayer.units)
            if (u != null && u.active && u.tile != null && u.Strength > 0 && (u.trasnporter == null || u.trasnporter == u)) idle.Add(u);
        if (idle.Count == 0) return null;
        Unit from = activeUnit != null ? activeUnit : lastIdle;
        int i = from != null ? idle.IndexOf(from) : -1;
        Unit next = idle[(i + 1) % idle.Count];
        // Whatever was selected is dropped first, so the click on the next one's tile selects it.
        activeUnit = null;
        HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
        next.tile.ClickBoard();
        lastIdle = next;
        return next;
    }

    public static void ResetTileData() {
        HexGenerator.forAllTiles(x => {
            GameController.gc.players.ForEach(y => {
                x.threat[y].firepower = 0;
                x.threat[y].antiair = 0;
                x.threat[y].antiarmor = 0;
                x.threat[y].closestUnit = 0;
            });
            x.aiData.Clear();
            return 0;
        });
    }

    /// <summary>
    /// A board was just loaded from a file (a scenario, a save): what is drawn from the rules is
    /// worked out for it now rather than at the first move — every side's supply (published to the
    /// tiles and their borders) and sight — and the fog is shown at once.
    /// </summary>
    public static void BoardLoaded() {
        if (gc == null) return;
        gc.CancelPendingEndTurn();
        if (gc.players != null)
            foreach (Player p in gc.players) {
                if (p == null) continue;
                if (p.HasHQ) p.hadHQ = true;
                p.Resupply();
            }
        HexTilePlayerSupplyDisplay.WakeAll();
        FogOfWar.Changed();
        FogOfWar.Refresh();
        FogView.BoardLoaded();
    }

    public static void RecalculateThreat() {
        ResetTileData();
        List<Unit> snapshot = new List<Unit>(UnitController.uc.units);
        // (A unit off the board — carried, or concealed by fog of war — threatens nothing.)
        // Each side's reach is worked out on the board as it knows it (fog of war): one copy per side.
        // (Each unit's movement is kept from the last recalculation while nothing near it changed: ThreatReach, real board only.)
        if (FogOfWar.On && BoardView.Current == null && gc != null && gc.players != null) {
            foreach (Player p in gc.players) {
                if (p == null) continue;
                using (FogOfWar.Concealing(p)) {
                    ThreatReach.Begin(p);
                    try { foreach (Unit x in snapshot) if (x != null && x.owner == p && x.tile != null) x.updateThreat(); }
                    finally { ThreatReach.End(); }
                }
            }
            foreach (Unit x in snapshot) if (x != null && (x.owner == null || !gc.players.Contains(x.owner)) && x.tile != null) x.updateThreat();
            return;
        }
        if (BoardView.Current == null) ThreatReach.Begin(null); else ThreatReach.End();
        try { snapshot.ForEach(x => { if (x != null && x.tile != null) x.updateThreat(); }); }
        finally { ThreatReach.End(); }
    }

    /// <summary>A turn is about to end (before captures resolve): the player ending it.</summary>
    public static event System.Action<Player> TurnEnding;

    public void EndTurn() {
        if (gameOver) return;
        if (TurnEnding != null) TurnEnding(activePlayer);
        // Infantry that ended the turn on a building without attacking captures it.
        Building.ResolveCaptures(activePlayer);
        if (gameOver) return;

        activePlayer.EndTurn();
        FogOfWar.OnTurnEnded(activePlayer);

        int index = players.IndexOf(activePlayer);
        int guard = 0;
        do {
            if (index == 0) {
                days++;
                index = players.Count - 1;
            } else {
                index--;
            }
            activePlayer = players[index];
        } while (activePlayer.eliminated && rules != RuleSet.Sandbox && guard++ < players.Count);   // Sandbox: nobody is ever out

        activePlayer.StartTurn();
        FogOfWar.Changed();
        FogOfWar.Refresh();
        CheckVictory();
        // Attrition: after roundLimit full rounds, the most left standing wins (equal: a draw).
        if (!gameOver && rules == RuleSet.Attrition && days >= roundLimit) {
            Player best = null; float bestM = -1f; bool tie = false;
            for (int i = 0; i < players.Count; i++) {
                Player p = players[i];
                if (p == null || p.eliminated) continue;
                float m = Remaining(p);
                if (m > bestM) { best = p; bestM = m; tie = false; } else if (m == bestM) tie = true;
            }
            gameOver = true;
            winner = tie ? null : best;
            if (GameEnded != null) GameEnded();
        }
        if (TurnStarted != null) TurnStarted(activePlayer);
    }

    /// <summary>
    /// End the game now with <paramref name="winner"/> the winner (null: a draw), as a scenario's Win or Lose action
    /// does. Nothing if it is over already.
    /// </summary>
    public void EndGame(Player winner) {
        if (gameOver) return;
        gameOver = true;
        this.winner = winner;
        CancelPendingEndTurn();
        if (GameEnded != null) GameEnded();
    }

    /// <summary>The game has just ended (gameOver and winner are set): however it ended, an HQ taken, a side eliminated, the round limit. For logs.</summary>
    public static event System.Action GameEnded;

    /// <summary>A player's turn has begun (after its upkeep), or the game just ended. For logs.</summary>
    public static event System.Action<Player> TurnStarted;

    /// <summary>"Capturing HQ ends the game for that player."</summary>
    public void OnHQCaptured(Building hq, Player previousOwner) {
        if (previousOwner == null) return;
        if (rules == RuleSet.Sandbox) return;   // nobody is ever out
        if (!previousOwner.HasHQ) {
            Eliminate(previousOwner);
        }
        CheckVictory();
    }

    public void Eliminate(Player p) {
        if (p == null || p.eliminated) return;
        p.eliminated = true;
        List<Unit> doomed = new List<Unit>(p.units);
        for (int i = 0; i < doomed.Count; i++) {
            if (doomed[i] != null) doomed[i].Delete();
        }
    }

    public void CheckVictory() {
        if (gameOver) return;
        if (rules == RuleSet.Sandbox) return;   // the game never ends

        for (int i = 0; i < players.Count; i++) {
            Player p = players[i];
            if (p == null || p.eliminated) continue;
            bool hasHQ = p.HasHQ;
            if (hasHQ) p.hadHQ = true;
            // "Capturing HQ ends the game for that player" — and destroying an HQ unit
            // likewise. A player who never had an HQ (an old save) is out only when they
            // have nothing left.
            if (rules == RuleSet.Attrition) {
                // HQs are out of play: a side is out when it has nothing else left.
                if (CombatUnits(p) == 0 && (p.hadHQ || hasHQ || p.units.Count == 0)) Eliminate(p);
                continue;
            }
            if ((p.hadHQ && !hasHQ) || (!hasHQ && p.units.Count == 0)) Eliminate(p);
        }

        Player survivor = null;
        int alive = 0;
        for (int i = 0; i < players.Count; i++) {
            if (players[i] != null && !players[i].eliminated) { alive++; survivor = players[i]; }
        }
        if (alive <= 1) {
            gameOver = true;
            winner = survivor;
            if (GameEnded != null) GameEnded();
        }
    }

    /// <summary>Enemies of a given player, for threat aggregation.</summary>
    public IEnumerable<Player> EnemiesOf(Player p) {
        for (int i = 0; i < players.Count; i++) {
            if (players[i] != null && players[i] != p) yield return players[i];
        }
    }
}
