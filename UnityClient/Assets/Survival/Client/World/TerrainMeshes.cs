using System;
using Survival.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Survival.Client.WorldView
{
    /// <summary>Raw vertex data for one terrain chunk, built off the main thread.</summary>
    public sealed class ChunkMeshData
    {
        public int Cx, Cz, Lod;
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Color32[] Colors;
        public int Res; // quads per side
        public Bounds Bounds;
    }

    /// <summary>
    /// Builds terrain meshes from the deterministic height field. Chunks are 128 m (64 cells) with
    /// LOD steps of 1/2/4/8 cells, vertical skirts hide cracks between LODs, vertex colours encode
    /// biome (sand, grass, forest floor, rock, snow). Thread-safe (pure data) except
    /// <see cref="ToMesh"/> which must run on the main thread.
    /// </summary>
    public static class TerrainMeshes
    {
        public const int ChunkCells = HeightField.ChunkCells;
        public const float ChunkSize = ChunkCells * WorldConfig.CellSize;
        public const float SkirtDepth = 6f;

        private static readonly ushort[][] IndexCache = new ushort[5][];

        public static int StepForLod(int lod) => 1 << lod;

        public static ChunkMeshData Build(WorldRuntime world, int cx, int cz, int lod)
        {
            HeightField hf = world.Heights;
            if (lod == 0) hf.EnsureChunk(cx, cz);
            int step = StepForLod(lod);
            int res = ChunkCells / step;
            int n = res + 1;
            int skirt = n * 4;
            var pos = new Vector3[n * n + skirt];
            var nrm = new Vector3[pos.Length];
            var col = new Color32[pos.Length];
            int baseX = cx * ChunkCells, baseZ = cz * ChunkCells;
            float min = float.MaxValue, max = float.MinValue;
            IslandGenerator gen = world.Generator;
            float sea = world.Config.SeaLevel;

            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int ix = baseX + x * step, iz = baseZ + z * step;
                    float h = hf.GetSample(ix, iz);
                    float wx = ix * WorldConfig.CellSize, wz = iz * WorldConfig.CellSize;
                    int i = z * n + x;
                    pos[i] = new Vector3(x * step * WorldConfig.CellSize, h, z * step * WorldConfig.CellSize);
                    float hl = hf.GetSample(ix - step, iz), hr = hf.GetSample(ix + step, iz);
                    float hd = hf.GetSample(ix, iz - step), hu = hf.GetSample(ix, iz + step);
                    Vector3 normal = new Vector3(hl - hr, 2f * step * WorldConfig.CellSize, hd - hu).normalized;
                    nrm[i] = normal;
                    col[i] = BiomeColor(gen, wx, wz, h, normal.y, sea);
                    if (h < min) min = h;
                    if (h > max) max = h;
                }
            }

            // Skirts: duplicate the border vertices pushed down
            int k = n * n;
            for (int e = 0; e < 4; e++)
            {
                for (int j = 0; j < n; j++)
                {
                    int src = e == 0 ? j : e == 1 ? (n - 1) * n + j : e == 2 ? j * n : j * n + n - 1;
                    pos[k] = pos[src] - new Vector3(0, SkirtDepth, 0);
                    nrm[k] = nrm[src];
                    col[k] = col[src];
                    k++;
                }
            }

            Vector3 origin = new Vector3(baseX * WorldConfig.CellSize, 0, baseZ * WorldConfig.CellSize);
            return new ChunkMeshData
            {
                Cx = cx,
                Cz = cz,
                Lod = lod,
                Positions = pos,
                Normals = nrm,
                Colors = col,
                Res = res,
                Bounds = new Bounds(origin + new Vector3(ChunkSize * 0.5f, (min + max) * 0.5f - SkirtDepth * 0.5f, ChunkSize * 0.5f),
                    new Vector3(ChunkSize, max - min + SkirtDepth + 1f, ChunkSize)),
            };
        }

        public static Color32 BiomeColor(IslandGenerator gen, float wx, float wz, float h, float ny, float sea)
        {
            float alt = h - sea;
            float moist = gen.Moisture(wx, wz);
            float variation = (Hash(wx * 0.37f, wz * 0.41f) - 0.5f) * 0.08f;
            Color c;
            if (alt < -0.3f)
            {
                c = Color.Lerp(new Color(0.76f, 0.70f, 0.52f), new Color(0.35f, 0.40f, 0.36f), Mathf.Clamp01(-alt / 12f));
            }
            else
            {
                Color sand = new Color(0.86f, 0.79f, 0.58f);
                Color grass = Color.Lerp(new Color(0.46f, 0.58f, 0.25f), new Color(0.30f, 0.48f, 0.20f), moist);
                Color forest = new Color(0.24f, 0.33f, 0.16f);
                Color rock = new Color(0.47f, 0.45f, 0.42f);
                Color snow = new Color(0.92f, 0.93f, 0.96f);
                float beach = 1f - Mathf.Clamp01((alt - 1.2f) / 2.2f);
                float forestK = Mathf.Clamp01((gen.ForestDensity(wx, wz) - 0.2f) * 2f);
                c = Color.Lerp(grass, forest, forestK * 0.85f);
                c = Color.Lerp(c, sand, beach);
                float rocky = Mathf.Clamp01((0.82f - ny) / 0.12f) + Mathf.Clamp01((alt - 160f) / 60f) * 0.6f;
                c = Color.Lerp(c, rock, Mathf.Clamp01(rocky));
                float snowK = Mathf.Clamp01((alt - 235f) / 25f) * Mathf.Clamp01((ny - 0.7f) / 0.15f);
                c = Color.Lerp(c, snow, snowK);
            }
            c *= 1f + variation;
            return c;
        }

        private static float Hash(float x, float y)
        {
            float h = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>Shared index buffer per LOD (same topology for every chunk).</summary>
        public static ushort[] Indices(int lod)
        {
            ushort[] cached = IndexCache[lod];
            if (cached != null) return cached;
            int res = ChunkCells / StepForLod(lod);
            int n = res + 1;
            var list = new System.Collections.Generic.List<ushort>(res * res * 6 + res * 4 * 6);
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i00 = z * n + x, i10 = i00 + 1, i01 = i00 + n, i11 = i01 + 1;
                    // Same diagonal (00-11) as HeightField.GetHeight so feet match the visible ground
                    list.Add((ushort)i00); list.Add((ushort)i01); list.Add((ushort)i11);
                    list.Add((ushort)i00); list.Add((ushort)i11); list.Add((ushort)i10);
                }
            }
            int sk = n * n;
            // Edge 0: z = 0 row (faces -Z), edge 1: z = n-1 (faces +Z), edge 2: x = 0 (faces -X), edge 3: x = n-1 (faces +X)
            for (int j = 0; j < res; j++)
            {
                AddSkirt(list, j, j + 1, sk + j, sk + j + 1, false);
                AddSkirt(list, (n - 1) * n + j, (n - 1) * n + j + 1, sk + n + j, sk + n + j + 1, true);
                AddSkirt(list, j * n, (j + 1) * n, sk + 2 * n + j, sk + 2 * n + j + 1, true);
                AddSkirt(list, j * n + n - 1, (j + 1) * n + n - 1, sk + 3 * n + j, sk + 3 * n + j + 1, false);
            }
            cached = list.ToArray();
            IndexCache[lod] = cached;
            return cached;
        }

        private static void AddSkirt(System.Collections.Generic.List<ushort> l, int a, int b, int sa, int sb, bool flip)
        {
            if (!flip)
            {
                l.Add((ushort)a); l.Add((ushort)sb); l.Add((ushort)sa);
                l.Add((ushort)a); l.Add((ushort)b); l.Add((ushort)sb);
            }
            else
            {
                l.Add((ushort)a); l.Add((ushort)sa); l.Add((ushort)sb);
                l.Add((ushort)a); l.Add((ushort)sb); l.Add((ushort)b);
            }
        }

        public static Mesh ToMesh(ChunkMeshData d, Mesh reuse = null)
        {
            Mesh m = reuse != null ? reuse : new Mesh();
            m.Clear();
            m.name = $"chunk_{d.Cx}_{d.Cz}_lod{d.Lod}";
            m.indexFormat = IndexFormat.UInt16;
            m.SetVertices(d.Positions);
            m.SetNormals(d.Normals);
            m.SetColors(d.Colors);
            ushort[] idx = Indices(d.Lod);
            m.SetIndices(idx, MeshTopology.Triangles, 0, false);
            Vector3 origin = new Vector3(d.Cx * ChunkSize, 0, d.Cz * ChunkSize);
            m.bounds = new Bounds(d.Bounds.center - origin, d.Bounds.size);
            return m;
        }

        /// <summary>Vertex data of the whole-island low-res mesh (thread-safe to compute).</summary>
        public sealed class FarIslandData
        {
            public Vector3[] Positions;
            public Vector3[] Normals;
            public Color32[] Colors;
            public int N;
        }

        /// <summary>
        /// Whole-island low-res terrain (default 40 m spacing) shown beyond the streaming radius and
        /// in the lobby. Pure maths: run it on a worker thread, then call <see cref="ToMesh(FarIslandData)"/>.
        /// </summary>
        public static FarIslandData ComputeFarIsland(WorldRuntime world, float spacing, float lowerBy = 0.4f)
        {
            int n = Mathf.CeilToInt(world.Config.Size / spacing) + 1;
            var pos = new Vector3[n * n];
            var nrm = new Vector3[n * n];
            var col = new Color32[n * n];
            IslandGenerator gen = world.Generator;
            HeightField hf = world.Heights;
            float sea = world.Config.SeaLevel;
            int stepCells = Mathf.Max(1, Mathf.RoundToInt(spacing / WorldConfig.CellSize));
            var heights = new float[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    heights[z * n + x] = hf.GetSample(x * stepCells, z * stepCells);
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    float h = heights[i];
                    float wx = x * stepCells * WorldConfig.CellSize, wz = z * stepCells * WorldConfig.CellSize;
                    pos[i] = new Vector3(wx, h - lowerBy, wz);
                    float hl = heights[z * n + Math.Max(0, x - 1)], hr = heights[z * n + Math.Min(n - 1, x + 1)];
                    float hd = heights[Math.Max(0, z - 1) * n + x], hu = heights[Math.Min(n - 1, z + 1) * n + x];
                    Vector3 normal = new Vector3(hl - hr, 2f * spacing, hd - hu).normalized;
                    nrm[i] = normal;
                    col[i] = BiomeColor(gen, wx, wz, h, normal.y, sea);
                }
            }
            return new FarIslandData { Positions = pos, Normals = nrm, Colors = col, N = n };
        }

        public static Mesh ToMesh(FarIslandData d)
        {
            int n = d.N;
            var idx = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int z = 0; z < n - 1; z++)
            {
                for (int x = 0; x < n - 1; x++)
                {
                    int i00 = z * n + x, i10 = i00 + 1, i01 = i00 + n, i11 = i01 + 1;
                    idx[t++] = i00; idx[t++] = i01; idx[t++] = i11;
                    idx[t++] = i00; idx[t++] = i11; idx[t++] = i10;
                }
            }
            var m = new Mesh { name = "far_island", indexFormat = n * n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(d.Positions);
            m.SetNormals(d.Normals);
            m.SetColors(d.Colors);
            m.SetTriangles(idx, 0, true);
            return m;
        }
    }
}
