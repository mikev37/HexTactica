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
        /// <summary>One thread's tallies (forecasts run on worker threads too: AIParallel). Report sums every thread's.</summary>
        sealed class Tally {
            public readonly Dictionary<string, double> ms = new Dictionary<string, double>();
            public readonly Dictionary<string, int> calls = new Dictionary<string, int>();
            public readonly Dictionary<string, long> bytes = new Dictionary<string, long>();
            public readonly Stack<long> allocAt = new Stack<long>();
        }
        [System.ThreadStatic] static Tally mine;
        static readonly List<Tally> all = new List<Tally>();
        static Tally Mine { get { if (mine == null) { mine = new Tally(); lock (all) all.Add(mine); } return mine; } }

        public static long Begin() {
            if (!Enabled) return 0;
            Mine.allocAt.Push(System.GC.GetAllocatedBytesForCurrentThread());
            return Stopwatch.GetTimestamp();
        }

        public static void End(string section, long started) {
            if (!Enabled) return;
            Tally t = Mine;
            double d = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            long b = t.allocAt.Count > 0 ? System.GC.GetAllocatedBytesForCurrentThread() - t.allocAt.Pop() : 0;
            lock (t) {
                double v; t.ms.TryGetValue(section, out v); t.ms[section] = v + d;
                int c; t.calls.TryGetValue(section, out c); t.calls[section] = c + 1;
                long bb; t.bytes.TryGetValue(section, out bb); t.bytes[section] = bb + b;
            }
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

        public static void Reset() { lock (all) foreach (Tally t in all) lock (t) { t.ms.Clear(); t.calls.Clear(); t.bytes.Clear(); t.allocAt.Clear(); } }

        /// <summary>Every thread's time summed (on worker threads, time spent in parallel with the thinking thread).</summary>
        public static string Report() {
            Dictionary<string, double> ms = new Dictionary<string, double>();
            Dictionary<string, int> calls = new Dictionary<string, int>();
            Dictionary<string, long> bytes = new Dictionary<string, long>();
            lock (all) foreach (Tally t in all) lock (t) foreach (KeyValuePair<string, double> kv in t.ms) {
                double v; ms.TryGetValue(kv.Key, out v); ms[kv.Key] = v + kv.Value;
                int c; calls.TryGetValue(kv.Key, out c); calls[kv.Key] = c + t.calls[kv.Key];
                long b; bytes.TryGetValue(kv.Key, out b); bytes[kv.Key] = b + t.bytes[kv.Key];
            }
            List<KeyValuePair<string, double>> rows = new List<KeyValuePair<string, double>>(ms);
            rows.Sort((a, b) => b.Value.CompareTo(a.Value));
            StringBuilder sb = new StringBuilder("      sections:");
            foreach (KeyValuePair<string, double> r in rows) sb.AppendFormat("\n        {0,8:0} ms {3,7:0} MB  {1,6} calls  {2}", r.Value, calls[r.Key], r.Key, bytes[r.Key] / 1048576.0);
            return sb.ToString();
        }
    }
}
