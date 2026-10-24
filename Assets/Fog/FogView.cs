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
        GameObject g = new GameObject("Fog");
        g.transform.SetParent(t.transform, false);
        g.transform.localPosition = Vector3.zero;
        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = set.tileSprite != null ? set.tileSprite : BuiltIn();
        if (!string.IsNullOrEmpty(set.sortingLayer)) sr.sortingLayerName = set.sortingLayer;
        sr.sortingOrder = set.sortingOrder;
        sr.color = new Color(0f, 0f, 0f, 0f);
        return sr;
    }

    /// <summary>
    /// A flat-topped hex on the terrain tiles' canvas (1024x1280 at 100 pixels per unit, pivot in the
    /// middle), drawn at a quarter of the size: it covers a tile's ground exactly as its sprites do,
    /// a little larger so neighbouring hexes meet.
    /// </summary>
    Sprite BuiltIn() {
        if (builtIn != null) return builtIn;
        const int W = 256, H = 320;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        // The ground's hex in the terrain art: x 44..980, y 107..829 from the top of 1280 (quartered).
        float left = 11f - 2f, right = 245f + 2f, top = H - 27f + 2f, bottom = H - 207f - 2f;
        float midY = (top + bottom) * 0.5f, inset = (right - left) * 0.25f;
        Color[] px = new Color[W * H];
        for (int y = 0; y < H; y++) {
            for (int x = 0; x < W; x++) {
                float fx = x + 0.5f, fy = y + 0.5f;
                bool inside = fy >= bottom && fy <= top;
                if (inside) {
                    // The slanted sides: from the left and right points at mid height to the corners.
                    float k = Mathf.Abs(fy - midY) / Mathf.Max(1f, top - midY);
                    float l = left + inset * k, r = right - inset * k;
                    inside = fx >= l && fx <= r;
                }
                px[y * W + x] = inside ? Color.white : new Color(1f, 1f, 1f, 0f);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        builtIn = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 25f);
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
            if (b != null) h = h * 31 + (b.owner != null ? b.owner.GetInstanceID() : 3);
        }
        if (GameController.gc != null) h = h * 31 + (GameController.gc.fogOfWar ? 1 : 0) + GameController.gc.days * 7;
        return h;
    }
}
