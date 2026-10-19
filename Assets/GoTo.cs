using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HexTactica.AI;

/// <summary>Move the army toward a specific tile.</summary>
public class GoTo : AIMissionObjective {

    public HexTile target;

    public void Start() {
        if (target == null) target = HexGenerator.hg.get(0, 0, 0);
    }

    public override bool MissionFinished() {
        return target != null && target.occupier != null;
    }

    public override float TileBonus(Unit unit, HexTile tile, AIMap map) {
        if (target == null || tile == null) return 0f;
        return weight * 0.6f / (1 + tile.Distance(target));
    }

    public override HexTile evaluateTiles(Unit unit, List<HexTile> moves) {
        if (moves == null || moves.Count == 0) return unit != null ? unit.tile : null;
        if (target == null) return moves[0];

        List<HexTile> sorted = new List<HexTile>(moves);
        sorted.Sort((x, y) => {
            int c = x.Distance(target).CompareTo(y.Distance(target));
            if (c != 0) return c;
            return CombatMath.CompareTiles(x, y);
        });
        return sorted[0];
    }
}
