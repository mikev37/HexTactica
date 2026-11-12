using UnityEngine;

/// <summary>
/// Sounds that belong to the game rather than to a unit (a building captured, ...): one asset,
/// Assets/Resources/GameSounds.asset (made automatically the first time the editor opens without it; set the clips
/// there). A sound with no clip is not played. Everything plays through <see cref="Play"/>, at the options menu's
/// volume (AudioListener.volume, set by GameOptions.Volume), and never in headless runs or an AI's board view.
/// </summary>
[CreateAssetMenu(menuName = "HexTactica/Game Sounds", fileName = "GameSounds")]
public class GameSounds : ScriptableObject {

    [Tooltip("A building changes hands (captured by anyone the viewer can see it happen to).")]
    public AudioClip buildingCaptured;

    [Tooltip("A stealthy unit is spotted (fog of war): an enemy's by the side watching, or one of the watcher's own by an enemy.")]
    public AudioClip stealthDetected;

    [Header("Music (MusicManager: the tracks are in Resources/Music)")]
    [Tooltip("Music volume, under the options menu's volume (0 to 1).")]
    [Range(0, 1)] public float musicVolume = 0.6f;
    [Tooltip("Seconds before a track ends that it starts fading out, as the next one fades in.")]
    public float musicEndFade = 10f;
    [Tooltip("Seconds a track takes to fade in.")]
    public float musicFadeIn = 3f;
    [Tooltip("Seconds the music takes to change on a trigger (game start, game over, a scenario's special track).")]
    public float musicSwitchFade = 2f;

    public const string ResourceName = "GameSounds";
    static GameSounds instance;

    /// <summary>The asset in Resources; an empty one (no sounds) if there is none.</summary>
    public static GameSounds Instance {
        get {
            if (instance == null) instance = Resources.Load<GameSounds>(ResourceName);
            if (instance == null) instance = CreateInstance<GameSounds>();
            return instance;
        }
    }

    /// <summary>Sounds are played: not headless (tests, the harness), and on the main thread outside any board view.</summary>
    public static bool On {
        get { return BoardView.current == null && (GameController.gc == null || !GameController.gc.headless); }
    }

    static AudioSource source;

    /// <summary>
    /// Play <paramref name="clip"/> once, from a source that outlives any unit or scene (a unit's death still sounds
    /// after its object is gone). Nothing if the clip is empty or sounds are off.
    /// </summary>
    public static void Play(AudioClip clip, float volume = 1f) {
        if (clip == null || !On) return;
        if (source == null) {
            GameObject go = new GameObject("Game Sounds");
            DontDestroyOnLoad(go);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }
        source.PlayOneShot(clip, volume * GameOptions.EffectsVolume);   // the options' sound-effects volume
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook() {
        Building.OwnerChanged -= OnOwnerChanged;
        Building.OwnerChanged += OnOwnerChanged;
    }

    /// <summary>A building changed hands: the capture sound, if whoever is watching could see it (fog of war).</summary>
    static void OnOwnerChanged(Building b, Player previousOwner) {
        if (b == null || b.owner == null || !On) return;   // gone neutral (decay) is not a capture
        Player viewer = FogOfWar.Viewer;
        if (viewer != null && viewer != b.owner && viewer != previousOwner && !FogOfWar.TileVisible(viewer, b.tile)) return;
        Play(Instance.buildingCaptured);
    }
}
