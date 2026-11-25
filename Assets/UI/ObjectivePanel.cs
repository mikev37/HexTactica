using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The mission's current objective on screen (a scenario's Set Objective action: ScenarioGame.ObjectiveHook), top left,
/// flashing for a moment when it changes; hidden while there is none. Its look is Resources/ObjectivePanel.prefab if
/// there is one (a TMP text named "Text" in it takes the objective), else a dark panel made here. Takes no clicks.
/// Installed in every game scene.
/// </summary>
public class ObjectivePanel : MonoBehaviour {

    public const string PrefabName = "ObjectivePanel";

    [Tooltip("Seconds it flashes when the objective changes.")]
    public float flashSeconds = 2.5f;

    GameObject shown;
    TMP_Text text;
    CanvasGroup group;
    float flashUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook() { GameScene.WhenLoaded(Install); }

    static void Install() {
        if (GameController.gc != null && GameController.gc.headless) return;
        if (FindAnyObjectByType<ObjectivePanel>() != null) return;
        new GameObject("Objective Panel").AddComponent<ObjectivePanel>();
    }

    void OnEnable() {
        ScenarioGame.ObjectiveHook = Set;
        ScenarioScript s = ScenarioScript.Instance;
        if (s != null) Set(s.Objective);
    }

    void OnDestroy() { if (ScenarioGame.ObjectiveHook == (System.Action<string>)Set) ScenarioGame.ObjectiveHook = null; }

    void Set(string objective) {
        if (string.IsNullOrEmpty(objective)) { if (shown != null) shown.SetActive(false); return; }
        if (shown == null) Make();
        shown.SetActive(true);
        if (text != null && text.text != objective) { text.text = objective; flashUntil = Time.unscaledTime + flashSeconds; }
    }

    void Update() {
        if (group == null) return;
        bool flashing = Time.unscaledTime < flashUntil;
        group.alpha = flashing ? Mathf.Lerp(0.35f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 4f)) : 1f;
    }

    void Make() {
        GameObject prefab = Resources.Load<GameObject>(PrefabName);
        GameObject canvasGo = new GameObject("ObjectiveCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        shown = prefab != null ? Instantiate(prefab, canvasGo.transform, false) : BuildDefault(canvasGo.transform);
        Transform t = shown.transform.Find("Text");
        text = t != null ? t.GetComponent<TMP_Text>() : shown.GetComponentInChildren<TMP_Text>(true);
        group = shown.GetComponent<CanvasGroup>() ?? shown.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;
    }

    /// <summary>The default look: a dark panel at the top left, "OBJECTIVE" in the marker yellow over the objective.</summary>
    public static GameObject BuildDefault(Transform parent) {
        GameObject g = new GameObject(PrefabName, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        RectTransform rt = (RectTransform)g.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(16f, -16f);
        rt.sizeDelta = new Vector2(440f, 96f);
        Image bg = g.GetComponent<Image>();
        bg.color = new Color(0.03f, 0.04f, 0.06f, 0.82f); bg.raycastTarget = false;
        Label(g.transform, "Header", "OBJECTIVE", 20f, UIHighlights.Yellow, new Vector2(0f, -8f), 26f, FontStyles.Bold);
        Label(g.transform, "Text", "", 24f, Color.white, new Vector2(0f, -36f), 52f, FontStyles.Normal);
        return g;
    }

    static void Label(Transform parent, string name, string s, float size, Color c, Vector2 at, float height, FontStyles style) {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f); r.pivot = new Vector2(0.5f, 1f);
        r.anchoredPosition = at; r.sizeDelta = new Vector2(-24f, height);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = s; t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = TextAlignmentOptions.TopLeft;
        t.raycastTarget = false;
    }
}
