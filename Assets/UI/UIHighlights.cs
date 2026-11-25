using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A scenario's Highlight UI action (ScenarioGame.HighlightUIHook): a part of the screen outlined and flashing to draw the
/// player's eye ("the truck's panel: click the grunt in it"). The highlight is a copy of Resources/UIHighlight.prefab
/// (made the first time the project opens without one: HexTactica ▸ Scenario Triggers ▸ Recreate UI Highlight Prefab;
/// edit it as you like), laid over the element and stretched to it. It takes no clicks and changes no layout.
/// The element is found by its object's name, or by the name of a component on it (TransportPanel): if it is not on
/// screen yet (a panel that opens when a unit is selected), the highlight waits and appears with it.
/// Installed in every game scene.
/// </summary>
public class UIHighlights : MonoBehaviour {

    public const string PrefabName = "UIHighlight";

    /// <summary>The marker yellow (ScenarioMarkers' default marker).</summary>
    public static readonly Color Yellow = new Color(1f, 0.85f, 0.25f, 1f);

    class Wanted { public string what; public RectTransform on; public GameObject shown; }
    readonly Dictionary<string, Wanted> wanted = new Dictionary<string, Wanted>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook() { GameScene.WhenLoaded(Install); }

    static void Install() {
        if (GameController.gc != null && GameController.gc.headless) return;
        if (FindAnyObjectByType<UIHighlights>() != null) return;
        new GameObject("UI Highlights").AddComponent<UIHighlights>();
    }

    void OnEnable() { ScenarioGame.HighlightUIHook = Set; }

    void OnDestroy() {
        if (ScenarioGame.HighlightUIHook == (System.Action<string, string, bool>)Set) ScenarioGame.HighlightUIHook = null;
        Clear("");
    }

    void Set(string what, string id, bool on) {
        if (string.IsNullOrEmpty(id)) id = what ?? "";
        if (!on) { Clear(id); return; }
        Clear(id);
        wanted[id] = new Wanted { what = what };
        Update();
    }

    void Clear(string id) {
        List<string> gone = new List<string>();
        foreach (KeyValuePair<string, Wanted> kv in wanted) if (id.Length == 0 || kv.Key == id) gone.Add(kv.Key);
        foreach (string k in gone) { if (wanted[k].shown != null) Destroy(wanted[k].shown); wanted.Remove(k); }
    }

    void Update() {
        foreach (Wanted w in wanted.Values) {
            if (w.shown != null && w.on != null && w.on.gameObject.activeInHierarchy) continue;
            RectTransform target = Find(w.what);
            if (target == null) continue;
            if (w.shown != null) Destroy(w.shown);
            w.on = target;
            w.shown = Make(target);
        }
    }

    /// <summary>The element on screen named <paramref name="what"/>: an active object of that name, or one with a component of that type name.</summary>
    static RectTransform Find(string what) {
        if (string.IsNullOrEmpty(what)) return null;
        foreach (RectTransform r in FindObjectsByType<RectTransform>(FindObjectsSortMode.None)) {
            if (r == null || !r.gameObject.activeInHierarchy) continue;
            if (r.name == what || r.name == what + "(Clone)") return r;
            foreach (MonoBehaviour m in r.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name == what) return r;
        }
        return null;
    }

    static GameObject Make(RectTransform target) {
        GameObject prefab = Resources.Load<GameObject>(PrefabName);
        GameObject g = prefab != null ? Instantiate(prefab) : BuildDefault();
        g.name = PrefabName;
        RectTransform rt = g.GetComponent<RectTransform>();
        if (rt == null) rt = g.AddComponent<RectTransform>();
        rt.SetParent(target, false);
        rt.SetAsLastSibling();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
        // The prefab's own margin: its offsets as made (a default one stands 8 out all round).
        if (prefab == null) { rt.offsetMin = new Vector2(-8f, -8f); rt.offsetMax = new Vector2(8f, 8f); }
        else { RectTransform pr = prefab.GetComponent<RectTransform>(); if (pr != null) { rt.offsetMin = pr.offsetMin; rt.offsetMax = pr.offsetMax; } }
        LayoutElement le = g.GetComponent<LayoutElement>() ?? g.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        CanvasGroup cg = g.GetComponent<CanvasGroup>() ?? g.AddComponent<CanvasGroup>();
        cg.interactable = false; cg.blocksRaycasts = false;
        foreach (Graphic gr in g.GetComponentsInChildren<Graphic>(true)) gr.raycastTarget = false;
        return g;
    }

    /// <summary>
    /// The default highlight (what the prefab is made from): a yellow frame, four bars round the element over a faint
    /// yellow wash, pulsing (UIHighlightPulse). Takes no clicks.
    /// </summary>
    public static GameObject BuildDefault() {
        GameObject g = new GameObject(PrefabName, typeof(RectTransform), typeof(CanvasGroup), typeof(LayoutElement), typeof(UIHighlightPulse));
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-8f, -8f); rt.offsetMax = new Vector2(8f, 8f);
        g.GetComponent<LayoutElement>().ignoreLayout = true;
        CanvasGroup cg = g.GetComponent<CanvasGroup>();
        cg.interactable = false; cg.blocksRaycasts = false;
        Part(g.transform, "Wash", new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, new Color(Yellow.r, Yellow.g, Yellow.b, 0.15f));
        const float w = 6f;
        Part(g.transform, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -w), Vector2.zero, Yellow);
        Part(g.transform, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, w), Yellow);
        Part(g.transform, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(w, 0f), Yellow);
        Part(g.transform, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-w, 0f), Vector2.zero, Yellow);
        return g;
    }

    static void Part(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color c) {
        GameObject p = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform r = (RectTransform)p.transform;
        r.SetParent(parent, false);
        r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = oMin; r.offsetMax = oMax;
        Image i = p.GetComponent<Image>();
        i.color = c; i.raycastTarget = false;
    }
}

/// <summary>A highlight's flashing: its alpha (CanvasGroup) between Low and 1, Speed times a second; unscaled time, so it flashes under a held game too.</summary>
public class UIHighlightPulse : MonoBehaviour {
    [Range(0f, 1f)] public float low = 0.25f;
    public float speed = 1.5f;
    CanvasGroup group;

    void Awake() { group = GetComponent<CanvasGroup>(); }

    void Update() {
        if (group == null) return;
        float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed * Mathf.PI * 2f);
        group.alpha = Mathf.Lerp(low, 1f, k);
    }
}
