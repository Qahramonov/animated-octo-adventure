using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Survival.Client
{
    /// <summary>
    /// Applies graphics presets (Low / Medium / High / Custom) and the FPS limit.
    /// Each preset maps to a Quality level with its own URP asset (shadow resolution, HDR, MSAA);
    /// custom values (render scale, shadow distance, view distance, vegetation) are applied on top.
    /// </summary>
    public static class QualityManager
    {
        public static float ViewDistance { get; private set; } = 1100f;
        public static float VegetationDensity { get; private set; } = 0.75f;
        public static float VegetationDistance { get; private set; } = 220f;
        public static bool Shadows { get; private set; } = true;
        public static bool PostProcessing { get; private set; }

        public static event System.Action Changed;

        public static void Apply(GameSettings s)
        {
            if (s.Preset != QualityPreset.Custom) s.ApplyPreset(s.Preset);

            int level = s.Preset == QualityPreset.Low ? 0 : s.Preset == QualityPreset.High ? 2 : 1;
            if (s.Preset == QualityPreset.Custom) level = s.Shadows && s.ShadowDistance > 60f ? 2 : s.Shadows ? 1 : 0;
            level = Mathf.Clamp(level, 0, QualitySettings.names.Length - 1);
            if (QualitySettings.GetQualityLevel() != level) QualitySettings.SetQualityLevel(level, true);

            var urp = UniversalRenderPipeline.asset;
            if (urp != null)
            {
                urp.renderScale = Mathf.Clamp(s.RenderScale, 0.5f, 1f);
                urp.shadowDistance = s.Shadows ? Mathf.Max(10f, s.ShadowDistance) : 0f;
            }
            QualitySettings.shadowDistance = s.Shadows ? s.ShadowDistance : 0f;

            ViewDistance = s.ViewDistance;
            VegetationDensity = s.VegetationDensity;
            VegetationDistance = s.VegetationDistance;
            Shadows = s.Shadows;
            PostProcessing = s.PostProcessing;
            ApplyFrameRate(s.TargetFps);
            Changed?.Invoke();
        }

        public static void ApplyFrameRate(int fps)
        {
            QualitySettings.vSyncCount = 0;
            int max = MaxSupportedFps();
            Application.targetFrameRate = Mathf.Min(fps, max);
        }

        /// <summary>Highest of 30/45/60 that the display can show.</summary>
        public static int MaxSupportedFps()
        {
            double hz = Screen.currentResolution.refreshRateRatio.value;
            if (hz <= 1) return 60;
            if (hz >= 59) return 60;
            if (hz >= 44) return 45;
            return 30;
        }

        /// <summary>Configures a camera according to the current quality settings.</summary>
        public static void SetupCamera(Camera cam, bool allowPost = true)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;
            data.renderShadows = Shadows;
            data.renderPostProcessing = allowPost && PostProcessing;
            data.antialiasing = AntialiasingMode.None;
        }
    }
}
