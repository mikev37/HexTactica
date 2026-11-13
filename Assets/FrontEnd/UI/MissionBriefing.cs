using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A campaign mission's pre-game screen (the MissionBrief scene, over the game scene with the mission's board shown
/// and held, in place of the PreGame scene): the mission's introduction, its objectives and its pars. There is
/// nothing to choose: who plays each side, the factions and the rules are the scenario file's. Start counts the
/// mission as attempted (closing the missions it blocks) and lets it play; Back returns to the campaign screen.
/// </summary>
public class MissionBriefing : MonoBehaviour {
    public TMP_Text title;
    [Tooltip("Whose point of view the mission is told from.")]
    public TMP_Text faction;
    [Tooltip("Tinted with the faction's flag colour. Optional.")]
    public Graphic flag;
    [Tooltip("The mission's introduction (Mission.briefing).")]
    public TMP_Text briefing;
    [Tooltip("What the player has to do (Mission.objectives).")]
    public TMP_Text objectives;
    [Tooltip("The mission's pars.")]
    public TMP_Text pars;
    public Button startButton;
    [Tooltip("The flag colour of a mission with no faction.")]
    public Color noFactionColor = Color.white;
    [Tooltip("The pars: {0} turns, {1} enemy units to destroy, {2} units that may be lost.")]
    [TextArea(2, 5)] public string parsFormat = "Speed: win within {0} turns\nAggression: destroy at least {1} enemy units\nDefense: lose no more than {2} units";
    [Tooltip("The pars of a mission with no speed star (Par Turns 0).")]
    [TextArea(2, 5)] public string parsFormatNoSpeed = "Aggression: destroy at least {1} enemy units\nDefense: lose no more than {2} units";
    [Tooltip("Shown in place of the introduction when the scenario could not be loaded: {0} the file.")]
    public string couldNotLoad = "Could not load {0}";

    void Awake() { TransportPanel.EnsureEventSystem(); }

    void Start() {
        MissionInfo m = MatchSetup.Mission;
        if (m == null) return;
        if (title != null) title.text = m.Title;
        Color colour = m.faction != null ? m.faction.flagColor : noFactionColor;
        if (faction != null) {
            faction.text = m.faction != null ? (string.IsNullOrEmpty(m.faction.campaignName) ? m.faction.name : m.faction.campaignName) : "";
            faction.color = colour;
        }
        if (flag != null) flag.color = colour;
        if (briefing != null) briefing.text = m.briefing;
        if (objectives != null) objectives.text = m.objectives;
        if (pars != null) pars.text = string.Format(m.parTurns > 0 ? parsFormat : parsFormatNoSpeed, m.parTurns, m.parKills, m.parLosses);
    }

    /// <summary>Start the mission as its scenario file sets it up.</summary>
    public void StartMission() {
        // (MatchSetup's sides and rules are the file's own, read from the board by GameSession: nothing was changed.)
        if (!MatchSetup.StartGame()) { if (briefing != null) briefing.text = string.Format(couldNotLoad, MatchSetup.FilePath); return; }
        CampaignProgress.Attempt(MatchSetup.Mission);
        GameSession.Frame();
        if (GameSession.Instance != null) GameSession.Instance.SetupDone();
    }

    public void Back() { MatchSetup.ToCampaign(); }
}
