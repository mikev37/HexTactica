using UnityEngine;

/// <summary>
/// The build list of the building clicked (HexTile.OnMouseDown opens it for an owned building
/// with no unit on it, on its owner's turn). One button per unit it can build, with the cost;
/// a unit the owner cannot afford is shown but does nothing. Escape or Close dismisses it.
/// </summary>
public class BuildingMenu : MonoBehaviour {

    public static Building open;

    public static void Open(Building b) { open = b; }

    void Update() {
        if (open != null && Input.GetKeyDown(KeyCode.Escape)) open = null;
        if (open != null && (open.owner == null || GameController.gc == null || open.owner != GameController.gc.activePlayer)) open = null;
    }

    void OnGUI() {
        Building b = open;
        if (b == null || b.buildable == null) return;
        float w = 240f, row = 28f;
        float h = 60f + row * (b.buildable.Count + 1);
        Rect box = new Rect(Screen.width - w - 16f, 16f, w, h);
        GUI.Box(box, (string.IsNullOrEmpty(b.personal_name) ? b.type.ToString() : b.personal_name) + " — money " + b.owner.money);
        float y = box.y + 30f;
        foreach (Unit u in b.buildable) {
            if (u == null) continue;
            string label = u.name.Replace("(Clone)", "") + "   " + u.cost;
            GUI.enabled = b.owner.money >= u.cost;
            if (GUI.Button(new Rect(box.x + 10f, y, w - 20f, row - 4f), label)) {
                if (BuildingController.bc != null && BuildingController.bc.Produce(b, u) != null) open = null;
            }
            GUI.enabled = true;
            y += row;
        }
        if (GUI.Button(new Rect(box.x + 10f, y, w - 20f, row - 4f), "Close")) open = null;
    }
}
