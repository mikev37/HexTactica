using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HexTactica.AI;

/// <summary>
/// A mission an AI player is trying to accomplish.
///
/// The original comment on evaluateTiles — "narrow the possibility space of the unit's
/// possible moves to the one that helps accomplish the mission" — is the right design
/// and is preserved here. Objectives filter and bias candidates that the tactical layer
/// already produced; they do not seize control of units. That is what lets an objective
/// compose with tactics rather than fight them, and it means a bad objective degrades
/// play rather than breaking it.
/// </summary>
public abstract class AIMissionObjective : MonoBehaviour {

    [Tooltip("How strongly this objective bends unit decisions")]
    public float weight = 1f;

    /// <summary>Return true when the mission is either accomplished or failed.</summary>
    public abstract bool MissionFinished();

    /// <summary>
    /// Additional value this objective places on a unit standing on a tile, as a
    /// fraction of that unit's own worth (1.0 = as valuable as the unit itself). The
    /// tactical layer multiplies by the unit's gold value, so objectives stay meaningful
    /// whether the roster costs hundreds or hundreds of thousands. Return 0 for tiles the
    /// objective does not care about. This is the composable path most objectives should use.
    /// </summary>
    public virtual float TileBonus(Unit unit, HexTile tile, AIMap map) { return 0f; }

    /// <summary>
    /// True if this objective wants to fully dictate this unit's action, bypassing the
    /// tactical layer. Should be rare — reserve it for scripted behaviour.
    /// </summary>
    public virtual bool OverridesTactics(Unit unit, AIMap map) { return false; }

    /// <summary>Only consulted when OverridesTactics returns true.</summary>
    public virtual AIAction Decide(Unit unit, AIMap map, AIProfile profile, Posture posture) {
        return AIAction.Wait(unit, "objective: no directive");
    }

    /// <summary>
    /// Legacy entry point. Kept because older scenes reference it; it now falls back to
    /// picking the highest-bonus tile rather than throwing.
    /// </summary>
    public virtual HexTile evaluateTiles(Unit unit, List<HexTile> moves) {
        if (moves == null || moves.Count == 0) return unit != null ? unit.tile : null;
        return moves[0];
    }
}

/// <summary>Eliminate a specific set of enemy units.</summary>
public class DestroyUnits : AIMissionObjective {

    public List<Unit> destroyThese;

    public override bool MissionFinished() {
        if (destroyThese == null) return true;
        destroyThese.RemoveAll(u => u == null || u.Strength <= 0);
        return destroyThese.Count == 0;
    }

    public override float TileBonus(Unit unit, HexTile tile, AIMap map) {
        if (destroyThese == null || destroyThese.Count == 0 || tile == null) return 0f;

        float best = 0f;
        for (int i = 0; i < destroyThese.Count; i++) {
            Unit target = destroyThese[i];
            if (target == null || target.tile == null || target.Strength <= 0) continue;

            int d = tile.Distance(target.tile);
            // Being in firing range of a marked target is worth a great deal; being
            // merely closer to it is worth a little.
            float bonus = d <= unit.AttackRange && d > unit.MinRange ? 1.0f : 0.5f / (1 + d);
            if (bonus > best) best = bonus;
        }
        return best * weight;
    }

    /// <summary>Previously this threw NotImplementedException, in the exact code path
    /// the fallback existed to handle. It now steers toward the nearest marked target.</summary>
    public override HexTile evaluateTiles(Unit unit, List<HexTile> moves) {
        if (moves == null || moves.Count == 0) return unit != null ? unit.tile : null;

        HexTile best = moves[0];
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < moves.Count; i++) {
            float score = TileBonus(unit, moves[i], null);
            if (score > bestScore) { bestScore = score; best = moves[i]; }
        }
        return best;
    }
}
