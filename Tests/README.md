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

## Measured results (real prefabs, round-robin)

Three opponents, all deterministic:

- **Legacy** (`Assets/Harness/LegacyAI.cs`): a faithful port of the original `AIPlayer` /
  `AttackMove`, including the three-move approach fallback, the GoTo-to-(0,0,0) objective
  fallback, and `Retreat` being a no-op. This is the opponent that attacks.
- **Greedy**: a reactive turtle. Each unit strikes whatever it can reach this turn and
  otherwise does nothing. It never advances, but it does strike anything that comes within
  reach, so it is not free to approach.
- **Full**: this AI, against itself, for seat bias.

Every cell is 20 games: widths 7/8/9/10/11 × two mirror-symmetric forest layouts × both
seats. "+20%" gives one side an extra Tank and Grunt. 60-round limit; a draw is a timeout.

| Full AI vs | Equal | +20% for Full | +20% for opponent |
|---|---|---|---|
| Legacy | **19W 1L** 0D | **20W** 0L 0D | **18W 2L** 0D |
| Greedy | 12W **0L** 8D | 10W **0L** 10D | 3W **0L** 17D |

For comparison, Legacy vs Greedy: 0W 1L 19D equal. Mirror matches: Full vs Full 7–3 for
seat A (0 draws); Legacy vs Legacy 3–5–2; Greedy vs Greedy all draws.

Reproduce: `PROBE=matchups WIDE=1 bash Tests/run.sh Assets` (≈4 min). Quick iteration on
one opponent: `PROBE=cells THEM=Greedy WIDE=1 VERBOSE=1`. Any profile field can be
overridden: `SET=cohesionWeight=0.4,assaultReadiness=0.7`. One game, turn by turn:
`PROBE=trace GAME=9,Full,Greedy,0,40 FRONTS=1 [VARIANT=1] [EXPLAIN=Tank SIDE=A FROM=10]`.

### What changed to get here

Each of these was found in a trace of a lost or drawn game, not by reading the code.

- **Force ratio by reach, not radius.** Fronts counted units within a fixed radius, so the
  enemy army sitting at home outnumbered our forward units and equal armies read as 0.2–0.9.
  Fronts now count what can arrive within `frontReachTurns`.
- **Staging.** An attack beyond our supply waits on the edge of our supply nearest the
  objective until `assaultReadiness` of the attacking force is healthy and gathered there,
  and once in, keeps going until it falls below `assaultBreakoff`, then regroups. Before
  this, units went in the order they arrived — tank, then grunts, then guns — and wounded
  units pressed on. Readiness is judged per objective, across every front aimed at it.
- **Cohesion.** In an assault, ending the turn ahead of the rest of the assault costs
  `cohesionWeight` of the unit's worth per hex. The tank (move 6) no longer parks beside the
  enemy HQ while the grunts (move 3) are three hexes back.
- **HQ chip damage was priced at the HQ's 100,000 cost.** Three points on an HQ that heals
  were worth more than a tank, so tanks dived the HQ alone. A hit that does not kill is now
  worth `hqSetupFactor` of the win only if the units that will survive tonight can finish it
  next turn; otherwise it is chip.
- **Defending the HQ.** Nothing said that losing the HQ loses everything: two Legacy tanks
  drove at it while our tank went on an advance. Each enemy that could strike our HQ in one
  or two turns now carries a share of the HQ's stake, and hitting it is worth
  `hqDefenseWeight` of that.
- **Siege.** Three grunts sat in supply for fifty turns, thirteen hexes from an undefended
  HQ they could kill in one volley. When a front's healthy units can destroy the HQ in one
  volley and outmatch what defends it, the objective pull is multiplied by `siegePull`.
- **Hub advances toward where the army is going** (intents), but only to connected positions.

### What is still not good enough

- **Against the turtle, 8 of 20 equal games are draws, and an extra Tank and Grunt does not
  improve that.** The reactive defender gets the first strike on anything entering its reach,
  heals in its own supply, and on the wider maps sits 6+ hexes beyond the end of ours. Our
  wins come from its units lunging out and being counter-punched; when they don't, nothing
  breaks the position. Draws are no-contact standoffs or an assault that trades evenly and
  runs out of army.
- **Outnumbered by the turtle, it mostly declines to attack** (17 draws, 0 losses). That is
  defensible but not clever: it does not try to bait.
- **Mirror seat bias**: Full vs Full is 7–3 for seat A over 10 games — moving first is worth
  something, and there may be an asymmetry left to find.
- **Tried and rejected** (measured on the 20-game set, no improvement or worse): sharper
  focus-fire prediction (`focusExponent` 2 and 3), relaxing the out-of-supply posture ratio
  to 0.85, and gating staging on enemy position instead of the front centre. The knobs are
  left in the profile at their neutral values.
- **Results are deterministic but chaotic.** On the old 6-game set, one change looked like
  5 wins of 6; on 20 games it was 12 of 20. Don't trust a tuning result on fewer games.

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
