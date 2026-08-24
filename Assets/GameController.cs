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

    private void Start() {
        gc = this;
    }

    public void Update() {
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
                x.threat[y].closestSupply = 0;
                x.threat[y].inSupply = false;
                x.aiData.attackValue = 0;
            });
            return 0;
        });
    }

    public static void RecalculateThreat() {
        ResetTileData();
        UnitController.uc.units.ForEach(x => x.updateThreat());
    }

    public void EndTurn() {
        activePlayer.EndTurn();
        int index = players.IndexOf(activePlayer);
        if (index == 0) {
            days++;
            activePlayer = players[players.Count - 1];
        } else {
            activePlayer = players[index - 1];
        }
        activePlayer.StartTurn();
    }
}
