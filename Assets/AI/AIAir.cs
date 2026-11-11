using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Aircraft and ships: units that are ditched when their fuel runs out. They are refuelled, healed
    /// and rearmed on one of our buildings (or its runway or dock tiles) that services their kind,
    /// aircraft by an air tanker beside them, and helicopters also on any tile in our supply
    /// (LocomotionRules.ResuppliedInSupply), which counts as a base for them here.
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

        /// <summary>
        /// May <paramref name="u"/> end this turn on <paramref name="t"/>? Not if another aircraft has
        /// reserved it for this turn (the tile must be clear for its landing), nor if a link of the
        /// supply chain is moving up to it (AISupplyChain).
        /// </summary>
        public static bool MayEndOn(Unit u, HexTile t, AIMap map) {
            if (u != null && u.trasnporter != null) return true;
            // A transport does not park on a building of ours that builds (keepBuildSlotsClear): it would take the slot
            // and nothing could be bought there (a supply truck held on the HQ for twenty turns: no unit bought since).
            if (BlocksBuildSlot(u, t, map)) return false;
            Unit link;
            if (t != null && map.chainTiles.TryGetValue(t, out link) && link != u) return false;
            if (map.landings == null || t == null) return true;
            // (The carrier an aircraft lands on is where it lands: it stays there.)
            foreach (Landing l in map.landings.Values) if (l.turn == 1 && l.tile == t && l.unit != u && (u == null || l.carrier != u)) return false;
            return true;
        }

        /// <summary>Would <paramref name="u"/>, a transport, ending its turn on <paramref name="t"/> take one of our building slots (keepBuildSlotsClear)? Its own tile counts too: it should leave.</summary>
        /// <summary>Short of fuel, ammunition, supplies or strength: it needs a base's service.</summary>
        static bool NeedsService(Unit u) {
            return (u.fuelMax > 0 && u.fuel < u.fuelMax) || (u.ammoMax > 0 && u.ammo < u.ammoMax) || (u.suppliesMax > 0 && u.supplies < u.suppliesMax) || u.Strength < 6;
        }

        public static bool BlocksBuildSlot(Unit u, HexTile t, AIMap map) {
            if (u == null || t == null || map == null || map.profile == null) return false;
            Building b = t.building;
            if (b == null || b.owner != u.owner || !b.CanProduce) return false;
            // offFacilities: nobody ends on it (purchases allowed), but an aircraft or ship it services.
            if (map.profile.offFacilities && (GameController.gc == null || GameController.gc.purchasesAllowed)) {
                if (u.tile == t) return false;   // (already there: standing still is not a move onto it)
                // (Aircraft and ships it services: only there do they land, rearm or refuel — when it will not build this turn
                // (saving, or buying nothing: it then gives its budget to the others), or when they need it now.)
                if ((LocomotionRules.IsAirborne(u.locomotion) || LocomotionRules.IsNaval(u.locomotion)) && b.Services(u)
                    && (!map.profile.procBudgets || !AIProduction.WillBuild(u.owner, b) || NeedsService(u))) return false;
                return b.Purchasable().Count > 0;
            }
            if (!map.profile.keepBuildSlotsClear) return false;
            if (!Transporting.IsTransport(u) || LocomotionRules.IsAirborne(u.locomotion) || LocomotionRules.IsNaval(u.locomotion)) return false;
            return true;
        }

        /// <summary>Remove the stops another aircraft has reserved for landing this turn (any unit; an aircraft parked there must lift off).</summary>
        public static void FilterReserved(Unit u, AIMap map, Dictionary<HexTile, KeyValuePair<float, HexTile>> stops) {
            if ((map.landings == null || map.landings.Count == 0) && map.chainTiles.Count == 0) return;
            foreach (HexTile s in new List<HexTile>(stops.Keys)) if (!MayEndOn(u, s, map)) stops.Remove(s);
        }

        public static Landing LandingOf(Unit u, AIMap map) {
            Landing l;
            return map.landings != null && map.landings.TryGetValue(u, out l) ? l : null;
        }

        /// <summary>
        /// A landing reserved for an aircraft: the base tile, and the turn it lands (1 = this turn). The
        /// tile is held for that turn only. On that turn the aircraft reserving it acts last; whatever
        /// stands on the tile must leave it first (an aircraft parked there lifts off: it was refuelled
        /// on landing, so it always can), nothing else may end its move there, and nothing may be built
        /// there.
        /// </summary>
        public class Landing {
            public Unit unit;
            public HexTile tile;
            public int turn;
            /// <summary>Landing on a carrier of ours (Carriers): it lands on the carrier wherever the carrier is then.</summary>
            public Unit carrier;
            /// <summary>Where it lands: the carrier's tile now, else the base tile.</summary>
            public HexTile Where { get { return carrier != null && carrier.tile != null && carrier.trasnporter == null ? carrier.tile : tile; } }
            public override string ToString() { return unit.name.Replace("(Clone)", "") + " lands on (" + tile.x + "," + tile.y + ") in " + turn + (turn == 1 ? " turn (this one)" : " turns"); }
        }

        /// <summary>How far ahead (turns) aircraft reserve their landings.</summary>
        public const int LandingHorizon = 3;

        /// <summary>
        /// The tiles of our buildings that service <paramref name="u"/>, whatever is on them; for an aircraft (no
        /// <paramref name="gives"/>), also the tiles of our carriers that would refuel it (Carriers).
        /// </summary>
        static List<HexTile> AllBases(Unit u, System.Func<Building, bool> gives = null) {
            List<HexTile> list = new List<HexTile>();
            foreach (Building b in Building.all) {
                if (b == null || b.owner != u.owner || !b.Services(u) || (gives != null && !gives(b))) continue;
                if (b.tile != null && u.CanEnter(b.tile)) list.Add(b.tile);
            }
            if (gives == null) foreach (Unit c in Carriers(u)) if (!list.Contains(c.tile)) list.Add(c.tile);
            return list;
        }

        /// <summary>A ship that carries aircraft (its prefab's carries lists an airborne locomotion): a carrier.</summary>
        public static bool IsCarrier(Unit t) {
            if (t == null || !Transporting.IsTransport(t) || !LocomotionRules.IsNaval(t.locomotion) || t.carries == null) return false;
            foreach (Locomotion l in t.carries) if (LocomotionRules.IsAirborne(l)) return true;
            return false;
        }

        /// <summary>
        /// Our carriers <paramref name="u"/> (an aircraft) could land on and be refuelled aboard at the start of our next
        /// turn: afloat, able to take it (its kind, room for its weight), and stocked (supplies of its own, or on a building
        /// that services it): aboard a carrier out of supplies its cargo starves (Player.CargoUpkeep).
        /// </summary>
        public static List<Unit> Carriers(Unit u) {
            List<Unit> list = new List<Unit>();
            if (u == null || u.owner == null || !LocomotionRules.IsAirborne(u.locomotion)) return list;
            foreach (Unit c in u.owner.units) {
                if (c == null || c == u || c.tile == null || c.Strength <= 0 || c.trasnporter != null || !IsCarrier(c)) continue;
                if (!Transporting.CarriesKind(c, u)) continue;
                if (u.trasnporter != c && Mathf.Max(1, u.weight) > Transporting.FreeWeight(c)) continue;
                if (!(c.suppliesMax <= 0 || c.supplies > 0 || Building.ServicingUnder(c) != null)) continue;
                list.Add(c);
            }
            return list;
        }

        /// <summary>
        /// Aboard <paramref name="carrier"/> this turn, if it can get there: from beside it, or from a tile beside it it
        /// reaches this turn (the step in fits: AIPath.BoardsFrom). Null if it cannot.
        /// </summary>
        public static AIAction? LandOn(Unit u, Unit carrier, AIMap map, string why) {
            if (u == null || carrier == null || carrier.tile == null || !Transporting.CanCarry(carrier, u)) return null;
            if (Transporting.CanBoardFrom(carrier, u) && Transporting.StepInFits(u, u.tile, 0, carrier)) return AIAction.Board(u, carrier, carrier.tile, why);
            MoveField now = map.ReachThisTurn(u);
            HexTile beside = null;
            foreach (HexTile n in carrier.tile.neighbors) {
                if (n == null || n == u.tile || !AIPath.BoardsFrom(now, u, carrier, n)) continue;
                if (beside == null || CombatMath.Precedes(n, beside)) beside = n;
            }
            return beside != null ? AIAction.Board(u, carrier, beside, why) : (AIAction?)null;
        }

        /// <summary>
        /// A carrier's own move, before the rest (AIPlayer): it holds where it is while an aircraft of ours has a landing
        /// reserved on it (the aircraft counted on it being there); empty, it sails toward the nearest of our aircraft it
        /// could take (as near as it can safely get), so they have somewhere to land. Null: it decides as any transport.
        /// </summary>
        public static AIAction? DecideCarrier(Unit t, AIMap map) {
            if (!IsCarrier(t) || t.tile == null || t.trasnporter != null) return null;
            if (map.landings != null)
                foreach (Landing l in map.landings.Values)
                    if (l.carrier == t) return AIAction.Wait(t, "carrier: holds for " + l.unit.name.Replace("(Clone)", "") + ", landing on it in " + l.turn + " turn" + (l.turn == 1 ? "" : "s"));
            if (t.transported.Count > 0) return null;
            Unit near = null; int nd = int.MaxValue;
            foreach (Unit a in t.owner.units) {
                if (a == null || a.tile == null || a.trasnporter != null || a.Strength <= 0 || !LocomotionRules.IsAirborne(a.locomotion) || !Transporting.CarriesKind(t, a)) continue;
                int d = t.tile.Distance(a.tile);
                if (d < nd || (d == nd && CombatMath.Precedes(a.tile, near.tile))) { nd = d; near = a; }
            }
            if (near == null) return null;
            MoveField horizon = map.ReachHorizon(t);
            HexTile goal = null; int gd = int.MaxValue;
            foreach (HexTile x in horizon.Tiles) {
                if (!horizon.Reachable(x) || (x.occupier != null && x.occupier != t) || !AIPath.CanFinishTurnOn(t, x)) continue;
                int d = x.Distance(near.tile);
                if (d < gd || (d == gd && CombatMath.Precedes(x, goal))) { gd = d; goal = x; }
            }
            string who = near.name.Replace("(Clone)", "") + " (" + near.tile.x + "," + near.tile.y + ")";
            if (goal == null || goal == t.tile || gd >= nd) return AIAction.Wait(t, "carrier: stays near " + who);
            HexTile step = AITransportPlanner.SafeStepToward(t, goal, horizon, map, false);
            if (step == null || step == t.tile || !MayEndOn(t, step, map)) return AIAction.Wait(t, "carrier: stays near " + who);
            return AIAction.Move(t, step, 0f, "carrier: sails toward " + who + " to take it aboard");
        }

        /// <summary>
        /// Tiles in our supply <paramref name="u"/> could end on, if it is resupplied there (a helicopter,
        /// LocomotionRules.ResuppliedInSupply); none otherwise.
        /// </summary>
        public static List<HexTile> SupplyHomes(Unit u, AIMap map) {
            List<HexTile> list = new List<HexTile>();
            if (u == null || !LocomotionRules.ResuppliedInSupply(u.locomotion) || u.owner != map.player || map.supply == null || HexGenerator.hg == null || HexGenerator.hg.tiles == null) return list;
            foreach (HexTile t in HexGenerator.hg.tiles)
                if (t != null && map.supply.DepthAt(t) > 0 && u.CanEnter(t) && (t.occupier == null || t.occupier.owner == u.owner)) list.Add(t);
            return list;
        }

        /// <summary>
        /// Base tiles <paramref name="u"/> may plan to get back to: its reserved landing tile if it has one;
        /// otherwise any of ours not held by the enemy. A helicopter's include every tile in our supply.
        /// </summary>
        public static List<HexTile> Bases(Unit u, AIMap map) {
            Landing mine;
            if (map.landings != null && map.landings.TryGetValue(u, out mine)) return new List<HexTile> { mine.Where };
            List<HexTile> homes = SupplyHomes(u, map);
            // Every base tile of ours counts as somewhere to get back to. Reservations hold a single
            // turn, and whatever stands on a tile reserved for this turn has to leave it, so neither
            // another aircraft's reservation nor an aircraft parked there closes a tile for good;
            // ending on a tile reserved for this turn is ruled out separately (MayEndOn).
            List<HexTile> list = new List<HexTile>();
            foreach (HexTile t in AllBases(u)) if (t.occupier == null || t.occupier.owner == u.owner) list.Add(t);
            foreach (HexTile t in homes) if (!list.Contains(t)) list.Add(t);
            foreach (HexTile t in BesideTenders(u)) if (!list.Contains(t)) list.Add(t);
            return list;
        }

        /// <summary>
        /// For a ship: the water beside our supply ships (Player.IsSupplyShip), where it is refuelled and restocked at the start
        /// of its turn (not rearmed: only a naval yard does that). None for anything else, or for a supply ship itself.
        /// </summary>
        static List<HexTile> BesideTenders(Unit u) {
            List<HexTile> list = new List<HexTile>();
            if (u == null || u.owner == null || !LocomotionRules.IsNaval(u.locomotion) || Player.IsSupplyShip(u)) return list;
            foreach (Unit t in u.owner.units) {
                if (t == null || t == u || t.tile == null || t.Strength <= 0 || !Player.IsSupplyShip(t)) continue;
                foreach (HexTile n in t.tile.neighbors) if (n != null && !list.Contains(n) && u.CanEnter(n) && (n.occupier == null || n.occupier == u)) list.Add(n);
            }
            return list;
        }

        /// <summary>
        /// The latest turn (1 = this one) <paramref name="u"/> can land on <paramref name="t"/>: within its
        /// movement, with the fuel for the trip and a turn's burn for each turn before it lands, and
        /// before its own supplies run out. 0 if it cannot.
        /// </summary>
        static int LatestLanding(Unit u, HexTile t) {
            int d = u.tile.Distance(t);
            int per = Mathf.Max(0, u.minFuelPerTile), burn = Mathf.Max(0, u.minFuelPerTurn);
            int left = u.fuel - d * per;
            if (left < 0) return 0;
            int byFuel = burn > 0 ? left / burn + 1 : 99;
            int bySupplies = u.suppliesMax > 0 ? u.supplies + 1 : 99;
            int latest = Mathf.Min(byFuel, bySupplies);
            int earliest = Mathf.Max(1, Mathf.CeilToInt(d / (float)Mathf.Max(1, u.moveRange)));
            return latest >= earliest ? latest : 0;
        }

        /// <summary>
        /// Plan this turn's landings. Every aircraft of ours that has to land within LandingHorizon turns,
        /// the most urgent first, tries its latest landing turn on the nearest base tile it can make; if
        /// that tile is reserved for that turn, the next base tile; if every one is, a turn earlier; and
        /// so on down to this turn. Aircraft already on a base need no reservation.
        /// </summary>
        public static Dictionary<Unit, Landing> PlanLandings(AIMap map) {
            Dictionary<Unit, Landing> plan = new Dictionary<Unit, Landing>();
            List<KeyValuePair<Unit, int>> need = new List<KeyValuePair<Unit, int>>();
            foreach (Unit u in map.ownUnits) {
                if (u == null || u.tile == null || u.trasnporter != null || !LocomotionRules.IsAirborne(u.locomotion) || u.fuelMax <= 0) continue;
                if (Building.ServicingUnder(u) != null) continue;
                // A helicopter that can make our supply in time needs no runway: no reservation.
                bool home = false;
                foreach (HexTile t in SupplyHomes(u, map)) if (LatestLanding(u, t) > 0) { home = true; break; }
                if (home) continue;
                int latest = 0;
                foreach (HexTile t in AllBases(u)) latest = Mathf.Max(latest, LatestLanding(u, t));
                if (latest <= 0 || latest > LandingHorizon) continue;
                need.Add(new KeyValuePair<Unit, int>(u, latest));
            }
            need.Sort((a, b) => { int c = a.Value.CompareTo(b.Value); return c != 0 ? c : AIMap.CompareUnits(a.Key, b.Key); });
            // Held: tile -> the turns it is reserved for. An aircraft parked on a tile does not hold it: if the
            // tile is reserved for this turn, the parked aircraft lifts off (it refuelled when it landed).
            Dictionary<HexTile, HashSet<int>> held = new Dictionary<HexTile, HashSet<int>>();
            Func<HexTile, HashSet<int>> at = t => { HashSet<int> h; if (!held.TryGetValue(t, out h)) { h = new HashSet<int>(); held[t] = h; } return h; };
            foreach (KeyValuePair<Unit, int> kv in need) {
                Unit u = kv.Key;
                List<HexTile> bases = AllBases(u);
                bases.Sort((a, b) => { int c = u.tile.Distance(a).CompareTo(u.tile.Distance(b)); return c != 0 ? c : CombatMath.CompareTiles(a, b); });
                Landing got = null;
                for (int turn = kv.Value; turn >= 1 && got == null; turn--) {
                    foreach (HexTile t in bases) {
                        if (LatestLanding(u, t) < turn) continue;
                        if (Mathf.CeilToInt(u.tile.Distance(t) / (float)Mathf.Max(1, u.moveRange)) > turn) continue;
                        HashSet<int> h = at(t);
                        if (h.Contains(turn)) continue;
                        got = new Landing { unit = u, tile = t, turn = turn, carrier = t.occupier != null && IsCarrier(t.occupier) && t.occupier.owner == u.owner ? t.occupier : null };
                        h.Add(turn);
                        break;
                    }
                }
                if (got != null) plan[u] = got;
            }
            return plan;
        }

        /// <summary>
        /// Could an aircraft be built at (or land on) <paramref name="b"/> without taking a tile an aircraft
        /// has reserved within the next <paramref name="turns"/> turns? For production: no new aircraft on a
        /// crowded airfield, or on a turn another needs its runway.
        /// </summary>
        /// <summary>May a unit be built on <paramref name="t"/> this turn? Not on a tile reserved for a landing this turn.</summary>
        public static bool MayBuildOn(HexTile t, AIMap map) { return MayEndOn(null, t, map); }

        public static bool RoomForAircraft(Building b, AIMap map, int turns) {
            if (b == null) return false;
            HashSet<HexTile> reserved = new HashSet<HexTile>();
            if (map.landings != null) foreach (Landing l in map.landings.Values) if (l.turn <= turns) reserved.Add(l.tile);
            if (b.tile != null && b.tile.occupier == null && !reserved.Contains(b.tile)) return true;
            return false;
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
            if (LocomotionRules.IsNaval(u.locomotion)) return BesideTenders(u).Contains(at);
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
            return Margin(u, stop, toBase, bases, null);
        }

        /// <param name="land">Its reserved landing, if any: then it must also still make that tile by its turn.</param>
        /// <param name="map">If given, a stop the enemy can hit leaves room for being driven back a tile
        /// (a retreat after combat): one tile further from home, one more tile of fuel.</param>
        public static int Margin(Unit u, HexTile stop, Dictionary<HexTile, int> toBase, List<HexTile> bases, Landing land, AIMap map = null) {
            if (u.fuelMax <= 0) return int.MaxValue;
            if (land != null && stop != land.Where) {
                int turnsLeft = land.turn - 1;
                if (turnsLeft <= 0 || stop.Distance(land.Where) > turnsLeft * Mathf.Max(1, u.moveRange)) return -1;
            }
            int per = Mathf.Max(0, u.minFuelPerTile);
            int after = u.fuel - u.tile.Distance(stop) * per;
            if (bases.Contains(stop) || BesideTanker(u, stop)) return after;
            int burn = LocomotionRules.IsAirborne(u.locomotion) ? Mathf.Max(0, u.minFuelPerTurn) : 0;
            int home;
            if (!toBase.TryGetValue(stop, out home)) return -999;
            if (map != null && map.ExpectedLoss(u, stop) > 0f) home += 1;
            int turns = Mathf.Max(1, Mathf.CeilToInt(home / (float)Mathf.Max(1, u.moveRange)));
            return after - burn * turns - home * per;
        }

        /// <summary>
        /// Keep the stops that are safe; if none is, keep the ones nearest a base (heading home).
        /// </summary>
        public static void FilterSafe(Unit u, AIMap map, Dictionary<HexTile, KeyValuePair<float, HexTile>> stops) {
            if (!NeedsBase(u) || u.fuelMax <= 0 || stops.Count == 0) return;
            Landing land = LandingOf(u, map);
            List<HexTile> bases = Bases(u, map);
            Dictionary<HexTile, int> toBase = DistanceToBase(u, bases);
            List<HexTile> safe = new List<HexTile>(), all = new List<HexTile>(stops.Keys);
            foreach (HexTile s in all) if (Margin(u, s, toBase, bases, land, map) >= 0) safe.Add(s);
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
        /// <summary>Where <paramref name="action"/> leaves its unit at the end of the turn.</summary>
        public static HexTile EndOf(Unit u, AIAction action) {
            if (action.kind == AIActionKind.Wait || action.kind == AIActionKind.Attack) return u.tile;
            if (action.kind == AIActionKind.Load && action.transport != null) return action.transport.tile;   // it ends aboard
            if (action.kind == AIActionKind.Unload) return action.via ?? u.tile;   // the transport ends where it put the cargo down
            return action.destination ?? u.tile;
        }

        public static AIAction Guard(Unit u, AIAction action, AIMap map) {
            action = KeepsUnrefuelledAboard(action);
            if (u != null && u.tile != null && u.trasnporter == null && LocomotionRules.IsNaval(u.locomotion)) action = Refit(u, action, map);
            if (!NeedsBase(u) || u.fuelMax <= 0 || u.tile == null || u.trasnporter != null) return action;
            Landing land = LandingOf(u, map);
            // Its landing this turn is on a carrier: aboard it, wherever the carrier is now.
            if (land != null && land.carrier != null && land.turn == 1 && !(action.kind == AIActionKind.Load && action.transport == land.carrier)) {
                AIAction? onto = LandOn(u, land.carrier, map, "lands on " + land.carrier.name.Replace("(Clone)", "") + " (" + land.carrier.tile.x + "," + land.carrier.tile.y + ") to refuel (fuel " + u.fuel + ")");
                if (onto.HasValue) return onto.Value;
            }
            List<HexTile> bases = Bases(u, map);
            Dictionary<HexTile, int> toBase = DistanceToBase(u, bases);
            // No base it can get back to from here (none at all, or none on its water): holding would only idle it for
            // good, so it carries on with what it was going to do.
            if (!toBase.ContainsKey(u.tile)) return action;
            // Where it ends the turn (EndOf): a ship putting troops ashore stays on the water it unloads from, not on the
            // land they step onto (read as the land, every unloading ship was "out of fuel" and held).
            HexTile stop = EndOf(u, action);
            if (Margin(u, stop, toBase, bases, land, map) >= 0 && MayEndOn(u, stop, map)) return action;
            // Short: a carrier it can get aboard this turn takes it in.
            foreach (Unit c in Carriers(u)) {
                if (land != null && land.carrier != null && land.carrier != c) continue;
                AIAction? onto = LandOn(u, c, map, "bingo fuel: lands on " + c.name.Replace("(Clone)", "") + " (" + c.tile.x + "," + c.tile.y + ") (fuel " + u.fuel + ")");
                if (onto.HasValue) return onto.Value;
            }
            MoveField f = map.ReachThisTurn(u);
            HexTile best = null; int bestMargin = int.MinValue, bestDist = int.MaxValue;
            foreach (HexTile t in f.Tiles) {
                if (t != u.tile && (!f.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(u, t) || (t.occupier != null && t.occupier != u))) continue;
                if (!MayEndOn(u, t, map)) continue;
                int m = Margin(u, t, toBase, bases, land, map);
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
        /// An aircraft aboard a transport is not put down (by the transport, or getting off itself) before it has had the
        /// start of a turn aboard (still to act: Unit.active), which is when it is refuelled there: one that landed this turn
        /// would be back out with the fuel it came in with. The transport moves (or holds) instead.
        /// </summary>
        public static AIAction KeepsUnrefuelledAboard(AIAction a) {
            if (a.kind != AIActionKind.Unload || a.target == null || a.target.trasnporter == null || a.target.active) return a;
            if (!LocomotionRules.IsAirborne(a.target.locomotion) || !LocomotionRules.NeedsBase(a.target.locomotion)) return a;
            Unit t = a.target.trasnporter;
            string why = "keeps " + a.target.name.Replace("(Clone)", "") + " aboard: it has not been refuelled yet (" + a.reason + ")";
            if (a.unit == t && a.via != null && a.via != t.tile) return AIAction.Move(t, a.via, a.score, why);
            return AIAction.Wait(a.unit, why);
        }

        /// <summary>
        /// A ship back for what it is running out of, to where it is given it — as the buildings' prefabs say: one of ours
        /// that serves ships (Building.Services) and rearms it (Building.rearm) when it is out of ammunition; one that
        /// restocks it (Building.resupply), or the water beside a supply ship of ours (Player.TankerBeside), when its
        /// supplies are down to the turns it takes to get there (and one more). It moves as far toward the nearest as it can
        /// this turn, or stays if it is there. (Out of supplies a ship loses a strength a turn; fuel is Guard's.) Its action
        /// stands if it is already going to end the turn there, or if it has nothing to go back for.
        /// </summary>
        static AIAction Refit(Unit u, AIAction action, AIMap map) {
            bool noAmmo = u.Firepower > 0 && u.ammoMax > 0 && u.ammo <= 0;
            List<HexTile> homes = new List<HexTile>();
            foreach (HexTile t in AllBases(u, b => noAmmo ? b.rearm : b.resupply)) if (t.occupier == null || t.occupier == u || t.occupier.owner == u.owner) homes.Add(t);
            if (!noAmmo) foreach (HexTile t in BesideTenders(u)) if (!homes.Contains(t)) homes.Add(t);
            if (homes.Count == 0) return action;
            Dictionary<HexTile, int> toHome = DistanceToBase(u, homes);
            int here; if (!toHome.TryGetValue(u.tile, out here)) return action;
            int turnsHome = Mathf.CeilToInt(here / (float)Mathf.Max(1, u.moveRange));
            bool lowSupplies = u.suppliesMax > 0 && u.supplies <= turnsHome + 1;
            if (!noAmmo && !lowSupplies) return action;
            HexTile stop = EndOf(u, action);
            if (homes.Contains(stop)) return action;
            // Putting troops ashore, with the supplies to get back after it: that first.
            if (action.kind == AIActionKind.Unload && !noAmmo && u.supplies > turnsHome) return action;
            MoveField f = map.ReachThisTurn(u);
            HexTile best = null; int bd = int.MaxValue;
            foreach (HexTile t in f.Tiles) {
                if (t != u.tile && (!f.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(u, t))) continue;
                if (!MayEndOn(u, t, map)) continue;
                int d; if (!toHome.TryGetValue(t, out d)) continue;
                if (best == null || d < bd || (d == bd && CombatMath.Precedes(t, best))) { best = t; bd = d; }
            }
            string why = noAmmo ? "out of ammunition" : "supplies " + u.supplies + ", " + turnsHome + " turns from restocking";
            if (best == null || best == u.tile) return AIAction.Wait(u, "refit: " + why + ": holds" + (homes.Contains(u.tile) ? " where it is restocked" : ""));
            return AIAction.Move(u, best, 0f, "refit: " + why + ": heads " + (bd == 0 ? "to " : "toward ") + (noAmmo ? "a base that rearms it" : "a base that restocks it, or a supply ship"));
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
                if (Margin(u, t, toBase, bases, LandingOf(u, map), map) < 0) continue;
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
