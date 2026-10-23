/// <summary>
/// Best guess at a unit's locomotion from stats its prefab already carries. Used by the
/// editor migration and safe to call at runtime.
///
/// The first version mapped terrainScramble to locomotion using Unit's own tooltip
/// ("Wheels 0, Treads 2, Feet 3"). The shipped prefabs do not follow that mapping —
/// the Grunt has scramble 2 and would have become Treads, and Treads cannot capture
/// buildings. These rules use the design doc's distinctions instead, which the prefabs
/// do follow: infantry use up to 3 cover while vehicles use 1; tanks are armoured;
/// helicopters evade; aircraft fly high; ships sit below sea level.
/// </summary>
public static class LocomotionInference {

    public static Locomotion Infer(Unit u) {
        if (u.elevationMax >= 4) return Locomotion.Jet;
        if (u.elevationMax <= 0 && u.elevationMin < 0) return Locomotion.Ship;
        if (u.Evasion >= 3) return Locomotion.Rotor;
        if (u.Concealment >= 2) return Locomotion.Boots;                 // only infantry use real cover
        if (u.Armor >= 4) return Locomotion.Treads;
        if (u.Armor == 0 && u.moveRange <= 3) return Locomotion.Boots;    // unarmoured and slow: on foot
        return Locomotion.Wheels;
    }

    /// <summary>Human-readable reason, for the migration report.</summary>
    public static string Why(Unit u) {
        if (u.elevationMax >= 4) return "elevationMax " + u.elevationMax + " (flies high)";
        if (u.elevationMax <= 0 && u.elevationMin < 0) return "elevation " + u.elevationMin + ".." + u.elevationMax + " (water only)";
        if (u.Evasion >= 3) return "Evasion " + u.Evasion + " (helicopter class)";
        if (u.Concealment >= 2) return "Concealment " + u.Concealment + " (uses infantry cover)";
        if (u.Armor >= 4) return "Armor " + u.Armor + " (tank class)";
        if (u.Armor == 0 && u.moveRange <= 3) return "unarmoured, moveRange " + u.moveRange;
        return "armoured, Concealment " + u.Concealment + " (vehicle)";
    }
}
