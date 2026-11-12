using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
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

    [Header("Sounds")]
    [Tooltip("Played when a human player selects this unit (never for an AI's units).")]
    [LooksOnly] public AudioClip orderSound;
    [Tooltip("Played once as the unit starts moving.")]
    [LooksOnly] public AudioClip moveBurstSound;
    [Tooltip("Looped while the unit moves; fades out and pitches down when it arrives.")]
    [LooksOnly] public AudioClip moveLoopSound;
    [Tooltip("Played when the unit plays its attack animation.")]
    [LooksOnly] public AudioClip fireSound;
    [Tooltip("Played when the unit dies.")]
    [LooksOnly] public AudioClip dieSound;
    [Tooltip("Seconds the move loop takes to fade out once the unit arrives.")]
    [LooksOnly] public float moveLoopFadeOut = 0.5f;
    [Tooltip("Pitch the move loop slides down to as it fades out (1 = no change).")]
    [LooksOnly, Range(0.1f, 1f)] public float moveLoopEndPitch = 0.6f;

    public Text textArea;

    public Animator animator;

    public int cost;
    [Tooltip("What the unit is and how it is meant to be used, in a few sentences: shown in the main menu's unit explorer.")]
    [TextArea(3, 8), LooksOnly] public string description;

    [Header("Personal Variables")]
    [LooksOnly] public string personal_name;
    [Tooltip("Can this unit act this turn")]
    public bool active;
    [Tooltip("Which player owns the vehicle")]
    public Player owner;
    [Range(0, 6)]
    [Tooltip("How much health the unit has")]
    [SerializeField, FormerlySerializedAs("Strength")] int _strength;
    /// <summary>How much health the unit has (in a BoardView, the view's: see there).</summary>
    public int Strength {
        get { BoardView v = BoardView.current; return v != null ? v.StrengthOf(this, _strength) : _strength; }
        set { BoardView v = BoardView.current; if (v == null || !v.SetStrength(this, value)) _strength = value; }
    }
    /// <summary>The real strength, whatever view is entered.</summary>
    internal int RealStrength { get { return _strength; } }
    [Range(0, 3)]
    [Tooltip("Veteran units are stronger")]
    public int Veterancy;
    [Tooltip("Where the unit stands")]
    [SerializeField, FormerlySerializedAs("tile")] HexTile _tile;
    /// <summary>Where the unit stands (in a BoardView, the view's: see there).</summary>
    public HexTile tile {
        get { BoardView v = BoardView.current; return v != null ? v.TileOf(this, _tile) : _tile; }
        set { BoardView v = BoardView.current; if (v == null || !v.SetTile(this, value)) _tile = value; }
    }
    /// <summary>The real tile, whatever view is entered.</summary>
    internal HexTile RealTile { get { return _tile; } }

    /// <summary>This unit's number in board views (BoardView), once it has been in one.</summary>
    [System.NonSerialized] internal int viewId = -1;
    [System.NonSerialized] SupplyPoint hubCache;
    [System.NonSerialized] bool hubKnown;
    [System.NonSerialized] string nameCache;

    /// <summary>
    /// Its supply point, if it is a supply unit. On the main thread looked up each time (one may be
    /// added); thinking in a BoardView (perhaps on another thread, where Unity cannot be asked), the
    /// one it had when the view was made.
    /// </summary>
    public SupplyPoint Hub {
        get {
            if (BoardView.current != null && hubKnown) return hubCache;
            hubCache = GetComponent<SupplyPoint>();
            hubKnown = true;
            return hubCache;
        }
    }

    /// <summary>The object's name; in a BoardView, as it was when the view was made (Unity cannot be asked off the main thread).</summary>
    public new string name {
        get { if (BoardView.current != null && nameCache != null) return nameCache; nameCache = base.name; return nameCache; }
        set { base.name = value; nameCache = value; }
    }

    /// <summary>Called by BoardView.Snapshot on the main thread: remember what a thought elsewhere will need to read.</summary>
    internal void Prime() { hubCache = GetComponent<SupplyPoint>(); hubKnown = true; nameCache = base.name; }
    [Tooltip("Ground unit able to move into and through water tiles that border land")]
    public bool amphibious;
    [Tooltip("Air tanker: refuels and resupplies friendly air units (not other tankers) that start their turn beside it, as long as it has supplies. Its own supplies run down a turn at a time like any aircraft's (resupplying others costs nothing more), so suppliesMax is how many turns it can do it away from base.")]
    public bool airTanker;
    /// <summary>Fired this turn: an infantry unit that attacked does not capture the building it stands on.</summary>
    [System.NonSerialized] public bool attackedThisTurn;

    [Tooltip("((Firepower+Veterancy) / (Evasion - Targeting)™ / (Armor - Pierce)™ - (Concealment of Hex.Cover)) * Unit_Strength / Defender_Strength")]
    [Header("Combat Variables")]
    [Range(0, 10)]
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
    [Range(0, 6)]
    public int Vulnerability;
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
    [Tooltip("Its attacks ignore the defender's cover (napalm, flamethrowers): no cover off its damage, and a defender cannot use the ground to fire first on it (an ambush by veterancy alone still can).")]
    public bool ignoresCover;

    [Header("Vision (fog of war)")]
    [Range(0, 10)]
    [Tooltip("Tiles it sees around it when fog of war is on: everything within this many steps.")]
    public int visionRange = 2;
    [Range(0, 10)]
    [Tooltip("Stealth: an enemy observer this many steps away or more does not see it, even inside its vision (0: no stealth). A unit next to an enemy unit, or on an enemy building, is always seen.")]
    public int stealthRange = 0;
    [Range(0, 10)]
    [Tooltip("Stealth detection, as a Radar's (Building.detectRange): a stealthy enemy unit of any kind (land, air or sea) this many steps from it or fewer is seen by its owner, whether its own vision reaches or not (0: none). Fog of war only.")]
    public int detectRange = 0;


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
    [LooksOnly] public int moveSpeed = 10;
    /// <summary>Every unit's move speed on screen is multiplied by this (a replay's fast forward); 1 in play.</summary>
    public static float MoveSpeedScale = 1f;
    [Tooltip("How much health the unit appears to have (for animations so the unit doesn't die before the shot is fired)")]
    [LooksOnly] public int StrengthDisplay;

    [Tooltip("Strength pips suppressed by indirect fire: they do not fire back if the unit is attacked again before its own turn starts, when they all come back. Never more than Strength.")]
    public int suppressed;

    /// <summary>Strength that fires back when this unit is attacked: Strength less the suppressed pips (not below 0).</summary>
    public int RetaliationStrength { get { return Mathf.Max(0, Strength - Mathf.Clamp(suppressed, 0, Strength)); } }

    /// <summary>
    /// Destroyed some other way than by dying (deleted in the editor's Hierarchy, the scene closing):
    /// off every list and off its tile, so nothing reads it afterwards.
    /// </summary>
    void OnDestroy() {
        if (dead) return;
        dead = true;
        if (owner != null && owner.units != null) owner.units.Remove(this);
        if (UnitController.uc != null) UnitController.uc.units.Remove(this);
        if (transported != null) {
            foreach (Unit c in transported) if (c != null && c.trasnporter == this) c.trasnporter = null;
            transported.Clear();
        }
        if (trasnporter != null && trasnporter.transported != null) trasnporter.transported.Remove(this);
        trasnporter = null;
        if (tile != null && tile.occupier == this) tile.occupier = null;
        tile = null;
        FogOfWar.Changed();
    }

    internal void Delete() {
        DieActual();
        Destroy(gameObject);
    }

    /// <summary>
    /// The unit, and whatever it carries, now belongs to <paramref name="newOwner"/> (a scenario's Give Units action):
    /// off its owner's roster onto the new one's, where it stands. Supply and sight are worked out again for everyone.
    /// </summary>
    public void ChangeOwner(Player newOwner) {
        if (newOwner == null || newOwner == owner || dead) return;
        if (owner != null && owner.units != null) owner.units.Remove(this);
        owner = newOwner;
        if (newOwner.units != null && !newOwner.units.Contains(this)) newOwner.units.Add(this);
        if (transported != null) foreach (Unit c in transported.ToArray()) if (c != null) c.ChangeOwner(newOwner);
        if (GameController.gc != null && GameController.gc.players != null)
            foreach (Player p in GameController.gc.players) if (p != null) p.Resupply();
        FogOfWar.Changed();
        if (GameController.gc != null) GameController.gc.CheckVictory();
    }

    /// <summary>
    /// Lost without a shot (starved, ditched): off the board at once, like any death, and on screen
    /// it plays its death as if shot (the explosion and the "Die" animation, whose end removes it).
    /// </summary>
    internal void Perish() {
        DieActual();
        if (GameController.gc != null && GameController.gc.headless) { Destroy(gameObject); return; }
        Die();
    }

    public List<HexTile> path;//path for following when moving

    [Header("Supply Variables")]
    public int supplies;
    public int suppliesMax;
    public int fuel;
    public int fuelMax;
    [Tooltip("The kinds of unit (by locomotion) this unit can attack. Unticked: it cannot attack them, and does them no damage (artillery and jets, say). All ticked by default.")]
    public List<Locomotion> attacksLocomotion = new List<Locomotion> { Locomotion.Boots, Locomotion.Wheels, Locomotion.Treads, Locomotion.Rotor, Locomotion.Munition, Locomotion.Jet, Locomotion.Ship, Locomotion.Immobile };
    public int ammo;
    public int ammoMax;

    [Header("Transport Variables")]
    [Tooltip("Room aboard, in slots: the weights of what it carries add up to at most this. 0: not a transport.")]
    public int transportSlots;
    [Tooltip("Not used: capacity is transportSlots, counted in weight.")]
    public int maxWeight;
    [Tooltip("Slots this unit takes aboard a transport (a grunt 1, a supply unit 1, a tank 2).")]
    public int weight;
    [Tooltip("The locomotion types this transport carries (an APC: Boots). Empty: Boots only.")]
    public List<Locomotion> carries = new List<Locomotion>();

    public List<Unit> transported;
    public Unit trasnporter;
    [Tooltip("Where carried units sit on this transport, one per slot (the transport itself if none)")]
    public List<Transform> cargoSlots = new List<Transform>();
    [Tooltip("Scale of a carried unit's sprites while aboard")]
    public float cargoScale = 0.45f;
    [Tooltip("Aircraft transport (a plane, not a helicopter): what a unit it puts down wears on the way down — attached when it is dropped, removed when it lands. Optional: empty uses the shared one, Resources/Units/Prefabs/Parachute (GameAssets.Parachute).")]
    public GameObject parachutePrefab;
    [Tooltip("Aircraft transport: how far above its tile a unit it puts down appears, before settling onto it (world units).")]
    public float dropHeight = 0.6f;
    [Tooltip("Aircraft transport: how fast a unit it puts down comes down to its tile (world units a second).")]
    public float dropSpeed = 0.6f;

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
        // Fades the unit in and out of sight under fog of war (does nothing with fog off).
        if (GetComponent<FogFader>() == null) gameObject.AddComponent<FogFader>();
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
            if (boardingWalk.Count == 1 || next == null) {
                // Only into the transport once both are on the same cell on screen: the transport has stopped
                // and this sprite has reached it. (Parented while the transport was still driving, the cargo
                // went with every flip of the transport's sprite and was drawn far from where it should be.)
                Unit tr = trasnporter;
                bool driving = tr.path != null && tr.path.Count > 0;
                Vector3 there = tr.transform.position;
                if (driving || Vector2.Distance(transform.position, there) > .1f) {
                    if (animator != null) animator.SetBool("Moving", true);
                    if (!driving || Vector2.Distance(transform.position, there) > .1f) {
                        transform.rotation = there.x < transform.position.x ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;
                        transform.position = Vector2.MoveTowards(transform.position, there, Time.deltaTime * moveSpeed * MoveSpeedScale);
                    }
                    return;
                }
                boardingWalk.Clear(); SettleAboard(tr);
            }
            else {
                if (animator != null) animator.SetBool("Moving", true);
                Vector3 to = next.unitPlacement != null ? next.unitPlacement.position : next.transform.position;
                transform.rotation = to.x < transform.position.x ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;
                if (Vector2.Distance(transform.position, to) > .1f) transform.position = Vector2.MoveTowards(transform.position, to, Time.deltaTime * moveSpeed * MoveSpeedScale);
                else { spriteTile = next; boardingWalk.RemoveAt(0); }
                return;
            }
        }
        // Put down at the end of its transport's move: it rides along until the transport has
        // stopped, then gets off and walks to its tile.
        if (disembarkFrom != null) {
            if (disembarkFrom.Strength > 0 && disembarkFrom.path != null && disembarkFrom.path.Count > 0) {
                transform.localPosition = Vector3.MoveTowards(transform.localPosition, Vector3.zero, Time.deltaTime * moveSpeed * MoveSpeedScale);
                return;
            }
            FinishDisembark();
        }
        // Aboard: shrink into the transport's slot; nothing else moves this sprite.
        if (trasnporter != null) {
            if (!Finite(transform.localPosition)) transform.localPosition = Vector3.zero;
            transform.localPosition = Vector3.MoveTowards(transform.localPosition, Vector3.zero, Time.deltaTime * moveSpeed * MoveSpeedScale);
            transform.localScale = Vector3.MoveTowards(transform.localScale, homeScale * cargoScale, Time.deltaTime * 2f);
            return;
        }
        // Dropped from a plane: coming down onto its tile, then the parachute goes.
        if (dropFrom != null) {
            if (tile == null || tile.unitPlacement == null) { EndDrop(); }
            else {
                Vector3 to = tile.unitPlacement.position;
                float speed = dropFrom != null ? Mathf.Max(0.05f, dropFrom.dropSpeed) : 0.6f;
                transform.position = Vector3.MoveTowards(transform.position, to, Time.deltaTime * speed);
                if (Vector2.Distance(transform.position, to) <= 0.01f) { transform.position = to; spriteTile = tile; EndDrop(); }
                else return;
            }
        }
        // Just unloaded: grow back while walking off. (Not one that died aboard: it stays the size it rode at.)
        if (scaleKnown && !diedAboard && transform.localScale != homeScale) transform.localScale = Vector3.MoveTowards(transform.localScale, homeScale, Time.deltaTime * 2f);
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
                transform.position = Vector2.MoveTowards(transform.position, ht.unitPlacement.position, Time.deltaTime * moveSpeed * MoveSpeedScale);
            } else {
                spriteTile = ht;
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
                transform.position = Vector2.MoveTowards(transform.position, slideFirst.Value, Time.deltaTime * moveSpeed * MoveSpeedScale);
                if (Vector2.Distance(transform.position, slideFirst.Value) <= 0.05f) slideFirst = null;
            }
            else if (refuelHopTo != null && tile && retreatHold <= 0f) RefuelHopStep();
            else if(tile && retreatHold <= 0f) {
                transform.position = Vector2.MoveTowards(transform.position, tile.unitPlacement.position, Time.deltaTime * moveSpeed * MoveSpeedScale);
                if (Vector2.Distance(transform.position, tile.unitPlacement.position) <= 0.2f) spriteTile = tile;
            }
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

    /// <summary>The tile the sprite was last on, while it is still walking or sliding (animation only).</summary>
    [System.NonSerialized] HexTile spriteTile;

    /// <summary>
    /// Where the unit is drawn, as a tile: the tile its sprite last reached while it walks or slides
    /// there, else the tile it stands on; aboard, its transport's. Fog of war shows an enemy by this,
    /// so it appears as it walks into sight and fades as it walks out (FogOfWar.ShownToViewer).
    /// </summary>
    public HexTile ShownTile {
        get {
            if (trasnporter != null && trasnporter != this) return trasnporter.ShownTile;
            if (spriteTile == null || spriteTile == tile) return tile;
            // Not moving any more (or never drawn): where it stands.
            if (path.Count == 0 && boardingWalk.Count == 0 && retreatHold <= 0f && !slideFirst.HasValue && !RetreatPlaying
                && (tile == null || tile.unitPlacement == null || Vector2.Distance(transform.position, tile.unitPlacement.position) <= 0.2f)) {
                spriteTile = null;
                return tile;
            }
            return spriteTile;
        }
    }

    /// <summary>Any unit on the board still animating (see <see cref="Animating"/>).</summary>
    public static bool AnyAnimating() {
        if (UnitController.uc == null) return false;
        foreach (Unit u in UnitController.uc.units) if (u != null && u.Animating) return true;
        return false;
    }

    // ---- Sounds (on screen only: nothing here changes the rules). Volume is the options menu's, through
    // AudioListener.volume (GameOptions.Volume), which every AudioSource follows. ----

    bool SoundsOn { get { return GameSounds.On; } }

    /// <summary>
    /// Can whoever is watching see this unit (fog of war)? Unseen units make no sound, as they show no explosion.
    /// Asked of the fog itself as well as the fader: the fader only catches up at the end of the frame, and a unit
    /// that fires from the fog is revealed (FogOfWar.Reveal) in the same frame its shot sounds.
    /// </summary>
    bool Audible {
        get { FogFader f = GetComponent<FogFader>(); return f == null || f.Shown || FogOfWar.ShownToViewer(this); }
    }

    AudioSource oneShotSource, moveLoopSource;
    bool soundMoving;
    float loopFadeT = -1f;

    AudioSource NewSource() {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        return s;
    }

    /// <summary>Play a clip from this unit (cut off if the unit is destroyed while it plays).</summary>
    void PlaySound(AudioClip clip) {
        if (clip == null || !SoundsOn || !Audible) return;
        if (oneShotSource == null) oneShotSource = NewSource();
        oneShotSource.PlayOneShot(clip, GameOptions.EffectsVolume);
    }

    /// <summary>A human player selected this unit (HexTile.ClickBoard): its order sound.</summary>
    internal void PlayOrderSound() {
        // Only the human whose turn it is, selecting one of their own units: nothing for an AI's units, a replay,
        // or a unit of someone else's clicked to look at it.
        if (owner == null || !FogOfWar.IsHuman(owner) || HexTactica.Harness.Replay.Active) return;
        if (GameController.gc == null || owner != GameController.gc.activePlayer) return;
        PlaySound(orderSound);
    }

    /// <summary>
    /// The move sounds, every frame: the burst and the start of the loop when the sprite starts walking, the
    /// loop fading out and pitching down once it arrives.
    /// </summary>
    void LateUpdate() {
        if (!SoundsOn) return;
        bool moving = !deathPlayed && ((path != null && path.Count > 0) || boardingWalk.Count > 0);
        if (moving && !soundMoving) {
            soundMoving = true;
            if (Audible) {
                PlaySound(moveBurstSound);
                if (moveLoopSound != null) {
                    if (moveLoopSource == null) { moveLoopSource = NewSource(); moveLoopSource.loop = true; }
                    moveLoopSource.clip = moveLoopSound;
                    moveLoopSource.volume = GameOptions.EffectsVolume;
                    moveLoopSource.pitch = 1f;
                    moveLoopSource.Play();
                }
            }
            loopFadeT = -1f;
        } else if (!moving && soundMoving) {
            soundMoving = false;
            if (moveLoopSource != null && moveLoopSource.isPlaying) loopFadeT = 0f;
        }
        if (moveLoopSource == null) return;
        // Walking into the fog: silent while unseen.
        moveLoopSource.mute = !Audible;
        if (loopFadeT >= 0f) {
            loopFadeT += Time.deltaTime;
            float k = moveLoopFadeOut > 0f ? Mathf.Clamp01(loopFadeT / moveLoopFadeOut) : 1f;
            moveLoopSource.volume = (1f - k) * GameOptions.EffectsVolume;
            moveLoopSource.pitch = Mathf.Lerp(1f, moveLoopEndPitch, k);
            if (k >= 1f) { moveLoopSource.Stop(); moveLoopSource.volume = 1f; moveLoopSource.pitch = 1f; loopFadeT = -1f; }
        } else if (moveLoopSource.isPlaying) moveLoopSource.volume = GameOptions.EffectsVolume;   // the options' slider, as it moves
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
        disembarkFrom = null;
        if (!scaleKnown) { homeScale = transform.localScale; scaleKnown = true; }
        // Its own UI goes: strength, veterancy and warnings are not shown for a unit aboard. (Kept from an
        // earlier boarding not yet undone — boarded again, or moved between transports — so it all comes
        // back on getting off: cleared here, the pips hidden the first time were never shown again.)
        foreach (Transform t in GetComponentsInChildren<Transform>(true)) {
            if (t == transform || !t.gameObject.activeSelf) continue;
            if (IsOwnUI(t)) {
                t.gameObject.SetActive(false);
                if (!hiddenUI.Contains(t.gameObject)) hiddenUI.Add(t.gameObject);
            }
        }
        // The walk there (if it moved to board) is played out first, at full size.
        boardingWalk.Clear();
        if (path != null && path.Count > 0) boardingWalk.AddRange(path);
        boardingWalk.Add(transport.tile);
        if (path != null) path.Clear();
    }

    /// <summary>
    /// Getting on or off a transport on screen: walking to it before stepping in, or riding to where it is
    /// put down. Fog of war then judges it by the tile under its sprite (TileUnderSprite).
    /// </summary>
    public bool GettingOnOrOff { get { return (trasnporter != null && boardingWalk.Count > 0) || disembarkFrom != null; } }

    /// <summary>The tile nearest where this unit's sprite is drawn (as a marine's boat decides it is on water).</summary>
    public HexTile TileUnderSprite() {
        HexTile best = tile;
        if (HexGenerator.hg == null) return best;
        float bestD = float.MaxValue;
        Vector2 here = transform.position;
        foreach (HexTile t in HexGenerator.hg.tiles) {
            if (t == null || t.unitPlacement == null) continue;
            float d = Vector2.Distance(t.unitPlacement.position, here);
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    /// <summary>A child that is this unit's own UI: strength pips, veterancy, status icons.</summary>
    internal static bool IsOwnUI(Transform t) {
        if (t.GetComponent<Canvas>() != null || t.name == "HealthCircles" || t.name == "Veterancy" || t.name == "Canvas") return true;
        foreach (MonoBehaviour m in t.GetComponents<MonoBehaviour>())
            if (m != null && (m.GetType().Name == "UnitStatusIcon" || m.GetType().Name == "HealthDot" || m.GetType().Name == "VeterancyChevron")) return true;
        return false;
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
        // The first cargo slot no other unit aboard is sitting in. (By its place in the list, a unit boarding
        // after another had got off took the slot of one still aboard: the two were drawn on top of each other.)
        int slot = 0;
        while (transport.transported.Exists(c => c != null && c != this && c.cargoSlot == slot)) slot++;
        cargoSlot = slot;
        Transform at = transport.cargoSlots != null && slot < transport.cargoSlots.Count && transport.cargoSlots[slot] != null
                     ? transport.cargoSlots[slot] : transport.transform;
        // Into the slot, sliding there from where it stands (Update). A slot (or transport) scaled to
        // nothing on some axis at that moment (an animation, a 2D prefab with z scale 0) has no inverse:
        // keeping the world position would give a NaN local position, so it goes straight into the slot.
        Vector3 ls = at.lossyScale;
        bool degenerate = Mathf.Abs(ls.x) < 1e-4f || Mathf.Abs(ls.y) < 1e-4f || Mathf.Abs(ls.z) < 1e-4f;
        transform.SetParent(at, !degenerate);
        if (degenerate || !Finite(transform.localPosition)) transform.localPosition = Vector3.zero;
        transform.rotation = at.rotation;
        if (cargoGroup == null) cargoGroup = gameObject.AddComponent<UnityEngine.Rendering.SortingGroup>();
        int layer = SortingLayer.NameToID("Transported");
        SpriteRenderer top = null;
        foreach (SpriteRenderer r in transport.GetComponentsInChildren<SpriteRenderer>()) if (top == null || r.sortingOrder > top.sortingOrder) top = r;
        if (layer != 0 && SortingLayer.IsValid(layer)) { cargoGroup.sortingLayerID = layer; cargoGroup.sortingOrder = slot; }
        else if (top != null) { cargoGroup.sortingLayerID = top.sortingLayerID; cargoGroup.sortingOrder = top.sortingOrder + 1 + slot; }
        // Supplies aboard are shown like any cargo; a transport whose animator has a "CarryingSupply"
        // bool is told, in case its art wants to react.
        if (Hub != null) transport.SetAnimatorBool("CarryingSupply", true);
        // Settled in its slot: a unit whose animator has an "Aboard" bool is told (an aircraft's landing pose, say).
        SetAnimatorBool("Aboard", true);
    }

    static bool Finite(Vector3 v) {
        return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }

    public bool HasAnimatorParameter(string name) {
        if (animator == null) return false;
        foreach (AnimatorControllerParameter p in animator.parameters) if (p.name == name) return true;
        return false;
    }

    /// <summary>The plane that dropped it, while it comes down (animation only), and the parachute it wears.</summary>
    [System.NonSerialized] Unit dropFrom;
    [System.NonSerialized] GameObject chute;

    void EndDrop() {
        dropFrom = null;
        if (chute != null) { Destroy(chute); chute = null; }
    }

    /// <summary>The cargo slot its sprite sits in aboard a transport (SettleAboard); -1 when not aboard.</summary>
    [System.NonSerialized] public int cargoSlot = -1;

    /// <summary>The transport this unit was put down from, while its sprite still rides it to the end of its move (animation only).</summary>
    [System.NonSerialized] Unit disembarkFrom;

    /// <summary>
    /// Unloading (Transporting.Unload calls this): everything OnBoarded did, undone; the unit walks to
    /// its tile. The rules have it on its tile at once; on screen, if the transport is still driving
    /// to where it unloads, the unit rides in it until it stops, and only then gets off.
    /// </summary>
    public void OnUnloaded(Unit transport) {
        boardingWalk.Clear();
        disembarkFrom = transport;
        if (transport != null && transport.path != null && transport.path.Count > 0 && transport.Strength > 0) return;
        FinishDisembark();
    }

    /// <summary>The getting off, on screen: back on the board at full size, walking to its tile.</summary>
    void FinishDisembark() {
        Unit transport = disembarkFrom;
        disembarkFrom = null;
        cargoSlot = -1;
        foreach (GameObject g in hiddenUI) if (g != null) g.SetActive(true);
        hiddenUI.Clear();
        SetAnimatorBool("Aboard", false);
        // ... and the strength pips whatever happened in between (a supply unit was put down without them).
        foreach (Transform t in GetComponentsInChildren<Transform>(true)) if (t != transform && t.name == "HealthCircles" && !t.gameObject.activeSelf) t.gameObject.SetActive(true);
        transform.SetParent(null, true);
        transform.rotation = Quaternion.identity;
        if (cargoGroup != null) { Destroy(cargoGroup); cargoGroup = null; }
        // Put down from a plane: it appears a little above its tile, wearing the plane's parachute if it has one,
        // and drifts down onto it (Update).
        if (transport != null && tile != null && tile.unitPlacement != null && LocomotionRules.IsAirborne(transport.locomotion) && !LocomotionRules.ResuppliedInSupply(transport.locomotion)) {
            dropFrom = transport;
            transform.position = tile.unitPlacement.position + Vector3.up * transport.dropHeight;
            if (scaleKnown) transform.localScale = homeScale;
            if (path != null) path.Clear();
            // One parachute for every plane (GameAssets.Parachute: Resources/Units/Prefabs/Parachute); a transport's own overrides it.
            GameObject chutePrefab = transport.parachutePrefab != null ? transport.parachutePrefab : GameAssets.Parachute;
            if (chutePrefab != null) chute = GameObject.Instantiate(chutePrefab, transform.position, Quaternion.identity, transform);
        }
        if (Hub != null && transport != null) {
            bool still = false;
            foreach (Unit c in transport.transported) if (c != null && c != this && c.Hub != null) still = true;
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
        if (GameController.gc != null && GameController.gc.headless) { Destroy(gameObject); return; }
        // Its own "Ditch" animation if it has one, else its death as if shot.
        bool hasDitch = false;
        if (animator != null) foreach (AnimatorControllerParameter p in animator.parameters) if (p.name == "Ditch") hasDitch = true;
        Die(hasDitch ? "Ditch" : "Die");
    }

    /// <summary>
    /// Move to <paramref name="hexTile"/> along the path the tiles' parent chain gives (left there by
    /// CheckMovementRange), walking it on screen. False if the unit did not get there: it ran into an
    /// enemy it did not see (fog of war; see <see cref="MoveAlong"/>), which ends its move.
    /// </summary>
    internal bool CompleteMove(HexTile hexTile, bool thenAttacks = false) {
        tile.parent = null;
        List<HexTile> path = hexTile.buildpath();
        bool arrived = MoveAlong(path, thenAttacks, true);
        HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
        return arrived;
    }

    /// <summary>
    /// Carried this turn: aboard a transport (or a transport inside one) that moved. A unit that must move or
    /// attack (not moveAndAttack: artillery) cannot attack after that (CanAttack). Cleared at the end of its turn (Player.EndTurn).
    /// </summary>
    [System.NonSerialized] public bool carriedThisTurn;

    /// <summary>Everything aboard <paramref name="t"/>, and aboard what it carries, marked carriedThisTurn.</summary>
    static void MarkCarried(Unit t) {
        if (t == null || t.transported == null) return;
        foreach (Unit c in t.transported) if (c != null && c != t && !c.carriedThisTurn) { c.carriedThisTurn = true; MarkCarried(c); }
    }

    /// <summary>The enemy that stopped this unit's last move (fog of war), if any. For logs and tests.</summary>
    [System.NonSerialized] public Unit ambushedBy;

    /// <summary>
    /// Move along <paramref name="steps"/> (its first tile the unit's own), animated or not. Fuel is
    /// burnt per tile entered.
    ///
    /// Fog of war: an enemy on the way that this unit's side did not see (the path was planned as if
    /// it were not there) stops it on the last tile before it that is free (a friend's tile cannot be
    /// ended on: it stops short of that too, or does not move at all). The move is over, a planned
    /// attack is off, the enemy is revealed to the mover's side and fires on the mover if it can reach
    /// it — the mover answering as usual (OrderAttack with surprise: the hidden unit always fires
    /// first). Returns true if the unit reached the end of the path.
    /// </summary>
    internal bool MoveAlong(List<HexTile> steps, bool thenAttacks, bool animate) {
        ambushedBy = null;
        if (steps == null || steps.Count == 0 || tile == null) return false;
        // Aboard a transport it does not move by itself: it gets off (Transporting.Unload). (A move from aboard left
        // its new tile naming it as the occupier while it stayed aboard: a ghost there once the transport moved on.)
        if (trasnporter != null) { Debug.LogWarning(name + " was told to move while aboard " + trasnporter.name + ": it stays aboard"); return false; }
        HexTile start = tile;
        int stop = steps.Count - 1;
        Unit ambusher = null;
        if (FogOfWar.On) {
            for (int i = 1; i < steps.Count; i++) {
                Unit o = steps[i] != null ? steps[i].occupier : null;
                if (o != null && o != this && o.owner != owner) { ambusher = o; stop = i - 1; break; }
            }
            if (ambusher != null) while (stop > 0 && steps[stop].occupier != null && steps[stop].occupier != this) stop--;
        }
        HexTile dest = steps[stop];
        int tilesEntered = Mathf.Max(0, stop);
        // Movement burns fuel per tile entered; air units also burn minFuelPerTurn every turn
        // they are away from base (Player.StartTurn).
        if (fuelMax > 0) fuel = Mathf.Max(0, fuel - tilesEntered * minFuelPerTile);

        if (dest != start) {
            MarkCarried(this);
            if (spriteTile == null) spriteTile = start;
            if (tile.occupier == this) tile.occupier = null;
            tile = dest;
            tile.occupier = this;
        }
        if (animate) {
            path = steps.GetRange(0, stop + 1);
            if (animator != null) animator.SetBool("Moving", true);
        }
        // A move that was to end in an attack and was stopped is a plain move.
        if (!thenAttacks || ambusher != null) ServiceOnLanding();

        // Carried units ride along rather than being left behind on the old tile.
        SetCargoTile(dest);
        FogOfWar.Changed();

        if (ambusher != null) {
            ambushedBy = ambusher;
            FogOfWar.Reveal(ambusher, owner);
            if (ambusher.Strength > 0 && ambusher.tile != null && CombatMath.InFireRange(ambusher, ambusher.tile, dest, this)
                && ambusher.CanAttack(this) && GameController.Targetable(this))
                ambusher.OrderAttack(this, true);
            if (RanInto != null) RanInto(this, ambusher, dest);
            return false;
        }
        return dest == steps[steps.Count - 1];
    }

    /// <summary>A unit ran into an enemy it did not see (fog of war): mover, the hidden unit, where the mover stopped. For logs.</summary>
    public static event System.Action<Unit, Unit, HexTile> RanInto;

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
    }

    /// <summary>
    /// Would ending this turn's move on <paramref name="end"/> leave this unit unable to get back to a building
    /// of ours that services it before it runs dry (a fuel-burning aircraft or ship)? It would start next turn
    /// with its fuel less the tiles flown there (minFuelPerTile each) and, airborne, a turn's burn
    /// (minFuelPerTurn); out of fuel then, it ditches; otherwise it must be no further from such a building (or from
    /// a carrier of ours that would take it in and refuel it: AIAir.Carriers) than that fuel takes it. Ending on such a building refuels it there (ServiceOnLanding): no warning.
    /// </summary>
    public bool WouldDitchFrom(HexTile end) {
        if (end == null || tile == null || fuelMax <= 0) return false;
        if (end.building != null && end.building.owner == owner && end.building.Services(this)) return false;
        if (locomotion != Locomotion.Jet) return false;
        int flown = tile.Distance(end);
        int left = fuel - flown * minFuelPerTile - (LocomotionRules.IsAirborne(locomotion) ? minFuelPerTurn : 0);
        if (left <= 0) return true;
        if (minFuelPerTile <= 0) return false;
        int reach = left / minFuelPerTile;
        int nearest = int.MaxValue;
        foreach (Building b in Building.all) {
            if (b == null || b.owner != owner || !b.Services(this)) continue;
            if (b.tile != null) nearest = Mathf.Min(nearest, b.tile.Distance(end));
        }
        // A carrier of ours it could land on and be refuelled aboard (afloat, room for it, stocked) is a base too,
        // where it is now.
        foreach (Unit c in HexTactica.AI.AIAir.Carriers(this)) nearest = Mathf.Min(nearest, c.tile.Distance(end));
        return nearest > reach;
    }

    /// <summary>Everything aboard (and aboard that, and so on) is where this transport is.</summary>
    public void SetCargoTile(HexTile t) {
        for (int i = 0; i < transported.Count; i++) {
            if (transported[i] == null) continue;
            transported[i].tile = t;
            transported[i].SetCargoTile(t);
        }
    }

    /// <summary>
    /// The steps from this unit's tile to <paramref name="dest"/> along <paramref name="field"/>'s paths (its own
    /// tile first), or null when the field does not reach it.
    /// </summary>
    internal List<HexTile> PathIn(HexTactica.AI.MoveField field, HexTile dest) {
        if (field == null || dest == null || tile == null || !field.Reachable(dest)) return null;
        List<HexTile> steps = new List<HexTile>();
        for (HexTile t = dest; t != null && steps.Count < 256; t = t == tile ? null : field.ParentOf(t)) steps.Insert(0, t);
        return steps.Count > 0 && steps[0] == tile ? steps : null;
    }

    /// <summary>
    /// Go to <paramref name="dest"/>, <paramref name="tilesEntered"/> tiles away along <paramref name="field"/>: in
    /// play, walked along the field's path, tile by tile, as a player's move is (a sprite sent straight there by
    /// TeleportTo slid across whatever stood in between, enemy units included); headless, TeleportTo.
    /// </summary>
    internal void TravelTo(HexTile dest, HexTactica.AI.MoveField field, int tilesEntered, bool thenAttacks = false) {
        List<HexTile> steps = GameController.gc != null && !GameController.gc.headless ? PathIn(field, dest) : null;
        if (steps != null && steps.Count > 1) MoveAlong(steps, thenAttacks, true);
        else TeleportTo(dest, tilesEntered, thenAttacks);
    }

    /// <summary>
    /// Walk <paramref name="steps"/> (its own tile first) to a tile beside <paramref name="transport"/> and board it
    /// from there — the player's "get in" and the AI's alike. The last step may hold a friendly unit: it is only passed
    /// through on the way in (Transporting.CanBoardFromTile). Fuel is burnt per tile entered, as a move. Fog of war:
    /// an enemy on the way that its side did not see stops it as a move does (MoveAlong) and it does not board.
    /// Returns true if it boarded.
    /// </summary>
    internal bool WalkAndBoard(List<HexTile> steps, Unit transport, bool animate) {
        if (steps == null || steps.Count == 0 || tile == null || steps[0] != tile || transport == null) return false;
        HexTile from = steps[steps.Count - 1];
        if (!Transporting.CanBoardFromTile(transport, this, from)) return false;
        if (FogOfWar.On) {
            for (int i = 1; i < steps.Count; i++) {
                Unit o = steps[i] != null ? steps[i].occupier : null;
                if (o != null && o != this && o.owner != owner) { MoveAlong(steps, false, animate); return false; }
            }
        }
        int entered = steps.Count - 1;
        if (fuelMax > 0) fuel = Mathf.Max(0, fuel - entered * minFuelPerTile);
        if (animate && entered > 0) {
            if (spriteTile == null) spriteTile = tile;
            path = new List<HexTile>(steps);   // OnBoarded plays it out before stepping in
            if (animator != null) animator.SetBool("Moving", true);
        }
        return Transporting.BoardFrom(transport, this, from);
    }

    /// <summary>Move without animation or tile-state churn. Used by the headless harness.</summary>
    internal void TeleportTo(HexTile hexTile, int tilesEntered, bool thenAttacks = false) {
        if (trasnporter != null) { Debug.LogWarning(name + " was told to move while aboard " + trasnporter.name + ": it stays aboard"); return; }
        // Movement burns fuel per tile entered; air units also burn minFuelPerTurn every turn
        // they are away from base (Player.StartTurn).
        if (fuelMax > 0) fuel = Mathf.Max(0, fuel - tilesEntered * minFuelPerTile);
        if (tile != null && tile.occupier == this) tile.occupier = null;
        tile = hexTile;
        if (hexTile != null) hexTile.occupier = this;
        if (!thenAttacks) ServiceOnLanding();
        SetCargoTile(hexTile);
        FogOfWar.Changed();
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
        // Move or attack: a unit that cannot do both, carried somewhere this turn, has had its move.
        if (!moveAndAttack && carriedThisTurn) return false;
        if (ammoMax > 0 && ammo <= 0) return false;
        if (Firepower <= 0) return false;
        return true;
    }

    /// <summary>
    /// Can this unit attack <paramref name="opposition"/> at all, wherever they stand: able to fire now
    /// (strength, ammunition, firepower), the target's locomotion one it can attack (attacksLocomotion), and
    /// doing damage. Not range: see CanAttackFrom.
    /// </summary>
    public bool CanAttack(Unit opposition) {
        if (opposition == null || opposition.tile == null) return false;
        // Aboard a transport: out of reach (the transport is what stands on the tile).
        if (opposition.trasnporter != null && opposition.trasnporter != opposition) return false;
        if (!CanAttack(opposition.tile)) return false;
        if (!CanTarget(opposition)) return false;
        return calculateDamage(this, opposition, opposition.tile, 0) > 0;
    }

    /// <summary>Can this unit attack <paramref name="opposition"/> from <paramref name="from"/>: CanAttack, and in range from there.</summary>
    public bool CanAttackFrom(HexTile from, Unit opposition) {
        if (from == null || !CanAttack(opposition)) return false;
        return CombatMath.InFireRange(this, from, opposition.tile, opposition);
    }

    /// <summary>Is <paramref name="target"/>'s locomotion one this unit can attack (attacksLocomotion)?</summary>
    public bool CanTarget(Unit target) {
        return target != null && attacksLocomotion != null && attacksLocomotion.Contains(target.locomotion);
    }

    /// <summary>True once the unit has nothing left to fight with.</summary>
    public bool OutOfAmmo { get { return ammoMax > 0 && ammo <= 0; } }
    public bool OutOfFuel { get { return fuelMax > 0 && fuel <= 0; } }
    public bool OutOfSupplies { get { return suppliesMax > 0 && supplies <= 0; } }



    internal void updateThreat() {
        int valueGain = 0;
        List<HexTile> moves = ThreatReach.Of(this);
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

    /// <summary>A transport carrying supply units re-solves its side's supply when it finishes acting (its cargo's supply moved with it).</summary>
    public static bool ResupplyWithCargo = true;
    /// <summary>Any unit's death re-solves every side's supply (it may have been blocking a corridor), not only a supply unit's its own.</summary>
    public static bool ResupplyOnAnyDeath = true;

    /// <summary>Does <paramref name="u"/> carry a supply unit (aboard it, or aboard what it carries)?</summary>
    static bool CarriesSupply(Unit u) {
        if (u == null || u.transported == null) return false;
        foreach (Unit c in u.transported) if (c != null && (c.Hub != null || CarriesSupply(c))) return true;
        return false;
    }

    internal void FinishActive() {
        GameController.gc.activeUnit = null;
        active = false;
        // Infantry that ended its action on a building without having attacked captures it now (not as the turn ends).
        Building.CaptureBy(this);
        FogOfWar.Refresh();
        GameController.RecalculateThreat();
        // In play, every side's supply: any unit that moved may now stand in (or have left) a corridor of the
        // other side's network — an enemy standing in it severs it — and the borders on screen must show that now,
        // not at the next supply unit's move. (The headless harness keeps to what the rules need below; the AI
        // solves supply on its own copies of the board.)
        if (GameController.gc != null && !GameController.gc.headless && GameController.gc.players != null && BoardView.current == null && !Player.InUpkeep) {
            foreach (Player p in GameController.gc.players) if (p != null) p.Resupply();
        }
        // Its supply moved with it: a supply unit, or a transport carrying one (its cargo rode along).
        else if (Hub || (ResupplyWithCargo && CarriesSupply(this))) {
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
    /// <summary>An attack is about to be resolved: attacker, defender, and whether it is a surprise (a hidden unit run into, fog of war). For logs and tests.</summary>
    public static event System.Action<Unit, Unit, bool> Attacking;
    /// <summary>A unit finished its action (moved, attacked, or both). For logs.</summary>
    public static event System.Action<Unit> Finished;

    /// <summary>
    /// Attack <paramref name="defender"/>. <paramref name="surprise"/>: an enemy that was run into
    /// under fog of war fires first whatever the ground (no ambush by the unit it surprised).
    /// </summary>
    internal CombatOutcome OrderAttack(Unit defender, bool surprise = false) {
        if (Attacking != null) Attacking(this, defender, surprise);
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
        CombatOutcome o = CombatMath.ResolveUnderFog(this, defender, tile, surprise);
        // Firing gives a unit away to the side it fires on (fog of war).
        FogOfWar.Reveal(this, defender.owner);

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

        // Indirect fire that would have driven it back suppresses it where it stands.
        if (o.defenderSuppressed && !o.defenderDies) defender.suppressed += o.suppressionAdded;
        defender.suppressed = Mathf.Clamp(defender.suppressed, 0, Mathf.Max(0, defender.Strength));
        suppressed = Mathf.Clamp(suppressed, 0, Mathf.Max(0, Strength));

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

        FogOfWar.Changed();
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
        if (to == null || Strength <= 0 || trasnporter != null) return;
        if (to.occupier != null) return;
        if (!CanEnter(to)) return;

        if (spriteTile == null) spriteTile = tile;
        if (tile != null && tile.occupier == this) tile.occupier = null;
        tile = to;
        to.occupier = this;
        SetCargoTile(to);
        FogOfWar.Changed();
        // Empty path: Update() glides the sprite to the new tile at moveSpeed. Still walking (run
        // into a hidden enemy and driven back by its fire, under fog of war): it finishes the walk
        // first, is hit where it stopped, and then slides back.
        if (!FogOfWar.On) path.Clear();
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
    /// <summary>A unit is dying (before it leaves the board: its tile, owner and strength are still as they were).</summary>
    public static event System.Action<Unit> Died;

    /// <summary>
    /// The board is being wiped (a scenario or save about to be loaded, the map editor's New): units taken off are not
    /// lost in the game — no Died event, no victory check, no resupply, and cargo is removed with its transport rather
    /// than playing its death (no animation, explosion or sound).
    /// </summary>
    public static bool Wiping;

    private void DieActual() {
        if (dead) return;
        dead = true;
        if (Wiping) { Wipe(); return; }
        if (Died != null) Died(this);
        // Dying aboard: it stays small where it rode (Update does not grow it back) and plays its own death there,
        // not taken down unseen with its transport's sprite (FallAboard).
        if (trasnporter != null) diedAboard = true;
        SupplyPoint ownHub = Hub;
        if (ownHub != null && ownHub.producer && owner != null) owner.hadHQ = true;
        Strength = 0;
        if (owner != null) owner.units.Remove(this);
        if (UnitController.uc != null) UnitController.uc.units.Remove(this);
        // Anything being carried goes down with the transport.
        for (int i = transported.Count - 1; i >= 0; i--) {
            Unit c = transported[i];
            if (c == null) continue;
            c.DieActual();
            if (GameController.gc == null || !GameController.gc.headless) c.FallAboard();
        }
        transported.Clear();
        if (trasnporter != null) { trasnporter.transported.Remove(this); trasnporter = null; }
        if (tile != null && tile.occupier == this) tile.occupier = null;
        tile = null;
        FogOfWar.Changed();
        // Losing an HQ unit ends the game for its owner immediately, not at end of turn.
        if (GameController.gc != null) GameController.gc.CheckVictory();
        // Every side's supply: a supply unit lost shrinks its own; any unit lost may open a corridor it was
        // blocking for the other side (an enemy standing in it severs it). Cargo goes down with it first, then this.
        // (Not in the middle of a side's turn-start upkeep: that feeds every unit from the field solved when it began —
        // a forward base spending its last point of store still feeds that turn — and re-solves when it is done.)
        if (ResupplyOnAnyDeath && BoardView.current == null && GameController.gc != null && GameController.gc.players != null && (trasnporter == null || Hub != null) && !Player.InUpkeep) {
            foreach (Player p in GameController.gc.players) if (p != null) p.Resupply();
        } else {
            if (ResupplyOnAnyDeath && Player.InUpkeep) Player.ResupplyAfterUpkeep = true;
            if (Hub) owner.Resupply();
        }
    }

    /// <summary>Lost outside combat in a recorded game played again (starved, out of fuel, its side out): as Perish.</summary>
    internal void ReplayLoss() { Perish(); }

    /// <summary>Off the board while it is wiped (Wiping): out of every list, its cargo with it, nothing played.</summary>
    void Wipe() {
        Strength = 0;
        if (owner != null) owner.units.Remove(this);
        if (UnitController.uc != null) UnitController.uc.units.Remove(this);
        for (int i = transported.Count - 1; i >= 0; i--) {
            Unit c = transported[i];
            if (c == null) continue;
            c.DieActual();
            Destroy(c.gameObject);
        }
        transported.Clear();
        if (trasnporter != null) { trasnporter.transported.Remove(this); trasnporter = null; }
        if (tile != null && tile.occupier == this) tile.occupier = null;
        tile = null;
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
        PlaySound(fireSound);
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
    bool deathPlayed;

    /// <summary>The death on screen, once: the explosion and the animation trigger (whose end, DeathResolve, removes the unit). Removed anyway after a few seconds, in case the animation never ends.</summary>
    /// <summary>Died aboard a transport (DieActual): kept at the size it rode at.</summary>
    [System.NonSerialized] bool diedAboard;

    /// <summary>
    /// Its transport died with it aboard: off the transport's sprite (so it is not destroyed with it), where and as
    /// small as it rode, and its own death played there.
    /// </summary>
    void FallAboard() {
        if (this == null || gameObject == null) return;
        transform.SetParent(null, true);
        if (cargoGroup != null) { Destroy(cargoGroup); cargoGroup = null; }
        Die();
    }

    private void Die(string trigger = "Die") {
        if (deathPlayed) return;
        deathPlayed = true;
        // Died aboard (starved, say): off the transport's sprite where it rode, staying small, so it plays its own death.
        if (diedAboard && transform.parent != null && (GameController.gc == null || !GameController.gc.headless)) {
            transform.SetParent(null, true);
            if (cargoGroup != null) { Destroy(cargoGroup); cargoGroup = null; }
        }
        // Off the board and out of every list first, whatever brought it here (a unit whose strength
        // reached 0 some other way was destroyed while still listed, and saving the board then
        // read a destroyed unit).
        DieActual();
        if (animator != null) animator.SetTrigger(trigger);
        // Not where anyone watching can see it (fog of war): no explosion to give it away.
        FogFader fader = GetComponent<FogFader>();
        if (deathExplosion != null && (fader == null || fader.Shown)) GameObject.Instantiate(deathExplosion, transform.position, transform.rotation);
        if (dieSound != null && SoundsOn && (fader == null || fader.Shown)) GameSounds.Play(dieSound);
        Destroy(gameObject, 6f);
    }

    public void DeathResolve() {
        Destroy(gameObject);
    }

    internal int displayAttackOdds(Unit attacker) {
        return displayAttackOdds(attacker, this.tile, 0);
    }

    /// <param name="attackerAt">Where the attacker fires from (default: where it stands); <paramref name="defenderAt"/>
    /// where the defender stands (default: <paramref name="tile"/>, else where it stands). Only for a building's
    /// garrison bonuses (Building.attackBonus, defenseBonus): <paramref name="tile"/> is for cover, and null for none.</param>
    internal static int calculateDamage(Unit attacker, Unit defender, HexTile tile, int firstStrike, HexTile attackerAt = null, HexTile defenderAt = null) {
        if (attacker.Strength - firstStrike <= 0)
            return 0;
        // Not something it can attack (artillery at a jet): no damage, and no return fire either way.
        if (!attacker.CanTarget(defender)) return 0;
        float cover = 0;
        Building atk = Building.GarrisonFor(attacker, attackerAt ?? attacker.tile);
        Building def = Building.GarrisonFor(defender, defenderAt ?? tile ?? defender.tile);
        float destruction = Mathf.Max(0, attacker.Firepower + attacker.Veterancy + (atk != null ? atk.attackBonus : 0) + defender.Vulnerability - (def != null ? def.defenseBonus : 0));
        float penetration = Mathf.Max(1, defender.Armor - attacker.Piercing);
        float aquire = Mathf.Max(1, defender.Evasion - attacker.Targeting);
        float toughness = 6;
        if (tile != null && tile.terrain != null && !LocomotionRules.IsAirborne(defender.locomotion) && !attacker.ignoresCover)
            cover = Math.Min(defender.Concealment, tile.EffectiveCover);
        int minDamage = 1;
        return (int) Mathf.Max(minDamage, Mathf.Floor(
            (destruction / penetration / aquire - cover)
            * (attacker.Strength - firstStrike) / toughness));
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
        return position.occupier != null && position.occupier.owner != owner && GameController.Targetable(position.occupier)
            && FogOfWar.CanTarget(owner, position.occupier) && CanTarget(position.occupier);
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
    /// <summary>Suppressed strength pips (zero in older saves, which is right: nothing was suppressed).</summary>
    public int suppressed;
    public UnitData(Unit t) {
        suppressed = t.suppressed;
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

    /// <summary>
    /// Every unit of a save or scenario, by its place in the file (null: none made — an HQ that became a building, or a
    /// unit the game cannot find). Units on the board first, then cargo straight into its transport (a file may list
    /// cargo before its transport, on the same tile: placed first, it would take the transport's tile), cargo of cargo
    /// after that. Cargo whose transport is missing goes on its tile, if free.
    /// </summary>
    public static List<Unit> LoadAll(List<UnitData> all) {
        Unit[] made = new Unit[all.Count];
        bool[] done = new bool[all.Count];
        for (int i = 0; i < all.Count; i++)
            if (all[i].transporterIndexPlusOne <= 0) { made[i] = all[i].load(); done[i] = true; }
        for (bool progress = true; progress; ) {
            progress = false;
            for (int i = 0; i < all.Count; i++) {
                if (done[i]) continue;
                int t = all[i].transporterIndexPlusOne - 1;
                bool bad = t < 0 || t >= all.Count || t == i;
                if (!bad && !done[t]) continue;   // its transport is cargo itself, not yet made
                Unit transport = bad ? null : made[t];
                if (transport == null) Debug.LogError("Scenario unit '" + all[i].gameobject_name + "' was aboard unit " + t + ", which was not made: put on its tile instead");
                made[i] = all[i].load(transport);
                done[i] = true; progress = true;
            }
        }
        for (int i = 0; i < all.Count; i++) if (!done[i]) { Debug.LogError("Scenario unit '" + all[i].gameobject_name + "' is aboard a loop of transports: put on its tile"); made[i] = all[i].load(); }
        return new List<Unit>(made);
    }

    public Unit load() { return load(null); }

    /// <summary>This unit, on its tile — or aboard <paramref name="transport"/> when given.</summary>
    public Unit load(Unit transport) {
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
        // A unit the game cannot find, or whose tile is taken, is left out (said so) — not the rest of the board with it.
        Unit prefab = LevelEditor.UnitFor(player, gameobject_name);
        if (prefab == null || (transport == null && tile == null)) {
            Debug.LogError("Scenario unit '" + gameobject_name + "' at " + x + "," + y + "," + z + (prefab == null ? ": no unit prefab of that name in Resources/Units or the LevelEditor's lists" : ": no such tile") + " — left out");
            return null;
        }
        Unit ut = transport != null ? UnitController.uc.CreateAboard(player, prefab, transport) : UnitController.uc.CreateUnit(player, prefab, tile);
        if (ut == null) { Debug.LogError("Scenario unit '" + gameobject_name + "' at " + x + "," + y + "," + z + ": its tile is taken by " + (tile.occupier != null ? tile.occupier.name : "?") + " — left out"); return null; }
        ut.Strength = Strength;
        ut.Veterancy = Veterancy;
        ut.personal_name = personalName;
        ut.active = active;
        ut.supplies = supplies;
        ut.fuel = fuel;
        ut.ammo = ammo;
        ut.suppressed = Mathf.Clamp(suppressed, 0, Strength);
        return ut;
    }
}