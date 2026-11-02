using System;
using Survival.Core;

namespace Survival.World
{
    public enum Biome : byte
    {
        Ocean,
        Beach,
        Grassland,
        Forest,
        Rocky,
        Mountain,
        Snow,
    }

    /// <summary>
    /// Analytic, deterministic island height function: one large island surrounded by ocean with
    /// natural coastline (domain-warped radial mask), sandy beaches, coastal cliffs, rolling hills,
    /// plains and ridged mountain ranges. No towns or buildings by design.
    /// Rivers, lakes and cave entrances are added on top in Phase 2 (see docs/ROADMAP.md).
    /// </summary>
    public sealed class IslandGenerator
    {
        private readonly WorldConfig _cfg;
        private readonly int _seed;
        private readonly double _size;
        private readonly double _center;
        private readonly double _radius;
        private readonly double _sea;

        public IslandGenerator(WorldConfig cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _seed = cfg.Seed;
            _size = cfg.Size;
            _center = _size * 0.5;
            _radius = _size * 0.43;
            _sea = cfg.SeaLevel;
        }

        public WorldConfig Config => _cfg;
        public double Center => _center;
        public double IslandRadius => _radius;

        /// <summary>Normalized, coastline-warped distance from the island centre (~0.93 at the shore).</summary>
        public double IslandDistance(double x, double z)
        {
            double nx = (x - _center) / _radius;
            double nz = (z - _center) / _radius;
            double wx = Noise.Fbm(_seed + 11, x / 900.0, z / 900.0, 3) * 0.22;
            double wz = Noise.Fbm(_seed + 12, x / 900.0 + 40.0, z / 900.0 - 40.0, 3) * 0.22;
            double dx = nx + wx;
            double dz = nz + wz;
            double d = Math.Sqrt(dx * dx + dz * dz);
            d += Noise.Fbm(_seed + 13, x / 260.0, z / 260.0, 3) * 0.05;
            return d;
        }

        /// <summary>Raw (unquantized) terrain height in metres at world position (x, z).</summary>
        public double ComputeHeight(double x, double z)
        {
            double d = IslandDistance(x, z);

            // 1 = land, 0 = ocean; the shoreline sits around d ~ 0.93
            double land = SMath.SmoothStep(1.0, 0.86, d);
            // 0 at the coast, 1 in the interior
            double inland = SMath.SmoothStep(0.945, 0.6, d);

            // Plains vs hills
            double plains = SMath.SmoothStep(-0.15, 0.35, Noise.Fbm(_seed + 51, x / 1100.0, z / 1100.0, 2));
            double hills = 0.5 + 0.5 * Noise.Fbm(_seed + 21, x / 700.0, z / 700.0, 5);
            double detail = Noise.Fbm(_seed + 22, x / 120.0, z / 120.0, 3);

            // Mountain ranges: ridged noise masked by a low-frequency range mask, only inland
            double rangeMask = SMath.SmoothStep(-0.12, 0.32, Noise.Fbm(_seed + 31, x / 1500.0, z / 1500.0, 3)) * SMath.SmoothStep(0.9, 0.55, d);
            double ridge = Noise.Ridged(_seed + 32, x / 1000.0, z / 1000.0, 5);
            double mountain = ridge * Math.Sqrt(ridge) * rangeMask; // ridge^1.5 (sqrt is exact on every platform)

            double landH = _sea + 1.2
                           + inland * (6.0 + hills * hills * (32.0 + 58.0 * (1.0 - plains)))
                           + detail * 2.2 * inland
                           + mountain * 290.0;

            // Coastal cliffs on some stretches of the shore
            double cliffMask = SMath.SmoothStep(0.25, 0.55, Noise.Fbm(_seed + 41, x / 800.0, z / 800.0, 2));
            landH += cliffMask * SMath.SmoothStep(0.975, 0.885, d) * 16.0;

            // Ocean floor sloping away from the shore
            double depth = SMath.SmoothStep(0.86, 1.25, d);
            double oceanH = _sea - 1.5 - depth * 26.0 + detail * 0.8;

            double h = oceanH + (landH - oceanH) * land;
            return h < 0.0 ? 0.0 : h;
        }

        /// <summary>0..1 moisture used for forest density and grass colour.</summary>
        public float Moisture(double x, double z) =>
            (float)SMath.Clamp01(0.5 + 0.55 * Noise.Fbm(_seed + 61, x / 900.0, z / 900.0, 4));

        /// <summary>0..1 likelihood of forest at a position (before slope/height rules).</summary>
        public float ForestDensity(double x, double z)
        {
            float m = Moisture(x, z);
            float patches = (float)(0.5 + 0.5 * Noise.Fbm(_seed + 71, x / 260.0, z / 260.0, 3));
            return SMath.Clamp01((m - 0.38f) * 2.2f) * SMath.Clamp01(patches * 1.4f - 0.15f);
        }

        public Biome ClassifyBiome(float height, float slopeDeg, float moisture, float inlandDistance01)
        {
            float sea = _cfg.SeaLevel;
            if (height < sea - 0.4f) return Biome.Ocean;
            if (height < sea + 2.6f && inlandDistance01 > 0.86f && slopeDeg < 18f) return Biome.Beach;
            if (height > sea + 255f) return Biome.Snow;
            if (slopeDeg > 34f) return Biome.Rocky;
            if (height > sea + 140f) return Biome.Mountain;
            return moisture > 0.52f ? Biome.Forest : Biome.Grassland;
        }
    }
}
