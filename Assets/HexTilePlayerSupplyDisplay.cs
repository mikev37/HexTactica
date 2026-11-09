using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HexTilePlayerSupplyDisplay : MonoBehaviour
{
    internal HexTile myTile;
    internal Player myPlayer;
    public SpriteRenderer up, upleft, upright, down, downleft, downright;
    // Start is called before the first frame update
    void Start()
    {
        Tint();
        up.sortingOrder = 5 + GameController.gc.players.IndexOf(myPlayer);
        down.sortingOrder = 5 + GameController.gc.players.IndexOf(myPlayer);
        upright.sortingOrder = 5 + GameController.gc.players.IndexOf(myPlayer);
        upleft.sortingOrder = 5 + GameController.gc.players.IndexOf(myPlayer);
        downleft.sortingOrder = 5 + GameController.gc.players.IndexOf(myPlayer);
        downright.sortingOrder = 5 + GameController.gc.players.IndexOf(myPlayer);
        enabled = false;   // nothing to do each frame: redrawn through the queue (Pump)
        Register();
        Queue();
    }

    // A border is redrawn only when it may have changed: the tiles whose supply (as shown) flipped when a field was
    // published, and their neighbours (WakeAround); every border when the board is loaded or the fog's sight moves
    // (WakeAll, WakeOthers). Redraws are queued and done a few at a time (Pump, with a time budget per frame), so a
    // big change — a network cut, a turn's upkeep, the fog lifting — spreads over a few frames instead of stalling one.
    // The component is never enabled: Unity calls nothing on it between redraws.
    static readonly List<HexTilePlayerSupplyDisplay> all = new List<HexTilePlayerSupplyDisplay>();
    static readonly Dictionary<HexTile, List<HexTilePlayerSupplyDisplay>> byTile = new Dictionary<HexTile, List<HexTilePlayerSupplyDisplay>>();
    static readonly Queue<HexTilePlayerSupplyDisplay> queue = new Queue<HexTilePlayerSupplyDisplay>();
    bool queued, registered;

    /// <summary>Milliseconds of supply-border redrawing per frame at most (the rest waits for the next frame).</summary>
    public static float budgetMs = 2f;
    /// <summary>Borders redrawn each frame whatever the time (so a big change always moves on).</summary>
    public const int minPerFrame = 48;

    void Register() {
        if (registered || myTile == null) return;
        registered = true;
        if (!all.Contains(this)) all.Add(this);
        List<HexTilePlayerSupplyDisplay> l;
        if (!byTile.TryGetValue(myTile, out l)) byTile[myTile] = l = new List<HexTilePlayerSupplyDisplay>();
        if (!l.Contains(this)) l.Add(this);
    }

    void Queue() {
        if (queued || this == null) return;
        queued = true;
        queue.Enqueue(this);
        SupplyBorderPump.Ensure();
    }

    /// <summary>Redraw every supply border (spread over the next frames): a board was loaded, or the viewer changed.</summary>
    public static void WakeAll() {
        for (int i = all.Count - 1; i >= 0; i--) {
            if (all[i] == null) { all.RemoveAt(i); continue; }
            all[i].Queue();
        }
    }

    /// <summary>Redraw the borders of every side but <paramref name="viewer"/>'s (what the fog's sight decides; its own are always drawn).</summary>
    public static void WakeOthers(Player viewer) {
        for (int i = all.Count - 1; i >= 0; i--) {
            if (all[i] == null) { all.RemoveAt(i); continue; }
            if (viewer == null || all[i].myPlayer != viewer) all[i].Queue();
        }
    }

    /// <summary>Redraw <paramref name="player"/>'s borders on <paramref name="tiles"/> and the tiles beside them (their shown supply changed).</summary>
    public static void WakeAround(List<HexTile> tiles, Player player) {
        if (tiles == null) return;
        for (int i = 0; i < tiles.Count; i++) {
            HexTile t = tiles[i];
            if (t == null) continue;
            WakeAt(t, player);
            List<HexTile> n = t.neighbors;
            if (n != null) for (int k = 0; k < n.Count; k++) if (n[k] != null) WakeAt(n[k], player);
        }
    }

    static void WakeAt(HexTile t, Player player) {
        List<HexTilePlayerSupplyDisplay> l;
        if (!byTile.TryGetValue(t, out l)) return;
        for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].myPlayer == player) l[i].Queue();
    }

    /// <summary>Redraw queued borders until the frame's budget is spent (SupplyBorderPump calls it each frame).</summary>
    public static void Pump() {
        if (queue.Count == 0) return;
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        int done = 0;
        while (queue.Count > 0 && (done < minPerFrame || sw.Elapsed.TotalMilliseconds < budgetMs)) {
            HexTilePlayerSupplyDisplay d = queue.Dequeue();
            if (d == null) continue;
            d.queued = false;
            d.Redraw();
            done++;
        }
    }

    void OnDestroy() {
        all.Remove(this);
        List<HexTilePlayerSupplyDisplay> l;
        if (myTile != null && byTile.TryGetValue(myTile, out l)) l.Remove(this);
    }

    /// <summary>The border in its side's colour as it is now (a loaded scenario or the map editor may have changed it).</summary>
    void Tint() {
        Color c = new Color(myPlayer.displayColor.r, myPlayer.displayColor.g, myPlayer.displayColor.b, .7f);
        up.color = c; down.color = c; upright.color = c; upleft.color = c; downleft.color = c; downright.color = c;
    }

    void Redraw()
    {
        if (myTile == null || myPlayer == null) return;
        Tint();
        bool u = false, d = false, ur = false, ul = false, dl = false, dr = false;
        // Under fog of war an enemy's supply is drawn only on tiles the viewer sees (so it can be
        // cut), never in the fog.
        Player viewer = FogOfWar.Viewer;
        bool shown = viewer == null || viewer == myPlayer || FogOfWar.TileVisible(viewer, myTile);
        if (shown && myTile.isSuppliedShown(myPlayer)) {
            List<HexTile> neighbors = myTile.neighbors;
            for (int i = 0; i < neighbors.Count; i++) {
                HexTile tile = neighbors[i];
                if (tile == null || tile.isSuppliedShown(myPlayer)) continue;
                if (tile.x == myTile.x) {
                    if (tile.z > myTile.z) u = true; else d = true;
                } else if (tile.x > myTile.x) {
                    if (tile.z == myTile.z) ur = true; else dr = true;
                } else { //tile.x < x
                    if (tile.y == myTile.y) ul = true; else dl = true;
                }
            }
        }
        Show(up, u); Show(down, d); Show(upright, ur); Show(upleft, ul); Show(downleft, dl); Show(downright, dr);
    }

    static void Show(SpriteRenderer r, bool on) {
        if (r != null && r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
    }
}

