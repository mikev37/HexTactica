using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fades a unit in and out of sight under fog of war (added to every unit by Unit.Start). Seen from
/// the screen's player (FogOfWar.Viewer):
///   - an enemy it does not see: invisible (opacity 0), judged where the sprite is, so it appears as
///     it walks into sight and fades as it walks out;
///   - one of its own units that no enemy sees: half-faded (FogSettings.ownHiddenAlpha);
///   - anything else: as it is.
/// Changes fade over FogSettings.fadeSeconds.
///
/// Every part of the unit fades together, whatever draws it and whatever else sets its colour: its
/// sprites (shadows, pips, team colours — the opacity multiplies the colour other scripts give them,
/// each frame), lines (treads), its canvases (through a CanvasGroup: damage numbers, alarms), and
/// anything else that renders (smoke): those are switched off while it is hidden. Units it carries
/// fade with their own fader, as their transport does.
/// </summary>
public class FogFader : MonoBehaviour {

    Unit unit;
    float alpha = 1f, target = 1f;
    bool started, idle = true;
    float rescanAt;

    class Tint { public Color baseColor, applied; }
    class Line { public Gradient baseGradient; }

    readonly Dictionary<SpriteRenderer, Tint> sprites = new Dictionary<SpriteRenderer, Tint>();
    readonly Dictionary<LineRenderer, Line> lines = new Dictionary<LineRenderer, Line>();
    readonly Dictionary<CanvasGroup, float> groups = new Dictionary<CanvasGroup, float>();
    readonly List<Renderer> switchedOff = new List<Renderer>();
    readonly List<Renderer> others = new List<Renderer>();
    /// <summary>Shadow sprites (SpriteShadow's copies, the ground shadow): switched off while the unit is faded at all — at part opacity their stacked copies only darken it.</summary>
    readonly List<SpriteRenderer> shadows = new List<SpriteRenderer>();

    /// <summary>Is the unit shown at all (or on its way in)? Explosions and the like are not played for one that is not.</summary>
    public bool Shown { get { return alpha > 0.01f || target > 0.01f; } }

    /// <summary>The opacity the unit is drawn at now (1: as it is).</summary>
    public float Alpha { get { return alpha; } }

    void Awake() { unit = GetComponent<Unit>(); }

    /// <summary>Where the unit's opacity should be heading.</summary>
    public static float TargetFor(Unit u) {
        if (u == null || !FogOfWar.On) return 1f;
        Player v = FogOfWar.Viewer;
        if (v == null) return 1f;
        // Getting on or off (walking to it, or riding to where it is put down): judged where its sprite is.
        if (u.owner != v && (u.BoardingSpriteTile != null || u.RidingOffWith != null)) return FogOfWar.ShownToViewer(u) ? 1f : 0f;
        if (u.trasnporter != null && u.trasnporter != u) {
            // Aboard: as its transport is drawn.
            return TargetFor(u.trasnporter);
        }
        // Our own: half-faded only a stealthy unit (stealthRange) that no enemy sees.
        if (u.owner == v) return u.stealthRange > 0 && FogOfWar.HiddenFromEnemies(u) ? FogSettings.Instance.ownHiddenAlpha : 1f;
        return FogOfWar.ShownToViewer(u) ? 1f : 0f;
    }

    void LateUpdate() {
        if (unit == null) return;
        // Dead or dying: keep whatever it was (a unit killed in sight plays its death in sight).
        if (unit.Strength > 0 && unit.tile != null) target = TargetFor(unit);
        if (!started) { started = true; alpha = target; }
        float seconds = Mathf.Max(0.01f, FogSettings.Instance.fadeSeconds);
        alpha = Mathf.MoveTowards(alpha, target, Time.deltaTime / seconds);

        if (alpha >= 0.999f && target >= 0.999f) {
            if (!idle) { Apply(1f); Forget(); idle = true; }
            return;
        }
        if (idle || Time.time >= rescanAt) { Scan(); rescanAt = Time.time + 0.5f; idle = false; }
        Apply(alpha);
    }

    /// <summary>Collect what draws the unit (not what draws the units it carries).</summary>
    void Scan() {
        foreach (SpriteRenderer r in GetComponentsInChildren<SpriteRenderer>(true)) {
            if (r == null || !Mine(r)) continue;
            if (IsShadow(r)) { if (!shadows.Contains(r)) shadows.Add(r); continue; }
            if (!sprites.ContainsKey(r)) sprites[r] = new Tint { baseColor = r.color, applied = r.color };
        }
        foreach (LineRenderer r in GetComponentsInChildren<LineRenderer>(true))
            if (r != null && Mine(r) && !lines.ContainsKey(r)) lines[r] = new Line { baseGradient = Copy(r.colorGradient) };
        foreach (Canvas c in GetComponentsInChildren<Canvas>(true)) {
            if (c == null || !Mine(c)) continue;
            CanvasGroup g = c.GetComponent<CanvasGroup>();
            if (g == null) { g = c.gameObject.AddComponent<CanvasGroup>(); g.alpha = 1f; g.blocksRaycasts = false; g.interactable = false; }
            if (!groups.ContainsKey(g)) groups[g] = g.alpha;
        }
        others.Clear();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r != null && Mine(r) && !(r is SpriteRenderer) && !(r is LineRenderer)) others.Add(r);
    }

    bool Mine(Component c) { return c.GetComponentInParent<Unit>() == unit; }

    /// <summary>A shadow: under an object named Shadow (SpriteShadow makes them) or the unit's "shadow".</summary>
    bool IsShadow(SpriteRenderer r) {
        for (Transform t = r.transform; t != null && t != unit.transform; t = t.parent)
            if (t.name.StartsWith("Shadow", System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    void Apply(float a) {
        foreach (KeyValuePair<SpriteRenderer, Tint> kv in sprites) {
            SpriteRenderer r = kv.Key;
            if (r == null) continue;
            Tint t = kv.Value;
            // Another script set the colour since we last did: that is the colour to fade.
            if (r.color != t.applied) t.baseColor = r.color;
            Color c = t.baseColor;
            c.a *= a;
            r.color = c;
            t.applied = c;
        }
        foreach (KeyValuePair<LineRenderer, Line> kv in lines) {
            if (kv.Key == null || kv.Value.baseGradient == null) continue;
            kv.Key.colorGradient = Faded(kv.Value.baseGradient, a);
        }
        foreach (KeyValuePair<CanvasGroup, float> kv in groups) if (kv.Key != null) kv.Key.alpha = kv.Value * a;
        // Shadows go while it is faded at all; they come back with it.
        bool shadowOff = a < 0.999f;
        foreach (SpriteRenderer r in shadows) {
            if (r == null) continue;
            if (shadowOff && r.enabled) { r.enabled = false; if (!switchedOff.Contains(r)) switchedOff.Add(r); }
            else if (!shadowOff && !r.enabled && switchedOff.Contains(r)) { r.enabled = true; switchedOff.Remove(r); }
        }
        // What cannot be faded is switched off while the unit is mostly gone, and back on after.
        bool off = a < 0.05f;
        foreach (Renderer r in others) {
            if (r == null) continue;
            if (off && r.enabled) { r.enabled = false; if (!switchedOff.Contains(r)) switchedOff.Add(r); }
            else if (!off && !r.enabled && switchedOff.Contains(r)) { r.enabled = true; switchedOff.Remove(r); }
        }
    }

    void Forget() {
        sprites.Clear();
        lines.Clear();
        groups.Clear();
        for (int i = switchedOff.Count - 1; i >= 0; i--) if (switchedOff[i] != null) switchedOff[i].enabled = true;
        switchedOff.Clear();
        others.Clear();
        shadows.Clear();
    }

    static Gradient Copy(Gradient g) {
        if (g == null) return null;
        Gradient c = new Gradient();
        c.SetKeys(g.colorKeys, g.alphaKeys);
        return c;
    }

    static Gradient Faded(Gradient g, float a) {
        GradientAlphaKey[] keys = g.alphaKeys;
        for (int i = 0; i < keys.Length; i++) keys[i].alpha *= a;
        Gradient c = new Gradient();
        c.SetKeys(g.colorKeys, keys);
        return c;
    }
}
