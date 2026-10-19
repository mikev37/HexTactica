using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a unit as a supply hub. The propagation itself lives in SupplyNetwork,
/// which solves the whole player's field at once — a hub cannot know on its own
/// whether it is connected to HQ, and connection is what decides whether it pays
/// health to keep supplying.
/// </summary>
public class SupplyPoint : MonoBehaviour {

    public Unit myUnit;

    [Tooltip("How far supply reaches from this hub, in movement points")]
    public int range = 4;

    [Tooltip("Whether this hub originates supply rather than only relaying it")]
    public bool producer;

    [Tooltip("Set each turn by SupplyNetwork: is there an unbroken chain back to HQ?")]
    public bool connected;

    [Tooltip("Set each turn by AISupplyPlanner: how many friendly units this hub keeps alive")]
    public int dependents;

    public LineRenderer line;

    void Awake() {
        if (myUnit == null) myUnit = GetComponent<Unit>();
    }

    void Start() {
        if (myUnit == null) myUnit = GetComponent<Unit>();
    }

    /// <summary>
    /// Turns of autonomy left before the hub burns itself out, if it stays cut off.
    /// This is the number the AI watches: a disconnected hub is a countdown.
    /// </summary>
    public int TurnsOfAutonomy {
        get { return myUnit != null ? Mathf.Max(0, myUnit.Strength) : 0; }
    }

    /// <summary>Kept so older scenes and scripts still compile; the solver drives supply now.</summary>
    internal void SupplyZone(SupplyPoint start) {
        if (myUnit != null && myUnit.owner != null) SupplyNetwork.Solve(myUnit.owner, false);
    }
}
