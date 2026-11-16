using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexTactica.Multiplayer {

    /// <summary>A side with nobody to play it, in the host's Players panel: give it to a player in the game, to the AI, or take it out of the game.</summary>
    public class MpSideRow : MonoBehaviour {
        public TMP_Text sideName;
        [Tooltip("The players it can be given to.")]
        public TMP_Dropdown players;
        public Button giveButton;
        public Button aiButton;
        [Tooltip("The side resigns: its units go.")]
        public Button eliminateButton;

        public void Show(string side, List<string> takers, System.Action<int> give, System.Action ai, System.Action eliminate) {
            if (sideName != null) sideName.text = side;
            if (players != null) { players.ClearOptions(); players.AddOptions(takers); }
            Wire(giveButton, () => give(players != null ? players.value : 0));
            Wire(aiButton, ai);
            Wire(eliminateButton, eliminate);
            if (giveButton != null) giveButton.interactable = takers.Count > 0;
        }

        static void Wire(Button b, System.Action a) {
            if (b == null) return;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => a());
        }
    }
}
