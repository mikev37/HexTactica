using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace HexTactica.AI {

    /// <summary>
    /// Section timings for finding where the AI's time goes (the harness's PROBE=perf turns it on).
    /// Off by default: Begin/End cost one bool check each.
    /// </summary>
    public static class AIPerf {
        public static bool Enabled;
        static readonly Dictionary<string, double> ms = new Dictionary<string, double>();
        static readonly Dictionary<string, int> calls = new Dictionary<string, int>();
        static readonly Dictionary<string, long> bytes = new Dictionary<string, long>();
        static readonly Stack<long> allocAt = new Stack<long>();

        public static long Begin() {
            if (!Enabled) return 0;
            allocAt.Push(System.GC.GetAllocatedBytesForCurrentThread());
            return Stopwatch.GetTimestamp();
        }

        public static void End(string section, long started) {
            if (!Enabled) return;
            double d = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            long b = allocAt.Count > 0 ? System.GC.GetAllocatedBytesForCurrentThread() - allocAt.Pop() : 0;
            double v; ms.TryGetValue(section, out v); ms[section] = v + d;
            int c; calls.TryGetValue(section, out c); calls[section] = c + 1;
            long bb; bytes.TryGetValue(section, out bb); bytes[section] = bb + b;
        }

        /// <summary>When CallerDepth is set (PERFCALLERS in the harness): " from A < B < C", the methods that asked (outside AIPath and MoveField).</summary>
        public static int CallerDepth;
        public static string Caller() {
            if (!Enabled || CallerDepth <= 0) return "";
            StackTrace st = new StackTrace(1, false);
            List<string> names = new List<string>();
            for (int i = 0; i < st.FrameCount && names.Count < CallerDepth; i++) {
                System.Reflection.MethodBase m = st.GetFrame(i).GetMethod();
                if (m == null || m.DeclaringType == null) continue;
                string t = m.DeclaringType.Name;
                if (t == "AIPath" || t == "MoveField" || t == "AIPerf") continue;
                names.Add(t + "." + m.Name);
            }
            return " from " + string.Join(" < ", names.ToArray());
        }

        public static void Reset() { ms.Clear(); calls.Clear(); bytes.Clear(); allocAt.Clear(); }

        public static string Report() {
            List<KeyValuePair<string, double>> rows = new List<KeyValuePair<string, double>>(ms);
            rows.Sort((a, b) => b.Value.CompareTo(a.Value));
            StringBuilder sb = new StringBuilder("      sections:");
            foreach (KeyValuePair<string, double> r in rows) sb.AppendFormat("\n        {0,8:0} ms {3,7:0} MB  {1,6} calls  {2}", r.Value, calls[r.Key], r.Key, bytes[r.Key] / 1048576.0);
            return sb.ToString();
        }
    }
}
