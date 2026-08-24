using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitController : MonoBehaviour
{
    public static UnitController uc;

    public List<Unit> units;
    // Start is called before the first frame update
    void Start()
    {
        uc = this;
    }

    // Update is called once per frame
    public Unit CreateUnit(Player player, Unit unit, HexTile tile)
    {
        if (!tile.occupier) {
            Unit newboy = GameObject.Instantiate(unit.gameObject, tile.unitPlacement.transform.position, Quaternion.identity).GetComponent<Unit>();
            newboy.tile = tile;
            tile.occupier = newboy;
            newboy.owner = player;
            units.Add(newboy);
            player.units.Add(newboy);
            return newboy;
        }
        else return null;
    }
}
