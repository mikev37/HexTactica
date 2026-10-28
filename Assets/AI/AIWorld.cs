using System;
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
            /// <summary>Death premiums: + for theirs destroyed, - for ours.</summary>
            public float deaths;
            /// <summary>Self-preservation: weighted exposure of the acting unit beyond what the reply took.</summary>
            public float selfRisk;
            /// <summary>Best next blows: ours (we act first), theirs after.</summary>
            public float ownStrike, enemyStrike;
            /// <summary>Strength value given back for damage that will heal (ours, theirs).</summary>
            public float healOwn, healEnemy;
            /// <summary>Space: move+strike tiles (ours, theirs), enemy supply tiles cut, and the gold it adds.</summary>
            public int ourSpace, theirSpace, supplyCut, enemyUnsupplied;
            /// <summary>Artillery only: enemy-reachable tiles under this gun next turn.</summary>
            public int gunCover;
            /// <summary>Tiles next to our units the enemy could step onto: covered by another of ours, or not.</summary>
            public int covered, exposed;
            /// <summary>Enemy units our action puts out of supply (supplyForecastValue).</summary>
            public int forcedOut;
            public float space;
            /// <summary>Buildings: what our capture this turn is worth, less what the enemy's infantry could take from ours next turn.</summary>
            public float buildings;
            public string buildingNote;
            public readonly List<string> replies = new List<string>();
            /// <summary>Strength each of our units loses in the reply.</summary>
            public readonly Dictionary<Unit, float> lost = new Dictionary<Unit, float>();
            public string Summary() {
                StringBuilder sb = new StringBuilder();
                sb.Append(score.ToString("0"));
                if (ourHQFalls) sb.Append(" OUR HQ FALLS");
                if (assist) sb.Append(" assist");
                if (deaths != 0f) sb.Append(" deaths ").Append(deaths.ToString("+0;-0"));
                if (selfRisk != 0f) sb.Append(" self -").Append(selfRisk.ToString("0"));
                if (hubs != 0f) sb.Append(" hubs ").Append(hubs.ToString("+0;-0"));
                if (space != 0f) sb.Append(" space ").Append(space.ToString("+0;-0")).Append(" (").Append(ourSpace).Append('/').Append(theirSpace).Append(supplyCut > 0 ? " cut " + supplyCut : "").Append(enemyUnsupplied > 0 ? " unsupplied " + enemyUnsupplied : "").Append(')');
                if (gunCover > 0) sb.Append(" covers ").Append(gunCover);
                if (forcedOut > 0) sb.Append(" forced out of supply ").Append(forcedOut);
                if (buildings != 0f) sb.Append(" buildings ").Append(buildings.ToString("+0;-0")).Append(buildingNote != null ? " (" + buildingNote + ")" : "");
                if (covered > 0 || exposed > 0) sb.Append(" mutual ").Append(covered).Append('/').Append(exposed);
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

        /// <summary>Units that get no assist credit this turn (AICoordinator: no one has committed to finish their target).</summary>
        public static HashSet<Unit> NoAssist;

        public static Outcome Evaluate(Unit u, HexTile stop, AttackAppraisal shot, AIMap map, Cache cache, bool narrate) {
            return Evaluate(u, stop, shot, map, cache, narrate, false);
        }

        /// <summary>The enemy unit our shot is forecast to drive back, while Evaluate runs (forecastKnockback).</summary>
        static Unit knocked;
        static HexTile knockedFrom;

        /// <param name="space">Also score space (AIProfile.spaceTileValue / spaceSupplyValue): used when choosing a move.</param>
        public static Outcome Evaluate(Unit u, HexTile stop, AttackAppraisal shot, AIMap map, Cache cache, bool narrate, bool space) {
            // Knockback: a target that survives our shot and falls back is forecast from its retreat
            // tile — the reply it fires from there, the tile it frees (for our reach and cover), the
            // supply and space around where it ends up. Done by moving it on the board for the
            // duration of the forecast.
            Unit t = shot.valid ? shot.target : null;
            HexTile rt = null, was = null;
            if (map.profile.forecastKnockback && t != null && t.tile != null && t.moveRange > 0 && knocked == null
                && shot.outcome.defenderRetreats && !shot.outcome.defenderPinned && !shot.outcome.defenderDies
                && t.Strength - shot.outcome.damageToDefender > 0) {
                Dictionary<Unit, float> alive = new Dictionary<Unit, float>();
                foreach (Unit x in map.ownUnits) if (x != null && x.tile != null) alive[x] = x.Strength;
                foreach (Unit x in map.enemyUnits) if (x != null && x.tile != null) alive[x] = x.Strength;
                HashSet<HexTile> taken = new HashSet<HexTile>();
                if (stop != null) taken.Add(stop);
                rt = AIPlan.RetreatTile(t, t.tile, stop ?? u.tile, alive, taken);
                if (rt != null && rt.occupier == null) {
                    // Our units' reach is cached per board read: fill it before the board changes.
                    foreach (Unit x in map.ownUnits) if (x != null && x.tile != null && x.active && x.moveAndAttack && x.Firepower > 0) map.ReachThisTurn(x);
                    was = t.tile;
                    was.occupier = null; rt.occupier = t; t.tile = rt;
                    knocked = t; knockedFrom = was;
                } else rt = null;
            }
            HashSet<Unit> wasTheater = Theater;
            Theater = map.TheaterOf(u);
            try {
                long __p = AIPerf.Begin();
                Outcome w = EvaluateOnBoard(u, stop, shot, map, cache, narrate, space);
                AIPerf.End("forecast: AIWorld.Evaluate", __p);
                if (rt != null && narrate) w.replies.Insert(0, t.name + " driven back to (" + rt.x + "," + rt.y + ")");
                return w;
            } finally {
                Theater = wasTheater;
                if (rt != null) { t.tile = was; rt.occupier = null; was.occupier = t; knocked = null; knockedFrom = null; }
            }
        }

        /// <summary>
        /// The units the forecast plays out (profile.theaterForecast; null: every unit). Units outside
        /// stand and score as they are, the same for every option of the unit deciding.
        /// </summary>
        static HashSet<Unit> Theater;
        static bool In(Unit x) { return Theater == null || Theater.Contains(x); }

        static Outcome EvaluateOnBoard(Unit u, HexTile stop, AttackAppraisal shot, AIMap map, Cache cache, bool narrate, bool space) {
            Outcome w = new Outcome();
            Dictionary<Unit, float> hp = new Dictionary<Unit, float>();
            foreach (Unit x in map.ownUnits) if (x != null && x.tile != null) hp[x] = x.Strength;
            foreach (Unit x in map.enemyUnits) if (x != null && x.tile != null) hp[x] = x.Strength;

            HexTile from = u.tile;
            HexTile uAt = stop ?? from;
            bool moving = stop != null && stop != from && from.occupier == u && (stop.occupier == null || stop.occupier == u);
            Unit stopWas = stop != null ? stop.occupier : null;

            // Our action.
            if (shot.valid && shot.target != null && hp.ContainsKey(shot.target)) {
                hp[shot.target] = Mathf.Max(0f, hp[shot.target] - shot.outcome.damageToDefender);
                hp[u] = Mathf.Max(0f, hp[u] - shot.outcome.damageToAttacker);
                // An assist: the target survives, but units of ours still to act this turn have a
                // real attack on it (a free firing tile they can reach this turn) that finishes it.
                // Counted as assistWeight of a kill — its reply and its material go in that
                // proportion — so a hit that sets up a kill beats a hit that does not.
                if (hp[shot.target] > 0f && map.profile.assistWeight > 0f && (NoAssist == null || !NoAssist.Contains(u))) {
                    string by;
                    bool can = map.profile.assistNeedsReach ? CanFinish(shot.target, hp[shot.target], u, uAt, map, out by)
                                                            : CanFinishByDistance(shot.target, hp[shot.target], u, map, out by);
                    if (can) {
                        w.assist = true;
                        hp[shot.target] *= 1f - map.profile.assistWeight;
                        if (narrate) w.replies.Add("assist: " + by + " can finish " + shot.target.name);
                    }
                }
            }

            // Their reply, strongest first, on the board with our unit moved.
            //   - Their own units never block them: they pass through each other (the game's rule)
            //     and may finish on a tile another of theirs is standing on, since it can move off.
            //   - A unit of ours destroyed during the reply stops blocking at once: reachability is
            //     re-run around it for every enemy still to act. (A grunt on the one bridge held it
            //     in the forecast after the artillery had killed it, so the tanks behind never
            //     appeared, and our artillery beyond the bridge looked safe.)
            //   - A defender driven back with nowhere to go takes the extra point (pinned), and an
            //     HQ cannot move, so it is always pinned.
            List<Unit> enemies = new List<Unit>(map.enemyUnits);
            if (Theater != null) enemies.RemoveAll(e => !Theater.Contains(e));
            enemies.Sort((a, b) => {
                int c = (b.Firepower * b.Strength).CompareTo(a.Firepower * a.Strength);
                return c != 0 ? c : AIMap.CompareUnits(a, b);
            });
            HashSet<Unit> gone = new HashSet<Unit>();
            foreach (KeyValuePair<Unit, float> kv in hp) if (kv.Value <= 0f && kv.Key.owner == u.owner) gone.Add(kv.Key);

            long __t = AIPerf.Begin();
            Dictionary<Unit, HashSet<HexTile>> positions = new Dictionary<Unit, HashSet<HexTile>>();
            foreach (Unit e in enemies) {
                if (e == null || e.tile == null || !hp.ContainsKey(e) || hp[e] <= 0f) continue;
                if (e.Firepower <= 0 || !e.CanAttack(e.tile)) continue;
                bool near = e.moveAndAttack && (e.tile.Distance(uAt) <= e.moveRange + 1 || e.tile.Distance(from) <= e.moveRange + 1 || NearAny(e, gone, uAt));
                HashSet<HexTile> p;
                if (e == knocked) near = true;   // it stands somewhere else in this forecast
                if (!near && cache.positions.TryGetValue(e, out p)) { positions[e] = p; continue; }
                HashSet<Unit> goneHere = map.profile.forecastDeadBlock ? NoneGone : gone;
                // Near, but nothing this option changes on the board (the tile our unit leaves, the
                // one it takes, units it kills) lies in or beside where the enemy could go: that is
                // exactly where it could go on the board as it stands, worked out once per board.
                if (knocked == null) {
                    HashSet<HexTile> b = map.BaseReach(e, map.profile.forecastOwnSideBlocks);
                    if (Unaffected(b, e, moving ? from : null, moving ? uAt : null, goneHere, u, uAt)) { positions[e] = b; if (!near) cache.positions[e] = b; continue; }
                }
                p = PositionsOnBoard(e, u, from, uAt, moving, stopWas, goneHere, ownSideBlocks: map.profile.forecastOwnSideBlocks);
                positions[e] = p;
                if (!near) cache.positions[e] = p;
            }

            AIPerf.End("forecast: enemy positions", __t); __t = AIPerf.Begin();
            // Self-preservation: what could land on the acting unit itself if the enemy chose to hit
            // it, measured before the reply is played out. Every enemy blow that can reach its tile
            // (real movement, a firing tile it can get to), biggest first, summed at falling weights
            // 1, 1/2, 1/4 … — so standing where many guns reach is worse than where one does, even
            // when the greedy reply would send them all elsewhere. Kills and assists this turn have
            // already removed or weakened the enemies in hp.
            float selfExposure = 0f;
            AIProfile sp0 = map.profile;
            if (sp0.selfPreservationWeight > 0f && hp[u] > 0f && GameController.Targetable(u)) {
                List<float> blows = new List<float>();
                foreach (KeyValuePair<Unit, HashSet<HexTile>> kv in positions) {
                    Unit e = kv.Key;
                    if (hp[e] <= 0f) continue;
                    HexTile fire = FiringTile(e, uAt, kv.Value, new HashSet<HexTile>());
                    if (fire == null) continue;
                    int dmg, back;
                    CombatMath.Exchange(e, Hp(hp[e]), fire, u, Hp(hp[u]), uAt, out dmg, out back);
                    if (dmg > 0) blows.Add(dmg);
                }
                blows.Sort((x, y) => y.CompareTo(x));
                float sum = 0f, wgt = 1f;
                foreach (float b in blows) { sum += b * wgt; wgt *= 0.5f; }
                // Measured from the strength it starts the turn with, retaliation for its own shot
                // included: otherwise taking damage in a bad attack shrank what it had left to lose,
                // and a grunt trading 1 for 3 with a tank looked safer for it.
                float start = u.Strength;
                float total = (start - hp[u]) + sum;
                selfExposure = Mathf.Min(total, start) * Value(u) + (total >= start ? sp0.deathPremium * Value(u) : 0f);
            }
            float hpBeforeReply = u.Strength;
            AIPerf.End("forecast: self exposure", __t); __t = AIPerf.Begin();

            Dictionary<Unit, HexTile> endsAt = new Dictionary<Unit, HexTile>();
            HashSet<HexTile> taken = new HashSet<HexTile>();
            foreach (Unit x in map.ownUnits) if (x != null && x.tile != null && x != u && hp.ContainsKey(x) && hp[x] > 0f) taken.Add(x.tile);
            if (hp[u] > 0f) taken.Add(uAt);
            // Only units that fire from where they stand (artillery able to fire this turn) or
            // cannot move at all keep their tiles. Everything else on their side — supply units,
            // units with nothing to shoot — can step aside for a unit that wants the tile.
            bool ownSideBlocks = map.profile.forecastOwnSideBlocks;
            foreach (Unit e in enemies) {
                if (e == null || e.tile == null) continue;
                bool firesInPlace = !e.moveAndAttack && e.Firepower > 0 && positions.ContainsKey(e);
                if (ownSideBlocks || firesInPlace || e.moveRange <= 0) taken.Add(e.tile);
            }

            bool weighs = map.profile.enemyWeighsRisk;
            bool breakpoints = map.profile.replyBreakpoints;
            // With replyBreakpoints, the reply is played at full damage (kills where they really
            // happen) and the discount is applied afterwards to what it cost us; unanswered ranged
            // fire (artillery) is certain and not discounted, so it is also tracked on its own.
            Dictionary<Unit, float> hpCertain = breakpoints ? new Dictionary<Unit, float>(hp) : null;
            // Where each of ours stands as the reply plays out (forecastRetreatChain moves those driven back).
            Dictionary<Unit, HexTile> displaced = new Dictionary<Unit, HexTile>();
            // Our pips their indirect fire suppresses as the reply plays out: they do not fire back at
            // the blows that follow. (Everything of ours starts their turn unsuppressed: suppression
            // only ever comes from their fire, and ours came back at the start of our turn.)
            Dictionary<Unit, int> sup = new Dictionary<Unit, int>();
            Func<Unit, int> supOf = x => { int v; return sup.TryGetValue(x, out v) ? v : 0; };
            string chainNote = null;
            Func<Unit, HexTile> ourPos = x => { HexTile t; return displaced.TryGetValue(x, out t) ? t : x == u ? uAt : x.tile; };
            for (int ei = 0; ei < enemies.Count; ei++) {
                Unit e = enemies[ei];
                HashSet<HexTile> pos;
                if (!positions.TryGetValue(e, out pos) || hp[e] <= 0f) continue;
                Unit bestTarget = null; HexTile bestTile = null; float bestValue = 0f; int bestDamage = 0, bestBack = 0;
                bool bestPinned = false;
                float home = weighs ? Exposure(e, hp[e], e.tile, map, hp, ourPos) : 0f;
                foreach (Unit d in map.ownUnits) {
                    if (d == null || d.tile == null || !hp.ContainsKey(d) || hp[d] <= 0f || !GameController.Targetable(d) || (d != u && !In(d))) continue;
                    HexTile dAt = ourPos(d);
                    HexTile fire = FiringTile(e, dAt, pos, taken);
                    if (fire == null) continue;
                    // The game's own exchange: cover on the defender's tile, retaliation without
                    // cover, an ambush when the defender's usable concealment allows it.
                    int dmg, back;
                    CombatMath.Exchange(e, Hp(hp[e]), fire, d, Hp(hp[d]), dAt, supOf(d), out dmg, out back);
                    if (dmg <= 0) continue;
                    bool pinned = false;
                    // (Indirect fire never pins: it suppresses.)
                    if (map.profile.forecastPins && !CombatMath.Suppresses(e) && dmg < hp[d] && dmg - d.Veterancy > back - e.Veterancy && !CanFallBack(d, dAt, fire, u, uAt, hp, taken)) {
                        dmg += 1;
                        pinned = true;
                    }
                    float value = (dmg * Value(d) - back * Value(e)) * (dmg >= hp[d] ? 2f : 1f);
                    // Focus fire: the enemy counts assists as we do. A hit that leaves the target for
                    // its units still to act to finish is worth assistWeight of the kill.
                    if (breakpoints && dmg < hp[d] && map.profile.assistWeight > 0f
                        && EnemyCanFinish(d, dAt, hp[d] - dmg, enemies, ei, positions, taken, fire, hp)) {
                        float aw = map.profile.assistWeight;
                        value = ((dmg + aw * (hp[d] - dmg)) * Value(d) - back * Value(e)) * (1f + aw);
                    }
                    // Would it rather? The attack must be worth more than the extra our next turn
                    // could cost it on the tile it fires from (the counterattack it invites).
                    if (weighs) value -= Mathf.Max(0f, Exposure(e, hp[e] - back, fire, map, hp, ourPos) - home) * map.profile.enemyCaution;
                    if (value > bestValue || (value == bestValue && bestTarget != null && AIMap.CompareUnits(d, bestTarget) < 0)) {
                        bestValue = value; bestTarget = d; bestTile = fire; bestDamage = dmg; bestBack = back; bestPinned = pinned;
                    }
                }
                if (bestTarget == null) continue;
                if (ownSideBlocks && bestTile != e.tile) taken.Remove(e.tile);
                taken.Add(bestTile);
                endsAt[e] = bestTile;
                float dealt = Mathf.Min(hp[bestTarget], bestDamage * (breakpoints ? 1f : 1f - map.profile.replyDiscount));
                bool certain = false;
                if (breakpoints) {
                    HexTile tAt = ourPos(bestTarget);
                    certain = bestBack == 0 && bestTile.Distance(tAt) > 1;
                    if (certain) {
                        hpCertain[bestTarget] = Mathf.Max(0f, hpCertain[bestTarget] - dealt);
                    }
                }
                bool pushed = CombatMath.Pushes(e, bestBack, bestTarget, bestDamage, hp[bestTarget]);
                hp[bestTarget] -= dealt;
                // Suppressed by indirect fire: half its unsuppressed pips do not answer the blows to come.
                if (pushed && CombatMath.Suppresses(e) && hp[bestTarget] > 0f) {
                    sup[bestTarget] = supOf(bestTarget) + CombatMath.SuppressionFrom(Hp(hp[bestTarget]), supOf(bestTarget));
                    if (narrate) chainNote = bestTarget.name + " suppressed " + sup[bestTarget];
                }
                // Driven back: for the blows still to come it stands on its retreat tile, and the tile
                // it left is open to anyone who could step into it. (A tank knocked back by artillery,
                // followed into the tile it left by a tank that knocks it back again, then finished.)
                if (map.profile.forecastRetreatChain && !CombatMath.Suppresses(e) && hp[bestTarget] > 0f && !bestPinned && bestDamage - bestTarget.Veterancy > bestBack - e.Veterancy) {
                    HexTile left = ourPos(bestTarget);
                    HexTile back = AIPlan.RetreatTile(bestTarget, left, bestTile, hp, taken);
                    if (back != null) {
                        displaced[bestTarget] = back;
                        taken.Remove(left); taken.Add(back);
                        foreach (Unit later in enemies) {
                            HashSet<HexTile> lp;
                            if (later == e || !later.moveAndAttack || hp.ContainsKey(later) && hp[later] <= 0f || !positions.TryGetValue(later, out lp) || lp.Contains(left)) continue;
                            foreach (HexTile n in left.neighbors) if (n != null && lp.Contains(n) && later.CanEnter(left)) { lp = new HashSet<HexTile>(lp); lp.Add(left); positions[later] = lp; break; }
                        }
                        chainNote = bestTarget.name + " driven back to (" + back.x + "," + back.y + ")";
                    }
                }
                float sofar;
                w.lost[bestTarget] = (w.lost.TryGetValue(bestTarget, out sofar) ? sofar : 0f) + dealt;
                hp[e] = Mathf.Max(0f, hp[e] - bestBack);
                if (narrate) w.replies.Add(e.name + "->" + bestTarget.name + " " + dealt.ToString("0.#") + (bestPinned ? " pinned" : "") + (bestBack > 0 ? "/" + bestBack : "")
                                           + (certain ? " certain" : "") + (breakpoints && hp[bestTarget] <= 0f ? " kill" : ""));
                if (narrate && chainNote != null) w.replies.Add(chainNote);
                chainNote = null;
                if (hp[bestTarget] <= 0f && !map.profile.forecastDeadBlock) {
                    // Destroyed: it no longer holds its tile or blocks anyone still to act.
                    HexTile freed = ourPos(bestTarget);
                    taken.Remove(freed);
                    gone.Add(bestTarget);
                    foreach (Unit later in enemies) {
                        if (later == e || !positions.ContainsKey(later) || hp[later] <= 0f || !later.moveAndAttack) continue;
                        if (later.tile.Distance(freed) > later.moveRange + 1) continue;
                        positions[later] = PositionsOnBoard(later, u, from, uAt, moving, stopWas, gone, ownSideBlocks: map.profile.forecastOwnSideBlocks);
                    }
                }
            }

            AIPerf.End("forecast: reply", __t); __t = AIPerf.Begin();
            // The self-preservation term, net of what the reply already took from this unit.
            if (selfExposure > 0f) {
                float already = (hpBeforeReply - Mathf.Max(0f, hp[u])) * Value(u) + (hp[u] <= 0f ? sp0.deathPremium * Value(u) : 0f);
                w.selfRisk = Mathf.Max(0f, selfExposure - already) * sp0.selfPreservationWeight;
            }

            // What is left. With replyBreakpoints: valued at full damage and with only the certain
            // fire, and the uncertain part of the difference discounted.
            AIPerf.End("forecast: self risk", __t); __t = AIPerf.Begin();
            float valued = Valuate(hp, u, uAt, endsAt, map, w);
            if (breakpoints) {
                Outcome c = new Outcome();
                float vc = Valuate(hpCertain, u, uAt, endsAt, map, c);
                float k = 1f - map.profile.replyDiscount;
                valued = vc + k * (valued - vc);
                w.own = c.own + k * (w.own - c.own);
                w.enemy = c.enemy + k * (w.enemy - c.enemy);
                w.deaths = c.deaths + k * (w.deaths - c.deaths);
                w.hubs = c.hubs + k * (w.hubs - c.hubs);
                w.healOwn = c.healOwn + k * (w.healOwn - c.healOwn);
                w.healEnemy = c.healEnemy + k * (w.healEnemy - c.healEnemy);
            }
            w.score = valued - w.selfRisk;
            AIProfile prof = map.profile;

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
            AIPerf.End("forecast: valuate", __t); __t = AIPerf.Begin();
            Dictionary<Unit, float> ourFire = new Dictionary<Unit, float>();
            if (prof.ownStrikeWeight > 0f || prof.enemyStrikeWeight > 0f) {
                foreach (Unit x in hp.Keys) {
                    if (hp[x] <= 0f || x.Firepower <= 0 || IsHQ(x) || x.owner != u.owner || (x != u && !In(x))) continue;
                    HexTile xAt = ourPos(x);
                    float best = 0f;
                    Unit bestD = null; int bestDmg = 0;
                    foreach (Unit d in hp.Keys) {
                        if (hp[d] <= 0f || d.owner == u.owner || IsHQ(d) || !In(d)) continue;
                        HexTile dAt = endsAt.ContainsKey(d) ? endsAt[d] : d.tile;
                        int dmg, back;
                        if (!InReach(x, xAt, dAt, out dmg, out back, Hp(hp[x]), d, Hp(hp[d]))) continue;
                        float v = dmg * Value(d) - back * Value(x) + (dmg >= hp[d] ? prof.deathPremium * Value(d) : 0f);
                        if (bestD == null || v > best) { best = v; bestD = d; bestDmg = dmg; }
                    }
                    // Each of our units fires once: it counts against the one enemy it would pick.
                    // (Summing every unit's reach on every enemy declared most enemies dead before
                    // they could fire, and hid the threat to our HQ.)
                    if (bestD != null) { float f; ourFire[bestD] = (ourFire.TryGetValue(bestD, out f) ? f : 0f) + bestDmg; }
                    best = Mathf.Max(0f, best);
                    w.ownStrike += best * prof.ownStrikeWeight;
                }
                if (prof.enemyStrikeWeight > 0f) {
                    foreach (Unit x in hp.Keys) {
                        if (hp[x] <= 0f || x.Firepower <= 0 || IsHQ(x) || x.owner == u.owner || !In(x)) continue;
                        HexTile xAt = endsAt.ContainsKey(x) ? endsAt[x] : x.tile;
                        float survives = 1f;
                        float ours;
                        if (ourFire.TryGetValue(x, out ours)) survives = Mathf.Clamp01(1f - ours / hp[x]);
                        if (survives <= 0f) continue;
                        float best = 0f;
                        foreach (Unit d in hp.Keys) {
                            if (hp[d] <= 0f || d.owner != u.owner || IsHQ(d) || (d != u && !In(d))) continue;
                            HexTile dAt = ourPos(d);
                            int dmg, back;
                            if (!InReach(x, xAt, dAt, out dmg, out back, Hp(hp[x]), d, Hp(hp[d]))) continue;
                            best = Mathf.Max(best, dmg * Value(d) - back * Value(x) + (dmg >= hp[d] ? prof.deathPremium * Value(d) : 0f));
                        }
                        w.enemyStrike += best * survives * prof.enemyStrikeWeight;
                    }
                }
                w.score += w.ownStrike - w.enemyStrike;
            }

            AIPerf.End("forecast: next-turn strikes", __t); __t = AIPerf.Begin();
            if (space && (prof.spaceTileValue > 0f || prof.spaceSupplyValue > 0f || prof.enemyUnsuppliedValue > 0f || prof.artilleryCoverValue > 0f || prof.coverTileValue > 0f || prof.supplyForecastValue > 0f)) Space(w, u, uAt, from, moving, map, positions, hp);
            AIPerf.End("forecast: space", __t); __t = AIPerf.Begin();
            if (prof.forecastBuildings && Building.all.Count > 0) Buildings(w, u, uAt, from, shot, map, positions, hp, endsAt, narrate);
            AIPerf.End("forecast: buildings", __t);
            return w;
        }

        /// <summary>What is left on <paramref name="hp"/>: material both sides (heal-back counted), death
        /// premiums, HQ stakes and supply-unit dependents. Fills <paramref name="w"/>'s parts; returns the sum.</summary>
        static float Valuate(Dictionary<Unit, float> hp, Unit u, HexTile uAt, Dictionary<Unit, HexTile> endsAt, AIMap map, Outcome w) {
            return Valuate(hp, u.owner, x => x == u ? uAt : (endsAt.ContainsKey(x) ? endsAt[x] : x.tile), map, w);
        }

        /// <param name="where">Where each unit ends up (for healing).</param>
        internal static float Valuate(Dictionary<Unit, float> hp, Player us, Func<Unit, HexTile> where, AIMap map, Outcome w) {
            float stake = map.BoardStake;
            w.own = 0f; w.enemy = 0f; w.deaths = 0f; w.hubs = 0f; w.healOwn = 0f; w.healEnemy = 0f;
            foreach (KeyValuePair<Unit, float> kv in hp) {
                float left = Mathf.Max(0f, kv.Value);
                // The strength a supplied unit heals back by its next turn (one, by the game's rule)
                // doesn't count; kills, damage beyond that, and damage to units that won't heal
                // (out of supply, or a cut-off supply unit) count in full. (It was half of all the
                // damage, which made a 5-point hit on our HQ look like 2.5 when only 1 comes back.)
                if (left >= map.profile.healMinStrength && left < kv.Key.Strength) {
                    HexTile at = where(kv.Key);
                    float heal = map.HealAt(kv.Key, at);
                    if (heal > 0f) {
                        // As the game heals it there: a city or base that heals more counts more (Building.healPerTurn).
                        float healed = Mathf.Min(kv.Key.Strength - left, heal);
                        left += healed;
                        if (kv.Key.owner == us) w.healOwn += healed * Value(kv.Key); else w.healEnemy += healed * Value(kv.Key);
                    }
                }
                float v = left * Value(kv.Key);
                bool hq = IsHQ(kv.Key);
                // A unit destroyed is worth more than its last strength point: alive, it heals
                // back, holds ground and fires again. deathPremium more points of its worth.
                if (kv.Value <= 0f && !hq) {
                    float extra = map.profile.deathPremium * Value(kv.Key);
                    if (kv.Key.owner == us) w.deaths -= extra; else w.deaths += extra;
                }
                if (kv.Key.owner == us) {
                    w.own += v;
                    if (hq && kv.Value <= 0f) w.ourHQFalls = true;
                } else {
                    w.enemy += v;
                    if (hq && kv.Value <= 0f) w.theirHQFalls = true;
                }
            }
            float score = w.own - w.enemy + w.deaths;
            if (w.ourHQFalls) score -= stake;
            if (w.theirHQFalls) score += stake;

            // A supply unit's death also costs a strength of everyone only it kept supplied.
            AIProfile prof = map.profile;
            if (prof.hubDependentsWeight > 0f) {
                foreach (KeyValuePair<Unit, float> kv in hp) {
                    if (kv.Value > 0f || IsHQ(kv.Key) || kv.Key.Hub == null) continue;
                    // With supplyForecastValue the enemy's loss is the supply forecast's (Space).
                    if (kv.Key.owner != us && prof.supplyForecastValue > 0f) continue;
                    float dep = map.HubStake(kv.Key) * prof.hubDependentsWeight;
                    if (kv.Key.owner == us) w.hubs -= dep; else w.hubs += dep;
                }
                score += w.hubs;
            }
            return score;
        }

        /// <summary>
        /// Can the enemies after index <paramref name="ei"/> (those still to act in the reply) finish
        /// <paramref name="d"/> on <paramref name="dAt"/> at <paramref name="left"/>? Each from a free firing
        /// tile it can reach (not <paramref name="fire"/>, which the first attacker takes); strongest first.
        /// </summary>
        internal static bool EnemyCanFinish(Unit d, HexTile dAt, float left, List<Unit> enemies, int ei,
                                   Dictionary<Unit, HashSet<HexTile>> positions, HashSet<HexTile> taken, HexTile fire, Dictionary<Unit, float> hp) {
            HashSet<HexTile> claimed = new HashSet<HexTile>(taken);
            claimed.Add(fire);
            float sum = 0f;
            for (int j = ei + 1; j < enemies.Count; j++) {
                Unit x = enemies[j];
                HashSet<HexTile> pos;
                if (x == null || !positions.TryGetValue(x, out pos) || !hp.ContainsKey(x) || hp[x] <= 0f) continue;
                HexTile f = FiringTile(x, dAt, pos, claimed);
                if (f == null) continue;
                int dmg, back;
                CombatMath.Exchange(x, Hp(hp[x]), f, d, Hp(left), dAt, out dmg, out back);
                if (dmg <= 0) continue;
                if (f != x.tile) claimed.Add(f);
                sum += dmg;
                if (sum >= left) return true;
            }
            return false;
        }

        /// <summary>A building's worth to whoever holds it, in gold (AIProfile building values; an HQ that can change hands: the game).</summary>
        internal static float BuildingWorth(Building b, AIMap map) {
            if (b == null || !b.Capturable) return 0f;
            if (b.IsHQ) return map.BoardStake * Mathf.Max(0f, map.profile.hqCaptureWeight);
            AIProfile p = map.profile;
            // A destroyed building earns, supplies and builds nothing (until it is repaired).
            if (!b.Functioning) return p.buildingValue;
            return p.buildingValue + b.income * p.buildingIncomeTurns + (b.producesSupply ? p.buildingSupplyValue : 0f) + (b.CanProduce ? p.buildingProductionValue : 0f);
        }

        /// <summary>
        /// Control of a building for <paramref name="side"/>, -1..1: 1 held by it, -1 fully held by
        /// another (-0.5 at its last point), neutral in between by whose progress it carries.
        /// <paramref name="signed"/> is points: + for <paramref name="side"/>, - for anyone else.
        /// </summary>
        static float Control(Building b, Player side, Player owner, int signed) {
            float th = b.CaptureThreshold;
            // Held: its points count too (an HQ worn down to 4 of 10 by enemy infantry is nearer lost than one at 10).
            if (owner == side) return 0.5f + 0.5f * Mathf.Clamp01(Mathf.Abs(signed) / th);
            if (owner != null) return -(0.5f + 0.5f * Mathf.Abs(signed) / th);
            return 0.5f * signed / th;
        }

        /// <summary>
        /// Buildings, in gold:
        ///   - Our capture this turn: infantry standing on a building at the end of our turn without
        ///     attacking captures it, adding its strength (Building.ForecastPoints, the game's rule).
        ///     For the building the acting unit ends on (and the one it leaves): its worth times the
        ///     change in our control, with every other unit of ours where it stands.
        ///   - Their capture next turn: on each building we hold (or are taking), the infantry the
        ///     enemy could get onto a free tile of it after the reply — those that did not attack in
        ///     it — and the control that would cost us. Standing on our own building blocks a tile
        ///     and contests it (they can then only bring it down to 0).
        /// </summary>
        static void Buildings(Outcome w, Unit u, HexTile uAt, HexTile from, AttackAppraisal shot, AIMap map,
                              Dictionary<Unit, HashSet<HexTile>> positions, Dictionary<Unit, float> hp, Dictionary<Unit, HexTile> endsAt, bool narrate) {
            Player us = u.owner;
            Func<Unit, HexTile> ourAt = x => x == u ? uAt : x.tile;
            float total = 0f;
            StringBuilder note = narrate ? new StringBuilder() : null;
            int radius = Theater != null ? map.TheaterRadius(u) : int.MaxValue;
            foreach (Building b in Building.all) {
                if (b == null || b.tile == null || !b.Capturable) continue;
                // Outside the theatre: nothing this unit does reaches it (the same for every option).
                if (radius != int.MaxValue && b.tile.Distance(from) > radius) continue;
                float worth = BuildingWorth(b, map);
                if (worth <= 0f) continue;
                List<HexTile> fp = b.Footprint;
                // Ours on it at the end of our turn (capturing) and theirs on it (contesting).
                int ourStrength = 0; bool ourInfantry = false, theirInfantry = false, touched = false;
                foreach (Unit x in map.ownUnits) {
                    if (x == null || x.tile == null || !hp.ContainsKey(x) || hp[x] <= 0f || !Building.CanCapture(x)) continue;
                    HexTile at = ourAt(x);
                    if (!fp.Contains(at)) { if (x == u && fp.Contains(from)) touched = true; continue; }
                    ourInfantry = true;
                    if (x == u) { touched = true; if (shot.valid) continue; }
                    else if (x.attackedThisTurn) continue;
                    ourStrength += Mathf.Max(1, Hp(hp[x]));
                }
                foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && fp.Contains(e.tile) && Building.CanCapture(e)) theirInfantry = true;
                int signedNow = b.owner == us || (b.owner == null && b.capturingPlayer == us) ? b.capturePoints : -b.capturePoints;
                float now = Control(b, us, b.owner, signedNow);
                Player ownerAfter = b.owner;
                int signedAfter = signedNow;
                if (touched) {
                    signedAfter = b.ForecastPoints(us, ourStrength, theirInfantry, out ownerAfter);
                    float gain = (Control(b, us, ownerAfter, signedAfter) - now) * worth;
                    total += gain;
                    if (note != null && gain != 0f) note.Append(b.name.Replace("(Clone)", "")).Append(" capture ").Append(gain.ToString("+0;-0")).Append(' ');
                }
                // Their infantry next turn, on ours (or on what we are taking).
                bool ours = ownerAfter == us || (ownerAfter == null && signedAfter > 0);
                if (!ours) continue;
                int threat = 0;
                Player enemy = null;
                HashSet<HexTile> claimed = new HashSet<HexTile>();
                foreach (Unit x in map.ownUnits) if (x != null && x.tile != null && hp.ContainsKey(x) && hp[x] > 0f) claimed.Add(ourAt(x));
                foreach (KeyValuePair<Unit, HashSet<HexTile>> kv in positions) {
                    Unit e = kv.Key;
                    if (!Building.CanCapture(e) || hp[e] <= 0f || endsAt.ContainsKey(e)) continue;
                    HexTile onto = null;
                    foreach (HexTile t in fp) if (!claimed.Contains(t) && (kv.Value.Contains(t) || e.tile == t)) { onto = t; break; }
                    if (onto == null) continue;
                    claimed.Add(onto);
                    threat += Mathf.Max(1, Hp(hp[e]));
                    enemy = e.owner;
                }
                if (threat <= 0 || enemy == null) continue;
                // Their capture, from where ours leaves it: signed for them is minus ours.
                Player ownerTheirs;
                Player keep = b.owner; int keepPts = b.capturePoints; Player keepCap = b.capturingPlayer;
                try {
                    b.owner = ownerAfter;
                    b.capturePoints = Mathf.Abs(signedAfter);
                    b.capturingPlayer = ownerAfter == null && signedAfter > 0 ? us : (ownerAfter == null ? null : b.capturingPlayer);
                    int theirs = b.ForecastPoints(enemy, threat, ourInfantry, out ownerTheirs);
                    float loss = (Control(b, us, ownerAfter, signedAfter) - Control(b, us, ownerTheirs, ownerTheirs == us || (ownerTheirs == null && theirs < 0) ? -theirs : -theirs)) * worth;
                    if (loss > 0f) {
                        total -= loss;
                        if (note != null) note.Append(b.name.Replace("(Clone)", "")).Append(" at risk ").Append((-loss).ToString("+0;-0")).Append(' ');
                    }
                } finally { b.owner = keep; b.capturePoints = keepPts; b.capturingPlayer = keepCap; }
            }
            // Our HQ (standard rules): enemy infantry within a move of it after their reply costs
            // hqThreatNear of the board per strength point, on it hqThreatOn per strength point.
            // (A Marine stood on our HQ worn down to 4 of 10, and nothing of ours near it took it on.)
            Building home = GameController.HQOf(us);
            if (home != null && home.tile != null && home.Capturable && (map.profile.hqThreatNear > 0f || map.profile.hqThreatOn > 0f)) {
                float threatCost = 0f;
                List<HexTile> hfp = home.Footprint;
                foreach (Unit e in map.enemyUnits) {
                    if (e == null || e.tile == null || !Building.CanCapture(e)) continue;
                    float ehp; if (!hp.TryGetValue(e, out ehp)) ehp = e.Strength;
                    if (ehp <= 0f) continue;
                    HexTile at = endsAt.ContainsKey(e) ? endsAt[e] : e.tile;
                    int pts = Mathf.Max(1, Hp(ehp));
                    if (hfp.Contains(at)) threatCost += pts * map.profile.hqThreatOn;
                    else if (at.Distance(home.tile) <= Mathf.Max(1, e.moveRange)) threatCost += pts * map.profile.hqThreatNear;
                }
                if (threatCost > 0f) {
                    float c = threatCost * map.BoardStake;
                    total -= c;
                    if (note != null) note.Append("HQ threat ").Append((-c).ToString("+0;-0")).Append(' ');
                }
            }
            w.buildings = total;
            if (note != null && note.Length > 0) w.buildingNote = note.ToString().Trim();
            w.score += total;
        }

        /// <summary>
        /// Space: tiles each side could move to and strike next turn (union over its units), and enemy
        /// supply cut. Ours with our unit on its new tile; theirs with their movement re-run around it
        /// (the same reachable positions the reply used).
        /// </summary>
        static readonly HashSet<HexTile> scratchOurMove = new HashSet<HexTile>(), scratchOurStrike = new HashSet<HexTile>(),
                                         scratchTheirMove = new HashSet<HexTile>(), scratchTheirStrike = new HashSet<HexTile>();
        static readonly Dictionary<Unit, HashSet<HexTile>> scratchFireOf = new Dictionary<Unit, HashSet<HexTile>>();

        static void Space(Outcome w, Unit u, HexTile uAt, HexTile from, bool moving, AIMap map,
                          Dictionary<Unit, HashSet<HexTile>> positions, Dictionary<Unit, float> hp) {
            AIProfile prof = map.profile;
            // Scratch sets, reused call to call (this runs for every option of every unit: fresh
            // sets each time were a good part of the AI's garbage).
            HashSet<HexTile> ourMove = scratchOurMove, ourStrike = scratchOurStrike;
            ourMove.Clear(); ourStrike.Clear();
            Dictionary<Unit, HashSet<HexTile>> fireOf = scratchFireOf;
            fireOf.Clear();
            foreach (Unit x in map.ownUnits) {
                if (x == null || x.tile == null || !hp.ContainsKey(x) || hp[x] <= 0f || x.trasnporter != null || (x != u && !In(x))) continue;
                // Near a unit driven back, reach is re-run on this board (the cached read is from before).
                bool fresh = knocked != null && (x.tile.Distance(knockedFrom) <= x.moveRange + x.AttackRange + 1 || x.tile.Distance(knocked.tile) <= x.moveRange + x.AttackRange + 1);
                HashSet<HexTile>[] s = x == u ? AIMap.SpaceFrom(u, uAt, null) : fresh ? AIMap.SpaceFrom(x, x.tile, null) : map.SpaceOf(x);
                ourMove.UnionWith(s[0]);
                ourStrike.UnionWith(s[1]);
                if (x.Firepower > 0 && x.Hub == null) fireOf[x] = s[1];
            }
            HashSet<HexTile> theirMove = scratchTheirMove, theirStrike = scratchTheirStrike;
            theirMove.Clear(); theirStrike.Clear();
            foreach (Unit e in map.enemyUnits) {
                if (e == null || e.tile == null || !hp.ContainsKey(e) || hp[e] <= 0f || e.trasnporter != null || !In(e)) continue;
                HashSet<HexTile> pos;
                positions.TryGetValue(e, out pos);
                HashSet<HexTile>[] s = map.EnemySpace(e, pos);
                theirMove.UnionWith(s[0]);
                theirStrike.UnionWith(s[1]);
            }
            // Artillery's zone of control: every tile the enemy could move to next turn that this
            // gun will cover from where it ends up. It cannot move and fire, so where it stands now
            // is where it shoots from next turn. Inside enemy guns' range is already priced by the
            // reply; outside it, this pulls the battery as close as it can get — no man's land, held.
            if (prof.artilleryCoverValue > 0f && !u.moveAndAttack && u.AttackRange >= 2 && u.Firepower > 0 && hp[u] > 0f) {
                foreach (HexTile t in AIPath.TilesInRangeShared(uAt, u.MinRange, u.AttackRange)) if (theirMove.Contains(t)) w.gunCover++;
                w.score += w.gunCover * prof.artilleryCoverValue;
            }
            // Mutual cover: every tile next to one of our units that the enemy could move onto next
            // turn (where it would stand to hit that unit) is covered if another of our units could
            // strike it next turn, and exposed if none could. Covered tiles count +coverTileValue,
            // exposed ones -coverTileValue: keep within reach of each other near the enemy.
            if (prof.coverTileValue > 0f) {
                foreach (Unit f in map.ownUnits) {
                    if (f == null || f.tile == null || !hp.ContainsKey(f) || hp[f] <= 0f || f.trasnporter != null || !GameController.Targetable(f)) continue;
                    HexTile fAt = f == u ? uAt : f.tile;
                    foreach (HexTile n in fAt.neighbors) {
                        if (n == null || !theirMove.Contains(n)) continue;
                        bool covered = false;
                        foreach (KeyValuePair<Unit, HashSet<HexTile>> kv in fireOf) if (kv.Key != f && kv.Value.Contains(n)) { covered = true; break; }
                        if (covered) w.covered++; else w.exposed++;
                    }
                }
                w.score += (w.covered - w.exposed) * prof.coverTileValue;
            }
            if (prof.spaceContestedOnly) {
                ourMove.RemoveWhere(t => map.EnemyStrikeTurns(t) > prof.enemyReachTurns);
                ourStrike.RemoveWhere(t => map.EnemyStrikeTurns(t) > prof.enemyReachTurns);
            }
            w.ourSpace = ourMove.Count + ourStrike.Count;
            w.theirSpace = theirMove.Count + theirStrike.Count;

            // Enemy supply with our unit on its new tile — re-solved only where it could change
            // anything: in or beside their supply. Supply is checked at the start of their turn,
            // before they move, so their units count where they stand now.
            SupplyNetwork.Result after = map.theirSupply;
            // supplyForecastValue: also re-solved without an enemy supply unit our shot destroys.
            Unit deadHub = null;
            if (prof.supplyForecastValue > 0f)
                foreach (Unit e in map.enemyUnits)
                    if (e != null && hp.ContainsKey(e) && hp[e] <= 0f && !IsHQ(e) && e.Hub != null) deadHub = e;
            bool near = moving && map.DistanceToTheirSupply(uAt) <= 1;
            if ((prof.spaceSupplyValue > 0f || prof.enemyUnsuppliedValue > 0f || prof.supplyForecastValue > 0f) && (near || deadHub != null)) {
                Unit was = uAt.occupier;
                if (moving) { from.occupier = null; uAt.occupier = u; }
                try {
                    after = new SupplyNetwork.Result();
                    foreach (Player o in map.enemyPlayers) {
                        SupplyNetwork.Result r = deadHub != null && deadHub.owner == o
                            ? SupplyNetwork.SolveWithout(o, deadHub.Hub)
                            : SupplyNetwork.SolveMoved(o, new Dictionary<SupplyPoint, HexTile>());
                        foreach (KeyValuePair<HexTile, int> kv in r.depth)
                            if (kv.Value > after.DepthAt(kv.Key)) after.depth[kv.Key] = kv.Value;
                    }
                    w.supplyCut = Mathf.Max(0, map.theirSupply.depth.Count - after.depth.Count);
                } finally { if (moving) { uAt.occupier = was; from.occupier = u; } }
                // Enemy units in supply now and out of it at the start of their turn (when supply
                // is checked, before they move): forced to leave the fight or wither.
                if (prof.supplyForecastValue > 0f) {
                    foreach (Unit e in map.enemyUnits) {
                        if (e == null || e.tile == null || e == deadHub || !hp.ContainsKey(e) || hp[e] <= 0f || IsHQ(e)) continue;
                        if (map.theirSupply.DepthAt(e.tile) > 0 && after.DepthAt(e.tile) <= 0) {
                            w.forcedOut++;
                            w.score += prof.supplyForecastValue * Value(e);
                        }
                    }
                }
            }
            foreach (Unit e in map.enemyUnits) {
                if (e == null || e.tile == null || !hp.ContainsKey(e) || hp[e] <= 0f || IsHQ(e)) continue;
                if (after.DepthAt(e.tile) <= 0) w.enemyUnsupplied++;
            }
            w.space = (w.ourSpace - w.theirSpace) * prof.spaceTileValue + w.supplyCut * prof.spaceSupplyValue
                    + w.enemyUnsupplied * prof.enemyUnsuppliedValue;
            w.score += w.space;
        }

        /// <summary>
        /// Can our units that have not acted yet this turn finish <paramref name="target"/> at
        /// <paramref name="left"/> strength? Hex reach (move + range), strongest first.
        /// </summary>
        public static bool CanFinish(Unit target, float left, Unit except, AIMap map, out string by) {
            return CanFinish(target, left, except, except != null ? except.tile : null, map, out by);
        }

        /// <summary>The old assist check: hex distance (move + range), no paths, no free firing tile.</summary>
        static bool CanFinishByDistance(Unit target, float left, Unit except, AIMap map, out string by) {
            by = "";
            List<float> blows = new List<float>();
            List<string> names = new List<string>();
            foreach (Unit x in map.ownUnits) {
                if (x == null || x == except || x.tile == null || !x.active || x.Firepower <= 0) continue;
                if (x.Hub != null || x.trasnporter != null) continue;
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

        /// <summary>
        /// Can our units still to act this turn finish <paramref name="target"/> at <paramref name="left"/>?
        /// Each needs a real attack: a firing tile it can reach this turn by its own movement, free
        /// (not <paramref name="exceptAt"/>, where the acting unit will stand, and not one another
        /// finisher has taken), or its own tile if it is already in range. Strongest blows first.
        /// </summary>
        public static bool CanFinish(Unit target, float left, Unit except, HexTile exceptAt, AIMap map, out string by) {
            by = "";
            if (target == null || target.tile == null) return false;
            List<KeyValuePair<Unit, List<HexTile>>> finishers = new List<KeyValuePair<Unit, List<HexTile>>>();
            Dictionary<Unit, float> blow = new Dictionary<Unit, float>();
            foreach (Unit x in map.ownUnits) {
                if (x == null || x == except || x.tile == null || !x.active || x.Firepower <= 0) continue;
                if (x.Hub != null || x.trasnporter != null || !x.CanAttack(x.tile)) continue;
                float dmg = Unit.calculateDamage(x, target, target.tile, 0);
                if (dmg <= 0f) continue;
                List<HexTile> options = new List<HexTile>();
                int d0 = x.tile.Distance(target.tile);
                if (d0 <= x.AttackRange && d0 > x.MinRange) options.Add(x.tile);
                if (x.moveAndAttack) {
                    MoveField f = map.ReachThisTurn(x);
                    List<HexTile> ring = AIPath.TilesInRangeSorted(target.tile, x.MinRange, x.AttackRange);
                    foreach (HexTile r in ring) {
                        if (r == exceptAt || r == x.tile) continue;
                        if (f.ReachableThisTurn(r) && AIPath.CanFinishTurnOn(x, r)) options.Add(r);
                    }
                }
                if (options.Count == 0) continue;
                blow[x] = dmg;
                finishers.Add(new KeyValuePair<Unit, List<HexTile>>(x, options));
            }
            finishers.Sort((p1, p2) => {
                int c = blow[p2.Key].CompareTo(blow[p1.Key]);
                return c != 0 ? c : AIMap.CompareUnits(p1.Key, p2.Key);
            });
            float sum = 0f;
            HashSet<HexTile> claimed = new HashSet<HexTile>();
            foreach (KeyValuePair<Unit, List<HexTile>> f in finishers) {
                HexTile pick = null;
                foreach (HexTile r in f.Value) if (!claimed.Contains(r)) { pick = r; break; }
                if (pick == null) continue;
                // Its own tile is not a claim on anyone else's space.
                if (pick != f.Key.tile) claimed.Add(pick);
                sum += blow[f.Key];
                by += (by.Length > 0 ? "+" : "") + f.Key.name;
                if (sum >= left) return true;
            }
            return false;
        }

        /// <summary>Material per strength point; HQs at their (deliberately enormous) cost.</summary>
        internal static float Value(Unit x) {
            return AIScore.Worth(x);
        }

        /// <summary>
        /// Could <paramref name="x"/> standing on <paramref name="xAt"/> strike <paramref name="d"/> on
        /// <paramref name="dAt"/> next turn (hex reach: move + range), and for how much each way.
        /// The firing tile is approximated by the defender's neighbour nearest the attacker.
        /// </summary>
        internal static bool InReach(Unit x, HexTile xAt, HexTile dAt, out int dmg, out int back, int xHp, Unit d, int dHp) {
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

        /// <summary>
        /// What our next turn could cost enemy <paramref name="e"/> (at <paramref name="eHp"/>) standing on
        /// <paramref name="tile"/>: every blow our surviving units could land there (hex reach),
        /// biggest first at weights 1, 1/2, 1/4 …, capped at its strength, plus the death premium
        /// if that would destroy it. In gold.
        /// </summary>
        internal static float Exposure(Unit e, float eHp, HexTile tile, AIMap map, Dictionary<Unit, float> hp, Func<Unit, HexTile> posOf) {
            if (eHp <= 0f || tile == null || !GameController.Targetable(e)) return 0f;
            List<float> blows = new List<float>();
            foreach (Unit x in map.ownUnits) {
                if (x == null || !hp.ContainsKey(x) || hp[x] <= 0f || x.Firepower <= 0 || IsHQ(x)) continue;
                HexTile xAt = posOf(x);
                if (xAt == null) continue;
                int dmg, back;
                if (InReach(x, xAt, tile, out dmg, out back, Hp(hp[x]), e, Hp(eHp))) blows.Add(dmg);
            }
            blows.Sort((a, b) => b.CompareTo(a));
            float sum = 0f, w = 1f;
            foreach (float b in blows) { sum += b * w; w *= 0.5f; }
            return Mathf.Min(sum, eHp) * Value(e) + (sum >= eHp ? map.profile.deathPremium * Value(e) : 0f);
        }

        /// <summary>Simulated strength as the whole number the game's damage rules use.</summary>
        internal static int Hp(float h) { return h <= 0f ? 0 : Mathf.Max(1, Mathf.RoundToInt(h)); }

        internal static bool IsHQ(Unit x) {
            SupplyPoint sp = x != null ? x.Hub : null;
            return sp != null && sp.producer;
        }

        internal static HexTile FiringTile(Unit e, HexTile target, HashSet<HexTile> pos, HashSet<HexTile> taken) {
            if (!e.moveAndAttack) {
                int d = e.tile.Distance(target);
                return d <= e.AttackRange && d > e.MinRange ? e.tile : null;
            }
            List<HexTile> ring = AIPath.TilesInRangeSorted(target, e.MinRange, e.AttackRange);
            if (ring.Contains(e.tile) && (!taken.Contains(e.tile) || e.tile.occupier == e)) return e.tile;
            foreach (HexTile r in ring) if (pos.Contains(r) && !taken.Contains(r)) return r;
            return null;
        }

        static bool NearAny(Unit e, HashSet<Unit> gone, HexTile uAt) {
            foreach (Unit g in gone) {
                HexTile t = g.tile != null ? g.tile : uAt;
                if (t != null && e.tile.Distance(t) <= e.moveRange + 1) return true;
            }
            return false;
        }

        /// <summary>
        /// Tiles <paramref name="e"/> could fire from this turn, with our acting unit moved to
        /// <paramref name="uAt"/> and our units in <paramref name="gone"/> off the board. Tiles held by
        /// its own side count: they pass through each other and can make room.
        /// </summary>
        static readonly HashSet<Unit> NoneGone = new HashSet<Unit>();

        /// <summary>
        /// Would emptying <paramref name="vacated"/> (and the tiles of <paramref name="gone"/>) and
        /// filling <paramref name="filled"/> leave <paramref name="e"/>'s reach <paramref name="reach"/>
        /// as it is? Movement has no zone of control: a tile matters only if the enemy could stand
        /// on it (filled) or step into it from somewhere it can reach (vacated).
        /// </summary>
        static bool Unaffected(HashSet<HexTile> reach, Unit e, HexTile vacated, HexTile filled, HashSet<Unit> gone, Unit u, HexTile uAt) {
            if (filled != null && (reach.Contains(filled) || filled == e.tile)) return false;
            if (vacated != null && Touches(reach, e, vacated)) return false;
            foreach (Unit g in gone) {
                HexTile t = g == u ? uAt : g.tile;
                if (t != null && Touches(reach, e, t)) return false;
            }
            return true;
        }

        static bool Touches(HashSet<HexTile> reach, Unit e, HexTile t) {
            if (reach.Contains(t) || t == e.tile) return true;
            foreach (HexTile n in t.neighbors) if (n != null && (reach.Contains(n) || n == e.tile)) return true;
            return false;
        }

        static HashSet<HexTile> PositionsOnBoard(Unit e, Unit u, HexTile from, HexTile uAt, bool moving, Unit stopWas, HashSet<Unit> gone, bool ownSideBlocks = false) {
            Dictionary<HexTile, Unit> restore = new Dictionary<HexTile, Unit>();
            try {
                if (moving) { restore[from] = from.occupier; restore[uAt] = uAt.occupier; from.occupier = null; uAt.occupier = u; }
                foreach (Unit g in gone) {
                    HexTile t = g == u ? uAt : g.tile;
                    if (t == null || t.occupier != g) continue;
                    if (!restore.ContainsKey(t)) restore[t] = t.occupier;
                    t.occupier = null;
                }
                return Positions(e, ownSideBlocks);
            } finally {
                foreach (KeyValuePair<HexTile, Unit> kv in restore) kv.Key.occupier = kv.Value;
                if (moving) { uAt.occupier = stopWas; from.occupier = u; }
            }
        }

        /// <summary>
        /// Could <paramref name="d"/>, hit on <paramref name="dAt"/> from <paramref name="fire"/>, fall
        /// back to a free neighbour farther from the attacker (CombatMath.FindRetreatTile's rule)?
        /// Immobile units (an HQ) never can.
        /// </summary>
        internal static bool CanFallBack(Unit d, HexTile dAt, HexTile fire, Unit u, HexTile uAt, Dictionary<Unit, float> hp, HashSet<HexTile> taken) {
            if (d.moveRange <= 0) return true;   // immobile (an HQ): never driven back, never pinned
            foreach (HexTile n in dAt.neighbors) {
                if (n == null || taken.Contains(n) || n == uAt) continue;
                if (!CombatMath.IsRetreatStep(d, dAt, n, fire)) continue;
                Unit o = n.occupier;
                if (o != null && o != u && hp.ContainsKey(o) && hp[o] > 0f) continue;
                return true;
            }
            return false;
        }

        internal static HashSet<HexTile> Positions(Unit e, bool ownSideBlocks = false) {
            HashSet<HexTile> positions = new HashSet<HexTile>();
            positions.Add(e.tile);
            if (e.moveAndAttack) {
                MoveField f = AIPath.ThisTurn(e);
                foreach (HexTile p in f.Tiles) {
                    if (p == e.tile || !f.ReachableThisTurn(p) || !e.CanEnter(p)) continue;
                    // Free, or held by its own side (which can move off).
                    if (p.occupier == null || (!ownSideBlocks && p.occupier.owner == e.owner)) positions.Add(p);
                }
                AIPath.Release(f);
            }
            return positions;
        }
    }
}
