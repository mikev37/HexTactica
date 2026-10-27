using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Supply as a player runs it (AIProfile.supplyBackbone): most supply units ride a transport — a
    /// truck or an APC, whichever the ground suits — as moving links, spaced as far apart as a
    /// transport's move lets them stay linked (a supply unit aboard is linked while its transport
    /// could reach supplied ground in one move, and spreads its supply from the transport's tile), from
    /// the network the HQ and our buildings give out to each group of the army; and a few on foot
    /// beside the army, for the hills and forest the transports cross badly (the chain places those).
    /// Supply serves the army: the backbone goes to where each group of fighting units is, nearest
    /// group first, never ahead of it, and no link stops where the enemy can strike it next turn.
    ///
    /// Each turn, from the static network: for the group whose front (its unit nearest the enemy HQ)
    /// is not supplied, the next link goes on the tile nearest that front that a transport there would
    /// keep linked, safe, and not past the front; the supply unit and transport that can get there
    /// soonest take it (one already aboard first). Then the network is worked out again with it there,
    /// and so on, until every group is supplied or no pair is left. A link out of reach this turn
    /// heads there; one not yet aboard boards first.
    /// </summary>
    public static class AISupplyBackbone {

        public class Link {
            public SupplyPoint hub;
            public Unit transport;
            public HexTile tile;
        }

        public class Plan {
            public readonly Dictionary<SupplyPoint, Link> links = new Dictionary<SupplyPoint, Link>();
            public readonly HashSet<Unit> transports = new HashSet<Unit>();
            public override string ToString() {
                List<string> s = new List<string>();
                foreach (Link l in links.Values) s.Add("(" + l.hub.myUnit.tile.x + "," + l.hub.myUnit.tile.y + ")" + (l.hub.myUnit.trasnporter == l.transport ? " aboard" : "") + "->(" + l.tile.x + "," + l.tile.y + ")");
                return "supply backbone: " + (s.Count > 0 ? string.Join(", ", s.ToArray()) : "none");
            }
        }

        public static System.Action<string> DebugLog;

        static bool IsCarrier(Unit t) { return t != null && t.tile != null && Transporting.IsTransport(t) && t.Firepower <= 1 && !LocomotionRules.IsAirborne(t.locomotion) && !LocomotionRules.IsNaval(t.locomotion); }

        /// <summary>Groups of our fighting units on the ground (each linked to the next within 3 tiles).</summary>
        public static List<List<Unit>> Groups(AIMap map) {
            List<Unit> army = new List<Unit>();
            foreach (Unit u in map.ownUnits)
                if (u != null && u.tile != null && u.trasnporter == null && u.Firepower > 0 && u.Hub == null && !IsCarrier(u) && !LocomotionRules.NeedsBase(u.locomotion)
                    && !AICampaign.HomeFight(u, map, map.profile)) army.Add(u);
            army.Sort(AIMap.CompareUnits);
            List<List<Unit>> groups = new List<List<Unit>>();
            HashSet<Unit> seen = new HashSet<Unit>();
            foreach (Unit u in army) {
                if (seen.Contains(u)) continue;
                List<Unit> g = new List<Unit> { u }; seen.Add(u);
                for (int i = 0; i < g.Count; i++)
                    foreach (Unit v in army) if (!seen.Contains(v) && v.tile.Distance(g[i].tile) <= 3) { seen.Add(v); g.Add(v); }
                groups.Add(g);
            }
            return groups;
        }

        public static Plan Make(AIMap map, ICollection<Unit> reserved) {
            Plan plan = new Plan();
            HexTile objective = AISupplyChain.Objective(map);
            if (objective == null) return plan;
            Dictionary<HexTile, int> toObj = map.LandSteps(objective);
            System.Func<HexTile, int> steps = t => { int v; return t != null && toObj.TryGetValue(t, out v) ? v : int.MaxValue; };

            // Supply units and the transports they could ride.
            List<SupplyPoint> hubs = new List<SupplyPoint>();
            foreach (SupplyPoint h in map.ownHubs)
                if (h != null && !h.producer && h.myUnit != null && h.myUnit.tile != null && !LocomotionRules.IsNaval(h.myUnit.locomotion) && !LocomotionRules.IsAirborne(h.myUnit.locomotion)
                    && (h.myUnit.trasnporter == null || IsCarrier(h.myUnit.trasnporter))) hubs.Add(h);
            List<Unit> carriers = new List<Unit>();
            foreach (Unit t in map.ownUnits) if (IsCarrier(t) && (reserved == null || !reserved.Contains(t))) carriers.Add(t);
            if (hubs.Count == 0 || carriers.Count == 0) return plan;

            // The groups of the army, nearest the HQ first, each by its front.
            List<List<Unit>> groups = Groups(map);
            List<HexTile> fronts = new List<HexTile>();
            foreach (List<Unit> g in groups) {
                HexTile f = null; int fs = int.MaxValue;
                foreach (Unit u in g) { int s = steps(u.tile); if (f == null || s < fs || (s == fs && CombatMath.Precedes(u.tile, f))) { fs = s; f = u.tile; } }
                if (f != null) fronts.Add(f);
            }
            if (fronts.Count == 0) return plan;

            Dictionary<SupplyPoint, HexTile> placed = new Dictionary<SupplyPoint, HexTile>();
            Dictionary<SupplyPoint, Unit> aboard = new Dictionary<SupplyPoint, Unit>();
            Dictionary<SupplyPoint, Unit> was = SupplyNetwork.PlannedAboard;
            SupplyNetwork.PlannedAboard = aboard;
            try {
                SupplyNetwork.Result net = SupplyNetwork.SolvePlanned(map.player, placed);
                HashSet<HexTile> taken = new HashSet<HexTile>();
                HashSet<Unit> usedCarriers = new HashSet<Unit>();
                float caution = Mathf.Max(1f, map.profile.loadedTransportRisk);
                int guard = 0;
                bool progress = true;
                while (progress && guard++ < 32) {
                    progress = false;
                    // The group front still unsupplied, the one the network is nearest first.
                    HexTile target = null; int targetGap = int.MaxValue;
                    foreach (HexTile f in fronts) {
                        if (net.DepthAt(f) > 0) continue;
                        Dictionary<HexTile, int> toF = map.LandSteps(f);
                        int gap = int.MaxValue;
                        foreach (KeyValuePair<HexTile, int> kv in net.depth) { int d; if (kv.Value > 0 && toF.TryGetValue(kv.Key, out d) && d < gap) gap = d; }
                        if (target == null || gap < targetGap || (gap == targetGap && CombatMath.Precedes(f, target))) { targetGap = gap; target = f; }
                    }
                    if (target == null || targetGap == int.MaxValue) break;
                    Dictionary<HexTile, int> toTarget = map.LandSteps(target);
                    int frontSteps = steps(target);

                    // The pair (supply unit, transport) and the link tile: a tile nearest the group's front
                    // that a transport there keeps linked, safe tonight and beyond the enemy's strike next
                    // turn, and not past the front.
                    SupplyPoint bestHub = null; Unit bestCarrier = null; HexTile bestTile = null;
                    int bestGap = int.MaxValue, bestCost = int.MaxValue;
                    foreach (Unit c in carriers) {
                        if (usedCarriers.Contains(c)) continue;
                        // Its supply unit: the one aboard it, else the nearest free one on foot it can carry.
                        SupplyPoint h = null; int pickup = 0;
                        foreach (Unit cargo in c.transported) if (cargo != null && cargo.Hub != null && hubs.Contains(cargo.Hub) && !placed.ContainsKey(cargo.Hub)) { h = cargo.Hub; break; }
                        if (h == null) {
                            if (c.transported.Count > 0) continue;
                            int nd = int.MaxValue;
                            foreach (SupplyPoint x in hubs) {
                                if (placed.ContainsKey(x) || x.myUnit.trasnporter != null || !Transporting.CanCarry(c, x.myUnit)) continue;
                                int d = map.Travel(c, c.tile, x.myUnit.tile);
                                if (d < nd || (d == nd && h != null && AIMap.CompareUnits(x.myUnit, h.myUnit) < 0)) { nd = d; h = x; }
                            }
                            if (h == null || nd >= 99) continue;
                            pickup = nd;
                        }
                        // The link tile for this transport.
                        foreach (KeyValuePair<HexTile, int> kv in toTarget) {
                            HexTile x = kv.Key;
                            int gap = kv.Value;
                            if (gap > bestGap) continue;
                            if (taken.Contains(x) || (x.occupier != null && x.occupier != c) || !c.CanEnter(x) || !AIPath.CanFinishTurnOn(c, x)) continue;
                            if (steps(x) < frontSteps) continue;                       // not past the army
                            if (map.EnemyStrikeTurns(x) <= 1) continue;               // out of reach next turn
                            if (map.ExpectedLoss(c, x) * caution >= c.Strength) continue;
                            if (!SupplyNetwork.WithinMoveOfSupply(c, x, net)) continue;
                            int cost = pickup + map.Travel(c, c.tile, x);
                            if (cost >= 99) continue;
                            if (gap < bestGap || (gap == bestGap && (cost < bestCost || (cost == bestCost && (bestTile == null || CombatMath.Precedes(x, bestTile)))))) {
                                bestGap = gap; bestCost = cost; bestTile = x; bestHub = h; bestCarrier = c;
                            }
                        }
                    }
                    if (bestTile == null) break;
                    // Only if it brings the network nearer that front.
                    placed[bestHub] = bestTile; aboard[bestHub] = bestCarrier;
                    SupplyNetwork.Result after = SupplyNetwork.SolvePlanned(map.player, placed);
                    int newGap = int.MaxValue;
                    foreach (KeyValuePair<HexTile, int> kv in after.depth) { int d; if (kv.Value > 0 && toTarget.TryGetValue(kv.Key, out d) && d < newGap) newGap = d; }
                    if (!after.connectedHubs.Contains(bestHub) || newGap >= targetGap) { placed.Remove(bestHub); aboard.Remove(bestHub); fronts.Remove(target); progress = fronts.Count > 0; continue; }
                    net = after;
                    taken.Add(bestTile); usedCarriers.Add(bestCarrier);
                    plan.links[bestHub] = new Link { hub = bestHub, transport = bestCarrier, tile = bestTile };
                    plan.transports.Add(bestCarrier);
                    progress = true;
                }
            } finally { SupplyNetwork.PlannedAboard = was; }
            if (DebugLog != null) DebugLog("   " + plan);
            return plan;
        }
    }
}
