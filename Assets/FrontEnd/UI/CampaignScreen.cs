using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The campaign screen (a campaign scene: the Campaign scene, or any other made like it): the campaign's map with
/// its missions on it, each a Mission Flag (Mission + MissionFlag) placed under Flags where it stands on the map.
/// Flags of missions the player cannot see yet are hidden while the game runs. Picking a flag plays the mission's intro
/// lines in the dialogue box, then shows its briefing with Start. Coming back from a mission, its result is played
/// out: the stars it gained on its flag, each newly unlocked mission's flag appearing with the view zoomed onto it,
/// then the mission's closing lines.
/// The map pans and zooms as the game board does: the wheel (or + and -) zooms about the cursor, dragging with the
/// right or middle mouse button or the arrow keys pan, Home shows the whole map again. The view never leaves the
/// picture, and the flags keep their size on screen as it zooms, so zooming in spreads crowded flags apart.
/// Everything on screen is the scene's and the flag prefab's; this component finds its parts through its fields.
/// </summary>
public class CampaignScreen : MonoBehaviour {

    [Header("The map")]
    [Tooltip("The campaign's picture: the flags stand on it, and it is what zooms and pans. Its parent is the window it is seen through.")]
    public RectTransform map;
    [Tooltip("Where the missions are: a child of the map, stretched over it, holding a Mission Flag for every mission of this campaign.")]
    public RectTransform flags;
    [Tooltip("The flag colour of a mission with no faction.")]
    public Color noFactionColor = Color.white;
    [Tooltip("Catches clicks while a result is being played out. Optional.")]
    public GameObject busyBlocker;

    [Header("The picked mission")]
    public GameObject missionPanel;
    public TMP_Text missionTitle;
    [Tooltip("Whose point of view the mission is told from.")]
    public TMP_Text missionFaction;
    public TMP_Text missionBriefing;
    [Tooltip("The mission's pars, and how the player has done so far.")]
    public TMP_Text missionPars;
    [Tooltip("The picked mission's stars, in order: speed, aggression, defense. Optional.")]
    public StarPip[] missionStars = new StarPip[0];
    public Button startButton;
    [Tooltip("The pars: {0} turns, {1} enemy units to destroy, {2} units that may be lost.")]
    [TextArea(2, 5)] public string parsFormat = "Speed: win within {0} turns\nAggression: destroy at least {1} enemy units\nDefense: lose no more than {2} units";
    [Tooltip("The pars of a mission with no speed star (Par Turns 0): {1} and {2} as above.")]
    [TextArea(2, 5)] public string parsFormatNoSpeed = "Aggression: destroy at least {1} enemy units\nDefense: lose no more than {2} units";
    [Tooltip("Added under the pars of a mission already won: {0} the stars earned, {1} its best turns.")]
    public string completedFormat = "\nCompleted: {0} of 3 stars, best {1} turns";
    [Tooltip("Added when the mission's scenario file is not in the campaign's folders: {0} the file's name.")]
    public string missingScenario = "\nThe scenario file \"{0}\" is missing from the campaign folder.";

    [Header("Pan and zoom (as the game board)")]
    [Tooltip("The most the map can be zoomed in, in times its own size (1: zooming off).")]
    public float maxZoom = 4f;
    [Tooltip("How much one wheel notch (or + / -) zooms, as a fraction of the zoom.")]
    public float zoomStep = 0.12f;
    [Tooltip("Arrow-key panning, in view heights per second.")]
    public float panSpeed = 1.2f;
    [Tooltip("The flags keep their size on screen as the map zooms (zooming in spreads them apart). Off: they grow with the map.")]
    public bool flagsKeepSize = true;

    [Header("Playing out a result")]
    [Tooltip("Seconds before the result starts to play, once the screen is up.")]
    public float resultDelay = 0.6f;
    [Tooltip("Seconds between one star gained and the next.")]
    public float starInterval = 0.25f;
    [Tooltip("How far the view zooms onto a newly unlocked mission (1: not at all; already zoomed in further: stays at that zoom).")]
    public float unlockZoom = 2f;
    [Tooltip("Seconds the zoom in, and the zoom back out, each take.")]
    public float zoomSeconds = 0.8f;
    [Tooltip("The zoom's progress over its length (both 0 to 1).")]
    public AnimationCurve zoomEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Seconds the view rests on a new flag once it has appeared.")]
    public float unlockHold = 0.7f;

    /// <summary>Every mission flag of this campaign (the Flags object's), shown or not.</summary>
    readonly List<MissionFlag> made = new List<MissionFlag>();
    readonly Dictionary<Mission, MissionInfo> infos = new Dictionary<Mission, MissionInfo>();
    /// <summary>Missions whose intro lines have been heard since the game was started (they are not said again on every click).</summary>
    static readonly HashSet<string> heard = new HashSet<string>();
    Mission picked;
    bool busy;
    Vector3 mapScale;
    Vector2 mapAt;
    Vector2 dragFrom;
    bool dragging;
    Canvas canvas;

    /// <summary>The map's zoom: its scale over its own (1: the whole map).</summary>
    float ZoomNow { get { return map != null && mapScale.x != 0f ? map.localScale.x / mapScale.x : 1f; } }

    void Awake() {
        TransportPanel.EnsureEventSystem();
        DialogueBox.Ensure();
    }

    void Start() {
        GameController.Paused = false;
        ScenarioDialogue.Clear();   // nothing left over from a game
        if (map != null) { mapScale = map.localScale; mapAt = map.anchoredPosition; }
        canvas = GetComponentInParent<Canvas>();
        if (missionPanel != null) missionPanel.SetActive(false);
        CampaignProgress.Outcome result = CampaignProgress.TakePending();
        Mission played = null;
        if (flags != null) foreach (Mission m in flags.GetComponentsInChildren<Mission>(true)) {
            MissionFlag f = m.GetComponent<MissionFlag>();
            if (f == null) { Debug.LogWarning(m.name + " has no MissionFlag: it can't be shown on the campaign map.", m); continue; }
            MissionInfo info = m.Info;
            infos[m] = info;
            made.Add(f);
            if (f.button != null) { Mission mission = m; f.button.onClick.AddListener(() => Pick(mission)); }
            if (result != null && result.mission != null && result.mission.key == info.key) played = m;
            // A mission just unlocked appears when the result is played out; the stars just gained are gained on screen.
            bool appearing = result != null && result.unlocked.Contains(info.key) && !CampaignProgress.Get(info).attempted && !info.availableAtStart;
            MissionStars stars = CampaignProgress.Get(info).stars;
            if (m == played) stars &= ~result.gained;
            Show(m, stars);
            if (appearing) f.gameObject.SetActive(false);
        }
        Busy(false);
        if (played != null) StartCoroutine(PlayResult(result, played));
    }

    /// <summary>Show the mission's flag as its progress stands (hidden while the player cannot see it).</summary>
    void Show(Mission m, MissionStars stars) {
        MissionFlag f = m.GetComponent<MissionFlag>();
        MissionInfo info = Info(m);
        bool visible = CampaignProgress.Visible(info);
        f.gameObject.SetActive(visible);
        if (!visible) return;
        f.Show(m, m.faction != null ? m.faction.flagColor : noFactionColor, CampaignProgress.Get(info).completed, CampaignProgress.Blocked(info), stars);
        f.SetSize(flagsKeepSize ? 1f / ZoomNow : 1f);
    }

    MissionInfo Info(Mission m) {
        MissionInfo i;
        if (!infos.TryGetValue(m, out i)) { i = m.Info; infos[m] = i; }
        return i;
    }

    MissionFlag FlagOf(Mission m) { return made.Find(f => f != null && f.Mission == m); }

    void Busy(bool on) {
        busy = on;
        if (busyBlocker != null) busyBlocker.SetActive(on);
    }

    // ------------------------------------------------------------------ picking a mission

    /// <summary>A flag was clicked: the mission's intro lines (the first time), then its briefing.</summary>
    public void Pick(Mission m) {
        if (busy || m == null || !CampaignProgress.Playable(Info(m))) return;
        picked = m;
        foreach (MissionFlag f in made) if (f != null) f.Picked(f.Mission == m);
        if (missionPanel != null) missionPanel.SetActive(false);
        StartCoroutine(Introduce(m));
    }

    IEnumerator Introduce(Mission m) {
        if (m.intro != null && m.intro.Count > 0 && heard.Add(m.Key)) {
            Busy(true);
            yield return Say(m.intro);
            Busy(false);
        }
        if (picked == m) ShowMission(m);
    }

    /// <summary>The lines in the dialogue box, until the last is closed.</summary>
    static IEnumerator Say(List<DialogueLine> lines) {
        int last = ScenarioDialogue.Enqueue(lines);
        while (!ScenarioDialogue.Closed(last)) yield return null;
    }

    void ShowMission(Mission m) {
        CampaignProgress.Entry e = CampaignProgress.Get(Info(m));
        MissionInfo info = Info(m);
        bool file = GameFiles.CampaignScenarioPath(info.ScenarioName) != null;
        if (missionTitle != null) missionTitle.text = info.Title;
        if (missionFaction != null) {
            missionFaction.text = m.faction != null ? (string.IsNullOrEmpty(m.faction.campaignName) ? m.faction.name : m.faction.campaignName) : "";
            missionFaction.color = m.faction != null ? m.faction.flagColor : noFactionColor;
        }
        if (missionBriefing != null) missionBriefing.text = m.briefing;
        if (missionPars != null) {
            string pars = string.Format(m.parTurns > 0 ? parsFormat : parsFormatNoSpeed, m.parTurns, m.parKills, m.parLosses);
            if (e.completed) pars += string.Format(completedFormat, MissionRating.Count(e.stars), e.bestTurns);
            if (!file) pars += string.Format(missingScenario, info.ScenarioName);
            missionPars.text = pars;
        }
        for (int i = 0; i < missionStars.Length && i < MissionRating.Each.Length; i++)
            if (missionStars[i] != null) missionStars[i].Show(MissionRating.Has(e.stars, MissionRating.Each[i]));
        if (startButton != null) startButton.interactable = file;
        if (missionPanel != null) missionPanel.SetActive(true);
    }

    /// <summary>The briefing's Start: the game scene on the mission's scenario, under the mission's own pre-game screen.</summary>
    public void StartMission() {
        if (busy || picked == null) return;
        MatchSetup.PlayMission(Info(picked));
    }

    /// <summary>The briefing's Close.</summary>
    public void CloseMission() {
        picked = null;
        foreach (MissionFlag f in made) if (f != null) f.Picked(false);
        if (missionPanel != null) missionPanel.SetActive(false);
    }

    public void Back() { MatchSetup.ToMainMenu(); }

    // ------------------------------------------------------------------ a mission's result, played out

    IEnumerator PlayResult(CampaignProgress.Outcome o, Mission played) {
        Busy(true);
        if (resultDelay > 0f) yield return new WaitForSecondsRealtime(resultDelay);
        MissionFlag flag = FlagOf(played);
        if (flag != null) {
            // Its first win: the complete mark was shown with the flag; the stars come one by one.
            yield return flag.GainStars(o.gained, starInterval);
        }
        Vector3 viewScale = map != null ? map.localScale : Vector3.one;
        Vector2 viewAt = map != null ? map.anchoredPosition : Vector2.zero;
        bool zoomed = false;
        foreach (MissionFlag f in made) {
            if (f == null || f.gameObject.activeSelf || !o.unlocked.Contains(Info(f.Mission).key)) continue;
            zoomed = true;
            yield return Zoom(map != null ? (Vector2)map.InverseTransformPoint(f.transform.position) : Vector2.zero, Mathf.Max(unlockZoom, ZoomNow));
            Show(f.Mission, CampaignProgress.Get(Info(f.Mission)).stars);
            if (f.gameObject.activeSelf) yield return f.Appear();
            if (unlockHold > 0f) yield return new WaitForSecondsRealtime(unlockHold);
        }
        if (zoomed) yield return Glide(viewScale, viewAt);   // back to where the player was looking
        // Missions this attempt closed off show it now (their flags were made as they stand).
        List<DialogueLine> lines = o.tally.won ? played.outro : played.outroLost;
        if (lines != null && lines.Count > 0) yield return Say(lines);
        Busy(false);
    }

    /// <summary>The view glides so that <paramref name="local"/> (a point on the map, in its own units from its pivot) is in the middle (as near as the map's edges allow), at <paramref name="zoom"/> times the map's own size.</summary>
    IEnumerator Zoom(Vector2 local, float zoom) {
        if (map == null) yield break;
        zoom = Mathf.Clamp(zoom, 1f, Mathf.Max(1f, maxZoom));
        Vector3 toScale = mapScale * zoom;
        Vector2 toAt = MapView.CentreOn(mapAt, local, toScale.x);
        yield return Glide(toScale, toAt);
    }

    /// <summary>The view glides to this scale and position of the map (kept covering the view).</summary>
    IEnumerator Glide(Vector3 toScale, Vector2 toAt) {
        if (map == null) yield break;
        toAt = Clamped(toAt, toScale.x);
        Vector3 fromScale = map.localScale;
        Vector2 fromAt = map.anchoredPosition;
        for (float t = 0f; t < zoomSeconds; t += Time.unscaledDeltaTime) {
            float k = zoomEase != null ? zoomEase.Evaluate(t / zoomSeconds) : t / zoomSeconds;
            map.localScale = fromScale + (toScale - fromScale) * k;
            map.anchoredPosition = fromAt + (toAt - fromAt) * k;
            FitFlags();
            yield return null;
        }
        map.localScale = toScale;
        map.anchoredPosition = toAt;
        FitFlags();
    }

    // ------------------------------------------------------------------ pan and zoom by the player

    Vector2 ViewSize { get { RectTransform v = map != null ? map.parent as RectTransform : null; return v != null ? v.rect.size : (map != null ? map.rect.size : Vector2.zero); } }

    Vector2 Clamped(Vector2 at, float scale) { return MapView.Clamp(at, mapAt, map.rect.size, scale, ViewSize); }

    Camera UICamera { get { return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null; } }

    /// <summary>The cursor is over the picked mission's panel (the wheel and drags there are the panel's).</summary>
    bool OverPanel(Vector2 screen) {
        RectTransform panel = missionPanel != null && missionPanel.activeInHierarchy ? missionPanel.transform as RectTransform : null;
        return panel != null && RectTransformUtility.RectangleContainsScreenPoint(panel, screen, UICamera);
    }

    /// <summary>The cursor in the view's own units (the map's parent), or false if it can't be told.</summary>
    bool InView(Vector2 screen, out Vector2 local) {
        local = Vector2.zero;
        RectTransform view = map != null ? map.parent as RectTransform : null;
        return view != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(view, screen, UICamera, out local);
    }

    void LateUpdate() {
        if (map == null || busy || !Application.isFocused) { dragging = false; return; }
        Vector2 mouse = Input.mousePosition;
        bool inWindow = mouse.x >= 0f && mouse.y >= 0f && mouse.x <= Screen.width && mouse.y <= Screen.height;
        bool talking = ScenarioDialogue.Showing;

        if (Input.GetKeyDown(KeyCode.Home)) { map.localScale = mapScale; map.anchoredPosition = mapAt; FitFlags(); }

        // Arrows pan.
        Vector2 move = Vector2.zero;
        if (Input.GetKey(KeyCode.LeftArrow)) move.x -= 1f;
        if (Input.GetKey(KeyCode.RightArrow)) move.x += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) move.y -= 1f;
        if (Input.GetKey(KeyCode.UpArrow)) move.y += 1f;
        if (move.x != 0f || move.y != 0f) map.anchoredPosition = Clamped(map.anchoredPosition - move * ViewSize.y * panSpeed * Time.unscaledDeltaTime, map.localScale.x);

        // Right or middle drag pans: the point grabbed stays under the cursor.
        Vector2 here;
        bool button = Input.GetMouseButton(1) || Input.GetMouseButton(2);
        if ((Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) && inWindow && !OverPanel(mouse) && InView(mouse, out here)) { dragging = true; dragFrom = here; }
        if (!button) dragging = false;
        if (dragging && InView(mouse, out here)) {
            map.anchoredPosition = Clamped(map.anchoredPosition + (here - dragFrom), map.localScale.x);
            dragFrom = here;
        }

        // The wheel (or + / -) zooms about the cursor.
        float wheel = inWindow && !OverPanel(mouse) && !talking ? Input.mouseScrollDelta.y : 0f;
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) wheel += 1f;
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) wheel -= 1f;
        if (wheel != 0f && maxZoom > 1f) {
            float from = map.localScale.x;
            float to = Mathf.Clamp(from * (1f + zoomStep * wheel), mapScale.x, mapScale.x * maxZoom);
            Vector2 point;
            if (to != from && RectTransformUtility.ScreenPointToLocalPointInRectangle(map, inWindow ? mouse : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), UICamera, out point)) {
                Vector2 at = MapView.ZoomAbout(map.anchoredPosition, point, from, to);
                map.localScale = mapScale * (to / mapScale.x);
                map.anchoredPosition = Clamped(at, to);
                FitFlags();
            }
        }
    }

    /// <summary>The flags back to their own size on screen, whatever the map's zoom (if Flags Keep Size).</summary>
    void FitFlags() {
        float size = flagsKeepSize ? 1f / ZoomNow : 1f;
        foreach (MissionFlag f in made) if (f != null) f.SetSize(size);
    }
}
