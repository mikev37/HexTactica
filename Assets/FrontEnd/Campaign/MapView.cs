using UnityEngine;

/// <summary>
/// The arithmetic of the campaign map's pan and zoom (CampaignScreen), apart from Unity's UI so it can be checked
/// headless. The map is a UI rectangle, its pivot in its middle, moved by its anchored position and sized by its
/// scale inside a view (its parent) it must always cover.
/// </summary>
public static class MapView {

    /// <summary>
    /// The map's position that keeps it covering the view: it may move only as far as it overhangs the view on each
    /// side. <paramref name="home"/> is where it stands unpanned (the middle), <paramref name="mapSize"/> its own size
    /// (unscaled), <paramref name="scale"/> its scale, <paramref name="viewSize"/> the size of the view.
    /// </summary>
    public static Vector2 Clamp(Vector2 at, Vector2 home, Vector2 mapSize, float scale, Vector2 viewSize) {
        float hx = Mathf.Max(0f, (mapSize.x * scale - viewSize.x) * 0.5f);
        float hy = Mathf.Max(0f, (mapSize.y * scale - viewSize.y) * 0.5f);
        return new Vector2(Mathf.Clamp(at.x, home.x - hx, home.x + hx), Mathf.Clamp(at.y, home.y - hy, home.y + hy));
    }

    /// <summary>
    /// The position that keeps <paramref name="local"/> (a point on the map, in its own unscaled units from its pivot)
    /// where it is on screen when the scale goes from <paramref name="fromScale"/> to <paramref name="toScale"/>:
    /// zooming about the cursor.
    /// </summary>
    public static Vector2 ZoomAbout(Vector2 at, Vector2 local, float fromScale, float toScale) {
        return at + local * (fromScale - toScale);
    }

    /// <summary>The point of the map (0,0 bottom left, 1,1 top right) as a point in its own units from its pivot.</summary>
    public static Vector2 Local(Vector2 point, Vector2 pivot, Vector2 mapSize) {
        return new Vector2((point.x - pivot.x) * mapSize.x, (point.y - pivot.y) * mapSize.y);
    }

    /// <summary>The position that puts <paramref name="local"/> in the middle of the view at <paramref name="scale"/> (unclamped).</summary>
    public static Vector2 CentreOn(Vector2 home, Vector2 local, float scale) {
        return home - local * scale;
    }
}
