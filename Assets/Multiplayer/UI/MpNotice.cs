using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexTactica.Multiplayer {

    /// <summary>
    /// The your-turn notice (the TurnNotice scene, loaded over any screen by MpWatcher): "Your turn in Tidal
    /// Ridge#K7Q2MX". Open goes to that game; Dismiss takes the notice down. It goes by itself after
    /// MultiplayerSettings.noticeSeconds.
    /// </summary>
    public class MpNotice : MonoBehaviour {
        public TMP_Text message;
        [Tooltip("The message: {0} the game's name and code, {1} the side to move.")]
        public string format = "Your turn in {0}";
        public Button openButton;

        /// <summary>The game to announce when the scene comes up.</summary>
        public static MpSummary Pending;
        public static MpNotice Instance { get; private set; }
        MpSummary shown;

        void Awake() { Instance = this; TransportPanel.EnsureEventSystem(); }
        void OnDestroy() { if (Instance == this) Instance = null; }
        void Start() { if (Pending != null) Show(Pending); }

        public void Show(MpSummary s) {
            shown = s;
            if (message != null) message.text = string.Format(format, s.title, s.turnSide);
        }

        /// <summary>Play the game the notice is about.</summary>
        public void Open() {
            if (shown == null) return;
            if (MpWatcher.Instance != null) MpWatcher.Instance.HideNotice();
            MatchSetup.PlayNetworked(shown.code);
        }

        public void Dismiss() { if (MpWatcher.Instance != null) MpWatcher.Instance.HideNotice(); }
    }
}
