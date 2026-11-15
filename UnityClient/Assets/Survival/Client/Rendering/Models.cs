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
            // Scanned rocks (CC0, Poly Haven) replace the procedural boulders when they are in the build
            string scan = ScanName(kind);
            if (scan != null)
            {
                m = Resources.Load<Mesh>("PH/" + scan + "_lod" + Mathf.Min(lod, 1));
                if (m != null)
                {
                    Cache[key] = m;
                    return m;
                }
            }
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
                case (NodeKind)200: Stump(); break;
                default: B.Box(Vector3.up * 0.5f, Vector3.one, MeshBuilder.Rgb(1, 0, 1)); break;
            }
            m = B.ToMesh(key, true, true); // readable: the vegetation renderer merges instances into block meshes
            Cache[key] = m;
            return m;
        }

        private static string ScanName(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.TreePine: return "tree_fir";
                case NodeKind.TreeBroadleaf: return "tree_leaf";
                case NodeKind.Bush: return "bush";
                case NodeKind.Boulder: return "boulder_01";
                case NodeKind.StoneNode: return "namaqualand_boulder_03";
                case NodeKind.MetalNode: return "namaqualand_boulder_05";
                case NodeKind.SulfurNode: return "namaqualand_boulder_02";
                default: return null;
            }
        }

        /// <summary>
        /// Material for a world node model: textured foliage / rock materials when the scanned assets are in the
        /// build, otherwise the given vertex-colour fallback (which matches the procedural fallback meshes).
        /// </summary>
        public static Material NodeMaterial(NodeKind kind, Material fallback)
        {
            string scan = ScanName(kind);
            if (scan == null || Resources.Load<Mesh>("PH/" + scan + "_lod0") == null) return fallback;
            Material m = null;
            switch (kind)
            {
                case NodeKind.TreePine: m = App.Materials.Foliage("tree_fir_atlas"); break;
                case NodeKind.TreeBroadleaf:
                case NodeKind.Bush: m = App.Materials.Foliage("tree_leaf_atlas"); break;
                case NodeKind.Boulder: m = App.Materials.Rock("rock_boulder", new Color(0.92f, 0.9f, 0.88f), Color.white, 0f); break;
                case NodeKind.StoneNode: m = App.Materials.Rock("rock_stone", new Color(1.05f, 1.05f, 1.08f), new Color(0.9f, 0.9f, 0.92f), 0.4f); break;
                case NodeKind.MetalNode: m = App.Materials.Rock("rock_metal", new Color(0.78f, 0.72f, 0.68f), new Color(0.74f, 0.4f, 0.22f), 0.62f); break;
                case NodeKind.SulfurNode: m = App.Materials.Rock("rock_sulfur", new Color(0.95f, 0.92f, 0.8f), new Color(0.96f, 0.82f, 0.12f), 0.62f); break;
            }
            return m != null ? m : fallback;
        }

        /// <summary>How a hand-held item model is placed in the hand (and for its icon).</summary>
        public struct ItemPose
        {
            public Mesh Mesh;
            public Material Material;
            public Quaternion Rotation;
            public Vector3 Offset;
            public float Scale;
        }

        private static readonly Dictionary<Survival.Gameplay.ItemId, ItemPose> Poses = new Dictionary<Survival.Gameplay.ItemId, ItemPose>();

        /// <summary>Mesh + material + pose of an item; scanned models where available, procedural otherwise.</summary>
        public static ItemPose Item(Survival.Gameplay.ItemId id)
        {
            if (Poses.TryGetValue(id, out ItemPose p) && p.Mesh != null && p.Material != null) return p;
            p = new ItemPose { Rotation = Quaternion.identity, Scale = 1f };
            string scan = id == Survival.Gameplay.ItemId.StoneHatchet ? "hatchet" : id == Survival.Gameplay.ItemId.StonePickaxe ? "picke_dirty_01" : null;
            if (scan != null)
            {
                Mesh mesh = Resources.Load<Mesh>("PH/" + scan + "_lod0");
                Material mat = App.Materials.Textured(scan);
                if (mesh != null && mat != null)
                {
                    // Scans stand upright (handle along +Y, head on top): lay them along +Z, grip at the origin
                    p.Mesh = mesh;
                    p.Material = mat;
                    p.Rotation = Quaternion.Euler(90f, 0f, 0f);
                    p.Scale = scan == "hatchet" ? 1.9f : 0.62f;
                    p.Offset = new Vector3(0, 0, scan == "hatchet" ? 0.06f : 0.16f);
                }
            }
            if (p.Mesh == null && (id == Survival.Gameplay.ItemId.Rock || id == Survival.Gameplay.ItemId.Stone))
            {
                Mesh mesh = Resources.Load<Mesh>("PH/namaqualand_boulder_05_lod1");
                Material mat = App.Materials.Rock("rock_item", Color.white, Color.white, 0f);
                if (mesh != null && mat != null)
                {
                    p.Mesh = mesh;
                    p.Material = mat;
                    p.Scale = 0.11f;
                    p.Offset = new Vector3(0, -0.04f, 0.1f);
                }
            }
            if (p.Mesh == null)
            {
                p.Mesh = Tool(id);
                p.Material = App.Materials.Lit("tool", Color.white, 0.08f, 0.1f);
                p.Rotation = Quaternion.identity;
                p.Offset = Vector3.zero;
                p.Scale = 1f;
            }
            Poses[id] = p;
            return p;
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
                B.Blob(new Vector3(0.45f, 0.3f, -0.35f), new Vector3(0.6f, 0.45f, 0.55f), MeshBuilder.Shade(stone, 0.9f), 0, 0.25f, 31 + variant, 0.12f);
                for (int i = 0; i < 9; i++)
                {
                    float a = i * 0.72f + variant;
                    float up = 0.35f + (i % 3) * 0.24f;
                    float ring = 0.78f - (i % 3) * 0.14f;
                    Vector3 p = new Vector3(Mathf.Cos(a) * ring, up, Mathf.Sin(a) * ring * 0.9f);
                    float size = 0.2f + (i % 2) * 0.08f;
                    B.Blob(p, new Vector3(size, size * 0.8f, size), i % 4 == 0 ? MeshBuilder.Shade(vein, 1.15f) : vein, 0, 0.3f, 40 + i + variant * 7, 0.2f);
                }
            }
        }

        private static void Stump()
        {
            Color32 bark = MeshBuilder.Rgb(0.33f, 0.24f, 0.16f);
            Color32 cut = MeshBuilder.Rgb(0.78f, 0.64f, 0.42f);
            B.Cylinder(Vector3.zero, 0.55f, 0.4f, 0.32f, 7, bark, true, false, cut);
        }

        /// <summary>Hand-held tool / item model, grip at the origin, pointing along +Z (forward).</summary>
        public static Mesh Tool(Survival.Gameplay.ItemId id)
        {
            string key = "tool_" + id;
            if (Cache.TryGetValue(key, out Mesh m)) return m;
            B.Clear();
            Color32 wood = MeshBuilder.Rgb(0.45f, 0.31f, 0.18f);
            Color32 stone = MeshBuilder.Rgb(0.56f, 0.56f, 0.55f);
            Color32 stoneDark = MeshBuilder.Rgb(0.40f, 0.40f, 0.41f);
            Color32 cord = MeshBuilder.Rgb(0.72f, 0.63f, 0.42f);
            switch (id)
            {
                case Survival.Gameplay.ItemId.StoneHatchet:
                    Handle(0.62f, wood);
                    B.Box(new Vector3(0, 0.07f, 0.50f), new Vector3(0.05f, 0.24f, 0.13f), stone);
                    B.Box(new Vector3(0, 0.19f, 0.50f), new Vector3(0.035f, 0.10f, 0.19f), stoneDark, 0.4f);
                    B.Box(new Vector3(0, 0.0f, 0.50f), new Vector3(0.07f, 0.07f, 0.09f), cord);
                    break;
                case Survival.Gameplay.ItemId.StonePickaxe:
                    Handle(0.66f, wood);
                    B.Box(new Vector3(0, 0.0f, 0.56f), new Vector3(0.06f, 0.50f, 0.08f), stone);
                    B.Box(new Vector3(0, 0.27f, 0.56f), new Vector3(0.04f, 0.10f, 0.05f), stoneDark, 0.2f);
                    B.Box(new Vector3(0, -0.27f, 0.56f), new Vector3(0.04f, 0.10f, 0.05f), stoneDark);
                    B.Box(new Vector3(0, 0.0f, 0.56f), new Vector3(0.08f, 0.09f, 0.10f), cord);
                    break;
                case Survival.Gameplay.ItemId.Spear:
                    B.Transform = Matrix4x4.TRS(new Vector3(0, 0, -0.45f), Quaternion.Euler(90, 0, 0), Vector3.one);
                    B.Cylinder(Vector3.zero, 1.5f, 0.022f, 0.018f, 5, wood, true);
                    B.Cylinder(new Vector3(0, 1.5f, 0), 0.26f, 0.045f, 0f, 4, stone, false);
                    B.Transform = Matrix4x4.identity;
                    break;
                case Survival.Gameplay.ItemId.Bandage:
                    B.Box(new Vector3(0, 0, 0.1f), new Vector3(0.12f, 0.08f, 0.16f), MeshBuilder.Rgb(0.9f, 0.88f, 0.82f));
                    B.Box(new Vector3(0, 0.045f, 0.1f), new Vector3(0.04f, 0.01f, 0.12f), MeshBuilder.Rgb(0.8f, 0.2f, 0.18f));
                    break;
                case Survival.Gameplay.ItemId.Berries:
                    B.Blob(new Vector3(0.03f, 0, 0.1f), Vector3.one * 0.05f, MeshBuilder.Rgb(0.75f, 0.12f, 0.2f));
                    B.Blob(new Vector3(-0.04f, 0.01f, 0.13f), Vector3.one * 0.045f, MeshBuilder.Rgb(0.62f, 0.1f, 0.3f));
                    B.Blob(new Vector3(0, 0.05f, 0.08f), Vector3.one * 0.04f, MeshBuilder.Rgb(0.8f, 0.2f, 0.25f));
                    break;
                case Survival.Gameplay.ItemId.Wood:
                    B.Transform = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90, 0, 0), Vector3.one);
                    B.Cylinder(Vector3.zero, 0.4f, 0.06f, 0.06f, 6, MeshBuilder.Rgb(0.36f, 0.25f, 0.15f), true, true, MeshBuilder.Rgb(0.78f, 0.64f, 0.42f));
                    B.Transform = Matrix4x4.identity;
                    break;
                case Survival.Gameplay.ItemId.Fiber:
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * 1.05f;
                        Vector3 baseP = new Vector3(Mathf.Cos(a) * 0.03f, -0.08f, 0.1f + Mathf.Sin(a) * 0.03f);
                        Vector3 tip = baseP + new Vector3(Mathf.Cos(a) * 0.09f, 0.22f + (i % 2) * 0.05f, Mathf.Sin(a) * 0.09f);
                        Vector3 side = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * 0.022f;
                        Color32 g = MeshBuilder.Rgb(0.36f + i * 0.02f, 0.62f + i * 0.02f, 0.26f);
                        B.Tri(baseP - side, tip, baseP + side, g);
                        B.Tri(baseP + side, tip, baseP - side, g);
                    }
                    B.Box(new Vector3(0, -0.06f, 0.1f), new Vector3(0.09f, 0.03f, 0.09f), cord);
                    break;
                case Survival.Gameplay.ItemId.RawMeat:
                case Survival.Gameplay.ItemId.CookedMeat:
                {
                    bool cooked = id == Survival.Gameplay.ItemId.CookedMeat;
                    Color32 meat = cooked ? MeshBuilder.Rgb(0.52f, 0.30f, 0.16f) : MeshBuilder.Rgb(0.82f, 0.28f, 0.30f);
                    B.Blob(new Vector3(0, 0, 0.12f), new Vector3(0.11f, 0.07f, 0.13f), meat, 0, 0.2f, 9, 0.1f);
                    B.Blob(new Vector3(0.03f, 0.03f, 0.1f), new Vector3(0.05f, 0.03f, 0.06f), cooked ? MeshBuilder.Rgb(0.36f, 0.2f, 0.1f) : MeshBuilder.Rgb(0.95f, 0.75f, 0.72f));
                    B.Transform = Matrix4x4.TRS(new Vector3(0, 0, -0.02f), Quaternion.Euler(90, 0, 0), Vector3.one);
                    B.Cylinder(Vector3.zero, 0.12f, 0.018f, 0.018f, 5, MeshBuilder.Rgb(0.93f, 0.9f, 0.82f), true, true);
                    B.Transform = Matrix4x4.identity;
                    break;
                }
                case Survival.Gameplay.ItemId.Hide:
                    B.Box(new Vector3(0, 0, 0.1f), new Vector3(0.24f, 0.025f, 0.2f), MeshBuilder.Rgb(0.55f, 0.38f, 0.22f), 0.8f);
                    B.Box(new Vector3(0, 0.016f, 0.1f), new Vector3(0.15f, 0.01f, 0.12f), MeshBuilder.Rgb(0.72f, 0.55f, 0.36f));
                    break;
                case Survival.Gameplay.ItemId.Bone:
                    B.Transform = Matrix4x4.TRS(new Vector3(0, 0, -0.04f), Quaternion.Euler(90, 0, 0), Vector3.one);
                    B.Cylinder(Vector3.zero, 0.28f, 0.02f, 0.02f, 5, MeshBuilder.Rgb(0.93f, 0.9f, 0.82f), true, true);
                    B.Transform = Matrix4x4.identity;
                    B.Blob(new Vector3(0, 0, -0.04f), Vector3.one * 0.04f, MeshBuilder.Rgb(0.9f, 0.87f, 0.78f));
                    B.Blob(new Vector3(0, 0, 0.24f), Vector3.one * 0.04f, MeshBuilder.Rgb(0.9f, 0.87f, 0.78f));
                    break;
                case Survival.Gameplay.ItemId.Campfire:
                    B.Transform = Matrix4x4.TRS(new Vector3(0, -0.08f, 0.12f), Quaternion.identity, Vector3.one * 0.32f);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        B.Blob(new Vector3(Mathf.Cos(a) * 0.5f, 0.08f, Mathf.Sin(a) * 0.5f), new Vector3(0.17f, 0.12f, 0.15f), stone, 0, 0.25f, 60 + i, 0.1f);
                    }
                    B.Cylinder(new Vector3(0, 0.05f, 0), 0.55f, 0.2f, 0f, 6, MeshBuilder.Rgb(1f, 0.45f, 0.1f), false, false, MeshBuilder.Rgb(1f, 0.88f, 0.35f));
                    B.Box(new Vector3(0, 0.05f, 0), new Vector3(0.7f, 0.1f, 0.12f), wood);
                    B.Box(new Vector3(0, 0.05f, 0), new Vector3(0.12f, 0.1f, 0.7f), wood);
                    B.Transform = Matrix4x4.identity;
                    break;
                default: // rock and any other resource: a fist-sized stone
                    B.Blob(new Vector3(0, 0, 0.1f), new Vector3(0.1f, 0.075f, 0.12f), id == Survival.Gameplay.ItemId.SulfurOre ? MeshBuilder.Rgb(0.85f, 0.76f, 0.2f)
                        : id == Survival.Gameplay.ItemId.MetalOre ? MeshBuilder.Rgb(0.62f, 0.42f, 0.32f) : stone, 0, 0.25f, 3, 0.1f);
                    break;
            }
            m = B.ToMesh(key);
            Cache[key] = m;
            return m;
        }

        private static void Handle(float length, Color32 wood)
        {
            B.Transform = Matrix4x4.TRS(new Vector3(0, 0, -0.08f), Quaternion.Euler(90, 0, 0), Vector3.one);
            B.Cylinder(Vector3.zero, length, 0.024f, 0.02f, 5, wood, true, true);
            B.Transform = Matrix4x4.identity;
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
