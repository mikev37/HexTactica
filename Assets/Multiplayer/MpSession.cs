using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using HexTactica.Harness;
using UnityEngine;

namespace HexTactica.Multiplayer {

    /// <summary>
    /// A networked game on this device's board: the game scene plays it as it plays any game, and this keeps the board
    /// and the store's log in step.
    ///
    /// - What is done here (a player's orders at this screen, an AI this device runs, a turn ended) is taken from the
    ///   game log as it is written (GameLog.Written: the order lines, GameLog.Lines) and added to the store's log, an
    ///   entry at a time, in order.
    /// - What others did arrives as entries and is ordered on this board through the game's own rules, exactly as a
    ///   replay orders it (Replay.BeginOrders, NextOrder); what comes of each order is checked against its line, and at
    ///   every turn's start the whole board against the check line its author wrote (GameLog.BoardCheck). A difference
    ///   is a desync: reported, never papered over (the host decides: MpSession.HostContinue, HostTerminate).
    /// - Opening a game starts from the board the game log saved at the latest turn's start this device has (its
    ///   turn-NNN file; a small index per game says which, at what log entry, with its check line) and plays only the
    ///   log after it: what the player has seen already at once, the rest as moves on screen, speeded up. The local
    ///   game log goes on from that turn. Without a usable one (a new device, the host went on from their own board
    ///   after a desync), the game is rebuilt from the board it is played on from.
    /// - A side is played by the player who holds it; an AI side by the device of the next player to move after it
    ///   (DriverOf). Everyone else only watches that side (GameController.PlayedElsewhere).
    ///
    /// No device is a server: whoever's turn it is writes, the store refuses a write that is out of order (Mp.Append),
    /// and a device that finds the log has moved on under it loads the game again from the store (Resync).
    /// </summary>
    public static class MpSession {

        public enum Phase {
            None,
            /// <summary>Loading the board and the log.</summary>
            Opening,
            /// <summary>Playing what happened since the player last looked.</summary>
            CatchingUp,
            /// <summary>In step with the log: this device's players play, the others' moves show as they arrive.</summary>
            Live,
            /// <summary>Over (won, lost, ended by the host), or this player is no longer in it.</summary>
            Ended
        }

        public static bool Active { get { return State != Phase.None; } }
        public static Phase State { get; private set; }
        /// <summary>The game as the store last had it.</summary>
        public static MpGame Game { get; private set; }
        /// <summary>This player only watches.</summary>
        public static bool Spectating { get; private set; }
        /// <summary>Something for the player to read (could not reach the store, removed by the host), "" nothing.</summary>
        public static string Notice = "";
        /// <summary>The game, the phase or the notice changed (for the screens).</summary>
        public static event Action Changed;

        /// <summary>How a board is loaded from a file: every side played as the file says. The front end sets its own (MatchSetup); the tests theirs.</summary>
        public static Func<string, bool> LoadBoard = DefaultLoad;
        /// <summary>
        /// The board as a file's text: what the host sends as the board the game is played on from. The game log's
        /// snapshot (as a replay's turn boards are): every unit with its stores, every building, each side's money and
        /// the round, so it loads back exactly.
        /// </summary>
        public static Func<string> BoardText = () => GameLog.Snapshot(GameController.gc != null ? GameController.gc.activePlayer : null, "multiplayer");

        /// <summary>Entries played on this board (the next one expected).</summary>
        public static int Applied { get; private set; }
        /// <summary>This device's board differs from the log (a desync it saw): it plays nothing more until the host puts it right.</summary>
        public static bool Diverged { get; private set; }

        static readonly Queue<MpEntry> incoming = new Queue<MpEntry>();
        static MpEntry current;
        static bool begun;

        /// <summary>An entry waiting to go to the store. Its lines may be worked out only when it is sent (<see cref="make"/>): the board as it stands once what caused it is over.</summary>
        class Send { public string kind; public List<string> lines; public Func<List<string>> make; public Action<MpGame> then; public LocalSave save; }
        static readonly List<Send> pending = new List<Send>();
        static readonly List<string> outbox = new List<string>();

        /// <summary>Others' orders are being played on the board: nothing that happens is this device's to send.</summary>
        static bool applying;
        /// <summary>A call to the store is under way.</summary>
        static bool busy;
        static bool hooked;
        /// <summary>Sending failed (no connection): not tried again before this time (unscaled seconds).</summary>
        static float retryAt;
        static float retryWait = 2f;
        static int desyncsSeen;
        /// <summary>The entry count at which the turn on the board began (which turn the clock in the game's meta is for).</summary>
        static int turnBeganAt;
        static float nextPoll;
        static int seenAtOpen;

        static GameController gc { get { return GameController.gc; } }
        static void Say() { if (Changed != null) Changed(); }

        // ------------------------------------------------------------------ who plays what

        /// <summary>
        /// The player whose device plays <paramref name="p"/>: the one who holds it; for an AI side, the holder of the
        /// next side after it in turn order that a player holds (so the AI moves when that player is there to move
        /// after it); "" for an open side (its player left: the host decides).
        /// </summary>
        public static string DriverOf(MpGame g, Player p) {
            if (g == null || p == null || gc == null) return "";
            MpSlot s = g.Slot(p.id);
            if (s == null) return g.host;
            if (s.kind == MpSlotKind.Player) return s.player;
            if (s.kind == MpSlotKind.Open) return "";
            int i = gc.players.IndexOf(p);
            for (int k = 0; k < gc.players.Count; k++) {
                i = i <= 0 ? gc.players.Count - 1 : i - 1;   // (the turn passes down the list: GameController.EndTurn)
                Player q = gc.players[i];
                if (q == null || q.eliminated) continue;
                MpSlot qs = g.Slot(q.id);
                if (qs != null && qs.kind == MpSlotKind.Player) return qs.player;
            }
            return g.host;   // no player left in the game: the host's device plays it out
        }

        public static string DriverOf(Player p) { return DriverOf(Game, p); }

        /// <summary>Is the side to move this device's to play now (its player's, or an AI it runs)?</summary>
        public static bool MyTurn {
            get { return State == Phase.Live && !Blocked && gc != null && !gc.gameOver && gc.activePlayer != null && DriverOf(gc.activePlayer) == Mp.Me && Mp.Me.Length > 0; }
        }

        /// <summary>This device is in step with the log and may add to it (the in-game menu's Resign, at any time).</summary>
        public static bool InStep { get { return State == Phase.Live && !Blocked && !busy && current == null && incoming.Count == 0 && gc != null && !gc.gameOver; } }

        /// <summary>Nothing may be played here now: others' orders are being played, a desync waits for the host, or this player only watches.</summary>
        static bool Blocked { get { return applying || Diverged || Spectating || (Game != null && Game.desync.Length > 0); } }

        static bool Elsewhere(Player p) {
            if (State != Phase.Live || Blocked) return true;
            return DriverOf(p) != Mp.Me;
        }

        /// <summary>
        /// Each AI side's AI runs on this device only while it is that side's turn and this device is the one to play
        /// it; the screen shows the game through this player's side (the one to move, if it is theirs), a spectator
        /// everything.
        /// </summary>
        static void RefreshControl() {
            if (gc == null || Game == null) return;
            foreach (Player p in gc.players) {
                if (p == null) continue;
                MpSlot s = Game.Slot(p.id);
                bool ai = s != null && s.kind == MpSlotKind.AI;
                AIPlayer full = p.GetComponent<AIPlayer>();
                if (ai && full == null) { ScenarioLoader.SetControl(p, true, s.profile); full = p.GetComponent<AIPlayer>(); }
                if (!ai && full != null && full.enabled) ScenarioLoader.SetControl(p, false);
                if (ai && full != null) full.enabled = State == Phase.Live && !Blocked && !gc.gameOver && gc.activePlayer == p && DriverOf(p) == Mp.Me;
            }
            int viewer = -1; bool shown = !Spectating;
            if (!Spectating) {
                Player v = null;
                if (gc.activePlayer != null && Game.Holds(Mp.Me, gc.activePlayer.id)) v = gc.activePlayer;
                if (v == null) foreach (Player p in gc.players) if (p != null && !p.eliminated && Game.Holds(Mp.Me, p.id)) { v = p; break; }
                if (v == null) foreach (Player p in gc.players) if (p != null && Game.Holds(Mp.Me, p.id)) { v = p; break; }
                viewer = v != null ? gc.players.IndexOf(v) : -1;
                if (v == null) shown = false;
            }
            if (gc.fogViewer != viewer || gc.fogShownOnScreen != shown) {
                gc.fogViewer = viewer; gc.fogShownOnScreen = shown;
                FogOfWar.Changed(); FogOfWar.Refresh();
            }
        }

        // ------------------------------------------------------------------ opening and closing

        static bool DefaultLoad(string path) {
            bool fromFile = ScenarioLoader.ControlFromFile, asFiled = ScenarioLoader.SidesAsFiled;
            try { ScenarioLoader.ControlFromFile = true; ScenarioLoader.SidesAsFiled = true; return ScenarioLoader.LoadBoardFromPath(path); }
            finally { ScenarioLoader.ControlFromFile = fromFile; ScenarioLoader.SidesAsFiled = asFiled; }
        }

        static string Folder { get { return Path.Combine(Application.persistentDataPath, "multiplayer"); } }
        static string SeenKey(MpGame g) { return "mp-seen-" + Mp.Me + "-" + g.code + "-" + g.epoch; }

        // ------------------------------------------------------------------ where this device's game stands: the game log's own turn boards

        /// <summary>A turn's start as the game log saved it: its board file, the log's place after it, and the board's check line.</summary>
        class LocalSave { public string board, check; public GameLog.SavePoint point; }

        /// <summary>The entry count the index on disk stands at (-1 none written this session).</summary>
        static int savedAt = -1;
        /// <summary>Opening the game: the entry count it was played on from (0 the start; more: a turn this device saved, or the host's board after a desync).</summary>
        public static int OpenedFrom { get; private set; }
        /// <summary>Opening the game: how many log entries were played onto that board.</summary>
        public static int EntriesPlayedOnOpen { get; private set; }
        /// <summary>Opening the game: the board came from this device's own game log (a turn it saved).</summary>
        public static bool OpenedFromCopy { get; private set; }

        static string Stem(MpGame g) {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in Mp.Me) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return Path.Combine(Folder, "index-" + sb + "-" + g.code + "-" + g.epoch);
        }

        /// <summary>The turn the game log has just saved (GameLog.LastTurnBoard): its file and place, and the board's check line now.</summary>
        static LocalSave Capture() {
            if (gc == null || Game == null || GameLog.LastTurnBoard == null || GameLog.LastTurnPoint == null) return null;
            return new LocalSave { board = GameLog.LastTurnBoard, point = GameLog.LastTurnPoint, check = GameLog.BoardCheck() };
        }

        /// <summary>Note that the game after <paramref name="entries"/> log entries is the board <paramref name="save"/> names (an index, a few lines: no board is written).</summary>
        static void WriteCopy(int entries, LocalSave save) {
            if (save == null || Game == null || Diverged || entries <= savedAt) return;
            try {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Stem(Game) + ".txt", string.Join("\n", new[] {
                    entries.ToString(CultureInfo.InvariantCulture), save.board, save.point.folder,
                    save.point.length.ToString(CultureInfo.InvariantCulture), save.point.turns.ToString(CultureInfo.InvariantCulture), save.check }));
                savedAt = entries;
            } catch (Exception e) { Debug.LogWarning("Multiplayer: could not note where the game stands: " + e.Message); }
        }

        /// <summary>The turn this device's index names for a game: the entry count, the check line, the board file and the log's place.</summary>
        static bool ReadCopy(MpGame g, out int entries, out string check, out string board, out GameLog.SavePoint point) {
            entries = -1; check = ""; board = null; point = null;
            try {
                string txt = Stem(g) + ".txt";
                if (!File.Exists(txt)) return false;
                string[] l = File.ReadAllText(txt).Split(new[] { '\n' }, 6);
                long length; int turns;
                if (l.Length < 6 || !int.TryParse(l[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out entries)
                    || !long.TryParse(l[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out length) || !int.TryParse(l[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out turns)) return false;
                board = l[1]; check = l[5];
                point = new GameLog.SavePoint { folder = l[2], length = length, turns = turns };
                return entries > 0 && File.Exists(board);
            } catch (Exception) { return false; }
        }

        /// <summary>The turn's board, ready to load: with the log's place marked, so the local game log goes on from that turn (when its folder is still there).</summary>
        static string BoardToOpen(string board, GameLog.SavePoint point) {
            string text = File.ReadAllText(board);
            if (point != null && Directory.Exists(point.folder)) text = ScenarioLoader.WithSavePoint(text, point);
            string path = Path.Combine(Folder, "opening.dat");
            File.WriteAllText(path, text);
            return path;
        }

        /// <summary>Forget where this device's game stood (it is over and looked at, or the player left it). Its game logs stay, as any game's do.</summary>
        public static void ForgetCopies(string code) {
            try {
                if (!Directory.Exists(Folder)) return;
                foreach (string f in Directory.GetFiles(Folder, "index-*-" + code + "-*")) File.Delete(f);
            } catch (Exception) { }
        }

        /// <summary>
        /// A fresh game log for the board just loaded, unless loading it started one (ScenarioLoader does in play): the
        /// log knows where every unit stands from its start, and only then writes their moves.
        /// </summary>
        static void Record() {
            if (GameLog.Recording || Replay.Active) return;
            GameLog.Start("multiplayer " + (Game != null ? Game.Title : ""), "Human", null);
        }

        /// <summary>The banner's name for a side a person plays on another device.</summary>
        static string NameOf(Player p) {
            if (Game == null || p == null) return null;
            MpSlot s = Game.Slot(p.id);
            if (s == null || s.kind != MpSlotKind.Player || s.player == Mp.Me) return null;
            return s.playerName;
        }

        static string EndLine() { return Game != null && Game.state == MpState.Terminated ? "Ended by the host" : null; }

        static void Hook() {
            if (hooked) return;
            hooked = true;
            GameController.PlayedElsewhere = Elsewhere;
            GameController.PlayerNameOf = NameOf;
            GameController.EndLineOverride = EndLine;
            GameLog.Written += OnWritten;
            GameController.TurnStarted += OnTurnStarted;
            GameController.GameEnded += OnGameEnded;
        }

        static void Unhook() {
            if (!hooked) return;
            hooked = false;
            if (GameController.PlayedElsewhere == (Func<Player, bool>)Elsewhere) GameController.PlayedElsewhere = null;
            if (GameController.PlayerNameOf == (Func<Player, string>)NameOf) GameController.PlayerNameOf = null;
            if (GameController.EndLineOverride == (Func<string>)EndLine) GameController.EndLineOverride = null;
            GameLog.Written -= OnWritten;
            GameController.TurnStarted -= OnTurnStarted;
            GameController.GameEnded -= OnGameEnded;
        }

        /// <summary>Leave the networked game on this device (the game in the store goes on): the board is an ordinary board again.</summary>
        public static void Close() {
            Unhook();
            if (State != Phase.None && gc != null) { gc.fogViewer = -1; gc.fogShownOnScreen = true; }
            State = Phase.None; Game = null; Spectating = false; Diverged = false; Notice = ""; savedAt = -1;
            incoming.Clear(); current = null; pending.Clear(); outbox.Clear();
            applying = busy = false;
            retryAt = 0f; retryWait = 2f;
            Replay.Instant = false;
            Unit.MoveSpeedScale = 1f;
            Say();
        }

        /// <summary>
        /// The host's lobby started the game: the board on this device is the one it is played on from (the scenario with
        /// the lobby's choices). It goes to the store, and the game is under way.
        /// </summary>
        public static async Task<MpResult> BeginAsHost(string code) {
            Close();
            MpGame g = await Mp.Load(code);
            if (g == null) return MpResult.Fail("There is no game " + code + ".");
            if (gc == null || gc.activePlayer == null) return MpResult.Fail("No board to start the game on.");
            MpResult r = await Mp.Start(code, BoardText(), gc.activePlayer.personal_name, DriverOf(g, gc.activePlayer));
            if (!r.ok) return r;
            Game = r.game; Spectating = false; Applied = 0; turnBeganAt = 0; Diverged = false;
            desyncsSeen = Replay.Desyncs.Count;
            if (gc.headless) GameLog.Stop();   // (headless, a board is loaded with no log of its own: whatever is recording is an earlier board's)
            Record();
            Hook();
            State = Phase.Live;
            PlayerPrefs.SetString(SeenKey(Game), "0");
            TurnBegan();
            Say();
            return r;
        }

        /// <summary>
        /// Open the game under <paramref name="code"/> on this device's board, as it stands in the store: the board it is
        /// played on from, then its log. False (Notice says why) if it cannot be: no such game, not started, this
        /// player not in it, the store out of reach.
        /// </summary>
        public static async Task<bool> Open(string code) {
            Close();
            State = Phase.Opening;
            MpGame g = await Mp.Load(code);
            if (g == null) return Fail(Mp.LastError.Length > 0 ? Mp.LastError : "There is no game " + code + ".");
            MpMember m = g.Member(Mp.Me);
            if (m == null) return Fail(g.banned.Contains(Mp.Me) ? "The host removed you from " + g.Title + "." : "You are not in " + g.Title + ".");
            if (g.state == MpState.Lobby) return Fail(g.Title + " has not started yet.");
            Game = g;
            Spectating = !g.IsPlayer(Mp.Me);
            int seen;
            int.TryParse(PlayerPrefs.GetString(SeenKey(g), "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out seen);
            return await Rebuild(seen);
        }

        static bool Fail(string why) {
            Close();
            Notice = why;
            Say();
            return false;
        }

        /// <summary>
        /// The game loaded again from the store: the board it is played on from, then every entry after it. Entries
        /// below <paramref name="instantBelow"/> are played at once, the rest as moves on screen (Tick).
        /// </summary>
        static async Task<bool> Rebuild(int instantBelow) {
            busy = true;
            try {
                Unhook();
                incoming.Clear(); current = null; pending.Clear(); outbox.Clear(); applying = false; Diverged = false;
                List<MpEntry> entries = null;
                int from = -1;
                OpenedFromCopy = false;
                // This device's copy, if the log still agrees with it: the entry it stands after is the turn (or the
                // game's end, or the host's new board) whose check line it kept, and loaded it checks the same.
                int copyAt; string copyCheck, copyBoard; GameLog.SavePoint copyPoint;
                if (ReadCopy(Game, out copyAt, out copyCheck, out copyBoard, out copyPoint) && copyAt >= Game.baseEntry) {
                    List<MpEntry> after = await Mp.Fetch(Game, copyAt - 1);
                    if (after == null) return Fail(Mp.LastError);
                    MpEntry last = after.Count > 0 ? after[0] : null;
                    bool agrees = last != null && last.n == copyAt - 1 && last.kind != MpEntryKind.Orders && last.lines.Count > 1 && last.lines[1] == copyCheck;
                    if (agrees && LoadInto(BoardToOpen(copyBoard, copyPoint)) && GameLog.BoardCheck() == copyCheck) {
                        from = copyAt;
                        entries = after.GetRange(1, after.Count - 1);
                        OpenedFromCopy = true;
                        savedAt = copyAt;
                    } else if (agrees) Debug.LogWarning("Multiplayer: the turn this device saved of " + Game.Title + " did not load as it was: the game is rebuilt from its board.");
                }
                if (from < 0) {
                    // The board the game is played on from, and the whole log after it.
                    KeyValuePair<int, string> board = await Mp.Base(Game);
                    if (board.Value == null) return Fail("Could not read " + Game.Title + "'s board from the game store.");
                    Directory.CreateDirectory(Folder);
                    string path = Path.Combine(Folder, Game.code + "-" + Game.epoch + "-base.dat");
                    File.WriteAllText(path, board.Value);
                    if (!LoadInto(path)) return Fail("Could not load " + Game.Title + "'s board.");
                    from = board.Key;
                    entries = await Mp.Fetch(Game, from);
                    if (entries == null) return Fail(Mp.LastError);
                }
                Record();
                Applied = from; turnBeganAt = from;
                OpenedFrom = from; EntriesPlayedOnOpen = entries.Count;
                desyncsSeen = Replay.Desyncs.Count;
                foreach (MpEntry e in entries) incoming.Enqueue(e);
                seenAtOpen = instantBelow;
                Hook();
                State = Phase.CatchingUp;
                RefreshControl();
                // What the player has seen already: at once.
                Replay.Instant = true;
                try { while ((current != null || (incoming.Count > 0 && incoming.Peek().n < instantBelow)) && !Diverged) if (!StepIncoming()) break; }
                finally { Replay.Instant = false; }
                if (gc != null && gc.headless) Drain();
                Settle();
                Say();
                return true;
            } finally { busy = false; }
        }

        /// <summary>A board file onto the board (the game log of the board as it was stopped first). False if it did not load.</summary>
        static bool LoadInto(string path) {
            GameLog.Stop();
            try { return LoadBoard(path); }
            catch (Exception e) { Debug.LogError("Multiplayer: loading " + path + " failed: " + e); return false; }
        }

        /// <summary>Load the game again from the store as it stands (this device's board had something the log refused).</summary>
        static Task<bool> Resync() { return Rebuild(int.MaxValue); }

        // ------------------------------------------------------------------ what others did, onto this board

        /// <summary>Headless (no frames): everything waiting is played at once.</summary>
        static void Drain() {
            int guard = 0;
            while ((current != null || incoming.Count > 0) && !Diverged && guard++ < 1000000) if (!StepIncoming()) break;
        }

        /// <summary>Nothing left to play: the session is in step with the log (live), or over.</summary>
        static void Settle() {
            if (current != null || incoming.Count > 0) { applying = true; return; }
            applying = false;
            Unit.MoveSpeedScale = 1f;
            if (State == Phase.CatchingUp || State == Phase.Live) {
                State = Game.Finished || (gc != null && gc.gameOver) ? Phase.Ended : Phase.Live;
                if (Game.state == MpState.Terminated && gc != null && !gc.gameOver) gc.EndGame(null);
                TurnBegan();
            }
            RefreshControl();
        }

        /// <summary>
        /// One step of playing others' entries on the board: the next order of the entry in hand, or the entry after
        /// it. Orders wait for the board to be still (as in play); at once when headless or catching up on what was
        /// seen. False when there is nothing to do just now.
        /// </summary>
        static bool StepIncoming() {
            if (Diverged || gc == null) return false;
            if (current == null) {
                if (incoming.Count == 0) return false;
                current = incoming.Dequeue();
                begun = false;
                if (current.n < Applied) { current = null; return true; }   // (played already)
                if (current.n > Applied) { Diverge("the log's entry " + current.n + " arrived where " + Applied + " was expected"); return false; }
            }
            applying = true;
            bool still = gc.headless || Replay.Instant || !Unit.AnyAnimating();
            switch (current.kind) {
                case MpEntryKind.Orders:
                    if (!begun) { Replay.BeginOrders(current.lines); begun = true; }
                    if (!still) return false;
                    if (Replay.NextOrder()) { if (NewDesyncs()) return false; if (Replay.OrdersLeft > 0) return true; }
                    break;
                case MpEntryKind.Turn:
                    if (!still) return false;
                    if (!PlayTurnEntry(current)) return false;
                    break;
                case MpEntryKind.Over:
                    if (!still) return false;
                    if (!PlayOverEntry(current)) return false;
                    break;
                case MpEntryKind.Base:
                    // (The board the game goes on from is loaded by Tick, which sees the game's base has moved.)
                    break;
                case MpEntryKind.End:
                    if (!gc.gameOver) gc.EndGame(null);
                    break;
            }
            Applied = current.n + 1;
            if (current.kind == MpEntryKind.Turn || current.kind == MpEntryKind.Base) turnBeganAt = Applied;
            // A turn's start: the game log has just saved its board; the index says it is this entry's.
            if (current.kind == MpEntryKind.Turn) WriteCopy(Applied, Capture());
            current = null;
            PlayerPrefs.SetString(SeenKey(Game), Applied.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        static bool NewDesyncs() {
            if (Replay.Desyncs.Count <= desyncsSeen) return false;
            string what = string.Join("; ", Replay.Desyncs.GetRange(desyncsSeen, Replay.Desyncs.Count - desyncsSeen).ToArray());
            desyncsSeen = Replay.Desyncs.Count;
            Diverge(what);
            return true;
        }

        /// <summary>The turn was ended on its author's device: it is ended here as the game ends it, and the board checked against theirs.</summary>
        static bool PlayTurnEntry(MpEntry e) {
            string heading = e.lines.Count > 0 ? e.lines[0] : "", check = e.lines.Count > 1 ? e.lines[1] : "";
            if (Heading() != heading && !gc.gameOver) {
                try { gc.EndTurn(); }
                catch (Exception ex) { Diverge("ending the turn failed here: " + ex.Message); return false; }
            }
            if (NewDesyncs()) return false;
            if (Heading() != heading) { Diverge("the turn ended as \"" + Heading().Trim('=', ' ') + "\" here, the log has \"" + heading.Trim('=', ' ') + "\""); return false; }
            string here = GameLog.BoardCheck();
            if (check.Length > 0 && here != check) { Diverge("at the start of \"" + heading.Trim('=', ' ') + "\": " + Difference(here, check)); return false; }
            return true;
        }

        static bool PlayOverEntry(MpEntry e) {
            string line = e.lines.Count > 0 ? e.lines[0] : "", check = e.lines.Count > 1 ? e.lines[1] : "";
            if (!gc.gameOver) {
                try { gc.EndTurn(); }   // (a game that ends as a turn ends: an HQ taken, the round limit)
                catch (Exception ex) { Diverge("ending the turn failed here: " + ex.Message); return false; }
            }
            if (!gc.gameOver) { Diverge("the game is not over here, the log has \"" + line.Trim('=', ' ') + "\""); return false; }
            string mine = GameLog.Lines.GameOver(gc.days, gc.winner);
            if (mine != line) { Diverge("the game ended as \"" + mine.Trim('=', ' ') + "\" here, the log has \"" + line.Trim('=', ' ') + "\""); return false; }
            string here = GameLog.BoardCheck();
            if (check.Length > 0 && here != check) { Diverge("at the game's end: " + Difference(here, check)); return false; }
            return true;
        }

        static string Heading() { return GameLog.Lines.Round(gc.days, gc.activePlayer != null ? gc.activePlayer.personal_name : "?"); }

        /// <summary>What two board check lines differ in, in words: "here Blue Gr(1,2)s4, the log Gr(1,2)s3".</summary>
        public static string Difference(string here, string there) {
            HashSet<string> a = new HashSet<string>(Tokens(here)), b = new HashSet<string>(Tokens(there));
            List<string> onlyHere = new List<string>(), onlyThere = new List<string>();
            foreach (string t in a) if (!b.Contains(t)) onlyHere.Add(t);
            foreach (string t in b) if (!a.Contains(t)) onlyThere.Add(t);
            onlyHere.Sort(StringComparer.Ordinal); onlyThere.Sort(StringComparer.Ordinal);
            const int most = 6;
            string h = string.Join(", ", onlyHere.GetRange(0, Math.Min(most, onlyHere.Count)).ToArray()) + (onlyHere.Count > most ? ", ..." : "");
            string t2 = string.Join(", ", onlyThere.GetRange(0, Math.Min(most, onlyThere.Count)).ToArray()) + (onlyThere.Count > most ? ", ..." : "");
            return "this device has " + (h.Length > 0 ? h : "nothing more") + "; the log has " + (t2.Length > 0 ? t2 : "nothing more");
        }

        static IEnumerable<string> Tokens(string check) {
            foreach (string part in check.Split('|')) {
                int colon = part.IndexOf(':');
                string kind = colon > 0 ? part.Substring(0, colon).Trim() : "";
                string rest = colon > 0 ? part.Substring(colon + 1) : part;
                foreach (string t in rest.Split(kind == "units" ? new[] { ' ' } : new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    if (t.Trim().Length > 0) yield return (kind.Length > 0 ? kind.TrimEnd('s') + " " : "") + t.Trim();
            }
        }

        /// <summary>This board is not the log's: nothing more is played here, and the game is told (the host decides what happens).</summary>
        static void Diverge(string what) {
            Diverged = true;
            applying = false;
            Debug.LogError("Multiplayer desync in " + (Game != null ? Game.Title : "?") + ": " + what);
            RefreshControl();
            Report(what);
            Say();
        }

        static async void Report(string what) {
            if (Game == null) return;
            string code = Game.code, who = Mp.PlayerName;
            MpResult r = await Mp.Change(code, g => {
                if (g.desync.Length > 0 || g.Finished) return "";   // (reported already)
                g.desync = what; g.desyncBy = who;
                return null;
            });
            if (r.game != null && Game != null && Game.code == code) { Game = r.game; Say(); }
        }

        // ------------------------------------------------------------------ what happens here, to the log

        static void OnWritten(string line) {
            if (State != Phase.Live || applying || Diverged) return;
            if (Replay.IsOrder(line)) outbox.Add(line.TrimEnd());
        }

        static void QueueOrders() {
            if (outbox.Count == 0) return;
            pending.Add(new Send { kind = MpEntryKind.Orders, lines = new List<string>(outbox) });
            outbox.Clear();
        }

        static void OnTurnStarted(Player p) {
            if (State != Phase.Live || applying || Diverged || gc == null) return;
            QueueOrders();
            if (gc.gameOver) return;   // (its end is written by OnGameEnded)
            Player next = gc.activePlayer;
            string name = next != null ? next.personal_name : "";
            int turn = Applied + pending.Count + 1;
            // The next side is this player's own too (two sides at one screen): their clock starts with the turn.
            bool mine = next != null && Game.turnSeconds > 0 && Game.Holds(Mp.Me, next.id);
            string now = mine ? MpJson.Now() : "";
            pending.Add(new Send {
                kind = MpEntryKind.Turn, lines = new List<string> { Heading(), GameLog.BoardCheck() },
                then = g => { g.turnSide = name; g.turnDriver = DriverOf(g, next); g.clockStarted = now; g.clockEntry = mine ? turn : -1; },
                save = Capture()   // (the board the game log saved as the turn started, before anyone acts in it)
            });
            turnBeganAt = turn;
            Game.clockStarted = now; Game.clockEntry = mine ? turn : -1;
            TurnBegan();
            Flush();
        }

        /// <summary>
        /// The game ended here: its end goes to the log once what ended it is over (a side resigning ends the game
        /// before its units are gone), from the next Tick or the host's own call. Nobody acts once a game is over, so
        /// the board is final by then.
        /// </summary>
        static void OnGameEnded() {
            if (State == Phase.CatchingUp && current != null && current.kind != MpEntryKind.Over && current.kind != MpEntryKind.End && !(Game != null && Game.Finished))
                Debug.LogWarning("Multiplayer: the game ended while " + (Game != null ? Game.Title : "a game") + " was being opened, at entry " + (current != null ? current.n.ToString() : "?") + " (" + Replay.LastAction + "). Please report this with the console above.\n" + Environment.StackTrace);
            if (State != Phase.Live || applying || Diverged || gc == null) return;
            QueueOrders();
            pending.Add(new Send {
                kind = MpEntryKind.Over,
                make = () => new List<string> { GameLog.Lines.GameOver(gc.days, gc.winner), GameLog.BoardCheck() },
                then = null
            });
            Send over = pending[pending.Count - 1];
            over.then = g => { g.state = MpState.Over; g.winner = gc != null ? gc.WinnersName() ?? "" : ""; g.turnDriver = ""; };
        }

        /// <summary>
        /// Send what is waiting, an entry at a time, in order. An entry the store refuses because the log moved on
        /// (someone ended this player's turn, the host stepped in) means this board has something the log never will:
        /// the game is loaded again from the store. One that could not be sent (no connection) waits and is tried again.
        /// </summary>
        static async void Flush() {
            if (busy || State != Phase.Live) return;
            if (Time.unscaledTime < retryAt) return;
            busy = true;
            bool resync = false;
            try {
                while (pending.Count > 0 && Game != null) {
                    Send s = pending[0];
                    if (s.lines == null) s.lines = s.make != null ? s.make() : new List<string>();
                    MpResult r = await Mp.Append(Game, Applied, s.kind, s.lines, s.then);
                    if (State == Phase.None) return;
                    if (r.ok) {
                        pending.RemoveAt(0);
                        Applied++;
                        if (s.save != null) WriteCopy(Applied, s.save);
                        if (r.game != null) Game = r.game;
                        PlayerPrefs.SetString(SeenKey(Game), Applied.ToString(CultureInfo.InvariantCulture));
                        retryWait = 2f;
                        if (Notice.Length > 0) { Notice = ""; Say(); }
                    } else if (r.conflict) { resync = true; break; }
                    else {
                        // Out of reach (no connection, the service down): kept, and tried again later, longer each time (at most a minute).
                        Notice = (r.error ?? "Could not reach the game store.") + " Your moves are kept and will be sent when it can be reached.";
                        retryAt = Time.unscaledTime + retryWait;
                        retryWait = Mathf.Min(retryWait * 2f, 60f);
                        Say();
                        break;
                    }
                }
            } finally { busy = false; }
            if (resync) {
                Notice = "The game moved on while you were playing: it has been loaded again as it stands.";
                await Resync();
            } else if (pending.Count == 0) { if (State == Phase.Live && Game != null && Game.Finished) { State = Phase.Ended; } Say(); }
        }

        /// <summary>
        /// A turn has begun on this board (or the session has just come into step): the AIs and the view are set for
        /// it, and if it is this player's own side to move, their clock starts ("your turn" is on the screen).
        /// </summary>
        static void TurnBegan() {
            RefreshControl();
            if (State != Phase.Live || Game == null || gc == null || gc.gameOver || gc.activePlayer == null) return;
            if (Game.turnSeconds <= 0 || Blocked || pending.Count > 0) return;
            if (!Game.Holds(Mp.Me, gc.activePlayer.id)) return;
            if (Game.clockEntry == turnBeganAt && Game.clockStarted.Length > 0) return;
            StartClock(turnBeganAt);
        }

        static async void StartClock(int turn) {
            string code = Game.code;
            string now = MpJson.Now();
            Game.clockStarted = now; Game.clockEntry = turn;   // (shown at once; the store's follows)
            MpResult r = await Mp.Change(code, g => {
                if (g.clockEntry == turn && g.clockStarted.Length > 0) return "";
                g.clockStarted = now; g.clockEntry = turn;
                return null;
            });
            if (r.game != null && Game != null && Game.code == code && r.game.entries >= Game.entries) { Game = r.game; Say(); }
        }

        // ------------------------------------------------------------------ the turn clock

        /// <summary>Seconds the player to move has left; null when the game has no limit or their clock has not started (they have not seen "your turn" yet).</summary>
        public static double? SecondsLeft {
            get {
                if (Game == null || Game.turnSeconds <= 0 || Game.clockEntry != turnBeganAt) return null;
                DateTime started;
                if (!MpJson.Time(Game.clockStarted, out started)) return null;
                return Game.turnSeconds - (DateTime.UtcNow - started.ToUniversalTime()).TotalSeconds;
            }
        }

        /// <summary>The player to move is out of time and it is not this device's turn: any player may end the turn for them.</summary>
        public static bool CanSkip {
            get {
                if (State != Phase.Live || Spectating || Blocked || busy || pending.Count > 0 || gc == null || gc.gameOver || gc.activePlayer == null) return false;
                if (DriverOf(gc.activePlayer) == Mp.Me) return false;
                double? left = SecondsLeft;
                return left.HasValue && left.Value <= 0;
            }
        }

        /// <summary>End the turn of a player who is out of time: what they did stands, and the next side moves.</summary>
        public static void Skip() {
            if (!CanSkip) return;
            gc.CancelPendingEndTurn();
            gc.EndTurn();   // (OnTurnStarted writes it to the log)
        }

        // ------------------------------------------------------------------ the host's decisions

        public static bool IsHost { get { return Game != null && Game.host == Mp.Me; } }

        /// <summary>
        /// The host hands a side on: to another player in the game, or to the AI. Whoever must act for the side to
        /// move is worked out again.
        /// </summary>
        public static async Task<MpResult> HostHandOver(long side, MpSlotKind kind, string player, string profile) {
            if (!IsHost) return MpResult.Fail("Only the host can do that.");
            MpResult r = await Mp.Change(Game.code, g => {
                MpSlot s = g.Slot(side);
                if (s == null) return "No such side.";
                if (kind == MpSlotKind.Player) {
                    MpMember m = g.Member(player);
                    if (m == null) return "That player is not in the game.";
                    s.kind = MpSlotKind.Player; s.player = m.id; s.playerName = m.name; s.profile = ""; m.spectator = false;
                } else { s.kind = MpSlotKind.AI; s.player = ""; s.playerName = ""; s.profile = profile ?? ""; }
                if (gc != null && gc.activePlayer != null) g.turnDriver = DriverOf(g, gc.activePlayer);
                return null;
            });
            if (r.ok) { Game = r.game; TurnBegan(); Say(); }
            return r;
        }

        /// <summary>The host takes a side out of the game: it resigns (its units go), as the game's own rule has it; the AI holds what is left of its place.</summary>
        public static async Task<MpResult> HostEliminate(long side) {
            if (!IsHost) return MpResult.Fail("Only the host can do that.");
            if (State != Phase.Live || applying || Diverged || busy || pending.Count > 0) return MpResult.Fail("Wait until the game is in step.");
            Player p = gc != null ? gc.players.Find(x => x != null && x.id == side) : null;
            if (p == null) return MpResult.Fail("No such side.");
            MpResult r = await HostHandOver(side, MpSlotKind.AI, "", "");
            if (!r.ok) return r;
            gc.Resign(p);       // (its line, and the turn's end if it was the side to move, go to the log as any order does)
            QueueOrders();
            Flush();
            return r;
        }

        /// <summary>The host removes a player (never to come back). Their sides are left open: hand each on (HostHandOver) or take it out (HostEliminate).</summary>
        public static async Task<MpResult> HostKick(string player) {
            if (!IsHost) return MpResult.Fail("Only the host can do that.");
            MpResult r = await Mp.Kick(Game.code, player);
            if (r.ok) { Game = r.game; RefreshControl(); Say(); }
            return r;
        }

        /// <summary>
        /// After a desync, the host goes on from their own board: it becomes the board the game is played on from, for
        /// everyone, and the desync is cleared.
        /// </summary>
        public static async Task<MpResult> HostContinue() {
            if (!IsHost) return MpResult.Fail("Only the host can do that.");
            if (gc == null || busy) return MpResult.Fail("Wait a moment.");
            busy = true;
            try {
                MpGame g = await Mp.Load(Game.code);
                if (g == null) return MpResult.Fail(Mp.LastError);
                List<MpEntry> rest = await Mp.Fetch(g, Applied);
                if (rest == null) return MpResult.Fail(Mp.LastError);
                int total = Applied + rest.Count;
                // The board first (under a key of its own, so nobody loads half of it), then the entry that says so.
                string was = g.epoch;
                MpResult wrote = await WriteBase(g, total + 1);
                if (!wrote.ok) return wrote;
                Player active = gc.activePlayer;
                string name = active != null ? active.personal_name : "";
                MpResult r = await Mp.Append(g, total, MpEntryKind.Base, new List<string> { Heading(), GameLog.BoardCheck() }, x => {
                    x.baseEntry = total + 1; x.desync = ""; x.desyncBy = "";
                    x.turnSide = name; x.turnDriver = DriverOf(x, active); x.clockStarted = ""; x.clockEntry = -1;
                });
                if (!r.ok) return r.conflict ? MpResult.Fail("The game moved on: try again.") : r;
                Game = r.game;
                incoming.Clear(); current = null; pending.Clear(); outbox.Clear();
                Applied = total + 1; turnBeganAt = Applied; Diverged = false; applying = false;
                desyncsSeen = Replay.Desyncs.Count;
                State = Phase.Live;
                TurnBegan();
                Say();
                return r;
            } finally { busy = false; }
        }

        static async Task<MpResult> WriteBase(MpGame g, int entry) {
            // (Mp.Start writes entry 0's board; this is the same key, written over.)
            string key = Mp.KeyBase(g);
            for (int attempt = 0; attempt < 4; attempt++) {
                Dictionary<string, MpValue> got = await Mp.Store.Read(Mp.DataId(g), new[] { key });
                if (got == null) return MpResult.Fail("Could not reach the game store.");
                MpValue was; got.TryGetValue(key, out was);
                MpWritten w = await Mp.Store.Write(Mp.DataId(g), key, entry + "\n" + BoardText(), was != null ? was.writeLock : null);
                if (w.ok) return new MpResult { ok = true, game = g };
                if (!w.conflict) return MpResult.Fail(w.error ?? "Could not write to the game store.");
            }
            return MpResult.Fail("The game kept changing: try again.");
        }

        /// <summary>The host ends the game for everyone (after a desync, or at any time).</summary>
        public static async Task<MpResult> HostTerminate() {
            if (!IsHost) return MpResult.Fail("Only the host can do that.");
            MpResult r = await Mp.Change(Game.code, g => { if (!g.Finished) g.state = MpState.Terminated; g.desync = ""; g.turnDriver = ""; return null; });
            if (r.ok) { Game = r.game; Ended(); }
            return r;
        }

        static void Ended() {
            State = Phase.Ended;   // (first: the game's end here is not news for the log)
            if (gc != null && !gc.gameOver) gc.EndGame(null);
            applying = false;
            RefreshControl();
            Say();
        }

        // ------------------------------------------------------------------ every frame

        /// <summary>
        /// Called every frame while a networked game is on the board (MpPump): others' entries are played a step at a
        /// time, what was done here is sent, and every so often the store is asked for news.
        /// </summary>
        public static void Tick() {
            if (State == Phase.None || State == Phase.Opening || Game == null || gc == null) return;
            if (!Diverged && (current != null || incoming.Count > 0)) {
                MultiplayerSettings s = MultiplayerSettings.Get;
                // What happened while the player was away plays speeded up; what happens as they watch, as it is played.
                Unit.MoveSpeedScale = State == Phase.CatchingUp ? Mathf.Max(1f, s.catchUpMoveSpeed) : 1f;
                StepIncoming();
                if (current == null && incoming.Count == 0) { Settle(); Say(); }
            } else if (State == Phase.Live && !applying) {
                QueueOrders();
                if (pending.Count > 0) Flush();
            }
            if (Time.unscaledTime >= nextPoll && !busy) {
                nextPoll = Time.unscaledTime + Mathf.Max(0.25f, MultiplayerSettings.Get.livePollSeconds);
                PollInBackground();
            }
        }

        static async void PollInBackground() { await Poll(); }

        /// <summary>Ask the store how the game stands, and take up whatever is new (entries, a new base board, the host's decisions, the game's end).</summary>
        public static async Task Poll() {
            if (busy || Game == null || State == Phase.None || State == Phase.Opening) return;
            busy = true;
            bool rebuild = false;
            try {
                string code = Game.code;
                MpGame g = await Mp.Load(code);
                if (g == null || Game == null || Game.code != code) return;
                if (g.epoch != Game.epoch) { Fail(Game.Title + " is gone."); return; }
                bool wasDesync = Game.desync.Length > 0;
                Game = g;
                if (g.Member(Mp.Me) == null) {
                    Notice = g.banned.Contains(Mp.Me) ? "The host removed you from " + g.Title + "." : "You are no longer in " + g.Title + ".";
                    State = Phase.Ended; RefreshControl(); Say();
                    return;
                }
                Spectating = !g.IsPlayer(Mp.Me);
                if (g.baseEntry > Applied) rebuild = true;   // the host put a desync right: on from their board
                else if (pending.Count == 0 && outbox.Count == 0 && !Diverged && g.entries > Applied + incoming.Count + (current != null ? 1 : 0)) {
                    int from = Applied + incoming.Count + (current != null ? 1 : 0);
                    List<MpEntry> more = await Mp.Fetch(g, from);
                    if (more != null && Game != null && Game.code == code)
                        foreach (MpEntry e in more) if (e.n >= from) { incoming.Enqueue(e); from = e.n + 1; }
                    if (incoming.Count > 0) applying = true;
                }
                if (!rebuild) {
                    if (g.state == MpState.Terminated && State != Phase.Ended && incoming.Count == 0 && current == null) Ended();
                    RefreshControl();
                    if (State == Phase.Live && !applying) TurnBegan();
                    if (gc != null && gc.headless) { Drain(); Settle(); }
                }
                Say();
            } finally { busy = false; }
            if (rebuild) await Rebuild(int.MaxValue);
        }
    }
}
