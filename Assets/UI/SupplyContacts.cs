using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Known unknowns on the board: a contact marker on each tile where the screen's player's supply is truly not received
/// because of an enemy it does not see (SupplyOverlay.UnseenContacts): a corridor cut, or a transit stopped. The supply
/// shown spreading never gives an unseen enemy away; a unit that misses supply is shown unfed, and the marker says
/// why. It says only that something is on or beside that tile, never what.
///
/// The marker is its prefab, Assets/Resources/SupplyContact.prefab (any look: sprites, text, animation), stood at the
/// tile's unit placement. Worked out again whenever supply is published or the screen's side changes. Installed in
/// every game scene; nothing under no fog.
/// </summary>
public class SupplyContacts : MonoBehaviour {

    /// <summary>The marker's prefab, in Resources.</summary>
    public const string PrefabName = "SupplyContact";

    readonly List<GameObject> shown = new List<GameObject>();
    GameObject prefab;
    bool looked;
    int published = -1;
    Player viewer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook() { GameScene.WhenLoaded(Install); }

    static void Install() {
        if (GameController.gc != null && GameController.gc.headless) return;
        if (FindAnyObjectByType<SupplyContacts>() != null) return;
        new GameObject("Supply Contacts").AddComponent<SupplyContacts>();
    }

    void LateUpdate() {
        Player v = FogOfWar.On ? FogOfWar.Viewer : null;
        if (v == viewer && SupplyNetwork.Published == published) return;
        viewer = v;
        published = SupplyNetwork.Published;
        Show(v != null ? SupplyOverlay.UnseenContacts(v) : new List<HexTile>());
    }

    void Show(List<HexTile> tiles) {
        if (!looked) {
            looked = true;
            prefab = Resources.Load<GameObject>(PrefabName);
            if (prefab == null) Debug.LogWarning("Supply contacts: no prefab Resources/" + PrefabName + ".prefab.");
        }
        int i = 0;
        if (prefab != null)
            foreach (HexTile t in tiles) {
                if (t == null) continue;
                if (i >= shown.Count) shown.Add(Instantiate(prefab, transform));
                GameObject g = shown[i++];
                g.name = PrefabName + " (" + t.x + "," + t.y + ")";
                g.transform.position = t.unitPlacement != null ? t.unitPlacement.position : t.transform.position;
                g.SetActive(true);
            }
        for (; i < shown.Count; i++) if (shown[i] != null) shown[i].SetActive(false);
    }
}
