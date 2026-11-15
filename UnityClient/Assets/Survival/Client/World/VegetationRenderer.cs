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
    /// 128 m blocks are merged into one mesh per block and kind (frustum culling per block).
    /// Near instances use the detailed LOD with shadows, far instances a cheap LOD without.
    /// </summary>
    public sealed class VegetationRenderer : MonoBehaviour
    {
        private const float BlockSize = 128f;
        /// <summary>Instances merged into one mesh / draw call.</summary>
        private const int MaxBatch = 300;

        private struct Range
        {
            public int Start;
            public int Count;
            public Bounds Bounds;
            /// <summary>Identity of the instance set (kind, LOD, block, positions) for the combined-mesh cache.</summary>
            public long Hash;
            public Mesh Combined;
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
        private Material _rockBoulder, _rockStone, _rockMetal, _rockSulfur;
        private Dictionary<int, Batch> _batches = new Dictionary<int, Batch>();
        private Task<BuildResult> _task;
        private Vector3 _lastBuildPos = new Vector3(float.MaxValue, 0, 0);
        private readonly Plane[] _planes = new Plane[6];
        private HashSet<uint> _depleted = new HashSet<uint>();
        private bool _dirty;

        /// <summary>Pseudo node kind used to draw the stump of a felled tree.</summary>
        public const NodeKind StumpKind = (NodeKind)200;

        /// <summary>Replaces the set of felled/mined nodes (copied, so the caller may keep mutating its own set).</summary>
        public void SetDepleted(HashSet<uint> live)
        {
            _depleted = new HashSet<uint>(live);
            _dirty = true;
        }

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
            v._foliage = (App.Materials.Lit("foliage", Color.white, 0f, 0f, 0f, 0.6f));
            v._rock = (App.Materials.Lit("rock", Color.white, 0.05f, 0.1f));
            // Textured rock materials (triplanar scans); null entries fall back to the vertex-colour rock
            v._rockBoulder = (App.Materials.Rock("rock_boulder", new Color(0.92f, 0.9f, 0.88f), Color.white, 0f));
            v._rockStone = (App.Materials.Rock("rock_stone", new Color(1.05f, 1.05f, 1.08f), new Color(0.9f, 0.9f, 0.92f), 0.4f));
            v._rockMetal = (App.Materials.Rock("rock_metal", new Color(0.78f, 0.72f, 0.68f), new Color(0.74f, 0.4f, 0.22f), 0.62f));
            v._rockSulfur = (App.Materials.Rock("rock_sulfur", new Color(0.95f, 0.92f, 0.8f), new Color(0.96f, 0.82f, 0.12f), 0.62f));
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
            if (_task == null && (_dirty || (p - _lastBuildPos).sqrMagnitude > 14f * 14f))
            {
                _lastBuildPos = p;
                _dirty = false;
                WorldRuntime world = _world;
                float near = NearDistance, dist = Distance, far = TreeFarDistance, density = Density;
                HashSet<uint> alive = _depleted; // immutable snapshot, safe on the worker thread
                _task = Task.Run(() => Build(world, p, near, dist, far, density, alive));
            }
            if (_task != null && _task.IsCompleted)
            {
                if (_task.Status == TaskStatus.RanToCompletion)
                {
                    ApplyBuild(_task.Result);
                }
                else
                {
                    Log.Warn("Vegetation", "Build failed: " + _task.Exception?.GetBaseException().Message);
                }
                _task = null;
            }
            Draw();
        }

        private static BuildResult Build(WorldRuntime world, Vector3 p, float near, float dist, float far, float density, HashSet<uint> depleted)
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
                        float dx = n.X - p.x, dz = n.Z - p.z;
                        float d2 = dx * dx + dz * dz;
                        bool tree = NodeKinds.IsTree(n.Kind);
                        if (depleted.Contains(n.Id))
                        {
                            // Felled trees leave a stump nearby; mined nodes and picked bushes disappear
                            if (!tree || d2 > dist2) continue;
                            n.Kind = StumpKind;
                            tree = false;
                        }
                        bool ore = NodeKinds.IsOre(n.Kind);
                        if (d2 > (tree ? far2 : dist2)) continue;
                        // Density thins bushes/boulders on low quality; trees and resources always stay (gameplay)
                        if (!tree && !ore && n.Kind != StumpKind && Hashing.To01(Hashing.Mix(n.Id)) > density) continue;
                        int lod = n.Kind == StumpKind || d2 < near2 ? 0 : 1;
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
                    bool end = i == list.Count - 1 || list[i + 1].block != list[i].block || i - start + 1 >= MaxBatch;
                    if (end)
                    {
                        Bounds b = new Bounds(list[start].pos, Vector3.one);
                        for (int k = start + 1; k <= i; k++) b.Encapsulate(list[k].pos);
                        b.Expand(new Vector3(8f, 24f, 8f));
                        long hash = kv.Key * 1000003L + list[start].block;
                        for (int k = start; k <= i; k++)
                        {
                            Vector3 q = list[k].pos;
                            hash = hash * 31L + (long)(q.x * 64f) * 73856093L + (long)(q.z * 64f) * 19349663L;
                        }
                        batch.Ranges.Add(new Range { Start = start, Count = i - start + 1, Bounds = b, Hash = hash });
                        start = i + 1;
                    }
                }
                result.Batches[kv.Key] = batch;
                result.Instances += list.Count;
            }
            return result;
        }

        private readonly Dictionary<long, Mesh> _combined = new Dictionary<long, Mesh>();
        private readonly List<CombineInstance> _combineScratch = new List<CombineInstance>(MaxBatch);

        /// <summary>
        /// Turns each range of instances into one combined mesh (reused from the cache when the same
        /// instances are still in range). GPU instancing is not used: instanced draws rendered nothing
        /// on OpenGL ES devices, while plain meshes work everywhere and batch well.
        /// </summary>
        private void ApplyBuild(BuildResult result)
        {
            var next = new Dictionary<long, Mesh>();
            foreach (Batch b in result.Batches.Values)
            {
                Mesh source = Models.Get(b.Kind, b.Lod);
                for (int i = 0; i < b.Ranges.Count; i++)
                {
                    Range r = b.Ranges[i];
                    if (!_combined.TryGetValue(r.Hash, out Mesh mesh) || mesh == null)
                    {
                        _combineScratch.Clear();
                        for (int k = 0; k < r.Count; k++) _combineScratch.Add(new CombineInstance { mesh = source, transform = b.Matrices[r.Start + k] });
                        mesh = new Mesh { name = "veg_" + b.Kind + "_" + b.Lod, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                        mesh.CombineMeshes(_combineScratch.ToArray(), true, true, false);
                        mesh.UploadMeshData(true);
                    }
                    next[r.Hash] = mesh;
                    r.Combined = mesh;
                    b.Ranges[i] = r;
                }
            }
            foreach (KeyValuePair<long, Mesh> kv in _combined)
            {
                if (!next.ContainsKey(kv.Key) && kv.Value != null) Destroy(kv.Value);
            }
            _combined.Clear();
            foreach (KeyValuePair<long, Mesh> kv in next) _combined[kv.Key] = kv.Value;
            _batches = result.Batches;
            Instances = result.Instances;
        }

        private void OnDisable()
        {
            foreach (Mesh m in _combined.Values) if (m != null) Destroy(m);
            _combined.Clear();
            _batches = new Dictionary<int, Batch>();
            _dirty = true;
        }

        private void Draw()
        {
            if (_camera == null) return;
            GeometryUtility.CalculateFrustumPlanes(_camera, _planes);
            int calls = 0;
            foreach (Batch b in _batches.Values)
            {
                bool rocky = b.Kind == NodeKind.Boulder || NodeKinds.IsOre(b.Kind);
                Material rockMat = b.Kind == NodeKind.Boulder ? _rockBoulder : b.Kind == NodeKind.StoneNode ? _rockStone
                    : b.Kind == NodeKind.MetalNode ? _rockMetal : _rockSulfur;
                var rp = new RenderParams(rocky ? (rockMat != null ? rockMat : _rock) : _foliage)
                {
                    shadowCastingMode = Shadows && b.Lod == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = 0,
                };
                foreach (Range r in b.Ranges)
                {
                    if (r.Combined == null || !GeometryUtility.TestPlanesAABB(_planes, r.Bounds)) continue;
                    rp.worldBounds = r.Bounds;
                    Graphics.RenderMesh(rp, r.Combined, 0, Matrix4x4.identity);
                    calls++;
                }
            }
            DrawCalls = calls;
        }
    }
}
