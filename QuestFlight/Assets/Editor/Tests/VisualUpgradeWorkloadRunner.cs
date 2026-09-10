using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using QuestFlightLab.Environment;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public static class VisualUpgradeWorkloadRunner
    {
        public static void RunCanopyRepairQa()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_UPGRADE_QA_ROOT");
            System.Environment.SetEnvironmentVariable("QFL_CANOPY_PROXY_BENCHMARK", "0");
            RunBenchmarkAudit();
            System.Environment.SetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR", Path.Combine(root, "standard_qa"));
            ProductionVisualQaBatchRunner.RunProductionVisualQa();
        }

        // Read-only Editor investigation. No scene or product assets are saved.
        public static void RunBenchmarkAudit()
        {
            bool previous = AirportCloseTreeLodRenderer.AllowBenchmarkCrownProxiesForVisualQa;
            AirportCloseTreeLodRenderer.AllowBenchmarkCrownProxiesForVisualQa =
                System.Environment.GetEnvironmentVariable("QFL_CANOPY_PROXY_BENCHMARK") == "1";
            try { RunBenchmarkAuditInternal(); }
            finally { AirportCloseTreeLodRenderer.AllowBenchmarkCrownProxiesForVisualQa = previous; }
        }

        private static void RunBenchmarkAuditInternal()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_UPGRADE_QA_ROOT");
            Directory.CreateDirectory(root);
            WriteValmontFootprintAudit(root);
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity", OpenSceneMode.Single);
            var trees = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AirportCloseTreeLodRenderer>(true)).Single();
            if (System.Environment.GetEnvironmentVariable("QFL_DISABLE_AUTHORED_COVERAGE_PATCH") == "1")
                typeof(AirportCloseTreeLodRenderer).GetField("enableAuthoredFarCoveragePatch", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(trees, false);
            object Field(string name) => typeof(AirportCloseTreeLodRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(trees);
            var go = new GameObject("Canopy coverage audit camera") { hideFlags = HideFlags.DontSave };
            var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 55;
            var target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
            var summaries = new List<string> { "camera_local_y,source_crowns_in_patch,active_crowns_in_patch,selected_near,selected_middle,selected_far,selected_projected_crown_area_proxy,total_source_area_proxy,global_near,global_middle,global_far,global_shrubs" };
            try
            {
                foreach (int height in new[] { 100, 300, 500 })
                {
                    camera.transform.position = new Vector3(-800, height, -700 - height * 1.2f);
                    camera.transform.rotation = Quaternion.LookRotation(new Vector3(-800, 0, -700) - camera.transform.position);
                    QuestFlight.Editor.FullMapBuildingClipmapAssetAuthoring.LoadEditorPreviewWindow(trees.BuildingClipmap, camera.transform.position);
                    trees.PrepareSettledPreview(camera);
                    var instances = (Array)Field("runtimeInstances");
                    var active = new HashSet<int>(((int[])Field("activeIndices")).Take((int)Field("activeCount")));
                    var selected = (bool[])Field("selectedForDraw");
                    var desired = (byte[])Field("desiredLods");
                    var globalLods = new int[3]; int globalShrubs = 0;
                    var rows = new List<string> { "id,x,y,z,radius,kind,active,selected,lod,viewport_x,viewport_y,depth" };
                    int count = 0, resident = 0; var lods = new int[3]; double selectedArea = 0, sourceArea = 0;
                    var type = instances.GetType().GetElementType();
                    var positionField = type.GetField("Position"); var radiusField = type.GetField("Radius"); var kindField = type.GetField("Kind");
                    var allSelected = new List<string> { "id,x,y,z,lod,viewport_x,viewport_y,depth" };
                    foreach (int index in active.Where(index => selected[index]))
                    {
                        byte kind = (byte)kindField.GetValue(instances.GetValue(index));
                        if (kind == 2 && desired[index] != 2) globalShrubs++; else globalLods[desired[index]]++;
                        Vector3 p = (Vector3)positionField.GetValue(instances.GetValue(index));
                        Vector3 v = camera.WorldToViewportPoint(p);
                        allSelected.Add(FormattableString.Invariant($"{index},{p.x},{p.y},{p.z},{desired[index]},{v.x},{v.y},{v.z}"));
                    }
                    File.WriteAllLines(Path.Combine(root, $"all_selected_{height}.csv"), allSelected);
                    if (globalLods[0] > AirportCloseTreeLodRenderer.MaximumLod0Instances ||
                        globalLods[1] > AirportCloseTreeLodRenderer.MaximumLod1Instances ||
                        globalLods[2] > AirportCloseTreeLodRenderer.MaximumLod2Instances ||
                        globalShrubs > AirportCloseTreeLodRenderer.MaximumShrubInstances)
                        throw new InvalidOperationException("Benchmark exceeded existing selection caps.");
                    for (int i = 0; i < instances.Length; i++)
                    {
                        object instance = instances.GetValue(i);
                        var position = (Vector3)positionField.GetValue(instance);
                        // Exact City2020 Valmont comparison crop; placement remains authored.
                        if (position.x < -884 || position.x > -135 || position.z < -914 || position.z > -494) continue;
                        byte kind = (byte)kindField.GetValue(instance); if (kind == 2 || kind == 3) continue;
                        float radius = (float)radiusField.GetValue(instance); var viewport = camera.WorldToViewportPoint(position);
                        bool isActive = active.Contains(i), isSelected = isActive && selected[i];
                        count++; if (isActive) resident++;
                        double area = radius * radius / Math.Max(1, (position - camera.transform.position).sqrMagnitude);
                        sourceArea += area;
                        if (isSelected) { lods[desired[i]]++; selectedArea += area; }
                        rows.Add(FormattableString.Invariant($"{i},{position.x},{position.y},{position.z},{radius},{kind},{isActive},{isSelected},{(isSelected ? desired[i] : 3)},{viewport.x},{viewport.y},{viewport.z}"));
                    }
                    File.WriteAllLines(Path.Combine(root, $"canopy_{height}.csv"), rows);
                    summaries.Add(FormattableString.Invariant($"{height},{count},{resident},{lods[0]},{lods[1]},{lods[2]},{selectedArea},{sourceArea},{globalLods[0]},{globalLods[1]},{globalLods[2]},{globalShrubs}"));
                    trees.ClearSettledPreview();
                }
                File.WriteAllLines(Path.Combine(root, "canopy_summary.csv"), summaries);
                File.WriteAllText(Path.Combine(root, "scope.txt"), "Read-only authored City2020 Valmont crop selection audit. Area is a radius-squared/distance-squared proxy, not visible pixel coverage; crowns overlap and may be occluded. Editor projection/residency is not Android acceptance.\n");
            }
            finally { trees.ClearSettledPreview(); camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(go); }
            if (System.Environment.GetEnvironmentVariable("QFL_DISABLE_AUTHORED_COVERAGE_PATCH") == "1") return;
            System.Environment.SetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR", Path.Combine(root, "settled_benchmark"));
            ProductionVisualQaBatchRunner.RunSettledSceneryBenchmark();
        }

        private static void WriteValmontFootprintAudit(string root)
        {
            Type authoring = typeof(QuestFlight.Editor.FullMapBuildingClipmapAssetAuthoring);
            object db = authoring.GetMethod("LoadDatabase", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            object Get(object o, string field) => o.GetType().GetField(field).GetValue(o);
            float half = Convert.ToSingle(Get(db, "map_half_extent_meters"));
            float pageSize = Convert.ToSingle(Get(db, "page_size_meters"));
            float quantization = Convert.ToSingle(Get(db, "quantization_meters"));
            MethodInfo compile = authoring.GetMethod("CompileStableFootprint", BindingFlags.Static | BindingFlags.NonPublic);
            var rows = new List<string> { "id,source_vertices,previous_compiled_vertices,candidate_vertices,height_dm,added_triangles" };
            foreach (object page in (Array)Get(db, "pages"))
            foreach (object building in (Array)Get(page, "buildings"))
            {
                if ((int)Get(building, "roof_id") != 0 || Convert.ToSingle(Get(building, "area_m2")) < 1000f || (bool)Get(building, "landmark")) continue;
                int[] ring = (int[])Get(building, "lod2_q");
                var points = Enumerable.Range(0, ring.Length / 2).Select(i => new Vector2(
                    -half + Convert.ToInt32(Get(page, "column")) * pageSize + ring[i * 2] * quantization,
                    -half + Convert.ToInt32(Get(page, "row")) * pageSize + ring[i * 2 + 1] * quantization)).ToArray();
                float x = points.Average(p => p.x), z = points.Average(p => p.y);
                if (x < -884 || x > -135 || z < -914 || z > -494) continue;
                var candidate = (Vector2[])compile.Invoke(null, new object[] { points, building });
                // roof_id only selects the new flat-outline exception in this function.
                // Bypass that exception to evaluate the otherwise unchanged previous compiler.
                FieldInfo roof = building.GetType().GetField("roof_id");
                Vector2[] previous;
                roof.SetValue(building, 1);
                try { previous = (Vector2[])compile.Invoke(null, new object[] { points, building }); }
                finally { roof.SetValue(building, 0); }
                rows.Add($"{Get(building, "id")},{points.Length},{previous.Length},{candidate.Length},{Get(building, "height_dm")},{3 * (candidate.Length - previous.Length)}");
            }
            File.WriteAllLines(Path.Combine(root, "building_outlines.csv"), rows);
        }

        [Serializable] private class Result
        {
            public string scope = "Editor CPU traffic preparation only; not Quest frame timing";
            public Row[] rows;
        }
        [Serializable] private class Row
        {
            public Vector3 position;
            public double averageMilliseconds;
            public int near, far;
        }
        [Serializable] private class BuildingResult
        {
            public string scope = "Editor selection CPU/allocation; not Quest timing. Test wrapper included. Allocation bytes -1 means unavailable; zero-return API is not evidence of no allocation.";
            public double millisecondsPerSelection;
            public long bytesPerSelection;
            public string[] selections;
        }
        public static void RunBuildingSelection()
        {
            string output = System.Environment.GetEnvironmentVariable("QFL_WORKLOAD_REPORT");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity", OpenSceneMode.Single);
            var controller = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AircraftCenteredBuildingClipmapController>(true)).Single();
            var selections = new System.Collections.Generic.List<string>();
            for (int x = -10000; x <= 10000; x += 2000)
            for (int z = -10000; z <= 10000; z += 2000)
            for (int heading = 0; heading < 8; heading++)
            {
                Vector3 forward = Quaternion.Euler(0, heading * 45, 0) * Vector3.forward;
                foreach (int view in new[] { 0, 90, 180 })
                {
                    var pages = controller.CalculateDesiredPagesForTest(new Vector3(x, 300, z), forward,
                        Quaternion.Euler(0, view, 0) * forward);
                    selections.Add(string.Join(";", pages.Select(p => p.x + "," + p.y)));
                }
            }
            controller.CalculateDesiredPagesForTest(Vector3.zero, Vector3.forward, Vector3.forward);
            var watch = new Stopwatch();
            long before = GC.GetAllocatedBytesForCurrentThread();
            watch.Start();
            for (int i = 0; i < 100; i++)
                controller.CalculateDesiredPagesForTest(Vector3.zero, Vector3.forward, Vector3.forward);
            watch.Stop();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            File.WriteAllText(output, JsonUtility.ToJson(new BuildingResult {
                millisecondsPerSelection = watch.Elapsed.TotalMilliseconds / 100,
                bytesPerSelection = bytes > 0 ? bytes / 100 : -1, selections = selections.ToArray() }, true));
        }

        public static void RunRecoveryQa()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_UPGRADE_QA_ROOT");
            Directory.CreateDirectory(root);
            foreach (bool baseline in new[] { true, false })
            {
                System.Environment.SetEnvironmentVariable("QFL_CULLING_BASELINE", baseline ? "1" : null);
                System.Environment.SetEnvironmentVariable("QFL_WORKLOAD_REPORT", Path.Combine(root, baseline ? "traffic_unculled.json" : "traffic_culled.json"));
                Run();
            }
            System.Environment.SetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR", Path.Combine(root, "standard_qa"));
            ProductionVisualQaBatchRunner.RunProductionVisualQa();
            System.Environment.SetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR", Path.Combine(root, "settled_benchmark"));
            ProductionVisualQaBatchRunner.RunSettledSceneryBenchmark();
            System.Environment.SetEnvironmentVariable("QFL_TRAFFIC_V2_FULL_MAP_QA_DIR", Path.Combine(root, "traffic_qa"));
            QuestFlightLab.Environment.Editor.Tests.TrafficV2FullMapVisualQaBatchRunner.Run();
            System.Environment.SetEnvironmentVariable("QFL_GRADE_SEPARATED_VISUAL_QA_DIR", Path.Combine(root, "bridge_qa"));
            GradeSeparatedRoadVisualQaBatchRunner.RunBatch();
        }

        public static void Run()
        {
            string output = System.Environment.GetEnvironmentVariable("QFL_WORKLOAD_REPORT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("QFL_WORKLOAD_REPORT required");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity", OpenSceneMode.Single);
            var traffic = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<KbduTrafficV2Runtime>(true)).Single();
            if (!traffic.TryValidateContract(out string report)) throw new InvalidOperationException(report);
            if (System.Environment.GetEnvironmentVariable("QFL_DIAGNOSE_PATCH_COST") == "1")
            {
                var field = typeof(KbduTrafficV2Runtime).GetField("terrainSurface", BindingFlags.NonPublic | BindingFlags.Instance);
                var copy = UnityEngine.Object.Instantiate((ProductionTerrainSurfaceAsset)field.GetValue(traffic));
                copy.hideFlags = HideFlags.DontSave;
                copy.transitionPatches = Array.Empty<ProductionTerrainSurfaceAsset.TrianglePatch>();
                field.SetValue(traffic, copy);
                UnityEngine.Debug.Log("DIAGNOSTIC ONLY: transition patches bypassed in an unsaved terrain copy; invalid as product surface evidence.");
            }
            traffic.DisableFrustumCullingForVisualQa = System.Environment.GetEnvironmentVariable("QFL_CULLING_BASELINE") == "1";
            var prepare = (Action<Camera, double>)Delegate.CreateDelegate(typeof(Action<Camera, double>), traffic,
                typeof(KbduTrafficV2Runtime).GetMethod("PrepareVisibleInstances", BindingFlags.NonPublic | BindingFlags.Instance));
            var cameraObject = new GameObject("Traffic CPU diagnostic camera") { hideFlags = HideFlags.DontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 95f; camera.aspect = 1f; camera.farClipPlane = 6000f;
            try
            {
                var result = new Result { rows = new Vector3[] { new Vector3(0, 300, 0),
                    new Vector3(0, 300, 3500), new Vector3(-3400, 500, -2300) }.Select(position =>
                {
                    camera.transform.position = position;
                    prepare(camera, 60);
                    var watch = Stopwatch.StartNew();
                    for (int i = 0; i < 20; i++) prepare(camera, 60 + i / 72.0);
                    watch.Stop();
                    return new Row { position = position, averageMilliseconds = watch.Elapsed.TotalMilliseconds / 20,
                        near = traffic.PreparedNearInstanceCount, far = traffic.PreparedFarInstanceCount };
                }).ToArray() };
                File.WriteAllText(output, JsonUtility.ToJson(result, true));
                UnityEngine.Debug.Log(JsonUtility.ToJson(result));
            }
            finally { UnityEngine.Object.DestroyImmediate(cameraObject); }
        }
    }
}
