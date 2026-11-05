using System.Collections.Generic;
using Survival.Core;
using UnityEngine;

namespace Survival.Client.Rendering
{
    /// <summary>
    /// Creates and caches materials for the custom OROL shaders. If a custom shader is missing or
    /// unsupported on the device (e.g. failed to compile), it falls back to URP's built-in particle
    /// shaders so the game never renders pink.
    /// </summary>
    public sealed class MaterialLibrary
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int GlossId = Shader.PropertyToID("_Gloss");
        private static readonly int SmoothId = Shader.PropertyToID("_Smoothness");
        private static readonly int RimId = Shader.PropertyToID("_Rim");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int EmissionId = Shader.PropertyToID("_Emission");
        private static readonly int WindId = Shader.PropertyToID("_Wind");
        private static readonly int DetailId = Shader.PropertyToID("_DetailStrength");
        private static readonly int DetailScaleId = Shader.PropertyToID("_DetailScale");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        private readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();
        private readonly Shader _lit;
        private readonly Shader _water;
        private readonly Shader _sky;
        private readonly Shader _additive;
        private readonly Material _fallbackLit;
        private readonly Material _fallbackAdditive;

        public bool UsingFallbackLit { get; }

        public MaterialLibrary()
        {
            _lit = Find("OROL/VertexLit", "Shaders/OrolVertexLit");
            _water = Find("OROL/Water", "Shaders/OrolWater");
            _sky = Find("OROL/Sky", "Shaders/OrolSky");
            _additive = Find("OROL/Additive", "Shaders/OrolAdditive");
            _fallbackLit = Resources.Load<Material>("Materials/Fallback_Lit");
            _fallbackAdditive = Resources.Load<Material>("Materials/Fallback_Additive");
            UsingFallbackLit = _lit == null;
            if (UsingFallbackLit) Log.Error("Render", "OROL/VertexLit unavailable - using URP fallback materials");
        }

        private static Shader Find(string name, string resourcePath)
        {
            Shader s = Shader.Find(name);
            if (s == null) s = Resources.Load<Shader>(resourcePath);
            if (s != null && !s.isSupported)
            {
                Log.Error("Render", $"Shader {name} is not supported on this device");
                s = null;
            }
            return s;
        }

        /// <summary>Vertex-colour lit material (terrain, trees, rocks, characters).</summary>
        public Material Lit(string key, Color tint, float gloss = 0f, float rim = 0f, float emission = 0f, float wind = 0f,
            float detail = 0f, float smoothness = 24f)
        {
            if (_cache.TryGetValue(key, out Material m) && m != null) return m;
            if (_lit != null)
            {
                m = new Material(_lit) { name = key, enableInstancing = false }; // SRP Batcher batches these; see Instanced()
                m.SetColor(ColorId, tint);
                m.SetFloat(GlossId, gloss);
                m.SetFloat(SmoothId, smoothness);
                m.SetFloat(RimId, rim);
                m.SetColor(RimColorId, new Color(1f, 0.9f, 0.8f));
                m.SetFloat(EmissionId, emission);
                m.SetFloat(WindId, wind);
                m.SetFloat(DetailId, detail);
                m.SetFloat(DetailScaleId, 0.35f);
            }
            else
            {
                m = _fallbackLit != null ? new Material(_fallbackLit) : new Material(Shader.Find("Universal Render Pipeline/Particles/Simple Lit"));
                m.name = key + "_fallback";
                m.SetColor(BaseColorId, tint);
            }
            _cache[key] = m;
            return m;
        }

        /// <summary>
        /// Instancing-enabled copy of a material, only for Graphics.RenderMeshInstanced (vegetation).
        /// Regular renderers use the non-instanced original: on GLES the instanced variants hit uniform
        /// limits / driver issues (the emulator rendered instanced lobby props as broken red geometry).
        /// </summary>
        public Material Instanced(Material source)
        {
            if (source == null) return null;
            string key = source.name + "#instanced";
            if (_cache.TryGetValue(key, out Material m) && m != null) return m;
            m = new Material(source) { name = key, enableInstancing = true };
            _cache[key] = m;
            return m;
        }

        public Material Water(string key)
        {
            if (_cache.TryGetValue(key, out Material m) && m != null) return m;
            if (_water != null)
            {
                m = new Material(_water) { name = key };
            }
            else
            {
                m = Lit(key + "_lit", new Color(0.1f, 0.35f, 0.5f), 0.6f);
            }
            _cache[key] = m;
            return m;
        }

        public Material Sky()
        {
            if (_cache.TryGetValue("sky", out Material m) && m != null) return m;
            m = _sky != null ? new Material(_sky) { name = "sky" } : null;
            _cache["sky"] = m;
            return m;
        }

        /// <summary>Additive glow material (particles, flares, fire, light sweeps).</summary>
        public Material Additive(string key, Texture tex, Color tint, float intensity = 1f)
        {
            if (_cache.TryGetValue(key, out Material m) && m != null) return m;
            if (_additive != null)
            {
                m = new Material(_additive) { name = key };
                m.SetTexture(MainTexId, tex);
                m.SetColor(ColorId, tint);
                m.SetFloat(IntensityId, intensity);
            }
            else
            {
                m = _fallbackAdditive != null ? new Material(_fallbackAdditive) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                m.name = key + "_fallback";
                m.SetTexture(BaseMapId, tex);
                m.SetColor(BaseColorId, tint * intensity);
            }
            _cache[key] = m;
            return m;
        }

        public static void SetTint(Material m, Color c)
        {
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
        }
    }
}
