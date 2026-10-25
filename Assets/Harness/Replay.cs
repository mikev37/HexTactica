using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HexTactica.Harness {

    /// <summary>
    /// Watching a recorded game again (GameLog), a turn at a time.
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
        }

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
            ReplayPanel.Ensure();
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
