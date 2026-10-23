using UnityEngine;

public class Marine : MonoBehaviour
{
    //Put this on the object on a amphibious unit that appears in the water
    Unit myUnit;
    Vector3 size;
    void Start()
    {
        myUnit = GetComponentInParent<Unit>();
        size = transform.localScale;
    }

    void Update()
    {
        HexTile myTile = TileUnderSprite();
        Vector3 desiredsize = size;
        if (myTile.terrain.elevation >= 0)
            desiredsize *= 0;
        transform.localScale = Vector3.Lerp(transform.localScale, desiredsize, Time.deltaTime*5);
        if (transform.localScale.x >= .9999f)
            transform.localScale = Vector3.one;
        else if (transform.localScale.x <= .00001f)
            transform.localScale = Vector3.zero;
    }

    HexTile TileUnderSprite() {
        HexTile best = myUnit.tile;
        float bestD = float.MaxValue;
        Vector2 here = myUnit.transform.position;
        foreach (HexTile t in HexGenerator.hg.tiles) {
            if (t == null || t.unitPlacement == null) continue;
            float d = ((Vector2)t.unitPlacement.position - here).sqrMagnitude;
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }
}
