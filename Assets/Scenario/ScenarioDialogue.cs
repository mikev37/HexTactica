using System.Collections.Generic;
using UnityEngine;

/// <summary>Shows dialogue lines (the dialogue box, DialogueBox): told when a line is to be shown, and when the conversation is over.</summary>
public interface IDialoguePresenter {
    /// <summary>Show <paramref name="line"/> (with its portrait and sound), in place of the one before.</summary>
    void ShowLine(DialogueLine line);
    /// <summary>No line is left: close the box.</summary>
    void Close();
}

/// <summary>
/// The scenario's conversation: lines from Dialogue actions wait in a queue and are shown one at a time, each until the
/// player moves on (Next), so two triggers that speak at once speak one after the other, never over each other. While
/// a line is shown the game is held (GameController.ScriptHold): no side plays and the board takes no clicks.
/// The screen's part is a presenter (DialogueBox, on the Dialogue prefab). With none (a headless run, or no prefab),
/// each line is written to the log and passes at once, so the game never waits on a box nobody can see.
/// </summary>
public static class ScenarioDialogue {

    /// <summary>Where sounds for lines and Play Sound actions are, under Resources (by clip name).</summary>
    public const string SoundFolder = "Dialogue/";
    /// <summary>Where Dialogue Character assets are, under Resources (by asset name).</summary>
    public const string CharacterFolder = "Characters/";

    struct Queued { public DialogueLine line; public int serial; }

    static readonly Queue<Queued> queue = new Queue<Queued>();
    static Queued current;
    static bool showing;
    static int lastSerial, lastClosed;
    static IDialoguePresenter presenter;
    static bool warnedNoPresenter;

    /// <summary>The box on screen; null when it goes. Lines already waiting are shown by the new one.</summary>
    public static IDialoguePresenter Presenter {
        get { return presenter; }
        set {
            presenter = value;
            if (presenter != null && !showing && queue.Count > 0) Advance();
        }
    }

    /// <summary>A line is on screen (the game is held).</summary>
    public static bool Showing { get { return showing; } }

    /// <summary>The line on screen, or null.</summary>
    public static DialogueLine Current { get { return showing ? current.line : null; } }

    /// <summary>Lines waiting after the one on screen.</summary>
    public static int Waiting { get { return queue.Count; } }

    /// <summary>
    /// Queue <paramref name="lines"/>; shown at once if nothing is. Returns the serial of the last one (0 if none): the
    /// caller may wait for it (<see cref="Closed"/>).
    /// </summary>
    public static int Enqueue(IList<DialogueLine> lines) {
        int last = 0;
        if (lines == null) return last;
        foreach (DialogueLine l in lines) {
            if (l == null) continue;
            last = ++lastSerial;
            queue.Enqueue(new Queued { line = l, serial = last });
        }
        if (!showing) Advance();
        return last;
    }

    /// <summary>Has the line with this serial been closed (and every line before it)?</summary>
    public static bool Closed(int serial) { return serial <= 0 || lastClosed >= serial; }

    /// <summary>The player is done with the line on screen: the next one, or the box closes and the game goes on.</summary>
    public static void Next() {
        if (!showing) return;
        lastClosed = current.serial;
        showing = false;
        Advance();
    }

    static void Advance() {
        while (queue.Count > 0) {
            current = queue.Dequeue();
            if (presenter == null) {
                // Nobody can see it: logged, and closed at once.
                GameController gc = GameController.gc;
                if (!warnedNoPresenter && (gc == null || !gc.headless)) {
                    warnedNoPresenter = true;
                    Debug.LogWarning("Scenario dialogue: no dialogue box (Assets/Resources/" + DialogueBoxPrefabName + ".prefab): lines go to the log and the game does not wait for them.");
                }
                Debug.Log("Dialogue: " + (string.IsNullOrEmpty(current.line.Character) ? "" : current.line.Character + ": ") + current.line.Text);
                lastClosed = current.serial;
                continue;
            }
            showing = true;
            GameController.ScriptHold = true;
            presenter.ShowLine(current.line);
            return;
        }
        showing = false;
        GameController.ScriptHold = false;
        if (presenter != null) presenter.Close();
    }

    /// <summary>The prefab the dialogue box is made from (Assets/Resources/Dialogue.prefab).</summary>
    public const string DialogueBoxPrefabName = "Dialogue";

    /// <summary>The line on screen and those waiting (for a save), oldest first.</summary>
    public static List<DialogueLine> Pending() {
        List<DialogueLine> r = new List<DialogueLine>();
        if (showing) r.Add(current.line);
        foreach (Queued q in queue) r.Add(q.line);
        return r;
    }

    /// <summary>Everything dropped (a board is loaded): nothing shown, nothing waiting, the game not held.</summary>
    public static void Clear() {
        queue.Clear();
        bool was = showing;
        showing = false;
        lastClosed = lastSerial;
        GameController.ScriptHold = false;
        if (was && presenter != null) presenter.Close();
    }
}
