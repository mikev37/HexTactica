using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Aircraft and ships: units that take nothing from the supply network and are ditched when their
    /// fuel runs out. They are refuelled, healed and rearmed only on one of our buildings (or its
    /// runway or dock tiles) that services their kind, and aircraft by an air tanker beside them.
    ///
    /// Bingo fuel: a stop is safe if, after flying (or sailing) there and the turn's burn (aircraft:
    /// minFuelPerTurn at the start of each turn away from base), the fuel left still reaches a free
    /// base tile, a turn's burn for every turn the trip back takes. A base tile itself is always safe,
    /// and so is a tile beside one of our air tankers with supplies left (it refuels on the next turn).
    /// Nothing else on our side may plan to stand on runway or dock tiles while we have units that
    /// land there (RunwayBlockCost).
    /// </summary>
    public static class AIAir {

        public static bool NeedsBase(Unit u) { return u != null && LocomotionRules.NeedsBase(u.locomotion); }

        /// <summary>Tiles of our buildings that service <paramref name="u"/> and are free (or where it stands).</summary>
        public static List<HexTile> Bases(Unit u, AIMap map) {
            List<HexTile> list = new List<HexTile>();
            foreach (Building b in Building.all) {
                if (b == null || b.owner != u.owner || !b.Services(u)) continue;
                foreach (HexTile t in b.ServiceTiles()) {
                    if (t == null || !u.CanEnter(t)) continue;
                    if (t.occupier == null || t.occupier == u) list.Add(t);
                }
            }
            return list;
        }

        /// <summary>Fuel steps from every tile to the nearest of <paramref name="bases"/>, moving as <paramref name="u"/> moves (air: any tile, one each; ships: water only).</summary>
        public static Dictionary<HexTile, int> DistanceToBase(Unit u, List<HexTile> bases) {
            Dictionary<HexTile, int> d = new Dictionary<HexTile, int>();
            Queue<HexTile> q = new Queue<HexTile>();
            foreach (HexTile b in bases) { d[b] = 0; q.Enqueue(b); }
            while (q.Count > 0) {
                HexTile t = q.Dequeue();
                foreach (HexTile n in t.neighbors) {
                    if (n == null || d.ContainsKey(n) || !u.CanEnter(n)) continue;
                    if (n.occupier != null && n.occupier.owner != u.owner) continue;
                    d[n] = d[t] + 1;
                    q.Enqueue(n);
                }
            }
            return d;
        }

        static bool BesideTanker(Unit u, HexTile at) {
            if (u.airTanker || !LocomotionRules.IsAirborne(u.locomotion) || u.owner == null) return false;
            foreach (Unit t in u.owner.units) {
                if (t == null || t == u || !t.airTanker || t.tile == null || t.Strength <= 0 || t.supplies <= 0) continue;
                if (at.neighbors.Contains(t.tile)) return true;
            }
            return false;
        }

        /// <summary>
        /// Fuel left over (negative: short) if <paramref name="u"/> ends this turn on <paramref name="stop"/>
        /// and then heads for the nearest base. Bases and tiles beside a tanker: its fuel after the move.
        /// </summary>
        public static int Margin(Unit u, HexTile stop, Dictionary<HexTile, int> toBase, List<HexTile> bases) {
            if (u.fuelMax <= 0) return int.MaxValue;
            int per = Mathf.Max(0, u.minFuelPerTile);
            int after = u.fuel - u.tile.Distance(stop) * per;
            if (bases.Contains(stop) || BesideTanker(u, stop)) return after;
            int burn = LocomotionRules.IsAirborne(u.locomotion) ? Mathf.Max(0, u.minFuelPerTurn) : 0;
            int home;
            if (!toBase.TryGetValue(stop, out home)) return -999;
            int turns = Mathf.Max(1, Mathf.CeilToInt(home / (float)Mathf.Max(1, u.moveRange)));
            return after - burn * turns - home * per;
        }

        /// <summary>
        /// Keep the stops that are safe; if none is, keep the ones nearest a base (heading home).
        /// </summary>
        public static void FilterSafe(Unit u, AIMap map, Dictionary<HexTile, KeyValuePair<float, HexTile>> stops) {
            if (!NeedsBase(u) || u.fuelMax <= 0 || stops.Count == 0) return;
            List<HexTile> bases = Bases(u, map);
            Dictionary<HexTile, int> toBase = DistanceToBase(u, bases);
            List<HexTile> safe = new List<HexTile>(), all = new List<HexTile>(stops.Keys);
            foreach (HexTile s in all) if (Margin(u, s, toBase, bases) >= 0) safe.Add(s);
            if (safe.Count == 0) {
                int best = int.MaxValue;
                foreach (HexTile s in all) { int d; if (toBase.TryGetValue(s, out d) && d < best) best = d; }
                foreach (HexTile s in all) { int d; if (toBase.TryGetValue(s, out d) && d == best) safe.Add(s); }
                if (safe.Count == 0) return;
            }
            foreach (HexTile s in all) if (!safe.Contains(s)) stops.Remove(s);
        }

        /// <summary>
        /// The last word on an aircraft's or ship's action: if it would end the turn somewhere it cannot
        /// get home from, it heads for the nearest base instead (the farthest it can get toward it).
        /// </summary>
        public static AIAction Guard(Unit u, AIAction action, AIMap map) {
            if (!NeedsBase(u) || u.fuelMax <= 0 || u.tile == null || u.trasnporter != null) return action;
            List<HexTile> bases = Bases(u, map);
            Dictionary<HexTile, int> toBase = DistanceToBase(u, bases);
            HexTile stop = action.destination ?? u.tile;
            if (action.kind == AIActionKind.Wait || action.kind == AIActionKind.Attack) stop = u.tile;
            if (Margin(u, stop, toBase, bases) >= 0) return action;
            MoveField f = map.ReachThisTurn(u);
            HexTile best = null; int bestMargin = int.MinValue, bestDist = int.MaxValue;
            foreach (HexTile t in f.Tiles) {
                if (t != u.tile && (!f.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(u, t))) continue;
                int m = Margin(u, t, toBase, bases);
                int d; if (!toBase.TryGetValue(t, out d)) d = 999;
                // Safe tiles first; among them (or, if none is safe, among all) the nearest a base.
                bool better;
                if (best == null) better = true;
                else if ((m >= 0) != (bestMargin >= 0)) better = m >= 0;
                else better = d < bestDist || (d == bestDist && m > bestMargin);
                if (better) { best = t; bestMargin = m; bestDist = d; }
            }
            if (best == null || best == stop) return action;
            if (best == u.tile) return AIAction.Wait(u, "bingo fuel: holds (fuel " + u.fuel + ")");
            return AIAction.Move(u, best, 0f, "bingo fuel: heading for base (fuel " + u.fuel + ", margin " + bestMargin + ")");
        }

        /// <summary>
        /// Gold for a unit of ours that does not land there ending its turn on one of our runway or dock
        /// tiles while we have units that do: they need the space to land.
        /// </summary>
        public static float RunwayBlockCost(Unit u, HexTile stop, AIMap map) {
            if (u == null || stop == null || NeedsBase(u)) return 0f;
            Building b = stop.building;
            if (b == null || b.owner != u.owner || b.services == null || b.services.Count == 0) return 0f;
            foreach (Unit x in map.ownUnits) {
                if (x == null || x == u || !NeedsBase(x) || !b.Services(x)) continue;
                return map.profile.runwayBlockValue;
            }
            return 0f;
        }

        /// <summary>
        /// An air tanker: where to end its turn. Beside the air units of ours that will need fuel most
        /// (fuel needed, summed over those beside the tile), from a tile it is itself safe on; with no
        /// supplies left, back to base.
        /// </summary>
        public static AIAction DecideTanker(Unit u, AIMap map) {
            List<HexTile> bases = Bases(u, map);
            Dictionary<HexTile, int> toBase = DistanceToBase(u, bases);
            MoveField f = map.ReachThisTurn(u);
            HexTile best = null; float bestValue = float.NegativeInfinity;
            foreach (HexTile t in f.Tiles) {
                if (t != u.tile && (!f.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(u, t))) continue;
                if (Margin(u, t, toBase, bases) < 0) continue;
                float v = 0f;
                if (u.supplies > 0) {
                    foreach (Unit x in map.ownUnits) {
                        if (x == null || x == u || x.airTanker || !LocomotionRules.IsAirborne(x.locomotion) || x.tile == null || x.fuelMax <= 0) continue;
                        if (!t.neighbors.Contains(x.tile)) continue;
                        if (Building.ServicingUnder(x) != null) continue;
                        v += (x.fuelMax - x.fuel) * AIScore.Worth(x) / Mathf.Max(1, x.fuelMax);
                    }
                } else {
                    int d; v = toBase.TryGetValue(t, out d) ? -d * 100f : -10000f;
                }
                v -= AIScore.Risk(u, t, map, map.profile);
                if (best == null || v > bestValue || (v == bestValue && CombatMath.Precedes(t, best))) { best = t; bestValue = v; }
            }
            if (best == null || best == u.tile) return AIAction.Wait(u, "tanker: holds");
            return AIAction.Move(u, best, bestValue, u.supplies > 0 ? "tanker: beside the thirstiest aircraft" : "tanker: empty, home to refill");
        }
    }
}
