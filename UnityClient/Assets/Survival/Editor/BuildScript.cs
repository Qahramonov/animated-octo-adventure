using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Survival.Editor
{
    /// <summary>
    /// Command-line / CI build entry points (GameCI passes the same arguments as its default script):
    ///   -executeMethod Survival.Editor.BuildScript.BuildAndroid
    ///   -customBuildPath build/Android/OROL.apk  -androidExportType androidPackage|androidAppBundle
    ///   -buildVersion 0.1.42  -androidVersionCode 42
    ///   -androidKeystoreName ... -androidKeystorePass ... -androidKeyaliasName ... -androidKeyaliasPass ...
    /// Without a keystore the APK is signed with Unity's debug key (installable on your own phone,
    /// not uploadable to Google Play).
    /// </summary>
    public static class BuildScript
    {
        public const string PackageId = "com.qahramonov.orol";
        public const string ProductName = "OROL Survival";
        public const string CompanyName = "Qahramonov";

        private static readonly string[] SecretArgs = { "androidKeystorePass", "androidKeyaliasName", "androidKeyaliasPass" };

        [MenuItem("OROL/Build/Android APK (debug-signed)")]
        public static void BuildAndroidMenu() => Build(BuildTarget.Android, "build/Android/OROL.apk", new Dictionary<string, string>(), false);

        [MenuItem("OROL/Validate Project")]
        public static void ValidateMenu()
        {
            List<string> errors = ProjectValidator.Validate(true);
            if (errors.Count == 0) EditorUtility.DisplayDialog("OROL", "Project validation passed.", "OK");
            else EditorUtility.DisplayDialog("OROL", string.Join("\n", errors), "OK");
        }

        /// <summary>CI entry point (exits the editor with a non-zero code on failure).</summary>
        public static void BuildAndroid()
        {
            Dictionary<string, string> args = ParseArgs();
            string path = Get(args, "customBuildPath", "build/Android/OROL.apk");
            Build(BuildTarget.Android, path, args, true);
        }

        /// <summary>CI step that only validates (no build).</summary>
        public static void ValidateOrFail()
        {
            List<string> errors = ProjectValidator.Validate(true);
            foreach (string e in errors) Debug.LogError("[Validate] " + e);
            Debug.Log(errors.Count == 0 ? "[Validate] OK" : $"[Validate] FAILED with {errors.Count} error(s)");
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }

        private static void Build(BuildTarget target, string path, Dictionary<string, string> args, bool exitOnFinish)
        {
            Debug.Log($"[Build] {ProductName} -> {target} at {path}");
            Debug.Log("[Build] Args: " + string.Join(" ", args.Where(kv => !SecretArgs.Contains(kv.Key)).Select(kv => $"-{kv.Key} {kv.Value}")));

            List<string> errors = ProjectValidator.Validate(false);
            if (errors.Count > 0)
            {
                foreach (string e in errors) Debug.LogError("[Validate] " + e);
                Finish(exitOnFinish, 101, "Project validation failed");
                return;
            }

            ConfigurePlayerSettings(args);

            if (target == BuildTarget.Android)
            {
                bool aab = Get(args, "androidExportType", "androidPackage") == "androidAppBundle" || path.EndsWith(".aab", StringComparison.OrdinalIgnoreCase);
                EditorUserBuildSettings.buildAppBundle = aab;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                if (aab && path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) path = Path.ChangeExtension(path, ".aab");
                if (!aab && !path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) path += ".apk";
                ConfigureSigning(args);
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = path,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None,
            };
            if (Get(args, "development", "false") == "true") options.options |= BuildOptions.Development;

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[Build] Result: {summary.result} | size {summary.totalSize / (1024f * 1024f):0.0} MB | " +
                      $"time {summary.totalTime} | errors {summary.totalErrors} | warnings {summary.totalWarnings}");
            foreach (BuildStep step in report.steps)
            {
                foreach (BuildStepMessage m in step.messages)
                {
                    if (m.type == LogType.Error || m.type == LogType.Exception) Debug.LogError($"[Build] {step.name}: {m.content}");
                }
            }
            if (summary.result == BuildResult.Succeeded)
            {
                File.WriteAllText(Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "build-info.txt"),
                    $"product={ProductName}\nversion={PlayerSettings.bundleVersion}\nversionCode={PlayerSettings.Android.bundleVersionCode}\n" +
                    $"unity={Application.unityVersion}\nfile={Path.GetFileName(path)}\nsizeBytes={summary.totalSize}\nbuiltUtc={DateTime.UtcNow:O}\n");
                Finish(exitOnFinish, 0, "Build succeeded: " + path);
            }
            else
            {
                Finish(exitOnFinish, 102, "Build failed: " + summary.result);
            }
        }

        private static void Finish(bool exit, int code, string message)
        {
            if (code == 0) Debug.Log("[Build] " + message);
            else Debug.LogError("[Build] " + message);
            if (exit) EditorApplication.Exit(code);
        }

        /// <summary>Applies all player settings from code so CI builds never depend on hand-edited YAML.</summary>
        public static void ConfigurePlayerSettings(Dictionary<string, string> args)
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            string version = Get(args, "buildVersion", "none");
            if (version != "none" && !string.IsNullOrEmpty(version)) PlayerSettings.bundleVersion = version;
            if (int.TryParse(Get(args, "androidVersionCode", "0"), out int code) && code > 0) PlayerSettings.Android.bundleVersionCode = code;

            NamedBuildTarget android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, PackageId);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);
            PlayerSettings.SetApiCompatibilityLevel(android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.optimizedFramePacing = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.Android.startInFullscreen = true;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

            // iOS-ready defaults (future target)
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, PackageId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);

            PlayerSettings.SplashScreen.backgroundColor = new Color(0.008f, 0.01f, 0.016f);
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureSigning(Dictionary<string, string> args)
        {
            string keystore = Get(args, "androidKeystoreName", string.Empty);
            if (string.IsNullOrEmpty(keystore) || !File.Exists(keystore))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.Log("[Build] No keystore provided: signing with the Unity debug key (fine for sideloading, not for Google Play)");
                return;
            }
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = Get(args, "androidKeystorePass", string.Empty);
            PlayerSettings.Android.keyaliasName = Get(args, "androidKeyaliasName", string.Empty);
            PlayerSettings.Android.keyaliasPass = Get(args, "androidKeyaliasPass", string.Empty);
            Debug.Log("[Build] Release signing with provided keystore");
        }

        private static Dictionary<string, string> ParseArgs()
        {
            var result = new Dictionary<string, string>();
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].StartsWith("-")) continue;
                string key = a[i].TrimStart('-');
                string value = i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : "true";
                result[key] = value;
            }
            return result;
        }

        private static string Get(Dictionary<string, string> args, string key, string def) =>
            args.TryGetValue(key, out string v) && !string.IsNullOrEmpty(v) ? v : def;
    }
}
