using UnityEngine;
/// <summary>Drives HexTilePlayerSupplyDisplay.Pump once a frame (made on first need; made again if a scene change destroyed it).</summary>
public class SupplyBorderPump : MonoBehaviour {
    static SupplyBorderPump instance;

    public static void Ensure() {
        if (instance != null || !Application.isPlaying) return;
        GameObject go = new GameObject("SupplyBorderPump");
        instance = go.AddComponent<SupplyBorderPump>();
    }

    void LateUpdate() { HexTilePlayerSupplyDisplay.Pump(); }
}
