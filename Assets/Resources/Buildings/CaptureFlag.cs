using UnityEngine;

/// <summary>
/// A building's capture flag. Put it on the flag's sprite (a child of the building): it stands
/// on its pole between <see cref="bottom"/> and <see cref="top"/> (local height) — at the top while
/// held, coming down as someone takes it; on a neutral one, climbing with the capturer's progress
/// (Building.FlagHeightOf) — and takes the colour of whoever holds the building, or of whoever is
/// taking it while it is neutral — <see cref="neutral"/> (white: as drawn) when it is neither. Under fog of war it shows the
/// building as the viewer last saw it (FogOfWar.ShownOwner, FogOfWar.ShownCapture).
/// </summary>
public class CaptureFlag : MonoBehaviour {

    [Tooltip("Local height of the flag when nobody holds it and nobody is taking it (the foot of the pole).")]
    public float bottom = 0f;
    [Tooltip("Local height of the flag while held with nobody taking it (the top of the pole).")]
    public float top = 2.5f;
    [Tooltip("How fast it climbs or drops, in poles per second.")]
    public float speed = 0.8f;
    [Tooltip("Colour while nobody holds it or is taking it.")]
    public Color neutral = Color.white;

    Building building;
    SpriteRenderer render;
    float shown = -1f;

    void Start() {
        building = GetComponentInParent<Building>();
        render = GetComponent<SpriteRenderer>();
        Refresh(true);
    }

    void Update() { Refresh(false); }

    void Refresh(bool jump) {
        if (building == null) return;
        Player capturing; int points;
        FogOfWar.ShownCapture(building, out capturing, out points);
        float target = Building.FlagHeightOf(FogOfWar.ShownOwner(building), capturing, points, building.CaptureThreshold);
        shown = jump || shown < 0f ? target : Mathf.MoveTowards(shown, target, Time.deltaTime * speed);
        Vector3 p = transform.localPosition;
        p.y = bottom + (top - bottom) * shown;
        transform.localPosition = p;
        if (render != null) {
            Player who = FogOfWar.ShownOwner(building) ?? capturing;
            Color c = who != null ? who.displayColor : neutral;
            render.color = new Color(c.r, c.g, c.b, render.color.a);
        }
    }
}
