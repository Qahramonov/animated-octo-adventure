using System.Runtime.CompilerServices;

namespace Survival.Core
{
    /// <summary>
    /// Deterministic 2D gradient noise. Uses integer hashing for gradients and only
    /// add/multiply/floor in double precision, so the server (x64 .NET) and the phone (ARM64 IL2CPP)
    /// produce the same values; world heights are additionally quantized to absorb rounding noise.
    /// </summary>
    public static class Noise
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Grad(uint h, double x, double y)
        {
            switch (h & 7u)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }

        /// <summary>Gradient noise in roughly [-1, 1].</summary>
        public static double Perlin(int seed, double x, double y)
        {
            int x0 = SMath.FloorToInt(x);
            int y0 = SMath.FloorToInt(y);
            double fx = x - x0;
            double fy = y - y0;
            double u = Fade(fx);
            double v = Fade(fy);

            double n00 = Grad(Hashing.Hash2(seed, x0, y0), fx, fy);
            double n10 = Grad(Hashing.Hash2(seed, x0 + 1, y0), fx - 1.0, fy);
            double n01 = Grad(Hashing.Hash2(seed, x0, y0 + 1), fx, fy - 1.0);
            double n11 = Grad(Hashing.Hash2(seed, x0 + 1, y0 + 1), fx - 1.0, fy - 1.0);

            double nx0 = n00 + (n10 - n00) * u;
            double nx1 = n01 + (n11 - n01) * u;
            return (nx0 + (nx1 - nx0) * v) * 0.95;
        }

        /// <summary>Fractal Brownian motion, normalized to roughly [-1, 1].</summary>
        public static double Fbm(int seed, double x, double y, int octaves, double lacunarity = 2.0, double gain = 0.5)
        {
            double sum = 0.0, amp = 1.0, norm = 0.0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin(seed + i * 1013, x, y) * amp;
                norm += amp;
                amp *= gain;
                x = x * lacunarity + 17.31;
                y = y * lacunarity - 9.73;
            }
            return sum / norm;
        }

        /// <summary>Ridged multifractal in [0, 1] (sharp mountain crests).</summary>
        public static double Ridged(int seed, double x, double y, int octaves, double lacunarity = 2.0, double gain = 0.5)
        {
            double sum = 0.0, amp = 1.0, norm = 0.0, weight = 1.0;
            for (int i = 0; i < octaves; i++)
            {
                double n = Perlin(seed + i * 7919, x, y);
                n = n < 0 ? -n : n;
                n = 1.0 - n;
                n *= n;
                n *= weight;
                weight = SMath.Clamp01(n * 1.6);
                sum += n * amp;
                norm += amp;
                amp *= gain;
                x = x * lacunarity + 31.7;
                y = y * lacunarity + 11.3;
            }
            return sum / norm;
        }
    }
}
