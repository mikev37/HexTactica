using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// The AI and strikes (Strikes.cs): when to order one, and what one in the air means for where our units stand.
    ///
    ///   - Offence (Decide): a unit that strikes ground or from the air weighs every tile it could aim at by what its
    ///     strike would do to the units its side knows to be there: enemy material destroyed, less our own and our
    ///     allies' hit by the blast. A delayed strike lands after the enemy has moved: a unit that can move counts at
    ///     half. A unit that is expended by its strike (a missile) fires only when that is worth enough of what is
    ///     spent, or when it is about to be destroyed where it stands anyway.
    ///   - Defence (AIMap.IncomingStrikeLoss): a hostile strike in the air is known to every side (its launch is seen),
    ///     but not where it is aimed. Its owner will aim where it hits most of us, so a unit's chance of being under it
    ///     is the share of our units in its range that one blast around this tile would also catch: a lone unit among
    ///     many risks little, a crowd risks all. The danger is part of ExpectedLoss, so every decision (where to stop,
    ///     whether to hold) spreads out or leaves the strike's reach without a rule of its own.
    ///
    /// Fair play: only what a player could know. The target of an enemy strike is never read; our own strikes are
    /// weighed on the units our side sees or remembers (FogOfWar.ApparentOccupier).
    /// </summary>
    public static class AIStrike {

        /// <summary>A unit that is expended by its strike fires when it would destroy at least this share of its own worth.</summary>
        public const float SpendShare = 0.4f;
        /// <summary>...or this share when it is about to be destroyed where it stands (better spent than lost).</summary>
        public const float SpendShareThreatened = 0.05f;
        /// <summary>Our own and our allies' units under our blast count this many times their worth against it.</summary>
        public const float FriendlyFireWeight = 1.5f;
        /// <summary>A delayed strike on a unit that can move: it may not be there when it lands.</summary>
        public const float MayMoveAway = 0.5f;

        /// <summary>
        /// The strike <paramref name="u"/> should make this turn, or null: no tile is worth it (a unit expended by its
        /// strike then holds, see AIPlayer.DecideForUnit; another unit decides as any unit does).
        /// </summary>
        public static AIAction? Decide(Unit u, AIMap map) {
            if (u == null || map == null || !u.StrikesArea || u.tile == null || !u.active) return null;
            if (u.trasnporter != null && u.trasnporter != u) return null;
            if (!u.CanAttack(u.tile)) return null;
            Player me = u.owner;
            HexTile best = null; float bestValue = 0f;
            foreach (HexTile c in Candidates(u, me)) {
                float v = Value(u, u.tile, c, me);
                if (v > bestValue || (v > 0f && Mathf.Abs(v - bestValue) <= 0.001f && CombatMath.Precedes(c, best))) { bestValue = v; best = c; }
            }
            if (best == null || bestValue <= 0f) return null;
            if (u.selfDestructs) {
                float spent = Spent(u);
                bool doomed = map.ExpectedLoss(u, u.tile) >= Mathf.Max(1, u.Strength) - 0.001f;
                float need = spent * (doomed ? SpendShareThreatened : SpendShare);
                if (bestValue < need) return null;
            }
            return AIAction.StrikeTile(u, best, bestValue,
                (u.ballistic ? "launches at " : "strikes ") + "(" + best.x + "," + best.y + "): worth " + bestValue.ToString("0"));
        }

        /// <summary>What a unit expended by its strike gives up: its own worth.</summary>
        public static float Spent(Unit u) { return AIScore.Worth(u) * Mathf.Max(1, u.Strength); }

        /// <summary>The tiles worth weighing: any in range for a unit that targets ground (only those with a unit we know of
        /// within its blast can be worth anything); the tiles of enemies it could fire on for one that aims at units.</summary>
        static IEnumerable<HexTile> Candidates(Unit u, Player me) {
            List<HexTile> inRange = u.tile.AllInAttackRange(u);
            if (!u.targetsGround) {
                foreach (HexTile t in inRange) {
                    Unit o = FogOfWar.ApparentOccupier(t, me);
                    if (o != null && Player.Hostile(o.owner, me) && u.CanTarget(o)) yield return t;
                }
                yield break;
            }
            int r = Mathf.Max(0, u.blastRadius);
            foreach (HexTile t in inRange) {
                bool near = false;
                if (FogOfWar.ApparentOccupier(t, me) != null) near = true;
                else if (r > 0) foreach (HexTile n in Strikes.Blast(t, r)) if (FogOfWar.ApparentOccupier(n, me) != null) { near = true; break; }
                if (near) yield return t;
            }
        }

        /// <summary>
        /// What a strike by <paramref name="u"/> from <paramref name="from"/> on <paramref name="target"/> is worth to
        /// <paramref name="me"/>, in the AI's gold (AIScore.Worth per strength point): enemy losses, a kill counting
        /// killBonus more, less our side's and our allies' losses.
        /// </summary>
        public static float Value(Unit u, HexTile from, HexTile target, Player me) {
            float killBonus = 1.35f;
            float v = 0f;
            foreach (StrikeHit h in Strikes.Preview(u, from, target, me)) {
                Unit o = h.unit;
                float worth = AIScore.Worth(o) * h.damage * (h.dies ? killBonus : 1f);
                if (Player.Hostile(o.owner, me)) {
                    if (u.ballistic && o.moveRange > 0 && o.locomotion != Locomotion.Immobile) worth *= MayMoveAway;
                    v += worth;
                } else v -= worth * FriendlyFireWeight;
            }
            // A hostile building it would damage: a small part of its worth, so ties go to the one that also hurts it.
            Building b = target.building;
            if (b != null && Player.Hostile(b.owner, me) && u.buildingDamage > 0 && b.health > 0 && !b.Destroyed)
                v += 100f * Mathf.Min(u.buildingDamage, b.health - b.damage);
            return v;
        }
    }

    public partial class AIMap {

        /// <summary>
        /// Strength <paramref name="u"/> should expect to lose on <paramref name="at"/> to hostile strikes in the air
        /// (AIStrike: where they are aimed is not known; the share of our units in reach that one blast here would also
        /// catch). 0 when none is in the air: games without strikes play exactly as before.
        /// </summary>
        public float IncomingStrikeLoss(Unit u, HexTile at) {
            IList<Strike> air = Strikes.InFlight;
            if (air.Count == 0 || u == null || at == null) return 0f;
            float loss = 0f;
            for (int i = 0; i < air.Count; i++) {
                Strike s = air[i];
                if (s == null || s.weapon == null || s.from == null) continue;
                Unit w = s.weapon;
                int r = Mathf.Max(0, w.blastRadius);
                // Our own: we know where it lands. Never stand under it.
                if (s.owner == player) {
                    if (s.target != null && s.target.Distance(at) <= r) loss += Mathf.Min(u.Strength, Unit.DamageFrom(w, s.strength, s.veterancy, s.attackBonus, u, at));
                    continue;
                }
                if (!Player.Hostile(s.owner, player)) continue;
                int reach = w.AttackRange + r;
                if (s.from.Distance(at) > reach) continue;
                int damage = Mathf.Min(u.Strength, Unit.DamageFrom(w, s.strength, s.veterancy, s.attackBonus, u, at));
                if (damage <= 0) continue;
                // Ours it could also choose among (in its reach), and those one blast around here would catch too.
                int inReach = 1, together = 1;
                for (int k = 0; k < ownUnits.Count; k++) {
                    Unit o = ownUnits[k];
                    if (o == null || o == u || o.tile == null || o.Strength <= 0 || (o.trasnporter != null && o.trasnporter != o)) continue;
                    if (s.from.Distance(o.tile) > reach || !w.CanTarget(o)) continue;
                    inReach++;
                    if (o.tile.Distance(at) <= 2 * r) together++;
                }
                loss += damage * (float)together / inReach;
            }
            return loss;
        }
    }
}
