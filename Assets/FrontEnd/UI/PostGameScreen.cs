using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// The post-game screen (the PostGame scene, loaded over the game when it ends): who won, and what the game's
/// statistics say (GameInsights) — a page of findings and a page of each side's units. Main Menu leaves; View Board
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
    [Tooltip("Shown when there are no statistics (the game was not started from a file).")]
    public string noStatistics = "No statistics were recorded for this game.";

    void Awake() { TransportPanel.EnsureEventSystem(); }

    void Start() {
        GameStats s = GameStats.Current;
        long viewer = GameInsights.ViewerId();
        if (headline != null) headline.text = GameInsights.Headline(s, viewer);
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
        ShowFindings();
    }

    public void ShowFindings() { if (findingsPage != null) findingsPage.SetActive(true); if (unitsPage != null) unitsPage.SetActive(false); }
    public void ShowUnits() { if (findingsPage != null) findingsPage.SetActive(false); if (unitsPage != null) unitsPage.SetActive(true); }

    public void MainMenu() { MatchSetup.ToMainMenu(); }

    public void ViewBoard() { if (GameSession.Instance != null) GameSession.Instance.ClosePostGame(); }

    /// <summary>The same scenario again, from the pre-game screen.</summary>
    public void PlayAgain() {
        GameStats s = GameStats.Current;
        if (s != null && System.IO.File.Exists(s.source)) MatchSetup.SetUp(s.source);
        else MatchSetup.ToMainMenu();
    }
}
