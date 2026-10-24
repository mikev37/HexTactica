using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// What fog of war is worth to the AI when it plays by it (AIProfile.fogFair): seeing, and not
    /// being seen. Both are worked out on the concealed board (FogOfWar.current), so they use only
    /// what this side knows; with no concealed board (fog off, or an AI that sees everything) they
    /// are worth nothing and the AI plays exactly as it always has.
    ///
    ///   - Vision: a stop is worth scoutTileValue for every tile it would bring into sight that is not
    ///     seen now, more the longer that tile has gone unseen (full value after 4 turns, or never
    ///     seen). Recon units count it double, and are drawn to look at what they cannot see.
    ///   - Ambush: a stop the enemy (as far as we know — its units where they are thought to be, its
    ///     buildings as last seen) would not see this unit on. Part of tonight's risk is taken back
    ///     (hiddenRiskRelief: it can only be hit by what stumbles into it), and a unit that fires at
    ///     close range is worth ambushValue of itself for each enemy that could reach it next turn
    ///     (up to three): one that runs into it is shot first, and cannot pick its fight.
    /// </summary>
    public static class AIFog {

        public static float StopValue(Unit unit, HexTile stop, AIMap map, AIProfile p) {
            if (FogOfWar.current == null || unit == null || stop == null) return 0f;
            return ScoutValue(unit, stop, p) + AmbushValue(unit, stop, map, p);
        }

        public static bool IsRecon(Unit u) { return u != null && u.moveRange >= 6 && u.Firepower <= 2; }

        public static float ScoutValue(Unit unit, HexTile at, AIProfile p) {
            FogOfWar.Scope s = FogOfWar.current;
            if (s == null || unit == null || at == null || p.scoutTileValue <= 0f) return 0f;
            float v = 0f;
            foreach (HexTile t in AIPath.TilesInRangeShared(at, -1, FogOfWar.VisionFrom(unit, at))) {
                if (s.visibleTiles.Contains(t)) continue;
                int last = FogOfWar.TileLastSeen(s.player, t);
                v += (last < 0 ? 4f : Mathf.Min(4f, FogOfWar.Turn - last)) / 4f;
            }
            return v * p.scoutTileValue * (IsRecon(unit) ? 2f : 1f);
        }

        public static float AmbushValue(Unit unit, HexTile at, AIMap map, AIProfile p) {
            FogOfWar.Scope s = FogOfWar.current;
            if (s == null || unit == null || at == null || s.EnemySees(unit, at)) return 0f;
            float v = p.hiddenRiskRelief > 0f ? AIScore.Risk(unit, at, map, p) * p.hiddenRiskRelief : 0f;
            if (p.ambushValue > 0f && unit.Firepower > 0 && unit.AttackRange >= 1 && unit.MinRange < 1) {
                int near = 0;
                foreach (Unit e in map.enemyUnits) {
                    if (e == null || e.tile == null || e.trasnporter != null || e.Strength <= 0) continue;
                    if (e.tile.Distance(at) <= AIPath.RangeThisTurn(e) + 1) near++;
                }
                v += Mathf.Min(near, 3) * p.ambushValue * AIScore.Worth(unit);
            }
            return v;
        }
    }
}
