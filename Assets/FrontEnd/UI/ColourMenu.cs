using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small pop-up of colour swatches to choose one from (the map editor's side colours). It covers the screen with a
/// see-through button that closes it; the swatches are copies of <see cref="swatchTemplate"/> inside <see cref="panel"/>.
/// The look (sizes, background, swatch sprite) is the prefab's.
/// </summary>
public class ColourMenu : MonoBehaviour {
    [Tooltip("Holds the swatches; moved to where the menu was opened from.")]
    public RectTransform panel;
    [Tooltip("One swatch: copied for each colour (its Image takes the colour). Left switched off itself.")]
    public Button swatchTemplate;
    [Tooltip("Covers the screen behind the swatches: a click on it closes the menu without choosing.")]
    public Button outside;
    [Tooltip("The scale of the swatch showing the colour the side has now.")]
    public float currentScale = 1.2f;

    /// <summary>Show <paramref name="colours"/> at <paramref name="at"/> (world position); <paramref name="picked"/> is called with the one clicked. The menu removes itself either way.</summary>
    public void Show(List<Color> colours, Color current, Vector3 at, Action<Color> picked) {
        if (panel != null) panel.position = at;
        if (outside != null) outside.onClick.AddListener(Close);
        if (swatchTemplate == null) return;
        swatchTemplate.gameObject.SetActive(false);
        foreach (Color c in colours) {
            Button b = Instantiate(swatchTemplate, swatchTemplate.transform.parent);
            b.gameObject.SetActive(true);
            Color mine = new Color(c.r, c.g, c.b, 1f);
            if (b.targetGraphic != null) b.targetGraphic.color = mine;
            if (Mathf.Abs(c.r - current.r) + Mathf.Abs(c.g - current.g) + Mathf.Abs(c.b - current.b) < 0.02f) b.transform.localScale = Vector3.one * currentScale;
            b.onClick.AddListener(() => { Close(); if (picked != null) picked(mine); });
        }
    }

    public void Close() { if (this != null && gameObject != null) Destroy(gameObject); }
}
