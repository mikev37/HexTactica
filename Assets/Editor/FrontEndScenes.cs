using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Makes the front end's scenes the first time the project opens without them — MainMenu, PreGame, PostGame,
/// InGameMenu, MapEditor in Assets/Scenes — with the FrontEndSettings asset, the unit explorer's render texture, and
/// the prefabs of the rows the screens list things in (Assets/FrontEnd/Prefabs: a file, a unit, an attribute, a side,
/// a palette entry, a map side), and lists the scenes in the build settings (MainMenu first, so a build starts there).
/// Each scene is a starting layout in the flat art of Sprites/UI/Widgets, wired to its component (MainMenu,
/// PreGameMenu, PostGameScreen, InGameMenu, MapEditorPanel): after that the scenes and prefabs are yours to edit in
/// Unity, and nothing here touches one that exists — except "Rebuild Front End Scenes (overwrites)", which asks first.
/// The game never builds any of this at run time.
/// </summary>
[InitializeOnLoad]
static class FrontEndScenes {
    const string SceneFolder = "Assets/Scenes/";
    const string SettingsPath = "Assets/Resources/" + FrontEndSettings.ResourceName + ".asset";
    const string PreviewTexturePath = "Assets/FrontEnd/UnitPreview.renderTexture";
    const string PrefabFolder = "Assets/FrontEnd/Prefabs/";
    static readonly string[] RowPrefabs = { "File Row", "Unit Row", "Attribute Row", "Side Row", "Palette Entry", "Map Side Row" };

    static FrontEndScenes() { EditorApplication.delayCall += () => CreateMissing(false); }

    [MenuItem("HexTactica/Front End/Create Missing Scenes")]
    static void CreateMissingMenu() { CreateMissing(true); }

    [MenuItem("HexTactica/Front End/Open Main Menu Scene")]
    static void OpenMainMenu() {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(SceneFolder + Settings().mainMenuScene + ".unity", OpenSceneMode.Single);
    }

    /// <summary>
    /// Destructive, and named so: the five front-end scenes and the row prefabs are deleted and made again from this
    /// file's layout (your edits to them are lost). Asks first. The settings asset is kept.
    /// </summary>
    [MenuItem("HexTactica/Front End/Rebuild Front End Scenes (overwrites)")]
    static void Rebuild() {
        FrontEndSettings s = Settings();
        string[] names = { s.mainMenuScene, s.preGameScene, s.postGameScene, s.inGameMenuScene, s.mapEditorScene };
        for (int i = 0; i < SceneManager.sceneCount; i++)
            foreach (string n in names)
                if (SceneManager.GetSceneAt(i).path == SceneFolder + n + ".unity") {
                    EditorUtility.DisplayDialog("Rebuild Front End Scenes", "Open another scene first: " + n + " is open.", "OK");
                    return;
                }
        if (!EditorUtility.DisplayDialog("Rebuild Front End Scenes",
                "Delete and make again: the scenes " + string.Join(", ", names) + " and the row prefabs in " + PrefabFolder + ". Any changes you made to them are lost.",
                "Rebuild", "Cancel")) return;
        foreach (string n in names) AssetDatabase.DeleteAsset(SceneFolder + n + ".unity");
        foreach (string r in RowPrefabs) AssetDatabase.DeleteAsset(PrefabFolder + r + ".prefab");
        CreateMissing(true);
    }

    /// <summary>
    /// Brings scenes made by an earlier version of this tool up to date, changing only what the update is about and
    /// keeping everything else (your art, sounds, layout). Safe to run more than once: a step already done is skipped.
    /// Today: the map editor's Look page (the sides, name, width and rules move onto it, off the palette's screen),
    /// the palette's scroll view named for hiding, and Neutral moved into the For list (the Neutral checkbox removed).
    /// </summary>
    [MenuItem("HexTactica/Front End/Update Scenes")]
    static void UpdateScenes() {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        FrontEndSettings s = Settings();
        string path = SceneFolder + s.mapEditorScene + ".unity";
        if (!File.Exists(path)) { Debug.Log("No " + path + ": nothing to update (Create Missing Scenes makes it new)."); return; }
        Scene was = SceneManager.GetActiveScene();
        bool open = false;
        for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).path == path) open = true;
        Scene scene = open ? SceneManager.GetSceneByPath(path) : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        List<string> done = new List<string>();
        try {
            MapEditorPanel m = null;
            foreach (GameObject root in scene.GetRootGameObjects()) { m = root.GetComponentInChildren<MapEditorPanel>(true); if (m != null) break; }
            if (m == null) { Debug.LogWarning(path + " has no MapEditorPanel: not updated."); return; }
            if (m.paletteView == null && m.palette != null) {
                ScrollRect sr = m.palette.GetComponentInParent<ScrollRect>(true);
                if (sr != null) { m.paletteView = sr.gameObject; done.Add("palette view linked"); }
            }
            if (m.lookPanel == null && m.paletteView != null) {
                Transform panel = m.paletteView.transform.parent;
                GameObject look = new GameObject("Look Panel", typeof(RectTransform));
                look.layer = 5;
                look.transform.SetParent(panel, false);
                look.transform.SetSiblingIndex(m.paletteView.transform.GetSiblingIndex() + 1);
                VLayout(look, 0, 14f);
                Flexible(look, true);
                foreach (string n in new[] { "Sides Heading", "Sides", "Name", "Width", "Rules" }) {
                    Transform t = panel.Find(n);
                    if (t != null) t.SetParent(look.transform, false);
                }
                m.lookPanel = look;
                done.Add("Look page made (Sides, Name, Width, Rules moved onto it)");
            }
            Transform neutral = m.sideRow != null ? m.sideRow.transform.Find("Neutral") : null;
            if (neutral != null) { Object.DestroyImmediate(neutral.gameObject); done.Add("Neutral checkbox removed (Neutral is in the For list)"); }
            if (done.Count > 0) { EditorUtility.SetDirty(m); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        } finally {
            if (!open) EditorSceneManager.CloseScene(scene, true);
            if (was.IsValid()) SceneManager.SetActiveScene(was);
        }
        Debug.Log(done.Count > 0 ? "Updated " + path + ": " + string.Join("; ", done.ToArray()) + "." : path + " is up to date.");
    }

    static FrontEndSettings Settings() {
        FrontEndSettings s = AssetDatabase.LoadAssetAtPath<FrontEndSettings>(SettingsPath);
        if (s == null) {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            s = ScriptableObject.CreateInstance<FrontEndSettings>();
            AssetDatabase.CreateAsset(s, SettingsPath);
            Debug.Log("Created " + SettingsPath + " (the front end's scenes, folders, AI profiles and report settings).");
        }
        // The AI profiles a side can be played by: every AIProfile asset, if none is listed yet (a list you set is kept).
        if (s.aiProfiles.Count == 0) {
            // (By every ScriptableObject: "t:AIProfile" is ambiguous, the old AI has a class of that name too.)
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" })) {
                HexTactica.AI.AIProfile p = AssetDatabase.LoadAssetAtPath<HexTactica.AI.AIProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null && !s.aiProfiles.Contains(p)) s.aiProfiles.Add(p);
            }
            if (s.aiProfiles.Count > 0) { EditorUtility.SetDirty(s); Debug.Log("FrontEndSettings: listed " + s.aiProfiles.Count + " AI profile(s) for the pre-game screen."); }
        }
        return s;
    }

    static void CreateMissing(bool fromMenu) {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        FrontEndSettings s = Settings();
        List<string> made = new List<string>();
        Make(s.mainMenuScene, BuildMainMenu, fromMenu, made);
        Make(s.preGameScene, BuildPreGame, fromMenu, made);
        Make(s.postGameScene, BuildPostGame, fromMenu, made);
        Make(s.inGameMenuScene, BuildInGameMenu, fromMenu, made);
        Make(s.mapEditorScene, BuildMapEditor, fromMenu, made);
        bool listed = ListInBuild(s);
        AssetDatabase.SaveAssets();
        if (made.Count > 0) Debug.Log("Created the front end's scenes: " + string.Join(", ", made.ToArray()) + ". They are yours to edit; open " + SceneFolder + s.mainMenuScene + ".unity and press Play.");
        else if (fromMenu) Debug.Log("The front end's scenes are all there" + (listed ? " (build settings updated)." : "; nothing changed."));
    }

    static void Make(string name, System.Action build, bool fromMenu, List<string> made) {
        string path = SceneFolder + name + ".unity";
        if (File.Exists(path)) return;
        // A scene is made beside the open ones and closed again: what you have open is left as it is.
        for (int i = 0; i < SceneManager.sceneCount; i++) {
            if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)) {
                if (fromMenu) Debug.LogWarning("Save the open untitled scene first: " + path + " was not made.");
                return;
            }
        }
        PrepareArt();
        Scene was = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try {
            build();
            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, path);
            made.Add(name);
        } finally {
            if (was.IsValid()) SceneManager.SetActiveScene(was);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>The front end's scenes in the build settings (added if missing; MainMenu first, then the game scene).</summary>
    static bool ListInBuild(FrontEndSettings s) {
        List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool changed = false;
        string[] ours = { s.mainMenuScene, s.preGameScene, s.postGameScene, s.inGameMenuScene, s.mapEditorScene };
        foreach (string n in ours) {
            string path = SceneFolder + n + ".unity";
            if (!File.Exists(path) || list.Exists(x => x.path == path)) continue;
            if (n == s.mainMenuScene) list.Insert(0, new EditorBuildSettingsScene(path, true));
            else list.Add(new EditorBuildSettingsScene(path, true));
            changed = true;
        }
        if (changed) EditorBuildSettings.scenes = list.ToArray();
        return changed;
    }

    // ================================================================== the scenes

    static void BuildMainMenu() {
        FrontEndSettings settings = Settings();
        Camera cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        cam.tag = "MainCamera";
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Background;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        // The unit explorer's stage: far below the menu, filmed into a render texture.
        GameObject stageRoot = new GameObject("Unit Preview Stage");
        stageRoot.transform.position = new Vector3(0f, -1000f, 0f);
        UnitPreview preview = stageRoot.AddComponent<UnitPreview>();
        Transform stage = new GameObject("Stage").transform;
        stage.SetParent(stageRoot.transform, false);
        Camera pc = new GameObject("Preview Camera", typeof(Camera)).GetComponent<Camera>();
        pc.transform.SetParent(stageRoot.transform, false);
        pc.transform.localPosition = new Vector3(0f, 0f, -10f);
        pc.orthographic = true;
        pc.orthographicSize = 1.5f;
        pc.clearFlags = CameraClearFlags.SolidColor;
        pc.backgroundColor = new Color(0.16f, 0.2f, 0.26f, 1f);
        pc.targetTexture = PreviewTexture();
        preview.stage = stage;
        preview.previewCamera = pc;

        Canvas canvas = MakeCanvas("Main Menu", 0);
        Transform root = canvas.transform;
        MainMenu menu = canvas.gameObject.AddComponent<MainMenu>();
        Text(root, "Title", "HexTactica", 72, TextAlignmentOptions.Center, FontStyles.Bold).rectTransform.Let(rt => Place(rt, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(900f, 110f)));

        // Main buttons.
        GameObject main = Column(root, "Main", 440f, 520f, new Vector2(0f, -40f));
        Btn(main.transform, "Load Scenario", menu.ShowLoadScenario);
        Btn(main.transform, "Map Editor", menu.ShowMapEditor);
        Btn(main.transform, "Unit Explorer", menu.ShowUnitExplorer);
        Btn(main.transform, "Options", menu.ShowOptions);
        Btn(main.transform, "Exit", menu.Exit);
        menu.mainPanel = main;

        // Load Scenario: scenarios or saved games.
        GameObject load = Window(root, "Load Scenario", 1200f, 820f);
        Text(load.transform, "Heading", "Load Scenario", 40, TextAlignmentOptions.Left, FontStyles.Bold);
        GameObject tabs = Row(load.transform, "Tabs", 50f);
        Btn(tabs.transform, "Scenarios", menu.ListScenarios, 220f);
        Btn(tabs.transform, "Saved games", menu.ListSaves, 220f);
        Spacer(tabs.transform);
        FileList loadList = FileBrowser(load.transform, "Files");
        GameObject loadButtons = Row(load.transform, "Buttons", 60f);
        Btn(loadButtons.transform, "Back", menu.ShowMain, 200f);
        Spacer(loadButtons.transform);
        Button play = Btn(loadButtons.transform, "Set up game", menu.Play, 280f);
        menu.loadPanel = load; menu.loadList = loadList; menu.playButton = play; menu.playButtonLabel = play.GetComponentInChildren<TMP_Text>();
        menu.playScenarioLabel = "Set up game"; menu.playSaveLabel = "Continue";

        // Map editor: a scenario to edit, or a new map.
        GameObject editor = Window(root, "Map Editor", 1200f, 820f);
        Text(editor.transform, "Heading", "Map Editor", 40, TextAlignmentOptions.Left, FontStyles.Bold);
        Text(editor.transform, "Hint", "Pick a scenario to edit, or start a new map.", 24, TextAlignmentOptions.Left, FontStyles.Normal);
        FileList editorList = FileBrowser(editor.transform, "Files");
        GameObject editorButtons = Row(editor.transform, "Buttons", 60f);
        Btn(editorButtons.transform, "Back", menu.ShowMain, 200f);
        Spacer(editorButtons.transform);
        Btn(editorButtons.transform, "New map", menu.EditNew, 220f);
        Button edit = Btn(editorButtons.transform, "Edit", menu.EditPicked, 220f);
        menu.editorPanel = editor; menu.editorList = editorList; menu.editButton = edit;

        // Unit explorer.
        GameObject units = Window(root, "Unit Explorer", 1780f, 960f);
        UnitExplorer explorer = units.AddComponent<UnitExplorer>();
        Text(units.transform, "Heading", "Unit Explorer", 40, TextAlignmentOptions.Left, FontStyles.Bold);
        GameObject body = Row(units.transform, "Body", -1f, 1f);
        RectTransform list = ScrollList(body.transform, "Units", 380f, -1f);
        explorer.listContent = list;
        explorer.rowTemplate = RowPrefab("Unit Row", UnitRow);
        GameObject middle = Column(body.transform, "Picture", 640f, -1f, Vector2.zero, false);
        Flexible(middle, true);
        RawImage pic = Go("Preview", middle.transform, typeof(RawImage)).GetComponent<RawImage>();
        pic.texture = PreviewTexture();
        Size(pic.gameObject, 640f, 640f);
        GameObject anim = Row(middle.transform, "Animation", 56f);
        Btn(anim.transform, "<", explorer.PreviousAnimation, 70f);
        explorer.animationName = Text(anim.transform, "Name", "Idle", 26, TextAlignmentOptions.Center, FontStyles.Normal);
        Flexible(explorer.animationName.gameObject);
        Btn(anim.transform, ">", explorer.NextAnimation, 70f);
        explorer.preview = preview;
        GameObject info = Column(body.transform, "Info", -1f, -1f, Vector2.zero, false);
        Flexible(info);
        Flexible(info, true);
        explorer.unitName = Text(info.transform, "Name", "Unit", 36, TextAlignmentOptions.Left, FontStyles.Bold);
        explorer.kindLine = Text(info.transform, "Kind", "", 22, TextAlignmentOptions.Left, FontStyles.Italic);
        RectTransform attrs = ScrollList(info.transform, "Attributes", -1f, 430f);
        explorer.attributeContent = attrs;
        explorer.attributeTemplate = RowPrefab("Attribute Row", AttrRow);
        explorer.dotFull = Widget("bullet");
        explorer.dotEmpty = Widget("bullet_empty");
        string[,] icons = {
            { "Strength", "icon_strength" }, { "Firepower", "icon_firepower" }, { "Targeting", "icon_targeting" }, { "Evasion", "icon_evasion" },
            { "Armor", "icon_armor" }, { "Piercing", "icon_piercing" }, { "Concealment", "icon_concealment" }, { "Move range", "icon_moverange" },
            { "Terrain penalty", "icon_terrain" }, { "Ammunition", "icon_ammo" }, { "Fuel", "icon_fuel" }, { "Supplies", "icon_supplies" },
            { "Attack range", "icon_targeting" }, { "Vision", "icon_concealment" }, { "Cost", "icon_building" }, { "Locomotion", "icon_move" }
        };
        for (int i = 0; i < icons.GetLength(0); i++)
            explorer.icons.Add(new UnitExplorer.AttributeIcon { attribute = icons[i, 0], icon = Widget(icons[i, 1]) });
        explorer.description = Text(info.transform, "Description", "", 24, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Flexible(explorer.description.gameObject, true);
        GameObject unitButtons = Row(units.transform, "Buttons", 60f);
        Btn(unitButtons.transform, "Back", menu.ShowMain, 200f);
        menu.unitsPanel = units;

        // Options.
        GameObject options = Window(root, "Options", 760f, 640f);
        OptionsPanel op = options.AddComponent<OptionsPanel>();
        Text(options.transform, "Heading", "Options", 40, TextAlignmentOptions.Left, FontStyles.Bold);
        GameObject vol = Row(options.transform, "Volume", 50f);
        Text(vol.transform, "Label", "Sound volume", 26, TextAlignmentOptions.Left, FontStyles.Normal).gameObject.Let(g => Size(g, 260f, -1f));
        op.volume = Slide(vol.transform);
        op.volumeValue = Text(vol.transform, "Value", "100%", 26, TextAlignmentOptions.Right, FontStyles.Normal);
        Size(op.volumeValue.gameObject, 90f, -1f);
        op.fullScreen = Tog(options.transform, "Full screen");
        GameObject res = Row(options.transform, "Resolution", 50f);
        Text(res.transform, "Label", "Resolution", 26, TextAlignmentOptions.Left, FontStyles.Normal).gameObject.Let(g => Size(g, 260f, -1f));
        op.resolution = Drop(res.transform, 360f);
        GameObject q = Row(options.transform, "Quality", 50f);
        Text(q.transform, "Label", "Graphics quality", 26, TextAlignmentOptions.Left, FontStyles.Normal).gameObject.Let(g => Size(g, 260f, -1f));
        op.quality = Drop(q.transform, 360f);
        op.vSync = Tog(options.transform, "Vertical sync");
        Spacer(options.transform, true);
        GameObject optButtons = Row(options.transform, "Buttons", 60f);
        Btn(optButtons.transform, "Back", menu.ShowMain, 200f);
        menu.optionsPanel = options;

        foreach (GameObject p in new[] { load, editor, units, options }) p.SetActive(false);
    }

    static void BuildPreGame() {
        Canvas canvas = MakeCanvas("Pre-Game", 100);
        PreGameMenu m = canvas.gameObject.AddComponent<PreGameMenu>();
        GameObject panel = SidePanel(canvas.transform, "Setup", 760f, true);
        m.title = Text(panel.transform, "Title", "Scenario", 40, TextAlignmentOptions.Left, FontStyles.Bold);
        m.details = Text(panel.transform, "Details", "", 22, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Text(panel.transform, "Sides", "Sides", 28, TextAlignmentOptions.Left, FontStyles.Bold);
        GameObject rows = Column(panel.transform, "Rows", -1f, -1f, Vector2.zero, false);
        m.rows = (RectTransform)rows.transform;
        m.rowTemplate = RowPrefab("Side Row", SetupRow);
        Text(panel.transform, "Rules", "Rules", 28, TextAlignmentOptions.Left, FontStyles.Bold);
        m.fog = Tog(panel.transform, "Fog of war");
        m.build = Tog(panel.transform, "Build units (purchases at buildings)");
        m.specialUnits = Tog(panel.transform, "Faction special units");
        m.specialNote = Text(panel.transform, "Special note", "", 20, TextAlignmentOptions.TopLeft, FontStyles.Italic);
        Spacer(panel.transform, true);
        GameObject buttons = Row(panel.transform, "Buttons", 64f);
        Btn(buttons.transform, "Back", m.Back, 200f);
        Spacer(buttons.transform);
        m.startButton = Btn(buttons.transform, "Start", m.StartGame, 240f);
    }

    static void BuildPostGame() {
        Canvas canvas = MakeCanvas("Post-Game", 110);
        PostGameScreen m = canvas.gameObject.AddComponent<PostGameScreen>();
        Dim(canvas.transform);
        GameObject w = Window(canvas.transform, "Report", 1500f, 940f);
        m.headline = Text(w.transform, "Headline", "Blue won", 44, TextAlignmentOptions.Left, FontStyles.Bold);
        GameObject tabs = Row(w.transform, "Tabs", 50f);
        Btn(tabs.transform, "What happened", m.ShowFindings, 260f);
        Btn(tabs.transform, "Units", m.ShowUnits, 200f);
        Spacer(tabs.transform);
        GameObject pages = Column(w.transform, "Pages", -1f, -1f, Vector2.zero, false);
        Flexible(pages, true);
        RectTransform f = ScrollList(pages.transform, "Findings", -1f, -1f);
        Flexible(f.GetComponentInParent<ScrollRect>().gameObject, true);
        m.findings = Text(f, "Text", "", 24, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        m.findingsPage = f.GetComponentInParent<ScrollRect>().gameObject;
        RectTransform u = ScrollList(pages.transform, "Units", -1f, -1f);
        Flexible(u.GetComponentInParent<ScrollRect>().gameObject, true);
        m.unitTables = Text(u, "Text", "", 22, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        m.unitsPage = u.GetComponentInParent<ScrollRect>().gameObject;
        GameObject buttons = Row(w.transform, "Buttons", 64f);
        Btn(buttons.transform, "View board", m.ViewBoard, 240f);
        Spacer(buttons.transform);
        Btn(buttons.transform, "Play again", m.PlayAgain, 240f);
        Btn(buttons.transform, "Main menu", m.MainMenu, 240f);
    }

    static void BuildInGameMenu() {
        Canvas canvas = MakeCanvas("In-Game Menu", 120);
        InGameMenu m = canvas.gameObject.AddComponent<InGameMenu>();
        Dim(canvas.transform);
        GameObject w = Window(canvas.transform, "Menu", 1200f, 820f);
        Text(w.transform, "Heading", "Paused", 44, TextAlignmentOptions.Left, FontStyles.Bold);
        GameObject top = Row(w.transform, "Resume", 60f);
        Btn(top.transform, "Resume", m.Resume, 240f);
        Spacer(top.transform);
        Btn(top.transform, "Exit to main menu", m.ExitToMainMenu, 320f);
        Text(w.transform, "Save heading", "Save", 28, TextAlignmentOptions.Left, FontStyles.Bold);
        GameObject save = Row(w.transform, "Save", 56f);
        m.saveName = Field(save.transform, "Name of the save");
        Flexible(m.saveName.gameObject);
        Btn(save.transform, "Save", m.Save, 200f);
        Text(w.transform, "Load heading", "Load", 28, TextAlignmentOptions.Left, FontStyles.Bold);
        m.saves = FileBrowser(w.transform, "Saves");
        m.saves.source = FileList.Source.Saves;
        m.saves.empty = "No saved games yet.";
        GameObject bottom = Row(w.transform, "Buttons", 60f);
        m.status = Text(bottom.transform, "Status", "", 22, TextAlignmentOptions.Left, FontStyles.Italic);
        Flexible(m.status.gameObject);
        m.loadButton = Btn(bottom.transform, "Load", m.Load, 200f);
    }

    static void BuildMapEditor() {
        Canvas canvas = MakeCanvas("Map Editor", 100);
        MapEditorPanel m = canvas.gameObject.AddComponent<MapEditorPanel>();
        GameObject panel = SidePanel(canvas.transform, "Tools", 480f, true);
        Text(panel.transform, "Heading", "Map Editor", 34, TextAlignmentOptions.Left, FontStyles.Bold);
        GameObject tools = Row(panel.transform, "Tools", 48f);
        Btn(tools.transform, "Look", m.ToolLook, -1f, 22);
        Btn(tools.transform, "Terrain", m.ToolTerrain, -1f, 22);
        Btn(tools.transform, "Units", m.ToolUnits, -1f, 22);
        Btn(tools.transform, "Buildings", m.ToolBuildings, -1f, 22);
        m.toolLabel = Text(panel.transform, "Tool", "", 20, TextAlignmentOptions.Left, FontStyles.Italic);
        GameObject side = Row(panel.transform, "Side", 44f);
        m.sideRow = side;
        Text(side.transform, "Label", "For", 22, TextAlignmentOptions.Left, FontStyles.Normal).gameObject.Let(g => Size(g, 60f, -1f));
        m.side = Drop(side.transform, 300f);
        GameObject filter = Row(panel.transform, "Unit Filter", 44f);
        m.unitFilterRow = filter;
        Text(filter.transform, "Label", "Show", 22, TextAlignmentOptions.Left, FontStyles.Normal).gameObject.Let(g => Size(g, 60f, -1f));
        m.unitFilter = Drop(filter.transform, 300f);
        RectTransform pal = ScrollList(panel.transform, "Palette", -1f, -1f);
        Flexible(pal.GetComponentInParent<ScrollRect>().gameObject, true);
        m.palette = pal;
        m.paletteView = pal.GetComponentInParent<ScrollRect>().gameObject;
        m.paletteTemplate = RowPrefab("Palette Entry", PaletteRow);

        // The Look tool's page: the map's sides, name, width and rules.
        GameObject look = Column(panel.transform, "Look Panel", -1f, -1f, Vector2.zero, false);
        Flexible(look, true);
        m.lookPanel = look;
        panel = look;
        GameObject sidesHead = Row(panel.transform, "Sides Heading", 40f);
        Text(sidesHead.transform, "Label", "Sides", 24, TextAlignmentOptions.Left, FontStyles.Bold).gameObject.Let(g => Flexible(g));
        Btn(sidesHead.transform, "Add side", m.AddSide, 150f, 20);
        GameObject sides = Column(panel.transform, "Sides", -1f, -1f, Vector2.zero, false);
        sides.GetComponent<VerticalLayoutGroup>().spacing = 4f;
        m.sides = (RectTransform)sides.transform;
        m.sideTemplate = RowPrefab("Map Side Row", MapSideRow);

        GameObject name = Row(panel.transform, "Name", 46f);
        Text(name.transform, "Label", "Name", 22, TextAlignmentOptions.Left, FontStyles.Normal).gameObject.Let(g => Size(g, 80f, -1f));
        m.scenarioName = Field(name.transform, "Scenario name");
        Flexible(m.scenarioName.gameObject);
        GameObject width = Row(panel.transform, "Width", 46f);
        Text(width.transform, "Label", "Width", 22, TextAlignmentOptions.Left, FontStyles.Normal).gameObject.Let(g => Size(g, 80f, -1f));
        m.boardWidth = Field(width.transform, "Width");
        m.boardWidth.contentType = TMP_InputField.ContentType.IntegerNumber;
        Flexible(m.boardWidth.gameObject);
        Btn(width.transform, "Resize", m.Resize, 130f, 22);
        GameObject rules = Row(panel.transform, "Rules", 44f);
        m.fog = Tog(rules.transform, "Fog of war", 22);
        m.build = Tog(rules.transform, "Purchases", 22);
        panel = m.lookPanel.transform.parent.gameObject;
        GameObject file = Row(panel.transform, "File", 50f);
        Btn(file.transform, "New", m.NewMap, -1f, 22);
        Btn(file.transform, "Load", m.ShowLoad, -1f, 22);
        Btn(file.transform, "Save", m.SaveButton, -1f, 22);
        Btn(file.transform, "Play", m.PlayTest, -1f, 22);
        Btn(file.transform, "Exit", m.Exit, -1f, 22);
        m.status = Text(panel.transform, "Status", "", 18, TextAlignmentOptions.Left, FontStyles.Italic);

        GameObject load = Window(canvas.transform, "Load", 1100f, 760f);
        Text(load.transform, "Heading", "Load a scenario to edit", 34, TextAlignmentOptions.Left, FontStyles.Bold);
        m.loadList = FileBrowser(load.transform, "Files");
        GameObject lb = Row(load.transform, "Buttons", 60f);
        Btn(lb.transform, "Cancel", m.HideLoad, 200f);
        Spacer(lb.transform);
        Btn(lb.transform, "Load", m.LoadPicked, 200f);
        m.loadPanel = load;
        load.SetActive(false);
    }

    // ================================================================== rows (prefabs the lists copy)

    /// <summary>The row prefab of this name: made from <paramref name="build"/> if it does not exist (Assets/FrontEnd/Prefabs), else the one there.</summary>
    static T RowPrefab<T>(string name, System.Func<Transform, T> build) where T : Component {
        string path = PrefabFolder + name + ".prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing.GetComponent<T>();
        Directory.CreateDirectory(PrefabFolder);
        T made = build(null);
        GameObject go = made.gameObject;
        go.name = name;
        try {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            return saved.GetComponent<T>();
        } finally {
            Object.DestroyImmediate(go);
        }
    }

    static FileListRow FileRow(Transform parent) {
        GameObject g = ListRow(parent, "File Row", 46f);
        FileListRow r = g.AddComponent<FileListRow>();
        r.background = g.GetComponent<Image>();
        r.button = g.GetComponent<Button>();
        r.label = Text(g.transform, "Name", "Scenario", 24, TextAlignmentOptions.Left, FontStyles.Normal);
        Flexible(r.label.gameObject);
        r.note = Text(g.transform, "Note", "2026-01-01 12:00", 18, TextAlignmentOptions.Right, FontStyles.Italic);
        Size(r.note.gameObject, 260f, -1f);
        return r;
    }

    static UnitListRow UnitRow(Transform parent) {
        GameObject g = ListRow(parent, "Unit Row", 44f);
        UnitListRow r = g.AddComponent<UnitListRow>();
        r.background = g.GetComponent<Image>();
        r.button = g.GetComponent<Button>();
        r.label = Text(g.transform, "Name", "Unit", 22, TextAlignmentOptions.Left, FontStyles.Normal);
        Flexible(r.label.gameObject);
        r.note = Text(g.transform, "Note", "", 16, TextAlignmentOptions.Right, FontStyles.Italic);
        Size(r.note.gameObject, 140f, -1f);
        return r;
    }

    /// <summary>An attribute as the HUD shows one: icon, label, then dots (a hidden dot copied per dot) or a value.</summary>
    static AttributeRow AttrRow(Transform parent) {
        GameObject g = Row(parent, "Attribute Row", 34f);
        AttributeRow r = g.AddComponent<AttributeRow>();
        r.icon = Go("Icon", g.transform, typeof(Image)).GetComponent<Image>();
        r.icon.preserveAspect = true;
        Size(r.icon.gameObject, 28f, 28f);
        r.label = Text(g.transform, "Label", "Firepower", 22, TextAlignmentOptions.Left, FontStyles.Normal);
        Size(r.label.gameObject, 210f, -1f);
        GameObject dots = Go("Dots", g.transform);
        HorizontalLayoutGroup h = HLayout(dots, 0, 4f);
        Flexible(dots);
        r.dots = (RectTransform)dots.transform;
        r.dotTemplate = Go("Dot", dots.transform, typeof(Image)).GetComponent<Image>();
        r.dotTemplate.sprite = Widget("bullet");
        r.dotTemplate.preserveAspect = true;
        Size(r.dotTemplate.gameObject, 18f, 18f);
        r.dotTemplate.gameObject.SetActive(false);
        r.value = Text(g.transform, "Value", "0", 22, TextAlignmentOptions.Right, FontStyles.Bold);
        Size(r.value.gameObject, 240f, -1f);
        return r;
    }

    static PlayerSetupRow SetupRow(Transform parent) {
        GameObject g = Row(parent, "Side Row", 52f);
        PlayerSetupRow r = g.AddComponent<PlayerSetupRow>();
        r.swatch = Go("Colour", g.transform, typeof(Image)).GetComponent<Image>();
        r.swatch.sprite = Widget("hex");
        r.swatch.preserveAspect = true;
        Size(r.swatch.gameObject, 36f, 36f);
        r.sideName = Text(g.transform, "Name", "Blue", 26, TextAlignmentOptions.Left, FontStyles.Bold);
        Flexible(r.sideName.gameObject);
        r.control = Drop(g.transform, 230f);
        r.faction = Drop(g.transform, 230f);
        TextMeshProUGUI locked = Text(g.transform, "Fixed by the map", "fixed", 16, TextAlignmentOptions.Left, FontStyles.Italic);
        Size(locked.gameObject, 50f, -1f);
        r.lockedMark = locked.gameObject;
        return r;
    }

    static PaletteButton PaletteRow(Transform parent) {
        GameObject g = ListRow(parent, "Palette Entry", 64f);
        PaletteButton r = g.AddComponent<PaletteButton>();
        r.background = g.GetComponent<Image>();
        r.button = g.GetComponent<Button>();
        GameObject box = Go("Picture Box", g.transform);
        Size(box, 56f, 56f);
        r.icon = Go("Icon", box.transform, typeof(Image)).GetComponent<Image>();
        r.icon.preserveAspect = true;
        r.icon.raycastTarget = false;
        Fill(r.icon.rectTransform);
        r.picture = (RectTransform)Go("Picture", box.transform).transform;
        Fill(r.picture);
        r.label = Text(g.transform, "Name", "Grassland", 22, TextAlignmentOptions.Left, FontStyles.Normal);
        Flexible(r.label.gameObject);
        return r;
    }

    static MapPlayerRow MapSideRow(Transform parent) {
        GameObject g = Row(parent, "Map Side Row", 44f);
        MapPlayerRow r = g.AddComponent<MapPlayerRow>();
        GameObject colour = Go("Colour", g.transform, typeof(Image), typeof(Button));
        r.swatch = colour.GetComponent<Image>();
        r.swatch.sprite = Widget("hex");
        r.swatch.preserveAspect = true;
        r.colour = colour.GetComponent<Button>();
        r.colour.targetGraphic = r.swatch;
        Size(colour, 34f, 34f);
        r.sideName = Field(g.transform, "Name");
        Size(r.sideName.gameObject, 130f, 40f);
        r.faction = Drop(g.transform, 200f);
        r.remove = Btn(g.transform, "X", null, 40f, 20);
        return r;
    }

    /// <summary>A clickable row: a tinted background that is the button, its children laid out left to right.</summary>
    static GameObject ListRow(Transform parent, string name, float height) {
        GameObject g = Go(name, parent, typeof(Image), typeof(Button));
        Image bg = g.GetComponent<Image>();
        bg.sprite = Widget("panel"); bg.type = Image.Type.Sliced; bg.color = new Color(1f, 1f, 1f, 0.08f);
        g.GetComponent<Button>().targetGraphic = bg;
        HLayout(g, 12, 10f);
        Size(g, -1f, height);
        return g;
    }

    // ================================================================== building blocks

    static readonly Color Background = new Color(0.08f, 0.09f, 0.12f, 1f);
    static readonly Color PanelColor = new Color(0.11f, 0.13f, 0.17f, 0.96f);
    static readonly Color ButtonColor = new Color(0.22f, 0.27f, 0.36f, 1f);
    static readonly Color Accent = new Color(0.36f, 0.62f, 0.95f, 1f);
    static readonly Color TextColor = new Color(0.93f, 0.94f, 0.96f, 1f);

    static Sprite Widget(string name) { return AssetDatabase.LoadAssetAtPath<Sprite>(HudPrefab.Art + name + ".png"); }
    static Sprite Builtin(string name) { return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/" + name + ".psd"); }

    static void PrepareArt() { HudPrefab.PrepareArt(); }

    static DefaultControls.Resources UiResources() {
        return new DefaultControls.Resources {
            standard = Builtin("UISprite"), background = Builtin("Background"), inputField = Builtin("InputFieldBackground"),
            knob = Builtin("Knob"), checkmark = Builtin("Checkmark"), dropdown = Builtin("DropdownArrow"), mask = Builtin("UIMask")
        };
    }

    static TMP_DefaultControls.Resources TmpResources() {
        return new TMP_DefaultControls.Resources {
            standard = Builtin("UISprite"), background = Builtin("Background"), inputField = Builtin("InputFieldBackground"),
            knob = Builtin("Knob"), checkmark = Builtin("Checkmark"), dropdown = Builtin("DropdownArrow"), mask = Builtin("UIMask")
        };
    }

    static RenderTexture PreviewTexture() {
        RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(PreviewTexturePath);
        if (rt != null) return rt;
        Directory.CreateDirectory(Path.GetDirectoryName(PreviewTexturePath));
        rt = new RenderTexture(1024, 1024, 24) { name = "UnitPreview" };
        AssetDatabase.CreateAsset(rt, PreviewTexturePath);
        return rt;
    }

    static GameObject Go(string name, Transform parent, params System.Type[] components) {
        GameObject g = new GameObject(name, typeof(RectTransform));
        foreach (System.Type t in components) g.AddComponent(t);
        g.layer = 5;   // UI
        if (parent != null) g.transform.SetParent(parent, false);
        return g;
    }

    static Canvas MakeCanvas(string name, int order) {
        GameObject g = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        g.layer = 5;
        Canvas c = g.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = order;
        CanvasScaler s = g.GetComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920f, 1080f);
        s.matchWidthOrHeight = 0.5f;
        return c;
    }

    static void Fill(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size) {
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position; rt.sizeDelta = size;
    }

    static Image Panel(GameObject g, Color c) {
        Image i = g.GetComponent<Image>();
        if (i == null) i = g.AddComponent<Image>();
        i.sprite = Widget("panel"); i.type = Image.Type.Sliced; i.color = c;
        return i;
    }

    /// <summary>A darkened screen behind a window (and catching clicks meant for the board).</summary>
    static void Dim(Transform parent) {
        GameObject g = Go("Dim", parent, typeof(Image));
        g.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        Fill((RectTransform)g.transform);
    }

    /// <summary>A centred window, its children in a column.</summary>
    static GameObject Window(Transform parent, string name, float w, float h) {
        GameObject g = Go(name, parent, typeof(Image));
        Panel(g, PanelColor);
        Place((RectTransform)g.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
        VLayout(g, 28, 14f);
        return g;
    }

    /// <summary>A panel down one side of the screen (the board stays in view beside it).</summary>
    static GameObject SidePanel(Transform parent, string name, float w, bool left) {
        GameObject g = Go(name, parent, typeof(Image));
        Panel(g, PanelColor);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = new Vector2(left ? 0f : 1f, 0f); rt.anchorMax = new Vector2(left ? 0f : 1f, 1f);
        rt.pivot = new Vector2(left ? 0f : 1f, 0.5f);
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(w, 0f);
        VLayout(g, 22, 10f);
        return g;
    }

    /// <summary>A column: placed on its own (a size and an offset from the centre), or laid out by its parent (w, h: preferred sizes, -1 none).</summary>
    static GameObject Column(Transform parent, string name, float w, float h, Vector2 offset, bool placed = true) {
        GameObject g = Go(name, parent);
        if (placed) Place((RectTransform)g.transform, new Vector2(0.5f, 0.5f), offset, new Vector2(w, h));
        else Size(g, w, h);
        VLayout(g, 0, 14f);
        return g;
    }

    static GameObject Row(Transform parent, string name, float h, float flexH = -1f) {
        GameObject g = Go(name, parent);
        HLayout(g, 0, 12f);
        LayoutElement le = Size(g, -1f, h);
        if (flexH > 0f) le.flexibleHeight = flexH;
        return g;
    }

    static void Spacer(Transform parent, bool vertical = false) {
        GameObject g = Go("Space", parent);
        LayoutElement le = g.AddComponent<LayoutElement>();
        if (vertical) le.flexibleHeight = 1f; else le.flexibleWidth = 1f;
    }

    static VerticalLayoutGroup VLayout(GameObject g, int pad, float spacing) {
        VerticalLayoutGroup v = g.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(pad, pad, pad, pad);
        v.spacing = spacing;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        return v;
    }

    static HorizontalLayoutGroup HLayout(GameObject g, int pad, float spacing) {
        HorizontalLayoutGroup h = g.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(pad, pad, 0, 0);
        h.spacing = spacing;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        return h;
    }

    static LayoutElement Size(GameObject g, float w, float h) {
        LayoutElement le = g.GetComponent<LayoutElement>();
        if (le == null) le = g.AddComponent<LayoutElement>();
        if (w >= 0f) { le.preferredWidth = w; le.minWidth = w; }
        if (h >= 0f) { le.preferredHeight = h; le.minHeight = h; }
        return le;
    }

    static void Flexible(GameObject g, bool vertical = false) {
        LayoutElement le = g.GetComponent<LayoutElement>();
        if (le == null) le = g.AddComponent<LayoutElement>();
        if (vertical) le.flexibleHeight = 1f; else le.flexibleWidth = 1f;
    }

    static TextMeshProUGUI Text(Transform parent, string name, string text, float size, TextAlignmentOptions align, FontStyles style) {
        GameObject g = Go(name, parent, typeof(TextMeshProUGUI));
        TextMeshProUGUI t = g.GetComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.alignment = align; t.fontStyle = style; t.color = TextColor;
        t.raycastTarget = false;
        return t;
    }

    static Button Btn(Transform parent, string label, UnityAction call, float w = -1f, float fontSize = 28f) {
        GameObject g = Go(label, parent, typeof(Image), typeof(Button));
        Image i = Panel(g, ButtonColor);
        Button b = g.GetComponent<Button>();
        b.targetGraphic = i;
        ColorBlock cb = b.colors;
        cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f); cb.pressedColor = Accent; cb.selectedColor = Color.white;
        b.colors = cb;
        TextMeshProUGUI t = Text(g.transform, "Label", label, fontSize, TextAlignmentOptions.Center, FontStyles.Bold);
        Fill(t.rectTransform);
        LayoutElement le = Size(g, w, fontSize > 24f ? 60f : 46f);
        if (w < 0f) le.flexibleWidth = 1f;
        if (call != null) UnityEventTools.AddPersistentListener(b.onClick, call);
        return b;
    }

    static Toggle Tog(Transform parent, string label, float fontSize = 26f) {
        GameObject g = Go(label, parent, typeof(Toggle));
        HLayout(g, 0, 10f);
        Size(g, -1f, 44f);
        GameObject box = Go("Background", g.transform, typeof(Image));
        Image bi = box.GetComponent<Image>();
        bi.sprite = Builtin("UISprite"); bi.type = Image.Type.Sliced; bi.color = Color.white;
        Size(box, 32f, 32f);
        GameObject check = Go("Checkmark", box.transform, typeof(Image));
        Image ci = check.GetComponent<Image>();
        ci.sprite = Builtin("Checkmark"); ci.color = Accent;
        Fill((RectTransform)check.transform);
        TextMeshProUGUI t = Text(g.transform, "Label", label, fontSize, TextAlignmentOptions.Left, FontStyles.Normal);
        Flexible(t.gameObject);
        Toggle tog = g.GetComponent<Toggle>();
        tog.targetGraphic = bi; tog.graphic = ci; tog.isOn = true;
        return tog;
    }

    static Slider Slide(Transform parent) {
        GameObject g = DefaultControls.CreateSlider(UiResources());
        g.transform.SetParent(parent, false);
        LayoutElement le = Size(g, -1f, 28f);
        le.flexibleWidth = 1f;
        return g.GetComponent<Slider>();
    }

    static TMP_Dropdown Drop(Transform parent, float w) {
        GameObject g = TMP_DefaultControls.CreateDropdown(TmpResources());
        g.transform.SetParent(parent, false);
        Size(g, w, 44f);
        TMP_Dropdown d = g.GetComponent<TMP_Dropdown>();
        if (d.captionText != null) d.captionText.fontSize = 22f;
        if (d.itemText != null) {
            d.itemText.fontSize = 22f;
            RectTransform item = (RectTransform)d.itemText.transform.parent;
            item.sizeDelta = new Vector2(item.sizeDelta.x, 38f);
            RectTransform content = (RectTransform)item.parent;
            content.sizeDelta = new Vector2(content.sizeDelta.x, 38f);
        }
        if (d.template != null) d.template.sizeDelta = new Vector2(d.template.sizeDelta.x, 300f);
        d.ClearOptions();
        return d;
    }

    static TMP_InputField Field(Transform parent, string placeholder) {
        GameObject g = TMP_DefaultControls.CreateInputField(TmpResources());
        g.transform.SetParent(parent, false);
        Size(g, -1f, 46f);
        TMP_InputField f = g.GetComponent<TMP_InputField>();
        if (f.textComponent != null) f.textComponent.fontSize = 22f;
        TMP_Text ph = f.placeholder as TMP_Text;
        if (ph != null) { ph.fontSize = 22f; ph.text = placeholder; }
        return f;
    }

    /// <summary>A scroll view (vertical only) whose content lays its children out in a column; returns the content.</summary>
    static RectTransform ScrollList(Transform parent, string name, float w, float h) {
        GameObject g = DefaultControls.CreateScrollView(UiResources());
        g.name = name;
        g.transform.SetParent(parent, false);
        Panel(g, new Color(0f, 0f, 0f, 0.25f));
        ScrollRect sr = g.GetComponent<ScrollRect>();
        sr.horizontal = false;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 30f;
        if (sr.horizontalScrollbar != null) { Object.DestroyImmediate(sr.horizontalScrollbar.gameObject); sr.horizontalScrollbar = null; }
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        LayoutElement le = Size(g, w, h);
        if (w < 0f) le.flexibleWidth = 1f;
        if (h < 0f) le.flexibleHeight = 1f;
        RectTransform content = sr.content;
        VerticalLayoutGroup v = VLayout(content.gameObject, 8, 6f);
        v.padding = new RectOffset(8, 8, 8, 8);
        ContentSizeFitter fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return content;
    }

    /// <summary>A list of files beside the picked one's details.</summary>
    static FileList FileBrowser(Transform parent, string name) {
        GameObject row = Row(parent, name, -1f, 1f);
        RectTransform content = ScrollList(row.transform, "List", 640f, -1f);
        FileList list = row.AddComponent<FileList>();
        list.content = content;
        list.rowTemplate = RowPrefab("File Row", FileRow);
        GameObject detailBox = Go("Details", row.transform, typeof(Image));
        Panel(detailBox, new Color(0f, 0f, 0f, 0.25f));
        VLayout(detailBox, 16, 6f);
        Flexible(detailBox);
        Flexible(detailBox, true);
        list.details = Text(detailBox.transform, "Text", "", 22, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Flexible(list.details.gameObject, true);
        return list;
    }

    static T Let<T>(this T x, System.Action<T> a) { a(x); return x; }
}
