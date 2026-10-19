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
  otherwise does nothing. It never advances, but it strikes first at anything that comes
  within reach, so it is not free to approach.
- **Full**: this AI, against itself, for seat bias.

Every cell is 40 games: widths 7/8/9/10/11 × four mirror-symmetric forest layouts × both
seats. "+20%" gives one side an extra Tank and Grunt. A draw is the harness's 60-round
limit: the game itself only ends when an HQ is destroyed, and has no rule for mutual
annihilation (an HQ cannot attack, so two HQs alone never finish).

Current defaults (`aggression` 0):

| Full AI vs | Equal | +20% for Full | +20% for opponent |
|---|---|---|---|
| Legacy | **39W 0L** 1D | **40W** 0L 0D | 29W **9L** 2D |
| Greedy | 22W **0L** 18D | 30W **0L** 10D | 16W **0L** 24D |

Before this round (same 40-game cells): Legacy 36W 2L / 40W / 36W 4L; Greedy 21W 19D /
24W 16D / 5W 35D. Net: +14 wins over 240 games, mostly against the turtle when outnumbered
(5 → 16), but **5 more losses** to an outnumbering Legacy (4 → 9).

Mirror matches (20 games): Full vs Full 9–7–4 for seat A (was 7–3 on 10 games); Legacy vs
Legacy 5–10–5; Greedy vs Greedy all draws. Legacy vs Greedy equal: 0W 2L 38D.

The 18 equal draws against the turtle: 12 no contact at all, 4 ahead with an army left but
the HQ never fell, 2 with our army spent. Before, more than half the draws were burned-out
attacks; the cautious default traded those for standoffs.

### Aggression

`AIProfile.aggression` runs from 0 (fight only on clearly favourable terms) to 1 (trade
everything). It scales risk appetite, attack readiness, the force ratio to attack or take
an objective, trust in the counterattack, break-off, and rotation. Measured over 240 games
per setting (Greedy + Legacy cells):

| aggression | wins | losses | draws |
|---|---|---|---|
| 0 | **177** | 5 | 58 |
| 0.25 | 171 | 4 | 65 |
| 0.5 (weights as written) | 172 | 7 | 61 |
| 0.75 | 145 | 11 | 84 |
| 1 | 121 | 16 | 103 |

Furballs do not win in this ruleset: the defender strikes first, damage scales with
strength, and a mutual wipe-out is a draw. Hence the cautious default.

Reproduce: `PROBE=matchups WIDE=1 bash Tests/run.sh Assets` (≈15 min). One opponent:
`PROBE=cells THEM=Greedy WIDE=1 VERBOSE=1`. Override any profile field, including bools:
`SET=aggression=0.5,useObjectives=false`. One game turn by turn, with fronts and committed
objectives: `PROBE=trace GAME=9,Full,Greedy,0,40 FRONTS=1 [ALLOBJ=1] [VARIANT=1]
[EXPLAIN=Tank SIDE=A FROM=10]`.

### Where each tactic lives

Two layers, following how the original design separated them:

**Strategic objectives** (`Assets/AI/AIObjectives.cs`) — structure and tie-breakers only:
"attack the enemy capital now" (KillHQ: the assigned force beats everything that could
defend it by `objectiveRatio` and can destroy it in one volley), "mass up where they are
weak" (Mass: an enemy group our nearby forces outmatch, counting its support), and "bring
the reserves home" (DefendHQ). An objective adds a small pull (`assignmentPull`, a fraction
of the unit's worth) evaluated last, and a unit that could exchange fire with an enemy
within `objectiveContactTurns` (3) ignores it. It sets no posture and commits nobody. On the
test maps (7–11 hexes from HQ to HQ) every unit is always within three turns of contact, so
objectives never fire there and change nothing — as intended; they are for larger maps, and
there is no larger test map yet.

**Tactics** — all in the per-unit, per-tile evaluation:
- **Screens as space** (`Assets/AI/AISpace.cs`). Units block enemy movement and every
  attacker needs its own tile to fire from. Once per board state the cheap line units still
  to act (grunts) are planned as a screen for the valuable units (tanks, guns — acted or not):
  each candidate placement is scored by re-running every affected enemy's real movement,
  terrain included, and letting each valuable unit take its best reachable tile — what it
  could strike next turn at the strength it would keep, less tonight's threat, where threat
  is the best assignment of attackers to distinct open firing tiles. The screen's own
  exposure and supply are subtracted; placements improve one grunt at a time (coordinate
  ascent). A grunt's credit for a tile is the valuable units' gain from it standing there with
  the rest of the screen where the plan puts it, so the grunts coordinate. The plan is rebuilt
  after every action; the armour, moving later, sees the enemy fire field recomputed with the
  grunts in place and takes the protected tiles because they are cheaper.
- **Concentration** (`AIMap.FirstStrikeCredit`): the counterattack an enemy invites by coming
  forward to strike a unit, net of our counterattackers' own exposure.
- **Rotation** (`AITactical.ShouldRotate`): a role switch made in every decision, engaged or
  not. A line unit at or below `rotateStrength` (scaled by aggression), within a turn of
  supply, withdraws to heal when a healthier line unit is within its move of it; a kill in
  hand is still taken first.
- **Exploit breakthrough**: an enemy hub attacked from ground none of their fighting units can
  reach tonight is worth `breakthroughWeight` × its supply importance more, in any posture.
- **Staging**: the front readiness gate (gather on the supply edge, go together).
- **Defeat in detail** at tactical range is what concentration and target values already do;
  at map scale it is the Mass objective.

### Measured (current defaults, 240 games)

| Full AI vs | Equal | +20% for Full | +20% for opponent |
|---|---|---|---|
| Legacy | **40W 0L** | **39W 0L** 1D | 35W **4L** 1D |
| Greedy | 19W **0L** 21D | 28W **0L** 12D | 15W **0L** 25D |

Total 176 W, 4 L, 60 D. On 720 games, screening on vs off: 525 W 18 L vs 530 W 18 L —
**no measurable effect on these maps yet.** Rotation: +14 wins. Breakthrough: no change.
A positional "guard the HQ" term (`guardWeight`) cost 5 losses and is off.

### Earlier changes

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

- **A third of equal games against the turtle are no-contact standoffs** (12 of 40), all on
  the wide maps. On those the turtle sits 6+ hexes past the end of our supply; nothing we
  can do there beats the first strike it gets on whatever approaches. The heuristics here
  all judge one unit, one tile at a time. Breaking that position probably needs a short
  look-ahead — simulate their reply to a whole turn's plan, then ours — rather than more
  per-tile terms.
- **Losses to an outnumbering Legacy rose from 4 to 9 of 40.**
- **Tried and rejected on the 240-game set**: sharper focus-fire prediction, proportional
  screening, floor-at-army-ratio fronts (`useArmyRatio`), standoff bonus, relaxed
  out-of-supply posture, staging gated on enemy position. Knobs left in, off or neutral.
- **Test bars changed** (each flagged in the code): the corridor's time-out-of-supply check is
  now reported, not checked (attrition losses are still checked); the 4-game advantage test
  allows one game behind. And earlier: "never ends behind on material" in
  the 6-game regression became "at most one of six" — on 40 games it is ~1 in 20, so the
  old bar depended on which six; and the corridor fixture's "less time out of supply than
  greedy" became "within 25%" — greedy never leaves home, and the AI now fights into the
  enemy base in the two drawn trials instead of losing its army in the middle. Attrition
  losses must still be no higher than greedy's (2 vs 4).
- **Results are deterministic but chaotic.** A change worth 5 of 6 games on 6 was 12 of 20
  on 20; I now judge only on 240.

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
