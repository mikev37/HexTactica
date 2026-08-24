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
    [Tooltip("If False unit has to move OR attack")]
    public bool moveAndAttack;
    [Range(0, 10)]
    [Tooltip("Range in which unit can attack")]
    public int AttackRange;
    public int MinRange;
    public bool retaliates = true;


    [Header("Movement Variables")]
    public Sprite move_display;
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
            if(tile)
                transform.position = Vector2.MoveTowards(transform.position, tile.unitPlacement.position, Time.deltaTime * moveSpeed);
            else {

            }

            if (attacking) {
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
        tile.occupier = null;
        tile = hexTile;
        tile.occupier = this;
        this.path = path;
        animator.SetBool("Moving", true);
        HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
    }

    internal bool CanAttack(HexTile position) {

        return ammo > 0;
    }
    internal bool CanAttack(Unit opposition) {

        return calculateDamage(this,opposition,opposition.tile,0) > 0;
    }



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
    }





    internal void OrderAttack(Unit defender) {
        attacking = true;
        target = defender;
        AttackActual(defender,true);
        if ((tile == null ||defender.CanAttack(tile))
            && (defender.tile == null || defender.tile.neighbors.Contains(tile))
            && defender.retaliates) {
            defender.AttackActual(this,false);
        }
    }

    internal void AttackActual(Unit defender, bool useCover) {
        HexTile coverTile = defender.tile;
        if (!useCover)
            coverTile = null;
        int damage = Unit.calculateDamage(this,defender,coverTile,0);
        defender.Strength -= damage;
        if (defender.Strength <= 0) {
            defender.DieActual();
        }
    }

    private void DieActual() {
        Strength = 0;
        owner.units.Remove(this);
        UnitController.uc.units.Remove(this);
        tile.occupier = null;
        tile = null;
        if (GetComponent<SupplyPoint>()) {
            owner.Resupply();
        }
    }

    internal int value() {


        return (int)(cost + Veterancy * cost * .2f)*1/6 + threatValue * 1/10;
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

    internal void AttackResolve(Unit defender, bool retaliate) {
        if (!defender) return;
        GameObject.Instantiate(weaponImpact, defender.transform.position, defender.transform.rotation);
        defender.StrengthDisplay = defender.Strength;
        if (defender.Strength <= 0) {
            defender.Die();
        } else if (retaliate && (tile == null || (defender.CanAttack(tile) && defender.tile.neighbors.Contains(tile))) && defender.retaliates) {
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
        if (tile != null)
            cover = Math.Min(defender.Concealment, tile.terrain.cover);
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

    internal bool validTarget(HexTile position) {
        return position.occupier != null && position.occupier.owner != owner;
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
    public UnitData(Unit t) {
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
    }

    public Unit load() {
        Player player = null;
        UnitData that = this;
        GameController.gc.players.ForEach(pl => { if (pl.id == that.owner_id) player = pl; });
        HexTile tile = HexGenerator.hg.get(x, y, z);
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