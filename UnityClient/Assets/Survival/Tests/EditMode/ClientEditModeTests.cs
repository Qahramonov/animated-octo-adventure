using System.Collections.Generic;
using NUnit.Framework;
using Survival.Client;
using Survival.Client.Rendering;
using Survival.Client.WorldView;
using Survival.Networking;
using Survival.World;
using UnityEngine;

namespace Survival.Tests
{
    /// <summary>
    /// Unity EditMode tests (Window > General > Test Runner, or game-ci/unity-test-runner in CI).
    /// They run the shared code under Unity's Mono runtime and check Unity-side pieces.
    /// </summary>
    public class ClientEditModeTests
    {
        [Test]
        public void WorldFingerprint_MatchesDotNetServer()
        {
            // Same pinned value as Server/tests (FingerprintTests): proves Unity and .NET generate the same island
            uint fp = WorldFingerprint.Compute(new WorldRuntime(new WorldConfig()));
            Assert.AreEqual(0xf94ef610u, fp, $"0x{fp:x8}");
        }

        [Test]
        public void Localization_HasAllLanguages()
        {
            foreach (string lang in new[] { "en", "uz", "ru" })
            {
                Loc.Language = lang;
                foreach (string key in new[] { "play", "servers", "settings", "profile", "inventory", "exit", "join", "offline_note" })
                {
                    string v = Loc.T(key);
                    Assert.IsFalse(string.IsNullOrEmpty(v));
                    Assert.AreNotEqual(key, v, $"missing '{key}' for {lang}");
                }
            }
            Loc.Language = "uz";
        }

        [Test]
        public void QualityPresets_AreOrdered()
        {
            var low = new GameSettings();
            low.ApplyPreset(QualityPreset.Low);
            var high = new GameSettings();
            high.ApplyPreset(QualityPreset.High);
            Assert.Less(low.ViewDistance, high.ViewDistance);
            Assert.Less(low.RenderScale, high.RenderScale);
            Assert.IsFalse(low.Shadows);
            Assert.IsTrue(high.Shadows);
        }

        [Test]
        public void MeshBuilder_FacesPointOutwards()
        {
            AssertOutwards(b => b.Box(Vector3.zero, new Vector3(1, 2, 1), Color.white), Vector3.zero);
            AssertOutwards(b => b.Blob(Vector3.zero, Vector3.one, Color.white, 1), Vector3.zero);
            AssertOutwards(b => b.Cylinder(new Vector3(0, -1, 0), 2f, 1f, 0.6f, 8, Color.white, true, true), Vector3.zero);
            AssertOutwards(b => b.Cylinder(new Vector3(0, -1, 0), 2f, 1f, 0f, 7, Color.white, false, true), Vector3.zero);
        }

        private static void AssertOutwards(System.Action<MeshBuilder> build, Vector3 center)
        {
            var b = new MeshBuilder();
            build(b);
            Mesh m = b.ToMesh("test", true, true);
            Vector3[] v = m.vertices;
            int[] t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], p = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 normal = Vector3.Cross(p - a, c - a);
                Vector3 centroid = (a + p + c) / 3f - center;
                Assert.Greater(Vector3.Dot(normal, centroid), -1e-5f, $"triangle {i / 3} faces inwards");
            }
            Object.DestroyImmediate(m);
        }

        [Test]
        public void TerrainChunk_HasExpectedTopology()
        {
            var world = new WorldRuntime(new WorldConfig());
            ChunkMeshData d = TerrainMeshes.Build(world, 19, 19, 0);
            int n = TerrainMeshes.ChunkCells + 1;
            Assert.AreEqual(n * n + n * 4, d.Positions.Length);
            ushort[] idx = TerrainMeshes.Indices(0);
            Assert.AreEqual(0, idx.Length % 3);
            foreach (ushort i in idx) Assert.Less(i, d.Positions.Length);
            // Upward-facing ground: first triangle normal must point up
            Vector3 a = d.Positions[idx[0]], b = d.Positions[idx[1]], c = d.Positions[idx[2]];
            Assert.Greater(Vector3.Cross(b - a, c - a).y, 0f);
        }

        [Test]
        public void Protocol_VersionIsPositive()
        {
            Assert.Greater(NetProtocol.Version, 0);
            Assert.AreEqual(7777, NetProtocol.DefaultPort);
        }
    }
}
