using System;
using HexTactica.AI;
using HexTactica.Testing;

/// <summary>
/// Entry point. Every test is a scenario file Unity can load (see Scenarios, ScenarioFile).
///
///   bash Tests/run.sh Assets                                run every scenario
///   PROBE=export bash Tests/run.sh Assets                   regenerate Tests/Scenarios/*.dat
///   PROBE=game FILE=<name> [ROUNDS=r] [SIDE=AI|Red|both]    narrate one scenario
///   PROBE=chokes FILE=<name>                                where blocking cuts enemy movement
///   SET=field=value[,field=value]                           override any AIProfile field
/// </summary>
public static class Tests {

    static Action<AIProfile> tweak;

    static void ApplySet() {
        tweak = Tweak(Environment.GetEnvironmentVariable("SET"));
    }

    /// <summary>"field=value,field=value" as a profile tweak (null for none).</summary>
    public static Action<AIProfile> Tweak(string set) {
        if (string.IsNullOrEmpty(set)) return null;
        string[] pairs = set.Split(',');
        return p => {
            foreach (string pair in pairs) {
                string[] kv = pair.Split('=');
                var fi = typeof(AIProfile).GetField(kv[0]);
                if (fi == null) throw new Exception("No AIProfile field " + kv[0]);
                if (fi.FieldType == typeof(int)) fi.SetValue(p, int.Parse(kv[1]));
                else if (fi.FieldType == typeof(bool)) fi.SetValue(p, bool.Parse(kv[1]));
                else fi.SetValue(p, float.Parse(kv[1]));
            }
        };
    }

    public static int Main(string[] args) {
        if (args.Length > 0) PrefabLoader.AssetsRoot = args[0];
        if (args.Length > 1) Scenarios.Dir = args[1];
        ApplySet();
        string probe = Environment.GetEnvironmentVariable("PROBE");
        if (probe == "export") { Scenarios.Export(); return 0; }
        if (probe == "game") {
            Scenarios.Narrate(Environment.GetEnvironmentVariable("FILE"),
                int.Parse(Environment.GetEnvironmentVariable("ROUNDS") ?? "30"), tweak);
            return 0;
        }
        if (probe == "accuracy") { Scenarios.Accuracy(tweak); return 0; }
        if (probe == "equiv") { Scenarios.Equivalence(Environment.GetEnvironmentVariable("FILE")); return 0; }
        if (probe == "export-rules") { Console.WriteLine(Scenarios.ExportBuildingRules() + " rule files"); return 0; }
        if (probe == "export-zigzag") { Scenarios.ExportZigzag(); Console.WriteLine("logistics-zigzag.dat"); return 0; }
        if (probe == "logistics") {
            Scenarios.Logistics(Environment.GetEnvironmentVariable("FILE") ?? "logistics-zigzag", Tweak(Environment.GetEnvironmentVariable("SET_BLUE")),
                                Tweak(Environment.GetEnvironmentVariable("SET_RED")), Environment.GetEnvironmentVariable("OUT"));
            return 0;
        }
        if (probe == "export-air-gauntlet") { Console.WriteLine(Scenarios.ExportAirGauntlet() + " air gauntlet files"); return 0; }
        if (probe == "export-gauntlet") { Console.WriteLine(Scenarios.ExportGauntlet() + " gauntlet files"); return 0; }
        if (probe == "gauntlet") {
            Scenarios.Gauntlet(Tweak(Environment.GetEnvironmentVariable("SET_A")), Tweak(Environment.GetEnvironmentVariable("SET_B")),
                               Environment.GetEnvironmentVariable("SET_A") ?? "defaults", Environment.GetEnvironmentVariable("SET_B") ?? "defaults");
            return 0;
        }
        if (probe == "versus") {
            Scenarios.Versus(Tweak(Environment.GetEnvironmentVariable("SET_A")), Tweak(Environment.GetEnvironmentVariable("SET_B")),
                             Environment.GetEnvironmentVariable("SET_A") ?? "defaults", Environment.GetEnvironmentVariable("SET_B") ?? "defaults");
            return 0;
        }
        if (probe == "buyvalue") {
            // PROBE=buyvalue FILE=<board> [SIDE=Red]: what that side's buildings can build, valued as AIProduction values them.
            ScenarioFile.Loaded l = ScenarioFile.Load(Scenarios.Find(Environment.GetEnvironmentVariable("FILE")), "Full", null, null);
            Player p = Environment.GetEnvironmentVariable("SIDE") == "Red" ? l.other : l.ai;
            HexTactica.AI.AIMap m = new HexTactica.AI.AIMap(p, p.GetComponent<AIPlayer>().Profile);
            foreach (Building b in Building.all) if (b != null && b.owner == p && b.Purchasable().Count > 0) {
                Console.WriteLine(b.name + " at (" + b.tile.x + "," + b.tile.y + "), money " + p.money + ":");
                foreach (string line in HexTactica.AI.AIProduction.Rank(p, b, m)) Console.WriteLine("  " + line);
            }
            return 0;
        }
        if (probe == "unopposed") { Scenarios.Unopposed(Environment.GetEnvironmentVariable("FILE"), tweak, int.Parse(Environment.GetEnvironmentVariable("ROUNDS") ?? "30")); return 0; }
        if (probe == "suite") {
            Scenarios.Suite(Tweak(Environment.GetEnvironmentVariable("SET_A")), Tweak(Environment.GetEnvironmentVariable("SET_B")),
                            Environment.GetEnvironmentVariable("SET_A") ?? "defaults", Environment.GetEnvironmentVariable("SET_B") ?? "defaults");
            return 0;
        }
        if (probe == "bench") {
            Scenarios.Bench(Tweak(Environment.GetEnvironmentVariable("SET_A")), Tweak(Environment.GetEnvironmentVariable("SET_B")),
                            Environment.GetEnvironmentVariable("SET_A") ?? "defaults", Environment.GetEnvironmentVariable("SET_B") ?? "defaults");
            return 0;
        }
        if (probe == "tree") { Scenarios.Tree(Environment.GetEnvironmentVariable("FILE"), Environment.GetEnvironmentVariable("SIDE") ?? "Blue"); return 0; }
        if (probe == "analyze") {
            Scenarios.Analyze(Environment.GetEnvironmentVariable("FILE"), int.Parse(Environment.GetEnvironmentVariable("ROUNDS") ?? "30"));
            return 0;
        }
        if (probe == "perf") {
            // PROBE=perf FILE=<path or name> [ROUNDS=r]: time every AI step (one Update's worth of work in the game).
            string f = Environment.GetEnvironmentVariable("FILE");
            Scenarios.Perf(System.IO.File.Exists(f) ? f : Scenarios.Find(f), int.Parse(Environment.GetEnvironmentVariable("ROUNDS") ?? "1"), tweak);
            return 0;
        }
        if (probe == "duel") {
            Scenarios.Duel(Environment.GetEnvironmentVariable("FILE"), int.Parse(Environment.GetEnvironmentVariable("ROUNDS") ?? "40"),
                           Tweak(Environment.GetEnvironmentVariable("SET_BLUE")), Tweak(Environment.GetEnvironmentVariable("SET_RED")),
                           Environment.GetEnvironmentVariable("OUT"));
            return 0;
        }
        if (probe == "record") {
            Scenarios.Record(Environment.GetEnvironmentVariable("FILE"), int.Parse(Environment.GetEnvironmentVariable("ROUNDS") ?? "3"),
                             Environment.GetEnvironmentVariable("OUT") ?? "/tmp/hextactica-logs", tweak);
            return 0;
        }
        if (probe == "options") {
            Scenarios.Options(Environment.GetEnvironmentVariable("FILE"), Environment.GetEnvironmentVariable("UNIT"), Environment.GetEnvironmentVariable("AT"), tweak);
            return 0;
        }
        if (probe == "chokes") { Scenarios.Chokes(Environment.GetEnvironmentVariable("FILE")); return 0; }
        return Scenarios.RunAll(tweak) == 0 ? 0 : 1;
    }
}
