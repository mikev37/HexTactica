using Defective.JSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class LevelEditor : MonoBehaviour
{
    public TerrainData td;
    public static LevelEditor singleton;
    public bool mouseDown;
	public bool editing,unit_placement,building_placement;
    public Unit ut;
    [Tooltip("Building placed by clicking a tile while building_placement is on (owned by the active player, or nobody with placeNeutral)")]
    public Building bt;
    [Tooltip("Buildings placed are neutral (owned by nobody) instead of the active player's")]
    public bool placeNeutral;

    public List<TerrainData> allTerrain;
    public List<Unit> allUnit;
    public List<Building> allBuilding;
    [Tooltip("Every faction a save may name (a player's faction is saved by name); their own units load by name too")]
    public List<Faction> allFaction;

	// Start is called before the first frame update
	void Start()
    {
        singleton = this;
        Debug.Log(Application.persistentDataPath);
    }

    // Update is called once per frame
    void Update()
    {
        // The keys and the HexTactica menu go the same way: SaveBoard, and ScenarioLoader.LoadBoardFromPath.
        // Not while a menu is open over the game (the map editor, the in-game menu): their own buttons save and load,
        // and a name typed there would hit the keys.
        if (GameController.Paused) return;
        if (Input.GetKeyDown("s")) SaveBoard(QuickSavePath);
        if (Input.GetKeyDown("l")) HexTactica.Harness.ScenarioLoader.LoadBoardFromPath(QuickSavePath);
    }

    /// <summary>Where the S and L keys save and load: save.dat in the game's data folder.</summary>
    public static string QuickSavePath { get { return Path.Combine(Application.persistentDataPath, "save.dat"); } }

    /// <summary>Save the board as it stands to <paramref name="path"/> (the S key, and HexTactica ▸ Save Scenario File).</summary>
    public static void SaveBoard(string path) {
        File.WriteAllText(path, BoardJson());
        Debug.Log("Saved the board to " + path);
    }

    /// <summary>
    /// Save a game in progress (the in-game menu): the board as a scenario file, plus where its game log stands, so
    /// loading it goes on with the same log (GameLog.MarkSave, Continue) and the post-game report covers the whole game.
    /// </summary>
    public static void SaveGame(string path) {
        HexTactica.Harness.GameLog.SavePoint at = HexTactica.Harness.GameLog.MarkSave(path);
        File.WriteAllText(path, HexTactica.Harness.ScenarioLoader.WithSavePoint(BoardJson(), at));
        Debug.Log("Saved the game to " + path);
    }

    /// <summary>The board as a scenario file: tiles, players, purchase settings, buildings and units (what the S key saves, and HexTactica ▸ Save Scenario File).</summary>
    public static string BoardJson() {
        string json = "{\n\"width\":" + HexGenerator.hg.width + ",\n \"tiles\":[";

        HexGenerator.forAllTiles(t => {
            TileData td = new TileData(t);
            string json_td = JsonUtility.ToJson(td);
            json += json_td + ",";
            return 0; });
        json = json.Substring(0, json.Length - 1) + "\n]";

        if (GameController.gc.players.Count > 0) {
            json += ",\n";
            json += "\n\"players\":[";
            GameController.gc.players.ForEach(player => {
                PlayerData pd = new PlayerData(player);
                string json_pd = JsonUtility.ToJson(pd);
                json += json_pd + ",";
            });
            json = json.Substring(0, json.Length - 1) + "\n],\n";

            json += "\n\"active\":" + GameController.gc.activePlayer.id;
            // The day (round) the game is on: a save goes on from where it was (0 for a scenario's start).
            json += ",\n\"day\":" + GameController.gc.days;
            // The scenario's purchase settings (GameController inspector).
            json += ",\n\"purchases\":\"" + GameController.gc.PurchasesSetting + "\",\n\"allowedUnits\":\"" + GameController.gc.AllowedUnitsSetting + "\"";
            // Fog of war (GameController inspector).
            json += ",\n\"fog\":\"" + GameController.gc.FogSetting + "\"";
            // Faction special units (GameController inspector, the pre-game screen).
            json += ",\n\"factionSpecialUnits\":\"" + GameController.gc.SpecialUnitsSetting + "\"";
            // The rules the map fixes (the pre-game screen shows them and cannot change them).
            json += ",\n\"fixedRules\":\"" + GameController.gc.FixedRulesSetting + "\"";
        }
        if (BuildingController.bc != null && BuildingController.bc.buildings.Count > 0) {
            json += ",\n";
            json += "\n\"buildings\":[";
            BuildingController.bc.buildings.ForEach(b => {
                if (b == null) return;
                BuildingData bd = new BuildingData(b);
                json += JsonUtility.ToJson(bd) + ",";
            });
            json = json.Substring(0, json.Length - 1) + "\n]";
        }
        // A unit destroyed without leaving the list (Unity: == null) is not on the board.
        UnitController.uc.units.RemoveAll(u => u == null);
        if (UnitController.uc.units.Count > 0) {
            json += ",\n";
            json += "\n\"units\":[";
            UnitController.uc.units.ForEach(unit => {
                UnitData ud = new UnitData(unit);
                string json_ud = JsonUtility.ToJson(ud);
                json += json_ud + ",";
            });
            json = json.Substring(0, json.Length - 1) + "\n]\n";
        }
        json += "}";
        return json;
    }


    /// <summary>
    /// The terrain of that name: one in the editor's list (allTerrain), or one the game itself puts
    /// on tiles, so a save can hold it without the list having it: a building's ground (Building.ground,
    /// such as BuildingGround) or what a terrain becomes when destroyed
    /// (TerrainData.destroyedInto). Null if none has that name.
    /// </summary>
    /// <summary>Terrains renamed since saves were written: old name, new name.</summary>
    public static readonly System.Collections.Generic.Dictionary<string, string> renamed = new System.Collections.Generic.Dictionary<string, string> { { "BuildingGround", "Urban" } };

    public static TerrainData loadTerrain(string name) {
        TerrainData td = FindTerrain(name);
        string now;
        if (td == null && name != null && renamed.TryGetValue(name, out now)) td = FindTerrain(now);
        return td;
    }

    static TerrainData FindTerrain(string name) {
        if (string.IsNullOrEmpty(name)) return null;
        // The Resources folder has every terrain (GameAssets); the scene's lists, when filled, come first.
        if (singleton == null) return GameAssets.Terrain(name);
        if (singleton.allTerrain != null)
            foreach (TerrainData t in singleton.allTerrain)
                if (t != null && t.name.Equals(name)) return t;
        if (singleton.allBuilding != null)
            foreach (Building b in singleton.allBuilding) {
                if (b == null) continue;
                if (b.ground != null && b.ground.name.Equals(name)) return b.ground;
            }
        if (singleton.allTerrain != null)
            foreach (TerrainData t in singleton.allTerrain) {
                TerrainData d = t != null ? t.destroyedInto : null;
                for (int n = 0; d != null && n < 16; d = d.destroyedInto, n++)
                    if (d.name.Equals(name)) return d;
            }
        return GameAssets.Terrain(name);
    }

    public static Building loadBuilding(string name) {
        name = name.Replace("(Clone)", "").Trim();
        if (singleton != null && singleton.allBuilding != null)
            foreach (Building b in singleton.allBuilding) {
                if (b != null && b.name.Equals(name)) return b;
            }
        return GameAssets.Building(name);
    }

    /// <summary>Units renamed since saves were written: old name, new name.</summary>
    /// <summary>
    /// The name a unit saved as <paramref name="name"/> goes by now, if it has none of that name: the same name without a
    /// trailing " Variant" (prefab variants that were renamed to their plain names, "Truck Variant" → "Truck"). Null: none.
    /// </summary>
    public static string RenamedUnit(string name) {
        const string suffix = " Variant";
        return name != null && name.EndsWith(suffix) ? name.Substring(0, name.Length - suffix.Length).Trim() : null;
    }

    /// <summary>
    /// The unit prefab saved as <paramref name="name"/>: in the scene's list (allUnit) if it is filled, else from the
    /// Resources folder (GameAssets: every unit, faction variants included). A name not found that ends in " Variant" is
    /// tried without it (prefab variants renamed to plain names since the save: "Truck Variant" → "Truck").
    /// </summary>
    public static Unit loadUnit(string name) {
        name = name.Replace("(Clone)","").Trim();
        if (singleton != null && singleton.allUnit != null)
            foreach (Unit t in singleton.allUnit) if (t != null && t.name.Equals(name)) return t;
        // A faction's own units (its versions and extras) need not be on allUnit.
        if (singleton != null && singleton.allFaction != null) foreach (Faction f in singleton.allFaction) if (f != null) foreach (Unit t in f.OwnUnits()) if (t.name.Equals(name)) return t;
        Unit r = GameAssets.Unit(name);
        if (r != null) return r;
        string now = RenamedUnit(name);
        return now != null ? loadUnit(now) : null;
    }

    /// <summary>The faction saved as <paramref name="name"/>: in the scene's list (allFaction) if there, else from the Resources folder; null if none.</summary>
    public static Faction FindFaction(string name) {
        if (string.IsNullOrEmpty(name)) return null;
        if (singleton != null && singleton.allFaction != null) foreach (Faction f in singleton.allFaction) if (f != null && f.name == name) return f;
        Faction r = GameAssets.Faction(name);
        if (r == null) Debug.LogWarning("Faction '" + name + "' is in neither the LevelEditor's allFaction list nor Resources/Factions: the player gets the base units");
        return r;
    }

    /// <summary>
    /// Place the unit saved as <paramref name="unit_name"/> for <paramref name="p"/>: as <paramref name="p"/>'s faction
    /// fields it (a base unit becomes the faction's version of it, Faction.Map — the visually right one), the unit as
    /// named when the faction has no version of it.
    /// </summary>
    internal static Unit PlaceNewUnit(Player p, String unit_name, HexTile t) {
        Unit unit = UnitFor(p, unit_name);
        return unit != null ? UnitController.uc.CreateUnit(p, unit, t) : null;
    }

    /// <summary>The prefab <paramref name="p"/> fields for a unit saved as <paramref name="unit_name"/> (its faction's version of it, see PlaceNewUnit); null if there is none of that name.</summary>
    internal static Unit UnitFor(Player p, String unit_name) {
        Unit unit = loadUnit(unit_name);
        Faction f = p != null ? p.faction : null;
        if (f != null && unit != null) {
            // Saved as another faction's version ("Grunt Empire Variant" for a side now Federation): back to the base unit first.
            Unit b = f.BaseOf(unit);
            if (b == unit) foreach (Faction any in AllFactions()) { Unit x = any.BaseOf(unit); if (x != unit) { b = x; break; } }
            Unit own = f.Map(b);
            if (own != null) unit = own;
        }
        return unit;
    }

    /// <summary>Every faction known: the scene's list (allFaction), then the Resources folder's.</summary>
    static IEnumerable<Faction> AllFactions() {
        if (singleton != null && singleton.allFaction != null) foreach (Faction f in singleton.allFaction) if (f != null) yield return f;
        foreach (Faction f in GameAssets.AllFactions) yield return f;
    }

    internal static Unit PlaceNewUnit(Player p, Unit unit, HexTile t) {
        return UnitController.uc.CreateUnit(p, unit, t);
    }

    public static void PlaceNewBuilding(HexTile t) {
        if (singleton.bt == null || BuildingController.bc == null) return;
        if (t.building != null) {
            // Clicking a building again removes it.
            Building old = t.building;
            old.Release();
            BuildingController.bc.buildings.Remove(old);
            Building.all.Remove(old);
            GameObject.Destroy(old.gameObject);
            return;
        }
        BuildingController.bc.Place(singleton.placeNeutral ? null : GameController.gc.activePlayer, singleton.bt, t);
    }

    /// <summary>Placing units: on an empty tile, a new one; clicking a unit again removes it (as with buildings).</summary>
    public static void PlaceNewUnit(HexTile t) {
        if (t.occupier != null) {
            // Taken off the map being edited, not killed in a game.
            Unit.Wiping = true;
            try { t.occupier.Delete(); } finally { Unit.Wiping = false; }
            return;
        }
        PlaceNewUnit(GameController.gc.activePlayer, singleton.ut,t);
    }
}
