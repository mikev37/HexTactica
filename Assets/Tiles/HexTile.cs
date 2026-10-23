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
    /// <summary>Set by a save being loaded: the terrain under the building that will stand here, whose ground is already on the tile.</summary>
    [System.NonSerialized] public TerrainData pendingUnder;

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

    /// <summary>The unit being moved would capture a building by ending here (and there is a capture sprite to show).</summary>
    bool CaptureHere() {
        return uiData != null && uiData.capture != null && GameController.gc != null && Building.WouldCapture(GameController.gc.activeUnit, this);
    }

    Color overlayColour;
    bool overlayColourKnown;

    /// <summary>Show this tile as one an enemy can move to or fire on, in that enemy's colour.</summary>
    public void ShowEnemy(UIState s, Player enemy) {
        changeState(s);
        Color c = enemy != null ? enemy.displayColor : Color.red;
        overlay.color = new Color(c.r, c.g, c.b, s == UIState.ENEMY_MOVE ? 0.85f : 0.6f);
    }


    public void changeState(UIState ud) {
        state = ud;
        if (!overlayColourKnown) { overlayColour = overlay.color; overlayColourKnown = true; }
        overlay.color = overlayColour;
        overlay.gameObject.SetActive(true);
        arrow.gameObject.SetActive(false);
        switch (state) {
            case UIState.SELECTED:
                overlay.sprite = uiData.selected;
                break;
            case UIState.MOVE:
                overlay.sprite = CaptureHere() ? uiData.capture : uiData.move_possible;
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
                overlay.sprite = CaptureHere() ? uiData.capture : uiData.move_confirm;
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
            case UIState.ENEMY_MOVE:
                overlay.sprite = uiData.move_possible;
                break;
            case UIState.ENEMY_THREAT:
            case UIState.RANGE:
                overlay.sprite = uiData.attack_possible;
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



    /// <summary>Show this tile as in the range of our selected unit (faint, in our colour).</summary>
    public void ShowRange(Player owner) {
        changeState(UIState.RANGE);
        Color c = owner != null ? owner.displayColor : Color.white;
        overlay.color = new Color(c.r, c.g, c.b, 0.35f);
    }

    private void WorldEdit() {
        if (LevelEditor.singleton.editing) {
            loadData(LevelEditor.singleton.td);
        } else if (LevelEditor.singleton.unit_placement) {
            LevelEditor.PlaceNewUnit(this);
        } else if (LevelEditor.singleton.building_placement) {
            LevelEditor.PlaceNewBuilding(this);
        }
    }

	public void OnMouseOver() {
		if (LevelEditor.singleton.mouseDown) {
            WorldEdit();
        }
    }

	public void OnMouseUp() {
        LevelEditor.singleton.mouseDown = false;
    }

	public void OnMouseDown() {
        WorldEdit();
        if (LevelEditor.singleton.editing) {
            LevelEditor.singleton.mouseDown = true;
		} else { 
            // A click on an open menu is the menu's (IMGUI does not stop it reaching the board).
            if (TransportMenu.Covers(Input.mousePosition) || BuildingMenu.Covers(Input.mousePosition)) return;
            // Any other click closes them; one that is not on a tile offered for unloading drops the unload.
            TransportMenu.open = null;
            BuildingMenu.open = null;
            if (TransportMenu.pendingCargo != null && state != UIState.UNLOAD_CONFIRM) { TransportMenu.pendingCargo = null; TransportMenu.pendingTransport = null; }
            // An enemy's move and threat on show: any click clears it first.
            SupplyOverlay.HideEnemy();
            UnitController.uc.units.ForEach(x => x.clearDisplay());
            if (occupier == null && building != null && state == UIState.FREE && GameController.gc.activeUnit == null
                     && building.owner != null && building.owner == GameController.gc.activePlayer && BuildingMenu.IsHumanTurn() && building.Purchasable().Count > 0) {
                // A building is clickable only with no unit on it: its build list.
                BuildingMenu.Open(building);
            } else if (state == UIState.UNLOAD_CONFIRM && TransportMenu.pendingCargo != null) {
                // The tile picked for the unit being unloaded.
                Unit cargo = TransportMenu.pendingCargo, transport = TransportMenu.pendingTransport;
                TransportMenu.pendingCargo = null; TransportMenu.pendingTransport = null;
                Transporting.Unload(transport, cargo, this);
                HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
            } else if (occupier != null && Transporting.IsTransport(occupier) && occupier.transported.Count > 0 && occupier.owner == GameController.gc.activePlayer
                       && (state == UIState.SELECTED || (state == UIState.FREE && !occupier.active && !occupier.attackedThisTurn))) {
                // A transport clicked again once selected, or after it has moved without attacking: what is aboard.
                TransportMenu.Open(occupier);
            } else if (state == UIState.LOAD_CONFIRM) {
                // Board: walk beside the transport if not already there, then get in.
                Unit mover = GameController.gc.activeUnit;
                Unit transport = occupier;
                if (mover != null && transport != null) {
                    if (!(neighbors.Contains(mover.tile) && Transporting.CanBoardOnto(mover, mover.tile, this, transport)) && mover.tile != this) {
                        HexTile beside = null;
                        foreach (HexTile n in neighbors) if (n != null && n.state == UIState.MOVE && n.occupier == null && Transporting.CanBoardOnto(mover, n, this, transport) && (beside == null || n.G < beside.G)) beside = n;
                        if (beside != null) mover.CompleteMove(beside);
                    }
                    if (Transporting.Load(transport, mover)) mover.FinishActive();
                }
                HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
            } else {
                if (occupier != null && (state == UIState.FREE || state == UIState.RANGE) && occupier.active && occupier.owner.Equals(GameController.gc.activePlayer)) {
                    GameController.gc.activeUnit = occupier;
                    HexGenerator.forAllTiles(x => { x.G = 0; x.Reset(); return 0; });
                    CheckMovementRange(occupier).ForEach(x => { if (x.G != 0 && x.CanEndTurnHere(occupier)) x.changeState(UIState.MOVE); });
                    CheckAttackRange(occupier).ForEach(position => { position.changeState(UIState.ATTACK); position.parent = this; });
                    // A unit that cannot move and fire (artillery): its whole range, not only what it can hit now.
                    if (!occupier.moveAndAttack)
                        foreach (HexTile t in AllInAttackRange(occupier)) if (t.state == UIState.FREE) t.ShowRange(occupier.owner);
                    changeState(UIState.SELECTED);
                    // Our transports it can board: beside it now, or beside a tile it can move to.
                    Unit cargo = occupier;
                    foreach (Unit tr in cargo.owner.units) {
                        if (tr == null || tr == cargo || tr.tile == null || !Transporting.CanCarry(tr, cargo)) continue;
                        // From here or from a tile it can move to beside the transport, and able to step aboard from there.
                        bool reach = tr.tile.neighbors.Contains(this) && Transporting.CanBoardOnto(cargo, this, tr.tile, tr);
                        foreach (HexTile n in tr.tile.neighbors) if (n != null && n.state == UIState.MOVE && Transporting.CanBoardOnto(cargo, n, tr.tile, tr)) reach = true;
                        if (reach) tr.tile.changeState(UIState.LOAD_CONFIRM);
                    }
                } else if (state == UIState.MOVE) {
                    Unit mover = GameController.gc.activeUnit;
                    mover.tile.parent = null;
                    HexGenerator.forAllTiles(x => {
                        if (x.G != 0 && x.CanEndTurnHere(mover)) x.changeState(UIState.MOVE);
                        if (x.state != UIState.MOVE) x.changeState(UIState.FREE);
                        return 0;
                    });
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
                        attacker.CompleteMove(move_confirm, true);
                    }
                    //TODO Attack
                    attacker.OrderAttack(defender);
                    attacker.FinishActive();
                    HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
                } else if (occupier != null && (state == UIState.FREE || state == UIState.RANGE) && occupier.owner != GameController.gc.activePlayer) {
                    // An enemy we are not attacking: where it can go and what it can hit (any selection is dropped).
                    GameController.gc.activeUnit = null;
                    SupplyOverlay.ShowEnemy(occupier);
                } else {
                    // Anywhere else: clear the board and the selection.
                    GameController.gc.activeUnit = null;
                    HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
                }
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
        HexTactica.AI.AIPath.Release(field);
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
    /// <summary>
    /// A building's tile: the terrain under the building (what the tile gets back if the building
    /// goes), while <see cref="terrain"/> is the ground shown, which the editor can paint. Empty
    /// for other tiles. Older saves have no such field and store the terrain under the building in
    /// <see cref="terrain"/>; the building then lays its own ground over it when it loads.
    /// </summary>
    public string under;
    public TileData(HexTile t) {
        x = t.x;
        y = t.y;
        z = t.z;
        damage = t.damageLevel;
        combatCount = t.combatCount;
        terrain = t.terrain.name;
        under = t.building != null ? t.building.Underlying(t).name : "";
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
        // A building's tile, saved with its ground as shown: the building keeps it (see Building.Occupy).
        t.pendingUnder = !string.IsNullOrEmpty(under) ? LevelEditor.loadTerrain(under) : null;
        t.RefreshDamageSprite();
        t.changeState(UIState.FREE);
        return t;
    }
}