using System;
using Survival.Core;

namespace Survival.World
{
    /// <summary>
    /// Hash of a sparse sample of the generated terrain and node layout. The server sends it in
    /// the Welcome message; the client compares it with its own after generating the island.
    /// A mismatch means client and server run different generator code (outdated game build),
    /// which would make client-side prediction wrong everywhere - so it must be detected.
    /// Whenever the generator output changes, bump <see cref="WorldConfig.CurrentGeneratorVersion"/>
    /// and update the pinned value in the server tests.
    /// </summary>
    public static class WorldFingerprint
    {
        public static uint Compute(WorldRuntime world)
        {
            uint h = 2166136261U;
            HeightField hf = world.Heights;
            int n = hf.SamplesPerSide;
            const int Grid = 48;
            for (int z = 0; z < Grid; z++)
            {
                for (int x = 0; x < Grid; x++)
                {
                    int ix = (int)((long)x * (n - 1) / (Grid - 1));
                    int iz = (int)((long)z * (n - 1) / (Grid - 1));
                    h = Hashing.Combine(h, hf.GetSampleQ(ix, iz));
                }
            }

            NodeLayout nodes = world.Nodes;
            int cells = nodes.CellsPerSide;
            for (int i = 0; i < 24; i++)
            {
                int cx = (int)((long)(i * 7 + 3) * cells / 170) % cells;
                int cz = (int)((long)(i * 11 + 5) * cells / 270) % cells;
                cx = cells / 4 + cx / 2;
                cz = cells / 4 + cz / 2;
                WorldNode[] list = nodes.GetCell(cx, cz);
                h = Hashing.Combine(h, (uint)list.Length);
                for (int k = 0; k < list.Length; k++)
                {
                    h = Hashing.Combine(h, list[k].Id);
                    h = Hashing.Combine(h, (uint)list[k].Kind);
                    h = Hashing.Combine(h, (uint)(int)Math.Round(list[k].X * 100f));
                    h = Hashing.Combine(h, (uint)(int)Math.Round(list[k].Z * 100f));
                }
            }
            return Hashing.Mix(h ^ (uint)world.Config.GeneratorVersion);
        }
    }
}
