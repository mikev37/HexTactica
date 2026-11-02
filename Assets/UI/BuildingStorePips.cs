using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A forward base's store, shown above it as green circles: one filled circle a turn of supply it can still give while
/// cut off from the HQ, a faint one a turn it could still take (suppliesMax). Distinct from the strength pips (grey hexes
/// under a unit). Added at run time to every supply building with a store (Building.Start); no prefab needed.
/// Shown for the screen's player's own buildings, and for others only while their tile is in sight (fog of war).
/// </summary>
public class BuildingStorePips : MonoBehaviour {
    /// <summary>Shown at all (off: no circles).</summary>
    public static bool Enabled = true;

    public Building building;
    [Tooltip("Diameter of a circle (world units).")]
    public float size = 0.75f;
    [Tooltip("Centre to centre (world units).")]
    public float spacing = 0.85f;
    [Tooltip("Height of the row above the building (world units).")]
    public float height = 2.2f;
    public Color filled = new Color(0.25f, 0.9f, 0.3f, 1f);
    public Color empty = new Color(0.25f, 0.9f, 0.3f, 0.25f);

    readonly List<SpriteRenderer> circles = new List<SpriteRenderer>();
    int shownStore = -1, shownMax = -1;
    bool shownVisible;
    static Sprite circle;

    /// <summary>Does <paramref name="b"/> keep a store worth showing (a supply building other than the HQ)?</summary>
    public static bool HasStore(Building b) { return b != null && b.producesSupply && !b.IsHQ && b.suppliesMax > 0; }

    /// <summary>How many circles are filled for <paramref name="b"/>: the turns of store it has left.</summary>
    public static int Filled(Building b) { return HasStore(b) ? Mathf.Clamp(b.supplies, 0, b.suppliesMax) : 0; }

    /// <summary>Are the circles shown to the screen's player: its own building, or one in its sight (fog of war)?</summary>
    public static bool ShownTo(Building b) {
        if (!Enabled || !HasStore(b) || !b.Functioning) return false;
        if (!FogOfWar.On) return true;
        Player viewer = FogOfWar.Viewer;
        if (viewer == null || b.owner == viewer) return true;
        foreach (HexTile t in b.Footprint) if (FogOfWar.TileVisible(viewer, t)) return true;
        return false;
    }

    void LateUpdate() {
        if (building == null) building = GetComponent<Building>();
        if (building == null) return;
        bool visible = ShownTo(building);
        int store = Filled(building), max = HasStore(building) ? building.suppliesMax : 0;
        if (visible == shownVisible && store == shownStore && max == shownMax) return;
        shownVisible = visible; shownStore = store; shownMax = max;
        Redraw(visible ? max : 0, store);
    }

    void Redraw(int max, int store) {
        if (circle == null) circle = MakeCircle();
        while (circles.Count < max) {
            GameObject g = new GameObject("StorePip" + circles.Count);
            g.transform.SetParent(transform, false);
            SpriteRenderer r = g.AddComponent<SpriteRenderer>();
            r.sprite = circle;
            r.sortingLayerName = "UI";
            r.sortingOrder = 40;
            circles.Add(r);
        }
        float left = -(max - 1) * spacing / 2f;
        for (int i = 0; i < circles.Count; i++) {
            SpriteRenderer r = circles[i];
            bool on = i < max;
            if (r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
            if (!on) continue;
            r.transform.localPosition = new Vector3(left + i * spacing, height, 0f);
            r.transform.localScale = new Vector3(size, size, 1f);
            r.color = i < store ? filled : empty;
        }
    }

    /// <summary>A white disc with a soft edge, one world unit across (tinted by the renderer).</summary>
    static Sprite MakeCircle() {
        const int n = 64;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] px = new Color[n * n];
        float c = (n - 1) / 2f, rad = n / 2f - 1f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++) {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(rad - d);                       // soft edge
                float ring = d > rad - 5f ? 0.55f : 1f;                  // a darker rim, so it reads on any ground
                px[y * n + x] = new Color(ring, ring, ring, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }
}
