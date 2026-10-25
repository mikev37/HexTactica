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
    Player previewPlayer;
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
        // Only for a human's unit: an AI's (it selects each unit as it acts) is not the player's
        // business, and the AI may be thinking on another thread while this would publish supply.
        if (u != null && !FogOfWar.IsHuman(u.owner)) u = null;
        if (u != null && (u.tile == null || u.owner == null)) u = null;
        HexTile dest = Destination(u);
        if (u == shownUnit && dest == shownTile) return;
        Clear();
        shownUnit = u;
        shownTile = dest;
        if (u == null) return;

        Player p = u.owner;
        List<SupplyPoint> carried = CarriedSupplies(u);
        bool movingHub = carried.Count > 0 && dest != null;
        // The field to read: supply units being moved (itself, or all a transport carries) show the
        // supply they would give from there (published, so the board's supply edges show it);
        // anything else reads the real one.
        Dictionary<SupplyPoint, HexTile> preview = null;
        if (movingHub) { preview = new Dictionary<SupplyPoint, HexTile>(); foreach (SupplyPoint sp in carried) preview[sp] = dest; }
        SupplyNetwork.Result field = movingHub ? SupplyNetwork.SolvePreview(p, preview) : SupplyNetwork.Solve(p, false);
        if (movingHub) previewPlayer = p;

        HexTile from = dest ?? u.tile;
        // Leaving supply: supplied where it stands, not where it is going.
        if (dest != null && field.DepthAt(u.tile) > 0 && field.DepthAt(dest) <= 0 && !movingHub) {
            if (dest.overlay != null) { warnedTile = dest; warnedColour = dest.overlay.color; dest.overlay.color = new Color(1f, 0.55f, 0.1f, 1f); }
            if (u.textArea != null) { u.textArea.text = "Leaves supply"; warnedText = true; }
        }
        // The line is for supply units (and what carries them): where their supply comes from.
        if (carried.Count > 0) DrawSupplyLine(p, from, field, preview);
    }

    /// <summary>The supply units that move when <paramref name="u"/> does: itself, or all it carries (and what they carry).</summary>
    public static List<SupplyPoint> CarriedSupplies(Unit u) {
        List<SupplyPoint> list = new List<SupplyPoint>();
        SupplyPoint own = u.Hub;
        if (own != null) { if (!own.producer) list.Add(own); return list; }
        foreach (Unit c in u.transported) {
            if (c == null) continue;
            foreach (SupplyPoint sp in CarriedSupplies(c)) if (!list.Contains(sp)) list.Add(sp);
        }
        return list;
    }

    void Clear() {
        foreach (LineRenderer l in lines) if (l != null) l.gameObject.SetActive(false);
        if (previewPlayer != null) SupplyNetwork.Solve(previewPlayer, false);   // the real field back
        previewPlayer = null;
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
    void DrawSupplyLine(Player p, HexTile from, SupplyNetwork.Result field, Dictionary<SupplyPoint, HexTile> preview) {
        List<HexTile> path = SourcePath(p, from, field, preview);
        List<Vector3> points = new List<Vector3>();
        foreach (HexTile t in path) Add(points, t);
        if (points.Count < 2) return;
        LineRenderer lr = Line(0);
        lr.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) lr.SetPosition(i, points[i]);
        Tint(lr, p.displayColor);
        lr.gameObject.SetActive(true);
    }

    /// <summary>
    /// The hexes supply comes along to <paramref name="from"/>: up the field to the source feeding
    /// it, then on through each supply unit to what feeds that, ending at a building (the HQ) or at
    /// a supply unit that is cut off. <paramref name="moved"/> standing on <paramref name="movedTo"/>
    /// (a move being previewed), if given. Leaves the real field (or that preview) on the board.
    /// </summary>
    public static List<HexTile> SourcePath(Player p, HexTile from, SupplyNetwork.Result field, SupplyPoint moved, HexTile movedTo) {
        Dictionary<SupplyPoint, HexTile> preview = null;
        if (moved != null && movedTo != null) preview = new Dictionary<SupplyPoint, HexTile> { { moved, movedTo } };
        return SourcePath(p, from, field, preview);
    }

    /// <summary>As above, with the supply units in <paramref name="preview"/> standing where it says (a move being previewed), if given.</summary>
    public static List<HexTile> SourcePath(Player p, HexTile from, SupplyNetwork.Result field, Dictionary<SupplyPoint, HexTile> preview) {
        List<HexTile> points = new List<HexTile>();
        if (from == null || field.DepthAt(from) <= 0) return points;
        HashSet<SupplyPoint> seen = new HashSet<SupplyPoint>();
        HexTile at = from;
        SupplyNetwork.Result f = field;
        int guard = 0;
        while (at != null && guard++ < 16) {
            HexTile top = Climb(at, f, points);
            // Several supply units can share a tile (all aboard one transport): each in turn.
            SupplyPoint source = HubOn(p, top, preview, seen);
            if (source == null || source.producer) break;   // a building or the HQ: the root
            seen.Add(source);
            // What feeds it: the network without it and without every hub already on the line
            // (else the next hub's field, which reaches back over this one, would lead the line
            // back to it: two hubs drawn feeding each other, neither reaching the HQ).
            f = SolveWithout(p, seen, preview);
            if (f.DepthAt(top) <= 0) {
                // Aboard a transport it links over the transport's move (SupplyNetwork): the line
                // jumps to the nearest supplied ground and carries on from there.
                HexTile near = null;
                if (source.myUnit != null && source.myUnit.trasnporter != null)
                    foreach (KeyValuePair<HexTile, int> kv in f.depth)
                        if (kv.Value > 0 && (near == null || kv.Key.Distance(top) < near.Distance(top) || (kv.Key.Distance(top) == near.Distance(top) && CombatMath.Precedes(kv.Key, near)))) near = kv.Key;
                if (near == null || !Linked(p, source, preview)) break;   // stranded: it feeds itself
                top = near;
            }
            at = top;
        }
        if (preview != null) SupplyNetwork.SolvePreview(p, preview);   // the preview stays on the board
        else SupplyNetwork.Solve(p, false);
        return points;
    }

    static bool Linked(Player p, SupplyPoint hub, Dictionary<SupplyPoint, HexTile> preview) {
        SupplyNetwork.Result r = preview != null ? SupplyNetwork.SolveMoved(p, preview) : SupplyNetwork.Solve(p, false);
        return r.connectedHubs.Contains(hub);
    }

    /// <summary>The network without the hubs in <paramref name="without"/> (and with those in <paramref name="preview"/> where it says, if given).</summary>
    public static SupplyNetwork.Result SolveWithout(Player p, HashSet<SupplyPoint> without, Dictionary<SupplyPoint, HexTile> preview) {
        Dictionary<SupplyPoint, HexTile> at = new Dictionary<SupplyPoint, HexTile>();
        foreach (Unit u in p.units) {
            SupplyPoint sp = u != null ? u.Hub : null;
            if (sp == null || without.Contains(sp) || u.tile == null) continue;
            HexTile to;
            at[sp] = preview != null && preview.TryGetValue(sp, out to) ? to : u.tile;
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

    /// <summary>
    /// A supply unit standing on <paramref name="t"/> (or being moved to it in <paramref name="preview"/>)
    /// not already on the line (<paramref name="seen"/>), if any. Several can share a tile: all aboard
    /// one transport.
    /// </summary>
    static SupplyPoint HubOn(Player p, HexTile t, Dictionary<SupplyPoint, HexTile> preview, HashSet<SupplyPoint> seen) {
        if (t == null) return null;
        if (preview != null) foreach (KeyValuePair<SupplyPoint, HexTile> kv in preview) if (kv.Value == t && !seen.Contains(kv.Key)) return kv.Key;
        foreach (Unit u in p.units) {
            if (u == null || u.tile != t) continue;
            SupplyPoint sp = u.Hub;
            if (sp != null && (preview == null || !preview.ContainsKey(sp)) && !seen.Contains(sp)) return sp;
        }
        return null;
    }

    /// <summary>The line's look: Assets/Resources/SupplyLine.prefab (width, colours, material, caps, sorting).</summary>
    public const string LinePrefab = "SupplyLine";
    Gradient prefabGradient;

    /// <summary>The prefab's colours times the player's: its gradient sets opacity and fade, the player the hue.</summary>
    void Tint(LineRenderer lr, Color player) {
        if (prefabGradient == null) return;
        GradientColorKey[] keys = prefabGradient.colorKeys;
        for (int k = 0; k < keys.Length; k++) {
            Color c = keys[k].color;
            keys[k].color = new Color(c.r * player.r, c.g * player.g, c.b * player.b, 1f);
        }
        Gradient g = new Gradient();
        g.SetKeys(keys, prefabGradient.alphaKeys);
        lr.colorGradient = g;
    }

    LineRenderer Line(int i) {
        while (lines.Count <= i) {
            LineRenderer template = Resources.Load<LineRenderer>(LinePrefab);
            if (template != null) {
                LineRenderer made = GameObject.Instantiate(template, transform);
                made.name = "SupplyLine" + lines.Count;
                made.useWorldSpace = true;
                if (prefabGradient == null) prefabGradient = template.colorGradient;
                made.gameObject.SetActive(false);
                lines.Add(made);
                continue;
            }
            // No prefab: a plain line in code.
            GameObject g = new GameObject("SupplyLine" + lines.Count);
            g.transform.SetParent(transform, false);
            LineRenderer lr = g.AddComponent<LineRenderer>();
            if (prefabGradient == null) {
                prefabGradient = new Gradient();
                prefabGradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                       new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 1f) });
            }
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
        // Under fog of war, worked out on the board as the viewer knows it: the enemy's units the
        // viewer does not see are not there (neither blocking nor taking a tile).
        using (FogOfWar.Concealing(FogOfWar.Viewer))
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
