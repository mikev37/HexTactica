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
            turns.Clear(); Ending = null;
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
            if (!Active) {
                GameLog.Stop();
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
            try { Loader(turns[i].file); }
            finally { ScenarioLoader.RecordGames = recordWas; }
            Index = i;
            Freeze();
            ApplyView();
            Prepare(turns[i]);
            if (Changed != null) Changed();
        }

        // ---------------------------------------------------------------- actions within a turn

        static long Key(int x, int y) { return ((long)x << 32) ^ (uint)y; }
        static HexTile Tile(int x, int y) { HexTile t; return tileAt.TryGetValue(Key(x, y), out t) ? t : null; }

        /// <summary>The board just loaded: its tiles by coordinate, its sides' names, and which of the turn's log lines are actions.</summary>
        static void Prepare(Turn t) {
            Step = 0; LastAction = ""; played.Clear();
            tileAt.Clear();
            HexGenerator.forAllTiles(x => { if (x != null) tileAt[Key(x.x, x.y)] = x; return 0; });
            sides.Clear();
            GameController gc = GameController.gc;
            if (gc != null) foreach (Player p in gc.players) if (p != null) sides.Add(p.personal_name);
            t.actions.Clear();
            for (int i = 0; i < t.events.Count; i++) if (IsAction(t.events[i])) t.actions.Add(i);
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
                Next();
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

        static Player Side(string name) {
            GameController gc = GameController.gc;
            if (gc != null) foreach (Player p in gc.players) if (p != null && p.personal_name == name) return p;
            return null;
        }

        /// <summary>The unit of this side and kind standing on the tile, or null.</summary>
        static Unit UnitAt(string side, string kind, int x, int y) {
            HexTile t = Tile(x, y);
            Unit u = t != null ? t.occupier : null;
            return u != null && u.Strength > 0 && Is(u, side, kind) ? u : null;
        }

        static bool Is(Unit u, string side, string kind) {
            return u.owner != null && u.owner.personal_name == side && GameLog.Lines.UnitName(u) == kind;
        }

        static void Apply(Turn t, int e) {
            string line = t.events[e];
            string side, unit, cause, a, b; int x1, y1, x2, y2, cost;
            GameLog.Lines.AttackLine at;
            GameLog.Lines.CargoLine cargo;
            if (GameLog.Lines.ReadMoved(line, sides, out side, out unit, out x1, out y1, out x2, out y2)) {
                Move(UnitAt(side, unit, x1, y1), Tile(x2, y2));
            } else if (GameLog.Lines.ReadAttack(line, sides, out at)) {
                Unit attacker = UnitAt(at.side, at.unit, at.fromX, at.fromY);
                if (attacker == null) {
                    // It moved and then fired: its move is logged after (when it finished). Play that move first.
                    for (int j = t.actions.IndexOf(e) + 1; j < t.actions.Count; j++) {
                        int k = t.actions[j];
                        if (played.Contains(k)) continue;
                        if (GameLog.Lines.ReadMoved(t.events[k], sides, out side, out unit, out x1, out y1, out x2, out y2)
                            && side == at.side && unit == at.unit && x2 == at.fromX && y2 == at.fromY) {
                            Move(UnitAt(side, unit, x1, y1), Tile(x2, y2));
                            played.Add(k);
                            break;
                        }
                    }
                    attacker = UnitAt(at.side, at.unit, at.fromX, at.fromY);
                }
                Unit defender = UnitAt(at.targetSide, at.targetUnit, at.atX, at.atY);
                if (defender == null) return;
                HexTile back = at.retreated ? Tile(at.retreatX, at.retreatY) : null;
                if (attacker != null && attacker != defender) attacker.ReplayAttack(defender, at.dealt, at.taken, back);
                else { defender.Strength = Mathf.Max(0, defender.Strength - at.dealt); if (defender.Strength <= 0) defender.ReplayLoss(); }
            } else if (GameLog.Lines.ReadCargo(line, sides, out cargo)) {
                Unit transport = UnitAt(cargo.transportSide, cargo.transportUnit, cargo.transportX, cargo.transportY);
                if (transport == null) return;
                if (cargo.boards) {
                    Unit rider = UnitAt(cargo.side, cargo.unit, cargo.x, cargo.y);
                    if (rider != null) Transporting.Embark(transport, rider);
                } else {
                    Unit rider = transport.transported.Find(u => u != null && Is(u, cargo.side, cargo.unit));
                    if (rider != null) Transporting.Unload(transport, rider, Tile(cargo.x, cargo.y));
                }
            } else if (GameLog.Lines.ReadBuy(line, sides, out side, out unit, out cost)) {
                Player p = Side(side);
                Unit prefab = GameAssets.Unit(unit);
                if (p != null) p.money -= cost;
                if (p != null && prefab != null && GameLog.Lines.ReadBuyPlace(line, out x1, out y1)) {
                    HexTile where = Tile(x1, y1);
                    if (where != null && where.occupier == null && UnitController.uc != null) UnitController.uc.CreateUnit(p, prefab, where);
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

        static void Move(Unit u, HexTile to) {
            if (u == null || to == null || u.tile == to) return;
            if (to.occupier != null && to.occupier != u) return;   // (into a transport, or out of step with the log: the next turn's board puts it right)
            u.TeleportTo(to, 0);
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
