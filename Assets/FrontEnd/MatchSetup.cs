using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using HexTactica.Harness;

/// <summary>What the game scene is opened for, from the main menu.</summary>
public enum SessionMode {
    /// <summary>The game scene was opened directly (in the editor): it plays its own board as it always did.</summary>
    None,
    /// <summary>A scenario, shown under the pre-game screen until the player starts it.</summary>
    Setup,
    /// <summary>A saved game, played on from where it was saved.</summary>
    Play,
    /// <summary>The map editor: a scenario (or the scene's board) to edit.</summary>
    Editor,
    /// <summary>A recorded game (a game log folder), watched again.</summary>
    Replay
}

/// <summary>
/// The hand-over between the menus and the game scene: which file to open and how, and the choices made on the
/// pre-game screen (who plays each side, factions, fog, purchases, faction special units). The game scene's
/// GameSession reads it when it opens; loading goes through ScenarioLoader with these choices as its overrides.
/// </summary>
public static class MatchSetup {

    public class Side {
        public long id;
        public string name;
        /// <summary>This AI plays the side; false: a human at the screen.</summary>
        public bool ai;
        /// <summary>The AI profile it plays with (an AIProfile's profileName, FrontEndSettings.aiProfiles); "" the AI's default.</summary>
        public string profile = "";
        /// <summary>The map makes the side its faction: the pre-game screen shows it and cannot change it.</summary>
        public bool factionLocked;
        /// <summary>A Resources/Factions asset name; "" no faction (the base units).</summary>
        public string faction = "";
    }

    public static SessionMode Mode;
    /// <summary>The campaign mission being played (null: an ordinary game): the briefing and result screens stand in for the pre-game and post-game ones, and the way out leads to the campaign screen.</summary>
    public static Mission Mission;
    /// <summary>The scenario or save being set up, played or edited ("" none: the map editor on the scene's own board).</summary>
    public static string FilePath = "";
    public static readonly List<Side> Sides = new List<Side>();
    public static bool Fog, Purchases = true, SpecialUnits = true;
    /// <summary>The rules the map fixes (GameController.fogFixed and the rest): the pre-game screen shows them, cannot change them, and the file's stand.</summary>
    public static bool FogFixed, PurchasesFixed, SpecialUnitsFixed;

    // ------------------------------------------------------------------ from the menus

    /// <summary>Open the game scene on a scenario, with the pre-game screen over it.</summary>
    public static void SetUp(string scenarioPath) { Open(SessionMode.Setup, scenarioPath); }

    /// <summary>Open the game scene on a saved game and play on.</summary>
    public static void PlaySave(string savePath) { Open(SessionMode.Play, savePath); }

    /// <summary>Open the game scene in the map editor, on a scenario (null: the scene's own board).</summary>
    public static void Edit(string scenarioPath) { Open(SessionMode.Editor, scenarioPath); }

    /// <summary>Open the game scene on a recorded game (its game log folder) to watch it again.</summary>
    public static void Watch(string logFolder) { Open(SessionMode.Replay, logFolder); }

    /// <summary>
    /// Open the game scene on a campaign mission's scenario, with the mission's briefing over it. False (and nothing
    /// opened) when its scenario file is not in the campaign's folders.
    /// </summary>
    public static bool PlayMission(Mission mission) {
        string path = mission != null ? GameFiles.CampaignScenarioPath(mission.ScenarioName) : null;
        if (path == null) return false;
        Open(SessionMode.Setup, path, mission);
        return true;
    }

    /// <summary>Leave the game (or the main menu) for the campaign screen.</summary>
    public static void ToCampaign() { Leave(FrontEndSettings.Get.campaignScene); }

    static void Open(SessionMode mode, string path, Mission mission = null) {
        Mission = mission;
        Mode = mode;
        FilePath = path ?? "";
        GameController.Paused = mode != SessionMode.Play;
        SceneManager.LoadScene(FrontEndSettings.Get.gameScene, LoadSceneMode.Single);
    }

    /// <summary>Leave the game for the main menu.</summary>
    public static void ToMainMenu() { Leave(FrontEndSettings.Get.mainMenuScene); }

    static void Leave(string toScene) {
        Mission = null;
        Mode = SessionMode.None;
        FilePath = "";
        GameController.Paused = false;
        GameLog.Stop();
        Replay.Close();
        Replay.BuiltInPanel = true;
        SceneManager.LoadScene(toScene, LoadSceneMode.Single);
    }

    // ------------------------------------------------------------------ in the game scene

    /// <summary>The scenario as its file has it, to look at on the pre-game screen (the game is held meanwhile).</summary>
    public static bool LoadPreview() {
        return Load(FilePath, () => { }, false);
    }

    /// <summary>
    /// The scenario again, with the pre-game screen's choices (who plays each side, factions, fog, purchases, special
    /// units): the game starts from this board.
    /// </summary>
    public static bool StartGame() {
        bool ok = Load(FilePath, () => {
            Dictionary<long, bool> control = new Dictionary<long, bool>();
            Dictionary<long, string> factions = new Dictionary<long, string>();
            Dictionary<long, string> profiles = new Dictionary<long, string>();
            foreach (Side s in Sides) {
                control[s.id] = s.ai; profiles[s.id] = s.profile ?? "";
                if (!s.factionLocked) factions[s.id] = s.faction ?? "";
            }
            ScenarioLoader.ControlOverride = control;
            ScenarioLoader.ProfileOverride = profiles;
            ScenarioLoader.FactionOverride = factions;
            if (!FogFixed) ScenarioLoader.FogOverride = Fog ? "on" : "off";
            if (!PurchasesFixed) ScenarioLoader.PurchasesOverride = Purchases ? "on" : "off";
            if (!SpecialUnitsFixed) ScenarioLoader.SpecialUnitsOverride = SpecialUnits ? "on" : "off";
        });
        if (ok) { Mode = SessionMode.Play; GameController.Paused = false; }
        return ok;
    }

    /// <summary>A saved game: every side played as it was (the file's "AI" per side), its game log going on from where it was saved.</summary>
    public static bool LoadSave(string path) {
        bool ok = Load(path, () => { ScenarioLoader.ControlFromFile = true; ScenarioLoader.SidesAsFiled = true; });
        if (ok) { FilePath = path; Mission = MissionStamped(path); }
        return ok;
    }

    /// <summary>The key a saved game keeps its campaign mission under (the mission asset's name).</summary>
    public const string MissionKey = "mission";

    /// <summary>
    /// Note in the saved game at <paramref name="path"/> that it is the campaign mission being played, so loading it
    /// goes on as that mission (its result counts for the campaign). Nothing is written for an ordinary game.
    /// </summary>
    public static void StampMission(string path) {
        if (Mission == null || string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
        string text = System.IO.File.ReadAllText(path).TrimEnd();
        int end = text.LastIndexOf('}');
        if (end < 0) return;
        string key = Mission.Key.Replace("\\", "").Replace("\"", "");
        System.IO.File.WriteAllText(path, text.Substring(0, end).TrimEnd() + ",\n\"" + MissionKey + "\": \"" + key + "\"\n}\n");
    }

    /// <summary>The campaign mission a saved game was saved in (StampMission), or null.</summary>
    static Mission MissionStamped(string path) {
        try {
            Defective.JSON.JSONObject key = new Defective.JSON.JSONObject(System.IO.File.ReadAllText(path)).GetField(MissionKey);
            return key != null && key.isString ? Mission.Named(key.stringValue) : null;
        } catch (Exception) { return null; }
    }

    /// <summary>A scenario into the map editor: as the file has it, nobody playing.</summary>
    public static bool LoadForEditing(string path) {
        return Load(path, () => { }, false);
    }

    /// <summary>
    /// Load <paramref name="path"/> with the front end's choices only: the editor menu's overrides (HexTactica ▸ Rules,
    /// Fog, Purchases, Red Opponent) are set aside meanwhile and put back after, so a game started from the menus goes
    /// by its file and the pre-game screen.
    /// </summary>
    static bool Load(string path, Action choices, bool record = true) {
        if (string.IsNullOrEmpty(path)) return false;
        string red = ScenarioLoader.RedOverride, rules = ScenarioLoader.RulesOverride, buy = ScenarioLoader.PurchasesOverride,
               fog = ScenarioLoader.FogOverride, special = ScenarioLoader.SpecialUnitsOverride;
        Dictionary<long, bool> control = ScenarioLoader.ControlOverride;
        Dictionary<long, string> profilesWere = ScenarioLoader.ProfileOverride;
        Dictionary<long, string> factions = ScenarioLoader.FactionOverride;
        bool fromFile = ScenarioLoader.ControlFromFile, recording = ScenarioLoader.RecordGames;
        try {
            // A board only looked at (the pre-game screen's, the map editor's) is not a game: no game log for it.
            ScenarioLoader.RecordGames = recording && record;
            ScenarioLoader.RedOverride = null; ScenarioLoader.RulesOverride = null; ScenarioLoader.PurchasesOverride = null;
            ScenarioLoader.FogOverride = null; ScenarioLoader.SpecialUnitsOverride = null;
            ScenarioLoader.ControlOverride = null; ScenarioLoader.FactionOverride = null; ScenarioLoader.ControlFromFile = false;
            ScenarioLoader.ProfileOverride = null;
            choices();
            return ScenarioLoader.LoadBoardFromPath(path);
        } finally {
            ScenarioLoader.RedOverride = red; ScenarioLoader.RulesOverride = rules; ScenarioLoader.PurchasesOverride = buy;
            ScenarioLoader.FogOverride = fog; ScenarioLoader.SpecialUnitsOverride = special;
            ScenarioLoader.ControlOverride = control; ScenarioLoader.FactionOverride = factions; ScenarioLoader.ControlFromFile = fromFile;
            ScenarioLoader.ProfileOverride = profilesWere;
            ScenarioLoader.SidesAsFiled = false;
            ScenarioLoader.RecordGames = recording;
        }
    }

    /// <summary>The pre-game screen's starting choices: the board as loaded (who plays each side, factions, fog, purchases).</summary>
    public static void ReadFromBoard() {
        Sides.Clear();
        GameController gc = GameController.gc;
        if (gc == null) return;
        foreach (Player p in gc.players) {
            if (p == null) continue;
            AIPlayer full = p.GetComponent<AIPlayer>();
            Sides.Add(new Side {
                id = p.id, name = p.personal_name, ai = AIControl.Of(p) != null, faction = p.faction != null ? p.faction.name : "",
                profile = full != null && full.profile != null ? full.profile.profileName : "", factionLocked = p.factionLocked
            });
        }
        Fog = gc.fogOfWar;
        Purchases = gc.purchasesAllowed;
        SpecialUnits = gc.factionSpecialUnits;
        FogFixed = gc.fogFixed;
        PurchasesFixed = gc.purchasesFixed;
        SpecialUnitsFixed = gc.specialUnitsFixed;
    }
}
