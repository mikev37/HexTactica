using UnityEngine;

namespace HexTactica.Harness {

    /// <summary>
    /// The replay's controls (Replay), on the screen while one runs: the turn shown and how it is
    /// seen, buttons to step back and forth (or the arrow keys; Home and End for the first and last
    /// turn; V to change whose eyes), and the log of that turn — what was moved and fought, and
    /// the AI's reasoning — in a scrolling list on the left.
    /// </summary>
    public class ReplayPanel : MonoBehaviour {

        public static void Ensure() {
            if (GameController.gc != null && GameController.gc.headless) return;
            if (Object.FindAnyObjectByType<ReplayPanel>() != null) return;
            new GameObject("ReplayPanel").AddComponent<ReplayPanel>();
        }

        Vector2 scroll;
        int shownIndex = -1;
        GUIStyle text, title;

        void Update() {
            if (!Replay.Active) return;
            if (Input.GetKeyDown(KeyCode.RightArrow)) Replay.Next();
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) Replay.Previous();
            else if (Input.GetKeyDown(KeyCode.Home)) Replay.Show(0);
            else if (Input.GetKeyDown(KeyCode.End)) Replay.Show(Replay.turns.Count - 1);
            else if (Input.GetKeyDown(KeyCode.V)) Replay.CycleView();
        }

        void OnGUI() {
            if (!Replay.Active) return;
            if (text == null) {
                text = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            }
            if (shownIndex != Replay.Index) { scroll = Vector2.zero; shownIndex = Replay.Index; }
            Color was = GUI.color;
            Rect box = new Rect(10f, 54f, 430f, Mathf.Max(200f, Screen.height - 120f));
            GUI.color = new Color(0.07f, 0.08f, 0.1f, 0.85f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(box.x + 8f, box.y + 6f, box.width - 16f, box.height - 12f));
            GUILayout.Label("Replay: " + Replay.Title(), title);
            GUILayout.Label("Seen " + Replay.ViewName() + (Replay.Ending != null ? " — " + Replay.Ending : ""), text);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("|<")) Replay.Show(0);
            if (GUILayout.Button("< Back")) Replay.Previous();
            if (GUILayout.Button("Next >")) Replay.Next();
            if (GUILayout.Button(">|")) Replay.Show(Replay.turns.Count - 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Whose eyes (V)")) Replay.CycleView();
            if (GUILayout.Button("Close replay")) Replay.Close();
            GUILayout.EndHorizontal();
            GUILayout.Label("What happened this turn:", text);
            scroll = GUILayout.BeginScrollView(scroll);
            Replay.Turn t = Replay.Current;
            if (t != null) {
                if (t.events.Count == 0) GUILayout.Label("(nothing in the log)", text);
                foreach (string line in t.events) GUILayout.Label(line, text);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.color = was;
        }
    }
}
