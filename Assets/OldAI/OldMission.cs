using UnityEngine;

namespace HexTactica.OldAI {

    /// <summary>
    /// The old AI's reading of a scenario's mission objective (the scene's AIMissionObjective holds the rules — what is
    /// to be destroyed, how much it weighs — and each AI reads them its own way). As the AI read them on
    /// claude/campaign-2 (each type's TileBonus there, copied); no objective overrides the tactics.
    /// </summary>
    public static class OldMission {
        public static float TileBonus(AIMissionObjective objective, Unit unit, HexTile tile, AIMap map) {
            if (objective == null || tile == null || unit == null) return 0f;
            float w = objective.weight;
            MaintainSupply ms = objective as MaintainSupply;
            if (ms != null) {
                if (map == null) return 0f;
                float bonus = 0f;
                int depth = map.SupplyDepth(tile);
                if (depth > 0) bonus += depth * 0.03f;
                bonus += AIScore.Relief(map.Distress(tile)) * 0.8f;
                for (int i = 0; i < map.ownHubs.Count; i++) {
                    SupplyPoint hub = map.ownHubs[i];
                    if (hub == null || hub.connected || hub.myUnit == null || hub.myUnit.tile == null) continue;
                    int hd = tile.Distance(hub.myUnit.tile);
                    if (hd <= 3) bonus += (4 - hd) * 0.15f;
                }
                return bonus * w;
            }
            SeizeBuilding sb = objective as SeizeBuilding;
            if (sb != null) {
                if (sb.target == null || sb.target.tile == null) return 0f;
                int sd = tile.Distance(sb.target.tile);
                if (Building.CanCapture(unit)) {
                    if (tile == sb.target.tile) return w * (sb.target.IsHQ ? 20f : 3f);
                    return w * 1.5f / (1 + sd);
                }
                if (sd >= 1 && sd <= 3) return w * 0.5f / sd;
                return w * 0.25f / (1 + sd);
            }
            HoldTile ht = objective as HoldTile;
            if (ht != null) {
                if (ht.target == null) return 0f;
                if (tile == ht.target) return w * 2.5f;
                int td = tile.Distance(ht.target);
                if (td <= 2) return w * 0.8f / td;
                return w * 0.3f / (1 + td);
            }
            GoTo gt = objective as GoTo;
            if (gt != null) return gt.target == null ? 0f : w * 0.6f / (1 + tile.Distance(gt.target));
            DestroyUnits d = objective as DestroyUnits;
            if (d == null || d.destroyThese == null || d.destroyThese.Count == 0) return 0f;
            float best = 0f;
            for (int i = 0; i < d.destroyThese.Count; i++) {
                Unit target = d.destroyThese[i];
                if (target == null || target.tile == null || target.Strength <= 0) continue;
                int dist = tile.Distance(target.tile);
                float bonus = dist <= unit.AttackRange && dist > unit.MinRange ? 1.0f : 0.5f / (1 + dist);
                if (bonus > best) best = bonus;
            }
            return best * d.weight;
        }

        public static bool OverridesTactics(AIMissionObjective objective, Unit unit, AIMap map) { return false; }

        public static AIAction Decide(AIMissionObjective objective, Unit unit, AIMap map, AIProfile profile, Posture posture) {
            return AIAction.Wait(unit, "objective: no directive");
        }
    }
}
