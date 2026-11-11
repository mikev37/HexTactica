using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The in-game menu (the InGameMenu scene, over the game on the menu key; the game is held while it is open):
/// Resume, Save (a name, into the saves folder — apart from the scenarios; the save notes where the game log stands,
/// so loading it goes on with the same log), Load (a saved game), Exit to the main menu.
/// </summary>
public class InGameMenu : MonoBehaviour {
    [Tooltip("The menu's own window: hidden while the options are open.")]
    public GameObject window;
    [Tooltip("The options (the same panel as the main menu's): volume, display.")]
    public OptionsPanel optionsPanel;
    [Tooltip("Gives the game up for the human side: off when no human is playing or the game is over.")]
    public Button resignButton;
    public TMP_InputField saveName;
    public FileList saves;
    public Button loadButton;
    [Tooltip("What was just done (saved, loaded, or why not).")]
    public TMP_Text status;
    [Tooltip("The name offered for a save: {0} the scenario, {1} the round.")]
    public string defaultName = "{0} round {1}";

    void Awake() {
        TransportPanel.EnsureEventSystem();
        if (saves != null) saves.onPicked.AddListener(() => {
            if (loadButton != null) loadButton.interactable = saves.Selected != null;
            if (saveName != null && saves.SelectedFile != null) saveName.text = saves.SelectedFile.name;
        });
    }

    void Start() {
        if (saves != null) saves.ShowSaves();
        if (loadButton != null) loadButton.interactable = saves != null && saves.Selected != null;
        GameController gc = GameController.gc;
        string scenario = string.IsNullOrEmpty(MatchSetup.FilePath) ? "game" : Path.GetFileNameWithoutExtension(MatchSetup.FilePath);
        if (saveName != null) saveName.text = string.Format(defaultName, scenario, gc != null ? gc.days : 0);
        if (status != null) status.text = "";
        if (optionsPanel != null) { optionsPanel.returnTo = window; optionsPanel.gameObject.SetActive(false); }
        if (resignButton != null) resignButton.interactable = Resigner() != null;
    }

    /// <summary>The options over the menu (their Back returns here).</summary>
    public void ShowOptions() {
        if (optionsPanel == null) return;
        if (window != null) window.SetActive(false);
        optionsPanel.gameObject.SetActive(true);
    }

    /// <summary>The human side that would resign: the side to move if a human plays it, else the first human side still in.</summary>
    static Player Resigner() {
        GameController gc = GameController.gc;
        if (gc == null || gc.gameOver || gc.rules == RuleSet.Sandbox) return null;
        if (gc.activePlayer != null && !gc.activePlayer.eliminated && FogOfWar.IsHuman(gc.activePlayer)) return gc.activePlayer;
        foreach (Player p in gc.players) if (p != null && !p.eliminated && FogOfWar.IsHuman(p)) return p;
        return null;
    }

    /// <summary>Give the game up: the human side is out, and (one side left) the game ends on the post-game screen.</summary>
    public void Resign() {
        Player p = Resigner();
        if (p == null) return;
        GameController.gc.Resign(p);
        if (GameSession.Instance != null) GameSession.Instance.CloseMenu();
    }

    public void Resume() { if (GameSession.Instance != null) GameSession.Instance.CloseMenu(); }

    public void Save() {
        string name = saveName != null ? GameFiles.SafeName(saveName.text) : "";
        if (name.Length == 0) { Say("Give the save a name."); return; }
        string path = GameFiles.SavePath(name);
        LevelEditor.SaveGame(path);
        if (saves != null) { saves.Refresh(); saves.PickNamed(name); }
        Say("Saved " + name + ".");
    }

    public void Load() {
        if (saves == null || saves.Selected == null) return;
        string name = Path.GetFileNameWithoutExtension(saves.Selected);
        if (!MatchSetup.LoadSave(saves.Selected)) { Say("Could not load " + name + "."); return; }
        MatchSetup.Mode = SessionMode.Play;
        GameSession.Frame();
        if (GameSession.Instance != null) { GameSession.Instance.NewGame(); GameSession.Instance.CloseMenu(); }
    }

    public void ExitToMainMenu() { MatchSetup.ToMainMenu(); }

    void Say(string s) { if (status != null) status.text = s; }
}
