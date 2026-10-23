using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Supply units as a chain from the HQ toward the objective (profile.supplyChain).
    ///
    /// A supply unit relays supply when it stands on supplied ground, so a chain reaches as far as
    /// its links are spaced: each link inside the reach of the one behind it. The chain advances
    /// like an inchworm: the rearmost unit moves as far forward as it can while still inside the
    /// HQ's reach, the next as far as it can inside the reach of the HQ and that one, and so on,
    /// so the chain at the end of the turn is whole even though every link moved. Only the end of
    /// the turn matters: supply is worked out when the next turn starts.
    ///
    /// A link stands only where nothing can hit it tonight, never past the frontmost of our
    /// fighting units (the army leads, the supply follows), and only where it pushes the supplied
    /// ground further toward the objective than the links behind it already do. A supply unit with
    /// nothing to add is spare: the ordinary supply planner (AISupplyPlanner) places it.
    ///
    /// "Forward" is measured over land (AIMap.LandSteps), so a road that winds around water is
    /// followed rather than cut across.
    /// </summary>
    public static class AISupplyChain {

        public class Plan {
            /// <summary>Where each link of the chain should end this turn (its own tile if it holds).</summary>
            public readonly Dictionary<SupplyPoint, HexTile> links = new Dictionary<SupplyPoint, HexTile>();
            /// <summary>Supply units the chain does not need this turn.</summary>
            public readonly List<SupplyPoint> spare = new List<SupplyPoint>();
            /// <summary>Where the chain is heading.</summary>
            public HexTile objective;
            /// <summary>Steps over land from the frontmost of our fighting units to the objective.</summary>
            public int frontSteps = int.MaxValue;
            /// <summary>Steps from the objective to the nearest supplied tile, with the chain as planned.</summary>
            public int reachSteps = int.MaxValue;
            /// <summary>
            /// The chain is stretched: no link can move forward this turn, and none is held back by
            /// the army (the army is not ahead of it) — only by the reach of the links behind. A
            /// further supply unit would carry supply further. (Also true with no supply units.)
            /// </summary>
            public bool short_;

            public override string ToString() {
                List<string> parts = new List<string>();
                foreach (KeyValuePair<SupplyPoint, HexTile> kv in links) {
                    HexTile from = kv.Key.myUnit.tile;
                    parts.Add("(" + from.x + "," + from.y + ")" + (kv.Value == from ? " holds" : "->(" + kv.Value.x + "," + kv.Value.y + ")"));
                }
                return "chain toward (" + (objective != null ? objective.x + "," + objective.y : "-") + "): " + string.Join(", ", parts.ToArray())
                     + "; spare " + spare.Count + "; supply reaches " + reachSteps + " steps from the objective, the army " + frontSteps
                     + (short_ ? "; short a supply unit" : "");
            }
        }

        /// <summary>The tile the chain heads for: the nearest enemy HQ by land from ours, else the nearest enemy unit.</summary>
        public static HexTile Objective(AIMap map) {
            Building home = GameController.HQOf(map.player);
            HexTile from = home != null ? home.tile : (map.ownUnits.Count > 0 ? map.ownUnits[0].tile : null);
            if (from == null) return null;
            HexTile best = null; int bestD = int.MaxValue;
            foreach (Building b in Building.all) {
                if (b == null || !b.IsHQ || b.owner == null || b.owner == map.player || b.tile == null) continue;
                int d = map.Travel(null, from, b.tile);
                if (d < bestD || (d == bestD && CombatMath.Precedes(b.tile, best))) { best = b.tile; bestD = d; }
            }
            if (best != null) return best;
            foreach (Unit e in map.enemyUnits) {
                if (e == null || e.tile == null) continue;
                int d = map.Travel(null, from, e.tile);
                if (d < bestD || (d == bestD && CombatMath.Precedes(e.tile, best))) { best = e.tile; bestD = d; }
            }
            return best;
        }

        static bool IsLink(SupplyPoint h) {
            return h != null && !h.producer && h.myUnit != null && h.myUnit.tile != null && h.myUnit.trasnporter == null
                && !LocomotionRules.IsNaval(h.myUnit.locomotion) && !LocomotionRules.IsAirborne(h.myUnit.locomotion);
        }

        /// <summary>
        /// Plan the chain. <paramref name="fixedHubs"/>: supply units that have already moved this
        /// turn, which stay where they are. Tiles other units of ours stand on count as free (they
        /// move first); a supply unit's own tile and tiles another link takes do not.
        /// </summary>
        public static Plan Make(AIMap map, ICollection<Unit> fixedHubs) {
            Plan plan = new Plan();
            plan.objective = Objective(map);
            if (plan.objective == null) return plan;
            Dictionary<HexTile, int> toObj = map.LandSteps(plan.objective);
            System.Func<HexTile, int> steps = t => { int s; return t != null && toObj.TryGetValue(t, out s) ? s : int.MaxValue; };

            // The army's front: the fighting unit nearest the objective over land.
            foreach (Unit u in map.ownUnits) {
                if (u == null || u.tile == null || u.Firepower <= 0 || u.GetComponent<SupplyPoint>() != null) continue;
                if (LocomotionRules.NeedsBase(u.locomotion)) continue;
                plan.frontSteps = Mathf.Min(plan.frontSteps, steps(u.tile));
            }

            List<SupplyPoint> hubs = new List<SupplyPoint>();
            foreach (SupplyPoint h in map.ownHubs) if (IsLink(h)) hubs.Add(h);
            // Rearmost first.
            hubs.Sort((a, b) => { int c = steps(b.myUnit.tile).CompareTo(steps(a.myUnit.tile)); return c != 0 ? c : AIMap.CompareUnits(a.myUnit, b.myUnit); });

            Dictionary<SupplyPoint, HexTile> placed = new Dictionary<SupplyPoint, HexTile>();
            HashSet<HexTile> taken = new HashSet<HexTile>();
            foreach (SupplyPoint h in hubs) if (fixedHubs != null && fixedHubs.Contains(h.myUnit)) { placed[h] = h.myUnit.tile; taken.Add(h.myUnit.tile); }
            SupplyNetwork.Result net = SupplyNetwork.SolvePlanned(map.player, placed);
            int reach = Reach(net, toObj);
            bool anyAdvance = false, anyCapped = false;

            foreach (SupplyPoint h in hubs) {
                if (placed.ContainsKey(h)) continue;
                Unit u = h.myUnit;
                MoveField field = map.ReachThisTurn(u);
                HexTile best = null; int bestSteps = int.MaxValue, bestReach = reach;
                bool cappedOut = false;
                foreach (HexTile t in field.Tiles) {
                    if (t != u.tile && (!field.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(u, t))) continue;
                    if (taken.Contains(t)) continue;
                    if (t != u.tile && t.occupier != null && (t.occupier.owner != u.owner || t.occupier.GetComponent<SupplyPoint>() != null)) continue;
                    if (!AIAir.MayEndOn(u, t, map)) continue;
                    if (net.DepthAt(t) <= 0) continue;                         // a link must be supplied by the links behind it
                    int s = steps(t);
                    if (s == int.MaxValue) continue;
                    // Not ahead of the army — unless no enemy can strike there for more than two
                    // turns (profile.chainLeadsWhenClear): a link behind which the army stands
                    // would otherwise hold in the road and the army, behind it on a narrow road,
                    // never get past (zigzag: Red's chain stood in front of its army all game).
                    if (s < plan.frontSteps && !(map.profile.chainLeadsWhenClear && map.EnemyStrikeTurns(t) > 2)) { cappedOut = true; continue; }
                    if (map.ExpectedLoss(u, t) > 0f) continue;                  // nothing can hit it tonight
                    if (best != null && !(s < bestSteps || (s == bestSteps && CombatMath.Precedes(t, best)))) continue;
                    // ...not even by a chain of blows (the enemy's reply played out, as everywhere else).
                    float lost;
                    if (AIWorld.Evaluate(u, t, new AttackAppraisal(), map, map.replyCache, false).lost.TryGetValue(u, out lost) && lost > 0f) continue;
                    best = t; bestSteps = s;
                }
                if (best != null) {
                    Dictionary<SupplyPoint, HexTile> trial = new Dictionary<SupplyPoint, HexTile>(placed);
                    trial[h] = best;
                    SupplyNetwork.Result after = SupplyNetwork.SolvePlanned(map.player, trial);
                    bestReach = Reach(after, toObj);
                    if (bestReach < reach) {
                        placed[h] = best; taken.Add(best);
                        net = after; reach = bestReach;
                        plan.links[h] = best;
                        if (best != u.tile && steps(best) < steps(u.tile)) anyAdvance = true;
                        if (cappedOut) anyCapped = true;
                        continue;
                    }
                }
                if (cappedOut) anyCapped = true;
                plan.spare.Add(h);
            }
            plan.reachSteps = reach;
            plan.short_ = hubs.Count == 0 || (plan.spare.Count == 0 && !anyAdvance && !anyCapped && reach > 0);
            return plan;
        }

        /// <summary>Steps over land from the objective to the nearest supplied tile.</summary>
        static int Reach(SupplyNetwork.Result net, Dictionary<HexTile, int> toObj) {
            int best = int.MaxValue;
            foreach (KeyValuePair<HexTile, int> kv in net.depth) {
                if (kv.Value <= 0) continue;
                int s;
                if (toObj.TryGetValue(kv.Key, out s) && s < best) best = s;
            }
            return best;
        }
    }
}
