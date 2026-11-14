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

    [Tooltip("A building is destroyed (its health runs out), where the viewer can see it.")]
    public AudioClip buildingDestroyed;

    [Tooltip("A match starts: play begins (after the pre-game screen, a game loaded or started again).")]
    public AudioClip matchStart;

    [Tooltip("The game is over and a human player won (or nobody human played and somebody won).")]
    public AudioClip victory;

    [Tooltip("The game is over and every human player lost, or it was a draw.")]
    public AudioClip loss;

    [Tooltip("A unit gets into a transport, where the viewer can see it.")]
    public AudioClip unitLoad;

    [Tooltip("A unit gets out of a transport, where the viewer can see it.")]
    public AudioClip unitUnload;

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
        Building.BuildingDestroyed -= OnBuildingDestroyed;
        Building.BuildingDestroyed += OnBuildingDestroyed;
        GameController.GameEnded -= OnGameEnded;
        GameController.GameEnded += OnGameEnded;
        Transporting.Boarded -= OnBoarded;
        Transporting.Boarded += OnBoarded;
        Transporting.Unloaded -= OnUnloaded;
        Transporting.Unloaded += OnUnloaded;
    }

    /// <summary>Can whoever is watching see <paramref name="tile"/> (fog of war)? Always, with no fog or no viewer.</summary>
    static bool ViewerSees(HexTile tile) {
        Player viewer = FogOfWar.Viewer;
        return viewer == null || tile == null || FogOfWar.TileVisible(viewer, tile);
    }

    /// <summary>A building was destroyed: its sound, if the viewer could see it (its own buildings always).</summary>
    static void OnBuildingDestroyed(Building b) {
        if (b == null || !On) return;
        Player viewer = FogOfWar.Viewer;
        if (viewer != null && b.owner != viewer && !ViewerSees(b.tile)) return;
        Play(Instance.buildingDestroyed);
    }

    /// <summary>A match has started (MusicManager: play has begun after the menus).</summary>
    public static void MatchStarted() { if (On) Play(Instance.matchStart); }

    /// <summary>The game is over: victory or loss, as the music decides it (HumanWon).</summary>
    static void OnGameEnded() {
        GameController gc = GameController.gc;
        if (gc == null || !On) return;
        Play(HumanWon(gc) ? Instance.victory : Instance.loss);
    }

    /// <summary>Did a human player win (or, with no human playing, did anybody)? A draw is not a win.</summary>
    public static bool HumanWon(GameController gc) {
        if (gc.winner == null) return false;
        bool anyHuman = false;
        if (gc.players != null) foreach (Player p in gc.players) if (p != null && FogOfWar.IsHuman(p)) anyHuman = true;
        if (!anyHuman) return true;
        // A human on the winning side (the winner, or one of its allies).
        foreach (Player p in gc.players) if (p != null && FogOfWar.IsHuman(p) && gc.Won(p)) return true;
        return false;
    }

    /// <summary>A unit got into a transport: the load sound, if the viewer could see the transport or where the unit stood.</summary>
    static void OnBoarded(Unit transport, Unit cargo, HexTile stoodOn) {
        if (!On) return;
        if (!FogOfWar.ShownToViewer(transport) && !ViewerSees(stoodOn)) return;
        Play(Instance.unitLoad);
    }

    /// <summary>A unit got out of a transport: the unload sound, if the viewer could see the transport or where the unit went.</summary>
    static void OnUnloaded(Unit transport, Unit cargo, HexTile onto) {
        if (!On) return;
        if (!FogOfWar.ShownToViewer(transport) && !ViewerSees(onto)) return;
        Play(Instance.unitUnload);
    }

    /// <summary>A building changed hands: the capture sound, if whoever is watching could see it (fog of war).</summary>
    static void OnOwnerChanged(Building b, Player previousOwner) {
        if (b == null || b.owner == null || !On) return;   // gone neutral (decay) is not a capture
        Player viewer = FogOfWar.Viewer;
        if (viewer != null && viewer != b.owner && viewer != previousOwner && !FogOfWar.TileVisible(viewer, b.tile)) return;
        Play(Instance.buildingCaptured);
    }
}
