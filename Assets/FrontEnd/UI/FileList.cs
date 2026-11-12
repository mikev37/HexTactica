using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// A list of scenarios or saved games (GameFiles), one row per file made from the row template (a child the scene
/// holds, hidden), with the picked file's details. The panel around it reads <see cref="Selected"/> when its
/// Play / Load / Edit button is pressed.
/// </summary>
public class FileList : MonoBehaviour {
    public enum Source { Scenarios, Saves, Recordings, Editable }

    [Tooltip("Which files it lists when it opens (ShowScenarios and ShowSaves switch).")]
    public Source source = Source.Scenarios;
    [Tooltip("The row each file is shown in: a hidden child of Content, copied for every file.")]
    public FileListRow rowTemplate;
    [Tooltip("Where the rows go (a scroll view's content, with a vertical layout).")]
    public RectTransform content;
    [Tooltip("The picked file's details (sides, board, fog, purchases, date). Optional.")]
    public TMP_Text details;
    [Tooltip("Shown in the details when nothing is picked.")]
    public string nothingPicked = "Pick a file.";
    [Tooltip("Shown in the details when the list is empty.")]
    public string empty = "Nothing here yet.";
    [Tooltip("Shown on a row of a file the player made (not shipped with the game).")]
    public string userTag = "yours";
    [Tooltip("Shown on a row of a campaign mission's scenario (the map editor's lists).")]
    public string campaignTag = "campaign";
    public Color rowColor = new Color(1f, 1f, 1f, 0.08f);
    public Color pickedColor = new Color(0.36f, 0.62f, 0.95f, 0.55f);
    [Tooltip("Called when the pick changes (to enable the panel's buttons).")]
    public UnityEvent onPicked = new UnityEvent();

    readonly List<FileListRow> rows = new List<FileListRow>();
    GameFile picked;

    /// <summary>The picked file's path, or null.</summary>
    public string Selected { get { return picked != null ? picked.path : null; } }
    public GameFile SelectedFile { get { return picked; } }

    void Awake() { if (rowTemplate != null && rowTemplate.gameObject.scene.IsValid()) rowTemplate.gameObject.SetActive(false); }   // a template in the scene; a prefab is left alone
    void OnEnable() { Refresh(); }

    public void ShowScenarios() { source = Source.Scenarios; Refresh(); }
    public void ShowSaves() { source = Source.Saves; Refresh(); }
    public void ShowRecordings() { source = Source.Recordings; Refresh(); }
    /// <summary>What the map editor can open: the scenarios and the campaign's missions.</summary>
    public void ShowEditable() { source = Source.Editable; Refresh(); }

    /// <summary>Read the folder again (a file saved meanwhile shows; the pick is kept if it is still there).</summary>
    public void Refresh() {
        string keep = Selected;
        foreach (FileListRow r in rows) if (r != null) Destroy(r.gameObject);
        rows.Clear();
        picked = null;
        if (rowTemplate == null || content == null) return;
        List<GameFile> files = source == Source.Saves ? GameFiles.Saves() : source == Source.Recordings ? GameFiles.Recordings() : source == Source.Editable ? GameFiles.Editable() : GameFiles.Scenarios();
        foreach (GameFile f in files) {
            FileListRow row = Instantiate(rowTemplate, content);
            row.gameObject.SetActive(true);
            row.name = f.name;
            row.Show(f, f.campaign ? campaignTag : f.user && source != Source.Saves && source != Source.Recordings ? userTag : "", rowColor);
            GameFile file = f;
            if (row.button != null) row.button.onClick.AddListener(() => Pick(file));
            rows.Add(row);
        }
        GameFile again = files.Find(x => x.path == keep);
        if (again != null) Pick(again);
        else {
            if (details != null) details.text = files.Count == 0 ? empty : nothingPicked;
            onPicked.Invoke();
        }
    }

    public void Pick(GameFile f) {
        picked = f;
        foreach (FileListRow r in rows) if (r != null) r.Highlight(r.File == f ? pickedColor : rowColor);
        if (details != null) details.text = f != null ? f.name + "\n" + GameFiles.Describe(f.path) : nothingPicked;
        onPicked.Invoke();
    }

    /// <summary>Pick the file of this name, if listed.</summary>
    public void PickNamed(string name) {
        foreach (FileListRow r in rows) if (r != null && r.File != null && r.File.name == name) { Pick(r.File); return; }
    }
}
