using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One campaign: a map with its mission flags (under <see cref="flags"/>), on the Map object of a campaign scene.
/// The campaign hub (the Campaign scene's CampaignScreen) loads every campaign scene it lists, lays their maps side by
/// side in one view, and lets the player pan across or jump between those unlocked. A campaign scene can also be
/// opened and played on its own: it then opens the hub, looking at this campaign.
/// The campaign is known by its scene's name: missions' progress is kept under it ("Campaign 2/Mission 1"), and
/// missions unlock a campaign by naming its scene.
/// </summary>
[DisallowMultipleComponent]
public class CampaignMap : MonoBehaviour {
    [Tooltip("Its name on the campaign tabs. Empty: its scene's name.")]
    public string title = "";
    [Tooltip("Open from the start. Off: it appears once a mission whose Unlocks Campaigns names it has been won.")]
    public bool availableAtStart;
    [Tooltip("Where its missions are: a child of this map, stretched over it, holding a Mission Flag per mission.")]
    public RectTransform flags;

    string sceneName;

    /// <summary>The campaign hub is up (CampaignScreen sets this): a campaign scene loaded by it stays where it is put.</summary>
    public static bool HubOpen;

    void Awake() { sceneName = gameObject.scene.IsValid() ? gameObject.scene.name : ""; }

    /// <summary>Which campaign it is: the name of the scene it was made in (kept when the hub moves it into its own scene).</summary>
    public string Id {
        get {
            if (string.IsNullOrEmpty(sceneName)) sceneName = gameObject.scene.IsValid() ? gameObject.scene.name : "";
            return sceneName;
        }
    }

    public string Title { get { return string.IsNullOrEmpty(title) ? Id : title; } }

    /// <summary>Its missions, shown or not.</summary>
    public List<Mission> Missions {
        get {
            List<Mission> list = new List<Mission>();
            if (flags != null) list.AddRange(flags.GetComponentsInChildren<Mission>(true));
            return list;
        }
    }

    /// <summary>The player can see it: open from the start, or unlocked.</summary>
    public bool Open { get { return availableAtStart || CampaignProgress.CampaignUnlocked(Id); } }

    void Start() {
        // Played on its own (no hub loaded): open the hub, looking at this campaign.
        if (HubOpen) return;
        string hub = FrontEndSettings.Get.campaignScene;
        if (string.IsNullOrEmpty(hub) || hub == Id) return;
        CampaignProgress.Focus = Id;
        SceneManager.LoadScene(hub, LoadSceneMode.Single);
    }
}

/// <summary>
/// A text field holding a campaign scene's name: shown in the inspector as a list of the scenes in the build to pick
/// from (no name to type), and still a plain name in the data.
/// </summary>
public class CampaignSceneAttribute : PropertyAttribute { }
