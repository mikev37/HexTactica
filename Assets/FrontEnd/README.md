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
| MainMenu | `MainMenu` | **Load Scenario** has two tabs. Scenarios: sets one up on the pre-game screen. Saved games: continues one. **Map Editor** edits a scenario or starts a new map. **Unit Explorer** (`UnitExplorer`, `UnitPreview`) and **Options** (`OptionsPanel`) open panels. **Exit** quits. |
| PreGame (over SampleScene) | `PreGameMenu`, `PlayerSetupRow` | Shows the scenario's board. Each side gets **who plays it** (Human, AI with the default profile, or AI with a profile from `FrontEndSettings.aiProfiles`) and a **faction** (fixed if the map sets it). Also **Fog of war**, **Build units** (purchases) and **Faction special units**. **Start** reloads the scenario with these choices and the game plays. |
| PostGame (over the game, when it ends) | `PostGameScreen` | Shows who won and **What happened**, the findings from `GameInsights`. It also has a **Units** table for each side. **View board**, **Play again** and **Main menu** close it. |
| InGameMenu (Esc, over the game) | `InGameMenu` | **Resume**, **Save** (named, into the saves folder), **Load** a saved game, **Exit to main menu**. The game is held while it is open. |
| MapEditor (over SampleScene) | `MapEditorPanel`, `PaletteButton`, `MapPlayerRow` | Tools: **Look**, **Terrain** (click or drag), **Units** (shown by base units, one faction's, or all), **Buildings**. Placement uses the chosen side, or neutral for buildings; clicking a unit or building again removes it. **Sides**: add or remove sides, rename them, change their colour, and fix a side's faction or leave it "Any". Also sets the name, width (**Resize**), fog and purchases. **New**, **Load**, **Save**, **Play** (pre-game on it) and **Exit**. |

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

The terrain palette lists only terrain with `TerrainData ▸ In Map Editor` on. It is off for what buildings put
under themselves (Urban, City, Bunker) and for destroyed or damaged forms (stumps, damaged roads, the collapsed
bridge, broken ice, Tidal Mud, Permafrost, Badlands).

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
