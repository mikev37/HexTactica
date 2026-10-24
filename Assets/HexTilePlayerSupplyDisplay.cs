using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HexTilePlayerSupplyDisplay : MonoBehaviour
{
    internal HexTile myTile;
    internal Player myPlayer;
    public SpriteRenderer up, upleft, upright, down, downleft, downright;
    // Start is called before the first frame update
    void Start()
    {
        if (!all.Contains(this)) all.Add(this);
        up.color = new Color(myPlayer.displayColor.r, myPlayer.displayColor.g, myPlayer.displayColor.b, .7f) ;
        down.color = new Color(myPlayer.displayColor.r, myPlayer.displayColor.g, myPlayer.displayColor.b, .7f);
        upright.color = new Color(myPlayer.displayColor.r, myPlayer.displayColor.g, myPlayer.displayColor.b, .7f);
        upleft.color = new Color(myPlayer.displayColor.r, myPlayer.displayColor.g, myPlayer.displayColor.b, .7f);
        downleft.color = new Color(myPlayer.displayColor.r, myPlayer.displayColor.g, myPlayer.displayColor.b, .7f);
        downright.color = new Color(myPlayer.displayColor.r, myPlayer.displayColor.g, myPlayer.displayColor.b, .7f);
        up.sortingOrder = 5 + GameController.gc.players.IndexOf(myPlayer);
        down.sortingOrder = 5 + GameController.gc.players.IndexOf(myPlayer);
        upright.sortingOrder = 5 + GameController.gc.players.IndexOf(myPlayer);
        upleft.sortingOrder = 5 + GameController.gc.players.IndexOf(myPlayer);
        downleft.sortingOrder = 5 + GameController.gc.players.IndexOf(myPlayer);
        downright.sortingOrder = 5 + GameController.gc.players.IndexOf(myPlayer);
    }

    // Update is called once per frame
    // The border is redrawn only when a supply field has been published (SupplyNetwork wakes every
    // display then), and an edge is switched only when it changes. In between the component is
    // disabled, so Unity does not call Update at all: one call per tile per player per frame, even
    // returning at once, was a cost of its own on a large board.
    static readonly List<HexTilePlayerSupplyDisplay> all = new List<HexTilePlayerSupplyDisplay>();

    /// <summary>Redraw every supply border on the next frame (called when a supply field is published).</summary>
    public static void WakeAll() {
        for (int i = all.Count - 1; i >= 0; i--) {
            if (all[i] == null) { all.RemoveAt(i); continue; }
            all[i].enabled = true;
        }
    }

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDestroy() { all.Remove(this); }

    void Update()
    {
        enabled = false;   // until the next publish wakes it
        bool u = false, d = false, ur = false, ul = false, dl = false, dr = false;
        // Under fog of war an enemy's supply is drawn only on tiles the viewer sees (so it can be
        // cut), never in the fog.
        Player viewer = FogOfWar.Viewer;
        bool shown = viewer == null || viewer == myPlayer || FogOfWar.TileVisible(viewer, myTile);
        if (shown && myTile.isSupplied(myPlayer)) {
            List<HexTile> neighbors = myTile.neighbors;
            for (int i = 0; i < neighbors.Count; i++) {
                HexTile tile = neighbors[i];
                if (tile == null || tile.isSupplied(myPlayer)) continue;
                if (tile.x == myTile.x) {
                    if (tile.z > myTile.z) u = true; else d = true;
                } else if (tile.x > myTile.x) {
                    if (tile.z == myTile.z) ur = true; else dr = true;
                } else { //tile.x < x
                    if (tile.y == myTile.y) ul = true; else dl = true;
                }
            }
        }
        Show(up, u); Show(down, d); Show(upright, ur); Show(upleft, ul); Show(downleft, dl); Show(downright, dr);
    }

    static void Show(SpriteRenderer r, bool on) {
        if (r != null && r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
    }
}
