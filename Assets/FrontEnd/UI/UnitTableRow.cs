using TMPro;
using UnityEngine;

/// <summary>One row of the post-game units table: a cell per column (unit, start, bought, lost, kills, trade).</summary>
public class UnitTableRow : MonoBehaviour {
    [Tooltip("The row's cells, in column order.")]
    public TMP_Text[] cells = new TMP_Text[0];

    public void Show(string[] values) {
        for (int i = 0; i < cells.Length; i++) if (cells[i] != null) cells[i].text = i < values.Length ? values[i] : "";
    }
}
