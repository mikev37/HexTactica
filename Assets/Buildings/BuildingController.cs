using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry for buildings, mirroring UnitController so that save/load, clearing and
/// placement all work the same way they already do for units. Installs itself in any scene
/// that lacks one.
/// </summary>
public class BuildingController : MonoBehaviour {

    public static BuildingController bc;

    public List<Building> buildings = new List<Building>();

    /// <summary>How a produced unit is made (the harness swaps in its own); default UnitController.CreateUnit.</summary>
    public static System.Func<Player, Unit, HexTile, Unit> Spawner;

    void Awake() { bc = this; }
    void Start() { bc = this; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() {
        if (bc != null) return;
        GameObject go = new GameObject("BuildingController");
        bc = go.AddComponent<BuildingController>();
        go.AddComponent<BuildingMenu>();
        go.AddComponent<TransportMenu>();
        go.AddComponent<SupplyOverlay>();
    }

    /// <summary>Place a building from its prefab with its anchor on <paramref name="tile"/>. Null if any of its tiles is taken by another building.</summary>
    public Building Place(Player player, Building prefab, HexTile tile) {
        if (prefab == null || tile == null) return null;
        if (tile.building != null) return null;

        // Building art is drawn like a tile's upper layer: at the tile itself.
        Vector3 at = tile.transform.position;

        Building made = GameObject.Instantiate(prefab.gameObject, at, Quaternion.identity)
                                  .GetComponent<Building>();
        return Adopt(made, player, tile);
    }

    /// <summary>Register an instantiated building and stand it on its tiles.</summary>
    public Building Adopt(Building made, Player player, HexTile tile) {
        if (made == null) return null;
        made.owner = player;
        made.Occupy(tile);
        if (!buildings.Contains(made)) buildings.Add(made);
        if (!Building.all.Contains(made)) Building.all.Add(made);
        FogOfWar.Changed();
        made.RefreshFlag();
        return made;
    }

    public void Clear() {
        List<Building> all = new List<Building>(buildings);
        for (int i = 0; i < all.Count; i++) {
            if (all[i] == null) continue;
            all[i].Release();
            GameObject.Destroy(all[i].gameObject);
        }
        buildings.Clear();
        Building.all.Clear();
    }

    /// <summary>
    /// Build <paramref name="unit"/> at <paramref name="b"/>: paid from the owner's money, placed on
    /// the first free tile of the building it can stand on. It has used its turn. Null if the
    /// owner cannot afford it or there is no room. <paramref name="allowed"/>, if given, says which
    /// tiles may be built on (the AI passes AIAir.MayBuildOn: never a tile reserved for a landing).
    /// </summary>
    public Unit Produce(Building b, Unit unit, System.Func<HexTile, bool> allowed = null) {
        if (b == null || unit == null || b.owner == null || !b.CanBuild(unit)) return null;
        if (b.owner.money < unit.cost) return null;
        foreach (HexTile t in b.Footprint) {
            if (t.occupier != null || !unit.CanEnter(t)) continue;
            if (allowed != null && !allowed(t)) continue;
            Unit made = Spawner != null ? Spawner(b.owner, unit, t) : UnitController.uc.CreateUnit(b.owner, unit, t);
            if (made == null) continue;
            b.owner.money -= unit.cost;
            made.active = false;
            return made;
        }
        return null;
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
    public int capturePoints;
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
        capturePoints = b.capturePoints;
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

        if (!string.IsNullOrEmpty(personal_name)) b.personal_name = personal_name;
        b.supplies = supplies;
        b.damageLevel = damageLevel;
        // An owned building saved with no points (an older save) is fully held.
        b.capturePoints = owner != null && capturePoints <= 0 ? b.CaptureThreshold : capturePoints;
        b.capturingPlayer = capturing;
        b.RefreshFlag();
        return b;
    }
}
