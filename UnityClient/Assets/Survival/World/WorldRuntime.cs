using System;
using System.Collections.Generic;
using System.Numerics;
using Survival.Core;

namespace Survival.World
{
    /// <summary>Collision/ground queries needed by the shared movement simulation.</summary>
    public interface IMovementWorld
    {
        float WorldSize { get; }
        float WaterLevel { get; }
        float GroundHeight(float x, float z);
        /// <summary>Pushes a circle (player capsule footprint) out of static obstacles (trees, rocks).</summary>
        void ResolveObstacles(ref float x, ref float z, float radius);
    }

    /// <summary>
    /// The complete deterministic world: generator + height field + node layout + collision.
    /// One instance per process on the server, one per session on the client.
    /// </summary>
    public sealed class WorldRuntime : IMovementWorld
    {
        [ThreadStatic] private static List<WorldNode> _scratch;

        /// <summary>
        /// Optional predicate for node liveness (felled trees no longer block). Set by the server
        /// world and by the client replication layer. Null = every node exists.
        /// </summary>
        public Func<uint, bool> IsNodeAlive;

        public WorldRuntime(WorldConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Generator = new IslandGenerator(config);
            Heights = new HeightField(Generator);
            Nodes = new NodeLayout(Heights);
        }

        public WorldConfig Config { get; }
        public IslandGenerator Generator { get; }
        public HeightField Heights { get; }
        public NodeLayout Nodes { get; }

        public float WorldSize => Config.Size;
        public float WaterLevel => Config.SeaLevel;

        public float GroundHeight(float x, float z) => Heights.GetHeight(x, z);

        public bool IsUnderwater(float x, float z) => Heights.GetHeight(x, z) < Config.SeaLevel;

        public void ResolveObstacles(ref float x, ref float z, float radius)
        {
            var list = _scratch ?? (_scratch = new List<WorldNode>(32));
            list.Clear();
            Nodes.Query(x, z, radius + 2.5f, list);
            Func<uint, bool> alive = IsNodeAlive;
            for (int i = 0; i < list.Count; i++)
            {
                WorldNode n = list[i];
                if (n.Radius <= 0f) continue;
                if (alive != null && !alive(n.Id)) continue;
                float dx = x - n.X;
                float dz = z - n.Z;
                float min = n.Radius + radius;
                float d2 = dx * dx + dz * dz;
                if (d2 >= min * min) continue;
                float d = (float)Math.Sqrt(d2);
                if (d < 1e-4f)
                {
                    dx = 1f;
                    dz = 0f;
                    d = 1f;
                }
                float push = min - d;
                x += dx / d * push;
                z += dz / d * push;
            }
        }

        /// <summary>True when a circle at (x, z) overlaps any live obstacle.</summary>
        public bool OverlapsObstacle(float x, float z, float radius)
        {
            float rx = x, rz = z;
            ResolveObstacles(ref rx, ref rz, radius);
            float dx = rx - x, dz = rz - z;
            return dx * dx + dz * dz > 1e-6f;
        }
    }

    /// <summary>
    /// Server-side safe spawn selection: never in water, never on steep slopes or inside rocks/trees,
    /// never on top of another player, preferring places far from danger.
    /// </summary>
    public static class SpawnFinder
    {
        public const float MaxSpawnSlopeDeg = 24f;
        public const float MinSeparation = 12f;

        public static bool IsSafe(WorldRuntime world, float x, float z, out float groundY)
        {
            groundY = 0f;
            if (!world.Heights.IsInsideWorld(x, z)) return false;
            groundY = world.GroundHeight(x, z);
            if (groundY < world.WaterLevel + 1.0f) return false; // water / wet beach line
            if (world.Heights.GetSlopeDeg(x, z) > MaxSpawnSlopeDeg) return false;
            if (world.OverlapsObstacle(x, z, 1.2f)) return false;
            return true;
        }

        /// <summary>
        /// Picks the best of several valid random candidates. <paramref name="occupied"/> are other
        /// players' positions, <paramref name="danger"/> are positions to stay away from (bots in
        /// combat, predators, active events).
        /// </summary>
        public static bool TryFind(WorldRuntime world, ref Rng rng, IReadOnlyList<Vector3> occupied,
            IReadOnlyList<Vector3> danger, out Vector3 position, int maxAttempts = 400)
        {
            var gen = world.Generator;
            float c = (float)gen.Center;
            float r = (float)gen.IslandRadius;
            float bestScore = float.NegativeInfinity;
            position = default;
            int valid = 0;

            for (int attempt = 0; attempt < maxAttempts && valid < 10; attempt++)
            {
                float x = c + rng.Range(-r, r);
                float z = c + rng.Range(-r, r);
                if (!IsSafe(world, x, z, out float y)) continue;

                var p = new Vector3(x, y, z);
                float nearestOccupied = Nearest(occupied, p);
                if (nearestOccupied < MinSeparation) continue;

                valid++;
                float nearestDanger = Nearest(danger, p);
                // Prefer distance from danger, mild preference for not-too-high ground (easier start)
                float score = Math.Min(nearestDanger, 400f) + Math.Min(nearestOccupied, 60f) * 0.25f
                              - Math.Max(0f, y - world.WaterLevel - 60f) * 0.5f + rng.NextFloat() * 20f;
                if (score > bestScore)
                {
                    bestScore = score;
                    position = p;
                }
            }

            return valid > 0;
        }

        private static float Nearest(IReadOnlyList<Vector3> points, Vector3 p)
        {
            if (points == null || points.Count == 0) return float.MaxValue;
            float best = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                float d = SMath.DistanceXZ(points[i], p);
                if (d < best) best = d;
            }
            return best;
        }
    }
}
