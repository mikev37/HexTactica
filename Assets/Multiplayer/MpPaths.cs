using System.IO;
using UnityEngine;

namespace HexTactica.Multiplayer {
    /// <summary>
    /// Where this device keeps its multiplayer files: the player's data folder (<see cref="DataFolder"/>), never a
    /// relative path.
    /// </summary>
    public static class MpPaths {
        /// <summary>The player's data folder (absolute): see <see cref="DataFolder"/>.</summary>
        public static string Data { get { return DataFolder.Path_; } }

        /// <summary>This device's working copies: entered lobbies' maps, the latest-turn indexes, the opening scratch board.</summary>
        public static string Local { get { return Path.Combine(Data, "multiplayer"); } }
        /// <summary>The shared folder backend's default store.</summary>
        public static string Shared { get { return Path.Combine(Data, "multiplayer-shared"); } }
    }
}
