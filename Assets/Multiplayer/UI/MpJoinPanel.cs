using TMPro;
using UnityEngine;

namespace HexTactica.Multiplayer {

    /// <summary>
    /// The main menu's Join Game panel: type a game's join code (as the host shows it: "K7Q2MX", or with its name,
    /// "Tidal Ridge#K7Q2MX"; any case). Join: into its lobby to claim a side, or into the game if it has started (a
    /// side can still be handed to you by the host). Watch: follow it without playing.
    /// </summary>
    public class MpJoinPanel : MonoBehaviour {
        public TMP_InputField code;
        [Tooltip("What went wrong (no such game, removed by the host, another version...).")]
        public TMP_Text status;

        void OnEnable() { if (status != null) status.text = ""; if (code != null) code.text = ""; }

        public void Join() { Go(false); }
        public void Watch() { Go(true); }

        async void Go(bool spectator) {
            string c = code != null ? code.text : "";
            Say(spectator ? "Looking for the game..." : "Joining...");
            MpResult r = await Mp.Join(c, spectator);
            if (!r.ok) { Say(r.error ?? "Could not join."); return; }
            if (r.game.state == MpState.Lobby) {
                if (!await MatchSetup.EnterLobby(r.game.code)) Say("Joined, but could not open the lobby: " + Mp.LastError);
            } else MatchSetup.PlayNetworked(r.game.code);
        }

        void Say(string s) { if (status != null) status.text = s; }
    }
}
