# HexTactica — supply-aware AI, mechanics, and test harness (reviewed)

This package replaces the previous one. It is the result of an adversarial review of
that work, run against **your real prefabs** rather than invented fixtures, which found
that several things I told you worked did not.

- **Every test is a scenario file Unity can load** (`Tests/Scenarios/*.dat`): 7 behaviour
  scenarios with one check each and 36 matchups on generated terrain maps. The headless
  runner (`bash Tests/run.sh <your Assets folder>`) plays the same files, about 8 seconds.
- The two Editor scripts compile with `UNITY_EDITOR` defined, as Unity will build them.

---

## What the review found

### Broken in your actual game

| Problem | Effect | Fix |
|---|---|---|
| Supply only rooted at `Building`s; `SupplyPoint.producer` ignored | Your HQ is `TempHQ`, a *unit*. It was classed as a stranded relay and **lost a strength per turn until it died on turn 6**, taking every hub's connection with it | Producer hubs are supply roots and never drain |
| Two movement models: game's `CheckMovementRange` vs the AI's planner | AI could pick a destination (on a road) the game couldn't path to; the unit then **glided straight through whatever was in the way** | `CheckMovementRange` now delegates to the AI's pathfinder — one model |
| `HasHQ` only counted buildings | Destroying your HQ unit **ended nothing** | HQ units count; losing one eliminates its owner immediately |
| Locomotion inferred from `terrainScramble` | The migration tool would have made your **Grunt a Treads unit — unable to capture** | Inference from doc-grounded stats; `locomotion` written directly into `Tank.prefab` (Treads) and `Artillery.prefab` (Wheels) |
| Transports acted *before* the hubs that hire them; loaded cargo was excluded from the turn queue | **A transport never actually delivered anything** — the planner was only ever tested in isolation | Ordering fixed, cargo stays queued, transport executes rides. Tested end to end |
| Shipped `run.sh` used a blacklist | **The "89 passing" was never reproducible** from what you received | Explicit file list; verified from a fresh clone |
| HQ could be shoved by post-combat retreat | `TempHQ` (moveRange 0) moved | Immobile units are pinned (doc: "if they cannot, they take 1 extra damage") |

### Wrong in the AI's reasoning — found by measuring, not reading

| Problem | Symptom in play |
|---|---|
| **Risk in firepower points, reward in gold.** On your costs, a tile costing a Grunt ~1,800 gold scored a risk penalty of **2** | AI was effectively risk-blind |
| Threat field assumes every enemy fires at every tile | Every approach looked suicidal; **all six equal games were standoffs** |
| `Worth` used `Unit.value()`, which includes a tenth of what a unit threatens | Near your 100,000 HQ a Tank's own "worth" swung 1,166 → 7,800, and every cost scaled with it |
| Supply cost divided by 6 twice | Attrition priced at a sixth of combat loss |
| Kills multiplied by a supply curve | A clean one-shot kill shrank ~130 → ~11; **six units stalled against one Grunt** |
| HQ / hub chip damage priced at face value | Your HQ is 16,667/strength: 3 damage looked worth more than the whole army → **army rushed in and died** |
| Multi-turn plans charged risk only at tonight's stop | Tank drove six hexes out and six back, **every turn** |
| Next-turn reach by hex distance | Tank **starved in a corner** chasing a target it could never reach |
| Planner discarded every option below zero | Hub bled to death one tile outside the network when stepping back was −229 vs −694 |
| Shortlist proxy judged connection from a field including the hub's own bubble | The right relay position never got evaluated |

Each fix is commented in the code where it lives, with what went wrong.

The model of enemy fire is now **calibrated**: each enemy fires once, at its most
attractive target (damage × worth, doubled for a kill), with real `calculateDamage`.
Predicted vs actual losses per enemy turn: 6.8/9, 8.0/6, 10.5/13, 5.1/6, 2.8/2.

---

## Tests are scenarios

Every test is a level-editor save with a `test` block (name, question, opponent, rounds,
check) that both the game and the harness read; the game ignores the fields it doesn't know.
Units belong to SampleScene's players: **Blue, id 112** (has the `AIPlayer` — this AI) and
**Red, id 0** (the opponent: a baseline AI or you). SampleScene's board is now radius 15
(HexGenerator `width` 31, was 15); a file records its own width, and every tile it doesn't
list loads as Water (the level editor's own Load does the same). The camera frames the whole
board on start and pans/zooms (`CameraRig`, self-installing): arrow keys or right/middle drag
to pan, wheel or +/- to zoom, Home to re-frame.

**In Unity:** enter play mode in SampleScene, **HexTactica → Load Scenario File…**, pick a
file. **Blue is always this AI** (the scene's `AIPlayer` — the new AI replaced the old one;
loading a scenario also clears Blue's GoTo mission objective so it plays exactly as the
headless harness does). Red is whatever the file's test block says (`Legacy` = the original
AttackMove AI, ported faithfully; `Greedy` = defend-only), unless you pick otherwise under
**HexTactica → Red Opponent**: As the File Says, This AI (mirror match), Legacy AI, Greedy,
or Human (you play Red). The choice is remembered and applies on the next load.

**Headless:** `bash Tests/run.sh <Assets>` plays every file and reports:

| Behaviour scenario | Check |
|---|---|
| `hub-stays-out-of-reach` | the supply unit never ends our turn where an enemy can hit it (4 rounds) |
| `no-suicide-attack` | a lone tank does not take a shot that leaves it where the reply destroys it |
| `hit-the-supply-unit` | a tank beside the enemy supply unit and a grunt hits the supply unit |
| `defend-the-hq` | our HQ survives an enemy tank and two grunts arriving (4 rounds) |
| `rotate-the-wounded` | a 3-strength grunt with fresh grunts behind it pulls back |
| `hold-the-gap` | a unit takes the single gap in a water line before the enemy arrives |
| `finish-the-game` | an army destroys an enemy HQ left alone (5 rounds) |

Matchups: six generated maps (radius 5–6; mountains, water and forest planted at random,
then two open paths cut between the HQs), AI on either side, against Legacy, Greedy, and
Legacy with an extra tank and grunt. Current: **31 W 5 L 0 D**; the five losses are all
the outnumbered line-up.

Large boards (`large-*`): three radius-7 maps (all of SampleScene), twelve units a side
(two supply units each), the same three line-ups, AI on either side — 18 games, each
reporting how many unit-turns got a strategic-objective pull. Current: **15 W 3 L 0 D**,
losses again all outnumbered.

Campaign boards (`campaign-*`): two radius-13 maps, sixteen units a side with three supply
units, same line-ups — 12 games, the sides start 3–4 turns apart. Current: **10 W 0 L 2 D**;
both draws are against Greedy, which turtles: after 60 rounds we have tanks and grunts left,
it has eight grunts packed round its HQ, and nobody attacks.

`ONLY=behaviour|match|large|campaign` runs one group. The full 73 games take about 2 minutes.

Diagnostics:
- `PROBE=game FILE=<name> [ROUNDS=r] [SIDE=AI|Red|both]` — the board after every turn and,
  for each of our units, every option it weighed with the simulated enemy reply.
- `PROBE=chokes FILE=<name>` — how much enemy movement a grunt would cut on each tile.
- `PROBE=export` — regenerate the files from the builders in `Tests/Scenarios.cs`.
- `SET=field=value,…` — override any `AIProfile` field for a run.
- `PROBE=duel FILE=<name> [SET_BLUE=…] [SET_RED=…] [ROUNDS=r] [OUT=dir]` — this AI against
  itself with different settings, one game, a line per round (and a game log with OUT). The
  forecast fixes are switches: `forecastPins`, `forecastOwnSideBlocks`, `forecastDeadBlock`,
  `assistNeedsReach` (plus `deathPremium`, `worldTie`); the old behaviour is
  `forecastPins=false,forecastOwnSideBlocks=true,forecastDeadBlock=true,assistNeedsReach=false,deathPremium=0,worldTie=1`.

### How this AI chooses a move (defaults)

For line units, artillery, anti-air and recon (`choiceMode` 2): every tile it can stop on,
with the best attack from it, is played out against the enemy's reply (`AIWorld`): each
enemy, strongest first, fires its whole attack at the most valuable thing it can reach from
a tile it can actually get to, damaged units hit for less, retaliation applies. Each option
is scored by the material left on both sides (HQs at their 100,000 cost, a fallen HQ worth
the game, a capture under way counted). Options within one strength point of the best survive;
the winner among them is the one cutting the most enemy movement and supply (`AIDenial`) plus
the positional score (approach, supply, strategy). Supply units place themselves by
coverage minus the simulated hit where they will stand, and where they stop on the way.
Enemies are valued by the best blow they could land on us next turn (`threatTargetWeight`).

The world score also carries: a supply unit's death costs one strength of every unit only
it keeps supplied (`hubDependentsWeight`, both sides); a hit our not-yet-acted units can
finish this turn is an **assist**, counted as `assistWeight` (0.75) of a kill — its reply
and material removed in that proportion; and each surviving unit's best next blow counts,
ours at `ownStrikeWeight` (0.1), theirs against us at `enemyStrikeWeight` (0.25). HQs are not
targets in that term (blows on HQs are the reply's and the HQ-danger terms' job), and each
enemy's blow is discounted by how much of it our units can destroy first, since we move
before it does. Without those two rules the enemy term rewarded baiting: a tank took a 1-for-4
exchange because the enemy's reply then ended too far from our HQ to hit it the turn after.

Damage that will heal (a unit ending in its own supply; a supply unit only if connected)
has the point it heals back by its next turn (`healedPerTurn`, 1 — the game's rule) taken off
in the world score and in attack values; kills and damage that won't heal count in full.
Each enemy unit out of supply in the world after our move is worth `enemyUnsuppliedValue`
(100). Space (`spaceTileValue` 10, `spaceSupplyValue` 50): each tile our units could
move to next turn and each tile they could strike counts for us — only on contested ground,
within the enemy's three-turn reach (`spaceContestedOnly`) — and the enemy's move and strike
tiles count against us; each enemy supply tile our unit cuts counts for us.

Every simulated exchange uses the game's rules (`CombatMath.Exchange`): the defender's cover
counts against the shot, retaliation ignores cover, damage taken first weakens the answer, and
an **ambush** — the defender firing first — happens only when
`min(defender concealment, tile cover) + defender veterancy > attacker concealment + attacker veterancy`.
Open ground gives nothing; a grunt ambushes a tank in forest, hills, mountains or a city;
tanks never ambush. The attack preview on hover is `CombatMath.Resolve` itself, the function
the attack and the AI use — damage both ways, "!" on the attacker's number for an ambush,
"pin"/"ret"/"X" for a pinned defender, a retreat, a kill — and the animations play in the
resolved order (an ambusher fires first, after its attacker has moved in). A pinned defender's
extra point of damage is part of the resolved damage (it used to be skipped when it was lethal).

In the reply, the enemy's own units never block it (they pass through each other and may finish
on each other's tiles), a unit of ours destroyed during the reply stops blocking immediately
(reachability is re-run for every enemy still to act), and a defender driven back with nowhere
to go takes the pinned point — an HQ always. A destroyed unit costs `deathPremium` (1) more of
its strength points' worth than the points themselves, both sides. An assist needs a real
attack: a finisher with a free firing tile it can reach this turn. Options within `worldTie`
(0.25) of a strength point of the best world survive to the positional tie-break.

Each tile a unit could stop on is simulated once without firing and once for every shot it
could take from there; the simulation, not the attack score, picks. The simulated enemy picks targets net of the retaliation it takes.

Board read (`AIMapRead`): supply split into static (buildings, HQ), mobile (only there
because a supply unit stands where it does) and potential (what supply units could cover
after one move, worth `potentialSupplyValue` = ¼ of real supply in out-of-supply costs);
hop distance to our supply and the enemy's; and how many enemy turns until each tile can be
struck (3 turns deep, real movement, ~2 ms per read at radius 7).

Supply units plan first, as a ghost: the field is re-solved as if they had moved, so the line
attacks into supply that is coming; they still move last, re-planned on the real board
(`ghostSupply`). Their price (`hubPricing` 1): per unit supplied only by them, a tenth of its
cost, plus a sixth (one strength — what the game heals) if wounded, plus the attrition spared;
plus coverage of contested ground and of where units are heading; minus their own loss in the
simulated reply.
Retired along with the fixture tests: the 240/720-game batches and their tables.

## Integration

### Files that replace yours (11) — `.meta` files included, keep them

```
Assets/AIPlayer.cs      Assets/GameController.cs  Assets/Unit.cs
Assets/DestroyUnits.cs  Assets/Player.cs          Assets/Tiles/HexTile.cs
Assets/GoTo.cs          Assets/SupplyPoint.cs     Assets/LevelEditor.cs
Assets/Units/Tank.prefab        (adds: locomotion: 2 — Treads)
Assets/Units/Artillery.prefab   (adds: locomotion: 1 — Wheels)
```

Without the `.meta` files Unity regenerates GUIDs and every reference to `SupplyPoint` and
`AIPlayer` breaks. `Unit.cs` and `HexTile.cs` are edits to your code; diff-merge if you've
changed them since.

### New files (28)
`Assets/AI/` · `Assets/Buildings/` · `Assets/Supply/` · `Assets/Harness/` · `Assets/UI/`
· `Assets/Editor/` · `Assets/Units/{CombatMath,Locomotion,LocomotionInference,Transporting}.cs`

### Prefabs
Your current roster needs **no migration**: Grunt, Supplies and TempHQ are correctly
`Boots` by default; Tank and Artillery now carry their locomotion. For new units, run
**HexTactica → Prefabs → Validate Units**.

### Scene
1. Add a **`BuildingController`** (mirrors `UnitController`) if you use building HQs/cities.
2. Optionally create an **`AIProfile`** (`Create → HexTactica → AI Profile`) per `AIPlayer`;
   defaults are the sweep's best.
3. To watch a test: play mode, **HexTactica → Load Scenario File…**. For batch runs: add a
   **`BatchRunner`** and a **`ScenarioLoader`** listing save files, **HexTactica → Run Batch**.

### Game logs — send me a game

Every game you start with **Load Scenario File…** is recorded (turn it off under
**HexTactica → Game Log → Record Loaded Scenarios**; **Start Recording Now** records whatever
is on the board). Each game gets a folder under `<persistentDataPath>/game-logs/` — **Game Log
→ Open Logs Folder** shows it — containing:

- `game.log`: the board at the start of every turn, every move and attack by anyone (you,
  Legacy, this AI) with the damage both ways, and for this AI its board read (supply split,
  enemy reach, objectives, turn order) and every decision with the options it weighed and the
  simulated enemy reply.
- `turn-NNN-<side>.dat`: the board at the start of every turn as a scenario file, so any
  moment can be reloaded — in Unity or headless — and replayed.

Zip the folder and attach it. It is written as the game goes, so stopping play mode mid-game
keeps everything up to that point. Headless, `PROBE=record FILE=<name> [ROUNDS=r] [OUT=dir]`
records the same way and checks that the last snapshot reloads to the same board.

### Debugging the AI in the editor
- `AIPlayer.onDecision` — callback with every unit's action, score and reason.
- `AITactical.DebugUnit` + `DebugLog` — explains one unit's top candidate moves term by
  term (value, delay, risk, supply cost, stranding). Set `DebugBreakdown` for components.
- `AIProfile.annotateBoard` writes each decision's reason onto the tiles.

---

## Running the tests

```
bash Tests/run.sh /path/to/HexTactica/Assets      # after copying this package in
```

Needs Mono (`apt install mono-mcs`, or `brew install mono`). The scenarios use your real
unit prefabs and terrain assets, so point it at your project. Nothing in `Tests/` belongs
in `Assets/`; the Unity menu finds `Tests/Scenarios` next to your `Assets` folder.

---

## Decisions for you (flagged, not changed)

- **`calculateDamage` floors at 1.** Your doc says "0.99 is 0 kills". Every hit does ≥1.
- **Retreat after nearly every non-lethal hit.** Per the doc's rule (compare casualties),
  any hit without retaliation — including every artillery shot — pushes the target back.
  Probably not intended for ranged fire.
- **Unit costs drive AI priorities hard.** TempHQ 100,000 and Supplies 25,000 make them the
  dominant targets. That's coherent, but it's a design choice as much as a balance one.

## Still open

- **Objectives are direction, not orders.** A unit more than 3 turns from contact is pulled
  toward the best objective whether or not the army has the force to take it
  (`objectivesNeedForce` restores the old 2.5×-the-defenders and no-starving-trip gates). On
  radius ≤ 7 boards nothing is ever 3 turns from contact, so they only act on campaign boards.
  Their effect there is small: same results, one draw turned into a win.
- **Transport presentation**: cargo isn't hidden or parented, and the `LOAD_CONFIRM` /
  `UNLOAD_CONFIRM` click states have no handler. Transports work for the AI only.
- **Placing buildings** in the level editor UI (they load from saves only).
- **Production**, **air**, **naval**.
