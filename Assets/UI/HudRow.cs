using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One line of a HUD panel (Hud): an icon, a label, then either a row of bullets (values whose
/// cap is small: Hud.bulletLimit) or a number. Bullets are filled for the value, empty for the rest
/// of the cap, green for a good value and red for a bad one (Hud's colours).
/// Any of the parts may be left unset in the prefab.
/// </summary>
public class HudRow : MonoBehaviour {

    public Image icon;
    public TMP_Text label;
    [Tooltip("Parent of the bullets (laid out by a HorizontalLayoutGroup).")]
    public RectTransform bullets;
    [Tooltip("Copied once per bullet; hidden itself.")]
    public Image bulletTemplate;
    public TMP_Text value;

    readonly List<Image> made = new List<Image>();
    string shown;

    public void Show(bool on) { if (gameObject.activeSelf != on) gameObject.SetActive(on); }

    /// <summary>
    /// The value as bullets (cap below the HUD's bullet limit) or as "value/cap". A negative value
    /// is drawn as that many red bullets; <paramref name="badWhenPositive"/> marks a value that hurts
    /// (a move cost), drawn red however it is signed.
    /// </summary>
    public void Set(int v, int cap, bool badWhenPositive, Hud hud) {
        Show(true);
        bool asBullets = cap > 0 && cap < hud.bulletLimit;
        string key = v + "/" + cap + "/" + badWhenPositive + "/" + asBullets;
        if (key == shown) return;
        shown = key;
        if (asBullets) {
            bool good = v >= 0 && !badWhenPositive;
            int filled = Mathf.Min(Mathf.Abs(v), Mathf.Max(cap, 1));
            int total = Mathf.Max(cap, filled);
            Bullets(total, filled, good ? hud.positive : hud.negative, hud);
            if (value != null) value.gameObject.SetActive(false);
        } else {
            Bullets(0, 0, Color.white, hud);
            if (value != null) {
                value.gameObject.SetActive(true);
                value.text = cap > 0 ? v + "/" + cap : v.ToString();
                value.color = v <= 0 ? hud.negative : hud.text;
            }
        }
    }

    /// <summary>A line of text instead of a value (a terrain's height, a building's name).</summary>
    public void SetText(string s, Color c) {
        Show(true);
        string key = "text:" + s + c;
        if (key == shown) return;
        shown = key;
        Bullets(0, 0, Color.white, null);
        if (value != null) { value.gameObject.SetActive(true); value.text = s; value.color = c; }
    }

    void Bullets(int total, int filled, Color c, Hud hud) {
        if (bullets == null || bulletTemplate == null) return;
        bulletTemplate.gameObject.SetActive(false);
        bullets.gameObject.SetActive(total > 0);
        while (made.Count < total) {
            Image b = GameObject.Instantiate(bulletTemplate, bullets);
            made.Add(b);
        }
        for (int i = 0; i < made.Count; i++) {
            Image b = made[i];
            bool on = i < total;
            if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
            if (!on) continue;
            bool full = i < filled;
            if (hud != null && hud.bulletEmpty != null) b.sprite = full ? hud.bulletFull : hud.bulletEmpty;
            b.color = full ? c : (hud != null ? hud.empty : new Color(1f, 1f, 1f, 0.25f));
        }
    }
}
