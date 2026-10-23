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

    [Header("Purchases")]
    [Tooltip("Can units be bought at buildings? (A scenario sets this; when it does not, Standard and Sandbox allow purchases and Attrition does not.)")]
    public bool purchasesAllowed = true;
    [Tooltip("The units that may be bought, by prefab name (Grunt, Tank, Supplies ...). Empty: any unit a building can build.")]
    public List<string> allowedUnits = new List<string>();

    /// <summary>A unit's prefab name ("Tank" for "Tank(Clone)").</summary>
    public static string UnitName(Unit u) {
        return u == null ? "" : u.name.Replace("(Clone)", "").Trim();
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
        if (gc != null && gc.rules == RuleSet.Attrition) {
            SupplyPoint sp = u.GetComponent<SupplyPoint>();
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
            SupplyPoint sp = u.GetComponent<SupplyPoint>();
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
            SupplyPoint sp = u.GetComponent<SupplyPoint>();
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
    }

    public void Update() {
        if (headless) return;
        if (Input.GetKeyDown("t")) {
            RecalculateThreat();
            HexGenerator.forAllTiles(x => {
                x.textField.text = x.threat[GameController.gc.activePlayer].firepower + "";
                return 0;
            });
        }
        if (Input.GetKeyDown(KeyCode.Space)) {
            EndTurn();
        }
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

    public static void RecalculateThreat() {
        ResetTileData();
        List<Unit> snapshot = new List<Unit>(UnitController.uc.units);
        snapshot.ForEach(x => { if (x != null) x.updateThreat(); });
    }

    public void EndTurn() {
        if (gameOver) return;
        // Infantry that ended the turn on a building without attacking captures it.
        Building.ResolveCaptures(activePlayer);
        if (gameOver) return;

        activePlayer.EndTurn();

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
        }
        if (TurnStarted != null) TurnStarted(activePlayer);
    }

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
        }
    }

    /// <summary>Enemies of a given player, for threat aggregation.</summary>
    public IEnumerable<Player> EnemiesOf(Player p) {
        for (int i = 0; i < players.Count; i++) {
            if (players[i] != null && players[i] != p) yield return players[i];
        }
    }
}
