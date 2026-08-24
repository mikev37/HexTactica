using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class GoTo : AIMissionObjective {

    public HexTile target;

    public void Start() {
        if (target == null)
            target = HexGenerator.hg.get(0, 0, 0);
    }
    public override HexTile evaluateTiles(Unit unit, List<HexTile> moves) {

        moves.Sort((x, y) => (int)(x.Distance(target) - y.Distance(target)));
        return moves[0];
    }

    public override bool MissionFinished() {
        return false;
    }
}
