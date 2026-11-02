using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Survival.Core;
using Survival.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Survival.Client.WorldView
{
    /// <summary>
    /// World chunk streaming: keeps 128 m terrain chunks within the view distance alive with
    /// distance-based LOD, builds vertex data on worker threads, uploads a few meshes per frame
    /// (no frame spikes) and recycles chunk objects/meshes. A coarse whole-island mesh fills the
    /// horizon beyond the streaming radius (clipped inside it by the shader).
    /// </summary>
    public sealed class TerrainStreamer : MonoBehaviour
    {
        private sealed class ChunkView
        {
            public int Cx, Cz;
            public int Lod = -1;
            public int WantedLod;
            public bool Building;
            public GameObject Go;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Mesh Mesh;
        }

        private static readonly int ClipRadiusId = Shader.PropertyToID("_ClipInnerRadius");

        private WorldRuntime _world;
        private Transform _viewer;
        private Material _material;
        private Material _farMaterial;
        private readonly Dictionary<long, ChunkView> _chunks = new Dictionary<long, ChunkView>(512);
        private readonly Stack<ChunkView> _pool = new Stack<ChunkView>();
        private readonly ConcurrentQueue<ChunkMeshData> _results = new ConcurrentQueue<ChunkMeshData>();
        private readonly List<(float dist, ChunkView view)> _requests = new List<(float, ChunkView)>(256);
        private readonly List<long> _remove = new List<long>(64);
        private int _running;
        private Vector3 _lastUpdatePos = new Vector3(float.MaxValue, 0, 0);
        private float _updateTimer;
        private Task<TerrainMeshes.FarIslandData> _farTask;
        private Transform _far;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public float ViewDistance = 1100f;
        public int MaxConcurrentBuilds = 2;
        public int MaxUploadsPerFrame = 2;
        public float[] LodDistances = { 220f, 450f, 800f };

        /// <summary>0..1: fraction of the LOD0 chunks around the viewer that are visible.</summary>
        public float NearReadyFraction { get; private set; }
        public int ActiveChunks => _chunks.Count;

        public static TerrainStreamer Create(WorldRuntime world, Transform viewer, float viewDistance)
        {
            var go = new GameObject("[Terrain]");
            var s = go.AddComponent<TerrainStreamer>();
            s._world = world;
            s._viewer = viewer;
            s.ViewDistance = viewDistance;
            s._material = App.Materials.Lit("terrain", Color.white, 0.02f, 0f, 0f, 0f, 0.22f);
            s._farMaterial = new Material(App.Materials.Lit("far_terrain", Color.white, 0f, 0f, 0f, 0f, 0.2f)) { name = "far_terrain_clip" };
            s._farTask = Task.Run(() => TerrainMeshes.ComputeFarIsland(world, 40f, 0.6f));
            return s;
        }

        private void Update()
        {
            if (_viewer == null) return;
            ApplyFar();
            Vector3 p = _viewer.position;
            _updateTimer -= Time.unscaledDeltaTime;
            if (_updateTimer <= 0f || (p - _lastUpdatePos).sqrMagnitude > 24f * 24f)
            {
                _updateTimer = 0.3f;
                _lastUpdatePos = p;
                Refresh(p);
            }
            Dispatch();
            Upload();
            if (_farMaterial != null) _farMaterial.SetFloat(ClipRadiusId, Mathf.Max(0f, ViewDistance - 110f));
        }

        private void ApplyFar()
        {
            if (_farTask == null || !_farTask.IsCompleted) return;
            if (_farTask.Status == TaskStatus.RanToCompletion)
            {
                var go = new GameObject("FarIsland");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = TerrainMeshes.ToMesh(_farTask.Result);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _farMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                _far = go.transform;
            }
            else
            {
                Log.Warn("Terrain", "Far island mesh failed: " + _farTask.Exception?.GetBaseException().Message);
            }
            _farTask = null;
        }

        private static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;

        private int LodFor(float dist)
        {
            for (int i = 0; i < LodDistances.Length; i++) if (dist < LodDistances[i]) return i;
            return LodDistances.Length;
        }

        private void Refresh(Vector3 p)
        {
            float size = TerrainMeshes.ChunkSize;
            int per = _world.Heights.ChunksPerSide;
            int r = Mathf.CeilToInt(ViewDistance / size) + 1;
            int pcx = Mathf.FloorToInt(p.x / size), pcz = Mathf.FloorToInt(p.z / size);
            float maxD2 = (ViewDistance + size * 0.71f) * (ViewDistance + size * 0.71f);
            int nearTotal = 0, nearReady = 0;
            _requests.Clear();

            for (int cz = pcz - r; cz <= pcz + r; cz++)
            {
                if (cz < 0 || cz >= per) continue;
                for (int cx = pcx - r; cx <= pcx + r; cx++)
                {
                    if (cx < 0 || cx >= per) continue;
                    float dx = (cx + 0.5f) * size - p.x, dz = (cz + 0.5f) * size - p.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > maxD2) continue;
                    float dist = Mathf.Sqrt(d2);
                    int lod = LodFor(dist);
                    long key = Key(cx, cz);
                    if (!_chunks.TryGetValue(key, out ChunkView v))
                    {
                        v = _pool.Count > 0 ? _pool.Pop() : NewView();
                        v.Cx = cx;
                        v.Cz = cz;
                        v.Lod = -1;
                        v.Building = false;
                        v.Go.name = $"Chunk {cx},{cz}";
                        v.Go.transform.position = new Vector3(cx * size, 0, cz * size);
                        _chunks.Add(key, v);
                    }
                    v.WantedLod = lod;
                    if (dist < 300f)
                    {
                        nearTotal++;
                        if (v.Lod == 0) nearReady++;
                    }
                    if (v.Lod != lod && !v.Building) _requests.Add((dist, v));
                }
            }
            NearReadyFraction = nearTotal == 0 ? 1f : nearReady / (float)nearTotal;

            _remove.Clear();
            float keep2 = (ViewDistance + size * 1.5f) * (ViewDistance + size * 1.5f);
            foreach (KeyValuePair<long, ChunkView> kv in _chunks)
            {
                ChunkView v = kv.Value;
                float dx = (v.Cx + 0.5f) * size - p.x, dz = (v.Cz + 0.5f) * size - p.z;
                if (dx * dx + dz * dz > keep2 && !v.Building) _remove.Add(kv.Key);
            }
            foreach (long k in _remove)
            {
                ChunkView v = _chunks[k];
                _chunks.Remove(k);
                v.Go.SetActive(false);
                v.Lod = -1;
                _pool.Push(v);
            }
            _requests.Sort((a, b) => a.dist.CompareTo(b.dist));
        }

        private ChunkView NewView()
        {
            var go = new GameObject("Chunk");
            go.transform.SetParent(transform, false);
            var v = new ChunkView { Go = go };
            v.Filter = go.AddComponent<MeshFilter>();
            v.Renderer = go.AddComponent<MeshRenderer>();
            v.Renderer.sharedMaterial = _material;
            v.Renderer.shadowCastingMode = ShadowCastingMode.Off; // terrain receives, does not cast (cheap)
            v.Renderer.receiveShadows = true;
            v.Mesh = new Mesh();
            v.Mesh.MarkDynamic();
            v.Filter.sharedMesh = v.Mesh;
            go.SetActive(false);
            return v;
        }

        private void Dispatch()
        {
            int i = 0;
            while (_running < MaxConcurrentBuilds && i < _requests.Count)
            {
                ChunkView v = _requests[i++].view;
                if (v.Building || v.Lod == v.WantedLod) continue;
                long key = Key(v.Cx, v.Cz);
                if (!_chunks.TryGetValue(key, out ChunkView current) || current != v) continue;
                v.Building = true;
                int cx = v.Cx, cz = v.Cz, lod = v.WantedLod;
                Interlocked.Increment(ref _running);
                WorldRuntime world = _world;
                CancellationToken ct = _cts.Token;
                Task.Run(() =>
                {
                    try
                    {
                        if (!ct.IsCancellationRequested) _results.Enqueue(TerrainMeshes.Build(world, cx, cz, lod));
                    }
                    catch (System.Exception e)
                    {
                        Log.Exception("Terrain", e);
                        _results.Enqueue(new ChunkMeshData { Cx = cx, Cz = cz, Lod = -1 });
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _running);
                    }
                }, ct);
            }
            if (i > 0) _requests.RemoveRange(0, Mathf.Min(i, _requests.Count));
        }

        private void Upload()
        {
            int budget = MaxUploadsPerFrame;
            while (budget > 0 && _results.TryDequeue(out ChunkMeshData d))
            {
                if (!_chunks.TryGetValue(Key(d.Cx, d.Cz), out ChunkView v))
                {
                    continue; // chunk was recycled while building
                }
                v.Building = false;
                if (d.Lod < 0) continue;
                TerrainMeshes.ToMesh(d, v.Mesh);
                v.Lod = d.Lod;
                if (!v.Go.activeSelf) v.Go.SetActive(true);
                budget--;
            }
        }

        private void OnDestroy()
        {
            _cts.Cancel();
            foreach (ChunkView v in _chunks.Values) if (v.Mesh != null) Destroy(v.Mesh);
            foreach (ChunkView v in _pool) if (v.Mesh != null) Destroy(v.Mesh);
            if (_far != null) Destroy(_far.GetComponent<MeshFilter>().sharedMesh);
            if (_farMaterial != null) Destroy(_farMaterial);
        }
    }
}
