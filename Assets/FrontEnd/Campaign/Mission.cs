using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A campaign mission: a flag placed on a campaign scene's map (the Mission Flag prefab, with this component and its
/// MissionFlag look). Where it stands on the map is where it is placed. It holds the scenario it plays, whose point
/// of view it is told from (the flag's colour), what it says before and after, what it opens when won and closes when
/// attempted (other flags of the same scene, dragged in), and the pars its three stars are earned against.
/// Each campaign is its own scene; a mission's progress is kept under its scene and its name (renaming either
/// forgets it). The scenario itself is a file in the campaign's folder (FrontEndSettings.campaignFolders), so it is
/// not listed under Load Scenario.
/// </summary>
[DisallowMultipleComponent]
public class Mission : MonoBehaviour {

    [Header("The mission")]
    [Tooltip("Its name on the flag and the briefing. Empty: the object's name.")]
    public string title = "";
    [Tooltip("The scenario file it plays: the file's name, without extension, in the campaign's scenario folder (Assets/Resources/Campaign/Scenarios). Empty: the object's name.")]
    public string scenario = "";
    [Tooltip("Whose point of view the mission is told from: the flag takes this faction's Flag Colour. Empty: the campaign screen's No Faction colour.")]
    public Faction faction;
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
    [Tooltip("Missions (flags in this scene) that appear when this one is won.")]
    public List<Mission> unlocks = new List<Mission>();
    [Tooltip("Missions (flags in this scene) closed for good once this one is attempted (started, whether won or not): a fork in the road. One already won stays won.")]
    public List<Mission> blocks = new List<Mission>();

    [Header("Stars (earned only by winning)")]
    [Tooltip("Speed: won within this many of the player's turns. 0: this mission has no speed star.")]
    public int parTurns;
    [Tooltip("Aggression: at least this many enemy units destroyed by the player.")]
    public int parKills;
    [Tooltip("Defense: at most this many of the player's units lost (to anything).")]
    public int parLosses;

    /// <summary>Its name on the flag: Title, or the object's name.</summary>
    public string Title { get { return string.IsNullOrEmpty(title) ? name : title; } }

        string Scene { get { return gameObject.scene.IsValid() ? gameObject.scene.name : ""; } }

    /// <summary>What its progress is kept under: its scene and its name.</summary>
    public string Key { get { return MissionInfo.KeyOf(Scene, name); } }

    /// <summary>The mission as the rest of the game keeps it, once this scene is gone.</summary>
    public MissionInfo Info {
        get {
            MissionInfo m = new MissionInfo {
                key = Key, campaignScene = Scene, title = title, scenario = scenario,
                briefing = briefing, objectives = objectives, availableAtStart = availableAtStart,
                parTurns = parTurns, parKills = parKills, parLosses = parLosses
            };
            m.faction = faction;
            if (unlocks != null) foreach (Mission u in unlocks) if (u != null && u != this) m.unlocks.Add(u.Key);
            if (blocks != null) foreach (Mission b in blocks) if (b != null && b != this) m.blocks.Add(b.Key);
            return m;
        }
    }

    /// <summary>
    /// Pin the flag's anchors to where it stands on the map, so it keeps its place on the picture whatever the
    /// screen's shape (the map stretches with the screen). Also on the component's menu in the inspector.
    /// </summary>
    [ContextMenu("Pin To Map")]
    public void PinToMap() {
        RectTransform rt = transform as RectTransform, parent = transform.parent as RectTransform;
        if (rt == null || parent == null) return;
        Rect r = parent.rect;
        if (r.width <= 0f || r.height <= 0f) return;
        Vector2 local = (Vector2)parent.InverseTransformPoint(rt.position);   // from the parent's pivot
        Vector2 at = new Vector2(parent.pivot.x + local.x / r.width, parent.pivot.y + local.y / r.height);
        rt.anchorMin = rt.anchorMax = at;
        rt.anchoredPosition = Vector2.zero;
    }
}
