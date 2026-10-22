using UnityEngine;

/// <summary>
/// The unload list of a transport (HexTile.OnMouseDown opens it): clicking a selected transport
/// that has not moved, or one that has moved and finished its turn without attacking, shows
/// every unit aboard. Clicking one highlights the tiles beside the transport it can step onto
/// (UNLOAD_CONFIRM); clicking one of those unloads it there (Transporting.Unload, which plays the
/// unload: out of the transport's slot, back to full size, walking onto the tile).
/// </summary>
public class TransportMenu : MonoBehaviour {

    public static Unit open;
    /// <summary>The unit picked to unload, and its transport, while its tiles are highlighted.</summary>
    public static Unit pendingCargo, pendingTransport;

    public static void Open(Unit transport) { open = transport; pendingCargo = null; pendingTransport = null; }

    /// <summary>Where the menu was last drawn (GUI coordinates), so a click on it is not also a click on the board.</summary>
    static Rect drawn;
    public static bool Covers(Vector3 mouse) { return open != null && drawn.Contains(new Vector2(mouse.x, Screen.height - mouse.y)); }

    void Update() {
        if (open != null && Input.GetKeyDown(KeyCode.Escape)) open = null;
        if (open != null && (open.tile == null || GameController.gc == null || open.owner != GameController.gc.activePlayer || open.transported.Count == 0)) open = null;
    }

    void OnGUI() {
        Unit t = open;
        if (t == null) return;
        float w = 220f, row = 28f;
        float h = 60f + row * (t.transported.Count + 1);
        Rect box = new Rect(16f, 16f, w, h);
        drawn = box;
        GUI.Box(box, t.name.Replace("(Clone)", "") + ": unload");
        float y = box.y + 30f;
        foreach (Unit c in t.transported.ToArray()) {
            if (c == null) continue;
            int options = Transporting.UnloadOptions(t, c).Count;
            GUI.enabled = options > 0;
            if (GUI.Button(new Rect(box.x + 10f, y, w - 20f, row - 4f), c.name.Replace("(Clone)", "") + "  s" + c.Strength + (options == 0 ? "  (no room)" : ""))) Pick(t, c);
            GUI.enabled = true;
            y += row;
        }
        if (GUI.Button(new Rect(box.x + 10f, y, w - 20f, row - 4f), "Close")) open = null;
    }

    static void Pick(Unit transport, Unit cargo) {
        HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
        foreach (HexTile n in Transporting.UnloadOptions(transport, cargo)) n.changeState(UIState.UNLOAD_CONFIRM);
        pendingCargo = cargo;
        pendingTransport = transport;
        open = null;
    }
}
