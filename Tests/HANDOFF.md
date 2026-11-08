# Handoff: the HexTactica AI, as of 2 Nov 2026 (branch `claude/ai-speed`, commit `a45ec72` and after)

Written at the end of a long session, for whoever picks this up next (human or AI). It says what the code is, how
the AI thinks, how to test it, what was changed and found in this session, and what is open. Where something is
my understanding rather than something I verified, it says so.

---

## 1. The owner's standing rules (follow them; they were given repeatedly)

- Answer questions before building. Don't plan stories or parcel work out: do it.
- Never delete anything yourself; never automate destructive processes. Never push to `main`; work on `claude/ai-speed`.
- **Every game you run is traced** (`DUELTRACE=1`). A result you cannot explain from its log is worthless. Read the
  log before running another game. Run small traced games, not big batches with summaries.
- Report honestly. Mixed results: report and stop. Results worse: dig into the AI and its logs (first divergence)
  to tell an AI mistake from a flawed idea. Merely acknowledging an issue isn't addressing it.
- New behaviour goes behind an on/off flag in `AIProfile`; keep `Assets/AI/TestProfile.asset` in sync with code defaults.
- Every test is a Unity-loadable scenario file (`Tests/Scenarios/*.dat`, written by `PROBE=export`).
- AI thinking time matters more than strength. HQ is intentionally worse than an FOB. Fog supply display must not
  reveal enemy positions. Never make visuals in code. Don't touch the prefabs. Legacy AI is not used. No noise.
- Game rules take precedence over the AI: the AI must not change how the rules work (no AI "what-if" switches in
  game code); AI-only helpers live in AI code.
- No hard-coded behaviour by unit **name**: decide by capability (what a unit carries, its locomotion, its slots).
- Scenarios are not symmetric: look at each side's units, buildings and start positions before reading a result.
  Judge a change side by side (new as Blue v old as Red, and old as Blue v new as Red), never by a flag-off "control".

## 2. Repository and harness

- Unity C# hex wargame. Game code in `Assets/` (units `Assets/Unit.cs`, tiles `Assets/Resources/Tiles/HexTile.cs`,
  buildings `Assets/Resources/Buildings/Building.cs`, supply `Assets/Supply/SupplyNetwork.cs`, fog `Assets/Fog/FogOfWar.cs`,
  transport rules `Assets/Resources/Units/Transporting.cs`, combat `Assets/Resources/Units/CombatMath.cs`).
- Current AI: `Assets/AI/*.cs` (namespace `HexTactica.AI`) + `Assets/AIPlayer.cs` (global namespace, the component).
- Old AI: `Assets/OldAI/*.cs` (namespace `HexTactica.OldAI`), component `OldAIPlayer` (see section 6).
- World layer shared by both AIs: `Assets/World/BoardView.cs` (copies of the board an AI thinks on; game code reads
  through `BoardView.current` while an AI thinks) and `Assets/World/AIController.cs` (`IAIController`,
  `AIControl.Of(player)`: all the game asks of an AI — playing, thinking time, fog fairness).
- Headless harness: `bash Tests/run.sh Assets` compiles the files listed in `Tests/logicfiles.txt` with Mono `mcs`
  against shims (`Tests/FunctionalShims.cs`; the shim does **not** call `Start()`/`Awake()` — set fields by hand) and runs
  the checks in `Tests/Scenarios.cs`. Add new .cs files to `logicfiles.txt`. New Unity files need a `.meta` with a new GUID.
- `PROBE=export` rewrites the scenario files; then `bash Tests/run.sh Assets` runs every check (117 at last count).
  `NAME=ai-` filters checks by name prefix. `CHECKTRACE=1` traces a check (only where the check hooks it).
- Duels: `PROBE=duel FILE=Assets/Resources/Scenarios/<Map>.dat ROUNDS=25 PURCHASES=on FOG=on|off`.
  `SET_BLUE="flag=value,..."` / `SET_RED=...` tweak one side's profile (note: when a side has SET_BLUE/SET_RED, a plain
  `SET` is ignored for that side). `OLD=Blue|Red` plays that side with the old AI. `DUELTRACE=1` logs every decision of
  both sides and the supply chain's reasoning; `DUELTRACE=full` also every option each unit weighed.
- Duel log shape: per round `round N: Blue u units, material m | Red ... | out of supply (unit-turns so far) ..., lost
  to attrition ...`; at the end `think time`, `transports`, `end:` (units, material = cost × strength, buildings,
  income, gold), `winner`. Games are deterministic: same code + same settings = same game.
- Scratch tools (session scratchpad, not in the repo; re-create if gone): `ovn.sh` (old v new on maps, both sides,
  traced), `h2h.sh` (flag A v flag B, traced). Always run batches from a **copy** of `Assets`+`Tests` (a snapshot), so
  edits made meanwhile don't reach games in progress. Don't use `pkill -f`/`pgrep -f` patterns that match your own shell.
- Maps (`Assets/Resources/Scenarios/`): BigMapLakes, Biomes, BridgeCrossing(-swapped), BushWar, ChanceMeeting,
  ForestClearing, Korea, LogisticsZigzag, LogisticsZigZag2, MountainPass, RiverCrossing. BridgeCrossing, -swapped and
  LogisticsZigZag2 gave identical numbers in a run: probably the same map (not verified by opening them).
  No-build (no building makes fighting units; HQs make only supply): RiverCrossing, ChanceMeeting, MountainPass,
  BridgeCrossing. BushWar is lopsided (Blue: HQ only; Red: HQ + Airport + ArmyBase). ChanceMeeting: Red starts with
  3 more APCs and beside most cities; both HQs in corners reach ~3 tiles, so every supply unit starts unlinked.
  The owner's test maps: RiverCrossing (fog off), ChanceMeeting (fog on), Biomes (build map, fog off).

## 3. How the AI thinks (my understanding of the current AI)

**Turn loop (`AIPlayer`).** Each turn: prepare (board read `AIMap`, campaign, fronts, logistics, supply chain plan,
ghosts, landings, coordination), build a unit queue, then repeatedly take the next unit, decide (`DecideFor`), act
(`Run`), re-read. Thinking runs on a board copy (`BoardView`), possibly on worker threads (`AIParallel`); acting is on
the real board on the main thread. `Run` marks the acting unit done (`active = false`) after any action, including a
wait. Exceptions: a rider that gets off by itself keeps its turn (self-drop); a unit put down from a transport is moved
to the front of the queue and acts next (`freshUnload`).

**Turn order.** `AIRoles.Order` sorts by role priority (artillery with a target first, high-firepower units with a
target, AA, then slower before faster; supply 85; transports 90). Then supply units are moved to the very end
(rearmost first), a chain link's truck just after its link, coordinated battle plans to the front, aircraft landing
this turn last. With `assaultOrderWithLine`: plans, then artillery firing and non-infantry with an enemy in reach, then
each loaded transport followed by its riders, then the rest; empty **troop** transports last (supply-held ones keep the
supply order). With `waitTwice`: a unit that would wait is set aside and decides again after everyone else; waiting
again, it skips.

**Decision layers for a unit (`DecideFor` → `DecideForUnit`).** Roughly: carried-supply trucks are driven by their
supply unit (`CarriesSupply`); a rider put down to strike makes its planned strike (`AIAssault.CarryOut`); coordinated
plan steps; riders aboard (`AIAssault.RiderAction`); assault transports with riders (`AIAssault.TransportAction`);
objectives that override tactics; supply units (chain / logistics / planner); transports (assignments, fetch, held for
supply); otherwise `AITactical.Decide` (score every reachable stop and shot with `AIWorld.Evaluate`: the enemy's reply
played out, supply, space, buildings). After the decision, boarding is weighed against it (`BoardOrNot`), the landing /
supply-link guard may send it back to tactics (that marks an assault transport as fallen back), aircraft fuel guard.

**Campaign (`AICampaign`, `AIStrategic`).** Candidates: our HQ under threat (DefendHQ), every building not ours
(Secure), enemy groups (Clear), chokepoints/road junctions (Hold), scouting (Scout), masses. Each gets a value and a
need = enemy power expected there × `objectiveForceRatio` (1.1; DefendHQ/Clear use a lerp by `objectiveSpread`).
"Enemy expected there" = seen enemies + an estimate of unseen ones spread over the dark ground — even on a no-build map,
where the total is known exactly (`AIKnowledge`: "enemy could have X, we know of Y"); that is why single units get sent
at objectives early under fog. Allocation: kept objectives keep their units (unless broken: power below
`objectiveReplanLoss` 0.5 of formed power, or fell back); then by priority, at most 1 + round(`objectiveSpread` 0.5 ×
(candidates − 1)) pursued, each filled to its need (a capturer first for Secure); a fallback takes the best our supply
can carry if nothing was taken on. Fronts get posture Aggressive/Defensive/staging. A task force goes once the healthy
power gathered on its staging ground is `campaignGatherShare` 0.8 of its need; once going it keeps going while
committed ≥ need × `assaultBreakoff`. Front-level fallback is **off** (`campaignFallbackRatio` 0) in both AIs; units pull
back only through regrouping (units worth ≥ 3000 without local superiority) and their own withdraw. Reserve: of units
on no task force, `campaignActiveReserveShare` 0.1 stay with the fronts, the rest (mobile ones) gather as a central
reserve, released when knowledge confidence ≥ 0.6 or all out; regrouping sends it to the most pressed front. Observed:
when needs jump (enemy revealed), allocation absorbs the reserve into fronts that are already short.

**Supply.** Game rules: `SupplyNetwork` (HQ/FOB/cities project supply over ground; a supply unit relays when it stands
on supplied ground; a supply unit aboard a transport counts as linked while the transport is within a move of supplied
ground). AI side: `AISupplyChain` (an "inchworm" from the HQ toward the objective: rearmost supply unit first, each
placed on the most forward safe tile still supplied by the links behind, not ahead of the army, only if it brings
supplied ground nearer the objective; else spare), `AILogistics` (per-front supply duties, rides, chain trucks),
`AISupplyPlanner` (spare supply units), `AITransportPlanner` (walk-or-ride plans). Supply units aboard never get
out (`supplyStaysAboard`), except a transport about to be destroyed. A supply unit aboard is placed by the same rule as
on foot (`unifiedLinkPlacement`). Gap found: a cut-off supply unit with no supplied tile in reach is marked spare and
sent toward its front; nothing makes it walk toward supplied ground (or a transport) first.

**Transports (`AIAssault`, behind `assaultRides`).** Roles by capability each turn (`AIAssault.SupplyTransports`):
held for supply if carrying a supply unit, on a logistics duty, within a move of a supply unit on foot it could carry,
or — on a no-build map — any that can carry supply (not capped; owner wants at most one per supply unit). Infantry may
use a supply transport's spare slot once its supply unit is aboard and the ride takes it nearer. Troop transports
(armed ones too: Recons) carry riders toward the fight; the transport plans the rider's strike (stand tile + target or
building, survivable) and puts it down, the rider carries it out; riders also get off at the front line, to hold the
line, or when the transport's move was not its assault ride; armed transports fire first, then unload, unless the
return fire would kill them. Medevac: wounded units (≤ 3 strength, not about to take a building) ride to a building that
heals. Empty troop transports fetch riders or return to a base. Boarding is weighed against the unit's own move (only a
walk gives way, and only if the ride gets it nearer).

**Production (`AIProduction`).** Slot pass + upgrade pass; value = lifetime value against the enemy mix (overkill,
shielding by tiers, reach), transports valued by the turns they save riders far from the fight, infantry beside an empty
troop transport valued higher. Capture rule (`capturerLives`, `AIAssault.TurnsToTake`): a turn-by-turn simulation of
walking there, starving out of supply, capture points and nightly expected loss; the campaign only takes on a Secure
some free unit would live to complete.

## 4. Flags added in this session's work (all default on unless noted; all new relative to the old AI)

Transports: `assaultRides`, `assaultBoardTurns` 2, `assaultTransportWeight` 2, `assaultFillBonus` 0.5,
`assaultMedevacStrength` 3, `assaultWaveRatio` 1, `assaultArmedRides` (off: old same-turn hop), `assaultCarryOut`,
`assaultBoardWeighs`, `assaultOrderWithLine`, `assaultFrontlineOut`, `assaultFireThenUnload`.
Supply: `supplyStaysAboard`, `unifiedLinkPlacement`, `boardSpentTransports`, `keepBuildSlotsClear` (now mostly
redundant: unified placement no longer parks supply on the HQ).
Campaign: `capturerLives`, `campaignGatherWithBody`, `campaignSurgeAbort` (+ `campaignEngagedTurns` 2, `campaignSurgeRatio` 1.5,
`campaignSurgeWithdrawTurns` 2), `campaignBreakShort` (+ `campaignBreakShortRatio` 0.5).
Turn: `artilleryFireOrDefer`. Fog: `fogStaleRadius` 5, `fogConcentrate` (+ `fogAnchorFalloff` 2, `fogHQWeight` 4, `fogProductionWeight` 2). Game (not AI): `GameController.fogShownOnScreen` (off: the screen shows everything, sides
still play by their fog).
Turn: `waitTwice`. Production: `productionOverkill`, `productionLifetime` (+ `productionLifeMax` 8, `productionLifeRef`
3, `productionReachRef` 6), `productionShieldCut` 0.6, `productionShieldRatio` 2, `productionDefenceFade` 20.
Speed (meant to give identical decisions, faster): `parallelForecast`, `speculate`, `keepCaches`, `vacatedReach`, ...
No existing default was changed. Tested but not adopted: massing (`objectiveForceRatio` 1.6 + gather share 1.0),
sticky / flexible replanning (`objectiveReplanLoss` 0.25 / 0.75): mixed results.

## 5. Results and findings (latest first)

- **Old v new, current code** (traced, 25 rounds):
  ForestClearing (fog on, before the last fixes): new wins from both sides (new Blue 173k v 6k; new Red 138k v 45k),
  but thinks longer (33 s v 18.5 s total in one game).
  ChanceMeeting (fog on), after `waitTwice` + `boardSpentTransports`: new Blue 90k v old Red 185k; old Blue 168k v
  new Red 98k → new loses from both sides. Before those two fixes: new Blue 30k; new Red ~131k v 129k.
  RiverCrossing (fog off): Red wins whichever AI plays it. Biomes (fog off): new better as Blue, worse as Red.
  (These three were run before `a45ec72` and without tracing; re-run them traced.)
- **ChanceMeeting, new Red v old Blue, rounds 12–24** (log: section 7): new Red's objectives switch every 1–2 turns and
  are all Hold/Clear (road junctions in its own rear, chasing Blue's raiders) — no Secure from turn 20 to 32; its main
  front (19–23 units) is Aggressive at local ratio 0.75–0.85 but attacks only 2–5 times a turn (old Blue 3–7); all 9 of
  its transports sit as supply links. **Investigated (campaign reasons now in the duel trace as `campaign: not taken on ...`):
  `capturerLives` is not the cause** — it rejected nothing for Red all game, and with it off Red did worse (65,833 v
  97,833). Red's Secure objectives were dropped 17 times for "needs N supply units, N free" (Holds 50 times): its supply
  chain held 9–11 of its 11–12 supply units as links, clustered within 1–2 tiles of each other ((3,0), (1,2), (5,1), (5,2),
  (3,2), (4,3), (3,3) all "hold"), leaving 0–2 spare. The old AI as Red (other game) held 8–9 links, spread further
  apart, with 4 spare, and pursued Secure FOB (0,-10) with them. Suspects (today's supply changes): `unifiedLinkPlacement`
  (a link aboard a transport now moves only if it brings supply nearer, so it never leapfrogs: aboard links pile up and
  hold) and `boardSpentTransports` (cut-off supply units board and become links). Not yet tested.
- **ChanceMeeting, new Blue (before fixes):** supply never linked until turn 7 because held supply transports acted
  before the supply units and their "wait" spent them, and the chain only boarded transports that had not acted
  (both fixed). Five single-unit task forces went in under fog (tiny unseen estimates) and stayed in when needs jumped
  to 38k–67k (shared campaign behaviour, old AI too).
- Old AI: transports did only supply leaps; boarded no fighting units in either ChanceMeeting game.
- Earlier this session: a supply truck parked on the HQ for 20 turns blocked every purchase (fixed: unified link
  placement); supply trucks were named "Truck Variant" in saved maps, so name checks missed them (all name checks
  removed); a capturer ranking change sent a lone forward unit and the gather check counted it (fixed: gather with the
  body; ranking back to "nearest to the staging ground").

## 6. Old AI against new AI (the test approach)

See `Tests/README.md`, "Old AI against new AI". In short: `Assets/OldAI` is the AI of `claude/campaign-2` (`49a2dbb`),
isolated in its own namespace; changes go into the new AI only; when a change beats the old AI on the test maps from
both sides and is not slower, the new AI is copied over the old one and becomes the baseline. Known shared state left in
place: `SupplyNetwork`'s thread-static `RecordLinked`, `AssumeLinked`, `AssumeLinkedVia`, `PlannedAboard` (both AIs set and
restore them). The old copy reads mission objectives through `OldAI/OldMission.cs` and no longer sets the removed
`PutDownHypothetical`. The old branch is checked out at `/home/claude/old` (a git worktree; may not survive).

- **Chain spacing (`chainLinkSpacing` 2, added after the above):** a link within 2 tiles of a kept link or a supply
  building is released as spare. ChanceMeeting, traced: new Red v old Blue now **157,500 v 141,916** (was 97,833 v
  168,416; no building dropped for want of a supply unit); new Blue v old Red **41,166 v 201,333** (was 90,083 v
  184,916). Blue's supply is not worse (46 unit-turns out of supply by round 12, was 59); Blue loses more material by
  round 12 (89k, was 125k). Unverified reading: freed supply units let Blue take on more buildings at once and it
  spreads thin against a Red that starts beside most of them. Logs: scratchpad `games/cm-rev-sp.log`, `games/cm-fwd-sp.log`.
- **Why new Blue got worse with spacing (ChanceMeeting, OLD=Red, fog on; traced + `BOARD=1`, 2 Nov):** the reading
  above is wrong. Games are identical to round 3; the campaigns split at turn 7, when spacing released 2 links (spare 3
  v 1). Blue did not pursue more objectives at once (8,5,5,4,4,3 v 7,4,5,3,4,3) nor more buildings (fewer Secures).
  The spare supply units let it take on Clear@-6,8 and Hold@-7,8 (without spacing: "needs 1 supply unit, 0 free"), and
  from then its body went south-west (Clear@0,4 n10, Clear@-5,6 n10, Hold@-7,8 n12, Clear@-6,5 n13; Hold@-6,9 kept
  turns 5–15 at have 5–14k v need 35–48k), while without spacing it took the FOB axis (Secure@0,-10, Secure@0,8,
  Clear@0,-4, Hold@1,-10). "Not taken on" reasons: supply only bound through turn 7; after that both games give force.
  Losses: fighting units fall alike to turn 18 (25→15 v 24→15). The gap (rounds 9–14, ~55k) is supply: turns 16–28
  Blue lost 5 Supplies, 2 trucks, 2 APCs, all chain links holding at (-7,4), (-5,4), (-6,4), (-7,6), (-5,0), (-12,8),
  (-4,-5), after its south-west task forces broke (Clear@-6,5 lost 4 units on turn 18); without spacing, 1 Supplies.
  Links were not placed worse (without spacing Blue also had links at (-6,5), (-6,8), (-4,8), which lived while its
  army held there); they were left holding after the army in front of them was beaten. Logs: scratchpad
  `games/cm-fwd-cur-b.log`, `games/cm-fwd-sp0-b.log`; tools `camp.py` (campaign per turn), `losses.py` (hits per turn).

- **Plan unit status and the surge stop (`campaignSurgeAbort`, 2 Nov):** each task force sorts its units into staged /
  en route / engaged (within `campaignEngagedTurns` 2 of an enemy unit), shown in its trace line with the estimate it
  was formed against. A task force with most units not engaged whose estimate rises to 1.5× that stops; its en-route
  units pull back to its staging ground (Defensive front, up to 2 turns) and the objective is planned afresh.
  Old v new, traced, 25 rounds (logs `games/surge-*` v `games/base-*`): identical games on RiverCrossing (both
  sides), Biomes (both sides) and ChanceMeeting new Blue (it never fired). ChanceMeeting new Red: fired on turn 6
  (Secure@0,-10, one Grunt, estimate 3,169 → 18,155), re-formed with 4 units that gathered before going in; new Red
  **220,166 v 63,666** (was 157,500 v 141,916). Why it rarely fires: on ChanceMeeting every plan's units are within 2
  turns of an enemy from turn 5. New Blue's Hold@-6,9 had all 3 units engaged when its estimate jumped (2.2k → 13k),
  then was kept 8 turns at 5–14k v need 33–48k: the broken check compares power with the power it formed with, not
  with its need. Medevac (RiverCrossing): 3 units carried to cities, all at strength 3; each stayed one heal, then left
  at 4–5.
- **Broken when short of need (`campaignBreakShort`, 2 Nov; mixed):** a kept task force whose power is below 0.5 of its
  need now is broken like one that lost half its power (formed again only if what can join is enough). Old v new,
  traced (logs `games/short-*` v `games/surge-*`): RiverCrossing new Blue **248,083 v 23,000** (was 173,750 v
  168,416; 4 breaks); new Red unchanged (no break). ChanceMeeting new Blue **67,250 v 159,333** (was 41,166 v 201,333;
  breaks on turn 7 of four one-unit Secures, turn 9 of Secure@0,-10 and Hold@-6,9); new Red **116,833 v 183,916** (was
  220,166 v 63,666). Biomes new Blue 143,250 v 55,000, 12 buildings (was 62,833, 9); new Red 25,166 v 127,916 (was
  25,416 v 123,833). New Red on ChanceMeeting, read from the log: breaks on turn 6 (five one-unit city plans, 4x
  short), 8 and 10 (under half their need); the game diverged on turn 6. Red took the FOB at (0,8) by turn 16, then
  pushed south (Hold@1,11 and its ordinary fronts); artillery and tanks moved up to (-5,9), (-4,7), (-5,8) and were
  destroyed on Blue's turns 17–19 (Red 209k → 161k, rounds 8–12). Most of those losses were units on no task force. Not
  traced to the break itself; looks like the push after a success.
- **Fog fear concentrated, and artillery that won't fire goes last (2 Nov):** (1) `fogConcentrate`: the enemy we cannot
  place (AIKnowledge: estimate − known, in units of an average enemy unit) is shared among anchors — each enemy unit
  seen or remembered 1, the enemy HQ 4, its production sites 2 (purchases on) — and spread over unknown ground,
  falling off by e every 2 tiles; a stop fears the expected unplaced units within their reach times their average
  damage. Turn 0: all of it at the HQ. (2) `fogStaleRadius` 5: ground out of sight within 5 of a known enemy unit counts
  as unknown however lately seen. (Probe, ChanceMeeting turn 16: Blue's unseen gun stood on (-7,7), seen 4 turns before
  and so counted empty with `fogUnknownTurns` 6; Red's artillery stops had 7% dark ground in reach, expected loss
  0.5 strength.) (3) `artilleryFireOrDefer`: artillery placed first to fire that would do anything else decides again
  after the rest. Old v new, traced (logs `games/fog-*` v `games/short-*`): ChanceMeeting new Blue **176,416 v
  145,666** (was 67,250 v 159,333), new Red **174,749 v 153,916** (was 116,833 v 183,916). Fog off (only the artillery
  rule applies): RiverCrossing new Blue 169,750 v 126,666 (was 248,083 v 23,000; 7 deferrals, game diverges at the
  first on turn 6, no mistake found in the deferred move), new Red 263,583 v 0 (was 280,333 v 0); Biomes new Blue
  146,500 v 68,250, 8 buildings (was 143,250 v 55,000, 12), new Red identical. Think time unchanged.

- **Transports: one per supply unit, spare seat early, no hop off (2 Nov; mixed):** `supplyTransportCap` (held for supply:
  carrying one, on a duty, or the nearest empty one for each supply unit on foot, no more; was every capable one on a
  no-build map: RiverCrossing held 12 for 10 supply units, 7 idle all turn while infantry walked), `supplySpareSeat` (a
  rider takes a supply transport's spare seat before its supply unit is aboard, if the supply unit still fits: supply
  units move last, so riders never got the seat), `noHopOff` (a transport that has not moved does not put down a rider
  that got in this turn: a Grunt walked into a Recon and was put down beside it, a tile more than it could walk).
  RiverCrossing turn 0 under fog: 15 of 16 infantry board (was 4). Old v new, traced, 25 rounds (logs `games/seat-*`
  v `games/fog-*`): ChanceMeeting new Blue 173,500 v 186,833 (was 176,416 v 145,666), new Red 181,166 v 142,416 (was
  174,749 v 153,916); RiverCrossing new Blue 168,250 v 125,583 (same), new Red **143,750 v 162,833** (was 263,583 v 0:
  before, Red's infantry walked as a mass and broke Blue in rounds 4–8; now 17 rode instead of 7 and the attack never
  landed; not traced further); Biomes new Blue 150,416 v 43,333, 11 buildings (was 146,500 v 68,250, 8), new Red
  26,500 v 110,166 (was 25,166 v 127,916); ForestClearing (fog on, builds) new Blue 158,583 v 86,083, 15 buildings,
  new Red 46,833 v 160,833 (loses). Think time down (RiverCrossing new Blue 157 s, was 264 s).
- **Item 2 done, transport cap tightened, early seat needs a forward ride (2 Nov; mixed):** `cutOffSeeksSupply`: a cut-off
  supply unit on foot first boards an empty transport beside it, else walks to the reachable tile nearest supplied
  ground (moves seen: ChanceMeeting 5 and 2, none elsewhere). `supplyTransportCap` now never holds more transports than we
  have supply units (carrying first, then duties, then nearest). `supplySpareSeat` now takes an early seat only where the
  supply ride planned this turn takes the rider a move nearer: RiverCrossing new Red had 6 riders on turn 1 board supply
  trucks that held their links and let them out again, a turn lost. Old v new, traced (logs `games/item2-*` v
  `games/seat-*`): RiverCrossing new Red **210,666 v 94,166** (was 143,750 v 162,833), new Blue 169,666 v 112,250 (was
  168,250 v 125,583); ChanceMeeting new Blue 155,000 v 184,000 (was 173,500 v 186,833), new Red 159,833 v 147,666 (was
  181,166 v 142,416); Biomes new Blue 153,250 v 62,583 (was 150,416 v 43,333), new Red 25,166 v 127,916 (was 26,500 v
  110,166); ForestClearing new Blue 144,166 v 114,416 (was 158,583 v 86,083), new Red **101,083 v 125,083** (was 46,833 v
  160,833). Riders still let out having gone no nearer: up to 10 a game (ChanceMeeting new Red).
- **Campaign per the owner's rules (2 Nov; mostly better, two regressions):** `productionValueByIncome` (a building that
  makes units is worth its income plus our income per production building we have, ×10), `campaignHubNeedSimple` (one
  supply unit only if the target is more than two moves beyond supplied ground; was relays + spread: 3 for the ArmyBase
  at (13,-13)), `campaignHoldContested` (a junction is a Hold only if unknown or an enemy is within two moves),
  `campaignReserveShare` 0.33 (new task forces only while committed power stays within the other two thirds),
  `campaignMinTaskForce` 4 (at most one task force per 4 fighting units after the reserve; scouts not counted),
  `campaignStageOnSupply` (gather on the supplied tile nearest the target). Old v new, traced (logs `games/camp-*` v
  `games/item2-*`): ForestClearing new Blue **167,166 v 38,500** (was 144,166 v 114,416; ArmyBase (-13,0) producing from
  turn 7, was 13; 1–2 task forces, was 5–8), new Red 65,333 v 145,583 (was 101,083 v 125,083); ChanceMeeting new Blue
  **170,916 v 110,500** (was 155,000 v 184,000), new Red **225,166 v 65,750** (was 159,833 v 147,666); RiverCrossing new
  Blue **122,500 v 209,333** (was 169,666 v 112,250: now loses), new Red 227,250 v 147,166 (was 210,666 v 94,166);
  Biomes new Blue 162,750 v 62,500 (was 153,250 v 62,583), new Red 44,833 v 137,000 (was 25,166 v 127,916). Think time
  down. Found in ForestClearing new Blue, not yet fixed: ArmyBase (13,-13) never produced (one Grunt sent on turn 9;
  its task force's supply unit rode to (-11,-2), the far side: a logistics routing bug); 9 supply units bought, all for
  "its supply is short" (a production rule this change doesn't reach); the reserve held 0–3 units because only mobile
  units join it: the third held back goes to the ordinary fronts (12 units at home on turn 9).
- **Corrections (2 Nov, not yet judged against the old AI):** supply need is now none within one transport move of
  supplied ground and one per transport move beyond (`campaignHubNeedSimple`; gap = land movement steps to the nearest
  supplied tile, over the median move of our ground transports); the spread step that handed spare supply units to
  task forces by distance-to-supply, ignoring where the supply units were, is off with it (it sent the supply unit
  for the (13,-13) ArmyBase task force from (-11,-2)); Holds contested within `fogStaleRadius`; staging on our supply
  unit on supplied ground nearest the target; `campaignMinTaskForce` 0 (off). `cutOffSeeksSupply` steps aside when a
  transport that could carry the supply unit is within a move (on ChanceMeeting it walked every supply unit back
  toward the HQ, and none boarded the waiting trucks; now 6 boardings in 4 rounds). The chokepoint check runs with
  `campaignHoldContested` off (it checks detection; its enemy is 6 tiles off). Open: "its supply is short" buys one
  supply unit a turn while any front unit is out of supply, not counting supply already riding there, with no cap.
- **Reserve drafting / reserve demand / spare-seat guard (2 Nov, NOT working yet, flags on):** `reserveDraftsRiders`
  (an idle transport not held for supply plus idle infantry to fill it join the reserve), `reserveDemand` (when no task
  force has engaged units and no front is short of supply, production values reserve-capable kinds at the producer
  nearest the rally as a front short by the reserve's shortfall of `campaignReserveShare`). ForestClearing new Blue, 10
  rounds: no drafts (every transport was held for supply), nothing bought for the reserve (demand applied on only 3 of 9
  turns and a reserve-capable unit never won), reserve 0–2 units. Spare-seat guard (a transport a rider boarded this
  turn is not used to hold a link) is in the chain's leap path only; the case seen boards through "chain link boards its
  truck" (claims), so riders let out having gone nowhere did not fall (3 → 4 in 3 rounds of ChanceMeeting).
  `reserveTakesSpare` and `productionAbsolute` are off by default.
- **Follow-up (3 Nov):** reserve demand now goes to the nearest producer that can build a reserve-capable kind (the HQ,
  nearest on ForestClearing, builds only infantry and supply); the trace prints a "reserve demand" line with each such
  kind's worth there. ForestClearing: Airport, Gunship worth 7,215 (802 per 1000 gold), but 500 gold left: the other
  slots spend the gold first (the first pass takes the best value per gold across all slots). The reserve needs to save
  up. Drafting: ForestClearing Blue had 4 supply units and one unarmed transport (its 2 Recons carry one rider and scout),
  so nothing idle to draft. Spare seat: a rider in a supply transport now keeps the ride planned at the start of the turn
  ("rides its truck ... as planned"; 2 in 3 rounds of ChanceMeeting), and the chain-link boarding path no longer holds a
  transport a rider just boarded; riders still let out having gone nowhere: 4 in 3 rounds (another path, not found yet).
- **Holds achieved by vision (3 Nov):** `campaignHoldContested` now treats a junction in our vision (the whole board with fog
  off) as held, like a building taken: its task force is released ("achieved or gone") and it is not a Hold again until
  it has gone dark for `fogUnknownTurns` (6) or an enemy we see or remember comes within `fogStaleRadius` (5) of it while
  out of our sight. Not yet judged against the old AI. Last full comparison (57cad55, ForestClearing only finished): new
  Blue 149,750 v 34,500, new Red 129,000 v 119,083. Open: idle task forces next to a front under attack should join it
  (ChanceMeeting turn 34, Hold(5,5)); a task force holding more supply units than it needs should release them to
  fronts out of supply (Hold(5,5) held 4, need 0, while Clear@-4,-5 was short); rerun the eight games.
- **Skittishness fixes (8 Nov; flags on, results mixed):** found: `fogConcentrate` (2 Nov) priced every expected
  unplaced unit's full fire, uncapped: near the front a stop read as 3–4 strength lost (a tank: -4,196 against forecast
  gaps of ~200), and the world-score tie then let position pick tiles in the rear. `fogConcentrateCapped`: at most one
  unplaced unit's fire × chance one is in reach (1 − e^−expected) × the unplaced share. `fogScreenBelowCost` 3000: cheaper
  units ignore unknown ground. `noZeroDamageAttacks`: a 0-damage attack becomes a move / hold. `tieFavorsShot`: in the
  tie, a damaging shot wins (full strength, from supplied ground). `productionPossibleFoes`: no unit that hurts nothing
  the enemy started with, builds, or has shown (AA without air). `productionAvoidFire`: no unit built where the known
  enemy destroys it next turn. `defensiveNarrows`: on the defensive (aggression < 0.25) passages ≤ 3 wide near a known
  enemy are Holds (`AICampaign.Narrows`). 12-round ChanceMeeting, new Blue v old Red at round 11: all on 79,750 v
  181,583; all off 142,583 v 183,916; `fogScreenBelowCost=0,defensiveNarrows=false` 142,333 v 150,750; cap off 156,583 v
  159,333. New Red: all on 172,833 v 145,333; all off 186,416 v 150,166. ForestClearing 14 rounds (new Blue): all on
  77,666 v 57,416, all off 76,916 v 48,750. Reading: as Blue on ChanceMeeting, screens + narrows together spread the army
  forward without coordinated attacks ("group battle" absent) and it was picked apart in rounds 2–4. Not resolved.
- **Not done yet:** item 4's reserve and scouting parts, item 5
  (profile thinking time), item 6 (refresh OldAI); the owner's ForestClearing points (supply links bunched 1–2 tiles
  apart: overbuilt; natural ArmyBases taken late, turn 27 for (13,-13); whole army moved across the map between FOBs;
  reserve not committed); RiverCrossing new Red regression above; new Red loses ForestClearing.

## 7. Open work, in the order I'd take it

1. **The ChanceMeeting regression** (new Red v old Blue: `OLD=Blue`, fog on, traced): run with
   `SET_RED="unifiedLinkPlacement=false"` and with `SET_RED="boardSpentTransports=false"`; read the chain lines
   (`chain toward ...: ... holds; spare N`) and `campaign: not taken on ... supply units` counts. The fix is likely a
   spacing rule for links (the owner's: as far apart as possible while one transport move from the nearest node) and
   releasing links that add no supplied ground, so supply units are free for objectives.
2. **Cut-off supply units walk toward supplied ground or a transport** before anything else (shared gap).
3. Cap held supply transports at one per supply unit; release the rest to troops.
4. Campaign (each change in the new AI, tested against the old):
   - Treat the unaccounted enemy as possibly concentrated (no-build maps: the total is known).
   - Re-plan a front when its need jumps past what it has, before feeding it the reserve.
   - Keep the reserve out of fronts that should be called off; consider front-level fallback.
   - More scouting before committing.
5. Thinking time: the new AI is slower than the old in most games measured; profile with `PROBE=perf SECTIONS=1`.
6. When a change wins: refresh `Assets/OldAI` from the new AI (README steps).

Logs from this session (scratchpad; may be gone):
`/tmp/claude-0/-home-claude/797c7809-295c-500b-909a-d77a00b7476c/scratchpad/games/cm-rev.log` (old Blue v new Red,
ChanceMeeting, traced), `.../games/cm-trace2.log` (new Blue v old Red, traced).
