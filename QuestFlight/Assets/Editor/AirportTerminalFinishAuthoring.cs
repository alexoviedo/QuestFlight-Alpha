using System;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    /// <summary>
    /// Bounded terminal finish: restore the source model's east-facing apron facade
    /// and physical masonry/cladding scale. No new props, geometry or textures.
    /// City 2020 orthophoto and airport source specification agree on the east apron.
    /// </summary>
    public static class AirportTerminalFinishAuthoring
    {
        private const string EnvironmentPath = "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        private const string MaterialPath = "Assets/Production/Environment/AirportCoreV4/Materials/AirportCoreTerminalAtlas.mat";

        public static void AuthorBatch()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null || !material.HasProperty("_MetricTerminalWalls"))
                throw new InvalidOperationException("Terminal material does not expose the reviewed wall mapping.");
            material.SetFloat("_MetricTerminalWalls", 1f);
            material.SetFloat("_MasonryTileMeters", 1.4f);
            material.SetFloat("_CladdingTileMeters", 4f);
            EditorUtility.SetDirty(material);
            GameObject environment = PrefabUtility.LoadPrefabContents(EnvironmentPath);
            try
            {
                var benchmark = environment.GetComponentsInChildren<AirportCoreBenchmarkRoot>(true).Single();
                Transform terminal = benchmark.executiveTerminal.transform;
                if (terminal.name != "ExecutiveTerminal_3335" ||
                    Vector2.Distance(new Vector2(terminal.localPosition.x, terminal.localPosition.z),
                        new Vector2(-385.25f, -224.125f)) > 0.05f)
                    throw new InvalidOperationException("Reviewed terminal identity or source footprint moved.");
                MeshFilter[] filters = terminal.GetComponentsInChildren<MeshFilter>(true);
                if (filters.Length != 3 || filters.Any(filter => filter.sharedMesh == null ||
                        filter.sharedMesh.bounds.min.x > -13.5f || filter.sharedMesh.bounds.max.x > 11.2f))
                    throw new InvalidOperationException("Source terminal no longer has its imported west-facing balcony.");
                // glTFast flips source X; Blender's Y-up export flips source Y/Z.
                // The source balcony, entrance and signs were authored east (+X),
                // but identity placement faced them west into the car park.
                terminal.localRotation = Quaternion.Euler(0f, 180f, 0f);
                EditorUtility.SetDirty(terminal);
                PrefabUtility.SaveAsPrefabAsset(environment, EnvironmentPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(environment); }
            AssetDatabase.SaveAssets();
            Debug.Log("[AirportTerminalFinish] Restored east-facing apron facade at the existing footprint; " +
                "terminal masonry/cladding uses metre-scale mapping; unchanged meshes, textures and draw calls. " +
                "Refresh the existing bounded airport lightmap before packaging.");
        }
    }

    public static partial class ProductionVisualQaBatchRunner
    {
        public static void RunAirportTerminalFinishQa()
        {
            string output = System.Environment.GetEnvironmentVariable("QFL_TERMINAL_QA");
            if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("QFL_TERMINAL_QA is required.");
            Directory.CreateDirectory(Path.Combine(output, "screenshots"));
            const int width = 1440, height = 1080;
            var report = new ProductionVisualQaReport { generatedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion, scenePath = ProductionScenePath,
                outputDirectory = output, renderWidth = width, renderHeight = height,
                humanHeadsetAcceptance = "PENDING" };
            try
            {
                VisualContext c = LoadContext(width, height, report);
                Transform terminal = c.environment.GetComponentInChildren<AirportCoreBenchmarkRoot>()
                    .executiveTerminal.transform;
                Vector3 ground = terminal.position;
                Vector3 target = ground + Vector3.up * 4f;
                Vector3 taxiEye = ground + Vector3.right * 52f + Vector3.up * 1.8f;
                Capture(c, report, output, width, height, new ShotDefinition("terminal_apron_eye",
                    "Terminal from apron seated-eye height", "airport_core_terminal", taxiEye,
                    LookAt(taxiEye, target), 55f, ViewProfile.Environment));
                CaptureStereoEnvironment(c, report, output, width, height, "terminal_apron_stereo",
                    "Terminal apron facade stereo", taxiEye, target, 55f);
                Vector3 aerial = ground + new Vector3(50, 70, 35);
                Capture(c, report, output, width, height, new ShotDefinition("terminal_departure",
                    "Terminal facade and reference footprint on departure", "airport_core_terminal", aerial,
                    LookAt(aerial, target), 48f, ViewProfile.Environment));
                report.passed = report.errors.Count == 0 && report.shots.All(shot => shot.passed);
                report.classification = report.passed ? "PASS_TERMINAL_MACHINE_QA" : "REVIEW_TERMINAL_QA";
            }
            catch (Exception exception) { report.errors.Add(exception.ToString()); Debug.LogException(exception); }
            WriteReportFiles(report, output);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
