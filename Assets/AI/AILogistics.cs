using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Supply for the campaign's fronts (AIProfile.campaignLogistics), planned at the start of each turn.
    ///
    /// The fronts (task forces with units) in order of their size, largest first. For each, the
    /// logistics chain comes first: from the static network (HQ and our buildings) toward the front
    /// (its staging ground while it gathers, its objective once it has gone in), of the supply units
    /// not yet given to an earlier front's chain, and never ahead of that front's units (AISupplyChain).
    /// The chain takes only the supply units that bring supply nearer the front — none when the front
    /// is already on or beside supplied ground; its links are the front's link nodes, the only supply
    /// units the chain keeps connected. The chain walks; it does not leap.
    ///
    /// The supply units left over after every chain are the fronts' own, shared out by the number of
    /// units in each front (the largest first takes any remainder). Each heads to its front — on a free
    /// truck if one can take it (claimed now, before any fighting unit picks a ride: the first supply
    /// unit aboard a vehicle gains more from it than anyone; a second gains nothing, so one each) — and,
    /// once near it, is placed by the ordinary tactical supply placement (AISupplyPlanner) to serve the
    /// units around it.
    /// </summary>
    public class AILogistics {

        public class Task {
            public CampaignObjective front;
            public HexTile goal;
            public int size;
            public readonly List<SupplyPoint> chain = new List<SupplyPoint>();
            public readonly List<SupplyPoint> tactical = new List<SupplyPoint>();
            /// <summary>The chain does not bring supply to the front this turn (spare supply goes to its head).</summary>
            public bool gap;
            /// <summary>... and there is no spare supply unit to send it either (buying pressure: AIProduction).</summary>
            public bool short_;
            /// <summary>Power (worth times strength, the scale AIProduction values fighting units on) of the front's fighting units standing outside supply.</summary>
            public float unsupplied;
        }

        /// <summary>A truck claimed for one of a front's own supply units, and where it takes it.</summary>
        public class Ride { public Unit truck; public HexTile to; public Task task; }

        public readonly List<Task> tasks = new List<Task>();
        public readonly Dictionary<SupplyPoint, Task> chainOf = new Dictionary<SupplyPoint, Task>();
        public readonly Dictionary<SupplyPoint, Task> tacticalOf = new Dictionary<SupplyPoint, Task>();
        public readonly Dictionary<SupplyPoint, Ride> rides = new Dictionary<SupplyPoint, Ride>();
        /// <summary>Free trucks after the claims.</summary>
        public int freeTrucks;

        /// <summary>Where a front's supply goes: its staging ground while it gathers, its objective once it has gone in.</summary>
        public static HexTile Goal(CampaignObjective o) { return o.went || o.staging == null ? o.target : o.staging; }

        static int Size(CampaignObjective o) {
            int n = 0;
            foreach (Unit u in o.units) if (u != null && u.tile != null && u.Firepower > 0 && u.Hub == null) n++;
            return n;
        }

        public static bool IsCarrier(Unit t) {
            return t != null && t.tile != null && Transporting.IsTransport(t) && t.Firepower <= 1 && !LocomotionRules.IsAirborne(t.locomotion) && !LocomotionRules.IsNaval(t.locomotion);
        }

        public static AILogistics Make(AIMap map, AICampaign campaign, ICollection<Unit> reservedTrucks) {
            AILogistics l = new AILogistics();
            if (campaign == null) return l;
            foreach (CampaignObjective o in campaign.objectives) {
                if (o.kind == CampaignKind.Scout || o.target == null) continue;
                int n = Size(o);
                if (n > 0) l.tasks.Add(new Task { front = o, goal = Goal(o), size = n });
            }
            // Largest first; between equals, the campaign's priority, then the board.
            l.tasks.Sort((a, b) => {
                int c = b.size.CompareTo(a.size); if (c != 0) return c;
                c = b.front.priority.CompareTo(a.front.priority); if (c != 0) return c;
                return CombatMath.CompareTiles(a.front.target, b.front.target);
            });
            foreach (Task t in l.tasks)
                foreach (Unit u in t.front.units) if (u != null && u.tile != null && u.Firepower > 0 && u.Hub == null && map.SupplyDepth(u.tile) <= 0) t.unsupplied += AIScore.Worth(u) * u.Strength;
            List<SupplyPoint> pool = new List<SupplyPoint>();
            foreach (SupplyPoint h in map.ownHubs)
                if (h != null && !h.producer && h.myUnit != null && h.myUnit.tile != null && !LocomotionRules.IsNaval(h.myUnit.locomotion) && !LocomotionRules.IsAirborne(h.myUnit.locomotion)) pool.Add(h);
            if (l.tasks.Count == 0) return l;

            // The logistics chains first, largest front first. Choosing them, the chain connects no one
            // (it is choosing who its links are).
            foreach (Task t in l.tasks) {
                if (pool.Count == 0) { t.gap = t.short_ = t.unsupplied > 0f; continue; }
                AISupplyChain.Plan p = Chain(map, t, pool, null, reservedTrucks, new HashSet<SupplyPoint>());
                foreach (SupplyPoint h in p.links.Keys) { if (!pool.Contains(h)) continue; t.chain.Add(h); l.chainOf[h] = t; }
                foreach (SupplyPoint h in t.chain) pool.Remove(h);
                t.gap = p.reachSteps > 0 && t.unsupplied > 0f;
            }

            // The rest: the fronts' own, by their number of units, the largest first taking any remainder.
            int total = 0; foreach (Task t in l.tasks) total += t.size;
            int left = pool.Count;
            Dictionary<Task, int> share = new Dictionary<Task, int>();
            int given = 0;
            foreach (Task t in l.tasks) { int s = Mathf.FloorToInt(left * t.size / (float)Mathf.Max(1, total)); share[t] = s; given += s; }
            for (int i = 0; given < left; i = (i + 1) % l.tasks.Count) { share[l.tasks[i]]++; given++; }
            foreach (Task t in l.tasks) {
                for (int k = 0; k < share[t] && pool.Count > 0; k++) {
                    // The nearest to the front's units.
                    SupplyPoint best = null; int bd = int.MaxValue;
                    foreach (SupplyPoint h in pool) {
                        int d = NearestOf(t, h.myUnit, map);
                        if (d < bd || (d == bd && best != null && AIMap.CompareUnits(h.myUnit, best.myUnit) < 0)) { bd = d; best = h; }
                    }
                    if (best == null) break;
                    t.tactical.Add(best); l.tacticalOf[best] = t; pool.Remove(best);
                }
            }

            // Buying pressure: a gap with no spare supply unit going to it.
            foreach (Task t in l.tasks) t.short_ = t.gap && t.tactical.Count == 0;

            // Rides for the fronts' own that are far from their front: the nearest free truck that can
            // take it, one supply unit to a truck, the largest front's first, the farthest first.
            List<Unit> trucks = new List<Unit>();
            foreach (Unit u in map.ownUnits) if (IsCarrier(u) && u.active && (reservedTrucks == null || !reservedTrucks.Contains(u))) trucks.Add(u);
            trucks.Sort(AIMap.CompareUnits);
            foreach (Task t in l.tasks) {
                // Far: more than two of its moves from where a truck would set it down.
                List<SupplyPoint> far = new List<SupplyPoint>();
                HexTile dropFor = null;
                foreach (SupplyPoint h in t.tactical) {
                    HexTile from = h.myUnit.trasnporter != null ? h.myUnit.trasnporter.tile : h.myUnit.tile;
                    HexTile drop = DropNear(t, h.myUnit, map);
                    if (drop != null && map.Travel(h.myUnit, from, drop) > 2 * Mathf.Max(1, h.myUnit.moveRange)) far.Add(h);
                    dropFor = drop;
                }
                far.Sort((a, b) => { int c = NearestOf(t, b.myUnit, map).CompareTo(NearestOf(t, a.myUnit, map)); return c != 0 ? c : AIMap.CompareUnits(a.myUnit, b.myUnit); });
                foreach (SupplyPoint h in far) {
                    Unit u = h.myUnit;
                    if (u.trasnporter != null && IsCarrier(u.trasnporter)) { trucks.Remove(u.trasnporter); l.rides[h] = new Ride { truck = u.trasnporter, to = DropNear(t, u.trasnporter, map), task = t }; continue; }
                    Unit pick = null; int pd = int.MaxValue;
                    foreach (Unit tr in trucks) {
                        if (tr.transported.Count > 0 || !Transporting.CanCarry(tr, u)) continue;
                        // To beside it (its own tile is taken by it: the truck that had arrived beside it
                        // read as unable to reach it, lost its claim, and drove off).
                        int d = 99;
                        if (Transporting.CanBoardFrom(tr, u)) d = 0;
                        else foreach (HexTile n in u.tile.neighbors) if (n != null && (n.occupier == null || n.occupier == tr) && tr.CanEnter(n)) d = Mathf.Min(d, map.Travel(tr, tr.tile, n));
                        if (d >= 99) continue;
                        if (d < pd || (d == pd && pick != null && AIMap.CompareUnits(tr, pick) < 0)) { pd = d; pick = tr; }
                    }
                    if (pick == null) continue;
                    trucks.Remove(pick);
                    l.rides[h] = new Ride { truck = pick, to = DropNear(t, pick, map), task = t };
                }
            }
            l.freeTrucks = trucks.Count;
            return l;
        }

        /// <summary>More than two of its moves from the nearest of the front's units.</summary>
        public static bool Far(Task t, Unit u, AIMap map) { return NearestOf(t, u, map) > 2 * Mathf.Max(1, u.moveRange); }

        /// <summary>
        /// Where a truck sets a front's supply unit down. The front's chain short: at its head — the
        /// supplied tile nearest the front's goal — to go on from there as the chain's next link (the
        /// spare supply becomes more of the chain). Otherwise the tile nearest the front's leading unit
        /// (the one nearest its goal) not ahead of it. Either way, one the enemy we know of cannot hit tonight.
        /// </summary>
        static HexTile DropNear(Task t, Unit truck, AIMap map) {
            Dictionary<HexTile, int> toGoal = map.LandSteps(t.goal);
            System.Func<HexTile, int> steps = x => { int v; return x != null && toGoal.TryGetValue(x, out v) ? v : int.MaxValue; };
            if (t.gap) {
                HexTile head = null; int hs = int.MaxValue;
                foreach (KeyValuePair<HexTile, int> kv in toGoal) {
                    HexTile x = kv.Key;
                    if (map.SupplyDepth(x) <= 0 || !truck.CanEnter(x) || !AIPath.CanFinishTurnOn(truck, x)) continue;
                    if (x.occupier != null && x.occupier != truck) continue;
                    if (map.KnownLoss(truck, x) > 0f) continue;
                    if (kv.Value < hs || (kv.Value == hs && CombatMath.Precedes(x, head))) { hs = kv.Value; head = x; }
                }
                if (head != null) return head;
            }
            int front = int.MaxValue; HexTile lead = null;
            foreach (Unit v in t.front.units) if (v != null && v.tile != null && v.Firepower > 0 && v.Hub == null) { int s = steps(v.tile); if (s < front) { front = s; lead = v.tile; } }
            if (lead == null) return t.goal;
            Dictionary<HexTile, int> toLead = map.LandSteps(lead);
            HexTile best = null; int bd = int.MaxValue;
            foreach (KeyValuePair<HexTile, int> kv in toLead) {
                HexTile x = kv.Key;
                if (kv.Value > 6 || steps(x) < front || !truck.CanEnter(x) || !AIPath.CanFinishTurnOn(truck, x)) continue;
                if (x.occupier != null && x.occupier != truck) continue;
                if (map.KnownLoss(truck, x) > 0f) continue;
                if (kv.Value < bd || (kv.Value == bd && CombatMath.Precedes(x, best))) { bd = kv.Value; best = x; }
            }
            return best ?? lead;
        }

        /// <summary>
        /// The chain toward <paramref name="t"/>'s goal of <paramref name="hubs"/>, those in
        /// <paramref name="moved"/> where they now stand; it keeps connected only <paramref name="connect"/>
        /// (its link nodes) and does not leap.
        /// </summary>
        public static AISupplyChain.Plan Chain(AIMap map, Task t, ICollection<SupplyPoint> hubs, ICollection<Unit> moved, ICollection<Unit> reservedTrucks, HashSet<SupplyPoint> connect) {
            HashSet<Unit> was = AISupplyChain.ReservedTrucks;
            HashSet<SupplyPoint> wasConnect = AISupplyChain.ConnectOnly;
            bool wasNoLeaps = AISupplyChain.NoLeaps;
            AISupplyChain.ReservedTrucks = reservedTrucks != null ? new HashSet<Unit>(reservedTrucks) : null;
            AISupplyChain.ConnectOnly = connect;
            AISupplyChain.NoLeaps = true;
            try { return AISupplyChain.Make(map, moved, t.goal, hubs, t.front.units); }
            finally { AISupplyChain.ReservedTrucks = was; AISupplyChain.ConnectOnly = wasConnect; AISupplyChain.NoLeaps = wasNoLeaps; }
        }

        /// <summary>Travel from <paramref name="u"/> to the nearest of the front's units (and the tile it is nearest).</summary>
        public static int NearestOf(Task t, Unit u, AIMap map) { HexTile at; return NearestOf(t, u, map, out at); }

        public static int NearestOf(Task t, Unit u, AIMap map, out HexTile at) {
            at = null; int bd = int.MaxValue;
            HexTile from = u.trasnporter != null ? u.trasnporter.tile : u.tile;
            foreach (Unit v in t.front.units) {
                if (v == null || v.tile == null || v.Firepower <= 0 || v.Hub != null) continue;
                int d = map.Travel(u, from, v.tile);
                if (d < bd || (d == bd && at != null && CombatMath.Precedes(v.tile, at))) { bd = d; at = v.tile; }
            }
            return bd;
        }

        public override string ToString() {
            List<string> s = new List<string>();
            foreach (Task t in tasks) {
                int rode = 0; foreach (Ride r in rides.Values) if (r.task == t) rode++;
                s.Add(t.front.Id + " (" + t.size + " units, to (" + t.goal.x + "," + t.goal.y + ")): chain " + t.chain.Count + ", its own " + t.tactical.Count + (rode > 0 ? " (" + rode + " on trucks)" : "") + (t.gap ? ", chain short" : "") + (t.short_ ? ", nothing spare" : ""));
            }
            List<string> rs = new List<string>();
            foreach (KeyValuePair<SupplyPoint, Ride> kv in rides) rs.Add("(" + kv.Key.myUnit.tile.x + "," + kv.Key.myUnit.tile.y + ") on the truck at (" + kv.Value.truck.tile.x + "," + kv.Value.truck.tile.y + ") to (" + kv.Value.to.x + "," + kv.Value.to.y + ")");
            return "logistics: " + (s.Count > 0 ? string.Join("; ", s.ToArray()) : "no fronts") + "; free trucks " + freeTrucks + (rs.Count > 0 ? "; rides " + string.Join(", ", rs.ToArray()) : "");
        }
    }
}
