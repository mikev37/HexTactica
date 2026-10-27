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
    /// units the chain keeps connected. Its links ride trucks where it has them, each at the farthest
    /// tile its truck keeps it linked from (a few walk), as a player builds a supply line.
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
            /// <summary>An ordinary front's (AIStrategic): the fallback, served after the campaign's.</summary>
            public bool ordinary;
            /// <summary>An owned FOB (or other supply building) the chain makes for first, cut off now, that brings supply nearer the front once joined.</summary>
            public Building fob;
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
        /// <summary>
        /// What each supply unit joined to the network is doing for the war: the fighting units supplied
        /// only through it — in contact (front-line combat) counting most, then on a campaign front — and
        /// a campaign front's chain links. The ordinary fronts' units count for nothing: they are not left
        /// behind on purpose, but nothing is kept for them. Only supply units with a duty are protected from
        /// being cut off (AISupplyChain.Harm, AIPlayer.KeepLinks).
        /// </summary>
        public readonly Dictionary<SupplyPoint, float> duty = new Dictionary<SupplyPoint, float>();
        public readonly HashSet<SupplyPoint> dutied = new HashSet<SupplyPoint>();
        /// <summary>The protected supply units this turn (dutied), for AISupplyChain.Harm and AIPlayer.KeepLinks.</summary>
        [System.ThreadStatic] public static HashSet<SupplyPoint> Protect;
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

        public static AILogistics Make(AIMap map, AICampaign campaign, ICollection<Unit> reservedTrucks) { return Make(map, campaign, reservedTrucks, null); }

        /// <param name="ordinary">The ordinary fronts (AIStrategic) of the units on no task force: each is a
        /// front for supply too, toward its objective (or where it stands).</param>
        public static AILogistics Make(AIMap map, AICampaign campaign, ICollection<Unit> reservedTrucks, List<Front> ordinary) {
            AILogistics l = new AILogistics();
            if (campaign == null) return l;
            List<CampaignObjective> fronts = new List<CampaignObjective>(campaign.objectives);
            if (ordinary != null)
                foreach (Front f in ordinary) {
                    if (f.campaign) continue;
                    HexTile at = f.objectiveTile ?? f.center;
                    if (at == null) continue;
                    CampaignObjective o = new CampaignObjective { kind = CampaignKind.Clear, target = at, went = true, priority = f.priority, ordinaryFront = true };
                    foreach (Unit u in f.assigned) if (u != null && u.Hub == null) o.units.Add(u);
                    fronts.Add(o);
                }
            foreach (CampaignObjective o in fronts) {
                if (o.kind == CampaignKind.Scout || o.target == null) continue;
                int n = Size(o);
                if (n > 0) l.tasks.Add(new Task { front = o, goal = Goal(o), size = n, ordinary = o.ordinaryFront });
            }
            // The campaign's fronts first, largest first; the ordinary fronts (the fallback, kept inside
            // our supply) after them. Between equals, the campaign's priority, then the board.
            l.tasks.Sort((a, b) => {
                if (a.ordinary != b.ordinary) return a.ordinary ? 1 : -1;
                int c = b.size.CompareTo(a.size); if (c != 0) return c;
                c = b.front.priority.CompareTo(a.front.priority); if (c != 0) return c;
                return CombatMath.CompareTiles(a.front.target, b.front.target);
            });
            foreach (Task t in l.tasks)
                foreach (Unit u in t.front.units) if (u != null && u.tile != null && u.Firepower > 0 && u.Hub == null && map.SupplyDepth(u.tile) <= 0) t.unsupplied += AIScore.Worth(u) * u.Strength;
            // Only supply units joined to the network now: one cut off moves as a unit (AISupplyPlanner),
            // which brings it back to our supply first.
            SupplyNetwork.Result now = SupplyNetwork.SolveAll(map.player, new Dictionary<SupplyPoint, HexTile>());
            List<SupplyPoint> pool = new List<SupplyPoint>();
            foreach (SupplyPoint h in map.ownHubs)
                if (h != null && !h.producer && h.myUnit != null && h.myUnit.tile != null && !LocomotionRules.IsNaval(h.myUnit.locomotion) && !LocomotionRules.IsAirborne(h.myUnit.locomotion)
                    && now.connectedHubs.Contains(h)) pool.Add(h);
            if (l.tasks.Count == 0) return l;

            // A supply building of ours cut off from the network — a FOB — that would carry supply nearer
            // the front once joined: the chain makes for it first (joined, it supplies like the HQ).
            foreach (Task t in l.tasks) {
                Dictionary<HexTile, int> toGoal = map.LandSteps(t.goal);
                int reachNow = int.MaxValue;
                foreach (KeyValuePair<HexTile, int> kv in toGoal) if (map.SupplyDepth(kv.Key) > 0 && kv.Value < reachNow) reachNow = kv.Value;
                if (reachNow == 0) continue;
                Building best = null; int bestGap = int.MaxValue;
                foreach (Building b in Building.all) {
                    if (b == null || b.owner != map.player || !b.producesSupply || b.IsHQ || b.tile == null || !b.Functioning) continue;
                    bool joined = false; foreach (HexTile x in b.Footprint) if (map.SupplyDepth(x) > 0) { joined = true; break; }
                    if (joined) continue;
                    int there; if (!toGoal.TryGetValue(b.tile, out there) || there >= reachNow - 2) continue;   // not nearer the front by much
                    // The one nearest our supply now: the least chain to join it.
                    Dictionary<HexTile, int> toB = map.LandSteps(b.tile);
                    int gap = int.MaxValue; foreach (KeyValuePair<HexTile, int> kv in toB) if (map.SupplyDepth(kv.Key) > 0 && kv.Value < gap) gap = kv.Value;
                    if (gap < bestGap || (gap == bestGap && best != null && CombatMath.CompareTiles(b.tile, best.tile) < 0)) { bestGap = gap; best = b; }
                }
                if (best != null) { t.fob = best; t.goal = best.tile; }
            }

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
            // First what each needs — one for every campaignUnitsPerSupply of its units, the largest front
            // first — then any left over by their number of units.
            int total = 0; foreach (Task t in l.tasks) total += t.size;
            int left = pool.Count;
            Dictionary<Task, int> share = new Dictionary<Task, int>();
            int given = 0;
            int per = Mathf.Max(1, map.profile.campaignUnitsPerSupply);
            foreach (Task t in l.tasks) { int want = Mathf.Min(Mathf.CeilToInt(t.size / (float)per), left - given); share[t] = want; given += want; }
            int extra = left - given, extraGiven = 0;
            foreach (Task t in l.tasks) { int e = Mathf.FloorToInt(extra * t.size / (float)Mathf.Max(1, total)); share[t] += e; extraGiven += e; }
            for (int i = 0; extraGiven < extra; i = (i + 1) % l.tasks.Count) { share[l.tasks[i]]++; extraGiven++; }
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

            // Duty (see duty): who each supply unit keeps supplied.
            HashSet<Unit> onFront = new HashSet<Unit>();
            foreach (Task t in l.tasks) if (!t.ordinary) foreach (Unit u in t.front.units) if (u != null) onFront.Add(u);
            SupplyNetwork.Result all = SupplyNetwork.SolveAll(map.player, new Dictionary<SupplyPoint, HexTile>());
            foreach (SupplyPoint h in all.connectedHubs) {
                if (h == null || h.producer || h.myUnit == null) continue;
                SupplyNetwork.Result w = SupplyNetwork.SolveWithout(map.player, h);
                float d = 0f;
                foreach (Unit u in map.ownUnits) {
                    if (u == null || u.tile == null || u.Firepower <= 0 || u.Hub != null) continue;
                    if (all.DepthAt(u.tile) <= 0 || w.DepthAt(u.tile) > 0) continue;   // not supplied, or supplied without it
                    if (map.TurnsToContact(u) <= 1) d += 3f;
                    else if (onFront.Contains(u)) d += 1f;
                }
                Task ct;
                if (l.chainOf.TryGetValue(h, out ct) && !ct.ordinary) d += 1f;
                l.duty[h] = d;
                if (d > 0f) l.dutied.Add(h);
            }
            // Those a dutied one hangs from have a duty too (cut them and it is cut).
            foreach (SupplyPoint h in new List<SupplyPoint>(l.duty.Keys)) {
                if (l.dutied.Contains(h)) continue;
                SupplyNetwork.Result w = SupplyNetwork.SolveWithout(map.player, h);
                foreach (SupplyPoint x in l.dutied) if (x != h && !w.connectedHubs.Contains(x)) { l.dutied.Add(h); break; }
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
                Dictionary<SupplyPoint, HexTile> drops = new Dictionary<SupplyPoint, HexTile>();
                HexTile dropFor = null;
                foreach (SupplyPoint h in t.tactical) {
                    HexTile from = h.myUnit.trasnporter != null ? h.myUnit.trasnporter.tile : h.myUnit.tile;
                    HexTile drop = DropNear(t, h.myUnit, map);
                    if (drop != null && map.Travel(h.myUnit, from, drop) > 2 * Mathf.Max(1, h.myUnit.moveRange)) { far.Add(h); drops[h] = drop; }
                    dropFor = drop;
                }
                far.Sort((a, b) => { int c = NearestOf(t, b.myUnit, map).CompareTo(NearestOf(t, a.myUnit, map)); return c != 0 ? c : AIMap.CompareUnits(a.myUnit, b.myUnit); });
                foreach (SupplyPoint h in far) {
                    Unit u = h.myUnit;
                    if (u.trasnporter != null && IsCarrier(u.trasnporter)) { trucks.Remove(u.trasnporter); l.rides[h] = new Ride { truck = u.trasnporter, to = drops[h], task = t }; continue; }
                    int walk = Mathf.CeilToInt(map.Travel(u, u.tile, drops[h]) / (float)Mathf.Max(1, u.moveRange));
                    Unit pick = null; int pd = int.MaxValue;
                    foreach (Unit tr in trucks) {
                        if (tr.transported.Count > 0 || !Transporting.CanCarry(tr, u)) continue;
                        // To beside it (its own tile is taken by it: the truck that had arrived beside it
                        // read as unable to reach it, lost its claim, and drove off).
                        int d = 99;
                        if (Transporting.CanBoardFrom(tr, u)) d = 0;
                        else foreach (HexTile n in u.tile.neighbors) if (n != null && (n.occupier == null || n.occupier == tr) && tr.CanEnter(n)) d = Mathf.Min(d, map.Travel(tr, tr.tile, n));
                        if (d >= 99) continue;
                        // Only if fetching it and driving it there beats walking.
                        int byTruck = Mathf.CeilToInt(d / (float)Mathf.Max(1, tr.moveRange)) + Mathf.CeilToInt(map.Travel(tr, u.tile, drops[h]) / (float)Mathf.Max(1, tr.moveRange));
                        if (byTruck >= walk) continue;
                        if (d < pd || (d == pd && pick != null && AIMap.CompareUnits(tr, pick) < 0)) { pd = d; pick = tr; }
                    }
                    if (pick == null) continue;
                    trucks.Remove(pick);
                    l.rides[h] = new Ride { truck = pick, to = drops[h], task = t };
                }
            }
            l.freeTrucks = trucks.Count;
            return l;
        }

        /// <summary>More than two of its moves from the nearest of the front's units.</summary>
        public static bool Far(Task t, Unit u, AIMap map) { return NearestOf(t, u, map) > 2 * Mathf.Max(1, u.moveRange); }

        /// <summary>
        /// Where a front's spare supply unit goes (on a truck, or walking). The front's chain short: to
        /// the back of the chain — the tile at the edge of the supply our HQ and buildings give out (no
        /// supply units), nearest the front's goal — where the next turn's chain takes it as its rear link
        /// and every link ahead of it moves up: backfilling the nearest, rather than carrying the new one
        /// all the way to the front. Otherwise the tile nearest the front's leading unit (the one nearest
        /// its goal) not ahead of it. Either way, one the enemy we know of cannot hit tonight.
        /// </summary>
        public static HexTile DropNear(Task t, Unit truck, AIMap map) {
            Dictionary<HexTile, int> toGoal = map.LandSteps(t.goal);
            System.Func<HexTile, int> steps = x => { int v; return x != null && toGoal.TryGetValue(x, out v) ? v : int.MaxValue; };
            if (t.gap) {
                // The back (the fixed network's edge) and the head (the whole network's edge, the chain
                // included), each the tile nearest the goal; the one nearer the supply unit itself.
                SupplyNetwork.Result fixedNet = SupplyNetwork.SolvePlanned(map.player, new Dictionary<SupplyPoint, HexTile>());
                HexTile back = Edge(toGoal, x => fixedNet.DepthAt(x) > 0, truck, map);
                HexTile head = Edge(toGoal, x => map.SupplyDepth(x) > 0, truck, map);
                Unit who = truck;
                HexTile from = who.trasnporter != null ? who.trasnporter.tile : who.tile;
                if (back != null && head != null) return map.Travel(who, from, head) < map.Travel(who, from, back) ? head : back;
                if (back != null || head != null) return back ?? head;
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

        /// <summary>Of the tiles where <paramref name="ours"/> holds, the one nearest the goal that <paramref name="u"/> may end on, empty and safe from the enemy we know of.</summary>
        static HexTile Edge(Dictionary<HexTile, int> toGoal, System.Func<HexTile, bool> ours, Unit u, AIMap map) {
            HexTile best = null; int bs = int.MaxValue;
            foreach (KeyValuePair<HexTile, int> kv in toGoal) {
                HexTile x = kv.Key;
                if (!ours(x) || !u.CanEnter(x) || !AIPath.CanFinishTurnOn(u, x)) continue;
                if (x.occupier != null && x.occupier != u && !(u.trasnporter != null && x.occupier == u.trasnporter)) continue;
                if (map.KnownLoss(u, x) > 0f) continue;
                if (kv.Value < bs || (kv.Value == bs && CombatMath.Precedes(x, best))) { bs = kv.Value; best = x; }
            }
            return best;
        }

        /// <summary>
        /// The chain toward <paramref name="t"/>'s goal of <paramref name="hubs"/>, those in
        /// <paramref name="moved"/> where they now stand; it keeps connected only <paramref name="connect"/>
        /// (its link nodes); its links ride trucks where that reaches further.
        /// </summary>
        public static AISupplyChain.Plan Chain(AIMap map, Task t, ICollection<SupplyPoint> hubs, ICollection<Unit> moved, ICollection<Unit> reservedTrucks, HashSet<SupplyPoint> connect) {
            HashSet<Unit> was = AISupplyChain.ReservedTrucks;
            HashSet<SupplyPoint> wasConnect = AISupplyChain.ConnectOnly;
            bool wasNoLeaps = AISupplyChain.NoLeaps;
            AISupplyChain.ReservedTrucks = reservedTrucks != null ? new HashSet<Unit>(reservedTrucks) : null;
            HashSet<SupplyPoint> wasProtect = AISupplyChain.ProtectOnly;
            if (Protect != null) AISupplyChain.ProtectOnly = Protect;
            AISupplyChain.ConnectOnly = connect;
            AISupplyChain.NoLeaps = false;   // its links ride trucks at the farthest range they stay linked from
            try { return AISupplyChain.Make(map, moved, t.goal, hubs, t.front.units); }
            finally { AISupplyChain.ReservedTrucks = was; AISupplyChain.ConnectOnly = wasConnect; AISupplyChain.NoLeaps = wasNoLeaps; AISupplyChain.ProtectOnly = wasProtect; }
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
                s.Add(t.front.Id + " (" + t.size + " units, to (" + t.goal.x + "," + t.goal.y + ")" + (t.fob != null ? " joining its " + t.fob.name.Replace("(Clone)", "") : "") + "): chain " + t.chain.Count + ", its own " + t.tactical.Count + (rode > 0 ? " (" + rode + " on trucks)" : "") + (t.gap ? ", chain short" : "") + (t.short_ ? ", nothing spare" : ""));
            }
            List<string> rs = new List<string>();
            foreach (KeyValuePair<SupplyPoint, Ride> kv in rides) rs.Add("(" + kv.Key.myUnit.tile.x + "," + kv.Key.myUnit.tile.y + ") on the truck at (" + kv.Value.truck.tile.x + "," + kv.Value.truck.tile.y + ") to (" + kv.Value.to.x + "," + kv.Value.to.y + ")");
            return "logistics: " + (s.Count > 0 ? string.Join("; ", s.ToArray()) : "no fronts") + "; free trucks " + freeTrucks + "; supply units with a duty " + dutied.Count + " of " + duty.Count + (rs.Count > 0 ? "; rides " + string.Join(", ", rs.ToArray()) : "");
        }
    }
}
