using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// The post-game screen (the PostGame scene, loaded over the game when it ends): who won, and what the game log
/// says (read into a GameRecord; GameInsights) — a page of findings and a page of each side's units. Main Menu leaves; View Board
/// closes the screen and leaves the board to look at; Play Again sets the same scenario up again.
/// </summary>
public class PostGameScreen : MonoBehaviour {
    public TMP_Text headline;
    public TMP_Text findings;
    public TMP_Text unitTables;
    public GameObject findingsPage;
    public GameObject unitsPage;
    [Tooltip("A finding's heading, before and after it (TextMeshPro rich text).")]
    public string titleStart = "<b><size=115%>", titleEnd = "</size></b>";
    [Tooltip("Shown when the game has no game log (recording was turned off).")]
    public string noStatistics = "This game was not recorded (no game log), so there is nothing to report.";

    void Awake() { TransportPanel.EnsureEventSystem(); }

    void Start() {
        // The report is read from the game log (GameLog.Folder), the one record of the game.
        GameRecord s = GameRecord.Read(HexTactica.Harness.GameLog.Folder);
        long viewer = GameInsights.ViewerId(s);
        if (headline != null) headline.text = s != null ? GameInsights.Headline(s, viewer) : noStatistics;
        if (findings != null) {
            if (s == null) findings.text = noStatistics;
            else {
                StringBuilder sb = new StringBuilder();
                foreach (GameInsights.Finding f in GameInsights.Findings(s, viewer))
                    sb.Append(titleStart).Append(f.title).Append(titleEnd).Append('\n').Append(f.body).Append("\n\n");
                findings.text = sb.Length > 0 ? sb.ToString() : noStatistics;
            }
        }
        if (unitTables != null) unitTables.text = s != null ? GameInsights.UnitTables(s, viewer, true) : noStatistics;
        source = s != null ? s.source : null;
        ShowFindings();
    }

    string source;

    public void ShowFindings() { if (findingsPage != null) findingsPage.SetActive(true); if (unitsPage != null) unitsPage.SetActive(false); }
    public void ShowUnits() { if (findingsPage != null) findingsPage.SetActive(false); if (unitsPage != null) unitsPage.SetActive(true); }

    public void MainMenu() { MatchSetup.ToMainMenu(); }

    public void ViewBoard() { if (GameSession.Instance != null) GameSession.Instance.ClosePostGame(); }

    /// <summary>The same scenario again, from the pre-game screen.</summary>
    public void PlayAgain() {
        if (!string.IsNullOrEmpty(source) && System.IO.File.Exists(source)) MatchSetup.SetUp(source);
        else MatchSetup.ToMainMenu();
    }
}
