using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Makes Assets/Resources/UnitAlarmSprites.asset the first time the project opens without it, with the out-of-fuel and out-of-ammo pictures from Sprites/UI. Never touches an existing one.</summary>
[InitializeOnLoad]
static class UnitAlarmSpritesAsset {
    const string Path_ = "Assets/Resources/UnitAlarmSprites.asset";

    static UnitAlarmSpritesAsset() { EditorApplication.delayCall += Create; }

    static void Create() {
        if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(Path_)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path_));
        UnitAlarmSprites a = ScriptableObject.CreateInstance<UnitAlarmSprites>();
        a.outOfFuel = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/out_gas.png");
        a.outOfAmmo = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/out_ammo.png");
        AssetDatabase.CreateAsset(a, Path_);
        AssetDatabase.SaveAssets();
        Debug.Log("Created " + Path_ + " (unit alarm pictures): set the starving picture there.");
    }
}
