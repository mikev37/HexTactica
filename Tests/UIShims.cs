// Type-check-only shim for the uGUI parts of Assets/UI/TransportPanel.cs (not run by the harness).
using System;
namespace UnityEngine {
    public static class RectTransformUtility { public static bool RectangleContainsScreenPoint(RectTransform r, Vector2 p, Camera c) { return false; } }
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
    public class LayoutElement : UnityEngine.Behaviour { public float preferredWidth, preferredHeight, minWidth, minHeight, flexibleWidth, flexibleHeight; }
}
namespace TMPro {
    public enum TextAlignmentOptions { Left, Center, Right, MidlineLeft, Midline, MidlineRight, TopLeft, Top, TopRight }
    public enum FontStyles { Normal = 0, Bold = 1, Italic = 2 }
    public class TMP_FontAsset : UnityEngine.Object { }
    public class TMP_Text : UnityEngine.UI.Graphic { public int maxVisibleCharacters = 99999; public string text = ""; public float fontSize; public TMP_FontAsset font; public TextAlignmentOptions alignment; public FontStyles fontStyle; public UnityEngine.RectTransform rectTransform; }
    public class TextMeshProUGUI : TMP_Text { }
    public class TextMeshPro : TMP_Text { public int sortingLayerID, sortingOrder; }
}
