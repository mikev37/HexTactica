using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The main menu (the MainMenu scene): Campaign (the campaign screen), Load Scenario (a scenario to set up, or a saved game to play on), Map Editor,
/// Unit Explorer, Options, Exit. Each opens its panel; everything they look like is the scene's. The buttons call the
/// methods below (wired in the scene).
/// </summary>
public class MainMenu : MonoBehaviour {
    [Header("Panels (one shown at a time)")]
    public GameObject mainPanel;
    public GameObject loadPanel;
    public GameObject editorPanel;
    public GameObject unitsPanel;
    public GameObject optionsPanel;
    public GameObject replayPanel;
    [Tooltip("Networked games: Join Game's panel (an MpJoinPanel). Optional.")]
    public GameObject joinPanel;
    [Tooltip("Shown only with the main panel (the networked games list and name, top right): hidden under every other panel. Empty: the MpGamesMenu under this menu.")]
    public GameObject[] mainPanelOnly = new GameObject[0];

    [Header("Replay")]
    [Tooltip("The recorded games (game logs) that can be watched again.")]
    public FileList replayList;
    public Button watchButton;

    [Header("Load Scenario")]
    [Tooltip("Scenarios or saved games, by the tabs.")]
    public FileList loadList;
    [Tooltip("Sets up the picked scenario (the pre-game screen), or plays on the picked saved game.")]
    public Button playButton;
    public TMPro.TMP_Text playButtonLabel;
    public string playScenarioLabel = "Set up game";
    public string playSaveLabel = "Continue";
    [Tooltip("The button's text while hosting a networked game (Host Game): the scenario becomes its lobby.")]
    public string hostScenarioLabel = "Host game";
    [Tooltip("The Load panel's heading, changed to say so while hosting. Optional.")]
    public TMPro.TMP_Text loadHeading;
    public string loadHeadingText = "Load Scenario";
    public string hostHeadingText = "Host Game: pick a scenario";
    [Tooltip("The Load panel's tabs (Scenarios, Saved games): hidden while hosting, which takes scenarios only. Optional.")]
    public GameObject loadTabs;

    /// <summary>The Load panel was opened by Host Game: the scenario picked is hosted as a networked game.</summary>
    bool hosting;

    [Header("Map Editor")]
    public FileList editorList;
    public Button editButton;

    void Awake() {
        if (loadList != null) loadList.onPicked.AddListener(UpdateButtons);
        if (editorList != null) editorList.onPicked.AddListener(UpdateButtons);
        if (replayList != null) replayList.onPicked.AddListener(UpdateButtons);
    }

    void Start() {
        GameController.Paused = false;
        ShowMain();
    }

    void Show(GameObject panel) {
        foreach (GameObject p in new[] { mainPanel, loadPanel, editorPanel, unitsPanel, optionsPanel, replayPanel, joinPanel })
            if (p != null) p.SetActive(p == panel);
        GameObject[] mainOnly = mainPanelOnly;
        if (mainOnly == null || mainOnly.Length == 0) {
            HexTactica.Multiplayer.MpGamesMenu games = GetComponentInChildren<HexTactica.Multiplayer.MpGamesMenu>(true);
            mainOnly = games != null ? new[] { games.gameObject } : new GameObject[0];
        }
        foreach (GameObject g in mainOnly) if (g != null) g.SetActive(panel == mainPanel);
        if (panel != loadPanel) hosting = false;
        if (loadHeading != null) loadHeading.text = hosting ? hostHeadingText : loadHeadingText;
        if (loadTabs != null) loadTabs.SetActive(!hosting);
        UpdateButtons();
    }

    public void ShowMain() { Show(mainPanel); }
    public void ShowLoadScenario() { hosting = false; Show(loadPanel); if (loadList != null) loadList.ShowScenarios(); }

    /// <summary>Host Game: the Load panel, scenarios only; the one picked becomes a networked game's lobby.</summary>
    public void ShowHostGame() { hosting = true; Show(loadPanel); if (loadList != null) loadList.ShowScenarios(); UpdateButtons(); }

    /// <summary>Join Game: the join code panel.</summary>
    public void ShowJoinGame() { if (joinPanel != null) Show(joinPanel); }
    public void ShowMapEditor() { Show(editorPanel); if (editorList != null) editorList.ShowEditable(); }
    public void ShowUnitExplorer() { Show(unitsPanel); }
    public void ShowOptions() { Show(optionsPanel); }
    public void ShowReplays() { Show(replayPanel); if (replayList != null) replayList.ShowRecordings(); UpdateButtons(); }

    /// <summary>The campaign screen: the map and its mission flags.</summary>
    public void Campaign() { MatchSetup.ToDefaultCampaign(); }

    /// <summary>Watch the picked recording.</summary>
    public void Watch() { if (replayList != null && replayList.Selected != null) MatchSetup.Watch(replayList.Selected); }

    /// <summary>The Load panel's tabs.</summary>
    public void ListScenarios() { if (loadList != null) loadList.ShowScenarios(); UpdateButtons(); }
    public void ListSaves() { if (loadList != null) loadList.ShowSaves(); UpdateButtons(); }

    void UpdateButtons() {
        if (playButton != null) playButton.interactable = loadList != null && loadList.Selected != null;
        if (playButtonLabel != null && loadList != null) playButtonLabel.text = hosting ? hostScenarioLabel : loadList.source == FileList.Source.Saves ? playSaveLabel : playScenarioLabel;
        if (editButton != null) editButton.interactable = editorList != null && editorList.Selected != null;
        if (watchButton != null) watchButton.interactable = replayList != null && replayList.Selected != null;
    }

    /// <summary>Set up the picked scenario, or play on the picked saved game.</summary>
    public void Play() {
        if (loadList == null || loadList.Selected == null) return;
        if (hosting) MatchSetup.HostNetworked(loadList.Selected);
        else if (loadList.source == FileList.Source.Saves) MatchSetup.PlaySave(loadList.Selected);
        else MatchSetup.SetUp(loadList.Selected);
    }

    /// <summary>The map editor on the picked scenario.</summary>
    public void EditPicked() { if (editorList != null && editorList.Selected != null) MatchSetup.Edit(editorList.Selected); }

    /// <summary>The map editor on the game scene's own board (a new map).</summary>
    public void EditNew() { MatchSetup.Edit(null); }

    public void Exit() {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
