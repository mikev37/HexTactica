# Scenario triggers and dialogue (campaign scripting)

A campaign map is a scenario file with a **script**: a list of triggers, *when this happens, do these things*. The
script is saved in the scenario file (its `"script"` block), so a level is still a scenario file and nothing else.
Triggers are not in the map editor. You edit them in the Unity inspector.

## Editing triggers

- **Outside play:** HexTactica ▸ Scenario Triggers ▸ Open Scenario File… opens a `.dat` file. It puts its triggers on a
  *Scenario Script (editing a file)* object in the open scene. That object is never saved with the scene. Select it,
  edit the triggers, then press **Save triggers into it**. Save writes only the file's `"script"` and leaves the rest
  of the file as it was.
- **While playing:** select **Scenario Script** in the Hierarchy. Its triggers are live: a change applies at once,
  and each folded trigger shows how often it has fired. Changes made in play are lost when play stops. To keep them,
  press **Save triggers into it**. The file gets the triggers as you authored them, without the game's progress.
- The inspector shows only the fields a trigger's condition, or an action's type, uses. A folded trigger reads as one
  sentence: `Ambush: when the player's Tank enters (3,-3) r1, 2 actions`. Names are chosen from lists: other
  triggers, sides, unit and building kinds, characters, markers, music tracks and sounds.
- Problems show above the list: a part or an After Trigger naming a trigger that isn't there, two triggers with one
  name, a Dialogue action with no lines. They are also logged when the scenario loads. A trigger whose condition can
  never be met warns once, in play.
- **Areas on the board:** with the Scenario Script selected, the Scene view outlines every area a trigger or action
  uses (yellow for trigger areas, blue for action tiles), with the trigger's name. Drag the dot in an area's centre
  to move it to another tile. While playing, the inspector shows the last tile you clicked, so you can copy its
  coordinates.
- Tiles are given as cube coordinates `x, y` (z is −x−y): the same ones the scenario file uses.

## Sides

A side is a player's id from the scenario file (0, 112, …), or one of these:

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
| Day Reached (5) | the day on screen is *Value* or later (day 1 is the first) | day |
| Units In Area (6) | now: at least *Count* matching units within the area | side, kind, name, area |
| Money At Least (7) / At Most (8) | now: *Side* has at least / at most *Value* gold | whose, gold |
| Unit Built (9) | matching units are bought at a building: *Count* of them | side, kind, name, in area |
| Unit Damaged (10) | a matching unit loses strength and is left at *Value* or less (0 is destroyed) | side, kind, name, in area, strength |
| Turn Over (11) | *Side*'s turn ends: *Count* times | whose |
| Unit Healed (12) | a matching unit gains strength and reaches *Value* or more | side, kind, name, in area, strength |
| Unit Starving (13) | now: matching units out of supplies (they lose a strength each turn) | side, kind, name, in area |
| Unit Out Of Ammo (14) / Out Of Fuel (15) | now: matching units with none left | side, kind, name, in area |
| Unit Low On Fuel (16) | now: matching units with *Value* % of a full tank or less, but some left | side, kind, name, in area, % |
| After Trigger (17) | the named trigger has fired at least once (sequencing) | trigger |
| All Of (18) / Any Of (19) / None Of (20) | the named triggers' conditions hold now: all of them / at least one / none | parts |
| Turn Started (21) | *Side*'s turn begins (after its upkeep): *Count* times | whose |
| Units At Most (22) | now: at most *Value* matching units are alive (0: all gone, as in "destroy every enemy tank") | side, kind, name, in area |
| Building Owned (23) | now: at least *Count* matching buildings are held by *Side* | held by, kind, in area |
| Unit Cut Off (24) | now: matching units stand outside their side's supply | side, kind, name, in area |
| Units At Least (25) | now: at least *Value* matching units are alive (as in "when you have 3 tanks"; with no kind, any units) | side, kind, name, in area |

A **kind** is a unit or building prefab's name (Grunt, Tank, City, HQ…). A faction's version of a unit counts as its
base unit. A **name** is a unit's personal name, which Spawn Unit can give its units, so that later triggers can pick
them out. **In area** limits a condition to units or buildings within *Radius* of the tile. With Radius 0, it is the
tile itself, for example *that* city.

**A trigger switched off** never fires, but it can still be a part of All Of / Any Of / None Of. This makes it a
named condition you can reuse. Its counts still go up while it is off.

## Timing

- A trigger fires **once**, unless **Repeat** is on. With Repeat it fires each time it is met anew, at most every
  *Repeat Every* seconds. Its counts start again from 0 each time it fires.
- **Delay** is how long the condition must have held, in seconds of play, without a break. If the condition breaks,
  the wait starts over.
- Triggers are checked every frame, in list order, while the game plays. They are not checked during the pre-game
  screen, the map editor, the in-game menu, or a replay. The level clock stops while a menu is open.
- An **Enable Trigger** action switches a trigger on. Its counts start from then, so "after the briefing, the next
  kill…" counts only kills after the briefing. **Reset Trigger** lets a trigger fire again.

## Actions (in order)

| Action | Does |
|---|---|
| Dialogue (0) | shows its lines one after another. The game is held until the last line is closed, and the trigger's next action waits until then. |
| Camera Focus (1) / Camera Frame Board (25) | glides the camera to the tile (*Zoom* sets the orthographic size; 0 keeps the zoom) / shows the whole board |
| Show Marker (2) / Hide Marker (3) | puts a marker prefab from `Resources/ScenarioMarkers` on the tile, with *Text*, known by *Marker Id* / removes it (empty id: every marker) |
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
| Wait (24) | the trigger's next action waits *Seconds* |

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
- A line names a **character**: a `DialogueCharacter` asset in `Assets/Resources/Characters` with a display name,
  portrait, name colour, an optional voice (played with lines that have no sound of their own) and which way the
  picture faces (it is mirrored to face the text). Three placeholders are there for now (`Placeholder Officer`,
  `Placeholder Scout`, `Placeholder Commander`, from `Assets/Sprites/Portraits`). Replace their portraits, or add new
  characters, when the real art exists.
- With no dialogue box (headless tests, or a missing prefab), each line goes to the log and nothing waits. A missing
  prefab is reported once.

## Markers

`Assets/Resources/ScenarioMarkers/Marker.prefab` (made by Recreate Marker Prefab) is a ring round the tile, an arrow
bobbing over it, and the action's text. Add other marker prefabs beside it; each needs a `ScenarioMarker` on its root.
Its own particle systems and animators restart each time it is shown.

## Saving

- A scenario file keeps the triggers as authored.
- A saved game (the in-game menu, the S key) also keeps how far they have got:
  - each trigger's fired count, its counts, its Delay timer, and whether it is on;
  - the level clock;
  - the markers shown;
  - actions still waiting;
  - the conversation that was on screen, which is shown again on load.

  A save loads exactly as it was saved.
- Conditions, actions and every other enum are saved **by number** (shown in the tables above). New values always go
  at the end. A number is never reordered, renumbered or reused (see `ScenarioTrigger.cs`). Renaming a field drops
  its value from existing files, so keep field names.

## Worked example: a tutorial beat

1. **Briefing**: Level Start → Camera Focus on the bridge (3,-1), zoom 25 → Dialogue (Placeholder Officer: "Take
   the bridge before nightfall.") → Show Marker `Marker`, id `bridge`, text "Take the bridge", at (3,-1).
2. **Across**: Units In Area, the player's units, (3,-1) radius 0, after a 1-second Delay → Hide Marker `bridge` →
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
