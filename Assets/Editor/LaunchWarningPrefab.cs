using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes Assets/Resources/LaunchWarning.prefab the first time the project opens without it, as a first layout: a yellow
/// warning banner across the top of the screen ("MISSILE LAUNCH DETECTED", and which site of how many), clickable, with
/// an audio source for a warning sound (none assigned). After that it is yours to edit in Unity: text, colours, layout,
/// sound and timings (LaunchWarning). Nothing here touches the prefab once it exists, except the menu item, which asks
/// first. Nothing builds or styles it at run time.
/// </summary>
[InitializeOnLoad]
static class LaunchWarningPrefab {
    const string PrefabPath = "Assets/Resources/" + LaunchWarning.PrefabName + ".prefab";

    static readonly Color Yellow = new Color(1f, 0.82f, 0.1f, 0.95f);
    static readonly Color Ink = new Color(0.08f, 0.07f, 0.02f, 1f);

    static LaunchWarningPrefab() { EditorApplication.delayCall += () => Create(false); }

    [MenuItem("HexTactica/UI/Recreate Launch Warning Prefab")]
    static void Recreate() {
        if (File.Exists(PrefabPath) && !EditorUtility.DisplayDialog("Recreate Launch Warning Prefab",
                "Replace " + PrefabPath + " with the default banner? Your edits to it will be lost.", "Replace", "Cancel")) return;
        Create(true);
    }

    static void Create(bool replace) {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!replace && File.Exists(PrefabPath)) return;

        GameObject g = new GameObject(LaunchWarning.PrefabName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        g.layer = 5;
        Canvas canvas = g.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 95;   // over the HUD (90), under the transport panel (100) and the dialogue box (105)
        CanvasScaler scaler = g.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        LaunchWarning w = g.AddComponent<LaunchWarning>();
        AudioSource sound = g.AddComponent<AudioSource>();
        sound.playOnAwake = false;
        sound.spatialBlend = 0f;
        w.sound = sound;

        GameObject banner = Go("Banner", g.transform, typeof(Image), typeof(Button), typeof(CanvasGroup));
        RectTransform br = Rect(banner);
        br.anchorMin = new Vector2(0.5f, 1f); br.anchorMax = new Vector2(0.5f, 1f); br.pivot = new Vector2(0.5f, 1f);
        br.anchoredPosition = new Vector2(0f, -90f); br.sizeDelta = new Vector2(860f, 84f);
        Image bi = banner.GetComponent<Image>();
        bi.color = Yellow;
        Button button = banner.GetComponent<Button>();
        button.targetGraphic = bi;
        ColorBlock cb = button.colors;
        cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f); cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f); cb.selectedColor = Color.white;
        button.colors = cb;
        w.banner = banner;
        w.button = button;
        w.fade = banner.GetComponent<CanvasGroup>();

        TMP_Text title = Label(banner.transform, "Text", "MISSILE LAUNCH DETECTED", 40f, TextAlignmentOptions.Center, FontStyles.Bold);
        RectTransform tr = title.rectTransform;
        Fill(tr); tr.offsetMin = new Vector2(20f, 0f); tr.offsetMax = new Vector2(-20f, 0f);

        TMP_Text count = Label(banner.transform, "Count", "", 22f, TextAlignmentOptions.Right, FontStyles.Bold);
        RectTransform cr = count.rectTransform;
        Fill(cr); cr.offsetMin = new Vector2(20f, 0f); cr.offsetMax = new Vector2(-24f, 0f);
        w.count = count;

        banner.SetActive(false);
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        PrefabUtility.SaveAsPrefabAsset(g, PrefabPath);
        Object.DestroyImmediate(g);
        AssetDatabase.SaveAssets();
        Debug.Log("Launch warning prefab made: " + PrefabPath + ".");
    }

    // ------------------------------------------------------------------ building blocks

    static TMP_Text Label(Transform parent, string name, string text, float size, TextAlignmentOptions align, FontStyles style) {
        GameObject g = Go(name, parent, typeof(TextMeshProUGUI));
        TextMeshProUGUI t = g.GetComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.alignment = align; t.fontStyle = style; t.color = Ink;
        t.raycastTarget = false;
        return t;
    }

    static GameObject Go(string name, Transform parent, params System.Type[] components) {
        GameObject g = new GameObject(name, typeof(RectTransform));
        foreach (System.Type t in components) g.AddComponent(t);
        g.layer = 5;
        if (parent != null) g.transform.SetParent(parent, false);
        return g;
    }

    static RectTransform Rect(GameObject g) { return (RectTransform)g.transform; }

    static void Fill(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
}
