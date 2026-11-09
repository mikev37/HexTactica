using UnityEngine;

/// <summary>
/// Assets a scenario's script names (a clip, a character, a marker): the scenario file keeps a key, which the
/// inspector writes for you when you drag the asset in. The key is the asset's name if it is in its kind's folder
/// under Resources (Resources/Music/Special for music, ...), else its path under any Resources folder
/// ("Campaign/Act1/Briefing"), so an asset can live anywhere under a Resources folder.
/// </summary>
public static class ScenarioAssets {

    /// <summary>The asset <paramref name="key"/> names: in <paramref name="folder"/> (e.g. "Music/Special/") by name, else by its path under Resources. Null if none.</summary>
    public static T Load<T>(string key, string folder) where T : Object {
        if (string.IsNullOrEmpty(key)) return null;
        key = key.Trim();
        T found = key.IndexOf('/') < 0 ? Resources.Load<T>(folder + key) : null;
        return found != null ? found : Resources.Load<T>(key);
    }

    /// <summary>
    /// The key for an asset at <paramref name="resourcesPath"/> (its path under a Resources folder, without the
    /// extension): its name if that is <paramref name="folder"/> + name, else the path.
    /// </summary>
    public static string KeyOf(string resourcesPath, string folder) {
        if (string.IsNullOrEmpty(resourcesPath)) return "";
        string f = folder.EndsWith("/") ? folder : folder + "/";
        if (resourcesPath.StartsWith(f) && resourcesPath.IndexOf('/', f.Length) < 0) return resourcesPath.Substring(f.Length);
        return resourcesPath;
    }
}
