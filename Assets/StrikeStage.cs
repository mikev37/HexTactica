using UnityEngine;

/// <summary>
/// Put on a state of a unit's animator controller (as FireWhenReady is on Fire, CleanUpOnDeath on Dying): the unit is
/// told how far that stage of its strike has played (Unit.StrikeStageUpdate) and when it is over (Unit.StrikeStageEnded).
/// The animation decides how long each stage takes and what it looks like; the code only follows it.
///
///   - Launched, on the state its Launch trigger plays: nothing waits for it (play, the AI and replays carry on); once
///     its clip has played through, the unit stays on its pad as the clip left it until its side's turn ends.
///   - Landed, on the state its Fall trigger plays (a copy of the unit, appearing near its launch site): the copy travels
///     to its target as the state plays. The moment the state starts to blend out (or ends) is the impact: its
///     weaponImpact and strikeSound play, the tiles, buildings and units show what the strike did, and its Impact
///     trigger is set.
///   - Impact Done, on the state that follows: when it ends, the copy is removed.
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

    override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
        Unit unit = animator.GetComponent<Unit>();
        if (unit == null) return;
        // Blending out of this state: it is the current one and a transition is under way.
        bool leaving = animator.IsInTransition(layerIndex) && animator.GetCurrentAnimatorStateInfo(layerIndex).fullPathHash == stateInfo.fullPathHash;
        unit.StrikeStageUpdate(stage, stateInfo.normalizedTime, leaving);
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
        Unit unit = animator.GetComponent<Unit>();
        if (unit != null) unit.StrikeStageEnded(stage);
    }
}
