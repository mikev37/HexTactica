using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The unit explorer (a panel of the main menu): every unit in Resources/Units in a list; the one picked shown large
/// and animated (UnitPreview), with its attributes and its description, and buttons that step through its animations.
/// Faction versions and extras are listed with their faction. The list rows and attribute rows are copies of hidden
/// templates the scene holds.
/// </summary>
public class UnitExplorer : MonoBehaviour {
    [Header("List")]
    public UnitListRow rowTemplate;
    public RectTransform listContent;
    [Tooltip("Units not listed, by prefab name (retired or internal ones).")]
    public List<string> hidden = new List<string> { "TempHQ" };
    public Color rowColor = new Color(1f, 1f, 1f, 0.08f);
    public Color pickedColor = new Color(0.36f, 0.62f, 0.95f, 0.55f);

    [Header("The unit picked")]
    public TMP_Text unitName;
    [Tooltip("Whose unit it is: a base unit, a faction's version of one (and whether it only looks different), a faction's own.")]
    public TMP_Text kindLine;
    public TMP_Text description;
    [Tooltip("Shown when the unit has no description yet.")]
    public string noDescription = "No description yet (Unit ▸ Description on the prefab).";
    public AttributeRow attributeTemplate;
    public RectTransform attributeContent;
    public UnitPreview preview;
    public TMP_Text animationName;
    [Tooltip("Shown for the animation when the unit has none.")]
    public string noAnimation = "No animations";

    readonly List<UnitListRow> rows = new List<UnitListRow>();
    readonly List<AttributeRow> attributes = new List<AttributeRow>();
    Unit picked;

    void Awake() {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
        if (attributeTemplate != null) attributeTemplate.gameObject.SetActive(false);
    }

    void OnEnable() {
        if (rows.Count == 0) Fill();
        if (picked == null && rows.Count > 0) Pick(rows[0].Unit);
        else if (picked != null) Pick(picked);
    }

    void OnDisable() { if (preview != null) preview.Clear(); }

    void Fill() {
        if (rowTemplate == null || listContent == null) return;
        foreach (Unit u in GameAssets.AllUnits.Where(x => x != null && !hidden.Contains(x.name)).OrderBy(x => FactionOf(x) != null ? 1 : 0).ThenBy(x => x.name)) {
            UnitListRow row = Instantiate(rowTemplate, listContent);
            row.gameObject.SetActive(true);
            row.name = u.name;
            Faction f = FactionOf(u);
            row.Show(u, f != null ? f.name : "", rowColor);
            Unit unit = u;
            if (row.button != null) row.button.onClick.AddListener(() => Pick(unit));
            rows.Add(row);
        }
    }

    /// <summary>The faction that fields this unit as its own (a version or an extra), or null for a base unit.</summary>
    static Faction FactionOf(Unit u) {
        foreach (Faction f in GameAssets.AllFactions) foreach (Unit own in f.OwnUnits()) if (own == u) return f;
        return null;
    }

    public void Pick(Unit u) {
        picked = u;
        foreach (UnitListRow r in rows) if (r != null) r.Highlight(r.Unit == u ? pickedColor : rowColor);
        if (u == null) return;
        if (unitName != null) unitName.text = u.name;
        if (kindLine != null) kindLine.text = KindLine(u);
        if (description != null) description.text = string.IsNullOrEmpty(u.description) ? noDescription : u.description;
        ShowAttributes(u);
        if (preview != null) preview.Show(u);
        ShowAnimationName();
    }

    string KindLine(Unit u) {
        foreach (Faction f in GameAssets.AllFactions) {
            if (f.replacements != null) foreach (Faction.Replacement r in f.replacements)
                if (r != null && r.replacement == u && r.original != null) {
                    if (f.Cosmetic(r)) return f.name + "'s look for the " + r.original.name + " (plays the same)";
                    List<string> diff = Faction.Differences(r.original, u).Take(6).ToList();
                    return f.name + "'s version of the " + r.original.name + ": a special unit (differs in " + string.Join(", ", diff.ToArray()) + ")";
                }
            if (f.extras != null) foreach (Faction.Extra e in f.extras)
                if (e != null && e.unit == u) return f.name + " only: a special unit" + (e.builtAt != null && e.builtAt.Count > 0 ? ", built at " + string.Join(", ", e.builtAt.Select(b => b.ToString()).ToArray()) : "");
        }
        return "A base unit: every side can field it" + RemovedBy(u);
    }

    static string RemovedBy(Unit u) {
        List<string> lacking = new List<string>();
        foreach (Faction f in GameAssets.AllFactions) if (f.removed != null && f.removed.Contains(u)) lacking.Add(f.name);
        return lacking.Count > 0 ? " (but not " + string.Join(", ", lacking.ToArray()) + ")" : ".";
    }

    void ShowAttributes(Unit u) {
        foreach (AttributeRow r in attributes) if (r != null) Destroy(r.gameObject);
        attributes.Clear();
        if (attributeTemplate == null || attributeContent == null) return;
        foreach (KeyValuePair<string, string> a in Attributes(u)) {
            AttributeRow row = Instantiate(attributeTemplate, attributeContent);
            row.gameObject.SetActive(true);
            row.Show(a.Key, a.Value);
            attributes.Add(row);
        }
    }

    /// <summary>What the unit is, in the terms the game uses: cost, strength, combat, movement, vision, supplies, and its special rules.</summary>
    static List<KeyValuePair<string, string>> Attributes(Unit u) {
        List<KeyValuePair<string, string>> a = new List<KeyValuePair<string, string>>();
        System.Action<string, object> add = (k, v) => a.Add(new KeyValuePair<string, string>(k, v.ToString()));
        add("Cost", u.cost.ToString("N0"));
        add("Strength", u.Strength);
        add("Firepower", u.Firepower);
        add("Targeting", u.Targeting);
        add("Evasion", u.Evasion);
        add("Armor", u.Armor);
        add("Piercing", u.Piercing);
        add("Concealment", u.Concealment);
        if (u.Vulnerability != 0) add("Vulnerability", u.Vulnerability);
        add("Attack range", u.MinRange > 1 ? u.MinRange + "–" + u.AttackRange : u.AttackRange.ToString());
        add("Moves and attacks", u.moveAndAttack ? "yes" : "no (move or attack)");
        if (!u.retaliates) add("Returns fire", "no");
        if (u.ignoresCover) add("Ignores cover", "yes");
        add("Locomotion", u.locomotion);
        add("Move range", u.moveRange);
        if (u.terrainScramble != 0) add("Terrain penalty", u.terrainScramble);
        if (u.amphibious) add("Amphibious", "yes");
        add("Vision", u.visionRange);
        if (u.stealthRange > 0) add("Stealth", "unseen from " + u.stealthRange + " tiles");
        if (u.ammoMax > 0) add("Ammunition", u.ammoMax + (u.ammoPerShot != 1 ? " (" + u.ammoPerShot + " a shot)" : ""));
        if (u.fuelMax > 0) add("Fuel", u.fuelMax + (u.minFuelPerTurn > 0 ? " (" + u.minFuelPerTurn + " a turn)" : ""));
        if (u.suppliesMax > 0) add("Supplies", u.suppliesMax);
        if (u.airTanker) add("Air tanker", "refuels aircraft beside it");
        return a;
    }

    public void NextAnimation() { if (preview != null) preview.Step(1); ShowAnimationName(); }
    public void PreviousAnimation() { if (preview != null) preview.Step(-1); ShowAnimationName(); }

    void ShowAnimationName() {
        if (animationName == null || preview == null) return;
        animationName.text = preview.AnimationCount == 0 ? noAnimation : preview.AnimationName + "  (" + (preview.AnimationIndex + 1) + "/" + preview.AnimationCount + ")";
    }
}
