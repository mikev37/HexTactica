using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>One unit's part in a group plan: where it ends its move, and whom it shoots (or nobody).</summary>
    public class PlanStep {
        public Unit unit;
        public HexTile stop;
        public Unit target;
        /// <summary>Optional: the exact outcome of the shot, when it is already known (CombatMath.Resolve).</summary>
        public CombatOutcome? known;
        public string kind;
        /// <summary>Which battle plan this step belongs to (AICoordinator); 0 = none.</summary>
        public int group;
        public override string ToString() {
            return unit.name.Replace("(Clone)", "") + (stop != null && stop != unit.tile ? "->(" + stop.x + "," + stop.y + ")" : "")
                 + (target != null ? " hits " + target.name.Replace("(Clone)", "") + (target.tile != null ? "(" + target.tile.x + "," + target.tile.y + ")" : "(gone)") : "");
        }
    }

    public class GroupPlan {
        public string kind;
        public readonly List<PlanStep> steps = new List<PlanStep>();
        public AIWorld.Outcome world;
    }

    /// <summary>
    /// Coordinated plans for groups of units in contact (AIProfile.coordination).
    ///
    /// Units still decide one at a time — but before they do, each group of units the enemy can
    /// strike next turn (or that can strike now) is given three whole-group plans, each forecast as
    /// one board with every unit of the group moved:
    ///   Strike   — go after enemy units in order of value; give each target attackers (artillery
    ///              first, then the strongest blows) from real, free firing tiles until it dies;
    ///              targets that can't be killed this turn get nobody.
    ///   Withdraw — every unit to the nearest-to-the-enemy tile it can reach that is out of the
    ///              enemy's next-turn strike range (least exposed tile if there is none).
    ///   Stay     — everyone holds where they are and takes its best shot from there: the baseline.
    /// The best of the three wins if it beats Stay by coordinationMargin; then its units act first,
    /// in plan order, each step checked before it runs (target alive and in range, tile free,
    /// reachable). A step gone stale — a target that retreated, a tile taken — falls back to the
    /// unit's normal one-at-a-time decision. Stay winning means no plan: today's behaviour.
    ///
    /// The forecast (Evaluate below) is AIWorld.Evaluate generalised to many moves, applied in order,
    /// and with retreats modelled: a defender driven back moves to its retreat tile, freeing its old
    /// one — for our shots and for the enemy reply. With one move and retreats off it must score
    /// exactly what AIWorld.Evaluate does with its extras (assists, space, strike terms,
    /// self-preservation) off; PROBE=equiv checks that.
    /// </summary>
    public static class AIPlan {

        // ================================================================ the board, overridden

        /// <summary>Temporary occupancy changes, all undone by Restore.</summary>
        class Board {
            readonly Dictionary<HexTile, Unit> saved = new Dictionary<HexTile, Unit>();
            void Set(HexTile t, Unit u) { if (t == null) return; if (!saved.ContainsKey(t)) saved[t] = t.occupier; t.occupier = u; }
            public void Move(Unit u, HexTile from, HexTile to) {
                if (from != null && from.occupier == u) Set(from, null);
                if (to != null) Set(to, u);
            }
            public void Clear(HexTile t, Unit u) { if (t != null && t.occupier == u) Set(t, null); }
            /// <summary>Is every tile this board has changed beyond <paramref name="e"/>'s move this turn (so its reach is the real board's)?</summary>
            public bool FarFrom(Unit e) {
                int reach = (e.moveAndAttack ? e.moveRange : 0) + 1;
                foreach (HexTile t in saved.Keys) if (t.Distance(e.tile) <= reach) return false;
                return true;
            }
            public void Restore() { foreach (KeyValuePair<HexTile, Unit> kv in saved) kv.Key.occupier = kv.Value; saved.Clear(); }
        }

        /// <summary>A free neighbour away from <paramref name="attacker"/> (CombatMath.FindRetreatTile's rule), on the overridden board.</summary>
        internal static HexTile RetreatTile(Unit d, HexTile dAt, HexTile attacker, Dictionary<Unit, float> hp, HashSet<HexTile> taken) {
            if (d.moveRange <= 0 || dAt == null) return null;
            HexTile best = null; int bestD = 0;
            foreach (HexTile n in dAt.neighbors) {
                if (!CombatMath.IsRetreatStep(d, dAt, n, attacker)) continue;
                if (taken != null && taken.Contains(n)) continue;
                Unit o = n.occupier;
                if (o != null && (!hp.ContainsKey(o) || hp[o] > 0f)) continue;
                int dd = attacker != null ? n.Distance(attacker) : 1;
                if (best == null || dd > bestD || (dd == bestD && CombatMath.Straighter(n, best, dAt, attacker))) { best = n; bestD = dd; }
            }
            return best;
        }

        static HashSet<HexTile> PositionsFrom(Unit e, HexTile origin, bool ownSideBlocks) {
            if (origin == e.tile) return AIWorld.Positions(e, ownSideBlocks);
            HashSet<HexTile> positions = new HashSet<HexTile>();
            positions.Add(origin);
            if (e.moveAndAttack) {
                MoveField f = AIPath.ThisTurnFrom(e, origin, AIPath.RangeThisTurn(e));
                foreach (HexTile p in f.Tiles) {
                    if (p == origin || !f.ReachableThisTurn(p) || !e.CanEnter(p)) continue;
                    if (p.occupier == null || (!ownSideBlocks && p.occupier.owner == e.owner)) positions.Add(p);
                }
                AIPath.Release(f);
            }
            return positions;
        }

        // ================================================================ the multi-move forecast

        public static AIWorld.Outcome Evaluate(List<PlanStep> steps, AIMap map, bool narrate, bool modelRetreats) {
            AIWorld.Outcome w = new AIWorld.Outcome();
            AIProfile prof = map.profile;
            Player us = map.player;
            Dictionary<Unit, float> hp = new Dictionary<Unit, float>();
            Dictionary<Unit, HexTile> at = new Dictionary<Unit, HexTile>();
            foreach (Unit x in map.ownUnits) if (x != null && x.tile != null) { hp[x] = x.Strength; at[x] = x.tile; }
            foreach (Unit x in map.enemyUnits) if (x != null && x.tile != null) { hp[x] = x.Strength; at[x] = x.tile; }
            Board board = new Board();
            Dictionary<Unit, HexTile> endsAt = new Dictionary<Unit, HexTile>();
            bool breakpoints = prof.replyBreakpoints;
            Dictionary<Unit, float> hpCertain = null;
            // Suppressed pips as the forecast plays out. Ours: their fire suppresses in their reply.
            // Theirs: what our indirect fire has suppressed this turn (and what it will in the steps
            // below), which do not answer our later blows; their turn starting gives them back.
            Dictionary<Unit, int> sup = new Dictionary<Unit, int>();
            foreach (Unit x in map.enemyUnits) if (x != null && x.suppressed > 0) sup[x] = x.suppressed;
            Func<Unit, int> supOf = x => { int v; return sup.TryGetValue(x, out v) ? v : 0; };
            try {
                // ---- our moves and shots, in order.
                foreach (PlanStep st in steps) {
                    Unit x = st.unit;
                    if (x == null || !hp.ContainsKey(x) || hp[x] <= 0f) continue;
                    if (st.stop != null && st.stop != at[x]) { board.Move(x, at[x], st.stop); at[x] = st.stop; }
                    Unit t = st.target;
                    if (t == null || !hp.ContainsKey(t) || hp[t] <= 0f) continue;
                    // Out of range now (an earlier shot drove it back): no shot.
                    int range = at[x].Distance(at[t]);
                    if (range > x.AttackRange || range <= x.MinRange) continue;
                    int dmg, back;
                    HexTile retreat = null;
                    bool pinned = false;
                    if (st.known.HasValue) {
                        CombatOutcome o = st.known.Value;
                        dmg = o.damageToDefender; back = o.damageToAttacker;
                        pinned = o.defenderPinned;
                        if (o.defenderRetreats && !o.defenderPinned && !o.defenderDies) retreat = o.retreatTile;
                    } else {
                        CombatMath.Exchange(x, AIWorld.Hp(hp[x]), at[x], t, AIWorld.Hp(hp[t]), at[t], supOf(t), out dmg, out back);
                        if (CombatMath.Suppresses(x)) { }   // indirect fire: suppresses (below), never drives back or pins
                        else if (dmg > 0 && dmg < hp[t] && dmg - t.Veterancy > back - x.Veterancy && t.moveRange > 0) {
                            retreat = RetreatTile(t, at[t], at[x], hp, null);
                            if (retreat == null && prof.forecastPins) { dmg += 1; pinned = true; }
                        }
                    }
                    float dealt = Mathf.Min(hp[t], dmg);
                    bool suppresses = CombatMath.Suppresses(x) && CombatMath.Pushes(x, back, t, dmg, hp[t]);
                    hp[t] -= dealt;
                    hp[x] = Mathf.Max(0f, hp[x] - back);
                    if (suppresses && hp[t] > 0f) sup[t] = supOf(t) + CombatMath.SuppressionFrom(AIWorld.Hp(hp[t]), supOf(t));
                    if (narrate) w.replies.Add("we: " + st + " " + dealt.ToString("0.#") + (pinned ? " pinned" : "") + (back > 0 ? "/" + back : "") + (suppresses && hp[t] > 0f ? " suppressed " + supOf(t) : ""));
                    if (hp[t] <= 0f) board.Clear(at[t], t);
                    else if (modelRetreats && retreat != null) { board.Move(t, at[t], retreat); at[t] = retreat; }
                    if (hp[x] <= 0f) board.Clear(at[x], x);
                }

                // ---- their reply: AIWorld.Evaluate's, with every moved unit where it now stands.
                if (breakpoints) hpCertain = new Dictionary<Unit, float>(hp);
                sup.Clear();   // their turn starts: their pips come back; ours are suppressed only by what follows
                List<Unit> enemies = new List<Unit>(map.enemyUnits);
                enemies.Sort((a, b) => {
                    int c = (b.Firepower * b.Strength).CompareTo(a.Firepower * a.Strength);
                    return c != 0 ? c : AIMap.CompareUnits(a, b);
                });
                bool ownSideBlocks = prof.forecastOwnSideBlocks;
                // Our units destroyed by retaliation are off the board (the single-move forecast does the same).
                if (prof.forecastDeadBlock) foreach (Unit x in map.ownUnits) if (hp.ContainsKey(x) && hp[x] <= 0f && x.tile != null) board.Move(x, null, at[x]);

                Dictionary<Unit, HashSet<HexTile>> positions = new Dictionary<Unit, HashSet<HexTile>>();
                foreach (Unit e in enemies) {
                    if (e == null || e.tile == null || !hp.ContainsKey(e) || hp[e] <= 0f) continue;
                    if (e.Firepower <= 0 || !e.CanAttack(e.tile)) continue;
                    // An enemy where it stands, out of reach of every tile this plan changed (moves,
                    // kills, retreats): where it can go is what it is on the real board, worked out
                    // once per board (the reply cache) instead of once per plan scored.
                    HashSet<HexTile> cached;
                    if (at[e] == e.tile && !ownSideBlocks && board.FarFrom(e)) {
                        if (!map.replyCache.positions.TryGetValue(e, out cached)) { cached = AIWorld.Positions(e, false); map.replyCache.positions[e] = cached; }
                        positions[e] = cached;
                        continue;
                    }
                    positions[e] = PositionsFrom(e, at[e], ownSideBlocks);
                }
                HashSet<HexTile> taken = new HashSet<HexTile>();
                foreach (Unit x in map.ownUnits) if (x != null && hp.ContainsKey(x) && hp[x] > 0f) taken.Add(at[x]);
                foreach (Unit e in enemies) {
                    if (e == null || e.tile == null || !hp.ContainsKey(e) || hp[e] <= 0f) continue;
                    bool firesInPlace = !e.moveAndAttack && e.Firepower > 0 && positions.ContainsKey(e);
                    if (ownSideBlocks || firesInPlace || e.moveRange <= 0) taken.Add(at[e]);
                }

                bool weighs = prof.enemyWeighsRisk;
                Func<Unit, HexTile> ourPos = x => at.ContainsKey(x) ? at[x] : x.tile;
                for (int ei = 0; ei < enemies.Count; ei++) {
                    Unit e = enemies[ei];
                    HashSet<HexTile> pos;
                    if (!positions.TryGetValue(e, out pos) || hp[e] <= 0f) continue;
                    Unit bestTarget = null; HexTile bestTile = null; float bestValue = 0f; int bestDamage = 0, bestBack = 0;
                    bool bestPinned = false;
                    float home = weighs ? AIWorld.Exposure(e, hp[e], at[e], map, hp, ourPos) : 0f;
                    foreach (Unit d in map.ownUnits) {
                        if (d == null || !hp.ContainsKey(d) || hp[d] <= 0f || !GameController.Targetable(d)) continue;
                        HexTile dAt = at[d];
                        HexTile fire = FiringFrom(e, at[e], dAt, pos, taken);
                        if (fire == null) continue;
                        int dmg, back;
                        CombatMath.Exchange(e, AIWorld.Hp(hp[e]), fire, d, AIWorld.Hp(hp[d]), dAt, supOf(d), out dmg, out back);
                        if (dmg <= 0) continue;
                        bool pinned = false;
                        if (prof.forecastPins && !CombatMath.Suppresses(e) && dmg < hp[d] && dmg - d.Veterancy > back - e.Veterancy && !AIWorld.CanFallBack(d, dAt, fire, null, null, hp, taken)) {
                            dmg += 1;
                            pinned = true;
                        }
                        float value = (dmg * AIWorld.Value(d) - back * AIWorld.Value(e)) * (dmg >= hp[d] ? 2f : 1f);
                        if (breakpoints && dmg < hp[d] && prof.assistWeight > 0f
                            && AIWorld.EnemyCanFinish(d, dAt, hp[d] - dmg, enemies, ei, positions, taken, fire, hp)) {
                            float aw = prof.assistWeight;
                            value = ((dmg + aw * (hp[d] - dmg)) * AIWorld.Value(d) - back * AIWorld.Value(e)) * (1f + aw);
                        }
                        if (weighs) value -= Mathf.Max(0f, AIWorld.Exposure(e, hp[e] - back, fire, map, hp, ourPos) - home) * prof.enemyCaution;
                        if (value > bestValue || (value == bestValue && bestTarget != null && AIMap.CompareUnits(d, bestTarget) < 0)) {
                            bestValue = value; bestTarget = d; bestTile = fire; bestDamage = dmg; bestBack = back; bestPinned = pinned;
                        }
                    }
                    if (bestTarget == null) continue;
                    if (ownSideBlocks && bestTile != at[e]) taken.Remove(at[e]);
                    taken.Add(bestTile);
                    endsAt[e] = bestTile;
                    float dealt = Mathf.Min(hp[bestTarget], bestDamage * (breakpoints ? 1f : 1f - prof.replyDiscount));
                    bool certain = breakpoints && bestBack == 0 && bestTile.Distance(at[bestTarget]) > 1;
                    if (certain) hpCertain[bestTarget] = Mathf.Max(0f, hpCertain[bestTarget] - dealt);
                    bool pushed = CombatMath.Pushes(e, bestBack, bestTarget, bestDamage, hp[bestTarget]);
                    hp[bestTarget] -= dealt;
                    if (pushed && CombatMath.Suppresses(e) && hp[bestTarget] > 0f) {
                        sup[bestTarget] = supOf(bestTarget) + CombatMath.SuppressionFrom(AIWorld.Hp(hp[bestTarget]), supOf(bestTarget));
                        if (narrate) w.replies.Add(bestTarget.name + " suppressed " + sup[bestTarget]);
                    }
                    float sofar;
                    w.lost[bestTarget] = (w.lost.TryGetValue(bestTarget, out sofar) ? sofar : 0f) + dealt;
                    hp[e] = Mathf.Max(0f, hp[e] - bestBack);
                    if (narrate) w.replies.Add(e.name + "->" + bestTarget.name + " " + dealt.ToString("0.#") + (bestPinned ? " pinned" : "") + (bestBack > 0 ? "/" + bestBack : "") + (certain ? " certain" : ""));

                    HexTile freed = null;
                    if (hp[bestTarget] <= 0f && !prof.forecastDeadBlock) {
                        freed = at[bestTarget];
                        taken.Remove(freed);
                        board.Clear(freed, bestTarget);
                    } else if (hp[bestTarget] > 0f && modelRetreats && !bestPinned && !CombatMath.Suppresses(e)
                               && bestDamage - bestTarget.Veterancy > bestBack - e.Veterancy && bestTarget.moveRange > 0) {
                        // Driven back: it moves, and the tile it held opens.
                        HexTile rt = RetreatTile(bestTarget, at[bestTarget], bestTile, hp, taken);
                        if (rt != null) {
                            freed = at[bestTarget];
                            board.Move(bestTarget, freed, rt);
                            taken.Remove(freed); taken.Add(rt);
                            at[bestTarget] = rt;
                            if (narrate) w.replies.Add(bestTarget.name + " falls back to (" + rt.x + "," + rt.y + ")");
                        }
                    }
                    if (freed != null) {
                        foreach (Unit later in enemies) {
                            if (later == e || !positions.ContainsKey(later) || hp[later] <= 0f || !later.moveAndAttack) continue;
                            if (at[later].Distance(freed) > later.moveRange + 1) continue;
                            positions[later] = PositionsFrom(later, at[later], ownSideBlocks);
                        }
                    }
                }
            } finally {
                board.Restore();
            }

            // ---- what is left: AIWorld.Evaluate's scoring (and its breakpoint blend).
            Func<Unit, HexTile> where = x => x.owner == us ? at[x] : (endsAt.ContainsKey(x) ? endsAt[x] : at[x]);
            float valued = AIWorld.Valuate(hp, us, where, map, w);
            if (breakpoints) {
                AIWorld.Outcome c = new AIWorld.Outcome();
                float vc = AIWorld.Valuate(hpCertain, us, where, map, c);
                float k = 1f - prof.replyDiscount;
                valued = vc + k * (valued - vc);
                w.own = c.own + k * (w.own - c.own);
                w.enemy = c.enemy + k * (w.enemy - c.enemy);
                w.deaths = c.deaths + k * (w.deaths - c.deaths);
                w.hubs = c.hubs + k * (w.hubs - c.hubs);
            }
            w.score = valued;
            return w;
        }

        /// <summary>AIWorld.FiringTile for an enemy that may have been driven to <paramref name="origin"/>.</summary>
        static HexTile FiringFrom(Unit e, HexTile origin, HexTile target, HashSet<HexTile> pos, HashSet<HexTile> taken) {
            if (origin == e.tile) return AIWorld.FiringTile(e, target, pos, taken);
            if (!e.moveAndAttack) {
                int d = origin.Distance(target);
                return d <= e.AttackRange && d > e.MinRange ? origin : null;
            }
            List<HexTile> ring = AIPath.TilesInRangeSorted(target, e.MinRange, e.AttackRange);
            if (ring.Contains(origin) && (!taken.Contains(origin) || origin.occupier == e)) return origin;
            foreach (HexTile r in ring) if (pos.Contains(r) && !taken.Contains(r)) return r;
            return null;
        }

        // ================================================================ groups and plans

        static bool Eligible(Unit u) {
            if (u == null || u.tile == null || u.Strength <= 0 || !u.active || u.trasnporter != null) return false;
            if (u.Firepower <= 0 || u.GetComponent<SupplyPoint>() != null) return false;
            return true;
        }

        /// <summary>Our combat units in contact (TurnsToContact ≤ 1), linked within 3 tiles; groups of two or more.</summary>
        public static List<List<Unit>> Groups(AIMap map) {
            List<Unit> pool = new List<Unit>();
            foreach (Unit u in map.ownUnits) if (Eligible(u) && map.TurnsToContact(u) <= 1) pool.Add(u);
            List<List<Unit>> groups = new List<List<Unit>>();
            HashSet<Unit> seen = new HashSet<Unit>();
            foreach (Unit a in pool) {
                if (seen.Contains(a)) continue;
                List<Unit> g = new List<Unit>();
                Queue<Unit> q = new Queue<Unit>();
                q.Enqueue(a); seen.Add(a);
                while (q.Count > 0) {
                    Unit x = q.Dequeue(); g.Add(x);
                    foreach (Unit b in pool) if (!seen.Contains(b) && x.tile.Distance(b.tile) <= 3) { seen.Add(b); q.Enqueue(b); }
                }
                if (g.Count >= 2) { g.Sort(AIMap.CompareUnits); groups.Add(g); }
            }
            return groups;
        }

        /// <summary>Tiles <paramref name="u"/> could fire on <paramref name="target"/> from this turn: free and reachable (artillery: only where it stands).</summary>
        static List<HexTile> FiringOptions(Unit u, Unit target, AIMap map, HashSet<HexTile> claimed) {
            List<HexTile> list = new List<HexTile>();
            if (!u.CanAttack(u.tile) || !GameController.Targetable(target)) return list;
            int d0 = u.tile.Distance(target.tile);
            if (d0 <= u.AttackRange && d0 > u.MinRange && !claimed.Contains(u.tile)) list.Add(u.tile);
            if (!u.moveAndAttack) return list;
            MoveField f = map.ReachThisTurn(u);
            foreach (HexTile r in AIPath.TilesInRange(target.tile, u.MinRange, u.AttackRange)) {
                if (r == u.tile || claimed.Contains(r)) continue;
                if (f.ReachableThisTurn(r) && AIPath.CanFinishTurnOn(u, r)) list.Add(r);
            }
            list.Sort((a, b) => {
                int c = map.ExpectedLoss(u, a).CompareTo(map.ExpectedLoss(u, b));
                return c != 0 ? c : CombatMath.CompareTiles(a, b);
            });
            return list;
        }

        /// <summary>Kill what can be killed this turn, most valuable first, with the fewest blows each.</summary>
        public static GroupPlan Strike(List<Unit> group, AIMap map) {
            GroupPlan plan = new GroupPlan { kind = "strike" };
            Dictionary<Unit, float> hp = new Dictionary<Unit, float>();
            foreach (Unit x in map.ownUnits) if (x != null && x.tile != null) hp[x] = x.Strength;
            foreach (Unit x in map.enemyUnits) if (x != null && x.tile != null) hp[x] = x.Strength;
            List<Unit> targets = new List<Unit>();
            foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && GameController.Targetable(e)) targets.Add(e);
            targets.Sort((a, b) => {
                int c = (AIWorld.Value(b) * b.Strength).CompareTo(AIWorld.Value(a) * a.Strength);
                return c != 0 ? c : AIMap.CompareUnits(a, b);
            });
            HashSet<Unit> used = new HashSet<Unit>();
            HashSet<HexTile> claimed = new HashSet<HexTile>();
            foreach (Unit t in targets) {
                // Candidate blows on this target from units not yet used: artillery first (it can't
                // move after), then by damage.
                List<KeyValuePair<Unit, HexTile>> blows = new List<KeyValuePair<Unit, HexTile>>();
                foreach (Unit u in group) {
                    if (used.Contains(u)) continue;
                    List<HexTile> opts = FiringOptions(u, t, map, claimed);
                    if (opts.Count > 0) blows.Add(new KeyValuePair<Unit, HexTile>(u, opts[0]));
                }
                if (blows.Count == 0) continue;
                blows.Sort((a, b) => {
                    bool aa = !a.Key.moveAndAttack, ba = !b.Key.moveAndAttack;
                    if (aa != ba) return aa ? -1 : 1;
                    int c = Unit.calculateDamage(b.Key, t, t.tile, 0).CompareTo(Unit.calculateDamage(a.Key, t, t.tile, 0));
                    return c != 0 ? c : AIMap.CompareUnits(a.Key, b.Key);
                });
                float left = hp[t];
                int tsup = t.suppressed;   // artillery goes first: what it suppresses does not answer the assault
                List<PlanStep> chosen = new List<PlanStep>();
                HashSet<HexTile> tentative = new HashSet<HexTile>();
                foreach (KeyValuePair<Unit, HexTile> b in blows) {
                    if (left <= 0f) break;
                    if (tentative.Contains(b.Value)) continue;
                    int dmg, back;
                    CombatMath.Exchange(b.Key, AIWorld.Hp(hp[b.Key]), b.Value, t, AIWorld.Hp(left), t.tile, tsup, out dmg, out back);
                    if (dmg <= 0) continue;
                    bool suppresses = CombatMath.Suppresses(b.Key) && CombatMath.Pushes(b.Key, back, t, dmg, left);
                    left -= dmg;
                    if (suppresses && left > 0f) tsup += CombatMath.SuppressionFrom(AIWorld.Hp(left), tsup);
                    tentative.Add(b.Value);
                    chosen.Add(new PlanStep { unit = b.Key, stop = b.Value, target = t, kind = "strike" });
                }
                if (left > 0f) continue;   // can't be killed this turn: nobody goes in for it
                foreach (PlanStep s in chosen) { used.Add(s.unit); claimed.Add(s.stop); plan.steps.Add(s); }
                hp[t] = 0f;
            }
            // Artillery fires first: its shot cannot follow a move, and melee can finish what it starts.
            plan.steps.Sort((a, b) => {
                bool aa = !a.unit.moveAndAttack, ba = !b.unit.moveAndAttack;
                return aa == ba ? 0 : aa ? -1 : 1;
            });
            return plan;
        }

        /// <summary>Everyone out of the enemy's next-turn strike range, as close to the enemy as that allows.</summary>
        public static GroupPlan Withdraw(List<Unit> group, AIMap map) {
            GroupPlan plan = new GroupPlan { kind = "withdraw" };
            HashSet<HexTile> claimed = new HashSet<HexTile>();
            foreach (Unit u in map.ownUnits) if (u != null && u.tile != null && !group.Contains(u)) claimed.Add(u.tile);
            foreach (Unit u in group) {
                MoveField f = map.ReachThisTurn(u);
                List<HexTile> tiles = new List<HexTile> { u.tile };
                if (u.moveRange > 0) foreach (HexTile t in f.Tiles) if (t != u.tile && f.ReachableThisTurn(t) && AIPath.CanFinishTurnOn(u, t)) tiles.Add(t);
                HexTile best = null; int bestNear = int.MaxValue; bool bestSafe = false; float bestLoss = float.MaxValue;
                foreach (HexTile t in tiles) {
                    if (claimed.Contains(t)) continue;
                    bool safe = map.EnemyStrikeTurns(t) > 1;
                    int near = NearestEnemy(t, map);
                    float loss = map.ExpectedLoss(u, t);
                    bool better;
                    if (safe != bestSafe) better = safe;
                    else if (safe) better = near < bestNear || (near == bestNear && (map.SupplyDepth(t) > (best != null ? map.SupplyDepth(best) : -1)));
                    else better = loss < bestLoss || (loss == bestLoss && near < bestNear);
                    if (best == null || better) { best = t; bestSafe = safe; bestNear = near; bestLoss = loss; }
                }
                if (best == null) continue;
                claimed.Add(best);
                plan.steps.Add(new PlanStep { unit = u, stop = best, target = null, kind = "withdraw" });
            }
            return plan;
        }

        /// <summary>Hold: each unit stays and takes its best shot from where it stands (attack score's pick).</summary>
        public static GroupPlan Stay(List<Unit> group, AIMap map) {
            GroupPlan plan = new GroupPlan { kind = "stay" };
            foreach (Unit u in group) {
                AttackAppraisal a = AITargeting.BestFrom(u, u.tile, map, map.profile, Posture.Defensive, true);
                plan.steps.Add(new PlanStep { unit = u, stop = u.tile, target = a.valid && a.value > 0f ? a.target : null, kind = "stay" });
            }
            return plan;
        }

        static int NearestEnemy(HexTile t, AIMap map) {
            int best = int.MaxValue;
            foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && e.Firepower > 0) best = Mathf.Min(best, t.Distance(e.tile));
            return best;
        }

        /// <summary>
        /// The plan for this turn: for each group, the best of strike / withdraw / stay by forecast;
        /// strike or withdraw only if it beats stay by coordinationMargin. Returns the steps to run,
        /// in order (empty when every group is best left to individual decisions).
        /// </summary>
        public static List<PlanStep> Choose(AIMap map, AIProfile profile, Action<string> log) {
            List<PlanStep> result = new List<PlanStep>();
            foreach (List<Unit> g in Groups(map)) {
                List<GroupPlan> plans = new List<GroupPlan> { Stay(g, map), Strike(g, map), Withdraw(g, map) };
                foreach (GroupPlan p in plans) p.world = Evaluate(p.steps, map, log != null, profile.coordinationRetreats);
                GroupPlan stay = plans[0], best = stay;
                foreach (GroupPlan p in plans) if (p.steps.Count > 0 && p.world.score > best.world.score) best = p;
                bool adopt = best != stay && best.world.score >= stay.world.score + profile.coordinationMargin;
                if (log != null) {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("   group of ").Append(g.Count).Append(':');
                    foreach (Unit u in g) sb.Append(' ').Append(u.name.Replace("(Clone)", "")).Append("(").Append(u.tile.x).Append(',').Append(u.tile.y).Append(')');
                    log(sb.ToString());
                    foreach (GroupPlan p in plans) {
                        log("     " + (p == best && adopt ? "> " : "  ") + p.kind + " world " + p.world.score.ToString("0") + ": "
                            + (p.steps.Count == 0 ? "(nothing)" : string.Join("; ", p.steps.ConvertAll(s => s.ToString()).ToArray()))
                            + " [" + string.Join("; ", p.world.replies.ToArray()) + "]");
                    }
                    log("     " + (adopt ? "plan adopted: " + best.kind : "no plan: units decide one at a time"));
                }
                if (adopt) result.AddRange(best.steps);
            }
            return result;
        }

        /// <summary>Is this step still possible as planned, on the board as it is now?</summary>
        public static bool StillValid(PlanStep s, AIMap map) {
            Unit u = s.unit;
            if (u == null || u.tile == null || u.Strength <= 0 || !u.active) return false;
            if (s.stop != null && s.stop != u.tile) {
                if (!u.moveAndAttack && s.target != null) return false;
                if (!AIPath.CanFinishTurnOn(u, s.stop)) return false;
                if (!map.ReachThisTurn(u).ReachableThisTurn(s.stop)) return false;
            }
            if (s.target != null) {
                Unit t = s.target;
                if (t == null || t.tile == null || t.Strength <= 0 || !GameController.Targetable(t)) return false;
                HexTile from = s.stop ?? u.tile;
                int d = from.Distance(t.tile);
                if (d > u.AttackRange || d <= u.MinRange || !u.CanAttack(from)) return false;
            }
            return true;
        }
    }
}
