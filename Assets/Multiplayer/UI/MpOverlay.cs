using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexTactica.Multiplayer {

    /// <summary>
    /// Over a networked game (the MultiplayerOverlay scene, loaded by GameSession). Whose turn it is is the turn
    /// banner's to say (it names the person playing a side on another device). This adds, under the banner:
    /// - a strip, only when there is something to say: catching up on what happened, watching (a spectator), a problem
    ///   reaching the game store; and Skip their turn once the player to move is out of time (any player may);
    /// - the turn clock beside the banner, on the player's own turn once the limit is near (MultiplayerSettings
    ///   .clockShowsWithinSeconds; always, for a 1-minute game);
    /// - a desync: guests are told something went wrong and the host is deciding; the host gets what differed, why it
    ///   can happen, and Continue from my board / End the game;
    /// - for the host, Players: everyone in the game with Remove, and each side with nobody to play it (its player left
    ///   or was removed) with Give to (a player in the game), AI, or Eliminate.
    /// </summary>
    public class MpOverlay : MonoBehaviour {
        [Header("Under the banner")]
        [Tooltip("The strip under the turn banner: shown only when it has something to say.")]
        public GameObject statusPanel;
        [Tooltip("What is going on: catching up, watching, a side waiting for the host.")]
        public TMP_Text turnLine;
        public string catchingUp = "Catching up on what happened...";
        public string watching = "Watching";
        [Tooltip("{0} the side.")]
        public string openTurn = "{0} has nobody to play it: waiting for the host";
        [Tooltip("Status and problems (no connection, removed by the host). Optional.")]
        public TMP_Text notice;

        [Header("Turn clock")]
        public MpClock clock;
        public Button skipButton;

        [Header("Desync")]
        public GameObject desyncPanel;
        public TMP_Text desyncText;
        [Tooltip("Continue from my board and End the game: the host's only.")]
        public GameObject hostDesyncButtons;
        [Tooltip("What guests read: {0} who saw it.")]
        public string guestDesync = "Something went wrong: {0}'s game no longer matches the others'. The host is deciding what happens next.";
        [Tooltip("What the host reads: {0} who saw it, {1} what differed.")]
        public string hostDesync = "{0}'s game no longer matches the log.\n\n{1}\n\nThis can happen when players run different versions of the game, or when a game has been tampered with. Continue from my board: everyone goes on from the board as it is on this device. End the game: it is over for everyone.";

        [Header("Players (the host's)")]
        public Button playersButton;
        public GameObject playersPanel;
        public RectTransform memberRows;
        public MpMemberRow memberTemplate;
        public RectTransform sideRows;
        public MpSideRow sideTemplate;

        readonly List<MpMemberRow> madeMembers = new List<MpMemberRow>();
        readonly List<MpSideRow> madeSides = new List<MpSideRow>();
        string drawnPlayers = "";

        void Awake() {
            TransportPanel.EnsureEventSystem();
            if (memberTemplate != null && memberTemplate.gameObject.scene.IsValid()) memberTemplate.gameObject.SetActive(false);
            if (sideTemplate != null && sideTemplate.gameObject.scene.IsValid()) sideTemplate.gameObject.SetActive(false);
            if (playersPanel != null) playersPanel.SetActive(false);
            if (desyncPanel != null) desyncPanel.SetActive(false);
        }

        void Update() {
            MpGame g = MpSession.Game;
            GameController gc = GameController.gc;
            string status = g == null || gc == null ? (MpSession.State == MpSession.Phase.Opening ? catchingUp : "") : Status(g, gc);
            if (turnLine != null) turnLine.text = status;
            if (notice != null) notice.text = MpSession.Notice;
            bool skip = g != null && gc != null && MpSession.CanSkip;
            Show(skipButton != null ? skipButton.gameObject : null, skip);
            Show(turnLine != null ? turnLine.gameObject : null, status.Length > 0);
            Show(notice != null ? notice.gameObject : null, MpSession.Notice.Length > 0);
            Show(statusPanel, status.Length > 0 || MpSession.Notice.Length > 0 || skip);
            if (g == null || gc == null) { Show(clock != null ? clock.gameObject : null, false); return; }

            double? left = MpSession.SecondsLeft;
            MultiplayerSettings s = MultiplayerSettings.Get;
            bool clockOn = left.HasValue && MpSession.MyTurn && (g.turnSeconds <= s.clockShowsWithinSeconds || left.Value <= s.clockShowsWithinSeconds);
            Show(clock != null ? clock.gameObject : null, clockOn);
            if (clockOn) clock.Show(left.Value);

            bool desync = g.desync.Length > 0 || MpSession.Diverged;
            Show(desyncPanel, desync);
            if (desync && desyncText != null) {
                string who = g.desyncBy.Length > 0 ? g.desyncBy : Mp.PlayerName;
                desyncText.text = MpSession.IsHost ? string.Format(hostDesync, who, g.desync.Length > 0 ? g.desync : "(being reported)") : string.Format(guestDesync, who);
            }
            Show(hostDesyncButtons, desync && MpSession.IsHost);

            Show(playersButton != null ? playersButton.gameObject : null, MpSession.IsHost && !g.Finished);
            if (playersPanel != null && playersPanel.activeSelf) DrawPlayers(g);
        }

        static void Show(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }

        /// <summary>What the strip says beyond the banner: catching up, watching, a side waiting for the host; "" nothing.</summary>
        string Status(MpGame g, GameController gc) {
            if (MpSession.State == MpSession.Phase.Opening || MpSession.State == MpSession.Phase.CatchingUp) return catchingUp;
            if (MpSession.State == MpSession.Phase.Ended || gc.gameOver) return "";
            Player p = gc.activePlayer;
            MpSlot slot = p != null ? g.Slot(p.id) : null;
            if (slot != null && slot.kind == MpSlotKind.Open) return string.Format(openTurn, p.personal_name);
            return MpSession.Spectating ? watching : "";
        }

        // ------------------------------------------------------------------ buttons

        public void Skip() { MpSession.Skip(); }

        public async void ContinueFromMyBoard() {
            MpResult r = await MpSession.HostContinue();
            if (!r.ok) MpSession.Notice = r.error;
        }

        public async void EndGame() {
            MpResult r = await MpSession.HostTerminate();
            if (!r.ok) MpSession.Notice = r.error;
        }

        public void TogglePlayers() {
            if (playersPanel == null) return;
            playersPanel.SetActive(!playersPanel.activeSelf);
            drawnPlayers = "";
        }

        void DrawPlayers(MpGame g) {
            string key = g.ToJson();
            if (key == drawnPlayers) return;
            drawnPlayers = key;
            foreach (MpMemberRow r in madeMembers) if (r != null) Destroy(r.gameObject);
            foreach (MpSideRow r in madeSides) if (r != null) Destroy(r.gameObject);
            madeMembers.Clear(); madeSides.Clear();
            if (memberRows != null && memberTemplate != null)
                foreach (MpMember m in g.members) {
                    MpMemberRow row = Instantiate(memberTemplate, memberRows);
                    row.gameObject.SetActive(true);
                    List<string> held = g.slots.Where(s => s.kind == MpSlotKind.Player && s.player == m.id).Select(s => s.sideName).ToList();
                    string what = m.id == g.host ? " (host)" : held.Count == 0 ? " (watching)" : "";
                    string id = m.id;
                    row.Show(m.name + what + (held.Count > 0 ? ": " + string.Join(", ", held.ToArray()) : ""), m.id != g.host, () => Kick(id));
                    madeMembers.Add(row);
                }
            if (sideRows != null && sideTemplate != null) {
                List<MpMember> takers = g.members.ToList();
                foreach (MpSlot s in g.slots.Where(x => x.kind == MpSlotKind.Open)) {
                    Player p = GameController.gc != null ? GameController.gc.players.Find(x => x != null && x.id == s.side) : null;
                    if (p != null && p.eliminated) continue;
                    MpSideRow row = Instantiate(sideTemplate, sideRows);
                    row.gameObject.SetActive(true);
                    long side = s.side;
                    row.Show(s.sideName, takers.Select(m => m.name).ToList(),
                        i => { if (i >= 0 && i < takers.Count) HandOver(side, MpSlotKind.Player, takers[i].id); },
                        () => HandOver(side, MpSlotKind.AI, ""),
                        () => Eliminate(side));
                    madeSides.Add(row);
                }
            }
        }

        async void Kick(string player) { MpResult r = await MpSession.HostKick(player); if (!r.ok) MpSession.Notice = r.error; drawnPlayers = ""; }
        async void HandOver(long side, MpSlotKind kind, string player) { MpResult r = await MpSession.HostHandOver(side, kind, player, ""); if (!r.ok) MpSession.Notice = r.error; drawnPlayers = ""; }
        async void Eliminate(long side) { MpResult r = await MpSession.HostEliminate(side); if (!r.ok) MpSession.Notice = r.error; drawnPlayers = ""; }
    }
}
