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

## Supply carried by transports, ships and aircraft

A supply unit aboard something links to its side's network through what carries it (SupplyNetwork.CarriedLinked):

- A land transport or an aircraft links when, in one move of its own (its real move: range, terrain, fuel, enemies in
  the way), it can get onto supplied ground. Beside supplied ground is not enough.
- That move is a **transit** (SupplyNetwork.Exposed, TransitField): it may not use any tile on or beside an enemy unit
  that could attack the carrier — one with firepower whose weapons target the carrier's locomotion (a fighter does not
  intercept a truck; it does a transport helicopter). The carrier's own tile counts: one beside such an enemy links only
  if it already stands on supplied ground. It applies to every carried link (ships and convoys too), and the supply line
  follows the transit's way. A supply unit on foot is never in transit: it links by standing in supply, as before.
- Under fog, **the screen shows the supply network as it truly is** (SupplyNetwork.FillUnseenHoles): a line or ring of
  enemies the side does not see that cuts supply off shows as cut off (the oddity is a reason to look). The one thing
  hidden is the tile such an enemy stands on: it shows as the side's supply would be without those enemies, so no
  one-tile hole pinpoints it. The supply line follows the way the network truly links a carrier. Where unseen enemies
  stop a transit or send it round, a **contact** marker (Assets/Resources/SupplyContact.prefab, placed by SupplyContacts;
  SupplyNetwork.UnseenContacts) sits on the first tile of the way the carrier would take knowing only what it sees that
  they touch: something is on or beside it. It never sits on one of the side's units or on the unseen enemy itself.
  The AI plans with the enemies it knows of, and does not use contacts.
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
- An enemy unit standing on a supply building (HQ, FOB, naval yard) stops its supply while it is there; an ally's or
  one's own does not.

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
