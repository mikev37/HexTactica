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
    public static class EditorUtility {
        public static void SetDirty(UnityEngine.Object o) { }
        public static void RevealInFinder(string path) { }
        public static string OpenFilePanel(string title, string dir, string ext) { return ""; }
        public static string SaveFilePanel(string title, string dir, string name, string ext) { return ""; }
    }
    public static class AssetDatabase {
        public static string[] FindAssets(string filter) { return new string[0]; }
        public static string GUIDToAssetPath(string guid) { return ""; }
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object { return null; }
        public static void SaveAssets() { }
        public static void Refresh() { }
    }
    public static class Undo { public static void RecordObject(UnityEngine.Object o, string name) { } }
}
