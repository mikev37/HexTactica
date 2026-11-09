using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Buying units (profile.buyUnits), at the end of the AI's turn, when the rules and the scenario
    /// allow purchases. Supply units are bought by the supply chain's own rule (AIPlayer.BuySupply);
    /// this buys fighting units.
    ///
    /// Each building that can build something, while money lasts and it has a free tile (never one
    /// reserved for a landing): the allowed, affordable unit worth most per unit of cost against the
    /// enemy on the board now — the damage it would do to each enemy unit, priced at what that unit
    /// is worth, less half the damage it would take from each — with a type we already have many of,
    /// or just bought this turn, worth a little less each time, so the army stays mixed.
    /// </summary>
    public static class AIProduction {

        /// <summary>Harness tracing of candidate values.</summary>
        public static System.Action<string> DebugValues;

        public static List<Unit> Buy(Player p, AIMap map, AIProfile profile, System.Action<string> trace) { return Buy(p, map, profile, trace, null); }

        /// <summary>
        /// With the campaign's logistics (<paramref name="logistics"/>), the fronts put pressure on what
        /// is bought: each front's wants go to the producer nearest it — the units that do most against
        /// the enemy it faces, the more the further short of its need it is; and, when its chain cannot
        /// bring supply to it, a supply unit (worth the units it would supply), and a transport if none is
        /// free. These compete with the ordinary choice by value for the gold.
        /// </summary>
        /// <summary>Enemy units each side has seen, and the day it last saw each (kept productionMemoryDays after it is out of sight).</summary>
        static readonly Dictionary<Player, Dictionary<Unit, int>> seen = new Dictionary<Player, Dictionary<Unit, int>>();

        /// <summary>The enemy we buy against: what we see now, and what we have seen lately (a gunship gone behind the trees is still out there).</summary>
        /// <summary>productionPossibleFoes: each enemy kind there was at the start of the game, by player (recorded once).</summary>
        static readonly Dictionary<Player, HashSet<string>> startKinds = new Dictionary<Player, HashSet<string>>();
        [System.ThreadStatic] static List<Unit> possibleFoes;

        /// <summary>
        /// productionPossibleFoes: one of each kind the enemy has or could field: its units at the start (a no-build map's whole
        /// army), what its production buildings make, and what it has been seen with.
        /// </summary>
        static List<Unit> PossibleFoes(Player p, AIMap map) {
            HashSet<string> names;
            lock (startKinds) {
                if (!startKinds.TryGetValue(p, out names)) {
                    startKinds[p] = names = new HashSet<string>();
                    if (GameController.gc != null && GameController.gc.players != null)
                        foreach (Player o in GameController.gc.players)
                            if (o != null && o != p && o.units != null) foreach (Unit u in o.units) if (u != null) names.Add(GameController.UnitName(u));
                }
            }
            Dictionary<string, Unit> kinds = new Dictionary<string, Unit>();
            foreach (string n in names) { Unit u = GameAssets.Unit(n); if (u != null) kinds[n] = u; }
            foreach (Building b in Building.all) {
                if (b == null || b.owner == null || b.owner == p || !b.CanProduce) continue;
                foreach (Unit u in b.Purchasable()) if (u != null) kinds[GameController.UnitName(u)] = u;
            }
            foreach (Unit e in map.enemyUnits) if (e != null) kinds[GameController.UnitName(e)] = e;
            List<string> keys = new List<string>(kinds.Keys); keys.Sort(System.StringComparer.Ordinal);
            List<Unit> r = new List<Unit>(); foreach (string k in keys) r.Add(kinds[k]);
            return r;
        }

        /// <summary>productionPossibleFoes: can <paramref name="t"/> hurt any kind the enemy has or could field?</summary>
        static bool HurtsPossible(Unit t, HexTile at) {
            if (possibleFoes == null || possibleFoes.Count == 0 || t.Firepower <= 0) return true;
            foreach (Unit e in possibleFoes) if (Unit.calculateDamage(t, e, at, 0) > 0) return true;
            return false;
        }

        static List<Unit> Foes(Player p, AIMap map, AIProfile profile) {
            int day = GameController.gc != null ? GameController.gc.days : 0;
            Dictionary<Unit, int> mine;
            lock (seen) { if (!seen.TryGetValue(p, out mine)) seen[p] = mine = new Dictionary<Unit, int>(); }
            foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && e.Strength > 0) mine[e] = day;
            List<Unit> foes = new List<Unit>();
            foreach (KeyValuePair<Unit, int> kv in new List<KeyValuePair<Unit, int>>(mine)) {
                if (kv.Key == null || kv.Key.Strength <= 0 || day - kv.Value > Mathf.Max(0, profile.productionMemoryDays)) { mine.Remove(kv.Key); continue; }
                foes.Add(kv.Key);
            }
            foes.Sort(AIMap.CompareUnits);
            return foes;
        }

        /// <summary>
        /// How little of our army can hurt each enemy unit (1: nothing of ours can; toward 0 the more of ours
        /// can kill it): what is bought against it counts that much. (Ten enemy gunships and nothing of ours
        /// that could hit them: averaged over every enemy unit, an anti-air gun was worth no more than a grunt.)
        /// </summary>
        static Dictionary<Unit, float> Scarcity(List<Unit> foes, AIMap map, AIProfile profile) {
            Dictionary<Unit, float> r = new Dictionary<Unit, float>();
            float k = Mathf.Max(0.01f, profile.productionCounterUnits);
            foreach (Unit e in foes) {
                float cover = 0f;
                foreach (Unit u in map.ownUnits) {
                    if (u == null || u.tile == null || u.Firepower <= 0 || u.Hub != null) continue;
                    cover += Mathf.Min(e.Strength, Unit.calculateDamage(u, e, u.tile, 0)) / 6f;
                }
                if (planned != null) foreach (KeyValuePair<Unit, HexTile> pu in planned) cover += Mathf.Min(e.Strength, Unit.calculateDamage(pu.Key, e, pu.Value, 0)) / 6f;
                r[e] = 1f / (1f + cover / k);
            }
            return r;
        }

        [System.ThreadStatic] static Dictionary<Unit, float> scarce;
        [System.ThreadStatic] static List<Unit> allFoes;
        /// <summary>Units planned this turn (and where they would be built): counted in Scarcity as part of our army.</summary>
        [System.ThreadStatic] static List<KeyValuePair<Unit, HexTile>> planned;

        /// <summary>A building tile free to build on this turn, and what is planned for it.</summary>
        class Slot { public Building b; public HexTile tile; public Unit pick; public float value; public string why; public bool free = true; }

        static int DistToSet(HexTile a, List<HexTile> set) { int d = int.MaxValue; foreach (HexTile x in set) if (x != null) d = Mathf.Min(d, a.Distance(x)); return d; }

        /// <summary>A transport that does not fight (truck, APC, helicopter, plane, ship).</summary>
        static bool IsCarrier(Unit t) { return t != null && t.Hub == null && Transporting.IsTransport(t) && t.Firepower <= 1; }
        /// <summary>An air tanker or a supply ship: resupplies our aircraft or ships beside it.</summary>
        static bool IsResupplier(Unit t) { return t != null && (t.airTanker || Player.IsSupplyShip(t)); }

        /// <summary>
        /// procTerrainReach: move points from every tile to a goal (or the nearest of a set) by a kind of unit's own movement —
        /// what it can enter, its scramble over rough ground, roads; aircraft a point a tile — ignoring who stands where.
        /// One field per way of moving and goal, kept for the turn.
        /// </summary>
        class Reach {
            readonly Dictionary<long, Dictionary<HexTile, Dictionary<HexTile, int>>> single = new Dictionary<long, Dictionary<HexTile, Dictionary<HexTile, int>>>();
            readonly Dictionary<long, Dictionary<HexTile, int>> toSet = new Dictionary<long, Dictionary<HexTile, int>>();
            readonly List<HexTile> set;
            public Reach(List<HexTile> set) { this.set = set; }
            static long Sig(Unit u) {
                return (long)(int)u.locomotion | ((long)(u.terrainScramble & 0xff) << 8) | ((long)((u.elevationMin + 8) & 0xff) << 16) | ((long)((u.elevationMax + 8) & 0xff) << 24) | ((u.amphibious ? 1L : 0L) << 32);
            }
            static Dictionary<HexTile, int> Field(Unit mover, IEnumerable<HexTile> goals) {
                Dictionary<HexTile, int> cost = new Dictionary<HexTile, int>();
                AIHeap<HexTile> open = new AIHeap<HexTile>();
                foreach (HexTile g in goals) if (g != null && !cost.ContainsKey(g)) { cost[g] = 0; open.Push(g, 0); }
                HashSet<HexTile> closed = new HashSet<HexTile>();
                while (open.Count > 0) {
                    HexTile c = open.Pop();
                    if (!closed.Add(c)) continue;
                    int here = cost[c];
                    foreach (HexTile n in c.neighbors) {
                        if (n == null || closed.Contains(n) || !mover.CanEnter(n)) continue;
                        int step = AIPath.StepCost(mover, n, c);
                        if (step == int.MaxValue) continue;
                        int next = here + step, prev;
                        if (cost.TryGetValue(n, out prev) && prev <= next) continue;
                        cost[n] = next; open.Push(n, next);
                    }
                }
                return cost;
            }
            /// <summary>Move points from <paramref name="from"/> to <paramref name="goal"/>; int.MaxValue: it cannot get there.</summary>
            public int Cost(Unit mover, HexTile from, HexTile goal) {
                if (mover == null || from == null || goal == null) return int.MaxValue;
                long k = Sig(mover);
                Dictionary<HexTile, Dictionary<HexTile, int>> byGoal;
                if (!single.TryGetValue(k, out byGoal)) single[k] = byGoal = new Dictionary<HexTile, Dictionary<HexTile, int>>();
                Dictionary<HexTile, int> f;
                if (!byGoal.TryGetValue(goal, out f)) byGoal[goal] = f = Field(mover, new HexTile[] { goal });
                int c; return f.TryGetValue(from, out c) ? c : int.MaxValue;
            }
            /// <summary>Move points from <paramref name="from"/> to the nearest tile of the set (where the fight is).</summary>
            public int CostToSet(Unit mover, HexTile from) {
                if (mover == null || from == null || set == null || set.Count == 0) return int.MaxValue;
                long k = Sig(mover);
                Dictionary<HexTile, int> f;
                if (!toSet.TryGetValue(k, out f)) toSet[k] = f = Field(mover, set);
                int c; return f.TryGetValue(from, out c) ? c : int.MaxValue;
            }
            /// <summary>Turns for <paramref name="mover"/> to get from <paramref name="from"/> to <paramref name="goal"/> (the fight when null); infinity: it cannot.</summary>
            public float Turns(Unit mover, HexTile from, HexTile goal) {
                int c = goal != null ? Cost(mover, from, goal) : CostToSet(mover, from);
                return c == int.MaxValue ? float.PositiveInfinity : c / (float)Mathf.Max(1, mover.moveRange);
            }
        }

        /// <summary>
        /// Buying as a whole turn's plan (AIProfile.production*): every free building tile is a slot, and the
        /// gold is spread over the slots to buy the most value — first the best value for its cost in each
        /// slot, then, while gold is left, the upgrade that adds most value per extra gold (so a turn with
        /// gold to spare fills every slot with the best it can, not the cheapest). A unit's value is what it
        /// does against the enemy (each enemy unit weighted by how little of our army can hurt it — counting
        /// what is planned this turn too, so the second anti-air gun is worth less than the first), or what a
        /// front wants against the enemy it faces (at the front's nearest producer); a supply unit is worth
        /// the fighting units it would supply, up to the supply cap; a truck half that, when none is free.
        /// </summary>
        /// <summary>
        /// reserveDemand, set by the AI before buying: where the central reserve gathers and how short it is of its share of
        /// the army (0..1), when no task force faces active opposition (otherwise null). Bought as a front's needs are: at the
        /// producer nearest it, a unit's value raised by the shortfall.
        /// </summary>
        [System.ThreadStatic] public static HexTile ReserveRally;
        /// <summary>Where each of our units wants to be (AIProfile.seaLift; set by the AI before buying, else null): a land unit whose ground is across water is a rider only a ship can take.</summary>
        [System.ThreadStatic] public static System.Func<Unit, HexTile> GroundOf;
        [System.ThreadStatic] public static float ReserveShort;
        /// <summary>Units bought this turn for the reserve (reserveDemand): the AI keeps them out of task forces.</summary>
        [System.ThreadStatic] public static List<Unit> ReserveBought;
        /// <summary>What each side is saving for the reserve, by unit name: bought as soon as it can be paid for (not traded up).</summary>
        static readonly Dictionary<Player, string> reserveSaving = new Dictionary<Player, string>();

        /// <summary>reserveDemand: a kind that can be in the central reserve — armed aircraft or ships, a fast fighting unit (campaignReserveMinMove), or a troop transport (the reserve drafts infantry into it).</summary>
        static bool CanJoinReserve(Unit t, AIProfile profile) {
            if (t == null || t.Hub != null) return false;
            bool air = LocomotionRules.IsAirborne(t.locomotion), sea = LocomotionRules.IsNaval(t.locomotion);
            if (Transporting.IsTransport(t) && !air && !sea) return true;
            if (t.Firepower <= 0) return false;
            return air || sea || t.moveRange >= profile.campaignReserveMinMove;
        }

        public static List<Unit> Buy(Player p, AIMap map, AIProfile profile, System.Action<string> trace, AILogistics logistics) {
            List<Unit> bought = new List<Unit>();
            if (p == null || map == null || BuildingController.bc == null) return bought;
            allFoes = Foes(p, map, profile);
            possibleFoes = profile.productionPossibleFoes ? PossibleFoes(p, map) : null;
            defenceWeightNow = profile.productionDefenceWeight;
            overkillNow = profile.productionOverkill; retaliationNow = profile.productionRetaliation;
            lifetimeNow = profile.productionLifetime; lifeMaxNow = Mathf.Max(1f, profile.productionLifeMax); lifeRefNow = profile.productionLifeRef; reachRefNow = profile.productionReachRef;
            shieldCutNow = Mathf.Clamp01(profile.productionShieldCut); shieldRatioNow = Mathf.Max(0.1f, profile.productionShieldRatio); tierCostsNow = profile.productionTierCosts;
            tierCount = new int[3];
            int fightingNow = 0;
            foreach (Unit u in map.ownUnits) if (u != null && u.tile != null && u.Hub == null && u.Firepower > 0 && !(Transporting.IsTransport(u) && u.Firepower <= 1)) { tierCount[Tier(u)]++; fightingNow++; }
            if (profile.productionDefenceFade > 0f && !profile.productionLifetime) defenceWeightNow = profile.productionDefenceWeight / (1f + fightingNow / profile.productionDefenceFade);
            presenceNow = Mathf.Max(0f, profile.productionPresence); floorNow = Mathf.Max(0f, profile.productionFloor);
            planned = new List<KeyValuePair<Unit, HexTile>>();
            scarce = Scarcity(allFoes, map, profile);
            int fighters = 0, hubsHave = 0;
            foreach (Unit u in map.ownUnits) { if (u == null || u.tile == null) continue; if (u.Hub != null && !u.Hub.producer) hubsHave++; else if (u.Firepower > 0 && !(Transporting.IsTransport(u) && u.Firepower <= 1)) fighters++; }
            int shortFronts = 0; if (logistics != null) foreach (AILogistics.Task t in logistics.tasks) if (!t.ordinary && t.short_) shortFronts++;
            int supplyCap = Mathf.CeilToInt(fighters / (float)Mathf.Max(1, profile.campaignUnitsPerSupply)) + Mathf.Min(Mathf.Max(0, profile.productionSupplyExtra), shortFronts + (logistics != null && logistics.supplyDemand > 0f ? 1 : 0));
            if (!profile.productionSupplyCap) supplyCap = int.MaxValue;
            // How far our army outmatches what we know of theirs (AIProfile.productionSupplySuperiority): supply
            // counts for more the more we outmatch them (the fight is won; reach is what is short), less when we
            // are outmatched. (Twenty to one up, every front asking for supply, and it bought tanks.)
            float ourPower = 0f, theirPower = 0f;
            foreach (Unit u in map.ownUnits) if (u != null && u.tile != null && u.Firepower > 0 && u.Hub == null) ourPower += AIScore.Worth(u) * u.Strength;
            foreach (Unit e in allFoes) if (e != null && e.Firepower > 0 && e.Hub == null) theirPower += AIScore.Worth(e) * e.Strength;
            float edge = theirPower > 0f ? ourPower / theirPower : 4f;
            float supplyScale = profile.productionSupplySuperiority ? Mathf.Clamp(edge / 2f, 0.25f, 2f) : 1f;
            List<Building> sites = new List<Building>();
            foreach (Building b in Building.all) if (b != null && b.owner == p && b.tile != null && b.Purchasable().Count > 0) sites.Add(b);
            sites.Sort((a, b) => CombatMath.CompareTiles(a.tile, b.tile));
            Dictionary<string, int> have = new Dictionary<string, int>();
            foreach (Unit u in map.ownUnits) if (u != null) { string n = GameController.UnitName(u); int c; have.TryGetValue(n, out c); have[n] = c + 1; }

            // Each front's nearest producer of each kind of want: fighting units, supply units, trucks.
            Dictionary<AILogistics.Task, Building>[] nearest = { new Dictionary<AILogistics.Task, Building>(), new Dictionary<AILogistics.Task, Building>(), new Dictionary<AILogistics.Task, Building>() };
            if (logistics != null)
                foreach (AILogistics.Task task in logistics.tasks)
                    for (int kind = 0; kind < 3; kind++) {
                        Building near = null; int nd = int.MaxValue;
                        foreach (Building b in sites) {
                            bool builds = false;
                            foreach (Unit t in b.Purchasable()) if (Kind(t) == kind) { builds = true; break; }
                            if (!builds) continue;
                            int d = int.MaxValue;
                            foreach (Unit u in task.front.units) if (u != null && u.tile != null) d = Mathf.Min(d, map.Travel(null, b.tile, u.tile));
                            if (d < nd || (d == nd && near != null && CombatMath.CompareTiles(b.tile, near.tile) < 0)) { nd = d; near = b; }
                        }
                        if (near != null) nearest[kind][task] = near;
                    }
            int freeTrucks = logistics != null ? logistics.freeTrucks : 0;
            // Riders waiting (assaultRides): our fighting units far from the fight, on foot, less the room in empty troop
            // transports we have (not those held for supply, AIAssault.SupplyTransports). A transport bought takes its room off.
            List<Unit> waitingRiders = new List<Unit>();
            int troopRoom = 0;
            // Sea lift (seaLift, GroundOf): land units whose ground is across water, with where; and the room in our ships.
            // Only a ship gets them there: a truck or an APC helps none of them, and a ship none of the others.
            Dictionary<Unit, HexTile> seaRiders = new Dictionary<Unit, HexTile>();
            int seaRoom = 0;
            System.Func<Unit, HexTile> groundOf = GroundOf;
            HashSet<Unit> forSupply = profile.assaultRides ? AIAssault.SupplyTransports(map.player, map, null) : new HashSet<Unit>();
            if (profile.assaultRides && profile.assaultTransportWeight > 0f) {
                foreach (Unit u in map.ownUnits) {
                    if (u == null || u.tile == null) continue;
                    if (Transporting.IsTransport(u) && u.Firepower <= 1) {
                        if (groundOf != null && LocomotionRules.IsNaval(u.locomotion)) seaRoom += Transporting.FreeWeight(u);
                        else if (!forSupply.Contains(u)) troopRoom += Transporting.FreeWeight(u);
                        continue;
                    }
                    if (u.trasnporter != null || u.Hub != null || u.Firepower <= 0) continue;
                    HexTile g = groundOf != null && !LocomotionRules.IsAirborne(u.locomotion) && !LocomotionRules.IsNaval(u.locomotion) ? groundOf(u) : null;
                    if (g != null && map.AcrossWater(u.tile, g)) { seaRiders[u] = g; continue; }
                    if (u.locomotion == Locomotion.Boots && map.TurnsToContact(u) > profile.assaultBoardTurns) waitingRiders.Add(u);
                }
                int seaWeight = 0; foreach (Unit u in seaRiders.Keys) seaWeight += Mathf.Max(1, u.weight);
                if (trace != null && seaRiders.Count > 0) trace("   sea lift: " + seaRiders.Count + " land units want to be across water (weight " + seaWeight + "), room in our ships " + seaRoom);
            }
            if (trace != null && profile.assaultRides) trace("   riders waiting for a transport: " + waitingRiders.Count + ", room in empty troop transports " + troopRoom);
            int troopPlanned = 0, seaPlanned = 0;
            // Room in empty troop transports beside each base (assaultFillBonus), and infantry planned there this turn.
            Dictionary<Building, int> roomAt = new Dictionary<Building, int>(), fillPlanned = new Dictionary<Building, int>();
            if (profile.assaultRides && profile.assaultFillBonus > 0f)
                foreach (Building b in sites) {
                    int room = 0;
                    foreach (Unit u in map.ownUnits) {
                        if (u == null || u.tile == null || !Transporting.IsTransport(u) || u.Firepower > 1 || u.transported.Count > 0 || forSupply.Contains(u)) continue;
                        bool beside = b.tile != null && b.tile.Distance(u.tile) <= 1;
                        if (beside) room += Mathf.Max(1, u.transportSlots);
                    }
                    if (room > 0) roomAt[b] = room;
                }
            float riderWorth = 0f; foreach (Unit u in waitingRiders) riderWorth += AIScore.Worth(u);
            if (waitingRiders.Count > 0) riderWorth /= waitingRiders.Count;

            // The slots: every building that can build, its tile free.
            // (allSlots, procBudgets: every facility, its tile taken or not — one taken this turn keeps its budget and may save.)
            List<Slot> slots = new List<Slot>(), allSlots = new List<Slot>();
            foreach (Building b in sites)
                for (HexTile x = b.tile; x != null; x = null) {
                    bool any = false; foreach (Unit t in b.Purchasable()) if (t.cost > 0 && t.CanEnter(x)) { any = true; break; }
                    if (!any) continue;
                    Slot s = new Slot { b = b, tile = x, free = x.occupier == null && AIAir.MayBuildOn(x, map) };
                    allSlots.Add(s);
                    if (s.free) slots.Add(s);
                }

            // Where the fight is (productionTimeToFront): the enemy units we know of, else the enemy's HQ.
            List<HexTile> fight = new List<HexTile>();
            if (profile.productionTimeToFront) {
                foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && e.Firepower > 0) fight.Add(e.tile);
                FogOfWar.Knowledge fk = FogOfWar.current;
                if (fk != null) foreach (FogOfWar.Phantom ph in fk.phantoms) if (ph.last != null && ph.kind != null && ph.kind.firepower > 0) fight.Add(ph.last);
                if (fight.Count == 0) foreach (Building b in Building.all) if (b != null && b.IsHQ && b.tile != null && (FogOfWar.OwnerAsSeen(p, b) ?? b.owner) != p && b.owner != null) fight.Add(b.tile);
            }
            Dictionary<string, float> timeFactor = new Dictionary<string, float>();
            Reach reach = new Reach(fight);
            // Its worth there: divided by (1 + turns to the fight / horizon), by its own movement.
            System.Func<Unit, HexTile, float> toFight = (t, at) => {
                if (fight.Count == 0 || t.moveRange <= 0) return 1f;
                string key = GameController.UnitName(t) + "@" + at.x + "," + at.y;
                float f;
                if (timeFactor.TryGetValue(key, out f)) return f;
                int d = int.MaxValue;
                // procTerrainReach: in move points over the terrain by its own movement (aircraft: a point a tile).
                if (profile.procTerrainReach) d = reach.CostToSet(t, at);
                if (d == int.MaxValue) foreach (HexTile x in fight) d = Mathf.Min(d, map.Travel(t, at, x));
                int turns = Mathf.Max(0, Mathf.CeilToInt(Mathf.Max(0, d - Mathf.Max(1, t.AttackRange)) / (float)t.moveRange) - 1);
                f = 1f / (1f + turns / Mathf.Max(0.1f, profile.productionTimeHorizon));
                timeFactor[key] = f;
                return f;
            };
            // Relative to the quickest to the fight of what we can build anywhere: where and what we build shifts
            // toward what gets there, but fighting units as a whole lose nothing against supply units and trucks.
            // (Scaled absolutely, every fighting unit lost half its worth on a big map and the gold went to saving
            // for supply units: an army of twelve stood still for twenty turns.)
            Building reserveProducer = null;
            if (profile.reserveDemand && ReserveRally != null && ReserveShort > 0f) {
                int rd = int.MaxValue;
                // The nearest that can build something for the reserve (the HQ, nearest on ForestClearing, builds only infantry and supply).
                foreach (Slot sl in slots) {
                    bool can = false; foreach (Unit t in sl.b.Purchasable()) if (t.cost > 0 && t.CanEnter(sl.tile) && CanJoinReserve(t, profile)) { can = true; break; }
                    if (!can) continue;
                    int d = sl.b.tile != null ? sl.b.tile.Distance(ReserveRally) : int.MaxValue; if (d < rd) { rd = d; reserveProducer = sl.b; }
                }
            }
            float quickest = 0f;
            if (profile.productionTimeToFront && fight.Count > 0)
                foreach (Slot sl in slots) foreach (Unit t in sl.b.Purchasable()) if (Kind(t) == 0 && t.cost > 0 && t.CanEnter(sl.tile)) quickest = Mathf.Max(quickest, toFight(t, sl.tile));
            int money = p.money;
            int supplyPlanned = 0, trucksPlanned = 0;

            // ===== procCargo: every transport by what it carries, on each one's own path. =====
            // Riders: our land units far from where they are going (or across water from it), with where (null: the fight).
            List<KeyValuePair<Unit, HexTile>> cargoRiders = new List<KeyValuePair<Unit, HexTile>>();
            List<KeyValuePair<Unit, HexTile>> carriers = new List<KeyValuePair<Unit, HexTile>>();   // empty transports we have, and those planned this turn (at their facility)
            if (profile.procCargo) {
                foreach (Unit u in map.ownUnits) {
                    if (u == null || u.tile == null) continue;
                    if (IsCarrier(u)) { if (u.trasnporter == null && Transporting.FreeWeight(u) > 0 && !forSupply.Contains(u)) carriers.Add(new KeyValuePair<Unit, HexTile>(u, u.tile)); continue; }
                    if (u.trasnporter != null || u.Hub != null || u.Firepower <= 0 || LocomotionRules.IsAirborne(u.locomotion) || LocomotionRules.IsNaval(u.locomotion)) continue;
                    HexTile g = groundOf != null ? groundOf(u) : null;
                    bool across = g != null && map.AcrossWater(u.tile, g);
                    if (!across && map.TurnsToContact(u) <= profile.assaultBoardTurns) continue;
                    if (g == null && fight.Count == 0) continue;
                    cargoRiders.Add(new KeyValuePair<Unit, HexTile>(u, g));
                }
                if (trace != null && cargoRiders.Count > 0) trace("   cargo: " + cargoRiders.Count + " land units far from where they are going, " + carriers.Count + " empty transports");
            }
            Dictionary<Unit, float> riderValue = new Dictionary<Unit, float>();
            System.Func<Unit, float> valueOf = u => { float v; if (!riderValue.TryGetValue(u, out v)) riderValue[u] = v = Value(u, u.tile, map); return v; };
            // A carrier's turns to take a rider where it is going: fetch it (a plane that needs a runway: the rider comes to
            // the runway), then ride. Aircraft and ships by the tile, ground transports over the terrain. Infinity: it cannot.
            System.Func<Unit, HexTile, Unit, HexTile, float> carryTurns = (c, from, r, goal) => {
                bool flies = LocomotionRules.IsAirborne(c.locomotion), sails = LocomotionRules.IsNaval(c.locomotion);
                System.Func<HexTile, HexTile, float> leg = (a, b) => {
                    if (flies || sails) return b != null ? a.Distance(b) / (float)Mathf.Max(1, c.moveRange) : (fight.Count == 0 ? float.PositiveInfinity : DistToSet(a, fight) / (float)Mathf.Max(1, c.moveRange));
                    return reach.Turns(c, a, b);
                };
                float fetch;
                if (flies && LocomotionRules.NeedsBase(c.locomotion) && !LocomotionRules.ResuppliedInSupply(c.locomotion)) { fetch = reach.Turns(r, r.tile, from); return fetch + leg(from, goal); }
                fetch = leg(from, r.tile);
                return fetch + leg(r.tile, goal);
            };
            // What the riders a carrier could serve gain from it: the riders the transports we have (and those planned) do
            // not already serve, the best for it first, as many as it carries.
            System.Func<Unit, HexTile, KeyValuePair<float, int>> cargoGain = (t, at) => {
                Dictionary<Unit, bool> served = new Dictionary<Unit, bool>();
                System.Func<Unit, HexTile, Unit, HexTile, float> gainOf = (c, from, r, goal) => {
                    if (!Transporting.CarriesKind(c, r)) return 0f;
                    float walk = reach.Turns(r, r.tile, goal);
                    float ride = 1f + carryTurns(c, from, r, goal);
                    if (float.IsInfinity(ride)) return 0f;
                    // Across water: without it, never there (the army short of its share, reserveDemand, is short because these cannot get there: the same pull).
                    if (float.IsInfinity(walk)) return profile.seaLiftWeight / (1f + ride / Mathf.Max(0.1f, profile.productionTimeHorizon))
                                                       * (profile.reserveDemand && ReserveShort > 0f ? (1f + ReserveShort) * profile.productionFrontWeight : 1f);
                    return profile.assaultTransportWeight * Mathf.Clamp01((walk - ride) / Mathf.Max(1f, profile.productionLifeRef));
                };
                // The transports we have, and those planned: each takes the riders it gains most for, as many as it carries.
                foreach (KeyValuePair<Unit, HexTile> c in carriers) {
                    int room = c.Key.tile != null ? Transporting.FreeWeight(c.Key) : Mathf.Max(1, c.Key.transportSlots);
                    List<KeyValuePair<Unit, float>> mine = new List<KeyValuePair<Unit, float>>();
                    foreach (KeyValuePair<Unit, HexTile> r in cargoRiders) {
                        if (served.ContainsKey(r.Key)) continue;
                        float gn = gainOf(c.Key, c.Value, r.Key, r.Value);
                        if (gn > 0f) mine.Add(new KeyValuePair<Unit, float>(r.Key, gn * valueOf(r.Key)));
                    }
                    mine.Sort((a, b) => { int cc = b.Value.CompareTo(a.Value); return cc != 0 ? cc : CombatMath.CompareTiles(a.Key.tile, b.Key.tile); });
                    foreach (KeyValuePair<Unit, float> m in mine) { int wgt = Mathf.Max(1, m.Key.weight); if (wgt > room) continue; room -= wgt; served[m.Key] = true; }
                }
                List<KeyValuePair<Unit, float>> gains = new List<KeyValuePair<Unit, float>>();
                foreach (KeyValuePair<Unit, HexTile> r in cargoRiders) {
                    if (served.ContainsKey(r.Key)) continue;
                    float gn = gainOf(t, at, r.Key, r.Value);
                    if (gn > 0f) gains.Add(new KeyValuePair<Unit, float>(r.Key, gn * valueOf(r.Key)));
                }
                gains.Sort((a, b) => { int cc = b.Value.CompareTo(a.Value); return cc != 0 ? cc : CombatMath.CompareTiles(a.Key.tile, b.Key.tile); });
                int left = Mathf.Max(1, t.transportSlots), n = 0; float tv = 0f;
                foreach (KeyValuePair<Unit, float> g in gains) { int wgt = Mathf.Max(1, g.Key.weight); if (wgt > left) continue; left -= wgt; tv += g.Value; n++; }
                return new KeyValuePair<float, int>(tv, n);
            };
            int resupPlannedAir = 0, resupPlannedSea = 0;
            Dictionary<string, int> picked = new Dictionary<string, int>();
            // What a unit would add in this slot, given what is planned so far.
            System.Func<Unit, Slot, KeyValuePair<float, string>> worth = null;
            worth = (t, sl) => {
                // procCargo: a transport that does not fight, of any kind, is worth what it carries — riders the transports
                // we have do not serve, the turns sooner it gets them where they are going; and, with no truck free, a front's
                // supply short for want of a ride (a supply unit it can carry), the sooner the nearer.
                if (profile.procCargo && IsCarrier(t)) {
                    if (t.cost <= 0 || !t.CanEnter(sl.tile)) return new KeyValuePair<float, string>(-1f, null);
                    if (profile.productionAvoidFire && map.KnownLoss(t, sl.tile) >= Mathf.Max(1, t.Strength > 0 ? t.Strength : 6)) return new KeyValuePair<float, string>(0f, "destroyed there next turn");
                    KeyValuePair<float, int> cg = cargoGain(t, sl.tile);
                    float cv = cg.Key; string ctag = cg.Value > 0 ? "cargo: " + cg.Value + " units it gets where they are going sooner" : null;
                    if (DebugValues != null) DebugValues(GameController.UnitName(t) + " at " + sl.tile.x + "," + sl.tile.y + " for cargo: " + cv.ToString("0") + " (" + cg.Value + " of " + cargoRiders.Count + " riders)");
                    if (logistics != null && freeTrucks + trucksPlanned <= 0) {
                        bool carriesSupply = false;
                        foreach (Unit h in map.ownUnits) if (h != null && h.Hub != null && !h.Hub.producer && Transporting.CarriesKind(t, h)) { carriesSupply = true; break; }
                        if (!carriesSupply) foreach (Unit h in sl.b.Purchasable()) if (Kind(h) == 1 && Transporting.CarriesKind(t, h)) { carriesSupply = true; break; }
                        if (carriesSupply)
                            foreach (AILogistics.Task task in logistics.tasks) {
                                if (task.ordinary || !task.short_ || task.front == null || task.front.target == null) continue;
                                float there = LocomotionRules.IsAirborne(t.locomotion) || LocomotionRules.IsNaval(t.locomotion) ? sl.tile.Distance(task.front.target) / (float)Mathf.Max(1, t.moveRange) : reach.Turns(t, sl.tile, task.front.target);
                                if (float.IsInfinity(there)) continue;
                                float fv = task.unsupplied * 0.5f * profile.productionSupplyWeight * supplyScale / (1f + there / Mathf.Max(0.1f, profile.productionTimeHorizon));
                                if (fv > cv) { cv = fv; ctag = task.front.Id + ": no transport for its supply"; }
                            }
                    }
                    return new KeyValuePair<float, string>(cv, ctag);
                }
                // procResupply: an air tanker (a supply ship) is worth our aircraft (ships) running low, shared with those we have.
                if (profile.procResupply && IsResupplier(t)) {
                    if (t.cost <= 0 || !t.CanEnter(sl.tile)) return new KeyValuePair<float, string>(-1f, null);
                    bool air = t.airTanker;
                    float rv = 0f; int low = 0, have_ = 0;
                    foreach (Unit u in map.ownUnits) {
                        if (u == null || u.tile == null) continue;
                        if (IsResupplier(u) && u.airTanker == air) { have_++; continue; }
                        if (air ? !LocomotionRules.IsAirborne(u.locomotion) : !LocomotionRules.IsNaval(u.locomotion)) continue;
                        float lvl = 1f;
                        if (u.fuelMax > 0) lvl = Mathf.Min(lvl, u.fuel / (float)u.fuelMax);
                        if (u.suppliesMax > 0) lvl = Mathf.Min(lvl, u.supplies / (float)u.suppliesMax);
                        if (lvl >= profile.procResupplyLow) continue;
                        low++; rv += valueOf(u) * (1f - lvl);
                    }
                    rv /= 1f + have_ + (air ? resupPlannedAir : resupPlannedSea);
                    return new KeyValuePair<float, string>(rv, low > 0 ? low + (air ? " aircraft" : " ships") + " running low" : null);
                }
                // Nothing to shoot at (productionPossibleFoes): anti-air where the enemy has and can build no aircraft.
                if (!HurtsPossible(t, sl.tile)) return new KeyValuePair<float, string>(0f, "hurts nothing the enemy has or can build");
                // Destroyed where it stands next turn (productionAvoidFire): the enemy we see or remember has the building in range.
                if (profile.productionAvoidFire && map.KnownLoss(t, sl.tile) >= Mathf.Max(1, t.Strength > 0 ? t.Strength : 6)) return new KeyValuePair<float, string>(0f, "destroyed there next turn");
                int kindOf = Kind(t);
                // A ship for the sea lift: what it adds to the land units waiting to cross (the nearest, as much as it carries)
                // — each one's value, discounted for the turns until a ship bought now has it at its ground.
                if (seaRiders.Count > 0 && t.cost > 0 && t.CanEnter(sl.tile) && LocomotionRules.IsNaval(t.locomotion) && Transporting.IsTransport(t) && t.Firepower <= 1) {
                    int seaShort = -seaRoom - seaPlanned; foreach (Unit u in seaRiders.Keys) seaShort += Mathf.Max(1, u.weight);
                    if (seaShort > 0) {
                        List<Unit> near = new List<Unit>(seaRiders.Keys);
                        near.Sort((x, y) => { int c = map.Travel(t, sl.tile, x.tile).CompareTo(map.Travel(t, sl.tile, y.tile)); return c != 0 ? c : CombatMath.CompareTiles(x.tile, y.tile); });
                        int room = Mathf.Min(Mathf.Max(1, t.transportSlots), seaShort);
                        float tv = 0f; int counted = 0, used = 0;
                        foreach (Unit rider in near) {
                            if (used >= room) break;
                            if (!Transporting.CarriesKind(t, rider) || used + Mathf.Max(1, rider.weight) > room) continue;
                            int fetch = map.Travel(t, sl.tile, rider.tile), ride = map.Travel(t, rider.tile, seaRiders[rider]);
                            float rideTurns = 1f + (fetch + ride) / (float)Mathf.Max(1, t.moveRange);   // built this turn, out next
                            // Without a ship it never gets there: all of its value, discounted for the turns to get it there as a
                            // unit bought for the fight is (productionTimeHorizon).
                            float gain = 1f / (1f + rideTurns / Mathf.Max(0.1f, profile.productionTimeHorizon));
                            tv += Value(rider, rider.tile, map) * gain;
                            used += Mathf.Max(1, rider.weight); counted++;
                        }
                        tv *= profile.seaLiftWeight;
                        // The army short of its share (reserveDemand) is short because these cannot get there: the same pull.
                        if (profile.reserveDemand && ReserveShort > 0f) tv *= (1f + ReserveShort) * profile.productionFrontWeight;
                        if (DebugValues != null) DebugValues(GameController.UnitName(t) + " at " + sl.tile.x + "," + sl.tile.y + " for the sea lift: " + tv.ToString("0") + " (" + counted + " units)");
                        if (tv > 0f) return new KeyValuePair<float, string>(tv, "sea lift: " + counted + " land units to take across the water");
                    }
                }
                // A troop transport (assaultRides): what it adds to the riders waiting for room (the nearest of them, as many as
                // it carries) — each one's value, times the turns sooner it reaches the fight riding (fetched from where it
                // stands) than walking, over the turns its value is measured on (productionLifeRef), at most all of it.
                if (waitingRiders.Count > 0 && t.cost > 0 && t.CanEnter(sl.tile) && Transporting.IsTransport(t) && t.Firepower <= 1 && fight.Count > 0) {
                    int short_ = waitingRiders.Count - troopRoom - troopPlanned;
                    if (short_ > 0) {
                        List<Unit> near = new List<Unit>(waitingRiders);
                        near.Sort((x, y) => sl.tile.Distance(x.tile).CompareTo(sl.tile.Distance(y.tile)));
                        int carried = Mathf.Min(Mathf.Max(1, t.transportSlots), short_);
                        float tv = 0f; int counted = 0;
                        for (int i = 0; i < near.Count && counted < carried; i++) {
                            Unit rider = near[i];
                            int walk = int.MaxValue, ride = int.MaxValue;
                            foreach (HexTile x in fight) { walk = Mathf.Min(walk, map.Travel(rider, rider.tile, x)); ride = Mathf.Min(ride, map.Travel(t, rider.tile, x)); }
                            int fetch = map.Travel(t, sl.tile, rider.tile);
                            // (A transport not yet bought has no owner: whether it carries its kind.)
                            if (!Transporting.CarriesKind(t, rider) || walk >= int.MaxValue / 2 || ride >= int.MaxValue / 2 || fetch >= int.MaxValue / 2) continue;
                            float walkTurns = walk / (float)Mathf.Max(1, rider.moveRange);
                            float rideTurns = 1f + (fetch + ride) / (float)Mathf.Max(1, t.moveRange);   // built this turn, out next
                            // Each turn sooner at the fight is a turn of what it does there (its value is over productionLifeRef turns).
                            float gain = Mathf.Clamp01((walkTurns - rideTurns) / Mathf.Max(1f, profile.productionLifeRef));
                            if (gain <= 0f) continue;
                            tv += Value(rider, rider.tile, map) * gain;
                            counted++;
                        }
                        tv *= profile.assaultTransportWeight;
                        if (DebugValues != null) DebugValues(GameController.UnitName(t) + " at " + sl.tile.x + "," + sl.tile.y + " for riders: " + tv.ToString("0") + " (" + counted + " riders)");
                        if (tv > 0f) return new KeyValuePair<float, string>(tv, "riders: " + short_ + " far from the fight without room");
                    }
                }
                if (kindOf < 0 || t.cost <= 0 || !t.CanEnter(sl.tile)) return new KeyValuePair<float, string>(-1f, null);
                string name = GameController.UnitName(t);
                int n, m; have.TryGetValue(name, out n); picked.TryGetValue(name, out m);
                float mix = Mathf.Pow(Mathf.Clamp01(profile.productionVariety), n + m);
                float v = 0f; string tag = null;
                if (kindOf == 0) {
                    v = Value(t, sl.tile, map) * mix;
                    // Beside an empty troop transport (or one bought here this turn): it fills it.
                    if (profile.assaultRides && profile.assaultFillBonus > 0f && t.locomotion == Locomotion.Boots) {
                        int room, filled; roomAt.TryGetValue(sl.b, out room); fillPlanned.TryGetValue(sl.b, out filled);
                        if (room > filled) { v *= 1f + profile.assaultFillBonus; tag = "fills the transport beside it"; }
                    }
                    // The reserve (reserveDemand): at the producer nearest where it gathers, as a front short by ReserveShort.
                    if (profile.reserveDemand && ReserveRally != null && ReserveShort > 0f && sl.b == reserveProducer && CanJoinReserve(t, profile)) {
                        float fv = Value(t, sl.tile, map) * mix * (1f + ReserveShort) * profile.productionFrontWeight;
                        if (fv > v) { v = fv; tag = "the reserve (short " + (ReserveShort * 100f).ToString("0") + "%)"; }
                    }
                    foreach (KeyValuePair<AILogistics.Task, Building> kv in nearest[0]) {
                        if (kv.Value != sl.b) continue;
                        AILogistics.Task f = kv.Key;
                        List<Unit> foes = new List<Unit>(f.front.enemies);
                        foreach (Unit e in f.front.arriving.Keys) if (!foes.Contains(e)) foes.Add(e);
                        if (foes.Count == 0) continue;
                        float shortBy = Mathf.Max(0f, f.front.needPower - f.front.assignedPower) / Mathf.Max(1f, f.front.needPower);
                        float fv = ValueVs(t, sl.tile, foes) * mix * (1f + shortBy) * profile.productionFrontWeight;
                        if (fv > v) { v = fv; tag = f.front.Id; }
                    }
                    if (profile.productionTimeToFront && quickest > 0f) {
                        float tf = Mathf.Min(1f, toFight(t, sl.tile) / quickest);
                        if (tf < 1f) { v *= tf; tag = (tag ?? "") + (tag != null ? ", " : "") + "x" + tf.ToString("0.00") + " for its time to the fight"; }
                    }
                } else if (kindOf == 1) {
                    if (hubsHave + supplyPlanned >= supplyCap) return new KeyValuePair<float, string>(-1f, null);
                    foreach (KeyValuePair<AILogistics.Task, Building> kv in nearest[1]) {
                        if (kv.Value != sl.b || !kv.Key.short_) continue;
                        float fv = kv.Key.unsupplied * profile.productionSupplyWeight * supplyScale * mix;
                        if (fv > v) { v = fv; tag = kv.Key.front.Id + ": its supply is short"; }
                    }
                    if (logistics != null && logistics.supplyDemand > 0f) {
                        // The campaign's next front, a supply unit away: worth the force it would let go (its need), not the objective's whole value.
                        float dv = Mathf.Min(logistics.supplyDemand, logistics.supplyDemandNeed > 0f ? logistics.supplyDemandNeed : logistics.supplyDemand) * profile.productionSupplyWeight * supplyScale * mix;
                        if (dv > v) { v = dv; tag = logistics.supplyDemandFor + ": no supply unit free for it"; }
                    }
                } else {
                    if (freeTrucks + trucksPlanned > 0) return new KeyValuePair<float, string>(-1f, null);
                    foreach (KeyValuePair<AILogistics.Task, Building> kv in nearest[2]) {
                        if (kv.Value != sl.b || !kv.Key.short_) continue;
                        float fv = kv.Key.unsupplied * 0.5f * profile.productionSupplyWeight * supplyScale * mix;
                        if (fv > v) { v = fv; tag = kv.Key.front.Id + ": no truck for its supply"; }
                    }
                }
                return new KeyValuePair<float, string>(v, tag);
            };
            System.Action<Unit, int> count = (t, d) => {
                string name = GameController.UnitName(t); int c; picked.TryGetValue(name, out c); picked[name] = c + d;
                int k = Kind(t); if (k == 1) supplyPlanned += d; else if (k == 2) trucksPlanned += d;
                if (Transporting.IsTransport(t) && t.Firepower <= 1) {
                    if (groundOf != null && LocomotionRules.IsNaval(t.locomotion)) seaPlanned += d * Mathf.Max(1, t.transportSlots);
                    else troopPlanned += d * Mathf.Max(1, t.transportSlots);
                }
            };
            System.Action<Unit, Slot, int> plan = (t, sl, d) => {
                count(t, d);
                if (profile.procCargo && IsCarrier(t)) {
                    trucksPlanned += Kind(t) == 2 ? 0 : d;   // (count() has the ground ones)
                    if (d > 0) carriers.Add(new KeyValuePair<Unit, HexTile>(t, sl.tile));
                    else { int i = carriers.FindIndex(x => x.Key == t && x.Value == sl.tile); if (i >= 0) carriers.RemoveAt(i); }
                }
                if (IsResupplier(t)) { if (t.airTanker) resupPlannedAir += d; else resupPlannedSea += d; }
                if (Transporting.IsTransport(t) && t.Firepower <= 1) { int r0; roomAt.TryGetValue(sl.b, out r0); roomAt[sl.b] = r0 + d * Mathf.Max(1, t.transportSlots); }
                if (t.locomotion == Locomotion.Boots && Kind(t) == 0) { int f0; fillPlanned.TryGetValue(sl.b, out f0); fillPlanned[sl.b] = f0 + d; }
                if (Kind(t) == 0) { if (d > 0) planned.Add(new KeyValuePair<Unit, HexTile>(t, sl.tile)); else planned.RemoveAll(x => x.Key == t && x.Value == sl.tile); scarce = Scarcity(allFoes, map, profile); if (tierCount != null) tierCount[Tier(t)] += d; }
            };

            // ===== Procurement (AIProfile.procurement): the inputs, the fronts' pressure, then one plan for every facility. =====
            Proc proc = profile.procurement ? new Proc(p, map, profile, profile.procBudgets ? allSlots : slots, logistics, ourPower) : null;
            if (proc != null) {
                System.Func<Unit, Slot, KeyValuePair<float, string>> baseWorth = worth;
                worth = (t, sl) => proc.Worth(t, sl, baseWorth(t, sl));
                if (trace != null) trace("   " + p.personal_name + " procurement: " + proc.Describe());
            }
            // procBudgets: each facility buys now or saves, from its own budget (Budgets below); nothing else buys.
            bool budgeted = proc != null && profile.procBudgets;
            if (budgeted) money = Budgets(p, profile, proc, allSlots, worth, plan, money, trace);

            // Saving for the reserve (reserveDemand): what was saved for, bought as soon as it can be paid for.
            string savingFor; lock (reserveSaving) reserveSaving.TryGetValue(p, out savingFor);
            if (proc != null) savingFor = null;   // procurement saves only for gaps, transit and supply
            bool boughtSaved = budgeted;
            if (savingFor != null) {
                foreach (Slot sl in slots) {
                    if (sl.pick != null) continue;
                    bool offers = false; foreach (Unit t in sl.b.Purchasable()) if (GameController.UnitName(t) == savingFor && t.CanEnter(sl.tile)) offers = true;
                    if (!offers) continue;
                    foreach (Unit t in sl.b.Purchasable())
                        if (GameController.UnitName(t) == savingFor && t.cost <= money && t.CanEnter(sl.tile)) {
                            KeyValuePair<float, string> w = worth(t, sl);
                            sl.pick = t; sl.value = w.Key; sl.why = "the reserve (saved for)"; money -= t.cost; plan(t, sl, 1); boughtSaved = true;
                            lock (reserveSaving) reserveSaving.Remove(p);
                            break;
                        }
                    break;
                }
            }
            // Saving up: a supply unit (or truck) worth more for its cost than anything we can pay for now,
            // but dearer than the gold we have: nothing bought this turn, the gold kept for it. (Not the turn the reserve's
            // saved-for unit is bought: the saving rule returned before buying it.)
            if (!boughtSaved) {
                float bestAfford = 0f, bestSupply = 0f; Unit supplyPick = null; Building supplyAt = null;
                foreach (Slot sl in slots)
                    foreach (Unit t in sl.b.Purchasable()) {
                        KeyValuePair<float, string> w = worth(t, sl);
                        if (w.Key <= 0f) continue;
                        float r = w.Key / t.cost;
                        if (t.cost <= money) bestAfford = Mathf.Max(bestAfford, r);
                        else if (Kind(t) != 0 && r > bestSupply) { bestSupply = r; supplyPick = t; supplyAt = sl.b; }
                        // The reserve's unit at its producer (reserveDemand): saved for the same way.
                        else if (profile.reserveDemand && sl.b == reserveProducer && Kind(t) == 0 && CanJoinReserve(t, profile) && w.Value != null && w.Value.StartsWith("the reserve") && r > bestSupply) { bestSupply = r; supplyPick = t; supplyAt = sl.b; }
                    }
                if (proc != null) { supplyPick = null; proc.SaveTarget(slots, worth, money, ref supplyPick, ref supplyAt, ref bestSupply, ref bestAfford); }
                bool forReserve = supplyPick != null && Kind(supplyPick) == 0 && proc == null;
                if (forReserve) { string was; lock (reserveSaving) reserveSaving.TryGetValue(p, out was); if (was != null && was != GameController.UnitName(supplyPick)) forReserve = false; }
                if (supplyPick != null && bestSupply > bestAfford && (proc != null || Kind(supplyPick) != 0 || forReserve || !reserveSaving.ContainsKey(p))) {
                    if (Kind(supplyPick) == 0 && proc == null) lock (reserveSaving) reserveSaving[p] = GameController.UnitName(supplyPick);
                    if (trace != null) trace("   " + p.personal_name + " saves for " + GameController.UnitName(supplyPick) + (Kind(supplyPick) == 0 ? " (the reserve)" : "") + " at " + supplyAt.name.Replace("(Clone)", "") + " (" + (bestSupply * 1000f).ToString("0.0") + " a 1000 gold, against " + (bestAfford * 1000f).ToString("0.0") + "): " + money + " of " + supplyPick.cost);
                    planned = null;
                    return bought;
                }
            }
            // Procurement: the set of purchases, one per facility, with the most value in all for the gold (three infantry
            // against one tank is compared whole, not unit by unit).
            if (proc != null && !budgeted) {
                List<Unit> picks; List<float> vals; List<string> whys;
                proc.Choose(slots, worth, money, out picks, out vals, out whys);
                for (int i = 0; i < slots.Count; i++) if (picks[i] != null) { slots[i].pick = picks[i]; slots[i].value = vals[i]; slots[i].why = whys[i]; money -= picks[i].cost; plan(picks[i], slots[i], 1); }
                if (trace != null) trace("   " + p.personal_name + " procurement options: " + proc.ChooseNote);
            }
            // First: each slot, in turn, the best value for its cost that can be paid for now.
            for (int pass = 0; proc == null && pass < slots.Count; pass++) {
                Slot best = null; Unit bu = null; float br = 0f, bv = 0f; string bw = null;
                // productionAbsolute: the most value outright, as long as every other empty slot can still buy its cheapest; only
                // when gold is short does value for its cost decide. (Value per gold bought infantry turn after turn.)
                int keep = 0;
                if (profile.productionAbsolute) {
                    int empty = 0, cheapest = int.MaxValue;
                    foreach (Slot sl in slots) if (sl.pick == null) { empty++; foreach (Unit t in sl.b.Purchasable()) if (t.cost > 0 && t.CanEnter(sl.tile)) cheapest = Mathf.Min(cheapest, t.cost); }
                    if (cheapest < int.MaxValue) keep = Mathf.Max(0, empty - 1) * cheapest;
                }
                bool outright = profile.productionAbsolute && money - keep > 0;
                for (int tryOutright = outright ? 0 : 1; tryOutright < 2 && best == null; tryOutright++) {
                outright = tryOutright == 0;
                foreach (Slot sl in slots) {
                    if (sl.pick != null) continue;
                    foreach (Unit t in sl.b.Purchasable()) {
                        if (t.cost > money || (outright && t.cost > money - keep)) continue;
                        KeyValuePair<float, string> w = worth(t, sl);
                        if (w.Key <= 0f) continue;
                        float r = outright ? w.Key : w.Key / t.cost;
                        if (r > br || (r == br && bu != null && string.CompareOrdinal(t.name, bu.name) < 0)) { br = r; best = sl; bu = t; bv = w.Key; bw = w.Value; }
                    }
                }
                }   // nothing worth it outright within the gold kept for the other slots: value for its cost, as before
                if (best == null) break;
                best.pick = bu; best.value = bv; best.why = bw; money -= bu.cost; plan(bu, best, 1);
            }
            // The reserve's demand in the trace: what the reserve-capable kinds were worth at its producer, against what was picked.
            if (trace != null && reserveProducer != null) {
                foreach (Slot sl in slots) {
                    if (sl.b != reserveProducer) continue;
                    List<string> opts = new List<string>();
                    foreach (Unit t in sl.b.Purchasable()) {
                        if (!CanJoinReserve(t, profile) || t.cost <= 0 || !t.CanEnter(sl.tile)) continue;
                        KeyValuePair<float, string> w = worth(t, sl);
                        opts.Add(GameController.UnitName(t) + " " + w.Key.ToString("0") + " (" + (w.Key / t.cost * 1000f).ToString("0") + "/1000g" + (t.cost > money + (sl.pick != null ? sl.pick.cost : 0) ? ", cannot afford" : "") + ")");
                    }
                    trace("   " + p.personal_name + " reserve demand (short " + (ReserveShort * 100f).ToString("0") + "%) at " + sl.b.name.Replace("(Clone)", "") + ": " + string.Join(", ", opts.ToArray())
                          + "; picked " + (sl.pick != null ? GameController.UnitName(sl.pick) + " " + sl.value.ToString("0") + " (" + (sl.value / sl.pick.cost * 1000f).ToString("0") + "/1000g)" : "nothing") + ", gold " + money);
                    break;
                }
            }
            // Then: gold left over goes to upgrades, the most value per extra gold first (AIProfile.productionSpendAll).
            for (int guard = 0; proc == null && profile.productionSpendAll && money > 0 && guard < 64; guard++) {
                Slot best = null; Unit bu = null; float br = 0f, bv = 0f; string bw = null;
                foreach (Slot sl in slots) {
                    if (sl.pick == null) continue;
                    Unit was = sl.pick; plan(was, sl, -1);
                    // (Within its role: a transport bought for riders is not traded for a fighting unit, nor the other way.)
                    bool troopWas = Transporting.IsTransport(was) && was.Firepower <= 1;
                    foreach (Unit t in sl.b.Purchasable()) {
                        int extra = t.cost - was.cost;
                        if (extra <= 0 || extra > money) continue;
                        if ((Transporting.IsTransport(t) && t.Firepower <= 1) != troopWas || Kind(t) != Kind(was) && !troopWas) continue;
                        KeyValuePair<float, string> w = worth(t, sl);
                        float gain = w.Key - sl.value;
                        if (gain <= 0f) continue;
                        float r = gain / extra;
                        if (r > br) { br = r; best = sl; bu = t; bv = w.Key; bw = w.Value; }
                    }
                    plan(was, sl, 1);
                }
                if (best == null) break;
                plan(best.pick, best, -1);
                money -= bu.cost - best.pick.cost;
                best.pick = bu; best.value = bv; best.why = bw; plan(bu, best, 1);
            }
            if (trace != null) {
                int filled = 0; foreach (Slot sl in slots) if (sl.pick != null) filled++;
                trace("   " + p.personal_name + " buys against " + allFoes.Count + " enemy units (" + map.enemyUnits.Count + " in sight): " + filled + " of " + slots.Count + " building slots, " + (p.money - money) + " of " + p.money + " gold; supply units " + hubsHave + " (at most " + supplyCap + ")");
            }

            foreach (Slot sl in slots) {
                if (sl.pick == null) continue;
                Unit made;
                using (BoardView.Outside()) made = BuildingController.bc.Produce(sl.b, sl.pick, x => AIAir.MayBuildOn(x, map));
                if (made != null && made.tile != null) made.tile.occupier = made;
                if (made == null) continue;
                bought.Add(made);
                if (trace != null) trace("   " + p.personal_name + " buys " + GameController.UnitName(sl.pick) + " at " + sl.b.name.Replace("(Clone)", "") + " (" + made.tile.x + "," + made.tile.y + ")" + (sl.why != null ? " for " + sl.why : "") + " (worth " + sl.value.ToString("0") + ", " + (sl.value / sl.pick.cost * 1000f).ToString("0.0") + " a 1000 gold), money left " + p.money);
                if (made != null && sl.why != null && (sl.why.StartsWith("the reserve") || sl.why.Contains("the reserve"))) { if (ReserveBought == null) ReserveBought = new List<Unit>(); ReserveBought.Add(made); }
            }
            planned = null;
            return bought;
        }

        /// <summary>For diagnostics: each unit <paramref name="b"/> can build, with its value against the enemy now, its cost, and value per cost.</summary>
        public static List<string> Rank(Player p, Building b, AIMap map) {
            List<string> r = new List<string>();
            foreach (Unit t in b.Purchasable()) {
                if (t.cost <= 0 || t.Hub != null || t.Firepower <= 0) continue;
                HexTile at = b.tile;
                float v = Value(t, at, map);
                r.Add(string.Format("{0,-24} cost {1,6} value {2,7:0} per 1000 gold {3,6:0.0}", GameController.UnitName(t), t.cost, v, v / t.cost * 1000f));
            }
            return r;
        }

        /// <summary>The tile of <paramref name="b"/>, if <paramref name="t"/> could be built there now.</summary>
        static HexTile FreeTile(Building b, Unit t, AIMap map) {
            HexTile x = b.tile;
            return x != null && x.occupier == null && t.CanEnter(x) && AIAir.MayBuildOn(x, map) ? x : null;
        }

        /// <summary>
        /// Gold: what a fresh <paramref name="t"/> would do to the enemy on the board (each unit hit
        /// where it stands), less half what they would do to it on <paramref name="at"/>. With no
        /// enemy left, its own worth.
        /// </summary>
        /// <summary>0 a fighting unit, 1 a supply unit (ground), 2 a truck (a ground transport that does not fight); -1 none of them.</summary>
        static int Kind(Unit t) {
            if (t == null) return -1;
            if (t.Hub != null) return !t.Hub.producer && !LocomotionRules.NeedsBase(t.locomotion) && !LocomotionRules.IsNaval(t.locomotion) && !LocomotionRules.IsAirborne(t.locomotion) ? 1 : -1;
            if (Transporting.IsTransport(t) && t.Firepower <= 1) return !LocomotionRules.IsAirborne(t.locomotion) && !LocomotionRules.IsNaval(t.locomotion) ? 2 : -1;
            return t.Firepower > 0 ? 0 : -1;
        }

        /// <summary>As Value, against <paramref name="foes"/> only (a front's enemy).</summary>
        static float ValueVs(Unit t, HexTile at, List<Unit> foes) {
            float worth = AIScore.Worth(t);
            float offence = 0f, defence = 0f, retal = 0f; int enemies = 0;
            foreach (Unit e in foes) {
                if (e == null || e.Strength <= 0) continue;
                enemies++;
                // Each enemy unit counted as much as our army lacks the means to fight it (Scarcity); on our
                // own ground (at), not wherever it stands now (one remembered under the fog is not seen there).
                float need = scarce != null && scarce.ContainsKey(e) ? scarce[e] : 1f;
                float k = Kills(t, e, at);
                offence += k * AIScore.Worth(e) * need;
                if (e.Firepower > 0) defence += Mathf.Min(6, Unit.calculateDamage(e, t, at, 0)) * worth;
                // productionRetaliation: attacking it from beside (a unit without range), the return fire, if it can answer.
                if (retaliationNow && k > 0f && t.AttackRange <= 1 && e.Firepower > 0 && e.retaliates && e.MinRange < 1 && e.AttackRange >= 1 && e.CanTarget(t) && k < e.Strength)
                    retal += Mathf.Min(6, Unit.calculateDamage(e, t, null, Mathf.RoundToInt(k)));
            }
            if (enemies == 0) return worth * 6f;
            float exposure = 1f - Shield(t);
            if (lifetimeNow) {
                // What it does in a shot, times the turns it lives (6 strength over what it takes a turn, behind our screen).
                float perShot = offence / enemies;
                float takes = (worth > 0f ? defence / enemies / worth * exposure : 0f) + retal / enemies;
                float life = Mathf.Clamp(6f / Mathf.Max(0.1f, takes), 1f, lifeMaxNow);
                float reach = t.AttackRange + (t.moveAndAttack ? t.moveRange : 0);
                float engaged = reachRefNow > 0f ? Mathf.Clamp01(reach / reachRefNow) : 1f;
                float lived = perShot * engaged * life / Mathf.Max(0.5f, lifeRefNow) + presenceNow * worth * 6f;
                return Mathf.Max(floorNow * worth * 6f, lived);
            }
            defence *= exposure;
            // A unit on the field is worth something even if it would lose every exchange: the enemy has to
            // spend actions on it and come forward to do so, and it sees and holds ground (presenceNow of its
            // own worth). Never below floorNow of its worth.
            float raw = (offence - defenceWeight * defence) / enemies + presenceNow * worth * 6f;
            return Mathf.Max(floorNow * worth * 6f, raw);
        }
        [System.ThreadStatic] static float presenceNow, floorNow;
        [System.ThreadStatic] static bool overkillNow, lifetimeNow, retaliationNow;
        [System.ThreadStatic] static float lifeMaxNow, lifeRefNow, reachRefNow;
        [System.ThreadStatic] static float shieldCutNow, shieldRatioNow;
        [System.ThreadStatic] static Vector2 tierCostsNow;
        /// <summary>Our fighting units by tier (Tier), with what is planned this turn.</summary>
        [System.ThreadStatic] static int[] tierCount;

        /// <summary>
        /// What <paramref name="t"/> takes from <paramref name="e"/> in a shot, in strength: at full strength, capped at
        /// a kill; with productionOverkill, the mean over its life (its strength 6 down to 1: damage falls with strength,
        /// so firepower beyond a kill keeps killing as it is worn down).
        /// </summary>
        static float Kills(Unit t, Unit e, HexTile at) {
            if (!overkillNow) return Mathf.Min(e.Strength, Unit.calculateDamage(t, e, at, 0));
            int full = Mathf.Max(1, t.Strength);
            float sum = 0f;
            for (int s = full; s >= 1; s--) sum += Mathf.Min(e.Strength, Unit.calculateDamage(t, e, at, full - s));
            return sum / full;
        }

        /// <summary>0 cheap (infantry, recon), 1 the line (tanks, AA, gunships, artillery), 2 dear (bombers, rockets, missiles, fighters): AIProfile.productionTierCosts.</summary>
        static int Tier(Unit t) { return t.cost >= tierCostsNow.y && tierCostsNow.y > 0f ? 2 : t.cost >= tierCostsNow.x && tierCostsNow.x > 0f ? 1 : 0; }

        /// <summary>
        /// How much of what <paramref name="t"/> would take from the enemy our screen keeps off it (AIProfile.productionShieldCut):
        /// the lower-tier units we have, against productionShieldRatio of them for each of its tier (it included); all of
        /// the cut for one that fires from range or flies, half for one that must close.
        /// </summary>
        static float Shield(Unit t) {
            if (shieldCutNow <= 0f || tierCount == null) return 0f;
            int tier = Tier(t);
            if (tier == 0) return 0f;
            int lower = 0; for (int i = 0; i < tier; i++) lower += tierCount[i];
            float covered = Mathf.Clamp01(lower / (shieldRatioNow * (tierCount[tier] + 1)));
            return shieldCutNow * covered * (t.AttackRange > 1 || LocomotionRules.IsAirborne(t.locomotion) ? 1f : 0.5f);
        }
        [System.ThreadStatic] static float defenceWeightNow;
        static float defenceWeight { get { return defenceWeightNow > 0f ? defenceWeightNow : 0.5f; } }

        static float Value(Unit t, HexTile at, AIMap map) { return ValueVs(t, at, allFoes ?? map.enemyUnits); }

        /// <summary>procBudgets: what each facility has saved, and what it is saving for, by side (kept from turn to turn).</summary>
        class Budget { public readonly Dictionary<Building, float> acct = new Dictionary<Building, float>(); public readonly Dictionary<Building, Unit> target = new Dictionary<Building, Unit>(); }
        static readonly Dictionary<Player, Budget> budgets = new Dictionary<Player, Budget>();

        /// <summary>procBudgets: a unit's value, waited for <paramref name="wait"/> turns.</summary>
        static float WaitDiscount(AIProfile pr, int wait) { return wait <= 0 ? 1f : Mathf.Pow(Mathf.Max(1.0001f, pr.procWaitBase), -pr.procWaitPower * wait); }

        /// <summary>
        /// procBudgets: each facility's budget and choice. Weights: 1 each, plus up to procPriorityShare for the priority of
        /// the front it serves and for how much its best is worth against the others' best (a front that wants a gunship
        /// raises the airbase); a facility the enemy holds or can destroy a new unit on: 0, its savings given to the others.
        /// The gold not in any budget (income, savings given up) is split by weight. Then, highest weight first, each
        /// facility takes the option worth most after the wait (procWaitBase^-(procWaitPower x turns)) — the turns its
        /// savings, and those of the facilities that were saving for the same unit, take to pay for it on their shares of
        /// our income — the unit it was saving for procStick more. Paid for now (and its tile free): bought, from its own
        /// savings first, then theirs. Returns the gold left.
        /// </summary>
        static int Budgets(Player p, AIProfile pr, Proc proc, List<Slot> all, System.Func<Unit, Slot, KeyValuePair<float, string>> worth, System.Action<Unit, Slot, int> plan, int money, System.Action<string> trace) {
            Budget bg; lock (budgets) { if (!budgets.TryGetValue(p, out bg)) budgets[p] = bg = new Budget(); }
            Dictionary<Building, Slot> slotOf = new Dictionary<Building, Slot>();
            foreach (Slot s in all) if (!proc.Locked(s)) slotOf[s.b] = s;
            // Lost, or held or under the enemy's guns: its savings go back to be shared.
            foreach (Building b in new List<Building>(bg.acct.Keys)) if (!slotOf.ContainsKey(b)) { bg.acct.Remove(b); bg.target.Remove(b); }
            foreach (Building b in new List<Building>(bg.target.Keys)) if (!slotOf.ContainsKey(b)) bg.target.Remove(b);
            List<Slot> live = new List<Slot>(); foreach (Slot s in all) if (slotOf.ContainsKey(s.b) && slotOf[s.b] == s) live.Add(s);
            if (live.Count == 0) return money;
            // Weights: the front's priority, and what it is wanted for (its best, against the best anywhere).
            Dictionary<Slot, float> want = new Dictionary<Slot, float>(), wgt = new Dictionary<Slot, float>();
            float topWant = 0f;
            foreach (Slot s in live) {
                float bw = 0f;
                foreach (Unit t in s.b.Purchasable()) { if (t.cost <= 0 || !t.CanEnter(s.tile)) continue; bw = Mathf.Max(bw, worth(t, s).Key); }
                want[s] = bw; topWant = Mathf.Max(topWant, bw);
            }
            float wsum = 0f;
            foreach (Slot s in live) {
                float prio = 0.5f * proc.FrontPriority(s) + 0.5f * (topWant > 0f ? want[s] / topWant : 0f);
                wgt[s] = 1f + Mathf.Max(0f, pr.procPriorityShare) * prio; wsum += wgt[s];
            }
            // The gold no budget holds: shared by weight (spent elsewhere: every budget cut alike).
            float held = 0f; foreach (Slot s in live) { float a; bg.acct.TryGetValue(s.b, out a); held += a; }
            float pool = money - held;
            foreach (Slot s in live) {
                float a; bg.acct.TryGetValue(s.b, out a);
                bg.acct[s.b] = pool >= 0f ? a + pool * wgt[s] / wsum : (held > 0f ? a * money / held : 0f);
            }
            Dictionary<Slot, float> share = new Dictionary<Slot, float>();
            foreach (Slot s in live) share[s] = Mathf.Max(1f, proc.income * wgt[s] / wsum);
            live.Sort((x, y) => { int c = wgt[y].CompareTo(wgt[x]); return c != 0 ? c : CombatMath.CompareTiles(x.tile, y.tile); });
            Dictionary<Building, Unit> was = new Dictionary<Building, Unit>(bg.target);
            foreach (Slot s in live) {
                Unit prev; was.TryGetValue(s.b, out prev);
                Unit best = null; float bs = 0f, bv = 0f; int bwait = 0; string bwhy = null;
                foreach (Unit t in s.b.Purchasable()) {
                    if (t.cost <= 0 || !t.CanEnter(s.tile)) continue;
                    KeyValuePair<float, string> w = worth(t, s);
                    if (w.Key <= 0f) continue;
                    // Its savings, and those of the others saving for the same unit; their shares of income.
                    float have = bg.acct[s.b], inc = share[s];
                    foreach (Slot o in live) { Unit ot; if (o != s && was.TryGetValue(o.b, out ot) && ot == t) { have += bg.acct[o.b]; inc += share[o]; } }
                    int wait = have >= t.cost ? 0 : Mathf.CeilToInt((t.cost - have) / inc);
                    if (!s.free) wait = Mathf.Max(wait, 1);
                    if (wait > proc.maxWait && Kind(t) != 1) continue;
                    float sc = w.Key * WaitDiscount(pr, wait) * (prev == t ? Mathf.Max(1f, pr.procStick) : 1f);
                    if (sc > bs || (sc == bs && best != null && t.cost < best.cost)) { bs = sc; best = t; bv = w.Key; bwait = wait; bwhy = w.Value; }
                }
                string line = null;
                if (trace != null) line = "   " + p.personal_name + " budget " + s.b.name.Replace("(Clone)", "") + " (" + s.tile.x + "," + s.tile.y + ")" + (proc.FrontOf(s) != null ? " for " + proc.FrontOf(s) : "") + ": weight " + wgt[s].ToString("0.00") + ", share " + share[s].ToString("0") + ", has " + bg.acct[s.b].ToString("0");
                if (best == null) { bg.target.Remove(s.b); if (line != null) trace(line + "; nothing worth it"); continue; }
                if (bwait == 0 && s.free && best.cost <= money) {
                    // Paid for: its own savings first, then the others saving for it (the most saved first).
                    float owe = best.cost;
                    float own = Mathf.Min(owe, bg.acct[s.b]); bg.acct[s.b] -= own; owe -= own;
                    List<Slot> lenders = new List<Slot>();
                    foreach (Slot o in live) { Unit ot; if (o != s && was.TryGetValue(o.b, out ot) && ot == best) lenders.Add(o); }
                    lenders.Sort((x, y) => { int c = bg.acct[y.b].CompareTo(bg.acct[x.b]); return c != 0 ? c : CombatMath.CompareTiles(x.tile, y.tile); });
                    foreach (Slot o in lenders) { if (owe <= 0f) break; float take = Mathf.Min(owe, bg.acct[o.b]); bg.acct[o.b] -= take; owe -= take; }
                    if (owe > 0.5f) bg.acct[s.b] -= owe;   // (rounding)
                    s.pick = best; s.value = bv; s.why = bwhy; money -= best.cost; plan(best, s, 1);
                    bg.target.Remove(s.b);
                    if (line != null) trace(line + "; buys " + GameController.UnitName(best) + " (" + bv.ToString("0") + ")" + (lenders.Count > 0 ? " with " + lenders.Count + " others' savings" : ""));
                } else {
                    bg.target[s.b] = best;
                    if (line != null) trace(line + "; saves for " + GameController.UnitName(best) + " (" + bv.ToString("0") + ", " + bwait + " turns: " + bs.ToString("0") + ")" + (prev == best ? " as before" : ""));
                }
            }
            return money;
        }

        /// <summary>
        /// Procurement (AIProfile.procurement). Inputs: our gold and income, our facilities, the enemy's income, the map's, the
        /// campaign's fronts by priority and what each expects to meet (seen and unseen). Each facility serves the front it is
        /// nearest by priority; the front's pressure scales what it can build; a unit gains procFacilityBonus for each facility
        /// that can build it (pushed back by the resistance it faces), its stealth, the buildings it could take near its base,
        /// and a small fixed jiggle. Then one plan picks a unit (or none) for every facility for the most value in all within the
        /// gold. Saving: only for supply, a gap (an enemy kind nothing of ours can hurt) or transit (sea lift), within a wait the
        /// map sets (the turns to save for a supply unit at 30% of the map's income, up to 15,000 at 70%), measured on our income.
        /// </summary>
        class Proc {
            readonly Player p; readonly AIMap map; readonly AIProfile pr; readonly List<Slot> slots;
            public int income, enemyIncome, mapIncome, minWait, maxWait;
            readonly float ourPower;
            readonly Dictionary<Slot, CampaignObjective> serves = new Dictionary<Slot, CampaignObjective>();
            readonly Dictionary<Slot, bool> locked = new Dictionary<Slot, bool>();
            readonly Dictionary<Slot, float> captureNear = new Dictionary<Slot, float>();
            readonly Dictionary<CampaignObjective, float> resistance = new Dictionary<CampaignObjective, float>();
            readonly Dictionary<CampaignObjective, float[]> ground = new Dictionary<CampaignObjective, float[]>();   // water share, cover share, across water from its facility
            readonly int cheapest;
            readonly int day;

            public Proc(Player p, AIMap map, AIProfile pr, List<Slot> slots, AILogistics logistics, float ourPower) {
                this.p = p; this.map = map; this.pr = pr; this.slots = slots; this.ourPower = Mathf.Max(1f, ourPower);
                day = GameController.gc != null ? GameController.gc.days : 0;
                foreach (Building b in Building.all) {
                    if (b == null) continue;
                    mapIncome += Mathf.Max(0, b.income);
                    if (b.owner == p && b.Functioning) income += b.income;
                    else if (b.owner != null && b.owner != p) enemyIncome += b.income;
                }
                int supplyCost = int.MaxValue; cheapest = int.MaxValue;
                foreach (Slot sl in slots) foreach (Unit t in sl.b.Purchasable()) {
                    if (t.cost <= 0 || !t.CanEnter(sl.tile)) continue;
                    if (Kind(t) == 1) supplyCost = Mathf.Min(supplyCost, t.cost);
                    if (Kind(t) == 0) cheapest = Mathf.Min(cheapest, t.cost);
                }
                if (supplyCost == int.MaxValue) supplyCost = 6500;
                minWait = Mathf.CeilToInt(supplyCost / Mathf.Max(1f, 0.3f * mapIncome));
                maxWait = Mathf.Max(minWait, Mathf.CeilToInt(pr.procSaveMost / Mathf.Max(1f, 0.7f * mapIncome)));
                // Fronts: the campaign's (the logistics' tasks), by priority.
                List<CampaignObjective> fronts = new List<CampaignObjective>();
                if (logistics != null) foreach (AILogistics.Task t in logistics.tasks) if (!t.ordinary && t.front != null && t.front.target != null && !fronts.Contains(t.front)) fronts.Add(t.front);
                float prioSum = 0f; foreach (CampaignObjective o in fronts) prioSum += Mathf.Max(0.01f, o.priority);
                foreach (CampaignObjective o in fronts) {
                    // Expected resistance: what it expects there (seen and unseen), plus its share of the enemy we cannot place.
                    float r = Mathf.Max(0f, o.enemyPower) + Mathf.Max(0f, map.fogUnplacedPower) * Mathf.Max(0.01f, o.priority) / Mathf.Max(0.01f, prioSum) * (o.enemyPower > 0f ? 1f : 0f);
                    resistance[o] = r;
                    int water = 0, cover = 0, n = 0;
                    foreach (HexTile x in AIPath.TilesInRangeShared(o.target, 0, 3)) {
                        if (x == null || x.terrain == null) continue;
                        n++;
                        if (LocomotionRules.IsWater(x)) water++;
                        else if (x.terrain.cover > 0) cover++;
                    }
                    ground[o] = new float[] { n > 0 ? water / (float)n : 0f, n > 0 ? cover / (float)n : 0f };
                }
                foreach (Slot sl in slots) {
                    // Locked down: the enemy we see or remember would destroy what is built there next turn.
                    Unit cheap = null; foreach (Unit t in sl.b.Purchasable()) if (t.cost > 0 && t.CanEnter(sl.tile) && Kind(t) == 0 && (cheap == null || t.cost < cheap.cost)) cheap = t;
                    // procBudgets: the enemy on it, or able to destroy a new unit there outright (it gives its budget up).
                    bool enemyOn = sl.tile.occupier != null && sl.tile.occupier.owner != null && sl.tile.occupier.owner != p;
                    float lossLimit = pr.procBudgets && cheap != null ? Mathf.Max(1, cheap.Strength > 0 ? cheap.Strength : 6) : 3f;
                    locked[sl] = (pr.procBudgets && enemyOn) || cheap != null && map.KnownLoss(cheap, sl.tile) >= lossLimit;
                    // The front it serves: the most priority for the distance.
                    CampaignObjective best = null; float bs = 0f;
                    foreach (CampaignObjective o in fronts) {
                        float sc = Mathf.Max(0.01f, o.priority) / (1f + sl.tile.Distance(o.target));
                        if (sc > bs) { bs = sc; best = o; }
                    }
                    if (best != null) serves[sl] = best;
                    // Buildings we could take within two turns of it (for those that capture).
                    float cv = 0f;
                    foreach (Building b in Building.all) {
                        if (b == null || b.tile == null || !b.Capturable || b.owner == p) continue;
                        if (cheap == null || map.Travel(cheap, sl.tile, b.tile) > 2 * Mathf.Max(1, cheap.moveRange)) continue;
                        cv += b.income * pr.objectiveHorizonTurns + (b.CanProduce ? pr.procCaptureProducerValue : 0f);
                    }
                    captureNear[sl] = cv;
                }
            }

            public bool Locked(Slot sl) { bool b; return locked.TryGetValue(sl, out b) && b; }
            /// <summary>The priority of the front a facility serves, over the highest (0: it serves none).</summary>
            public float FrontPriority(Slot sl) {
                CampaignObjective o; if (!serves.TryGetValue(sl, out o) || o == null) return 0f;
                float top = 0f; foreach (CampaignObjective f in serves.Values) if (f != null) top = Mathf.Max(top, f.priority);
                return top > 0f ? Mathf.Clamp01(o.priority / top) : 0f;
            }
            public string FrontOf(Slot sl) { CampaignObjective o; return serves.TryGetValue(sl, out o) && o != null ? o.Id : null; }

            /// <summary>Facilities (not locked down) that can build a kind like <paramref name="t"/>.</summary>
            int Builders(Unit t) {
                int n = 0;
                foreach (Slot sl in slots) { if (locked[sl]) continue; foreach (Unit u in sl.b.Purchasable()) if (u == t) { n++; break; } }
                return n;
            }

            public KeyValuePair<float, string> Worth(Unit t, Slot sl, KeyValuePair<float, string> w) {
                if (locked[sl]) return new KeyValuePair<float, string>(0f, "facility under fire");
                float v = w.Key; string why = w.Value;
                if (v <= 0f && Kind(t) != 0) return w;
                if (Kind(t) == 0 && !HurtsPossible(t, sl.tile)) return new KeyValuePair<float, string>(0f, "hurts nothing the enemy has or can build");
                CampaignObjective o; serves.TryGetValue(sl, out o);
                float r = o != null ? resistance[o] : 0f;
                bool fighter = Kind(t) == 0 && t.Firepower > 0 && !(Transporting.IsTransport(t) && t.Firepower <= 1);
                bool groundTransport = Transporting.IsTransport(t) && t.Firepower <= 1 && !LocomotionRules.IsAirborne(t.locomotion) && !LocomotionRules.IsNaval(t.locomotion);
                // (procCargo: any transport that does not fight; without it, ground ones only.)
                bool troopTransport = pr.procCargo ? IsCarrier(t) : groundTransport;
                bool air = LocomotionRules.IsAirborne(t.locomotion), sea = LocomotionRules.IsNaval(t.locomotion);
                if (o != null) {
                    float[] g = ground[o];
                    if (r <= 0f) {
                        // No resistance: infantry and transports; a front short of its units: the cheapest.
                        if (t.locomotion == Locomotion.Boots || troopTransport) { v *= pr.procNoResistance; why = (why != null ? why + ", " : "") + o.Id + " unopposed"; }
                        if (o.assignedPower < o.needPower && fighter) v *= 1f + pr.procCheapBonus * cheapest / Mathf.Max(1f, t.cost);
                    } else if (fighter) {
                        // Resistance: the most powerful, outright (its own worth, by how much resistance there is against us).
                        v += AIScore.Worth(t) * 6f * r / (r + ourPower);
                    }
                    if (map.AcrossWater(sl.tile, o.target) && (air || sea || t.AttackRange > 1)) v *= pr.procAcrossWater;
                    if (g[0] > 0.2f && (air || t.amphibious)) v *= pr.procMixedWater;
                    if (t.locomotion == Locomotion.Boots && fighter) v *= 1f + g[1];
                }
                if (fighter) {
                    // Screening tiers: infantry to mobile, mobile to expensive, past procTierRatio to one.
                    int tr = Tier(t);
                    if (tr == 1 && tierCount[0] > pr.procTierRatio * Mathf.Max(1, tierCount[1])) v *= 1.5f;
                    if (tr == 2 && tierCount[1] > pr.procTierRatio * Mathf.Max(1, tierCount[2])) v *= 1.5f;
                    if (t.stealthRange > 0 && v > 0f) v *= pr.procStealth;
                    // Out of supply where it is built, and its own supplies do not carry it to ours: less.
                    if (map.SupplyDepth(sl.tile) <= 0 && t.suppliesMax > 0) {
                        int d = int.MaxValue;
                        foreach (HexTile x in AIPath.TilesInRangeShared(sl.tile, 0, Mathf.Max(1, t.moveRange) * Mathf.Max(1, t.suppliesMax))) if (x != null && map.SupplyDepth(x) > 0) { d = Mathf.Min(d, sl.tile.Distance(x)); }
                        if (d == int.MaxValue) v *= 0.5f;
                    }
                }
                // The empty reserve, short of gold: a troop transport it fills with infantry.
                if (groundTransport && ReserveShort > 0f && p.money < pr.procReserveRich) { v = Mathf.Max(v, AIScore.Worth(t) * 6f * (1f + ReserveShort)); why = "the reserve: a transport for infantry"; }
                // Every facility that can build it: +procFacilityBonus, pushed back by the resistance its front expects.
                if (v > 0f) {
                    float push = r > 0f ? ourPower / (ourPower + r) : 1f;
                    v += pr.procFacilityBonus * Builders(t) * push;
                    if (fighter && Building.CanCapture(t)) v += captureNear[sl];
                }
                if (v > 0f && pr.procJiggle > 0) v += ((t.cost / 100) * 7 + t.moveRange * 13 + t.AttackRange * 29 + (sl.tile.x + 64) * 31 + (sl.tile.y + 64) * 17 + day * 11) % (pr.procJiggle + 1);
                return new KeyValuePair<float, string>(v, why);
            }

            /// <summary>What to save for, if anything: supply, a gap or transit, reachable on our income within the wait the map allows.</summary>
            public void SaveTarget(List<Slot> sls, System.Func<Unit, Slot, KeyValuePair<float, string>> worth, int money, ref Unit pick, ref Building at, ref float bestSave, ref float bestAfford) {
                bestSave = 0f; bestAfford = 0f; pick = null; at = null;
                foreach (Slot sl in sls) foreach (Unit t in sl.b.Purchasable()) {
                    if (t.cost <= 0 || !t.CanEnter(sl.tile)) continue;
                    KeyValuePair<float, string> w = worth(t, sl);
                    if (w.Key <= 0f) continue;
                    float r = w.Key / t.cost;
                    if (t.cost <= money) { bestAfford = Mathf.Max(bestAfford, r); continue; }
                    bool supply = Kind(t) != 0, transit = w.Value != null && w.Value.StartsWith("sea lift"), gap = Kind(t) == 0 && FillsGap(t, sl.tile);
                    if (!supply && !transit && !gap) continue;
                    int turns = Mathf.CeilToInt((t.cost - money) / (float)Mathf.Max(1, income));
                    // (A supply unit may always be saved for: on a poor map a supply unit is the one thing worth waiting for.)
                    if (turns > maxWait && Kind(t) != 1) continue;
                    if (w.Key > bestSave) { bestSave = w.Key; pick = t; at = sl.b; }
                }
                // Saved for only if it is worth more than everything the gold buys now (the plan over every facility):
                // waiting gives up this turn's units. (Compared by value per gold, the per-facility bonus made the cheapest
                // unit always win.)
                if (pick != null) {
                    List<Unit> ps; List<float> vs; List<string> ws;
                    Choose(sls, worth, money, out ps, out vs, out ws);
                    float now = 0f; foreach (float v in vs) now += v;
                    if (bestSave > now) { bestAfford = 0f; bestSave = Mathf.Max(bestSave, 1f); }
                    else { pick = null; at = null; }
                }
            }

            /// <summary>An enemy kind seen that nothing of ours can hurt, and this one can.</summary>
            bool FillsGap(Unit t, HexTile at) {
                foreach (Unit e in allFoes) {
                    if (e == null || Unit.calculateDamage(t, e, at, 0) <= 0) continue;
                    bool covered = false;
                    foreach (Unit u in map.ownUnits) if (u != null && u.tile != null && u.Firepower > 0 && Unit.calculateDamage(u, e, at, 0) > 0) { covered = true; break; }
                    if (!covered) return true;
                }
                return false;
            }

            /// <summary>One unit (or none) per facility, for the most value in all within the gold.</summary>
            public void Choose(List<Slot> sls, System.Func<Unit, Slot, KeyValuePair<float, string>> worth, int money, out List<Unit> picks, out List<float> vals, out List<string> whys) {
                const int step = 100;
                int cap = Mathf.Max(0, money / step), n = sls.Count;
                picks = new List<Unit>(); vals = new List<float>(); whys = new List<string>();
                List<List<KeyValuePair<Unit, KeyValuePair<float, string>>>> opts = new List<List<KeyValuePair<Unit, KeyValuePair<float, string>>>>();
                foreach (Slot sl in sls) {
                    List<KeyValuePair<Unit, KeyValuePair<float, string>>> o = new List<KeyValuePair<Unit, KeyValuePair<float, string>>>();
                    foreach (Unit t in sl.b.Purchasable()) {
                        if (t.cost <= 0 || t.cost > money || !t.CanEnter(sl.tile)) continue;
                        KeyValuePair<float, string> w = worth(t, sl);
                        if (w.Key > 0f) o.Add(new KeyValuePair<Unit, KeyValuePair<float, string>>(t, w));
                    }
                    opts.Add(o);
                }
                System.Text.StringBuilder note = new System.Text.StringBuilder();
                for (int i = 0; i < n; i++) {
                    note.Append(i > 0 ? "; " : "").Append(sls[i].b.name.Replace("(Clone)", "")).Append(" (").Append(sls[i].tile.x).Append(',').Append(sls[i].tile.y).Append("): ");
                    if (opts[i].Count == 0) { int all = 0, zero = 0; foreach (Unit t in sls[i].b.Purchasable()) { all++; if (t.cost <= money && t.CanEnter(sls[i].tile)) zero++; } note.Append("nothing worth it (" + zero + " of " + all + " affordable:"); foreach (Unit t in sls[i].b.Purchasable()) if (t.cost <= money && t.CanEnter(sls[i].tile)) { KeyValuePair<float, string> w = worth(t, sls[i]); note.Append(' ').Append(GameController.UnitName(t)).Append(' ').Append(w.Key.ToString("0")).Append(" ").Append(w.Value ?? "-"); } note.Append(")"); }
                    foreach (KeyValuePair<Unit, KeyValuePair<float, string>> kv in opts[i]) note.Append(GameController.UnitName(kv.Key)).Append(' ').Append(kv.Value.Key.ToString("0")).Append(' ');
                }
                ChooseNote = note.ToString();
                float[,] best = new float[n + 1, cap + 1];
                int[,] choice = new int[n + 1, cap + 1];
                for (int i = n - 1; i >= 0; i--)
                    for (int m = 0; m <= cap; m++) {
                        best[i, m] = best[i + 1, m]; choice[i, m] = -1;
                        for (int k = 0; k < opts[i].Count; k++) {
                            int c = Mathf.CeilToInt(opts[i][k].Key.cost / (float)step);
                            if (c > m) continue;
                            float v = opts[i][k].Value.Key + best[i + 1, m - c];
                            if (v > best[i, m] + 0.001f) { best[i, m] = v; choice[i, m] = k; }
                        }
                    }
                int left = cap;
                for (int i = 0; i < n; i++) {
                    int k = choice[i, left];
                    if (k < 0) { picks.Add(null); vals.Add(0f); whys.Add(null); continue; }
                    picks.Add(opts[i][k].Key); vals.Add(opts[i][k].Value.Key); whys.Add(opts[i][k].Value.Value);
                    left -= Mathf.CeilToInt(opts[i][k].Key.cost / (float)step);
                }
            }

            public string ChooseNote;
            public string Describe() {
                int lockedN = 0; foreach (bool b in locked.Values) if (b) lockedN++;
                return "income " + income + " (enemy " + enemyIncome + ", map " + mapIncome + "), " + slots.Count + " facilities (" + lockedN + " under fire), "
                       + serves.Count + " serving fronts, wait for savings " + minWait + "-" + maxWait + " turns";
            }
        }
    }
}
