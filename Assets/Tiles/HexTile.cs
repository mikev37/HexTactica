using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;


public class HexTile : MonoBehaviour
{
    [Header("Render Fields")]
    [Tooltip("Variable Display Field for destructible terrain and the like")]
    public SpriteRenderer upper;
    public SpriteRenderer lower;
    public SpriteRenderer water;
    public SpriteRenderer floor;
    [Tooltip("Where the UI Overlays are shown")]
    public SpriteRenderer overlay;
    public SpriteRenderer arrow;
    public Text textField;
    public UIState state;
    [Header("UI Sprites")]
    public HexTileUIData uiData;// = HexTileUIData.instance;
    public TerrainData terrain;

    [Header("Connection Variables")]
    public List<HexTile> neighbors;
    public int x, y, z;
    public bool road;
    public SpriteRenderer road_up, road_lu, road_ld, road_ru, road_rd, road_dn;

    [Header("Personal Variables")]
    public int damageLevel;


    public Dictionary<Player, int> supply; //how far the closest player's supply depot is
    public HexTilePlayerSupplyDisplay supplyDisplayPrefab;
    public Dictionary<Player, Heat> threat; // how many units can hit this tile with how much damage

    internal int getThreatTo(Unit unit) {
        float outint = 0;
        if(unit.Evasion >= 3) {
            foreach(Player p in threat.Keys) {
                if(p != unit.owner) {
                    outint += threat[p].antiair;
                }
            }
        } else if(unit.Armor >= 3) {
            foreach (Player p in threat.Keys) {
                if (p != unit.owner) {
                    outint += threat[p].antiarmor;
                }
            }
        } else {
            foreach (Player p in threat.Keys) {
                if (p != unit.owner) {
                    outint += threat[p].firepower;
                }
            }
        }
        return (int)outint;

    }

    internal bool isSupplied(Player myPlayer) {
        return supply[myPlayer] > 0;
    }

    public AITileData aiData;

    [Header("Movement Variables")]
    public Unit occupier;// unit reference
    [Tooltip("Reference to the building in the tile, if any")]
    public Building building;
    public int G;
    public HexTile parent;
    public Transform unitPlacement;

    // Start is called before the first frame update
    void Start()
    {
        aiData = new AITileData();
        threat = new Dictionary<Player, Heat>();
        supply = new Dictionary<Player, int>();
        GameController.gc.players.ForEach(x => {
            threat.Add(x, new Heat());
            supply.Add(x, 0);
            HexTilePlayerSupplyDisplay dis = GameObject.Instantiate(supplyDisplayPrefab, transform).GetComponent<HexTilePlayerSupplyDisplay>();
            dis.myPlayer = x;
            dis.myTile = this;
        });
        loadData(terrain);
    }

    public void loadData(TerrainData td) {
        terrain = td;
        upper.sprite = td.upper_normal;
        lower.sprite = td.lower;
        water.sprite = td.water;
        floor.sprite = td.floor;
        road = td.road;
        drawRoads();
        neighbors.ForEach(x => x.drawRoads());
    }

    public void changeState(UIState ud) {
        state = ud;
        overlay.gameObject.SetActive(true);
        arrow.gameObject.SetActive(false);
        switch (state) {
            case UIState.SELECTED:
                overlay.sprite = uiData.selected;
                break;
            case UIState.MOVE:
                overlay.sprite = uiData.move_possible;
                break;
            case UIState.ATTACK:
                overlay.sprite = uiData.attack_possible;
                break;
            case UIState.MOVE_ARROW:
                if (parent != null) {
                    arrow.gameObject.SetActive(true);
                    arrow.transform.up = parent.transform.position - transform.position;
                    arrow.sprite = uiData.move_arrow;
                    if(occupier == null)
                        overlay.sprite = uiData.move_possible;
                    else
                        overlay.gameObject.SetActive(false);
                }
                break;
            case UIState.MOVE_CONFIRM:
                if (parent != null) {
                    arrow.gameObject.SetActive(true);
                    arrow.transform.up = parent.transform.position - transform.position;
                    arrow.sprite = uiData.move_arrow;
                }
                overlay.sprite = uiData.move_confirm;
                break;
            case UIState.ATTACK_CONFIRM:
                overlay.sprite = uiData.attack_Confirm;
                break;
            case UIState.LOAD_CONFIRM:
                overlay.sprite = uiData.load;
                break;
            case UIState.UNLOAD_CONFIRM:
                overlay.sprite = uiData.unload;
                break;
            default: overlay.gameObject.SetActive(false); break;
        }
    }

    private void drawRoads() {
        road_up.gameObject.SetActive(false);
        road_dn.gameObject.SetActive(false);
        road_ru.gameObject.SetActive(false);
        road_rd.gameObject.SetActive(false);
        road_lu.gameObject.SetActive(false);
        road_ld.gameObject.SetActive(false);
        if (!road) return;
        neighbors.ForEach(tile => {
            if (tile.road) {
                if (tile.x == x) {
                    if (tile.z > z) {
                        road_up.gameObject.SetActive(true);
                    } else {
                        road_dn.gameObject.SetActive(true);
                    }
                } else if (tile.x > x) {
                    if (tile.z == z) {
                        road_ru.gameObject.SetActive(true);
                    } else {
                        road_rd.gameObject.SetActive(true);
                    }
                } else { //tile.x < x
                    if (tile.y == y) {
                        road_lu.gameObject.SetActive(true);
                    } else {
                        road_ld.gameObject.SetActive(true);
                    }
                }
            }
        });
    }
    public bool CanEndTurnHere(Unit unit) {
        return occupier == null;
    }

    [Tooltip("Fights resolved on this tile; every 10 raises destruction one tier, to a max of 3")]
    public int combatCount;

    /// <summary>
    /// "Destruction increases the more a zone is bombarded or attacked in. For every
    /// 10 times combat is had in a zone, the sprite changes up to level 3 - maximum
    /// devastation. It affects movability and defense."
    /// </summary>
    public void RegisterCombat() {
        combatCount++;
        int tier = Mathf.Min(3, combatCount / 10);
        if (tier != damageLevel) {
            damageLevel = tier;
            RefreshDamageSprite();
        }
    }

    public void RefreshDamageSprite() {
        if (terrain == null || upper == null) return;
        if (damageLevel >= 3 && terrain.upper_destroyed != null) upper.sprite = terrain.upper_destroyed;
        else if (damageLevel >= 1 && terrain.upper_damaged != null) upper.sprite = terrain.upper_damaged;
        else upper.sprite = terrain.upper_normal;
    }

    /// <summary>Cover after destruction. Bombarded ground stops protecting anyone.</summary>
    public int EffectiveCover {
        get {
            if (terrain == null) return 0;
            return Mathf.Max(0, terrain.cover - damageLevel);
        }
    }

    /// <summary>Movement resistance after destruction. Cratered ground is harder to cross.</summary>
    public int EffectiveMoveability {
        get {
            if (terrain == null) return 0;
            return Mathf.Max(0, terrain.moveability + (damageLevel > 0 ? 1 : 0));
        }
    }



    public void OnMouseDown() {
        UnitController.uc.units.ForEach(x => x.clearDisplay());
        if (LevelEditor.singleton.editing) {
            loadData(LevelEditor.singleton.td);
        } else if (LevelEditor.singleton.unit_placement) {
            LevelEditor.PlaceNewUnit(this);
        } else if (LevelEditor.singleton.building_placement) {
            LevelEditor.PlaceNewBuilding(this);
        }
        else if (occupier == null && building != null && state == UIState.FREE && GameController.gc.activeUnit == null
                 && building.owner != null && building.owner == GameController.gc.activePlayer && building.CanProduce) {
            // A building is clickable only with no unit on it: its build list.
            BuildingMenu.Open(building);
        }
        else {
            if (occupier != null && state == UIState.FREE && occupier.active && occupier.owner.Equals(GameController.gc.activePlayer)) {
                GameController.gc.activeUnit = occupier;
                HexGenerator.forAllTiles(x => { x.G = 0; x.Reset(); return 0; });
                CheckMovementRange(occupier).ForEach(x => { if(x.G != 0 && x.CanEndTurnHere(occupier)) x.changeState(UIState.MOVE);});
                CheckAttackRange(occupier).ForEach(position => { position.changeState(UIState.ATTACK); position.parent = this; });
                changeState(UIState.SELECTED);
            } else if( state==UIState.MOVE) {
                Unit mover = GameController.gc.activeUnit;
                mover.tile.parent = null;
                HexGenerator.forAllTiles(x => { 
                    if (x.G != 0 && x.CanEndTurnHere(mover)) x.changeState(UIState.MOVE);
                    if (x.state != UIState.MOVE) x.changeState(UIState.FREE);
                        return 0; });
                List<HexTile> movepath = buildpath();
                movepath.ForEach(x => x.changeState(UIState.MOVE_ARROW));
                changeState(UIState.MOVE_CONFIRM);
                HexGenerator.forAllTiles(x => { if (x.state == UIState.ATTACK_CONFIRM) x.changeState(UIState.FREE); return 0; });

                if (mover.moveAndAttack) {
                    CheckAttackRange(mover).ForEach(position => { position.changeState(UIState.ATTACK); position.parent = this; });
                } else {

                }
            } else if (state == UIState.MOVE_CONFIRM) {
                Unit mover = GameController.gc.activeUnit;
                mover.CompleteMove(this);
                mover.FinishActive();
                HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
            } else if (state == UIState.ATTACK) {
                HexGenerator.forAllTiles(x => { if (x.state == UIState.ATTACK_CONFIRM) x.changeState(UIState.ATTACK); return 0; });
                changeState(UIState.ATTACK_CONFIRM);
                HexTile move_confirm = parent;
                Unit attacker = GameController.gc.activeUnit;
                Unit defender = occupier;
                // The exact resolution OrderAttack will run, firing from the tile the attacker will
                // stand on (parent when moving and firing): ambush order, retaliation, pinned retreat.
                defender.PreviewAttack(attacker, move_confirm != null ? move_confirm : attacker.tile);
            } else if (state == UIState.ATTACK_CONFIRM) {
                HexTile move_confirm = parent;
                Unit attacker = GameController.gc.activeUnit;
                Unit defender = occupier;
                if (attacker.tile != move_confirm) {
                    //firing on the move
                    attacker.CompleteMove(move_confirm);
                }
                //TODO Attack
                attacker.OrderAttack(defender);
                attacker.FinishActive();
                HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
            } else {
                HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
            }
        }
        //gameObject.Destroy(this.gameObject);
    }

    public int Distance(HexTile t) {
        HexTile a = this;
        HexTile b = t;
        return (Math.Abs(a.x - b.x)
              + Math.Abs(a.x + a.y - b.x - b.y)
              + Math.Abs(a.y - b.y)) / 2;
    }

    public void Reset() {
        G = 0;
        parent = null;
        changeState(UIState.FREE);
    }

    public List<HexTile> buildpath() {
        List<HexTile> ht = new List<HexTile>();
        if(parent != null) {
            ht.AddRange(parent.buildpath());
        }
        ht.Add(this);
        return ht;
    }

    public HexTile getParent() {
        HexTile u;
        if(parent == this) {
            parent = null;
            return this;
        }
        if (parent != null) {
            u = parent.getParent();
        } else u = this;
        return u;
    }

    public void Select()
    {
       
  
    }

    // Update is called once per frame
    void Update()
    {
        //textField.text = "" + (int)G;
        //enabled = false;
    }

    int slow() {
       return EffectiveMoveability;
    }

    // moveFormula removed: movement cost now lives in AIPath.StepCost, the single model.


    public List<HexTile> CheckMovementRange(Unit mover) {
        return CheckMovementRange(mover, HexTactica.AI.AIPath.RangeThisTurn(mover));
    }

    /// <summary>
    /// Tiles the mover can reach this turn from here, leaving G (movement spent) and
    /// parent (previous step) on each tile for the click-to-move flow and buildpath().
    ///
    /// Delegates to AIPath so there is exactly one movement model in the game. When the
    /// AI and the player each had their own search, the AI could choose a destination
    /// (along a road, say) that this search could not reach — and CompleteMove would then
    /// follow a stale parent chain and glide the unit straight through whatever was in
    /// the way.
    /// </summary>
    public List<HexTile> CheckMovementRange(Unit mover, int distance) {
        HexTactica.AI.MoveField field = HexTactica.AI.AIPath.ThisTurnFrom(mover, this, distance);
        List<HexTile> closed = new List<HexTile>();
        foreach (HexTile t in field.Tiles) {
            if (!field.ReachableThisTurn(t)) continue;
            t.G = field.Get(t).used;
            t.parent = field.ParentOf(t);
            closed.Add(t);
        }
        this.G = 0;
        this.parent = null;
        return closed;
    }

    public List<HexTile> CheckAttackRange(Unit attacker) {
        return AllInAttackRange(attacker).FindAll(position => {
            bool valid_target = attacker.validTarget(position);//neighbor.occupant == noone or neighbor.occupant.owner == mover.owner or neighbor.occupant.owner == noone;
            return valid_target;
        });
    }

   public List<HexTile> AllInAttackRange(Unit attacker) {
        // Same set as the original triple loop over dx/dy/dz, without iterating the
        // two-thirds of that cube where x+y+z != 0. CanAttack does not depend on the
        // target tile, so it is checked once rather than per tile.
        if (!attacker.CanAttack(this)) return new List<HexTile>();
        return HexTactica.AI.AIPath.TilesInRange(this, attacker.MinRange, attacker.AttackRange);
    }
}

public class Heat {
    public float firepower;
    public float antiarmor;
    public float antiair;
    public int closestUnit;
}

/// <summary>
/// Per-tile AI annotation: the reasoning the AI wrote onto the board for its last
/// decision here, shown in the tile's text field when the profile's annotateBoard is on.
///
/// The AI's working layers (conflict, distress, supply projection) used to live here and
/// now live in AIMap. On shared tiles they were wiped by ResetTileData after every unit
/// action, and two players' AIs would overwrite each other's values.
/// </summary>
public class AITileData {
    public string debugTag;

    public void Clear() {
        debugTag = null;
    }
}

public struct TileData {
    public int x, y, z;
    public int damage;
    public int combatCount;
    public string terrain;
    public TileData(HexTile t) {
        x = t.x;
        y = t.y;
        z = t.z;
        damage = t.damageLevel;
        combatCount = t.combatCount;
        // A building's tiles record the terrain under it (the building restores its ground on load).
        terrain = (t.building != null ? t.building.Underlying(t) : t.terrain).name;
    }

    public HexTile load() {
        HexTile t = HexGenerator.hg.get(x, y, z);
        if (t == null) return null;   // a save written for a bigger board: skip what this one lacks
        t.damageLevel = damage;
        t.combatCount = combatCount;
        // City was a terrain; it is a building now. A City tile in an older map becomes a neutral
        // City building on grassland.
        Building city = terrain == "City" ? LevelEditor.loadBuilding("City") : null;
        TerrainData grass = LevelEditor.loadTerrain("Grassland");
        if (city != null && grass != null && BuildingController.bc != null) {
            t.loadData(grass);
            if (t.building == null) BuildingController.bc.Place(null, city, t);
        } else t.loadData(LevelEditor.loadTerrain(terrain));
        t.RefreshDamageSprite();
        t.changeState(UIState.FREE);
        return t;
    }
}