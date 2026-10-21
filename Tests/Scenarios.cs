using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using HexTactica.AI;
using HexTactica.Harness;
using HexTactica.Testing;

/// <summary>
/// Every test is a scenario file you can load in Unity (Tests/Scenarios/*.dat; see ScenarioFile).
///
///   bash Tests/run.sh Assets                      run them all: behaviour checks + matchups (≤ 50 games)
///   PROBE=export bash Tests/run.sh Assets          regenerate the files from the builders below
///   PROBE=game FILE=name [ROUNDS=r] [SIDE=AI|Red]  one scenario narrated: board each turn, every
///                                                  decision's reasoning
///   PROBE=chokes FILE=name                         where a grunt would cut enemy movement most
///
/// Behaviour scenarios are small set-ups with one check each. Matchups are the generated terrain
/// maps (random mountains/water/forest plus two cut paths between the HQs) with full armies,
/// against the original AI ported faithfully (Legacy) and the defend-only baseline (Greedy).
/// </summary>
public static class Scenarios {

    public static string Dir = "Scenarios";

    // ================================================================ builders

    struct MapSpec {
        public string name; public int radius, seed; public float mountain, water, forest;
        public MapSpec(string n, int r, int s, float m, float w, float f) { name = n; radius = r; seed = s; mountain = m; water = w; forest = f; }
    }

    static readonly MapSpec[] Maps = {
        new MapSpec("std-r5",   5, 11, 0.10f, 0.10f, 0.30f),
        new MapSpec("std-r6",   6, 23, 0.10f, 0.10f, 0.30f),
        new MapSpec("open-r5",  5, 37, 0.05f, 0.05f, 0.15f),
        new MapSpec("rough-r5", 5, 41, 0.15f, 0.15f, 0.30f),
        new MapSpec("rough-r6", 6, 53, 0.15f, 0.15f, 0.35f),
        new MapSpec("choke-r5", 5, 67, 0.20f, 0.20f, 0.20f),
    };

    /// <summary>
    /// Larger boards — radius 7, all of SampleScene — with doubled armies, so the sides start
    /// more than three turns apart and the strategic layer (objectives, supply) has room to matter.
    /// </summary>
    static readonly MapSpec[] LargeMaps = {
        new MapSpec("large-std-a",   7, 71, 0.10f, 0.10f, 0.30f),
        new MapSpec("large-std-b",   7, 83, 0.10f, 0.10f, 0.30f),
        new MapSpec("large-rough",   7, 97, 0.15f, 0.15f, 0.30f),
    };

    /// <summary>Radius 13 of SampleScene's 15: room for a march, a supply chain, and objectives.</summary>
    static readonly MapSpec[] CampaignMaps = {
        new MapSpec("campaign-std",   13, 101, 0.10f, 0.10f, 0.30f),
        new MapSpec("campaign-rough", 13, 113, 0.15f, 0.15f, 0.30f),
    };

    /// <summary>
    /// The gauntlet's random maps: fresh seeds never used for tuning, radius 7 (SampleScene), the
    /// bridge map's army on each side (HQ, three grunts, two tanks, two artillery, two supply units).
    /// Each map is played twice, the challenger on either side: ten games.
    /// </summary>
    static readonly MapSpec[] GauntletMaps = {
        new MapSpec("gauntlet-1", 7, 211, 0.10f, 0.10f, 0.30f),
        new MapSpec("gauntlet-2", 7, 223, 0.10f, 0.12f, 0.25f),
        new MapSpec("gauntlet-3", 7, 227, 0.15f, 0.15f, 0.30f),
        new MapSpec("gauntlet-4", 7, 229, 0.05f, 0.10f, 0.20f),
        new MapSpec("gauntlet-5", 7, 233, 0.12f, 0.08f, 0.35f),
    };

    static readonly string[] GauntletRoster = { "Grunt", "Grunt", "Tank", "Grunt", "Supplies", "Artillery", "Tank", "Artillery", "Supplies" };

    /// <summary>Write the gauntlet's random-map files (Tests/Scenarios/gauntlet-*.dat); the bridge file is kept.</summary>
    public static int ExportGauntlet() {
        int n = 0;
        foreach (MapSpec s in GauntletMaps) {
            foreach (bool aiLeft in new[] { true, false }) {
                Generate(s);
                Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
                HexTile left = Rig.At(-(s.radius - 1), 0), right = Rig.At(s.radius - 1, 0);
                foreach (Player p in new[] { ai, other }) {
                    HexTile hq = (p == ai) == aiLeft ? left : right;
                    List<HexTile> dep = Deployment(hq, DeploymentSize(s));
                    Put("TempHQ", p, hq.x, hq.y);
                    for (int i = 0; i < GauntletRoster.Length; i++) Put(GauntletRoster[i], p, dep[i].x, dep[i].y);
                }
                string name = s.name + (aiLeft ? "-challenger-left" : "-challenger-right");
                Dictionary<string, string> test = Test(name, "gauntlet", "Full", 30, "report",
                    "Random radius-7 map (seed " + s.seed + ", mountain " + s.mountain + ", water " + s.water + ", forest " + s.forest
                    + "), the bridge map's army each side, attrition rules. This AI on both sides: the challenger as Blue. The left side moves first.");
                test["rules"] = "attrition";
                ScenarioFile.Save(Path.Combine(Dir, name + ".dat"), test, ai, other, aiLeft ? ai : other);
                n++;
            }
        }
        return n;
    }

    /// <summary>
    /// Challenger (Blue, tweak <paramref name="challenger"/>) against the champion (Red, defaults
    /// plus <paramref name="champion"/>): the bridge map once, then the ten random-map games.
    /// </summary>
    public static void Gauntlet(Action<AIProfile> challenger, Action<AIProfile> champion, string cName, string chName) {
        Console.WriteLine("challenger = " + cName + "\nchampion   = " + chName);
        int w = 0, lo = 0, d = 0;
        string only = Environment.GetEnvironmentVariable("FILTER");
        foreach (string f in Files("gauntlet")) {
            string n = Path.GetFileNameWithoutExtension(f);
            ScenarioFile.Loaded l = ScenarioFile.Load(f, "Full", challenger, champion);
            GameController gc = Rig.gc;
            int rounds = l.meta.GetInt("rounds", 30);
            DateTime t0 = DateTime.Now;
            for (int t = 0; t < rounds * 2 && !gc.gameOver; t++) ScenarioFile.PlayTurn();
            string r = gc.winner == l.ai ? "challenger" : gc.winner == l.other ? "champion" : "draw";
            bool bridge = n == "gauntlet-bridge";
            if (!bridge) { if (gc.winner == l.ai) w++; else if (gc.winner == l.other) lo++; else d++; }
            Console.WriteLine(string.Format("  {0,-34} {1,-10} round {2,2}; units left: challenger {3,2} v champion {4,2}; material {5,6:0} v {6,6:0} ({7:0}s)",
                n, r, gc.days, Alive(l.ai), Alive(l.other), ScenarioFile.Material(l.ai), ScenarioFile.Material(l.other), (DateTime.Now - t0).TotalSeconds));
        }
        Console.WriteLine(string.Format("  random maps: challenger {0}W {1}L {2}D", w, lo, d));
    }

    static int DeploymentSize(MapSpec s) { return s.radius >= 13 ? 30 : s.radius >= 7 ? 18 : 9; }

    /// <summary>One map spec list as matchups: AI on either side, three line-ups.</summary>
    static int ExportMatches(MapSpec[] maps, string kind, int rounds, string[] roster, string[] extra, string what) {
        int n = 0;
        foreach (MapSpec s in maps) {
            foreach (bool aiLeft in new[] { true, false }) {
                foreach (string lineup in new[] { "legacy", "greedy", "legacy-outnumbered" }) {
                    Generate(s);
                    Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
                    HexTile left = Rig.At(-(s.radius - 1), 0), right = Rig.At(s.radius - 1, 0);
                    foreach (Player p in new[] { ai, other }) {
                        HexTile hq = (p == ai) == aiLeft ? left : right;
                        List<HexTile> dep = Deployment(hq, DeploymentSize(s));
                        Put("TempHQ", p, hq.x, hq.y);
                        for (int i = 0; i < roster.Length; i++) Put(roster[i], p, dep[i].x, dep[i].y);
                        if (lineup == "legacy-outnumbered" && p == other)
                            for (int i = 0; i < extra.Length; i++) Put(extra[i], p, dep[roster.Length + i].x, dep[roster.Length + i].y);
                    }
                    string name = kind + "-" + s.name.Substring(s.name.IndexOf('-') + 1) + "-" + lineup + (aiLeft ? "-ai-left" : "-ai-right");
                    ScenarioFile.Save(Path.Combine(Dir, name + ".dat"),
                        Test(name, kind, lineup.StartsWith("legacy") ? "Legacy" : "Greedy", rounds, "report",
                             "Radius-" + s.radius + " map " + s.name + " (mountain " + s.mountain + ", water " + s.water + ", forest " + s.forest
                             + "), " + what + (lineup == "legacy-outnumbered" ? "; opponent has " + extra.Length + " extra units" : "")
                             + ". The left side moves first."),
                        ai, other, aiLeft ? ai : other);
                    n++;
                }
            }
        }
        return n;
    }

    static TerrainData T(string n) { return ScenarioFile.Terrain(n); }

    static int rng;
    static int Next(int bound) {
        rng = (int)((rng * 1103515245L + 12345L) & 0x7FFFFFFF);
        return bound <= 0 ? 0 : (rng >> 8) % bound;
    }

    static void Blank(int radius) {
        ScenarioFile.Board = Math.Max(7, radius);
        Rig.NewGame(radius);
        foreach (HexTile t in Rig.hg.tiles) t.terrain = T("Grassland");
    }

    static void Generate(MapSpec s) {
        Blank(s.radius);
        rng = s.seed * 7919 + 17;
        foreach (HexTile t in Rig.hg.tiles) {
            float r = Next(10000) / 10000f;
            if (r < s.mountain) t.terrain = T("Mountains");
            else if (r < s.mountain + s.water) t.terrain = T("Water");
            else if (r < s.mountain + s.water + s.forest) t.terrain = T("Forest");
        }
        HexTile a = Rig.At(-(s.radius - 1), 0), b = Rig.At(s.radius - 1, 0);
        int k = Math.Max(1, s.radius / 2);
        Cut(a, Rig.At(0, -k), b);
        Cut(a, Rig.At(0, k), b);
        foreach (HexTile hq in new[] { a, b }) {
            hq.terrain = T("Grassland");
            foreach (HexTile n in Deployment(hq, DeploymentSize(s))) n.terrain = T("Grassland");
        }
    }

    static void Cut(HexTile from, HexTile via, HexTile to) {
        HexTile at = from;
        foreach (HexTile goal in new[] { via, to }) {
            int guard = 0;
            while (at != goal && guard++ < 200) {
                at.terrain = T("Grassland");
                int d = at.Distance(goal);
                List<HexTile> closer = new List<HexTile>();
                foreach (HexTile n in at.neighbors) if (n.Distance(goal) < d) closer.Add(n);
                closer.Sort(CombatMath.CompareTiles);
                at = closer[Next(closer.Count)];
            }
            at.terrain = T("Grassland");
        }
    }

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

    static Unit Put(string prefab, Player p, int x, int y) {
        Unit u = Rig.Spawn(PrefabLoader.Load(prefab), p, x, y);
        PrefabLoader.AttachHubIfAny(prefab, u);
        return u;
    }

    static Dictionary<string, string> Test(string name, string kind, string opponent, int rounds, string check, string question) {
        return new Dictionary<string, string> {
            { "name", name }, { "kind", kind }, { "opponent", opponent }, { "rounds", rounds.ToString() },
            { "check", check }, { "question", question }
        };
    }

    /// <summary>Regenerate every scenario file.</summary>
    public static void Export() {
        if (Directory.Exists(Dir)) foreach (string f in Directory.GetFiles(Dir, "*.dat")) if (!Path.GetFileName(f).StartsWith("gauntlet-bridge")) File.Delete(f);
        int n = 0;

        // ---- matchups: each generated map, AI on either side, three line-ups.
        foreach (MapSpec s in Maps) {
            foreach (bool aiLeft in new[] { true, false }) {
                foreach (string lineup in new[] { "legacy", "greedy", "legacy-outnumbered" }) {
                    Generate(s);
                    Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
                    HexTile left = Rig.At(-(s.radius - 1), 0), right = Rig.At(s.radius - 1, 0);
                    foreach (Player p in new[] { ai, other }) {
                        HexTile hq = (p == ai) == aiLeft ? left : right;
                        List<HexTile> dep = Deployment(hq, 9);
                        Put("TempHQ", p, hq.x, hq.y);
                        string[] roster = { "Grunt", "Grunt", "Tank", "Grunt", "Supplies", "Artillery" };
                        for (int i = 0; i < roster.Length; i++) Put(roster[i], p, dep[i].x, dep[i].y);
                        if (lineup == "legacy-outnumbered" && p == other) { Put("Tank", p, dep[6].x, dep[6].y); Put("Grunt", p, dep[7].x, dep[7].y); }
                    }
                    string name = "match-" + s.name + "-" + lineup + (aiLeft ? "-ai-left" : "-ai-right");
                    ScenarioFile.Save(Path.Combine(Dir, name + ".dat"),
                        Test(name, "match", lineup.StartsWith("legacy") ? "Legacy" : "Greedy", 30, "report",
                             "Generated map " + s.name + " (mountain " + s.mountain + ", water " + s.water + ", forest " + s.forest + ")"
                             + (lineup == "legacy-outnumbered" ? "; opponent has one extra tank and grunt" : "") + ". The left side moves first."),
                        ai, other, aiLeft ? ai : other);
                    n++;
                }
            }
        }

        // ---- large boards: doubled armies, three line-ups, AI on either side.
        n += ExportMatches(LargeMaps, "large", 40,
            new[] { "Grunt", "Grunt", "Tank", "Grunt", "Supplies", "Artillery", "Grunt", "Grunt", "Tank", "Grunt", "Supplies", "Artillery" },
            new[] { "Tank", "Grunt", "Tank", "Grunt" }, "twelve units a side");

        // ---- campaign boards: radius 13, armies that start more than three turns apart and
        // need a chain of supply units to stay supplied on the way.
        n += ExportGauntlet();

        n += ExportMatches(CampaignMaps, "campaign", 60,
            new[] { "Grunt", "Grunt", "Tank", "Grunt", "Supplies", "Artillery", "Grunt", "Grunt", "Tank", "Grunt", "Supplies",
                    "Artillery", "Grunt", "Tank", "Supplies", "Grunt" },
            new[] { "Tank", "Grunt", "Tank", "Grunt" }, "sixteen units a side, three supply units");

        // ---- behaviour scenarios: one question each.
        {   // The supply unit must not end its turn where the enemy can hit it.
            Blank(7);
            for (int y = -7; y <= 7; y++) { HexTile t = Rig.At(3, y); if (t != null && Math.Abs(y) > 1) t.terrain = T("Forest"); }
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("TempHQ", ai, -6, 0); Put("Supplies", ai, -4, 0);
            Put("Grunt", ai, 1, 0); Put("Grunt", ai, 1, 1); Put("Tank", ai, 0, 0); Put("Artillery", ai, -2, 0);
            Put("TempHQ", other, 6, 0); Put("Grunt", other, 4, -1); Put("Grunt", other, 4, 1); Put("Tank", other, 5, 0);
            ScenarioFile.Save(Path.Combine(Dir, "hub-stays-out-of-reach.dat"),
                Test("hub-stays-out-of-reach", "behaviour", "Legacy", 4, "hubSafe",
                     "Our army stands beyond HQ supply with the enemy close. The supply unit should bring supply forward without ever ending our turn where an enemy can attack it."),
                ai, other, ai);
            n++;
        }
        {   // A lone tank must not take a shot that leaves it where the enemy army kills it.
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("TempHQ", ai, -6, 0); Put("Tank", ai, -1, 0); Put("Grunt", ai, -5, 1);
            Put("TempHQ", other, 6, 0); Put("Grunt", other, 2, 0); Put("Tank", other, 4, 0); Put("Grunt", other, 4, -1); Put("Grunt", other, 4, 1);
            ScenarioFile.Save(Path.Combine(Dir, "no-suicide-attack.dat"),
                Test("no-suicide-attack", "behaviour", "Legacy", 1, "tankSurvivesReply",
                     "Our lone tank can hit an enemy grunt, but every tile it could hit from is within reach of the enemy tank and two grunts. It should not end its turn where the reply destroys it."),
                ai, other, ai);
            n++;
        }
        {   // Hub before rifle: killing their supply unit outweighs a grunt that can only scratch us.
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("TempHQ", ai, -6, 0); Put("Tank", ai, 0, 0);
            Put("TempHQ", other, 6, 0); Put("Supplies", other, 1, 0); Put("Grunt", other, 0, 1);
            for (int x = 2; x <= 4; x++) Put("Grunt", other, x, 0);
            ScenarioFile.Save(Path.Combine(Dir, "hit-the-supply-unit.dat"),
                Test("hit-the-supply-unit", "behaviour", "Legacy", 1, "enemyHubHit",
                     "Our tank is next to the enemy supply unit (25,000) that sustains four grunts, and next to a grunt that can only do 1 damage to it. It should hit the supply unit."),
                ai, other, ai);
            n++;
        }
        {   // Our HQ under direct threat: it must survive.
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("TempHQ", ai, -6, 0); Put("Grunt", ai, -5, 1); Put("Grunt", ai, -4, -1); Put("Tank", ai, -3, 1); Put("Artillery", ai, -6, 2);
            Put("TempHQ", other, 6, 0); Put("Tank", other, -1, 0); Put("Grunt", other, -2, 2); Put("Grunt", other, -1, -1);
            ScenarioFile.Save(Path.Combine(Dir, "defend-the-hq.dat"),
                Test("defend-the-hq", "behaviour", "Legacy", 4, "hqSurvives",
                     "An enemy tank and two grunts are two moves from our HQ. Our HQ must still stand after four rounds."),
                ai, other, ai);
            n++;
        }
        {   // A damaged grunt with a fresh one behind it should pull back to heal.
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("TempHQ", ai, -6, 0);
            Unit hurt = Put("Grunt", ai, -1, 0); hurt.Strength = 3;
            Put("Grunt", ai, -3, 0); Put("Grunt", ai, -3, 1);
            Put("TempHQ", other, 6, 0); Put("Grunt", other, 1, 0); Put("Grunt", other, 1, -1);
            ScenarioFile.Save(Path.Combine(Dir, "rotate-the-wounded.dat"),
                Test("rotate-the-wounded", "behaviour", "Legacy", 1, "woundedPullsBack",
                     "Our 3-strength grunt is in contact; two fresh grunts are close behind, inside supply. It should pull back rather than stay in reach."),
                ai, other, ai);
            n++;
        }
        {   // A single gap in a water line: hold it.
            Blank(7);
            for (int y = -7; y <= 7; y++) { HexTile t = Rig.At(0, y); if (t != null && y != 0) t.terrain = T("Water"); }
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("TempHQ", ai, -6, 0); Put("Grunt", ai, -3, 0); Put("Grunt", ai, -3, 1); Put("Tank", ai, -4, 0);
            Put("TempHQ", other, 6, 0); Put("Grunt", other, 6, -1); Put("Grunt", other, 5, 1); Put("Tank", other, 5, 0);
            ScenarioFile.Save(Path.Combine(Dir, "hold-the-gap.dat"),
                Test("hold-the-gap", "behaviour", "Legacy", 2, "gapHeld",
                     "A water line splits the map with a single land gap at (0,0). The enemy is outside striking range. One of our units should stand on the gap within two rounds."),
                ai, other, ai);
            n++;
        }
        {   // Finish the game: their HQ stands alone.
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("TempHQ", ai, -6, 0); Put("Grunt", ai, 0, 0); Put("Grunt", ai, 0, 1); Put("Tank", ai, -1, 0); Put("Artillery", ai, -2, 1);
            Put("TempHQ", other, 5, 0);
            ScenarioFile.Save(Path.Combine(Dir, "finish-the-game.dat"),
                Test("finish-the-game", "behaviour", "Legacy", 5, "aiWins",
                     "The enemy has only its HQ left, five hexes away. We should destroy it within five rounds."),
                ai, other, ai);
            n++;
        }
        Console.WriteLine("wrote " + n + " scenario files to " + Path.GetFullPath(Dir));
    }

    // ================================================================ running

    static AIWorld.Outcome Reply(Unit u, AIMap map) {
        return AIWorld.Evaluate(u, u.tile, new AttackAppraisal(), map, new AIWorld.Cache(), true);
    }

    static Unit First(Player p, string prefab) {
        foreach (Unit u in p.units) if (u != null && u.name.StartsWith(prefab)) return u;
        return null;
    }

    /// <summary>Play a behaviour scenario and judge its check. Returns pass, with detail.</summary>
    static bool Judge(ScenarioFile.Loaded l, out string detail) {
        string check = l.meta.Get("check", "report");
        int rounds = l.meta.GetInt("rounds", 30);
        GameController gc = Rig.gc;
        detail = "";
        switch (check) {
            case "hubSafe": {
                Unit hub = First(l.ai, "Supplies");
                for (int r = 0; r < rounds && !gc.gameOver; r++) {
                    ScenarioFile.PlayTurn();                      // ours
                    if (hub == null || hub.tile == null) { detail = "supply unit destroyed"; return false; }
                    AIWorld.Outcome w = Reply(hub, new AIMap(l.ai, AIProfile.Default));
                    float lost; w.lost.TryGetValue(hub, out lost);
                    if (lost > 0f) { detail = "round " + r + ": supply unit at " + D(hub.tile) + " can be hit for " + lost.ToString("0.#") + " " + w.Summary(); return false; }
                    if (!gc.gameOver) ScenarioFile.PlayTurn();    // theirs
                }
                detail = "never ended our turn in reach; at " + D(hub.tile) + " s" + hub.Strength;
                return true;
            }
            case "tankSurvivesReply": {
                Unit tank = First(l.ai, "Tank");
                string before = Pos(tank.tile);
                ScenarioFile.PlayTurn();
                if (tank.tile == null) { detail = "tank died in its own attack"; return false; }
                AIWorld.Outcome w = Reply(tank, new AIMap(l.ai, AIProfile.Default));
                float lost; w.lost.TryGetValue(tank, out lost);
                detail = "tank " + Fmt(before) + " -> " + D(tank.tile) + " s" + tank.Strength + ", reply would take " + lost.ToString("0.#") + " " + w.Summary();
                return lost < tank.Strength;
            }
            case "enemyHubHit": {
                Unit hub = First(l.other, "Supplies");
                ScenarioFile.PlayTurn();
                detail = "enemy supply unit " + (hub == null || hub.tile == null ? "destroyed" : "at s" + hub.Strength);
                return hub == null || hub.tile == null || hub.Strength < 6;
            }
            case "hqSurvives": {
                Unit hq = First(l.ai, "TempHQ");
                for (int r = 0; r < rounds * 2 && !gc.gameOver; r++) ScenarioFile.PlayTurn();
                bool alive = hq != null && hq.tile != null && hq.Strength > 0;
                detail = "our HQ " + (alive ? "s" + hq.Strength : "destroyed") + ", winner " + (gc.winner != null ? gc.winner.personal_name : "none");
                return alive;
            }
            case "woundedPullsBack": {
                Unit hurt = null;
                foreach (Unit u in l.ai.units) if (u.name.StartsWith("Grunt") && u.Strength < 6) hurt = u;
                int before = NearestEnemy(hurt, l.other);
                string from = Pos(hurt.tile);
                ScenarioFile.PlayTurn();
                if (hurt.tile == null) { detail = "the wounded grunt died"; return false; }
                int after = NearestEnemy(hurt, l.other);
                detail = "wounded grunt " + Fmt(from) + " -> " + D(hurt.tile) + ", nearest enemy " + before + " -> " + after + " hexes";
                return after > before;
            }
            case "gapHeld": {
                HexTile gap = Rig.At(0, 0);
                for (int r = 0; r < rounds && !gc.gameOver; r++) {
                    ScenarioFile.PlayTurn();
                    if (gap.occupier != null && gap.occupier.owner == l.ai) { detail = gap.occupier.name + " on the gap after our turn " + (r + 1); return true; }
                    if (!gc.gameOver) ScenarioFile.PlayTurn();
                }
                detail = "nobody of ours on (0,0); there: " + (gap.occupier != null ? gap.occupier.owner.personal_name + " " + gap.occupier.name : "empty");
                return false;
            }
            case "aiWins": {
                for (int r = 0; r < rounds * 2 && !gc.gameOver; r++) ScenarioFile.PlayTurn();
                detail = "winner " + (gc.winner != null ? gc.winner.personal_name : "none") + " in " + gc.days + " rounds";
                return gc.winner == l.ai;
            }
            default: {
                for (int r = 0; r < rounds * 2 && !gc.gameOver; r++) ScenarioFile.PlayTurn();
                string w = gc.winner == l.ai ? "win" : gc.winner == l.other ? "LOSS" : "draw";
                detail = w + " in " + gc.days + " rounds, material " + ScenarioFile.Material(l.ai) + " v " + ScenarioFile.Material(l.other);
                return gc.winner != l.other;
            }
        }
    }

    static int NearestEnemy(Unit u, Player enemy) {
        int best = 99;
        foreach (Unit e in enemy.units) if (e != null && e.tile != null && e.Firepower > 0) best = Math.Min(best, u.tile.Distance(e.tile));
        return best;
    }

    static string Pos(HexTile t) { return D(t); }
    static string Fmt(string v) { return v; }
    static string D(HexTile t) { return t == null ? "(dead)" : "(" + t.x + "," + t.y + ")"; }

    static IEnumerable<string> Files(string kind) {
        List<string> files = new List<string>(Directory.GetFiles(Dir, "*.dat"));
        files.Sort(StringComparer.Ordinal);
        string filter = Environment.GetEnvironmentVariable("FILTER");
        foreach (string f in files) {
            if (ScenarioFile.ReadMeta(f).Get("kind", "") != kind) continue;
            if (filter != null) {
                bool any = false;
                foreach (string part in filter.Split('|')) if (part.Length > 0 && Path.GetFileName(f).Contains(part)) any = true;
                if (!any) continue;
            }
            yield return f;
        }
    }

    /// <summary>Run every scenario. Returns the number of failed behaviour checks.</summary>
    public static int RunAll(Action<AIProfile> tweak) {
        int failed = 0, passed = 0;
        string only = Environment.GetEnvironmentVariable("ONLY");
        Console.WriteLine("== Behaviour scenarios ==");
        foreach (string f in Files(only == null || only == "behaviour" ? "behaviour" : "-")) {
            ScenarioFile.Loaded l = ScenarioFile.Load(f, null, tweak);
            string detail;
            bool ok = Judge(l, out detail);
            if (ok) passed++; else failed++;
            Console.WriteLine(string.Format("  {0}  {1,-24} {2}", ok ? "PASS" : "FAIL", l.meta.Name, detail));
        }
        Console.WriteLine();
        Console.WriteLine("== Matchups (generated terrain maps) ==");
        int w = 0, lo = 0, d = 0;
        foreach (string f in Files(only == null || only == "match" ? "match" : "-")) {
            ScenarioFile.Loaded l = ScenarioFile.Load(f, null, tweak);
            string detail;
            Judge(l, out detail);
            if (Rig.gc.winner == l.ai) w++; else if (Rig.gc.winner == l.other) lo++; else d++;
            Console.WriteLine(string.Format("  {0,-46} {1}", l.meta.Name, detail));
        }
        Console.WriteLine(string.Format("  matchups: {0}W {1}L {2}D", w, lo, d));
        foreach (string kind in new[] { "large", "campaign" }) {
            if (only != null && only != kind) continue;
            Console.WriteLine();
            Console.WriteLine(kind == "large" ? "== Large boards (radius 7, twelve a side) ==" : "== Campaign boards (radius 13, sixteen a side) ==");
            int lw = 0, ll = 0, ld = 0;
            foreach (string f in Files(kind)) {
                ScenarioFile.Loaded l = ScenarioFile.Load(f, null, tweak);
                string detail;
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                Judge(l, out detail);
                AIPlayer me = l.ai.gameObject.GetComponent<AIPlayer>();
                if (Rig.gc.winner == l.ai) lw++; else if (Rig.gc.winner == l.other) ll++; else ld++;
                Console.WriteLine(string.Format("  {0,-46} {1}; objective pulls {2} over {3} turns; {4:0.0}s", l.meta.Name, detail,
                    me.objectivePulls, me.turnsPlayed, sw.Elapsed.TotalSeconds));
            }
            Console.WriteLine(string.Format("  {0}: {1}W {2}L {3}D", kind, lw, ll, ld));
        }
        Console.WriteLine();
        Console.WriteLine(string.Format("behaviour checks: passed {0}, failed {1}", passed, failed));
        return failed;
    }

    // ================================================================ narration

    /// <summary>The board as text: . grass, f forest, ^ mountain, ~ water; Blue (AI) upper case, Red lower.</summary>
    public static string Draw() {
        StringBuilder sb = new StringBuilder();
        int r = ScenarioFile.Board;
        for (int y = -r; y <= r; y++) {
            char[] row = new string(' ', r * 4 + 3).ToCharArray();
            bool any = false;
            for (int x = -r; x <= r; x++) {
                HexTile t = Rig.At(x, y);
                if (t == null) continue;
                int col = 2 * x + y + r * 2;
                row[col] = Glyph(t);
                if (row[col] != '~' || t.occupier != null) any = true;
            }
            if (any) sb.Append(string.Format("  {0,3} ", y)).Append(new string(row).TrimEnd()).Append('\n');
        }
        return sb.ToString();
    }

    static char Glyph(HexTile t) {
        if (t.occupier != null) {
            Unit u = t.occupier;
            char c = 'U';
            SupplyPoint sp = u.GetComponent<SupplyPoint>();
            if (sp != null) c = sp.producer ? 'H' : 'S';
            else if (u.name.StartsWith("Grunt")) c = 'G';
            else if (u.name.StartsWith("Tank")) c = 'T';
            else if (u.name.StartsWith("Artillery")) c = 'A';
            return u.owner != null && u.owner.id == ScenarioFile.AI_ID ? c : char.ToLower(c);
        }
        if (t.terrain == null) return '?';
        switch (t.terrain.name) {
            case "Forest": return 'f';
            case "Mountains": return '^';
            case "Water": return '~';
            default: return '.';
        }
    }

    public static string Find(string name) {
        string path = Path.Combine(Dir, name.EndsWith(".dat") ? name : name + ".dat");
        if (!File.Exists(path)) throw new Exception("No scenario " + path);
        return path;
    }

    /// <summary>One scenario, narrated: the board after every turn and every decision's reasoning.</summary>
    public static void Narrate(string name, int rounds, Action<AIProfile> tweak) {
        ScenarioFile.Loaded l = ScenarioFile.Load(Find(name), null, tweak);
        Console.WriteLine(l.meta.Name + ": " + l.meta.Get("question", ""));
        Console.WriteLine("Blue (upper case) is this AI; Red (lower case) is " + l.meta.Get("opponent", "?") + ".");
        Console.Write(Draw());
        string side = Environment.GetEnvironmentVariable("SIDE") ?? "AI";
        GameController gc = Rig.gc;
        for (int turn = 0; turn < rounds * 2 && !gc.gameOver; turn++) {
            Player active = gc.activePlayer;
            bool log = side == "both" || (side == "AI") == (active == l.ai);
            AITactical.DecisionLog = log ? (Action<string>)(m => Console.WriteLine("    " + m)) : null;
            AISupplyPlanner.DebugLog = AITactical.DecisionLog;
            AIPlayer full = active.gameObject.GetComponent<AIPlayer>();
            Console.WriteLine("-- round " + gc.days + ": " + active.personal_name);
            if (full != null) {
                bool first = true;
                full.onGhost = (u, at) => { if (log) Console.WriteLine("    ghost: " + u.name + (at == null ? " stays" : " will move to " + D(at)) + "; the line plans with that supply"); };
                full.onDecision = (u, act, did) => {
                    if (log && first && full.CurrentMap != null) {
                        first = false;
                        AIMap m = full.CurrentMap;
                        Console.WriteLine("    read: " + m.SupplyReport());
                        string contact = "";
                        foreach (Unit x in m.ownUnits) contact += " " + x.name.Substring(0, 2) + D(x.tile) + "=" + m.TurnsToContact(x);
                        Console.WriteLine("    turns to contact:" + contact);
                        foreach (Objective o in full.CurrentObjectives) Console.WriteLine("    objective " + o);
                        Console.WriteLine("    objective pulls this turn: " + full.CurrentAssignments.Count);
                    }
                    if (!log || (act.kind == AIActionKind.Wait && !did)) return;
                    if (u.GetComponent<SupplyPoint>() != null || act.reason == null || act.reason.IndexOf("world") < 0 && act.reason.IndexOf("tiers") < 0)
                        Console.WriteLine("    " + u.owner.personal_name + " " + u.name + " " + act.kind + " (" + act.reason + ")");
                };
            }
            ScenarioFile.PlayTurn();
            Console.Write(Draw());
            string units = "";
            foreach (Player p in new[] { l.ai, l.other }) foreach (Unit u in p.units)
                units += " " + p.personal_name[0] + u.name.Substring(0, 2) + D(u.tile) + "s" + u.Strength;
            Console.WriteLine("   " + units);
        }
        AITactical.DecisionLog = null;
        AISupplyPlanner.DebugLog = null;
        Console.WriteLine("winner " + (gc.winner != null ? gc.winner.personal_name : "none"));
    }

    /// <summary>
    /// This AI against itself with different profiles: Blue with <paramref name="blue"/>, Red with
    /// <paramref name="red"/>. One game, recorded like a game in Unity (GameLog) when OUT is set,
    /// with a line per round of material and losses.
    /// </summary>
    public static void Duel(string name, int rounds, Action<AIProfile> blue, Action<AIProfile> red, string root) {
        ScenarioFile.Loaded l = ScenarioFile.Load(Find(name), "Full", blue, red);
        if (root != null) HexTactica.Harness.GameLog.Start(l.path, "Full", root);
        GameController gc = Rig.gc;
        Console.WriteLine("duel on " + name + ": Blue " + (Environment.GetEnvironmentVariable("SET_BLUE") ?? "defaults")
                          + " | Red " + (Environment.GetEnvironmentVariable("SET_RED") ?? "defaults"));
        int startBlue = l.ai.units.Count, startRed = l.other.units.Count;
        for (int turn = 0; turn < rounds * 2 && !gc.gameOver; turn++) {
            ScenarioFile.PlayTurn();
            if (gc.activePlayer == l.ai || gc.gameOver)
                Console.WriteLine(string.Format("  round {0,2}: Blue {1,2} units, material {2,6} | Red {3,2} units, material {4,6}",
                    gc.days, Alive(l.ai), ScenarioFile.Material(l.ai), Alive(l.other), ScenarioFile.Material(l.other)));
        }
        string folder = HexTactica.Harness.GameLog.Folder;
        if (root != null) HexTactica.Harness.GameLog.Stop();
        Console.WriteLine("winner: " + (gc.winner != null ? gc.winner.personal_name : "none (round limit)")
                          + string.Format("; units lost: Blue {0} of {1}, Red {2} of {3}", startBlue - Alive(l.ai), startBlue, startRed - Alive(l.other), startRed));
        if (root != null) Console.WriteLine("log: " + folder);
    }

    /// <summary>
    /// How well the forecast predicts the opponent's turn: for each of Blue's turns, the loss the
    /// last decision's forecast expected Blue to take in Red's reply, against what Red actually did
    /// (strength lost to Red's attacks, in gold). Over every match and large file.
    /// </summary>
    public static void Accuracy(Action<AIProfile> tweak) {
        double absErr = 0, bias = 0, predSum = 0, actSum = 0; int n = 0;
        foreach (string kind in new[] { "match", "large" }) {
            foreach (string f in Files(kind)) {
                ScenarioFile.Loaded l = ScenarioFile.Load(f, string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPP")) ? null : Environment.GetEnvironmentVariable("OPP"), tweak);
                GameController gc = Rig.gc;
                float actual = 0f;
                Action<Unit, Unit, HexTile, HexTile, CombatOutcome> hook = (a, d, fr, at, o) => {
                    if (d.owner == l.ai) actual += o.damageToDefender * AIScore.Cost(d);
                    if (a.owner == l.ai) actual += o.damageToAttacker * AIScore.Cost(a);
                };
                int rounds = l.meta.GetInt("rounds", 30);
                for (int t = 0; t < rounds * 2 && !gc.gameOver; t++) {
                    bool blue = gc.activePlayer == l.ai;
                    AITactical.LastChosen = null;
                    ScenarioFile.PlayTurn();
                    if (!blue || gc.gameOver) continue;
                    AIWorld.Outcome w = AITactical.LastChosen;
                    if (w == null) continue;
                    float predicted = 0f;
                    foreach (KeyValuePair<Unit, float> kv in w.lost) predicted += kv.Value * AIScore.Cost(kv.Key);
                    // Red's turn, with its attacks counted.
                    actual = 0f;
                    Unit.Attacked += hook;
                    try { ScenarioFile.PlayTurn(); t++; } finally { Unit.Attacked -= hook; }
                    absErr += Math.Abs(predicted - actual); bias += predicted - actual; predSum += predicted; actSum += actual; n++;
                }
            }
        }
        Console.WriteLine(string.Format("forecast accuracy over {0} turns: mean predicted loss {1:0}, mean actual {2:0}, mean |error| {3:0}, bias {4:+0;-0}",
            n, predSum / n, actSum / n, absErr / n, bias / n));
    }

    /// <summary>
    /// The group-plan forecast (AIPlan.Evaluate) with one move and retreats off must score what the
    /// single-move forecast (AIWorld.Evaluate) does with its extras off. Checks every option of every
    /// unit of Blue's on the boards of each match file (or FILE), and prints the mismatches.
    /// </summary>
    public static void Equivalence(string name) {
        List<string> files = name != null ? new List<string> { Find(name) } : new List<string>(Files("match"));
        int checks = 0, bad = 0;
        foreach (string f in files) {
            ScenarioFile.Loaded l = ScenarioFile.Load(f, "Human", p => {
                p.assistWeight = 0f; p.selfPreservationWeight = 0f; p.ownStrikeWeight = 0f; p.enemyStrikeWeight = 0f;
            });
            // Play a few rounds so units are in contact, then check Blue's options.
            GameController gc = Rig.gc;
            Rig.AttachBaseline(l.other, AIKind.Legacy);
            for (int t = 0; t < 6 && !gc.gameOver; t++) ScenarioFile.PlayTurn();
            if (gc.gameOver || gc.activePlayer != l.ai) continue;
            AIMap map = new AIMap(l.ai, l.ai.GetComponent<AIPlayer>().Profile);
            foreach (Unit u in new List<Unit>(map.ownUnits)) {
                if (u == null || u.tile == null || u.Firepower <= 0) continue;
                MoveField fld = AIPath.ThisTurn(u);
                List<HexTile> stops = new List<HexTile> { u.tile };
                foreach (HexTile t in fld.Tiles) if (t != u.tile && fld.ReachableThisTurn(t) && AIPath.CanFinishTurnOn(u, t)) stops.Add(t);
                foreach (HexTile s in stops) {
                    List<AttackAppraisal> shots = new List<AttackAppraisal> { new AttackAppraisal() };
                    if (u.moveAndAttack || s == u.tile) shots.AddRange(AITargeting.AllFrom(u, s, map, map.profile, Posture.Defensive, true));
                    foreach (AttackAppraisal a in shots) {
                        AIWorld.Outcome one = AIWorld.Evaluate(u, s, a, map, new AIWorld.Cache(), false, false);
                        PlanStep step = new PlanStep { unit = u, stop = s, target = a.valid ? a.target : null };
                        if (a.valid) step.known = a.outcome;
                        AIWorld.Outcome many = AIPlan.Evaluate(new List<PlanStep> { step }, map, false, false);
                        checks++;
                        if (Mathf.Abs(one.score - many.score) > 0.5f) {
                            bad++;
                            if (bad <= 10) Console.WriteLine(string.Format("  {0}: {1} at {2} {3}: single {4:0} vs plan {5:0}", Path.GetFileNameWithoutExtension(f), u.name, D(s),
                                a.valid ? "hit " + a.target.name : "no shot", one.score, many.score));
                        }
                    }
                }
            }
        }
        Console.WriteLine(string.Format("equivalence: {0} options checked, {1} mismatches", checks, bad));
    }

    /// <summary>
    /// AI A (Blue) against AI B (Red) on every generated map with even armies, A on each side in
    /// turn (the "-legacy-ai-left/right" files: equal line-ups; the left side moves first).
    /// 22 games. Prints each result and A's record.
    /// </summary>
    public static void Versus(Action<AIProfile> a, Action<AIProfile> b, string aName, string bName) {
        int w = 0, lo = 0, d = 0;
        Console.WriteLine("A = " + aName + "\nB = " + bName + "\nrules: " + (Environment.GetEnvironmentVariable("RULES") ?? "as files say"));
        foreach (string kind in new[] { "match", "large", "campaign" }) {
            foreach (string f in Files(kind)) {
                string n = Path.GetFileNameWithoutExtension(f);
                if (n.IndexOf("-legacy-ai-") < 0) continue;
                ScenarioFile.Loaded l = ScenarioFile.Load(f, "Full", a, b);
                GameController gc = Rig.gc;
                int rounds = l.meta.GetInt("rounds", 30);
                for (int t = 0; t < rounds * 2 && !gc.gameOver; t++) ScenarioFile.PlayTurn();
                string r = gc.winner == l.ai ? "A wins" : gc.winner == l.other ? "B wins" : "draw";
                if (gc.winner == l.ai) w++; else if (gc.winner == l.other) lo++; else d++;
                Console.WriteLine(string.Format("  {0,-36} {1,-6} round {2,2}; left: A {3,6:0} v B {4,6:0}", n.Replace("-legacy", ""), r, gc.days,
                    GameController.Remaining(l.ai), GameController.Remaining(l.other)));
            }
        }
        Console.WriteLine(string.Format("  A: {0}W {1}L {2}D", w, lo, d));
    }

    static int Alive(Player p) { int n = 0; foreach (Unit u in p.units) if (u != null && u.Strength > 0 && u.tile != null) n++; return n; }

    /// <summary>
    /// Record a scenario the way the game does (GameLog), then load the snapshot of the last
    /// turn recorded and check it reproduces the board — the round trip a log sent from Unity
    /// relies on.
    /// </summary>
    public static void Record(string name, int rounds, string root, Action<AIProfile> tweak) {
        ScenarioFile.Loaded l = ScenarioFile.Load(Find(name), null, tweak);
        HexTactica.Harness.GameLog.Start(l.path, l.meta.Get("opponent", "?"), root);
        GameController gc = Rig.gc;
        for (int turn = 0; turn < rounds * 2 && !gc.gameOver; turn++) ScenarioFile.PlayTurn();
        string folder = HexTactica.Harness.GameLog.Folder;
        string board = HexTactica.Harness.GameLog.Draw();
        HexTactica.Harness.GameLog.Stop();
        List<string> snaps = new List<string>(Directory.GetFiles(folder, "turn-*.dat"));
        snaps.Sort(StringComparer.Ordinal);
        Console.WriteLine("recorded to " + folder + ": game.log + " + snaps.Count + " snapshots");
        if (snaps.Count == 0) return;
        string last = snaps[snaps.Count - 1];
        ScenarioFile.Load(last, null, tweak);
        string again = HexTactica.Harness.GameLog.Draw();
        Console.WriteLine("reloaded " + Path.GetFileName(last) + ": board " + (again == board ? "matches" : "DIFFERS") + " the recorded game's");
        if (again != board) { Console.WriteLine(board); Console.WriteLine(again); }
    }

    /// <summary>For every open tile, how much enemy movement a grunt standing there would remove.</summary>
    public static void Chokes(string name) {
        ScenarioFile.Loaded l = ScenarioFile.Load(Find(name), null, null);
        Unit probe = First(l.ai, "Grunt");
        AIMap map = new AIMap(l.ai, AIProfile.Default);
        Dictionary<HexTile, int> denial = new Dictionary<HexTile, int>();
        foreach (HexTile t in Rig.hg.tiles) if (t.occupier == null && probe.CanEnter(t)) denial[t] = map.Denial(probe, t).tiles;
        Console.WriteLine(l.meta.Name + ": enemy reach (two turns, all units) a grunt would remove on each tile, in tens");
        int r = ScenarioFile.Board;
        for (int y = -r; y <= r; y++) {
            char[] row = new string(' ', r * 4 + 3).ToCharArray();
            for (int x = -r; x <= r; x++) {
                HexTile t = Rig.At(x, y);
                if (t == null) continue;
                int d;
                char c = Glyph(t);
                if (t.occupier == null && denial.TryGetValue(t, out d) && d > 0) c = (char)('0' + Math.Min(9, (d + 5) / 10));
                row[2 * x + y + r * 2] = c;
            }
            Console.WriteLine(string.Format("  {0,3} ", y) + new string(row).TrimEnd());
        }
    }
}
