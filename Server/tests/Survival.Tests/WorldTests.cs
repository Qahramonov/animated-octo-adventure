using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Survival.Core;
using Survival.World;
using Xunit;

namespace Survival.Tests
{
    public class WorldTests
    {
        private static readonly WorldConfig Cfg = new WorldConfig { Seed = 20261102, Size = 5000f, SeaLevel = 30f };

        [Fact]
        public void Noise_IsDeterministic_AndBounded()
        {
            for (int i = 0; i < 2000; i++)
            {
                double x = i * 13.37, y = i * -7.91;
                double a = Noise.Fbm(42, x / 100, y / 100, 5);
                double b = Noise.Fbm(42, x / 100, y / 100, 5);
                Assert.Equal(a, b);
                Assert.InRange(a, -1.2, 1.2);
                double r = Noise.Ridged(7, x / 50, y / 50, 4);
                Assert.InRange(r, 0.0, 1.0);
            }
        }

        [Fact]
        public void Island_HasLandInTheMiddle_AndOceanAtTheEdges()
        {
            var w = new WorldRuntime(Cfg);
            Assert.True(w.GroundHeight(2500, 2500) > Cfg.SeaLevel, "centre must be land");
            foreach (var p in new[] { new Vector2(30, 30), new Vector2(4970, 30), new Vector2(30, 4970), new Vector2(4970, 4970), new Vector2(2500, 15) })
            {
                Assert.True(w.GroundHeight(p.X, p.Y) < Cfg.SeaLevel - 3f, $"edge {p} must be ocean");
            }
        }

        [Fact]
        public void Island_HasBeachesHillsAndMountains()
        {
            var w = new WorldRuntime(Cfg);
            int beach = 0, high = 0, land = 0, samples = 0;
            float max = 0;
            for (int z = 0; z < 5000; z += 25)
            {
                for (int x = 0; x < 5000; x += 25)
                {
                    float h = w.GroundHeight(x, z);
                    samples++;
                    if (h > Cfg.SeaLevel) land++;
                    if (h > Cfg.SeaLevel && h < Cfg.SeaLevel + 3f) beach++;
                    if (h > Cfg.SeaLevel + 120f) high++;
                    if (h > max) max = h;
                }
            }
            float landFraction = land / (float)samples;
            Assert.InRange(landFraction, 0.35f, 0.75f);
            Assert.True(beach > 200, "beaches expected, got " + beach);
            Assert.True(high > 50, "mountains expected, got " + high);
            Assert.True(max - Cfg.SeaLevel > 150f, "peaks expected, max " + max);
        }

        [Fact]
        public void HeightField_CachedAndUncachedSamplesMatch_AcrossThreads()
        {
            var a = new WorldRuntime(Cfg);
            var b = new WorldRuntime(Cfg);
            Parallel.For(0, 16, i => a.Heights.EnsureChunk(18 + i % 4, 18 + i / 4));
            for (int i = 0; i < 5000; i++)
            {
                float x = 18 * 128 + (i * 37 % 512) + 0.37f;
                float z = 18 * 128 + (i * 91 % 512) + 0.71f;
                Assert.Equal(a.GroundHeight(x, z), b.GroundHeight(x, z));
            }
        }

        [Fact]
        public void NodeLayout_IsDeterministic_WithUniqueIds()
        {
            var a = new WorldRuntime(Cfg);
            var b = new WorldRuntime(Cfg);
            var ids = new HashSet<uint>();
            int trees = 0, ores = 0;
            for (int cz = 60; cz < 100; cz++)
            {
                for (int cx = 60; cx < 100; cx++)
                {
                    WorldNode[] na = a.Nodes.GetCell(cx, cz);
                    WorldNode[] nb = b.Nodes.GetCell(cx, cz);
                    Assert.Equal(na.Length, nb.Length);
                    for (int i = 0; i < na.Length; i++)
                    {
                        Assert.Equal(na[i].Id, nb[i].Id);
                        Assert.Equal(na[i].Kind, nb[i].Kind);
                        Assert.Equal(na[i].X, nb[i].X);
                        Assert.True(ids.Add(na[i].Id), "duplicate id");
                        Assert.True(a.Nodes.TryGetNode(na[i].Id, out WorldNode found));
                        Assert.Equal(na[i].Z, found.Z);
                        if (NodeKinds.IsTree(na[i].Kind)) trees++;
                        if (NodeKinds.IsOre(na[i].Kind)) ores++;
                        Assert.True(na[i].Y >= Cfg.SeaLevel + 0.8f, "nodes must not be under water");
                    }
                }
            }
            Assert.True(trees > 1000, "forest expected, trees=" + trees);
            Assert.True(ores > 20, "resource nodes expected, ores=" + ores);
        }

        [Fact]
        public void SpawnFinder_ReturnsSafeSeparatedPoints()
        {
            var w = new WorldRuntime(Cfg);
            var rng = new Rng(99);
            var taken = new List<Vector3>();
            for (int i = 0; i < 40; i++)
            {
                Assert.True(SpawnFinder.TryFind(w, ref rng, taken, null, out Vector3 p));
                Assert.True(p.Y > Cfg.SeaLevel + 1f, "spawn in water");
                Assert.True(w.Heights.GetSlopeDeg(p.X, p.Z) <= SpawnFinder.MaxSpawnSlopeDeg + 0.01f, "spawn on steep slope");
                Assert.False(w.OverlapsObstacle(p.X, p.Z, 1.0f), "spawn inside obstacle");
                foreach (Vector3 o in taken) Assert.True(SMath.DistanceXZ(o, p) >= SpawnFinder.MinSeparation, "spawn on top of another player");
                taken.Add(p);
            }
        }
    }
}

namespace Survival.Tests
{
    public class FingerprintTests
    {
        /// <summary>
        /// Pinned output of the island generator for the default seed. If this fails you changed
        /// terrain/vegetation generation: bump WorldConfig.CurrentGeneratorVersion (old game builds
        /// must not join servers running the new generator) and update the expected value.
        /// </summary>
        public const uint ExpectedDefaultFingerprint = 0xf94ef610;

        [Fact]
        public void DefaultIsland_FingerprintIsPinned()
        {
            uint fp = WorldFingerprint.Compute(new WorldRuntime(new WorldConfig()));
            Assert.True(fp == ExpectedDefaultFingerprint, $"Generator output changed: fingerprint is 0x{fp:x8}");
        }

        [Fact]
        public void Fingerprint_DependsOnSeed()
        {
            uint a = WorldFingerprint.Compute(new WorldRuntime(new WorldConfig { Seed = 1 }));
            uint b = WorldFingerprint.Compute(new WorldRuntime(new WorldConfig { Seed = 2 }));
            Assert.NotEqual(a, b);
        }
    }
}
