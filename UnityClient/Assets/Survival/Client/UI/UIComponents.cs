using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Survival.Client.UI
{
    /// <summary>Keeps a RectTransform inside Screen.safeArea (notches, punch holes, rounded corners).</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeArea : MonoBehaviour
    {
        private Rect _applied;
        private Vector2Int _screen;

        private void OnEnable() => Apply();

        private void Update()
        {
            if (_applied != Screen.safeArea || _screen.x != Screen.width || _screen.y != Screen.height) Apply();
        }

        private void Apply()
        {
            Rect safe = Screen.safeArea;
            _applied = safe;
            _screen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var rt = (RectTransform)transform;
            Vector2 min = safe.position;
            Vector2 max = safe.position + safe.size;
            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>Button with hover glow, press squash, release bounce and UI sound.</summary>
    public sealed class AnimatedButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler,
        IPointerExitHandler, IPointerClickHandler
    {
        private Image _bg;
        private Text _label;
        private bool _accent;
        private Action _onClick;
        private Color _base;
        private float _hover;
        private float _press;
        private float _bounce;
        private bool _interactable = true;

        public Text Label => _label;
        public Image Background => _bg;

        public bool Interactable
        {
            get => _interactable;
            set
            {
                _interactable = value;
                if (_bg) _bg.color = value ? _base : _base * new Color(0.6f, 0.6f, 0.6f, 0.7f);
                if (_label) _label.color = _label.color.WithAlpha(value ? 1f : 0.45f);
            }
        }

        public void Init(Image bg, Text label, bool accent, Action onClick)
        {
            _bg = bg;
            _label = label;
            _accent = accent;
            _onClick = onClick;
            _base = bg.color;
        }

        public void SetText(string t)
        {
            if (_label) _label.text = t;
        }

        public void SetColor(Color c)
        {
            _base = c;
            if (_bg) _bg.color = c;
        }

        public void SetAction(Action a) => _onClick = a;

        public void OnPointerEnter(PointerEventData e) => _hover = 1f;
        public void OnPointerExit(PointerEventData e)
        {
            _hover = 0f;
            _press = 0f;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (!_interactable) return;
            _press = 1f;
            App.Audio?.PlayUi(UiSound.Press);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (_press > 0f) _bounce = 1f;
            _press = 0f;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (!_interactable) return;
            App.Audio?.PlayUi(_accent ? UiSound.Confirm : UiSound.Click);
            _onClick?.Invoke();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _bounce = Mathf.MoveTowards(_bounce, 0f, dt * 4f);
            float bounce = Mathf.Sin(_bounce * Mathf.PI) * 0.06f * _bounce;
            float target = 1f - _press * 0.06f + _hover * 0.02f + bounce;
            float s = Mathf.Lerp(transform.localScale.x, target, 1f - Mathf.Exp(-dt * 22f));
            transform.localScale = new Vector3(s, s, 1f);
            if (_interactable && _bg)
            {
                Color hover = _accent ? Theme.AccentBright : Theme.ButtonHover;
                _bg.color = Color.Lerp(_base, hover, Mathf.Max(_hover * 0.8f, _press));
            }
        }
    }

    /// <summary>Coroutine tween helpers used by panels and the intro (unscaled time).</summary>
    public static class UIAnim
    {
        public static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
        public static float EaseInOut(float t) => Mathf.SmoothStep(0f, 1f, t);

        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        public static IEnumerator Fade(CanvasGroup g, float to, float duration)
        {
            float from = g.alpha;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                g.alpha = Mathf.Lerp(from, to, EaseInOut(t / duration));
                yield return null;
            }
            g.alpha = to;
        }

        /// <summary>Panel entrance: fade + slide + slight scale.</summary>
        public static IEnumerator PopIn(RectTransform rt, CanvasGroup g, Vector2 fromOffset, float duration = 0.35f)
        {
            Vector2 target = rt.anchoredPosition;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = EaseOutCubic(t / duration);
                rt.anchoredPosition = target + fromOffset * (1f - k);
                float s = Mathf.Lerp(0.96f, 1f, k);
                rt.localScale = new Vector3(s, s, 1f);
                if (g) g.alpha = k;
                yield return null;
            }
            rt.anchoredPosition = target;
            rt.localScale = Vector3.one;
            if (g) g.alpha = 1f;
        }

        public static CanvasGroup Group(Component c)
        {
            var g = c.GetComponent<CanvasGroup>();
            return g != null ? g : c.gameObject.AddComponent<CanvasGroup>();
        }
    }

    /// <summary>Short message toasts at the top of the screen.</summary>
    public sealed class Toaster : MonoBehaviour
    {
        private RectTransform _root;

        public static Toaster Create(RectTransform safeRoot)
        {
            var rt = UIFactory.Rect(safeRoot, "Toasts");
            UIFactory.Place(rt, new Vector2(0.5f, 1f), new Vector2(900, 400), new Vector2(0, -20), new Vector2(0.5f, 1f));
            UIFactory.VStack(rt, 10, null, TextAnchor.UpperCenter);
            var t = rt.gameObject.AddComponent<Toaster>();
            t._root = rt;
            return t;
        }

        public void Show(string text, Color accent, float seconds = 3f)
        {
            Image bg = UIFactory.Panel(_root, "Toast", new Color(0.05f, 0.07f, 0.1f, 0.88f), 16);
            UIFactory.Layout(bg, 70);
            Image bar = UIFactory.Image(bg.transform, "Bar", accent, UISprites.Rounded(4));
            bar.rectTransform.anchorMin = new Vector2(0, 0);
            bar.rectTransform.anchorMax = new Vector2(0, 1);
            bar.rectTransform.sizeDelta = new Vector2(8, -16);
            bar.rectTransform.anchoredPosition = new Vector2(14, 0);
            Text label = UIFactory.Label(bg.transform, "Text", text, 30, Theme.Text, TextAnchor.MiddleLeft);
            UIFactory.Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(36, 4);
            label.rectTransform.offsetMax = new Vector2(-16, -4);
            StartCoroutine(Life(bg, seconds));
        }

        private IEnumerator Life(Image bg, float seconds)
        {
            CanvasGroup g = UIAnim.Group(bg);
            g.alpha = 0f;
            yield return UIAnim.Fade(g, 1f, 0.2f);
            yield return new WaitForSecondsRealtime(seconds);
            yield return UIAnim.Fade(g, 0f, 0.4f);
            Destroy(bg.gameObject);
        }
    }

    /// <summary>Modal dialog with a message and up to two buttons.</summary>
    public static class Modal
    {
        public static GameObject Show(Transform canvasRoot, string title, string message, string okText, Action onOk,
            string cancelText = null, Action onCancel = null)
        {
            Image dim = UIFactory.Image(canvasRoot, "Modal", new Color(0, 0, 0, 0.62f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            Image panel = UIFactory.Panel(dim.transform, "Panel");
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(980, 460));
            Text t = UIFactory.Label(panel.transform, "Title", title, 46, Theme.AccentBright, TextAnchor.UpperCenter, UIFactory.Display);
            UIFactory.Place(t.rectTransform, new Vector2(0.5f, 1f), new Vector2(900, 80), new Vector2(0, -34), new Vector2(0.5f, 1f));
            Text m = UIFactory.Label(panel.transform, "Message", message, 32, Theme.Text, TextAnchor.MiddleCenter);
            UIFactory.Place(m.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(880, 200), new Vector2(0, 20));
            GameObject root = dim.gameObject;
            AnimatedButton ok = UIFactory.Button(panel.transform, "Ok", okText, () =>
            {
                UnityEngine.Object.Destroy(root);
                onOk?.Invoke();
            }, 34, true, new Vector2(320, 88));
            if (cancelText != null)
            {
                UIFactory.Place(ok.Background.rectTransform, new Vector2(0.5f, 0f), new Vector2(320, 88), new Vector2(180, 40), new Vector2(0.5f, 0f));
                AnimatedButton cancel = UIFactory.Button(panel.transform, "Cancel", cancelText, () =>
                {
                    UnityEngine.Object.Destroy(root);
                    onCancel?.Invoke();
                }, 34, false, new Vector2(320, 88));
                UIFactory.Place(cancel.Background.rectTransform, new Vector2(0.5f, 0f), new Vector2(320, 88), new Vector2(-180, 40), new Vector2(0.5f, 0f));
            }
            else
            {
                UIFactory.Place(ok.Background.rectTransform, new Vector2(0.5f, 0f), new Vector2(320, 88), new Vector2(0, 40), new Vector2(0.5f, 0f));
            }
            return root;
        }
    }
}
