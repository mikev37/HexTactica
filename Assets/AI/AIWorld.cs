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
            /// <summary>Strength value given back for damage that will heal (ours, theirs).</summary>
            public float healOwn, healEnemy;
            /// <summary>Space: move+strike tiles (ours, theirs), enemy supply tiles cut, and the gold it adds.</summary>
            public int ourSpace, theirSpace, supplyCut;
            public float space;
            public readonly List<string> replies = new List<string>();
            /// <summary>Strength each of our units loses in the reply.</summary>
            public readonly Dictionary<Unit, float> lost = new Dictionary<Unit, float>();
            public string Summary() {
                StringBuilder sb = new StringBuilder();
                sb.Append(score.ToString("0"));
                if (ourHQFalls) sb.Append(" OUR HQ FALLS");
                if (assist) sb.Append(" assist");
                if (hubs != 0f) sb.Append(" hubs ").Append(hubs.ToString("+0;-0"));
                if (space != 0f) sb.Append(" space ").Append(space.ToString("+0;-0")).Append(" (").Append(ourSpace).Append('/').Append(theirSpace).Append(supplyCut > 0 ? " cut " + supplyCut : "").Append(')');
                if (healOwn != 0f || healEnemy != 0f) sb.Append(" heals +").Append(healOwn.ToString("0")).Append("/-").Append(healEnemy.ToString("0"));
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
            return Evaluate(u, stop, shot, map, cache, narrate, false);
        }

        /// <param name="space">Also score space (AIProfile.spaceTileValue / spaceSupplyValue): used when choosing a move.</param>
        public static Outcome Evaluate(Unit u, HexTile stop, AttackAppraisal shot, AIMap map, Cache cache, bool narrate, bool space) {
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
                Unit bestTarget = null; HexTile bestTile = null; float bestValue = 0f; int bestDamage = 0, bestBack = 0;
                foreach (Unit d in map.ownUnits) {
                    if (d == null || d.tile == null || !hp.ContainsKey(d) || hp[d] <= 0f) continue;
                    HexTile dAt = d == u ? uAt : d.tile;
                    HexTile fire = FiringTile(e, dAt, pos, taken);
                    if (fire == null) continue;
                    // The game's own exchange: cover on the defender's tile, retaliation without
                    // cover, a dug-in veteran firing first, damaged units hitting for less.
                    int dmg, back;
                    CombatMath.Exchange(e, Hp(hp[e]), fire, d, Hp(hp[d]), dAt, out dmg, out back);
                    if (dmg <= 0) continue;
                    float value = (dmg * Value(d) - back * Value(e)) * (dmg >= hp[d] ? 2f : 1f);
                    if (value > bestValue || (value == bestValue && bestTarget != null && AIMap.CompareUnits(d, bestTarget) < 0)) {
                        bestValue = value; bestTarget = d; bestTile = fire; bestDamage = dmg; bestBack = back;
                    }
                }
                if (bestTarget == null) continue;
                if (bestTile != e.tile) { taken.Remove(e.tile); taken.Add(bestTile); }
                endsAt[e] = bestTile;
                float dealt = Mathf.Min(hp[bestTarget], bestDamage);
                hp[bestTarget] -= dealt;
                float sofar;
                w.lost[bestTarget] = (w.lost.TryGetValue(bestTarget, out sofar) ? sofar : 0f) + dealt;
                hp[e] = Mathf.Max(0f, hp[e] - bestBack);
                if (narrate) w.replies.Add(e.name + "->" + bestTarget.name + " " + dealt.ToString("0.#") + (bestBack > 0 ? "/" + bestBack : ""));
            }

            // What is left.
            float stake = map.BoardStake;
            float healF = map.profile.healingDamageFactor;
            foreach (KeyValuePair<Unit, float> kv in hp) {
                float left = Mathf.Max(0f, kv.Value);
                // Damage that will heal counts at healingDamageFactor; kills, and damage to units
                // that won't heal (out of supply, or a cut-off supply unit), count in full.
                if (left > 0f && left < kv.Key.Strength) {
                    HexTile at = kv.Key == u ? uAt : (endsAt.ContainsKey(kv.Key) ? endsAt[kv.Key] : kv.Key.tile);
                    if (map.Heals(kv.Key, at)) {
                        float healed = (kv.Key.Strength - left) * (1f - healF);
                        left += healed;
                        if (kv.Key.owner == u.owner) w.healOwn += healed * Value(kv.Key); else w.healEnemy += healed * Value(kv.Key);
                    }
                }
                float v = left * Value(kv.Key);
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
            //
            // HQs are not targets here: a blow on an HQ is already in the reply above, and in the
            // HQ danger terms. Counting the enemy's *second* blow on our HQ (worth 16,667 a point)
            // made any option that drew the enemy's reply away from the HQ look good — a tank
            // charged into a 1-for-4 exchange because the enemy then fired from tiles too far from
            // our HQ to hit it the turn after.
            //
            // And the enemy's next blow comes after our next turn: whatever we can destroy first
            // never lands, so each enemy's blow is discounted by the share of it our units can kill.
            if (prof.ownStrikeWeight > 0f || prof.enemyStrikeWeight > 0f) {
                Dictionary<Unit, float> ourFire = new Dictionary<Unit, float>();
                foreach (Unit x in hp.Keys) {
                    if (hp[x] <= 0f || x.Firepower <= 0 || IsHQ(x) || x.owner != u.owner) continue;
                    HexTile xAt = x == u ? uAt : x.tile;
                    float best = 0f;
                    foreach (Unit d in hp.Keys) {
                        if (hp[d] <= 0f || d.owner == u.owner || IsHQ(d)) continue;
                        HexTile dAt = endsAt.ContainsKey(d) ? endsAt[d] : d.tile;
                        int dmg, back;
                        if (!InReach(x, xAt, dAt, out dmg, out back, Hp(hp[x]), d, Hp(hp[d]))) continue;
                        float f; ourFire[d] = (ourFire.TryGetValue(d, out f) ? f : 0f) + dmg;
                        best = Mathf.Max(best, dmg * Value(d) - back * Value(x));
                    }
                    w.ownStrike += best * prof.ownStrikeWeight;
                }
                if (prof.enemyStrikeWeight > 0f) {
                    foreach (Unit x in hp.Keys) {
                        if (hp[x] <= 0f || x.Firepower <= 0 || IsHQ(x) || x.owner == u.owner) continue;
                        HexTile xAt = endsAt.ContainsKey(x) ? endsAt[x] : x.tile;
                        float survives = 1f;
                        float ours;
                        if (ourFire.TryGetValue(x, out ours)) survives = Mathf.Clamp01(1f - ours / hp[x]);
                        if (survives <= 0f) continue;
                        float best = 0f;
                        foreach (Unit d in hp.Keys) {
                            if (hp[d] <= 0f || d.owner != u.owner || IsHQ(d)) continue;
                            HexTile dAt = d == u ? uAt : d.tile;
                            int dmg, back;
                            if (!InReach(x, xAt, dAt, out dmg, out back, Hp(hp[x]), d, Hp(hp[d]))) continue;
                            best = Mathf.Max(best, dmg * Value(d) - back * Value(x));
                        }
                        w.enemyStrike += best * survives * prof.enemyStrikeWeight;
                    }
                }
                w.score += w.ownStrike - w.enemyStrike;
            }
            if (space && (prof.spaceTileValue > 0f || prof.spaceSupplyValue > 0f)) Space(w, u, uAt, from, moving, map, positions, hp);
            return w;
        }

        /// <summary>
        /// Space: tiles each side could move to and strike next turn (union over its units), and enemy
        /// supply cut. Ours with our unit on its new tile; theirs with their movement re-run around it
        /// (the same reachable positions the reply used).
        /// </summary>
        static void Space(Outcome w, Unit u, HexTile uAt, HexTile from, bool moving, AIMap map,
                          Dictionary<Unit, HashSet<HexTile>> positions, Dictionary<Unit, float> hp) {
            AIProfile prof = map.profile;
            HashSet<HexTile> ourMove = new HashSet<HexTile>(), ourStrike = new HashSet<HexTile>();
            foreach (Unit x in map.ownUnits) {
                if (x == null || x.tile == null || !hp.ContainsKey(x) || hp[x] <= 0f || x.trasnporter != null) continue;
                HashSet<HexTile>[] s = x == u ? AIMap.SpaceFrom(u, uAt, null) : map.SpaceOf(x);
                ourMove.UnionWith(s[0]);
                ourStrike.UnionWith(s[1]);
            }
            HashSet<HexTile> theirMove = new HashSet<HexTile>(), theirStrike = new HashSet<HexTile>();
            foreach (Unit e in map.enemyUnits) {
                if (e == null || e.tile == null || !hp.ContainsKey(e) || hp[e] <= 0f || e.trasnporter != null) continue;
                HashSet<HexTile> pos;
                positions.TryGetValue(e, out pos);
                HashSet<HexTile>[] s = AIMap.SpaceFrom(e, e.tile, pos);
                theirMove.UnionWith(s[0]);
                theirStrike.UnionWith(s[1]);
            }
            if (prof.spaceContestedOnly) {
                ourMove.RemoveWhere(t => map.EnemyStrikeTurns(t) > prof.enemyReachTurns);
                ourStrike.RemoveWhere(t => map.EnemyStrikeTurns(t) > prof.enemyReachTurns);
            }
            w.ourSpace = ourMove.Count + ourStrike.Count;
            w.theirSpace = theirMove.Count + theirStrike.Count;

            // Enemy supply our unit cuts, only where it could matter: in or beside their supply.
            if (prof.spaceSupplyValue > 0f && moving && map.DistanceToTheirSupply(uAt) <= 1) {
                Unit was = uAt.occupier;
                from.occupier = null; uAt.occupier = u;
                try {
                    int after = 0;
                    foreach (Player o in map.enemyPlayers) after += SupplyNetwork.SolveMoved(o, new Dictionary<SupplyPoint, HexTile>()).depth.Count;
                    w.supplyCut = Mathf.Max(0, map.theirSupply.depth.Count - after);
                } finally { uAt.occupier = was; from.occupier = u; }
            }
            w.space = (w.ourSpace - w.theirSpace) * prof.spaceTileValue + w.supplyCut * prof.spaceSupplyValue;
            w.score += w.space;
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

        /// <summary>
        /// Could <paramref name="x"/> standing on <paramref name="xAt"/> strike <paramref name="d"/> on
        /// <paramref name="dAt"/> next turn (hex reach: move + range), and for how much each way.
        /// The firing tile is approximated by the defender's neighbour nearest the attacker.
        /// </summary>
        static bool InReach(Unit x, HexTile xAt, HexTile dAt, out int dmg, out int back, int xHp, Unit d, int dHp) {
            dmg = 0; back = 0;
            int dist = xAt.Distance(dAt);
            int reach = (x.moveAndAttack ? x.moveRange : 0) + x.AttackRange;
            if (dist > reach || (!x.moveAndAttack && dist <= x.MinRange)) return false;
            HexTile fire = xAt;
            if (x.moveAndAttack && dist > x.AttackRange) {
                fire = null;
                foreach (HexTile n in dAt.neighbors) if (n != null && (fire == null || n.Distance(xAt) < fire.Distance(xAt))) fire = n;
                if (x.AttackRange > 1 || fire == null) fire = xAt;   // ranged: shot from range, no retaliation
            }
            CombatMath.Exchange(x, xHp, fire, d, dHp, dAt, out dmg, out back);
            return dmg > 0;
        }

        /// <summary>Simulated strength as the whole number the game's damage rules use.</summary>
        static int Hp(float h) { return h <= 0f ? 0 : Mathf.Max(1, Mathf.RoundToInt(h)); }

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
