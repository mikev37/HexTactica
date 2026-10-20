using System.Collections.Generic;
using UnityEngine;
using HexTactica.AI;

namespace HexTactica.Harness {

    /// <summary>
    /// The original AIPlayer (commit 3ce795e), ported as a baseline opponent.
    ///
    /// Faithful to the original's decisions, including its quirks:
    ///   - units act in roster order; each one's action ends with FinishActive, which
    ///     recalculates the threat field the next unit reads;
    ///   - a unit above 2 strength runs AttackMove: best attack value this turn, then
    ///     least incoming threat, then cover; on a remaining tie it looks for the best
    ///     attack within three move ranges and steps along the path toward it; failing
    ///     that it asks its objective, which in SampleScene is a GoTo with no target,
    ///     i.e. "walk toward the map centre (0,0,0)";
    ///   - then it attacks whatever is in range (threshold -9000, so always);
    ///   - a unit at 2 strength or less runs Retreat, which in the original never moves.
    ///
    /// Deviations, each forced: attack values live in a dictionary because the per-tile
    /// field was removed from HexTile; sorts compare floats instead of truncating to int
    /// (the int cast made the comparator inconsistent, so .NET could order the list
    /// arbitrarily); exact ties break on board position so runs are deterministic; and the
    /// headless path moves units without animation.
    /// </summary>
    public static class LegacyAI {

        public static HexTile ObjectiveTarget() {
            return HexGenerator.hg != null ? HexGenerator.hg.get(0, 0, 0) : null;
        }

        public static void PlayWholeTurn(Player player) {
            if (player == null || player.eliminated) return;
            int guard = 0;
            while (guard++ < 1000) {
                Unit unit = null;
                for (int i = 0; i < player.units.Count; i++) {
                    Unit u = player.units[i];
                    if (u != null && u.active && u.tile != null && u.Strength > 0 && u.trasnporter == null) { unit = u; break; }
                }
                if (unit == null) break;
                if (GameController.gc != null && GameController.gc.gameOver) return;

                if (GameController.gc != null) GameController.gc.activeUnit = unit;
                HexTile origin = unit.tile;
                if (unit.Strength > 2) {
                    Dictionary<HexTile, float> value = new Dictionary<HexTile, float>();
                    AttackMove(unit, value);
                    if (unit.Strength > 0 && unit.tile != null && (unit.moveAndAttack || unit.tile == origin)) {
                        AttackAttack(unit, value);
                    }
                }
                // Retreat(): the original sorted candidate tiles and returned without moving.
                unit.active = false;
                if (unit.tile != null) unit.FinishActive();
            }
            if (GameController.gc != null && !GameController.gc.gameOver) GameController.gc.EndTurn();
        }

        static float V(Dictionary<HexTile, float> d, HexTile t) {
            float v;
            return t != null && d.TryGetValue(t, out v) ? v : 0f;
        }

        static void CalculateThreatOdds(Unit unit, int distance, Dictionary<HexTile, float> value) {
            List<HexTile> moves = unit.tile.CheckMovementRange(unit, distance);
            if (!moves.Contains(unit.tile)) moves.Add(unit.tile);
            for (int m = 0; m < moves.Count; m++) {
                HexTile x = moves[m];
                float imminent = (x == unit.tile || unit.moveAndAttack) ? 1f : 0.1f;
                List<HexTile> attacks = x.CheckAttackRange(unit);
                for (int a = 0; a < attacks.Count; a++) {
                    HexTile y = attacks[a];
                    int dmg = Unit.calculateDamage(unit, y.occupier, y, 0);
                    float v = dmg * y.occupier.value() * imminent;
                    if (dmg >= y.occupier.Strength) v *= 1.1f;
                    if (unit.AttackRange == 1 && y.occupier.retaliates) {
                        int back = Unit.calculateDamage(y.occupier, unit, x, dmg);
                        v -= back * unit.value();
                    }
                    value[x] = Mathf.Max(V(value, x), v);
                }
            }
        }

        static void AttackMove(Unit unit, Dictionary<HexTile, float> value) {
            CalculateThreatOdds(unit, unit.moveRange, value);
            List<HexTile> reach = unit.tile.CheckMovementRange(unit);
            List<HexTile> allMoves = new List<HexTile>();
            for (int i = 0; i < reach.Count; i++) if (reach[i].CanEndTurnHere(unit)) allMoves.Add(reach[i]);
            allMoves.Add(unit.tile);
            List<HexTile> moves = new List<HexTile>(allMoves);

            moves.Sort((x, y) => { int c = V(value, y).CompareTo(V(value, x)); return c != 0 ? c : CombatMath.CompareTiles(x, y); });
            float top = V(value, moves[0]);
            moves = moves.FindAll(x => V(value, x) == top);

            if (moves.Count > 1) {
                moves.Sort((x, y) => { int c = x.getThreatTo(unit).CompareTo(y.getThreatTo(unit)); return c != 0 ? c : CombatMath.CompareTiles(x, y); });
                int least = moves[0].getThreatTo(unit);
                moves = moves.FindAll(x => x.getThreatTo(unit) == least);
            }
            if (moves.Count > 1 && unit.Concealment > 0) {
                moves.Sort((x, y) => { int c = y.terrain.cover.CompareTo(x.terrain.cover); return c != 0 ? c : CombatMath.CompareTiles(x, y); });
                int best = moves[0].terrain.cover;
                moves = moves.FindAll(x => x.terrain.cover == best);
            }

            HexTile chosen = null;
            if (moves.Count > 1) {
                CalculateThreatOdds(unit, 20, value);
                List<HexTile> farther = unit.tile.CheckMovementRange(unit, unit.moveRange * 3);
                farther.Sort((x, y) => { int c = V(value, y).CompareTo(V(value, x)); return c != 0 ? c : CombatMath.CompareTiles(x, y); });
                if (farther.Count > 0 && V(value, farther[0]) > 0) {
                    chosen = FindFirstParentInList(allMoves, farther[0]);
                }
            } else {
                chosen = moves[0];
            }

            if (chosen == null) {
                // objective.evaluateTiles: GoTo toward its target, the map centre by default.
                HexTile target = ObjectiveTarget();
                List<HexTile> sorted = new List<HexTile>(allMoves);
                if (target != null) sorted.Sort((x, y) => { int c = x.Distance(target).CompareTo(y.Distance(target)); return c != 0 ? c : CombatMath.CompareTiles(x, y); });
                chosen = sorted[0];
            }

            if (chosen != null && chosen != unit.tile && chosen.occupier == null) {
                MoveField f = AIPath.ThisTurn(unit);
                int steps = f.Reachable(chosen) ? f.Get(chosen).steps : 1;
                unit.TeleportTo(chosen, steps);
            }
        }

        static HexTile FindFirstParentInList(List<HexTile> moves, HexTile t) {
            int guard = 0;
            while (t != null && guard++ < 4096) {
                if (moves.Contains(t)) return t;
                t = t.parent;
            }
            return null;
        }

        static void AttackAttack(Unit unit, Dictionary<HexTile, float> value) {
            List<HexTile> targets = unit.tile.CheckAttackRange(unit);
            if (targets.Count == 0) return;
            targets.Sort((x, y) => { int c = V(value, y).CompareTo(V(value, x)); return c != 0 ? c : CombatMath.CompareTiles(x, y); });
            if (V(value, targets[0]) >= -9000) unit.OrderAttack(targets[0].occupier);
        }
    }
}
