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
**Red, id 0** (the opponent: a baseline AI or you). Tiles outside a scenario's map are Water.

**In Unity:** enter play mode in SampleScene, **HexTactica → Load Scenario File…**, pick a
file. The test block attaches Red's AI (`Legacy` = the original AttackMove AI, ported
faithfully; `Greedy` = defend-only) and sets who moves first, then it plays itself.

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
losses again all outnumbered. Objective pulls: **0** — see "Still open".

`ONLY=behaviour|match|large` runs one group.

Diagnostics:
- `PROBE=game FILE=<name> [ROUNDS=r] [SIDE=AI|Red|both]` — the board after every turn and,
  for each of our units, every option it weighed with the simulated enemy reply.
- `PROBE=chokes FILE=<name>` — how much enemy movement a grunt would cut on each tile.
- `PROBE=export` — regenerate the files from the builders in `Tests/Scenarios.cs`.
- `SET=field=value,…` — override any `AIProfile` field for a run.

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
and material removed in that proportion; and each surviving unit of ours adds its best
next blow at `ownStrikeWeight` (0.1). The enemy-side equivalent (`enemyStrikeWeight`) is 0:
at 0.25 it turned wins into draws.

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

- **Strategic objectives never fire on SampleScene.** A unit ignores objectives with contact
  within 3 turns; a tank moves 6 and artillery reaches 10, so on a 15-hex board every unit is
  within 2 turns of contact from turn 0. Testing them needs a bigger board: SampleScene's
  HexGenerator `width` (15) and the camera size would have to grow. Two more gates also block
  them in an even fight — an objective needs 2.5× the defending force, and units won't take a
  trip they can't get back into supply from. (A third gate, objective reach ≤ 3 turns, made
  them impossible on any board; it is now 8.)
- **Transport presentation**: cargo isn't hidden or parented, and the `LOAD_CONFIRM` /
  `UNLOAD_CONFIRM` click states have no handler. Transports work for the AI only.
- **Placing buildings** in the level editor UI (they load from saves only).
- **Production**, **air**, **naval**.
