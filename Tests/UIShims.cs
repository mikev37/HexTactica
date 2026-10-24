// Type-check-only shim for the uGUI parts of Assets/UI/TransportPanel.cs (not run by the harness).
using System;
namespace UnityEngine {
    public static class BuiltinResources { }
}
namespace UnityEngine.UI {
    public class CanvasScaler : UnityEngine.Behaviour { public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize } public ScaleMode uiScaleMode; public UnityEngine.Vector2 referenceResolution; public float matchWidthOrHeight; }
    public class GraphicRaycaster : UnityEngine.Behaviour { }
    public class GridLayoutGroup : UnityEngine.Behaviour { public enum Constraint { Flexible, FixedColumnCount, FixedRowCount } public UnityEngine.Vector2 cellSize, spacing; public Constraint constraint; public int constraintCount; }
    public class VerticalLayoutGroup : UnityEngine.Behaviour { public UnityEngine.RectOffset padding; public float spacing; public bool childControlWidth, childControlHeight, childForceExpandWidth, childForceExpandHeight; }
    public class ContentSizeFitter : UnityEngine.Behaviour { public enum FitMode { Unconstrained, MinSize, PreferredSize } public FitMode horizontalFit, verticalFit; }
}
namespace UnityEngine.EventSystems {
    public class EventSystem : UnityEngine.Behaviour { public static EventSystem current; public bool IsPointerOverGameObject() { return false; } }
    public class StandaloneInputModule : UnityEngine.Behaviour { }
}

namespace UnityEngine.UI {
    public class HorizontalLayoutGroup : UnityEngine.Behaviour { public float spacing; public UnityEngine.TextAnchor childAlignment; public bool childControlWidth, childControlHeight, childForceExpandWidth, childForceExpandHeight; public UnityEngine.RectOffset padding; }
    public class LayoutElement : UnityEngine.Behaviour { public float preferredWidth, preferredHeight, minWidth, minHeight; }
}
namespace TMPro {
    public enum TextAlignmentOptions { Left, Center, Right, MidlineLeft, Midline, MidlineRight }
    public class TMP_Text : UnityEngine.UI.Graphic { public string text = ""; public float fontSize; public TextAlignmentOptions alignment; }
    public class TextMeshProUGUI : TMP_Text { }
}
