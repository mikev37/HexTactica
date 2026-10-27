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
    /// A link stands only where nothing can hit it tonight (or little: profile.chainRiskTolerance),
    /// the least exposed of the tiles that reach furthest, never past the frontmost of our
    /// fighting units (the army leads, the supply follows), and only where it pushes the supplied
    /// ground further toward the objective than the links behind it already do. A supply unit with
    /// nothing to add is spare: the ordinary supply planner (AISupplyPlanner) places it.
    ///
    /// "Forward" is measured over land (AIMap.LandSteps), so a road that winds around water is
    /// followed rather than cut across.
    /// </summary>
    public static class AISupplyChain {

        /// <summary>Diagnostic: why each supply unit does or does not take a place in the chain.</summary>
        public static System.Action<string> DebugLog;

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
            /// <summary>Links that ride a truck this turn (AIProfile.supplyLeaps): the truck, where it stops with the supply unit aboard, and where the supply unit boards it from (null: aboard already).</summary>
            public readonly Dictionary<SupplyPoint, Leap> leaps = new Dictionary<SupplyPoint, Leap>();

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

        public class Leap { public Unit truck; public HexTile tile; public HexTile board; }

        /// <summary>Trucks already promised this turn (AIPlayer): not for a leap.</summary>
        public static HashSet<Unit> ReservedTrucks;

        static bool IsTruck(Unit t) { return t != null && t.tile != null && Transporting.IsTransport(t) && t.Firepower <= 1; }

        /// <summary>
        /// A supply leap for <paramref name="h"/>: the truck it is aboard, or a free truck it can board
        /// this turn, and the tile furthest toward the objective where that truck stops tonight with it
        /// aboard and still linked to <paramref name="net"/> (within a move of supplied ground), safe,
        /// and not ahead of the army. Null: none.
        /// </summary>
        static Leap FindLeap(SupplyPoint h, AIMap map, SupplyNetwork.Result net, System.Func<HexTile, int> steps, int front, HashSet<Unit> used, HashSet<HexTile> taken) {
            Unit u = h.myUnit;
            Unit truck = u.trasnporter;
            HexTile board = null;
            if (truck != null) { if (!IsTruck(truck) || truck.transported.Count > 1) return null; }
            else {
                MoveField walk = map.ReachThisTurn(u);
                int bestD = int.MaxValue;
                foreach (Unit t in map.ownUnits) {
                    if (!IsTruck(t) || t.transported.Count > 0 || !t.active || used.Contains(t) || (ReservedTrucks != null && ReservedTrucks.Contains(t)) || !Transporting.CanCarry(t, u)) continue;
                    HexTile b = null;
                    if (Transporting.CanBoardFrom(t, u)) b = u.tile;
                    else foreach (HexTile n in t.tile.neighbors) {
                        if (n == null || n.occupier != null || taken.Contains(n) || !walk.ReachableThisTurn(n) || !Transporting.CanBoardOnto(u, n, t.tile, t)) continue;
                        if (b == null || CombatMath.Precedes(n, b)) b = n;
                    }
                    if (b == null) continue;
                    int d = u.tile.Distance(t.tile);
                    if (d < bestD || (d == bestD && AIMap.CompareUnits(t, truck) < 0)) { bestD = d; truck = t; board = b; }
                }
                if (truck == null) return null;
            }
            MoveField drive = map.ReachThisTurn(truck);
            float caution = Mathf.Max(1f, map.profile.loadedTransportRisk), tolerance = Mathf.Max(0f, map.profile.chainRiskTolerance);
            HexTile best = null; int bestSteps = int.MaxValue;
            int nT = 0, nOcc = 0, nAhead = 0, nRisk = 0, nUnlinked = 0;
            foreach (HexTile x in drive.Tiles) {
                nT++;
                if (x != truck.tile && (!drive.ReachableThisTurn(x) || !AIPath.CanFinishTurnOn(truck, x))) continue;
                if (taken.Contains(x) || (x != truck.tile && x.occupier != null)) { nOcc++; continue; }
                int sx = steps(x);
                if (sx == int.MaxValue || sx > bestSteps || (sx == bestSteps && !CombatMath.Precedes(x, best))) continue;
                // Supply serves the army: never ahead of it. (Carried out in front where nothing could strike
                // it yet, it fed itself to the enemy and left the army behind a gap it would not cross.)
                if (sx < front && !(map.profile.leapsAheadWhenClear && map.EnemyStrikeTurns(x) > 2)) { nAhead++; continue; }
                float loss = map.ExpectedLoss(truck, x);
                if (loss * caution >= truck.Strength || loss > tolerance) { nRisk++; continue; }
                if (!SupplyNetwork.WithinMoveOfSupply(truck, x, net)) { nUnlinked++; continue; }
                best = x; bestSteps = sx;
            }
            if (DebugLog != null && best == null) DebugLog("   chain: leap with the truck at (" + truck.tile.x + "," + truck.tile.y + "): " + nT + " tiles, " + nOcc + " taken, " + nAhead + " ahead of the army, " + nRisk + " risky, " + nUnlinked + " not linked");
            return best == null ? null : new Leap { truck = truck, tile = best, board = board };
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
        public static Plan Make(AIMap map, ICollection<Unit> fixedHubs) { return Make(map, fixedHubs, null, null, null); }

        /// <summary>
        /// A chain for one task force (AICampaign): toward <paramref name="objective"/>, of only the
        /// supply units in <paramref name="only"/>, never ahead of <paramref name="army"/>. Nulls: the
        /// whole army's chain toward the nearest enemy HQ.
        /// </summary>
        public static Plan Make(AIMap map, ICollection<Unit> fixedHubs, HexTile objective, ICollection<SupplyPoint> only, ICollection<Unit> army) {
            Plan plan = MakeOne(map, fixedHubs, objective, only, army, null);
            if (!map.profile.supplyLeaps || !plan.short_ || plan.reachSteps == 0) return plan;
            // Stretched, with trucks to hand: try each supply unit on foot that a truck could take now
            // carried forward, and the rest of the chain planned after it. A chain of links on foot has
            // every link holding the next: none could leap alone without cutting the ones ahead, though
            // one truck past the rear link does what three on foot did, freeing them to go further.
            Plan best = plan;
            int cut = CutAfter(plan, map), army0 = ArmySupplied(plan, map);
            foreach (SupplyPoint h in map.ownHubs) {
                if (!IsLink(h) || (only != null && !only.Contains(h)) || (fixedHubs != null && fixedHubs.Contains(h.myUnit))) continue;
                if (!TruckBeside(h, map)) continue;
                Plan alt = MakeOne(map, fixedHubs, objective, only, army, h);
                int altCut = alt.leaps.ContainsKey(h) ? CutAfter(alt, map) : -1;
                if (DebugLog != null) DebugLog("   chain: leap for (" + h.myUnit.tile.x + "," + h.myUnit.tile.y + ") " + (alt.leaps.ContainsKey(h) ? "to (" + alt.leaps[h].tile.x + "," + alt.leaps[h].tile.y + "): reach " + alt.reachSteps + " vs " + plan.reachSteps + ", cut " + altCut + " vs " + cut + ", spare " + alt.spare.Count : "none"));
                if (!alt.leaps.ContainsKey(h)) continue;
                if (altCut > cut) continue;
                // Not by leaving the army where it stands without supply: the units behind the gap a
                // leap opened starved, turned back for supply, and the army ended twenty steps back.
                if (ArmySupplied(alt, map) < army0) continue;
                if (alt.reachSteps < best.reachSteps || (alt.reachSteps == best.reachSteps && alt.spare.Count > best.spare.Count)) { best = alt; }
            }
            return best;
        }

        /// <summary>A free truck stands beside (or within this turn's walk of a tile beside) <paramref name="h"/>.</summary>
        static bool TruckBeside(SupplyPoint h, AIMap map) {
            foreach (Unit t in map.ownUnits) {
                if (!IsTruck(t) || t.transported.Count > 0 || !t.active || (ReservedTrucks != null && ReservedTrucks.Contains(t))) continue;
                if (t.tile.Distance(h.myUnit.tile) <= h.myUnit.moveRange + 1) return true;
            }
            return false;
        }

        /// <summary>Our fighting units (where they stand) the plan's network supplies.</summary>
        static int ArmySupplied(Plan plan, AIMap map) {
            Dictionary<SupplyPoint, HexTile> placed = new Dictionary<SupplyPoint, HexTile>(plan.links);
            Dictionary<SupplyPoint, Unit> aboard = new Dictionary<SupplyPoint, Unit>();
            foreach (KeyValuePair<SupplyPoint, Leap> kv in plan.leaps) aboard[kv.Key] = kv.Value.truck;
            Dictionary<SupplyPoint, Unit> was = SupplyNetwork.PlannedAboard;
            SupplyNetwork.PlannedAboard = aboard;
            try {
                SupplyNetwork.Result net = SupplyNetwork.SolvePlanned(map.player, placed);
                int n = 0;
                foreach (Unit u in map.ownUnits) if (u != null && u.tile != null && u.Firepower > 0 && u.Hub == null && u.trasnporter == null && net.DepthAt(u.tile) > 0) n++;
                return n;
            } finally { SupplyNetwork.PlannedAboard = was; }
        }

        /// <summary>Our supply units the plan's network leaves cut off.</summary>
        static int CutAfter(Plan plan, AIMap map) {
            Dictionary<SupplyPoint, HexTile> placed = new Dictionary<SupplyPoint, HexTile>(plan.links);
            Dictionary<SupplyPoint, Unit> aboard = new Dictionary<SupplyPoint, Unit>();
            foreach (KeyValuePair<SupplyPoint, Leap> kv in plan.leaps) aboard[kv.Key] = kv.Value.truck;
            Dictionary<SupplyPoint, Unit> was = SupplyNetwork.PlannedAboard;
            SupplyNetwork.PlannedAboard = aboard;
            try {
                SupplyNetwork.Result net = SupplyNetwork.SolvePlanned(map.player, placed);
                int n = 0;
                foreach (SupplyPoint h in map.ownHubs)
                    if (h != null && !h.producer && h.myUnit != null && h.myUnit.tile != null && !net.connectedHubs.Contains(h)) n++;
                return n;
            } finally { SupplyNetwork.PlannedAboard = was; }
        }

        static Plan MakeOne(AIMap map, ICollection<Unit> fixedHubs, HexTile objective, ICollection<SupplyPoint> only, ICollection<Unit> army, SupplyPoint forceLeap) {
            Plan plan = new Plan();
            plan.objective = objective ?? Objective(map);
            if (plan.objective == null) return plan;
            Dictionary<HexTile, int> toObj = map.LandSteps(plan.objective);
            System.Func<HexTile, int> steps = t => { int s; return t != null && toObj.TryGetValue(t, out s) ? s : int.MaxValue; };

            // The army's front: the fighting unit nearest the objective over land.
            foreach (Unit u in (IEnumerable<Unit>)army ?? map.ownUnits) {
                if (u == null || u.tile == null || u.Firepower <= 0 || u.Hub != null) continue;
                if (LocomotionRules.NeedsBase(u.locomotion)) continue;
                plan.frontSteps = Mathf.Min(plan.frontSteps, steps(u.tile));
            }

            bool leaps = map.profile.supplyLeaps;
            List<SupplyPoint> hubs = new List<SupplyPoint>();
            foreach (SupplyPoint h in map.ownHubs) {
                if (only != null && !only.Contains(h)) continue;
                // A supply unit already aboard a truck is a link too when leaping: its truck can move it.
                if (IsLink(h) || (leaps && h != null && !h.producer && h.myUnit != null && h.myUnit.tile != null && IsTruck(h.myUnit.trasnporter))) hubs.Add(h);
            }
            // Rearmost first.
            hubs.Sort((a, b) => { int c = steps(b.myUnit.tile).CompareTo(steps(a.myUnit.tile)); return c != 0 ? c : AIMap.CompareUnits(a.myUnit, b.myUnit); });

            Dictionary<SupplyPoint, HexTile> placed = new Dictionary<SupplyPoint, HexTile>();
            HashSet<HexTile> taken = new HashSet<HexTile>();
            Dictionary<SupplyPoint, Unit> aboard = new Dictionary<SupplyPoint, Unit>();
            HashSet<Unit> trucksUsed = new HashSet<Unit>();
            Dictionary<SupplyPoint, Unit> aboardWas = SupplyNetwork.PlannedAboard;
            SupplyNetwork.PlannedAboard = aboard;
            try {
            foreach (SupplyPoint h in hubs) if (fixedHubs != null && fixedHubs.Contains(h.myUnit)) { placed[h] = h.myUnit.tile; taken.Add(h.myUnit.tile); }
            SupplyNetwork.Result net = SupplyNetwork.SolvePlanned(map.player, placed);
            int reach = Reach(net, toObj);
            bool anyAdvance = false, anyCapped = false;

            foreach (SupplyPoint h in hubs) {
                if (placed.ContainsKey(h)) continue;
                Unit u = h.myUnit;
                if (u.trasnporter != null) {
                    // Aboard a truck: only a leap moves it (on to the furthest linked tile, or it holds there).
                    Leap ride = FindLeap(h, map, net, steps, plan.frontSteps, trucksUsed, taken);
                    if (ride == null) { plan.spare.Add(h); continue; }
                    aboard[h] = ride.truck; trucksUsed.Add(ride.truck);
                    placed[h] = ride.tile; taken.Add(ride.tile);
                    net = SupplyNetwork.SolvePlanned(map.player, placed); reach = Reach(net, toObj);
                    plan.links[h] = ride.tile; plan.leaps[h] = ride;
                    if (steps(ride.tile) < steps(u.tile)) anyAdvance = true;
                    continue;
                }
                MoveField field = map.ReachThisTurn(u);
                HexTile best = null; int bestSteps = int.MaxValue, bestReach = reach; float bestRisk = float.MaxValue;
                bool cappedOut = false;
                int nUnsupplied = 0, nAhead = 0, nRisky = 0, nTiles = 0;
                foreach (HexTile t in field.Tiles) {
                    if (t != u.tile && (!field.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(u, t))) continue;
                    if (taken.Contains(t)) continue;
                    if (t != u.tile && t.occupier != null && (t.occupier.owner != u.owner || t.occupier.Hub != null)) continue;
                    if (!AIAir.MayEndOn(u, t, map)) continue;
                    nTiles++;
                    if (net.DepthAt(t) <= 0) { nUnsupplied++; continue; }       // a link must be supplied by the links behind it
                    int s = steps(t);
                    if (s == int.MaxValue) continue;
                    if (s < plan.frontSteps && !(map.profile.chainLeadsWhenClear && map.EnemyStrikeTurns(t) > 2)) nAhead++;
                    // Not ahead of the army — unless no enemy can strike there for more than two
                    // turns (profile.chainLeadsWhenClear): a link behind which the army stands
                    // would otherwise hold in the road and the army, behind it on a narrow road,
                    // never get past (zigzag: Red's chain stood in front of its army all game).
                    if (s < plan.frontSteps && !(map.profile.chainLeadsWhenClear && map.EnemyStrikeTurns(t) > 2)) { cappedOut = true; continue; }
                    // Little or nothing can hit it tonight (profile.chainRiskTolerance strength at most)...
                    float tolerance = Mathf.Max(0f, map.profile.chainRiskTolerance);
                    float expected = map.ExpectedLoss(u, t);
                    if (expected > tolerance) { nRisky++; continue; }
                    if (best != null && !(s < bestSteps || (s == bestSteps && (expected < bestRisk || expected == bestRisk && CombatMath.Precedes(t, best))))) continue;
                    // ...not even by a chain of blows (the enemy's reply played out, as everywhere else).
                    float lost;
                    if (!AIWorld.Evaluate(u, t, new AttackAppraisal(), map, map.replyCache, false).lost.TryGetValue(u, out lost)) lost = 0f;
                    if (lost > tolerance) { nRisky++; continue; }
                    best = t; bestSteps = s; bestRisk = Mathf.Max(expected, lost);
                }
                if (DebugLog != null)
                    DebugLog("   chain: supply unit at (" + u.tile.x + "," + u.tile.y + ", " + steps(u.tile) + " steps): " + nTiles + " tiles, " + nUnsupplied + " not supplied by the links behind, "
                             + nAhead + " ahead of the army, " + nRisky + " in reach of the enemy; best " + (best != null ? "(" + best.x + "," + best.y + ", " + bestSteps + " steps)" : "none") + "; reach now " + reach);
                // A forced leap (Make's second look): this one rides a truck forward whatever, and the rest plan after it.
                if (h == forceLeap) {
                    Leap forced = FindLeap(h, map, net, steps, plan.frontSteps, trucksUsed, taken);
                    if (DebugLog != null) DebugLog("   chain: forced leap for (" + u.tile.x + "," + u.tile.y + "): " + (forced == null ? "no truck or tile" : "truck (" + forced.truck.tile.x + "," + forced.truck.tile.y + ") to (" + forced.tile.x + "," + forced.tile.y + ")"));
                    if (forced != null) {
                        aboard[h] = forced.truck; trucksUsed.Add(forced.truck);
                        placed[h] = forced.tile; taken.Add(forced.tile);
                        if (forced.board != null) taken.Add(forced.board);
                        net = SupplyNetwork.SolvePlanned(map.player, placed); reach = Reach(net, toObj);
                        plan.links[h] = forced.tile; plan.leaps[h] = forced;
                        anyAdvance = true;
                        continue;
                    }
                }
                // Reconnecting first (profile.chainReconnects): of the tiles a link could take, the one
                // that connects the most of our cut-off supply units again, if any does — a link held
                // in the rear only for its reach toward the enemy HQ left five supply units cut off
                // for three turns after the one joining them to the HQ was killed.
                if (map.profile.chainReconnects && Cut(net, map) > 0) {
                    HexTile fix = null; int fixCut = Cut(net, map); int fixSteps = int.MaxValue;
                    foreach (HexTile t in field.Tiles) {
                        if (t != u.tile && (!field.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(u, t))) continue;
                        if (taken.Contains(t) || net.DepthAt(t) <= 0 || !AIAir.MayEndOn(u, t, map)) continue;
                        if (t != u.tile && t.occupier != null && (t.occupier.owner != u.owner || t.occupier.Hub != null)) continue;
                        if (map.ExpectedLoss(u, t) > Mathf.Max(0f, map.profile.chainRiskTolerance)) continue;
                        int ahead = steps(t);
                        if (ahead < plan.frontSteps && !(map.profile.chainLeadsWhenClear && map.EnemyStrikeTurns(t) > 2)) continue;   // not ahead of the army
                        Dictionary<SupplyPoint, HexTile> tr = new Dictionary<SupplyPoint, HexTile>(placed);
                        tr[h] = t;
                        int c = Cut(SupplyNetwork.SolvePlanned(map.player, tr), map);
                        int st = steps(t);
                        if (c < fixCut || (c == fixCut && fix != null && (st < fixSteps || st == fixSteps && CombatMath.Precedes(t, fix)))) { fix = t; fixCut = c; fixSteps = st; }
                    }
                    if (fix != null) {
                        placed[h] = fix; taken.Add(fix);
                        net = SupplyNetwork.SolvePlanned(map.player, placed); reach = Reach(net, toObj);
                        plan.links[h] = fix;
                        if (DebugLog != null) DebugLog("   chain: supply unit at (" + u.tile.x + "," + u.tile.y + ") reconnects: to (" + fix.x + "," + fix.y + "), " + fixCut + " left cut off");
                        continue;
                    }
                }
                // A leap (supplyLeaps): aboard a truck past the edge of supply, if that reaches further than walking.
                Leap jump = leaps ? FindLeap(h, map, net, steps, plan.frontSteps, trucksUsed, taken) : null;
                if (jump != null) {
                    Dictionary<SupplyPoint, HexTile> trial = new Dictionary<SupplyPoint, HexTile>(placed);
                    trial[h] = jump.tile; aboard[h] = jump.truck;
                    SupplyNetwork.Result after = SupplyNetwork.SolvePlanned(map.player, trial);
                    int leapReach = Reach(after, toObj), walkReach = int.MaxValue;
                    if (best != null) {
                        aboard.Remove(h);
                        Dictionary<SupplyPoint, HexTile> walkTrial = new Dictionary<SupplyPoint, HexTile>(placed);
                        walkTrial[h] = best;
                        walkReach = Reach(SupplyNetwork.SolvePlanned(map.player, walkTrial), toObj);
                        aboard[h] = jump.truck;
                    }
                    // Better than walking; or, no worse for the reach, a long jump forward that keeps every
                    // supply unit linked: the rear link carried past the ones ahead of it (they are then
                    // free to move up or leap in turn — leapfrogging, as a player moves a column of trucks).
                    bool better = (leapReach < reach && leapReach < walkReach) || h == forceLeap;
                    bool leapfrog = leapReach <= Mathf.Min(reach, walkReach) && steps(jump.tile) <= steps(u.tile) - Mathf.Max(2, jump.truck.moveRange / 2) && Cut(after, map) <= Cut(net, map);
                    if (after.connectedHubs.Contains(h) && (better || leapfrog) && (h == forceLeap || !map.profile.chainReconnects || Cut(after, map) <= Cut(net, map))) {
                        trucksUsed.Add(jump.truck);
                        placed[h] = jump.tile; taken.Add(jump.tile);
                        if (jump.board != null) taken.Add(jump.board);
                        net = after; reach = leapReach;
                        plan.links[h] = jump.tile; plan.leaps[h] = jump;
                        anyAdvance = true;
                        continue;
                    }
                    aboard.Remove(h);
                }
                if (best != null) {
                    Dictionary<SupplyPoint, HexTile> trial = new Dictionary<SupplyPoint, HexTile>(placed);
                    trial[h] = best;
                    SupplyNetwork.Result after = SupplyNetwork.SolvePlanned(map.player, trial);
                    bestReach = Reach(after, toObj);
                    // Never a move that cuts off more of our supply units than are cut off now.
                    if (bestReach < reach && (!map.profile.chainReconnects || Cut(after, map) <= Cut(net, map))) {
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
            } finally { SupplyNetwork.PlannedAboard = aboardWas; }
        }

        /// <summary>Our supply units (not producers, not aboard) the network leaves cut off.</summary>
        static int Cut(SupplyNetwork.Result net, AIMap map) {
            int n = 0;
            foreach (SupplyPoint h in map.ownHubs)
                if (h != null && !h.producer && h.myUnit != null && h.myUnit.tile != null && h.myUnit.trasnporter == null && !net.connectedHubs.Contains(h)) n++;
            return n;
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
