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
///     another tile is picked;
///   - a transport whose supply is cut off: the cut supply line (its own prefab, SupplyLineCut) from it to the source that
///     should feed it (SupplyNetwork.CutRoute), and the cut marker (SupplyContact) where it is cut. A transport that links
///     only round enemies it does not see (fog): the marker where they turn it (SupplyNetwork.UnseenContacts).
/// Neither the cut line nor a marker shows unless that transport is selected, or all supply lines are on: the option
/// (GameOptions.AllSupplyLines, kept between games), toggled by its key (FrontEndSettings.supplyLinesKey), shows every
/// supply line of the screen's side at once: each supply unit's to what feeds it, every cut one, every marker.
/// Whether a picked tile is in supply is shown by the board's supply borders alone.
/// Everything follows the picked tile, not the pointer, so it works the same with a finger.
/// Clicking an enemy unit shows where it can move (its colour) and what it can hit from there, and for an enemy supply
/// unit (or a transport carrying one) its supply line too, in its colour: the way its supply comes, as the network truly
/// has it, so you can see how your units turn it and where to cut it. Under fog, only as far as you see along it.
/// </summary>
public class SupplyOverlay : MonoBehaviour {

    public static SupplyOverlay instance;

    readonly List<LineRenderer> lines = new List<LineRenderer>();
    /// <summary>The enemy's supply line (ShowEnemy): apart from the selection's, which comes and goes with it.</summary>
    readonly List<LineRenderer> enemyLines = new List<LineRenderer>();
    /// <summary>Cut supply lines (the selection's, or all of them), and every supply line when all are on.</summary>
    readonly List<LineRenderer> cutLines = new List<LineRenderer>();
    readonly List<LineRenderer> allLines = new List<LineRenderer>();
    /// <summary>Cut markers shown.</summary>
    readonly List<GameObject> markers = new List<GameObject>();
    Material lineMaterial;

    // What is shown now, to redo it only when something changes.
    Unit shownUnit;
    HexTile shownTile;
    Player previewPlayer;
    int shownFrameCheck;
    bool shownAll;
    Player shownSide;
    int shownPublished = -1;
    float shownAt = -1f;

    /// <summary>How often (seconds), at most, the cut lines and markers are worked out again while supply keeps being published.</summary>
    public const float RedrawEvery = 0.25f;

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
        if (Input.GetKeyDown(FrontEndSettings.Get.supplyLinesKey)) GameOptions.AllSupplyLines = !GameOptions.AllSupplyLines;
        // The supply the player sees: the game's rules only, never the AI's what-ifs (see SupplyNetwork.AsPlayed).
        using (SupplyNetwork.AsPlayed()) UpdateShown();
    }

    /// <summary>The side whose supply lines the screen shows: the fog's viewer, else whose turn it is if a person plays it.</summary>
    public static Player ScreenSide() {
        if (FogOfWar.On) return FogOfWar.Viewer;
        GameController gc = GameController.gc;
        Player p = gc != null ? gc.activePlayer : null;
        return p != null && FogOfWar.IsHuman(p) ? p : null;
    }

    void UpdateShown() {
        GameController gc = GameController.gc;
        Unit u = gc != null ? gc.activeUnit : null;
        // Only for a human's unit: an AI's (it selects each unit as it acts) is not the player's
        // business, and the AI may be thinking on another thread while this would publish supply.
        if (u != null && !FogOfWar.IsHuman(u.owner)) u = null;
        if (u != null && (u.tile == null || u.owner == null)) u = null;
        HexTile dest = Destination(u);
        bool all = GameOptions.AllSupplyLines;
        Player side = ScreenSide();
        bool moved = SupplyNetwork.Published != shownPublished && Time.unscaledTime - shownAt >= RedrawEvery;
        if (u == shownUnit && dest == shownTile && all == shownAll && side == shownSide && !moved) return;
        bool reselected = u != shownUnit || dest != shownTile;
        shownAll = all; shownSide = side; shownAt = Time.unscaledTime;
        if (reselected || moved) {
            Clear();
            shownUnit = u;
            shownTile = dest;
            if (u != null) DrawSelection(u, dest);
        }
        Layer layer = new Layer();
        if (u != null && dest == null) ForSelection(u, side, layer);
        if (all && side != null) AllLines(side, layer);
        DrawLayer(layer, side ?? (u != null ? u.owner : null));
        shownPublished = SupplyNetwork.Published;   // after drawing: drawing solves (and publishes) supply itself
    }

    void DrawSelection(Unit u, HexTile dest) {
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
        // The line is for supply units (and what carries them): where their supply comes from.
        if (carried.Count > 0) DrawSupplyLine(p, from, field, preview, carried);
    }

    /// <summary>What the cut lines, the markers and (all supply lines on) every supply line show: tiles, for the screen to draw.</summary>
    public sealed class Layer {
        /// <summary>Supply lines (all supply lines on): each supply unit's to what feeds it.</summary>
        public readonly List<List<HexTile>> lines = new List<List<HexTile>>();
        /// <summary>Cut supply lines, each from a transport to the source that should feed it.</summary>
        public readonly List<SupplyNetwork.Cut> cuts = new List<SupplyNetwork.Cut>();
        /// <summary>Where the markers go: cuts, and where enemies the side does not see turn a transit.</summary>
        public readonly List<HexTile> markers = new List<HexTile>();
        internal readonly HashSet<Unit> carriers = new HashSet<Unit>();

        internal void Mark(HexTile t) { if (t != null && !markers.Contains(t)) markers.Add(t); }
    }

    /// <summary>
    /// What selecting <paramref name="u"/> adds (into <paramref name="layer"/>): if it is a transport (or rides in one)
    /// carrying supply that is cut off, the cut line (SupplyNetwork.CutRoute) and its marker; if it links only round enemies
    /// <paramref name="viewer"/> does not see, the marker where they turn it. Nothing for anything else.
    /// </summary>
    public static void ForSelection(Unit u, Player viewer, Layer layer) {
        if (u == null || u.owner == null) return;
        Unit carrier = SupplyNetwork.Outermost(u);
        if (carrier == null || carrier.tile == null || layer.carriers.Contains(carrier) || CarriedSupplies(carrier).Count == 0) return;
        layer.carriers.Add(carrier);
        Player side = viewer != null && Player.Friendly(viewer, u.owner) ? viewer : u.owner;
        SupplyNetwork.Cut cut = SupplyNetwork.CutRoute(carrier, side);
        if (cut != null) { layer.cuts.Add(cut); layer.Mark(cut.marker); return; }
        foreach (HexTile t in SupplyNetwork.UnseenContacts(side, carrier)) layer.Mark(t);
    }

    /// <summary>
    /// Every supply line of <paramref name="side"/> (into <paramref name="layer"/>): each supply unit's to what feeds it
    /// (SourcePath, its first link: together they are the whole network), and for each transport carrying supply, its cut
    /// line and marker or the markers where enemies the side does not see turn it (ForSelection).
    /// </summary>
    public static void AllLines(Player side, Layer layer) {
        if (side == null) return;
        SupplyNetwork.Result field = SupplyNetwork.Solve(side, false);
        HashSet<HexTile> from = new HashSet<HexTile>();
        foreach (Unit u in new List<Unit>(side.units)) {
            SupplyPoint h = u != null ? u.Hub : null;
            if (h == null || h.producer || u.Strength <= 0 || u.tile == null) continue;
            Unit carrier = u.trasnporter != null ? SupplyNetwork.Outermost(u.trasnporter) : null;
            if (carrier != null) {
                ForSelection(carrier, side, layer);
                if (!field.connectedHubs.Contains(h)) continue;
            }
            HexTile at = carrier != null ? carrier.tile : u.tile;
            if (at == null || !from.Add(at)) continue;
            List<HexTile> line = SourcePathWithLegs(side, at, field, null, null, 1);
            if (line.Count >= 2) layer.lines.Add(line);
        }
    }

    void DrawLayer(Layer layer, Player p) {
        foreach (LineRenderer l in cutLines) if (l != null) l.gameObject.SetActive(false);
        foreach (LineRenderer l in allLines) if (l != null) l.gameObject.SetActive(false);
        Color c = p != null ? p.displayColor : Color.white;
        for (int i = 0; i < layer.lines.Count; i++) Show(Line(allLines, i, LinePrefab), layer.lines[i], c, 1f, 1f);
        for (int i = 0; i < layer.cuts.Count; i++) {
            SupplyNetwork.Cut cut = layer.cuts[i];
            Show(Line(cutLines, i, CutLinePrefab), cut.line, cut.carrier.owner != null ? cut.carrier.owner.displayColor : c, 1f, 1f);
        }
        ShowMarkers(layer.markers);
    }

    /// <summary>The cut marker: Assets/Resources/SupplyContact.prefab (any look), stood at the tile's unit placement.</summary>
    public const string MarkerPrefab = "SupplyContact";
    GameObject markerPrefab;
    bool lookedForMarker;

    void ShowMarkers(List<HexTile> tiles) {
        if (!lookedForMarker) {
            lookedForMarker = true;
            markerPrefab = Resources.Load<GameObject>(MarkerPrefab);
            if (markerPrefab == null) Debug.LogWarning("Supply overlay: no prefab Resources/" + MarkerPrefab + ".prefab for the cut marker.");
        }
        int i = 0;
        if (markerPrefab != null)
            foreach (HexTile t in tiles) {
                if (t == null) continue;
                if (i >= markers.Count) markers.Add(Instantiate(markerPrefab, transform));
                GameObject g = markers[i++];
                g.name = MarkerPrefab + " (" + t.x + "," + t.y + ")";
                g.transform.position = t.unitPlacement != null ? t.unitPlacement.position : t.transform.position;
                g.SetActive(true);
            }
        for (; i < markers.Count; i++) if (markers[i] != null) markers[i].SetActive(false);
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
        if (path == null) return;
        List<Vector3> points = new List<Vector3>();
        foreach (HexTile t in path) Add(points, t);
        if (points.Count < 2) return;
        lr.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) lr.SetPosition(i, points[i]);
        Tint(lr, c, alpha);
        float w;
        if (!baseWidths.TryGetValue(lr, out w)) baseWidths[lr] = w = lr.widthMultiplier;
        lr.widthMultiplier = w * width;
        lr.gameObject.SetActive(true);
    }
    /// <summary>Each line's own width and colours, as its prefab made it (the width and tint scale them).</summary>
    readonly Dictionary<LineRenderer, float> baseWidths = new Dictionary<LineRenderer, float>();
    readonly Dictionary<LineRenderer, Gradient> baseGradients = new Dictionary<LineRenderer, Gradient>();

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
        return SourcePathWithLegs(p, from, field, preview, null);
    }

    /// <summary>Where <paramref name="hub"/>'s unit stands (or is being moved to, in <paramref name="preview"/>).</summary>
    static HexTile TileOfHub(SupplyPoint hub, Dictionary<SupplyPoint, HexTile> preview) {
        HexTile t;
        if (preview != null && preview.TryGetValue(hub, out t) && t != null) return t;
        return hub.myUnit != null ? hub.myUnit.tile : null;
    }

    /// <summary>
    /// As above; <paramref name="legs"/>, if given, gets one entry per tile of the line: the carrier whose transit
    /// enters that tile (it drives or sails there carrying supply), or null where supply flows over the ground.
    /// </summary>
    public static List<HexTile> SourcePathWithLegs(Player p, HexTile from, SupplyNetwork.Result field, Dictionary<SupplyPoint, HexTile> preview, List<Unit> legs) {
        return SourcePathWithLegs(p, from, field, preview, legs, 0);
    }

    /// <summary>As above, as far as the <paramref name="links"/>-th source along it (its tile the line's last), all of it for 0: 1 is the supply unit on <paramref name="from"/> to what feeds it.</summary>
    public static List<HexTile> SourcePathWithLegs(Player p, HexTile from, SupplyNetwork.Result field, Dictionary<SupplyPoint, HexTile> preview, List<Unit> legs, int links) {
        int found = 0;
        List<HexTile> points = new List<HexTile>();
        if (legs != null) legs.Clear();
        System.Action<Unit> mark = c => { if (legs != null) while (legs.Count < points.Count) legs.Add(c); };
        if (from == null) return points;
        HashSet<SupplyPoint> seen = new HashSet<SupplyPoint>();
        // Supply carried on 'from' (a ship out at sea gives nothing where it floats): its line starts there, whatever the field.
        if (field.DepthAt(from) <= 0 && Carried(HubOn(p, from, preview, seen)) == null) return points;
        HexTile at = from;
        SupplyNetwork.Result f = field;
        // The network as it stands (or as previewed): which carried supply links through another carrier (a convoy).
        SupplyNetwork.Result whole = preview != null ? SupplyNetwork.SolveMoved(p, preview) : SupplyNetwork.Solve(p, false);
        SupplyPoint next = null;
        int guard = 0;
        while (at != null && guard++ < 32) {
            HexTile top;
            SupplyPoint source;
            if (next != null) { top = at; source = next; next = null; }   // a convoy's next carrier: the line is at it already
            else if (Carried(HubOn(p, at, preview, seen)) != null) {
                // Carried supply here: it links by its carrier's own move (below), not up the field. (Climbing would
                // cross the land beside two ships to the one that supplies it, not along the water.)
                top = at;
                if (points.Count == 0 || points[points.Count - 1] != at) points.Add(at);
                mark(null);
                source = HubOn(p, at, preview, seen);
            } else {
                top = Climb(at, f, points);
                mark(null);
                // Several supply units can share a tile (all aboard one transport): each in turn.
                source = HubOn(p, top, preview, seen);
                // Up on the shore with nothing there: the supply came from a ship beside it (the sea is never supplied,
                // so the field's top is the land it borders, not the water it floats on).
                if (source == null && top != null) {
                    HexTile ship = null; SupplyPoint aboard = null;
                    foreach (HexTile n in top.neighbors) {
                        if (n == null || n.terrain == null || n.terrain.elevation >= 0) continue;
                        SupplyPoint h = Carried(HubOn(p, n, preview, seen));
                        if (h != null && (ship == null || CombatMath.Precedes(n, ship))) { ship = n; aboard = h; }
                    }
                    if (ship != null) { points.Add(ship); mark(null); top = ship; source = aboard; }
                }
            }
            if (source == null || source.producer) break;   // a building or the HQ: the root
            if (links > 0 && ++found > links) break;   // as far as asked
            seen.Add(source);
            Unit carrier = SupplyNetwork.Outermost(source.myUnit != null ? source.myUnit.trasnporter : null);
            // Carried supply links the way the network chose for it (Result.routes, SupplyNetwork.RouteFor): its carrier's
            // tiles to the tile it links from, then on from there (through another carrier, on the ground beside a ship, or
            // up the supply it reached). The line draws that one way, the network's own.
            List<HexTile> route;
            if (carrier != null && whole.routes.TryGetValue(source, out route) && route != null && route.Count > 0 && route[0] == top) {
                for (int i = 1; i < route.Count; i++) points.Add(route[i]);
                mark(carrier);
                f = SolveWithout(p, seen, preview);
                SupplyPoint via;
                if (whole.linkedThrough.TryGetValue(source, out via) && via != null && !seen.Contains(via)) {
                    HexTile viaTile = TileOfHub(via, preview);
                    if (viaTile != null) {
                        if (points[points.Count - 1] != viaTile) { points.Add(viaTile); mark(null); }
                        at = viaTile; next = via;
                        continue;
                    }
                }
                HexTile landing;
                if (whole.landings.TryGetValue(source, out landing) && landing != null) { points.Add(landing); mark(null); }
                at = points[points.Count - 1];
                if (at == top && f.DepthAt(at) <= 0) break;   // on supplied ground it no longer finds: nothing more to draw
                continue;
            }
            if (carrier != null) break;   // carried and not linked: stranded, it feeds itself
            // What feeds it: the network without it and without every hub already on the line
            // (else the next hub's field, which reaches back over this one, would lead the line
            // back to it: two hubs drawn feeding each other, neither reaching the HQ).
            f = SolveWithout(p, seen, preview);
            if (f.DepthAt(top) <= 0) break;
            at = top;
        }
        mark(null);
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
    /// <summary><paramref name="hub"/> if it is a supply unit aboard something, else null.</summary>
    static SupplyPoint Carried(SupplyPoint hub) {
        return hub != null && !hub.producer && hub.myUnit != null && hub.myUnit.trasnporter != null ? hub : null;
    }

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
    /// <summary>The cut supply line's look: Assets/Resources/SupplyLineCut.prefab (a copy of SupplyLine's to restyle, a dotted line, say).</summary>
    public const string CutLinePrefab = "SupplyLineCut";

    /// <summary>The prefab's colours times the player's: its gradient sets opacity and fade, the player the hue.</summary>
    void Tint(LineRenderer lr, Color player, float alpha = 1f) {
        Gradient prefabGradient;
        if (!baseGradients.TryGetValue(lr, out prefabGradient) || prefabGradient == null) return;
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

    LineRenderer Line(int i) { return Line(lines, i, LinePrefab); }

    LineRenderer Line(List<LineRenderer> lines, int i) { return Line(lines, i, LinePrefab); }

    /// <summary>The <paramref name="i"/>-th line of <paramref name="lines"/>, made from Resources/<paramref name="prefab"/> (the supply line's, if that is missing; a plain line in code, if both are).</summary>
    LineRenderer Line(List<LineRenderer> lines, int i, string prefab) {
        while (lines.Count <= i) {
            LineRenderer template = Resources.Load<LineRenderer>(prefab);
            if (template == null && prefab != LinePrefab) {
                if (!warned.Contains(prefab)) { warned.Add(prefab); Debug.LogWarning("Supply overlay: no prefab Resources/" + prefab + ".prefab: drawn with " + LinePrefab + "'s look."); }
                template = Resources.Load<LineRenderer>(LinePrefab);
            }
            if (template != null) {
                LineRenderer made = GameObject.Instantiate(template, transform);
                made.name = prefab + lines.Count;
                made.useWorldSpace = true;
                baseGradients[made] = template.colorGradient;
                made.gameObject.SetActive(false);
                lines.Add(made);
                continue;
            }
            // No prefab: a plain line in code.
            GameObject g = new GameObject(prefab + lines.Count);
            g.transform.SetParent(transform, false);
            LineRenderer lr = g.AddComponent<LineRenderer>();
            Gradient plain = new Gradient();
            plain.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 1f) });
            baseGradients[lr] = plain;
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
    readonly HashSet<string> warned = new HashSet<string>();

    // ------------------------------------------------------------------ enemy move and threat

    /// <summary>
    /// The supply line of <paramref name="enemy"/>, if it is a supply unit or carries one: from where it stands back to the
    /// HQ (SourcePath, the network as it truly is: its carriers' routes are the network's own), in its side's colour. Under
    /// fog, from it as far as the viewer sees each tile along it: the line stops where it goes into the fog (it does not
    /// show where the enemy's supply comes from beyond what you see).
    /// </summary>
    void ShowEnemyLine(Unit enemy) {
        foreach (LineRenderer l in enemyLines) if (l != null) l.gameObject.SetActive(false);
        List<HexTile> path = EnemyLine(enemy);
        if (path.Count < 2) return;
        Show(Line(enemyLines, 0), path, enemy.owner.displayColor, 1f, 1f);
    }

    /// <summary>The tiles ShowEnemyLine draws for <paramref name="enemy"/> (empty if it carries no supply).</summary>
    public static List<HexTile> EnemyLine(Unit enemy) {
        List<HexTile> path = new List<HexTile>();
        if (enemy == null || enemy.tile == null || enemy.owner == null || CarriedSupplies(enemy).Count == 0) return path;
        Player p = enemy.owner;
        using (SupplyNetwork.AsPlayed()) path = SourcePath(p, enemy.tile, SupplyNetwork.Solve(p, false), null);
        Player viewer = FogOfWar.On ? FogOfWar.Viewer : null;
        if (viewer != null && !Player.Friendly(viewer, p)) {
            int seen = 0;
            while (seen < path.Count && FogOfWar.TileVisible(viewer, path[seen])) seen++;
            if (seen < path.Count) path = path.GetRange(0, seen);
        }
        return path;
    }

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
        if (instance != null) instance.ShowEnemyLine(enemy);
        HashSet<HexTile> threat = new HashSet<HexTile>();
        if (enemy.Firepower > 0) {
            if (enemy.moveAndAttack) foreach (HexTile m in moves) foreach (HexTile a in m.AllInAttackRange(enemy)) threat.Add(a);
            else foreach (HexTile a in enemy.tile.AllInAttackRange(enemy)) threat.Add(a);
        }
        HexGenerator.forAllTiles(x => { x.G = 0; x.parent = null; return 0; });
        // A unit that cannot move and fire (artillery) fires only from where it stands: its whole range shows, over its
        // moves. One that can: its moves show, and where it could fire beyond them.
        bool fixedFire = !enemy.moveAndAttack;
        foreach (HexTile t in threat) if (fixedFire || !moves.Contains(t)) t.ShowEnemy(UIState.ENEMY_THREAT);
        foreach (HexTile t in moves) if (t != enemy.tile && !(fixedFire && threat.Contains(t))) t.ShowEnemy(UIState.ENEMY_MOVE);
        enemyShown = true;
    }

    public static void HideEnemy() {
        if (!enemyShown) return;
        enemyShown = false;
        if (instance != null) foreach (LineRenderer l in instance.enemyLines) if (l != null) l.gameObject.SetActive(false);
        HexGenerator.forAllTiles(x => { if (x.state == UIState.ENEMY_MOVE || x.state == UIState.ENEMY_THREAT) x.Reset(); return 0; });
    }
}
