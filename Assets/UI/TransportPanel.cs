using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The unload panel of a transport (Assets/Resources/TransportPanel.prefab; TransportMenu decides
/// when it shows). A grid of buttons, one per unit aboard, each showing that unit's picture and
/// strength. Clicking one asks TransportMenu to offer the tiles it can be put down on.
///
/// Edit the prefab freely: the panel only needs <see cref="grid"/> (where the buttons go) and
/// <see cref="slotTemplate"/> (a button copied for each unit; it may have a child Image named
/// "Icon" for the picture and a child Text named "Strength"). <see cref="title"/> is optional.
/// Without the prefab, the same panel is built in code (<see cref="Build"/>), and in the editor the
/// prefab is created from it the first time the project opens (Editor/TransportPanelPrefab).
/// </summary>
public class TransportPanel : MonoBehaviour {

    [Tooltip("Parent of the unit buttons (a GridLayoutGroup lays them out).")]
    public RectTransform grid;
    [Tooltip("Copied once per unit aboard. A child Image named \"Icon\" gets the unit's picture, a child Text named \"Strength\" its strength.")]
    public Button slotTemplate;
    [Tooltip("Optional: shows the transport's name.")]
    public Text title;
    [Tooltip("Optional: shows the transport's name (TextMeshPro).")]
    public TMP_Text titleText;
    [Tooltip("Tint of a unit's button.")]
    public Color slotColor = new Color(0.2f, 0.23f, 0.28f, 0.95f);
    [Tooltip("Tint of the button of the unit picked for unloading.")]
    public Color pickedColor = new Color(1f, 0.85f, 0.3f, 1f);

    public const string PrefabName = "TransportPanel";

    static TransportPanel instance;
    readonly List<Button> slots = new List<Button>();

    [RuntimeInitializeOnLoadMethod]
    static void Hook() {
        TransportMenu.PanelShow = Show;
        TransportMenu.PanelHide = Hide;
        TransportMenu.PointerOverUI = () => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    static TransportPanel Get() {
        if (instance != null) return instance;
        TransportPanel prefab = Resources.Load<TransportPanel>(PrefabName);
        instance = prefab != null ? GameObject.Instantiate(prefab) : Build();
        instance.name = PrefabName;
        if (instance.slotTemplate != null) instance.slotTemplate.gameObject.SetActive(false);
        EnsureEventSystem();
        return instance;
    }

    static void Show(Unit transport, Unit picked) { Get().Fill(transport, picked); }

    static void Hide() { if (instance != null) instance.gameObject.SetActive(false); }

    void Fill(Unit transport, Unit picked) {
        gameObject.SetActive(true);
        string name = transport.name.Replace("(Clone)", "").Trim();
        if (title != null) title.text = name;
        if (titleText != null) titleText.text = name;
        foreach (Button b in slots) if (b != null) Destroy(b.gameObject);
        slots.Clear();
        if (grid == null || slotTemplate == null) return;
        List<KeyValuePair<Unit, RectTransform>> pictures = new List<KeyValuePair<Unit, RectTransform>>();
        List<KeyValuePair<Unit, RectTransform>> pips = new List<KeyValuePair<Unit, RectTransform>>(), chevrons = new List<KeyValuePair<Unit, RectTransform>>();
        foreach (Unit c in transport.transported.ToArray()) {
            if (c == null) continue;
            Unit cargo = c;
            Button b = GameObject.Instantiate(slotTemplate, grid);
            b.gameObject.SetActive(true);
            b.name = "Slot " + cargo.name.Replace("(Clone)", "").Trim();
            Transform icon = b.transform.Find("Icon");
            if (icon != null) {
                Image own = icon.GetComponent<Image>();
                if (own != null) own.enabled = false;   // the picture is made of the unit's own sprites, below it
                pictures.Add(new KeyValuePair<Unit, RectTransform>(cargo, (RectTransform)icon));
            }
            // Strength as the unit's own pips and veterancy as its chevrons (drawn below, once laid
            // out); the number only for a unit without pips, or a slot without the places for them.
            Transform pipsAt = b.transform.Find("Pips"), chevAt = b.transform.Find("Chevrons");
            if (pipsAt != null) pips.Add(new KeyValuePair<Unit, RectTransform>(cargo, (RectTransform)pipsAt));
            if (chevAt != null) chevrons.Add(new KeyValuePair<Unit, RectTransform>(cargo, (RectTransform)chevAt));
            Transform st = b.transform.Find("Strength");
            if (st != null) {
                TMP_Text tmp = st.GetComponent<TMP_Text>();
                if (tmp != null) tmp.text = cargo.Strength.ToString();
                Text txt = st.GetComponent<Text>();
                if (txt != null) txt.text = cargo.Strength.ToString();
            }
            if (b.targetGraphic != null) b.targetGraphic.color = cargo == picked ? pickedColor : slotColor;
            Transform badge = b.transform.Find("Badge");
            if (badge != null && badge.GetComponent<Image>() != null && cargo.owner != null) badge.GetComponent<Image>().color = cargo.owner.displayColor;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => TransportMenu.PickCargo(cargo));
            slots.Add(b);
        }
        // Lay the grid out first, so each picture knows its box.
        Canvas.ForceUpdateCanvases();
        foreach (KeyValuePair<Unit, RectTransform> kv in pictures) {
            Unit u = kv.Key;
            SpritePortrait.Compose(u.transform, kv.Value, r => SpritePortrait.UnitPart(u, r));
        }
        foreach (KeyValuePair<Unit, RectTransform> kv in pips) {
            bool drawn = SpritePortrait.ComposePips(kv.Key, kv.Value);
            Transform st = kv.Value.parent != null ? kv.Value.parent.Find("Strength") : null;
            if (st != null) st.gameObject.SetActive(!drawn);
        }
        foreach (KeyValuePair<Unit, RectTransform> kv in chevrons) SpritePortrait.ComposeChevrons(kv.Key, kv.Value);
    }

    /// <summary>Make an EventSystem if the scene has none (uGUI buttons need one).</summary>
    public static void EnsureEventSystem() {
        if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    /// <summary>
    /// The panel as the prefab starts out: top left, in the game's flat style (Sprites/UI/Widgets,
    /// tinted): a dark rounded box with the transport's name and a four-column grid of buttons, each
    /// the unit's picture on its owner's hex with its strength. <paramref name="art"/> finds a sprite
    /// by file name there (the editor passes the asset database; null: plain shapes).
    /// </summary>
    public static TransportPanel Build(System.Func<string, Sprite> art = null) {
        System.Func<string, Sprite> A = file => art != null ? art(file) : null;
        GameObject root = new GameObject(PrefabName);
        root.AddComponent<RectTransform>();
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        TransportPanel panel = root.AddComponent<TransportPanel>();

        GameObject box = new GameObject("Panel");
        RectTransform boxRt = box.AddComponent<RectTransform>();
        boxRt.SetParent(root.transform, false);
        boxRt.anchorMin = new Vector2(0f, 1f); boxRt.anchorMax = new Vector2(0f, 1f); boxRt.pivot = new Vector2(0f, 1f);
        boxRt.anchoredPosition = new Vector2(16f, -16f);
        Image bg = box.AddComponent<Image>();
        bg.sprite = A("panel");
        if (bg.sprite != null) bg.type = Image.Type.Sliced;
        bg.color = new Color(0.09f, 0.11f, 0.14f, 0.88f);
        VerticalLayoutGroup v = box.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(16, 16, 12, 14);
        v.spacing = 8f;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = false; v.childForceExpandHeight = false;
        ContentSizeFitter fit = box.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject t = new GameObject("Title");
        RectTransform tRt = t.AddComponent<RectTransform>();
        tRt.SetParent(boxRt, false);
        TextMeshProUGUI title = t.AddComponent<TextMeshProUGUI>();
        title.fontSize = 24f; title.color = Color.white; title.text = "Transport";
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.raycastTarget = false;

        GameObject g = new GameObject("Grid");
        RectTransform gRt = g.AddComponent<RectTransform>();
        gRt.SetParent(boxRt, false);
        GridLayoutGroup grid = g.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(84f, 84f);
        grid.spacing = new Vector2(8f, 8f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;

        GameObject s = new GameObject("SlotTemplate");
        RectTransform sRt = s.AddComponent<RectTransform>();
        sRt.SetParent(root.transform, false);
        sRt.sizeDelta = new Vector2(84f, 84f);
        Image sBg = s.AddComponent<Image>();
        sBg.sprite = A("panel");
        if (sBg.sprite != null) sBg.type = Image.Type.Sliced;
        sBg.color = panel.slotColor;
        Button button = s.AddComponent<Button>();
        button.targetGraphic = sBg;

        GameObject bd = new GameObject("Badge");
        RectTransform bdRt = bd.AddComponent<RectTransform>();
        bdRt.SetParent(sRt, false);
        bdRt.anchorMin = Vector2.zero; bdRt.anchorMax = Vector2.one;
        bdRt.offsetMin = new Vector2(8f, 8f); bdRt.offsetMax = new Vector2(-8f, -8f);
        Image badge = bd.AddComponent<Image>();
        badge.sprite = A("hex"); badge.preserveAspect = true; badge.raycastTarget = false;
        badge.color = new Color(1f, 1f, 1f, 0.35f);

        GameObject i = new GameObject("Icon");
        RectTransform iRt = i.AddComponent<RectTransform>();
        iRt.SetParent(sRt, false);
        iRt.anchorMin = Vector2.zero; iRt.anchorMax = Vector2.one;
        iRt.offsetMin = new Vector2(10f, 10f); iRt.offsetMax = new Vector2(-10f, -10f);
        Image icon = i.AddComponent<Image>();
        icon.enabled = false; icon.raycastTarget = false;

        // The unit's strength pips along the bottom, its veterancy chevrons at the top right.
        GameObject pp = new GameObject("Pips");
        RectTransform ppRt = pp.AddComponent<RectTransform>();
        ppRt.SetParent(sRt, false);
        ppRt.anchorMin = new Vector2(0f, 0f); ppRt.anchorMax = new Vector2(1f, 0f); ppRt.pivot = new Vector2(0.5f, 0f);
        ppRt.offsetMin = new Vector2(6f, 4f); ppRt.offsetMax = new Vector2(-6f, 20f);
        GameObject cv = new GameObject("Chevrons");
        RectTransform cvRt = cv.AddComponent<RectTransform>();
        cvRt.SetParent(sRt, false);
        cvRt.anchorMin = new Vector2(1f, 1f); cvRt.anchorMax = new Vector2(1f, 1f); cvRt.pivot = new Vector2(1f, 1f);
        cvRt.anchoredPosition = new Vector2(-4f, -4f); cvRt.sizeDelta = new Vector2(22f, 26f);

        GameObject n = new GameObject("Strength");
        RectTransform nRt = n.AddComponent<RectTransform>();
        nRt.SetParent(sRt, false);
        nRt.anchorMin = new Vector2(1f, 0f); nRt.anchorMax = new Vector2(1f, 0f); nRt.pivot = new Vector2(1f, 0f);
        nRt.anchoredPosition = new Vector2(-6f, 4f); nRt.sizeDelta = new Vector2(30f, 24f);
        TextMeshProUGUI num = n.AddComponent<TextMeshProUGUI>();
        num.fontSize = 20f; num.color = Color.white; num.alignment = TextAlignmentOptions.MidlineRight;
        num.raycastTarget = false;

        s.SetActive(false);
        panel.grid = gRt;
        panel.slotTemplate = button;
        panel.titleText = title;
        return panel;
    }
}
