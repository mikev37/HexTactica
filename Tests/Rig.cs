using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HexTactica.AI;

namespace HexTactica.Testing {

    /// <summary>
    /// Constructs a real playable board in memory, bypassing prefabs and Awake/Start,
    /// so the AI can be executed and its behaviour asserted against.
    /// </summary>
    public static class Rig {

        public static GameController gc;
        public static UnitController uc;
        public static BuildingController bc;
        public static HexGenerator hg;

        public static void NewGame(int radius) {
            Building.all.Clear();

            gc = New<GameController>();
            gc.players = new List<Player>();
            gc.headless = true;
            SetStatic(typeof(GameController), "gc", gc);

            uc = New<UnitController>();
            uc.units = new List<Unit>();
            SetStatic(typeof(UnitController), "uc", uc);

            bc = New<BuildingController>();
            bc.buildings = new List<Building>();
            SetStatic(typeof(BuildingController), "bc", bc);
            BuildingController.Spawner = (p, u, t) => {
                Unit made = Spawn(u, p, t.x, t.y);
                PrefabLoader.AttachHubIfAny(u.name, made);
                return made;
            };

            hg = New<HexGenerator>();
            hg.width = radius * 4;
            hg.Grid = new HexTile[hg.width, hg.width, hg.width];
            hg.tiles = new List<HexTile>();
            SetStatic(typeof(HexGenerator), "hg", hg);

            BuildGrid(radius);
        }

        static void BuildGrid(int radius) {
            // Cube coordinates with x+y+z == 0.
            for (int x = -radius; x <= radius; x++) {
                for (int y = Math.Max(-radius, -x - radius); y <= Math.Min(radius, -x + radius); y++) {
                    int z = -x - y;
                    HexTile t = New<HexTile>();
                    t.x = x; t.y = y; t.z = z;
                    t.neighbors = new List<HexTile>();
                    t.aiData = new AITileData();
                    t.threat = new Dictionary<Player, Heat>();
                    t.supply = new Dictionary<Player, int>();
                    t.terrain = ScenarioFile.Terrain("Grassland");
                    hg.Grid[x + hg.width / 2, y + hg.width / 2, z + hg.width / 2] = t;
                    hg.tiles.Add(t);
                }
            }

            int[,] dirs = { { 1, -1, 0 }, { 1, 0, -1 }, { 0, 1, -1 }, { -1, 1, 0 }, { -1, 0, 1 }, { 0, -1, 1 } };
            foreach (HexTile t in hg.tiles) {
                for (int d = 0; d < 6; d++) {
                    HexTile n = hg.get(t.x + dirs[d, 0], t.y + dirs[d, 1], t.z + dirs[d, 2]);
                    if (n != null) t.neighbors.Add(n);
                }
            }
        }

        public static HexTile At(int x, int y) { return hg.get(x, y, -x - y); }

        public static Player AddPlayer(string name, long id) {
            Player p = New<Player>();
            p.personal_name = name;
            p.id = id;
            p.units = new List<Unit>();
            gc.players.Add(p);
            if (gc.activePlayer == null) gc.activePlayer = p;

            foreach (HexTile t in hg.tiles) {
                if (!t.threat.ContainsKey(p)) t.threat.Add(p, new Heat());
                if (!t.supply.ContainsKey(p)) t.supply.Add(p, 0);
            }
            return p;
        }

        public static AIPlayer AttachAI(Player p, AIProfile profile) {
            AIPlayer ai = p.gameObject.AddComponent<AIPlayer>();
            ai.player = p;
            ai.profile = profile;
            return ai;
        }

        public static HexTactica.Harness.BaselineAIPlayer AttachBaseline(
                Player p, HexTactica.Harness.AIKind kind) {
            var b = p.gameObject.AddComponent<HexTactica.Harness.BaselineAIPlayer>();
            b.player = p;
            b.kind = kind;
            return b;
        }

        /// <summary>A building from its prefab, anchored on (x, y), owned by <paramref name="owner"/> (null: neutral).</summary>
        public static Building PlaceBuilding(string prefab, Player owner, int x, int y) {
            HexTile tile = At(x, y);
            if (tile == null) throw new Exception("no tile at " + x + "," + y);
            if (tile.building != null) throw new Exception("building already at " + x + "," + y);
            return bc.Adopt(PrefabLoader.LoadBuilding(prefab), owner, tile);
        }

        public static Unit Spawn(Unit archetype, Player owner, int x, int y) {
            HexTile tile = At(x, y);
            if (tile == null) throw new Exception("no tile at " + x + "," + y);
            if (tile.occupier != null) throw new Exception("tile occupied at " + x + "," + y);

            Unit u = Clone(archetype);
            u.owner = owner;
            u.tile = tile;
            tile.occupier = u;
            u.path = new List<HexTile>();
            u.transported = new List<Unit>();
            u.active = true;
            uc.units.Add(u);
            owner.units.Add(u);
            return u;
        }

        public static SupplyPoint MakeHub(Unit u, int range) {
            SupplyPoint sp = u.gameObject.AddComponent<SupplyPoint>();
            sp.myUnit = u;
            sp.range = range;
            return sp;
        }

        public static void SetTerrain(int x, int y, TerrainData td) {
            HexTile t = At(x, y);
            if (t != null) t.terrain = td;
        }

        // ------------------------------------------------------------- plumbing

        public static T New<T>() where T : Component, new() {
            T c = new T();
            GameObject go = new GameObject(typeof(T).Name);
            go.Attach(c);
            return c;
        }

        /// <summary>Shallow field copy, so archetypes act as prefabs.</summary>
        public static Unit Clone(Unit source) {
            Unit u = New<Unit>();
            foreach (FieldInfo f in typeof(Unit).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                if (f.IsInitOnly) continue;
                f.SetValue(u, f.GetValue(source));
            }
            u.carries = source.carries != null ? new List<Locomotion>(source.carries) : new List<Locomotion>();
            u.name = source.name;
            return u;
        }

        static void SetStatic(Type t, string name, object value) {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (f == null) throw new Exception("no static " + name + " on " + t.Name);
            f.SetValue(null, value);
        }
    }
}
