using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A [CampaignScene] text field (a campaign scene's name): a list of the scenes in the build to pick from, plus
/// "(none)". A name that is not a scene in the build is kept and shown as missing.
/// </summary>
[CustomPropertyDrawer(typeof(CampaignSceneAttribute))]
public class CampaignSceneDrawer : PropertyDrawer {
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        if (property.propertyType != SerializedPropertyType.String) { EditorGUI.PropertyField(position, property, label); return; }
        List<string> names = new List<string> { "(none)" };
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes) {
            string n = Path.GetFileNameWithoutExtension(s.path);
            if (!names.Contains(n)) names.Add(n);
        }
        string value = property.stringValue;
        int index = string.IsNullOrEmpty(value) ? 0 : names.IndexOf(value);
        if (index < 0) { names.Add(value + " (not in the build)"); index = names.Count - 1; }
        EditorGUI.BeginProperty(position, label, property);
        int picked = EditorGUI.Popup(position, label.text, index, names.ToArray());
        if (picked != index) property.stringValue = picked == 0 ? "" : names[picked].Replace(" (not in the build)", "");
        EditorGUI.EndProperty();
    }
}
