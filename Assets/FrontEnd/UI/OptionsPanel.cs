using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The options (a panel of the main menu): sound volume, full screen, resolution, graphics quality, vertical sync.
/// A change takes effect at once and is kept (GameOptions).
/// </summary>
public class OptionsPanel : MonoBehaviour {
    public Slider volume;
    [Tooltip("The volume as a percentage. Optional.")]
    public TMP_Text volumeValue;
    public Toggle fullScreen;
    public TMP_Dropdown resolution;
    public TMP_Dropdown quality;
    public Toggle vSync;

    readonly List<Vector2Int> resolutions = new List<Vector2Int>();

    void Awake() {
        if (volume != null) volume.onValueChanged.AddListener(v => { GameOptions.Volume = v; ShowVolume(); });
        if (fullScreen != null) fullScreen.onValueChanged.AddListener(v => GameOptions.FullScreen = v);
        if (vSync != null) vSync.onValueChanged.AddListener(v => GameOptions.VSync = v);
        if (quality != null) quality.onValueChanged.AddListener(i => GameOptions.Quality = i);
        if (resolution != null) resolution.onValueChanged.AddListener(i => { if (i >= 0 && i < resolutions.Count) GameOptions.SetResolution(resolutions[i].x, resolutions[i].y); });
    }

    void OnEnable() {
        if (volume != null) { volume.minValue = 0f; volume.maxValue = 1f; volume.SetValueWithoutNotify(GameOptions.Volume); }
        ShowVolume();
        if (fullScreen != null) fullScreen.SetIsOnWithoutNotify(Screen.fullScreen);
        if (vSync != null) vSync.SetIsOnWithoutNotify(QualitySettings.vSyncCount > 0);
        if (quality != null) {
            quality.ClearOptions();
            quality.AddOptions(QualitySettings.names.ToList());
            quality.SetValueWithoutNotify(QualitySettings.GetQualityLevel());
        }
        if (resolution != null) {
            resolutions.Clear();
            foreach (Resolution r in Screen.resolutions) {
                Vector2Int v = new Vector2Int(r.width, r.height);
                if (!resolutions.Contains(v)) resolutions.Add(v);
            }
            Vector2Int now = new Vector2Int(Screen.width, Screen.height);
            if (!resolutions.Contains(now)) resolutions.Add(now);
            resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            resolution.ClearOptions();
            resolution.AddOptions(resolutions.Select(v => v.x + " x " + v.y).ToList());
            resolution.SetValueWithoutNotify(resolutions.IndexOf(now));
        }
    }

    void ShowVolume() { if (volumeValue != null) volumeValue.text = Mathf.RoundToInt(GameOptions.Volume * 100f) + "%"; }
}
