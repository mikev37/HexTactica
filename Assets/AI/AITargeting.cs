using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    public enum Posture { Aggressive, Defensive }

    public struct AttackAppraisal {
        public Unit target;
        public HexTile from;
        public float value;
        public CombatOutcome outcome;
        public bool valid;
    }

    /// <summary>
    /// Scoring and selection of attacks, following the doc's rules:
    ///
    ///   Attack value = damage x gold value, + a bonus if the unit will die,
    ///   a demerit for overkill, then subtract the same if there is a retaliation.
    ///
    ///   Aggressive: target the enemy providing the most supply; failing that, the
    ///   one most vulnerable to being eliminated.
    ///   Defensive: target the enemy closest to friendly supply, value targets with
    ///   the most ability to strike, value preserving self.
    ///
    /// Every appraisal runs through CombatMath.Resolve, so predicted damage, predicted
    /// retaliation and predicted post-combat retreat are the same numbers the game will
    /// produce. Nothing here can drift from what actually happens.
    /// </summary>
    public static class AITargeting {

        /// <summary>Score a specific attack, delivered from a specific tile.</summary>
        public static AttackAppraisal Appraise(Unit attacker, Unit defender, HexTile from,
                                               AIMap map, AIProfile profile, Posture posture,
                                               bool imminent) {
            AttackAppraisal a = new AttackAppraisal { target = defender, from = from };

            if (attacker == null || defender == null || defender.tile == null) return a;
            if (!attacker.CanAttack(from)) return a;
            if (defender.owner == attacker.owner) return a;

            int distance = from.Distance(defender.tile);
            if (distance > attacker.AttackRange || distance <= attacker.MinRange) return a;

            CombatOutcome o = CombatMath.Resolve(attacker, defender, from);
            if (o.damageToDefender <= 0 && !o.defenderRetreats) return a;

            a.outcome = o;
            a.valid = true;

            float targetWorth = AIScore.TargetWorth(defender);
            float value = o.damageToDefender * targetWorth;

            // Damage that does not kill a supplied combat unit heals back a strength a turn.
            // Pricing it as permanent let a tank trade itself for two points on a supply hub
            // that was whole again two turns later. Damage that sticks — or kills — is worth
            // all of it.
            // (Supply units heal too, unless cut off — the 25,000 hub healed its 3 points back while the
            // tank that hit it was lost for good. It used to be exempt here.)
            // The game heals one strength a turn (profile.healedPerTurn): that much doesn't count.
            if (!o.defenderDies && o.damageToDefender > 0 && map.Heals(defender, defender.tile))
                value *= Mathf.Max(0f, o.damageToDefender - profile.healedPerTurn) / o.damageToDefender;

            // An HQ is priced by what attacking it achieves: the win, or nothing lasting.
            if (IsHQ(defender)) {
                value = HQValue(attacker, defender, from, o, map, profile, imminent);
            }

            // So is any unit that does not fight. A supply hub at two strength supplies
            // exactly as well as one at six, so damage to it is only progress toward a kill,
            // and it heals. Pricing that chip as permanent sent a tank chasing a fleeing hub
            // into a corner until it starved.
            else if (defender.Firepower <= 0) {
                float whole = defender.Strength * targetWorth * profile.killBonus;
                if (o.defenderDies) value = whole;
                else if (imminent && map.FollowUpDamage(defender, attacker) >= defender.Strength - o.damageToDefender) value = whole * 0.8f;
                else value = o.damageToDefender * targetWorth * profile.hqChipFactor;
            }

            // "+ a bonus if the unit will die"
            else if (o.defenderDies && defender.Firepower > 0) value *= profile.killBonus;

            // "a demerit for overkill" — the raw damage before capping tells us how
            // much firepower was wasted. Without this the AI is rewarded for smashing
            // a one-strength conscript with a battleship.
            // (Not on units that do not fight: they are priced by the kill as a whole.)
            if (defender.Firepower > 0) {
                int raw = Unit.calculateDamage(attacker, defender, defender.tile, 0);
                int wasted = Mathf.Max(0, raw - defender.Strength);
                value -= wasted * targetWorth * profile.overkillPenalty;
            }

            // "then subtract the same if there is a retaliation"
            if (o.damageToAttacker > 0) {
                value -= o.damageToAttacker * AIScore.Worth(attacker) * profile.retaliationWeight;
            }
            if (o.attackerDies) {
                value -= AIScore.Worth(attacker) * profile.killBonus;
            }

            // Driving a defender off a tile has worth of its own even without a kill.
            if (o.defenderRetreats) value += targetWorth * 0.2f;

            // Posture-specific preferences, all in gold.
            if (posture == Posture.Aggressive) {
                // Supply first: a hub is the thing whose loss unravels a position. Its
                // importance is a share of the value of everything it keeps alive.
                value += map.SupplyImportance(defender);
                if (o.defenderDies) value += targetWorth * 0.5f;
            } else {
                // "Target the enemy closest to friendly supply": pressure on our depth.
                value += AIScore.Depth(map.SupplyDepth(defender.tile), profile) * targetWorth * 0.15f;
                // "Value targets that have the most ability to strike": threatValue is the
                // best single attack the target can currently make, already in gold.
                value += defender.threatValue * 0.25f;
                // "Value preserving self, minimizing counterstrike."
                if (o.damageToAttacker > 0) {
                    value -= o.damageToAttacker * AIScore.Worth(attacker) * 0.5f;
                }
                // "If a unit can eliminate another unit without retribution, take the shot"
                if (o.defenderDies && o.damageToAttacker == 0) value += targetWorth * 1.5f;
            }

            // Removing a threat to our HQ, in proportion to the strength taken off it: damage
            // to a unit scales its damage down with it.
            // And any threat to anything else: an enemy is worth what it can do to us next turn —
            // its best blow, in full — in proportion to how much of it this attack takes away
            // (damage scales with strength). The HQ term is kept separately because it also weighs
            // how close the HQ is to falling.
            float threat = Mathf.Max(map.HQThreat(defender) * profile.hqDefenseWeight,
                                     map.ThreatBonus(defender) * profile.threatTargetWeight);
            if (threat > 0f && defender.Strength > 0) {
                float removed = o.defenderDies ? 1f : o.damageToDefender / (float)defender.Strength;
                value += threat * removed;
            }

            // Exploit a breakthrough: a supply hub hit from ground none of their fighting units can
            // reach tonight. We are past their line, and the hub is what keeps it standing.
            if (defender.GetComponent<SupplyPoint>() != null && !IsHQ(defender) && map.CombatFirersAt(from, defender) == 0) {
                value += map.SupplyImportance(defender) * profile.breakthroughWeight;
            }

            // Delivered next turn rather than this one.
            if (!imminent) value *= profile.imminentDiscount;

            // No supply multiplier here. A kill is a kill wherever it is made; being out of
            // supply is a cost of *staying* somewhere, priced where position is priced. An
            // earlier version multiplied attack value by the supply curve, which shrank a
            // clean one-shot kill from ~130 to ~11 on a depth-1 tile, let the risk term
            // outweigh it, and stalled six units against one — and since positional scoring
            // applies the multiplier too, future attacks were penalised twice.

            a.value = value;
            return a;
        }

        /// <summary>Best attack available from a given tile, considering every enemy in reach.</summary>
        public static AttackAppraisal BestFrom(Unit attacker, HexTile from, AIMap map,
                                               AIProfile profile, Posture posture, bool imminent) {
            AttackAppraisal best = new AttackAppraisal();
            if (attacker == null || from == null || !attacker.CanAttack(from)) return best;

            List<HexTile> inRange = AIPath.TilesInRange(from, attacker.MinRange, attacker.AttackRange);
            for (int i = 0; i < inRange.Count; i++) {
                HexTile t = inRange[i];
                if (t.occupier == null || t.occupier.owner == attacker.owner || !GameController.Targetable(t.occupier)) continue;

                AttackAppraisal a = Appraise(attacker, t.occupier, from, map, profile, posture, imminent);
                if (!a.valid) continue;
                if (!best.valid || a.value > best.value ||
                    (a.value == best.value && CombatMath.Precedes(t, best.target.tile))) {
                    best = a;
                }
            }
            return best;
        }

        /// <summary>Every valid shot from <paramref name="from"/>, one per enemy in range, in board order.</summary>
        public static List<AttackAppraisal> AllFrom(Unit attacker, HexTile from, AIMap map,
                                                    AIProfile profile, Posture posture, bool imminent) {
            List<AttackAppraisal> all = new List<AttackAppraisal>();
            if (attacker == null || from == null || !attacker.CanAttack(from)) return all;
            List<HexTile> inRange = AIPath.TilesInRange(from, attacker.MinRange, attacker.AttackRange);
            inRange.Sort(CombatMath.CompareTiles);
            for (int i = 0; i < inRange.Count; i++) {
                HexTile t = inRange[i];
                if (t.occupier == null || t.occupier.owner == attacker.owner || !GameController.Targetable(t.occupier)) continue;
                AttackAppraisal a = Appraise(attacker, t.occupier, from, map, profile, posture, imminent);
                if (a.valid) all.Add(a);
            }
            return all;
        }

        public static bool IsHQUnit(Unit u) { return IsHQ(u); }

        static bool IsHQ(Unit u) {
            SupplyPoint sp = u != null ? u.GetComponent<SupplyPoint>() : null;
            return sp != null && sp.producer;
        }

        /// <summary>
        /// Value of hitting an enemy HQ unit.
        ///
        /// "Damage x value per strength" — the rule every other target uses — priced three
        /// damage on the shipped HQ (cost 100,000) at 50,000 gold, more than the AI's whole
        /// army, and the army marched into the HQ's defences and died. But an HQ is a
        /// producer, always inside its own supply, so it heals a point every turn: damage
        /// that does not kill it evaporates. What attacking it is worth is the win.
        ///
        ///   - A killing blow wins the game: worth everything on the board.
        ///   - A hit this turn that this player's other units, still to act, can finish this
        ///     turn is worth nearly as much — that is a coordinated assault.
        ///   - Anything else is chip damage that will heal: worth a small fraction.
        /// </summary>
        static float HQValue(Unit attacker, Unit hq, HexTile from, CombatOutcome o, AIMap map, AIProfile profile, bool imminent) {
            float win = map.BoardStake;
            if (o.defenderDies) return win;
            if (imminent) {
                int remaining = hq.Strength - o.damageToDefender;
                if (remaining > 0 && map.FollowUpDamage(hq, attacker) >= remaining) return win * 0.8f;
            }
            // Not finished this turn, so it heals a strength before we can strike again. The hit
            // is worth the win only as far as it sets up a kill next turn that the units now
            // close enough could deliver; otherwise it is chip on a 100,000-gold unit that heals.
            // Pricing that chip at the HQ's own value made three points on it worth more than a
            // tank, and tanks dived the HQ alone and died next to it.
            int left = hq.Strength - o.damageToDefender + 1;
            if (left > 0 && !o.attackerDies && map.NextTurnDamage(hq, attacker, from) >= left) return win * profile.hqSetupFactor;
            return o.damageToDefender * AIScore.Worth(attacker) * profile.hqChipFactor;
        }

        /// <summary>
        /// What it costs to end the turn on the firing tile, in gold: tonight's expected
        /// losses (ignoring the target's own fire if this shot kills it — the dead shoot
        /// nobody), less a cover preference, plus a turn of supply if the tile is outside it.
        /// </summary>
        static float StandingCost(Unit attacker, HexTile at, AttackAppraisal shot,
                                  AIMap map, AIProfile profile, Posture posture) {
            Unit killed = shot.valid && shot.outcome.defenderDies ? shot.target : null;
            float loss = map.ExpectedLoss(attacker, at, killed);
            float cost = loss * AIScore.Worth(attacker) * profile.riskWeight * AIScore.PostureRisk(profile, posture);
            cost -= AIScore.CoverPreference(attacker, at, map, profile);
            cost += AIScore.OutOfSupplyCost(attacker, at, map);
            cost += map.CohesionCost(attacker, at);
            cost -= map.FirstStrikeCredit(attacker, at);
            cost -= map.SpaceDenial(attacker, at);
            return cost;
        }

        /// <summary>
        /// The best attack this unit could make NEXT turn if it ended this turn on
        /// <paramref name="from"/>, discounted for not being available yet.
        ///
        /// BestFrom(imminent:false) only counts enemies already in weapon range of the tile,
        /// i.e. "next turn, without moving". A unit that can move and attack threatens far
        /// more than that — a Grunt reaches move 3 + range 1. Reachability uses real movement
        /// (AIMap.FiringTileNextTurn); an earlier hex-distance estimate ignored terrain and
        /// blocking, and sent a tank to starve in a corner chasing a target it could not reach.
        /// </summary>
        public static AttackAppraisal BestNextTurn(Unit attacker, HexTile from, AIMap map,
                                                   AIProfile profile, Posture posture) {
            AttackAppraisal best = new AttackAppraisal();
            if (attacker == null || from == null || !attacker.CanAttack(from)) return best;
            if (!attacker.moveAndAttack) {
                best = BestFrom(attacker, from, map, profile, posture, false);
                if (best.valid && best.value > 0) best.value *= AIScore.SurvivingFraction(attacker, from, map, null);
                return best;
            }

            for (int i = 0; i < map.enemyUnits.Count; i++) {
                Unit e = map.enemyUnits[i];
                if (e == null || e.tile == null || e.Strength <= 0) continue;

                // Real movement: can it get from here to somewhere it can fire on e?
                HexTile firingTile = map.FiringTileNextTurn(attacker, e, from);
                if (firingTile == null) continue;

                AttackAppraisal a = Appraise(attacker, e, firingTile, map, profile, posture, false);
                if (!a.valid) continue;
                // Only what survives tonight attacks tomorrow. Without this, a lone unit saw an
                // attack on the enemy HQ at full value from a tile where it would be destroyed,
                // and the army rushed in piecemeal and died. With it, fire shared across a
                // supported advance keeps the attack valuable; a lone rush does not.
                if (a.value > 0) a.value *= AIScore.SurvivingFraction(attacker, from, map, null);
                if (!best.valid || a.value > best.value ||
                    (a.value == best.value && CombatMath.Precedes(e.tile, best.target.tile))) {
                    best = a;
                }
            }
            return best;
        }

        /// <summary>
        /// Best attack available anywhere the unit can move to this turn, honouring
        /// whether it can move and shoot in the same turn.
        /// </summary>
        public static AttackAppraisal BestThisTurn(Unit attacker, AIMap map, AIProfile profile, Posture posture) {
            AttackAppraisal best = new AttackAppraisal();
            if (attacker == null || attacker.tile == null) return best;

            // Fire from where it stands, priced the same way as firing after a move.
            best = BestFrom(attacker, attacker.tile, map, profile, posture, true);
            if (best.valid) best.value -= StandingCost(attacker, attacker.tile, best, map, profile, posture);

            if (!attacker.moveAndAttack) return best;   // indirect fire: move OR attack

            MoveField reach = map.ReachThisTurn(attacker);
            foreach (HexTile t in reach.Tiles) {
                if (!AIPath.CanFinishTurnOn(attacker, t)) continue;
                AttackAppraisal a = BestFrom(attacker, t, map, profile, posture, true);
                if (!a.valid) continue;

                float adjusted = a.value - StandingCost(attacker, t, a, map, profile, posture);

                if (!best.valid || adjusted > best.value) {
                    a.value = adjusted;
                    best = a;
                }
            }
            return best;
        }
    }
}
