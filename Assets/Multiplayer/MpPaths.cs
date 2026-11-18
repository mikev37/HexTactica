using System.IO;
using UnityEngine;

namespace HexTactica.Multiplayer {
    /// <summary>
    /// Where this device keeps its multiplayer files: Application.persistentDataPath, read once on the main thread as
    /// the game starts. Never a relative path: a data path that came back empty once put these files in the project
    /// folder (the working directory), where git picked them up; that is reported and a temporary folder used instead.
    /// </summary>
    public static class MpPaths {
        static string data;
        static bool warned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Read() { if (string.IsNullOrEmpty(data)) data = Application.persistentDataPath; }

        /// <summary>The player's data folder (absolute).</summary>
        public static string Data {
            get {
                if (string.IsNullOrEmpty(data)) Read();
                if (!string.IsNullOrEmpty(data) && Path.IsPathRooted(data)) return data;
                string fallback = Path.Combine(Path.GetTempPath(), "HexTactica");
                if (!warned) { warned = true; Debug.LogError("Multiplayer: the player data folder came back as '" + data + "'; keeping multiplayer files in " + fallback + " instead (never the project folder)."); }
                return fallback;
            }
        }

        /// <summary>This device's working copies: entered lobbies' maps, the latest-turn indexes, the opening scratch board.</summary>
        public static string Local { get { return Path.Combine(Data, "multiplayer"); } }
        /// <summary>The shared folder backend's default store.</summary>
        public static string Shared { get { return Path.Combine(Data, "multiplayer-shared"); } }
    }
}
