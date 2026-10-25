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
                        Building b = Rig.PlaceBuilding("Airport", p, c.x, c.y);
                        List<HexTile> all = new List<HexTile>(b.ServiceTiles());
                        bool fits = b.Footprint.Count == 2 && b.runway.Count == 3;
                        foreach (HexTile t in all) if (t == null || t.occupier != null || t == hq || t.terrain == T("Water") || (t.building != null && t.building != b)) fits = false;
                        if (fits) { ap = b; break; }
                        b.Release(); BuildingController.bc.buildings.Remove(b); Building.all.Remove(b);
                    }
                    if (ap == null) { ok = false; break; }
                    List<HexTile> pads = new List<HexTile>(ap.ServiceTiles());
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
            setup(ai, other);
            Dictionary<string, string> test = Test(name, "rules", "Human", 4, check, question);
            test["rules"] = check.StartsWith("attrition") ? "attrition" : "standard";
            ScenarioFile.Save(Path.Combine(Dir, name + ".dat"), test, ai, other, ai);
            n++;
        };
        make("rule-capture-neutral", "captureNeutral", (a, o) => { Rig.PlaceBuilding("City", null, 0, 0); Put("Grunt", a, 0, 0); },
             "A Blue grunt (6) on a neutral City (threshold 10): neutral with 6 Blue after one Blue turn, Blue's after two.");
        make("rule-capture-overflow", "captureOverflow", (a, o) => { Building c = Rig.PlaceBuilding("City", a, 0, 0); c.capturePoints = 3; Put("Grunt", o, 0, 0); },
             "A Blue City at 3 points, a Red grunt (6) on it: after Red's turn it is neutral with 3 Red.");
        make("rule-attack-no-capture", "attackNoCapture", (a, o) => { Rig.PlaceBuilding("City", a, 0, 0); Put("Grunt", o, 0, 0); Put("Grunt", a, 1, 0); },
             "A Red grunt on a Blue City that attacks during its turn does not capture.");
        make("rule-contested-airport", "contested", (a, o) => { Building ap = Rig.PlaceBuilding("Airport", a, 0, 0); ap.capturePoints = 3; Put("Grunt", a, 0, 0); Put("Grunt", o, 1, 0); },
             "A two-tile Airport (threshold 15) at 3 Blue, a Blue grunt and a Red grunt on it: Blue cannot raise it; Red brings it to 0 (neutral), not past.");
        make("rule-neutral-decay", "decay", (a, o) => { Building c = Rig.PlaceBuilding("City", null, 0, 0); c.capturingPlayer = o; c.capturePoints = 4; Put("Grunt", o, 3, 0); },
             "A neutral City with 4 Red progress and no Red infantry on it: 2 after Red's turn. An owned building does not decay.");
        make("rule-hq-standard", "hqStandard", (a, o) => { Put("Grunt", o, -6, 0); },
             "Standard rules: a Red grunt on Blue's HQ (10). Two Red turns bring it to 0: Blue has lost.");
        make("attrition-rule-hq", "attritionHQ", (a, o) => { Put("Grunt", o, -6, 0); Put("Grunt", a, -3, 2); },
             "Attrition rules: the HQ cannot be captured, a Red grunt standing on it changes nothing.");
        make("rule-fob-battery", "battery", (a, o) => { Building f = Rig.PlaceBuilding("FOB", a, -4, 0); f.supplies = 0; Put("Grunt", a, -3, 1); },
             "A Blue FOB inside the HQ's supply charges 1 a turn; cut off (the HQ lost), it supplies its area for as many turns as it stored, then stops.");
        make("rule-production", "production", (a, o) => { },
             "Blue's HQ builds a Grunt for 2000 of 3000: it appears on the HQ, spent for the turn, money 1000.");
        // Air and sea (attrition: these do not test capture).
        Action<int, int, int, int> water = (x0, x1, y0, y1) => { for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) { HexTile t = Rig.At(x, y); if (t != null) t.terrain = T("Water"); } };
        make("attrition-rule-air-fuel", "attritionAirFuel", (a, o) => { Unit f = Put("Fighter", a, -4, 1); f.fuel = 5; f.Strength = 4; Put("Grunt", o, 4, 0); },
             "A Fighter (fuel 5, burns 3 a turn; 3 supplies aboard) inside the HQ's supply but off any airport: not healed or refuelled, its own supplies down to 2 and 2 fuel after a turn, ditched the next.");
        make("attrition-rule-air-base", "attritionAirBase", (a, o) => { Rig.PlaceBuilding("Airport", a, -3, 2); Unit f = Put("Fighter", a, -3, 3); f.fuel = 2; f.Strength = 4; Put("Grunt", o, 4, 0); },
             "A Fighter on its own airport's runway (fuel 2, strength 4) is refuelled to 14 and healed to 5.");
        make("attrition-rule-air-tanker", "attritionTanker", (a, o) => { Unit f = Put("Fighter", a, 0, 0); f.fuel = 6; Unit t = Put("AirTanker", a, 1, 0); t.refuelStock = 1; Put("Grunt", o, 5, 0); },
             "A Fighter starting its turn beside an air tanker with 1 refuel left is refuelled (tanker 0); the next turn the tanker is empty and the fighter burns 3.");
        make("attrition-rule-ship-fuel", "attritionShip", (a, o) => { water(-1, 2, -7, 7); Rig.PlaceBuilding("NavalYard", a, -2, 0); Put("Gunboat", a, 1, 2); Unit d = Put("Gunboat", a, -1, 0); d.Strength = 3; d.fuel = 10; Put("Grunt", o, 5, 0); },
             "Ships burn fuel only by moving (3 tiles: 3 fuel) and never starve away from port; one on its naval yard's dock is healed and refuelled.");
        make("attrition-rule-refuel-on-landing", "attritionLandRefuel", (a, o) => { Rig.PlaceBuilding("Airport", a, -3, 2); Unit f = Put("Fighter", a, 0, 2); f.fuel = 3; f.ammo = 1; Unit g = Put("Fighter", a, 0, 4); g.fuel = 5; g.ammo = 1; Put("Grunt", o, 4, 0); },
             "A Fighter with 3 fuel flies 3 tiles onto its airport's runway (a plain move): refuelled and re-armed as it lands (fuel 14, ammo 6). Another lands there as a move and attack: nothing until its next turn (fuel 5 - 3 = 2, ammo 1). The airport is then lost before their next turn: the first burns a turn's fuel (11) and is not ditched.");
        make("attrition-rule-supply-aboard", "attritionSupplyAboard", (a, o) => { Put("APC", a, 1, 0); Put("Supplies", a, 2, 0); Put("Grunt", o, 6, -3); },
             "A supply unit (range 4) aboard an APC (moves 6) beyond the HQ's supply: linked to the network, because the APC could carry it onto supplied ground in one move, and spreading supply only its own range around the APC. With the APC slowed to 1 move, it is cut off.");
        make("attrition-rule-supply-line", "attritionSupplyLine", (a, o) => { Put("Supplies", a, -3, 0); Put("Grunt", a, -2, 0); Put("Grunt", o, 6, -3); },
             "A grunt supplied only through a supply unit standing at the edge of the HQ's supply: the line traced back from the grunt runs to the supply unit, then on to the HQ.");
        make("rule-retreat-away", "retreatAway", (a, o) => { Put("Tank", a, 1, 0); Put("Grunt", o, 0, 0); },
             "A Red grunt attacked by a Blue tank beside it retreats straight back, to one of the three tiles on its far side; with those three taken it is pinned, never sliding sideways (still beside the tank).");
        make("rule-bridge-road-only", "bridgeRoad", (a, o) => {
                 water(0, 0, -7, 7);
                 Rig.At(0, 0).terrain = T("Bridge");
                 Rig.At(-1, 0).terrain = T("Road"); Rig.At(1, 0).terrain = T("Road");
                 Put("Grunt", a, -1, 1); Put("Grunt", o, 4, 3); },
             "A river with one bridge, a road tile at each end and grass beside them. A bridge joins only those roads: no stepping onto it from the grass beside it, or off it onto the grass across; a unit driven back off it goes onto the road.");
        make("rule-swap-sides", "swapSides", (a, o) => { Put("Tank", a, -2, 0); Put("Grunt", a, -3, 1); Put("Artillery", o, 3, 0); Building c = Rig.PlaceBuilding("City", o, 2, 2); c.capturingPlayer = a; },
             "HexTactica > Swap Sides of Scenario File on this file: every unit and building changes to the other side (the HQs too, and who was capturing what); the player list and terrain are unchanged.");
        make("attrition-rule-cut-off-supply", "attritionCutOff", (a, o) => { Put("Supplies", a, 4, 0); Put("Grunt", a, 4, 1); Put("Grunt", o, 6, -4); },
             "A supply unit beside a grunt, both far outside the HQ's supply: a supply unit relays supply, it does not make it, so neither is supplied. After a turn the grunt has used a turn of its own supplies and the supply unit has lost no strength.");
        Action<string, string, Action<Player, Player>, string, string, string> makeBuy = (name, check, setup, question, purchases, allowed) => {
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("HQ", other, 6, 0);
            setup(ai, other);
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
            setup(ai, other);
            Dictionary<string, string> test = Test(name, "rules", opponent, opponent == "Full" ? 12 : 4, check, question);
            test["rules"] = "attrition";
            test["fog"] = "on";
            ScenarioFile.Save(Path.Combine(Dir, name + ".dat"), test, ai, other, ai);
            n++;
        };
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
        makeFog("fog-rule-bump", "fogBump", (a, o) => { Put("Grunt", a, -2, 0); Put("Grunt", o, 0, 0); Put("Grunt", a, 0, 3); Put("Grunt", o, 2, 3); },
             "Running into a hidden enemy: a Blue grunt (vision 1) moving through a Red grunt it cannot see stops on the tile before it; its move is over; the Red grunt fires first (whatever the ground) and the Blue grunt answers; the Red grunt is seen from then. The move range shown is the same as if the hidden unit were not there (its tile can be ended on as far as Blue knows).", "Human");
        makeFog("fog-view-after-load", "fogViewLoad", (a, o) => { Put("Grunt", o, -3, 0); Put("Grunt", a, 3, 0); },
             "Loading a scenario with fog of war on (Red, human, to play): every side's supply is published to the board at once (the borders are drawn), and on the first frame every tile Red does not see is covered, none it sees, without fading in. The fog reaches one cell past the edge of the board; those cells are cleared only beside a unit Red sees standing on the edge (not beside an enemy it does not see), and with fog off there are none.", "Human");
        makeFog("fog-ui-clicks", "fogClicks", (a, o) => { Put("Grunt", o, 0, 0); Put("Grunt", a, 2, 0); },
             "The board's clicks under fog of war, Red (human, vision 1) to play: a tile with a Blue grunt Red does not see behaves exactly like an empty one — clicked with nothing selected it selects nothing and shows no enemy; with Red's grunt selected it is offered as a move, the tile beyond it too, and the path drawn across it looks like a path across empty ground. Moving through it, Red's grunt stops before it and is fired on.", "Human");
        makeFog("fog-ai-knowledge", "fogKnowledge", (a, o) => { Put("Tank", a, -2, 0); Put("Recon", a, 0, 0); Put("Grunt", o, 3, 0); Put("Grunt", o, 2, -5); Put("Tank", o, 5, -2); },
             "The AI playing by the fog: thinking, it knows only what Blue sees — a hidden enemy is off the board (not in its read of the board, not targetable, not blocking) and one seen lately stands where it was last seen, unless that tile is in sight and empty; everything is put back afterwards. An ambush tile (out of the enemy's sight, in reach of an enemy) is worth something, an exposed one is not; a stop that brings unseen tiles into sight is worth more than one that does not. With fogFair off it knows everything.", "Human");
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
             "The board's overlays. Selecting Blue's artillery marks only the enemies it can fire on now (not its whole range, not the Blue grunt in range, not empty tiles). Clicking an enemy tank shows its move and reach at half opacity, the move tiles at half its colour's saturation (the reach in its full colour). The tile panel's capture line: a neutral city a Blue grunt is taking shows \"Capturing: Blue 6/10\"; a Red city it is bringing down shows \"Held: 4/10\" in Red; a city at full points shows none. The line at the top says whose turn it is, and for an AI whether it is thinking or moving.");
        make("rule-rotor-supply", "rotorSupply", (a, o) => {
                 Rig.At(-6, 0).building.supplyRange = 3;
                 Put("Fighter", a, -4, 0); Put("Fighter", a, 2, 3); Put("Fighter", a, -5, 1); },
             "Helicopters (rotor) are resupplied on any tile in their side's supply, as well as at airports: one in supply at the start of Blue's turn is refuelled, rearmed and heals a strength; one out of supply burns a turn's fuel and does not starve; a jet in supply still burns fuel (it needs an airport). The AI counts every supplied tile as somewhere a helicopter can go home to, and a helicopter there as healing.");
        make("rule-air-not-ambushed", "airNotAmbushed", (a, o) => {
                 Put("Fighter", a, -2, 0); Put("Tank", o, -1, 0); Put("Fighter", a, 2, 0); Put("Grunt", o, 3, 0); Put("Fighter", a, 0, 3); Put("Fighter", o, 1, 3);
                 Put("Tank", a, 0, -3); Put("Tank", o, 1, -3);
                 Rig.At(3, 0).terrain = T("Forest"); },
             "Aircraft are never ambushed by land or sea units: a helicopter attacking a tank on grassland, or a veteran grunt in a forest, fires first; land units still ambush each other as before (a veteran tank on grassland fires first against a tank), and an aircraft can still be fired on first by a veteran aircraft.");
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
                 Put("TransportShip", a, 3, 0); Put("Grunt", o, 6, -3); },
             "What fits where. An APC (2 slots, carries Boots) takes two grunts (weight 1) or one supply unit (weight 2), never a tank (Treads) or a fighter. A grunt cannot board a transport ship on open water beside the shore, but can at the naval yard's dock. A loaded APC can itself board the ship at the dock (it takes its own weight, 3 of the ship's 4), and everything aboard goes where the ship goes.");
        make("attrition-rule-suppression", "suppression", (a, o) => {
                 Put("Artillery", a, -4, 0); Put("Artillery", a, -4, 2); Put("Grunt", a, -1, 1); Put("Tank", a, -3, -2);
                 Put("Tank", o, 0, 0); Put("Tank", o, 0, 3); Put("Tank", o, -1, -2); Put("Grunt", o, 2, -2); },
             "Indirect fire suppresses instead of driving back. Artillery hits a tank hard enough that it would have retreated: it stays where it is, takes the same damage, and half its unsuppressed pips (rounded up) are suppressed. Attacked again that turn, it fires back with its strength less the suppressed pips (none at all when every pip is suppressed). A second artillery hit suppresses half of what is left. A hit too light to push it (a veteran) suppresses nothing. Its own turn starting, every pip comes back. Direct fire still drives back. Indirect fire is any attacker with a minimum range (it cannot fire on the next tile), not one that reaches far: a tank reaching two tiles still drives back.");
        make("attrition-rule-recon-transport", "reconTransport", (a, o) => { Put("Recon", a, -3, 0); Put("Grunt", a, -2, 0); Put("Grunt", o, 5, 0); },
             "The recon truck (1 slot, carries Boots) takes a grunt, drives, and puts it down beside it, as the APC does.");
        make("attrition-rule-alarms", "alarms", (a, o) => { Put("Grunt", a, -5, 0); Put("Grunt", a, 5, 0); Put("Tank", a, -4, 1); Put("Grunt", o, 6, -3); },
             "The alarm icon's conditions (fuel and ammo alarms from a third of a full load down): a grunt with no supplies out of supply is starving, one with none inside supply is not (it is fed next turn); a tank with no fuel is out of fuel, with no ammunition out of ammo.");
        make("attrition-rule-portrait-layout", "portraitLayout", (a, o) => { Put("Grunt", a, -3, 0); Put("Grunt", o, 4, 0); },
             "The UI pictures of units and tiles (SpritePortrait): every sprite of the unit is placed as on the board and the whole fitted into the box. A 2x1 hull, a 1x1 turret turned 90 degrees above its right half and a mirrored flag, fitted into 100x100 with a 4 px margin: scale 46, centred, the turret at 90 degrees, the flag flipped.");
        make("rule-sandbox-turns", "sandboxTurns", (a, o) => { Put("Grunt", o, 4, 0); },
             "Sandbox: turns go Blue, Red, Blue, Red even if a side is marked out (a file saved after it was knocked out), and the day goes up once per round.");
        make("rule-destructible-terrain", "destructible", (a, o) => {
                 Put("Grunt", a, 0, 0); Put("Grunt", o, 1, 0);
                 Rig.At(3, -2).terrain = T("Bridge"); Rig.At(3, -2).road = true; Put("Grunt", a, 3, -2);
                 Rig.PlaceBuilding("City", a, -3, 2); Put("Grunt", a, -3, 2); Put("Supplies", a, -4, 3); },
             "Destructible terrain: health split into (damage pictures + 1) equal parts (10 health, one picture: 10-6 normal, 5-1 damaged; two: 10-7, 6-4, 3-1). Each attack on a unit damages its tile by 1. A Grassland tile (health 10) hit ten times becomes Badlands, with what it was remembered; UndoDestruction puts the grass back one short of destroyed and Repair heals it. A bridge with a grunt on it, hit ten times, becomes water: no road, and the grunt is lost. A City (health 10) with a grunt on it takes the hits instead of its ground; destroyed, it earns, supplies and builds nothing until repaired. Water takes no damage.");
        make("rule-runway-no-capture", "runwayNoCapture", (a, o) => { Rig.PlaceBuilding("Airport", a, -3, 2); Put("Grunt", o, -3, 3); },
             "A Red grunt on a Blue airport's runway tile does not capture it: runways and docks are not part of the building.");
        make("attrition-rule-supply-boats", "attritionBoats", (a, o) => {
                 water(-2, 1, -7, 7);
                 Rig.PlaceBuilding("NavalYard", a, -3, 0);
                 Put("SupplyBoat", a, -1, 0); Put("SupplyBoat", a, 1, 0); Put("Grunt", a, 3, 0); Put("Grunt", o, 4, 3); },
             "A strait four tiles wide: the HQ's supply stops at the water. Two supply boats link from the naval yard's dock (in supply) to each other, and the far one supplies the island it borders. With the far boat moved out to sea, the island grunt is not supplied.");
        make("attrition-rule-transport", "attritionTransport", (a, o) => { Put("APC", a, -3, 0); Put("Grunt", a, -2, 0); Put("Grunt", o, 5, 0); },
             "A Grunt boards the APC beside it: off the board, riding the APC's tile and moving with it, parented to the APC's slot; unloaded beside it, it is back on the board and unparented. Getting off costs nothing: having boarded this turn it is still spent; aboard at the start of a turn, it is still free to act once unloaded.");
        make("attrition-rule-cargo-supply", "attritionCargoSupply", (a, o) => { water(-1, 7, -7, 7); Put("APC", a, -4, 0); Unit g = Put("Grunt", a, -4, 1); g.Strength = 4;
                 Put("TransportShip", a, -1, 1); Unit m = Put("Marine", a, -2, 1); m.supplies = 2; Put("Grunt", o, 4, 3); },
             "Cargo gets what its transport gets: aboard the APC inside supply a grunt is healed; a marine that boards a transport ship from the shore (it can step into water beside land) neither heals nor uses its supplies while the ship has supplies, and starves once the ship's are gone.");
        make("attrition-rule-amphibious", "attritionAmphibious", (a, o) => { water(-1, 2, -7, 7); Put("Marine", a, -2, 1); Put("Grunt", a, -2, 2); Put("Grunt", o, 5, 0); },
             "A Marine can enter water beside land but not open water away from it; a Grunt cannot enter water.");
        return n;
    }

    static bool RuleCheck(ScenarioFile.Loaded l, out string detail) {
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
                bool ok2 = at00.owner == a && at00.capturePoints == 10;
                detail = "after one Blue turn " + first + ", after two " + show(at00);
                return ok1 && ok2;
            }
            case "captureOverflow": {
                endTurn(); endTurn();
                detail = show(at00);
                return at00.owner == null && at00.capturingPlayer == o && at00.capturePoints == 3;
            }
            case "attackNoCapture": {
                endTurn();
                Unit red = Rig.At(0, 0).occupier;
                red.OrderAttack(Rig.At(1, 0).occupier);
                endTurn();
                detail = show(at00) + " after the Red grunt attacked from it";
                return at00.owner == a && at00.capturePoints == 10;
            }
            case "contested": {
                endTurn(); string afterBlue = show(at00);
                bool ok1 = at00.owner == a && at00.capturePoints == 3;
                endTurn();
                detail = "after Blue " + afterBlue + ", after Red " + show(at00);
                return ok1 && at00.owner == null && at00.capturePoints == 0 && at00.capturingPlayer == null;
            }
            case "decay": {
                endTurn(); endTurn();
                bool ok = at00.owner == null && at00.capturePoints == 2;
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
                return hq.owner == a && hq.capturePoints == 10 && !gc.gameOver;
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
                detail = made == null ? "nothing built" : "built " + made.name + " on " + Pos(made.tile) + ", active " + made.active + ", money " + a.money;
                return made != null && made.tile == hq.tile && !made.active && a.money == 1000;
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
                detail = "on the runway (" + (Rig.At(-3, 3).building != null && Rig.At(-3, 3).building.IsServiceTile(Rig.At(-3, 3)) ? "runway tile" : "not a runway") + "): fuel " + f.fuel + ", strength " + f.Strength;
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
                apc.moveRange = moves;
                SupplyNetwork.Solve(l.ai, false);
                detail = "loaded " + loaded + "; HQ supply " + gap + " tiles from the APC; linked " + linked + "; spreads up to " + far + " tiles from the APC (range " + hub.range + "); with 1 move, cut off " + cut;
                return loaded && gap > 1 && gap <= moves && linked && far <= hub.range && far > 0 && cut;
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
                l.ai.GetComponent<AIPlayer>().Profile.buyUnits = true;
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
                float share = UnitAlarmSprites.Instance.lowShare;
                bool a2 = UnitAlarmIcon.Low(0, tank.fuelMax, share) && UnitAlarmIcon.Low(0, tank.ammoMax, share);
                int third = tank.ammoMax / 3;
                bool a3 = UnitAlarmIcon.Low(third, tank.ammoMax, share) && !UnitAlarmIcon.Low(third + 1, tank.ammoMax, share) && !UnitAlarmIcon.Low(tank.fuelMax, tank.fuelMax, share)
                       && !UnitAlarmIcon.Low(0, 0, share);
                detail = "grunt in supply (" + homeSupplied + ") starving " + UnitAlarmIcon.Starving(home) + "; out of supply (" + farSupplied + ") starving " + UnitAlarmIcon.Starving(far)
                       + "; tank empty: fuel and ammo alarms " + a2 + "; ammo " + third + "/" + tank.ammoMax + " alarm, " + (third + 1) + " not, full fuel not, a unit carrying none never " + a3;
                return homeSupplied && !farSupplied && a1 && a2 && a3;
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
                            // As the game's profiles are by default: notes written on the board as the AI thinks.
                            ScenarioFile.Loaded g = ScenarioFile.Load(Find(map), "Full", pr => pr.annotateBoard = true, pr => pr.annotateBoard = true);
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
                HexTileUIData ui = HexTileUIData.instance;
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
                o.displayColor = new Color(0.9f, 0.2f, 0.25f, 1f);
                SupplyOverlay.ShowEnemy(tank);
                Color red = o.displayColor;
                float sat = Math.Max(red.r, Math.Max(red.g, red.b)) <= 0f ? 0f : (Math.Max(red.r, Math.Max(red.g, red.b)) - Math.Min(red.r, Math.Min(red.g, red.b))) / Math.Max(red.r, Math.Max(red.g, red.b));
                int moveTiles = 0, reachTiles = 0, badMove = 0, badReach = 0;
                foreach (HexTile t in Rig.hg.tiles) {
                    Color c = t.overlay.color;
                    float mx = Math.Max(c.r, Math.Max(c.g, c.b)), mn = Math.Min(c.r, Math.Min(c.g, c.b));
                    float s2 = mx <= 0f ? 0f : (mx - mn) / mx;
                    if (t.state == UIState.ENEMY_MOVE) { moveTiles++; if (Math.Abs(c.a - 0.5f) > 1e-4 || Math.Abs(s2 - sat * 0.5f) > 1e-3) badMove++; }
                    if (t.state == UIState.ENEMY_THREAT) { reachTiles++; if (Math.Abs(c.a - 0.5f) > 1e-4 || Math.Abs(s2 - sat) > 1e-3) badReach++; }
                }
                bool overlays = moveTiles > 0 && reachTiles > 0 && badMove == 0 && badReach == 0 && sat > 0f;
                r.Add("enemy tank shown: " + moveTiles + " move tiles (" + badMove + " not at half opacity and half saturation), " + reachTiles + " reach tiles (" + badReach + " not at half opacity, full colour)");
                SupplyOverlay.HideEnemy();
                // Capture: one Blue turn on each city.
                Building neutral = Rig.At(-2, 3).building, theirs = Rig.At(3, 3).building;
                string w0, v0; Player p0;
                bool fullNone = !theirs.ShownCaptureProgress(out w0, out v0, out p0);
                endTurn();
                string w1, v1, w2, v2; Player p1, p2;
                bool n1 = neutral.ShownCaptureProgress(out w1, out v1, out p1), n2 = theirs.ShownCaptureProgress(out w2, out v2, out p2);
                bool capture = fullNone && n1 && w1 == "Capturing" && v1 == "Blue 6/10" && p1 == a && n2 && w2 == "Held" && v2 == "4/10" && p2 == o;
                r.Add("capture line: Red city at full points shows none " + fullNone + "; neutral city " + w1 + ": " + v1 + "; Red city " + w2 + ": " + v2);
                string top = TurnBanner.Line(o, 2, true) + " | " + TurnBanner.Line(a, 2, false) + " | " + TurnBanner.Line(a, 2, false, true);
                bool line = top == "Red \u2014 your turn, day 3 | Blue is moving | Blue is thinking";
                r.Add("top line: " + top);
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
                    foreach (string meta in Directory.GetFiles(Path.Combine(root, "Tiles"), "*.asset.meta"))
                        if (File.ReadAllText(meta).Contains("guid: " + g.Groups[1].Value)) sceneTerrain.Add(Path.GetFileName(meta).Replace(".asset.meta", ""));
                }
                LevelEditor was = LevelEditor.singleton;
                LevelEditor ed = Rig.New<LevelEditor>();
                ed.allTerrain = new List<TerrainData>();
                foreach (string t in sceneTerrain) ed.allTerrain.Add(ScenarioFile.Terrain(t));
                ed.allBuilding = new List<Building>();
                foreach (string f in Directory.GetFiles(Path.Combine(root, "Buildings", "Prefabs"), "*.prefab"))
                    try { ed.allBuilding.Add(PrefabLoader.LoadBuilding(Path.GetFileNameWithoutExtension(f))); } catch (Exception) { }
                ed.allUnit = new List<Unit>();
                LevelEditor.singleton = ed;
                try {
                    // What a tile needs to be loaded into headless: its pictures, overlay and roads.
                    HexTileUIData ui = HexTileUIData.instance;
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
                HexTileUIData ui = HexTileUIData.instance;
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
            case "fogGame": {
                int actions = 0, broken = 0, blindShots = 0, bumps = 0, shots = 0;
                string firstBroken = null;
                System.Action<Unit, Unit, HexTile> ran = (mover, hid, at) => bumps++;
                System.Action<Unit, Unit, bool> shot = (att, def, surprise) => { shots++; if (!surprise && !FogOfWar.Sees(att.owner, def)) blindShots++; };
                System.Action<Unit, HexTactica.AI.AIAction, bool> check = (u, act, did) => {
                    actions++;
                    string why = null;
                    if (FogOfWar.current != null) why = "left concealed";
                    foreach (Unit x in Rig.uc.units) {
                        if (x == null || x.Strength <= 0) continue;
                        if (x.tile == null) { why = x.name + " off the board"; break; }
                        if (x.trasnporter == null && x.tile.occupier != x) { why = x.name + " not on its tile"; break; }
                    }
                    foreach (HexTile t in Rig.hg.tiles) if (t.occupier != null && t.occupier.tile != t) { why = "tile holds a unit that is elsewhere"; break; }
                    if (why != null) { broken++; if (firstBroken == null) firstBroken = why; }
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
                for (int i = 0; i < 10; i++) bridge.RegisterCombat();
                bool fell = bridge.terrain.name == "Water" && !bridge.road && bridge.occupier == null && onBridge.Strength <= 0 && !a.units.Contains(onBridge);
                r.Add("bridge: " + bridge.terrain.name + ", road " + bridge.road + ", grunt lost " + (onBridge.Strength <= 0) + " " + fell);
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
                city.Repair(1);
                int m1 = a.money; gc.EndTurn(); gc.EndTurn(); int earnedAgain = a.money - m1;
                bool back = !city.Destroyed && earnedAgain == hqIncome + city.income;
                r.Add("city: ground untouched, destroyed " + destroyed + ", turn's income " + earned + " (the HQ's " + hqIncome + "), no supply or production " + stopped + "; repaired by 1: income " + earnedAgain + " " + back);
                // Water takes nothing.
                HexTile w = bridge;
                w.RegisterCombat();
                bool water = w.terrain.name == "Water" && w.damage == 0;
                r.Add("water takes no damage " + water);
                detail = string.Join("; ", r.ToArray());
                return parts && hit && badlands && undo && repaired && fell && stopped && back && water;
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
                // The ship beside the shore on open water: a grunt cannot board; at the dock it can.
                Building yard = Rig.At(-2, 0).building;
                HexTile dock = yard.runway.Count > 0 ? yard.runway[0] : null;
                HexTile beach = null, shore = null;
                foreach (HexTile t in HexGenerator.hg.tiles) {
                    if (t.terrain.elevation >= 0 || t.building != null || t.occupier != null) continue;
                    foreach (HexTile n in t.neighbors) if (n != null && n.terrain.elevation >= 0 && n.occupier == null && n.building == null && t.Distance(Rig.At(-2, 0)) > 3) { beach = t; shore = n; break; }
                    if (beach != null) break;
                }
                ship.TeleportTo(beach, 0); g1.TeleportTo(shore, 0);
                bool d1 = !Transporting.CanBoardFrom(ship, g1);
                HexTile quay = null;
                foreach (HexTile n in dock.neighbors) if (n != null && n.terrain.elevation >= 0 && n.occupier == null && n.building == null) { quay = n; break; }
                ship.TeleportTo(dock, 0); g1.TeleportTo(quay, 0);
                bool d2 = Transporting.CanBoardFrom(ship, g1);
                r.Add("grunt boards at sea " + !d1 + ", at the dock " + d2);
                apc.TeleportTo(quay == null ? null : quay, 0);
                g1.TeleportTo(Rig.At(-6, 2), 0);
                bool e1 = Transporting.Load(ship, apc);
                HexTile sea = Rig.At(4, 1);
                ship.TeleportTo(sea, 0);
                bool e2 = apc.tile == sea && sup.tile == sea && Transporting.FreeWeight(ship) == 1;
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
                foreach (HexTile t in ap.ServiceTiles()) if (t != runway && t.occupier == null && other == null) other = t;
                g.TeleportTo(other, 3, true);
                landed += "; move and attack: fuel " + g.fuel + ", ammo " + g.ammo;
                ok1 = ok1 && g.fuel == 2 && g.ammo == 1;
                ap.owner = l.other;
                Unit.Ditched += hook;
                try { endTurn(); endTurn(); } finally { Unit.Ditched -= hook; }
                detail = landed + "; airport lost, next turn fuel " + f.fuel + (ditched || f.tile == null ? ", ditched" : ", flying");
                return ok1 && !ditched && f.tile != null && f.fuel == f.fuelMax - f.minFuelPerTurn;
            }
            case "attritionTanker": {
                Unit f = Rig.At(0, 0).occupier, t = Rig.At(1, 0).occupier;
                endTurn(); endTurn();
                string first = "fighter fuel " + f.fuel + ", tanker refuels left " + t.refuelStock;
                bool ok1 = f.fuel == f.fuelMax && t.refuelStock == 0;
                endTurn(); endTurn();
                detail = "after a turn " + first + "; after two fighter fuel " + f.fuel;
                return ok1 && f.fuel == f.fuelMax - f.minFuelPerTurn;
            }
            case "attritionShip": {
                Unit away = Rig.At(1, 2).occupier, dock = Rig.At(-1, 0).occupier;
                int before = away.fuel;
                endTurn(); endTurn();
                int idle = away.fuel;
                away.TeleportTo(Rig.At(1, 5), 3);
                endTurn(); endTurn();
                detail = "away from port: fuel " + before + " -> " + idle + " idle, " + away.fuel + " after 3 tiles, strength " + away.Strength + "; on the dock: strength " + dock.Strength + ", fuel " + dock.fuel;
                return idle == before && away.fuel == before - 3 && away.Strength == 6 && dock.Strength == 5 && dock.fuel == dock.fuelMax;
            }
            case "runwayNoCapture": {
                Building ap = Rig.At(-3, 2).building;
                endTurn(); endTurn();
                detail = "Red grunt on a runway tile: " + show(ap);
                return ap.owner == a && ap.capturePoints == ap.CaptureThreshold;
            }
            case "attritionBoats": {
                Unit g = Rig.At(3, 0).occupier, far = Rig.At(1, 0).occupier;
                SupplyNetwork.Result with = SupplyNetwork.Solve(a, false);
                bool withBoats = with.IsSupplied(g.tile);
                int linked = with.connectedHubs.Count;
                far.TeleportTo(Rig.At(0, 6), 0);
                SupplyNetwork.Result without = SupplyNetwork.Solve(a, false);
                bool withoutFar = without.IsSupplied(g.tile);
                bool yardSupplied = with.IsSupplied(Rig.At(-3, 0));
                detail = "naval yard in supply " + yardSupplied + "; island grunt supplied with both boats " + withBoats + ", without the far one " + withoutFar
                         + "; boats linked " + with.connectedHubs.Count;
                return yardSupplied && withBoats && !withoutFar && linked == 2;
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
                bool ok1 = l1 && l2 && g.Strength == 5 && m.supplies == 2 && m.Strength == 6;
                ship.supplies = 0;
                endTurn(); endTurn();
                detail = "after a turn " + first + "; with the ship's supplies gone, ship cargo supplies " + m.supplies;
                return ok1 && m.supplies == 1;
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
                    "Artillery", "Grunt", "Tank", "Supplies", "Grunt" },
            new[] { "Tank", "Grunt", "Tank", "Grunt" }, "sixteen units a side, three supply units");

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
                     "Our army stands beyond HQ supply with the enemy close. The supply unit should bring supply forward without ever ending our turn where an enemy can attack it."),
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
                     "Our tank is next to the enemy supply unit (25,000) that sustains four grunts, and next to a grunt that can only do 1 damage to it. It should hit the supply unit."),
                ai, other, ai);
            n++;
        }
        // ---- air and sea (attrition, Red passive: these test the AI's own logistics).
        Action<string, string, Action<Player, Player>, int, string> air = (name, check, setup, rounds, question) => {
            Blank(7);
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, -6, 0); Put("HQ", other, 6, 0); Put("Grunt", other, 5, 1);
            setup(ai, other);
            Dictionary<string, string> test = Test(name, "behaviour", "Human", rounds, check, question);
            test["rules"] = "attrition";
            test["purchases"] = "on";   // the build-on-reserved check buys at the airport
            ScenarioFile.Save(Path.Combine(Dir, name + ".dat"), test, ai, other, ai);
            n++;
        };
        Action<int, int> sea = (x0, x1) => { for (int x = x0; x <= x1; x++) for (int y = -7; y <= 7; y++) { HexTile t = Rig.At(x, y); if (t != null) t.terrain = T("Water"); } };
        air("fighter-comes-home", "fighterHome", (a, o) => { Rig.PlaceBuilding("Airport", a, -4, 2); Unit f = Put("Fighter", a, 2, -2); f.fuel = 11; Put("Grunt", a, -5, 0); }, 4,
            "A Fighter 6 tiles from its airport with 11 fuel (7 a turn's flying, 3 burnt each turn away): it has to come home now, and should never be ditched.");
        air("keep-the-runway-clear", "runwayClear", (a, o) => { Rig.PlaceBuilding("Airport", a, -4, 2); Unit f = Put("Fighter", a, 0, 0); f.fuel = 8; Put("Grunt", a, -4, 3); Put("Grunt", a, -5, 3); Put("Grunt", a, -3, 3); }, 1,
            "Our grunts are parked on all three runway tiles and a Fighter low on fuel needs one: the grunts should leave the runway.");
        air("reserve-the-runway", "runwayReserve", (a, o) => { Rig.PlaceBuilding("Airport", a, -4, 2); Unit f1 = Put("Fighter", a, -1, 2); f1.fuel = 10; Unit f2 = Put("Fighter", a, -1, 3); f2.fuel = 10; Put("Grunt", a, -5, 0); }, 1,
            "An airfield cut down to one landing tile and two Fighters that could each stay up three more turns: the first reserves the tile for its third turn, so the second takes it a turn earlier (a reservation holds one turn). Nobody needs it this turn, so it stays empty.");
        air("lift-off-for-landing", "liftOff", (a, o) => { Rig.PlaceBuilding("Airport", a, -4, 2); Put("Fighter", a, -4, 2); Unit f2 = Put("Fighter", a, -1, 2); f2.fuel = 4; Put("Grunt", a, -5, 0); }, 1,
            "One landing tile, a full Fighter parked on it and another 3 tiles out with 4 fuel that must land this turn: the parked one lifts off, the other lands (acting last), nobody is ditched.");
        air("no-build-on-reserved", "noBuildReserved", (a, o) => { Rig.PlaceBuilding("Airport", a, -4, 2); Unit f2 = Put("Fighter", a, -1, 2); f2.fuel = 4; Put("Grunt", a, -5, 0); }, 1,
            "One landing tile reserved for a Fighter landing this turn: the AI may not build a unit on it.");
        air("tanker-meets-fighter", "tankerMeets", (a, o) => { Rig.PlaceBuilding("Airport", a, -4, 2); Unit f = Put("Fighter", a, 1, -1); f.fuel = 9; Put("AirTanker", a, -3, 0); Put("Grunt", a, -5, 0); }, 1,
            "An air tanker (3 supplies) and a Fighter that is short of fuel: the tanker should end its turn beside the fighter.");
        air("boats-supply-the-island", "boatsIsland", (a, o) => { sea(-2, 1); Rig.PlaceBuilding("NavalYard", a, -3, 0); Put("SupplyBoat", a, -2, 0); Put("SupplyBoat", a, -2, -1); Put("Grunt", a, 3, 0); Put("Grunt", a, -4, 1); }, 3,
            "A Blue grunt stranded on an island across a four-tile strait; two supply boats at the naval yard's docks. Within three turns the boats should link up and supply the island.");

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
            Put("Grunt", ai, road[1].x, road[1].y); Put("Grunt", ai, road[2].x, road[2].y); Put("APC", ai, road[3].x, road[3].y + 1);
            Put("Supplies", ai, road[4].x, road[4].y); Put("Supplies", ai, road[7].x, road[7].y); Put("Supplies", ai, road[10].x, road[10].y); Put("Tank", ai, road[12].x, road[12].y);
            HexTile end = road[road.Count - 1];
            Put("HQ", other, end.x, end.y); Put("Grunt", other, road[road.Count - 2].x, road[road.Count - 2].y);
            Dictionary<string, string> test = Test("grunts-ride-the-apc", "behaviour", "Human", 2, "combatRide",
                "The winding road again: Blue's two grunts at home with an empty APC beside them (grunts move 3, the APC 6 and takes both); three supply units already carry supply up the road to a tank at the far end of it, the enemy beyond. With rides for fighting units on (combatRides), over two rounds the grunts should ride the APC: the farthest grunt ends further up the road than walking could take it (6 steps), or aboard.");
            test["rules"] = "attrition";
            ScenarioFile.Save(Path.Combine(Dir, "grunts-ride-the-apc.dat"), test, ai, other, ai);
            n++;
        }
        {
            List<HexTile> road = windingRoad();
            Player ai = Rig.AddPlayer("Blue", ScenarioFile.AI_ID), other = Rig.AddPlayer("Red", ScenarioFile.OTHER_ID);
            Put("HQ", ai, road[0].x, road[0].y);
            Put("APC", ai, road[1].x, road[1].y);
            Put("Supplies", ai, road[4].x, road[4].y); Put("Supplies", ai, road[7].x, road[7].y); Put("Supplies", ai, road[10].x, road[10].y); Put("Tank", ai, road[12].x, road[12].y);
            HexTile end = road[road.Count - 1];
            Put("HQ", other, end.x, end.y); Put("Grunt", other, road[road.Count - 2].x, road[road.Count - 2].y);
            Dictionary<string, string> test = Test("drop-and-advance", "behaviour", "Human", 1, "dropAndAdvance",
                "The winding road with supply carried up it, as in grunts-ride-the-apc, but the grunt starts the turn aboard the APC at home (it has not acted). Getting off costs nothing, so in one turn the APC should drive it up the road and put it down, and the grunt should then make its own move from there: it ends off the APC, two or more tiles from it.");
            test["rules"] = "attrition";
            ScenarioFile.Save(Path.Combine(Dir, "drop-and-advance.dat"), test, ai, other, ai);
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
                foreach (HexTile t in ap.runway) if (t.occupier != null && !AIAir.NeedsBase(t.occupier)) blocked++;
                Unit f = First(l.ai, "Fighter");
                detail = blocked + " runway tiles still blocked by grunts; fighter at " + (f != null && f.tile != null ? Pos(f.tile) + " fuel " + f.fuel : "gone");
                return blocked < ap.runway.Count;
            }
            case "runwayReserve": {
                Building ap = Rig.At(-4, 2).building;
                // One landing tile: the anchor; the rest of the airfield is taken out for the test.
                foreach (HexTile t in new List<HexTile>(ap.ServiceTiles())) if (t != ap.tile) { t.building = null; ap.tiles.Remove(t); ap.runway.Remove(t); }
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
            case "dropAndAdvance": {
                Unit apc = First(l.ai, "APC");
                Unit g = Put("Grunt", l.ai, apc.tile.neighbors.Find(t => t != null && t.occupier == null && t.terrain.elevation >= 0).x, apc.tile.neighbors.Find(t => t != null && t.occupier == null && t.terrain.elevation >= 0).y);
                Transporting.Load(apc, g); g.FinishBoarding();
                g.active = true;   // aboard at the start of the turn: it has not acted
                HexTile home = GameController.HQOf(l.ai).tile;
                Dictionary<HexTile, int> fromHome = new AIMap(l.ai, l.ai.GetComponent<AIPlayer>().Profile).LandSteps(home);
                Func<HexTile, int> hs = t => { int v; return t != null && fromHome.TryGetValue(t, out v) ? v : -1; };
                string apcFrom = Pos(apc.tile);
                ScenarioFile.PlayTurn();
                bool off = g.trasnporter == null && g.tile != null;
                int apart = off && apc.tile != null ? g.tile.Distance(apc.tile) : 0;
                detail = "APC " + apcFrom + " -> " + Pos(apc.tile) + " (" + hs(apc.tile) + " steps from home); grunt " + (off ? "off at " + Pos(g.tile) + " (" + hs(g.tile) + " steps), " + apart + " tiles from the APC" : "still aboard");
                return off && apart >= 2;
            }
            case "combatRide": {
                if (Environment.GetEnvironmentVariable("SET") == null) l.ai.GetComponent<AIPlayer>().Profile.combatRides = true;
                HexTile home = GameController.HQOf(l.ai).tile;
                Dictionary<HexTile, int> fromHome = new AIMap(l.ai, l.ai.GetComponent<AIPlayer>().Profile).LandSteps(home);
                Func<HexTile, int> hs = t => { int v; return t != null && fromHome.TryGetValue(t, out v) ? v : -1; };
                List<Unit> grunts = new List<Unit>();
                foreach (Unit u in l.ai.units) if (u != null && GameController.UnitName(u) == "Grunt") grunts.Add(u);
                int start = 0; foreach (Unit g in grunts) start = Math.Max(start, hs(g.tile));
                List<string> trail = new List<string>(); bool aboard = false;
                for (int r = 0; r < rounds * 2 && !gc.gameOver; r++) {
                    bool blue = gc.activePlayer == l.ai;
                    ScenarioFile.PlayTurn();
                    if (!blue) continue;
                    List<string> at = new List<string>();
                    foreach (Unit g in grunts) { if (g.trasnporter != null) aboard = true; at.Add(hs(g.tile) + (g.trasnporter != null ? " aboard" : "")); }
                    trail.Add(string.Join(", ", at.ToArray()));
                }
                int far = 0; foreach (Unit g in grunts) far = Math.Max(far, hs(g.tile));
                detail = "grunts' steps from home after each Blue turn (start " + start + "): " + string.Join(" | ", trail.ToArray());
                return far > start + 6 || aboard;
            }
            case "buySupply": {
                l.ai.money = 25000;
                int before = 0;
                foreach (Unit u in l.ai.units) if (u != null && u.GetComponent<SupplyPoint>() != null && !u.GetComponent<SupplyPoint>().producer) before++;
                ScenarioFile.PlayTurn();
                int after = 0; Unit made = null;
                foreach (Unit u in l.ai.units) if (u != null && u.tile != null && u.GetComponent<SupplyPoint>() != null && !u.GetComponent<SupplyPoint>().producer) { after++; if (u.tile == GameController.HQOf(l.ai).tile) made = u; }
                detail = "supply units " + before + " -> " + after + ", money " + l.ai.money + (made != null ? ", new one on the HQ" : "");
                return after == before + 1 && made != null && l.ai.money == 0;
            }
            case "liftOff": {
                Building ap = Rig.At(-4, 2).building;
                foreach (HexTile t in new List<HexTile>(ap.ServiceTiles())) if (t != ap.tile) { t.building = null; ap.tiles.Remove(t); ap.runway.Remove(t); }
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
                foreach (HexTile t in new List<HexTile>(ap.ServiceTiles())) if (t != ap.tile) { t.building = null; ap.tiles.Remove(t); ap.runway.Remove(t); }
                // Leave only the anchor as a footprint tile too, so the reserved tile is the only place to build.
                foreach (HexTile t in new List<HexTile>(ap.tiles)) if (t != ap.tile) { t.building = null; ap.tiles.Remove(t); }
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
                Unit g = Rig.At(3, 0).occupier;
                List<string> trail = new List<string>();
                bool ok = false;
                for (int r = 0; r < rounds * 2 && !gc.gameOver; r++) {
                    bool blue = gc.activePlayer == l.ai;
                    ScenarioFile.PlayTurn();
                    if (!blue) continue;
                    SupplyNetwork.Result res = SupplyNetwork.Solve(l.ai, false);
                    string boats = "";
                    foreach (Unit u in l.ai.units) if (u != null && u.name == "SupplyBoat" && u.tile != null) boats += " " + Pos(u.tile) + (res.connectedHubs.Contains(u.GetComponent<SupplyPoint>()) ? "+" : "-");
                    bool sup = g.tile != null && res.IsSupplied(g.tile);
                    trail.Add("boats" + boats + (sup ? ", island supplied" : ""));
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
        foreach (string f in Files(only == null || only == "behaviour" ? "behaviour" : "-")) {
            ScenarioFile.Loaded l = ScenarioFile.Load(f, null, tweak);
            string detail;
            bool ok = Judge(l, out detail);
            if (ok) passed++; else failed++;
            Console.WriteLine(string.Format("  {0}  {1,-24} {2}", ok ? "PASS" : "FAIL", l.meta.Name, detail));
        }
        Console.WriteLine();
        Console.WriteLine("== Building rules ==");
        foreach (string f in Files(only == null || only == "rules" ? "rules" : "-")) {
            ScenarioFile.Loaded l = ScenarioFile.Load(f, null, tweak);
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
        ScenarioFile.Loaded l = ScenarioFile.Load(path, "Full", tweak, tweak);
        GameController gc = Rig.gc;
        AIPerf.Enabled = Environment.GetEnvironmentVariable("SECTIONS") != null;
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
            if (AIPerf.Enabled) { Console.WriteLine(AIPerf.Report()); AIPerf.Reset(); }
        }
    }

    /// <summary>One scenario, narrated: the board after every turn and every decision's reasoning.</summary>
    public static void Narrate(string name, int rounds, Action<AIProfile> tweak) {
        ScenarioFile.Loaded l = ScenarioFile.Load(Find(name), Environment.GetEnvironmentVariable("OPPONENT"), tweak);
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
        if (root != null) HexTactica.Harness.GameLog.Start(l.path, "Full", root);
        GameController gc = Rig.gc;
        Console.WriteLine("duel on " + name + ": Blue " + (Environment.GetEnvironmentVariable("SET_BLUE") ?? "defaults")
                          + " | Red " + (Environment.GetEnvironmentVariable("SET_RED") ?? "defaults"));
        int startBlue = l.ai.units.Count, startRed = l.other.units.Count;
        int ditchBlue = 0, ditchRed = 0; int sorties = 0, landings = 0;
        Action<Unit> dh = u => { if (u.owner == l.ai) ditchBlue++; else ditchRed++; };
        Unit.Ditched += dh;
        int ranBlue = 0, ranRed = 0, hiddenShotsBlue = 0, hiddenShotsRed = 0;
        Action<Unit, Unit, HexTile> rh = (m, h, t) => { if (m.owner == l.ai) ranBlue++; else ranRed++; };
        // A shot from where the side fired on did not see the firer (an ambush or a shot from the fog).
        Action<Unit, Unit, bool> sh = (att, def, surprise) => { if (gc.fogOfWar && (surprise || !FogOfWar.Sees(def.owner, att))) { if (att.owner == l.ai) hiddenShotsBlue++; else hiddenShotsRed++; } };
        Unit.RanInto += rh; Unit.Attacking += sh;
        for (int turn = 0; turn < rounds * 2 && !gc.gameOver; turn++) {
            ScenarioFile.PlayTurn();
            if (Environment.GetEnvironmentVariable("BOARD") != null) {
                List<string> b = new List<string>();
                foreach (Player p in new[] { l.ai, l.other }) foreach (Unit u in p.units) if (u != null && u.tile != null)
                    b.Add(p.personal_name + ":" + u.name.Replace("(Clone)", "") + "@" + u.tile.x + "," + u.tile.y + " s" + u.Strength + " f" + u.fuel + " a" + u.ammo + " p" + u.supplies);
                b.Sort(string.CompareOrdinal);
                Console.WriteLine("  board after turn " + turn + ": " + string.Join(" | ", b.ToArray()));
            }
            if (gc.activePlayer == l.ai || gc.gameOver)
                Console.WriteLine(string.Format("  round {0,2}: Blue {1,2} units, material {2,6} | Red {3,2} units, material {4,6}",
                    gc.days, Alive(l.ai), ScenarioFile.Material(l.ai), Alive(l.other), ScenarioFile.Material(l.other)));
        }
        Unit.Ditched -= dh; Unit.RanInto -= rh; Unit.Attacking -= sh;
        if (gc.fogOfWar) Console.WriteLine("fog of war: ran into unseen units Blue " + ranBlue + " Red " + ranRed + "; shots fired unseen Blue " + hiddenShotsBlue + " Red " + hiddenShotsRed);
        int airBlue = 0, airRed = 0;
        foreach (Unit u in l.ai.units) if (u != null && u.tile != null && LocomotionRules.IsAirborne(u.locomotion)) airBlue++;
        foreach (Unit u in l.other.units) if (u != null && u.tile != null && LocomotionRules.IsAirborne(u.locomotion)) airRed++;
        Console.WriteLine("aircraft: ditched Blue " + ditchBlue + " Red " + ditchRed + "; still flying Blue " + airBlue + " Red " + airRed);
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
                if (!gc.gameOver && gc.activePlayer != mover && eye.ContainsKey(mover)) {
                    predicted.Clear(); before.Clear();
                    AIPlayer ai = mover.GetComponent<AIPlayer>();
                    AIProfile prof = ai != null ? ai.Profile : ScriptableObject.CreateInstance<AIProfile>();
                    using (FogOfWar.Concealing(prof.fogFair ? mover : null, true, prof.fogMemoryTurns)) {
                        AIMap m = new AIMap(mover, prof);
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
                    if (detailRound != null && detailRound == gc.days.ToString()) {
                        List<string> hs = new List<string>(), cs = new List<string>();
                        foreach (Unit u in p.units) {
                            if (u == null || u.tile == null || u.Strength <= 0) continue;
                            string at = "(" + u.tile.x + "," + u.tile.y + ")";
                            if (u.Hub != null && !u.Hub.producer) hs.Add(u.name.Replace(" Variant", "") + at + (u.trasnporter != null ? " aboard " + u.trasnporter.name : "") + (net.connectedHubs.Contains(u.Hub) ? "" : " CUT") + " hq" + (hq != null ? hq.Distance(u.tile) : 99) + " e" + toEnemy(u.tile));
                            else if (u.Hub == null && u.Firepower > 0 && toEnemy(u.tile) <= 6 && !LocomotionRules.NeedsBase(u.locomotion) && net.DepthAt(u.tile) <= 0) cs.Add(u.name.Replace(" Variant", "") + at + " s" + u.Strength + " p" + u.supplies + " a" + u.ammo + " hq" + (hq != null ? hq.Distance(u.tile) : 99));
                        }
                        Console.WriteLine("    " + p.personal_name + " supply units: " + string.Join("; ", hs.ToArray()));
                        Console.WriteLine("    " + p.personal_name + " engaged and cut off: " + string.Join("; ", cs.ToArray()));
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

    /// <summary>PROBE=tree FILE=&lt;board&gt; [SIDE=Red]: the supply tree that side would plan on that board, link by link, and the supply around each fighting unit left out.</summary>
    public static void Tree(string name, string side) {
        ScenarioFile.Loaded l = ScenarioFile.Load(Find(name), "Full", null, null);
        Player p = side == "Red" ? l.other : l.ai;
        AIProfile prof = p.GetComponent<AIPlayer>().Profile;
        AIMap m = new AIMap(p, prof);
        AISupplyTree.DebugLog = Console.WriteLine;
        AISupplyTree.Plan plan;
        try { plan = AISupplyTree.Make(m, null); } finally { AISupplyTree.DebugLog = null; }
        Console.WriteLine(plan);
        SupplyNetwork.Result net = SupplyNetwork.SolvePlanned(p, plan.links);
        foreach (Unit u in m.ownUnits) {
            if (u == null || u.tile == null || u.Hub != null || u.Firepower <= 0 || net.DepthAt(u.tile) > 0) continue;
            List<string> around = new List<string>();
            foreach (HexTile n in u.tile.neighbors) if (n != null) around.Add(Pos(n) + n.terrain.name.Substring(0, 3) + "d" + net.DepthAt(n) + (n.occupier != null ? "[" + n.occupier.name.Substring(0, 3) + (n.occupier.owner == p ? "" : "!") + "]" : ""));
            Console.WriteLine("  out: " + u.name + Pos(u.tile) + " on " + u.tile.terrain.name + " | " + string.Join(" ", around.ToArray()));
        }
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
        foreach (string fog in (Environment.GetEnvironmentVariable("FOGS") ?? "off,on").Split(',')) {
            foreach (string map in maps) {
                float[] lead = new float[2];
                for (int k = 0; k < 2; k++) {
                    ScenarioFile.Loaded l = ScenarioFile.Load(Find(map), "Full", k == 0 ? challenger : champion, champion);
                    Rig.gc.fogOfWar = fog == "on"; FogOfWar.Reset();
                    GameController gc = Rig.gc;
                    for (int t = 0; t < rounds * 2 && !gc.gameOver; t++) ScenarioFile.PlayTurn();
                    lead[k] = ScenarioFile.Material(l.ai) - ScenarioFile.Material(l.other);
                    if (gc.winner == l.ai) lead[k] += 100000f; else if (gc.winner == l.other) lead[k] -= 100000f;
                }
                float d = lead[0] - lead[1];
                total += d; if (d > 0) better++; else if (d < 0) worse++;
                Console.WriteLine(string.Format("  fog {0,-3} {1,-44} challenger lead {2,8:0}, champion's {3,8:0}: {4,8:+0;-0}", fog, Path.GetFileNameWithoutExtension(map), lead[0], lead[1], d));
            }
        }
        Console.WriteLine(string.Format("  total {0:+0;-0}: better in {1}, worse in {2}", total, better, worse));
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
