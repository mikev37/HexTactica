/// <summary>
/// Locomotive types from the design doc.
///
/// Previously this was implied by <c>Unit.terrainScramble</c> (the tooltip reads
/// "Wheels - 0, Treads - 2, Feet - 3, Flight - 5"), which conflated terrain
/// resistance with locomotion category. They need to be separate: two units can
/// share a scramble value and still differ on roads, fuel burn, and whether they
/// must return to an airfield.
/// </summary>
public enum Locomotion {
    Boots = 0,      // infantry & mechs: low range, crosses anything cheaply
    Wheels = 1,     // long range, heavily terrain-impacted, road bonus
    Treads = 2,     // middling range, standard terrain impact, road bonus
    Rotor = 3,      // ignores terrain, fast, burns fuel hard
    Marine = 4,     // as Boots, plus shallow water adjacent to land
    Aircraft = 5,   // ignores terrain, must return to airfield, fixed fuel burn
    Ship = 6        // water only, long endurance, carries supply
}

public static class LocomotionRules {

    /// <summary>Wheels and treads move faster along connected roads.</summary>
    public static bool UsesRoads(Locomotion l) {
        return l == Locomotion.Wheels || l == Locomotion.Treads;
    }

    /// <summary>Terrain movement cost is ignored entirely by air movement.</summary>
    public static bool IgnoresTerrain(Locomotion l) {
        return l == Locomotion.Rotor || l == Locomotion.Aircraft;
    }

    /// <summary>Air units burn their full per-turn fuel allowance no matter how far they fly.</summary>
    public static bool BurnsFullFuelPerTurn(Locomotion l) {
        return l == Locomotion.Aircraft;
    }

    /// <summary>Units that must periodically return to a friendly airfield to keep flying.</summary>
    public static bool NeedsAirfield(Locomotion l) {
        return l == Locomotion.Aircraft;
    }

    /// <summary>Terrain cover and entrenchment do not protect airborne units.</summary>
    public static bool IsAirborne(Locomotion l) {
        return l == Locomotion.Rotor || l == Locomotion.Aircraft;
    }

    /// <summary>Resupplied (healed, refuelled, rearmed) only on a building that services them: aircraft at airports, ships at naval yards.</summary>
    public static bool NeedsBase(Locomotion l) {
        return l == Locomotion.Aircraft || l == Locomotion.Ship;
    }

    public static bool IsNaval(Locomotion l) {
        return l == Locomotion.Ship;
    }
}
