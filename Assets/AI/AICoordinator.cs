using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Coordinated strikes (AIProfile.coordinationPasses): a correction on top of the units' own
    /// decisions, not a replacement for them.
    ///
    ///   1. Every combat unit in contact runs its normal decision without acting. What it chose is
    ///      its favourite; its scored options are kept too: its coordOffensiveOptions best attacks,
    ///      its best no-shot move that keeps contact ("survive") and its best move out of the
    ///      enemy's reach ("retreat").
    ///   2. Battle plans: for each enemy that two or more units have an attack on, every group of
    ///      those attackers gets one plan — each takes its most preferred attack on that target
    ///      from a tile nobody else in the group has taken. Groups whose blows kill are kept, and
    ///      the group of all of them.
    ///   3. A plan is worth taking only for what acting together adds. Synergy = the plan's
    ///      forecast (AIPlan.Evaluate: all moves and shots in order, retreats modelled, then the
    ///      enemy's reply) minus what each of its steps would have done on its own. Sacrifice = for
    ///      each unit, how much worse its plan move is than its favourite by its own normal
    ///      evaluation (so blocking, cover, space and the rest still count). Surplus = synergy −
    ///      sacrifice; a plan whose units all keep their favourites costs nothing.
    ///   4. Plans are taken best surplus first, above coordinationPassMargin; a plan sharing a unit, a
    ///      tile or a target with one already taken is dropped. Inside a plan the units go in order of
    ///      the retaliation they take, least first (artillery, then tanks, then infantry).
    ///   5. A unit whose favourite is an attack that counted on an assist (someone else finishing the
    ///      target), and that is not in a plan making that kill, decides again with no assist credit:
    ///      nobody has promised to finish the job, so it should not charge in expecting it.
    ///   6. Everyone else decides alone as usual, and so does anyone whose planned step has gone
    ///      stale (target gone, tile taken).
    /// </summary>
    public static class AICoordinator {

        public class Candidate {
            public Unit unit;
            public HexTile stop;
            public AttackAppraisal shot;
            public float world;
            public bool assist;
        }

        class Menu {
            public Unit unit;
            public Candidate favourite, survive, retreat;
            public List<Candidate> attacks = new List<Candidate>();
            public List<Candidate> all;
        }

        class Battle {
            public Unit target;
            public List<PlanStep> steps = new List<PlanStep>();
            public List<Candidate> picks = new List<Candidate>();
            public float joint, synergy, sacrifice, surplus;
            public bool kills;
        }

        static bool Eligible(Unit u, AIMap map) {
            if (u == null || u.tile == null || u.Strength <= 0 || !u.active || u.trasnporter != null) return false;
            if (u.Firepower <= 0 || u.Hub != null) return false;
            return map.TurnsToContact(u) <= 1;
        }

        static string D(HexTile t) { return t == null ? "-" : "(" + t.x + "," + t.y + ")"; }
        static string N(Unit u) { return u == null ? "-" : u.name.Replace("(Clone)", ""); }

        static string Show(Candidate c) {
            if (c == null) return "none";
            return D(c.stop) + (c.shot.valid ? " hit " + N(c.shot.target) + D(c.shot.target.tile) + " " + c.shot.outcome.damageToDefender + "/" + c.shot.outcome.damageToAttacker : " no shot")
                   + (c.assist ? " (counts on assist)" : "") + " world " + c.world.ToString("0");
        }

        /// <param name="decide">The unit's normal decision (its options are captured; nothing acts).</param>
        /// <param name="noAssist">Filled with the units that must decide without assist credit (rule 5).</param>
        public static List<PlanStep> Plan(AIMap map, AIProfile profile, Func<Unit, AIAction> decide, HashSet<Unit> noAssist, Action<string> log) {
            List<PlanStep> result = new List<PlanStep>();

            // ---- pass 1: each unit's favourite and menu.
            List<Menu> menus = new List<Menu>();
            Action<string> savedLog = AITactical.DecisionLog;
            Dictionary<Unit, HexTile> savedIntents = AITactical.Intents;
            AIWorld.Outcome savedLast = AITactical.LastChosen;
            try {
                AITactical.DecisionLog = null;
                AITactical.Intents = new Dictionary<Unit, HexTile>(savedIntents ?? new Dictionary<Unit, HexTile>());
                foreach (Unit u in map.ownUnits) {
                    if (!Eligible(u, map)) continue;
                    List<Candidate> all = new List<Candidate>();
                    AIAction chosen;
                    AITactical.Capture = all;
                    try { chosen = decide(u); } finally { AITactical.Capture = null; }
                    if (all.Count == 0) continue;
                    HexTile stop = chosen.destination ?? u.tile;
                    Unit target = chosen.kind == AIActionKind.Attack || chosen.kind == AIActionKind.MoveAndAttack ? chosen.target : null;
                    Candidate fav = all.Find(c => c.stop == stop && (c.shot.valid ? c.shot.target : null) == target);
                    if (fav == null) continue;   // not a world-scored choice (a withdraw, a capture)
                    all.Sort((a, b) => {
                        int c = b.world.CompareTo(a.world);
                        return c != 0 ? c : CombatMath.CompareTiles(a.stop, b.stop);
                    });
                    Menu m = new Menu { unit = u, favourite = fav, all = all };
                    if (fav.shot.valid && GameController.Targetable(fav.shot.target)) m.attacks.Add(fav);
                    foreach (Candidate c in all) {
                        if (c.shot.valid && c.shot.target != null && GameController.Targetable(c.shot.target)) {
                            if (m.attacks.Count >= profile.coordOffensiveOptions) continue;
                            if (!m.attacks.Exists(x => x.stop == c.stop && x.shot.target == c.shot.target)) m.attacks.Add(c);
                        } else if (map.EnemyStrikeTurns(c.stop) > 1) { if (m.retreat == null) m.retreat = c; }
                        else if (m.survive == null) m.survive = c;
                    }
                    menus.Add(m);
                }
            } finally {
                AITactical.DecisionLog = savedLog;
                AITactical.Intents = savedIntents;
                AITactical.LastChosen = savedLast;
            }
            if (log != null && menus.Count > 0) {
                log("   coordination pass 1: " + menus.Count + " units in contact");
                foreach (Menu m in menus) {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("     ").Append(N(m.unit)).Append(D(m.unit.tile)).Append(" favourite ").Append(Show(m.favourite)).Append(" |");
                    for (int i = 0; i < m.attacks.Count; i++) sb.Append(" attack").Append(i + 1).Append(' ').Append(Show(m.attacks[i])).Append(';');
                    sb.Append(" survive ").Append(Show(m.survive)).Append("; retreat ").Append(Show(m.retreat));
                    log(sb.ToString());
                }
            }

            // ---- pass 2: battle plans per target.
            bool retreats = profile.coordinationRetreats;
            float empty = float.NaN;
            Dictionary<string, float> single = new Dictionary<string, float>();
            Func<PlanStep, float> alone = st => {
                float v;
                string key = st.unit.tile.x + "," + st.unit.tile.y + ">" + st.stop.x + "," + st.stop.y + ">" + (st.target != null ? st.target.tile.x + "," + st.target.tile.y : "-");
                if (single.TryGetValue(key, out v)) return v;
                if (float.IsNaN(empty)) empty = AIPlan.Evaluate(new List<PlanStep>(), map, false, retreats).score;
                v = AIPlan.Evaluate(new List<PlanStep> { st }, map, false, retreats).score - empty;
                single[key] = v;
                return v;
            };
            Dictionary<Unit, float> strength = new Dictionary<Unit, float>();
            foreach (Unit x in map.ownUnits) if (x != null && x.tile != null) strength[x] = x.Strength;
            foreach (Unit x in map.enemyUnits) if (x != null && x.tile != null) strength[x] = x.Strength;

            Dictionary<Unit, List<Menu>> byTarget = new Dictionary<Unit, List<Menu>>();
            List<Unit> targets = new List<Unit>();
            foreach (Menu m in menus) {
                foreach (Candidate c in m.attacks) {
                    List<Menu> l;
                    if (!byTarget.TryGetValue(c.shot.target, out l)) { l = new List<Menu>(); byTarget[c.shot.target] = l; targets.Add(c.shot.target); }
                    if (!l.Contains(m)) l.Add(m);
                }
            }
            targets.Sort(AIMap.CompareUnits);
            List<Battle> battles = new List<Battle>();
            foreach (Unit t in targets) {
                List<Menu> atk = byTarget[t];
                if (atk.Count < 2) continue;
                // Retaliation order: least taken first, artillery first on ties.
                atk.Sort((a, b) => {
                    int c = Retaliation(a, t).CompareTo(Retaliation(b, t));
                    if (c != 0) return c;
                    bool aa = !a.unit.moveAndAttack, ba = !b.unit.moveAndAttack;
                    if (aa != ba) return aa ? -1 : 1;
                    return AIMap.CompareUnits(a.unit, b.unit);
                });
                int n = Mathf.Min(atk.Count, 6);
                int full = (1 << n) - 1;
                for (int mask = 1; mask <= full; mask++) {
                    if (CountBits(mask) < 2) continue;
                    Battle b = new Battle { target = t };
                    HashSet<HexTile> claimed = new HashSet<HexTile>();
                    float left = t.Strength;
                    int tsup = t.suppressed;
                    HexTile tAt = t.tile;
                    bool ok = true;
                    for (int i = 0; i < n; i++) {
                        if ((mask & (1 << i)) == 0) continue;
                        Menu m = atk[i];
                        Candidate best = m.attacks.Find(c => c.shot.target == t);
                        HexTile stop = null;
                        Candidate pick = null;
                        if (tAt == t.tile) {
                            // Its favourite if that is an attack on this target, else its next preferred.
                            foreach (Candidate c in m.attacks) if (c.shot.target == t && !claimed.Contains(c.stop)) { pick = c; break; }
                            if (pick != null) stop = pick.stop;
                        } else {
                            // An earlier blow drove the target back: fire on where it went.
                            stop = FreshTile(m.unit, tAt, claimed, map);
                        }
                        if (stop == null || left <= 0f) { ok = false; break; }
                        claimed.Add(stop);
                        if (pick != null) b.picks.Add(pick);
                        b.steps.Add(new PlanStep { unit = m.unit, stop = stop, target = t, kind = "battle" });
                        b.sacrifice += Mathf.Max(0f, m.favourite.world - (pick ?? best).world);
                        int dmg, back;
                        CombatMath.Exchange(m.unit, m.unit.Strength, stop, t, AIWorld.Hp(left), tAt, tsup, out dmg, out back);
                        left -= dmg;
                        if (left > 0f && dmg > 0 && dmg - t.Veterancy > back - m.unit.Veterancy && t.moveRange > 0) {
                            if (CombatMath.Suppresses(m.unit)) {
                                // Indirect fire: suppressed where it stands, so the blows after it are answered less.
                                tsup += CombatMath.SuppressionFrom(AIWorld.Hp(left), tsup);
                            } else {
                                HexTile rt = AIPlan.RetreatTile(t, tAt, stop, strength, claimed);
                                if (rt == null) left -= 1f; else tAt = rt;
                            }
                        }
                    }
                    if (!ok) continue;
                    b.kills = left <= 0f;
                    if (!b.kills && (profile.coordKillsOnly || mask != full)) continue;
                    if (float.IsNaN(empty)) empty = AIPlan.Evaluate(new List<PlanStep>(), map, false, retreats).score;
                    b.joint = AIPlan.Evaluate(b.steps, map, false, retreats).score - empty;
                    float sum = 0f;
                    foreach (PlanStep st in b.steps) sum += alone(st);
                    b.synergy = b.joint - sum;
                    b.surplus = b.synergy - b.sacrifice;
                    battles.Add(b);
                }
            }

            // ---- pass 3: best plans first; no shared units, tiles or targets.
            battles.Sort((a, b) => {
                int c = b.surplus.CompareTo(a.surplus);
                return c != 0 ? c : a.steps.Count.CompareTo(b.steps.Count);
            });
            HashSet<Unit> usedUnits = new HashSet<Unit>(), usedTargets = new HashSet<Unit>();
            HashSet<HexTile> usedTiles = new HashSet<HexTile>();
            int shown = 0;
            foreach (Battle b in battles) {
                bool adopt = b.surplus > profile.coordinationPassMargin && !usedTargets.Contains(b.target);
                foreach (PlanStep s in b.steps) if (usedUnits.Contains(s.unit) || usedTiles.Contains(s.stop)) adopt = false;
                if (log != null && (adopt || shown < 5)) {
                    shown++;
                    log("     " + (adopt ? "> " : "  ") + "battle on " + N(b.target) + D(b.target.tile) + (b.kills ? " (kill)" : "")
                        + ": together " + b.joint.ToString("+0;-0") + ", synergy " + b.synergy.ToString("+0;-0")
                        + ", sacrifice " + b.sacrifice.ToString("0") + " = " + b.surplus.ToString("+0;-0") + " | "
                        + string.Join("; ", b.steps.ConvertAll(s => s.ToString()).ToArray()));
                }
                if (!adopt) continue;
                usedTargets.Add(b.target);
                int group = usedTargets.Count;
                foreach (PlanStep s in b.steps) { s.group = group; usedUnits.Add(s.unit); usedTiles.Add(s.stop); result.Add(s); }
            }

            // ---- cover swaps: a unit standing guard over a friend, or the friend stepping aside.
            if (profile.coordCoverSwaps) CoverSwaps(menus, map, profile, alone, retreats, usedUnits, usedTiles, result, log);

            // ---- rule 5: no charging in on a promise nobody made.
            foreach (Menu m in menus) {
                if (!profile.coordNoAssist || !m.favourite.assist || usedUnits.Contains(m.unit)) continue;
                noAssist.Add(m.unit);
            }
            if (log != null) {
                log("   coordination: " + (result.Count == 0 ? "no battle plan" : result.Count + " units in battle plans, acting first")
                    + (noAssist.Count > 0 ? "; deciding again without assist credit: " + string.Join(", ", new List<Unit>(noAssist).ConvertAll(x => N(x) + D(x.tile)).ToArray()) : ""));
            }
            return result;
        }

        class Swap {
            public Menu guard, ward;
            public Candidate guardAlt, wardAlt;
            public List<PlanStep> steps;
            public float joint, synergy, sacrifice, surplus;
        }

        /// <summary>
        /// Cover swaps, after the kill plans (whose units are excused: they had to be where they are).
        /// A unit whose favourite is a move with no shot near a friend (it is guarding an approach)
        /// is paired with that friend: instead of the guard holding the tile, the friend steps to a
        /// safer tile and the guard does its next best thing. Both pairs of moves are forecast
        /// together (AIPlan.Evaluate). As with kill plans, the swap is taken only for what acting
        /// together adds (its synergy: the joint difference minus each move's own difference) beyond
        /// what the two give up by their own evaluations (sacrifice). A friend that wanted to shoot
        /// from where it stands loses that shot in the sacrifice, so it is rarely asked to move.
        /// </summary>
        static void CoverSwaps(List<Menu> menus, AIMap map, AIProfile profile, Func<PlanStep, float> alone, bool retreats,
                               HashSet<Unit> usedUnits, HashSet<HexTile> usedTiles, List<PlanStep> result, Action<string> log) {
            List<Swap> swaps = new List<Swap>();
            Func<List<PlanStep>, float> joint = steps => AIPlan.Evaluate(steps, map, false, retreats).score;
            foreach (Menu g in menus) {
                if (usedUnits.Contains(g.unit) || g.favourite.shot.valid) continue;
                foreach (Menu w in menus) {
                    if (w == g || usedUnits.Contains(w.unit)) continue;
                    HexTile wAt = w.favourite.stop;
                    if (g.favourite.stop.Distance(wAt) > 2) continue;
                    float wLoss = map.ExpectedLoss(w.unit, wAt);
                    if (wLoss <= 0f) continue;   // the friend is not under threat where it means to stand
                    // The friend's safer alternatives, best by its own evaluation first.
                    List<Candidate> wAlts = w.all.FindAll(c => c != w.favourite && !usedTiles.Contains(c.stop) && map.ExpectedLoss(w.unit, c.stop) < wLoss);
                    if (wAlts.Count > 3) wAlts = wAlts.GetRange(0, 3);
                    List<Candidate> gAlts = g.all.FindAll(c => c != g.favourite && !usedTiles.Contains(c.stop));
                    if (gAlts.Count > 3) gAlts = gAlts.GetRange(0, 3);
                    if (wAlts.Count == 0 || gAlts.Count == 0) continue;
                    PlanStep wFav = Step(w.favourite), gFav = Step(g.favourite);
                    float before = joint(new List<PlanStep> { wFav, gFav });
                    float singlesBefore = alone(wFav) + alone(gFav);
                    foreach (Candidate wa in wAlts) {
                        foreach (Candidate ga in gAlts) {
                            if (ga.stop == wa.stop) continue;
                            PlanStep ws = Step(wa), gs = Step(ga);
                            ws.kind = "cover swap"; gs.kind = "cover swap";
                            Swap sw = new Swap { guard = g, ward = w, guardAlt = ga, wardAlt = wa, steps = new List<PlanStep> { ws, gs } };
                            float after = joint(sw.steps);
                            sw.joint = after - before;
                            sw.synergy = sw.joint - ((alone(ws) + alone(gs)) - singlesBefore);
                            sw.sacrifice = Mathf.Max(0f, w.favourite.world - wa.world) + Mathf.Max(0f, g.favourite.world - ga.world);
                            sw.surplus = sw.synergy - sw.sacrifice;
                            swaps.Add(sw);
                        }
                    }
                }
            }
            swaps.Sort((a, b) => b.surplus.CompareTo(a.surplus));
            int shown = 0, group = 1000;
            foreach (Swap sw in swaps) {
                // The pair's joint forecast must itself be better than the favourites', as well as the
                // surplus counting both units' own evaluations.
                bool adopt = sw.surplus > profile.coordinationPassMargin && sw.joint > 0f;
                foreach (PlanStep st in sw.steps) if (usedUnits.Contains(st.unit) || usedTiles.Contains(st.stop)) adopt = false;
                if (log != null && (adopt || shown < 3)) {
                    shown++;
                    log("     " + (adopt ? "> " : "  ") + "cover swap: " + N(sw.guard.unit) + D(sw.guard.unit.tile) + " was to guard " + N(sw.ward.unit) + D(sw.ward.unit.tile)
                        + " from " + D(sw.guard.favourite.stop) + "; instead " + string.Join("; ", sw.steps.ConvertAll(x => x.ToString()).ToArray())
                        + ": together " + sw.joint.ToString("+0;-0") + ", synergy " + sw.synergy.ToString("+0;-0") + ", sacrifice " + sw.sacrifice.ToString("0")
                        + " = " + sw.surplus.ToString("+0;-0"));
                }
                if (!adopt) continue;
                group++;
                foreach (PlanStep st in sw.steps) { st.group = group; usedUnits.Add(st.unit); usedTiles.Add(st.stop); result.Add(st); }
            }
        }

        /// <summary>A tile <paramref name="u"/> can reach this turn and fire on <paramref name="at"/> from: free, not claimed, least exposed.</summary>
        static HexTile FreshTile(Unit u, HexTile at, HashSet<HexTile> claimed, AIMap map) {
            if (!u.CanAttack(u.tile)) return null;
            int d0 = u.tile.Distance(at);
            if (d0 <= u.AttackRange && d0 > u.MinRange && !claimed.Contains(u.tile)) return u.tile;
            if (!u.moveAndAttack) return null;
            MoveField f = map.ReachThisTurn(u);
            HexTile best = null; float bestLoss = float.MaxValue;
            foreach (HexTile r in AIPath.TilesInRange(at, u.MinRange, u.AttackRange)) {
                if (r == u.tile || claimed.Contains(r) || !f.ReachableThisTurn(r) || !AIPath.CanFinishTurnOn(u, r)) continue;
                float loss = map.ExpectedLoss(u, r);
                if (best == null || loss < bestLoss || (loss == bestLoss && CombatMath.Precedes(r, best))) { best = r; bestLoss = loss; }
            }
            return best;
        }

        /// <summary>
        /// Can these steps (what is left of one battle plan, this unit's first) still kill their
        /// target on the board as it is now? Each from its planned tile if still in range, else a tile
        /// it can still reach; blows in order, the target driven back where the rule says.
        /// </summary>
        public static bool StillKills(List<PlanStep> steps, AIMap map) {
            if (steps.Count == 0) return false;
            Unit t = steps[0].target;
            if (t == null || t.tile == null || t.Strength <= 0) return false;
            float left = t.Strength;
            int tsup = t.suppressed;
            HexTile tAt = t.tile;
            HashSet<HexTile> claimed = new HashSet<HexTile>();
            Dictionary<Unit, float> strength = new Dictionary<Unit, float>();
            foreach (Unit x in map.ownUnits) if (x != null && x.tile != null) strength[x] = x.Strength;
            foreach (Unit x in map.enemyUnits) if (x != null && x.tile != null) strength[x] = x.Strength;
            foreach (PlanStep s in steps) {
                Unit u = s.unit;
                if (u == null || u.tile == null || u.Strength <= 0) continue;
                HexTile stop = s.stop;
                int d = stop != null ? stop.Distance(tAt) : 99;
                bool reachable = stop != null && (stop == u.tile || (u.moveAndAttack && map.ReachThisTurn(u).ReachableThisTurn(stop) && AIPath.CanFinishTurnOn(u, stop)));
                if (!reachable || d > u.AttackRange || d <= u.MinRange || claimed.Contains(stop)) stop = FreshTile(u, tAt, claimed, map);
                if (stop == null) continue;
                claimed.Add(stop);
                int dmg, back;
                CombatMath.Exchange(u, u.Strength, stop, t, AIWorld.Hp(left), tAt, tsup, out dmg, out back);
                left -= dmg;
                if (left <= 0f) return true;
                if (dmg > 0 && dmg - t.Veterancy > back - u.Veterancy && t.moveRange > 0 && CombatMath.Suppresses(u)) {
                    tsup += CombatMath.SuppressionFrom(AIWorld.Hp(left), tsup);   // suppressed, not driven back
                } else if (dmg > 0 && dmg - t.Veterancy > back - u.Veterancy && t.moveRange > 0) {
                    HexTile rt = AIPlan.RetreatTile(t, tAt, stop, strength, claimed);
                    if (rt == null) left -= 1f; else tAt = rt;
                    if (left <= 0f) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A planned step gone stale because its target was driven back: the same unit and target,
        /// fired on from a tile it can still reach. Null if there is none.
        /// </summary>
        public static PlanStep Refresh(PlanStep s, AIMap map) {
            if (s == null || s.unit == null || s.target == null || s.target.tile == null || s.target.Strength <= 0) return null;
            HexTile stop = FreshTile(s.unit, s.target.tile, new HashSet<HexTile>(), map);
            return stop == null ? null : new PlanStep { unit = s.unit, stop = stop, target = s.target, kind = s.kind + " (target moved)" };
        }

        static PlanStep Step(Candidate c) {
            return new PlanStep { unit = c.unit, stop = c.stop, target = c.shot.valid ? c.shot.target : null, kind = "battle" };
        }

        static float Retaliation(Menu m, Unit t) {
            foreach (Candidate c in m.attacks) if (c.shot.target == t) return c.shot.outcome.damageToAttacker;
            return 99f;
        }

        static int CountBits(int x) { int n = 0; while (x != 0) { n += x & 1; x >>= 1; } return n; }
    }
}
