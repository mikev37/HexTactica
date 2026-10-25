using UnityEngine;

/// <summary>
/// Whose turn it is, on the screen: a line at the top centre all game ("Red — your turn, day 3",
/// or "Blue is moving…" while an AI plays), and, as a human player's turn begins, a banner across
/// the middle of the screen in that player's colour that is gone after a second. Under fog of
/// war the board alone does not show when the other side has finished.
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

    Player lastPlayer;
    int lastDay = -1;
    float bannerAt = -100f;
    string bannerText;
    Color bannerColour;
    GUIStyle big, small;

    /// <summary>The line at the top for <paramref name="p"/>'s turn (<paramref name="day"/> counted from 0).</summary>
    public static string Line(Player p, int day, bool human) {
        if (p == null) return "";
        return human ? p.personal_name + " \u2014 your turn, day " + (day + 1) : p.personal_name + " is moving\u2026";
    }

    void Update() {
        GameController gc = GameController.gc;
        if (gc == null) return;
        Player p = gc.activePlayer;
        if (p == lastPlayer && gc.days == lastDay) return;
        lastPlayer = p; lastDay = gc.days;
        if (p != null && FogOfWar.IsHuman(p) && !gc.gameOver) {
            bannerAt = Time.unscaledTime;
            bannerText = p.personal_name + "'s turn";
            bannerColour = p.displayColor;
        }
    }

    void OnGUI() {
        GameController gc = GameController.gc;
        if (gc == null || gc.activePlayer == null || gc.gameOver) return;
        LevelEditor le = LevelEditor.singleton;
        if (le != null && (le.editing || le.unit_placement || le.building_placement)) return;
        if (big == null) {
            big = new GUIStyle(GUI.skin.label) { fontSize = 54, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            small = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }
        Color was = GUI.color;
        Player p = gc.activePlayer;
        bool human = FogOfWar.IsHuman(p);

        // The line at the top, all the time.
        string line = Line(p, gc.days, human);
        Rect top = new Rect(Screen.width * 0.5f - 220f, 10f, 440f, 34f);
        GUI.color = new Color(0.07f, 0.08f, 0.1f, 0.78f);
        GUI.DrawTexture(top, Texture2D.whiteTexture);
        GUI.color = new Color(p.displayColor.r, p.displayColor.g, p.displayColor.b, 1f);
        GUI.DrawTexture(new Rect(top.x, top.y, 6f, top.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        small.normal.textColor = Color.white;
        GUI.Label(top, line, small);

        // The banner as a human's turn begins.
        float t = Time.unscaledTime - bannerAt;
        float a = t < 0f ? 0f : t < fadeIn ? t / fadeIn : t < fadeIn + hold ? 1f : 1f - (t - fadeIn - hold) / fadeOut;
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
