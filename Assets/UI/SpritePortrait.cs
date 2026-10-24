using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A picture, in the UI, of something drawn on the board from several sprites (a unit's hull,
/// turret, wheels and markings; a tile's ground, water, edge and roads): every sprite is copied
/// into a UI Image laid out exactly as on the board — position, rotation, scale, mirroring, colour
/// and draw order — then the whole is fitted into the target box. Seen from the root's own frame,
/// so a unit facing left is still shown facing right.
///
/// Used by the transport panel's buttons and by the HUD's unit and terrain pictures.
/// </summary>
public static class SpritePortrait {

    /// <summary>
    /// Show every sprite under <paramref name="root"/> that <paramref name="include"/> accepts, fitted
    /// into <paramref name="target"/> (its existing child Images are reused). False if there was none.
    /// </summary>
    public static bool Compose(Transform root, RectTransform target, Func<SpriteRenderer, bool> include, float margin = 4f) {
        List<SpriteLayout.Part> parts = new List<SpriteLayout.Part>();
        List<SpriteRenderer> rs = new List<SpriteRenderer>();
        foreach (SpriteRenderer r in root.GetComponentsInChildren<SpriteRenderer>(false)) {
            if (r == null || r.sprite == null || !r.enabled) continue;
            if (include != null && !include(r)) continue;
            rs.Add(r);
        }
        // Draw order as on the board: sorting layer, then order in layer, then hierarchy.
        Dictionary<SpriteRenderer, int> seq = new Dictionary<SpriteRenderer, int>();
        for (int i = 0; i < rs.Count; i++) seq[rs[i]] = i;
        rs.Sort((a, b) => {
            int c = SortingLayer.GetLayerValueFromID(a.sortingLayerID).CompareTo(SortingLayer.GetLayerValueFromID(b.sortingLayerID));
            if (c != 0) return c;
            c = a.sortingOrder.CompareTo(b.sortingOrder);
            return c != 0 ? c : seq[a].CompareTo(seq[b]);
        });
        Matrix4x4 toRoot = root.worldToLocalMatrix;
        foreach (SpriteRenderer r in rs) {
            Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
            Bounds b = r.sprite.bounds;   // in the sprite's local units, around its pivot
            Vector3 c = b.center;
            Vector3 ax = m.MultiplyVector(Vector3.right), ay = m.MultiplyVector(Vector3.up);
            if (r.flipX) { c.x = -c.x; ax = -ax; }
            if (r.flipY) { c.y = -c.y; ay = -ay; }
            Vector3 p = m.MultiplyPoint3x4(c);
            parts.Add(new SpriteLayout.Part {
                sprite = r.sprite, color = r.color,
                center = new Vector2(p.x, p.y),
                axisX = new Vector2(ax.x, ax.y), axisY = new Vector2(ay.x, ay.y),
                size = new Vector2(b.size.x, b.size.y)
            });
        }
        Vector2 box = target.rect.size;
        // Not laid out yet (a layout group sizes it next frame): use its own size, else a slot's.
        if (box.x < 1f || box.y < 1f) box = target.sizeDelta.x >= 1f && target.sizeDelta.y >= 1f ? target.sizeDelta : new Vector2(64f, 64f);
        List<SpriteLayout.Placed> placed = SpriteLayout.Layout(parts, box, margin);

        // Reuse the target's child images, one per part, in draw order.
        List<Image> images = new List<Image>();
        for (int i = 0; i < target.childCount; i++) {
            Image img = target.GetChild(i).GetComponent<Image>();
            if (img != null && target.GetChild(i).name.StartsWith("Part")) images.Add(img);
        }
        while (images.Count < parts.Count) {
            GameObject g = new GameObject("Part");
            RectTransform rt = g.AddComponent<RectTransform>();
            rt.SetParent(target, false);
            Image img = g.AddComponent<Image>();
            img.raycastTarget = false;
            images.Add(img);
        }
        for (int i = 0; i < images.Count; i++) {
            Image img = images[i];
            bool on = i < parts.Count;
            if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
            if (!on) continue;
            RectTransform rt = img.rectTransform;
            rt.SetSiblingIndex(i);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            img.sprite = parts[i].sprite;
            img.color = parts[i].color;
            img.preserveAspect = false;
            rt.sizeDelta = placed[i].size;
            rt.anchoredPosition = placed[i].position;
            rt.localRotation = Quaternion.Euler(0f, 0f, placed[i].angle);
            rt.localScale = new Vector3(placed[i].scale.x, placed[i].scale.y, 1f);
        }
        return parts.Count > 0;
    }

    // ---------------------------------------------------------------- what to include

    /// <summary>
    /// Parts of a unit that are not the unit itself: any sprite with one of these in its own or a
    /// parent's name (below the unit; case ignored) is left out of the picture. They are the nested
    /// prefabs every unit has: pips, chevrons, shadow, exhaust smoke and muzzle flashes.
    /// </summary>
    public static string[] UnitSkips = { "shadow", "HealthCircles", "Veterancy", "SmokeStack", "Cannon", "GunMuzzle" };

    /// <summary>Sorting layers that are never the unit's body (pips and chevrons are on UI).</summary>
    public static string[] SkipLayers = { "UI", "Effects", "UnitShadow" };

    /// <summary>A unit's own sprites: not its shadow, pips, chevrons, smoke or flashes, not the units it carries.</summary>
    public static bool UnitPart(Unit u, SpriteRenderer r) {
        if (r.color.a < 0.02f) return false;   // hidden by its animation
        if (r.GetComponentInParent<Canvas>() != null) return false;
        if (r.GetComponentInParent<Unit>() != u) return false;
        string layer = SortingLayer.IDToName(r.sortingLayerID);
        foreach (string l in SkipLayers) if (layer == l) return false;
        for (Transform t = r.transform; t != null && t != u.transform; t = t.parent)
            foreach (string skip in UnitSkips)
                if (t.name.IndexOf(skip, StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return true;
    }

    /// <summary>A tile's ground: its terrain sprites and roads, not its move/attack overlay, arrow or supply borders.</summary>
    public static bool TilePart(HexTile t, SpriteRenderer r) {
        return r == t.upper || r == t.lower || r == t.water || r == t.floor
            || r == t.road_up || r == t.road_lu || r == t.road_ld || r == t.road_ru || r == t.road_rd || r == t.road_dn;
    }
}
