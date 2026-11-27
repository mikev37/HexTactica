using System;
using System.Reflection;
using UnityEngine;

// Drives FogFader.LateUpdate frame by frame on a bare unit (off the board, so its target opacity is set here
// directly) and checks the hidden-idle path: switched off once, untouched while hidden, back on when seen.
static class FogFaderCheck {
    static int failed;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what); if (!ok) failed++; }

    static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static void Set(object o, string f, object v) { o.GetType().GetField(f, Any).SetValue(o, v); }
    static object Get(object o, string f) { return o.GetType().GetField(f, Any).GetValue(o); }
    static void Call(object o, string m) { o.GetType().GetMethod(m, Any).Invoke(o, null); }

    static int Main() {
        GameObject g = new GameObject("Enemy");
        Unit u = g.AddComponent<Unit>();
        SpriteRenderer body = g.AddComponent<SpriteRenderer>();
        body.color = new Color(1f, 0f, 0f, 1f);
        LineRenderer tread = g.AddComponent<LineRenderer>();
        Gradient grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        tread.colorGradient = grad;
        FogFader f = g.AddComponent<FogFader>();
        Call(f, "Awake");
        Check(u.tile == null && u.ShownAt == null, "setup: the unit is off the board, so LateUpdate keeps the target set here");

        // Out of sight from the start.
        Set(f, "target", 0f);
        Call(f, "LateUpdate");
        Check(f.Alpha == 0f && (bool)Get(f, "hiddenIdle"), "hidden from the start: settles at 0 and goes idle");
        Check(body.forceRenderingOff && tread.forceRenderingOff, "every renderer is forced off");

        // Another script (ColorObjectUnit, UnitAlarmIcon) sets colour and enabled while it is hidden.
        body.color = new Color(0f, 0f, 1f, 1f); body.enabled = true;
        for (int i = 0; i < 20; i++) Call(f, "LateUpdate");
        Check(body.color.a == 1f, "idle: the fader no longer rewrites colours each frame (another script's colour stands)");
        Check(body.forceRenderingOff, "...but the sprite stays off however that script sets it");

        // A new part appears under it while hidden (smoke, a chute).
        MeshRenderer smoke = g.AddComponent<MeshRenderer>();
        Call(f, "LateUpdate");
        Check(smoke.forceRenderingOff, "a part added while hidden is switched off too");

        // Seen again: fades back in.
        Set(f, "target", 1f);
        Call(f, "LateUpdate");
        Check(!(bool)Get(f, "hiddenIdle") && !body.forceRenderingOff && !tread.forceRenderingOff && !smoke.forceRenderingOff, "seen: everything it forced off is drawn again");
        Check(f.Alpha > 0f && f.Alpha < 1f && Math.Abs(body.color.a - f.Alpha) < 1e-4f && body.color.b == 1f, "fading in from the colour the other script set (" + body.color + ")");
        GradientAlphaKey[] k = tread.colorGradient.alphaKeys;
        Check(Math.Abs(k[0].alpha - f.Alpha) < 1e-4f && Math.Abs(k[1].alpha - f.Alpha) < 1e-4f, "the tread's gradient is faded to the same opacity");
        Gradient first = (Gradient)Get(((System.Collections.IDictionary)Get(f, "lines"))[tread], "faded");
        Call(f, "LateUpdate");
        Gradient second = (Gradient)Get(((System.Collections.IDictionary)Get(f, "lines"))[tread], "faded");
        Check(ReferenceEquals(first, second), "the faded gradient is reused frame to frame (no new Gradient each frame)");
        for (int i = 0; i < 500 && f.Alpha < 1f; i++) Call(f, "LateUpdate");
        Call(f, "LateUpdate");
        Check(f.Alpha == 1f && body.color.a == 1f && tread.colorGradient.alphaKeys[0].alpha == 1f, "fully shown again: opacity 1 everywhere");
        Check(((System.Collections.ICollection)Get(f, "sprites")).Count == 0, "and idle (shown) again: nothing held");

        // Half-faded (one's own stealthy unit) is not the hidden idle: it keeps fading the colours other scripts set.
        Set(f, "target", 0.5f);
        for (int i = 0; i < 500; i++) Call(f, "LateUpdate");
        Check(f.Alpha == 0.5f && !(bool)Get(f, "hiddenIdle") && !body.forceRenderingOff && Math.Abs(body.color.a - 0.5f) < 1e-4f, "half-faded: drawn at half opacity, not forced off");

        // Fade out from sight: forced off only once it reaches 0.
        Set(f, "target", 0f);
        Call(f, "LateUpdate");
        Check(!body.forceRenderingOff && f.Alpha > 0f, "fading out: still drawn until opacity reaches 0");
        for (int i = 0; i < 500 && f.Alpha > 0f; i++) Call(f, "LateUpdate");
        Call(f, "LateUpdate");
        Check((bool)Get(f, "hiddenIdle") && body.forceRenderingOff && smoke.forceRenderingOff, "faded out: hidden and idle");

        Console.WriteLine(failed == 0 ? "fog fader checks: all passed" : "fog fader checks: " + failed + " FAILED");
        return failed == 0 ? 0 : 1;
    }
}
