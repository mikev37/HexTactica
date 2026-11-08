using TMPro;
using UnityEngine;

/// <summary>A name and a value, one line (the unit explorer's attributes).</summary>
public class AttributeRow : MonoBehaviour {
    public TMP_Text label;
    public TMP_Text value;

    public void Show(string name, string v) {
        if (label != null) label.text = name;
        if (value != null) value.text = v;
    }
}
