using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
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
    [Tooltip("The damage picture showing (0: none), from the terrain's health left. Set by RefreshDamageSprite.")]
    public int damageLevel;


    Dictionary<Player, int> _supply;
    /// <summary>How far the closest player's supply depot is, per player, as last published (in a BoardView, the view's copy).</summary>
    public Dictionary<Player, int> supply {
        get { BoardView v = BoardView.current; return v != null ? v.SupplyOf(this, _supply) : _supply; }
        set { _supply = value; }
    }

    /// <summary>This tile's number in board views (BoardView), once it has been in one.</summary>
    [System.NonSerialized] internal int viewId = -1;
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
    [SerializeField, FormerlySerializedAs("occupier")] Unit _occupier;
    /// <summary>The unit standing here (in a BoardView, the view's: see there).</summary>
    public Unit occupier {
        get { BoardView v = BoardView.current; return v != null ? v.OccupierOf(this, _occupier) : _occupier; }
        set { BoardView v = BoardView.current; if (v == null || !v.SetOccupier(this, value)) _occupier = value; }
    }
    /// <summary>The real occupier, whatever view is entered.</summary>
    internal Unit RealOccupier { get { return _occupier; } }
    [Tooltip("Reference to the building in the tile, if any")]
    public Building building;
    public int G;
    public HexTile parent;
    public Transform unitPlacement;

    // Start is called before the first frame update
    void Start() { Init(); }

    /// <summary>Per-player state and supply borders (Start; HexGenerator.EnsureWidth calls it at once for tiles it makes).</summary>
    public void Init()
    {
        if (threat != null) return;
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
        damage = 0;
        damageLevel = 0;
        upper.sprite = td.upper_normal;
        lower.sprite = td.lower;
        water.sprite = td.water;
        floor.sprite = td.floor;
        road = td.road;
        drawRoads();
        neighbors.ForEach(x => x.drawRoads());
    }

    /// <summary>The unit being moved would be stranded without fuel by ending here (and there is a warning sprite to show).</summary>
    bool DitchHere() {
        return uiData != null && uiData.move_warning != null && GameController.gc != null && GameController.gc.activeUnit != null && GameController.gc.activeUnit.WouldDitchFrom(this);
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
        // Both at half opacity; where it can move at half the colour's saturation, so where it can
        // fire stands out.
        if (s == UIState.ENEMY_MOVE) c = Desaturate(c, EnemyMoveSaturation);
        overlay.color = new Color(c.r, c.g, c.b, EnemyOverlayAlpha);
    }

    /// <summary>Opacity of an enemy's move and reach on the board (clicking an enemy unit).</summary>
    public const float EnemyOverlayAlpha = 0.5f;
    /// <summary>Saturation of an enemy's move tiles, as a share of its colour's (its reach keeps the full colour).</summary>
    public const float EnemyMoveSaturation = 0.5f;

    /// <summary><paramref name="c"/> with its saturation (HSV) scaled by <paramref name="share"/>, hue and value kept.</summary>
    public static Color Desaturate(Color c, float share) {
        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        // In HSV, scaling saturation by k moves each channel toward the value (the largest) by 1 - k.
        return new Color(max + (c.r - max) * share, max + (c.g - max) * share, max + (c.b - max) * share, c.a);
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
                overlay.sprite = DitchHere() ? uiData.move_warning : CaptureHere() ? uiData.capture : uiData.move_possible;
                break;
            case UIState.ATTACK:
                overlay.sprite = uiData.attack_possible;
                break;
            case UIState.MOVE_ARROW:
                if (parent != null) {
                    arrow.gameObject.SetActive(true);
                    arrow.transform.up = parent.transform.position - transform.position;
                    arrow.sprite = uiData.move_arrow;
                    // (Under fog of war, a unit the viewer does not see is not there as far as the path shows.)
                    if(FogOfWar.ApparentOccupier(this, FogOfWar.Viewer) == null)
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
                overlay.sprite = DitchHere() ? uiData.move_warning : CaptureHere() ? uiData.capture : uiData.move_confirm;
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
    /// <summary>
    /// Can <paramref name="unit"/> end its move here, as far as its side knows? Under fog of war an
    /// enemy it does not see is not there for this (ending on it means running into it).
    /// </summary>
    public bool CanEndTurnHere(Unit unit) {
        return FogOfWar.ApparentOccupier(this, unit != null ? unit.owner : null) == null;
    }

    [Tooltip("Fights resolved on this tile (every attack on a unit standing on it).")]
    public int combatCount;
    [Tooltip("Health this tile's terrain has lost (TerrainData.health): every attack on a unit standing on it takes one.")]
    public int damage;
    /// <summary>The terrains this tile was before it was destroyed, the latest last (to put it back: UndoDestruction).</summary>
    [System.NonSerialized] public List<TerrainData> destroyedFrom = new List<TerrainData>();

    /// <summary>A tile's terrain was destroyed and replaced: the tile, what it was. For logs.</summary>
    public static event System.Action<HexTile, TerrainData> TerrainDestroyed;

    /// <summary>
    /// A fight on this tile (a unit standing here was attacked): one point of damage to the ground,
    /// or to the building standing on it or owning it as a runway or dock (Building.Damage), which
    /// takes the hits instead.
    /// </summary>
    public void RegisterCombat() {
        combatCount++;
        Damage(1);
    }

    /// <summary>
    /// <paramref name="amount"/> damage to what stands here: the building if there is one, else the
    /// terrain. The terrain's top sprite steps through its damage pictures as its health goes
    /// (TerrainData.Stage); at 0 it is destroyed (<see cref="DestroyTerrain"/>). Terrain with no
    /// health (water, mountains) takes none.
    /// </summary>
    public void Damage(int amount) {
        if (amount <= 0) return;
        if (building != null) { building.Damage(amount); return; }   // its runways and docks too
        if (terrain == null || terrain.health <= 0) return;
        damage = Mathf.Min(terrain.health, damage + amount);
        RefreshDamageSprite();
        if (damage >= terrain.health && terrain.destroyedInto != null) DestroyTerrain();
    }

    /// <summary>
    /// The terrain is gone: the tile becomes its destroyedInto terrain at full health, exactly as if
    /// painted (roads redrawn on it and its neighbours: a destroyed bridge leaves its shores' roads
    /// ending at the water). A unit that cannot stand on the new ground is lost with what it carries
    /// (a grunt on a bridge that falls). Supply is worked out again for everyone (it runs over terrain).
    /// What it was is kept (<see cref="destroyedFrom"/>) so it can be put back (UndoDestruction).
    /// </summary>
    public void DestroyTerrain() {
        if (terrain == null || terrain.destroyedInto == null) return;
        TerrainData was = terrain;
        destroyedFrom.Add(was);
        SetTerrain(terrain.destroyedInto);
        AfterTerrainChange();
        if (TerrainDestroyed != null) TerrainDestroyed(this, was);
    }

    /// <summary>
    /// Repair: take back up to <paramref name="amount"/> damage from the terrain (or the building
    /// standing here). The damage pictures step back as it heals. Does not undo a destruction (see
    /// <see cref="UndoDestruction"/>).
    /// </summary>
    public void Repair(int amount) {
        if (amount <= 0) return;
        if (building != null) { building.Repair(amount); return; }
        damage = Mathf.Max(0, damage - amount);
        RefreshDamageSprite();
    }

    /// <summary>
    /// Put back the terrain this tile was before its last destruction (a rebuilt bridge, regrown
    /// forest), with <paramref name="damageLeft"/> damage on it (default: one short of destroyed,
    /// as it was the moment before). False if it was never destroyed. A unit standing here that
    /// cannot stand on it (a boat under a rebuilt bridge) is lost.
    /// </summary>
    public bool UndoDestruction(int damageLeft = -1) {
        if (destroyedFrom.Count == 0) return false;
        TerrainData back = destroyedFrom[destroyedFrom.Count - 1];
        destroyedFrom.RemoveAt(destroyedFrom.Count - 1);
        SetTerrain(back);
        damage = damageLeft < 0 ? Mathf.Max(0, back.health - 1) : Mathf.Clamp(damageLeft, 0, Mathf.Max(0, back.health));
        RefreshDamageSprite();
        AfterTerrainChange();
        return true;
    }

    /// <summary>The terrain changed under whatever stands here: units that cannot be on it are lost; supply and sight are worked out again.</summary>
    void AfterTerrainChange() {
        if (occupier != null && occupier.Strength > 0 && !occupier.CanEnter(this)) occupier.Perish();
        if (GameController.gc != null && GameController.gc.players != null)
            foreach (Player p in GameController.gc.players) if (p != null) p.Resupply();
        FogOfWar.Changed();
    }

    /// <summary>Lay new terrain on the tile, as painting does (the headless harness has no sprites: just the data). Damage starts again from 0.</summary>
    void SetTerrain(TerrainData td) {
        if (upper != null) loadData(td);
        else { terrain = td; road = td.road; damage = 0; }
    }

    /// <summary>The terrain's damage picture for the health it has left (TerrainData.Stage), or its normal top sprite.</summary>
    public void RefreshDamageSprite() {
        if (terrain == null) return;
        Sprite[] states = terrain.DamageStates;
        int stage = TerrainData.Stage(terrain.health, damage, states.Length);
        damageLevel = stage;
        if (upper == null) return;
        upper.sprite = stage > 0 ? states[stage - 1] : terrain.upper_normal;
    }

    /// <summary>Cover: the terrain's. (Damage changes the picture; the ground itself changes only when it is destroyed into something else.)</summary>
    public int EffectiveCover {
        get {
            if (terrain == null) return 0;
            return Mathf.Max(0, terrain.cover);
        }
    }

    /// <summary>Movement resistance: the terrain's (it changes when the terrain is destroyed into something else).</summary>
    public int EffectiveMoveability {
        get {
            if (terrain == null) return 0;
            return Mathf.Max(0, terrain.moveability);
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

    /// <summary>The tile last clicked on the board (the HUD describes it and the unit on it).</summary>
    public static HexTile lastClicked;

	public void OnMouseDown() {
        if (HexTactica.Harness.Replay.Active) return;   // a replay: the board is only watched
        WorldEdit();
        if (LevelEditor.singleton.editing) {
            LevelEditor.singleton.mouseDown = true;
		} else if(!LevelEditor.singleton.unit_placement && !LevelEditor.singleton.building_placement) { 
            // A click on the UI (a panel, the HUD) or an open menu is theirs: neither the UI nor IMGUI
            // stops it reaching the board.
            if ((TransportMenu.PointerOverUI != null && TransportMenu.PointerOverUI()) || BuildingMenu.Covers(Input.mousePosition)) return;
            lastClicked = this;
            // The unit here as the player at the screen knows it: under fog of war, one it does not
            // see is not there, so the click does what it would on an empty tile and gives nothing away.
            Player knower = FogOfWar.Viewer ?? GameController.gc.activePlayer;
            Unit seen = FogOfWar.ApparentOccupier(this, knower);
            // Any other click closes them (the transport panel reopens below while its transport
            // stays selected); one that is not on a tile offered for unloading drops the unload.
            Unit justMoved = null;
            bool unloading = state == UIState.UNLOAD_CONFIRM && TransportMenu.pendingCargo != null;
            if (!unloading) TransportMenu.Close();
            BuildingMenu.open = null;
            // An enemy's move and threat on show: any click clears it first.
            SupplyOverlay.HideEnemy();
            UnitController.uc.units.ForEach(x => x.clearDisplay());
            if (seen == null && building != null && state == UIState.FREE && GameController.gc.activeUnit == null
                     && building.owner != null && building.owner == GameController.gc.activePlayer && BuildingMenu.IsHumanTurn() && building.Purchasable().Count > 0) {
                // A building is clickable only with no unit on it: its build list.
                BuildingMenu.Open(building);
            } else if (unloading) {
                // The tile picked for the unit being unloaded (the transport moves to its picked tile first, if it unloads from there).
                TransportMenu.UnloadOnto(this);
            } else if (seen != null && Transporting.IsTransport(seen) && seen.transported.Count > 0 && seen.owner == GameController.gc.activePlayer
                       && (state == UIState.SELECTED || (state == UIState.FREE && !seen.active))) {
                // A transport clicked again once selected, or after its turn is over (units can still
                // be put down then): what is aboard.
                TransportMenu.Open(seen);
            } else if (state == UIState.LOAD_CONFIRM) {
                // Board: walk beside the transport if not already there, then get in.
                Unit mover = GameController.gc.activeUnit;
                Unit transport = seen;
                if (mover != null && transport != null) {
                    if (!(neighbors.Contains(mover.tile) && Transporting.CanBoardOnto(mover, mover.tile, this, transport)) && mover.tile != this) {
                        HexTile beside = null;
                        foreach (HexTile n in neighbors) if (n != null && n.state == UIState.MOVE && FogOfWar.ApparentOccupier(n, mover.owner) == null && Transporting.CanBoardOnto(mover, n, this, transport) && (beside == null || n.G < beside.G)) beside = n;
                        // Stopped on the way by an enemy it did not see (fog of war): no boarding, its move is over.
                        if (beside != null && !mover.CompleteMove(beside)) { mover.FinishActive(); transport = null; }
                    }
                    if (transport != null && Transporting.Load(transport, mover)) mover.FinishActive();
                }
                HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
            } else {
                if (seen != null && (state == UIState.FREE || state == UIState.RANGE) && seen.active && seen.owner.Equals(GameController.gc.activePlayer)) {
                    Unit sel = seen;
                    GameController.gc.activeUnit = sel;
                    HexGenerator.forAllTiles(x => { x.G = 0; x.Reset(); return 0; });
                    CheckMovementRange(sel).ForEach(x => { if (x.G != 0 && x.CanEndTurnHere(sel)) x.changeState(UIState.MOVE); });
                    // Only what it can fire on now: enemies it sees in range (not its whole range, not friends or empty tiles).
                    CheckAttackRange(sel).ForEach(position => { position.changeState(UIState.ATTACK); position.parent = this; });
                    changeState(UIState.SELECTED);
                    // Our transports it can board: beside it now, or beside a tile it can move to.
                    Unit cargo = sel;
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
                    // A transport that has moved can still put down what it carries.
                    if (Transporting.IsTransport(mover) && mover.transported.Count > 0) justMoved = mover;
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
                    bool arrived = true;
                    if (attacker.tile != move_confirm) {
                        //firing on the move (stopped on the way by an enemy it did not see: no attack)
                        arrived = attacker.CompleteMove(move_confirm, true);
                    }
                    if (arrived && attacker.Strength > 0 && defender != null && defender.tile != null) attacker.OrderAttack(defender);
                    attacker.FinishActive();
                    HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
                } else if (seen != null && (state == UIState.FREE || state == UIState.RANGE) && seen.owner != GameController.gc.activePlayer) {
                    // An enemy we are not attacking: where it can go and what it can hit (any selection is dropped).
                    GameController.gc.activeUnit = null;
                    SupplyOverlay.ShowEnemy(seen);
                } else {
                    // Anywhere else: clear the board and the selection.
                    GameController.gc.activeUnit = null;
                    HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
                }
            }
            // A selected transport with something aboard shows its panel, whatever the click did to
            // the selection (picking its move tile, say); so does one that has just moved.
            Unit selected = GameController.gc.activeUnit;
            if (TransportMenu.open == null) {
                if (selected != null && Transporting.IsTransport(selected) && selected.transported.Count > 0 && selected.owner == GameController.gc.activePlayer) TransportMenu.Open(selected);
                else if (justMoved != null && justMoved.tile != null) TransportMenu.Open(justMoved);
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
        // Under fog of war the move is planned on the board as the mover's side knows it: an enemy
        // it does not see does not block the way (and running into it is MoveAlong's business).
        using (FogOfWar.Concealing(mover != null ? mover.owner : null))
            return CheckMovementRangeOnBoard(mover, distance);
    }

    /// <summary>
    /// The tiles <paramref name="mover"/> could reach this turn from here, without leaving anything
    /// on the tiles (CheckMovementRange leaves the path for the click-to-move flow). For the threat map,
    /// which the AI works out on its own copy of the board, perhaps on another thread.
    /// </summary>
    public List<HexTile> ReachableThisTurn(Unit mover) {
        using (FogOfWar.Concealing(mover != null ? mover.owner : null)) {
            HexTactica.AI.MoveField field = HexTactica.AI.AIPath.ThisTurnFrom(mover, this, HexTactica.AI.AIPath.RangeThisTurn(mover));
            List<HexTile> reach = new List<HexTile>();
            foreach (HexTile t in field.Tiles) if (field.ReachableThisTurn(t)) reach.Add(t);
            HexTactica.AI.AIPath.Release(field);
            return reach;
        }
    }

    List<HexTile> CheckMovementRangeOnBoard(Unit mover, int distance) {
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
    /// <summary>Health the terrain has lost (older saves: a destruction tier, read as that much damage).</summary>
    public int damage;
    public int combatCount;
    public string terrain;
    /// <summary>The terrains this tile was before it was destroyed, oldest first, comma-separated (for repair). Empty if never.</summary>
    public string destroyedFrom;
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
        damage = t.damage;
        combatCount = t.combatCount;
        terrain = t.terrain.name;
        destroyedFrom = "";
        foreach (TerrainData d in t.destroyedFrom) if (d != null) destroyedFrom += (destroyedFrom.Length > 0 ? "," : "") + d.name;
        under = t.building != null ? t.building.Underlying(t).name : "";
    }

    public HexTile load() {
        HexTile t = HexGenerator.hg.get(x, y, z);
        if (t == null) return null;   // a save written for a bigger board: skip what this one lacks
        t.combatCount = combatCount;
        // City was a terrain; it is a building now. A City tile in an older map becomes a neutral
        // City building on grassland.
        Building city = terrain == "City" ? LevelEditor.loadBuilding("City") : null;
        TerrainData grass = LevelEditor.loadTerrain("Grassland");
        if (city != null && grass != null && BuildingController.bc != null) {
            t.loadData(grass);
            if (t.building == null) BuildingController.bc.Place(null, city, t);
        } else {
            TerrainData td = LevelEditor.loadTerrain(terrain);
            if (td == null) {
                // A terrain this game does not have (renamed or removed since the save): the ground
                // under a building if the save kept it, else grassland, rather than failing the load.
                td = (!string.IsNullOrEmpty(under) ? LevelEditor.loadTerrain(under) : null) ?? grass;
                Debug.LogWarning("Tile (" + x + "," + y + "," + z + "): no terrain named '" + terrain + "'; loaded as " + (td != null ? td.name : "nothing") + ".");
            }
            if (td != null) t.loadData(td);
        }
        // A building's tile, saved with its ground as shown: the building keeps it (see Building.Occupy).
        t.pendingUnder = !string.IsNullOrEmpty(under) ? LevelEditor.loadTerrain(under) : null;
        t.damage = Mathf.Max(0, damage);
        t.destroyedFrom.Clear();
        if (!string.IsNullOrEmpty(destroyedFrom))
            foreach (string n in destroyedFrom.Split(',')) { TerrainData d = LevelEditor.loadTerrain(n.Trim()); if (d != null) t.destroyedFrom.Add(d); }
        t.RefreshDamageSprite();
        t.changeState(UIState.FREE);
        return t;
    }
}