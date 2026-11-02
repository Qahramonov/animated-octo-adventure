using System.Collections.Generic;
using Survival.World;
using UnityEngine;

namespace Survival.Client.Rendering
{
    /// <summary>
    /// Procedural low-poly models (original art, generated at runtime) with two LODs each.
    /// Replace with authored models later by swapping the meshes returned here.
    /// </summary>
    public static class Models
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();
        private static readonly MeshBuilder B = new MeshBuilder();

        public static Mesh Get(NodeKind kind, int lod)
        {
            string key = kind + "_" + lod;
            if (Cache.TryGetValue(key, out Mesh m)) return m;
            B.Clear();
            switch (kind)
            {
                case NodeKind.TreePine: Pine(lod); break;
                case NodeKind.TreeBroadleaf: Broadleaf(lod); break;
                case NodeKind.TreeDead: DeadTree(lod); break;
                case NodeKind.Bush: Bush(lod); break;
                case NodeKind.Boulder: Rock(lod, MeshBuilder.Rgb(0.46f, 0.45f, 0.43f), default, 0); break;
                case NodeKind.StoneNode: Rock(lod, MeshBuilder.Rgb(0.55f, 0.54f, 0.52f), MeshBuilder.Rgb(0.7f, 0.7f, 0.68f), 1); break;
                case NodeKind.MetalNode: Rock(lod, MeshBuilder.Rgb(0.42f, 0.38f, 0.36f), MeshBuilder.Rgb(0.72f, 0.45f, 0.30f), 2); break;
                case NodeKind.SulfurNode: Rock(lod, MeshBuilder.Rgb(0.5f, 0.48f, 0.42f), MeshBuilder.Rgb(0.95f, 0.85f, 0.22f), 3); break;
                default: B.Box(Vector3.up * 0.5f, Vector3.one, MeshBuilder.Rgb(1, 0, 1)); break;
            }
            m = B.ToMesh(key);
            Cache[key] = m;
            return m;
        }

        private static void Pine(int lod)
        {
            Color32 bark = MeshBuilder.Rgb(0.33f, 0.22f, 0.14f);
            B.Cylinder(Vector3.zero, 2.2f, 0.32f, 0.2f, lod == 0 ? 6 : 4, bark, false);
            if (lod == 0)
            {
                float[] heights = { 1.6f, 3.4f, 5.0f, 6.4f };
                float[] radii = { 2.3f, 1.85f, 1.35f, 0.85f };
                for (int i = 0; i < heights.Length; i++)
                {
                    float sway = 0.25f + i * 0.22f;
                    Color32 dark = MeshBuilder.Rgb(0.10f + i * 0.015f, 0.26f + i * 0.03f, 0.15f, sway);
                    Color32 light = MeshBuilder.Rgb(0.16f + i * 0.02f, 0.38f + i * 0.03f, 0.19f, sway + 0.1f);
                    B.Cylinder(new Vector3(0, heights[i], 0), 2.3f - i * 0.25f, radii[i], 0f, 7, dark, false, false, light, i * 0.5f);
                }
            }
            else
            {
                B.Cylinder(new Vector3(0, 1.6f, 0), 6.0f, 2.2f, 0f, 5, MeshBuilder.Rgb(0.12f, 0.30f, 0.16f, 0.4f), false, false,
                    MeshBuilder.Rgb(0.17f, 0.4f, 0.2f, 0.8f));
            }
        }

        private static void Broadleaf(int lod)
        {
            Color32 bark = MeshBuilder.Rgb(0.36f, 0.27f, 0.18f);
            B.Cylinder(Vector3.zero, 3.2f, 0.36f, 0.24f, lod == 0 ? 6 : 4, bark, false);
            if (lod == 0)
            {
                B.Cylinder(new Vector3(0, 2.4f, 0), 1.4f, 0.14f, 0.08f, 4, bark, false); // branch stub
                Vector3[] blobs = { new Vector3(0, 4.6f, 0), new Vector3(1.1f, 4.0f, 0.4f), new Vector3(-0.9f, 4.2f, -0.6f), new Vector3(0.2f, 5.4f, -0.3f), new Vector3(-0.4f, 3.9f, 1.0f) };
                float[] sizes = { 2.0f, 1.4f, 1.5f, 1.3f, 1.3f };
                for (int i = 0; i < blobs.Length; i++)
                {
                    Color32 leaf = MeshBuilder.Rgb(0.20f + i * 0.02f, 0.42f + i * 0.025f, 0.16f, 0.5f + i * 0.08f);
                    B.Blob(blobs[i], new Vector3(sizes[i], sizes[i] * 0.85f, sizes[i]), leaf, 0, 0.18f, 11 + i, 0.12f);
                }
            }
            else
            {
                B.Blob(new Vector3(0, 4.5f, 0), new Vector3(2.4f, 2.0f, 2.4f), MeshBuilder.Rgb(0.24f, 0.46f, 0.18f, 0.6f), 0, 0.1f, 3, 0.1f);
            }
        }

        private static void DeadTree(int lod)
        {
            Color32 wood = MeshBuilder.Rgb(0.42f, 0.36f, 0.30f);
            B.Cylinder(Vector3.zero, 4.5f, 0.3f, 0.1f, lod == 0 ? 5 : 4, wood, false);
            if (lod == 0)
            {
                B.Transform = Matrix4x4.TRS(new Vector3(0, 2.6f, 0), Quaternion.Euler(0, 0, 50), Vector3.one);
                B.Cylinder(Vector3.zero, 1.6f, 0.1f, 0.03f, 4, wood, false);
                B.Transform = Matrix4x4.TRS(new Vector3(0, 3.2f, 0), Quaternion.Euler(-40, 120, 0), Vector3.one);
                B.Cylinder(Vector3.zero, 1.3f, 0.08f, 0.03f, 4, wood, false);
                B.Transform = Matrix4x4.identity;
            }
        }

        private static void Bush(int lod)
        {
            Color32 leaf = MeshBuilder.Rgb(0.22f, 0.40f, 0.17f, 0.7f);
            B.Blob(new Vector3(0, 0.5f, 0), new Vector3(0.9f, 0.6f, 0.9f), leaf, 0, 0.2f, 5, 0.15f);
            if (lod == 0)
            {
                B.Blob(new Vector3(0.5f, 0.4f, 0.3f), new Vector3(0.6f, 0.45f, 0.6f), MeshBuilder.Rgb(0.26f, 0.45f, 0.19f, 0.8f), 0, 0.2f, 6, 0.15f);
                B.Blob(new Vector3(-0.45f, 0.35f, -0.2f), new Vector3(0.55f, 0.4f, 0.55f), MeshBuilder.Rgb(0.2f, 0.37f, 0.15f, 0.8f), 0, 0.2f, 7, 0.15f);
            }
        }

        private static void Rock(int lod, Color32 stone, Color32 vein, int variant)
        {
            B.Blob(new Vector3(0, 0.45f, 0), new Vector3(1.0f, 0.75f, 0.9f), stone, lod == 0 ? 1 : 0, 0.22f, 20 + variant, 0.12f);
            if (variant > 0 && lod == 0)
            {
                // Mineral veins / crystals so resource nodes read differently from plain boulders
                for (int i = 0; i < 5; i++)
                {
                    float a = i * 1.3f + variant;
                    Vector3 p = new Vector3(Mathf.Cos(a) * 0.62f, 0.55f + (i % 2) * 0.25f, Mathf.Sin(a) * 0.55f);
                    B.Blob(p, new Vector3(0.24f, 0.18f, 0.24f), vein, 0, 0.3f, 40 + i + variant * 7, 0.2f);
                }
            }
        }

        /// <summary>Faceted grass tuft (for close vegetation, Medium/High quality).</summary>
        public static Mesh GrassTuft()
        {
            if (Cache.TryGetValue("grass", out Mesh m)) return m;
            B.Clear();
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.256f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Vector3 side = new Vector3(-dir.z, 0, dir.x) * 0.06f;
                Vector3 tip = dir * 0.18f + Vector3.up * (0.45f + (i % 2) * 0.15f);
                Color32 c = MeshBuilder.Rgb(0.32f, 0.52f + i * 0.02f, 0.2f, 1f);
                B.Tri(-side, tip, side, c);
                B.Tri(side, tip, -side, c);
            }
            m = B.ToMesh("grass");
            Cache["grass"] = m;
            return m;
        }

        /// <summary>
        /// OROL emblem: bevelled hexagonal frame with a twin-peak island and a rising sun.
        /// Built facing -Z (towards a default camera).
        /// </summary>
        public static Mesh Emblem()
        {
            if (Cache.TryGetValue("emblem", out Mesh m)) return m;
            B.Clear();
            Color32 metal = MeshBuilder.Rgb(0.78f, 0.80f, 0.84f);
            Color32 metalDark = MeshBuilder.Rgb(0.42f, 0.45f, 0.5f);
            Color32 ember = MeshBuilder.Rgb(1f, 0.55f, 0.16f);
            Color32 sea = MeshBuilder.Rgb(0.2f, 0.72f, 0.72f);
            const float depth = 0.18f;
            HexRing(1.0f, 0.84f, depth, metal, metalDark);
            HexRing(0.8f, 0.76f, depth * 0.6f, ember, ember);
            // Twin peaks
            Prism(new[] { new Vector2(-0.55f, -0.30f), new Vector2(-0.12f, 0.38f), new Vector2(0.18f, -0.30f) }, depth * 0.8f, metal, metalDark);
            Prism(new[] { new Vector2(-0.02f, -0.30f), new Vector2(0.30f, 0.16f), new Vector2(0.56f, -0.30f) }, depth * 0.7f, metalDark, metalDark);
            // Sun
            Disc(new Vector2(0.28f, 0.42f), 0.13f, depth * 0.5f, ember);
            // Waves
            Prism(new[] { new Vector2(-0.62f, -0.38f), new Vector2(0.62f, -0.38f), new Vector2(0.62f, -0.46f), new Vector2(-0.62f, -0.46f) }, depth * 0.5f, sea, sea);
            Prism(new[] { new Vector2(-0.45f, -0.52f), new Vector2(0.45f, -0.52f), new Vector2(0.45f, -0.58f), new Vector2(-0.45f, -0.58f) }, depth * 0.4f, sea, sea);
            m = B.ToMesh("emblem");
            Cache["emblem"] = m;
            return m;
        }

        private static Vector3 P(Vector2 p, float z) => new Vector3(p.x, p.y, z);

        private static void HexRing(float outer, float inner, float depth, Color32 face, Color32 side)
        {
            for (int i = 0; i < 6; i++)
            {
                float a0 = Mathf.PI / 2f + i * Mathf.PI / 3f, a1 = a0 + Mathf.PI / 3f;
                Vector2 o0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * outer, o1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * outer;
                Vector2 i0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * inner, i1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * inner;
                float zf = -depth * 0.5f, zb = depth * 0.5f;
                // Angles increase counter-clockwise as seen from -Z, so the front face lists the
                // inner edge first to stay clockwise (front-facing) for the viewer.
                B.Quad(P(i0, zf), P(i1, zf), P(o1, zf), P(o0, zf), face);       // front (-Z)
                B.Quad(P(o0, zb), P(o1, zb), P(i1, zb), P(i0, zb), side);       // back
                B.Quad(P(o0, zf), P(o1, zf), P(o1, zb), P(o0, zb), side);       // outer rim
                B.Quad(P(i0, zb), P(i1, zb), P(i1, zf), P(i0, zf), MeshBuilder.Shade(side, 0.8f)); // inner rim
            }
        }

        /// <summary>Extruded convex polygon (points clockwise when seen from -Z).</summary>
        private static void Prism(Vector2[] pts, float depth, Color32 face, Color32 side)
        {
            float zf = -depth * 0.5f, zb = depth * 0.5f;
            for (int i = 1; i < pts.Length - 1; i++)
            {
                B.Tri(P(pts[0], zf), P(pts[i], zf), P(pts[i + 1], zf), face);
                B.Tri(P(pts[0], zb), P(pts[i + 1], zb), P(pts[i], zb), side);
            }
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
                B.Quad(P(a, zb), P(b, zb), P(b, zf), P(a, zf), side);
            }
        }

        private static void Disc(Vector2 c, float r, float depth, Color32 col)
        {
            var pts = new Vector2[12];
            for (int i = 0; i < pts.Length; i++)
            {
                float a = -i * Mathf.PI * 2f / pts.Length;
                pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            Prism(pts, depth, col, MeshBuilder.Shade(col, 0.7f));
        }

        /// <summary>Inside-out sphere for the sky dome.</summary>
        public static Mesh SkyDome()
        {
            if (Cache.TryGetValue("sky", out Mesh m)) return m;
            B.Clear();
            B.Blob(Vector3.zero, Vector3.one, new Color32(255, 255, 255, 0), 1);
            m = B.ToMesh("sky");
            Cache["sky"] = m;
            return m;
        }

        /// <summary>Camera-facing quad (for glow sprites), centred, size 1.</summary>
        public static Mesh Quad()
        {
            if (Cache.TryGetValue("quad", out Mesh m)) return m;
            m = new Mesh { name = "quad" };
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            m.SetColors(new List<Color32> { Color.white, Color.white, Color.white, Color.white });
            m.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            Cache["quad"] = m;
            return m;
        }
    }
}
