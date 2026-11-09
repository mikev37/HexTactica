using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// One terrain, unit or building in the map editor's palette: a picture and a name. A terrain shows its top sprite;
/// a unit or building is drawn from its prefab's sprites as on the board (SpritePortrait), fitted into the picture box.
/// </summary>
public class PaletteButton : MonoBehaviour {
    public Button button;
    public TMP_Text label;
    [Tooltip("A single sprite (a terrain's top). Optional.")]
    public Image icon;
    [Tooltip("The box a unit or building is drawn into from its sprites (SpritePortrait). Optional.")]
    public RectTransform picture;
    public Image background;

    public Object Item { get; private set; }

    public void Show(Object item, string name, Color normal) {
        Item = item;
        if (label != null) label.text = name;
        bool composed = false;
        Unit u = item as Unit;
        Building b = item as Building;
        if (picture != null && u != null) composed = SpritePortrait.Compose(u.transform, picture, r => SpritePortrait.UnitPart(u, r));
        else if (picture != null && b != null) composed = SpritePortrait.Compose(b.transform, picture, r => SpritePortrait.BuildingPart(b, r));
        if (picture != null) picture.gameObject.SetActive(composed);
        TerrainData td = item as TerrainData;
        Sprite top = td != null ? (td.upper_normal != null ? td.upper_normal : td.floor) : null;
        if (icon != null) { icon.sprite = top; icon.gameObject.SetActive(top != null && !composed); icon.preserveAspect = true; }
        Highlight(normal);
    }

    public void Highlight(Color c) { if (background != null) background.color = c; }
}
