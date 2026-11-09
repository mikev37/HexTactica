using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// A unit shown large and animated, away from the menu: a copy of its prefab stood on the stage, filmed by the
/// preview camera into a render texture the menu shows (a RawImage). Only its looks come along: the unit's game
/// scripts are taken off before it wakes (it is not in a game), except those that only animate it
/// (<see cref="keepBehaviours"/>). Its animator's clips are played one at a time, looping; Step goes to the next.
/// </summary>
public class UnitPreview : MonoBehaviour {
    [Tooltip("Where the unit stands (in front of the preview camera, far from the menu).")]
    public Transform stage;
    [Tooltip("Films the stage into the render texture the menu shows.")]
    public Camera previewCamera;
    [Tooltip("How much room to leave around the unit: the camera frames its sprites times this.")]
    public float margin = 1.25f;
    [Tooltip("The smallest view, in world units (a small unit is not blown up past this).")]
    public float minSize = 0.6f;
    [Tooltip("Scripts kept on the copy, by class name: those that only animate the unit's sprites. Every other script on it is game logic and is removed.")]
    public List<string> keepBehaviours = new List<string> { "SpriteAnimator", "BobUpDown", "ConstantRotate", "counterRotate", "AnimatedOffestDashedLine" };
    [Tooltip("The animations shown, in this order, by clip name (others the unit's animator has are not shown).")]
    public List<string> animations = new List<string> { "Wait", "Move", "Fire", "Die" };
    [Tooltip("Seconds of stillness between loops of an animation.")]
    public float loopPause = 0.4f;

    Unit prefab;
    GameObject shown;
    Animator animator;
    AnimationClip[] clips = new AnimationClip[0];
    int index;
    float time;

    public int AnimationCount { get { return clips.Length; } }
    public int AnimationIndex { get { return index; } }
    public string AnimationName { get { return clips.Length > 0 ? clips[index].name : ""; } }

    /// <summary>Show this unit, its first animation playing.</summary>
    public void Show(Unit u) {
        prefab = u;
        clips = new AnimationClip[0];
        index = 0;
        Rebuild();
        if (animator != null && animator.runtimeAnimatorController != null)
            clips = animator.runtimeAnimatorController.animationClips.Where(c => c != null && Listed(c) >= 0).Distinct()
                .OrderBy(Listed).ToArray();
    }

    int Listed(AnimationClip c) { return animations.FindIndex(n => string.Equals(n, c.name, System.StringComparison.OrdinalIgnoreCase)); }

    /// <summary>The next (+1) or previous (-1) animation. The unit is put up afresh, so nothing of the last clip stays on it.</summary>
    public void Step(int by) {
        if (clips.Length == 0) return;
        index = (index + by + clips.Length) % clips.Length;
        Rebuild();
    }

    public void Clear() {
        if (shown != null) Destroy(shown);
        shown = null;
        animator = null;
    }

    void Rebuild() {
        Clear();
        time = 0f;
        if (prefab == null || stage == null) return;
        // Made under a sleeping holder, so none of its scripts wakes before the game ones are taken off.
        GameObject holder = new GameObject(prefab.name + " (preview)");
        holder.SetActive(false);
        holder.transform.SetParent(stage, false);
        GameObject copy = Instantiate(prefab.gameObject, holder.transform, false);
        copy.transform.localPosition = Vector3.zero;
        StripGameScripts(copy);
        Unit unitOnCopy = copy.GetComponent<Unit>();
        Animator a = copy.GetComponentInChildren<Animator>(true);
        animator = a;
        if (animator != null) animator.enabled = false;   // the clips are played by hand (Update)
        if (unitOnCopy != null) DestroyImmediate(unitOnCopy);
        holder.SetActive(true);
        shown = holder;
        Frame();
    }

    void StripGameScripts(GameObject copy) {
        // In passes: a script another needs (RequireComponent) goes once that one is gone.
        for (int pass = 0; pass < 4; pass++) {
            bool any = false;
            foreach (MonoBehaviour m in copy.GetComponentsInChildren<MonoBehaviour>(true)) {
                if (m == null || m is Unit || keepBehaviours.Contains(m.GetType().Name)) continue;
                DestroyImmediate(m);
                any = true;
            }
            if (!any) break;
        }
    }

    /// <summary>The camera on the unit's sprites.</summary>
    void Frame() {
        if (previewCamera == null || shown == null) return;
        Renderer[] rs = shown.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        Vector3 c = b.center;
        previewCamera.transform.position = new Vector3(c.x, c.y, previewCamera.transform.position.z);
        float aspect = previewCamera.aspect > 0f ? previewCamera.aspect : 1f;
        previewCamera.orthographicSize = Mathf.Max(minSize, Mathf.Max(b.extents.y, b.extents.x / aspect) * margin);
    }

    void Update() {
        if (shown == null || animator == null || clips.Length == 0) return;
        AnimationClip clip = clips[index];
        time += Time.deltaTime;
        float length = Mathf.Max(0.01f, clip.length);
        float t = clip.isLooping ? time % length : Mathf.Min(time % (length + loopPause), length);
        clip.SampleAnimation(animator.gameObject, t);
    }
}
