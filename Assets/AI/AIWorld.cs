using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Score a candidate action by the world it leaves after the enemy's reply.
    ///
    /// The tiers judged an action by parts — damage dealt, damage expected tonight, ground denied
    /// — and the parts disagreed in the cases that matter most. A grunt that could hit the enemy
    /// tank beside our HQ for one point, or stand on the tile that tank needs, could not compare
    /// the two: one is "attack", the other "denial". Here both are the same question: after we do
    /// this and they answer, what is left?
    ///
    /// The candidate is applied to a copy of the strengths (and the board's occupancy, for
    /// movement): our unit on its tile, its target damaged or dead, its retaliation taken. Then
    /// the enemy replies, one unit at a time, strongest first, each taking the attack a sensible
    /// opponent would — the most material it can destroy, kills counted double — from a tile it
    /// can actually reach this turn and nobody else has taken; damaged units hit for less;
    /// retaliation applies. What remains is valued in material on both sides, with the HQs at
    /// their cost — 100,000, which is "arbitrarily high" against a 7,000 tank — and a destroyed HQ
    /// worth the game.
    ///
    /// So an enemy tank that can reach our HQ for 5 of its 6 strength carries 5/6 of the HQ's
    /// value in the score until something stops it — hitting it hard enough that its blow is
    /// small, or standing where it would have to fire from. A tank that can strike ours first is
    /// what makes a forward tile cost the tank, because the reply is simulated, not averaged.
    /// </summary>
    public static class AIWorld {

        /// <summary>The world after an action and the enemy's reply.</summary>
        public class Outcome {
            public float score;
            public float own, enemy;
            public bool ourHQFalls, theirHQFalls, assist;
            /// <summary>Supply-unit deaths priced at their dependents (+ theirs, - ours).</summary>
            public float hubs;
            /// <summary>Best next blows: ours (we act first), theirs after.</summary>
            public float ownStrike, enemyStrike;
            public readonly List<string> replies = new List<string>();
            /// <summary>Strength each of our units loses in the reply.</summary>
            public readonly Dictionary<Unit, float> lost = new Dictionary<Unit, float>();
            public string Summary() {
                StringBuilder sb = new StringBuilder();
                sb.Append(score.ToString("0"));
                if (ourHQFalls) sb.Append(" OUR HQ FALLS");
                if (assist) sb.Append(" assist");
                if (hubs != 0f) sb.Append(" hubs ").Append(hubs.ToString("+0;-0"));
                if (ownStrike != 0f || enemyStrike != 0f) sb.Append(" strike +").Append(ownStrike.ToString("0")).Append("/-").Append(enemyStrike.ToString("0"));
                if (replies.Count > 0) sb.Append(" [").Append(string.Join("; ", replies.ToArray())).Append(']');
                return sb.ToString();
            }
        }

        /// <summary>Per-board cache of each enemy's reachable firing positions with nothing moved.</summary>
        public class Cache {
            public readonly Dictionary<Unit, HashSet<HexTile>> positions = new Dictionary<Unit, HashSet<HexTile>>();
        }

        public static Outcome Evaluate(Unit u, HexTile stop, AttackAppraisal shot, AIMap map, Cache cache, bool narrate) {
            Outcome w = new Outcome();
            Dictionary<Unit, float> hp = new Dictionary<Unit, float>();
            foreach (Unit x in map.ownUnits) if (x != null && x.tile != null) hp[x] = x.Strength;
            foreach (Unit x in map.enemyUnits) if (x != null && x.tile != null) hp[x] = x.Strength;

            // Our action.
            if (shot.valid && shot.target != null && hp.ContainsKey(shot.target)) {
                hp[shot.target] = Mathf.Max(0f, hp[shot.target] - shot.outcome.damageToDefender);
                hp[u] = Mathf.Max(0f, hp[u] - shot.outcome.damageToAttacker);
                // An assist: the target survives, but units of ours still to act this turn can
                // finish it. Counted as assistWeight of a kill — its reply and its material go
                // in that proportion — so a hit that sets up a kill beats a hit that does not.
                if (hp[shot.target] > 0f && map.profile.assistWeight > 0f) {
                    string by;
                    if (CanFinish(shot.target, hp[shot.target], u, map, out by)) {
                        w.assist = true;
                        hp[shot.target] *= 1f - map.profile.assistWeight;
                        if (narrate) w.replies.Add("assist: " + by + " can finish " + shot.target.name);
                    }
                }
            }

            // Their reply, on the board with our unit moved.
            HexTile from = u.tile;
            bool moving = stop != null && stop != from && from.occupier == u && (stop.occupier == null || stop.occupier == u);
            Unit stopWas = stop != null ? stop.occupier : null;
            List<Unit> enemies = new List<Unit>(map.enemyUnits);
            enemies.Sort((a, b) => {
                int c = (b.Firepower * b.Strength).CompareTo(a.Firepower * a.Strength);
                return c != 0 ? c : AIMap.CompareUnits(a, b);
            });
            Dictionary<Unit, HashSet<HexTile>> positions = new Dictionary<Unit, HashSet<HexTile>>();
            if (moving) { from.occupier = null; stop.occupier = u; }
            try {
                foreach (Unit e in enemies) {
                    if (e == null || e.tile == null || !hp.ContainsKey(e) || hp[e] <= 0f) continue;
                    if (e.Firepower <= 0 || !e.CanAttack(e.tile)) continue;
                    bool near = e.moveAndAttack && (e.tile.Distance(stop ?? from) <= e.moveRange + 1 || e.tile.Distance(from) <= e.moveRange + 1);
                    HashSet<HexTile> p;
                    if (!near && cache.positions.TryGetValue(e, out p)) { positions[e] = p; continue; }
                    p = Positions(e);
                    positions[e] = p;
                    if (!near) cache.positions[e] = p;
                }
            } finally {
                if (moving) { stop.occupier = stopWas; from.occupier = u; }
            }

            HexTile uAt = stop ?? from;
            Dictionary<Unit, HexTile> endsAt = new Dictionary<Unit, HexTile>();
            HashSet<HexTile> taken = new HashSet<HexTile>();
            foreach (Unit x in map.ownUnits) if (x != null && x.tile != null && x != u) taken.Add(x.tile);
            taken.Add(uAt);
            foreach (Unit e in enemies) if (e != null && e.tile != null) taken.Add(e.tile);

            foreach (Unit e in enemies) {
                HashSet<HexTile> pos;
                if (!positions.TryGetValue(e, out pos) || hp[e] <= 0f) continue;
                float scale = hp[e] / Mathf.Max(1f, e.Strength);
                Unit bestTarget = null; HexTile bestTile = null; float bestValue = 0f, bestDamage = 0f;
                foreach (Unit d in map.ownUnits) {
                    if (d == null || d.tile == null || !hp.ContainsKey(d) || hp[d] <= 0f) continue;
                    HexTile dAt = d == u ? uAt : d.tile;
                    float dmg = Mathf.Min(hp[d], Unit.calculateDamage(e, d, dAt, 0) * scale);
                    if (dmg <= 0f) continue;
                    HexTile fire = FiringTile(e, dAt, pos, taken);
                    if (fire == null) continue;
                    float value = dmg * Value(d) * (dmg >= hp[d] ? 2f : 1f);
                    if (value > bestValue || (value == bestValue && bestTarget != null && AIMap.CompareUnits(d, bestTarget) < 0)) {
                        bestValue = value; bestTarget = d; bestTile = fire; bestDamage = dmg;
                    }
                }
                if (bestTarget == null) continue;
                if (bestTile != e.tile) { taken.Remove(e.tile); taken.Add(bestTile); }
                endsAt[e] = bestTile;
                hp[bestTarget] -= bestDamage;
                float sofar;
                w.lost[bestTarget] = (w.lost.TryGetValue(bestTarget, out sofar) ? sofar : 0f) + bestDamage;
                // Retaliation, if the defender survives and can answer at that range.
                if (hp[bestTarget] > 0f && bestTarget.retaliates && bestTarget.Firepower > 0) {
                    HexTile dAt = bestTarget == u ? uAt : bestTarget.tile;
                    int range = dAt.Distance(bestTile);
                    if (range <= bestTarget.AttackRange && range > bestTarget.MinRange) {
                        float back = Unit.calculateDamage(bestTarget, e, bestTile, 0) * hp[bestTarget] / Mathf.Max(1f, bestTarget.Strength);
                        hp[e] = Mathf.Max(0f, hp[e] - back);
                    }
                }
                if (narrate) w.replies.Add(e.name + "->" + bestTarget.name + " " + bestDamage.ToString("0.#"));
            }

            // What is left.
            float stake = map.BoardStake;
            foreach (KeyValuePair<Unit, float> kv in hp) {
                float v = Mathf.Max(0f, kv.Value) * Value(kv.Key);
                bool hq = IsHQ(kv.Key);
                if (kv.Key.owner == u.owner) {
                    w.own += v;
                    if (hq && kv.Value <= 0f) w.ourHQFalls = true;
                } else {
                    w.enemy += v;
                    if (hq && kv.Value <= 0f) w.theirHQFalls = true;
                }
            }
            w.score = w.own - w.enemy;
            if (w.ourHQFalls) w.score -= stake;
            if (w.theirHQFalls) w.score += stake;

            // A supply unit's death also costs a strength of everyone only it kept supplied.
            AIProfile prof = map.profile;
            if (prof.hubDependentsWeight > 0f) {
                foreach (KeyValuePair<Unit, float> kv in hp) {
                    if (kv.Value > 0f || IsHQ(kv.Key) || kv.Key.GetComponent<SupplyPoint>() == null) continue;
                    float dep = map.HubStake(kv.Key) * prof.hubDependentsWeight;
                    if (kv.Key.owner == u.owner) w.hubs -= dep; else w.hubs += dep;
                }
                w.score += w.hubs;
            }

            // Each unit's point of view: what it can hit next. Ours strike first.
            if (prof.ownStrikeWeight > 0f || prof.enemyStrikeWeight > 0f) {
                foreach (Unit x in hp.Keys) {
                    if (hp[x] <= 0f || x.Firepower <= 0 || IsHQ(x)) continue;
                    bool ours = x.owner == u.owner;
                    float weight = ours ? prof.ownStrikeWeight : prof.enemyStrikeWeight;
                    if (weight <= 0f) continue;
                    HexTile xAt = x == u ? uAt : (endsAt.ContainsKey(x) ? endsAt[x] : x.tile);
                    float scale = hp[x] / Mathf.Max(1f, x.Strength);
                    int reach = (x.moveAndAttack ? x.moveRange : 0) + x.AttackRange;
                    float best = 0f;
                    foreach (Unit d in hp.Keys) {
                        if (hp[d] <= 0f || (d.owner == u.owner) == ours) continue;
                        HexTile dAt = d == u ? uAt : (endsAt.ContainsKey(d) ? endsAt[d] : d.tile);
                        if (xAt.Distance(dAt) > reach) continue;
                        float dmg = Mathf.Min(hp[d], Unit.calculateDamage(x, d, dAt, 0) * scale);
                        best = Mathf.Max(best, dmg * Value(d));
                    }
                    if (ours) w.ownStrike += best * weight; else w.enemyStrike += best * weight;
                }
                w.score += w.ownStrike - w.enemyStrike;
            }
            return w;
        }

        /// <summary>
        /// Can our units that have not acted yet this turn finish <paramref name="target"/> at
        /// <paramref name="left"/> strength? Hex reach (move + range), strongest first.
        /// </summary>
        public static bool CanFinish(Unit target, float left, Unit except, AIMap map, out string by) {
            by = "";
            List<float> blows = new List<float>();
            List<string> names = new List<string>();
            foreach (Unit x in map.ownUnits) {
                if (x == null || x == except || x.tile == null || !x.active || x.Firepower <= 0) continue;
                if (x.GetComponent<SupplyPoint>() != null || x.trasnporter != null) continue;
                int reach = (x.moveAndAttack ? x.moveRange : 0) + x.AttackRange;
                if (x.tile.Distance(target.tile) > reach) continue;
                if (!x.moveAndAttack && x.tile.Distance(target.tile) <= x.MinRange) continue;
                float dmg = Unit.calculateDamage(x, target, target.tile, 0);
                if (dmg <= 0f) continue;
                int at = 0;
                while (at < blows.Count && blows[at] >= dmg) at++;
                blows.Insert(at, dmg); names.Insert(at, x.name);
            }
            float sum = 0f;
            for (int i = 0; i < blows.Count; i++) {
                sum += blows[i];
                by += (i > 0 ? "+" : "") + names[i];
                if (sum >= left) return true;
            }
            return false;
        }

        /// <summary>Material per strength point; HQs at their (deliberately enormous) cost.</summary>
        static float Value(Unit x) {
            return AIScore.Worth(x);
        }

        static bool IsHQ(Unit x) {
            SupplyPoint sp = x != null ? x.GetComponent<SupplyPoint>() : null;
            return sp != null && sp.producer;
        }

        static HexTile FiringTile(Unit e, HexTile target, HashSet<HexTile> pos, HashSet<HexTile> taken) {
            if (!e.moveAndAttack) {
                int d = e.tile.Distance(target);
                return d <= e.AttackRange && d > e.MinRange ? e.tile : null;
            }
            List<HexTile> ring = AIPath.TilesInRange(target, e.MinRange, e.AttackRange);
            ring.Sort(CombatMath.CompareTiles);
            if (ring.Contains(e.tile)) return e.tile;
            foreach (HexTile r in ring) if (pos.Contains(r) && !taken.Contains(r)) return r;
            return null;
        }

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
    }
}
