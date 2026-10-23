using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unloading, for the player. While one of our transports with something aboard is selected —
/// or clicked after its turn is over: units can be put down then too — its panel shows a button
/// per unit aboard (TransportPanel, Assets/Resources/TransportPanel.prefab).
///
/// Clicking a unit's button marks the tiles it can be put down on (UNLOAD_CONFIRM): the six around
/// the tile picked for the transport's move (MOVE_CONFIRM), if one is picked, else around the
/// transport. Clicking one of those (HexTile.OnMouseDown → <see cref="UnloadOnto"/>) puts the unit
/// down there — after moving the transport to the picked tile and ending its turn, if it was
/// around that tile. Getting off costs the unit nothing (Transporting.Unload).
/// </summary>
public class TransportMenu : MonoBehaviour {

    /// <summary>The transport whose panel is showing (null: none).</summary>
    public static Unit open;
    /// <summary>The unit picked to unload, its transport, and the tile the transport will stand on to put it down.</summary>
    public static Unit pendingCargo, pendingTransport;
    public static HexTile pendingAnchor;

    /// <summary>Set by TransportPanel (the UI side): show the panel for a transport (the unit picked, if any), hide it, and whether the pointer is over the UI.</summary>
    public static System.Action<Unit, Unit> PanelShow;
    public static System.Action PanelHide;
    public static System.Func<bool> PointerOverUI;

    public static void Open(Unit transport) {
        if (transport == null || transport.transported.Count == 0) { Close(); return; }
        if (open != transport) { pendingCargo = null; pendingTransport = null; pendingAnchor = null; }
        open = transport;
    }

    public static void Close() { open = null; pendingCargo = null; pendingTransport = null; pendingAnchor = null; }

    /// <summary>Is the pointer over the panel (a click there is the panel's, not the board's)?</summary>
    public static bool Covers(Vector3 mouse) { return open != null && PointerOverUI != null && PointerOverUI(); }

    /// <summary>
    /// The unit's button was clicked: mark where it can be put down — around the tile picked for the
    /// transport's move if there is one (the transport moves there first, ending its turn), else
    /// around the transport.
    /// </summary>
    public static void PickCargo(Unit cargo) {
        Unit transport = open;
        if (transport == null || cargo == null || cargo.trasnporter != transport) return;
        HexTile target = null;
        if (transport.active && GameController.gc.activeUnit == transport)
            HexGenerator.forAllTiles(t => { if (t.state == UIState.MOVE_CONFIRM) target = t; return 0; });
        HexTile anchor = target ?? transport.tile;
        HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
        if (target != null) target.changeState(UIState.MOVE_CONFIRM);   // where the transport will go
        foreach (HexTile n in Transporting.UnloadOptions(transport, cargo, anchor)) n.changeState(UIState.UNLOAD_CONFIRM);
        pendingCargo = cargo;
        pendingTransport = transport;
        pendingAnchor = anchor;
    }

    /// <summary>
    /// An unload tile was clicked: put the picked unit down on <paramref name="onto"/>, moving the
    /// transport to its picked tile first (and ending its turn) if that is where it unloads from.
    /// The panel stays open on the transport while it has anything left aboard.
    /// </summary>
    public static void UnloadOnto(HexTile onto) {
        Unit cargo = pendingCargo, transport = pendingTransport;
        HexTile anchor = pendingAnchor;
        pendingCargo = null; pendingTransport = null; pendingAnchor = null;
        if (cargo == null || transport == null || transport.tile == null || onto == null) return;
        if (anchor != null && anchor != transport.tile && transport.active) {
            // Move first: the path is read from the tiles' parent chain, which the pick cleared.
            HexGenerator.forAllTiles(t => { t.G = 0; t.parent = null; return 0; });
            transport.tile.CheckMovementRange(transport);
            transport.CompleteMove(anchor);
            transport.FinishActive();
        }
        Transporting.Unload(transport, cargo, onto);
        HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
        GameController.gc.activeUnit = null;
        if (transport.transported.Count > 0) Open(transport); else Close();
    }

    Unit shownFor, shownPicked;
    int shownCount = -1;

    void Update() {
        if (open != null && Input.GetKeyDown(KeyCode.Escape)) Close();
        if (open != null && (open.tile == null || GameController.gc == null || open.owner != GameController.gc.activePlayer || open.transported.Count == 0)) Close();
        // The panel follows: shown for the transport while it is open, redrawn when what is aboard
        // or the unit picked changes, hidden otherwise.
        if (open == null) {
            if (shownFor != null && PanelHide != null) PanelHide();
            shownFor = null; shownCount = -1; shownPicked = null;
            return;
        }
        if (open != shownFor || open.transported.Count != shownCount || pendingCargo != shownPicked) {
            shownFor = open; shownCount = open.transported.Count; shownPicked = pendingCargo;
            if (PanelShow != null) PanelShow(open, pendingCargo);
        }
    }
}
