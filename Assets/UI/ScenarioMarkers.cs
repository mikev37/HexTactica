using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The markers on the board (ScenarioGame.ShowMarkerHook / HideMarkerHook): one object per marker id, made from its
/// prefab and stood at its tile. Installed in every game scene.
/// </summary>
public class ScenarioMarkers : MonoBehaviour {

    readonly Dictionary<string, ScenarioMarker> shown = new Dictionary<string, ScenarioMarker>();
    readonly Dictionary<string, string> kinds = new Dictionary<string, string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook() { GameScene.WhenLoaded(Install); }

    static void Install() {
        if (GameController.gc != null && GameController.gc.headless) return;
        if (FindAnyObjectByType<ScenarioMarkers>() != null) return;
        new GameObject("Scenario Markers").AddComponent<ScenarioMarkers>();
    }

    void OnEnable() {
        ScenarioGame.ShowMarkerHook = Show;
        ScenarioGame.HideMarkerHook = Hide;
    }

    void OnDestroy() {
        if (ScenarioGame.ShowMarkerHook == (System.Action<ShownMarker, Vector3>)Show) ScenarioGame.ShowMarkerHook = null;
        if (ScenarioGame.HideMarkerHook == (System.Action<string>)Hide) ScenarioGame.HideMarkerHook = null;
    }

    void Show(ShownMarker m, Vector3 at) {
        ScenarioMarker mk;
        string kind;
        if (shown.TryGetValue(m.Id, out mk) && mk != null && kinds.TryGetValue(m.Id, out kind) && kind == m.Marker) {
            mk.transform.position = at;
            mk.Show(m.Text);
            return;
        }
        Hide(m.Id);
        GameObject asset = ScenarioAssets.Load<GameObject>(m.Marker, ScenarioMarker.Folder);
        ScenarioMarker prefab = asset != null ? asset.GetComponent<ScenarioMarker>() : null;
        if (prefab == null) { Debug.LogWarning("Scenario: no marker prefab '" + m.Marker + "' in Resources/" + ScenarioMarker.Folder + " (it needs a ScenarioMarker on its root)."); return; }
        mk = Instantiate(prefab, at, Quaternion.identity, transform);
        mk.name = m.Marker + " (" + m.Id + ")";
        mk.Show(m.Text);
        shown[m.Id] = mk;
        kinds[m.Id] = m.Marker;
    }

    void Hide(string id) {
        if (string.IsNullOrEmpty(id)) {
            foreach (ScenarioMarker mk in shown.Values) if (mk != null) Destroy(mk.gameObject);
            shown.Clear(); kinds.Clear();
            return;
        }
        ScenarioMarker m;
        if (shown.TryGetValue(id, out m)) { if (m != null) Destroy(m.gameObject); shown.Remove(id); kinds.Remove(id); }
    }
}
