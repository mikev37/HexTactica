# HexTactica

## Ground rules

- Nothing about UI, unit or level design is written in code, and code is never used for configuration.
  - UI lives in prefabs and scenes edited in Unity. Code does not build, lay out, style or patch widgets at run time,
    and has no fallbacks for an older prefab: if a prefab lacks something, change the prefab.
  - Units are their prefabs; levels are scenario files.
  - Tunable values live in assets or serialized fields, not constants in code.
- More standing rules for working on this project: `Tests/HANDOFF.md`, section 1.

## Map editor and scenario files

The **map editor** (main menu ▸ Map Editor) is for players: they make and change their own maps with it. It lists
and opens the scenarios in `FrontEndSettings.scenarioFolders` (`Resources/Scenarios`, `StreamingAssets/Scenarios`) and
the player's own (`persistentDataPath`), and saves to the player's folder (in the Unity editor, with
`FrontEndSettings` set so, back where a scenario was loaded from). It never lists the campaign's mission maps
(`FrontEndSettings.campaignFolders`, `Resources/Campaign/Scenarios`): those are the game's, not the player's.

Developers edit any scenario, the campaign's included, in the Unity editor in play mode: **HexTactica ▸ Load Scenario
File...** opens a file from any folder onto the board, and **HexTactica ▸ Save Scenario File...** writes the board to
any folder. With the map editor open, a file loaded either way shows in it: its sides (names, colours, factions),
rules and width are read again from the board (`ScenarioLoader.Loaded`).

## Campaign scripting: scenario triggers and dialogue

A scenario file can carry a script: triggers (*when this happens, do these things*) edited in the inspector, with a
dialogue box, markers on tiles, camera moves, music, spawns, ownership changes and win or loss. See
`Assets/Scenario/README.md`.

## Allies

A scenario can put sides on one team: each player's **Team** (on the Player, in the inspector; `"team"` in the file's
players). Sides with the same number, not 0, are allies; 0 is on nobody's side. Teams are the scenario's, fixed for the
game: there is no changing sides in play. The **Allies** rule (`"allies": "on"/"off"`, on by default; map editor ▸ Look ▸
Allies: players choose, or the map fixes it on or off) turns them on or off as a whole. While it is on, allies:

- do not fire on each other's units (`Player.Hostile`), and pass through each other's tiles as through their own;
- do not take each other's buildings: an ally's infantry on a building counts as the owner's (it wears a capture down);
- live on each other's supply: a unit in an ally's network is fed, healed and rearmed as in its own
  (`HexTile.isSupplied`). Supply units relay only their own side's network, and a building services its owner's allies;
- see what each other sees under fog of war, and always see each other's units;
- win together: the game is over when the sides left are all allies (enemy HQs taken or enemy sides eliminated), and every
  side of the winning team has won (`GameController.Won`), including one already knocked out.

The post-game report compares you and your allies ("your side") with everyone you fought, gives every side a units
table, and has two findings of its own for each team: **Best assist** (one side's unit destroying an enemy unit that had
been killing an ally's units, the more it had killed and the more of them it still had within reach the better) and **who
carried the team** (each side's share of the enemy worth destroyed and of the team's losses). The report follows each
unit through the game log by where it stands (`GameRecord.Track`); thresholds are `assistKills` and `teamKills` on the
FrontEndSettings asset's insights.

The AI treats an ally as neither friend to command nor enemy: it never targets an ally's units, HQ or buildings, does not
count them in its forecasts or estimates of the enemy, and knows its units are fed on an ally's supply. It does not plan
with its allies. Where allies come into the code: `Player.Allied/Friendly/Hostile`; tests `rule-allies`,
`ai-allies-hold-fire`, `ai-allies-battle` (Blue and Green, a third side, against Red).

## Strikes: missiles, barrages, bombs

Four attributes on a unit (its prefab's Unit, header *Strike*), each on or off by itself, so a unit can have any mix:

| Attribute | What it does |
|---|---|
| **Targets Ground** | It attacks a tile, not a unit: any tile in its range and line of fire, whatever is on it, seen or not. |
| **Blast Radius** (0-3) | Its attack also hits every unit within this many tiles of the target (1: the seven tiles), friend and foe, each with that unit's ordinary damage (`Unit.DamageFrom`, the one formula), with no answer and no retreat. On a unit that aims at units and fires at once, it is the splash around its ordinary shot. |
| **Ballistic** | Its attack lands at the start of its owner's next turn, on the tile it was aimed at, on whatever is there then. A side knows it is coming only if it saw the unit fire, or had a unit within **Launch Seen Within** of it (then it also sees **Launch Reveal Radius** round the launch tile until its own turn ends, and its player is warned). No side but its owner knows where it is aimed. |
| **Self Destructs** | It is expended by its attack (not killed: no loss is counted or logged). With Ballistic it leaves the board as it is launched, and there is nothing left to attack. |

Also under *Strike*: **Strike Reveal Radius** (its owner sees that far round the target until the end of the turn it
lands), **Crater Terrain** (the target tile becomes this: land only; water stays water, a bridge falls as bridges do, a
building's tile is the building's), **Building Damage** (to a building on the target tile), and for looks only
**Strike Explosion** and **Strike Sound**. How long a launch or a fall takes is its animator's (below).

**The Missile** (`Resources/Units/Missile`) has all four: 30,000 gold, built at the Missile Silo, cannot move (so a silo
holds one at a time), range 2-10, blast radius 1, reveals 2, seen launching from 10 tiles (revealing 1 round its launch
tile), leaves a **Crater** (`Resources/Tiles/Crater`, also
paintable in the map editor). Its look is placeholder art (`Sprites/Units/Missile`); the crater uses `craters_0` until
its art arrives.

- **Aiming.** Select it, click a tile in range: the target and the tiles its blast reaches show the Threat sprite
  (`Sprites/UI/Threat`, `HexTileUIData.strike`), and every unit you know of on them shows what it would lose (as things
  stand now: a delayed strike hits what is there when it lands). Click the target again to fire.
- **Its animator** is authored by hand (no code makes or edits it). What the code sets on it:
  - **Started** (bool): true on its first frame, as on every unit.
  - **Flying** (bool): true as Launch is set on the pad unit, and on the arrival copy before its first frame; never
    set back. Keep on-the-pad states (idle) behind *Flying false*.
  - **Launch** (trigger): on the pad, as it is fired. Its state carries **StrikeStage** *Launched*: when it ends, the
    unit is removed.
  - **Fall** (trigger): at its side's next turn start, on a copy made from the prefab at the target tile. Its state
    carries StrikeStage *Landed*: when it ends, the strike sound plays and Impact is set.
  - **Impact** (trigger): the explosion, big enough to hide the tile. An optional animation event **StrikeCovered** on
    its clip is the moment the crater, the building's damage and the units' hits appear (until then the blast's tiles
    and buildings keep their old look); without it they appear when the state ends. Its state carries StrikeStage
    *Impact Done*: when it ends, the copy is removed.
  - **Die** (trigger): killed in its silo, as any unit.
  A missing trigger skips its stage at once; a stage still playing after 15 s is cut (`Unit.StageTimeout`). The code
  only places the root (the pad, then the target tile) and never moves, flips or rotates it: climbing off screen,
  coming in from above and turning nose-down are the clips' work, on child objects. *Strike Explosion* is an optional
  extra effect (empty on the Missile).
- **Fog of war.** Once launched it is off the board (its tile is free: the silo can build another); while its launch
  plays it is shown to whoever sees its launch tile, and its fall to whoever sees the target.
- **Launch warning.** A side with a unit within 10 tiles of the launch (or sight of it) sees the launch tile and the
  tiles round it until the end of its next turn, and knows a strike is coming (its AI too). If that is the player at the
  screen, a yellow banner shows for 10 seconds or until the turn ends, then fades, and the camera glides to the site;
  any camera input of the player's or a scenario's camera action cuts the glide short. Clicking the banner cycles the
  camera through this turn's launch sites. The banner is the prefab `Resources/LaunchWarning` (text, colours, layout,
  sound, and its timings on its LaunchWarning component), made by `Editor/LaunchWarningPrefab.cs` the first time the
  project opens; *HexTactica ▸ UI ▸ Recreate Launch Warning Prefab* puts the default back.
- **Saves, the game log and replays.** A strike in the air is in every save (`"strikes"`, and `"lit"` for ground lit up
  that turn): the in-game save, the map editor's, the game log's turn snapshots. The log has `Blue Missile (-4,0)
  launches at (3,0)` (an action, which a replay plays again) and `Blue Missile strike lands on (3,0): Red Tank (3,0) -6
  destroyed, ...; ground Grassland destroyed` (worked out again in a replay, not played from the line). The post-game
  record credits its kills to the Missile.
- **Scenario scripts** have Strike Tile, Strike Launched and Strike Landed (`Assets/Scenario/README.md`); a Missile is
  placed like any unit in the map editor or by Spawn Unit.
- **The AI** (`AI/AIStrike.cs`) aims where its strike destroys the most enemy material, by what its side knows, less its
  own and its allies' units under the blast; a unit that can move counts half against a delayed strike. A self-destructing
  unit fires only for at least 40% of its own worth (5% when it is about to be destroyed anyway); otherwise it holds.
  It learns of strikes only as a player does (`Strikes.KnownTo`): its own, target and all; an enemy's only if it saw the
  launch, and then only who fired, from where and with what, never the target. A launch it did not see changes nothing
  (test `ai-strike-unseen` plays move for move as with nothing in the air). Against one it saw, each unit's danger is the
  share of its side's units in reach that one blast around its tile would also catch (part of `AIMap.ExpectedLoss`), so
  crowds spread out or leave reach. It never walks under its own strike. AI code may not read the list of every strike
  in the air (`Strikes.InFlight`) or the strike events: test `ai-strike-blind` checks the AI's source for it. It buys missiles by the purchase rules' generic
  value for a unit that fires from afar without moving (about 12,000 for the 30,000 Missile).
- Tests: `strike-missile`, `strike-attributes`, `strike-save`, `strike-log-replay`, `script-strike`, `ai-strike-fire`,
  `ai-strike-spread`, `ai-strike-blind`, `strike-launch-seen` (`Tests/StrikeChecks.cs`; `ONLY=rules NAME=strike bash Tests/run.sh Assets`).

## Supply carried by transports, ships and aircraft

A supply unit aboard something links to its side's network through what carries it (SupplyNetwork.CarriedLinked):

- A land transport or an aircraft links when, in one move of its own (its real move: range, terrain, fuel, enemies in
  the way), it can get onto supplied ground. Beside supplied ground is not enough.
- A ship links when, in one move, it can get beside supplied ground or onto a supplied harbor. Any supplied shore
  counts, not only a harbor: a shoreline city in supply links a supply ship lying off it.
- A ship also links through another carrier with supply aboard that is linked (a convoy, any length), and through an
  aircraft; an aircraft links through a linked ship that lies beside land. A ship tries its convoy first, so its supply
  line follows the water.
- What a carried supply unit then gives: its own range around its carrier; a ship only while beside land, an aircraft
  only over land. Water is never supplied ground: no sea tile is in anyone's supply, not even the one a ship carrying
  supply floats on. A ship is in supply in harbor, or at sea while a supply unit it carries is linked (above): it is
  then healed, rearmed, refuelled and restocked each turn like a unit on supplied ground, so a convoy never needs
  rotating out. Otherwise at sea it lives on its own supplies (and mends while it has them). An aircraft carrying
  supply gets no such thing: it runs on its fuel and supplies as always.
- Supply aboard a transport aboard another (a truck in a ship) is carried by the outermost: it links and spreads by the
  ship's rules.
- What a ship carries is resupplied at the start of the turn as on supplied ground (healed, rearmed, refuelled,
  restocked) while the ship has supplies of its own or a supply ship beside it restocks it, or in harbor; once the
  ship's supplies are gone, its cargo starves.
- An HQ supplies only the side it began with: a captured HQ supplies nobody until it is retaken.

## Replays and game logs

Every game is recorded (`game.log` plus a board for each turn) and can be replayed from the main menu. A replay plays
the logged orders again through the game's own rules and checks every result, and every turn's starting board, against
the recording. A mismatch is a desync: it is reported as an error, and the replay then loads the recorded board to carry
on (`Replay.CorrectDesyncs`, counted in `Replay.Corrections`). That load is error correction, never the normal way from
one turn to the next. For turns played over the network, correction is to be turned off: there a desync means an
incompatible client or cheating, so it is reported and play stops.

Planned, not built yet:
- **"Save replay" at game end.** Every game stays recorded by default; leaving the box unchecked deletes that game's
  log files.
- **Managing game logs.** An easier way to see and delete recordings.
- **Player-facing recordings.** Today a recording also serves debugging: it holds turn boards, save points and the AI's
  reasoning. A player needs only the basic order log a replay plays from.
