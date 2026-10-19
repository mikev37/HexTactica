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

            AIRole role = AIRoles.Classify(unit, profile);

            switch (role) {
                case AIRole.Artillery: return DecideArtillery(unit, map, profile, posture);
                case AIRole.Wounded:   return DecideWithdraw(unit, map, profile);
                case AIRole.Recon:     return DecideRecon(unit, map, profile, posture);
                case AIRole.AntiAir:   return DecideAntiAir(unit, map, profile, posture);
                default:               return DecideLine(unit, map, profile, posture);
            }
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
        public static Dictionary<Unit, HexTile> Intents {
            get { return context.intents; }
            set { context.intents = value; }
        }
        static readonly Context context = new Context();

        // -------------------------------------------------------------- shared loop

        /// <summary>
        /// Pick the best destination over the planning horizon and return the legal move
        /// for this turn. <paramref name="valueOf"/> returns a destination's worth in gold,
        /// before delay, risk and (when <paramref name="applySupply"/>) supply cost are
        /// applied here uniformly.
        /// </summary>
        static AIAction Choose(Unit unit, AIMap map, AIProfile profile,
                               Func<HexTile, float> valueOf,
                               bool applySupply, float riskFactor, float stayFactor, string tag) {

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

            Front front = context.front;
            if (front != null && front.objectiveTile != null) {
                int d = tile.Distance(front.objectiveTile);
                bool building = front.objective != null;
                bool canTake = building && Building.CanCapture(unit);

                // The objective's own value, not the acting unit's: scaling the pull by the
                // unit's worth made capture nearly worthless to cheap infantry, which are
                // the only units that can capture.
                float pull = front.objectiveValue * context.profile.objectivePull;
                if (building && !canTake) pull *= 0.35f;      // escort rather than seize
                if (posture == Posture.Defensive) pull *= 0.45f;
                if (front.siege && posture == Posture.Aggressive) pull *= context.profile.siegePull;
                bonus += pull / (1 + d);

                if (canTake && tile == front.objectiveTile) bonus += front.objectiveValue * 0.5f;
            }
            // Regrouping for an attack: gather on the edge of our supply nearest the objective.
            if (front != null && front.staging && front.stagingTile != null) {
                bonus += worth * context.profile.stagingPull / (1 + tile.Distance(front.stagingTile));
            }
            return bonus;
        }

        // -------------------------------------------------------------------- line

        static AIAction DecideLine(Unit unit, AIMap map, AIProfile profile, Posture posture) {

            AIAction capture = TryCapture(unit, map, profile);
            if (!capture.IsNothing) return capture;

            AttackAppraisal best = AITargeting.BestThisTurn(unit, map, profile, posture);

            float worth = AIScore.Worth(unit);
            AIAction positional = Choose(unit, map, profile, t => {
                float v = 0f;
                AttackAppraisal future = AITargeting.BestNextTurn(unit, t, map, profile, posture);
                float fut = future.valid ? future.value : 0f;
                float hold = AIScore.Hold(map.Conflict(t)) * worth * profile.holdWeight;
                float relief = AIScore.Relief(map.Distress(t)) * worth * 0.5f;
                float strat = StrategicBonus(unit, t, posture);
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
            AIAction positional = Choose(unit, map, profile, t => {
                float v = 0f;
                AttackAppraisal future = AITargeting.BestNextTurn(unit, t, map, profile, posture);
                if (future.valid) v += future.value;
                v += AIScore.Hold(map.Conflict(t)) * worth * 0.15f;
                v += AIScore.Depth(map.SupplyDepth(t), profile) * worth * 0.2f;
                v += StrategicBonus(unit, t, posture) * 0.25f;
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
            return Better(shot, positional, unit, "artillery fire", "artillery fire");
        }

        // ---------------------------------------------------------------- anti-air

        static AIAction DecideAntiAir(Unit unit, AIMap map, AIProfile profile, Posture posture) {
            AttackAppraisal shot = AITargeting.BestThisTurn(unit, map, profile, posture);

            float worth = AIScore.Worth(unit);
            int cover = Mathf.Max(1, unit.AttackRange);
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
            return Better(shot, positional, unit, "AA fire", "AA reposition and fire");
        }

        // ------------------------------------------------------------------- recon

        static AIAction DecideRecon(Unit unit, AIMap map, AIProfile profile, Posture posture) {

            AIAction capture = TryCapture(unit, map, profile);
            if (!capture.IsNothing) return capture;

            AttackAppraisal shot = AITargeting.BestThisTurn(unit, map, profile, posture);
            if (shot.valid && shot.outcome.damageToAttacker != 0) shot.valid = false;   // recon only takes free shots

            float worth = AIScore.Worth(unit);
            Building objective = map.NearestEnemyBuilding(unit.tile);
            AIAction positional = Choose(unit, map, profile, t => {
                float v = 0f;
                v += AIScore.Relief(map.Distress(t)) * worth;           // screen whoever is cut off
                v += AIScore.Depth(map.SupplyDepth(t), profile) * worth * 0.1f;
                if (objective != null && objective.tile != null) v += worth * 0.6f / (1 + t.Distance(objective.tile));
                if (t.building != null && t.building.owner != unit.owner) v += worth * 0.4f;
                v += StrategicBonus(unit, t, posture) * 0.6f;
                return v;
            }, true, 1.5f, 0.01f, "recon");
            return Better(shot, positional, unit, "recon: free shot", "recon: free shot");
        }

        // ----------------------------------------------------------------- wounded

        /// <summary>
        /// Withdrawal is up the supply gradient, because that is where healing is. Sorting
        /// by threat alone walks a unit into a quiet corner outside supply where it starves.
        /// </summary>
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
            }, true, 1.5f, 0.02f, "transport");
        }

        // ------------------------------------------------------------------ capture

        static AIAction TryCapture(Unit unit, AIMap map, AIProfile profile) {
            if (!Building.CanCapture(unit)) return AIAction.Wait(unit, "");
            if (unit.tile == null || unit.tile.building == null) return AIAction.Wait(unit, "");
            Building b = unit.tile.building;
            if (b.owner == unit.owner) return AIAction.Wait(unit, "");

            float worth = b.IsHQ ? 1e6f : AIScore.Worth(unit) * 3f + b.income * 10f;
            return AIAction.Seize(unit, b, worth, b.IsHQ ? "capture HQ" : "capture building");
        }
    }
}
