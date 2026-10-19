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
/// So connection to HQ is not a nicety, it is the difference between a sustainable
/// line and a burning fuse. A hub cut off from the network keeps supplying the
/// units around it and pays for that out of its own health, six turns at most.
///
/// Propagation uses the *supplier's own movement rules*, which is what makes roads
/// matter and lets enemy units physically sever a corridor. A hub riding a transport
/// projects along the transport's movement instead — "the supply range is extended
/// by the range of any transportation that the unit is on."
/// </summary>
public static class SupplyNetwork {

    /// <summary>Result of a solve, for the AI and the harness to inspect.</summary>
    public class Result {
        public readonly Dictionary<HexTile, int> depth = new Dictionary<HexTile, int>();
        /// <summary>Producers and every relay chained back to a producer or supply building.</summary>
        public readonly List<SupplyPoint> connectedHubs = new List<SupplyPoint>();
        public readonly List<SupplyPoint> strandedHubs = new List<SupplyPoint>();

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

    static Result Solve(Player player, bool applyDrain, SupplyPoint movedHub, HexTile hypotheticalTile, SupplyPoint excluded) {

        Result result = new Result();
        if (player == null || HexGenerator.hg == null) return result;

        // Seed from every supply-producing building this player owns.
        Queue<SupplyPoint> pending = new Queue<SupplyPoint>();
        HashSet<SupplyPoint> activated = new HashSet<SupplyPoint>();

        for (int i = 0; i < Building.all.Count; i++) {
            Building b = Building.all[i];
            if (b == null || b.owner != player || !b.HasSupplyToGive) continue;
            FloodFromBuilding(b, result);
        }

        // Gather this player's hubs, in a deterministic order.
        List<SupplyPoint> hubs = new List<SupplyPoint>();
        for (int i = 0; i < player.units.Count; i++) {
            Unit u = player.units[i];
            if (u == null) continue;
            SupplyPoint sp = u.GetComponent<SupplyPoint>();
            if (sp != null && sp != excluded) hubs.Add(sp);
        }
        hubs.Sort((a, b) => StableId(a).CompareTo(StableId(b)));

        // Producer hubs are roots, exactly like supply buildings. The shipped game's HQ
        // is a *unit* (TempHQ) carrying a producer SupplyPoint, not a Building. Treating
        // it as a relay meant it could never connect to anything, was classed as stranded,
        // and paid one strength per turn until it died on turn six — taking every relay
        // hub's connection with it.
        for (int i = 0; i < hubs.Count; i++) {
            SupplyPoint hub = hubs[i];
            if (!hub.producer) continue;
            HexTile at = TileOf(hub, movedHub, hypotheticalTile);
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
                HexTile at = TileOf(hub, movedHub, hypotheticalTile);
                if (at == null) continue;
                if (!result.IsSupplied(at)) continue;

                activated.Add(hub);
                result.connectedHubs.Add(hub);
                FloodFromHub(hub, at, result);
                progress = true;
            }
        }

        // Hubs that never connected still supply their surroundings, out of pocket.
        for (int i = 0; i < hubs.Count; i++) {
            SupplyPoint hub = hubs[i];
            if (activated.Contains(hub)) continue;
            HexTile at = TileOf(hub, movedHub, hypotheticalTile);
            if (at == null) continue;
            result.strandedHubs.Add(hub);
            FloodFromHub(hub, at, result);
        }

        // Publish onto the board — but never a counterfactual network missing a hub.
        if (excluded == null) HexGenerator.forAllTiles(t => {
            if (t.supply != null && t.supply.ContainsKey(player)) t.supply[player] = result.DepthAt(t);
            return 0;
        });

        if (applyDrain) {
            for (int i = 0; i < result.strandedHubs.Count; i++) {
                SupplyPoint hub = result.strandedHubs[i];
                hub.connected = false;
                if (hub.myUnit != null) {
                    hub.myUnit.Strength = Mathf.Max(0, hub.myUnit.Strength - 1);
                    if (hub.myUnit.Strength <= 0) hub.myUnit.Delete();
                }
            }
            for (int i = 0; i < result.connectedHubs.Count; i++) {
                result.connectedHubs[i].connected = true;
            }
        }

        return result;
    }

    static HexTile TileOf(SupplyPoint hub, SupplyPoint movedHub, HexTile hypotheticalTile) {
        if (hub == movedHub && hypotheticalTile != null) return hypotheticalTile;
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
        Flood(b.tile, b.supplyRange, b.supplyScramble, b.owner, Locomotion.Wheels, result);
    }

    static void FloodFromHub(SupplyPoint hub, HexTile origin, Result result) {
        Unit u = hub.myUnit;
        if (u == null) return;

        int range = hub.range;
        int scramble = u.terrainScramble;
        Locomotion loco = u.locomotion;

        // Riding a transport extends reach using the transport's legs.
        if (u.trasnporter != null) {
            range += u.trasnporter.moveRange;
            scramble = Mathf.Max(scramble, u.trasnporter.terrainScramble);
            loco = u.trasnporter.locomotion;
        }

        Flood(origin, range, scramble, u.owner, loco, result);
    }

    /// <summary>
    /// Dijkstra flood writing depth = range - cost. Enemy units block; friendly units
    /// do not. Depth is graded rather than boolean because the gradient is excellent
    /// AI input even though the *rule* (resupply or not) is binary.
    /// </summary>
    static void Flood(HexTile origin, int range, int scramble, Player owner, Locomotion loco, Result result) {
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
