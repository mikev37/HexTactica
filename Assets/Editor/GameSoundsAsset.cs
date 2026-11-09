using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Makes Assets/Resources/GameSounds.asset (the game's sounds, empty) the first time the project opens without it. Never touches an existing one.</summary>
[InitializeOnLoad]
static class GameSoundsAsset {
    const string Path_ = "Assets/Resources/GameSounds.asset";

    static GameSoundsAsset() { EditorApplication.delayCall += Create; }

    static void Create() {
        if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(Path_)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path_));
        AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GameSounds>(), Path_);
        AssetDatabase.SaveAssets();
        Debug.Log("Created " + Path_ + " (game sounds): set the clips there.");
    }
}
