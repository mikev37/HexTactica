/// <summary>
/// The build list of the building clicked (HexTile.OnMouseDown opens it for an owned building with no unit on it,
/// on its owner's turn): which building it is for, and whether it covers the mouse (so a click on it is not also a
/// click on the board). What is shown, and how, is the ProductionPanel in the HUD prefab.
/// </summary>
public static class BuildingMenu {

    /// <summary>The building whose build list is open, if any.</summary>
    public static Building open;

    public static void Open(Building b) { open = b; }

    /// <summary>Set by the panel on screen: does it cover this screen point?</summary>
    public static System.Func<UnityEngine.Vector3, bool> CoversPoint;

    public static bool Covers(UnityEngine.Vector3 mouse) { return open != null && CoversPoint != null && CoversPoint(mouse); }

    /// <summary>The player whose turn it is plays by hand (no AI playing it, the baseline AIs included: FogOfWar.IsHuman).</summary>
    public static bool IsHumanTurn() {
        return GameController.gc != null && FogOfWar.IsHuman(GameController.gc.activePlayer);
    }
}
