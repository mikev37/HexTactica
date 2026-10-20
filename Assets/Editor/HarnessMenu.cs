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
        if (ScenarioLoader.LoadBoardFromPath(path)) UnityEngine.Debug.Log("Loaded scenario " + path);
    }

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

    [MenuItem("HexTactica/Open Results Folder")]
    public static void OpenResults() {
        EditorUtility.RevealInFinder(Application.persistentDataPath);
    }
}
#endif
