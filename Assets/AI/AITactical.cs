using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Per-role movement and action selection.
    ///
    /// The old AIPlayer had exactly one decider, AttackMove, applied to everything
    /// including supply trucks. Different roles answer different questions: a howitzer
    /// wants contested ground it can shoot into and nothing can shoot back from, a hub
    /// wants sustainment coverage, a wounded squad wants to get somewhere it will heal.
    ///
    /// Every role shares one positional loop (Choose) and one currency (gold, via
    /// AIScore). Roles differ only in how they value a destination. Two things the loop
    /// gets right that the per-role loops did not:
    ///
    ///   - A multi-turn destination is valued as a plan, but this turn's *stopping tile*
    ///     is what gets attacked tonight, so its risk is charged separately. Scoring the
    ///     destination alone let a unit stop in a killzone on the way to a safe tile.
    ///   - Moving through threatened ground costs nothing in this ruleset — enemies attack
    ///     on their own turn, wherever you ended yours — so there is no route-exposure term.
    /// </summary>
    public static class AITactical {

        /// <summary>Front and objective are optional; both simply add positional bias.</summary>
        public static AIAction Decide(Unit unit, AIMap map, AIProfile profile, Posture posture) {
            return Decide(unit, map, profile, posture, null, null);
        }

        public static AIAction Decide(Unit unit, AIMap map, AIProfile profile, Posture posture,
                                      Front front, AIMissionObjective objective) {
            if (unit == null || unit.tile == null || unit.Strength <= 0) {
                return AIAction.Wait(unit, "no unit");
            }
            if (unit.trasnporter != null) {
                return AIAction.Wait(unit, "aboard transport");
            }

            context.map = map;
            context.profile = profile;
            context.front = front;
            context.objective = objective;
            context.posture = posture;

            if (unit.airTanker) return AIAir.DecideTanker(unit, map);
            AIRole role = AIRoles.Classify(unit, profile);
            if (role == AIRole.Wounded && profile.woundedNeedsSafety) return DecideWounded(unit, map, profile, posture);
            if (role == AIRole.Line && ShouldRotate(unit, map, profile)) role = AIRole.Wounded;

            switch (role) {
                case AIRole.Artillery: return DecideArtillery(unit, map, profile, posture);
                case AIRole.Wounded:   return DecideWithdraw(unit, map, profile);
                case AIRole.Recon:     return DecideRecon(unit, map, profile, posture);
                case AIRole.AntiAir:   return DecideAntiAir(unit, map, profile, posture);
                default:               return DecideLine(unit, map, profile, posture);
            }
        }

        /// <summary>
        /// Rotation: a damaged line unit near supply pulls back to heal when a healthier unit is
        /// close enough to take its place, instead of fighting on at half strength until it is
        /// wounded or dead. Damage scales with strength, so a four-strength grunt hits for two
        /// where a fresh one hits for three, and draws the same fire. A kill in hand is still
        /// taken first (DecideWithdraw does that). The threshold falls as aggression rises.
        /// </summary>
        static bool ShouldRotate(Unit unit, AIMap map, AIProfile profile) {
            int threshold = Mathf.RoundToInt(profile.rotateStrength * profile.Aggro(1.25f, 0.5f));
            if (unit.Strength > threshold || unit.Strength <= profile.withdrawStrength) return false;
            if (map.TurnsToSupply(unit, unit.tile) > 1) return false;
            int healthy = Mathf.CeilToInt(6 * profile.readyStrength);
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit v = map.ownUnits[i];
                if (v == null || v == unit || v.tile == null || v.trasnporter != null) continue;
                if (v.Firepower <= 0 || v.GetComponent<SupplyPoint>() != null || v.Strength < healthy) continue;
                if (v.AttackRange >= 2 && !v.moveAndAttack) continue;      // guns do not hold a line
                if (v.tile.Distance(unit.tile) <= v.moveRange + 1) return true;
            }
            return false;
        }

        /// <summary>Per-decision context, so the scorers do not need six extra parameters.</summary>
        class Context {
            public AIMap map;
            public AIProfile profile;
            public Front front;
            public AIMissionObjective objective;
            public Posture posture;
            public Dictionary<Unit, HexTile> intents;
        }

        /// <summary>
        /// Set to a unit to have its next positional decision explained term by term
        /// through <see cref="DebugLog"/> — top candidates with value, supply, delay, risk,
        /// cover, supply cost and commitment. Diagnostic only.
        /// </summary>
        public static Unit DebugUnit;
        public static Action<string> DebugLog;
        /// <summary>Also print the value breakdown for every candidate (very verbose).</summary>
        public static bool DebugBreakdown;

        /// <summary>
        /// Where each unit was heading last turn, owned by the caller (AIPlayer) so it
        /// persists across turns. Optional: without it there is simply no commitment.
        /// </summary>
        /// <summary>Each unit's objective this turn (AIObjectives), owned by the caller. Optional.</summary>
        public static Dictionary<Unit, Objective> Assignments;

        public static Dictionary<Unit, HexTile> Intents {
            get { return context.intents; }
            set { context.intents = value; }
        }
        static readonly Context context = new Context();

        /// <summary>When set, Choose records the best score reachable through each of this turn's stops.</summary>
        static Dictionary<HexTile, KeyValuePair<float, HexTile>> stopCapture;

        /// <summary>
        /// Per-decision reasoning, one block per unit: every tier's survivors with their numbers,
        /// and the choice. Set by the harness; null in the game.
        /// </summary>
        public static Action<string> DecisionLog;

        // ------------------------------------------------------------ tiered choice

        class Option {
            public HexTile stop;
            public HexTile destination;
            public AttackAppraisal shot;
            public float attack;
            public float received;
            public float positional;
            public AIDenial.Result denial;
            public float rest;
            public float hqThreat;
            public AIWorld.Outcome world;
        }

        /// <summary>
        /// Choose this turn's move in tiers, the way the original AttackMove did, instead of adding
        /// every consideration into one number. Attacking first is the biggest advantage in this
        /// ruleset, and in a single sum any positional term — footwork, screens, an objective's
        /// pull — could outbid a shot. Here nothing can:
        ///
        ///   0. Our HQ: drop every option that would leave it destroyable on the enemy's coming turn,
        ///      if any option would not; if none would not, keep those leaving it least exposed
        ///      (AIDenial.HQThreat). Losing the HQ loses everything.
        ///   1. Damage dealt: every stop and the best attack from it. Keep the options whose attack
        ///      is at least attackTie of the best (if anything can attack at all). Fuzzy, so that a
        ///      slightly smaller hit from a much safer tile survives to tier 2.
        ///   2. Damage received: expected loss tonight at that tile, plus any retaliation, in the
        ///      acting unit's strength points. Keep those within receivedTie of the least.
        ///   3. Everything else, in gold: enemy movement and supply denied by standing there
        ///      (AIDenial — measured by moving the unit on the board and recomputing), plus the
        ///      positional score (approach toward the best multi-turn destination, supply, cover,
        ///      strategy, counterattack, commitment). Highest wins; exact ties on board position.
        ///
        /// When nothing is in reach, tier 1 keeps everything, tier 2 keeps the safe tiles, and tier
        /// 3 picks among them — which is what puts units on chokepoints while the enemy is still
        /// out of range, and walks them forward when it is not.
        /// </summary>
        static AIAction Tiered(Unit unit, AIMap map, AIProfile profile, Posture posture,
                               Dictionary<HexTile, KeyValuePair<float, HexTile>> stops,
                               Func<HexTile, bool> canShootFrom, Func<AttackAppraisal, bool> shotAllowed,
                               float shotRiskFactor, string tag) {
            float worth = AIScore.Worth(unit);
            List<Option> options = new List<Option>();
            if (!stops.ContainsKey(unit.tile)) stops[unit.tile] = new KeyValuePair<float, HexTile>(float.NegativeInfinity, unit.tile);
            // Aircraft and ships: only stops they can still get home from (bingo fuel). Nobody ends on a
            // tile reserved for an aircraft's landing this turn.
            AIAir.FilterSafe(unit, map, stops);
            AIAir.FilterReserved(unit, map, stops);
            foreach (KeyValuePair<HexTile, KeyValuePair<float, HexTile>> kv in stops) {
                HexTile s = kv.Key;
                if (s != unit.tile && !AIPath.CanFinishTurnOn(unit, s)) continue;
                if (profile.choiceMode == 2) {
                    // Every choice from this tile goes to the simulation: standing without firing,
                    // and each shot it could take — not just the one the attack score likes best.
                    options.Add(new Option { stop = s, destination = kv.Value.Value, positional = kv.Value.Key });
                    if (canShootFrom(s)) {
                        foreach (AttackAppraisal a in AITargeting.AllFrom(unit, s, map, profile, posture, true)) {
                            if (!shotAllowed(a)) continue;
                            options.Add(new Option { stop = s, destination = kv.Value.Value, positional = kv.Value.Key, shot = a, attack = a.value });
                        }
                    }
                    continue;
                }
                Option o = new Option { stop = s, destination = kv.Value.Value, positional = kv.Value.Key };
                if (canShootFrom(s)) {
                    AttackAppraisal a = AITargeting.BestFrom(unit, s, map, profile, posture, true);
                    if (a.valid && a.value > 0f && shotAllowed(a)) { o.shot = a; o.attack = a.value; }
                    else if (DecisionLog != null && s == unit.tile && a.valid)
                        DecisionLog(string.Format("{0} {1}{2}: shot at {3}{4} rejected, value {5:0} (dealt {6}, taken {7})",
                            unit.owner.personal_name, unit.name, D(s), a.target.name, D(a.target.tile), a.value, a.outcome.damageToDefender, a.outcome.damageToAttacker));
                }
                Unit killed = o.shot.valid && o.shot.outcome.defenderDies ? o.shot.target : null;
                o.received = map.ExpectedLoss(unit, s, killed) * shotRiskFactor
                           + (o.shot.valid ? o.shot.outcome.damageToAttacker : 0);
                // An attack only counts as one if it is worth more than what standing there tonight
                // will cost. Otherwise a lone tank's only shot — a grunt, from a tile the whole enemy
                // army could reach — won tier 1 by default over every safe tile, and the tank was
                // destroyed for it on turn one. (Losing the unit outright costs the kill bonus too.)
                // Judged in material, both sides: attack values carry the target's threat premium
                // (Unit.value), which near an HQ inflated a tank to six times its cost, and a tank
                // drove to a tile where it was certain to die to scratch it.
                bool dies = o.received >= unit.Strength;
                float cost = Mathf.Min(unit.Strength, o.received) * worth * (dies ? profile.killBonus : 1f);
                float gain = 0f;
                if (o.shot.valid) {
                    gain = o.shot.outcome.damageToDefender * AIScore.Worth(o.shot.target) * (o.shot.outcome.defenderDies ? profile.killBonus : 1f);
                    if (AITargeting.IsHQUnit(o.shot.target)) gain = Mathf.Max(gain, o.attack);
                }
                // Exempt: anything threatening our HQ. Weakening it is worth what the HQ is worth,
                // which tier 0 judges.
                if (profile.choiceMode != 2 && o.shot.valid && gain <= cost && map.HQThreat(o.shot.target) <= 0f) {
                    if (DecisionLog != null && s == unit.tile)
                        DecisionLog(string.Format("{0} {1}{2}: shot at {3}{4} not worth it here: gain {5:0} <= cost {6:0} (recv {7:0.0})",
                            unit.owner.personal_name, unit.name, D(s), o.shot.target.name, D(o.shot.target.tile), gain, cost, o.received));
                    o.shot = new AttackAppraisal();
                    o.attack = 0f;
                    o.received = map.ExpectedLoss(unit, s, null) * shotRiskFactor;
                }
                options.Add(o);
            }

            if (profile.choiceMode == 2) return ByWorld(unit, map, profile, posture, options, tag);

            // Tier 0: the HQ. If the enemy could destroy our HQ on its coming turn, nothing else
            // matters: keep only the options that leave it the least exposed — killing or crippling
            // an attacker, or standing on (or in the way of) the tiles they would strike it from.
            int hqStrength;
            float hqNow = AIDenial.HQThreat(unit, unit.tile, null, 0, map, out hqStrength);
            List<Option> t0 = options;
            bool hqLethal = hqStrength > 0 && hqNow >= hqStrength;
            if (hqStrength > 0 && hqNow > 0f) {
                foreach (Option o in options) {
                    int dmg = o.shot.valid ? o.shot.outcome.damageToDefender : 0;
                    int ignore;
                    o.hqThreat = AIDenial.HQThreat(unit, o.stop, o.shot.valid ? o.shot.target : null, dmg, map, out ignore);
                }
                // Never leave the HQ killable when something else would not — whether it is in
                // danger already or this move would open the way (a grunt stepping off the one
                // approach it was blocking).
                List<Option> safe = options.FindAll(o => o.hqThreat < hqStrength);
                if (safe.Count > 0 && safe.Count < options.Count) t0 = safe;
                else if (safe.Count == 0) {
                    float leastHq = float.PositiveInfinity;
                    foreach (Option o in options) leastHq = Mathf.Min(leastHq, o.hqThreat);
                    t0 = options.FindAll(o => o.hqThreat <= leastHq + 0.5f);
                }
            }

            float bestAttack = 0f, least = float.PositiveInfinity;
            List<Option> t1, t2;
            if (profile.choiceMode == 1) {
                // Survival first: least damage received, then the best attack among those.
                foreach (Option o in t0) least = Mathf.Min(least, o.received);
                t1 = t0.FindAll(o => o.received <= least + profile.receivedTie);
                foreach (Option o in t1) bestAttack = Mathf.Max(bestAttack, o.attack);
                t2 = bestAttack > 0f ? t1.FindAll(o => o.attack >= bestAttack * profile.attackTie && o.attack > 0f) : new List<Option>(t1);
            } else {
                // Tier 1.
                foreach (Option o in t0) bestAttack = Mathf.Max(bestAttack, o.attack);
                t1 = bestAttack > 0f
                    ? t0.FindAll(o => o.attack >= bestAttack * profile.attackTie && o.attack > 0f)
                    : new List<Option>(t0);

                // Tier 2.
                foreach (Option o in t1) least = Mathf.Min(least, o.received);
                t2 = t1.FindAll(o => o.received <= least + profile.receivedTie);
            }

            // Tier 3.
            Option best = null;
            foreach (Option o in t2) {
                o.denial = map.Denial(unit, o.stop);
                float positional = float.IsNegativeInfinity(o.positional) ? 0f : o.positional;
                // Within the tier the attack still counts in gold, so a better hit from an equally
                // safe tile is preferred to a weaker one.
                // Short of lethal, taking threat off the HQ is still worth its share of the game.
                float hqRelief = hqStrength > 0 ? (hqNow - o.hqThreat) / (hqStrength + 1f) * map.BoardStake * profile.hqDefenseWeight * 0.25f : 0f;
                o.rest = o.denial.gold + positional + o.attack + hqRelief;
                if (best == null || o.rest > best.rest || (o.rest == best.rest && CombatMath.Precedes(o.stop, best.stop))) best = o;
            }

            if (DecisionLog != null) {
                if (hqStrength > 0 && hqNow > 0f)
                    DecisionLog(string.Format("{0} {1}{2}: HQ threat {3:0.0} vs strength {4}{5}", unit.owner.personal_name, unit.name, D(unit.tile), hqNow, hqStrength,
                        (hqLethal ? " LETHAL" : "") + (t0.Count < options.Count ? " -> T0 keeps " + t0.Count + " of " + options.Count : "")));
                LogTiers(unit, tag, posture, options, t1, t2, best, bestAttack, least, profile);
            }

            if (context.intents != null) {
                if (best != null && best.destination != null && best.destination != unit.tile) context.intents[unit] = best.destination;
                else context.intents.Remove(unit);
            }

            if (best == null) return AIAction.Wait(unit, tag + ": no option");
            string why = tag + string.Format(": tiers {0}/{1}/{2}", options.Count, t1.Count, t2.Count);
            if (best.shot.valid) {
                if (best.stop == unit.tile) return AIAction.Strike(unit, best.shot.target, best.rest, why + " strike");
                return AIAction.MoveStrike(unit, best.stop, best.shot.target, best.rest, why + " move+strike");
            }
            if (best.stop == unit.tile) { AIAction hold = AIAction.Wait(unit, why + " hold"); hold.score = best.rest; return hold; }
            return AIAction.Move(unit, best.stop, best.rest, why + " move");
        }

        /// <summary>
        /// Choice by simulated outcome (choiceMode 2): every option is played out against the
        /// enemy's reply (AIWorld) and scored by what is left on both sides. Options within
        /// worldTie of this unit's strength points of the best world survive; among them the
        /// highest denial + positional score wins (approach, supply, strategy, commitment).
        /// </summary>
        /// <summary>The forecast of the option most recently chosen by the world-score decider (for diagnostics).</summary>
        public static AIWorld.Outcome LastChosen;

        /// <summary>When set, the world-score decider adds every option it scored here (AICoordinator's first pass).</summary>
        public static List<AICoordinator.Candidate> Capture;

        static AIAction ByWorld(Unit unit, AIMap map, AIProfile profile, Posture posture, List<Option> options, string tag) {
            float worth = AIScore.Worth(unit);
            AIWorld.Cache cache = map.replyCache;
            float bestWorld = float.NegativeInfinity;
            foreach (Option o in options) {
                o.world = AIWorld.Evaluate(unit, o.stop, o.shot, map, cache, DecisionLog != null, true);
                // Capturing buildings (and the threat to ours) is in the world score (AIWorld.Buildings).
                // Runways and docks are kept clear for the aircraft and ships that land there.
                o.world.score -= AIAir.RunwayBlockCost(unit, o.stop, map);
                bestWorld = Mathf.Max(bestWorld, o.world.score);
            }
            if (Capture != null) foreach (Option o in options) Capture.Add(new AICoordinator.Candidate { unit = unit, stop = o.stop, shot = o.shot, world = o.world.score, assist = o.world.assist });
            // The tie margin: a fraction of a strength point, but never less than worldTieMin —
            // a quarter of a grunt's point (83) treated real differences as noise in reverse:
            // tiny ones decided everything.
            float tie = Mathf.Max(profile.worldTieMin, profile.worldTie * worth);
            List<Option> kept = options.FindAll(o => o.world.score >= bestWorld - tie);
            Option best = null;
            Dictionary<HexTile, AIDenial.Result> denial = new Dictionary<HexTile, AIDenial.Result>();
            foreach (Option o in kept) {
                AIDenial.Result d;
                if (!denial.TryGetValue(o.stop, out d)) { d = map.Denial(unit, o.stop); denial[o.stop] = d; }
                o.denial = d;
                float positional = float.IsNegativeInfinity(o.positional) ? 0f : o.positional;
                o.rest = o.denial.gold + positional;
                // Same tile, same position: the better simulated world (shoot or not, and at whom) decides.
                if (best == null || o.rest > best.rest
                    || (o.rest == best.rest && o.world.score > best.world.score)
                    || (o.rest == best.rest && o.world.score == best.world.score && CombatMath.Precedes(o.stop, best.stop))) best = o;
            }

            if (DecisionLog != null) {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append(unit.owner.personal_name).Append(' ').Append(unit.name).Append(D(unit.tile)).Append(" s").Append(unit.Strength)
                  .Append(" [").Append(tag).Append(", world] ").Append(options.Count).Append(" options | best world ").Append(bestWorld.ToString("0"))
                  .Append(", within ").Append(tie.ToString("0")).Append(": ").Append(kept.Count);
                List<Option> shown = new List<Option>(options);
                shown.Sort((a, b) => b.world.score.CompareTo(a.world.score));
                for (int i = 0; i < Mathf.Min(5, shown.Count); i++) {
                    Option o = shown[i];
                    sb.Append("\n      ").Append(o == best ? "> " : kept.Contains(o) ? "  " : "x ").Append(D(o.stop))
                      .Append(o.shot.valid ? " hit " + o.shot.target.name + D(o.shot.target.tile) + " " + o.shot.outcome.damageToDefender + "/" + o.shot.outcome.damageToAttacker : " no shot")
                      .Append(" world ").Append(o.world.Summary());
                    if (kept.Contains(o)) sb.Append(" | deny ").Append(o.denial.gold.ToString("0")).Append(" pos ").Append(float.IsNegativeInfinity(o.positional) ? "-" : o.positional.ToString("0"));
                }
                if (best != null && !shown.GetRange(0, Mathf.Min(5, shown.Count)).Contains(best))
                    sb.Append("\n      > ").Append(D(best.stop)).Append(" world ").Append(best.world.Summary()).Append(" | pos ").Append(best.rest.ToString("0"));
                DecisionLog(sb.ToString());
            }

            if (context.intents != null) {
                if (best != null && best.destination != null && best.destination != unit.tile) context.intents[unit] = best.destination;
                else context.intents.Remove(unit);
            }
            if (best == null) return AIAction.Wait(unit, tag + ": no option");
            LastChosen = best.world;
            string why = tag + ": world " + best.world.score.ToString("0");
            if (best.shot.valid) {
                if (best.stop == unit.tile) return AIAction.Strike(unit, best.shot.target, best.rest, why + " strike");
                return AIAction.MoveStrike(unit, best.stop, best.shot.target, best.rest, why + " move+strike");
            }
            if (best.stop == unit.tile) { AIAction hold = AIAction.Wait(unit, why + " hold"); hold.score = best.rest; return hold; }
            return AIAction.Move(unit, best.stop, best.rest, why + " move");
        }

        static string D(HexTile t) { return t == null ? "-" : "(" + t.x + "," + t.y + ")"; }

        static void LogTiers(Unit unit, string tag, Posture posture, List<Option> all, List<Option> t1, List<Option> t2,
                             Option best, float bestAttack, float least, AIProfile profile) {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(unit.owner.personal_name).Append(' ').Append(unit.name).Append(D(unit.tile))
              .Append(" s").Append(unit.Strength).Append(" [").Append(tag).Append(", ").Append(posture).Append("] ")
              .Append(all.Count).Append(" stops");
            sb.Append(bestAttack > 0f
                ? string.Format(" | T1 attack>={0:0} ({1:0}% of {2:0}): {3}", bestAttack * profile.attackTie, profile.attackTie * 100, bestAttack, t1.Count)
                : " | T1 nothing in reach: " + t1.Count);
            sb.Append(string.Format(" | T2 received<={0:0.0}: {1}", least + profile.receivedTie, t2.Count));
            List<Option> shown = new List<Option>(t2);
            shown.Sort((a, b) => b.rest.CompareTo(a.rest));
            for (int i = 0; i < Mathf.Min(4, shown.Count); i++) {
                Option o = shown[i];
                sb.Append("\n      ").Append(o == best ? "> " : "  ").Append(D(o.stop))
                  .Append(o.shot.valid ? " hit " + o.shot.target.name + D(o.shot.target.tile) + " " + o.attack.ToString("0") : " no shot")
                  .Append(" recv ").Append(o.received.ToString("0.0"))
                  .Append(" deny ").Append(o.denial.tiles).Append("mv/").Append(o.denial.supplyTiles).Append("sup/").Append(o.denial.unitsCut).Append("cut=").Append(o.denial.gold.ToString("0"))
                  .Append(" pos ").Append(float.IsNegativeInfinity(o.positional) ? "-" : o.positional.ToString("0"))
                  .Append(o.hqThreat > 0f ? " hq " + o.hqThreat.ToString("0.0") : "")
                  .Append(" toward ").Append(D(o.destination))
                  .Append(" => ").Append(o.rest.ToString("0"));
            }
            // The best option that tier 1 or 2 threw out, so the cost of the filter is visible.
            Option dropped = null;
            foreach (Option o in all) if (!t2.Contains(o) && (dropped == null || o.positional > dropped.positional)) dropped = o;
            if (dropped != null) sb.Append("\n      x ").Append(D(dropped.stop)).Append(" dropped at ")
                .Append(t1.Contains(dropped) ? "T2 (recv " + dropped.received.ToString("0.0") + ")" : "T1 (attack " + dropped.attack.ToString("0") + ")")
                .Append(" pos ").Append(float.IsNegativeInfinity(dropped.positional) ? "-" : dropped.positional.ToString("0"));
            DecisionLog(sb.ToString());
        }

        // -------------------------------------------------------------- shared loop

        /// <summary>
        /// Pick the best destination over the planning horizon and return the legal move
        /// for this turn. <paramref name="valueOf"/> returns a destination's worth in gold,
        /// before delay, risk and (when <paramref name="applySupply"/>) supply cost are
        /// applied here uniformly.
        /// </summary>
        static AIAction Choose(Unit unit, AIMap map, AIProfile profile,
                               Func<HexTile, float> valueOf,
                               bool applySupply, float riskFactor, float stayFactor, string tag, bool screens = true) {

            MoveField horizon = map.ReachHorizon(unit);
            HexTile bestTile = null, bestStop = null;
            float bestScore = float.NegativeInfinity;
            bool explain = DebugUnit == unit && DebugLog != null;
            List<KeyValuePair<float, string>> explained = explain ? new List<KeyValuePair<float, string>>() : null;

            foreach (HexTile t in horizon.Tiles) {
                if (!AIPath.CanFinishTurnOn(unit, t)) continue;

                Eta eta = horizon.Get(t);
                HexTile stop = eta.ThisTurn ? t : horizon.StepToward(t);
                if (stop == null) continue;

                // Supply is priced as a cost — escalating per-turn attrition pressure at tonight's
                // stop and at the destination — not as a multiplier on value. The multiplier
                // predated the single currency: it scaled a destination's entire worth down to
                // 5% at depth zero, and stacked with the per-turn cost it made one tile past the
                // supply edge worthless whatever was there. The line could never step past the
                // edge, and nothing pulled the edge forward.
                float raw = valueOf(t);
                float score = raw;
                float supplied = score;
                score = AIScore.Delay(score, eta.turns, profile);
                float delayed = score;

                // Commitment: continuing toward last turn's destination is worth something in
                // itself. Re-planning from scratch every turn let two destinations trade places
                // — a tank drove six hexes east one turn and six back the next, indefinitely,
                // while its supplies ran down. A new plan now has to beat the old by a margin.
                HexTile intent;
                if (context.intents != null && context.intents.TryGetValue(unit, out intent) && intent != null) {
                    int d = t.Distance(intent);
                    if (d < 3) score += AIScore.Worth(unit) * profile.commitment * (1f - d / 3f);
                }

                // Tonight's exposure is where the unit actually stops. Posture sets the appetite
                // for it: "aggressive fronts attempt to go deeper", "defensive fronts prefer not
                // to attack". Without this, two equal armies each priced the first step into the
                // other's reach as pure loss, and every game on the shipped roster was a standoff.
                float appetite = riskFactor * PostureRisk(profile);
                score -= AIScore.Risk(unit, stop, map, profile) * appetite;
                score -= map.CohesionCost(unit, stop);
                // The other side of tonight's risk: the counterattack it invites and the fire it
                // draws off friends (AIMap.FirstStrikeCredit).
                // Not for a unit that is not there to fight (an empty transport): drawing fire and
                // blocking the way are a fighting unit's work, and credited to an APC they drove it
                // to the front line, where it was shot to pieces with nobody aboard.
                if (screens) {
                    score += map.FirstStrikeCredit(unit, stop);
                    // And the space it makes for the armour still to move (AISpace).
                    score += map.SpaceDenial(unit, stop);
                }

                // And the destination's exposure, since standing there is the point of going.
                // Charging only tonight's stop made deep plans look free until the turn they
                // were about to be paid for, when they collapsed into a retreat: a tank drove
                // six hexes toward the enemy HQ and six back, every turn.
                if (stop != t) {
                    float later = Mathf.Max(0.5f, 1f - profile.turnPenalty * eta.turns);
                    score -= AIScore.Risk(unit, t, map, profile) * appetite * later;
                    if (applySupply) score -= AIScore.OutOfSupplyCost(unit, t, map) * later;
                }
                // Whether it can get home before it starves.
                if (applySupply) score -= AIScore.StrandingCost(unit, t, eta.turns, map);
                score += AIScore.CoverPreference(unit, stop, map, profile);
                // And tonight's supply: camping outside it every night en route is a real cost
                // even when the destination itself is supplied.
                if (applySupply) score -= AIScore.OutOfSupplyCost(unit, stop, map);
                if (t == unit.tile) score += AIScore.StayBonus(unit, stayFactor);

                if (explain) {
                    explained.Add(new KeyValuePair<float, string>(score, string.Format(
                        "  ({0},{1}) T+{2} stop({3},{4})  value {5:0} supplied {6:0} delayed {7:0}  risk -{8:0}  supplyCost -{9:0}  => {10:0}",
                        t.x, t.y, eta.turns, stop.x, stop.y, raw, supplied, delayed,
                        AIScore.Risk(unit, stop, map, profile) * riskFactor * PostureRisk(profile)
                            + (stop != t ? AIScore.Risk(unit, t, map, profile) * riskFactor * PostureRisk(profile) : 0f),
                        applySupply ? AIScore.OutOfSupplyCost(unit, stop, map) + AIScore.StrandingCost(unit, t, eta.turns, map) : 0f, score)));
                }

                if (stopCapture != null) {
                    KeyValuePair<float, HexTile> prior;
                    if (!stopCapture.TryGetValue(stop, out prior) || score > prior.Key ||
                        (score == prior.Key && CombatMath.Precedes(t, prior.Value)))
                        stopCapture[stop] = new KeyValuePair<float, HexTile>(score, t);
                }

                if (score > bestScore ||
                    (score == bestScore && bestTile != null && CombatMath.Precedes(t, bestTile))) {
                    bestScore = score;
                    bestTile = t;
                    bestStop = stop;
                }
            }

            if (explain) {
                explained.Sort((x, y) => y.Key.CompareTo(x.Key));
                DebugLog(unit.name + " at (" + unit.tile.x + "," + unit.tile.y + ") " + tag + ", posture " + context.posture
                         + ", intent " + (context.intents != null && context.intents.ContainsKey(unit) && context.intents[unit] != null
                             ? "(" + context.intents[unit].x + "," + context.intents[unit].y + ")" : "none"));
                for (int i = 0; i < Mathf.Min(6, explained.Count); i++) DebugLog(explained[i].Value);
            }

            if (context.intents != null) {
                if (bestTile != null && bestTile != unit.tile) context.intents[unit] = bestTile;
                else context.intents.Remove(unit);
            }

            if (bestStop == null || bestStop == unit.tile) {
                AIAction hold = AIAction.Wait(unit, tag + ": holding");
                hold.score = bestScore;
                return hold;
            }
            return AIAction.Move(unit, bestStop, bestScore, tag + (bestStop == bestTile ? ": move" : ": advance"));
        }

        /// <summary>
        /// Shoot only if the shot (already net of the risk of where it is fired from) beats
        /// the best positional alternative. "Positive" is not the bar: a unit standing in a
        /// killzone with a mediocre shot should pull back, not fire and stay.
        /// </summary>
        static AIAction Better(AttackAppraisal shot, AIAction positional, Unit unit, string inPlace, string onMove) {
            if (!shot.valid || shot.value <= 0) return positional;
            // Holding and firing in place adds the shot to holding, so it always beats holding.
            bool firesInPlace = shot.from == unit.tile;
            bool positionalHolds = positional.kind == AIActionKind.Wait;
            if (!(positionalHolds && firesInPlace) && positional.score > shot.value) return positional;
            if (shot.from == unit.tile) return AIAction.Strike(unit, shot.target, shot.value, inPlace);
            return AIAction.MoveStrike(unit, shot.from, shot.target, shot.value, onMove);
        }

        static float PostureRisk(AIProfile profile) {
            return AIScore.PostureRisk(profile, context.posture);
        }

        // ---------------------------------------------------------- strategic pull

        /// <summary>
        /// Positional bias from the mission objective and from the front's objective, in
        /// gold. Objectives bias candidates the tactical layer already produced rather than
        /// seizing control of units. Without this pull, two competent armies fought to a
        /// standstill: nothing in the evaluator wanted the win condition.
        /// </summary>
        static float StrategicBonus(Unit unit, HexTile tile, Posture posture) {
            float worth = AIScore.Worth(unit);
            float bonus = 0f;

            if (context.objective != null) {
                bonus += context.objective.TileBonus(unit, tile, context.map) * worth;
            }

            // The strategic objective, if this unit has one (only units out of contact do): a
            // tie-breaker on top of everything else, evaluated last.
            Objective job;
            if (Assignments != null && Assignments.TryGetValue(unit, out job) && job.tile != null) {
                bonus += worth * context.profile.assignmentPull / (1 + context.map.Travel(unit, tile, job.tile));
            }

            Front front = context.front;
            if (front != null && front.objectiveTile != null) {
                int d = context.map.Travel(unit, tile, front.objectiveTile);
                bool building = front.objective != null;
                bool canTake = building && Building.CanCapture(unit);

                // The objective's own value, not the acting unit's: scaling the pull by the
                // unit's worth made capture nearly worthless to cheap infantry, which are
                // the only units that can capture.
                float pull = front.objectiveValue * context.profile.objectivePull;
                if (building && !canTake) {
                    // Escort rather than seize — if anyone is left who can seize it. A tank with no
                    // infantry left parked on the enemy HQ it could not take until it starved.
                    bool anyCapturer = false;
                    for (int i = 0; i < context.map.ownUnits.Count && !anyCapturer; i++) {
                        Unit v = context.map.ownUnits[i];
                        if (v != null && v.tile != null && Building.CanCapture(v)) anyCapturer = true;
                    }
                    pull *= anyCapturer ? 0.35f : 0f;
                }
                if (posture == Posture.Defensive) pull *= 0.45f;
                if (front.siege && posture == Posture.Aggressive) pull *= context.profile.siegePull;
                bonus += pull / (1 + d);

                if (canTake && tile == front.objectiveTile) bonus += front.objectiveValue * 0.5f;
                // A unit that cannot capture must not stand on the building: it blocks the one that can.
                // (A tank parked on the enemy HQ for four turns while three rifles walked around it.)
                if (building && !canTake && tile == front.objectiveTile) bonus -= worth * unit.Strength;
            }
            // March: out of contact, every step over land toward the objective is worth the same, so
            // the army walks up a long road to the edge of its supply instead of feeling a pull that
            // fades to nothing with distance.
            if (context.profile.marchWeight > 0f && front != null && front.objectiveTile != null && unit.tile != null
                && unit.GetComponent<SupplyPoint>() == null && context.map.TurnsToContact(unit) > context.profile.objectiveContactTurns) {
                int gained = context.map.Travel(unit, unit.tile, front.objectiveTile) - context.map.Travel(unit, tile, front.objectiveTile);
                bonus += worth * context.profile.marchWeight * gained;
            }
            // Regrouping for an attack: gather on the edge of our supply nearest the objective.
            if (front != null && front.staging && front.stagingTile != null) {
                bonus += worth * context.profile.stagingPull / (1 + context.map.Travel(unit, tile, front.stagingTile));
            }
            return bonus;
        }

        // -------------------------------------------------------------------- line

        static AIAction DecideLine(Unit unit, AIMap map, AIProfile profile, Posture posture) {

            AttackAppraisal best = AITargeting.BestThisTurn(unit, map, profile, posture);

            float worth = AIScore.Worth(unit);
            stopCapture = profile.tieredChoice ? new Dictionary<HexTile, KeyValuePair<float, HexTile>>() : null;
            AIAction positional = Choose(unit, map, profile, t => {
                float v = 0f;
                AttackAppraisal future = AITargeting.BestNextTurn(unit, t, map, profile, posture);
                float fut = future.valid ? future.value : 0f;
                // Standoff: nothing can strike this tile tonight, yet from it we strike first next
                // turn. Only possible where our reach is longer than theirs; there the enemy must
                // either give ground or step into our reach and take the first blow.
                if (fut > 0f && map.FirersAt(t) == 0) fut *= 1f + profile.standoffWeight;
                float hold = AIScore.Hold(map.Conflict(t)) * worth * profile.holdWeight;
                float relief = AIScore.Relief(map.Distress(t)) * worth * 0.5f;
                float strat = StrategicBonus(unit, t, posture) + map.GuardValue(unit, t);
                float depth = posture == Posture.Defensive ? AIScore.Depth(map.SupplyDepth(t), profile) * worth * 0.3f : 0f;
                // "Aggressive: attempt to go deeper into enemy supply range." An enemy unit in a
                // corridor severs supply through it, and so do ours: standing in their network
                // is how a fortified line is starved out rather than assaulted head-on.
                float cut = posture == Posture.Aggressive ? map.EnemySupplyShare(t) * worth * profile.cutSupplyWeight : 0f;
                v = fut + hold + relief + strat + depth + cut;
                if (DebugUnit == unit && DebugLog != null && DebugBreakdown)
                    DebugLog(string.Format("      ({0},{1}) future {2:0}{3} hold {4:0} relief {5:0} strategic {6:0} depth {7:0}",
                        t.x, t.y, fut, future.valid ? " on " + future.target.name : "", hold, relief, strat, depth));
                return v;
            }, true, 1f, 0.02f, "line");
            if (stopCapture != null) {
                Dictionary<HexTile, KeyValuePair<float, HexTile>> stops = stopCapture;
                stopCapture = null;
                return Tiered(unit, map, profile, posture, stops, s2 => unit.moveAndAttack || s2 == unit.tile, a => true, 1f, "line");
            }
            return Better(best, positional, unit, "engage in place", "close and engage");
        }

        // --------------------------------------------------------------- artillery

        /// <summary>
        /// Indirect fire cannot move and shoot in one turn and takes no retaliation, so a
        /// shot is close to pure profit and artillery acts first. Otherwise it wants safe,
        /// supplied ground within range of the fight. (QuickStrat divided by threat here and
        /// produced Infinity on quiet tiles; threat is priced, not divided by.)
        /// </summary>
        static AIAction DecideArtillery(Unit unit, AIMap map, AIProfile profile, Posture posture) {

            AttackAppraisal shot = AITargeting.BestFrom(unit, unit.tile, map, profile, posture, true);
            if (shot.valid) shot.value -= AIScore.Risk(unit, unit.tile, map, profile) * 1.5f * AIScore.PostureRisk(profile, posture);

            float worth = AIScore.Worth(unit);
            stopCapture = profile.tieredChoice ? new Dictionary<HexTile, KeyValuePair<float, HexTile>>() : null;
            AIAction positional = Choose(unit, map, profile, t => {
                float v = 0f;
                AttackAppraisal future = AITargeting.BestNextTurn(unit, t, map, profile, posture);
                if (future.valid) v += future.value;
                v += AIScore.Hold(map.Conflict(t)) * worth * 0.15f;
                v += AIScore.Depth(map.SupplyDepth(t), profile) * worth * 0.2f;
                v += StrategicBonus(unit, t, posture) * 0.25f;
                v += map.GuardValue(unit, t);
                // Support: guns belong within range of the line they cover. Without this the
                // artillery had no reason to leave home, and sat at the HQ all game while the
                // army advanced without it.
                for (int i = 0; i < map.ownUnits.Count; i++) {
                    Unit friend = map.ownUnits[i];
                    if (friend == null || friend == unit || friend.tile == null || friend.Firepower <= 0) continue;
                    if (friend.GetComponent<SupplyPoint>() != null) continue;
                    int d = t.Distance(friend.tile);
                    if (d > unit.MinRange && d <= unit.AttackRange + 1) v += AIScore.Worth(friend) * profile.supportWeight;
                }
                return v;
            }, true, 1.5f, 0.05f, "artillery");
            if (stopCapture != null) {
                Dictionary<HexTile, KeyValuePair<float, HexTile>> stops = stopCapture;
                stopCapture = null;
                return Tiered(unit, map, profile, posture, stops, s2 => s2 == unit.tile, a => true, 1f, "artillery");
            }
            return Better(shot, positional, unit, "artillery fire", "artillery fire");
        }

        // ---------------------------------------------------------------- anti-air

        static AIAction DecideAntiAir(Unit unit, AIMap map, AIProfile profile, Posture posture) {
            AttackAppraisal shot = AITargeting.BestThisTurn(unit, map, profile, posture);

            float worth = AIScore.Worth(unit);
            int cover = Mathf.Max(1, unit.AttackRange);
            stopCapture = profile.tieredChoice ? new Dictionary<HexTile, KeyValuePair<float, HexTile>>() : null;
            AIAction positional = Choose(unit, map, profile, t => {
                float v = 0f;
                for (int i = 0; i < map.ownUnits.Count; i++) {
                    Unit friend = map.ownUnits[i];
                    if (friend == null || friend.tile == null || friend == unit) continue;
                    if (t.Distance(friend.tile) <= cover) v += AIScore.Worth(friend) * 0.15f;
                }
                v += AIScore.Depth(map.SupplyDepth(t), profile) * worth * 0.1f;
                return v;
            }, true, 1f, 0.03f, "AA");
            if (stopCapture != null) {
                Dictionary<HexTile, KeyValuePair<float, HexTile>> stops = stopCapture;
                stopCapture = null;
                return Tiered(unit, map, profile, posture, stops, s2 => unit.moveAndAttack || s2 == unit.tile, a => true, 1f, "AA");
            }
            return Better(shot, positional, unit, "AA fire", "AA reposition and fire");
        }

        // ------------------------------------------------------------------- recon

        static AIAction DecideRecon(Unit unit, AIMap map, AIProfile profile, Posture posture) {

            AttackAppraisal shot = AITargeting.BestThisTurn(unit, map, profile, posture);
            if (shot.valid && shot.outcome.damageToAttacker != 0) shot.valid = false;   // recon only takes free shots

            float worth = AIScore.Worth(unit);
            Building objective = map.NearestEnemyBuilding(unit.tile);
            stopCapture = profile.tieredChoice ? new Dictionary<HexTile, KeyValuePair<float, HexTile>>() : null;
            AIAction positional = Choose(unit, map, profile, t => {
                float v = 0f;
                v += AIScore.Relief(map.Distress(t)) * worth;           // screen whoever is cut off
                v += AIScore.Depth(map.SupplyDepth(t), profile) * worth * 0.1f;
                if (objective != null && objective.tile != null) v += worth * 0.6f / (1 + t.Distance(objective.tile));
                if (t.building != null && t.building.owner != unit.owner) v += worth * 0.4f;
                v += StrategicBonus(unit, t, posture) * 0.6f;
                return v;
            }, true, 1.5f, 0.01f, "recon");
            if (stopCapture != null) {
                Dictionary<HexTile, KeyValuePair<float, HexTile>> stops = stopCapture;
                stopCapture = null;
                return Tiered(unit, map, profile, posture, stops, s2 => unit.moveAndAttack || s2 == unit.tile, a => a.outcome.damageToAttacker == 0, 1f, "recon");
            }
            return Better(shot, positional, unit, "recon: free shot", "recon: free shot");
        }

        // ----------------------------------------------------------------- wounded

        /// <summary>
        /// Withdrawal is up the supply gradient, because that is where healing is. Sorting
        /// by threat alone walks a unit into a quiet corner outside supply where it starves.
        /// </summary>
        /// <summary>
        /// A unit at or below withdrawStrength (woundedNeedsSafety on). It falls back only when the
        /// withdraw tile both heals it (friendly supply) and is out of the enemy's reach next turn,
        /// and fighting on is not worth more than that (the forecast's world score of its best
        /// fighting move against that of the withdraw tile). Otherwise it fights as its type does.
        /// </summary>
        static AIAction DecideWounded(Unit unit, AIMap map, AIProfile profile, Posture posture) {
            AIRole baseRole = AIRole.Line;
            if (unit.AttackRange >= 2 && !unit.moveAndAttack) baseRole = AIRole.Artillery;
            else if (unit.moveRange >= 6 && unit.Firepower <= 2) baseRole = AIRole.Recon;
            Func<AIAction> fight = () => baseRole == AIRole.Artillery ? DecideArtillery(unit, map, profile, posture)
                                       : baseRole == AIRole.Recon ? DecideRecon(unit, map, profile, posture)
                                       : DecideLine(unit, map, profile, posture);
            AIAction back = DecideWithdraw(unit, map, profile);
            if (back.kind == AIActionKind.Attack) return back;      // the free kill
            HexTile dest = back.destination ?? unit.tile;
            bool heals = map.Heals(unit, dest);
            bool safe = map.EnemyStrikeTurns(dest) > 1;
            LastChosen = null;
            AIAction f = fight();
            AIWorld.Outcome fw = LastChosen;
            if (profile.woundedSafetyGate && (!heals || !safe)) {
                f.reason = "wounded, no safe supplied tile (" + (heals ? "" : "no supply") + (heals || safe ? "" : ", ") + (safe ? "" : "enemy reach") + "): " + f.reason;
                return f;
            }
            if (fw != null) {
                AIWorld.Outcome rw = AIWorld.Evaluate(unit, dest, default(AttackAppraisal), map, map.replyCache, false, true);
                if (fw.score > rw.score) {
                    f.reason = "wounded, fighting worth more (" + fw.score.ToString("0") + " v withdraw " + rw.score.ToString("0") + "): " + f.reason;
                    return f;
                }
                LastChosen = rw;
                back.reason = "wounded, withdraw to safe supply (" + rw.score.ToString("0") + " v fight " + fw.score.ToString("0") + "): " + back.reason;
            }
            return back;
        }

        static AIAction DecideWithdraw(Unit unit, AIMap map, AIProfile profile) {

            // A free kill is still a free kill, even at one strength.
            AttackAppraisal shot = AITargeting.BestFrom(unit, unit.tile, map, profile, Posture.Defensive, true);
            if (shot.valid && shot.outcome.defenderDies && shot.outcome.damageToAttacker == 0) {
                return AIAction.Strike(unit, shot.target, shot.value, "withdraw: free kill first");
            }

            float worth = AIScore.Worth(unit);
            return Choose(unit, map, profile, t => {
                int depth = map.SupplyDepth(t);
                float v = AIScore.Depth(depth, profile) * worth;
                if (depth >= profile.supplyComfort) v += worth * 0.5f;
                v -= AIScore.Hold(map.Conflict(t)) * worth * 0.3f;      // off the contact line
                // Friendly neighbours mean an attacker has to eat a counterattack.
                List<HexTile> neighbors = t.neighbors;
                for (int i = 0; i < neighbors.Count; i++) {
                    HexTile n = neighbors[i];
                    if (n != null && n.occupier != null && n.occupier.owner == unit.owner && n.occupier != unit) {
                        v += worth * 0.05f;
                    }
                }
                return v;
            }, false, 1.5f, 0f, "withdraw");
        }

        // --------------------------------------------------------- idle transport

        /// <summary>
        /// A transport nobody needs right now waits somewhere safe near the hubs it is
        /// most likely to be asked to carry, so the next ride starts close.
        /// </summary>
        public static AIAction DecideIdleTransport(Unit unit, AIMap map, AIProfile profile) {
            float worth = AIScore.Worth(unit);
            return Choose(unit, map, profile, t => {
                float v = 0f;
                for (int i = 0; i < map.ownHubs.Count; i++) {
                    SupplyPoint sp = map.ownHubs[i];
                    if (sp == null || sp.producer || sp.myUnit == null || sp.myUnit.tile == null) continue;
                    v += worth * 0.5f / (1 + t.Distance(sp.myUnit.tile));
                }
                v += AIScore.Depth(map.SupplyDepth(t), profile) * worth * 0.2f;
                return v;
            }, true, 1.5f, 0.02f, "transport", profile.idleTransportScreens);
        }

    }
}
