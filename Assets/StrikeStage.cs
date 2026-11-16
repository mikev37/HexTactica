using UnityEngine;

/// <summary>
/// Put on a state of a unit's animator controller (as FireWhenReady is on Fire, CleanUpOnDeath on Dying): when the state
/// ends, the unit is told that stage of its strike has played out (Unit.StrikeStageEnded). The animation decides how long
/// each stage takes and what it looks like; the code only waits for it.
///
///   - Launched, on the state its Launch trigger plays: it has left the screen; the unit is removed.
///   - Landed, on the state its Fall trigger plays (a copy of the unit, falling onto the tile its strike lands on): it has
///     hit the ground; its strikeSound plays and its Impact trigger is set.
///   - Impact Done, on the state its Impact trigger plays (the explosion, big enough to hide the tile): the copy is
///     removed. The tile's new ground, the building's damage and the units' hits appear at the clip's StrikeCovered
///     animation event (Unit.StrikeCovered), or here if the clip has none.
///
/// The unit's Flying bool is true from its launch on (the pad unit as Launch is set; the copy from its first frame), so
/// its animator can keep it out of on-the-pad states.
///
/// A unit whose controller has no such trigger skips that stage at once (Unit.HasAnimatorParameter).
/// </summary>
public class StrikeStage : StateMachineBehaviour {

    public enum Stage { Launched = 0, Landed = 1, ImpactDone = 2 }

    [Tooltip("Which stage of a strike ends when this state ends.")]
    public Stage stage;

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
        Unit unit = animator.GetComponent<Unit>();
        if (unit != null) unit.StrikeStageEnded(stage);
    }
}
