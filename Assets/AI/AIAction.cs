using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    public enum AIActionKind {
        Wait,
        Move,
        Attack,          // fire from the current tile
        MoveAndAttack,   // move, then fire (only for units with moveAndAttack)
        Load,            // board a transport
        Unload,          // disembark cargo onto a tile
        Capture
    }

    /// <summary>
    /// A decision, with no side effects. Separating this from execution is what makes
    /// the AI testable, replayable, and runnable headlessly at full speed — the old
    /// AIPlayer decided and acted inside an Update() timer, so "what would it do" and
    /// "do it" were the same call and neither could happen without a frame.
    /// </summary>
    public struct AIAction {
        public AIActionKind kind;
        public Unit unit;
        public HexTile destination;
        public Unit target;
        public Unit transport;
        public Building building;
        public float score;
        public string reason;

        public static AIAction Wait(Unit u, string why) {
            return new AIAction { kind = AIActionKind.Wait, unit = u, reason = why };
        }

        public static AIAction Move(Unit u, HexTile dest, float score, string why) {
            return new AIAction { kind = AIActionKind.Move, unit = u, destination = dest, score = score, reason = why };
        }

        public static AIAction Strike(Unit u, Unit target, float score, string why) {
            return new AIAction { kind = AIActionKind.Attack, unit = u, target = target, score = score, reason = why };
        }

        public static AIAction MoveStrike(Unit u, HexTile dest, Unit target, float score, string why) {
            return new AIAction { kind = AIActionKind.MoveAndAttack, unit = u, destination = dest, target = target, score = score, reason = why };
        }

        public static AIAction Board(Unit cargo, Unit transport, HexTile at, string why) {
            return new AIAction { kind = AIActionKind.Load, unit = cargo, transport = transport, destination = at, reason = why };
        }

        public static AIAction Disembark(Unit transport, Unit cargo, HexTile at, string why) {
            return new AIAction { kind = AIActionKind.Unload, unit = transport, target = cargo, destination = at, reason = why };
        }

        public static AIAction Seize(Unit u, Building b, float score, string why) {
            return new AIAction { kind = AIActionKind.Capture, unit = u, building = b, score = score, reason = why };
        }

        public bool IsNothing { get { return kind == AIActionKind.Wait; } }
    }

    public enum AIRole {
        Line,        // close-combat infantry and tanks: take and hold ground
        Artillery,   // indirect fire: never exposed, always in range
        AntiAir,     // reactive cover for airborne threats
        Recon,       // fast, weak: screen, scout, and cover the supply corridor
        Supply,      // hubs: keep the network connected
        Transport,   // carries other units
        Wounded      // anything below the withdraw threshold, regardless of type
    }

    public static class AIRoles {

        public static AIRole Classify(Unit u, AIProfile profile) {
            if (u == null) return AIRole.Line;

            if (u.Strength <= profile.withdrawStrength && u.GetComponent<SupplyPoint>() == null) {
                return AIRole.Wounded;
            }
            if (u.GetComponent<SupplyPoint>() != null) return AIRole.Supply;
            if (u.transportSlots > 0 && u.Firepower <= 1) return AIRole.Transport;
            if (u.Targeting >= 4 && u.Firepower > 0) return AIRole.AntiAir;
            if (u.AttackRange >= 2 && !u.moveAndAttack) return AIRole.Artillery;
            if (u.moveRange >= 6 && u.Firepower <= 2) return AIRole.Recon;
            return AIRole.Line;
        }

        /// <summary>
        /// Order in which units act, straight from the design doc:
        ///
        ///   "Artillery shoots first if it can /
        ///    Breakthrough, high attack units that can attack a target move first /
        ///    Slower units move first because otherwise they will be stuck /
        ///    Faster units that have no attackable target move"
        ///
        /// The reasoning behind the middle two is the important part and it is why the
        /// old AIPlayer's activeUnits[0] was actively harmful: a mobile unit that had
        /// six good options would take the one tile an immobile unit needed, and then
        /// nobody exploited the gap it opened. Constrained, high-punch units claim
        /// ground first; mobile units clean up afterwards. Supply moves last of all,
        /// because a hub cannot know where to park until the line has settled.
        /// </summary>
        public static int Priority(Unit u, AIProfile profile, bool hasTargetInRange) {
            if (u != null && u.airTanker) return 80;   // after the aircraft it refuels have moved
            AIRole role = Classify(u, profile);

            switch (role) {
                case AIRole.Artillery:
                    return hasTargetInRange ? 0 : 40;
                case AIRole.AntiAir:
                    return 15;
                case AIRole.Wounded:
                    return 70;
                case AIRole.Supply:
                    return 85;      // after the line has settled
                case AIRole.Transport:
                    return 90;      // after the hub that hires it, so the ride is already assigned
            }

            if (hasTargetInRange && u.Firepower >= 4) return 10;   // breakthrough

            // Slower units first: they have the fewest alternatives, so they should
            // not be the ones displaced.
            int mobility = Mathf.Clamp(u.moveRange, 0, 20);
            return 20 + mobility;
        }

        /// <summary>Deterministic full ordering of a player's units for this turn.</summary>
        public static List<Unit> Order(List<Unit> units, AIMap map, AIProfile profile) {
            List<Unit> ordered = new List<Unit>(units);

            Dictionary<Unit, int> priority = new Dictionary<Unit, int>();
            for (int i = 0; i < ordered.Count; i++) {
                Unit u = ordered[i];
                priority[u] = Priority(u, profile, HasTargetInRange(u));
            }

            ordered.Sort((a, b) => {
                int pa = priority[a], pb = priority[b];
                if (pa != pb) return pa.CompareTo(pb);
                // Stable tiebreak, so batch runs replay exactly.
                return AIMap.CompareUnits(a, b);
            });
            return ordered;
        }

        public static bool HasTargetInRange(Unit u) {
            if (u == null || u.tile == null || !u.CanAttack(u.tile)) return false;
            List<HexTile> inRange = AIPath.TilesInRange(u.tile, u.MinRange, u.AttackRange);
            for (int i = 0; i < inRange.Count; i++) {
                if (u.validTarget(inRange[i])) return true;
            }
            return false;
        }
    }
}
