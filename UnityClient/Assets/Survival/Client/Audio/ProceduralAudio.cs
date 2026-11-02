using UnityEngine;

namespace Survival.Client
{
    /// <summary>
    /// Tiny synthesizer: builds every sound of the game from oscillators, filtered noise and
    /// envelopes at startup. Original by construction (no samples), small APK footprint.
    /// </summary>
    public static class ProceduralAudio
    {
        public const int Rate = 22050;

        private static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static uint _noiseState = 0x9E3779B9u;

        private static float Noise()
        {
            _noiseState ^= _noiseState << 13;
            _noiseState ^= _noiseState >> 17;
            _noiseState ^= _noiseState << 5;
            return (_noiseState & 0xFFFFFF) / 8388607.5f - 1f;
        }

        private static float Env(float t, float attack, float decay, float length)
        {
            if (t < attack) return t / Mathf.Max(attack, 1e-4f);
            float r = (t - attack) / Mathf.Max(length - attack, 1e-4f);
            return Mathf.Exp(-r * decay);
        }

        /// <summary>Makes the end of a loop blend into its start (seamless loops).</summary>
        private static float[] LoopFade(float[] d, float seconds)
        {
            int n = Mathf.Min(d.Length / 2, (int)(seconds * Rate));
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)n;
                int end = d.Length - n + i;
                d[i] = d[i] * k + d[end] * (1f - k);
            }
            System.Array.Resize(ref d, d.Length - n);
            return d;
        }

        private static float[] Normalize(float[] d, float peak)
        {
            float m = 1e-5f;
            for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
            return d;
        }

        public static AudioClip Blip(string name, float freq, float length, float volume)
        {
            int n = (int)(length * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = freq * (1f - 0.25f * t / length);
                d[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Env(t, 0.002f, 5f, length) * volume;
            }
            return Make(name, d);
        }

        public static AudioClip Chime(string name, float[] freqs, float length, float volume)
        {
            int n = (int)(length * Rate);
            var d = new float[n];
            float step = length / freqs.Length;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                int idx = Mathf.Min(freqs.Length - 1, (int)(t / step));
                float lt = t - idx * step;
                float f = freqs[idx];
                float s = Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * f * t);
                d[i] = s * Env(lt, 0.004f, 4f, step) * volume * 0.7f;
            }
            return Make(name, d);
        }

        public static AudioClip Whoosh(string name, float length, float volume)
        {
            int n = (int)(length * Rate);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float k = Mathf.Sin(Mathf.PI * t / length);
                float cutoff = 0.05f + 0.3f * k;
                lp += (Noise() - lp) * cutoff;
                d[i] = lp * k * volume * 2.5f;
            }
            return Make(name, d);
        }

        public static AudioClip Boom(string name, float length)
        {
            int n = (int)(length * Rate);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = 38f + 40f * Mathf.Exp(-t * 6f);
                float sub = Mathf.Sin(2f * Mathf.PI * f * t) * Env(t, 0.005f, 3.2f, length);
                lp += (Noise() - lp) * 0.04f;
                float rumble = lp * Env(t, 0.01f, 5f, length) * 1.4f;
                d[i] = (sub + rumble) * 0.9f;
            }
            return Make(name, Normalize(d, 0.85f));
        }

        public static AudioClip Riser(string name, float length)
        {
            int n = (int)(length * Rate);
            var d = new float[n];
            float lp = 0f, phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float k = t / length;
                float cutoff = 0.01f + 0.25f * k * k;
                lp += (Noise() - lp) * cutoff;
                phase += 2f * Mathf.PI * (110f + 330f * k * k) / Rate;
                float tone = Mathf.Sin(phase) * 0.25f * k;
                d[i] = (lp * 1.5f + tone) * k * (1f - Mathf.Pow(k, 12f));
            }
            return Make(name, Normalize(d, 0.6f));
        }

        public static AudioClip Shimmer(string name, float length)
        {
            int n = (int)(length * Rate);
            var d = new float[n];
            float[] partials = { 1046.5f, 1318.5f, 1568f, 2093f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                for (int p = 0; p < partials.Length; p++)
                    s += Mathf.Sin(2f * Mathf.PI * partials[p] * t + p) * Env(t, 0.02f + p * 0.03f, 2.5f + p, length);
                d[i] = s * 0.2f;
            }
            return Make(name, Normalize(d, 0.45f));
        }

        public static AudioClip Footstep(string name, int seed, float bright)
        {
            _noiseState = 0x1234567u + (uint)seed * 7919u;
            float length = 0.16f;
            int n = (int)(length * Rate);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * (0.08f * bright);
                float thump = Mathf.Sin(2f * Mathf.PI * (70f + seed * 4f) * t) * Mathf.Exp(-t * 40f);
                d[i] = (lp * 2.2f + thump * 0.5f) * Env(t, 0.003f, 6f, length);
            }
            return Make(name, Normalize(d, 0.5f));
        }

        public static AudioClip Splash(string name, float length)
        {
            int n = (int)(length * Rate);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.35f;
                float bubbles = Mathf.Sin(2f * Mathf.PI * (600f + 400f * Mathf.Sin(t * 37f)) * t) * 0.15f;
                d[i] = (lp + bubbles) * Env(t, 0.01f, 4f, length);
            }
            return Make(name, Normalize(d, 0.55f));
        }

        public static AudioClip WindLoop(string name, float length)
        {
            int n = (int)(length * Rate);
            var d = new float[n];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float gust = 0.55f + 0.45f * Mathf.Sin(t * 0.9f) * Mathf.Sin(t * 0.37f + 1f);
                lp += (Noise() - lp) * (0.015f + 0.02f * gust);
                lp2 += (lp - lp2) * 0.2f;
                d[i] = lp2 * gust * 3f;
            }
            d = LoopFade(d, 1f);
            return Make(name, Normalize(d, 0.5f));
        }

        public static AudioClip OceanLoop(string name, float length)
        {
            int n = (int)(length * Rate);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float wave = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t / 4f), 2f);
                lp += (Noise() - lp) * (0.03f + 0.25f * wave);
                d[i] = lp * (0.25f + wave) * 1.6f;
            }
            d = LoopFade(d, 1f);
            return Make(name, Normalize(d, 0.5f));
        }

        public static AudioClip BirdsLoop(string name, float length)
        {
            int n = (int)(length * Rate);
            var d = new float[n];
            var rng = new System.Random(42);
            for (int call = 0; call < 9; call++)
            {
                float start = (float)rng.NextDouble() * (length - 1f);
                float baseF = 2200f + (float)rng.NextDouble() * 1800f;
                int chirps = 2 + rng.Next(4);
                for (int c = 0; c < chirps; c++)
                {
                    float cs = start + c * 0.13f;
                    float cl = 0.07f + (float)rng.NextDouble() * 0.05f;
                    int i0 = (int)(cs * Rate), i1 = Mathf.Min(n, (int)((cs + cl) * Rate));
                    for (int i = i0; i < i1; i++)
                    {
                        float t = (i - i0) / (float)Rate;
                        float f = baseF * (1f + 0.35f * Mathf.Sin(Mathf.PI * t / cl));
                        d[i] += Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Sin(Mathf.PI * t / cl) * 0.25f;
                    }
                }
            }
            return Make(name, Normalize(d, 0.35f));
        }

        public static AudioClip CricketsLoop(string name, float length)
        {
            int n = (int)(length * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float pulse = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 28f * t));
                float gate = Mathf.Sin(2f * Mathf.PI * 0.8f * t) > 0.2f ? 1f : 0f;
                d[i] = Mathf.Sin(2f * Mathf.PI * 4300f * t) * pulse * gate * 0.2f
                       + Mathf.Sin(2f * Mathf.PI * 3900f * t) * Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 23f * t + 1f)) * 0.08f;
            }
            d = LoopFade(d, 0.5f);
            return Make(name, Normalize(d, 0.25f));
        }

        public static AudioClip FireLoop(string name, float length)
        {
            int n = (int)(length * Rate);
            var d = new float[n];
            float lp = 0f;
            var rng = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                lp += (Noise() - lp) * 0.06f;
                float crackle = rng.NextDouble() < 0.0006 ? (float)(rng.NextDouble() * 2 - 1) : 0f;
                d[i] = lp * 1.2f + crackle;
            }
            d = LoopFade(d, 0.5f);
            return Make(name, Normalize(d, 0.4f));
        }
    }
}
