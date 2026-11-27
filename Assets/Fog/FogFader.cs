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
///
/// Cost: a unit fully shown, or fully hidden and staying so (an enemy in the fog), does no work a frame beyond working
/// out its target opacity: a hidden one has every renderer forced off (Renderer.forceRenderingOff) once, and is scanned
/// again only when a part is added under it. Only units fading, or held half-faded, set colours every frame, and
/// without making garbage.
/// </summary>
public class FogFader : MonoBehaviour {

    Unit unit;
    float alpha = 1f, target = 1f;
    bool started, idle = true;
    float rescanAt;
    /// <summary>Settled out of sight (opacity 0, staying there): every part is switched off (Renderer.forceRenderingOff,
    /// which no other script touches) and nothing is done each frame but notice the way back in or a new part.</summary>
    bool hiddenIdle;
    /// <summary>The unit's hierarchy size when last scanned while hidden: a new child (smoke, a chute, cargo) changes it.</summary>
    int hierarchySeen;

    class Tint { public Color baseColor, applied; public bool ui; }
    /// <summary>A line's own colours, kept with a gradient and key buffer reused every frame (no garbage).</summary>
    class Line { public GradientColorKey[] colors; public GradientAlphaKey[] alphas, work; public Gradient faded; }

    readonly Dictionary<SpriteRenderer, Tint> sprites = new Dictionary<SpriteRenderer, Tint>();
    readonly Dictionary<LineRenderer, Line> lines = new Dictionary<LineRenderer, Line>();
    readonly Dictionary<CanvasGroup, float> groups = new Dictionary<CanvasGroup, float>();
    readonly HashSet<CanvasGroup> uiGroups = new HashSet<CanvasGroup>();
    readonly List<Renderer> switchedOff = new List<Renderer>();
    readonly List<Renderer> others = new List<Renderer>();
    /// <summary>Shadow sprites (SpriteShadow's copies, the ground shadow): switched off while the unit is faded at all — at part opacity their stacked copies only darken it.</summary>
    readonly List<SpriteRenderer> shadows = new List<SpriteRenderer>();
    /// <summary>Renderers this fader forced off while the unit is hidden (hiddenIdle).</summary>
    readonly List<Renderer> forced = new List<Renderer>();

    // Scan's buffers, shared by every fader (all run on the main thread): no arrays made per scan.
    static readonly List<SpriteRenderer> spriteBuf = new List<SpriteRenderer>();
    static readonly List<LineRenderer> lineBuf = new List<LineRenderer>();
    static readonly List<Canvas> canvasBuf = new List<Canvas>();
    static readonly List<Renderer> rendererBuf = new List<Renderer>();

    /// <summary>Is the unit shown at all (or on its way in)? Explosions and the like are not played for one that is not.</summary>
    public bool Shown { get { return alpha > 0.01f || target > 0.01f; } }

    /// <summary>The opacity the unit is drawn at now (1: as it is).</summary>
    public float Alpha { get { return alpha; } }

    void Awake() { unit = GetComponent<Unit>(); }

    /// <summary>
    /// The opacity of the unit's own UI (strength pips, veterancy chevrons, status icons): as the unit, except that the
    /// half-fade a stealthy unit of the viewer's own gets (no enemy sees it) is for its body only: its owner reads its UI in full.
    /// </summary>
    float UIAlpha(float a) {
        return unit != null && FogOfWar.On && unit.owner == FogOfWar.Viewer && a > 0f ? 1f : a;
    }

    /// <summary>Drawn as part of the unit's own UI (under a child Unit.IsOwnUI says is).</summary>
    bool IsUI(Component c) {
        for (Transform t = c.transform; t != null && t != unit.transform; t = t.parent) if (Unit.IsOwnUI(t)) return true;
        return false;
    }

    /// <summary>Where the unit's opacity should be heading.</summary>
    public static float TargetFor(Unit u) {
        if (u == null || !FogOfWar.On) return 1f;
        Player v = FogOfWar.Viewer;
        if (v == null) return 1f;
        // Getting on or off (walking to it, or riding to where it is put down): judged where its sprite is.
        if (u.owner != v && u.GettingOnOrOff) return FogOfWar.ShownToViewer(u) ? 1f : 0f;
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
        // (Or off the board playing its strike, a launch or a fall: shown as the tile it plays on, Unit.ShownAt.)
        if ((unit.Strength > 0 && unit.tile != null) || unit.ShownAt != null) target = TargetFor(unit);
        if (!started) { started = true; alpha = target; }
        float seconds = Mathf.Max(0.01f, FogSettings.Instance.fadeSeconds);
        alpha = Mathf.MoveTowards(alpha, target, Time.deltaTime / seconds);

        if (alpha >= 0.999f && target >= 0.999f) {
            if (hiddenIdle) { Unhide(); hiddenIdle = false; }
            if (!idle) { Apply(1f); Forget(); idle = true; }
            return;
        }
        // Settled out of sight: switch everything off once, then idle until it is seen again
        // (or a new part appears under it, which is switched off too).
        if (alpha <= 0f && target <= 0f) {
            if (!hiddenIdle || transform.hierarchyCount != hierarchySeen) {
                Scan(); Apply(0f); Hide();
                hierarchySeen = transform.hierarchyCount;
                hiddenIdle = true; idle = false;
            }
            return;
        }
        if (hiddenIdle) { Unhide(); hiddenIdle = false; }
        if (idle || Time.time >= rescanAt) {
            Scan();
            // Spread over the half second by the unit, so a whole army spawned together does not rescan in one frame.
            rescanAt = Time.time + 0.5f + (GetInstanceID() & 15) / 32f;
            idle = false;
        }
        Apply(alpha);
    }

    /// <summary>Switch off everything that draws the unit (hiddenIdle), whatever other scripts set its colours or enabled to.</summary>
    void Hide() {
        foreach (SpriteRenderer r in sprites.Keys) Force(r);
        foreach (LineRenderer r in lines.Keys) Force(r);
        foreach (SpriteRenderer r in shadows) Force(r);
        foreach (Renderer r in others) Force(r);
    }

    void Force(Renderer r) {
        if (r == null || r.forceRenderingOff) return;
        r.forceRenderingOff = true;
        forced.Add(r);
    }

    void Unhide() {
        for (int i = 0; i < forced.Count; i++) if (forced[i] != null) forced[i].forceRenderingOff = false;
        forced.Clear();
    }

    void OnDestroy() { Unhide(); }

    /// <summary>Collect what draws the unit (not what draws the units it carries).</summary>
    void Scan() {
        GetComponentsInChildren(true, spriteBuf);
        foreach (SpriteRenderer r in spriteBuf) {
            if (r == null || !Mine(r)) continue;
            if (IsShadow(r)) { if (!shadows.Contains(r)) shadows.Add(r); continue; }
            if (!sprites.ContainsKey(r)) sprites[r] = new Tint { baseColor = r.color, applied = r.color, ui = IsUI(r) };
        }
        GetComponentsInChildren(true, lineBuf);
        foreach (LineRenderer r in lineBuf)
            if (r != null && Mine(r) && !lines.ContainsKey(r)) lines[r] = Keep(r.colorGradient);
        GetComponentsInChildren(true, canvasBuf);
        foreach (Canvas c in canvasBuf) {
            if (c == null || !Mine(c)) continue;
            CanvasGroup g = c.GetComponent<CanvasGroup>();
            if (g == null) { g = c.gameObject.AddComponent<CanvasGroup>(); g.alpha = 1f; g.blocksRaycasts = false; g.interactable = false; }
            if (!groups.ContainsKey(g)) groups[g] = g.alpha;
            if (IsUI(c)) uiGroups.Add(g);
        }
        others.Clear();
        GetComponentsInChildren(true, rendererBuf);
        foreach (Renderer r in rendererBuf)
            if (r != null && Mine(r) && !(r is SpriteRenderer) && !(r is LineRenderer)) others.Add(r);
        spriteBuf.Clear(); lineBuf.Clear(); canvasBuf.Clear(); rendererBuf.Clear();
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
            c.a *= t.ui ? UIAlpha(a) : a;
            r.color = c;
            t.applied = c;
        }
        foreach (KeyValuePair<LineRenderer, Line> kv in lines) {
            if (kv.Key == null || kv.Value == null) continue;
            kv.Key.colorGradient = Faded(kv.Value, a);
        }
        foreach (KeyValuePair<CanvasGroup, float> kv in groups) if (kv.Key != null) kv.Key.alpha = kv.Value * (uiGroups.Contains(kv.Key) ? UIAlpha(a) : a);
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
        uiGroups.Clear();
        for (int i = switchedOff.Count - 1; i >= 0; i--) if (switchedOff[i] != null) switchedOff[i].enabled = true;
        switchedOff.Clear();
        others.Clear();
        shadows.Clear();
    }

    /// <summary>A line's own colours, read once (Gradient's key getters make new arrays each call).</summary>
    static Line Keep(Gradient g) {
        if (g == null) return null;
        GradientAlphaKey[] alphas = g.alphaKeys;
        return new Line { colors = g.colorKeys, alphas = alphas, work = new GradientAlphaKey[alphas.Length], faded = new Gradient() };
    }

    /// <summary>The line's colours at opacity <paramref name="a"/>, in its reused gradient (the renderer copies it when set).</summary>
    static Gradient Faded(Line l, float a) {
        for (int i = 0; i < l.alphas.Length; i++) { l.work[i] = l.alphas[i]; l.work[i].alpha *= a; }
        l.faded.SetKeys(l.colors, l.work);
        return l.faded;
    }
}
