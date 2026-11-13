# Front end: menus, pre-game, post-game, in-game menu, map editor

## First run

Open the project. `Editor/FrontEndScenes.cs` makes whatever is missing, once, and touches nothing that exists:

- `Assets/Resources/FrontEndSettings.asset`: scene names, scenario and save folders, the menu key, and the
  post-game report's thresholds;
- `Assets/FrontEnd/UnitPreview.renderTexture`: the unit explorer's picture;
- `Assets/Scenes/MainMenu.unity`, `PreGame.unity`, `PostGame.unity`, `InGameMenu.unity`, `MapEditor.unity`;
- `Assets/FrontEnd/Prefabs/`: the rows the screens list things in (File Row, Unit Row, Attribute Row, Side Row,
  Palette Entry, Map Side Row). Edit a prefab to change every row of its kind;
- the build settings: these scenes are added and MainMenu goes first, so a build starts there.

Scenes made by an older version of the tool don't pick up later layout changes. **HexTactica ▸ Front End ▸ Update
Scenes** applies them in place. It changes only what each update is about, keeps your art, sounds and layout, and
skips steps already done. **Rebuild Front End Scenes (overwrites)** deletes and remakes everything. It is a last
resort, for scenes with nothing of yours in them.

If an untitled scene is open, it skips the scenes. Save that scene, then run **HexTactica ▸ Front End ▸ Create
Missing Scenes**. **HexTactica ▸ Front End ▸ Open Main Menu Scene** opens the menu; press Play.

The generator only makes a first layout. After that the scenes are yours to edit in Unity: restyle, move or
replace anything. Each screen's component finds its widgets through its inspector fields, and the buttons call its
methods through their On Click lists. Lists such as files, units, sides and palette entries copy a row
**prefab**. Nothing builds or lays out UI at run time.
`SampleScene` still plays its own board when you open it directly, as before.

## Screens

| Scene | Component | What it does |
|---|---|---|
| MainMenu | `MainMenu` | **Campaign** opens the campaign screen. **Load Scenario** has two tabs. Scenarios: sets one up on the pre-game screen. Saved games: continues one. **Map Editor** edits a scenario or starts a new map. **Unit Explorer** (`UnitExplorer`, `UnitPreview`) and **Options** (`OptionsPanel`) open panels. **Exit** quits. |
| PreGame (over SampleScene) | `PreGameMenu`, `PlayerSetupRow` | Shows the scenario's board. Each side gets **who plays it** (Human, AI with the default profile, or AI with a profile from `FrontEndSettings.aiProfiles`) and a **faction** (fixed if the map sets it). Also **Fog of war**, **Build units** (purchases) and **Faction special units**. When two or more sides are on one team, **Allies**: on, the sides are listed team by team with a divider (the *Side Divider* prefab) between teams; fixed when the map fixes it. **Start** reloads the scenario with these choices and the game plays. |
| PostGame (over the game, when it ends) | `PostGameScreen` | Shows who won and **What happened**, the findings from `GameInsights`. It also has a **Units** table for each side. **View board**, **Play again** and **Main menu** close it. |
| Campaign | `CampaignScreen`, `MissionFlag`, `StarPip` | The campaign map with a flag per visible mission. Picking one plays its intro lines, then shows its briefing, pars and **Start**. See *Campaign* below. |
| MissionBrief (over SampleScene) | `MissionBriefing` | A campaign mission's pre-game screen: introduction, objectives and pars. No choices. **Start** / **Back**. |
| MissionResult (over the game, when a mission ends) | `MissionResultScreen` | Won or lost, the scenario's words, each par with the result and its star (gained one by one). **Return to campaign** / **View board**. |
| InGameMenu (Esc, over the game) | `InGameMenu` | **Resume**, **Save** (named, into the saves folder), **Load** a saved game, **Exit to main menu**. The game is held while it is open. |
| MapEditor (over SampleScene) | `MapEditorPanel`, `PaletteButton`, `MapPlayerRow` | Tools: **Look**, **Terrain** (click or drag), **Units** (shown by base units, one faction's, or all), **Buildings**. Placement uses the chosen side, or neutral for buildings; clicking a unit or building again removes it. **Sides**: add or remove sides, rename them, change their colour, and fix a side's faction or leave it "Any". Also sets the name, width (**Resize**), fog, purchases, special units and **Allies** (players choose, or always on or off). Which sides are allies is not edited here: set each Player's *Team* in the inspector and save. **New**, **Load**, **Save**, **Play** (pre-game on it) and **Exit**. |

`GameSession` is created in every game scene, never in the menus. It opens what the menu asked for (`MatchSetup`),
loads the overlays, and holds the game while one is open (`GameController.Paused`). The HUD, turn banner, fog view,
building registry and camera rig now install only in a scene with a GameController (`GameScene`), not in the menu.

## Files

- Scenarios are listed from `FrontEndSettings.scenarioFolders`, relative to `Application.dataPath`. In the editor
  that is `Assets/Resources/Scenarios`. A build ships no `.dat` from Resources, so copy them to
  `StreamingAssets/Scenarios` for a build. Scenarios made in the map editor go to `<persistentDataPath>/scenarios`.
- Saved games go to `<persistentDataPath>/saves`, a separate folder from scenarios. A save is the same format as a
  scenario. It keeps who played each side (each player's `AI`), the round, fog, purchases and faction special units,
  and where its game log stood (`gameLog`, `gameLogLength`, `gameLogTurns`). Loading it starts a new recording that
  begins with that log cut back to the save (`GameLog.MarkSave`, `GameLog.Continue`), so one log covers the whole
  game, however many sittings it took.
- The map editor saves to the player's folder. With `editorSavesInPlace` (off by default), it saves back over the
  file it loaded, in the editor only, and keeps that file's `test` block.

## Campaign

- **Missions** are `Mission` assets in `Assets/Resources/Campaign/Missions` (*Create ▸ HexTactica ▸ Campaign Mission*).
  Each names a scenario file in `Assets/Resources/Campaign/Scenarios` (`FrontEndSettings.campaignFolders`), so
  missions never appear under Load Scenario. The map editor's Load lists them too, tagged *campaign*.
- **Fields:** title, scenario, faction (the flag's colour: `Faction.flagColor`; the factions now carry their campaign
  names and colours: Blue Sun Federation, Red Wolf Empire, Green World Union, Golden Eagle Coalition, Rebels brown),
  map position (0–1 over the map image), *Available At Start*, intro lines, briefing, objectives, outro lines (won)
  and lost lines, *Unlocks* (appear on a win), *Blocks* (closed for good on attempting; one already won stays won),
  and three pars.
- **Stars** (only for a win): **Speed** — won within *Par Turns* of the player's turns (0: no speed star);
  **Aggression** — at least *Par Kills* enemy units destroyed; **Defense** — at most *Par Losses* own units lost.
  Read from the game log (`GameRecord`), as the post-game report is, so they hold across saves. A game that was not
  recorded counts as won but earns no stars. Stars once earned are kept (`CampaignProgress`).
- **Progress** is `<persistentDataPath>/campaign.dat`. *HexTactica ▸ Campaign ▸ Reset Campaign Progress* clears it
  (asks first); *Check Missions* lists missing scenario files and missions nothing unlocks.
- **Flow:** flag → intro lines (once per run) → briefing → Start → MissionBrief → Start (counts as attempted, closes
  what it blocks) → game → MissionResult (stars gained one by one) → Return → Campaign: the flag gains its new stars,
  the view zooms onto each mission just unlocked as its flag appears, then the outro lines play.
- A save made during a mission remembers the mission (`"mission"` in the file), so loading it goes on as that mission.
- **Moving around the map** works as on the game board: the wheel (or + / -) zooms about the cursor, the right or
  middle mouse button drags it, the arrow keys pan, and Home shows the whole map. The view never leaves the picture.
  Flags keep their size on screen as you zoom (*Flags Keep Size*), so zooming in spreads crowded flags apart: place
  missions closer together (a finer *Map Position*, e.g. 0.512) and they separate once zoomed in. *Max Zoom* (4 by
  default), *Zoom Step* and *Pan Speed* are on the scene's CampaignScreen. When a mission unlocks another, the view
  zooms onto it and then glides back to where you were looking.
- **The map picture** is a placeholder colour: give the Campaign scene's *Map* object's Image your picture (and
  delete the *Placeholder Note*). Flag, star, tick and cross art are white placeholders in `Sprites/UI/Widgets`.
- The Campaign, MissionBrief and MissionResult scenes and the *Mission Flag* prefab are made by the front end's
  first-layout tool when missing (on opening the project, or *HexTactica ▸ Front End ▸ Create Missing Scenes*).
  *HexTactica ▸ Front End ▸ Update Scenes* adds the **Campaign** button to an existing main menu (a copy of its
  Load Scenario button, placed before it). Nothing that already exists is changed.

## Replay

**Main menu ▸ Replay** lists the recorded games (the game log folders in `GameLog.DefaultRoot`) and opens the game
scene on one, with the **Replay** scene's controls (`ReplayControls`) over it. Nothing but the game log is read:
each turn's board comes from its `turn-NNN-Side.dat`, and each action from that turn's lines in game.log, read by
`GameLog.Lines` (which wrote them) and played by `Harness/Replay.cs`.

- **Next action** gives the turn's next logged order to the game as a player or the AI would. A move walks the
  path the game finds (`Unit.MoveAlong`). An attack is `Unit.OrderAttack`, after which each unit's strength is held
  to the log's (a warning in the console if the game worked it out differently). Boarding is `Unit.WalkAndBoard`.
  After the turn's last action it loads the next turn's board.
- **Next turn** and **Previous turn** load that turn's board.
- **Play** orders the next action `FrontEndSettings.replayPlayPause` (0.1 s) after the last one's animation has
  finished.
- **Fast forward** orders an action every `replayFastInterval` (0.2 s) without waiting for the last to finish, and
  multiplies every unit's move speed on screen by `replayFastMoveSpeed` (3). A turn still ends only once the board
  is still.
- **Whose eyes** cycles through each side's fog and everything.

Three log lines changed for this. A move is now written and read in `GameLog.Lines` (`Moved`). An attack that
drives the defender back records where (`retreats to (x,y)`). A purchase records where the unit was put
(`placed (x,y)`). Getting into and out of a transport is logged too (`Blue Grunt (1,2) boards Blue APC (2,2)`, `Blue Grunt gets
off Blue APC (2,2) onto (3,2)`; `Transporting.Boarded`, `Unloaded`), with the transport's own move written first.
Recordings made before these lines existed replay without them, and catch up when the next turn's board loads.

Going from one turn to the next loads nothing: the turn is ended as the game ends it (`GameController.EndTurn`:
captures, upkeep, the next side's start) and every unit stays where it is. The board is then compared with the
recording's board for that turn (`Replay.Differs`), and only a board that differs is loaded, with the first
difference in the console. **Next turn** and **Previous turn** always load.

Each turn's board (`turn-NNN-Side.dat`, written by `GameLog.Snapshot`) now holds the sides: name, colour, faction
and money. Recordings made before this have no sides in their boards; for those the replay takes each side's name
and faction from the log's `side:` lines, and the colours stay the scene's.

The sides are read from the log's own `side:` lines (name and id), not from the board. A side the recording gives
no faction has none (`ScenarioLoader.SidesAsFiled`; the same for a saved game), where a scenario opened in the scene
keeps the scene's. Nothing is recorded while a recording is watched, and the scene's own board is no longer recorded
when it opens under a menu (pre-game, map editor, replay).

## Resigning, and options in game

The in-game menu has **Resign**: `GameController.Resign` takes the human side out (logged as `Blue resigns`), and
with one side left the game ends on the post-game screen, headed "Defeat." It also has **Options**, which shows
the same options panel as the main menu. That panel is the `Options Panel` prefab in `Assets/FrontEnd/Prefabs`,
made from your main menu's panel as it is by **Update Scenes**.

## Options

`GameOptions` keeps every option in PlayerPrefs. Each one is saved as it changes and applied when the game starts,
before the first scene loads. The options are:

- **Master volume** (`AudioListener.volume`);
- **Music** (MusicManager plays at `GameSounds.musicVolume` × this);
- **Sound effects** (`GameSounds.Play` and each unit's sounds play at this);
- full screen, resolution, quality and vsync.

Full screen and resolution only take effect in a build, not in the editor's Game view. **Update Scenes** adds the
Music and Sound effects rows to an existing MainMenu scene, as copies of its volume row.

## AI profiles

"AI (default)" is this AI (`AIPlayer`) with no profile, so it plays `AIProfile.Default`, the built-in defaults. Each
`AIProfile` asset listed in `FrontEndSettings.aiProfiles` adds an "AI: <profileName>" choice. The tool fills the
list with every AIProfile asset the first time, and only while the list is empty. Assets that aren't listed can't
be chosen. A save keeps each side's profile by its profileName (`PlayerData.aiProfile`).

## Map editor palette

A terrain is drawn as a tile of it looks: its water, lower and upper sprites stacked as on the board, never the floor
(`SpritePortrait.ComposeTerrain`, on a board tile's layout). A unit or building is drawn from its prefab's own sprites
(`SpritePortrait.ComposePrefab`). The pick also shows in the HUD's unit or tile panel (`Hud.Pinned`); the HUD's turn
controls are hidden in the editor. On the pre-game screen the board is shown whole, without fog, because who plays which side isn't chosen yet. Once
the game starts, fog is drawn for the human side. The **Look** tool shows the map's sides, name, width and rules in place of the
palette. **For** sets the side units and buildings are placed for, with **Neutral** for buildings. A base unit
placed for a side with a faction becomes that faction's version (a Conscript for a Union side is the Union
Conscript).

Terrain is listed by `TerrainData ▸ Palette Group`, then by name. The tool fills the group from the asset's folder
under Resources/Tiles when it's empty; change it to regroup. Terrain straight in Tiles has no group and is listed
first. A faction in **Show** lists only that faction's own units (its versions and extras). Pictures are drawn once
the palette is laid out, so every unit fits its box the same way, and owner-coloured sprites take the colour of the
side you're placing for. The terrain palette lists only terrain with `TerrainData ▸ In Map Editor` on. It is off for what buildings put
under themselves (Urban, City, Bunker) and for destroyed or damaged forms (stumps, damaged roads, the collapsed
bridge, broken ice, Tidal Mud, Permafrost, Badlands).

## Rules the map fixes

On the Look page, fog of war, purchases and faction special units each have a list. The choices are: players choose
(starting on), players choose (starting off), always on, always off. A fixed rule is saved as `"fixedRules":
"fog,purchases,factionSpecialUnits"` (whichever are fixed; `GameController.fogFixed` and the rest). The pre-game
screen shows a fixed rule and can't change it, the same as a faction the map fixes. **Update Scenes** replaces the
old Rules row (two checkboxes) with these lists.

## Loading a board

Units already on the board are removed without dying (`Unit.Wiping`): no death animations, sounds, log lines or
victory check. Before this, units riding transports played their deaths when a scenario started. Supply borders
take their side's current colour each time they redraw. A side added after the board was made (more sides than the
scene has) gets its per-tile state and borders (`HexTile.EnsureSide`).

## Sides

A map can have any number of sides, at least two. Sides the scene doesn't have are created when the map loads, and
scene sides the map doesn't have are left out. A side whose faction the map fixes is saved with
`factionLocked: true`. The HUD and turn banner are hidden in the map editor and on the pre-game screen.

## Factions: special units on or off

`GameController.factionSpecialUnits`, set by a scenario's `"factionSpecialUnits"`, the pre-game screen, or a save.
When it is off, a faction only changes how units look:

- `Faction.Map` keeps a replacement only if it is **cosmetic** (`Faction.Cosmetic`): the replacement's `Unit`
  component has the same value as the original's in every serialized number, flag, enum and text;
- asset references (sprites, prefabs) are looks and are not compared, and neither are fields marked `[LooksOnly]`
  (`description`, `personal_name`, `moveSpeed`, `StrengthDisplay`);
- extras are not offered, and no base unit is removed.

`PROBE=factions bash Tests/run.sh Assets` lists every replacement as "looks only" or "special", with the fields
that differ. For example, "Tank Rebel Variant" is special: it differs in supplies.

## Post-game report: read from the game log

The game log (`Harness/GameLog.cs`) is the one record of a game: nothing else tracks statistics. Its event lines are
worded in `Harness/GameLogLines.cs`, where every line has a writer and a reader next to each other. Replays, network
turns and the post-game report all read what was written, through the same code. Besides moves and attacks, the log
now records:

- each side at the start of every turn: `state` (worth, units, cities, buildings, income, gold, front), `fields`
  (units by kind) and `can buy`;
- purchases: `Blue buys Grunt at City (3,4) for 2500`;
- every unit lost and how: `Blue Grunt (3,4) dies: combat|supply|fuel|aboard|eliminated`. A combat death comes just before
  the attack line that names the killer;
- buildings changing hands: `City (3,4) changes hands: Red -> Blue`;
- `side:` lines (id, faction, AI or human), the HQ-to-HQ `axis:`, and `-- saved as` / `-- resumed from` lines.

The game's end is written the moment it happens (`GameController.GameEnded`), so the report always knows who won.
The post-game screen opens once the end-of-game banner has played out. Its Units page is a table built from the
**Unit Table Heading** and **Unit Table Row** prefabs.

`GameRecord.Read(folder)` parses a game.log. `GameInsights` turns the result into findings, written for the human
player ("you"):

- how strength went: the starting ratio, when the weaker side drew level, the end;
- the longest stretch where both sides lost units and the front barely moved, or where one side bled for no ground;
- cities and income;
- the unit kind each side fed in: bought and lost, what killed them, their own kills and trade, the worst turn, and
  the answer it bought. It also names a faction swap that priced the side out of contesting the enemy's main
  killer, as in "Coalition swaps the Fighter for the 21,000 Stealth Fighter, and Blue bought 2 all game";
- a killer that went unanswered: its shots and kills after the enemy's last attack on it, firing distance against
  the enemy's longest ground reach, and losses of the enemy aircraft that could have reached it;
- the best trade production hardly bought (only kinds it could buy);
- units lost to supply and fuel, and what each side spent on what.

Each finding appears only once there is enough data for it. The thresholds are in
`FrontEndSettings ▸ Post-game report`. The AI's own forecasts (what it expected to lose, its fog notes) are written
to the same log as trace lines, but the report doesn't read them yet.

Headless: `STATS=1 PROBE=duel FILE=... bash Tests/run.sh Assets` records the duel to a game log and prints the report read back from it (`STATS=you` writes it for a human Red).
