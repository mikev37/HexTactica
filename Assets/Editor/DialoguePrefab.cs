using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes the scenario script's prefabs the first time the project opens without them, as a first layout:
/// <list type="bullet">
/// <item>Assets/Resources/Dialogue.prefab: the dialogue box (DialogueBox): a dark see-through panel along the bottom
///   with the speaker's name and the line, a Next button, and a full-body portrait standing at each end.</item>
/// <item>Assets/Resources/ScenarioMarkers/Marker.prefab: the default marker (ScenarioMarker): a ring round the tile,
///   an arrow bobbing over it and a label.</item>
/// </list>
/// After that they are yours to edit in Unity: nothing here touches a prefab that exists, except the menu item, which
/// asks first. Nothing builds or styles them at run time.
/// </summary>
[InitializeOnLoad]
static class DialoguePrefab {
    const string DialoguePath = "Assets/Resources/" + ScenarioDialogue.DialogueBoxPrefabName + ".prefab";
    const string MarkerPath = "Assets/Resources/" + ScenarioMarker.Folder + "Marker.prefab";
    const string UiArt = "Assets/Sprites/UI/";

    static readonly Color PanelColour = new Color(0.03f, 0.04f, 0.06f, 0.82f);
    static readonly Color PlateColour = new Color(0.11f, 0.13f, 0.17f, 0.96f);
    static readonly Color ButtonColour = new Color(0.22f, 0.27f, 0.36f, 1f);
    static readonly Color TextColour = new Color(0.93f, 0.94f, 0.96f, 1f);
    static readonly Color Accent = new Color(0.36f, 0.62f, 0.95f, 1f);
    static readonly Color MarkerColour = new Color(1f, 0.85f, 0.25f, 1f);

    static DialoguePrefab() { EditorApplication.delayCall += () => { CreateDialogue(false); CreateMarker(false); }; }

    [MenuItem("HexTactica/Scenario Triggers/Recreate Dialogue Prefab")]
    static void RecreateDialogue() {
        if (File.Exists(DialoguePath) && !EditorUtility.DisplayDialog("Recreate Dialogue Prefab",
                "Replace " + DialoguePath + " with the default dialogue box? Your edits to it will be lost.", "Replace", "Cancel")) return;
        CreateDialogue(true);
    }

    [MenuItem("HexTactica/Scenario Triggers/Recreate Marker Prefab")]
    static void RecreateMarker() {
        if (File.Exists(MarkerPath) && !EditorUtility.DisplayDialog("Recreate Marker Prefab",
                "Replace " + MarkerPath + " with the default marker? Your edits to it will be lost.", "Replace", "Cancel")) return;
        CreateMarker(true);
    }

    // ------------------------------------------------------------------ the dialogue box

    static void CreateDialogue(bool replace) {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!replace && File.Exists(DialoguePath)) return;
        HudPrefab.PrepareArt();
        Sprite panel = AssetDatabase.LoadAssetAtPath<Sprite>(HudPrefab.Art + "panel.png");

        GameObject g = new GameObject(ScenarioDialogue.DialogueBoxPrefabName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        g.layer = 5;
        Canvas canvas = g.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 105;   // over the HUD (90) and the transport panel (100), under the post-game screen and the in-game menu
        CanvasScaler scaler = g.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        DialogueBox box = g.AddComponent<DialogueBox>();
        AudioSource voice = g.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0f;
        box.voice = voice;

        GameObject root = Go("Box", g.transform);
        Fill(Rect(root));
        box.root = root;

        // A light shade over the board, catching the clicks meant for it while a line is up.
        GameObject blocker = Go("Blocker", root.transform, typeof(Image));
        Image bi = blocker.GetComponent<Image>();
        bi.color = new Color(0f, 0f, 0f, 0.25f);
        bi.raycastTarget = true;
        Fill(Rect(blocker));

        // Full-body portraits at either end, their feet on the bottom of the screen, standing beside the text.
        box.leftPortrait = Portrait(root.transform, "Left Portrait", 0f);
        box.rightPortrait = Portrait(root.transform, "Right Portrait", 1f);

        // The text, on a dark see-through panel between the portraits.
        GameObject text = Go("Panel", root.transform, typeof(Image));
        Image pi = text.GetComponent<Image>();
        pi.sprite = panel; pi.type = Image.Type.Sliced; pi.color = PanelColour; pi.raycastTarget = true;
        RectTransform pr = Rect(text);
        pr.anchorMin = new Vector2(0.2f, 0f); pr.anchorMax = new Vector2(0.8f, 0f); pr.pivot = new Vector2(0.5f, 0f);
        pr.offsetMin = new Vector2(0f, 24f); pr.offsetMax = new Vector2(0f, 24f + 270f);

        GameObject plate = Go("Name Plate", text.transform, typeof(Image));
        Image npi = plate.GetComponent<Image>();
        npi.sprite = panel; npi.type = Image.Type.Sliced; npi.color = PlateColour; npi.raycastTarget = false;
        RectTransform npr = Rect(plate);
        npr.anchorMin = new Vector2(0f, 1f); npr.anchorMax = new Vector2(0f, 1f); npr.pivot = new Vector2(0f, 0f);
        npr.anchoredPosition = new Vector2(28f, -18f); npr.sizeDelta = new Vector2(360f, 56f);
        box.namePlate = plate;
        box.speakerName = Label(plate.transform, "Name", "", 30f, TextAlignmentOptions.Left, FontStyles.Bold);
        RectTransform nr = box.speakerName.rectTransform;
        Fill(nr); nr.offsetMin = new Vector2(20f, 0f); nr.offsetMax = new Vector2(-12f, 0f);

        box.text = Label(text.transform, "Text", "", 30f, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        RectTransform tr = box.text.rectTransform;
        Fill(tr); tr.offsetMin = new Vector2(40f, 84f); tr.offsetMax = new Vector2(-40f, -52f);

        GameObject next = Go("Next", text.transform, typeof(Image), typeof(Button));
        Image ni = next.GetComponent<Image>();
        ni.sprite = panel; ni.type = Image.Type.Sliced; ni.color = ButtonColour;
        Button nb = next.GetComponent<Button>();
        nb.targetGraphic = ni;
        ColorBlock cb = nb.colors;
        cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f); cb.pressedColor = Accent; cb.selectedColor = Color.white;
        nb.colors = cb;
        RectTransform xr = Rect(next);
        xr.anchorMin = new Vector2(1f, 0f); xr.anchorMax = new Vector2(1f, 0f); xr.pivot = new Vector2(1f, 0f);
        xr.anchoredPosition = new Vector2(-24f, 20f); xr.sizeDelta = new Vector2(200f, 56f);
        box.nextButton = nb;
        box.nextLabel = Label(next.transform, "Label", "Next", 28f, TextAlignmentOptions.Center, FontStyles.Bold);
        Fill(box.nextLabel.rectTransform);

        root.SetActive(false);
        Directory.CreateDirectory(Path.GetDirectoryName(DialoguePath));
        PrefabUtility.SaveAsPrefabAsset(g, DialoguePath);
        Object.DestroyImmediate(g);
        AssetDatabase.Refresh();
        Debug.Log("Created " + DialoguePath + " (the scenario dialogue box): edit it as you like. TextMeshPro may ask to import its essentials once.");
    }

    static Image Portrait(Transform parent, string name, float side) {
        GameObject p = Go(name, parent, typeof(Image));
        Image i = p.GetComponent<Image>();
        i.preserveAspect = true;
        i.raycastTarget = false;
        RectTransform r = Rect(p);
        r.anchorMin = new Vector2(side, 0f); r.anchorMax = new Vector2(side, 0f); r.pivot = new Vector2(0.5f, 0f);
        r.anchoredPosition = new Vector2(side < 0.5f ? 200f : -200f, 0f);
        r.sizeDelta = new Vector2(560f, 700f);
        p.SetActive(false);
        return i;
    }

    // ------------------------------------------------------------------ the marker

    static void CreateMarker(bool replace) {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!replace && File.Exists(MarkerPath)) return;
        GameObject g = new GameObject("Marker");
        ScenarioMarker m = g.AddComponent<ScenarioMarker>();

        GameObject ring = new GameObject("Ring", typeof(SpriteRenderer));
        ring.transform.SetParent(g.transform, false);
        ring.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
        SpriteRenderer rr = ring.GetComponent<SpriteRenderer>();
        rr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiArt + "Selected.png");
        rr.color = MarkerColour;
        rr.sortingLayerName = "UI"; rr.sortingOrder = 20;
        m.pulse = ring.transform;

        GameObject arrow = new GameObject("Arrow", typeof(SpriteRenderer));
        arrow.transform.SetParent(g.transform, false);
        arrow.transform.localPosition = new Vector3(0f, 4.5f, 0f);
        arrow.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
        SpriteRenderer ar = arrow.GetComponent<SpriteRenderer>();
        ar.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiArt + "arrow.png");
        ar.color = MarkerColour;
        ar.sortingLayerName = "UI"; ar.sortingOrder = 21;
        m.bob = arrow.transform;

        GameObject label = new GameObject("Label", typeof(TextMeshPro));
        label.transform.SetParent(g.transform, false);
        label.transform.localPosition = new Vector3(0f, 8f, 0f);
        TextMeshPro t = label.GetComponent<TextMeshPro>();
        t.text = ""; t.fontSize = 12f; t.alignment = TextAlignmentOptions.Center; t.fontStyle = FontStyles.Bold; t.color = MarkerColour;
        t.rectTransform.sizeDelta = new Vector2(24f, 4f);
        t.sortingLayerID = SortingLayer.NameToID("UI"); t.sortingOrder = 22;
        m.label = t;

        Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath));
        PrefabUtility.SaveAsPrefabAsset(g, MarkerPath);
        Object.DestroyImmediate(g);
        AssetDatabase.Refresh();
        Debug.Log("Created " + MarkerPath + " (the default scenario marker): edit it, or add more marker prefabs beside it.");
    }

    // ------------------------------------------------------------------ building blocks

    static GameObject Go(string name, Transform parent, params System.Type[] components) {
        GameObject g = new GameObject(name, typeof(RectTransform));
        foreach (System.Type t in components) g.AddComponent(t);
        g.layer = 5;
        if (parent != null) g.transform.SetParent(parent, false);
        return g;
    }

    static RectTransform Rect(GameObject g) { return (RectTransform)g.transform; }

    static void Fill(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }

    static TextMeshProUGUI Label(Transform parent, string name, string text, float size, TextAlignmentOptions align, FontStyles style) {
        GameObject g = Go(name, parent, typeof(TextMeshProUGUI));
        TextMeshProUGUI t = g.GetComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.alignment = align; t.fontStyle = style; t.color = TextColour;
        t.raycastTarget = false;
        return t;
    }
}
