using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using HexTactica.AI;

namespace HexTactica.Harness {

    /// <summary>
    /// Records a game as it is played — by the AI, the baselines, or you — so it can be read
    /// and replayed afterwards.
    ///
    /// Each recording is a folder (Application.persistentDataPath/game-logs/&lt;date-time&gt;/) with:
    ///   game.log          — the board at the start of every turn (same text drawing as the
    ///                       headless narration) and each side's state (worth, units by kind,
    ///                       cities, income, what it can buy); every move, attack, purchase,
    ///                       unit lost (and how) and building changing hands; and for this AI
    ///                       every decision with the options it weighed and the simulated reply.
    ///                       Event lines are worded by GameLog.Lines, which also reads them back
    ///                       (replays, the post-game report: GameRecord).
    ///   turn-NNN-Side.dat — the board at the start of every turn, as a scenario file: load it
    ///                       in Unity (HexTactica ▸ Load Scenario File…) or headless to replay
    ///                       from that exact position
    /// Zip the folder and send it.
    ///
    /// Starts automatically when a scenario file is loaded; HexTactica ▸ Game Log ▸ Start
    /// Recording records whatever is on the board now. Writes as it goes, so stopping play mode
    /// mid-game loses nothing.
    /// </summary>
    public static partial class GameLog {

        public static bool Recording { get { return writer != null; } }
        public static string Folder { get; private set; }

        static StreamWriter writer;
        static string redKind = "?";
        static string source = "";
        static readonly Dictionary<Unit, HexTile> lastSeen = new Dictionary<Unit, HexTile>();
        static readonly HashSet<Unit> ditching = new HashSet<Unit>();
        static bool finished;
        /// <summary>The line the first two sides' HQs stand on (for each side's "front").</summary>
        static Vector3 hqA, hqB;
        static int axisTiles;

        public static string DefaultRoot {
            get { return Path.Combine(Application.persistentDataPath, "game-logs"); }
        }

        /// <summary>Start a new recording (closing any open one). <paramref name="root"/> null = DefaultRoot.</summary>
        public static void Start(string from, string red, string root) {
            Stop();
            if (Replay.Active) return;   // a recording being watched is not a game to record
            source = from ?? "";
            redKind = string.IsNullOrEmpty(red) ? "?" : red;
            Folder = Path.Combine(root ?? DefaultRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(Folder);
            writer = new StreamWriter(Path.Combine(Folder, "game.log"), false, new UTF8Encoding(false));
            writer.AutoFlush = true;
            finished = false;
            turns = 0;
            Listen();

            Line("HexTactica game log, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Line(Lines.Source(source.Length > 0 ? source : "board as it was"));
            WriteSides();
            Line("Blue (upper case) = this AI; Red (lower case) = " + (redKind == "Full" ? "this AI" : redKind));
            if (GameController.gc != null) Line("rules: " + GameController.gc.rules + (GameController.gc.rules == RuleSet.Attrition ? " (HQs cannot be attacked; after " + GameController.gc.roundLimit + " rounds the most strength x cost left wins)" : GameController.gc.rules == RuleSet.Sandbox ? " (the game never ends)" : ""));
            Line("glyphs: S supply unit, G grunt, T tank, A artillery; buildings (no unit on them) H HQ, C city, B forward base, P airport, D naval yard, = neutral; . grass, f forest, ^ mountain, ~ water");
            OnTurnStarted(GameController.gc != null ? GameController.gc.activePlayer : null);
        }

        static void Listen() {
            lastSeen.Clear();
            ditching.Clear();
            Unit.Attacked += OnAttack;
            Building.Captured += OnCapture;
            Building.OwnerChanged += OnOwnerChanged;
            BuildingController.Produced += OnProduced;
            Unit.Ditched += OnDitch;
            Unit.Died += OnDied;
            Unit.RanInto += OnRanInto;
            Unit.Finished += OnFinished;
            GameController.TurnStarted += OnTurnStarted;
            GameController.GameEnded += GameOver;
            GameController.Resigned += OnResigned;
            Transporting.Boarded += OnBoarded;
            Transporting.Unloaded += OnUnloaded;
            AIPlayer.Trace = Line;
            AITactical.DecisionLog = Line;
            AISupplyPlanner.DebugLog = Line;
            HexTactica.AI.AISupplyChain.DebugLog = Line;
            HexTactica.AI.AISupplyTree.DebugLog = Line;
        }

        /// <summary>Each side (who plays it, its faction) and the HQ-to-HQ line fronts are measured on.</summary>
        static void WriteSides() {
            GameController gc = GameController.gc;
            if (gc == null) return;
            foreach (Player p in gc.players) if (p != null) Line(Lines.Side(p));
            Player a = gc.players.Count > 0 ? gc.players[0] : null, b = gc.players.Count > 1 ? gc.players[1] : null;
            Building ha = a != null ? GameController.HQOf(a) : null, hb = b != null ? GameController.HQOf(b) : null;
            axisTiles = 0;
            if (ha != null && hb != null && ha.tile != null && hb.tile != null) {
                hqA = new Vector3(ha.tile.x, ha.tile.y, ha.tile.z); hqB = new Vector3(hb.tile.x, hb.tile.y, hb.tile.z);
                axisTiles = Mathf.RoundToInt(Mathf.Max(Mathf.Abs(hqA.x - hqB.x), Mathf.Max(Mathf.Abs(hqA.y - hqB.y), Mathf.Abs(hqA.z - hqB.z))));
            }
            Line(Lines.Axis(axisTiles));
        }

        /// <summary>Where a game stood when it was saved: its log folder, how much of game.log was written, how many turn snapshots.</summary>
        public class SavePoint { public string folder; public long length; public int turns; }

        /// <summary>
        /// Mark a save in the log: a saved game keeps where its log was, so loading it goes on with the same log
        /// (cut back to this line) rather than starting a new one. Null when nothing is recording.
        /// </summary>
        public static SavePoint MarkSave(string path) {
            if (writer == null) return null;
            Line(Lines.Saved(path));
            writer.Flush();
            return new SavePoint { folder = Folder, length = writer.BaseStream.Position, turns = turns };
        }

        /// <summary>
        /// A saved game loaded: a new recording that starts with its log as it was when saved (game.log up to the
        /// save line, the turn snapshots taken before it), then goes on. False (nothing started) if that log is gone.
        /// </summary>
        public static bool Continue(SavePoint from, string savePath, string red, string root) {
            if (from == null || string.IsNullOrEmpty(from.folder)) return false;
            string oldLog = Path.Combine(from.folder, "game.log");
            if (!File.Exists(oldLog)) return false;
            Stop();
            source = savePath ?? "";
            redKind = string.IsNullOrEmpty(red) ? "?" : red;
            Folder = Path.Combine(root ?? DefaultRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(Folder);
            using (FileStream src = File.OpenRead(oldLog))
            using (FileStream dst = File.Create(Path.Combine(Folder, "game.log"))) {
                byte[] buffer = new byte[65536];
                long left = Math.Min(from.length, src.Length);
                while (left > 0) {
                    int n = src.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                    if (n <= 0) break;
                    dst.Write(buffer, 0, n);
                    left -= n;
                }
            }
            foreach (string f in Directory.GetFiles(from.folder, "turn-*.dat")) {
                int index;
                string name = Path.GetFileName(f);
                if (name.Length > 8 && int.TryParse(name.Substring(5, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) && index < from.turns)
                    File.Copy(f, Path.Combine(Folder, name), true);
            }
            writer = new StreamWriter(Path.Combine(Folder, "game.log"), true, new UTF8Encoding(false));
            writer.AutoFlush = true;
            finished = false;
            turns = from.turns;
            Listen();
            Line(Lines.Resumed(source));
            WriteSides();
            OnTurnStarted(GameController.gc != null ? GameController.gc.activePlayer : null);
            return true;
        }

        public static void Stop() {
            if (writer == null) return;
            Unit.Attacked -= OnAttack;
            Building.Captured -= OnCapture;
            Building.OwnerChanged -= OnOwnerChanged;
            BuildingController.Produced -= OnProduced;
            Unit.Ditched -= OnDitch;
            Unit.Died -= OnDied;
            Unit.Finished -= OnFinished;
            Unit.RanInto -= OnRanInto;
            GameController.TurnStarted -= OnTurnStarted;
            GameController.GameEnded -= GameOver;
            GameController.Resigned -= OnResigned;
            Transporting.Boarded -= OnBoarded;
            Transporting.Unloaded -= OnUnloaded;
            if (AIPlayer.Trace == (Action<string>)Line) AIPlayer.Trace = null;
            if (AITactical.DecisionLog == (Action<string>)Line) AITactical.DecisionLog = null;
            if (AISupplyPlanner.DebugLog == (Action<string>)Line) AISupplyPlanner.DebugLog = null;
            if (HexTactica.AI.AISupplyChain.DebugLog == (Action<string>)Line) HexTactica.AI.AISupplyChain.DebugLog = null;
            if (HexTactica.AI.AISupplyTree.DebugLog == (Action<string>)Line) HexTactica.AI.AISupplyTree.DebugLog = null;
            Line("-- recording stopped");
            writer.Dispose();
            writer = null;
        }

        static void Line(string s) {
            if (writer != null) writer.WriteLine(s);
        }

        // ------------------------------------------------------------------ events

        static void OnTurnStarted(Player p) {
            if (writer == null) return;
            GameController gc = GameController.gc;
            if (gc != null && gc.gameOver) { GameOver(); return; }
            string side = p != null ? p.personal_name : "?";
            int round = gc != null ? gc.days : 0;
            Line("");
            Line(Lines.Round(round, side));
            Line(Draw());
            Line("   units:" + Roster());
            if (Buildings().Length > 0) Line("   buildings:" + Buildings());
            if (gc != null) foreach (Player q in gc.players) if (q != null) WriteState(q);
            string file = string.Format("turn-{0:000}-{1}.dat", TurnIndex(), Safe(side));
            try {
                File.WriteAllText(Path.Combine(Folder, file), Snapshot(p, "snapshot of round " + round + ", " + side + " to move"));
                Line("   (board saved as " + file + ")");
            } catch (Exception e) {
                Line("   (could not save board: " + e.Message + ")");
            }
            lastSeen.Clear();
            foreach (Unit u in AllUnits()) if (u != null && u.tile != null) lastSeen[u] = u.tile;
        }

        static int turns;
        static int TurnIndex() { return turns++; }

        static void OnFinished(Unit u) {
            if (writer == null || u == null) return;
            if (u.trasnporter != null) return;   // aboard: it goes where its transport goes (the boarding was logged)
            HexTile was;
            lastSeen.TryGetValue(u, out was);
            if (u.tile != null && was != null && was != u.tile)
                Line(Lines.Moved(u, was, u.tile));
            if (u.tile != null) lastSeen[u] = u.tile;
        }

        /// <summary>A unit about to do something from <paramref name="now"/> that it moved to first: its move is logged before what it does there (it only "finishes" after).</summary>
        static void FlushMove(Unit u, HexTile now) {
            HexTile was;
            if (u == null || now == null || !lastSeen.TryGetValue(u, out was) || was == now) return;
            Line(Lines.Moved(u, was, now));
            lastSeen[u] = now;
        }

        static void OnBoarded(Unit transport, Unit cargo, HexTile from) {
            if (writer == null || transport == null || cargo == null) return;
            FlushMove(transport, transport.tile);
            HexTile was;
            // Where it stood at the start of its action (it may have walked up to the transport first).
            if (!lastSeen.TryGetValue(cargo, out was) || was == null) was = from;
            Line(Lines.Boards(transport, cargo, was));
            lastSeen.Remove(cargo);
        }

        static void OnUnloaded(Unit transport, Unit cargo, HexTile onto) {
            if (writer == null || transport == null || cargo == null) return;
            FlushMove(transport, transport.tile);
            Line(Lines.GetsOff(transport, cargo, onto));
            lastSeen[cargo] = onto;
        }

        /// <summary>Each side's state lines at the start of a turn (Lines.State, Fields, CanBuy).</summary>
        static void WriteState(Player p) {
            float worth = 0f, along = 0f;
            int units = 0, placed = 0, cities = 0, buildings = 0, income = 0;
            SortedDictionary<string, int> kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (Unit u in p.units) {
                if (u == null || u.Strength <= 0) continue;
                SupplyPoint sp = u.Hub;
                if (sp != null && sp.producer) continue;   // an HQ that is a unit (older saves) is not a unit to count
                units++;
                worth += u.cost * u.Strength / (float)Mathf.Max(1, FullStrength(u));
                string k = Lines.UnitName(u);
                int n; kinds.TryGetValue(k, out n); kinds[k] = n + 1;
                if (u.tile != null && axisTiles > 0) { along += Along(u.tile); placed++; }
            }
            SortedSet<string> buy = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Building b in Building.all) {
                if (b == null || b.owner != p) continue;
                buildings++;
                income += b.income;
                if (b.type == BuildingType.City) cities++;
                foreach (Unit u in b.Purchasable()) buy.Add(Lines.UnitName(u));
            }
            Line(Lines.State(p.personal_name, Mathf.RoundToInt(worth), units, cities, buildings, income, p.money, placed > 0 ? along / placed : -1f));
            if (kinds.Count > 0) Line(Lines.Fields(p.personal_name, kinds));
            if (buy.Count > 0) Line(Lines.CanBuy(p.personal_name, buy));
        }

        static readonly Dictionary<string, int> fullStrength = new Dictionary<string, int>();
        /// <summary>The strength a unit of its kind has when new (its prefab's).</summary>
        static int FullStrength(Unit u) {
            string n = Lines.UnitName(u);
            int s;
            if (fullStrength.TryGetValue(n, out s)) return s;
            Unit prefab = GameAssets.Unit(n);
            s = Mathf.Max(1, prefab != null ? prefab.Strength : u.Strength);
            fullStrength[n] = s;
            return s;
        }

        /// <summary>How far along the line from the first side's HQ to the second's a tile lies (0 at the first, 1 at the second).</summary>
        static float Along(HexTile t) {
            Vector3 ab = hqB - hqA, ap = new Vector3(t.x, t.y, t.z) - hqA;
            float len2 = ab.x * ab.x + ab.y * ab.y + ab.z * ab.z;
            return len2 > 0f ? (ap.x * ab.x + ap.y * ab.y + ap.z * ab.z) / len2 : 0f;
        }

        static void OnResigned(Player p) { if (writer != null && p != null) Line(Lines.Resigns(p)); }

        static void OnProduced(Building b, Unit made) {
            if (writer == null || made == null) return;
            Line(Lines.Buy(b, made));
        }

        static void OnOwnerChanged(Building b, Player previous) {
            if (writer == null || b == null) return;
            Line(Lines.ChangesHands(b, previous));
            if (GameController.gc != null && GameController.gc.gameOver) GameOver();
        }

        /// <summary>Every unit lost, and how: out of fuel, aboard a transport that went down, starved in upkeep, or in combat (the attack line after it says by whom).</summary>
        static void OnDied(Unit u) {
            if (writer == null || u == null) return;
            string cause = ditching.Remove(u) ? Lines.Fuel : u.owner != null && u.owner.eliminated ? Lines.Eliminated
                         : u.trasnporter != null ? Lines.Aboard : Player.InUpkeep ? Lines.Supply : Lines.Combat;
            Line(Lines.Dies(u, cause));
        }

        static void OnDitch(Unit u) {
            if (writer == null || u == null) return;
            ditching.Add(u);
            Line("   " + Who(u) + " ditched at " + D(u.tile) + ": out of fuel");
        }

        static void OnRanInto(Unit mover, Unit hidden, HexTile stoppedAt) {
            if (writer == null || mover == null || hidden == null) return;
            Line("   " + Who(mover) + " ran into " + Who(hidden) + " at " + D(hidden.tile) + " (unseen, fog of war): stopped at " + D(stoppedAt));
        }

        static void OnCapture(Building b, Player side, int before, int after) {
            if (writer == null || b == null) return;
            Line("   " + (side != null ? side.personal_name : "?") + " captures " + b.name.Replace("(Clone)", "") + " " + D(b.tile) + ": "
                 + before + " -> " + after + " of " + b.CaptureThreshold + ", now " + (b.owner != null ? b.owner.personal_name + "'s" : "neutral"
                 + (b.capturingPlayer != null ? " (" + b.capturingPlayer.personal_name + " progress)" : "")));
        }

        static void OnAttack(Unit attacker, Unit defender, HexTile from, HexTile at, CombatOutcome o) {
            if (writer == null) return;
            // A move-and-attack: report the move first (the unit only "finishes" after firing).
            FlushMove(attacker, from);
            // A defender fired on mid-move (it ran into an unseen unit): its move up to where it was stopped comes first.
            FlushMove(defender, at);
            Line(Lines.Attack(attacker, defender, from, at, o));
            // A retreat moves the defender without an action of its own: its next move starts from where it was driven.
            if (defender != null && defender.Strength > 0 && defender.tile != null) lastSeen[defender] = defender.tile;
            if (GameController.gc != null && GameController.gc.gameOver) GameOver();
        }

        /// <summary>The game has ended (an HQ taken, a side eliminated: not every end has a turn after it): write its end, once.</summary>
        public static void EndIfOver() {
            if (writer != null && GameController.gc != null && GameController.gc.gameOver) GameOver();
        }

        static void GameOver() {
            if (finished) return;
            finished = true;
            GameController gc = GameController.gc;
            Line("");
            Line(Lines.GameOver(gc != null ? gc.days : 0, gc != null ? gc.winner : null));
            Line(Draw());
            Line("   units:" + Roster());
            if (Buildings().Length > 0) Line("   buildings:" + Buildings());
        }

        // ------------------------------------------------------------------ text

        static string Who(Unit u) { return Lines.Who(u); }
        static string D(HexTile t) { return Lines.D(t); }

        static string Safe(string s) {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "side";
        }

        static List<Unit> AllUnits() {
            return UnitController.uc != null ? new List<Unit>(UnitController.uc.units) : new List<Unit>();
        }

        /// <summary>Every building: owner (or neutral and whose progress), points of its threshold, store.</summary>
        static string Buildings() {
            StringBuilder sb = new StringBuilder();
            foreach (Building b in Building.all) {
                if (b == null || b.tile == null) continue;
                sb.Append(' ').Append(b.name.Replace("(Clone)", "")).Append(D(b.tile)).Append(' ')
                  .Append(b.owner != null ? b.owner.personal_name : "neutral" + (b.capturingPlayer != null ? "/" + b.capturingPlayer.personal_name : ""))
                  .Append(' ').Append(b.capturePoints).Append('/').Append(b.CaptureThreshold);
                if (b.producesSupply && !b.IsHQ) sb.Append(" store ").Append(b.supplies).Append('/').Append(b.suppliesMax);
                sb.Append(';');
            }
            return sb.ToString();
        }

        public static string Roster() {
            StringBuilder sb = new StringBuilder();
            foreach (Unit u in AllUnits()) {
                if (u == null || u.tile == null) continue;
                string n = u.name.Replace("(Clone)", "");
                sb.Append(' ').Append(u.owner != null ? u.owner.personal_name.Substring(0, 1) : "?").Append(n.Substring(0, Math.Min(2, n.Length)))
                  .Append(D(u.tile)).Append('s').Append(u.Strength);
                if (u.ammoMax > 0 && u.ammo < u.ammoMax) sb.Append('a').Append(u.ammo);
                if (u.suppliesMax > 0 && u.supplies < u.suppliesMax) sb.Append('u').Append(u.supplies);
                if (LocomotionRules.NeedsBase(u.locomotion) && u.fuelMax > 0) sb.Append('f').Append(u.fuel);
            }
            return sb.ToString();
        }

        public static int Radius {
            get {
                if (HexGenerator.hg == null) return 0;
                int r = 0;
                foreach (HexTile t in HexGenerator.hg.tiles) if (t != null) r = Math.Max(r, Math.Max(Math.Abs(t.x), Math.Max(Math.Abs(t.y), Math.Abs(t.z))));
                return r;
            }
        }

        /// <summary>The board as text: Blue (id 112) upper case, everyone else lower case. Rows of only water are skipped.</summary>
        public static string Draw() {
            StringBuilder sb = new StringBuilder();
            if (HexGenerator.hg == null) return "";
            int r = Radius;
            for (int y = -r; y <= r; y++) {
                char[] row = new string(' ', r * 4 + 3).ToCharArray();
                bool any = false;
                for (int x = -r; x <= r; x++) {
                    HexTile t = HexGenerator.hg.get(x, y, -x - y);
                    if (t == null) continue;
                    int col = 2 * x + y + r * 2;
                    if (col < 0 || col >= row.Length) continue;
                    row[col] = Glyph(t);
                    if (row[col] != '~' || t.occupier != null) any = true;
                }
                if (any) sb.Append(string.Format("  {0,3} ", y)).Append(new string(row).TrimEnd()).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        public static char Glyph(HexTile t) {
            if (t.occupier != null) {
                Unit u = t.occupier;
                char c = 'U';
                SupplyPoint sp = u.Hub;
                if (sp != null) c = sp.producer ? 'H' : 'S';
                else if (u.name.StartsWith("Grunt")) c = 'G';
                else if (u.name.StartsWith("Tank")) c = 'T';
                else if (u.name.StartsWith("Artillery")) c = 'A';
                return u.owner != null && u.owner.id == 112 ? c : char.ToLower(c);
            }
            if (t.building != null) {
                Building b = t.building;
                char c = b.IsHQ ? 'H' : b.type == BuildingType.City ? 'C' : b.type == BuildingType.FOB ? 'B' : b.type == BuildingType.Airport ? 'P' : b.type == BuildingType.NavalYard ? 'D' : 'X';
                if (b.owner == null) return '=';
                return b.owner.id == 112 ? c : char.ToLower(c);
            }
            if (t.terrain == null) return '?';
            switch (t.terrain.name) {
                case "Forest": return 'f';
                case "Mountains": return '^';
                case "Water": return '~';
                default: return '.';
            }
        }

        // ------------------------------------------------------------------ snapshot

        static TerrainData Under(HexTile t) { return t.building != null ? t.building.Underlying(t) : t.terrain; }

        static string Q(string s) { return "\"" + (s ?? "").Replace("\\", "/").Replace("\"", "'") + "\""; }

        /// <summary>
        /// The board as a scenario file: the level editor's format (tiles, units, buildings, active
        /// player) plus a test block naming Red's controller, so loading it replays from here.
        /// Written by hand rather than JsonUtility so it is identical in Unity and headless.
        /// </summary>
        public static string Snapshot(Player active, string question) {
            StringBuilder sb = new StringBuilder();
            int r = Radius;
            sb.Append("{\n\"width\":").Append(2 * r + 1).Append(",\n\"active\":").Append(active != null ? active.id : 112);
            sb.Append(",\n\"test\":{\"name\":").Append(Q("snapshot")).Append(",\"kind\":\"snapshot\",\"opponent\":").Append(Q(redKind == "?" ? "Human" : redKind))
              .Append(",\"rules\":").Append(Q(GameController.gc != null ? GameController.gc.rules.ToString().ToLowerInvariant() : "standard")).Append(",\"rounds\":\"30\",\"check\":\"report\",\"question\":").Append(Q(question)).Append('}');
            // The sides as they are (name, colour, faction, money), the round, and whether factions field their special
            // units: a replay's board is this turn's, sides and all.
            if (GameController.gc != null) {
                sb.Append(",\n\"day\":").Append(GameController.gc.days);
                sb.Append(",\n\"factionSpecialUnits\":").Append(Q(GameController.gc.SpecialUnitsSetting));
                sb.Append(",\n\"players\":[");
                bool firstSide = true;
                foreach (Player q in GameController.gc.players) {
                    if (q == null) continue;
                    if (!firstSide) sb.Append(',');
                    firstSide = false;
                    sb.Append('\n').Append(JsonUtility.ToJson(new PlayerData(q)));
                }
                sb.Append("\n]");
            }
            sb.Append(",\n\"tiles\":[");
            bool first = true;
            foreach (HexTile t in HexGenerator.hg.tiles) {
                if (t == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(string.Format(CultureInfo.InvariantCulture, "\n{{\"x\":{0},\"y\":{1},\"z\":{2},\"damage\":{3},\"combatCount\":{4},\"terrain\":{5}}}",
                    t.x, t.y, t.z, t.damage, t.combatCount, Q(Under(t) != null ? Under(t).name : "Water")));
            }
            sb.Append("\n]");

            if (BuildingController.bc != null && BuildingController.bc.buildings.Count > 0) {
                sb.Append(",\n\"buildings\":[");
                first = true;
                foreach (Building b in BuildingController.bc.buildings) {
                    if (b == null || b.tile == null) continue;
                    BuildingData bd = new BuildingData(b);
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(string.Format(CultureInfo.InvariantCulture,
                        "\n{{\"x\":{0},\"y\":{1},\"z\":{2},\"gameobject_name\":{3},\"personal_name\":{4},\"type\":{5},\"owner_id\":{6},\"income\":{7},\"producesSupply\":{8},\"supplyRange\":{9},\"supplies\":{10},\"suppliesMax\":{11},\"damageLevel\":{12},\"capturePoints\":{13},\"capturingPlayerId\":{14}}}",
                        bd.x, bd.y, bd.z, Q(bd.gameobject_name), Q(bd.personal_name), bd.type, bd.owner_id, bd.income, bd.producesSupply ? "true" : "false",
                        bd.supplyRange, bd.supplies, bd.suppliesMax, bd.damageLevel, bd.capturePoints, bd.capturingPlayerId));
                }
                sb.Append("\n]");
            }

            List<Unit> units = new List<Unit>();
            foreach (Unit u in AllUnits()) if (u != null && u.tile != null && u.Strength > 0) units.Add(u);
            if (units.Count == 0) { sb.Append(",\n\"units\":[]\n}\n"); return sb.ToString(); }   // (the JSON reader does not take "[\n]")
            sb.Append(",\n\"units\":[");
            first = true;
            foreach (Unit u in units) {
                if (!first) sb.Append(',');
                first = false;
                string prefab = u.name.Replace("(Clone)", "").Trim();
                int carrier = u.trasnporter != null ? units.IndexOf(u.trasnporter) + 1 : 0;
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "\n{{\"x\":{0},\"y\":{1},\"z\":{2},\"Strength\":{3},\"Veterancy\":{4},\"personalName\":{5},\"active\":true,\"owner_id\":{6},\"supplies\":{7},\"fuel\":{8},\"ammo\":{9},\"gameobject_name\":{10},\"transporterIndexPlusOne\":{11}}}",
                    u.tile.x, u.tile.y, u.tile.z, u.Strength, u.Veterancy, Q(prefab), u.owner != null ? u.owner.id : 0,
                    u.supplies, u.fuel, u.ammo, Q(prefab), carrier));
            }
            sb.Append("\n]\n}\n");
            return sb.ToString();
        }
    }
}
