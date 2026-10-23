using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// How much a unit standing on a tile takes away from the enemy: ground they can move over
    /// and ground they can supply. The third tier of a tile choice (see AITactical.Tiered) — after
    /// damage dealt and damage received — so it decides between moves that fight equally well,
    /// and when nothing is in reach it is what sends units to chokepoints.
    ///
    /// Both are measured, not guessed: units block movement (AIPath.CanPassThrough) and block
    /// enemy supply (SupplyNetwork floods around enemy-occupied tiles), so the unit is moved
    /// there on the board, the enemy's real movement and supply are recomputed, terrain and all,
    /// and the board is put back. Vacating the current tile counts too: leaving a choke opens it.
    /// </summary>
    public static class AIDenial {

        public struct Result {
            /// <summary>Enemy-reachable tiles (within two turns, summed over enemy units) removed.</summary>
            public int tiles;
            /// <summary>Tiles removed from enemy supply.</summary>
            public int supplyTiles;
            /// <summary>Enemy units cut off from supply.</summary>
            public int unitsCut;
            /// <summary>Gold value under the profile's weights.</summary>
            public float gold;
        }

        /// <summary>Per-board cache of the "before" side, so each candidate only pays for its "after".</summary>
        public class Baseline {
            public readonly Dictionary<Unit, int> reach = new Dictionary<Unit, int>();
            /// <summary>The tiles behind each count (to tell whether a move can change it at all).</summary>
            public readonly Dictionary<Unit, HashSet<HexTile>> reachTiles = new Dictionary<Unit, HashSet<HexTile>>();
            public readonly Dictionary<Player, SupplyNetwork.Result> supply = new Dictionary<Player, SupplyNetwork.Result>();
        }

        public static Result Measure(Unit u, HexTile at, AIMap map, Baseline baseline) {
            Result r = new Result();
            if (u == null || at == null || u.tile == null || at == u.tile) return r;
            if (at.occupier != null && at.occupier != u) return r;
            // Aboard a transport (or otherwise not the tile's occupant): nothing to move.
            if (u.tile.occupier != u) return r;
            AIProfile p = map.profile;

            List<Unit> affected = new List<Unit>();
            for (int i = 0; i < map.enemyUnits.Count; i++) {
                Unit e = map.enemyUnits[i];
                if (e == null || e.tile == null || e.moveRange <= 0 || e.trasnporter != null) continue;
                int window = 2 * e.moveRange + 1;
                if (e.tile.Distance(at) <= window || e.tile.Distance(u.tile) <= window) affected.Add(e);
            }
            List<Player> enemies = new List<Player>();
            for (int i = 0; i < map.enemyUnits.Count; i++) {
                Player o = map.enemyUnits[i] != null ? map.enemyUnits[i].owner : null;
                if (o != null && !enemies.Contains(o)) enemies.Add(o);
            }

            // Before.
            foreach (Unit e in affected) {
                if (!baseline.reach.ContainsKey(e)) { HashSet<HexTile> tiles; baseline.reach[e] = Reach(e, out tiles); baseline.reachTiles[e] = tiles; }
            }
            foreach (Player o in enemies) {
                if (!baseline.supply.ContainsKey(o)) baseline.supply[o] = SupplyNetwork.Solve(o, false);
            }

            // After: move the unit on the board, measure, put it back.
            HexTile from = u.tile;
            int afterReach = 0, beforeReach = 0;
            Dictionary<Player, SupplyNetwork.Result> afterSupply = new Dictionary<Player, SupplyNetwork.Result>();
            // Only what the move can touch is measured again: an enemy whose reach neither contains
            // the tile taken nor borders the tile left keeps its count (movement has no zone of
            // control), and a side whose supply comes nowhere near either tile keeps its field.
            // (Every enemy and every side was re-solved for every option: much of the AI's time.)
            List<Player> resolve = new List<Player>();
            foreach (Player o in enemies) if (Near(baseline.supply[o], at) || Near(baseline.supply[o], from)) resolve.Add(o); else afterSupply[o] = baseline.supply[o];
            from.occupier = null;
            at.occupier = u;
            try {
                foreach (Unit e in affected) {
                    beforeReach += baseline.reach[e];
                    HashSet<HexTile> tiles = baseline.reachTiles[e];
                    if (!tiles.Contains(at) && !Borders(tiles, e, from)) afterReach += baseline.reach[e];
                    else { HashSet<HexTile> unused; afterReach += Reach(e, out unused); }
                }
                foreach (Player o in resolve) afterSupply[o] = SupplyNetwork.Solve(o, false);
            } finally {
                at.occupier = null;
                from.occupier = u;
                // Solve publishes onto HexTile.supply; republish the real network.
                foreach (Player o in resolve) SupplyNetwork.Solve(o, false);
            }
            r.tiles = beforeReach - afterReach;

            foreach (Player o in enemies) {
                SupplyNetwork.Result before = baseline.supply[o], after = afterSupply[o];
                r.supplyTiles += Count(before) - Count(after);
                for (int i = 0; i < o.units.Count; i++) {
                    Unit e = o.units[i];
                    if (e == null || e.tile == null) continue;
                    if (before.DepthAt(e.tile) > 0 && after.DepthAt(e.tile) <= 0) r.unitsCut++;
                }
            }

            float worth = AIScore.Worth(u);
            r.gold = r.tiles * worth * p.denialTileWeight
                   + r.supplyTiles * worth * p.supplyTileWeight;
            // A unit cut off is a turn of attrition pressure on it: its worth.
            for (int i = 0; i < enemies.Count; i++) {
                Player o = enemies[i];
                for (int k = 0; k < o.units.Count; k++) {
                    Unit e = o.units[k];
                    if (e == null || e.tile == null) continue;
                    if (baseline.supply[o].DepthAt(e.tile) > 0 && afterSupply[o].DepthAt(e.tile) <= 0)
                        r.gold += AIScore.Worth(e) * p.supplyCutWeight;
                }
            }
            return r;
        }

        /// <summary>
        /// Damage the enemy could put on our most endangered HQ on its coming turn, if
        /// <paramref name="u"/> ends on <paramref name="at"/> and its shot (if any) lands on
        /// <paramref name="target"/> for <paramref name="damage"/>. Each attacker needs its own
        /// firing tile it can actually reach — movement re-run with the unit in its new place —
        /// so standing on an approach, or in the path to one, takes attackers away; and a
        /// damaged attacker hits for proportionally less, a dead one not at all.
        /// Also returns that HQ's strength (0 when there is no HQ in reach of anything).
        /// </summary>
        public static float HQThreat(Unit u, HexTile at, Unit target, int damage, AIMap map, out int hqStrength) {
            hqStrength = 0;
            float worst = 0f;
            if (GameController.gc != null && GameController.gc.rules == RuleSet.Attrition) return 0f;   // HQs out of play
            for (int h = 0; h < map.ownHubs.Count; h++) {
                SupplyPoint sp = map.ownHubs[h];
                if (sp == null || !sp.producer || sp.myUnit == null || sp.myUnit.tile == null) continue;
                Unit hq = sp.myUnit;
                List<Unit> attackers = new List<Unit>();
                for (int i = 0; i < map.enemyUnits.Count; i++) {
                    Unit e = map.enemyUnits[i];
                    if (e == null || e.tile == null || e.Firepower <= 0 || e.GetComponent<SupplyPoint>() != null) continue;
                    if (e == target && damage >= e.Strength) continue;
                    int reach = (e.moveAndAttack ? e.moveRange : 0) + e.AttackRange;
                    if (e.tile.Distance(hq.tile) <= reach) attackers.Add(e);
                }
                if (attackers.Count == 0) continue;
                attackers.Sort((a, b) => {
                    int c = Unit.calculateDamage(b, hq, hq.tile, 0).CompareTo(Unit.calculateDamage(a, hq, hq.tile, 0));
                    return c != 0 ? c : AIMap.CompareUnits(a, b);
                });

                HexTile from = u.tile;
                bool moving = at != null && at != from && from.occupier == u && (at.occupier == null || at.occupier == u);
                Dictionary<Unit, HashSet<HexTile>> positions = new Dictionary<Unit, HashSet<HexTile>>();
                if (moving) { from.occupier = null; at.occupier = u; }
                try {
                    foreach (Unit e in attackers) positions[e] = Positions(e);
                } finally {
                    if (moving) { at.occupier = null; from.occupier = u; }
                }

                float total = 0f;
                HashSet<HexTile> claimed = new HashSet<HexTile>();
                foreach (Unit e in attackers) {
                    float dmg = Mathf.Min(hq.Strength, Unit.calculateDamage(e, hq, hq.tile, 0));
                    if (e == target && damage > 0) dmg *= Mathf.Max(0f, e.Strength - damage) / (float)e.Strength;
                    if (dmg <= 0f) continue;
                    if (!e.moveAndAttack) {
                        int d = e.tile.Distance(hq.tile);
                        if (d <= e.AttackRange && d > e.MinRange) total += dmg;
                        continue;
                    }
                    List<HexTile> ring = AIPath.TilesInRange(hq.tile, e.MinRange, e.AttackRange);
                    ring.Sort(CombatMath.CompareTiles);
                    foreach (HexTile r in ring) {
                        if (claimed.Contains(r) || !positions[e].Contains(r)) continue;
                        if (moving && r == at) continue;
                        if (r.occupier != null && r.occupier != e && !(moving && r.occupier == u)) continue;
                        claimed.Add(r);
                        total += dmg;
                        break;
                    }
                }
                if (total > worst || hqStrength == 0) { worst = Mathf.Max(worst, total); hqStrength = hq.Strength; }
            }
            return worst;
        }

        /// <summary>Every tile this enemy could stand on to fire tonight, given current occupancy.</summary>
        static HashSet<HexTile> Positions(Unit e) {
            HashSet<HexTile> positions = new HashSet<HexTile>();
            positions.Add(e.tile);
            if (e.moveAndAttack) {
                MoveField f = AIPath.ThisTurn(e);
                foreach (HexTile p in f.Tiles) {
                    if (p != e.tile && f.ReachableThisTurn(p) && AIPath.CanFinishTurnOn(e, p)) positions.Add(p);
                }
            }
            return positions;
        }

        /// <summary>Tiles this unit can reach within two turns from where it stands.</summary>
        static int Reach(Unit e, out HashSet<HexTile> tiles) {
            MoveField f = AIPath.MultiTurn(e, 1);
            tiles = new HashSet<HexTile>();
            int n = 0;
            foreach (HexTile t in f.Tiles) if (!f.Get(t).IsUnreachable) { n++; tiles.Add(t); }
            return n;
        }

        /// <summary>Is <paramref name="t"/> one of <paramref name="tiles"/>, the unit's own tile, or beside one?</summary>
        static bool Borders(HashSet<HexTile> tiles, Unit e, HexTile t) {
            if (t == null) return false;
            if (tiles.Contains(t) || t == e.tile) return true;
            foreach (HexTile n in t.neighbors) if (n != null && (tiles.Contains(n) || n == e.tile)) return true;
            return false;
        }

        /// <summary>Could a unit on (or leaving) <paramref name="t"/> change this supply field: is it in it, or beside it?</summary>
        static bool Near(SupplyNetwork.Result s, HexTile t) {
            if (s == null || s.depth == null || t == null) return false;
            if (s.depth.ContainsKey(t)) return true;
            foreach (HexTile n in t.neighbors) if (n != null && s.depth.ContainsKey(n)) return true;
            return false;
        }

        static int Count(SupplyNetwork.Result s) {
            return s != null && s.depth != null ? s.depth.Count : 0;
        }
    }
}
