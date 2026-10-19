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
    public bool defenderPinned;        // wanted to retreat, had nowhere to go: takes +1
    public HexTile retreatTile;
    public bool defenderFiresFirst;
    public bool retaliationHappens;

    public int NetDefenderLoss { get { return damageToDefender + (defenderPinned ? 1 : 0); } }
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

        // "The first attacker is determined by the hex tile and unit veterancy."
        // A dug-in veteran gets the drop on its attacker; open ground does not.
        int defenderPoise = defender.Veterancy + CoverFor(defender, defender.tile);
        int attackerPoise = attacker.Veterancy;
        o.defenderFiresFirst = o.retaliationHappens && defenderPoise > attackerPoise;

        if (o.defenderFiresFirst) {
            // Defender pre-empts: retaliation damage lands first and degrades the
            // attacker's shot, since calculateDamage scales by attacker Strength.
            o.damageToAttacker = Cap(
                Unit.calculateDamage(defender, attacker, null, 0),
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
                    Unit.calculateDamage(defender, attacker, null, o.damageToDefender),
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
            if (defenderPressure > attackerPressure) {
                o.defenderRetreats = true;
                o.retreatTile = FindRetreatTile(defender, attackerTile);
                o.defenderPinned = o.retreatTile == null;
                if (o.defenderPinned && o.damageToDefender + 1 >= defender.Strength) {
                    o.defenderDies = true;
                }
            }
        }

        return o;
    }

    static int Cap(int damage, int strength) {
        return Mathf.Clamp(damage, 0, Mathf.Max(0, strength));
    }

    static int CoverFor(Unit u, HexTile t) {
        if (u == null || t == null || t.terrain == null) return 0;
        if (LocomotionRules.IsAirborne(u.locomotion)) return 0;
        return Mathf.Min(u.Concealment, t.terrain.cover);
    }

    /// <summary>
    /// Retreat one tile directly away from the attacker where possible, otherwise
    /// any legal neighbour that does not close the distance. Deterministic: ties
    /// break on cube coordinate so replays are exact.
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
            if (!defender.CanEnter(n)) continue;

            int d = attackerTile != null ? n.Distance(attackerTile) : 1;
            if (d < bestDistance) continue;

            if (best == null || d > bestDistance || Precedes(n, best)) {
                best = n;
                bestDistance = d;
            }
        }
        return best;
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
