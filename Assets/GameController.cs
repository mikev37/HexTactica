using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Standard: destroy or capture the enemy HQ.
/// Attrition (a test variant): HQs cannot be attacked; a side is out when it has no units but its
/// HQ; after roundLimit rounds the side with the most strength x cost left (HQs not counted) wins.
/// </summary>
public enum RuleSet { Standard, Attrition }

public class GameController : MonoBehaviour
{
    [Header("Rules")]
    public RuleSet rules = RuleSet.Standard;
    [Tooltip("Attrition: rounds before the game is decided on what is left.")]
    public int roundLimit = 30;

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
        } while (activePlayer.eliminated && guard++ < players.Count);

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
