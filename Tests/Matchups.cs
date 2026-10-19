using System;
using System.Collections.Generic;
using UnityEngine;
using HexTactica.AI;
using HexTactica.Harness;
using HexTactica.Testing;

/// <summary>
/// Round-robin measurement on the real roster. Not a test: prints a results table.
///
///   PROBE=matchups bash Tests/run.sh Assets
///
/// Each cell plays three map widths, both seats, so six games; the "+20%" columns give
/// one side an extra Tank and Grunt. Deterministic, so one run per game suffices.
/// </summary>
public static class Matchups {

    public enum Kind { Full, Greedy, Legacy }

    public struct Result {
        public int wins, losses, draws, behind, games;
        public float meanRounds;
        public override string ToString() {
            return string.Format("{0}W {1}L {2}D  behind {3}  ~{4:0} rounds", wins, losses, draws, behind, meanRounds);
        }
    }

    public static int[] Widths = { 7, 9, 11 };
    /// <summary>Forest layouts; each cell plays every width on every layout.</summary>
    public static int[] Variants = { 0 };
    public static int variant;
    /// <summary>Applied to every Full AI profile; used by sweeps.</summary>
    public static Action<AIProfile> Tweak;
    public const int MaxPlayerTurns = 120;

    public static void Run() {
        Kind[] all = { Kind.Full, Kind.Legacy, Kind.Greedy };
        Console.WriteLine("Seat bias (same AI both sides; W = seat A wins):");
        foreach (Kind k in all) Console.WriteLine("  " + k + " vs " + k + ": " + SeatBias(k));
        Console.WriteLine();
        Console.WriteLine("Results for the first-named AI, both seats, widths " + string.Join("/", Array.ConvertAll(Widths, w => w.ToString())) + ":");
        foreach (Kind me in all) foreach (Kind them in all) {
            if (me == them) continue;
            Console.WriteLine(string.Format("  {0,-6} vs {1,-6}  equal: {2}   +20% for {0}: {3}   +20% for {1}: {4}",
                me, them, Cell(me, them, 0), Cell(me, them, 1), Cell(me, them, -1)));
        }
    }

    /// <summary>Summary line for one profile: what matters, in one row.</summary>
    public static string Summary() {
        Result g = Cell(Kind.Full, Kind.Greedy, 0), ga = Cell(Kind.Full, Kind.Greedy, 1);
        Result l = Cell(Kind.Full, Kind.Legacy, 0), lb = Cell(Kind.Full, Kind.Legacy, -1);
        Result m = SeatBias(Kind.Full);
        return string.Format("vsGreedy {0}/{1}/{2} +20%: {3}/{4}/{5} | vsLegacy {6}/{7}/{8} legacy+20%: {9}/{10}/{11} | mirror A{12} B{13} D{14}",
            g.wins, g.losses, g.draws, ga.wins, ga.losses, ga.draws, l.wins, l.losses, l.draws, lb.wins, lb.losses, lb.draws, m.wins, m.losses, m.draws);
    }

    public static void Sweep(string name, float[] values, Action<AIProfile, float> set) {
        foreach (float v in values) {
            Tweak = p => set(p, v);
            Console.WriteLine(string.Format("{0}={1,-5} {2}", name, v, Summary()));
        }
        Tweak = null;
    }

    /// <summary>advantage: +1 = `me` gets the extra units, -1 = `them` does, 0 = equal.</summary>
    public static Result Cell(Kind me, Kind them, int advantage) {
        Result r = new Result();
        float rounds = 0;
        foreach (int v in Variants) foreach (int w in Widths) foreach (bool meIsA in new[] { true, false }) {
            variant = v;
            Player pm, pt;
            Setup(w, meIsA ? me : them, meIsA ? them : me, out pm, out pt, advantage == 0 ? 0 : (advantage > 0) == meIsA ? 1 : 2);
            if (!meIsA) { Player tmp = pm; pm = pt; pt = tmp; }
            string winner = Play();
            r.games++;
            rounds += Rig.gc.days;
            if (Rig.gc.winner == pm) r.wins++;
            else if (Rig.gc.winner == pt) r.losses++;
            else r.draws++;
            if (Material(pm) < Material(pt)) r.behind++;
            if (Environment.GetEnvironmentVariable("VERBOSE") != null)
                Console.WriteLine(string.Format("    v" + variant + " {0} vs {1} adv{2} w{3} {4}: winner {5} rounds {6} material {7}/{8}",
                    me, them, advantage, w, meIsA ? "A" : "B", Rig.gc.winner == pm ? "me" : Rig.gc.winner == pt ? "them" : "none",
                    Rig.gc.days, Material(pm), Material(pt)));
        }
        r.meanRounds = rounds / r.games;
        return r;
    }

    static Result SeatBias(Kind k) {
        Result r = new Result();
        float rounds = 0;
        foreach (int v in Variants) foreach (int w in Widths) {
            variant = v;
            Player a, b;
            Setup(w, k, k, out a, out b, 0);
            Play();
            r.games++;
            rounds += Rig.gc.days;
            if (Rig.gc.winner == a) r.wins++;
            else if (Rig.gc.winner == b) r.losses++;
            else r.draws++;
        }
        r.meanRounds = rounds / r.games;
        return r;
    }

    /// <summary>extraFor: 0 none, 1 seat A, 2 seat B.</summary>
    public static void Setup(int half, Kind aKind, Kind bKind, out Player a, out Player b, int extraFor) {
        Rig.NewGame(half + 2);
        a = Rig.AddPlayer("A", 1);
        b = Rig.AddPlayer("B", 2);
        for (int x = -half; x <= half; x++) for (int y = -3; y <= 3; y++) {
            HexTile t = Rig.At(x, y);
            if (t != null && Forest(x, y, variant) && Math.Abs(x) < half - 1) t.terrain = Terrain.forest;
        }
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
        if (extraFor != 0) {
            Player p = extraFor == 1 ? a : b;
            int s = p == a ? -1 : 1;
            Spawn("Tank", p, s * (half - 3), s);
            Spawn("Grunt", p, s * (half - 3), -s);
        }
        Attach(a, aKind);
        Attach(b, bKind);
        Rig.gc.activePlayer = a;
    }

    /// <summary>Variant 0 is the original pattern. Others are mirror-symmetric (x to -x, y to -y), so neither seat gets better ground.</summary>
    static bool Forest(int x, int y, int v) {
        if (v == 0) return (x * 7 + y * 3) % 5 == 0;
        long k = (Math.Abs(x) * 31L + (x * y >= 0 ? 1 : 2) * Math.Abs(y) * 17L + v * 101L) * 2654435761L;
        return (k >> 7) % 5 == 0;
    }

    static void Attach(Player p, Kind k) {
        if (k == Kind.Full) {
            AIProfile profile = ScriptableObject.CreateInstance<AIProfile>();
            profile.annotateBoard = false;
            if (Tweak != null) Tweak(profile);
            Rig.AttachAI(p, profile);
        } else {
            Rig.AttachBaseline(p, k == Kind.Greedy ? AIKind.Greedy : AIKind.Legacy);
        }
    }

    /// <summary>Round-by-round positions and every Full-AI decision, for one game.</summary>
    public static void Trace(int width, Kind aKind, Kind bKind, int extraFor, int rounds) {
        Player a, b;
        Setup(width, aKind, bKind, out a, out b, extraFor);
        foreach (Player p in new[] { a, b }) {
            AIPlayer ai = p.gameObject.GetComponent<AIPlayer>();
            if (ai != null) ai.onDecision = (u, act, did) => {
                if (act.kind == AIActionKind.Wait && !did) return;
                Console.WriteLine("    " + u.owner.personal_name + " " + u.name + D(u.tile) + " " + act.kind
                    + (act.target != null ? "->" + act.target.name + D(act.target.tile) : "") + " (" + act.reason + ")");
            };
        }
        string explain = Environment.GetEnvironmentVariable("EXPLAIN");
        int explainFrom = Environment.GetEnvironmentVariable("FROM") != null ? int.Parse(Environment.GetEnvironmentVariable("FROM")) : 0;
        GameController gc = Rig.gc;
        for (int r = 0; r < rounds * 2 && !gc.gameOver; r++) {
            AITactical.DebugUnit = null;
            if (explain != null && gc.days >= explainFrom) {
                Player side = Environment.GetEnvironmentVariable("SIDE") == "B" ? b : a;
                foreach (Unit u in side.units) if (u.name == explain) { AITactical.DebugUnit = u; break; }
                AITactical.DebugLog = msg => Console.WriteLine("      X " + msg);
                AITactical.DebugBreakdown = Environment.GetEnvironmentVariable("BREAKDOWN") != null;
            }
            Player active = gc.activePlayer;
            AIPlayer full = active.gameObject.GetComponent<AIPlayer>();
            BaselineAIPlayer baseline = active.gameObject.GetComponent<BaselineAIPlayer>();
            if (full != null) {
                List<HexTactica.AI.Front> fr = null;
                full.PlayWholeTurn();
                fr = full.CurrentFronts;
                if (Environment.GetEnvironmentVariable("FRONTS") != null && fr != null)
                    foreach (var f in fr) Console.WriteLine("    front " + f + " assault=" + f.assault + " objective=" + D(f.objectiveTile)
                        + " staging=" + f.staging + " ready=" + f.readiness.ToString("0.00") + " own=" + f.ownStrength.ToString("0") + " enemy=" + f.enemyStrength.ToString("0") + " units=" + f.assigned.Count);
                if (Environment.GetEnvironmentVariable("FRONTS") != null && full.CurrentObjectives != null)
                    foreach (var o in full.CurrentObjectives) if (o.committed || Environment.GetEnvironmentVariable("ALLOBJ") != null) Console.WriteLine("    objective " + o);
            } else if (baseline != null) baseline.PlayWholeTurn(); else gc.EndTurn();
            if (active == b) {
                foreach (Player p in new[] { a, b }) {
                    string line = "  r" + gc.days + " " + p.personal_name + ":";
                    foreach (Unit u in p.units) line += " " + u.name.Substring(0, 2) + D(u.tile) + "s" + u.Strength;
                    Console.WriteLine(line);
                }
            }
        }
        Console.WriteLine("winner " + (gc.winner != null ? gc.winner.personal_name : "none"));
    }

    static string D(HexTile t) { return t == null ? "(dead)" : "(" + t.x + "," + t.y + ")"; }

    public static string Play() {
        GameController gc = Rig.gc;
        int turn = 0;
        while (!gc.gameOver && turn++ < MaxPlayerTurns) {
            Player active = gc.activePlayer;
            if (active.eliminated) { gc.EndTurn(); continue; }
            AIPlayer full = active.gameObject.GetComponent<AIPlayer>();
            BaselineAIPlayer baseline = active.gameObject.GetComponent<BaselineAIPlayer>();
            if (full != null) full.PlayWholeTurn();
            else if (baseline != null) baseline.PlayWholeTurn();
            else gc.EndTurn();
        }
        return gc.winner != null ? gc.winner.personal_name : "none";
    }

    public static int Material(Player p) {
        float m = 0;
        foreach (Unit u in p.units) if (u != null) m += u.Strength * AIScore.Worth(u);
        return (int)m;
    }

    static void Spawn(string n, Player p, int x, int y) {
        Unit u = Rig.Spawn(PrefabLoader.Load(n), p, x, y);
        PrefabLoader.AttachHubIfAny(n, u);
    }
}
