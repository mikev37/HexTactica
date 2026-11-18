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
/// the carrier (Exposed), its own tile included. Supply units on foot are not in transit: they stand in supply.
/// </summary>
public static class SupplyNetwork {

    /// <summary>
    /// The HQ first: a cut-off supply building (a FOB) spends a point of its store only on a turn it feeds a unit that
    /// the HQ's network (or a building before it) does not supply; otherwise it keeps its store (its area is still
    /// shown supplied). Off: every cut-off building with store spends a point a turn.
    /// </summary>
    public static bool StoreOnlyWhenNeeded = true;

    /// <summary>Under fog, the screen's player's own supply as the screen shows it (borders, out-of-supply icons) is worked out on the board as it knows it (HexTile.supplyShown): enemy units it does not see do not cut it. Other sides' supply is shown as it truly is, on the tiles the viewer sees. Off: the true network is shown.</summary>
    public static bool ShownAsKnown = true;

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
        /// Every carrier's transit this solve looked at: where it stood and its move range. A link through one depends on
        /// the units within that range + 1 of it (on its way, or beside it), not only on those on or beside the field
        /// (AIMap keeps a network only while none of them changed).
        /// </summary>
        public readonly List<KeyValuePair<HexTile, int>> transits = new List<KeyValuePair<HexTile, int>>();
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
        Dictionary<ReachKey, HashSet<HexTile>> outer = reachMemo;
        if (outer == null) reachMemo = new Dictionary<ReachKey, HashSet<HexTile>>();
        try { return SolveOnce(player, applyDrain, movedHub, hypotheticalTile, excluded, staticOnly, moved, onlyMoved); }
        finally { reachMemo = outer; }
    }

    /// <summary>
    /// Carriers' move reaches kept until the scope is disposed, across every solve in it: for a planner that solves
    /// many hypothetical placements on one board (nothing on the board may move inside the scope).
    /// </summary>
    public static System.IDisposable KeepReach() { return new ReachScope(); }
    sealed class ReachScope : System.IDisposable {
        readonly Dictionary<ReachKey, HashSet<HexTile>> outer = reachMemo;
        public ReachScope() { if (outer == null) reachMemo = new Dictionary<ReachKey, HashSet<HexTile>>(); }
        public void Dispose() { reachMemo = outer; }
    }
    struct ReachKey : System.IEquatable<ReachKey> {
        public Unit mover; public HexTile at; public int range, steps; public object unseen;
        public bool Equals(ReachKey o) { return ReferenceEquals(mover, o.mover) && ReferenceEquals(at, o.at) && range == o.range && steps == o.steps && ReferenceEquals(unseen, o.unseen); }
        public override bool Equals(object o) { return o is ReachKey && Equals((ReachKey)o); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(mover) * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(at); }
    }
    [System.ThreadStatic] static Dictionary<ReachKey, HashSet<HexTile>> reachMemo;

    static Result SolveOnce(Player player, bool applyDrain, SupplyPoint movedHub, HexTile hypotheticalTile, SupplyPoint excluded,
                        bool staticOnly, Dictionary<SupplyPoint, HexTile> moved, bool onlyMoved) {

        Result result = new Result();
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
        // What the screen shows (real board only). The screen's player's own network is worked out on the board as it
        // knows it, under fog: an enemy it does not see does not cut it (that would give the enemy away). Another side's
        // network is the true one: a tile of it the viewer sees shows supplied when it is, though the supplier and the
        // chain up to it are in the fog (HexTilePlayerSupplyDisplay draws it only on tiles the viewer sees).
        if (BoardView.current == null) {
            Result shown = result;
            Player viewer = ShownAsKnown && FogOfWar.On ? FogOfWar.Viewer : null;
            // Transit is the exception: a carrier's way blocked by an enemy it does not see is blocked as shown too (the
            // supply really is cut), and SupplyContacts marks a contact where that happened, to say why.
            if (viewer != null && viewer == player && again != null) using (WithUnseen(viewer)) using (FogOfWar.Concealing(viewer)) shown = again();
            // Borders to redraw: only where being supplied (as shown) changed, and beside it.
            List<HexTile> flipped = new List<HexTile>();
            HexGenerator.forAllTiles(t => {
                if (t.supplyShown == null) t.supplyShown = new Dictionary<Player, int>();
                int now = shown.DepthAt(t), was;
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
    /// Buildings push supply out along generic ground movement. Cost model matches
    /// AIPath.StepCost so that the field the AI reasons about is the field the game
    /// actually produces.
    /// </summary>
    static void FloodFromBuilding(Building b, Result result) {
        if (b.tile == null) return;
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
                if (n.occupier != null && Player.Hostile(n.occupier.owner, owner)) continue;
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
                if (n.occupier != null && Player.Hostile(n.occupier.owner, u.owner)) continue;
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
    /// Is a supply unit carried by <paramref name="transport"/> standing on <paramref name="at"/> linked? Each carrier by
    /// its own move (its own path, as it would go):
    ///   - a land transport: supplied ground within one move of it (WithinMoveOfSupply);
    ///   - a ship: supplied ground within one move (a supplied harbor, or land beside water it reaches), or another
    ///     linked carrier — a ship or an aircraft with supply aboard — within one move. Convoys chain to any length;
    ///   - an aircraft: supplied ground within one move, or a linked ship within one move that gives supply to land
    ///     (one beside the shore).
    /// What it then gives is FloodFromHub's (a ship: only beside the shore; an aircraft: only over land). A link
    /// through another carrier is recorded (Result.linkedThrough) for the supply line.
    /// </summary>
    static bool CarriedLinked(Unit transport, HexTile at, SupplyPoint hub, List<SupplyPoint> hubs, Result result, HashSet<SupplyPoint> activated,
                              SupplyPoint movedHub, HexTile hypotheticalTile, Dictionary<SupplyPoint, HexTile> moved) {
        if (transport == null || at == null) return false;
        bool ship = LocomotionRules.IsNaval(transport.locomotion), air = LocomotionRules.IsAirborne(transport.locomotion);
        // A ship tries its convoy first: the shore another ship supplies is ground it could reach, but the link (and
        // the line drawn for it) is that ship, along the water — not across the land beside them both.
        // Its transit, for AIMap's cache (Result.transits): recorded here, in the solve that owns the result.
        result.transits.Add(new KeyValuePair<HexTile, int>(at, HexTactica.AI.AIPath.RangeThisTurn(transport)));
        if (!ship && WithinMoveOfSupply(transport, at, result)) return true;
        if (!ship && !air) return false;
        HashSet<HexTile> reach = null;
        SupplyPoint best = null; HexTile bestTile = null;
        foreach (SupplyPoint other in hubs) {
            if (other == null || other == hub || other.producer || !activated.Contains(other) || other.myUnit == null) continue;
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
        if (best == null) return ship && WithinMoveOfSupply(transport, at, result);
        result.linkedThrough[hub] = best;
        return true;
    }

    /// <summary>
    /// The tiles <paramref name="mover"/> on <paramref name="at"/> can reach in one move in transit, its own tile included:
    /// the player's own move field (AIPath.ThisTurnFrom, as HexTile shows moves), so supply reach is exactly movement,
    /// keeping clear of every tile it would be exposed on (TransitField). Exposed where it stands: nothing at all.
    /// </summary>
    static HashSet<HexTile> MoveReach(Unit mover, HexTile at) {
        HashSet<HexTile> reach;
        ReachKey k = new ReachKey { mover = mover, at = at, range = HexTactica.AI.AIPath.RangeThisTurn(mover), steps = HexTactica.AI.AIPath.StepBudget(mover), unseen = unseen };
        if (reachMemo != null && reachMemo.TryGetValue(k, out reach)) {
            if (HexTactica.AI.AIMap.Verify) { Dictionary<ReachKey, HashSet<HexTile>> m = reachMemo; reachMemo = null; HexTactica.AI.AIMap.CountCheck("carrier reach", !MoveReach(mover, at).SetEquals(reach)); reachMemo = m; }
            return reach;
        }
        reach = new HashSet<HexTile>();
        if (!Exposed(mover, at)) {
            reach.Add(at);
            HexTactica.AI.MoveField f = TransitField(mover, at, HexTactica.AI.AIPath.RangeThisTurn(mover));
            foreach (HexTile t in f.Tiles) if (f.ReachableThisTurn(t)) reach.Add(t);
            HexTactica.AI.AIPath.Release(f);
        }
        if (reachMemo != null) reachMemo[k] = reach;
        return reach;
    }

    // ------------------------------------------------------------------ transit

    /// <summary>
    /// The move <paramref name="transport"/> on <paramref name="from"/> could make carrying supply (the supply line follows
    /// it too): its own move field, entering no tile it would be exposed on (Exposed). Whether <paramref name="from"/> is
    /// exposed is the caller's to ask (a carrier exposed where it stands has no transit). Release it when done.
    /// </summary>
    public static HexTactica.AI.MoveField TransitField(Unit transport, HexTile from, int range) {
        return HexTactica.AI.AIPath.ThisTurnFrom(transport, from, range, t => Exposed(transport, t));
    }

    /// <summary>
    /// Would <paramref name="transport"/> be exposed in transit on <paramref name="t"/>: an enemy unit that could attack it
    /// (CouldAttack) on that tile or beside it? On the board as the thread knows it (an AI: the enemies it knows of), and,
    /// inside WithUnseen, the enemies that side does not see as well.
    /// </summary>
    public static bool Exposed(Unit transport, HexTile t) {
        if (transport == null || t == null) return false;
        if (CouldAttack(t.occupier, transport)) return true;
        List<HexTile> ns = t.neighbors;
        for (int i = 0; i < ns.Count; i++) if (ns[i] != null && CouldAttack(ns[i].occupier, transport)) return true;
        return unseen != null && ExposedToUnseen(transport, t);
    }

    /// <summary>Exposed on <paramref name="t"/> to an enemy unit the side in WithUnseen does not see (false outside it).</summary>
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
    /// Until disposed, transit on this thread also keeps clear of the enemy units <paramref name="viewer"/> does not see
    /// (Exposed). For the screen only (what the player's own supply shows, and its supply line): the network the game
    /// plays by already has every unit on the board, and an AI plans with what it knows. Made on the real board (enter a
    /// concealed copy inside it, not around it); nothing (no scope) under no fog, for nobody, or inside a copy.
    /// </summary>
    public static System.IDisposable WithUnseen(Player viewer) { return new UnseenScope(viewer); }
    sealed class UnseenScope : System.IDisposable {
        readonly Dictionary<HexTile, List<Unit>> outer = unseen;
        public UnseenScope(Player viewer) {
            if (viewer == null || !FogOfWar.On || BoardView.current != null || UnitController.uc == null) return;
            Dictionary<HexTile, List<Unit>> map = new Dictionary<HexTile, List<Unit>>();
            foreach (Unit u in UnitController.uc.units) {
                if (u == null || u.tile == null || u.Strength <= 0 || u.owner == null || !Player.Hostile(u.owner, viewer)) continue;
                if (u.trasnporter != null && u.trasnporter != u) continue;
                if (FogOfWar.Sees(viewer, u)) continue;
                List<Unit> l;
                if (!map.TryGetValue(u.tile, out l)) map[u.tile] = l = new List<Unit>();
                l.Add(u);
            }
            if (map.Count > 0) unseen = map;
        }
        public void Dispose() { unseen = outer; }
    }

    /// <summary>
    /// Known unknowns: where <paramref name="viewer"/>'s supply in transit is cut off, or sent another way, by an enemy it
    /// does not see. For each of its supply units aboard a carrier that is not on supplied ground: the way the carrier
    /// would go knowing only what its side sees (fewest movement points to the nearest tile it would link from, then the
    /// board's order, as the supply line picks it); the first tile on that way exposed only to an unseen enemy is a
    /// contact. Only the tile is told, never the enemy: it is somewhere on or beside it. Worked out on the board as the
    /// viewer knows it, with the unseen enemies' threat (WithUnseen), as the screen shows the network. Real board, main
    /// thread; empty without fog, or with no enemy unseen. Each tile once, in the board's order.
    /// </summary>
    public static List<HexTile> UnseenContacts(Player viewer) {
        List<HexTile> contacts = new List<HexTile>();
        if (viewer == null || BoardView.current != null) return contacts;
        using (AsPlayed()) using (WithUnseen(viewer)) {
            if (unseen == null) return contacts;
            using (FogOfWar.Concealing(viewer)) {
                for (int i = 0; i < viewer.units.Count; i++) {
                    Unit u = viewer.units[i];
                    SupplyPoint hub = u != null ? u.Hub : null;
                    if (hub == null || hub.producer || u.trasnporter == null || u.Strength <= 0) continue;
                    Unit carrier = Outermost(u.trasnporter);
                    HexTile at = carrier != null ? carrier.tile : null;
                    if (at == null) continue;
                    Result net = SolveWithout(viewer, hub);
                    if (SuppliedGround(at, net)) continue;   // no transit: it is on supplied ground
                    HexTile c = ContactOnWay(carrier, at, hub, net);
                    if (c != null && !contacts.Contains(c)) contacts.Add(c);
                }
            }
        }
        contacts.Sort((a, b) => CombatMath.Precedes(a, b) ? -1 : CombatMath.Precedes(b, a) ? 1 : 0);
        return contacts;
    }

    /// <summary>UnseenContacts for one carrier: the first tile of its way, as its side sees it, exposed to an unseen enemy; null if none.</summary>
    static HexTile ContactOnWay(Unit carrier, HexTile at, SupplyPoint hub, Result net) {
        Dictionary<HexTile, List<Unit>> all = unseen;
        HexTactica.AI.MoveField field = null;
        bool exposedSeen;
        unseen = null;   // the way as its side sees it: the threat it knows of only
        try {
            exposedSeen = Exposed(carrier, at);
            if (!exposedSeen) field = TransitField(carrier, at, HexTactica.AI.AIPath.RangeThisTurn(carrier));
        } finally { unseen = all; }
        if (exposedSeen) return null;   // cut by an enemy it sees: nothing to explain
        bool ship = LocomotionRules.IsNaval(carrier.locomotion), air = LocomotionRules.IsAirborne(carrier.locomotion);
        // The tiles it would link from: supplied ground (beside it, for a ship), or a linked carrier it would convoy through (CarriedLinked).
        HashSet<HexTile> convoy = new HashSet<HexTile>();
        if (ship || air)
            foreach (SupplyPoint other in net.connectedHubs) {
                if (other == null || other == hub || other.producer || other.myUnit == null) continue;
                Unit oc = Outermost(other.myUnit.trasnporter);
                if (oc == null || oc == carrier || oc.owner != carrier.owner || oc.tile == null) continue;
                bool cShip = LocomotionRules.IsNaval(oc.locomotion), cAir = LocomotionRules.IsAirborne(oc.locomotion);
                if (ship ? !(cShip || cAir) : !(cShip && Unit.BordersLand(oc.tile))) continue;
                convoy.Add(oc.tile);
                foreach (HexTile n in oc.tile.neighbors) if (n != null) convoy.Add(n);
            }
        HexTile best = null; int bestCost = int.MaxValue;
        foreach (HexTile t in field.Tiles) {
            if (t != at && !field.ReachableThisTurn(t)) continue;
            bool goal = convoy.Contains(t) || (!ship && SuppliedGround(t, net));
            if (!goal && ship) foreach (HexTile n in t.neighbors) if (SuppliedGround(n, net)) { goal = true; break; }
            if (!goal) continue;
            int cost = t == at ? 0 : field.Get(t).used;
            if (best == null || cost < bestCost || (cost == bestCost && CombatMath.Precedes(t, best))) { best = t; bestCost = cost; }
        }
        List<HexTile> way = best == null ? null : best == at ? new List<HexTile> { at } : field.PathTo(best);
        HexTactica.AI.AIPath.Release(field);
        if (way == null) return null;   // no way even as it sees the board: cut by what it sees
        foreach (HexTile t in way) if (ExposedToUnseen(carrier, t)) return t;
        return null;
    }

    static bool SuppliedGround(HexTile t, Result result) {
        return t != null && t.terrain != null && t.terrain.elevation >= 0 && result.IsSupplied(t);
    }

    /// <summary>
    /// Can <paramref name="transport"/> standing on <paramref name="at"/> get its cargo to supplied ground in one move,
    /// moving as it moves (its range, terrain, fuel and locomotion; enemies block) and in transit (never on or beside an
    /// enemy that could attack it: Exposed)? A land transport or an aircraft must
    /// reach a supplied ground tile itself; a ship, which cannot go ashore, one beside the water it reaches (or a
    /// supplied harbor it can enter). Water is never supplied ground (a ship's supply covers the water it floats on,
    /// but nothing links to that).
    /// </summary>
    public static bool WithinMoveOfSupply(Unit transport, HexTile at, Result result) {
        if (transport == null || at == null) return false;
        if (SuppliedGround(at, result)) return true;
        bool ship = LocomotionRules.IsNaval(transport.locomotion);
        foreach (HexTile t in MoveReach(transport, at)) {
            if (SuppliedGround(t, result)) return true;
            if (ship) foreach (HexTile n in t.neighbors) if (SuppliedGround(n, result)) return true;
        }
        return false;
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
                if (n.occupier != null && Player.Hostile(n.occupier.owner, owner)) continue;
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
