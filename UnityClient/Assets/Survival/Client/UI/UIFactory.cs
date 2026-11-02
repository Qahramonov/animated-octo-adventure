using System;
using Survival.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Survival.Client.UI
{
    /// <summary>Creates the EventSystem once (Input System UI module: touch, mouse, gamepad).</summary>
    public static class UIRoot
    {
        public static void EnsureEventSystem(Transform parent)
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("[EventSystem]");
            go.transform.SetParent(parent, false);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }

    /// <summary>
    /// Code-built uGUI: canvases, panels, labels, buttons, sliders and toggles in the OROL style.
    /// Everything is created at runtime so the project needs no prefab/scene wiring.
    /// </summary>
    public static class UIFactory
    {
        private static Font _regular;
        private static Font _bold;
        private static Font _display;

        public static Font Regular => _regular ? _regular : (_regular = LoadFont("Fonts/Play-Regular"));
        public static Font Bold => _bold ? _bold : (_bold = LoadFont("Fonts/Play-Bold"));
        public static Font Display => _display ? _display : (_display = LoadFont("Fonts/RussoOne-Regular"));

        private static Font LoadFont(string path)
        {
            Font f = Resources.Load<Font>(path);
            if (f == null)
            {
                Log.Warn("UI", "Font missing: " + path + " (using built-in)");
                f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return f;
        }

        public static Canvas Canvas(string name, int order, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            c.pixelPerfect = false;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.6f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        /// <summary>Full-screen child that follows Screen.safeArea (notches, rounded corners).</summary>
        public static RectTransform SafeRoot(Canvas canvas)
        {
            var rt = Rect(canvas.transform, "Safe");
            Stretch(rt);
            rt.gameObject.AddComponent<SafeArea>();
            return rt;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>Anchors at a normalized point with a fixed size.</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 offset = default, Vector2? pivot = null)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
        }

        public static Image Image(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = sprite.border.sqrMagnitude > 0 ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            }
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Dark glass panel with a subtle light border and top highlight.</summary>
        public static Image Panel(Transform parent, string name, Color? color = null, int radius = 22)
        {
            Image bg = Image(parent, name, color ?? Theme.Panel, UISprites.Rounded(radius));
            bg.raycastTarget = true;
            Image border = Image(bg.transform, "Border", Theme.PanelBorder, UISprites.Rounded(radius, 2));
            Stretch(border.rectTransform);
            Image sheen = Image(bg.transform, "Sheen", Color.white, UISprites.Gradient(new Color(1, 1, 1, 0.07f), new Color(1, 1, 1, 0f)));
            sheen.rectTransform.anchorMin = new Vector2(0, 0.55f);
            sheen.rectTransform.anchorMax = Vector2.one;
            sheen.rectTransform.offsetMin = new Vector2(4, 0);
            sheen.rectTransform.offsetMax = new Vector2(-4, -4);
            return bg;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color? color = null,
            TextAnchor align = TextAnchor.MiddleLeft, Font font = null)
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font != null ? font : Regular;
            t.text = text;
            t.fontSize = size;
            t.color = color ?? Theme.Text;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        public static Shadow AddShadow(Graphic g, float alpha = 0.6f, float dist = 2f)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, alpha);
            s.effectDistance = new Vector2(dist, -dist);
            return s;
        }

        /// <summary>Animated button: glass background, label, optional accent bar; press/hover feedback and click sound.</summary>
        public static AnimatedButton Button(Transform parent, string name, string text, Action onClick, int fontSize = 34,
            bool accent = false, Vector2? size = null)
        {
            Image bg = Image(parent, name, accent ? Theme.Accent : Theme.Button, UISprites.Rounded(16));
            bg.raycastTarget = true;
            bg.rectTransform.sizeDelta = size ?? new Vector2(360, 86);
            Image border = Image(bg.transform, "Border", accent ? new Color(1, 1, 1, 0.35f) : Theme.PanelBorder, UISprites.Rounded(16, 2));
            Stretch(border.rectTransform);
            Text label = Label(bg.transform, "Label", text, fontSize, accent ? new Color(0.1f, 0.06f, 0.02f) : Theme.Text, TextAnchor.MiddleCenter, Bold);
            Stretch(label.rectTransform, 8);
            var btn = bg.gameObject.AddComponent<AnimatedButton>();
            btn.Init(bg, label, accent, onClick);
            return btn;
        }

        public static Slider Slider(Transform parent, string name, float min, float max, float value, Action<float> onChange, bool whole = false)
        {
            var rt = Rect(parent, name);
            rt.sizeDelta = new Vector2(420, 44);
            Image track = Image(rt, "Track", new Color(1, 1, 1, 0.12f), UISprites.Rounded(10));
            track.rectTransform.anchorMin = new Vector2(0, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0, 12);
            var fillArea = Rect(rt, "FillArea");
            fillArea.anchorMin = new Vector2(0, 0.5f);
            fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.sizeDelta = new Vector2(-24, 12);
            Image fill = Image(fillArea, "Fill", Theme.Accent, UISprites.Rounded(10));
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Rect(rt, "HandleArea");
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(16, 0);
            handleArea.offsetMax = new Vector2(-16, 0);
            Image handle = Image(handleArea, "Handle", Color.white, UISprites.Circle(64));
            handle.rectTransform.sizeDelta = new Vector2(38, 38);
            handle.raycastTarget = true;
            var s = rt.gameObject.AddComponent<UnityEngine.UI.Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = min;
            s.maxValue = max;
            s.wholeNumbers = whole;
            s.SetValueWithoutNotify(value);
            s.onValueChanged.AddListener(v => onChange?.Invoke(v));
            Image hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            return s;
        }

        public static Toggle Toggle(Transform parent, string name, bool value, Action<bool> onChange)
        {
            var rt = Rect(parent, name);
            rt.sizeDelta = new Vector2(96, 48);
            Image bg = Image(rt, "Bg", new Color(1, 1, 1, 0.14f), UISprites.Rounded(24));
            Stretch(bg.rectTransform);
            bg.raycastTarget = true;
            Image knob = Image(rt, "Knob", Color.white, UISprites.Circle(64));
            knob.rectTransform.sizeDelta = new Vector2(38, 38);
            var t = rt.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            t.targetGraphic = bg;
            t.isOn = value;
            void Visual(bool on)
            {
                bg.color = on ? Theme.Accent.WithAlpha(0.9f) : new Color(1, 1, 1, 0.14f);
                knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(on ? 0.72f : 0.28f, 0.5f);
                knob.rectTransform.anchoredPosition = Vector2.zero;
            }
            Visual(value);
            t.onValueChanged.AddListener(on =>
            {
                Visual(on);
                App.Audio?.PlayUi(UiSound.Tick);
                onChange?.Invoke(on);
            });
            return t;
        }

        public static InputField Input(Transform parent, string name, string value, int fontSize = 32, string placeholder = "")
        {
            Image bg = Image(parent, name, new Color(0, 0, 0, 0.45f), UISprites.Rounded(12));
            bg.raycastTarget = true;
            bg.rectTransform.sizeDelta = new Vector2(520, 72);
            Text text = Label(bg.transform, "Text", value, fontSize, Theme.Text, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 18);
            text.supportRichText = false;
            Text ph = Label(bg.transform, "Placeholder", placeholder, fontSize, Theme.TextDim.WithAlpha(0.6f), TextAnchor.MiddleLeft);
            Stretch(ph.rectTransform, 18);
            var field = bg.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = ph;
            field.text = value;
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        /// <summary>Horizontal bar (vitals, progress). Returns the fill image (use fillAmount).</summary>
        public static Image Bar(Transform parent, string name, Color color, Vector2 size)
        {
            Image bg = Image(parent, name, new Color(0, 0, 0, 0.45f), UISprites.Rounded(8));
            bg.rectTransform.sizeDelta = size;
            Image fill = Image(bg.transform, "Fill", color, UISprites.Rounded(8));
            Stretch(fill.rectTransform, 3);
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 1f;
            return fill;
        }

        /// <summary>Vertical stack layout helper.</summary>
        public static VerticalLayoutGroup VStack(RectTransform rt, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.UpperLeft)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset(0, 0, 0, 0);
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = false;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static LayoutElement Layout(Component c, float height, float width = -1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            if (width > 0)
            {
                le.preferredWidth = width;
                le.minWidth = width;
            }
            return le;
        }

        /// <summary>Scrollable vertical list. Returns the content transform.</summary>
        public static RectTransform ScrollList(Transform parent, string name, out ScrollRect scroll)
        {
            var view = Rect(parent, name);
            var mask = view.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;
            Image hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var content = Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            VStack(content, 14, new RectOffset(4, 4, 4, 4));
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;
            scroll.viewport = view;
            return content;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
