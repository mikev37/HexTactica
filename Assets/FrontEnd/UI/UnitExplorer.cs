using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The unit explorer (a panel of the main menu): every unit in Resources/Units in a list; the one picked shown large
/// and animated (UnitPreview), with its attributes — as dots against the most any unit has, as the HUD shows them —
/// its description, and buttons that step through its animations. A faction's version shows as the unit it stands in
/// for, its faction beside it. Rows are copies of the row prefabs.
/// </summary>
public class UnitExplorer : MonoBehaviour {
    [System.Serializable]
    public class AttributeIcon { public string attribute; public Sprite icon; }

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

    [Header("Attribute dots (as the HUD's)")]
    [Tooltip("An attribute whose largest value among all units is above this is shown as a number, not dots.")]
    public int dotLimit = 12;
    public Color dotOn = new Color(0.36f, 0.82f, 0.36f, 1f);
    public Color dotOff = new Color(1f, 1f, 1f, 0.22f);
    public Sprite dotFull, dotEmpty;
    [Tooltip("The icon shown beside each attribute, by its label.")]
    public List<AttributeIcon> icons = new List<AttributeIcon>();

    readonly List<UnitListRow> rows = new List<UnitListRow>();
    readonly List<AttributeRow> attributes = new List<AttributeRow>();
    readonly Dictionary<string, int> most = new Dictionary<string, int>();
    Unit picked;

    /// <summary>An attribute: a number (shown as dots or a number), or a text.</summary>
    struct Attr { public string name; public int v; public string text; }

    void Awake() {
        if (rowTemplate != null && rowTemplate.gameObject.scene.IsValid()) rowTemplate.gameObject.SetActive(false);
        if (attributeTemplate != null && attributeTemplate.gameObject.scene.IsValid()) attributeTemplate.gameObject.SetActive(false);
    }

    void OnEnable() {
        if (rows.Count == 0) Fill();
        if (picked == null && rows.Count > 0) Pick(rows[0].Unit);
        else if (picked != null) Pick(picked);
    }

    void OnDisable() { if (preview != null) preview.Clear(); }

    void Fill() {
        List<Unit> all = GameAssets.AllUnits.Where(x => x != null && !hidden.Contains(x.name)).ToList();
        // The most any unit has of each attribute: the dots' full row.
        foreach (Unit u in all) foreach (Attr a in Attributes(u)) if (a.text == null) { int m; most.TryGetValue(a.name, out m); most[a.name] = Mathf.Max(m, a.v); }
        if (rowTemplate == null || listContent == null) return;
        foreach (Unit u in all.OrderBy(x => UnitCatalog.FactionOf(x) != null ? 1 : 0).ThenBy(UnitCatalog.DisplayName).ThenBy(x => x.name)) {
            UnitListRow row = Instantiate(rowTemplate, listContent);
            row.gameObject.SetActive(true);
            row.name = u.name;
            Faction f = UnitCatalog.FactionOf(u);
            row.Show(u, UnitCatalog.DisplayName(u), f != null ? f.name : "", rowColor);
            Unit unit = u;
            if (row.button != null) row.button.onClick.AddListener(() => Pick(unit));
            rows.Add(row);
        }
    }

    public void Pick(Unit u) {
        picked = u;
        foreach (UnitListRow r in rows) if (r != null) r.Highlight(r.Unit == u ? pickedColor : rowColor);
        if (u == null) return;
        if (unitName != null) unitName.text = UnitCatalog.DisplayName(u);
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
                    return f.name + " only: its special version of the " + r.original.name + " (differs in " + string.Join(", ", diff.ToArray()) + ")";
                }
            if (f.extras != null) foreach (Faction.Extra e in f.extras)
                if (e != null && e.unit == u) return f.name + " only: a special unit" + (e.builtAt != null && e.builtAt.Count > 0 ? ", built at " + string.Join(", ", e.builtAt.Select(b => b.ToString()).ToArray()) : "");
        }
        List<string> lacking = new List<string>();
        foreach (Faction f in GameAssets.AllFactions) if (f.removed != null && f.removed.Contains(u)) lacking.Add(f.name);
        return "A base unit: every side can field it" + (lacking.Count > 0 ? " (but not " + string.Join(", ", lacking.ToArray()) + ")" : ".");
    }

    void ShowAttributes(Unit u) {
        foreach (AttributeRow r in attributes) if (r != null) Destroy(r.gameObject);
        attributes.Clear();
        if (attributeTemplate == null || attributeContent == null) return;
        foreach (Attr a in Attributes(u)) {
            AttributeRow row = Instantiate(attributeTemplate, attributeContent);
            row.gameObject.SetActive(true);
            Sprite pic = IconFor(a.name);
            int cap;
            if (a.text != null) row.ShowText(pic, a.name, a.text);
            else if (most.TryGetValue(a.name, out cap) && cap > 0 && cap <= dotLimit && a.v > 0) row.ShowDots(pic, a.name, a.v, cap, dotOn, dotOff, dotFull, dotEmpty);
            else row.ShowText(pic, a.name, a.v.ToString("N0"));
            attributes.Add(row);
        }
    }

    Sprite IconFor(string attribute) { AttributeIcon i = icons.Find(x => x.attribute == attribute); return i != null ? i.icon : null; }

    /// <summary>What the unit is, in the terms the game uses; an attribute it has none of (0, or a rule it lacks) is left out.</summary>
    static List<Attr> Attributes(Unit u) {
        List<Attr> a = new List<Attr>();
        System.Action<string, int> num = (k, v) => { if (v != 0) a.Add(new Attr { name = k, v = v }); };
        System.Action<string, string> text = (k, v) => a.Add(new Attr { name = k, text = v });
        text("Cost", u.cost.ToString("N0"));
        num("Strength", u.Strength);
        num("Firepower", u.Firepower);
        num("Targeting", u.Targeting);
        num("Evasion", u.Evasion);
        num("Armor", u.Armor);
        num("Piercing", u.Piercing);
        num("Concealment", u.Concealment);
        num("Vulnerability", u.Vulnerability);
        if (u.AttackRange > 0) text("Attack range", u.MinRange > 1 ? u.MinRange + "–" + u.AttackRange : u.AttackRange.ToString());
        num("Move range", u.moveRange);
        num("Terrain penalty", u.terrainScramble);
        num("Vision", u.visionRange);
        num("Stealth", u.stealthRange);
        num("Ammunition", u.ammoMax);
        num("Fuel", u.fuelMax);
        num("Supplies", u.suppliesMax);
        text("Locomotion", u.locomotion.ToString());
        if (u.AttackRange > 0) text("Moves and attacks", u.moveAndAttack ? "Yes" : "Move or attack");
        if (u.AttackRange > 0 && !u.retaliates) text("Returns fire", "No");
        if (u.ignoresCover) text("Ignores cover", "Yes");
        if (u.amphibious) text("Amphibious", "Yes");
        if (u.airTanker) text("Air tanker", "Refuels aircraft beside it");
        return a;
    }

    public void NextAnimation() { if (preview != null) preview.Step(1); ShowAnimationName(); }
    public void PreviousAnimation() { if (preview != null) preview.Step(-1); ShowAnimationName(); }

    void ShowAnimationName() {
        if (animationName == null || preview == null) return;
        animationName.text = preview.AnimationCount == 0 ? noAnimation : preview.AnimationName + "  (" + (preview.AnimationIndex + 1) + "/" + preview.AnimationCount + ")";
    }
}
