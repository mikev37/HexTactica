using System;
using UnityEngine;
using HexTactica.AI;
using HexTactica.Testing;

/// <summary>Prints the magnitude of each scoring term for a real-prefab position. Diagnostic only.</summary>
public static class Probe {
    public static void Run() {
        Rig.NewGame(10);
        Player a = Rig.AddPlayer("A", 1), b = Rig.AddPlayer("B", 2);
        Unit hq = Rig.Spawn(PrefabLoader.Load("TempHQ"), a, -8, 0);
        PrefabLoader.AttachHubIfAny("TempHQ", hq);
        Unit g = Rig.Spawn(PrefabLoader.Load("Grunt"), a, -2, 0);
        Rig.Spawn(PrefabLoader.Load("Tank"), b, 2, 0);
        Rig.Spawn(PrefabLoader.Load("Artillery"), b, 4, 1);
        AIProfile p = ScriptableObject.CreateInstance<AIProfile>();
        p.annotateBoard = false;
        AIMap m = new AIMap(a, p);
        HexTile t = Rig.At(0, 0);
        AttackAppraisal f = AITargeting.BestFrom(g, t, m, p, Posture.Aggressive, false);
        float threat = m.ThreatTo(g, t);
        Console.WriteLine("grunt value (gold)            = " + g.value());
        Console.WriteLine("future attack term (gold)     = " + (f.valid ? f.value : 0));
        Console.WriteLine("risk term: threat * riskWeight = " + threat * p.riskWeight);
        Console.WriteLine("cover term: cover * coverWeight= " + m.UsableCover(g, t) * p.coverWeight);
        Console.WriteLine("hold term: conflict * hold    = " + m.Conflict(t) * p.holdWeight);
        Console.WriteLine("what risk SHOULD cost (gold)  = " + Math.Min(6f, threat) * g.value());
    }
}

public static class Trace {
    public static void Transport() {
        AIProfile profile = ScriptableObject.CreateInstance<AIProfile>();
        profile.annotateBoard = false;
        Rig.NewGame(14);
        Player a = Rig.AddPlayer("A", 1), b = Rig.AddPlayer("B", 2);
        Unit hq = Rig.Spawn(PrefabLoader.Load("TempHQ"), a, -12, 0); PrefabLoader.AttachHubIfAny("TempHQ", hq);
        Unit hub = Rig.Spawn(PrefabLoader.Load("Supplies"), a, -12, 6); SupplyPoint sp = PrefabLoader.AttachHubIfAny("Supplies", hub);
        Rig.Spawn(Archetypes.Helicopter(), a, -11, 6);
        Rig.Spawn(PrefabLoader.Load("Grunt"), a, -2, -1);
        Rig.Spawn(PrefabLoader.Load("Grunt"), a, -2, 0);
        Rig.Spawn(PrefabLoader.Load("Grunt"), a, -3, 1);
        Rig.Spawn(PrefabLoader.Load("Tank"), a, -2, 1);
        Rig.Spawn(PrefabLoader.Load("Grunt"), b, 13, -6);
        AIMap map = new AIMap(a, profile);
        foreach (int[] c in new[]{ new[]{-5,0}, new[]{-6,0}, new[]{-6,1}, new[]{-12,6} }) {
            HexTile t = Rig.At(c[0], c[1]);
            Console.WriteLine("(" + c[0] + "," + c[1] + ") depth=" + map.SupplyDepth(t)
                + " exact=" + AISupplyPlanner.ExactValue(sp, t, map, profile).ToString("0")
                + " proxy=" + AISupplyPlanner.ProxyValue(sp, t, map).ToString("0.00")
                + " walkReach=" + map.ReachHorizon(hub).Get(t));
        }
        foreach (Unit u in a.units) Console.WriteLine("  " + u.name + " at (" + u.tile.x + "," + u.tile.y + ") depth=" + map.SupplyDepth(u.tile));
        TransportPlan plan = AISupplyPlanner.PlanHub(sp, map, profile);
        Console.WriteLine("plan: " + (plan == null ? "null" : plan + " value=" + plan.value.ToString("0")));
        HexTile good = Rig.At(-5, 0);
        System.Collections.Generic.List<HexTile> only = new System.Collections.Generic.List<HexTile> { good };
        TransportPlan solo = AITransportPlanner.Plan(hub, map, profile, t => 179f, only);
        Console.WriteLine("solo plan to (-5,0): " + (solo == null ? "null" : solo + " score=" + solo.score.ToString("0")));
        // shortlist as PlanHub builds it
        var reach = map.ReachHorizon(hub);
        var cands = new System.Collections.Generic.List<HexTile>();
        foreach (HexTile t in reach.Tiles) if (AIPath.CanFinishTurnOn(hub, t)) cands.Add(t);
        Console.WriteLine("candidates=" + cands.Count + " contains(-5,0)=" + cands.Contains(good) + " canFinish=" + AIPath.CanFinishTurnOn(hub, good));
    }

    public static void Game() {
        AIProfile profile = ScriptableObject.CreateInstance<AIProfile>();
        profile.annotateBoard = false;
        int half = 7;
        Rig.NewGame(half + 2);
        Player a = Rig.AddPlayer("A", 1), b = Rig.AddPlayer("B", 2);
        foreach (Player p in new[] { a, b }) {
            int s = p == a ? -1 : 1;
            Spawn("TempHQ", p, s * half, 0);
            Spawn("Supplies", p, s * (half - 1), s);
            Spawn("Grunt", p, s * (half - 1), 0);
            Spawn("Grunt", p, s * (half - 2), s);
            Spawn("Grunt", p, s * (half - 2), -s);
            Spawn("Tank", p, s * (half - 2), 0);
            Spawn("Artillery", p, s * half, -s);
        }
        AIPlayer ai = Rig.AttachAI(a, profile);
        AIPlayer ai2 = Rig.AttachAI(b, profile);
        System.Action<Unit, AIAction, bool> log = (u, act, did) =>
            Console.WriteLine("      " + u.owner.personal_name + " " + u.name + "@" + D(u.tile) + " " + act.kind
                + " score=" + act.score.ToString("0") + " did=" + did + " (" + act.reason + ")");
        ai.onDecision = log; ai2.onDecision = log;
        Rig.gc.activePlayer = a;
        for (int turn = 0; turn < 4; turn++) {
            Player act = Rig.gc.activePlayer;
            AIPlayer who = act == a ? ai : ai2;
            Console.WriteLine("--- turn " + turn + " " + act.personal_name);
            // Step manually to log each decision.
            int guard = 0;
            while (guard++ < 100) {
                var before = new System.Collections.Generic.Dictionary<Unit, HexTile>();
                foreach (Unit u in act.units) before[u] = u.tile;
                bool more = who.StepOnce(true);
                foreach (Unit u in act.units) if (before.ContainsKey(u) && before[u] != u.tile)
                    Console.WriteLine("   " + u.name + " " + D(before[u]) + " -> " + D(u.tile) + " str " + u.Strength);
                if (!more) break;
            }
            foreach (Player p in new[] { a, b }) {
                string line = "   [" + p.personal_name + "]";
                foreach (Unit u in p.units) line += " " + u.name.Substring(0, 2) + D(u.tile) + "s" + u.Strength;
                Console.WriteLine(line);
            }
        }
        // Explain one grunt's choice in detail.
        AIMap m = new AIMap(a, profile);
        foreach (Front f in AIStrategic.BuildFronts(m, profile)) Console.WriteLine("front " + f + " objectiveTile=" + D(f.objectiveTile) + " assigned=" + f.assigned.Count);
        SupplyPoint hubA = null; foreach (Unit u in a.units) { var sp = u.GetComponent<SupplyPoint>(); if (sp != null && !sp.producer) hubA = sp; }
        Console.WriteLine("hub at " + D(hubA.myUnit.tile) + " contested tiles=" + new System.Collections.Generic.List<HexTile>(m.ContestedTiles).Count);
        foreach (int[] c in new[] { new[]{-6,-1}, new[]{-4,0}, new[]{-3,-1}, new[]{-2,0} }) {
            HexTile t = Rig.At(c[0], c[1]);
            Console.WriteLine("  hub@" + D(t) + " exact=" + AISupplyPlanner.ExactValue(hubA, t, m, profile).ToString("0")
                + " proxy=" + AISupplyPlanner.ProxyValue(hubA, t, m).ToString("0") + " loss=" + m.ExpectedLoss(hubA.myUnit, t).ToString("0.0")
                + " depth=" + m.SupplyDepth(t));
        }
        Unit g = null; foreach (Unit u in a.units) if (u.name == "Grunt") { g = u; break; }
        Console.WriteLine("grunt at " + D(g.tile) + " worth " + AIScore.Worth(g));
        foreach (int dx in new[] { 0, 1, 2, 3, 4, 5 }) {
            HexTile t = Rig.At(g.tile.x + dx, g.tile.y);
            if (t == null) continue;
            AttackAppraisal f = AITargeting.BestFrom(g, t, m, profile, Posture.Aggressive, false);
            Console.WriteLine("  +" + dx + " " + D(t) + " loss=" + m.ExpectedLoss(g, t).ToString("0.0")
                + " firers=" + m.FirersAt(t) + " future=" + (f.valid ? f.value.ToString("0") : "-")
                + " hold=" + (AIScore.Hold(m.Conflict(t)) * AIScore.Worth(g) * profile.holdWeight).ToString("0")
                + " depth=" + m.SupplyDepth(t));
        }
    }
    static void Spawn(string n, Player p, int x, int y) { Unit u = Rig.Spawn(PrefabLoader.Load(n), p, x, y); PrefabLoader.AttachHubIfAny(n, u); }
    static string D(HexTile t) { return t == null ? "dead" : "(" + t.x + "," + t.y + ")"; }

    public static void Decisive() {
        AIProfile profile = ScriptableObject.CreateInstance<AIProfile>(); profile.annotateBoard = false;
        Rig.NewGame(7);
        Player a = Rig.AddPlayer("A", 1), b = Rig.AddPlayer("B", 2);
        Rig.AddBuilding(BuildingType.HQ, a, -4, 0, true, 6);
        Building bhq = Rig.AddBuilding(BuildingType.HQ, b, 4, 0, true, 6);
        for (int i = -1; i <= 1; i++) { Rig.Spawn(Archetypes.Rifle(), a, -3, i); Rig.Spawn(Archetypes.Tank(), a, -2, i); }
        Rig.MakeHub(Rig.Spawn(Archetypes.Truck(), a, -4, 1), 5);
        Rig.Spawn(Archetypes.Rifle(), b, 3, 0);
        AIPlayer ai = Rig.AttachAI(a, profile);
        var other = Rig.AttachBaseline(b, HexTactica.Harness.AIKind.Greedy);
        ai.onDecision = (u, act, did) => Console.WriteLine("      " + u.name + "@" + D(u.tile) + " " + act.kind + " " + D(act.destination)
            + " score=" + act.score.ToString("0") + " did=" + did + " (" + act.reason + ")");
        Rig.gc.activePlayer = a;
        for (int round = 0; round < 6 && !Rig.gc.gameOver; round++) {
            Console.WriteLine("--- round " + round);
            ai.PlayWholeTurn();
            if (!Rig.gc.gameOver) other.PlayWholeTurn();
            string line = "   B:"; foreach (Unit u in b.units) line += " " + u.name + D(u.tile) + "s" + u.Strength;
            Console.WriteLine(line + "  HQ owner=" + bhq.owner.personal_name + " progress=" + bhq.captureProgress);
        }
        AIMap m = new AIMap(a, profile);
        foreach (Front f in AIStrategic.BuildFronts(m, profile)) Console.WriteLine("front " + f + " objTile=" + D(f.objectiveTile));
    }

    public static void Wide() {
        AIProfile profile = ScriptableObject.CreateInstance<AIProfile>(); profile.annotateBoard = false;
        int half = 11;
        Rig.NewGame(half + 2);
        Player a = Rig.AddPlayer("A", 1), b = Rig.AddPlayer("B", 2);
        foreach (Player p in new[] { a, b }) {
            int s = p == a ? -1 : 1;
            Spawn("TempHQ", p, s * half, 0); Spawn("Supplies", p, s * (half - 1), s);
            Spawn("Grunt", p, s * (half - 1), 0); Spawn("Grunt", p, s * (half - 2), s); Spawn("Grunt", p, s * (half - 2), -s);
            Spawn("Tank", p, s * (half - 2), 0); Spawn("Artillery", p, s * half, -s);
        }
        AIPlayer ai = Rig.AttachAI(a, profile);
        var other = Rig.AttachBaseline(b, HexTactica.Harness.AIKind.Greedy);
        foreach (Unit u in a.units) if (u.name == "Tank") AITactical.DebugUnit = u;
        AITactical.DebugLog = msg => Console.WriteLine("    " + msg);
        ai.onDecision = (u, act, did) => { if (u.name == "Tank" || u.name == "Artillery") Console.WriteLine("      " + u.name + " " + act.kind
            + (act.target != null ? " target " + act.target.name + D(act.target.tile) : "") + " -> " + D(act.destination)
            + " score=" + act.score.ToString("0") + " (" + act.reason + ")"); };
        Rig.gc.activePlayer = a;
        for (int round = 0; round < 14 && !Rig.gc.gameOver; round++) {
            ai.PlayWholeTurn();
            SupplyNetwork.Result sup = SupplyNetwork.Solve(a, false);
            string line = "r" + round + " A:";
            foreach (Unit u in a.units) line += " " + u.name.Substring(0, 2) + D(u.tile) + "d" + sup.DepthAt(u.tile) + "s" + u.Strength + "p" + u.supplies;
            Console.WriteLine(line);
            if (round == 6) {
                AIMap m = new AIMap(a, profile);
                SupplyPoint hub = null; foreach (Unit u in a.units) { var sp = u.GetComponent<SupplyPoint>(); if (sp != null && !sp.producer) hub = sp; }
                Console.WriteLine("   hub@" + D(hub.myUnit.tile) + " connected=" + hub.connected + " inConnected=" + m.supply.connectedHubs.Contains(hub));
                foreach (int[] c in new[] { new[]{-3,0}, new[]{-4,0}, new[]{-4,1}, new[]{-5,0} }) {
                    HexTile t = Rig.At(c[0], c[1]);
                    Console.WriteLine("     @" + D(t) + " occ=" + (t.occupier != null ? t.occupier.name : "-")
                        + " exact=" + AISupplyPlanner.ExactValue(hub, t, m, profile).ToString("0")
                        + " proxy=" + AISupplyPlanner.ProxyValue(hub, t, m).ToString("0")
                        + " depthNow=" + m.SupplyDepth(t));
                }
                TransportPlan plan = AISupplyPlanner.PlanHub(hub, m, profile);
                Console.WriteLine("   plan: " + (plan == null ? "null" : plan.ToString() + " v=" + plan.value.ToString("0")));
            }
            if (!Rig.gc.gameOver) other.PlayWholeTurn();
        }
    }

    public static void Real(int half, bool fullIsA, int rounds) {
        AIProfile profile = ScriptableObject.CreateInstance<AIProfile>(); profile.annotateBoard = false;
        Rig.NewGame(half + 2);
        Player a = Rig.AddPlayer("A", 1), b = Rig.AddPlayer("B", 2);
        for (int x = -half; x <= half; x++) for (int y = -3; y <= 3; y++) {
            HexTile t = Rig.At(x, y);
            if (t != null && (x * 7 + y * 3) % 5 == 0 && Math.Abs(x) < half - 1) t.terrain = Terrain.forest;
        }
        foreach (Player p in new[] { a, b }) {
            int s = p == a ? -1 : 1;
            Spawn("TempHQ", p, s * half, 0); Spawn("Supplies", p, s * (half - 1), s);
            Spawn("Grunt", p, s * (half - 1), 0); Spawn("Grunt", p, s * (half - 2), s); Spawn("Grunt", p, s * (half - 2), -s);
            Spawn("Tank", p, s * (half - 2), 0); Spawn("Artillery", p, s * half, -s);
        }
        Player full = fullIsA ? a : b, greedy = fullIsA ? b : a;
        if (Environment.GetEnvironmentVariable("ADVANTAGE") != null) {
            int sg = full == a ? -1 : 1;
            Spawn("Tank", full, sg * (half - 3), sg); Spawn("Grunt", full, sg * (half - 3), -sg);
        }
        if (Environment.GetEnvironmentVariable("EXPLAIN") != null) {
            foreach (Unit u in full.units) if (u.name == Environment.GetEnvironmentVariable("EXPLAIN")) AITactical.DebugUnit = u;
            AITactical.DebugLog = msg => Console.WriteLine("    X " + msg);
            AITactical.DebugBreakdown = Environment.GetEnvironmentVariable("BREAKDOWN") != null;
        }
        AIPlayer ai = Rig.AttachAI(full, profile);
        var g = Rig.AttachBaseline(greedy, HexTactica.Harness.AIKind.Greedy);
        ai.onDecision = (u, act, did) => {
            if (act.kind == AIActionKind.Attack || act.kind == AIActionKind.MoveAndAttack || act.kind == AIActionKind.Move)
                Console.WriteLine("    FULL " + u.name + D(u.tile) + " s" + u.Strength + " " + act.kind + (act.target != null ? " -> " + act.target.name : "")
                    + " score=" + act.score.ToString("0") + " (" + act.reason + ")");
        };
        Rig.gc.activePlayer = a;
        for (int round = 0; round < rounds && !Rig.gc.gameOver; round++) {
            foreach (Player p in new[] { a, b }) {
                if (Rig.gc.gameOver) break;
                var before = new System.Collections.Generic.Dictionary<Unit, int>();
                foreach (Player q in new[] { a, b }) foreach (Unit u in q.units) before[u] = u.Strength;
                bool fullTurn = Rig.gc.activePlayer == full;
                if (!fullTurn) {
                    AIMap pm = new AIMap(full, profile);
                    float predicted = 0; string per = "";
                    foreach (Unit u in full.units) { float l = pm.ExpectedLoss(u, u.tile); predicted += l; if (l > 0) per += " " + u.name + ":" + l.ToString("0.0"); }
                    var snap = new System.Collections.Generic.Dictionary<Unit, int>();
                    foreach (Unit u in full.units) snap[u] = u.Strength;
                    g.PlayWholeTurn();
                    int actual = 0;
                    foreach (var kv in snap) actual += kv.Value - (full.units.Contains(kv.Key) ? kv.Key.Strength : 0);
                    Console.WriteLine("  CALIBRATION predicted " + predicted.ToString("0.0") + " actual " + actual + " |" + per);
                } else ai.PlayWholeTurn();
                string dmg = "";
                foreach (var kv in before) {
                    int now = kv.Key.owner.units.Contains(kv.Key) ? kv.Key.Strength : 0;
                    if (now != kv.Value) dmg += " " + (kv.Key.owner == full ? "full." : "greedy.") + kv.Key.name + " " + kv.Value + "->" + now;
                }
                Console.WriteLine("  r" + round + " after " + (p == full ? "FULL" : "greedy") + ":" + dmg);
                if (p == full) {
                    SupplyNetwork.Result sup = SupplyNetwork.Solve(full, false);
                    string line = "     FULLPOS";
                    foreach (Unit u in full.units) line += " " + u.name.Substring(0, 2) + D(u.tile) + "d" + sup.DepthAt(u.tile) + "s" + u.Strength + "p" + u.supplies;
                    Console.WriteLine(line);
                }
            }
        }
        Console.WriteLine("winner " + (Rig.gc.winner != null ? Rig.gc.winner.personal_name : "none") + " full=" + full.personal_name);
    }

    /// Plays the real-roster scenarios (equal armies at three widths, and +20% at two),
    /// both seats, for each profile in a grid. Deterministic, so one run per cell suffices.
    public static void Sweep() {
        float[] aggr = Environment.GetEnvironmentVariable("SWEEP2") != null ? new[] { 0.4f, 0.5f } : new[] { 0.5f, 0.75f, 1.0f };
        float[] cut = Environment.GetEnvironmentVariable("SWEEP2") != null ? new[] { 0.5f } : new[] { 0f, 0.5f };
        float[] support = Environment.GetEnvironmentVariable("SWEEP2") != null ? new[] { 0.03f, 0.05f } : new[] { 0f, 0.1f };
        Console.WriteLine("aggrRisk cut support | eqW eqL eqD | advW advL advD | behind");
        foreach (float ar in aggr) foreach (float c in cut) foreach (float sp in support) {
            int eqW = 0, eqL = 0, eqD = 0, adW = 0, adL = 0, adD = 0, behind = 0;
            foreach (int adv in new[] { 0, 1 }) {
                int[] widths = adv == 0 ? new[] { 7, 9, 11 } : new[] { 7, 9 };
                foreach (int w in widths) foreach (bool fullIsA in new[] { true, false }) {
                    AIProfile p = ScriptableObject.CreateInstance<AIProfile>();
                    p.annotateBoard = false; p.aggressiveRisk = ar; p.cutSupplyWeight = c; p.supportWeight = sp;
                    string res = Play(w, fullIsA, adv == 1, p, 120, out bool fullBehind);
                    if (fullBehind) behind++;
                    if (adv == 0) { if (res == "W") eqW++; else if (res == "L") eqL++; else eqD++; }
                    else { if (res == "W") adW++; else if (res == "L") adL++; else adD++; }
                }
            }
            Console.WriteLine(string.Format("{0,8} {1,3} {2,7} | {3,3} {4,3} {5,3} | {6,4} {7,4} {8,4} | {9}",
                ar, c, sp, eqW, eqL, eqD, adW, adL, adD, behind));
        }
    }

    static string Play(int half, bool fullIsA, bool advantage, AIProfile profile, int maxTurns, out bool fullBehind) {
        Rig.NewGame(half + 2);
        Player a = Rig.AddPlayer("A", 1), b = Rig.AddPlayer("B", 2);
        for (int x = -half; x <= half; x++) for (int y = -3; y <= 3; y++) {
            HexTile t = Rig.At(x, y);
            if (t != null && (x * 7 + y * 3) % 5 == 0 && Math.Abs(x) < half - 1) t.terrain = Terrain.forest;
        }
        foreach (Player pl in new[] { a, b }) {
            int s = pl == a ? -1 : 1;
            Spawn("TempHQ", pl, s * half, 0); Spawn("Supplies", pl, s * (half - 1), s);
            Spawn("Grunt", pl, s * (half - 1), 0); Spawn("Grunt", pl, s * (half - 2), s); Spawn("Grunt", pl, s * (half - 2), -s);
            Spawn("Tank", pl, s * (half - 2), 0); Spawn("Artillery", pl, s * half, -s);
        }
        Player full = fullIsA ? a : b, greedy = fullIsA ? b : a;
        if (advantage) { int sg = full == a ? -1 : 1; Spawn("Tank", full, sg * (half - 3), sg); Spawn("Grunt", full, sg * (half - 3), -sg); }
        AIPlayer ai = Rig.AttachAI(full, profile);
        var g = Rig.AttachBaseline(greedy, HexTactica.Harness.AIKind.Greedy);
        Rig.gc.activePlayer = a;
        int turn = 0;
        while (!Rig.gc.gameOver && turn++ < maxTurns) {
            if (Rig.gc.activePlayer == full) ai.PlayWholeTurn(); else g.PlayWholeTurn();
        }
        float fm = 0, gm = 0;
        foreach (Unit u in full.units) fm += u.Strength * AIScore.Worth(u);
        foreach (Unit u in greedy.units) gm += u.Strength * AIScore.Worth(u);
        fullBehind = fm < gm;
        if (Rig.gc.winner == full) return "W";
        if (Rig.gc.winner == greedy) return "L";
        return "D";
    }

    public static void BuildingSupply() {
        Rig.NewGame(10);
        Player a = Rig.AddPlayer("A", 1); Rig.AddPlayer("B", 2);
        Building c = Rig.AddBuilding(BuildingType.City, a, 0, -3, true, 6);
        Console.WriteLine("building tile=" + (c.tile != null) + " gives=" + c.HasSupplyToGive + " all=" + Building.all.Count + " owner=" + (c.owner == a));
        SupplyNetwork.Result r = SupplyNetwork.Solve(a, false);
        Console.WriteLine("depth at building=" + r.DepthAt(Rig.At(0, -3)) + " at (3,0)=" + r.DepthAt(Rig.At(3, 0)) + " tiles=" + r.depth.Count);
    }
}
