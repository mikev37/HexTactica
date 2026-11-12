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
/// from a palette of the terrain a map is painted with (TerrainData.inMapEditor) and of every unit (by faction) and
/// building. The map's sides: how many, their names, colours, and the faction the map makes each (or any). The
/// scenario's name, board width, fog and purchases; New, Load, Save, Play (the pre-game screen on it), Exit.
/// Palette entries and side rows are copies of their prefabs.
/// </summary>
public class MapEditorPanel : MonoBehaviour {
    public enum Tool { Look, Terrain, Units, Buildings }

    [Header("Tools")]
    public PaletteButton paletteTemplate;
    public RectTransform palette;
    [Tooltip("Names the tool in use and what is picked.")]
    public TMP_Text toolLabel;
    [Tooltip("The row with the side units and buildings are placed for: shown for the Units and Buildings tools.")]
    public GameObject sideRow;
    [Tooltip("The side units and buildings are placed for (and, for buildings, Neutral: owned by nobody).")]
    public TMP_Dropdown side;
    public string neutralLabel = "Neutral";
    [Tooltip("The palette (its scroll view): shown for the Terrain, Units and Buildings tools.")]
    public GameObject paletteView;
    [Tooltip("What the Look tool shows: the map's sides, name, width and rules.")]
    public GameObject lookPanel;
    [Tooltip("The Units tool's filter: base units, one faction's units, or all.")]
    public GameObject unitFilterRow;
    public TMP_Dropdown unitFilter;
    public string baseUnitsLabel = "Base units";
    public string allUnitsLabel = "All units";
    public Color paletteColor = new Color(1f, 1f, 1f, 0.08f);
    public Color pickedColor = new Color(0.36f, 0.62f, 0.95f, 0.55f);

    [Header("Sides")]
    public MapPlayerRow sideTemplate;
    public RectTransform sides;
    [Tooltip("The faction list's first entry: the side's faction is chosen on the pre-game screen.")]
    public string anyFaction = "Any (chosen before the game)";
    [Tooltip("The pop-up a side's colour is chosen from (clicking its swatch): one swatch per colour of the list below. None: the swatch steps to the next colour instead.")]
    public ColourMenu colourMenu;
    [Tooltip("The colours a side can have (the colour menu shows them all), and the colours a new side is given (the first not near any side's).")]
    public List<Color> extraSideColors = new List<Color> {
        new Color(0f, 0.5294118f, 1f, 1f),   // blue
        new Color(1f, 0f, 0f, 1f),           // red
        new Color(0.2f, 0.75f, 0.25f, 1f),   // green
        new Color(0.95f, 0.85f, 0.15f, 1f),  // yellow
        new Color(0.6f, 0.3f, 0.85f, 1f),    // purple
        new Color(1f, 0.55f, 0.1f, 1f),      // orange
        new Color(0f, 0.85f, 0.75f, 1f),     // teal
        new Color(1f, 0.45f, 0.75f, 1f),     // pink
        new Color(0.55f, 0.35f, 0.15f, 1f),  // brown
        new Color(0.85f, 0.85f, 0.85f, 1f),  // white
    };
    [Tooltip("How far apart two colours must be (red, green and blue differences added) to count as different.")]
    public float colourDistance = 0.6f;
    [Tooltip("A new side's name: {0} its number.")]
    public string newSideName = "Player {0}";
    [Tooltip("A map has at least this many sides.")]
    public int minSides = 2;

    [Header("Scenario")]
    public TMP_InputField scenarioName;
    public TMP_InputField boardWidth;
    [Tooltip("Older layout: fog on or off, which players may change. The rule lists below replace it.")]
    public Toggle fog;
    [Tooltip("Older layout: purchases on or off, which players may change.")]
    public Toggle build;
    [Tooltip("Fog of war: players choose (on or off to start with), or the map fixes it on or off.")]
    public TMP_Dropdown fogRule;
    [Tooltip("Purchases (building units): players choose, or the map fixes them.")]
    public TMP_Dropdown buildRule;
    [Tooltip("Faction special units: players choose, or the map fixes them.")]
    public TMP_Dropdown specialRule;
    [Tooltip("The rule lists' choices, in this order: players choose (starting on), players choose (starting off), always on, always off.")]
    public List<string> ruleChoices = new List<string> { "Players choose (on)", "Players choose (off)", "Always on", "Always off" };
    [Tooltip("What was just done (saved, loaded, or why not).")]
    public TMP_Text status;
    [Tooltip("The name a new map starts with.")]
    public string newName = "New map";

    [Header("Load")]
    public GameObject loadPanel;
    public FileList loadList;

    readonly List<PaletteButton> buttons = new List<PaletteButton>();
    readonly List<MapPlayerRow> sideRows = new List<MapPlayerRow>();
    /// <summary>Who moves first in the scenario (the side to move when it was loaded): the Side list only says whose units are placed.</summary>
    Player firstToMove;
    Tool tool = Tool.Look;
    Object pickedItem;
    string loadedPath;
    List<Faction> factions = new List<Faction>();

    void Awake() {
        if (paletteTemplate != null && paletteTemplate.gameObject.scene.IsValid()) paletteTemplate.gameObject.SetActive(false);
        if (sideTemplate != null && sideTemplate.gameObject.scene.IsValid()) sideTemplate.gameObject.SetActive(false);
        if (loadPanel != null) loadPanel.SetActive(false);
        TransportPanel.EnsureEventSystem();
        factions = GameAssets.AllFactions.Where(f => f != null).OrderBy(f => f.name).ToList();
        if (side != null) side.onValueChanged.AddListener(i => ApplySide());
        if (fog != null) fog.onValueChanged.AddListener(v => { if (GameController.gc != null) { GameController.gc.fogOfWar = v; FogOfWar.Reset(); FogOfWar.Changed(); FogOfWar.Refresh(); } });
        if (build != null) build.onValueChanged.AddListener(v => { if (GameController.gc != null) GameController.gc.purchasesAllowed = v; });
        Rule(fogRule, (gc, on, fixd) => { gc.fogOfWar = on; gc.fogFixed = fixd; FogOfWar.Reset(); FogOfWar.Changed(); FogOfWar.Refresh(); });
        Rule(buildRule, (gc, on, fixd) => { gc.purchasesAllowed = on; gc.purchasesFixed = fixd; });
        Rule(specialRule, (gc, on, fixd) => { gc.factionSpecialUnits = on; gc.specialUnitsFixed = fixd; });
        if (unitFilter != null) {
            unitFilter.ClearOptions();
            List<string> opts = new List<string> { baseUnitsLabel };
            opts.AddRange(factions.Select(f => f.name));
            opts.Add(allUnitsLabel);
            unitFilter.AddOptions(opts);
            unitFilter.onValueChanged.AddListener(i => { if (tool == Tool.Units) SetTool(Tool.Units); });
        }
    }

    void Start() {
        loadedPath = string.IsNullOrEmpty(MatchSetup.FilePath) ? null : MatchSetup.FilePath;
        ReadBoard();
        SetTool(Tool.Look);
    }

    void OnDestroy() { SetFlags(Tool.Look); Hud.Pinned = null; }

    /// <summary>The scenario's settings and sides as the board has them now (after a load).</summary>
    /// <summary>A rule list: its choices, and what picking one sets (on or off; fixed by the map or not).</summary>
    void Rule(TMP_Dropdown d, System.Action<GameController, bool, bool> set) {
        if (d == null) return;
        d.ClearOptions();
        d.AddOptions(ruleChoices);
        d.onValueChanged.AddListener(i => { if (GameController.gc != null) set(GameController.gc, i == 0 || i == 2, i >= 2); });
    }

    static int RuleChoice(bool on, bool fixd) { return fixd ? (on ? 2 : 3) : (on ? 0 : 1); }

    void ReadBoard() {
        GameController gc = GameController.gc;
        if (scenarioName != null) scenarioName.text = loadedPath != null ? Path.GetFileNameWithoutExtension(loadedPath) : newName;
        if (boardWidth != null && HexGenerator.hg != null) boardWidth.text = HexGenerator.hg.width.ToString();
        if (gc == null) return;
        firstToMove = gc.activePlayer;
        if (fog != null) fog.SetIsOnWithoutNotify(gc.fogOfWar);
        if (build != null) build.SetIsOnWithoutNotify(gc.purchasesAllowed);
        if (fogRule != null) fogRule.SetValueWithoutNotify(RuleChoice(gc.fogOfWar, gc.fogFixed));
        if (buildRule != null) buildRule.SetValueWithoutNotify(RuleChoice(gc.purchasesAllowed, gc.purchasesFixed));
        if (specialRule != null) specialRule.SetValueWithoutNotify(RuleChoice(gc.factionSpecialUnits, gc.specialUnitsFixed));
        ShowSides();
    }

    // ------------------------------------------------------------------ sides

    List<Player> Players { get { GameController gc = GameController.gc; return gc != null ? gc.players.Where(p => p != null).ToList() : new List<Player>(); } }

    /// <summary>The side rows and the Side list, from the board's players.</summary>
    void ShowSides() {
        foreach (MapPlayerRow r in sideRows) if (r != null) Destroy(r.gameObject);
        sideRows.Clear();
        List<Player> ps = Players;
        if (sideTemplate != null && sides != null) {
            List<string> names = new List<string> { anyFaction };
            names.AddRange(factions.Select(f => f.name));
            foreach (Player p in ps) {
                MapPlayerRow row = Instantiate(sideTemplate, sides);
                row.gameObject.SetActive(true);
                row.Player = p;
                Player me = p;
                if (row.sideName != null) { row.sideName.text = p.personal_name; row.sideName.onEndEdit.AddListener(t => Rename(me, t)); }
                if (row.swatch != null) row.swatch.color = Opaque(p.displayColor);
                if (row.colour != null) { Button swatchButton = row.colour; row.colour.onClick.AddListener(() => ChooseColour(me, swatchButton)); }
                if (row.faction != null) {
                    row.faction.ClearOptions();
                    row.faction.AddOptions(names);
                    int at = p.factionLocked && p.faction != null ? factions.IndexOf(p.faction) + 1 : 0;
                    row.faction.SetValueWithoutNotify(Mathf.Max(0, at));
                    row.faction.onValueChanged.AddListener(i => SetFaction(me, i));
                }
                if (row.remove != null) { row.remove.interactable = ps.Count > minSides; row.remove.onClick.AddListener(() => RemoveSide(me)); }
                sideRows.Add(row);
            }
        }
        FillSideList();
    }

    /// <summary>The For list: every side, and Neutral for the Buildings tool.</summary>
    void FillSideList() {
        if (side == null) return;
        List<Player> ps = Players;
        int was = side.value;
        side.ClearOptions();
        List<string> names = ps.Select(p => p.personal_name).ToList();
        if (tool == Tool.Buildings) names.Add(neutralLabel);
        side.AddOptions(names);
        GameController gc = GameController.gc;
        LevelEditor le = LevelEditor.singleton;
        int at = tool == Tool.Buildings && le != null && le.placeNeutral ? ps.Count : Mathf.Max(0, gc != null ? ps.IndexOf(gc.activePlayer) : 0);
        side.SetValueWithoutNotify(Mathf.Clamp(at, 0, names.Count - 1));
    }

    static Color Opaque(Color c) { c.a = 1f; return c; }

    void Rename(Player p, string name) {
        name = (name ?? "").Trim();
        if (name.Length == 0 || Players.Exists(x => x != p && x.personal_name == name)) { ShowSides(); Say("Each side needs a name of its own."); return; }
        p.personal_name = name;
        ShowSides();
    }

    ColourMenu openColours;

    /// <summary>
    /// The colour menu, opened at the side's swatch: the side takes the colour clicked. A colour another side has is
    /// swapped with it (that side takes this one's old colour), so any side can be given any colour.
    /// </summary>
    void ChooseColour(Player p, Button from) {
        if (colourMenu == null) { NextColour(p); return; }
        if (openColours != null) openColours.Close();
        Canvas canvas = GetComponentInParent<Canvas>();
        Transform under = canvas != null ? canvas.transform : transform;
        openColours = Instantiate(colourMenu, under);
        openColours.transform.SetAsLastSibling();
        openColours.Show(extraSideColors, p.displayColor, from != null ? from.transform.position : transform.position, c => {
            Color old = Opaque(p.displayColor);
            foreach (Player x in Players) if (x != p && Same(x.displayColor, c)) x.displayColor = old;
            p.displayColor = Opaque(c);
            ShowSides();
        });
    }

    static bool Same(Color a, Color b) { return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f; }

    /// <summary>The next colour of the list that no other side is near (units and buildings take it at once: ColorObjectUnit).</summary>
    void NextColour(Player p) {
        if (extraSideColors.Count == 0) return;
        // Where it is in the list: the colour it has, not the first one near it (yellow is near orange: from orange
        // the step went back to the colour after yellow, and the swatch went round two or three colours).
        int i = -1; float best = 0.02f;
        for (int k = 0; k < extraSideColors.Count; k++) {
            Color c = extraSideColors[k];
            float d = Mathf.Abs(c.r - p.displayColor.r) + Mathf.Abs(c.g - p.displayColor.g) + Mathf.Abs(c.b - p.displayColor.b);
            if (d < best) { best = d; i = k; }
        }
        for (int step = 1; step <= extraSideColors.Count; step++) {
            Color c = extraSideColors[(i + step + extraSideColors.Count) % extraSideColors.Count];
            if (Players.Exists(x => x != p && Near(x.displayColor, c))) continue;
            p.displayColor = Opaque(c);
            break;
        }
        ShowSides();
    }

    bool Near(Color a, Color b) { return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < colourDistance; }

    /// <summary>Option 0: any faction (chosen before the game); else the map makes the side that faction.</summary>
    void SetFaction(Player p, int option) {
        Faction f = option > 0 && option <= factions.Count ? factions[option - 1] : null;
        p.faction = f;
        p.factionLocked = f != null;
    }

    /// <summary>A new side, with a colour and a name no side has.</summary>
    public void AddSide() {
        GameController gc = GameController.gc;
        if (gc == null) return;
        List<Player> ps = Players;
        long id = ps.Count > 0 ? ps.Max(p => p.id) + 1 : 0;
        int n = ps.Count + 1;
        while (ps.Exists(p => p.personal_name == string.Format(newSideName, n))) n++;
        Player made = new GameObject(string.Format(newSideName, n) + " (player)").AddComponent<Player>();
        made.id = id;
        made.personal_name = string.Format(newSideName, n);
        Color free = extraSideColors.FirstOrDefault(c => !ps.Exists(p => Near(p.displayColor, c)));
        made.displayColor = free.a > 0f ? free : Color.white;
        made.money = ps.Count > 0 ? ps[0].money : 0;
        gc.players.Add(made);
        HexGenerator.forAllTiles(t => { t.EnsureSide(made); return 0; });
        ShowSides();
        Say("Added " + made.personal_name + ": give it an HQ.");
    }

    /// <summary>A side gone: its units removed, its buildings neutral.</summary>
    void RemoveSide(Player p) {
        GameController gc = GameController.gc;
        if (gc == null || Players.Count <= minSides) return;
        
        Unit.Wiping = true;
        try { foreach (Unit u in new List<Unit>(p.units)) if (u != null) u.Delete(); }
        finally { Unit.Wiping = false; }
        foreach (Building b in Building.all) if (b != null && b.owner == p) b.owner = null;
        gc.players.Remove(p);
        if (gc.activePlayer == p) gc.activePlayer = gc.players.FirstOrDefault(x => x != null);
        if (firstToMove == p) firstToMove = gc.activePlayer;
        Destroy(p.gameObject);
        ShowSides();
        ApplySide();
        Say("Removed " + p.personal_name + ".");
    }

    // ------------------------------------------------------------------ tools

    public void ToolLook() { SetTool(Tool.Look); }
    public void ToolTerrain() { SetTool(Tool.Terrain); }
    public void ToolUnits() { SetTool(Tool.Units); }
    public void ToolBuildings() { SetTool(Tool.Buildings); }

    void SetTool(Tool t) {
        tool = t;
        pickedItem = null;
        if (sideRow != null) sideRow.SetActive(t == Tool.Units || t == Tool.Buildings);
        if (unitFilterRow != null) unitFilterRow.SetActive(t == Tool.Units);
        if (paletteView != null) paletteView.SetActive(t != Tool.Look);
        if (lookPanel != null) lookPanel.SetActive(t == Tool.Look);
        if (t != Tool.Buildings && LevelEditor.singleton != null) LevelEditor.singleton.placeNeutral = false;
        FillSideList();
        FillPalette();
        if (buttons.Count > 0) Pick(buttons[0]);
        else { Hud.Pinned = null; SetFlags(tool); ShowTool(); }
    }

    /// <summary>The units the filter picks: base units, a faction's, or all.</summary>
    IEnumerable<Unit> FilteredUnits() {
        int f = unitFilter != null ? unitFilter.value : 0;
        if (f == 0) return UnitCatalog.BaseUnits();
        if (f >= 1 && f <= factions.Count) return UnitCatalog.FactionUnits(factions[f - 1]);
        return GameAssets.AllUnits.Where(u => u != null).OrderBy(UnitCatalog.DisplayName);
    }

    void FillPalette() {
        foreach (PaletteButton b in buttons) if (b != null) Destroy(b.gameObject);
        buttons.Clear();
        if (paletteTemplate == null || palette == null) return;
        IEnumerable<Object> items =
            tool == Tool.Terrain ? UnitCatalog.PaintableTerrain().Cast<Object>() :
            tool == Tool.Units ? FilteredUnits().Cast<Object>() :
            tool == Tool.Buildings ? GameAssets.AllBuildings.OrderBy(x => x.name).Cast<Object>() :
            Enumerable.Empty<Object>();
        foreach (Object o in items) {
            PaletteButton b = Instantiate(paletteTemplate, palette);
            b.gameObject.SetActive(true);
            Unit u = o as Unit;
            Faction f = u != null ? UnitCatalog.FactionOf(u) : null;
            b.Show(o, u != null ? UnitCatalog.DisplayName(u) + (f != null && (unitFilter == null || unitFilter.value > factions.Count) ? " (" + f.name + ")" : "") : o.name, paletteColor);
            PaletteButton me = b;
            if (b.button != null) b.button.onClick.AddListener(() => Pick(me));
            buttons.Add(b);
        }
        // Pictures are fitted to their boxes' real size: lay the palette out first.
        LayoutRebuilder.ForceRebuildLayoutImmediate(palette);
        DrawPalette(true);
    }

    /// <summary>The palette's pictures, units and buildings in the colour of the side they are placed for.</summary>
    void DrawPalette(bool always = false) {
        Color? owner = SideColour();
        if (!always && owner == drawnFor) return;
        drawnFor = owner;
        foreach (PaletteButton b in buttons) if (b != null) b.Draw(owner);
    }
    Color? drawnFor;

    /// <summary>The colour of the side being placed for (null for Neutral: a building's neutral colour).</summary>
    Color? SideColour() {
        GameController gc = GameController.gc;
        LevelEditor le = LevelEditor.singleton;
        if (gc == null || gc.activePlayer == null || (le != null && le.placeNeutral)) return null;
        Color c = gc.activePlayer.displayColor;
        return new Color(c.r, c.g, c.b, 1f);
    }

    void Pick(PaletteButton b) {
        pickedItem = b != null ? b.Item : null;
        foreach (PaletteButton x in buttons) if (x != null) x.Highlight(x == b ? pickedColor : paletteColor);
        LevelEditor le = LevelEditor.singleton;
        if (le != null) {
            if (pickedItem is TerrainData) le.td = (TerrainData)pickedItem;
            if (pickedItem is Building) le.bt = (Building)pickedItem;
        }
        Hud.Pinned = pickedItem;   // the HUD's panels show what is picked
        SetFlags(tool);
        ShowTool();
    }

    /// <summary>
    /// The unit placed for the side: a base unit picked for a side with a faction is that faction's version of it
    /// (a Conscript for a Union side is the Union Conscript); a faction's own unit, as picked.
    /// </summary>
    Unit ForSide(Unit picked) {
        GameController gc = GameController.gc;
        Faction f = gc != null && gc.activePlayer != null ? gc.activePlayer.faction : null;
        if (picked == null || f == null || f.replacements == null || UnitCatalog.FactionOf(picked) != null) return picked;
        foreach (Faction.Replacement r in f.replacements) if (r != null && r.original == picked && r.replacement != null) return r.replacement;
        return picked;
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

    /// <summary>The For list's pick: the side placed for (the game's active side), or neutral for buildings; and the unit for that side.</summary>
    void ApplySide() {
        GameController gc = GameController.gc;
        LevelEditor le = LevelEditor.singleton;
        if (gc == null || side == null) return;
        List<Player> ps = Players;
        if (side.value >= 0 && side.value < ps.Count) gc.activePlayer = ps[side.value];
        if (le != null) {
            le.placeNeutral = tool == Tool.Buildings && side.value == ps.Count;
            if (pickedItem is Unit) le.ut = ForSide((Unit)pickedItem);
        }
        if (tool == Tool.Units || tool == Tool.Buildings) DrawPalette();
    }

    void ShowTool() {
        if (toolLabel == null) return;
        Unit u = pickedItem as Unit;
        string what = u != null ? UnitCatalog.DisplayName(u) : pickedItem != null ? pickedItem.name : "";
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
        TerrainData td = LevelEditor.singleton != null && LevelEditor.singleton.td != null ? LevelEditor.singleton.td : UnitCatalog.PaintableTerrain().FirstOrDefault();
        if (td != null) HexGenerator.forAllTiles(t => { t.loadData(td); return 0; });
        loadedPath = null;
        GameController.BoardLoaded();
        ReadBoard();
        Say("A new map.");
    }

    static void Clear() {
        if (UnitController.uc != null) {
            Unit.Wiping = true;   // taken off, not killed (no deaths played, no victory check)
            try { foreach (Unit u in new List<Unit>(UnitController.uc.units)) if (u != null) u.Delete(); }
            finally { Unit.Wiping = false; }
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

    /// <summary>The Save button (Save returns where it saved, for Play).</summary>
    public void SaveButton() { Save(); }

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
