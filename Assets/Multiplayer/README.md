# Networked games (play by post)

Two or more players on their own devices (PC or phone) play a game a turn at a time, whenever each of them likes.
There is no game server to run. Each device keeps the game in step by itself; a hosted store holds the games.

## How it works

- **A game is a log of orders**, in the game log's own lines (`Blue Grunt moved (0,4) -> (2,1)`, `... attacks ...`,
  `... buys ...`, boarding, getting off, strikes, resigning), plus a **board it is played on from** (the start, as
  the host loaded it).
- **What a player does is sent as they do it.** The game log is tapped as it is written (`GameLog.Written`). Order
  lines go to the store a batch at a time, then a "turn ended" entry when the turn ends.
- **Other devices play those orders through the game's rules** (`Replay.BeginOrders`, `NextOrder`). Each order is
  given as a click would give it, never by copying the outcome. What comes of it is then checked against its line,
  and the unit finishes as in play (captures and supply follow). At each turn's start the whole board is checked
  against the line its author wrote (`GameLog.BoardCheck`: every unit's place, strength and stores, every building,
  the money).
- **A difference is a desync.** It is reported and never papered over. That device plays nothing more, nobody can
  act, and the host decides (see *The host*).
- **Opening a game starts from the latest turn this device has.** The game log already saves the board at every
  turn's start (`turn-NNN-Side.dat` in its folder). Per game, a small index (`<persistentDataPath>/multiplayer/index-*`)
  records which of those files is the latest, the log entry it stands at, its board check line, and the log's place
  after it. Opening loads that board (the local game log goes on from that turn) and plays only the log after it:
  at most the turn in progress and anything since. Moves the player has seen are applied at once; newer ones play on
  screen, sped up (`MultiplayerSettings.catchUpMoveSpeed`).
- The index is used only if the log still agrees with it (the entry it stands after has the same check line) and
  the board loads to the same check. Otherwise, and on a new device, a reinstall, or after the host went on from
  their own board, the game is rebuilt from the board it is played on from, with the whole log. Finishing with a
  game deletes its index; its game logs stay, like any game's.
- A game that is open is polled every couple of seconds, so the opponent's moves show as they make them.
- **No device is a server.** Whoever's turn it is writes. The store refuses a write that is out of order (every
  value has a write lock), and a device that finds the log has moved on under it reloads the game as it stands.

All of this is `MpSession` (on the board) and `Mp` (lobbies, the log, the player's list), behind one interface for
the backend (`IMpStore`). Nothing in them depends on a particular backend.

## Who plays what

- A side is held by a **player** (theirs to play on their device; the host's own sides are "Local"), by the **AI**,
  or is **open** (in the lobby: anyone joined may take it; in a game under way: its player left, and the host
  decides).
- **An AI side is played by the next player to move after it**, on their device. That is the player who would
  otherwise wait on it. It is worked out down the turn order, skipping sides that are out. The AI's orders go to the
  log like anyone's, so nobody else runs the AI.
- Only the device whose turn it is may act. Everyone else's board takes no orders for that side, and their End Turn,
  building menu and Next Unit are off (`GameController.PlayedElsewhere`). Each player sees the board through their
  own side; a spectator sees everything.

## The turn clock

- The lobby offers a turn limit (`MultiplayerSettings.turnLimits`): none, 1 minute, 10 minutes, 4 hours or 1 day.
- **A player's clock starts when "your turn" is on their screen.** Their device writes the time (UTC) into the game,
  so a turn is not lost to the time it took them to open the app.
- The analog clock (minute and second hands) shows beside the banner on your own turn once 5 minutes or less are
  left (`clockShowsWithinSeconds`; always, in a 1-minute game).
- When the player to move is out of time, **any other player may skip the rest of their turn** (the button under
  the turn banner). What they did stands, and the next side moves.
- Clocks are on the honor system: a device's clock is trusted.

## The host

- The host makes the lobby: its name (the scenario's at first, changeable until the game starts, then locked), the
  sides, the rules, the turn limit. The host also starts the game.
- **Removing a player** (lobby or game): they cannot come back. In a game under way their sides wait for the host's
  Players panel: **Give** to another player in the game (someone who joined later, or one already playing), the
  **AI**, or **Eliminate** (the side resigns and its units go).
- **Leaving a game under way** is the in-game menu's **Resign** (your side is out; the game goes on for the others)
  or **Exit to main menu** (you stay in the game and can open it again from your list). A player who leaves a lobby
  (Back) frees their side. The host leaving the lobby ends it for everyone.
- **A desync**: guests are told something went wrong. The host sees what differed ("this device has unit
  RGr(0,0)s4; the log has RGr(0,0)s6") and why it can happen (different versions, a tampered game), with two
  choices:
  - **Continue from my board**: the host's board becomes the board the game is played on from, every device loads
    it, and play goes on from there.
  - **End the game**: it is over for everyone.
- The version check (`Application.version`) stops a device on another version from joining. A desync can still
  happen, which is why the host has the last word.

## Join codes and the player's list

- A game's code is 6 characters from an alphabet with no easily confused letters (`codeLength`, `codeAlphabet`).
  It is shown with the game's name: **Tidal Ridge#K7Q2MX**. Typed in any case, with or without the name.
- A code is free again once its game is over. A new game under it gets a new *epoch*, and its data is kept apart
  (`Mp.DataId`), so nothing of the old game leaks into it.
- Each player's list (top right of the main menu) shows every game they are in. Its button reads **Games .** and
  turns to **Games !** when one waits on them; those games are highlighted. A finished game stays in the list until
  the player closes its post-game screen.
- **The player's name** is for show only and can be changed at any time. The player id is what counts.
- **Linking another device** (Games ▸ Link another device): Get code on the new device, Confirm it on the device
  you already play on, then Use it on the new device. The new device becomes you, with all your games. For a phone
  and a PC, or after a reinstall.

## On screen

- **Whose turn** is the turn banner's: the line at the top names the side and, for a side played on another device,
  its player ("Red — Bob's turn, day 3"); the big colored banner shows as your own turn begins. The overlay adds no
  turn line of its own.
- Under the banner, a small strip shows only when there is something to say: catching up on what happened, watching,
  a side waiting for the host, a problem reaching the store, and **Skip their turn** when it applies.
- The turn clock sits beside the banner.
- In the lobby, the **join code** has a panel of its own on the right (just the code, e.g. K7Q2MX, and Copy). The
  code is all anyone types; the game's name shown elsewhere with it (Tidal Ridge#K7Q2MX) is only a label.
- The main menu's games list shows on the main panel only (not over Host Game, Join Game or Load Scenario).

## No connection

- **Reading** (polling, opening a game): a failed read is shown in the strip under the banner (or the games list)
  and tried again at the next poll. Nothing is lost.
- **Writing your turn**: the moves you made are kept in memory and sent again, waiting longer each time (doubling, up
  to a minute). The strip says your moves are kept. Once the store takes them, play goes on.
- **If the app is closed** before an unsent turn reaches the store, that turn is lost; the game stays at the last
  turn the store has, and you play it again. (Future work: keep unsent entries on disk and send them at the next
  launch.)
- Sign-in is retried the same way.
- **Unity Gaming Services by region**: since 30 June 2026, organizations based in mainland China, Hong Kong and Macau
  can no longer use Cloud Code or Cloud Save (Authentication still works). Players there may also be unable to reach
  the services. If they matter, keep another backend behind `IMpStore` in mind.

## Notifications

- While the app runs, on any screen, `MpWatcher` looks at the player's games every `listPollSeconds`. The first
  time a game waits on them, it puts up the **TurnNotice** scene ("Your turn in Tidal Ridge#K7Q2MX", Open, Dismiss),
  unless that game is the one on screen.
- **Not done yet: notifications to a closed app.** Ways to add them later:
  - **Push**: Cloud Code calls a push provider (Firebase Cloud Messaging, or a service such as OneSignal) when a turn
    is written. This needs the device token stored per player and the provider's key in Cloud Code.
  - **Background polling**: a small native plugin (Android WorkManager, at most every 15 minutes; iOS Background App
    Refresh, when iOS allows) asks the store whether a game waits, then raises a local notification. No push
    provider is needed, but notices arrive late, and on iOS unreliably.
  - **Steam**: Steam's Game Notifications can say "your turn" in the Steam client for a game on Steam. Its Web API
    needs the publisher key, so the call would come from Cloud Code.

## Backends

Pick one in **Assets/Resources/MultiplayerSettings.asset** (made by HexTactica ▸ Front End ▸ Create Missing Scenes).

### Shared folder (the default: nothing to set up)

Games are kept as files in a folder (`MpFolderStore`): `<persistentDataPath>/multiplayer-shared`, or the folder in
the settings. Two copies of the game on one computer (the editor and a build) share it, so you can try a whole game
alone. A folder synced between computers (Dropbox, a network share) also works for a casual game, but two devices
writing in the same instant over a slow sync can both win. Device linking works here too.

### Unity Gaming Services

What it uses:
- **Authentication**: anonymous sign-in, no account needed by players; Code-Link to link devices.
- **Cloud Save**: one Game Data custom item per game, read by the players; each player's list in their own Player
  Data.
- **Cloud Code**: one small module (`CloudCode/HexTacticaMp`) whose `Write` function stores a value. Cloud Save does
  not let players write game data, and the write lock check also lives here. It holds no game rules.

**What you do (once):**
1. **Link the project**: in the Unity Dashboard, create a project. In the editor, **Edit ▸ Project Settings ▸
   Services**, link to it.
2. **Install the packages** (Package Manager, Unity Registry): Authentication, Cloud Save, Cloud Code, and
   Deployment (to deploy the module from the editor).
3. **Turn on the code**: **Edit ▸ Project Settings ▸ Player ▸ Scripting Define Symbols**, add `HEXTACTICA_UGS` (for
   every platform you build: Standalone, Android, iOS).
4. **Deploy the module**: install the .NET 8 SDK. In `CloudCode/HexTacticaMp` run `dotnet build` once. It fetches
   the packages and shows whether this code matches their current version. The module was written against Unity's
   documentation but **could not be compiled here**; fix any namespace the compiler names (Cloud Save's models,
   `ApiException`), then pin the package versions in the `.csproj`. Then deploy it: **Window ▸ Unity Gaming
   Services ▸ Deployment** lists `Assets/Multiplayer/HexTacticaMp.ccmr`; select it and Deploy. If it is not listed,
   make one with **Assets ▸ Create ▸ Services ▸ Cloud Code C# Module Reference** pointing at
   `CloudCode/HexTacticaMp/HexTacticaMp.sln`.
5. **Device linking** (optional): **Project Settings ▸ Services ▸ Authentication ▸ ID Providers**, add **Code-Link**.
6. **Switch the backend**: in MultiplayerSettings, Backend = Unity Gaming Services.

**Costs.** UGS's free tier includes 1,000,000 Cloud Save reads and 1,000,000 writes a month, and 1,000,000 Cloud
Code calls a month (check the current pricing page). A turn costs a handful of writes. Reads add up while games are
open: about 30 a minute per player in an open game at `livePollSeconds` = 2, and a few a minute for the list. Raise
those settings to read less.

**Limits.** A custom item holds up to 2000 keys and 5 MiB. A game uses about 3 keys, plus one per
`logChunk` (20) log entries. A long game (a few hundred turns) stays well inside both.

### Another backend

Implement `IMpStore`: keyed text per game, with a write lock that refuses a stale write, and a per-player record.
Optionally implement `IMpLinking` too. Then return it from `Mp.MakeStore`. Firebase (Firestore with a transaction
for the lock) or a small Cloudflare Worker would each be about 150 lines.

## Out of scope for now (honor system)

- **Fog of war** hides nothing from a determined player: every device has the whole board.
- **Spectators** see everything.
- **Clocks** and the store's checks run on the devices; a modified client could break the rules. A desync shows
  that something differs, but not who is to blame.

## Files

| File | What |
|---|---|
| `MpModel.cs` | A game as stored: lobby, sides, members, where the game stands; log entries |
| `IMpStore.cs` | What a backend provides (and `IMpLinking`, optional) |
| `MpFolderStore.cs`, `MpUgsStore.cs` | The shared-folder and UGS backends |
| `Mp.cs` | Hosting, joining, the lobby, the log, the player's list, linking |
| `MpSession.cs` | The game on the board: playing others' orders, sending this device's, catching up, the clock, the host's decisions, desyncs |
| `MultiplayerSettings.cs` | The settings asset |
| `UI/` | `MpWatcher` (always on), `MpNotice`, `MpGamesMenu` + `MpGameRow`, `MpJoinPanel`, `MpLobby` + `MpMemberRow`, `MpOverlay` + `MpClock` + `MpSideRow` |
| `../Editor/MultiplayerScenes.cs` | Makes the scenes and prefabs, and updates existing scenes (HexTactica ▸ Front End ▸ Update Scenes) |
| `../../CloudCode/HexTacticaMp/` | The Cloud Code module |

Engine hooks outside this folder: `GameController.PlayedElsewhere`, `FogOfWar.IsHuman`, the board's click gates in
`HexTile`, `GameLog.Written` and `GameLog.BoardCheck`, and `Replay`'s live orders.

## Tests

`Tests/MultiplayerChecks.cs`: six headless checks (`NAME=mp- bash Tests/run.sh Assets`). Several devices share one
folder store; the board is switched between them by opening the game as each player.
- `mp-lobby`: codes, joining, claiming, removing, versions, spectators, code reuse, leaving, linking.
- `mp-turns`: three rounds on two devices, by catching up and by watching live, with the same board throughout;
  each reopening starts from the game log's latest turn board and plays only the other player's turn; no board is
  saved twice; an index the log disagrees with, or naming the wrong board, is passed over.
- `mp-ai`: who runs an AI side, and a spectator.
- `mp-clock`: when clocks start, skipping a player out of time.
- `mp-desync`: a tampered board found, reported, and put right by the host; the host ending the game.
- `mp-kick`: removing a player mid-game, handing their side to the AI and to a new player, eliminating it.

Not covered headless: the screens (type-checked only), anything in the Unity editor, and the UGS backend.

## Known traps

- **Turn starts only.** The index names a turn's start, never a point mid-turn: a board saved mid-turn would load
  with every unit ready to act again. For this device's own turn end, the index is written only once the store has
  that entry, so it never runs ahead of the log.
- **Finishing units.** A replay never finishes units, but live orders must: finishing is when captures start and
  supply is recalculated (`Replay.FinishLive`).
- **The game's end** goes to the log only once what ended it is over. A side resigning ends the game before its
  units are gone (`MpSession.OnGameEnded`, written at the next Tick).
- **A game ended by the host** is not news for the log. `MpSession.Ended` sets its phase before ending the game
  here, or the end would be written as an entry.
- **GameLog after a reload** must start afresh. It writes a unit's move only if it knew where the unit stood
  (`MpSession.Record`).
