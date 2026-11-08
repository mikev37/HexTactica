using System.Collections.Generic;
using System.IO;
using System.Linq;
using Defective.JSON;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The map editor's tools (the MapEditor scene, over the game scene held still): paint terrain, place units for a
/// side, place buildings (for a side or neutral) — clicking a unit or building again removes it, as in LevelEditor —
/// with a palette of every terrain, unit and building in Resources. The scenario's name, its board width, fog and
/// purchases; New (an empty board of the terrain picked), Load, Save, Play (the pre-game screen on it), Exit.
/// The palette's buttons are copies of a hidden template the scene holds.
/// </summary>
public class MapEditorPanel : MonoBehaviour {
    public enum Tool { Look, Terrain, Units, Buildings }

    [Header("Tools")]
    public PaletteButton paletteTemplate;
    public RectTransform palette;
    [Tooltip("Names the tool in use and what is picked.")]
    public TMP_Text toolLabel;
    [Tooltip("The side units and buildings are placed for.")]
    public TMP_Dropdown side;
    [Tooltip("Buildings are placed neutral (owned by nobody).")]
    public Toggle neutral;
    public Color paletteColor = new Color(1f, 1f, 1f, 0.08f);
    public Color pickedColor = new Color(0.36f, 0.62f, 0.95f, 0.55f);

    [Header("Scenario")]
    public TMP_InputField scenarioName;
    public TMP_InputField boardWidth;
    public Toggle fog;
    public Toggle build;
    [Tooltip("What was just done (saved, loaded, or why not).")]
    public TMP_Text status;
    [Tooltip("The name a new map starts with.")]
    public string newName = "New map";

    [Header("Load")]
    public GameObject loadPanel;
    public FileList loadList;

    readonly List<PaletteButton> buttons = new List<PaletteButton>();
    /// <summary>Who moves first in the scenario (the side to move when it was loaded): the Side list only says whose units are placed.</summary>
    Player firstToMove;
    Tool tool = Tool.Look;
    Object pickedItem;
    string loadedPath;

    void Awake() {
        if (paletteTemplate != null) paletteTemplate.gameObject.SetActive(false);
        if (loadPanel != null) loadPanel.SetActive(false);
        TransportPanel.EnsureEventSystem();
        if (side != null) side.onValueChanged.AddListener(i => ApplySide());
        if (neutral != null) neutral.onValueChanged.AddListener(v => { if (LevelEditor.singleton != null) LevelEditor.singleton.placeNeutral = v; });
        if (fog != null) fog.onValueChanged.AddListener(v => { if (GameController.gc != null) { GameController.gc.fogOfWar = v; FogOfWar.Reset(); FogOfWar.Changed(); FogOfWar.Refresh(); } });
        if (build != null) build.onValueChanged.AddListener(v => { if (GameController.gc != null) GameController.gc.purchasesAllowed = v; });
    }

    void Start() {
        loadedPath = string.IsNullOrEmpty(MatchSetup.FilePath) ? null : MatchSetup.FilePath;
        ReadBoard();
        SetTool(Tool.Look);
    }

    void OnDestroy() { SetFlags(Tool.Look); }

    /// <summary>The scenario's settings as the board has them now (after a load).</summary>
    void ReadBoard() {
        GameController gc = GameController.gc;
        if (scenarioName != null) scenarioName.text = loadedPath != null ? Path.GetFileNameWithoutExtension(loadedPath) : newName;
        if (boardWidth != null && HexGenerator.hg != null) boardWidth.text = HexGenerator.hg.width.ToString();
        if (gc == null) return;
        firstToMove = gc.activePlayer;
        if (fog != null) fog.SetIsOnWithoutNotify(gc.fogOfWar);
        if (build != null) build.SetIsOnWithoutNotify(gc.purchasesAllowed);
        if (side != null) {
            side.ClearOptions();
            side.AddOptions(gc.players.Where(p => p != null).Select(p => p.personal_name).ToList());
            side.SetValueWithoutNotify(Mathf.Max(0, gc.players.IndexOf(gc.activePlayer)));
        }
        if (neutral != null && LevelEditor.singleton != null) neutral.SetIsOnWithoutNotify(LevelEditor.singleton.placeNeutral);
    }

    // ------------------------------------------------------------------ tools

    public void ToolLook() { SetTool(Tool.Look); }
    public void ToolTerrain() { SetTool(Tool.Terrain); }
    public void ToolUnits() { SetTool(Tool.Units); }
    public void ToolBuildings() { SetTool(Tool.Buildings); }

    void SetTool(Tool t) {
        tool = t;
        pickedItem = null;
        FillPalette();
        if (buttons.Count > 0) Pick(buttons[0]);
        else { SetFlags(tool); ShowTool(); }
    }

    void FillPalette() {
        foreach (PaletteButton b in buttons) if (b != null) Destroy(b.gameObject);
        buttons.Clear();
        if (paletteTemplate == null || palette == null) return;
        IEnumerable<Object> items =
            tool == Tool.Terrain ? GameAssets.AllTerrains.OrderBy(x => x.name).Cast<Object>() :
            tool == Tool.Units ? GameAssets.AllUnits.OrderBy(x => x.name).Cast<Object>() :
            tool == Tool.Buildings ? GameAssets.AllBuildings.OrderBy(x => x.name).Cast<Object>() :
            Enumerable.Empty<Object>();
        foreach (Object o in items) {
            PaletteButton b = Instantiate(paletteTemplate, palette);
            b.gameObject.SetActive(true);
            b.Show(o, o.name, Icon(o), paletteColor);
            PaletteButton me = b;
            if (b.button != null) b.button.onClick.AddListener(() => Pick(me));
            buttons.Add(b);
        }
    }

    static Sprite Icon(Object o) {
        TerrainData td = o as TerrainData;
        if (td != null) return td.upper_normal != null ? td.upper_normal : td.floor;
        Building b = o as Building;
        if (b != null) return b.sprite != null ? b.sprite.sprite : null;
        Unit u = o as Unit;
        if (u != null) {
            SpriteRenderer sr = u.upper != null ? u.upper : u.lower != null ? u.lower : u.head;
            return sr != null ? sr.sprite : null;
        }
        return null;
    }

    void Pick(PaletteButton b) {
        pickedItem = b != null ? b.Item : null;
        foreach (PaletteButton x in buttons) if (x != null) x.Highlight(x == b ? pickedColor : paletteColor);
        LevelEditor le = LevelEditor.singleton;
        if (le != null) {
            if (pickedItem is TerrainData) le.td = (TerrainData)pickedItem;
            if (pickedItem is Unit) le.ut = (Unit)pickedItem;
            if (pickedItem is Building) le.bt = (Building)pickedItem;
        }
        SetFlags(tool);
        ShowTool();
    }

    /// <summary>What a click on the board does (LevelEditor's switches, read by HexTile).</summary>
    void SetFlags(Tool t) {
        LevelEditor le = LevelEditor.singleton;
        if (le == null) return;
        le.editing = t == Tool.Terrain && le.td != null;
        le.unit_placement = t == Tool.Units && le.ut != null;
        le.building_placement = t == Tool.Buildings && le.bt != null;
        le.mouseDown = false;
        ApplySide();
    }

    void ApplySide() {
        GameController gc = GameController.gc;
        if (gc == null || side == null) return;
        List<Player> ps = gc.players.Where(p => p != null).ToList();
        if (side.value >= 0 && side.value < ps.Count) gc.activePlayer = ps[side.value];
    }

    void ShowTool() {
        if (toolLabel == null) return;
        string what = pickedItem != null ? pickedItem.name : "";
        switch (tool) {
            case Tool.Terrain: toolLabel.text = "Painting " + what + " (click or drag on the board)"; break;
            case Tool.Units: toolLabel.text = "Placing " + what + " (click a unit again to remove it)"; break;
            case Tool.Buildings: toolLabel.text = "Placing " + what + " (click a building again to remove it)"; break;
            default: toolLabel.text = "Looking (pick a tool to edit)"; break;
        }
    }

    // ------------------------------------------------------------------ the scenario

    /// <summary>An empty board: every unit and building gone, every tile the terrain picked (else the first terrain).</summary>
    public void NewMap() {
        Clear();
        TerrainData td = LevelEditor.singleton != null && LevelEditor.singleton.td != null ? LevelEditor.singleton.td : GameAssets.AllTerrains.OrderBy(x => x.name).FirstOrDefault();
        if (td != null) HexGenerator.forAllTiles(t => { t.loadData(td); return 0; });
        loadedPath = null;
        GameController.BoardLoaded();
        ReadBoard();
        Say("A new map.");
    }

    static void Clear() {
        if (UnitController.uc != null) {
            foreach (Unit u in new List<Unit>(UnitController.uc.units)) if (u != null) u.Delete();
            UnitController.uc.units.Clear();
        }
        if (BuildingController.bc != null) BuildingController.bc.Clear();
    }

    /// <summary>Make the board the width typed (units and buildings are cleared first, as a load does).</summary>
    public void Resize() {
        int w;
        if (boardWidth == null || !int.TryParse(boardWidth.text, out w) || w < 3) { Say("The width is a number, 3 or more."); return; }
        if (HexGenerator.hg == null || w == HexGenerator.hg.width) return;
        Clear();
        HexGenerator.hg.SetWidth(w);
        GameController.BoardLoaded();
        GameSession.Frame();
        Say("The board is " + w + " wide (units and buildings cleared).");
    }

    /// <summary>
    /// Save the scenario: to the player's scenario folder under its name — or, in the editor with
    /// FrontEndSettings.editorSavesInPlace, back over the file it was loaded from (keeping that file's test block).
    /// </summary>
    public string Save() {
        string name = scenarioName != null ? GameFiles.SafeName(scenarioName.text) : "";
        if (name.Length == 0) { Say("Give the scenario a name."); return null; }
        bool inPlace = Application.isEditor && FrontEndSettings.Get.editorSavesInPlace && loadedPath != null && Path.GetFileNameWithoutExtension(loadedPath) == name;
        string path = inPlace ? loadedPath : GameFiles.UserScenarioPath(name);
        // Saved with the side that moves first as the side to move (not the side units were being placed for).
        GameController gc = GameController.gc;
        Player placing = gc != null ? gc.activePlayer : null;
        string json;
        try {
            if (gc != null && firstToMove != null && gc.players.Contains(firstToMove)) gc.activePlayer = firstToMove;
            json = LevelEditor.BoardJson();
        } finally {
            if (gc != null && placing != null) gc.activePlayer = placing;
        }
        if (File.Exists(path)) {
            JSONObject old = new JSONObject(File.ReadAllText(path));
            JSONObject test = old.GetField("test");
            if (test != null) { JSONObject now = new JSONObject(json); now.SetField("test", test); json = now.Print(true); }
        }
        File.WriteAllText(path, json);
        loadedPath = path;
        Say("Saved " + path);
        return path;
    }

    /// <summary>The Save button (Save returns where it saved, for Play).</summary>
    public void SaveButton() { Save(); }

    public void ShowLoad() { if (loadPanel != null) loadPanel.SetActive(true); if (loadList != null) loadList.ShowScenarios(); }
    public void HideLoad() { if (loadPanel != null) loadPanel.SetActive(false); }

    public void LoadPicked() {
        if (loadList == null || loadList.Selected == null) return;
        string path = loadList.Selected;
        HideLoad();
        if (!MatchSetup.LoadForEditing(path)) { Say("Could not load " + path); return; }
        GameController.Paused = true;
        loadedPath = path;
        MatchSetup.FilePath = path;
        GameSession.Frame();
        ReadBoard();
        SetTool(tool);
        Say("Loaded " + Path.GetFileNameWithoutExtension(path) + ".");
    }

    /// <summary>Save, then set the scenario up to play (the pre-game screen).</summary>
    public void PlayTest() {
        string path = Save();
        if (path != null) MatchSetup.SetUp(path);
    }

    public void Exit() { MatchSetup.ToMainMenu(); }

    void Say(string s) { if (status != null) status.text = s; }
}
