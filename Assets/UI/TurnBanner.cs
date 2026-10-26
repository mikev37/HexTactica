using UnityEngine;

/// <summary>
/// Whose turn it is, on the screen: a line at the top centre all game ("Red — your turn, day 3";
/// while an AI plays, "Blue is thinking..." as it works out its next move and "Blue is moving..."
/// as it carries it out, the dots going round), and, as a human player's turn begins, a banner across
/// the middle of the screen in that player's colour that is gone after a second. Under fog of
/// war the board alone does not show when the other side has finished.
///
/// When the game ends, a banner in the winner's colour says who won ("Red wins", or "Draw" when
/// nobody did), held for endHold seconds, and the line at the top says so from then on.
///
/// Spawned when a scene starts (not in the headless harness); not shown in the level editor.
/// </summary>
public class TurnBanner : MonoBehaviour {

    [Tooltip("Seconds the banner stays fully shown before fading.")]
    public float hold = 0.6f;
    [Tooltip("Seconds to fade in and out (with hold: one second in all).")]
    public float fadeIn = 0.15f, fadeOut = 0.25f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn() {
        if (GameController.gc != null && GameController.gc.headless) return;
        if (Object.FindAnyObjectByType<TurnBanner>() != null) return;
        new GameObject("TurnBanner").AddComponent<TurnBanner>();
    }

    [Tooltip("Seconds the end-of-game banner stays fully shown before fading.")]
    public float endHold = 4f;

    Player lastPlayer;
    int lastDay = -1;
    bool wasOver;
    float bannerHold;
    float bannerAt = -100f;
    string bannerText;
    Color bannerColour;
    GUIStyle big, small;

    /// <summary>
    /// The line at the top for <paramref name="p"/>'s turn (<paramref name="day"/> counted from 0): yours,
    /// or an AI's — thinking (working out its next move) or moving (carrying one out).
    /// </summary>
    /// <summary>What the end of the game says: "<paramref name="winner"/> wins", or "Draw" (no winner).</summary>
    public static string EndLine(Player winner) {
        return winner != null ? winner.personal_name + " wins" : "Draw";
    }

    public static string Line(Player p, int day, bool human, bool thinking = false) {
        if (p == null) return "";
        if (human) return p.personal_name + " \u2014 your turn, day " + (day + 1);
        return p.personal_name + (thinking ? " is thinking" : " is moving");
    }

    [Tooltip("An AI's thought must run this long (seconds) before the line says it is thinking: the quick ones between moves do not flicker it.")]
    public float thinkingAfter = 0.25f;
    [Tooltip("Seconds the line keeps saying thinking after a thought ends, unless it is acting.")]
    public float thinkingLinger = 0.4f;
    float lastThinking = -100f;

    /// <summary>Is <paramref name="p"/> (an AI) thinking, as the line should say it?</summary>
    bool Thinking(Player p) {
        AIPlayer ai = p.GetComponent<AIPlayer>();
        float f = ai != null && ai.enabled ? ai.ThinkingFor : -1f;
        // A unit of its still on the way: moving, whatever it is working out meanwhile.
        bool acting = GameController.gc != null && GameController.gc.activeUnit != null && GameController.gc.activeUnit.path != null && GameController.gc.activeUnit.path.Count > 0;
        if (acting) return false;
        if (f >= thinkingAfter) lastThinking = Time.unscaledTime;
        return Time.unscaledTime - lastThinking < thinkingLinger;
    }

    void Update() {
        GameController gc = GameController.gc;
        if (gc == null) return;
        Player p = gc.activePlayer;
        if (gc.gameOver && !wasOver) {
            bannerAt = Time.unscaledTime;
            bannerHold = endHold;
            bannerText = EndLine(gc.winner);
            bannerColour = gc.winner != null ? gc.winner.displayColor : Color.white;
        }
        wasOver = gc.gameOver;
        if (gc.gameOver) return;
        if (p == lastPlayer && gc.days == lastDay) return;
        lastPlayer = p; lastDay = gc.days;
        if (p != null && FogOfWar.IsHuman(p) && !gc.gameOver) {
            bannerAt = Time.unscaledTime;
            bannerText = p.personal_name + "'s turn";
            bannerColour = p.displayColor;
            bannerHold = hold;
        }
    }

    void OnGUI() {
        GameController gc = GameController.gc;
        if (gc == null || (gc.activePlayer == null && !gc.gameOver)) return;
        LevelEditor le = LevelEditor.singleton;
        if (le != null && (le.editing || le.unit_placement || le.building_placement)) return;
        if (big == null) {
            big = new GUIStyle(GUI.skin.label) { fontSize = 54, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            small = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }
        Color was = GUI.color;
        Player p = gc.gameOver ? gc.winner : gc.activePlayer;
        bool human = p != null && FogOfWar.IsHuman(p);

        // The line at the top, all the time: whose turn, or who won.
        string line;
        if (gc.gameOver) line = EndLine(gc.winner);
        else {
            line = Line(p, gc.days, human, !human && Thinking(p));
            // An AI's line ends in dots that keep going, so it never looks stuck.
            if (!human) line += new string('.', 1 + (int)(Time.unscaledTime * 3f) % 3);
        }
        Color stripe = p != null ? p.displayColor : Color.white;
        Rect top = new Rect(Screen.width * 0.5f - 220f, 10f, 440f, 34f);
        GUI.color = new Color(0.07f, 0.08f, 0.1f, 0.78f);
        GUI.DrawTexture(top, Texture2D.whiteTexture);
        GUI.color = new Color(stripe.r, stripe.g, stripe.b, 1f);
        GUI.DrawTexture(new Rect(top.x, top.y, 6f, top.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        small.normal.textColor = Color.white;
        GUI.Label(top, line, small);

        // The banner as a human's turn begins.
        float t = Time.unscaledTime - bannerAt;
        float a = t < 0f ? 0f : t < fadeIn ? t / fadeIn : t < fadeIn + bannerHold ? 1f : 1f - (t - fadeIn - bannerHold) / fadeOut;
        if (a > 0f && bannerText != null) {
            Rect band = new Rect(0f, Screen.height * 0.5f - 55f, Screen.width, 110f);
            GUI.color = new Color(0.05f, 0.06f, 0.08f, 0.72f * a);
            GUI.DrawTexture(band, Texture2D.whiteTexture);
            GUI.color = new Color(bannerColour.r, bannerColour.g, bannerColour.b, 0.9f * a);
            GUI.DrawTexture(new Rect(0f, band.y, Screen.width, 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, band.yMax - 4f, Screen.width, 4f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, a);
            big.normal.textColor = Color.white;
            GUI.Label(band, bannerText, big);
        }
        GUI.color = was;
    }
}
