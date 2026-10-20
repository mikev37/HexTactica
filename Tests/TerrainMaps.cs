using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using HexTactica.AI;
using HexTactica.Harness;
using HexTactica.Testing;

/// <summary>
/// Small maps with real chokepoints, and a turn-by-turn reasoning log.
///
/// Generation (as specified): plant mountains, water and forest at random by percentage, then
/// cut two paths of open land from one HQ to the other so a connection always exists. Water
/// is impassable to every land unit; mountains only to vehicles (grunts climb them; tanks,
/// guns and trucks cannot). The HQ neighbourhoods are cleared for deployment. Terrain values
/// come from the real assets (Grassland, Forest, Mountains, Water), not the test fixtures.
///
///   PROBE=maps                      the scenario set, ≤100 games, one line per game
///   PROBE=mapgame MAP=n SEAT=A|B THEM=Greedy|Legacy|Full [ROUNDS=r] [SIDE=A|B|both]
///                                   one game: the board each round and every decision's tiers
///   PROBE=mapshow                   just draw the maps
/// </summary>
public static class TerrainMaps {

    public struct Spec {
        public string name;
        public int radius, seed;
        public float mountain, water, forest;
        public Spec(string name, int radius, int seed, float mountain, float water, float forest) {
            this.name = name; this.radius = radius; this.seed = seed;
            this.mountain = mountain; this.water = water; this.forest = forest;
        }
    }

    /// <summary>The scenario maps: the specified mix, then lighter and heavier terrain, two sizes.</summary>
    public static readonly Spec[] Maps = {
        new Spec("std-r5",    5, 11, 0.10f, 0.10f, 0.30f),
        new Spec("std-r6",    6, 23, 0.10f, 0.10f, 0.30f),
        new Spec("open-r5",   5, 37, 0.05f, 0.05f, 0.15f),
        new Spec("rough-r5",  5, 41, 0.15f, 0.15f, 0.30f),
        new Spec("rough-r6",  6, 53, 0.15f, 0.15f, 0.35f),
        new Spec("choke-r5",  5, 67, 0.20f, 0.20f, 0.20f),
    };

    static TerrainData grass, forest, mountain, water;

    static void LoadTerrain() {
        if (grass != null) return;
        grass = Asset("Grassland"); forest = Asset("Forest"); mountain = Asset("Mountains"); water = Asset("Water");
    }

    static TerrainData Asset(string name) {
        string path = Path.Combine(PrefabLoader.AssetsRoot, "Tiles", name + ".asset");
        TerrainData td = new TerrainData();
        td.name = name;
        foreach (string raw in File.ReadAllLines(path)) {
            string line = raw.Trim();
            int c = line.IndexOf(':');
            if (c < 0) continue;
            string k = line.Substring(0, c).Trim(), v = line.Substring(c + 1).Trim();
            int n;
            if (!int.TryParse(v, out n)) continue;
            if (k == "cover") td.cover = n;
            else if (k == "moveability") td.moveability = n;
            else if (k == "elevation") td.elevation = n;
        }
        return td;
    }

    static int rng;
    static int Next(int bound) {
        rng = (int)((rng * 1103515245L + 12345L) & 0x7FFFFFFF);
        return bound <= 0 ? 0 : (rng >> 8) % bound;
    }

    public static HexTile HqA(Spec s) { return Rig.At(-(s.radius - 1), 0); }
    public static HexTile HqB(Spec s) { return Rig.At(s.radius - 1, 0); }

    /// <summary>Build the board for <paramref name="s"/> (no units).</summary>
    public static void Build(Spec s) {
        LoadTerrain();
        Rig.NewGame(s.radius);
        rng = s.seed * 7919 + 17;
        foreach (HexTile t in Rig.hg.tiles) {
            int roll = Next(10000);
            float r = roll / 10000f;
            if (r < s.mountain) t.terrain = mountain;
            else if (r < s.mountain + s.water) t.terrain = water;
            else if (r < s.mountain + s.water + s.forest) t.terrain = forest;
            else t.terrain = grass;
        }
        HexTile a = HqA(s), b = HqB(s);
        int k = Math.Max(1, s.radius / 2);
        // Two paths, one through each half of the map.
        Cut(a, Rig.At(0, -k) ?? Rig.At(k, -k), b);
        Cut(a, Rig.At(0, k) ?? Rig.At(-k, k), b);
        // Deployment: each HQ and its neighbours, plus the two ring-2 tiles nearest the centre.
        foreach (HexTile hq in new[] { a, b }) {
            hq.terrain = grass;
            foreach (HexTile n in hq.neighbors) n.terrain = grass;
            foreach (HexTile n in Deployment(hq, 9)) n.terrain = grass;
        }
    }

    static void Cut(HexTile from, HexTile via, HexTile to) {
        HexTile at = from;
        foreach (HexTile goal in new[] { via, to }) {
            int guard = 0;
            while (at != goal && guard++ < 200) {
                at.terrain = grass;
                int d = at.Distance(goal);
                List<HexTile> closer = new List<HexTile>();
                foreach (HexTile n in at.neighbors) if (n.Distance(goal) < d) closer.Add(n);
                closer.Sort(CombatMath.CompareTiles);
                at = closer[Next(closer.Count)];
            }
            at.terrain = grass;
        }
    }

    /// <summary>The <paramref name="n"/> tiles nearest the HQ (excluding it), nearest the map centre first among equals.</summary>
    static List<HexTile> Deployment(HexTile hq, int n) {
        List<HexTile> tiles = new List<HexTile>(Rig.hg.tiles);
        tiles.Remove(hq);
        HexTile centre = Rig.At(0, 0);
        tiles.Sort((x, y) => {
            int c = x.Distance(hq).CompareTo(y.Distance(hq));
            if (c != 0) return c;
            c = x.Distance(centre).CompareTo(y.Distance(centre));
            return c != 0 ? c : CombatMath.CompareTiles(x, y);
        });
        return tiles.GetRange(0, Math.Min(n, tiles.Count));
    }

    /// <summary>
    /// Board plus armies. Each side: HQ, supply truck, 3 grunts, tank, artillery. extraFor
    /// (0 none, 1 seat A, 2 seat B) adds a tank and a grunt — one more of each, which is about
    /// +47% fighting strength (not "+20%", as the earlier tables called it).
    /// </summary>
    public static void Setup(Spec s, Matchups.Kind aKind, Matchups.Kind bKind, int extraFor, out Player a, out Player b) {
        Build(s);
        a = Rig.AddPlayer("A", 1);
        b = Rig.AddPlayer("B", 2);
        foreach (Player p in new[] { a, b }) {
            HexTile hq = p == a ? HqA(s) : HqB(s);
            List<HexTile> dep = Deployment(hq, 9);
            Spawn("TempHQ", p, hq);
            string[] roster = { "Grunt", "Grunt", "Tank", "Grunt", "Supplies", "Artillery" };
            for (int i = 0; i < roster.Length; i++) Spawn(roster[i], p, dep[i]);
            if (extraFor == (p == a ? 1 : 2)) { Spawn("Tank", p, dep[6]); Spawn("Grunt", p, dep[7]); }
        }
        Matchups.AttachKind(a, aKind);
        Matchups.AttachKind(b, bKind);
        Rig.gc.activePlayer = a;
    }

    static void Spawn(string n, Player p, HexTile t) {
        Unit u = Rig.Spawn(PrefabLoader.Load(n), p, t.x, t.y);
        PrefabLoader.AttachHubIfAny(n, u);
    }

    // ------------------------------------------------------------------ drawing

    /// <summary>
    /// The board as text. Rows are the y axis; each row is offset by half a hex. Terrain:
    /// . grass, f forest, ^ mountain, ~ water. Units: seat A upper case, seat B lower case —
    /// H HQ, S supply truck, G grunt, T tank, A artillery.
    /// </summary>
    public static string Draw(int radius) {
        StringBuilder sb = new StringBuilder();
        for (int y = -radius; y <= radius; y++) {
            char[] row = new string(' ', radius * 4 + 3).ToCharArray();
            for (int x = -radius; x <= radius; x++) {
                HexTile t = Rig.At(x, y);
                if (t == null) continue;
                int col = 2 * x + y + radius * 2;
                if (col < 0 || col >= row.Length) continue;
                row[col] = Glyph(t);
            }
            sb.Append(string.Format("  {0,3} ", y)).Append(new string(row).TrimEnd()).Append('\n');
        }
        return sb.ToString();
    }

    static char Glyph(HexTile t) {
        if (t.occupier != null) {
            Unit u = t.occupier;
            char c = 'U';
            if (u.GetComponent<SupplyPoint>() != null) c = u.GetComponent<SupplyPoint>().producer ? 'H' : 'S';
            else if (u.name.StartsWith("Grunt")) c = 'G';
            else if (u.name.StartsWith("Tank")) c = 'T';
            else if (u.name.StartsWith("Artillery")) c = 'A';
            return u.owner != null && u.owner.personal_name == "A" ? c : char.ToLower(c);
        }
        if (t.terrain == null) return '?';
        switch (t.terrain.name) {
            case "Forest": return 'f';
            case "Mountains": return '^';
            case "Water": return '~';
            default: return '.';
        }
    }

    public static void Show() {
        foreach (Spec s in Maps) {
            Build(s);
            Console.WriteLine(s.name + " (radius " + s.radius + ", seed " + s.seed + ", mountain " + s.mountain + " water " + s.water + " forest " + s.forest + ")");
            Player a, b;
            Setup(s, Matchups.Kind.Greedy, Matchups.Kind.Greedy, 0, out a, out b);
            Console.Write(Draw(s.radius));
            Console.WriteLine();
        }
    }

    // ---------------------------------------------------------------- scenarios

    /// <summary>
    /// The scenario set: every map, both seats, against Legacy and against Greedy, plus a mirror
    /// per map and the outnumbered case against Legacy. 6 × (2 + 2 + 2) + 6 = 42 games.
    /// </summary>
    public static void RunSet() {
        int w = 0, l = 0, d = 0;
        foreach (Spec s in Maps) {
            foreach (Matchups.Kind them in new[] { Matchups.Kind.Legacy, Matchups.Kind.Greedy }) {
                foreach (bool meA in new[] { true, false }) Tally(Game(s, them, meA, 0), ref w, ref l, ref d);
            }
            foreach (bool meA in new[] { true, false }) Tally(Game(s, Matchups.Kind.Legacy, meA, meA ? 2 : 1), ref w, ref l, ref d);
            Game(s, Matchups.Kind.Full, true, 0);
        }
        Console.WriteLine(string.Format("TOTAL (excluding mirrors): {0}W {1}L {2}D", w, l, d));
    }

    static void Tally(string r, ref int w, ref int l, ref int d) {
        if (r == "me") w++; else if (r == "them") l++; else d++;
    }

    static string Game(Spec s, Matchups.Kind them, bool meA, int extraFor) {
        Player a, b;
        Setup(s, meA ? Matchups.Kind.Full : them, meA ? them : Matchups.Kind.Full, extraFor, out a, out b);
        Player me = meA ? a : b, other = meA ? b : a;
        Matchups.Play();
        string r = Rig.gc.winner == me ? "me" : Rig.gc.winner == other ? "them" : "none";
        string extra = extraFor == 0 ? "equal" : (extraFor == 1) == meA ? "us+TG" : "them+TG";
        Console.WriteLine(string.Format("  {0,-9} Full({1}) vs {2,-6} {3,-7} winner {4,-4} rounds {5,2} units {6} v {7} material {8} v {9}",
            s.name, meA ? "A" : "B", them, extra, r, Rig.gc.days, me.units.Count, other.units.Count,
            Matchups.Material(me), Matchups.Material(other)));
        return r;
    }

    /// <summary>One game, narrated: the board after each player's turn and every decision's tiers.</summary>
    public static void Narrate(int index, Matchups.Kind them, bool meA, int extraFor, int rounds) {
        Spec s = Maps[index];
        Player a, b;
        Setup(s, meA ? Matchups.Kind.Full : them, meA ? them : Matchups.Kind.Full, extraFor, out a, out b);
        string side = Environment.GetEnvironmentVariable("SIDE") ?? "both";
        Console.WriteLine(s.name + ": Full is seat " + (meA ? "A (upper case)" : "B (lower case)") + " vs " + them);
        Console.Write(Draw(s.radius));
        GameController gc = Rig.gc;
        for (int turn = 0; turn < rounds * 2 && !gc.gameOver; turn++) {
            Player active = gc.activePlayer;
            bool log = side == "both" || side == active.personal_name;
            AITactical.DecisionLog = log ? (Action<string>)(m => Console.WriteLine("    " + m)) : null;
            AIPlayer full = active.gameObject.GetComponent<AIPlayer>();
            BaselineAIPlayer baseline = active.gameObject.GetComponent<BaselineAIPlayer>();
            Console.WriteLine("-- round " + gc.days + " seat " + active.personal_name + (full != null ? " (Full)" : " (" + them + ")"));
            if (full != null) {
                full.onDecision = (u, act, did) => {
                    if (!log || (act.kind == AIActionKind.Wait && !did)) return;
                    if (u.GetComponent<SupplyPoint>() != null || act.reason == null || !act.reason.Contains("tiers"))
                        Console.WriteLine("    " + u.owner.personal_name + " " + u.name + " " + act.kind + " (" + act.reason + ")");
                };
                full.PlayWholeTurn();
            } else if (baseline != null) baseline.PlayWholeTurn(); else gc.EndTurn();
            Console.Write(Draw(s.radius));
            string units = "";
            foreach (Player p in new[] { a, b }) foreach (Unit u in p.units)
                units += " " + p.personal_name + u.name.Substring(0, 2) + "(" + u.tile.x + "," + u.tile.y + ")s" + u.Strength;
            Console.WriteLine("   " + units);
        }
        AITactical.DecisionLog = null;
        Console.WriteLine("winner " + (gc.winner != null ? gc.winner.personal_name : "none"));
    }
}
