using System.Collections.Generic;
using System.Text;
using UnityEngine;
using HexTactica.AI;

namespace HexTactica.Harness {

    /// <summary>
    /// Runs whole games with no rendering and no frames.
    ///
    /// This exists because tuning by hand does not scale past about three parameters,
    /// and AIProfile has twenty. Without it, every threshold in the evaluator is
    /// unfalsifiable — which is the state QuickStrat is still in, nine years on, with
    /// roughly forty magic numbers nobody can demonstrate are better than the number
    /// next to them.
    ///
    /// Combat is fully deterministic (calculateDamage has no RNG, tie-breaks are on
    /// cube coordinate, the Random baseline uses a seeded LCG), so a scenario replays
    /// exactly and a regression is a real regression rather than variance. That is a
    /// large gift and worth protecting: any RNG added later should be seeded from the
    /// scenario, never from UnityEngine.Random.
    /// </summary>
    public class BatchRunner : MonoBehaviour {

        public List<Scenario> scenarios = new List<Scenario>();
        public bool logEachRun = true;

        readonly List<ScenarioResult> results = new List<ScenarioResult>();
        public IList<ScenarioResult> Results { get { return results; } }

        public void ClearResults() { results.Clear(); }

        /// <summary>Add a result produced outside RunAll (e.g. by ScenarioLoader) to the report.</summary>
        public void Record(ScenarioResult r) { if (r != null) results.Add(r); }

        /// <summary>Run every scenario once and return the collected results.</summary>
        public List<ScenarioResult> RunAll() {
            results.Clear();
            for (int i = 0; i < scenarios.Count; i++) {
                Scenario s = scenarios[i];
                if (s == null || !s.IsValid) continue;
                ScenarioResult r = Run(s);
                results.Add(r);
                if (logEachRun) UnityEngine.Debug.Log(r.Summary());
            }
            return results;
        }

        /// <summary>
        /// Play one scenario to completion against the board as currently loaded.
        ///
        /// Scene construction is deliberately left to the caller: building a hex grid
        /// and instantiating prefabs is editor work, and mixing it into the runner
        /// would make the runner untestable. The runner's job is the loop and the
        /// measurement.
        /// </summary>
        public ScenarioResult Run(Scenario scenario) {

            ScenarioResult result = new ScenarioResult { scenarioName = scenario.scenarioName };
            GameController gc = GameController.gc;
            if (gc == null) {
                UnityEngine.Debug.LogError("BatchRunner: no GameController in scene");
                return result;
            }

            bool previousHeadless = gc.headless;
            gc.headless = true;

            // Baseline snapshot.
            Dictionary<Player, int> startValue = new Dictionary<Player, int>();
            for (int i = 0; i < gc.players.Count; i++) {
                Player p = gc.players[i];
                if (p == null) continue;
                ScenarioResult.SideMetrics m = result.Side(p.personal_name);
                m.unitsStart = p.units.Count;
                m.valueStart = p.ArmyValue;
                m.ai = AIKindOf(p);
                startValue[p] = p.ArmyValue;
                p.unitTurnsOutOfSupply = 0;
                p.unitsLostToAttrition = 0;
            }

            int turn = 0;
            while (!gc.gameOver && turn < scenario.turnLimit) {
                turn++;

                Player active = gc.activePlayer;
                if (active == null) break;

                if (!active.eliminated) {
                    TakeTurn(active);
                } else {
                    gc.EndTurn();
                }

                SampleTurn(result, gc);

                if (gc.gameOver) break;
            }

            result.turns = turn;
            result.timedOut = !gc.gameOver;
            result.winner = gc.winner != null ? gc.winner.personal_name : "none";

            // Final tallies.
            for (int i = 0; i < gc.players.Count; i++) {
                Player p = gc.players[i];
                if (p == null) continue;
                ScenarioResult.SideMetrics m = result.Side(p.personal_name);
                m.unitsEnd = p.units.Count;
                m.valueEnd = p.ArmyValue;
                m.unitTurnsOutOfSupply = p.unitTurnsOutOfSupply;
                m.unitsLostToAttrition = p.unitsLostToAttrition;
                m.unitsLostToCombat = Mathf.Max(0, m.unitsStart - m.unitsEnd - m.unitsLostToAttrition);
            }
            // Value destroyed is the mirror of what the others lost.
            for (int i = 0; i < gc.players.Count; i++) {
                Player p = gc.players[i];
                if (p == null) continue;
                ScenarioResult.SideMetrics m = result.Side(p.personal_name);
                int enemyLoss = 0;
                for (int j = 0; j < gc.players.Count; j++) {
                    Player q = gc.players[j];
                    if (q == null || q == p) continue;
                    int qStart;
                    startValue.TryGetValue(q, out qStart);
                    enemyLoss += Mathf.Max(0, qStart - q.ArmyValue);
                }
                m.valueDestroyed = enemyLoss;
            }

            gc.headless = previousHeadless;
            return result;
        }

        static AIKind AIKindOf(Player p) {
            BaselineAIPlayer baseline = p.GetComponent<BaselineAIPlayer>();
            if (baseline != null) return baseline.kind;
            return p.GetComponent<AIPlayer>() != null ? AIKind.Full : AIKind.Full;
        }

        static void TakeTurn(Player p) {
            BaselineAIPlayer baseline = p.GetComponent<BaselineAIPlayer>();
            if (baseline != null) { baseline.PlayWholeTurn(); return; }

            AIPlayer ai = p.GetComponent<AIPlayer>();
            if (ai != null) { ai.PlayWholeTurn(); return; }

            // Not an AI: nothing to do, pass.
            if (GameController.gc != null) GameController.gc.EndTurn();
        }

        /// <summary>
        /// Per-turn sampling. The supply time series is the diagnostic that matters —
        /// it degrades several turns before the win rate does.
        /// </summary>
        static void SampleTurn(ScenarioResult result, GameController gc) {
            for (int i = 0; i < gc.players.Count; i++) {
                Player p = gc.players[i];
                if (p == null || p.eliminated) continue;

                ScenarioResult.SideMetrics m = result.Side(p.personal_name);
                SupplyNetwork.Result supply = SupplyNetwork.Solve(p, false);

                float totalDepth = 0;
                int inSupply = 0;
                HexTile hq = HQTile(p);

                for (int j = 0; j < p.units.Count; j++) {
                    Unit u = p.units[j];
                    if (u == null || u.tile == null) continue;

                    int depth = supply.DepthAt(u.tile);
                    totalDepth += depth;
                    if (depth > 0) inSupply++;

                    if (hq != null) {
                        int penetration = u.tile.Distance(hq);
                        if (penetration > m.deepestPenetration) m.deepestPenetration = penetration;
                    }
                    if (depth <= 0) {
                        int gap = DistanceToSupply(u.tile, supply);
                        if (gap > m.worstSupplyGap) m.worstSupplyGap = gap;
                    }
                }

                int count = Mathf.Max(1, p.units.Count);
                m.meanSupplyDepthByTurn.Add(totalDepth / count);
                m.unitsInSupplyByTurn.Add(inSupply);

                for (int j = 0; j < p.units.Count; j++) {
                    Unit u = p.units[j];
                    if (u == null) continue;
                    SupplyPoint sp = u.Hub;
                    if (sp != null && !sp.connected) m.hubStrandedTurns++;
                }
            }
        }

        static HexTile HQTile(Player p) {
            for (int i = 0; i < Building.all.Count; i++) {
                Building b = Building.all[i];
                if (b != null && b.owner == p && b.IsHQ) return b.tile;
            }
            return null;
        }

        /// <summary>How far this tile is from the nearest supplied tile. Bounded search.</summary>
        static int DistanceToSupply(HexTile from, SupplyNetwork.Result supply) {
            const int maxSearch = 12;
            for (int r = 1; r <= maxSearch; r++) {
                List<HexTile> ring = AIPath.TilesInRange(from, r - 1, r);
                for (int i = 0; i < ring.Count; i++) {
                    if (supply.IsSupplied(ring[i])) return r;
                }
            }
            return maxSearch;
        }

        // ---------------------------------------------------------------- reporting

        public string ToCsv() {
            StringBuilder sb = new StringBuilder();
            sb.Append(ScenarioResult.CsvHeader());
            for (int i = 0; i < results.Count; i++) sb.Append(results[i].ToCsvRow());
            return sb.ToString();
        }

        /// <summary>Win rate for one side name across all collected results.</summary>
        public float WinRate(string sideName) {
            if (results.Count == 0) return 0;
            int wins = 0;
            for (int i = 0; i < results.Count; i++) {
                if (results[i].winner == sideName) wins++;
            }
            return wins / (float)results.Count;
        }

        /// <summary>
        /// Aggregate supply competence: total unit-turns spent out of supply, summed
        /// across every run for one side. Lower is better and it is the headline
        /// number for the AI work.
        /// </summary>
        public int TotalUnitTurnsOutOfSupply(string sideName) {
            int total = 0;
            for (int i = 0; i < results.Count; i++) {
                ScenarioResult.SideMetrics m;
                if (results[i].sides.TryGetValue(sideName, out m)) total += m.unitTurnsOutOfSupply;
            }
            return total;
        }
    }
}
