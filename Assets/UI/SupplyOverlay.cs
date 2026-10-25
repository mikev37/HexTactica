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
        if (carried.Count > 0) DrawSupplyLine(p, from, field, preview, carried);
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
    void DrawSupplyLine(Player p, HexTile from, SupplyNetwork.Result field, Dictionary<SupplyPoint, HexTile> preview, List<SupplyPoint> carried) {
        List<HexTile> path = SourcePath(p, from, field, preview);
        Show(Line(0), path, p.displayColor, 1f, 1f);
        // What hangs on it: every supply unit linked only through it, with its line back to it, fainter.
        int n = 1;
        foreach (KeyValuePair<SupplyPoint, List<HexTile>> kv in Dependents(p, carried, from, field, preview))
            Show(Line(n++), kv.Value, p.displayColor, DependentAlpha, DependentWidth);
    }

    /// <summary>Opacity and width (shares of the main line's) of the lines from the supply units that depend on the one selected.</summary>
    public const float DependentAlpha = 0.45f, DependentWidth = 0.6f;

    void Show(LineRenderer lr, List<HexTile> path, Color c, float alpha, float width) {
        List<Vector3> points = new List<Vector3>();
        foreach (HexTile t in path) Add(points, t);
        if (points.Count < 2) return;
        lr.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) lr.SetPosition(i, points[i]);
        Tint(lr, c, alpha);
        if (baseWidth < 0f) baseWidth = lr.widthMultiplier;
        lr.widthMultiplier = baseWidth * width;
        lr.gameObject.SetActive(true);
    }
    float baseWidth = -1f;

    /// <summary>
    /// The supply units that depend on <paramref name="carried"/> (the one selected, or all a selected
    /// transport carries): linked now (or in the move previewed), cut off without them. Each with its
    /// line back as far as the selected unit (<paramref name="at"/>, where it stands or is to move to).
    /// </summary>
    public static Dictionary<SupplyPoint, List<HexTile>> Dependents(Player p, List<SupplyPoint> carried, HexTile at, SupplyNetwork.Result field, Dictionary<SupplyPoint, HexTile> preview) {
        Dictionary<SupplyPoint, List<HexTile>> deps = new Dictionary<SupplyPoint, List<HexTile>>();
        if (carried == null || carried.Count == 0) return deps;
        SupplyNetwork.Result without = SolveWithout(p, new HashSet<SupplyPoint>(carried), preview);
        List<SupplyPoint> linked = new List<SupplyPoint>(field.connectedHubs);
        foreach (SupplyPoint h in linked) {
            if (h == null || h.producer || carried.Contains(h) || without.connectedHubs.Contains(h) || h.myUnit == null) continue;
            HexTile ht;
            if (preview == null || !preview.TryGetValue(h, out ht)) ht = h.myUnit.tile;
            List<HexTile> line = SourcePath(p, ht, field, preview);
            int cut = line.IndexOf(at);
            if (cut >= 0) line.RemoveRange(cut + 1, line.Count - cut - 1);
            deps[h] = line;
        }
        return deps;
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
            Unit carrier = source.myUnit != null ? source.myUnit.trasnporter : null;
            if (carrier != null) {
                // Aboard: if its transport can drive straight to ground the network supplies without
                // any supply unit that is aboard something, that is its link (rather than through a
                // neighbouring transport's supply unit that itself links by driving back past it).
                HashSet<SupplyPoint> ground = new HashSet<SupplyPoint>(seen);
                foreach (Unit u in p.units) { SupplyPoint sp = u != null ? u.Hub : null; if (sp != null && u.trasnporter != null) ground.Add(sp); }
                List<HexTile> direct = DriveToSupply(carrier, top, SolveWithout(p, ground, preview));
                if (direct != null && Linked(p, source, preview)) {
                    for (int i = 1; i < direct.Count; i++) points.Add(direct[i]);
                    f = SolveWithout(p, seen, preview);
                    at = direct[direct.Count - 1];
                    continue;
                }
            }
            // What feeds it: the network without it and without every hub already on the line
            // (else the next hub's field, which reaches back over this one, would lead the line
            // back to it: two hubs drawn feeding each other, neither reaching the HQ).
            f = SolveWithout(p, seen, preview);
            if (f.DepthAt(top) <= 0) {
                // Aboard a transport it links over the transport's move (SupplyNetwork): the line
                // follows the way the transport would drive to supplied ground (or beside it) and
                // carries on from there.
                Unit transport = source.myUnit != null ? source.myUnit.trasnporter : null;
                if (transport == null || !Linked(p, source, preview)) break;   // stranded: it feeds itself
                List<HexTile> drive = DriveToSupply(transport, top, f);
                if (drive == null) break;
                for (int i = 1; i < drive.Count; i++) points.Add(drive[i]);
                top = drive[drive.Count - 1];
            }
            at = top;
        }
        if (preview != null) SupplyNetwork.SolvePreview(p, preview);   // the preview stays on the board
        else SupplyNetwork.Solve(p, false);
        return points;
    }

    /// <summary>
    /// The way <paramref name="transport"/>, standing on <paramref name="from"/>, would drive in one move
    /// to ground supplied in <paramref name="f"/> (or beside it, where its cargo can step off): the tiles
    /// from <paramref name="from"/> to the nearest such tile (fewest movement points, then the board's
    /// order), then the supplied tile beside it if it is not supplied itself. Planned as its side knows
    /// the board (fog of war): around the enemies it sees, never through them. Null if there is none.
    /// </summary>
    static List<HexTile> DriveToSupply(Unit transport, HexTile from, SupplyNetwork.Result f) {
        HexTactica.AI.MoveField field;
        using (FogOfWar.Concealing(transport.owner))
            field = HexTactica.AI.AIPath.ThisTurnFrom(transport, from, transport.moveRange);
        HexTile best = null, step = null; int bestCost = int.MaxValue;
        foreach (HexTile t in field.Tiles) {
            if (!field.ReachableThisTurn(t) && t != from) continue;
            HexTile onto = null;
            if (f.DepthAt(t) > 0) onto = t;
            else foreach (HexTile n in t.neighbors) if (n != null && f.DepthAt(n) > 0 && (onto == null || CombatMath.Precedes(n, onto))) onto = n;
            if (onto == null) continue;
            int cost = t == from ? 0 : field.Get(t).used;
            if (best == null || cost < bestCost || (cost == bestCost && CombatMath.Precedes(t, best))) { best = t; bestCost = cost; step = onto; }
        }
        List<HexTile> path = null;
        if (best != null) {
            path = best == from ? new List<HexTile> { from } : field.PathTo(best);
            if (path.Count == 0) path.Add(from);
            if (step != best) path.Add(step);
        }
        HexTactica.AI.AIPath.Release(field);
        return path;
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
    void Tint(LineRenderer lr, Color player, float alpha = 1f) {
        if (prefabGradient == null) return;
        GradientColorKey[] keys = prefabGradient.colorKeys;
        for (int k = 0; k < keys.Length; k++) {
            Color c = keys[k].color;
            keys[k].color = new Color(c.r * player.r, c.g * player.g, c.b * player.b, 1f);
        }
        GradientAlphaKey[] alphas = prefabGradient.alphaKeys;
        for (int k = 0; k < alphas.Length; k++) alphas[k].alpha *= alpha;
        Gradient g = new Gradient();
        g.SetKeys(keys, alphas);
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
