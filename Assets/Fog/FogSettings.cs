using UnityEngine;

/// <summary>
/// How fog of war looks: one asset, Assets/Resources/FogSettings.asset (made the first time the
/// editor opens without it; edit it there). Without it these defaults are used.
/// </summary>
[CreateAssetMenu(menuName = "HexTactica/Fog Settings", fileName = "FogSettings")]
public class FogSettings : ScriptableObject {

    [Tooltip("Laid over every tile the viewer does not see, at the tile's position like its terrain sprites. Empty: a plain hex the exact size of one grid cell (worked out from how the tiles are spaced), so neighbours meet without overlapping. (A HexTile prefab with a child named \"Fog\" that has a SpriteRenderer uses that instead, as placed there.)")]
    public Sprite tileSprite;
    [Tooltip("The plain hex's centre, from the tile's position (the terrain art's ground sits above its pivot).")]
    public Vector2 hexOffset = new Vector2(0f, 1.72f);
    [Tooltip("The plain hex's size as a share of one grid cell (1: neighbours just meet; above 1 they overlap).")]
    public float hexScale = 1f;
    [Tooltip("Tint and opacity of the fog over a tile not seen.")]
    public Color fogColor = new Color(0.16f, 0.16f, 0.18f, 0.72f);
    [Tooltip("Sorting layer of the fog: above the ground and buildings, below units and the move/attack overlays.")]
    public string sortingLayer = "Building";
    public int sortingOrder = 1000;
    [Tooltip("Seconds for a unit or a tile to fade fully in or out of sight.")]
    public float fadeSeconds = 0.35f;
    [Tooltip("Opacity of the viewer's own units that no enemy sees.")]
    [Range(0, 1)] public float ownHiddenAlpha = 0.5f;

    public const string ResourceName = "FogSettings";
    static FogSettings instance;

    public static FogSettings Instance {
        get {
            if (instance == null) instance = Resources.Load<FogSettings>(ResourceName);
            if (instance == null) instance = CreateInstance<FogSettings>();
            return instance;
        }
    }
}
