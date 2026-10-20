using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameController : MonoBehaviour
{
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
    }

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
