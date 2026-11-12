using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A campaign mission (an asset in Assets/Resources/Campaign/Missions): the scenario it plays, whose point of view it
/// is told from (the flag's colour), where its flag stands on the campaign map, what it says before and after, what
/// it opens when it is won and closes when it is attempted, and the pars its three stars are earned against.
/// The scenario itself is a scenario file like any other, in the campaign's own folder
/// (FrontEndSettings.campaignFolders), so it is not listed under Load Scenario.
/// </summary>
[CreateAssetMenu(menuName = "HexTactica/Campaign Mission")]
public class Mission : ScriptableObject {

    [Header("The mission")]
    [Tooltip("Its name on the flag and the briefing. Empty: the asset's name.")]
    public string title = "";
    [Tooltip("The scenario file it plays: the file's name, without extension, in the campaign's scenario folder (Assets/Resources/Campaign/Scenarios). Empty: the asset's name.")]
    public string scenario = "";
    [Tooltip("Whose point of view the mission is told from: the flag takes this faction's Flag Colour. Empty: the campaign screen's No Faction colour.")]
    public Faction faction;
    [Tooltip("Where its flag stands on the campaign map: 0,0 the image's bottom left, 1,1 its top right.")]
    public Vector2 mapPosition = new Vector2(0.5f, 0.5f);
    [Tooltip("Its flag is on the map from the start. Off: it appears once a mission that unlocks it has been won.")]
    public bool availableAtStart;

    [Header("Before the mission")]
    [Tooltip("Said on the campaign screen when the flag is picked, before the briefing shows (the dialogue box).")]
    public List<DialogueLine> intro = new List<DialogueLine>();
    [Tooltip("The mission's introduction, on the campaign screen's briefing and on the screen before the game starts.")]
    [TextArea(3, 10)] public string briefing = "";
    [Tooltip("What the player has to do, on the screen before the game starts.")]
    [TextArea(2, 8)] public string objectives = "";

    [Header("After the mission (back on the campaign screen)")]
    [Tooltip("Said once the mission has been won, after its stars and the new flags have been shown.")]
    public List<DialogueLine> outro = new List<DialogueLine>();
    [Tooltip("Said when the mission was lost. Empty: nothing.")]
    public List<DialogueLine> outroLost = new List<DialogueLine>();

    [Header("The campaign")]
    [Tooltip("Missions whose flags appear when this one is won.")]
    public List<Mission> unlocks = new List<Mission>();
    [Tooltip("Missions closed for good once this one is attempted (started, whether won or not): a fork in the road. One already won stays won.")]
    public List<Mission> blocks = new List<Mission>();

    [Header("Stars (earned only by winning)")]
    [Tooltip("Speed: won within this many of the player's turns. 0: this mission has no speed star.")]
    public int parTurns;
    [Tooltip("Aggression: at least this many enemy units destroyed by the player.")]
    public int parKills;
    [Tooltip("Defense: at most this many of the player's units lost (to anything).")]
    public int parLosses;

    /// <summary>What progress is kept under (the asset's name: renaming the asset forgets its progress).</summary>
    public string Key { get { return name; } }
    public string Title { get { return string.IsNullOrEmpty(title) ? name : title; } }
    public string ScenarioName { get { return string.IsNullOrEmpty(scenario) ? name : scenario; } }

    /// <summary>Where the mission assets are, under Resources.</summary>
    public const string Folder = "Campaign/Missions";

    static List<Mission> all;

    /// <summary>Every mission of the campaign (Resources/Campaign/Missions), by name.</summary>
    public static List<Mission> All {
        get {
            if (all == null) {
                all = new List<Mission>(Resources.LoadAll<Mission>(Folder));
                all.RemoveAll(m => m == null);
                all.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            }
            return all;
        }
        // (Set by the headless checks, which have no Resources folder.)
        set { all = value; }
    }

    public static Mission Named(string key) {
        if (string.IsNullOrEmpty(key)) return null;
        return All.Find(m => m.Key == key);
    }
}
