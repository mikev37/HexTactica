using System.Collections.Generic;
using UnityEngine;
using HexTactica.AI;

/// <summary>
/// Keep the army connected. Biases every unit toward protecting the corridor rather
/// than toward the fight — the objective that makes a supply game play like one.
/// </summary>
public class MaintainSupply : AIMissionObjective {

    [Tooltip("Fraction of the army that must be in supply for the mission to count as held")]
    [Range(0f, 1f)]
    public float requiredFraction = 0.8f;

    public override bool MissionFinished() {
        // A sustainment objective is never "done"; it is held or it is lost.
        return false;
    }

    public override float TileBonus(Unit unit, HexTile tile, AIMap map) {
        if (map == null || tile == null) return 0f;

        float bonus = 0f;

        // Standing in the corridor keeps it open, because an enemy unit in a tile
        // severs supply through it — occupying the route denies that.
        int depth = map.SupplyDepth(tile);
        if (depth > 0) bonus += depth * 0.03f;

        // Anywhere someone is shouting for help is where this objective wants bodies.
        bonus += AIScore.Relief(map.Distress(tile)) * 0.8f;

        // Tiles adjacent to a stranded hub are worth holding: relieving the hub is
        // worth more than the tile.
        for (int i = 0; i < map.ownHubs.Count; i++) {
            SupplyPoint hub = map.ownHubs[i];
            if (hub == null || hub.connected || hub.myUnit == null || hub.myUnit.tile == null) continue;
            int d = tile.Distance(hub.myUnit.tile);
            if (d <= 3) bonus += (4 - d) * 0.15f;
        }

        return bonus * weight;
    }

    /// <summary>Fraction of this player's army currently in supply.</summary>
    public float SupplyHealth(AIMap map) {
        if (map == null || map.ownUnits.Count == 0) return 1f;
        int supplied = 0;
        for (int i = 0; i < map.ownUnits.Count; i++) {
            Unit u = map.ownUnits[i];
            if (u != null && u.tile != null && map.SupplyDepth(u.tile) > 0) supplied++;
        }
        return supplied / (float)map.ownUnits.Count;
    }
}

/// <summary>Take and hold a specific building. Infantry get pulled toward it; everyone else screens.</summary>
public class SeizeBuilding : AIMissionObjective {

    public Building target;

    public override bool MissionFinished() {
        return target == null || (target.owner != null && OwnedByMe());
    }

    bool OwnedByMe() {
        Player me = GetComponent<Player>();
        return me != null && target.owner == me;
    }

    public override float TileBonus(Unit unit, HexTile tile, AIMap map) {
        if (target == null || target.tile == null || tile == null) return 0f;

        int d = tile.Distance(target.tile);

        if (Building.CanCapture(unit)) {
            if (tile == target.tile) return weight * (target.IsHQ ? 20f : 3f);
            return weight * 1.5f / (1 + d);
        }

        // Everything else forms a ring: close enough to cover the capture, not on it.
        if (d >= 1 && d <= 3) return weight * 0.5f / d;
        return weight * 0.25f / (1 + d);
    }
}

/// <summary>Hold a tile for a number of turns. The classic scenario objective.</summary>
public class HoldTile : AIMissionObjective {

    public HexTile target;
    public int turnsRequired = 10;
    public int turnsHeld;

    public override bool MissionFinished() {
        return turnsHeld >= turnsRequired;
    }

    public void Tick(Player me) {
        if (target != null && target.occupier != null && target.occupier.owner == me) turnsHeld++;
        else turnsHeld = 0;
    }

    public override float TileBonus(Unit unit, HexTile tile, AIMap map) {
        if (target == null || tile == null) return 0f;
        if (tile == target) return weight * 2.5f;
        int d = tile.Distance(target);
        if (d <= 2) return weight * 0.8f / d;
        return weight * 0.3f / (1 + d);
    }
}
