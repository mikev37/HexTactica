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

    public class Object {
        public string name = "";
        public static implicit operator bool(Object o) { return !ReferenceEquals(o, null); }
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
        public static T FindObjectOfType<T>() where T : class { return null; }
    }

    public struct Vector2 {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(0, 0); } }
        public static Vector2 one { get { return new Vector2(1, 1); } }
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
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float k) { return new Vector3(a.x * k, a.y * k, a.z * k); }
        public static implicit operator Vector2(Vector3 v) { return new Vector2(v.x, v.y); }
    }

    public struct Vector4 { public float x, y, z, w; public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; } }
    public struct Matrix4x4 { public static Matrix4x4 identity { get { return new Matrix4x4(); } } }
    public struct LayerMask { public int value; public static implicit operator int(LayerMask m) { return m.value; } }
    public struct Rect { public float x, y, width, height; public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; } }
    public class RectOffset { public int left, right, top, bottom; }
    public struct Quaternion {
        public static Quaternion identity { get { return new Quaternion(); } }
        public static Quaternion Euler(float x, float y, float z) { return new Quaternion(); }
    }
    public struct Color {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1, 1, 1, 1); } }
        public static Color black { get { return new Color(0, 0, 0, 1); } }
        public static Color gray { get { return new Color(.5f, .5f, .5f, 1); } }
        public static Color clear { get { return new Color(0, 0, 0, 0); } }
    }
    public struct Bounds {
        public Vector3 center, extents;
        public Bounds(Vector3 c, Vector3 size) { center = c; extents = new Vector3(size.x / 2, size.y / 2, size.z / 2); }
        public void Encapsulate(Vector3 p) { }
        public void Expand(float a) { }
    }
    public class Camera : Component {
        public static Camera main;
        public bool orthographic;
        public float orthographicSize, aspect;
        public Vector3 ScreenToWorldPoint(Vector3 p) { return p; }
    }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }
    [AttributeUsage(AttributeTargets.Method)] public class RuntimeInitializeOnLoadMethodAttribute : Attribute {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { }
    }

    public static class Mathf {
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
                if (_go == null) { _go = new GameObject(name); _go.Attach(this); }
                return _go;
            }
        }
        public Transform transform { get { return gameObject.transform; } }

        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>() where T : class { return new T[0]; }
        public T AddComponent<T>() where T : class, new() { return gameObject.AddComponent<T>(); }
    }

    public class Behaviour : Component { public bool enabled = true; }

    public class MonoBehaviour : Behaviour {
        public void StartCoroutine(IEnumerator e) { }
        public void StopAllCoroutines() { }
        public void Invoke(string m, float t) { }
    }

    public class Transform : Component {
        public Vector3 position, localPosition, localScale, up, right;
        public Quaternion rotation, localRotation;
        public Transform parent;
        public int childCount { get { return 0; } }
        public IEnumerator GetEnumerator() { return new List<Transform>().GetEnumerator(); }
    }

    public class GameObject : Object {
        readonly List<Component> components = new List<Component>();
        Transform _transform;

        public GameObject() { }
        public GameObject(string n) { name = n; }

        public Transform transform {
            get {
                if (_transform == null) { _transform = new Transform(); _transform._go = this; }
                return _transform;
            }
        }

        public bool activeSelf { get { return true; } }
        public void SetActive(bool b) { }

        internal void Attach(Component c) {
            if (!components.Contains(c)) { components.Add(c); c._go = this; }
        }

        public T GetComponent<T>() where T : class {
            for (int i = 0; i < components.Count; i++) {
                T t = components[i] as T;
                if (t != null) return t;
            }
            return null;
        }

        public T AddComponent<T>() where T : class, new() {
            T made = new T();
            Component c = made as Component;
            if (c != null) Attach(c);
            return made;
        }

        public static GameObject Instantiate(GameObject g) { return g; }
        public static T Instantiate<T>(T o) where T : Object { return o; }
        public static T Instantiate<T>(T o, Transform p) where T : Object { return o; }
        public static T Instantiate<T>(T o, Vector3 p, Quaternion q) where T : Object { return o; }
        public static T Instantiate<T>(T o, Vector3 p, Quaternion q, Transform t) where T : Object { return o; }
        public static void Destroy(Object o) { }
        public static GameObject Find(string n) { return null; }
    }

    public class ScriptableObject : Object {
        public static T CreateInstance<T>() where T : ScriptableObject, new() { return new T(); }
    }

    public class Sprite : Object { public Bounds bounds; }
    public class Material : Object {
        public Color color;
        public void SetFloat(string n, float v) { }
        public void SetVector(string n, Vector4 v) { }
        public void SetTexture(string n, Texture t) { }
        public float GetFloat(string n) { return 0; }
    }
    public class Texture : Object { }
    public class Texture2D : Texture { }
    public class Renderer : Component { public bool enabled = true; }
    public class SpriteRenderer : Renderer {
        public Sprite sprite; public Color color; public int sortingOrder;
        public int rendererPriority; public string sortingLayerName;
    }
    public class MeshRenderer : Renderer { public Material material; public Material sharedMaterial; }
    public class MeshFilter : Component { }
    public class LineRenderer : Renderer {
        public int positionCount;
        public void SetPosition(int i, Vector3 v) { }
    }
    public class Animator : Behaviour {
        public void SetBool(string s, bool b) { }
        public void SetTrigger(string s) { }
        public AnimatorControllerParameter[] parameters { get { return new AnimatorControllerParameter[0]; } }
        public void ResetTrigger(string s) { }
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) { return new AnimatorStateInfo(); }
    }
    public class AnimatorControllerParameter { public string name; }
    public struct AnimatorStateInfo { public float normalizedTime { get { return 0; } } }
    public class StateMachineBehaviour : ScriptableObject {
        public virtual void OnStateEnter(Animator a, AnimatorStateInfo s, int l) { }
        public virtual void OnStateExit(Animator a, AnimatorStateInfo s, int l) { }
        public virtual void OnStateUpdate(Animator a, AnimatorStateInfo s, int l) { }
    }

    public static class Debug {
        public static bool verbose = false;
        public static void Log(object o) { if (verbose) Console.WriteLine("[log] " + o); }
        public static void LogWarning(object o) { Console.WriteLine("[warn] " + o); }
        public static void LogError(object o) { Console.WriteLine("[error] " + o); }
    }
    public static class Screen { public static int width { get { return 1920; } } public static int height { get { return 1080; } } }
    public static class GUI {
        public static bool enabled = true;
        public static void Box(Rect r, string s) { }
        public static void Label(Rect r, string s) { }
        public static bool Button(Rect r, string s) { return false; }
    }
    public static class Time {
        public static float deltaTime { get { return 0.016f; } }
        public static float fixedDeltaTime { get { return 0.02f; } }
        public static float time { get { return 0; } }
        public static float unscaledDeltaTime { get { return 0.016f; } }
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
    public enum KeyCode { None, Space, Escape, Return, Tab, Home, LeftArrow, RightArrow, UpArrow, DownArrow, Equals, Minus, KeypadPlus, KeypadMinus, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z }
    public static class Application {
        public static string persistentDataPath { get { return "/tmp/hextactica"; } }
        public static string dataPath { get { return "/tmp/hextactica"; } }
        public static bool isPlaying { get { return true; } }
    }
    public static class JsonUtility {
        public static string ToJson(object o) { return "{}"; }
        public static T FromJson<T>(string s) { return default(T); }
    }
    public static class Random {
        public static float Range(float a, float b) { return a; }
        public static int Range(int a, int b) { return a; }
        public static float value { get { return 0.5f; } }
        public static void InitState(int seed) { }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class TextAreaAttribute : Attribute { public TextAreaAttribute(int a, int b) { } }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class CreateAssetMenuAttribute : Attribute { public string menuName; public string fileName; }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class SerializeFieldAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class HideInInspectorAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class SpaceAttribute : Attribute { public SpaceAttribute() { } public SpaceAttribute(float f) { } }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class ContextMenuAttribute : Attribute { public ContextMenuAttribute(string s) { } }
}

namespace UnityEngine.UI {
    public class Graphic : UnityEngine.Component { public UnityEngine.Color color; }
    public class Text : Graphic { public string text = ""; public int fontSize; }
    public class Image : Graphic { public UnityEngine.Sprite sprite; }
    public class Button : UnityEngine.Component { }
}

namespace UnityEditor {
    public class ScriptableSingleton<T> : UnityEngine.ScriptableObject where T : UnityEngine.ScriptableObject {
        static T inst;
        public static T instance { get { if (inst == null) inst = System.Activator.CreateInstance<T>(); return inst; } }
    }
}
