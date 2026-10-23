using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes Assets/Resources/TransportPanel.prefab (the transport unload panel) the first time the
/// project opens without it, from TransportPanel.Build. After that it is yours to edit: nothing
/// here touches an existing prefab except the menu item, which asks first.
/// </summary>
[InitializeOnLoad]
static class TransportPanelPrefab {
    const string Path_ = "Assets/Resources/TransportPanel.prefab";

    static TransportPanelPrefab() { EditorApplication.delayCall += () => Create(false); }

    [MenuItem("HexTactica/Recreate Transport Panel Prefab")]
    static void Recreate() {
        if (File.Exists(Path_) && !EditorUtility.DisplayDialog("Recreate Transport Panel Prefab",
                "Replace " + Path_ + " with the default panel? Your edits to it will be lost.", "Replace", "Cancel")) return;
        Create(true);
    }

    static void Create(bool replace) {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!replace && File.Exists(Path_)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path_));
        TransportPanel panel = TransportPanel.Build();
        PrefabUtility.SaveAsPrefabAsset(panel.gameObject, Path_);
        Object.DestroyImmediate(panel.gameObject);
        AssetDatabase.Refresh();
        Debug.Log("Created " + Path_ + " (the transport unload panel): edit it as you like.");
    }
}
