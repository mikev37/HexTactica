using UnityEngine;

/// <summary>
/// The animator of a unit whose strike flies (a missile): its launch, rising off the top of the screen, and, as a copy
/// of it, its arrival, falling from above the screen onto the tile its strike lands on. Motion is in code, so it plays
/// with no clips at all; an Animator on the unit with a "Launch" or "Land" trigger is told too (Unit.Expend,
/// Unit.BeginArrival), for art to play on top. The timing is the unit's launchSeconds and landSeconds; the rules never
/// wait for any of this (Strikes: the strike has already landed or left when this plays).
/// </summary>
public class StrikeFlight : MonoBehaviour {

    [Tooltip("How far above the top of the screen it goes (or comes from), in world units.")]
    public float offScreen = 6f;
    [Tooltip("Shape of the climb and the fall: 1 even speed, 2 or more starts slow and speeds up (a rocket lifting off, a warhead falling).")]
    [Range(1f, 4f)] public float acceleration = 2.5f;
    [Tooltip("Shown under it while it flies (exhaust, a trail). Empty: nothing.")]
    public GameObject exhaust;
    [Tooltip("Seconds it shakes on the ground before it climbs (ignition), part of the launch time.")]
    public float ignition = 0.25f;

    Vector3 from, to;
    float start, seconds;
    bool flying, rising;
    GameObject trail;

    /// <summary>Climb from where it stands to above the top of the screen over <paramref name="duration"/> seconds.</summary>
    public void Launch(float duration) {
        from = transform.position;
        to = new Vector3(from.x, TopOfScreen(from) + offScreen, from.z);
        Begin(duration, true);
    }

    /// <summary>Start above the top of the screen over <paramref name="ground"/> and fall onto it over <paramref name="duration"/> seconds.</summary>
    public void Arrive(Vector3 ground, float duration) {
        to = ground;
        from = new Vector3(ground.x, TopOfScreen(ground) + offScreen, ground.z);
        transform.position = from;
        Begin(duration, false);
    }

    void Begin(float duration, bool up) {
        seconds = Mathf.Max(0.05f, duration);
        start = Time.time;
        rising = up;
        flying = true;
        if (exhaust != null && trail == null) trail = Instantiate(exhaust, transform.position, Quaternion.identity, transform);
        // Pointing the way it flies: nose up when rising, nose down when falling.
        transform.rotation = up ? Quaternion.identity : Quaternion.Euler(0f, 0f, 180f);
    }

    static float TopOfScreen(Vector3 at) {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic) return at.y + 40f;
        return cam.transform.position.y + cam.orthographicSize;
    }

    void Update() {
        if (!flying) return;
        float t = Mathf.Clamp01((Time.time - start) / seconds);
        float lift = rising ? ignition / seconds : 0f;
        if (rising && t < lift) {
            // Ignition: a shake where it stands.
            float s = 0.06f * Mathf.Sin(Time.time * 90f);
            transform.position = from + new Vector3(s, 0f, 0f);
            return;
        }
        float k = rising ? (lift >= 1f ? 1f : (t - lift) / (1f - lift)) : t;
        float e = Mathf.Pow(k, acceleration);
        transform.position = Vector3.Lerp(from, to, e);
        if (t >= 1f) flying = false;
    }
}
