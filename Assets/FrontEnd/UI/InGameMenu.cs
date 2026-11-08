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
