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
                    bool moved = DoMove(action.unit, action.destination, headless, map, true);
                    // Stopped on the way by an enemy it did not see (fog of war): no attack.
                    if (moved && action.unit.tile != action.destination) return true;
                    bool fired = DoAttack(action.unit, action.target, map);
                    return moved || fired;
                }

                case AIActionKind.Load:
                    // A destination beside the transport: walk there first, then board.
                    if (action.destination != null && action.unit.tile != action.destination && action.transport != null && action.destination != action.transport.tile) {
                        if (!DoMove(action.unit, action.destination, headless, map)) return false;
                        if (action.unit.tile != action.destination) return true;
                    }
                    if (Transporting.Load(action.transport, action.unit)) {
                        map.Invalidate(action.unit);
                        map.Invalidate(action.transport);
                        return true;
                    }
                    return false;

                case AIActionKind.Unload:
                    // Drive there first, then put the cargo down (same turn: getting off costs nothing).
                    if (action.via != null && action.via != action.unit.tile) {
                        if (!DoMove(action.unit, action.via, headless, map)) return false;
                        if (action.unit.tile != action.via) return true;
                    }
                    if (Transporting.Unload(action.unit, action.target, action.destination)) {
                        map.Invalidate(action.unit);
                        map.Invalidate(action.target);
                        return true;
                    }
                    return false;

                case AIActionKind.Capture:
                    // Capture is implicit (infantry ending its turn on a building without attacking):
                    // holding the tile is all there is to do.
                    return false;
            }
            return false;
        }

        static bool DoMove(Unit unit, HexTile dest, bool headless, AIMap map, bool thenAttacks = false) {
            if (unit == null || dest == null || dest == unit.tile) return false;
            // (An enemy its side does not see is not in the way as far as it knows: under fog of war
            // the move goes ahead, and running into it ends it — Unit.MoveAlong.)
            if (FogOfWar.ApparentOccupier(dest, unit.owner) != null) return false;
            if (!unit.CanEnter(dest)) return false;

            if (headless && FogOfWar.On) {
                // The path the plan used (its field's parent chain), walked through MoveAlong.
                MoveField field;
                using (FogOfWar.Concealing(unit.owner)) field = map.ReachThisTurn(unit);
                List<HexTile> steps = new List<HexTile>();
                if (field.Reachable(dest))
                    for (HexTile t = dest; t != null && steps.Count < 256; t = t == unit.tile ? null : field.ParentOf(t)) steps.Insert(0, t);
                if (steps.Count == 0 || steps[0] != unit.tile) { steps.Clear(); steps.Add(unit.tile); steps.Add(dest); }
                unit.MoveAlong(steps, thenAttacks, false);
            } else if (headless) {
                // Reconstruct step count from the planner's own field so fuel is
                // charged identically to the animated path.
                MoveField field = map.ReachThisTurn(unit);
                int steps = field.Reachable(dest) ? field.Get(dest).steps : unit.tile.Distance(dest);
                unit.TeleportTo(dest, steps, thenAttacks);
            } else {
                // CompleteMove reads the parent chain that CheckMovementRange leaves on
                // the tiles, so that has to be primed first.
                HexGenerator.forAllTiles(t => { t.G = 0; t.parent = null; return 0; });
                unit.tile.CheckMovementRange(unit);
                unit.CompleteMove(dest, thenAttacks);
            }

            map.Invalidate(unit);
            return true;
        }

        static bool DoAttack(Unit attacker, Unit defender, AIMap map) {
            if (attacker == null || defender == null || defender.tile == null) return false;
            if (attacker.tile == null) return false;
            if (!attacker.CanAttack(attacker.tile)) return false;

            // Under fog of war only what its side sees (it may have planned on where it believed an enemy was).
            if (!FogOfWar.CanTarget(attacker.owner, defender)) return false;
            int distance = attacker.tile.Distance(defender.tile);
            if (distance > attacker.AttackRange || distance <= attacker.MinRange) return false;

            attacker.OrderAttack(defender);

            map.Invalidate(attacker);
            map.Invalidate(defender);
            return true;
        }
    }
}
