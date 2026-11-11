using System.Text;
using HexTactica.Harness;
using TMPro;
using UnityEngine;

/// <summary>
/// The replay's controls (the Replay scene, over the game scene held still): the turn on screen and the action just
/// played, the turn's actions listed (those still to come dimmed), and buttons — Next action, Next turn, Previous
/// turn, Play (an action every FrontEndSettings.replayStepSeconds, once the last one's animation is done), Fast
/// forward (the same, replayFastForward times faster), whose eyes (a side's fog, or everything), Exit.
/// </summary>
public class ReplayControls : MonoBehaviour {
    public TMP_Text title;
    [Tooltip("Whose eyes the board is seen through, and how the game ended.")]
    public TMP_Text view;
    [Tooltip("The action just played, as the log has it.")]
    public TMP_Text lastAction;
    [Tooltip("The turn's actions: played ones plain, those to come dimmed.")]
    public TMP_Text log;
    [Tooltip("The Play button's label: Play, or Pause while playing.")]
    public TMP_Text playLabel;
    public string playText = "Play", pauseText = "Pause";
    [Tooltip("The Fast forward button's label, and what it reads while fast forward is on.")]
    public TMP_Text fastLabel;
    public string fastText = "Fast forward", fastOnText = "Normal speed";
    [Tooltip("Rich-text colour of the actions still to come.")]
    public string dimColour = "#8a8f99";
    public string nothingYet = "(start of the turn)";
    public string noActions = "(nothing happened this turn)";

    bool playing, fast;
    float nextAt;

    void Awake() { TransportPanel.EnsureEventSystem(); }
    void OnEnable() { Replay.Changed += Show; Show(); }
    void OnDisable() { Replay.Changed -= Show; }

    public void NextAction() { Pause(); Replay.NextAction(); }
    public void NextTurn() { Pause(); Replay.NextTurn(); }
    public void PreviousTurn() { Pause(); Replay.Previous(); }
    public void CycleView() { Replay.CycleView(); Show(); }
    public void Exit() { MatchSetup.ToMainMenu(); }

    /// <summary>Play mode on or off.</summary>
    public void TogglePlay() { playing = !playing; nextAt = 0f; Show(); }

    /// <summary>Fast forward on or off (turning it on starts playing).</summary>
    public void ToggleFast() { fast = !fast; if (fast) playing = true; nextAt = 0f; Show(); }

    void Pause() { playing = false; }

    void Update() {
        if (!playing || !Replay.Active) return;
        if (Unit.AnyAnimating()) { nextAt = 0f; return; }   // the step's wait starts when the board is still
        FrontEndSettings s = FrontEndSettings.Get;
        float wait = s.replayStepSeconds / (fast ? Mathf.Max(1f, s.replayFastForward) : 1f);
        if (nextAt <= 0f) { nextAt = Time.unscaledTime + wait; return; }
        if (Time.unscaledTime < nextAt) return;
        nextAt = 0f;
        if (!Replay.NextAction()) { playing = false; fast = false; Show(); }
    }

    void Show() {
        if (title != null) title.text = Replay.Title();
        if (view != null) view.text = "Seen " + Replay.ViewName() + (Replay.Ending != null ? "\n" + Replay.Ending : "");
        if (lastAction != null) lastAction.text = Replay.LastAction.Length > 0 ? Replay.LastAction : nothingYet;
        if (playLabel != null) playLabel.text = playing ? pauseText : playText;
        if (fastLabel != null) fastLabel.text = fast ? fastOnText : fastText;
        if (log == null) return;
        Replay.Turn t = Replay.Current;
        StringBuilder sb = new StringBuilder();
        int max = FrontEndSettings.Get.replayLinesShown;
        if (t == null || t.actions.Count == 0) {
            // No action could be read: the turn's log as it is, so what happened can still be seen.
            if (t != null) for (int i = 0; i < t.events.Count && i < max; i++) sb.Append(t.events[i].Trim()).Append('\n');
            log.text = sb.Length > 0 ? sb.ToString() : noActions;
            return;
        }
        // Around the action in hand, when the turn has more than fit.
        int first = Mathf.Clamp(Replay.Step - max / 2, 0, Mathf.Max(0, t.actions.Count - max));
        for (int i = first; i < t.actions.Count && i < first + max; i++) {
            string line = t.events[t.actions[i]].Trim();
            if (i < Replay.Step) sb.Append(line).Append('\n');
            else sb.Append("<color=").Append(dimColour).Append('>').Append(line).Append("</color>\n");
        }
        log.text = sb.ToString();
    }
}
