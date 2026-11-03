# How the AI was made faster

The AI's thinking time was cut without changing a single decision. Every speed change is behind its own flag in
`AIProfile`, described as "Speed only ... Same answers", and was accepted only if a traced game played move for move
the same with it on and off. This note says how, so new AI code can follow the same pattern.

## The three principles

1. **Cache what can be re-used, and keep it as long as it is still true.** Work out a thing once, keep it, and throw
   away only what an action actually changed — not everything after every action.
2. **Be ready for parallel access.** Thinking happens on copies of the board that any thread can read, with
   per-thread state, so independent pieces of work can run side by side.
3. **Do the work in small pieces, where and when the action happens.** Instead of one big preparation pass at the
   start of the turn (every unit's reach, all enemy threat, all supply), work out only what the next decision needs,
   when it needs it, and after an action re-do only the part of the board that action touched.

## 1. Caching that survives actions (`keepCaches`, `AIMapCache`)

- The AI's view of the board (`AIMap`) holds derived data: each unit's reach (move fields), the enemy's strike reach,
  space, supply networks, threat.
- It used to drop every cache after every action. Most of them only depend on the ground near one unit, and one
  action changes the board in a few places.
- `AIMapCache` records what changed since the map last read the board:
  - each unit's **mark** (tile, strength, transport, acted or not, fuel, ammunition, suppression, supplies);
  - the **changed tiles** (every tile a unit left or took: moved, killed, retreated, boarded, put down, bought);
  - terrain and building changes (owner, capture, damage, store).
- Each cache is kept unless what it depends on is in that change set. A unit's move field survives if nothing moved
  within its reach; a supply network survives if no supply unit, building or blocking unit changed.
- The same holds from turn to turn: what the opponent's turn left standing is kept.
- Caches are keyed by **unit**, not tile, so a unit that moves invalidates its own entries and nothing else.
- Short-lived structures are pooled rather than allocated anew (move fields are released back with `AIPath.Release`),
  which keeps garbage collection out of the turn.

## 2. Ready for parallel access (`BoardView`, `AIParallel`, `parallelForecast`)

- `BoardView` (`Assets/World/BoardView.cs`) is a copy of the board's state an AI thinks on. While a thread thinks, the
  game's own code (`Unit`, `HexTile`, `Building`, `FogOfWar`) reads through `BoardView.current`, which is
  **thread-static**: each thread sees its own copy, and simulating a move on it changes nothing real.
- Under fog, the copy is the board **as that side knows it** (enemies unseen are off it, those seen lately stand where
  they were last seen), so fair play and threading come from the same mechanism.
- Things the copy needs that only the main thread may ask Unity are worked out when the copy is made (for example who
  "knows all" for fog), never later from a worker thread.
- Per-decision settings travel with the thought as thread-static fields, set at the start of the thought and restored
  after it, so a thought redone on another thread leaves nothing behind.
- `AIParallel` runs independent work on worker threads. With `parallelForecast`, each option a unit weighs (a stop and
  a shot) is forecast — the enemy's reply played out — on its own copy, all at once; each result lands on its own
  option, so the order of completion cannot change the answer.
- Acting always happens on the main thread, on the real board.

## 3. Small pieces at the moment of action, not one big pre-pass

- **Re-read only what an action touched.** After each action (`RefreshAfterAction`), the map recomputes who is alive,
  where supply reaches and who the enemy can now fire on, using the change set above instead of rebuilding the board
  read.
- **Re-decide only units an action affected** (`coordinationReuse`). In the coordination pass every unit in contact
  decides once before anyone acts. On its turn a unit keeps that decision unless something within its reach or the
  enemy's has moved, been hurt or appeared, or its tile or target is gone. The trace says why each unit decided again
  ("a unit within reach changed ...").
- **Bound the work to where it matters.** The reply forecast skips our units out of every enemy's reach, and counts
  our reach once per acting unit. `vacatedReach` works out once per decision where an enemy could go once our unit
  leaves its tile, and re-uses it for every option that does not block it.
- **Cheap tests before expensive ones.** For example, before working out a rider's full walk-and-strike from a
  landing tile, a distance check against every enemy and building rules most tiles out. Results are cached by (rider,
  tile) for the rest of the decision.
- **Use idle time** (`speculate`). While the opponent plays, once it has moved most of its units
  (`speculateAfterShare`) and the board has been still for a while (`speculateQuietFrames`), the AI warms its caches on
  a low-priority worker thread. Its own turn keeps whatever is still true (`keepCaches`).

## How a speed change is proven safe

- Measure first: `PROBE=perf SECTIONS=1` prints every turn's time by section (`AIPerf.Begin`/`End` around each part:
  board read, coordination, each decision kind, acting, refresh). Optimise the sections that dominate.
- Every speed flag must give the **same answers**: run a traced game with the flag on and off and compare the traces
  line by line, ignoring timings. Any difference is a bug in the optimisation, not a new behaviour.
- Check worst-turn time as well as the total: players feel the slowest turn.

## Adding new AI code without losing the speed

- Read derived data through the map's caches; when you add a cache, say what invalidates it, and key it by unit.
- Keep state per decision or thread-static, never in plain statics a worker thread could see half-written.
- Prefer working something out when a decision needs it over a turn-wide pre-pass; if a pre-pass is needed, make it
  incremental (update what an action changed).
- Wrap new work in an `AIPerf` section so it shows up in the profile.
