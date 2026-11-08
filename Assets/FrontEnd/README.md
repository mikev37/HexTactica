# Front end: menus, pre-game, post-game, in-game menu, map editor

## First run

Open the project. `Editor/FrontEndScenes.cs` makes whatever is missing, once, and touches nothing that exists:

- `Assets/Resources/FrontEndSettings.asset`: scene names, scenario and save folders, the menu key, and the
  post-game report's thresholds;
- `Assets/FrontEnd/UnitPreview.renderTexture`: the unit explorer's picture;
- `Assets/Scenes/MainMenu.unity`, `PreGame.unity`, `PostGame.unity`, `InGameMenu.unity`, `MapEditor.unity`;
- the build settings: these scenes are added and MainMenu goes first, so a build starts there.

If an untitled scene is open, it skips the scenes. Save that scene, then run **HexTactica ▸ Front End ▸ Create
Missing Scenes**. **HexTactica ▸ Front End ▸ Open Main Menu Scene** opens the menu; press Play.

The generator only makes a first layout. After that the scenes are yours to edit in Unity: restyle, move or
replace anything. Each screen's component finds its widgets through its inspector fields, and the buttons call its
methods through their On Click lists. Lists such as files, units, sides and palette entries copy a hidden
**template** child. Edit the template to change every row. Nothing builds or lays out UI at run time.
`SampleScene` still plays its own board when you open it directly, as before.

## Screens

| Scene | Component | What it does |
|---|---|---|
| MainMenu | `MainMenu` | **Load Scenario** has two tabs. Scenarios: sets one up on the pre-game screen. Saved games: continues one. **Map Editor** edits a scenario or starts a new map. **Unit Explorer** (`UnitExplorer`, `UnitPreview`) and **Options** (`OptionsPanel`) open panels. **Exit** quits. |
| PreGame (over SampleScene) | `PreGameMenu`, `PlayerSetupRow` | Shows the scenario's board. Each side gets an **AI** toggle and a **faction**. Also **Fog of war**, **Build units** (purchases) and **Faction special units**. **Start** reloads the scenario with these choices and the game plays. |
| PostGame (over the game, when it ends) | `PostGameScreen` | Shows who won and **What happened**, the findings from `GameInsights`. It also has a **Units** table for each side. **View board**, **Play again** and **Main menu** close it. |
| InGameMenu (Esc, over the game) | `InGameMenu` | **Resume**, **Save** (named, into the saves folder), **Load** a saved game, **Exit to main menu**. The game is held while it is open. |
| MapEditor (over SampleScene) | `MapEditorPanel`, `PaletteButton` | Tools: **Look**, **Terrain** (click or drag), **Units**, **Buildings**. Placement uses the chosen side, or neutral; clicking a unit or building again removes it. Also sets the name, width (**Resize**), fog and purchases. **New**, **Load**, **Save**, **Play** (pre-game on it) and **Exit**. |

`GameSession` is created in every game scene, never in the menus. It opens what the menu asked for (`MatchSetup`),
loads the overlays, and holds the game while one is open (`GameController.Paused`). The HUD, turn banner, fog view,
building registry and camera rig now install only in a scene with a GameController (`GameScene`), not in the menu.

## Files

- Scenarios are listed from `FrontEndSettings.scenarioFolders`, relative to `Application.dataPath`. In the editor
  that is `Assets/Resources/Scenarios`. A build ships no `.dat` from Resources, so copy them to
  `StreamingAssets/Scenarios` for a build. Scenarios made in the map editor go to `<persistentDataPath>/scenarios`.
- Saved games go to `<persistentDataPath>/saves`, a separate folder from scenarios. A save is the same format as a
  scenario. It keeps who played each side (each player's `AI`), the round, fog, purchases and faction special units.
  Its statistics are kept beside it (`<save>.stats.json`), so the post-game report covers the whole game.
- The map editor saves to the player's folder. With `editorSavesInPlace` (off by default), it saves back over the
  file it loaded, in the editor only, and keeps that file's `test` block.

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

## Post-game statistics

`GameStats` records each game from the moment a board loads. It uses the game log's events: purchases
(`BuildingController.Produced`), attacks, deaths and their cause, buildings changing hands, and a snapshot of each
side at every turn start. That snapshot holds its worth (cost × share of full strength), units, cities and where its
units stand on the HQ-to-HQ line. At the end it also writes the report into the game log folder (`post-game.txt`).

`GameInsights` turns those statistics into findings, written for the human player ("you"):

- how strength went: the starting ratio, when the weaker side drew level, the end;
- the longest stretch where both sides lost units and the front barely moved, or where one side bled for no ground;
- who held the cities for most of the game;
- the unit kind each side fed in: bought and lost, what killed them, their own kills and trade, the worst turn, and
  the answer it bought. It also names a faction swap that priced the side out of contesting the enemy's main killer,
  as in "Coalition swaps the Fighter for the 21,000 Stealth Fighter, and Blue bought 2 all game";
- a killer that went unanswered: its shots and kills after the enemy's last attack on it, firing distance against
  the enemy's longest ground reach, and losses of the enemy aircraft that could have reached it;
- the best trade production hardly bought;
- units lost to supply and fuel, and what each side spent on what.

Each finding appears only once there is enough data for it. The thresholds are in
`FrontEndSettings ▸ Post-game report`. The AI's own forecasts (what it expected to lose, its fog notes) are not
in these statistics. They are in the game log's AI trace.

Headless: `STATS=1 PROBE=duel FILE=... bash Tests/run.sh Assets` prints the same report at the end of a duel.
