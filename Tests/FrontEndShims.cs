// Type-check-only shim for the front end's UI (uGUI controls, TextMeshPro input and dropdown) and the editor APIs its
// scene builder uses. Signatures follow Unity's; nothing here runs.
using System;
using System.Collections.Generic;
namespace UnityEngine.UI {
    public class Toggle : Selectable {
        public class ToggleEvent : UnityEngine.Events.UnityEvent<bool> { }
        public bool isOn;
        public Graphic graphic;
        public ToggleEvent onValueChanged = new ToggleEvent();
        public void SetIsOnWithoutNotify(bool v) { isOn = v; }
    }
    public class Slider : Selectable {
        public class SliderEvent : UnityEngine.Events.UnityEvent<float> { }
        public float value, minValue, maxValue;
        public SliderEvent onValueChanged = new SliderEvent();
        public void SetValueWithoutNotify(float v) { value = v; }
    }
    public class Scrollbar : Selectable { }
    public class ScrollRect : UnityEngine.Behaviour {
        public enum MovementType { Unrestricted, Elastic, Clamped }
        public enum ScrollbarVisibility { Permanent, AutoHide, AutoHideAndExpandViewport }
        public UnityEngine.RectTransform content;
        public bool horizontal, vertical;
        public MovementType movementType;
        public float scrollSensitivity;
        public Scrollbar horizontalScrollbar, verticalScrollbar;
        public ScrollbarVisibility horizontalScrollbarVisibility, verticalScrollbarVisibility;
    }
    public class RawImage : Graphic { public UnityEngine.Texture texture; }
    public static class LayoutRebuilder { public static void ForceRebuildLayoutImmediate(UnityEngine.RectTransform r) { } }
    public static class DefaultControls {
        public struct Resources { public UnityEngine.Sprite standard, background, inputField, knob, checkmark, dropdown, mask; }
        public static UnityEngine.GameObject CreateSlider(Resources r) { return new UnityEngine.GameObject("Slider"); }
        public static UnityEngine.GameObject CreateScrollView(Resources r) { return new UnityEngine.GameObject("Scroll View"); }
    }
}
namespace TMPro {
    public class TMP_InputField : UnityEngine.UI.Selectable {
        public enum ContentType { Standard, Autocorrected, IntegerNumber, DecimalNumber, Alphanumeric, Name, EmailAddress, Password, Pin, Custom }
        public class SubmitEvent : UnityEngine.Events.UnityEvent<string> { }
        public string text = "";
        public ContentType contentType;
        public SubmitEvent onEndEdit = new SubmitEvent();
        public TMP_Text textComponent;
        public UnityEngine.UI.Graphic placeholder;
    }
    public class TMP_Dropdown : UnityEngine.UI.Selectable {
        public class DropdownEvent : UnityEngine.Events.UnityEvent<int> { }
        public int value;
        public TMP_Text captionText, itemText;
        public UnityEngine.RectTransform template;
        public DropdownEvent onValueChanged = new DropdownEvent();
        public void ClearOptions() { }
        public void AddOptions(List<string> options) { }
        public void SetValueWithoutNotify(int v) { value = v; }
    }
    public static class TMP_DefaultControls {
        public struct Resources { public UnityEngine.Sprite standard, background, inputField, knob, checkmark, dropdown, mask; }
        public static UnityEngine.GameObject CreateDropdown(Resources r) { return new UnityEngine.GameObject("Dropdown"); }
        public static UnityEngine.GameObject CreateInputField(Resources r) { return new UnityEngine.GameObject("InputField"); }
    }
}
namespace UnityEditor {
    public static class EditorBuildSettings { public static EditorBuildSettingsScene[] scenes = new EditorBuildSettingsScene[0]; }
    public class EditorBuildSettingsScene { public string path; public bool enabled; public EditorBuildSettingsScene(string p, bool e) { path = p; enabled = e; } }
    public static partial class AssetDatabaseExtra { }
}
namespace UnityEditor.SceneManagement {
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
    public enum NewSceneMode { Single, Additive }
    public enum OpenSceneMode { Single, Additive, AdditiveWithoutLoading }
    public static class EditorSceneManager {
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup s, NewSceneMode m) { return new UnityEngine.SceneManagement.Scene(); }
        public static bool SaveScene(UnityEngine.SceneManagement.Scene s, string path = "") { return true; }
        public static bool MarkSceneDirty(UnityEngine.SceneManagement.Scene s) { return true; }
        public static bool CloseScene(UnityEngine.SceneManagement.Scene s, bool remove) { return true; }
        public static UnityEngine.SceneManagement.Scene OpenScene(string path, OpenSceneMode m) { return new UnityEngine.SceneManagement.Scene(); }
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() { return true; }
    }
}
namespace UnityEditor.Events {
    public static class UnityEventTools {
        public static void AddPersistentListener(UnityEngine.Events.UnityEvent e, UnityEngine.Events.UnityAction call) { }
    }
}
namespace UnityEngine {
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
    public class RequireComponent : System.Attribute { public RequireComponent(System.Type t) { } }
}
