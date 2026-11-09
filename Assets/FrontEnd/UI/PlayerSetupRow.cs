using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One side on the pre-game screen: its name and colour, who plays it (a human, or this AI with one of its profiles),
/// and its faction (fixed when the map makes it that faction).
/// </summary>
public class PlayerSetupRow : MonoBehaviour {
    public TMP_Text sideName;
    [Tooltip("Tinted the side's colour.")]
    public Image swatch;
    [Tooltip("Who plays the side: Human, AI (default profile), or AI with a profile (FrontEndSettings.aiProfiles).")]
    public TMP_Dropdown control;
    public TMP_Dropdown faction;
    [Tooltip("Shown when the map fixes the side's faction. Optional.")]
    public GameObject lockedMark;

    public MatchSetup.Side Side { get; private set; }

    public void Show(MatchSetup.Side side, Color color, List<string> controls, int controlPicked, List<string> factions, int factionPicked) {
        Side = side;
        if (sideName != null) sideName.text = side.name;
        if (swatch != null) swatch.color = new Color(color.r, color.g, color.b, 1f);   // (a side's colour may have no alpha: its sprites keep their own)
        if (control != null) {
            control.ClearOptions();
            control.AddOptions(controls);
            control.SetValueWithoutNotify(Mathf.Clamp(controlPicked, 0, controls.Count - 1));
        }
        if (faction != null) {
            faction.ClearOptions();
            faction.AddOptions(factions);
            faction.SetValueWithoutNotify(Mathf.Clamp(factionPicked, 0, factions.Count - 1));
            faction.interactable = !side.factionLocked;
        }
        if (lockedMark != null) lockedMark.SetActive(side.factionLocked);
    }
}
