using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Survival.Editor
{
    /// <summary>
    /// Pre-build checks that turn silent runtime problems into clear CI failures:
    /// build scenes exist and have no missing scripts, custom shaders compile, required
    /// Resources (fonts, client config, fallback materials) load, every quality level has a URP asset.
    /// </summary>
    public static class ProjectValidator
    {
        private static readonly string[] RequiredShaders = { "OROL/VertexLit", "OROL/Water", "OROL/Sky", "OROL/Additive" };
        private static readonly string[] RequiredFonts = { "Fonts/Play-Regular", "Fonts/Play-Bold", "Fonts/RussoOne-Regular" };

        public static List<string> Validate(bool openScenes)
        {
            var errors = new List<string>();

            // Scenes
            if (EditorBuildSettings.scenes.Length == 0) errors.Add("No scenes in build settings");
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (!s.enabled) continue;
                if (!File.Exists(s.path))
                {
                    errors.Add("Build scene missing: " + s.path);
                    continue;
                }
                if (!openScenes) continue;
                var scene = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    int missing = CountMissingScripts(root);
                    if (missing > 0) errors.Add($"{s.path}: {missing} missing script(s) under '{root.name}'");
                }
            }

            // Shaders
            foreach (string name in RequiredShaders)
            {
                Shader shader = Shader.Find(name);
                if (shader == null)
                {
                    errors.Add("Shader not found: " + name);
                    continue;
                }
                if (ShaderUtil.ShaderHasError(shader))
                {
                    int before = errors.Count;
                    foreach (ShaderMessage m in ShaderUtil.GetShaderMessages(shader))
                    {
                        if (m.severity == ShaderCompilerMessageSeverity.Error)
                            errors.Add($"Shader error {name} ({m.platform}) line {m.line}: {m.message}");
                    }
                    if (errors.Count == before) errors.Add("Shader has errors: " + name);
                }
            }

            // Resources
            foreach (string f in RequiredFonts)
                if (Resources.Load<Font>(f) == null) errors.Add("Font missing in Resources: " + f);
            if (Resources.Load<TextAsset>("client_config") == null) errors.Add("Resources/client_config.json missing");
            if (Resources.Load<Material>("Materials/Fallback_Lit") == null) errors.Add("Fallback_Lit material missing");
            if (Resources.Load<Material>("Materials/Fallback_Additive") == null) errors.Add("Fallback_Additive material missing");

            // Render pipeline per quality level
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (QualitySettings.GetRenderPipelineAssetAt(i) == null)
                    errors.Add($"Quality level '{names[i]}' has no render pipeline asset");
            }
            if (GraphicsSettings.defaultRenderPipeline == null) errors.Add("GraphicsSettings has no default render pipeline (URP)");

            return errors;
        }

        private static int CountMissingScripts(GameObject go)
        {
            int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            foreach (Transform child in go.transform) count += CountMissingScripts(child.gameObject);
            return count;
        }
    }
}
