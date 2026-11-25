using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace ZG2
{
    /// <summary>
    /// Draws the settings tooltip above the Mod Manager window (pattern from CareerRework).
    ///
    /// A tooltip drawn inside the settings' OnGUI lies under everything UMM draws afterwards
    /// (bottom bar, mod list). So the settings only measure and position it, and this overlay
    /// draws it as its own GUI window that is brought to the front every frame, with UMM's
    /// GUI matrix and skin.
    /// </summary>
    internal class ZG2TooltipOverlay : MonoBehaviour
    {
        private const int WindowId = 0x5A4732;
        private const int KeepFrames = 2;

        private static ZG2TooltipOverlay instance;
        private static string text;
        private static Rect rect;
        private static Matrix4x4 matrix = Matrix4x4.identity;
        private static GUISkin skin;
        private static int shownFrame = -100;
        private static GUIStyle boxStyle;

        // Area of the settings (top-level GUI coordinates), captured at the start of the settings GUI.
        private static float areaX, areaWidth;
        private static FieldInfo windowRectField;
        private static bool windowRectResolved;

        public static GUIStyle BoxStyle
        {
            get
            {
                if (boxStyle == null)
                {
                    boxStyle = new GUIStyle(GUI.skin.label)
                    {
                        wordWrap = true,
                        richText = true,
                        alignment = TextAnchor.UpperLeft,
                        clipping = TextClipping.Overflow,
                        padding = new RectOffset(10, 10, 8, 10)
                    };
                    boxStyle.normal.textColor = new Color(0.95f, 0.95f, 0.95f);
                }
                return boxStyle;
            }
        }

        /// <summary>First call in the settings GUI: remembers width and position of the settings area.</summary>
        public static void CaptureArea()
        {
            Rect r = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            Vector2 p = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y));
            areaX = p.x;
            areaWidth = r.width;
        }

        /// <summary>Last call in the settings GUI: takes GUI.tooltip and hands it to the overlay.</summary>
        public static void DrawTooltip()
        {
            if (Event.current.type != EventType.Repaint) return;
            string tip = GUI.tooltip;
            GUI.tooltip = string.Empty; // UMM must not show it a second time
            if (string.IsNullOrEmpty(tip)) return;

            Rect win = WindowRect();
            float left = areaWidth > 50f ? Mathf.Max(areaX, win.xMin) : win.xMin;
            float right = areaWidth > 50f ? Mathf.Min(areaX + areaWidth, win.xMax) : win.xMax;
            float avail = Mathf.Max(100f, right - left);

            float width = Mathf.Min(Mathf.Clamp(avail * 0.45f, 320f, 560f), avail);
            var content = new GUIContent(tip);
            float height = BoxStyle.CalcHeight(content, width) + 2f;

            Vector2 m = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);

            // Horizontal: left of the mouse, otherwise right of it, otherwise clamped.
            float x = m.x - width - 16f;
            if (x < left)
            {
                x = m.x + 20f;
                if (x + width > right) x = Mathf.Clamp(right - width, left, right);
            }

            // Vertical: below the mouse, otherwise above it, otherwise clamped into the window.
            float y = m.y + 20f;
            if (y + height > win.yMax)
            {
                y = m.y - height - 12f;
                if (y < win.yMin) y = Mathf.Clamp(win.yMax - height, win.yMin, win.yMax);
            }

            Show(tip, new Rect(x, y, width, height));
        }

        // UnityModManager.UI is only touched via reflection: its base class lives in the old UnityEngine.dll
        // facade, which this project does not reference.
        private static PropertyInfo uiInstanceProp;
        private static FieldInfo uiInstanceField;

        private static Rect WindowRect()
        {
            if (!windowRectResolved)
            {
                windowRectResolved = true;
                var uiType = typeof(UnityModManager).Assembly.GetType("UnityModManagerNet.UnityModManager+UI");
                if (uiType != null)
                {
                    const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
                    windowRectField = uiType.GetField("mWindowRect", any);
                    uiInstanceProp = uiType.GetProperty("Instance", any);
                    if (uiInstanceProp == null) uiInstanceField = uiType.GetField("mInstance", any);
                }
            }
            try
            {
                object ui = uiInstanceProp != null ? uiInstanceProp.GetValue(null, null)
                          : uiInstanceField != null ? uiInstanceField.GetValue(null) : null;
                if (windowRectField != null && ui != null)
                {
                    var r = (Rect)windowRectField.GetValue(ui);
                    if (r.width > 50f && r.height > 50f) return r;
                }
            }
            catch { }
            // Fallback: whole screen in GUI coordinates
            Vector3 s = GUI.matrix.lossyScale;
            return new Rect(0f, 0f, Screen.width / Mathf.Max(0.01f, s.x), Screen.height / Mathf.Max(0.01f, s.y));
        }

        private static void Show(string tip, Rect r)
        {
            if (instance == null)
            {
                var go = new GameObject("[ZG2 Tooltip]");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<ZG2TooltipOverlay>();
            }
            text = tip;
            rect = r;
            matrix = GUI.matrix;
            skin = GUI.skin;
            shownFrame = Time.frameCount;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(text) || Time.frameCount - shownFrame > KeepFrames) return;

            var oldMatrix = GUI.matrix;
            var oldSkin = GUI.skin;
            GUI.matrix = matrix;
            if (skin != null) GUI.skin = skin;

            GUI.Window(WindowId, rect, DrawWindow, GUIContent.none, GUIStyle.none);
            GUI.BringWindowToFront(WindowId);

            GUI.matrix = oldMatrix;
            GUI.skin = oldSkin;
        }

        private static void DrawWindow(int id)
        {
            var r = new Rect(0f, 0f, rect.width, rect.height);
            Color old = GUI.color;
            GUI.color = new Color(0.08f, 0.08f, 0.08f, 1f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.8f, 0.3f, 1f);
            GUI.DrawTexture(new Rect(0f, 0f, r.width, 2f), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(r, text, BoxStyle);
        }

        public static void Shutdown()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
            text = null;
        }
    }
}