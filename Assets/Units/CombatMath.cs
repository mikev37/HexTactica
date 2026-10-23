using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The full outcome of one exchange, computed without touching game state.
/// </summary>
public struct CombatOutcome {
    public int damageToDefender;
    public int damageToAttacker;
    public bool defenderDies;
    public bool attackerDies;
    public bool defenderRetreats;
    public bool defenderPinned;        // wanted to retreat, had nowhere to go: takes +1 (already in damageToDefender)
    public HexTile retreatTile;
    public bool defenderFiresFirst;
    public bool retaliationHappens;
    /// <summary>Indirect fire (an attacker with range over 1) that would have driven the defender back suppresses it instead: no retreat, no pin.</summary>
    public bool defenderSuppressed;
    /// <summary>Strength pips newly suppressed (half the unsuppressed pips left after the hit, rounded up).</summary>
    public int suppressionAdded;

    /// <summary>Strength the defender loses, the pinned point included. Same as damageToDefender.</summary>
    public int NetDefenderLoss { get { return damageToDefender; } }
}

/// <summary>
/// Deterministic combat resolution, extracted so that AI prediction and actual
/// resolution run the exact same code path.
///
/// This matters more than it looks. The design doc gives the defender an
/// autonomous post-combat retreat, which means an attack can end with the target
/// no longer on the tile that was attacked. An AI that scores attacks without
/// simulating that is wrong in a way no amount of weight tuning fixes. Sharing
/// one resolver makes prediction/reality drift impossible by construction.
/// </summary>
public static class CombatMath {

    /// <summary>
    /// Resolve an attack by <paramref name="attacker"/> (standing on
    /// <paramref name="attackerTile"/>) against <paramref name="defender"/>.
    /// Pure: mutates nothing.
    /// </summary>
    public static CombatOutcome Resolve(Unit attacker, Unit defender, HexTile attackerTile) {
        CombatOutcome o = new CombatOutcome();
        if (attacker == null || defender == null || defender.tile == null) return o;
        if (attackerTile == null) attackerTile = attacker.tile;

        bool adjacent = attackerTile != null &&
                        defender.tile.neighbors.Contains(attackerTile);

        // Retaliation only happens in close combat, and only if the defender
        // is capable of shooting back at all.
        o.retaliationHappens = adjacent
                            && defender.MinRange < 1 && defender.AttackRange >= 1
                            && defender.retaliates
                            && defender.CanAttack(attackerTile)
                            && defender.Strength > 0;
        // Suppressed pips do not fire back (whatever the defender's veterancy): they count as
        // strength already lost, for the answer only.
        int quiet = Mathf.Clamp(defender.suppressed, 0, defender.Strength);

        o.defenderFiresFirst = o.retaliationHappens && Ambush(attacker, defender, defender.tile);

        if (o.defenderFiresFirst) {
            // Defender pre-empts: retaliation damage lands first and degrades the
            // attacker's shot, since calculateDamage scales by attacker Strength.
            o.damageToAttacker = Cap(
                Unit.calculateDamage(defender, attacker, null, quiet),
                attacker.Strength);
            o.damageToDefender = Cap(
                Unit.calculateDamage(attacker, defender, defender.tile, o.damageToAttacker),
                defender.Strength);
        } else {
            o.damageToDefender = Cap(
                Unit.calculateDamage(attacker, defender, defender.tile, 0),
                defender.Strength);
            if (o.retaliationHappens) {
                // Retaliation ignores cover, per the doc.
                o.damageToAttacker = Cap(
                    Unit.calculateDamage(defender, attacker, null, o.damageToDefender + quiet),
                    attacker.Strength);
            }
        }

        o.defenderDies = o.damageToDefender >= defender.Strength;
        o.attackerDies = o.damageToAttacker >= attacker.Strength;

        if (!o.defenderDies) {
            // "Then the defender can choose to retreat. This is determined by adding
            // the casualties and seeing who had more + the veterancy of each."
            int defenderPressure = o.damageToDefender - defender.Veterancy;
            int attackerPressure = o.damageToAttacker - attacker.Veterancy;
            // An immobile unit (an HQ) is never driven back, so never pinned either.
            if (defenderPressure > attackerPressure && defender.moveRange > 0 && Suppresses(attacker)) {
                // Indirect fire: suppressed where it stands instead of driven back.
                o.defenderSuppressed = true;
                o.suppressionAdded = SuppressionFrom(defender.Strength - o.damageToDefender, defender.suppressed);
            } else if (defenderPressure > attackerPressure && defender.moveRange > 0) {
                o.defenderRetreats = true;
                o.retreatTile = FindRetreatTile(defender, attackerTile);
                o.defenderPinned = o.retreatTile == null;
                if (o.defenderPinned) {
                    // Nowhere to fall back to: one more strength, counted in the damage itself so
                    // the preview, the AI and the resolution all read the same number.
                    o.damageToDefender = Cap(o.damageToDefender + 1, defender.Strength);
                    o.defenderDies = o.damageToDefender >= defender.Strength;
                }
            }
        }

        return o;
    }

    /// <summary>Indirect fire: any attacker that reaches beyond the next tile (AttackRange over 1). It suppresses instead of driving back.</summary>
    public static bool Suppresses(Unit attacker) {
        return attacker != null && attacker.AttackRange > 1;
    }

    /// <summary>
    /// Would a hit of <paramref name="toDefender"/> (answered with <paramref name="toAttacker"/>) push
    /// the defender — drive it back, or for indirect fire suppress it? The rule in Resolve, for the
    /// AI's simulations: a defender left alive that lost more than it took, veterancy counted, and
    /// able to move.
    /// </summary>
    public static bool Pushes(Unit attacker, int toAttacker, Unit defender, int toDefender, float defenderStrength) {
        if (attacker == null || defender == null || defender.moveRange <= 0) return false;
        if (toDefender >= defenderStrength) return false;
        return toDefender - defender.Veterancy > toAttacker - attacker.Veterancy;
    }

    /// <summary>Pips a suppressing hit adds: half the unsuppressed pips left, rounded up.</summary>
    public static int SuppressionFrom(int strengthLeft, int alreadySuppressed) {
        int free = Mathf.Max(0, strengthLeft - Mathf.Max(0, alreadySuppressed));
        return (free + 1) / 2;
    }

    /// <summary>
    /// Damage both ways for a hypothetical exchange: attacker at <paramref name="attackerStrength"/>
    /// on <paramref name="attackerTile"/>, defender at <paramref name="defenderStrength"/> on
    /// <paramref name="defenderTile"/>. The same rules as Resolve — defender's cover counts against
    /// the shot, retaliation ignores cover, a dug-in veteran fires first, damage taken first
    /// weakens the answer — without needing either unit to actually stand there. For the AI's
    /// simulations. Returns (damage to defender, damage to attacker).
    /// </summary>
    public static void Exchange(Unit attacker, int attackerStrength, HexTile attackerTile,
                                Unit defender, int defenderStrength, HexTile defenderTile,
                                out int toDefender, out int toAttacker) {
        Exchange(attacker, attackerStrength, attackerTile, defender, defenderStrength, defenderTile, defender != null ? defender.suppressed : 0, out toDefender, out toAttacker);
    }

    /// <summary>As above, with the defender carrying <paramref name="defenderSuppressed"/> suppressed pips (they do not fire back).</summary>
    public static void Exchange(Unit attacker, int attackerStrength, HexTile attackerTile,
                                Unit defender, int defenderStrength, HexTile defenderTile, int defenderSuppressed,
                                out int toDefender, out int toAttacker) {
        toDefender = 0; toAttacker = 0;
        if (attacker == null || defender == null || defenderTile == null || attackerTile == null) return;
        attackerStrength = Mathf.Clamp(attackerStrength, 0, attacker.Strength);
        defenderStrength = Mathf.Clamp(defenderStrength, 0, defender.Strength);
        if (attackerStrength <= 0 || defenderStrength <= 0) return;
        int attackerDown = attacker.Strength - attackerStrength, defenderDown = defender.Strength - defenderStrength;
        int quiet = Mathf.Clamp(defenderSuppressed, 0, defenderStrength);

        bool adjacent = defenderTile.neighbors.Contains(attackerTile);
        bool retaliation = adjacent && defender.MinRange < 1 && defender.AttackRange >= 1
                        && defender.retaliates && defender.CanAttack(attackerTile);
        bool defenderFirst = retaliation && Ambush(attacker, defender, defenderTile);
        if (defenderFirst) {
            toAttacker = Cap(Unit.calculateDamage(defender, attacker, null, defenderDown + quiet), attackerStrength);
            toDefender = Cap(Unit.calculateDamage(attacker, defender, defenderTile, attackerDown + toAttacker), defenderStrength);
        } else {
            toDefender = Cap(Unit.calculateDamage(attacker, defender, defenderTile, attackerDown), defenderStrength);
            if (retaliation && toDefender < defenderStrength)
                toAttacker = Cap(Unit.calculateDamage(defender, attacker, null, defenderDown + toDefender + quiet), attackerStrength);
        }
    }

    /// <summary>
    /// Ambush: the defender fires first. It needs concealment the ground lets it use, plus
    /// veterancy, greater than the attacker's concealment plus veterancy:
    ///     min(defender.Concealment, cover) + defender.Veterancy  >  attacker.Concealment + attacker.Veterancy
    /// So open ground confers nothing: a grunt (concealment 3) on grassland (cover 1) has 1,
    /// a tank has 1, no ambush. In forest, hills, mountains or a city it has 2-3 and ambushes a
    /// tank; a tank (concealment 1) can never ambush one; grunts never ambush grunts.
    /// (Before: cover plus veterancy against veterancy alone, so every defender on grassland
    /// fired first.)
    /// </summary>
    public static bool Ambush(Unit attacker, Unit defender, HexTile defenderTile) {
        if (attacker == null || defender == null) return false;
        int hidden = CoverFor(defender, defenderTile) + defender.Veterancy;
        int seen = (LocomotionRules.IsAirborne(attacker.locomotion) ? 0 : attacker.Concealment) + attacker.Veterancy;
        return hidden > seen;
    }

    static int Cap(int damage, int strength) {
        return Mathf.Clamp(damage, 0, Mathf.Max(0, strength));
    }

    static int CoverFor(Unit u, HexTile t) {
        if (u == null || t == null || t.terrain == null) return 0;
        if (LocomotionRules.IsAirborne(u.locomotion)) return 0;
        // The same cover the damage formula uses (combat damage to the tile wears it down).
        return Mathf.Max(0, Mathf.Min(u.Concealment, t.EffectiveCover));
    }

    /// <summary>
    /// Retreat one tile away from the attacker: one of the neighbours that is farther from the
    /// attacker than the defender is now (beside the attacker, the three tiles on the far side).
    /// Sideways or closer is never a retreat; if none of those tiles is open, the defender is
    /// pinned (and takes the extra point). The one straight back first, then the others;
    /// deterministic ties on cube coordinate so replays are exact.
    /// </summary>
    public static HexTile FindRetreatTile(Unit defender, HexTile attackerTile) {
        if (defender == null || defender.tile == null) return null;
        // An immobile unit (an HQ, a dug-in emplacement) cannot fall back; the doc's rule
        // for a defender that cannot retreat applies instead — it takes the extra point.
        if (defender.moveRange <= 0) return null;
        int currentDistance = attackerTile != null ? defender.tile.Distance(attackerTile) : 0;

        HexTile best = null;
        int bestDistance = currentDistance;

        List<HexTile> neighbors = defender.tile.neighbors;
        for (int i = 0; i < neighbors.Count; i++) {
            HexTile n = neighbors[i];
            if (n == null) continue;
            if (n.occupier != null) continue;
            if (!IsRetreatStep(defender, defender.tile, n, attackerTile)) continue;
            int d = attackerTile != null ? n.Distance(attackerTile) : currentDistance + 1;

            // Straight back is the one farthest from the attacker's line: the most distant, then
            // the one whose step is most nearly opposite the attacker.
            if (best == null || d > bestDistance || (d == bestDistance && Straighter(n, best, defender.tile, attackerTile))) {
                best = n;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>
    /// Could <paramref name="d"/> on <paramref name="from"/> fall back onto <paramref name="to"/> (a
    /// neighbour), hit from <paramref name="attacker"/>? It must be able to stand there, step there
    /// (off a bridge only by road), and end farther from the attacker. Occupancy is the caller's.
    /// The AI's forecasts use this too, so they cannot drift from the rule.
    /// </summary>
    public static bool IsRetreatStep(Unit d, HexTile from, HexTile to, HexTile attacker) {
        if (d == null || from == null || to == null || to == attacker) return false;
        if (!d.CanEnter(to)) return false;
        if (!LocomotionRules.BridgeStep(from, to, d.locomotion)) return false;
        return attacker == null || to.Distance(attacker) > from.Distance(attacker);
    }

    /// <summary>Is the step to <paramref name="a"/> more nearly straight away from the attacker than the step to <paramref name="b"/>? (Ties: cube order.)</summary>
    public static bool Straighter(HexTile a, HexTile b, HexTile from, HexTile attacker) {
        if (attacker == null) return Precedes(a, b);
        // Opposite of the attacker's direction, in cube coordinates; compare by dot product.
        int ax = from.x - attacker.x, ay = from.y - attacker.y, az = from.z - attacker.z;
        int da = (a.x - from.x) * ax + (a.y - from.y) * ay + (a.z - from.z) * az;
        int db = (b.x - from.x) * ax + (b.y - from.y) * ay + (b.z - from.z) * az;
        if (da != db) return da > db;
        return Precedes(a, b);
    }

    /// <summary>
    /// Three-way total order on tiles for List.Sort. Returns 0 for the same tile — a
    /// comparator that answers 1 for compare(a, a) breaks the sort contract, and .NET's
    /// introsort may then misorder the list or throw.
    /// </summary>
    public static int CompareTiles(HexTile a, HexTile b) {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return 1;
        if (b == null) return -1;
        if (a.x != b.x) return a.x.CompareTo(b.x);
        if (a.y != b.y) return a.y.CompareTo(b.y);
        return a.z.CompareTo(b.z);
    }

    /// <summary>Stable total ordering on tiles, so nothing in combat depends on list order.</summary>
    public static bool Precedes(HexTile a, HexTile b) {
        if (a.x != b.x) return a.x < b.x;
        if (a.y != b.y) return a.y < b.y;
        return a.z < b.z;
    }

    /// <summary>
    /// Damage the attacker would deal if it were standing on <paramref name="from"/>.
    /// Convenience wrapper used heavily by the evaluator.
    /// </summary>
    public static int PredictDamage(Unit attacker, Unit defender, HexTile from) {
        if (attacker == null || defender == null || defender.tile == null) return 0;
        return Cap(Unit.calculateDamage(attacker, defender, defender.tile, 0), defender.Strength);
    }
}
