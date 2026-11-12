using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HexTactica.Harness {

    /// <summary>
    /// Watching a recorded game again (GameLog), a turn at a time — and within a turn an action at a time
    /// (NextAction): each of the turn's logged moves, attacks, purchases, losses and captures is played on the board
    /// with the result the log gives (GameLog.Lines reads the lines it wrote). The next turn's board is loaded from
    /// its snapshot, so anything the log does not tell (units getting in and out of transports) is put right there.
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

        static bool fogWas, recordWas;
        static int viewerWas = -1;

        /// <summary>Read the recording in <paramref name="folder"/>: its boards in order, each with its part of the log. False if it has none.</summary>
        public static bool Read(string folder) {
            turns.Clear(); Ending = null; logSides.Clear(); logFactions.Clear();
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
            return true;
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
            }
            Folder = folder;
            Active = true;
            ViewAs = -1;
            Index = -1;
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

        // ---------------------------------------------------------------- turn to turn without loading

        /// <summary>
        /// The turn after the one on screen, reached as the game reaches it: the turn is ended (captures, upkeep, the
        /// next side's start), with every unit left where it is — nothing is taken off and put back. The board is then
        /// checked against the recording's board for that turn, and only if it is not the same is that board loaded.
        /// </summary>
        /// <summary>How many times going on to the next turn had to load its board (the board was not the recording's).</summary>
        public static int Reloads;

        static void Advance() {
            GameController gc = GameController.gc;
            int next = Index + 1;
            if (gc == null || gc.gameOver) { Show(next); return; }
            try { gc.EndTurn(); }
            catch (Exception e) { Debug.LogWarning("Replay: ending the turn failed (" + e.Message + "): the next turn's board is loaded."); Show(next); return; }
            string why = Differs(turns[next].file);
            if (why != null && SetDown(turns[next].file)) why = Differs(turns[next].file);
            if (why != null) {
                Reloads++;
                Debug.LogWarning("Replay: after turn " + (Index + 1) + " the board is not the recording's (" + why + "): turn " + (next + 1) + "'s board is loaded.");
                Show(next);
                return;
            }
            Index = next;
            Freeze();
            Prepare(turns[next]);
            if (Changed != null) Changed();
        }

        /// <summary>
        /// A recording from before getting off was logged says nothing of a unit set down that did nothing after. Each
        /// unit the turn's recorded board has standing where this board has none, while one of that side and kind rides
        /// a transport, is set down there (from the nearest such transport). True if any was.
        /// </summary>
        static bool SetDown(string file) {
            GameController gc = GameController.gc;
            bool any = false;
            try {
                Defective.JSON.JSONObject units = new Defective.JSON.JSONObject(File.ReadAllText(file)).GetField("units");
                if (units == null || units.list == null || UnitController.uc == null) return false;
                foreach (Defective.JSON.JSONObject uj in units.list) {
                    Defective.JSON.JSONObject carried = uj.GetField("transporterIndexPlusOne");
                    if (carried != null && carried.intValue > 0) continue;
                    HexTile t = Tile(uj.GetField("x").intValue, uj.GetField("y").intValue);
                    if (t == null || t.occupier != null) continue;
                    string kind = uj.GetField("gameobject_name").stringValue;
                    long owner = uj.GetField("owner_id").intValue;
                    Unit transport = null, rider = null; int best = int.MaxValue;
                    foreach (Unit x in UnitController.uc.units) {
                        if (x == null || x.owner == null || x.owner.id != owner || x.tile == null || x.Strength <= 0) continue;
                        Unit c = x.transported.Find(q => q != null && GameLog.Lines.UnitName(q) == kind);
                        int d = x.tile.Distance(t);
                        if (c != null && d < best) { best = d; transport = x; rider = c; }
                    }
                    if (rider != null && (Transporting.Unload(transport, rider, t) || Transporting.Disembark(transport, rider, t))) any = true;
                }
            } catch (Exception e) { Debug.LogWarning("Replay: " + e.Message); }
            return any;
        }

        /// <summary>
        /// How the board differs from a turn's recorded board (the side to move, a unit's place, kind, side or strength, a
        /// building's owner), or null if it does not. Where they agree, each unit's supplies, fuel and ammunition and each
        /// side's money are set to the recording's.
        /// </summary>
        static string Differs(string file) {
            GameController gc = GameController.gc;
            Defective.JSON.JSONObject obj;
            try { obj = new Defective.JSON.JSONObject(File.ReadAllText(file)); }
            catch (Exception e) { return "could not read " + Path.GetFileName(file) + ": " + e.Message; }
            Defective.JSON.JSONObject active = obj.GetField("active");
            if (active != null && gc.activePlayer != null && gc.activePlayer.id != active.intValue) return "it is " + gc.activePlayer.personal_name + "'s turn";
            int onBoard = 0;
            if (UnitController.uc != null) foreach (Unit u in UnitController.uc.units) if (u != null && u.Strength > 0 && u.tile != null && u.trasnporter == null) onBoard++;
            int inFile = 0;
            List<KeyValuePair<Unit, Defective.JSON.JSONObject>> matched = new List<KeyValuePair<Unit, Defective.JSON.JSONObject>>();
            Defective.JSON.JSONObject units = obj.GetField("units");
            if (units != null && units.list != null)
                foreach (Defective.JSON.JSONObject uj in units.list) {
                    Defective.JSON.JSONObject carried = uj.GetField("transporterIndexPlusOne");
                    if (carried != null && carried.intValue > 0) continue;
                    inFile++;
                    int x = uj.GetField("x").intValue, y = uj.GetField("y").intValue;
                    HexTile t = Tile(x, y);
                    Unit u = t != null ? t.occupier : null;
                    string kind = uj.GetField("gameobject_name").stringValue;
                    if (u == null || u.Strength <= 0) return "no unit at (" + x + "," + y + "), the recording has " + kind;
                    if (GameLog.Lines.UnitName(u) != kind) return GameLog.Lines.UnitName(u) + " at (" + x + "," + y + "), the recording has " + kind;
                    if (u.owner == null || u.owner.id != uj.GetField("owner_id").intValue) return "the unit at (" + x + "," + y + ") is another side's";
                    if (u.Strength != uj.GetField("Strength").intValue) return GameLog.Lines.Who(u) + " at (" + x + "," + y + ") has strength " + u.Strength + ", the recording " + uj.GetField("Strength").intValue;
                    matched.Add(new KeyValuePair<Unit, Defective.JSON.JSONObject>(u, uj));
                }
            if (onBoard != inFile) return onBoard + " units on the board, the recording has " + inFile;
            Defective.JSON.JSONObject buildings = obj.GetField("buildings");
            if (buildings != null && buildings.list != null)
                foreach (Defective.JSON.JSONObject bj in buildings.list) {
                    HexTile t = Tile(bj.GetField("x").intValue, bj.GetField("y").intValue);
                    if (t == null || t.building == null) return "no building at (" + bj.GetField("x").intValue + "," + bj.GetField("y").intValue + ")";
                    long owner = t.building.owner != null ? t.building.owner.id : -1;
                    long filed = bj.GetField("owner_id").intValue;
                    bool filedNeutral = true;
                    foreach (Player p in gc.players) if (p != null && p.id == filed) filedNeutral = false;
                    if (filedNeutral ? t.building.owner != null : owner != filed) return "the building at (" + t.x + "," + t.y + ") has another owner";
                }
            // The same board: what the log does not tell (stores) is taken from the recording, so nothing drifts.
            foreach (KeyValuePair<Unit, Defective.JSON.JSONObject> m in matched) {
                Defective.JSON.JSONObject v;
                if ((v = m.Value.GetField("supplies")) != null) m.Key.supplies = v.intValue;
                if ((v = m.Value.GetField("fuel")) != null) m.Key.fuel = v.intValue;
                if ((v = m.Value.GetField("ammo")) != null) m.Key.ammo = v.intValue;
            }
            Defective.JSON.JSONObject players = obj.GetField("players");
            if (players != null && players.list != null)
                foreach (Defective.JSON.JSONObject pj in players.list) {
                    Defective.JSON.JSONObject id = pj.GetField("id"), money = pj.GetField("money");
                    if (id == null || money == null) continue;
                    foreach (Player p in gc.players) if (p != null && p.id == id.intValue) p.money = money.intValue;
                }
            return null;
        }

        // ---------------------------------------------------------------- actions within a turn

        static long Key(int x, int y) { return ((long)x << 32) ^ (uint)y; }
        static HexTile Tile(int x, int y) { HexTile t; return tileAt.TryGetValue(Key(x, y), out t) ? t : null; }

        /// <summary>The board just loaded: its tiles by coordinate, its sides' names, and which of the turn's log lines are actions.</summary>
        static void Prepare(Turn t) {
            Step = 0; LastAction = ""; played.Clear();
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
            if (GameLog.Lines.ReadDies(line, sides, out a, out b, out c)) return c != GameLog.Lines.Combat && c != GameLog.Lines.Aboard;
            if (GameLog.Lines.ReadChangesHands(line, out a, out b, out c)) return true;
            GameLog.Lines.CargoLine cargo;
            if (GameLog.Lines.ReadCargo(line, sides, out cargo)) return true;
            return GameLog.Lines.ReadResigns(line, sides, out a);
        }

        /// <summary>Actions left to play in the turn on screen.</summary>
        public static int StepsLeft { get { Turn t = Current; return t != null ? Mathf.Max(0, t.actions.Count - Step) : 0; } }
        /// <summary>Nothing left to play: the last action of the last turn has been.</summary>
        public static bool AtEnd { get { return !Active || (Index >= turns.Count - 1 && StepsLeft == 0); } }

        /// <summary>
        /// Play the next action of the turn on screen; when the turn has none left, go to the next turn's board.
        /// False when the recording is over.
        /// </summary>
        public static bool NextAction() {
            Turn t = Current;
            if (!Active || t == null) return false;
            while (Step < t.actions.Count && played.Contains(t.actions[Step])) Step++;   // (a move played early, with its attack)
            if (Step >= t.actions.Count) {
                if (Index >= turns.Count - 1) return false;
                if (Unit.AnyAnimating()) return true;   // the turn ends when the board is still (as in play)
                LastAction = "";
                Advance();
                return true;
            }
            int e = t.actions[Step++];
            LastAction = t.events[e].Trim();
            try { Apply(t, e); }
            catch (Exception ex) { Debug.LogWarning("Replay: could not play \"" + LastAction + "\": " + ex.Message); }
            if (Changed != null) Changed();
            return true;
        }

        /// <summary>The board of the next turn (whatever is left of this one is not played).</summary>
        public static bool NextTurn() {
            if (!Active || Index >= turns.Count - 1) return false;
            Next();
            return true;
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

        static void Apply(Turn t, int e) {
            string line = t.events[e];
            string side, unit, cause, a, b; int x1, y1, x2, y2, cost;
            GameLog.Lines.AttackLine at;
            GameLog.Lines.CargoLine cargo;
            if (GameLog.Lines.ReadMoved(line, sides, out side, out unit, out x1, out y1, out x2, out y2)) {
                // Ordered as a player or the AI orders it: the unit walks its path (and fires after, if its next line is its attack from there).
                GameLog.Lines.AttackLine then;
                int next = t.actions.IndexOf(e) + 1;
                bool fires = next < t.actions.Count && GameLog.Lines.ReadAttack(t.events[t.actions[next]], sides, out then)
                             && then.side == side && then.unit == unit && then.fromX == x2 && then.fromY == y2;
                MoveOrCargo(t, e, side, unit, x1, y1, Tile(x2, y2), fires);
            } else if (GameLog.Lines.ReadAttack(line, sides, out at)) {
                Unit attacker = UnitAt(at.side, at.unit, at.fromX, at.fromY);
                if (attacker == null) {
                    // (An older log: a move-and-attack's move written after the attack.) Play that move first.
                    for (int j = t.actions.IndexOf(e) + 1; j < t.actions.Count; j++) {
                        int k = t.actions[j];
                        if (played.Contains(k)) continue;
                        if (GameLog.Lines.ReadMoved(t.events[k], sides, out side, out unit, out x1, out y1, out x2, out y2)
                            && side == at.side && unit == at.unit && x2 == at.fromX && y2 == at.fromY) {
                            Move(UnitAt(side, unit, x1, y1), Tile(x2, y2), true);
                            played.Add(k);
                            break;
                        }
                    }
                    attacker = UnitAt(at.side, at.unit, at.fromX, at.fromY);
                }
                Unit defender = UnitAt(at.targetSide, at.targetUnit, at.atX, at.atY);
                if (defender == null) {
                    // The target walked into this (it ran into a unit it had not seen, which fired): its move is
                    // logged after. Play that move first.
                    for (int j = t.actions.IndexOf(e) + 1; j < t.actions.Count; j++) {
                        int k = t.actions[j];
                        if (played.Contains(k)) continue;
                        if (GameLog.Lines.ReadMoved(t.events[k], sides, out side, out unit, out x1, out y1, out x2, out y2)
                            && side == at.targetSide && unit == at.targetUnit && x2 == at.atX && y2 == at.atY) {
                            MoveOrCargo(t, k, side, unit, x1, y1, Tile(x2, y2), false);
                            played.Add(k);
                            break;
                        }
                    }
                    defender = UnitAt(at.targetSide, at.targetUnit, at.atX, at.atY);
                }
                if (defender == null) { Warn("no " + at.targetSide + " " + at.targetUnit + " at (" + at.atX + "," + at.atY + ") to be attacked"); return; }
                HexTile back = at.retreated ? Tile(at.retreatX, at.retreatY) : null;
                if (attacker == null || attacker == defender) {
                    Warn("no " + at.side + " " + at.unit + " at (" + at.fromX + "," + at.fromY + ") to attack: only the result is shown");
                    defender.Strength = Mathf.Max(0, at.targetStrengthAfter);
                    if (defender.Strength <= 0) defender.ReplayLoss();
                    return;
                }
                // The attack is ordered as in play (the shot, the answer, the sounds, a retreat); then the board is held
                // to what the log says came of it, should the game work it out otherwise (fog is not as it was).
                attacker.OrderAttack(defender);
                Hold(attacker, at.strengthAfter);
                Hold(defender, at.targetStrengthAfter);
                if (back != null && defender.Strength > 0 && defender.tile != back && back.occupier == null) defender.RetreatTo(back);
            } else if (GameLog.Lines.ReadCargo(line, sides, out cargo)) {
                Unit transport = UnitAt(cargo.transportSide, cargo.transportUnit, cargo.transportX, cargo.transportY);
                if (transport == null) { Warn("no " + cargo.transportSide + " " + cargo.transportUnit + " at (" + cargo.transportX + "," + cargo.transportY + ")"); return; }
                if (cargo.boards) {
                    Unit rider = UnitAt(cargo.side, cargo.unit, cargo.x, cargo.y);
                    if (rider == null) return;
                    Board(rider, transport);
                } else {
                    Unit rider = transport.transported.Find(u => u != null && Is(u, cargo.side, cargo.unit, true));
                    if (rider == null) rider = transport.transported.Find(u => u != null && Is(u, cargo.side, cargo.unit, false));
                    if (rider != null) Transporting.Unload(transport, rider, Tile(cargo.x, cargo.y));
                }
            } else if (GameLog.Lines.ReadBuy(line, sides, out side, out unit, out cost)) {
                Player p = Side(side);
                Unit prefab = GameAssets.Unit(unit);
                if (p != null) p.money -= cost;
                if (p != null && prefab != null && GameLog.Lines.ReadBuyPlace(line, out x1, out y1)) {
                    HexTile where = Tile(x1, y1);
                    if (where != null && where.occupier == null && UnitController.uc != null) {
                        Unit made = UnitController.uc.CreateUnit(p, prefab, where);
                        if (made != null) made.active = false;   // (a unit does nothing the turn it is bought)
                    } else Warn("the unit bought could not be put at (" + x1 + "," + y1 + ")");
                }
            } else if (GameLog.Lines.ReadDies(line, sides, out side, out unit, out cause)) {
                if (GameLog.Lines.ReadTile(line, out x1, out y1)) {
                    Unit u = UnitAt(side, unit, x1, y1);
                    if (u != null) u.ReplayLoss();
                }
            } else if (GameLog.Lines.ReadChangesHands(line, out unit, out a, out b)) {
                if (GameLog.Lines.ReadTile(line, out x1, out y1)) {
                    HexTile where = Tile(x1, y1);
                    if (where != null && where.building != null) where.building.owner = Side(b);
                }
            }
            FogOfWar.Changed();
            FogOfWar.Refresh();
        }

        /// <summary>
        /// A "moved" line. Usually the unit of that kind on the first tile, walking to the second. In a recording made
        /// before boarding and getting off had lines of their own, it may also be: a unit walking into its transport
        /// (the second tile holds a friendly unit that carries it), or a unit getting off one (the first tile is where its
        /// transport stood at the start of the turn; the transport's own move, if any, is played first).
        /// </summary>
        static void MoveOrCargo(Turn t, int e, string side, string kind, int x1, int y1, HexTile to, bool fires) {
            if (to == null) return;
            HexTile from = Tile(x1, y1);
            Unit u = from != null ? from.occupier : null;
            Player owner = Side(side);
            if (u != null && u.owner == owner && u.Strength > 0 && GameLog.Lines.UnitName(u) == kind) {
                Unit carrier = to.occupier;
                if (carrier != null && carrier != u && carrier.owner == owner && Transporting.CanCarry(carrier, u)) { Board(u, carrier); return; }
                Move(u, to, fires);
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
                        Move(transport, Tile(a2, b2), false);
                        played.Add(k);
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
            if (rider == null && UnitController.uc != null) {
                // ... or one further off (it got off and walked on): the nearest transport of the side's carrying one.
                int best = int.MaxValue;
                foreach (Unit x in UnitController.uc.units) {
                    if (x == null || x.owner != owner || x.tile == null || x.Strength <= 0) continue;
                    Unit c = x.transported.Find(q => q != null && GameLog.Lines.UnitName(q) == kind);
                    int d = x.tile.Distance(to);
                    if (c != null && d < best) { best = d; transport = x; rider = c; }
                }
            }
            if (rider != null) {
                // Off as in play where the game allows it; else set down where the recording has it.
                if (!Transporting.Unload(transport, rider, to) && !Transporting.Disembark(transport, rider, to))
                    Warn(kind + " could not get off at (" + to.x + "," + to.y + ")");
                return;
            }
            Warn("no " + side + " " + kind + " at (" + x1 + "," + y1 + ") or aboard a transport there");
        }

        /// <summary>The unit walks up to the transport and gets in, as ordered in play; if the game will not have it, it is put aboard as the log says.</summary>
        static void Board(Unit rider, Unit transport) {
            List<HexTile> steps = rider.tile == transport.tile || transport.tile.neighbors.Contains(rider.tile) ? new List<HexTile> { rider.tile } : PathBeside(rider, transport.tile);
            bool headless = GameController.gc != null && GameController.gc.headless;
            if (steps == null || !rider.WalkAndBoard(steps, transport, !headless) || rider.trasnporter != transport) {
                if (rider.trasnporter != transport) Transporting.Embark(transport, rider);
            }
        }

        static void Warn(string what) { Debug.LogWarning("Replay: " + what + " (\"" + LastAction + "\")"); }

        /// <summary>The unit's strength as the log has it after the action (it is lost if that is none).</summary>
        static void Hold(Unit u, int strength) {
            if (u == null || u.Strength <= 0) return;   // (already lost in the attack as played)
            if (u.Strength == strength) return;
            Warn(GameLog.Lines.Who(u) + " came out at " + u.Strength + ", the log says " + strength + ": set to the log's");
            u.Strength = Mathf.Max(0, strength);
            if (u.Strength <= 0) u.ReplayLoss();
        }

        /// <summary>
        /// The move, ordered as in play: the unit walks the path the game finds for it to <paramref name="to"/> (its
        /// animation and sounds with it). Only when the game finds none (the board is not as it was) is it set there.
        /// </summary>
        static void Move(Unit u, HexTile to, bool thenAttacks) {
            if (u == null || to == null || u.tile == to) return;
            if (to.occupier != null && to.occupier != u) { Warn("(" + to.x + "," + to.y + ") is taken: the move is left out"); return; }
            List<HexTile> steps = GameController.gc != null && !GameController.gc.headless ? PathTo(u, to) : null;
            if (steps != null) u.MoveAlong(steps, thenAttacks, true);
            else u.TeleportTo(to, u.tile.Distance(to), thenAttacks);
        }

        /// <summary>The path the game's own search gives the unit to <paramref name="to"/> this turn (its tile first), as a player's click follows; null if it does not reach.</summary>
        static List<HexTile> PathTo(Unit u, HexTile to) {
            HexGenerator.forAllTiles(x => { x.G = 0; x.parent = null; return 0; });
            List<HexTile> reach = u.tile.CheckMovementRange(u);
            List<HexTile> steps = reach.Contains(to) ? to.buildpath() : null;
            HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
            return steps != null && steps.Count >= 2 && steps[0] == u.tile ? steps : null;
        }

        /// <summary>A path for the unit to a tile beside <paramref name="target"/> (to board what stands there), or null.</summary>
        static List<HexTile> PathBeside(Unit u, HexTile target) {
            if (GameController.gc == null || GameController.gc.headless) return null;
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

        static void ApplyView() {
            GameController gc = GameController.gc;
            if (gc == null) return;
            gc.fogOfWar = ViewAs >= 0;
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
            if (gc != null) { gc.fogOfWar = fogWas; gc.fogViewer = viewerWas; FogOfWar.Changed(); FogOfWar.Refresh(); FogView.BoardLoaded(); }
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
