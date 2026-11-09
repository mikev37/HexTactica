// Functional Unity shim for headless execution of HexTactica logic.
// NOT part of the game. Lives outside Assets/ and is never shipped.
//
// Unlike the type-check-only shim, GetComponent / gameObject / AddComponent are
// really implemented, so MonoBehaviours can be constructed and wired up and the
// AI can be run for real.
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine {

    /// <summary>
    /// What Unity allows only on the main thread throws on any other here as it does in Unity
    /// (UnityException "... can only be called from the main thread"): the AI thinking on a worker
    /// thread (AIPlayer.ThinkOnThread) is held to it in the harness. Main is the thread the harness
    /// started on.
    /// </summary>
    public static class MainThreadOnly {
        public static int mainId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        public static int violations;
        public static string first;
        public static void Check(string what) {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == mainId) return;
            System.Threading.Interlocked.Increment(ref violations);
            string msg = what + " can only be called from the main thread.";
            if (first == null) first = msg + "\n" + Environment.StackTrace;
            throw new UnityException(msg);
        }
    }
    public class UnityException : Exception { public UnityException(string m) : base(m) { } }

    public class Object {
        string _name = "";
        public string name { get { MainThreadOnly.Check("get_name"); return _name; } set { MainThreadOnly.Check("set_name"); _name = value; } }
        public int GetInstanceID() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        public static implicit operator bool(Object o) { return !ReferenceEquals(o, null); }
        public static void Destroy(Object o) { MainThreadOnly.Check("Destroy"); }
        public static void Destroy(Object o, float t) { MainThreadOnly.Check("Destroy"); }
        public static void DestroyImmediate(Object o) { MainThreadOnly.Check("DestroyImmediate"); }
        public static void DontDestroyOnLoad(Object o) { }
        public static T Instantiate<T>(T o, Transform p) where T : Object { return GameObject.Instantiate(o, p); }
        public static T Instantiate<T>(T o, Transform p, bool worldPositionStays) where T : Object { return GameObject.Instantiate(o, p); }
        public static T Instantiate<T>(T o) where T : Object { return GameObject.Instantiate(o); }
        public static T Instantiate<T>(T o, Vector3 at, Quaternion q) where T : Object { return GameObject.Instantiate(o, at, q); }
        public static T Instantiate<T>(T o, Vector3 at, Quaternion q, Transform p) where T : Object { return GameObject.Instantiate(o, at, q, p); }
        public static T FindObjectOfType<T>() where T : class { return null; }
        public static T FindAnyObjectByType<T>() where T : class { return null; }
        public static T[] FindObjectsByType<T>(FindObjectsSortMode m) where T : class { return new T[0]; }
        public HideFlags hideFlags;
    }
    public enum FindObjectsSortMode { None, InstanceID }
    [Flags] public enum HideFlags { None = 0, DontSave = 52 }
    public class ParticleSystem : Component { public void Clear() { } public void Play() { } }

    public struct Vector2 {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(0, 0); } }
        public static Vector2 one { get { return new Vector2(1, 1); } }
        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y); } }
        public static float Distance(Vector2 a, Vector2 b) {
            float dx = a.x - b.x, dy = a.y - b.y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
        public static Vector2 MoveTowards(Vector2 a, Vector2 b, float d) { return b; }
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t); }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator *(Vector2 a, float f) { return new Vector2(a.x * f, a.y * f); }
        public static Vector2 operator *(float f, Vector2 a) { return new Vector2(a.x * f, a.y * f); }
        public static implicit operator Vector3(Vector2 v) { return new Vector3(v.x, v.y, 0); }
    }

    public struct Vector3 {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(0, 0, 0); } }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 right { get { return new Vector3(1, 0, 0); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 MoveTowards(Vector3 a, Vector3 b, float d) { return b; }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t); }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public override bool Equals(object o) { return o is Vector3 && (Vector3)o == this; }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a) { return new Vector3(-a.x, -a.y, -a.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float k) { return new Vector3(a.x * k, a.y * k, a.z * k); }
        public static implicit operator Vector2(Vector3 v) { return new Vector2(v.x, v.y); }
    }

    public struct Vector4 { public float x, y, z, w; public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero { get { return new Vector4(); } }
        public static bool operator ==(Vector4 a, Vector4 b) { return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w; }
        public static bool operator !=(Vector4 a, Vector4 b) { return !(a == b); }
        public override bool Equals(object o) { return o is Vector4 && (Vector4)o == this; } public override int GetHashCode() { return 0; } }
    public struct Matrix4x4 { public static Matrix4x4 identity { get { return new Matrix4x4(); } }
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) { return a; }
        public static Matrix4x4 TRS(Vector3 p, Quaternion r, Vector3 s) { return new Matrix4x4(); }
        public Vector3 MultiplyVector(Vector3 v) { return v; } public Vector3 MultiplyPoint3x4(Vector3 v) { return v; } }
    public struct LayerMask { public int value; public static implicit operator int(LayerMask m) { return m.value; } }
    public struct Rect { public float x, y, width, height; public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; } public Vector2 size { get { return new Vector2(width, height); } } public float yMax { get { return y + height; } } public float xMax { get { return x + width; } } public bool Contains(Vector2 p) { return p.x >= x && p.x < x + width && p.y >= y && p.y < y + height; } }
    public class RectOffset { public int left, right, top, bottom; public RectOffset() { } public RectOffset(int l, int r, int t, int b) { left = l; right = r; top = t; bottom = b; } }
    public struct Quaternion {
        public static Quaternion identity { get { return new Quaternion(); } }
        public static Quaternion Euler(float x, float y, float z) { return new Quaternion(); }
    }
    public struct Color {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1f; }
        public static bool operator ==(Color x, Color y) { return x.r == y.r && x.g == y.g && x.b == y.b && x.a == y.a; }
        public static bool operator !=(Color x, Color y) { return !(x == y); }
        public override bool Equals(object o) { return o is Color && (Color)o == this; } public override int GetHashCode() { return 0; }
        public static Color white { get { return new Color(1, 1, 1, 1); } }
        public static Color Lerp(Color x, Color y, float t) { t = t < 0 ? 0 : t > 1 ? 1 : t; return new Color(x.r + (y.r - x.r) * t, x.g + (y.g - x.g) * t, x.b + (y.b - x.b) * t, x.a + (y.a - x.a) * t); }
        public static Color black { get { return new Color(0, 0, 0, 1); } }
        public static Color gray { get { return new Color(.5f, .5f, .5f, 1); } }
        public static Color red { get { return new Color(1, 0, 0, 1); } }
        public static Color clear { get { return new Color(0, 0, 0, 0); } }
    }
    public struct Bounds {
        public Vector3 center, extents;
        public Bounds(Vector3 c, Vector3 size) { center = c; extents = new Vector3(size.x / 2, size.y / 2, size.z / 2); }
        public Vector3 size { get { return new Vector3(extents.x * 2, extents.y * 2, extents.z * 2); } }
        public void Encapsulate(Vector3 p) { }
        public void Encapsulate(Bounds b) { }
        public void Expand(float a) { }
    }
    public class Camera : Component {
        public static Camera main;
        public bool orthographic;
        public float orthographicSize, aspect;
        public CameraClearFlags clearFlags;
        public Color backgroundColor;
        public RenderTexture targetTexture;
        public Vector3 ScreenToWorldPoint(Vector3 p) { return p; }
    }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }
    public enum CameraClearFlags { Skybox = 1, SolidColor = 2, Depth = 3, Nothing = 4 }
    public class RenderTexture : Texture { public RenderTexture(int w, int h, int depth) { } }
    public struct Vector2Int { public int x, y; public Vector2Int(int x, int y) { this.x = x; this.y = y; } }
    public struct Resolution { public int width, height; }
    public class AudioListener : Behaviour { public static float volume = 1f; }
    public class AudioClip : Object { public float length; }
    public enum AudioClipLoadType { DecompressOnLoad, CompressedInMemory, Streaming }
    public enum AudioCompressionFormat { PCM, Vorbis, ADPCM, MP3 }
    public class AudioSource : Behaviour {
        public AudioClip clip; public bool playOnAwake, loop, mute, isPlaying, ignoreListenerPause; public float volume = 1f, pitch = 1f, spatialBlend, time;
        public void Play() { isPlaying = true; } public void Stop() { isPlaying = false; } public void PlayOneShot(AudioClip c) { } public void PlayOneShot(AudioClip c, float v) { }
    }
    public static class QualitySettings {
        public static string[] names { get { return new string[0]; } }
        public static int vSyncCount;
        public static int GetQualityLevel() { return 0; }
        public static void SetQualityLevel(int i, bool expensive) { }
    }
    public static class PlayerPrefs {
        public static bool HasKey(string k) { return false; }
        public static float GetFloat(string k, float d) { return d; }
        public static int GetInt(string k) { return 0; }
        public static int GetInt(string k, int d) { return d; }
        public static void SetFloat(string k, float v) { }
        public static void SetInt(string k, int v) { }
        public static void Save() { }
    }
    public class Motion : Object { }
    public class AnimationClip : Motion { public float length; public bool isLooping; public void SampleAnimation(GameObject g, float t) { } }
    public class RuntimeAnimatorController : Object { public AnimationClip[] animationClips { get { return new AnimationClip[0]; } } }
    public class ParticleSystemRenderer : Renderer { }
    [AttributeUsage(AttributeTargets.Method)] public class RuntimeInitializeOnLoadMethodAttribute : Attribute {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { }
    }

    public static class Mathf { public const float PI = (float)Math.PI;
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public const float Rad2Deg = 57.29578f;
        public static float MoveTowards(float a, float b, float d) { return Math.Abs(b - a) <= d ? b : a + Math.Sign(b - a) * d; }
        public const float Infinity = float.PositiveInfinity;
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static int Min(int a, int b) { return Math.Min(a, b); }
        public static float Floor(float f) { return (float)Math.Floor(f); }
        public static float Ceil(float f) { return (float)Math.Ceiling(f); }
        public static int CeilToInt(float f) { return (int)Math.Ceiling(f); }
        public static int FloorToInt(float f) { return (int)Math.Floor(f); }
        public static int RoundToInt(float f) { return (int)Math.Round(f); }
        public static float Pow(float a, float b) { return (float)Math.Pow(a, b); }
        public static float Sqrt(float f) { return (float)Math.Sqrt(f); }
        public static float Abs(float f) { return Math.Abs(f); }
        public static int Abs(int f) { return Math.Abs(f); }
        public static float Sin(float f) { return (float)Math.Sin(f); }
        public static float Cos(float f) { return (float)Math.Cos(f); }
        public static float Clamp(float v, float a, float b) { return Math.Min(b, Math.Max(a, v)); }
        public static int Clamp(int v, int a, int b) { return Math.Min(b, Math.Max(a, v)); }
        public static float Clamp01(float v) { return Clamp(v, 0f, 1f); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static float PerlinNoise(float a, float b) { return 0.5f; }
    }

    // ---------------------------------------------------------------- objects

    public class Component : Object {
        internal GameObject _go;
        public GameObject gameObject {
            get {
                MainThreadOnly.Check("get_gameObject");
                if (_go == null) { _go = new GameObject(name); _go.Attach(this); }
                return _go;
            }
        }
        public Transform transform { get { return gameObject.transform; } }

        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>(bool includeInactive) where T : class { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>() where T : class { return new T[0]; }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class { return new T[0]; }
        public T[] GetComponents<T>() where T : class { return gameObject.GetComponents<T>(); }
        public T AddComponent<T>() where T : class, new() { return gameObject.AddComponent<T>(); }
        public string tag { get { return gameObject.tag; } set { gameObject.tag = value; } }
    }

    public class Behaviour : Component { public bool enabled = true; }

    public class MonoBehaviour : Behaviour {
        public void StartCoroutine(IEnumerator e) { }
        public void StopAllCoroutines() { }
        public void Invoke(string m, float t) { }
    }

    public class Transform : Component {
        public Vector3 position, localPosition, localScale, up, right;
        public Vector3 lossyScale { get { return localScale; } }
        public Quaternion rotation, localRotation;
        public Transform parent;
        public void SetParent(Transform p, bool worldPositionStays) { parent = p; }
        public Transform Find(string n) { return null; }
        public void SetParent(Transform p) { parent = p; }
        public int childCount { get { return 0; } }
        public Transform GetChild(int i) { return null; }
        public void SetSiblingIndex(int i) { }
        public void SetAsLastSibling() { }
        public int GetSiblingIndex() { return 0; }
        public Matrix4x4 worldToLocalMatrix, localToWorldMatrix;
        public IEnumerator GetEnumerator() { return new List<Transform>().GetEnumerator(); }
    }

    public class GameObject : Object {
        readonly List<Component> components = new List<Component>();
        Transform _transform;

        public GameObject() { }
        public GameObject(string n) { name = n; }
        public GameObject(string n, params Type[] components) { name = n; foreach (Type t in components) AddComponent(t); }
        public Component AddComponent(Type t) { Component c = (Component)Activator.CreateInstance(t); Attach(c); return c; }
        public T GetComponentInChildren<T>(bool includeInactive) where T : class { return GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class { return GetComponents<T>(); }
        public int layer;
        public string tag = "Untagged";
        public UnityEngine.SceneManagement.Scene scene { get { return new UnityEngine.SceneManagement.Scene(); } }

        public Transform transform {
            get {
                MainThreadOnly.Check("get_transform");
                if (_transform == null) { _transform = new Transform(); _transform._go = this; }
                return _transform;
            }
        }

        public bool activeSelf { get { MainThreadOnly.Check("get_activeSelf"); return true; } }
        public bool activeInHierarchy { get { MainThreadOnly.Check("get_activeInHierarchy"); return true; } }
        public void SetActive(bool b) { MainThreadOnly.Check("SetActive"); }

        internal void Attach(Component c) {
            if (!components.Contains(c)) { components.Add(c); c._go = this; }
        }

        public T GetComponent<T>() where T : class {
            MainThreadOnly.Check("GetComponent");
            for (int i = 0; i < components.Count; i++) {
                T t = components[i] as T;
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Every component that is a <typeparamref name="T"/>, as Unity gives them (not only the first).</summary>
        public T[] GetComponents<T>() where T : class {
            MainThreadOnly.Check("GetComponents");
            List<T> all = new List<T>();
            for (int i = 0; i < components.Count; i++) { T t = components[i] as T; if (t != null) all.Add(t); }
            return all.ToArray();
        }

        public T AddComponent<T>() where T : class, new() {
            MainThreadOnly.Check("AddComponent");
            T made = new T();
            Component c = made as Component;
            if (c != null) Attach(c);
            return made;
        }

        /// <summary>When set (a check that runs the game's own code that makes units), Instantiate copies through it; null: the object itself.</summary>
        public static Func<Object, Object> Cloner;
        static T Copy<T>(T o) where T : Object { if (Cloner == null || o == null) return o; T c = Cloner(o) as T; return c ?? o; }
        public static GameObject Instantiate(GameObject g) { return Copy(g); }
        public static T Instantiate<T>(T o) where T : Object { return Copy(o); }
        public static T Instantiate<T>(T o, Transform p) where T : Object { return Copy(o); }
        public static T Instantiate<T>(T o, Vector3 p, Quaternion q) where T : Object { return Copy(o); }
        public static T Instantiate<T>(T o, Vector3 p, Quaternion q, Transform t) where T : Object { return Copy(o); }
        public static void Destroy(Object o) { }
        public static GameObject Find(string n) { return null; }
    }

    public class ScriptableObject : Object {
        public static T CreateInstance<T>() where T : ScriptableObject, new() { return new T(); }
    }

    public class Sprite : Object { public Bounds bounds; public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu) { return new Sprite(); } }
    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Repeat, Clamp }
    public class Shader : Object { public static Shader Find(string n) { return null; } }
    public class Material : Object {
        public Material() { }
        public Material(Shader s) { }
        public Color color;
        public void SetFloat(string n, float v) { }
        public void SetVector(string n, Vector4 v) { }
        public void SetTexture(string n, Texture t) { }
        public float GetFloat(string n) { return 0; }
    }
    public class Texture : Object { }
    public class Texture2D : Texture {
        public Texture2D() { }
        public Texture2D(int w, int h, TextureFormat f, bool mips) { }
        public static Texture2D whiteTexture { get { return new Texture2D(); } }
        public TextureWrapMode wrapMode;
        public void SetPixels(Color[] c) { }
        public void Apply() { }
    }
    public class Renderer : Component { public Bounds bounds; public bool enabled = true; public int sortingLayerID; public string sortingLayerName; public int sortingOrder; }
    public static class SortingLayer { public static int NameToID(string n) { return 0; } public static bool IsValid(int id) { return false; } public static int GetLayerValueFromID(int id) { return 0; } public static string IDToName(int id) { return "Default"; } }
    public class SpriteRenderer : Renderer {
        public Sprite sprite; public Color color;
        public int rendererPriority;
        public bool flipX, flipY;
    }
    public class MeshRenderer : Renderer { public Material material; public Material sharedMaterial; }
    public class MeshFilter : Component { }
    public struct GradientColorKey { public Color color; public float time; public GradientColorKey(Color c, float t) { color = c; time = t; } }
    public struct GradientAlphaKey { public float alpha; public float time; public GradientAlphaKey(float a, float t) { alpha = a; time = t; } }
    public class Gradient {
        GradientColorKey[] _c = new GradientColorKey[0];
        GradientAlphaKey[] _a = new GradientAlphaKey[0];
        // As in Unity: the keys come back as copies.
        public GradientColorKey[] colorKeys { get { return (GradientColorKey[])_c.Clone(); } set { _c = (GradientColorKey[])value.Clone(); } }
        public GradientAlphaKey[] alphaKeys { get { return (GradientAlphaKey[])_a.Clone(); } set { _a = (GradientAlphaKey[])value.Clone(); } }
        public void SetKeys(GradientColorKey[] c, GradientAlphaKey[] a) { colorKeys = c; alphaKeys = a; }
    }
    public static class Resources {
        public static T Load<T>(string path) where T : class { return null; }
        public static T GetBuiltinResource<T>(string path) where T : class { return null; }
        public static T[] FindObjectsOfTypeAll<T>() where T : class { return new T[0]; }
        /// <summary>The harness's Resources folder: what it finds under that path, by type (PrefabLoader; null: nothing).</summary>
        public static System.Func<System.Type, string, object[]> Provider;
        public static Object[] LoadAll(string path) { return LoadAll<Object>(path); }
        public static T[] LoadAll<T>(string path) where T : class {
            object[] found = Provider != null ? Provider(typeof(T), path) : null;
            if (found == null) return new T[0];
            System.Collections.Generic.List<T> r = new System.Collections.Generic.List<T>();
            foreach (object o in found) { T t = o as T; if (t != null) r.Add(t); }
            return r.ToArray();
        }
    }
    public class LineRenderer : Renderer {
        public Gradient colorGradient;
        public int positionCount;
        public Color startColor, endColor;
        public Material material;
        public float widthMultiplier;
        public int numCapVertices, numCornerVertices;
        public bool useWorldSpace;
        public void SetPosition(int i, Vector3 v) { }
    }
    public class Canvas : Behaviour { public RenderMode renderMode; public int sortingOrder; public static void ForceUpdateCanvases() { } }
    public class CanvasGroup : Behaviour { public float alpha = 1f; public bool blocksRaycasts = true, interactable = true; }
    public class Animator : Behaviour {
        public RuntimeAnimatorController runtimeAnimatorController;
        public void SetBool(string s, bool b) { }
        public void SetTrigger(string s) { }
        public AnimatorControllerParameter[] parameters { get { return new AnimatorControllerParameter[0]; } }
        public void ResetTrigger(string s) { }
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) { return new AnimatorStateInfo(); }
        public void Rebind() { }
        public void Update(float dt) { }
    }
    public class AnimatorControllerParameter { public string name; }
    public struct AnimatorStateInfo { public float normalizedTime { get { return 0; } } }
    public class StateMachineBehaviour : ScriptableObject {
        public virtual void OnStateEnter(Animator a, AnimatorStateInfo s, int l) { }
        public virtual void OnStateExit(Animator a, AnimatorStateInfo s, int l) { }
        public virtual void OnStateUpdate(Animator a, AnimatorStateInfo s, int l) { }
    }

    public static class Debug {
        public static void LogException(System.Exception e) { System.Console.WriteLine("EXCEPTION " + e); }
        public static bool verbose = false;
        public static void Log(object o) { if (verbose) Console.WriteLine("[log] " + o); }
        public static void LogWarning(object o) { Console.WriteLine("[warn] " + o); }
        public static int errors;
        public static void LogError(object o) { System.Threading.Interlocked.Increment(ref errors); Console.WriteLine("[error] " + o); }
    }
    public static class Screen {
        public static int width { get { return 1920; } } public static int height { get { return 1080; } }
        public static bool fullScreen;
        public static Resolution[] resolutions { get { return new Resolution[0]; } }
        public static void SetResolution(int w, int h, bool full) { }
    }
    public static class GUI {
        public static bool enabled = true;
        public static Color color = Color.white;
        public static GUISkin skin = new GUISkin();
        public static void Box(Rect r, string s) { }
        public static void Label(Rect r, string s) { }
        public static void Label(Rect r, string s, GUIStyle st) { }
        public static void DrawTexture(Rect r, Texture t) { }
        public static bool Button(Rect r, string s) { return false; }
    }
    public static class GUILayout {
        public static void Label(string s, GUIStyle st) { }
        public static void Label(string s) { }
        public static bool Button(string s) { return false; }
        public static void BeginHorizontal() { }
        public static void EndHorizontal() { }
        public static void BeginArea(Rect r) { }
        public static void EndArea() { }
        public static Vector2 BeginScrollView(Vector2 v) { return v; }
        public static void EndScrollView() { }
    }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public class GUIStyleState { public Color textColor; }
    public class GUIStyle {
        public GUIStyle() { } public GUIStyle(GUIStyle o) { }
        public int fontSize; public FontStyle fontStyle; public TextAnchor alignment; public bool wordWrap;
        public GUIStyleState normal = new GUIStyleState();
        public Vector2 CalcSize(GUIContent c) { return new Vector2(100, 20); }
    }
    public class GUIContent { public string text = ""; public GUIContent(string s) { text = s; } public GUIContent(string s, string tip) { text = s; } }
    public class GUISkin { public GUIStyle label = new GUIStyle(), box = new GUIStyle(); }

    public static class Time {
        public static float deltaTime { get { MainThreadOnly.Check("get_deltaTime"); return 0.016f; } }
        public static float fixedDeltaTime { get { MainThreadOnly.Check("get_fixedDeltaTime"); return 0.02f; } }
        public static float time { get { MainThreadOnly.Check("get_time"); return 0; } }
        public static float unscaledTime { get { MainThreadOnly.Check("get_unscaledTime"); return 0; } }
        public static float timeSinceLevelLoad { get { MainThreadOnly.Check("get_timeSinceLevelLoad"); return 0; } }
        public static int frameCount { get { MainThreadOnly.Check("get_frameCount"); return 0; } }
        public static float unscaledDeltaTime { get { MainThreadOnly.Check("get_unscaledDeltaTime"); return 0.016f; } }
    }
    public static class Input {
        public static bool GetKeyDown(string s) { return false; }
        public static bool GetKeyDown(KeyCode k) { return false; }
        public static bool GetMouseButtonDown(int i) { return false; }
        public static bool GetKey(KeyCode k) { return false; }
        public static bool GetMouseButton(int i) { return false; }
        public static Vector3 mousePosition { get { return new Vector3(); } }
        public static Vector2 mouseScrollDelta { get { return new Vector2(); } }
    }
    public enum KeyCode { None, Space, Escape, Return, KeypadEnter, Tab, Home, End, LeftArrow, RightArrow, UpArrow, DownArrow, Equals, Minus, KeypadPlus, KeypadMinus, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z }
    public static class Application {
        public static string persistentDataPath { get { return "/tmp/hextactica"; } }
        public static string dataPath { get { return "/tmp/hextactica"; } }
        public static bool isPlaying { get { return true; } }
        public static bool isFocused { get { return true; } }
        public static bool isEditor { get { return false; } }
        public static string streamingAssetsPath { get { return "/tmp/hextactica/StreamingAssets"; } }
        public static void Quit() { }
    }
    public static class JsonUtility {
        public static string ToJson(object o) { return "{}"; }
        /// <summary>Unity's: the public fields of <typeparamref name="T"/> (numbers, bools, strings, enums) from a JSON object; others left at their defaults.</summary>
        public static T FromJson<T>(string s) {
            object o = Activator.CreateInstance(typeof(T));
            Defective.JSON.JSONObject j = new Defective.JSON.JSONObject(s);
            foreach (System.Reflection.FieldInfo f in typeof(T).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
                Defective.JSON.JSONObject v = j.GetField(f.Name);
                if (v == null) continue;
                Type t = f.FieldType;
                if (t == typeof(string)) f.SetValue(o, v.stringValue);
                else if (t == typeof(int)) f.SetValue(o, (int)v.longValue);
                else if (t == typeof(long)) f.SetValue(o, v.longValue);
                else if (t == typeof(float)) f.SetValue(o, v.floatValue);
                else if (t == typeof(double)) f.SetValue(o, (double)v.floatValue);
                else if (t == typeof(bool)) f.SetValue(o, v.boolValue);
                else if (t.IsEnum) f.SetValue(o, Enum.ToObject(t, (int)v.longValue));
            }
            return (T)o;
        }
    }
    public static class Random {
        public static float Range(float a, float b) { return a; }
        public static int Range(int a, int b) { return a; }
        public static float value { get { return 0.5f; } }
        public static void InitState(int seed) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)] public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class TextAreaAttribute : Attribute { public TextAreaAttribute(int a, int b) { } }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class CreateAssetMenuAttribute : Attribute { public string menuName; public string fileName; }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class HideInInspectorAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class SpaceAttribute : Attribute { public SpaceAttribute() { } public SpaceAttribute(float f) { } }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class ContextMenuAttribute : Attribute { public ContextMenuAttribute(string s) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public abstract class PropertyAttribute : Attribute { }
}

namespace UnityEngine {
    public class RectTransform : Transform {
        public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta, offsetMin, offsetMax;
        public Rect rect { get { return new Rect(0, 0, sizeDelta.x, sizeDelta.y); } }
    }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public class Font : Object { }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
}
namespace UnityEngine.Events {
    public delegate void UnityAction();
    public delegate void UnityAction<T0>(T0 a);
    public delegate void UnityAction<T0, T1>(T0 a, T1 b);
    public abstract class UnityEventBase { public int GetPersistentEventCount() { return 0; } }
    public class UnityEvent : UnityEventBase {
        UnityAction calls;
        public void AddListener(UnityAction a) { calls += a; }
        public void RemoveListener(UnityAction a) { calls -= a; }
        public void RemoveAllListeners() { calls = null; }
        public void Invoke() { if (calls != null) calls(); }
    }
    public class UnityEvent<T0> : UnityEventBase {
        UnityAction<T0> calls;
        public void AddListener(UnityAction<T0> a) { calls += a; }
        public void RemoveAllListeners() { calls = null; }
        public void Invoke(T0 v) { if (calls != null) calls(v); }
    }
}
namespace UnityEngine.SceneManagement {
    public enum LoadSceneMode { Single, Additive }
    public struct Scene {
        public string name { get { return ""; } }
        public string path { get { return ""; } }
        public bool isLoaded { get { return false; } }
        public bool IsValid() { return false; }
        public UnityEngine.GameObject[] GetRootGameObjects() { return new UnityEngine.GameObject[0]; }
    }
    public static class SceneManager {
        public static event UnityEngine.Events.UnityAction<Scene, LoadSceneMode> sceneLoaded;
        public static int sceneCount { get { return 0; } }
        public static Scene GetSceneAt(int i) { return new Scene(); }
        public static Scene GetActiveScene() { return new Scene(); }
        public static bool SetActiveScene(Scene s) { return true; }
        public static Scene GetSceneByName(string n) { return new Scene(); }
        public static Scene GetSceneByPath(string p) { return new Scene(); }
        public static void LoadScene(string n, LoadSceneMode m) { if (sceneLoaded != null) sceneLoaded(new Scene(), m); }
        public static UnityEngine.AsyncOperation UnloadSceneAsync(Scene s) { return null; }
    }
}
namespace UnityEngine { public class AsyncOperation { } }
namespace UnityEngine.UI {
    public class ButtonClickedEvent : UnityEngine.Events.UnityEvent { }
    public struct ColorBlock { public UnityEngine.Color normalColor, highlightedColor, pressedColor, selectedColor, disabledColor; }
    public class Selectable : UnityEngine.Behaviour { public Graphic targetGraphic; public bool interactable = true; public ColorBlock colors; }
    public class Graphic : UnityEngine.Behaviour { public UnityEngine.Color color; public bool raycastTarget; }
    public class Text : Graphic { string _text = ""; public string text { get { UnityEngine.MainThreadOnly.Check("Text.get_text"); return _text; } set { UnityEngine.MainThreadOnly.Check("Text.set_text"); _text = value; } } public int fontSize; public UnityEngine.Font font; public UnityEngine.TextAnchor alignment; }
    public class Image : Graphic { public enum Type { Simple, Sliced, Tiled, Filled } public Type type; public UnityEngine.Sprite sprite; public bool preserveAspect; public UnityEngine.RectTransform rectTransform; }
    public class Button : Selectable { public ButtonClickedEvent onClick = new ButtonClickedEvent(); }
}

namespace UnityEditor {
    public class ScriptableSingleton<T> : UnityEngine.ScriptableObject where T : UnityEngine.ScriptableObject {
        static T inst;
        public static T instance { get { if (inst == null) inst = System.Activator.CreateInstance<T>(); return inst; } }
    }
}

namespace UnityEngine.Rendering {
    public class SortingGroup : Behaviour { public int sortingLayerID; public string sortingLayerName; public int sortingOrder; }
}

namespace UnityEngine.Serialization { public class FormerlySerializedAsAttribute : System.Attribute { public readonly string oldName; public FormerlySerializedAsAttribute(string old) { oldName = old; } } }
