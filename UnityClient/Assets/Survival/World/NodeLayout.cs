using System;
using System.Collections.Generic;
using System.Threading;
using Survival.Core;

namespace Survival.World
{
    /// <summary>Everything that is placed on the terrain by the deterministic layout.</summary>
    public enum NodeKind : byte
    {
        None = 0,
        TreePine = 1,
        TreeBroadleaf = 2,
        TreeDead = 3,
        Bush = 4,
        Boulder = 5,
        StoneNode = 10,
        MetalNode = 11,
        SulfurNode = 12,
    }

    public static class NodeKinds
    {
        public static bool IsTree(NodeKind k) => k == NodeKind.TreePine || k == NodeKind.TreeBroadleaf || k == NodeKind.TreeDead;
        public static bool IsOre(NodeKind k) => k == NodeKind.StoneNode || k == NodeKind.MetalNode || k == NodeKind.SulfurNode;
        public static bool IsHarvestable(NodeKind k) => IsTree(k) || IsOre(k);
    }

    /// <summary>A tree, bush, boulder or resource node. Ids are stable for a given seed.</summary>
    public struct WorldNode
    {
        public uint Id;
        public NodeKind Kind;
        public float X, Y, Z;
        public float Scale;
        public float RotY;
        /// <summary>Collision radius on the XZ plane (0 = walk-through, e.g. bushes).</summary>
        public float Radius;
    }

    /// <summary>
    /// Deterministic vegetation/resource placement in 32 m cells. Every client and the server
    /// compute exactly the same nodes from the seed; the server only replicates node state changes
    /// (felled/mined/respawned) in Phase 2.
    /// </summary>
    public sealed class NodeLayout
    {
        public const float CellSize = 32f;
        public const int MaxNodesPerCell = 32;
        private static readonly WorldNode[] Empty = Array.Empty<WorldNode>();

        private readonly HeightField _heights;
        private readonly IslandGenerator _gen;
        private readonly WorldConfig _cfg;
        private readonly WorldNode[][] _cells;

        public NodeLayout(HeightField heights)
        {
            _heights = heights ?? throw new ArgumentNullException(nameof(heights));
            _gen = heights.Generator;
            _cfg = _gen.Config;
            CellsPerSide = (int)Math.Ceiling(_cfg.Size / CellSize);
            _cells = new WorldNode[CellsPerSide * CellsPerSide][];
        }

        public int CellsPerSide { get; }

        public static uint MakeId(int cellIndex, int local) => ((uint)cellIndex << 5) | (uint)local;
        public static int CellIndexOf(uint id) => (int)(id >> 5);

        public bool TryGetNode(uint id, out WorldNode node)
        {
            int cell = CellIndexOf(id);
            node = default;
            if ((uint)cell >= (uint)_cells.Length) return false;
            WorldNode[] nodes = GetCell(cell % CellsPerSide, cell / CellsPerSide);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Id == id)
                {
                    node = nodes[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>Nodes of one 32 m cell (generated on first access, thread-safe).</summary>
        public WorldNode[] GetCell(int cx, int cz)
        {
            if ((uint)cx >= (uint)CellsPerSide || (uint)cz >= (uint)CellsPerSide) return Empty;
            int idx = cz * CellsPerSide + cx;
            WorldNode[] nodes = Volatile.Read(ref _cells[idx]);
            if (nodes != null) return nodes;
            nodes = Generate(cx, cz, idx);
            WorldNode[] existing = Interlocked.CompareExchange(ref _cells[idx], nodes, null);
            return existing ?? nodes;
        }

        /// <summary>Appends all nodes whose cells overlap the circle.</summary>
        public void Query(float x, float z, float radius, List<WorldNode> results)
        {
            int minX = SMath.FloorToInt((x - radius) / CellSize);
            int maxX = SMath.FloorToInt((x + radius) / CellSize);
            int minZ = SMath.FloorToInt((z - radius) / CellSize);
            int maxZ = SMath.FloorToInt((z + radius) / CellSize);
            float r2 = radius * radius;
            for (int cz = minZ; cz <= maxZ; cz++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    WorldNode[] nodes = GetCell(cx, cz);
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        float dx = nodes[i].X - x, dz = nodes[i].Z - z;
                        if (dx * dx + dz * dz <= r2) results.Add(nodes[i]);
                    }
                }
            }
        }

        private WorldNode[] Generate(int cx, int cz, int cellIndex)
        {
            double centerX = (cx + 0.5) * CellSize;
            double centerZ = (cz + 0.5) * CellSize;
            double dist = _gen.IslandDistance(centerX, centerZ);
            if (dist > 1.0) return Empty; // open ocean

            var rng = new Rng(Hashing.Hash2(_cfg.Seed ^ 0x5EED, cx, cz));
            float forest = _gen.ForestDensity(centerX, centerZ);
            float moisture = _gen.Moisture(centerX, centerZ);
            float veg = Math.Max(0f, _cfg.VegetationDensity);
            float res = Math.Max(0f, _cfg.ResourceDensity);

            // Expected counts per 32x32 m cell
            float trees = (0.35f + forest * 7.5f) * veg;
            float bushes = (0.6f + moisture * 1.6f) * veg;
            float boulders = 0.35f * veg;
            float ores = 0.11f * res;

            var list = new List<WorldNode>(12);
            int local = 0;
            int candidates = Math.Min(MaxNodesPerCell, (int)(trees + bushes + boulders + ores + 2f));

            for (int i = 0; i < candidates && local < MaxNodesPerCell; i++)
            {
                float px = (float)(cx * CellSize + rng.Range(0.5f, CellSize - 0.5f));
                float pz = (float)(cz * CellSize + rng.Range(0.5f, CellSize - 0.5f));
                float roll = rng.NextFloat() * (trees + bushes + boulders + ores + 2f);
                float scaleRoll = rng.NextFloat();
                float rot = rng.Range(0f, 360f);
                float variant = rng.NextFloat();

                NodeKind kind;
                if (roll < trees) kind = NodeKind.TreePine;
                else if (roll < trees + bushes) kind = NodeKind.Bush;
                else if (roll < trees + bushes + boulders) kind = NodeKind.Boulder;
                else if (roll < trees + bushes + boulders + ores) kind = NodeKind.StoneNode;
                else continue; // empty candidate keeps density variation natural

                float h = _heights.GetHeight(px, pz);
                float sea = _cfg.SeaLevel;
                if (h < sea + 0.8f) continue;

                float slopeTan = _heights.GetSlopeTanGrid(px, pz); // tan(angle), deterministic
                float alt = h - sea;

                switch (kind)
                {
                    case NodeKind.TreePine:
                        if (slopeTan > 0.62f || alt < 2.5f || alt > 235f) continue; // > ~32 deg
                        // Pines dominate higher/colder ground, broadleaf the lowlands, dead trees on dry ground
                        if (alt < 70f && variant < 0.55f) kind = NodeKind.TreeBroadleaf;
                        else if (moisture < 0.4f && variant > 0.9f) kind = NodeKind.TreeDead;
                        break;
                    case NodeKind.Bush:
                        if (slopeTan > 0.70f || alt < 1.5f || alt > 180f) continue; // > ~35 deg
                        break;
                    case NodeKind.Boulder:
                        if (alt < 1.0f) continue;
                        break;
                    case NodeKind.StoneNode:
                        // Metal and sulfur prefer rocky high ground (resource-rich zones)
                        float rich = SMath.Clamp01((alt - 40f) / 120f) + SMath.Clamp01((slopeTan - 0.27f) / 0.6f);
                        float r = rng.NextFloat();
                        if (r < 0.25f + rich * 0.25f) kind = NodeKind.MetalNode;
                        else if (r < 0.37f + rich * 0.33f) kind = NodeKind.SulfurNode;
                        break;
                }

                float scale;
                float radius;
                switch (kind)
                {
                    case NodeKind.TreePine: scale = 0.8f + scaleRoll * 0.6f; radius = 0.35f * scale; break;
                    case NodeKind.TreeBroadleaf: scale = 0.8f + scaleRoll * 0.5f; radius = 0.45f * scale; break;
                    case NodeKind.TreeDead: scale = 0.7f + scaleRoll * 0.4f; radius = 0.3f * scale; break;
                    case NodeKind.Bush: scale = 0.6f + scaleRoll * 0.7f; radius = 0f; break;
                    case NodeKind.Boulder: scale = 0.6f + scaleRoll * 1.6f; radius = 0.75f * scale; break;
                    default: scale = 0.9f + scaleRoll * 0.3f; radius = 0.9f * scale; break;
                }

                list.Add(new WorldNode
                {
                    Id = MakeId(cellIndex, local++),
                    Kind = kind,
                    X = px,
                    Y = h,
                    Z = pz,
                    Scale = scale,
                    RotY = rot,
                    Radius = radius,
                });
            }

            return list.Count == 0 ? Empty : list.ToArray();
        }
    }
}
