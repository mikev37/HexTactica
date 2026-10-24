using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The arithmetic of SpritePortrait: where each of a board object's sprites goes when the whole is
/// fitted into a UI box. No scene objects, so the tests check it.
/// </summary>
public static class SpriteLayout {

    /// <summary>One sprite as it sits in the root's frame (what the layout needs, without Unity types beyond vectors).</summary>
    public struct Part {
        public Sprite sprite;
        public Color color;
        public Vector2 center;     // middle of the sprite, in the root's frame
        public Vector2 axisX, axisY;   // the sprite's local x and y axes in the root's frame (length = scale)
        public Vector2 size;       // the sprite's own size, in its local units
    }

    /// <summary>Where a part goes in the target: position, rotation (degrees), scale, size.</summary>
    public struct Placed {
        public Vector2 position, size, scale;
        public float angle;
    }

    /// <summary>
    /// Fit the parts into a box of <paramref name="box"/> pixels (centred, keeping proportions,
    /// <paramref name="margin"/> pixels inside the edge). Pure: no scene objects, so it can be tested.
    /// </summary>
    public static List<Placed> Layout(List<Part> parts, Vector2 box, float margin) {
        List<Placed> result = new List<Placed>();
        if (parts.Count == 0) return result;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (Part p in parts) {
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2) {
                    Vector2 corner = p.center + p.axisX * (sx * p.size.x * 0.5f) + p.axisY * (sy * p.size.y * 0.5f);
                    minX = Mathf.Min(minX, corner.x); maxX = Mathf.Max(maxX, corner.x);
                    minY = Mathf.Min(minY, corner.y); maxY = Mathf.Max(maxY, corner.y);
                }
        }
        float w = Mathf.Max(maxX - minX, 1e-4f), h = Mathf.Max(maxY - minY, 1e-4f);
        float k = Mathf.Min(Mathf.Max(box.x - 2f * margin, 1f) / w, Mathf.Max(box.y - 2f * margin, 1f) / h);
        Vector2 mid = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        foreach (Part p in parts) {
            float sxLen = p.axisX.magnitude, syLen = p.axisY.magnitude;
            float angle = Mathf.Atan2(p.axisX.y, p.axisX.x) * Mathf.Rad2Deg;
            // Mirrored (the axes turned the other way round): flip y, so x keeps the rotation.
            float det = p.axisX.x * p.axisY.y - p.axisX.y * p.axisY.x;
            result.Add(new Placed {
                position = (p.center - mid) * k,
                size = p.size * k,
                scale = new Vector2(sxLen, det < 0f ? -syLen : syLen),
                angle = angle
            });
        }
        return result;
    }

}
