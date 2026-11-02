using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Survival.Client.Rendering
{
    /// <summary>
    /// Sun light, sky dome, ambient light and fog for a given time of day (0..1, 0.5 = noon) and
    /// storm factor. Drives the global shader values used by the sky, water and vegetation shaders.
    /// </summary>
    public sealed class SkyController : MonoBehaviour
    {
        private static readonly int SkyZenith = Shader.PropertyToID("_OrolSkyZenith");
        private static readonly int SkyHorizon = Shader.PropertyToID("_OrolSkyHorizon");
        private static readonly int SkyGround = Shader.PropertyToID("_OrolSkyGround");
        private static readonly int SunDir = Shader.PropertyToID("_OrolSunDir");
        private static readonly int SunColor = Shader.PropertyToID("_OrolSunColor");
        private static readonly int MoonDir = Shader.PropertyToID("_OrolMoonDir");
        private static readonly int Night = Shader.PropertyToID("_OrolNight");
        private static readonly int Storm = Shader.PropertyToID("_OrolStorm");
        private static readonly int WindStrength = Shader.PropertyToID("_OrolWindStrength");
        private static readonly int WindDir = Shader.PropertyToID("_OrolWindDir");

        private Light _sun;
        private Transform _dome;
        private Camera _camera;
        private Gradient _zenith, _horizon, _sunCol;

        public Light Sun => _sun;
        public float TimeOfDay { get; private set; } = 0.35f;
        public float StormFactor { get; set; }
        public float FogStart = 120f;
        public float FogEnd = 1100f;
        public Color HorizonColor { get; private set; }
        public float Daylight { get; private set; } = 1f;

        public static SkyController Create(Camera cam, float domeRadius)
        {
            var go = new GameObject("[Sky]");
            var sky = go.AddComponent<SkyController>();
            sky.Init(cam, domeRadius);
            return sky;
        }

        private void Init(Camera cam, float domeRadius)
        {
            _camera = cam;
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = QualityManager.Shadows ? LightShadows.Soft : LightShadows.None;
            _sun.shadowStrength = 0.85f;
            _sun.shadowBias = 0.06f;
            _sun.shadowNormalBias = 0.6f;
            RenderSettings.sun = _sun;

            Material skyMat = App.Materials.Sky();
            if (skyMat != null)
            {
                var dome = new GameObject("SkyDome");
                dome.transform.SetParent(transform, false);
                dome.AddComponent<MeshFilter>().sharedMesh = Models.SkyDome();
                var r = dome.AddComponent<MeshRenderer>();
                r.sharedMaterial = skyMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                dome.transform.localScale = Vector3.one * domeRadius;
                _dome = dome.transform;
            }

            _zenith = Grad((0f, new Color(0.01f, 0.02f, 0.05f)), (0.22f, new Color(0.03f, 0.05f, 0.12f)), (0.28f, new Color(0.22f, 0.25f, 0.45f)),
                (0.36f, new Color(0.22f, 0.42f, 0.75f)), (0.5f, new Color(0.18f, 0.42f, 0.82f)), (0.66f, new Color(0.24f, 0.42f, 0.74f)),
                (0.74f, new Color(0.28f, 0.22f, 0.45f)), (0.8f, new Color(0.03f, 0.05f, 0.12f)));
            _horizon = Grad((0f, new Color(0.03f, 0.05f, 0.09f)), (0.22f, new Color(0.08f, 0.09f, 0.15f)), (0.27f, new Color(0.95f, 0.55f, 0.32f)),
                (0.34f, new Color(0.78f, 0.82f, 0.88f)), (0.5f, new Color(0.72f, 0.84f, 0.95f)), (0.68f, new Color(0.85f, 0.78f, 0.7f)),
                (0.74f, new Color(0.98f, 0.5f, 0.25f)), (0.8f, new Color(0.08f, 0.09f, 0.15f)));
            _sunCol = Grad((0f, new Color(0.2f, 0.25f, 0.4f)), (0.24f, new Color(0.4f, 0.3f, 0.3f)), (0.28f, new Color(1f, 0.55f, 0.3f)),
                (0.36f, new Color(1f, 0.9f, 0.78f)), (0.5f, new Color(1f, 0.97f, 0.92f)), (0.66f, new Color(1f, 0.9f, 0.78f)),
                (0.73f, new Color(1f, 0.5f, 0.25f)), (0.78f, new Color(0.3f, 0.3f, 0.45f)));

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            SetTime(TimeOfDay);
        }

        private static Gradient Grad(params (float t, Color c)[] keys)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++) ck[i] = new GradientColorKey(keys[i].c, keys[i].t);
            g.SetKeys(ck, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return g;
        }

        private static Color Sample(Gradient g, float t)
        {
            // Mirror the second half of the day so the gradient only needs keys up to ~0.8
            return g.Evaluate(t > 0.8f ? 0.8f + (t - 0.8f) * 0f : t);
        }

        /// <summary>0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset.</summary>
        public void SetTime(float t01)
        {
            TimeOfDay = Mathf.Repeat(t01, 1f);
            float t = TimeOfDay;
            float sunAngle = (t - 0.25f) * 360f; // 0 deg at sunrise, 90 at noon
            Quaternion rot = Quaternion.Euler(sunAngle, -35f, 0f);
            Vector3 toSun = -(rot * Vector3.forward);
            float elevation = toSun.y;
            Daylight = Mathf.Clamp01((elevation + 0.08f) / 0.35f);
            float nightK = 1f - Daylight;
            float storm = Mathf.Clamp01(StormFactor);

            Color zenith = Sample(_zenith, t);
            Color horizon = Sample(_horizon, t);
            Color sunColor = Sample(_sunCol, t);
            Color grey = new Color(0.42f, 0.45f, 0.5f) * Mathf.Lerp(0.25f, 1f, Daylight);
            zenith = Color.Lerp(zenith, grey * 0.8f, storm * 0.85f);
            horizon = Color.Lerp(horizon, grey, storm * 0.85f);

            // Below the horizon the directional light becomes faint moonlight from the opposite side
            if (elevation < 0.02f)
            {
                _sun.transform.rotation = Quaternion.LookRotation(-new Vector3(-toSun.x, Mathf.Max(0.35f, -toSun.y), -toSun.z));
                _sun.color = new Color(0.45f, 0.55f, 0.8f);
                _sun.intensity = 0.18f * (1f - storm * 0.6f);
            }
            else
            {
                _sun.transform.rotation = rot;
                _sun.color = sunColor;
                _sun.intensity = Mathf.Lerp(0.25f, 1.25f, Daylight) * (1f - storm * 0.55f);
            }
            _sun.shadowStrength = Mathf.Lerp(0.85f, 0.35f, storm);

            RenderSettings.ambientSkyColor = Color.Lerp(zenith * 0.9f + new Color(0.03f, 0.03f, 0.05f), grey * 0.6f, storm * 0.5f);
            RenderSettings.ambientEquatorColor = horizon * 0.65f + new Color(0.02f, 0.02f, 0.03f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.14f, 0.11f) * Mathf.Lerp(0.25f, 1f, Daylight);
            RenderSettings.fogColor = horizon;
            RenderSettings.fogStartDistance = Mathf.Lerp(FogStart, FogStart * 0.25f, storm);
            RenderSettings.fogEndDistance = Mathf.Lerp(FogEnd, FogEnd * 0.45f, storm);
            HorizonColor = horizon;
            if (_camera != null) _camera.backgroundColor = horizon;

            Shader.SetGlobalColor(SkyZenith, zenith);
            Shader.SetGlobalColor(SkyHorizon, horizon);
            Shader.SetGlobalColor(SkyGround, new Color(0.1f, 0.12f, 0.14f) * Mathf.Lerp(0.3f, 1f, Daylight));
            Shader.SetGlobalVector(SunDir, toSun);
            Shader.SetGlobalVector(MoonDir, -toSun + new Vector3(0.2f, 0.4f, 0f));
            Shader.SetGlobalColor(SunColor, sunColor * Mathf.Lerp(0.6f, 1.2f, Daylight));
            Shader.SetGlobalFloat(Night, Mathf.SmoothStep(0f, 1f, nightK));
            Shader.SetGlobalFloat(Storm, storm);
            Shader.SetGlobalFloat(WindStrength, 0.3f + storm * 1.2f);
            Shader.SetGlobalVector(WindDir, new Vector4(0.8f, 0.6f, 0, 0));
        }

        private void LateUpdate()
        {
            if (_dome != null && _camera != null) _dome.position = _camera.transform.position;
        }
    }

    /// <summary>Global post-processing volume (bloom, ACES tonemapping, vignette, grading).</summary>
    public static class PostFx
    {
        public static Volume Create(Transform parent, float bloom, float vignette, float saturation = 8f, float contrast = 10f, float exposure = 0f)
        {
            var go = new GameObject("[PostFx]");
            if (parent != null) go.transform.SetParent(parent, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Bloom b = profile.Add<Bloom>(true);
            b.intensity.Override(bloom);
            b.threshold.Override(0.95f);
            b.scatter.Override(0.65f);
            Tonemapping tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            Vignette v = profile.Add<Vignette>(true);
            v.intensity.Override(vignette);
            v.smoothness.Override(0.45f);
            ColorAdjustments ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(saturation);
            ca.contrast.Override(contrast);
            ca.postExposure.Override(exposure);
            vol.sharedProfile = profile;
            return vol;
        }
    }

    public static class CameraRig
    {
        public static Camera CreateMain(string name, float far, float fov = 65f, bool allowPost = true)
        {
            var go = new GameObject(name);
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = far;
            cam.fieldOfView = fov;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            go.AddComponent<AudioListener>();
            QualityManager.SetupCamera(cam, allowPost);
            return cam;
        }
    }
}
