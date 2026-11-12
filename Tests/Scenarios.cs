using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
public static partial class Scenarios {

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

    static readonly string[] GauntletRoster = { "Grunt", "Grunt", "Tank", "Grunt", "Supplies", "Artillery", "Tank", "Artillery", "Supplies", "APC" };

    /// <summary>What may be bought on a map with purchases on: every kind of unit on it.</summary>
    static string AllowedOf(string[] roster) {
        List<string> kinds = new List<string>();
        foreach (string r in roster) if (!kinds.Contains(r)) kinds.Add(r);
        return string.Join(",", kinds.ToArray());
    }

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
                    Put("HQ", p, hq.x, hq.y);
                    for (int i = 0; i < GauntletRoster.Length; i++) Put(GauntletRoster[i], p, dep[i].x, dep[i].y);
                }
                string name = s.name + (aiLeft ? "-challenger-left" : "-challenger-right");
                Dictionary<string, string> test = Test(name, "gauntlet", "Full", 30, "report",
                    "Random radius-7 map (seed " + s.seed + ", mountain " + s.mountain + ", water " + s.water + ", forest " + s.forest
                    + "), the bridge map's army plus an APC each side, attrition rules. This AI on both sides: the challenger as Blue. The left side moves first.");
                test["rules"] = "attrition";
                test["allowedUnits"] = AllowedOf(GauntletRoster);   // with purchases on: any kind on the map
                ScenarioFile.Save(Path.Combine(Dir, name + ".dat"), test, ai, other, aiLeft ? ai : other);
                n++;
            }
        }
        return n;
    }

    /// <summary>
    /// The gauntlet's random maps with an air wing each side: an Airport as near its HQ as it fits
    /// on land (two footprint tiles, three runway tiles), two Fighters and an Air Tanker on it, then
    /// the usual army. Kind "airgauntlet", attrition rules.
    /// </summary>
    public static int ExportAirGauntlet() {
        int n = 0;
        foreach (MapSpec s in GauntletMaps) {
            foreach (bool aiLeft in new[] { true, false }) {
                Generate(s);
                Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
                HexTile left = Rig.At(-(s.radius - 1), 0), right = Rig.At(s.radius - 1, 0);
                bool ok = true;
                foreach (Player p in new[] { ai, other }) {
                    HexTile hq = (p == ai) == aiLeft ? left : right;
                    Put("HQ", p, hq.x, hq.y);
                    Building ap = null;
                    foreach (HexTile c in Deployment(hq, Rig.hg.tiles.Count)) {
                        if (c.Distance(hq) < 2 || c.building != null || c.occupier != null) continue;
                        // An airfield: the airport and its three runways, all on dry, free ground.
                        bool fits = c.terrain != T("Water");
                        foreach (int[] o in new[] { new[] { 0, 1 }, new[] { 1, 1 }, new[] { -1, 1 } }) {
                            HexTile t = Rig.At(c.x + o[0], c.y + o[1]);
                            if (t == null || t.occupier != null || t == hq || t.terrain == T("Water") || t.building != null) fits = false;
                        }
                        if (!fits) continue;
                        ap = Rig.PlaceAirfield(p, c.x, c.y);
                        break;
                    }
                    if (ap == null) { ok = false; break; }
                    List<HexTile> pads = new List<HexTile> { ap.tile };
                    foreach (Building rw in Runways(ap)) pads.Add(rw.tile);
                    Put("Fighter", p, pads[0].x, pads[0].y);
                    Put("Fighter", p, pads[1].x, pads[1].y);
                    Put("AirTanker", p, pads[2].x, pads[2].y);
                    int placed = 0;
                    foreach (HexTile t in Deployment(hq, Rig.hg.tiles.Count)) {
                        if (placed >= GauntletRoster.Length) break;
                        if (t.occupier != null || t.building != null) continue;
                        Put(GauntletRoster[placed], p, t.x, t.y);
                        placed++;
                    }
                }
                if (!ok) { Console.WriteLine("  " + s.name + ": no room for an airport, skipped"); continue; }
                string name = "air-" + s.name + (aiLeft ? "-challenger-left" : "-challenger-right");
                Dictionary<string, string> test = Test(name, "airgauntlet", "Full", 30, "report",
                    "Random radius-7 map (seed " + s.seed + "), the gauntlet army plus an Airport with two Fighters and an Air Tanker each side, attrition rules. The left side moves first.");
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

    /// <summary>
    /// Logistics test: an ocean with one zigzag land corridor (three tiles wide, some forest)
    /// between the HQs. The HQs are 25 tiles apart in a straight line but about 52 along the road,
    /// so the armies meet far beyond the HQ's supply (8) and each side has to chain its five supply
    /// units (4 each) up the road to fight supplied. Attrition rules, 60 rounds. The arms of the
    /// zigzag are close enough for artillery to fire across the water.
    /// </summary>
    public static int ExportZigzag() {
        int r = 14;
        Blank(r);
        foreach (HexTile t in Rig.hg.tiles) t.terrain = T("Water");
        // A serpentine road two tiles wide: three long rows (y = -6, 0, 6) joined at alternate ends.
        // Rows are six apart, so the water between them (five tiles) is wider than artillery range.
        int[,] way = { { -7, -6 }, { 13, -6 }, { 13, 0 }, { -13, 0 }, { -13, 6 }, { 7, 6 } };
        List<HexTile> road = new List<HexTile>();
        HexTile at = Rig.At(way[0, 0], way[0, 1]);
        road.Add(at);
        for (int i = 1; i < way.GetLength(0); i++) {
            HexTile goal = Rig.At(way[i, 0], way[i, 1]);
            int guard = 0;
            while (at != goal && guard++ < 100) {
                HexTile next = null;
                foreach (HexTile n in at.neighbors) if (n != null && (next == null || n.Distance(goal) < next.Distance(goal) || (n.Distance(goal) == next.Distance(goal) && CombatMath.Precedes(n, next)))) next = n;
                at = next;
                road.Add(at);
            }
        }
        rng = 4241;
        foreach (HexTile t in road) {
            t.terrain = T("Grassland");
            // The second lane: the tile below on the rows, the tile beside on the joins.
            HexTile lane = Rig.At(t.x, t.y + 1);
            if (lane != null && Math.Abs(t.y) % 6 != 0) lane = Rig.At(t.x - 1, t.y);
            if (lane != null && lane.terrain.name == "Water") lane.terrain = Next(100) < 20 ? T("Forest") : T("Grassland");
        }
        Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
        string[] roster = { "Grunt", "Grunt", "Tank", "Grunt", "Supplies", "Artillery", "Tank", "Artillery", "Supplies", "Supplies", "Supplies", "Supplies", "APC" };
        foreach (Player p in new[] { ai, other }) {
            HexTile hq = p == ai ? road[1] : road[road.Count - 2];
            List<HexTile> land = new List<HexTile>();
            foreach (HexTile t in Rig.hg.tiles) if (t != hq && t.terrain.name != "Water") land.Add(t);
            land.Sort((x, y) => { int c = RoadSteps(hq, x).CompareTo(RoadSteps(hq, y)); return c != 0 ? c : CombatMath.CompareTiles(x, y); });
            Put("HQ", p, hq.x, hq.y);
            for (int i = 0; i < roster.Length; i++) Put(roster[i], p, land[i].x, land[i].y);
        }
        Dictionary<string, string> test = Test("logistics-zigzag", "logistics", "Full", 60, "report",
            "Ocean with one serpentine road (" + road.Count + " tiles, two wide) between the HQs; the water between its rows is wider than artillery range. Supply must be chained forward: HQ range 8, five supply units of range 4 (moving 2 a turn) and an APC each side, and the armies meet about " + (road.Count / 2) + " tiles from home. Attrition rules, 60 rounds. This AI on both sides; Blue moves first.");
        test["rules"] = "attrition";
        test["purchases"] = "on";
        test["allowedUnits"] = AllowedOf(roster);   // any kind of unit on the map, the APC included
        ScenarioFile.Save(Path.Combine(Dir, "logistics-zigzag.dat"), test, ai, other, ai);
        return 1;
    }

    /// <summary>
    /// Every choice one Blue unit has on a scenario's board (UNIT=x,y), as the AI forecasts it: each
    /// tile it can end on this turn, standing or with each shot it could take from there, with the
    /// world score and the enemy reply. AT=x,y shows only that tile. For finding why it chose what it did.
    /// </summary>
    public static void Options(string name, string unitAt, string onlyAt, Action<AIProfile> tweak) {
        ScenarioFile.Loaded l = ScenarioFile.Load(Find(name), "Human", tweak);
        string[] xy = unitAt.Split(',');
        Unit u = Rig.At(int.Parse(xy[0]), int.Parse(xy[1])).occupier;
        AIMap map = new AIMap(l.ai, l.ai.GetComponent<AIPlayer>().Profile);
        HexTile only = null;
        if (!string.IsNullOrEmpty(onlyAt)) { string[] a = onlyAt.Split(','); only = Rig.At(int.Parse(a[0]), int.Parse(a[1])); }
        MoveField f = map.ReachThisTurn(u);
        List<KeyValuePair<float, string>> rows = new List<KeyValuePair<float, string>>();
        foreach (HexTile t in f.Tiles) {
            if (t != u.tile && (!f.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(u, t))) continue;
            if (only != null && t != only) continue;
            AIWorld.Outcome w = AIWorld.Evaluate(u, t, new AttackAppraisal(), map, new AIWorld.Cache(), true);
            rows.Add(new KeyValuePair<float, string>(w.score, Pos(t) + " no shot: " + w.Summary()));
            if (u.moveAndAttack || t == u.tile)
                foreach (AttackAppraisal s in AITargeting.AllFrom(u, t, map, map.profile, Posture.Aggressive, true)) {
                    AIWorld.Outcome ws = AIWorld.Evaluate(u, t, s, map, new AIWorld.Cache(), true);
                    rows.Add(new KeyValuePair<float, string>(ws.score, Pos(t) + " hit " + s.target.name.Replace("(Clone)", "") + Pos(s.target.tile) + " " + s.outcome.damageToDefender + "/" + s.outcome.damageToAttacker + ": " + ws.Summary()));
                }
        }
        rows.Sort((a, b) => b.Key.CompareTo(a.Key));
        Console.WriteLine(u.name + " at " + Pos(u.tile) + " s" + u.Strength + ": " + rows.Count + " options, best first");
        foreach (KeyValuePair<float, string> r in rows) Console.WriteLine(string.Format("  {0,8:0}  {1}", r.Key, r.Value));
    }

    /// <summary>
    /// A logistics game, narrated by supply: each round, per side, combat units in and out of
    /// supply, supply units connected to the HQ, how far up the road the front unit is (steps from
    /// its own HQ), unit-turns spent out of supply and units lost to attrition so far.
    /// </summary>
    public static void Logistics(string name, Action<AIProfile> blue, Action<AIProfile> red, string root) {
        ScenarioFile.Loaded l = ScenarioFile.Load(Find(name), "Full", blue, red);
        UseOldAI(l);
        if (root != null) HexTactica.Harness.GameLog.Start(l.path, "Full", root);
        GameController gc = Rig.gc;
        int rounds = l.meta.GetInt("rounds", 60);
        Dictionary<Player, HexTile> hq = new Dictionary<Player, HexTile>();
        foreach (Player p in new[] { l.ai, l.other })
            if (GameController.HQOf(p) != null) hq[p] = GameController.HQOf(p).tile;
        Console.WriteLine(l.meta.Name + ": " + l.meta.Get("question", ""));
        for (int turn = 0; turn < rounds * 2 && !gc.gameOver; turn++) {
            ScenarioFile.PlayTurn();
            if (gc.activePlayer != l.ai && !gc.gameOver) continue;
            StringBuilder sb = new StringBuilder();
            sb.Append(string.Format("  round {0,2}:", gc.days));
            foreach (Player p in new[] { l.ai, l.other }) {
                SupplyNetwork.Result sup = SupplyNetwork.Solve(p, false);
                int inS = 0, outS = 0, hubs = 0, linked = 0, front = 0;
                foreach (Unit u in p.units) {
                    if (u == null || u.tile == null || u.Strength <= 0 || u.trasnporter != null) continue;
                    SupplyPoint sp = u.GetComponent<SupplyPoint>();
                    if (sp != null) { if (!sp.producer) { hubs++; if (sup.connectedHubs.Contains(sp)) linked++; } continue; }
                    if (sup.DepthAt(u.tile) > 0) inS++; else outS++;
                    front = Math.Max(front, RoadSteps(hq[p], u.tile));
                }
                sb.Append(string.Format(" | {0}: {1} in / {2} out of supply, hubs linked {3}/{4}, front {5,2} steps, out-of-supply unit-turns {6,3}, lost to attrition {7}",
                    p == l.ai ? "Blue" : "Red", inS, outS, linked, hubs, front, p.unitTurnsOutOfSupply, p.unitsLostToAttrition));
            }
            Console.WriteLine(sb.ToString());
        }
        string folder = HexTactica.Harness.GameLog.Folder;
        if (root != null) HexTactica.Harness.GameLog.Stop();
        Console.WriteLine("winner: " + (gc.winner != null ? gc.winner.personal_name : "none") + string.Format("; material left Blue {0}, Red {1}", ScenarioFile.Material(l.ai), ScenarioFile.Material(l.other)));
        if (root != null) Console.WriteLine("log: " + folder);
    }

    /// <summary>Land steps from <paramref name="a"/> to <paramref name="b"/> (breadth first over non-water tiles).</summary>
    static int RoadSteps(HexTile a, HexTile b) {
        if (a == null || b == null) return 0;
        Dictionary<HexTile, int> d = new Dictionary<HexTile, int> { { a, 0 } };
        Queue<HexTile> q = new Queue<HexTile>();
        q.Enqueue(a);
        while (q.Count > 0) {
            HexTile t = q.Dequeue();
            if (t == b) return d[t];
            foreach (HexTile n in t.neighbors) {
                if (n == null || d.ContainsKey(n) || n.terrain == null || n.terrain.name == "Water") continue;
                d[n] = d[t] + 1;
                q.Enqueue(n);
            }
        }
        return 0;
    }

    // ------------------------------------------------------------------ building rules

    /// <summary>
    /// Building rule scenarios (kind "rules"): small boards set up for one rule each; the check
    /// plays the turns by hand (moves, an attack, end of turn) and reads the building afterwards.
    /// </summary>
    public static int ExportBuildingRules() {
        int n = 0;
        Action<string, string, Action<Player, Player>, string> make = (name, check, setup, question) => {
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("HQ", other, 6, 0);
            try { setup(ai, other); } catch (FileNotFoundException e) { Console.WriteLine("  (not made: " + name + " needs " + Path.GetFileName(e.FileName) + ", no longer in the game)"); return; }
            Dictionary<string, string> test = Test(name, "rules", "Human", 4, check, question);
            test["rules"] = check.StartsWith("attrition") ? "attrition" : "standard";
            ScenarioFile.Save(Path.Combine(Dir, name + ".dat"), test, ai, other, ai);
            n++;
        };
        make("ai-campaign-objectives", "campaignObjectives", (a, o) => {
                 for (int y = -7; y <= 7; y++) { HexTile w = Rig.At(0, y); if (w != null && Math.Abs(y) != 4 && y != 0) w.terrain = T("Water"); }
                 Rig.PlaceBuilding("City", null, -1, -5); Rig.PlaceBuilding("City", null, 2, 3);
                 foreach (int[] u in new[] { new[] { -5, -1 }, new[] { -5, 1 }, new[] { -4, -1 }, new[] { -4, 0 }, new[] { -4, 1 }, new[] { -5, 2 }, new[] { -6, 2 }, new[] { -5, -2 }, new[] { -4, 2 }, new[] { -4, -2 } }) Put("Grunt", a, u[0], u[1]);
                 Put("Tank", a, -3, 0); Put("Tank", a, -3, -1); Put("Tank", a, -3, 1); Put("Tank", a, -3, -2); Put("Tank", a, -2, 0); Put("Tank", a, -4, -3); Put("Grunt", a, -3, -4); Put("Grunt", a, -2, -5); Put("Supplies", a, -5, 0); Put("Supplies", a, -6, 1); Put("Supplies", a, -6, -1);
                 Put("Tank", o, 2, 0); Put("Tank", o, 3, 0); Put("Grunt", o, 2, 1); Put("Grunt", o, 5, 0); },
             "The campaign (AIProfile.campaign) on a board cut by a river with three crossings, the middle one held by Red tanks: objectives are read from the map — each city, Red's HQ, the Red group by the middle crossing (it can reach our HQ next turn, so dealing with it is defending the HQ); each with a force need (what can fight there, times objectiveForceRatio; a building needs someone who can take it, and gets one) and, beyond our supply, supply units to carry supply there; its staging tile is out of the enemy's reach next turn and short of the objective, and a task force gathers there until campaignGatherShare of it (or what its objective needs) has arrived, then goes in together. Nothing is held back at home while the enemy cannot reach our HQ next turn. An objective is taken on only if what is left can give it all it needs: none is left short. All in (objectiveSpread 0) pursues one objective besides our HQ; spread (1) pursues several. Planned again with the same board, the same units stay on the same objectives; with the Red group gone, it is planned afresh. Fronts come one per task force, with the rest at home.");
        make("ai-campaign-logistics", "campaignLogistics", (a, o) => {
                 for (int y = -7; y <= 7; y++) { HexTile w = Rig.At(0, y); if (w != null && Math.Abs(y) != 4 && y != 0) w.terrain = T("Water"); }
                 Rig.PlaceBuilding("City", null, -1, -5); Rig.PlaceBuilding("City", null, 2, 3);
                 foreach (int[] u in new[] { new[] { -5, -1 }, new[] { -5, 1 }, new[] { -4, -1 }, new[] { -4, 0 }, new[] { -4, 1 }, new[] { -5, 2 }, new[] { -6, 2 }, new[] { -5, -2 }, new[] { -4, 2 }, new[] { -4, -2 } }) Put("Grunt", a, u[0], u[1]);
                 Put("Tank", a, -3, 0); Put("Tank", a, -3, -1); Put("Tank", a, -3, 1); Put("Tank", a, -3, -2); Put("Tank", a, -2, 0); Put("Tank", a, -4, -3); Put("Grunt", a, -3, -4); Put("Grunt", a, -2, -5); Put("Supplies", a, -5, 0); Put("Supplies", a, -6, 1); Put("Supplies", a, -6, -1);
                 Put("Tank", o, 2, 0); Put("Tank", o, 3, 0); Put("Grunt", o, 2, 1); Put("Grunt", o, 5, 0); },
             "The ai-campaign-objectives board with the task forces spread (objectiveSpread 1): the campaign's supply by logistics (AIProfile.campaignLogistics). Fronts in order of their number of units, largest first; each front's chain takes only supply units that bring supply nearer it, the largest front's first; every supply unit left is one front's own, shared by number of units; no supply unit has two jobs, and none is left without one.");
        make("ai-chain-link-rides-its-truck", "chainTruck", (a, o) => {
                 Put("Supplies", a, -4, 0); Put("Truck Variant", a, -6, 3);
                 Put("Grunt", a, 1, 0); Put("Grunt", a, 1, 1); Put("Tank", a, 2, -1); Put("Grunt", o, 5, 2); },
             "Blue's army stands beyond the HQ's supply, its one supply unit a link of the chain on foot, an empty truck by the HQ (campaign logistics). The truck is the link's (AILogistics.chainTrucks): it comes for it and the link boards it within two turns; once aboard, it is never set down (AITransportPlanner.KeepsAboard).");
        make("rule-transport-plane-boards-on-runway", "planeBoarding", (a, o) => { Rig.PlaceAirfield(a, -3, 2); Put("Transport Plane", a, -3, 3); Put("Grunt", a, -2, 3); Put("Transport Plane", a, 2, 0); Put("Grunt", a, 3, 0); Put("Transport Helo", a, 2, -3); Put("Grunt", a, 3, -3); Put("Grunt", o, 5, 1); },
             "A transport plane (an aircraft that needs a base and is not a helicopter) takes on cargo only standing on one of our buildings that services it: a grunt beside one on our runway can board it, a grunt beside one over open ground cannot. A transport helicopter over open ground still can (Transporting.CanBoardOnto).");
        make("ai-campaign-supply-not-overpromised", "campaignNoOverpromise", (a, o) => {
                 for (int y = -7; y <= 7; y++) { HexTile w = Rig.At(0, y); if (w != null && Math.Abs(y) != 4 && y != 0) w.terrain = T("Water"); }
                 Rig.PlaceBuilding("City", null, -1, -5); Rig.PlaceBuilding("City", null, 2, 3);
                 foreach (int[] u in new[] { new[] { -5, -1 }, new[] { -5, 1 }, new[] { -4, -1 }, new[] { -4, 0 }, new[] { -4, 1 }, new[] { -5, 2 }, new[] { -6, 2 }, new[] { -5, -2 }, new[] { -4, 2 }, new[] { -4, -2 } }) Put("Grunt", a, u[0], u[1]);
                 Put("Tank", a, -3, 0); Put("Tank", a, -3, -1); Put("Tank", a, -3, 1); Put("Tank", a, -3, -2); Put("Tank", a, -2, 0); Put("Tank", a, -4, -3); Put("Grunt", a, -3, -4); Put("Grunt", a, -2, -5); Put("Supplies", a, -5, 0); Put("Supplies", a, -6, 1); Put("Supplies", a, -6, -1);
                 Put("Tank", o, 2, 0); Put("Tank", o, 3, 0); Put("Grunt", o, 2, 1); Put("Grunt", o, 5, 0); },
             "The ai-campaign-objectives board, spread (objectiveSpread 1): the campaign is planned, then all but one of Blue's supply units are lost and it is planned again from the first plan. The objectives it holds never need more supply units, beyond those they have, than we have free: those least by priority are given up until they fit (AICampaign.Allocate).");
        make("profile-asset-matches-code", "profileAsset", (a, o) => { Put("Grunt", o, 4, 0); },
             "The game's AI profile (Assets/AI/TestProfile.asset) has every setting the code has, at the code's default, but campaign on: what is tested is what is played.");
        make("ai-knowledge-all-out", "knowledgeAllOut", (a, o) => { Put("Tank", a, -4, 0); Put("Tank", a, -4, 1); Put("Tank", a, -3, -1); Put("Grunt", a, -3, 0); Put("Grunt", o, 4, 0); },
             "How well Blue knows Red's forces (AIKnowledge): without fog everything is known (confidence 1), and three tanks and a grunt against one grunt is far more than knowledgeAllOutRatio: all out, the reserve released, no fear of the fog. With fog on and Red's grunt unseen, confidence is low: not all out, the reserve held.");
        make("ai-mobile-reserve", "mobileReserve", (a, o) => { Put("Tank", a, -5, -1); Put("Recon", a, -5, 2); Unit apc = Put("APC", a, -4, 2); Unit rider = Put("Grunt", a, -6, 2); Put("Grunt", a, -4, -2); Put("Grunt", a, -5, 1); Put("Gunship Variant", a, -6, 1); Put("Grunt", o, 6, -3); },
             "Who can be in the campaign's central reserve (AICampaign.ReserveMember). Mobile (campaignMobileReserve): the tank, the recon, the gunship and the APC carrying a grunt (the grunt goes with it); a grunt on foot cannot. Off: the tank, the recon and the grunt on foot; not the gunship or the APC.");
        make("ui-transport-cargo", "transportCargo", (a, o) => { Put("APC", a, 0, 0); Put("Grunt", a, 1, 0); Put("Grunt", a, -1, 0); Put("Grunt", a, 0, 1); Rig.PlaceAirfield(a, -3, 2); Put("Transport Plane", a, -3, 3); Put("Grunt", a, -2, 3); Put("Grunt", o, 5, 0); },
             "Transport visuals. Cargo slots: two grunts board an APC (slots 0 and 1); the first gets off and a third boards: it takes the free slot 0, not the second's slot 1. A unit that dies aboard (its transport destroyed) stays the size it rode at (it does not grow back to full size). A unit put down from a transport plane appears dropHeight above its tile and comes down onto it.");
        make("rule-faction-roster", "factionRoster", (a, o) => { Rig.PlaceBuilding("HQ", a, -4, 0); Rig.PlaceBuilding("ArmyBase", a, -2, 2); Put("Grunt", o, 5, 0); },
             "Factions (Faction, Building.Offered): Blue's HQ and Factory keep their base lists. Given a faction that replaces the Grunt with its own version, drops the first other unit the HQ lists, and adds an extra unit made at Factories (the ArmyBase): the HQ offers the version where the Grunt was, not the Grunt or the dropped unit; the ArmyBase offers the version in its Grunt's place and the extra; without the faction, the base lists. The version is bought and placed under its own name, is allowed where the scenario allows the Grunt, and its name and the faction's survive a save (LevelEditor.loadUnit, PlayerData.factionName).");
        make("rule-faction-load", "factionLoad", (a, o) => { Put("Grunt", o, 5, 0); },
             "Loading from the Resources folder, with nothing listed on the scene's LevelEditor: the Grunt, a faction's 'Grunt Federation Variant', the City and Grassland are found by name. A scenario's Grunt placed for a Federation side becomes the Federation's version (also when saved as another faction's version); for a side without a faction it stays the Grunt. The build menu calls the version 'Grunt' (and 'Gunship Variant' 'Gunship'). On the AI's thinking thread, after the board is copied, the faction matches its units by name without a call Unity allows only on the main thread.");
        make("rule-load-cargo", "loadCargo", (a, o) => { Put("Grunt", o, 5, 0); },
             "Loading a scenario as the game does (ScenarioLoader; the in-game load shares UnitData.LoadAll): Korea, whose trucks are listed after the supplies they carry, on the same tile, with the scene's LevelEditor listing only the older units. Every unit in the file is made (the rocket artillery, missile trucks and trucks from the Resources folder), the supplies are aboard their trucks and not active, every other unit is on its own tile, and nothing is logged as an error.");
        {
            Blank(14);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -13, 0); Put("HQ", other, 13, 0);
            Rig.PlaceBuilding("FOB", ai, 0, 0);
            Dictionary<string, string> test = Test("rule-supply-fob-refresh", "rules", "Human", 4, "supplyFob", "Supply around a forward base, and the board kept up to date. HQ at -13, FOB at 0 with a store. Nothing linking them: the FOB is cut off and gives from its store (the ground around it is supplied), and supply units standing there, or aboard a truck there, relay its supply on (they link to it as to the HQ's). After each kind of move the supply shown on the board is the network as it is: a truck carrying supply units driven by the player (its supply moves with it), a supply unit walking, an enemy blocking a corridor killed; and a supply unit's cut-off flag (the cut-off icon) follows the network, not the start of the turn. Also reports whether an enemy Blue does not see, under fog, cuts Blue's supply.");
            test["rules"] = "standard";
            ScenarioFile.Save(Path.Combine(Dir, "rule-supply-fob-refresh.dat"), test, ai, other, ai);
            n++;
        }
        {
            Blank(14);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -13, 0); Put("HQ", other, 13, 0);
            Rig.PlaceBuilding("FOB", ai, -4, 0); Rig.PlaceBuilding("FOB", ai, 5, -2);
            Dictionary<string, string> test = Test("rule-fob-prefers-hq", "rules", "Human", 4, "fobPrefersHQ", "The HQ first (SupplyNetwork.StoreOnlyWhenNeeded): a forward base cut off from the HQ's supply spends a point of its store only on a turn it feeds a unit nothing else supplies. HQ at -13, FOB at -4 (cut off, store 4): with nobody near it, it keeps its store; a grunt that the HQ's supply also reaches costs it nothing; a grunt only it supplies costs a point; linked to the HQ by a supply unit, it refills a point. A second cut-off FOB with nobody near spends nothing (the old rule: a point every turn).");
            test["rules"] = "standard";
            ScenarioFile.Save(Path.Combine(Dir, "rule-fob-prefers-hq.dat"), test, ai, other, ai);
            n++;
        }
        {
            Blank(14);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -13, 0); Put("HQ", other, 13, 0);
            Rig.PlaceBuilding("FOB", ai, -4, 0);
            Dictionary<string, string> test = Test("rule-supply-preview-fob", "rules", "Human", 4, "previewFob", "The player's supply preview for a truck carrying supplies, linked through a forward base: HQ at -13, FOB at -4 linked to it by two supply units, a truck carrying supplies at 1,2 moved forward to 7,2. Picked (the preview), the destination shows supplied, as it is once the move is made, even with the AI's what-if rule (a supply unit put down where it is moved to) left on the main thread; and an AI turn thought on the main thread no longer leaves that rule on.");
            test["rules"] = "standard";
            ScenarioFile.Save(Path.Combine(Dir, "rule-supply-preview-fob.dat"), test, ai, other, ai);
            n++;
        }
        {
            Blank(14);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -13, 0); Put("HQ", other, 13, 0);
            Rig.PlaceBuilding("FOB", ai, -4, 0);
            Dictionary<string, string> test = Test("fog-rule-supply-shown", "rules", "Human", 4, "fogSupplyShown", "Under fog, the supply the screen shows does not give enemies away (SupplyNetwork.ShownAsKnown): a Blue grunt at the edge of a forward base's supply, a line of Red grunts Blue does not see cutting it off. The grunt is truly cut off (its upkeep goes by that) but shown supplied; once a Blue recon sees the line, shown cut off; with fog off the screen shows the network as it is. An enemy's supply is shown as it is on tiles Blue sees: a tile a Blue recon sees shows in Red's supply, though the Red supply unit giving it is in the fog.");
            test["rules"] = "standard";
            ScenarioFile.Save(Path.Combine(Dir, "fog-rule-supply-shown.dat"), test, ai, other, ai);
            n++;
        }
        make("ui-fob-store-pips", "fobStorePips", (a, o) => { Rig.PlaceBuilding("FOB", a, -4, 0); Rig.PlaceBuilding("FOB", o, 4, 0); },
             "A forward base's store shown as supply pips (SupplyDot, one per circle of a pip row under the building, as HealthDot for strength): Blue's FOB with 3 of 6 in store shows 3 filled and 3 faint, the seventh hidden; after spending one, 2; after refilling three, 5. The HQ keeps no store and shows none. A pip named 'circle (4)' is the fifth: filled with 5 in store, faint with 2. Under fog Blue sees its own FOB's pips always, Red's only while a Blue unit sees it.");
        make("ai-assault-ride", "assaultRide", (a, o) => {
                 Unit apc = Put("APC", a, -6, 0); Unit g1 = Put("Grunt", a, -6, 1); Unit g2 = Put("Grunt", a, -7, 1);
                 Transporting.Load(apc, g1); Transporting.Load(apc, g2);
                 Put("Tank", a, -5, 1); Put("APC", a, -5, 4); Put("Grunt", a, -6, 4);
                 Put("Artillery", o, 5, 0); },
             "Riding to the assault (assaultRides): an APC carrying two grunts moves up with them aboard, out of the enemy's reach, and puts them down only where they can strike that turn (getting off costs nothing: whether they then attack is their own call), to hold the line once their front holds its ground (here, with the enemy gone), or once at the front line (in contact next turn: they get off and decide for themselves); nothing else puts them down. Also reports whether a grunt far from the fight gets into the empty APC beside it. Blue plays four turns against a standing Red artillery piece (which cannot fire back at a unit beside it).");
        make("ai-armed-transport", "armedTransport", (a, o) => {
                 Unit rc = Put("Recon", a, -3, 0); Unit g = Put("Grunt", a, -3, 1); Transporting.Load(rc, g);
                 Put("Tank", a, -4, 1);
                 Put("Grunt", o, 2, 0); },
             "An armed transport with a rider (assaultFireThenUnload, assaultFrontlineOut): a Blue recon carrying a grunt, a Blue tank, a Red grunt standing in the open. On Blue's turn (no coordinated plans: the transport's own choice) the recon stops where it can fire as well as put its rider down, fires on the Red grunt first and lets its rider out after (its rider still has its whole turn). Then: a recon whose assault move was set aside for its ordinary one (a fallback) has its rider get off to decide for itself.");
        make("ai-capturer-lives", "capturerLives", (a, o) => { Put("Grunt", a, 3, 3); Put("APC", a, 1, 3); Rig.PlaceBuilding("City", a, -6, 4); Put("Grunt", o, 6, -6); },
             "A capturer that lives to take it (capturerLives, AIAssault.TurnsToTake): a Blue grunt down to strength 2 a tile from Red's HQ, out of supply, an empty APC beside it and Blue's city far behind. Taking the HQ needs 20 points (brought down from 10, then up to 10) at 2 a turn while it starves: it would be gone first. So it is not the HQ's capturer and it does not go and stand on it; it rides to mend. Then a full-strength grunt in the same place: it would take the HQ in time, and the campaign makes it the capturer.");
        make("ai-gather-with-body", "gatherWithBody", (a, o) => { Put("Grunt", a, -5, 2); Put("Tank", a, 3, -2); Put("Tank", o, 5, 2); Put("Grunt", o, 6, 1); },
             "Gathering counts the body, not a lone unit up front (campaignGatherWithBody): Blue has a grunt back home and a tank alone near Red's force; together they make the task force's need, but they are not together. With the rule the front does not go in on its first turn (it gathers); without it, the lone tank ahead counts and it goes.");
        make("ai-medevac", "medevac", (a, o) => { Put("Grunt", a, 0, -5); Put("APC", a, -1, -4); Rig.PlaceBuilding("City", a, -6, 4); Put("Grunt", o, 6, -6); },
             "Medevac (assaultRides, assaultMedevacStrength): a Blue grunt down to strength 2, away from Red's HQ (boarding is weighed against its own move: a wounded unit a turn from taking a building would go and take it), an empty APC beside it, Blue's city nine tiles off (three turns' walk and more): within three Blue turns the APC has carried it onto a building that mends it, and once there it does not ride off to the fight while still wounded (strength 3 or less).");
        {
            Blank(14);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -13, 0); Put("HQ", other, 13, 0);
            Rig.PlaceBuilding("ArmyBase", ai, -11, 0);
            foreach (int[] g in new[] { new[] { -10, 2 }, new[] { -9, 2 }, new[] { -10, 3 }, new[] { -9, 3 } }) Put("Grunt", ai, g[0], g[1]);
            Put("APC", ai, -12, 1);
            Put("Tank", other, 11, 0); Put("Grunt", other, 11, 1);
            Dictionary<string, string> test = Test("ai-buy-transports", "rules", "Human", 4, "buyTransports", "Buying for riders (assaultTransportWeight, assaultFillBonus): four Blue grunts far from the fight and an APC that is already carrying... nothing: with 20,000 gold Blue buys at least one transport at its army base for the riders without room; then, with an empty APC beside the base, the infantry it buys there is worth more for filling it.");
            test["purchases"] = "on";
            ScenarioFile.Save(Path.Combine(Dir, "ai-buy-transports.dat"), test, ai, other, ai);
            n++;
        }
        // Buying infantry where infantry is what is wanted: one board each, the whole AI playing (so the campaign's fronts
        // exist when it buys), nothing of Blue's that can capture on the board, 6000 gold, a lone Red grunt far off.
        //   near:  buildings to take two or three tiles from the army base.
        //   front: buildings to take at the far end of the board (not near any facility), a Blue tank already there.
        //   plain: only buildings that earn nothing and build nothing (a bunker, Int. Ops) beside the base.
        Action<string, Action<Player, Player>, string> infantry = (name, setup, question) => {
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("HQ", other, 6, 0);
            Put("Grunt", other, 5, 0);
            try { setup(ai, other); } catch (FileNotFoundException e) { Console.WriteLine("  (not made: " + name + " needs " + Path.GetFileName(e.FileName) + ", no longer in the game)"); return; }
            Dictionary<string, string> test = Test(name, "rules", "Human", 4, "buyInfantry", question);
            test["purchases"] = "on";
            ScenarioFile.Save(Path.Combine(Dir, name + ".dat"), test, ai, other, ai);
            n++;
        };
        infantry("ai-buy-infantry-near", (a, o) => { Rig.PlaceBuilding("ArmyBase", a, -4, 0); Rig.PlaceBuilding("City", null, -3, 2); Rig.PlaceBuilding("Village", null, -3, -2); Rig.PlaceBuilding("City", null, -2, 0); },
                 "Buying infantry for buildings near a facility: three neutral buildings two or three tiles from Blue's army base, nothing of Blue's that can capture: within three turns it buys a unit that can.");
        infantry("ai-buy-infantry-front", (a, o) => { Rig.PlaceBuilding("ArmyBase", a, -5, 1); Put("Tank", a, 1, 0); Rig.PlaceBuilding("City", null, 3, -3); Rig.PlaceBuilding("City", null, 3, 3); },
                 "Buying infantry for buildings at the front: two neutral cities at the far end of the board (near no facility), a Blue tank there that cannot take them: within three turns it buys a unit that can.");
        infantry("ai-buy-infantry-plain", (a, o) => { Rig.PlaceBuilding("ArmyBase", a, -4, 0); Rig.PlaceBuilding("Bunker", null, -3, 2); Rig.PlaceBuilding("Int. Ops", null, -3, -2); },
                 "Buying infantry for buildings that earn and build nothing: a neutral bunker and Int. Ops beside Blue's army base: within three turns it buys a unit that can take them.");
        make("ai-cover-by-target", "coverByTarget", (a, o) => { Put("Anti Air Missile Truck", a, -4, 0); Put("Artillery", a, -4, 2); Put("Tank", o, 3, 0); Put("Grunt", o, 3, 2); },
             "A gun's zone of control (artilleryCoverValue) counts the tiles in its range the enemy could move to next turn. With coverByTarget, only for enemies it can fire on: the anti-air missile truck (aircraft only) holds none of the ground Red's tank and grunt could reach; the artillery still holds it. Off: the truck held it all.");
        make("ai-self-preservation-scope", "selfPreservationScope", (a, o) => { Put("Grunt", a, -4, 0); Put("Tank", a, -4, 2); Put("Artillery", a, -5, 2); Put("Anti Air Missile Truck", a, -5, 1); Put("Supplies", a, -6, 1); Put("Tank", o, 0, 0); Put("Tank", o, 0, 2); },
             "Which units shield anything by standing in reach (AIWorld.Shields, selfPreservationShieldless): those that fire back at ground attackers beside them (the grunt, the tank); not the artillery, the anti-air truck or the supply unit. With self-preservation on, the truck standing where both Red tanks could reach pays for it; priced as the reply prices blows (selfPreservationDiscount) it pays no more than undiscounted.");
        make("rule-capture-neutral", "captureNeutral", (a, o) => { Rig.PlaceBuilding("City", null, 0, 0); Put("Grunt", a, 0, 0); },
             "A Blue grunt (6) standing on a neutral City (threshold 10): neutral with 6 Blue after one Blue turn, Blue's after two (progress cleared).");
        make("rule-capture-from-zero", "captureFromZero", (a, o) => { Rig.PlaceBuilding("City", a, 0, 0); Put("Grunt", o, 0, 0); },
             "A Blue City, a Red grunt (6) on it: it is not brought down to neutral first — after Red's turn it is still Blue's with Red 6/10, after Red's second turn Red's.");
        make("rule-attack-no-capture", "attackNoCapture", (a, o) => { Rig.PlaceBuilding("City", a, 0, 0); Put("Grunt", o, 0, 0); Put("Grunt", a, 1, 0); },
             "A Red grunt on a Blue City that attacks during its turn does not capture.");
        make("rule-capture-on-action", "captureOnAction", (a, o) => { Rig.PlaceBuilding("City", null, 0, 0); Put("Grunt", a, 2, 0); },
             "A Blue grunt (6) that ends its move on a neutral City captures there and then (6 Blue before the turn ends), and not again as the turn ends; standing there through its next turn it takes it.");
        make("rule-neutral-decay", "decay", (a, o) => { Building c = Rig.PlaceBuilding("City", null, 0, 0); c.capturingPlayer = o; c.capturePoints = 4; Put("Grunt", o, 3, 0); },
             "A neutral City with 4 Red progress and no Red infantry on it: lost (0, nobody capturing) after Red's turn. An owned building nobody is taking has nothing to lose.");
        make("rule-hq-standard", "hqStandard", (a, o) => { Put("Grunt", o, -6, 0); },
             "Standard rules: a Red grunt (6) on Blue's HQ (10). Two Red turns take it (6, then 12): Blue has lost.");
        make("rule-captured-hq-supplies-nobody", "capturedHQ", (a, o) => { },
             "Sandbox rules: Blue's HQ taken by Red supplies Red nothing (an HQ supplies only the side it began with); retaken, it supplies Blue again. The home side is saved and loaded with the board.");
        make("attrition-rule-hq", "attritionHQ", (a, o) => { Put("Grunt", o, -6, 0); Put("Grunt", a, -3, 2); },
             "Attrition rules: the HQ cannot be captured, a Red grunt standing on it changes nothing.");
        make("rule-fob-battery", "battery", (a, o) => { Building f = Rig.PlaceBuilding("FOB", a, -4, 0); f.supplies = 0; Put("Grunt", a, -3, 1); },
             "A Blue FOB inside the HQ's supply charges 1 a turn; cut off (the HQ lost), it supplies its area for as many turns as it stored, then stops.");
        make("rule-production", "production", (a, o) => { },
             "Blue's HQ builds a Grunt from 3000: it appears on the HQ, spent for the turn, money 3000 less the Grunt's cost.");
        // Air and sea (attrition: these do not test capture).
        Action<int, int, int, int> water = (x0, x1, y0, y1) => { for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) { HexTile t = Rig.At(x, y); if (t != null) t.terrain = T("Water"); } };
        make("attrition-rule-air-fuel", "attritionAirFuel", (a, o) => { Unit f = Put("Fighter", a, -4, 1); f.fuel = 5; f.Strength = 4; Put("Grunt", o, 4, 0); },
             "A Fighter (fuel 5, burns 3 a turn; 3 supplies aboard) inside the HQ's supply but off any airport: not healed or refuelled, its own supplies down to 2 and 2 fuel after a turn, ditched the next.");
        make("attrition-rule-air-base", "attritionAirBase", (a, o) => { Rig.PlaceAirfield(a, -3, 2); Unit f = Put("Fighter", a, -3, 3); f.fuel = 2; f.Strength = 4; Put("Grunt", o, 4, 0); },
             "A Fighter on its own airport's runway (fuel 2, strength 4) is refuelled to 14 and healed to 5.");
        make("attrition-rule-air-tanker", "attritionTanker", (a, o) => { Unit f = Put("Fighter", a, 0, 0); f.fuel = 6; Unit g = Put("Fighter", a, 2, 0); g.fuel = 6; Put("AirTanker", a, 1, 0); Put("AirTanker", a, -2, 4); Put("AirTanker", a, -3, 4); Put("Grunt", o, 5, 0); },
             "Two Fighters beside an air tanker with 2 supplies. The tanker's supplies run down a turn at a time like any aircraft's, and while it started the turn with some it refuels and resupplies the aircraft beside it at no further cost: turn 1 both fighters are filled (tanker 1 left), turn 2 again (0 left); turn 3 it has none, the fighters burn 3, and the tanker loses a strength as any aircraft out of supplies. Two tankers side by side do not feed each other: both run down.");
        make("attrition-rule-ship-fuel", "attritionShip", (a, o) => { water(-1, 2, -7, 7); Rig.PlaceBuilding("NavalYard", a, -2, 0); Put("Gunboat", a, 1, 2); Unit d = Put("Gunboat", a, -2, 0); d.Strength = 3; d.fuel = 10; Put("Grunt", o, 5, 0); },
             "Ships burn fuel only by moving (3 tiles: 3 fuel) and never starve away from port; one on its naval yard is healed and refuelled.");
        make("attrition-rule-refuel-on-landing", "attritionLandRefuel", (a, o) => { Rig.PlaceAirfield(a, -3, 2); Unit f = Put("Fighter", a, 0, 2); f.fuel = 3; f.ammo = 1; Unit g = Put("Fighter", a, 0, 4); g.fuel = 5; g.ammo = 1; Put("Grunt", o, 4, 0); },
             "A Fighter with 3 fuel flies 3 tiles onto its airport's runway (a plain move): refuelled and re-armed as it lands (fuel 14, ammo 6). Another lands there as a move and attack: nothing until its next turn (fuel 5 - 3 = 2, ammo 1). The airport is then lost before their next turn: the first burns a turn's fuel (11) and is not ditched.");
        make("attrition-rule-supply-aboard", "attritionSupplyAboard", (a, o) => { Put("APC", a, 1, 0); Put("Supplies", a, 2, 0); Put("Grunt", o, 6, -3); },
             "A supply unit (range 4) aboard an APC (moves 6) beyond the HQ's supply: linked to the network, because the APC could carry it onto supplied ground in one move, and spreading supply only its own range around the APC. With the APC slowed to 1 move, it is cut off. Exactly: with as many moves as it takes to drive onto supplied ground it links, with one fewer it does not (beside supplied ground is not enough for a land transport).");
        make("attrition-rule-supply-line", "attritionSupplyLine", (a, o) => { Put("Supplies", a, -3, 0); Put("Grunt", a, -2, 0); Put("Grunt", o, 6, -3); },
             "A grunt supplied only through a supply unit standing at the edge of the HQ's supply: the line traced back from the grunt runs to the supply unit, then on to the HQ.");
        make("rule-bunker", "bunker", (a, o) => { Rig.PlaceBuilding("Bunker", a, 0, 0); Put("Grunt", a, 0, 0); Put("Tank", o, 1, 0); Put("Grunt", o, -1, 0); Put("Gunship", a, -3, 3); },
             "A Blue grunt in a Blue bunker (the Bunker prefab: holdsGround, attack and defence bonuses), attacked by a Red tank beside it (made hard-hitting): it is not driven back, takes less and hits a Red grunt beside it harder than on a bunker that is not its side's. A Blue gunship over the bunker has none of it: the garrison is land units only.");
        make("rule-retreat-away", "retreatAway", (a, o) => { Put("Tank", a, 1, 0); Put("Grunt", o, 0, 0); },
             "A Red grunt attacked by a Blue tank beside it retreats straight back, to one of the three tiles on its far side; with those three taken it is pinned, never sliding sideways (still beside the tank).");
        make("ai-land-steps-layout", "landStepsLayout", (a, o) => {
                 water(0, 0, -7, 7);
                 Rig.At(0, 0).terrain = T("Bridge");
                 Rig.At(-1, 0).terrain = T("Road"); Rig.At(1, 0).terrain = T("Road");
                 Put("Grunt", a, -3, 0); Put("Grunt", o, 4, 0); },
             "Land steps (the map's shape for strategy) are kept for the whole game, one table per layout of terrain and buildings, shared by both sides. A river with one bridge: from the east shore's road, the west shore's road is two steps over the bridge. The bridge is destroyed (it becomes shallows): the table is worked out again, and the west shore is no longer reachable by land; a new map read on the same layout reuses the same table.");
        make("rule-bridge-road-only", "bridgeRoad", (a, o) => {
                 water(0, 0, -7, 7);
                 Rig.At(0, 0).terrain = T("Bridge");
                 Rig.At(-1, 0).terrain = T("Road"); Rig.At(1, 0).terrain = T("Road");
                 Put("Grunt", a, -1, 1); Put("Grunt", o, 4, 3); },
             "A river with one bridge, a road tile at each end and grass beside them. A bridge joins only those roads: no stepping onto it from the grass beside it, or off it onto the grass across; a unit driven back off it goes onto the road.");
        make("rule-bridge-water", "bridgeWater", (a, o) => {
                 water(0, 0, -7, 7);
                 Rig.At(0, 0).terrain = T("Bridge");
                 Rig.At(-1, 0).terrain = T("Road"); Rig.At(1, 0).terrain = T("Road");
                 Unit boat = Put("Gunboat", a, 0, 0); boat.elevationMax = 0;
                 Put("Grunt", a, -1, 0); Put("Grunt", o, 4, 3); },
             "A river with one bridge and a road at each end; a gunboat that can stand on water and bridges (elevation -2 to 0) on the bridge, and a grunt on the road beside it. Between a bridge and the water under it the boat may always step (the bridge's road-only rule is for land): it reaches the water from the bridge, and back onto it. The grunt still cannot step into the water, and still cannot step off the bridge onto the grass.");
        make("rule-swap-sides", "swapSides", (a, o) => { Put("Tank", a, -2, 0); Put("Grunt", a, -3, 1); Put("Artillery", o, 3, 0); Building c = Rig.PlaceBuilding("City", o, 2, 2); c.capturingPlayer = a; },
             "HexTactica > Swap Sides of Scenario File on this file: every unit and building changes to the other side (the HQs too, and who was capturing what); the player list and terrain are unchanged.");
        make("rule-building-service", "buildingService", (a, o) => {
                 Rig.PlaceBuilding("City", a, 4, -4); Put("Grunt", a, 4, -4);
                 Rig.PlaceBuilding("City", a, -5, 1); Put("Grunt", a, -5, 1);
                 Put("Grunt", o, 6, -2); },
             "Two of Blue's cities that service infantry, each with a wounded grunt (strength 3, no ammunition, 2 supplies) on it. Far beyond supply, a city that heals 1 and gives rations but no ammunition: the grunt heals to 4, is fed, and is still out of ammunition. Inside the HQ's supply, a city whose heal of 1 stacks with the network's: the grunt heals to 5 and is refilled.");
        make("ai-heal-where-it-heals", "aiHealWhere", (a, o) => {
                 Rig.PlaceBuilding("City", a, -5, 1); Put("Grunt", a, -3, 0); Put("Tank", o, 3, 0); Put("Grunt", o, 4, -1); },
             "A wounded Blue grunt (strength 2) inside the HQ's supply, a Red tank six tiles off (out of reach of the city), and a Blue city two tiles away that services infantry and heals 2 a turn on top of the network's 1. The AI counts 3 there and 1 on plain supplied ground, and the grunt pulls back onto the city.");
        make("ai-fight-before-capture", "aiFightFirst", (a, o) => {
                 Rig.PlaceBuilding("City", null, 1, 0); Put("Grunt", a, 0, 0); Put("Grunt", o, 1, -1); },
             "A Blue grunt beside a neutral city, with a Red grunt beside it too. With an attack open to it, it does not step onto the city to capture (AIProfile.fightBeforeCapture): it attacks, or falls back if the attack is a bad one; with the rule off it captures.");
        make("ai-campaign-clear-the-building", "aiCampaignClear", (a, o) => {
                 Rig.PlaceBuilding("City", null, -2, 0); Put("Tank", a, -2, 0); Put("Grunt", a, -3, 1); Put("Grunt", o, 5, -3); },
             "Campaign on: a neutral city near Blue's HQ, a Blue tank standing on it and a Blue grunt beside it, Red far away. The city is the campaign's objective; the tank cannot capture, so it steps off on Blue's first turn, and by Blue's second the grunt is on the city taking it. (A gunship parked on a city a whole game long kept Blue's army round it for 30 rounds on RiverCrossing.)");
        make("rule-attacks-locomotion", "attacksLocomotion", (a, o) => { Put("Artillery", a, 0, 0); Put("Fighter", o, 2, 0); Put("Grunt", o, 2, 1); },
             "Blue's artillery with jets unticked in its locomotion checklist (Unit.attacksLocomotion, read from the prefab): a fighter in range cannot be attacked (Unit.CanAttack false, no damage, not a valid target), a grunt in range can (boots ticked). With boots unticked too, the grunt cannot be attacked either.");
        make("ui-fuel-warning-tiles", "fuelWarning", (a, o) => { Rig.PlaceAirfield(a, -3, 2); Unit f = Put("Fighter", a, 0, 0); f.fuel = 10; Put("Grunt", o, 5, 0); },
             "A fighter at (0,0) with 10 fuel (1 a tile, 3 a turn in the air), Blue's airport at (-3,2). A move tile it could not get back to a building that refuels it from before running dry next turn is marked with the move warning (Unit.WouldDitchFrom): (3,0) is — 3 tiles flown and a turn's burn leave 4, and the airfield is further than that — (-2,1) is not, nor is the airport itself. With 3 fuel every tile is, but the airport. A stocked Blue carrier two tiles past (3,0) is somewhere to land too: (3,0) is no longer marked; with the carrier out of supplies it is again.");
        make("ai-regroup-press-with-reserves", "aiRegroupPress", (a, o) => { Put("Tank", a, 0, 0); Put("Supplies", o, 1, 0); Put("Grunt", o, 0, 1); for (int x = 2; x <= 4; x++) Put("Grunt", o, x, 0); },
             "The hit-the-supply-unit board: Blue's tank beside the enemy supply unit that keeps four grunts going, with regrouping on. Blue has 100,000 gold and an HQ that builds: what it could replace makes its front aggressive (AIStrategic.StrategicNeed), and the tank hits the supply unit.");
        make("ai-regroup-hold-off-without-reserves", "aiRegroupHold", (a, o) => { Put("Tank", a, 0, 0); Put("Supplies", o, 1, 0); Put("Grunt", o, 0, 1); for (int x = 2; x <= 4; x++) Put("Grunt", o, x, 0); },
             "The same board, but Blue has no gold and purchases are off: nothing to replace a loss with, so its front is defensive and the tank does not engage — it ends where no enemy can strike it tonight (AIProfile.regroup).");
        make("ai-clear-our-hq", "aiClearOurHQ", (a, o) => { Put("Tank", a, -4, 0); Put("Marine", o, -6, 0); Put("Grunt", o, -3, 2); },
             "A Red marine stands on Blue's HQ, which it has worn down to 4 of 10; a Red grunt at strength 2 stands two tiles from Blue's tank, the other way. The tank goes for the marine on the HQ (AIProfile.hqThreatOn, and the HQ's points in the move score), not the easy kill.");
        make("ai-hold-the-enemy-hq", "aiHoldHQ", (a, o) => { Put("Grunt", a, 6, 0); Put("Grunt", a, 5, 0); Put("Tank", o, 3, 1); },
             "A Blue grunt on Red's HQ, wounded to strength 1 and out of rations, with a Red tank two tiles off. It stays on the HQ to take it (no withdrawing to heal, no attack that would stop the capture).");
        make("ai-campaign-chokepoint-objectives", "campaignChokepoints", (a, o) => {
                 water(0, 0, -7, 7);
                 Rig.At(0, 0).terrain = T("Bridge");
                 Rig.At(-1, 0).terrain = T("Road"); Rig.At(1, 0).terrain = T("Road");
                 Put("Grunt", a, -4, 0); Put("Grunt", a, -4, 1); Put("Grunt", o, 2, 1); },
             "A river with one bridge (the rule-bridge-road-only board) and Blue's grunts on the near side: the bridge is a chokepoint, and the campaign has an objective at each end of it — on our side and on theirs — to bring into our supply (AICampaign.Places, AIProfile.campaignPlaces).");
        make("attrition-rule-cut-off-supply", "attritionCutOff", (a, o) => { Put("Supplies", a, 4, 0); Put("Grunt", a, 4, 1); Put("Grunt", o, 6, -4); },
             "A supply unit beside a grunt, both far outside the HQ's supply: a supply unit relays supply, it does not make it, so neither is supplied. After a turn the grunt has used a turn of its own supplies and the supply unit has lost no strength.");
        Action<string, string, Action<Player, Player>, string, string, string> makeBuy = (name, check, setup, question, purchases, allowed) => {
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("HQ", other, 6, 0);
            try { setup(ai, other); } catch (FileNotFoundException e) { Console.WriteLine("  (not made: " + name + " needs " + Path.GetFileName(e.FileName) + ", no longer in the game)"); return; }
            Dictionary<string, string> test = Test(name, "rules", "Human", 1, check, question);
            test["rules"] = "standard";
            if (purchases != null) test["purchases"] = purchases;
            if (allowed != null) test["allowedUnits"] = allowed;
            ScenarioFile.Save(Path.Combine(Dir, name + ".dat"), test, ai, other, ai);
            n++;
        };
        // Fog of war: small boards, sight and its rules checked by hand (Red is human, so the screen shows Red's view).
        Action<string, string, Action<Player, Player>, string, string> makeFog = (name, check, setup, question, opponent) => {
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("HQ", other, 6, 0);
            try { setup(ai, other); } catch (FileNotFoundException e) { Console.WriteLine("  (not made: " + name + " needs " + Path.GetFileName(e.FileName) + ", no longer in the game)"); return; }
            Dictionary<string, string> test = Test(name, "rules", opponent, opponent == "Full" ? 12 : 4, check, question);
            test["rules"] = "attrition";
            test["fog"] = "on";
            ScenarioFile.Save(Path.Combine(Dir, name + ".dat"), test, ai, other, ai);
            n++;
        };
        makeFog("fog-rule-unit-detector", "fogUnitDetect", (a, o) => { Put("Grunt", a, -4, 0); Put("Recon", o, 1, 0); },
             "A unit's detectRange works as a Radar's: a stealthy Red recon (stealth 3) five tiles from a Blue grunt (vision 2) is unseen; with the grunt's detectRange 5 it is seen, and six tiles off it is not. Each time it goes from unseen to seen it is announced once (FogOfWar.StealthSpotted, the cue for the detection sound), not again while it stays seen.", "Human");
        makeFog("fog-rule-int-ops", "fogIntOps", (a, o) => {
                 Rig.PlaceBuilding("Int. Ops", a, -4, 0); Put("Grunt", a, -7, 3);
                 Rig.At(-3, 0).terrain = T("Mountains");
                 Put("Recon", o, 1, 0); },
             "Int. Ops (the prefab: detectRange 5, vision 6 over terrain): a stealthy Red recon five tiles from it is seen, which no observer that far off would see (stealth 3), and six tiles off it is not; with its detection off it is unseen at five too. The tile four beyond a mountain beside it is seen; with visionOverTerrain off it is not (the mountain blocks the line).", "Human");
        makeFog("fog-rule-vision", "fogVision", (a, o) => {
                 Put("Grunt", a, -2, 0); Put("Grunt", o, 0, 0); Put("Grunt", o, 1, 0); Put("Recon", o, 2, -3);
                 Rig.PlaceBuilding("City", a, 2, 2); Put("Artillery", o, -2, 4); Put("APC", o, 4, -4); Put("Grunt", o, 4, -3); },
             "Sight: a grunt (vision 2) sees an enemy two tiles away, not three; with vision 4 it sees three. A stealthy recon (stealth 3) inside its vision is unseen three tiles off and seen two off; next to a unit it is always seen, and on one of our buildings too. Cargo is seen when its transport is. Artillery firing from out of sight is seen from then until the end of the side it hit's next turn. The screen (Red's view) hides Blue units Red does not see and half-fades Red's stealthy units Blue does not see (not the others). With fog off, everything is seen.", "Human");
        makeFog("ai-thread-equivalence", "aiThread", (a, o) => { Put("Grunt", a, -3, 0); Put("Grunt", o, 3, 0); },
             "The AI thinking on a worker thread (on its own copy of the board) makes no call Unity allows only on the main thread (the harness throws as Unity does), and plays exactly as thinking in line, with fog of war off and on, while the main thread keeps reading the board (move ranges, sight, supply) as the game does: the board after every turn is the same, and the real board is never seen half-changed. Played on gauntlet-1 and air-gauntlet-1, both sides the AI, six rounds each way.", "Human");
        makeFog("fog-rule-line-of-sight", "fogLineOfSight", (a, o) => {
                 Put("Grunt", a, -4, 0); Put("Grunt", o, -1, 0); Put("Fighter", a, -4, 3); Put("Gunboat", a, -4, -3);
                 Rig.At(-2, 0).terrain = T("Hills"); },
             "Line of sight for land and sea units: ground higher than the observer's tile hides what is behind it. A grunt on grassland (vision 4) sees the hill two tiles off but not the forest, the grassland or the enemy grunt behind it; from the hill it sees the forest beside it and what is behind that (as high as the hill, no higher); a mountain on the line hides even from the hill. Where the line runs along the edge between two tiles one high tile does not hide, two do. An aircraft sees over everything; a gunboat at sea is hidden from by land higher than the water, not by shallows. An enemy behind the hill is unseen until it stands next to one of ours.", "Human");
        makeFog("fog-rule-kinds", "fogKinds", (a, o) => {
                 Put("Grunt", a, -2, 0); Put("Grunt", o, 1, 0); Put("Fighter", a, -5, 4); Put("Fighter", o, 0, -5);
                 foreach (int[] w in new[] { new[] { 4, 2 }, new[] { 5, 2 }, new[] { 4, 3 }, new[] { 5, 1 } }) Rig.At(w[0], w[1]).terrain = T("Water");
                 Put("Gunboat", a, 4, 2); Put("Grunt", o, 2, 4); Rig.PlaceBuilding("City", a, -3, 3); },
             "High ground and kinds of unit: a land unit sees one tile further from hills and two from mountains (an aircraft gets nothing from them). A stealthy unit is seen only by observers of its own kind (land — buildings count as land — air, sea): a stealth jet next to a grunt or over our city goes unseen, a fighter two tiles off sees it; a stealthy grunt next to a gunboat goes unseen, a grunt two tiles off sees it, and on our city it is seen.", "Human");
        makeFog("rule-combat-scars-target", "combatScars", (a, o) => { Put("Tank", a, -2, 0); Put("Grunt", o, -1, 0); Put("Grunt", a, -2, 3); Put("Grunt", o, 0, 3); },
             "Combat damages the ground fought over, the target's tile, never the tile fired from: a Blue tank attacks a Red grunt beside it (the grunt answers); a Blue grunt walks into a hidden Red grunt (the ambusher fires first). The tile of the unit fired on takes damage: the Red grunt's in the attack, the walker's in the ambush; never the tile of the one that fired, and an answer (retaliation) damages nothing.", "Human");
        makeFog("fog-rule-bump-artillery", "fogBumpArt", (a, o) => { Put("Artillery", a, -2, 0); Put("Grunt", o, 0, 0); Put("Grunt", a, -2, 3); Put("Artillery", o, 0, 3); },
             "Artillery (no close-range fire, never answers) running into a hidden grunt: the grunt fires on it, and the artillery plays no shot back.", "Human");
        makeFog("fog-rule-bump", "fogBump", (a, o) => { Put("Grunt", a, -2, 0); Put("Grunt", o, 0, 0); Put("Grunt", a, 0, 3); Put("Grunt", o, 2, 3); },
             "Running into a hidden enemy: a Blue grunt (vision 1) moving through a Red grunt it cannot see stops on the tile before it; its move is over; the Red grunt fires first (whatever the ground) and the Blue grunt answers; the Red grunt is seen from then. The move range shown is the same as if the hidden unit were not there (its tile can be ended on as far as Blue knows).", "Human");
        makeFog("fog-view-after-load", "fogViewLoad", (a, o) => { Put("Grunt", o, -3, 0); Put("Grunt", a, 3, 0); },
             "Loading a scenario with fog of war on (Red, human, to play): every side's supply is published to the board at once (the borders are drawn), and on the first frame every tile Red does not see is covered, none it sees, without fading in. The fog reaches one cell past the edge of the board; those cells are cleared only beside a unit Red sees standing on the edge (not beside an enemy it does not see), and with fog off there are none.", "Human");
        makeFog("fog-ui-clicks", "fogClicks", (a, o) => { Put("Grunt", o, 0, 0); Put("Grunt", a, 2, 0); },
             "The board's clicks under fog of war, Red (human, vision 1) to play: a tile with a Blue grunt Red does not see behaves exactly like an empty one — clicked with nothing selected it selects nothing and shows no enemy; with Red's grunt selected it is offered as a move, the tile beyond it too, and the path drawn across it looks like a path across empty ground. Moving through it, Red's grunt stops before it and is fired on.", "Human");
        makeFog("fog-ai-knowledge", "fogKnowledge", (a, o) => { Put("Tank", a, -2, 0); Put("Recon", a, 0, 0); Put("Grunt", o, 3, 0); Put("Grunt", o, 2, -5); Put("Tank", o, 5, -2); },
             "The AI playing by the fog: thinking, it knows only what Blue sees — a hidden enemy is off the board (not in its read of the board, not targetable, not blocking) and one seen lately stands where it was last seen, unless that tile is in sight and empty; everything is put back afterwards. An ambush tile (out of the enemy's sight, in reach of an enemy) is worth something, an exposed one is not; a stop that brings unseen tiles into sight is worth more than one that does not. With fogFair off it knows everything.", "Human");
        makeFog("fog-ai-sigint", "fogSigint", (a, o) => { Put("Gunship Variant", a, -3, 0); Put("Grunt", a, -4, 2); Put("Recon", a, -1, 0); Put("Grunt", o, 1, 0); Put("Tank", o, 5, -4); },
             "What the fog may hide (fogThreat): Blue saw a Red grunt, which then went out of sight, and its last tile has been seen empty since. It is not believed there any more, but it is still about: a Blue grunt ending its turn where (a grunt does no damage to a gunship since main's airborne damage floor of 0) that unit could reach it tonight expects the share of its damage from the share of the places it could be (unseen) from which it could; less the more moves it has had, but never nothing (it is not forgotten); a tile out of its reach expects nothing from it. Ground not seen lately may hide any kind of unit the enemy has been seen to field: priced by fogAggression (0: in full, 1: not at all), more next to more unknown ground. A scouting stop that would look where the lost unit may be is worth more. The damage priced is the game's own (Unit.calculateDamage) for every pair of kinds on the board. With fogThreat off there is nothing.", "Human");
        makeFog("fog-ai-risk-by-price", "fogPrice", (a, o) => { Put("Bomber", a, -3, 0); Put("Conscript", a, -4, 2); Put("Recon", a, -1, 0); Put("Gunner Anti Air", o, 1, 0); Put("Grunt", o, 1, 2); Put("Tank", o, 5, -4); },
             "Blue saw a Red anti-air gunner and a Red grunt, then lost both (their tiles seen empty since). The fog risk of a stop is the strength each lost unit could take there (its own damage against the unit in question, by the share of its places from which it reaches) times the unit's worth: next to where they may be, a bomber's risk comes only from the anti-air gunner (a grunt cannot hit it), and a bomber's risk in gold is many times a conscript's for the same tile.", "Human");
        makeFog("fog-ai-unknown-ground", "fogUnknownGround", (a, o) => { Put("Tank", a, -5, 0); Put("Conscript", a, -5, 2); Put("Grunt", o, 4, 0); Put("Tank", o, 4, 2); },
             "Unknown ground (fogUncertainty): Blue has never seen east of the middle, and none of Red's units. At a stop beside that ground, the fog's loss is the average damage of the kinds Red fields by the share of unknown ground near, times the share of Red's forces Blue cannot place: none when every Red unit is placed (0), the most when none is (1), and nothing with fogUncertainty off (and fogAggression 1). Only the move's own fog risk counts it: the expected loss that supply, trucks, regrouping, targeting and the coordinator use stays at what Blue has seen (none). (With none of Red's kinds seen, it fears what Blue fields.) Priced at the unit's worth: the tank's risk far above the conscript's.", "Human");
        makeFog("game-log-replay", "gameLogReplay", (a, o) => { Put("Grunt", a, -3, 0); Put("Tank", a, -4, 2); Put("Grunt", o, 3, 0); Put("Tank", o, 4, -2); },
             "Replaying a recorded game: three rounds of the AI against itself are recorded (game.log and a board for every turn), then replayed. The replay has a turn for every board saved, each with its round, its side and what the log says happened in it; it shows each board exactly as it was at the start of that turn, forward and back; nobody plays (every AI off, clicks on the board and the recording ignored); it can be seen as either side sees it (that side's fog) or with everything shown; closing it puts the fog as it was.", "Full");
        makeFog("fog-ai-game", "fogGame", (a, o) => {
                 Put("Tank", a, -4, 1); Put("Grunt", a, -3, -1); Put("Grunt", a, -4, 3); Put("Recon", a, -3, 2); Put("Artillery", a, -5, 2); Put("Supplies", a, -5, 1);
                 Put("Tank", o, 4, -1); Put("Grunt", o, 3, 1); Put("Grunt", o, 4, -3); Put("Recon", o, 3, -2); Put("Artillery", o, 5, -2); Put("Supplies", o, 5, -1);
                 for (int x = -1; x <= 1; x++) for (int y = -3; y <= 3; y++) { HexTile t = Rig.At(x, y); if (t != null && (x + y) % 2 == 0) t.terrain = T("Forest"); } },
             "Both sides the AI, both playing by the fog, eight rounds: after every action the board is whole (every unit on its tile and only there, nothing left concealed), attacks only ever at units their side sees, and any unit run into stops short and is fired on.", "Full");
        make("attrition-rule-supply-chain-line", "attritionSupplyChainLine", (a, o) => {
                 Rig.At(-6, 0).building.supplyRange = 3;
                 Put("Supplies", a, -3, 0); Put("Supplies", a, 0, 0); Put("Grunt", a, 3, 0);
                 Put("Supplies", a, 3, 3); Put("Supplies", a, 4, 3); Put("APC", a, 2, -6); Put("APC", a, 0, -6);
                 Put("Grunt", o, -4, 4); Put("Grunt", o, -3, 4); Put("Grunt", o, -2, 4); },
             "Supply through a chain of two supply units (the HQ feeds A, A feeds B, B feeds a grunt): both are linked, and the line drawn from the grunt runs through B, then A, to the HQ, each once (it used to turn back from A to B: two hubs drawn feeding each other). Two supply units next to each other far from any supply are not linked, supply nothing (not even each other), and have no line. And for every placement of two supply units near the HQ, every one-tile move of one of them previewed, every placement with both aboard transports, and with both aboard one transport (and its moves previewed), the line from each linked one is drawn and ends at the HQ, passing each once, tile to tile and never over an enemy (from a transport it follows the way the transport would drive to supplied ground). Selecting A also shows what depends on it: B, with its line back to A; nothing depends on B.");
        make("ui-selection-overlays", "uiOverlays", (a, o) => {
                 Put("Artillery", a, -3, 0); Put("Grunt", a, -1, 0); Put("Grunt", o, 0, 0); Put("Tank", o, 2, -2);
                 Rig.PlaceBuilding("City", null, -2, 3); Put("Grunt", a, -2, 3); Rig.PlaceBuilding("City", o, 3, 3); Put("Grunt", a, 3, 3); },
             "The board's overlays. Selecting Blue's artillery marks only the enemies it can fire on now (not its whole range, not the Blue grunt in range, not empty tiles). Clicking an enemy tank shows its move and reach at half opacity, the move tiles at half its colour's saturation (the reach in its full colour). The tile panel's capture line: a neutral city a Blue grunt is taking shows \"Capturing: Blue 6/10\"; a Red city it is bringing down shows \"Held: 4/10\" in Red; a city at full points shows none. The line at the top says whose turn it is, and for an AI whether it is thinking or moving; when the game ends the banner and the line say who won (\"Red wins\"), or \"Draw\".");
        make("rule-rotor-supply", "rotorSupply", (a, o) => {
                 Rig.At(-6, 0).building.supplyRange = 3;
                 Put("Fighter", a, -4, 0); Put("Fighter", a, 2, 3); Put("Fighter", a, -5, 1); },
             "Helicopters (rotor) are resupplied on any tile in their side's supply, as well as at airports: one in supply at the start of Blue's turn is refuelled, rearmed and heals a strength; one out of supply burns a turn's fuel and does not starve; a jet in supply still burns fuel (it needs an airport). The AI counts every supplied tile as somewhere a helicopter can go home to, and a helicopter there as healing.");
        make("rule-air-not-ambushed", "airNotAmbushed", (a, o) => {
                 Put("Fighter", a, -2, 0); Put("Tank", o, -1, 0); Put("Fighter", a, 2, 0); Put("Grunt", o, 3, 0); Put("Fighter", a, 0, 3); Put("Fighter", o, 1, 3);
                 Put("Tank", a, 0, -3); Put("Tank", o, 1, -3);
                 Rig.At(3, 0).terrain = T("Forest"); },
             "Aircraft are never ambushed by land or sea units: a helicopter attacking a tank on grassland, or a veteran grunt in a forest, fires first; land units still ambush each other as before (a veteran tank on grassland fires first against a tank), and an aircraft can still be fired on first by a veteran aircraft.");
        make("editor-board-grows", "boardGrows", (a, o) => { Put("Grunt", a, -3, 0); },
             "Loading a map: the board is made the size the map was made on (the file's width, never too small for its farthest tile), bigger or smaller, rather than leaving outer tiles off or a ring of water around it. The tiles already there stay where they are, with what is on them; every tile has its neighbours again (an old edge tile now six); the cached rings of tiles are worked out again; a board already that size is left alone; made smaller, the tiles beyond it go and no tile keeps a neighbour that has gone.");
        make("editor-delete-safely", "deleteSafely", (a, o) => { Put("Grunt", a, -3, 0); Put("APC", a, -2, 1); Put("Grunt", a, -1, 1); Rig.PlaceBuilding("City", a, 2, 2); },
             "Deleting a unit or building in the editor (its object destroyed in the Hierarchy, not killed in play): it leaves every list and its tile at once, so nothing reads it afterwards (saving the board threw MissingReferenceException). A transport deleted with cargo aboard leaves the cargo unattached.");
        make("editor-load-building-ground", "editorLoad", (a, o) => { Rig.PlaceBuilding("City", null, 0, 0); Put("Grunt", a, -3, 0); },
             "Saving and loading the board in Unity: every tile written as the editor and the save menu write it (terrain as shown, the ground under a building) and read back with only the terrains in the scene's LevelEditor list (the ground every building lays, Urban, once BuildingGround, found whether the list has it or not) comes back as it was: building tiles keep that ground and the ground under, nothing fails; a save naming it BuildingGround gets Urban. A tile naming a terrain the game has never had loads as the ground under it, or grassland.");
        makeBuy("rule-purchases-off", "purchasesOff", (a, o) => { Put("Grunt", o, 4, 0); },
             "A scenario with purchases off: Blue's HQ offers nothing to build and refuses a Grunt even with the money.", "off", null);
        makeBuy("rule-allowed-units", "allowedUnits", (a, o) => { Put("Grunt", o, 4, 0); },
             "A scenario allowing only Grunts: Blue's HQ offers only the Grunt; it builds one, and refuses a Tank even with the money.", "on", "Grunt");
        makeBuy("ai-buys-units", "aiBuys", (a, o) => { Put("Tank", o, 3, 0); Put("Grunt", o, 3, 1); Put("Grunt", a, -4, 0); },
             "An AI with money (10,000), purchases on, Grunt and Tank allowed: at the end of its turn it buys an allowed unit at its HQ (one: the HQ has one tile to build on), never a unit the scenario does not allow.", "on", "Grunt,Tank");
        make("attrition-rule-transport-load", "transportLoad", (a, o) => {
                 water(-1, 7, -7, 7);
                 Rig.PlaceBuilding("NavalYard", a, -2, 0);
                 Put("APC", a, -4, 0); Put("Grunt", a, -4, 1); Put("Grunt", a, -5, 1); Put("Supplies", a, -5, 0); Put("Tank", a, -3, -1); Put("Fighter", a, -6, 0);
                 Put("Hauler", a, 3, 0); Put("Grunt", o, 6, -3); },
             "What fits where. An APC (2 slots, carries Boots) takes two grunts (weight 1 each) and then has no room for a supply unit; emptied, it takes the supply unit. Never a tank (Treads) or a fighter. A grunt on the shore boards a hauler on the open water beside it (any coastal tile), as it does at the naval yard. A loaded APC can itself board the hauler (it takes its own weight of the hauler's slots), and everything aboard goes where the ship goes.");
        make("attrition-rule-suppression", "suppression", (a, o) => {
                 Put("Artillery", a, -4, 0); Put("Artillery", a, -4, 2); Put("Grunt", a, -1, 1); Put("Tank", a, -3, -2);
                 Put("Tank", o, 0, 0); Put("Tank", o, 0, 3); Put("Tank", o, -1, -2); Put("Grunt", o, 2, -2); },
             "Indirect fire suppresses instead of driving back. Artillery hits a tank hard enough that it would have retreated: it stays where it is, takes the same damage, and half its unsuppressed pips (rounded up) are suppressed. Attacked again that turn, it fires back with its strength less the suppressed pips (none at all when every pip is suppressed). A second artillery hit suppresses half of what is left. A hit too light to push it (a veteran) suppresses nothing. Its own turn starting, every pip comes back. Direct fire still drives back. Indirect fire is any attacker with a minimum range (it cannot fire on the next tile), not one that reaches far: a tank reaching two tiles still drives back.");
        make("attrition-rule-recon-transport", "reconTransport", (a, o) => { Put("Recon", a, -3, 0); Put("Grunt", a, -2, 0); Put("Grunt", o, 5, 0); },
             "The recon truck (1 slot, carries Boots) takes a grunt, drives, and puts it down beside it, as the APC does.");
        make("attrition-rule-alarms", "alarms", (a, o) => { Put("Grunt", a, -5, 0); Put("Grunt", a, 5, 0); Put("Tank", a, -4, 1); Rig.PlaceAirfield(a, -3, 2); Put("Fighter", a, -3, 3); Put("Fighter", a, -5, 2); Put("AirTanker", a, -2, 0); Put("Grunt", o, 6, -3); },
             "The alarm icon's conditions (fuel and ammo alarms from a third of a full load down): a grunt with no supplies out of supply is starving, one with none inside supply is not (it is fed next turn); a tank with no fuel is out of fuel, with no ammunition out of ammo. A fighter with no supplies over our own ground in supply is starving (the network does not feed aircraft), one on our airport's runway is not; an air tanker with no supplies away from base is starving too, like any aircraft.");
        make("attrition-rule-portrait-layout", "portraitLayout", (a, o) => { Put("Grunt", a, -3, 0); Put("Grunt", o, 4, 0); },
             "The UI pictures of units and tiles (SpritePortrait): every sprite of the unit is placed as on the board and the whole fitted into the box. A 2x1 hull, a 1x1 turret turned 90 degrees above its right half and a mirrored flag, fitted into 100x100 with a 4 px margin: scale 46, centred, the turret at 90 degrees, the flag flipped.");
        make("rule-sandbox-turns", "sandboxTurns", (a, o) => { Put("Grunt", o, 4, 0); },
             "Sandbox: turns go Blue, Red, Blue, Red even if a side is marked out (a file saved after it was knocked out), and the day goes up once per round.");
        make("rule-destructible-terrain", "destructible", (a, o) => {
                 Put("Grunt", a, 0, 0); Put("Grunt", o, 1, 0);
                 Rig.At(3, -2).terrain = T("Bridge"); Rig.At(3, -2).road = true; Put("Grunt", a, 3, -2);
                 Rig.PlaceBuilding("City", a, -3, 2); Put("Grunt", a, -3, 2); Put("Supplies", a, -4, 3); },
             "Destructible terrain: health split into (damage pictures + 1) equal parts (10 health, one picture: 10-6 normal, 5-1 damaged; two: 10-7, 6-4, 3-1). Each attack on a unit damages its tile by 1. A Grassland tile (health 10) hit ten times becomes Badlands, with what it was remembered; UndoDestruction puts the grass back one short of destroyed and Repair heals it. A bridge with a grunt on it, hit until it falls into what its data says (TerrainData.destroyedInto, down to a water terrain that takes no damage), leaves no road, and the grunt is lost. A City (health 10) with a grunt on it takes the hits instead of its ground; destroyed, it becomes neutral and earns, supplies and builds nothing; a ruin cannot be captured, so the grunt on it takes it back only once it is repaired (from 0: two turns), and it earns again. Water takes no damage.");
        make("rule-line-of-fire", "lineOfFire", (a, o) => { Rig.At(0, 0).terrain = T("Hills"); Put("Tank", a, -1, 0); Put("Artillery", a, -1, 2); Put("Grunt", o, 1, 0); Put("Grunt", o, 6, -3); },
             "Direct fire does not go through higher ground (CombatMath.LineOfFire): a tank given range 2 cannot fire at a grunt two tiles off across a hill — not the player's target list, not the AI's appraisal, not its executor — but can over grassland, or from a hill of its own height. Artillery (fires without moving) lobs over the hill.");
        make("rule-board-through-friend", "boardThroughFriend", (a, o) => {
                 Put("APC", a, 0, 0);
                 foreach (int[] nb in new[] { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 }, new[] { 1, -1 }, new[] { -1, 1 } }) Put("Tank", a, nb[0], nb[1]);
                 Put("Grunt", a, -2, 1); Put("Grunt", o, 6, -3); },
             "An APC with our tanks on every tile around it, and a grunt two tiles off: it walks through a tank's tile and boards from there (the tile a friend holds is passed through, not stopped on); the tank stays where it is. The AI sees the same: boarding from a friend's tile is open to it (Transporting.CanBoardFromTile).");
        make("rule-runway-no-capture", "runwayNoCapture", (a, o) => { Rig.PlaceAirfield(a, -3, 2); Put("Grunt", o, -3, 3); },
             "A Red grunt on a runway beside a Blue airport does not capture the airport: the runway is a building of its own.");
        make("attrition-rule-supply-boats", "attritionBoats", (a, o) => {
                 water(-2, 1, -7, 7);
                 Rig.PlaceBuilding("NavalYard", a, -3, 0);
                 foreach (int x in new[] { -1, 1 }) { Unit h = Put("Hauler", a, x, 0); Unit su = Put("Supplies", a, -5, x + 2); Aboard(h, su); }
                 Put("Grunt", a, 2, 0); Put("Grunt", o, 4, 3); },
             "A strait four tiles wide: the HQ's supply stops at the water. Two haulers, each with a supply unit aboard, link from beside the naval yard (in supply) to each other, and the far one supplies the island it borders. With the far hauler moved out to sea, the island grunt is not supplied.");
        make("attrition-rule-ship-mends", "shipMends", (a, o) => { water(-2, 2, -7, 7); Unit g = Put("Gunboat", a, 0, 0); g.Strength = 3; g.supplies = 5; Unit dry = Put("Gunboat", a, 0, 4); dry.Strength = 3; dry.supplies = 0; Put("Grunt", o, 5, 0); },
             "Ships mend themselves at sea while they have supplies: a Gunboat at strength 3 with 5 supplies is at 4 (4 supplies) after a turn, 5 after two; one with no supplies left loses a strength instead.");
        make("attrition-rule-supply-ship-tends", "shipTender", (a, o) => { water(-2, 2, -7, 7); Unit sb = Put("Hauler", a, 0, 0); sb.supplies = 5; Unit g = Put("Gunboat", a, 1, 0); g.fuel = 10; g.supplies = 1; Unit far = Put("Gunboat", a, 0, 5); far.fuel = 10; Unit sb2 = Put("Hauler", a, -1, 0); sb2.fuel = 10; Put("Grunt", o, 5, 0); },
             "A supply ship refuels and restocks the ships beside it at the start of the turn, as an air tanker does aircraft: the Gunboat beside it (fuel 10, 1 supply) is full after a turn; one away from it is not; a supply ship beside it is not tended by it (a tender is never tended by another).");
        make("attrition-rule-ship-carried-supply", "shipCarriedSupply", (a, o) => {
                 water(-4, 5, -7, 7);
                 Put("Hauler", a, -1, 0); Put("Hauler", a, 3, 0); Put("Hauler", a, 5, 0);
                 Put("Supplies", a, -6, 2); Put("Supplies", a, -6, 3); Put("Supplies", a, -6, 4); Put("Supplies", a, -5, 4);
                 Put("Transport Helo", a, -5, 3);
                 Put("Grunt", a, 6, -1); Put("Grunt", o, 7, -2); },
             "Convoys: a ship with a supply unit aboard is a link if within one move (its own path) of supplied ground or of another linked carrier, to any length, and gives supply to land only beside it. Ten tiles of sea between Blue's shore and an island, ships moving 3: TS A within a move of Blue's shore, TS B within a move of A only, TS C within a move of B only and beside the island. C is linked through B through A, the Blue grunt on the island is supplied, A and B out at sea give nothing, and the supply line runs from the grunt through C, B and A to the HQ. Without A, B and C are cut off. A helicopter with supplies aboard in A's place over the water (linked by Blue's shore, giving nothing over water) links B in turn: a ship links through an aircraft.");
        make("attrition-rule-carrier", "carrierRule", (a, o) => {
                 water(-4, 5, -7, 7);
                 Rig.At(-5, 0).terrain = T("Grassland"); Rig.PlaceBuilding("NavalYard", a, -5, 0);
                 Put("Carrier", a, 0, 0); Put("Carrier", a, 0, 4); Put("Fighter", a, 1, 0); Put("Bomber", a, -1, 0); Put("Grunt", a, -6, 2);
                 Put("Fighter", a, 1, 4); Put("Grunt", o, 6, 0); },
             "The carrier (a hauler variant for aircraft): the naval yard sells it; it takes fighters and bombers (3 weight: a fighter and a bomber), not grunts; aircraft board it from beside it at sea; at the start of Blue's turn one aboard a carrier with supplies is refuelled, rearmed and mended, and one aboard a carrier out of supplies is not (it starves, as the cargo of any ship out of supplies does).");
        make("attrition-rule-ship-supply-upkeep", "shipSupplyUpkeep", (a, o) => {
                 water(-4, 5, -7, 7);
                 for (int x = 0; x <= 4; x++) Rig.At(x, -1).terrain = T("Grassland");
                 Put("Hauler", a, -1, 0); Put("APC", a, -6, 1); Put("Supplies", a, -6, 2); Put("Grunt", a, -6, 3);
                 Put("Gunboat", a, 0, 0);
                 Put("Grunt", o, 7, -6); },
             "Supply at sea, and what it gives: a hauler within a move of Blue's shore, beside a strip of island, carrying an APC with a supply unit inside it and a weakened grunt. (1) The supply two transports deep links and supplies the island, as supply aboard the hauler itself would (ship rules). (2) The grunt aboard, the hauler having supplies, is resupplied at the start of Blue's turn as on supplied ground: healed a strength, rearmed, refuelled. (3) The hauler itself only mends from its own supplies (+1): the water its own supply covers is not supplied ground for it. (4) When the hauler sails away, the water and the island it supplied are no longer supplied.");
        make("attrition-rule-ship-line-on-water", "shipLineOnWater", (a, o) => {
                 water(-4, 5, -7, 7);
                 for (int x = 0; x <= 4; x++) Rig.At(x, -1).terrain = T("Grassland");
                 Put("Hauler", a, -1, 0); Put("Hauler", a, 3, 0);
                 Put("Supplies", a, -6, 2); Put("Supplies", a, -6, 3);
                 Put("Grunt", o, 7, -6); },
             "Ten tiles of sea off Blue's shore with a strip of island (x 0..4) in it; TS A (supplies aboard) within a move of Blue's shore and beside the island, TS B (supplies aboard) beside the island too, within a move of A only. B links through A (a ship tries its convoy first), and every line between them keeps to the water: B's line to the HQ, and the faint line of B hanging on A when A is selected. A selected at sea, or moved out to sea in a preview, still shows its line to Blue's shore.");
        make("ai-sea-lift", "seaLift", (a, o) => {
                 for (int x = -1; x <= 2; x++) for (int y = -7; y <= 7; y++) { HexTile w = Rig.At(x, y); if (w != null) w.terrain = T("Water"); }
                 Rig.PlaceBuilding("NavalYard", a, -2, 0);
                 Put("Grunt", a, -4, 1); Put("Grunt", a, -4, 2); Put("Grunt", a, -5, 2);
                 Put("Grunt", o, 5, -2); },
             "The enemy HQ across a channel no land unit can cross: Blue (the AI, with a naval yard on its shore and gold) builds a transport ship and ferries infantry over, landing on Red's shore, and takes Red's HQ (Red, a human player here, does nothing).");
        make("attrition-ai-sea-supply", "attritionSeaSupply", (a, o) => {
                 for (int x = -1; x <= 2; x++) for (int y = -7; y <= 7; y++) { HexTile w = Rig.At(x, y); if (w != null) w.terrain = T("Water"); }
                 Rig.PlaceBuilding("NavalYard", a, -2, 0);
                 Put("Hauler", a, -2, 0); Put("Supplies", a, -3, 1);
                 Put("Grunt", a, 4, 1); Put("Grunt", a, 4, 2);
                 Put("Grunt", o, 6, -5); },
             "A supply convoy: two Blue grunts already across a channel, out of supply; on Blue's shore an empty transport ship at the naval yard and a supply unit beside it. The AI puts the supply unit aboard and takes the ship over to lie off the far shore by the grunts, where its supply is linked (within a move of Blue's supplied shore) and reaches them: within three Blue turns both grunts are in supply.");
        make("ai-no-ghost-from-nested-transport", "nestedGhost", (a, o) => {
                 water(-3, 2, -7, 7);
                 Unit d = Put("Destroyer", a, 0, 0); Unit v = Put("Transport Helo", a, 4, 0); Unit su = Put("Supplies", a, 6, 0);
                 Aboard(v, su); Aboard(d, v);
                 Put("Grunt", a, -5, 2); Put("Grunt", o, 6, -2); Put("Grunt", o, 5, 2); },
             "From a game (Mekong Delta): a transport VTOL carrying a supply unit had landed on a destroyer, and the AI moved the VTOL as the supply unit's ride while it was still aboard. Each such move left the tile it went to naming it as the occupier while it stayed aboard (a ghost that blocked paths, supply and could be attacked). Over Blue's turns no unit aboard is moved, and every tile's occupier stands on it and is not aboard anything.");
        make("ai-carrier-keeps-unrefuelled", "carrierKeeps", (a, o) => {
                 water(-4, 4, -7, 7);
                 Put("Carrier", a, 1, 0); Put("Bomber", a, 2, 0);
                 Put("Grunt", o, 6, -2); Put("Grunt", o, 5, 1); },
             "A bomber that landed on a carrier this turn (it has not had the start of a turn aboard, when it is refuelled) is not put back out this turn, though enemies are in its reach: an order to put it down becomes the carrier's move (or a hold), and over Blue's turn it stays aboard.");
        make("ai-carrier", "carrierAI", (a, o) => {
                 water(-4, 4, -7, 7);
                 Put("Carrier", a, -2, 2); Put("Bomber", a, 2, -3);
                 Put("Grunt", o, 6, -2); Put("Grunt", o, 5, 1); },
             "No airfield: Blue's bomber, low on fuel over the sea, and a stocked carrier. The bomber lands on the carrier (it would ditch otherwise); the carrier holds for it and does not put it back out the turn it landed; at the start of Blue's next turn it is refuelled aboard; after that it flies off again to strike.");
        make("ai-ship-without-base", "seaMeeting", (a, o) => {
                 for (int x = -1; x <= 2; x++) for (int y = -7; y <= 7; y++) { HexTile w = Rig.At(x, y); if (w != null) w.terrain = T("Water"); }
                 Put("Hauler", a, 1, -6); Put("Grunt", a, -5, 3); Put("Grunt", a, -4, 3);
                 Put("Grunt", o, 5, -2); },
             "As ai-sea-meeting, but Blue has no naval yard and no supply ship: nowhere to refuel. The hauler still acts (it does not hold for want of a base it cannot get back to): it meets the grunts and takes them across.");
        make("ai-sea-meeting", "seaMeeting", (a, o) => {
                 for (int x = -1; x <= 2; x++) for (int y = -7; y <= 7; y++) { HexTile w = Rig.At(x, y); if (w != null) w.terrain = T("Water"); }
                 Rig.At(-1, -5).terrain = T("Grassland"); Rig.PlaceBuilding("NavalYard", a, -1, -5);
                 Put("Hauler", a, 1, -6); Put("Grunt", a, -5, 3); Put("Grunt", a, -4, 3);
                 Put("Grunt", o, 5, -2); },
             "An empty hauler beside Blue's naval yard at the north end of the channel, far from Blue's two grunts, Red's HQ across. The sea-travel estimate counts the trip from the grunts' own shore. The hauler sails south to meet the grunts off the shore nearest them (not to a harbor), they walk to that shore and board from it, and it takes them over: a grunt lands on Red's side.");
        make("ai-sea-convoy", "seaConvoy", (a, o) => {
                 for (int x = -1; x <= 2; x++) for (int y = -7; y <= 7; y++) { HexTile w = Rig.At(x, y); if (w != null) w.terrain = T("Water"); }
                 Rig.PlaceBuilding("NavalYard", a, -2, 0);
                 Put("Hauler", a, -2, 0); Put("Supplies", a, -3, 1); Put("Tank", a, -3, 0); Put("Grunt", a, -2, 1);
                 Put("Grunt", o, 6, -5); },
             "One ship takes troops and their supply together: a transport ship (4 slots) at Blue's naval yard with a grunt (1) aboard, a supply unit (weight 1) and a tank (2) beside it, Red's HQ across the channel and nothing of Blue's supply over there. Loaded, the ship acts first in Blue's turn, but it does not sail while either of them can still get aboard: in Blue's first turn all three get in; it sails full, and the supply aboard is linked off Red's shore, where the troops are put down (the troops' landing ground beside the ship is supplied; with unifiedLogistics the ship then holds there as that shore's supply link).");
        make("ai-turn-order", "turnOrder", (a, o) => {
                 Put("Artillery", a, -2, 0); Put("APC", a, -4, 2); Put("Grunt", a, -5, 2); Put("Grunt", a, -5, 3);
                 Put("APC", a, -4, -2); Put("Grunt", a, -3, -2); Put("Grunt", a, -4, 0);
                 Put("Grunt", o, 1, 0); },
             "The AI's turn order (AIProfile.turnOrderSteps): artillery with an enemy in range first; then a full transport (an APC with two grunts aboard); then the units on foot (its riders, and the rest); then a transport with room (an empty APC with a grunt beside it), after the grunt beside it has had the chance to get in.");
        make("ai-ship-refit", "shipRefit", (a, o) => {
                 for (int x = -1; x <= 5; x++) for (int y = -7; y <= 7; y++) { HexTile w = Rig.At(x, y); if (w != null) w.terrain = T("Water"); }
                 Rig.PlaceBuilding("NavalYard", a, -2, 0);
                 Put("Gunboat", a, 3, -2); Put("Gunboat", a, 4, 3);
                 Put("Grunt", o, 6, -5); },
             "Ships go back for what they run out of: a gunboat out of ammunition five tiles from Blue's naval yard heads for the yard (only a yard rearms) and is rearmed there; a gunboat with one turn of supplies left, far from the yard, goes back to it too (nothing restocks ships at sea), and is restocked there without losing strength.");
        make("rule-artillery-after-ride", "artilleryRide", (a, o) => {
                 water(-3, -1, -7, 7);
                 Put("Artillery", a, -4, 0); Put("Grunt", a, -4, 1); Put("Hauler", a, -3, 0); Put("Grunt", o, 3, 0); },
             "Move or attack: artillery carried somewhere this turn has had its move. Put down from a hauler that has not moved it can still fire; put down after the hauler sailed it, it cannot fire until its next turn (a grunt riding with it, which moves and attacks, still can).");
        make("rule-board-reach", "boardReach", (a, o) => {
                 water(0, 7, -7, 7);
                 Put("Grunt", a, -4, 0); Put("APC", a, -1, 0); Put("APC", a, -1, 1); Put("Hauler", a, 0, -2); Put("Grunt", o, 6, -3); },
             "Boarding reaches as far as a move: a grunt (3 moves, plains) gets into an APC 3 tiles off on land it could walk onto (the step in is the third), and not one 4 off; a hauler on the water, which it cannot enter, it boards from the shore beside it — 4 off, its 3 moves taking it to the shore and the step in free.");
        make("rule-ignores-cover", "ignoresCover", (a, o) => { Rig.At(1, 0).terrain = T("Forest"); Put("Tank", a, 0, 0); Put("Grunt", o, 1, 0); Put("Grunt", o, 6, -3); },
             "An attacker that ignores cover (Unit.ignoresCover: napalm, flamethrowers): a grunt in a forest ambushes a tank attacking it (fires first) and takes less from it for the cover; with the tank set to ignore cover, the grunt cannot fire first and takes full damage, as on open ground. The AI's forecast (CombatMath.Resolve) agrees.");
        make("ui-next-unit", "nextUnit", (a, o) => { Put("Grunt", a, 0, 0); Put("Grunt", a, 2, 0); Put("Tank", a, 0, 2); Put("Grunt", o, 5, -2); },
             "The HUD's Next unit (GameController.SelectNextIdle): selects Blue's idle units in turn, as a click on the unit's tile does (its tile SELECTED, its moves shown), wrapping round; a unit that has acted is skipped; nothing on an AI's turn.");
        make("rule-harbor-open", "harborOpen", (a, o) => { water(-1, 2, -7, 7); Rig.PlaceBuilding("NavalYard", a, -2, 0); Rig.At(-3, 4).terrain = T("Bridge"); Put("Grunt", a, -3, 1); Put("Grunt", a, -4, 4); Put("Gunboat", a, 0, 0); Put("Grunt", o, 5, 0); },
             "A harbor (a naval yard's tile, on bridge ground) is open from every side: a grunt steps onto it from grassland with no road, a ship from the water, and supply reaches it from the land around it. A plain bridge keeps the road rule: the same grunt step onto one is refused.");
        make("attrition-rule-transport", "attritionTransport", (a, o) => { Put("APC", a, -3, 0); Put("Grunt", a, -2, 0); Put("Grunt", o, 5, 0); },
             "A Grunt boards the APC beside it: off the board, riding the APC's tile and moving with it, parented to the APC's slot; unloaded beside it, it is back on the board and unparented. Getting off costs nothing: having boarded this turn it is still spent; aboard at the start of a turn, it is still free to act once unloaded.");
        make("attrition-rule-cargo-supply", "attritionCargoSupply", (a, o) => { water(-1, 7, -7, 7); Put("APC", a, -4, 0); Unit g = Put("Grunt", a, -4, 1); g.Strength = 4;
                 Put("Hauler", a, -1, 1); Unit m = Put("Marine", a, -2, 1); m.supplies = 2; m.Strength = 4; Put("Grunt", o, 4, 3); },
             "Cargo gets what its transport gets: aboard the APC inside supply a grunt is healed; a marine (strength 4, 2 supplies) aboard a hauler at sea is resupplied as on supplied ground while the ship has supplies of its own (healed a strength, restocked), and starves once the ship's are gone.");
        n += ExportScriptChecks(make);
        make("attrition-rule-amphibious", "attritionAmphibious", (a, o) => { water(-1, 2, -7, 7); Put("Marine", a, -2, 1); Put("Grunt", a, -2, 2); Put("Grunt", o, 5, 0); },
             "A Marine can enter water beside land but not open water away from it; a Grunt cannot enter water.");
        return n;
    }

    static bool RuleCheck(ScenarioFile.Loaded l, out string detail) {
        if (l.meta.Get("check", "").StartsWith("script")) return ScriptCheck(l, out detail);   // scenario triggers (ScenarioTriggerChecks.cs)
        GameController gc = Rig.gc;
        Player a = l.ai, o = l.other;
        Building at00 = Rig.At(0, 0).building;
        Func<Building, string> show = b => b.name + " " + (b.owner != null ? b.owner.personal_name : "neutral" + (b.capturingPlayer != null ? "/" + b.capturingPlayer.personal_name : "")) + " " + b.capturePoints + "/" + b.CaptureThreshold;
        Action endTurn = () => gc.EndTurn();
        switch (l.meta.Get("check", "")) {
            case "captureNeutral": {
                endTurn(); string first = show(at00);
                bool ok1 = at00.owner == null && at00.capturingPlayer == a && at00.capturePoints == 6;
                endTurn(); endTurn();
                bool ok2 = at00.owner == a && at00.capturePoints == 0 && at00.capturingPlayer == null;
                detail = "after one Blue turn " + first + ", after two " + show(at00);
                return ok1 && ok2;
            }
            case "captureFromZero": {
                endTurn(); endTurn(); string first = show(at00);
                bool ok1 = at00.owner == a && at00.capturingPlayer == o && at00.capturePoints == 6;
                endTurn(); endTurn();
                detail = "after one Red turn " + first + ", after two " + show(at00);
                return ok1 && at00.owner == o && at00.capturingPlayer == null && at00.capturePoints == 0;
            }
            case "attackNoCapture": {
                endTurn();
                Unit red = Rig.At(0, 0).occupier;
                red.OrderAttack(Rig.At(1, 0).occupier);
                endTurn();
                detail = show(at00) + " after the Red grunt attacked from it";
                return at00.owner == a && at00.capturePoints == 0 && at00.capturingPlayer == null;
            }
            case "captureOnAction": {
                Unit g = Rig.At(2, 0).occupier;
                gc.activeUnit = g;
                g.TeleportTo(at00.tile, 2);
                g.FinishActive();
                string moved = show(at00);
                bool ok1 = at00.owner == null && at00.capturingPlayer == a && at00.capturePoints == 6;
                endTurn(); string ended = show(at00);
                bool ok2 = at00.owner == null && at00.capturingPlayer == a && at00.capturePoints == 6;
                endTurn(); endTurn();
                detail = "after its move " + moved + ", after Blue's turn " + ended + ", after its next turn " + show(at00);
                return ok1 && ok2 && at00.owner == a;
            }
            case "decay": {
                endTurn(); endTurn();
                bool ok = at00.owner == null && at00.capturePoints == 0 && at00.capturingPlayer == null;
                Building hq = GameController.HQOf(a);
                int before = hq.capturePoints;
                endTurn(); endTurn();
                detail = show(at00) + "; Blue HQ " + before + " -> " + hq.capturePoints;
                return ok && hq.capturePoints == before;
            }
            case "hqStandard": {
                Building hq = GameController.HQOf(a);
                endTurn(); endTurn(); string mid = show(hq);
                endTurn(); endTurn();
                detail = "after one Red turn " + mid + ", after two: " + show(hq) + "; game over " + gc.gameOver + ", winner " + (gc.winner != null ? gc.winner.personal_name : "none");
                return gc.gameOver && gc.winner == o;
            }
            case "attritionHQ": {
                Building hq = GameController.HQOf(a);
                for (int i = 0; i < 4; i++) endTurn();
                detail = show(hq) + ", game over " + gc.gameOver;
                return hq.owner == a && hq.capturePoints == 0 && hq.capturingPlayer == null && !gc.gameOver;
            }
            case "battery": {
                Building fob = Rig.At(-4, 0).building;
                for (int i = 0; i < 6; i++) endTurn();   // three Blue turns connected
                int charged = fob.supplies;
                // Cut off: the forward base (and the grunt beside it) moved far from the HQ.
                Unit g = Rig.At(-3, 1).occupier;
                fob.Occupy(Rig.At(5, -4));
                g.tile.occupier = null; g.tile = Rig.At(4, -3); g.tile.occupier = g;
                List<string> trace = new List<string>();
                int suppliedTurns = 0;
                for (int i = 0; i < 12 && !gc.gameOver; i++) {
                    endTurn();
                    if (gc.activePlayer != a) continue;
                    bool supplied = g.tile.isSupplied(a);
                    if (supplied) suppliedTurns++;
                    trace.Add(fob.supplies + (supplied ? "s" : "-"));
                }
                detail = "store after three connected turns " + charged + " (max " + fob.suppliesMax + ", charge " + fob.chargePerTurn + ", supplies area " + fob.producesSupply + ")" + "; cut off, each Blue turn (store, s = grunt supplied): " + string.Join(" ", trace.ToArray());
                return charged == 3 && suppliedTurns == 3 && fob.supplies == 0;
            }
            case "production": {
                a.money = 3000;
                Building hq = GameController.HQOf(a);
                Unit grunt = null;
                foreach (Unit u in hq.buildable) if (u.name == "Grunt") grunt = u;
                Unit made = Rig.bc.Produce(hq, grunt);
                int price = grunt != null ? grunt.cost : 0;
                detail = made == null ? "nothing built" : "built " + made.name + " (cost " + price + ") on " + Pos(made.tile) + ", active " + made.active + ", money " + a.money;
                return made != null && made.tile == hq.tile && !made.active && a.money == 3000 - price;
            }
            case "service":
                detail = "SKIP: obsolete file (aircraft no longer use supplies); replaced by attrition-rule-air-*; yours to delete";
                return true;
            case "attritionAirFuel": {
                Unit f = Rig.At(-4, 1).occupier;
                int ditched = 0; Action<Unit> hook = u => ditched++;
                Unit.Ditched += hook;
                try {
                    endTurn(); endTurn();
                    string first = "fuel " + f.fuel + ", strength " + f.Strength + ", supplies " + f.supplies;
                    bool ok1 = f.fuel == 2 && f.Strength == 4 && f.tile != null && f.supplies == 2;
                    endTurn(); endTurn();
                    detail = "after a turn " + first + "; after two " + (f.tile == null ? "gone, ditched " + ditched : "still flying, fuel " + f.fuel);
                    return ok1 && f.tile == null && ditched == 1;
                } finally { Unit.Ditched -= hook; }
            }
            case "attritionAirBase": {
                Unit f = Rig.At(-3, 3).occupier;
                endTurn(); endTurn();
                detail = "on the runway (" + (Rig.At(-3, 3).building != null && Rig.At(-3, 3).building.name.StartsWith("Runway") ? "runway tile" : "not a runway") + "): fuel " + f.fuel + ", strength " + f.Strength;
                return f.fuel == f.fuelMax && f.Strength == 5;
            }
            case "attritionSupplyAboard": {
                Unit apc = Rig.At(1, 0).occupier, sup = Rig.At(2, 0).occupier;
                bool loaded = Transporting.Load(apc, sup);
                SupplyPoint hub = sup.GetComponent<SupplyPoint>();
                SupplyNetwork.Result stat = SupplyNetwork.SolveStatic(l.ai);
                SupplyNetwork.Result net = SupplyNetwork.Solve(l.ai, false);
                bool linked = net.connectedHubs.Contains(hub);
                // Its own spread: nothing supplied farther from the APC than its range that the HQ does not supply.
                int far = 0;
                foreach (KeyValuePair<HexTile, int> kv in net.depth) if (kv.Value > 0 && !stat.IsSupplied(kv.Key)) far = Math.Max(far, kv.Key.Distance(apc.tile));
                int gap = 99;
                foreach (KeyValuePair<HexTile, int> kv in stat.depth) if (kv.Value > 0) gap = Math.Min(gap, kv.Key.Distance(apc.tile));
                int moves = apc.moveRange;
                apc.moveRange = 1;
                bool cut = !SupplyNetwork.Solve(l.ai, false).connectedHubs.Contains(hub);
                // Exactly its move: the fewest movement points to drive onto supplied ground links; one fewer does not.
                HexTactica.AI.MoveField mf = HexTactica.AI.AIPath.ThisTurnFrom(apc, apc.tile, 99);
                int need = 99;
                foreach (HexTile t in mf.Tiles) if (t.terrain.elevation >= 0 && stat.IsSupplied(t) && mf.ReachableThisTurn(t)) need = Math.Min(need, mf.Get(t).used);
                apc.moveRange = need;
                bool exact = SupplyNetwork.Solve(l.ai, false).connectedHubs.Contains(hub);
                apc.moveRange = need - 1;
                bool short1 = !SupplyNetwork.Solve(l.ai, false).connectedHubs.Contains(hub);
                apc.moveRange = moves;
                SupplyNetwork.Solve(l.ai, false);
                detail = "loaded " + loaded + "; HQ supply " + gap + " tiles from the APC; linked " + linked + "; spreads up to " + far + " tiles from the APC (range " + hub.range + "); with 1 move, cut off " + cut
                       + "; " + need + " points to supplied ground: with " + need + " moves linked " + exact + ", with " + (need - 1) + " cut off " + short1;
                return loaded && gap > 1 && gap <= moves && linked && far <= hub.range && far > 0 && cut && need > 1 && need <= moves && exact && short1;
            }
            case "attritionSupplyChainLine": {
                Unit hubA = Rig.At(-3, 0).occupier, hubB = Rig.At(0, 0).occupier, g = Rig.At(3, 0).occupier, c = Rig.At(3, 3).occupier, d = Rig.At(4, 3).occupier;
                SupplyPoint A = hubA.Hub, B = hubB.Hub, C = c.Hub, D = d.Hub;
                HexTile hq = GameController.HQOf(l.ai).tile;
                SupplyNetwork.Result net = SupplyNetwork.Solve(l.ai, false);
                bool linked = net.connectedHubs.Contains(A) && net.connectedHubs.Contains(B);
                bool viaBoth = SupplyNetwork.SolveWithout(l.ai, B).DepthAt(g.tile) <= 0 && SupplyNetwork.SolveWithout(l.ai, A).DepthAt(hubB.tile) <= 0;
                List<HexTile> path = SupplyOverlay.SourcePath(l.ai, g.tile, net, null, null);
                List<string> shown = new List<string>(); foreach (HexTile t in path) shown.Add(Pos(t));
                int bAt = path.IndexOf(hubB.tile), aAt = path.IndexOf(hubA.tile);
                bool once = path.FindAll(t => t == hubA.tile).Count == 1 && path.FindAll(t => t == hubB.tile).Count == 1;
                bool line = path.Count > 2 && path[0] == g.tile && bAt > 0 && aAt > bAt && path[path.Count - 1] == hq && once;
                // What hangs on A: B (linked only through it), with its line back to A; nothing hangs on B.
                Dictionary<SupplyPoint, List<HexTile>> depA = SupplyOverlay.Dependents(l.ai, new List<SupplyPoint> { A }, hubA.tile, net, null);
                Dictionary<SupplyPoint, List<HexTile>> depB = SupplyOverlay.Dependents(l.ai, new List<SupplyPoint> { B }, hubB.tile, net, null);
                List<HexTile> bLine = depA.ContainsKey(B) ? depA[B] : new List<HexTile>();
                bool deps = depA.Count == 1 && bLine.Count >= 2 && bLine[0] == hubB.tile && bLine[bLine.Count - 1] == hubA.tile && depB.Count == 0;
                List<string> bl = new List<string>(); foreach (HexTile t in bLine) bl.Add(Pos(t));
                detail = "depending on A: " + depA.Count + " (B, line " + string.Join(" ", bl.ToArray()) + "), on B: " + depB.Count + "; ";
                bool strandedOk = !net.connectedHubs.Contains(C) && !net.connectedHubs.Contains(D) && net.DepthAt(c.tile) <= 0 && net.DepthAt(d.tile) <= 0
                                  && SupplyOverlay.SourcePath(l.ai, c.tile, net, null, null).Count == 0;
                detail += "A and B linked " + linked + ", the grunt fed only through both " + viaBoth + "; line " + string.Join(" ", shown.ToArray())
                       + "; two cut-off supply units side by side: linked " + (net.connectedHubs.Contains(C) || net.connectedHubs.Contains(D)) + ", supplied " + (net.DepthAt(c.tile) > 0 || net.DepthAt(d.tile) > 0);
                // Every placement of two supply units near the HQ: the line from each linked one ends at the HQ, passing each supply unit once.
                c.TeleportTo(Rig.At(6, -6), 0); d.TeleportTo(Rig.At(5, -6), 0); g.TeleportTo(Rig.At(-1, 6), 0);
                List<HexTile> near = new List<HexTile>();
                foreach (HexTile t in Rig.hg.tiles) if (t.Distance(hq) >= 1 && t.Distance(hq) <= 7 && t.occupier == null && t.building == null) near.Add(t);
                int placements = 0, previews = 0, bad = 0; string firstBad = null;
                // A line goes tile to tile (no jumps) and never over an enemy.
                Func<List<HexTile>, bool> walkable = pth => {
                    for (int i = 0; i < pth.Count; i++) {
                        if (pth[i].occupier != null && pth[i].occupier.owner != l.ai) return false;
                        if (i > 0 && pth[i].Distance(pth[i - 1]) != 1) return false;
                        if (i > 1 && pth[i] == pth[i - 2]) return false;   // straight back where it came from
                    }
                    return true;
                };
                HexTile parkA = Rig.At(4, -6), parkB = Rig.At(3, -6);
                foreach (HexTile ta in near) foreach (HexTile tb in near) {
                    if (ta == tb || ta.Distance(tb) > 5) continue;
                    hubA.TeleportTo(parkA, 0); hubB.TeleportTo(parkB, 0);
                    if (ta.occupier != null || tb.occupier != null) continue;
                    hubA.TeleportTo(ta, 0); hubB.TeleportTo(tb, 0);
                    SupplyNetwork.Result n2 = SupplyNetwork.Solve(l.ai, false);
                    placements++;
                    foreach (Unit h in new[] { hubA, hubB }) {
                        if (!n2.connectedHubs.Contains(h.Hub)) continue;
                        List<HexTile> pth = SupplyOverlay.SourcePath(l.ai, h.tile, n2, null, null);
                        bool ok = pth.Count > 0 && pth[pth.Count - 1] == hq && pth.FindAll(t => t == ta).Count <= 1 && pth.FindAll(t => t == tb).Count <= 1 && walkable(pth);
                        if (!ok) { bad++; if (firstBad == null) { List<string> sh = new List<string>(); foreach (HexTile t in pth) sh.Add(Pos(t)); firstBad = "hubs at " + Pos(ta) + " " + Pos(tb) + ", from " + Pos(h.tile) + ": " + string.Join(" ", sh.ToArray()); } }
                    }
                    // A move of A being previewed (a tile picked for it): the line from where it would stand.
                    if (ta.Distance(tb) > 3) continue;
                    foreach (HexTile dest in ta.neighbors) {
                        if (dest == null || dest.occupier != null || dest.building != null) continue;
                        SupplyNetwork.Result pv = SupplyNetwork.Solve(l.ai, false, A, dest);
                        if (!pv.connectedHubs.Contains(A)) continue;
                        previews++;
                        List<HexTile> pth = SupplyOverlay.SourcePath(l.ai, dest, pv, A, dest);
                        bool ok = pth.Count > 0 && pth[pth.Count - 1] == hq && pth.FindAll(t => t == dest).Count <= 1 && pth.FindAll(t => t == tb).Count <= 1 && walkable(pth);
                        if (!ok) { bad++; if (firstBad == null) { List<string> sh = new List<string>(); foreach (HexTile t in pth) sh.Add(Pos(t)); firstBad = "A previewed moving from " + Pos(ta) + " to " + Pos(dest) + ", B at " + Pos(tb) + ": " + string.Join(" ", sh.ToArray()); } }
                    }
                }
                // Both supply units aboard transports (two APCs), for every placement of the two.
                Unit apc1 = Rig.At(2, -6).occupier, apc2 = Rig.At(0, -6).occupier;
                hubA.TeleportTo(Rig.At(2, -5), 0); hubB.TeleportTo(Rig.At(0, -5), 0);
                bool boarded = Transporting.Load(apc1, hubA) && Transporting.Load(apc2, hubB);
                int aboard = 0;
                if (boarded) foreach (HexTile ta in near) foreach (HexTile tb in near) {
                    if (ta == tb || ta.Distance(tb) > 5) continue;
                    apc1.TeleportTo(Rig.At(2, -6), 0); apc2.TeleportTo(Rig.At(0, -6), 0);
                    if (ta.occupier != null || tb.occupier != null) continue;
                    apc1.TeleportTo(ta, 0); apc1.SetCargoTile(ta); apc2.TeleportTo(tb, 0); apc2.SetCargoTile(tb);
                    SupplyNetwork.Result n3 = SupplyNetwork.Solve(l.ai, false);
                    aboard++;
                    foreach (Unit h in new[] { hubA, hubB }) {
                        if (!n3.connectedHubs.Contains(h.Hub)) continue;
                        List<HexTile> pth = SupplyOverlay.SourcePath(l.ai, h.tile, n3, null, null);
                        bool ok = pth.Count > 0 && pth[pth.Count - 1] == hq && pth.FindAll(t => t == ta).Count <= 1 && pth.FindAll(t => t == tb).Count <= 1 && walkable(pth);
                        if (!ok) { bad++; if (firstBad == null) { List<string> sh = new List<string>(); foreach (HexTile t in pth) sh.Add(Pos(t)); firstBad = "both aboard, APCs at " + Pos(ta) + " " + Pos(tb) + ", from " + Pos(h.tile) + ": " + string.Join(" ", sh.ToArray()); } }
                    }
                }
                // Both aboard ONE transport (room for two): the line from it, and from where a previewed move would take it.
                apc1.TeleportTo(Rig.At(2, -6), 0); apc1.SetCargoTile(apc1.tile); apc2.TeleportTo(Rig.At(0, -6), 0); apc2.SetCargoTile(apc2.tile);
                apc1.transportSlots = 4;
                bool moved2 = Transporting.Unload(apc2, hubB, Rig.At(1, -6)) && Transporting.Load(apc1, hubB);
                bool both = moved2 && SupplyOverlay.CarriedSupplies(apc1).Count == 2;
                int one = 0, onePreview = 0;
                if (both) foreach (HexTile ta in near) {
                    apc1.TeleportTo(Rig.At(2, -6), 0); apc1.SetCargoTile(apc1.tile);
                    if (ta.occupier != null) continue;
                    apc1.TeleportTo(ta, 0); apc1.SetCargoTile(ta);
                    SupplyNetwork.Result n4 = SupplyNetwork.Solve(l.ai, false);
                    if (n4.connectedHubs.Contains(hubA.Hub)) {
                        one++;
                        List<HexTile> pth = SupplyOverlay.SourcePath(l.ai, ta, n4, (Dictionary<SupplyPoint, HexTile>)null);
                        bool ok = pth.Count >= 2 && pth[0] == ta && pth[pth.Count - 1] == hq && pth.FindAll(t => t == ta).Count == 1 && walkable(pth);
                        if (!ok) { bad++; if (firstBad == null) { List<string> sh = new List<string>(); foreach (HexTile t in pth) sh.Add(Pos(t)); firstBad = "both aboard one APC at " + Pos(ta) + ": " + string.Join(" ", sh.ToArray()); } }
                    }
                    foreach (HexTile dest in ta.neighbors) {
                        if (dest == null || dest.occupier != null || dest.building != null) continue;
                        Dictionary<SupplyPoint, HexTile> pvm = new Dictionary<SupplyPoint, HexTile> { { hubA.Hub, dest }, { hubB.Hub, dest } };
                        SupplyNetwork.Result pv = SupplyNetwork.SolvePreview(l.ai, pvm);
                        if (!pv.connectedHubs.Contains(hubA.Hub)) continue;
                        onePreview++;
                        List<HexTile> pth = SupplyOverlay.SourcePath(l.ai, dest, pv, pvm);
                        bool ok = pth.Count >= 2 && pth[0] == dest && pth[pth.Count - 1] == hq && pth.FindAll(t => t == dest).Count == 1 && walkable(pth);
                        if (!ok) { bad++; if (firstBad == null) { List<string> sh = new List<string>(); foreach (HexTile t in pth) sh.Add(Pos(t)); firstBad = "both aboard one APC at " + Pos(ta) + ", previewed to " + Pos(dest) + ": " + string.Join(" ", sh.ToArray()); } }
                    }
                }
                SupplyNetwork.Solve(l.ai, false);
                detail += "; " + aboard + " placements with both aboard APCs" + (boarded ? "" : " (could not board)") + ", " + one + " with both aboard one (and " + onePreview + " previewed moves)" + (both ? "" : " (could not put both in one)");
                detail += "; " + placements + " placements of two supply units and " + previews + " previewed moves, " + bad + " lines not ending at the HQ" + (firstBad != null ? " (first: " + firstBad + ")" : "");
                return deps && linked && viaBoth && line && strandedOk && placements > 0 && aboard > 0 && one > 0 && onePreview > 0 && bad == 0;
            }
            case "attritionSupplyLine": {
                Unit g = Rig.At(-2, 0).occupier, sup = Rig.At(-3, 0).occupier;
                HexTile hq = GameController.HQOf(l.ai).tile;
                SupplyNetwork.Result net = SupplyNetwork.Solve(l.ai, false);
                bool viaHub = SupplyNetwork.SolveWithout(l.ai, sup.GetComponent<SupplyPoint>()).DepthAt(g.tile) <= 0;
                List<HexTile> path = SupplyOverlay.SourcePath(l.ai, g.tile, net, null, null);
                List<string> shown = new List<string>();
                foreach (HexTile t in path) shown.Add(Pos(t));
                detail = "grunt supplied only through the supply unit " + viaHub + "; line " + string.Join(" ", shown.ToArray());
                return viaHub && path.Count > 2 && path[0] == g.tile && path.Contains(sup.tile) && path[path.Count - 1] == hq
                       && path.IndexOf(sup.tile) < path.Count - 1;
            }
            case "bunker": {
                Unit g = Rig.At(0, 0).occupier, tank = Rig.At(1, 0).occupier, rg = Rig.At(-1, 0).occupier;
                Building sh = Rig.At(0, 0).building;
                bool prefab = sh != null && sh.holdsGround && sh.attackBonus > 0 && sh.defenseBonus > 0;
                sh.attackBonus = 3; sh.defenseBonus = 3;
                tank.Firepower += 4;   // hard enough to drive a grunt back off a bunker's ground
                CombatOutcome on = CombatMath.Resolve(tank, g, tank.tile);
                int hitOn = Unit.calculateDamage(tank, g, g.tile, 0), backOn = Unit.calculateDamage(g, rg, rg.tile, 0, g.tile);
                sh.owner = l.other;   // not the grunt's: no garrison
                CombatOutcome off = CombatMath.Resolve(tank, g, tank.tile);
                int hitOff = Unit.calculateDamage(tank, g, g.tile, 0), backOff = Unit.calculateDamage(g, rg, rg.tile, 0, g.tile);
                sh.owner = l.ai;
                Unit heli = Rig.At(-3, 3).occupier;
                bool airOut = heli != null && Building.GarrisonFor(heli, g.tile) == null && Building.GarrisonFor(g, g.tile) == sh;
                detail = "prefab holds ground with bonuses " + prefab + "; a gunship over it has no garrison " + airOut + "; in its bunker: retreats " + on.defenderRetreats + " (pinned " + on.defenderPinned + "), takes " + hitOn + ", hits a Red grunt for " + backOn
                       + "; on the enemy's: retreats " + off.defenderRetreats + ", takes " + hitOff + ", hits a Red grunt for " + backOff;
                return prefab && airOut && !on.defenderRetreats && !on.defenderPinned && off.defenderRetreats && hitOn < hitOff && backOn > backOff;
            }
            case "retreatAway": {
                Unit tank = Rig.At(1, 0).occupier, g = Rig.At(0, 0).occupier;
                List<HexTile> away = new List<HexTile>();
                foreach (HexTile n in g.tile.neighbors) if (n != null && n.Distance(tank.tile) > g.tile.Distance(tank.tile)) away.Add(n);
                HexTile straight = Rig.At(2 * g.tile.x - tank.tile.x, 2 * g.tile.y - tank.tile.y);
                HexTile first = CombatMath.FindRetreatTile(g, tank.tile);
                foreach (HexTile t in away) if (t.occupier == null) Put("Grunt", l.other, t.x, t.y);
                HexTile blocked = CombatMath.FindRetreatTile(g, tank.tile);
                List<string> aw = new List<string>(); foreach (HexTile t in away) aw.Add(Pos(t));
                detail = "away tiles " + string.Join(" ", aw.ToArray()) + "; retreats to " + (first != null ? Pos(first) : "none") + " (straight back " + Pos(straight) + "); with those taken: " + (blocked != null ? Pos(blocked) : "pinned");
                return away.Count == 3 && first == straight && blocked == null;
            }
            case "bridgeRoad": {
                Unit g = Rig.At(-1, 1).occupier;
                HexTile bridge = Rig.At(0, 0), westRoad = Rig.At(-1, 0), eastRoad = Rig.At(1, 0), eastGrass = Rig.At(1, -1);
                bool noGrassOn = HexTactica.AI.AIPath.StepCost(g, g.tile, bridge) == int.MaxValue;
                bool roadOn = HexTactica.AI.AIPath.StepCost(g, westRoad, bridge) != int.MaxValue;
                bool noGrassOff = HexTactica.AI.AIPath.StepCost(g, bridge, eastGrass) == int.MaxValue;
                g.moveRange = 20;
                HexTactica.AI.MoveField f = HexTactica.AI.AIPath.ThisTurn(g);
                HexTile via = f.Reachable(bridge) ? f.ParentOf(bridge) : null;
                HexTile grassFrom = f.Reachable(eastGrass) ? f.ParentOf(eastGrass) : null;
                g.TeleportTo(bridge, 0);
                HexTile back = CombatMath.FindRetreatTile(g, westRoad);
                detail = "onto the bridge from grass " + !noGrassOn + ", from road " + roadOn + "; off onto grass " + !noGrassOff + "; path onto it from " + (via != null ? Pos(via) : "nowhere")
                       + ", to the grass across from " + (grassFrom != null ? Pos(grassFrom) : "nowhere") + "; hit from the west road it falls back to " + (back != null ? Pos(back) : "nowhere");
                return noGrassOn && roadOn && noGrassOff && via == westRoad && grassFrom != null && grassFrom != bridge && back == eastRoad;
            }
            case "bridgeWater": {
                Unit boat = Rig.At(0, 0).occupier, g = Rig.At(-1, 0).occupier;
                HexTile bridge = Rig.At(0, 0);
                HexTile wet = bridge.neighbors.Find(t => t != null && t.terrain != null && t.terrain.elevation < 0);
                HexTile grass = bridge.neighbors.Find(t => t != null && t.terrain != null && t.terrain.elevation > 0 && !t.terrain.road);
                HexTactica.AI.MoveField f = HexTactica.AI.AIPath.ThisTurn(boat);
                int reach = 0; foreach (HexTile t in f.Tiles) if (t != bridge && f.ReachableThisTurn(t)) reach++;
                bool off = wet != null && HexTactica.AI.AIPath.StepCost(boat, bridge, wet) != int.MaxValue && f.ReachableThisTurn(wet);
                bool on = wet != null && HexTactica.AI.AIPath.StepCost(boat, wet, bridge) != int.MaxValue;
                bool gruntDry = wet != null && !g.CanEnter(wet);
                g.TeleportTo(Rig.At(1, 0), 0);
                bool gruntRoadOnly = grass == null || HexTactica.AI.AIPath.StepCost(g, bridge, grass) == int.MaxValue;
                detail = "boat on the bridge reaches " + reach + " tiles; off into the water " + off + ", back onto the bridge " + on
                       + "; the grunt cannot enter the water " + gruntDry + ", cannot step off the bridge onto grass " + gruntRoadOnly;
                return off && on && reach > 0 && gruntDry && gruntRoadOnly;
            }
            case "swapSides": {
                string text = File.ReadAllText(l.path);
                string swapped = HexTactica.Harness.ScenarioMirror.SwapSides(text);
                string tmp = Path.Combine(Path.GetTempPath(), "hextactica-swapped.dat");
                File.WriteAllText(tmp, swapped);
                Dictionary<string, Player> before = new Dictionary<string, Player>();
                foreach (Unit u in UnitController.uc.units) if (u != null && u.tile != null) before[Pos(u.tile)] = u.owner;
                Dictionary<string, Player> bBefore = new Dictionary<string, Player>();
                foreach (Building b in Building.all) if (b != null && b.tile != null) bBefore[Pos(b.tile)] = b.owner;
                Player beforeCapturer = Rig.At(2, 2).building.capturingPlayer;
                long aiId = l.ai.id;
                ScenarioFile.Loaded m = ScenarioFile.Load(tmp, "Human", null, null);
                int units = 0, flipped = 0, bFlipped = 0;
                foreach (Unit u in UnitController.uc.units) {
                    if (u == null || u.tile == null) continue;
                    units++;
                    Player was; if (before.TryGetValue(Pos(u.tile), out was) && was != null && u.owner != null && (was.id == aiId) != (u.owner.id == aiId)) flipped++;
                }
                foreach (Building b in Building.all) {
                    Player was; if (b != null && b.tile != null && bBefore.TryGetValue(Pos(b.tile), out was) && was != null && b.owner != null && (was.id == aiId) != (b.owner.id == aiId)) bFlipped++;
                }
                Building city = Rig.At(2, 2).building;
                bool capturer = city != null && city.capturingPlayer != null && beforeCapturer != null && (city.capturingPlayer.id == aiId) != (beforeCapturer.id == aiId);
                detail = units + " units, " + flipped + " changed side; " + bFlipped + " of " + bBefore.Count + " buildings changed side; capture progress changed side " + capturer;
                return units == before.Count && flipped == units && bFlipped == bBefore.Count && capturer;
            }
            case "aiHealWhere": {
                Building city = Rig.At(-5, 1).building; Unit g = Rig.At(-3, 0).occupier;
                city.services = new List<Locomotion> { Locomotion.Boots }; city.healPerTurn = 2; city.healStacksWithSupply = true; city.producesSupply = false;
                g.Strength = 2;
                l.ai.Resupply(); l.other.Resupply();
                AIProfile prof = l.ai.GetComponent<AIPlayer>().Profile;
                HexTactica.AI.AIMap m = new HexTactica.AI.AIMap(l.ai, prof);
                float onCity = m.HealAt(g, city.tile), plain = m.HealAt(g, g.tile);
                if (Environment.GetEnvironmentVariable("EXPLAIN") != null) { AITactical.DebugUnit = g; AITactical.DebugLog = Console.WriteLine; AITactical.DecisionLog = Console.WriteLine; }
                AIAction act = AITactical.Decide(g, m, prof, Posture.Defensive);
                AITactical.DebugUnit = null;
                HexTile to = act.destination ?? g.tile;
                detail = "heal on the city " + onCity + ", on plain supplied ground " + plain + "; the grunt " + act.kind + " to (" + to.x + "," + to.y + ") - " + act.reason;
                return onCity == 3f && plain == 1f && to == city.tile;
            }
            case "aiCampaignClear": {
                Unit tank = Rig.At(-2, 0).occupier, g = Rig.At(-3, 1).occupier;
                Building city = Rig.At(-2, 0).building;
                ScenarioFile.PlayTurn();
                bool off = tank.tile != city.tile;
                ScenarioFile.PlayTurn(); ScenarioFile.PlayTurn();
                AICampaign c = l.ai.GetComponent<AIPlayer>().CurrentCampaign;
                string obj = c == null ? "none" : string.Join("; ", c.objectives.ConvertAll(x => x.kind + "(" + (x.target != null ? x.target.x + "," + x.target.y : "-") + ")").ToArray());
                detail = "objectives " + obj + "; the tank off the city on Blue's first turn " + off + "; after Blue's second: tank at " + Pos(tank.tile) + ", grunt at " + Pos(g.tile) + ", city " + (city.capturingPlayer == l.ai ? "being taken by Blue" : "not being taken") + " (" + city.capturePoints + ")";
                return off && tank.tile != city.tile && g.tile == city.tile;
            }
            case "attacksLocomotion": {
                Unit art = Rig.At(0, 0).occupier, jet = Rig.At(2, 0).occupier, grunt = Rig.At(2, 1).occupier;
                // The prefab's own checklist (main's Artillery leaves jets unticked); the check unticks jets itself whatever it says.
                bool prefabJets = art.attacksLocomotion.Contains(Locomotion.Jet), prefabBoots = art.attacksLocomotion.Contains(Locomotion.Boots);
                art.attacksLocomotion.Remove(Locomotion.Jet);
                bool jetOff = art.CanAttack(jet), valid = art.validTarget(jet.tile);
                int dmg = Unit.calculateDamage(art, jet, jet.tile, 0);
                bool gruntOn = art.CanAttackFrom(art.tile, grunt);
                art.attacksLocomotion.Remove(Locomotion.Boots);
                bool gruntOff = art.CanAttackFrom(art.tile, grunt);
                int gruntDmg = Unit.calculateDamage(art, grunt, grunt.tile, 0);
                detail = "prefab ticks jets " + prefabJets + ", boots " + prefabBoots + "; jets unticked: fighter " + jetOff + " (damage " + dmg + ", valid target " + valid + "); boots ticked: grunt " + gruntOn + "; boots unticked: grunt " + gruntOff + " (damage " + gruntDmg + ")";
                return prefabBoots && !jetOff && dmg == 0 && !valid && gruntOn && !gruntOff && gruntDmg == 0;
            }
            case "fuelWarning": {
                Unit f = Rig.At(0, 0).occupier;
                bool far = f.WouldDitchFrom(Rig.At(3, 0)), near = f.WouldDitchFrom(Rig.At(-2, 1)), port = f.WouldDitchFrom(Rig.At(-3, 2));
                f.fuel = 3;
                bool nearLow = f.WouldDitchFrom(Rig.At(-2, 1)), portLow = f.WouldDitchFrom(Rig.At(-3, 2));
                // A stocked carrier of ours two tiles past (3,0): a base too, so (3,0) is safe; out of supplies it is not one.
                f.fuel = 10;
                HexTile sea = null;
                foreach (HexTile t in Rig.hg.tiles) if (t.occupier == null && t.x > 3 && t.Distance(Rig.At(3, 0)) == 2 && (sea == null || CombatMath.Precedes(t, sea))) sea = t;
                sea.terrain = T("Water");
                Unit carrier = Put("Carrier", a, sea.x, sea.y);
                carrier.supplies = Mathf.Max(1, carrier.suppliesMax);
                bool farCarrier = f.WouldDitchFrom(Rig.At(3, 0));
                carrier.supplies = 0;
                bool farEmptyCarrier = f.WouldDitchFrom(Rig.At(3, 0));
                detail = "10 fuel: (3,0) " + far + ", (-2,1) " + near + ", airport " + port + "; 3 fuel: (-2,1) " + nearLow + ", airport " + portLow
                       + "; stocked carrier at " + Pos(sea) + ", 2 past it: (3,0) " + farCarrier + "; that carrier out of supplies: (3,0) " + farEmptyCarrier;
                return far && !near && !port && nearLow && !portLow && !farCarrier && farEmptyCarrier;
            }
            case "aiRegroupPress":
            case "aiRegroupHold": {
                bool press = l.meta.Get("check", "") == "aiRegroupPress";
                gc.ApplyPurchaseRules(press ? "on" : "off", null);
                l.ai.money = press ? 100000 : 0;
                Unit tank = Rig.At(0, 0).occupier;
                AIProfile prof = l.ai.GetComponent<AIPlayer>().Profile;
                AIMap m = new AIMap(l.ai, prof);
                List<Front> fronts = AIStrategic.BuildFronts(m, prof);
                Front f = AIStrategic.FrontFor(tank, fronts);
                Posture posture = f != null ? f.posture : Posture.Defensive;
                AIAction act = AITactical.Decide(tank, m, prof, posture, f, null);
                HexTile end = act.destination ?? tank.tile;
                bool hitsHub = act.target != null && act.target.tile == Rig.At(1, 0);
                detail = "front " + (f != null ? f.ToString() : "none") + ", edge needed x" + AIStrategic.StrategicNeed(m, prof).ToString("0.00") + "; the tank: " + act.kind + (act.destination != null ? " to " + Pos(act.destination) : "") + (act.target != null ? " at " + act.target.name.Replace("(Clone)", "") + Pos(act.target.tile) : "") + ", " + m.FirersAt(end) + " enemy can strike there - " + act.reason;
                return press ? posture == Posture.Aggressive && hitsHub : posture != Posture.Aggressive && !hitsHub && m.FirersAt(end) == 0;
            }
            case "aiClearOurHQ": {
                Building hq = Rig.At(-6, 0).building;
                hq.capturingPlayer = l.other; hq.capturePoints = 4;
                Rig.At(-3, 2).occupier.Strength = 2;
                Unit r = Rig.At(-4, 0).occupier;
                AIProfile prof = l.ai.GetComponent<AIPlayer>().Profile;
                AIMap m = new AIMap(l.ai, prof);
                if (Environment.GetEnvironmentVariable("EXPLAIN") != null) {
                    AITactical.DecisionLog = Console.WriteLine;
                    Unit mar = Rig.At(-6, 0).occupier;
                    foreach (HexTile n in Rig.At(-6, 0).neighbors) if (n != null && n.occupier == null) {
                        CombatOutcome co = CombatMath.Resolve(r, mar, n);
                        Console.WriteLine("   tank from " + Pos(n) + " on the marine at the HQ: deals " + co.damageToDefender + ", takes " + co.damageToAttacker + (co.defenderRetreats ? ", it retreats" : "")); break;
                    }
                }
                AIAction act;
                try { act = AITactical.Decide(r, m, prof, Posture.Aggressive); } finally { AITactical.DecisionLog = null; }
                detail = "the tank: " + act.kind + (act.destination != null ? " to " + Pos(act.destination) : "") + (act.target != null ? " at " + act.target.name.Replace("(Clone)", "") + Pos(act.target.tile) : "") + " - " + act.reason;
                return act.target != null && act.target.tile == Rig.At(-6, 0);
            }
            case "aiHoldHQ": {
                Unit g = Rig.At(6, 0).occupier;
                g.Strength = 1; g.supplies = 0;
                AIProfile prof = l.ai.GetComponent<AIPlayer>().Profile;
                AIMap m = new AIMap(l.ai, prof);
                if (Environment.GetEnvironmentVariable("EXPLAIN") != null) AITactical.DecisionLog = Console.WriteLine;
                AIAction act;
                try { act = AITactical.Decide(g, m, prof, Posture.Aggressive); } finally { AITactical.DecisionLog = null; }
                detail = "the grunt on the HQ: " + act.kind + (act.destination != null ? " to " + Pos(act.destination) : "") + " - " + act.reason;
                return act.kind == AIActionKind.Capture || (act.kind == AIActionKind.Wait);
            }
            case "campaignLogistics": {
                AIProfile prof = ScriptableObject.CreateInstance<AIProfile>();
                prof.objectiveSpread = 1f;
                AIMap m = new AIMap(l.ai, prof);
                AICampaign c = AICampaign.Plan(m, prof, null);
                c.Fronts(m, prof, new Dictionary<HexTile, Posture>(), new HashSet<HexTile>());
                AILogistics lg = AILogistics.Make(m, c, null);
                detail = lg.ToString();
                bool ordered = true;
                for (int i = 1; i < lg.tasks.Count; i++) ordered &= lg.tasks[i - 1].size >= lg.tasks[i].size;
                HashSet<SupplyPoint> seen = new HashSet<SupplyPoint>(); bool once = true; int hubs = 0;
                foreach (AILogistics.Task t in lg.tasks) { foreach (SupplyPoint h in t.chain) once &= seen.Add(h); foreach (SupplyPoint h in t.tactical) once &= seen.Add(h); }
                foreach (SupplyPoint h in m.ownHubs) if (h != null && !h.producer && h.myUnit != null) hubs++;
                int chained = 0; foreach (AILogistics.Task t in lg.tasks) chained += t.chain.Count;
                // The rest by share: each front's own within one of its share of what was left.
                bool shared = true; int total = 0; foreach (AILogistics.Task t in lg.tasks) total += t.size;
                foreach (AILogistics.Task t in lg.tasks) shared &= Math.Abs(t.tactical.Count - (hubs - chained) * t.size / (float)Math.Max(1, total)) < 1.01f;
                detail += "; " + lg.tasks.Count + " fronts, largest first " + ordered + "; " + seen.Count + " of " + hubs + " supply units given one job each " + once + "; shared by size " + shared;
                return lg.tasks.Count >= 2 && ordered && once && seen.Count == hubs && shared;
            }
            case "chainTruck": {
                Unit hub = Rig.At(-4, 0).occupier, truck = Rig.At(-6, 3).occupier;
                AIPlayer ap = l.ai.GetComponent<AIPlayer>();
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) { AIPlayer.Trace = Console.WriteLine; HexTactica.AI.AISupplyChain.DebugLog = Console.WriteLine; AITactical.DecisionLog = Console.WriteLine; }
                List<string> r = new List<string>();
                int boardedOn = -1; bool setDown = false, claimed = false;
                for (int turn = 0; turn < 4 && !gc.gameOver; turn++) {
                    ScenarioFile.PlayTurn();
                    if (hub.tile == null) { r.Add("supply unit lost"); break; }
                    HexTactica.AI.AILogistics lg = ap.CurrentLogistics;
                    if (lg != null) foreach (KeyValuePair<SupplyPoint, Unit> kv in lg.chainTrucks) if (kv.Key.myUnit == hub && kv.Value == truck) claimed = true;
                    bool aboard = hub.trasnporter == truck;
                    if (aboard && boardedOn < 0) boardedOn = turn;
                    if (!aboard && boardedOn >= 0) setDown = true;
                    r.Add("turn " + turn + ": supply " + (aboard ? "aboard at " + Pos(truck.tile) : "on foot at " + Pos(hub.tile) + ", truck at " + Pos(truck.tile)) + (lg != null ? (lg.chainOf.ContainsKey(hub.Hub) ? " [chain link]" : " [not a link]") : " [no logistics]"));
                    if (!gc.gameOver) ScenarioFile.PlayTurn();
                }
                detail = string.Join("; ", r.ToArray()) + "; the truck claimed for it " + claimed + ", boarded on turn " + boardedOn + ", set down " + setDown;
                return claimed && boardedOn >= 0 && boardedOn <= 1 && !setDown;
            }
            case "planeBoarding": {
                Unit planeBase = Rig.At(-3, 3).occupier, gBase = Rig.At(-2, 3).occupier, planeOpen = Rig.At(2, 0).occupier, gOpen = Rig.At(3, 0).occupier, helo = Rig.At(2, -3).occupier, gHelo = Rig.At(3, -3).occupier;
                bool onRunway = Building.ServicingUnder(planeBase) != null;
                bool baseOk = Transporting.CanBoardFrom(planeBase, gBase), openOk = Transporting.CanBoardFrom(planeOpen, gOpen), heloOk = Transporting.CanBoardFrom(helo, gHelo);
                detail = "plane " + planeBase.locomotion + " on our runway (" + onRunway + "): grunt boards " + baseOk + "; plane over open ground: grunt boards " + openOk + "; helicopter (" + helo.locomotion + ") over open ground: grunt boards " + heloOk + " (slots " + helo.transportSlots + ", carries " + string.Join(",", helo.carries.ConvertAll(x => x.ToString()).ToArray()) + ", can carry " + Transporting.CanCarry(helo, gHelo) + ", beside " + helo.tile.neighbors.Contains(gHelo.tile) + ", grunt enters " + gHelo.CanEnter(helo.tile) + ")";
                return onRunway && baseOk && !openOk && heloOk;
            }
            case "campaignNoOverpromise": {
                AIProfile prof = ScriptableObject.CreateInstance<AIProfile>();
                prof.objectiveSpread = 1f;
                AIMap m = new AIMap(l.ai, prof);
                AICampaign first = AICampaign.Plan(m, prof, null);
                Func<AICampaign, AIMap, string> promised = (c, mm) => {
                    SupplyNetwork.Result net = SupplyNetwork.SolveAll(l.ai, new Dictionary<SupplyPoint, HexTile>());
                    int free = 0, owed = 0;
                    HashSet<SupplyPoint> given = new HashSet<SupplyPoint>();
                    foreach (CampaignObjective x in c.objectives) foreach (SupplyPoint h in x.hubs) given.Add(h);
                    foreach (SupplyPoint h in mm.ownHubs) if (h != null && !h.producer && h.myUnit != null && h.myUnit.tile != null && net.connectedHubs.Contains(h) && !given.Contains(h)) free++;
                    foreach (CampaignObjective x in c.objectives) { if (!x.lasting) continue; int has = 0; foreach (SupplyPoint h in x.hubs) if (net.connectedHubs.Contains(h)) has++; owed += Math.Max(0, x.needHubs - has); }
                    return owed + "/" + free;
                };
                string before = promised(first, m);
                int needBefore = 0; foreach (CampaignObjective x in first.objectives) if (x.lasting) needBefore += x.needHubs;
                Rig.At(-6, 1).occupier.Perish(); Rig.At(-6, -1).occupier.Perish();
                AIMap m2 = new AIMap(l.ai, prof);
                List<string> gaveUp = new List<string>();
                Action<string> was = AICampaign.Debug;
                AICampaign.Debug = x => { if (x.StartsWith("given up")) gaveUp.Add(x); };
                AICampaign second;
                try { second = AICampaign.Plan(m2, prof, first); } finally { AICampaign.Debug = was; }
                string after = promised(second, m2);
                string[] pa = after.Split('/');
                bool fits = int.Parse(pa[0]) <= int.Parse(pa[1]);
                Func<AICampaign, string> list = c => string.Join(", ", c.objectives.ConvertAll(x => x.Id + " " + x.hubs.Count + "/" + x.needHubs).ToArray());
                detail = "first plan: " + list(first) + " (supply owed/free " + before + ", needs " + needBefore + "); with one supply unit left: " + list(second) + " (owed/free " + after + ")" + (gaveUp.Count > 0 ? "; " + string.Join("; ", gaveUp.ToArray()) : "; none given up");
                return fits;
            }
            case "profileAsset": {
                Dictionary<string, string> asset = ProfileAsset();
                AIProfile def = ScriptableObject.CreateInstance<AIProfile>();
                List<string> missing = new List<string>(), differ = new List<string>();
                foreach (System.Reflection.FieldInfo fi in typeof(AIProfile).GetFields()) {
                    if (fi.IsStatic || (fi.FieldType != typeof(bool) && fi.FieldType != typeof(int) && fi.FieldType != typeof(float))) continue;
                    string v;
                    if (!asset.TryGetValue(fi.Name, out v)) { missing.Add(fi.Name); continue; }
                    double want = fi.FieldType == typeof(bool) ? ((bool)fi.GetValue(def) ? 1 : 0) : Convert.ToDouble(fi.GetValue(def));
                    if (fi.Name == "campaign") want = 1;
                    double have; if (!double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out have) || Math.Abs(have - want) > 1e-4) differ.Add(fi.Name + " " + v + " (code " + want + ")");
                }
                detail = asset.Count + " settings in the asset; missing " + missing.Count + (missing.Count > 0 ? " (" + string.Join(", ", missing.ToArray()) + ")" : "") + "; differing " + differ.Count + (differ.Count > 0 ? " (" + string.Join(", ", differ.ToArray()) + ")" : "");
                return missing.Count == 0 && differ.Count == 0;
            }
            case "knowledgeAllOut": {
                Func<bool, AIKnowledge> read = fogOn => {
                    gc.fogOfWar = fogOn; FogOfWar.Reset(); FogOfWar.Changed(); FogOfWar.Refresh();
                    AIProfile prof = ScriptableObject.CreateInstance<AIProfile>();
                    AIKnowledge k = new AIKnowledge();
                    BoardView v = fogOn ? FogOfWar.Conceal(a, true, prof.fogMemoryTurns) : null;
                    using (v != null ? v.Enter() : null) { AIMap m = new AIMap(a, prof); k.Update(m, prof); }
                    return k;
                };
                AIKnowledge clear = read(false), fogged = read(true);
                gc.fogOfWar = false; FogOfWar.Reset();
                detail = "no fog: " + clear + " | fog, Red unseen: " + fogged;
                return clear.confidence >= 0.99f && clear.allOut && clear.releaseReserve && clear.aggression > 0.9f && !fogged.allOut && !fogged.releaseReserve;
            }
            case "mobileReserve": {
                Unit apc = Rig.At(-4, 2).occupier, rider = Rig.At(-6, 2).occupier, tank = Rig.At(-5, -1).occupier, recon = Rig.At(-5, 2).occupier, gun = Rig.At(-6, 1).occupier, foot = Rig.At(-4, -2).occupier;
                rider.TeleportTo(Rig.At(-4, 1), 0);
                Transporting.Load(apc, rider);
                Func<bool, Unit, bool> member = (mobile, u) => { AIProfile prof = ScriptableObject.CreateInstance<AIProfile>(); return AICampaign.ReserveMember(u, prof); };
                Func<bool, string> list = mobile => string.Join(", ", new[] { tank, recon, gun, apc, foot }.Where(u => member(mobile, u)).Select(u => GameController.UnitName(u)).ToArray());
                string on = list(true);
                detail = "rider aboard the APC " + (rider.trasnporter == apc) + "; mobile reserve may hold: " + on;
                bool mob = member(true, tank) && member(true, recon) && member(true, gun) && member(true, apc) && !member(true, foot) && !member(true, rider);
                return rider.trasnporter == apc && mob;
            }
            case "factionRoster": {
                Building hq = Rig.At(-4, 0).building, fac = Rig.At(-2, 2).building;
                Func<List<Unit>, string> names = list => string.Join(", ", list.ConvertAll(u => u.name.Replace("(Clone)", "").Trim()).ToArray());
                List<Unit> baseHQ = hq.Purchasable(), baseFac = fac.Purchasable();
                Unit grunt = baseHQ.Find(u => u.name.Replace("(Clone)", "").Trim() == "Grunt");
                Unit dropped = baseHQ.Find(u => u != grunt);
                Unit version = PrefabLoader.Load("Grunt"); version.name = "Grunt - Testland";
                Unit extra = PrefabLoader.Load("Tank"); extra.name = "Heavy Tank - Testland";
                Faction f = ScriptableObject.CreateInstance<Faction>(); f.name = "Testland";
                f.replacements.Add(new Faction.Replacement { original = grunt, replacement = version });
                f.removed.Add(dropped);
                f.extras.Add(new Faction.Extra { unit = extra, builtAt = new List<BuildingType> { BuildingType.Factory } });
                l.ai.faction = f;
                List<Unit> hqF = hq.Purchasable(), facF = fac.Purchasable();
                bool roster = grunt != null && dropped != null
                    && hqF.Contains(version) && !hqF.Contains(grunt) && !hqF.Contains(dropped) && !hqF.Contains(extra)
                    && facF.Contains(extra) && facF.Contains(version) && !facF.Exists(u => u.name.Replace("(Clone)", "").Trim() == "Grunt")
                    && hqF.Count == baseHQ.Count - 1 && facF.Count == baseFac.Count + 1;
                List<string> r = new List<string>();
                r.Add("HQ base: " + names(baseHQ) + "; with the faction: " + names(hqF));
                r.Add("ArmyBase base: " + names(baseFac) + "; with the faction: " + names(facF));
                // Allowed where the scenario allows the Grunt.
                List<string> wasAllowed = GameController.gc.allowedUnits;
                GameController.gc.allowedUnits = new List<string> { "Grunt" };
                bool allowed = hq.CanBuild(version) && !fac.CanBuild(extra);
                GameController.gc.allowedUnits = wasAllowed;
                r.Add("allowed units 'Grunt': the version " + (allowed ? "allowed, the extra not" : "WRONG"));
                // Bought under its own name.
                l.ai.money = 100000;
                Unit made = BuildingController.bc.Produce(hq, version);
                string madeName = made != null ? made.name.Replace("(Clone)", "").Trim() : "nothing";
                bool bought = made != null && madeName == "Grunt - Testland";
                r.Add("bought: " + madeName);
                // Saved by name, loaded by name.
                LevelEditor was = LevelEditor.singleton;
                bool saved;
                try {
                    LevelEditor ed = Rig.New<LevelEditor>();
                    ed.allUnit = new List<Unit>();
                    ed.allFaction = new List<Faction> { f };
                    LevelEditor.singleton = ed;
                    PlayerData pd = new PlayerData(l.ai);
                    string json = JsonUtility.ToJson(pd);
                    Faction back = LevelEditor.FindFaction(pd.factionName);
                    Unit loaded = LevelEditor.loadUnit(madeName + "(Clone)");
                    saved = pd.factionName == "Testland" && back == f && loaded == version;
                    r.Add("saved faction '" + pd.factionName + "', found again " + (back == f) + "; unit '" + madeName + "' loads as the version " + (loaded == version));
                } finally { LevelEditor.singleton = was; }
                l.ai.faction = null;
                bool off = names(hq.Purchasable()) == names(baseHQ);
                r.Add("without the faction again: " + names(hq.Purchasable()));
                detail = string.Join("; ", r.ToArray());
                return roster && allowed && bought && saved && off;
            }
            case "factionLoad": {
                List<string> r = new List<string>();
                LevelEditor was = LevelEditor.singleton;
                bool lookups, placed, other, shown, threaded;
                try {
                    // Nothing listed on the scene's LevelEditor: everything comes from the Resources folder.
                    LevelEditor ed = Rig.New<LevelEditor>();
                    ed.allUnit = new List<Unit>(); ed.allBuilding = new List<Building>(); ed.allTerrain = new List<TerrainData>(); ed.allFaction = new List<Faction>();
                    LevelEditor.singleton = ed;
                    Unit gruntR = LevelEditor.loadUnit("Grunt"), variantR = LevelEditor.loadUnit("Grunt Federation Variant(Clone)");
                    Building cityR = LevelEditor.loadBuilding("City");
                    TerrainData grassR = LevelEditor.loadTerrain("Grassland");
                    lookups = gruntR != null && variantR != null && variantR.name == "Grunt Federation Variant" && cityR != null && grassR != null;
                    r.Add("from Resources with empty lists: Grunt " + (gruntR != null) + ", 'Grunt Federation Variant' " + (variantR != null ? variantR.name : "MISSING") + ", City " + (cityR != null) + ", Grassland " + (grassR != null));

                    // Fresh prefab copies (no name read yet), as a faction asset holds them.
                    Unit original = PrefabLoader.Load("Grunt"), version = PrefabLoader.Load("Grunt Federation Variant");
                    Faction fed = ScriptableObject.CreateInstance<Faction>(); fed.name = "Federation";
                    fed.replacements.Add(new Faction.Replacement { original = original, replacement = version });
                    Unit otherVersion = PrefabLoader.Load("Grunt"); otherVersion.name = "Grunt Testland Variant";
                    Faction test = ScriptableObject.CreateInstance<Faction>(); test.name = "Testland";
                    test.replacements.Add(new Faction.Replacement { original = PrefabLoader.Load("Grunt"), replacement = otherVersion });
                    ed.allFaction = new List<Faction> { fed, test };
                    // (As Unity loads an asset: no name read yet. The harness loader names its copies, which reads it.)
                    System.Reflection.FieldInfo cache = typeof(Unit).GetField("nameCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    cache.SetValue(original, null); cache.SetValue(version, null);
                    Building hqB = Rig.At(-6, 0).building;
                    List<Unit> offered = hqB != null && hqB.buildable != null ? new List<Unit>(hqB.buildable) : new List<Unit>();
                    foreach (Unit ou in offered) if (ou != null) cache.SetValue(ou, null);
                    List<string> offeredAs = new List<string>();
                    l.ai.faction = fed;

                    // Thinking on another thread, after the board is copied, the faction matches units by name:
                    // the names must have been read on the main thread (Unity: "GetName can only be called from the main thread").
                    int before = UnityEngine.MainThreadOnly.violations;
                    string threadErr = null; Unit mapped = null, based = null;
                    BoardView view = BoardView.Snapshot();
                    System.Threading.Thread th = new System.Threading.Thread(() => {
                        try { using (view.Enter()) { mapped = fed.Map(original); based = fed.BaseOf(version); foreach (Unit ou in offered) { Unit m = fed.Map(ou); offeredAs.Add(m != null ? m.name : "none"); } } }
                        catch (Exception e) { threadErr = e.GetBaseException().Message; }
                    });
                    th.Start(); th.Join();
                    threaded = threadErr == null && UnityEngine.MainThreadOnly.violations == before && mapped == version && based == original && offered.Count > 1 && offeredAs.Count == offered.Count;
                    r.Add("on a thinking thread: the faction maps Grunt to " + (mapped != null ? mapped.name : "nothing") + ", back to " + (based != null ? based.name : "nothing") + ", the HQ's " + offered.Count + " units to " + string.Join("/", offeredAs.ToArray()) + (threadErr != null ? ", FAILED: " + threadErr : ", no main-thread-only call"));

                    foreach (HexTile t in Rig.hg.tiles) if (t.unitPlacement == null) { t.unitPlacement = Rig.New<Transform>(); t.unitPlacement.position = new Vector3(t.x, t.y, 0f); }
                    // A scenario loading a Grunt for a Federation side places the Federation's version; the build menu calls it Grunt.
                    Unit ua = LevelEditor.PlaceNewUnit(l.ai, "Grunt", Rig.At(-1, 0));
                    string an = ua != null ? ua.name.Replace("(Clone)", "").Trim() : "nothing";
                    placed = an == "Grunt Federation Variant";
                    Unit ub = LevelEditor.PlaceNewUnit(l.ai, "Grunt Testland Variant(Clone)", Rig.At(-1, 1));
                    string bn = ub != null ? ub.name.Replace("(Clone)", "").Trim() : "nothing";
                    other = bn == "Grunt Federation Variant";
                    Unit uc2 = LevelEditor.PlaceNewUnit(l.other, "Grunt", Rig.At(1, 0));
                    string cn = uc2 != null ? uc2.name.Replace("(Clone)", "").Trim() : "nothing";
                    placed &= cn == "Grunt";
                    r.Add("loaded 'Grunt' for Federation Blue: " + an + "; 'Grunt Testland Variant' for Blue: " + bn + "; 'Grunt' for factionless Red: " + cn);
                    string d1 = GameController.DisplayName(version, l.ai), d2 = GameController.DisplayName(original, l.ai), d3 = GameController.DisplayName(PrefabLoader.Load("Gunship Variant"), l.other);
                    shown = d1 == "Grunt" && d2 == "Grunt" && d3 == "Gunship";
                    r.Add("shown as: '" + d1 + "', '" + d2 + "', 'Gunship Variant' as '" + d3 + "'");
                } finally { LevelEditor.singleton = was; l.ai.faction = null; }
                detail = string.Join("; ", r.ToArray());
                return lookups && threaded && placed && other && shown;
            }
            case "loadCargo": {
                // The game's own loader (HexTactica > Load Scenario File: ScenarioLoader, which shares UnitData.LoadAll with the
                // in-game load) on Korea, whose trucks are listed after the supplies aboard them, on the same tile. The scene's
                // LevelEditor lists the older units only (as the scene does): the rest come from the Resources folder.
                List<string> r = new List<string>();
                LevelEditor was = LevelEditor.singleton;
                int errorsBefore = UnityEngine.Debug.errors;
                bool ok;
                try {
                    LevelEditor ed = Rig.New<LevelEditor>();
                    ed.allUnit = new List<Unit>();
                    foreach (string n in new[] { "Grunt", "Conscript", "Marine", "Tank", "APC", "Artillery", "Recon", "Supplies", "Fighter", "Bomber", "Stealth Fighter", "Gunner Anti Air" }) { Unit u = PrefabLoader.Load(n); u.name = n; ed.allUnit.Add(u); }
                    ed.allFaction = new List<Faction>();
                    LevelEditor.singleton = ed;
                    string path = Path.Combine(PrefabLoader.Root("Scenarios"), "Korea.dat");
                    Dictionary<string, int> want = new Dictionary<string, int>(), carried = new Dictionary<string, int>();
                    int wantAboard = 0;
                    foreach (Dictionary<string, string> u in ScenarioFile.Array(File.ReadAllText(path), "units")) {
                        string n = u["gameobject_name"].Replace("(Clone)", "").Trim();
                        int k; want.TryGetValue(n, out k); want[n] = k + 1;
                        if (u.ContainsKey("transporterIndexPlusOne") && u["transporterIndexPlusOne"] != "0") wantAboard++;
                    }
                    // (Tiles given what the game's tile prefab has — sprites, road pieces, a placement point — and new
                    // ones made that way: the board grows to the map's size.)
                    Action<HexTile> dress = t => {
                        foreach (System.Reflection.FieldInfo fi in typeof(HexTile).GetFields()) {
                            if (fi.GetValue(t) != null) continue;
                            if (fi.FieldType == typeof(SpriteRenderer)) fi.SetValue(t, Rig.New<SpriteRenderer>());
                            else if (fi.FieldType == typeof(Transform)) fi.SetValue(t, Rig.New<Transform>());
                            else if (fi.FieldType == typeof(UnityEngine.UI.Text)) fi.SetValue(t, Rig.New<UnityEngine.UI.Text>());
                        }
                    };
                    Rig.hg.makeTile = at => {
                        HexTile t = Rig.New<HexTile>();
                        t.neighbors = new List<HexTile>(); t.aiData = new AITileData();
                        t.threat = new Dictionary<Player, Heat>(); t.supply = new Dictionary<Player, int>();
                        t.terrain = ScenarioFile.Terrain("Grassland");
                        dress(t);
                        return t;
                    };
                    foreach (HexTile t in Rig.hg.tiles) dress(t);
                    bool loaded;
                    // (Instantiate copies a unit as Unity does: its own lists, its supply hub.)
                    UnityEngine.GameObject.Cloner = obj => {
                        GameObject g = obj as GameObject; Unit src = g != null ? g.GetComponent<Unit>() : null;
                        if (src == null) return null;
                        Unit c = Rig.Clone(src);
                        c.path = new List<HexTile>(); c.transported = new List<Unit>();
                        PrefabLoader.AttachHubIfAny(src.name.Replace("(Clone)", "").Trim(), c);
                        return c.gameObject;
                    };
                    try { loaded = HexTactica.Harness.ScenarioLoader.LoadBoardFromPath(path); } finally { Rig.hg.makeTile = null; UnityEngine.GameObject.Cloner = null; }
                    Dictionary<string, int> got = new Dictionary<string, int>();
                    int aboard = 0, aboardActive = 0, misplaced = 0;
                    foreach (Unit u in Rig.uc.units) {
                        if (u == null) continue;
                        string n = u.name.Replace("(Clone)", "").Trim();
                        int k; got.TryGetValue(n, out k); got[n] = k + 1;
                        if (u.trasnporter != null) { aboard++; if (u.active) aboardActive++; if (u.tile != u.trasnporter.tile || u.tile.occupier == u || !u.trasnporter.transported.Contains(u)) misplaced++; }
                        else if (u.tile == null || u.tile.occupier != u) misplaced++;
                    }
                    List<string> counts = new List<string>(); bool all = true;
                    foreach (KeyValuePair<string, int> kv in want) { int g; got.TryGetValue(kv.Key, out g); if (g != kv.Value) all = false; if (g != kv.Value || kv.Key == "Rocket Artillery" || kv.Key == "Anti Air Missile Truck" || kv.Key == "Truck" || kv.Key == "Supplies") counts.Add(kv.Key + " " + g + "/" + kv.Value); }
                    int errors = UnityEngine.Debug.errors - errorsBefore;
                    ok = loaded && all && aboard == wantAboard && aboardActive == 0 && misplaced == 0 && errors == 0;
                    r.Add("loaded " + loaded + "; " + string.Join(", ", counts.ToArray()) + (all ? " (every unit in the file)" : " (MISSING units)") + "; aboard " + aboard + "/" + wantAboard + ", of them active " + aboardActive + "; misplaced " + misplaced + "; errors logged " + errors);
                } finally { LevelEditor.singleton = was; }
                detail = string.Join("; ", r.ToArray());
                return ok;
            }
            case "supplyFob": {
                List<string> r = new List<string>();
                Building fob = Rig.At(0, 0).building;
                System.Reflection.MethodInfo die = typeof(Unit).GetMethod("DieActual", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                // The field the board shows (published) against one worked out afresh (not published).
                Func<string> stale = () => {
                    SupplyNetwork.Result fresh = SupplyNetwork.SolveAll(a, new Dictionary<SupplyPoint, HexTile>());
                    int n = 0; HexTile first = null;
                    foreach (HexTile t in Rig.hg.tiles) if ((t.supply[a] > 0) != fresh.IsSupplied(t)) { n++; if (first == null) first = t; }
                    return n == 0 ? "board up to date" : "BOARD STALE on " + n + " tiles (first " + first.x + "," + first.y + ": shows " + (first.supply[a] > 0 ? "supplied" : "unsupplied") + ")";
                };
                Func<SupplyNetwork.Result, string> row = res => { string o2 = ""; for (int x = -13; x <= 13; x++) o2 += res.DepthAt(Rig.At(x, 0)) > 0 ? (Rig.At(x, 0).building != null ? "B" : res.DepthAt(Rig.At(x, 0)).ToString("X")) : "."; return o2; };
                Func<SupplyPoint, SupplyNetwork.Result, string> st = (h, res) => res.connectedHubs.Contains(h) ? "linked" : res.strandedHubs.Contains(h) ? "CUT OFF" : "?";
                fob.supplies = 6;
                SupplyNetwork.Result s0 = SupplyNetwork.Solve(a, false);
                r.Add("1. HQ at -13, FOB at 0 (range " + fob.supplyRange + ", store 6), no supply units. Row y=0 depth: " + row(s0) + "; FOB " + (s0.connectedBuildings.Contains(fob) ? "linked" : s0.strandedBuildings.Contains(fob) ? "cut off, giving from its store" : "?"));
                // A supply unit in the cut-off FOB's ground relays its store on, past the FOB's own reach (to 8).
                Unit relayU = Put("Supplies", a, 7, 0);
                SupplyNetwork.Result s0b = SupplyNetwork.Solve(a, false);
                bool relays = s0b.connectedHubs.Contains(relayU.Hub) && s0b.IsSupplied(Rig.At(10, 0)) && !s0.IsSupplied(Rig.At(10, 0));
                r.Add("1b. Supplies at 7 (FOB cut off): " + st(relayU.Hub, s0b) + "; (10,0) supplied " + s0b.IsSupplied(Rig.At(10, 0)) + "; row " + row(s0b));
                die.Invoke(relayU, null);
                // Supply units on foot between them.
                Unit h1 = Put("Supplies", a, -6, 0);
                SupplyNetwork.Result s1 = SupplyNetwork.Solve(a, false);
                r.Add("2. Supplies on foot at -6: " + st(h1.Hub, s1) + "; FOB " + (s1.connectedBuildings.Contains(fob) ? "linked" : "cut off") + "; row " + row(s1));
                Unit h2 = Put("Supplies", a, 3, 0);
                SupplyNetwork.Result s2 = SupplyNetwork.Solve(a, false);
                r.Add("3. Supplies on foot at 3 (in the FOB's area): " + st(h2.Hub, s2) + "; row " + row(s2));
                // A truck carrying supplies, beside the FOB.
                Unit truck = Put("Truck", a, 2, 3);
                Unit cargo = Put("Supplies", a, 2, 4);
                bool boarded = Transporting.Load(truck, cargo);
                SupplyNetwork.Result s3 = SupplyNetwork.Solve(a, false);
                bool truckTileSupplied = s3.IsSupplied(truck.tile);
                r.Add("4. Truck at 2,3 carrying supplies (boarded " + boarded + "): its supplies " + st(cargo.Hub, s3) + ", truck's tile " + (s3.IsSupplied(truck.tile) ? "supplied" : "UNSUPPLIED"));
                // Moved as the player moves it (HexTile: CompleteMove then FinishActive).
                SupplyNetwork.Solve(a, false);
                truck.MoveAlong(new List<HexTile> { Rig.At(2, 3), Rig.At(3, 3), Rig.At(4, 3), Rig.At(5, 3) }, false, false);
                truck.FinishActive();
                string st5 = stale(); r.Add("5. Truck driven to 5,3 (as the player moves it): " + st5);
                SupplyNetwork.Solve(a, false);
                // The supply unit on foot moves.
                h2.MoveAlong(new List<HexTile> { Rig.At(3, 0), Rig.At(4, 0), Rig.At(5, 0) }, false, false);
                h2.FinishActive();
                string st6a = stale(); r.Add("6. Supplies on foot walked to 5,0: " + st6a);
                Unit truck2 = Put("Truck", a, -11, 2);
                Unit cargo2 = Put("Supplies", a, -11, 3);
                Transporting.Load(truck2, cargo2);
                SupplyNetwork.Result s6 = SupplyNetwork.Solve(a, false);
                string before6 = st(cargo2.Hub, s6);
                List<HexTile> drive = new List<HexTile>(); for (int x = -11; x <= -7; x++) drive.Add(Rig.At(x, 2));
                truck2.MoveAlong(drive, false, false);
                truck2.FinishActive();
                string staleNow = stale();
                SupplyNetwork.Result s7 = SupplyNetwork.Solve(a, false);
                r.Add("6b. Truck carrying linked supplies (" + before6 + ") driven -11,2 to -7,2 as the player moves it: " + staleNow + "; afterwards its supplies " + st(cargo2.Hub, s7) + ", hub.connected flag says " + cargo2.Hub.connected);
                // An enemy unit standing in the corridor, then killed by the player (not a supply unit).
                Unit blocker = Put("Grunt", o, -3, 0);
                foreach (int[] w in new[] { new[] { -3, -1 }, new[] { -3, 1 }, new[] { -2, -1 }, new[] { -4, 1 } }) Put("Grunt", o, w[0], w[1]);
                SupplyNetwork.Result s4 = SupplyNetwork.Solve(a, false);
                r.Add("7. Red grunts across the corridor at x=-3: FOB " + (s4.connectedBuildings.Contains(fob) ? "linked" : "cut off") + "; row " + row(s4));
                die.Invoke(blocker, null);
                string st8 = stale(); r.Add("8. The Red grunt at -3,0 killed (not a supply unit): " + st8);
                SupplyNetwork.Solve(a, false);
                // Fog: an enemy Blue does not see, blocking.
                gc.fogOfWar = true; FogOfWar.Reset(); FogOfWar.Refresh();
                Unit hidden = Put("Grunt", o, -3, 0);
                FogOfWar.Changed(); FogOfWar.Refresh();
                bool seen = FogOfWar.Sees(a, hidden);
                SupplyNetwork.Result s5 = SupplyNetwork.Solve(a, false);
                r.Add("9. Fog on, a Red grunt back at -3,0, seen by Blue " + seen + ": FOB " + (s5.connectedBuildings.Contains(fob) ? "linked" : "cut off") + "; row " + row(s5));
                gc.fogOfWar = false; FogOfWar.Reset();
                detail = string.Join(" | ", r.ToArray());
                bool fobRules = s0.strandedBuildings.Contains(fob) && s0.IsSupplied(Rig.At(5, 0)) && s1.connectedHubs.Contains(h1.Hub) && s2.connectedHubs.Contains(h2.Hub)
                                && s3.connectedHubs.Contains(cargo.Hub) && truckTileSupplied;
                bool upToDate = st5 == "board up to date" && st6a == "board up to date" && staleNow == "board up to date" && st8 == "board up to date";
                return fobRules && relays && upToDate && before6 == "linked" && s7.connectedHubs.Contains(cargo2.Hub) && cargo2.Hub.connected;
            }
            case "fobPrefersHQ": {
                // HQ at -13 (its supply reaches -10 on grassland). FOB at -4, its own range covering -11..3, cut off.
                Building fob = Rig.At(-4, 0).building;
                List<string> r = new List<string>();
                Func<string, int> turn = what => { int before = fob.supplies; SupplyNetwork.Solve(a, true); r.Add(what + ": store " + before + " -> " + fob.supplies); return before - fob.supplies; };
                fob.supplies = 4;
                int empty = turn("nobody near it");
                Unit g = Put("Grunt", a, -11, 0);
                bool overlapSupplied = SupplyNetwork.Solve(a, false).IsSupplied(g.tile);
                int overlap = turn("a grunt at -11,0, inside the HQ's supply too (" + (SupplyNetwork.SolveStatic(a).IsSupplied(g.tile) ? "it is" : "IT IS NOT") + ")");
                Unit g2 = Put("Grunt", a, 0, 1);
                bool onlyStore = !SupplyNetwork.SolveStatic(a).IsSupplied(g2.tile) && SupplyNetwork.Solve(a, false).IsSupplied(g2.tile);
                int needed = turn("and a grunt at 0,1, which only the FOB supplies (supplied " + onlyStore + ")");
                // Linked to the HQ: a supply unit between them.
                Put("Supplies", a, -10, 0); Put("Supplies", a, -7, 0);
                SupplyNetwork.Result lr = SupplyNetwork.Solve(a, false);
                bool linked = lr.connectedBuildings.Contains(fob);
                int refill = turn("supply units at -10,0 and -7,0 link it to the HQ (linked " + linked + ")");
                // Switch off: the old rule (every cut-off building with store spends every turn).
                SupplyNetwork.StoreOnlyWhenNeeded = false;
                Building f2 = Rig.At(5, -2).building; f2.supplies = 3; int b2 = f2.supplies; fob.supplies = 4;
                SupplyNetwork.Solve(a, true);
                int oldRule = b2 - f2.supplies;
                SupplyNetwork.StoreOnlyWhenNeeded = true;
                f2.supplies = 3; fob.supplies = 4; SupplyNetwork.Solve(a, true);
                int newRule = 3 - f2.supplies;
                r.Add("a second FOB at 5,-2 cut off with nobody near: spends " + newRule + " (old rule " + oldRule + ")");
                detail = string.Join("; ", r.ToArray());
                return empty == 0 && overlapSupplied && overlap == 0 && onlyStore && needed == 1 && linked && refill == -1 && newRule == 0 && oldRule == 1;
            }
            case "previewFob": {
                // HQ at -13; FOB at -4, linked to the HQ by supply units at -10,0 and -7,0. A truck carrying supplies at 1,2
                // (inside the FOB's supply) is moved forward to 7,2 by the player: picked (the preview), then made.
                List<string> r = new List<string>();
                Building fob = Rig.At(-4, 0).building; fob.supplies = 4;
                Put("Supplies", a, -10, 0); Put("Supplies", a, -7, 0);
                Unit truck = Put("Truck", a, 1, 2); Unit cargo = Put("Supplies", a, 1, 3);
                Transporting.Load(truck, cargo);
                foreach (HexTile t in Rig.hg.tiles) if (t.unitPlacement == null) { t.unitPlacement = Rig.New<Transform>(); t.unitPlacement.position = new Vector3(t.x, t.y, 0f); }
                HexTile dest = Rig.At(7, 2);
                SupplyNetwork.Result now = SupplyNetwork.Solve(a, false);
                bool start = now.connectedBuildings.Contains(fob) && now.connectedHubs.Contains(cargo.Hub);
                r.Add("FOB linked " + now.connectedBuildings.Contains(fob) + ", truck's supplies " + (now.connectedHubs.Contains(cargo.Hub) ? "linked" : "cut off"));
                // The player's view: Red plays the AI here, Blue is the player's (the overlay shows only a human's unit).
                bool wasHumanAI = a.GetComponent<AIPlayer>() != null;
                AIPlayer blueAI = a.GetComponent<AIPlayer>();
                if (blueAI != null) blueAI.enabled = false;
                SupplyOverlay overlay = Rig.New<SupplyOverlay>();
                System.Reflection.MethodInfo update = typeof(SupplyOverlay).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                bool human = FogOfWar.IsHuman(a);
                gc.activeUnit = truck;
                dest.state = UIState.MOVE_CONFIRM;
                bool previewOk;
                try { update.Invoke(overlay, null); previewOk = dest.isSupplied(a); }
                finally { dest.state = UIState.FREE; gc.activeUnit = null; update.Invoke(overlay, null); }
                r.Add("Blue is a human here " + human + "; the player's preview: destination " + (previewOk ? "supplied" : "UNSUPPLIED"));
                // The move made.
                List<HexTile> drive = new List<HexTile>(); for (int x = 1; x <= 7; x++) drive.Add(Rig.At(x, 2));
                truck.MoveAlong(drive, false, false); truck.FinishActive();
                SupplyNetwork.Result after = SupplyNetwork.Solve(a, false);
                bool afterOk = after.connectedHubs.Contains(cargo.Hub) && after.IsSupplied(dest);
                r.Add("after the move: " + (after.connectedHubs.Contains(cargo.Hub) ? "linked" : "CUT OFF") + ", destination " + (after.IsSupplied(dest) ? "supplied" : "UNSUPPLIED"));
                // An AI turn played on the main thread leaves the player's preview as it was.
                bool restored = true;
                if (blueAI != null) {
                    blueAI.enabled = true; blueAI.PlayWholeTurn(); blueAI.enabled = false;
                    if (truck.tile != null && cargo.trasnporter == truck) {
                        HexTile d2 = null; foreach (HexTile n in truck.tile.neighbors) if (n != null && n.occupier == null && n.terrain != null && n.terrain.elevation >= 0) { d2 = n; break; }
                        if (d2 != null) {
                            gc.activeUnit = truck; d2.state = UIState.MOVE_CONFIRM;
                            try { update.Invoke(overlay, null); restored = d2.isSupplied(a) == SupplyNetwork.SolveMoved(a, new Dictionary<SupplyPoint, HexTile> { { cargo.Hub, d2 } }).IsSupplied(d2); }
                            finally { d2.state = UIState.FREE; gc.activeUnit = null; update.Invoke(overlay, null); }
                            r.Add("after an AI turn thought on the main thread, the preview matches the game's rules " + restored);
                        }
                    }
                }
                detail = string.Join(" | ", r.ToArray());
                return start && human && previewOk && afterOk && restored;
            }
            case "fogSupplyShown": {
                // HQ at -13; FOB at -4 linked to it by supply units at -10,0 and -7,0; a Blue grunt at 3,0 at the edge of the
                // FOB's supply. A line of Red grunts at x=-1 (y -3..3), out of Blue's sight, cuts the FOB's supply to the grunt. Blue is the screen's player.
                List<string> r = new List<string>();
                Building fob = Rig.At(-4, 0).building; fob.supplies = 4;
                Put("Supplies", a, -10, 0); Put("Supplies", a, -7, 0);
                Unit g = Put("Grunt", a, 3, 0);
                AIPlayer blueAI = a.GetComponent<AIPlayer>(); if (blueAI != null) blueAI.enabled = false;
                gc.activePlayer = a;
                SupplyNetwork.Solve(a, false);
                bool before = g.tile.isSupplied(a) && g.tile.isSuppliedShown(a);
                List<Unit> wall = new List<Unit>();
                for (int y = -3; y <= 3; y++) wall.Add(Put("Grunt", o, -1, y));
                gc.fogOfWar = true; FogOfWar.Reset(); FogOfWar.Changed(); FogOfWar.Refresh();
                bool unseen = true; foreach (Unit w in wall) if (FogOfWar.Sees(a, w)) unseen = false;
                SupplyNetwork.Solve(a, false);
                bool trulyCut = !g.tile.isSupplied(a), shownSupplied = g.tile.isSuppliedShown(a);
                r.Add("viewer " + (FogOfWar.Viewer != null ? FogOfWar.Viewer.personal_name : "none") + "; before the wall the grunt is supplied " + before + "; the wall unseen by Blue " + unseen + ": truly cut off " + trulyCut + " (its upkeep goes by this), shown supplied " + shownSupplied + " (the screen does not give the wall away)");
                // Seen: a Blue recon beside the wall.
                Unit rec = Put("Recon", a, 0, 1);
                FogOfWar.Changed(); FogOfWar.Refresh();
                SupplyNetwork.Solve(a, false);
                bool seenNow = FogOfWar.Sees(a, wall[3]), shownCut = !g.tile.isSuppliedShown(a);
                r.Add("a Blue recon beside it sees the wall " + seenNow + ": shown cut off " + shownCut);
                // Red's supply: a Red supply unit at (10,0) linked to Red's HQ, out of Blue's sight, supplies (7,0), which a
                // Blue recon at (5,0) sees: it shows in Red's supply.
                Unit rs = Put("Supplies", o, 10, 0); Unit rec2 = Put("Recon", a, 5, 0); rec2.visionRange = 2;
                FogOfWar.Changed(); FogOfWar.Refresh();
                SupplyNetwork.Solve(o, false);
                HexTile wallTile = Rig.At(7, 0);
                bool rsHidden = !FogOfWar.Sees(a, rs), wallSeen = FogOfWar.TileVisible(a, wallTile);
                bool redTrue = wallTile.isSupplied(o), redShown = wallTile.isSuppliedShown(o);
                r.Add("Red supplies at (10,0) hidden from Blue " + rsHidden + "; (7,0) seen " + wallSeen + ", truly in Red supply " + redTrue + ", shown in Red supply " + redShown);
                // Fog off: the screen shows the true network.
                gc.fogOfWar = false; FogOfWar.Reset(); FogOfWar.Refresh();
                SupplyNetwork.Solve(a, false);
                bool fogOff = g.tile.isSuppliedShown(a) == g.tile.isSupplied(a);
                r.Add("fog off: shown as it is " + fogOff);
                if (blueAI != null) blueAI.enabled = true;
                detail = string.Join(" | ", r.ToArray());
                return before && unseen && trulyCut && shownSupplied && seenNow && shownCut && fogOff && rsHidden && wallSeen && redTrue && redShown;
            }
            case "fobStorePips": {
                // A FOB (Blue) with 3 of 6 in store, Blue's HQ, and a Red FOB with 5. Blue is the screen's player.
                Building fob = Rig.At(-4, 0).building, hq = GameController.HQOf(a), red = Rig.At(4, 0).building;
                fob.supplies = 3; red.supplies = 5;
                AIPlayer blueAI = a.GetComponent<AIPlayer>(); if (blueAI != null) blueAI.enabled = false;
                gc.activePlayer = a;
                List<string> r = new List<string>();
                Func<Building, string> row = bb => { string o2 = ""; for (int i = 0; i < 7; i++) { SupplyDot.State st = SupplyDot.StateOf(bb, i); o2 += st == SupplyDot.State.Filled ? "o" : st == SupplyDot.State.Empty ? "." : " "; } return "[" + o2 + "]"; };
                string d1 = row(fob);
                fob.SpendSupply(1); string d2 = row(fob);
                fob.Replenish(3); string d3 = row(fob);
                string dh = row(hq);
                r.Add("Blue FOB pips (o filled, . room, blank hidden) 3 of 6: " + d1 + "; spends one: " + d2 + "; refills three: " + d3 + "; the HQ: " + dh);
                // A pip as it is set up on a building: the circle's own sprite, faded toward its state.
                SupplyDot dot = Rig.New<SupplyDot>(); dot.building = fob; dot.gameObject.name = "circle (4)";
                SpriteRenderer sr = dot.gameObject.AddComponent<SpriteRenderer>(); sr.color = Color.white;
                System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(SupplyDot).GetMethod("Start", bf).Invoke(dot, null);
                for (int i = 0; i < 200; i++) typeof(SupplyDot).GetMethod("Update", bf).Invoke(dot, null);
                bool dotFilled = dot.iter == 4 && sr.color.a > 0.9f && sr.color.g > 0.8f;
                fob.supplies = 2;
                for (int i = 0; i < 200; i++) typeof(SupplyDot).GetMethod("Update", bf).Invoke(dot, null);
                bool dotEmpty = sr.color.a < 0.3f && sr.color.a > 0.2f;
                fob.supplies = 5;
                r.Add("pip 'circle (4)': place " + dot.iter + ", with 5 in store filled " + dotFilled + ", with 2 faint " + dotEmpty);
                gc.fogOfWar = true; FogOfWar.Reset(); FogOfWar.Changed(); FogOfWar.Refresh();
                bool ownShown = SupplyDot.ShownTo(fob), redHidden = !SupplyDot.ShownTo(red);
                Unit spot = Put("Recon", a, 2, 0); FogOfWar.Changed(); FogOfWar.Refresh();
                bool redSeen = SupplyDot.ShownTo(red);
                gc.fogOfWar = false; FogOfWar.Reset();
                r.Add("fog: our own FOB's shown " + ownShown + ", Red's FOB out of sight hidden " + redHidden + ", in sight shown " + redSeen);
                if (blueAI != null) blueAI.enabled = true;
                detail = string.Join(" | ", r.ToArray());
                return d1 == "[ooo... ]" && d2 == "[oo.... ]" && d3 == "[ooooo. ]" && dh == "[       ]" && dotFilled && dotEmpty && ownShown && redHidden && redSeen;
            }
            case "assaultRide": {
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) { AIPlayer.Trace = Console.WriteLine; HexTactica.AI.AIAssault.Debug = Console.WriteLine; }
                // Blue: an APC with two grunts aboard at (-6,0) and a tank at (-5,1); an empty APC at (-5,4) beside a grunt
                // at (-6,4). Red: an artillery piece at (5,0), standing. Blue plays four turns (Red does nothing).
                Unit apc = Rig.At(-6, 0).occupier, apc2 = Rig.At(-5, 4).occupier, walker = Rig.At(-6, 4).occupier;
                List<Unit> riders = new List<Unit>(apc.transported);
                List<string> r = new List<string>();
                int earlyDrops = 0, struckOnLanding = 0, landed = 0;
                HashSet<Unit> aboardAtStart = new HashSet<Unit>();
                HashSet<Unit> attackedThisTurn = new HashSet<Unit>();
                Action<Unit, Unit, bool> att = (x, d, sur) => { if (x.owner == a) attackedThisTurn.Add(x); };
                Unit.Attacking += att;
                List<string> why = new List<string>();
                AIPlayer bai = a.GetComponent<AIPlayer>();
                System.Action<Unit, HexTactica.AI.AIAction, bool> dec = (u, act, did) => { if (did && (act.kind == HexTactica.AI.AIActionKind.Unload || act.kind == HexTactica.AI.AIActionKind.Load || riders.Contains(u) || u == apc)) why.Add(u.name.Replace("(Clone)", "") + " " + act.kind + " " + Pos(act.destination) + ": " + act.reason); };
                if (bai != null) bai.onDecision += dec;
                bool walkerBoarded = false;
                try {
                    for (int turn = 0; turn < 8 && !gc.gameOver; turn++) {
                        bool blue = gc.activePlayer == a;
                        if (blue) { aboardAtStart.Clear(); foreach (Unit u in riders) if (u.trasnporter != null) aboardAtStart.Add(u); attackedThisTurn.Clear(); }
                        ScenarioFile.PlayTurn();
                        if (!blue) continue;
                        string line = "Blue turn: APC at " + Pos(apc.tile) + " carrying " + apc.transported.Count;
                        foreach (Unit u in riders) {
                            if (!aboardAtStart.Contains(u) || u.trasnporter != null || u.tile == null) continue;
                            landed++;
                            int nearest = int.MaxValue; foreach (Unit e in o.units) if (e != null && e.tile != null) nearest = Math.Min(nearest, u.tile.Distance(e.tile));
                            bool struck = attackedThisTurn.Contains(u);
                            // (With no enemy left, putting troops down is no early drop: there is nothing to strike.)
                            if (struck) struckOnLanding++; else if (nearest > 1 && nearest != int.MaxValue) earlyDrops++;
                            line += "; " + u.name.Replace("(Clone)", "") + " put down at " + Pos(u.tile) + (struck ? ", attacked the same turn" : ", did not attack (" + nearest + " from the nearest enemy)");
                        }
                        if (walker.trasnporter == apc2) walkerBoarded = true;
                        r.Add(line + "; walker " + (walker.trasnporter != null ? "aboard" : "at " + Pos(walker.tile)));
                    }
                } finally { Unit.Attacking -= att; if (bai != null) bai.onDecision -= dec; }
                if (Environment.GetEnvironmentVariable("WHY") != null) foreach (string w in why) Console.WriteLine("    " + w);
                int rodeUp = 0, assaultDrops = 0, otherDrops = 0;
                foreach (string w in why) {
                    if (w.Contains("assault ride: moves up")) rodeUp++;
                    if (w.StartsWith("APC Unload") || w.Contains(" Unload ")) { if (w.Contains("assault:") || w.Contains("to take it")) assaultDrops++; else if (!w.Contains("holds the line") && !w.Contains("at the front line") && !w.Contains("not its assault ride")) otherDrops++; }
                }
                detail = string.Join(" | ", r.ToArray()) + " || moved up with riders aboard " + rodeUp + " times; put down to strike " + assaultDrops + ", put down otherwise " + otherDrops + "; landed " + landed + " (attacked the turn they landed " + struckOnLanding + ": the rider's own call); walker boarded " + walkerBoarded;
                return landed > 0 && assaultDrops >= 1 && otherDrops == 0;
            }
            case "armedTransport": {
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) { AIPlayer.Trace = Console.WriteLine; HexTactica.AI.AIAssault.Debug = Console.WriteLine; }
                Unit rc = Rig.At(-3, 0).occupier;
                Unit g = rc.transported.Count > 0 ? rc.transported[0] : null;
                g.active = true;
                // The transport's own choice (no coordinated plan, which may also send the recon in and set its ride aside).
                AIProfile ap0 = a.GetComponent<AIPlayer>().Profile;
                List<string> events = new List<string>();
                Action<Unit, Unit, bool> att = (x, d, sur) => { if (x.owner == a) events.Add(GameController.UnitName(x) + " fires" + (x == g ? (g.trasnporter == null ? " (off)" : " (aboard)") : "")); };
                Unit.Attacking += att;
                AIPlayer bai = a.GetComponent<AIPlayer>();
                System.Action<Unit, HexTactica.AI.AIAction, bool> dec = (u, act, did) => { if (did && (u == rc || u == g)) events.Add(GameController.UnitName(u) + " " + act.kind + ": " + act.reason); };
                if (bai != null) bai.onDecision += dec;
                try { ScenarioFile.PlayTurn(); }
                finally { Unit.Attacking -= att; if (bai != null) bai.onDecision -= dec; }
                int reconFired = events.FindIndex(x => x == "Recon fires");
                int gruntOff = events.FindIndex(x => x.StartsWith("Grunt") && !x.Contains("aboard") || x.StartsWith("Recon Unload"));
                bool firstFire = reconFired >= 0 && (gruntOff < 0 || reconFired < gruntOff) && g.trasnporter == null;
                // A fallback: the recon (having acted) with the grunt aboard again, its assault move set aside.
                if (g.tile != null && rc.tile != null && g.trasnporter == null && Transporting.CanCarry(rc, g)) {
                    HexTile beside = rc.tile.neighbors.Find(t => t != null && (t.occupier == null || t.occupier == g) && g.CanEnter(t));
                    if (beside != null && g.tile.Distance(rc.tile) > 1) g.TeleportTo(beside, 0);
                    Transporting.Load(rc, g);
                }
                rc.active = false; g.active = true;
                HexTactica.AI.AIAssault.FellBack.Clear(); HexTactica.AI.AIAssault.FellBack.Add(rc);
                AIAction? fb = null;
                if (g.trasnporter == rc) fb = HexTactica.AI.AIAssault.RiderAction(g, true, new AIMap(a, bai.Profile), bai.Profile);
                HexTactica.AI.AIAssault.FellBack.Clear();
                bool fellBackOut = fb.HasValue && fb.Value.kind == HexTactica.AI.AIActionKind.Unload && fb.Value.reason.Contains("not its assault ride");
                detail = string.Join(" / ", events.ToArray()) + " || the recon fired before its rider got off, the rider off by the end of the turn: " + firstFire
                       + "; fallback: " + (fb.HasValue ? fb.Value.kind + " - " + fb.Value.reason : g.trasnporter == rc ? "nothing" : "(could not put the grunt back aboard)");
                return firstFire && fellBackOut;
            }
            case "capturerLives": {
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) { AIPlayer.Trace = Console.WriteLine; HexTactica.AI.AIAssault.Debug = Console.WriteLine; HexTactica.AI.AICampaign.Debug = Console.WriteLine; }
                Unit hurt = Rig.At(3, 3).occupier; hurt.Strength = 2;
                Building hq = GameController.HQOf(o);
                AIPlayer bai = a.GetComponent<AIPlayer>();
                AIMap m0 = new AIMap(a, bai.Profile);
                int weak = HexTactica.AI.AIAssault.TurnsToTake(hurt, hq, m0);
                List<string> r = new List<string> { "strength 2: turns to take the HQ " + weak };
                bool stoodOnHq = false, mended = false;
                for (int turn = 0; turn < 8 && !gc.gameOver; turn++) {
                    bool blue = gc.activePlayer == a;
                    ScenarioFile.PlayTurn();
                    if (!blue) continue;
                    if (hurt.tile == hq.tile) stoodOnHq = true;
                    if (hurt.tile != null && hurt.trasnporter == null && Building.ServicingUnder(hurt) != null) mended = true;
                    r.Add("grunt " + (hurt.Strength <= 0 ? "dead" : hurt.trasnporter != null ? "aboard at " + Pos(hurt.tile) : "at " + Pos(hurt.tile)) + " strength " + hurt.Strength);
                    if (mended) break;
                }
                // Full strength, same place: it can take it in time.
                Unit fresh = Put("Grunt", a, 4, 1);
                int strong = HexTactica.AI.AIAssault.TurnsToTake(fresh, hq, new AIMap(a, bai.Profile));
                r.Add("a full-strength grunt at (4,1): turns to take the HQ " + strong);
                detail = string.Join(" | ", r.ToArray());
                return weak < 0 && !stoodOnHq && mended && hurt.Strength > 0 && strong >= 0;
            }
            case "gatherWithBody": {
                AIPlayer bai = a.GetComponent<AIPlayer>();
                List<string> lines = new List<string>();
                Action<string> was = AIPlayer.Trace;
                AIPlayer.Trace = x => { if (x.Contains(" front Front(") || x.Contains(" objective ") && x.Contains("gone in")) lines.Add(x.Trim()); if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) Console.WriteLine(x); };
                try { ScenarioFile.PlayTurn(); } finally { AIPlayer.Trace = was; }
                List<string> fronts = lines.FindAll(x => x.StartsWith("front Front("));
                bool anyAggressiveNoStaging = fronts.Exists(x => x.Contains(" Aggressive ") && !x.Contains("staging at") && !x.Contains("ratio 99"));
                bool gather = fronts.Exists(x => x.Contains("staging at"));
                detail = string.Join(" / ", lines.ToArray());
                return gather && !lines.Exists(x => x.Contains("gone in"));
            }
            case "medevac": {
                // Blue: a grunt down to strength 2 at (0,-5), an empty APC at (-1,-4), Blue's city at (-6,4), out of the grunt's
                // walk for three turns. Red: a grunt far off.
                Unit hurt = Rig.At(0, -5).occupier; Building city = Rig.At(-6, 4).building;
                hurt.Strength = 2;
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) { AIPlayer.Trace = Console.WriteLine; HexTactica.AI.AIAssault.Debug = Console.WriteLine; }
                List<string> r = new List<string>();
                int reachedTurn = -1, blueTurns = 0; bool leftHurt = false;
                for (int turn = 0; turn < 12 && !gc.gameOver; turn++) {
                    bool blue = gc.activePlayer == a;
                    ScenarioFile.PlayTurn();
                    if (!blue) continue;
                    blueTurns++;
                    bool on = hurt.trasnporter == null && hurt.tile != null && Building.ServicingUnder(hurt) != null;
                    if (on && reachedTurn < 0) reachedTurn = blueTurns;
                    else if (reachedTurn > 0 && hurt.trasnporter != null && hurt.Strength <= 3 && Building.ServicingUnder(hurt.trasnporter) == null) leftHurt = true;
                    r.Add("grunt " + (hurt.trasnporter != null ? "aboard" : "at " + Pos(hurt.tile)) + " strength " + hurt.Strength);
                }
                detail = string.Join(" | ", r.ToArray()) + "; on a building that mends it from Blue turn " + reachedTurn + (leftHurt ? ", but RODE OFF while still wounded" : ", and did not ride off while wounded (whether it stays on the building is its own call)");
                return reachedTurn > 0 && reachedTurn <= 3 && !leftHurt;
            }
            case "buyInfantry": {
                // The whole AI plays three Blue turns from 6000 gold; what it buys, turn by turn.
                List<string> said = new List<string>(), r = new List<string>();
                Action<string> was = AIPlayer.Trace;
                bool showAll = Environment.GetEnvironmentVariable("CHECKTRACE") != null;
                AIPlayer.Trace = x => { if (showAll) Console.WriteLine(x); if (x.Contains(" buys ") || x.Contains("budget") || x.Contains("objective ")) said.Add(x.Trim()); };
                if (Environment.GetEnvironmentVariable("VALUES") != null) { AIProduction.ValueTrace = Console.WriteLine; AIProduction.ValueFilter = Environment.GetEnvironmentVariable("VALUES"); }
                l.ai.money = 6000;
                HashSet<Unit> had = new HashSet<Unit>(l.ai.units);
                int capturers = 0, blueTurns = 0;
                try {
                    for (int turn = 0; turn < 8 && blueTurns < 3 && !gc.gameOver; turn++) {
                        bool blue = gc.activePlayer == l.ai;
                        int before = l.ai.money;
                        said.Clear();
                        ScenarioFile.PlayTurn();
                        if (!blue) continue;
                        blueTurns++;
                        List<string> got = new List<string>();
                        foreach (Unit u in l.ai.units) if (u != null && !had.Contains(u)) { had.Add(u); got.Add(GameController.UnitName(u)); if (Building.CanCapture(u)) capturers++; }
                        r.Add("turn " + blueTurns + " (" + before + " gold): " + (got.Count > 0 ? string.Join(", ", got.ToArray()) : "nothing")
                              + " [" + string.Join(" / ", said.FindAll(x => x.Contains(" buys ") || x.Contains("builds nothing")).ToArray()) + "]");
                    }
                } finally { AIPlayer.Trace = was; AIProduction.ValueTrace = null; }
                int held = 0; foreach (Building bb in Building.all) if (bb != null && bb.owner == l.ai) held++;
                detail = string.Join(" | ", r.ToArray()) + "; units that can capture bought: " + capturers + "; buildings held after: " + held;
                return capturers > 0;
            }
            case "buyTransports": {
                Building bas = Rig.At(-11, 0).building;
                List<string> r = new List<string>();
                bool boughtTransport = false;
                Func<int, string> buy = money => {
                    l.ai.money = money;
                    AIPlayer ap = l.ai.GetComponent<AIPlayer>();
                    List<string> said = new List<string>();
                    if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) AIProduction.DebugValues = Console.WriteLine;
                    if (Environment.GetEnvironmentVariable("VALUES") != null) { AIProduction.ValueTrace = Console.WriteLine; AIProduction.ValueFilter = Environment.GetEnvironmentVariable("VALUES"); }
                    List<Unit> got = AIProduction.Buy(l.ai, new AIMap(l.ai, ap.Profile), ap.Profile, x => said.Add(x.Trim()));
                    foreach (Unit u in got) if (Transporting.IsTransport(u) && u.Firepower <= 1) boughtTransport = true;   // by what it does, not its name
                    string res = string.Join(", ", got.ConvertAll(u => GameController.UnitName(u) + " at " + Pos(u.tile)).ToArray());
                    return res + " [" + string.Join(" / ", said.FindAll(x => x.Contains(" buys ") || x.Contains("riders waiting")).ToArray()) + "]";
                };
                string first = buy(20000);
                bool apc = boughtTransport;
                r.Add("four grunts far from the fight, 20000 gold: " + first);
                // An empty APC beside the base, and no riders waiting beyond its room: what is bought there fills it.
                foreach (Unit u in new List<Unit>(l.ai.units)) if (u != null && u.tile != null && (u.tile.building != null || (GameController.UnitName(u) == "Grunt" && u.tile.Distance(bas.tile) > 1))) u.Perish();
                string second = buy(5000);
                bool fill = second.Contains("fills the transport");
                r.Add("then, an APC empty beside the base, 5000 gold: " + second);
                detail = string.Join(" | ", r.ToArray());
                return apc && fill;
            }
            case "coverByTarget": {
                Unit truck = Rig.At(-4, 0).occupier, gun = Rig.At(-4, 2).occupier;
                Func<bool, Unit, int> cover = (on, x) => {
                    AIProfile prof = ScriptableObject.CreateInstance<AIProfile>();
                    HexTactica.AI.AIMap m = new HexTactica.AI.AIMap(l.ai, prof);
                    return HexTactica.AI.AIWorld.Evaluate(x, x.tile, new AttackAppraisal(), m, m.replyCache, false, true).gunCover;
                };
                int tOn = cover(true, truck), gOn = cover(true, gun);
                detail = "missile truck covers " + tOn + " tiles; artillery " + gOn;
                return tOn == 0 && gOn > 0;
            }
            case "selfPreservationScope": {
                Unit grunt = Rig.At(-4, 0).occupier, tank = Rig.At(-4, 2).occupier, gun = Rig.At(-5, 2).occupier, truck = Rig.At(-5, 1).occupier, sup = Rig.At(-6, 1).occupier;
                Func<Unit, bool> sh = HexTactica.AI.AIWorld.ShieldsForTest;
                bool scope = sh(grunt) && sh(tank) && !sh(gun) && !sh(truck) && !sh(sup);
                HexTile exposed = Rig.At(-2, 0);
                Func<bool, float> risk = disc => {
                    AIProfile prof = ScriptableObject.CreateInstance<AIProfile>(); prof.selfPreservationWeight = 1f;
                    HexTactica.AI.AIMap m = new HexTactica.AI.AIMap(l.ai, prof);
                    return HexTactica.AI.AIWorld.Evaluate(truck, exposed, new AttackAppraisal(), m, m.replyCache, false, true).selfRisk;
                };
                float on = risk(true);
                detail = "shields: grunt " + sh(grunt) + ", tank " + sh(tank) + ", artillery " + sh(gun) + ", missile truck " + sh(truck) + ", supplies " + sh(sup) + "; the truck at " + Pos(exposed) + " pays " + on.ToString("0") + " for its exposure priced as the reply prices it";
                return scope && on > 0f;
            }
            case "landStepsLayout": {
                HexTile bridge = Rig.At(0, 0), west = Rig.At(-1, 0), east = Rig.At(1, 0);
                AIProfile prof = ScriptableObject.CreateInstance<AIProfile>();
                HexTactica.AI.AIMap m1 = new HexTactica.AI.AIMap(l.ai, prof);
                Dictionary<HexTile, int> before = m1.LandSteps(east);
                int over; bool reached = before.TryGetValue(west, out over);
                HexTactica.AI.AIMap m2 = new HexTactica.AI.AIMap(l.ai, prof);
                bool shared = ReferenceEquals(m2.LandSteps(east), before);
                string was = bridge.terrain.name;
                bridge.DestroyTerrain();
                HexTactica.AI.AIMap m3 = new HexTactica.AI.AIMap(l.ai, prof);
                Dictionary<HexTile, int> after = m3.LandSteps(east);
                bool cut = !after.ContainsKey(west);
                detail = "west shore " + (reached ? over + " steps" : "unreachable") + " from the east over the " + was + "; another read shares the table " + shared + "; bridge now " + bridge.terrain.name + ": west shore " + (cut ? "unreachable" : after[west] + " steps") + " (a new table " + !ReferenceEquals(after, before) + ")";
                return reached && over == 2 && shared && cut && !ReferenceEquals(after, before);
            }
            case "transportCargo": {
                Unit apc = Rig.At(0, 0).occupier, ga = Rig.At(1, 0).occupier, gb = Rig.At(-1, 0).occupier, gc3 = Rig.At(0, 1).occupier;
                Unit plane = Rig.At(-3, 3).occupier, para = Rig.At(-2, 3).occupier;
                foreach (HexTile t in Rig.hg.tiles) if (t.unitPlacement == null) { t.unitPlacement = Rig.New<Transform>(); t.unitPlacement.position = new Vector3(t.x, t.y, 0f); }
                System.Reflection.MethodInfo updateM = typeof(Unit).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                // One frame of the unit's sprite logic. (The harness has no animator: the walking part at the end of a
                // frame fails on it, after the parts checked here have run.)
                Action<Unit> frame = u => { try { updateM.Invoke(u, null); } catch (System.Reflection.TargetInvocationException e) { if (!(e.InnerException is NullReferenceException)) throw; } };
                object update = updateM;
                List<string> r = new List<string>();
                // Slots.
                Transporting.Load(apc, ga); ga.FinishBoarding();
                Transporting.Load(apc, gb); gb.FinishBoarding();
                int sa = ga.cargoSlot, sb = gb.cargoSlot;
                List<HexTile> off = Transporting.UnloadOptions(apc, ga);
                bool unloaded = off.Count > 0 && Transporting.Unload(apc, ga, off[0]);
                Transporting.Load(apc, gc3); gc3.FinishBoarding();
                bool slots = sa == 0 && sb == 1 && unloaded && gc3.cargoSlot == 0 && gb.cargoSlot == 1;
                r.Add("slots: first " + sa + ", second " + sb + "; first off " + unloaded + "; third boards into " + gc3.cargoSlot);
                // Dying aboard.
                gb.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
                apc.Perish();
                if (update != null) for (int i = 0; i < 3; i++) frame(gb);
                bool small = Math.Abs(gb.transform.localScale.x - 0.45f) < 1e-4f && gb.Strength <= 0;
                r.Add("its transport destroyed, the second grunt's scale " + gb.transform.localScale.x.ToString("0.00") + " (dead " + (gb.Strength <= 0) + ")");
                // The drop.
                bool boarded = Transporting.Load(plane, para); para.FinishBoarding();
                List<HexTile> drop = Transporting.UnloadOptions(plane, para);
                bool dropped = boarded && drop.Count > 0 && Transporting.Unload(plane, para, drop[0]);
                Vector3 land = para.tile != null ? para.tile.unitPlacement.position : new Vector3();
                float startAbove = para.transform.position.y - land.y;
                if (update != null) frame(para);
                float after = Vector2.Distance(para.transform.position, land);
                bool drops = dropped && Math.Abs(startAbove - plane.dropHeight) < 1e-3f && after < 0.02f;
                r.Add("boarded the plane " + boarded + ", put down " + dropped + ": appears " + startAbove.ToString("0.00") + " above its tile (dropHeight " + plane.dropHeight + "), after coming down " + after.ToString("0.00") + " away");
                detail = string.Join("; ", r.ToArray());
                return slots && small && drops;
            }
            case "campaignChokepoints": {
                AIProfile prof = ScriptableObject.CreateInstance<AIProfile>();
                AIMap m = new AIMap(l.ai, prof);
                List<KeyValuePair<HexTile, string>> places = AICampaign.Places(m);
                List<string> ends = new List<string>(); bool west = false, east = false;
                foreach (KeyValuePair<HexTile, string> kv in places) if (kv.Value == "chokepoint end") { ends.Add(Pos(kv.Key)); if (kv.Key.x < 0) west = true; if (kv.Key.x > 0) east = true; }
                AICampaign c = AICampaign.Plan(m, prof, null);
                int holds = c.considered.FindAll(x => x.kind == CampaignKind.Hold).Count;
                // (The board is in plain sight here, so the crossing counts as held and no Hold objective is raised for it:
                // one is raised only for ground gone dark under the fog, or with the enemy near it out of our sight.)
                detail = "chokepoint ends " + string.Join(" ", ends.ToArray()) + "; Hold objectives considered " + holds + " (in plain sight: none)";
                return west && east && holds == 0;
            }
            case "aiFightFirst": {
                Unit g = Rig.At(0, 0).occupier, e = Rig.At(1, -1).occupier;
                l.ai.Resupply(); l.other.Resupply();
                AIProfile prof = l.ai.GetComponent<AIPlayer>().Profile;
                HexTactica.AI.AIMap m = new HexTactica.AI.AIMap(l.ai, prof);
                AIAction act = AITactical.Decide(g, m, prof, Posture.Aggressive);
                detail = "the grunt: " + act.kind + (act.destination != null ? " to (" + act.destination.x + "," + act.destination.y + ")" : "") + " - " + act.reason;
                // Not onto the city while it has an attack open to it: it attacks, or does whatever else is best.
                return act.kind == AIActionKind.Attack || act.kind == AIActionKind.MoveAndAttack || act.destination != Rig.At(1, 0);
            }
            case "buildingService": {
                Building far = Rig.At(4, -4).building, near = Rig.At(-5, 1).building;
                Unit gf = Rig.At(4, -4).occupier, gn = Rig.At(-5, 1).occupier;
                foreach (Building b in new[] { far, near }) { b.services = new List<Locomotion> { Locomotion.Boots }; b.producesSupply = false; b.healPerTurn = 1; }
                far.rearm = false; far.refuel = false; far.resupply = true;
                near.healStacksWithSupply = true;
                foreach (Unit g in new[] { gf, gn }) { g.Strength = 3; g.ammo = 0; g.supplies = 2; }
                SupplyNetwork.Result sn = SupplyNetwork.Solve(l.ai, false);
                bool farOut = !sn.IsSupplied(Rig.At(4, -4)), nearIn = sn.IsSupplied(Rig.At(-5, 1));
                endTurn(); endTurn();
                detail = "far city (out of supply " + farOut + "): strength " + gf.Strength + ", ammo " + gf.ammo + ", supplies " + gf.supplies + "/" + gf.suppliesMax
                       + "; near city (in supply " + nearIn + ", heal stacks): strength " + gn.Strength + ", ammo " + gn.ammo + "/" + gn.ammoMax;
                return farOut && nearIn && gf.Strength == 4 && gf.ammo == 0 && gf.supplies == gf.suppliesMax && gn.Strength == 5 && gn.ammo == gn.ammoMax;
            }
            case "attritionCutOff": {
                Unit hub = Rig.At(4, 0).occupier, g = Rig.At(4, 1).occupier;
                SupplyNetwork.Result net = SupplyNetwork.Solve(l.ai, false);
                bool cut = net.strandedHubs.Contains(hub.GetComponent<SupplyPoint>()) && !net.IsSupplied(hub.tile) && !net.IsSupplied(g.tile);
                int gs = g.supplies, hs = hub.Strength;
                endTurn(); endTurn();
                detail = "cut off, nothing supplied " + cut + "; grunt supplies " + gs + " -> " + g.supplies + "; supply unit strength " + hs + " -> " + hub.Strength;
                return cut && g.supplies == gs - 1 && hub.Strength == hs;
            }
            case "purchasesOff":
            case "allowedUnits": {
                Building hq = GameController.HQOf(l.ai);
                l.ai.money = 100000;
                List<string> offered = new List<string>();
                foreach (Unit u in hq.Purchasable()) offered.Add(GameController.UnitName(u));
                Unit grunt = null, tank = null;
                foreach (Unit u in hq.buildable) { if (GameController.UnitName(u) == "Grunt") grunt = u; if (GameController.UnitName(u) == "Tank") tank = u; }
                Unit madeTank = Rig.bc.Produce(hq, tank);
                Unit madeGrunt = Rig.bc.Produce(hq, grunt);
                detail = "purchases " + gc.PurchasesSetting + ", allowed '" + gc.AllowedUnitsSetting + "'; offered: " + string.Join(",", offered.ToArray()) + "; tank " + (madeTank != null ? "built" : "refused") + ", grunt " + (madeGrunt != null ? "built" : "refused");
                if (l.meta.Get("check", "") == "purchasesOff") return offered.Count == 0 && madeTank == null && madeGrunt == null;
                return offered.Count == 1 && offered[0] == "Grunt" && madeTank == null && madeGrunt != null;
            }
            case "aiBuys": {
                l.ai.money = 10000;
                List<Unit> had = new List<Unit>(l.ai.units);
                int before = l.ai.units.Count;
                ScenarioFile.PlayTurn();
                List<string> got = new List<string>(); bool bad = false;
                foreach (Unit u in l.ai.units) if (u != null && !had.Contains(u)) got.Add(GameController.UnitName(u) + " at " + Pos(u.tile));
                foreach (Unit u in l.ai.units) { string nm = u != null ? GameController.UnitName(u) : ""; if (nm != "Grunt" && nm != "Tank" && nm != "TempHQ" && nm != "HQ") bad = true; }
                detail = "units " + before + " -> " + l.ai.units.Count + ", money " + l.ai.money + "; bought on the HQ: " + string.Join(",", got.ToArray());
                return l.ai.units.Count > before && !bad && l.ai.money < 10000;
            }
            case "suppression": {
                Unit art = Rig.At(-4, 0).occupier, art2 = Rig.At(-4, 2).occupier, ours = Rig.At(-1, 1).occupier, tank = Rig.At(-3, -2).occupier;
                Unit g = Rig.At(0, 0).occupier, far = Rig.At(0, 3).occupier, vet = Rig.At(-1, -2).occupier, open = Rig.At(2, -2).occupier;
                vet.Veterancy = 3;
                List<string> r = new List<string>(); bool ok = true;
                // 1. Artillery: same damage as ever, no retreat, half the pips left suppressed.
                CombatOutcome pre = CombatMath.Resolve(art, g, art.tile);
                int expectDmg = Math.Min(g.Strength, Unit.calculateDamage(art, g, g.tile, 0));
                HexTile gAt = g.tile;
                art.OrderAttack(g);
                int left = g.Strength;
                bool a1 = pre.defenderSuppressed && !pre.defenderRetreats && !pre.defenderPinned && g.tile == gAt && left == 6 - expectDmg && g.suppressed == (left + 1) / 2;
                r.Add("artillery hit " + expectDmg + ", stays " + (g.tile == gAt) + ", suppressed " + g.suppressed + "/" + left + " " + a1); ok &= a1;
                // 2. Attacked again: answers with strength less the suppressed pips.
                CombatOutcome back = CombatMath.Resolve(ours, g, ours.tile);
                int expectBack = Math.Min(ours.Strength, Unit.calculateDamage(g, ours, null, back.damageToDefender + g.suppressed));
                int sup = g.suppressed; g.suppressed = 0;
                CombatOutcome full = CombatMath.Resolve(ours, g, ours.tile);
                g.suppressed = sup;
                bool a2 = back.damageToAttacker == expectBack && (back.damageToDefender >= left || back.damageToAttacker <= full.damageToAttacker);
                r.Add("its answer " + back.damageToAttacker + " (unsuppressed " + full.damageToAttacker + ") " + a2); ok &= a2;
                // 3. Every pip suppressed: no answer at all.
                g.suppressed = g.Strength;
                CombatOutcome none = CombatMath.Resolve(ours, g, ours.tile);
                bool a3 = none.damageToAttacker == 0;
                r.Add("all suppressed: answer " + none.damageToAttacker + " " + a3); ok &= a3;
                // 4. A second hit stacks on what is left.
                Unit two = far; int s0;
                art.OrderAttack(two); s0 = two.suppressed;
                int st = two.Strength;
                CombatOutcome second = CombatMath.Resolve(art2, two, art2.tile);
                art2.OrderAttack(two);
                bool a4 = s0 > 0 && (!second.defenderSuppressed || two.suppressed == Math.Min(two.Strength, s0 + (two.Strength - s0 + 1) / 2));
                r.Add("second hit: " + s0 + " then " + two.suppressed + " of " + two.Strength + " " + a4); ok &= a4;
                // 5. Too light to push (a veteran): nothing suppressed, nothing moves.
                Unit art3 = art2; art3.attackedThisTurn = false;
                HexTile vAt = vet.tile;
                CombatOutcome lite = CombatMath.Resolve(tank, vet, tank.tile);
                CombatOutcome liteArt = CombatMath.Resolve(art, vet, art.tile);
                bool a5 = !liteArt.defenderSuppressed || liteArt.damageToDefender - vet.Veterancy > 0;
                r.Add("veteran hit by artillery: suppressed " + liteArt.defenderSuppressed + " (damage " + liteArt.damageToDefender + ", veterancy " + vet.Veterancy + ") " + a5); ok &= a5;
                // 6. Its own turn: every pip back.
                l.other.StartTurn();
                bool a6 = g.suppressed == 0 && two.suppressed == 0;
                r.Add("Red's turn starts: suppressed " + g.suppressed + ", " + two.suppressed + " " + a6); ok &= a6;
                // 7. Direct fire still drives back.
                HexTile oAt = open.tile;
                CombatOutcome d = CombatMath.Resolve(tank, open, tank.tile);
                bool a7 = !d.defenderSuppressed && (d.defenderRetreats || d.damageToDefender - open.Veterancy <= d.damageToAttacker || d.defenderDies);
                r.Add("tank on a grunt: retreats " + d.defenderRetreats + ", suppressed " + d.defenderSuppressed + " " + a7); ok &= a7;
                // 8. Indirect is having a minimum range, not a long one: a tank reaching two tiles (minimum 0) still drives back; a gun with a minimum range of 1 suppresses even at range 1.
                int tr = tank.AttackRange, am = art.AttackRange;
                tank.AttackRange = 2; art.AttackRange = 1;
                CombatOutcome longTank = CombatMath.Resolve(tank, open, tank.tile);
                bool a8 = !CombatMath.Suppresses(tank) && CombatMath.Suppresses(art) && !longTank.defenderSuppressed
                          && (longTank.defenderRetreats || longTank.damageToDefender - open.Veterancy <= longTank.damageToAttacker || longTank.defenderDies);
                tank.AttackRange = tr; art.AttackRange = am;
                r.Add("tank with range 2, minimum 0: indirect " + CombatMath.Suppresses(tank) + ", retreats " + longTank.defenderRetreats + "; gun range 1 minimum 1 indirect " + CombatMath.Suppresses(art) + " " + a8); ok &= a8;
                detail = string.Join("; ", r.ToArray());
                return ok;
            }
            case "reconTransport": {
                Unit rc = Rig.At(-3, 0).occupier, g = Rig.At(-2, 0).occupier;
                bool kinds = rc.carries.Count == 1 && rc.carries[0] == Locomotion.Boots;
                bool loaded = Transporting.Load(rc, g);
                g.FinishBoarding();
                rc.TeleportTo(Rig.At(-3, 3), 3);
                List<HexTile> opts = Transporting.UnloadOptions(rc, g);
                bool unloaded = opts.Count > 0 && Transporting.Unload(rc, g, opts[0]);
                detail = "carries " + string.Join(",", rc.carries.ConvertAll(x => x.ToString()).ToArray()) + " (slots " + rc.transportSlots + "); loaded " + loaded + "; " + opts.Count + " tiles to put it down; unloaded " + unloaded;
                return kinds && loaded && unloaded;
            }
            case "alarms": {
                Unit home = Rig.At(-5, 0).occupier, far = Rig.At(5, 0).occupier, tank = Rig.At(-4, 1).occupier;
                l.ai.Resupply();
                home.supplies = 0; far.supplies = 0; tank.fuel = 0; tank.ammo = 0;
                bool homeSupplied = home.tile.isSupplied(l.ai), farSupplied = far.tile.isSupplied(l.ai);
                bool a1 = !UnitAlarmIcon.Starving(home) && UnitAlarmIcon.Starving(far);
                Unit onBase = Rig.At(-3, 3).occupier, overHome = Rig.At(-5, 2).occupier, tankerU = Rig.At(-2, 0).occupier;
                onBase.supplies = 0; overHome.supplies = 0; tankerU.supplies = 0;
                bool airGround = overHome.tile.isSupplied(l.ai);
                bool a4 = airGround && UnitAlarmIcon.Starving(overHome) && !UnitAlarmIcon.Starving(onBase) && UnitAlarmIcon.Starving(tankerU);
                string air = "; fighter over our ground in supply (" + airGround + ") starving " + UnitAlarmIcon.Starving(overHome) + ", on the runway " + UnitAlarmIcon.Starving(onBase) + "; empty tanker " + UnitAlarmIcon.Starving(tankerU);
                float share = UnitAlarmSprites.Instance.lowShare;
                bool a2 = UnitAlarmIcon.Low(0, tank.fuelMax, share) && UnitAlarmIcon.Low(0, tank.ammoMax, share);
                int third = tank.ammoMax / 3;
                bool a3 = UnitAlarmIcon.Low(third, tank.ammoMax, share) && !UnitAlarmIcon.Low(third + 1, tank.ammoMax, share) && !UnitAlarmIcon.Low(tank.fuelMax, tank.fuelMax, share)
                       && !UnitAlarmIcon.Low(0, 0, share);
                detail = "grunt in supply (" + homeSupplied + ") starving " + UnitAlarmIcon.Starving(home) + "; out of supply (" + farSupplied + ") starving " + UnitAlarmIcon.Starving(far)
                       + "; tank empty: fuel and ammo alarms " + a2 + "; ammo " + third + "/" + tank.ammoMax + " alarm, " + (third + 1) + " not, full fuel not, a unit carrying none never " + a3 + air;
                return homeSupplied && !farSupplied && a1 && a2 && a3 && a4;
            }
            case "portraitLayout": {
                var parts = new List<SpriteLayout.Part> {
                    new SpriteLayout.Part { center = new Vector2(0f, 0f), axisX = new Vector2(1f, 0f), axisY = new Vector2(0f, 1f), size = new Vector2(2f, 1f) },
                    new SpriteLayout.Part { center = new Vector2(0.5f, 0.5f), axisX = new Vector2(0f, 1f), axisY = new Vector2(-1f, 0f), size = new Vector2(1f, 1f) },
                    new SpriteLayout.Part { center = new Vector2(-0.5f, 0f), axisX = new Vector2(-0.5f, 0f), axisY = new Vector2(0f, 0.5f), size = new Vector2(0.4f, 0.4f) },
                };
                var p = SpriteLayout.Layout(parts, new Vector2(100f, 100f), 4f);
                Func<float, float, bool> near = (x, y) => Math.Abs(x - y) < 1e-3f;
                bool hull = near(p[0].size.x, 92f) && near(p[0].size.y, 46f) && near(p[0].position.x, 0f) && near(p[0].position.y, -11.5f) && near(p[0].angle, 0f);
                bool turret = near(p[1].position.x, 23f) && near(p[1].position.y, 11.5f) && near(p[1].angle, 90f) && near(p[1].scale.y, 1f);
                bool flag = near(Math.Abs(p[2].angle), 180f) && near(p[2].scale.x, 0.5f) && near(p[2].scale.y, -0.5f) && near(p[2].position.x, -23f);
                bool none = SpriteLayout.Layout(new List<SpriteLayout.Part>(), new Vector2(10f, 10f), 0f).Count == 0;
                detail = "hull " + p[0].size.x + "x" + p[0].size.y + " at " + p[0].position.x + "," + p[0].position.y + "; turret at " + p[1].position.x + "," + p[1].position.y + " " + p[1].angle
                       + "deg; flag " + p[2].angle + "deg scale " + p[2].scale.x + "," + p[2].scale.y + "; empty " + none;
                return hull && turret && flag && none;
            }
            case "fogUnitDetect": {
                PinVision();
                Unit g = Rig.At(-4, 0).occupier, rc = Rig.At(1, 0).occupier;
                rc.stealthRange = 3; g.visionRange = 2;
                int spotted = 0;
                Action<Unit, Player> hook = (u, q) => { if (u == rc && q == a) spotted++; };
                FogOfWar.StealthSpotted += hook;
                try {
                    Action moved = () => { FogOfWar.Changed(); FogOfWar.Refresh(); };
                    moved();
                    bool plain = FogOfWar.Sees(a, rc);
                    g.detectRange = 5; moved();
                    bool at5 = FogOfWar.Sees(a, rc); int first = spotted;
                    moved(); int still = spotted;
                    rc.TeleportTo(Rig.At(2, 0), 0); moved();
                    bool at6 = FogOfWar.Sees(a, rc);
                    rc.TeleportTo(Rig.At(1, 0), 0); moved();
                    detail = "no detector: seen " + plain + "; detectRange 5: 5 off seen " + at5 + ", 6 off seen " + at6 + "; announced " + first + " on first sight, " + still + " while still seen, " + spotted + " after leaving and coming back";
                    return !plain && at5 && !at6 && first == 1 && still == 1 && spotted == 2;
                } finally { FogOfWar.StealthSpotted -= hook; }
            }
            case "fogIntOps": {
                PinVision();
                Unit rc = Rig.At(1, 0).occupier;
                Building radar = Rig.At(-4, 0).building;
                bool prefab = radar != null && radar.detectRange == 5 && radar.visionOverTerrain && radar.visionRange >= 6;
                rc.stealthRange = 3;
                Action moved = () => { FogOfWar.Changed(); FogOfWar.Refresh(); };
                moved();
                bool at3 = FogOfWar.Sees(a, rc);
                rc.TeleportTo(Rig.At(2, 0), 0); moved();
                bool at4 = FogOfWar.Sees(a, rc);
                rc.TeleportTo(Rig.At(1, 0), 0);
                radar.detectRange = 0; moved();
                bool noDetect = FogOfWar.Sees(a, rc);
                radar.detectRange = 5;
                HexTile beyond = Rig.At(0, 0);
                rc.TeleportTo(Rig.At(-1, 2), 0); moved();
                bool over = FogOfWar.TileVisible(a, beyond);
                radar.visionOverTerrain = false; moved();
                bool blocked = !FogOfWar.TileVisible(a, beyond);
                radar.visionOverTerrain = true; moved();
                detail = "prefab detect 5, vision 6 over terrain " + prefab + "; stealthy recon 5 off seen " + at3 + ", 6 off seen " + at4 + ", 5 off without detection seen " + noDetect
                       + "; (0,0) behind the mountain seen " + over + ", without seeing over terrain hidden " + blocked;
                return prefab && at3 && !at4 && !noDetect && over && blocked;
            }
            case "fogVision": {
                PinVision();
                Unit g = Rig.At(-2, 0).occupier, r1 = Rig.At(0, 0).occupier, r2 = Rig.At(1, 0).occupier, rc = Rig.At(2, -3).occupier;
                rc.stealthRange = 3;   // the stealthy recon this check was written for (the prefab's own may be rebalanced)
                Unit art = Rig.At(-2, 4).occupier, apc = Rig.At(4, -4).occupier, cargo = Rig.At(4, -3).occupier;
                Building city = Rig.At(2, 2).building;
                List<string> r = new List<string>();
                Action moved = () => { FogOfWar.Changed(); FogOfWar.Refresh(); };
                moved();
                bool v1 = FogOfWar.Sees(a, r1) && !FogOfWar.Sees(a, r2) && FogOfWar.TileVisible(a, Rig.At(0, 0)) && !FogOfWar.TileVisible(a, Rig.At(1, 0));
                r.Add("vision 2: d2 seen, d3 not " + v1);
                g.visionRange = 4; moved();
                bool v2 = FogOfWar.Sees(a, r2);
                r.Add("vision 4: d3 seen " + v2);
                rc.TeleportTo(Rig.At(1, -1), 0); moved();
                bool s1 = !FogOfWar.Sees(a, rc) && FogOfWar.TileVisible(a, Rig.At(1, -1));
                rc.TeleportTo(Rig.At(0, -1), 0); moved();
                bool s2 = FogOfWar.Sees(a, rc);
                r.Add("stealth 3: d3 in vision unseen " + s1 + ", d2 seen " + s2);
                g.visionRange = 0; rc.TeleportTo(Rig.At(-1, 0), 0); moved();
                bool adj = FogOfWar.Sees(a, rc) && !FogOfWar.Sees(a, r1);
                r.Add("vision 0: adjacent seen, d2 not " + adj);
                rc.stealthRange = 1; rc.TeleportTo(Rig.At(2, 2), 0); moved();
                bool onCity = FogOfWar.Sees(a, rc) && city != null && city.owner == a;
                rc.stealthRange = 3;
                r.Add("on our city (stealth 1, d0) seen " + onCity);
                Transporting.Load(apc, cargo); moved();
                g.visionRange = 2; g.TeleportTo(Rig.At(3, -4), 0); moved();
                bool car = FogOfWar.Sees(a, apc) && FogOfWar.Sees(a, cargo);
                g.TeleportTo(Rig.At(-2, 0), 0); moved();
                car = car && !FogOfWar.Sees(a, apc) && !FogOfWar.Sees(a, cargo);
                r.Add("cargo seen with its APC, hidden with it " + car);
                g.TeleportTo(Rig.At(-2, 0), 0); moved();
                // Fired on from out of sight during Red's turn: seen until the end of Blue's next turn.
                gc.EndTurn();
                bool before = !FogOfWar.Sees(a, art);
                art.OrderAttack(g);
                bool fired = FogOfWar.Sees(a, art);
                gc.EndTurn();
                bool afterRed = FogOfWar.Sees(a, art);
                gc.EndTurn();
                bool afterBlue = !FogOfWar.Sees(a, art);
                r.Add("artillery: unseen " + before + ", fires: seen " + fired + ", Red's turn over: still " + afterRed + ", Blue's over: unseen " + afterBlue);
                // The screen is Red's (the human): Blue's units Red does not see are invisible, Red's units Blue does not see half-faded.
                moved();
                Player viewer = FogOfWar.Viewer;
                g.TeleportTo(Rig.At(-5, 2), 0); moved();   // out of the sight of every Red unit
                bool gHidden = !FogOfWar.Sees(o, g);
                float tg = FogFader.TargetFor(g), tr2 = FogFader.TargetFor(r2), tr1 = FogFader.TargetFor(r1);
                bool r2Hidden = !FogOfWar.Sees(a, r2), r1Seen = FogOfWar.Sees(a, r1);
                // Red's own: only a stealthy unit (the recon) Blue does not see is half-faded; a grunt Blue does not see is drawn as it is.
                rc.TeleportTo(Rig.At(4, -6), 0); moved();   // off Blue's city, out of Blue's sight
                bool rcHidden = !FogOfWar.Sees(a, rc);
                float trc = FogFader.TargetFor(rc);
                bool screen = viewer == o && gHidden && tg == 0f && r2Hidden && tr2 == 1f && tr1 == 1f && rcHidden && trc == FogSettings.Instance.ownHiddenAlpha;
                r.Add("screen (" + (viewer != null ? viewer.personal_name : "none") + "): hidden Blue unit " + tg + ", Red grunt Blue does not see " + tr2 + ", Red recon Blue does not see " + trc + " " + screen);
                gc.fogOfWar = false;
                bool off = FogOfWar.Sees(a, r2) && FogOfWar.Sees(a, cargo) && FogOfWar.Viewer == null && FogFader.TargetFor(g) == 1f;
                gc.fogOfWar = true;
                r.Add("fog off: all seen " + off);
                detail = string.Join("; ", r.ToArray());
                return v1 && v2 && s1 && s2 && adj && onCity && car && before && fired && afterRed && afterBlue && screen && off;
            }
            case "aiThread": {
                List<string> res = new List<string>();
                bool all = true;
                foreach (string map in new[] { "gauntlet-1-challenger-left", "air-gauntlet-1-challenger-left" }) {
                    foreach (bool fog in new[] { false, true }) {
                        int hammered = 0, torn = 0;
                        Func<bool, List<string>> play = threaded => {
                            // As the game plays: its profile (TestProfile.asset, the campaign AI), notes written on the board as the AI thinks.
                            Action<AIProfile> asPlayed = pr => { ApplyProfileAsset(pr); pr.annotateBoard = true; };
                            ScenarioFile.Loaded g = ScenarioFile.Load(Find(map), "Full", asPlayed, asPlayed);
                            foreach (HexTile t in Rig.hg.tiles) if (t.textField == null) t.textField = Rig.New<UnityEngine.UI.Text>();
                            Rig.gc.fogOfWar = fog; FogOfWar.Reset();
                            AIPlayer.HarnessThreads = threaded;
                            // While the AI thinks elsewhere, the main thread does what the game does: move
                            // ranges for clicks, sight, published supply, and checks the real board is whole.
                            int k = 0;
                            AIPlayer.WhileThinking = () => {
                                hammered++;
                                List<Unit> us = new List<Unit>(Rig.uc.units);
                                if (us.Count == 0) return;
                                Unit pick = us[k++ % us.Count];
                                if (pick != null && pick.tile != null && pick.trasnporter == null) {
                                    if (pick.tile.occupier != pick) torn++;
                                    pick.tile.CheckMovementRange(pick);
                                    foreach (Player pl in Rig.gc.players) { FogOfWar.Sees(pl, pick); FogOfWar.TileVisible(pl, pick.tile); pick.tile.isSupplied(pl); }
                                }
                            };
                            List<string> boards = new List<string>();
                            try {
                                for (int turn = 0; turn < 12 && !Rig.gc.gameOver; turn++) {
                                    ScenarioFile.PlayTurn();
                                    List<string> b = new List<string>();
                                    foreach (Unit u in Rig.uc.units) if (u != null && u.tile != null) b.Add(u.owner.personal_name + u.name + "@" + u.tile.x + "," + u.tile.y + "s" + u.Strength + "f" + u.fuel + "a" + u.ammo);
                                    b.Sort(string.CompareOrdinal);
                                    boards.Add(string.Join("|", b.ToArray()));
                                }
                            } finally { AIPlayer.HarnessThreads = false; AIPlayer.WhileThinking = null; }
                            return boards;
                        };
                        List<string> inLine = play(false);
                        int before = UnityEngine.MainThreadOnly.violations;
                        List<string> onThread;
                        try { onThread = play(true); }
                        catch (Exception e) { onThread = new List<string>(); res.Add("thread failed: " + e.GetBaseException().Message); }
                        int offMain = UnityEngine.MainThreadOnly.violations - before;
                        if (offMain > 0) { all = false; res.Add(offMain + " main-thread-only Unity calls from the AI's thread, first: " + UnityEngine.MainThreadOnly.first); }
                        int firstDiff = -1;
                        for (int i = 0; i < Math.Max(inLine.Count, onThread.Count); i++)
                            if (i >= inLine.Count || i >= onThread.Count || inLine[i] != onThread[i]) { firstDiff = i; break; }
                        bool same = firstDiff < 0 && torn == 0 && hammered > 0;
                        all &= same;
                        res.Add(map + (fog ? " fog on" : " fog off") + ": " + inLine.Count + " turns, " + (firstDiff < 0 ? "same board after every turn" : "differs after turn " + firstDiff)
                                + ", main thread read the board " + hammered + " times while the AI thought" + (torn > 0 ? ", saw it torn " + torn + " times" : ""));
                    }
                }
                detail = string.Join("; ", res.ToArray());
                return all;
            }
            case "fogKinds": {
                PinVision();
                Unit g = Rig.At(-2, 0).occupier, r = Rig.At(1, 0).occupier, bf = Rig.At(-5, 4).occupier, rf = Rig.At(0, -5).occupier;
                Unit boat = Rig.At(4, 2).occupier, rg = Rig.At(2, 4).occupier;
                Action moved = () => { FogOfWar.Changed(); FogOfWar.Refresh(); };
                List<string> res = new List<string>();
                HexTile gt = g.tile; TerrainData grass = gt.terrain;
                moved();
                bool flat = !FogOfWar.Sees(a, r);
                gt.terrain = T("Hills"); moved();
                bool hills = FogOfWar.Sees(a, r) && FogOfWar.VisionFrom(g, gt) == g.visionRange + 1;
                gt.terrain = T("Mountains"); r.TeleportTo(Rig.At(2, 0), 0); moved();
                bool mountains = FogOfWar.Sees(a, r) && FogOfWar.VisionFrom(g, gt) == g.visionRange + 2;
                r.TeleportTo(Rig.At(3, -2), 0); moved();   // five off, out of the gunboat's sight too
                bool beyond = !FogOfWar.Sees(a, r);
                bool air = FogOfWar.VisionFrom(bf, gt) == bf.visionRange;
                gt.terrain = grass; moved();
                res.Add("grunt (vision " + g.visionRange + "): d3 unseen on flat " + flat + ", seen from hills " + hills + ", d4 from mountains " + mountains + ", d5 not " + beyond + "; no high ground for aircraft " + air);
                // A stealth jet: unseen by the grunt it is next to; seen by a fighter two tiles off, not three.
                rf.stealthRange = 3; rg.stealthRange = 3;
                rf.TeleportTo(Rig.At(-1, 0), 0); bf.TeleportTo(Rig.At(-4, 6), 0); moved();
                bool jetHidden = !FogOfWar.Sees(a, rf);
                bf.TeleportTo(Rig.At(1, -1), 0); moved();
                bool jetSeen = FogOfWar.Sees(a, rf);
                bf.TeleportTo(Rig.At(2, -2), 0); moved();
                bool jetFar = !FogOfWar.Sees(a, rf);
                bf.TeleportTo(Rig.At(-4, 6), 0); rf.TeleportTo(Rig.At(-3, 3), 0); moved();
                bool jetOverCity = !FogOfWar.Sees(a, rf);
                res.Add("stealth jet: next to a grunt unseen " + jetHidden + ", fighter at 2 sees it " + jetSeen + ", at 3 not " + jetFar + ", over our city unseen " + jetOverCity);
                // A stealthy grunt by the shore: the gunboat next to it does not see it; a grunt two off does; on our city it is seen.
                rg.TeleportTo(Rig.At(3, 2), 0); rf.TeleportTo(Rig.At(0, -5), 0); moved();
                bool boatBlind = !FogOfWar.Sees(a, rg);
                g.TeleportTo(Rig.At(3, 0), 0); moved();
                bool gruntSees = FogOfWar.Sees(a, rg);
                g.TeleportTo(Rig.At(-2, 0), 0); rg.TeleportTo(Rig.At(-3, 3), 0); moved();
                bool onCity = FogOfWar.Sees(a, rg);
                res.Add("stealthy grunt: next to the gunboat unseen " + boatBlind + ", grunt at 2 sees it " + gruntSees + ", on our city seen " + onCity);
                detail = string.Join("; ", res.ToArray());
                return flat && hills && mountains && beyond && air && jetHidden && jetSeen && jetFar && jetOverCity && boatBlind && gruntSees && onCity;
            }
            case "combatScars": {
                Unit tank = Rig.At(-2, 0).occupier, g1 = Rig.At(-1, 0).occupier, b = Rig.At(-2, 3).occupier;
                HexTile from = tank.tile, to = g1.tile;
                foreach (HexTile t in new[] { from, to, Rig.At(-2, 3), Rig.At(-1, 3), Rig.At(0, 3) }) { t.terrain = T("Grassland"); t.damage = 0; }
                g1.Strength = 6;
                CombatOutcome oc = tank.OrderAttack(g1);
                string d1 = "attack: from " + from.damage + ", target " + to.damage + " (answered " + oc.retaliationHappens + ")";
                foreach (Unit u in a.units) if (u != null) u.visionRange = 1;
                FogOfWar.Changed(); FogOfWar.Refresh();
                CombatOutcome amb = new CombatOutcome(); HexTile ambFrom = null, ambAt = null;
                System.Action<Unit, Unit, HexTile, HexTile, CombatOutcome> hook = (att, def, f, t, x) => { amb = x; ambFrom = f; ambAt = t; };
                Unit.Attacked += hook;
                try { b.MoveAlong(new List<HexTile> { Rig.At(-2, 3), Rig.At(-1, 3), Rig.At(0, 3), Rig.At(1, 3) }, false, false); } finally { Unit.Attacked -= hook; }
                string d2 = "ambush: walker's tile " + Rig.At(-1, 3).damage + ", ambusher's " + Rig.At(0, 3).damage + ", start " + Rig.At(-2, 3).damage + " (fired first " + amb.defenderFiresFirst + ")";
                detail = d1 + "; " + d2;
                return from.damage == 0 && to.damage == 1 && Rig.At(-2, 3).damage == 0 && Rig.At(-1, 3).damage == 1 && Rig.At(0, 3).damage == 0;
            }
            case "fogBumpArt": {
                Unit m = Rig.At(-2, 0).occupier, h = Rig.At(0, 0).occupier;
                foreach (Unit u in a.units) if (u != null) u.visionRange = 1;
                FogOfWar.Changed(); FogOfWar.Refresh();
                int before = m.Strength, hb = h.Strength;
                CombatOutcome seen = new CombatOutcome();
                System.Action<Unit, Unit, HexTile, HexTile, CombatOutcome> hook = (att, def, f, t, oc) => { seen = oc; };
                Unit.Attacked += hook;
                try { m.MoveAlong(new List<HexTile> { Rig.At(-2, 0), Rig.At(-1, 0), Rig.At(0, 0), Rig.At(1, 0) }, false, false); }
                finally { Unit.Attacked -= hook; }
                detail = "artillery " + before + "->" + m.Strength + ", grunt " + hb + "->" + h.Strength + "; retaliation " + seen.retaliationHappens + ", damage back " + seen.damageToAttacker
                       + "; grunt answeredBy " + (h.answeredBy != null) + "; artillery attacking " + m.attacking + " firing " + m.firing;
                Unit g = Rig.At(-2, 3).occupier, ha = Rig.At(0, 3).occupier; int gb = g.Strength; bool shot = false;
                System.Action<Unit, Unit, bool> sh = (x, y, z) => { if (x == ha) shot = true; };
                Unit.Attacking += sh;
                try { g.MoveAlong(new List<HexTile> { Rig.At(-2, 3), Rig.At(-1, 3), Rig.At(0, 3), Rig.At(1, 3) }, false, false); } finally { Unit.Attacking -= sh; }
                detail += " | grunt into hidden artillery: artillery shot " + shot + ", grunt " + gb + "->" + g.Strength + " at " + Pos(g.tile) + " art attacking " + ha.attacking;
                m.tile.terrain = T("Forest"); m.Veterancy = 3;
                CombatOutcome o2 = h.OrderAttack(m);
                detail += " | plain attack on it in forest: firesFirst " + o2.defenderFiresFirst + " retaliation " + o2.retaliationHappens + " back " + o2.damageToAttacker + " art attacking " + m.attacking + " grunt attacking " + h.attacking + " answeredBy " + (h.answeredBy != null) + "/" + (m.answeredBy != null);
                return m.Strength < before && h.Strength == hb && !seen.retaliationHappens && h.answeredBy == null && !m.attacking && !m.firing;
            }
            case "fogBump": {
                Unit m = Rig.At(-2, 0).occupier, h = Rig.At(0, 0).occupier, p = Rig.At(0, 3).occupier, q = Rig.At(2, 3).occupier;
                foreach (Unit u in a.units) if (u != null) u.visionRange = 1;
                FogOfWar.Changed(); FogOfWar.Refresh();
                List<string> r = new List<string>();
                bool hidden = !FogOfWar.Sees(a, h) && !FogOfWar.Sees(a, q);
                r.Add("hidden at the start " + hidden);
                // The move range: the same as with the hidden grunt off the board.
                HashSet<HexTile> withFog = new HashSet<HexTile>();
                foreach (HexTile t in p.tile.CheckMovementRange(p)) if (t.G != 0 && t.CanEndTurnHere(p)) withFog.Add(t);
                HexTile qt = q.tile, ht = h.tile; qt.occupier = null; q.tile = null; ht.occupier = null; h.tile = null; gc.fogOfWar = false;
                HashSet<HexTile> empty = new HashSet<HexTile>();
                foreach (HexTile t in p.tile.CheckMovementRange(p)) if (t.G != 0 && t.CanEndTurnHere(p)) empty.Add(t);
                gc.fogOfWar = true; q.tile = qt; qt.occupier = q; h.tile = ht; ht.occupier = h;
                bool range = withFog.SetEquals(empty) && withFog.Contains(qt);
                r.Add("range as if not there " + range + " (" + withFog.Count + " tiles, its tile among them " + withFog.Contains(qt) + ")");
                // Walk through it.
                int mBefore = m.Strength, hBefore = h.Strength;
                CombatOutcome seen = new CombatOutcome(); Unit firer = null, target = null;
                System.Action<Unit, Unit, HexTile, HexTile, CombatOutcome> hook = (att, def, f, t, oc) => { firer = att; target = def; seen = oc; };
                Unit.Attacked += hook;
                bool arrived;
                try { arrived = m.MoveAlong(new List<HexTile> { Rig.At(-2, 0), Rig.At(-1, 0), Rig.At(0, 0), Rig.At(1, 0) }, false, false); }
                finally { Unit.Attacked -= hook; }
                bool stopped = !arrived && m.ambushedBy == h && (m.tile == Rig.At(-1, 0) || (seen.defenderRetreats && m.tile != null && m.tile != Rig.At(1, 0)));
                bool fight = firer == h && target == m && !seen.defenderFiresFirst && seen.retaliationHappens && m.Strength < mBefore && (h.Strength < hBefore || m.Strength <= 0);
                bool revealed = FogOfWar.Sees(a, h);
                r.Add("stopped short " + stopped + " at " + (m.tile != null ? "(" + m.tile.x + "," + m.tile.y + ")" : "-") + "; hidden grunt fired first " + fight + " (" + mBefore + "->" + m.Strength + ", it " + hBefore + "->" + h.Strength + "); seen now " + revealed);
                // The AI's move (headless, as the harness plays it): the same, through its executor.
                AIPlayer aip = a.GetComponent<AIPlayer>();
                HexTactica.AI.AIMap pm;
                using (FogOfWar.Concealing(a, true, 2)) pm = new HexTactica.AI.AIMap(a, aip.Profile);
                bool qHidden = !FogOfWar.Sees(a, q);
                int pBefore = p.Strength;
                bool did = HexTactica.AI.AIExecutor.Execute(HexTactica.AI.AIAction.Move(p, Rig.At(3, 3), 0f, "test"), pm, true);
                bool aiStopped = qHidden && did && p.tile == Rig.At(1, 3) && p.ambushedBy == q && p.Strength < pBefore && FogOfWar.Sees(a, q);
                r.Add("AI move through a hidden grunt: stopped at " + (p.tile != null ? "(" + p.tile.x + "," + p.tile.y + ")" : "-") + ", fired on " + aiStopped);
                detail = string.Join("; ", r.ToArray());
                return hidden && range && stopped && fight && revealed && aiStopped;
            }
            case "airNotAmbushed": {
                Unit h1 = Rig.At(-2, 0).occupier, tank = Rig.At(-1, 0).occupier, h2 = Rig.At(2, 0).occupier, grunt = Rig.At(3, 0).occupier;
                Unit f1 = Rig.At(0, 3).occupier, f2 = Rig.At(1, 3).occupier, t1 = Rig.At(0, -3).occupier, t2 = Rig.At(1, -3).occupier;
                h1.locomotion = Locomotion.Rotor; h2.locomotion = Locomotion.Rotor;
                h1.MinRange = 0; h1.AttackRange = 1; h2.MinRange = 0; h2.AttackRange = 1;
                grunt.Veterancy = 3; t2.Veterancy = 2; f2.Veterancy = 2;
                Func<Unit, Unit, bool> first = (x, y) => CombatMath.Resolve(x, y, x.tile).defenderFiresFirst;
                bool tankGrass = !first(h1, tank) && !CombatMath.Ambush(h1, tank, tank.tile);
                bool gruntForest = !first(h2, grunt) && !CombatMath.Ambush(h2, grunt, grunt.tile);
                bool landStill = first(t1, t2);
                bool airStill = CombatMath.Ambush(f1, f2, f2.tile);
                detail = "helicopter on a tank on grassland: tank fires first " + !tankGrass + "; on a veteran grunt in forest: grunt fires first " + !gruntForest
                       + "; veteran tank against a tank on grassland fires first " + landStill + "; veteran fighter against a fighter fires first " + airStill;
                return tankGrass && gruntForest && landStill && airStill;
            }
            case "uiOverlays": {
                Unit art = Rig.At(-3, 0).occupier, friend = Rig.At(-1, 0).occupier, enemy = Rig.At(0, 0).occupier, tank = Rig.At(2, -2).occupier;
                HexTileUIData ui = ScriptableObject.CreateInstance<HexTileUIData>();
                ui.move_possible = new Sprite(); ui.move_confirm = new Sprite(); ui.selected = new Sprite(); ui.attack_possible = new Sprite(); ui.move_arrow = new Sprite();
                foreach (HexTile t in Rig.hg.tiles) { t.overlay = Rig.New<SpriteRenderer>(); t.arrow = Rig.New<SpriteRenderer>(); t.uiData = ui; t.state = UIState.FREE; }
                foreach (Unit u in Rig.uc.units) if (u.textArea == null) u.textArea = Rig.New<UnityEngine.UI.Text>();
                if (LevelEditor.singleton == null) LevelEditor.singleton = Rig.New<LevelEditor>();
                gc.activePlayer = a;
                List<string> r = new List<string>();
                art.tile.OnMouseDown();
                int marked = 0, wrong = 0;
                foreach (HexTile t in Rig.hg.tiles) {
                    if (t.state != UIState.ATTACK && t.state != UIState.RANGE) continue;
                    marked++;
                    if (t.occupier == null || t.occupier.owner == a || t.state == UIState.RANGE) wrong++;
                }
                bool inRange = art.tile.Distance(enemy.tile) <= art.AttackRange && art.tile.Distance(enemy.tile) > art.MinRange && art.tile.Distance(friend.tile) > art.MinRange;
                bool arty = gc.activeUnit == art && enemy.tile.state == UIState.ATTACK && friend.tile.state != UIState.ATTACK && friend.tile.state != UIState.RANGE && wrong == 0 && inRange;
                r.Add("artillery selected: " + marked + " tiles marked, " + wrong + " not an enemy; the enemy grunt " + enemy.tile.state + ", the Blue grunt in range " + friend.tile.state);
                HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
                gc.activeUnit = null;
                SupplyOverlay.ShowEnemy(tank);
                Func<Color, Color, bool> same = (x, y) => Math.Abs(x.r - y.r) + Math.Abs(x.g - y.g) + Math.Abs(x.b - y.b) + Math.Abs(x.a - y.a) < 1e-4;
                int moveTiles = 0, reachTiles = 0, badMove = 0, badReach = 0;
                foreach (HexTile t in Rig.hg.tiles) {
                    if (t.state == UIState.ENEMY_MOVE) { moveTiles++; if (!same(t.overlay.color, ui.enemyMove)) badMove++; }
                    if (t.state == UIState.ENEMY_THREAT) { reachTiles++; if (!same(t.overlay.color, ui.enemyShoot)) badReach++; }
                }
                SupplyOverlay.HideEnemy();
                // Artillery (cannot move and fire): its whole range from where it stands shows as fire, over its moves.
                SupplyOverlay.ShowEnemy(art);
                int artRange = 0, artMissing = 0, artFireOverMove = 0;
                foreach (HexTile t in Rig.hg.tiles) {
                    int d = art.tile.Distance(t);
                    if (d > art.MinRange && d <= art.AttackRange && t != art.tile) { artRange++; if (t.state != UIState.ENEMY_THREAT) artMissing++; else if (t.occupier == null && art.tile.Distance(t) <= art.moveRange) artFireOverMove++; }
                }
                SupplyOverlay.HideEnemy();
                bool overlays = moveTiles > 0 && reachTiles > 0 && badMove == 0 && badReach == 0 && artRange > 0 && artMissing == 0 && artFireOverMove > 0;
                r.Add("enemy tank shown: " + moveTiles + " move tiles (" + badMove + " not grey), " + reachTiles + " fire tiles (" + badReach + " not orange); artillery: " + artRange + " tiles in range, " + artMissing + " not shown as fire, " + artFireOverMove + " of its moves shown as fire");
                // Capture: one Blue turn on each city.
                Building neutral = Rig.At(-2, 3).building, theirs = Rig.At(3, 3).building;
                string w0, v0; Player p0;
                bool fullNone = !theirs.ShownCaptureProgress(out w0, out v0, out p0);
                endTurn();
                string w1, v1, w2, v2; Player p1, p2;
                bool n1 = neutral.ShownCaptureProgress(out w1, out v1, out p1), n2 = theirs.ShownCaptureProgress(out w2, out v2, out p2);
                bool capture = fullNone && n1 && w1 == "Capturing" && v1 == "Blue 6/10" && p1 == a && n2 && w2 == "Capturing" && v2 == "Blue 6/10" && p2 == a;
                r.Add("capture line: Red city nobody is taking shows none " + fullNone + "; neutral city " + w1 + ": " + v1 + "; Red city " + w2 + ": " + v2);
                string top = TurnBanner.Line(o, 2, true) + " | " + TurnBanner.Line(a, 2, false) + " | " + TurnBanner.Line(a, 2, false, true);
                bool line = top == "Red \u2014 your turn, day 3 | Blue is moving | Blue is thinking";
                r.Add("top line: " + top);
                string end = TurnBanner.EndLine(o) + " | " + TurnBanner.EndLine(null);
                line = line && end == "Red wins | Draw";
                r.Add("game over: " + end);
                detail = string.Join("; ", r.ToArray());
                return arty && overlays && capture && line;
            }
            case "rotorSupply": {
                Unit inS = Rig.At(-4, 0).occupier, outS = Rig.At(2, 3).occupier, jet = Rig.At(-5, 1).occupier;
                inS.locomotion = Locomotion.Rotor; outS.locomotion = Locomotion.Rotor;
                List<string> r = new List<string>();
                SupplyNetwork.Result sup = SupplyNetwork.Solve(a, false);
                bool layout = sup.DepthAt(inS.tile) > 0 && sup.DepthAt(jet.tile) > 0 && sup.DepthAt(outS.tile) <= 0;
                r.Add("in supply: (-4,0) " + (sup.DepthAt(inS.tile) > 0) + ", (-5,1) " + (sup.DepthAt(jet.tile) > 0) + "; (2,3) out " + (sup.DepthAt(outS.tile) <= 0));
                // The AI's view before the turn: a supplied tile is a home for the helicopter, not for the jet.
                HexTactica.AI.AIMap m = new HexTactica.AI.AIMap(a, a.GetComponent<AIPlayer>().Profile);
                List<HexTile> heloHomes = HexTactica.AI.AIAir.Bases(inS, m), jetHomes = HexTactica.AI.AIAir.Bases(jet, m);
                bool ai = heloHomes.Contains(Rig.At(-4, 1)) && !jetHomes.Contains(Rig.At(-4, 1)) && m.Heals(inS, Rig.At(-4, 1)) && !m.Heals(jet, Rig.At(-4, 1)) && !m.Heals(outS, outS.tile);
                r.Add("AI: supplied tile a home for the helicopter " + heloHomes.Contains(Rig.At(-4, 1)) + ", not the jet " + !jetHomes.Contains(Rig.At(-4, 1)) + ", heals there " + m.Heals(inS, Rig.At(-4, 1)));
                foreach (Unit u in new[] { inS, outS, jet }) { u.fuel = 8; u.ammo = 0; u.Strength = 4; }
                endTurn(); endTurn();   // Red's turn, then the start of Blue's
                bool fed = inS.fuel == inS.fuelMax && inS.ammo == inS.ammoMax && inS.Strength == 5;
                bool aloft = outS.fuel == 8 - outS.minFuelPerTurn && outS.Strength == 4 && outS.ammo == 0;
                bool jetBurns = jet.fuel == 8 - jet.minFuelPerTurn && jet.Strength == 4;
                r.Add("helicopter in supply: fuel " + inS.fuel + "/" + inS.fuelMax + ", ammo " + inS.ammo + ", strength " + inS.Strength + "; out of supply: fuel " + outS.fuel + ", strength " + outS.Strength + "; jet in supply: fuel " + jet.fuel + ", strength " + jet.Strength);
                detail = string.Join("; ", r.ToArray());
                return layout && ai && fed && aloft && jetBurns;
            }
            case "fogLineOfSight": {
                PinVision();
                Unit g = Rig.At(-4, 0).occupier, e = Rig.At(-1, 0).occupier, jet = Rig.At(-4, 3).occupier, boat = Rig.At(-4, -3).occupier;
                g.visionRange = 4;
                Action moved = () => { FogOfWar.Changed(); FogOfWar.Refresh(); };
                Func<int, int, bool> sees = (x, y) => FogOfWar.TileVisible(a, Rig.At(x, y));
                List<string> r = new List<string>();
                // Only the grunt looks first: the others somewhere out of the way.
                jet.TeleportTo(Rig.At(6, -6), 0); boat.TeleportTo(Rig.At(6, -5), 0);
                Rig.At(0, 0).terrain = T("Forest");
                moved();
                bool flat = sees(-3, 0) && sees(-2, 0) && !sees(-1, 0) && !sees(0, 0) && !FogOfWar.Sees(a, e) && sees(-2, -2) && sees(0, -2);
                r.Add("grunt on grassland: the hill seen " + sees(-2, 0) + ", behind it hidden: (-1,0) " + !sees(-1, 0) + ", the forest " + !sees(0, 0) + ", the enemy grunt " + !FogOfWar.Sees(a, e) + "; off the line seen " + (sees(-2, -2) && sees(0, -2)));
                g.TeleportTo(Rig.At(-2, 0), 0); e.TeleportTo(Rig.At(2, -3), 0); moved();
                bool hill = sees(-1, 0) && sees(0, 0) && sees(1, 0) && sees(2, 0);
                r.Add("grunt on the hill: the forest behind " + sees(0, 0) + ", and past it (as high as the hill) " + sees(1, 0));
                Rig.At(0, 0).terrain = T("Mountains"); moved();
                bool mountain = sees(0, 0) && !sees(1, 0) && !sees(2, 0);
                r.Add("a mountain there instead: the mountain seen " + sees(0, 0) + ", past it hidden " + (!sees(1, 0) && !sees(2, 0)));
                Rig.At(0, 0).terrain = T("Grassland"); Rig.At(-2, 0).terrain = T("Grassland"); g.TeleportTo(Rig.At(-4, 0), 0);
                // The line from (-4,0) to (-3,1) runs along the edge between (-3,0) and (-4,1).
                Rig.At(-3, 0).terrain = T("Hills"); moved();
                bool edgeOne = sees(-3, 1);
                Rig.At(-4, 1).terrain = T("Hills"); moved();
                bool edgeTwo = !sees(-3, 1);
                r.Add("along an edge: one hill beside the line hides nothing " + edgeOne + ", two hide " + edgeTwo);
                Rig.At(-3, 0).terrain = T("Grassland"); Rig.At(-4, 1).terrain = T("Grassland");
                // The jet looks over a mountain wall; the grunt goes out of the way.
                g.TeleportTo(Rig.At(-6, 6), 0); jet.TeleportTo(Rig.At(-4, 0), 0);
                Rig.At(-3, 0).terrain = T("Mountains"); moved();
                bool air = sees(-2, 0) && sees(-1, 0) && sees(0, 0);
                r.Add("fighter over a mountain: sees past it " + air);
                Rig.At(-3, 0).terrain = T("Grassland"); jet.TeleportTo(Rig.At(6, -6), 0);
                // A gunboat on water: shallows on the line hide nothing, grassland does.
                foreach (int[] w in new[] { new[] { -4, -3 }, new[] { -3, -3 }, new[] { -2, -3 }, new[] { -1, -3 } }) Rig.At(w[0], w[1]).terrain = T("Water");
                Rig.At(-3, -3).terrain = T("Shallows"); boat.TeleportTo(Rig.At(-4, -3), 0); moved();
                bool sea1 = sees(-2, -3) && sees(-1, -3);
                Rig.At(-2, -3).terrain = T("Grassland"); moved();
                bool sea2 = sees(-2, -3) && !sees(-1, -3);
                r.Add("gunboat: past shallows " + sea1 + ", past a grassland tile hidden " + sea2);
                // An enemy behind a hill, then next to one of ours.
                boat.TeleportTo(Rig.At(6, -5), 0); g.TeleportTo(Rig.At(-4, 0), 0); Rig.At(-2, 0).terrain = T("Hills");
                e.TeleportTo(Rig.At(-1, 0), 0); moved();
                bool hidden = !FogOfWar.Sees(a, e);
                e.TeleportTo(Rig.At(-3, 1), 0); moved();
                bool adjacent = FogOfWar.Sees(a, e);
                r.Add("enemy behind the hill unseen " + hidden + ", next to the grunt seen " + adjacent);
                detail = string.Join("; ", r.ToArray());
                return flat && hill && mountain && edgeOne && edgeTwo && air && sea1 && sea2 && hidden && adjacent;
            }
            case "deleteSafely": {
                Unit g = Rig.At(-3, 0).occupier, apc = Rig.At(-2, 1).occupier, rider = Rig.At(-1, 1).occupier;
                Building city = Rig.At(2, 2).building;
                Transporting.Load(apc, rider);
                System.Reflection.MethodInfo ud = typeof(Unit).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                System.Reflection.MethodInfo bd = typeof(Building).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                ud.Invoke(g, null); ud.Invoke(apc, null); bd.Invoke(city, null);
                bool unitGone = !Rig.uc.units.Contains(g) && !a.units.Contains(g) && Rig.At(-3, 0).occupier == null && g.tile == null;
                bool apcGone = !Rig.uc.units.Contains(apc) && Rig.At(-2, 1).occupier == null && rider.trasnporter == null;
                bool cityGone = !Building.all.Contains(city) && Rig.At(2, 2).building == null && (BuildingController.bc == null || !BuildingController.bc.buildings.Contains(city));
                detail = "deleted grunt off the lists and its tile " + unitGone + "; transport too, its cargo unattached " + apcGone + "; city off the lists and its tile " + cityGone;
                return unitGone && apcGone && cityGone;
            }
            case "campaignObjectives": {
                List<string> r = new List<string>();
                AIProfile prof = ScriptableObject.CreateInstance<AIProfile>();
                if (Environment.GetEnvironmentVariable("CAMPDBG") != null) HexTactica.AI.AICampaign.Debug = x => Console.WriteLine("      " + x);
                HexTactica.AI.AIMap m = new HexTactica.AI.AIMap(a, prof);
                HexTactica.AI.AICampaign c = HexTactica.AI.AICampaign.Plan(m, prof, null);
                Func<HexTactica.AI.CampaignKind, HexTile, HexTactica.AI.CampaignObjective> find = (k, t) => c.objectives.Find(x => x.kind == k && (t == null || x.target == t));
                // Candidates and needs: replan with everything pursued to see them all.
                prof.objectiveSpread = 1f;
                HexTactica.AI.AICampaign all = HexTactica.AI.AICampaign.Plan(new HexTactica.AI.AIMap(a, prof), prof, null);
                HexTactica.AI.CampaignObjective cityN = all.objectives.Find(x => x.kind == HexTactica.AI.CampaignKind.Secure && x.units.Count > 0);
                // The Red group can reach our HQ next turn: dealing with it is defending the HQ.
                HexTactica.AI.CampaignObjective group = all.considered.Find(x => x.kind == HexTactica.AI.CampaignKind.Clear || x.kind == HexTactica.AI.CampaignKind.DefendHQ);
                HexTactica.AI.CampaignObjective hq = all.considered.Find(x => x.kind == HexTactica.AI.CampaignKind.Secure && x.building != null && x.building.IsHQ);
                bool read = cityN != null && group != null && hq != null;
                r.Add("objectives (spread 1): " + all.objectives.Count + ", none short of its need " + !all.objectives.Exists(x => x.units.Count > 0 && x.assignedPower < x.needPower - 0.5f) + "; a building to take " + (cityN != null) + ", Red group " + (group != null) + ", Red HQ " + (hq != null));
                bool needs = read && group.needPower >= group.enemyPower - 1f && group.enemyPower > 0f
                             && cityN.units.Exists(Building.CanCapture) && cityN.needPower > 0f
                             && !all.objectives.Exists(x => x.units.Count > 0 && x.assignedPower < x.needPower - 0.5f);
                bool supply = read && hq.needHubs >= 1 && hq.hubs.Count <= hq.needHubs;
                bool staged = read && hq.staging != null && m.EnemyStrikeTurns(hq.staging) > 1;
                if (read) r.Add("Red group: enemy " + group.enemyPower.ToString("0") + ", need " + group.needPower.ToString("0") + "; the building: need " + cityN.needPower.ToString("0") + ", a capturer " + cityN.units.Exists(Building.CanCapture)
                                + "; Red HQ: supply " + hq.hubs.Count + "/" + hq.needHubs + ", staging " + (hq.staging != null ? "(" + hq.staging.x + "," + hq.staging.y + ") strike in " + m.EnemyStrikeTurns(hq.staging) : "none"));
                int pursuedAll = all.objectives.FindAll(x => x.kind != HexTactica.AI.CampaignKind.DefendHQ && x.units.Count > 0).Count;
                prof.objectiveSpread = 0f;
                HexTactica.AI.AICampaign allIn = HexTactica.AI.AICampaign.Plan(new HexTactica.AI.AIMap(a, prof), prof, null);
                int pursuedOne = allIn.objectives.FindAll(x => x.kind != HexTactica.AI.CampaignKind.DefendHQ && x.units.Count > 0).Count;
                r.Add("pursued: all in " + pursuedOne + ", spread " + pursuedAll);
                bool spread = pursuedOne == 1 && pursuedAll >= 2;
                // Kept from turn to turn.
                prof.objectiveSpread = 0.5f;
                HexTactica.AI.AICampaign t1 = HexTactica.AI.AICampaign.Plan(new HexTactica.AI.AIMap(a, prof), prof, null);
                HexTactica.AI.AICampaign t2 = HexTactica.AI.AICampaign.Plan(new HexTactica.AI.AIMap(a, prof), prof, t1);
                // Red can reach our HQ only in two turns: nothing is held back for it.
                bool noDefence = !t1.objectives.Exists(x => x.kind == HexTactica.AI.CampaignKind.DefendHQ);
                // Far from where they gather, the task forces hold there first, none goes in alone.
                List<Front> early = t1.Fronts(new HexTactica.AI.AIMap(a, prof), prof, null, new HashSet<HexTile>());
                bool gather = true; int gathering = 0;
                foreach (Front f in early) {
                    if (f.campaignStaging == null) continue;
                    gathering++;
                    // On its staging ground or going because enough of it is there (campaignGatherShare), and that ground safe.
                    if (Environment.GetEnvironmentVariable("CAMPDBG") != null) r.Add("front " + f + " staging " + f.staging + " assault " + f.assault + " at " + f.campaignStaging.x + "," + f.campaignStaging.y + " objective " + (f.objectiveTile != null ? f.objectiveTile.x + "," + f.objectiveTile.y : "-"));
                    if (f.staging == f.assault) gather = false;
                    if (m.EnemyStrikeTurns(f.campaignStaging) <= 1) gather = false;   // a safe staging ground
                }
                r.Add("no HQ defence with Red two turns off " + noDefence + "; " + gathering + " task forces each gathering short of its objective or going with enough there " + (gather && gathering > 0));
                bool kept = t2.replanned == null;
                foreach (HexTactica.AI.CampaignObjective x in t1.objectives) {
                    HexTactica.AI.CampaignObjective y = t2.objectives.Find(z => z.Id == x.Id);
                    if (y == null) { kept = false; continue; }
                    foreach (Unit u in x.units) if (!y.units.Contains(u)) kept = false;
                }
                // The Red group destroyed: planned afresh.
                foreach (Unit e in new[] { Rig.At(2, 0).occupier, Rig.At(3, 0).occupier, Rig.At(2, 1).occupier, Rig.At(5, 0).occupier }) if (e != null) e.Delete();
                HexTactica.AI.AICampaign t3 = HexTactica.AI.AICampaign.Plan(new HexTactica.AI.AIMap(a, prof), prof, t2);
                bool replanned = t3.replanned != null;
                r.Add("planned again: kept " + kept + "; Red group gone: replanned " + replanned + " (" + t3.replanned + ")");
                // Fronts.
                List<Front> fronts = t3.Fronts(new HexTactica.AI.AIMap(a, prof), prof, null, null);
                int withUnits = fronts.FindAll(f => f.assigned.Count > 0).Count;
                int placed = 0; foreach (Front f in fronts) placed += f.assigned.Count;
                bool frontsOk = withUnits >= 2 && placed == a.units.FindAll(u => u != null && u.tile != null).Count;
                r.Add("fronts " + fronts.Count + " (" + withUnits + " with units), every unit on one " + frontsOk);
                detail = string.Join("; ", r.ToArray());
                return read && needs && supply && staged && spread && kept && replanned && frontsOk && noDefence && gather && gathering > 0;
            }
            case "boardGrows": {
                HexGenerator hg = HexGenerator.hg;
                List<string> r = new List<string>();
                hg.makeTile = at => {
                    HexTile t = Rig.New<HexTile>();
                    t.neighbors = new List<HexTile>(); t.aiData = new AITileData();
                    t.threat = new Dictionary<Player, Heat>(); t.supply = new Dictionary<Player, int>();
                    t.terrain = ScenarioFile.Terrain("Grassland");
                    return t;
                };
                try {
                    int w0 = hg.width, n0 = hg.tiles.Count;
                    HexTile centre = Rig.At(0, 0), edge = Rig.At(7, 0);
                    Unit g = Rig.At(-3, 0).occupier;
                    int edgeRing = HexTactica.AI.AIPath.TilesInRangeShared(edge, 0, 1).Count;
                    bool same = !hg.SetWidth(w0) && hg.width == w0;
                    int need = w0 + 6;
                    Defective.JSON.JSONObject file = new Defective.JSON.JSONObject("{\"width\":" + need + ",\"tiles\":[]}");
                    bool grew = HexTactica.Harness.ScenarioLoader.FitBoard(file) && hg.width == need;
                    bool kept = Rig.At(0, 0) == centre && Rig.At(7, 0) == edge && Rig.At(-3, 0).occupier == g && g.tile == Rig.At(-3, 0);
                    int bad = 0, interior = 0, six = 0;
                    HashSet<long> coords = new HashSet<long>();
                    foreach (HexTile t in hg.tiles) {
                        if (hg.get(t.x, t.y, t.z) != t || t.x + t.y + t.z != 0 || !coords.Add(t.x * 100000L + t.y)) bad++;
                        foreach (HexTile n in t.neighbors) if (!n.neighbors.Contains(t) || t.Distance(n) != 1) bad++;
                        if (t.neighbors.Count > 6) bad++;
                        if (Mathf.Max(Mathf.Abs(t.x), Mathf.Max(Mathf.Abs(t.y), Mathf.Abs(t.z))) < 10) { interior++; if (t.neighbors.Count == 6) six++; }
                    }
                    int edgeNow = HexTactica.AI.AIPath.TilesInRangeShared(edge, 0, 1).Count;
                    bool far = hg.get(10, -10, 0) != null;
                    r.Add("board " + w0 + " wide, " + n0 + " tiles; asked for its own width: left alone " + same + "; a file " + need + " wide: grew " + grew + " to " + hg.tiles.Count + " tiles");
                    r.Add("tiles there kept, the grunt on its tile " + kept + "; tiles wrong " + bad + "; tiles within 9 of the centre with six neighbours " + six + " of " + interior + "; old edge tile (7,0): ring of " + edgeRing + " then " + edgeNow + "; (10,-10) exists " + far);
                    // And smaller: a file made 15 wide.
                    Defective.JSON.JSONObject small = new Defective.JSON.JSONObject("{\"width\":15,\"tiles\":[]}");
                    bool shrank = HexTactica.Harness.ScenarioLoader.FitBoard(small) && hg.width == 15;
                    int outside = 0, bad2 = 0;
                    foreach (HexTile t in hg.tiles) {
                        if (Mathf.Max(Mathf.Abs(t.x), Mathf.Max(Mathf.Abs(t.y), Mathf.Abs(t.z))) > 7) outside++;
                        if (hg.get(t.x, t.y, t.z) != t) bad2++;
                        foreach (HexTile n in t.neighbors) if (!hg.tiles.Contains(n) || !n.neighbors.Contains(t)) bad2++;
                    }
                    bool kept2 = Rig.At(0, 0) == centre && Rig.At(-3, 0).occupier == g && hg.get(10, -10, 0) == null;
                    r.Add("a file 15 wide: shrank " + shrank + " to " + hg.tiles.Count + " tiles, " + outside + " beyond radius 7, wrong " + bad2 + ", the centre and the grunt kept " + kept2);
                    detail = string.Join("; ", r.ToArray());
                    return same && grew && kept && bad == 0 && six == interior && edgeRing == 3 && edgeNow == 6 && far && shrank && outside == 0 && bad2 == 0 && kept2 && hg.tiles.Count == 169;
                } finally { hg.makeTile = null; }
            }
            case "editorLoad": {
                // The terrains the scene's LevelEditor lists (Assets/Scenes/SampleScene.unity), and every building prefab.
                string root = PrefabLoader.AssetsRoot;
                List<string> sceneTerrain = new List<string>();
                bool inList = false;
                foreach (string line in File.ReadAllLines(Path.Combine(root, "Scenes", "SampleScene.unity"))) {
                    if (line.StartsWith("  allTerrain:")) { inList = true; continue; }
                    if (!inList) continue;
                    System.Text.RegularExpressions.Match g = System.Text.RegularExpressions.Regex.Match(line, "^  - .*guid: ([0-9a-f]+)");
                    if (!g.Success) break;
                    foreach (string meta in Directory.GetFiles(Path.Combine(PrefabLoader.Root("Tiles")), "*.asset.meta", SearchOption.AllDirectories))
                        if (File.ReadAllText(meta).Contains("guid: " + g.Groups[1].Value)) sceneTerrain.Add(Path.GetFileName(meta).Replace(".asset.meta", ""));
                }
                LevelEditor was = LevelEditor.singleton;
                LevelEditor ed = Rig.New<LevelEditor>();
                ed.allTerrain = new List<TerrainData>();
                foreach (string t in sceneTerrain) ed.allTerrain.Add(ScenarioFile.Terrain(t));
                ed.allBuilding = new List<Building>();
                foreach (string f in Directory.GetFiles(Path.Combine(PrefabLoader.Root("Buildings"), "Prefabs"), "*.prefab"))
                    try { ed.allBuilding.Add(PrefabLoader.LoadBuilding(Path.GetFileNameWithoutExtension(f))); } catch (Exception) { }
                ed.allUnit = new List<Unit>();
                LevelEditor.singleton = ed;
                try {
                    // What a tile needs to be loaded into headless: its pictures, overlay and roads.
                    HexTileUIData ui = ScriptableObject.CreateInstance<HexTileUIData>();
                    foreach (HexTile t in Rig.hg.tiles) {
                        t.upper = Rig.New<SpriteRenderer>(); t.lower = Rig.New<SpriteRenderer>(); t.water = Rig.New<SpriteRenderer>(); t.floor = Rig.New<SpriteRenderer>();
                        t.road_up = Rig.New<SpriteRenderer>(); t.road_dn = Rig.New<SpriteRenderer>(); t.road_ru = Rig.New<SpriteRenderer>();
                        t.road_rd = Rig.New<SpriteRenderer>(); t.road_lu = Rig.New<SpriteRenderer>(); t.road_ld = Rig.New<SpriteRenderer>();
                        if (t.overlay == null) t.overlay = Rig.New<SpriteRenderer>();
                        if (t.arrow == null) t.arrow = Rig.New<SpriteRenderer>();
                        if (t.uiData == null) t.uiData = ui;
                    }
                    List<string> r = new List<string>();
                    r.Add("scene list: " + string.Join(",", sceneTerrain.ToArray()));
                    HexTile hq = Rig.At(-6, 0), city = Rig.At(0, 0);
                    List<TileData> saved = new List<TileData>();
                    foreach (HexTile t in Rig.hg.tiles) saved.Add(new TileData(t));
                    Dictionary<HexTile, string> before = new Dictionary<HexTile, string>();
                    foreach (HexTile t in Rig.hg.tiles) before[t] = t.terrain.name;
                    string hqWas = hq.terrain.name, cityWas = city.terrain.name;
                    int failed = 0, changed = 0;
                    string firstError = null;
                    foreach (TileData d in saved) {
                        try { d.load(); } catch (Exception e) { failed++; if (firstError == null) firstError = e.GetType().Name + " at (" + d.x + "," + d.y + ") '" + d.terrain + "'"; }
                    }
                    foreach (HexTile t in Rig.hg.tiles) if (t.terrain == null || t.terrain.name != before[t]) changed++;
                    r.Add("HQ tile " + hqWas + " → " + (hq.terrain != null ? hq.terrain.name : "null") + ", city tile " + cityWas + " → " + (city.terrain != null ? city.terrain.name : "null"));
                    r.Add(saved.Count + " tiles loaded, " + failed + " failed" + (firstError != null ? " (" + firstError + ")" : "") + ", " + changed + " changed");
                    // The ground buildings lay (BuildingGround, renamed Urban; the scene's list has it now, older ones did not).
                    Building hqB = hq.building;
                    string groundName = hqB != null && hqB.ground != null ? hqB.ground.name : null;
                    bool roundTrip = failed == 0 && changed == 0 && groundName != null && hqWas == groundName;
                    r.Add("the buildings' ground: " + (groundName ?? "none (its asset's .meta file is missing, so the prefabs' reference to it is broken)"));
                    // An older save naming the ground by its old name.
                    TileData oldName = new TileData(city); oldName.terrain = "BuildingGround"; oldName.under = "Grassland";
                    try { oldName.load(); } catch (Exception e) { r.Add("old name: " + e.GetType().Name); }
                    bool renamed = groundName == null || city.terrain.name == groundName;
                    r.Add("a save naming BuildingGround loads " + city.terrain.name);
                    roundTrip &= renamed;
                    // A terrain the game never had: the ground under, else grassland.
                    TileData odd = new TileData(Rig.At(2, 0)); odd.terrain = "Swampland"; odd.under = "";
                    TileData oddUnder = new TileData(Rig.At(3, 0)); oddUnder.terrain = "Swampland"; oddUnder.under = "Forest";
                    bool unknown = false;
                    try { odd.load(); oddUnder.load(); unknown = Rig.At(2, 0).terrain.name == "Grassland" && Rig.At(3, 0).terrain.name == "Forest"; }
                    catch (Exception e) { r.Add("unknown terrain: " + e.GetType().Name); }
                    r.Add("unknown terrain loads as grassland / the ground under " + unknown);
                    detail = string.Join("; ", r.ToArray());
                    return roundTrip && unknown;
                } finally { LevelEditor.singleton = was; }
            }
            case "fogViewLoad": {
                gc.activePlayer = o;
                Unit red = Rig.At(-3, 0).occupier, blue = Rig.At(3, 0).occupier;
                List<string> r = new List<string>();
                // The scene started with fog off and nothing published; the scenario turns fog on.
                gc.fogOfWar = false; FogOfWar.Reset();
                foreach (HexTile t in Rig.hg.tiles) foreach (Player pl in gc.players) t.supply[pl] = 0;
                FogView fv = Rig.New<FogView>();
                System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                System.Reflection.MethodInfo late = typeof(FogView).GetMethod("LateUpdate", inst);
                late.Invoke(fv, null);
                gc.fogOfWar = true; FogOfWar.Reset();
                int published = SupplyNetwork.Published;
                GameController.BoardLoaded();
                bool supplied = Rig.At(6, 0).isSupplied(o) && Rig.At(-6, 0).isSupplied(a) && SupplyNetwork.Published > published;
                r.Add("supply published on load " + supplied);
                late.Invoke(fv, null);   // one frame: the fog is there at once
                System.Collections.IDictionary covers = (System.Collections.IDictionary)typeof(FogView).GetField("covers", inst).GetValue(fv);
                Func<object, bool> shown = c => c != null && ((SpriteRenderer)c.GetType().GetField("sr").GetValue(c)).enabled && (float)c.GetType().GetField("alpha").GetValue(c) >= 0.999f;
                Func<int[]> count = () => {
                    int hidden = 0, covered = 0, wrong = 0;
                    foreach (HexTile t in Rig.hg.tiles) {
                        bool vis = FogOfWar.TileVisible(o, t);
                        bool on = shown(covers.Contains(t) ? covers[t] : null);
                        if (!vis) hidden++; if (on) covered++; if (on == vis) wrong++;
                    }
                    return new[] { hidden, covered, wrong };
                };
                int[] n0 = count();
                bool atOnce = FogOfWar.Viewer == o && n0[0] > 0 && n0[2] == 0;
                r.Add("first frame after the load: tiles hidden " + n0[0] + ", covered " + n0[1] + ", wrong " + n0[2]);
                // Beyond the edge: every cell off the board beside it is fogged...
                System.Collections.IList ring = (System.Collections.IList)typeof(FogView).GetField("beyond", inst).GetValue(fv);
                Func<object, bool> beyondShown = b => shown(b.GetType().GetField("cover").GetValue(b));
                Func<object, List<HexTile>> nextOf = b => (List<HexTile>)b.GetType().GetField("next").GetValue(b);
                int fogged = 0; foreach (object b in ring) if (beyondShown(b)) fogged++;
                bool ringAll = ring.Count > 0 && fogged == ring.Count;
                r.Add("cells past the edge: " + ring.Count + ", fogged " + fogged);
                // ... until a unit Red sees stands beside one: Red's grunt on the edge clears the cells beside it, no others.
                HexTile edge = null, far = null;
                foreach (object b in ring) { HexTile t = nextOf(b)[0]; if (t.occupier == null && t.x < 0 && edge == null) edge = t; }
                foreach (object b in ring) { HexTile t = nextOf(b)[0]; if (t.occupier == null && t.x > 3 && t.Distance(edge) > 6 && far == null) far = t; }
                red.TeleportTo(edge, 0); blue.TeleportTo(far, 0); FogOfWar.Changed();
                for (int i = 0; i < 100; i++) late.Invoke(fv, null);
                int clearedNear = 0, near = 0, foggedElse = 0, elsewhere = 0, foggedBlue = 0, besideBlue = 0;
                foreach (object b in ring) {
                    List<HexTile> nx = nextOf(b);
                    if (nx.Contains(edge)) { near++; if (!beyondShown(b)) clearedNear++; }
                    else { elsewhere++; if (beyondShown(b)) foggedElse++; }
                    if (nx.Contains(far) && !nx.Contains(edge)) { besideBlue++; if (beyondShown(b)) foggedBlue++; }
                }
                bool hiddenBlue = !FogOfWar.ShownToViewer(blue);
                bool ringUnit = near > 0 && clearedNear == near && foggedElse == elsewhere && besideBlue > 0 && foggedBlue == besideBlue && hiddenBlue;
                r.Add("Red's grunt on the edge at (" + edge.x + "," + edge.y + "): " + clearedNear + " of " + near + " cells beside it cleared, " + foggedElse + " of " + elsewhere + " others fogged; beside the Blue grunt Red does not see: " + foggedBlue + " of " + besideBlue + " fogged");
                int[] n1 = count();
                r.Add("tiles after: hidden " + n1[0] + ", wrong " + n1[2]);
                // Fog off: nothing past the edge either.
                gc.fogOfWar = false;
                for (int i = 0; i < 100; i++) late.Invoke(fv, null);
                int left = 0; foreach (object b in ring) if (beyondShown(b)) left++;
                r.Add("fog off: cells past the edge fogged " + left);
                detail = string.Join("; ", r.ToArray());
                return supplied && atOnce && ringAll && ringUnit && n1[2] == 0 && left == 0;
            }
            case "fogClicks": {
                Unit red = Rig.At(0, 0).occupier, blue = Rig.At(2, 0).occupier;
                red.visionRange = 1;
                // What the board needs to be clicked on headless: overlays, the UI pictures, the level editor (closed), damage text.
                HexTileUIData ui = ScriptableObject.CreateInstance<HexTileUIData>();
                ui.move_possible = new Sprite(); ui.move_confirm = new Sprite(); ui.selected = new Sprite(); ui.attack_possible = new Sprite(); ui.move_arrow = new Sprite();
                foreach (HexTile t in Rig.hg.tiles) { t.overlay = Rig.New<SpriteRenderer>(); t.arrow = Rig.New<SpriteRenderer>(); t.uiData = ui; t.state = UIState.FREE; }
                foreach (Unit u in Rig.uc.units) if (u.textArea == null) u.textArea = Rig.New<UnityEngine.UI.Text>();
                if (LevelEditor.singleton == null) LevelEditor.singleton = Rig.New<LevelEditor>();
                gc.activePlayer = o;
                FogOfWar.Changed(); FogOfWar.Refresh();
                List<string> r = new List<string>();
                HexTile hiddenAt = Rig.At(2, 0), emptyAt = Rig.At(1, -1), beyond = Rig.At(3, 0);
                bool viewer = FogOfWar.Viewer == o && !FogOfWar.Sees(o, blue);
                hiddenAt.OnMouseDown();
                bool asEmpty1 = gc.activeUnit == null && !SupplyOverlay.enemyShown && HexTile.lastClicked == hiddenAt && FogOfWar.ApparentOccupier(hiddenAt, FogOfWar.Viewer) == null;
                r.Add("Red's view " + viewer + "; clicked with nothing selected: nothing selected, no enemy shown " + asEmpty1);
                Rig.At(0, 0).OnMouseDown();
                bool offered = gc.activeUnit == red && hiddenAt.state == UIState.MOVE && emptyAt.state == UIState.MOVE && beyond.state == UIState.MOVE;
                r.Add("Red's grunt selected: the hidden grunt's tile offered as a move " + (hiddenAt.state == UIState.MOVE) + ", and the tile beyond " + (beyond.state == UIState.MOVE));
                beyond.OnMouseDown();
                bool path = beyond.state == UIState.MOVE_CONFIRM && hiddenAt.state == UIState.MOVE_ARROW && hiddenAt.overlay.sprite == ui.move_possible && hiddenAt.overlay.gameObject.activeSelf;
                r.Add("path drawn across it as across empty ground " + path);
                beyond.OnMouseDown();
                bool bumped = red.tile == Rig.At(1, 0) && red.ambushedBy == blue && red.Strength < 6 && FogOfWar.Sees(o, blue) && !red.active;
                r.Add("moved: stopped at " + (red.tile != null ? "(" + red.tile.x + "," + red.tile.y + ")" : "-") + ", fired on, the Blue grunt seen now, its move over " + bumped);
                detail = string.Join("; ", r.ToArray());
                return viewer && asEmpty1 && offered && path && bumped;
            }
            case "fogKnowledge": {
                PinVision();
                Unit tank = Rig.At(-2, 0).occupier, recon = Rig.At(0, 0).occupier, near = Rig.At(3, 0).occupier, far = Rig.At(2, -5).occupier, etank = Rig.At(5, -2).occupier;
                AIPlayer ai = a.GetComponent<AIPlayer>();
                List<string> r = new List<string>();
                FogOfWar.Changed(); FogOfWar.Refresh();
                bool start = FogOfWar.Sees(a, near) && !FogOfWar.Sees(a, far) && !FogOfWar.Sees(a, etank);
                r.Add("Blue sees the near grunt only " + start);
                // A board snapshot, to check it is put back exactly.
                Dictionary<Unit, KeyValuePair<HexTile, int>> snap = new Dictionary<Unit, KeyValuePair<HexTile, int>>();
                foreach (Unit u in Rig.uc.units) snap[u] = new KeyValuePair<HexTile, int>(u.tile, u.Strength);
                // Seen a turn ago at (2,-4)?: let Blue see the far grunt, then it moves out of sight.
                recon.TeleportTo(Rig.At(1, -3), 0); FogOfWar.Changed(); FogOfWar.Refresh();
                bool sawFar = FogOfWar.Sees(a, far);
                recon.TeleportTo(Rig.At(0, 0), 0); far.TeleportTo(Rig.At(-2, -5), 0); FogOfWar.Changed(); FogOfWar.Refresh();
                snap[recon] = new KeyValuePair<HexTile, int>(recon.tile, recon.Strength); snap[far] = new KeyValuePair<HexTile, int>(far.tile, far.Strength);
                bool inside = false, believed = false, offBoard = false, notTarget = false, map = false, values = false;
                float hiddenV = 0, exposedV = 0, scoutA = 0, scoutB = 0;
                BoardView cv = FogOfWar.Conceal(a, true, 2);
                using (cv != null ? cv.Enter() : null) {
                    FogOfWar.Knowledge sc = cv != null ? cv.Fog : null;
                    inside = sc != null;
                    if (sc != null) {
                        believed = far.tile == Rig.At(2, -5) && Rig.At(2, -5).occupier == far && sc.believed.Contains(far);
                        offBoard = etank.tile == null && Rig.At(5, -2).occupier == null;
                        notTarget = !GameController.Targetable(far) && !GameController.Targetable(etank) && GameController.Targetable(near);
                        HexTactica.AI.AIMap am = new HexTactica.AI.AIMap(a, ai.Profile);
                        map = !am.enemyUnits.Contains(etank) && am.enemyUnits.Contains(near) && am.enemyUnits.Contains(far);
                        hiddenV = HexTactica.AI.AIFog.AmbushValue(tank, Rig.At(0, 2), am, ai.Profile);
                        exposedV = HexTactica.AI.AIFog.AmbushValue(tank, Rig.At(2, 0), am, ai.Profile);
                        scoutA = HexTactica.AI.AIFog.ScoutValue(tank, Rig.At(4, -6), ai.Profile);   // (tiles seen earlier this turn count for nothing: nothing has moved since)
                        scoutB = HexTactica.AI.AIFog.ScoutValue(tank, Rig.At(-1, 0), ai.Profile);
                        values = hiddenV > 0f && exposedV == 0f && scoutA > scoutB;
                    }
                }
                bool restored = FogOfWar.current == null;
                foreach (KeyValuePair<Unit, KeyValuePair<HexTile, int>> kv in snap) {
                    Unit u = kv.Key;
                    if (u.tile != kv.Value.Key || u.Strength != kv.Value.Value) restored = false;
                    if (u.tile != null && u.trasnporter == null && u.tile.occupier != u) restored = false;
                }
                r.Add("concealed " + inside + ": far grunt where last seen " + believed + ", unseen tank off the board " + offBoard + ", only the seen targetable " + notTarget + ", board read without the unseen " + map);
                r.Add("ambush tile " + hiddenV.ToString("0") + " vs exposed " + exposedV.ToString("0") + ", scouting " + scoutA.ToString("0") + " vs " + scoutB.ToString("0") + " " + values);
                r.Add("board put back " + restored);
                // Last seen tile now in sight and empty: forgotten.
                recon.TeleportTo(Rig.At(1, -3), 0); FogOfWar.Changed(); FogOfWar.Refresh();
                bool forgot;
                BoardView cv2 = FogOfWar.Conceal(a, true, 2);
                using (cv2 != null ? cv2.Enter() : null) forgot = cv2 != null && far.tile == null && !cv2.Fog.believed.Contains(far);
                r.Add("its last tile seen empty: forgotten " + forgot);
                ai.Profile.fogFair = false;
                bool all = FogOfWar.Conceal(a, true, 2) == null && FogOfWar.Sees(a, etank);
                ai.Profile.fogFair = true;
                r.Add("fogFair off: sees everything " + all);
                detail = string.Join("; ", r.ToArray());
                return start && sawFar && inside && believed && offBoard && notTarget && map && values && restored && forgot && all;
            }
            case "fogSigint": {
                PinVision();
                Unit gun = Rig.At(-3, 0).occupier, grunt = Rig.At(-4, 2).occupier, recon = Rig.At(-1, 0).occupier, aa = Rig.At(1, 0).occupier, etank = Rig.At(5, -4).occupier;
                // (aa: the Red grunt Blue loses track of.)
                AIPlayer ai = a.GetComponent<AIPlayer>();
                AIProfile prof = ai.Profile;
                List<string> r = new List<string>();
                FogOfWar.Changed(); FogOfWar.Refresh();
                bool saw = FogOfWar.Sees(a, aa) && !FogOfWar.Sees(a, etank);
                // A round later the anti-air unit has gone (out of sight), and its tile is seen empty.
                aa.TeleportTo(Rig.At(4, 1), 0);
                FogOfWar.Turn += 2; FogOfWar.Changed(); FogOfWar.Refresh();
                bool lost = !FogOfWar.Sees(a, aa) && FogOfWar.TileVisible(a, Rig.At(1, 0));
                r.Add("saw the Red grunt " + saw + ", lost it with its tile in sight " + lost);
                HexTile near = Rig.At(1, -1), far = Rig.At(-5, 1);
                Func<Action<AIProfile>, Func<HexTactica.AI.AIMap, float>, float> with = (tweak, f) => {
                    AIProfile c = ScriptableObject.CreateInstance<AIProfile>();
                    foreach (System.Reflection.FieldInfo fi in typeof(AIProfile).GetFields()) if (!fi.IsStatic) fi.SetValue(c, fi.GetValue(prof));
                    c.fogScreenBelowCost = 0;   // the grunt here measures the fear of unknown ground (screens ignore it)
                    if (tweak != null) tweak(c);
                    float v = float.NaN;
                    BoardView cv = FogOfWar.Conceal(a, true, c.fogMemoryTurns);
                    using (cv != null ? cv.Enter() : null) if (cv != null) v = f(new HexTactica.AI.AIMap(a, c));
                    return v;
                };
                bool phantom = false, notBelieved = false;
                BoardView pv = FogOfWar.Conceal(a, true, prof.fogMemoryTurns);
                using (pv != null ? pv.Enter() : null) if (pv != null) {
                    phantom = pv.Fog.phantoms.Exists(x => x.unit == aa && x.moves == 1);
                    notBelieved = !pv.Fog.believed.Contains(aa) && aa.tile == null;
                }
                r.Add("not believed where it was " + notBelieved + ", known to be about " + phantom);
                if (Environment.GetEnvironmentVariable("SIGDBG") != null) with(null, m => { if (m.sigint != null) foreach (string line in m.sigint.Describe()) r.Add(line); return 0f; });
                Action<AIProfile> calm = c => { c.fogAggression = 1f; };
                float nearLoss = with(calm, m => m.FogLoss(grunt, near)), farLoss = with(calm, m => m.FogLoss(grunt, far));
                float inRisk = with(calm, m => m.ExpectedLoss(grunt, near));
                FogOfWar.Turn += 2; FogOfWar.Refresh();
                float later = with(calm, m => m.FogLoss(grunt, near));
                FogOfWar.Turn += 2 * prof.sigintMoves; FogOfWar.Refresh();
                float forgotten = with(calm, m => m.FogLoss(grunt, near));
                FogOfWar.Turn -= 2 + 2 * prof.sigintMoves; FogOfWar.Refresh();
                r.Add(string.Format("Blue grunt's loss to it next to where it may be {0:0.00}, a move later {1:0.00}, after {2} moves {3:0.00}; out of its reach {4:0.00}; in the risk priced {5}",
                    nearLoss, later, prof.sigintMoves + 2, forgotten, farLoss, inRisk >= nearLoss && nearLoss > 0f));
                // Not forgotten: spread thinner over more places the longer it is unseen, never to nothing.
                bool zones = nearLoss > 0f && later > 0f && later < nearLoss && forgotten > 0f && forgotten <= later && farLoss == 0f && inRisk >= nearLoss;
                // Unknown ground (what it adds over the lost unit, fogAggression 1): Red's tank was never seen, nor the ground far east.
                HexTile edge = Rig.At(2, 3), home = Rig.At(-6, 1);
                float uBase = with(c => { c.fogAggression = 1f; c.fogUncertainty = false; }, m => m.FogLoss(grunt, edge));
                float u0 = with(c => { c.fogAggression = 0f; c.fogUncertainty = false; }, m => m.FogLoss(grunt, edge)) - uBase;
                float uHalf = with(c => { c.fogAggression = 0.5f; c.fogUncertainty = false; }, m => m.FogLoss(grunt, edge)) - uBase;
                float u1 = 0f;
                float uHome = with(c => { c.fogAggression = 0f; c.fogUncertainty = false; }, m => m.FogLoss(grunt, home)) - with(c => { c.fogAggression = 1f; c.fogUncertainty = false; }, m => m.FogLoss(grunt, home));
                r.Add(string.Format("grunt by unknown ground: fogAggression 0 {0:0.00}, 0.5 {1:0.00}, 1 {2:0.00}; at home {3:0.00}", u0, uHalf, u1, uHome));
                // (Capped at the grunt's strength, so the half is checked only below the cap.)
                bool unknown = u0 > 0f && uHalf > 0f && uHalf <= u0 && (u0 + uBase >= grunt.Strength || Mathf.Abs(uHalf - u0 / 2f) < 0.01f + u0 * 0.01f) && uHome < u0;
                // Scouting where it may be.
                float scoutWith = with(c => { c.fogAggression = 1f; }, m => HexTactica.AI.AIFog.ScoutValue(recon, Rig.At(1, 1), m.profile, m));
                float scoutWithout = with(c => { c.fogAggression = 1f; c.scoutThreatBonus = 0f; }, m => HexTactica.AI.AIFog.ScoutValue(recon, Rig.At(1, 1), m.profile, m));
                r.Add(string.Format("scouting where it may be {0:0} vs without the bonus {1:0}", scoutWith, scoutWithout));
                bool scout = scoutWith > scoutWithout && scoutWithout > 0f;
                // The damage priced is the game's.
                int pairs = 0, wrong = 0;
                foreach (Unit x in Rig.uc.units) foreach (Unit y in Rig.uc.units) {
                    if (x == y || x.Hub != null || y.tile == null) continue;
                    pairs++;
                    if (FogOfWar.Kind.Of(x).Damage(x.Strength, y, y.tile) != Unit.calculateDamage(x, y, y.tile, 0)) wrong++;
                }
                r.Add("damage as the game's for " + (pairs - wrong) + " of " + pairs + " pairs");
                float off = with(c => { c.fogThreat = false; }, m => m.sigint == null ? 0f : 1f);
                r.Add("fogThreat off: nothing " + (off == 0f));
                detail = string.Join("; ", r.ToArray());
                return saw && lost && phantom && notBelieved && zones && unknown && scout && wrong == 0 && pairs > 0 && off == 0f;
            }
            case "fogUnknownGround": {
                PinVision();
                Unit bomber = Rig.At(-5, 0).occupier, conscript = Rig.At(-5, 2).occupier;   // (the dear one is a tank)
                HexTile near = Rig.At(-2, 1);
                FogOfWar.Changed(); FogOfWar.Refresh();
                Func<bool, float, Unit, float[]> at = (on, unc, x) => {
                    AIProfile prof = ScriptableObject.CreateInstance<AIProfile>(); prof.fogUncertainty = on; prof.fogAggression = 1f;
                    float[] res = new float[3];
                    BoardView v = FogOfWar.Conceal(a, true, prof.fogMemoryTurns);
                    using (v != null ? v.Enter() : null) if (v != null) {
                        HexTactica.AI.AIMap m = new HexTactica.AI.AIMap(a, prof);
                        m.fogUncertainty = unc;
                        res[0] = m.MoveFogLoss(x, near); res[1] = HexTactica.AI.AIFog.FogRisk(x, near, m, prof);
                        // Everything else (supply, trucks, regrouping, targeting, the coordinator) prices only the enemy seen.
                        res[2] = m.ExpectedLoss(x, near) + m.FogLoss(x, near);
                    }
                    return res;
                };
                float[] b1 = at(true, 1f, bomber), b0 = at(true, 0f, bomber), bh = at(true, 0.5f, bomber), bOff = at(false, 1f, bomber), c1 = at(true, 1f, conscript);
                bool unseen = !FogOfWar.Sees(a, Rig.At(4, 0).occupier);
                detail = "elsewhere (expected loss and fog loss outside the move's fog risk) " + b1[2].ToString("0.##") + "; Red unseen " + unseen + "; at " + Pos(near) + " the tank's fog loss " + b1[0].ToString("0.##") + " strength (" + b1[1].ToString("0") + " gold) with none of Red placed, " + bh[0].ToString("0.##") + " with half, " + b0[0].ToString("0.##") + " with all, " + bOff[0].ToString("0.##") + " with fogUncertainty off; the conscript's " + c1[0].ToString("0.##") + " (" + c1[1].ToString("0") + " gold)";
                return b1[2] == 0f && unseen && b1[0] > 0f && bh[0] > 0f && bh[0] < b1[0] && b0[0] == 0f && bOff[0] == 0f && b1[1] > 2f * Mathf.Max(1f, c1[1]);
            }
            case "fogPrice": {
                PinVision();
                Unit bomber = Rig.At(-3, 0).occupier, conscript = Rig.At(-4, 2).occupier, aaU = Rig.At(1, 0).occupier, gr = Rig.At(1, 2).occupier;
                AIProfile prof = a.GetComponent<AIPlayer>().Profile;
                FogOfWar.Changed(); FogOfWar.Refresh();
                bool saw = FogOfWar.Sees(a, aaU) && FogOfWar.Sees(a, gr);
                aaU.TeleportTo(Rig.At(4, 1), 0); gr.TeleportTo(Rig.At(4, 3), 0);
                FogOfWar.Turn += 2; FogOfWar.Changed(); FogOfWar.Refresh();
                bool lost = !FogOfWar.Sees(a, aaU) && !FogOfWar.Sees(a, gr);
                HexTile near = Rig.At(2, 1);
                float bLoss = 0f, cLoss = 0f, bGold = 0f, cGold = 0f, bFromGrunt = 0f; List<string> lines = new List<string>();
                BoardView v = FogOfWar.Conceal(a, true, prof.fogMemoryTurns);
                using (v != null ? v.Enter() : null) if (v != null) {
                    HexTactica.AI.AIMap m = new HexTactica.AI.AIMap(a, prof);
                    bLoss = m.FogLoss(bomber, near); cLoss = m.FogLoss(conscript, near);
                    bGold = HexTactica.AI.AIFog.FogRisk(bomber, near, m, prof); cGold = HexTactica.AI.AIFog.FogRisk(conscript, near, m, prof);
                    if (m.sigint != null) foreach (string line in m.sigint.Describe()) lines.Add(line);
                }
                bFromGrunt = FogOfWar.Kind.Of(gr).Damage(gr.Strength, bomber, near);
                detail = "saw both " + saw + ", lost both " + lost + "; at " + Pos(near) + ": bomber " + bLoss.ToString("0.##") + " strength (" + bGold.ToString("0") + " gold), conscript " + cLoss.ToString("0.##") + " strength (" + cGold.ToString("0") + " gold); a grunt's damage to a bomber " + bFromGrunt + "; " + string.Join("; ", lines.ToArray());
                return saw && lost && bLoss > 0f && bFromGrunt == 0f && bGold > 3f * Mathf.Max(1f, cGold);
            }
            case "gameLogReplay": {
                List<string> r = new List<string>();
                string root = Path.Combine(Path.GetTempPath(), "hextactica-replay-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                List<string> boards = new List<string>();
                Action<Player> onTurn = p => { if (!gc.gameOver) boards.Add(Draw()); };
                boards.Add(Draw());
                HexTactica.Harness.GameLog.Start("replay check", "Full", root);
                GameController.TurnStarted += onTurn;
                try { for (int i = 0; i < 6 && !gc.gameOver; i++) ScenarioFile.PlayTurn(); }
                finally { GameController.TurnStarted -= onTurn; }
                string folder = HexTactica.Harness.GameLog.Folder;
                HexTactica.Harness.GameLog.Stop();
                HexTactica.Harness.Replay.Loader = f => { ScenarioFile.Load(f, "Full", null); return true; };
                bool opened = HexTactica.Harness.Replay.Open(folder);
                List<HexTactica.Harness.Replay.Turn> turns = HexTactica.Harness.Replay.turns;
                int withEvents = 0, withRound = 0;
                foreach (HexTactica.Harness.Replay.Turn t in turns) { if (t.events.Count > 0) withEvents++; if (t.round >= 0) withRound++; }
                bool read = opened && turns.Count == boards.Count && withRound == turns.Count && withEvents >= turns.Count - 1
                    && turns.Count > 1 && turns[1].side != turns[0].side;
                r.Add("recorded " + boards.Count + " turns; replay read " + turns.Count + ", " + withRound + " with their round, " + withEvents + " with events; first " + HexTactica.Harness.Replay.Title());
                bool frozen = HexTactica.Harness.Replay.Active && !HexTactica.Harness.GameLog.Recording;
                foreach (Player p in GameController.gc.players) { AIPlayer x = p.GetComponent<AIPlayer>(); if (x != null && x.enabled) frozen = false; }
                int same = 0;
                for (int i = 0; i < turns.Count && i < boards.Count; i++) { HexTactica.Harness.Replay.Show(i); if (Draw() == boards[i]) same++; }
                HexTactica.Harness.Replay.Show(turns.Count - 1); HexTactica.Harness.Replay.Previous();
                bool back = HexTactica.Harness.Replay.Index == turns.Count - 2 && Draw() == boards[turns.Count - 2];
                HexTactica.Harness.Replay.Next(); HexTactica.Harness.Replay.Next();
                bool clamp = HexTactica.Harness.Replay.Index == turns.Count - 1;
                // Action by action: each turn's logged actions played on its board should leave the next turn's board.
                int stepped = 0, sameAfter = 0, compared = 0; string firstOff = null;
                for (int i = 0; i + 1 < turns.Count && i + 1 < boards.Count; i++) {
                    HexTactica.Harness.Replay.Show(i);
                    while (HexTactica.Harness.Replay.StepsLeft > 0) { HexTactica.Harness.Replay.NextAction(); stepped++; }
                    compared++;
                    if (Draw() == boards[i + 1]) sameAfter++; else if (firstOff == null) firstOff = "turn " + i;
                }
                // Straight through with Next action: each turn ended as the game ends it, no board loaded in between.
                HexTactica.Harness.Replay.Show(0);
                HexTactica.Harness.Replay.Reloads = 0;
                int through = 0;
                while (HexTactica.Harness.Replay.NextAction() && through++ < 500) { }
                r.Add("played through to turn " + (HexTactica.Harness.Replay.Index + 1) + " with " + HexTactica.Harness.Replay.Reloads + " boards loaded on the way; ends as recorded " + (Draw() == boards[turns.Count - 1]));
                HexTactica.Harness.Replay.Show(turns.Count - 1);
                r.Add("actions played " + stepped + "; boards after a turn's actions equal to the next turn's " + sameAfter + "/" + compared + (firstOff != null ? " (first off: " + firstOff + ")" : ""));
                r.Add("boards as recorded " + same + " of " + turns.Count + "; back " + back + ", stops at the last " + clamp + "; nobody plays " + frozen);
                HexTile clickedWas = HexTile.lastClicked;
                Rig.At(0, 0).OnMouseDown();
                bool noClick = HexTile.lastClicked == clickedWas;
                HexTactica.Harness.Replay.View(0);
                bool asFirst = GameController.gc.fogOfWar && FogOfWar.Viewer == GameController.gc.players[0] && HexTactica.Harness.Replay.ViewName() == "as " + GameController.gc.players[0].personal_name + " sees it";
                HexTactica.Harness.Replay.CycleView();
                bool asSecond = FogOfWar.Viewer == GameController.gc.players[1];
                HexTactica.Harness.Replay.CycleView();
                bool all = !GameController.gc.fogOfWar && HexTactica.Harness.Replay.ViewAs == -1;
                r.Add("clicks ignored " + noClick + "; seen as " + GameController.gc.players[0].personal_name + " " + asFirst + ", as " + GameController.gc.players[1].personal_name + " " + asSecond + ", everything " + all);
                HexTactica.Harness.Replay.View(1);
                HexTactica.Harness.Replay.Close();
                bool closed = !HexTactica.Harness.Replay.Active && GameController.gc.fogOfWar && GameController.gc.fogViewer == -1;
                r.Add("closed, fog as it was " + closed);
                try { Directory.Delete(root, true); } catch (Exception) { }
                HexTactica.Harness.Replay.Loader = HexTactica.Harness.ScenarioLoader.LoadBoardFromPath;
                detail = string.Join("; ", r.ToArray());
                return stepped > 0 && read && frozen && same == turns.Count && back && clamp && noClick && asFirst && asSecond && all && closed;
            }
            case "fogGame": {
                int actions = 0, broken = 0, blindShots = 0, bumps = 0, shots = 0;
                string firstBroken = null;
                System.Action<Unit, Unit, HexTile> ran = (mover, hid, at) => bumps++;
                System.Action<Unit, Unit, bool> shot = (att, def, surprise) => { shots++; if (!surprise && !FogOfWar.Sees(att.owner, def)) blindShots++; };
                System.Action<Unit, HexTactica.AI.AIAction, bool> check = (u, act, did) => {
                    if (!did) return;   // a decision set aside within the thought (a rider staying aboard), not an action on the board
                    actions++;
                    string why = null;
                    if (FogOfWar.current != null) why = "left concealed";
                    foreach (Unit x in Rig.uc.units) {
                        if (x == null || x.Strength <= 0) continue;
                        if (x.tile == null) { why = x.name + " off the board"; break; }
                        if (x.trasnporter == null && x.tile.occupier != x) { why = x.name + " not on its tile"; break; }
                    }
                    foreach (HexTile t in Rig.hg.tiles) if (t.occupier != null && t.occupier.tile != t) { why = "tile holds a unit that is elsewhere"; break; }
                    if (why != null) { broken++; if (firstBroken == null) firstBroken = why + " after " + u.name.Replace("(Clone)", "") + " " + act.kind + " (" + act.reason + ")" + (u.trasnporter != null ? " aboard" : ""); }
                };
                Unit.Attacking += shot; Unit.RanInto += ran;
                foreach (Player p in new[] { a, o }) { AIPlayer x = p.GetComponent<AIPlayer>(); if (x != null) x.onDecision += check; }
                try { for (int i = 0; i < 16 && !gc.gameOver; i++) ScenarioFile.PlayTurn(); }
                finally { Unit.RanInto -= ran; Unit.Attacking -= shot; foreach (Player p in new[] { a, o }) { AIPlayer x = p.GetComponent<AIPlayer>(); if (x != null) x.onDecision -= check; } }
                detail = actions + " actions, " + shots + " shots (" + blindShots + " at units not seen), " + bumps + " units ran into hidden ones; board broken after " + broken + " actions" + (firstBroken != null ? " (" + firstBroken + ")" : "")
                       + "; left Blue " + a.units.Count + ", Red " + o.units.Count;
                return actions > 20 && broken == 0 && blindShots == 0;
            }
            case "destructible": {
                List<string> r = new List<string>();
                // The parts.
                int[] one = new int[11], two = new int[11];
                for (int left = 10; left >= 1; left--) { one[left] = TerrainData.Stage(10, 10 - left, 1); two[left] = TerrainData.Stage(10, 10 - left, 2); }
                bool parts = one[10] == 0 && one[6] == 0 && one[5] == 1 && one[1] == 1 && two[10] == 0 && two[7] == 0 && two[6] == 1 && two[4] == 1 && two[3] == 2 && two[1] == 2;
                r.Add("stages: one picture " + string.Join("", Array.ConvertAll(new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, x => one[x].ToString())) + ", two " + string.Join("", Array.ConvertAll(new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, x => two[x].ToString())) + " " + parts);
                // A real attack damages the defender's tile.
                Unit g = Rig.At(0, 0).occupier, red = Rig.At(1, 0).occupier;
                HexTile grass = Rig.At(0, 0);
                red.OrderAttack(g);
                bool hit = grass.damage == 1 && grass.combatCount == 1;
                for (int i = 0; i < 9; i++) grass.RegisterCombat();
                bool badlands = grass.terrain.name == "Badlands" && grass.damage == 0 && grass.destroyedFrom.Count == 1 && grass.destroyedFrom[0].name == "Grassland";
                bool undo = grass.UndoDestruction() && grass.terrain.name == "Grassland" && grass.damage == 9;
                grass.Repair(9);
                bool repaired = grass.damage == 0 && grass.damageLevel == 0;
                r.Add("attack damages the tile " + hit + "; ten hits: Grassland -> " + (badlands ? "Badlands" : grass.terrain.name) + " " + badlands + "; undone " + undo + ", repaired " + repaired);
                // The bridge falls with a grunt on it.
                HexTile bridge = Rig.At(3, -2);
                Unit onBridge = bridge.occupier;
                // The bridge's own data says what it falls into (Bridge -> BridgeCollapsed -> Shallows on main): hit it until it is a terrain that takes no damage.
                List<string> stagesSeen = new List<string> { bridge.terrain.name };
                for (int i = 0; i < 40 && bridge.terrain.health > 0; i++) { bridge.RegisterCombat(); if (stagesSeen[stagesSeen.Count - 1] != bridge.terrain.name) stagesSeen.Add(bridge.terrain.name); }
                bool fell = bridge.terrain.health == 0 && bridge.terrain.elevation < 0 && !bridge.road && bridge.occupier == null && onBridge.Strength <= 0 && !a.units.Contains(onBridge);
                r.Add("bridge: " + string.Join(" -> ", stagesSeen.ToArray()) + ", road " + bridge.road + ", grunt lost " + (onBridge.Strength <= 0) + " " + fell);
                // The city takes the hits; destroyed it stops working.
                HexTile ct = Rig.At(-3, 2);
                Building city = ct.building;
                TerrainData ground = ct.terrain;
                gc.EndTurn(); gc.EndTurn();
                for (int i = 0; i < 10; i++) ct.RegisterCombat();
                bool destroyed = city.Destroyed && !city.Functioning && ct.terrain == ground && ct.damage == 0;
                int m0 = a.money; gc.EndTurn(); gc.EndTurn(); int earned = a.money - m0;
                a.Resupply();
                int hqIncome = GameController.HQOf(a) != null ? GameController.HQOf(a).income : 0;
                bool stopped = destroyed && earned == hqIncome && !city.HasSupplyToGive && !city.CanProduce;
                // Destroyed it went neutral, and a ruin cannot be captured: the Blue grunt on it took nothing back.
                bool ruinNeutral = city.owner == null && city.capturingPlayer == null && city.capturePoints == 0;
                city.Repair(1);
                // Repaired, it is taken again from 0 by the grunt standing on it (6, then 12: Blue's), and earns again.
                int turns = 0;
                while (city.owner != a && turns < 4) { gc.EndTurn(); gc.EndTurn(); turns++; }
                int m1 = a.money; gc.EndTurn(); gc.EndTurn(); int earnedAgain = a.money - m1;
                bool back = !city.Destroyed && city.owner == a && turns == 2 && earnedAgain == hqIncome + city.income;
                r.Add("city: ground untouched, destroyed " + destroyed + ", turn's income " + earned + " (the HQ's " + hqIncome + "), no supply or production " + stopped
                      + "; as a ruin neutral and not taken back " + ruinNeutral + "; repaired by 1, retaken in " + turns + " turns: income " + earnedAgain + " " + back);
                back = back && ruinNeutral;
                // Water takes nothing.
                HexTile w = bridge;
                TerrainData wasWater = w.terrain;
                w.RegisterCombat();
                bool water = w.terrain == wasWater && w.damage == 0;
                r.Add("water takes no damage " + water);
                detail = string.Join("; ", r.ToArray());
                return parts && hit && badlands && undo && repaired && fell && stopped && back && water;
            }
            case "capturedHQ": {
                gc.rules = RuleSet.Sandbox;
                Building hq = GameController.HQOf(l.ai);
                bool home = hq.Home == l.ai;
                bool before = SupplyNetwork.Solve(l.ai, false).IsSupplied(hq.tile);
                hq.owner = l.other;
                bool red = SupplyNetwork.Solve(l.other, false).IsSupplied(hq.tile);
                bool blueOff = !SupplyNetwork.Solve(l.ai, false).IsSupplied(hq.tile);
                BuildingData saved = new BuildingData(hq);
                bool kept = saved.hasHome && saved.home_id == l.ai.id;
                hq.owner = l.ai;
                bool back = SupplyNetwork.Solve(l.ai, false).IsSupplied(hq.tile);
                SupplyNetwork.Solve(l.other, false);
                detail = "home Blue " + home + "; Blue supplied there " + before + "; taken by Red: Red supplied there " + red + ", Blue cut " + blueOff + "; saved home Blue " + kept + "; retaken: Blue supplied " + back;
                return home && before && !red && blueOff && kept && back;
            }
            case "sandboxTurns": {
                gc.rules = RuleSet.Sandbox;
                l.other.eliminated = true;
                List<string> order = new List<string>();
                int day0 = gc.days;
                for (int i = 0; i < 4; i++) { order.Add(gc.activePlayer.personal_name + "@" + (gc.days - day0)); gc.EndTurn(); }
                detail = string.Join(" ", order.ToArray());
                return order[0] != order[1].Split('@')[0] + "@" && order[0].Split('@')[0] != order[1].Split('@')[0] && order[0].Split('@')[0] == order[2].Split('@')[0] && order[1].Split('@')[0] == order[3].Split('@')[0];
            }
            case "transportLoad": {
                Unit apc = Rig.At(-4, 0).occupier, g1 = Rig.At(-4, 1).occupier, g2 = Rig.At(-5, 1).occupier, sup = Rig.At(-5, 0).occupier;
                Unit tank = Rig.At(-3, -1).occupier, fighter = Rig.At(-6, 0).occupier, ship = Rig.At(3, 0).occupier;
                List<string> r = new List<string>();
                bool a1 = Transporting.Load(apc, g1), a2 = Transporting.Load(apc, g2), a3 = !Transporting.Load(apc, sup);
                r.Add("two grunts " + (a1 && a2) + ", then supplies refused " + a3);
                bool b1 = Transporting.Unload(apc, g1, Rig.At(-4, 1)) && Transporting.Unload(apc, g2, Rig.At(-5, 1));
                bool b2 = Transporting.Load(apc, sup);
                r.Add("emptied " + b1 + ", supplies alone " + b2);
                Unit tankBeside = tank; tank.TeleportTo(Rig.At(-3, 0), 0);
                bool c1 = !Transporting.CanCarry(apc, tankBeside) && !Transporting.CanCarry(apc, fighter);
                r.Add("tank and fighter refused " + c1);
                // The ship beside the shore on open water: a grunt boards it from the shore (any coastal tile), and at the dock.
                Building yard = Rig.At(-2, 0).building;
                HexTile dock = yard.tile;   // ships come in to the yard itself (no dock tiles)
                HexTile beach = null, shore = null;
                foreach (HexTile t in HexGenerator.hg.tiles) {
                    if (t.terrain.elevation >= 0 || t.building != null || t.occupier != null) continue;
                    foreach (HexTile n in t.neighbors) if (n != null && n.terrain.elevation >= 0 && n.occupier == null && n.building == null && t.Distance(Rig.At(-2, 0)) > 3) { beach = t; shore = n; break; }
                    if (beach != null) break;
                }
                ship.TeleportTo(beach, 0); g1.TeleportTo(shore, 0);
                bool d1 = Transporting.CanBoardFrom(ship, g1);
                HexTile quay = null;
                foreach (HexTile n in dock.neighbors) if (n != null && n.terrain.elevation >= 0 && n.occupier == null && n.building == null) { quay = n; break; }
                ship.TeleportTo(dock, 0); g1.TeleportTo(quay, 0);
                bool d2 = Transporting.CanBoardFrom(ship, g1);
                r.Add("grunt boards from the shore " + d1 + ", at the dock " + d2);
                apc.TeleportTo(quay == null ? null : quay, 0);
                g1.TeleportTo(Rig.At(-6, 2), 0);
                bool e1 = Transporting.Load(ship, apc);
                HexTile sea = Rig.At(4, 1);
                ship.TeleportTo(sea, 0);
                bool e2 = apc.tile == sea && sup.tile == sea && Transporting.FreeWeight(ship) == ship.transportSlots - Mathf.Max(1, apc.weight);
                r.Add("loaded APC boards at the dock " + e1 + ", rides with its cargo " + e2);
                detail = string.Join("; ", r.ToArray());
                return a1 && a2 && a3 && b1 && b2 && c1 && d1 && d2 && e1 && e2;
            }
            case "attritionLandRefuel": {
                Unit f = Rig.At(0, 2).occupier;
                HexTile runway = Rig.At(-3, 3);
                Building ap = Rig.At(-3, 2).building;
                bool ditched = false; Action<Unit> hook = u => { if (u == f) ditched = true; };
                f.TeleportTo(runway, 3);
                string landed = "plain move: on landing fuel " + f.fuel + ", ammo " + f.ammo;
                bool ok1 = f.fuel == f.fuelMax && f.ammo == f.ammoMax;
                Unit g = Rig.At(0, 4).occupier;
                HexTile other = null;
                foreach (Building rw in Runways(ap)) if (rw.tile != runway && rw.tile.occupier == null && other == null) other = rw.tile;
                g.TeleportTo(other, 3, true);
                landed += "; move and attack: fuel " + g.fuel + ", ammo " + g.ammo;
                ok1 = ok1 && g.fuel == 2 && g.ammo == 1;
                ap.owner = l.other;
                foreach (Building rw in Runways(ap)) rw.owner = l.other;
                Unit.Ditched += hook;
                try { endTurn(); endTurn(); } finally { Unit.Ditched -= hook; }
                detail = landed + "; airport lost, next turn fuel " + f.fuel + (ditched || f.tile == null ? ", ditched" : ", flying");
                return ok1 && !ditched && f.tile != null && f.fuel == f.fuelMax - f.minFuelPerTurn;
            }
            case "attritionTanker": {
                Unit f = Rig.At(0, 0).occupier, g = Rig.At(2, 0).occupier, t = Rig.At(1, 0).occupier, idle = Rig.At(-3, 4).occupier, pair = Rig.At(-2, 4).occupier;
                t.supplies = 2; idle.supplies = 2; pair.supplies = 2;
                bool besideIdle = idle.tile.neighbors.Contains(pair.tile);
                endTurn(); endTurn();
                string first = "fighters " + f.fuel + "/" + g.fuel + ", tanker " + t.supplies;
                bool ok1 = f.fuel == f.fuelMax && g.fuel == g.fuelMax && t.supplies == 1;
                f.fuel = 6; g.fuel = 6;
                endTurn(); endTurn();
                string second = "fighters " + f.fuel + "/" + g.fuel + ", tanker " + t.supplies;
                bool ok2 = f.fuel == f.fuelMax && g.fuel == g.fuelMax && t.supplies == 0;
                int before = f.fuel, tStr = t.Strength;
                endTurn(); endTurn();
                bool ok3 = f.fuel == before - f.minFuelPerTurn && t.supplies == 0 && t.Strength == tStr - 1;
                bool pairDrain = idle.supplies == 0 && pair.supplies == 0;
                detail = "tankers side by side " + besideIdle + "; turn 1: " + first + "; turn 2: " + second + "; turn 3: fighter " + before + " -> " + f.fuel + ", tanker strength " + tStr + " -> " + t.Strength + "; the side-by-side tankers' supplies after three turns " + idle.supplies + "/" + pair.supplies;
                return ok1 && ok2 && ok3 && pairDrain && besideIdle;
            }
            case "attritionShip": {
                Unit away = Rig.At(1, 2).occupier, dock = Rig.At(-2, 0).occupier;
                int before = away.fuel;
                endTurn(); endTurn();
                int idle = away.fuel;
                away.TeleportTo(Rig.At(1, 5), 3);
                endTurn(); endTurn();
                detail = "away from port: fuel " + before + " -> " + idle + " idle, " + away.fuel + " after 3 tiles, strength " + away.Strength + "; on the dock: strength " + dock.Strength + ", fuel " + dock.fuel;
                return idle == before && away.fuel == before - 3 && away.Strength == 6 && dock.Strength == 5 && dock.fuel == dock.fuelMax;
            }
            case "lineOfFire": {
                Unit tank = Rig.At(-1, 0).occupier, art = Rig.At(-1, 2).occupier, g = Rig.At(1, 0).occupier;
                tank.AttackRange = 2;
                AIMap map = new AIMap(l.ai, l.ai.GetComponent<AIPlayer>().Profile);
                bool hillBlocks = !tank.CanAttackFrom(tank.tile, g) && !tank.tile.CheckAttackRange(tank).Contains(g.tile)
                    && !AITargeting.Appraise(tank, g, tank.tile, map, map.profile, Posture.Aggressive, false).valid;
                bool artOver = CombatMath.InFireRange(art, art.tile, g.tile, g);
                Rig.At(-1, 0).terrain = T("Hills");
                bool fromHill = tank.CanAttackFrom(tank.tile, g);
                Rig.At(-1, 0).terrain = T("Grassland"); Rig.At(0, 0).terrain = T("Grassland");
                bool open = tank.CanAttackFrom(tank.tile, g) && tank.tile.CheckAttackRange(tank).Contains(g.tile);
                detail = "across a hill: blocked " + hillBlocks + "; from a hill of its own: fires " + fromHill + "; over grassland: fires " + open + "; artillery over the hill: fires " + artOver;
                return hillBlocks && fromHill && open && artOver;
            }
            case "boardThroughFriend": {
                Unit apc = Rig.At(0, 0).occupier, g = Rig.At(-2, 1).occupier;
                bool before = !Transporting.CanBoardFrom(apc, g);
                List<HexTile> steps = HexTile.BoardingPath(g, apc);
                HexTile via = steps != null ? steps[steps.Count - 1] : null;
                Unit friend = via != null ? via.occupier : null;
                bool aiSees = via != null && Transporting.CanBoardFromTile(apc, g, via);
                bool boarded = steps != null && g.WalkAndBoard(steps, apc, false);
                bool kept = friend != null && friend.tile == via && via.occupier == friend && friend.owner == a;
                detail = "not beside it: plain boarding refused " + before + "; path " + (steps != null ? steps.Count + " tiles, via " + Pos(via) + " (" + (friend != null ? friend.name : "nobody") + ")" : "none")
                       + "; AI may board from there " + aiSees + "; boarded " + boarded + " (aboard " + (g.trasnporter == apc) + "); the tank kept its tile " + kept + "; the grunt's old tile free " + (Rig.At(-2, 1).occupier == null);
                return before && aiSees && boarded && g.trasnporter == apc && kept && Rig.At(-2, 1).occupier == null;
            }
            case "runwayNoCapture": {
                Building ap = Rig.At(-3, 2).building;
                endTurn(); endTurn();
                detail = "Red grunt on a runway tile: " + show(ap);
                return ap.owner == a && ap.capturePoints == 0 && ap.capturingPlayer == null;
            }
            case "attritionBoats": {
                Unit g = Rig.At(2, 0).occupier, far = Rig.At(1, 0).occupier;
                SupplyNetwork.Result with = SupplyNetwork.Solve(a, false);
                bool withBoats = with.IsSupplied(g.tile);
                int linked = with.connectedHubs.Count;
                far.TeleportTo(Rig.At(0, 6), 0);
                SupplyNetwork.Result without = SupplyNetwork.Solve(a, false);
                bool withoutFar = without.IsSupplied(g.tile);
                bool yardSupplied = with.IsSupplied(Rig.At(-3, 0));
                detail = "naval yard in supply " + yardSupplied + "; island grunt supplied with both haulers " + withBoats + ", without the far one " + withoutFar
                         + "; supply aboard linked " + with.connectedHubs.Count;
                return yardSupplied && withBoats && !withoutFar && linked == 2;
            }
            case "shipMends": {
                Unit g = Rig.At(0, 0).occupier, dry = Rig.At(0, 4).occupier;
                endTurn(); endTurn(); string one = g.Strength + " (" + g.supplies + " supplies)";
                bool ok1 = g.Strength == 4 && g.supplies == 4 && dry.Strength == 2;
                endTurn(); endTurn();
                detail = "with supplies: 3 -> " + one + " -> " + g.Strength + "; without: 3 -> " + dry.Strength + " after two turns";
                return ok1 && g.Strength == 5 && dry.Strength == 1;
            }
            case "shipTender": {
                // A supply ship (Player.IsSupplyShip: a ship with a supply point of its own) tends the ships beside it. No
                // unit in the game is one since the SupplyBoat went; the rule (and this check) wait for one.
                if (!Player.IsSupplyShip(Rig.At(0, 0).occupier)) { detail = "SKIP: no supply ship in the game (the SupplyBoat was removed): nothing tends ships at sea"; return true; }
                Unit g = Rig.At(1, 0).occupier, far = Rig.At(0, 5).occupier, sb2 = Rig.At(-1, 0).occupier;
                endTurn(); endTurn();
                detail = "beside it: fuel " + g.fuel + "/" + g.fuelMax + ", supplies " + g.supplies + "/" + g.suppliesMax + "; away: fuel " + far.fuel + "; the supply ship beside it: fuel " + sb2.fuel;
                return g.fuel == g.fuelMax && g.supplies == g.suppliesMax && far.fuel == 10 && sb2.fuel == 10;
            }
            case "shipCarriedSupply": {
                Unit ta = Rig.At(-1, 0).occupier, tb = Rig.At(3, 0).occupier, tc = Rig.At(5, 0).occupier, helo = Rig.At(-5, 3).occupier;
                Unit sa = Rig.At(-6, 2).occupier, sb = Rig.At(-6, 3).occupier, sc = Rig.At(-6, 4).occupier, sh = Rig.At(-5, 4).occupier;
                Unit g = Rig.At(6, -1).occupier;
                foreach (Unit t in new[] { ta, tb, tc }) t.moveRange = 3;
                Action<Unit, Unit> aboard = (ship, sup) => { sup.tile.occupier = null; sup.tile = ship.tile; sup.trasnporter = ship; ship.transported.Add(sup); };
                aboard(ta, sa); aboard(tb, sb); aboard(tc, sc);
                SupplyNetwork.Result r = SupplyNetwork.Solve(a, false);
                bool chain = r.connectedHubs.Contains(sa.Hub) && r.connectedHubs.Contains(sb.Hub) && r.connectedHubs.Contains(sc.Hub) && r.IsSupplied(g.tile);
                SupplyPoint viaC, viaB;
                bool through = r.linkedThrough.TryGetValue(sc.Hub, out viaC) && viaC == sb.Hub && r.linkedThrough.TryGetValue(sb.Hub, out viaB) && viaB == sa.Hub && !r.linkedThrough.ContainsKey(sa.Hub);
                bool atSeaNothing = r.DepthAt(ta.tile) == 0 && r.DepthAt(tb.tile) == 0;
                List<HexTile> line = SupplyOverlay.SourcePath(a, g.tile, r, null);
                Building hq = GameController.HQOf(a);
                bool drawn = line.Contains(tc.tile) && line.Contains(tb.tile) && line.Contains(ta.tile) && hq != null && line.Contains(hq.tile)
                             && line.IndexOf(tc.tile) < line.IndexOf(tb.tile) && line.IndexOf(tb.tile) < line.IndexOf(ta.tile);
                // Without A: nothing for B or C to reach.
                HexTile aAt = ta.tile;
                ta.TeleportTo(Rig.At(-3, -3), 0); sa.tile = ta.tile;
                SupplyNetwork.Result cut = SupplyNetwork.Solve(a, false);
                bool cutOff = !cut.connectedHubs.Contains(sb.Hub) && !cut.connectedHubs.Contains(sc.Hub) && !cut.IsSupplied(g.tile);
                // A helicopter with supplies in A's place over the water: B and C link through it.
                ta.TeleportTo(Rig.At(-4, -3), 0); sa.tile = ta.tile;
                helo.TeleportTo(aAt, 0); aboard(helo, sh);
                SupplyNetwork.Result viaAir = SupplyNetwork.Solve(a, false);
                SupplyPoint viaH;
                bool air = viaAir.connectedHubs.Contains(sh.Hub) && viaAir.DepthAt(helo.tile) == 0 && viaAir.linkedThrough.TryGetValue(sb.Hub, out viaH) && viaH == sh.Hub
                           && viaAir.connectedHubs.Contains(sc.Hub) && viaAir.IsSupplied(g.tile);
                detail = "A, B, C linked and the island supplied " + chain + "; C through B through A " + through + "; A and B at sea give nothing " + atSeaNothing
                       + "; line through C, B, A to the HQ " + drawn + " (" + line.Count + " tiles); without A cut off " + cutOff + "; through a helicopter over the water " + air;
                return chain && through && atSeaNothing && drawn && cutOff && air;
            }
            case "carrierRule": {
                Unit c1 = Rig.At(0, 0).occupier, c2 = Rig.At(0, 4).occupier, f1 = Rig.At(1, 0).occupier, b = Rig.At(-1, 0).occupier, g = Rig.At(-6, 2).occupier, f2 = Rig.At(1, 4).occupier;
                Building yard = Rig.At(-5, 0).building;
                bool sold = false; foreach (Unit u in yard.Purchasable()) if (u != null && u.name.Replace("(Clone)", "") == "Carrier") sold = true;
                bool takesAir = Transporting.CanCarry(c1, f1) && Transporting.CanCarry(c1, b), takesGrunt = Transporting.CanCarry(c1, g);
                bool boardF = Transporting.CanBoardFrom(c1, f1);
                Transporting.Load(c1, f1); f1.FinishBoarding();
                Transporting.Load(c1, b); b.FinishBoarding();
                Transporting.Load(c2, f2); f2.FinishBoarding();
                bool full = Transporting.FreeWeight(c1) == 0 && f1.trasnporter == c1 && b.trasnporter == c1 && f2.trasnporter == c2;
                f1.fuel = 4; f1.Strength = 3; b.fuel = 10; f2.fuel = 4; c1.supplies = 10; c2.supplies = 0;
                endTurn(); endTurn();   // Blue's next turn starts: upkeep
                bool refuelled = f1.fuel == f1.fuelMax && b.fuel == b.fuelMax && f1.Strength == 4;
                bool notRefuelled = f2.fuel < 4;
                detail = "yard sells it " + sold + "; carries fighter and bomber " + takesAir + ", grunt " + takesGrunt + "; fighter boards from beside " + boardF + "; both aboard, full " + full
                       + "; stocked carrier: fighter fuel " + f1.fuel + "/" + f1.fuelMax + " strength " + f1.Strength + ", bomber " + b.fuel + "/" + b.fuelMax + "; empty carrier: fighter fuel " + f2.fuel;
                return sold && takesAir && !takesGrunt && boardF && full && refuelled && notRefuelled;
            }
            case "shipSupplyUpkeep": {
                Unit ship = Rig.At(-1, 0).occupier, apc = Rig.At(-6, 1).occupier, sup = Rig.At(-6, 2).occupier, g = Rig.At(-6, 3).occupier;
                ship.moveRange = 3;
                Action<Unit, Unit> aboard = (t, c) => { c.tile.occupier = null; c.tile = t.tile; c.trasnporter = t; t.transported.Add(c); c.transform.SetParent(t.transform); };
                aboard(apc, sup); aboard(ship, apc); sup.tile = ship.tile; aboard(ship, g);
                bool loaded = sup.trasnporter == apc && apc.trasnporter == ship && g.trasnporter == ship && sup.tile == ship.tile;
                g.Strength = 3; g.ammo = 0; g.fuel = 5; ship.Strength = 3; ship.supplies = 10;
                Unit gun = Rig.At(0, 0).occupier;
                gun.Strength = 3; gun.supplies = 10;
                SupplyNetwork.Result r = SupplyNetwork.Solve(a, false);
                HexTile island = Rig.At(0, -1);
                bool nested = r.connectedHubs.Contains(sup.Hub) && r.IsSupplied(island);
                int shipTile = r.DepthAt(ship.tile);
                endTurn(); endTurn();   // Blue's next turn starts: upkeep
                bool cargo = g.Strength == 4 && g.ammo == g.ammoMax && g.fuel == g.fuelMax;
                bool shipOnce = ship.Strength == 4;
                // Away from the island and the shore, out of reach of Blue's supply: what it supplied is not any more.
                HexTile far = Rig.At(4, 5);
                List<HexTile> was = new List<HexTile>();
                foreach (HexTile t in HexGenerator.hg.tiles) if (t.isSupplied(a) && t.Distance(ship.tile) <= 3 && t.Distance(Rig.At(-6, 0)) > 6) was.Add(t);
                ship.TeleportTo(far, 0);
                a.Resupply();
                int still = 0; foreach (HexTile t in was) if (t.isSupplied(a)) still++;
                detail = "loaded " + loaded + "; supply two deep linked, island supplied " + nested + "; the hauler's own tile depth " + shipTile
                       + "; after upkeep: grunt aboard strength " + g.Strength + " ammo " + g.ammo + "/" + g.ammoMax + " fuel " + g.fuel + "/" + g.fuelMax + " (resupplied " + cargo + "), hauler 3 -> " + ship.Strength
                       + "; gunboat in the zone 3 -> " + gun.Strength + " (its tile supplied " + r.IsSupplied(gun.tile) + ")"
                       + "; tiles it supplied " + was.Count + ", still supplied after it left " + still;
                return loaded && nested && shipTile == 0 && cargo && shipOnce && was.Count > 0 && still == 0;
            }
            case "shipLineOnWater": {
                Unit ta = Rig.At(-1, 0).occupier, tb = Rig.At(3, 0).occupier;
                Unit sa = Rig.At(-6, 2).occupier, sb = Rig.At(-6, 3).occupier;
                foreach (Unit t in new[] { ta, tb }) t.moveRange = 3;
                Action<Unit, Unit> aboard = (ship, sup) => { sup.tile.occupier = null; sup.tile = ship.tile; sup.trasnporter = ship; ship.transported.Add(sup); };
                aboard(ta, sa); aboard(tb, sb);
                SupplyNetwork.Result r = SupplyNetwork.Solve(a, false);
                SupplyPoint via;
                bool linked = r.connectedHubs.Contains(sa.Hub) && r.connectedHubs.Contains(sb.Hub) && r.linkedThrough.TryGetValue(sb.Hub, out via) && via == sa.Hub;
                Building hq = GameController.HQOf(a);
                Func<List<HexTile>, HexTile, HexTile, bool> wet = (line, x, y) => {
                    int i = line.IndexOf(x), j = line.IndexOf(y);
                    if (i < 0 || j < 0) return false;
                    for (int k = Math.Min(i, j); k <= Math.Max(i, j); k++) if (line[k].terrain.elevation >= 0) return false;
                    return true;
                };
                List<HexTile> fromB = SupplyOverlay.SourcePath(a, tb.tile, r, null);
                bool bLine = wet(fromB, tb.tile, ta.tile) && fromB.Contains(hq.tile);
                Dictionary<SupplyPoint, List<HexTile>> deps = SupplyOverlay.Dependents(a, new List<SupplyPoint> { sa.Hub }, ta.tile, r, null);
                List<HexTile> dep;
                bool depLine = deps.TryGetValue(sb.Hub, out dep) && wet(dep, tb.tile, ta.tile);
                List<HexTile> fromA = SupplyOverlay.SourcePath(a, ta.tile, r, null);
                bool aLine = fromA.Count > 1 && fromA.Contains(hq.tile);
                HexTile sea = Rig.At(-2, 2);
                Dictionary<SupplyPoint, HexTile> preview = new Dictionary<SupplyPoint, HexTile> { { sa.Hub, sea } };
                SupplyNetwork.Result pf = SupplyNetwork.SolvePreview(a, preview);
                List<HexTile> moved = SupplyOverlay.SourcePath(a, sea, pf, preview);
                bool previewLine = sea.terrain.elevation < 0 && moved.Count > 1 && moved.Contains(hq.tile);
                SupplyNetwork.Solve(a, false);
                detail = "B through A " + linked + "; B's line on the water to A and on to the HQ " + bLine + " (" + string.Join(" ", fromB.ConvertAll(t => Pos(t))) + ")"
                       + "; B hanging on A, on the water " + depLine + "; A at sea shows its line " + aLine + "; A previewed further out shows its line " + previewLine;
                return linked && bLine && depLine && aLine && previewLine;
            }
            case "seaLift": {
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) { AIPlayer.Trace = Console.WriteLine; HexTactica.AI.AIProduction.DebugValues = Console.WriteLine; }
                List<string> r = new List<string>();
                a.money = 20000;
                int landed = -1, bought = -1, blueTurns = 0;
                for (int turn = 0; turn < 24 && !gc.gameOver; turn++) {
                    bool blue = gc.activePlayer == a;
                    ScenarioFile.PlayTurn();
                    if (!blue) continue;
                    blueTurns++;
                    string line = "T" + blueTurns + ":";
                    foreach (Unit u in a.units) {
                        if (u == null || u.tile == null) continue;
                        line += " " + u.name.Replace("(Clone)", "") + (u.trasnporter != null ? "[aboard]" : Pos(u.tile));
                        if (LocomotionRules.IsNaval(u.locomotion) && Transporting.IsTransport(u) && bought < 0) bought = blueTurns;
                        if (u.trasnporter == null && u.tile.x >= 3 && u.locomotion == Locomotion.Boots && landed < 0) landed = blueTurns;
                    }
                    r.Add(line + " gold " + a.money);
                }
                detail = string.Join(" | ", r.ToArray()) + "; ship from Blue turn " + bought + ", landed on Red's shore turn " + landed + "; game over " + gc.gameOver + (gc.winner != null ? ", " + gc.winner.personal_name + " wins" : "") + " after " + blueTurns + " Blue turns";
                return bought > 0 && landed > 0 && gc.gameOver && gc.winner == a;
            }
            case "attritionSeaSupply": {
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) AIPlayer.Trace = Console.WriteLine;
                Unit g1 = Rig.At(4, 1).occupier, g2 = Rig.At(4, 2).occupier, sup = Rig.At(-3, 1).occupier, ship = Rig.At(-2, 0).occupier;
                List<string> r = new List<string>();
                int supplied = -1, blueTurns = 0;
                for (int turn = 0; turn < 6 && !gc.gameOver && supplied < 0; turn++) {
                    bool blue = gc.activePlayer == a;
                    ScenarioFile.PlayTurn();
                    if (!blue) continue;
                    blueTurns++;
                    SupplyNetwork.Result net = SupplyNetwork.Solve(a, false);
                    bool both = g1 != null && g2 != null && g1.tile != null && g2.tile != null && net.IsSupplied(g1.tile) && net.IsSupplied(g2.tile);
                    r.Add("T" + blueTurns + ": supplies " + (sup.trasnporter != null ? "aboard" : Pos(sup.tile)) + ", ship " + Pos(ship.tile) + ", grunts " + Pos(g1.tile) + " " + Pos(g2.tile) + " supplied " + both);
                    if (both) supplied = blueTurns;
                }
                detail = string.Join(" | ", r.ToArray());
                return supplied > 0 && supplied <= 3;
            }
            case "nestedGhost": {
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) AIPlayer.Trace = Console.WriteLine;
                List<string> bad = new List<string>(), trail = new List<string>();
                Unit v = null; foreach (Unit u in a.units) if (u != null && u.name.StartsWith("Transport Helo")) v = u;
                // The order the AI gave in that game, carried out as it was: the VTOL told to fly on while aboard.
                if (v != null && gc.activePlayer == a) {
                    HexTile to = null;
                    foreach (HexTile n in v.tile.neighbors) if (n != null && n.occupier == null && v.CanEnter(n) && (to == null || CombatMath.Precedes(n, to))) to = n;
                    if (to != null) {
                        // (Given on purpose: not counted among the AI's illegal orders.)
                        var illegal = AIExecutor.OnIllegal; AIExecutor.OnIllegal = null;
                        try { AIExecutor.Execute(AIAction.Move(v, to, 0f, "test: the order from the game"), new AIMap(a, a.GetComponent<AIPlayer>().Profile), true); }
                        finally { AIExecutor.OnIllegal = illegal; }
                        trail.Add("ordered to fly to " + Pos(to) + " while aboard: " + (v.trasnporter != null ? "stays aboard" : "LEFT at " + Pos(v.tile)) + ", tile names " + (to.occupier != null ? to.occupier.name.Replace("(Clone)", "") : "nobody"));
                    }
                }
                for (int turn = 0; turn < 8 && !gc.gameOver; turn++) {
                    bool blue = gc.activePlayer == a;
                    ScenarioFile.PlayTurn();
                    foreach (HexTile t in Rig.hg.tiles) {
                        Unit oc = t.occupier;
                        if (oc != null && (oc.tile != t || oc.trasnporter != null || oc.Strength <= 0)) bad.Add("turn " + turn + ": (" + t.x + "," + t.y + ") names " + oc.name.Replace("(Clone)", "") + (oc.trasnporter != null ? " (aboard)" : " (at " + Pos(oc.tile) + ")"));
                    }
                    if (blue && v != null) trail.Add("VTOL " + (v.Strength <= 0 ? "lost" : v.trasnporter != null ? "aboard at " + Pos(v.tile) : Pos(v.tile)) + " carrying " + v.transported.Count);
                }
                detail = string.Join(" | ", trail.ToArray()) + (bad.Count > 0 ? "; GHOSTS: " + string.Join("; ", bad.ToArray()) : "; no ghosts");
                return bad.Count == 0;
            }
            case "carrierKeeps": {
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) AIPlayer.Trace = Console.WriteLine;
                Unit carrier = Rig.At(1, 0).occupier, bomber = Rig.At(2, 0).occupier;
                bomber.fuel = 3;
                Transporting.Load(carrier, bomber); bomber.FinishBoarding();
                bool inactive = !bomber.active;
                AIAction put = HexTactica.AI.AIAir.KeepsUnrefuelledAboard(AIAction.Disembark(carrier, bomber, Rig.At(2, 0), "test"));
                AIAction moveput = HexTactica.AI.AIAir.KeepsUnrefuelledAboard(AIAction.MoveAndDisembark(carrier, Rig.At(0, 0), bomber, Rig.At(0, 1), "test"));
                bool guard = put.kind == AIActionKind.Wait && moveput.kind == AIActionKind.Move && moveput.destination == Rig.At(0, 0);
                if (gc.activePlayer == a) ScenarioFile.PlayTurn();
                bool stayed = bomber.trasnporter == carrier;
                bomber.active = true;   // as at the start of its next turn
                AIAction later = HexTactica.AI.AIAir.KeepsUnrefuelledAboard(AIAction.Disembark(carrier, bomber, Rig.At(2, 0), "test"));
                detail = "landed this turn (not active) " + inactive + "; put-down order becomes " + put.kind + ", move-and-put-down becomes " + moveput.kind + "; aboard after Blue's turn " + stayed + "; next turn the order stands " + (later.kind == AIActionKind.Unload);
                return inactive && guard && stayed && later.kind == AIActionKind.Unload;
            }
            case "carrierAI": {
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) AIPlayer.Trace = Console.WriteLine;
                Unit carrier = Rig.At(-2, 2).occupier, bomber = Rig.At(2, -3).occupier;
                bomber.fuel = 9;
                List<string> r = new List<string>();
                int landed = -1, refuelled = -1, launched = -1, blueTurns = 0; bool outEarly = false;
                for (int turn = 0; turn < 8 && !gc.gameOver && launched < 0; turn++) {
                    bool blue = gc.activePlayer == a;
                    if (blue && landed > 0 && refuelled < 0 && bomber.trasnporter == carrier && bomber.fuel == bomber.fuelMax) refuelled = blueTurns + 1;
                    ScenarioFile.PlayTurn();
                    if (!blue) continue;
                    blueTurns++;
                    if (bomber.Strength <= 0) { r.Add("T" + blueTurns + ": bomber lost"); break; }
                    bool aboard = bomber.trasnporter == carrier;
                    if (landed < 0 && aboard) landed = blueTurns;
                    if (landed > 0 && !aboard && refuelled < 0) outEarly = true;
                    if (landed > 0 && refuelled > 0 && !aboard && launched < 0) launched = blueTurns;
                    r.Add("T" + blueTurns + ": carrier " + Pos(carrier.tile) + ", bomber " + (aboard ? "aboard" : Pos(bomber.tile)) + " fuel " + bomber.fuel);
                }
                detail = string.Join(" | ", r.ToArray()) + "; landed on Blue turn " + landed + ", refuelled for turn " + refuelled + ", out again on turn " + launched + (outEarly ? ", PUT OUT BEFORE REFUELLING" : "");
                return landed > 0 && refuelled > landed && launched >= refuelled && !outEarly;
            }
            case "seaMeeting": {
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) AIPlayer.Trace = Console.WriteLine;
                Unit ship = Rig.At(1, -6).occupier, g1 = Rig.At(-5, 3).occupier, g2 = Rig.At(-4, 3).occupier;
                HexTile hq = GameController.HQOf(o).tile;
                AIMap m = new AIMap(a, a.GetComponent<AIPlayer>().Profile);
                int sea = m.SeaTravel(g1.tile, hq);
                int shipStart = ship.tile.Distance(g1.tile);
                List<string> r = new List<string>();
                int boarded = -1, landed = -1, blueTurns = 0; int closest = shipStart;
                for (int turn = 0; turn < 16 && !gc.gameOver && landed < 0; turn++) {
                    bool blue = gc.activePlayer == a;
                    ScenarioFile.PlayTurn();
                    if (!blue) continue;
                    blueTurns++;
                    if (ship.tile != null) closest = Math.Min(closest, ship.tile.Distance(g1.tile.Distance(g2.tile) >= 0 && g1.trasnporter == null ? g1.tile : ship.tile));
                    if (boarded < 0 && (g1.trasnporter == ship || g2.trasnporter == ship)) boarded = blueTurns;
                    foreach (Unit g in new[] { g1, g2 }) if (landed < 0 && g.trasnporter == null && g.tile != null && g.tile.x >= 3) landed = blueTurns;
                    r.Add("T" + blueTurns + ": ship " + Pos(ship.tile) + " carrying " + ship.transported.Count + ", grunts " + (g1.trasnporter != null ? "aboard" : Pos(g1.tile)) + " " + (g2.trasnporter != null ? "aboard" : Pos(g2.tile)));
                }
                detail = "sea travel from the grunt to Red's HQ " + (sea == int.MaxValue ? "none" : sea.ToString()) + "; " + string.Join(" | ", r.ToArray()) + "; boarded on Blue turn " + boarded + ", landed on turn " + landed;
                return sea < int.MaxValue && boarded > 0 && landed > 0;
            }
            case "seaConvoy": {
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) AIPlayer.Trace = Console.WriteLine;
                Unit ship = Rig.At(-2, 0).occupier, sup = Rig.At(-3, 1).occupier, tank = Rig.At(-3, 0).occupier, g = Rig.At(-2, 1).occupier;
                // The grunt starts aboard: a loaded transport moves first in the AI's turn order, before the others can get in.
                bool gruntIn = Transporting.Load(ship, g);
                List<string> r = new List<string>();
                bool allAboard = false, sailedFull = false, landedSupplied = false;
                int blueTurns = 0;
                for (int turn = 0; turn < 8 && !gc.gameOver; turn++) {
                    bool blue = gc.activePlayer == a;
                    HexTile before = ship.tile;
                    ScenarioFile.PlayTurn();
                    if (!blue) continue;
                    blueTurns++;
                    bool aboard = sup.trasnporter == ship && tank.trasnporter == ship && g.trasnporter == ship;
                    if (blueTurns == 1) allAboard = aboard || (sup.trasnporter == ship && ship.tile != before);
                    if (ship.tile != before && sup.trasnporter == ship && Transporting.CarriedWeight(ship) == ship.transportSlots) sailedFull = true;
                    SupplyNetwork.Result net = SupplyNetwork.Solve(a, false);
                    foreach (Unit u in new[] { tank, g }) if (u != null && u.trasnporter == null && u.tile != null && u.tile.x >= 3 && net.IsSupplied(u.tile)) landedSupplied = true;
                    // (unifiedLogistics: the ship with the supply aboard holds off the shore as that shore's supply link, the troops
                    // free to go on from there: their landing ground, beside the ship, is supplied.)
                    bool putDown = (tank.trasnporter == null && tank.tile != null && tank.tile.x >= 3) || (g.trasnporter == null && g.tile != null && g.tile.x >= 3);
                    if (putDown && sup.trasnporter == ship && ship.tile != null)
                        foreach (HexTile x in ship.tile.neighbors) if (x != null && x.terrain != null && x.terrain.elevation >= 0 && x.x >= 3 && net.IsSupplied(x)) landedSupplied = true;
                    r.Add("T" + blueTurns + ": ship " + Pos(ship.tile) + " carrying " + Transporting.CarriedWeight(ship) + "/" + ship.transportSlots + "; supplies " + (sup.trasnporter == ship ? "aboard" : Pos(sup.tile)) + ", tank " + (tank.trasnporter == ship ? "aboard" : Pos(tank.tile)) + ", grunt " + (g.trasnporter == ship ? "aboard" : Pos(g.tile)));
                    if (landedSupplied) break;
                }
                detail = "grunt aboard at the start " + gruntIn + "; " + string.Join(" | ", r.ToArray()) + "; all three aboard in turn 1 " + allAboard + ", sailed full with its supply " + sailedFull + ", troops landed in supply " + landedSupplied;
                return allAboard && sailedFull && landedSupplied;
            }
            case "turnOrder": {
                Unit art = Rig.At(-2, 0).occupier, full = Rig.At(-4, 2).occupier, r1 = Rig.At(-5, 2).occupier, r2 = Rig.At(-5, 3).occupier;
                Unit empty = Rig.At(-4, -2).occupier, beside = Rig.At(-3, -2).occupier, walker = Rig.At(-4, 0).occupier;
                bool loaded = Transporting.Load(full, r1) && Transporting.Load(full, r2);
                r1.active = true; r2.active = true;   // aboard, still to act this turn
                List<Unit> acted = new List<Unit>();
                AIPlayer ap = a.GetComponent<AIPlayer>();
                ap.onDecision += (u, act, did) => { if (did && u != null && !acted.Contains(u)) acted.Add(u); };
                ScenarioFile.PlayTurn();
                Func<Unit, int> at = u => acted.IndexOf(u);
                string names = string.Join(", ", acted.ConvertAll(u => u.name.Replace("(Clone)", "") + Pos(u.trasnporter != null ? u.trasnporter.tile : u.tile)).ToArray());
                bool artFirst = at(art) == 0;
                bool fullBeforeFoot = at(full) >= 0 && at(walker) >= 0 && at(full) < at(walker) && at(full) < at(beside);
                bool emptyAfterFoot = at(empty) > at(walker) && at(empty) > at(beside);
                detail = "loaded " + loaded + "; acted in order: " + names + "; artillery first " + artFirst + ", the full APC before those on foot " + fullBeforeFoot + ", the empty APC after them " + emptyAfterFoot;
                return loaded && artFirst && fullBeforeFoot && emptyAfterFoot;
            }
            case "shipRefit": {
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) AIPlayer.Trace = Console.WriteLine;
                Unit dry = Rig.At(3, -2).occupier, hungry = Rig.At(4, 3).occupier;
                HexTile yard = Rig.At(-2, 0);
                dry.ammo = 0; hungry.supplies = 1;
                List<string> r = new List<string>();
                bool rearmed = false, restocked = false;
                for (int turn = 0; turn < 8 && !gc.gameOver && !(rearmed && restocked); turn++) {
                    bool blue = gc.activePlayer == a;
                    ScenarioFile.PlayTurn();
                    if (!blue) continue;
                    if (dry.ammo == dry.ammoMax) rearmed = true;
                    if (hungry.supplies == hungry.suppliesMax) restocked = true;
                    r.Add("dry " + Pos(dry.tile) + " ammo " + dry.ammo + ", hungry " + Pos(hungry.tile) + " supplies " + hungry.supplies + " (strength " + hungry.Strength + ")");
                }
                detail = string.Join(" | ", r.ToArray()) + "; rearmed " + rearmed + ", restocked " + restocked + " without losing strength " + (hungry.Strength == 6);
                return rearmed && restocked && hungry.Strength == 6;
            }
            case "artilleryRide": {
                Unit art = Rig.At(-4, 0).occupier, g = Rig.At(-4, 1).occupier, apc = Rig.At(-3, 0).occupier, e = Rig.At(3, 0).occupier;
                Func<Unit, bool> board = c => { if (!Transporting.CanCarry(apc, c)) return false; Transporting.Load(apc, c); c.FinishBoarding(); c.active = true; return true; };
                // Not moved: put down beside it, it fires.
                bool loaded = board(art);
                Transporting.Unload(apc, art, Rig.At(-4, 0));
                bool firesUnmoved = art.CanAttack(e) && art.tile.AllInAttackRange(art).Count > 0;
                // Driven two tiles, then put down: it does not; the grunt that rode along does.
                bool both = board(art) && board(g);
                apc.MoveAlong(new List<HexTile> { Rig.At(-3, 0), Rig.At(-2, 0), Rig.At(-1, 0) }, false, false);
                Transporting.Unload(apc, art, Rig.At(0, 0)); Transporting.Unload(apc, g, Rig.At(0, -1));
                bool firesRidden = art.CanAttack(e) || art.tile.AllInAttackRange(art).Count > 0, gruntFires = g.CanAttack(e);
                a.EndTurn();
                bool firesNextTurn = art.CanAttack(e);
                detail = "loaded " + loaded + "/" + both + "; artillery put down unmoved fires " + firesUnmoved + "; after the ride fires " + firesRidden + ", the grunt that rode fires " + gruntFires + "; next turn the artillery fires " + firesNextTurn;
                return loaded && both && art.trasnporter == null && art.tile == Rig.At(0, 0) && g.trasnporter == null && firesUnmoved && !firesRidden && gruntFires && firesNextTurn;
            }
            case "boardReach": {
                Unit g = Rig.At(-4, 0).occupier, near = Rig.At(-1, 0).occupier, far = Rig.At(-1, 1).occupier, ship = Rig.At(0, -2).occupier;
                int dn = g.tile.Distance(near.tile), df = g.tile.Distance(far.tile), ds = g.tile.Distance(ship.tile);
                List<HexTile> pn = HexTile.BoardingPath(g, near), pf = HexTile.BoardingPath(g, far), ps = HexTile.BoardingPath(g, ship);
                bool atNear = pn != null && g.WalkAndBoard(pn, near, false);
                if (atNear) { Transporting.Unload(near, g, Rig.At(-4, 0)); g.active = true; }
                detail = "grunt move " + g.moveRange + "; APC " + dn + " off: path " + (pn == null ? "none" : (pn.Count - 1) + " steps") + ", boards " + atNear
                       + "; APC " + df + " off: path " + (pf == null ? "none" : (pf.Count - 1) + " steps") + "; hauler " + ds + " off on the water: path " + (ps == null ? "none" : (ps.Count - 1) + " steps");
                return dn == g.moveRange && df == g.moveRange + 1 && ds == g.moveRange + 1 && pn != null && atNear && pf == null && ps != null;
            }
            case "ignoresCover": {
                Unit tank = Rig.At(0, 0).occupier, g = Rig.At(1, 0).occupier;
                CombatOutcome before = CombatMath.Resolve(tank, g, tank.tile);
                int coverDmg = Unit.calculateDamage(tank, g, g.tile, 0), openDmg = Unit.calculateDamage(tank, g, null, 0);
                tank.ignoresCover = true;
                CombatOutcome after = CombatMath.Resolve(tank, g, tank.tile);
                int ignoringDmg = Unit.calculateDamage(tank, g, g.tile, 0);
                detail = "forest grunt fires first: normally " + before.defenderFiresFirst + ", against cover-ignoring " + after.defenderFiresFirst
                       + "; damage in the forest " + coverDmg + " (open ground " + openDmg + "), ignoring cover " + ignoringDmg + "; forecast to the grunt " + before.damageToDefender + " -> " + after.damageToDefender;
                return before.defenderFiresFirst && !after.defenderFiresFirst && coverDmg < openDmg && ignoringDmg == openDmg && after.damageToDefender >= before.damageToDefender;
            }
            case "nextUnit": {
                HexTileUIData ui = ScriptableObject.CreateInstance<HexTileUIData>();
                foreach (HexTile t in Rig.hg.tiles) { t.overlay = Rig.New<SpriteRenderer>(); t.arrow = Rig.New<SpriteRenderer>(); t.uiData = ui; t.state = UIState.FREE; }
                foreach (Unit u in Rig.uc.units) if (u.textArea == null) u.textArea = Rig.New<UnityEngine.UI.Text>();
                Unit g1 = Rig.At(0, 0).occupier, g2 = Rig.At(2, 0).occupier, tk = Rig.At(0, 2).occupier;
                // Blue played by hand: its AI off (the harness gives every side one).
                foreach (MonoBehaviour m in a.GetComponents<MonoBehaviour>()) if (m is IAIController) m.enabled = false;
                List<Unit> order = new List<Unit>();
                bool selectedLikeClick = true;
                for (int i = 0; i < 4; i++) {
                    Unit u = gc.SelectNextIdle();
                    order.Add(u);
                    int moves = 0; foreach (HexTile t in Rig.hg.tiles) if (t.state == UIState.MOVE) moves++;
                    if (u == null || gc.activeUnit != u || u.tile.state != UIState.SELECTED || moves == 0) selectedLikeClick = false;
                }
                bool cycles = order[0] != null && order[0] != order[1] && order[1] != order[2] && order[3] == order[0];
                g2.active = false;
                Unit after = null; bool skipped = true;
                for (int i = 0; i < 3; i++) { after = gc.SelectNextIdle(); if (after == g2) skipped = false; }
                gc.activeUnit = null; HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
                // An AI's turn (Blue handed back to its AI): nothing.
                foreach (MonoBehaviour m in a.GetComponents<MonoBehaviour>()) if (m is IAIController) m.enabled = true;
                bool redNone = gc.SelectNextIdle() == null && gc.activeUnit == null;
                detail = "order " + string.Join(", ", order.ConvertAll(u => u != null ? u.name + Pos(u.tile) : "none")) + "; each selected as a click does " + selectedLikeClick
                       + "; wraps round " + cycles + "; one that acted skipped " + skipped + "; on an AI's turn nothing " + redNone;
                return selectedLikeClick && cycles && skipped && redNone;
            }
            case "harborOpen": {
                HexTile yard = Rig.At(-2, 0), bridge = Rig.At(-3, 4);
                Unit g = Rig.At(-3, 1).occupier, g2 = Rig.At(-4, 4).occupier, boat = Rig.At(0, 0).occupier;
                bool landStep = LocomotionRules.BridgeStep(g.tile, yard, g.locomotion) && AIPath.ThisTurn(g).ReachableThisTurn(yard);
                bool shipStep = AIPath.ThisTurn(boat).ReachableThisTurn(yard);
                bool supplied = SupplyNetwork.Solve(a, false).IsSupplied(yard);
                bool bridgeHeld = !LocomotionRules.BridgeStep(g2.tile, bridge, g2.locomotion);
                detail = "grunt onto the harbor from grassland " + landStep + "; ship onto it from the water " + shipStep + "; harbor in supply " + supplied + "; plain bridge still road-only " + bridgeHeld;
                return landStep && shipStep && supplied && bridgeHeld;
            }
            case "attritionTransport": {
                Unit apc = Rig.At(-3, 0).occupier, g = Rig.At(-2, 0).occupier;
                bool loaded = Transporting.Load(apc, g);
                g.FinishBoarding();   // the boarding walk is animation: played out here at once
                bool off = Rig.At(-2, 0).occupier == null && g.tile == apc.tile && g.trasnporter == apc && g.transform.parent == (apc.cargoSlots.Count > 0 ? apc.cargoSlots[0] : apc.transform);
                apc.TeleportTo(Rig.At(-3, 3), 3);
                bool rides = g.tile == apc.tile;
                // Boarded this turn: it has used its turn, and getting off does not give it back.
                HexTile onto = Transporting.UnloadOptions(apc, g)[0];
                bool unloaded = Transporting.Unload(apc, g, onto);
                bool back = onto.occupier == g && g.trasnporter == null && g.transform.parent == null && !g.active && apc.transported.Count == 0;
                // Aboard at the start of a turn (fresh): unloaded, it is still fresh and can act.
                bool reloaded = Transporting.Load(apc, g); g.FinishBoarding();
                l.ai.EndTurn();   // the turn passes: every unit, cargo included, is ready again
                HexTile onto2 = Transporting.UnloadOptions(apc, g)[0];
                bool fresh = reloaded && g.active && Transporting.Unload(apc, g, onto2) && g.active;
                detail = "loaded " + loaded + ", off the board and in slot 0 " + off + ", moves with the APC " + rides + "; unloaded to " + Pos(onto) + " " + unloaded + ", back on the board, unparented, still spent " + back
                         + "; aboard at the start of a turn and unloaded, still able to act " + fresh;
                return loaded && off && rides && unloaded && back && fresh;
            }
            case "attritionCargoSupply": {
                Unit apc = Rig.At(-4, 0).occupier, g = Rig.At(-4, 1).occupier, ship = Rig.At(-1, 1).occupier, m = Rig.At(-2, 1).occupier;
                bool l1 = Transporting.Load(apc, g);
                // The marine boards the ship from the shore beside it.
                bool l2 = Transporting.Load(ship, m);
                endTurn(); endTurn();
                string first = "loaded " + l1 + "/" + l2 + "; APC cargo strength " + g.Strength + "; ship cargo supplies " + m.supplies + ", strength " + m.Strength;
                bool ok1 = l1 && l2 && g.Strength == 5 && m.supplies == m.suppliesMax && m.Strength == 5;
                ship.supplies = 0;
                endTurn(); endTurn();
                detail = "after a turn " + first + "; with the ship's supplies gone, ship cargo supplies " + m.supplies + " (of " + m.suppliesMax + ")";
                return ok1 && m.supplies == m.suppliesMax - 1;
            }
            case "attritionAmphibious": {
                Unit m = Rig.At(-2, 1).occupier, g = Rig.At(-2, 2).occupier;
                HexTile coast = Rig.At(-1, 1), open = Rig.At(0, 1);
                detail = "Marine: coastal water " + m.CanEnter(coast) + ", open water " + m.CanEnter(open) + "; Grunt: coastal water " + g.CanEnter(coast);
                return m.CanEnter(coast) && !m.CanEnter(open) && !g.CanEnter(coast);
            }
        }
        detail = "unknown check";
        return false;
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
                        Put("HQ", p, hq.x, hq.y);
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

    /// <summary>A unit, or a building when <paramref name="prefab"/> names one (HQ, City, FOB ...): null then.</summary>
    /// <summary>Puts <paramref name="c"/> aboard <paramref name="t"/> where it stands (setting up a scenario: no move, no boarding rules).</summary>
    static void Aboard(Unit t, Unit c) {
        if (c.tile != null && c.tile.occupier == c) c.tile.occupier = null;
        c.tile = t.tile; c.trasnporter = t; t.transported.Add(c); c.transform.SetParent(t.transform);
    }

    static Unit Put(string prefab, Player p, int x, int y) {
        if (!PrefabLoader.Has(prefab) && PrefabLoader.HasBuilding(prefab)) { Rig.PlaceBuilding(prefab, p, x, y); return null; }
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
        // Files are written over, never deleted: a scenario that is no longer generated stays until you remove it.
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
                        Put("HQ", p, hq.x, hq.y);
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
        n += ExportBuildingRules();
        n += ExportZigzag();

        n += ExportMatches(CampaignMaps, "campaign", 60,
            new[] { "Grunt", "Grunt", "Tank", "Grunt", "Supplies", "Artillery", "Grunt", "Grunt", "Tank", "Grunt", "Supplies",
                    "Artillery", "Grunt", "Tank", "Supplies", "Grunt", "Supplies", "Supplies", "Supplies",
                    "APC", "APC", "Truck Variant", "Truck Variant", "Recon", "Recon" },
            new[] { "Tank", "Grunt", "Tank", "Grunt" }, "twenty-five units a side: six supply units, two APCs, two trucks, two recons");

        // ---- behaviour scenarios: one question each.
        {   // The supply unit must not end its turn where the enemy can hit it.
            Blank(7);
            for (int y = -7; y <= 7; y++) { HexTile t = Rig.At(3, y); if (t != null && Math.Abs(y) > 1) t.terrain = T("Forest"); }
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("Supplies", ai, -4, 0);
            Put("Grunt", ai, 1, 0); Put("Grunt", ai, 1, 1); Put("Tank", ai, 0, 0); Put("Artillery", ai, -2, 0);
            Put("HQ", other, 6, 0); Put("Grunt", other, 4, -1); Put("Grunt", other, 4, 1); Put("Tank", other, 5, 0);
            ScenarioFile.Save(Path.Combine(Dir, "hub-stays-out-of-reach.dat"),
                Test("hub-stays-out-of-reach", "behaviour", "Legacy", 4, "hubSafe",
                     "Our army stands beyond HQ supply with the enemy close. The supply unit should bring supply forward without ending our turn where an enemy can attack it whenever it had a tile it could reach that turn where none could (a turn with no such tile is not judged)."),
                ai, other, ai);
            n++;
        }
        {   // A lone tank must not take a shot that leaves it where the enemy army kills it.
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("Tank", ai, -1, 0); Put("Grunt", ai, -5, 1);
            Put("HQ", other, 6, 0); Put("Grunt", other, 2, 0); Put("Tank", other, 4, 0); Put("Grunt", other, 4, -1); Put("Grunt", other, 4, 1);
            ScenarioFile.Save(Path.Combine(Dir, "no-suicide-attack.dat"),
                Test("no-suicide-attack", "behaviour", "Legacy", 1, "tankSurvivesReply",
                     "Our lone tank can hit an enemy grunt, but every tile it could hit from is within reach of the enemy tank and two grunts. It should not end its turn where the reply destroys it."),
                ai, other, ai);
            n++;
        }
        {   // Hub before rifle: killing their supply unit outweighs a grunt that can only scratch us.
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("Tank", ai, 0, 0);
            Put("HQ", other, 6, 0); Put("Supplies", other, 1, 0); Put("Grunt", other, 0, 1);
            for (int x = 2; x <= 4; x++) Put("Grunt", other, x, 0);
            ScenarioFile.Save(Path.Combine(Dir, "hit-the-supply-unit.dat"),
                Test("hit-the-supply-unit", "behaviour", "Legacy", 1, "enemyHubHit",
                     "Our tank is next to the enemy supply unit (25,000) that sustains four grunts, and next to a grunt that can only do 1 damage to it. It should hit the supply unit, with the gold to replace a loss (the check gives Blue 100,000 and purchases)."),
                ai, other, ai);
            n++;
        }
        // ---- air and sea (attrition, Red passive: these test the AI's own logistics).
        Action<string, string, Action<Player, Player>, int, string> air = (name, check, setup, rounds, question) => {
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("HQ", other, 6, 0); Put("Grunt", other, 5, 1);
            try { setup(ai, other); } catch (FileNotFoundException e) { Console.WriteLine("  (not made: " + name + " needs " + Path.GetFileName(e.FileName) + ", no longer in the game)"); return; }
            Dictionary<string, string> test = Test(name, "behaviour", "Human", rounds, check, question);
            test["rules"] = "attrition";
            test["purchases"] = "on";   // the build-on-reserved check buys at the airport
            ScenarioFile.Save(Path.Combine(Dir, name + ".dat"), test, ai, other, ai);
            n++;
        };
        Action<int, int> sea = (x0, x1) => { for (int x = x0; x <= x1; x++) for (int y = -7; y <= 7; y++) { HexTile t = Rig.At(x, y); if (t != null) t.terrain = T("Water"); } };
        air("fighter-comes-home", "fighterHome", (a, o) => { Rig.PlaceAirfield(a, -4, 2); Unit f = Put("Fighter", a, 2, -2); f.fuel = 11; Put("Grunt", a, -5, 0); }, 4,
            "A Fighter 6 tiles from its airport with 11 fuel (7 a turn's flying, 3 burnt each turn away): it has to come home now, and should never be ditched.");
        air("keep-the-runway-clear", "runwayClear", (a, o) => { Rig.PlaceAirfield(a, -4, 2); Unit f = Put("Fighter", a, 0, 0); f.fuel = 8; Put("Grunt", a, -4, 3); Put("Grunt", a, -5, 3); Put("Grunt", a, -3, 3); }, 1,
            "Our grunts are parked on all three runway tiles and a Fighter low on fuel needs one: the grunts should leave the runway.");
        air("reserve-the-runway", "runwayReserve", (a, o) => { Rig.PlaceAirfield(a, -4, 2); Unit f1 = Put("Fighter", a, -1, 2); f1.fuel = 10; Unit f2 = Put("Fighter", a, -1, 3); f2.fuel = 10; Put("Grunt", a, -5, 0); }, 1,
            "An airfield cut down to one landing tile and two Fighters that could each stay up three more turns: the first reserves the tile for its third turn, so the second takes it a turn earlier (a reservation holds one turn). Nobody needs it this turn, so it stays empty.");
        air("lift-off-for-landing", "liftOff", (a, o) => { Rig.PlaceAirfield(a, -4, 2); Put("Fighter", a, -4, 2); Unit f2 = Put("Fighter", a, -1, 2); f2.fuel = 4; Put("Grunt", a, -5, 0); }, 1,
            "One landing tile, a full Fighter parked on it and another 3 tiles out with 4 fuel that must land this turn: the parked one lifts off, the other lands (acting last), nobody is ditched.");
        air("no-build-on-reserved", "noBuildReserved", (a, o) => { Rig.PlaceAirfield(a, -4, 2); Unit f2 = Put("Fighter", a, -1, 2); f2.fuel = 4; Put("Grunt", a, -5, 0); }, 1,
            "One landing tile reserved for a Fighter landing this turn: the AI may not build a unit on it.");
        air("tanker-meets-fighter", "tankerMeets", (a, o) => { Rig.PlaceAirfield(a, -4, 2); Unit f = Put("Fighter", a, 1, -1); f.fuel = 9; Put("AirTanker", a, -3, 0); Put("Grunt", a, -5, 0); }, 1,
            "An air tanker (3 supplies) and a Fighter that is short of fuel: the tanker should end its turn beside the fighter.");
        air("boats-supply-the-island", "boatsIsland", (a, o) => { sea(-2, 1); Rig.PlaceBuilding("NavalYard", a, -3, 0); foreach (int y in new[] { 0, -1 }) { Unit h = Put("Hauler", a, -2, y); Unit su = Put("Supplies", a, -6, y + 3); Aboard(h, su); } Put("Grunt", a, 2, 0); Put("Grunt", a, -4, 1); }, 3,
            "A Blue grunt stranded on the shore of an island across a four-tile strait (a ship's supply reaches only the land beside it); two haulers with a supply unit aboard each at the naval yard's docks. Within three turns the haulers should link up and supply the island.");

        // A winding road two tiles wide over water: east along y = -3, down, and back west along y = 3.
        // The HQs are 6 apart as the crow flies and about 26 apart by road.
        Func<List<HexTile>> windingRoad = () => {
            Blank(7);
            foreach (HexTile t in Rig.hg.tiles) t.terrain = T("Water");
            int[,] way = { { -4, -3 }, { 6, -3 }, { 4, 3 }, { -6, 3 } };
            List<HexTile> road = new List<HexTile>();
            HexTile at = Rig.At(way[0, 0], way[0, 1]);
            road.Add(at);
            for (int i = 1; i < way.GetLength(0); i++) {
                HexTile goal = Rig.At(way[i, 0], way[i, 1]);
                int guard = 0;
                while (at != goal && guard++ < 100) {
                    HexTile next = null;
                    foreach (HexTile nb in at.neighbors) if (nb != null && (next == null || nb.Distance(goal) < next.Distance(goal) || (nb.Distance(goal) == next.Distance(goal) && CombatMath.Precedes(nb, next)))) next = nb;
                    at = next;
                    road.Add(at);
                }
            }
            foreach (HexTile t in road) {
                t.terrain = T("Grassland");
                HexTile lane = Rig.At(t.x, t.y + 1);
                if (lane != null && Math.Abs(t.y) % 3 != 0) lane = Rig.At(t.x - 1, t.y);
                if (lane != null && lane.terrain.name == "Water") lane.terrain = T("Grassland");
            }
            return road;
        };
        {
            List<HexTile> road = windingRoad();
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, road[0].x, road[0].y);
            string[] blue = { "Grunt", "Tank", "Grunt", "Supplies", "Supplies", "Supplies" };
            for (int i = 0; i < blue.Length; i++) Put(blue[i], ai, road[i + 1].x, road[i + 1].y);
            HexTile end = road[road.Count - 1];
            Put("HQ", other, end.x, end.y); Put("Grunt", other, road[road.Count - 2].x, road[road.Count - 2].y);
            Dictionary<string, string> test = Test("supply-chain-up-the-road", "behaviour", "Human", 6, "chainAdvance",
                "A winding road over water (" + road.Count + " tiles; the enemy HQ 6 away as the crow flies, about " + road.Count + " by road). Blue has an HQ (supply 8), two grunts, a tank and three supply units, Red sits at home. Over six rounds Blue should go up the road, not toward the water: the supply units moving up as a chain that is never broken, never ahead of the army, and the army at least 12 steps from home.");
            test["rules"] = "attrition";
            ScenarioFile.Save(Path.Combine(Dir, "supply-chain-up-the-road.dat"), test, ai, other, ai);
            n++;
        }
        {
            List<HexTile> road = windingRoad();
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, road[0].x, road[0].y);
            Put("Supplies", ai, road[3].x, road[3].y); Put("Grunt", ai, road[GruntsAt].x, road[GruntsAt].y); Put("Grunt", ai, road[GruntsAt + 1].x, road[GruntsAt + 1].y);
            HexTile end = road[road.Count - 1];
            Put("HQ", other, end.x, end.y); Put("Grunt", other, road[road.Count - 2].x, road[road.Count - 2].y);
            Dictionary<string, string> test = Test("buy-supply-when-stretched", "behaviour", "Human", 1, "buySupply",
                "Blue's one supply unit stands at the far edge of the HQ's supply, its grunts up the road beyond what it can reach: supply cannot get to them with what Blue has. With 25,000 (a supply unit's price) Blue should buy a supply unit at the HQ.");
            test["rules"] = "attrition";
            test["purchases"] = "on";
            ScenarioFile.Save(Path.Combine(Dir, "buy-supply-when-stretched.dat"), test, ai, other, ai);
            n++;
        }
        {
            List<HexTile> road = windingRoad();
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, road[0].x, road[0].y);
            Put("Supplies", ai, road[3].x, road[3].y); Put("Grunt", ai, road[GruntsAt].x, road[GruntsAt].y); Put("Grunt", ai, road[GruntsAt + 1].x, road[GruntsAt + 1].y);
            HexTile end = road[road.Count - 1];
            Put("HQ", other, end.x, end.y); Put("Grunt", other, road[road.Count - 2].x, road[road.Count - 2].y);
            Dictionary<string, string> test = Test("ai-campaign-front-buys-supply", "behaviour", "Human", 2, "campaignBuySupply",
                "The buy-supply-when-stretched board with the campaign on (logistics): the grunts are the front, beyond what Blue's one supply unit can reach — its chain is short and there is no spare supply unit. With a supply unit's price and a little more, Blue buys a supply unit (for the front), not a fighting unit; with 500 less than the price, it buys nothing and saves for one.");
            test["rules"] = "attrition";
            test["purchases"] = "on";
            ScenarioFile.Save(Path.Combine(Dir, "ai-campaign-front-buys-supply.dat"), test, ai, other, ai);
            n++;
        }
        {   // Artillery first, then the assault: the suppressed pips do not fire back.
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("Artillery", ai, -4, 0); Put("Tank", ai, -3, 1); Put("Tank", ai, -3, -1);
            Put("HQ", other, 6, 0); Put("Tank", other, 0, 0);
            Dictionary<string, string> test = Test("artillery-then-assault", "behaviour", "Human", 1, "suppressThenAssault",
                "A lone enemy tank in the open, our artillery in range and two tanks that can reach it. Artillery suppresses (half the pips left do not fire back until the enemy's turn), so it should fire first and the tanks attack after it, taking the smaller answer.");
            test["rules"] = "attrition";
            ScenarioFile.Save(Path.Combine(Dir, "artillery-then-assault.dat"), test, ai, other, ai);
            n++;
        }
        {   // Infantry next to a neutral city with nothing in the way should step onto it and capture.
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("Grunt", ai, -3, 0); Put("Tank", ai, -5, 1);
            Rig.PlaceBuilding("City", null, -1, 0);
            Put("HQ", other, 6, 0); Put("Grunt", other, 5, 0); Put("Tank", other, 5, 1);
            ScenarioFile.Save(Path.Combine(Dir, "take-the-city.dat"),
                Test("take-the-city", "behaviour", "Legacy", 1, "capturesCity",
                     "A neutral City two tiles from our grunt, the enemy far away. The grunt should end its turn on the City (capturing it)."),
                ai, other, ai);
            n++;
        }
        {   // Our HQ under direct threat: it must survive.
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("Grunt", ai, -5, 1); Put("Grunt", ai, -4, -1); Put("Tank", ai, -3, 1); Put("Artillery", ai, -6, 2);
            Put("HQ", other, 6, 0); Put("Tank", other, -1, 0); Put("Grunt", other, -2, 2); Put("Grunt", other, -1, -1);
            ScenarioFile.Save(Path.Combine(Dir, "defend-the-hq.dat"),
                Test("defend-the-hq", "behaviour", "Legacy", 4, "hqSurvives",
                     "An enemy tank and two grunts are two moves from our HQ. Our HQ must still stand after four rounds."),
                ai, other, ai);
            n++;
        }
        {   // A damaged grunt with a fresh one behind it should pull back to heal.
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0);
            Unit hurt = Put("Grunt", ai, -1, 0); hurt.Strength = 3;
            Put("Grunt", ai, -3, 0); Put("Grunt", ai, -3, 1);
            Put("HQ", other, 6, 0); Put("Grunt", other, 1, 0); Put("Grunt", other, 1, -1);
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
            Put("HQ", ai, -6, 0); Put("Grunt", ai, -3, 0); Put("Grunt", ai, -3, 1); Put("Tank", ai, -4, 0);
            Put("HQ", other, 6, 0); Put("Grunt", other, 6, -1); Put("Grunt", other, 5, 1); Put("Tank", other, 5, 0);
            ScenarioFile.Save(Path.Combine(Dir, "hold-the-gap.dat"),
                Test("hold-the-gap", "behaviour", "Legacy", 2, "gapHeld",
                     "A water line splits the map with a single land gap at (0,0). The enemy is outside striking range. One of our units should stand on the gap within two rounds."),
                ai, other, ai);
            n++;
        }
        {   // Finish the game: their HQ stands alone.
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("Grunt", ai, 0, 0); Put("Grunt", ai, 0, 1); Put("Tank", ai, -1, 0); Put("Artillery", ai, -2, 1);
            Put("HQ", other, 5, 0);
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
                // The rule: the supply unit never ends our turn where an enemy can hit it when it had a tile it
                // could reach that turn where none could (read at the start of our turn). With none, it is not judged.
                Unit hub = First(l.ai, "Supplies");
                int unjudged = 0;
                for (int r = 0; r < rounds && !gc.gameOver; r++) {
                    if (hub == null || hub.tile == null) { detail = "supply unit destroyed"; return false; }
                    AIMap before = new AIMap(l.ai, AIProfile.Default);
                    MoveField reach = before.ReachThisTurn(hub);
                    List<string> safe = new List<string>();
                    foreach (HexTile t in reach.Tiles) {
                        if (t != hub.tile && (!reach.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(hub, t) || t.occupier != null)) continue;
                        float tl; if (!AIWorld.Evaluate(hub, t, new AttackAppraisal(), before, new AIWorld.Cache(), true).lost.TryGetValue(hub, out tl)) tl = 0f;
                        if (tl <= 0f) safe.Add(D(t));
                    }
                    ScenarioFile.PlayTurn();                      // ours
                    if (hub == null || hub.tile == null) { detail = "supply unit destroyed"; return false; }
                    AIWorld.Outcome w = Reply(hub, new AIMap(l.ai, AIProfile.Default));
                    float lost; w.lost.TryGetValue(hub, out lost);
                    if (lost > 0f) {
                        if (safe.Count > 0) { detail = "round " + r + ": supply unit at " + D(hub.tile) + " can be hit for " + lost.ToString("0.#") + " though it could have ended on " + safe.Count + " safe tiles (" + string.Join(" ", safe.GetRange(0, Math.Min(5, safe.Count)).ToArray()) + ") " + w.Summary(); return false; }
                        unjudged++;
                    }
                    if (!gc.gameOver) ScenarioFile.PlayTurn();    // theirs
                }
                detail = "never ended our turn in reach with a safe tile to go to" + (unjudged > 0 ? " (" + unjudged + " turns with none: not judged)" : "") + "; at " + (hub.tile != null ? D(hub.tile) : "-") + " s" + hub.Strength + (gc.gameOver ? ", game over" : "");
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
                // With the gold and an HQ that builds to replace a loss (AIStrategic.StrategicNeed): the front is on
                // the offensive and the tank engages (with nothing to replace it, regrouping keeps it out of reach:
                // ai-regroup-hold-off-without-reserves).
                gc.ApplyPurchaseRules("on", null); l.ai.money = 100000;
                Unit hub = First(l.other, "Supplies");
                ScenarioFile.PlayTurn();
                detail = "enemy supply unit " + (hub == null || hub.tile == null ? "destroyed" : "at s" + hub.Strength);
                return hub == null || hub.tile == null || hub.Strength < 6;
            }
            case "fighterHome": {
                Unit f = First(l.ai, "Fighter");
                int ditched = 0; Action<Unit> hook = u => { if (u == f) ditched++; };
                Unit.Ditched += hook;
                List<string> trail = new List<string>();
                try {
                    for (int r = 0; r < rounds * 2 && !gc.gameOver; r++) {
                        bool blue = gc.activePlayer == l.ai;
                        ScenarioFile.PlayTurn();
                        if (blue && f.tile != null) trail.Add(Pos(f.tile) + " fuel " + f.fuel);
                    }
                } finally { Unit.Ditched -= hook; }
                bool home = f.tile != null && f.tile.building != null && f.tile.building.Services(f);
                detail = (ditched > 0 ? "DITCHED; " : "") + string.Join(" -> ", trail.ToArray()) + (home ? " (on base)" : "");
                return ditched == 0 && f.tile != null;
            }
            case "runwayClear": {
                Building ap = Rig.At(-4, 2).building;
                ScenarioFile.PlayTurn();
                int blocked = 0;
                List<Building> rws = Runways(ap);
                foreach (Building rw in rws) if (rw.tile.occupier != null && !AIAir.NeedsBase(rw.tile.occupier)) blocked++;
                Unit f = First(l.ai, "Fighter");
                detail = blocked + " of " + rws.Count + " runways still blocked by grunts; fighter at " + (f != null && f.tile != null ? Pos(f.tile) + " fuel " + f.fuel : "gone");
                return rws.Count > 0 && blocked < rws.Count;
            }
            case "runwayReserve": {
                Building ap = Rig.At(-4, 2).building;
                // One landing tile: the anchor; the rest of the airfield is taken out for the test.
                foreach (Building rw in Runways(ap)) RemoveBuilding(rw);
                Unit f1 = Rig.At(-1, 2).occupier, f2 = Rig.At(-1, 3).occupier;
                AIMap map = new AIMap(l.ai, l.ai.GetComponent<AIPlayer>().Profile);
                Dictionary<Unit, AIAir.Landing> plan = AIAir.PlanLandings(map);
                AIAir.Landing l1, l2;
                plan.TryGetValue(f1, out l1); plan.TryGetValue(f2, out l2);
                ScenarioFile.PlayTurn();
                Unit landed = ap.tile.occupier;
                detail = "plan: " + (l1 != null ? l1.ToString() : "first none") + "; " + (l2 != null ? l2.ToString() : "second none") + "; after our turn the tile has " + (landed != null ? landed.name + " " + Pos(landed.tile) : "nothing");
                return l1 != null && l2 != null && l1.tile == ap.tile && l2.tile == ap.tile && l1.turn == 3 && l2.turn == 2 && landed == null;
            }
            case "chainAdvance": {
                HexTile home = GameController.HQOf(l.ai).tile, theirs = GameController.HQOf(l.other).tile;
                AIMap m0 = new AIMap(l.ai, l.ai.GetComponent<AIPlayer>().Profile);
                Dictionary<HexTile, int> fromHome = m0.LandSteps(home), toThem = m0.LandSteps(theirs);
                Func<HexTile, int> hs = t => { int v; return t != null && fromHome.TryGetValue(t, out v) ? v : -1; };
                Func<HexTile, int> ts = t => { int v; return t != null && toThem.TryGetValue(t, out v) ? v : 999; };
                List<string> trail = new List<string>();
                bool broken = false, ahead = false;
                for (int r = 0; r < rounds * 2 && !gc.gameOver; r++) {
                    bool blue = gc.activePlayer == l.ai;
                    ScenarioFile.PlayTurn();
                    if (!blue) continue;
                    SupplyNetwork.Result net = SupplyNetwork.Solve(l.ai, false);
                    int front = 999, hubFront = 999, armyFar = 0, reach = 0;
                    foreach (Unit u in l.ai.units) {
                        if (u == null || u.tile == null || u.Strength <= 0) continue;
                        SupplyPoint sp = u.GetComponent<SupplyPoint>();
                        if (sp != null && sp.producer) continue;
                        if (sp != null) hubFront = Math.Min(hubFront, ts(u.tile));
                        else { front = Math.Min(front, ts(u.tile)); armyFar = Math.Max(armyFar, hs(u.tile)); }
                    }
                    foreach (KeyValuePair<HexTile, int> kv in net.depth) if (kv.Value > 0) reach = Math.Max(reach, hs(kv.Key));
                    if (net.strandedHubs.Count > 0) broken = true;
                    if (hubFront < front) ahead = true;
                    trail.Add("army " + armyFar + " supply " + reach + (net.strandedHubs.Count > 0 ? " BROKEN" : "") + (hubFront < front ? " HUB AHEAD" : ""));
                }
                int farthest = 0;
                foreach (Unit u in l.ai.units) if (u != null && u.tile != null && u.GetComponent<SupplyPoint>() == null) farthest = Math.Max(farthest, hs(u.tile));
                detail = "steps from home after each Blue turn: " + string.Join(" | ", trail.ToArray());
                return !broken && !ahead && farthest >= 12;
            }
            case "suppressThenAssault": {
                Unit target = First(l.other, "Tank");
                List<string> order = new List<string>();
                Action<Unit, Unit, HexTile, HexTile, CombatOutcome> hook = (a, d, af, df, o) => { if (d == target) order.Add(a.name.Replace("(Clone)", "") + " " + o.damageToDefender + "/" + o.damageToAttacker + (o.defenderSuppressed ? " suppressed +" + o.suppressionAdded : "") + " (target suppressed " + d.suppressed + ")"); };
                Unit.Attacked += hook;
                try { ScenarioFile.PlayTurn(); } finally { Unit.Attacked -= hook; }
                detail = order.Count == 0 ? "nobody attacked it" : string.Join(" -> ", order.ToArray());
                return order.Count >= 2 && order[0].StartsWith("Artillery");
            }
            case "buySupply": {
                l.ai.money = 25000;
                int before = 0;
                foreach (Unit u in l.ai.units) if (u != null && u.GetComponent<SupplyPoint>() != null && !u.GetComponent<SupplyPoint>().producer) before++;
                ScenarioFile.PlayTurn();
                int after = 0; Unit made = null;
                foreach (Unit u in l.ai.units) if (u != null && u.tile != null && u.GetComponent<SupplyPoint>() != null && !u.GetComponent<SupplyPoint>().producer) { after++; if (u.tile == GameController.HQOf(l.ai).tile) made = u; }
                detail = "supply units " + before + " -> " + after + ", money " + l.ai.money + (made != null ? ", new one on the HQ" : "");
                return after == before + 1 && made != null && made.cost > 0 && l.ai.money == 25000 - made.cost;
            }
            case "campaignBuySupply": {
                AIPlayer ap = l.ai.GetComponent<AIPlayer>();
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) AIPlayer.Trace = Console.WriteLine;
                Building hq = GameController.HQOf(l.ai);
                int price = 0; foreach (Unit b in hq.buildable) if (b != null && b.Hub != null && !b.Hub.producer) price = price == 0 ? b.cost : Math.Min(price, b.cost);
                Func<int, string> turn = money => {
                    l.ai.money = money;
                    int sup = 0, all = 0; foreach (Unit u in l.ai.units) if (u != null && u.tile != null) { all++; if (u.Hub != null && !u.Hub.producer) sup++; }
                    ScenarioFile.PlayTurn();
                    int sup2 = 0, all2 = 0; foreach (Unit u in l.ai.units) if (u != null && u.tile != null) { all2++; if (u.Hub != null && !u.Hub.producer) sup2++; }
                    ScenarioFile.PlayTurn();   // Red
                    return "supply units +" + (sup2 - sup) + ", others +" + (all2 - all - (sup2 - sup)) + ", money " + money + " -> " + l.ai.money;
                };
                string a1 = turn(price - 500);
                string a2 = turn(price + 1000);
                detail = "supply unit costs " + price + "; with " + (price - 500) + ": " + a1 + "; then with " + (price + 1000) + ": " + a2;
                return a1.StartsWith("supply units +0, others +0") && a2.StartsWith("supply units +1, others +0");
            }
            case "liftOff": {
                Building ap = Rig.At(-4, 2).building;
                foreach (Building rw in Runways(ap)) RemoveBuilding(rw);
                Unit parked = Rig.At(-4, 2).occupier, inbound = Rig.At(-1, 2).occupier;
                AIMap map = new AIMap(l.ai, l.ai.GetComponent<AIPlayer>().Profile);
                Dictionary<Unit, AIAir.Landing> plan = AIAir.PlanLandings(map);
                AIAir.Landing li; plan.TryGetValue(inbound, out li);
                int ditched = 0; Action<Unit> hook = u => ditched++;
                Unit.Ditched += hook;
                try { ScenarioFile.PlayTurn(); } finally { Unit.Ditched -= hook; }
                detail = "plan: " + (li != null ? li.ToString() : "none") + "; after our turn the tile has " + (ap.tile.occupier == inbound ? "the inbound fighter" : ap.tile.occupier == parked ? "the parked fighter still" : "nothing") +
                         "; parked fighter at " + (parked.tile != null ? Pos(parked.tile) : "gone") + "; ditched " + ditched;
                return li != null && li.turn == 1 && ap.tile.occupier == inbound && parked.tile != null && parked.tile != ap.tile && ditched == 0;
            }
            case "noBuildReserved": {
                Building ap = Rig.At(-4, 2).building;
                foreach (Building rw in Runways(ap)) RemoveBuilding(rw);
                Unit inbound = Rig.At(-1, 2).occupier;
                AIMap map = new AIMap(l.ai, l.ai.GetComponent<AIPlayer>().Profile);
                map.landings = AIAir.PlanLandings(map);
                Unit fighter = null;
                if (ap.buildable != null) foreach (Unit b in ap.buildable) if (b != null && b.name.Replace("(Clone)", "") == "Fighter") fighter = b;
                if (fighter == null) { detail = "the airport cannot build a Fighter"; return false; }
                l.ai.money = 100000;
                Unit made = BuildingController.bc.Produce(ap, fighter, t => AIAir.MayBuildOn(t, map));
                bool reserved = !AIAir.MayBuildOn(ap.tile, map);
                Unit free = BuildingController.bc.Produce(ap, fighter);
                detail = "reserved " + reserved + "; AI build on it: " + (made != null ? "built" : "refused") + "; unrestricted build: " + (free != null ? "built" : "refused");
                return reserved && made == null && free != null && inbound != null;
            }
            case "tankerMeets": {
                Unit t = First(l.ai, "AirTanker"), f = First(l.ai, "Fighter");
                ScenarioFile.PlayTurn();
                bool beside = t.tile != null && f.tile != null && t.tile.neighbors.Contains(f.tile);
                detail = "tanker at " + Pos(t.tile) + ", fighter at " + Pos(f.tile) + " fuel " + f.fuel + (beside ? ": beside" : ": not beside");
                return beside;
            }
            case "boatsIsland": {
                if (Environment.GetEnvironmentVariable("CHECKTRACE") != null) AIPlayer.Trace = Console.WriteLine;
                Unit g = Rig.At(2, 0).occupier;
                List<string> trail = new List<string>();
                bool ok = false;
                for (int r = 0; r < rounds * 2 && !gc.gameOver; r++) {
                    bool blue = gc.activePlayer == l.ai;
                    ScenarioFile.PlayTurn();
                    if (!blue) continue;
                    SupplyNetwork.Result res = SupplyNetwork.Solve(l.ai, false);
                    string boats = "";
                    foreach (Unit u in l.ai.units) if (u != null && u.Hub != null && !u.Hub.producer && u.tile != null) boats += " " + Pos(u.tile) + (u.trasnporter != null ? " aboard" : " ashore") + (res.connectedHubs.Contains(u.Hub) ? "+" : "-");
                    bool sup = g.tile != null && res.IsSupplied(g.tile);
                    trail.Add("supplies" + boats + (sup ? ", island supplied" : ""));
                    if (sup) ok = true;
                }
                detail = string.Join(" | ", trail.ToArray());
                return ok;
            }
            case "capturesCity": {
                Building city = Rig.At(-1, 0).building;
                ScenarioFile.PlayTurn();
                Unit on = Rig.At(-1, 0).occupier;
                detail = "City " + (on != null ? "has our " + on.name : "empty") + "; " + (city.owner != null ? city.owner.personal_name + "'s" : "neutral") + " " + city.capturePoints + "/" + city.CaptureThreshold;
                return on != null && on.owner == l.ai && city.capturingPlayer == l.ai && city.capturePoints > 0;
            }
            case "hqSurvives": {
                Building hq = GameController.HQOf(l.ai);
                for (int r = 0; r < rounds * 2 && !gc.gameOver; r++) ScenarioFile.PlayTurn();
                bool alive = hq != null && hq.owner == l.ai;
                detail = "our HQ " + (alive ? hq.capturePoints + "/" + hq.CaptureThreshold : "lost") + ", winner " + (gc.winner != null ? gc.winner.personal_name : "none");
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
        string onlyBehaviour = Environment.GetEnvironmentVariable("NAME");
        foreach (string f in Files(only == null || only == "behaviour" ? "behaviour" : "-")) {
            if (onlyBehaviour != null && !Path.GetFileName(f).Contains(onlyBehaviour)) continue;
            ScenarioFile.Loaded l;
            try { l = ScenarioFile.Load(f, null, tweak); }
            catch (FileNotFoundException e) {
                failed++;
                Console.WriteLine(string.Format("  FAIL  {0,-24} cannot load: no {1} (removed from the game?)", Path.GetFileNameWithoutExtension(f), Path.GetFileName(e.FileName)));
                continue;
            }
            string detail;
            bool ok = Judge(l, out detail);
            if (ok) passed++; else failed++;
            Console.WriteLine(string.Format("  {0}  {1,-24} {2}", ok ? "PASS" : "FAIL", l.meta.Name, detail));
        }
        Console.WriteLine();
        Console.WriteLine("== Building rules ==");
        // NAME=<part of a file name>: only the rule checks whose file name contains it.
        string onlyName = Environment.GetEnvironmentVariable("NAME");
        foreach (string f in Files(only == null || only == "rules" ? "rules" : "-")) {
            if (onlyName != null && !Path.GetFileName(f).Contains(onlyName)) continue;
            ScenarioFile.Loaded l;
            try { l = ScenarioFile.Load(f, null, tweak); }
            catch (FileNotFoundException e) {
                failed++;
                Console.WriteLine(string.Format("  FAIL  {0,-24} cannot load: no {1} (removed from the game?)", Path.GetFileNameWithoutExtension(f), Path.GetFileName(e.FileName)));
                continue;
            }
            string detail;
            bool ok = RuleCheck(l, out detail);
            bool skip = detail.StartsWith("SKIP");
            if (skip) { } else if (ok) passed++; else failed++;
            Console.WriteLine(string.Format("  {0}  {1,-24} {2}", skip ? "SKIP" : ok ? "PASS" : "FAIL", l.meta.Name, skip ? detail.Substring(6) : detail));
        }
        Console.WriteLine();
        // The matchup, large and campaign files play the Legacy and Greedy baselines, which know
        // nothing of buildings, aircraft or ships: run them only on request (ONLY=match|large|campaign).
        Console.WriteLine("== Matchups (generated terrain maps; baseline opponents, ONLY=match to run) ==");
        int w = 0, lo = 0, d = 0;
        foreach (string f in Files(only == "match" ? "match" : "-")) {
            ScenarioFile.Loaded l = ScenarioFile.Load(f, null, tweak);
            string detail;
            Judge(l, out detail);
            if (Rig.gc.winner == l.ai) w++; else if (Rig.gc.winner == l.other) lo++; else d++;
            Console.WriteLine(string.Format("  {0,-46} {1}", l.meta.Name, detail));
        }
        Console.WriteLine(string.Format("  matchups: {0}W {1}L {2}D", w, lo, d));
        foreach (string kind in new[] { "large", "campaign" }) {
            if (only != kind) continue;
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

    /// <summary>
    /// The sight rule checks were written for these vision ranges; the prefabs' own may be rebalanced
    /// (Grunt 3, Fighter 6... since). Pinned on every unit on the board, so a check tests the rule,
    /// not the balance of the day.
    /// </summary>
    static void PinVision() {
        Dictionary<string, int> v = new Dictionary<string, int> { { "Grunt", 2 }, { "Marine", 2 }, { "Fighter", 4 }, { "Recon", 4 }, { "AirTanker", 3 }, { "Tank", 2 }, { "APC", 2 }, { "Artillery", 2 }, { "Gunboat", 3 }, { "Supplies", 1 } };
        foreach (Unit u in Rig.uc.units) {
            int r;
            if (u != null && v.TryGetValue(u.name.Replace("(Clone)", "").Trim(), out r)) u.visionRange = r;
        }
        FogOfWar.Changed();
    }

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
        if (t.building != null) {
            Building b = t.building;
            char c = b.IsHQ ? 'H' : b.type == BuildingType.City ? 'C' : b.type == BuildingType.FOB ? 'B' : b.type == BuildingType.Airport ? 'P' : b.type == BuildingType.NavalYard ? 'D' : 'X';
            if (b.owner == null) return '=';
            return b.owner.id == ScenarioFile.AI_ID ? c : char.ToLower(c);
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
        if (File.Exists(name)) return name;   // a path to a map anywhere (Assets/Scenarios/ChanceMeeting.dat)
        string path = Path.Combine(Dir, name.EndsWith(".dat") ? name : name + ".dat");
        if (!File.Exists(path)) throw new Exception("No scenario " + path);
        return path;
    }

    /// <summary>
    /// How long the AI takes: every step (StepOnce: one Update's worth of work in the game, one
    /// action), timed, per side and turn, with the slowest steps and what they did.
    /// </summary>
    public static void Perf(string path, int rounds, Action<AIProfile> tweak) {
        Action<AIProfile> both = pr => { ApplyProfileAsset(pr); if (tweak != null) tweak(pr); };
        ScenarioFile.Loaded l = ScenarioFile.Load(path, "Full", both, both);
        GameController gc = Rig.gc;
        // PERFADD=Fighter:2,Bomber:2 — that many of each for both sides, on and beside their airports (jets on a map with none).
        string add = Environment.GetEnvironmentVariable("PERFADD");
        if (!string.IsNullOrEmpty(add))
            foreach (Player side in new[] { l.ai, l.other }) {
                List<HexTile> spots = new List<HexTile>();
                foreach (Building b in Building.all) if (b != null && b.owner == side && b.tile != null && b.name.StartsWith("Airport")) { spots.Add(b.tile); foreach (HexTile t in b.tile.neighbors) if (t != null) spots.Add(t); }
                // No airport of its own: around its HQ.
                Building hq = GameController.HQOf(side);
                if (spots.Count == 0 && hq != null) foreach (HexTile t in AIPath.TilesInRangeShared(hq.tile, 1, 3)) if (t != null) spots.Add(t);
                int placed = 0;
                foreach (string part in add.Split(',')) {
                    string[] kv = part.Split(':');
                    int n = kv.Length > 1 ? int.Parse(kv[1]) : 1;
                    for (int i = 0; i < n; i++) {
                        HexTile at = spots.Find(t => t.occupier == null && t.terrain != null);
                        if (at == null) break;
                        Unit u = Rig.Spawn(PrefabLoader.Load(kv[0].Trim()), side, at.x, at.y);
                        PrefabLoader.AttachHubIfAny(kv[0].Trim(), u);
                        u.active = true; placed++;
                    }
                }
                Console.WriteLine("  " + side.personal_name + ": " + placed + " placed (" + (spots.Count > 0 ? "beside its airport or HQ" : "nowhere to put them") + ")");
            }
        if (!string.IsNullOrEmpty(add)) Console.WriteLine("added for each side: " + add);
        AIPerf.Enabled = Environment.GetEnvironmentVariable("SECTIONS") != null;
        AIPlayer.VerifyReuse = Environment.GetEnvironmentVariable("VERIFYREUSE") != null;
        AIPerf.CallerDepth = int.Parse(Environment.GetEnvironmentVariable("PERFCALLERS") ?? "0");
        Console.WriteLine(Path.GetFileName(path) + ": " + Rig.hg.tiles.Count + " tiles, Blue " + l.ai.units.Count + " units, Red " + l.other.units.Count);
        for (int turn = 0; turn < rounds * 2 && !gc.gameOver; turn++) {
            Player active = gc.activePlayer;
            AIPlayer ai = active.GetComponent<AIPlayer>();
            if (ai == null) { gc.EndTurn(); continue; }
            List<KeyValuePair<double, string>> steps = new List<KeyValuePair<double, string>>();
            string last = "";
            ai.onDecision = (u, act, did) => { last = u.name.Replace("(Clone)", "") + " " + act.kind + (did ? "" : " (not done)"); };
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            int guard = 0; bool more = true;
            while (more && guard++ < 4096) {
                last = "(end of turn)";
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                more = ai.StepOnce(true);
                steps.Add(new KeyValuePair<double, string>(sw.Elapsed.TotalMilliseconds, last));
            }
            double sum = total.Elapsed.TotalMilliseconds;
            steps.Sort((a, b) => b.Key.CompareTo(a.Key));
            Console.WriteLine(string.Format("  round {0} {1}: {2} steps, {3:0} ms in all, slowest {4:0} ms, median {5:0} ms; allocated {7:0} MB this turn; live managed memory {6:0.0} MB", gc.days, active.personal_name, steps.Count, sum,
                steps.Count > 0 ? steps[0].Key : 0, steps.Count > 0 ? steps[steps.Count / 2].Key : 0, GC.GetTotalMemory(true) / 1048576.0,
                (GC.GetAllocatedBytesForCurrentThread() - alloc0) / 1048576.0));
            for (int i = 0; i < Math.Min(5, steps.Count); i++) Console.WriteLine(string.Format("      {0,7:0} ms  {1}", steps[i].Key, steps[i].Value));
            Console.WriteLine(string.Format("      coordination-pass decisions: {0} reused, {1} made again" + (AIPlayer.VerifyReuse ? ", {2} checked against a fresh decision, {3} different" : ""), AIPlayer.reused, AIPlayer.notReused, AIPlayer.reuseChecked, AIPlayer.reuseDiffered));
            if (AIPlayer.notReusedWhy.Count > 0) Console.WriteLine("        made again because: " + string.Join("; ", AIPlayer.notReusedWhy.Select(kv => kv.Key + " " + kv.Value).ToArray()));
            AIPlayer.notReusedWhy.Clear();
            AIPlayer.reused = AIPlayer.notReused = AIPlayer.reuseChecked = AIPlayer.reuseDiffered = 0;
            if (AIPerf.Enabled) { Console.WriteLine(AIPerf.Report()); AIPerf.Reset(); }
        }
    }

    /// <summary>One scenario, narrated: the board after every turn and every decision's reasoning.</summary>
    public static void Narrate(string name, int rounds, Action<AIProfile> tweak) {
        ScenarioFile.Loaded l = ScenarioFile.Load(Find(name), Environment.GetEnvironmentVariable("OPPONENT"), tweak, Tests.Tweak(Environment.GetEnvironmentVariable("SET_RED")));
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
            // EXPLAIN=Red:APC (or Blue:Tank): every option that unit weighed, each turn it moves.
            string explain = Environment.GetEnvironmentVariable("EXPLAIN");
            AITactical.DebugUnit = null; AITactical.DebugLog = null;
            if (!string.IsNullOrEmpty(explain) && explain.Contains(":")) {
                string[] ex = explain.Split(':');
                Player who = ex[0] == "Blue" ? l.ai : l.other;
                if (who == active) foreach (Unit u in who.units) if (u != null && u.tile != null && u.name.StartsWith(ex[1])) { AITactical.DebugUnit = u; break; }
                if (AITactical.DebugUnit != null) AITactical.DebugLog = m => Console.WriteLine("      | " + m);
                AITactical.DebugBreakdown = Environment.GetEnvironmentVariable("BREAKDOWN") != null;
            }
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
                        if (full.CurrentCampaign != null) foreach (string line in full.CurrentCampaign.Describe()) Console.WriteLine("    " + line);
                        Console.WriteLine("    objective pulls this turn: " + full.CurrentAssignments.Count);
                        foreach (Front f in full.CurrentFronts)
                            Console.WriteLine("    front " + f + (f.objectiveTile != null ? " objective " + D(f.objectiveTile) + " value " + f.objectiveValue.ToString("0") : "")
                                              + (f.staging && f.stagingTile != null ? " staging at " + D(f.stagingTile) : "") + ", " + f.assigned.Count + " units");
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
        UseOldAI(l);
        // STATS=1: the game recorded (GameLog) and, at the end, the post-game report read back from its game.log, as
        // the post-game screen reads it.
        bool stats = Environment.GetEnvironmentVariable("STATS") != null;
        if (stats && root == null) root = Path.Combine(Path.GetTempPath(), "hextactica-stats");
        if (root != null) HexTactica.Harness.GameLog.Start(l.path, "Full", root);
        GameController gc = Rig.gc;
        Console.WriteLine("duel on " + name + ": Blue " + (Environment.GetEnvironmentVariable("SET_BLUE") ?? "defaults")
                          + " | Red " + (Environment.GetEnvironmentVariable("SET_RED") ?? "defaults"));
        int startBlue = l.ai.units.Count, startRed = l.other.units.Count;
        // Who held each building at the start, and the round each changed hands (printed at the end).
        Dictionary<Building, Player> heldAtStart = new Dictionary<Building, Player>();
        foreach (Building b in Building.all) if (b != null) heldAtStart[b] = b.owner;
        Dictionary<Building, Player> heldNow = new Dictionary<Building, Player>(heldAtStart);
        List<string> handsChanged = new List<string>();
        Action<int> noteHands = round => {
            foreach (Building b in Building.all) {
                if (b == null || b.tile == null) continue;
                Player was; heldNow.TryGetValue(b, out was);
                if (was == b.owner) continue;
                handsChanged.Add("r" + round + " " + b.name.Replace("(Clone)", "") + "(" + b.tile.x + "," + b.tile.y + ") " + (was != null ? (was == l.ai ? "Blue" : "Red") : "neutral") + "->" + (b.owner != null ? (b.owner == l.ai ? "Blue" : "Red") : "neutral"));
                heldNow[b] = b.owner;
            }
        };
        int ditchBlue = 0, ditchRed = 0; int sorties = 0, landings = 0;
        Action<Unit> dh = u => { if (u.owner == l.ai) ditchBlue++; else ditchRed++; };
        Unit.Ditched += dh;
        int ranBlue = 0, ranRed = 0, hiddenShotsBlue = 0, hiddenShotsRed = 0;
        Action<Unit, Unit, HexTile> rh = (m, h, t) => { if (m.owner == l.ai) ranBlue++; else ranRed++; };
        // A shot from where the side fired on did not see the firer (an ambush or a shot from the fog).
        Action<Unit, Unit, bool> sh = (att, def, surprise) => { if (gc.fogOfWar && (surprise || !FogOfWar.Sees(def.owner, att))) { if (att.owner == l.ai) hiddenShotsBlue++; else hiddenShotsRed++; } };
        Unit.RanInto += rh; Unit.Attacking += sh;
        if (Environment.GetEnvironmentVariable("VALUES") != null) { HexTactica.AI.AIProduction.ValueTrace = x => Console.WriteLine("   " + x); HexTactica.AI.AIProduction.ValueFilter = Environment.GetEnvironmentVariable("VALUES"); }
        if (Environment.GetEnvironmentVariable("DUELTRACE") != null) { AIPlayer.Trace = Console.WriteLine; HexTactica.AI.AIProduction.DebugValues = x => { if (x.Contains("sea lift")) Console.WriteLine("   value: " + x); }; HexTactica.OldAI.OldAIPlayer.Trace = Console.WriteLine; HexTactica.AI.AIAssault.Debug = Console.WriteLine; HexTactica.AI.AISupplyChain.DebugLog = Console.WriteLine; HexTactica.AI.AICampaign.Debug = x => { if (x.StartsWith("not taken on") || x.StartsWith("given up") || x.StartsWith("reserve drafts")) Console.WriteLine("   campaign: " + x); }; if (Environment.GetEnvironmentVariable("DUELTRACE") == "full") { AITactical.DecisionLog = m => Console.WriteLine("    " + m); AITactical.DebugBreakdown = true; } }
        double thinkBlue = 0, thinkRed = 0, worstBlue = 0, worstRed = 0;
        // How each side uses transports: supply units put down, fighting units boarding, put down, and turns spent aboard.
        int[] supplyDrops = new int[2], boards = new int[2], drops = new int[2], aboardTurns = new int[2], transportsBought = new int[2];
        foreach (Player pl in new[] { l.ai, l.other }) {
            AIPlayer ap = pl.GetComponent<AIPlayer>(); if (ap == null) continue;
            int side = pl == l.ai ? 0 : 1;
            ap.onDecision += (u, act, did) => {
                if (!did) return;
                if (act.kind == HexTactica.AI.AIActionKind.Unload && act.target != null) { if (act.target.Hub != null) supplyDrops[side]++; else drops[side]++; }
                if (act.kind == HexTactica.AI.AIActionKind.Load && act.unit != null && act.unit.Hub == null) boards[side]++;
            };
        }
        for (int turn = 0; turn < rounds * 2 && !gc.gameOver; turn++) {
            bool blueMoves = gc.activePlayer == l.ai;
            if (Environment.GetEnvironmentVariable("DUELTRACE") != null) Console.WriteLine("== turn " + turn + " " + (blueMoves ? "Blue" : "Red"));
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            ScenarioFile.PlayTurn();
            double secs = sw.Elapsed.TotalSeconds;
            if (blueMoves) { thinkBlue += secs; worstBlue = Math.Max(worstBlue, secs); } else { thinkRed += secs; worstRed = Math.Max(worstRed, secs); }
            foreach (Player pl in new[] { l.ai, l.other }) foreach (Unit u in pl.units) if (u != null && u.tile != null && u.trasnporter != null && u.Hub == null) aboardTurns[pl == l.ai ? 0 : 1]++;
            if (Environment.GetEnvironmentVariable("BOARD") != null) {
                List<string> b = new List<string>();
                foreach (Player p in new[] { l.ai, l.other }) foreach (Unit u in p.units) if (u != null && u.tile != null)
                    b.Add(p.personal_name + ":" + u.name.Replace("(Clone)", "") + "@" + u.tile.x + "," + u.tile.y + " s" + u.Strength + " f" + u.fuel + " a" + u.ammo + " p" + u.supplies);
                b.Sort(string.CompareOrdinal);
                Console.WriteLine("  board after turn " + turn + ": " + string.Join(" | ", b.ToArray()));
            }
            noteHands(gc.days);
            if (gc.activePlayer == l.ai || gc.gameOver)
                Console.WriteLine(string.Format("  round {0,2}: Blue {1,2} units, material {2,6} | Red {3,2} units, material {4,6} | out of supply (unit-turns so far) Blue {5} Red {6}, lost to attrition Blue {7} Red {8}",
                    gc.days, Alive(l.ai), ScenarioFile.Material(l.ai), Alive(l.other), ScenarioFile.Material(l.other),
                    l.ai.unitTurnsOutOfSupply, l.other.unitTurnsOutOfSupply, l.ai.unitsLostToAttrition, l.other.unitsLostToAttrition));
        }
        Unit.Ditched -= dh; Unit.RanInto -= rh; Unit.Attacking -= sh;
        if (gc.fogOfWar) Console.WriteLine("fog of war: ran into unseen units Blue " + ranBlue + " Red " + ranRed + "; shots fired unseen Blue " + hiddenShotsBlue + " Red " + hiddenShotsRed);
        int airBlue = 0, airRed = 0;
        foreach (Unit u in l.ai.units) if (u != null && u.tile != null && LocomotionRules.IsAirborne(u.locomotion)) airBlue++;
        foreach (Unit u in l.other.units) if (u != null && u.tile != null && LocomotionRules.IsAirborne(u.locomotion)) airRed++;
        Console.WriteLine("aircraft: ditched Blue " + ditchBlue + " Red " + ditchRed + "; still flying Blue " + airBlue + " Red " + airRed);
        Console.WriteLine(string.Format("think time: Blue {0:0.0} s (worst turn {1:0.0} s), Red {2:0.0} s (worst turn {3:0.0} s)", thinkBlue, worstBlue, thinkRed, worstRed));
        {
            int bB = 0, bR = 0, iB = 0, iR = 0;
            foreach (Building b in Building.all) {
                if (b == null) continue;
                if (b.owner == l.ai) { bB++; iB += b.income; } else if (b.owner == l.other) { bR++; iR += b.income; }
            }
            Console.WriteLine(string.Format("end: Blue {0} units, material {1}, {2} buildings (income {3}), gold {4} | Red {5} units, material {6}, {7} buildings (income {8}), gold {9}",
                l.ai.units.FindAll(u => u != null && u.Strength > 0).Count, ScenarioFile.Material(l.ai), bB, iB, l.ai.money,
                l.other.units.FindAll(u => u != null && u.Strength > 0).Count, ScenarioFile.Material(l.other), bR, iR, l.other.money));
        }
        Console.WriteLine("buildings that changed hands: " + (handsChanged.Count > 0 ? string.Join(", ", handsChanged.ToArray()) : "none"));
        Console.WriteLine(string.Format("transports: supply put down Blue {0} Red {1}; fighting units boarded Blue {2} Red {3}, put down Blue {4} Red {5}; unit-turns aboard Blue {6} Red {7}",
            supplyDrops[0], supplyDrops[1], boards[0], boards[1], drops[0], drops[1], aboardTurns[0], aboardTurns[1]));
        string folder = HexTactica.Harness.GameLog.Folder;
        if (root != null) HexTactica.Harness.GameLog.Stop();
        Console.WriteLine("winner: " + (gc.winner != null ? gc.winner.personal_name : "none (round limit)")
                          + string.Format("; units lost: Blue {0} of {1}, Red {2} of {3}", startBlue - Alive(l.ai), startBlue, startRed - Alive(l.other), startRed));
        if (root != null) Console.WriteLine("log: " + folder);
        if (stats) {
            GameRecord rec = GameRecord.Read(folder);
            if (rec == null) Console.WriteLine("no game.log in " + folder);
            else {
                // STATS=you: written as the post-game screen writes it for a human playing Red ("you").
                if (Environment.GetEnvironmentVariable("STATS") == "you") { GameRecord.Side me = rec.SideOf(l.other.id); if (me != null) me.ai = false; }
                Console.WriteLine("\n== post-game report (viewer: Red)\n" + GameInsights.Report(rec, l.other.id));
            }
        }
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
                p.assistWeight = 0f; p.forecastKnockback = false; p.forecastBuildings = false; p.selfPreservationWeight = 0f; p.ownStrikeWeight = 0f; p.enemyStrikeWeight = 0f;
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
                            if (bad <= 3) {
                                Console.WriteLine("     single: " + AIWorld.Evaluate(u, s, a, map, new AIWorld.Cache(), true, false).Summary());
                                Console.WriteLine("     plan:   " + AIPlan.Evaluate(new List<PlanStep> { step }, map, true, false).Summary());
                            }
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

    /// <summary>OLD=Blue or OLD=Red: that side is played by the old AI (HexTactica.OldAI, claude/campaign-2) with its own defaults.</summary>
    static void UseOldAI(ScenarioFile.Loaded l) {
        string oldSide = Environment.GetEnvironmentVariable("OLD");
        if (string.IsNullOrEmpty(oldSide)) return;
        Player op = oldSide == "Blue" ? l.ai : l.other;
        AIPlayer cur = op.GetComponent<AIPlayer>();
        if (cur != null) cur.enabled = false;
        HexTactica.OldAI.OldAIPlayer o = op.gameObject.AddComponent<HexTactica.OldAI.OldAIPlayer>();
        o.player = op;
        o.profile = ScriptableObject.CreateInstance<HexTactica.OldAI.AIProfile>();
        Console.WriteLine("old AI (claude/campaign-2) plays " + oldSide);
    }

    /// <summary>
    /// PROBE=analyze FILE=&lt;map&gt; [ROUNDS=r] [FOG=on|off]: both sides this AI; every round, per side,
    /// how its army and its supply stand. Units: alive, and lost so far to fire and to attrition. The
    /// army: fighting units engaged (an enemy within 6 tiles), and of those out of supply, starving (no
    /// supplies of their own left), short of ammunition (a third or less) or of fuel. Supply units:
    /// how far from their HQ and from the nearest enemy (median), how many idle in the rear (within 4 of
    /// the HQ while the fighting is farther), linked or cut off. Fire: shots, and value dealt and taken
    /// (strength times cost per point), and the trades that lost value.
    /// </summary>
    public static void Analyze(string name, int rounds) {
        ScenarioFile.Loaded l = ScenarioFile.Load(Find(name), "Full", Tests.Tweak(Environment.GetEnvironmentVariable("SET_BLUE")), Tests.Tweak(Environment.GetEnvironmentVariable("SET_RED")));
        GameController gc = Rig.gc;
        Console.WriteLine("analyze " + name + " (rules " + gc.rules + ", fog " + (gc.fogOfWar ? "on" : "off") + ")");
        // CUTS: every action of the AI's after which fewer of its supply units are connected.
        if (Environment.GetEnvironmentVariable("CUTS") != null) {
            AIPlayer bai = l.ai.GetComponent<AIPlayer>();
            Func<int> connected = () => { SupplyNetwork.Result r = SupplyNetwork.Solve(l.ai, false); int c = 0; foreach (Unit u in l.ai.units) if (u != null && u.Hub != null && !u.Hub.producer && r.connectedHubs.Contains(u.Hub)) c++; return c; };
            int lastConnected = -1;
            bai.onDecision += (u, act, did) => {
                int c = connected();
                if (lastConnected >= 0 && c < lastConnected && c == 0) {
                    SupplyNetwork.Result r0 = SupplyNetwork.Solve(l.ai, false);
                    Building hqb = GameController.HQOf(l.ai);
                    Console.WriteLine("      [debug] supplied tiles " + r0.depth.Count + ", HQ " + (hqb != null ? hqb.name + " functioning " + hqb.Functioning + " produces " + hqb.producesSupply + " owner " + (hqb.owner != null ? hqb.owner.personal_name : "-") : "none") + ", active " + gc.activePlayer.personal_name);
                }
                if (lastConnected >= 0 && c < lastConnected)
                    Console.WriteLine("    day " + gc.days + ": " + lastConnected + " -> " + c + " connected after " + (u != null ? u.name.Replace("(Clone)", "") : "?") + " " + act.kind + " to " + (act.destination != null ? "(" + act.destination.x + "," + act.destination.y + ")" : "-") + ": " + act.reason);
                lastConnected = c;
            };
            GameController.TurnStarted += pl => { if (pl == l.ai) lastConnected = connected(); };
        }
        Dictionary<Player, int[]> fire = new Dictionary<Player, int[]> { { l.ai, new int[4] }, { l.other, new int[4] } };   // shots, dealt, taken, losing
        Dictionary<Player, int> killed = new Dictionary<Player, int> { { l.ai, 0 }, { l.other, 0 } };
        Action<Unit, Unit, HexTile, HexTile, CombatOutcome> onAttack = (att, def, at, dt, o) => {
            if (att == null || def == null || att.owner == null || def.owner == null || !fire.ContainsKey(att.owner)) return;
            int vd = (int)(o.damageToDefender * AIScore.Cost(def) / 6f), vt = (int)(o.damageToAttacker * AIScore.Cost(att) / 6f);
            int[] f = fire[att.owner]; f[0]++; f[1] += vd; f[2] += vt; if (vt > vd) f[3]++;
            if (o.defenderDies && killed.ContainsKey(def.owner)) killed[def.owner]++;
            if (o.attackerDies && killed.ContainsKey(att.owner)) killed[att.owner]++;
        };
        Unit.Attacked += onAttack;
        Dictionary<Player, int> start = new Dictionary<Player, int> { { l.ai, Alive(l.ai) }, { l.other, Alive(l.other) } };
        // The forecast's eye: at the end of each side's turn, the strength its own forecast (the enemy's
        // reply, AIWorld, on the board as that side knows it) says each of its units will lose; after the
        // enemy's turn, what each really lost. Misses: a unit that lost 3 or more beyond the forecast.
        Dictionary<Player, float[]> eye = new Dictionary<Player, float[]> { { l.ai, new float[3] }, { l.other, new float[3] } };   // forecast, actual, big misses
        Dictionary<Unit, float> predicted = new Dictionary<Unit, float>(), before = new Dictionary<Unit, float>();
        List<string> misses = new List<string>();
        Player forecastFor = null;
        try {
            for (int turn = 0; turn < rounds * 2 && !gc.gameOver; turn++) {
                Player mover = gc.activePlayer;
                ScenarioFile.PlayTurn();
                // The enemy's turn is over: score the forecast made for the side it hit.
                if (forecastFor != null && mover != forecastFor) {
                    float[] ey = eye[forecastFor];
                    foreach (KeyValuePair<Unit, float> kv in before) {
                        Unit u = kv.Key;
                        float now = u != null && u.tile != null && u.Strength > 0 ? u.Strength : 0f;
                        float lost = kv.Value - now, pr;
                        predicted.TryGetValue(u, out pr);
                        ey[0] += pr; ey[1] += lost;
                        if (lost - pr >= 3f) { ey[2]++; if (misses.Count < 40) misses.Add("r" + gc.days + " " + forecastFor.personal_name + " " + u.name + " forecast -" + pr.ToString("0.#") + ", lost " + lost); }
                    }
                    forecastFor = null;
                }
                // This side's turn is over: its forecast for the enemy's reply.
                if (!gc.gameOver && gc.activePlayer != mover && eye.ContainsKey(mover) && Environment.GetEnvironmentVariable("CUTS") != null) {
                    SupplyNetwork.Result endNet = SupplyNetwork.Solve(mover, false);
                    int nh = 0, nc = 0, ue = 0, uc = 0;
                    foreach (Unit u in mover.units) {
                        if (u == null || u.tile == null || u.Strength <= 0) continue;
                        if (u.Hub != null && !u.Hub.producer) { nh++; if (!endNet.connectedHubs.Contains(u.Hub)) nc++; }
                        else if (u.Hub == null && u.Firepower > 0) { ue++; if (endNet.DepthAt(u.tile) <= 0) uc++; }
                    }
                    Console.WriteLine("  " + mover.personal_name + " ends its turn with " + nc + " of " + nh + " supply units cut, " + uc + " of " + ue + " fighting units out of supply");
                }
                if (!gc.gameOver && gc.activePlayer != mover && eye.ContainsKey(mover)) {
                    predicted.Clear(); before.Clear();
                    AIPlayer ai = mover.GetComponent<AIPlayer>();
                    AIProfile prof = ai != null ? ai.Profile : ScriptableObject.CreateInstance<AIProfile>();
                    using (FogOfWar.Concealing(prof.fogFair ? mover : null, true, prof.fogMemoryTurns)) {
                        AIMap m = new AIMap(mover, prof);
                        if (m.sigint != null && Environment.GetEnvironmentVariable("SIGINT") != null)
                            foreach (string line in m.sigint.Describe()) Console.WriteLine("  " + mover.personal_name + " fog: " + line);
                        Unit any = null;
                        foreach (Unit u in m.ownUnits) if (u != null && u.tile != null && u.Strength > 0) { any = u; break; }
                        if (any != null) {
                            // The whole board's reply (no theatre: the forecast for every unit at once).
                            bool th = prof.theaterForecast;
                            prof.theaterForecast = false;
                            try {
                                AIWorld.Outcome w = AIWorld.Evaluate(any, null, new AttackAppraisal(), m, new AIWorld.Cache(), false);
                                foreach (KeyValuePair<Unit, float> kv in w.lost) predicted[kv.Key] = kv.Value;
                            } finally { prof.theaterForecast = th; }
                        }
                    }
                    foreach (Unit u in mover.units) if (u != null && u.tile != null && u.Strength > 0) before[u] = u.Strength;
                    forecastFor = mover;
                }
                if (gc.activePlayer != l.ai && !gc.gameOver) continue;
                Console.WriteLine("round " + gc.days + ":");
                foreach (Player p in new[] { l.ai, l.other }) {
                    Player e = p == l.ai ? l.other : l.ai;
                    SupplyNetwork.Result net = SupplyNetwork.Solve(p, false);
                    HexTile hq = GameController.HQOf(p) != null ? GameController.HQOf(p).tile : null;
                    List<Unit> enemies = new List<Unit>();
                    foreach (Unit u in e.units) if (u != null && u.tile != null && u.Strength > 0) enemies.Add(u);
                    Func<HexTile, int> toEnemy = t => { int best = 99; foreach (Unit x in enemies) best = Math.Min(best, x.tile.Distance(t)); return best; };
                    int engaged = 0, cut = 0, starving = 0, ammo = 0, fuel = 0;
                    List<int> hubHq = new List<int>(), hubEnemy = new List<int>();
                    int idle = 0, hubs = 0, stranded = 0, nearestFight = 99;
                    foreach (Unit u in p.units) {
                        if (u == null || u.tile == null || u.Strength <= 0) continue;
                        if (u.Hub == null && u.Firepower > 0 && toEnemy(u.tile) <= 6) {
                            engaged++;
                            nearestFight = Math.Min(nearestFight, hq != null ? hq.Distance(u.tile) : 99);
                            bool base_ = LocomotionRules.NeedsBase(u.locomotion);
                            if (!base_ && net.DepthAt(u.tile) <= 0) cut++;
                            if (u.suppliesMax > 0 && u.supplies <= 0) starving++;
                            if (u.ammoMax > 0 && u.ammo * 3 <= u.ammoMax) ammo++;
                            if (u.fuelMax > 0 && u.fuel * 3 <= u.fuelMax) fuel++;
                        }
                    }
                    foreach (Unit u in p.units) {
                        if (u == null || u.tile == null || u.Strength <= 0 || u.Hub == null || u.Hub.producer) continue;
                        hubs++;
                        int dh = hq != null ? hq.Distance(u.tile) : 99, de = toEnemy(u.tile);
                        hubHq.Add(dh); hubEnemy.Add(de);
                        if (dh <= 4 && nearestFight > 6 && nearestFight < 99) idle++;
                        if (!net.connectedHubs.Contains(u.Hub)) stranded++;
                    }
                    // DETAIL=round: where each supply unit and each engaged unit out of supply stands, that round.
                    string detailRound = Environment.GetEnvironmentVariable("DETAIL");
                    if (detailRound != null && Array.IndexOf(detailRound.Split(','), gc.days.ToString()) >= 0) {
                        List<string> hs = new List<string>(), cs = new List<string>();
                        foreach (Unit u in p.units) {
                            if (u == null || u.tile == null || u.Strength <= 0) continue;
                            string at = "(" + u.tile.x + "," + u.tile.y + ")";
                            if (u.Hub != null && !u.Hub.producer) hs.Add(u.name.Replace(" Variant", "") + at + (u.trasnporter != null ? " aboard " + u.trasnporter.name : "") + (net.connectedHubs.Contains(u.Hub) ? "" : " CUT") + " hq" + (hq != null ? hq.Distance(u.tile) : 99) + " e" + toEnemy(u.tile));
                            else if (u.Hub == null && u.Firepower > 0 && toEnemy(u.tile) <= 6 && !LocomotionRules.NeedsBase(u.locomotion) && net.DepthAt(u.tile) <= 0) { int gap = HexTactica.AI.AIMap.GapToSupply(u.tile, net.DepthAt, 12); cs.Add(u.name.Replace(" Variant", "") + at + " s" + u.Strength + " p" + u.supplies + " a" + u.ammo + " hq" + (hq != null ? hq.Distance(u.tile) : 99) + " gap" + gap); }
                        }
                        Console.WriteLine("    " + p.personal_name + " supply units: " + string.Join("; ", hs.ToArray()));
                        Console.WriteLine("    " + p.personal_name + " engaged and cut off: " + string.Join("; ", cs.ToArray()));
                    }
                    if (Environment.GetEnvironmentVariable("HQWATCH") != null && hq != null) {
                        Building hb = GameController.HQOf(p);
                        List<string> near = new List<string>();
                        foreach (Unit x in enemies) if (x.tile.Distance(hq) <= 4) near.Add(x.name.Replace(" Variant", "").Replace("(Clone)", "") + "(" + x.tile.x + "," + x.tile.y + ")s" + x.Strength + (FogOfWar.On && !FogOfWar.Sees(p, x) ? " unseen" : ""));
                        int mine = 0; foreach (Unit x in p.units) if (x != null && x.tile != null && x.Strength > 0 && x.tile.Distance(hq) <= 4) mine++;
                        if (near.Count > 0 || (hb != null && hb.capturingPlayer != null))
                            Console.WriteLine("    " + p.personal_name + " HQ: capture " + (hb != null && hb.capturingPlayer != null ? hb.capturingPlayer.personal_name + " " + hb.capturePoints : "none") + "; enemies within 4: " + string.Join(", ", near.ToArray()) + "; ours within 4: " + mine);
                    }
                    hubHq.Sort(); hubEnemy.Sort();
                    Func<List<int>, string> med = xs => xs.Count == 0 ? "-" : xs[xs.Count / 2].ToString();
                    int[] f = fire[p];
                    float[] eyeP = eye[p];
                    Console.WriteLine(string.Format("  {0,-4} forecast of the enemy's replies so far: -{1:0} strength, really -{2:0}; units that lost 3+ beyond it: {3}", p.personal_name, eyeP[0], eyeP[1], eyeP[2]));
                    Console.WriteLine(string.Format("  {0,-4} units {1,2} (lost {2,2}: {3} to fire, {4} to attrition) | engaged {5,2}: cut off {6}, starving {7}, ammo low {8}, fuel low {9} | supply units {10}: from HQ {11}, from enemy {12}, idle in the rear {13}, cut off {14} | fire {15} shots, dealt {16}, taken {17}, losing {18}",
                        p.personal_name, Alive(p), start[p] - Alive(p), killed[p], p.unitsLostToAttrition, engaged, cut, starving, ammo, fuel,
                        hubs, med(hubHq), med(hubEnemy), idle, stranded, f[0], f[1], f[2], f[3]));
                }
            }
        } finally { Unit.Attacked -= onAttack; }
        Console.WriteLine("forecast misses (lost 3+ beyond the forecast):");
        foreach (string m in misses) Console.WriteLine("  " + m);
        Console.WriteLine("winner: " + (gc.winner != null ? gc.winner.personal_name : "none") + "; material Blue " + ScenarioFile.Material(l.ai) + " Red " + ScenarioFile.Material(l.other));
    }

    /// <summary>
    /// PROBE=bench SET_A=... [SET_B=...] [MAPS=a.dat,b.dat] [FOGS=off,on] [ROUNDS=r]: the challenger (A)
    /// against the champion (B) on bigger maps, each played from both sides, with fog off and on; and
    /// the champion against itself on the same games. Per game, the challenger's material lead at the
    /// end less the champion's own lead from that side (the self-play baseline): above 0, the
    /// challenger did better from there than the champion does. The sum is the verdict. (Default maps:
    /// ChanceMeeting and its side-swapped copy, and the legacy campaign maps from both sides.)
    /// </summary>
    public static void Bench(Action<AIProfile> challenger, Action<AIProfile> champion, string cName, string chName) {
        Console.WriteLine("challenger = " + cName + "\nchampion   = " + chName);
        List<string> maps = new List<string>();
        string m = Environment.GetEnvironmentVariable("MAPS");
        if (!string.IsNullOrEmpty(m)) maps.AddRange(m.Split(','));
        else {
            foreach (string f in Files("campaign")) if (Path.GetFileName(f).Contains("-legacy-ai-")) maps.Add(f);
        }
        int rounds = int.Parse(Environment.GetEnvironmentVariable("ROUNDS") ?? "30");
        float total = 0f; int better = 0, worse = 0;
        string[] seeds = (Environment.GetEnvironmentVariable("SEEDS") ?? "0").Split(',');
        Action<AIProfile> chal0 = challenger, champ0 = champion;
        foreach (string seedText in seeds)
        foreach (string fog in (Environment.GetEnvironmentVariable("FOGS") ?? "off,on").Split(',')) {
            int seed = int.Parse(seedText);
            foreach (string map in maps) {
                float[] lead = new float[2];
                int[] ground = new int[2], held = new int[2];
                // The champion's own game (k = 1) is the same every time: kept in BENCH_CACHE, if given.
                string cacheFile = Environment.GetEnvironmentVariable("BENCH_CACHE");
                string key = fog + "|" + Path.GetFileName(Find(map)) + "|" + rounds + "|" + chName + (seed != 0 ? "|seed " + seed : "");
                bool cached = false;
                if (!string.IsNullOrEmpty(cacheFile) && File.Exists(cacheFile))
                    foreach (string line in File.ReadAllLines(cacheFile)) {
                        int tab = line.LastIndexOf('\t');
                        if (tab <= 0 || line.Substring(0, tab) != key) continue;
                        string[] v = line.Substring(tab + 1).Split(';');
                        if (v.Length < 3) continue;   // an entry from before ground was kept: play it again
                        lead[1] = float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture);
                        ground[1] = int.Parse(v[1]); held[1] = int.Parse(v[2]);
                        cached = true;
                    }
                // BENCH_LOGS: every game played is recorded (GameLog: a board every turn, the moves and the fights),
                // and its last board, material, ground and buildings written to final.txt.
                string logs = Environment.GetEnvironmentVariable("BENCH_LOGS");
                for (int k = 0; k < (cached ? 1 : 2); k++) {
                    ScenarioFile.Loaded l = ScenarioFile.Load(Find(map), "Full", k == 0 ? challenger : champion, champion);
                    Rig.gc.fogOfWar = fog == "on"; FogOfWar.Reset();
                    GameController gc = Rig.gc;
                    string folder = null;
                    if (!string.IsNullOrEmpty(logs)) {
                        string name = Path.GetFileNameWithoutExtension(map) + "-fog-" + fog + (seed != 0 ? "-seed-" + seed : "") + (k == 0 ? "-challenger" : "-champion");
                        HexTactica.Harness.GameLog.Start(map + " (" + (k == 0 ? cName : chName) + " as Blue)", "Full", Path.Combine(logs, name));
                        // Moves, fights and boards; not every decision's reasoning (that is PROBE=game's).
                        AITactical.DecisionLog = null;
                        // BENCH_TRACE: keep the AI's turn trace (each unit's action and why, the campaign's objectives).
                        if (Environment.GetEnvironmentVariable("BENCH_TRACE") == null) AIPlayer.Trace = null;
                        folder = HexTactica.Harness.GameLog.Folder;
                    }
                    for (int t = 0; t < rounds * 2 && !gc.gameOver; t++) ScenarioFile.PlayTurn();
                    lead[k] = ScenarioFile.Material(l.ai) - ScenarioFile.Material(l.other);
                    if (gc.winner == l.ai) lead[k] += 100000f; else if (gc.winner == l.other) lead[k] -= 100000f;
                    int ga, go, ha, ho;
                    Ground(l.ai, l.other, out ga, out go, out ha, out ho);
                    ground[k] = ga - go; held[k] = ha - ho;
                    if (folder != null) {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("final board after round " + gc.days + (gc.gameOver ? ", winner " + (gc.winner != null ? gc.winner.personal_name : "none") : ""));
                        sb.Append(HexTactica.Harness.GameLog.Draw());
                        sb.AppendLine("material: Blue " + ScenarioFile.Material(l.ai) + ", Red " + ScenarioFile.Material(l.other) + "; lead " + lead[k].ToString("0"));
                        sb.AppendLine("ground (tiles only that side supplies): Blue " + ga + ", Red " + go + "; buildings: Blue " + ha + ", Red " + ho);
                        File.WriteAllText(Path.Combine(folder, "final.txt"), sb.ToString());
                        HexTactica.Harness.GameLog.Stop();
                    }
                    if (k == 1 && !string.IsNullOrEmpty(cacheFile))
                        File.AppendAllText(cacheFile, key + "\t" + lead[1].ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + ground[1] + ";" + held[1] + "\n");
                }
                float d = lead[0] - lead[1];
                total += d; if (d > 0) better++; else if (d < 0) worse++;
                Console.WriteLine(string.Format("  fog {0,-3} {1,-44}" + (seed != 0 ? " seed " + seed : "") + " challenger lead {2,8:0}, champion's {3,8:0}: {4,8:+0;-0} | ground {5:+0;-0} vs {6:+0;-0}, buildings {7:+0;-0} vs {8:+0;-0}",
                    fog, Path.GetFileNameWithoutExtension(map), lead[0], lead[1], d, ground[0], ground[1], held[0], held[1]));
            }
        }
        Console.WriteLine(string.Format("  total {0:+0;-0}: better in {1}, worse in {2}", total, better, worse));
    }

    /// <summary>
    /// PROBE=suite SET_A=... [SET_B=...]: the scenario suite, no randomness, head to head. Every map
    /// (MAPS, default the five scenarios and the two campaign maps) is one game of two runs: the
    /// challenger (A) as Blue against the champion (B) as Red, then the sides swapped. Standard rules,
    /// no purchases; fog on or off for both runs, alternating from map to map (FOGS=alt; or FOGS=on
    /// or off for every map). Each run is scored for each side at the end:
    ///   material — its share of the material left on the board (strength x cost);
    ///   board    — the share of the board's land only it supplies;
    ///   income   — its share of all the buildings' income (what it has captured and kept);
    /// 100% on all three if it won (by the HQ or on attrition), 0% if it lost. A run's score is the
    /// mean of the three; the game's, the mean of its two runs. BENCH_LOGS: each run recorded.
    /// </summary>
    public static void Suite(Action<AIProfile> challenger, Action<AIProfile> champion, string cName, string chName) {
        Console.WriteLine("challenger = " + cName + "\nchampion   = " + chName);
        Func<string, string, string[]> list = (k, d) => (Environment.GetEnvironmentVariable(k) ?? d).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string[] maps = list("MAPS", "ChanceMeeting,BushWar,MountainPass,RiverCrossing,LogisticsZigzag,campaign-rough-legacy-ai-left,campaign-std-legacy-ai-left");
        // A scenario's name is looked for in Assets/Scenarios, then as the harness's own.
        for (int i = 0; i < maps.Length; i++) {
            string game = Path.Combine(PrefabLoader.Root("Scenarios"), maps[i] + ".dat");
            if (!File.Exists(maps[i]) && File.Exists(game)) maps[i] = game;
        }
        string fogSet = Environment.GetEnvironmentVariable("FOGS") ?? "alt";
        // MAPINDEX: this map's place in the full list, for which way the fog alternates when maps are run one at a time.
        int first = int.Parse(Environment.GetEnvironmentVariable("MAPINDEX") ?? "0");
        string logs = Environment.GetEnvironmentVariable("BENCH_LOGS");
        int rounds = int.Parse(Environment.GetEnvironmentVariable("ROUNDS") ?? "30");
        float sumC = 0f, sumB = 0f; int n = 0, wonC = 0, wonB = 0;
        string rulesWas = Environment.GetEnvironmentVariable("RULES"), purchasesWas = Environment.GetEnvironmentVariable("PURCHASES");
        try {
            Environment.SetEnvironmentVariable("RULES", "standard");
            // BUILDS=on: units may be bought (ForestClearing, with its production buildings).
            Environment.SetEnvironmentVariable("PURCHASES", Environment.GetEnvironmentVariable("BUILDS") == "on" ? "on" : "off");
            for (int mi = 0; mi < maps.Length; mi++) {
                string map = maps[mi];
                string fog = fogSet == "alt" ? ((first + mi) % 2 == 0 ? "on" : "off") : fogSet;
                float[] c = new float[2], b = new float[2];
                string[] how = new string[2];
                for (int k = 0; k < 2; k++) {
                    bool challengerRed = k == 1;
                    ScenarioFile.Loaded l = ScenarioFile.Load(Find(map), "Full", challengerRed ? champion : challenger, challengerRed ? challenger : champion);
                    Rig.gc.fogOfWar = fog == "on"; FogOfWar.Reset();
                    GameController gc = Rig.gc;
                    Player us = challengerRed ? l.other : l.ai, them = challengerRed ? l.ai : l.other;
                    string folder = null;
                    if (!string.IsNullOrEmpty(logs)) {
                        string name = Path.GetFileNameWithoutExtension(map) + "-fog-" + fog + "-challenger-" + (challengerRed ? "Red" : "Blue");
                        HexTactica.Harness.GameLog.Start(map + " (" + cName + " as " + (challengerRed ? "Red" : "Blue") + ", " + chName + " the other)", "Full", Path.Combine(logs, name));
                        AITactical.DecisionLog = null;
                        if (Environment.GetEnvironmentVariable("BENCH_TRACE") == null) AIPlayer.Trace = null;
                        folder = HexTactica.Harness.GameLog.Folder;
                    }
                    for (int t = 0; t < rounds * 2 && !gc.gameOver; t++) ScenarioFile.PlayTurn();
                    float[] sc = Score(us, them, gc), so = Score(them, us, gc);
                    c[k] = (sc[0] + sc[1] + sc[2]) / 3f; b[k] = (so[0] + so[1] + so[2]) / 3f;
                    how[k] = string.Format("as {0}: {1,4:0%} (material {2,4:0%} board {3,4:0%} income {4,4:0%}) v {5,4:0%}, {6}",
                        challengerRed ? "Red " : "Blue", c[k], sc[0], sc[1], sc[2], b[k], gc.winner == us ? "won" : gc.winner == them ? "lost" : "round " + gc.days);
                    if (gc.winner == us) wonC++; else if (gc.winner == them) wonB++;
                    if (folder != null) {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("final board after round " + gc.days + (gc.gameOver ? ", winner " + (gc.winner != null ? gc.winner.personal_name : "none") : ""));
                        sb.Append(HexTactica.Harness.GameLog.Draw());
                        sb.AppendLine("challenger " + how[k]);
                        File.WriteAllText(Path.Combine(folder, "final.txt"), sb.ToString());
                        HexTactica.Harness.GameLog.Stop();
                    }
                }
                float gcScore = (c[0] + c[1]) / 2f, gbScore = (b[0] + b[1]) / 2f;
                sumC += gcScore; sumB += gbScore; n++;
                Console.WriteLine(string.Format("  {0,-30} fog {1,-3} challenger {2,4:0%} v champion {3,4:0%} | {4} | {5} | GAME",
                    Path.GetFileNameWithoutExtension(map), fog, gcScore, gbScore, how[0], how[1]));
            }
        } finally { Environment.SetEnvironmentVariable("RULES", rulesWas); Environment.SetEnvironmentVariable("PURCHASES", purchasesWas); }
        if (n > 0) Console.WriteLine(string.Format("  SUITE over {0} games: challenger {1:0.0%} (runs won {2}), champion {3:0.0%} (runs won {4})", n, sumC / n, wonC, sumB / n, wonB));
    }

    /// <summary>
    /// PROBE=unopposed FILE=&lt;map&gt; [SET=...] [ROUNDS=30] [FOG=off]: Blue (this AI) alone — every Red
    /// unit removed, Red's buildings left, nobody playing Red. Each round: how close Blue's supply has
    /// come to Red's HQ (land steps from it to the nearest tile Blue supplies), how close its nearest
    /// fighting unit is, the share of the land it supplies, where its supply units are; and the round
    /// the HQ falls. Does it get its supply to the enemy HQ, and how fast?
    /// </summary>
    /// <summary>The values in Assets/AI/TestProfile.asset (the profile the game's scene uses) onto <paramref name="p"/>.</summary>
    public static void ApplyProfileAsset(AIProfile p) {
        foreach (KeyValuePair<string, string> kv in ProfileAsset()) {
            System.Reflection.FieldInfo fi = typeof(AIProfile).GetField(kv.Key);
            if (fi == null || fi.IsStatic) continue;
            try {
                if (fi.FieldType == typeof(bool)) fi.SetValue(p, kv.Value.Trim() == "1");
                else if (fi.FieldType == typeof(int)) fi.SetValue(p, int.Parse(kv.Value, System.Globalization.CultureInfo.InvariantCulture));
                else if (fi.FieldType == typeof(float)) fi.SetValue(p, float.Parse(kv.Value, System.Globalization.CultureInfo.InvariantCulture));
            } catch (FormatException) { }
        }
    }

    static Dictionary<string, string> ProfileAsset() {
        Dictionary<string, string> d = new Dictionary<string, string>();
        string path = Path.Combine(PrefabLoader.AssetsRoot, "AI", "TestProfile.asset");
        if (!File.Exists(path)) return d;
        foreach (string line in File.ReadAllLines(path)) {
            Match m = Regex.Match(line, @"^  (\w+):\s*(.*)$");
            if (m.Success) d[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }
        return d;
    }

    public static void Unopposed(string file, Action<AIProfile> tweak, int rounds) {
        // Standard rules (under attrition a side with no fighting units is out at once), no purchases.
        if (Environment.GetEnvironmentVariable("RULES") == null) Environment.SetEnvironmentVariable("RULES", "standard");
        if (Environment.GetEnvironmentVariable("PURCHASES") == null) Environment.SetEnvironmentVariable("PURCHASES", "off");
        // ENEMY=half / full: Red is played by this AI with its default profile, with every other unit of each
        // kind removed (half) or all of them (full). Unset: Red has no units and does nothing (unopposed).
        string enemy = Environment.GetEnvironmentVariable("ENEMY");
        bool opposed = enemy == "half" || enemy == "full";
        // Blue: the campaign AI (Assets/AI/TestProfile.asset, the game's profile, with SET's changes on top).
        // Red: the baseline it is measured against — the same profile with the campaign off (SET_RED changes it).
        Action<AIProfile> blueProfile = pr => { ApplyProfileAsset(pr); if (tweak != null) tweak(pr); };
        Action<AIProfile> redTweak = Tests.Tweak(Environment.GetEnvironmentVariable("SET_RED"));
        Action<AIProfile> redProfile = pr => { ApplyProfileAsset(pr); if (redTweak != null) redTweak(pr); };
        ScenarioFile.Loaded l = opposed ? ScenarioFile.Load(Find(file), "Full", blueProfile, redProfile) : ScenarioFile.Load(Find(file), "Human", blueProfile);
        if (opposed) Console.WriteLine("Blue: TestProfile.asset with SET; Red, the baseline: TestProfile.asset with SET_RED");
        Rig.gc.fogOfWar = (Environment.GetEnvironmentVariable("FOG") ?? "off") == "on"; FogOfWar.Reset();
        if (!opposed) { foreach (Unit u in new List<Unit>(l.other.units)) if (u != null) u.Perish(); }
        else if (enemy == "half") {
            Dictionary<string, List<Unit>> byKind = new Dictionary<string, List<Unit>>();
            foreach (Unit x in l.other.units) if (x != null && x.tile != null) { string k = GameController.UnitName(x); if (!byKind.ContainsKey(k)) byKind[k] = new List<Unit>(); byKind[k].Add(x); }
            List<string> kinds = new List<string>(byKind.Keys); kinds.Sort(StringComparer.Ordinal);
            foreach (string k in kinds) { List<Unit> g = byKind[k]; g.Sort((a, b) => CombatMath.CompareTiles(a.tile, b.tile)); for (int i = 1; i < g.Count; i += 2) g[i].Perish(); }
        }
        // DEATHLOG=1: every unit's death, how it died, and whether the other side could see it.
        if (Environment.GetEnvironmentVariable("DEATHLOG") != null) {
            Unit lastAtk = null, lastDef = null;
            Unit.Attacking += (atk, def, surprise) => { lastAtk = atk; lastDef = def; };
            Unit.Died += u => {
                if (u == null) return;
                string how = u == lastDef ? "killed by " + (lastAtk != null ? lastAtk.owner.personal_name + " " + GameController.UnitName(lastAtk) : "?")
                           : u == lastAtk ? "died attacking " + (lastDef != null ? GameController.UnitName(lastDef) : "?")
                           : u.trasnporter != null ? "aboard " + GameController.UnitName(u.trasnporter) + " (it died)"
                           : u.Strength <= 0 && u.fuelMax > 0 && u.fuel <= 0 ? "out of fuel"
                           : "attrition or terrain (supplies " + u.supplies + "/" + u.suppliesMax + ", fuel " + u.fuel + ")";
                Player other = null; foreach (Player q in Rig.gc.players) if (q != null && q != u.owner) other = q;
                bool seen = !Rig.gc.fogOfWar || (u.tile != null && (u.trasnporter != null ? u.trasnporter.tile != null && FogOfWar.SeesAt(other, u.trasnporter, u.trasnporter.tile) : FogOfWar.SeesAt(other, u, u.tile)));
                Console.WriteLine("   death: day " + Rig.gc.days + " " + (u.owner != null ? u.owner.personal_name : "?") + " " + GameController.UnitName(u) + " at " + (u.tile != null ? "(" + u.tile.x + "," + u.tile.y + ")" : "-") + ", " + how + "; " + (other != null ? other.personal_name : "?") + " saw it " + seen + " (turn of " + (Rig.gc.activePlayer != null ? Rig.gc.activePlayer.personal_name : "?") + ")");
            };
        }
        int redStart = 0, blueStart = 0; foreach (Unit x in l.other.units) if (x != null && x.tile != null) redStart++; foreach (Unit x in l.ai.units) if (x != null && x.tile != null) blueStart++;
        if (opposed) Console.WriteLine("opposed (" + enemy + "): Red " + redStart + " units, Blue " + blueStart);
        Building theirs = GameController.HQOf(l.other);
        HexTactica.AI.AIMap m0 = new HexTactica.AI.AIMap(l.ai, l.ai.GetComponent<AIPlayer>().Profile);
        Dictionary<HexTile, int> steps = m0.LandSteps(theirs.tile);
        int land = 0; foreach (HexTile t in Rig.hg.tiles) if (t != null && t.terrain != null && t.terrain.elevation >= 0) land++;
        int start = 99; foreach (Unit u in l.ai.units) { int d; if (u != null && u.tile != null && steps.TryGetValue(u.tile, out d)) start = Math.Min(start, d); }
        Console.WriteLine("unopposed: " + Path.GetFileNameWithoutExtension(file) + ", Red HQ at (" + theirs.tile.x + "," + theirs.tile.y + "), Blue's nearest unit " + start + " land steps from it; " + land + " land tiles");
        GameController gc = Rig.gc;
        int reached = -1;
        string tr = Environment.GetEnvironmentVariable("TRACEROUND") ?? "-1"; int traceRound = tr == "all" ? -2 : int.Parse(tr);   // all: every round
        for (int r = 0; r < rounds && !gc.gameOver; r++) {
            if (r == traceRound || traceRound == -2) { Console.WriteLine("== round " + r); if (Environment.GetEnvironmentVariable("BOARD") != null) { Console.WriteLine(HexTactica.Harness.GameLog.Draw()); Console.WriteLine("   units: " + HexTactica.Harness.GameLog.Roster()); } if (Environment.GetEnvironmentVariable("CAMPDBG") != null) HexTactica.AI.AICampaign.Debug = x => Console.WriteLine("   " + x); AIPlayer.Trace = Console.WriteLine; HexTactica.AI.AISupplyChain.DebugLog = Console.WriteLine; if (Environment.GetEnvironmentVariable("EXPLAIN") != null) AITactical.DecisionLog = Console.WriteLine; AITactical.DebugBreakdown = Environment.GetEnvironmentVariable("BREAKDOWN") != null; }
            ScenarioFile.PlayTurn(); if (!gc.gameOver) ScenarioFile.PlayTurn();
            AIPlayer.Trace = null; AITactical.DecisionLog = null; HexTactica.AI.AISupplyChain.DebugLog = null;
            SupplyNetwork.Result net = SupplyNetwork.Solve(l.ai, false);
            int supplyGap = 99, unitGap = 99, ours = 0;
            foreach (HexTile t in Rig.hg.tiles) {
                if (t == null || t.terrain == null || t.terrain.elevation < 0) continue;
                if (net.DepthAt(t) <= 0) continue;
                ours++;
                int d; if (steps.TryGetValue(t, out d)) supplyGap = Math.Min(supplyGap, d);
            }
            List<string> hubs = new List<string>();
            foreach (Unit u in l.ai.units) {
                if (u == null || u.tile == null) continue;
                int d; bool on = steps.TryGetValue(u.tile, out d);
                if (u.Hub != null && !u.Hub.producer) hubs.Add("(" + u.tile.x + "," + u.tile.y + ")" + (on ? d.ToString() : "?") + (net.connectedHubs.Contains(u.Hub) ? "" : "x") + (u.trasnporter != null ? "T" : ""));
                else if (Transporting.IsTransport(u) && u.Firepower <= 1) hubs.Add("truck(" + u.tile.x + "," + u.tile.y + ")" + (on ? d.ToString() : "?") + (u.transported.Count > 0 ? "+" : ""));
                else if (u.Firepower > 0 && on) unitGap = Math.Min(unitGap, d);
            }
            if (supplyGap == 0 && reached < 0) reached = r;
            int held = 0, all = 0; List<string> taking = new List<string>();
            foreach (Building b in Building.all) {
                if (b == null || b.IsHQ) continue;
                all++; if (b.owner == l.ai) held++;
                else if (b.capturingPlayer == l.ai && b.capturePoints > 0) taking.Add("(" + b.tile.x + "," + b.tile.y + ")" + b.capturePoints);
            }
            Console.WriteLine(string.Format("  round {0,2}: supply {1,2} steps from their HQ, nearest unit {2,2}; land supplied {3,4:0%}; buildings {8}/{9}{10}; HQ {4}/{5} {6}; supply units {7}",
                r, supplyGap, unitGap, ours / (float)land, theirs.capturePoints, theirs.CaptureThreshold, theirs.owner == l.other ? "theirs" : theirs.owner == l.ai ? "OURS" : "neutral", string.Join(" ", hubs.ToArray()),
                held, all, taking.Count > 0 ? " (taking " + string.Join(" ", taking.ToArray()) + ")" : ""));
        }
        { List<string> owned = new List<string>(); foreach (Building b in Building.all) if (b != null && b.tile != null && b.owner == l.ai && !b.IsHQ) owned.Add(b.name.Replace("(Clone)", "") + Pos(b.tile)); owned.Sort(); Console.WriteLine("owned: " + string.Join(" ", owned.ToArray())); }
        Console.WriteLine("result: " + (gc.gameOver && gc.winner == l.ai ? "took the HQ on round " + gc.days : gc.gameOver && gc.winner == l.other ? "LOST (Red won) on round " + gc.days : "HQ not taken") + "; supply reached the HQ " + (reached >= 0 ? "on round " + reached : "never"));
        if (opposed) {
            int red = 0, blue = 0, redB = 0; foreach (Unit u in l.other.units) if (u != null && u.tile != null) red++; foreach (Unit u in l.ai.units) if (u != null && u.tile != null) blue++;
            foreach (Building b in Building.all) if (b != null && !b.IsHQ && b.owner == l.other) redB++;
            Console.WriteLine("units: Blue " + blue + " (started " + blueStart + "), Red " + red + " (started " + redStart + "); Red buildings " + redB);
        }
    }

    /// <summary>
    /// PROBE=reachcap FILE=&lt;map&gt; HUBS=n TRUCKS=k: how far Blue's supply can reach toward Red's HQ at
    /// best, with n supply units and k trucks (greedy, nobody in the way): every supply unit on the
    /// ground, each where the network is furthest along the way; then with k of them aboard trucks
    /// (a supply unit aboard is linked when its truck could reach supplied ground in one move, so a
    /// truck carries the network across a gap). Land steps from Red's HQ to the nearest tile supplied.
    /// </summary>
    public static void ReachCap(string file, int hubs, int trucks) {
        Environment.SetEnvironmentVariable("RULES", "standard");
        foreach (bool useTrucks in new[] { false, true }) {
            ScenarioFile.Loaded l = ScenarioFile.Load(Find(file), "Human", null);
            foreach (Unit u in new List<Unit>(l.other.units)) if (u != null) u.Perish();
            foreach (Unit u in new List<Unit>(l.ai.units)) if (u != null) u.Perish();
            Building theirs = GameController.HQOf(l.other);
            HexTactica.AI.AIMap m = new HexTactica.AI.AIMap(l.ai, l.ai.GetComponent<AIPlayer>().Profile);
            Dictionary<HexTile, int> steps = m.LandSteps(theirs.tile);
            Func<int> reach = () => {
                SupplyNetwork.Result net = SupplyNetwork.Solve(l.ai, false);
                int best = 99;
                foreach (KeyValuePair<HexTile, int> kv in steps) if (net.DepthAt(kv.Key) > 0) best = Math.Min(best, kv.Value);
                return best;
            };
            List<HexTile> order = new List<HexTile>(steps.Keys);
            order.Sort((a, b) => steps[a].CompareTo(steps[b]));
            List<string> log = new List<string>();
            int left = hubs;
            if (useTrucks) {
                for (int k = 0; k < trucks && left > 0; k++) {
                    bool placed = false;
                    foreach (HexTile t in order) {
                        if (t.occupier != null || t.building != null) continue;
                        Unit tr = Put("Truck Variant", l.ai, t.x, t.y);
                        if (tr == null || tr.tile != t) { if (tr != null) tr.Perish(); continue; }
                        HexTile beside = null;
                        foreach (HexTile n in t.neighbors) if (n != null && n.occupier == null && n.building == null && n.terrain != null && n.terrain.elevation >= 0) { beside = n; break; }
                        if (beside == null) { tr.Perish(); continue; }
                        Unit hub = Put("Supplies", l.ai, beside.x, beside.y);
                        if (hub == null) { tr.Perish(); continue; }
                        bool loaded = Transporting.Load(tr, hub);
                        SupplyNetwork.Result net = SupplyNetwork.Solve(l.ai, false);
                        if (loaded && net.connectedHubs.Contains(hub.GetComponent<SupplyPoint>())) { placed = true; log.Add("truck (" + t.x + "," + t.y + ")" + steps[t]); left--; break; }
                        hub.Perish(); tr.Perish();
                    }
                    if (!placed) break;
                }
            }
            for (; left > 0; left--) {
                SupplyNetwork.Result net = SupplyNetwork.Solve(l.ai, false);
                bool placed = false;
                foreach (HexTile t in order) {
                    if (t.occupier != null || t.building != null || net.DepthAt(t) <= 0) continue;
                    Unit hub = Put("Supplies", l.ai, t.x, t.y);
                    if (hub == null || hub.tile != t) { if (hub != null) hub.Perish(); continue; }
                    placed = true; log.Add("(" + t.x + "," + t.y + ")" + steps[t]); break;
                }
                if (!placed) break;
            }
            Console.WriteLine((useTrucks ? "with " + trucks + " trucks" : "all on the ground") + ": supply " + reach() + " steps from Red's HQ; " + string.Join(" ", log.ToArray()));
        }
    }

    /// <summary>The suite's three measures for <paramref name="us"/> at the end: material share, share of the land only it supplies, share of the buildings' income; all 1 if it won, 0 if it lost.</summary>
    static float[] Score(Player us, Player them, GameController gc) {
        if (gc.gameOver && gc.winner == us) return new[] { 1f, 1f, 1f };
        if (gc.gameOver && gc.winner == them) return new[] { 0f, 0f, 0f };
        float ma = ScenarioFile.Material(us), mo = ScenarioFile.Material(them);
        SupplyNetwork.Result ra = SupplyNetwork.Solve(us, false), ro = SupplyNetwork.Solve(them, false);
        int land = 0, ours = 0;
        foreach (HexTile t in Rig.hg.tiles) {
            if (t == null || t.terrain == null || t.terrain.elevation < 0) continue;
            land++;
            if (ra.DepthAt(t) > 0 && ro.DepthAt(t) <= 0) ours++;
        }
        float ia = 0f, all = 0f;
        foreach (Building b in Building.all) { if (b == null) continue; all += b.income; if (b.owner == us) ia += b.income; }
        return new[] { ma + mo > 0f ? ma / (ma + mo) : 0.5f, land > 0 ? (float)ours / land : 0f, all > 0f ? ia / all : 0f };
    }

    /// <summary>The Runway buildings beside <paramref name="ap"/> (Rig.PlaceAirfield).</summary>
    static List<Building> Runways(Building ap) {
        List<Building> r = new List<Building>();
        foreach (Building b in Building.all) if (b != null && b != ap && b.tile != null && b.name.Replace("(Clone)", "").Trim() == "Runway" && b.tile.Distance(ap.tile) <= 2) r.Add(b);
        return r;
    }

    /// <summary>Take <paramref name="b"/> off the board (its tiles get their ground back).</summary>
    static void RemoveBuilding(Building b) {
        b.Release();
        if (BuildingController.bc != null) BuildingController.bc.buildings.Remove(b);
        Building.all.Remove(b);
    }

    /// <summary>Tiles only each side supplies, and the buildings each owns (HQs included).</summary>
    static void Ground(Player a, Player o, out int groundA, out int groundO, out int heldA, out int heldO) {
        SupplyNetwork.Result ra = SupplyNetwork.Solve(a, false), ro = SupplyNetwork.Solve(o, false);
        groundA = groundO = heldA = heldO = 0;
        foreach (HexTile t in Rig.hg.tiles) {
            bool sa = ra.DepthAt(t) > 0, so = ro.DepthAt(t) > 0;
            if (sa && !so) groundA++; else if (so && !sa) groundO++;
        }
        foreach (Building b in Building.all) { if (b == null) continue; if (b.owner == a) heldA++; else if (b.owner == o) heldO++; }
    }

    /// <summary>Where up the winding road the buy-supply check puts Blue's grunts (road tiles from Blue's HQ).</summary>
    static int GruntsAt = 10;

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
