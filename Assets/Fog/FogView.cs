using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fog of war on the board: every tile the screen's player (FogOfWar.Viewer) does not see is covered
/// with a dark grey hex (FogSettings: the picture, its colour and opacity, its sorting), fading in and
/// out as sight changes. Ground and buildings still show through, dimmed; buildings show their owner
/// as last seen (Building.RefreshFlag). Units are faded by their own FogFader.
///
/// Spawned when a scene starts (not in the headless harness). It also keeps sight up to date: if
/// anything moved without saying so (FogOfWar.Changed), it notices from one frame to the next.
/// </summary>
public class FogView : MonoBehaviour {

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn() {
        if (GameController.gc != null && GameController.gc.headless) return;
        if (Object.FindAnyObjectByType<FogView>() != null) return;
        new GameObject("FogView").AddComponent<FogView>();
    }

    class Cover { public SpriteRenderer sr; public float alpha; }
    readonly Dictionary<HexTile, Cover> covers = new Dictionary<HexTile, Cover>();
    Sprite builtIn;
    int lastSignature, lastVersion = -1;
    Player lastViewer;
    bool hadFog;

    void LateUpdate() {
        HexGenerator hg = HexGenerator.hg;
        if (hg == null || hg.tiles == null) return;

        // Something moved that did not say so: work sight out again.
        int sig = Signature();
        if (sig != lastSignature) { lastSignature = sig; FogOfWar.Changed(); }
        FogOfWar.Refresh();

        Player viewer = FogOfWar.Viewer;
        bool fog = viewer != null;
        // Enemy supply borders are drawn only where the viewer sees: redraw them when sight changes.
        if (FogOfWar.Version != lastVersion || viewer != lastViewer || fog != hadFog) {
            lastVersion = FogOfWar.Version; lastViewer = viewer; hadFog = fog;
            HexTilePlayerSupplyDisplay.WakeAll();
        }
        if (!fog && covers.Count == 0) return;

        FogSettings set = FogSettings.Instance;
        float step = Time.deltaTime / Mathf.Max(0.01f, set.fadeSeconds);
        foreach (HexTile t in hg.tiles) {
            if (t == null) continue;
            float want = fog && !FogOfWar.TileVisible(viewer, t) ? 1f : 0f;
            Cover c;
            if (!covers.TryGetValue(t, out c)) {
                if (want <= 0f) continue;
                c = new Cover { sr = Make(t, set), alpha = Time.timeSinceLevelLoad < 0.5f ? 1f : 0f };
                covers[t] = c;
            }
            if (c.alpha == want && c.sr.enabled == (want > 0f)) continue;
            c.alpha = Mathf.MoveTowards(c.alpha, want, step);
            Color col = set.fogColor;
            col.a *= c.alpha;
            c.sr.color = col;
            c.sr.enabled = c.alpha > 0.001f;
        }
    }

    SpriteRenderer Make(HexTile t, FogSettings set) {
        // The tile prefab's own fog, if it has one (a child named "Fog" with a SpriteRenderer).
        Transform own = t.transform.Find("Fog");
        SpriteRenderer mine = own != null ? own.GetComponent<SpriteRenderer>() : null;
        if (mine != null) { mine.color = new Color(0f, 0f, 0f, 0f); return mine; }
        GameObject g = new GameObject("Fog");
        g.transform.SetParent(t.transform, false);
        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        if (set.tileSprite != null) { g.transform.localPosition = Vector3.zero; sr.sprite = set.tileSprite; }
        else { g.transform.localPosition = new Vector3(set.hexOffset.x, set.hexOffset.y, 0f); sr.sprite = BuiltIn(t, set); }
        if (!string.IsNullOrEmpty(set.sortingLayer)) sr.sortingLayerName = set.sortingLayer;
        sr.sortingOrder = set.sortingOrder;
        sr.color = new Color(0f, 0f, 0f, 0f);
        return sr;
    }

    /// <summary>
    /// A flat-topped hex the size of one grid cell, measured from how the tiles are laid out: the
    /// step to the next tile in the same column is the cell's height, the step across to the next
    /// column three quarters of its width. Neighbours meet edge to edge (FogSettings.hexScale).
    /// </summary>
    Sprite BuiltIn(HexTile any, FogSettings set) {
        if (builtIn != null) return builtIn;
        float h = 6.5f, w = 16f / 3f * 4f / 3f;   // HexGenerator's spacing, if it cannot be measured
        foreach (HexTile n in any.neighbors) {
            if (n == null) continue;
            Vector3 d = n.transform.position - any.transform.position;
            if (n.x == any.x) h = Mathf.Abs(d.y);
            else if (Mathf.Abs(d.x) > 0.01f) w = Mathf.Abs(d.x) * 4f / 3f;
        }
        w *= set.hexScale; h *= set.hexScale;
        const float ppu = 32f;
        int W = Mathf.CeilToInt(w * ppu), H = Mathf.CeilToInt(h * ppu);
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float cx = W * 0.5f, cy = H * 0.5f, hw = w * ppu * 0.5f, hh = h * ppu * 0.5f;
        Color[] px = new Color[W * H];
        for (int y = 0; y < H; y++) {
            for (int x = 0; x < W; x++) {
                float fx = Mathf.Abs(x + 0.5f - cx), fy = Mathf.Abs(y + 0.5f - cy);
                // Flat top and bottom; the sides slant in from the points at mid height.
                bool inside = fy <= hh && fx <= hw - (hw * 0.5f) * (fy / hh);
                px[y * W + x] = inside ? Color.white : new Color(1f, 1f, 1f, 0f);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        builtIn = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), ppu);
        builtIn.name = "FogHex";
        return builtIn;
    }

    /// <summary>A cheap fingerprint of where every unit stands and who owns every building.</summary>
    static int Signature() {
        int h = 17;
        if (UnitController.uc != null)
            foreach (Unit u in UnitController.uc.units) {
                if (u == null) continue;
                HexTile t = u.tile;
                h = h * 31 + (t != null ? (t.x * 73856093) ^ (t.y * 19349663) : 7) + u.Strength;
            }
        for (int i = 0; i < Building.all.Count; i++) {
            Building b = Building.all[i];
            if (b != null) h = h * 31 + (b.owner != null ? 5 + (GameController.gc != null ? GameController.gc.players.IndexOf(b.owner) : 0) : 3);
        }
        if (GameController.gc != null) h = h * 31 + (GameController.gc.fogOfWar ? 1 : 0) + GameController.gc.days * 7;
        return h;
    }
}
