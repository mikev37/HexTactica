using System.Collections;
using UnityEngine;

/// <summary>
/// One star of a mission's rating (on a mission flag, the campaign screen's briefing, the mission result): shown
/// earned or empty, and able to play being gained — its Animator's trigger if it has one, a sound, and a pop of its
/// size along a curve. Everything it looks like is the prefab's: the two objects, the curve, the sound.
/// </summary>
public class StarPip : MonoBehaviour {
    [Tooltip("Shown when the star has been earned.")]
    public GameObject earned;
    [Tooltip("Shown when it has not. Optional.")]
    public GameObject empty;
    [Tooltip("Optional: gets the Gain Trigger when the star is gained.")]
    public Animator animator;
    public string gainTrigger = "Gain";
    [Tooltip("Played when the star is gained. Optional.")]
    public AudioClip gainSound;
    [Tooltip("What the pop scales (the earned star, usually). Empty: no pop.")]
    public Transform popTarget;
    [Tooltip("The pop: the target's size over the pop's length (time 0 to 1), 1 being its own size.")]
    public AnimationCurve pop = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.6f, 1.35f), new Keyframe(1f, 1f));
    [Tooltip("How long the pop takes, in seconds.")]
    public float popSeconds = 0.45f;

    Vector3 restScale;
    bool restKnown;

    /// <summary>Show the star as earned or not, at once.</summary>
    public void Show(bool on) {
        if (earned != null) earned.SetActive(on);
        if (empty != null) empty.SetActive(!on);
    }

    /// <summary>The star is gained: shown as earned, with its animation (the caller may wait for it).</summary>
    public IEnumerator Gain() {
        Show(true);
        if (gainSound != null) GameSounds.Play(gainSound);
        if (animator != null && !string.IsNullOrEmpty(gainTrigger)) animator.SetTrigger(gainTrigger);
        if (popTarget == null || popSeconds <= 0f || pop == null) yield break;
        if (!restKnown) { restScale = popTarget.localScale; restKnown = true; }
        for (float t = 0f; t < popSeconds; t += Time.unscaledDeltaTime) {
            popTarget.localScale = restScale * pop.Evaluate(t / popSeconds);
            yield return null;
        }
        popTarget.localScale = restScale;
    }
}
