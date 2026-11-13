using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The campaign screen, the hub of every campaign (the Campaign scene). Its own map is one campaign; it also loads
/// each campaign scene listed under Campaigns and lays all their maps out in one view, each where its entry puts it.
/// The campaigns the player has opened are shown, each with a tab to jump to it; the player can also pan straight
/// across from one to the next. A campaign is a CampaignMap: a map with its missions on it, each a Mission Flag
/// (Mission + MissionFlag) placed under its Flags where it stands on the map.
/// Flags of missions the player cannot see yet are hidden while the game runs. Picking a flag plays the mission's intro
/// lines in the dialogue box, then shows its briefing with Start. Coming back from a mission, its result is played
/// out: the stars it gained on its flag, each newly unlocked mission's flag appearing with the view zoomed onto it,
/// each newly opened campaign appearing with the view taken to it, then the mission's closing lines.
/// The view pans and zooms as the game board does: the wheel (or + and -) zooms about the cursor, dragging with the
/// right or middle mouse button or the arrow keys pan, Home shows the whole of the campaign in view again. The view
/// never leaves the open campaigns' maps, and the flags keep their size on screen as it zooms, so zooming in spreads
/// crowded flags apart.
/// Everything on screen is the scenes' and the flag prefab's; this component finds its parts through its fields.
/// </summary>
public class CampaignScreen : MonoBehaviour {

    /// <summary>A campaign scene shown on the hub, and where its map stands.</summary>
    [Serializable]
    public class CampaignSlot {
        [Tooltip("The campaign's scene (it must be in the build). This scene itself may be listed, to place its own map.")]
        [CampaignScene] public string scene = "";
        [Tooltip("Where its map's middle stands, in steps of the hub's own map's size (plus Map Gap): 0,0 is the hub's own map's place, 1,0 the next place to its right, 0,-1 the place below it.")]
        public Vector2 at;
    }

    [Header("The campaigns")]
    [Tooltip("This scene's own campaign map (its picture; the flags stand on it). Its parent is the window every map is seen through.")]
    public RectTransform map;
    [Tooltip("This scene's own map's missions: a child of the map, stretched over it, holding a Mission Flag per mission. (The map's CampaignMap names it too.)")]
    public RectTransform flags;
    [Tooltip("Other campaign scenes, loaded with this one and laid out beside its map. In this order on the tabs, after this scene's own map if it is not listed.")]
    public List<CampaignSlot> campaigns = new List<CampaignSlot>();
    [Tooltip("The empty space between neighbouring places, as a fraction of the hub's own map's size. Each map keeps its own size: a map larger than the hub's may need its place further out.")]
    public float mapGap = 0.08f;
    [Tooltip("The flag colour of a mission with no faction.")]
    public Color noFactionColor = Color.white;
    [Tooltip("Catches clicks while a result is being played out. Optional.")]
    public GameObject busyBlocker;

    [Header("Campaign tabs (one per open campaign; hidden while only one is open)")]
    [Tooltip("The row the tabs go in. Empty: a row is made across the top of the screen.")]
    public RectTransform tabs;
    [Tooltip("The button copied for each tab (its first text shows the campaign's name). Empty: the screen's first button outside the flags and the mission panel (the Main menu button) is copied.")]
    public Button tabTemplate;
    [Tooltip("Shows the name of the campaign in view. Optional.")]
    public TMP_Text campaignTitle;

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
    [Tooltip("The most the view can be zoomed in, in times a map's own size (1: zooming off).")]
    public float maxZoom = 4f;
    [Tooltip("How much one wheel notch (or + / -) zooms, as a fraction of the zoom.")]
    public float zoomStep = 0.12f;
    [Tooltip("Arrow-key panning, in view heights per second.")]
    public float panSpeed = 1.2f;
    [Tooltip("The flags keep their size on screen as the view zooms (zooming in spreads them apart). Off: they grow with the map.")]
    public bool flagsKeepSize = true;

    [Header("Playing out a result")]
    [Tooltip("Seconds before the result starts to play, once the screen is up.")]
    public float resultDelay = 0.6f;
    [Tooltip("Seconds between one star gained and the next.")]
    public float starInterval = 0.25f;
    [Tooltip("How far the view zooms onto a newly unlocked mission (1: not at all; already zoomed in further: stays at that zoom).")]
    public float unlockZoom = 2f;
    [Tooltip("Seconds the zoom in, and the zoom back out, each take (and a jump to another campaign).")]
    public float zoomSeconds = 0.8f;
    [Tooltip("The zoom's progress over its length (both 0 to 1).")]
    public AnimationCurve zoomEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Seconds the view rests on a new flag, or a newly opened campaign, once it has appeared.")]
    public float unlockHold = 0.7f;

    /// <summary>Every campaign on the hub, in tab order.</summary>
    readonly List<CampaignMap> maps = new List<CampaignMap>();
    readonly Dictionary<CampaignMap, Button> tabOf = new Dictionary<CampaignMap, Button>();
    /// <summary>Every mission flag of every campaign, shown or not.</summary>
    readonly List<MissionFlag> made = new List<MissionFlag>();
    readonly Dictionary<Mission, MissionInfo> infos = new Dictionary<Mission, MissionInfo>();
    /// <summary>Missions whose intro lines have been heard since the game was started (they are not said again on every click).</summary>
    static readonly HashSet<string> heard = new HashSet<string>();
    Mission picked;
    bool busy;
    bool ready;
    /// <summary>What pans and zooms: holds every campaign's map, in the window the maps are seen through.</summary>
    RectTransform world;
    Vector2 dragFrom;
    bool dragging;
    Canvas canvas;
    CampaignMap current;

    /// <summary>The view's zoom: 1 is one map filling the window.</summary>
    float ZoomNow { get { return world != null ? world.localScale.x : 1f; } }

    void Awake() {
        CampaignMap.HubOpen = true;
        TransportPanel.EnsureEventSystem();
        DialogueBox.Ensure();
    }

    IEnumerator Start() {
        GameController.Paused = false;
        ScenarioDialogue.Clear();   // nothing left over from a game
        canvas = GetComponentInParent<Canvas>();
        if (missionPanel != null) missionPanel.SetActive(false);
        Busy(true);
        if (map == null) { Debug.LogWarning("The campaign screen has no Map: nothing to show."); yield break; }

        // The world every map stands in: where this scene's map was, filling the window.
        RectTransform view = map.parent as RectTransform;
        world = new GameObject("World", typeof(RectTransform)).GetComponent<RectTransform>();
        world.SetParent(view, false);
        world.anchorMin = Vector2.zero; world.anchorMax = Vector2.one;
        world.offsetMin = world.offsetMax = Vector2.zero;
        world.pivot = new Vector2(0.5f, 0.5f);
        world.SetSiblingIndex(map.GetSiblingIndex());

        // This scene's own campaign.
        CampaignMap own = map.GetComponent<CampaignMap>();
        if (own == null) { own = map.gameObject.AddComponent<CampaignMap>(); own.flags = flags; own.availableAtStart = true; }
        if (own.flags == null) own.flags = flags;
        string here = gameObject.scene.name;
        Dictionary<CampaignMap, Vector2> place = new Dictionary<CampaignMap, Vector2>();
        bool ownListed = campaigns.Exists(c => c != null && c.scene == here);
        if (!ownListed) { maps.Add(own); place[own] = Vector2.zero; }

        // The other campaigns: each scene loaded beside this one, its map(s) moved into the world, the rest of it let go.
        foreach (CampaignSlot slot in campaigns) {
            if (slot == null || string.IsNullOrEmpty(slot.scene)) continue;
            if (slot.scene == here) { if (!maps.Contains(own)) { maps.Add(own); place[own] = slot.at; } continue; }
            if (maps.Exists(m => m.Id == slot.scene)) continue;
            if (!Application.CanStreamedLevelBeLoaded(slot.scene)) { Debug.LogWarning("Campaign scene \"" + slot.scene + "\" is not in the build: add it in File > Build Profiles (HexTactica > Campaign > Check Missions lists these)."); continue; }
            AsyncOperation load = SceneManager.LoadSceneAsync(slot.scene, LoadSceneMode.Additive);
            while (load != null && !load.isDone) yield return null;
            Scene s = SceneManager.GetSceneByName(slot.scene);
            if (!s.IsValid()) continue;
            int found = 0;
            foreach (GameObject root in s.GetRootGameObjects())
                foreach (CampaignMap m in root.GetComponentsInChildren<CampaignMap>(true)) {
                    string id = m.Id;   // (read before the move: it is its scene's name)
                    m.transform.SetParent(world, false);
                    if (found++ == 0) { maps.Add(m); place[m] = slot.at; }
                    else Destroy(m.gameObject);   // one campaign per scene
                    if (id != slot.scene) Debug.LogWarning("Campaign " + id + " was loaded as " + slot.scene + ".");
                }
            if (found == 0) Debug.LogWarning("Campaign scene \"" + slot.scene + "\" has no CampaignMap: give its Map object one.");
            SceneManager.UnloadSceneAsync(s);
        }
        map.SetParent(world, false);

        // Lay the maps out, each at its own size (a map stretched over its window takes the window's), where its entry
        // puts it: a step is the hub's own map's size plus the gap.
        Vector2 step = map.rect.size;
        foreach (CampaignMap m in maps) {
            RectTransform rt = (RectTransform)m.transform;
            Vector2 mapSize = rt.rect.size;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = mapSize;
            rt.localScale = Vector3.one;
            Vector2 at = place[m];
            rt.anchoredPosition = new Vector2(at.x * step.x * (1f + mapGap), at.y * step.y * (1f + mapGap));
        }

        // The missions, and what the last one played changed.
        CampaignProgress.Outcome result = CampaignProgress.TakePending();
        Mission played = null;
        foreach (CampaignMap cm in maps) foreach (Mission m in cm.Missions) {
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

        // The campaigns open (one just opened appears when the result is played out), and their tabs.
        bool anyOpen = false;
        foreach (CampaignMap cm in maps) {
            bool appearing = result != null && result.unlockedCampaigns.Contains(cm.Id);
            cm.gameObject.SetActive(cm.Open && !appearing);
            anyOpen |= cm.Open;
        }
        if (!anyOpen && maps.Count > 0) { maps[0].gameObject.SetActive(true); Debug.LogWarning("No campaign is open: showing " + maps[0].Id + " (tick Available At Start on its CampaignMap)."); }
        MakeTabs();

        // Look at the campaign the last mission was played in, else the one last looked at, else the first open.
        CampaignMap first = played != null ? played.GetComponentInParent<CampaignMap>(true) : null;
        if (first == null || !first.gameObject.activeSelf) first = maps.Find(m => m.gameObject.activeSelf && m.Id == MatchSetup.CampaignScene);
        if (first == null) first = maps.Find(m => m.gameObject.activeSelf);
        if (first != null) { world.anchoredPosition = CentreOf(first, 1f); world.localScale = Vector3.one; }
        FitFlags();
        Watch();
        ready = true;
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

    void OnDestroy() { CampaignMap.HubOpen = false; }

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

    // ------------------------------------------------------------------ the campaigns: tabs, and which is in view

    void MakeTabs() {
        Button template = tabTemplate;
        if (template == null) {
            // The screen's own button style: its first button that is not a flag's or the mission panel's.
            foreach (Button b in GetComponentsInChildren<Button>(true)) {
                if (b.GetComponentInParent<MissionFlag>(true) != null) continue;
                if (missionPanel != null && b.transform.IsChildOf(missionPanel.transform)) continue;
                template = b;
                break;
            }
        }
        if (template == null) return;
        if (tabs == null) {
            tabs = new GameObject("Campaign Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            tabs.SetParent(canvas != null ? canvas.transform : transform, false);
            tabs.anchorMin = tabs.anchorMax = new Vector2(0.5f, 1f);
            tabs.pivot = new Vector2(0.5f, 1f);
            tabs.anchoredPosition = new Vector2(0f, -30f);
            tabs.sizeDelta = new Vector2(1200f, ((RectTransform)template.transform).rect.height);
            HorizontalLayoutGroup row = tabs.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 12f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = false;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            if (missionPanel != null) tabs.SetSiblingIndex(missionPanel.transform.GetSiblingIndex());   // under the mission panel
        }
        foreach (CampaignMap cm in maps) {
            Button tab = Instantiate(template, tabs);
            tab.name = "Tab " + cm.Id;
            tab.onClick = new Button.ButtonClickedEvent();   // (the copy's own click is let go)
            CampaignMap target = cm;
            tab.onClick.AddListener(() => GoTo(target));
            TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = cm.Title;
            tab.gameObject.SetActive(cm.gameObject.activeSelf);
            tabOf[cm] = tab;
        }
        RefreshTabs();
    }

    void RefreshTabs() {
        int open = 0;
        foreach (CampaignMap cm in maps) if (cm.gameObject.activeSelf) open++;
        if (tabs != null) tabs.gameObject.SetActive(open > 1);
        foreach (KeyValuePair<CampaignMap, Button> t in tabOf) {
            t.Value.gameObject.SetActive(t.Key.gameObject.activeSelf);
            t.Value.interactable = t.Key != current;   // the campaign in view: its tab stands out by not being clickable
        }
        if (campaignTitle != null) campaignTitle.text = current != null ? current.Title : "";
    }

    /// <summary>The campaign whose map is nearest the middle of the view.</summary>
    void Watch() {
        if (world == null) return;
        Vector2 middle = -world.anchoredPosition / Mathf.Max(0.0001f, ZoomNow);   // the view's middle, in world units
        CampaignMap best = null;
        float bestD = float.MaxValue;
        foreach (CampaignMap cm in maps) {
            if (!cm.gameObject.activeSelf) continue;
            float d = (((RectTransform)cm.transform).anchoredPosition - middle).sqrMagnitude;
            if (d < bestD) { bestD = d; best = cm; }
        }
        if (best == current) return;
        current = best;
        if (current != null) MatchSetup.CampaignScene = current.Id;   // the main menu's Campaign button comes back here
        RefreshTabs();
    }

    /// <summary>A campaign's tab: the view glides to its whole map.</summary>
    public void GoTo(CampaignMap cm) {
        if (busy || cm == null || !cm.gameObject.activeSelf) return;
        StartCoroutine(GoToRoutine(cm));
    }

    IEnumerator GoToRoutine(CampaignMap cm) {
        Busy(true);
        yield return Glide(Vector3.one, CentreOf(cm, 1f));
        Busy(false);
    }

    /// <summary>The world's position that puts the middle of a campaign's map in the middle of the view, at this zoom.</summary>
    Vector2 CentreOf(CampaignMap cm, float zoom) {
        return MapView.CentreOn(Vector2.zero, ((RectTransform)cm.transform).anchoredPosition, zoom);
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
        Vector3 viewScale = world.localScale;
        Vector2 viewAt = world.anchoredPosition;
        bool moved = false;
        foreach (MissionFlag f in made) {
            if (f == null || f.gameObject.activeSelf || !o.unlocked.Contains(Info(f.Mission).key)) continue;
            CampaignMap on = f.GetComponentInParent<CampaignMap>(true);
            if (on != null && !on.gameObject.activeSelf) continue;   // in a campaign not open yet: it is there when the campaign is
            moved = true;
            yield return Zoom(world.InverseTransformPoint(f.transform.position), Mathf.Max(unlockZoom, ZoomNow));
            Show(f.Mission, CampaignProgress.Get(Info(f.Mission)).stars);
            if (f.gameObject.activeSelf) yield return f.Appear();
            if (unlockHold > 0f) yield return new WaitForSecondsRealtime(unlockHold);
        }
        // Campaigns this win opened: each appears, its tab with it, and the view is taken there.
        foreach (CampaignMap cm in maps) {
            if (cm.gameObject.activeSelf || !o.unlockedCampaigns.Contains(cm.Id)) continue;
            moved = true;
            cm.gameObject.SetActive(true);
            RefreshTabs();
            yield return Glide(Vector3.one, CentreOf(cm, 1f));
            if (unlockHold > 0f) yield return new WaitForSecondsRealtime(unlockHold * 2f);
        }
        if (moved) yield return Glide(viewScale, viewAt);   // back to where the player was looking
        List<DialogueLine> lines = o.tally.won ? played.outro : played.outroLost;
        if (lines != null && lines.Count > 0) yield return Say(lines);
        Busy(false);
    }

    /// <summary>The view glides so that <paramref name="local"/> (a point in the world, in its own units from its middle) is in the middle (as near as the maps' edges allow), at <paramref name="zoom"/> times a map's own size.</summary>
    IEnumerator Zoom(Vector2 local, float zoom) {
        zoom = Mathf.Clamp(zoom, 1f, Mathf.Max(1f, maxZoom));
        yield return Glide(Vector3.one * zoom, MapView.CentreOn(Vector2.zero, local, zoom));
    }

    /// <summary>The view glides to this scale and position of the world (kept over the open maps).</summary>
    IEnumerator Glide(Vector3 toScale, Vector2 toAt) {
        if (world == null) yield break;
        toAt = Clamped(toAt, toScale.x);
        Vector3 fromScale = world.localScale;
        Vector2 fromAt = world.anchoredPosition;
        for (float t = 0f; t < zoomSeconds; t += Time.unscaledDeltaTime) {
            float k = zoomEase != null ? zoomEase.Evaluate(t / zoomSeconds) : t / zoomSeconds;
            world.localScale = fromScale + (toScale - fromScale) * k;
            world.anchoredPosition = fromAt + (toAt - fromAt) * k;
            FitFlags();
            yield return null;
        }
        world.localScale = toScale;
        world.anchoredPosition = toAt;
        FitFlags();
        Watch();
    }

    // ------------------------------------------------------------------ pan and zoom by the player

    Vector2 ViewSize { get { RectTransform v = world != null ? world.parent as RectTransform : null; return v != null ? v.rect.size : Vector2.zero; } }

    /// <summary>The world's position kept so the view stays over the open campaigns' maps.</summary>
    Vector2 Clamped(Vector2 at, float scale) {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (CampaignMap cm in maps) {
            if (!cm.gameObject.activeSelf) continue;
            RectTransform rt = (RectTransform)cm.transform;
            Vector2 half = rt.rect.size * 0.5f, centre = rt.anchoredPosition;
            min = Vector2.Min(min, centre - half);
            max = Vector2.Max(max, centre + half);
        }
        if (min.x > max.x) return at;
        return MapView.ClampBounds(at, min, max, scale, ViewSize);
    }

    Camera UICamera { get { return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null; } }

    /// <summary>The cursor is over the picked mission's panel or the tabs (the wheel and drags there are theirs).</summary>
    bool OverPanel(Vector2 screen) {
        RectTransform panel = missionPanel != null && missionPanel.activeInHierarchy ? missionPanel.transform as RectTransform : null;
        if (panel != null && RectTransformUtility.RectangleContainsScreenPoint(panel, screen, UICamera)) return true;
        return tabs != null && tabs.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(tabs, screen, UICamera);
    }

    /// <summary>The cursor in the window's own units (the world's parent), or false if it can't be told.</summary>
    bool InView(Vector2 screen, out Vector2 local) {
        local = Vector2.zero;
        RectTransform view = world != null ? world.parent as RectTransform : null;
        return view != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(view, screen, UICamera, out local);
    }

    void LateUpdate() {
        if (!ready || world == null || busy || !Application.isFocused) { dragging = false; return; }
        Vector2 mouse = Input.mousePosition;
        bool inWindow = mouse.x >= 0f && mouse.y >= 0f && mouse.x <= Screen.width && mouse.y <= Screen.height;
        bool talking = ScenarioDialogue.Showing;
        bool changed = false;

        // Home: the whole of the campaign in view.
        if (Input.GetKeyDown(KeyCode.Home) && current != null) { world.localScale = Vector3.one; world.anchoredPosition = Clamped(CentreOf(current, 1f), 1f); changed = true; }

        // Arrows pan.
        Vector2 move = Vector2.zero;
        if (Input.GetKey(KeyCode.LeftArrow)) move.x -= 1f;
        if (Input.GetKey(KeyCode.RightArrow)) move.x += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) move.y -= 1f;
        if (Input.GetKey(KeyCode.UpArrow)) move.y += 1f;
        if (move.x != 0f || move.y != 0f) { world.anchoredPosition = Clamped(world.anchoredPosition - move * ViewSize.y * panSpeed * Time.unscaledDeltaTime, ZoomNow); changed = true; }

        // Right or middle drag pans: the point grabbed stays under the cursor.
        Vector2 here;
        bool button = Input.GetMouseButton(1) || Input.GetMouseButton(2);
        if ((Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) && inWindow && !OverPanel(mouse) && InView(mouse, out here)) { dragging = true; dragFrom = here; }
        if (!button) dragging = false;
        if (dragging && InView(mouse, out here)) {
            world.anchoredPosition = Clamped(world.anchoredPosition + (here - dragFrom), ZoomNow);
            dragFrom = here;
            changed = true;
        }

        // The wheel (or + / -) zooms about the cursor.
        float wheel = inWindow && !OverPanel(mouse) && !talking ? Input.mouseScrollDelta.y : 0f;
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) wheel += 1f;
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) wheel -= 1f;
        if (wheel != 0f && maxZoom > 1f) {
            float from = world.localScale.x;
            float to = Mathf.Clamp(from * (1f + zoomStep * wheel), 1f, maxZoom);
            Vector2 point;
            if (to != from && RectTransformUtility.ScreenPointToLocalPointInRectangle(world, inWindow ? mouse : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), UICamera, out point)) {
                Vector2 at = MapView.ZoomAbout(world.anchoredPosition, point, from, to);
                world.localScale = Vector3.one * to;
                world.anchoredPosition = Clamped(at, to);
                FitFlags();
                changed = true;
            }
        }
        if (changed) Watch();
    }

    /// <summary>The flags back to their own size on screen, whatever the view's zoom (if Flags Keep Size).</summary>
    void FitFlags() {
        float size = flagsKeepSize ? 1f / ZoomNow : 1f;
        foreach (MissionFlag f in made) if (f != null) f.SetSize(size);
    }
}
