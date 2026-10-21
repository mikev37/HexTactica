using System;
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
    Radar,
    FOB
}

/// <summary>A tile a building covers, relative to its anchor tile (cube offsets: x + y + z = 0).</summary>
[Serializable]
public struct TileOffset {
    public int x, y;
    public TileOffset(int x, int y) { this.x = x; this.y = y; }
}

/// <summary>
/// A building: a prefab (its own sprites and animations, like a unit) that stands on one or
/// more tiles and replaces their ground (its own cover and movement cost, like a tile). Units
/// stand on top of it. It belongs to a player, or to nobody.
///
/// Capture is implicit: at the end of a player's turn, every infantry unit of theirs standing on
/// one of the building's tiles that did not attack this turn captures, adding its strength.
///   - It takes CaptureThreshold points (10, +5 per extra tile) to take a neutral building.
///   - Ownership is sticky: a neutral building stays neutral until someone reaches the threshold,
///     and an owned one stays owned until its points are brought down to 0, when it becomes
///     neutral. Points beyond what it takes to bring it to 0 carry over as the capturer's
///     progress: 3 Blue points and a 6-strength Red grunt leave it neutral with 3 Red.
///   - Contested (infantry of another side also on it): nobody can raise points; the side
///     capturing can still bring the others' points down to 0, but not past.
///   - Neutral progress decays by 2 at the end of the capturing side's turn when none of its
///     infantry is on the building. An owned building never decays.
///   - The HQ: losing it ends the game under standard rules; under attrition it cannot be captured.
///
/// Supply: a building that produces supply is a relay when it is connected to its owner's network
/// (like a supply unit), and charges its store by chargePerTurn each turn it is. Cut off, it becomes
/// its own source while the store lasts, spending one point per turn of supply given out. The HQ
/// is the root of the network and never runs dry.
///
/// Service: units of the listed locomotion standing on it are healed, refuelled and rearmed each
/// turn (an airport for aircraft, a naval yard for ships); aircraft and ships are resupplied only
/// this way.
///
/// Production: clicking an owned building with no unit on it opens its build list (BuildingMenu);
/// units are paid for from the owner's money (income from buildings).
/// </summary>
public class Building : MonoBehaviour {

    [Header("Identity")]
    public string personal_name;
    public BuildingType type = BuildingType.Village;
    [Tooltip("Anchor tile (set on placement)")]
    public HexTile tile;
    public Player owner;

    [Header("Footprint and ground")]
    [Tooltip("Extra tiles the building covers, relative to its anchor (cube x, y)")]
    public List<TileOffset> footprint = new List<TileOffset>();
    [Tooltip("The ground of every tile it covers: cover, movement cost and floor sprites. Empty keeps the terrain.")]
    public TerrainData ground;
    [NonSerialized] public List<HexTile> tiles = new List<HexTile>();
    readonly Dictionary<HexTile, TerrainData> underlying = new Dictionary<HexTile, TerrainData>();

    [Header("Render")]
    public SpriteRenderer sprite;
    [Tooltip("Parts tinted with the owner's colour (neutral: grey)")]
    public SpriteRenderer ownerFlag;
    [Tooltip("The capture flag, raised along its pole as points build up")]
    public Transform flag;
    public SpriteRenderer flagSprite;
    public float flagBottom = 0f, flagTop = 1.2f;

    [Header("Economy")]
    [Tooltip("Cash gathered per turn for the owner")]
    public int income;

    [Header("Supply")]
    [Tooltip("Whether this building supplies the area around it")]
    public bool producesSupply;
    [Tooltip("How far supply reaches from here, in movement points")]
    public int supplyRange = 4;
    [Tooltip("Terrain resistance ignored when pushing supply out from this building")]
    public int supplyScramble = 1;
    [Tooltip("Store: turns of supply it can give out while cut off. HQs never run dry.")]
    public int supplies = 0;
    public int suppliesMax = 6;
    [Tooltip("Store gained per turn while connected to the owner's network")]
    public int chargePerTurn = 1;

    [Header("Service")]
    [Tooltip("Locomotion types healed, refuelled and rearmed while standing on this building")]
    public List<Locomotion> services = new List<Locomotion>();

    [Header("Capture")]
    [Tooltip("Owner's points when owned; the capturing side's progress when neutral")]
    public int capturePoints;
    [Tooltip("Whose progress capturePoints is, while neutral")]
    public Player capturingPlayer;

    [Header("Condition")]
    [Range(0, 3)]
    public int damageLevel;

    [Header("Production")]
    public List<Unit> buildable;

    public static readonly List<Building> all = new List<Building>();

    /// <summary>A building changed hands (or became neutral): building, previous owner.</summary>
    public static event Action<Building, Player> OwnerChanged;
    /// <summary>Capture points moved: building, player capturing, points before, points after.</summary>
    public static event Action<Building, Player, int, int> Captured;

    float flagShown = -1f;

    void Awake() {
        if (!all.Contains(this)) all.Add(this);
    }

    void OnDestroy() {
        all.Remove(this);
    }

    void Start() {
        RefreshFlag();
    }

    void Update() {
        // The flag climbs (or drops) to where the points are, rather than jumping.
        if (flag == null) return;
        float target = FlagHeight;
        flagShown = flagShown < 0f ? target : Mathf.MoveTowards(flagShown, target, Time.deltaTime * 0.8f);
        Vector3 p = flag.localPosition;
        p.y = flagBottom + (flagTop - flagBottom) * flagShown;
        flag.localPosition = p;
    }

    public bool IsHQ { get { return type == BuildingType.HQ; } }

    public bool CanProduce { get { return buildable != null && buildable.Count > 0; } }

    public int CaptureThreshold { get { return 10 + 5 * Mathf.Max(0, Footprint.Count - 1); } }

    /// <summary>The tiles it stands on (anchor first).</summary>
    public List<HexTile> Footprint {
        get {
            if (tiles.Count == 0 && tile != null) tiles.Add(tile);
            return tiles;
        }
    }

    /// <summary>0..1: how far up its pole the flag is.</summary>
    public float FlagHeight { get { return Mathf.Clamp01(capturePoints / (float)CaptureThreshold); } }

    /// <summary>Only infantry take ground. Vehicles can sit on a building but not flip it.</summary>
    public static bool CanCapture(Unit u) {
        return u != null
            && u.Strength > 0
            && u.GetComponent<SupplyPoint>() == null
            && (u.locomotion == Locomotion.Boots || u.locomotion == Locomotion.Marine);
    }

    /// <summary>Can this building change hands under the current rules? (Attrition: not the HQ.)</summary>
    public bool Capturable {
        get { return !(IsHQ && GameController.gc != null && GameController.gc.rules == RuleSet.Attrition); }
    }

    // ------------------------------------------------------------------ placement

    /// <summary>
    /// Stand on <paramref name="anchor"/> and the footprint around it: each tile takes the
    /// building's ground (its old terrain is kept, for saving and for removal).
    /// </summary>
    public void Occupy(HexTile anchor) {
        Release();
        tile = anchor;
        tiles.Clear();
        if (anchor == null) return;
        tiles.Add(anchor);
        if (footprint != null && HexGenerator.hg != null) {
            foreach (TileOffset o in footprint) {
                HexTile t = HexGenerator.hg.get(anchor.x + o.x, anchor.y + o.y, anchor.z - o.x - o.y);
                if (t != null && !tiles.Contains(t)) tiles.Add(t);
            }
        }
        foreach (HexTile t in tiles) {
            t.building = this;
            underlying[t] = t.terrain;
            if (ground != null) SetGround(t, ground);
        }
        if (owner != null && capturePoints <= 0) capturePoints = CaptureThreshold;
    }

    /// <summary>Give the tiles their own terrain back.</summary>
    public void Release() {
        foreach (KeyValuePair<HexTile, TerrainData> kv in underlying) {
            if (kv.Key == null) continue;
            if (kv.Key.building == this) kv.Key.building = null;
            if (ground != null && kv.Value != null) SetGround(kv.Key, kv.Value);
        }
        underlying.Clear();
    }

    /// <summary>The terrain under the building on <paramref name="t"/> (what a save records).</summary>
    public TerrainData Underlying(HexTile t) {
        TerrainData d;
        return t != null && underlying.TryGetValue(t, out d) ? d : (t != null ? t.terrain : null);
    }

    static void SetGround(HexTile t, TerrainData d) {
        if (t.upper != null) t.loadData(d); else t.terrain = d;
    }

    // ------------------------------------------------------------------ capture

    /// <summary>
    /// End of <paramref name="side"/>'s turn: its infantry on buildings capture (see the class
    /// notes). Called by GameController before the turn passes.
    /// </summary>
    public static void ResolveCaptures(Player side) {
        if (side == null) return;
        List<Building> snapshot = new List<Building>(all);
        foreach (Building b in snapshot) if (b != null) b.ResolveCapture(side);
    }

    void ResolveCapture(Player side) {
        if (!Capturable) return;
        int mine = 0;
        bool opposed = false;
        foreach (HexTile t in Footprint) {
            Unit u = t.occupier;
            if (u == null || !CanCapture(u)) continue;
            if (u.owner == side) { if (!u.attackedThisTurn) mine += u.Strength; }
            else opposed = true;
        }
        int before = capturePoints;
        Player prevOwner = owner;
        int threshold = CaptureThreshold;

        if (mine == 0) {
            // Neutral progress fades when the side making it has left.
            if (owner == null && capturingPlayer == side && capturePoints > 0) {
                capturePoints = Mathf.Max(0, capturePoints - 2);
                if (capturePoints == 0) capturingPlayer = null;
            }
        } else if (owner == side) {
            // Reinforcing: back up to full, unless contested.
            if (!opposed) capturePoints = Mathf.Min(threshold, capturePoints + mine);
        } else if (owner != null) {
            // Someone else's: bring their points down; anything beyond carries over.
            int left = capturePoints - mine;
            if (left > 0) capturePoints = left;
            else {
                owner = null;
                capturingPlayer = null;
                capturePoints = 0;
                if (left < 0 && !opposed) { capturingPlayer = side; capturePoints = -left; }
            }
        } else if (capturingPlayer == null || capturingPlayer == side) {
            // Neutral, ours to raise — unless contested.
            if (!opposed) { capturingPlayer = side; capturePoints += mine; }
        } else {
            // Neutral with another side's progress: bring it down; beyond 0 it becomes ours.
            int left = capturePoints - mine;
            if (left > 0) capturePoints = left;
            else {
                capturingPlayer = null;
                capturePoints = 0;
                if (left < 0 && !opposed) { capturingPlayer = side; capturePoints = -left; }
            }
        }

        if (owner == null && capturingPlayer != null && capturePoints >= threshold) {
            owner = capturingPlayer;
            capturingPlayer = null;
            capturePoints = threshold;
        }

        if (capturePoints != before || owner != prevOwner) {
            if (Captured != null) Captured(this, side, before, capturePoints);
            RefreshFlag();
        }
        if (owner != prevOwner) {
            if (OwnerChanged != null) OwnerChanged(this, prevOwner);
            if (IsHQ && prevOwner != null && GameController.gc != null) GameController.gc.OnHQCaptured(this, prevOwner);
        }
    }

    /// <summary>
    /// What a capture by <paramref name="side"/> with <paramref name="strength"/> would leave: the
    /// owner afterwards and the points (signed: + for <paramref name="side"/>, - for anyone else).
    /// The same rule as ResolveCapture, for the AI to forecast with.
    /// </summary>
    public int ForecastPoints(Player side, int strength, bool opposed, out Player ownerAfter) {
        ownerAfter = owner;
        int threshold = CaptureThreshold;
        if (!Capturable || strength <= 0) return owner == side || capturingPlayer == side ? capturePoints : -capturePoints;
        if (owner == side) return opposed ? capturePoints : Mathf.Min(threshold, capturePoints + strength);
        Player other = owner ?? capturingPlayer;
        if (other == null || other == side) {
            if (opposed) return capturePoints;
            int p = capturePoints + strength;
            if (p >= threshold) { ownerAfter = side; return threshold; }
            return p;
        }
        int left = capturePoints - strength;
        if (left > 0) return -left;
        if (owner != null) ownerAfter = null;
        if (opposed || left == 0) return 0;
        if (-left >= threshold) { ownerAfter = side; return threshold; }
        return -left;
    }

    public void RefreshFlag() {
        Color c = owner != null ? owner.displayColor : new Color(0.6f, 0.6f, 0.6f, 1f);
        if (ownerFlag != null) ownerFlag.color = c;
        if (flagSprite != null) {
            Player shown = owner ?? capturingPlayer;
            flagSprite.color = shown != null ? shown.displayColor : new Color(0.6f, 0.6f, 0.6f, 1f);
        }
    }

    // ------------------------------------------------------------------ supply and service

    /// <summary>HQs never run dry; any other supply building gives while its store lasts (or while connected).</summary>
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

    public bool Services(Unit u) {
        return u != null && services != null && services.Contains(u.locomotion);
    }

    /// <summary>The owner's building, if any, that services <paramref name="u"/> where it stands.</summary>
    public static Building ServicingUnder(Unit u) {
        if (u == null || u.tile == null) return null;
        Building b = u.tile.building;
        return b != null && b.owner == u.owner && b.Services(u) ? b : null;
    }
}
