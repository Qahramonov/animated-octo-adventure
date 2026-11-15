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

        // ------------------------------------------------------------------ pictograms

        private static float Capsule(float x, float y, float ax, float ay, float bx, float by)
        {
            float px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01((px * dx + py * dy) / Mathf.Max(1e-5f, dx * dx + dy * dy));
            float cx = px - dx * t, cy = py - dy * t;
            return Mathf.Sqrt(cx * cx + cy * cy);
        }

        private static bool InPoly(float x, float y, float[] pts)
        {
            bool inside = false;
            int n = pts.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = pts[i * 2], yi = pts[i * 2 + 1], xj = pts[j * 2], yj = pts[j * 2 + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        private static readonly float[] BoltPoly = { 0.15f, 0.85f, -0.45f, -0.05f, -0.05f, -0.05f, -0.2f, -0.85f, 0.45f, 0.1f, 0.05f, 0.1f };
        private static readonly float[] BladePoly = { 0.05f, 0.62f, 0.5f, 0.72f, 0.72f, 0.3f, 0.45f, 0.05f, 0.22f, 0.3f };
        private static readonly float[] DropPoly = { 0f, 0.8f, -0.36f, 0.0f, 0.36f, 0.0f };
        private static readonly float[] FlamePoly = { 0f, 0.85f, -0.42f, 0.05f, -0.3f, -0.55f, 0.3f, -0.55f, 0.42f, 0.05f, 0.18f, 0.3f };

        /// <summary>Coordinates are -1..1 with +Y up. Returns true where the pictogram is solid.</summary>
        private static bool IconShape(string name, float x, float y)
        {
            const float t = 0.115f;
            switch (name)
            {
                case "jump":
                    return Capsule(x, y, -0.5f, 0.0f, 0f, 0.5f) < t || Capsule(x, y, 0f, 0.5f, 0.5f, 0.0f) < t
                        || Capsule(x, y, -0.5f, -0.5f, 0f, 0.0f) < t || Capsule(x, y, 0f, 0.0f, 0.5f, -0.5f) < t;
                case "crouch":
                    return IconShape("jump", x, -y);
                case "sprint":
                    return IconShape("jump", -y, x);
                case "attack":
                    return Capsule(x, y, -0.6f, -0.65f, 0.3f, 0.4f) < 0.1f || InPoly(x, y, BladePoly);
                case "bag":
                {
                    bool body = Mathf.Abs(x) < 0.56f && y > -0.7f && y < 0.2f && !(Mathf.Abs(x) > 0.5f && (y < -0.64f || y > 0.14f));
                    float r = Mathf.Sqrt(x * x + (y - 0.2f) * (y - 0.2f));
                    bool strap = y >= 0.2f && r < 0.4f && r > 0.26f;
                    bool flapGap = Mathf.Abs(y + 0.12f) < 0.045f && Mathf.Abs(x) < 0.56f;
                    bool buckle = Mathf.Abs(x) < 0.1f && y < -0.12f && y > -0.34f;
                    return (body && !flapGap && !buckle) || strap;
                }
                case "menu":
                    return Mathf.Abs(x) < 0.6f && (Mathf.Abs(y - 0.42f) < 0.1f || Mathf.Abs(y) < 0.1f || Mathf.Abs(y + 0.42f) < 0.1f);
                case "close":
                    return Capsule(x, y, -0.5f, -0.5f, 0.5f, 0.5f) < t || Capsule(x, y, -0.5f, 0.5f, 0.5f, -0.5f) < t;
                case "health":
                {
                    float hx = x * 1.25f, hy = y * 1.25f + 0.15f;
                    float a = hx * hx + hy * hy - 1f;
                    return a * a * a - hx * hx * hy * hy * hy < 0f;
                }
                case "water":
                    return (x * x + (y + 0.25f) * (y + 0.25f) < 0.44f * 0.44f) || InPoly(x, y - 0.05f, DropPoly);
                case "food":
                {
                    bool meat = (x - 0.18f) * (x - 0.18f) + (y - 0.18f) * (y - 0.18f) < 0.46f * 0.46f;
                    bool bone = Capsule(x, y, -0.1f, -0.1f, -0.52f, -0.52f) < 0.085f;
                    bool knob = (x + 0.66f) * (x + 0.66f) + (y + 0.46f) * (y + 0.46f) < 0.016f || (x + 0.46f) * (x + 0.46f) + (y + 0.66f) * (y + 0.66f) < 0.016f;
                    return meat || bone || knob;
                }
                case "stamina":
                    return InPoly(x, y, BoltPoly);
                case "fire":
                    return InPoly(x, y, FlamePoly) && !((x * x) + (y + 0.25f) * (y + 0.25f) < 0.03f);
                case "craft":
                    // hammer: head + handle
                    return Capsule(x, y, -0.55f, -0.6f, 0.15f, 0.2f) < 0.09f
                        || (Mathf.Abs((x - 0.25f) * 0.7071f + (y - 0.3f) * 0.7071f) < 0.2f && Mathf.Abs(-(x - 0.25f) * 0.7071f + (y - 0.3f) * 0.7071f) < 0.42f);
                default:
                    return x * x + y * y < 0.4f;
            }
        }

        /// <summary>White anti-aliased pictogram (tint with Image.color): jump, crouch, sprint, attack, bag, menu, close, health, water, food, stamina, fire, craft.</summary>
        public static Sprite Icon(string name, int size = 96)
        {
            string key = "icon_" + name + "_" + size;
            if (Cache.TryGetValue(key, out Sprite s)) return s;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            const int ss = 4;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float fx = ((x + (sx + 0.5f) / ss) / size) * 2f - 1f;
                            float fy = ((y + (sy + 0.5f) / ss) / size) * 2f - 1f;
                            if (IconShape(name, fx * 1.08f, fy * 1.08f)) hit++;
                        }
                    }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(hit * 255 / (ss * ss)));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = s;
            return s;
        }
    }
}
