using System.Collections.Generic;
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
        if (title != null) title.text = transport.name.Replace("(Clone)", "").Trim();
        foreach (Button b in slots) if (b != null) Destroy(b.gameObject);
        slots.Clear();
        if (grid == null || slotTemplate == null) return;
        foreach (Unit c in transport.transported.ToArray()) {
            if (c == null) continue;
            Unit cargo = c;
            Button b = GameObject.Instantiate(slotTemplate, grid);
            b.gameObject.SetActive(true);
            b.name = "Slot " + cargo.name.Replace("(Clone)", "").Trim();
            Transform icon = b.transform.Find("Icon");
            Image img = icon != null ? icon.GetComponent<Image>() : null;
            if (img != null) { img.sprite = IconOf(cargo); img.preserveAspect = true; img.enabled = img.sprite != null; }
            Transform st = b.transform.Find("Strength");
            Text txt = st != null ? st.GetComponent<Text>() : null;
            if (txt != null) txt.text = cargo.Strength.ToString();
            if (cargo == picked && b.targetGraphic != null) b.targetGraphic.color = pickedColor;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => TransportMenu.PickCargo(cargo));
            slots.Add(b);
        }
    }

    /// <summary>The unit's picture: its largest sprite (shadows and its own UI left out).</summary>
    public static Sprite IconOf(Unit u) {
        Sprite best = null; float bestArea = 0f;
        foreach (SpriteRenderer r in u.GetComponentsInChildren<SpriteRenderer>(true)) {
            if (r == null || r.sprite == null) continue;
            if (r.gameObject.name.StartsWith("Shadow") || r.GetComponentInParent<Canvas>() != null) continue;
            Vector3 size = r.sprite.bounds.size;
            float area = size.x * size.y;
            if (area > bestArea) { best = r.sprite; bestArea = area; }
        }
        return best;
    }

    /// <summary>Make an EventSystem if the scene has none (uGUI buttons need one).</summary>
    public static void EnsureEventSystem() {
        if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    /// <summary>The panel as the prefab starts out: top-left, a dark box with the transport's name and a four-column grid of 72-pixel buttons.</summary>
    public static TransportPanel Build() {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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
        bg.color = new Color(0f, 0f, 0f, 0.65f);
        VerticalLayoutGroup v = box.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(10, 10, 8, 10);
        v.spacing = 6f;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = false; v.childForceExpandHeight = false;
        ContentSizeFitter fit = box.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject t = new GameObject("Title");
        RectTransform tRt = t.AddComponent<RectTransform>();
        tRt.SetParent(boxRt, false);
        Text title = t.AddComponent<Text>();
        title.font = font; title.fontSize = 20; title.color = Color.white; title.text = "Transport";
        title.raycastTarget = false;

        GameObject g = new GameObject("Grid");
        RectTransform gRt = g.AddComponent<RectTransform>();
        gRt.SetParent(boxRt, false);
        GridLayoutGroup grid = g.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(72f, 72f);
        grid.spacing = new Vector2(6f, 6f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;

        GameObject s = new GameObject("SlotTemplate");
        RectTransform sRt = s.AddComponent<RectTransform>();
        sRt.SetParent(root.transform, false);
        sRt.sizeDelta = new Vector2(72f, 72f);
        Image sBg = s.AddComponent<Image>();
        sBg.color = new Color(0.25f, 0.25f, 0.28f, 0.95f);
        Button button = s.AddComponent<Button>();
        button.targetGraphic = sBg;

        GameObject i = new GameObject("Icon");
        RectTransform iRt = i.AddComponent<RectTransform>();
        iRt.SetParent(sRt, false);
        iRt.anchorMin = Vector2.zero; iRt.anchorMax = Vector2.one;
        iRt.offsetMin = new Vector2(6f, 6f); iRt.offsetMax = new Vector2(-6f, -6f);
        Image icon = i.AddComponent<Image>();
        icon.preserveAspect = true; icon.raycastTarget = false;

        GameObject n = new GameObject("Strength");
        RectTransform nRt = n.AddComponent<RectTransform>();
        nRt.SetParent(sRt, false);
        nRt.anchorMin = new Vector2(1f, 0f); nRt.anchorMax = new Vector2(1f, 0f); nRt.pivot = new Vector2(1f, 0f);
        nRt.anchoredPosition = new Vector2(-4f, 2f); nRt.sizeDelta = new Vector2(30f, 22f);
        Text num = n.AddComponent<Text>();
        num.font = font; num.fontSize = 16; num.color = Color.white; num.alignment = TextAnchor.LowerRight;
        num.raycastTarget = false;

        s.SetActive(false);
        panel.grid = gRt;
        panel.slotTemplate = button;
        panel.title = title;
        return panel;
    }
}
