using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexTactica.Multiplayer {

    /// <summary>Someone in a networked game, in the lobby's and the host's player lists: their name and what they are (host, watching), with Remove for the host.</summary>
    public class MpMemberRow : MonoBehaviour {
        public TMP_Text label;
        [Tooltip("The host removes the player: they cannot come back.")]
        public Button kickButton;

        public void Show(string text, bool canKick, System.Action kick) {
            if (label != null) label.text = text;
            if (kickButton != null) {
                kickButton.gameObject.SetActive(canKick);
                kickButton.onClick.RemoveAllListeners();
                if (canKick) kickButton.onClick.AddListener(() => kick());
            }
        }
    }
}
