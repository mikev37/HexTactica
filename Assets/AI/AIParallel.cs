using System;
using System.Collections.Generic;
using System.Threading;

namespace HexTactica.AI {

    /// <summary>
    /// Work that splits into independent pieces (the forecasts of a unit's options), run on worker threads at once.
    ///
    /// Each worker thinks on its own copy of the board the caller is thinking on (BoardView.Clone): a forecast moves
    /// pieces on the board for a moment, and copies keep those moves apart. The caller's per-thread settings
    /// (ThreadStatic: the turn's role worths, supply protections, transport links, exclusions) are copied to every
    /// worker first. The caller takes a share of the pieces too. Results go where the piece's index says, so the
    /// outcome is the same as running them in order. Errors are rethrown on the caller.
    ///
    /// Off (Workers 0, or one piece, or no board view: the game outside a thought): runs in order on the caller.
    /// </summary>
    public static class AIParallel {
        /// <summary>Worker threads besides the caller (default: one fewer than the processor's cores, at most 7). 0: off.</summary>
        public static int Workers = Math.Max(0, Math.Min(7, Environment.ProcessorCount - 1));
        /// <summary>Fewer pieces than this run in order (a thread's start-up is not free).</summary>
        public static int MinPieces = 8;

        /// <summary>The caller's per-thread settings, carried to a worker.</summary>
        sealed class Context {
            public Dictionary<Unit, float> roles;
            public HashSet<SupplyPoint> chainLinks, protect, protectOnly;
            public HashSet<Unit> protectUnitsNow, protectUnits, strategicExclude, objectivesExclude;
            public Dictionary<SupplyPoint, Unit> previousChainTrucks, plannedAboard;
            public bool frontLine, recordLinked, putDownHypothetical;
            public SupplyPoint assumeLinked; public Unit assumeLinkedVia;

            public static Context Capture() {
                return new Context {
                    roles = AIWorld.Roles, chainLinks = AITransportPlanner.ChainLinks, protect = AILogistics.Protect, protectOnly = AISupplyChain.ProtectOnly,
                    protectUnitsNow = AILogistics.ProtectUnitsNow, protectUnits = AISupplyChain.ProtectUnits, strategicExclude = AIStrategic.Exclude, objectivesExclude = AIObjectives.Exclude,
                    previousChainTrucks = AILogistics.PreviousChainTrucks, plannedAboard = SupplyNetwork.PlannedAboard,
                    frontLine = AISupplyPlanner.FrontLine, recordLinked = SupplyNetwork.RecordLinked, putDownHypothetical = SupplyNetwork.PutDownHypothetical,
                    assumeLinked = SupplyNetwork.AssumeLinked, assumeLinkedVia = SupplyNetwork.AssumeLinkedVia,
                };
            }
            public void Apply() {
                AIWorld.Roles = roles; AITransportPlanner.ChainLinks = chainLinks; AILogistics.Protect = protect; AISupplyChain.ProtectOnly = protectOnly;
                AILogistics.ProtectUnitsNow = protectUnitsNow; AISupplyChain.ProtectUnits = protectUnits; AIStrategic.Exclude = strategicExclude; AIObjectives.Exclude = objectivesExclude;
                AILogistics.PreviousChainTrucks = previousChainTrucks; SupplyNetwork.PlannedAboard = plannedAboard;
                AISupplyPlanner.FrontLine = frontLine; SupplyNetwork.RecordLinked = recordLinked; SupplyNetwork.PutDownHypothetical = putDownHypothetical;
                SupplyNetwork.AssumeLinked = assumeLinked; SupplyNetwork.AssumeLinkedVia = assumeLinkedVia;
            }
        }

        /// <summary>body(i) for every i in [0, n), split over the workers and the caller.</summary>
        public static void For(int n, Action<int> body) {
            BoardView view = BoardView.Current;
            int workers = Math.Min(Workers, n - 1);
            if (workers <= 0 || n < MinPieces || view == null) { for (int i = 0; i < n; i++) body(i); return; }
            Context ctx = Context.Capture();
            int next = -1;
            Exception failed = null;
            Action run = () => {
                for (int i = Interlocked.Increment(ref next); i < n && failed == null; i = Interlocked.Increment(ref next)) {
                    try { body(i); } catch (Exception e) { Interlocked.CompareExchange(ref failed, e, null); }
                }
            };
            CountdownEvent done = new CountdownEvent(workers);
            for (int w = 0; w < workers; w++) {
                BoardView copy = view.Clone();
                ThreadPool.QueueUserWorkItem(_ => {
                    try { ctx.Apply(); using (copy.Enter()) run(); }
                    catch (Exception e) { Interlocked.CompareExchange(ref failed, e, null); }
                    finally { done.Signal(); }
                });
            }
            run();
            done.Wait();
            done.Dispose();
            if (failed != null) throw new AggregateException("a forecast on a worker thread failed", failed);
        }
    }
}
