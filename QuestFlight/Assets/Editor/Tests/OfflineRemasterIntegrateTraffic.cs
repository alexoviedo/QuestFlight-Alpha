using System;
using System.IO;
using System.Security.Cryptography;
using QuestFlight.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public static class OfflineRemasterIntegrateTraffic
    {
        public static void ValidateAndroidPlayerScripts()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_REMASTER_ROOT");
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("QFL_REMASTER_ROOT required");
            string output = Path.Combine(root, "android_player_scripts");
            Directory.CreateDirectory(output);
            var settings = new UnityEditor.Build.Player.ScriptCompilationSettings
            {
                group = BuildTargetGroup.Android,
                target = BuildTarget.Android,
                options = UnityEditor.Build.Player.ScriptCompilationOptions.None
            };
            var result = UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(settings, output);
            if (result.assemblies == null || result.assemblies.Count == 0)
                throw new InvalidOperationException("Android player-script compilation produced no assemblies");
            File.WriteAllText(Path.Combine(output, "validation.txt"),
                "Android player-script compilation passed. This is not APK/IL2CPP/package validation.\n" +
                string.Join("\n", result.assemblies));
        }

        public static void RunWithReview()
        {
            Run();
            OfflineRemasterTrafficReview.Run();
            global::QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunProductionVisualQa();
        }

        public static void Run()
        {
            const string shaderPath = "Assets/Production/Environment/TrafficV1/Materials/KBDU_TrafficVehicle.shader";
            if (!File.ReadAllText(shaderPath).Contains("baseColor *= input.semantic.a;"))
                throw new InvalidOperationException("Traffic AO was rejected and restored. Reinstating the staged experiment requires a reviewed compatible shader; refusing unused baked attributes.");
            string root = System.Environment.GetEnvironmentVariable("QFL_REMASTER_ROOT");
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("QFL_REMASTER_ROOT required");
            string backups = Path.Combine(root, "integration_backup");
            Directory.CreateDirectory(backups);
            string materialPath = "Assets/Production/Environment/TrafficV1/Materials/KBDU_TrafficVehicle.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material || ShaderUtil.ShaderHasError(material.shader))
                throw new InvalidOperationException("Reviewed AO shader required");
            // Compile the actual Android/Vulkan stereo variants before product writes.
            var pass = ShaderUtil.GetShaderData(material.shader).GetSubshader(0).GetPass(0);
            foreach (string fog in new[] { "", "FOG_LINEAR" })
            foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
            {
                string[] keywords = fog == "" ? new[] { "DIRECTIONAL", "INSTANCING_ON", "STEREO_INSTANCING_ON" }
                    : new[] { "DIRECTIONAL", "INSTANCING_ON", "STEREO_INSTANCING_ON", fog };
                var compiled = pass.CompileVariant(stage, keywords, ShaderCompilerPlatform.Vulkan, BuildTarget.Android, true);
                if (!compiled.Success || compiled.ShaderData.Length == 0)
                    throw new InvalidOperationException("Android traffic shader compilation failed: " + stage);
                File.WriteAllBytes(Path.Combine(root, "traffic_" + stage + "_" + fog + ".bin"), compiled.ShaderData);
            }
            string[] paths = Directory.GetFiles("Assets/Production/Environment/TrafficV1/Models", "*.asset");
            if (paths.Length != 8) throw new InvalidOperationException("Unexpected traffic archetype count");
            // Validate all clones and preserve originals before changing any asset.
            foreach (string path in paths)
            {
                Mesh source = AssetDatabase.LoadAssetAtPath<Mesh>(path.Replace('\\', '/'));
                Mesh copy = UnityEngine.Object.Instantiate(source);
                copy.name = source.name;
                try { TrafficSelfOcclusionAuthoring.ApplyIfPresent(copy); }
                finally { UnityEngine.Object.DestroyImmediate(copy); }
                Backup(path, backups);
                Backup(path + ".meta", backups);
            }
            Backup(materialPath, backups);
            Backup(materialPath + ".meta", backups);
            foreach (string path in paths)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path.Replace('\\', '/'));
                TrafficSelfOcclusionAuthoring.ApplyIfPresent(mesh);
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssetIfDirty(mesh);
            }
            File.WriteAllText(Path.Combine(root, "traffic_integration.json"),
                "{\"integrated_meshes\":8,\"android_vulkan_variants_compiled\":4,\"quest_performance_verified\":false}");
        }

        private static void Backup(string source, string root)
        {
            string target = Path.Combine(root, source);
            if (File.Exists(target)) return; // Preserve the first accepted copy on rerun.
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target);
            using (SHA256 sha = SHA256.Create())
            {
                string originalHash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(source)));
                string backupHash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(target)));
                if (originalHash != backupHash) throw new IOException("Backup mismatch: " + source);
            }
        }
    }
}
