using UnityEngine;

/// <summary>
/// Someone who speaks in a scenario's dialogue: one asset per character in Assets/Resources/Characters, named by its
/// asset name in a dialogue line (DialogueLine.Character). The dialogue box shows its portrait beside the text and its
/// name above it.
/// </summary>
[CreateAssetMenu(menuName = "HexTactica/Dialogue Character", fileName = "Character")]
public class DialogueCharacter : ScriptableObject {
    [Tooltip("The name shown above what they say (empty: the asset's name).")]
    public string displayName = "";
    [Tooltip("Their picture beside the text: a full-body portrait, its feet at the bottom of the box. Empty: no picture.")]
    public Sprite portrait;
    [Tooltip("The colour of their name.")]
    public Color nameColour = Color.white;
    [Tooltip("Played with each of their lines that has no sound of its own (a voice blip, a radio crackle). Empty: silent.")]
    public AudioClip voice;
    [Tooltip("The portrait faces left as drawn: it is mirrored when it stands on the left, so it faces the text (and the other way round).")]
    public bool facesLeft;

    public string ShownName { get { return string.IsNullOrEmpty(displayName) ? name : displayName; } }

    /// <summary>The character named <paramref name="assetName"/> in Resources/Characters, or null.</summary>
    public static DialogueCharacter Find(string assetName) {
        if (string.IsNullOrEmpty(assetName)) return null;
        return Resources.Load<DialogueCharacter>(ScenarioDialogue.CharacterFolder + assetName.Trim());
    }
}
