using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One side of the map in the map editor: its name, its colour (click to change), the faction the map makes it (or any, chosen before the game), and a button that removes it.</summary>
public class MapPlayerRow : MonoBehaviour {
    public TMP_InputField sideName;
    [Tooltip("Shows the side's colour; clicking it gives the next colour (MapEditorPanel.sideColors).")]
    public Button colour;
    public Image swatch;
    [Tooltip("\"Any\" (chosen on the pre-game screen), or a faction the map fixes.")]
    public TMP_Dropdown faction;
    public Button remove;

    public Player Player { get; set; }
}
