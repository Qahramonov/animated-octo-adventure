using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Survival.Editor
{
    /// <summary>
    /// Import rules for third-party art (Assets/ThirdParty/**): mobile texture sizes and compression,
    /// correct normal-map / data-map flags, and lean model import (no embedded materials, cameras or
    /// lights, non-readable meshes). World textures and UI textures get different settings.
    /// </summary>
    public sealed class ThirdPartyImportRules : AssetPostprocessor
    {
        public const string Root = "Assets/ThirdParty/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var ti = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            bool ui = assetPath.Contains("/UI/");
            bool normal = name.EndsWith("_nor") || name.EndsWith("_normal");
            bool data = name.EndsWith("_arm") || name.EndsWith("_rough") || name.EndsWith("_metal") || name.EndsWith("_ao");

            ti.isReadable = false;
            bool atlas = name.EndsWith("_atlas");
            if (atlas)
            {
                // Foliage atlas: cut-out alpha that must keep its coverage in the mip maps (thin needles far away)
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false; // colours are already bled into the transparent area by make_trees.py
                ti.mipmapEnabled = true;
                ti.mipMapsPreserveCoverage = true;
                ti.alphaTestReferenceValue = 0.38f;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 2;
                ti.maxTextureSize = 1024;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "Android", overridden = true, maxTextureSize = 1024, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50,
                });
                return;
            }
            if (ui)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = 1024;
            }
            else
            {
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal && !data;
                ti.mipmapEnabled = true;
                ti.streamingMipmaps = false;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 4;
                ti.maxTextureSize = data ? 512 : 1024;
            }
            // ASTC on Android (every device that runs the game supports it; ETC2 is the automatic fallback)
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = ti.maxTextureSize,
                format = ui ? TextureImporterFormat.ASTC_4x4 : normal ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6,
                compressionQuality = 50,
            });
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.None; // materials are created by the game (OROL shaders)
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.importBlendShapes = false;
            // World objects (*_lod0/_lod1) are merged into block meshes at runtime, which needs readable meshes
            mi.isReadable = System.IO.Path.GetFileNameWithoutExtension(assetPath).Contains("_lod");
            mi.meshCompression = ModelImporterMeshCompression.Low;
            mi.optimizeMeshVertices = true;
            mi.optimizeMeshPolygons = true;
            mi.weldVertices = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.globalScale = 1f;
            mi.useFileScale = false;
        }
    }

    /// <summary>
    /// Scans imported art and reports problems: missing texture sets, oversized or uncompressed
    /// textures, readable meshes, models over the mobile triangle budget, missing LODs, materials
    /// with broken shaders and missing scripts on prefabs. Menu: OROL / Validate Imported Assets.
    /// CI: -executeMethod Survival.Editor.AssetImportValidator.ValidateOrFail
    /// </summary>
    public static class AssetImportValidator
    {
        public const int MaxWorldTexture = 2048;
        public const int MaxPropTriangles = 3000;
        public const int MaxLod0Triangles = 1200;

        public sealed class Report
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public int Textures, Models, Materials, Prefabs;
            public long TextureBytes;

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[Assets] {Textures} textures (~{TextureBytes / (1024f * 1024f):0.0} MB on disk), {Models} models, {Materials} materials, {Prefabs} prefabs");
                foreach (string e in Errors) sb.AppendLine("[Assets] ERROR " + e);
                foreach (string w in Warnings) sb.AppendLine("[Assets] WARN  " + w);
                sb.AppendLine(Errors.Count == 0 ? "[Assets] OK" : $"[Assets] {Errors.Count} error(s)");
                return sb.ToString();
            }
        }

        [MenuItem("OROL/Validate Imported Assets")]
        public static void ValidateMenu() => Debug.Log(Validate().ToString());

        public static void ValidateOrFail()
        {
            Report r = Validate();
            Debug.Log(r.ToString());
            EditorApplication.Exit(r.Errors.Count == 0 ? 0 : 1);
        }

        public static Report Validate(string root = "Assets/ThirdParty")
        {
            var r = new Report();
            if (!AssetDatabase.IsValidFolder(root)) return r;
            string[] folders = { root };

            // ---- textures ----
            var textureNames = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (ti == null || tex == null) continue;
                r.Textures++;
                r.TextureBytes += new FileInfo(path).Length;
                string name = Path.GetFileNameWithoutExtension(path);
                textureNames.Add(name);
                if (Mathf.Max(tex.width, tex.height) > MaxWorldTexture) r.Errors.Add($"{path}: {tex.width}x{tex.height} is larger than {MaxWorldTexture}");
                if (ti.isReadable) r.Warnings.Add($"{path}: Read/Write is enabled (doubles memory)");
                bool ui = ti.textureType == TextureImporterType.Sprite;
                if (!ui && !ti.mipmapEnabled) r.Warnings.Add($"{path}: world texture without mip maps");
                if (ui && ti.mipmapEnabled) r.Warnings.Add($"{path}: UI texture with mip maps");
                if (name.EndsWith("_nor") && ti.textureType != TextureImporterType.NormalMap) r.Errors.Add($"{path}: normal map is not imported as Normal Map");
                if (name.EndsWith("_arm") && ti.sRGBTexture) r.Errors.Add($"{path}: data map imported as sRGB");
                TextureImporterPlatformSettings android = ti.GetPlatformTextureSettings("Android");
                if (!android.overridden) r.Warnings.Add($"{path}: no Android compression override (ASTC expected)");
                if (!Mathf.IsPowerOfTwo(tex.width) || !Mathf.IsPowerOfTwo(tex.height)) r.Warnings.Add($"{path}: non power-of-two size {tex.width}x{tex.height}");
            }
            // A PBR set needs all three maps
            foreach (string name in textureNames.Where(n => n.EndsWith("_diff")))
            {
                string stem = name.Substring(0, name.Length - 5);
                if (!textureNames.Contains(stem + "_nor")) r.Warnings.Add($"{stem}: albedo without a normal map");
                if (!textureNames.Contains(stem + "_arm")) r.Warnings.Add($"{stem}: albedo without an AO/roughness/metallic map");
            }

            // ---- models ----
            var meshNames = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                r.Models++;
                string name = Path.GetFileNameWithoutExtension(path);
                meshNames.Add(name);
                int tris = 0;
                bool uv = true, normals = true;
                foreach (Mesh m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>())
                {
                    tris += (int)(m.GetIndexCount(0) / 3);
                    for (int s = 1; s < m.subMeshCount; s++) tris += (int)(m.GetIndexCount(s) / 3);
                    if (m.uv == null || m.uv.Length == 0) uv = false;
                    if (m.normals == null || m.normals.Length == 0) normals = false;
                    if (m.isReadable && !name.Contains("_lod")) r.Warnings.Add($"{path}: mesh '{m.name}' is readable");
                    Vector3 size = m.bounds.size;
                    if (size.magnitude > 60f || size.magnitude < 0.01f) r.Warnings.Add($"{path}: suspicious scale, bounds {size}");
                }
                if (tris == 0) r.Errors.Add($"{path}: no mesh geometry");
                if (!normals) r.Errors.Add($"{path}: no normals");
                if (!uv) r.Warnings.Add($"{path}: no UVs (only usable with the triplanar material)");
                if (tris > MaxPropTriangles) r.Errors.Add($"{path}: {tris} triangles (budget {MaxPropTriangles})");
                else if (name.EndsWith("_lod0") && tris > MaxLod0Triangles) r.Warnings.Add($"{path}: {tris} triangles for LOD0 (budget {MaxLod0Triangles})");
                if (mi != null && mi.materialImportMode != ModelImporterMaterialImportMode.None) r.Warnings.Add($"{path}: embedded materials are imported");
                if (mi != null && mi.importAnimation && mi.animationType != ModelImporterAnimationType.None && AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Count() == 0)
                    r.Warnings.Add($"{path}: rig imported but no animation clips");
            }
            // World objects (sit on the ground, drawn many times) need a second LOD
            foreach (string name in meshNames.Where(n => n.EndsWith("_lod0") && (n.Contains("boulder") || n.Contains("rock") || n.Contains("tree") || n.Contains("crate") || n.Contains("barrel"))))
            {
                if (!meshNames.Contains(name.Replace("_lod0", "_lod1"))) r.Warnings.Add($"{name}: no LOD1 mesh");
            }

            // ---- materials / prefabs ----
            foreach (string guid in AssetDatabase.FindAssets("t:Material", folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                r.Materials++;
                if (mat == null || mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader") r.Errors.Add($"{path}: missing or broken shader");
                else if (mat.shader.name == "Standard") r.Warnings.Add($"{path}: Built-in Standard shader in a URP project");
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                r.Prefabs++;
                if (go == null) continue;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0) r.Errors.Add($"{path}: missing script on '{t.name}'");
                }
            }
            return r;
        }
    }
}
