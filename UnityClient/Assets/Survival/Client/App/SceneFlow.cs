using System;
using System.Collections;
using Survival.Client.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Survival.Client
{
    /// <summary>Smooth scene transitions: fade to black, async load, fade back in.</summary>
    public sealed class SceneFlow : MonoBehaviour
    {
        private Canvas _canvas;
        private Image _fade;
        private bool _busy;

        public bool IsBusy => _busy;
        public float FadeAlpha => _fade != null ? _fade.color.a : 0f;

        private void Awake()
        {
            var go = new GameObject("[Fade]");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;
            _fade = UIFactory.Image(go.transform, "Fade", new Color(0, 0, 0, 0));
            UIFactory.Stretch(_fade.rectTransform);
            _fade.raycastTarget = false;
        }

        public void Load(string scene, float fadeOut = 0.35f, float fadeIn = 0.45f, Action onLoaded = null)
        {
            if (_busy) return;
            StartCoroutine(LoadRoutine(scene, fadeOut, fadeIn, onLoaded));
        }

        /// <summary>Activates an already preloaded scene (used by the intro).</summary>
        public IEnumerator FadeTo(float alpha, float duration)
        {
            float start = _fade.color.a;
            _fade.raycastTarget = alpha > 0.01f;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                SetAlpha(Mathf.Lerp(start, alpha, Mathf.SmoothStep(0, 1, t / duration)));
                yield return null;
            }
            SetAlpha(alpha);
        }

        public void SetAlpha(float a)
        {
            _fade.color = new Color(0, 0, 0, a);
            _fade.raycastTarget = a > 0.01f;
        }

        private IEnumerator LoadRoutine(string scene, float fadeOut, float fadeIn, Action onLoaded)
        {
            _busy = true;
            yield return FadeTo(1f, fadeOut);
            AsyncOperation op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            while (!op.isDone) yield return null;
            // Release meshes/materials of the previous scene before the new one fills memory
            yield return Resources.UnloadUnusedAssets();
            GC.Collect();
            onLoaded?.Invoke();
            _busy = false;
            yield return FadeTo(0f, fadeIn);
        }
    }
}
