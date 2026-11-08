using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One file in a FileList: its name, a note (when it was saved, or that it is the player's own), a background that shows the pick.</summary>
public class FileListRow : MonoBehaviour {
    public Button button;
    public TMP_Text label;
    [Tooltip("The date it was saved, and a tag for the player's own scenarios. Optional.")]
    public TMP_Text note;
    [Tooltip("Tinted to show the row is picked.")]
    public Image background;

    public GameFile File { get; private set; }

    public void Show(GameFile f, string tag, Color normal) {
        File = f;
        if (label != null) label.text = f.name;
        if (note != null) note.text = (tag.Length > 0 ? tag + "  " : "") + f.modified.ToString("yyyy-MM-dd HH:mm");
        Highlight(normal);
    }

    public void Highlight(Color c) { if (background != null) background.color = c; }
}
