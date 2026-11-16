using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.Multiplayer {

    /// <summary>
    /// Networked games' settings, in one asset (Assets/Resources/MultiplayerSettings.asset): which backend keeps the
    /// games, how often it is asked for news, the join code's shape, the turn limits on offer, and when the turn clock
    /// shows. Read by Mp, MpSession and the multiplayer screens.
    /// </summary>
    [CreateAssetMenu(menuName = "HexTactica/Multiplayer Settings")]
    public class MultiplayerSettings : ScriptableObject {

        public enum Backend {
            /// <summary>Files in a folder (MpFolderStore): no cloud; for trying it out on one computer or over a shared folder.</summary>
            SharedFolder,
            /// <summary>Unity Gaming Services (MpUgsStore): needs the project linked to UGS and the HEXTACTICA_UGS scripting define (see Multiplayer/README.md).</summary>
            UnityGamingServices
        }

        [Header("Backend")]
        public Backend backend = Backend.SharedFolder;
        [Tooltip("Shared folder backend: the folder the games are kept in. Empty: 'multiplayer-shared' in the player's data folder (Application.persistentDataPath).")]
        public string sharedFolder = "";
        [Tooltip("Unity Gaming Services: the Cloud Code module that writes game data (CloudCode/HexTacticaMp).")]
        public string ugsModule = "HexTacticaMp";

        [Header("Asking for news")]
        [Tooltip("Seconds between looks at the player's games anywhere in the app (the main menu's list, the your-turn notice).")]
        public float listPollSeconds = 15f;
        [Tooltip("Seconds between looks at the game on screen (the lobby, a game being watched): other players' moves show this soon after they are made.")]
        public float livePollSeconds = 2f;
        [Tooltip("Seconds the your-turn notice stays up.")]
        public float noticeSeconds = 6f;
        [Tooltip("The your-turn notice: a prefab under a Resources folder, with an MpNotice on it and its own canvas.")]
        public string noticePrefab = "Multiplayer/Turn Notice";

        [Header("Join codes")]
        [Tooltip("How many characters a game's join code has.")]
        public int codeLength = 6;
        [Tooltip("The characters a join code is made of (none that are easily mistaken for each other).")]
        public string codeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        [Serializable]
        public class TurnLimit {
            public string label = "No limit";
            [Tooltip("Seconds a player has for a turn; 0 no limit.")]
            public int seconds;
        }

        [Header("Turn clock")]
        [Tooltip("The turn limits the host can pick from in the lobby.")]
        public List<TurnLimit> turnLimits = new List<TurnLimit> {
            new TurnLimit { label = "No limit", seconds = 0 },
            new TurnLimit { label = "1 minute", seconds = 60 },
            new TurnLimit { label = "10 minutes", seconds = 600 },
            new TurnLimit { label = "4 hours", seconds = 14400 },
            new TurnLimit { label = "1 day", seconds = 86400 }
        };
        [Tooltip("The clock shows on your turn once this many seconds or fewer are left.")]
        public int clockShowsWithinSeconds = 300;

        [Header("Catching up")]
        [Tooltip("Moves made since the player last looked are played back with every unit's move speed multiplied by this.")]
        public float catchUpMoveSpeed = 3f;

        [Header("Storage")]
        [Tooltip("How many log entries are kept under one key. Changing it only affects games hosted afterwards on every device alike: leave it alone once games are being played.")]
        public int logChunk = 20;

        public const string ResourceName = "MultiplayerSettings";
        static MultiplayerSettings loaded;

        /// <summary>The settings asset (Resources/MultiplayerSettings). Missing, the defaults are used and the console says so.</summary>
        public static MultiplayerSettings Get {
            get {
                if (loaded != null) return loaded;
                loaded = Resources.Load<MultiplayerSettings>(ResourceName);
                if (loaded == null) {
                    Debug.LogWarning("No Assets/Resources/" + ResourceName + ".asset: multiplayer uses its default settings (HexTactica ▸ Multiplayer ▸ Create Missing makes the asset).");
                    loaded = ScriptableObject.CreateInstance<MultiplayerSettings>();
                }
                return loaded;
            }
        }
    }
}
