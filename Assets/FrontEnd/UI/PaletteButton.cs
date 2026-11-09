using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// One terrain, unit or building in the map editor's palette: a picture and a name. A terrain is drawn as a tile of
/// it looks (water, lower and upper; never the floor), a unit or building from its prefab's sprites, as the HUD draws
/// them (SpritePortrait), fitted into the picture box.
/// </summary>
public class PaletteButton : MonoBehaviour {
    public Button button;
    public TMP_Text label;
    [Tooltip("Not used (kept hidden): the picture box draws every kind.")]
    public Image icon;
    [Tooltip("The box a unit or building is drawn into from its sprites (SpritePortrait). Optional.")]
    public RectTransform picture;
    public Image background;

    public Object Item { get; private set; }

    public void Show(Object item, string name, Color normal) {
        Item = item;
        if (label != null) label.text = name;
        if (icon != null) icon.gameObject.SetActive(false);
        Highlight(normal);
    }

    /// <summary>
    /// Draw the picture: once the palette is laid out (the picture box has its real size, so every unit fits it the
    /// same way), and again when the side placed for changes (its owner-coloured sprites take <paramref name="owner"/>).
    /// </summary>
    public void Draw(Color? owner) {
        if (picture == null) return;
        bool drawn = false;
        Unit u = Item as Unit;
        Building b = Item as Building;
        TerrainData td = Item as TerrainData;
        if (u != null) drawn = SpritePortrait.ComposePrefab(u.transform, picture, true, owner);
        else if (b != null) drawn = SpritePortrait.ComposePrefab(b.transform, picture, false, owner);
        // A terrain as a tile of it looks: water, lower and upper stacked as on the board (drawn on a board tile's layout).
        else if (td != null) drawn = SpritePortrait.ComposeTerrain(td, FindAnyObjectByType<HexTile>(), picture);
        picture.gameObject.SetActive(drawn);
    }

    public void Highlight(Color c) { if (background != null) background.color = c; }
}
