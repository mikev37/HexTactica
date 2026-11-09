using UnityEngine;

/// <summary>
/// Someone who speaks in a scenario's dialogue: one asset per character in Assets/Resources/Characters, named by its
/// asset name in a dialogue line (DialogueLine.Character). The dialogue box shows its portrait beside the text and its
/// name above it.
/// </summary>
[CreateAssetMenu(menuName = "HexTactica/Dialogue Character", fileName = "Character")]
public class DialogueCharacter : ScriptableObject {
    /// <summary>Which way a portrait looks as drawn. Saved by number: append only.</summary>
    public enum PortraitFacing { Right = 0, Left = 1 }

    [Tooltip("The name shown above what they say (empty: the asset's name).")]
    public string displayName = "";
    [Tooltip("Their picture beside the text: a full-body portrait, its feet at the bottom of the box. Empty: no picture.")]
    public Sprite portrait;
    [Tooltip("The colour of their name.")]
    public Color nameColour = Color.white;
    [Tooltip("Which way the person in the portrait looks, as drawn. The box mirrors the picture when needed so they always look toward the text: a portrait drawn looking right is mirrored when it stands on the right.")]
    public PortraitFacing drawnFacing = PortraitFacing.Right;

    public string ShownName { get { return string.IsNullOrEmpty(displayName) ? name : displayName; } }

    /// <summary>The character a line names (its name in Resources/Characters, or its path under any Resources folder: ScenarioAssets), or null.</summary>
    public static DialogueCharacter Find(string assetName) {
        if (string.IsNullOrEmpty(assetName)) return null;
        return ScenarioAssets.Load<DialogueCharacter>(assetName, ScenarioDialogue.CharacterFolder);
    }
}
