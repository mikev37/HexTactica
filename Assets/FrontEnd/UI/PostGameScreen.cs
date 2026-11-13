using System.Collections.Generic;
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
    [Tooltip("Under the tables: what the trade column means.")]
    public TMP_Text unitTables;
    [Tooltip("Where each side's units table goes (a vertical layout): a heading, then a row per kind of unit.")]
    public RectTransform unitRows;
    public UnitTableHeading tableHeading;
    public UnitTableRow tableRow;
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
        // The winning side as the game has it (the winner and its allies).
        GameController gc = GameController.gc;
        List<long> side = null;
        if (gc != null && gc.gameOver && gc.winner != null) { side = new List<long>(); foreach (Player p in gc.players) if (p != null && gc.Won(p)) side.Add(p.id); }
        if (headline != null) headline.text = s != null ? GameInsights.Headline(s, viewer, side) : noStatistics;
        // The scenario's own words for how it went (its win or loss text), above the findings.
        Player me = FogOfWar.Viewer;
        if (me == null && gc != null && gc.players != null) foreach (Player p in gc.players) if (p != null && FogOfWar.IsHuman(p)) { me = p; break; }
        string said = gc != null ? ScenarioScript.EndText(gc.Won(me)) : "";
        string top = string.IsNullOrEmpty(said) ? "" : said + "\n\n";
        if (findings != null) {
            if (s == null) findings.text = top + noStatistics;
            else {
                StringBuilder sb = new StringBuilder(top);
                foreach (GameInsights.Finding f in GameInsights.Findings(s, viewer))
                    sb.Append(titleStart).Append(f.title).Append(titleEnd).Append('\n').Append(f.body).Append("\n\n");
                findings.text = sb.Length > top.Length ? sb.ToString() : top + noStatistics;
            }
        }
        ShowTables(s, viewer);
        source = s != null ? s.source : null;
        ShowFindings();
    }

    string source;

    /// <summary>A heading and rows per side (copies of the table prefabs), then the note on trade.</summary>
    void ShowTables(GameRecord s, long viewer) {
        if (unitTables != null) unitTables.text = s != null ? GameInsights.TradeNote : noStatistics;
        if (s == null || unitRows == null || tableRow == null) return;
        foreach (GameInsights.UnitTable t in GameInsights.UnitTablesOf(s, viewer)) {
            if (tableHeading != null) {
                UnitTableHeading h = Instantiate(tableHeading, unitRows);
                if (h.title != null) h.title.text = t.title;
            }
            foreach (string[] r in t.rows) Instantiate(tableRow, unitRows).Show(r);
        }
        if (unitTables != null && unitTables.transform.parent == unitRows) unitTables.transform.SetAsLastSibling();
    }

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
