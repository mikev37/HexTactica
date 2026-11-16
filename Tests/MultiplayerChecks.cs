using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using HexTactica.Harness;
using HexTactica.Multiplayer;
using HexTactica.Testing;

/// <summary>
/// Networked games (Assets/Multiplayer), headless. Several "devices" share one store (a folder, MpFolderStore); there
/// is one board in the process, so a device is switched to by loading the game on the board as that player
/// (MpSession.Open), exactly as a player opening the game would: the board the game is played on from, then the log.
/// What each device does on its turn is played by the AI standing in for the player at the screen; its orders go to
/// the store from the game log as a player's would.
/// </summary>
public static partial class Scenarios {

    static int ExportMpChecks(Action<string, string, Action<Player, Player>, string> make) {
        int n = 0;
        Action<Player, Player> armies = (a, o) => {
            Put("Grunt", a, -2, 0); Put("Tank", a, -3, 1); Put("Grunt", a, -2, -1);
            Put("Grunt", o, 2, 0); Put("Tank", o, 3, -1); Put("Grunt", o, 2, 1);
        };
        make("mp-lobby", "mpLobby", armies,
             "Networked games, the lobby: the host's game gets a join code (6 characters from the settings' alphabet) and a name (the scenario's, the host's to change until it starts: shown as name#code). Players join by the code (typed in any case, with or without the name before it), claim open sides, and cannot take a side someone holds or change what only the host may. The host removes a player, who cannot come back; a device on another version cannot join; a spectator joins to watch. The game cannot start with a side open; once started its name is locked. Each player's list says whose turn it is. A join code is used again once its game is over, under a new epoch (the old game's log is not the new one's). A guest who leaves frees their side; the host leaving ends the game.");
        n++;
        make("mp-turns", "mpTurns", armies,
             "Networked games, turns: two players on two devices play three rounds. Each turn's orders go to the store as the game log writes them; the other device plays them through the game's rules and ends up with the same board (checked against the line its author wrote at each turn's start), opening the game afresh (catching up) or polling while it is open (watching live). Only the player to move may act; the board is seen through the player's own side.");
        n++;
        make("mp-ai", "mpAi", armies,
             "Networked games, AI sides: an AI side is played by the device of the next player to move after it (the player who would otherwise wait on it), worked out down the turn order, skipping sides that are out. With Red the AI, the host's device plays Red's turn as soon as its own ends; a spectator's device plays the same game back to the same board and can act for no side.");
        n++;
        make("mp-clock", "mpClock", armies,
             "Networked games, the turn clock: a player's clock starts when their turn is on their screen (their device writes the time), not before. A player out of time can have their turn ended by any other player: what they did stands, and the next side moves.");
        n++;
        make("mp-desync", "mpDesync", armies,
             "Networked games, a desync: a device whose board differs from the log (a unit's strength changed on it) plays its turn; the next device to play that turn back finds the board is not the log's, stops, and reports what differs in words. Nobody can act while it waits. The host goes on from their board: it becomes the board the game is played on from, every device loads it, and play goes on in step. The host ending the game ends it on every device.");
        n++;
        make("mp-kick", "mpKick", armies,
             "Networked games, the host's say over players: a player removed mid-game cannot open it again, and their side waits for the host. The host hands it to the AI (the host's device then plays it, being next to move), then to a player who joined later (who plays it on their device), and finally takes it out of the game (it resigns, the game ends for everyone). A finished game stays in a player's list until they are done with it.");
        n++;
        return n;
    }

    // ------------------------------------------------------------------ the rig: devices on one store

    /// <summary>A store that hides the log past an entry (a device that has not looked since then), for playing "polled while open".</summary>
    class LaggingStore : IMpStore, IMpLinking {
        readonly IMpStore inner;
        public int limit = int.MaxValue;
        public LaggingStore(IMpStore inner) { this.inner = inner; }
        public string PlayerId { get { return inner.PlayerId; } }
        public Task<string> SignIn() { return inner.SignIn(); }
        public async Task<Dictionary<string, MpValue>> Read(string game, IList<string> keys) {
            Dictionary<string, MpValue> got = await inner.Read(game, keys);
            if (got == null || limit == int.MaxValue) return got;
            foreach (string k in got.Keys.ToList()) {
                MpValue v = got[k];
                if (k.StartsWith("log-")) {
                    List<MpEntry> kept = MpEntry.ChunkFromJson(v.value).FindAll(e => e.n < limit);
                    if (kept.Count == 0) got.Remove(k); else v.value = MpEntry.ChunkToJson(kept);
                } else if (k == Mp.KeyMeta) {
                    MpGame g = MpGame.FromJson(v.value);
                    if (g != null && g.entries > limit) { g.entries = limit; v.value = g.ToJson(); }
                }
            }
            return got;
        }
        public Task<MpWritten> Write(string game, string key, string value, string writeLock) { return inner.Write(game, key, value, writeLock); }
        public Task<string> ReadMine() { return inner.ReadMine(); }
        public Task<bool> WriteMine(string value) { return inner.WriteMine(value); }
        IMpLinking L { get { return (IMpLinking)inner; } }
        public Task<string> StartLink() { return L.StartLink(); }
        public Task<string> ConfirmLink(string code) { return L.ConfirmLink(code); }
        public Task<string> FinishLink() { return L.FinishLink(); }
        public string LinkError { get { return L.LinkError; } }
    }

    static string mpRoot;
    static LaggingStore mpStore;

    static void MpBegin() {
        mpRoot = Path.Combine(Path.GetTempPath(), "hextactica-mp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        MpSession.LoadBoard = f => { ScenarioFile.Load(f, "Human", null); return true; };
        Application.version = "1.0";
        MultiplayerSettings s = MultiplayerSettings.Get;
        s.codeLength = 6; s.codeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; s.logChunk = 4;   // (small chunks: the log spans several keys)
    }

    static void MpEnd() {
        MpSession.Close();
        Mp.Reset();
        MultiplayerSettings.Get.logChunk = 20;
        try { Directory.Delete(mpRoot, true); } catch (Exception) { }
    }

    /// <summary>Switch to another device: its player, its name, the store as it sees it. Whatever game was open closes.</summary>
    static void Device(string id, string name) {
        MpSession.Close();
        Mp.Reset();
        mpStore = new LaggingStore(new MpFolderStore(mpRoot, id));
        Mp.Store = mpStore;
        Mp.PlayerName = name;
    }

    static T Wait<T>(Task<T> t) { return t.Result; }

    /// <summary>The sides of the board on screen, as a lobby lists them (all open).</summary>
    static List<MpSlot> BoardSlots() {
        List<MpSlot> l = new List<MpSlot>();
        foreach (Player p in Rig.gc.players) if (p != null) l.Add(new MpSlot { side = p.id, sideName = p.personal_name });
        return l;
    }

    /// <summary>
    /// A game hosted by alice (Blue) on the board on screen, with bob (Red) joined, or Red the AI; started. Leaves
    /// alice's device on screen, the game open on it.
    /// </summary>
    static string MpStartGame(Player blue, Player red, bool redAI, int turnSeconds) {
        Device("alice", "Alice");
        MpResult h = Wait(Mp.Host("Duel", "{}"));
        string code = h.game.code;
        Wait(Mp.SetSides(code, BoardSlots()));
        Wait(Mp.HostSetSlot(code, blue.id, MpSlotKind.Player, ""));
        if (redAI) Wait(Mp.HostSetSlot(code, red.id, MpSlotKind.AI, ""));
        Wait(Mp.HostChange(code, g => g.turnSeconds = turnSeconds));
        if (!redAI) {
            Device("bob", "Bob");
            Wait(Mp.Join(code, false));
            Wait(Mp.Claim(code, red.id));
            Device("alice", "Alice");
        }
        MpResult s = Wait(MpSession.BeginAsHost(code));
        if (!s.ok) throw new Exception("could not start: " + s.error);
        return code;
    }

    /// <summary>Open the game on this device's board (catching up). False if it could not be.</summary>
    static bool MpOpen(string code) { return Wait(MpSession.Open(code)); }

    /// <summary>The player at this screen plays their side's whole turn (the AI stands in for them, its orders the board's own).</summary>
    static void PlayAs(Player p) {
        ScenarioLoader.SetControl(p, true);
        AIPlayer ai = p.GetComponent<AIPlayer>();
        ai.PlayWholeTurn();
        ScenarioLoader.SetControl(p, false);
    }

    static Player Side(long id) { return Rig.gc.players.Find(p => p != null && p.id == id); }

    static List<MpEntry> Log(string code) {
        MpGame g = Wait(Mp.Load(code));
        return Wait(Mp.Fetch(g, 0));
    }

    static bool MpCheck(ScenarioFile.Loaded l, out string detail) {
        MpBegin();
        try {
            switch (l.meta.Get("check", "")) {
                case "mpLobby": return MpLobby(l, out detail);
                case "mpTurns": return MpTurns(l, out detail);
                case "mpAi": return MpAi(l, out detail);
                case "mpClock": return MpClock(l, out detail);
                case "mpDesync": return MpDesync(l, out detail);
                case "mpKick": return MpKick(l, out detail);
            }
            detail = "no such check";
            return false;
        } catch (Exception e) {
            detail = "threw: " + e;
            return false;
        } finally { MpEnd(); }
    }

    // ------------------------------------------------------------------ the lobby

    static bool MpLobby(ScenarioFile.Loaded l, out string detail) {
        List<string> r = new List<string>();
        long blue = l.ai.id, red = l.other.id;
        string alphabet = MultiplayerSettings.Get.codeAlphabet;

        Device("alice", "Alice");
        MpResult h = Wait(Mp.Host("Tidal Ridge", "{\"width\":15}"));
        string code = h.ok ? h.game.code : "";
        bool codeOk = code.Length == 6 && code.All(c => alphabet.IndexOf(c) >= 0);
        r.Add("hosted " + h.ok + ", code " + code + " (6 of the alphabet " + codeOk + "), title " + (h.game != null ? h.game.Title : "?"));
        bool titled = h.game != null && h.game.Title == "Tidal Ridge#" + code;
        Wait(Mp.SetSides(code, BoardSlots()));
        Wait(Mp.HostSetSlot(code, blue, MpSlotKind.Player, ""));
        MpResult early = Wait(Mp.Start(code, "{}", "Blue", "alice"));
        r.Add("started with Red open: refused " + !early.ok);
        MpResult renamed = Wait(Mp.HostChange(code, g => { g.name = "Chicken"; g.turnSeconds = 600; }));
        string scenario = Wait(Mp.Scenario(Wait(Mp.Load(code))));

        Device("bob", "Bob");
        MpResult joined = Wait(Mp.Join("chicken#" + code.ToLowerInvariant(), false));
        MpResult takeBlue = Wait(Mp.Claim(code, blue));
        MpResult takeRed = Wait(Mp.Claim(code, red));
        MpResult bobHosts = Wait(Mp.HostChange(code, g => g.name = "Mine"));
        r.Add("bob joined by \"chicken#" + code.ToLowerInvariant() + "\" " + joined.ok + ", took Blue (alice's) " + takeBlue.ok + ", took Red " + takeRed.ok + ", renamed as a guest " + bobHosts.ok);

        Device("carol", "Carol");
        bool carolIn = Wait(Mp.Join(code, false)).ok;
        bool carolRed = Wait(Mp.Claim(code, red)).ok;
        Device("alice", "Alice");
        bool kicked = Wait(Mp.Kick(code, "carol")).ok;
        Device("carol", "Carol");
        MpResult back = Wait(Mp.Join(code, false));
        r.Add("carol joined " + carolIn + ", took Red " + carolRed + "; removed " + kicked + ", back in " + back.ok + " (" + back.error + ")");

        Device("dave", "Dave");
        Application.version = "2.0";
        MpResult dave = Wait(Mp.Join(code, false));
        Application.version = "1.0";
        r.Add("another version: joined " + dave.ok + " (" + dave.error + ")");

        Device("eve", "Eve");
        MpResult eve = Wait(Mp.Join(code, true));
        bool eveWatches = eve.ok && eve.game.Member("eve") != null && eve.game.Member("eve").spectator;

        Device("alice", "Alice");
        MpResult began = Wait(MpSession.BeginAsHost(code));
        MpResult late = Wait(Mp.HostChange(code, g => g.name = "Later"));
        MpGame g1 = Wait(Mp.Load(code));
        r.Add("started " + began.ok + " as " + g1.Title + ", " + g1.turnSide + " to move by " + g1.turnDriver + "; renamed after " + late.ok + "; eve watches " + eveWatches);
        Wait(Mp.Refresh());
        MpSummary mine = Mp.Games.Find(s => s.code == code);
        bool aliceTurn = mine != null && mine.myTurn && mine.host;
        Device("bob", "Bob");
        Wait(Mp.Refresh());
        MpSummary his = Mp.Games.Find(s => s.code == code);
        bool bobWaits = his != null && !his.myTurn && !his.host && his.title == "Chicken#" + code;
        r.Add("lists: alice's turn " + aliceTurn + ", bob waits " + bobWaits);

        // A code is used again once its game is over: a new epoch, the old log not the new game's.
        MultiplayerSettings s1 = MultiplayerSettings.Get;
        s1.codeAlphabet = "Z"; s1.codeLength = 1;
        Device("alice", "Alice");
        MpResult z1 = Wait(Mp.Host("One", "{}"));
        MpResult z2 = Wait(Mp.Host("Two", "{}"));
        Wait(Mp.SetSides("Z", BoardSlots()));
        Wait(Mp.Append(z1.game, 0, MpEntryKind.Orders, new List<string> { "old" }, null));
        Wait(Mp.Leave("Z"));
        MpGame zOver = Wait(Mp.Load("Z"));
        MpResult z3 = Wait(Mp.Host("Three", "{}"));
        int oldLog = z3.ok ? Wait(Mp.Fetch(z3.game, 0)).Count : -1;
        s1.codeAlphabet = alphabet; s1.codeLength = 6;
        bool reused = z1.ok && z1.game.code == "Z" && !z2.ok && zOver.state == MpState.Terminated && z3.ok && z3.game.code == "Z" && z3.game.epoch != z1.game.epoch && oldLog == 0;
        r.Add("code Z: hosted " + z1.ok + ", again while in use " + z2.ok + ", host left: " + zOver.state + ", hosted again " + z3.ok + " (new epoch " + (z3.ok && z3.game.epoch != z1.game.epoch) + ", old log entries " + oldLog + ")");

        // A guest leaving frees their side and drops the game from their list.
        Device("bob", "Bob");
        Wait(Mp.Leave(code));
        MpGame g2 = Wait(Mp.Load(code));
        Wait(Mp.Refresh());
        bool left = g2.Slot(red).kind == MpSlotKind.Open && g2.Member("bob") == null && !Mp.Games.Exists(s => s.code == code) && g2.state == MpState.Playing;
        r.Add("bob left: Red open, not a member, off his list " + left);

        // A second device of alice's (her phone): a code made there, typed on her PC, and the phone is alice, games and all.
        Device("phone", "Alice's phone");
        IMpStore phone = Mp.Store;
        string linkCode = Wait(Mp.StartLink());
        Device("alice", "Alice");
        string confirmed = Wait(Mp.ConfirmLink(linkCode ?? ""));
        MpSession.Close(); Mp.Reset(); Mp.Store = phone;
        string finished = Wait(Mp.FinishLink());
        bool linked = linkCode != null && confirmed == null && finished == null && Mp.Me == "alice" && Mp.Games.Exists(s => s.code == code);
        Device("eve", "Eve");
        string bogus = Wait(Mp.ConfirmLink("NOPE00"));
        r.Add("a second device: code " + linkCode + ", confirmed on alice's " + (confirmed == null) + ", the phone is now " + (linked ? "alice, her games listed" : "not linked (" + finished + ")") + "; a wrong code refused " + (bogus != null));

        detail = string.Join("; ", r.ToArray());
        return linked && bogus != null && h.ok && codeOk && titled && !early.ok && renamed.ok && scenario == "{\"width\":15}"
            && joined.ok && !takeBlue.ok && takeRed.ok && !bobHosts.ok
            && carolIn && !carolRed && kicked && !back.ok && back.error.Contains("removed")
            && !dave.ok && dave.error.Contains("version") && eveWatches
            && began.ok && !late.ok && g1.Title == "Chicken#" + code && g1.turnDriver == "alice" && g1.turnSeconds == 600
            && aliceTurn && bobWaits && reused && left;
    }

    // ------------------------------------------------------------------ turns, catching up, watching live

    static bool MpTurns(ScenarioFile.Loaded l, out string detail) {
        List<string> r = new List<string>();
        long blueId = l.ai.id, redId = l.other.id;
        string code = MpStartGame(l.ai, l.other, false, 0);
        int desyncsBefore = Replay.Desyncs.Count;
        bool ok = true;
        int caughtUp = 0, watched = 0;
        bool gatesOk = true;
        List<string> opens = new List<string>();
        bool fromCopies = true;
        for (int round = 0; round < 3 && ok; round++) {
            // Alice: on round 0 the game is open from starting it; later she watches it live, polling.
            if (round > 0) {
                Device("alice", "Alice");
                mpStore.limit = aliceSaw;   // (as alice's device was when bob began)
                bool opened = MpOpen(code);
                opens.Add("alice " + (MpSession.OpenedFromCopy ? "copy@" : "base@") + MpSession.OpenedFrom + "+" + MpSession.EntriesPlayedOnOpen);
                fromCopies &= MpSession.OpenedFromCopy && MpSession.EntriesPlayedOnOpen == 0;
                mpStore.limit = int.MaxValue;
                MpSession.Poll().Wait();   // (what MpSession.Tick does every few seconds while the game is open)
                bool same = GameLog.BoardCheck() == bobCheck;
                if (!opened || !same || MpSession.Diverged) { r.Add("round " + round + ": alice watching: opened " + opened + ", same board as bob " + same + ", desync " + MpSession.Diverged); ok = false; break; }
                watched++;
            }
            if (!MpSession.MyTurn || Rig.gc.activePlayer.id != blueId) { r.Add("round " + round + ": not alice's turn on her device"); ok = false; break; }
            // Not hers to touch: Red's side is played elsewhere on her device during her own turn? It is bob's.
            gatesOk &= GameController.IsPlayedElsewhere(Side(redId)) && !GameController.IsPlayedElsewhere(Side(blueId)) && FogOfWar.IsHuman(Side(blueId)) && !FogOfWar.IsHuman(Side(redId));
            PlayAs(Side(blueId));
            aliceCheck = GameLog.BoardCheck();
            aliceSaw = MpSession.Applied;

            Device("bob", "Bob");
            bool open = MpOpen(code);
            opens.Add("bob " + (MpSession.OpenedFromCopy ? "copy@" : "base@") + MpSession.OpenedFrom + "+" + MpSession.EntriesPlayedOnOpen);
            // From his own copy at his last turn's start, after the first time: only alice's turn is played on it.
            if (round > 0) fromCopies &= MpSession.OpenedFromCopy && MpSession.EntriesPlayedOnOpen <= 3;
            bool same2 = GameLog.BoardCheck() == aliceCheck;
            if (!open || !same2 || MpSession.Diverged || !MpSession.MyTurn || MpSession.State != MpSession.Phase.Live) {
                r.Add("round " + round + ": bob caught up: opened " + open + ", same board " + same2 + ", desync " + MpSession.Diverged + " (" + (MpSession.Game != null ? MpSession.Game.desync : "") + "), his turn " + MpSession.MyTurn + ", " + MpSession.State);
                ok = false; break;
            }
            caughtUp++;
            gatesOk &= GameController.IsPlayedElsewhere(Side(blueId)) && !GameController.IsPlayedElsewhere(Side(redId)) && Rig.gc.fogViewer == Rig.gc.players.IndexOf(Side(redId));
            PlayAs(Side(redId));
            bobCheck = GameLog.BoardCheck();
        }
        List<MpEntry> log = Log(code);
        int orders = log.Count(e => e.kind == MpEntryKind.Orders), turns = log.Count(e => e.kind == MpEntryKind.Turn);
        int moves = log.Sum(e => e.lines.Count(x => x.Contains(" moved ("))), attacks = log.Sum(e => e.lines.Count(x => x.Contains(" attacks ")));
        bool inOrder = true;
        for (int i = 0; i < log.Count; i++) if (log[i].n != i) inOrder = false;
        int desyncs = Replay.Desyncs.Count - desyncsBefore;

        // A copy the log no longer agrees with, and one whose board is not what it says: each is passed over, and the
        // game is rebuilt from its board (as on a new device), still ending as it stands.
        string folder = Path.Combine(Application.persistentDataPath, "multiplayer");
        string txt = Directory.GetFiles(folder, "copy-bob-" + code + "-*.txt").FirstOrDefault(), dat = txt != null ? txt.Substring(0, txt.Length - 4) + ".dat" : null;
        bool badCheck = false, badBoard = false;
        if (txt != null) {
            string kept = File.ReadAllText(txt);
            File.WriteAllText(txt, kept.Substring(0, kept.IndexOf('\n') + 1) + "units: tampered");
            Device("bob", "Bob");
            MpOpen(code);
            badCheck = !MpSession.OpenedFromCopy && MpSession.OpenedFrom == 0 && GameLog.BoardCheck() == bobCheck && !MpSession.Diverged;
            // (Opening wrote nothing new: the copy is put back as it was, then its board swapped for the starting one.)
            File.WriteAllText(txt, kept);
            KeyValuePair<int, string> start = Wait(Mp.Base(Wait(Mp.Load(code))));
            File.WriteAllText(dat, start.Value);
            Device("bob", "Bob");
            MpOpen(code);
            badBoard = !MpSession.OpenedFromCopy && MpSession.OpenedFrom == 0 && GameLog.BoardCheck() == bobCheck && !MpSession.Diverged;
        }
        r.Add("opened from: " + string.Join(", ", opens.ToArray()) + "; each reopening from the device's own copy, only the other's turn played " + fromCopies
              + "; a copy the log disagrees with passed over " + badCheck + ", one whose board is not as kept passed over " + badBoard);
        r.Add("rounds: bob caught up " + caughtUp + "/3, alice watched live " + watched + "/2; log " + log.Count + " entries in order " + inOrder + " (" + orders + " of orders: " + moves + " moves, " + attacks + " attacks; " + turns + " turn ends); desyncs " + desyncs + "; only the player to move may act, each sees through their own side " + gatesOk);
        detail = string.Join("; ", r.ToArray());
        return ok && caughtUp == 3 && watched == 2 && inOrder && turns == 6 && moves > 0 && desyncs == 0 && gatesOk && fromCopies && badCheck && badBoard;
    }

    static string aliceCheck = "", bobCheck = "";
    static int aliceSaw;

    // ------------------------------------------------------------------ AI sides

    static bool MpAi(ScenarioFile.Loaded l, out string detail) {
        List<string> r = new List<string>();
        Player blue = l.ai, red = l.other;
        long blueId = blue.id, redId = red.id;

        // Who plays an AI side, on three sides (Blue, Red, Green; the turn goes Blue, Green, Red).
        Player green = Rig.AddPlayer("Green", 5);
        MpGame t = new MpGame { host = "alice" };
        t.slots.Add(new MpSlot { side = blueId, kind = MpSlotKind.Player, player = "alice" });
        t.slots.Add(new MpSlot { side = redId, kind = MpSlotKind.AI });
        t.slots.Add(new MpSlot { side = green.id, kind = MpSlotKind.Player, player = "bob" });
        string redBy = MpSession.DriverOf(t, red);
        t.Slot(redId).kind = MpSlotKind.Player; t.Slot(redId).player = "bob";
        t.Slot(green.id).kind = MpSlotKind.AI; t.Slot(green.id).player = "";
        string greenBy = MpSession.DriverOf(t, green);
        t.Slot(green.id).kind = MpSlotKind.Player; t.Slot(green.id).player = "bob";
        t.Slot(redId).kind = MpSlotKind.AI; t.Slot(redId).player = "";
        blue.eliminated = true;
        string redByWhenBlueOut = MpSession.DriverOf(t, red);
        blue.eliminated = false;
        Rig.gc.players.Remove(green);
        r.Add("three sides: Red (AI) by " + redBy + ", Green (AI) by " + greenBy + ", Red with Blue out by " + redByWhenBlueOut);
        bool driverOk = redBy == "alice" && greenBy == "bob" && redByWhenBlueOut == "bob";

        string code = MpStartGame(blue, red, true, 0);
        int desyncsBefore = Replay.Desyncs.Count;
        PlayAs(Side(blueId));
        bool aiMine = MpSession.MyTurn && Rig.gc.activePlayer.id == redId && MpSession.DriverOf(Side(redId)) == "alice";
        AIPlayer redAi = Side(redId).GetComponent<AIPlayer>();
        bool aiOn = redAi != null && redAi.enabled;
        ScenarioFile.PlayTurn();   // (the AI, enabled on alice's device: what its Update would do over frames)
        string check = GameLog.BoardCheck();
        MpGame g = Wait(Mp.Load(code));
        r.Add("Red's turn on alice's device: hers to play " + aiMine + ", its AI on " + aiOn + "; after it " + g.turnSide + " to move by " + g.turnDriver);

        Device("eve", "Eve");
        Wait(Mp.Join(code, true));
        bool opened = MpOpen(code);
        bool same = GameLog.BoardCheck() == check;
        bool watches = MpSession.Spectating && !MpSession.MyTurn && GameController.IsPlayedElsewhere(Side(blueId)) && GameController.IsPlayedElsewhere(Side(redId)) && Rig.gc.fogViewer == -1;
        AIPlayer eveRed = Side(redId).GetComponent<AIPlayer>();
        bool noAiHere = eveRed == null || !eveRed.enabled;
        int desyncs = Replay.Desyncs.Count - desyncsBefore;
        r.Add("spectator eve: opened " + opened + ", same board " + same + ", acts for nobody, sees all " + watches + ", no AI runs on her device " + noAiHere + "; desyncs " + desyncs);
        detail = string.Join("; ", r.ToArray());
        return driverOk && aiMine && aiOn && g.turnSide == "Blue" && g.turnDriver == "alice" && opened && same && watches && noAiHere && desyncs == 0;
    }

    // ------------------------------------------------------------------ the turn clock

    static bool MpClock(ScenarioFile.Loaded l, out string detail) {
        List<string> r = new List<string>();
        long blueId = l.ai.id, redId = l.other.id;
        string code = MpStartGame(l.ai, l.other, false, 60);
        MpGame g0 = Wait(Mp.Load(code));
        double? left0 = MpSession.SecondsLeft;
        r.Add("started: alice's clock " + (g0.clockStarted.Length > 0) + " (" + (left0.HasValue ? left0.Value.ToString("0") : "none") + " s left)");
        PlayAs(Side(blueId));
        MpGame g1 = Wait(Mp.Load(code));
        bool notYet = g1.clockStarted.Length == 0 && g1.turnDriver == "bob";
        r.Add("alice's turn ended: bob's clock not started before he sees his turn " + notYet);

        Device("bob", "Bob");
        MpOpen(code);
        MpGame g2 = Wait(Mp.Load(code));
        bool bobClock = g2.clockStarted.Length > 0 && MpSession.SecondsLeft.HasValue && MpSession.SecondsLeft.Value > 50;
        // Bob moves one unit, then runs out of time.
        Unit mover = Side(redId).units.Find(u => u != null && u.tile != null && u.name.StartsWith("Grunt"));
        HexTile from = mover.tile, to = mover.tile.neighbors.Find(n => n != null && n.occupier == null && n.terrain != null && n.terrain.name != "Water" && n.building == null);
        mover.MoveAlong(new List<HexTile> { to }, false, false);
        mover.FinishActive();
        MpSession.Tick();
        int entries = Wait(Mp.Load(code)).entries;
        bool sent = entries > g2.entries;
        bool cannotSkipOwn = !MpSession.CanSkip;
        Wait(Mp.Change(code, g => { g.clockStarted = DateTime.UtcNow.AddHours(-1).ToString("o"); return null; }));
        r.Add("bob's turn on screen: his clock started " + bobClock + "; moved a grunt " + Pos(from) + " -> " + Pos(to) + ", sent at once " + sent + "; cannot skip his own turn " + cannotSkipOwn);

        Device("alice", "Alice");
        MpOpen(code);
        double? left = MpSession.SecondsLeft;
        bool canSkip = MpSession.CanSkip;
        MpSession.Skip();
        MpGame g3 = Wait(Mp.Load(code));
        Unit moved = Rig.At(to.x, to.y).occupier;
        bool stands = moved != null && moved.owner.id == redId && Rig.At(from.x, from.y).occupier == null;
        bool aliceAgain = Rig.gc.activePlayer.id == blueId && g3.turnDriver == "alice" && MpSession.MyTurn;
        string check = GameLog.BoardCheck();
        r.Add("bob out of time on alice's device (" + (left.HasValue ? left.Value.ToString("0") : "none") + " s): she can skip " + canSkip + "; skipped: his move stands " + stands + ", her turn again " + aliceAgain);

        Device("bob", "Bob");
        bool opened = MpOpen(code);
        bool same = GameLog.BoardCheck() == check && !MpSession.MyTurn && !MpSession.Diverged;
        r.Add("bob opens it again: same board, not his turn " + same);
        detail = string.Join("; ", r.ToArray());
        return g0.clockStarted.Length > 0 && left0.HasValue && left0.Value > 50 && notYet && bobClock && sent && cannotSkipOwn
            && left.HasValue && left.Value < 0 && canSkip && stands && aliceAgain && opened && same;
    }

    // ------------------------------------------------------------------ a desync, and the host putting it right

    static bool MpDesync(ScenarioFile.Loaded l, out string detail) {
        List<string> r = new List<string>();
        long blueId = l.ai.id, redId = l.other.id;
        string code = MpStartGame(l.ai, l.other, false, 0);
        PlayAs(Side(blueId));

        Device("bob", "Bob");
        MpOpen(code);
        // Something only bob's device has (a version difference, a tampered board): his tank is stronger.
        Unit tank = Side(redId).units.Find(u => u != null && u.tile != null && u.name.StartsWith("Tank"));
        tank.Strength = Math.Min(tank.Strength + 3, 10) == tank.Strength ? tank.Strength - 3 : Math.Min(tank.Strength + 3, 10);
        PlayAs(Side(redId));

        Device("alice", "Alice");
        MpOpen(code);
        bool diverged = MpSession.Diverged;
        MpGame g1 = Wait(Mp.Load(code));
        bool reported = g1.desync.Length > 0 && g1.desyncBy == "Alice";
        bool nobody = !g1.TurnOf("alice") && !g1.TurnOf("bob") && !MpSession.MyTurn;
        r.Add("alice plays bob's turn back: desync " + diverged + ", reported " + reported + " (\"" + g1.desync + "\"); nobody can act " + nobody);

        // Alice's board stopped where bob's turn stopped playing back: it goes on from there, Red still to move.
        MpResult go = Wait(MpSession.HostContinue());
        MpGame g2 = Wait(Mp.Load(code));
        bool cleared = go.ok && g2.desync.Length == 0 && g2.baseEntry > 0 && !MpSession.Diverged && g2.turnSide == "Red" && g2.turnDriver == "bob";
        string hostBoard = GameLog.BoardCheck();
        r.Add("host goes on from her board " + go.ok + ": cleared, base at entry " + g2.baseEntry + ", " + g2.turnSide + " to move by " + g2.turnDriver + " " + cleared);

        Device("bob", "Bob");
        MpOpen(code);
        bool bobSame = GameLog.BoardCheck() == hostBoard, bobTurn = MpSession.MyTurn;
        if (!bobSame) r.Add("bob's board after the host's: " + MpSession.Difference(GameLog.BoardCheck(), hostBoard));
        if (!bobTurn) r.Add("not bob's turn: " + MpSession.State + ", " + Rig.gc.activePlayer.personal_name + " to move, driver " + MpSession.DriverOf(Rig.gc.activePlayer));
        bool bobLoaded = bobSame && !MpSession.Diverged && bobTurn;
        int desyncsBefore = Replay.Desyncs.Count;
        PlayAs(Side(redId));
        MpSession.Tick();
        string after = GameLog.BoardCheck();
        Device("alice", "Alice");
        MpOpen(code);
        bool inStep = GameLog.BoardCheck() == after && !MpSession.Diverged && MpSession.MyTurn && Replay.Desyncs.Count == desyncsBefore;
        r.Add("bob loads the host's board and finishes his turn " + bobLoaded + "; it plays back in step on alice's " + inStep);

        MpResult end = Wait(MpSession.HostTerminate());
        Device("bob", "Bob");
        MpOpen(code);
        bool over = MpSession.State == MpSession.Phase.Ended && Rig.gc.gameOver && Wait(Mp.Load(code)).state == MpState.Terminated;
        r.Add("host ended it " + end.ok + ": over on bob's device " + over);
        detail = string.Join("; ", r.ToArray());
        return diverged && reported && nobody && cleared && bobLoaded && inStep && end.ok && over;
    }

    // ------------------------------------------------------------------ removing players, handing sides on

    static bool MpKick(ScenarioFile.Loaded l, out string detail) {
        List<string> r = new List<string>();
        long blueId = l.ai.id, redId = l.other.id;
        string code = MpStartGame(l.ai, l.other, false, 0);
        PlayAs(Side(blueId));
        MpResult k = Wait(MpSession.HostKick("bob"));
        MpGame g1 = Wait(Mp.Load(code));
        bool waits = k.ok && g1.Slot(redId).kind == MpSlotKind.Open && g1.turnDriver == "" && g1.banned.Contains("bob");
        Device("bob", "Bob");
        bool bobIn = MpOpen(code);
        string bobNotice = MpSession.Notice;
        r.Add("bob removed on Red's turn: Red waits for the host " + waits + "; bob opens it " + bobIn + " (" + bobNotice + ")");

        Device("alice", "Alice");
        MpOpen(code);
        int desyncsBefore = Replay.Desyncs.Count;
        MpResult ai = Wait(MpSession.HostHandOver(redId, MpSlotKind.AI, "", ""));
        bool hers = ai.ok && MpSession.MyTurn && Rig.gc.activePlayer.id == redId;
        ScenarioFile.PlayTurn();
        r.Add("Red to the AI: alice's device plays it " + hers + ", then " + Rig.gc.activePlayer.personal_name + " to move");

        Device("carol", "Carol");
        bool carolIn = Wait(Mp.Join(code, false)).ok;
        Device("alice", "Alice");
        MpOpen(code);
        MpResult toCarol = Wait(MpSession.HostHandOver(redId, MpSlotKind.Player, "carol", ""));
        PlayAs(Side(blueId));
        MpGame g2 = Wait(Mp.Load(code));
        Device("carol", "Carol");
        bool carolOpened = MpOpen(code);
        bool carolTurn = MpSession.MyTurn && g2.turnDriver == "carol" && Rig.gc.activePlayer.id == redId;
        PlayAs(Side(redId));
        r.Add("carol joined " + carolIn + ", given Red " + toCarol.ok + ": her turn on her device " + (carolOpened && carolTurn));

        Device("alice", "Alice");
        MpOpen(code);
        MpResult out1 = Wait(MpSession.HostEliminate(redId));
        MpGame g3 = Wait(Mp.Load(code));
        bool aliceWon = Rig.gc.gameOver && g3.state == MpState.Over && g3.winner.Length > 0;
        Device("carol", "Carol");
        MpOpen(code);
        bool carolSees = MpSession.State == MpSession.Phase.Ended && Rig.gc.gameOver && !MpSession.Diverged;
        Wait(Mp.Refresh());
        MpSummary listed = Mp.Games.Find(s => s.code == code);
        bool stillListed = listed != null && listed.state == MpState.Over;
        Mp.Forget(code).Wait();
        Wait(Mp.Refresh());
        bool gone = !Mp.Games.Exists(s => s.code == code);
        int desyncs = Replay.Desyncs.Count - desyncsBefore;
        r.Add("Red taken out " + out1.ok + ": game over (" + g3.winner + " wins) " + aliceWon + ", over on carol's device " + carolSees + ", in her list until done " + stillListed + ", then gone " + gone + "; desyncs " + desyncs);
        detail = string.Join("; ", r.ToArray());
        return waits && !bobIn && bobNotice.Contains("removed") && hers && carolIn && toCarol.ok && carolOpened && carolTurn && out1.ok && aliceWon && carolSees && stillListed && gone && desyncs == 0;
    }
}
