using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SupplyPoint : MonoBehaviour
{
    public Unit myUnit;
    public int range;
    public bool producer; //whether this makes new supplies or just redistributes them
    public bool supplying; //whether this unit is supplying this turn
    public SupplyPoint parent; //
    public LineRenderer line; // for showing where the supplies come from

    public List<SupplyPoint> children;
    // Start is called before the first frame update
    void Start()
    {
        myUnit = GetComponent<Unit>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    internal void SupplyZone(SupplyPoint start) {
        parent = start;
        children.Clear();
        supplying = true;
        List<HexTile> supplied = myUnit.tile.CheckMovementRange(myUnit,range);
        supplied.ForEach(x => {
            int supl = Mathf.Max(x.supply[myUnit.owner], range - (int)x.G);
            x.supply[myUnit.owner] = supl;
            x.textField.text = "" + (range);
            
            if(x.isSupplied(myUnit.owner) && x.occupier != null && x.occupier.owner == myUnit.owner && x.occupier.GetComponent<SupplyPoint>()) {
                SupplyPoint sup = x.occupier.GetComponent<SupplyPoint>();
                if (!sup.supplying)
                    children.Add(sup);
            }
        });
        children.ForEach(sup => { sup.SupplyZone(this); });
    }
}
