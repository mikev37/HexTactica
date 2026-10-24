using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Makes Assets/Resources/FogSettings.asset (how fog of war looks) the first time the project opens without it. Never touches an existing one.</summary>
[InitializeOnLoad]
static class FogSettingsAsset {
    const string Path_ = "Assets/Resources/FogSettings.asset";

    static FogSettingsAsset() { EditorApplication.delayCall += Create; }

    static void Create() {
        if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(Path_)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path_));
        FogSettings a = ScriptableObject.CreateInstance<FogSettings>();
        AssetDatabase.CreateAsset(a, Path_);
        AssetDatabase.SaveAssets();
        Debug.Log("Created " + Path_ + " (fog of war look: tile picture, colour, fade): edit it as you like.");
    }
}
