using System.Collections.Generic;
using System.Text;
using UnityEngine;
using HexTactica.AI;

namespace HexTactica.Harness {

    public enum AIKind {
        Full,        // the supply-aware role-dispatching AI
        Greedy,      // the pre-existing AttackMove behaviour, kept as a fixed baseline
        Random       // legal moves at random: the floor
    }

    [System.Serializable]
    public class ScenarioSide {
        public string playerName = "Side";
        public AIKind ai = AIKind.Full;
        public AIProfile profile;
        public List<UnitPlacement> units = new List<UnitPlacement>();
        public List<BuildingPlacement> buildings = new List<BuildingPlacement>();
    }

    [System.Serializable]
    public struct UnitPlacement {
        public string prefabName;
        public int x, y, z;
        public int strength;
        public int veterancy;
    }

    [System.Serializable]
    public struct BuildingPlacement {
        public BuildingType type;
        public int x, y, z;
        public int income;
        public bool producesSupply;
        public int supplyRange;
    }

    /// <summary>
    /// A reproducible test case. Each scenario should isolate one question — a long
    /// corridor where supply is the whole puzzle, an open map where it is not, a
    /// chokepoint one unit can sever — so that a regression tells you *what* broke
    /// rather than merely that something did.
    /// </summary>
    [CreateAssetMenu(menuName = "HexTactica/Scenario")]
    public class Scenario : ScriptableObject {

        public string scenarioName = "Untitled";
        [TextArea(2, 6)]
        public string question = "What is this scenario testing?";

        public int turnLimit = 40;
        public List<ScenarioSide> sides = new List<ScenarioSide>();

        public bool IsValid { get { return sides != null && sides.Count >= 2; } }
    }

    /// <summary>
    /// Outcome of one scenario run.
    ///
    /// Win rate is the least informative number here. The metrics that teach you
    /// something about a supply game are the sustainment ones: unit-turns spent out
    /// of supply, and the shape of mean supply depth over time. A competent AI's
    /// line holds roughly flat depth as it advances; an incompetent one shows depth
    /// decaying monotonically as the units outrun the trucks — and that goes bad
    /// several turns *before* the win rate does, which is exactly what you want from
    /// a diagnostic.
    /// </summary>
    public class ScenarioResult {

        public string scenarioName;
        public string winner = "none";
        public bool timedOut;
        public int turns;

        public readonly Dictionary<string, SideMetrics> sides = new Dictionary<string, SideMetrics>();

        public class SideMetrics {
            public string name;
            public AIKind ai;
            public int unitsStart;
            public int unitsEnd;
            public int unitsLostToCombat;
            public int unitsLostToAttrition;
            public int unitTurnsOutOfSupply;
            public int hubStrandedTurns;
            public int hubsLost;
            public int valueStart;
            public int valueEnd;
            public int valueDestroyed;          // enemy value killed
            public int deepestPenetration;      // furthest any unit got from own HQ
            public int worstSupplyGap;          // furthest a unit was from supply while out of it
            public readonly List<float> meanSupplyDepthByTurn = new List<float>();
            public readonly List<int> unitsInSupplyByTurn = new List<int>();

            public float ExchangeRatio {
                get {
                    int lost = Mathf.Max(1, valueStart - valueEnd);
                    return valueDestroyed / (float)lost;
                }
            }

            /// <summary>
            /// Slope of mean supply depth over the run. Negative means the army is
            /// steadily outrunning its logistics.
            /// </summary>
            public float SupplyTrend {
                get {
                    int n = meanSupplyDepthByTurn.Count;
                    if (n < 2) return 0;
                    float first = 0, last = 0;
                    int half = Mathf.Max(1, n / 3);
                    for (int i = 0; i < half; i++) first += meanSupplyDepthByTurn[i];
                    for (int i = n - half; i < n; i++) last += meanSupplyDepthByTurn[i];
                    return (last - first) / half;
                }
            }
        }

        public SideMetrics Side(string name) {
            SideMetrics m;
            if (!sides.TryGetValue(name, out m)) {
                m = new SideMetrics { name = name };
                sides[name] = m;
            }
            return m;
        }

        public string ToCsvRow() {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, SideMetrics> kv in sides) {
                SideMetrics m = kv.Value;
                sb.Append(scenarioName).Append(',')
                  .Append(m.name).Append(',')
                  .Append(m.ai).Append(',')
                  .Append(winner == m.name ? 1 : 0).Append(',')
                  .Append(timedOut ? 1 : 0).Append(',')
                  .Append(turns).Append(',')
                  .Append(m.unitsStart).Append(',')
                  .Append(m.unitsEnd).Append(',')
                  .Append(m.unitsLostToCombat).Append(',')
                  .Append(m.unitsLostToAttrition).Append(',')
                  .Append(m.unitTurnsOutOfSupply).Append(',')
                  .Append(m.hubStrandedTurns).Append(',')
                  .Append(m.hubsLost).Append(',')
                  .Append(m.deepestPenetration).Append(',')
                  .Append(m.worstSupplyGap).Append(',')
                  .Append(m.ExchangeRatio.ToString("0.000")).Append(',')
                  .Append(m.SupplyTrend.ToString("0.000"))
                  .Append('\n');
            }
            return sb.ToString();
        }

        public static string CsvHeader() {
            return "scenario,side,ai,won,timedOut,turns,unitsStart,unitsEnd,lostCombat," +
                   "lostAttrition,unitTurnsOutOfSupply,hubStrandedTurns,hubsLost," +
                   "deepestPenetration,worstSupplyGap,exchangeRatio,supplyTrend\n";
        }

        public string Summary() {
            StringBuilder sb = new StringBuilder();
            sb.Append(scenarioName).Append(": ");
            sb.Append(timedOut ? "timeout" : ("winner " + winner));
            sb.Append(" after ").Append(turns).Append(" turns\n");
            foreach (KeyValuePair<string, SideMetrics> kv in sides) {
                SideMetrics m = kv.Value;
                sb.Append("  ").Append(m.name).Append(" (").Append(m.ai).Append(") ")
                  .Append(m.unitsEnd).Append('/').Append(m.unitsStart).Append(" alive, ")
                  .Append(m.unitTurnsOutOfSupply).Append(" unit-turns unsupplied, ")
                  .Append("attrition ").Append(m.unitsLostToAttrition).Append(", ")
                  .Append("exch ").Append(m.ExchangeRatio.ToString("0.00")).Append(", ")
                  .Append("supply trend ").Append(m.SupplyTrend.ToString("+0.00;-0.00;0.00"))
                  .Append('\n');
            }
            return sb.ToString();
        }
    }
}
