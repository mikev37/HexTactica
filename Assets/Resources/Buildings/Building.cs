using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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

/// <summary>
/// A building: a prefab (its own sprites and animations, like a unit) that stands on one tile
/// and replaces its ground (its own cover and movement cost, like a tile). Units
/// stand on top of it. It belongs to a player, or to nobody.
///
/// Capture is implicit: an infantry unit that ends its action on the building's tile without having
/// attacked this turn captures there and then, adding its strength (CaptureBy, from Unit.FinishActive);
/// one that stood there all turn without acting captures as the turn ends (ResolveCaptures). Once per
/// turn per building.
///   - capturePoints is capturingPlayer's progress toward taking it from whoever holds it (or from
///     nobody). It starts from 0: an owned building does not have to be brought down to neutral
///     first. At CaptureThreshold (10) it changes hands straight to the capturer, progress cleared.
///   - Another side starting on it starts again from 0 (the earlier progress is lost).
///   - The owner's infantry on it brings a capturer's progress down by its strength.
///   - Progress decays by 2 at the end of the capturing side's turn when it did not capture there.
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
    [SerializeField, FormerlySerializedAs("owner")] Player _owner;
    /// <summary>Who holds it (in a BoardView, the view's: see there).</summary>
    public Player owner {
        get { BoardView v = BoardView.current; return v != null && v.Has(this) ? v.OwnerOf(this) : _owner; }
        set { BoardView v = BoardView.current; if (v != null && v.Has(this)) v.SetOwner(this, value); else _owner = value; }
    }
    internal Player RealOwner { get { return _owner; } }
    internal Player RealCapturingPlayer { get { return _capturingPlayer; } }
    internal int RealCapturePoints { get { return _capturePoints; } }

    /// <summary>This building's number in board views (BoardView), once it has been in one.</summary>
    [NonSerialized] internal int viewId = -1;
    [NonSerialized] string nameCache;

    /// <summary>The object's name; in a BoardView, as it was when the view was made.</summary>
    public new string name {
        get { if (BoardView.current != null && nameCache != null) return nameCache; nameCache = base.name; return nameCache; }
        set { base.name = value; nameCache = value; }
    }

    internal void Prime() { nameCache = base.name; }

    [Header("Ground")]
    [Tooltip("The ground of its tile: cover, movement cost and floor sprites. Empty keeps the terrain.")]
    public TerrainData ground;
    readonly Dictionary<HexTile, TerrainData> underlying = new Dictionary<HexTile, TerrainData>();

    [Header("Render")]
    [Tooltip("The building's picture, swapped for its damage pictures as it wears down. Parts in the owner's colour carry a ColorObjectUnit; the capture flag, a CaptureFlag.")]
    public SpriteRenderer sprite;

    [Header("Vision (fog of war)")]
    [Range(0, 10)]
    [Tooltip("Tiles its owner sees around each of its tiles when fog of war is on.")]
    public int visionRange = 1;

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
    [Tooltip("Locomotion types this building services while standing on it (what it gives: below)")]
    public List<Locomotion> services = new List<Locomotion>();
    [Tooltip("Strength a serviced unit heals each turn (0: none). Capped at full strength.")]
    [Range(0, 6)]
    public int healPerTurn = 1;
    [Tooltip("A serviced unit's ammunition is filled each turn.")]
    public bool rearm = true;
    [Tooltip("A serviced unit's fuel (and an air tanker's refuelling stock) is filled each turn.")]
    public bool refuel = true;
    [Tooltip("A serviced unit's supplies (rations: what it starves without) are filled each turn.")]
    public bool resupply = true;
    [Tooltip("The heal is on top of the supply network's (a unit in supply on it heals 1 + healPerTurn); off: whichever is more.")]
    public bool healStacksWithSupply = false;

    [Header("Capture")]
    [Tooltip("Owner's points when owned; the capturing side's progress when neutral")]
    [SerializeField, FormerlySerializedAs("capturePoints")] int _capturePoints;
    public int capturePoints {
        get { BoardView v = BoardView.current; return v != null && v.Has(this) ? v.PointsOf(this) : _capturePoints; }
        set { BoardView v = BoardView.current; if (v != null && v.Has(this)) v.SetPoints(this, value); else _capturePoints = value; }
    }
    [Tooltip("Whose progress capturePoints is (toward taking it from its owner, or from nobody)")]
    [SerializeField, FormerlySerializedAs("capturingPlayer")] Player _capturingPlayer;
    public Player capturingPlayer {
        get { BoardView v = BoardView.current; return v != null && v.Has(this) ? v.CapturingOf(this) : _capturingPlayer; }
        set { BoardView v = BoardView.current; if (v != null && v.Has(this)) v.SetCapturing(this, value); else _capturingPlayer = value; }
    }

    [Header("Condition")]
    [Tooltip("Hits it takes before it is destroyed: every attack on a unit standing on one of its tiles is one. 0: never damaged.")]
    public int health = 10;
    [Tooltip("Health it has lost. At its health it is destroyed: it stops working (no income, supply, production, service or sight) until repaired.")]
    [FormerlySerializedAs("damageLevel")] public int damage;
    [Tooltip("Its main sprite as it wears down, in order: the health is split into this many + 1 equal parts, the first (full health) showing its own sprite.")]
    public Sprite[] damageStates;
    [Tooltip("Its main sprite once destroyed. Empty: the last damage picture.")]
    public Sprite destroyedSprite;
    Sprite intactSprite;

    /// <summary>A building was destroyed (its health ran out): the building. For logs.</summary>
    public static event Action<Building> BuildingDestroyed;

    /// <summary>Destroyed: its health has run out (one with no health never is).</summary>
    public bool Destroyed { get { return health > 0 && damage >= health; } }

    /// <summary>Working: not destroyed. A destroyed building gives no income, supply, production, service or sight.</summary>
    public bool Functioning { get { return !Destroyed; } }

    /// <summary>
    /// <paramref name="amount"/> damage (a fight on one of its tiles: HexTile.Damage). Its sprite
    /// steps through its damage pictures; at 0 health it is destroyed and stops working.
    /// </summary>
    public void Damage(int amount) {
        if (amount <= 0 || health <= 0) return;
        bool was = Destroyed;
        damage = Mathf.Min(health, damage + amount);
        RefreshCondition();
        if (Destroyed && !was) {
            owner = null;
            Changed();
            if (BuildingDestroyed != null) BuildingDestroyed(this);
        }
    }

    /// <summary>Repair: take back up to <paramref name="amount"/> damage (a destroyed building works again as soon as it has any health).</summary>
    public void Repair(int amount) {
        if (amount <= 0) return;
        bool was = Destroyed;
        damage = Mathf.Max(0, damage - amount);
        RefreshCondition();
        if (was && !Destroyed) Changed();
    }

    /// <summary>Supply, sight and what everyone can do changed with it.</summary>
    void Changed() {
        if (GameController.gc != null && GameController.gc.players != null)
            foreach (Player p in GameController.gc.players) if (p != null) p.Resupply();
        FogOfWar.Changed();
    }

    /// <summary>The picture for the health it has left: its own, a damage picture, or the destroyed one.</summary>
    public void RefreshCondition() {
        if (sprite == null) return;
        if (intactSprite == null) intactSprite = sprite.sprite;
        int states = damageStates != null ? damageStates.Length : 0;
        if (Destroyed) sprite.sprite = destroyedSprite != null ? destroyedSprite : states > 0 ? damageStates[states - 1] : intactSprite;
        else {
            int stage = TerrainData.Stage(health, damage, states);
            sprite.sprite = stage > 0 ? damageStates[stage - 1] : intactSprite;
        }
    }

    [Header("Production")]
    public List<Unit> buildable;

    public static readonly List<Building> all = new List<Building>();

    /// <summary>A building changed hands (or became neutral): building, previous owner.</summary>
    public static event Action<Building, Player> OwnerChanged;
    /// <summary>Capture points moved: building, player capturing, points before, points after.</summary>
    public static event Action<Building, Player, int, int> Captured;

    void Awake() {
        if (!all.Contains(this)) all.Add(this);
    }

    /// <summary>Destroyed (removed in the editor, deleted in the Hierarchy): off every list and off its tiles.</summary>
    void OnDestroy() {
        all.Remove(this);
        if (BuildingController.bc != null) BuildingController.bc.buildings.Remove(this);
        foreach (KeyValuePair<HexTile, TerrainData> kv in underlying) if (kv.Key != null && kv.Key.building == this) kv.Key.building = null;
    }

    void Start() {
        if (sprite != null && intactSprite == null) intactSprite = sprite.sprite;
        RefreshCondition();
    }

    public bool IsHQ { get { return type == BuildingType.HQ; } }

    public bool CanProduce { get { return Functioning && buildable != null && buildable.Count > 0; } }

    /// <summary>The units this building offers <paramref name="p"/>: its list mapped through p's faction (Faction.Offer), whatever purchases allow.</summary>
    public List<Unit> Offered(Player p) { return Faction.Offer(p != null ? p.faction : null, type, buildable); }

    /// <summary>
    /// Can this building build <paramref name="u"/> now: offered to its owner (its list, through the owner's faction), and purchases
    /// allow it (rules and the scenario's allowed units; a faction's version of a unit is allowed where the unit it replaces is)?
    /// </summary>
    public bool CanBuild(Unit u) {
        if (u == null || !CanProduce || !Offered(owner).Contains(u)) return false;
        Faction f = owner != null ? owner.faction : null;
        return GameController.MayPurchase(u) || (f != null && f.BaseOf(u) != u && GameController.MayPurchase(f.BaseOf(u)));
    }

    /// <summary>The units this building may build now.</summary>
    public List<Unit> Purchasable() {
        List<Unit> list = new List<Unit>();
        if (CanProduce) foreach (Unit u in Offered(owner)) if (CanBuild(u)) list.Add(u);
        return list;
    }

    /// <summary>Points it takes to take it (and an owner's full hold).</summary>
    public int CaptureThreshold { get { return 10; } }

    /// <summary>
    /// Capture progress as the screen shows it (under fog of war, as the viewer last saw it), if
    /// there is any: someone taking it — "Capturing", "Blue 6/10", Blue (from its owner or from
    /// nobody). False when there is none, or it cannot be captured.
    /// </summary>
    public bool ShownCaptureProgress(out string what, out string value, out Player whose) {
        what = null; value = null; whose = null;
        if (!Capturable) return false;
        Player capturing; int points;
        FogOfWar.ShownCapture(this, out capturing, out points);
        if (capturing == null || points <= 0) return false;
        what = "Capturing"; value = capturing.personal_name + " " + points + "/" + CaptureThreshold; whose = capturing;
        return true;
    }

    /// <summary>
    /// 0..1: how far up its pole the flag stands. Held: full, coming down as someone takes it; neutral: the
    /// capturer's progress (CaptureFlag shows it as the viewer last saw it).
    /// </summary>
    public static float FlagHeightOf(Player shownOwner, Player capturing, int points, int threshold) {
        float p = capturing != null ? Mathf.Clamp01(points / (float)Mathf.Max(1, threshold)) : 0f;
        return shownOwner != null ? 1f - p : p;
    }


    /// <summary>Only infantry take ground. Vehicles can sit on a building but not flip it.</summary>
    public static bool CanCapture(Unit u) {
        return u != null
            && u.Strength > 0
            && u.Hub == null
            && u.locomotion == Locomotion.Boots;
    }

    /// <summary>
    /// Would <paramref name="u"/> ending its turn on <paramref name="t"/> work toward taking a
    /// building: its tile, not ours, capturable under the rules,
    /// and <paramref name="u"/> a unit that captures.
    /// </summary>
    public static bool WouldCapture(Unit u, HexTile t) {
        if (u == null || t == null || t.building == null || u.attackedThisTurn) return false;
        Building b = t.building;
        return b.Capturable && CanCapture(u) && (b.owner != u.owner || (b.capturingPlayer != null && b.capturingPlayer != u.owner && b.capturePoints > 0));
    }

    /// <summary>
    /// Can this building change hands under the current rules? Not while destroyed (a ruin is nothing to
    /// hold until it is repaired: its points stay as they were), and under attrition not the HQ.
    /// </summary>
    public bool Capturable {
        get { return !Destroyed && !(IsHQ && GameController.gc != null && GameController.gc.rules == RuleSet.Attrition); }
    }

    // ------------------------------------------------------------------ placement

    /// <summary>
    /// Stand on <paramref name="anchor"/>: the tile takes the building's ground (its old terrain is
    /// kept, for saving and for removal). The ground can be painted over in the editor afterwards; a
    /// save keeps what is shown, and the terrain under it.
    /// </summary>
    public void Occupy(HexTile anchor) {
        Release();
        tile = anchor;
        if (anchor == null) return;
        anchor.building = this;
        // Loaded from a save that kept the ground as shown (painted in the editor, say): keep it.
        TerrainData under = anchor.pendingUnder; anchor.pendingUnder = null;
        underlying[anchor] = under ?? anchor.terrain;
        if (under == null && ground != null) SetGround(anchor, ground);
        // (Points are a capturer's progress: none without one.)
        if (capturingPlayer == null) capturePoints = 0;
    }

    /// <summary>Give the tile its own terrain back.</summary>
    public void Release() {
        foreach (KeyValuePair<HexTile, TerrainData> kv in underlying) {
            if (kv.Key == null) continue;
            if (kv.Key.building == this) kv.Key.building = null;
            if (kv.Value != null && kv.Key.terrain != kv.Value) SetGround(kv.Key, kv.Value);
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

    /// <summary>The side that captured here this turn (CaptureBy), so a unit adds its strength once; cleared as the turn ends.</summary>
    [NonSerialized] public Player capturedThisTurnBy;

    /// <summary>
    /// <paramref name="u"/> ended its action here (Unit.FinishActive): it captures now if it is infantry of a side
    /// that has not captured here this turn, and did not attack this turn. The real board only (not an AI's copy).
    /// </summary>
    public static void CaptureBy(Unit u) {
        if (u == null || u.tile == null || u.tile.building == null || BoardView.current != null) return;
        if (u.trasnporter != null && u.trasnporter != u) return;
        Building b = u.tile.building;
        if (b.tile != u.tile || !b.Capturable || !CanCapture(u) || u.attackedThisTurn || b.capturedThisTurnBy == u.owner) return;
        b.capturedThisTurnBy = u.owner;
        b.Apply(u.owner, u.Strength);
    }

    /// <summary>
    /// End of <paramref name="side"/>'s turn (GameController, before the turn passes): its infantry standing on a
    /// building it has not captured this turn (it never acted, or got there without ending an action there) captures
    /// now; progress <paramref name="side"/> made somewhere it did not capture this turn decays.
    /// </summary>
    public static void ResolveCaptures(Player side) {
        if (side == null) return;
        List<Building> snapshot = new List<Building>(all);
        foreach (Building b in snapshot) if (b != null) b.ResolveCapture(side);
        foreach (Building b in snapshot) if (b != null) b.capturedThisTurnBy = null;
    }

    void ResolveCapture(Player side) {
        if (!Capturable || tile == null) return;
        if (capturedThisTurnBy == side) return;
        Unit u = tile.occupier;
        if (u != null && u.owner == side && CanCapture(u) && !u.attackedThisTurn) {
            capturedThisTurnBy = side;
            Apply(side, u.Strength);
            return;
        }
        // Progress fades when the side making it has not kept at it.
        if (capturingPlayer == side && capturePoints > 0) {
            int before = capturePoints;
            capturePoints = Mathf.Max(0, capturePoints - 2);
            if (capturePoints == 0) capturingPlayer = null;
            if (Captured != null) Captured(this, side, before, capturePoints);
        }
    }

    /// <summary>
    /// The capture rule, on any state: <paramref name="side"/> captures with <paramref name="strength"/> a building
    /// held by <paramref name="owner"/> (null: nobody) with <paramref name="capturing"/>'s <paramref name="points"/>.
    /// The owner's own infantry brings a capturer's progress down; anyone else's adds to its own progress, from 0
    /// if the progress there was another side's, and at <paramref name="threshold"/> takes it.
    /// </summary>
    public static void CaptureStep(int threshold, Player owner, Player capturing, int points, Player side, int strength,
                                   out Player ownerAfter, out Player capturingAfter, out int pointsAfter) {
        ownerAfter = owner; capturingAfter = capturing; pointsAfter = capturing != null ? Mathf.Max(0, points) : 0;
        if (side == null || strength <= 0) return;
        if (owner == side) {
            pointsAfter = Mathf.Max(0, pointsAfter - strength);
            if (pointsAfter == 0) capturingAfter = null;
            return;
        }
        if (capturingAfter != side) pointsAfter = 0;
        capturingAfter = side;
        pointsAfter += strength;
        if (pointsAfter >= threshold) { ownerAfter = side; capturingAfter = null; pointsAfter = 0; }
    }

    void Apply(Player side, int strength) {
        int before = capturePoints;
        Player prevOwner = owner;
        Player o, c; int pts;
        CaptureStep(CaptureThreshold, owner, capturingPlayer, capturePoints, side, strength, out o, out c, out pts);
        owner = o; capturingPlayer = c; capturePoints = pts;

        if (capturePoints != before || owner != prevOwner) {
            if (Captured != null) Captured(this, side, before, capturePoints);
        }
        if (owner != prevOwner) {
            FogOfWar.Changed();
            if (OwnerChanged != null) OwnerChanged(this, prevOwner);
            if (IsHQ && prevOwner != null && GameController.gc != null) GameController.gc.OnHQCaptured(this, prevOwner);
        }
    }

    /// <summary>
    /// How far <paramref name="side"/> holds a building in that state, in points from -threshold to +threshold:
    /// held by it, threshold less a capturer's progress; held by another, minus (threshold less our progress);
    /// neutral, our progress (+) or another's (-).
    /// </summary>
    public static int Signed(int threshold, Player side, Player owner, Player capturing, int points) {
        int p = capturing != null ? Mathf.Clamp(points, 0, threshold) : 0;
        if (owner == side) return threshold - p;
        if (owner != null) return -(threshold - (capturing == side ? p : 0));
        return capturing == side ? p : -p;
    }

    /// <summary>How far <paramref name="side"/> holds it now (Signed).</summary>
    public int SignedFor(Player side) { return Signed(CaptureThreshold, side, owner, capturingPlayer, capturePoints); }

    /// <summary>
    /// What a capture by <paramref name="side"/> with <paramref name="strength"/> would leave: the owner afterwards
    /// and how far <paramref name="side"/> then holds it (Signed). The same rule as CaptureBy, for the AI to forecast
    /// with. (<paramref name="opposed"/>: kept for callers; one tile holds one unit, so nothing contests a capture.)
    /// </summary>
    public int ForecastPoints(Player side, int strength, bool opposed, out Player ownerAfter) {
        ownerAfter = owner;
        if (!Capturable || strength <= 0) return SignedFor(side);
        Player c; int pts;
        CaptureStep(CaptureThreshold, owner, capturingPlayer, capturePoints, side, strength, out ownerAfter, out c, out pts);
        return Signed(CaptureThreshold, side, ownerAfter, c, pts);
    }


    // ------------------------------------------------------------------ supply and service

    /// <summary>HQs never run dry; any other supply building gives while its store lasts (or while connected).</summary>
    public bool HasSupplyToGive {
        get { return producesSupply && Functioning && (IsHQ || supplies > 0); }
    }

    public void SpendSupply(int amount) {
        if (IsHQ) return;
        supplies = Mathf.Max(0, supplies - amount);
    }

    public void Replenish(int amount) {
        supplies = Mathf.Min(suppliesMax, supplies + amount);
    }

    public bool Services(Unit u) {
        return u != null && Functioning && services != null && services.Contains(u.locomotion);
    }

    /// <summary>The owner's building, if any, that services <paramref name="u"/> where it stands.</summary>
    public static Building ServicingUnder(Unit u) {
        if (u == null || u.tile == null) return null;
        Building b = u.tile.building;
        return b != null && b.owner == u.owner && b.Services(u) ? b : null;
    }
}
