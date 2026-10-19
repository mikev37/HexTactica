using System.Collections.Generic;
using UnityEngine;

public enum BuildingType {
    HQ,
    Fort,
    City,
    Town,
    Village,
    Factory,
    Airport,
    NavalYard,
    AirResupply,
    Radar
}

/// <summary>
/// Static map installations. Buildings are the roots of the supply network and
/// the win condition: "Capturing HQ ends the game for that player."
/// </summary>
public class Building : MonoBehaviour {

    [Header("Identity")]
    public string personal_name;
    public BuildingType type = BuildingType.Village;
    public HexTile tile;
    public Player owner;

    [Header("Render")]
    public SpriteRenderer sprite;
    public SpriteRenderer ownerFlag;

    [Header("Economy")]
    [Tooltip("Cash gathered per turn for the owner")]
    public int income;

    [Header("Supply")]
    [Tooltip("Whether this building injects supply into the network")]
    public bool producesSupply;
    [Tooltip("How far supply reaches from here, in movement points")]
    public int supplyRange = 4;
    [Tooltip("Terrain resistance ignored when pushing supply out from this building")]
    public int supplyScramble = 1;
    [Tooltip("Stock remaining. HQs are effectively infinite; cities and towns run down.")]
    public int supplies = 999;
    public int suppliesMax = 999;

    [Header("Capture")]
    [Tooltip("Capture progress required. Infantry contribute their Strength per turn.")]
    public int captureThreshold = 6;
    public int captureProgress;
    public Player capturingPlayer;

    [Header("Condition")]
    [Range(0, 3)]
    public int damageLevel;

    [Header("Production")]
    public List<Unit> buildable;

    public static readonly List<Building> all = new List<Building>();

    void Awake() {
        if (!all.Contains(this)) all.Add(this);
    }

    void OnDestroy() {
        all.Remove(this);
    }

    void Start() {
        if (tile != null) tile.building = this;
        RefreshFlag();
    }

    public bool IsHQ { get { return type == BuildingType.HQ; } }

    public bool CanProduce { get { return buildable != null && buildable.Count > 0; } }

    /// <summary>Only infantry take ground. Vehicles can sit on a building but not flip it.</summary>
    public static bool CanCapture(Unit u) {
        return u != null
            && u.Strength > 0
            && (u.locomotion == Locomotion.Boots || u.locomotion == Locomotion.Marine);
    }

    /// <summary>
    /// Advance capture. Returns true when ownership actually changes this call.
    /// Progress resets if a different player starts working on it, so contested
    /// buildings do not accumulate progress from both sides at once.
    /// </summary>
    public bool Capture(Unit capturer) {
        if (!CanCapture(capturer)) return false;
        if (capturer.owner == owner) return false;

        if (capturingPlayer != capturer.owner) {
            capturingPlayer = capturer.owner;
            captureProgress = 0;
        }

        captureProgress += Mathf.Max(1, capturer.Strength);
        if (captureProgress < captureThreshold) return false;

        Player previous = owner;
        owner = capturer.owner;
        captureProgress = 0;
        capturingPlayer = null;
        RefreshFlag();

        if (IsHQ && GameController.gc != null) {
            GameController.gc.OnHQCaptured(this, previous);
        }
        return true;
    }

    /// <summary>Capture progress decays when nobody is standing on the building.</summary>
    public void TickCapture() {
        if (tile == null) return;
        bool held = tile.occupier != null
                 && capturingPlayer != null
                 && tile.occupier.owner == capturingPlayer
                 && CanCapture(tile.occupier);
        if (!held && captureProgress > 0) {
            captureProgress = Mathf.Max(0, captureProgress - 2);
            if (captureProgress == 0) capturingPlayer = null;
        }
    }

    public void RefreshFlag() {
        if (ownerFlag != null && owner != null) ownerFlag.color = owner.displayColor;
    }

    /// <summary>HQs never run dry; everything else spends stock to push supply.</summary>
    public bool HasSupplyToGive {
        get { return producesSupply && (IsHQ || supplies > 0); }
    }

    public void SpendSupply(int amount) {
        if (IsHQ) return;
        supplies = Mathf.Max(0, supplies - amount);
    }

    public void Replenish(int amount) {
        supplies = Mathf.Min(suppliesMax, supplies + amount);
    }
}
