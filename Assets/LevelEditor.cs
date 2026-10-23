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

	// Start is called before the first frame update
	void Start()
    {
        singleton = this;
        Debug.Log(Application.persistentDataPath);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown("s")) {
            Save();
        }
        if (Input.GetKeyDown("l")) {
            Load();
        }
    }

    void Save() {
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
            // The scenario's purchase settings (GameController inspector).
            json += ",\n\"purchases\":\"" + GameController.gc.PurchasesSetting + "\",\n\"allowedUnits\":\"" + GameController.gc.AllowedUnitsSetting + "\"";
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
        Debug.Log(json);

        string destination = Application.persistentDataPath + "/save.dat";
        Debug.Log(destination);
        //File file;

        /*if (File.Exists(destination)) file = File.OpenWrite(destination);
        else file = File.Create(destination);*/
        File.WriteAllText(destination, json);
    }

    void Load() {
        List<Unit> allunits = new List<Unit>();
        allunits.AddRange(UnitController.uc.units);
        allunits.ForEach(x => x.Delete());
        string destination = Application.persistentDataPath + "/save.dat";
        Debug.Log(destination);
        string json = File.ReadAllText(destination);
        Debug.Log(json);
        //HexGenerator.hg.load()
        JSONObject obj = new JSONObject(json);
        if (BuildingController.bc != null) BuildingController.bc.Clear();
        Debug.Log(obj.GetField("tiles"));
        HexTactica.Harness.ScenarioLoader.FloodWater();   // a save for a smaller board: the rest is water
        JSONObjectEnumerator numobj =
            obj.GetField("tiles").GetEnumerator();
        while (numobj.MoveNext()) {
            Debug.Log(numobj.Current.Print());
            TileData td = JsonUtility.FromJson<TileData>(numobj.Current.Print());
            td.load();
        }
        if (obj.GetField("players")) {
            JSONObjectEnumerator numplayer =
                obj.GetField("players").GetEnumerator();
            while (numplayer.MoveNext()) {
                Debug.Log(numplayer.Current.Print());
                PlayerData pd = JsonUtility.FromJson<PlayerData>(numplayer.Current.Print());
                Player loaded = pd.load();
                // Everyone starts in the game (a file saved after a side was knocked out says
                // "eliminated":true, and its turns were skipped from then on). A side with nothing
                // left is knocked out again by the next victory check, except in Sandbox.
                if (loaded != null) loaded.eliminated = false;
            }
            JSONObject pf = obj.GetField("purchases"), af = obj.GetField("allowedUnits");
            if (pf != null || af != null) GameController.gc.ApplyPurchaseRules(pf != null ? pf.stringValue : null, af != null ? af.stringValue : null);
            long active_player = obj.GetField("active").longValue;
            GameController.gc.players.ForEach(pl => { if (pl.id == active_player) GameController.gc.activePlayer = pl; });
        }
        if (obj.GetField("buildings")) {
            JSONObjectEnumerator numbuilding =
                obj.GetField("buildings").GetEnumerator();
            while (numbuilding.MoveNext()) {
                BuildingData bd = JsonUtility.FromJson<BuildingData>(numbuilding.Current.Print());
                bd.load();
            }
        }
        if (obj.GetField("units")) {
            JSONObjectEnumerator numunit =
                obj.GetField("units").GetEnumerator();
            while (numunit.MoveNext()) {
                Debug.Log(numunit.Current.Print());
                UnitData ud = JsonUtility.FromJson<UnitData>(numunit.Current.Print());
                ud.load();
            }
        }
    }

    public static TerrainData loadTerrain(string name) {
        foreach(TerrainData t in singleton.allTerrain) {
            if (t.name.Equals(name)) {
                return t;
            }
        }
        return null;
    }

    public static Building loadBuilding(string name) {
        name = name.Replace("(Clone)", "");
        if (singleton.allBuilding == null) return null;
        foreach (Building b in singleton.allBuilding) {
            if (b != null && b.name.Equals(name)) return b;
        }
        return null;
    }

    public static Unit loadUnit(string name) {
        name = name.Replace("(Clone)","");
        foreach (Unit t in singleton.allUnit) {
            if (t.name.Equals(name)) {
                return t;
            }
        }
        return null;
    }

    internal static Unit PlaceNewUnit(Player p, String unit_name, HexTile t) {
        Unit unit = loadUnit(unit_name);
        return UnitController.uc.CreateUnit(p, unit, t);
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

    public static void PlaceNewUnit(HexTile t) {
        PlaceNewUnit(GameController.gc.activePlayer, singleton.ut,t);
    }
}
