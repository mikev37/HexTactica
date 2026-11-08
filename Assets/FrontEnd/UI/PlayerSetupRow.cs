using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One side on the pre-game screen: its name and colour, whether this AI plays it, its faction.</summary>
public class PlayerSetupRow : MonoBehaviour {
    public TMP_Text sideName;
    [Tooltip("Tinted the side's colour.")]
    public Image swatch;
    [Tooltip("On: this AI plays the side. Off: a human at the screen.")]
    public Toggle ai;
    public TMP_Dropdown faction;

    public MatchSetup.Side Side { get; private set; }

    public void Show(MatchSetup.Side side, Color color, List<string> factions, int picked) {
        Side = side;
        if (sideName != null) sideName.text = side.name;
        if (swatch != null) swatch.color = color;
        if (ai != null) ai.SetIsOnWithoutNotify(side.ai);
        if (faction != null) {
            faction.ClearOptions();
            faction.AddOptions(factions);
            faction.SetValueWithoutNotify(Mathf.Clamp(picked, 0, factions.Count - 1));
        }
    }
}
