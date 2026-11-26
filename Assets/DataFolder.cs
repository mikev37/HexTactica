using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Where the game keeps the player's files (campaign progress, saves, map-editor scenarios, logs, multiplayer):
/// Application.persistentDataPath, read once on the main thread as the game starts.
///
/// Never a relative path. Unity returns an empty persistentDataPath when it cannot make its folder (an illegal
/// character in Company Name or Product Name, or a name ending in a space or a dot on Windows), and an empty folder
/// combined with "campaign.dat" is a relative path: the file lands in the working directory, which in the editor is
/// the project folder. When that happens it is reported once and a folder under the user's local app data is used.
/// </summary>
public static class DataFolder {
    static string data;
    static bool warned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Read() { if (string.IsNullOrEmpty(data)) data = Application.persistentDataPath; }

    /// <summary>The player's data folder (absolute).</summary>
    public static string Path_ {
        get {
            if (string.IsNullOrEmpty(data)) Read();
            if (!string.IsNullOrEmpty(data) && Path.IsPathRooted(data)) return data;
            string fallback = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mythic Visions"), "Hex Tactica");
            if (!warned) {
                warned = true;
                Debug.LogError("The player data folder (Application.persistentDataPath) came back as '" + data + "'. Check Company Name and Product Name in Player Settings for illegal characters or a trailing space or dot. Keeping files in " + fallback + " instead (never the project folder).");
            }
            return fallback;
        }
    }

    /// <summary>A file or folder in the player's data folder.</summary>
    public static string In(string name) { return Path.Combine(Path_, name ?? ""); }
}
