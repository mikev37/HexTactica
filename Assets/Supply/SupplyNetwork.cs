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
/// lies within one move of the transport.
/// </summary>
public static class SupplyNetwork {

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
        public int mobileHubs;
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

    static Result Solve(Player player, bool applyDrain, SupplyPoint movedHub, HexTile hypotheticalTile, SupplyPoint excluded) {
        return Solve(player, applyDrain, movedHub, hypotheticalTile, excluded, false, null);
    }

    static Result Solve(Player player, bool applyDrain, SupplyPoint movedHub, HexTile hypotheticalTile, SupplyPoint excluded,
                        bool staticOnly, Dictionary<SupplyPoint, HexTile> moved) {
        return Solve(player, applyDrain, movedHub, hypotheticalTile, excluded, staticOnly, moved, false);
    }

    static Result Solve(Player player, bool applyDrain, SupplyPoint movedHub, HexTile hypotheticalTile, SupplyPoint excluded,
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
            if (b.IsHQ) FloodFromBuilding(b, result);
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
        bool progress = true;
        int guard = 0;
        while (progress && guard++ < 64) {
            progress = false;
            for (int i = 0; i < hubs.Count; i++) {
                SupplyPoint hub = hubs[i];
                if (activated.Contains(hub)) continue;
                HexTile at = TileOf(hub, movedHub, hypotheticalTile, moved);
                if (at == null) continue;
                if (IsNavalHub(hub)) {
                    // A supply boat links to a friendly dock in supply, or to a linked supply boat,
                    // over water within its range; it supplies only the land it borders.
                    if (!NavalLinked(hub, at, player, result, activated, movedHub, hypotheticalTile, moved)) continue;
                } else if (hub.myUnit != null && hub.myUnit.trasnporter != null) {
                    // Aboard: linked if the transport could carry it onto supplied ground in one move.
                    if (!WithinMoveOfSupply(hub.myUnit.trasnporter, at, result)) continue;
                } else if (!result.IsSupplied(at)) continue;

                activated.Add(hub);
                result.connectedHubs.Add(hub);
                FloodFromHub(hub, at, result);
                progress = true;
            }
            for (int i = 0; i < relayBuildings.Count; i++) {
                Building b = relayBuildings[i];
                if (buildingsOn.Contains(b)) continue;
                bool reached = false;
                foreach (HexTile t in b.Footprint) if (result.IsSupplied(t)) { reached = true; break; }
                if (!reached) continue;
                buildingsOn.Add(b);
                result.connectedBuildings.Add(b);
                FloodFromBuilding(b, result);
                progress = true;
            }
        }

        if (RecordLinked) result.linked = new Dictionary<HexTile, int>(result.depth);
        // Cut-off supply buildings give out of their store while it lasts.
        if (!staticOnly) {
            for (int i = 0; i < relayBuildings.Count; i++) {
                Building b = relayBuildings[i];
                if (buildingsOn.Contains(b) || b.supplies <= 0) continue;
                result.strandedBuildings.Add(b);
                FloodFromBuilding(b, result);
            }
        }

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
        if (excluded == null && !staticOnly && moved == null && !onlyMoved) Publish(player, result);

        if (applyDrain) {
            for (int i = 0; i < result.strandedHubs.Count; i++) result.strandedHubs[i].connected = false;
            for (int i = 0; i < result.connectedHubs.Count; i++) {
                result.connectedHubs[i].connected = true;
            }
            // A connected supply building charges its store; a cut-off one spends a turn of it.
            for (int i = 0; i < result.connectedBuildings.Count; i++) result.connectedBuildings[i].Replenish(result.connectedBuildings[i].chargePerTurn);
            for (int i = 0; i < result.strandedBuildings.Count; i++) result.strandedBuildings[i].SpendSupply(1);
        }

        return result;
    }

    /// <summary>Put <paramref name="result"/> on the board as <paramref name="player"/>'s supply (the tiles' fields and their borders).</summary>
    static void Publish(Player player, Result result) {
        HexGenerator.forAllTiles(t => {
            if (t.supply != null && t.supply.ContainsKey(player)) t.supply[player] = result.DepthAt(t);
            return 0;
        });
        // (Into a board copy, if one is entered: only it changes. The displays are woken for the real board only.)
        if (BoardView.current == null) {
            Published++;
            HexTilePlayerSupplyDisplay.WakeAll();
        }
    }

    /// <summary>
    /// The network with some hubs standing on other tiles, published on the board: a move being
    /// previewed (a transport carrying several supply units, say) shows the supply it would give.
    /// </summary>
    public static Result SolvePreview(Player player, Dictionary<SupplyPoint, HexTile> moved) {
        Result r = Solve(player, false, null, null, null, false, moved);
        Publish(player, r);
        return r;
    }

    static HexTile TileOf(SupplyPoint hub, SupplyPoint movedHub, HexTile hypotheticalTile, Dictionary<SupplyPoint, HexTile> moved) {
        if (hub == movedHub && hypotheticalTile != null) return hypotheticalTile;
        HexTile m;
        if (moved != null && moved.TryGetValue(hub, out m) && m != null) return m;
        return hub.myUnit != null ? hub.myUnit.tile : null;
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
        foreach (HexTile t in b.Footprint) Flood(t, b.supplyRange, b.supplyScramble, b.owner, Locomotion.Wheels, result);
    }

    static bool IsNavalHub(SupplyPoint hub) {
        return hub != null && hub.myUnit != null && !hub.producer && hub.myUnit.trasnporter == null && LocomotionRules.IsNaval(hub.myUnit.locomotion);
    }

    /// <summary>
    /// Is the supply boat on <paramref name="at"/> linked: within its range over water (one step a
    /// tile) of a dock of one of our naval yards that is itself in supply, or of a supply boat already
    /// linked?
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
            Building b = t.building;
            if (b != null && b.owner == owner && b.IsServiceTile(t)) {
                foreach (HexTile f in b.Footprint) if (result.IsSupplied(f)) return true;
            }
            if (cost[t] >= hub.range) continue;
            foreach (HexTile n in t.neighbors) {
                if (n == null || n.terrain == null || cost.ContainsKey(n)) continue;
                if (n.terrain.elevation >= 0 && !(n.building != null && n.building.IsServiceTile(n))) continue;   // water (and docks) only
                if (n.occupier != null && n.occupier.owner != owner) continue;
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
        Unit u = hub.myUnit;
        if (u == null) return;
        if (IsNavalHub(hub)) {
            // Not across water: onto the land beside it, a step shorter.
            foreach (HexTile n in origin.neighbors) {
                if (n == null || n.terrain == null || n.terrain.elevation < 0) continue;
                if (n.occupier != null && n.occupier.owner != u.owner) continue;
                Flood(n, hub.range - 1, u.terrainScramble, u.owner, Locomotion.Wheels, result);
            }
            return;
        }

        // Aboard a transport or not, it spreads supply by its own range and rules from where it is
        // (the transport carries it; it does not reach further for riding).
        Flood(origin, hub.range, u.terrainScramble, u.owner, u.locomotion, result);
    }

    /// <summary>
    /// Is there supplied ground within one move of <paramref name="transport"/> standing on
    /// <paramref name="at"/>, moving as it moves (its range, terrain and locomotion; enemies block)?
    /// </summary>
    static bool WithinMoveOfSupply(Unit transport, HexTile at, Result result) {
        if (transport == null || at == null) return false;
        if (result.IsSupplied(at)) return true;
        Result reach = new Result();
        Flood(at, transport.moveRange, transport.terrainScramble, transport.owner, transport.locomotion, reach, true);
        // Onto supplied ground, or beside it (the cargo can step off there).
        foreach (HexTile t in reach.depth.Keys) {
            if (result.IsSupplied(t)) return true;
            foreach (HexTile n in t.neighbors) if (n != null && result.IsSupplied(n)) return true;
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
        if (u.trasnporter != null) {
            // Carried a move, then spreading its own range.
            range = hub.range + u.trasnporter.moveRange;
            scramble = Mathf.Max(scramble, u.trasnporter.terrainScramble);
            loco = u.trasnporter.locomotion;
        }
        HexTile origin = u.trasnporter != null && u.trasnporter.tile != null ? u.trasnporter.tile : u.tile;
        Flood(origin, range, scramble, u.owner, loco, r);
        return r;
    }

    /// <summary>
    /// Dijkstra flood writing depth = range - cost. Enemy units block; friendly units
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
                if (n.occupier != null && n.occupier.owner != owner) continue;
                // On and off a bridge only by road, as units move.
                if (!LocomotionRules.BridgeStep(current, n, loco)) continue;
                // Supply does not cross water over land (supply boats carry it; see FloodFromHub).
                if (n.terrain.elevation < 0 && !LocomotionRules.IgnoresTerrain(loco) && !LocomotionRules.IsNaval(loco)) continue;
                // A boat's move: water only (and its own docks).
                if (asMover && LocomotionRules.IsNaval(loco) && n.terrain.elevation >= 0 && !(n.building != null && n.building.IsServiceTile(n))) continue;

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
