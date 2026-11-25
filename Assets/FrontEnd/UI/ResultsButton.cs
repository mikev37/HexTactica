using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Results button over a finished game's board (after View Board): it opens the post-game screen again, to go on
/// from there (a campaign mission's Return to the campaign). Made in code, in the game's flat style, where the turn controls were.
/// </summary>
public class ResultsButton : MonoBehaviour {
    static ResultsButton shown;
    System.Action onClick;

    TurnControls replaced;

    /// <summary>
    /// Shown in the place of the HUD's turn controls (Next unit, End turn: nothing to do in a finished game), which are
    /// hidden while it is; top centre of the screen if the HUD has none.
    /// </summary>
    public static void Show(System.Action onClick) {
        if (shown != null) { shown.onClick = onClick; return; }
        TransportPanel.EnsureEventSystem();
        TurnControls tc = Object.FindObjectOfType<TurnControls>();
        RectTransform at = tc != null ? tc.transform as RectTransform : null;
        GameObject b = new GameObject("ResultsButton");
        RectTransform rt = b.AddComponent<RectTransform>();
        if (at != null) {
            rt.SetParent(at.parent, false);
            rt.SetSiblingIndex(at.GetSiblingIndex() + 1);
            rt.anchorMin = at.anchorMin; rt.anchorMax = at.anchorMax; rt.pivot = at.pivot;
            rt.anchoredPosition = at.anchoredPosition; rt.sizeDelta = at.sizeDelta;
            rt.localScale = at.localScale;
            tc.gameObject.SetActive(false);
        } else {
            GameObject root = new GameObject("ResultsButtonCanvas");
            root.AddComponent<RectTransform>();
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            rt.SetParent(root.transform, false);
            rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -16f);
            rt.sizeDelta = new Vector2(240f, 56f);
        }
        shown = b.AddComponent<ResultsButton>();
        shown.onClick = onClick;
        shown.replaced = at != null ? tc : null;
        Image bg = b.AddComponent<Image>();
        bg.color = new Color(0.09f, 0.11f, 0.14f, 0.92f);
        Button button = b.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(() => { if (shown != null && shown.onClick != null) shown.onClick(); });

        GameObject t = new GameObject("Label");
        RectTransform tr = t.AddComponent<RectTransform>();
        tr.SetParent(rt, false);
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
        TextMeshProUGUI label = t.AddComponent<TextMeshProUGUI>();
        label.text = "Results";
        label.fontSize = 26f; label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
    }

    /// <summary>The button goes; the turn controls it stood in for come back (a game loaded or started again).</summary>
    public static void Hide() {
        if (shown == null) return;
        if (shown.replaced != null) shown.replaced.gameObject.SetActive(true);
        GameObject go = shown.replaced != null ? shown.gameObject : shown.transform.parent != null ? shown.transform.parent.gameObject : shown.gameObject;
        shown = null;
        Destroy(go);
    }

    void OnDestroy() { if (shown == this) shown = null; }
}
