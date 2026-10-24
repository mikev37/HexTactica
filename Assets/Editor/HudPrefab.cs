using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes Assets/Resources/Hud.prefab (the unit and tile info panels) the first time the project
/// opens without it, from Hud.Build with the art in Sprites/UI/Widgets (imported as sprites, the
/// panel 9-sliced). After that the prefab is yours: only the menu item replaces it, and it asks.
/// </summary>
[InitializeOnLoad]
static class HudPrefab {
    const string Path_ = "Assets/Resources/Hud.prefab";
    internal const string Art = "Assets/Sprites/UI/Widgets/";

    static HudPrefab() { EditorApplication.delayCall += () => Create(false); }

    [MenuItem("HexTactica/Recreate HUD Prefab")]
    static void Recreate() {
        if (File.Exists(Path_) && !EditorUtility.DisplayDialog("Recreate HUD Prefab",
                "Replace " + Path_ + " with the default panels? Your edits to it will be lost.", "Replace", "Cancel")) return;
        Create(true);
    }

    static void Create(bool replace) {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!replace && File.Exists(Path_)) return;
        PrepareArt();
        Directory.CreateDirectory(Path.GetDirectoryName(Path_));
        Hud hud = Hud.Build(name => AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png"));
        PrefabUtility.SaveAsPrefabAsset(hud.gameObject, Path_);
        Object.DestroyImmediate(hud.gameObject);
        AssetDatabase.Refresh();
        Debug.Log("Created " + Path_ + " (unit and tile panels): edit it as you like. TextMeshPro may ask to import its essentials once.");
    }

    /// <summary>The widget art as sprites; the panel background with 24-pixel borders for 9-slicing.</summary>
    internal static void PrepareArt() {
        if (!Directory.Exists(Art)) return;
        foreach (string f in Directory.GetFiles(Art, "*.png")) {
            string path = f.Replace('\\', '/');
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            bool panel = Path.GetFileNameWithoutExtension(path) == "panel";
            Vector4 border = panel ? new Vector4(24f, 24f, 24f, 24f) : Vector4.zero;
            if (ti.textureType == TextureImporterType.Sprite && ti.spriteBorder == border && !ti.mipmapEnabled) continue;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.spriteBorder = border;
            ti.SaveAndReimport();
        }
    }
}
