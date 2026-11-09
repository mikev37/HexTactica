using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The dialogue box (Assets/Resources/Dialogue.prefab): a scenario's lines (ScenarioDialogue) on a dark, see-through
/// panel, the speaker's name above them and their full-body portrait beside them, with a Next button that moves on.
/// While it is open the game is held: no side plays and the board takes no clicks (its full-screen blocker catches
/// them too). Each line's sound plays on the box's own audio source and is stopped when the line is closed, so two
/// lines never talk over each other; lines from triggers that speak at once wait their turn.
///
/// The prefab is made once by HexTactica ▸ Scenario Triggers ▸ Recreate Dialogue Prefab (Editor/DialoguePrefab) and is
/// yours to restyle: the box finds its parts through the fields below. A part left empty is simply not used.
/// </summary>
public class DialogueBox : MonoBehaviour, IDialoguePresenter {

    [Header("Parts")]
    [Tooltip("Everything shown while a line is up (hidden between conversations).")]
    public GameObject root;
    [Tooltip("The line's text.")]
    public TMP_Text text;
    [Tooltip("The speaker's name.")]
    public TMP_Text speakerName;
    [Tooltip("Shown only when the line has a speaker (the plate behind the name).")]
    public GameObject namePlate;
    [Tooltip("The portrait on the left of the text (a speaker with Portrait: Left).")]
    public Image leftPortrait;
    [Tooltip("The portrait on the right of the text (a speaker with Portrait: Right).")]
    public Image rightPortrait;
    [Tooltip("Moves on to the next line (closes the box after the last).")]
    public Button nextButton;
    [Tooltip("The Next button's label.")]
    public TMP_Text nextLabel;
    [Tooltip("Plays each line's sound (stopped when the line is closed).")]
    public AudioSource voice;

    [Header("Behaviour")]
    [Tooltip("The Next button's label while more lines are waiting.")]
    public string nextText = "Next";
    [Tooltip("The Next button's label on the last line.")]
    public string closeText = "Close";
    [Tooltip("Keys that act as the Next button. (Not Space: Space also ends the turn.)")]
    public KeyCode[] advanceKeys = { KeyCode.Return, KeyCode.KeypadEnter };
    [Tooltip("Letters per second the text is written out at (0: the whole line at once). Next while it is being written shows the whole line first.")]
    public float lettersPerSecond = 60f;
    [Tooltip("Played as the box opens (empty: nothing).")]
    public AudioClip openSound;
    [Tooltip("Volume of the lines' sounds, under the options' sound effects volume.")]
    [Range(0, 1)] public float voiceVolume = 1f;
    [Tooltip("The speaker not talking (the other side's portrait, when the last line's speaker is still shown there) is dimmed to this colour; white: not dimmed.")]
    public Color listenerTint = new Color(0.45f, 0.45f, 0.5f, 1f);

    float shownSince;
    int letters;
    bool open;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook() { GameScene.WhenLoaded(Spawn); }

    static void Spawn() {
        if (GameController.gc != null && GameController.gc.headless) return;
        if (FindAnyObjectByType<DialogueBox>() != null) return;
        DialogueBox prefab = Resources.Load<DialogueBox>(ScenarioDialogue.DialogueBoxPrefabName);
        if (prefab == null) return;   // ScenarioDialogue says so when a line has nowhere to go
        DialogueBox box = Instantiate(prefab);
        box.name = ScenarioDialogue.DialogueBoxPrefabName;
        TransportPanel.EnsureEventSystem();
    }

    void Awake() {
        if (nextButton != null) nextButton.onClick.AddListener(Next);
        if (root != null) root.SetActive(false);
    }

    void OnEnable() { ScenarioDialogue.Presenter = this; }

    void OnDestroy() { if (ReferenceEquals(ScenarioDialogue.Presenter, this)) ScenarioDialogue.Presenter = null; }

    /// <summary>The Next button (and the advance keys): the whole line if it is still being written, else the next line.</summary>
    public void Next() {
        if (!open || GameController.MenuHeld) return;   // (a menu open over the game has the keys and clicks)
        if (text != null && Writing) { letters = int.MaxValue; text.maxVisibleCharacters = 99999; return; }
        StopVoice();
        ScenarioDialogue.Next();
    }

    bool Writing { get { return lettersPerSecond > 0f && text != null && letters < text.text.Length; } }

    void Update() {
        if (!open) return;
        if (advanceKeys != null) foreach (KeyCode k in advanceKeys) if (Input.GetKeyDown(k)) { Next(); break; }
        if (text != null && lettersPerSecond > 0f && letters < int.MaxValue) {
            letters = Mathf.FloorToInt((Time.unscaledTime - shownSince) * lettersPerSecond);
            text.maxVisibleCharacters = letters;
        }
        if (nextLabel != null) nextLabel.text = ScenarioDialogue.Waiting > 0 ? nextText : closeText;
    }

    public void ShowLine(DialogueLine line) {
        if (!open) {
            open = true;
            if (root != null) root.SetActive(true);
            if (openSound != null) GameSounds.Play(openSound);
        }
        DialogueCharacter who = DialogueCharacter.Find(line.Character);
        if (!string.IsNullOrEmpty(line.Character) && who == null)
            Debug.LogWarning("Dialogue: no character '" + line.Character + "' in Resources/" + ScenarioDialogue.CharacterFolder + ": the line is shown without a portrait.");
        string shown = who != null ? who.ShownName : (line.Character ?? "");
        if (speakerName != null) { speakerName.text = shown; if (who != null) speakerName.color = who.nameColour; }
        if (namePlate != null) namePlate.SetActive(shown.Length > 0);
        if (text != null) {
            text.text = line.Text ?? "";
            letters = lettersPerSecond > 0f ? 0 : int.MaxValue;
            text.maxVisibleCharacters = lettersPerSecond > 0f ? 0 : 99999;
        }
        shownSince = Time.unscaledTime;
        Image speaking = line.Portrait == PortraitSide.Right ? rightPortrait : leftPortrait;
        Image other = speaking == leftPortrait ? rightPortrait : leftPortrait;
        Sprite face = who != null ? who.portrait : null;
        if (speaking != null) {
            speaking.gameObject.SetActive(face != null);
            speaking.sprite = face;
            speaking.color = Color.white;
            // Facing the text: mirrored when the picture faces away from it.
            bool mirror = who != null && (who.facesLeft == (speaking == leftPortrait));
            Vector3 s = speaking.rectTransform.localScale;
            speaking.rectTransform.localScale = new Vector3(mirror ? -Mathf.Abs(s.x) : Mathf.Abs(s.x), s.y, s.z);
        }
        // The other side keeps the last speaker there, dimmed, as listening; a new conversation clears it.
        if (other != null && other.gameObject.activeSelf) other.color = listenerTint;
        PlayVoice(line, who);
    }

    void PlayVoice(DialogueLine line, DialogueCharacter who) {
        StopVoice();
        if (voice == null) return;
        AudioClip clip = null;
        if (!string.IsNullOrEmpty(line.Sound)) {
            clip = ScenarioAssets.Load<AudioClip>(line.Sound, ScenarioDialogue.SoundFolder);
            if (clip == null) Debug.LogWarning("Dialogue: no sound '" + line.Sound + "' in Resources/" + ScenarioDialogue.SoundFolder + ".");
        }
        if (clip == null && who != null) clip = who.voice;
        if (clip == null || !GameSounds.On) return;
        voice.clip = clip;
        voice.loop = false;
        voice.volume = voiceVolume * GameOptions.EffectsVolume;
        voice.Play();
    }

    void StopVoice() { if (voice != null && voice.isPlaying) voice.Stop(); }

    public void Close() {
        StopVoice();
        open = false;
        if (leftPortrait != null) leftPortrait.gameObject.SetActive(false);
        if (rightPortrait != null) rightPortrait.gameObject.SetActive(false);
        if (root != null) root.SetActive(false);
    }
}
