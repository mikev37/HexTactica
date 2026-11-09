using System;
using System.Collections.Generic;
using UnityEngine;

// The data of a scenario's script (ScenarioScript): triggers, their conditions and their actions. Saved in the
// scenario file ("script", ScenarioJson) and shown in the inspector (Editor/ScenarioTriggerDrawers).
//
// Enums are saved by number: every value has an explicit one, new values go at the end, and a number is never
// reordered, renumbered or reused (scenario files store the integer). To retire a value, delete it and leave a comment
// that its number is unused.

/// <summary>What a trigger waits for. Saved by number: append only (see the top of this file).</summary>
public enum TriggerCondition {
    /// <summary>The first moment of play (once the pre-game screen has gone).</summary>
    LevelStart = 0,
    /// <summary>Units matching the unit filter have moved (or appeared) into the area: Count times.</summary>
    UnitEnters = 1,
    /// <summary>Units matching the unit filter have died (any way): Count of them.</summary>
    UnitKilled = 2,
    /// <summary>A building matching the building filter has been taken by Side (captured, or given by a trigger): Count times.</summary>
    BuildingCaptured = 3,
    /// <summary>A building matching the building filter has been destroyed (its health ran out): Count of them.</summary>
    BuildingDestroyed = 4,
    /// <summary>The day shown on screen ("day 3") is Value or later, on Side's turn (Any: whoever's turn it is).</summary>
    DayReached = 5,
    /// <summary>At least Count units matching the unit filter stand within Radius of the tile (now).</summary>
    UnitsInArea = 6,
    /// <summary>Side has at least Value gold (now).</summary>
    MoneyAtLeast = 7,
    /// <summary>Side has at most Value gold (now).</summary>
    MoneyAtMost = 8,
    /// <summary>Units matching the unit filter have been bought at a building: Count of them.</summary>
    UnitBuilt = 9,
    /// <summary>A unit matching the unit filter lost strength and was left at Value strength or less (0: destroyed): Count times.</summary>
    UnitDamaged = 10,
    /// <summary>Side's turn has ended: Count times.</summary>
    TurnOver = 11,
    /// <summary>A unit matching the unit filter gained strength and reached Value strength or more: Count times.</summary>
    UnitHealed = 12,
    /// <summary>At least Count units matching the unit filter are out of supplies (they lose strength every turn).</summary>
    UnitStarving = 13,
    /// <summary>At least Count units matching the unit filter are out of ammunition.</summary>
    UnitOutOfAmmo = 14,
    /// <summary>At least Count units matching the unit filter are out of fuel.</summary>
    UnitOutOfFuel = 15,
    /// <summary>At least Count units matching the unit filter have Value percent of a full tank or less (and some left).</summary>
    UnitLowOnFuel = 16,
    /// <summary>The trigger named After Trigger has fired at least once.</summary>
    AfterTrigger = 17,
    /// <summary>Every trigger named in Parts has its condition met now (AND).</summary>
    AllOf = 18,
    /// <summary>At least one trigger named in Parts has its condition met now (OR).</summary>
    AnyOf = 19,
    /// <summary>None of the triggers named in Parts has its condition met now (NOT / NOR).</summary>
    NoneOf = 20,
    /// <summary>Side's turn has begun (after its upkeep): Count times.</summary>
    TurnStarted = 21,
    /// <summary>At most Value units matching the unit filter are alive (0: all of them are gone).</summary>
    UnitsAtMost = 22,
    /// <summary>At least Count buildings matching the building filter are held by Side (now).</summary>
    BuildingOwned = 23,
    /// <summary>At least Count units matching the unit filter stand outside their side's supply (now).</summary>
    UnitCutOff = 24,
    /// <summary>At least Value units matching the unit filter are alive (now): "when you have 3 tanks".</summary>
    UnitsAtLeast = 25,
}

/// <summary>What a trigger does. Saved by number: append only (see the top of this file).</summary>
public enum TriggerActionType {
    /// <summary>Show Lines in the dialogue box, one after the other; the game is held until the last is closed.</summary>
    Dialogue = 0,
    /// <summary>Move the camera to the tile (and zoom to Zoom, 0 keeping the zoom) over Seconds.</summary>
    CameraFocus = 1,
    /// <summary>Show the marker prefab Marker on the tile, with Text, known by Marker Id.</summary>
    ShowMarker = 2,
    /// <summary>Remove the marker with Marker Id (empty: every marker).</summary>
    HideMarker = 3,
    /// <summary>Play the music track Track from Resources/Music/Special (empty: one at random), looping if Loop.</summary>
    PlayMusic = 4,
    /// <summary>Back from a special track to the game's own music.</summary>
    StopMusic = 5,
    /// <summary>The game ends: Side wins.</summary>
    Win = 6,
    /// <summary>The game ends: Side loses (the other side wins).</summary>
    Lose = 7,
    /// <summary>Make Count units of Unit Kind for Side on the tile (beside it if taken), with Value strength and Veterancy.</summary>
    SpawnUnit = 8,
    /// <summary>The units matching the unit filter change hands to Side.</summary>
    GiveUnits = 9,
    /// <summary>The buildings matching the building filter change hands to Side.</summary>
    GiveBuilding = 10,
    /// <summary>The terrain within Radius of the tile is destroyed (a bridge falls, a forest burns).</summary>
    DestroyTile = 11,
    /// <summary>The terrain within Radius of the tile is put back as it was before it was destroyed, undamaged.</summary>
    RepairTile = 12,
    /// <summary>The buildings matching the building filter are destroyed.</summary>
    DestroyBuilding = 13,
    /// <summary>The buildings matching the building filter are repaired fully.</summary>
    RepairBuilding = 14,
    /// <summary>The units matching the unit filter get Value veterancy (Mode Add: that much more), 0 to 3.</summary>
    SetVeterancy = 15,
    /// <summary>The units matching the unit filter get Value of Stat (Mode Add: that much more), within 0 and the unit's most.</summary>
    SetStat = 16,
    /// <summary>The trigger named Trigger is switched on (its counts start again from now).</summary>
    EnableTrigger = 17,
    /// <summary>The trigger named Trigger is switched off.</summary>
    DisableTrigger = 18,
    /// <summary>Side gets Value gold (Mode Set: has exactly Value).</summary>
    GiveMoney = 19,
    /// <summary>The units matching the unit filter are destroyed.</summary>
    DestroyUnits = 20,
    /// <summary>Play the sound Sound (Resources/Dialogue) once.</summary>
    PlaySound = 21,
    /// <summary>The trigger named Trigger may fire again: its fired count and its counts start again from now.</summary>
    ResetTrigger = 22,
    /// <summary>The units matching the unit filter may act again this turn (or, with Value 0, have used their turn).</summary>
    ReadyUnits = 23,
    /// <summary>The trigger's next action waits Seconds of play: pacing within a scene (a pause after a camera move, before a line). For waiting turns, use a trigger's Delay.</summary>
    Wait = 24,
    /// <summary>The camera shows the whole board.</summary>
    CameraFrameBoard = 25,
}

/// <summary>SetStat: which of a unit's values. Saved by number: append only.</summary>
public enum UnitStat { Health = 0, Ammo = 1, Fuel = 2, Supplies = 3 }

/// <summary>SetVeterancy, SetStat, GiveMoney: set to the value, or add it (a negative value takes away). Saved by number: append only.</summary>
public enum ValueMode { Set = 0, Add = 1 }

/// <summary>Which side of the dialogue box a speaker's portrait stands. Saved by number: append only.</summary>
public enum PortraitSide { Left = 0, Right = 1 }

/// <summary>
/// A side in a trigger: a player's id (0, 112, ... as the scenario file has them), or one of these.
/// </summary>
public static class TriggerSide {
    public const long Any = -1;
    /// <summary>Any side a person plays (the first one, where one side is meant: a unit given "to the player").</summary>
    public const long Human = -2;
    /// <summary>Any side an AI plays (the first one, where one side is meant).</summary>
    public const long AI = -3;
    /// <summary>Nobody: a neutral building, or none.</summary>
    public const long Neutral = -4;
}

/// <summary>Marks a field that is the script's progress in a game, not part of the scenario as authored: saved in a game, not in the scenario as authored (ScenarioScript.ToJson(false)).</summary>
[AttributeUsage(AttributeTargets.Field)]
public class ScenarioStateAttribute : Attribute { }

/// <summary>One line of a conversation (TriggerActionType.Dialogue).</summary>
[Serializable]
public class DialogueLine {
    [Tooltip("Who speaks: a Dialogue Character asset in Resources/Characters, by its asset name (its portrait, name and colour). Empty: nobody (no portrait, no name).")]
    [ResourceName("Characters", typeof(DialogueCharacter))] public string Character = "";
    [Tooltip("What they say.")]
    [TextArea(2, 6)] public string Text = "";
    [Tooltip("A sound played with the line (drag a clip in): a voice line or a noise. Empty: silent. It stops when the line is closed, so lines never talk over each other.")]
    [ResourceName("Dialogue", typeof(AudioClip))] public string Sound = "";
    [Tooltip("Where the portrait stands. Left or right of the text.")]
    public PortraitSide Portrait = PortraitSide.Left;
}

/// <summary>One thing a trigger does (TriggerActionType). Every parameter is a plain field: each type uses some (see the tooltips; the inspector shows only those).</summary>
[Serializable]
public class TriggerAction {
    public TriggerActionType Type = TriggerActionType.Dialogue;

    [Tooltip("Dialogue: the lines, shown one after the other; the next action of the trigger waits until the last is closed.")]
    public List<DialogueLine> Lines = new List<DialogueLine>();

    [Tooltip("Camera Focus, Show Marker, Spawn Unit, Destroy Tile, Repair Tile, and the unit and building filters (with In Area): the tile, x (cube coordinates, as the HUD and the scenario file give them).")]
    [TileField] public int TileX;
    [Tooltip("Camera Focus, Show Marker, Spawn Unit, Destroy Tile, Repair Tile, and the unit and building filters (with In Area): the tile, y (z is -x-y).")]
    public int TileY;
    [Tooltip("Destroy Tile, Repair Tile: tiles within this many steps of the tile (0: the tile only). Unit and building filters with In Area: the area's radius.")]
    public int Radius;

    [Tooltip("Camera Focus: the camera's zoom (orthographic size; smaller is closer). 0: keep the zoom.")]
    public float Zoom;
    [Tooltip("Camera Focus: seconds the camera takes to get there (0: at once). Wait: seconds the next action waits.")]
    public float Seconds = 1f;

    [Tooltip("Show Marker: what the marker looks like: a marker prefab (the default ring-and-arrow is Marker; drag in another to use it).")]
    [ResourceName("ScenarioMarkers", typeof(GameObject), ResourceKind.Marker)] public string Marker = "Marker";
    [Tooltip("Show Marker: a name for this marker, so a Hide Marker action can remove it later (optional: without one, only Hide Marker's 'every marker' removes it). Showing a name again moves that marker. Hide Marker: the marker to remove, or every marker.")]
    [MarkerName] public string MarkerId = "";
    [Tooltip("Show Marker: the text shown with the marker (empty: none).")]
    public string Text = "";

    [Tooltip("Play Music: the track in Resources/Music/Special, by its clip's name (empty: one at random).")]
    [ResourceName("Music/Special", typeof(AudioClip))] public string Track = "";
    [Tooltip("Play Music: play it until Stop Music (else the game's music comes back when it ends).")]
    public bool Loop;

    [Tooltip("Win: the side that wins. Lose: the side that loses. Spawn Unit: whose units. Give Units, Give Building: the new owner. Give Money: who gets it. A player's id, or Any (-1), Human (-2: the first side a person plays), AI (-3), Neutral (-4: nobody; buildings only).")]
    [SideChoice(true)] public long Side = TriggerSide.Human;

    [Header("Which units (Give Units, Destroy Units, Set Veterancy, Set Stat, Ready Units)")]
    [Tooltip("Unit filter: whose units (a player's id, Any -1, Human -2, AI -3).")]
    [SideChoice(false)] public long UnitSide = TriggerSide.Any;
    [Tooltip("Spawn Unit: the unit prefab to make (by name: Grunt, Tank ...; a side with a faction gets its version). Unit filter: only units of this kind (empty: any kind; a faction's version counts as its base unit).")]
    [ResourceName("Units", typeof(Unit), ResourceKind.Unit)] public string UnitKind = "";
    [Tooltip("Spawn Unit: the name the new units go by (their personal name), so later triggers can pick them out. Unit filter: only units of this name (empty: any).")]
    [UnitName] public string UnitTag = "";
    [Tooltip("Unit and building filters: only those within Radius of the tile (off: anywhere on the board).")]
    public bool InArea;

    [Header("Which buildings (Give Building, Destroy Building, Repair Building)")]
    [Tooltip("Building filter: whose buildings (a player's id, Any -1, Human -2, AI -3, Neutral -4).")]
    [SideChoice(true)] public long BuildingSide = TriggerSide.Any;
    [Tooltip("Building filter: only buildings of this kind (the building prefab's name: City, HQ, ...; empty: any). With In Area and Radius 0: the building on the tile.")]
    [ResourceName("Buildings", typeof(Building), ResourceKind.Building)] public string BuildingKind = "";

    [Tooltip("Spawn Unit: how many to make (each on the nearest free tile it can stand on).")]
    public int Count = 1;
    [Tooltip("Spawn Unit: their strength (1 to 6). Set Veterancy, Set Stat, Give Money: the value (or what is added, Mode Add). Ready Units: 1 may act, 0 has used its turn.")]
    public int Value = 6;
    [Tooltip("Spawn Unit: their veterancy (0 to 3).")]
    public int Veterancy;
    [Tooltip("Spawn Unit: they may act at once (off: they have used this turn, as a unit just bought).")]
    public bool ReadyToAct = true;
    [Tooltip("Set Veterancy, Set Stat, Give Money: set to Value, or add Value.")]
    public ValueMode Mode = ValueMode.Set;
    [Tooltip("Set Stat: which value (Health is strength, 0 to 6; 0 destroys the unit).")]
    public UnitStat Stat = UnitStat.Health;

    [Tooltip("Enable Trigger, Disable Trigger, Reset Trigger: the trigger, by name (empty: this one).")]
    [TriggerName] public string Trigger = "";
    [Tooltip("Play Sound: the sound in Resources/Dialogue, by name.")]
    [ResourceName("Dialogue", typeof(AudioClip))] public string Sound = "";
}

/// <summary>Draws a string field as a choice of this scenario's trigger names (Editor/ScenarioTriggerDrawers).</summary>
public class TriggerNameAttribute : PropertyAttribute { }

/// <summary>What a ResourceName field takes: an asset of its type, or a unit, building or marker prefab (units and buildings: from the board too).</summary>
public enum ResourceKind { Any, Unit, Building, Marker }

/// <summary>
/// Draws a text field as an object field (Editor/ScenarioTriggerDrawers): drag an asset in (a clip, a character, a
/// marker, a unit or building prefab, or a unit or building on the board), and its key is kept as text, which the
/// scenario file can hold (ScenarioAssets.KeyOf). The asset must be in a Resources folder for the game to load it:
/// the inspector offers to move it there.
/// </summary>
public class ResourceNameAttribute : PropertyAttribute {
    public readonly string folder;
    public readonly Type type;
    public readonly ResourceKind kind;
    public ResourceNameAttribute(string folder, Type type, ResourceKind kind = ResourceKind.Any) { this.folder = folder; this.type = type; this.kind = kind; }
}

/// <summary>A marker's name: typed where a Show Marker names it, chosen from those names where a Hide Marker removes one.</summary>
public class MarkerNameAttribute : PropertyAttribute { }

/// <summary>A tile field (x; its y is the next field): drawn with a slot to drag a tile of the board onto, and the last tile clicked.</summary>
public class TileFieldAttribute : PropertyAttribute { }

/// <summary>A unit's name field (a unit filter): drawn with a slot to drag a unit of the board onto (the name is given to that unit if it has none of its own), and a choice of the names in use.</summary>
public class UnitNameAttribute : PropertyAttribute { }

/// <summary>Draws a side field (a player's id, or Any / Human / AI / Neutral) as a choice of the scenario's sides (Editor/ScenarioTriggerDrawers).</summary>
public class SideChoiceAttribute : PropertyAttribute {
    public readonly bool neutral;
    public SideChoiceAttribute(bool neutral) { this.neutral = neutral; }
}

/// <summary>
/// When this happens, do these things: one condition (combine several with All Of / Any Of / None Of, naming other
/// triggers), timing, and an ordered list of actions. Every parameter is a plain field: each condition uses some (see
/// the tooltips; the inspector shows only those).
/// </summary>
[Serializable]
public class ScenarioTrigger {
    [Tooltip("How other triggers name this one (After Trigger, All Of / Any Of / None Of, Enable / Disable / Reset Trigger). Keep it unique.")]
    public string Name = "Trigger";
    [Tooltip("Off: it never fires (until an Enable Trigger action switches it on), but other triggers can still use its condition as a part.")]
    public bool Enabled = true;
    public TriggerCondition Condition = TriggerCondition.LevelStart;

    [Tooltip("All Of, Any Of, None Of: the triggers whose conditions are combined, by name (a trigger switched off works as a named condition).")]
    [TriggerName] public List<string> Parts = new List<string>();
    [Tooltip("After Trigger: the trigger that must have fired, by name.")]
    [TriggerName] public string AfterTrigger = "";

    [Tooltip("Unit conditions: whose units. Building Captured: who took it. Building Owned: who holds it. Money At Least / At Most: whose. Turn Over, Turn Started, Day Reached: whose turn. A player's id, or Any (-1), Human (-2: any side a person plays), AI (-3), Neutral (-4: nobody; Building Owned only).")]
    [SideChoice(true)] public long Side = TriggerSide.Any;
    [Tooltip("Unit conditions: only units of this kind (the unit prefab's name: Grunt, Tank ...; a faction's version counts as its base unit). Empty: any kind.")]
    [ResourceName("Units", typeof(Unit), ResourceKind.Unit)] public string UnitKind = "";
    [Tooltip("Unit conditions: only units of this name (their personal name: a Spawn Unit action can give one). Empty: any.")]
    [UnitName] public string UnitTag = "";
    [Tooltip("Building conditions: only buildings of this kind (the building prefab's name: City, HQ, ...). Empty: any. With In Area and Radius 0: the building on the tile.")]
    [ResourceName("Buildings", typeof(Building), ResourceKind.Building)] public string BuildingKind = "";

    [Tooltip("Unit and building conditions: only within Radius of the tile (off: anywhere). Unit Enters and Units In Area always use the area.")]
    public bool InArea;
    [Tooltip("Unit Enters, Units In Area, and any condition with In Area: the centre tile, x (cube coordinates, as the HUD and the scenario file give them).")]
    [TileField] public int TileX;
    [Tooltip("Unit Enters, Units In Area, and any condition with In Area: the centre tile, y (z is -x-y).")]
    public int TileY;
    [Tooltip("Unit Enters, Units In Area, and any condition with In Area: tiles within this many steps of the centre (0: the tile only).")]
    public int Radius;

    [Tooltip("How many (at least 1): Unit Enters, Unit Killed, Unit Built, Unit Damaged, Unit Healed, Building Captured, Building Destroyed (times it happened); Turn Started, Turn Over (that side's turn number: 3 is its third turn); Units In Area, Unit Starving, Out Of Ammo, Out Of Fuel, Low On Fuel, Cut Off, Building Owned (at least this many now).")]
    public int Count = 1;
    [Tooltip("Day Reached: the day as the screen shows it (from 1). Money At Least / At Most: gold. Unit Damaged: strength left, at most (0: destroyed). Unit Healed: strength reached, at least. Low On Fuel: percent of a full tank, at most. Units At Most: how many units, at most (0: none left). Units At Least: how many units, at least.")]
    public int Value;

    [Header("Timing")]
    [Tooltip("Turns (of the side in Turns Of) the condition must hold, without a break, before it fires: 'hold the bridge for 2 turns'. It fires as the last of those turns starts. If the condition breaks, the count starts over. 0: at once.")]
    public int Delay;
    [Tooltip("Delay, Repeat Every: whose turns are counted (Any side: every turn, whoever's it is).")]
    [SideChoice(false)] public long TurnsOf = TriggerSide.Human;
    [Tooltip("Off: the trigger fires once, ever. On: it fires again each time its condition is met anew (a counted condition starts counting from 0 after each firing, so 'Unit Killed, Repeat' fires on every kill).")]
    public bool Repeat;
    [Tooltip("Repeat: at most once in this many turns (of the side in Turns Of). 0: no limit.")]
    public int RepeatEvery;

    [Header("Actions (in order)")]
    public List<TriggerAction> Actions = new List<TriggerAction>();

    // ---- Progress in a game (saved in a game, not part of the scenario as authored) ----
    [HideInInspector, ScenarioState] public int TimesFired;
    [HideInInspector, ScenarioState] public int Events;
    /// <summary>The turn count (TurnsOf's) when the condition began to hold without a break; -1: it does not hold.</summary>
    [HideInInspector, ScenarioState] public int ConditionMetTurn = -1;
    /// <summary>The turn count (TurnsOf's) when it last fired.</summary>
    [HideInInspector, ScenarioState] public int LastFiredTurn;
}

/// <summary>A marker shown on the board by a Show Marker action (kept in a save).</summary>
[Serializable]
public class ShownMarker {
    public string Id = "";
    public string Marker = "";
    public string Text = "";
    public int TileX, TileY;
}

/// <summary>A trigger's actions still being carried out: waiting on its dialogue, or on a Wait (kept in a save).</summary>
[Serializable]
public class RunningActions {
    public int Trigger;
    public int Next;
    /// <summary>The dialogue line (ScenarioDialogue serial) that must be closed first; 0: none.</summary>
    public int WaitLine;
    /// <summary>The level clock at which a Wait ends; 0: none.</summary>
    public float WaitUntil;
}

/// <summary>How many turns a side has begun in this game (Delay and Repeat Every count them; kept in a save).</summary>
[Serializable]
public class SideTurns { public long Id; public int Count; }

/// <summary>Everything a scenario's script is, as the scenario file keeps it ("script").</summary>
[Serializable]
public class ScenarioScriptData {
    [ScenarioState] public List<SideTurns> TurnStarts = new List<SideTurns>();
    public int Version = 1;
    public List<ScenarioTrigger> Triggers = new List<ScenarioTrigger>();
    [ScenarioState] public bool Started;
    [ScenarioState] public float Clock;
    [ScenarioState] public List<ShownMarker> Markers = new List<ShownMarker>();
    [ScenarioState] public List<RunningActions> Running = new List<RunningActions>();
    [ScenarioState] public List<DialogueLine> PendingLines = new List<DialogueLine>();
}
