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
    void Update()
    {
        up.gameObject.SetActive(false);
        down.gameObject.SetActive(false);
        upright.gameObject.SetActive(false);
        upleft.gameObject.SetActive(false);
        downleft.gameObject.SetActive(false);
        downright.gameObject.SetActive(false);
        if (myTile.isSupplied(myPlayer)) {
            myTile.neighbors.ForEach(tile => {
                if (!tile.isSupplied(myPlayer)) {
                    if (tile.x == myTile.x) {
                        if (tile.z > myTile.z) {
                            up.gameObject.SetActive(true);
                        } else {
                            down.gameObject.SetActive(true);
                        }
                    } else if (tile.x > myTile.x) {
                        if (tile.z == myTile.z) {
                            upright.gameObject.SetActive(true);
                        } else {
                            downright.gameObject.SetActive(true);
                        }
                    } else { //tile.x < x
                        if (tile.y == myTile.y) {
                            upleft.gameObject.SetActive(true);
                        } else {
                            downleft.gameObject.SetActive(true);
                        }
                    }
                }
            });
        }
    }
}
