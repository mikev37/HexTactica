using System.Collections.Generic;

/// <summary>
/// What the supply overlay shows of a side's network, worked out once per state of the supply and kept until it changes:
/// the supply line from any supply unit or transport, the branches of the ones fed through it, cut lines and their
/// markers, an enemy's lines as the viewer sees them, and every line at once (all supply lines on). The overlay
/// (SupplyOverlay) only asks for these and shows or hides its lines and markers; it works nothing out itself, but for a
/// move being previewed (a network that is not the board's).
///
/// "Changed" is SupplyNetwork.Changes: the game works supply out again when something changes (Player.Resupply after a
/// move, a boarding or getting off, a death, a capture, a change of owner or terrain, a script, a load; a turn's start),
/// and only then. The board's own supply borders are drawn from those same solves, so what is kept here is never older
/// than what the board shows. Also kept per screen side (FogOfWar.Viewer: what the fog hides of an enemy's lines) and
/// fog on or off. Main thread, real board.
/// </summary>
public static class SupplyLines {

    sealed class Side {
        public int changes;
        public Player viewer;
        public bool fog;
        public SupplyNetwork.Result net;
        public SupplyOverlay.Lines lines;
        public readonly Dictionary<Unit, Dictionary<SupplyPoint, List<HexTile>>> branches = new Dictionary<Unit, Dictionary<SupplyPoint, List<HexTile>>>();
        public readonly Dictionary<KeyValuePair<Unit, Player>, SupplyNetwork.Cut> cuts = new Dictionary<KeyValuePair<Unit, Player>, SupplyNetwork.Cut>();
        public readonly Dictionary<KeyValuePair<Unit, Player>, List<HexTile>> contacts = new Dictionary<KeyValuePair<Unit, Player>, List<HexTile>>();
        public readonly Dictionary<Unit, List<List<HexTile>>> enemyLines = new Dictionary<Unit, List<List<HexTile>>>();
        public SupplyOverlay.Layer all;
    }

    static readonly Dictionary<Player, Side> sides = new Dictionary<Player, Side>();

    /// <summary>How many times a side's network has been worked out here (for checks: once per change, not per click).</summary>
    public static int Solved;

    static Side Of(Player p) {
        Player viewer = FogOfWar.On ? FogOfWar.Viewer : null;
        Side s;
        if (sides.TryGetValue(p, out s) && s.changes == SupplyNetwork.Changes && s.viewer == viewer && s.fog == FogOfWar.On) return s;
        s = new Side { changes = SupplyNetwork.Changes, viewer = viewer, fog = FogOfWar.On };
        // The network as it stands, not published (the board shows it already: Player.Resupply).
        using (SupplyNetwork.AsPlayed()) s.net = SupplyNetwork.SolveMoved(p, new Dictionary<SupplyPoint, HexTile>());
        Solved++;
        s.lines = new SupplyOverlay.Lines(p, s.net, null);
        sides[p] = s;
        return s;
    }

    /// <summary>Forget everything kept (a new game).</summary>
    public static void Clear() { sides.Clear(); }

    /// <summary>The network of <paramref name="p"/> as it stands (not published).</summary>
    public static SupplyNetwork.Result Network(Player p) { return p != null ? Of(p).net : null; }

    /// <summary>The supply line from <paramref name="from"/> for <paramref name="p"/> (SupplyOverlay.SourcePath, as it stands). A new list.</summary>
    public static List<HexTile> Line(Player p, HexTile from) {
        if (p == null || from == null) return new List<HexTile>();
        SupplyOverlay.Lines.Line l;
        using (SupplyNetwork.AsPlayed()) l = Of(p).lines.Get(from);
        return l != null ? new List<HexTile>(l.tiles) : new List<HexTile>();
    }

    /// <summary>The branches of <paramref name="u"/> (SupplyOverlay.Dependents): every supply unit fed through it, its line back to it.</summary>
    public static Dictionary<SupplyPoint, List<HexTile>> Branches(Unit u) {
        if (u == null || u.owner == null || u.tile == null) return new Dictionary<SupplyPoint, List<HexTile>>();
        Side s = Of(u.owner);
        Dictionary<SupplyPoint, List<HexTile>> b;
        if (!s.branches.TryGetValue(u, out b)) {
            using (SupplyNetwork.AsPlayed()) b = SupplyOverlay.Dependents(u.owner, SupplyOverlay.CarriedSupplies(u), u.tile, s.net, null, s.lines);
            s.branches[u] = b;
        }
        return b;
    }

    /// <summary>The cut line of <paramref name="carrier"/> (SupplyNetwork.CutRoute) as <paramref name="side"/> is shown it, or null.</summary>
    public static SupplyNetwork.Cut Cut(Unit carrier, Player side) {
        if (carrier == null || carrier.owner == null) return null;
        Side s = Of(carrier.owner);
        SupplyNetwork.Cut c;
        KeyValuePair<Unit, Player> k = new KeyValuePair<Unit, Player>(carrier, side);
        if (!s.cuts.TryGetValue(k, out c)) { using (SupplyNetwork.AsPlayed()) c = SupplyNetwork.CutRoute(carrier, side); s.cuts[k] = c; }
        return c;
    }

    /// <summary>Where enemies <paramref name="side"/> does not see turn <paramref name="carrier"/>'s transit (SupplyNetwork.UnseenContacts).</summary>
    public static List<HexTile> Contacts(Unit carrier, Player side) {
        if (carrier == null || carrier.owner == null) return new List<HexTile>();
        Side s = Of(carrier.owner);
        List<HexTile> c;
        KeyValuePair<Unit, Player> k = new KeyValuePair<Unit, Player>(carrier, side);
        if (!s.contacts.TryGetValue(k, out c)) { using (SupplyNetwork.AsPlayed()) c = SupplyNetwork.UnseenContacts(side, carrier); s.contacts[k] = c; }
        return c;
    }

    /// <summary>An enemy supply unit's line as the screen's side sees it (SupplyOverlay.EnemyLineRuns).</summary>
    public static List<List<HexTile>> EnemyLine(Unit enemy) {
        if (enemy == null || enemy.owner == null) return new List<List<HexTile>>();
        Side s = Of(enemy.owner);
        List<List<HexTile>> runs;
        if (!s.enemyLines.TryGetValue(enemy, out runs)) {
            List<HexTile> whole = enemy.tile != null && SupplyOverlay.CarriedSupplies(enemy).Count > 0 ? Line(enemy.owner, enemy.tile) : new List<HexTile>();
            Player viewer = FogOfWar.On ? FogOfWar.Viewer : null;
            if (viewer != null && Player.Friendly(viewer, enemy.owner)) viewer = null;
            runs = SupplyOverlay.SeenRuns(whole, viewer);
            s.enemyLines[enemy] = runs;
        }
        return runs;
    }

    /// <summary>Every supply line of <paramref name="side"/> (SupplyOverlay.AllLines).</summary>
    public static SupplyOverlay.Layer All(Player side) {
        if (side == null) return new SupplyOverlay.Layer();
        Side s = Of(side);
        if (s.all == null) {
            s.all = new SupplyOverlay.Layer();
            using (SupplyNetwork.AsPlayed()) SupplyOverlay.AllLines(side, s.all, s.net);
        }
        return s.all;
    }
}
