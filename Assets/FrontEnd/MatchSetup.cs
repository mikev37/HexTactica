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
    Editor
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
        /// <summary>A Resources/Factions asset name; "" no faction (the base units).</summary>
        public string faction = "";
    }

    public static SessionMode Mode;
    /// <summary>The scenario or save being set up, played or edited ("" none: the map editor on the scene's own board).</summary>
    public static string FilePath = "";
    public static readonly List<Side> Sides = new List<Side>();
    public static bool Fog, Purchases = true, SpecialUnits = true;

    // ------------------------------------------------------------------ from the menus

    /// <summary>Open the game scene on a scenario, with the pre-game screen over it.</summary>
    public static void SetUp(string scenarioPath) { Open(SessionMode.Setup, scenarioPath); }

    /// <summary>Open the game scene on a saved game and play on.</summary>
    public static void PlaySave(string savePath) { Open(SessionMode.Play, savePath); }

    /// <summary>Open the game scene in the map editor, on a scenario (null: the scene's own board).</summary>
    public static void Edit(string scenarioPath) { Open(SessionMode.Editor, scenarioPath); }

    static void Open(SessionMode mode, string path) {
        Mode = mode;
        FilePath = path ?? "";
        GameController.Paused = mode == SessionMode.Setup || mode == SessionMode.Editor;
        SceneManager.LoadScene(FrontEndSettings.Get.gameScene, LoadSceneMode.Single);
    }

    /// <summary>Leave the game for the main menu.</summary>
    public static void ToMainMenu() {
        Mode = SessionMode.None;
        FilePath = "";
        GameController.Paused = false;
        GameStats.Stop();
        GameLog.Stop();
        SceneManager.LoadScene(FrontEndSettings.Get.mainMenuScene, LoadSceneMode.Single);
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
            foreach (Side s in Sides) { control[s.id] = s.ai; factions[s.id] = s.faction ?? ""; }
            ScenarioLoader.ControlOverride = control;
            ScenarioLoader.FactionOverride = factions;
            ScenarioLoader.FogOverride = Fog ? "on" : "off";
            ScenarioLoader.PurchasesOverride = Purchases ? "on" : "off";
            ScenarioLoader.SpecialUnitsOverride = SpecialUnits ? "on" : "off";
        });
        if (ok) { Mode = SessionMode.Play; GameController.Paused = false; }
        return ok;
    }

    /// <summary>A saved game: every side played as it was (the file's "AI" per side), the statistics kept beside it.</summary>
    public static bool LoadSave(string path) {
        bool ok = Load(path, () => { ScenarioLoader.ControlFromFile = true; });
        if (ok) { FilePath = path; GameStats.LoadBeside(path); }
        return ok;
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
        Dictionary<long, string> factions = ScenarioLoader.FactionOverride;
        bool fromFile = ScenarioLoader.ControlFromFile, recording = ScenarioLoader.RecordGames;
        try {
            // A board only looked at (the pre-game screen's, the map editor's) is not a game: no game log for it.
            ScenarioLoader.RecordGames = recording && record;
            ScenarioLoader.RedOverride = null; ScenarioLoader.RulesOverride = null; ScenarioLoader.PurchasesOverride = null;
            ScenarioLoader.FogOverride = null; ScenarioLoader.SpecialUnitsOverride = null;
            ScenarioLoader.ControlOverride = null; ScenarioLoader.FactionOverride = null; ScenarioLoader.ControlFromFile = false;
            choices();
            return ScenarioLoader.LoadBoardFromPath(path);
        } finally {
            ScenarioLoader.RedOverride = red; ScenarioLoader.RulesOverride = rules; ScenarioLoader.PurchasesOverride = buy;
            ScenarioLoader.FogOverride = fog; ScenarioLoader.SpecialUnitsOverride = special;
            ScenarioLoader.ControlOverride = control; ScenarioLoader.FactionOverride = factions; ScenarioLoader.ControlFromFile = fromFile;
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
            Sides.Add(new Side { id = p.id, name = p.personal_name, ai = AIControl.Of(p) != null, faction = p.faction != null ? p.faction.name : "" });
        }
        Fog = gc.fogOfWar;
        Purchases = gc.purchasesAllowed;
        SpecialUnits = gc.factionSpecialUnits;
    }
}
