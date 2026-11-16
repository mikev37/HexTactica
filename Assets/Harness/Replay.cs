using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HexTactica.Harness {

    /// <summary>
    /// Watching a recorded game again (GameLog), a turn at a time — and within a turn an action at a time
    /// (NextAction): each of the turn's logged orders (moves, attacks, strikes, boarding and getting off, purchases,
    /// resigning) is ordered again on the board and the game plays it by its own rules (GameLog.Lines reads the lines
    /// it wrote). Nothing is set to the log's result: what came of each order is checked against the log, and at each
    /// turn's start the board is checked against that turn's recorded board; every difference is a desync (Desyncs,
    /// and an error in the console). Boards are loaded to start, to jump to a turn (Show, Previous, Next), and, as an
    /// error correction only, after a desync (CorrectDesyncs, Corrections): never as the normal way from turn to turn.
    ///
    /// A recording folder holds the board at the start of every turn (turn-NNN-Side.dat) and the
    /// game's log (game.log): what each side did that turn, and the AI's reasoning. A replay loads
    /// those boards in order — nobody plays: every AI is switched off and the board ignores clicks
    /// and the space bar — and shows beside the board what happened in the turn on screen (ReplayPanel).
    ///
    /// It can be watched as either side sees it, through that side's fog of war (what the AI saw
    /// when it decided), or with everything shown. What a side remembered of units it saw earlier is
    /// not in the recording: the view is that side's sight on that board.
    ///
    /// Opening a replay stops any recording; nothing is recorded while it runs. HexTactica ▸ Game Log
    /// ▸ Replay a Recording... in the editor (play mode), or <see cref="Open"/>.
    /// </summary>
    public static class Replay {

        /// <summary>A replay is on the screen: the game's input is off.</summary>
        public static bool Active { get; private set; }
        public static string Folder { get; private set; }

        /// <summary>One turn of the recording: its board and what the log says happened in it.</summary>
        public class Turn {
            public string file;
            public int round;
            public string side;
            /// <summary>The log's lines for this turn after its board: moves, fights, the AI's reasoning.</summary>
            public readonly List<string> events = new List<string>();
            /// <summary>Which of the events are actions to play (indices into events), in order. Filled when the turn is shown.</summary>
            public readonly List<int> actions = new List<int>();
        }

        /// <summary>How many of the turn's actions have been played.</summary>
        public static int Step { get; private set; }
        /// <summary>The action just played, as the log has it ("" none yet this turn).</summary>
        public static string LastAction = "";
        /// <summary>The built-in panel (ReplayPanel, editor menu) shows with a replay; off when a scene has its own controls.</summary>
        public static bool BuiltInPanel = true;
        /// <summary>The turn on screen changed, or an action was played (for the controls).</summary>
        public static event Action Changed;

        static readonly Dictionary<long, HexTile> tileAt = new Dictionary<long, HexTile>();
        static readonly HashSet<int> played = new HashSet<int>();
        static readonly List<string> sides = new List<string>();
        /// <summary>The log's sides by name, with their ids (its "side:" lines).</summary>
        static readonly Dictionary<string, long> logSides = new Dictionary<string, long>();
        static readonly Dictionary<string, string> logFactions = new Dictionary<string, string>();

        public static readonly List<Turn> turns = new List<Turn>();
        public static int Index { get; private set; }
        public static Turn Current { get { return Index >= 0 && Index < turns.Count ? turns[Index] : null; } }

        /// <summary>Whose eyes: a player's index (their fog of war), or -1 for everything.</summary>
        public static int ViewAs { get; private set; }
        /// <summary>What the log says at the end (who won), if anything.</summary>
        public static string Ending;

        /// <summary>How a board is loaded: as the game loads a scenario file (the harness loads its own way).</summary>
        public static Func<string, bool> Loader = ScenarioLoader.LoadBoardFromPath;

        static bool fogWas, recordWas, shownWas = true;
        static int viewerWas = -1;

        /// <summary>
        /// The recorded game's fog rule: from its turn boards ("fog"), else the log's "fog:" line, else the scenario it was
        /// played from; null if none says (the board's is kept). Put in force on every board the replay loads.
        /// </summary>
        public static bool? RecordedFog;
        static bool? logFog;

        /// <summary>Read the recording in <paramref name="folder"/>: its boards in order, each with its part of the log. False if it has none.</summary>
        public static bool Read(string folder) {
            turns.Clear(); Ending = null; logSides.Clear(); logFactions.Clear(); logFog = null; RecordedFog = null; sourceFile = null;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
            List<string> files = new List<string>(Directory.GetFiles(folder, "turn-*.dat"));
            files.Sort(StringComparer.Ordinal);
            Dictionary<string, Turn> byFile = new Dictionary<string, Turn>();
            foreach (string f in files) {
                Turn t = new Turn { file = f, round = -1, side = SideOf(Path.GetFileName(f)) };
                turns.Add(t); byFile[Path.GetFileName(f)] = t;
            }
            if (turns.Count == 0) return false;
            string log = Path.Combine(folder, "game.log");
            if (File.Exists(log)) {
                Turn at = null; int round = -1; bool board = false;
                foreach (string raw in File.ReadAllLines(log)) {
                    string line = raw.TrimEnd();
                    long sideId; string sideName, sideFaction; bool sideAI;
                    if (round < 0 && GameLog.Lines.ReadSide(line, out sideId, out sideName, out sideFaction, out sideAI)) { logSides[sideName] = sideId; logFactions[sideName] = sideFaction; continue; }
                    bool fogLine;
                    if (round < 0 && GameLog.Lines.ReadFog(line, out fogLine)) { logFog = fogLine; continue; }
                    string fromFile;
                    if (round < 0 && GameLog.Lines.ReadSource(line, out fromFile)) { sourceFile = fromFile; continue; }
                    if (line.StartsWith("== round ", StringComparison.Ordinal)) {
                        at = null; board = true;
                        int.TryParse(line.Substring(9).Split(':')[0], out round);
                        continue;
                    }
                    int saved = line.IndexOf("(board saved as ", StringComparison.Ordinal);
                    if (saved >= 0) {
                        string name = line.Substring(saved + 16).TrimEnd(')');
                        if (byFile.TryGetValue(name, out at)) at.round = round;
                        board = false;
                        continue;
                    }
                    if (line.StartsWith("== game over", StringComparison.Ordinal)) { Ending = line.Trim('=', ' '); at = null; continue; }
                    if (board || at == null || line.Length == 0) continue;
                    at.events.Add(line);
                }
            }
            RecordedFog = FogOf(turns[0].file) ?? logFog ?? (sourceFile != null && File.Exists(sourceFile) ? FogOf(sourceFile) : null);
            return true;
        }

        static string sourceFile;

        /// <summary>A board file's fog rule ("fog": "on"/"off", in it or its test block), or null if it has none.</summary>
        static bool? FogOf(string file) {
            try {
                Defective.JSON.JSONObject obj = new Defective.JSON.JSONObject(File.ReadAllText(file));
                Defective.JSON.JSONObject test = obj.GetField("test");
                Defective.JSON.JSONObject f = test != null ? test.GetField("fog") : null;
                if (f == null) f = obj.GetField("fog");
                if (f == null || string.IsNullOrEmpty(f.stringValue)) return null;
                return GameController.ParseFog(f.stringValue);
            } catch (Exception) { return null; }
        }

        static string SideOf(string file) {
            // turn-NNN-Side.dat
            string s = Path.GetFileNameWithoutExtension(file);
            int dash = s.IndexOf('-', 5);
            return dash > 0 ? s.Substring(dash + 1) : s;
        }

        /// <summary>Start a replay of the recording in <paramref name="folder"/> at its first turn. False if there is nothing to replay.</summary>
        public static bool Open(string folder) {
            if (!Read(folder)) { Debug.LogError("Replay: no recorded turns in " + folder); return false; }
            GameController gc = GameController.gc;
            GameLog.Stop();   // nothing is recorded while a recording is watched
            if (!Active) {
                recordWas = ScenarioLoader.RecordGames;
                fogWas = gc != null && gc.fogOfWar;
                viewerWas = gc != null ? gc.fogViewer : -1;
                shownWas = gc == null || gc.fogShownOnScreen;
            }
            Folder = folder;
            Active = true;
            ViewAs = -1;
            Index = -1;
            Desyncs.Clear();
            Corrections = 0;
            Show(0);
            if (BuiltInPanel) ReplayPanel.Ensure();
            return true;
        }

        /// <summary>Show turn <paramref name="i"/> (clamped to the recording).</summary>
        public static void Show(int i) {
            if (!Active || turns.Count == 0) return;
            i = Mathf.Clamp(i, 0, turns.Count - 1);
            ScenarioLoader.RecordGames = false;
            bool exactWas = ScenarioLoader.SidesAsFiled;
            ScenarioLoader.SidesAsFiled = true;   // each side's faction exactly as recorded (none means none)
            try {
                // The board's sides are made the file's before it is loaded (units are placed as their side's faction
                // fields them), and checked after: a side that does not come out as recorded is said so, set, and the
                // board loaded once more.
                SidesFrom(turns[i].file, true);
                bool loaded = false;
                try { loaded = Loader(turns[i].file); }
                catch (Exception e) { Debug.LogError("Replay: loading " + turns[i].file + " failed: " + e); }
                if (!loaded) Debug.LogError("Replay: could not load " + turns[i].file + " (the replay goes on with the board as it is).");
                if (SidesFrom(turns[i].file, false)) {
                    Loader(turns[i].file);
                    if (SidesFrom(turns[i].file, false)) Debug.LogError("Replay: the sides still do not come out as recorded after loading " + turns[i].file + " twice (see the warnings above).");
                }
            }
            finally { ScenarioLoader.RecordGames = recordWas; ScenarioLoader.SidesAsFiled = exactWas; }
            if (RecordedFog.HasValue && GameController.gc != null) GameController.gc.fogOfWar = RecordedFog.Value;
            Halted = false;
            Index = i;
            Freeze();
            ApplyView();
            Prepare(turns[i]);
            if (Changed != null) Changed();
        }

        /// <summary>
        /// Make each of the board's sides what the turn file records for it (name, colour, faction), by id. Returns
        /// true if any side was not that already; <paramref name="quiet"/> off, each difference is logged.
        /// </summary>
        static bool SidesFrom(string file, bool quiet) {
            GameController gc = GameController.gc;
            if (gc == null) return false;
            bool changed = false;
            try {
                Defective.JSON.JSONObject obj = new Defective.JSON.JSONObject(File.ReadAllText(file));
                Defective.JSON.JSONObject players = obj.GetField("players");
                if (players == null || players.list == null) return SidesFromLog();
                foreach (Defective.JSON.JSONObject pj in players.list) {
                    PlayerData pd = JsonUtility.FromJson<PlayerData>(pj.Print());
                    if (string.IsNullOrEmpty(pd.personal_name)) continue;
                    Player p = null;
                    foreach (Player x in gc.players) if (x != null && x.id == pd.id) p = x;
                    if (p == null) { if (!quiet) Debug.LogWarning("Replay: the board has no side with id " + pd.id + " (" + pd.personal_name + ") after loading " + Path.GetFileName(file) + "."); continue; }
                    Faction f = string.IsNullOrEmpty(pd.factionName) ? null : LevelEditor.FindFaction(pd.factionName);
                    if (!string.IsNullOrEmpty(pd.factionName) && f == null && !quiet) Debug.LogWarning("Replay: no faction named " + pd.factionName + " (side " + pd.personal_name + ").");
                    bool same = p.personal_name == pd.personal_name && p.faction == f && p.displayColor == pd.displayColor;
                    if (same) continue;
                    changed = true;
                    if (!quiet) Debug.LogWarning("Replay: side " + pd.id + " came out as " + p.personal_name + " / " + (p.faction != null ? p.faction.name : "no faction") + " / " + p.displayColor
                        + "; " + Path.GetFileName(file) + " records " + pd.personal_name + " / " + (f != null ? f.name : "no faction") + " / " + pd.displayColor + ". Set to the file's.");
                    p.personal_name = pd.personal_name; p.faction = f; p.displayColor = pd.displayColor;
                }
            } catch (Exception e) {
                if (!quiet) Debug.LogWarning("Replay: could not read the sides in " + file + ": " + e.Message);
            }
            return changed;
        }

        /// <summary>
        /// A recording made before its turn boards kept the sides: each side's name and faction from the log's "side:"
        /// lines (its colour is not in such a recording: the board's stays). True if any side was changed.
        /// </summary>
        static bool SidesFromLog() {
            GameController gc = GameController.gc;
            bool changed = false;
            foreach (KeyValuePair<string, long> kv in logSides) {
                Player p = null;
                foreach (Player x in gc.players) if (x != null && x.id == kv.Value) p = x;
                if (p == null) continue;
                string fn; logFactions.TryGetValue(kv.Key, out fn);
                Faction f = string.IsNullOrEmpty(fn) ? null : LevelEditor.FindFaction(fn);
                if (p.personal_name == kv.Key && p.faction == f) continue;
                p.personal_name = kv.Key; p.faction = f;
                changed = true;
            }
            return changed;
        }

        // ---------------------------------------------------------------- turn to turn, as the game plays it

        /// <summary>
        /// Everything the replay played that did not come out as the log says (a move that ended elsewhere, an attack
        /// with other losses, a board at a turn's start that is not the recording's): one entry each, also in the console
        /// as an error ("Replay desync"). The replay never sets the board to the recording's: what is here is what the
        /// game made of the recorded orders.
        /// </summary>
        public static readonly List<string> Desyncs = new List<string>();
        /// <summary>The replay cannot go on (the game here ended, or a turn could not be ended): nothing more is played.</summary>
        public static bool Halted { get; private set; }
        /// <summary>The turn has been ended already (the side to move resigned).</summary>
        static bool turnEnded;
        /// <summary>Every action of the turn is played (Next turn): the turn ends once the board is still (Tick).</summary>
        static bool advancePending;

        static void Desync(string what) {
            string s = "turn " + (Index + 1) + ": " + what + (string.IsNullOrEmpty(LastAction) ? "" : " (\"" + LastAction + "\")");
            Desyncs.Add(s);
            Debug.LogError("Replay desync, " + s);
        }

        /// <summary>
        /// The turn after the one on screen, reached as the game reaches it: the turn is ended (captures, upkeep, the
        /// next side's start). The board is then checked against the recording's board for that turn (Verify). Only when
        /// that fails (a desync: a log or game that does not play back as recorded) is the recorded board loaded, as an
        /// error correction (CorrectDesyncs); a replay that plays back as recorded never loads one.
        /// </summary>
        static void Advance() {
            GameController gc = GameController.gc;
            int next = Index + 1;
            if (gc == null) { Halted = true; return; }
            if (gc.gameOver) {
                Desync("the game is over here, the recording goes on to turn " + (next + 1));
                Correct(next);
                return;
            }
            if (!turnEnded) {
                try { gc.EndTurn(); }
                catch (Exception e) {
                    Desync("ending the turn failed: " + e);
                    Correct(next);
                    return;
                }
            }
            Index = next;
            LastAction = "";
            Freeze();
            if (Verify(turns[next].file) > 0) { Correct(next); return; }
            Prepare(turns[next]);
            if (Changed != null) Changed();
        }

        /// <summary>
        /// After a desync, whether the recorded board is loaded to carry on from (a replay: true), or the desync is only
        /// reported and play stops (false: for turns played over the network, where a desync means an incompatible
        /// client or cheating, not something to paper over).
        /// </summary>
        public static bool CorrectDesyncs = true;
        /// <summary>How many times a recorded board was loaded to correct a desync. Each one is an error; a sound log needs none.</summary>
        public static int Corrections { get; private set; }

        static void Correct(int turn) {
            if (!CorrectDesyncs) {
                Halted = true;
                Debug.LogError("Replay desync at turn " + (turn + 1) + ": not corrected; the replay stops here.");
                if (Changed != null) Changed();
                return;
            }
            Corrections++;
            Debug.LogError("Replay desync at turn " + (turn + 1) + ": ERROR CORRECTION, loading the recorded board " + Path.GetFileName(turns[turn].file) + " to carry on (correction " + Corrections + "; a replay that plays back as recorded never needs this).");
            Show(turn);
        }

        /// <summary>
        /// Check the board against a turn's recorded board: the side to move; every unit's place, kind, side, strength,
        /// supplies, fuel and ammunition, and what each transport carries; every building's owner; each side's money.
        /// Each difference is a desync. Nothing is changed. Returns the number of differences.
        /// </summary>
        static int Verify(string file) {
            GameController gc = GameController.gc;
            List<string> why = new List<string>();
            Defective.JSON.JSONObject obj;
            try { obj = new Defective.JSON.JSONObject(File.ReadAllText(file)); }
            catch (Exception e) { Desync("could not read " + Path.GetFileName(file) + " to check the board: " + e.Message); return 1; }
            Defective.JSON.JSONObject active = obj.GetField("active");
            if (active != null && gc.activePlayer != null && gc.activePlayer.id != active.intValue) why.Add("it is " + gc.activePlayer.personal_name + "'s turn here");
            HashSet<Unit> accounted = new HashSet<Unit>();
            Defective.JSON.JSONObject units = obj.GetField("units");
            List<Defective.JSON.JSONObject> list = units != null && units.list != null ? units.list : new List<Defective.JSON.JSONObject>();
            for (int i = 0; i < list.Count; i++) {
                Defective.JSON.JSONObject uj = list[i];
                if (uj == null || uj.GetField("x") == null) continue;
                int x = uj.GetField("x").intValue, y = uj.GetField("y").intValue;
                string kind = uj.GetField("gameobject_name").stringValue;
                long owner = uj.GetField("owner_id").intValue;
                Defective.JSON.JSONObject carried = uj.GetField("transporterIndexPlusOne");
                Unit u;
                if (carried != null && carried.intValue > 0) {
                    // Aboard: the unit standing where its transport is recorded carries one of its kind and side.
                    Defective.JSON.JSONObject tj = carried.intValue - 1 < list.Count ? list[carried.intValue - 1] : null;
                    HexTile tt = tj != null ? Tile(tj.GetField("x").intValue, tj.GetField("y").intValue) : null;
                    Unit transport = tt != null ? tt.occupier : null;
                    u = transport != null ? transport.transported.Find(c => c != null && !accounted.Contains(c) && GameLog.Lines.UnitName(c) == kind && c.owner != null && c.owner.id == owner) : null;
                    if (u == null) { why.Add("no " + kind + " aboard the transport at " + (tt != null ? D(tt) : "?") + ", the recording has one"); continue; }
                } else {
                    HexTile t = Tile(x, y);
                    u = t != null ? t.occupier : null;
                    if (u == null || u.Strength <= 0) { why.Add("no unit at (" + x + "," + y + "), the recording has " + kind); continue; }
                    if (GameLog.Lines.UnitName(u) != kind) { why.Add(GameLog.Lines.UnitName(u) + " at (" + x + "," + y + "), the recording has " + kind); accounted.Add(u); continue; }
                    if (u.owner == null || u.owner.id != owner) { why.Add("the " + kind + " at (" + x + "," + y + ") is another side's"); accounted.Add(u); continue; }
                }
                accounted.Add(u);
                Check(why, u, "strength", u.Strength, uj.GetField("Strength"));
                Check(why, u, "supplies", u.supplies, uj.GetField("supplies"));
                Check(why, u, "fuel", u.fuel, uj.GetField("fuel"));
                Check(why, u, "ammunition", u.ammo, uj.GetField("ammo"));
            }
            if (UnitController.uc != null)
                foreach (Unit u in UnitController.uc.units)
                    if (u != null && u.Strength > 0 && u.tile != null && !accounted.Contains(u))
                        why.Add(GameLog.Lines.Who(u) + (u.trasnporter != null ? " aboard " + GameLog.Lines.Who(u.trasnporter) : "") + " at " + D(u.tile) + " is not in the recording");
            Defective.JSON.JSONObject buildings = obj.GetField("buildings");
            if (buildings != null && buildings.list != null)
                foreach (Defective.JSON.JSONObject bj in buildings.list) {
                    if (bj == null || bj.GetField("x") == null) continue;
                    HexTile t = Tile(bj.GetField("x").intValue, bj.GetField("y").intValue);
                    if (t == null || t.building == null) { why.Add("no building at (" + bj.GetField("x").intValue + "," + bj.GetField("y").intValue + ")"); continue; }
                    long filed = bj.GetField("owner_id").intValue;
                    Player filedOwner = null;
                    foreach (Player p in gc.players) if (p != null && p.id == filed) filedOwner = p;
                    if (t.building.owner != filedOwner)
                        why.Add("the building at " + D(t) + " is " + (t.building.owner != null ? t.building.owner.personal_name + "'s" : "nobody's") + ", the recording has it " + (filedOwner != null ? filedOwner.personal_name + "'s" : "nobody's"));
                }
            Defective.JSON.JSONObject players = obj.GetField("players");
            if (players != null && players.list != null)
                foreach (Defective.JSON.JSONObject pj in players.list) {
                    Defective.JSON.JSONObject id = pj.GetField("id"), money = pj.GetField("money");
                    if (id == null || money == null) continue;
                    foreach (Player p in gc.players)
                        if (p != null && p.id == id.intValue && p.money != money.intValue) why.Add(p.personal_name + " has " + p.money + " money, the recording " + money.intValue);
                }
            foreach (string w in why) Desync("at the turn's start, " + w);
            return why.Count;
        }

        static void Check(List<string> why, Unit u, string what, int here, Defective.JSON.JSONObject filed) {
            if (filed != null && here != filed.intValue) why.Add(GameLog.Lines.Who(u) + " at " + D(u.tile) + " has " + what + " " + here + ", the recording " + filed.intValue);
        }

        // ---------------------------------------------------------------- actions within a turn

        static long Key(int x, int y) { return ((long)x << 32) ^ (uint)y; }
        static HexTile Tile(int x, int y) { HexTile t; return tileAt.TryGetValue(Key(x, y), out t) ? t : null; }

        /// <summary>The board just loaded: its tiles by coordinate, its sides' names, and which of the turn's log lines are actions.</summary>
        static void Prepare(Turn t) {
            Step = 0; LastAction = ""; played.Clear(); turnEnded = false; advancePending = false;
            tileAt.Clear();
            HexGenerator.forAllTiles(x => { if (x != null) tileAt[Key(x.x, x.y)] = x; return 0; });
            // The sides as the log names them (its "side:" lines), and as the board does.
            sides.Clear();
            foreach (string name in logSides.Keys) sides.Add(name);
            GameController gc = GameController.gc;
            if (gc != null) foreach (Player p in gc.players) if (p != null && !sides.Contains(p.personal_name)) sides.Add(p.personal_name);
            t.actions.Clear();
            for (int i = 0; i < t.events.Count; i++) if (IsAction(t.events[i])) t.actions.Add(i);
            if (t.actions.Count == 0 && t.events.Exists(x => x.Contains(" moved (") || x.Contains(" attacks ")))
                Debug.LogWarning("Replay: turn " + (Index + 1) + " has moves or attacks in its log but none could be read (sides known: " + string.Join(", ", sides.ToArray()) + ").");
        }

        static bool IsAction(string line) {
            string a, b, c; int n, x1, y1, x2, y2; GameLog.Lines.AttackLine at;
            if (GameLog.Lines.ReadMoved(line, sides, out a, out b, out x1, out y1, out x2, out y2)) return true;
            if (GameLog.Lines.ReadAttack(line, sides, out at)) return true;
            if (GameLog.Lines.ReadBuy(line, sides, out a, out b, out n)) return true;
            GameLog.Lines.CargoLine cargo;
            if (GameLog.Lines.ReadCargo(line, sides, out cargo)) return true;
            // A strike ordered is played again; its landing is not a line to play (the game works it out as it did).
            GameLog.Lines.StrikeLine strike;
            if (GameLog.Lines.ReadStrikeFired(line, sides, out strike)) return true;
            return GameLog.Lines.ReadResigns(line, sides, out a);
        }

        /// <summary>Actions left to play in the turn on screen.</summary>
        public static int StepsLeft { get { Turn t = Current; return t != null ? Mathf.Max(0, t.actions.Count - Step) : 0; } }
        /// <summary>Nothing left to play: the last action of the last turn has been.</summary>
        public static bool AtEnd { get { return !Active || Halted || (Index >= turns.Count - 1 && StepsLeft == 0); } }

        /// <summary>
        /// Play the next action of the turn on screen, as it was ordered; when the turn has none left, end it as the
        /// game does (once the board is still) and check the next turn's board. False when the recording is over.
        /// </summary>
        public static bool NextAction() {
            Turn t = Current;
            if (!Active || Halted || t == null) return false;
            if (advancePending) { Tick(); return true; }
            while (Step < t.actions.Count && played.Contains(t.actions[Step])) Step++;   // (played already, with another)
            if (Step >= t.actions.Count) {
                if (Index >= turns.Count - 1) return false;
                if (Animate && Unit.AnyAnimating()) return true;   // the turn ends when the board is still (as in play)
                LastAction = "";
                Advance();
                return true;
            }
            int e = t.actions[Step++];
            LastAction = t.events[e].Trim();
            try { Apply(t, e); }
            catch (Exception ex) { Desync("playing it failed: " + ex); }
            if (Changed != null) Changed();
            return true;
        }

        /// <summary>The rest of the turn on screen, every action played at once; the turn ends once the board is still (Tick).</summary>
        public static bool NextTurn() {
            if (!Active || Halted || Index >= turns.Count - 1) return false;
            int at = Index, guard = 0;
            while (Index == at && !advancePending && Current != null && StepsLeft > 0 && guard++ < 10000 && NextAction()) { }
            if (Index == at) { advancePending = true; Tick(); }
            return true;
        }

        /// <summary>A turn played through (NextTurn) ends once the board is still. Called every frame by the controls.</summary>
        public static void Tick() {
            if (!advancePending || !Active) return;
            if (Animate && Unit.AnyAnimating()) return;
            advancePending = false;
            LastAction = "";
            Advance();
        }

        /// <summary>The player a side of the log is: by the id its "side:" line gives, else by name.</summary>
        static Player Side(string name) {
            GameController gc = GameController.gc;
            if (gc == null) return null;
            long id;
            if (logSides.TryGetValue(name, out id)) foreach (Player p in gc.players) if (p != null && p.id == id) return p;
            foreach (Player p in gc.players) if (p != null && p.personal_name == name) return p;
            return null;
        }

        /// <summary>The unit of this side standing on the tile (of this kind; the side's unit there whatever its kind, should the board name it otherwise), or null.</summary>
        static Unit UnitAt(string side, string kind, int x, int y) {
            HexTile t = Tile(x, y);
            Unit u = t != null ? t.occupier : null;
            return u != null && u.Strength > 0 && Is(u, side, kind, false) ? u : null;
        }

        static bool Is(Unit u, string side, string kind, bool exactKind) {
            return u.owner != null && u.owner == Side(side) && (!exactKind || GameLog.Lines.UnitName(u) == kind);
        }

        static bool Animate { get { return GameController.gc != null && !GameController.gc.headless; } }

        /// <summary>
        /// One logged action, ordered as a player or the AI orders it, through the game's own rules (paths, fog,
        /// ambushes, combat, purchases); what comes of it is then checked against the log. Nothing is set by hand.
        /// </summary>
        static void Apply(Turn t, int e) {
            string line = t.events[e];
            string side, unit, a; int x1, y1, x2, y2, cost;
            GameLog.Lines.AttackLine at;
            GameLog.Lines.CargoLine cargo;
            GameLog.Lines.StrikeLine fired;
            if (GameLog.Lines.ReadMoved(line, sides, out side, out unit, out x1, out y1, out x2, out y2)) {
                // The unit walks its path (and fires after, if its next line is its attack from there).
                GameLog.Lines.AttackLine then;
                int next = t.actions.IndexOf(e) + 1;
                bool fires = next < t.actions.Count && GameLog.Lines.ReadAttack(t.events[t.actions[next]], sides, out then)
                             && then.side == side && then.unit == unit && then.fromX == x2 && then.fromY == y2;
                // (Or strikes from there: a move and strike.)
                GameLog.Lines.StrikeLine thenStrike;
                if (!fires && next < t.actions.Count && GameLog.Lines.ReadStrikeFired(t.events[t.actions[next]], sides, out thenStrike))
                    fires = thenStrike.side == side && thenStrike.unit == unit && thenStrike.fromX == x2 && thenStrike.fromY == y2;
                MoveOrCargo(t, e, side, unit, x1, y1, Tile(x2, y2), fires);
            } else if (GameLog.Lines.ReadAttack(line, sides, out at)) {
                Unit attacker = UnitAt(at.side, at.unit, at.fromX, at.fromY);
                if (attacker == null) {
                    // (An older log: a move-and-attack's move written after the attack.) That move is ordered first.
                    for (int j = t.actions.IndexOf(e) + 1; j < t.actions.Count; j++) {
                        int k = t.actions[j];
                        if (played.Contains(k)) continue;
                        if (GameLog.Lines.ReadMoved(t.events[k], sides, out side, out unit, out x1, out y1, out x2, out y2)
                            && side == at.side && unit == at.unit && x2 == at.fromX && y2 == at.fromY) {
                            played.Add(k);
                            Move(t, e, UnitAt(side, unit, x1, y1), Tile(x2, y2), true, null);
                            break;
                        }
                    }
                    attacker = UnitAt(at.side, at.unit, at.fromX, at.fromY);
                }
                Unit defender = UnitAt(at.targetSide, at.targetUnit, at.atX, at.atY);
                if (defender == null) {
                    // The target walked into this (it ran into a unit it had not seen, which fired): an older log writes
                    // its move after, ending where it retreated to. That move is ordered: the game's own fog stops it
                    // and the hidden unit fires, as it did.
                    for (int j = t.actions.IndexOf(e) + 1; j < t.actions.Count; j++) {
                        int k = t.actions[j];
                        if (played.Contains(k)) continue;
                        if (GameLog.Lines.ReadMoved(t.events[k], sides, out side, out unit, out x1, out y1, out x2, out y2)
                            && side == at.targetSide && unit == at.targetUnit) {
                            played.Add(k);
                            Move(t, k, UnitAt(side, unit, x1, y1), Tile(x2, y2), false, null);
                            break;
                        }
                    }
                    if (played.Contains(e)) return;   // (the ambush was this attack: played with the move)
                    defender = UnitAt(at.targetSide, at.targetUnit, at.atX, at.atY);
                }
                if (attacker == null) { Desync("no " + at.side + " " + at.unit + " at (" + at.fromX + "," + at.fromY + ") to attack: the attack is not played"); return; }
                if (defender == null) { Desync("no " + at.targetSide + " " + at.targetUnit + " at (" + at.atX + "," + at.atY + ") to be attacked: the attack is not played"); return; }
                attacker.OrderAttack(defender);
                CheckAttack(at, attacker, defender);
            } else if (GameLog.Lines.ReadStrikeFired(line, sides, out fired)) {
                // A strike that lands at once does what it does on this board; one launched is in the air until its
                // side's next turn starts (Advance ends the turn as the game does), and lands then.
                Unit striker = UnitAt(fired.side, fired.unit, fired.fromX, fired.fromY);
                HexTile aim = Tile(fired.x, fired.y);
                if (striker == null || aim == null) { Desync("no " + fired.side + " " + fired.unit + " at (" + fired.fromX + "," + fired.fromY + ") to strike (" + fired.x + "," + fired.y + ")"); return; }
                if (!Strikes.Order(striker, aim)) Desync(fired.unit + " could not strike (" + fired.x + "," + fired.y + ") on the board as it is");
            } else if (GameLog.Lines.ReadCargo(line, sides, out cargo)) {
                Unit transport = UnitAt(cargo.transportSide, cargo.transportUnit, cargo.transportX, cargo.transportY);
                if (transport == null) { Desync("no " + cargo.transportSide + " " + cargo.transportUnit + " at (" + cargo.transportX + "," + cargo.transportY + ")"); return; }
                if (cargo.boards) {
                    Unit rider = UnitAt(cargo.side, cargo.unit, cargo.x, cargo.y);
                    if (rider == null) { Desync("no " + cargo.side + " " + cargo.unit + " at (" + cargo.x + "," + cargo.y + ") to board"); return; }
                    Board(rider, transport);
                } else {
                    Unit rider = transport.transported.Find(u => u != null && Is(u, cargo.side, cargo.unit, true));
                    if (rider == null) rider = transport.transported.Find(u => u != null && Is(u, cargo.side, cargo.unit, false));
                    HexTile where = Tile(cargo.x, cargo.y);
                    if (rider == null) Desync("no " + cargo.unit + " aboard " + GameLog.Lines.Who(transport) + " to get off");
                    else if (!Transporting.Unload(transport, rider, where)) Desync(GameLog.Lines.Who(rider) + " could not get off " + GameLog.Lines.Who(transport) + " at " + (where != null ? D(where) : "?"));
                }
            } else if (GameLog.Lines.ReadBuy(line, sides, out side, out unit, out cost)) {
                Buy(line, side, unit, cost);
            } else if (GameLog.Lines.ReadResigns(line, sides, out a)) {
                GameController gc = GameController.gc;
                Player p = Side(a), was = gc != null ? gc.activePlayer : null;
                if (gc == null || p == null) { Desync("no side " + a + " to resign"); return; }
                gc.Resign(p);
                if (gc.activePlayer != was || gc.gameOver) turnEnded = true;
            }
            FogOfWar.Changed();
            FogOfWar.Refresh();
        }

        /// <summary>What came of the attack as played, against the log: both sides' strength after, and where the defender was driven back to.</summary>
        static void CheckAttack(GameLog.Lines.AttackLine at, Unit attacker, Unit defender) {
            int aStr = Mathf.Max(0, attacker.Strength), dStr = Mathf.Max(0, defender.Strength);
            if (aStr != Mathf.Max(0, at.strengthAfter) || dStr != Mathf.Max(0, at.targetStrengthAfter))
                Desync(GameLog.Lines.Who(attacker) + " attacking " + GameLog.Lines.Who(defender) + " came out " + aStr + " to " + dStr + ", the log has " + Mathf.Max(0, at.strengthAfter) + " to " + Mathf.Max(0, at.targetStrengthAfter));
            if (at.retreated && dStr > 0 && defender.tile != null && (defender.tile.x != at.retreatX || defender.tile.y != at.retreatY))
                Desync(GameLog.Lines.Who(defender) + " ended at " + D(defender.tile) + ", the log has it driven back to (" + at.retreatX + "," + at.retreatY + ")");
        }

        /// <summary>A purchase, made as in play at the building the log names (BuildingController.Produce: its money, its rules).</summary>
        static void Buy(string line, string side, string kind, int cost) {
            Player p = Side(side);
            Unit prefab = GameAssets.Unit(kind);
            int bx, by, px, py;
            HexTile at = GameLog.Lines.ReadBuyAt(line, out bx, out by) ? Tile(bx, by) : null;
            Building b = at != null ? at.building : null;
            if (p == null || prefab == null || b == null || BuildingController.bc == null) { Desync("no " + (p == null ? "side " + side : prefab == null ? "unit " + kind : "building at (" + bx + "," + by + ")") + " to buy with"); return; }
            if (b.owner != p) { Desync("the building at " + D(at) + " is not " + side + "'s here: nothing bought"); return; }
            int money = p.money;
            Unit made = BuildingController.bc.Produce(b, prefab);
            if (made == null) { Desync(side + " could not buy " + kind + " at " + D(at) + " here (" + money + " money, " + (at.occupier != null ? D(at) + " taken by " + GameLog.Lines.Who(at.occupier) : "the building does not offer it") + ")"); return; }
            if (money - p.money != cost) Desync(kind + " cost " + (money - p.money) + ", the log has " + cost);
            if (GameLog.Lines.ReadBuyPlace(line, out px, out py) && made.tile != null && (made.tile.x != px || made.tile.y != py))
                Desync(kind + " was placed at " + D(made.tile) + ", the log has (" + px + "," + py + ")");
        }

        /// <summary>
        /// A "moved" line. Usually the unit of that kind on the first tile, walking to the second. In a recording made
        /// before boarding and getting off had lines of their own, it may also be: a unit walking into its transport
        /// (the second tile holds a friendly unit that carries it), or a unit getting off one (the first tile is where its
        /// transport stood at the start of the turn; the transport's own move, if any, is ordered first).
        /// </summary>
        static void MoveOrCargo(Turn t, int e, string side, string kind, int x1, int y1, HexTile to, bool fires) {
            if (to == null) { Desync("no such tile on this board"); return; }
            HexTile from = Tile(x1, y1);
            Unit u = from != null ? from.occupier : null;
            Player owner = Side(side);
            if (u != null && u.owner == owner && u.Strength > 0 && GameLog.Lines.UnitName(u) == kind) {
                Unit carrier = to.occupier;
                if (carrier != null && carrier != u && carrier.owner == owner && Transporting.CanCarry(carrier, u)) { Board(u, carrier); return; }
                int ox, oy;
                Move(t, e, u, to, fires, GameLog.Lines.ReadMoveOrder(t.events[e], out ox, out oy) ? Tile(ox, oy) : null);
                return;
            }
            // Getting off: the transport is the unit standing where the cargo was at the start of the turn...
            Unit transport = u != null && u.owner == owner ? u : null;
            Unit rider = transport != null ? transport.transported.Find(c => c != null && GameLog.Lines.UnitName(c) == kind) : null;
            if (rider != null && transport.tile != to && !transport.tile.neighbors.Contains(to)) {
                // ... which drives up first: its own move is later in the log (it finished after its cargo).
                string s2, k2; int a1, b1, a2, b2;
                for (int j = t.actions.IndexOf(e) + 1; j < t.actions.Count; j++) {
                    int k = t.actions[j];
                    if (played.Contains(k)) continue;
                    if (GameLog.Lines.ReadMoved(t.events[k], sides, out s2, out k2, out a1, out b1, out a2, out b2)
                        && s2 == side && a1 == x1 && b1 == y1 && k2 == GameLog.Lines.UnitName(transport)) {
                        played.Add(k);
                        Move(t, k, transport, Tile(a2, b2), false, null);
                        break;
                    }
                }
            }
            if (rider == null && UnitController.uc != null) {
                // ... or one that has already moved: a transport of the side's beside the tile, carrying one.
                foreach (Unit x in UnitController.uc.units) {
                    if (x == null || x.owner != owner || x.tile == null || x.Strength <= 0) continue;
                    if (x.tile != to && !x.tile.neighbors.Contains(to)) continue;
                    Unit c = x.transported.Find(q => q != null && GameLog.Lines.UnitName(q) == kind);
                    if (c != null) { transport = x; rider = c; break; }
                }
            }
            if (rider != null) {
                if (!Transporting.Unload(transport, rider, to)) Desync(kind + " could not get off " + GameLog.Lines.Who(transport) + " at " + D(to));
                return;
            }
            Desync("no " + side + " " + kind + " at (" + x1 + "," + y1 + ") or aboard a transport there");
        }

        /// <summary>The unit walks up to the transport and gets in, as ordered in play.</summary>
        static void Board(Unit rider, Unit transport) {
            List<HexTile> steps = rider.tile == transport.tile || transport.tile.neighbors.Contains(rider.tile) ? new List<HexTile> { rider.tile } : PathBeside(rider, transport.tile);
            if (steps == null) { Desync(GameLog.Lines.Who(rider) + " at " + D(rider.tile) + " has no path to " + GameLog.Lines.Who(transport) + " at " + D(transport.tile) + " here"); return; }
            rider.WalkAndBoard(steps, transport, Animate);
            if (rider.trasnporter != transport) Desync(GameLog.Lines.Who(rider) + " did not get aboard " + GameLog.Lines.Who(transport) + " at " + D(transport.tile) + " (it is at " + D(rider.tile) + ")");
        }

        /// <summary>
        /// The move, ordered as in play: the unit is ordered to where the recorded game ordered it (the log's "ordered
        /// to", else where it ended) along the path the game finds for it, and the game plays it — its animation, its
        /// fuel, and under the recorded fog rule an ambush by a unit its side did not see (which fires, as it did: that
        /// attack line is then played already). Where it ends is checked against the log.
        /// </summary>
        /// <param name="ordered">Where the recorded game ordered it, when it stopped short at <paramref name="to"/>; null when it got where it was ordered.</param>
        static void Move(Turn t, int e, Unit u, HexTile to, bool thenAttacks, HexTile ordered) {
            if (u == null || to == null) { Desync("no unit to move"); return; }
            HexTile target = ordered ?? to;
            HexTile was = u.tile;
            if (was == target) {
                if (was != to) Desync(GameLog.Lines.Who(u) + " is at " + D(was) + " already, the log has it stopping at " + D(to));
                return;
            }
            string order = " from " + D(was) + " ordered to " + D(target) + (ordered != null ? " (stopped short at " + D(to) + " in the recorded game)" : "");
            List<HexTile> steps = PathTo(u, target);
            if (steps == null) { Desync(GameLog.Lines.Who(u) + order + ": the game here finds no path; it stays at " + D(was)); return; }
            u.MoveAlong(steps, thenAttacks, Animate);
            Unit ambusher = u.ambushedBy;
            if (ambusher != null) {
                // The hidden unit's fire is the log's next attack by it on this unit where it stopped: played with the move.
                for (int j = Mathf.Max(0, Step - 1); j < t.actions.Count; j++) {
                    int k = t.actions[j];
                    GameLog.Lines.AttackLine shot;
                    if (played.Contains(k) || !GameLog.Lines.ReadAttack(t.events[k], sides, out shot)) continue;
                    if (Side(shot.side) == ambusher.owner && shot.unit == GameLog.Lines.UnitName(ambusher) && shot.atX == u.tile.x && shot.atY == u.tile.y) {
                        played.Add(k);
                        CheckAttack(shot, ambusher, u);
                        break;
                    }
                }
            }
            if (u.Strength > 0 && u.tile != to)
                Desync(GameLog.Lines.Who(u) + order + ": here it " + (ambusher != null ? "ran into " + GameLog.Lines.Who(ambusher) + " at " + D(ambusher.tile) + " and stopped at " + D(u.tile) : "ended at " + D(u.tile)) + ", the log has " + D(to));
        }

        static string D(HexTile t) { return GameLog.Lines.D(t); }

        /// <summary>The path the game's own search gives the unit to <paramref name="to"/> this turn (its tile first), as a player's click follows; null if it does not reach.</summary>
        static List<HexTile> PathTo(Unit u, HexTile to) {
            if (!Animate) {
                // Headless (no tile visuals to reset): the same search as the AI's, on what the mover's side knows.
                HexTactica.AI.MoveField field;
                using (FogOfWar.Concealing(u.owner)) field = HexTactica.AI.AIPath.ThisTurn(u);
                List<HexTile> p = u.PathIn(field, to);
                return p != null && p.Count >= 2 && p[0] == u.tile ? p : null;
            }
            HexGenerator.forAllTiles(x => { x.G = 0; x.parent = null; return 0; });
            List<HexTile> reach = u.tile.CheckMovementRange(u);
            List<HexTile> steps = reach.Contains(to) ? to.buildpath() : null;
            HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
            return steps != null && steps.Count >= 2 && steps[0] == u.tile ? steps : null;
        }

        /// <summary>A path for the unit to a tile beside <paramref name="target"/> (to board what stands there), or null.</summary>
        static List<HexTile> PathBeside(Unit u, HexTile target) {
            foreach (HexTile n in target.neighbors) {
                if (n == null) continue;
                List<HexTile> steps = PathTo(u, n);
                if (steps != null) return steps;
            }
            return null;
        }

        public static void Next() { Show(Index + 1); }
        public static void Previous() { Show(Index - 1); }

        /// <summary>Watch as player <paramref name="index"/> sees it (their fog), or -1: everything.</summary>
        public static void View(int index) {
            ViewAs = index;
            ApplyView();
        }

        /// <summary>The views on offer: every player, then everything.</summary>
        public static void CycleView() {
            GameController gc = GameController.gc;
            int n = gc != null && gc.players != null ? gc.players.Count : 0;
            View(ViewAs + 1 >= n ? -1 : ViewAs + 1);
        }

        /// <summary>
        /// What is shown, and nothing else: the fog rule is the recorded game's (RecordedFog) whatever the view. As a side:
        /// that side's fog drawn (fogShownOnScreen, fogViewer). Everything: fog not drawn (each side's fog faintly, as
        /// FogView shows it with fogShownOnScreen off).
        /// </summary>
        static void ApplyView() {
            GameController gc = GameController.gc;
            if (gc == null) return;
            gc.fogShownOnScreen = ViewAs >= 0;
            gc.fogViewer = ViewAs;
            FogOfWar.Changed();
            FogOfWar.Refresh();
            FogView.BoardLoaded();
        }

        /// <summary>Nobody plays a replay: every AI off.</summary>
        static void Freeze() {
            GameController gc = GameController.gc;
            if (gc == null || gc.players == null) return;
            foreach (Player p in gc.players) {
                if (p == null) continue;
                AIPlayer ai = p.GetComponent<AIPlayer>();
                if (ai != null) ai.enabled = false;
            }
        }

        /// <summary>End the replay: the board stays as last shown, the fog and the recording setting as they were.</summary>
        public static void Close() {
            if (!Active) return;
            Active = false;
            GameController gc = GameController.gc;
            if (gc != null) { gc.fogOfWar = fogWas; gc.fogViewer = viewerWas; gc.fogShownOnScreen = shownWas; FogOfWar.Changed(); FogOfWar.Refresh(); FogView.BoardLoaded(); }
            ScenarioLoader.RecordGames = recordWas;
        }

        /// <summary>The title of the turn on screen: "Round 3, Blue to move (turn 6 of 18)".</summary>
        public static string Title() {
            Turn t = Current;
            if (t == null) return "";
            return (t.round >= 0 ? "Round " + (t.round + 1) + ", " : "") + t.side + " to move (turn " + (Index + 1) + " of " + turns.Count + ")";
        }

        /// <summary>What the view shows: "seen as Blue" (their fog) or "everything".</summary>
        public static string ViewName() {
            GameController gc = GameController.gc;
            if (ViewAs < 0 || gc == null || gc.players == null || ViewAs >= gc.players.Count || gc.players[ViewAs] == null) return "everything";
            return "as " + gc.players[ViewAs].personal_name + " sees it";
        }
    }
}
