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
    // The border is redrawn only when a supply field has been published since the last time
    // (SupplyNetwork.Published), and an edge is switched only when it changes. (It was switched off
    // and on again on every tile, for every player, every frame, with a fresh closure each time:
    // thousands of SetActive calls and allocations a frame on a large map.)
    int seen = -1;

    void Update()
    {
        if (seen == SupplyNetwork.Published) return;
        seen = SupplyNetwork.Published;
        bool u = false, d = false, ur = false, ul = false, dl = false, dr = false;
        if (myTile.isSupplied(myPlayer)) {
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
