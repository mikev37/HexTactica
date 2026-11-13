using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A mission's flag on the campaign map (the Mission Flag prefab, placed in the campaign scene with its Mission): the
/// mission's name, the flag in the colour of the faction it is told from, a mark when it is complete, its three
/// stars (speed, aggression, defense), and a mark when it has been closed off. Clicking it picks the mission.
/// </summary>
public class MissionFlag : MonoBehaviour {
    public Button button;
    [Tooltip("Tinted with the mission's faction colour.")]
    public Graphic flag;
    public TMP_Text title;
    [Tooltip("Shown once the mission has been won.")]
    public GameObject completeMark;
    [Tooltip("Shown while the mission is closed off (another mission was attempted instead).")]
    public GameObject blockedMark;
    [Tooltip("Shown while this is the mission picked.")]
    public GameObject pickedMark;
    [Tooltip("The mission's stars, in order: speed, aggression, defense.")]
    public StarPip[] stars = new StarPip[0];
    [Tooltip("The flag's colour is multiplied by this while the mission is closed off.")]
    public Color blockedTint = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Header("Appearing (a mission just unlocked)")]
    [Tooltip("Optional: gets the Appear Trigger when the flag appears.")]
    public Animator animator;
    public string appearTrigger = "Appear";
    [Tooltip("Played as the flag appears. Optional.")]
    public AudioClip appearSound;
    [Tooltip("The flag's size as it appears, over the appearance (time 0 to 1), 1 being its own size.")]
    public AnimationCurve appear = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.7f, 1.2f), new Keyframe(1f, 1f));
    [Tooltip("How long appearing takes, in seconds.")]
    public float appearSeconds = 0.6f;

    Mission mission;
    /// <summary>The mission on the same object.</summary>
    public Mission Mission { get { if (mission == null) mission = GetComponent<Mission>(); return mission; } }

    float size = 1f, appearing = 1f;

    /// <summary>The flag's scale (the campaign screen keeps flags one size on screen as the map zooms).</summary>
    public void SetSize(float s) { size = s; Rescale(); }

    void Rescale() { transform.localScale = Vector3.one * (size * appearing); }

    /// <summary>Show <paramref name="m"/> as it stands: <paramref name="stars"/> are the stars shown as earned (the rest empty).</summary>
    public void Show(Mission m, Color colour, bool complete, bool blocked, MissionStars shown) {
        mission = m;
        if (title != null) title.text = m != null ? m.Title : "";
        if (flag != null) flag.color = blocked ? new Color(colour.r * blockedTint.r, colour.g * blockedTint.g, colour.b * blockedTint.b, colour.a * blockedTint.a) : colour;
        if (completeMark != null) completeMark.SetActive(complete);
        if (blockedMark != null) blockedMark.SetActive(blocked);
        if (button != null) button.interactable = !blocked;
        for (int i = 0; i < stars.Length && i < MissionRating.Each.Length; i++)
            if (stars[i] != null) stars[i].Show(MissionRating.Has(shown, MissionRating.Each[i]));
        Picked(false);
    }

    public void Picked(bool on) { if (pickedMark != null) pickedMark.SetActive(on); }

    /// <summary>The stars in <paramref name="gained"/> are gained one after the other, <paramref name="between"/> seconds apart.</summary>
    public IEnumerator GainStars(MissionStars gained, float between) {
        for (int i = 0; i < stars.Length && i < MissionRating.Each.Length; i++) {
            if (stars[i] == null || !MissionRating.Has(gained, MissionRating.Each[i])) continue;
            yield return stars[i].Gain();
            if (between > 0f) yield return new WaitForSecondsRealtime(between);
        }
    }

    /// <summary>The flag appears on the map (a mission just unlocked).</summary>
    public IEnumerator Appear() {
        if (appearSound != null) GameSounds.Play(appearSound);
        if (animator != null && !string.IsNullOrEmpty(appearTrigger)) animator.SetTrigger(appearTrigger);
        if (appearSeconds <= 0f || appear == null) yield break;
        for (float t = 0f; t < appearSeconds; t += Time.unscaledDeltaTime) {
            appearing = appear.Evaluate(t / appearSeconds);
            Rescale();
            yield return null;
        }
        appearing = 1f;
        Rescale();
    }
}
