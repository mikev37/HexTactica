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
        /// Restore the board to a saved state. Mirrors LevelEditor.Load, but without
        /// depending on keyboard input, and clears buildings as well as units.
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
                GameController.gc.days = 0;
                GameController.gc.players.ForEach(p => {
                    if (p == null) return;
                    p.eliminated = false;
                    p.unitTurnsOutOfSupply = 0;
                    p.unitsLostToAttrition = 0;
                    p.hubsStranded = 0;
                });
            }

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

            if (obj.GetField("players")) {
                JSONObjectEnumerator players = obj.GetField("players").GetEnumerator();
                while (players.MoveNext()) {
                    PlayerData pd = JsonUtility.FromJson<PlayerData>(players.Current.Print());
                    Player loaded = pd.load();
                    // A scenario is the start of a game: everyone is in it, whatever the file says
                    // (a file saved after a side was knocked out keeps "eliminated":true, and that
                    // side's turns were then skipped for the rest of the game).
                    if (loaded != null) loaded.eliminated = false;
                }
                JSONObject active = obj.GetField("active");
                if (active != null && GameController.gc != null) {
                    long activeId = active.longValue;
                    GameController.gc.players.ForEach(pl => {
                        if (pl.id == activeId) GameController.gc.activePlayer = pl;
                    });
                }
            }

            if (obj.GetField("buildings")) {
                JSONObjectEnumerator buildings = obj.GetField("buildings").GetEnumerator();
                while (buildings.MoveNext()) {
                    BuildingData bd = JsonUtility.FromJson<BuildingData>(buildings.Current.Print());
                    bd.load();
                }
            }

            List<UnitData> unitData = new List<UnitData>();
            List<Unit> made = new List<Unit>();   // by position in the file (null: an HQ that became a building)
            if (obj.GetField("units")) {
                JSONObjectEnumerator units = obj.GetField("units").GetEnumerator();
                while (units.MoveNext()) {
                    UnitData ud = JsonUtility.FromJson<UnitData>(units.Current.Print());
                    unitData.Add(ud);
                    made.Add(ud.load());
                }
            }

            string redKind = ApplyTestBlock(obj);

            // Second pass: re-establish transport links now that every unit exists.
            for (int i = 0; i < unitData.Count; i++) {
                int idx = unitData[i].transporterIndexPlusOne - 1;
                if (idx < 0 || idx >= made.Count) continue;
                Unit cargo = made[i];
                Unit transport = made[idx];
                if (cargo != null && transport != null) Transporting.Load(transport, cargo);
            }

            if (RecordGames && GameController.gc != null && !GameController.gc.headless) {
                GameLog.Start(path, redKind, null);
                UnityEngine.Debug.Log("Recording this game to " + GameLog.Folder);
            }

            return true;
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

        /// <summary>Record games started by loading a scenario file (GameLog). On by default.</summary>
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
        public static void FloodWater() {
            if (HexGenerator.hg == null || LevelEditor.singleton == null) return;
            TerrainData water = LevelEditor.loadTerrain("Water");
            if (water == null) return;
            HexGenerator.forAllTiles(t => { t.loadData(water); return 0; });
        }
    }
}
