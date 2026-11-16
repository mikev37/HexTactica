using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Makes the Missile's own animator the first time the project opens without it, as a first version for artists:
/// <list type="bullet">
/// <item>Assets/Animations/Missile/Missile.controller: a copy of the units' shared controller (so idle, fire and death
///   play as for any unit: a missile destroyed in its silo still dies), plus three triggers and their states:
///   <b>Launch</b> (it climbs off the screen; StrikeStage Launched), <b>Fall</b> (a copy of it falls onto the target;
///   Landed) and <b>Impact</b> (after the explosion; Impact Done), each followed by an empty <b>Gone</b> state.</item>
/// <item>Missile Launch.anim, Missile Fall.anim, Missile Impact.anim beside it: placeholder motion of the prefab's art
///   children (Building, Building (1)); its label, health and veterancy are hidden while it flies.</item>
/// <item>The Missile prefab's Animator is given the new controller, if it still has the shared one.</item>
/// </list>
/// The animations decide how long each stage takes: the game waits for each state to end (StrikeStage). After this
/// runs, everything is yours to edit in Unity: nothing here touches an asset that exists, except the menu item, which
/// asks first. Nothing is built or moved by code at run time.
/// </summary>
[InitializeOnLoad]
static class MissileAnimator {
    const string Folder = "Assets/Animations/Missile";
    const string ControllerPath = Folder + "/Missile.controller";
    const string SharedPath = "Assets/Animations/UnitController.controller";
    const string PrefabPath = "Assets/Resources/Units/Missile.prefab";

    // The prefab's children the clips move or hide (paths from its root, where the Animator is).
    static readonly string[] Art = { "Building", "Building (1)" };
    static readonly string[] Labels = { "Canvas", "HealthCircles", "Veterancy" };
    const float ArtY = -1.15f;      // where the art stands in the prefab
    const float Height = 40f;       // how far above it climbs, and falls from (well off the top of the screen)

    static MissileAnimator() { EditorApplication.delayCall += () => Create(false); }

    [MenuItem("HexTactica/Units/Recreate Missile Animator")]
    static void Recreate() {
        if (File.Exists(ControllerPath) && !EditorUtility.DisplayDialog("Recreate Missile Animator",
                "Replace " + ControllerPath + " and its three clips with the defaults? Your edits to them will be lost.", "Replace", "Cancel")) return;
        Create(true);
    }

    static void Create(bool replace) {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!replace && File.Exists(ControllerPath)) return;
        if (!File.Exists(SharedPath)) { Debug.LogWarning("Missile animator: " + SharedPath + " not found; nothing made."); return; }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Animations", "Missile");
        if (replace) AssetDatabase.DeleteAsset(ControllerPath);
        if (!AssetDatabase.CopyAsset(SharedPath, ControllerPath)) { Debug.LogWarning("Missile animator: could not copy " + SharedPath); return; }
        AnimatorController c = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (c == null) return;

        AnimationClip launch = Clip("Missile Launch", replace, LaunchClip);
        AnimationClip fall = Clip("Missile Fall", replace, FallClip);
        AnimationClip impact = Clip("Missile Impact", replace, ImpactClip);

        c.AddParameter("Launch", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Fall", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Impact", AnimatorControllerParameterType.Trigger);
        AnimatorStateMachine sm = c.layers[0].stateMachine;
        AnimatorState gone = sm.AddState("Gone", new Vector3(700f, 380f, 0f));
        AnimatorState sLaunch = Stage(sm, "Launch", launch, StrikeStage.Stage.Launched, new Vector3(450f, 300f, 0f));
        AnimatorState sFall = Stage(sm, "Fall", fall, StrikeStage.Stage.Landed, new Vector3(450f, 380f, 0f));
        AnimatorState sImpact = Stage(sm, "Impact", impact, StrikeStage.Stage.ImpactDone, new Vector3(450f, 460f, 0f));
        From(sm.AddAnyStateTransition(sLaunch), "Launch");
        From(sm.AddAnyStateTransition(sFall), "Fall");
        From(sm.AddAnyStateTransition(sImpact), "Impact");
        After(sLaunch.AddTransition(gone));
        After(sImpact.AddTransition(gone));
        // (A Fall that ends with no Impact set goes on to Gone: the copy is removed when Impact Done, so it waits there.)
        After(sFall.AddTransition(gone));
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssets();
        Assign(c);
        Debug.Log("Missile animator made: " + ControllerPath + " (Launch, Fall, Impact) and its clips in " + Folder + ".");
    }

    static AnimatorState Stage(AnimatorStateMachine sm, string name, AnimationClip clip, StrikeStage.Stage stage, Vector3 at) {
        AnimatorState s = sm.AddState(name, at);
        s.motion = clip;
        s.AddStateMachineBehaviour<StrikeStage>().stage = stage;
        return s;
    }

    static void From(AnimatorStateTransition t, string trigger) {
        t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        t.duration = 0f;
        t.hasExitTime = false;
        t.canTransitionToSelf = false;
    }

    static void After(AnimatorStateTransition t) {
        t.hasExitTime = true;
        t.exitTime = 1f;
        t.duration = 0f;
    }

    /// <summary>The prefab's Animator gets the controller, only if it still has the shared one (assigned assets win).</summary>
    static void Assign(AnimatorController c) {
        if (!File.Exists(PrefabPath)) return;
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try {
            Animator a = root.GetComponent<Animator>();
            if (a == null || a.runtimeAnimatorController == null || AssetDatabase.GetAssetPath(a.runtimeAnimatorController) != SharedPath) return;
            a.runtimeAnimatorController = c;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        } finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ------------------------------------------------------------------ clips

    static AnimationClip Clip(string name, bool replace, System.Action<AnimationClip> fill) {
        string path = Folder + "/" + name + ".anim";
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null && !replace) return existing;
        if (existing != null) AssetDatabase.DeleteAsset(path);
        AnimationClip clip = new AnimationClip { name = name };
        fill(clip);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    /// <summary>A shake where it stands (ignition), then a climb that speeds up, off the top of the screen; the art is hidden at the end.</summary>
    static void LaunchClip(AnimationClip clip) {
        const float ignition = 0.3f, end = 1.6f;
        foreach (string p in Art) {
            AnimationCurve x = new AnimationCurve();
            for (int i = 0; i <= 6; i++) x.AddKey(new Keyframe(ignition * i / 6f, i % 2 == 0 ? 0f : (i % 4 == 1 ? 0.06f : -0.06f)));
            x.AddKey(new Keyframe(end, 0f));
            Set(clip, p, typeof(Transform), "m_LocalPosition.x", x);
            Set(clip, p, typeof(Transform), "m_LocalPosition.y", new AnimationCurve(
                new Keyframe(0f, ArtY), new Keyframe(ignition, ArtY, 0f, 0f), new Keyframe(end, ArtY + Height, Height * 2.5f / (end - ignition), 0f)));
            Set(clip, p, typeof(GameObject), "m_IsActive", Step(1f, end, 0f));
        }
        foreach (string p in Labels) Set(clip, p, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, end, 0f));
    }

    /// <summary>Nose down, from high above the target, falling faster and faster onto it.</summary>
    static void FallClip(AnimationClip clip) {
        const float end = 0.9f;
        foreach (string p in Art) {
            Set(clip, p, typeof(Transform), "m_LocalPosition.y", new AnimationCurve(
                new Keyframe(0f, ArtY + Height, 0f, 0f), new Keyframe(end, ArtY, -Height * 2.5f / end, 0f)));
            Set(clip, p, typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Constant(0f, end, 180f));
            Set(clip, p, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, end, 1f));
        }
        foreach (string p in Labels) Set(clip, p, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, end, 0f));
    }

    /// <summary>The missile is gone (the explosion is the unit's Strike Explosion); a short hold for art to fill.</summary>
    static void ImpactClip(AnimationClip clip) {
        const float end = 0.5f;
        foreach (string p in Art) Set(clip, p, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, end, 0f));
        foreach (string p in Labels) Set(clip, p, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, end, 0f));
    }

    static AnimationCurve Step(float before, float at, float after) {
        AnimationCurve c = new AnimationCurve(new Keyframe(0f, before), new Keyframe(Mathf.Max(0f, at - 0.001f), before), new Keyframe(at, after));
        for (int i = 0; i < c.length; i++) {
            AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Constant);
        }
        return c;
    }

    static void Set(AnimationClip clip, string path, System.Type type, string property, AnimationCurve curve) {
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
    }
}
