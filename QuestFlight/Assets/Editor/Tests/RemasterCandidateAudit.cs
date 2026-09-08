using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    // Offline evidence only. No scene or mesh is saved by this audit.
    public static class RemasterCandidateAudit
    {
        [Serializable] private sealed class Entry
        {
            public string path;
            public string[] dependencies;
            public string[] importedObjects;
        }
        [Serializable] private sealed class Inventory
        {
            public string scope = "Unity scene dependencies and imported subassets; streaming manifests audited separately";
            public string[] sceneDependencies;
            public List<Entry> assets = new List<Entry>();
        }

        public static void Run()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_REMASTER_ROOT");
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("QFL_REMASTER_ROOT required");
            Directory.CreateDirectory(root);
            var inventory = new Inventory
            {
                sceneDependencies = AssetDatabase.GetDependencies("Assets/Scenes/ProductionVerticalSlice.unity", true)
            };
            foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p =>
                p.StartsWith("Assets/Production/Environment/", StringComparison.Ordinal) ||
                p.StartsWith("Assets/Generated/", StringComparison.Ordinal)))
            {
                if (AssetDatabase.IsValidFolder(path)) continue;
                string extension = Path.GetExtension(path).ToLowerInvariant();
                bool imported = extension == ".glb" || extension == ".fbx" || extension == ".obj";
                inventory.assets.Add(new Entry
                {
                    path = path,
                    dependencies = AssetDatabase.GetDependencies(path, false),
                    importedObjects = imported ? AssetDatabase.LoadAllAssetsAtPath(path)
                        .Where(a => a).Select(a => a.GetType().Name + ":" + a.name).ToArray() : Array.Empty<string>()
                });
            }
            File.WriteAllText(Path.Combine(root, "unity_consumer_inventory.json"), JsonUtility.ToJson(inventory, true));

            if (!File.ReadAllText("Assets/Production/Environment/TrafficV1/Materials/KBDU_TrafficVehicle.shader")
                .Contains("baseColor *= input.semantic.a;"))
                throw new InvalidOperationException("AO shader is not active; refusing a misleading before/after capture.");

            string models = "Assets/Production/Environment/TrafficV1/Models";
            Mesh[] meshes = Directory.GetFiles(models, "*.asset")
                .Select(p => AssetDatabase.LoadAssetAtPath<Mesh>(p.Replace('\\', '/'))).ToArray();
            if (meshes.Length != 8 || meshes.Any(m => !m)) throw new InvalidOperationException("Eight traffic meshes required");
            Color32[][] original = meshes.Select(m => m.colors32).ToArray();
            string previous = System.Environment.GetEnvironmentVariable("QFL_TRAFFIC_V2_FULL_MAP_QA_DIR");
            try
            {
                foreach (bool candidate in new[] { false, true })
                {
                    for (int i = 0; i < meshes.Length; i++)
                    {
                        Color32[] colors = (Color32[])original[i].Clone();
                        if (!candidate)
                            for (int v = 0; v < colors.Length; v++) colors[v].a = 255;
                        meshes[i].colors32 = colors;
                    }
                    System.Environment.SetEnvironmentVariable("QFL_TRAFFIC_V2_FULL_MAP_QA_DIR",
                        Path.Combine(root, candidate ? "traffic_ao_after" : "traffic_ao_before"));
                    global::QuestFlightLab.Environment.Editor.Tests.TrafficV2FullMapVisualQaBatchRunner.Run();
                }
            }
            finally
            {
                for (int i = 0; i < meshes.Length; i++) if (meshes[i]) meshes[i].colors32 = original[i];
                System.Environment.SetEnvironmentVariable("QFL_TRAFFIC_V2_FULL_MAP_QA_DIR", previous);
            }
        }

        public static void ValidateRestoredProduct()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_REMASTER_ROOT");
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("QFL_REMASTER_ROOT required");
            System.Environment.SetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR", Path.Combine(root, "restored_standard_qa"));
            global::QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunProductionVisualQa();
        }

        public static void ExportBuildContents()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_REMASTER_ROOT");
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("QFL_REMASTER_ROOT required");
            var report = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget("Library/LastBuild.buildreport")
                .OfType<BuildReport>().FirstOrDefault();
            if (!report) throw new InvalidOperationException("Last build report unavailable");
            string[] paths = report.packedAssets.SelectMany(p => p.contents)
                .Select(p => p.sourceAssetPath).Where(p => !string.IsNullOrEmpty(p)).Distinct().OrderBy(p => p).ToArray();
            File.WriteAllLines(Path.Combine(root, "early_build_packed_source_paths.txt"), paths);
        }
    }
}
