using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class Unit : MonoBehaviour
{
    [Header("Render Fields")]
    [Tooltip("Sprites for Object")]
    public SpriteRenderer head;
    public SpriteRenderer upper;
    public SpriteRenderer weapon_sprite;
    public SpriteRenderer lower;

    public GameObject weaponImpact;
    public GameObject deathExplosion;

    public Text textArea;

    public Animator animator;

    public int cost;

    [Header("Personal Variables")]
    public string personal_name;
    [Tooltip("Can this unit act this turn")]
    public bool active;
    [Tooltip("Which player owns the vehicle")]
    public Player owner;
    [Range(0, 6)]
    [Tooltip("How much health the unit has")]
    public int Strength;
    [Range(0, 3)]
    [Tooltip("Veteran units are stronger")]
    public int Veterancy;
    [Tooltip("Where the unit stands")]
    public HexTile tile;
    [Tooltip("Ground unit able to move into and through water tiles that border land")]
    public bool amphibious;
    [Tooltip("Air tanker: refuels friendly air units that start their turn beside it, one refuelStock for each")]
    public bool airTanker;
    [Tooltip("Air tanker: refuels it can give before it must go back to base")]
    public int refuelStock;
    public int refuelStockMax;
    /// <summary>Fired this turn: an infantry unit that attacked does not capture the building it stands on.</summary>
    [System.NonSerialized] public bool attackedThisTurn;

    [Tooltip("((Firepower+Veterancy) / (Evasion - Targeting)™ / (Armor - Pierce)™ - (Concealment of Hex.Cover)) * Unit_Strength / Defender_Strength")]
    [Header("Combat Variables")]
    [Range(0, 6)]
    public int Firepower;
    [Range(0, 6)]
    public int Evasion;
    [Range(0, 6)]
    public int Targeting;
    [Range(0, 6)]
    public int Armor;
    [Range(0, 6)]
    public int Piercing;
    [Range(0, 6)]
    public int Concealment;
    [Range(1, 6)]
    [Tooltip("Ammo consumed per shot")]
    public int ammoPerShot = 1;
    [Tooltip("If False unit has to move OR attack")]
    public bool moveAndAttack;
    [Range(0, 10)]
    [Tooltip("Range in which unit can attack")]
    public int AttackRange;
    public int MinRange;
    public bool retaliates = true;


    [Header("Movement Variables")]
    public Sprite move_display;
    [Tooltip("Locomotive category: drives road bonuses, fuel burn, airfield needs, and AI role inference")]
    public Locomotion locomotion = Locomotion.Boots;
    [Range(0, 10)]
    [Tooltip("How far to move under ideal circumstances")]
    public int moveRange;
    [Range(0, 5)]
    [Tooltip("How much to subtract from rough terrain Wheels - 0, Treads - 2, Feet - 3, Flight - 5")]
    public int terrainScramble;
    [Range(0, 10)]
    public int minFuelPerTurn;
    [Range(0, 3)]
    public int minFuelPerTile;
    [Range(-2, 3)]
    public int elevationMin;
    [Range(-2, 3)]
    public int elevationMax;

    [Tooltip("Just for animation - for movement edit moveRange")]
    public int moveSpeed = 10;
    [Tooltip("How much health the unit appears to have (for animations so the unit doesn't die before the shot is fired)")]
    public int StrengthDisplay;

    internal void Delete() {
        DieActual();
        Destroy(gameObject);
    }

    public List<HexTile> path;//path for following when moving

    [Header("Supply Variables")]
    public int supplies;
    public int suppliesMax;
    public int fuel;
    public int fuelMax;
    public int ammo;
    public int ammoMax;

    [Header("Transport Variables")]
    [Tooltip("Room aboard, in slots: the weights of what it carries add up to at most this. 0: not a transport.")]
    public int transportSlots;
    [Tooltip("Not used: capacity is transportSlots, counted in weight.")]
    public int maxWeight;
    [Tooltip("Slots this unit takes aboard a transport (a grunt 1, supplies 2).")]
    public int weight;
    [Tooltip("The locomotion types this transport carries (an APC: Boots). Empty: Boots only.")]
    public List<Locomotion> carries = new List<Locomotion>();

    public List<Unit> transported;
    public Unit trasnporter;
    [Tooltip("Where carried units sit on this transport, one per slot (the transport itself if none)")]
    public List<Transform> cargoSlots = new List<Transform>();
    [Tooltip("Scale of a carried unit's sprites while aboard")]
    public float cargoScale = 0.45f;

    // Boarding animation state: the unit's own scale, and whether it is still settling.
    Vector3 homeScale = Vector3.one;
    bool scaleKnown;
    UnityEngine.Rendering.SortingGroup cargoGroup;

    [Header("AI Variables")]
    public int threatValue;

    [Header("Animation Variables")]
    public bool attacking;
    public Unit target;

    // Start is called before the first frame update
    void Start()
    {
        /*
        animator.ResetTrigger("Fire!");
        animator.ResetTrigger("Die");
        */
        animator.SetBool("Started", true);
        StrengthDisplay = Strength;
    }

    private void Awake() {

    }
    float deathRattleTimer = 10;

    // Update is called once per frame
    void Update()
    {
        // Boarding: walk the path to the transport at full size; only the last step, onto the
        // transport's tile, is taken shrinking into the slot.
        if (trasnporter != null && boardingWalk.Count > 0) {
            HexTile next = boardingWalk[0];
            if (boardingWalk.Count == 1 || next == null) { boardingWalk.Clear(); SettleAboard(trasnporter); }
            else {
                if (animator != null) animator.SetBool("Moving", true);
                Vector3 to = next.unitPlacement != null ? next.unitPlacement.position : next.transform.position;
                transform.rotation = to.x < transform.position.x ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;
                if (Vector2.Distance(transform.position, to) > .1f) transform.position = Vector2.MoveTowards(transform.position, to, Time.deltaTime * moveSpeed);
                else boardingWalk.RemoveAt(0);
                return;
            }
        }
        // Aboard: shrink into the transport's slot; nothing else moves this sprite.
        if (trasnporter != null) {
            transform.localPosition = Vector3.MoveTowards(transform.localPosition, Vector3.zero, Time.deltaTime * moveSpeed);
            transform.localScale = Vector3.MoveTowards(transform.localScale, homeScale * cargoScale, Time.deltaTime * 2f);
            return;
        }
        // Just unloaded: grow back while walking off.
        if (scaleKnown && transform.localScale != homeScale) transform.localScale = Vector3.MoveTowards(transform.localScale, homeScale, Time.deltaTime * 2f);
        if(Strength <=0) {
            if( deathRattleTimer <= 0) {
                Die();
            } else {
                deathRattleTimer -= Time.deltaTime;
            }
        }
        // Walking onto (or through) a tile a retreating unit's sprite has not left yet: wait for it
        // to go (at most a few seconds), so the two are never drawn on one tile.
        if (path.Count > 0 && SlidingOff(path[0]) != null) {
            if (pathWaitSince < 0f) pathWaitSince = Time.time;
            if (Time.time - pathWaitSince < 3f) { animator.SetBool("Moving", false); return; }
        } else pathWaitSince = -1f;
        if (path.Count > 0) {
            animator.SetBool("Moving", true);
            HexTile ht = path[0];
            if (ht.unitPlacement.transform.position.x < transform.position.x) {
                transform.rotation = Quaternion.Euler(0, 180, 0);
            } else transform.rotation = Quaternion.identity;

            if(Vector2.Distance(transform.position, ht.unitPlacement.position) > .1f) {
                transform.position = Vector2.MoveTowards(transform.position, ht.unitPlacement.position, Time.deltaTime * moveSpeed);
            } else {
                path.RemoveAt(0);
            }
        } else {
            animator.SetBool("Moving", false);
            // A retreating unit's sprite waits on its old tile until the shot that drove it
            // back has landed (AttackResolve sets StrengthDisplay on impact), then slides.
            if (retreatHold > 0f) {
                // Once the shot has landed, a moment for the hit to show, then slide. (Waiting on the
                // animator as well never ended early: a looping idle state is always "playing", so
                // every retreat held the full three seconds, and the AI waited on it.)
                if (StrengthDisplay == Strength) retreatHold = Mathf.Min(retreatHold, retreatPause);
                retreatHold -= Time.deltaTime;
            }
            // Still sliding to an earlier retreat tile when this one was ordered: finish that slide
            // first, so the shot that waits on it (see the ambush/attack check below) has a sprite
            // to aim at, and the new retreat starts from where the unit really was.
            if (slideFirst.HasValue) {
                transform.position = Vector2.MoveTowards(transform.position, slideFirst.Value, Time.deltaTime * moveSpeed);
                if (Vector2.Distance(transform.position, slideFirst.Value) <= 0.05f) slideFirst = null;
            }
            else if (refuelHopTo != null && tile && retreatHold <= 0f) RefuelHopStep();
            else if(tile && retreatHold <= 0f)
                transform.position = Vector2.MoveTowards(transform.position, tile.unitPlacement.position, Time.deltaTime * moveSpeed);
            else {

            }

            // An ambusher waits for its attacker to finish moving in before it fires, and any
            // shot waits for a target still sliding back from an earlier retreat to get there
            // (at most a few seconds), so the hit is never drawn on the tile it has left.
            if (attacking && (target == null || (target.path.Count == 0 && !TargetStillSliding()))) {
                Fire(target,true);
            }
        }
    }

    /// <summary>
    /// Is this unit still showing something that already happened in the rules: walking, sliding
    /// back after a retreat, boarding, a shot on its way to it or from it, its sprite not yet on
    /// its tile? The AI waits for these before its next action, so what is on screen matches the
    /// board (a unit is never shot where its sprite no longer is).
    /// </summary>
    public bool Animating {
        get {
            if (Strength <= 0 || tile == null) return false;
            if (path.Count > 0 || attacking || retreatHold > 0f || boardingWalk.Count > 0 || refuelHopTo != null) return true;
            // Its shot has not landed yet (at most a couple of seconds: a fire animation without the
            // event that lands the shot must not hold the AI up for every action after it).
            if (firing && Time.time - firedAt < 2.5f) return true;
            if (trasnporter == null && tile.unitPlacement != null && Vector2.Distance(transform.position, tile.unitPlacement.position) > 0.15f) return true;
            return false;
        }
    }

    /// <summary>Any unit on the board still animating (see <see cref="Animating"/>).</summary>
    public static bool AnyAnimating() {
        if (UnitController.uc == null) return false;
        foreach (Unit u in UnitController.uc.units) if (u != null && u.Animating) return true;
        return false;
    }

    bool AnimatorIsPlaying() {
        return animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1 != 0;
    }

    internal bool CanEnter(HexTile n) {
        if (n == null || n.terrain == null) return false;
        int e = n.terrain.elevation;
        if (e >= elevationMin && e <= elevationMax) return true;
        // Amphibious: into and through water that borders land.
        return amphibious && e < 0 && BordersLand(n);
	}

    /// <summary>A tile with land (elevation 0 or more: bridges included) beside it.</summary>
    public static bool BordersLand(HexTile n) {
        if (n == null || n.neighbors == null) return false;
        for (int i = 0; i < n.neighbors.Count; i++) {
            HexTile m = n.neighbors[i];
            if (m != null && m.terrain != null && m.terrain.elevation >= 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Boarding (Transporting.Load calls this): the unit's sprites go into the transport — parented
    /// to its next cargo slot, shrunk to cargoScale (Update animates it), drawn in the "Transported"
    /// sorting layer when the project has one (else just above the transport). A supply unit is
    /// not shown: the transport's own animator gets CarryingSupply (its hand-made supply form).
    /// </summary>
    public void OnBoarded(Unit transport) {
        if (!scaleKnown) { homeScale = transform.localScale; scaleKnown = true; }
        // Its own UI goes: strength, veterancy and warnings are not shown for a unit aboard.
        hiddenUI.Clear();
        foreach (Transform t in GetComponentsInChildren<Transform>(true)) {
            if (t == transform || !t.gameObject.activeSelf) continue;
            bool ui = t.GetComponent<Canvas>() != null || t.name == "HealthCircles" || t.name == "Veterancy" || t.name == "Canvas";
            foreach (MonoBehaviour m in t.GetComponents<MonoBehaviour>())
                if (m != null && (m.GetType().Name == "UnitStatusIcon" || m.GetType().Name == "HealthDot" || m.GetType().Name == "VeterancyChevron")) ui = true;
            if (ui) {
                t.gameObject.SetActive(false);
                hiddenUI.Add(t.gameObject);
            }
        }
        // The walk there (if it moved to board) is played out first, at full size.
        boardingWalk.Clear();
        if (path != null && path.Count > 0) boardingWalk.AddRange(path);
        boardingWalk.Add(transport.tile);
        if (path != null) path.Clear();
    }

    /// <summary>Tiles still to walk before stepping into the transport (animation only).</summary>
    readonly List<HexTile> boardingWalk = new List<HexTile>();
    readonly List<GameObject> hiddenUI = new List<GameObject>();

    /// <summary>Skip the boarding walk: straight into the slot (no frames to play it, or not wanted).</summary>
    public void FinishBoarding() {
        if (trasnporter == null) return;
        boardingWalk.Clear();
        SettleAboard(trasnporter);
    }

    /// <summary>The last step of boarding: into the transport's slot and its sorting group.</summary>
    void SettleAboard(Unit transport) {
        if (animator != null) animator.SetBool("Moving", false);
        int slot = Mathf.Max(0, transport.transported.IndexOf(this));
        Transform at = transport.cargoSlots != null && slot < transport.cargoSlots.Count && transport.cargoSlots[slot] != null
                     ? transport.cargoSlots[slot] : transport.transform;
        transform.SetParent(at, true);
        transform.rotation = at.rotation;
        if (cargoGroup == null) cargoGroup = gameObject.AddComponent<UnityEngine.Rendering.SortingGroup>();
        int layer = SortingLayer.NameToID("Transported");
        SpriteRenderer top = null;
        foreach (SpriteRenderer r in transport.GetComponentsInChildren<SpriteRenderer>()) if (top == null || r.sortingOrder > top.sortingOrder) top = r;
        if (layer != 0 && SortingLayer.IsValid(layer)) { cargoGroup.sortingLayerID = layer; cargoGroup.sortingOrder = slot; }
        else if (top != null) { cargoGroup.sortingLayerID = top.sortingLayerID; cargoGroup.sortingOrder = top.sortingOrder + 1 + slot; }
        // Supplies: hidden only when the transport has its own carrying-supply animation to show
        // instead (a "CarryingSupply" parameter on its animator); otherwise shown like any cargo.
        if (GetComponent<SupplyPoint>() != null && transport.HasAnimatorParameter("CarryingSupply")) {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            transport.SetAnimatorBool("CarryingSupply", true);
        }
    }

    public bool HasAnimatorParameter(string name) {
        if (animator == null) return false;
        foreach (AnimatorControllerParameter p in animator.parameters) if (p.name == name) return true;
        return false;
    }

    /// <summary>Unloading (Transporting.Unload calls this): everything OnBoarded did, undone; the unit walks to its tile.</summary>
    public void OnUnloaded(Unit transport) {
        boardingWalk.Clear();
        foreach (GameObject g in hiddenUI) if (g != null) g.SetActive(true);
        hiddenUI.Clear();
        transform.SetParent(null, true);
        transform.rotation = Quaternion.identity;
        if (cargoGroup != null) { Destroy(cargoGroup); cargoGroup = null; }
        if (GetComponent<SupplyPoint>() != null) {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            bool still = false;
            foreach (Unit c in transport.transported) if (c != null && c != this && c.GetComponent<SupplyPoint>() != null) still = true;
            transport.SetAnimatorBool("CarryingSupply", still);
        }
    }

    /// <summary>Set an animator bool if this unit's animator has it (art can add states without code).</summary>
    public void SetAnimatorBool(string name, bool on) {
        if (animator == null) return;
        foreach (AnimatorControllerParameter p in animator.parameters) if (p.name == name) { animator.SetBool(name, on); return; }
    }

    /// <summary>A unit (aircraft or ship) ran out of fuel and was ditched.</summary>
    public static event System.Action<Unit> Ditched;

    /// <summary>
    /// Out of fuel: the unit is lost. Plays the "Ditch" animation when the unit's animator has one
    /// (its last frame should call DeathResolve, as "Die" does), the death animation otherwise.
    /// </summary>
    public void Ditch() {
        if (dead) return;
        if (Ditched != null) Ditched(this);
        DieActual();
        if (animator != null) {
            bool hasDitch = false;
            foreach (AnimatorControllerParameter p in animator.parameters) if (p.name == "Ditch") hasDitch = true;
            animator.SetTrigger(hasDitch ? "Ditch" : "Die");
        }
    }

    internal void CompleteMove(HexTile hexTile, bool thenAttacks = false) {
        tile.parent = null;
        List<HexTile> path = hexTile.buildpath();

        int tilesEntered = Mathf.Max(0, path.Count - 1);
        // Movement burns fuel per tile entered; air units also burn minFuelPerTurn every turn
        // they are away from base (Player.StartTurn).
        if (fuelMax > 0) fuel = Mathf.Max(0, fuel - tilesEntered * minFuelPerTile);

        tile.occupier = null;
        tile = hexTile;
        tile.occupier = this;
        this.path = path;
        if (!thenAttacks) ServiceOnLanding();

        // Carried units ride along rather than being left behind on the old tile.
        SetCargoTile(hexTile);

        animator.SetBool("Moving", true);
        HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
    }

    /// <summary>How long the visual hop to an air tanker and back takes, in seconds.</summary>
    public const float RefuelHopSeconds = 1f;
    HexTile refuelHopTo;
    float refuelHopT;

    /// <summary>
    /// Show a refuel from <paramref name="tanker"/>: the sprite flies onto the tanker's tile and back
    /// to its own over RefuelHopSeconds. Visual only: the unit never leaves its tile.
    /// </summary>
    public void PlayRefuelHop(Unit tanker) {
        if (tanker == null || tanker.tile == null || tile == null) return;
        refuelHopTo = tanker.tile;
        refuelHopT = 0f;
    }

    void RefuelHopStep() {
        refuelHopT += Time.deltaTime;
        float k = refuelHopT / RefuelHopSeconds;
        Vector3 home = tile.unitPlacement.position, there = refuelHopTo.unitPlacement.position;
        if (k >= 1f) { refuelHopTo = null; transform.position = home; return; }
        // Out for the first half, back for the second; facing the way it is flying.
        bool outbound = k < 0.5f;
        float f = outbound ? k * 2f : (1f - k) * 2f;
        transform.position = Vector3.Lerp(home, there, f);
        bool faceLeft = outbound ? there.x < home.x : home.x < there.x;
        transform.rotation = faceLeft ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;
    }

    /// <summary>
    /// An aircraft or ship whose turn is a move (not a move and attack) that ends on one of our
    /// buildings that services it is refuelled, re-armed and restocked there and then, not at the
    /// start of the next turn: whatever happens before then (the building lost, a turn's burn), it
    /// does not ditch at the runway it reached. A move and attack gets nothing until the start of
    /// the next turn. Healing always waits for the start of the turn (Player.StartTurn).
    /// </summary>
    public void ServiceOnLanding() {
        if (tile == null || !LocomotionRules.NeedsBase(locomotion) || Building.ServicingUnder(this) == null) return;
        fuel = fuelMax;
        ammo = ammoMax;
        supplies = suppliesMax;
        refuelStock = refuelStockMax;
    }

    /// <summary>Everything aboard (and aboard that, and so on) is where this transport is.</summary>
    public void SetCargoTile(HexTile t) {
        for (int i = 0; i < transported.Count; i++) {
            if (transported[i] == null) continue;
            transported[i].tile = t;
            transported[i].SetCargoTile(t);
        }
    }

    /// <summary>Move without animation or tile-state churn. Used by the headless harness.</summary>
    internal void TeleportTo(HexTile hexTile, int tilesEntered, bool thenAttacks = false) {
        // Movement burns fuel per tile entered; air units also burn minFuelPerTurn every turn
        // they are away from base (Player.StartTurn).
        if (fuelMax > 0) fuel = Mathf.Max(0, fuel - tilesEntered * minFuelPerTile);
        if (tile != null && tile.occupier == this) tile.occupier = null;
        tile = hexTile;
        if (hexTile != null) hexTile.occupier = this;
        if (!thenAttacks) ServiceOnLanding();
        SetCargoTile(hexTile);
    }

    /// <summary>
    /// Whether this unit is capable of firing at all right now.
    ///
    /// Note this was previously the sole reader of <c>ammo</c> in the entire
    /// codebase, and nothing ever refilled it — so a prefab authored with ammo 0
    /// could silently never attack, with nothing surfacing why. Ammo is now spent
    /// in AttackActual and restored by Resupply, so the check means what it says.
    /// </summary>
    internal bool CanAttack(HexTile position) {
        if (Strength <= 0) return false;
        if (ammoMax > 0 && ammo <= 0) return false;
        if (Firepower <= 0) return false;
        return true;
    }

    internal bool CanAttack(Unit opposition) {
        if (opposition == null || opposition.tile == null) return false;
        if (!CanAttack(opposition.tile)) return false;
        return calculateDamage(this, opposition, opposition.tile, 0) > 0;
    }

    /// <summary>True once the unit has nothing left to fight with.</summary>
    public bool OutOfAmmo { get { return ammoMax > 0 && ammo <= 0; } }
    public bool OutOfFuel { get { return fuelMax > 0 && fuel <= 0; } }
    public bool OutOfSupplies { get { return suppliesMax > 0 && supplies <= 0; } }



    internal void updateThreat() {
        int valueGain = 0;
        List<HexTile> moves = tile.CheckMovementRange(this);
        if (moveAndAttack) {
            HashSet<HexTile> tiles = new HashSet<HexTile>();
            
            moves.ForEach(x => {
                List<HexTile> attacks = x.AllInAttackRange(this);
                attacks.ForEach(y => {
                    tiles.Add(y);
                });
            });
            tiles.ToList().ForEach(x => {
                x.threat[owner].firepower += Firepower * Strength / 6f;
                x.threat[owner].antiarmor += Firepower * Strength * Piercing / 36f;
                x.threat[owner].antiair += Firepower * Strength * Targeting / 36f;
                if (validTarget(x)) {
                    int attackDamage = Unit.calculateDamage(this, x.occupier, x, 0);
                    float attackValue = attackDamage * x.occupier.value();
                    valueGain = (int)Mathf.Max(valueGain, attackValue);
                }
            });
        } else {
            List<HexTile> attacks = tile.AllInAttackRange(this);
            attacks.ForEach(x => {
                x.threat[owner].firepower += Firepower * Strength / 6f;
                x.threat[owner].antiarmor += Firepower * Strength * Piercing / 36f;
                x.threat[owner].antiair += Firepower * Strength * Targeting/ 36f;
                if (validTarget(x)) {
                    int attackDamage = Unit.calculateDamage(this, x.occupier, x, 0);
                    float attackValue = attackDamage * x.occupier.value();
                    valueGain = (int)Mathf.Max(valueGain, attackValue);
                }
            });
        }

        threatValue = valueGain;
    }

    internal void FinishActive() {
        GameController.gc.activeUnit = null;
        active = false;
        GameController.RecalculateThreat();
        if (GetComponent<SupplyPoint>()) {
            owner.Resupply();
        }
        if (Finished != null) Finished(this);
    }





    /// <summary>
    /// Resolve an attack. All arithmetic lives in CombatMath so that the AI's
    /// prediction and this resolution cannot drift apart.
    /// </summary>
    /// <summary>Every attack, by anyone, after it resolves: attacker, defender, attacker's tile, defender's tile, outcome. For logs.</summary>
    public static event System.Action<Unit, Unit, HexTile, HexTile, CombatOutcome> Attacked;
    /// <summary>A unit finished its action (moved, attacked, or both). For logs.</summary>
    public static event System.Action<Unit> Finished;

    internal CombatOutcome OrderAttack(Unit defender) {
        // The defender's sprite is still sliding back from an earlier retreat: the shot waits until
        // it has arrived where the defender now is (the tile it stands on as this attack begins).
        waitForSlide = null;
        if (defender != null && defender.RetreatPlaying && defender.tile != null && defender.tile.unitPlacement != null) {
            waitForSlide = defender; waitForSlideTo = defender.tile.unitPlacement.position; waitForSlideSince = Time.time;
        }
        attacking = true;
        attackedThisTurn = true;
        target = defender;
        HexTile firedFrom = tile;

        // Capture before resolving: a kill nulls defender.tile and a retreat moves it,
        // and either way destruction belongs to the ground that was fought over.
        HexTile fought = defender.tile;
        CombatOutcome o = CombatMath.Resolve(this, defender, tile);

        if (ammoMax > 0) ammo = Mathf.Max(0, ammo - Mathf.Max(1, ammoPerShot));
        if (o.retaliationHappens && defender.ammoMax > 0) {
            defender.ammo = Mathf.Max(0, defender.ammo - Mathf.Max(1, defender.ammoPerShot));
        }

        // (A pinned defender's extra point is already in damageToDefender.)
        defender.Strength = Mathf.Max(0, defender.Strength - o.damageToDefender);
        Strength = Mathf.Max(0, Strength - o.damageToAttacker);

        // The animations play the exchange in the order it was resolved: in an ambush the
        // defender fires first and the attacker answers if it survived; otherwise the attacker
        // fires and the defender answers if it retaliates and survived.
        if (o.defenderFiresFirst) {
            attacking = false;
            defender.target = this;
            defender.attacking = true;
            defender.answeredBy = Strength > 0 && o.damageToDefender > 0 ? this : null;
            answeredBy = null;
        } else {
            answeredBy = o.retaliationHappens && o.damageToAttacker > 0 && defender.Strength > 0 ? defender : null;
        }

        if (o.defenderRetreats && !o.defenderDies) {
            if (o.defenderPinned) {
                // nowhere to go; the extra strength is already taken
            } else if (o.retreatTile != null) {
                // The board changes now, like every other result of combat: the defender is on
                // its retreat tile from this moment, for the rules, the AI and the next click.
                // Only the sprite waits — it slides over once the shot has landed (see Update),
                // so the impact is not drawn on empty ground. (The move used to wait for the
                // animation too, which left the board wrong in between: the AI planned against
                // units that had already gone, attacks could be ordered onto empty tiles, and a
                // retreat that never got applied would fire later, from wherever the unit had
                // moved to since, sending it across the map.)
                defender.RetreatTo(o.retreatTile);
            }
        }

        if (defender.Strength <= 0) defender.DieActual();
        if (Strength <= 0) DieActual();

        // Combat degrades the ground it is fought over.
        if (fought != null) fought.RegisterCombat();

        if (Attacked != null) Attacked(this, defender, firedFrom, fought, o);
        return o;
    }

    /// <summary>Kept for old callers: a retreat now happens at once (RetreatTo), so there is never one pending.</summary>
    [HideInInspector] public HexTile pendingRetreat;

    /// <summary>Seconds the sprite may still wait before sliding to a retreat tile. Animation only.</summary>
    float retreatHold;

    [Tooltip("Seconds a retreating unit's sprite waits after the hit lands before it slides back (animation only).")]
    public float retreatPause = 0.1f;

    /// <summary>Where the sprite was still sliding to (an earlier retreat) when a new retreat was ordered. Animation only.</summary>
    [System.NonSerialized] Vector3? slideFirst;

    /// <summary>The target this unit's shot waits for: it was sliding back from an earlier retreat when the attack was ordered.</summary>
    [System.NonSerialized] Unit waitForSlide;
    [System.NonSerialized] Vector3 waitForSlideTo;
    [System.NonSerialized] float waitForSlideSince;

    [System.NonSerialized] float pathWaitSince = -1f;

    /// <summary>A unit (not this one) whose sprite is still sliding back off <paramref name="t"/> after a retreat, if any.</summary>
    Unit SlidingOff(HexTile t) {
        if (t == null || UnitController.uc == null) return null;
        foreach (Unit u in UnitController.uc.units)
            if (u != null && u != this && u.retreatFrom == t && u.RetreatPlaying) return u;
        return null;
    }

    bool TargetStillSliding() {
        if (waitForSlide == null || waitForSlide != target || waitForSlide.Strength <= 0) { waitForSlide = null; return false; }
        if (Time.time - waitForSlideSince > 3f || Vector2.Distance(waitForSlide.transform.position, waitForSlideTo) <= 0.15f) { waitForSlide = null; return false; }
        return true;
    }

    /// <summary>
    /// Post-combat retreat: the unit is on <paramref name="to"/> immediately. Only the sprite
    /// waits for the incoming shot to land before sliding over (at most a few seconds).
    /// </summary>
    public void RetreatTo(HexTile to) {
        HexTile was = tile;
        // Hit again while still sliding back from the last retreat: that slide is finished first.
        if (RetreatPlaying && was != null && was.unitPlacement != null) slideFirst = was.unitPlacement.position;
        pendingRetreat = to;
        ApplyPendingRetreat();
        if (tile == to) { retreatHold = 3f; retreatFrom = was; retreatStarted = Time.time; }
    }

    /// <summary>While a retreat is still playing on screen: the tile the sprite is leaving (else null).</summary>
    [System.NonSerialized] public HexTile retreatFrom;
    [System.NonSerialized] float retreatStarted;

    /// <summary>
    /// Is a retreat still playing for this unit (the sprite has not yet slid onto its new tile)?
    /// At most a few seconds, whatever the animation does.
    /// </summary>
    public bool RetreatPlaying {
        get {
            if (retreatFrom == null) return false;
            if (Strength <= 0 || tile == null || Time.time - retreatStarted > 4f) { retreatFrom = null; return false; }
            if (retreatHold > 0f) return true;
            if (tile.unitPlacement != null && Vector2.Distance(transform.position, tile.unitPlacement.position) > 0.15f) return true;
            retreatFrom = null;
            return false;
        }
    }

    /// <summary>
    /// Perform a deferred post-combat retreat. Safe to call repeatedly; safe if the
    /// destination got taken in the meantime, in which case the unit simply holds
    /// (it has already paid for the retreat in damage terms).
    /// </summary>
    public void ApplyPendingRetreat() {
        HexTile to = pendingRetreat;
        pendingRetreat = null;
        if (to == null || Strength <= 0) return;
        if (to.occupier != null) return;
        if (!CanEnter(to)) return;

        if (tile != null && tile.occupier == this) tile.occupier = null;
        tile = to;
        to.occupier = this;
        SetCargoTile(to);
        // Empty path: Update() glides the sprite to the new tile at moveSpeed.
        path.Clear();
    }

    /// <summary>Direct damage application, used by scripted events and the harness.</summary>
    internal void AttackActual(Unit defender, bool useCover) {
        HexTile coverTile = useCover ? defender.tile : null;
        int damage = Mathf.Min(defender.Strength,
                               Unit.calculateDamage(this, defender, coverTile, 0));
        defender.Strength -= damage;
        if (defender.Strength <= 0) {
            defender.DieActual();
        }
    }

    private bool dead;
    private void DieActual() {
        if (dead) return;
        dead = true;
        SupplyPoint ownHub = GetComponent<SupplyPoint>();
        if (ownHub != null && ownHub.producer && owner != null) owner.hadHQ = true;
        Strength = 0;
        if (owner != null) owner.units.Remove(this);
        if (UnitController.uc != null) UnitController.uc.units.Remove(this);
        // Anything being carried goes down with the transport.
        for (int i = transported.Count - 1; i >= 0; i--) {
            if (transported[i] != null) transported[i].DieActual();
        }
        transported.Clear();
        if (trasnporter != null) { trasnporter.transported.Remove(this); trasnporter = null; }
        if (tile != null && tile.occupier == this) tile.occupier = null;
        tile = null;
        // Losing an HQ unit ends the game for its owner immediately, not at end of turn.
        if (GameController.gc != null) GameController.gc.CheckVictory();
        if (GetComponent<SupplyPoint>()) {
            owner.Resupply();
        }
    }

    /// <summary>
    /// What this unit is worth as a target: build cost, a veterancy premium, and a
    /// share of the damage it currently threatens. Positional value, not just material —
    /// a gun that dominates a crossing is worth more than its price tag. Supply value is
    /// added by the AI's own map (AIMap.SupplyImportance), not stored on the unit.
    /// </summary>
    internal int value() {
        float material = cost + Veterancy * cost * 0.2f;
        float positional = threatValue * 0.1f;
        return (int)(material / 6f + positional);
    }

    public bool shooter;

    internal void clearDisplay() {
        textArea.text = "";
    }


    //This starts the shooting process, but the animation controller determines when it is resolved.
    internal void Fire(Unit defender, bool retaliate) {
        target = defender;
        firing = defender != null;
        firedAt = Time.time;
        animator.SetTrigger("Fire!");
        attacking = false;
        if (target == null)
            return;
        shooter = retaliate;
        if (target.transform.position.x < transform.position.x) {
            transform.rotation = Quaternion.Euler(0, 180, 0);
        } else transform.rotation = Quaternion.identity;
        
    }

    /// <summary>Who fires back once this unit's shot lands (decided when the attack resolved). Animation only.</summary>
    [HideInInspector] public Unit answeredBy;

    /// <summary>Between Fire and the shot landing (AttackResolve): animation only.</summary>
    [System.NonSerialized] public bool firing;
    [System.NonSerialized] float firedAt;

    internal void AttackResolve(Unit defender, bool retaliate) {
        firing = false;
        if (!defender) return;
        GameObject.Instantiate(weaponImpact, defender.transform.position, defender.transform.rotation);
        defender.StrengthDisplay = defender.Strength;
        Unit answer = answeredBy;
        answeredBy = null;
        if (defender.Strength <= 0) {
            defender.Die();
        } else if (retaliate && answer == defender) {
            defender.Fire(this, false);
        }
        shooter = false;
    }
    //Just aniamtions
    private void Die() {
        animator.SetTrigger("Die");
        GameObject.Instantiate(deathExplosion, transform.position, transform.rotation);
    }

    public void DeathResolve() {
        Destroy(gameObject);
    }

    internal int displayAttackOdds(Unit attacker) {
        return displayAttackOdds(attacker, this.tile, 0);
    }

    internal static int calculateDamage(Unit attacker, Unit defender, HexTile tile, int firstStrike) {
        if (attacker.Strength - firstStrike <= 0)
            return 0;
        float cover = 0;
        float destructin = (attacker.Firepower + attacker.Veterancy);
        float penetration = Mathf.Max(1, defender.Armor - attacker.Piercing);
        float aquire = Mathf.Max(1, defender.Evasion - attacker.Targeting);
        float toughness = 6;
        if (tile != null && tile.terrain != null && !LocomotionRules.IsAirborne(defender.locomotion))
            cover = Math.Min(defender.Concealment, tile.EffectiveCover);
        return (int)Mathf.Max(1,Mathf.Floor(
            (destructin / penetration / aquire - cover)
            * (attacker.Strength - firstStrike) / toughness ));// (defender.Strength));
    }

    internal int displayAttackOdds(Unit attacker, HexTile tile, int firstStrike) {
        Unit defender = this;
        int damage = Unit.calculateDamage(attacker, defender, tile, firstStrike);
        damage = Mathf.Min(defender.Strength, damage);
        textArea.text = "-" + damage;
        return damage;
    }

    /// <summary>
    /// Attack preview: the exact outcome OrderAttack will produce if <paramref name="attacker"/>
    /// fires on this unit from <paramref name="from"/> — CombatMath.Resolve, the same function
    /// the attack and the AI use. Shows damage on both units; an ambush (this unit fires first)
    /// is marked "!", a pinned retreat is included in the number.
    /// </summary>
    internal CombatOutcome PreviewAttack(Unit attacker, HexTile from) {
        CombatOutcome o = CombatMath.Resolve(attacker, this, from);
        textArea.text = "-" + o.damageToDefender + (o.defenderDies ? " X" : o.defenderRetreats ? (o.defenderPinned ? " pin" : " ret") : "");
        if (o.retaliationHappens || o.damageToAttacker > 0)
            attacker.textArea.text = (o.defenderFiresFirst ? "!" : "") + "-" + o.damageToAttacker + (o.attackerDies ? " X" : "");
        return o;
    }

    internal bool validTarget(HexTile position) {
        return position.occupier != null && position.occupier.owner != owner && GameController.Targetable(position.occupier);
    }
}

public struct UnitData {
    public int x, y, z;
    public int Strength;
    public int Veterancy;
    public string personalName;
    public bool active;
    public long owner_id;
    public int supplies;
    public int fuel;
    public int ammo;
    public string gameobject_name;
    /// <summary>
    /// Index of the carrying unit, PLUS ONE. Zero means "not being transported".
    ///
    /// The offset is not decoration: JsonUtility cannot tell a missing field from a
    /// zero one, so a save written before this field existed deserialises as 0. With
    /// a raw index that reads as "carried by units[0]", and every unit in an old save
    /// would be loaded into the first unit on the roster.
    /// </summary>
    public int transporterIndexPlusOne;
    /// <summary>An air tanker's refuels left, plus one (zero: an older save, keep the prefab's).</summary>
    public int refuelStockPlusOne;
    public UnitData(Unit t) {
        transporterIndexPlusOne = 0;
        refuelStockPlusOne = t.refuelStock + 1;
        x = t.tile.x;
        y = t.tile.y;
        z = t.tile.z;
        Strength = t.Strength;
        Veterancy = t.Veterancy;
        personalName = t.personal_name;
        active = t.active;
        owner_id = t.owner.id;
        gameobject_name = t.gameObject.name;
        supplies = t.supplies;
        ammo = t.ammo;
        fuel = t.fuel;
        if (t.trasnporter != null && UnitController.uc != null) {
            int idx = UnitController.uc.units.IndexOf(t.trasnporter);
            if (idx >= 0) transporterIndexPlusOne = idx + 1;
        }
    }

    public Unit load() {
        Player player = null;
        UnitData that = this;
        GameController.gc.players.ForEach(pl => { if (pl.id == that.owner_id) player = pl; });
        HexTile tile = HexGenerator.hg.get(x, y, z);
        // TempHQ is retired: an HQ unit in an older save becomes the HQ building.
        if (gameobject_name != null && gameobject_name.Replace("(Clone)", "").Trim() == "TempHQ") {
            Building hq = LevelEditor.loadBuilding("HQ");
            if (hq != null && BuildingController.bc != null && tile != null && tile.building == null) BuildingController.bc.Place(player, hq, tile);
            return null;
        }
        Unit ut = LevelEditor.PlaceNewUnit(player, gameobject_name,tile);
        ut.Strength = Strength;
        ut.Veterancy = Veterancy;
        ut.personal_name = personalName;
        ut.active = active;
        ut.supplies = supplies;
        ut.fuel = fuel;
        ut.ammo = ammo;
        if (refuelStockPlusOne > 0) ut.refuelStock = refuelStockPlusOne - 1;
        return ut;
    }
}