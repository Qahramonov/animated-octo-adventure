using System.Collections;
using Survival.Client.Rendering;
using Survival.Client.UI;
using Survival.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Survival.Client.Intro
{
    /// <summary>
    /// Cinematic intro (after the Unity splash): dark stage with drifting particles, the 3D OROL
    /// emblem spinning into place with a glow flash and light sweep, letter-by-letter title, and a
    /// real loading bar (audio synthesis, shader warm-up, model generation, lobby scene preload).
    /// Skippable; returning players get a shorter version.
    /// </summary>
    public sealed class IntroController : MonoBehaviour
    {
        private Camera _cam;
        private Transform _emblem;
        private Material _emblemMat;
        private Transform _halo;
        private Material _haloMat;
        private Transform _sweep;
        private Material _sweepMat;
        private ParticleSystem _dust;
        private ParticleSystem _sparks;
        private Text[] _letters;
        private Text _subtitle;
        private Text _tagline;
        private Image _barFill;
        private Image _barGlow;
        private Text _status;
        private Text _percent;
        private AnimatedButton _skip;
        private CanvasGroup _uiGroup;

        private float _timeScale = 1f;
        private bool _skipRequested;
        private float _loadProgress;
        private string _loadStatus = string.Empty;
        private bool _loadDone;
        private AsyncOperation _lobbyOp;

        private void Start()
        {
            bool returning = App.Settings.IntroSeen;
            _timeScale = returning ? 1.9f : 1f;
            BuildStage();
            BuildUi();
            App.Scenes.SetAlpha(1f);
            StartCoroutine(App.Scenes.FadeTo(0f, 0.6f));
            StartCoroutine(Load());
            StartCoroutine(Timeline());
        }

        // ------------------------------------------------------------------ stage

        private void BuildStage()
        {
            _cam = CameraRig.CreateMain("IntroCamera", 120f, 42f);
            _cam.backgroundColor = new Color(0.008f, 0.01f, 0.016f);
            _cam.transform.position = new Vector3(0.4f, 0.25f, -9.5f);
            if (QualityManager.PostProcessing || App.Settings.Preset != QualityPreset.Low)
            {
                QualityManager.SetupCamera(_cam, true);
                var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(_cam);
                data.renderPostProcessing = true;
                PostFx.Create(transform, 1.6f, 0.42f, 6f, 18f);
            }
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.08f, 0.09f, 0.12f);

            var keyLight = new GameObject("Key").AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, 0.86f, 0.72f);
            keyLight.intensity = 1.3f;
            keyLight.transform.rotation = Quaternion.Euler(25f, -30f, 0f);
            keyLight.shadows = LightShadows.None;

            _emblemMat = App.Materials.Lit("intro_emblem", Color.white, 0.9f, 1.4f, 0.0f, 0f, 0f, 48f);
            _emblemMat.SetColor("_RimColor", new Color(0.45f, 0.8f, 1f));
            var em = new GameObject("Emblem");
            em.AddComponent<MeshFilter>().sharedMesh = Models.Emblem();
            em.AddComponent<MeshRenderer>().sharedMaterial = _emblemMat;
            _emblem = em.transform;
            _emblem.position = new Vector3(0f, 0.55f, 0f);
            _emblem.localScale = Vector3.one * 1.25f;
            _emblem.rotation = Quaternion.Euler(0, 90f, 0);

            Texture2D glow = UISprites.GlowTexture(128);
            _haloMat = new Material(App.Materials.Additive("intro_halo", glow, new Color(1f, 0.55f, 0.2f), 0f));
            _halo = SpawnQuad("Halo", _haloMat, new Vector3(0, 0.55f, 0.4f), new Vector3(6.5f, 6.5f, 1));
            _sweepMat = new Material(App.Materials.Additive("intro_sweep", glow, new Color(0.7f, 0.9f, 1f), 0f));
            _sweep = SpawnQuad("Sweep", _sweepMat, new Vector3(-3f, 0.55f, -0.3f), new Vector3(0.6f, 4.5f, 1));
            _sweep.rotation = Quaternion.Euler(0, 0, -18f);

            _dust = Particles("Dust", glow, 260, new Vector3(14, 8, 10), new Color(0.6f, 0.75f, 1f, 0.35f), new Color(1f, 0.7f, 0.4f, 0.35f),
                0.03f, 0.09f, 0.12f, 6f, 10f);
            _sparks = Particles("Sparks", glow, 0, new Vector3(0.5f, 0.5f, 0.5f), new Color(1f, 0.7f, 0.3f, 1f), new Color(1f, 0.45f, 0.15f, 1f),
                0.03f, 0.07f, 4.5f, 0.6f, 1.6f);
            var sparkShape = _sparks.shape;
            sparkShape.shapeType = ParticleSystemShapeType.Sphere;
            sparkShape.radius = 0.9f;
            _sparks.transform.position = _emblem.position;
            var embers = Particles("Embers", glow, 60, new Vector3(10, 0.5f, 4), new Color(1f, 0.5f, 0.15f, 0.8f), new Color(1f, 0.3f, 0.1f, 0.6f),
                0.02f, 0.05f, 0.45f, 4f, 7f);
            embers.transform.position = new Vector3(0, -3.2f, 1f);
            var vel = embers.velocityOverLifetime;
            vel.enabled = true;
            vel.y = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        }

        private static Transform SpawnQuad(string name, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = Models.Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.position = pos;
            go.transform.localScale = scale;
            return go.transform;
        }

        private ParticleSystem Particles(string name, Texture2D tex, int rate, Vector3 box, Color a, Color b, float minSize, float maxSize,
            float speed, float minLife, float maxLife)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = rate > 0;
            var em = ps.emission;
            em.rateOverTime = rate / Mathf.Max(1f, (minLife + maxLife) * 0.5f) * 4f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.4f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = App.Materials.Additive("particles_glow", tex, Color.white, 1.4f);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ ui

        private void BuildUi()
        {
            Canvas canvas = UIFactory.Canvas("IntroUI", 10, transform);
            RectTransform safe = UIFactory.SafeRoot(canvas);
            _uiGroup = UIAnim.Group(safe);

            // Title letters
            string title = GameInfo.Title;
            var titleRoot = UIFactory.Rect(safe, "Title");
            UIFactory.Place(titleRoot, new Vector2(0.5f, 0.5f), new Vector2(900, 170), new Vector2(0, -190));
            _letters = new Text[title.Length];
            float spacing = 150f;
            for (int i = 0; i < title.Length; i++)
            {
                Text t = UIFactory.Label(titleRoot, "L" + i, title[i].ToString(), 150, Theme.Text, TextAnchor.MiddleCenter, UIFactory.Display);
                UIFactory.Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(170, 190), new Vector2((i - (title.Length - 1) * 0.5f) * spacing, 0));
                UIFactory.AddShadow(t, 0.8f, 4f);
                var o = t.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(1f, 0.55f, 0.2f, 0.35f);
                o.effectDistance = new Vector2(2f, -2f);
                t.color = t.color.WithAlpha(0f);
                _letters[i] = t;
            }
            _subtitle = UIFactory.Label(safe, "Subtitle", Spaced(GameInfo.Subtitle), 44, Theme.Accent, TextAnchor.MiddleCenter, UIFactory.Bold);
            UIFactory.Place(_subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900, 60), new Vector2(0, -300));
            _subtitle.color = _subtitle.color.WithAlpha(0f);
            _tagline = UIFactory.Label(safe, "Tagline", "an original open-world survival", 26, Theme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_tagline.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900, 40), new Vector2(0, -350));
            _tagline.color = _tagline.color.WithAlpha(0f);

            // Loading bar
            var bar = UIFactory.Rect(safe, "Loading");
            UIFactory.Place(bar, new Vector2(0.5f, 0f), new Vector2(760, 90), new Vector2(0, 70), new Vector2(0.5f, 0f));
            Image track = UIFactory.Image(bar, "Track", new Color(1, 1, 1, 0.08f), UISprites.Rounded(4));
            UIFactory.Place(track.rectTransform, new Vector2(0.5f, 0f), new Vector2(760, 6), new Vector2(0, 0), new Vector2(0.5f, 0f));
            _barFill = UIFactory.Image(track.transform, "Fill", Theme.Accent, UISprites.Rounded(4));
            UIFactory.Stretch(_barFill.rectTransform);
            _barFill.type = Image.Type.Filled;
            _barFill.fillMethod = Image.FillMethod.Horizontal;
            _barFill.fillAmount = 0f;
            _barGlow = UIFactory.Image(track.transform, "Glow", new Color(1f, 0.7f, 0.35f, 0.9f), UISprites.Glow(64));
            _barGlow.rectTransform.sizeDelta = new Vector2(60, 60);
            _barGlow.rectTransform.anchorMin = _barGlow.rectTransform.anchorMax = new Vector2(0, 0.5f);
            _status = UIFactory.Label(bar, "Status", Loc.T("loading") + "...", 24, Theme.TextDim, TextAnchor.LowerLeft);
            UIFactory.Place(_status.rectTransform, new Vector2(0f, 0f), new Vector2(600, 40), new Vector2(0, 18), new Vector2(0f, 0f));
            _percent = UIFactory.Label(bar, "Percent", "0%", 24, Theme.Text, TextAnchor.LowerRight, UIFactory.Bold);
            UIFactory.Place(_percent.rectTransform, new Vector2(1f, 0f), new Vector2(200, 40), new Vector2(0, 18), new Vector2(1f, 0f));

            Text version = UIFactory.Label(safe, "Version", $"v{GameInfo.Version} · {GameInfo.Phase}", 20, Theme.TextDim.WithAlpha(0.6f), TextAnchor.LowerLeft);
            UIFactory.Place(version.rectTransform, new Vector2(0f, 0f), new Vector2(700, 30), new Vector2(30, 20), new Vector2(0f, 0f));

            _skip = UIFactory.Button(safe, "Skip", Loc.T("skip") + "  ›", () => _skipRequested = true, 26, false, new Vector2(240, 66));
            UIFactory.Place(_skip.Background.rectTransform, new Vector2(1f, 1f), new Vector2(240, 66), new Vector2(-30, -30), new Vector2(1f, 1f));
            _skip.gameObject.SetActive(false);
        }

        private static string Spaced(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) sb.Append(c).Append(' ');
            return sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------ loading (real work)

        private IEnumerator Load()
        {
            yield return null;
            Step(0.05f, "Initializing");
            yield return null;

            Step(0.12f, "Synthesizing audio");
            yield return null;
            App.Audio.Build();
            App.Audio.Play(SfxSound.IntroRiser, 0.7f);

            Step(0.3f, "Generating models");
            yield return null;
            foreach (World.NodeKind k in new[] { World.NodeKind.TreePine, World.NodeKind.TreeBroadleaf, World.NodeKind.Bush, World.NodeKind.Boulder })
            {
                Models.Get(k, 0);
                Models.Get(k, 1);
                yield return null;
            }

            Step(0.45f, "Warming up shaders");
            yield return WarmupShaders();

            Step(0.55f, "Loading lobby");
            _lobbyOp = SceneManager.LoadSceneAsync(App.SceneLobby, LoadSceneMode.Single);
            _lobbyOp.allowSceneActivation = false;
            while (_lobbyOp.progress < 0.9f)
            {
                Step(0.55f + _lobbyOp.progress / 0.9f * 0.4f, "Loading lobby");
                yield return null;
            }
            Step(1f, "Ready");
            _loadDone = true;
        }

        private IEnumerator WarmupShaders()
        {
            // Render each material once in front of the camera so shader variants are compiled now,
            // not as a hitch when the lobby appears.
            Material[] mats =
            {
                App.Materials.Lit("warm_terrain", Color.white, 0f, 0f, 0f, 0f, 0.3f),
                App.Materials.Lit("warm_foliage", Color.white, 0f, 0f, 0f, 0.6f),
                App.Materials.Water("water"),
                App.Materials.Sky(),
            };
            var holders = new System.Collections.Generic.List<GameObject>();
            foreach (Material m in mats)
            {
                if (m == null) continue;
                var go = new GameObject("Warm_" + m.name);
                go.AddComponent<MeshFilter>().sharedMesh = Models.Quad();
                go.AddComponent<MeshRenderer>().sharedMaterial = m;
                go.transform.position = _cam.transform.position + _cam.transform.forward * 30f;
                go.transform.localScale = Vector3.one * 0.01f;
                holders.Add(go);
            }
            yield return null;
            yield return null;
            foreach (GameObject go in holders) Destroy(go);
        }

        private void Step(float p, string status)
        {
            _loadProgress = p;
            _loadStatus = status;
        }

        // ------------------------------------------------------------------ timeline

        private IEnumerator Timeline()
        {
            float t = 0f;
            bool flashed = false, lettersStarted = false, skipShown = false;
            float duration = 6.2f;
            Vector3 camStart = _cam.transform.position;
            Vector3 camEnd = new Vector3(0f, 0.35f, -5.2f);

            while (true)
            {
                float dt = Time.unscaledDeltaTime * (_skipRequested ? 6f : _timeScale);
                t += dt;

                // Camera dolly with subtle handheld motion
                float k = UIAnim.EaseInOut(Mathf.Clamp01(t / 4.5f));
                Vector3 shake = new Vector3(Mathf.PerlinNoise(t * 0.4f, 0) - 0.5f, Mathf.PerlinNoise(0, t * 0.4f) - 0.5f, 0) * 0.08f;
                _cam.transform.position = Vector3.Lerp(camStart, camEnd, k) + shake;
                _cam.transform.rotation = Quaternion.LookRotation(_emblem.position + new Vector3(0, -0.25f, 0) - _cam.transform.position) *
                                          Quaternion.Euler(0, 0, Mathf.Lerp(-4f, 0f, k));

                // Emblem spin-in (1.0 - 2.4 s)
                float spin = UIAnim.EaseOutBack(Mathf.InverseLerp(1.0f, 2.4f, t));
                float idle = Mathf.Sin(t * 0.8f) * 6f * Mathf.Clamp01(t - 2.4f);
                _emblem.rotation = Quaternion.Euler(Mathf.Sin(t * 0.6f) * 3f, Mathf.Lerp(90f, 0f, spin) + idle, 0);
                float scale = Mathf.Lerp(0.6f, 1.25f, UIAnim.EaseOutCubic(Mathf.InverseLerp(0.6f, 2.4f, t)));
                _emblem.localScale = Vector3.one * scale;
                float lit = Mathf.Clamp01(Mathf.InverseLerp(0.4f, 2.0f, t));
                MaterialLibrary.SetTint(_emblemMat, Color.Lerp(new Color(0.05f, 0.05f, 0.06f), Color.white, lit));

                // Flash + sparks at 2.2 s
                if (!flashed && t >= 2.2f)
                {
                    flashed = true;
                    _sparks.Emit(90);
                    App.Audio.Play(SfxSound.IntroBoom, 0.9f);
                    App.Audio.Play(SfxSound.IntroShimmer, 0.6f);
                }
                float flash = Mathf.Exp(-Mathf.Max(0f, t - 2.2f) * 3f) * (t >= 2.2f ? 1f : 0f);
                float pulse = 0.35f + 0.1f * Mathf.Sin(t * 2.2f);
                _haloMat.SetFloat("_Intensity", Mathf.Clamp01(lit) * pulse + flash * 2.5f);
                _emblemMat.SetFloat("_Emission", flash * 1.2f + 0.05f * lit);

                // Light sweep across the emblem (2.6 - 3.6 s)
                float sw = Mathf.InverseLerp(2.6f, 3.6f, t);
                _sweep.position = new Vector3(Mathf.Lerp(-3f, 3f, sw), 0.55f, -0.35f);
                _sweepMat.SetFloat("_Intensity", Mathf.Sin(sw * Mathf.PI) * 1.4f);

                // Title letters (2.4 s +)
                if (t >= 2.4f)
                {
                    if (!lettersStarted)
                    {
                        lettersStarted = true;
                        App.Audio.PlayUi(UiSound.Whoosh);
                    }
                    for (int i = 0; i < _letters.Length; i++)
                    {
                        float lt = Mathf.Clamp01((t - 2.4f - i * 0.12f) / 0.45f);
                        float e = UIAnim.EaseOutBack(lt);
                        _letters[i].color = _letters[i].color.WithAlpha(lt);
                        _letters[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, e);
                    }
                    float st = Mathf.Clamp01((t - 3.2f) / 0.6f);
                    _subtitle.color = _subtitle.color.WithAlpha(st);
                    _subtitle.rectTransform.localScale = new Vector3(Mathf.Lerp(1.25f, 1f, UIAnim.EaseOutCubic(st)), 1, 1);
                    _tagline.color = _tagline.color.WithAlpha(Mathf.Clamp01((t - 3.8f) / 0.6f));
                }

                if (!skipShown && t > 1.0f && !_skipRequested)
                {
                    skipShown = true;
                    _skip.gameObject.SetActive(true);
                }

                // Loading UI
                float shown = Mathf.MoveTowards(_barFill.fillAmount, _loadProgress, Time.unscaledDeltaTime * 0.9f);
                _barFill.fillAmount = shown;
                _barGlow.rectTransform.anchorMin = _barGlow.rectTransform.anchorMax = new Vector2(shown, 0.5f);
                _barGlow.color = _barGlow.color.WithAlpha(0.6f + 0.4f * Mathf.Sin(t * 6f));
                _percent.text = Mathf.RoundToInt(shown * 100f) + "%";
                _status.text = _loadStatus + "...";

                if (t >= duration && _loadDone && shown >= 0.999f) break;
                yield return null;
            }

            App.Settings.IntroSeen = true;
            App.Settings.Save();
            _skip.gameObject.SetActive(false);
            yield return App.Scenes.FadeTo(1f, 0.45f);
            _lobbyOp.allowSceneActivation = true;
        }
    }
}
