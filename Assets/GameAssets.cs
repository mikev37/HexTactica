using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game's units, buildings, terrains and factions, by name, from the Resources folder (Assets/Resources/Units,
/// Buildings/Prefabs, Tiles, Factions — any subfolder: faction variants live in their faction's folder). Saves and
/// scenarios name them; loading looks them up here, so nothing has to be listed on the scene's LevelEditor (its lists,
/// when filled, are looked at first). Loaded once, the first time anything is asked for (main thread: Resources).
/// </summary>
public static class GameAssets {
    static Dictionary<string, Unit> units;
    static Dictionary<string, Building> buildings;
    static Dictionary<string, TerrainData> terrains;
    static Dictionary<string, Faction> factions;
    static GameObject parachute;
    static bool parachuteLooked;

    /// <summary>The unit prefab named <paramref name="name"/> ("(Clone)" ignored), or null.</summary>
    public static Unit Unit(string name) { Load(); Unit u; return name != null && units.TryGetValue(Clean(name), out u) ? u : null; }
    public static Building Building(string name) { Load(); Building b; return name != null && buildings.TryGetValue(Clean(name), out b) ? b : null; }
    public static TerrainData Terrain(string name) { Load(); TerrainData t; return name != null && terrains.TryGetValue(name, out t) ? t : null; }
    public static Faction Faction(string name) { Load(); Faction f; return name != null && factions.TryGetValue(name, out f) ? f : null; }
    public static IEnumerable<Unit> AllUnits { get { Load(); return units.Values; } }
    public static IEnumerable<Building> AllBuildings { get { Load(); return buildings.Values; } }
    public static IEnumerable<TerrainData> AllTerrains { get { Load(); return terrains.Values; } }
    public static IEnumerable<Faction> AllFactions { get { Load(); return factions.Values; } }

    /// <summary>
    /// The parachute a unit put down from a transport plane drops with (Unit.parachutePrefab overrides it per transport):
    /// one prefab for every plane, Assets/Resources/Units/Prefabs/Parachute. Null when there is none (the drop happens
    /// without it).
    /// </summary>
    public static GameObject Parachute {
        get {
            if (!parachuteLooked) { parachuteLooked = true; parachute = Resources.Load<GameObject>("Units/Prefabs/Parachute"); }
            return parachute;
        }
    }

    static string Clean(string name) { return name.Replace("(Clone)", "").Trim(); }

    static void Load() {
        if (units != null) return;
        units = new Dictionary<string, Unit>();
        buildings = new Dictionary<string, Building>();
        terrains = new Dictionary<string, TerrainData>();
        factions = new Dictionary<string, Faction>();
        foreach (GameObject g in Resources.LoadAll<GameObject>("Units")) {
            Unit u = g != null ? g.GetComponent<Unit>() : null;
            if (u != null && !units.ContainsKey(g.name)) units[g.name] = u;
        }
        foreach (GameObject g in Resources.LoadAll<GameObject>("Buildings")) {
            Building b = g != null ? g.GetComponent<Building>() : null;
            if (b != null && !buildings.ContainsKey(g.name)) buildings[g.name] = b;
        }
        foreach (TerrainData t in Resources.LoadAll<TerrainData>("Tiles")) if (t != null && !terrains.ContainsKey(t.name)) terrains[t.name] = t;
        foreach (Faction f in Resources.LoadAll<Faction>("Factions")) if (f != null && !factions.ContainsKey(f.name)) factions[f.name] = f;
    }
}
