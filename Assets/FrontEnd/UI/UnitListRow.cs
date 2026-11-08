using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One unit in the unit explorer's list: its name, its faction (if it is a faction's) and cost.</summary>
public class UnitListRow : MonoBehaviour {
    public Button button;
    public TMP_Text label;
    [Tooltip("Its faction and cost. Optional.")]
    public TMP_Text note;
    public Image background;

    public Unit Unit { get; private set; }

    public void Show(Unit u, string faction, Color normal) {
        Unit = u;
        if (label != null) label.text = u.name;
        if (note != null) note.text = (faction.Length > 0 ? faction + "  " : "") + u.cost.ToString("N0");
        Highlight(normal);
    }

    public void Highlight(Color c) { if (background != null) background.color = c; }
}
