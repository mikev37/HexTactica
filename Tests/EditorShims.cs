// Type-check-only shim for UnityEditor APIs used by Assets/Editor. Not game code.
using System;
namespace UnityEditor {
    [AttributeUsage(AttributeTargets.Method)] public class MenuItemAttribute : Attribute {
        public MenuItemAttribute(string s) { }
        public MenuItemAttribute(string s, bool validate) { }
        public MenuItemAttribute(string s, bool validate, int priority) { }
    }
    public static class Menu { public static void SetChecked(string path, bool on) { } }
    public static class EditorPrefs {
        public static string GetString(string k, string d) { return d; }
        public static void SetString(string k, string v) { }
    }
    [AttributeUsage(AttributeTargets.Class)] public class InitializeOnLoadAttribute : Attribute { }
    public static class EditorApplication {
        public delegate void CallbackFunction();
        public static CallbackFunction delayCall;
        public static bool isPlayingOrWillChangePlaymode;
        public static bool isPlaying;
    }
    public enum TextureImporterType { Default, Sprite }
    public enum SpriteImportMode { None, Single, Multiple }
    public class AssetImporter { public static AssetImporter GetAtPath(string p) { return null; } public void SaveAndReimport() { } }
    public class AssetPostprocessor { public string assetPath; public AssetImporter assetImporter; }
    public struct AudioImporterSampleSettings { public UnityEngine.AudioClipLoadType loadType; public UnityEngine.AudioCompressionFormat compressionFormat; public float quality; }
    public class AudioImporter : AssetImporter { public bool importSettingsMissing, loadInBackground; public AudioImporterSampleSettings defaultSampleSettings; }
    public class TextureImporter : AssetImporter { public TextureImporterType textureType; public SpriteImportMode spriteImportMode; public bool mipmapEnabled, alphaIsTransparency; public UnityEngine.Vector4 spriteBorder; }
    public static class PrefabUtility { public static UnityEngine.GameObject SaveAsPrefabAsset(UnityEngine.GameObject g, string path) { return g; } }
    public static class EditorUtility {
        public static bool DisplayDialog(string t, string m, string ok, string cancel) { return false; }
        public static bool DisplayDialog(string t, string m, string ok) { return true; }
        public static void SetDirty(UnityEngine.Object o) { }
        public static bool IsPersistent(UnityEngine.Object o) { return false; }
        public static void RevealInFinder(string path) { }
        public static string OpenFilePanel(string title, string dir, string ext) { return ""; }
        public static string OpenFolderPanel(string title, string dir, string name) { return ""; }
        public static string SaveFilePanel(string title, string dir, string name, string ext) { return ""; }
    }
    public static class AssetDatabase {
        public static string[] FindAssets(string filter) { return new string[0]; }
        public static string[] FindAssets(string filter, string[] folders) { return new string[0]; }
        public static string GUIDToAssetPath(string guid) { return ""; }
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object { return null; }
        public static T GetBuiltinExtraResource<T>(string path) where T : UnityEngine.Object { return null; }
        public static void CreateAsset(UnityEngine.Object o, string path) { }
        public static bool DeleteAsset(string path) { return true; }
        public static void SaveAssets() { }
        public static void Refresh() { }
        public static string GetAssetPath(UnityEngine.Object o) { return ""; }
        public static string MoveAsset(string a, string b) { return ""; }
        public static bool IsValidFolder(string p) { return true; }
        public static string CreateFolder(string parent, string name) { return ""; }
    }
    public static class Undo { public static void RecordObject(UnityEngine.Object o, string name) { } }

    // (Inspectors and scene views: Editor/ScenarioTriggerDrawers.)
    public enum SerializedPropertyType { Generic, Integer, Boolean, Float, String }
    public class SerializedObject { public UnityEngine.Object targetObject; public void Update() { } public bool ApplyModifiedProperties() { return true; } public SerializedProperty FindProperty(string n) { return new SerializedProperty(); } }
    public class SerializedProperty {
        public SerializedObject serializedObject = new SerializedObject();
        public string name = "", propertyPath = "", displayName = "", tooltip = "", stringValue = "";
        public int intValue; public long longValue; public bool boolValue, isExpanded; public float floatValue;
        public SerializedPropertyType propertyType;
        public SerializedProperty FindPropertyRelative(string n) { return null; }
    }
    public abstract class PropertyDrawer {
        public UnityEngine.PropertyAttribute attribute;
        public virtual float GetPropertyHeight(SerializedProperty p, UnityEngine.GUIContent l) { return 16f; }
        public virtual void OnGUI(UnityEngine.Rect r, SerializedProperty p, UnityEngine.GUIContent l) { }
    }
    [AttributeUsage(AttributeTargets.Class)] public class CustomPropertyDrawer : Attribute { public CustomPropertyDrawer(Type t) { } }
    [AttributeUsage(AttributeTargets.Class)] public class CustomEditor : Attribute { public CustomEditor(Type t) { } }
    public class Editor : UnityEngine.ScriptableObject { public UnityEngine.Object target; public SerializedObject serializedObject = new SerializedObject(); public virtual void OnInspectorGUI() { } }
    public enum MessageType { None, Info, Warning, Error }
    public static class EditorGUIUtility { public static float singleLineHeight = 18f, standardVerticalSpacing = 2f; }
    public static class EditorGUI {
        public static int indentLevel;
        public static void BeginProperty(UnityEngine.Rect r, UnityEngine.GUIContent l, SerializedProperty p) { }
        public static void EndProperty() { }
        public static float GetPropertyHeight(SerializedProperty p, bool children) { return 18f; }
        public static bool PropertyField(UnityEngine.Rect r, SerializedProperty p, UnityEngine.GUIContent l, bool children) { return false; }
        public static bool PropertyField(UnityEngine.Rect r, SerializedProperty p, UnityEngine.GUIContent l) { return false; }
        public static bool Foldout(UnityEngine.Rect r, bool open, UnityEngine.GUIContent l, bool toggleOnLabelClick) { return open; }
        public static bool Foldout(UnityEngine.Rect r, bool open, string l, bool toggleOnLabelClick) { return open; }
        public static int Popup(UnityEngine.Rect r, string l, int i, string[] o) { return i; }
        public static long LongField(UnityEngine.Rect r, long v) { return v; }
        public static UnityEngine.Object ObjectField(UnityEngine.Rect r, UnityEngine.GUIContent l, UnityEngine.Object o, Type t, bool scene) { return o; }
        public static string TextField(UnityEngine.Rect r, UnityEngine.GUIContent l, string v) { return v; }
        public static bool DropdownButton(UnityEngine.Rect r, UnityEngine.GUIContent c, UnityEngine.FocusType f) { return false; }
        public static void HelpBox(UnityEngine.Rect r, string m, MessageType t) { }
    }
    public static class EditorGUILayout {
        public static void LabelField(string a, string b) { }
        public static void HelpBox(string m, MessageType t) { }
        public static bool PropertyField(SerializedProperty p, bool children) { return false; }
        public static void BeginHorizontal() { }
        public static void EndHorizontal() { }
    }
    public enum GizmoType { NonSelected = 1, Selected = 4 }
    [AttributeUsage(AttributeTargets.Method)] public class DrawGizmo : Attribute { public DrawGizmo(GizmoType t) { } }
    public static class Handles {
        public delegate void CapFunction(int id, UnityEngine.Vector3 p, UnityEngine.Quaternion r, float s, UnityEngine.EventType e);
        public static UnityEngine.Color color;
        public static void DrawPolyLine(params UnityEngine.Vector3[] p) { }
        public static void Label(UnityEngine.Vector3 p, string s) { }
        public static UnityEngine.Vector3 FreeMoveHandle(UnityEngine.Vector3 p, float size, UnityEngine.Vector3 snap, CapFunction cap) { return p; }
        public static void DotHandleCap(int id, UnityEngine.Vector3 p, UnityEngine.Quaternion r, float s, UnityEngine.EventType e) { }
    }
    public static class HandleUtility { public static float GetHandleSize(UnityEngine.Vector3 p) { return 1f; } }
    public static class Selection { public static UnityEngine.Object activeObject; }
    public class GenericMenu { public delegate void MenuFunction(); public void AddItem(UnityEngine.GUIContent c, bool on, MenuFunction f) { } public void DropDown(UnityEngine.Rect r) { } }
}
namespace UnityEngine {
    public enum EventType { Repaint, Layout }
    public enum FocusType { Passive, Keyboard }
}
