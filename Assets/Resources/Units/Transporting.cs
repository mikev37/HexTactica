using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Loading and unloading. A transport has slots, the locomotion types it carries, and (like every
/// unit) a weight: the slots it takes when it is carried itself. An APC (2 slots, Boots) takes two
/// grunts or supply units (weight 1 each), and no tanks (Treads). A loaded transport can
/// itself be carried; it takes its own weight.
///
/// Boarding is from beside the transport, onto its tile, so the unit must be able to step onto
/// that tile itself (a grunt cannot board a ship on open water) unless the transport stands on a
/// friendly building's tile that serves it (a ship at a dock, an aircraft on a runway). A transport
/// plane (an aircraft that needs a base) takes on cargo only on such a building. Getting off is onto
/// any neighbouring tile the unit can stand on.
/// </summary>
public static class Transporting {

    /// <summary>Slots taken aboard <paramref name="transport"/>: the weights of what it carries (at least 1 each).</summary>
    public static int CarriedWeight(Unit transport) {
        if (transport == null) return 0;
        int w = 0;
        for (int i = 0; i < transport.transported.Count; i++) {
            if (transport.transported[i] != null) w += Mathf.Max(1, transport.transported[i].weight);
        }
        return w;
    }

    public static int FreeWeight(Unit transport) {
        if (transport == null) return 0;
        return Mathf.Max(0, transport.transportSlots - CarriedWeight(transport));
    }

    public static bool IsTransport(Unit u) {
        return u != null && u.transportSlots > 0;
    }

    /// <summary>Does <paramref name="transport"/> carry units that move like <paramref name="cargo"/>?</summary>
    public static bool CarriesKind(Unit transport, Unit cargo) {
        if (transport == null || cargo == null) return false;
        if (transport.carries == null || transport.carries.Count == 0) return cargo.locomotion == Locomotion.Boots;
        return transport.carries.Contains(cargo.locomotion);
    }

    public static bool CanCarry(Unit transport, Unit cargo) {
        if (!IsTransport(transport) || cargo == null) return false;
        if (cargo == transport) return false;
        if (cargo.owner != transport.owner) return false;
        if (cargo.trasnporter != null) return false;
        if (transport.trasnporter != null) return false;   // a transport that is aboard something takes on nobody
        if (!CarriesKind(transport, cargo)) return false;
        if (Mathf.Max(1, cargo.weight) > FreeWeight(transport)) return false;
        return true;
    }

    /// <summary>
    /// Could <paramref name="cargo"/> step onto <paramref name="t"/> to board a transport there: for aircraft only on runway, otherwise anywhere
    /// </summary>
    public static bool CanBoardOnto(Unit cargo, HexTile from, HexTile t, Unit transport) {
        if (cargo == null || t == null) return false;
        Building b = t.building;
        bool servicing = b != null && b.owner == cargo.owner && transport != null && b.Services(transport);
        // An aircraft that needs a base (a transport plane, not a helicopter) takes on cargo only standing on
        // one of our buildings that services it (a runway).
        if (transport != null && LocomotionRules.IsAirborne(transport.locomotion) && LocomotionRules.NeedsBase(transport.locomotion) && !LocomotionRules.ResuppliedInSupply(transport.locomotion)) return servicing;

        return true;
    }

    /// <summary>Boarding: the cargo beside (or on) the transport, and able to step onto its tile (see CanBoardOnto).</summary>
    public static bool CanBoardFrom(Unit transport, Unit cargo) {
        if (!CanCarry(transport, cargo)) return false;
        if (transport.tile == null || cargo.tile == null) return false;
        if (cargo.tile != transport.tile && !transport.tile.neighbors.Contains(cargo.tile)) return false;
        // From where it stands, its whole move ahead of it: the step in must fit it (StepInFits).
        return CanBoardOnto(cargo, cargo.tile, transport.tile, transport) && StepInFits(cargo, cargo.tile, 0, transport);
    }

    public static bool Load(Unit transport, Unit cargo) {
        return cargo != null && BoardFrom(transport, cargo, cargo.tile);
    }

    /// <summary>
    /// Could <paramref name="cargo"/> board <paramref name="transport"/> stepping in from <paramref name="from"/> (beside
    /// it, or its own tile)? <paramref name="from"/> may hold a friendly unit: the cargo only passes through it on its
    /// way in (Unit.WalkAndBoard), it never stops there.
    /// </summary>
    public static bool CanBoardFromTile(Unit transport, Unit cargo, HexTile from) {
        if (!CanCarry(transport, cargo) || transport.tile == null || from == null) return false;
        if (from != transport.tile && !transport.tile.neighbors.Contains(from)) return false;
        Unit o = from.occupier;
        if (o != null && o != cargo && o != transport && o.owner != cargo.owner) return false;
        return CanBoardOnto(cargo, from, transport.tile, transport);
    }

    /// <summary>
    /// Does the step in fit <paramref name="cargo"/>'s move, having spent <paramref name="usedAtFrom"/> getting to
    /// <paramref name="from"/>? A transport on a tile the cargo could walk onto is boarded by moving onto it: that step
    /// costs what the move would (so boarding reaches no further than a move does). One on a tile the cargo cannot enter
    /// (a ship on the water, an aircraft over ground it cannot cross) is boarded from beside it, for nothing.
    /// </summary>
    public static bool StepInFits(Unit cargo, HexTile from, int usedAtFrom, Unit transport) {
        return transport != null && StepInFits(cargo, from, usedAtFrom, transport.tile);
    }

    /// <summary>As above, for a transport that will be standing on <paramref name="t"/> (a rendezvous being planned).</summary>
    public static bool StepInFits(Unit cargo, HexTile from, int usedAtFrom, HexTile t) {
        if (cargo == null || t == null || from == null) return false;
        if (from == t || !cargo.CanEnter(t)) return true;
        int c = HexTactica.AI.AIPath.StepCost(cargo, from, t);
        return c != int.MaxValue && usedAtFrom + c <= HexTactica.AI.AIPath.RangeThisTurn(cargo);
    }

    /// <summary>Movement <paramref name="cargo"/> spends walking <paramref name="steps"/> (its own tile first).</summary>
    public static int WalkCost(Unit cargo, List<HexTile> steps) {
        int c = 0;
        for (int i = 1; steps != null && i < steps.Count; i++) { int s = HexTactica.AI.AIPath.StepCost(cargo, steps[i - 1], steps[i]); if (s == int.MaxValue) return int.MaxValue; c += s; }
        return c;
    }

    /// <summary>Board <paramref name="transport"/> from <paramref name="from"/> (see CanBoardFromTile): the cargo leaves its own tile for the transport's.</summary>
    public static bool BoardFrom(Unit transport, Unit cargo, HexTile from) {
        if (!CanBoardFromTile(transport, cargo, from)) return false;
        return Embark(transport, cargo);
    }

    /// <summary>A unit got into a transport: the transport, the cargo, the tile the cargo stood on before. For logs (not raised in a board view).</summary>
    public static event System.Action<Unit, Unit, HexTile> Boarded;
    /// <summary>A unit got out of a transport onto a tile: the transport, the cargo, the tile. For logs (not raised in a board view).</summary>
    public static event System.Action<Unit, Unit, HexTile> Unloaded;

    /// <summary>
    /// The boarding itself, unchecked: BoardFrom after its checks, and a recorded boarding played again (a replay, a
    /// turn received), where the log is the authority.
    /// </summary>
    internal static bool Embark(Unit transport, Unit cargo) {
        if (transport == null || cargo == null || transport == cargo) return false;
        HexTile stoodOn = cargo.tile;
        if (cargo.tile != null && cargo.tile.occupier == cargo) cargo.tile.occupier = null;
        cargo.tile = transport.tile;
        cargo.trasnporter = transport;
        transport.transported.Add(cargo);
        cargo.active = false;
        cargo.OnBoarded(transport);
        FogOfWar.Changed();
        if (Boarded != null && BoardView.current == null) Boarded(transport, cargo, stoodOn);
        return true;
    }

    /// <summary>Legal disembark tiles: vacant neighbours of the transport the cargo can enter.</summary>
    public static List<HexTile> UnloadOptions(Unit transport, Unit cargo) {
        return UnloadOptions(transport, cargo, transport != null ? transport.tile : null);
    }

    /// <summary>Legal disembark tiles were the transport standing on <paramref name="at"/> (the AI planning a move and drop).</summary>
    public static List<HexTile> UnloadOptions(Unit transport, Unit cargo, HexTile at) {
        List<HexTile> options = new List<HexTile>();
        if (transport == null || at == null || cargo == null) return options;
        List<HexTile> neighbors = at.neighbors;
        for (int i = 0; i < neighbors.Count; i++) {
            HexTile n = neighbors[i];
            // An enemy its side does not see is no obstacle as far as it knows (fog of war).
            Unit o = n != null ? FogOfWar.ApparentOccupier(n, transport.owner) : null;
            if (n == null || (o != null && o != transport)) continue;
            if (!cargo.CanEnter(n)) continue;
            if (!LocomotionRules.BridgeStep(at, n, cargo.locomotion)) continue;   // off a bridge only by road
            options.Add(n);
        }
        return options;
    }

    public static bool Unload(Unit transport, Unit cargo, HexTile onto) {
        if (transport == null || cargo == null || onto == null) return false;
        if (!transport.transported.Contains(cargo)) return false;
        if (onto.occupier != null) {
            // An enemy nobody aboard saw (fog of war): the cargo cannot get off there, the enemy is
            // revealed and fires on the transport (which answers as usual if it can).
            Unit hidden = onto.occupier;
            if (FogOfWar.On && hidden.owner != transport.owner && !FogOfWar.Sees(transport.owner, hidden) && transport.tile != null) {
                FogOfWar.Reveal(hidden, transport.owner);
                if (hidden.Strength > 0 && CombatMath.InFireRange(hidden, hidden.tile, transport.tile, transport) && hidden.CanAttack(transport) && GameController.Targetable(transport))
                    hidden.OrderAttack(transport, true);
                transport.ambushedBy = hidden;
            }
            return false;
        }
        if (!cargo.CanEnter(onto)) return false;
        if (!LocomotionRules.BridgeStep(transport.tile, onto, cargo.locomotion)) return false;
        if (!transport.tile.neighbors.Contains(onto) && onto != transport.tile) return false;

        transport.transported.Remove(cargo);
        cargo.trasnporter = null;
        cargo.tile = onto;
        onto.occupier = cargo;
        // Getting off costs nothing: the unit is as it was aboard (one that has not acted this
        // turn can still move and fight; one that boarded this turn has already used its turn).
        cargo.OnUnloaded(transport);
        FogOfWar.Changed();
        if (Unloaded != null && BoardView.current == null) Unloaded(transport, cargo, onto);
        return true;
    }

    /// <summary>Every transport this player owns that could still take this cargo.</summary>
    public static List<Unit> AvailableFor(Player owner, Unit cargo) {
        List<Unit> result = new List<Unit>();
        if (owner == null || cargo == null) return result;
        for (int i = 0; i < owner.units.Count; i++) {
            Unit u = owner.units[i];
            if (u == null || u.Strength <= 0) continue;
            if (CanCarry(u, cargo)) result.Add(u);
        }
        return result;
    }
}
