using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Arrival time as (turns, movement points used within that turn, steps taken).
    /// Compared lexicographically, which makes it a proper metric for Dijkstra:
    /// minimise turns first, then movement spent on the arrival turn, then fuel.
    /// </summary>
    public struct Eta : IComparable<Eta> {

        public int turns;   // 0 == reachable this turn
        public int used;    // movement points consumed during the arrival turn
        public int steps;   // tiles entered, proxy for fuel burned

        public static readonly Eta Unreachable =
            new Eta { turns = int.MaxValue, used = int.MaxValue, steps = int.MaxValue };

        public static readonly Eta Origin = new Eta { turns = 0, used = 0, steps = 0 };

        public bool IsUnreachable { get { return turns == int.MaxValue; } }
        public bool ThisTurn { get { return turns == 0; } }

        public int CompareTo(Eta o) {
            if (turns != o.turns) return turns.CompareTo(o.turns);
            if (used != o.used) return used.CompareTo(o.used);
            return steps.CompareTo(o.steps);
        }

        const long USED_SCALE = 1L << 20;
        const long TURN_SCALE = 1L << 40;

        /// <summary>Packs the tuple into one sortable long for the heap.</summary>
        public long Key {
            get {
                if (IsUnreachable) return long.MaxValue;
                return turns * TURN_SCALE + used * USED_SCALE + steps;
            }
        }

        public override string ToString() {
            return IsUnreachable ? "unreachable" : ("T+" + turns + " (" + used + "mp, " + steps + " steps)");
        }
    }

    /// <summary>
    /// A computed reachability field for one unit from one or more origins.
    /// Holds no references into HexTile.G / HexTile.parent, so evaluating a
    /// hypothetical never disturbs the board the player is looking at. This is
    /// the fix for the thing that made QuickStrat structurally unable to ask
    /// "what if" — its scoring read a shared mutable global.
    /// </summary>
    public class MoveField {

        public readonly Unit unit;
        readonly Dictionary<HexTile, Eta> eta = new Dictionary<HexTile, Eta>();
        readonly Dictionary<HexTile, HexTile> cameFrom = new Dictionary<HexTile, HexTile>();

        public MoveField(Unit unit) { this.unit = unit; }

        public IEnumerable<HexTile> Tiles { get { return eta.Keys; } }
        public int Count { get { return eta.Count; } }

        public bool Reachable(HexTile t) {
            return t != null && eta.ContainsKey(t);
        }

        public bool ReachableThisTurn(HexTile t) {
            Eta e;
            return t != null && eta.TryGetValue(t, out e) && e.ThisTurn;
        }

        public Eta Get(HexTile t) {
            Eta e;
            if (t != null && eta.TryGetValue(t, out e)) return e;
            return Eta.Unreachable;
        }

        public HexTile ParentOf(HexTile t) {
            HexTile p;
            return (t != null && cameFrom.TryGetValue(t, out p)) ? p : null;
        }

        internal void Set(HexTile t, Eta e, HexTile from) {
            eta[t] = e;
            if (from != null) cameFrom[t] = from;
            else cameFrom.Remove(t);
        }

        /// <summary>Full tile sequence from origin to destination, inclusive of both.</summary>
        public List<HexTile> PathTo(HexTile dest) {
            List<HexTile> path = new List<HexTile>();
            if (!Reachable(dest)) return path;
            HexTile cur = dest;
            int guard = 0;
            while (cur != null && guard++ < 4096) {
                path.Add(cur);
                cur = ParentOf(cur);
            }
            path.Reverse();
            return path;
        }

        /// <summary>
        /// The furthest tile along the path to <paramref name="dest"/> that this unit
        /// can legally finish its turn on right now. This is how a multi-turn plan
        /// becomes a single legal move — the same trick the old AttackMove used with
        /// FindFirstParentInList, but driven off a field that understands turns
        /// rather than a raw parent chain of unknown length.
        /// </summary>
        public HexTile StepToward(HexTile dest) {
            if (!Reachable(dest)) return null;
            HexTile cur = dest;
            int guard = 0;
            while (cur != null && guard++ < 4096) {
                Eta e = Get(cur);
                if (e.ThisTurn && AIPath.CanFinishTurnOn(unit, cur)) return cur;
                cur = ParentOf(cur);
            }
            return null;
        }
    }

    public static class AIPath {

        /// <summary>Movement point cost of entering <paramref name="to"/> from <paramref name="from"/>.</summary>
        public static int StepCost(Unit mover, HexTile from, HexTile to) {
            if (mover == null || to == null || to.terrain == null) return int.MaxValue;

            Locomotion loco = mover.locomotion;

            if (LocomotionRules.IgnoresTerrain(loco)) return 1;
            // On and off a bridge only by road.
            if (!LocomotionRules.BridgeStep(from, to, loco)) return int.MaxValue;

            if (LocomotionRules.UsesRoads(loco) &&
                from != null && from.terrain != null &&
                from.terrain.road && to.terrain.road) {
                return 1;
            }

            return 1 + Mathf.Max(0, to.EffectiveMoveability - mover.terrainScramble);
        }

        /// <summary>Can this unit legally end its turn on this tile (ignoring who is standing there right now)?</summary>
        public static bool CanFinishTurnOn(Unit mover, HexTile t) {
            if (t == null) return false;
            if (!mover.CanEnter(t)) return false;
            if (t.occupier == null) return true;
            return t.occupier == mover;   // its own tile always counts
        }

        /// <summary>Passable for transit purposes: friendlies can be moved through, enemies cannot.</summary>
        public static bool CanPassThrough(Unit mover, HexTile t, bool ignoreEnemies) {
            if (t == null) return false;
            if (!mover.CanEnter(t)) return false;
            if (t.occupier == null) return true;
            if (t.occupier == mover) return true;
            if (t.occupier.owner == mover.owner) return true;
            return ignoreEnemies;
        }

        /// <summary>Movement points available on the current turn, after fuel.</summary>
        public static int RangeThisTurn(Unit mover) {
            // "Once out of fuel the unit will not be able to move." Partial fuel is
            // enforced per tile by StepBudget, not by shrinking movement points.
            if (mover.fuelMax > 0 && mover.fuel <= 0) return 0;
            if (!LocomotionRules.CanMove(mover.locomotion)) return 0;
            return Mathf.Max(0, mover.moveRange);
        }

        /// <summary>Maximum tiles this unit can enter before running dry.</summary>
        public static int StepBudget(Unit mover) {
            if (mover.minFuelPerTile <= 0) return int.MaxValue;
            if (mover.fuelMax <= 0) return int.MaxValue;
            return Mathf.Max(0, mover.fuel / mover.minFuelPerTile);
        }

        public static MoveField ThisTurn(Unit mover) {
            return Compute(mover, Single(mover.tile), 0, false);
        }

        /// <summary>
        /// Single-turn field from an arbitrary origin with an explicit movement budget.
        /// This is what HexTile.CheckMovementRange delegates to, so the movement the
        /// player is offered and the movement the AI plans with are the same function.
        /// </summary>
        public static MoveField ThisTurnFrom(Unit mover, HexTile origin, int range) {
            return Compute(mover, Single(origin), 0, false, range);
        }

        public static MoveField MultiTurn(Unit mover, int maxTurns) {
            return Compute(mover, Single(mover.tile), maxTurns, false);
        }

        public static MoveField MultiTurnFrom(Unit mover, HexTile origin, int maxTurns) {
            return Compute(mover, Single(origin), maxTurns, false);
        }

        /// <summary>
        /// Reachability ignoring enemy blocking — used for threat projection, where
        /// the question is "where could this unit shoot if unopposed", not "where can
        /// it actually walk".
        /// </summary>
        public static MoveField Unopposed(Unit mover, int maxTurns) {
            return Compute(mover, Single(mover.tile), maxTurns, true);
        }

        static IEnumerable<KeyValuePair<HexTile, Eta>> Single(HexTile origin) {
            yield return new KeyValuePair<HexTile, Eta>(origin, Eta.Origin);
        }

        /// <summary>
        /// Multi-source Dijkstra over (tile) with lexicographic (turns, used, steps) cost.
        ///
        /// The multi-source form is what makes the transport planner tractable: seeding
        /// every candidate rendezvous tile at its own arrival time collapses what would
        /// be one search per rendezvous into a single search.
        /// </summary>
        public static MoveField Compute(Unit mover,
                                        IEnumerable<KeyValuePair<HexTile, Eta>> sources,
                                        int maxTurns,
                                        bool ignoreEnemies) {
            return Compute(mover, sources, maxTurns, ignoreEnemies, -1);
        }

        public static MoveField Compute(Unit mover,
                                        IEnumerable<KeyValuePair<HexTile, Eta>> sources,
                                        int maxTurns,
                                        bool ignoreEnemies,
                                        int rangeOverride) {

            MoveField field = new MoveField(mover);
            if (mover == null) return field;

            int range = rangeOverride >= 0 ? rangeOverride : RangeThisTurn(mover);
            int futureRange = LocomotionRules.CanMove(mover.locomotion) ? Mathf.Max(0, mover.moveRange) : 0;
            int stepBudget = StepBudget(mover);

            // A unit with no movement at all still occupies its own tile.
            AIHeap<HexTile> open = new AIHeap<HexTile>();
            foreach (KeyValuePair<HexTile, Eta> src in sources) {
                if (src.Key == null) continue;
                Eta existing = field.Get(src.Key);
                if (src.Value.CompareTo(existing) < 0) {
                    field.Set(src.Key, src.Value, null);
                    open.Push(src.Key, src.Value.Key);
                }
            }

            HashSet<HexTile> closed = new HashSet<HexTile>();

            while (open.Count > 0) {
                HexTile current = open.Pop();
                if (closed.Contains(current)) continue;
                closed.Add(current);

                Eta here = field.Get(current);
                if (here.IsUnreachable) continue;
                if (here.turns > maxTurns) continue;

                List<HexTile> neighbors = current.neighbors;
                for (int i = 0; i < neighbors.Count; i++) {
                    HexTile next = neighbors[i];
                    if (next == null || closed.Contains(next)) continue;
                    if (!CanPassThrough(mover, next, ignoreEnemies)) continue;

                    int cost = StepCost(mover, current, next);
                    if (cost == int.MaxValue) continue;

                    int allowance = here.turns == 0 ? range : futureRange;

                    Eta candidate;
                    if (here.used + cost <= allowance) {
                        candidate = new Eta {
                            turns = here.turns,
                            used = here.used + cost,
                            steps = here.steps + 1
                        };
                    } else {
                        // Rolling into a new turn requires actually stopping here.
                        if (!CanFinishTurnOn(mover, current)) continue;
                        if (cost > futureRange) continue;          // unreachable at any budget
                        candidate = new Eta {
                            turns = here.turns + 1,
                            used = cost,
                            steps = here.steps + 1
                        };
                    }

                    if (candidate.turns > maxTurns) continue;
                    if (candidate.steps > stepBudget) continue;    // out of fuel

                    if (candidate.CompareTo(field.Get(next)) < 0) {
                        field.Set(next, candidate, current);
                        open.Push(next, candidate.Key);
                    }
                }
            }

            return field;
        }

        /// <summary>
        /// Exact movement cost, for <paramref name="mover"/>, from any tile to the nearest of
        /// <paramref name="goals"/>, within <paramref name="budget"/> movement points — with
        /// the goal each tile reaches recorded in <paramref name="goalOf"/>.
        ///
        /// Dijkstra run backwards from the goals: stepping from a neighbour n onto tile c
        /// costs StepCost(mover, n, c), the price of entering c. This answers "can this unit,
        /// standing on X, get to a firing position against Y next turn" for every X at once,
        /// with real terrain, roads and blocking — where a hex-distance estimate sent a tank
        /// to starve in a corner chasing a target it could never actually reach.
        /// </summary>
        public static Dictionary<HexTile, int> CostToReach(Unit mover, IEnumerable<HexTile> goals, int budget,
                                                          out Dictionary<HexTile, HexTile> goalOf) {
            Dictionary<HexTile, int> cost = new Dictionary<HexTile, int>();
            goalOf = new Dictionary<HexTile, HexTile>();
            AIHeap<HexTile> open = new AIHeap<HexTile>();
            foreach (HexTile g in goals) {
                if (g == null || cost.ContainsKey(g)) continue;
                cost[g] = 0;
                goalOf[g] = g;
                open.Push(g, 0);
            }
            HashSet<HexTile> closed = new HashSet<HexTile>();
            while (open.Count > 0) {
                HexTile c = open.Pop();
                if (!closed.Add(c)) continue;
                int here = cost[c];
                List<HexTile> neighbors = c.neighbors;
                for (int i = 0; i < neighbors.Count; i++) {
                    HexTile n = neighbors[i];
                    if (n == null || closed.Contains(n)) continue;
                    if (!CanPassThrough(mover, n, false)) continue;
                    int step = StepCost(mover, n, c);
                    if (step == int.MaxValue) continue;
                    int next = here + step;
                    if (next > budget) continue;
                    int prev;
                    if (cost.TryGetValue(n, out prev) && prev <= next) continue;
                    cost[n] = next;
                    goalOf[n] = goalOf[c];
                    open.Push(n, next);
                }
            }
            return cost;
        }

        /// <summary>
        /// Tiles within attack range of <paramref name="origin"/>.
        ///
        /// The original HexTile.AllInAttackRange iterated dx/dy/dz independently,
        /// which is O(n^3) over a coordinate space where x+y+z is fixed. Deriving
        /// the third axis makes it O(n^2): at range 10 that is ~331 lookups instead
        /// of 9,261, and this runs per candidate tile per unit.
        /// </summary>
        public static List<HexTile> TilesInRange(HexTile origin, int minRange, int maxRange) {
            List<HexTile> result = new List<HexTile>();
            if (origin == null || maxRange < 0) return result;

            for (int dx = -maxRange; dx <= maxRange; dx++) {
                int lo = Mathf.Max(-maxRange, -dx - maxRange);
                int hi = Mathf.Min(maxRange, -dx + maxRange);
                for (int dy = lo; dy <= hi; dy++) {
                    int dz = -dx - dy;
                    HexTile t = HexGenerator.hg.get(origin.x + dx, origin.y + dy, origin.z + dz);
                    if (t == null) continue;
                    int d = origin.Distance(t);
                    if (d > minRange && d <= maxRange) result.Add(t);
                }
            }
            return result;
        }

        /// <summary>Hex distance without needing a HexTile instance on both ends.</summary>
        public static int Distance(HexTile a, HexTile b) {
            if (a == null || b == null) return int.MaxValue;
            return a.Distance(b);
        }
    }
}
