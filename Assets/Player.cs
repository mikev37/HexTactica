using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player : MonoBehaviour
{
    public string personal_name;
    public long id;
    public Color displayColor;
    public Faction faction;
    public bool mainPlayer;
    public List<Unit> units;

    [Header("Economy")]
    public int money;

    [Header("State")]
    public bool eliminated;

    [Header("Turn telemetry (read by the harness)")]
    public int unitTurnsOutOfSupply;
    public int unitsLostToAttrition;
    public int hubsStranded;

    public void StartTurn() {
        // Suppression lasts until the unit's own turn: every pip comes back now.
        foreach (Unit u in units) if (u != null) u.suppressed = 0;
        CollectIncome();

        SupplyNetwork.Result supply = SupplyNetwork.Solve(this, true);
        hubsStranded = supply.strandedHubs.Count;

        // A stranded hub stands inside its own supply bubble, so a naive resupply
        // pass heals it by exactly the 1 point the drain just took — and it sustains
        // itself forever, which defeats the doc's six turns of autonomy entirely.
        // A hub cannot resupply itself out of its own stock; it must be reconnected.
        HashSet<Unit> selfSupplying = new HashSet<Unit>();
        for (int i = 0; i < supply.strandedHubs.Count; i++) {
            SupplyPoint sp = supply.strandedHubs[i];
            if (sp != null && sp.myUnit != null) selfSupplying.Add(sp.myUnit);
        }

        // Snapshot the list: attrition can remove units mid-iteration.
        List<Unit> roster = new List<Unit>(units);
        for (int i = 0; i < roster.Count; i++) {
            Unit u = roster[i];
            if (u == null || u.tile == null) continue;
            u.attackedThisTurn = false;
            // Carried units get what their transport gets (below), never anything of their own.
            if (u.trasnporter != null) continue;
            Upkeep(u, roster, selfSupplying);
        }
    }

    enum Fed { Resupplied, Starved, Aloft }

    /// <summary>One unit's turn-start supply, and the same for whatever it carries.</summary>
    void Upkeep(Unit u, List<Unit> roster, HashSet<Unit> selfSupplying) {
        Fed fed = UpkeepOne(u, roster, selfSupplying);
        CargoUpkeep(u, fed);
    }

    /// <summary>What is aboard <paramref name="u"/> (and aboard that, and so on) gets what <paramref name="u"/> got.</summary>
    void CargoUpkeep(Unit u, Fed fed) {
        if (u.transported == null) return;
        foreach (Unit c in u.transported.ToArray()) {
            if (c == null) continue;
            c.attackedThisTurn = false;
            if (fed == Fed.Resupplied) Resupplied(c, true);
            else if (fed == Fed.Starved || (u.suppliesMax > 0 && u.supplies <= 0)) Starve(c);
            // Aloft or at sea with supplies aboard: carried along, neither fed nor starving.
            CargoUpkeep(c, fed);
        }
    }

    Fed UpkeepOne(Unit u, List<Unit> roster, HashSet<Unit> selfSupplying) {
        {

            // On one of our buildings that services its kind (an airport, a naval yard): full service.
            if (Building.ServicingUnder(u) != null) { Resupplied(u, true); return Fed.Resupplied; }
            // Aircraft and ships take nothing from the supply network and do not starve: they run on
            // fuel. Air units burn minFuelPerTurn every turn away from base (an air tanker beside
            // them at the start of the turn fills them instead); out of fuel, they are ditched.
            if (LocomotionRules.NeedsBase(u.locomotion)) { Aloft(u, roster); return Fed.Aloft; }

            if (u.tile.isSupplied(this)) {
                Resupplied(u, !selfSupplying.Contains(u));
                return Fed.Resupplied;
            }
            Starve(u);
            return Fed.Starved;
        }
    }

    /// <summary>
    /// "Heal 1 point, fully re-arm all ammo, full refuel."
    ///
    /// <paramref name="mayHeal"/> is false for a cut-off hub standing in its own
    /// bubble: it can still hand out ammo and fuel from what it is carrying, but its
    /// health IS the supply it is spending, so it cannot top itself back up.
    /// </summary>
    void Resupplied(Unit u, bool mayHeal) {
        if (mayHeal) u.Strength = Mathf.Min(6, u.Strength + 1);
        u.ammo = u.ammoMax;
        u.fuel = u.fuelMax;
        u.supplies = u.suppliesMax;
        u.refuelStock = u.refuelStockMax;
    }

    /// <summary>Upkeep of an aircraft or ship away from its base (see StartTurn).</summary>
    void Aloft(Unit u, List<Unit> roster) {
        // Its own supplies run down a turn at a time (enough to reach a base, usually); out of
        // them, it weakens a strength a turn, like any unit out of supply.
        if (u.suppliesMax > 0) {
            if (u.supplies > 0) u.supplies--;
            else {
                u.Strength = Mathf.Max(0, u.Strength - 1);
                if (u.Strength <= 0) { unitsLostToAttrition++; u.Perish(); return; }
            }
        }
        if (LocomotionRules.IsAirborne(u.locomotion) && u.fuelMax > 0) {
            Unit tanker = TankerBeside(u, roster);
            if (tanker != null) {
                u.fuel = u.fuelMax;
                tanker.refuelStock = Mathf.Max(0, tanker.refuelStock - 1);
                u.PlayRefuelHop(tanker);
            } else {
                u.fuel = Mathf.Max(0, u.fuel - u.minFuelPerTurn);
            }
        }
        if (u.fuelMax > 0 && u.fuel <= 0) {
            unitsLostToAttrition++;
            u.Ditch();
        }
    }

    /// <summary>A friendly air tanker beside <paramref name="u"/> with supplies left to give.</summary>
    static Unit TankerBeside(Unit u, List<Unit> roster) {
        if (u.airTanker) return null;
        for (int i = 0; i < roster.Count; i++) {
            Unit t = roster[i];
            if (t == null || t == u || !t.airTanker || t.tile == null || t.Strength <= 0 || t.refuelStock <= 0) continue;
            if (u.tile.neighbors.Contains(t.tile)) return t;
        }
        return null;
    }

    /// <summary>
    /// Out of supply, a unit degrades along three axes at different rates, which is
    /// what makes stranding legible rather than a silent HP tick: it stops moving,
    /// then stops shooting, then gives up.
    /// </summary>
    void Starve(Unit u) {
        unitTurnsOutOfSupply++;

        if (u.suppliesMax > 0) {
            u.supplies = Mathf.Max(0, u.supplies - 1);
            if (u.supplies <= 0) {
                // "once out of supplies, the unit will give up"
                u.Strength = Mathf.Max(0, u.Strength - 1);
                if (u.Strength <= 0) {
                    unitsLostToAttrition++;
                    u.Perish();
                    return;
                }
            }
        }

        if (u.fuelMax > 0 && LocomotionRules.BurnsFullFuelPerTurn(u.locomotion)) {
            u.fuel = Mathf.Max(0, u.fuel - Mathf.Max(1, u.minFuelPerTurn));
        } else if (u.fuelMax > 0) {
            u.fuel = Mathf.Max(0, u.fuel - u.minFuelPerTurn);
        }
    }

    void CollectIncome() {
        for (int i = 0; i < Building.all.Count; i++) {
            Building b = Building.all[i];
            if (b != null && b.owner == this && b.Functioning) money += b.income;   // a destroyed building earns nothing
        }
    }

    /// <summary>Recompute the supply field without charging anyone for it.</summary>
    public void Resupply() {
        SupplyNetwork.Solve(this, false);
    }

    public void EndTurn() {
        units.ForEach(x => {
            if (x == null) return;
            x.active = true;
        });
    }

    /// <summary>
    /// True while this player owns an HQ building or a living producer supply unit.
    /// The shipped game's HQ is the latter — TempHQ is a Unit, not a Building — so a
    /// Building-only check meant the HQ could be destroyed without ending anything.
    /// </summary>
    public bool HasHQ {
        get {
            for (int i = 0; i < Building.all.Count; i++) {
                Building b = Building.all[i];
                if (b != null && b.owner == this && b.IsHQ) return true;
            }
            for (int i = 0; i < units.Count; i++) {
                Unit u = units[i];
                if (u == null || u.Strength <= 0) continue;
                SupplyPoint sp = u.GetComponent<SupplyPoint>();
                if (sp != null && sp.producer) return true;
            }
            return false;
        }
    }

    /// <summary>Set once this player has been seen holding an HQ. Losing it afterwards eliminates them.</summary>
    [HideInInspector] public bool hadHQ;

    public int ArmyValue {
        get {
            int v = 0;
            for (int i = 0; i < units.Count; i++) if (units[i] != null) v += units[i].value();
            return v;
        }
    }
}


public struct PlayerData {
    public string personal_name;
    public long id;
    public Color displayColor;
    public Faction faction;
    public bool AI;
    public int money;
    public bool eliminated;
    public PlayerData(Player t) {
        personal_name = t.personal_name;
        id = t.id;
        displayColor = t.displayColor;
        faction = t.faction;
        AI = t.GetComponent<AIPlayer>();
        money = t.money;
        eliminated = t.eliminated;
    }

    public Player load() {
        PlayerData that = this;
        Player output = null;
        GameController.gc.players.ForEach(pl => { 
            if(pl.id == that.id) {
                output = pl;
            }
        });
        if(output == null) {
            GameObject go = GameObject.Instantiate(new GameObject());
            output = go.AddComponent<Player>();
        }
        output.personal_name = that.personal_name;
        output.displayColor = that.displayColor;
        output.faction = that.faction;
        output.money = that.money;
        output.eliminated = that.eliminated;
        if (AI && output.GetComponent<AIPlayer>() == null) {
            output.gameObject.AddComponent<AIPlayer>();
        }
        return output;
    }
}
