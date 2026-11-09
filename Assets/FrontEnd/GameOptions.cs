using UnityEngine;

/// <summary>
/// The player's options, kept between runs (PlayerPrefs, saved as each changes) and put in force when the game starts,
/// before the first scene: master volume, music volume, sound-effects volume, full screen, resolution, graphics
/// quality, vertical sync. The options panel (OptionsPanel) changes them. Music (MusicManager) plays at
/// MusicVolume, every sound effect (GameSounds, a unit's sounds) at EffectsVolume, all under the master volume.
/// </summary>
public static class GameOptions {
    const string VolumeKey = "options.volume", MusicKey = "options.music", EffectsKey = "options.effects", FullScreenKey = "options.fullscreen", WidthKey = "options.width", HeightKey = "options.height",
                 QualityKey = "options.quality", VSyncKey = "options.vsync";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplySaved() {
        AudioListener.volume = Volume;
        if (PlayerPrefs.HasKey(QualityKey)) QualitySettings.SetQualityLevel(Quality, true);
        if (PlayerPrefs.HasKey(VSyncKey)) QualitySettings.vSyncCount = VSync ? 1 : 0;
        if (PlayerPrefs.HasKey(WidthKey)) Screen.SetResolution(PlayerPrefs.GetInt(WidthKey), PlayerPrefs.GetInt(HeightKey), FullScreen);
        else if (PlayerPrefs.HasKey(FullScreenKey)) Screen.fullScreen = FullScreen;
    }

    /// <summary>Music, 0 to 1 (under the master volume).</summary>
    public static float MusicVolume {
        get { return PlayerPrefs.GetFloat(MusicKey, 1f); }
        set { PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    /// <summary>Sound effects (game sounds, unit sounds), 0 to 1 (under the master volume).</summary>
    public static float EffectsVolume {
        get { return PlayerPrefs.GetFloat(EffectsKey, 1f); }
        set { PlayerPrefs.SetFloat(EffectsKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    /// <summary>Every sound (master), 0 to 1.</summary>
    public static float Volume {
        get { return PlayerPrefs.GetFloat(VolumeKey, 1f); }
        set { AudioListener.volume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume); PlayerPrefs.Save(); }
    }

    public static bool FullScreen {
        get { return PlayerPrefs.GetInt(FullScreenKey, Screen.fullScreen ? 1 : 0) != 0; }
        set { Screen.fullScreen = value; PlayerPrefs.SetInt(FullScreenKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// <summary>One of QualitySettings.names.</summary>
    public static int Quality {
        get { return PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()); }
        set { QualitySettings.SetQualityLevel(value, true); PlayerPrefs.SetInt(QualityKey, value); PlayerPrefs.Save(); }
    }

    public static bool VSync {
        get { return PlayerPrefs.GetInt(VSyncKey, QualitySettings.vSyncCount > 0 ? 1 : 0) != 0; }
        set { QualitySettings.vSyncCount = value ? 1 : 0; PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static void SetResolution(int width, int height) {
        Screen.SetResolution(width, height, Screen.fullScreen);
        PlayerPrefs.SetInt(WidthKey, width); PlayerPrefs.SetInt(HeightKey, height); PlayerPrefs.Save();
    }
}
