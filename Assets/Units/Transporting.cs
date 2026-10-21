using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Loading and unloading. Kept out of Unit.cs so the rules live in one readable
/// place: "Transport Slots / the amount of units the transport can carry /
/// Weight: how much space this unit will take while transporting."
/// </summary>
public static class Transporting {

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
        return Mathf.Max(0, transport.maxWeight - CarriedWeight(transport));
    }

    public static bool IsTransport(Unit u) {
        return u != null && u.transportSlots > 0 && u.maxWeight > 0;
    }

    public static bool CanCarry(Unit transport, Unit cargo) {
        if (!IsTransport(transport) || cargo == null) return false;
        if (cargo == transport) return false;
        if (cargo.owner != transport.owner) return false;
        if (cargo.trasnporter != null) return false;
        if (transport.transported.Count >= transport.transportSlots) return false;
        if (Mathf.Max(1, cargo.weight) > FreeWeight(transport)) return false;
        // A transport cannot carry another loaded transport.
        if (cargo.transported.Count > 0) return false;
        return true;
    }

    /// <summary>Boarding requires the cargo to be adjacent to, or standing on, the transport.</summary>
    public static bool CanBoardFrom(Unit transport, Unit cargo) {
        if (!CanCarry(transport, cargo)) return false;
        if (transport.tile == null || cargo.tile == null) return false;
        if (cargo.tile == transport.tile) return true;
        return transport.tile.neighbors.Contains(cargo.tile);
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
        List<HexTile> options = new List<HexTile>();
        if (transport == null || transport.tile == null || cargo == null) return options;
        List<HexTile> neighbors = transport.tile.neighbors;
        for (int i = 0; i < neighbors.Count; i++) {
            HexTile n = neighbors[i];
            if (n == null || n.occupier != null) continue;
            if (!cargo.CanEnter(n)) continue;
            options.Add(n);
        }
        return options;
    }

    public static bool Unload(Unit transport, Unit cargo, HexTile onto) {
        if (transport == null || cargo == null || onto == null) return false;
        if (!transport.transported.Contains(cargo)) return false;
        if (onto.occupier != null) return false;
        if (!cargo.CanEnter(onto)) return false;
        if (!transport.tile.neighbors.Contains(onto) && onto != transport.tile) return false;

        transport.transported.Remove(cargo);
        cargo.trasnporter = null;
        cargo.tile = onto;
        onto.occupier = cargo;
        cargo.active = false;   // it has used its turn getting off
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
