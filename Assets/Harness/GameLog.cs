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
    ///                       headless narration), every move and attack by anyone, and for this
    ///                       AI every decision with the options it weighed and the simulated reply
    ///   turn-NNN-Side.dat — the board at the start of every turn, as a scenario file: load it
    ///                       in Unity (HexTactica ▸ Load Scenario File…) or headless to replay
    ///                       from that exact position
    /// Zip the folder and send it.
    ///
    /// Starts automatically when a scenario file is loaded; HexTactica ▸ Game Log ▸ Start
    /// Recording records whatever is on the board now. Writes as it goes, so stopping play mode
    /// mid-game loses nothing.
    /// </summary>
    public static class GameLog {

        public static bool Recording { get { return writer != null; } }
        public static string Folder { get; private set; }

        static StreamWriter writer;
        static string redKind = "?";
        static string source = "";
        static readonly Dictionary<Unit, HexTile> lastSeen = new Dictionary<Unit, HexTile>();
        static bool finished;

        public static string DefaultRoot {
            get { return Path.Combine(Application.persistentDataPath, "game-logs"); }
        }

        /// <summary>Start a new recording (closing any open one). <paramref name="root"/> null = DefaultRoot.</summary>
        public static void Start(string from, string red, string root) {
            Stop();
            source = from ?? "";
            redKind = string.IsNullOrEmpty(red) ? "?" : red;
            Folder = Path.Combine(root ?? DefaultRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(Folder);
            writer = new StreamWriter(Path.Combine(Folder, "game.log"), false, new UTF8Encoding(false));
            writer.AutoFlush = true;
            finished = false;
            turns = 0;
            lastSeen.Clear();

            Unit.Attacked += OnAttack;
            Building.Captured += OnCapture;
            Unit.Ditched += OnDitch;
            Unit.RanInto += OnRanInto;
            Unit.Finished += OnFinished;
            GameController.TurnStarted += OnTurnStarted;
            AIPlayer.Trace = Line;
            AITactical.DecisionLog = Line;
            AISupplyPlanner.DebugLog = Line;

            Line("HexTactica game log, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Line("source: " + (source.Length > 0 ? source : "board as it was"));
            Line("Blue (upper case) = this AI; Red (lower case) = " + (redKind == "Full" ? "this AI" : redKind));
            if (GameController.gc != null) Line("rules: " + GameController.gc.rules + (GameController.gc.rules == RuleSet.Attrition ? " (HQs cannot be attacked; after " + GameController.gc.roundLimit + " rounds the most strength x cost left wins)" : GameController.gc.rules == RuleSet.Sandbox ? " (the game never ends)" : ""));
            Line("glyphs: S supply unit, G grunt, T tank, A artillery; buildings (no unit on them) H HQ, C city, B forward base, P airport, D naval yard, = neutral; . grass, f forest, ^ mountain, ~ water");
            OnTurnStarted(GameController.gc != null ? GameController.gc.activePlayer : null);
        }

        public static void Stop() {
            if (writer == null) return;
            Unit.Attacked -= OnAttack;
            Building.Captured -= OnCapture;
            Unit.Ditched -= OnDitch;
            Unit.Finished -= OnFinished;
            Unit.RanInto -= OnRanInto;
            GameController.TurnStarted -= OnTurnStarted;
            if (AIPlayer.Trace == (Action<string>)Line) AIPlayer.Trace = null;
            if (AITactical.DecisionLog == (Action<string>)Line) AITactical.DecisionLog = null;
            if (AISupplyPlanner.DebugLog == (Action<string>)Line) AISupplyPlanner.DebugLog = null;
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
            Line("== round " + round + ": " + side + " to move");
            Line(Draw());
            Line("   units:" + Roster());
            if (Buildings().Length > 0) Line("   buildings:" + Buildings());
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
            HexTile was;
            lastSeen.TryGetValue(u, out was);
            if (u.tile != null && was != null && was != u.tile)
                Line("   " + Who(u) + " moved " + D(was) + " -> " + D(u.tile));
            if (u.tile != null) lastSeen[u] = u.tile;
        }

        static void OnDitch(Unit u) {
            if (writer == null || u == null) return;
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
            HexTile was;
            if (attacker != null && from != null && lastSeen.TryGetValue(attacker, out was) && was != from) {
                Line("   " + Who(attacker) + " moved " + D(was) + " -> " + D(from));
                lastSeen[attacker] = from;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("   ").Append(Who(attacker)).Append(' ').Append(D(from)).Append(" attacks ").Append(Who(defender)).Append(' ').Append(D(at))
              .Append(": deals ").Append(o.damageToDefender).Append(", takes ").Append(o.damageToAttacker);
            if (o.defenderFiresFirst) sb.Append(" (ambush: defender fired first)");
            if (o.defenderDies) sb.Append(", ").Append(defender.name.Replace("(Clone)", "")).Append(" destroyed");
            else if (o.defenderSuppressed) sb.Append(", suppressed " + o.suppressionAdded + " (" + defender.suppressed + " of " + defender.Strength + " will not fire back until its turn)");
            else if (o.defenderRetreats) sb.Append(o.defenderPinned ? ", pinned (1 of that for having nowhere to retreat)" : ", retreats");
            if (o.attackerDies) sb.Append(", attacker destroyed");
            sb.Append(" -> ").Append(attacker.Strength).Append(" v ").Append(defender.Strength);
            Line(sb.ToString());
            if (GameController.gc != null && GameController.gc.gameOver) GameOver();
        }

        static void GameOver() {
            if (finished) return;
            finished = true;
            GameController gc = GameController.gc;
            Line("");
            Line("== game over after round " + (gc != null ? gc.days : 0) + ": winner " + (gc != null && gc.winner != null ? gc.winner.personal_name : "none"));
            Line(Draw());
            Line("   units:" + Roster());
            if (Buildings().Length > 0) Line("   buildings:" + Buildings());
        }

        // ------------------------------------------------------------------ text

        static string Who(Unit u) {
            if (u == null) return "?";
            return (u.owner != null ? u.owner.personal_name + " " : "") + u.name.Replace("(Clone)", "");
        }

        static string D(HexTile t) { return t == null ? "(gone)" : "(" + t.x + "," + t.y + ")"; }

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

        static string Roster() {
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
                SupplyPoint sp = u.GetComponent<SupplyPoint>();
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
            sb.Append(",\n\"tiles\":[");
            bool first = true;
            foreach (HexTile t in HexGenerator.hg.tiles) {
                if (t == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(string.Format(CultureInfo.InvariantCulture, "\n{{\"x\":{0},\"y\":{1},\"z\":{2},\"damage\":{3},\"combatCount\":{4},\"terrain\":{5}}}",
                    t.x, t.y, t.z, t.damageLevel, t.combatCount, Q(Under(t) != null ? Under(t).name : "Water")));
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
