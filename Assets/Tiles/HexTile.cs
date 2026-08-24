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



    public void OnMouseDown() {
        UnitController.uc.units.ForEach(x => x.clearDisplay());
        if (LevelEditor.singleton.editing) {
            loadData(LevelEditor.singleton.td);
        } else if (LevelEditor.singleton.unit_placement) {
            LevelEditor.PlaceNewUnit(this);
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
                int damage = defender.displayAttackOdds(attacker);
                if(neighbors.Contains(parent) && defender.retaliates) {
                    attacker.displayAttackOdds(defender, null, damage);
                }
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
       return terrain.moveability;
    }

    int moveFormula(HexTile n, Unit mover) {


        return 1 + Mathf.Max(0, n.slow() - mover.terrainScramble);

        //return Mathf.Pow(n.slow(), mover.terrainImpact);
    }

    public List<HexTile> CheckMovementRange(Unit mover) {
        return CheckMovementRange(mover, mover.moveRange);
    }

    public List<HexTile> CheckMovementRange(Unit mover, int distance) {

        //SortedSet<HexTile> open = new SortedSet<HexTile>() ;
        PriorityQueue<HexTile> open = new PriorityQueue<HexTile>();
        List<HexTile> closed = new List<HexTile>();

        ////add start
        //ds_priority_add(open, start, start.id);
        open.Enqueue(this, 999);
        this.G = 0;
        this.parent = null;
        while (open.Count() > 0) {
            HexTile current = open.Dequeue();

            closed.Add(current);

            current.neighbors.ForEach(n => {
                
                bool not_in_closed = !closed.Contains(n);

                bool passable = mover.CanEnter(n) && mover.CanEnter(current); // (can_go(neighbor, mover) or neighbor.occupant != noone) and can_go(current, mover) and can_move_treaty(mover, neighbor);

                bool not_occupied = n.occupier == null || n.occupier.owner == mover.owner;//neighbor.occupant == noone or neighbor.occupant.owner == mover.owner or neighbor.occupant.owner == noone;

                bool in_range = moveFormula(n, mover) + current.G <= distance;
                if (not_in_closed && passable && not_occupied && in_range) {
                    // if(ds_priority_find_priority(open,neighbor) == 0 or ds_priority_find_priority(open,neighbor) == undefined)
                    if (!open.Contains(n)) {
                        n.parent = current;
                        n.G = current.G + moveFormula(n, mover);
                        open.Enqueue(n, n.G);
                    } else {
                        int tempG = current.G + moveFormula(n, mover);

                        if (tempG < n.G) {
                            n.parent = current;
                            n.G = tempG;
                            open.AlterPriority(n, tempG);
                        }
                    }
                }
            });
        }
        return closed;
    }

    public List<HexTile> CheckAttackRange(Unit attacker) {
        return AllInAttackRange(attacker).FindAll(position => {
            bool valid_target = attacker.validTarget(position);//neighbor.occupant == noone or neighbor.occupant.owner == mover.owner or neighbor.occupant.owner == noone;
            return valid_target;
        });
    }

   public List<HexTile> AllInAttackRange(Unit attacker) {
        List<HexTile> inrange = new List<HexTile>();
        int distance = attacker.AttackRange;
        int minRange = attacker.MinRange;
        for (int dx = -distance; dx <= distance; dx++) {
            for (int dy = -distance; dy <= distance; dy++) {
                for (int dz = -distance; dz <= distance; dz++) {
                        HexTile position = HexGenerator.hg.get(dx + this.x, dy + this.y, dz + this.z);
                        if (position != null) {

                            bool min_distance = this.Distance(position) > minRange;

                            bool attackable = attacker.CanAttack(position); // (can_go(neighbor, mover) or neighbor.occupant != noone) and can_go(current, mover) and can_move_treaty(mover, neighbor);
                         
                            if (attackable && min_distance) {
                                 inrange.Add(position);// 
                            }
                        }
                }
            }
        }
        return inrange;
    }
}

public class Heat {
    public float firepower;
    public float antiarmor;
    public float antiair;
    public int closestUnit;
    public int closestSupply;
    public bool inSupply;
}

public class AITileData {
    public float attackValue;
}

public struct TileData {
    public int x, y, z;
    public int damage;
    public string terrain;
    public TileData(HexTile t) {
        x = t.x;
        y = t.y;
        z = t.z;
        damage = t.damageLevel;
        terrain = t.terrain.name;
    }

    public HexTile load() {
        HexTile t = HexGenerator.hg.get(x, y, z);
        t.damageLevel = damage;
        t.loadData(LevelEditor.loadTerrain(terrain));
        t.changeState(UIState.FREE);
        return t;
    }
}