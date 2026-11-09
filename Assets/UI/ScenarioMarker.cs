using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// A marker a scenario puts on a tile (a Show Marker action): "go here", "defend this", "the convoy lands here". One
/// prefab per kind of marker in Assets/Resources/ScenarioMarkers, named by its prefab name in the action; this
/// component is on its root. Its look is the prefab's (sprites, animations, particles: all restarted when it is shown);
/// the parts below are optional. The default one (Marker) is made by HexTactica ▸ Scenario Triggers ▸ Recreate
/// Dialogue Prefab (Editor/DialoguePrefab).
/// </summary>
public class ScenarioMarker : MonoBehaviour {

    [Tooltip("Shows the action's Text (hidden when it has none).")]
    public TMP_Text label;
    [Tooltip("Bobs up and down (an arrow pointing at the tile). Empty: nothing bobs.")]
    public Transform bob;
    [Tooltip("How far it bobs, in world units.")]
    public float bobHeight = 0.6f;
    [Tooltip("Grows and shrinks (a ring round the tile). Empty: nothing pulses.")]
    public Transform pulse;
    [Tooltip("How much it grows at most (1: not at all).")]
    public float pulseScale = 1.15f;
    [Tooltip("Bobs and pulses per second.")]
    public float speed = 1.2f;

    Vector3 bobFrom, pulseFrom;

    public const string Folder = "ScenarioMarkers/";

    void Awake() {
        if (bob != null) bobFrom = bob.localPosition;
        if (pulse != null) pulseFrom = pulse.localScale;
    }

    public void Show(string text) {
        if (label != null) { label.text = text ?? ""; label.gameObject.SetActive(!string.IsNullOrEmpty(text)); }
        foreach (ParticleSystem p in GetComponentsInChildren<ParticleSystem>(true)) { p.Clear(); p.Play(); }
        foreach (Animator a in GetComponentsInChildren<Animator>(true)) { a.Rebind(); a.Update(0f); }
    }

    void Update() {
        float s = (Mathf.Sin(Time.unscaledTime * speed * Mathf.PI * 2f) + 1f) * 0.5f;
        if (bob != null) bob.localPosition = bobFrom + Vector3.up * bobHeight * s;
        if (pulse != null) pulse.localScale = pulseFrom * Mathf.Lerp(1f, pulseScale, s);
    }
}
