using System;
using System.Collections.Generic;
using UnityEngine;

namespace Survival.Client
{
    public enum QualityPreset
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Custom = 3,
    }

    /// <summary>Saved position/size of one on-screen control (normalized to the safe area).</summary>
    [Serializable]
    public sealed class HudLayoutEntry
    {
        public string Id;
        public float X;      // 0..1 of safe area width (anchor centre)
        public float Y;      // 0..1 of safe area height
        public float Scale = 1f;
    }

    /// <summary>
    /// All player-facing settings. Stored as JSON in PlayerPrefs (survives app updates).
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        private const string Key = "orol.settings.v1";

        public QualityPreset Preset = QualityPreset.Medium;
        public int TargetFps = 30;

        // Custom quality values (used when Preset == Custom, otherwise derived from the preset)
        public float RenderScale = 0.85f;
        public float ViewDistance = 1100f;
        public float ShadowDistance = 45f;
        public bool Shadows = true;
        public bool PostProcessing = false;
        public float VegetationDensity = 0.75f;
        public float VegetationDistance = 220f;

        // Controls
        public float LookSensitivity = 1f;
        public bool InvertY;
        public float JoystickSize = 1f;
        public float ButtonOpacity = 0.75f;
        public bool AutoSprintOnStickEdge = true;
        public List<HudLayoutEntry> HudLayout = new List<HudLayoutEntry>();

        // Audio
        public float MasterVolume = 0.9f;
        public float MusicVolume = 0.6f;
        public float SfxVolume = 0.9f;

        // General
        public string Language = "uz";
        public bool ShowFps = true;
        public string LastServerKey = string.Empty;
        public List<string> CustomServers = new List<string>();
        public int OfflineBotCount = 20;
        public bool IntroSeen;

        public static GameSettings Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(Key, string.Empty);
                if (!string.IsNullOrEmpty(json))
                {
                    var s = JsonUtility.FromJson<GameSettings>(json);
                    if (s != null)
                    {
                        s.Sanitize();
                        return s;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Settings reset (corrupt): " + e.Message);
            }
            var fresh = new GameSettings();
            fresh.ApplyPreset(DetectDefaultPreset());
            fresh.Language = DefaultLanguage();
            return fresh;
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        private void Sanitize()
        {
            TargetFps = TargetFps <= 30 ? 30 : TargetFps <= 45 ? 45 : 60;
            RenderScale = Mathf.Clamp(RenderScale, 0.5f, 1f);
            ViewDistance = Mathf.Clamp(ViewDistance, 400f, 2000f);
            ShadowDistance = Mathf.Clamp(ShadowDistance, 0f, 120f);
            VegetationDensity = Mathf.Clamp(VegetationDensity, 0.2f, 1f);
            VegetationDistance = Mathf.Clamp(VegetationDistance, 80f, 400f);
            LookSensitivity = Mathf.Clamp(LookSensitivity, 0.2f, 3f);
            JoystickSize = Mathf.Clamp(JoystickSize, 0.6f, 1.6f);
            ButtonOpacity = Mathf.Clamp(ButtonOpacity, 0.2f, 1f);
            OfflineBotCount = Mathf.Clamp(OfflineBotCount, 0, 90);
            if (HudLayout == null) HudLayout = new List<HudLayoutEntry>();
            if (CustomServers == null) CustomServers = new List<string>();
            if (Language != "uz" && Language != "en" && Language != "ru") Language = "en";
        }

        /// <summary>Copies the preset's values into the custom fields.</summary>
        public void ApplyPreset(QualityPreset p)
        {
            Preset = p;
            switch (p)
            {
                case QualityPreset.Low:
                    RenderScale = 0.7f; ViewDistance = 700f; Shadows = false; ShadowDistance = 0f;
                    PostProcessing = false; VegetationDensity = 0.45f; VegetationDistance = 140f;
                    break;
                case QualityPreset.Medium:
                    RenderScale = 0.85f; ViewDistance = 1100f; Shadows = true; ShadowDistance = 45f;
                    PostProcessing = false; VegetationDensity = 0.75f; VegetationDistance = 220f;
                    break;
                case QualityPreset.High:
                    RenderScale = 1f; ViewDistance = 1600f; Shadows = true; ShadowDistance = 90f;
                    PostProcessing = true; VegetationDensity = 1f; VegetationDistance = 320f;
                    break;
            }
        }

        /// <summary>Rough first-launch guess from device memory and GPU; the player can change it.</summary>
        public static QualityPreset DetectDefaultPreset()
        {
            int ram = SystemInfo.systemMemorySize;
            int vram = SystemInfo.graphicsMemorySize;
            if (Application.isEditor || (ram >= 7000 && vram >= 2000)) return QualityPreset.High;
            if (ram >= 3500) return QualityPreset.Medium;
            return QualityPreset.Low;
        }

        private static string DefaultLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Russian:
                case SystemLanguage.Ukrainian:
                case SystemLanguage.Belarusian:
                    return "ru";
                case SystemLanguage.English:
                    return "en";
                default:
                    return "uz";
            }
        }

        public HudLayoutEntry GetLayout(string id)
        {
            foreach (HudLayoutEntry e in HudLayout) if (e.Id == id) return e;
            return null;
        }

        public void SetLayout(string id, float x, float y, float scale)
        {
            HudLayoutEntry e = GetLayout(id);
            if (e == null)
            {
                e = new HudLayoutEntry { Id = id };
                HudLayout.Add(e);
            }
            e.X = x;
            e.Y = y;
            e.Scale = scale;
        }
    }
}
