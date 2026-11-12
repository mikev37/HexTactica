using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The campaign screen (the Campaign scene): the campaign's map with a flag for every mission the player can see
/// (a copy of the Mission Flag prefab each, placed by Mission.mapPosition). Picking a flag plays the mission's intro
/// lines in the dialogue box, then shows its briefing with Start. Coming back from a mission, its result is played
/// out: the stars it gained on its flag, each newly unlocked mission's flag appearing with the view zoomed onto it,
/// then the mission's closing lines.
/// Everything on screen is the scene's and the flag prefab's; this component finds its parts through its fields.
/// </summary>
public class CampaignScreen : MonoBehaviour {

    [Header("The map")]
    [Tooltip("The campaign's picture: the flags stand on it, and it is what zooms and pans. Its parent is the window it is seen through.")]
    public RectTransform map;
    [Tooltip("Where the flags go: a child of the map, stretched over it.")]
    public RectTransform flags;
    [Tooltip("The flag copied for every mission the player can see (the Mission Flag prefab).")]
    public MissionFlag flagPrefab;
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

    [Header("Playing out a result")]
    [Tooltip("Seconds before the result starts to play, once the screen is up.")]
    public float resultDelay = 0.6f;
    [Tooltip("Seconds between one star gained and the next.")]
    public float starInterval = 0.25f;
    [Tooltip("How far the view zooms onto a newly unlocked mission (1: not at all).")]
    public float unlockZoom = 2f;
    [Tooltip("Seconds the zoom in, and the zoom back out, each take.")]
    public float zoomSeconds = 0.8f;
    [Tooltip("The zoom's progress over its length (both 0 to 1).")]
    public AnimationCurve zoomEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Seconds the view rests on a new flag once it has appeared.")]
    public float unlockHold = 0.7f;

    readonly List<MissionFlag> made = new List<MissionFlag>();
    /// <summary>Missions whose intro lines have been heard since the game was started (they are not said again on every click).</summary>
    static readonly HashSet<string> heard = new HashSet<string>();
    Mission picked;
    bool busy;
    Vector3 mapScale;
    Vector2 mapAt;

    void Awake() {
        TransportPanel.EnsureEventSystem();
        DialogueBox.Ensure();
    }

    void Start() {
        GameController.Paused = false;
        ScenarioDialogue.Clear();   // nothing left over from a game
        if (map != null) { mapScale = map.localScale; mapAt = map.anchoredPosition; }
        if (missionPanel != null) missionPanel.SetActive(false);
        if (flagPrefab != null && flagPrefab.gameObject.scene.IsValid()) flagPrefab.gameObject.SetActive(false);   // a template in the scene; a prefab is left alone
        CampaignProgress.Outcome result = CampaignProgress.TakePending();
        foreach (Mission m in Mission.All) {
            if (!CampaignProgress.Visible(m)) continue;
            if (result != null && result.unlocked.Contains(m)) continue;   // appears when the result is played out
            // The stars the last mission just gained are held back: they are gained on screen.
            Place(m, result != null && result.mission == m ? CampaignProgress.Get(m).stars & ~result.gained : CampaignProgress.Get(m).stars);
        }
        Busy(false);
        if (result != null && result.mission != null) StartCoroutine(PlayResult(result));
    }

    MissionFlag Place(Mission m, MissionStars shown) {
        if (flagPrefab == null || flags == null) return null;
        MissionFlag f = Instantiate(flagPrefab, flags);
        f.gameObject.SetActive(true);
        RectTransform rt = (RectTransform)f.transform;
        rt.anchorMin = rt.anchorMax = m.mapPosition;
        rt.anchoredPosition = Vector2.zero;
        f.Show(m, m.faction != null ? m.faction.flagColor : noFactionColor, CampaignProgress.Get(m).completed, CampaignProgress.Blocked(m), shown);
        Mission mission = m;
        if (f.button != null) f.button.onClick.AddListener(() => Pick(mission));
        made.Add(f);
        return f;
    }

    MissionFlag FlagOf(Mission m) { return made.Find(f => f != null && f.Mission == m); }

    void Busy(bool on) {
        busy = on;
        if (busyBlocker != null) busyBlocker.SetActive(on);
    }

    // ------------------------------------------------------------------ picking a mission

    /// <summary>A flag was clicked: the mission's intro lines (the first time), then its briefing.</summary>
    public void Pick(Mission m) {
        if (busy || m == null || !CampaignProgress.Playable(m)) return;
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
        CampaignProgress.Entry e = CampaignProgress.Get(m);
        bool file = GameFiles.CampaignScenarioPath(m.ScenarioName) != null;
        if (missionTitle != null) missionTitle.text = m.Title;
        if (missionFaction != null) {
            missionFaction.text = m.faction != null ? (string.IsNullOrEmpty(m.faction.campaignName) ? m.faction.name : m.faction.campaignName) : "";
            missionFaction.color = m.faction != null ? m.faction.flagColor : noFactionColor;
        }
        if (missionBriefing != null) missionBriefing.text = m.briefing;
        if (missionPars != null) {
            string pars = string.Format(m.parTurns > 0 ? parsFormat : parsFormatNoSpeed, m.parTurns, m.parKills, m.parLosses);
            if (e.completed) pars += string.Format(completedFormat, MissionRating.Count(e.stars), e.bestTurns);
            if (!file) pars += string.Format(missingScenario, m.ScenarioName);
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
        MatchSetup.PlayMission(picked);
    }

    /// <summary>The briefing's Close.</summary>
    public void CloseMission() {
        picked = null;
        foreach (MissionFlag f in made) if (f != null) f.Picked(false);
        if (missionPanel != null) missionPanel.SetActive(false);
    }

    public void Back() { MatchSetup.ToMainMenu(); }

    // ------------------------------------------------------------------ a mission's result, played out

    IEnumerator PlayResult(CampaignProgress.Outcome o) {
        Busy(true);
        if (resultDelay > 0f) yield return new WaitForSecondsRealtime(resultDelay);
        MissionFlag flag = FlagOf(o.mission);
        if (flag != null) {
            // Its first win: the complete mark was shown with the flag; the stars come one by one.
            yield return flag.GainStars(o.gained, starInterval);
        }
        foreach (Mission m in o.unlocked) {
            yield return Zoom(m.mapPosition, unlockZoom);
            MissionFlag f = Place(m, CampaignProgress.Get(m).stars);
            if (f != null) yield return f.Appear();
            if (unlockHold > 0f) yield return new WaitForSecondsRealtime(unlockHold);
        }
        if (o.unlocked.Count > 0) yield return Zoom(new Vector2(0.5f, 0.5f), 1f);
        // Missions this attempt closed off show it now (their flags were made as they stand).
        List<DialogueLine> lines = o.tally.won ? o.mission.outro : o.mission.outroLost;
        if (lines != null && lines.Count > 0) yield return Say(lines);
        Busy(false);
    }

    /// <summary>The view glides so that <paramref name="point"/> of the map (0,0 bottom left, 1,1 top right) is in the middle, at <paramref name="zoom"/> times the map's own size.</summary>
    IEnumerator Zoom(Vector2 point, float zoom) {
        if (map == null) yield break;
        Vector3 fromScale = map.localScale, toScale = mapScale * zoom;
        Vector2 fromAt = map.anchoredPosition;
        // The point's offset from the map's pivot, in the map's own units, scaled as it will be shown.
        Vector2 size = map.rect.size, offset = new Vector2((point.x - map.pivot.x) * size.x, (point.y - map.pivot.y) * size.y);
        Vector2 toAt = zoom == 1f ? mapAt : mapAt - new Vector2(offset.x * toScale.x, offset.y * toScale.y);
        for (float t = 0f; t < zoomSeconds; t += Time.unscaledDeltaTime) {
            float k = zoomEase != null ? zoomEase.Evaluate(t / zoomSeconds) : t / zoomSeconds;
            map.localScale = fromScale + (toScale - fromScale) * k;
            map.anchoredPosition = fromAt + (toAt - fromAt) * k;
            yield return null;
        }
        map.localScale = toScale;
        map.anchoredPosition = toAt;
    }
}
