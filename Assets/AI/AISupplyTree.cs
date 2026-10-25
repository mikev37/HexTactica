using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Supply units as a tree grown from the HQ out to wherever the army is (profile.supplyTree).
    ///
    /// The chain (AISupplyChain) runs one line toward the enemy HQ, and only a supply unit already
    /// inside the network can join it: one cut off, or still on its way, is spare, and the planner
    /// for spare units prices each where it stands. On a wide map the army splits — north and south
    /// of a lake, say — and the line toward the enemy HQ serves neither side; links in the middle
    /// went, the units ahead of them were cut off, and the ones behind sat by the HQ.
    ///
    /// Here the question is turned around: which fighting units are out of supply, and how is the
    /// network brought to them? Starting from what the HQ and our supply buildings supply on their
    /// own, the fighting unit nearest the edge of supply is taken first. The next link goes on the
    /// supplied tile nearest it (over land), where little can hit it tonight; the supply unit that can
    /// get there soonest takes it; the network is worked out again with it there. That repeats —
    /// the supply reaching further toward the unit with each link — until the unit is supplied, then
    /// the next unit out of supply, and so on until every fighting unit is supplied or no supply unit
    /// is left. Links grow out toward each group of the army in turn: the tree branches wherever the
    /// army does.
    ///
    /// A supply unit that already stands where a link should be keeps that place (the edge of
    /// supply a tile further back is just as good). Supply units the tree does not need — and those
    /// aboard a transport, at sea or in the air — are left to the ordinary planner (AISupplyPlanner).
    /// A link out of reach this turn heads there.
    /// </summary>
    public static class AISupplyTree {

        public class Plan {
            /// <summary>Where each supply unit taking part should stand: its link.</summary>
            public readonly Dictionary<SupplyPoint, HexTile> links = new Dictionary<SupplyPoint, HexTile>();
            /// <summary>The links in the order placed (each supplied by those before it).</summary>
            public readonly List<SupplyPoint> order = new List<SupplyPoint>();
            /// <summary>Fighting units the plan brings into supply, and those it cannot.</summary>
            public int reached, unreached;
            /// <summary>Every supply unit has a link and fighting units supply could reach are still out of it: another supply unit would carry it further.</summary>
            public bool short_;

            public override string ToString() {
                List<string> parts = new List<string>();
                foreach (SupplyPoint h in order) {
                    HexTile from = h.myUnit.tile, to = links[h];
                    parts.Add("(" + from.x + "," + from.y + ")" + (to == from ? " holds" : "->(" + to.x + "," + to.y + ")"));
                }
                return "supply tree: " + (parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "no links") + "; brings " + reached + " fighting units into supply, " + unreached + " still out" + (short_ ? "; short a supply unit" : "");
            }
        }

        /// <summary>Diagnostic: each link as it is placed.</summary>
        public static System.Action<string> DebugLog;

        static bool Takes(SupplyPoint h) {
            return h != null && !h.producer && h.myUnit != null && h.myUnit.tile != null && h.myUnit.Strength > 0 && h.myUnit.trasnporter == null
                && !LocomotionRules.IsNaval(h.myUnit.locomotion) && !LocomotionRules.IsAirborne(h.myUnit.locomotion);
        }

        static bool Fights(Unit u) {
            return u != null && u.tile != null && u.Strength > 0 && u.Firepower > 0 && u.Hub == null && u.trasnporter == null
                && !LocomotionRules.NeedsBase(u.locomotion);
        }

        /// <summary>
        /// Plan the tree. <paramref name="fixedHubs"/>: supply units that have already moved this turn,
        /// which stay where they are (and count as links if they are linked there).
        /// </summary>
        public static Plan Make(AIMap map, ICollection<Unit> fixedHubs) {
            bool was = SupplyNetwork.RecordLinked;
            SupplyNetwork.RecordLinked = true;
            try { return MakeLinked(map, fixedHubs); } finally { SupplyNetwork.RecordLinked = was; }
        }

        /// <summary>
        /// The plan, with the solves recording where a supply unit would link (Result.linked): the edge
        /// of supply that counts is the edge of the network, not of what a cut-off city gives out of its
        /// store (a supply unit there would be cut off with it).
        /// </summary>
        static Plan MakeLinked(AIMap map, ICollection<Unit> fixedHubs) {
            Plan plan = new Plan();
            Player me = map.player;
            Dictionary<SupplyPoint, HexTile> placed = new Dictionary<SupplyPoint, HexTile>();
            List<SupplyPoint> free = new List<SupplyPoint>();
            foreach (SupplyPoint h in map.ownHubs) {
                if (!Takes(h)) continue;
                if (fixedHubs != null && fixedHubs.Contains(h.myUnit)) placed[h] = h.myUnit.tile;
                else free.Add(h);
            }
            free.Sort((a, b) => AIMap.CompareUnits(a.myUnit, b.myUnit));
            SupplyNetwork.Result net = SupplyNetwork.SolvePlanned(me, placed);
            float tolerance = Mathf.Max(0f, map.profile.supplyTreeRisk);
            HashSet<HexTile> takenTiles = new HashSet<HexTile>(placed.Values);
            HashSet<Unit> givenUp = new HashSet<Unit>();
            int guard = 0;
            while (free.Count > 0 && guard++ < 64) {
                // The fighting unit out of supply nearest the edge of supply.
                Unit need = null; int needGap = int.MaxValue; Dictionary<HexTile, int> toNeed = null;
                foreach (Unit u in map.ownUnits) {
                    if (!Fights(u) || givenUp.Contains(u) || net.DepthAt(u.tile) > 0) continue;
                    Dictionary<HexTile, int> steps = map.LandSteps(u.tile);
                    int gap = Gap(net, steps);
                    if (gap == int.MaxValue) { givenUp.Add(u); continue; }
                    if (need == null || gap < needGap || (gap == needGap && AIMap.CompareUnits(u, need) < 0)) { need = u; needGap = gap; toNeed = steps; }
                }
                if (need == null) break;

                // Candidate places, best first: a supply unit already standing at the edge of the network
                // (a step back at most) keeps its place; otherwise the edge tiles nearest the unit, least
                // exposed first, each taken by the supply unit that can get there soonest. The first that
                // little can hit tonight (the expected share of fire, and with supplyTreeReply the enemy's
                // reply played out, each within supplyTreeRisk) and that brings supply nearer the unit is
                // the link.
                List<KeyValuePair<SupplyPoint, HexTile>> tries = new List<KeyValuePair<SupplyPoint, HexTile>>();
                foreach (SupplyPoint h in free) {
                    HexTile at = h.myUnit.tile;
                    int s;
                    if (net.LinkedAt(at) <= 0 || !toNeed.TryGetValue(at, out s) || s > needGap + 1 || takenTiles.Contains(at)) continue;
                    tries.Add(new KeyValuePair<SupplyPoint, HexTile>(h, at));
                }
                tries.Sort((a, b) => toNeed[a.Value].CompareTo(toNeed[b.Value]));
                SupplyPoint any = free[0];
                List<HexTile> edge = new List<HexTile>();
                foreach (KeyValuePair<HexTile, int> kv in (net.linked ?? net.depth)) {
                    HexTile t = kv.Key;
                    int s;
                    if (kv.Value <= 0 || takenTiles.Contains(t) || !toNeed.TryGetValue(t, out s) || s > needGap + 1) continue;
                    if (!any.myUnit.CanEnter(t) || (t.occupier != null && (t.occupier.owner != me || t.occupier.Hub != null))) continue;
                    edge.Add(t);
                }
                edge.Sort((a, b) => {
                    int c = toNeed[a].CompareTo(toNeed[b]);
                    if (c != 0) return c;
                    c = map.ExpectedLoss(any.myUnit, a).CompareTo(map.ExpectedLoss(any.myUnit, b));
                    return c != 0 ? c : CombatMath.CompareTiles(a, b);
                });
                foreach (HexTile t in edge) {
                    if (tries.Count >= 12) break;
                    SupplyPoint by = null; int bestTurns = int.MaxValue, bestDist = int.MaxValue;
                    foreach (SupplyPoint h in free) {
                        MoveField f = map.ReachHorizon(h.myUnit);
                        if (!f.Reachable(t) && h.myUnit.tile != t) continue;
                        int turns = h.myUnit.tile == t ? 0 : f.Get(t).turns;
                        int dist = h.myUnit.tile.Distance(t);
                        if (by == null || turns < bestTurns || (turns == bestTurns && dist < bestDist)) { by = h; bestTurns = turns; bestDist = dist; }
                    }
                    if (by != null) tries.Add(new KeyValuePair<SupplyPoint, HexTile>(by, t));
                }
                SupplyPoint hub = null; HexTile slot = null; SupplyNetwork.Result after = null; int gapAfter = needGap;
                string why = tries.Count == 0 ? "no edge tile a supply unit can get to" : "every place within reach of the enemy or bringing supply no nearer";
                foreach (KeyValuePair<SupplyPoint, HexTile> tr in tries) {
                    Unit hu = tr.Key.myUnit;
                    if (map.ExpectedLoss(hu, tr.Value) > tolerance) continue;
                    if (map.profile.supplyTreeReply) {
                        float lost;
                        if (!AIWorld.Evaluate(hu, tr.Value, new AttackAppraisal(), map, map.replyCache, false).lost.TryGetValue(hu, out lost)) lost = 0f;
                        if (lost > tolerance) continue;
                    }
                    placed[tr.Key] = tr.Value;
                    SupplyNetwork.Result a = SupplyNetwork.SolvePlanned(me, placed);
                    int g = Gap(a, toNeed);
                    if (g >= needGap) { placed.Remove(tr.Key); continue; }
                    hub = tr.Key; slot = tr.Value; after = a; gapAfter = g;
                    break;
                }
                if (hub == null) {
                    if (DebugLog != null) DebugLog("   supply tree: nothing for " + need.name + " (" + need.tile.x + "," + need.tile.y + "), " + needGap + " steps out: " + why);
                    givenUp.Add(need);
                    continue;
                }
                net = after;
                free.Remove(hub);
                takenTiles.Add(slot);
                plan.links[hub] = slot;
                plan.order.Add(hub);
                if (DebugLog != null) DebugLog("   supply tree: link at (" + slot.x + "," + slot.y + ") for " + need.name + " (" + need.tile.x + "," + need.tile.y + "), " + gapAfter + " steps short now (was " + needGap + "); supply unit from (" + hub.myUnit.tile.x + "," + hub.myUnit.tile.y + ")");
            }
            foreach (Unit u in map.ownUnits) {
                if (!Fights(u)) continue;
                bool before = map.SupplyDepth(u.tile) > 0, now = net.DepthAt(u.tile) > 0;
                if (now && !before) plan.reached++;
                if (!now) plan.unreached++;
                // Stretched: every supply unit has a link, and a fighting unit supply could reach is out of it.
                if (free.Count == 0 && !now && !givenUp.Contains(u)) plan.short_ = true;
            }
            return plan;
        }

        /// <summary>Steps over land from the edge of the network (Result.linked) to the unit (0: it is in it); int.MaxValue if supply cannot get there.</summary>
        static int Gap(SupplyNetwork.Result net, Dictionary<HexTile, int> toUnit) {
            int best = int.MaxValue;
            foreach (KeyValuePair<HexTile, int> kv in (net.linked ?? net.depth)) {
                if (kv.Value <= 0) continue;
                int s;
                if (toUnit.TryGetValue(kv.Key, out s) && s < best) best = s;
            }
            return best;
        }

        /// <summary>
        /// The move toward its link tonight: the link itself if it can get there (or a free supplied
        /// tile beside it, if the link is taken), else the furthest it can get along the way.
        /// </summary>
        public static AIAction Step(Unit u, HexTile link, AIMap map) {
            if (u.tile == link) return AIAction.Wait(u, "supply tree: holds its link");
            MoveField f = map.ReachHorizon(u);
            HexTile stop = f.Reachable(link) ? f.StepToward(link) : null;
            if (stop == null || stop == u.tile) return AIAction.Wait(u, "supply tree: no way to its link at (" + link.x + "," + link.y + ")");
            return AIAction.Move(u, stop, 0f, stop == link ? "supply tree: takes its link" : "supply tree: heads for its link at (" + link.x + "," + link.y + ")");
        }
    }
}
