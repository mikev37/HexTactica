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

    /// <summary>
    /// An attack on whoever holds the target or stands beside it: worth more than its trade alone, more for every
    /// strength it takes, more again for driving the holder off or killing it. So a squad dug into the city is attacked,
    /// not waited out (its cover makes the plain trade a poor one).
    /// </summary>
    public override float AttackBonus(Unit attacker, Unit defender, CombatOutcome o) {
        if (target == null || defender == null || defender.tile == null) return 0f;
        int d = defender.tile.Distance(target);
        if (d > 1) return 0f;
        float b = 1f + 0.5f * o.damageToDefender + (o.defenderRetreats ? 1.5f : 0f) + (o.defenderDies ? 2f : 0f);
        return weight * b * (d == 0 ? 1f : 0.5f);
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
