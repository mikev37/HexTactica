using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A campaign mission's post-game screen (the MissionResult scene, loaded over the game when it ends, in place of the
/// PostGame scene): won or lost, the scenario's own words for it, and the result against the mission's three pars —
/// speed, aggression, defense — with the stars it earned gained one after the other. The result is read from the game
/// log (GameRecord), as the post-game report is, and kept in the campaign's progress as the screen opens; Return
/// leads to the campaign screen, where the flag gains its new stars and what was unlocked appears.
/// </summary>
public class MissionResultScreen : MonoBehaviour {

    /// <summary>One par's line: what it is, how the player did against it, and its star.</summary>
    [System.Serializable]
    public class ParRow {
        [Tooltip("How the player did against the par.")]
        public TMP_Text result;
        public StarPip star;
        [Tooltip("Hidden when the mission has no such par (a mission with no speed star). Optional.")]
        public GameObject row;
    }

    public TMP_Text title;
    public TMP_Text headline;
    public string wonHeadline = "Victory";
    public string lostHeadline = "Defeat";
    [Tooltip("The scenario's own words for how it went (its win or loss text).")]
    public TMP_Text said;

    [Header("Rating")]
    public ParRow speed = new ParRow();
    public ParRow aggression = new ParRow();
    public ParRow defense = new ParRow();
    [Tooltip("Speed: {0} the player's turns, {1} the par.")]
    public string speedFormat = "Speed: {0} turns (par {1})";
    [Tooltip("Aggression: {0} enemy units destroyed, {1} the par.")]
    public string aggressionFormat = "Aggression: {0} enemy units destroyed (par {1})";
    [Tooltip("Defense: {0} of the player's units lost, {1} the par.")]
    public string defenseFormat = "Defense: {0} units lost (par {1})";
    [Tooltip("How many stars the attempt earned, and what that changed: {0} earned now, {1} the mission's stars in all.")]
    public TMP_Text summary;
    public string summaryWon = "{0} of 3 stars this time; {1} of 3 earned in all.";
    public string summaryLost = "No stars for a mission lost. Its stars so far: {1} of 3.";
    [Tooltip("Shown in place of the summary when the game has no game log (recording was turned off): the win counts, without stars.")]
    public string notRecorded = "This game was not recorded (no game log), so its stars could not be judged.";
    [Tooltip("Seconds before the first star, and between one star and the next.")]
    public float firstStarDelay = 0.8f, starInterval = 0.5f;
    [Tooltip("Off until the stars have been shown.")]
    public Button returnButton;

    void Awake() { TransportPanel.EnsureEventSystem(); }

    void Start() {
        Mission m = MatchSetup.Mission;
        GameController gc = GameController.gc;
        GameRecord record = GameRecord.Read(HexTactica.Harness.GameLog.Folder);
        Player me = FogOfWar.Viewer;
        if (me == null && gc != null && gc.players != null) foreach (Player p in gc.players) if (p != null && FogOfWar.IsHuman(p)) { me = p; break; }
        bool won = gc != null && gc.Won(me);   // the winner, or one of its allies
        MissionTally tally = MissionRating.Tally(record, me != null ? me.id : GameInsights.ViewerId(record), won);
        CampaignProgress.Outcome o = CampaignProgress.Finish(m, tally);

        if (title != null) title.text = m != null ? m.Title : "";
        if (headline != null) headline.text = won ? wonHeadline : lostHeadline;
        if (said != null) said.text = gc != null ? ScenarioScript.EndText(won) : "";
        int parTurns = m != null ? m.parTurns : 0, parKills = m != null ? m.parKills : 0, parLosses = m != null ? m.parLosses : 0;
        Fill(speed, tally.recorded && parTurns > 0, string.Format(speedFormat, tally.turns, parTurns));
        Fill(aggression, tally.recorded, string.Format(aggressionFormat, tally.kills, parKills));
        Fill(defense, tally.recorded, string.Format(defenseFormat, tally.losses, parLosses));
        if (summary != null)
            summary.text = !tally.recorded ? notRecorded
                : string.Format(won ? summaryWon : summaryLost, MissionRating.Count(o.earned), MissionRating.Count(CampaignProgress.Get(m).stars));
        if (returnButton != null) returnButton.interactable = false;
        StartCoroutine(ShowStars(o.earned));
    }

    static void Fill(ParRow r, bool shown, string text) {
        if (r.row != null) r.row.SetActive(shown);
        if (r.result != null) r.result.text = shown ? text : "";
        if (r.star != null) r.star.Show(false);
    }

    /// <summary>The stars earned, one after the other; then Return is offered.</summary>
    IEnumerator ShowStars(MissionStars earned) {
        ParRow[] rows = { speed, aggression, defense };
        if (earned != MissionStars.None && firstStarDelay > 0f) yield return new WaitForSecondsRealtime(firstStarDelay);
        for (int i = 0; i < rows.Length; i++) {
            if (rows[i].star == null || !MissionRating.Has(earned, MissionRating.Each[i])) continue;
            yield return rows[i].star.Gain();
            if (starInterval > 0f) yield return new WaitForSecondsRealtime(starInterval);
        }
        if (returnButton != null) returnButton.interactable = true;
    }

    /// <summary>Back to the campaign screen, where the result is played out on the map.</summary>
    public void ReturnToCampaign() { MatchSetup.ToCampaign(); }

    /// <summary>Close the screen and leave the board to look at (the menu key opens the in-game menu; its Exit leads out).</summary>
    public void ViewBoard() { if (GameSession.Instance != null) GameSession.Instance.ClosePostGame(); }
}
