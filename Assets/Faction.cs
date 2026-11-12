using System.Linq;
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
    [Tooltip("The faction's full name, as the campaign uses it (\"Blue Sun Federation\"). Empty: the asset's name.")]
    public string campaignName = "";
    [Tooltip("The colour of the flag of a campaign mission told from this faction's point of view.")]
    public Color flagColor = Color.white;
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

    /// <summary>
    /// Do factions field their special units (GameController.factionSpecialUnits)? Off: a faction only changes how
    /// units look — only its versions that are cosmetic (<see cref="Cosmetic"/>), no extras, nothing removed.
    /// </summary>
    public static bool SpecialUnits { get { GameController gc = GameController.gc; return gc == null || gc.factionSpecialUnits; } }

    /// <summary>What this faction builds in place of <paramref name="u"/>: its version, the unit itself, or null when the faction does not have it.</summary>
    public Unit Map(Unit u) {
        if (u == null) return null;
        bool special = SpecialUnits;
        if (special && removed != null) foreach (Unit x in removed) if (Same(x, u)) return null;
        if (replacements != null) foreach (Replacement r in replacements)
            if (r != null && Same(r.original, u) && r.replacement != null && (special || Cosmetic(r))) return r.replacement;
        return u;
    }

    /// <summary>
    /// A version that only looks different: its unit (the component on the prefab's top-level object) has the same
    /// value as the original's in every attribute — every serialized number, flag, enum and text; references to
    /// sprites, prefabs and other assets are looks and not compared. A prefab variant that changes only its art is
    /// cosmetic; one that changes its cost, firepower, range or anything else on the unit is a special unit.
    /// </summary>
    public bool Cosmetic(Replacement r) {
        if (r == null || r.original == null || r.replacement == null) return false;
        lock (cosmetic) {
            bool same;
            if (!cosmetic.TryGetValue(r, out same)) cosmetic[r] = same = SameAttributes(r.original, r.replacement);
            return same;
        }
    }
    [NonSerialized] readonly Dictionary<Replacement, bool> cosmetic = new Dictionary<Replacement, bool>();

    /// <summary>Is every serialized attribute of <paramref name="a"/> and <paramref name="b"/> (numbers, flags, enums, texts, and lists of them) the same? Asset references are not compared.</summary>
    public static bool SameAttributes(Unit a, Unit b) {
        if (a == null || b == null) return false;
        return Differences(a, b).Count == 0;
    }

    /// <summary>The names of the serialized attributes in which <paramref name="a"/> and <paramref name="b"/> differ (see SameAttributes).</summary>
    public static List<string> Differences(Unit a, Unit b) {
        List<string> diff = new List<string>();
        for (Type t = typeof(Unit); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            foreach (System.Reflection.FieldInfo f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)) {
                if (f.IsNotSerialized || f.IsInitOnly) continue;
                if (!f.IsPublic && f.GetCustomAttributes(typeof(SerializeField), false).Length == 0) continue;
                if (!Comparable(f.FieldType)) continue;
                if (f.GetCustomAttributes(typeof(LooksOnlyAttribute), false).Length > 0) continue;
                if (!SameValue(f.GetValue(a), f.GetValue(b))) diff.Add(f.Name);
            }
        return diff;
    }

    /// <summary>A plain value (number, flag, enum, text, colour, vector) or a list or array of them: an attribute, not a reference to an asset.</summary>
    static bool Comparable(Type t) {
        if (t.IsArray) return Comparable(t.GetElementType());
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return Comparable(t.GetGenericArguments()[0]);
        if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return false;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || (t.IsValueType && t.Namespace == "UnityEngine");
    }

    static bool SameValue(object x, object y) {
        if (x == null || y == null) {
            System.Collections.IList lx = x as System.Collections.IList, ly = y as System.Collections.IList;
            return (x == null && y == null) || (lx != null && lx.Count == 0 && y == null) || (ly != null && ly.Count == 0 && x == null)
                || (x is string && (string)x == "" && y == null) || (y is string && (string)y == "" && x == null);
        }
        System.Collections.IList a = x as System.Collections.IList, b = y as System.Collections.IList;
        if (a != null && b != null) {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!SameValue(a[i], b[i])) return false;
            return true;
        }
        return x.Equals(y);
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
        if (f != null && SpecialUnits && f.extras != null && baseList != null && baseList.Count > 0)
            foreach (Extra e in f.extras) if (e != null && e.unit != null && e.builtAt != null && e.builtAt.Contains(type) && !list.Exists(x => Same(x, e.unit))) list.Add(e.unit);
        // Cheapest first, a faction's own units among the rest by their cost; equal costs keep the building's order.
        return list.Select((u, i) => new { u, i }).OrderBy(x => x.u.cost).ThenBy(x => x.i).Select(x => x.u).ToList();
    }

    /// <summary>Every unit this faction can field that is not a base unit (its versions and extras): what a save may name.</summary>
    public IEnumerable<Unit> OwnUnits() {
        if (replacements != null) foreach (Replacement r in replacements) if (r != null && r.replacement != null) yield return r.replacement;
        if (extras != null) foreach (Extra e in extras) if (e != null && e.unit != null) yield return e.unit;
    }
}

/// <summary>
/// A unit field that is about how the unit is shown (a name, a description, an animation speed), not how it plays:
/// a faction's version that differs from the base unit only in such fields is still cosmetic (Faction.Cosmetic).
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class LooksOnlyAttribute : Attribute { }
