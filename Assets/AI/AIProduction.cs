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
        public static List<Unit> Buy(Player p, AIMap map, AIProfile profile, System.Action<string> trace, AILogistics logistics) {
            List<Unit> bought = new List<Unit>();
            if (p == null || map == null || BuildingController.bc == null) return bought;
            allFoes = Foes(p, map, profile);
            defenceWeightNow = profile.productionDefenceWeight;
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

            // The slots: every free tile of every building that can build.
            List<Slot> slots = new List<Slot>();
            foreach (Building b in sites)
                foreach (HexTile x in b.Footprint)
                    if (x.occupier == null && AIAir.MayBuildOn(x, map)) { bool any = false; foreach (Unit t in b.Purchasable()) if (t.cost > 0 && t.CanEnter(x)) { any = true; break; } if (any) slots.Add(new Slot { b = b, tile = x }); }

            int money = p.money;
            int supplyPlanned = 0, trucksPlanned = 0;
            Dictionary<string, int> picked = new Dictionary<string, int>();
            // What a unit would add in this slot, given what is planned so far.
            System.Func<Unit, Slot, KeyValuePair<float, string>> worth = (t, sl) => {
                int kindOf = Kind(t);
                if (kindOf < 0 || t.cost <= 0 || !t.CanEnter(sl.tile)) return new KeyValuePair<float, string>(-1f, null);
                string name = GameController.UnitName(t);
                int n, m; have.TryGetValue(name, out n); picked.TryGetValue(name, out m);
                float mix = Mathf.Pow(Mathf.Clamp01(profile.productionVariety), n + m);
                float v = 0f; string tag = null;
                if (kindOf == 0) {
                    v = Value(t, sl.tile, map) * mix;
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
            };
            System.Action<Unit, Slot, int> plan = (t, sl, d) => {
                count(t, d);
                if (Kind(t) == 0) { if (d > 0) planned.Add(new KeyValuePair<Unit, HexTile>(t, sl.tile)); else planned.RemoveAll(x => x.Key == t && x.Value == sl.tile); scarce = Scarcity(allFoes, map, profile); }
            };

            // Saving up: a supply unit (or truck) worth more for its cost than anything we can pay for now,
            // but dearer than the gold we have: nothing bought this turn, the gold kept for it.
            {
                float bestAfford = 0f, bestSupply = 0f; Unit supplyPick = null; Building supplyAt = null;
                foreach (Slot sl in slots)
                    foreach (Unit t in sl.b.Purchasable()) {
                        KeyValuePair<float, string> w = worth(t, sl);
                        if (w.Key <= 0f) continue;
                        float r = w.Key / t.cost;
                        if (t.cost <= money) bestAfford = Mathf.Max(bestAfford, r);
                        else if (Kind(t) != 0 && r > bestSupply) { bestSupply = r; supplyPick = t; supplyAt = sl.b; }
                    }
                if (supplyPick != null && bestSupply > bestAfford) {
                    if (trace != null) trace("   " + p.personal_name + " saves for " + GameController.UnitName(supplyPick) + " at " + supplyAt.name.Replace("(Clone)", "") + " (" + (bestSupply * 1000f).ToString("0.0") + " a 1000 gold, against " + (bestAfford * 1000f).ToString("0.0") + "): " + money + " of " + supplyPick.cost);
                    planned = null;
                    return bought;
                }
            }
            // First: each slot, in turn, the best value for its cost that can be paid for now.
            for (int pass = 0; pass < slots.Count; pass++) {
                Slot best = null; Unit bu = null; float br = 0f, bv = 0f; string bw = null;
                foreach (Slot sl in slots) {
                    if (sl.pick != null) continue;
                    foreach (Unit t in sl.b.Purchasable()) {
                        if (t.cost > money) continue;
                        KeyValuePair<float, string> w = worth(t, sl);
                        if (w.Key <= 0f) continue;
                        float r = w.Key / t.cost;
                        if (r > br || (r == br && bu != null && string.CompareOrdinal(t.name, bu.name) < 0)) { br = r; best = sl; bu = t; bv = w.Key; bw = w.Value; }
                    }
                }
                if (best == null) break;
                best.pick = bu; best.value = bv; best.why = bw; money -= bu.cost; plan(bu, best, 1);
            }
            // Then: gold left over goes to upgrades, the most value per extra gold first (AIProfile.productionSpendAll).
            for (int guard = 0; profile.productionSpendAll && money > 0 && guard < 64; guard++) {
                Slot best = null; Unit bu = null; float br = 0f, bv = 0f; string bw = null;
                foreach (Slot sl in slots) {
                    if (sl.pick == null) continue;
                    Unit was = sl.pick; plan(was, sl, -1);
                    foreach (Unit t in sl.b.Purchasable()) {
                        int extra = t.cost - was.cost;
                        if (extra <= 0 || extra > money) continue;
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

        /// <summary>A footprint tile of <paramref name="b"/> where <paramref name="t"/> could be built now.</summary>
        static HexTile FreeTile(Building b, Unit t, AIMap map) {
            foreach (HexTile x in b.Footprint) if (x.occupier == null && t.CanEnter(x) && AIAir.MayBuildOn(x, map)) return x;
            return null;
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
                offence += Mathf.Min(e.Strength, Unit.calculateDamage(t, e, at, 0)) * AIScore.Worth(e) * need;
                if (e.Firepower > 0) defence += Mathf.Min(6, Unit.calculateDamage(e, t, at, 0)) * worth;
            }
            if (enemies == 0) return worth * 6f;
            // A unit on the field is worth something even if it would lose every exchange: the enemy has to
            // spend actions on it and come forward to do so, and it sees and holds ground (presenceNow of its
            // own worth). Never below floorNow of its worth.
            float raw = (offence - defenceWeight * defence) / enemies + presenceNow * worth * 6f;
            return Mathf.Max(floorNow * worth * 6f, raw);
        }
        [System.ThreadStatic] static float presenceNow, floorNow;
        [System.ThreadStatic] static float defenceWeightNow;
        static float defenceWeight { get { return defenceWeightNow > 0f ? defenceWeightNow : 0.5f; } }

        static float Value(Unit t, HexTile at, AIMap map) { return ValueVs(t, at, allFoes ?? map.enemyUnits); }
    }
}
