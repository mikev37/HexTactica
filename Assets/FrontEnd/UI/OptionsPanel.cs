using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The options (a panel of the main menu): master, music and sound-effects volume, full screen, resolution, graphics
/// quality, vertical sync. A change takes effect at once and is kept (GameOptions, in PlayerPrefs).
/// </summary>
public class OptionsPanel : MonoBehaviour {
    [Tooltip("Master volume: every sound.")]
    public Slider volume;
    [Tooltip("The volume as a percentage. Optional.")]
    public TMP_Text volumeValue;
    public Slider musicVolume;
    public TMP_Text musicValue;
    [Tooltip("Sound effects: game and unit sounds.")]
    public Slider effectsVolume;
    public TMP_Text effectsValue;
    public Toggle fullScreen;
    public TMP_Dropdown resolution;
    public TMP_Dropdown quality;
    public Toggle vSync;

    readonly List<Vector2Int> resolutions = new List<Vector2Int>();

    void Awake() {
        if (volume != null) volume.onValueChanged.AddListener(v => { GameOptions.Volume = v; ShowVolume(); });
        if (musicVolume != null) musicVolume.onValueChanged.AddListener(v => { GameOptions.MusicVolume = v; ShowVolume(); });
        if (effectsVolume != null) effectsVolume.onValueChanged.AddListener(v => { GameOptions.EffectsVolume = v; ShowVolume(); });
        if (fullScreen != null) fullScreen.onValueChanged.AddListener(v => GameOptions.FullScreen = v);
        if (vSync != null) vSync.onValueChanged.AddListener(v => GameOptions.VSync = v);
        if (quality != null) quality.onValueChanged.AddListener(i => GameOptions.Quality = i);
        if (resolution != null) resolution.onValueChanged.AddListener(i => { if (i >= 0 && i < resolutions.Count) GameOptions.SetResolution(resolutions[i].x, resolutions[i].y); });
    }

    void OnEnable() {
        Init(volume, GameOptions.Volume);
        Init(musicVolume, GameOptions.MusicVolume);
        Init(effectsVolume, GameOptions.EffectsVolume);
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

    static void Init(Slider s, float v) { if (s != null) { s.minValue = 0f; s.maxValue = 1f; s.SetValueWithoutNotify(v); } }

    void ShowVolume() {
        if (volumeValue != null) volumeValue.text = Mathf.RoundToInt(GameOptions.Volume * 100f) + "%";
        if (musicValue != null) musicValue.text = Mathf.RoundToInt(GameOptions.MusicVolume * 100f) + "%";
        if (effectsValue != null) effectsValue.text = Mathf.RoundToInt(GameOptions.EffectsVolume * 100f) + "%";
    }
}
