using UnityEngine;

/// <summary>
/// Pan and zoom for the main camera, so boards bigger than the screen are usable.
///
/// Installs itself on Camera.main when a scene loads (no scene edit needed), and frames the
/// whole board on the first frame. Controls:
///   arrow keys, or drag with the right or middle mouse button — pan
///   mouse wheel, or +/- — zoom
///   Home — frame the whole board again
/// WASD is left alone: the level editor uses S and L.
/// </summary>
public class CameraRig : MonoBehaviour {

    public float panSpeed = 1.2f;     // screen heights per second at the current zoom
    public float zoomStep = 0.12f;    // fraction of the view per wheel notch
    public float minSize = 8f;

    Camera cam;
    float maxSize = 200f;
    bool framed;
    Vector3 dragOrigin;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() {
        Camera c = Camera.main;
        if (c != null && c.orthographic && c.GetComponent<CameraRig>() == null) c.gameObject.AddComponent<CameraRig>();
    }

    void Awake() { cam = GetComponent<Camera>(); }

    void LateUpdate() {
        if (cam == null) return;
        if (!framed && HexGenerator.hg != null && HexGenerator.hg.tiles != null && HexGenerator.hg.tiles.Count > 0) {
            Frame();
            framed = true;
        }
        if (Input.GetKeyDown(KeyCode.Home)) Frame();

        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.LeftArrow)) move.x -= 1;
        if (Input.GetKey(KeyCode.RightArrow)) move.x += 1;
        if (Input.GetKey(KeyCode.DownArrow)) move.y -= 1;
        if (Input.GetKey(KeyCode.UpArrow)) move.y += 1;
        transform.position += move * cam.orthographicSize * 2f * panSpeed * Time.unscaledDeltaTime;

        if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) dragOrigin = cam.ScreenToWorldPoint(Input.mousePosition);
        if (Input.GetMouseButton(1) || Input.GetMouseButton(2)) {
            Vector3 now = cam.ScreenToWorldPoint(Input.mousePosition);
            Vector3 d = dragOrigin - now;
            d.z = 0;
            transform.position += d;
        }

        float wheel = Input.mouseScrollDelta.y;
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) wheel += 1;
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) wheel -= 1;
        if (wheel != 0f) {
            // Zoom about the cursor, so the point under it stays put.
            Vector3 before = cam.ScreenToWorldPoint(Input.mousePosition);
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize * (1f - zoomStep * wheel), minSize, maxSize);
            Vector3 after = cam.ScreenToWorldPoint(Input.mousePosition);
            Vector3 d = before - after;
            d.z = 0;
            transform.position += d;
        }
    }

    /// <summary>Centre on the board and zoom so all of it is visible.</summary>
    public void Frame() {
        if (HexGenerator.hg == null || HexGenerator.hg.tiles == null || HexGenerator.hg.tiles.Count == 0) return;
        Bounds b = new Bounds(HexGenerator.hg.tiles[0].transform.position, Vector3.zero);
        foreach (HexTile t in HexGenerator.hg.tiles) if (t != null) b.Encapsulate(t.transform.position);
        b.Expand(8f);
        Vector3 p = transform.position;
        transform.position = new Vector3(b.center.x, b.center.y, p.z);
        float aspect = cam.aspect > 0f ? cam.aspect : 16f / 9f;
        float size = Mathf.Max(b.extents.y, b.extents.x / aspect);
        maxSize = Mathf.Max(size * 2f, minSize * 2f);
        cam.orthographicSize = Mathf.Clamp(size, minSize, maxSize);
    }
}
