using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    // Offline compiler evidence only. Does not save or modify product assets.
    public static class GpuShaderAuditRunner
    {
        public static void RunWithVisualQa()
        {
            Run();
            VisualUpgradeWorkloadRunner.RunCanopyRepairQa();
        }

        public static void Run()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_GPU_AUDIT_ROOT");
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("QFL_GPU_AUDIT_ROOT is required.");
            Directory.CreateDirectory(root);
            string[] paths = {
                "Assets/Production/Environment/PhotorealAerialSurfaceV1/Materials/ProductionSemanticSurface.shader",
                "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.shader",
                "Assets/Production/Environment/CloudsV1/BoulderCloudShell.shader"
            };
            foreach (string path in paths)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (!shader) throw new InvalidOperationException(path);
                var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
                foreach (string fog in new[] { "", "FOG_LINEAR" })
                foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                {
                    string[] keywords = string.IsNullOrEmpty(fog)
                        ? new[] { "DIRECTIONAL", "INSTANCING_ON", "STEREO_INSTANCING_ON" }
                        : new[] { "DIRECTIONAL", "INSTANCING_ON", "STEREO_INSTANCING_ON", fog };
                    var result = pass.CompileVariant(stage, keywords,
                        ShaderCompilerPlatform.Vulkan, BuildTarget.Android, true);
                    string stem = Path.GetFileNameWithoutExtension(path) + (fog == "" ? "_clear" : "_fog") + "_" + stage;
                    File.WriteAllText(Path.Combine(root, stem + ".txt"),
                        "Source: " + path + "\nUnity: " + Application.unityVersion +
                        "\nPlatform: Android Vulkan\nKeywords: " + string.Join(" ", keywords) +
                        "\nSuccess: " + result.Success + "\n" + string.Join("\n", Array.ConvertAll(result.Messages, m => m.message)));
                    if (!result.Success || result.ShaderData.Length == 0)
                        throw new InvalidOperationException("Shader audit compilation failed: " + stem);
                    File.WriteAllBytes(Path.Combine(root, stem + ".bin"), result.ShaderData);
                    File.Copy(path, Path.Combine(root, Path.GetFileName(path)), true);
                }
            }
            Debug.Log("GPU shader audit complete: " + root);
        }
    }
}
