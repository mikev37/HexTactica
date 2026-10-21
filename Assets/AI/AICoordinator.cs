using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Coordinated strikes in passes (AIProfile.coordinationPasses).
    ///
    ///   1. Every combat unit in contact runs its normal decision without acting, and its scored
    ///      options are kept: its coordOffensiveOptions best attacks (by forecast), its best move
    ///      that survives and keeps contact (no shot, still within a turn of the enemy), and its
    ///      best full retreat (no shot, out of the enemy's reach next turn).
    ///   2. Battle plans: for each enemy that two or more of our units have an attack on, every
    ///      group of those attackers gets one plan — each unit takes its most preferred attack on
    ///      that target whose tile no one in the group has taken (two can't share a tile; that is
    ///      why each unit keeps several). Groups whose blows kill the target are kept, and the group
    ///      of all of them. Each plan is forecast as one board (AIPlan.Evaluate: every move and shot
    ///      in order, retreats modelled, then the enemy's reply), and valued by what it adds over
    ///      the same units each doing its own best alone.
    ///   3. Plans are taken best first; a plan that shares a unit or a tile with one already taken
    ///      is dropped; until none are left. Inside a plan the units go in order of the retaliation
    ///      they take, least first: artillery (none), then tanks, then infantry.
    ///   4. Everyone else, and anyone whose planned step has gone stale (target gone, tile taken),
    ///      decides alone as usual.
    /// </summary>
    public static class AICoordinator {

        public class Candidate {
            public Unit unit;
            public HexTile stop;
            public AttackAppraisal shot;
            public float world;
        }

        class Menu {
            public Unit unit;
            public List<Candidate> attacks = new List<Candidate>();
            public Candidate survive, retreat, best;
            public List<Candidate> all;
            public float aloneGain;
        }

        class Battle {
            public Unit target;
            public List<PlanStep> steps = new List<PlanStep>();
            public float gain, alone, surplus;
            public bool kills;
            public List<PlanStep> aloneSteps;
        }

        static bool Eligible(Unit u, AIMap map) {
            if (u == null || u.tile == null || u.Strength <= 0 || !u.active || u.trasnporter != null) return false;
            if (u.Firepower <= 0 || u.GetComponent<SupplyPoint>() != null) return false;
            return map.TurnsToContact(u) <= 1;
        }

        static string D(HexTile t) { return t == null ? "-" : "(" + t.x + "," + t.y + ")"; }
        static string N(Unit u) { return u == null ? "-" : u.name.Replace("(Clone)", ""); }

        static string Show(Candidate c) {
            if (c == null) return "none";
            return D(c.stop) + (c.shot.valid ? " hit " + N(c.shot.target) + D(c.shot.target.tile) + " " + c.shot.outcome.damageToDefender + "/" + c.shot.outcome.damageToAttacker : " no shot")
                   + " world " + c.world.ToString("0");
        }

        /// <param name="decide">The unit's normal decision (its options are captured; nothing acts).</param>
        public static List<PlanStep> Plan(AIMap map, AIProfile profile, Func<Unit, AIAction> decide, Action<string> log) {
            List<PlanStep> result = new List<PlanStep>();

            // ---- pass 1: each unit's menu.
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
                    AITactical.Capture = all;
                    try { decide(u); } finally { AITactical.Capture = null; }
                    if (all.Count == 0) continue;
                    all.Sort((a, b) => {
                        int c = b.world.CompareTo(a.world);
                        return c != 0 ? c : CombatMath.CompareTiles(a.stop, b.stop);
                    });
                    Menu m = new Menu { unit = u, best = all[0], all = all };
                    foreach (Candidate c in all) {
                        if (c.shot.valid && c.shot.target != null && GameController.Targetable(c.shot.target)) {
                            if (m.attacks.Count >= profile.coordOffensiveOptions) continue;
                            if (!m.attacks.Exists(x => x.stop == c.stop && x.shot.target == c.shot.target)) m.attacks.Add(c);
                        } else {
                            int reach = map.EnemyStrikeTurns(c.stop);
                            if (reach > 1) { if (m.retreat == null) m.retreat = c; }
                            else if (m.survive == null) m.survive = c;
                        }
                    }
                    menus.Add(m);
                }
            } finally {
                AITactical.DecisionLog = savedLog;
                AITactical.Intents = savedIntents;
                AITactical.LastChosen = savedLast;
            }
            if (menus.Count < 2) return result;

            // What each unit adds alone, on the same forecast the plans use.
            bool retreats = profile.coordinationRetreats;
            float baseScore = AIPlan.Evaluate(new List<PlanStep>(), map, false, retreats).score;
            foreach (Menu m in menus) {
                List<PlanStep> one = new List<PlanStep> { Step(m.best, "alone") };
                m.aloneGain = AIPlan.Evaluate(one, map, false, retreats).score - baseScore;
            }
            if (log != null) {
                log("   coordination pass 1 (" + menus.Count + " units in contact; nothing yet with no plan: " + baseScore.ToString("0") + "):");
                foreach (Menu m in menus) {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("     ").Append(N(m.unit)).Append(D(m.unit.tile)).Append(" alone +").Append(m.aloneGain.ToString("0")).Append(':');
                    for (int i = 0; i < m.attacks.Count; i++) sb.Append(" attack").Append(i + 1).Append(' ').Append(Show(m.attacks[i])).Append(';');
                    sb.Append(" survive ").Append(Show(m.survive)).Append("; retreat ").Append(Show(m.retreat));
                    log(sb.ToString());
                }
            }

            // ---- pass 2: battle plans per target.
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
                    List<Menu> members = new List<Menu>();
                    float left = t.Strength;
                    bool ok = true;
                    for (int i = 0; i < n && ok; i++) {
                        if ((mask & (1 << i)) == 0) continue;
                        Menu m = atk[i];
                        Candidate pick = null;
                        foreach (Candidate c in m.attacks) if (c.shot.target == t && !claimed.Contains(c.stop)) { pick = c; break; }
                        if (pick == null) { ok = false; break; }
                        claimed.Add(pick.stop);
                        b.steps.Add(Step(pick, "battle"));
                        members.Add(m);
                        if (left > 0f) {
                            int dmg, back;
                            CombatMath.Exchange(m.unit, m.unit.Strength, pick.stop, t, AIWorld.Hp(left), t.tile, out dmg, out back);
                            left -= dmg;
                        }
                    }
                    if (!ok) continue;
                    b.kills = left <= 0f;
                    if (!b.kills && mask != full) continue;
                    AIWorld.Outcome w = AIPlan.Evaluate(b.steps, map, false, retreats);
                    b.gain = w.score - baseScore;
                    // The same units each doing its own favourite instead, on one board (the next
                    // favourite when a tile is already taken): what the plan has to beat.
                    List<PlanStep> alone = new List<PlanStep>();
                    HashSet<HexTile> aloneTiles = new HashSet<HexTile>();
                    foreach (Menu m in members) {
                        Candidate pick = m.all.Find(c => !aloneTiles.Contains(c.stop));
                        if (pick == null) continue;
                        if (pick.stop != m.unit.tile) aloneTiles.Add(pick.stop);
                        alone.Add(Step(pick, "alone"));
                    }
                    b.alone = AIPlan.Evaluate(alone, map, false, retreats).score - baseScore;
                    b.aloneSteps = alone;
                    b.surplus = b.gain - b.alone;
                    battles.Add(b);
                }
            }

            // ---- pass 3: best plans first, no shared units or tiles.
            battles.Sort((a, b) => {
                int c = b.surplus.CompareTo(a.surplus);
                return c != 0 ? c : a.steps.Count.CompareTo(b.steps.Count);
            });
            HashSet<Unit> usedUnits = new HashSet<Unit>();
            HashSet<HexTile> usedTiles = new HashSet<HexTile>();
            HashSet<Unit> usedTargets = new HashSet<Unit>();
            int shown = 0;
            foreach (Battle b in battles) {
                bool adopt = b.surplus > profile.coordinationPassMargin && !usedTargets.Contains(b.target);
                foreach (PlanStep s in b.steps) if (usedUnits.Contains(s.unit) || usedTiles.Contains(s.stop)) adopt = false;
                if (log != null && (adopt || shown < 6)) {
                    shown++;
                    log("     " + (adopt ? "> " : "  ") + "battle on " + N(b.target) + D(b.target.tile) + (b.kills ? " (kill)" : "") + ": gain " + b.gain.ToString("0")
                        + " v alone " + b.alone.ToString("0") + " = " + b.surplus.ToString("+0;-0") + " | "
                        + string.Join("; ", b.steps.ConvertAll(s => s.ToString()).ToArray()));
                    if (adopt) {
                        log("         plan forecast: " + AIPlan.Evaluate(b.steps, map, true, retreats).Summary());
                        log("         alone (" + string.Join("; ", b.aloneSteps.ConvertAll(s => s.ToString()).ToArray()) + "): " + AIPlan.Evaluate(b.aloneSteps, map, true, retreats).Summary());
                        log("         nobody acts: " + AIPlan.Evaluate(new List<PlanStep>(), map, true, retreats).Summary());
                    }
                }
                if (!adopt) continue;
                usedTargets.Add(b.target);
                foreach (PlanStep s in b.steps) { usedUnits.Add(s.unit); usedTiles.Add(s.stop); result.Add(s); }
            }
            if (log != null) log("   coordination: " + (result.Count == 0 ? "no battle plan; units decide one at a time" : result.Count + " units in battle plans, acting first"));
            return result;
        }

        static PlanStep Step(Candidate c, string kind) {
            return new PlanStep { unit = c.unit, stop = c.stop, target = c.shot.valid ? c.shot.target : null, kind = kind };
        }

        static float Retaliation(Menu m, Unit t) {
            foreach (Candidate c in m.attacks) if (c.shot.target == t) return c.shot.outcome.damageToAttacker;
            return 99f;
        }

        static int CountBits(int x) { int n = 0; while (x != 0) { n += x & 1; x >>= 1; } return n; }
    }
}
