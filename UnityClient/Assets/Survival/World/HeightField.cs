using System;
using System.Numerics;
using System.Threading;
using Survival.Core;

namespace Survival.World
{
    /// <summary>
    /// Quantized height samples on a 2 m grid, generated lazily in 64x64 chunks and cached.
    /// Thread-safe: chunks are generated off-thread by the client mesh builder and read by the
    /// game thread; the server shares one instance between all simulations.
    ///
    /// Heights are quantized to 1/64 m so tiny floating point differences between platforms
    /// cannot change gameplay-relevant results.
    /// </summary>
    public sealed class HeightField
    {
        public const int ChunkCells = 64;
        public const float QuantScale = 64f;
        public const float InvQuantScale = 1f / 64f;

        private readonly IslandGenerator _gen;
        private readonly int _chunksPerSide;
        private readonly ushort[][] _chunks;
        private readonly float _seaLevel;
        private int _generatedChunks;

        public HeightField(IslandGenerator generator)
        {
            _gen = generator ?? throw new ArgumentNullException(nameof(generator));
            SamplesPerSide = (int)Math.Round(generator.Config.Size / WorldConfig.CellSize) + 1;
            _chunksPerSide = (SamplesPerSide + ChunkCells - 1) / ChunkCells;
            _chunks = new ushort[_chunksPerSide * _chunksPerSide][];
            _seaLevel = generator.Config.SeaLevel;
        }

        public IslandGenerator Generator => _gen;
        public int SamplesPerSide { get; }
        public int ChunksPerSide => _chunksPerSide;
        public float WorldSize => _gen.Config.Size;
        public float SeaLevel => _seaLevel;
        public int GeneratedChunkCount => Volatile.Read(ref _generatedChunks);

        /// <summary>Quantizes a raw height exactly like the cache does.</summary>
        public static ushort Quantize(double h)
        {
            double q = h * QuantScale + 0.5;
            if (q <= 0) return 0;
            if (q >= 65535) return 65535;
            return (ushort)SMath.FloorToInt(q);
        }

        public bool IsChunkGenerated(int cx, int cz)
        {
            if ((uint)cx >= (uint)_chunksPerSide || (uint)cz >= (uint)_chunksPerSide) return true;
            return Volatile.Read(ref _chunks[cz * _chunksPerSide + cx]) != null;
        }

        /// <summary>Generates (if needed) and returns the chunk's samples. Safe from any thread.</summary>
        public ushort[] EnsureChunk(int cx, int cz)
        {
            if ((uint)cx >= (uint)_chunksPerSide || (uint)cz >= (uint)_chunksPerSide) return null;
            int idx = cz * _chunksPerSide + cx;
            ushort[] data = Volatile.Read(ref _chunks[idx]);
            if (data != null) return data;

            data = new ushort[ChunkCells * ChunkCells];
            int baseX = cx * ChunkCells;
            int baseZ = cz * ChunkCells;
            for (int z = 0; z < ChunkCells; z++)
            {
                double wz = (baseZ + z) * (double)WorldConfig.CellSize;
                for (int x = 0; x < ChunkCells; x++)
                {
                    double wx = (baseX + x) * (double)WorldConfig.CellSize;
                    data[z * ChunkCells + x] = Quantize(_gen.ComputeHeight(wx, wz));
                }
            }

            ushort[] existing = Interlocked.CompareExchange(ref _chunks[idx], data, null);
            if (existing != null) return existing;
            Interlocked.Increment(ref _generatedChunks);
            return data;
        }

        /// <summary>Generates all chunks overlapping a square area (blocking).</summary>
        public void Prewarm(float centerX, float centerZ, float radius)
        {
            float chunkSize = ChunkCells * WorldConfig.CellSize;
            int minX = Math.Max(0, SMath.FloorToInt((centerX - radius) / chunkSize));
            int maxX = Math.Min(_chunksPerSide - 1, SMath.FloorToInt((centerX + radius) / chunkSize));
            int minZ = Math.Max(0, SMath.FloorToInt((centerZ - radius) / chunkSize));
            int maxZ = Math.Min(_chunksPerSide - 1, SMath.FloorToInt((centerZ + radius) / chunkSize));
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                    EnsureChunk(x, z);
        }

        /// <summary>
        /// Quantized sample at grid index. Cached chunks are read directly; outside the cache the
        /// sample is computed on the fly (same result, not stored).
        /// </summary>
        public ushort GetSampleQ(int ix, int iz)
        {
            if (ix < 0 || iz < 0 || ix >= SamplesPerSide + ChunkCells || iz >= SamplesPerSide + ChunkCells)
            {
                return Quantize(_gen.ComputeHeight(ix * (double)WorldConfig.CellSize, iz * (double)WorldConfig.CellSize));
            }
            int cx = ix / ChunkCells;
            int cz = iz / ChunkCells;
            if ((uint)cx < (uint)_chunksPerSide && (uint)cz < (uint)_chunksPerSide)
            {
                ushort[] data = Volatile.Read(ref _chunks[cz * _chunksPerSide + cx]);
                if (data != null) return data[(iz - cz * ChunkCells) * ChunkCells + (ix - cx * ChunkCells)];
            }
            return Quantize(_gen.ComputeHeight(ix * (double)WorldConfig.CellSize, iz * (double)WorldConfig.CellSize));
        }

        public float GetSample(int ix, int iz) => GetSampleQ(ix, iz) * InvQuantScale;

        /// <summary>
        /// Height at a world position using the same triangle split as the rendered terrain mesh
        /// (diagonal from (0,0) to (1,1) of each cell), so feet stay exactly on the visible ground.
        /// </summary>
        public float GetHeight(float x, float z)
        {
            float gx = x / WorldConfig.CellSize;
            float gz = z / WorldConfig.CellSize;
            int ix = SMath.FloorToInt(gx);
            int iz = SMath.FloorToInt(gz);
            float fx = gx - ix;
            float fz = gz - iz;

            float h00 = GetSample(ix, iz);
            float h11 = GetSample(ix + 1, iz + 1);
            if (fx >= fz)
            {
                float h10 = GetSample(ix + 1, iz);
                return h00 + (h10 - h00) * fx + (h11 - h10) * fz;
            }
            float h01 = GetSample(ix, iz + 1);
            return h00 + (h11 - h01) * fx + (h01 - h00) * fz;
        }

        /// <summary>Surface normal from central differences on the sample grid.</summary>
        public Vector3 GetNormal(float x, float z)
        {
            const float e = WorldConfig.CellSize;
            float hl = GetHeight(x - e, z);
            float hr = GetHeight(x + e, z);
            float hd = GetHeight(x, z - e);
            float hu = GetHeight(x, z + e);
            var n = new Vector3(hl - hr, 2f * e, hd - hu);
            return Vector3.Normalize(n);
        }

        public float GetSlopeDeg(float x, float z)
        {
            Vector3 n = GetNormal(x, z);
            return (float)(Math.Acos(SMath.Clamp(n.Y, -1f, 1f)) * (180.0 / Math.PI));
        }

        /// <summary>
        /// Slope as tan(angle) from integer differences of the samples around the nearest grid
        /// point. Integer maths + sqrt only, so it is bit-identical on server and phone; use it for
        /// any gameplay decision (node placement, spawn rules). 1.0 = 45 degrees.
        /// </summary>
        public float GetSlopeTanGrid(float x, float z)
        {
            int ix = SMath.FloorToInt(x / WorldConfig.CellSize + 0.5f);
            int iz = SMath.FloorToInt(z / WorldConfig.CellSize + 0.5f);
            int dx = GetSampleQ(ix + 1, iz) - GetSampleQ(ix - 1, iz);
            int dz = GetSampleQ(ix, iz + 1) - GetSampleQ(ix, iz - 1);
            return (float)(Math.Sqrt((double)dx * dx + (double)dz * dz) / (QuantScale * 2.0 * WorldConfig.CellSize));
        }

        public bool IsInsideWorld(float x, float z) => x >= 0f && z >= 0f && x <= WorldSize && z <= WorldSize;
    }
}
