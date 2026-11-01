using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A side's faction: how its units differ from the base units every building lists.
///   - replacements: a base unit and the faction's own version of it (Conscript → "Conscript - Federation",
///     Fighter → Stealth Fighter). Wherever a building lists the base unit, this faction is offered its version.
///   - removed: base units this faction does not have (no building offers them to it).
///   - extras: units only this faction has, each with the kinds of building that make them.
/// Buildings keep one base list; Building.Offered maps it through the owner's faction. A player with no
/// faction is offered the base list unchanged.
/// </summary>
[CreateAssetMenu(menuName = "Faction")]
public class Faction : ScriptableObject {
    public Sprite icon;
    public string description;
    [Tooltip("Not used by production (kept as it was): see replacements, removed and extras")]
    public List<Unit> unitlist;

    [Serializable]
    public class Replacement {
        [Tooltip("The unit buildings list")]
        public Unit original;
        [Tooltip("This faction's version of it (a prefab variant, e.g. \"Conscript - Federation\")")]
        public Unit replacement;
    }

    [Serializable]
    public class Extra {
        [Tooltip("A unit only this faction has")]
        public Unit unit;
        [Tooltip("The kinds of building that make it")]
        public List<BuildingType> builtAt = new List<BuildingType>();
    }

    [Header("Differences from the base units")]
    public List<Replacement> replacements = new List<Replacement>();
    public List<Unit> removed = new List<Unit>();
    public List<Extra> extras = new List<Extra>();

    /// <summary>What this faction builds in place of <paramref name="u"/>: its version, the unit itself, or null when the faction does not have it.</summary>
    public Unit Map(Unit u) {
        if (u == null) return null;
        if (removed != null) foreach (Unit x in removed) if (Same(x, u)) return null;
        if (replacements != null) foreach (Replacement r in replacements) if (r != null && Same(r.original, u) && r.replacement != null) return r.replacement;
        return u;
    }

    /// <summary>The same unit: the same prefab, or one of the same name (a unit is known by its prefab's name, as saves know it).</summary>
    static bool Same(Unit a, Unit b) { return a != null && b != null && (a == b || Name(a) == Name(b)); }
    static string Name(Unit u) { return u.name.Replace("(Clone)", "").Trim(); }

    /// <summary>The base unit <paramref name="u"/> stands in for (itself when it replaces none).</summary>
    public Unit BaseOf(Unit u) {
        if (u != null && replacements != null) foreach (Replacement r in replacements) if (r != null && Same(r.replacement, u) && r.original != null) return r.original;
        return u;
    }

    /// <summary>
    /// The units a building of <paramref name="type"/> listing <paramref name="baseList"/> offers <paramref name="f"/>:
    /// each listed unit mapped through the faction (dropped when removed), then the faction's extras made there. No faction: the list as it is.
    /// </summary>
    public static List<Unit> Offer(Faction f, BuildingType type, List<Unit> baseList) {
        List<Unit> list = new List<Unit>();
        if (baseList != null) foreach (Unit u in baseList) {
            Unit m = f != null ? f.Map(u) : u;
            if (m != null && !list.Exists(x => Same(x, m))) list.Add(m);
        }
        if (f != null && f.extras != null && baseList != null && baseList.Count > 0)
            foreach (Extra e in f.extras) if (e != null && e.unit != null && e.builtAt != null && e.builtAt.Contains(type) && !list.Exists(x => Same(x, e.unit))) list.Add(e.unit);
        return list;
    }

    /// <summary>Every unit this faction can field that is not a base unit (its versions and extras): what a save may name.</summary>
    public IEnumerable<Unit> OwnUnits() {
        if (replacements != null) foreach (Replacement r in replacements) if (r != null && r.replacement != null) yield return r.replacement;
        if (extras != null) foreach (Extra e in extras) if (e != null && e.unit != null) yield return e.unit;
    }
}
