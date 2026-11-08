using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The production panel (in Hud.prefab): the open building's build list (BuildingMenu.open). One entry per unit it can
/// build, made from <see cref="entryTemplate"/> (its first text the unit's name, its second the cost); one the owner
/// cannot afford is shown but not clickable. Close, Escape, or the turn passing dismisses it. Everything it looks like
/// is the prefab's: this only fills it in.
/// </summary>
public class ProductionPanel : MonoBehaviour {
    [Tooltip("The panel shown while a build list is open (hidden otherwise).")]
    public GameObject panel;
    [Tooltip("The building's name and its owner's money.")]
    public TMP_Text title;
    [Tooltip("Where the entries go, in order.")]
    public RectTransform entries;
    [Tooltip("One entry (kept hidden): a Button whose first text is the unit's name and second its cost.")]
    public Button entryTemplate;

    readonly List<Button> made = new List<Button>();
    Building drawnFor;
    int drawnMoney = int.MinValue;

    void Awake() {
        BuildingMenu.CoversPoint = mouse => panel != null && panel.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)panel.transform, mouse, null);
        if (entryTemplate != null) entryTemplate.gameObject.SetActive(false);
        if (panel != null) panel.SetActive(false);
    }

    void OnDestroy() { BuildingMenu.CoversPoint = null; }

    /// <summary>Close the list (the Close button calls it).</summary>
    public void Close() { BuildingMenu.open = null; }

    void Update() {
        Building b = BuildingMenu.open;
        if (b != null && Input.GetKeyDown(KeyCode.Escape)) b = BuildingMenu.open = null;
        if (b != null && (b.owner == null || GameController.gc == null || b.owner != GameController.gc.activePlayer || !BuildingMenu.IsHumanTurn()))
            b = BuildingMenu.open = null;
        bool show = b != null && b.buildable != null;
        if (panel != null && panel.activeSelf != show) panel.SetActive(show);
        if (!show) { drawnFor = null; return; }
        if (b != drawnFor || b.owner.money != drawnMoney) Fill(b);
    }

    void Fill(Building b) {
        drawnFor = b; drawnMoney = b.owner.money;
        if (title != null) title.text = Hud.BuildingName(b) + " — money " + b.owner.money;
        foreach (Button e in made) if (e != null) Destroy(e.gameObject);
        made.Clear();
        if (entryTemplate == null || entries == null) return;
        foreach (Unit u in b.Purchasable()) {
            if (u == null) continue;
            Button e = Instantiate(entryTemplate, entries);
            e.gameObject.SetActive(true);
            TMP_Text[] texts = e.GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length > 0) texts[0].text = GameController.DisplayName(u, b.owner);
            if (texts.Length > 1) texts[1].text = u.cost.ToString();
            e.interactable = b.owner.money >= u.cost;
            Unit unit = u;
            e.onClick.AddListener(() => Buy(unit));
            made.Add(e);
        }
    }

    void Buy(Unit u) {
        Building b = BuildingMenu.open;
        if (b != null && BuildingController.bc != null && BuildingController.bc.Produce(b, u) != null) BuildingMenu.open = null;
    }
}
