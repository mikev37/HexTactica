using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Supply and threat read-outs on the board (installed with the BuildingController).
///
/// With one of your supply units (or a transport carrying one) selected:
///   - a line along the hexes from where it stands (or from the tile picked to move to) back to
///     where its supply comes from: the supply unit or building feeding it, then what feeds that,
///     and so on back to the HQ;
///   - once a tile is picked to move to (the first tap or click on it), the supply it would give
///     from there is shown on the board in place of the real supply, until the move is made or
///     another tile is picked.
/// With any of your units selected, a picked tile that would take it out of supply says so on the
/// unit ("Leaves supply") and is tinted.
/// Everything follows the picked tile, not the pointer, so it works the same with a finger.
/// Clicking an enemy unit shows where it can move (its colour) and what it can hit from there.
/// </summary>
public class SupplyOverlay : MonoBehaviour {

    public static SupplyOverlay instance;

    readonly List<LineRenderer> lines = new List<LineRenderer>();
    Material lineMaterial;

    // What is shown now, to redo it only when something changes.
    Unit shownUnit;
    HexTile shownTile;
    SupplyPoint previewHub;
    HexTile warnedTile;
    Color warnedColour;
    bool warnedText;
    int shownFrameCheck;

    void Awake() { instance = this; }

    /// <summary>The tile picked for the selected unit to move to (first click or tap on it), if any.</summary>
    static HexTile Destination(Unit u) {
        if (u == null || u.tile == null) return null;
        HexTile picked = null;
        if (HexGenerator.hg != null)
            foreach (HexTile t in HexGenerator.hg.tiles) if (t != null && t.state == UIState.MOVE_CONFIRM) { picked = t; break; }
        return picked;
    }

    void Update() {
        GameController gc = GameController.gc;
        Unit u = gc != null ? gc.activeUnit : null;
        if (u != null && (u.tile == null || u.owner == null)) u = null;
        HexTile dest = Destination(u);
        if (u == shownUnit && dest == shownTile) return;
        Clear();
        shownUnit = u;
        shownTile = dest;
        if (u == null) return;

        Player p = u.owner;
        SupplyPoint hub = CarriedSupply(u);
        bool movingHub = hub != null && dest != null;
        // The field to read: a supply unit being moved shows the supply it would give from there
        // (published, so the board's supply edges show it); anything else reads the real one.
        SupplyNetwork.Result field = movingHub ? SupplyNetwork.Solve(p, false, hub, dest) : SupplyNetwork.Solve(p, false);
        if (movingHub) previewHub = hub;

        HexTile from = dest ?? u.tile;
        // Leaving supply: supplied where it stands, not where it is going.
        if (dest != null && field.DepthAt(u.tile) > 0 && field.DepthAt(dest) <= 0 && !movingHub) {
            if (dest.overlay != null) { warnedTile = dest; warnedColour = dest.overlay.color; dest.overlay.color = new Color(1f, 0.55f, 0.1f, 1f); }
            if (u.textArea != null) { u.textArea.text = "Leaves supply"; warnedText = true; }
        }
        // The line is for supply units (and what carries them): where their supply comes from.
        if (hub != null) DrawSupplyLine(p, from, field, movingHub ? hub : null, dest);
    }

    /// <summary>The supply unit that moves when <paramref name="u"/> does: itself, or one it carries.</summary>
    static SupplyPoint CarriedSupply(Unit u) {
        SupplyPoint own = u.GetComponent<SupplyPoint>();
        if (own != null) return own.producer ? null : own;
        foreach (Unit c in u.transported) {
            SupplyPoint sp = c != null ? c.GetComponent<SupplyPoint>() : null;
            if (sp != null && !sp.producer) return sp;
        }
        return null;
    }

    void Clear() {
        foreach (LineRenderer l in lines) if (l != null) l.gameObject.SetActive(false);
        if (previewHub != null && previewHub.myUnit != null) SupplyNetwork.Solve(previewHub.myUnit.owner, false);   // the real field back
        previewHub = null;
        if (warnedTile != null && warnedTile.overlay != null) warnedTile.overlay.color = warnedColour;
        warnedTile = null;
        if (warnedText && shownUnit != null && shownUnit.textArea != null) shownUnit.textArea.text = "";
        warnedText = false;
    }

    /// <summary>
    /// From <paramref name="from"/> uphill in the supply field to the source that feeds it (the
    /// field falls by distance from each source, so the steepest way up is the way supply came);
    /// from a supply unit, on to whatever feeds it (the field without it); and so on to the HQ.
    /// </summary>
    void DrawSupplyLine(Player p, HexTile from, SupplyNetwork.Result field, SupplyPoint moved, HexTile movedTo) {
        List<HexTile> path = SourcePath(p, from, field, moved, movedTo);
        List<Vector3> points = new List<Vector3>();
        foreach (HexTile t in path) Add(points, t);
        if (points.Count < 2) return;
        LineRenderer lr = Line(0);
        lr.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) lr.SetPosition(i, points[i]);
        Color c = p.displayColor; c.a = 0.9f;
        lr.startColor = c; lr.endColor = new Color(c.r, c.g, c.b, 0.5f);
        lr.gameObject.SetActive(true);
    }

    /// <summary>
    /// The hexes supply comes along to <paramref name="from"/>: up the field to the source feeding
    /// it, then on through each supply unit to what feeds that, ending at a building (the HQ) or at
    /// a supply unit that is cut off. <paramref name="moved"/> standing on <paramref name="movedTo"/>
    /// (a move being previewed), if given. Leaves the real field (or that preview) on the board.
    /// </summary>
    public static List<HexTile> SourcePath(Player p, HexTile from, SupplyNetwork.Result field, SupplyPoint moved, HexTile movedTo) {
        List<HexTile> points = new List<HexTile>();
        if (from == null || field.DepthAt(from) <= 0) return points;
        HashSet<SupplyPoint> seen = new HashSet<SupplyPoint>();
        HexTile at = from;
        SupplyNetwork.Result f = field;
        int guard = 0;
        while (at != null && guard++ < 16) {
            HexTile top = Climb(at, f, points);
            SupplyPoint source = HubOn(p, top, moved, movedTo);
            if (source == null || source.producer || seen.Contains(source)) break;   // a building or the HQ: the root (or a loop)
            seen.Add(source);
            f = moved != null && source != moved ? SolveWithout(p, source, moved, movedTo) : SupplyNetwork.SolveWithout(p, source);
            if (f.DepthAt(top) <= 0) {
                // Aboard a transport it links over the transport's move (SupplyNetwork): the line
                // jumps to the nearest supplied ground and carries on from there.
                HexTile near = null;
                if (source.myUnit != null && source.myUnit.trasnporter != null)
                    foreach (KeyValuePair<HexTile, int> kv in f.depth)
                        if (kv.Value > 0 && (near == null || kv.Key.Distance(top) < near.Distance(top) || (kv.Key.Distance(top) == near.Distance(top) && CombatMath.Precedes(kv.Key, near)))) near = kv.Key;
                if (near == null || !Linked(p, source, moved, movedTo)) break;   // stranded: it feeds itself
                top = near;
            }
            at = top;
        }
        if (moved != null) SupplyNetwork.Solve(p, false, moved, movedTo);   // the preview stays on the board
        else SupplyNetwork.Solve(p, false);
        return points;
    }

    static bool Linked(Player p, SupplyPoint hub, SupplyPoint moved, HexTile movedTo) {
        SupplyNetwork.Result r = moved != null ? SupplyNetwork.Solve(p, false, moved, movedTo) : SupplyNetwork.Solve(p, false);
        return r.connectedHubs.Contains(hub);
    }

    static SupplyNetwork.Result SolveWithout(Player p, SupplyPoint without, SupplyPoint moved, HexTile movedTo) {
        Dictionary<SupplyPoint, HexTile> at = new Dictionary<SupplyPoint, HexTile>();
        foreach (Unit u in p.units) {
            SupplyPoint sp = u != null ? u.GetComponent<SupplyPoint>() : null;
            if (sp == null || sp == without || u.tile == null) continue;
            at[sp] = sp == moved ? movedTo : u.tile;
        }
        return SupplyNetwork.SolvePlanned(p, at);
    }

    /// <summary>Walk uphill from <paramref name="t"/> to a local top of the field, adding each tile's centre.</summary>
    static HexTile Climb(HexTile t, SupplyNetwork.Result f, List<HexTile> points) {
        if (points.Count == 0 || points[points.Count - 1] != t) points.Add(t);
        int guard = 0;
        while (guard++ < 64) {
            HexTile next = null;
            int best = f.DepthAt(t);
            foreach (HexTile n in t.neighbors) {
                if (n == null) continue;
                int d = f.DepthAt(n);
                if (d <= f.DepthAt(t)) continue;
                if (d > best || (d == best && CombatMath.Precedes(n, next))) { best = d; next = n; }
            }
            if (next == null) return t;
            t = next;
            points.Add(t);
        }
        return t;
    }

    static void Add(List<Vector3> points, HexTile t) {
        Vector3 v = t.unitPlacement != null ? t.unitPlacement.position : t.transform.position;
        v.z = -9f;
        if (points.Count == 0 || points[points.Count - 1] != v) points.Add(v);
    }

    /// <summary>The supply unit standing on <paramref name="t"/> (or being moved to it), if any.</summary>
    static SupplyPoint HubOn(Player p, HexTile t, SupplyPoint moved, HexTile movedTo) {
        if (t == null) return null;
        if (moved != null && t == movedTo) return moved;
        foreach (Unit u in p.units) {
            if (u == null || u.tile != t) continue;
            SupplyPoint sp = u.GetComponent<SupplyPoint>();
            if (sp != null && sp != moved) return sp;
        }
        return null;
    }

    LineRenderer Line(int i) {
        while (lines.Count <= i) {
            GameObject g = new GameObject("SupplyLine" + lines.Count);
            g.transform.SetParent(transform, false);
            LineRenderer lr = g.AddComponent<LineRenderer>();
            if (lineMaterial == null) {
                Shader s = Shader.Find("Sprites/Default");
                if (s != null) lineMaterial = new Material(s);
            }
            if (lineMaterial != null) lr.material = lineMaterial;
            lr.widthMultiplier = 0.18f;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 2;
            lr.useWorldSpace = true;
            lr.sortingLayerName = "UI";
            lr.sortingOrder = 50;
            g.SetActive(false);
            lines.Add(lr);
        }
        return lines[i];
    }

    // ------------------------------------------------------------------ enemy move and threat

    /// <summary>Is an enemy's move and threat on show (tiles in ENEMY_MOVE / ENEMY_THREAT)?</summary>
    public static bool enemyShown;

    /// <summary>Show where <paramref name="enemy"/> can move this coming turn and every tile it could fire on.</summary>
    public static void ShowEnemy(Unit enemy) {
        if (enemy == null || enemy.tile == null) return;
        HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
        List<HexTile> moves = new List<HexTile>();
        foreach (HexTile t in enemy.tile.CheckMovementRange(enemy)) if (t != null && (t == enemy.tile || (t.G != 0 && t.CanEndTurnHere(enemy)))) moves.Add(t);
        if (!moves.Contains(enemy.tile)) moves.Add(enemy.tile);
        HashSet<HexTile> threat = new HashSet<HexTile>();
        if (enemy.Firepower > 0) {
            if (enemy.moveAndAttack) foreach (HexTile m in moves) foreach (HexTile a in m.AllInAttackRange(enemy)) threat.Add(a);
            else foreach (HexTile a in enemy.tile.AllInAttackRange(enemy)) threat.Add(a);
        }
        HexGenerator.forAllTiles(x => { x.G = 0; x.parent = null; return 0; });
        foreach (HexTile t in threat) if (!moves.Contains(t)) t.ShowEnemy(UIState.ENEMY_THREAT, enemy.owner);
        foreach (HexTile t in moves) if (t != enemy.tile) t.ShowEnemy(UIState.ENEMY_MOVE, enemy.owner);
        enemyShown = true;
    }

    public static void HideEnemy() {
        if (!enemyShown) return;
        enemyShown = false;
        HexGenerator.forAllTiles(x => { if (x.state == UIState.ENEMY_MOVE || x.state == UIState.ENEMY_THREAT) x.Reset(); return 0; });
    }
}
