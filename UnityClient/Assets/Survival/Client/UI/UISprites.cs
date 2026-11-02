using System.Collections.Generic;
using UnityEngine;

namespace Survival.Client.UI
{
    /// <summary>
    /// Procedurally generated UI sprites (no image assets needed): anti-aliased rounded rectangles
    /// for 9-slicing, circles, rings, soft glows and gradients. Generated once and cached.
    /// </summary>
    public static class UISprites
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Rounded(int radius = 18, int border = 0)
        {
            string key = "r" + radius + "_" + border;
            if (Cache.TryGetValue(key, out Sprite s)) return s;
            int size = radius * 2 + 4;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float inner = radius - border;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedDistance(x + 0.5f, y + 0.5f, size, radius);
                    float a = Mathf.Clamp01(0.5f - d);
                    if (border > 0)
                    {
                        float ia = Mathf.Clamp01(0.5f - (d + border));
                        a = Mathf.Clamp01(a - ia);
                    }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            int b = radius + 1;
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            Cache[key] = s;
            return s;
        }

        private static float RoundedDistance(float x, float y, int size, float r)
        {
            float cx = Mathf.Clamp(x, r + 2, size - r - 2);
            float cy = Mathf.Clamp(y, r + 2, size - r - 2);
            float dx = x - cx, dy = y - cy;
            return Mathf.Sqrt(dx * dx + dy * dy) - r;
        }

        public static Sprite Circle(int size = 64) => Ring(size, 0f);

        /// <summary>Ring with thickness as a fraction of the radius (0 = filled circle).</summary>
        public static Sprite Ring(int size, float thickness)
        {
            string key = "c" + size + "_" + thickness;
            if (Cache.TryGetValue(key, out Sprite s)) return s;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float r = size * 0.5f - 1f;
            float inner = thickness > 0 ? r * (1f - thickness) : -1f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - size * 0.5f, dy = y + 0.5f - size * 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    if (inner > 0) a = Mathf.Min(a, Mathf.Clamp01(d - inner + 0.5f));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = s;
            return s;
        }

        /// <summary>Soft radial glow (gaussian-like falloff), also used as particle texture.</summary>
        public static Texture2D GlowTexture(int size = 64)
        {
            string key = "g" + size;
            if (Cache.TryGetValue(key, out Sprite s)) return s.texture;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    float a = Mathf.Pow(1f - d, 2.2f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = s;
            return tex;
        }

        public static Sprite Glow(int size = 64)
        {
            GlowTexture(size);
            return Cache["g" + size];
        }

        /// <summary>Vertical gradient strip (top colour -> bottom colour), for backgrounds/vignettes.</summary>
        public static Sprite Gradient(Color top, Color bottom, int height = 64)
        {
            string key = "v" + ColorUtility.ToHtmlStringRGBA(top) + ColorUtility.ToHtmlStringRGBA(bottom);
            if (Cache.TryGetValue(key, out Sprite s)) return s;
            var tex = NewTex(2, height);
            var px = new Color[2 * height];
            for (int y = 0; y < height; y++)
            {
                Color c = Color.Lerp(bottom, top, y / (float)(height - 1));
                px[y * 2] = c;
                px[y * 2 + 1] = c;
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, 2, height), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = s;
            return s;
        }

        /// <summary>Horizontal gradient (left -> right).</summary>
        public static Sprite GradientH(Color left, Color right, int width = 64)
        {
            string key = "h" + ColorUtility.ToHtmlStringRGBA(left) + ColorUtility.ToHtmlStringRGBA(right);
            if (Cache.TryGetValue(key, out Sprite s)) return s;
            var tex = NewTex(width, 2);
            var px = new Color[width * 2];
            for (int x = 0; x < width; x++)
            {
                Color c = Color.Lerp(left, right, x / (float)(width - 1));
                px[x] = c;
                px[width + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, width, 2), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = s;
            return s;
        }

        private static Texture2D NewTex(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
        }
    }
}
