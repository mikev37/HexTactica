#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using HexTactica.Harness;

/// <summary>
/// Editor entry points for the batch harness.
///
/// Deliberately in the editor rather than a separate command-line tool: the loop you
/// want is "batch finds an anomaly, replay shows you the turn, the on-tile debug text
/// tells you which decision fired". Splitting the runner out of the editor breaks the
/// middle step, which is the one that actually saves time.
/// </summary>
public static class HarnessMenu {

    /// <summary>
    /// Plays every save file listed on the scene's ScenarioLoader to completion, headless,
    /// and writes one CSV row per side per game. Your playtest saves are the scenarios:
    /// build a map, press 's', add the file name to ScenarioLoader.saveFiles.
    /// Scenario assets listed directly on BatchRunner run too, against the board as loaded.
    /// </summary>
    [MenuItem("HexTactica/Run Batch")]
    public static void RunBatch() {
        if (!Application.isPlaying) {
            UnityEngine.Debug.LogError("Enter play mode first: the runner needs an initialised board.");
            return;
        }
        BatchRunner runner = Object.FindObjectOfType<BatchRunner>();
        if (runner == null) {
            UnityEngine.Debug.LogError("No BatchRunner in the open scene. Add one (and a ScenarioLoader listing your save files).");
            return;
        }

        runner.ClearResults();
        ScenarioLoader loader = Object.FindObjectOfType<ScenarioLoader>();
        if (loader != null && loader.saveFiles != null && loader.saveFiles.Count > 0) {
            loader.RunAll(runner);
        }
        if (runner.scenarios != null && runner.scenarios.Count > 0) {
            foreach (Scenario sc in runner.scenarios) {
                if (sc == null || !sc.IsValid) continue;
                runner.Record(runner.Run(sc));
            }
        }
        if (runner.Results.Count == 0) {
            UnityEngine.Debug.LogWarning("Nothing ran: list save files on a ScenarioLoader, or Scenario assets on the BatchRunner.");
            return;
        }

        string path = Path.Combine(Application.persistentDataPath, "harness_results.csv");
        File.WriteAllText(path, runner.ToCsv());
        UnityEngine.Debug.Log("Harness: wrote " + runner.Results.Count + " results to " + path);
    }

    /// <summary>
    /// Load a scenario file (Tests/Scenarios/*.dat, or any level-editor save) into the running
    /// scene. The file's test block sets Red's opponent AI and who moves first; Blue keeps the
    /// scene's AIPlayer. Watch it play, or play Red yourself if the opponent is "Human".
    /// </summary>
    [MenuItem("HexTactica/Load Scenario File...")]
    public static void LoadScenarioFile() {
        if (!Application.isPlaying) {
            UnityEngine.Debug.LogError("Enter play mode first: scenarios load onto the running board.");
            return;
        }
        string start = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Tests", "Scenarios");
        string path = EditorUtility.OpenFilePanel("Load scenario", Directory.Exists(start) ? start : Application.persistentDataPath, "dat");
        if (string.IsNullOrEmpty(path)) return;
        ScenarioLoader.RedOverride = RedChoice();
        ScenarioLoader.PurchasesOverride = string.IsNullOrEmpty(BuyChoice()) ? null : BuyChoice();
        string rules = EditorPrefs.GetString(RulesKey, "");
        ScenarioLoader.RulesOverride = string.IsNullOrEmpty(rules) ? null : rules;
        if (ScenarioLoader.LoadBoardFromPath(path)) UnityEngine.Debug.Log("Loaded scenario " + path);
    }

    /// <summary>
    /// Write a copy of a scenario file with the sides swapped (ScenarioMirror): Red gets Blue's
    /// army and buildings and Blue gets Red's, the same army still moving first. Saved beside the
    /// original as name-swapped.dat; in play mode it is loaded straight away.
    /// </summary>
    [MenuItem("HexTactica/Swap Sides of Scenario File...")]
    public static void SwapSides() {
        string start = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Tests", "Scenarios");
        string path = EditorUtility.OpenFilePanel("Swap sides of scenario", Directory.Exists(start) ? start : Application.persistentDataPath, "dat");
        if (string.IsNullOrEmpty(path)) return;
        string text = File.ReadAllText(path);
        string swapped = ScenarioMirror.SwapSides(text);
        if (swapped == text) { UnityEngine.Debug.LogError("Swap sides: " + path + " does not have exactly two players."); return; }
        string name = Path.GetFileNameWithoutExtension(path);
        string outPath = Path.Combine(Path.GetDirectoryName(path), (name.EndsWith("-swapped") ? name.Substring(0, name.Length - 8) : name + "-swapped") + ".dat");
        File.WriteAllText(outPath, swapped);
        UnityEngine.Debug.Log("Sides swapped: " + outPath);
        if (Application.isPlaying) {
            ScenarioLoader.RedOverride = RedChoice();
            ScenarioLoader.PurchasesOverride = string.IsNullOrEmpty(BuyChoice()) ? null : BuyChoice();
            string rules = EditorPrefs.GetString(RulesKey, "");
            ScenarioLoader.RulesOverride = string.IsNullOrEmpty(rules) ? null : rules;
            if (ScenarioLoader.LoadBoardFromPath(outPath)) UnityEngine.Debug.Log("Loaded scenario " + outPath);
        }
    }

    // ---- Rules for loaded scenarios: as the file says, standard, or attrition (HQs out of play,
    // win by elimination or by strength x cost left after the round limit).

    const string RulesKey = "HexTactica.Rules";
    const string RulesMenu = "HexTactica/Rules/";
    static string RulesChoice() { return EditorPrefs.GetString(RulesKey, ""); }
    static void SetRules(string v) { EditorPrefs.SetString(RulesKey, v ?? ""); UnityEngine.Debug.Log("Rules for loaded scenarios: " + (string.IsNullOrEmpty(v) ? "as the file says" : v) + ". Applies the next time you load a scenario file."); }
    [MenuItem(RulesMenu + "As the File Says", false, 0)] static void RulesFile() { SetRules(null); }
    [MenuItem(RulesMenu + "As the File Says", true)] static bool RulesFileCheck() { Menu.SetChecked(RulesMenu + "As the File Says", RulesChoice() == ""); return true; }
    [MenuItem(RulesMenu + "Standard (HQ)", false, 1)] static void RulesStd() { SetRules("standard"); }
    [MenuItem(RulesMenu + "Standard (HQ)", true)] static bool RulesStdCheck() { Menu.SetChecked(RulesMenu + "Standard (HQ)", RulesChoice() == "standard"); return true; }
    [MenuItem(RulesMenu + "Attrition (no HQ attacks, 30 rounds)", false, 2)] static void RulesAtt() { SetRules("attrition"); }
    [MenuItem(RulesMenu + "Attrition (no HQ attacks, 30 rounds)", true)] static bool RulesAttCheck() { Menu.SetChecked(RulesMenu + "Attrition (no HQ attacks, 30 rounds)", RulesChoice() == "attrition"); return true; }
    [MenuItem(RulesMenu + "Sandbox (never ends)", false, 3)] static void RulesSandbox() { SetRules("sandbox"); }
    [MenuItem(RulesMenu + "Sandbox (never ends)", true)] static bool RulesSandboxCheck() { Menu.SetChecked(RulesMenu + "Sandbox (never ends)", RulesChoice() == "sandbox"); return true; }

    // ---- Purchases for loaded scenarios: as the file says (else the rules' default), on, or off.
    const string BuyKey = "HexTactica.Purchases";
    const string BuyMenu = "HexTactica/Purchases/";
    static string BuyChoice() { return EditorPrefs.GetString(BuyKey, ""); }
    static void SetBuy(string v) {
        EditorPrefs.SetString(BuyKey, v ?? "");
        ScenarioLoader.PurchasesOverride = string.IsNullOrEmpty(v) ? null : v;
        UnityEngine.Debug.Log("Purchases for loaded scenarios: " + (string.IsNullOrEmpty(v) ? "as the file says" : v) + ". Applies the next time you load a scenario file.");
    }
    [MenuItem(BuyMenu + "As the File Says", false, 0)] static void BuyFile() { SetBuy(null); }
    [MenuItem(BuyMenu + "As the File Says", true)] static bool BuyFileCheck() { Menu.SetChecked(BuyMenu + "As the File Says", BuyChoice() == ""); return true; }
    [MenuItem(BuyMenu + "Allowed", false, 1)] static void BuyOn() { SetBuy("on"); }
    [MenuItem(BuyMenu + "Allowed", true)] static bool BuyOnCheck() { Menu.SetChecked(BuyMenu + "Allowed", BuyChoice() == "on"); return true; }
    [MenuItem(BuyMenu + "Not Allowed", false, 2)] static void BuyOff() { SetBuy("off"); }
    [MenuItem(BuyMenu + "Not Allowed", true)] static bool BuyOffCheck() { Menu.SetChecked(BuyMenu + "Not Allowed", BuyChoice() == "off"); return true; }

    // ---- Who plays Red when a scenario loads. Blue (id 112) is always this AI.

    const string RedKey = "HexTactica.RedOpponent";
    const string RedMenu = "HexTactica/Red Opponent/";

    static string RedChoice() {
        string v = EditorPrefs.GetString(RedKey, "");
        return string.IsNullOrEmpty(v) ? null : v;
    }

    static void SetRed(string v) {
        EditorPrefs.SetString(RedKey, v ?? "");
        UnityEngine.Debug.Log("Red opponent for loaded scenarios: " + (v == null ? "as the file says" : v == "Full" ? "this AI" : v)
                              + ". Applies the next time you load a scenario file.");
    }

    [MenuItem(RedMenu + "As the File Says", false, 0)] static void RedFile() { SetRed(null); }
    [MenuItem(RedMenu + "As the File Says", true)] static bool RedFileCheck() { Menu.SetChecked(RedMenu + "As the File Says", RedChoice() == null); return true; }
    [MenuItem(RedMenu + "This AI (mirror match)", false, 1)] static void RedFull() { SetRed("Full"); }
    [MenuItem(RedMenu + "This AI (mirror match)", true)] static bool RedFullCheck() { Menu.SetChecked(RedMenu + "This AI (mirror match)", RedChoice() == "Full"); return true; }
    [MenuItem(RedMenu + "Legacy AI", false, 2)] static void RedLegacy() { SetRed("Legacy"); }
    [MenuItem(RedMenu + "Legacy AI", true)] static bool RedLegacyCheck() { Menu.SetChecked(RedMenu + "Legacy AI", RedChoice() == "Legacy"); return true; }
    [MenuItem(RedMenu + "Greedy (defend-only)", false, 3)] static void RedGreedy() { SetRed("Greedy"); }
    [MenuItem(RedMenu + "Greedy (defend-only)", true)] static bool RedGreedyCheck() { Menu.SetChecked(RedMenu + "Greedy (defend-only)", RedChoice() == "Greedy"); return true; }
    [MenuItem(RedMenu + "Human (you play Red)", false, 4)] static void RedHuman() { SetRed("Human"); }
    [MenuItem(RedMenu + "Human (you play Red)", true)] static bool RedHumanCheck() { Menu.SetChecked(RedMenu + "Human (you play Red)", RedChoice() == "Human"); return true; }

    // ---- Game logs: every game started from a scenario file is recorded (GameLog).

    [MenuItem("HexTactica/Game Log/Start Recording Now")]
    public static void StartRecording() {
        if (!Application.isPlaying) { UnityEngine.Debug.LogError("Enter play mode first."); return; }
        GameLog.Start("board as it was when recording started", null, null);
        UnityEngine.Debug.Log("Recording to " + GameLog.Folder);
    }

    [MenuItem("HexTactica/Game Log/Stop Recording")]
    public static void StopRecording() {
        string folder = GameLog.Folder;
        GameLog.Stop();
        UnityEngine.Debug.Log("Recording stopped" + (folder != null ? ": " + folder : "."));
    }

    [MenuItem("HexTactica/Game Log/Record Loaded Scenarios")]
    static void ToggleRecord() { ScenarioLoader.RecordGames = !ScenarioLoader.RecordGames; }
    [MenuItem("HexTactica/Game Log/Record Loaded Scenarios", true)]
    static bool ToggleRecordCheck() { Menu.SetChecked("HexTactica/Game Log/Record Loaded Scenarios", ScenarioLoader.RecordGames); return true; }

    [MenuItem("HexTactica/Game Log/Open Logs Folder")]
    public static void OpenLogs() {
        string target = GameLog.Folder ?? GameLog.DefaultRoot;
        Directory.CreateDirectory(target);
        EditorUtility.RevealInFinder(target);
    }

    [MenuItem("HexTactica/Open Results Folder")]
    public static void OpenResults() {
        EditorUtility.RevealInFinder(Application.persistentDataPath);
    }
}
#endif
