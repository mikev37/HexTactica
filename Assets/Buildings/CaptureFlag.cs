using UnityEngine;

/// <summary>
/// A building's capture flag. Put it on the flag's sprite (a child of the building): it climbs
/// its pole between <see cref="bottom"/> and <see cref="top"/> (local height) as capture points
/// build up, and takes the colour of whoever holds the building, or of whoever is taking it while
/// it is neutral — grey (<see cref="neutral"/>) when it is neither. Under fog of war it shows the
/// building as the viewer last saw it (FogOfWar.ShownOwner, FogOfWar.ShownCapture).
/// </summary>
public class CaptureFlag : MonoBehaviour {

    [Tooltip("Local height of the flag at no points (the foot of the pole).")]
    public float bottom = 0f;
    [Tooltip("Local height of the flag at full points (the top of the pole).")]
    public float top = 2.5f;
    [Tooltip("How fast it climbs or drops, in poles per second.")]
    public float speed = 0.8f;
    [Tooltip("Colour while nobody holds it or is taking it.")]
    public Color neutral = new Color(0.6f, 0.6f, 0.6f, 1f);

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
        float target = Mathf.Clamp01(points / (float)Mathf.Max(1, building.CaptureThreshold));
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
