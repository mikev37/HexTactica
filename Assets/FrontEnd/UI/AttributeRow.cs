using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One attribute of a unit, as the in-game HUD shows one: an icon, a label, then a row of dots (filled for the value,
/// empty up to the most any unit has) or, for a large or worded value, the value as text.
/// </summary>
public class AttributeRow : MonoBehaviour {
    public Image icon;
    public TMP_Text label;
    [Tooltip("Parent of the dots (a HorizontalLayoutGroup).")]
    public RectTransform dots;
    [Tooltip("Copied once per dot (its first child of Dots, kept hidden).")]
    public Image dotTemplate;
    public TMP_Text value;

    readonly List<Image> made = new List<Image>();

    /// <summary>The value as <paramref name="filled"/> of <paramref name="total"/> dots, in the colours and pictures given.</summary>
    public void ShowDots(Sprite picture, string name, int filled, int total, Color on, Color off, Sprite full, Sprite empty) {
        SetHead(picture, name);
        if (value != null) value.gameObject.SetActive(false);
        if (dots == null || dotTemplate == null) { ShowText(picture, name, filled.ToString()); return; }
        dots.gameObject.SetActive(true);
        dotTemplate.gameObject.SetActive(false);
        while (made.Count < total) made.Add(Instantiate(dotTemplate, dots));
        for (int i = 0; i < made.Count; i++) {
            Image d = made[i];
            bool shown = i < total;
            d.gameObject.SetActive(shown);
            if (!shown) continue;
            bool lit = i < filled;
            if (full != null && empty != null) d.sprite = lit ? full : empty;
            d.color = lit ? on : off;
        }
    }

    /// <summary>The value as text (a cost, a range "2–5", a locomotion).</summary>
    public void ShowText(Sprite picture, string name, string v) {
        SetHead(picture, name);
        if (dots != null) dots.gameObject.SetActive(false);
        if (value != null) { value.gameObject.SetActive(true); value.text = v; }
    }

    void SetHead(Sprite picture, string name) {
        if (icon != null) { icon.sprite = picture; icon.enabled = picture != null; }
        if (label != null) label.text = name;
    }
}
