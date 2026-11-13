using System;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// One supply pip of a forward base's store: the building-side counterpart of a unit's strength pip (HealthDot).
/// Put it on each circle of a pip row under the building (as HealthCircles is built: a circle sprite per pip, named
/// "circle", "circle (1)", "circle (2)"... — the number is the pip's place, 0 first), with setSize for the grow/shrink
/// and counterRotate if the building turns. Pip i is filled while the store holds more than i (a turn of supply the
/// building can still give cut off from the HQ), faint while it could hold it (room to refill, suppliesMax), and hidden
/// beyond that, for a building without a store, or one the screen's player cannot see under fog of war (its own are
/// always shown).
/// </summary>
public class SupplyDot : MonoBehaviour {
    [Tooltip("The building whose store this pip shows. Empty: the building this pip is under.")]
    public Building building;
    [Tooltip("This pip's place in the row (0 first). Read from the object's name, as HealthDot does, when it ends in a number.")]
    public int iter;
    [Tooltip("A turn of store the building holds.")]
    public Color filledColor = new Color(0.25f, 0.9f, 0.3f, 1f);
    [Tooltip("Room for a turn of store it does not hold (set its alpha to 0 to show only what it holds).")]
    public Color emptyColor = new Color(0.25f, 0.9f, 0.3f, 0.25f);
    [Tooltip("How quickly the pip fades between states.")]
    public float transition_speed = 2f;
    [Tooltip("Size of a filled pip and of one that is not (setSize.sizeF), as HealthDot uses.")]
    public float filledSize = 0.5f, emptySize = 0.5f, hiddenSize = 1f;

    setSize size;
    SpriteRenderer render;
    Color actual;

    public enum State { Hidden, Empty, Filled }

    void Start() {
        if (building == null) building = GetComponentInParent<Building>();
        size = GetComponent<setSize>();
        render = GetComponent<SpriteRenderer>();
        try {
            string n = Regex.Match(gameObject.name, @"\d+").Value;
            if (n.Length > 0) iter = Int32.Parse(n);
        } catch (FormatException) { }
        if (render != null) { actual = render.color; actual.a = 0f; render.color = actual; }
    }

    void Update() {
        State s = StateOf(building, iter);
        Color target = s == State.Filled ? filledColor : s == State.Empty ? emptyColor : new Color(filledColor.r, filledColor.g, filledColor.b, 0f);
        if (size != null) size.sizeF = s == State.Filled ? filledSize : s == State.Empty ? emptySize : hiddenSize;
        if (render == null) return;
        actual = Color.Lerp(actual, target, Time.deltaTime * transition_speed);
        render.color = actual;
    }

    /// <summary>What pip <paramref name="i"/> of <paramref name="b"/>'s row shows now.</summary>
    public static State StateOf(Building b, int i) {
        if (!ShownTo(b) || i < 0 || i >= b.suppliesMax) return State.Hidden;
        return i < Filled(b) ? State.Filled : State.Empty;
    }

    /// <summary>Does <paramref name="b"/> keep a store worth showing (a supply building other than the HQ)?</summary>
    public static bool HasStore(Building b) { return b != null && b.producesSupply && !b.IsHQ && b.suppliesMax > 0; }

    /// <summary>How many pips are filled for <paramref name="b"/>: the turns of store it has left.</summary>
    public static int Filled(Building b) { return HasStore(b) ? Mathf.Clamp(b.supplies, 0, b.suppliesMax) : 0; }

    /// <summary>Are <paramref name="b"/>'s pips shown to the screen's player: its own building, or one in its sight (fog of war)?</summary>
    public static bool ShownTo(Building b) {
        if (!HasStore(b) || !b.Functioning) return false;
        if (!FogOfWar.On) return true;
        Player viewer = FogOfWar.Viewer;
        if (viewer == null || (b.owner != null && Player.Friendly(b.owner, viewer))) return true;
        if (b.tile != null && FogOfWar.TileVisible(viewer, b.tile)) return true;
        return false;
    }
}
