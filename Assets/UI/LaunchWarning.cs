using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The missile launch warning: a banner, and the camera gliding to the launch site, when the player at the screen sees a
/// launch (Strikes.WarnsScreen: a unit of theirs within the launcher's Launch Seen Within, or sight of it).
///
///   - The banner shows for <see cref="showSeconds"/> or until the turn ends, whichever is first, then fades over
///     <see cref="fadeSeconds"/>.
///   - The camera glides to the site at the lowest priority (CameraRig.Notice): the player's own camera input or a
///     scenario's camera cuts it short.
///   - Clicking the banner cycles the camera through the sites launched from this turn that this player saw
///     (Strikes.SitesWarnedScreen, asked afresh on each click): a launch it did not see is never among them.
///
/// Everything it looks like is its prefab, Resources/LaunchWarning (made once by Editor/LaunchWarningPrefab, then
/// yours): text, colours, layout, sound and timings. The code only shows, hides, fades and counts.
/// </summary>
public class LaunchWarning : MonoBehaviour {

    /// <summary>The prefab's name in Resources.</summary>
    public const string PrefabName = "LaunchWarning";

    [Tooltip("The banner: shown on a launch, hidden when it has faded.")]
    public GameObject banner;
    [Tooltip("Fades the banner out (its alpha).")]
    public CanvasGroup fade;
    [Tooltip("Clicking it cycles the camera through this turn's launch sites this player saw.")]
    public Button button;
    [Tooltip("Optional: which site the camera is on, of how many (shown only with more than one).")]
    public TMP_Text count;
    [Tooltip("The count's text: {0} the site shown, {1} how many this turn.")]
    public string countFormat = "{0} / {1}";
    [Tooltip("Seconds the banner stays before it fades (unless the turn ends first).")]
    public float showSeconds = 10f;
    [Tooltip("Seconds it takes to fade away.")]
    public float fadeSeconds = 1f;
    [Tooltip("Seconds the camera takes to glide to a launch site.")]
    public float panSeconds = 0.8f;
    [Tooltip("Played on each launch warned of (empty: silent).")]
    public AudioSource sound;

    // The site the camera was last sent to (the list itself is the rules', Strikes.SitesWarnedScreen).
    HexTile current;
    float hideAt, fadeFrom;
    bool fading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook() {
        Strikes.Fired -= OnFired;
        Strikes.Fired += OnFired;
        GameController.TurnEnding -= OnTurnEnding;
        GameController.TurnEnding += OnTurnEnding;
    }

    static LaunchWarning instance;

    /// <summary>The warning in the open scene, made from its prefab if there is none yet. Null: no prefab (said once).</summary>
    static LaunchWarning Ensure() {
        if (instance != null) return instance;
        instance = FindAnyObjectByType<LaunchWarning>();
        if (instance != null) return instance;
        LaunchWarning prefab = Resources.Load<LaunchWarning>(PrefabName);
        if (prefab == null) {
            if (!missingSaid) { missingSaid = true; Debug.LogWarning("No Resources/" + PrefabName + " prefab: launches are not announced. Open the project in Unity to make it (Editor/LaunchWarningPrefab)."); }
            return null;
        }
        instance = Instantiate(prefab);
        instance.name = PrefabName;
        TransportPanel.EnsureEventSystem();
        return instance;
    }
    static bool missingSaid;

    static void OnFired(Unit u, Strike s, bool delayed) {
        if (!delayed || GameController.gc == null || GameController.gc.headless || !Strikes.WarnsScreen(s)) return;
        LaunchWarning w = Ensure();
        if (w != null) w.Warn(s.from);
    }

    static void OnTurnEnding(Player p) { if (instance != null) instance.FadeOut(); }


    void Awake() {
        instance = this;
        if (button != null) button.onClick.AddListener(Cycle);
        if (banner != null) banner.SetActive(false);
    }

    void OnDestroy() { if (instance == this) instance = null; }

    /// <summary>A launch from <paramref name="from"/> seen: the banner (again, from full), the camera to it.</summary>
    public void Warn(HexTile from) {
        if (from == null) return;
        current = from;
        if (banner != null) banner.SetActive(true);
        if (fade != null) fade.alpha = 1f;
        fading = false;
        hideAt = Time.unscaledTime + Mathf.Max(0f, showSeconds);
        ShowCount();
        if (sound != null && sound.clip != null && GameSounds.On) sound.Play();
        Look(from);
    }

    /// <summary>The banner clicked: the camera to the next site launched from this turn that this player saw.</summary>
    public void Cycle() {
        List<HexTile> sites = Strikes.SitesWarnedScreen();
        if (sites.Count == 0) { current = null; ShowCount(); return; }
        current = sites[(sites.IndexOf(current) + 1) % sites.Count];
        ShowCount();
        Look(current);
    }

    void Look(HexTile t) {
        CameraRig rig = CameraRig.Instance;
        if (rig != null && t != null) rig.Notice(ScenarioGame.TilePosition(t.x, t.y), panSeconds);
    }

    void ShowCount() {
        if (count == null) return;
        List<HexTile> sites = Strikes.SitesWarnedScreen();
        int at = sites.IndexOf(current);
        count.gameObject.SetActive(sites.Count > 1 && at >= 0);
        if (sites.Count > 1 && at >= 0) count.text = string.Format(countFormat, at + 1, sites.Count);
    }

    void FadeOut() {
        if (banner == null || !banner.activeSelf || fading) return;
        fading = true;
        fadeFrom = Time.unscaledTime;
    }

    void Update() {
        if (banner == null || !banner.activeSelf) return;
        if (!fading && Time.unscaledTime >= hideAt) FadeOut();
        if (!fading) return;
        float t = fadeSeconds <= 0f ? 1f : (Time.unscaledTime - fadeFrom) / fadeSeconds;
        if (fade != null) fade.alpha = 1f - Mathf.Clamp01(t);
        if (t >= 1f) { banner.SetActive(false); fading = false; }
    }
}
