using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DestroyUnits : AIMissionObjective
{
    public List<Unit> destroyThese;
    public override HexTile evaluateTiles(Unit unit, List<HexTile> moves) {
        throw new System.NotImplementedException();
    }

    public override bool MissionFinished() {
        return destroyThese.Count == 0;
    }
}

public abstract class AIMissionObjective : MonoBehaviour {
    //return true when the mission is either accomplished or failed
    public abstract bool MissionFinished();
    //Narrow the possibility space of the units possible moves to the one that helps accomplish the mission
    public abstract HexTile evaluateTiles(Unit unit, List<HexTile> moves);
}
