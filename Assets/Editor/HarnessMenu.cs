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

    [MenuItem("HexTactica/Open Results Folder")]
    public static void OpenResults() {
        EditorUtility.RevealInFinder(Application.persistentDataPath);
    }
}
#endif
