# Scenario triggers and dialogue (campaign scripting)

A campaign map is a scenario file with a **script**: a list of triggers, *when this happens, do these things*. The
script is saved in the scenario file (its `"script"` block), so a level is still a scenario file and nothing else.
Triggers are not in the map editor. You edit them in the Unity inspector.

## Editing triggers

**The easiest way is the map editor.** Open the scenario in the map editor and select **Scenario Script** in the
Hierarchy. Edit its triggers in the inspector, then save with the map editor's Save. The units, buildings and tiles
of the map are on the board, so you can drag them straight into the trigger fields.

You never type a key:
- **Assets:** drag the asset into its field: a music clip, a sound, a character, a marker prefab, or a unit or
  building prefab.
- **A specific unit:** drag the unit from the Hierarchy or the Scene view into Unit Name. If it has no name of its
  own, it gets one, such as "Grunt 2", which is saved with the map. The ▾ beside the field lists the names in use,
  including the ones your Spawn Unit actions give.
- **A specific building:** drag it into the kind field. That sets its kind, and its tile with radius 0.
- **A tile:** drag a tile, a unit or a building into the Tile slot, or press **Last clicked**.

An asset dragged from outside a Resources folder can't be loaded by the game. The inspector offers to move it into
the right folder for you, such as `Resources/Music/Special` for music. An asset can also live in a subfolder of any
Resources folder: the file then keeps its path.

Every normal save writes a scenario's triggers, and every load reads them: the map editor's Save, the S key, and
the in-game Save. Like any change made in play, an edit you don't save is lost when play stops.

- While a game is playing, the triggers are live: a change applies at once, and each folded trigger shows how often
  it has fired, which is handy for trying things out.
- The inspector shows only the fields a trigger's condition, or an action's type, uses. A folded trigger reads as one
  sentence: `Ambush: when the player's Tank enters (3,-3) r1, 2 actions`. Other triggers and sides are picked from
  lists. Everything else is dragged in (see above).
- Problems show above the list: a part or an After Trigger naming a trigger that isn't there, two triggers with one
  name, a Dialogue action with no lines. They are also logged when the scenario loads. A trigger whose condition can
  never be met warns once, in play.
- **Areas on the board:** with the Scenario Script selected, the Scene view outlines every area a trigger or action
  uses (yellow for trigger areas, blue for action tiles), with the trigger's name. Drag the dot in an area's centre
  to move it to another tile.
- Tiles are given as cube coordinates `x, y` (z is −x−y): the same ones the scenario file uses.

## Sides

Pick a side from the list, which names the scenario's players, or drag a player (its object in the Hierarchy) onto
the slot beside the list. The list also has:

- **Any side**.
- **The player**: a side a person plays. Where one side is meant, such as "give this unit to the player", it is the
  first side a person plays.
- **The AI**: a side an AI plays.
- **Nobody**: neutral; for buildings only.

## Conditions

Each trigger has one condition. To combine conditions, use **All Of**, **Any Of** and **None Of**. For counted
conditions, *Count* is how many times it must have happened (at least 1). For "now" conditions, *Count* is how many
units or buildings must match at least.

| Condition | Fires when | Fields |
|---|---|---|
| Level Start (0) | the first moment of play, after the pre-game screen | |
| Unit Enters (1) | a matching unit moves, rides, or appears (bought, spawned) into the area: *Count* times | side, kind, name, area |
| Unit Killed (2) | matching units die, in any way (combat, starving, ditched, destroyed by a trigger): *Count* of them | side, kind, name, in area |
| Building Captured (3) | a matching building is taken by *Side*, by capture or by a Give Building action: *Count* times | taken by, kind, in area |
| Building Destroyed (4) | a matching building's health runs out (any owner) | kind, in area |
| Day Reached (5) | the day on screen is *Value* or later (day 1 is the first), on *Side*'s turn (any side: whoever's turn it is) | day, on whose turn |
| Units In Area (6) | now: at least *Count* matching units within the area | side, kind, name, area |
| Money At Least (7) / At Most (8) | now: *Side* has at least / at most *Value* gold | whose, gold |
| Unit Built (9) | matching units are bought at a building: *Count* of them | side, kind, name, in area |
| Unit Damaged (10) | a matching unit loses strength and is left at *Value* or less (0 is destroyed) | side, kind, name, in area, strength |
| Turn Over (11) | *Side*'s turn number *Count* ends (2: the end of its second turn) | on whose turn, turn number |
| Unit Healed (12) | a matching unit gains strength and reaches *Value* or more | side, kind, name, in area, strength |
| Unit Starving (13) | now: matching units out of supplies (they lose a strength each turn) | side, kind, name, in area |
| Unit Out Of Ammo (14) / Out Of Fuel (15) | now: matching units with none left | side, kind, name, in area |
| Unit Low On Fuel (16) | now: matching units with *Value* % of a full tank or less, but some left | side, kind, name, in area, % |
| After Trigger (17) | the named trigger has fired at least once (sequencing) | trigger |
| All Of (18) / Any Of (19) / None Of (20) | the named triggers' conditions hold now: all of them / at least one / none | parts |
| Turn Started (21) | *Side*'s turn number *Count* begins (3: its third turn), after its upkeep | on whose turn, turn number |
| Units At Most (22) | now: at most *Value* matching units are alive (0: all gone, as in "destroy every enemy tank") | side, kind, name, in area |
| Building Owned (23) | now: at least *Count* matching buildings are held by *Side* | held by, kind, in area |
| Unit Cut Off (24) | now: matching units stand outside their side's supply | side, kind, name, in area |
| Unit Selected (26) | the player selects (clicks) a matching unit: *Count* times | side, kind, name, in area |
| Building Selected (27) | the player opens (clicks) a matching building's build list, held by *Side* | held by, kind, in area |
| Unit Spotted (29) | a matching unit comes into *Seen By*'s sight: it was hidden in the fog, or has just appeared where they can see it (*Count* times; with no fog, units are in sight as they appear) | seen by, side, kind, name, in area |
| Unit Spent (28) | now: matching units have used their turn (moved, fired or waited) | side, kind, name, in area |
| Units At Least (25) | now: at least *Value* matching units are alive (as in "when you have 3 tanks"; with no kind, any units) | side, kind, name, in area |

A **kind** is a unit or building prefab's name (Grunt, Tank, City, HQ…). A faction's version of a unit counts as its
base unit. A **name** is a unit's personal name, which Spawn Unit can give its units, so that later triggers can pick
them out. **In area** limits a condition to units or buildings within *Radius* of the tile. With Radius 0, it is the
tile itself, for example *that* city.

**A trigger switched off** never fires, but it can still be a part of All Of / Any Of / None Of. This makes it a
named condition you can reuse. Its counts still go up while it is off.

## Timing

Timing is counted in **turns**:

- **Delay (turns)** is how many turns the condition must keep holding, without a break, before the trigger fires.
  For example, "hold the bridge for 2 turns" is Units In Area with Delay 2. It fires as the last of those turns
  starts. If the condition breaks, even for a moment (the unit is shot off the bridge), the count starts over.
- **Turns Of** says whose turns are counted. The default is the player's; Any side counts every turn, whoever's it
  is.
- **Repeat** off: the trigger fires once, ever. Repeat on: it fires again each time its condition is met anew. A
  counted condition starts counting from 0 after each firing, so *Unit Killed* with Repeat fires on every kill.
  **Repeat Every (turns)** limits it to at most once in that many turns; 0 means no limit.
- For a specific turn or day, use the conditions: *Turn Started* (a side's 3rd turn), *Day Reached* (day 5, on a
  given side's turn), or *After Trigger* to chain triggers in order.
- The only thing measured in seconds is the **Wait** action. It paces a scene within one moment, for example a pause
  after the camera pans before the next line.
- **Wait For Animations** (on by default): a trigger whose condition is met fires only once no unit is walking,
  shooting, retreating or boarding, so "we took damage" comes after the shot has landed. It waits at most a few
  seconds.
- Turn numbers count the first turn too: the side that plays first is on its turn 1 when the level starts.
- A trigger that fires once keeps its count, so another trigger can still use it as a part (None Of ["Opened the HQ"]
  stays false once the HQ was opened). A repeating trigger starts counting from 0 each time it fires.
- **Fire** (on each trigger, while playing) runs its actions at once, whatever its condition: try a scene without
  playing up to it.
- Triggers are checked continuously, in list order, while the game plays. They are not checked during the pre-game
  screen, the map editor, the in-game menu, or a replay.
- An **Enable Trigger** action switches a trigger on. Its counts start from then, so "after the briefing, the next
  kill…" counts only kills after the briefing. **Reset Trigger** lets a trigger fire again.

## Actions (in order)

| Action | Does |
|---|---|
| Dialogue (0) | shows its lines one after another. The game is held until the last line is closed, and the trigger's next action waits until then. |
| Camera Focus (1) / Camera Frame Board (25) | glides the camera to the tile, or to *On Unit* (*Zoom* sets the orthographic size; 0 keeps the zoom) / shows the whole board |
| Show Marker (2) / Hide Marker (3) | puts a marker on the tile, or on *On Unit*: then it follows the unit, and is hidden while the player can't see it / removes it (or every marker) |
| Play Music (4) / Stop Music (5) | plays a track from `Resources/Music/Special` (empty: one at random), optionally looping / returns to the game's own music |
| Win (6) / Lose (7) | ends the game now: *Side* wins / loses (the side left wins) |
| Spawn Unit (8) | makes *Count* units of a kind for *Side* on the tile, each on the nearest free tile it can stand on if the tile is taken. Sets their strength, veterancy and name, and whether they can act this turn. |
| Give Units (9) / Give Building (10) | the matching units / buildings change hands to *Side* (units with their cargo; an HQ given away counts as taken) |
| Destroy Tile (11) / Repair Tile (12) | destroys the terrain within *Radius* (a bridge falls, a forest burns) / puts it back as it was, undamaged |
| Destroy Building (13) / Repair Building (14) | the matching buildings |
| Set Veterancy (15) | the matching units' veterancy, 0 to 3: set it, or add to it (Mode) |
| Set Stat (16) | the matching units' health (strength 0 to 6; 0 destroys the unit), ammunition, fuel or supplies: set it, or add to it |
| Enable / Disable / Reset Trigger (17, 18, 22) | switch a trigger on or off, or let it fire again (empty: this trigger) |
| Give Money (19) | *Side* gets gold, or has exactly that much |
| Destroy Units (20) | the matching units die |
| Play Sound (21) | plays a sound from `Resources/Dialogue` once |
| Ready Units (23) | the matching units can act again this turn (Value 1), or have used their turn (Value 0) |
| Order Units (26) | the matching units move and attack as if ordered: toward the unit in *Attack Unit*, or the nearest unit of *Or Nearest Of*, attacking it if they reach it this turn; with no target, toward the tile. They have then used their turn. (For scripted ambushes: "the raider goes for our grunt".) |
| Wait (24) | the trigger's next action waits *Seconds* (for pacing a scene; to wait turns, use a trigger's Delay) |

If an action matches nothing, it says so once in the console: no unit to give, no side by that name, no such marker
prefab. It never fails silently.

## Dialogue

- `Assets/Resources/Dialogue.prefab` is the dialogue box (`DialogueBox`): a dark, see-through panel with the
  speaker's name and their line, and a **Next** button (Return also works, Space does not, because Space ends the
  turn). The speaker's full-body portrait stands beside the panel, on the side the line asks for. The last speaker on
  the other side stays there, dimmed. The box is made once by HexTactica ▸ Scenario Triggers ▸ Recreate Dialogue
  Prefab. After that it is yours to restyle in Unity: the component finds its parts through its fields. Its settings
  are on the component: button labels, keys, how fast the text is written out, a sound as it opens, voice volume.
- While a line is up, the game is held (`GameController.ScriptHold`). No side plays, the AI waits, the board takes no
  clicks, and the turn can't be ended. The box's full-screen shade catches clicks too.
- **Sounds never overlap.** Each line's sound plays on the box's own audio source and stops when the line closes. If
  two triggers speak at once, their lines are queued and shown one after the other.
- A line names a **character**: a `DialogueCharacter` asset in `Assets/Resources/Characters`, with a display name, a
  portrait and a name colour. It also records which way the person in the portrait looks as drawn, so the box can
  mirror the picture to look toward the text whichever side it stands on. Three placeholders are there for now (`Placeholder Officer`,
  `Placeholder Scout`, `Placeholder Commander`, from `Assets/Sprites/Portraits`). Replace their portraits, or add new
  characters, when the real art exists.
- With no dialogue box (headless tests, or a missing prefab), each line goes to the log and nothing waits. A missing
  prefab is reported once.

## Markers

A marker is a "go here" sign on a tile: a ring round the tile, an arrow bobbing over it, and the action's text
("Take the bridge"). You don't need to make one. `Assets/Resources/ScenarioMarkers/Marker.prefab` is made for you
the first time Unity opens the project, and every Show Marker action uses it unless you drag another prefab into
*Looks Like*.

- *Name (to hide it later)* is optional. Give the marker a name, such as `bridge`, if a later Hide Marker action
  should remove just this one; Hide Marker then offers it in a list. Without a name, only Hide Marker's
  "every marker" removes it.
- For other looks (a skull for "destroy this", a flag), add more marker prefabs beside it. Each needs a
  `ScenarioMarker` component on its root. A marker's particle systems and animators restart each time it is shown.

## Saving

- A scenario file keeps the triggers as authored.
- A saved game (the in-game menu, the S key) also keeps how far they have got:
  - each trigger's fired count, its counts, how many turns it has been held, and whether it is on;
  - how many turns each side has begun;
  - the level clock;
  - the markers shown;
  - actions still waiting;
  - the conversation that was on screen, which is shown again on load.

  A save loads exactly as it was saved.
- Conditions, actions and every other enum are saved **by number** (shown in the tables above). New values always go
  at the end. A number is never reordered, renumbered or reused (see `ScenarioTrigger.cs`). Renaming a field drops
  its value from existing files, so keep field names.

## The tutorial mission

`Assets/Resources/Scenarios/Tutorial.dat` is a complete example, and the test `tutorial-playthrough` plays it
through:
- **Turn 1:** the scout introduces the grunt, with a marker on him. Selecting him moves the marker to a target tile
  and explains the two-click move. The lesson goes on only once he stands on that tile: "Good job!", then the
  end-turn hint, and only then is the ambush armed.
- **Raiders' turn:** a raider spawns in the fog and is ordered to attack the grunt. "Enemy spotted!" as it comes into
  sight. Taking damage (after the shot has landed) brings "move into supply to heal", with a marker on a tile in
  supply, and arriving there brings "Good, now end your turn". After that the raider holds its ground, resupplied
  every turn so it never starves, until it is killed.
- **Turn 3:** if the HQ was never opened, a hint points at it. Buying a grunt and a supply truck are each explained.
  A bought grunt is pointed at the neutral city to capture.
- **The end:** the attack hint comes once the truck is bought (or on turn 4), and killing the raider wins.

The raiders' side starts with nothing. It isn't knocked out because it isn't *eliminable* (below). Losing every unit is
the tutorial's own loss trigger.

## The scenario's words

At the top of the Scenario Script's inspector:
- **Description** is shown with the scenario when it is picked in the menus.
- **Win Text** and **Loss Text** are shown on the post-game screen, above the game's findings.

They are saved at the top of the scenario file (`"description"`, `"winText"`, `"lossText"`).

## What can be bought

The units a scenario lets the sides buy are the GameController's **Allowed Units** (`"allowedUnits"` in the file; empty
means every unit). The map editor's "All units" list is what you can place on the map, not what can be bought. With
purchases off, the HUD doesn't show gold.

## Sides that can't be knocked out

Each Player has **Eliminable**, on by default. Switch it off in the Player's inspector for a side the scenario keeps in
the game whatever happens: losing its HQ or its last unit doesn't knock it out, as for an enemy that starts with
nothing and is sent in by triggers. The scenario file saves the setting with the side (`"eliminable": false`). A side
that can't be knocked out never loses on its own, so end such a game with a Win or Lose action.

## Worked example: a tutorial beat

1. **Briefing**: Level Start → Camera Focus on the bridge (3,-1), zoom 25 → Dialogue (Placeholder Officer: "Take
   the bridge before nightfall.") → Show Marker `Marker`, id `bridge`, text "Take the bridge", at (3,-1).
2. **Across**: Units In Area, the player's units, (3,-1) radius 0, held for 1 of the player's turns (Delay 1) → Hide Marker `bridge` →
   Dialogue (Placeholder Scout, right: "We're over!") → Spawn Unit 2 Tank for the AI at (7,-4), named "Counterattack".
3. **Counterattack beaten**: Units At Most, the AI's units named "Counterattack", 0 → Play Music (a track you have put in
   `Resources/Music/Special`; that folder is empty for now) → Win for the player.
4. **Out of time**: Day Reached 8 → Dialogue ("Too late.") → Lose for the player.

## For programmers: adding a condition or action

1. Append the enum value with a new, never-used number (`ScenarioTrigger.cs`).
2. A fact that happens and is gone: count it in the one place it happens (an event the game already raises, or
   `ScenarioScript.Observe`, which compares each unit with the last frame). A state: a query on `ScenarioGame`.
3. A `case` in `ScenarioScript.IsMet` (or `Do`) that calls only `ScenarioGame`, which calls the game's own methods
   (`Unit.ChangeOwner`, `Building.ChangeOwner`, `HexTile.RepairFully`, `GameController.EndGame`, …).
4. Reuse a field if its meaning fits. Otherwise add one, with a tooltip that starts with the type's name.
5. Add it to the field tables and the summary text in `Editor/ScenarioTriggerDrawers.cs`.
6. Save: fields are saved by name automatically (`ScenarioJson`). Progress fields get `[ScenarioState]`.
7. Add a row to the tables above, and a check in `Tests/ScenarioTriggerChecks.cs`
   (`PROBE=export-rules bash Tests/run.sh Assets` writes its file; `ONLY=rules NAME=script- bash Tests/run.sh Assets`
   runs the checks).
