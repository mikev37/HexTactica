using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The music, in every scene (one object that lives through scene changes). Tracks are AudioClips in Resources/Music:
/// <list type="bullet">
/// <item>Menu: the main menu, the pre-game screen, the map editor.</item>
/// <item>General, and Faction/&lt;faction asset name&gt; (Faction/Federation, ...): in play, each track picked at random
///   from the general ones and those of the faction whose turn it is.</item>
/// <item>Victory, Loss: once the game is over (Victory if a human player won, or nobody human played and somebody
///   won; Loss otherwise, a draw included).</item>
/// <item>Special: only when asked for (<see cref="PlaySpecial"/>, for scenario triggers).</item>
/// </list>
/// Each track fades in, and fades out over its last seconds as the next one fades in (GameSounds.musicEndFade). A
/// trigger (game start, game over, a special track) fades the music over to the new track (GameSounds.musicSwitchFade).
/// Volume: GameSounds.musicVolume, under the options menu's volume (AudioListener.volume). Silent in headless runs.
/// </summary>
public class MusicManager : MonoBehaviour {

    public enum Kind { None, Menu, General, Victory, Loss, Special }

    public const string Root = "Music/";
    public static MusicManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() {
        if (Instance != null) return;
        GameObject go = new GameObject("Music");
        DontDestroyOnLoad(go);
        go.AddComponent<MusicManager>();
    }

    /// <summary>One of the two players the music crossfades between.</summary>
    class Deck {
        public AudioSource source;
        public float level, target, speed;     // level 0..1, moved toward target at speed per second
        public bool endingStarted;              // its end fade (and the next track) has begun
        public float startedAt;                 // when it was started (unscaled time)
    }

    readonly Deck[] decks = new Deck[2];
    int current;                                // the deck playing the track now (the other fades out or is idle)
    Kind kind = Kind.None;                      // what the music is for now
    bool special, specialLoop;
    AudioClip lastClip;
    readonly Dictionary<string, AudioClip[]> folders = new Dictionary<string, AudioClip[]>();

    void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // A scenario's Play Music and Stop Music actions (ScenarioGame).
        ScenarioGame.PlayMusicHook = (track, loop) => PlaySpecial(track, loop);
        ScenarioGame.StopMusicHook = StopSpecial;
        for (int i = 0; i < 2; i++) {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 0f;
            s.ignoreListenerPause = true;
            decks[i] = new Deck { source = s };
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ---- For scenario triggers ----

    /// <summary>
    /// Play a track from Resources/Music/Special: the one named <paramref name="track"/> (the clip's name), or one at
    /// random if null or empty. It fades in over the music playing. When it ends the music goes back to what the game
    /// is doing, unless <paramref name="loop"/> (then it plays until <see cref="StopSpecial"/> or the game starts or
    /// ends). False if there is no such track.
    /// </summary>
    public static bool PlaySpecial(string track = null, bool loop = false) {
        MusicManager m = Instance;
        if (m == null) return false;
        AudioClip clip = null;
        AudioClip[] all = m.Folder("Special");
        if (string.IsNullOrEmpty(track)) clip = m.Pick(all);
        else if (track.IndexOf('/') >= 0) clip = Resources.Load<AudioClip>(track);   // a clip elsewhere under Resources, by its path (ScenarioAssets)
        else foreach (AudioClip c in all) if (c != null && c.name == track) { clip = c; break; }
        if (clip == null) return false;
        m.special = true;
        m.specialLoop = loop;
        m.Switch(clip, Settings.musicSwitchFade, loop, Settings.musicSwitchFade);
        return true;
    }

    /// <summary>Back from a special track to the music for what the game is doing (faded over).</summary>
    public static void StopSpecial() {
        MusicManager m = Instance;
        if (m == null || !m.special) return;
        m.special = false;
        m.Switch(m.Next(m.kind), Settings.musicSwitchFade, false, Settings.musicSwitchFade);
    }

    /// <summary>Is a special track playing?</summary>
    public static bool SpecialPlaying { get { return Instance != null && Instance.special; } }

    /// <summary>What the music is playing for now.</summary>
    public static Kind Playing { get { return Instance == null ? Kind.None : Instance.special ? Kind.Special : Instance.kind; } }

    // ---- Every frame ----

    static GameSounds Settings { get { return GameSounds.Instance; } }

    void Update() {
        GameController gc = GameController.gc;
        float dt = Time.unscaledDeltaTime;
        if (gc != null && gc.headless) { foreach (Deck d in decks) d.source.Stop(); return; }

        // What the game is doing: a change is a trigger (game start, game over, back to the menu).
        // (Held a moment first: a scene just opened does not know yet what it is for, GameSession.Start.)
        Kind now = Context();
        if (now != seen) { seen = now; seenSince = Time.unscaledTime; }
        if (now != kind && Time.unscaledTime - seenSince >= 0.3f) {
            kind = now;
            special = false;
            Switch(Next(kind), Settings.musicSwitchFade, false, Settings.musicSwitchFade);
        }

        Deck cur = decks[current];
        if (cur.source.clip != null && !cur.source.loop) {
            float left = cur.source.clip.length - cur.source.time;
            float fade = EndFade(cur.source.clip);
            // Near the end: this one fades out over what is left, the next fades in.
            if (!cur.endingStarted && cur.source.isPlaying && left <= fade) {
                cur.endingStarted = true;
                if (special) special = false;
                Switch(Next(kind), Mathf.Max(0.05f, left), false, Settings.musicFadeIn);
            }
            // Over (or never started: a clip that could not play): the next one.
            else if (!cur.source.isPlaying && !cur.endingStarted && Time.unscaledTime - cur.startedAt > 1f) {
                cur.endingStarted = true;
                if (special) special = false;
                Switch(Next(kind), Settings.musicFadeIn, false, Settings.musicFadeIn);
            }
        } else if (cur.source.clip == null && kind != Kind.None && Time.unscaledTime >= retryAt) {
            // Nothing to play (an empty folder): look again now and then (tracks may come with a faction's turn).
            retryAt = Time.unscaledTime + 5f;
            AudioClip c = Next(kind);
            if (c != null) Switch(c, Settings.musicFadeIn, false, Settings.musicFadeIn);
        }

        float vol = Mathf.Clamp01(Settings.musicVolume) * GameOptions.MusicVolume;   // the mix level, times the options' music volume
        foreach (Deck d in decks) {
            d.level = Mathf.MoveTowards(d.level, d.target, d.speed * dt);
            d.source.volume = d.level * vol;
            if (d.target <= 0f && d.level <= 0f && d.source.isPlaying) d.source.Stop();
        }
    }

    float retryAt, seenSince;
    Kind seen = Kind.None;

    /// <summary>The end fade for a clip: the setting, but never more than a third of a short track.</summary>
    static float EndFade(AudioClip clip) { return Mathf.Min(Mathf.Max(0f, Settings.musicEndFade), clip.length / 3f); }

    /// <summary>What the music should be for now, from the scene and the game.</summary>
    static Kind Context() {
        GameController gc = GameController.gc;
        if (gc == null) return Kind.Menu;   // the main menu (or any scene without a game)
        GameSession session = GameSession.Instance;
        if (session != null && (session.Mode == SessionMode.Setup || session.Mode == SessionMode.Editor)) return Kind.Menu;
        if (gc.gameOver) return HumanWon(gc) ? Kind.Victory : Kind.Loss;
        return Kind.General;
    }

    static bool HumanWon(GameController gc) {
        if (gc.winner == null) return false;
        bool anyHuman = false;
        if (gc.players != null) foreach (Player p in gc.players) if (p != null && FogOfWar.IsHuman(p)) anyHuman = true;
        return !anyHuman || FogOfWar.IsHuman(gc.winner);
    }

    /// <summary>The next track for <paramref name="k"/>: at random, not the one just played if there is another.</summary>
    AudioClip Next(Kind k) {
        switch (k) {
            case Kind.Menu: return Pick(Folder("Menu"));
            case Kind.Victory: return Pick(Folder("Victory"));
            case Kind.Loss: return Pick(Folder("Loss"));
            case Kind.General: {
                List<AudioClip> all = new List<AudioClip>(Folder("General"));
                GameController gc = GameController.gc;
                Faction f = gc != null && gc.activePlayer != null ? gc.activePlayer.faction : null;
                if (f != null) all.AddRange(Folder("Faction/" + f.name));
                return Pick(all.ToArray());
            }
            default: return null;
        }
    }

    AudioClip Pick(AudioClip[] clips) {
        if (clips == null || clips.Length == 0) return null;
        List<AudioClip> choice = new List<AudioClip>();
        foreach (AudioClip c in clips) if (c != null && (c != lastClip || clips.Length == 1)) choice.Add(c);
        if (choice.Count == 0) return clips[0];
        return choice[Random.Range(0, choice.Count)];
    }

    /// <summary>The clips in Resources/Music/<paramref name="folder"/> (loaded once; streamed, see MusicImport).</summary>
    AudioClip[] Folder(string folder) {
        AudioClip[] clips;
        if (!folders.TryGetValue(folder, out clips)) {
            clips = Resources.LoadAll<AudioClip>(Root + folder);
            folders[folder] = clips;
        }
        return clips;
    }

    /// <summary>
    /// Fade the playing track out over <paramref name="fadeOut"/> seconds and <paramref name="clip"/> in on the other
    /// deck over <paramref name="fadeIn"/>. Null: the music only fades out.
    /// </summary>
    void Switch(AudioClip clip, float fadeOut, bool loop, float fadeIn) {
        Deck old = decks[current];
        old.target = 0f;
        old.speed = fadeOut > 0f ? Mathf.Max(old.level, 0.01f) / fadeOut : 1000f;
        current = 1 - current;
        Deck d = decks[current];
        d.source.Stop();
        d.source.clip = clip;
        d.source.loop = loop;
        d.endingStarted = false;
        d.level = 0f;
        if (clip == null) { d.target = 0f; return; }
        d.target = 1f;
        d.speed = 1f / Mathf.Max(0.05f, fadeIn);
        d.startedAt = Time.unscaledTime;
        d.source.Play();
        lastClip = clip;
    }
}
