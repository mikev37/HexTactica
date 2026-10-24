using System;
using TMPro;
using UnityEngine.Serialization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The two info panels (Assets/Resources/Hud.prefab): bottom right, the unit on the tile last
/// clicked (its picture, strength, fuel, ammunition, supplies, suppression, and — expanded with the
/// More button — every combat and movement attribute it has that is not 0); bottom left, that tile:
/// the building on it if there is one (its picture, name and owner), else the terrain (its picture
/// and name), with cover and move cost. Values with a small cap
/// (<see cref="bulletLimit"/>) are bullets, green when good and red when bad; others are numbers.
///
/// The prefab is built from the flat white art in Sprites/UI/Widgets, tinted here; restyle it as you
/// like — the panel finds its rows by the fields below, and a row left unset is simply not shown.
/// It is made the first time the editor opens without it (Editor/HudPrefab), and the game spawns it
/// when a scene starts (or builds the same panels in code if there is no prefab).
/// </summary>
public class Hud : MonoBehaviour {

    [Header("Unit panel (bottom right)")]
    public GameObject unitPanel;
    public Image unitPortrait;
    [Tooltip("Behind the portrait, in the owner's colour.")]
    public Image unitBadge;
    public TMP_Text unitName;
    public HudRow strength, fuel, ammo, supplies, suppressed;

    [Header("Unit details (expanded with the More button)")]
    [Tooltip("Shown and hidden by the button; holds the rows below.")]
    public GameObject details;
    public Button detailsButton;
    public TMP_Text detailsButtonText;
    [Tooltip("Whether the details start open.")]
    public bool detailsOpen;
    public HudRow firepower, evasion, targeting, armor, piercing, concealment, moveRange, terrain, elevationMin, elevationMax;

    [Header("Tile panel (bottom left)")]
    public GameObject tilePanel;
    public Image tilePicture;
    public TMP_Text tileName;
    public HudRow cover, moveCost;
    [Tooltip("Not used any more (a road shows in the move cost): hidden if the prefab still has it.")]
    [HideInInspector] public HudRow road;
    [Tooltip("A building's owner (shown only for a building).")]
    [FormerlySerializedAs("building")] public HudRow owner;
    [Tooltip("Not used any more (the height of the ground): hidden if the prefab still has it.")]
    [HideInInspector] public HudRow elevation;

    [Header("Look")]
    [Tooltip("Values whose cap is below this are drawn as bullets; the rest as numbers.")]
    public int bulletLimit = 6;
    public Color positive = new Color(0.36f, 0.82f, 0.36f, 1f);
    public Color negative = new Color(0.92f, 0.32f, 0.3f, 1f);
    public Color empty = new Color(1f, 1f, 1f, 0.22f);
    public Color text = Color.white;
    public Sprite bulletFull, bulletEmpty;

    public const string PrefabName = "Hud";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn() {
        if (GameController.gc != null && GameController.gc.headless) return;
        if (UnityEngine.Object.FindAnyObjectByType<Hud>() != null) return;
        Hud prefab = Resources.Load<Hud>(PrefabName);
        Hud h = prefab != null ? GameObject.Instantiate(prefab) : Build(null);
        h.name = PrefabName;
        TransportPanel.EnsureEventSystem();
    }

    [Tooltip("Seconds between redraws of the pictures while the same unit or tile is shown (it may turn, load cargo or change terrain).")]
    public float pictureRefresh = 0.5f;
    UnityEngine.Object shownUnit, shownTile;
    float unitDrawn, tileDrawn;

    /// <summary>Redraw a picture when it shows something else, or every pictureRefresh seconds.</summary>
    bool Due(UnityEngine.Object o, ref UnityEngine.Object shown, ref float drawn) {
        if (o == shown && Time.unscaledTime - drawn < pictureRefresh) return false;
        shown = o; drawn = Time.unscaledTime;
        return true;
    }

    bool hooked;

    void Update() {
        if (!hooked && detailsButton != null) { hooked = true; detailsButton.onClick.AddListener(ToggleDetails); }
        if (details != null && details.activeSelf != detailsOpen) details.SetActive(detailsOpen);
        if (detailsButtonText != null) detailsButtonText.text = detailsOpen ? "- Less" : "+ More";
        HexTile t = HexTile.lastClicked;
        // Under fog of war, only a unit the viewer sees (one it does not is not there for the HUD).
        Unit u = t != null ? FogOfWar.ApparentOccupier(t, FogOfWar.Viewer) : null;
        ShowUnit(u);
        ShowTile(t);
    }

    void ShowUnit(Unit u) {
        if (unitPanel != null && unitPanel.activeSelf != (u != null)) unitPanel.SetActive(u != null);
        if (u == null) return;
        if (unitPortrait != null && Due(u, ref shownUnit, ref unitDrawn)) {
            unitPortrait.enabled = false;   // the picture is the unit's own sprites, composed under it
            SpritePortrait.Compose(u.transform, unitPortrait.rectTransform, r => SpritePortrait.UnitPart(u, r));
        }
        if (unitBadge != null) unitBadge.color = u.owner != null ? u.owner.displayColor : empty;
        if (unitName != null) unitName.text = u.name.Replace("(Clone)", "").Trim();
        if (strength != null) strength.Set(u.Strength, 6, false, this);
        if (fuel != null) { if (u.fuelMax > 0) fuel.Set(u.fuel, u.fuelMax, false, this); else fuel.Show(false); }
        if (ammo != null) { if (u.ammoMax > 0) ammo.Set(u.ammo, u.ammoMax, false, this); else ammo.Show(false); }
        if (supplies != null) { if (u.suppliesMax > 0) supplies.Set(u.supplies, u.suppliesMax, false, this); else supplies.Show(false); }
        if (suppressed != null) { if (u.suppressed > 0) suppressed.Set(-u.suppressed, Mathf.Max(u.Strength, 1), false, this); else suppressed.Show(false); }
        if (detailsOpen) {
            Attr(firepower, u.Firepower);
            Attr(evasion, u.Evasion);
            Attr(targeting, u.Targeting);
            Attr(armor, u.Armor);
            Attr(piercing, u.Piercing);
            Attr(concealment, u.Concealment);
            Attr(moveRange, u.moveRange);
            Attr(terrain, u.terrainScramble);
            Attr(elevationMin, u.elevationMin);
            Attr(elevationMax, u.elevationMax);
        }
    }

    /// <summary>An attribute's row: its value, or hidden when it is 0.</summary>
    void Attr(HudRow row, int v) {
        if (row == null) return;
        if (v == 0) row.Show(false); else row.SetText(v.ToString(), text);
    }

    /// <summary>The More / Less button.</summary>
    public void ToggleDetails() { detailsOpen = !detailsOpen; }

    void ShowTile(HexTile t) {
        bool on = t != null && t.terrain != null;
        if (tilePanel != null && tilePanel.activeSelf != on) tilePanel.SetActive(on);
        if (!on) return;
        TerrainData td = t.terrain;
        Building b = t.building;
        if (tilePicture != null && Due(b != null ? (UnityEngine.Object)b : t, ref shownTile, ref tileDrawn)) {
            // The picture: the building's own sprites, or the tile's ground, water and roads.
            tilePicture.enabled = false;
            if (b != null) SpritePortrait.Compose(b.transform, tilePicture.rectTransform, r => SpritePortrait.BuildingPart(b, r));
            else SpritePortrait.Compose(t.transform, tilePicture.rectTransform, r => SpritePortrait.TilePart(t, r));
        }
        if (tileName != null) tileName.text = b != null ? BuildingName(b) : TerrainName(td);
        if (cover != null) cover.Set(t.EffectiveCover, 3, false, this);
        if (moveCost != null) { if (td.elevation >= 0) moveCost.Set(t.EffectiveMoveability, 5, true, this); else moveCost.Show(false); }
        if (elevation != null) elevation.Show(false);
        if (road != null) road.Show(false);
        if (owner != null) {
            Player bo = b != null ? FogOfWar.ShownOwner(b) : null;   // under fog of war, as last seen
            if (b != null) owner.SetText(bo != null ? bo.personal_name : "Neutral", bo != null ? bo.displayColor : text);
            else owner.Show(false);
        }
    }

    /// <summary>The terrain's name: its asset's name (Grassland, Forest...).</summary>
    public static string TerrainName(TerrainData td) {
        return td != null ? td.name.Replace("(Clone)", "").Trim() : "";
    }

    /// <summary>A building's name: its own (personal_name), else its prefab's (City, Airport...).</summary>
    public static string BuildingName(Building b) {
        if (b == null) return "";
        return !string.IsNullOrEmpty(b.personal_name) ? b.personal_name : b.name.Replace("(Clone)", "").Trim();
    }

    // ------------------------------------------------------------------ the default prefab

    /// <summary>
    /// The panels as the prefab starts out. <paramref name="art"/> finds a sprite by file name in
    /// Sprites/UI/Widgets (the editor passes the asset database; null: plain shapes).
    /// </summary>
    public static Hud Build(Func<string, Sprite> art) {
        Func<string, Sprite> A = n => art != null ? art(n) : null;
        GameObject root = new GameObject(PrefabName);
        root.AddComponent<RectTransform>();
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        Hud h = root.AddComponent<Hud>();
        h.bulletFull = A("bullet");
        h.bulletEmpty = A("bullet_empty");

        // Bottom right: the unit.
        RectTransform up = Panel(root.transform, "UnitPanel", new Vector2(1f, 0f), new Vector2(-16f, 16f), A("panel"));
        h.unitPanel = up.gameObject;
        RectTransform head = Line(up, "Header", 10f);
        Image badge = Img(head, "Badge", A("hex"), 64f, 64f, Color.white);
        h.unitBadge = badge;
        Image portrait = Img(badge.rectTransform, "Portrait", null, 0f, 0f, Color.white);
        Stretch(portrait.rectTransform, 6f);
        h.unitPortrait = portrait;
        h.unitName = Txt(head, "Name", 26f, 180f);
        h.strength = Row(up, "Strength", A("icon_strength"), "Strength", h);
        h.fuel = Row(up, "Fuel", A("icon_fuel"), "Fuel", h);
        h.ammo = Row(up, "Ammo", A("icon_ammo"), "Ammo", h);
        h.supplies = Row(up, "Supplies", A("icon_supplies"), "Supplies", h);
        h.suppressed = Row(up, "Suppressed", A("icon_suppressed"), "Suppressed", h);
        // More / Less: every attribute that is not 0.
        RectTransform more = Line(up, "More", 8f);
        Image mbg = Img(more, "Button", A("panel"), 110f, 30f, new Color(0.22f, 0.26f, 0.32f, 1f));
        if (mbg.sprite != null) { mbg.type = Image.Type.Sliced; mbg.preserveAspect = false; }
        mbg.raycastTarget = true;
        h.detailsButton = mbg.gameObject.AddComponent<Button>();
        h.detailsButton.targetGraphic = mbg;
        h.detailsButtonText = Txt(mbg.rectTransform, "Text", 18f, 0f);
        Stretch(h.detailsButtonText.rectTransform, 4f);
        h.detailsButtonText.alignment = TextAlignmentOptions.Center;
        h.detailsButtonText.text = "+ More";
        GameObject d = new GameObject("Details");
        RectTransform drt = d.AddComponent<RectTransform>();
        drt.SetParent(up, false);
        VerticalLayoutGroup dv = d.AddComponent<VerticalLayoutGroup>();
        dv.spacing = 6f; dv.childControlWidth = true; dv.childControlHeight = true; dv.childForceExpandWidth = false; dv.childForceExpandHeight = false;
        h.details = d;
        h.firepower = Row(drt, "Firepower", A("icon_firepower"), "Firepower", h);
        h.evasion = Row(drt, "Evasion", A("icon_evasion"), "Evasion", h);
        h.targeting = Row(drt, "Targeting", A("icon_targeting"), "Targeting", h);
        h.armor = Row(drt, "Armor", A("icon_armor"), "Armor", h);
        h.piercing = Row(drt, "Piercing", A("icon_piercing"), "Piercing", h);
        h.concealment = Row(drt, "Concealment", A("icon_concealment"), "Concealment", h);
        h.moveRange = Row(drt, "MoveRange", A("icon_moverange"), "Move range", h);
        h.terrain = Row(drt, "Terrain", A("icon_terrain"), "Terrain", h);
        h.elevationMin = Row(drt, "ElevationMin", A("icon_elevmin"), "Lowest ground", h);
        h.elevationMax = Row(drt, "ElevationMax", A("icon_elevmax"), "Highest ground", h);
        d.SetActive(false);

        // Bottom left: the tile.
        RectTransform tp = Panel(root.transform, "TilePanel", new Vector2(0f, 0f), new Vector2(16f, 16f), A("panel"));
        h.tilePanel = tp.gameObject;
        RectTransform th = Line(tp, "Header", 10f);
        h.tilePicture = Img(th, "Terrain", null, 64f, 64f, Color.white);
        h.tileName = Txt(th, "Name", 26f, 180f);
        h.cover = Row(tp, "Cover", A("icon_cover"), "Cover", h);
        h.moveCost = Row(tp, "MoveCost", A("icon_move"), "Move cost", h);
        h.owner = Row(tp, "Owner", A("icon_building"), "Owner", h);
        return h;
    }

    static RectTransform Panel(Transform parent, string name, Vector2 corner, Vector2 offset, Sprite bg) {
        GameObject g = new GameObject(name);
        RectTransform rt = g.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = corner; rt.anchorMax = corner; rt.pivot = corner;
        rt.anchoredPosition = offset;
        Image img = g.AddComponent<Image>();
        img.sprite = bg;
        if (bg != null) img.type = Image.Type.Sliced;
        img.color = new Color(0.09f, 0.11f, 0.14f, 0.88f);
        VerticalLayoutGroup v = g.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(16, 16, 12, 14);
        v.spacing = 6f;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = false; v.childForceExpandHeight = false;
        ContentSizeFitter fit = g.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    static RectTransform Line(Transform parent, string name, float spacing) {
        GameObject g = new GameObject(name);
        RectTransform rt = g.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        HorizontalLayoutGroup hl = g.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = spacing;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        return rt;
    }

    static Image Img(Transform parent, string name, Sprite s, float w, float h, Color c) {
        GameObject g = new GameObject(name);
        RectTransform rt = g.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        Image img = g.AddComponent<Image>();
        img.sprite = s; img.color = c; img.preserveAspect = true; img.raycastTarget = false;
        if (w > 0f) { LayoutElement le = g.AddComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = h; le.minWidth = w; le.minHeight = h; }
        return img;
    }

    static void Stretch(RectTransform rt, float inset) {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
    }

    static TMP_Text Txt(Transform parent, string name, float size, float minWidth) {
        GameObject g = new GameObject(name);
        RectTransform rt = g.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = Color.white; t.alignment = TextAlignmentOptions.MidlineLeft; t.raycastTarget = false;
        t.text = "";
        if (minWidth > 0f) { LayoutElement le = g.AddComponent<LayoutElement>(); le.minWidth = minWidth; }
        return t;
    }

    static HudRow Row(Transform parent, string name, Sprite icon, string label, Hud h) {
        RectTransform line = Line(parent, name, 8f);
        HudRow row = line.gameObject.AddComponent<HudRow>();
        row.icon = Img(line, "Icon", icon, 30f, 30f, new Color(0.85f, 0.88f, 0.92f, 1f));
        row.label = Txt(line, "Label", 20f, 110f);
        row.label.text = label;
        row.label.color = new Color(0.8f, 0.83f, 0.88f, 1f);
        RectTransform b = Line(line, "Bullets", 4f);
        row.bullets = b;
        row.bulletTemplate = Img(b, "Bullet", h.bulletFull, 18f, 18f, Color.white);
        row.bulletTemplate.gameObject.SetActive(false);
        row.value = Txt(line, "Value", 22f, 0f);
        return row;
    }
}
