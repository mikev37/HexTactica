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
    // Never null: a side made in code (AddComponent: a side the file has and the scene does not, or one the map
    // editor adds) gets no list from Unity's serialisation.
    public List<Unit> units = new List<Unit>();

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
        // Air tankers and supply ships resupply this turn if they started it with supplies (whatever order the roster runs in).
        tankersStocked.Clear();
        foreach (Unit t in roster) if (t != null && (t.airTanker || IsSupplyShip(t)) && t.supplies > 0) tankersStocked.Add(t);
        InUpkeep = true;
        try {
            for (int i = 0; i < roster.Count; i++) {
                Unit u = roster[i];
                if (u == null || u.tile == null) continue;
                u.attackedThisTurn = false;
                // Carried units get what their transport gets (below), never anything of their own.
                if (u.trasnporter != null) continue;
                Upkeep(u, roster, selfSupplying);
            }
        } finally { InUpkeep = false; }
        tankersStocked.Clear();
        // Units lost to attrition while feeding (Unit.ResupplyOnAnyDeath): every side's supply now they are gone.
        if (ResupplyAfterUpkeep) {
            ResupplyAfterUpkeep = false;
            if (GameController.gc != null) foreach (Player p in GameController.gc.players) if (p != null) p.Resupply();
        }
    }

    /// <summary>A side's turn-start upkeep is feeding its units (StartTurn): deaths then wait to re-solve supply until it is done.</summary>
    public static bool InUpkeep;
    /// <summary>A unit died during upkeep: re-solve every side's supply when it is done.</summary>
    public static bool ResupplyAfterUpkeep;

    /// <summary>The air tankers that started this turn with supplies: they resupply the aircraft beside them (StartTurn).</summary>
    readonly HashSet<Unit> tankersStocked = new HashSet<Unit>();

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

            // On one of our buildings that services its kind (an airport, a naval yard, a city): what
            // that building gives (Building.healPerTurn, rearm, refuel, resupply), together with what
            // the supply network gives where it reaches (land units, helicopters).
            Building service = Building.ServicingUnder(u);
            if (service != null) {
                bool network = (LocomotionRules.ResuppliedInSupply(u.locomotion) || !LocomotionRules.NeedsBase(u.locomotion)) && u.tile.isSupplied(this);
                Serviced(u, service, network, true);
                if (network || service.resupply) return Fed.Resupplied;
                // Healed or rearmed but given no rations: it lives on its own, as anywhere else.
                if (LocomotionRules.NeedsBase(u.locomotion)) { Aloft(u, roster); return Fed.Aloft; }
                Starve(u);
                return Fed.Starved;
            }
            // Aircraft and ships take nothing from the supply network and do not starve: they run on
            // fuel. Air units burn minFuelPerTurn every turn away from base (an air tanker beside
            // them at the start of the turn fills them instead); out of fuel, they are ditched.
            // Helicopters are also resupplied on any tile in supply, like ground units.
            if (LocomotionRules.ResuppliedInSupply(u.locomotion) && u.tile.isSupplied(this)) { Resupplied(u, true); return Fed.Resupplied; }
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
    }

    /// <summary>
    /// A turn on a building that services <paramref name="u"/>: the building's heal, ammunition,
    /// fuel and supplies as it gives them, and — when <paramref name="network"/>, the unit being in
    /// supply too — the network's full refill and heal of 1 (the larger heal of the two, or both
    /// when the building's stacks).
    /// </summary>
    void Serviced(Unit u, Building b, bool network, bool mayHeal) {
        int heal = network ? (b.healStacksWithSupply ? 1 + b.healPerTurn : Mathf.Max(1, b.healPerTurn)) : b.healPerTurn;
        if (mayHeal && heal > 0) u.Strength = Mathf.Min(6, u.Strength + heal);
        if (network || b.rearm) u.ammo = u.ammoMax;
        if (network || b.refuel) u.fuel = u.fuelMax;
        if (network || b.resupply) u.supplies = u.suppliesMax;
    }

    /// <summary>Upkeep of an aircraft or ship away from its base (see StartTurn).</summary>
    void Aloft(Unit u, List<Unit> roster) {
        // Its own supplies run down a turn at a time (enough to reach a base, usually); out of
        // them, it weakens a strength a turn, like any unit out of supply. An air tanker too: while it has
        // supplies it also resupplies the aircraft beside it, at no further cost.
        if (u.suppliesMax > 0) {
            if (u.supplies > 0) {
                u.supplies--;
                // A ship mends itself at sea while it has supplies (its crew's repairs): a strength a turn, until sunk.
                if (LocomotionRules.IsNaval(u.locomotion)) u.Strength = Mathf.Min(6, u.Strength + 1);
            } else {
                u.Strength = Mathf.Max(0, u.Strength - 1);
                if (u.Strength <= 0) { unitsLostToAttrition++; u.Perish(); return; }
            }
        }
        // A supply ship beside it at the start of the turn refuels and restocks a ship, as an air tanker does an aircraft.
        if (LocomotionRules.IsNaval(u.locomotion) && (u.fuelMax > 0 || u.suppliesMax > 0)) {
            Unit tender = TankerBeside(u, roster);
            if (tender != null) { u.fuel = u.fuelMax; u.supplies = u.suppliesMax; }
        }
        if (LocomotionRules.IsAirborne(u.locomotion) && u.fuelMax > 0) {
            Unit tanker = TankerBeside(u, roster);
            if (tanker != null) {
                u.fuel = u.fuelMax;
                u.supplies = u.suppliesMax;
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

    /// <summary>
    /// A supply ship: a ship carrying a supply point of its own (a supply boat). It tends the ships beside it as an air
    /// tanker does the aircraft beside it (Aloft).
    /// </summary>
    public static bool IsSupplyShip(Unit u) {
        return u != null && LocomotionRules.IsNaval(u.locomotion) && u.Hub != null && !u.Hub.producer;
    }

    /// <summary>
    /// The tender beside <paramref name="u"/> that started the turn with supplies: for an aircraft an air tanker, for a
    /// ship a supply ship. A tanker (or supply ship) is never resupplied by another.
    /// </summary>
    Unit TankerBeside(Unit u, List<Unit> roster) {
        bool ship = LocomotionRules.IsNaval(u.locomotion);
        if (u.airTanker || (ship && IsSupplyShip(u))) return null;
        for (int i = 0; i < roster.Count; i++) {
            Unit t = roster[i];
            if (t == null || t == u || t.tile == null || t.Strength <= 0 || !tankersStocked.Contains(t)) continue;
            if (ship ? !IsSupplyShip(t) : !t.airTanker) continue;
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
                SupplyPoint sp = u.Hub;
                if (sp != null && sp.producer) return true;
            }
            return false;
        }
    }

    /// <summary>The map makes this side this faction (the pre-game screen shows it and cannot change it).</summary>
    [Tooltip("The map makes this side its faction: the pre-game screen cannot change it.")]
    public bool factionLocked;

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
    /// <summary>The faction's asset name (a reference does not survive a save): resolved through LevelEditor.FindFaction.</summary>
    public string factionName;
    public bool AI;
    /// <summary>The AI profile that plays it (an AIProfile's profileName, among FrontEndSettings.aiProfiles); "" the default.</summary>
    public string aiProfile;
    /// <summary>The map makes this side this faction: the pre-game screen cannot change it.</summary>
    public bool factionLocked;
    public int money;
    public bool eliminated;
    public PlayerData(Player t) {
        personal_name = t.personal_name;
        id = t.id;
        displayColor = t.displayColor;
        factionName = t.faction != null ? t.faction.name : "";
        AI = AIControl.Of(t) != null;
        AIPlayer full = t.GetComponent<AIPlayer>();
        aiProfile = AI && full != null && full.profile != null ? full.profile.profileName : "";
        factionLocked = t.factionLocked;
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
            // A side the scene does not have (a map made with more players): made here, and it plays.
            GameObject go = new GameObject(that.personal_name + " (player)");
            output = go.AddComponent<Player>();
            output.id = that.id;
            GameController.gc.players.Add(output);
            HexGenerator.forAllTiles(t => { t.EnsureSide(output); return 0; });
        }
        output.factionLocked = that.factionLocked;
        output.personal_name = that.personal_name;
        output.displayColor = that.displayColor;
        // (A save that names none — older ones, or a side without a faction — leaves the side's faction as the scene set it.)
        if (!string.IsNullOrEmpty(that.factionName)) output.faction = LevelEditor.FindFaction(that.factionName);
        output.money = that.money;
        output.eliminated = that.eliminated;
        if (AI && AIControl.Of(output) == null) {
            output.gameObject.AddComponent<AIPlayer>();
        }
        return output;
    }
}
