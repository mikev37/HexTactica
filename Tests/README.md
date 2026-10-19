# HexTactica — supply-aware AI, mechanics, and test harness (reviewed)

This package replaces the previous one. It is the result of an adversarial review of
that work, run against **your real prefabs** rather than invented fixtures, which found
that several things I told you worked did not.

- **132 headless tests, all passing**, reproducible from a fresh clone of your repo with
  the shipped runner (`bash Tests/run.sh <your Assets folder>`).
- **Whole-project compile check:** your original repo and this package produce identical
  compiler output (same shim gaps in the same 11 untouched rendering scripts). These
  changes introduce no compile errors anywhere.
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

## Measured results (real prefabs vs. a passive "greedy" baseline)

**Read this first.** The opponent in every game below is `BaselineAIPlayer` (greedy): each
unit takes the best attack available *this turn*, and a line unit with nothing in reach
**does nothing**. It is **not** a faithful port of the original `AttackMove`, which had a
three-move approach fallback and an objective fallback; those were left out. Greedy
therefore never advances. "Never loses" is measured against an opponent that barely fights
back, and every draw is the AI failing to press a passive enemy — not careful defence.

| | Supply-aware AI | Greedy baseline |
|---|---|---|
| Equal armies, 6 games (3 widths × 2 seats) | **2 wins, 0 losses**, 4 draws | 0 wins |
| Games ending behind on material | **0** | — |
| +20% advantage, 4 games | **0 losses, 0 behind**, 1 win | — |
| Fixture corridor, 4 games (invented units) | 2 wins, 0 losses | 0 wins |
| Units lost to attrition (corridor) | **0** | 6 |
| Unit-turns out of supply (corridor) | 52 | 70 |
| Transport delivery | Boards, rides, unloads on turn 2 (walking: turn 3), stays connected, army 4/4 supplied | — |

**A parameter sweep (16 profiles × 10 games = 160 games) found zero losses and zero
games behind on material in every configuration.** That is the robust result.

### What is not as good as I'd like

- **Draws on wide maps are a failure to attack.** The opponent sits at home, so the burden
  is entirely on this AI, and it declines. Supply reach (HQ 8 + one hub's 4 ≈ 12 tiles)
  ends ~6 tiles short of the enemy HQ on the 9- and 11-wide maps, which is part of why, but
  a 20% advantage converting 1–2 times in 4 against a passive opponent is a weak result.
- **Untested against an opponent that attacks.** The only AI-vs-AI mirror match uses
  invented fixture units and only checks the game completes. Next steps: port the original
  `AttackMove` faithfully as a baseline, add real-prefab mirror matches, and re-measure.
- **I lowered one test bar.** "Advantage converts in 3 of 4" became "never loses, never ends
  behind; conversion reported". No profile in the sweep exceeded 2 of 4, so the old bar
  would only have been met by tuning to these four games.
- **Supply-cutting** ("aggressive: go deeper into enemy supply range", from your doc) is
  implemented and changed no game in the sweep. Reported as inert on these maps.

---

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

### New files (27)
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
3. For batch runs: add a **`BatchRunner`** and a **`ScenarioLoader`** listing your save
   files (e.g. `save.dat`), enter play mode, **HexTactica → Run Batch**. Results go to
   `persistentDataPath/harness_results.csv`.

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

Needs Mono (`apt install mono-mcs`, or `brew install mono`). The tests read your real unit
prefabs, so point it at your project, not at this package.

`PROBE=game|wide|decisive|transport|sweep bash Tests/run.sh <Assets>` runs diagnostics
instead (traces and the parameter sweep). Nothing in `Tests/` belongs in `Assets/`.

---

## Decisions for you (flagged, not changed)

- **`calculateDamage` floors at 1.** Your doc says "0.99 is 0 kills". Every hit does ≥1.
- **Retreat after nearly every non-lethal hit.** Per the doc's rule (compare casualties),
  any hit without retaliation — including every artillery shot — pushes the target back.
  Probably not intended for ranged fire.
- **Unit costs drive AI priorities hard.** TempHQ 100,000 and Supplies 25,000 make them the
  dominant targets. That's coherent, but it's a design choice as much as a balance one.

## Still open

- **Transport presentation**: cargo isn't hidden or parented, and the `LOAD_CONFIRM` /
  `UNLOAD_CONFIRM` click states have no handler. Transports work for the AI only.
- **Placing buildings** in the level editor UI (they load from saves only).
- **Production**, **air**, **naval**.
