using Survival.Client.Rendering;
using Survival.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Survival.Client.WorldView
{
    /// <summary>
    /// Ocean surface: a grid that follows the camera (snapped to avoid swimming waves) at sea level.
    /// The water shader gets a low-res terrain height texture to tint shallow water and draw foam
    /// along the shore without needing a depth texture.
    /// </summary>
    public sealed class WaterSurface : MonoBehaviour
    {
        private static readonly int HeightTexId = Shader.PropertyToID("_HeightTex");
        private static readonly int WorldSizeId = Shader.PropertyToID("_WorldSize");

        private Transform _follow;
        private float _snap;
        private Texture2D _heightTex;

        /// <param name="heightTex">Terrain height texture from <see cref="CreateHeightTexture"/> or null (deep water everywhere).</param>
        public static WaterSurface Create(Transform follow, float seaLevel, float size, int resolution, Texture2D heightTex, float worldSize)
        {
            var go = new GameObject("[Ocean]");
            var w = go.AddComponent<WaterSurface>();
            w._follow = follow;
            w._snap = size / resolution;
            go.transform.position = new Vector3(0, seaLevel, 0);
            go.AddComponent<MeshFilter>().sharedMesh = Grid(size, resolution);
            var r = go.AddComponent<MeshRenderer>();
            Material mat = new Material(App.Materials.Water("water"));
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (heightTex != null)
            {
                w._heightTex = heightTex;
                mat.SetTexture(HeightTexId, heightTex);
                mat.SetFloat(WorldSizeId, worldSize);
            }
            else
            {
                mat.SetFloat(WorldSizeId, 1f); // positions fall outside the uv range -> deep water
            }
            return w;
        }

        private static Mesh Grid(float size, int res)
        {
            int n = res + 1;
            var v = new Vector3[n * n];
            float step = size / res;
            float half = size * 0.5f;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    v[z * n + x] = new Vector3(x * step - half, 0, z * step - half);
            var idx = new int[res * res * 6];
            int t = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i00 = z * n + x, i10 = i00 + 1, i01 = i00 + n, i11 = i01 + 1;
                    idx[t++] = i00; idx[t++] = i01; idx[t++] = i11;
                    idx[t++] = i00; idx[t++] = i11; idx[t++] = i10;
                }
            }
            var m = new Mesh { name = "ocean", indexFormat = v.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.vertices = v;
            m.triangles = idx;
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.zero, new Vector3(size, 10f, size));
            return m;
        }

        /// <summary>
        /// Terrain height relative to sea level (metres) on a res x res grid over the world.
        /// Pure maths: call it from a worker thread during loading.
        /// </summary>
        public static float[] ComputeHeightGrid(WorldRuntime world, int res)
        {
            var data = new float[res * res];
            HeightField hf = world.Heights;
            int n = hf.SamplesPerSide - 1;
            float sea = world.Config.SeaLevel;
            for (int z = 0; z < res; z++)
            {
                int iz = (int)((z + 0.5f) / res * n);
                for (int x = 0; x < res; x++)
                {
                    int ix = (int)((x + 0.5f) / res * n);
                    data[z * res + x] = hf.GetSample(ix, iz) - sea;
                }
            }
            return data;
        }

        /// <summary>Uploads a height grid as an RHalf texture (main thread).</summary>
        public static Texture2D CreateHeightTexture(float[] grid, int res)
        {
            bool halfOk = SystemInfo.SupportsTextureFormat(TextureFormat.RHalf);
            var tex = new Texture2D(res, res, halfOk ? TextureFormat.RHalf : TextureFormat.RGBAFloat, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "terrain_height",
            };
            var px = new Color[res * res];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(grid[i], 0, 0, 1);
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        private void LateUpdate()
        {
            if (_follow == null) return;
            Vector3 p = _follow.position;
            transform.position = new Vector3(Mathf.Round(p.x / _snap) * _snap, transform.position.y, Mathf.Round(p.z / _snap) * _snap);
        }

        private void OnDestroy()
        {
            if (_heightTex != null) Destroy(_heightTex);
        }
    }
}
