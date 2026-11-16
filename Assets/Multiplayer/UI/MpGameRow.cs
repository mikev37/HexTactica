using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexTactica.Multiplayer {

    /// <summary>One of the player's networked games in the main menu's list: its name#code and how it stands. Click: open it.</summary>
    public class MpGameRow : MonoBehaviour {
        public TMP_Text title;
        public TMP_Text state;
        public Image background;
        public Button button;

        public MpSummary Game { get; private set; }

        public void Show(MpSummary g, string stateText, Color color, System.Action<MpSummary> open) {
            Game = g;
            if (title != null) title.text = g.title;
            if (state != null) state.text = stateText;
            if (background != null) background.color = color;
            if (button != null) { button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => open(g)); }
        }
    }
}
