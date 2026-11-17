using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexTactica.Multiplayer {

    /// <summary>
    /// The main menu's networked games: the player's display name (shown to the others; theirs to change at any time,
    /// the player's id is what counts), and a drop-down of every game they are in. Its button reads "Games ." and
    /// turns to "Games !" when any game waits on them; in the list, those games are highlighted. A game in its lobby
    /// opens the lobby, a game under way opens the game (after playing what happened since), a finished one its end.
    /// </summary>
    public class MpGamesMenu : MonoBehaviour {
        [Tooltip("The player's display name.")]
        public TMP_InputField playerName;
        [Tooltip("Opens and closes the list.")]
        public Button toggleButton;
        public TMP_Text toggleLabel;
        [Tooltip("The button's text: {0} the mark (idleMark, or turnMark when a game waits on the player).")]
        public string toggleFormat = "Games {0}";
        public string idleMark = ".";
        public string turnMark = "!";
        public GameObject listPanel;
        public RectTransform rows;
        public MpGameRow rowTemplate;
        [Tooltip("What went wrong, or that there are no games yet.")]
        public TMP_Text status;
        public string empty = "No networked games yet: host one, or join one by its code.";
        public Color rowColor = new Color(1f, 1f, 1f, 0.08f);
        public Color yourTurnColor = new Color(0.95f, 0.72f, 0.2f, 0.55f);
        public Color finishedColor = new Color(1f, 1f, 1f, 0.03f);

        [Header("Linking devices (optional)")]
        [Tooltip("Shown when the backend can make one player of two devices: a phone and a PC, or after a reinstall.")]
        public GameObject linkPanel;
        [Tooltip("The code: shown here on the new device, typed here on the device already playing.")]
        public TMP_InputField linkCode;
        public TMP_Text linkStatus;
        public string linkHelp = "On the new device, press Get code. Type that code here on the device you already play on and press Confirm. Then press Use it on the new device: it becomes you, with all your games.";

        readonly List<MpGameRow> made = new List<MpGameRow>();

        void Awake() {
            if (rowTemplate != null && rowTemplate.gameObject.scene.IsValid()) rowTemplate.gameObject.SetActive(false);
            if (listPanel != null) listPanel.SetActive(false);
            if (playerName != null) {
                playerName.text = Mp.PlayerName;
                playerName.onEndEdit.AddListener(v => { if (v.Trim().Length > 0) Mp.PlayerName = v; else playerName.text = Mp.PlayerName; });
            }
        }

        void OnEnable() {
            Mp.Listed += Draw; Draw();
            if (MpWatcher.Instance != null) MpWatcher.Instance.ListNow();
            if (linkStatus != null) linkStatus.text = linkHelp;
        }

        // ------------------------------------------------------------------ linking devices

        public void ToggleLink() { if (linkPanel != null) linkPanel.SetActive(!linkPanel.activeSelf && Mp.CanLink); }

        /// <summary>On the new device: a code for the other one.</summary>
        public async void GetCode() {
            LinkSay("Making a code...");
            string code = await Mp.StartLink();
            if (code == null) { LinkSay(Mp.LastError); return; }
            if (linkCode != null) linkCode.text = code;
            LinkSay("Type " + code + " on your other device and press Confirm there, then press Use it here.");
        }

        /// <summary>On the device already playing: confirm the code typed in.</summary>
        public async void ConfirmCode() {
            string code = linkCode != null ? linkCode.text.Trim() : "";
            if (code.Length == 0) { LinkSay("Type the code your new device shows."); return; }
            string failed = await Mp.ConfirmLink(code);
            LinkSay(failed ?? "Confirmed: press Use it on the new device.");
        }

        /// <summary>On the new device, once confirmed: play as the other device's player.</summary>
        public async void UseLinked() {
            string failed = await Mp.FinishLink();
            LinkSay(failed ?? "This device is now you, with all your games.");
            if (failed == null && playerName != null) playerName.text = Mp.PlayerName;
        }

        void LinkSay(string s) { if (linkStatus != null) linkStatus.text = s; }
        void OnDisable() { Mp.Listed -= Draw; }

        public void Toggle() {
            if (listPanel == null) return;
            listPanel.SetActive(!listPanel.activeSelf);
            if (listPanel.activeSelf && MpWatcher.Instance != null) MpWatcher.Instance.ListNow();
        }

        Color? toggleColor;

        void Draw() {
            bool waiting = Mp.Games.Exists(g => g.myTurn);
            if (toggleLabel != null) {
                toggleLabel.text = string.Format(toggleFormat, waiting ? turnMark : idleMark);
                toggleLabel.fontStyle = waiting ? FontStyles.Bold : FontStyles.Normal;
            }
            // Highlighted as a game waiting on the player is in the list.
            Image face = toggleButton != null ? toggleButton.targetGraphic as Image : null;
            if (face != null) {
                if (!toggleColor.HasValue) toggleColor = face.color;
                face.color = waiting ? yourTurnColor : toggleColor.Value;
            }
            foreach (MpGameRow r in made) if (r != null) Destroy(r.gameObject);
            made.Clear();
            if (status != null) status.text = Mp.LastError.Length > 0 ? Mp.LastError : Mp.Games.Count == 0 ? empty : "";
            if (rowTemplate == null || rows == null) return;
            // The games waiting on the player first, then those under way, the lobbies, and the finished ones.
            List<MpSummary> list = new List<MpSummary>(Mp.Games);
            list.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            foreach (MpSummary g in list) {
                MpGameRow row = Instantiate(rowTemplate, rows);
                row.gameObject.SetActive(true);
                row.Show(g, Describe(g), g.myTurn ? yourTurnColor : g.state == MpState.Over || g.state == MpState.Terminated || g.removed ? finishedColor : rowColor, Open);
                made.Add(row);
            }
        }

        static int Rank(MpSummary g) {
            if (g.myTurn) return 0;
            if (g.removed) return 4;
            switch (g.state) { case MpState.Playing: return 1; case MpState.Lobby: return 2; default: return 3; }
        }

        static string Describe(MpSummary g) {
            if (g.removed) return "You are no longer in it";
            switch (g.state) {
                case MpState.Lobby: return g.host ? "Lobby (yours)" : "Lobby";
                case MpState.Over: return "Over";
                case MpState.Terminated: return "Ended by the host";
            }
            if (g.myTurn) return "Your turn";
            return (g.spectator ? "Watching: " : "") + (g.turnSide.Length > 0 ? g.turnSide + " to move" : "Waiting");
        }

        async void Open(MpSummary g) {
            if (g.removed) { await Mp.Forget(g.code); return; }
            if (g.state == MpState.Lobby) {
                if (!await MatchSetup.EnterLobby(g.code) && status != null) status.text = "Could not open the lobby: " + Mp.LastError;
                return;
            }
            MatchSetup.PlayNetworked(g.code);
        }
    }
}
