using System;
using System.Collections.Generic;
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
                var fi = typeof(AIProfile).GetField(kv[0], System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);   // hidden (internal) fields too
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
        UnityEngine.Resources.Provider = PrefabLoader.ResourcesProvider;
        // Every action the AI carries out is held to what a player could order (AIExecutor.WhyIllegal): counted,
        // the first of each kind shown, the total printed when the run ends. LEGAL=off: no check.
        if (Environment.GetEnvironmentVariable("LEGAL") != "off") {
            var illegal = new System.Collections.Generic.Dictionary<string, int>();
            int illegalAll = 0;
            HexTactica.AI.AIExecutor.OnIllegal = (act, why) => {
                illegalAll++;
                string kind = act.kind + ": " + System.Text.RegularExpressions.Regex.Replace(why, @"\(-?\d+,-?\d+\)|^\S+ (?=is )", "_");
                int n; illegal.TryGetValue(kind, out n); illegal[kind] = n + 1;
                if (n < 3) Console.WriteLine("ILLEGAL AI ACTION: " + (act.unit != null ? act.unit.name : "?") + (act.unit != null && act.unit.tile != null ? " at (" + act.unit.tile.x + "," + act.unit.tile.y + ")" : "") + " " + act.kind + ": " + why + "   [" + act.reason + "]");
            };
            AppDomain.CurrentDomain.ProcessExit += (o, e) => {
                Console.WriteLine("AI legality: " + illegalAll + " actions a player could not order");
                foreach (var kv in illegal) Console.WriteLine("   " + kv.Value + " x " + kv.Key);
            };
        }
        // SUPPLYOFF=death,cargo,flags: switch off the board-supply refresh fixes (for comparing play).
        string supplyOff = Environment.GetEnvironmentVariable("SUPPLYOFF") ?? "";
        if (supplyOff.Contains("death")) Unit.ResupplyOnAnyDeath = false;
        if (supplyOff.Contains("cargo")) Unit.ResupplyWithCargo = false;
        if (supplyOff.Contains("flags")) SupplyNetwork.FlagsFollowPublish = false;
        // PARALLEL=n: worker threads for forecasts (0: none; default: the machine's cores less one).
        string par = Environment.GetEnvironmentVariable("PARALLEL");
        if (par != null) HexTactica.AI.AIParallel.Workers = int.Parse(par);
        // SPECULATE=1: at the end of each turn, every other AI side speculates in line on the board as it stands (in the
        // game it does so on a worker thread while the opponent plays), and its next turn takes the warmed caches over.
        if (Environment.GetEnvironmentVariable("SPECULATE") != null)
            GameController.TurnEnding += ending => {
                long __s = HexTactica.AI.AIPerf.Begin();
                if (GameController.gc != null) foreach (Player q in GameController.gc.players) {
                    if (q == null || q == ending) continue;
                    AIPlayer ai = q.GetComponent<AIPlayer>();
                    if (ai != null && ai.enabled) ai.SpeculateNow();
                }
                HexTactica.AI.AIPerf.End("speculation (in the opponent's turn)", __s);
            };
        // AIVERIFY=1: every cache the AI's map keeps is checked against a fresh one (reported at the end).
        if (Environment.GetEnvironmentVariable("AIVERIFY") != null) {
            HexTactica.AI.AIMap.Verify = true;
            AppDomain.CurrentDomain.ProcessExit += (o, e) => Console.WriteLine("AIVERIFY: " + HexTactica.AI.AIMap.VerifyReport());
        }
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
        if (probe == "reachcap") { Scenarios.ReachCap(Environment.GetEnvironmentVariable("FILE"), int.Parse(Environment.GetEnvironmentVariable("HUBS") ?? "5"), int.Parse(Environment.GetEnvironmentVariable("TRUCKS") ?? "0")); return 0; }
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
        if (probe == "factions") {
            // Which faction versions only look different (Faction.Cosmetic): what "faction special units off" keeps.
            // (The harness does not load factions: read here from the faction assets and the unit prefabs they name.)
            Dictionary<string, string> byGuid = new Dictionary<string, string>();
            foreach (string m in System.IO.Directory.GetFiles(PrefabLoader.Root("Units"), "*.prefab.meta", System.IO.SearchOption.AllDirectories)) {
                System.Text.RegularExpressions.Match g = System.Text.RegularExpressions.Regex.Match(System.IO.File.ReadAllText(m), @"guid: (\w+)");
                if (g.Success) byGuid[g.Groups[1].Value] = System.IO.Path.GetFileNameWithoutExtension(System.IO.Path.GetFileNameWithoutExtension(m));
            }
            Func<string, Unit> unitOf = line => {
                System.Text.RegularExpressions.Match g = System.Text.RegularExpressions.Regex.Match(line, @"guid: (\w+)");
                string n; if (!g.Success || !byGuid.TryGetValue(g.Groups[1].Value, out n)) return null;
                Unit u = PrefabLoader.Load(n); u.name = n; return u;
            };
            List<Faction> factions = new List<Faction>();
            foreach (string fa in System.IO.Directory.GetFiles(PrefabLoader.Root("Factions"), "*.asset")) {
                Faction f = new Faction(); f.name = System.IO.Path.GetFileNameWithoutExtension(fa);
                string section = ""; Faction.Replacement r = null;
                foreach (string line in System.IO.File.ReadAllLines(fa)) {
                    string t = line.Trim();
                    if (t == "replacements:" || t == "removed:" || t == "extras:" || t.StartsWith("unitlist:")) { section = t.TrimEnd(':'); continue; }
                    if (section == "replacements" && t.StartsWith("- original:")) { r = new Faction.Replacement { original = unitOf(t) }; f.replacements.Add(r); }
                    else if (section == "replacements" && t.StartsWith("replacement:") && r != null) r.replacement = unitOf(t);
                    else if (section == "removed" && t.StartsWith("- {")) f.removed.Add(unitOf(t));
                    else if (section == "extras" && t.StartsWith("- unit:")) f.extras.Add(new Faction.Extra { unit = unitOf(t) });
                }
                factions.Add(f);
            }
            foreach (Faction f in factions) {
                Console.WriteLine(f.name + ":");
                foreach (Faction.Replacement r in f.replacements)
                    if (r != null && r.original != null && r.replacement != null)
                        Console.WriteLine("  " + r.original.name + " -> " + r.replacement.name + ": " + (f.Cosmetic(r) ? "looks only" : "special (" + string.Join(", ", Faction.Differences(r.original, r.replacement).ToArray()) + ")"));
                foreach (Faction.Extra e in f.extras) if (e != null && e.unit != null) Console.WriteLine("  extra " + e.unit.name);
                foreach (Unit u in f.removed) if (u != null) Console.WriteLine("  removed " + u.name);
            }
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
