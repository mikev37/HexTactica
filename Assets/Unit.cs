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
    public int transportSlots;
    public int maxWeight;
    public int weight;

    public List<Unit> transported;
    public Unit trasnporter;

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
        if(Strength <=0) {
            if( deathRattleTimer <= 0) {
                Die();
            } else {
                deathRattleTimer -= Time.deltaTime;
            }
        }
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
                if (StrengthDisplay == Strength && !AnimatorIsPlaying()) retreatHold = 0f;
                else retreatHold -= Time.deltaTime;
            }
            if(tile && retreatHold <= 0f)
                transform.position = Vector2.MoveTowards(transform.position, tile.unitPlacement.position, Time.deltaTime * moveSpeed);
            else {

            }

            // An ambusher waits for its attacker to finish moving in before it fires.
            if (attacking && (target == null || target.path.Count == 0)) {
                Fire(target,true);
            }
        }
    }

    bool AnimatorIsPlaying() {
        return animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1 != 0;
    }

    internal bool CanEnter(HexTile n) {
        return n.terrain.elevation >= elevationMin && n.terrain.elevation <= elevationMax;
	}

    internal void CompleteMove(HexTile hexTile) {
        tile.parent = null;
        List<HexTile> path = hexTile.buildpath();

        // Fuel is charged per tile entered. Aircraft burn their whole per-turn
        // allowance regardless of distance flown, per the design doc.
        int tilesEntered = Mathf.Max(0, path.Count - 1);
        if (fuelMax > 0) {
            if (LocomotionRules.BurnsFullFuelPerTurn(locomotion)) {
                if (tilesEntered > 0) fuel = Mathf.Max(0, fuel - Mathf.Max(minFuelPerTurn, minFuelPerTile));
            } else {
                fuel = Mathf.Max(0, fuel - tilesEntered * minFuelPerTile);
            }
        }

        tile.occupier = null;
        tile = hexTile;
        tile.occupier = this;
        this.path = path;

        // Carried units ride along rather than being left behind on the old tile.
        for (int i = 0; i < transported.Count; i++) {
            if (transported[i] != null) transported[i].tile = hexTile;
        }

        animator.SetBool("Moving", true);
        HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
    }

    /// <summary>Move without animation or tile-state churn. Used by the headless harness.</summary>
    internal void TeleportTo(HexTile hexTile, int tilesEntered) {
        if (fuelMax > 0) {
            if (LocomotionRules.BurnsFullFuelPerTurn(locomotion)) {
                if (tilesEntered > 0) fuel = Mathf.Max(0, fuel - Mathf.Max(minFuelPerTurn, minFuelPerTile));
            } else {
                fuel = Mathf.Max(0, fuel - tilesEntered * minFuelPerTile);
            }
        }
        if (tile != null && tile.occupier == this) tile.occupier = null;
        tile = hexTile;
        if (hexTile != null) hexTile.occupier = this;
        for (int i = 0; i < transported.Count; i++) {
            if (transported[i] != null) transported[i].tile = hexTile;
        }
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

    /// <summary>
    /// Post-combat retreat: the unit is on <paramref name="to"/> immediately. Only the sprite
    /// waits for the incoming shot to land before sliding over (at most a few seconds).
    /// </summary>
    public void RetreatTo(HexTile to) {
        pendingRetreat = to;
        ApplyPendingRetreat();
        if (tile == to) retreatHold = 3f;
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
        for (int i = 0; i < transported.Count; i++) {
            if (transported[i] != null) transported[i].tile = to;
        }
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

    internal void AttackResolve(Unit defender, bool retaliate) {
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
    public UnitData(Unit t) {
        transporterIndexPlusOne = 0;
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
        return ut;
    }
}