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

        public static List<Unit> Buy(Player p, AIMap map, AIProfile profile, System.Action<string> trace) {
            List<Unit> bought = new List<Unit>();
            if (p == null || map == null || BuildingController.bc == null) return bought;
            List<Building> sites = new List<Building>();
            foreach (Building b in Building.all) if (b != null && b.owner == p && b.tile != null && b.Purchasable().Count > 0) sites.Add(b);
            sites.Sort((a, b) => CombatMath.CompareTiles(a.tile, b.tile));

            Dictionary<string, int> have = new Dictionary<string, int>();
            foreach (Unit u in map.ownUnits) if (u != null) { string n = GameController.UnitName(u); int c; have.TryGetValue(n, out c); have[n] = c + 1; }

            bool any = true;
            int guard = 0;
            while (any && guard++ < 32) {
                any = false;
                foreach (Building b in sites) {
                    Unit pick = null; float bestScore = 0f; HexTile where = null;
                    foreach (Unit t in b.Purchasable()) {
                        if (t.cost <= 0 || t.cost > p.money) continue;
                        if (t.Hub != null || t.Firepower <= 0) continue;
                        HexTile free = FreeTile(b, t, map);
                        if (free == null) continue;
                        int n; have.TryGetValue(GameController.UnitName(t), out n);
                        float score = Value(t, free, map) / t.cost * Mathf.Pow(0.85f, n);
                        if (score > bestScore || (score == bestScore && pick != null && string.CompareOrdinal(t.name, pick.name) < 0)) { pick = t; bestScore = score; where = free; }
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
                    if (trace != null) trace("   " + p.personal_name + " buys " + name + " at " + b.name.Replace("(Clone)", "") + " (" + made.tile.x + "," + made.tile.y + "), money left " + p.money);
                }
            }
            return bought;
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
