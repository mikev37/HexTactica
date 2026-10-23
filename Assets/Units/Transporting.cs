using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Loading and unloading. A transport has slots, the locomotion types it carries, and (like every
/// unit) a weight: the slots it takes when it is carried itself. An APC (2 slots, Boots) takes two
/// grunts (weight 1) or one supply unit (weight 2), and no tanks (Treads). A loaded transport can
/// itself be carried; it takes its own weight.
///
/// Boarding is from beside the transport, onto its tile, so the unit must be able to step onto
/// that tile itself (a grunt cannot board a ship on open water) unless the transport stands on a
/// friendly building's tile that serves it (a ship at a dock, an aircraft on a runway). Getting off
/// is onto any neighbouring tile the unit can stand on.
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
    /// Could <paramref name="cargo"/> step onto <paramref name="t"/> to board a transport there: a
    /// tile it can stand on (by road onto a bridge), or a friendly building's tile that serves the
    /// transport (a dock, a runway).
    /// </summary>
    public static bool CanBoardOnto(Unit cargo, HexTile from, HexTile t, Unit transport) {
        if (cargo == null || t == null) return false;
        if (from == t) return true;
        if (cargo.CanEnter(t) && LocomotionRules.BridgeStep(from, t, cargo.locomotion)) return true;
        Building b = t.building;
        return b != null && b.owner == cargo.owner && transport != null && b.Services(transport);
    }

    /// <summary>Boarding: the cargo beside (or on) the transport, and able to step onto its tile (see CanBoardOnto).</summary>
    public static bool CanBoardFrom(Unit transport, Unit cargo) {
        if (!CanCarry(transport, cargo)) return false;
        if (transport.tile == null || cargo.tile == null) return false;
        if (cargo.tile != transport.tile && !transport.tile.neighbors.Contains(cargo.tile)) return false;
        return CanBoardOnto(cargo, cargo.tile, transport.tile, transport);
    }

    public static bool Load(Unit transport, Unit cargo) {
        if (!CanBoardFrom(transport, cargo)) return false;

        if (cargo.tile != null && cargo.tile.occupier == cargo) cargo.tile.occupier = null;
        cargo.tile = transport.tile;
        cargo.trasnporter = transport;
        transport.transported.Add(cargo);
        cargo.active = false;
        cargo.OnBoarded(transport);
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
            if (n == null || (n.occupier != null && n.occupier != transport)) continue;
            if (!cargo.CanEnter(n)) continue;
            if (!LocomotionRules.BridgeStep(at, n, cargo.locomotion)) continue;   // off a bridge only by road
            options.Add(n);
        }
        return options;
    }

    public static bool Unload(Unit transport, Unit cargo, HexTile onto) {
        if (transport == null || cargo == null || onto == null) return false;
        if (!transport.transported.Contains(cargo)) return false;
        if (onto.occupier != null) return false;
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
