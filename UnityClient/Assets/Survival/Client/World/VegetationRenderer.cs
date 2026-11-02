using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Survival.Client.Rendering;
using Survival.Core;
using Survival.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Survival.Client.WorldView
{
    /// <summary>
    /// GPU-instanced trees, bushes, boulders and resource nodes from the deterministic node layout.
    /// Instance lists are rebuilt on a worker thread when the viewer moves; each frame only visible
    /// 128 m blocks are drawn (frustum culling per block) with Graphics.RenderMeshInstanced.
    /// Near instances use the detailed LOD with shadows, far instances a cheap LOD without.
    /// </summary>
    public sealed class VegetationRenderer : MonoBehaviour
    {
        private const float BlockSize = 128f;

        private struct Range
        {
            public int Start;
            public int Count;
            public Bounds Bounds;
        }

        private sealed class Batch
        {
            public NodeKind Kind;
            public int Lod;
            public Matrix4x4[] Matrices = Array.Empty<Matrix4x4>();
            public readonly List<Range> Ranges = new List<Range>();
        }

        private sealed class BuildResult
        {
            public readonly Dictionary<int, Batch> Batches = new Dictionary<int, Batch>();
            public int Instances;
        }

        private WorldRuntime _world;
        private Transform _viewer;
        private Camera _camera;
        private Material _foliage;
        private Material _rock;
        private Dictionary<int, Batch> _batches = new Dictionary<int, Batch>();
        private Task<BuildResult> _task;
        private Vector3 _lastBuildPos = new Vector3(float.MaxValue, 0, 0);
        private readonly Plane[] _planes = new Plane[6];

        public float NearDistance = 70f;
        public float Distance = 220f;
        public float TreeFarDistance = 450f;
        public float Density = 1f;
        public bool Shadows = true;
        public int Instances { get; private set; }
        public int DrawCalls { get; private set; }

        public static VegetationRenderer Create(WorldRuntime world, Transform viewer, Camera cam)
        {
            var go = new GameObject("[Vegetation]");
            var v = go.AddComponent<VegetationRenderer>();
            v._world = world;
            v._viewer = viewer;
            v._camera = cam;
            v._foliage = App.Materials.Lit("foliage", Color.white, 0f, 0f, 0f, 0.6f);
            v._rock = App.Materials.Lit("rock", Color.white, 0.05f, 0.1f);
            v.ApplyQuality();
            QualityManager.Changed += v.ApplyQuality;
            return v;
        }

        private void OnDestroy() => QualityManager.Changed -= ApplyQuality;

        public void ApplyQuality()
        {
            Distance = QualityManager.VegetationDistance;
            TreeFarDistance = Mathf.Min(QualityManager.ViewDistance * 0.6f, Distance * 2.2f);
            Density = QualityManager.VegetationDensity;
            Shadows = QualityManager.Shadows;
            _lastBuildPos = new Vector3(float.MaxValue, 0, 0);
        }

        private static int BatchKey(NodeKind k, int lod) => (int)k * 4 + lod;

        private void Update()
        {
            if (_viewer == null) return;
            Vector3 p = _viewer.position;
            if (_task == null && (p - _lastBuildPos).sqrMagnitude > 14f * 14f)
            {
                _lastBuildPos = p;
                WorldRuntime world = _world;
                float near = NearDistance, dist = Distance, far = TreeFarDistance, density = Density;
                Func<uint, bool> alive = _world.IsNodeAlive;
                _task = Task.Run(() => Build(world, p, near, dist, far, density, alive));
            }
            if (_task != null && _task.IsCompleted)
            {
                if (_task.Status == TaskStatus.RanToCompletion)
                {
                    _batches = _task.Result.Batches;
                    Instances = _task.Result.Instances;
                }
                else
                {
                    Log.Warn("Vegetation", "Build failed: " + _task.Exception?.GetBaseException().Message);
                }
                _task = null;
            }
            Draw();
        }

        private static BuildResult Build(WorldRuntime world, Vector3 p, float near, float dist, float far, float density, Func<uint, bool> alive)
        {
            var result = new BuildResult();
            var temp = new Dictionary<int, List<(int block, Matrix4x4 m, Vector3 pos)>>();
            float cs = NodeLayout.CellSize;
            int r = Mathf.CeilToInt(far / cs);
            int pcx = Mathf.FloorToInt(p.x / cs), pcz = Mathf.FloorToInt(p.z / cs);
            float near2 = near * near, dist2 = dist * dist, far2 = far * far;
            int blocksPerSide = Mathf.CeilToInt(world.Config.Size / BlockSize) + 1;

            for (int cz = pcz - r; cz <= pcz + r; cz++)
            {
                for (int cx = pcx - r; cx <= pcx + r; cx++)
                {
                    float ccx = (cx + 0.5f) * cs - p.x, ccz = (cz + 0.5f) * cs - p.z;
                    if (ccx * ccx + ccz * ccz > (far + cs) * (far + cs)) continue;
                    WorldNode[] nodes = world.Nodes.GetCell(cx, cz);
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        WorldNode n = nodes[i];
                        if (alive != null && !alive(n.Id)) continue;
                        float dx = n.X - p.x, dz = n.Z - p.z;
                        float d2 = dx * dx + dz * dz;
                        bool tree = NodeKinds.IsTree(n.Kind);
                        bool ore = NodeKinds.IsOre(n.Kind);
                        if (d2 > (tree ? far2 : dist2)) continue;
                        // Density thins bushes/boulders on low quality; trees and resources always stay (gameplay)
                        if (!tree && !ore && Hashing.To01(Hashing.Mix(n.Id)) > density) continue;
                        int lod = d2 < near2 ? 0 : 1;
                        int key = BatchKey(n.Kind, lod);
                        if (!temp.TryGetValue(key, out var list))
                        {
                            list = new List<(int, Matrix4x4, Vector3)>(256);
                            temp[key] = list;
                        }
                        Vector3 pos = new Vector3(n.X, n.Y - 0.08f, n.Z);
                        var m = Matrix4x4.TRS(pos, Quaternion.Euler(0, n.RotY, 0), Vector3.one * n.Scale);
                        int block = Mathf.FloorToInt(n.Z / BlockSize) * blocksPerSide + Mathf.FloorToInt(n.X / BlockSize);
                        list.Add((block, m, pos));
                    }
                }
            }

            foreach (var kv in temp)
            {
                var list = kv.Value;
                list.Sort((a, b) => a.block.CompareTo(b.block));
                var batch = new Batch { Kind = (NodeKind)(kv.Key / 4), Lod = kv.Key % 4, Matrices = new Matrix4x4[list.Count] };
                int start = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    batch.Matrices[i] = list[i].m;
                    bool end = i == list.Count - 1 || list[i + 1].block != list[i].block || i - start + 1 >= 1023;
                    if (end)
                    {
                        Bounds b = new Bounds(list[start].pos, Vector3.one);
                        for (int k = start + 1; k <= i; k++) b.Encapsulate(list[k].pos);
                        b.Expand(new Vector3(8f, 24f, 8f));
                        batch.Ranges.Add(new Range { Start = start, Count = i - start + 1, Bounds = b });
                        start = i + 1;
                    }
                }
                result.Batches[kv.Key] = batch;
                result.Instances += list.Count;
            }
            return result;
        }

        private void Draw()
        {
            if (_camera == null) return;
            GeometryUtility.CalculateFrustumPlanes(_camera, _planes);
            int calls = 0;
            foreach (Batch b in _batches.Values)
            {
                Mesh mesh = Models.Get(b.Kind, b.Lod);
                bool rocky = b.Kind == NodeKind.Boulder || NodeKinds.IsOre(b.Kind);
                var rp = new RenderParams(rocky ? _rock : _foliage)
                {
                    shadowCastingMode = Shadows && b.Lod == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = 0,
                };
                foreach (Range r in b.Ranges)
                {
                    if (!GeometryUtility.TestPlanesAABB(_planes, r.Bounds)) continue;
                    rp.worldBounds = r.Bounds;
                    Graphics.RenderMeshInstanced(rp, mesh, 0, b.Matrices, r.Count, r.Start);
                    calls++;
                }
            }
            DrawCalls = calls;
        }
    }
}
