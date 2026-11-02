using System;
using System.Collections.Generic;
using Survival.Client.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Survival.Client.Game
{
    /// <summary>
    /// Touch controls: floating joystick (left), look-drag area (rest of the screen, multi-touch),
    /// and action buttons whose position/size come from the player's saved layout.
    /// </summary>
    public sealed class MobileControls : MonoBehaviour
    {
        public sealed class ButtonDef
        {
            public string Id;
            public string Label;
            public Vector2 DefaultPos; // normalized in safe area
            public float Size;
            public bool Toggle;
        }

        public static readonly ButtonDef[] Buttons =
        {
            new ButtonDef { Id = "jump", Label = "jump", DefaultPos = new Vector2(0.92f, 0.22f), Size = 150f },
            new ButtonDef { Id = "crouch", Label = "crouch", DefaultPos = new Vector2(0.80f, 0.12f), Size = 120f, Toggle = true },
            new ButtonDef { Id = "sprint", Label = "sprint", DefaultPos = new Vector2(0.80f, 0.32f), Size = 120f, Toggle = true },
            new ButtonDef { Id = "menu", Label = "menu", DefaultPos = new Vector2(0.96f, 0.93f), Size = 96f },
        };

        public const string JoystickId = "joystick";
        public static readonly Vector2 JoystickDefault = new Vector2(0.14f, 0.24f);

        private RectTransform _root;
        private VirtualJoystick _stick;
        private LookArea _look;
        private readonly Dictionary<string, TouchButton> _buttons = new Dictionary<string, TouchButton>();

        public Vector2 Move => _stick != null ? _stick.Value : Vector2.zero;
        public bool StickAtEdge => _stick != null && _stick.Value.y > 0.92f;
        public Vector2 ConsumeLook() => _look != null ? _look.Consume() : Vector2.zero;
        public bool Held(string id) => _buttons.TryGetValue(id, out TouchButton b) && b.Held;
        public bool Toggled(string id) => _buttons.TryGetValue(id, out TouchButton b) && b.On;
        public bool Pressed(string id) => _buttons.TryGetValue(id, out TouchButton b) && b.ConsumePress();
        public void SetToggle(string id, bool on)
        {
            if (_buttons.TryGetValue(id, out TouchButton b)) b.SetOn(on);
        }

        public static MobileControls Create(RectTransform safeRoot, bool editMode = false)
        {
            var rt = UIFactory.Rect(safeRoot, "Controls");
            UIFactory.Stretch(rt);
            var c = rt.gameObject.AddComponent<MobileControls>();
            c._root = rt;
            c.Build(editMode);
            return c;
        }

        private void Build(bool edit)
        {
            GameSettings s = App.Settings;
            // Look area first (behind everything)
            var lookRt = UIFactory.Rect(_root, "LookArea");
            UIFactory.Stretch(lookRt);
            Image lookImg = lookRt.gameObject.AddComponent<Image>();
            lookImg.color = new Color(0, 0, 0, 0);
            if (!edit) _look = lookRt.gameObject.AddComponent<LookArea>();

            // Joystick zone: left 45 % of the screen
            var zone = UIFactory.Rect(_root, "StickZone");
            zone.anchorMin = new Vector2(0, 0);
            zone.anchorMax = new Vector2(0.45f, 0.75f);
            zone.offsetMin = zone.offsetMax = Vector2.zero;
            Image zoneImg = zone.gameObject.AddComponent<Image>();
            zoneImg.color = new Color(0, 0, 0, 0);
            zoneImg.raycastTarget = !edit;
            _stick = zone.gameObject.AddComponent<VirtualJoystick>();
            HudLayoutEntry stickLayout = s.GetLayout(JoystickId);
            Vector2 stickPos = stickLayout != null ? new Vector2(stickLayout.X, stickLayout.Y) : JoystickDefault;
            float stickScale = (stickLayout?.Scale ?? 1f) * s.JoystickSize;
            _stick.Build(_root, stickPos, 260f * stickScale, s.ButtonOpacity, edit);

            foreach (ButtonDef def in Buttons)
            {
                HudLayoutEntry l = s.GetLayout(def.Id);
                Vector2 pos = l != null ? new Vector2(l.X, l.Y) : def.DefaultPos;
                float size = def.Size * (l?.Scale ?? 1f);
                TouchButton b = TouchButton.Create(_root, def, pos, size, s.ButtonOpacity, edit);
                _buttons[def.Id] = b;
            }
        }

        public IEnumerable<LayoutHandle> Handles()
        {
            yield return _stick.Handle;
            foreach (TouchButton b in _buttons.Values) yield return b.Handle;
        }
    }

    /// <summary>Draggable wrapper used by the layout editor.</summary>
    public sealed class LayoutHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerDownHandler
    {
        public string Id;
        public RectTransform Target;
        public float Scale = 1f;
        public bool Editable;
        public Action<LayoutHandle> Selected;
        private RectTransform _parent;

        public Vector2 Normalized
        {
            get
            {
                _parent = _parent != null ? _parent : (RectTransform)Target.parent;
                Rect r = _parent.rect;
                Vector2 p = Target.anchoredPosition;
                return new Vector2(Mathf.Clamp01(p.x / r.width), Mathf.Clamp01(p.y / r.height));
            }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (Editable) Selected?.Invoke(this);
        }

        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            if (!Editable) return;
            _parent = _parent != null ? _parent : (RectTransform)Target.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, e.position, e.pressEventCamera, out Vector2 local))
            {
                Rect r = _parent.rect;
                Vector2 fromCorner = local - r.min;
                fromCorner.x = Mathf.Clamp(fromCorner.x, 40f, r.width - 40f);
                fromCorner.y = Mathf.Clamp(fromCorner.y, 40f, r.height - 40f);
                Target.anchoredPosition = fromCorner;
            }
        }
    }

    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private RectTransform _base;
        private RectTransform _knob;
        private Image _baseImg;
        private Vector2 _home;
        private float _radius;
        private int _pointer = int.MinValue;
        private RectTransform _space;

        public Vector2 Value { get; private set; }
        public LayoutHandle Handle { get; private set; }

        public void Build(RectTransform space, Vector2 normalizedHome, float size, float opacity, bool edit)
        {
            _space = space;
            _base = UIFactory.Rect(space, "Stick");
            _base.anchorMin = _base.anchorMax = Vector2.zero;
            _base.pivot = new Vector2(0.5f, 0.5f);
            _base.sizeDelta = new Vector2(size, size);
            _baseImg = _base.gameObject.AddComponent<Image>();
            _baseImg.sprite = UISprites.Ring(128, 0.08f);
            _baseImg.color = new Color(1, 1, 1, 0.45f * opacity);
            _baseImg.raycastTarget = edit;
            Image fill = UIFactory.Image(_base, "Fill", new Color(0, 0, 0, 0.25f * opacity), UISprites.Circle(128));
            UIFactory.Stretch(fill.rectTransform, 6);
            _knob = UIFactory.Image(_base, "Knob", new Color(1, 1, 1, 0.75f * opacity), UISprites.Circle(96)).rectTransform;
            _knob.sizeDelta = new Vector2(size * 0.42f, size * 0.42f);
            _radius = size * 0.4f;

            Handle = _base.gameObject.AddComponent<LayoutHandle>();
            Handle.Id = MobileControls.JoystickId;
            Handle.Target = _base;
            Handle.Editable = edit;
            // Position after layout so rect sizes are known
            Canvas.ForceUpdateCanvases();
            Rect r = space.rect;
            _home = new Vector2(normalizedHome.x * r.width, normalizedHome.y * r.height);
            _base.anchoredPosition = _home;
        }

        private void LateUpdate()
        {
            if (_pointer == int.MinValue && _space != null && Handle != null && !Handle.Editable)
            {
                Rect r = _space.rect;
                HudLayoutEntry l = App.Settings.GetLayout(MobileControls.JoystickId);
                Vector2 n = l != null ? new Vector2(l.X, l.Y) : MobileControls.JoystickDefault;
                _home = new Vector2(n.x * r.width, n.y * r.height);
                _base.anchoredPosition = Vector2.Lerp(_base.anchoredPosition, _home, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
            }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer != int.MinValue) return;
            _pointer = e.pointerId;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_space, e.position, e.pressEventCamera, out Vector2 local))
            {
                _base.anchoredPosition = local - _space.rect.min; // floating stick: appears under the thumb
            }
            _baseImg.color = _baseImg.color.WithAlpha(0.7f);
            OnDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_base, e.position, e.pressEventCamera, out Vector2 local)) return;
            Vector2 v = Vector2.ClampMagnitude(local, _radius);
            _knob.anchoredPosition = v;
            Vector2 n = v / _radius;
            float mag = n.magnitude;
            // Dead zone 12 %, then rescale so full deflection is exactly 1
            Value = mag < 0.12f ? Vector2.zero : n / mag * Mathf.InverseLerp(0.12f, 1f, mag);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            _pointer = int.MinValue;
            Value = Vector2.zero;
            _knob.anchoredPosition = Vector2.zero;
            _baseImg.color = _baseImg.color.WithAlpha(0.45f * App.Settings.ButtonOpacity);
        }
    }

    /// <summary>Drag anywhere (not on a control) to look around. Supports one look finger at a time.</summary>
    public sealed class LookArea : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private int _pointer = int.MinValue;
        private Vector2 _delta;

        public Vector2 Consume()
        {
            Vector2 d = _delta;
            _delta = Vector2.zero;
            return d;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer == int.MinValue) _pointer = e.pointerId;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            // Normalize by screen height so sensitivity is resolution independent
            _delta += e.delta / Mathf.Max(1f, Screen.height) * 1000f;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId == _pointer) _pointer = int.MinValue;
        }
    }

    public sealed class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private Image _bg;
        private Text _label;
        private bool _toggle;
        private bool _pressed;
        private float _opacity;

        public bool Held { get; private set; }
        public bool On { get; private set; }
        public LayoutHandle Handle { get; private set; }

        public static TouchButton Create(RectTransform parent, MobileControls.ButtonDef def, Vector2 normalized, float size, float opacity, bool edit)
        {
            Image bg = UIFactory.Image(parent, "Btn_" + def.Id, new Color(0.08f, 0.1f, 0.13f, 0.55f * opacity), UISprites.Circle(128));
            bg.raycastTarget = true;
            bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = Vector2.zero;
            bg.rectTransform.sizeDelta = new Vector2(size, size);
            Image ring = UIFactory.Image(bg.transform, "Ring", new Color(1, 1, 1, 0.5f * opacity), UISprites.Ring(128, 0.06f));
            UIFactory.Stretch(ring.rectTransform);
            Text label = UIFactory.Label(bg.transform, "Label", Loc.T(def.Label), Mathf.RoundToInt(size * 0.17f), new Color(1, 1, 1, 0.9f * opacity), TextAnchor.MiddleCenter, UIFactory.Bold);
            UIFactory.Stretch(label.rectTransform, 6);
            var b = bg.gameObject.AddComponent<TouchButton>();
            b._bg = bg;
            b._label = label;
            b._toggle = def.Toggle;
            b._opacity = opacity;
            b.Handle = bg.gameObject.AddComponent<LayoutHandle>();
            b.Handle.Id = def.Id;
            b.Handle.Target = bg.rectTransform;
            b.Handle.Scale = size / def.Size;
            b.Handle.Editable = edit;
            Canvas.ForceUpdateCanvases();
            Rect r = parent.rect;
            bg.rectTransform.anchoredPosition = new Vector2(normalized.x * r.width, normalized.y * r.height);
            return b;
        }

        public bool ConsumePress()
        {
            bool p = _pressed;
            _pressed = false;
            return p;
        }

        public void SetOn(bool on)
        {
            On = on;
            UpdateVisual();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (Handle.Editable) return;
            Held = true;
            _pressed = true;
            if (_toggle) On = !On;
            App.Audio.PlayUi(UiSound.Press);
            transform.localScale = Vector3.one * 0.92f;
            UpdateVisual();
        }

        public void OnPointerUp(PointerEventData e)
        {
            Held = false;
            transform.localScale = Vector3.one;
            UpdateVisual();
        }

        private void UpdateVisual()
        {
            bool active = Held || On;
            _bg.color = active ? Theme.Accent.WithAlpha(0.75f * _opacity + 0.15f) : new Color(0.08f, 0.1f, 0.13f, 0.55f * _opacity);
        }
    }

    /// <summary>Full-screen editor for the touch layout (drag controls, resize, save, reset).</summary>
    public sealed class LayoutEditor : MonoBehaviour
    {
        private MobileControls _controls;
        private LayoutHandle _selected;
        private Slider _size;
        private RectTransform _openedFrom;

        public static LayoutEditor Open(RectTransform safeRoot)
        {
            Image dim = UIFactory.Image(safeRoot.parent, "LayoutEditor", new Color(0.02f, 0.03f, 0.05f, 0.9f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            var safe = UIFactory.Rect(dim.transform, "Safe");
            UIFactory.Stretch(safe);
            safe.gameObject.AddComponent<SafeArea>();
            var ed = dim.gameObject.AddComponent<LayoutEditor>();
            ed._openedFrom = safeRoot;
            ed._controls = MobileControls.Create(safe, true);
            foreach (LayoutHandle h in ed._controls.Handles()) h.Selected = ed.Select;

            Text hint = UIFactory.Label(safe, "Hint", Loc.T("drag_to_move"), 28, Theme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(1200, 60), new Vector2(0, -30), new Vector2(0.5f, 1f));
            ed._size = UIFactory.Slider(safe, "Size", 0.6f, 1.6f, 1f, v => ed.Resize(v));
            UIFactory.Place((RectTransform)ed._size.transform, new Vector2(0.5f, 1f), new Vector2(420, 44), new Vector2(0, -110), new Vector2(0.5f, 1f));
            AnimatedButton save = UIFactory.Button(safe, "Save", Loc.T("done"), ed.Save, 30, true, new Vector2(260, 80));
            UIFactory.Place(save.Background.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(260, 80), new Vector2(140, 0));
            AnimatedButton reset = UIFactory.Button(safe, "Reset", Loc.T("reset"), ed.ResetLayout, 30, false, new Vector2(260, 80));
            UIFactory.Place(reset.Background.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(260, 80), new Vector2(-140, 0));
            return ed;
        }

        private void Select(LayoutHandle h)
        {
            _selected = h;
            _size.SetValueWithoutNotify(h.Scale);
        }

        private void Resize(float s)
        {
            if (_selected == null) return;
            float baseSize = _selected.Target.sizeDelta.x / Mathf.Max(0.01f, _selected.Scale);
            _selected.Scale = s;
            _selected.Target.sizeDelta = new Vector2(baseSize * s, baseSize * s);
        }

        private void Save()
        {
            foreach (LayoutHandle h in _controls.Handles())
            {
                Vector2 n = h.Normalized;
                App.Settings.SetLayout(h.Id, n.x, n.y, h.Scale);
            }
            App.Settings.Save();
            Destroy(gameObject);
        }

        private void ResetLayout()
        {
            App.Settings.HudLayout.Clear();
            App.Settings.Save();
            RectTransform from = _openedFrom;
            Destroy(gameObject);
            Open(from);
        }
    }
}
