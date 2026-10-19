using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Screening as space, the way Go treats territory: the cheap units coordinate to create
    /// ground where the enemy's attack is degraded, without standing in it themselves, and the
    /// valuable units then take that ground because from it they strike next turn at full
    /// strength.
    ///
    /// Mechanics it rests on. Units block enemy movement (AIPath.CanPassThrough), and every
    /// attacker needs a tile of its own to fire from. So where the screen stands changes which
    /// tiles each enemy can reach tonight — through real pathing, terrain included — and how many
    /// of them can fire on any given tile at once. A tile with six open approaches can take six
    /// attackers; with the screen on three of them and forest on two, it takes one.
    ///
    /// How it plans. Once per board state it partitions our units: the screen is the cheap line
    /// units that have yet to act (grunts); the shielded are the valuable ones (tanks, guns), acted
    /// or not. A placement of the screen is scored by re-running every affected enemy's movement
    /// with the screen in place and letting each shielded unit, most valuable first, take its best
    /// tile — reachable this turn, one unit per tile — where "best" is what it could strike next
    /// turn from there, at the strength it would keep, less tonight's threat there in gold. A unit
    /// that has already acted is fixed where it stands, so the screen also closes in around a tank
    /// that struck first. The screen's own exposure and supply are subtracted. Placements are improved one
    /// screener at a time over two passes (coordinate ascent), deterministic throughout.
    ///
    /// How it acts. A screener's credit for a tile is the shielded units' gain from it standing
    /// there rather than staying put, with the rest of the screen where the plan puts it. That is
    /// what makes the grunts coordinate: each one's tile is worth something because of where the
    /// others will be. The plan is rebuilt after every action, so it follows what actually
    /// happened, and the armour — which acts after the slow units — sees the enemy fire field
    /// recomputed with the grunts in place (AIMap.RefreshAfterAction) and finds the protected
    /// tiles cheaper on its own.
    /// </summary>
    public static class AISpace {

        /// <summary>Diagnostic: plan summaries and every non-zero credit computed.</summary>
        public static Action<string> DebugLog;

        const int MAX_CANDIDATES = 18;
        const int PASSES = 2;

        /// <summary>Everything the plan needs, for one board state; cached on the map.</summary>
        public class Board {
            public readonly List<Unit> shielded = new List<Unit>();
            public readonly List<Unit> screen = new List<Unit>();
            public readonly Dictionary<Unit, List<HexTile>> useful = new Dictionary<Unit, List<HexTile>>();
            public readonly Dictionary<Unit, List<HexTile>> candidates = new Dictionary<Unit, List<HexTile>>();
            public readonly List<Unit> enemies = new List<Unit>();
            public readonly List<Unit> attackers = new List<Unit>();
            public readonly Dictionary<Unit, HashSet<HexTile>> basePositions = new Dictionary<Unit, HashSet<HexTile>>();
            public readonly Dictionary<Unit, Dictionary<HexTile, float>> potential = new Dictionary<Unit, Dictionary<HexTile, float>>();
            public readonly Dictionary<Unit, HexTile> plan = new Dictionary<Unit, HexTile>();
            public readonly Dictionary<Unit, Dictionary<HexTile, float>> credit = new Dictionary<Unit, Dictionary<HexTile, float>>();
            public AIMap map;
            public bool empty = true;
        }

        public static Board Prepare(AIMap map) {
            Board b = new Board { map = map };

            // Partition: shielded are worth at least twice the cheapest fighting unit.
            float cheapest = float.MaxValue;
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (Fighter(u)) cheapest = Mathf.Min(cheapest, AIScore.Worth(u));
            }
            if (cheapest == float.MaxValue) return b;
            float line = cheapest * 2f;
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (!Fighter(u)) continue;
                if (AIScore.Worth(u) >= line) b.shielded.Add(u);
                else if (u.active && u.moveAndAttack && u.Strength > map.profile.withdrawStrength) b.screen.Add(u);
            }
            if (b.shielded.Count == 0 || b.screen.Count == 0) return b;
            b.shielded.Sort((x, y) => {
                int c = AIScore.Worth(y).CompareTo(AIScore.Worth(x));
                return c != 0 ? c : AIMap.CompareUnits(x, y);
            });
            b.screen.Sort(AIMap.CompareUnits);

            for (int j = 0; j < map.enemyUnits.Count; j++) {
                Unit e = map.enemyUnits[j];
                if (e == null || e.tile == null || e.Strength <= 0) continue;
                b.enemies.Add(e);
                if (!e.CanAttack(e.tile) || e.Firepower <= 0) continue;
                b.basePositions[e] = Positions(e);
                b.attackers.Add(e);
            }
            if (b.attackers.Count == 0) return b;
            b.attackers.Sort((x, y) => {
                int c = (y.Firepower * y.Strength).CompareTo(x.Firepower * x.Strength);
                return c != 0 ? c : AIMap.CompareUnits(x, y);
            });

            // Tiles each shielded unit could use: reachable this turn and within striking distance
            // of an enemy next turn, plus where it stands. Screeners' current tiles are included,
            // since they may move off them.
            foreach (Unit f in b.shielded) {
                List<HexTile> list = new List<HexTile>();
                if (f.active) {
                    MoveField field = map.ReachThisTurn(f);
                    int reach = f.moveAndAttack ? f.moveRange + f.AttackRange : f.AttackRange;
                    foreach (HexTile s in field.Tiles) {
                        if (!field.ReachableThisTurn(s) || !f.CanEnter(s)) continue;
                        if (s.occupier != null && s.occupier != f && !b.screen.Contains(s.occupier)) continue;
                        if (NearEnemy(b, s, reach)) list.Add(s);
                    }
                }
                if (!list.Contains(f.tile)) list.Add(f.tile);
                list.Sort(CombatMath.CompareTiles);
                b.useful[f] = list;
            }

            // Where each screener might stand: reachable this turn, where blocking can matter
            // (within an enemy's move of it), nearest the shielded units' useful ground first.
            HashSet<HexTile> ground = new HashSet<HexTile>();
            foreach (Unit f in b.shielded) foreach (HexTile s in b.useful[f]) ground.Add(s);
            foreach (Unit g in b.screen) {
                MoveField field = map.ReachThisTurn(g);
                List<KeyValuePair<int, HexTile>> scored = new List<KeyValuePair<int, HexTile>>();
                foreach (HexTile t in field.Tiles) {
                    if (t == g.tile || !field.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(g, t)) continue;
                    bool contested = false;
                    for (int j = 0; j < b.attackers.Count && !contested; j++) {
                        Unit e = b.attackers[j];
                        if (t.Distance(e.tile) <= (e.moveAndAttack ? e.moveRange : 0) + e.AttackRange + 1) contested = true;
                    }
                    if (!contested) continue;
                    int near = int.MaxValue;
                    foreach (HexTile s in ground) near = Mathf.Min(near, s.Distance(t));
                    if (near > 2) continue;
                    scored.Add(new KeyValuePair<int, HexTile>(near, t));
                }
                scored.Sort((x, y) => {
                    int c = x.Key.CompareTo(y.Key);
                    return c != 0 ? c : CombatMath.CompareTiles(x.Value, y.Value);
                });
                List<HexTile> cands = new List<HexTile> { g.tile };
                for (int i = 0; i < scored.Count && cands.Count < MAX_CANDIDATES; i++) cands.Add(scored[i].Value);
                b.candidates[g] = cands;
                b.plan[g] = g.tile;
            }
            b.empty = false;

            // Coordinate ascent on the whole placement.
            for (int pass = 0; pass < PASSES; pass++) {
                bool changed = false;
                foreach (Unit g in b.screen) {
                    HexTile best = b.plan[g];
                    float bestScore = Objective(b, b.plan, g, best);
                    foreach (HexTile t in b.candidates[g]) {
                        if (t == best || Taken(b.plan, g, t)) continue;
                        float score = Objective(b, b.plan, g, t);
                        if (score > bestScore + 0.5f) { bestScore = score; best = t; }
                    }
                    if (best != b.plan[g]) { b.plan[g] = best; changed = true; }
                }
                if (!changed) break;
            }

            if (DebugLog != null) {
                string s = "plan:";
                foreach (Unit g in b.screen) s += " " + g.name + "(" + g.tile.x + "," + g.tile.y + ")->(" + b.plan[g].x + "," + b.plan[g].y + ")";
                s += " shielding";
                foreach (Unit f in b.shielded) s += " " + f.name + (f.active ? "" : "*");
                DebugLog(s);
            }
            return b;
        }

        /// <summary>
        /// Gold the shielded units gain from screener <paramref name="g"/> ending on
        /// <paramref name="t"/> rather than staying put, with the rest of the screen where the
        /// plan puts it. Zero for units that are not part of the screen.
        /// </summary>
        public static float Credit(Board b, Unit g, HexTile t) {
            if (b == null || b.empty || g == null || t == null || !b.plan.ContainsKey(g)) return 0f;
            if (t == g.tile) return 0f;
            Dictionary<HexTile, float> mine;
            if (!b.credit.TryGetValue(g, out mine)) { mine = new Dictionary<HexTile, float>(); b.credit[g] = mine; }
            float c;
            if (mine.TryGetValue(t, out c)) return c;
            if ((t.occupier != null && t.occupier != g) || Taken(b.plan, g, t)) { mine[t] = 0f; return 0f; }
            c = Shield(b, b.plan, g, t) - Shield(b, b.plan, g, g.tile);
            mine[t] = c;
            if (DebugLog != null && c != 0f) DebugLog(g.name + " (" + g.tile.x + "," + g.tile.y + ")->(" + t.x + "," + t.y + ") credit " + c.ToString("0"));
            return c;
        }

        static bool Fighter(Unit u) {
            return u != null && u.tile != null && u.trasnporter == null && u.Firepower > 0
                && u.Strength > 0 && u.GetComponent<SupplyPoint>() == null;
        }

        static bool NearEnemy(Board b, HexTile s, int reach) {
            for (int j = 0; j < b.enemies.Count; j++) if (s.Distance(b.enemies[j].tile) <= reach) return true;
            return false;
        }

        static bool Taken(Dictionary<Unit, HexTile> plan, Unit g, HexTile t) {
            foreach (KeyValuePair<Unit, HexTile> kv in plan) if (kv.Key != g && kv.Value == t) return true;
            return false;
        }

        /// <summary>Plan objective: the shielded units' value with <paramref name="g"/> on <paramref name="t"/>, less the screen's own exposure.</summary>
        static float Objective(Board b, Dictionary<Unit, HexTile> plan, Unit g, HexTile t) {
            float v = Shield(b, plan, g, t);
            foreach (KeyValuePair<Unit, HexTile> kv in plan) {
                HexTile at = kv.Key == g ? t : kv.Value;
                v -= b.map.ExpectedLoss(kv.Key, at) * AIScore.Worth(kv.Key) * b.map.profile.riskWeight;
                // A screen that starves is no screen: the plan pays the screen's supply too.
                v -= AIScore.OutOfSupplyCost(kv.Key, at, b.map) + AIScore.StrandingCost(kv.Key, at, 0, b.map);
            }
            return v;
        }

        /// <summary>
        /// Value to the shielded units of the screen standing per <paramref name="plan"/>, with
        /// <paramref name="g"/> moved to <paramref name="t"/>.
        /// </summary>
        static float Shield(Board b, Dictionary<Unit, HexTile> plan, Unit g, HexTile t) {
            // Apply the placement to the board's occupancy, recompute the enemies it can affect,
            // and restore — whatever happens.
            List<KeyValuePair<Unit, HexTile>> moves = new List<KeyValuePair<Unit, HexTile>>();
            foreach (KeyValuePair<Unit, HexTile> kv in plan) {
                HexTile to = kv.Key == g ? t : kv.Value;
                if (to != kv.Key.tile) moves.Add(new KeyValuePair<Unit, HexTile>(kv.Key, to));
            }
            HashSet<HexTile> occupiedByScreen = new HashSet<HexTile>();
            foreach (KeyValuePair<Unit, HexTile> kv in plan) occupiedByScreen.Add(kv.Key == g ? t : kv.Value);

            Dictionary<Unit, HashSet<HexTile>> world = new Dictionary<Unit, HashSet<HexTile>>();
            List<KeyValuePair<HexTile, Unit>> restore = new List<KeyValuePair<HexTile, Unit>>();
            try {
                foreach (KeyValuePair<Unit, HexTile> m in moves) {
                    restore.Add(new KeyValuePair<HexTile, Unit>(m.Key.tile, m.Key.tile.occupier));
                    restore.Add(new KeyValuePair<HexTile, Unit>(m.Value, m.Value.occupier));
                }
                foreach (KeyValuePair<Unit, HexTile> m in moves) if (m.Key.tile.occupier == m.Key) m.Key.tile.occupier = null;
                foreach (KeyValuePair<Unit, HexTile> m in moves) if (m.Value.occupier == null) m.Value.occupier = m.Key;

                foreach (KeyValuePair<Unit, HashSet<HexTile>> kv in b.basePositions) {
                    Unit e = kv.Key;
                    bool affected = false;
                    if (e.moveAndAttack) {
                        foreach (KeyValuePair<Unit, HexTile> m in moves) {
                            if (e.tile.Distance(m.Value) <= e.moveRange + 1 || e.tile.Distance(m.Key.tile) <= e.moveRange + 1) { affected = true; break; }
                        }
                    }
                    world[e] = affected ? Positions(e) : kv.Value;
                }
            } finally {
                for (int i = restore.Count - 1; i >= 0; i--) restore[i].Key.occupier = restore[i].Value;
            }

            HashSet<HexTile> used = new HashSet<HexTile>(occupiedByScreen);
            float total = 0f;
            foreach (Unit f in b.shielded) {
                float best = float.NegativeInfinity;
                HexTile bestTile = null;
                float worth = AIScore.Worth(f);
                List<HexTile> tiles = b.useful[f];
                for (int i = 0; i < tiles.Count; i++) {
                    HexTile s = tiles[i];
                    if (used.Contains(s)) continue;
                    float threat = Threat(b, world, f, s, used);
                    float kept = f.Strength > 0 ? Mathf.Max(0f, f.Strength - threat) / f.Strength : 0f;
                    float net = Potential(b, f, s) * kept - Mathf.Min(f.Strength, threat) * worth;
                    if (net > best) { best = net; bestTile = s; }
                }
                if (bestTile == null) continue;
                used.Add(bestTile);
                total += best;
            }
            return total;
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

        /// <summary>
        /// Damage the enemy could put on <paramref name="f"/> standing at <paramref name="s"/>
        /// tonight: the best assignment of attackers to distinct firing tiles they can reach,
        /// strongest first, excluding tiles our units stand on. Not capped at the target's
        /// strength, so each attacker shut out counts even before the approach is closed — or the
        /// first grunt of a screen would see no gain until the last one completed it.
        /// </summary>
        static float Threat(Board b, Dictionary<Unit, HashSet<HexTile>> positions, Unit f, HexTile s, HashSet<HexTile> ours) {
            float total = 0f;
            HashSet<HexTile> claimed = new HashSet<HexTile>();
            foreach (Unit e in b.attackers) {
                HashSet<HexTile> pos;
                if (!positions.TryGetValue(e, out pos)) continue;
                int dmg = Mathf.Min(f.Strength, Unit.calculateDamage(e, f, s, 0));
                if (dmg <= 0) continue;
                if (!e.moveAndAttack) {
                    int d = e.tile.Distance(s);
                    if (d <= e.AttackRange && d > e.MinRange) total += dmg;
                    continue;
                }
                List<HexTile> ring = AIPath.TilesInRange(s, e.MinRange, e.AttackRange);
                ring.Sort(CombatMath.CompareTiles);
                for (int i = 0; i < ring.Count; i++) {
                    HexTile r = ring[i];
                    if (r == s || claimed.Contains(r) || ours.Contains(r) || !pos.Contains(r)) continue;
                    claimed.Add(r);
                    total += dmg;
                    break;
                }
            }
            return total;
        }

        /// <summary>Best damage, in gold, <paramref name="f"/> could strike from <paramref name="s"/> next turn.</summary>
        static float Potential(Board b, Unit f, HexTile s) {
            Dictionary<HexTile, float> cache;
            if (!b.potential.TryGetValue(f, out cache)) { cache = new Dictionary<HexTile, float>(); b.potential[f] = cache; }
            float v;
            if (cache.TryGetValue(s, out v)) return v;
            v = 0f;
            int reach = f.moveAndAttack ? f.moveRange + f.AttackRange : f.AttackRange;
            foreach (Unit e in b.enemies) {
                int d = s.Distance(e.tile);
                if (d > reach || (!f.moveAndAttack && d <= f.MinRange)) continue;
                int dmg = Mathf.Min(e.Strength, Unit.calculateDamage(f, e, e.tile, 0));
                v = Mathf.Max(v, dmg * AIScore.Worth(e));
            }
            cache[s] = v;
            return v;
        }
    }
}
