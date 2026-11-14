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

The AI treats an ally as neither friend to command nor enemy: it never targets an ally's units, HQ or buildings, does not
count them in its forecasts or estimates of the enemy, and knows its units are fed on an ally's supply. It does not plan
with its allies. Where allies come into the code: `Player.Allied/Friendly/Hostile`; tests `rule-allies`,
`ai-allies-hold-fire`, `ai-allies-battle` (Blue and Green, a third side, against Red).

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
  supply floats on. A ship is in supply only in harbor; at sea it lives on its own supplies (and mends while it has
  them).
- Supply aboard a transport aboard another (a truck in a ship) is carried by the outermost: it links and spreads by the
  ship's rules.
- What a ship carries is resupplied at the start of the turn as on supplied ground (healed, rearmed, refuelled,
  restocked) while the ship has supplies of its own or a supply ship beside it restocks it, or in harbor; once the
  ship's supplies are gone, its cargo starves.
- An HQ supplies only the side it began with: a captured HQ supplies nobody until it is retaken.
