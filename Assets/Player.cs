using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player : MonoBehaviour
{
    public string personal_name;
    public long id;
    public Color displayColor;
    public Faction faction;
    public bool mainPlayer;
    public List<Unit> units;

    public void StartTurn() {
        Resupply();
        units.ForEach(x => {
            if(x.tile.isSupplied(this))
                x.Strength = (int)Mathf.Min(6, x.Strength + 1); 
        
        } );
    }

    public void Resupply() {
        HexGenerator.forAllTiles(x => { x.supply[this] = 0; return 0; });
        List<SupplyPoint> suppliers = new List<SupplyPoint>();
        units.ForEach(x => {
            SupplyPoint sp = x.GetComponent<SupplyPoint>();
            if (sp) {
                if (sp.producer && !sp.supplying) {
                    sp.SupplyZone(null);
                }
                suppliers.Add(sp);
            }
        });
        suppliers.ForEach(x => x.supplying = false);
    }

    public void EndTurn() {
        units.ForEach(x => {
            x.active = true;
            SupplyPoint sp = x.GetComponent<SupplyPoint>();
            if (sp) {
                sp.supplying = false;
            }
        });
    }
}


public struct PlayerData {
    public string personal_name;
    public long id;
    public Color displayColor;
    public Faction faction;
    public bool AI;
    public PlayerData(Player t) {
        personal_name = t.personal_name;
        id = t.id;
        displayColor = t.displayColor;
        faction = t.faction;
        AI = t.GetComponent<AIPlayer>();
    }

    public Player load() {
        PlayerData that = this;
        Player output = null;
        GameController.gc.players.ForEach(pl => { 
            if(pl.id == that.id) {
                output = pl;
            }
        });
        if(output == null) {
            GameObject go = GameObject.Instantiate(new GameObject());
            output = go.AddComponent<Player>();
        }
        output.personal_name = that.personal_name;
        output.displayColor = that.displayColor;
        output.faction = that.faction;
        if (AI && output.GetComponent<AIPlayer>() == null) {
            output.gameObject.AddComponent<AIPlayer>();
        }
        return output;
    }
}