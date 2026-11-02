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
            FogOfWar.Changed();
            return newboy;
        }
        else return null;
    }

    /// <summary>
    /// A new unit already aboard <paramref name="transport"/> (a loaded save or scenario): on no tile of its own, in the
    /// transport's cargo as if it had boarded (Unit.OnBoarded: its sprites in the transport's next slot).
    /// </summary>
    public Unit CreateAboard(Player player, Unit unit, Unit transport)
    {
        if (unit == null || transport == null || transport.tile == null) return null;
        Unit cargo = GameObject.Instantiate(unit.gameObject, transport.transform.position, Quaternion.identity).GetComponent<Unit>();
        cargo.tile = transport.tile;
        cargo.owner = player;
        units.Add(cargo);
        if (player != null) player.units.Add(cargo);
        cargo.trasnporter = transport;
        transport.transported.Add(cargo);
        cargo.OnBoarded(transport);
        FogOfWar.Changed();
        return cargo;
    }
}
