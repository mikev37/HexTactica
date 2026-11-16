using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Defective.JSON;
using UnityEngine;

namespace HexTactica.Multiplayer {

    /// <summary>What an operation on a networked game came to.</summary>
    public class MpResult {
        public bool ok;
        /// <summary>The log had moved on (someone else wrote first): nothing was written.</summary>
        public bool conflict;
        public string error;
        /// <summary>The game as the store has it after the operation.</summary>
        public MpGame game;
        public static MpResult Fail(string why) { return new MpResult { error = why }; }
    }

    /// <summary>
    /// Networked games, as far as they can be dealt with without a board: this device's player, hosting and joining,
    /// the lobby (sides, members, rules), a game's log (adding to it, reading it), and the list of the player's games.
    /// Every device works these out for itself on the values the store keeps (IMpStore): a change is read, made and
    /// written back, and tried again if someone else wrote meanwhile. Nothing here is specific to one backend.
    /// What happens on the board is MpSession's.
    /// </summary>
    public static class Mp {

        public const string KeyMeta = "meta";
        /// <summary>
        /// Where a game's own data (its scenario, base board, log) is kept: apart from its meta (kept under the join code),
        /// one place per game played under the code, so a code used again starts empty and a backend's per-item limits
        /// (UGS: 2000 keys, 5 MiB) apply to one game only.
        /// </summary>
        public static string DataId(MpGame g) { return g.code + "-" + g.epoch; }
        /// <summary>The scenario file the lobby shows (per epoch).</summary>
        public static string KeyScenario(MpGame g) { return "scenario-" + g.epoch; }
        /// <summary>The board the game is played on from (per epoch): the start, or the host's after a desync. First line: the entry count it stands at.</summary>
        public static string KeyBase(MpGame g) { return "base-" + g.epoch; }
        static string KeyLog(string epoch, int chunk) { return "log-" + epoch + "-" + chunk.ToString("0000"); }

        /// <summary>The backend in use. Set before anything else to use another (the tests do); else made from the settings (Ready).</summary>
        public static IMpStore Store;

        public static string Me { get { return Store != null ? Store.PlayerId ?? "" : ""; } }

        const string NameKey = "mp-name", IdKey = "mp-player-id";
        /// <summary>The name other players see: for show only, the player's to change at any time (the id is what counts).</summary>
        public static string PlayerName {
            get { string n = PlayerPrefs.GetString(NameKey, ""); return string.IsNullOrEmpty(n) ? "Player" : n; }
            set { PlayerPrefs.SetString(NameKey, (value ?? "").Trim()); PlayerPrefs.Save(); }
        }

        /// <summary>The player's games as last listed (Refresh), and that the list was read again.</summary>
        public static readonly List<MpSummary> Games = new List<MpSummary>();
        public static event Action Listed;
        /// <summary>The last thing that went wrong talking to the store ("" nothing): for the screens to show.</summary>
        public static string LastError = "";

        static bool signedIn;

        /// <summary>The store made (from the settings, unless one was set) and signed in. Null when ready, else what went wrong.</summary>
        public static async Task<string> Ready() {
            if (Store == null) Store = MakeStore();
            if (Store == null) return LastError = "No multiplayer backend is set up (Multiplayer Settings).";
            if (signedIn) return null;
            string failed = await Store.SignIn();
            if (failed != null) return LastError = failed;
            signedIn = true;
            return null;
        }

        /// <summary>Forget the store (the settings changed, or a test is over).</summary>
        public static void Reset() { Store = null; signedIn = false; Games.Clear(); LastError = ""; }

        static IMpStore MakeStore() {
            MultiplayerSettings s = MultiplayerSettings.Get;
            if (s.backend == MultiplayerSettings.Backend.UnityGamingServices) {
#if HEXTACTICA_UGS
                return new MpUgsStore(s.ugsModule);
#else
                Debug.LogError("Multiplayer Settings ask for Unity Gaming Services, but the HEXTACTICA_UGS scripting define is not set (see Assets/Multiplayer/README.md).");
                return null;
#endif
            }
            string id = PlayerPrefs.GetString(IdKey, "");
            if (string.IsNullOrEmpty(id)) { id = Guid.NewGuid().ToString("N").Substring(0, 12); PlayerPrefs.SetString(IdKey, id); PlayerPrefs.Save(); }
            string folder = string.IsNullOrEmpty(s.sharedFolder) ? Path.Combine(Application.persistentDataPath, "multiplayer-shared") : s.sharedFolder;
            MpFolderStore store = new MpFolderStore(folder, id);
            store.PlayerChanged += p => { PlayerPrefs.SetString(IdKey, p); PlayerPrefs.Save(); };
            return store;
        }

        // ------------------------------------------------------------------ linking devices

        /// <summary>The backend can make one player of two devices (IMpLinking).</summary>
        public static bool CanLink { get { if (Store == null) Store = MakeStore(); return Store is IMpLinking; } }

        /// <summary>On the new device: a code to type on the other one; null (LastError says why) if it cannot.</summary>
        public static async Task<string> StartLink() {
            if (await Ready() != null) return null;
            IMpLinking l = Store as IMpLinking;
            if (l == null) { LastError = "This backend cannot link devices."; return null; }
            string code = await l.StartLink();
            if (code == null) LastError = l.LinkError ?? "Could not make a code.";
            return code;
        }

        /// <summary>On the device already playing: confirm the code the new device shows. Null when done, else what went wrong.</summary>
        public static async Task<string> ConfirmLink(string code) {
            string notReady = await Ready();
            if (notReady != null) return notReady;
            IMpLinking l = Store as IMpLinking;
            return l == null ? "This backend cannot link devices." : await l.ConfirmLink(code);
        }

        /// <summary>On the new device, once confirmed: this device plays as the other one's player from now on, with all their games.</summary>
        public static async Task<string> FinishLink() {
            IMpLinking l = Store as IMpLinking;
            if (l == null) return "This backend cannot link devices.";
            string failed = await l.FinishLink();
            if (failed != null) return failed;
            signedIn = true;
            Games.Clear();
            await Refresh();
            return null;
        }

        // ------------------------------------------------------------------ reading and changing a game

        class Read { public MpGame game; public string writeLock; public bool failed; }

        static async Task<Read> ReadMeta(string code) {
            Dictionary<string, MpValue> got = await Store.Read(code, new[] { KeyMeta });
            if (got == null) return new Read { failed = true };
            MpValue v;
            if (!got.TryGetValue(KeyMeta, out v)) return new Read();
            return new Read { game = MpGame.FromJson(v.value), writeLock = v.writeLock };
        }

        /// <summary>The game under <paramref name="code"/> as the store has it; null if there is none (or the store could not be read: LastError).</summary>
        public static async Task<MpGame> Load(string code) {
            if (await Ready() != null) return null;
            Read r = await ReadMeta(Normal(code));
            if (r.failed) LastError = "Could not reach the game store.";
            return r.game;
        }

        /// <summary>A join code as typed, made the stored form: upper case, no spaces, no "name#" before it.</summary>
        public static string Normal(string code) {
            code = (code ?? "").Trim();
            int hash = code.LastIndexOf('#');
            if (hash >= 0) code = code.Substring(hash + 1);
            return code.Replace(" ", "").ToUpperInvariant();
        }

        /// <summary>
        /// Change the game: read it, let <paramref name="change"/> alter it (returning null, or why it cannot be done),
        /// write it back; if someone else wrote meanwhile, again from the start.
        /// </summary>
        public static async Task<MpResult> Change(string code, Func<MpGame, string> change) {
            string notReady = await Ready();
            if (notReady != null) return MpResult.Fail(notReady);
            for (int attempt = 0; attempt < 8; attempt++) {
                Read r = await ReadMeta(code);
                if (r.failed) return MpResult.Fail(LastError = "Could not reach the game store.");
                if (r.game == null) return MpResult.Fail("There is no game " + code + ".");
                string why = change(r.game);
                if (why != null) return new MpResult { error = why, game = r.game };
                MpWritten w = await Store.Write(code, KeyMeta, r.game.ToJson(), r.writeLock);
                if (w.ok) return new MpResult { ok = true, game = r.game };
                if (!w.conflict) return MpResult.Fail(LastError = w.error ?? "Could not write to the game store.");
            }
            return MpResult.Fail("The game kept changing: try again.");
        }

        static string HostOnly(MpGame g) { return g.host == Me ? null : "Only the host can do that."; }

        // ------------------------------------------------------------------ hosting and joining

        static readonly System.Random random = new System.Random(Guid.NewGuid().GetHashCode());

        static string NewCode(int length, string alphabet) {
            char[] c = new char[Mathf.Max(1, length)];
            for (int i = 0; i < c.Length; i++) c[i] = alphabet[random.Next(alphabet.Length)];
            return new string(c);
        }

        /// <summary>
        /// A new game in the store, in its lobby, hosted by this player: a join code no game under way is using (one
        /// whose game is over is used again), the scenario's text for the others to look at, and the host as its first
        /// member. Its sides are filled in by the lobby once the scenario's board is up (SetSides).
        /// </summary>
        public static async Task<MpResult> Host(string name, string scenarioText) {
            string notReady = await Ready();
            if (notReady != null) return MpResult.Fail(notReady);
            MultiplayerSettings s = MultiplayerSettings.Get;
            for (int attempt = 0; attempt < 40; attempt++) {
                string code = NewCode(s.codeLength, string.IsNullOrEmpty(s.codeAlphabet) ? "ABCDEFGHJKLMNPQRSTUVWXYZ23456789" : s.codeAlphabet);
                Read was = await ReadMeta(code);
                if (was.failed) return MpResult.Fail(LastError = "Could not reach the game store.");
                if (was.game != null && !was.game.Finished) continue;   // in use
                MpGame g = new MpGame {
                    code = code, epoch = Guid.NewGuid().ToString("N").Substring(0, 8), name = string.IsNullOrEmpty(name) ? "Game" : name,
                    host = Me, hostName = PlayerName, appVersion = Application.version, state = MpState.Lobby
                };
                g.members.Add(new MpMember { id = Me, name = PlayerName });
                MpWritten sc = await Store.Write(DataId(g), KeyScenario(g), scenarioText ?? "", null);
                if (!sc.ok) return MpResult.Fail(LastError = sc.error ?? "Could not write to the game store.");
                MpWritten w = await Store.Write(code, KeyMeta, g.ToJson(), was.game != null ? was.writeLock : null);
                if (w.conflict) continue;   // someone took the code this instant
                if (!w.ok) return MpResult.Fail(LastError = w.error ?? "Could not write to the game store.");
                await Remember(code);
                return new MpResult { ok = true, game = g };
            }
            return MpResult.Fail("No free join code was found: try again.");
        }

        /// <summary>The scenario the lobby of <paramref name="g"/> shows, as text; null if it could not be read.</summary>
        public static async Task<string> Scenario(MpGame g) {
            Dictionary<string, MpValue> got = await Store.Read(DataId(g), new[] { KeyScenario(g) });
            MpValue v;
            return got != null && got.TryGetValue(KeyScenario(g), out v) ? v.value : null;
        }

        /// <summary>
        /// Join the game under <paramref name="code"/>, to play (a member who can claim an open side) or only to watch.
        /// Refused for a player the host removed, and for a device on another version of the game.
        /// </summary>
        public static async Task<MpResult> Join(string code, bool spectator) {
            code = Normal(code);
            if (code.Length == 0) return MpResult.Fail("Type the game's join code.");
            MpResult r = await Change(code, g => {
                if (g.banned.Contains(Me)) return "The host removed you from this game.";
                if (g.appVersion != Application.version) return "This game is played on version " + g.appVersion + " of HexTactica; this is version " + Application.version + ".";
                if (g.Finished) return "That game is over.";
                MpMember m = g.Member(Me);
                if (m == null) g.members.Add(m = new MpMember { id = Me, spectator = spectator });
                else if (!g.IsPlayer(Me)) m.spectator = spectator;
                m.name = PlayerName;
                foreach (MpSlot s in g.slots) if (s.kind == MpSlotKind.Player && s.player == Me) s.playerName = PlayerName;
                return null;
            });
            if (r.ok) await Remember(code);
            return r;
        }

        // ------------------------------------------------------------------ the lobby

        /// <summary>The host's lobby gives the game its sides (from the scenario's board), once.</summary>
        public static Task<MpResult> SetSides(string code, List<MpSlot> slots) {
            return Change(code, g => {
                string no = HostOnly(g); if (no != null) return no;
                if (g.state != MpState.Lobby) return "The game has started.";
                g.slots = slots;
                return null;
            });
        }

        /// <summary>A joined player takes an open side.</summary>
        public static Task<MpResult> Claim(string code, long side) {
            return Change(code, g => {
                MpSlot s = g.Slot(side);
                if (s == null) return "No such side.";
                if (g.Member(Me) == null) return "You are not in this game.";
                if (g.state != MpState.Lobby) return "The game has started.";
                if (s.kind != MpSlotKind.Open) return s.sideName + " is taken.";
                s.kind = MpSlotKind.Player; s.player = Me; s.playerName = PlayerName;
                g.Member(Me).spectator = false;
                return null;
            });
        }

        /// <summary>A player gives a side they hold back (it is open again).</summary>
        public static Task<MpResult> Release(string code, long side) {
            return Change(code, g => {
                MpSlot s = g.Slot(side);
                if (s == null || s.kind != MpSlotKind.Player || s.player != Me) return "That side is not yours.";
                if (g.state != MpState.Lobby) return "The game has started.";
                s.kind = MpSlotKind.Open; s.player = ""; s.playerName = "";
                return null;
            });
        }

        /// <summary>The host changes the lobby (a side's kind, profile or faction, the rules, the name, the turn limit): anything <paramref name="change"/> does.</summary>
        public static Task<MpResult> HostChange(string code, Action<MpGame> change) {
            return Change(code, g => {
                string no = HostOnly(g); if (no != null) return no;
                if (g.state != MpState.Lobby) return "The game has started.";
                change(g);
                return null;
            });
        }

        /// <summary>A side's faction in the lobby: the host's to set for any side, a player's for a side they hold (not one the map fixes).</summary>
        public static Task<MpResult> SetFaction(string code, long side, string faction) {
            return Change(code, g => {
                MpSlot s = g.Slot(side);
                if (s == null) return "No such side.";
                if (g.state != MpState.Lobby) return "The game has started.";
                if (s.factionLocked) return "The map fixes " + s.sideName + "'s faction.";
                if (g.host != Me && !(s.kind == MpSlotKind.Player && s.player == Me)) return "That side is not yours.";
                s.faction = faction ?? "";
                return null;
            });
        }

        /// <summary>The host sets who plays a side in the lobby: open to join, the AI (with a profile), or the host's own device ("Local").</summary>
        public static Task<MpResult> HostSetSlot(string code, long side, MpSlotKind kind, string profile) {
            return HostChange(code, g => {
                MpSlot s = g.Slot(side);
                if (s == null) return;
                s.kind = kind; s.profile = kind == MpSlotKind.AI ? profile ?? "" : "";
                s.player = kind == MpSlotKind.Player ? g.host : ""; s.playerName = kind == MpSlotKind.Player ? PlayerName : "";
            });
        }

        /// <summary>
        /// The host removes a player: out of the members, never to come back (banned). In the lobby their sides are
        /// open again; in a game under way they are left open for the host to hand on (MpSession: to a player, the AI,
        /// or out of the game).
        /// </summary>
        public static Task<MpResult> Kick(string code, string player) {
            return Change(code, g => {
                string no = HostOnly(g); if (no != null) return no;
                if (player == g.host) return "The host cannot be removed.";
                Vacate(g, player);
                if (!g.banned.Contains(player)) g.banned.Add(player);
                return null;
            });
        }

        static void Vacate(MpGame g, string player) {
            g.members.RemoveAll(m => m.id == player);
            foreach (MpSlot s in g.slots) if (s.kind == MpSlotKind.Player && s.player == player) { s.kind = MpSlotKind.Open; s.player = ""; s.playerName = ""; }
            if (g.turnDriver == player) g.turnDriver = "";
        }

        /// <summary>
        /// Leave the game, and take it off the player's list. A guest's sides are left open (in a game under way the
        /// host decides what becomes of them). The host leaving ends the game for everyone.
        /// </summary>
        public static async Task<MpResult> Leave(string code) {
            MpResult r = await Change(code, g => {
                if (g.host == Me) { if (!g.Finished) g.state = MpState.Terminated; return null; }
                Vacate(g, Me);
                return null;
            });
            await Forget(code);
            return r;
        }

        /// <summary>
        /// The host starts the game: the board it is played on from goes to the store (the scenario with the lobby's
        /// choices, as the host's device loaded it), then the game is under way, <paramref name="turnSide"/> to move and
        /// <paramref name="turnDriver"/> to act for it.
        /// </summary>
        public static async Task<MpResult> Start(string code, string board, string turnSide, string turnDriver) {
            MpGame g = await Load(code);
            if (g == null) return MpResult.Fail("There is no game " + code + ".");
            if (g.host != Me) return MpResult.Fail("Only the host can start the game.");
            if (g.slots.Exists(s => s.kind == MpSlotKind.Open)) return MpResult.Fail("Every side needs a player or the AI before the game starts.");
            MpResult wrote = await WriteBase(g, 0, board);
            if (!wrote.ok) return wrote;
            return await Change(code, x => {
                if (x.state != MpState.Lobby) return "The game has started.";
                if (x.slots.Exists(s => s.kind == MpSlotKind.Open)) return "Every side needs a player or the AI before the game starts.";
                x.state = MpState.Playing; x.entries = 0; x.baseEntry = 0;
                x.turnSide = turnSide ?? ""; x.turnDriver = turnDriver ?? ""; x.clockStarted = ""; x.clockEntry = -1;
                return null;
            });
        }

        // ------------------------------------------------------------------ the base board

        static async Task<MpResult> WriteBase(MpGame g, int entry, string board) {
            string key = KeyBase(g);
            for (int attempt = 0; attempt < 4; attempt++) {
                Dictionary<string, MpValue> got = await Store.Read(DataId(g), new[] { key });
                if (got == null) return MpResult.Fail(LastError = "Could not reach the game store.");
                MpValue was; got.TryGetValue(key, out was);
                MpWritten w = await Store.Write(DataId(g), key, entry + "\n" + board, was != null ? was.writeLock : null);
                if (w.ok) return new MpResult { ok = true, game = g };
                if (!w.conflict) return MpResult.Fail(LastError = w.error ?? "Could not write to the game store.");
            }
            return MpResult.Fail("The game kept changing: try again.");
        }

        /// <summary>The board the game is played on from, and the entry count it stands at (-1 and null if it could not be read).</summary>
        public static async Task<KeyValuePair<int, string>> Base(MpGame g) {
            Dictionary<string, MpValue> got = await Store.Read(DataId(g), new[] { KeyBase(g) });
            MpValue v;
            if (got == null || !got.TryGetValue(KeyBase(g), out v) || v.value == null) return new KeyValuePair<int, string>(-1, null);
            int nl = v.value.IndexOf('\n'), entry;
            if (nl < 0 || !int.TryParse(v.value.Substring(0, nl), out entry)) return new KeyValuePair<int, string>(-1, null);
            return new KeyValuePair<int, string>(entry, v.value.Substring(nl + 1));
        }

        // ------------------------------------------------------------------ the log

        static int Chunk { get { return Mathf.Max(1, MultiplayerSettings.Get.logChunk); } }

        /// <summary>The log's entries from number <paramref name="from"/> on, in order. Null if the store could not be read.</summary>
        public static async Task<List<MpEntry>> Fetch(MpGame g, int from) {
            List<MpEntry> found = new List<MpEntry>();
            int size = Chunk, c = Mathf.Max(0, from) / size;
            while (true) {
                List<string> keys = new List<string>();
                for (int i = 0; i < 6; i++) keys.Add(KeyLog(g.epoch, c + i));
                Dictionary<string, MpValue> got = await Store.Read(DataId(g), keys);
                if (got == null) { LastError = "Could not reach the game store."; return null; }
                bool more = true;
                for (int i = 0; i < keys.Count && more; i++) {
                    MpValue v;
                    if (!got.TryGetValue(keys[i], out v)) { more = false; break; }
                    List<MpEntry> chunk = MpEntry.ChunkFromJson(v.value);
                    foreach (MpEntry e in chunk) if (e.n >= from) found.Add(e);
                    if (chunk.Count < size) more = false;
                }
                if (!more) break;
                c += keys.Count;
            }
            return found;
        }

        /// <summary>
        /// Add an entry to the log, as number <paramref name="expected"/>: refused (conflict) if the log already has
        /// that many or more, which means another device wrote first and this one must catch up before it writes. Then
        /// the game's meta is brought up to date (the entry count, and whatever <paramref name="then"/> changes: whose
        /// turn it is now, the game's end).
        /// </summary>
        public static async Task<MpResult> Append(MpGame g, int expected, string kind, List<string> lines, Action<MpGame> then) {
            string notReady = await Ready();
            if (notReady != null) return MpResult.Fail(notReady);
            int size = Chunk, c = expected / size;
            string key = KeyLog(g.epoch, c), next = KeyLog(g.epoch, c + 1);
            Dictionary<string, MpValue> got = await Store.Read(DataId(g), new[] { key, next });
            if (got == null) return MpResult.Fail(LastError = "Could not reach the game store.");
            if (got.ContainsKey(next)) return new MpResult { conflict = true };
            MpValue was; got.TryGetValue(key, out was);
            List<MpEntry> chunk = was != null ? MpEntry.ChunkFromJson(was.value) : new List<MpEntry>();
            if (c * size + chunk.Count != expected) return new MpResult { conflict = true };
            chunk.Add(new MpEntry { n = expected, by = Me, kind = kind, at = MpJson.Now(), lines = lines ?? new List<string>() });
            MpWritten w = await Store.Write(DataId(g), key, MpEntry.ChunkToJson(chunk), was != null ? was.writeLock : null);
            if (w.conflict) return new MpResult { conflict = true };
            if (!w.ok) return MpResult.Fail(LastError = w.error ?? "Could not write to the game store.");
            MpResult meta = await Change(g.code, x => {
                if (x.epoch != g.epoch) return "The game is gone.";
                if (x.entries < expected + 1) x.entries = expected + 1;
                if (then != null) then(x);
                return null;
            });
            // (The entry is in the log whatever became of the meta: the next writer puts the count right.)
            return new MpResult { ok = true, game = meta.game ?? g, error = meta.ok ? null : meta.error };
        }

        // ------------------------------------------------------------------ the player's games

        static List<string> ParseMine(string text) {
            List<string> codes = new List<string>();
            if (string.IsNullOrEmpty(text)) return codes;
            JSONObject a = new JSONObject(text);
            if (a.list != null) foreach (JSONObject c in a.list) if (!string.IsNullOrEmpty(c.stringValue) && !codes.Contains(c.stringValue)) codes.Add(c.stringValue);
            return codes;
        }

        static async Task<bool> EditMine(Action<List<string>> edit) {
            string text = await Store.ReadMine();
            if (text == null) return false;
            List<string> codes = ParseMine(text);
            edit(codes);
            JSONObject a = new JSONObject(JSONObject.Type.Array);
            foreach (string c in codes) a.Add(c);
            return await Store.WriteMine(a.Print());
        }

        static Task<bool> Remember(string code) { return EditMine(l => { if (!l.Contains(code)) l.Add(code); }); }

        /// <summary>Take a game off the player's list (it is over and looked at, or they left it).</summary>
        public static async Task Forget(string code) {
            if (await Ready() != null) return;
            await EditMine(l => l.Remove(code));
            Games.RemoveAll(s => s.code == code);
            if (Listed != null) Listed();
        }

        /// <summary>Read the player's games again (Games), and say so (Listed). False if the store could not be read.</summary>
        public static async Task<bool> Refresh() {
            if (await Ready() != null) return false;
            string text = await Store.ReadMine();
            if (text == null) { LastError = "Could not reach the game store."; return false; }
            List<MpSummary> list = new List<MpSummary>();
            foreach (string code in ParseMine(text)) {
                Read r = await ReadMeta(code);
                if (r.failed) { LastError = "Could not reach the game store."; return false; }
                MpGame g = r.game;
                if (g == null) { list.Add(new MpSummary { code = code, title = "#" + code, turnSide = "", removed = true }); continue; }
                MpMember m = g.Member(Me);
                list.Add(new MpSummary {
                    code = code, title = g.Title, state = g.state, turnSide = g.turnSide, host = g.host == Me, entries = g.entries, epoch = g.epoch,
                    spectator = m != null && m.spectator, removed = m == null, myTurn = m != null && g.TurnOf(Me)
                });
            }
            Games.Clear(); Games.AddRange(list);
            LastError = "";
            if (Listed != null) Listed();
            return true;
        }
    }
}
