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
    static void Hook() { GameScene.WhenLoaded(Install); }

    static void Install() {
        Camera c = Camera.main;
        if (c != null && c.orthographic && c.GetComponent<CameraRig>() == null) c.gameObject.AddComponent<CameraRig>();
    }

    void Awake() {
        cam = GetComponent<Camera>();
        // A scenario's camera actions (ScenarioGame): Camera Focus and Camera Frame Board.
        ScenarioGame.CameraFocusHook = FocusOn;
        ScenarioGame.CameraFrameHook = Frame;
    }

    void OnDestroy() {
        if (ScenarioGame.CameraFocusHook == (System.Action<Vector3, float, float>)FocusOn) ScenarioGame.CameraFocusHook = null;
        if (ScenarioGame.CameraFrameHook == (System.Action)Frame) ScenarioGame.CameraFrameHook = null;
    }

    // A glide to a point (FocusOn): from, to, and when.
    bool focusing;
    Vector3 focusFrom, focusTo;
    float sizeFrom, sizeTo, focusStart, focusSeconds;

    /// <summary>
    /// Glide the view to <paramref name="world"/> over <paramref name="seconds"/> (0: at once), zooming to
    /// <paramref name="size"/> (orthographic size; 0 keeps the zoom). A scenario's Camera Focus action.
    /// </summary>
    public void FocusOn(Vector3 world, float size, float seconds) {
        if (cam == null) return;
        framed = true;   // (the first frame's framing of the board does not undo it)
        focusFrom = transform.position;
        focusTo = new Vector3(world.x, world.y, transform.position.z);
        sizeFrom = cam.orthographicSize;
        sizeTo = size > 0f ? Mathf.Clamp(size, minSize, maxSize) : sizeFrom;
        focusStart = Time.unscaledTime;
        focusSeconds = Mathf.Max(0f, seconds);
        focusing = true;
        if (focusSeconds <= 0f) Glide();
    }

    void Glide() {
        float t = focusSeconds <= 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - focusStart) / focusSeconds);
        float e = t * t * (3f - 2f * t);
        transform.position = Vector3.Lerp(focusFrom, focusTo, e);
        cam.orthographicSize = Mathf.Lerp(sizeFrom, sizeTo, e);
        if (t >= 1f) focusing = false;
    }

    /// <summary>The first frame after the window got focus back: its input is not ours (see LateUpdate).</summary>
    int ignoreInputUntil;

    void OnApplicationFocus(bool focus) { if (focus) ignoreInputUntil = Time.frameCount + 1; }

    /// <summary>
    /// Input meant for the game: the window has focus and the cursor is over it. Wheel turns while
    /// another window had focus were kept by Unity and handed over all at once on refocus, so the
    /// zoom jumped; they, and the frame focus comes back on, are dropped.
    /// </summary>
    bool OurInput() {
        if (!Application.isFocused || Time.frameCount <= ignoreInputUntil) return false;
        Vector3 m = Input.mousePosition;
        return m.x >= 0f && m.y >= 0f && m.x <= Screen.width && m.y <= Screen.height;
    }

    void LateUpdate() {
        if (cam == null) return;
        if (!framed && HexGenerator.hg != null && HexGenerator.hg.tiles != null && HexGenerator.hg.tiles.Count > 0) {
            Frame();
            framed = true;
        }
        if (focusing) { Glide(); return; }   // a scenario moves the camera: the player's input waits until it is there
        if (!Application.isFocused || Time.frameCount <= ignoreInputUntil) return;   // not ours (OurInput)
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

        // The wheel over a panel scrolls the panel (a list), not the map.
        bool overUI = TransportMenu.PointerOverUI != null && TransportMenu.PointerOverUI();
        float wheel = OurInput() && !overUI ? Input.mouseScrollDelta.y : 0f;
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

    /// <summary>Centre the view on <paramref name="world"/>, keeping the zoom.</summary>
    public void CenterOn(Vector3 world) {
        Vector3 p = transform.position;
        transform.position = new Vector3(world.x, world.y, p.z);
    }

    /// <summary>Centre on the board and zoom so all of it is visible.</summary>
    public void Frame() {
        focusing = false;
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
