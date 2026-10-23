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
        HexTile myTile = myUnit.tile;
        Vector3 desiredsize = size;
        if (myTile.terrain.elevation >= 0)
            desiredsize *= 0;
        transform.localScale = Vector3.Lerp(transform.localScale, desiredsize, Time.deltaTime);
    }
}
