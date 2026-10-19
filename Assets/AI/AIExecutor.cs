using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Applies a decided action to the board.
    ///
    /// Splitting this from the deciders is what lets the same AI run at half-second
    /// pacing for a watching human and at full speed in a batch of ten thousand
    /// headless scenarios. Previously the decision lived inside an Update() timer, so
    /// "what would it do" could not be asked without a frame passing and the thing
    /// actually happening.
    /// </summary>
    public static class AIExecutor {

        public static bool Execute(AIAction action, AIMap map, bool headless) {
            if (action.unit == null) return false;

            switch (action.kind) {

                case AIActionKind.Wait:
                    return false;

                case AIActionKind.Move:
                    return DoMove(action.unit, action.destination, headless, map);

                case AIActionKind.Attack:
                    return DoAttack(action.unit, action.target, map);

                case AIActionKind.MoveAndAttack: {
                    bool moved = DoMove(action.unit, action.destination, headless, map);
                    bool fired = DoAttack(action.unit, action.target, map);
                    return moved || fired;
                }

                case AIActionKind.Load:
                    if (Transporting.Load(action.transport, action.unit)) {
                        map.Invalidate(action.unit);
                        map.Invalidate(action.transport);
                        return true;
                    }
                    return false;

                case AIActionKind.Unload:
                    if (Transporting.Unload(action.unit, action.target, action.destination)) {
                        map.Invalidate(action.unit);
                        map.Invalidate(action.target);
                        return true;
                    }
                    return false;

                case AIActionKind.Capture:
                    if (action.building != null) {
                        bool flipped = action.building.Capture(action.unit);
                        if (flipped) map.Rebuild();
                        return true;
                    }
                    return false;
            }
            return false;
        }

        static bool DoMove(Unit unit, HexTile dest, bool headless, AIMap map) {
            if (unit == null || dest == null || dest == unit.tile) return false;
            if (dest.occupier != null) return false;
            if (!unit.CanEnter(dest)) return false;

            if (headless) {
                // Reconstruct step count from the planner's own field so fuel is
                // charged identically to the animated path.
                MoveField field = map.ReachThisTurn(unit);
                int steps = field.Reachable(dest) ? field.Get(dest).steps : unit.tile.Distance(dest);
                unit.TeleportTo(dest, steps);
            } else {
                // CompleteMove reads the parent chain that CheckMovementRange leaves on
                // the tiles, so that has to be primed first.
                HexGenerator.forAllTiles(t => { t.G = 0; t.parent = null; return 0; });
                unit.tile.CheckMovementRange(unit);
                unit.CompleteMove(dest);
            }

            map.Invalidate(unit);
            return true;
        }

        static bool DoAttack(Unit attacker, Unit defender, AIMap map) {
            if (attacker == null || defender == null || defender.tile == null) return false;
            if (attacker.tile == null) return false;
            if (!attacker.CanAttack(attacker.tile)) return false;

            int distance = attacker.tile.Distance(defender.tile);
            if (distance > attacker.AttackRange || distance <= attacker.MinRange) return false;

            attacker.OrderAttack(defender);

            map.Invalidate(attacker);
            map.Invalidate(defender);
            return true;
        }
    }
}
