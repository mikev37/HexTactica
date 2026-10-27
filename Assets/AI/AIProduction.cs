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
        public static List<Unit> Buy(Player p, AIMap map, AIProfile profile, System.Action<string> trace, AILogistics logistics) {
            List<Unit> bought = new List<Unit>();
            if (p == null || map == null || BuildingController.bc == null) return bought;
            List<Building> sites = new List<Building>();
            foreach (Building b in Building.all) if (b != null && b.owner == p && b.tile != null && b.Purchasable().Count > 0) sites.Add(b);
            sites.Sort((a, b) => CombatMath.CompareTiles(a.tile, b.tile));

            Dictionary<string, int> have = new Dictionary<string, int>();
            foreach (Unit u in map.ownUnits) if (u != null) { string n = GameController.UnitName(u); int c; have.TryGetValue(n, out c); have[n] = c + 1; }

            // Each front's nearest producer of each kind of want: fighting units, supply units, trucks.
            // (The nearest of all was a captured airport that builds no supply: the front's supply
            // pressure went nowhere.)
            Dictionary<Building, List<AILogistics.Task>> wants = new Dictionary<Building, List<AILogistics.Task>>();
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
                        if (near == null) continue;
                        nearest[kind][task] = near;
                        List<AILogistics.Task> list;
                        if (!wants.TryGetValue(near, out list)) wants[near] = list = new List<AILogistics.Task>();
                        if (!list.Contains(task)) list.Add(task);
                    }
            int freeTrucks = logistics != null ? logistics.freeTrucks : 0;
            // The fronts' producers first, so that saving for a front's supply is decided before anyone spends.
            HashSet<Building> supplyFirst = new HashSet<Building>();
            foreach (Building b in nearest[1].Values) supplyFirst.Add(b);
            foreach (Building b in nearest[2].Values) supplyFirst.Add(b);
            sites.Sort((a, b) => {
                bool sa = supplyFirst.Contains(a), sb = supplyFirst.Contains(b); if (sa != sb) return sa ? -1 : 1;
                bool wa = wants.ContainsKey(a), wb = wants.ContainsKey(b); if (wa != wb) return wa ? -1 : 1;
                return CombatMath.CompareTiles(a.tile, b.tile);
            });

            HashSet<Building> saving = new HashSet<Building>();
            bool any = true;
            int guard = 0;
            while (any && guard++ < 32) {
                any = false;
                foreach (Building b in sites) {
                    Unit pick = null; float bestScore = 0f; HexTile where = null; string why = null;
                    float bestSupply = 0f; string bestSupplyName = null; int bestSupplyCost = 0;
                    List<AILogistics.Task> fronts;
                    wants.TryGetValue(b, out fronts);
                    foreach (Unit t in b.Purchasable()) {
                        if (t.cost <= 0) continue;
                        int kindOf = Kind(t);
                        bool fighter = kindOf == 0, supply = kindOf == 1, truck = kindOf == 2;
                        if (kindOf < 0) continue;
                        if (!fighter && !((supply || truck) && fronts != null)) continue;
                        HexTile free = FreeTile(b, t, map);
                        if (free == null) continue;
                        int n; have.TryGetValue(GameController.UnitName(t), out n);
                        float mix = Mathf.Pow(0.85f, n);
                        float score = fighter ? Value(t, free, map) / t.cost * mix : 0f;
                        string tag = null;
                        if (fronts != null)
                            foreach (AILogistics.Task f in fronts) {
                                Building mine;
                                if (!nearest[kindOf].TryGetValue(f, out mine) || mine != b) continue;   // not this front's producer of that kind
                                float v = 0f;
                                if (fighter) {
                                    List<Unit> foes = new List<Unit>(f.front.enemies);
                                    foreach (Unit e in f.front.arriving.Keys) if (!foes.Contains(e)) foes.Add(e);
                                    float shortBy = Mathf.Max(0f, f.front.needPower - f.front.assignedPower) / Mathf.Max(1f, f.front.needPower);
                                    v = (foes.Count > 0 ? ValueVs(t, free, foes) : Value(t, free, map)) / t.cost * mix * (1f + shortBy);
                                } else if (f.short_ && (supply || freeTrucks == 0))
                                    v = f.unsupplied * (supply ? 1f : 0.5f) / t.cost * mix;
                                if (v > score) { score = v; tag = f.front.Id + (fighter ? "" : supply ? ": its supply is short" : ": no truck for its supply"); }
                            }
                        if (!fighter && score > bestSupply) { bestSupply = score; bestSupplyName = GameController.UnitName(t); bestSupplyCost = t.cost; }
                        if (t.cost > p.money) continue;
                        if (score > bestScore || (score == bestScore && pick != null && string.CompareOrdinal(t.name, pick.name) < 0)) { pick = t; bestScore = score; where = free; why = tag; }
                    }
                    // Saving up: a front's supply (or a truck for it) is worth more than anything we can buy
                    // now, but costs more than we have: nothing here this turn, and the gold kept for it.
                    if (bestSupplyName != null && bestSupply > bestScore && bestSupplyCost > p.money) {
                        if (trace != null && !saving.Contains(b)) trace("   " + p.personal_name + " saves for " + bestSupplyName + " at " + b.name.Replace("(Clone)", "") + " (" + (bestSupply * 1000f).ToString("0.0") + " a 1000 gold, against " + (bestScore * 1000f).ToString("0.0") + (pick != null ? " for " + GameController.UnitName(pick) : "") + "): " + p.money + " of " + bestSupplyCost);
                        saving.Add(b);
                        // Saved for the whole turn: no producer spends it.
                        any = false; break;
                    }
                    if (pick == null) continue;
                    // Bought for real (outside the board the AI thinks on), and put on that board too.
                    Unit made;
                    using (BoardView.Outside()) made = BuildingController.bc.Produce(b, pick, x => AIAir.MayBuildOn(x, map));
                    if (made != null && made.tile != null) made.tile.occupier = made;
                    if (made == null) continue;
                    bought.Add(made);
                    string name = GameController.UnitName(pick);
                    int c; have.TryGetValue(name, out c); have[name] = c + 1;
                    any = true;
                    if (made.Hub == null && Transporting.IsTransport(made) && made.Firepower <= 1) freeTrucks++;
                    if (trace != null) trace("   " + p.personal_name + " buys " + name + " at " + b.name.Replace("(Clone)", "") + " (" + made.tile.x + "," + made.tile.y + ")" + (why != null ? " for " + why : "") + " (" + (bestScore * 1000f).ToString("0.0") + " a 1000 gold" + (bestSupplyName != null ? "; best supply or transport: " + bestSupplyName + " " + (bestSupply * 1000f).ToString("0.0") : "") + "), money left " + p.money);
                }
            }
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
                if (e == null || e.tile == null || e.Strength <= 0 || !GameController.Targetable(e)) continue;
                enemies++;
                offence += Mathf.Min(e.Strength, Unit.calculateDamage(t, e, e.tile, 0)) * AIScore.Worth(e);
                if (e.Firepower > 0) defence += Mathf.Min(6, Unit.calculateDamage(e, t, at, 0)) * worth;
            }
            if (enemies == 0) return worth * 6f;
            return Mathf.Max(0f, (offence - 0.5f * defence) / enemies);
        }

        static float Value(Unit t, HexTile at, AIMap map) {
            float worth = AIScore.Worth(t);
            float offence = 0f, defence = 0f; int enemies = 0;
            foreach (Unit e in map.enemyUnits) {
                if (e == null || e.tile == null || e.Strength <= 0 || !GameController.Targetable(e)) continue;
                enemies++;
                int dealt = Mathf.Min(e.Strength, Unit.calculateDamage(t, e, e.tile, 0));
                offence += dealt * AIScore.Worth(e);
                if (e.Firepower > 0) defence += Mathf.Min(6, Unit.calculateDamage(e, t, at, 0)) * worth;
            }
            if (enemies == 0) return worth * 6f;
            return Mathf.Max(0f, (offence - 0.5f * defence) / enemies);
        }
    }
}
