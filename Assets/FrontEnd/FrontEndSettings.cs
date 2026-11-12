using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The front end's settings, in one asset (Assets/Resources/FrontEndSettings.asset): the scenes it moves between, where
/// scenarios and saved games are kept, the key that opens the in-game menu, and when the post-game report thinks a
/// finding is worth saying. Read by the menus, the game session and the statistics.
/// </summary>
[CreateAssetMenu(menuName = "HexTactica/Front End Settings")]
public class FrontEndSettings : ScriptableObject {

    [Header("Scenes (in the build settings)")]
    public string mainMenuScene = "MainMenu";
    [Tooltip("The scene that plays the game: the board, the players, the GameController.")]
    public string gameScene = "SampleScene";
    [Tooltip("Loaded over the game scene before a game starts: who plays each side, fog, purchases, factions.")]
    public string preGameScene = "PreGame";
    [Tooltip("Loaded over the game scene when the game ends: the statistics and what they say.")]
    public string postGameScene = "PostGame";
    [Tooltip("Loaded over the game scene with the menu key: save, load, exit to the main menu.")]
    public string inGameMenuScene = "InGameMenu";
    [Tooltip("Loaded over the game scene in the map editor: the editing tools.")]
    public string mapEditorScene = "MapEditor";

    [Tooltip("Loaded over the game scene to watch a recorded game: the replay's controls.")]
    public string replayScene = "Replay";

    [Header("Campaign")]
    [Tooltip("The campaign screen: the map with a flag for every mission.")]
    public string campaignScene = "Campaign";
    [Tooltip("Loaded over the game scene before a campaign mission starts, in place of the pre-game screen: the mission's introduction and objectives, no choices.")]
    public string missionBriefScene = "MissionBrief";
    [Tooltip("Loaded over the game scene when a campaign mission ends, in place of the post-game screen: the result against the mission's pars, and its stars.")]
    public string missionResultScene = "MissionResult";
    [Tooltip("Folders the campaign's mission scenarios are in, relative to the game's data folder (as Scenario Folders): apart from the scenarios, so they are not listed under Load Scenario. The first that holds a mission's file is used.")]
    public List<string> campaignFolders = new List<string> { "Resources/Campaign/Scenarios", "StreamingAssets/Campaign/Scenarios" };

    [Header("Replay")]
    [Tooltip("Play: seconds between one action and the next, counted from when the last one's animation is done.")]
    public float replayPlayPause = 0.1f;
    [Tooltip("Fast forward: an action is ordered every this many seconds, without waiting for the last one to finish.")]
    public float replayFastInterval = 0.2f;
    [Tooltip("Fast forward: every unit's move speed on screen is multiplied by this.")]
    public float replayFastMoveSpeed = 3f;
    [Tooltip("The most log lines the replay's controls list for a turn.")]
    public int replayLinesShown = 200;

    [Header("Files")]
    [Tooltip("Folders scenarios are listed from, relative to the game's data folder (Application.dataPath: Assets in the editor, <Game>_Data in a build). The first that exist are read; scenarios saved by the map editor are listed too (userScenarioFolder).")]
    public List<string> scenarioFolders = new List<string> { "Resources/Scenarios", "StreamingAssets/Scenarios" };
    [Tooltip("Where the map editor saves new scenarios: a folder in the player's data folder (Application.persistentDataPath).")]
    public string userScenarioFolder = "scenarios";
    [Tooltip("Where saved games go: a folder in the player's data folder (Application.persistentDataPath), apart from the scenarios.")]
    public string savesFolder = "saves";
    [Tooltip("The extension of scenario and save files.")]
    public string fileExtension = ".dat";
    [Tooltip("In the editor, the map editor saves a scenario back where it was loaded from (Assets/Resources/Scenarios); off, always to the user scenario folder.")]
    public bool editorSavesInPlace = true;

    [Header("AI")]
    [Tooltip("The AI profiles a side can be played by (the pre-game screen's list, after \"AI (default)\"). A save names a side's profile by its profileName, found here.")]
    public List<HexTactica.AI.AIProfile> aiProfiles = new List<HexTactica.AI.AIProfile>();

    /// <summary>The profile of this name (profileName, else the asset's name) among aiProfiles; null (the AI's default) for "" or one not listed.</summary>
    public HexTactica.AI.AIProfile Profile(string name) {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (HexTactica.AI.AIProfile p in aiProfiles) if (p != null && (p.profileName == name || p.name == name)) return p;
        return null;
    }

    [Header("Game")]
    [Tooltip("Opens and closes the in-game menu.")]
    public KeyCode menuKey = KeyCode.Escape;

    [Header("Post-game report")]
    public GameInsights.Limits insights = new GameInsights.Limits();

    public const string ResourceName = "FrontEndSettings";
    static FrontEndSettings loaded;

    /// <summary>The settings asset (Resources/FrontEndSettings). Missing, the defaults are used and the console says so.</summary>
    public static FrontEndSettings Get {
        get {
            if (loaded != null) return loaded;
            loaded = Resources.Load<FrontEndSettings>(ResourceName);
            if (loaded == null) {
                Debug.LogWarning("No Assets/Resources/" + ResourceName + ".asset: the front end uses its default settings (HexTactica ▸ Front End ▸ Create Missing Scenes makes the asset).");
                loaded = ScriptableObject.CreateInstance<FrontEndSettings>();
            }
            return loaded;
        }
    }
}
