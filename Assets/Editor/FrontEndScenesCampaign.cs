using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The campaign's part of the front end's first layout (see FrontEndScenes): the Campaign scene (the map and its
/// mission flags), the MissionBrief scene (a mission's pre-game screen) and the MissionResult scene (its post-game
/// screen), and the Mission Flag prefab. Made once, when missing; after that they are yours to edit in Unity.
/// The placeholder art (star, star_empty, flag, check, cross in Sprites/UI/Widgets) is white, tinted where it is used.
/// </summary>
static partial class FrontEndScenes {
    const string MissionFlagPrefab = "Mission Flag";
    static readonly Color StarColor = new Color(1f, 0.82f, 0.25f, 1f);
    static readonly Color StarEmptyColor = new Color(1f, 1f, 1f, 0.45f);
    static readonly Color MapPlaceholder = new Color(0.16f, 0.22f, 0.3f, 1f);

    static void MakeCampaignScenes(FrontEndSettings s, bool fromMenu, List<string> made) {
        Make(s.campaignScene, BuildCampaign, fromMenu, made);
        Make(s.missionBriefScene, BuildMissionBrief, fromMenu, made);
        Make(s.missionResultScene, BuildMissionResult, fromMenu, made);
    }

    [MenuItem("HexTactica/Campaign/Open Campaign Scene")]
    static void OpenCampaign() {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(SceneFolder + Settings().campaignScene + ".unity", OpenSceneMode.Single);
    }

    /// <summary>The player's campaign progress (campaign.dat in the player's data folder) made new. Asks first.</summary>
    [MenuItem("HexTactica/Campaign/Reset Campaign Progress")]
    static void ResetProgress() {
        if (!EditorUtility.DisplayDialog("Reset Campaign Progress",
                "Forget every mission played on this computer: missions won, stars, what was unlocked and closed off. The campaign starts as new.", "Reset", "Cancel")) return;
        CampaignProgress.Reset();
        Debug.Log("Campaign progress reset.");
    }

    /// <summary>
    /// A new campaign: a copy of a campaign scene (the open one if it is a campaign, else the default Campaign scene)
    /// with its missions taken off the map, saved where you choose and added to the build. Nothing else changes.
    /// </summary>
    [MenuItem("HexTactica/Campaign/New Campaign Scene...")]
    static void NewCampaignScene() {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string from = Object.FindAnyObjectByType<CampaignScreen>() != null ? UnityEngine.SceneManagement.SceneManager.GetActiveScene().path : SceneFolder + Settings().campaignScene + ".unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(from) == null) { Debug.LogWarning("No campaign scene to copy (" + from + "): run HexTactica > Front End > Create Missing Scenes first."); return; }
        string to = EditorUtility.SaveFilePanelInProject("New Campaign Scene", "New Campaign", "unity", "Where to save the new campaign scene", SceneFolder.TrimEnd('/'));
        if (string.IsNullOrEmpty(to)) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(to) != null) { Debug.LogWarning(to + " already exists: nothing was changed."); return; }
        if (!AssetDatabase.CopyAsset(from, to)) { Debug.LogWarning("Could not copy " + from + " to " + to + "."); return; }
        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(to, OpenSceneMode.Single);
        int removed = 0;
        foreach (CampaignScreen screen in Object.FindObjectsByType<CampaignScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (screen.flags != null) foreach (Mission m in screen.flags.GetComponentsInChildren<Mission>(true)) { Object.DestroyImmediate(m.gameObject); removed++; }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        List<EditorBuildSettingsScene> build = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!build.Exists(b => b.path == to)) { build.Add(new EditorBuildSettingsScene(to, true)); EditorBuildSettings.scenes = build.ToArray(); }
        Debug.Log("New campaign scene " + to + " (copied from " + from + ", " + removed + " mission(s) taken off its map; added to the build). Drag the Mission Flag prefab under its Flags object to add missions.");
    }

    /// <summary>Read-only: says what is wrong with the open campaign scene's missions (a scenario file that is not there, a mission that can never appear, two with one name).</summary>
    [MenuItem("HexTactica/Campaign/Check Missions")]
    static void CheckMissions() {
        CampaignScreen screen = Object.FindAnyObjectByType<CampaignScreen>(FindObjectsInactive.Include);
        if (screen == null || screen.flags == null) { Debug.LogWarning("Open a campaign scene (one with a CampaignScreen and its Flags) to check its missions."); return; }
        List<Mission> all = new List<Mission>(screen.flags.GetComponentsInChildren<Mission>(true));
        int problems = 0;
        if (all.Count == 0) { Debug.LogWarning("No missions under " + screen.flags.name + ": drag the Mission Flag prefab there to add one."); return; }
        if (!all.Exists(m => m.availableAtStart)) { Debug.LogWarning("No mission is Available At Start: the campaign map starts empty."); problems++; }
        HashSet<string> names = new HashSet<string>();
        foreach (Mission m in all) {
            if (!names.Add(m.name)) { Debug.LogWarning("Two missions are named \"" + m.name + "\": progress is kept by name, so they would share it. Rename one.", m); problems++; }
            if (m.GetComponent<MissionFlag>() == null) { Debug.LogWarning(m.name + ": no MissionFlag, so it can't be shown.", m); problems++; }
            MissionInfo info = m.Info;
            if (GameFiles.CampaignScenarioPath(info.ScenarioName) == null) {
                Debug.LogWarning(m.name + ": no scenario file \"" + info.ScenarioName + "\" in the campaign folders (" + string.Join(", ", Settings().campaignFolders.ToArray()) + ").", m);
                problems++;
            }
            if (!m.availableAtStart && !all.Exists(x => x.unlocks != null && x.unlocks.Contains(m))) {
                Debug.LogWarning(m.name + ": not Available At Start and no mission unlocks it, so its flag never appears.", m);
                problems++;
            }
            foreach (Mission x in m.unlocks) if (x != null && x.transform.IsChildOf(screen.flags) == false) { Debug.LogWarning(m.name + ": unlocks " + x.name + ", which is not one of this campaign's missions.", m); problems++; }
            if (m.faction == null) Debug.Log(m.name + ": no faction (its flag takes the campaign screen's No Faction colour).", m);
        }
        Debug.Log(problems == 0 ? "Missions checked: " + all.Count + ", nothing wrong." : "Missions checked: " + all.Count + ", " + problems + " problem(s) above.");
    }

    // ================================================================== the scenes

    /// <summary>The campaign screen: the map (a placeholder until you give its Image your picture) with the flags on it, the picked mission's briefing down the right.</summary>
    static void BuildCampaign() {
        Camera cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        cam.tag = "MainCamera";
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Background;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        Canvas canvas = MakeCanvas("Campaign", 0);
        Transform root = canvas.transform;
        CampaignScreen m = canvas.gameObject.AddComponent<CampaignScreen>();

        // The map: the window it is seen through, the picture (zoomed and panned), the flags on it.
        GameObject view = Go("View", root);
        Fill((RectTransform)view.transform);
        GameObject map = Go("Map", view.transform, typeof(Image));
        Fill((RectTransform)map.transform);
        Image picture = map.GetComponent<Image>();
        picture.color = MapPlaceholder;
        picture.raycastTarget = false;
        Text(map.transform, "Placeholder Note", "Campaign map: give the Map object's Image your picture, then delete this note.", 26, TextAlignmentOptions.Center, FontStyles.Italic)
            .rectTransform.Let(rt => Place(rt, new Vector2(0.5f, 0.08f), Vector2.zero, new Vector2(1400f, 60f)));
        GameObject flags = Go("Flags", map.transform);
        Fill((RectTransform)flags.transform);
        m.map = (RectTransform)map.transform;
        m.flags = (RectTransform)flags.transform;
        RowPrefab(MissionFlagPrefab, MissionFlagRow);   // (made for you to drag onto Flags: one per mission)

        Text(root, "Title", "Campaign", 56, TextAlignmentOptions.Left, FontStyles.Bold).rectTransform.Let(rt => {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(40f, -30f); rt.sizeDelta = new Vector2(700f, 80f);
        });
        Button back = Btn(root, "Main menu", m.Back, 260f);
        ((RectTransform)back.transform).Let(rt => {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f); rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(40f, 40f); rt.sizeDelta = new Vector2(260f, 60f);
        });

        // The picked mission.
        GameObject panel = SidePanel(root, "Mission", 640f, false);
        m.missionTitle = Text(panel.transform, "Title", "Mission", 40, TextAlignmentOptions.Left, FontStyles.Bold);
        m.missionFaction = Text(panel.transform, "Faction", "", 24, TextAlignmentOptions.Left, FontStyles.Bold);
        GameObject stars = Row(panel.transform, "Stars", 56f);
        m.missionStars = new[] { Star(stars.transform, "Speed", 52f), Star(stars.transform, "Aggression", 52f), Star(stars.transform, "Defense", 52f) };
        Spacer(stars.transform);
        m.missionBriefing = Text(panel.transform, "Briefing", "", 24, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Text(panel.transform, "Rating heading", "Rating", 28, TextAlignmentOptions.Left, FontStyles.Bold);
        m.missionPars = Text(panel.transform, "Pars", "", 22, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Spacer(panel.transform, true);
        GameObject buttons = Row(panel.transform, "Buttons", 64f);
        Btn(buttons.transform, "Close", m.CloseMission, 200f);
        Spacer(buttons.transform);
        m.startButton = Btn(buttons.transform, "Start", m.StartMission, 240f);
        m.missionPanel = panel;
        panel.SetActive(false);

        // Over everything while a result plays out: the clicks stop here.
        GameObject busy = Go("Busy", root, typeof(Image));
        busy.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        Fill((RectTransform)busy.transform);
        m.busyBlocker = busy;
        busy.SetActive(false);
    }

    /// <summary>A mission's pre-game screen: a panel down the left (the board stays in view), nothing to choose.</summary>
    static void BuildMissionBrief() {
        Canvas canvas = MakeCanvas("Mission Brief", 100);
        MissionBriefing m = canvas.gameObject.AddComponent<MissionBriefing>();
        GameObject panel = SidePanel(canvas.transform, "Briefing", 760f, true);
        GameObject head = Row(panel.transform, "Head", 72f);
        Image flag = Go("Flag", head.transform, typeof(Image)).GetComponent<Image>();
        flag.sprite = Widget("flag"); flag.preserveAspect = true; flag.raycastTarget = false;
        Size(flag.gameObject, 64f, 64f);
        m.flag = flag;
        m.title = Text(head.transform, "Title", "Mission", 40, TextAlignmentOptions.Left, FontStyles.Bold);
        Flexible(m.title.gameObject);
        m.faction = Text(panel.transform, "Faction", "", 24, TextAlignmentOptions.Left, FontStyles.Bold);
        m.briefing = Text(panel.transform, "Briefing", "", 24, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Text(panel.transform, "Objectives heading", "Objectives", 28, TextAlignmentOptions.Left, FontStyles.Bold);
        m.objectives = Text(panel.transform, "Objectives", "", 24, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Text(panel.transform, "Rating heading", "Rating", 28, TextAlignmentOptions.Left, FontStyles.Bold);
        m.pars = Text(panel.transform, "Pars", "", 22, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Spacer(panel.transform, true);
        GameObject buttons = Row(panel.transform, "Buttons", 64f);
        Btn(buttons.transform, "Back", m.Back, 200f);
        Spacer(buttons.transform);
        m.startButton = Btn(buttons.transform, "Start", m.StartMission, 240f);
    }

    /// <summary>A mission's post-game screen: won or lost, the result against each par with its star, Return to campaign.</summary>
    static void BuildMissionResult() {
        Canvas canvas = MakeCanvas("Mission Result", 110);
        MissionResultScreen m = canvas.gameObject.AddComponent<MissionResultScreen>();
        Dim(canvas.transform);
        GameObject w = Window(canvas.transform, "Result", 1100f, 760f);
        m.title = Text(w.transform, "Title", "Mission", 30, TextAlignmentOptions.Left, FontStyles.Normal);
        m.headline = Text(w.transform, "Headline", "Victory", 56, TextAlignmentOptions.Left, FontStyles.Bold);
        m.said = Text(w.transform, "Said", "", 24, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Text(w.transform, "Rating heading", "Rating", 30, TextAlignmentOptions.Left, FontStyles.Bold);
        m.speed = ParRow(w.transform, "Speed");
        m.aggression = ParRow(w.transform, "Aggression");
        m.defense = ParRow(w.transform, "Defense");
        m.summary = Text(w.transform, "Summary", "", 24, TextAlignmentOptions.Left, FontStyles.Italic);
        Spacer(w.transform, true);
        GameObject buttons = Row(w.transform, "Buttons", 64f);
        Btn(buttons.transform, "View board", m.ViewBoard, 240f);
        Spacer(buttons.transform);
        m.returnButton = Btn(buttons.transform, "Return to campaign", m.ReturnToCampaign, 360f);
    }

    static MissionResultScreen.ParRow ParRow(Transform parent, string name) {
        GameObject row = Row(parent, name, 72f);
        StarPip star = Star(row.transform, "Star", 64f);
        TextMeshProUGUI text = Text(row.transform, "Result", name, 28, TextAlignmentOptions.Left, FontStyles.Normal);
        Flexible(text.gameObject);
        return new MissionResultScreen.ParRow { result = text, star = star, row = row };
    }

    // ================================================================== their parts

    /// <summary>A star: its empty outline, and the earned star over it (what pops when it is gained).</summary>
    static StarPip Star(Transform parent, string name, float size) {
        GameObject g = Go(name, parent);
        Size(g, size, size);
        ((RectTransform)g.transform).sizeDelta = new Vector2(size, size);
        StarPip pip = g.AddComponent<StarPip>();
        Image empty = Go("Empty", g.transform, typeof(Image)).GetComponent<Image>();
        empty.sprite = Widget("star_empty"); empty.color = StarEmptyColor; empty.raycastTarget = false;
        Fill(empty.rectTransform);
        Image earned = Go("Earned", g.transform, typeof(Image)).GetComponent<Image>();
        earned.sprite = Widget("star"); earned.color = StarColor; earned.raycastTarget = false;
        Fill(earned.rectTransform);
        pip.empty = empty.gameObject;
        pip.earned = earned.gameObject;
        pip.popTarget = earned.transform;
        earned.gameObject.SetActive(false);
        return pip;
    }

    /// <summary>The Mission Flag prefab: the flag (tinted by faction), the mission's name under it, its stars, and the complete, closed-off and picked marks.</summary>
    static MissionFlag MissionFlagRow(Transform parent) {
        GameObject g = Go(MissionFlagPrefab, parent, typeof(Image), typeof(Button));
        RectTransform rt = (RectTransform)g.transform;
        rt.sizeDelta = new Vector2(200f, 190f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        Image hit = g.GetComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);   // the whole flag takes the click
        g.AddComponent<Mission>();
        MissionFlag f = g.AddComponent<MissionFlag>();
        f.button = g.GetComponent<Button>();
        f.button.targetGraphic = hit;

        Image picked = Go("Picked", g.transform, typeof(Image)).GetComponent<Image>();
        picked.sprite = Widget("hex"); picked.color = new Color(Accent.r, Accent.g, Accent.b, 0.55f); picked.raycastTarget = false; picked.preserveAspect = true;
        Place(picked.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -55f), new Vector2(130f, 130f));
        f.pickedMark = picked.gameObject;
        picked.gameObject.SetActive(false);

        Image flag = Go("Flag", g.transform, typeof(Image)).GetComponent<Image>();
        flag.sprite = Widget("flag"); flag.raycastTarget = false; flag.preserveAspect = true;
        Place(flag.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -55f), new Vector2(96f, 96f));
        f.flag = flag;

        Image complete = Go("Complete", g.transform, typeof(Image)).GetComponent<Image>();
        complete.sprite = Widget("check"); complete.color = new Color(0.45f, 0.9f, 0.45f, 1f); complete.raycastTarget = false; complete.preserveAspect = true;
        Place(complete.rectTransform, new Vector2(0.5f, 1f), new Vector2(48f, -24f), new Vector2(44f, 44f));
        f.completeMark = complete.gameObject;
        complete.gameObject.SetActive(false);

        Image blocked = Go("Blocked", g.transform, typeof(Image)).GetComponent<Image>();
        blocked.sprite = Widget("cross"); blocked.color = new Color(0.9f, 0.3f, 0.25f, 1f); blocked.raycastTarget = false; blocked.preserveAspect = true;
        Place(blocked.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -55f), new Vector2(84f, 84f));
        f.blockedMark = blocked.gameObject;
        blocked.gameObject.SetActive(false);

        GameObject stars = Go("Stars", g.transform);
        Place((RectTransform)stars.transform, new Vector2(0.5f, 1f), new Vector2(0f, -124f), new Vector2(120f, 36f));
        HorizontalLayoutGroup h = HLayout(stars, 0, 6f);
        h.childAlignment = TextAnchor.MiddleCenter;
        f.stars = new[] { Star(stars.transform, "Speed", 36f), Star(stars.transform, "Aggression", 36f), Star(stars.transform, "Defense", 36f) };

        f.title = Text(g.transform, "Title", "Mission", 22, TextAlignmentOptions.Center, FontStyles.Bold);
        Place(f.title.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(260f, 40f));
        return f;
    }
}
