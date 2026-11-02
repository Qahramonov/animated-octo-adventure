using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Survival.Client.Rendering
{
    /// <summary>
    /// Builds faceted low-poly meshes with vertex colours (alpha = wind sway weight) from simple
    /// primitives. Every model in Phase 1 (trees, rocks, characters, logo) is generated with it.
    /// </summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> _v = new List<Vector3>(512);
        private readonly List<Vector3> _n = new List<Vector3>(512);
        private readonly List<Color32> _c = new List<Color32>(512);
        private readonly List<int> _t = new List<int>(1024);

        public Matrix4x4 Transform = Matrix4x4.identity;
        public int VertexCount => _v.Count;

        public void Clear()
        {
            _v.Clear();
            _n.Clear();
            _c.Clear();
            _t.Clear();
            Transform = Matrix4x4.identity;
        }

        /// <summary>Adds a flat-shaded triangle (own vertices, face normal).</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color32 col)
        {
            a = Transform.MultiplyPoint3x4(a);
            b = Transform.MultiplyPoint3x4(b);
            c = Transform.MultiplyPoint3x4(c);
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            _n.Add(n); _n.Add(n); _n.Add(n);
            _c.Add(col); _c.Add(col); _c.Add(col);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col)
        {
            Tri(a, b, c, col);
            Tri(a, c, d, col);
        }

        /// <summary>Axis-aligned box (in builder space) centred at <paramref name="center"/>.</summary>
        public void Box(Vector3 center, Vector3 size, Color32 col, float taperTop = 1f)
        {
            Vector3 h = size * 0.5f;
            Vector3 ht = new Vector3(h.x * taperTop, h.y, h.z * taperTop);
            Vector3 p000 = center + new Vector3(-h.x, -h.y, -h.z), p100 = center + new Vector3(h.x, -h.y, -h.z);
            Vector3 p001 = center + new Vector3(-h.x, -h.y, h.z), p101 = center + new Vector3(h.x, -h.y, h.z);
            Vector3 p010 = center + new Vector3(-ht.x, h.y, -ht.z), p110 = center + new Vector3(ht.x, h.y, -ht.z);
            Vector3 p011 = center + new Vector3(-ht.x, h.y, ht.z), p111 = center + new Vector3(ht.x, h.y, ht.z);
            Quad(p000, p010, p110, p100, col);  // -Z
            Quad(p101, p111, p011, p001, col);  // +Z
            Quad(p001, p011, p010, p000, col);  // -X
            Quad(p100, p110, p111, p101, col);  // +X
            Quad(p010, p011, p111, p110, Shade(col, 1.08f)); // top
            Quad(p001, p000, p100, p101, Shade(col, 0.8f));  // bottom
        }

        /// <summary>Cylinder/cone frustum along +Y.</summary>
        public void Cylinder(Vector3 bottom, float height, float rBottom, float rTop, int segments, Color32 col,
            bool capTop = true, bool capBottom = false, Color32? topCol = null, float twist = 0f)
        {
            Color32 tc = topCol ?? col;
            for (int s = 0; s < segments; s++)
            {
                float a0 = (s / (float)segments) * Mathf.PI * 2f + twist;
                float a1 = ((s + 1) / (float)segments) * Mathf.PI * 2f + twist;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 b0 = bottom + d0 * rBottom, b1 = bottom + d1 * rBottom;
                Vector3 t0 = bottom + Vector3.up * height + d0 * rTop, t1 = bottom + Vector3.up * height + d1 * rTop;
                // Winding: Cross(b - a, c - a) must point outwards (Unity front faces are clockwise)
                if (rTop > 0.0001f) Quad(b0, t0, t1, b1, Lerp(col, tc, 0.5f));
                else Tri(b0, t0, b1, Lerp(col, tc, 0.5f));
                if (capTop && rTop > 0.0001f) Tri(bottom + Vector3.up * height, t1, t0, tc);
                if (capBottom) Tri(bottom, b0, b1, Shade(col, 0.7f));
            }
        }

        /// <summary>Low-poly sphere (icosahedron, optionally subdivided once) with per-vertex jitter.</summary>
        public void Blob(Vector3 center, Vector3 radius, Color32 col, int subdiv = 0, float jitter = 0f, int seed = 0,
            float colorVariance = 0f)
        {
            List<Vector3> verts;
            List<int> tris;
            Icosphere(subdiv, out verts, out tris);
            var rng = new System.Random(seed);
            var jittered = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                float j = 1f + ((float)rng.NextDouble() * 2f - 1f) * jitter;
                jittered[i] = center + Vector3.Scale(verts[i] * j, radius);
            }
            for (int i = 0; i < tris.Count; i += 3)
            {
                Color32 c = colorVariance > 0 ? Shade(col, 1f + ((float)rng.NextDouble() * 2f - 1f) * colorVariance) : col;
                Tri(jittered[tris[i]], jittered[tris[i + 1]], jittered[tris[i + 2]], c);
            }
        }

        private static List<Vector3> _icoV0, _icoV1;
        private static List<int> _icoT0, _icoT1;

        private static void Icosphere(int subdiv, out List<Vector3> v, out List<int> t)
        {
            if (_icoV0 == null) BuildIco();
            if (subdiv <= 0)
            {
                v = _icoV0;
                t = _icoT0;
            }
            else
            {
                v = _icoV1;
                t = _icoT1;
            }
        }

        private static void BuildIco()
        {
            float p = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, p, 0), new Vector3(1, p, 0), new Vector3(-1, -p, 0), new Vector3(1, -p, 0),
                new Vector3(0, -1, p), new Vector3(0, 1, p), new Vector3(0, -1, -p), new Vector3(0, 1, -p),
                new Vector3(p, 0, -1), new Vector3(p, 0, 1), new Vector3(-p, 0, -1), new Vector3(-p, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            // Classic icosahedron index list; with Unity's left-handed clockwise convention these
            // faces already point outwards (verified: Cross(b - a, c - a) . centroid > 0).
            var t = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            _icoV0 = v;
            _icoT0 = t;

            var v1 = new List<Vector3>(v);
            var t1 = new List<int>();
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int idx)) return idx;
                v1.Add(((v1[a] + v1[b]) * 0.5f).normalized);
                mid[key] = v1.Count - 1;
                return v1.Count - 1;
            }
            for (int i = 0; i < t.Count; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                t1.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            _icoV1 = v1;
            _icoT1 = t1;
        }

        public Mesh ToMesh(string name, bool recalcBounds = true, bool keepReadable = false)
        {
            var m = new Mesh { name = name };
            if (_v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetColors(_c);
            m.SetTriangles(_t, 0, recalcBounds);
            m.UploadMeshData(!keepReadable);
            return m;
        }

        public static Color32 Shade(Color32 c, float k) =>
            new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), c.a);

        public static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);

        public static Color32 Rgb(float r, float g, float b, float sway = 0f) =>
            new Color32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), (byte)(sway * 255));
    }
}
