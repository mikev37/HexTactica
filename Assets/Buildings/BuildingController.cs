using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry for buildings, mirroring UnitController so that save/load, clearing and
/// placement all work the same way they already do for units.
/// </summary>
public class BuildingController : MonoBehaviour {

    public static BuildingController bc;

    public List<Building> buildings = new List<Building>();

    void Awake() { bc = this; }
    void Start() { bc = this; }

    public Building Place(Player player, Building prefab, HexTile tile) {
        if (prefab == null || tile == null) return null;
        if (tile.building != null) return null;

        Vector3 at = tile.unitPlacement != null
                   ? tile.unitPlacement.transform.position
                   : tile.transform.position;

        Building made = GameObject.Instantiate(prefab.gameObject, at, Quaternion.identity)
                                  .GetComponent<Building>();
        made.tile = tile;
        made.owner = player;
        tile.building = made;
        buildings.Add(made);
        made.RefreshFlag();
        return made;
    }

    public void Clear() {
        List<Building> all = new List<Building>(buildings);
        for (int i = 0; i < all.Count; i++) {
            if (all[i] == null) continue;
            if (all[i].tile != null) all[i].tile.building = null;
            GameObject.Destroy(all[i].gameObject);
        }
        buildings.Clear();
        Building.all.Clear();
    }
}

[System.Serializable]
public struct BuildingData {
    public int x, y, z;
    public string gameobject_name;
    public string personal_name;
    public int type;
    public long owner_id;
    public int income;
    public bool producesSupply;
    public int supplyRange;
    public int supplies;
    public int suppliesMax;
    public int damageLevel;
    public int captureProgress;
    public long capturingPlayerId;

    public BuildingData(Building b) {
        x = b.tile != null ? b.tile.x : 0;
        y = b.tile != null ? b.tile.y : 0;
        z = b.tile != null ? b.tile.z : 0;
        gameobject_name = b.gameObject.name;
        personal_name = b.personal_name;
        type = (int)b.type;
        owner_id = b.owner != null ? b.owner.id : -1;
        income = b.income;
        producesSupply = b.producesSupply;
        supplyRange = b.supplyRange;
        supplies = b.supplies;
        suppliesMax = b.suppliesMax;
        damageLevel = b.damageLevel;
        captureProgress = b.captureProgress;
        capturingPlayerId = b.capturingPlayer != null ? b.capturingPlayer.id : -1;
    }

    public Building load() {
        BuildingData that = this;

        Player owner = null;
        Player capturing = null;
        GameController.gc.players.ForEach(pl => {
            if (pl.id == that.owner_id) owner = pl;
            if (pl.id == that.capturingPlayerId) capturing = pl;
        });

        HexTile tile = HexGenerator.hg.get(x, y, z);
        Building prefab = LevelEditor.loadBuilding(gameobject_name);
        if (prefab == null || tile == null) return null;

        Building b = BuildingController.bc.Place(owner, prefab, tile);
        if (b == null) return null;

        b.personal_name = personal_name;
        b.type = (BuildingType)type;
        b.income = income;
        b.producesSupply = producesSupply;
        b.supplyRange = supplyRange;
        b.supplies = supplies;
        b.suppliesMax = suppliesMax;
        b.damageLevel = damageLevel;
        b.captureProgress = captureProgress;
        b.capturingPlayer = capturing;
        b.RefreshFlag();
        return b;
    }
}
