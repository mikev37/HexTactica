using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Solves the supply field for one player.
///
/// The doc's model, implemented literally:
///   - Supply originates at buildings that produce it (HQ, cities).
///   - Supply hubs standing inside supplied territory relay it further, recursively.
///   - "The Supply Hub loses 1 point of health ... But the supply Hubs are recursive
///     from the HQ, so if there's an unbroken connection from the headquarters
///     building to the supply it is resupplied."
///
/// A supply unit is a relay, not a source: cut off from the network it supplies nothing
/// (supply buildings are different: a cut-off one gives out of the store it charged).
///
/// Propagation uses the *supplier's own movement rules*, which is what makes roads
/// matter and lets enemy units physically sever a corridor. A hub riding a transport
/// spreads supply only as far as it would on foot, from where the transport stands, but links
/// to the network as far as the transport could carry it: it is connected if supplied ground
/// lies within one move of the transport. Ships and aircraft with supply aboard also link through
/// each other (convoys: CarriedLinked); a ship gives supply only beside the shore, an aircraft only
/// over land. That move is a transit: it never uses a tile on or beside an enemy unit that could attack
/// the carrier (Exposed) on its way (where it stands is not on its way: only the tiles it moves through). Supply units on
/// foot are not in transit: they stand in supply.
/// </summary>
public static class SupplyNetwork {

    /// <summary>
    /// The HQ first: a cut-off supply building (a FOB) spends a point of its store only on a turn it feeds a unit that
    /// the HQ's network (or a building before it) does not supply; otherwise it keeps its store (its area is still
    /// shown supplied). Off: every cut-off building with store spends a point a turn.
    /// </summary>
    public static bool StoreOnlyWhenNeeded = true;

    /// <summary>Under fog, the screen shows the screen's side's (and its allies') supply as it truly is, cuts by enemies it does not see included, but for the tile each such enemy stands on, shown as it would be without the enemies it does not see (HexTile.supplyShown): a one-tile hole would pinpoint it; a line or ring cutting supply off shows as cut off, a reason to look. Other sides' supply is shown as it truly is, on the tiles the viewer sees. Off: the true network is shown, holes and all.</summary>
    public static bool FillUnseenHoles = true;

    /// <summary>Supply units' linked/cut-off flags (SupplyPoint.connected) set whenever the real network is published, not only at turn start.</summary>
    public static bool FlagsFollowPublish = true;

    /// <summary>Goes up every time a supply field is published onto the board (displays redraw only then).</summary>
    public static int Published;

    /// <summary>Record Result.linked in the solves made on this thread while set (AISupplyTree).</summary>
    [System.ThreadStatic] public static bool RecordLinked;


    /// <summary>Result of a solve, for the AI and the harness to inspect.</summary>
    public class Result {
        public readonly Dictionary<HexTile, int> depth = new Dictionary<HexTile, int>();
        /// <summary>Producers and every relay chained back to a producer or supply building.</summary>
        public readonly List<SupplyPoint> connectedHubs = new List<SupplyPoint>();
        public readonly List<SupplyPoint> strandedHubs = new List<SupplyPoint>();
        /// <summary>Supply buildings relaying the network, and cut-off ones living off their store.</summary>
        public readonly List<Building> connectedBuildings = new List<Building>();
        public readonly List<Building> strandedBuildings = new List<Building>();
        /// <summary>The cut-off supply buildings that spend a point of store this turn (all of them, unless StoreOnlyWhenNeeded: those feeding a unit nothing else supplies).</summary>
        public List<Building> spendingBuildings = new List<Building>();
        public int mobileHubs;
        /// <summary>
        /// Carried supply linked through another carrier rather than to supplied ground (a convoy: CarriedLinked) — the
        /// supply unit whose carrier it reached. The supply line follows these back toward the HQ (SupplyOverlay).
        /// </summary>
        public readonly Dictionary<SupplyPoint, SupplyPoint> linkedThrough = new Dictionary<SupplyPoint, SupplyPoint>();
        /// <summary>
        /// Each carried supply unit this network links, the way its carrier links it (RouteFor): the carrier's tiles from
        /// where it stands to the tile it links from (only its own tile when it stands on supplied ground). The supply line
        /// draws this, and the contacts compare it with the way its side would know (UnseenContacts): one choice of way.
        /// </summary>
        public readonly Dictionary<SupplyPoint, List<HexTile>> routes = new Dictionary<SupplyPoint, List<HexTile>>();
        /// <summary>A ship's carried supply linked by sailing beside supplied ground: the ground it puts it on (beside the route's end).</summary>
        public readonly Dictionary<SupplyPoint, HexTile> landings = new Dictionary<SupplyPoint, HexTile>();
        /// <summary>
        /// Every carrier's transit this solve looked at: where it stood and its move range. A link through one depends on
        /// the enemy units within that range + 1 of it (on its way, or beside it), not only on those on or beside the field
        /// (AIMap keeps a network only while none of them changed). Friendly units never change a transit: it passes
        /// through them, and is exposed only by enemies.
        /// </summary>
        public readonly List<KeyValuePair<HexTile, int>> transits = new List<KeyValuePair<HexTile, int>>();
        /// <summary>The side this network is (Solve's player).</summary>
        public Player owner;
        /// <summary>
        /// With RecordLinked set when it was solved: the field before cut-off supply buildings gave
        /// out of their stores, i.e. the ground a supply unit standing there would link to the network
        /// from. (Ground supplied only by a cut-off building's store supplies the units on it, but a
        /// supply unit there is cut off too.) Null otherwise.
        /// </summary>
        public Dictionary<HexTile, int> linked;
        public int LinkedAt(HexTile t) {
            if (linked == null) return DepthAt(t);
            int d;
            return (t != null && linked.TryGetValue(t, out d)) ? d : 0;
        }

        public int DepthAt(HexTile t) {
            int d;
            return (t != null && depth.TryGetValue(t, out d)) ? d : 0;
        }
        public bool IsSupplied(HexTile t) { return DepthAt(t) > 0; }
    }

    /// <summary>
    /// Compute the supply field for <paramref name="player"/> and write it into
    /// <c>HexTile.supply</c>. When <paramref name="applyDrain"/> is false the solve
    /// is a pure query — this is what lets the AI ask "what would supply look like
    /// if the hub stood there" without spending anyone's health.
    /// </summary>
    public static Result Solve(Player player, bool applyDrain) {
        return Solve(player, applyDrain, null, null);
    }

    /// <summary>
    /// Hypothetical solve: pretend <paramref name="movedHub"/> is standing on
    /// <paramref name="hypotheticalTile"/> instead of where it actually is.
    /// </summary>
    public static Result Solve(Player player, bool applyDrain, SupplyPoint movedHub, HexTile hypotheticalTile) {
        return Solve(player, applyDrain, movedHub, hypotheticalTile, null);
    }

    /// <summary>
    /// The network as it would be without <paramref name="excluded"/>. Used to judge
    /// whether a candidate position would connect a hub: the live field includes the hub's
    /// own bubble, so every tile around a stranded hub looks supplied.
    /// </summary>
    public static Result SolveWithout(Player player, SupplyPoint excluded) {
        return Solve(player, false, null, null, excluded);
    }

    /// <summary>
    /// Static supply only: buildings and producers (the HQ), no mobile hubs. What the army
    /// keeps if every supply unit dies or walks away. Not published.
    /// </summary>
    public static Result SolveStatic(Player player) {
        return Solve(player, false, null, null, null, true, null);
    }

    /// <summary>
    /// The network with some hubs standing on other tiles (the AI's planned "ghost" hub
    /// moves). Not published: the board keeps the real field.
    /// </summary>
    public static Result SolveMoved(Player player, Dictionary<SupplyPoint, HexTile> moved) {
        return Solve(player, false, null, null, null, false, moved);
    }

    /// <summary>
    /// The network made by our supply buildings, producers and only the hubs in
    /// <paramref name="planned"/>, each standing where it says. For planning a chain of supply
    /// units a link at a time (AISupplyChain). Not published.
    /// </summary>
    public static Result SolvePlanned(Player player, Dictionary<SupplyPoint, HexTile> planned) {
        return Solve(player, false, null, null, null, false, planned, true);
    }

    /// <summary>Every supply unit where it stands, those in <paramref name="planned"/> moved there instead.</summary>
    public static Result SolveAll(Player player, Dictionary<SupplyPoint, HexTile> planned) {
        return Solve(player, false, null, null, null, false, planned, false);
    }

    static Result Solve(Player player, bool applyDrain, SupplyPoint movedHub, HexTile hypotheticalTile, SupplyPoint excluded) {
        return Solve(player, applyDrain, movedHub, hypotheticalTile, excluded, false, null);
    }

    static Result Solve(Player player, bool applyDrain, SupplyPoint movedHub, HexTile hypotheticalTile, SupplyPoint excluded,
                        bool staticOnly, Dictionary<SupplyPoint, HexTile> moved) {
        return Solve(player, applyDrain, movedHub, hypotheticalTile, excluded, staticOnly, moved, false);
    }

    static Result Solve(Player player, bool applyDrain, SupplyPoint movedHub, HexTile hypotheticalTile, SupplyPoint excluded,
                        bool staticOnly, Dictionary<SupplyPoint, HexTile> moved, bool onlyMoved) {
        // The relay below asks a carrier's move reach again on every pass until nothing changes: worked out once per solve
        // (nothing moves during one).
        Dictionary<ReachKey, Reach> outer = reachMemo;
        Dictionary<ExposureKey, Dictionary<HexTile, bool>> outerExposure = exposureMemo;
        if (outer == null) { reachMemo = new Dictionary<ReachKey, Reach>(); exposureMemo = new Dictionary<ExposureKey, Dictionary<HexTile, bool>>(); }
        long __p = HexTactica.AI.AIPerf.Begin();
        try { return SolveOnce(player, applyDrain, movedHub, hypotheticalTile, excluded, staticOnly, moved, onlyMoved); }
        finally { reachMemo = outer; exposureMemo = outerExposure; HexTactica.AI.AIPerf.End("supply: SupplyNetwork.Solve", __p); }
    }

    /// <summary>
    /// Carriers' move reaches kept until the scope is disposed, across every solve in it: for a planner that solves
    /// many hypothetical placements on one board (nothing on the board may move inside the scope).
    /// </summary>
    public static System.IDisposable KeepReach() { return new ReachScope(); }
    sealed class ReachScope : System.IDisposable {
        readonly Dictionary<ReachKey, Reach> outer = reachMemo;
        readonly Dictionary<ExposureKey, Dictionary<HexTile, bool>> outerExposure = exposureMemo;
        public ReachScope() { if (outer == null) { reachMemo = new Dictionary<ReachKey, Reach>(); exposureMemo = new Dictionary<ExposureKey, Dictionary<HexTile, bool>>(); } }
        public void Dispose() { reachMemo = outer; exposureMemo = outerExposure; }
    }

    /// <summary>
    /// Whether a carrier of one side and locomotion is exposed on each tile asked about (Exposed), on one board, kept for
    /// the solve (or KeepReach scope): every carrier's search and every relay pass asks the same tiles again. Kept as asked,
    /// tile by tile, by the same rule (the tile's occupier and its neighbours'), so it answers exactly as asking afresh
    /// would. Nothing moves inside one.
    /// </summary>
    struct ExposureKey : System.IEquatable<ExposureKey> {
        public Player owner; public Locomotion loco; public BoardView view;
        public bool Equals(ExposureKey o) { return ReferenceEquals(owner, o.owner) && loco == o.loco && ReferenceEquals(view, o.view); }
        public override bool Equals(object o) { return o is ExposureKey && Equals((ExposureKey)o); }
        public override int GetHashCode() { return (owner != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(owner) : 0) * 31 + (int)loco; }
    }
    [System.ThreadStatic] static Dictionary<ExposureKey, Dictionary<HexTile, bool>> exposureMemo;
    /// <summary>A carrier's reach depends on the board it is read on: the real one, or a side's copy (the screen's network
    /// under fog is solved on the viewer's copy inside the real solve, Publish; a reach from the real board would leak
    /// the enemies it does not see into it).</summary>
    struct ReachKey : System.IEquatable<ReachKey> {
        public Unit mover; public HexTile at; public int range, steps; public BoardView view;
        public bool Equals(ReachKey o) { return ReferenceEquals(mover, o.mover) && ReferenceEquals(at, o.at) && range == o.range && steps == o.steps && ReferenceEquals(view, o.view); }
        public override bool Equals(object o) { return o is ReachKey && Equals((ReachKey)o); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(mover) * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(at); }
    }
    [System.ThreadStatic] static Dictionary<ReachKey, Reach> reachMemo;

    static Result SolveOnce(Player player, bool applyDrain, SupplyPoint movedHub, HexTile hypotheticalTile, SupplyPoint excluded,
                        bool staticOnly, Dictionary<SupplyPoint, HexTile> moved, bool onlyMoved) {

        Result result = new Result();
        result.owner = player;
        if (player == null || HexGenerator.hg == null) return result;

        // Seed from every supply-producing building this player owns.
        Queue<SupplyPoint> pending = new Queue<SupplyPoint>();
        HashSet<SupplyPoint> activated = new HashSet<SupplyPoint>();

        // The HQ is the root. Other supply buildings relay, like supply units (below).
        List<Building> relayBuildings = new List<Building>();
        for (int i = 0; i < Building.all.Count; i++) {
            Building b = Building.all[i];
            if (b == null || b.owner != player || !b.producesSupply || b.tile == null || !b.Functioning) continue;   // a destroyed building supplies nothing
            if (b.IsHQ) { if (b.Home == player) FloodFromBuilding(b, result); }   // a captured HQ supplies nobody (only its own side, once retaken)
            else relayBuildings.Add(b);
        }
        relayBuildings.Sort((a, b) => CompareBuildings(a, b));
        HashSet<Building> buildingsOn = new HashSet<Building>();

        // Gather this player's hubs, in a deterministic order.
        List<SupplyPoint> hubs = new List<SupplyPoint>();
        for (int i = 0; i < player.units.Count; i++) {
            Unit u = player.units[i];
            if (u == null) continue;
            SupplyPoint sp = u.Hub;
            if (sp != null && sp != excluded && (!staticOnly || sp.producer) && (!onlyMoved || sp.producer || moved.ContainsKey(sp))) hubs.Add(sp);
        }
        hubs.Sort((a, b) => StableId(a).CompareTo(StableId(b)));
        result.mobileHubs = 0;
        for (int i = 0; i < hubs.Count; i++) if (!hubs[i].producer) result.mobileHubs++;

        // Producer hubs are roots, exactly like supply buildings. The shipped game's HQ
        // is a *unit* (TempHQ) carrying a producer SupplyPoint, not a Building. Treating
        // it as a relay meant it could never connect to anything, was classed as stranded,
        // and paid one strength per turn until it died on turn six — taking every relay
        // hub's connection with it.
        for (int i = 0; i < hubs.Count; i++) {
            SupplyPoint hub = hubs[i];
            if (!hub.producer) continue;
            HexTile at = TileOf(hub, movedHub, hypotheticalTile, moved);
            if (at == null) continue;
            activated.Add(hub);
            result.connectedHubs.Add(hub);
            FloodFromHub(hub, at, result);
        }

        // Relay: any hub sitting in supplied territory joins the network and extends it.
        // Iterate to a fixed point so chains of arbitrary length resolve.
        // (Run again below once cut-off supply buildings give out of their store: supply units in their ground relay it.)
        HashSet<SupplyPoint> relayedFromStore = new HashSet<SupplyPoint>();
        bool fromStore = false;
        System.Action relay = () => {
        bool progress = true;
        int guard = 0;
        while (progress && guard++ < 64) {
            progress = false;
            for (int i = 0; i < hubs.Count; i++) {
                SupplyPoint hub = hubs[i];
                if (activated.Contains(hub)) continue;
                HexTile at = TileOf(hub, movedHub, hypotheticalTile, moved);
                if (at == null) continue;
                Unit carrier = CarrierOf(hub, moved);
                if (hub == AssumeLinked) {
                    // A front-line supply unit (see AssumeLinked): aboard its transport, linked as cargo is.
                    if (AssumeLinkedVia != null) carrier = AssumeLinkedVia;
                    if (AssumeLinkedVia != null && !CarriedLinked(AssumeLinkedVia, at, hub, hubs, result, activated, movedHub, hypotheticalTile, moved)) continue;
                } else if (IsNavalHub(hub)) {
                    // A supply boat links to a friendly dock in supply, or to a linked supply boat,
                    // over water within its range; it supplies only the land it borders.
                    if (!NavalLinked(hub, at, player, result, activated, movedHub, hypotheticalTile, moved)) continue;
                } else if (PlannedAboard != null && PlannedAboard.ContainsKey(hub) && moved != null && moved.ContainsKey(hub)) {
                    // Planned aboard a transport at that tile (a supply leap).
                    if (!CarriedLinked(PlannedAboard[hub], at, hub, hubs, result, activated, movedHub, hypotheticalTile, moved)) continue;
                } else if (hub.myUnit != null && hub.myUnit.trasnporter != null) {
                    // Aboard: linked as its carrier links (CarriedLinked: ground in a move, or a convoy) — the outermost
                    // carrier, for supply aboard a transport aboard another (CarrierOf).
                    if (!CarriedLinked(carrier, at, hub, hubs, result, activated, movedHub, hypotheticalTile, moved)) continue;
                } else if (!result.IsSupplied(at)) continue;

                activated.Add(hub);
                result.connectedHubs.Add(hub);
                if (fromStore) relayedFromStore.Add(hub);
                FloodFromHub(hub, at, result, carrier);
                progress = true;
            }
            for (int i = 0; i < relayBuildings.Count; i++) {
                Building b = relayBuildings[i];
                if (buildingsOn.Contains(b) || fromStore) continue;
                bool reached = false;
                if (result.IsSupplied(b.tile)) reached = true;
                if (!reached) continue;
                buildingsOn.Add(b);
                result.connectedBuildings.Add(b);
                FloodFromBuilding(b, result);
                progress = true;
            }
        }
        };
        relay();

        // Water is never supplied ground (a harbor is: it stands on bridge ground). A ship carrying supply covers the
        // water it floats on as it spreads (its flood starts there), but neither it nor anything else takes supply from
        // the sea: a ship is in supply only in harbor, and lives on its own stock at sea (Player.Aloft). Linking never
        // read the sea (a carrier links by its own move, a supply boat through the boats linked before it).
        StripWater(result);
        if (RecordLinked) result.linked = new Dictionary<HexTile, int>(result.depth);
        // Supply from the HQ's network, before any store is drawn on (for StoreOnlyWhenNeeded).
        Dictionary<HexTile, int> fromNetwork = applyDrain && StoreOnlyWhenNeeded ? new Dictionary<HexTile, int>(result.depth) : null;
        // Cut-off supply buildings give out of their store while it lasts.
        if (!staticOnly) {
            for (int i = 0; i < relayBuildings.Count; i++) {
                Building b = relayBuildings[i];
                if (buildingsOn.Contains(b) || b.supplies <= 0) continue;
                result.strandedBuildings.Add(b);
                FloodFromBuilding(b, result);
            }
            // Supply units standing in a cut-off building's ground relay its supply on, as they do the HQ's.
            if (result.strandedBuildings.Count > 0) { fromStore = true; relay(); StripWater(result); }
        }
        // Which cut-off buildings actually feed someone this turn: only those spend (the HQ's network first, then
        // the buildings in order, each for the units nothing before it supplies).
        List<Building> spending = result.strandedBuildings;
        if (fromNetwork != null) {
            spending = new List<Building>();
            HashSet<HexTile> fed = new HashSet<HexTile>();
            List<HexTile> standing = new List<HexTile>();
            foreach (Unit u in player.units) if (u != null && u.tile != null && u.Strength > 0 && u.trasnporter == null) standing.Add(u.tile);
            foreach (HexTile t in standing) { int d; if (fromNetwork.TryGetValue(t, out d) && d > 0) fed.Add(t); }
            foreach (Building b in result.strandedBuildings) {
                Result own = new Result();
                FloodFromBuilding(b, own);
                // Its ground, and what supply units in it relay on.
                foreach (SupplyPoint h in relayedFromStore) {
                    HexTile at = TileOf(h, movedHub, hypotheticalTile, moved);
                    if (at != null && own.IsSupplied(at)) FloodFromHub(h, at, own, CarrierOf(h, moved));
                }
                bool needed = false;
                foreach (HexTile t in standing) if (!fed.Contains(t) && own.IsSupplied(t)) { needed = true; fed.Add(t); }
                if (needed) spending.Add(b);
            }
        }
        result.spendingBuildings = spending;

        // Hubs that never connected are cut off: a supply unit relays supply, it does not make it,
        // so a cut-off one supplies nothing (not even itself; it lives on what it carries, like any
        // unit out of supply).
        for (int i = 0; i < hubs.Count; i++) {
            SupplyPoint hub = hubs[i];
            if (activated.Contains(hub)) continue;
            if (TileOf(hub, movedHub, hypotheticalTile, moved) == null) continue;
            result.strandedHubs.Add(hub);
        }

        // Publish onto the board — but never a counterfactual network missing a hub.
        if (excluded == null && !staticOnly && moved == null && !onlyMoved) {
            Publish(player, result, () => Solve(player, false, movedHub, hypotheticalTile, excluded, staticOnly, moved, onlyMoved));
            // The real network on the real board: each supply unit's linked/cut-off flag follows it now, not only at the
            // start of the turn (the cut-off icon read a flag set then, while the borders showed every move since).
            // Never from a thought (a board copy: the hub objects are shared) or a "what if it stood there".
            if (FlagsFollowPublish && BoardView.current == null && movedHub == null) {
                for (int i = 0; i < result.connectedHubs.Count; i++) result.connectedHubs[i].connected = true;
                for (int i = 0; i < result.strandedHubs.Count; i++) result.strandedHubs[i].connected = false;
            }
        }

        if (applyDrain) {
            for (int i = 0; i < result.strandedHubs.Count; i++) result.strandedHubs[i].connected = false;
            for (int i = 0; i < result.connectedHubs.Count; i++) {
                result.connectedHubs[i].connected = true;
            }
            // A connected supply building charges its store; a cut-off one spends a turn of it.
            for (int i = 0; i < result.connectedBuildings.Count; i++) result.connectedBuildings[i].Replenish(result.connectedBuildings[i].chargePerTurn);
            for (int i = 0; i < spending.Count; i++) spending[i].SpendSupply(1);
        }

        return result;
    }

    /// <summary>Put <paramref name="result"/> on the board as <paramref name="player"/>'s supply (the tiles' fields and their borders).</summary>
    static void Publish(Player player, Result result, System.Func<Result> again) {
        HexGenerator.forAllTiles(t => {
            if (t.supply != null && t.supply.ContainsKey(player)) t.supply[player] = result.DepthAt(t);
            return 0;
        });
        // What the screen shows (real board only): the true network. The game never shows a unit in supply that is not,
        // nor cut off when it is not. Under fog, the one exception is the tile an enemy the screen's side does not see
        // stands on, shown as it would be without the enemies it does not see (FillUnseenHoles): a one-tile hole in its
        // supply would pinpoint it. A line or ring of them cutting supply off shows as cut off (the oddity is a reason to
        // look), and a transit they stop or send round is marked with a contact (UnseenContacts, SupplyContacts). Another
        // side's network: a tile of it the viewer sees shows supplied when it is, though the supplier and the chain up to
        // it are in the fog (HexTilePlayerSupplyDisplay draws it only on tiles the viewer sees).
        if (BoardView.current == null) {
            Player viewer = FillUnseenHoles && FogOfWar.On ? FogOfWar.Viewer : null;
            Dictionary<HexTile, int> filled = null;
            if (viewer != null && Player.Friendly(viewer, player) && again != null) {
                Dictionary<HexTile, List<Unit>> holes = UnseenBy(viewer);
                if (holes != null) {
                    Result known;
                    using (FogOfWar.Concealing(viewer)) known = again();
                    filled = new Dictionary<HexTile, int>();
                    foreach (HexTile h in holes.Keys) filled[h] = known.DepthAt(h);
                }
            }
            // Borders to redraw: only where being supplied (as shown) changed, and beside it.
            List<HexTile> flipped = new List<HexTile>();
            HexGenerator.forAllTiles(t => {
                if (t.supplyShown == null) t.supplyShown = new Dictionary<Player, int>();
                int now, was;
                if (filled == null || !filled.TryGetValue(t, out now)) now = result.DepthAt(t);
                if (!t.supplyShown.TryGetValue(player, out was) || (was > 0) != (now > 0)) flipped.Add(t);
                t.supplyShown[player] = now;
                return 0;
            });
            Published++;
            HexTilePlayerSupplyDisplay.WakeAround(flipped, player);
        }
    }

    /// <summary>
    /// The network with some hubs standing on other tiles, published on the board: a move being
    /// previewed (a transport carrying several supply units, say) shows the supply it would give.
    /// </summary>
    public static Result SolvePreview(Player player, Dictionary<SupplyPoint, HexTile> moved) {
        Result r = Solve(player, false, null, null, null, false, moved);
        Publish(player, r, () => Solve(player, false, null, null, null, false, moved));
        return r;
    }

    /// <summary>
    /// Set (per thread) by an AI planning supply leaps (AISupplyChain): supply units a planned network
    /// has aboard these transports at their planned tiles, linked as cargo is — the transport within a
    /// move of supplied ground — though they are on foot now and board this turn.
    /// </summary>
    [System.ThreadStatic] public static Dictionary<SupplyPoint, Unit> PlannedAboard;

    /// <summary>
    /// Set (per thread) by an AI placing a front-line supply unit: this
    /// supply unit counts as linked wherever it is put — the units behind it will line up to link it —
    /// so where it is placed is judged by what it supplies there.
    /// </summary>
    [System.ThreadStatic] public static SupplyPoint AssumeLinked;
    /// <summary>With <see cref="AssumeLinked"/>: it counts as aboard this transport — linked only where the transport could reach supplied ground in one move.</summary>
    [System.ThreadStatic] public static Unit AssumeLinkedVia;

    /// <summary>
    /// The network as the game plays it on this thread until disposed: none of the AI's per-thread what-if settings
    /// (AssumeLinked, PlannedAboard, RecordLinked); what they were is put back after. (The put-down reading is not one
    /// of them any more: a supply unit aboard is always read as carried.) For the player's supply displays,
    /// and around each of the AI's thoughts (which set them for themselves).
    /// </summary>
    public static System.IDisposable AsPlayed() { return new WhatIfOff(); }
    sealed class WhatIfOff : System.IDisposable {
        readonly bool record = RecordLinked;
        readonly SupplyPoint assume = AssumeLinked; readonly Unit assumeVia = AssumeLinkedVia;
        readonly Dictionary<SupplyPoint, Unit> aboard = PlannedAboard;
        public WhatIfOff() { RecordLinked = false; AssumeLinked = null; AssumeLinkedVia = null; PlannedAboard = null; }
        public void Dispose() { RecordLinked = record; AssumeLinked = assume; AssumeLinkedVia = assumeVia; PlannedAboard = aboard; }
    }


    static HexTile TileOf(SupplyPoint hub, SupplyPoint movedHub, HexTile hypotheticalTile, Dictionary<SupplyPoint, HexTile> moved) {
        if (hub == movedHub && hypotheticalTile != null) return hypotheticalTile;
        HexTile m;
        if (moved != null && moved.TryGetValue(hub, out m) && m != null) return m;
        return hub.myUnit != null ? hub.myUnit.tile : null;
    }

    static readonly List<HexTile> wet = new List<HexTile>();
    /// <summary>Drop the water from <paramref name="r"/>: no sea tile is supplied (see Solve).</summary>
    static void StripWater(Result r) {
        lock (wet) {
            wet.Clear();
            foreach (HexTile t in r.depth.Keys) if (t != null && t.terrain != null && t.terrain.elevation < 0) wet.Add(t);
            foreach (HexTile t in wet) r.depth.Remove(t);
            wet.Clear();
        }
    }

    static int StableId(SupplyPoint sp) {
        if (sp == null || sp.myUnit == null || sp.myUnit.tile == null) return int.MaxValue;
        HexTile t = sp.myUnit.tile;
        return (t.x + 512) * 2048 + (t.y + 512);
    }

    /// <summary>
    /// Buildings push supply out along generic ground movement, while no enemy unit stands on them. Cost model matches
    /// AIPath.StepCost so that the field the AI reasons about is the field the game
    /// actually produces.
    /// </summary>
    static void FloodFromBuilding(Building b, Result result) {
        if (b.tile == null) return;
        // An enemy unit standing on it stops its supply (the flood only ever looked at the tiles it stepped onto, so one
        // on the building's own tile let supply through). An ally's does not.
        Unit on = SupplyOccupier(b.tile);
        if (on != null && on.Strength > 0 && Player.Hostile(on.owner, b.owner)) return;
        Flood(b.tile, b.supplyRange, b.supplyScramble, b.owner, Locomotion.Wheels, result);
    }

    /// <summary>A tile of one of <paramref name="owner"/>'s buildings that services ships (a naval yard): boats come in to it.</summary>
    static bool IsOurYard(HexTile t, Player owner) {
        Building b = t != null ? t.building : null;
        return b != null && b.owner == owner && b.tile == t && b.services != null && b.services.Contains(Locomotion.Ship);
    }

    static bool IsNavalHub(SupplyPoint hub) {
        return hub != null && hub.myUnit != null && !hub.producer && hub.myUnit.trasnporter == null && LocomotionRules.IsNaval(hub.myUnit.locomotion);
    }

    /// <summary>
    /// Is the supply boat on <paramref name="at"/> linked: within its range over water (one step a
    /// tile) of a tile at or beside one of our naval yards that is itself in supply, or of a supply boat
    /// already linked?
    /// </summary>
    static bool NavalLinked(SupplyPoint hub, HexTile at, Player owner, Result result, HashSet<SupplyPoint> activated,
                            SupplyPoint movedHub, HexTile hypotheticalTile, Dictionary<SupplyPoint, HexTile> moved) {
        HashSet<HexTile> linkedBoats = new HashSet<HexTile>();
        foreach (SupplyPoint other in activated) {
            if (other == hub || !IsNavalHub(other)) continue;
            HexTile t = TileOf(other, movedHub, hypotheticalTile, moved);
            if (t != null) linkedBoats.Add(t);
        }
        Dictionary<HexTile, int> cost = new Dictionary<HexTile, int> { { at, 0 } };
        Queue<HexTile> q = new Queue<HexTile>();
        q.Enqueue(at);
        FogOfWar.Knowledge layer = SupplyLayer();
        while (q.Count > 0) {
            HexTile t = q.Dequeue();
            if (t != at && linkedBoats.Contains(t)) return true;
            // At or beside one of our naval yards that is itself in supply (where its docks were).
            if (IsOurYard(t, owner) && result.IsSupplied(t)) return true;
            foreach (HexTile y in t.neighbors) if (IsOurYard(y, owner) && result.IsSupplied(y)) return true;
            if (cost[t] >= hub.range) continue;
            foreach (HexTile n in t.neighbors) {
                if (n == null || n.terrain == null || cost.ContainsKey(n)) continue;
                if (n.terrain.elevation >= 0 && !IsOurYard(n, owner)) continue;   // water (and our yards) only
                Unit no = SupplyOccupier(n, layer);
                if (no != null && Player.Hostile(no.owner, owner)) continue;
                cost[n] = cost[t] + 1;
                q.Enqueue(n);
            }
        }
        return false;
    }

    static int CompareBuildings(Building a, Building b) {
        int ka = a.tile != null ? (a.tile.x + 512) * 2048 + (a.tile.y + 512) : int.MaxValue;
        int kb = b.tile != null ? (b.tile.x + 512) * 2048 + (b.tile.y + 512) : int.MaxValue;
        return ka.CompareTo(kb);
    }

    static void FloodFromHub(SupplyPoint hub, HexTile origin, Result result) {
        FloodFromHub(hub, origin, result, hub != null && hub.myUnit != null ? Outermost(hub.myUnit.trasnporter) : null);
    }

    /// <summary>
    /// Supply from <paramref name="hub"/> on <paramref name="origin"/>, carried by <paramref name="carrier"/> (null: on its
    /// own). Carried by a ship it gives supply only beside the shore; by an aircraft, only over land. Elsewhere it is a
    /// link in its convoy and nothing more (CarriedLinked).
    /// </summary>
    static void FloodFromHub(SupplyPoint hub, HexTile origin, Result result, Unit carrier) {
        Unit u = hub.myUnit;
        if (u == null) return;
        if (carrier != null && carrier != u && origin != null) {
            if (LocomotionRules.IsNaval(carrier.locomotion) && !Unit.BordersLand(origin)) return;
            if (LocomotionRules.IsAirborne(carrier.locomotion) && (origin.terrain == null || origin.terrain.elevation < 0)) return;
        }
        if (IsNavalHub(hub)) {
            // Not across water: onto the land beside it, a step shorter.
            foreach (HexTile n in origin.neighbors) {
                if (n == null || n.terrain == null || n.terrain.elevation < 0) continue;
                Unit no = SupplyOccupier(n);
                if (no != null && Player.Hostile(no.owner, u.owner)) continue;
                Flood(n, hub.range - 1, u.terrainScramble, u.owner, Locomotion.Wheels, result);
            }
            return;
        }

        // Aboard a transport or not, it spreads supply by its own range and rules from where it is
        // (the transport carries it; it does not reach further for riding).
        Flood(origin, hub.range, u.terrainScramble, u.owner, u.locomotion, result);
    }

    /// <summary>What carries <paramref name="hub"/>'s unit: its transport (or the one it is planned aboard), else null.</summary>
    static Unit CarrierOf(SupplyPoint hub, Dictionary<SupplyPoint, HexTile> moved) {
        if (hub == null || hub.myUnit == null) return null;
        Unit t;
        if (PlannedAboard != null && moved != null && moved.ContainsKey(hub) && PlannedAboard.TryGetValue(hub, out t)) return Outermost(t);
        return Outermost(hub.myUnit.trasnporter);
    }

    /// <summary>
    /// What carries <paramref name="t"/> and everything in it: <paramref name="t"/> itself, or what <paramref name="t"/>
    /// rides in, and so on out. Supply in a truck in a ship is carried by the ship: it links and spreads as the
    /// ship's does (ship rules), not the truck's (which cannot move at sea).
    /// </summary>
    public static Unit Outermost(Unit t) {
        int guard = 0;
        while (t != null && t.trasnporter != null && guard++ < 8) t = t.trasnporter;
        return t;
    }

    /// <summary>
    /// Is a supply unit carried by <paramref name="transport"/> standing on <paramref name="at"/> linked? On supplied
    /// ground, yes; else each carrier by its own move (its own path, as it would go: RouteFor) to a source of supply
    /// (a linked supply unit, aboard or not, or a supply building spreading supply), onto it or beside it:
    ///   - a land transport: so (WithinMoveOfSupply);
    ///   - a ship: another linked carrier (a ship or an aircraft with supply aboard) within one move, or beside a source
    ///     on the shore. Convoys chain to any length;
    ///   - an aircraft: so, or a linked ship within one move that gives supply to land (one beside the shore).
    /// What it then gives is FloodFromHub's (a ship: only beside the shore; an aircraft: only over land). A link
    /// through another carrier is recorded (Result.linkedThrough) for the supply line.
    /// </summary>
    static bool CarriedLinked(Unit transport, HexTile at, SupplyPoint hub, List<SupplyPoint> hubs, Result result, HashSet<SupplyPoint> activated,
                              SupplyPoint movedHub, HexTile hypotheticalTile, Dictionary<SupplyPoint, HexTile> moved) {
        if (transport == null || at == null) return false;
        // Its transit, for AIMap's cache (Result.transits): recorded here, in the solve that owns the result.
        result.transits.Add(new KeyValuePair<HexTile, int>(at, HexTactica.AI.AIPath.RangeThisTurn(transport)));
        SupplyPoint via; HexTile landing;
        List<HexTile> route = RouteFor(transport, at, hub, result, hubs, activated, movedHub, hypotheticalTile, moved, out via, out landing);
        if (route == null) return false;
        result.routes[hub] = route;
        if (via != null) result.linkedThrough[hub] = via;
        if (landing != null) result.landings[hub] = landing;
        return true;
    }

    /// <summary>
    /// The one choice of the way a carrier links the supply it carries, for the network (CarriedLinked), the supply line
    /// and the contacts alike. <paramref name="transport"/> on <paramref name="at"/> carrying <paramref name="hub"/>, with
    /// <paramref name="result"/> the supply it could link to and the supply units of <paramref name="hubs"/> in
    /// <paramref name="activated"/> (all of them, if null) those whose carriers it could link through (where the moved ones
    /// stand, and what carries them, as the solve has them):
    /// - on supplied ground: no transit (only its own tile): already in supply is in supply;
    /// - else it must reach a source of supply itself, not just ground some source supplies (SourceTiles): a linked supply
    ///   unit (on foot, or aboard a carrier however deep: where its outermost carrier stands) or a supply building spreading
    ///   supply (the HQ among them), on its tile or beside it (taken or not), in its move (MoveReach), fewest movement points
    ///   first, then the board's order. The supply line (truck to truck to the HQ) is then the rule itself;
    /// - a land carrier or an aircraft: so; an aircraft with none, a convoy (below);
    /// - a ship: a convoy first (the link, and the line drawn for it, is that ship, along the water), else so, beside a
    ///   source (a supply unit on the beach, a naval yard), with the source it reaches (<paramref name="landing"/>: beside
    ///   it, the board's order first);
    /// - a convoy: the carrier first in the board's order whose tile, or one beside it, is in reach (a ship's: a ship's or an
    ///   aircraft's; an aircraft's: a ship's off land), its supply unit <paramref name="via"/>, and the way to that tile or
    ///   beside it, fewest movement points first.
    /// The tiles from <paramref name="at"/>, or null if it does not link.
    /// </summary>
    static List<HexTile> RouteFor(Unit transport, HexTile at, SupplyPoint hub, Result result, List<SupplyPoint> hubs, HashSet<SupplyPoint> activated,
                                  SupplyPoint movedHub, HexTile hypotheticalTile, Dictionary<SupplyPoint, HexTile> moved, out SupplyPoint via, out HexTile landing) {
        via = null; landing = null;
        bool ship = LocomotionRules.IsNaval(transport.locomotion), air = LocomotionRules.IsAirborne(transport.locomotion);
        HashSet<HexTile> sources = null;
        if (!ship) {
            sources = SourceTiles(transport.owner, hub, result, hubs, activated, movedHub, hypotheticalTile, moved);
            List<HexTile> direct = DirectRoute(transport, at, result, sources, out landing);
            if (direct != null) return direct;
            if (!air) return null;
        }
        Reach reach = null;
        SupplyPoint best = null; HexTile bestTile = null;
        foreach (SupplyPoint other in hubs) {
            if (other == null || other == hub || other.producer || other.myUnit == null || (activated != null && !activated.Contains(other))) continue;
            Unit c = CarrierOf(other, moved);
            if (c == null || c == other.myUnit || c == transport || c.owner != transport.owner) continue;
            bool cShip = LocomotionRules.IsNaval(c.locomotion), cAir = LocomotionRules.IsAirborne(c.locomotion);
            HexTile ct = TileOf(other, movedHub, hypotheticalTile, moved);
            if (ct == null) continue;
            if (ship ? !(cShip || cAir) : !(cShip && Unit.BordersLand(ct))) continue;
            if (reach == null) reach = MoveReach(transport, at);
            bool near = reach.Contains(ct);
            if (!near) foreach (HexTile n in ct.neighbors) if (n != null && reach.Contains(n)) { near = true; break; }
            if (near && (best == null || CombatMath.Precedes(ct, bestTile))) { best = other; bestTile = ct; }
        }
        if (best == null) return ship ? DirectRoute(transport, at, result, SourceTiles(transport.owner, hub, result, hubs, activated, movedHub, hypotheticalTile, moved), out landing) : null;
        HexTile target = bestTile;
        HexTile end = reach.Best(t => t == target || t.neighbors.Contains(target));
        via = best;
        return reach.PathTo(end);
    }

    /// <summary>
    /// RouteFor straight to a source: no transit on supplied ground; else to a tile of <paramref name="sources"/> or beside
    /// one in reach (a ship: beside one, with the source it reaches as its landing). Null if none.
    /// </summary>
    static List<HexTile> DirectRoute(Unit transport, HexTile at, Result result, HashSet<HexTile> sources, out HexTile landing) {
        landing = null;
        if (SuppliedGround(at, result)) return new List<HexTile> { at };
        if (sources == null || sources.Count == 0) return null;
        bool ship = LocomotionRules.IsNaval(transport.locomotion);
        Reach reach = MoveReach(transport, at);
        HexTile end = reach.Best(t => (!ship && sources.Contains(t)) || BesideSource(t, sources) != null);
        if (end == null) return null;
        if (ship || !sources.Contains(end)) landing = ship ? BesideSource(end, sources) : null;
        return reach.PathTo(end);
    }

    /// <summary>The source tile beside <paramref name="t"/> first in the board's order, or null.</summary>
    static HexTile BesideSource(HexTile t, HashSet<HexTile> sources) {
        HexTile best = null;
        foreach (HexTile n in t.neighbors) if (n != null && sources.Contains(n) && (best == null || CombatMath.Precedes(n, best))) best = n;
        return best;
    }

    /// <summary>
    /// Where <paramref name="owner"/>'s supply comes from in <paramref name="result"/> so far, for a carrier to reach
    /// (RouteFor): the tile of each supply unit of <paramref name="hubs"/> in <paramref name="activated"/> (all, if null)
    /// but <paramref name="self"/> (aboard: where its outermost carrier stands, however deep it is carried), and of each of
    /// its supply buildings spreading supply (its tile supplied: an enemy on it, or a captured HQ, spreads none).
    /// </summary>
    static HashSet<HexTile> SourceTiles(Player owner, SupplyPoint self, Result result, List<SupplyPoint> hubs, HashSet<SupplyPoint> activated,
                                        SupplyPoint movedHub, HexTile hypotheticalTile, Dictionary<SupplyPoint, HexTile> moved) {
        HashSet<HexTile> sources = new HashSet<HexTile>();
        if (hubs != null)
            foreach (SupplyPoint h in hubs) {
                if (h == null || h == self || (activated != null && !activated.Contains(h))) continue;
                HexTile t = TileOf(h, movedHub, hypotheticalTile, moved);
                if (t != null) sources.Add(t);
            }
        for (int i = 0; i < Building.all.Count; i++) {
            Building b = Building.all[i];
            if (b == null || b.tile == null || b.owner != owner || !b.producesSupply || !b.Functioning) continue;
            if (b.IsHQ && b.Home != owner) continue;
            if (result.DepthAt(b.tile) > 0) sources.Add(b.tile);
        }
        return sources;
    }

    /// <summary>
    /// A carrier's transit from one tile (MoveReach): the tiles it reaches this turn, each with the movement points it takes
    /// and the tile it comes from, so the way to any of them is kept (Best, PathTo), not searched again.
    /// </summary>
    public sealed class Reach {
        public readonly HexTile at;
        readonly Dictionary<HexTile, int> cost = new Dictionary<HexTile, int>();
        readonly Dictionary<HexTile, HexTile> parent = new Dictionary<HexTile, HexTile>();
        public Reach(HexTile at) { this.at = at; }
        List<HexTile> ordered;
        internal void Add(HexTile t, int used, HexTile from) { cost[t] = used; if (from != null) parent[t] = from; ordered = null; }
        /// <summary>Is <paramref name="t"/> reached?</summary>
        public bool Contains(HexTile t) { return t != null && cost.ContainsKey(t); }
        /// <summary>The tiles reached.</summary>
        public ICollection<HexTile> Tiles { get { return cost.Keys; } }
        /// <summary>The tile <paramref name="goal"/> accepts that takes fewest movement points, then the board's order; null if none.</summary>
        public HexTile Best(System.Func<HexTile, bool> goal) {
            if (ordered == null) {   // once per reach: then each ask stops at the first tile that will do
                ordered = new List<HexTile>(cost.Keys);
                ordered.Sort((a, b) => cost[a] != cost[b] ? cost[a].CompareTo(cost[b]) : CombatMath.Precedes(a, b) ? -1 : CombatMath.Precedes(b, a) ? 1 : 0);
            }
            for (int i = 0; i < ordered.Count; i++) if (goal(ordered[i])) return ordered[i];
            return null;
        }
        /// <summary>The tiles from <see cref="at"/> to <paramref name="t"/>, both included (null if not reached).</summary>
        public List<HexTile> PathTo(HexTile t) {
            if (!Contains(t)) return null;
            List<HexTile> path = new List<HexTile>();
            int guard = 0;
            for (HexTile cur = t; cur != null && guard++ < 4096; ) {
                path.Add(cur);
                HexTile p;
                cur = cur != at && parent.TryGetValue(cur, out p) ? p : null;
            }
            path.Reverse();
            return path;
        }
    }

    /// <summary>
    /// The tiles <paramref name="mover"/> on <paramref name="at"/> can reach in one move in transit, its own tile included:
    /// the player's own move field (AIPath.ThisTurnFrom, as HexTile shows moves), so supply reach is exactly movement,
    /// keeping clear of every tile it would be exposed on (TransitField). Where it stands does not count, exposed or not:
    /// only the tiles it moves through.
    /// </summary>
    static Reach MoveReach(Unit mover, HexTile at) {
        Reach reach;
        ReachKey k = new ReachKey { mover = mover, at = at, range = HexTactica.AI.AIPath.RangeThisTurn(mover), steps = HexTactica.AI.AIPath.StepBudget(mover), view = BoardView.current };
        if (reachMemo != null && reachMemo.TryGetValue(k, out reach)) {
            if (HexTactica.AI.AIMap.Verify) { Dictionary<ReachKey, Reach> m = reachMemo; reachMemo = null; HexTactica.AI.AIMap.CountCheck("carrier reach", !new HashSet<HexTile>(MoveReach(mover, at).Tiles).SetEquals(reach.Tiles)); reachMemo = m; }
            return reach;
        }
        long __q = HexTactica.AI.AIPerf.Begin();
        reach = new Reach(at);
        reach.Add(at, 0, null);
        HexTactica.AI.MoveField f = TransitField(mover, at, HexTactica.AI.AIPath.RangeThisTurn(mover));
        foreach (HexTile t in f.Tiles) if (t != at && f.ReachableThisTurn(t)) reach.Add(t, f.Get(t).used, f.ParentOf(t));
        HexTactica.AI.AIPath.Release(f);
        if (reachMemo != null) reachMemo[k] = reach;
        HexTactica.AI.AIPerf.End("supply: carrier reach (memo miss)", __q);
        return reach;
    }

    // ------------------------------------------------------------------ transit

    /// <summary>
    /// The move <paramref name="transport"/> on <paramref name="from"/> could make carrying supply (the supply line follows
    /// it too): its own move field, entering no tile it would be exposed on (Exposed). Whether <paramref name="from"/> is
    /// exposed does not matter (only the tiles it moves through). Release it when done.
    /// </summary>
    public static HexTactica.AI.MoveField TransitField(Unit transport, HexTile from, int range) {
        if (SupplyLayer() == null) return HexTactica.AI.AIPath.ThisTurnFrom(transport, from, range, t => Exposed(transport, t));
        return HexTactica.AI.AIPath.ThisTurnFrom(transport, from, range, t => Exposed(transport, t) || BlockedForSupply(transport, t));
    }

    /// <summary>
    /// Would <paramref name="transport"/> be exposed in transit on <paramref name="t"/>: an enemy unit that could attack it
    /// (CouldAttack) on that tile or beside it? On the board as the thread knows it: the real board for the game's rules,
    /// a side's copy for what it knows (an AI's thinking, the screen's network under fog).
    /// </summary>
    public static bool Exposed(Unit transport, HexTile t) {
        if (transport == null || t == null) return false;
        Dictionary<ExposureKey, Dictionary<HexTile, bool>> memo = exposureMemo;
        if (memo == null) return ExposedNow(transport, t);
        ExposureKey k = new ExposureKey { owner = transport.owner, loco = transport.locomotion, view = BoardView.current };
        Dictionary<HexTile, bool> tiles;
        if (!memo.TryGetValue(k, out tiles)) memo[k] = tiles = new Dictionary<HexTile, bool>();
        bool e;
        if (!tiles.TryGetValue(t, out e)) tiles[t] = e = ExposedNow(transport, t);
        else if (HexTactica.AI.AIMap.Verify) HexTactica.AI.AIMap.CountCheck("transit exposure", ExposedNow(transport, t) != e);
        return e;
    }

    static bool ExposedNow(Unit transport, HexTile t) {
        FogOfWar.Knowledge layer = SupplyLayer();
        if (CouldAttack(SupplyOccupier(t, layer), transport)) return true;
        List<HexTile> ns = t.neighbors;
        for (int i = 0; i < ns.Count; i++) if (ns[i] != null && CouldAttack(SupplyOccupier(ns[i], layer), transport)) return true;
        return false;
    }

    /// <summary>
    /// Who stands on <paramref name="t"/> for supply: its occupier, except on a side's copy made for its AI's thinking
    /// (FogOfWar.Knowledge.supplyAsItIs), where supply goes as it truly is: an enemy unit it does not see stands where it
    /// really is, and one it only believes in is not where it is believed to be. Its movement and fire still go by what it
    /// knows. Elsewhere (the real board, the screen's known board): the occupier.
    /// </summary>
    static Unit SupplyOccupier(HexTile t) { return SupplyOccupier(t, SupplyLayer()); }

    /// <summary>SupplyOccupier with the thread's layer already looked up (SupplyLayer: the hot loops look it up once).</summary>
    static Unit SupplyOccupier(HexTile t, FogOfWar.Knowledge k) {
        Unit u = t.occupier;
        if (k == null) return u;
        bool[] d = k.supplyDiffers;
        int i = t.viewId;
        if (d == null || i < 0 || i >= d.Length || !d[i]) return u;
        if (u != null && k.believed.Contains(u)) u = null;
        Unit hidden;
        if (u == null && k.supplyEnemies.TryGetValue(t, out hidden)) u = hidden;
        return u;
    }

    /// <summary>The knowledge whose supply goes as it truly is (FogOfWar.Knowledge.supplyAsItIs) on this thread's board, or null.</summary>
    static FogOfWar.Knowledge SupplyLayer() {
        BoardView v = BoardView.current;
        FogOfWar.Knowledge k = v != null ? v.Fog : null;
        return k != null && k.supplyAsItIs ? k : null;
    }

    /// <summary>An enemy unit the copy has not got stands on <paramref name="t"/>, for supply (SupplyOccupier): a carrier's transit cannot go there.</summary>
    static bool BlockedForSupply(Unit transport, HexTile t) {
        FogOfWar.Knowledge k = SupplyLayer();
        if (k == null) return false;
        Unit u = SupplyOccupier(t, k);
        return u != null && u != t.occupier && Player.Hostile(u.owner, transport.owner);
    }

    /// <summary>Is an enemy unit the side in WithUnseen does not see standing on <paramref name="t"/> (false outside it)?</summary>
    public static bool UnseenOn(HexTile t) {
        Dictionary<HexTile, List<Unit>> u = unseen;
        return u != null && t != null && u.ContainsKey(t);
    }

    /// <summary>Would <paramref name="transport"/> be exposed on <paramref name="t"/> to an enemy unit the side in WithUnseen does not see (false outside it)?</summary>
    public static bool ExposedToUnseen(Unit transport, HexTile t) {
        Dictionary<HexTile, List<Unit>> u = unseen;
        if (u == null || transport == null || t == null) return false;
        if (UnseenThreatOn(u, t, transport)) return true;
        foreach (HexTile n in t.neighbors) if (n != null && UnseenThreatOn(u, n, transport)) return true;
        return false;
    }

    static bool UnseenThreatOn(Dictionary<HexTile, List<Unit>> u, HexTile t, Unit transport) {
        List<Unit> l;
        if (!u.TryGetValue(t, out l)) return false;
        foreach (Unit e in l) if (CouldAttack(e, transport)) return true;
        return false;
    }

    /// <summary>
    /// Could <paramref name="enemy"/> attack <paramref name="transport"/>: a hostile unit standing on the board (not cargo),
    /// with strength and firepower, whose weapons can target the transport's locomotion (Unit.CanTarget: a fighter does not
    /// intercept a truck). Its ammunition and this turn's state do not count: a unit that could fire on it is a threat.
    /// </summary>
    // (It reads the transport's owner and locomotion only: Exposed's memo keys on them.)
    public static bool CouldAttack(Unit enemy, Unit transport) {
        if (enemy == null || transport == null || enemy == transport) return false;
        if (!Player.Hostile(enemy.owner, transport.owner)) return false;
        if (enemy.trasnporter != null && enemy.trasnporter != enemy) return false;
        return enemy.Strength > 0 && enemy.Firepower > 0 && enemy.CanTarget(transport);
    }

    /// <summary>
    /// Enemy units <see cref="WithUnseen"/>'s side does not see, by the tile they really stand on (a board copy made for
    /// that side has taken them off it). Per thread; null outside the scope.
    /// </summary>
    [System.ThreadStatic] static Dictionary<HexTile, List<Unit>> unseen;

    /// <summary>
    /// Until disposed, the enemy units <paramref name="viewer"/> does not see are known on this thread by where they really
    /// stand (UnseenOn, ExposedToUnseen), for marking where they stop or turn a transit (UnseenContacts). Nothing the
    /// network solves reads them. Made on the real board (enter a concealed
    /// copy inside it, not around it); nothing (no scope) under no fog, for nobody, or inside a copy.
    /// </summary>
    public static System.IDisposable WithUnseen(Player viewer) { return new UnseenScope(viewer); }

    /// <summary>The enemy units on the board <paramref name="viewer"/> does not see, by their tile (real board, under fog; null if none).</summary>
    static Dictionary<HexTile, List<Unit>> UnseenBy(Player viewer) {
        if (viewer == null || !FogOfWar.On || BoardView.current != null || UnitController.uc == null) return null;
        Dictionary<HexTile, List<Unit>> map = null;
        foreach (Unit u in UnitController.uc.units) {
            if (u == null || u.tile == null || u.Strength <= 0 || u.owner == null || !Player.Hostile(u.owner, viewer)) continue;
            if (u.trasnporter != null && u.trasnporter != u) continue;
            if (FogOfWar.Sees(viewer, u)) continue;
            if (map == null) map = new Dictionary<HexTile, List<Unit>>();
            List<Unit> l;
            if (!map.TryGetValue(u.tile, out l)) map[u.tile] = l = new List<Unit>();
            l.Add(u);
        }
        return map;
    }
    sealed class UnseenScope : System.IDisposable {
        readonly Dictionary<HexTile, List<Unit>> outer = unseen;
        public UnseenScope(Player viewer) { Dictionary<HexTile, List<Unit>> map = UnseenBy(viewer); if (map != null) unseen = map; }
        public void Dispose() { unseen = outer; }
    }

    /// <summary>
    /// Known unknowns: where <paramref name="viewer"/>'s supply in transit is stopped, or sent round, by enemy units it does
    /// not see. The network and the supply line show it as it truly is (the cut, or the detour); the contact says why. For
    /// each of its supply units aboard a carrier not on supplied ground: the network's own way for it (Result.routes, none
    /// if it is cut off) against the way RouteFor would choose on the board as its side knows it, to the same supply. Where
    /// they differ, the first tile of the known way exposed to an unseen enemy (ExposedToUnseen), or beside one standing in
    /// its way, is where the contact goes (ContactNear: never on one of the side's or its allies' units, never on the unseen
    /// enemy itself). Real board, main thread; empty without fog or with no enemy unseen. Each tile once, in the board's
    /// order.
    /// </summary>
    public static List<HexTile> UnseenContacts(Player viewer) {
        List<HexTile> contacts = new List<HexTile>();
        if (viewer == null || BoardView.current != null || !FogOfWar.On) return contacts;
        using (AsPlayed()) using (WithUnseen(viewer)) {
            if (unseen == null) return contacts;
            Result real = SolveMoved(viewer, new Dictionary<SupplyPoint, HexTile>());
            foreach (Unit u in viewer.units) {
                SupplyPoint hub = u != null ? u.Hub : null;
                if (hub == null || hub.producer || u.trasnporter == null || u.Strength <= 0) continue;
                Unit carrier = Outermost(u.trasnporter);
                HexTile at = carrier != null ? carrier.tile : null;
                if (at == null) continue;
                Result net = SolveWithout(viewer, hub);   // the supply it links to
                if (SuppliedGround(at, net)) continue;   // no transit
                List<HexTile> truly;
                real.routes.TryGetValue(hub, out truly);
                List<SupplyPoint> convoy = new List<SupplyPoint>(net.connectedHubs);
                List<HexTile> known;
                SupplyPoint via; HexTile landing;
                using (FogOfWar.Concealing(viewer))
                    known = RouteFor(carrier, at, hub, net, convoy, null, null, null, null, out via, out landing);
                if (known == null || (truly != null && SameWay(truly, known))) continue;
                for (int i = 0; i < known.Count; i++) {
                    HexTile t = known[i];
                    // Where it stands is not on its way (exposure there does not count); a tile it moves through is.
                    if ((i == 0 || !ExposedToUnseen(carrier, t)) && !(i + 1 < known.Count && UnseenOn(known[i + 1]))) continue;
                    HexTile c = ContactNear(t, viewer);
                    if (c != null && !contacts.Contains(c)) contacts.Add(c);
                    break;
                }
            }
        }
        contacts.Sort((a, b) => CombatMath.Precedes(a, b) ? -1 : CombatMath.Precedes(b, a) ? 1 : 0);
        return contacts;
    }

    static bool SameWay(List<HexTile> a, List<HexTile> b) {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>
    /// UnseenContacts' marker tile for a stop at <paramref name="t"/>: that tile, else the nearest tile beside it (then the
    /// board's order), that has an unseen enemy on or beside it, holds none of the viewer's side, and is not the unseen
    /// enemy's own. Null if there is none.
    /// </summary>
    static HexTile ContactNear(HexTile t, Player viewer) {
        System.Func<HexTile, bool> ok = c => {
            if (c == null || UnseenOn(c)) return false;
            Unit on = c.occupier;
            if (on != null && Player.Friendly(on.owner, viewer)) return false;
            foreach (HexTile n in c.neighbors) if (UnseenOn(n)) return true;
            return false;
        };
        if (ok(t)) return t;
        List<HexTile> near = new List<HexTile>();
        foreach (HexTile n in t.neighbors) if (ok(n)) near.Add(n);
        if (near.Count == 0) return null;
        near.Sort((a, b) => CombatMath.Precedes(a, b) ? -1 : CombatMath.Precedes(b, a) ? 1 : 0);
        return near[0];
    }

    static bool SuppliedGround(HexTile t, Result result) {
        return t != null && t.terrain != null && t.terrain.elevation >= 0 && result.IsSupplied(t);
    }

    /// <summary>
    /// Can <paramref name="transport"/> standing on <paramref name="at"/> get its cargo to a source of supply in
    /// <paramref name="result"/> in one move (onto it or beside it: RouteFor), moving as it moves (its range, terrain,
    /// fuel and locomotion; enemies block) and in transit (never through a tile on or beside an enemy that could attack
    /// it: Exposed)? Standing on supplied ground, it needs none. A ship, which cannot go ashore, reaches beside one.
    /// </summary>
    public static bool WithinMoveOfSupply(Unit transport, HexTile at, Result result) {
        if (transport == null || at == null) return false;
        HexTile landing;
        return DirectRoute(transport, at, result, SourceTiles(transport.owner, null, result, result.connectedHubs, null, null, null, null), out landing) != null;
    }

    /// <summary>
    /// The farthest this hub could push supply after one move of its own (or its
    /// transport's): its supply range plus its movement, flooded with its own movement
    /// rules from where it stands. An upper bound, for the AI's read of potential supply.
    /// </summary>
    public static Result Extent(SupplyPoint hub) {
        Result r = new Result();
        if (hub == null || hub.myUnit == null || hub.myUnit.tile == null) return r;
        Unit u = hub.myUnit;
        int range = hub.range + u.moveRange;
        int scramble = u.terrainScramble;
        Locomotion loco = u.locomotion;
        Unit carrier = Outermost(u.trasnporter);
        if (carrier != null) {
            // Carried a move, then spreading its own range.
            range = hub.range + carrier.moveRange;
            scramble = Mathf.Max(scramble, carrier.terrainScramble);
            loco = carrier.locomotion;
        }
        HexTile origin = carrier != null && carrier.tile != null ? carrier.tile : u.tile;
        Flood(origin, range, scramble, u.owner, loco, r);
        return r;
    }

    /// <summary>
    /// Dijkstra flood writing depth = range - cost. Enemy units block; friendly units (our own and our allies')
    /// do not. Depth is graded rather than boolean because the gradient is excellent
    /// AI input even though the *rule* (resupply or not) is binary.
    /// </summary>
    static void Flood(HexTile origin, int range, int scramble, Player owner, Locomotion loco, Result result) {
        Flood(origin, range, scramble, owner, loco, result, false);
    }

    /// <param name="asMover">Trace a unit's move rather than supply: water is open to boats (and to
    /// what can cross it), land is closed to them, and the origin counts at depth range.</param>
    static void Flood(HexTile origin, int range, int scramble, Player owner, Locomotion loco, Result result, bool asMover) {
        if (origin == null || range <= 0) return;

        Dictionary<HexTile, int> cost = new Dictionary<HexTile, int>();
        HexTactica.AI.AIHeap<HexTile> open = new HexTactica.AI.AIHeap<HexTile>();
        cost[origin] = 0;
        open.Push(origin, 0);
        FogOfWar.Knowledge layer = SupplyLayer();   // once, not per tile

        while (open.Count > 0) {
            HexTile current = open.Pop();
            int c = cost[current];
            if (c > range) continue;

            int depth = range - c;
            if (depth > result.DepthAt(current)) result.depth[current] = depth;

            List<HexTile> neighbors = current.neighbors;
            for (int i = 0; i < neighbors.Count; i++) {
                HexTile n = neighbors[i];
                if (n == null || n.terrain == null) continue;

                // An enemy unit standing in the corridor severs it.
                Unit no = SupplyOccupier(n, layer);
                if (no != null && Player.Hostile(no.owner, owner)) continue;
                // On and off a bridge only by road, as units move (a harbor is open from every side: LocomotionRules.IsHarbor).
                if (!LocomotionRules.BridgeStep(current, n, loco)) continue;
                // Supply does not cross water over land (supply boats carry it; see FloodFromHub).
                if (n.terrain.elevation < 0 && !LocomotionRules.IgnoresTerrain(loco) && !LocomotionRules.IsNaval(loco)) continue;
                // A boat's move: water only (and its own naval yards).
                if (asMover && LocomotionRules.IsNaval(loco) && n.terrain.elevation >= 0 && !IsOurYard(n, owner)) continue;

                int step;
                if (LocomotionRules.IgnoresTerrain(loco)) step = 1;
                else if (LocomotionRules.UsesRoads(loco) && current.terrain != null
                         && current.terrain.road && n.terrain.road) step = 1;
                else step = 1 + Mathf.Max(0, n.EffectiveMoveability - scramble);

                int nc = c + step;
                if (nc > range) continue;

                int prev;
                if (cost.TryGetValue(n, out prev) && prev <= nc) continue;
                cost[n] = nc;
                open.Push(n, nc);
            }
        }
    }
}
