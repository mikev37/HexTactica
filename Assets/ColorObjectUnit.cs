using UnityEngine;

/// <summary>
/// Colours this sprite in its owner's colour, keeping its own opacity: a unit's owner, or a
/// building's (put it on any sprite under a unit or a building). A building nobody holds takes
/// <see cref="neutral"/>; it recolours when the building changes hands.
/// </summary>
public class ColorObjectUnit : MonoBehaviour
{
    [Tooltip("Colour while nobody owns it (a neutral building).")]
    public Color neutral = new Color(0.6f, 0.6f, 0.6f, 1f);

    Unit unit;
    Building building;
    float alpha;
    SpriteRenderer render;

    void Start()
    {
        render = GetComponent<SpriteRenderer>();
        unit = GetComponentInParent<Unit>();
        if (unit == null) building = GetComponentInParent<Building>();
        if (render != null) alpha = render.color.a;
    }

    void Update()
    {
        if (render == null) return;
        Player owner = unit != null ? unit.owner : building != null ? building.owner : null;
        Color pc = owner != null ? owner.displayColor : neutral;
        render.color = new Color(pc.r, pc.g, pc.b, alpha);
    }
}
