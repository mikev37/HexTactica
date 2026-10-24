using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fog of war on the board: every tile the screen's player (FogOfWar.Viewer) does not see is covered
/// with a dark grey hex (FogSettings: the picture, its colour and opacity, its sorting), fading in and
/// out as sight changes. Ground and buildings still show through, dimmed; buildings show their owner
/// as last seen (Building.RefreshFlag). Units are faded by their own FogFader.
///
/// The fog reaches one tile past the edge of the board, so the edge tiles' art (which reaches past
/// their hex) is covered like the rest. Those cells beyond the edge are cleared only while a unit
/// the viewer sees stands next to them (on an edge tile).
///
/// Spawned when a scene starts (not in the headless harness). It also keeps sight up to date: if
/// anything moved without saying so (FogOfWar.Changed), it notices from one frame to the next.
/// A board loaded from a file (<see cref="BoardLoaded"/>) shows its fog at once, without fading.
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

    /// <summary>A cell one step past the edge of the board: where it is, and the board tiles beside it.</summary>
    class Beyond { public int x, y; public List<HexTile> next = new List<HexTile>(); public Cover cover; }
    readonly List<Beyond> beyond = new List<Beyond>();
    readonly Dictionary<HexTile, List<Beyond>> beyondOf = new Dictionary<HexTile, List<Beyond>>();
    readonly HashSet<Beyond> cleared = new HashSet<Beyond>();
    int ringFor = -1;

    static bool snap;
    /// <summary>A board was just loaded (a scenario, a save): show its fog at once, without fading in.</summary>
    public static void BoardLoaded() { snap = true; }

    static readonly int[,] Steps = { { 1, -1 }, { 1, 0 }, { 0, 1 }, { -1, 1 }, { -1, 0 }, { 0, -1 } };
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
        if (!fog && covers.Count == 0 && !AnyBeyondShown()) { snap = false; return; }

        FogSettings set = FogSettings.Instance;
        bool instant = snap || Time.timeSinceLevelLoad < 0.5f;
        snap = false;
        float step = Time.deltaTime / Mathf.Max(0.01f, set.fadeSeconds);
        foreach (HexTile t in hg.tiles) {
            if (t == null) continue;
            float want = fog && !FogOfWar.TileVisible(viewer, t) ? 1f : 0f;
            Cover c;
            if (!covers.TryGetValue(t, out c) || c.sr == null) {
                if (want <= 0f) { covers.Remove(t); continue; }
                c = new Cover { sr = Make(t, set), alpha = instant ? 1f : 0f };
                covers[t] = c;
            }
            Fade(c, want, instant, step, set);
        }

        // One cell past the edge: fogged, unless a unit the viewer sees stands beside it.
        if (ringFor != hg.tiles.Count) BuildRing(hg);
        cleared.Clear();
        if (fog && UnitController.uc != null)
            foreach (Unit u in UnitController.uc.units) {
                if (u == null || u.tile == null || u.Strength <= 0 || (u.trasnporter != null && u.trasnporter != u)) continue;
                List<Beyond> by;
                if (!beyondOf.TryGetValue(u.tile, out by)) continue;
                if (u.owner != viewer && !FogOfWar.ShownToViewer(u)) continue;
                foreach (Beyond b in by) cleared.Add(b);
            }
        foreach (Beyond b in beyond) {
            float want = fog && !cleared.Contains(b) ? 1f : 0f;
            if (b.cover == null || b.cover.sr == null) {
                if (want <= 0f) continue;
                SpriteRenderer sr = MakeBeyond(b, set);
                if (sr == null) continue;
                b.cover = new Cover { sr = sr, alpha = instant ? 1f : 0f };
            }
            Fade(b.cover, want, instant, step, set);
        }
    }

    static void Fade(Cover c, float want, bool instant, float step, FogSettings set) {
        if (c.alpha == want && c.sr.enabled == (want > 0f)) return;
        c.alpha = instant ? want : Mathf.MoveTowards(c.alpha, want, step);
        Color col = set.fogColor;
        col.a *= c.alpha;
        c.sr.color = col;
        c.sr.enabled = c.alpha > 0.001f;
    }

    bool AnyBeyondShown() {
        foreach (Beyond b in beyond) if (b.cover != null && b.cover.sr != null && b.cover.sr.enabled) return true;
        return false;
    }

    /// <summary>Every cell one step off the board next to a board tile, and which board tiles are beside each.</summary>
    void BuildRing(HexGenerator hg) {
        ringFor = hg.tiles.Count;
        Dictionary<long, Beyond> at = new Dictionary<long, Beyond>();
        foreach (Beyond b in beyond) at[Key(b.x, b.y)] = b;   // keep the covers already made
        beyond.Clear(); beyondOf.Clear();
        HashSet<long> kept = new HashSet<long>();
        foreach (HexTile t in hg.tiles) {
            if (t == null) continue;
            for (int i = 0; i < 6; i++) {
                int x = t.x + Steps[i, 0], y = t.y + Steps[i, 1];
                if (hg.get(x, y, -x - y) != null) continue;
                long k = Key(x, y);
                Beyond b;
                if (!at.TryGetValue(k, out b)) { b = new Beyond { x = x, y = y }; at[k] = b; }
                if (kept.Add(k)) { b.next.Clear(); beyond.Add(b); }
                if (!b.next.Contains(t)) b.next.Add(t);
                List<Beyond> by;
                if (!beyondOf.TryGetValue(t, out by)) { by = new List<Beyond>(); beyondOf[t] = by; }
                if (!by.Contains(b)) by.Add(b);
            }
        }
    }

    static long Key(int x, int y) { return ((long)x << 32) ^ (uint)y; }

    /// <summary>
    /// Where the board would put a tile at cube (x, y), as HexGenerator lays them out (column x, row
    /// from y and z), from the board's origin.
    /// </summary>
    static Vector3 LayoutPosition(int x, int y) {
        int z = -x - y;
        int j = z + (x - (x & 1)) / 2;
        float offset = x % 2 == 0 ? 3.25f : 0f;
        return new Vector3(x * 4f * 2f / 1.5f, j * 3.25f * 2f - offset, 0f);
    }

    /// <summary>A cover for a cell past the edge: like a board tile's, at the place the board would put a tile there.</summary>
    SpriteRenderer MakeBeyond(Beyond b, FogSettings set) {
        HexTile n = b.next.Count > 0 ? b.next[0] : null;
        if (n == null) return null;
        // One step in the same direction on the board, measured where the tiles really are (a step in
        // a given direction is the same everywhere on a hex grid); worked out from the layout if the
        // tile behind is missing too.
        int dx = b.x - n.x, dy = b.y - n.y;
        HexTile back = HexGenerator.hg != null ? HexGenerator.hg.get(n.x - dx, n.y - dy, -(n.x - dx) - (n.y - dy)) : null;
        Vector3 shift = back != null ? n.transform.position - back.transform.position : LayoutPosition(b.x, b.y) - LayoutPosition(n.x, n.y);
        Transform own = n.transform.Find("Fog");
        SpriteRenderer mine = own != null ? own.GetComponent<SpriteRenderer>() : null;
        SpriteRenderer sr;
        if (mine != null) {
            GameObject copy = GameObject.Instantiate(own.gameObject, transform);
            copy.name = "Fog beyond (" + b.x + "," + b.y + ")";
            copy.transform.position = own.position + shift;
            sr = copy.GetComponent<SpriteRenderer>();
        } else {
            GameObject g = new GameObject("Fog beyond (" + b.x + "," + b.y + ")");
            g.transform.SetParent(transform, false);
            sr = g.AddComponent<SpriteRenderer>();
            if (set.tileSprite != null) { g.transform.position = n.transform.position + shift; sr.sprite = set.tileSprite; }
            else { g.transform.position = n.transform.position + shift + new Vector3(set.hexOffset.x, set.hexOffset.y, 0f); sr.sprite = BuiltIn(n, set); }
            if (!string.IsNullOrEmpty(set.sortingLayer)) sr.sortingLayerName = set.sortingLayer;
            sr.sortingOrder = set.sortingOrder;
        }
        sr.color = new Color(0f, 0f, 0f, 0f);
        return sr;
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
