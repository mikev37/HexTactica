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
    /// <summary>What this game scene was opened for (MatchSetup.Mode when it opened; Play once a set-up game starts).</summary>
    SessionMode mode;
    /// <summary>What this game scene is open for now (the music follows it: MusicManager).</summary>
    public SessionMode Mode { get { return mode; } }

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
        mode = MatchSetup.Mode;
        ShowGamePanels(mode);
        // The game scene keeps whatever editing switch it was saved with (SampleScene: terrain painting on): a game
        // opened from the menus starts with every editing tool off (the map editor's tools turn them on).
        if (mode != SessionMode.None && LevelEditor.singleton != null) {
            LevelEditor le = LevelEditor.singleton;
            le.editing = le.unit_placement = le.building_placement = le.mouseDown = false;
        }
        switch (mode) {
            case SessionMode.Setup:
                GameController.Paused = true;
                MatchSetup.LoadPreview();
                MatchSetup.ReadFromBoard();   // the pre-game screen's starting choices, as the file has them
                // Who plays which side is not chosen yet: the board is shown whole, with no fog at all (Start loads the
                // scenario again, with the fog the pre-game screen chose).
                if (GameController.gc != null) { GameController.gc.fogOfWar = false; FogOfWar.Reset(); FogOfWar.Changed(); FogOfWar.Refresh(); }
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
            case SessionMode.Replay:
                // Nobody plays: the game is held, and the Replay scene's controls step through the recording.
                GameController.Paused = true;
                HexTactica.Harness.Replay.BuiltInPanel = false;
                // Whatever goes wrong opening the recording, the controls still come up (their Exit leads back).
                bool opened = false;
                try { opened = HexTactica.Harness.Replay.Open(MatchSetup.FilePath); }
                catch (System.Exception e) { Debug.LogError("Could not open the recording " + MatchSetup.FilePath + ": " + e); opened = HexTactica.Harness.Replay.Active; }
                if (opened) {
                    Frame();
                    SceneManager.LoadScene(s.replayScene, LoadSceneMode.Additive);
                } else MatchSetup.ToMainMenu();
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
        bool playing = mode == SessionMode.Play || mode == SessionMode.None;
        if (playing && Input.GetKeyDown(FrontEndSettings.Get.menuKey)) { if (menuOpen) CloseMenu(); else OpenMenu(); }
        if (playing && gc.gameOver && !postShown) {
            postShown = true;
            if (menuOpen) CloseMenu();
            HexTactica.Harness.GameLog.EndIfOver();   // the log's last line, read by the report
            StartCoroutine(PostGameAfterBanner());
        }
    }

    /// <summary>The post-game screen once the end-of-game banner ("Red wins") has played out.</summary>
    IEnumerator PostGameAfterBanner() {
        yield return null;   // the banner starts the frame after the game ends
        TurnBanner banner = FindAnyObjectByType<TurnBanner>();
        while (banner != null && banner.BannerShowing) yield return null;
        SceneManager.LoadScene(FrontEndSettings.Get.postGameScene, LoadSceneMode.Additive);
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
        GameController.Paused = mode != SessionMode.Play && mode != SessionMode.None;
        Unload(FrontEndSettings.Get.inGameMenuScene);
    }

    /// <summary>The post-game screen closed: the board stays to look at (the menu key opens the in-game menu).</summary>
    public void ClosePostGame() { Unload(FrontEndSettings.Get.postGameScene); }

    /// <summary>A game was loaded or started again: the post-game screen may show again when it ends.</summary>
    public void NewGame() { postShown = false; mode = SessionMode.Play; }

    /// <summary>The pre-game screen started the game: it goes, and the game plays.</summary>
    public void SetupDone() {
        postShown = false;
        mode = SessionMode.Play;
        ShowGamePanels(mode);
        Unload(FrontEndSettings.Get.preGameScene);
    }

    /// <summary>
    /// The game's own panels for the mode: all of them in play; none while setting up; in the map editor the HUD's
    /// unit and tile panels (they show the pick) without the turn controls (gold, next unit, end turn) or the banner.
    /// </summary>
    static void ShowGamePanels(SessionMode m) {
        bool hud = m != SessionMode.Setup, turns = m == SessionMode.Play || m == SessionMode.None;
        foreach (Hud h in Resources.FindObjectsOfTypeAll<Hud>()) if (h != null && h.gameObject.scene.IsValid()) h.gameObject.SetActive(hud);
        foreach (TurnControls c in Resources.FindObjectsOfTypeAll<TurnControls>()) if (c != null && c.gameObject.scene.IsValid()) c.gameObject.SetActive(turns);
        foreach (TurnBanner b in Resources.FindObjectsOfTypeAll<TurnBanner>()) if (b != null && b.gameObject.scene.IsValid()) b.gameObject.SetActive(turns);
    }

    public static void Unload(string scene) {
        Scene sc = SceneManager.GetSceneByName(scene);
        if (sc.IsValid() && sc.isLoaded) SceneManager.UnloadSceneAsync(sc);
    }
}
