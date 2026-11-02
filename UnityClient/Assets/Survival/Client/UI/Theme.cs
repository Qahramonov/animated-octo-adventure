using UnityEngine;

namespace Survival.Client.UI
{
    /// <summary>OROL visual identity: dark glass panels, warm ember accent, teal secondary.</summary>
    public static class Theme
    {
        public static readonly Color Accent = new Color(1f, 0.56f, 0.18f);          // ember orange
        public static readonly Color AccentBright = new Color(1f, 0.74f, 0.38f);
        public static readonly Color Secondary = new Color(0.27f, 0.82f, 0.78f);    // sea teal
        public static readonly Color Text = new Color(0.94f, 0.95f, 0.97f);
        public static readonly Color TextDim = new Color(0.68f, 0.72f, 0.78f);
        public static readonly Color Panel = new Color(0.06f, 0.08f, 0.11f, 0.78f);
        public static readonly Color PanelLight = new Color(0.14f, 0.17f, 0.22f, 0.82f);
        public static readonly Color PanelBorder = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color Button = new Color(0.11f, 0.14f, 0.19f, 0.86f);
        public static readonly Color ButtonHover = new Color(0.18f, 0.22f, 0.29f, 0.92f);
        public static readonly Color Good = new Color(0.36f, 0.86f, 0.42f);
        public static readonly Color Warn = new Color(1f, 0.78f, 0.25f);
        public static readonly Color Bad = new Color(1f, 0.33f, 0.30f);
        public static readonly Color Neutral = new Color(0.55f, 0.58f, 0.62f);
        public static readonly Color Health = new Color(0.90f, 0.26f, 0.26f);
        public static readonly Color Hunger = new Color(0.95f, 0.62f, 0.20f);
        public static readonly Color Thirst = new Color(0.26f, 0.62f, 0.98f);
        public static readonly Color Stamina = new Color(0.95f, 0.90f, 0.40f);

        public static Color PingColor(int ms)
        {
            if (ms < 0) return Neutral;
            if (ms < 80) return Good;
            if (ms < 160) return Warn;
            return Bad;
        }

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
