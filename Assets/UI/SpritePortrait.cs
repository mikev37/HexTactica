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
        return Compose(root, target, false, r => include == null || include(r) ? new Look { color = r.color } : (Look?)null, margin);
    }

    /// <summary>How a sprite is drawn in the picture: its colour, and a scale in place of its own (null: as it is).</summary>
    public struct Look {
        public Color color;
        public Vector3? localScale;
    }

    /// <summary>
    /// As above, with each sprite's look decided by <paramref name="look"/> (null: left out); with
    /// <paramref name="inactiveToo"/>, sprites on inactive objects count too (a unit's pips, hidden
    /// while it rides a transport, drawn from their layout).
    /// </summary>
    public static bool Compose(Transform root, RectTransform target, bool inactiveToo, Func<SpriteRenderer, Look?> look, float margin = 4f) {
        List<SpriteLayout.Part> parts = new List<SpriteLayout.Part>();
        List<SpriteRenderer> rs = new List<SpriteRenderer>();
        Dictionary<SpriteRenderer, Look> looks = new Dictionary<SpriteRenderer, Look>();
        foreach (SpriteRenderer r in root.GetComponentsInChildren<SpriteRenderer>(inactiveToo)) {
            if (r == null || r.sprite == null || (!r.enabled && !inactiveToo)) continue;
            Look? l = look(r);
            if (!l.HasValue) continue;
            looks[r] = l.Value;
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
            Look l = looks[r];
            Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
            if (l.localScale.HasValue) {
                Transform t = r.transform;
                Matrix4x4 parent = t.parent != null ? t.parent.localToWorldMatrix : Matrix4x4.identity;
                m = toRoot * parent * Matrix4x4.TRS(t.localPosition, t.localRotation, l.localScale.Value);
            }
            Bounds b = r.sprite.bounds;   // in the sprite's local units, around its pivot
            Vector3 c = b.center;
            Vector3 ax = m.MultiplyVector(Vector3.right), ay = m.MultiplyVector(Vector3.up);
            if (r.flipX) { c.x = -c.x; ax = -ax; }
            if (r.flipY) { c.y = -c.y; ay = -ay; }
            Vector3 p = m.MultiplyPoint3x4(c);
            parts.Add(new SpriteLayout.Part {
                sprite = r.sprite, color = l.color,
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

    // ---------------------------------------------------------------- pips and chevrons

    /// <summary>
    /// The unit's strength pips (its HealthCircles) as on the board, into <paramref name="target"/>:
    /// one lit pip per strength point in its owner's colour, the suppressed ones greyed, the rest
    /// empty outlines. False if the unit has no HealthCircles. Drawn from the pips' own layout and
    /// colouring (HealthDot, setOpacity, setSize), so they look like the board's, even while the
    /// unit rides a transport and its own are hidden.
    /// </summary>
    public static bool ComposePips(Unit u, RectTransform target, float margin = 2f) {
        Transform root = Find(u, "HealthCircles");
        if (root == null) return false;
        Compose(root, target, true, r => {
            HealthDot dot = r.GetComponent<HealthDot>();
            if (dot == null) return new Look { color = r.color };
            int i = Iter(r.gameObject.name);
            bool lit = u.Strength > i;
            bool quiet = lit && i >= u.Strength - u.suppressed;
            Color c = OwnerTint(r, u);
            if (quiet) c = Desaturate(c, dot.suppressedSaturation);
            c.a = lit ? 1f : 0f;
            setSize sz = r.GetComponent<setSize>();
            float scale = sz != null ? (lit ? 0.5f : 1f) : r.transform.localScale.x;
            return new Look { color = c, localScale = new Vector3(scale, scale, 1f) };
        }, margin);
        return true;
    }

    /// <summary>The unit's veterancy chevrons as on the board (one per level, its owner's colour). False if it has none.</summary>
    public static bool ComposeChevrons(Unit u, RectTransform target, float margin = 2f) {
        Transform root = Find(u, "Veterancy");
        if (root == null) return false;
        Compose(root, target, true, r => {
            if (r.GetComponent<VeterancyChevron>() == null) return new Look { color = r.color };
            Color c = OwnerTint(r, u);
            c.a = u.Veterancy > Iter(r.gameObject.name) ? 1f : 0f;
            return new Look { color = c };
        }, margin);
        return true;
    }

    /// <summary>The unit's own child of this name (not one of the units it carries).</summary>
    static Transform Find(Unit u, string name) {
        if (u == null) return null;
        foreach (Transform t in u.GetComponentsInChildren<Transform>(true))
            if (t.name == name && t.GetComponentInParent<Unit>() == u) return t;
        return null;
    }

    /// <summary>The number in a pip's or chevron's name, as HealthDot and VeterancyChevron read it (none: 0).</summary>
    static int Iter(string name) {
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(name ?? "", @"\d+");
        int n;
        return m.Success && int.TryParse(m.Value, out n) ? n : 0;
    }

    /// <summary>The owner's colour as setOpacity mixes it (with its offset colour), else the sprite's own.</summary>
    static Color OwnerTint(SpriteRenderer r, Unit u) {
        setOpacity o = r.GetComponent<setOpacity>();
        if (o == null || u.owner == null) return r.color;
        Color pc = u.owner.displayColor;
        float inv = 1f - o.offset;
        return new Color(pc.r * inv + o.offsetColor.r * o.offset, pc.g * inv + o.offsetColor.g * o.offset, pc.b * inv + o.offsetColor.b * o.offset, 1f);
    }

    static Color Desaturate(Color c, float saturation) {
        float grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
        return new Color(Mathf.Lerp(grey, c.r, saturation), Mathf.Lerp(grey, c.g, saturation), Mathf.Lerp(grey, c.b, saturation), c.a);
    }

    /// <summary>A building's own sprites (not its canvases, not a unit standing on it).</summary>
    public static bool BuildingPart(Building b, SpriteRenderer r) {
        if (r.GetComponentInParent<Canvas>() != null) return false;
        if (r.GetComponentInParent<Unit>() != null) return false;
        return r.GetComponentInParent<Building>() == b;
    }

    /// <summary>A tile's ground: its terrain sprites and roads, not its move/attack overlay, arrow or supply borders.</summary>
    public static bool TilePart(HexTile t, SpriteRenderer r) {
        return r == t.upper || r == t.lower || r == t.water || r == t.floor
            || r == t.road_up || r == t.road_lu || r == t.road_ld || r == t.road_ru || r == t.road_rd || r == t.road_dn;
    }
}
