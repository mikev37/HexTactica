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
                    t.terrain = Terrain.plains;
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

        public static Building AddBuilding(BuildingType type, Player owner, int x, int y,
                                           bool producesSupply, int supplyRange) {
            HexTile tile = At(x, y);
            Building b = New<Building>();
            b.type = type;
            b.owner = owner;
            b.tile = tile;
            b.producesSupply = producesSupply;
            b.supplyRange = supplyRange;
            b.supplyScramble = 1;
            b.supplies = 999;
            b.suppliesMax = 999;
            b.income = type == BuildingType.City ? 100 : 0;
            b.captureThreshold = 6;
            tile.building = b;
            Building.all.Add(b);
            bc.buildings.Add(b);
            return b;
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
            u.name = source.name;
            return u;
        }

        static void SetStatic(Type t, string name, object value) {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (f == null) throw new Exception("no static " + name + " on " + t.Name);
            f.SetValue(null, value);
        }
    }

    /// <summary>Terrain fixtures.</summary>
    public static class Terrain {
        public static TerrainData plains = Make("Plains", 0, 0, 0, false);
        public static TerrainData forest = Make("Forest", 2, 3, 0, false);
        public static TerrainData hill = Make("Hill", 2, 2, 1, false);
        public static TerrainData mountain = Make("Mountain", 4, 3, 2, false);
        public static TerrainData swamp = Make("Swamp", 3, 1, 0, false);
        public static TerrainData road = Make("Road", 0, 0, 0, true);
        public static TerrainData water = Make("Water", 0, 0, -1, false);

        static TerrainData Make(string name, int moveability, int cover, int elevation, bool isRoad) {
            TerrainData td = new TerrainData();
            td.name = name;
            td.moveability = moveability;
            td.cover = cover;
            td.elevation = elevation;
            td.road = isRoad;
            return td;
        }
    }

    /// <summary>Unit archetypes roughly matching the design doc's roster.</summary>
    public static class Archetypes {

        public static Unit Rifle() {
            Unit u = Base("Grunt");
            u.Firepower = 4; u.Armor = 1; u.Piercing = 1; u.Concealment = 3;
            u.AttackRange = 1; u.moveAndAttack = true; u.retaliates = true;
            u.moveRange = 3; u.terrainScramble = 3; u.locomotion = Locomotion.Boots;
            u.cost = 100;
            return u;
        }

        public static Unit Tank() {
            Unit u = Base("MainTank");
            u.Firepower = 6; u.Armor = 5; u.Piercing = 4; u.Concealment = 1;
            u.AttackRange = 1; u.moveAndAttack = true; u.retaliates = true;
            u.moveRange = 5; u.terrainScramble = 2; u.locomotion = Locomotion.Treads;
            u.cost = 300;
            return u;
        }

        public static Unit Artillery() {
            Unit u = Base("Artillery");
            u.Firepower = 7; u.Armor = 1; u.Piercing = 2; u.Concealment = 1;
            u.AttackRange = 3; u.MinRange = 1; u.moveAndAttack = false; u.retaliates = false;
            u.moveRange = 3; u.terrainScramble = 1; u.locomotion = Locomotion.Wheels;
            u.cost = 250;
            return u;
        }

        public static Unit Recon() {
            Unit u = Base("Recon");
            u.Firepower = 2; u.Armor = 2; u.Piercing = 0; u.Concealment = 1;
            u.AttackRange = 1; u.moveAndAttack = true; u.retaliates = true;
            u.moveRange = 7; u.terrainScramble = 0; u.locomotion = Locomotion.Wheels;
            u.cost = 80;
            return u;
        }

        /// <summary>Supply hub: no firepower, so it must never be sent to fight.</summary>
        public static Unit Truck() {
            Unit u = Base("Truck");
            u.Firepower = 0; u.Armor = 1; u.Concealment = 0;
            u.AttackRange = 0; u.moveAndAttack = false; u.retaliates = false;
            u.moveRange = 6; u.terrainScramble = 0; u.locomotion = Locomotion.Wheels;
            u.weight = 2; u.cost = 120;
            return u;
        }

        /// <summary>Carries two weight of cargo. Fast on roads.</summary>
        public static Unit APC() {
            Unit u = Base("APC");
            u.Firepower = 1; u.Armor = 3; u.Concealment = 1;
            u.AttackRange = 1; u.moveAndAttack = true; u.retaliates = true;
            u.moveRange = 8; u.terrainScramble = 1; u.locomotion = Locomotion.Wheels;
            u.transportSlots = 2; u.maxWeight = 4; u.weight = 3; u.cost = 150;
            return u;
        }

        /// <summary>Ignores terrain entirely — the interesting transport for supply.</summary>
        public static Unit Helicopter() {
            Unit u = Base("TransportHeli");
            u.Firepower = 0; u.Armor = 1; u.Evasion = 3; u.Concealment = 0;
            u.AttackRange = 0; u.moveAndAttack = false; u.retaliates = false;
            u.moveRange = 10; u.terrainScramble = 5; u.locomotion = Locomotion.Rotor;
            u.elevationMax = 3; u.elevationMin = -1;
            u.transportSlots = 1; u.maxWeight = 2; u.weight = 4; u.cost = 400;
            return u;
        }

        static Unit Base(string name) {
            Unit u = Rig.New<Unit>();
            u.name = name;
            u.personal_name = name;
            u.Strength = 6;
            u.Veterancy = 0;
            u.ammo = 6; u.ammoMax = 6; u.ammoPerShot = 1;
            u.fuel = 40; u.fuelMax = 40; u.minFuelPerTile = 1; u.minFuelPerTurn = 1;
            u.supplies = 5; u.suppliesMax = 5;
            u.elevationMin = 0; u.elevationMax = 2;
            u.path = new List<HexTile>();
            u.transported = new List<Unit>();
            u.weight = 1;
            return u;
        }
    }
}
