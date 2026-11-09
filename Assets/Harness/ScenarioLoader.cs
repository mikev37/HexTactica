using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Defective.JSON;

namespace HexTactica.Harness {

    /// <summary>
    /// Loads scenarios from the save format the level editor already writes.
    ///
    /// The map you save with 's' and load with 'l' while playtesting *is* the test
    /// case — there is no reason to author scenarios twice. This reads the same JSON
    /// LevelEditor.Load reads, resets the board to it, and hands control to the
    /// BatchRunner. Build a map, press 's', point the harness at it, and it becomes a
    /// regression test you can run ten thousand times.
    ///
    /// Keeping one format also means a scenario that misbehaves in batch can be
    /// loaded straight into the editor and watched.
    /// </summary>
    public class ScenarioLoader : MonoBehaviour {

        [Tooltip("Files under persistentDataPath to run, e.g. 'save.dat', 'corridor.dat'")]
        public List<string> saveFiles = new List<string> { "save.dat" };

        [Tooltip("Turn limit applied to every loaded scenario")]
        public int turnLimit = 40;

        public static string PathFor(string fileName) {
            return Path.Combine(Application.persistentDataPath, fileName);
        }

        public static bool Exists(string fileName) {
            return File.Exists(PathFor(fileName));
        }

        /// <summary>
        /// Restore the board to a saved state (the one loader: the L key, the HexTactica menu and the harness all use it).
        /// </summary>
        public static bool LoadBoard(string fileName) {
            return LoadBoardFromPath(PathFor(fileName));
        }

        /// <summary>
        /// Load a save (or a scenario file from Tests/Scenarios) from anywhere on disk. Scenario
        /// files carry a "test" block naming Red's opponent AI (Legacy, Greedy, Full, or Human) and
        /// an "active" player; both are applied here, so the scenario plays out as it does in the
        /// headless harness. Blue (id 112) keeps the scene's AIPlayer.
        /// </summary>
        public static bool LoadBoardFromPath(string path) {
            if (!File.Exists(path)) {
                UnityEngine.Debug.LogError("ScenarioLoader: no such save " + path);
                return false;
            }

            string json = File.ReadAllText(path);
            JSONObject obj = new JSONObject(json);

            // Wipe live state first.
            if (UnitController.uc != null) {
                List<Unit> existing = new List<Unit>(UnitController.uc.units);
                existing.ForEach(u => { if (u != null) u.Delete(); });
                UnitController.uc.units.Clear();
            }
            if (BuildingController.bc != null) BuildingController.bc.Clear();

            if (GameController.gc != null) {
                GameController.gc.gameOver = false;
                GameController.gc.winner = null;
                // The day the file was saved on (a save goes on from there); 0 for a file without one.
                GameController.gc.days = DayOf(obj);
                GameController.gc.players.ForEach(p => {
                    if (p == null) return;
                    p.eliminated = false;
                    p.unitTurnsOutOfSupply = 0;
                    p.unitsLostToAttrition = 0;
                    p.hubsStranded = 0;
                });
            }

            // A map made on a bigger board than the scene's: the board grows to fit it.
            FitBoard(obj);
            // A file covers only its own map; the rest of the scene's board is water. Without this a
            // radius-7 scenario on the radius-15 board would leave a ring of unedited land around it.
            FloodWater();

            JSONObjectEnumerator tiles = obj.GetField("tiles").GetEnumerator();
            int offBoard = 0;
            while (tiles.MoveNext()) {
                TileData td = JsonUtility.FromJson<TileData>(tiles.Current.Print());
                if (HexGenerator.hg.get(td.x, td.y, td.z) == null) { offBoard++; continue; }
                td.load();
            }
            // The scene's board is smaller than the file's map: those tiles are simply gone (water),
            // which can cut a map in two. Say so rather than load something else in silence.
            if (offBoard > 0) UnityEngine.Debug.LogWarning("Scenario: " + offBoard + " tiles lie outside this scene's board (width " + HexGenerator.hg.width
                + (obj.GetField("width") != null ? ", the file's is " + obj.GetField("width").intValue : "") + ") and were left out. Enlarge the board in the scene to play this map as made.");

            Dictionary<long, bool> fileAI = new Dictionary<long, bool>();
            Dictionary<long, string> fileProfile = new Dictionary<long, string>();
            if (obj.GetField("players")) {
                HashSet<long> inFile = new HashSet<long>();
                JSONObjectEnumerator players = obj.GetField("players").GetEnumerator();
                while (players.MoveNext()) {
                    PlayerData pd = JsonUtility.FromJson<PlayerData>(players.Current.Print());
                    fileAI[pd.id] = pd.AI;
                    fileProfile[pd.id] = pd.aiProfile ?? "";
                    inFile.Add(pd.id);
                    Player loaded = pd.load();
                    // The pre-game screen's faction for this side (before any unit is placed: units are placed as
                    // their side's faction fields them).
                    string faction;
                    if (loaded != null && FactionOverride != null && FactionOverride.TryGetValue(loaded.id, out faction))
                        loaded.faction = string.IsNullOrEmpty(faction) ? null : LevelEditor.FindFaction(faction);
                    // A scenario is the start of a game: everyone is in it, whatever the file says
                    // (a file saved after a side was knocked out keeps "eliminated":true, and that
                    // side's turns were then skipped for the rest of the game).
                    if (loaded != null) loaded.eliminated = false;
                }
                // The file's sides are the game's sides: a side of the scene's that the file does not have is out of
                // this game (put away, not destroyed: another file may bring it back by its id).
                if (inFile.Count > 0)
                    foreach (Player p in GameController.gc.players.ToArray())
                        if (p != null && !inFile.Contains(p.id)) { GameController.gc.players.Remove(p); p.gameObject.SetActive(false); }
                // (Who moves first: ApplyTestBlock, below.)
            }

            if (obj.GetField("buildings")) {
                JSONObjectEnumerator buildings = obj.GetField("buildings").GetEnumerator();
                while (buildings.MoveNext()) {
                    BuildingData bd = JsonUtility.FromJson<BuildingData>(buildings.Current.Print());
                    bd.load();
                }
            }

            // Transports first, then their cargo aboard them (UnitData.LoadAll: the file may list cargo first).
            List<UnitData> unitData = new List<UnitData>();
            if (obj.GetField("units")) {
                JSONObjectEnumerator units = obj.GetField("units").GetEnumerator();
                while (units.MoveNext()) unitData.Add(JsonUtility.FromJson<UnitData>(units.Current.Print()));
            }
            UnitData.LoadAll(unitData);

            string redKind = ApplyTestBlock(obj);
            ApplyControl(fileAI, fileProfile);

            GameController.BoardLoaded();

            if (RecordGames && GameController.gc != null && !GameController.gc.headless) {
                // A saved game goes on with its own log (cut back to where it was saved); anything else starts one.
                GameLog.SavePoint saved = SavePointOf(obj);
                if (saved == null || !GameLog.Continue(saved, path, redKind, null)) GameLog.Start(path, redKind, null);
                UnityEngine.Debug.Log("Recording this game to " + GameLog.Folder);
            }

            return true;
        }

        /// <summary>The keys a saved game keeps its game log under (GameLog.SavePoint).</summary>
        public const string LogFolderKey = "gameLog", LogLengthKey = "gameLogLength", LogTurnsKey = "gameLogTurns";

        /// <summary>Where a saved game's log was when it was saved, or null (a scenario, or a save made without a log).</summary>
        public static GameLog.SavePoint SavePointOf(JSONObject obj) {
            JSONObject folder = obj.GetField(LogFolderKey);
            if (folder == null || string.IsNullOrEmpty(folder.stringValue)) return null;
            JSONObject length = obj.GetField(LogLengthKey), turns = obj.GetField(LogTurnsKey);
            return new GameLog.SavePoint { folder = folder.stringValue, length = length != null ? length.longValue : 0, turns = turns != null ? turns.intValue : 0 };
        }

        /// <summary>A save's JSON with where its game log stands (so loading it goes on with that log).</summary>
        public static string WithSavePoint(string json, GameLog.SavePoint at) {
            if (at == null) return json;
            JSONObject obj = new JSONObject(json);
            obj.SetField(LogFolderKey, at.folder);
            obj.SetField(LogLengthKey, at.length);
            obj.SetField(LogTurnsKey, at.turns);
            return obj.Print(true);
        }

        /// <summary>The day (round, from 0) a file was saved on: its "day", 0 if it has none.</summary>
        public static int DayOf(JSONObject obj) {
            JSONObject d = obj != null ? obj.GetField("day") : null;
            return d != null && d.isNumber ? Mathf.Max(0, (int)d.intValue) : 0;
        }

        /// <summary>
        /// Who plays Red, overriding the file's test block: "Legacy", "Greedy", "Full" (this AI),
        /// "Human", or null to use the file. Set from HexTactica ▸ Red Opponent in the editor.
        /// </summary>
        public static string RedOverride;

        /// <summary>"standard", "attrition", "sandbox", or null to use the file. Set from HexTactica ▸ Rules.</summary>
        public static string RulesOverride;

        /// <summary>"on", "off", or null to use the file (else the rule set's default). Set from HexTactica ▸ Purchases.</summary>
        public static string PurchasesOverride;

        /// <summary>"on", "off", or null to use the file (fog of war; off when the file says nothing). Set from HexTactica ▸ Fog of War.</summary>
        public static string FogOverride;

        /// <summary>A setting from the test block, else from the top of the file, else null.</summary>
        static string Setting(JSONObject obj, JSONObject test, string key) {
            JSONObject f = test != null ? test.GetField(key) : null;
            if (f == null) f = obj.GetField(key);
            return f != null ? f.stringValue : null;
        }

        /// <summary>
        /// Red's opponent and who moves first, from a scenario file's test block. Blue (id 112) is
        /// always this AI (AIPlayer), set up exactly as the headless harness runs it: no mission
        /// objective (SampleScene's GoTo would otherwise pull every unit toward the centre).
        /// </summary>
        static string ApplyTestBlock(JSONObject obj) {
            if (GameController.gc == null) return null;
            JSONObject active = obj.GetField("active");
            if (active != null) {
                long id = active.longValue;
                GameController.gc.players.ForEach(pl => { if (pl != null && pl.id == id) GameController.gc.activePlayer = pl; });
            }
            JSONObject test = obj.GetField("test");
            string kind = RedOverride;
            if (string.IsNullOrEmpty(kind) && test != null) {
                JSONObject opponent = test.GetField("opponent");
                kind = opponent != null ? opponent.stringValue : null;
            }
            if (string.IsNullOrEmpty(kind)) kind = "Human";

            // Rules: the menu's choice, else the file's ("rules":"attrition"), else standard.
            string rules = RulesOverride;
            if (string.IsNullOrEmpty(rules) && test != null && test.GetField("rules") != null) rules = test.GetField("rules").stringValue;
            GameController.gc.rules = GameController.ParseRules(rules);
            // Purchases: the menu's choice, else the file's, else the rule set's default; and the
            // units the scenario allows to be bought (all when it names none).
            GameController.gc.ApplyPurchaseRules(PurchasesOverride ?? Setting(obj, test, "purchases"), Setting(obj, test, "allowedUnits"));
            // Fog of war: the menu's choice, else the file's ("fog":"on"), else off.
            GameController.gc.fogOfWar = GameController.ParseFog(FogOverride ?? Setting(obj, test, "fog"));
            // Faction special units: the pre-game screen's choice, else the file's, else on.
            string special = SpecialUnitsOverride ?? Setting(obj, test, "factionSpecialUnits");
            GameController.gc.factionSpecialUnits = string.IsNullOrEmpty(special) || GameController.ParseFog(special);
            FogOfWar.Reset();
            // The file's round count is the attrition limit (30 when it gives none).
            int limit;
            GameController.gc.roundLimit = test != null && test.GetField("rounds") != null && int.TryParse(test.GetField("rounds").stringValue, out limit) ? limit : 30;

            Player red = null, blue = null;
            GameController.gc.players.ForEach(pl => { if (pl != null && pl.id == 0) red = pl; if (pl != null && pl.id == 112) blue = pl; });

            if (blue != null) {
                AIPlayer ai = blue.GetComponent<AIPlayer>();
                if (ai == null) ai = blue.gameObject.AddComponent<AIPlayer>();
                ai.player = blue;
                ai.enabled = true;
                ai.objective = null;
                ai.ResetTurn();
                BaselineAIPlayer stray = blue.GetComponent<BaselineAIPlayer>();
                if (stray != null) Object.Destroy(stray);
            }

            if (red != null) {
                BaselineAIPlayer existing = red.GetComponent<BaselineAIPlayer>();
                if (existing != null) Object.Destroy(existing);
                AIPlayer redAI = red.GetComponent<AIPlayer>();
                if (kind == "Legacy" || kind == "Greedy") {
                    if (redAI != null) Object.Destroy(redAI);
                    BaselineAIPlayer b = red.gameObject.AddComponent<BaselineAIPlayer>();
                    b.player = red;
                    b.kind = kind == "Legacy" ? AIKind.Legacy : AIKind.Greedy;
                } else if (kind == "Full") {
                    if (redAI == null) redAI = red.gameObject.AddComponent<AIPlayer>();
                    redAI.player = red;
                    redAI.enabled = true;
                    redAI.objective = null;
                    redAI.ResetTurn();
                } else if (redAI != null) {
                    Object.Destroy(redAI);   // Human: nobody plays Red but you
                }
            }
            string q = "";
            if (test != null && test.GetField("question") != null) q = test.GetField("question").stringValue + " ";
            UnityEngine.Debug.Log("Scenario: " + q + "(Blue: this AI; Red: " + (kind == "Full" ? "this AI" : kind) + "; rules: " + GameController.gc.rules + ")");
            return kind;
        }

        /// <summary>"on", "off", or null to use the file (faction special units; on when the file says nothing). Set by the pre-game screen.</summary>
        public static string SpecialUnitsOverride;

        /// <summary>
        /// Who plays each side, by player id: true an AI (this AI, AIPlayer), false a human. Set by the pre-game screen;
        /// null: as the file's test block says (Blue this AI, Red the file's opponent). Applied after the test block.
        /// </summary>
        public static Dictionary<long, bool> ControlOverride;

        /// <summary>The AI profile for each side an AI plays, by player id (an AIProfile's profileName; "" the default). Set by the pre-game screen with ControlOverride.</summary>
        public static Dictionary<long, string> ProfileOverride;

        /// <summary>Who plays each side as the file says (each player's "AI"): a saved game goes on as it was played. Set when loading a save.</summary>
        public static bool ControlFromFile;

        /// <summary>Each side's faction by player id (a Resources/Factions asset name; "" none). Set by the pre-game screen; null: the file's.</summary>
        public static Dictionary<long, string> FactionOverride;

        /// <summary>The pre-game screen's or a save's choice of who plays each side (ControlOverride, ControlFromFile), over the test block's.</summary>
        static void ApplyControl(Dictionary<long, bool> fileAI, Dictionary<long, string> fileProfile) {
            if (GameController.gc == null || (ControlOverride == null && !ControlFromFile)) return;
            foreach (Player p in GameController.gc.players.ToArray()) {
                if (p == null) continue;
                bool ai; string profile;
                if (ControlOverride != null && ControlOverride.TryGetValue(p.id, out ai)) {
                    if (ProfileOverride == null || !ProfileOverride.TryGetValue(p.id, out profile)) profile = "";
                    SetControl(p, ai, profile);
                } else if (ControlFromFile && fileAI.TryGetValue(p.id, out ai)) {
                    if (!fileProfile.TryGetValue(p.id, out profile)) profile = "";
                    SetControl(p, ai, profile);
                }
            }
        }

        /// <summary>
        /// <paramref name="p"/> played by this AI (AIPlayer, set up as the harness runs it: no mission objective) with the
        /// profile named <paramref name="profile"/> (FrontEndSettings.aiProfiles; "" or unknown: the default), or by a
        /// human: every AI on it switched off and removed.
        /// </summary>
        public static void SetControl(Player p, bool ai, string profile = "") {
            if (p == null) return;
            AIPlayer full = p.GetComponent<AIPlayer>();
            foreach (MonoBehaviour m in p.GetComponents<MonoBehaviour>()) {
                if (m == null || m == full) continue;
                if (m is BaselineAIPlayer) { m.enabled = false; Object.Destroy(m); }
                else if (m is IAIController) m.enabled = false;
            }
            if (ai) {
                if (full == null) full = p.gameObject.AddComponent<AIPlayer>();
                full.player = p;
                full.enabled = true;
                full.objective = null;
                full.profile = FrontEndSettings.Get.Profile(profile);
                full.ResetTurn();
            } else if (full != null) {
                full.enabled = false;
                Object.Destroy(full);
            }
        }

        /// <summary>Record every game played in play mode (GameLog): the scene's board, a loaded scenario, or a loaded save. On by default.</summary>
        public static bool RecordGames = true;

        /// <summary>Build a Scenario descriptor for a save file, for reporting purposes.</summary>
        public Scenario Describe(string fileName) {
            Scenario s = ScriptableObject.CreateInstance<Scenario>();
            s.scenarioName = Path.GetFileNameWithoutExtension(fileName);
            s.turnLimit = turnLimit;
            s.question = "Loaded from " + fileName;

            if (GameController.gc != null) {
                for (int i = 0; i < GameController.gc.players.Count; i++) {
                    Player p = GameController.gc.players[i];
                    if (p == null) continue;
                    ScenarioSide side = new ScenarioSide { playerName = p.personal_name };
                    BaselineAIPlayer baseline = p.GetComponent<BaselineAIPlayer>();
                    AIPlayer full = p.GetComponent<AIPlayer>();
                    side.ai = baseline != null ? baseline.kind : AIKind.Full;
                    if (full != null) side.profile = full.profile;
                    s.sides.Add(side);
                }
            }
            return s;
        }

        /// <summary>Load each save file in turn, run it, and collect the results.</summary>
        public List<ScenarioResult> RunAll(BatchRunner runner) {
            List<ScenarioResult> all = new List<ScenarioResult>();
            for (int i = 0; i < saveFiles.Count; i++) {
                string file = saveFiles[i];
                if (!LoadBoard(file)) continue;
                Scenario s = Describe(file);
                ScenarioResult r = runner.Run(s);
                all.Add(r);
                runner.Record(r);
                UnityEngine.Debug.Log(r.Summary());
            }
            return all;
        }
    
        /// <summary>Every tile of the board to Water, before a file's tiles are applied.</summary>
        /// <summary>
        /// Make the scene's board the file's size (HexGenerator.SetWidth), bigger or smaller: its
        /// "width", and never too small for its farthest tile. True if the board changed.
        /// </summary>
        public static bool FitBoard(JSONObject obj) {
            if (HexGenerator.hg == null || obj == null) return false;
            int need = obj.GetField("width") != null ? (int)obj.GetField("width").intValue : 0;
            JSONObject tiles = obj.GetField("tiles");
            if (tiles != null) {
                JSONObjectEnumerator e = tiles.GetEnumerator();
                while (e.MoveNext()) {
                    TileData td = JsonUtility.FromJson<TileData>(e.Current.Print());
                    int far = Mathf.Max(Mathf.Abs(td.x), Mathf.Max(Mathf.Abs(td.y), Mathf.Abs(td.z)));
                    need = Mathf.Max(need, 2 * far + 1);
                }
            }
            if (need <= 0) return false;
            int was = HexGenerator.hg.width;
            if (!HexGenerator.hg.SetWidth(need)) return false;
            UnityEngine.Debug.Log("Scenario: the board is now " + need + " wide (was " + was + "), as this map was made.");
            return true;
        }

        public static void FloodWater() {
            if (HexGenerator.hg == null || LevelEditor.singleton == null) return;
            TerrainData water = LevelEditor.loadTerrain("Water");
            if (water == null) return;
            HexGenerator.forAllTiles(t => { t.loadData(water); return 0; });
        }
    }
}
