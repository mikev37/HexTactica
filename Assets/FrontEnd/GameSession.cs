using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs the game scene for the front end (nothing on screen of its own): opens what the main menu asked for
/// (MatchSetup) once the board is ready — a scenario under the pre-game screen, a saved game, the map editor — and
/// loads the overlay scenes over the game: the in-game menu on the menu key, the post-game screen when the game ends.
/// Holds the game (GameController.Paused) while an overlay that needs it is open.
/// Installed in every game scene (GameScene); opened directly in the editor, the scene plays its own board as before,
/// with the in-game menu and the post-game screen.
/// </summary>
public class GameSession : MonoBehaviour {

    public static GameSession Instance { get; private set; }

    bool menuOpen, postShown, ready;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook() { GameScene.WhenLoaded(Install); }

    static void Install() {
        if (GameController.gc != null && GameController.gc.headless) return;
        if (Instance != null) return;
        new GameObject("GameSession").AddComponent<GameSession>();
    }

    void Awake() { Instance = this; }

    void OnDestroy() {
        if (Instance == this) Instance = null;
        // The game scene is going: what lives on in statics would point at its destroyed objects.
        Building.all.Clear();
    }

    IEnumerator Start() {
        // Every object of the scene has started (the board is built, the level editor is ready).
        yield return null;
        FrontEndSettings s = FrontEndSettings.Get;
        switch (MatchSetup.Mode) {
            case SessionMode.Setup:
                GameController.Paused = true;
                MatchSetup.LoadPreview();
                Frame();
                SceneManager.LoadScene(s.preGameScene, LoadSceneMode.Additive);
                break;
            case SessionMode.Play:
                GameController.Paused = false;
                MatchSetup.LoadSave(MatchSetup.FilePath);
                Frame();
                break;
            case SessionMode.Editor:
                GameController.Paused = true;
                if (!string.IsNullOrEmpty(MatchSetup.FilePath)) MatchSetup.LoadForEditing(MatchSetup.FilePath);
                Frame();
                SceneManager.LoadScene(s.mapEditorScene, LoadSceneMode.Additive);
                break;
            default:
                GameController.Paused = false;
                break;
        }
        ready = true;
    }

    /// <summary>The camera on the whole board (a board just loaded may be another size).</summary>
    public static void Frame() {
        CameraRig rig = Camera.main != null ? Camera.main.GetComponent<CameraRig>() : null;
        if (rig != null) rig.Frame();
    }

    void Update() {
        if (!ready) return;
        GameController gc = GameController.gc;
        if (gc == null) return;
        bool playing = MatchSetup.Mode == SessionMode.Play || MatchSetup.Mode == SessionMode.None;
        if (playing && Input.GetKeyDown(FrontEndSettings.Get.menuKey)) { if (menuOpen) CloseMenu(); else OpenMenu(); }
        if (playing && gc.gameOver && !postShown) {
            postShown = true;
            if (GameStats.Current != null) GameStats.Current.EndIfOver();
            if (menuOpen) CloseMenu();
            SceneManager.LoadScene(FrontEndSettings.Get.postGameScene, LoadSceneMode.Additive);
        }
    }

    /// <summary>The in-game menu over the game (the game is held while it is open).</summary>
    public void OpenMenu() {
        if (menuOpen) return;
        menuOpen = true;
        GameController.Paused = true;
        SceneManager.LoadScene(FrontEndSettings.Get.inGameMenuScene, LoadSceneMode.Additive);
    }

    /// <summary>Back to the game.</summary>
    public void CloseMenu() {
        if (!menuOpen) return;
        menuOpen = false;
        GameController.Paused = MatchSetup.Mode == SessionMode.Setup || MatchSetup.Mode == SessionMode.Editor;
        Unload(FrontEndSettings.Get.inGameMenuScene);
    }

    /// <summary>The post-game screen closed: the board stays to look at (the menu key opens the in-game menu).</summary>
    public void ClosePostGame() { Unload(FrontEndSettings.Get.postGameScene); }

    /// <summary>A game was loaded or started again: the post-game screen may show again when it ends.</summary>
    public void NewGame() { postShown = false; }

    /// <summary>The pre-game screen started the game: it goes, and the game plays.</summary>
    public void SetupDone() {
        postShown = false;
        Unload(FrontEndSettings.Get.preGameScene);
    }

    public static void Unload(string scene) {
        Scene sc = SceneManager.GetSceneByName(scene);
        if (sc.IsValid() && sc.isLoaded) SceneManager.UnloadSceneAsync(sc);
    }
}
