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
        class Slot { public Building b; public HexTile tile; public Unit pick; public float value; public string why; }

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
            defenceWeightNow = profile.productionDefenceWeight;
            overkillNow = profile.productionOverkill;
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
            HashSet<Unit> forSupply = profile.assaultRides ? AIAssault.SupplyTransports(map.player, map, null) : new HashSet<Unit>();
            if (profile.assaultRides && profile.assaultTransportWeight > 0f) {
                foreach (Unit u in map.ownUnits) {
                    if (u == null || u.tile == null) continue;
                    if (Transporting.IsTransport(u) && u.Firepower <= 1) {
                        if (!forSupply.Contains(u)) troopRoom += Transporting.FreeWeight(u);
                        continue;
                    }
                    if (u.trasnporter == null && u.Hub == null && u.Firepower > 0 && u.locomotion == Locomotion.Boots && map.TurnsToContact(u) > profile.assaultBoardTurns) waitingRiders.Add(u);
                }
            }
            if (trace != null && profile.assaultRides) trace("   riders waiting for a transport: " + waitingRiders.Count + ", room in empty troop transports " + troopRoom);
            int troopPlanned = 0;
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
            List<Slot> slots = new List<Slot>();
            foreach (Building b in sites)
                for (HexTile x = b.tile; x != null; x = null)
                    if (x.occupier == null && AIAir.MayBuildOn(x, map)) { bool any = false; foreach (Unit t in b.Purchasable()) if (t.cost > 0 && t.CanEnter(x)) { any = true; break; } if (any) slots.Add(new Slot { b = b, tile = x }); }

            // Where the fight is (productionTimeToFront): the enemy units we know of, else the enemy's HQ.
            List<HexTile> fight = new List<HexTile>();
            if (profile.productionTimeToFront) {
                foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && e.Firepower > 0) fight.Add(e.tile);
                FogOfWar.Knowledge fk = FogOfWar.current;
                if (fk != null) foreach (FogOfWar.Phantom ph in fk.phantoms) if (ph.last != null && ph.kind != null && ph.kind.firepower > 0) fight.Add(ph.last);
                if (fight.Count == 0) foreach (Building b in Building.all) if (b != null && b.IsHQ && b.tile != null && (FogOfWar.OwnerAsSeen(p, b) ?? b.owner) != p && b.owner != null) fight.Add(b.tile);
            }
            Dictionary<string, float> timeFactor = new Dictionary<string, float>();
            // Its worth there: divided by (1 + turns to the fight / horizon), by its own movement.
            System.Func<Unit, HexTile, float> toFight = (t, at) => {
                if (fight.Count == 0 || t.moveRange <= 0) return 1f;
                string key = GameController.UnitName(t) + "@" + at.x + "," + at.y;
                float f;
                if (timeFactor.TryGetValue(key, out f)) return f;
                int d = int.MaxValue;
                foreach (HexTile x in fight) d = Mathf.Min(d, map.Travel(t, at, x));
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
            Dictionary<string, int> picked = new Dictionary<string, int>();
            // What a unit would add in this slot, given what is planned so far.
            System.Func<Unit, Slot, KeyValuePair<float, string>> worth = (t, sl) => {
                int kindOf = Kind(t);
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
                if (Transporting.IsTransport(t) && t.Firepower <= 1) troopPlanned += d * Mathf.Max(1, t.transportSlots);
            };
            System.Action<Unit, Slot, int> plan = (t, sl, d) => {
                count(t, d);
                if (Transporting.IsTransport(t) && t.Firepower <= 1) { int r0; roomAt.TryGetValue(sl.b, out r0); roomAt[sl.b] = r0 + d * Mathf.Max(1, t.transportSlots); }
                if (t.locomotion == Locomotion.Boots && Kind(t) == 0) { int f0; fillPlanned.TryGetValue(sl.b, out f0); fillPlanned[sl.b] = f0 + d; }
                if (Kind(t) == 0) { if (d > 0) planned.Add(new KeyValuePair<Unit, HexTile>(t, sl.tile)); else planned.RemoveAll(x => x.Key == t && x.Value == sl.tile); scarce = Scarcity(allFoes, map, profile); if (tierCount != null) tierCount[Tier(t)] += d; }
            };

            // Saving for the reserve (reserveDemand): what was saved for, bought as soon as it can be paid for.
            string savingFor; lock (reserveSaving) reserveSaving.TryGetValue(p, out savingFor);
            bool boughtSaved = false;
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
                bool forReserve = supplyPick != null && Kind(supplyPick) == 0;
                if (forReserve) { string was; lock (reserveSaving) reserveSaving.TryGetValue(p, out was); if (was != null && was != GameController.UnitName(supplyPick)) forReserve = false; }
                if (supplyPick != null && bestSupply > bestAfford && (Kind(supplyPick) != 0 || forReserve || !reserveSaving.ContainsKey(p))) {
                    if (Kind(supplyPick) == 0) lock (reserveSaving) reserveSaving[p] = GameController.UnitName(supplyPick);
                    if (trace != null) trace("   " + p.personal_name + " saves for " + GameController.UnitName(supplyPick) + (Kind(supplyPick) == 0 ? " (the reserve)" : "") + " at " + supplyAt.name.Replace("(Clone)", "") + " (" + (bestSupply * 1000f).ToString("0.0") + " a 1000 gold, against " + (bestAfford * 1000f).ToString("0.0") + "): " + money + " of " + supplyPick.cost);
                    planned = null;
                    return bought;
                }
            }
            // First: each slot, in turn, the best value for its cost that can be paid for now.
            for (int pass = 0; pass < slots.Count; pass++) {
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
            for (int guard = 0; profile.productionSpendAll && money > 0 && guard < 64; guard++) {
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
                if (made != null && sl.why != null && sl.why.StartsWith("the reserve")) { if (ReserveBought == null) ReserveBought = new List<Unit>(); ReserveBought.Add(made); }
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
            float offence = 0f, defence = 0f; int enemies = 0;
            foreach (Unit e in foes) {
                if (e == null || e.Strength <= 0) continue;
                enemies++;
                // Each enemy unit counted as much as our army lacks the means to fight it (Scarcity); on our
                // own ground (at), not wherever it stands now (one remembered under the fog is not seen there).
                float need = scarce != null && scarce.ContainsKey(e) ? scarce[e] : 1f;
                offence += Kills(t, e, at) * AIScore.Worth(e) * need;
                if (e.Firepower > 0) defence += Mathf.Min(6, Unit.calculateDamage(e, t, at, 0)) * worth;
            }
            if (enemies == 0) return worth * 6f;
            float exposure = 1f - Shield(t);
            if (lifetimeNow) {
                // What it does in a shot, times the turns it lives (6 strength over what it takes a turn, behind our screen).
                float perShot = offence / enemies;
                float takes = worth > 0f ? defence / enemies / worth * exposure : 0f;
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
        [System.ThreadStatic] static bool overkillNow, lifetimeNow;
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
    }
}
