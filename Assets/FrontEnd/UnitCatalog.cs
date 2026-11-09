using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The units and terrain the menus list: a unit's name as a player reads it, which faction a unit belongs to, the
/// base units, a faction's units, and the terrain a map is painted with.
/// </summary>
public static class UnitCatalog {

    /// <summary>
    /// A unit's name as shown: a faction's prefab variant ("Tank Coalition Variant") is shown as the unit it stands in
    /// for ("Tank"), its faction shown beside it; a faction's own unit ("Stealth Fighter") by its own name.
    /// </summary>
    public static string DisplayName(Unit u) {
        if (u == null) return "";
        string n = u.name;
        if (n.EndsWith(" Variant")) {
            Faction.Replacement r = ReplacementFor(u);
            if (r != null && r.original != null) return r.original.name;
            return n.Substring(0, n.Length - " Variant".Length).Trim();
        }
        return n;
    }

    /// <summary>The replacement that brings this unit in (a faction's version of a base unit), or null.</summary>
    public static Faction.Replacement ReplacementFor(Unit u) {
        foreach (Faction f in GameAssets.AllFactions)
            if (f.replacements != null) foreach (Faction.Replacement r in f.replacements) if (r != null && r.replacement == u) return r;
        return null;
    }

    /// <summary>The faction this unit belongs to (its version of a base unit, or a unit only it has), or null for a base unit.</summary>
    public static Faction FactionOf(Unit u) {
        foreach (Faction f in GameAssets.AllFactions) foreach (Unit own in f.OwnUnits()) if (own == u) return f;
        return null;
    }

    /// <summary>The units no faction owns: what a side without a faction fields.</summary>
    public static List<Unit> BaseUnits() {
        return GameAssets.AllUnits.Where(u => u != null && FactionOf(u) == null).OrderBy(u => u.name).ToList();
    }

    /// <summary>A faction's own units only: its versions of base units and the units only it has (not the base units it also fields).</summary>
    public static List<Unit> FactionUnits(Faction f) {
        return f.OwnUnits().Where(u => u != null).Distinct().OrderBy(DisplayName).ToList();
    }

    /// <summary>The terrain a map is painted with (TerrainData.inMapEditor), by group (its folder) and then name; terrain with no group first.</summary>
    public static List<TerrainData> PaintableTerrain() {
        return GameAssets.AllTerrains.Where(t => t != null && t.inMapEditor)
            .OrderBy(t => string.IsNullOrEmpty(t.paletteGroup) ? 0 : 1).ThenBy(t => t.paletteGroup ?? "").ThenBy(t => t.name).ToList();
    }
}
