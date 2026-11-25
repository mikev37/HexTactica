using UnityEngine;
using HexTactica.AI;

/// <summary>
/// A scenario's AI Objective (Set AI Objective action): "go for this tile". On an AI side's GameObject, as its mission
/// objective (AIPlayer.objective), it pulls the AI's own decisions there by its world score (AITactical.StrategicBonus):
/// a unit that can take a building there is drawn onto it, every other unit into the ring around it, close enough to
/// fight whoever holds it. The AI still chooses who goes where, who attacks whom, and who pulls back to heal: several
/// squads sent at one city take different tiles around it of their own accord.
/// </summary>
public class ScenarioAIGoal : AIMissionObjective {

    public HexTile target;

    public override bool MissionFinished() { return false; }

    public override float TileBonus(Unit unit, HexTile tile, AIMap map) {
        if (target == null || tile == null || unit == null) return 0f;
        int d = tile.Distance(target);
        // Every step closer counts the same, however far it is (a pull fading with distance moves nobody from afar).
        float march = 0f;
        if (map != null && unit.tile != null && unit.AttackRange > 0) {
            int gained = map.Travel(unit, unit.tile, target) - map.Travel(unit, tile, target);
            march = weight * gained;
        }
        return march + Near(unit, tile, d);
    }

    float Near(Unit unit, HexTile tile, int d) {
        if (target.building != null && Building.CanCapture(unit)) {
            if (tile == target) return weight * 3f;
            return weight * 1.5f / (1 + d);
        }
        if (tile == target) return weight * 1.5f;
        int reach = Mathf.Max(1, unit.AttackRange);
        if (d >= Mathf.Max(1, unit.MinRange + 1) && d <= reach) return weight * 0.8f;
        return weight * 0.3f / (1 + d);
    }
}
