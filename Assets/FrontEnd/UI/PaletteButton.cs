using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One terrain, unit or building in the map editor's palette.</summary>
public class PaletteButton : MonoBehaviour {
    public Button button;
    public TMP_Text label;
    [Tooltip("Its picture (a terrain's top, a building's sprite, a unit's body). Optional.")]
    public Image icon;
    public Image background;

    public Object Item { get; private set; }

    public void Show(Object item, string name, Sprite picture, Color normal) {
        Item = item;
        if (label != null) label.text = name;
        if (icon != null) { icon.sprite = picture; icon.enabled = picture != null; }
        Highlight(normal);
    }

    public void Highlight(Color c) { if (background != null) background.color = c; }
}
