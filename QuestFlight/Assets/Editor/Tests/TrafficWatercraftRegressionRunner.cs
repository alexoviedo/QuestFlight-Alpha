using System;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public static class TrafficWatercraftRegressionRunner
    {
        const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        const string TrafficPath = "Assets/Production/Environment/TrafficV1/KBDU_TrafficRoot.prefab";
        const string AlignmentPath = "Assets/Production/Environment/ReservoirWatercraftV1/ReservoirWatercraftSurfaceAlignmentV1.json";
        [Serializable] class Alignment { public Record[] records; }
        [Serializable] class Record { public string stable_id; public float surface_center_y_meters; public Vector3 model_origin; }

        public static void VisualQa()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_REGRESSION_DIR");
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("QFL_REGRESSION_DIR is required.");
            System.Environment.SetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR", Path.Combine(root, "standard_qa"));
            QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunProductionVisualQa();
            System.Environment.SetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR", Path.Combine(root, "watercraft_qa"));
            QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunReservoirWatercraftVisualQa();
            System.Environment.SetEnvironmentVariable("QFL_TRAFFIC_V2_FULL_MAP_QA_DIR", Path.Combine(root, "traffic_full_map_qa"));
            QuestFlightLab.Environment.Editor.Tests.TrafficV2FullMapVisualQaBatchRunner.Run();
        }

        public static void CaptureTrafficAndBuild()
        {
            QuestFlightLab.Environment.Editor.Tests.TrafficV2FullMapVisualQaBatchRunner.Run();
            QuestFlight.Editor.ProductionBuild.PerformAndroidBuildBatch();
        }

        public static void Repair()
        {
            try
            {
                var before = JsonUtility.FromJson<Alignment>(File.ReadAllText(AlignmentPath));
                QuestFlightLab.Editor.ReservoirWatercraftAuthoring.RevalidateExistingSurfaceAlignment();
                var after = JsonUtility.FromJson<Alignment>(File.ReadAllText(AlignmentPath));
                for (int i = 0; i < before.records.Length; i++)
                    Debug.Log($"REGRESSION rendered boat={before.records[i].stable_id} heightDelta={after.records[i].surface_center_y_meters - before.records[i].surface_center_y_meters:F6}");
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var environment = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ProductionEnvironmentRoot>(true)).Single();
                var budget = environment.CalculateBudget();
                environment.bakedRendererCount = budget.rendererCount;
                environment.bakedTriangleCount = budget.triangleCount;
                environment.bakedMaterialCount = budget.materialCount;
                PrefabUtility.RecordPrefabInstancePropertyModifications(environment);
                EditorUtility.SetDirty(environment);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("REGRESSION repaired environment=" + environment.TryValidateContract(out string report) + " " + report);
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        public static void Diagnose()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var traffic = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<KbduTrafficV2Runtime>(true)).Single();
                Debug.Log("REGRESSION scene traffic valid=" + traffic.TryValidateContract(out string report) + " report=" + report);
                var serialized = new SerializedObject(traffic);
                Debug.Log("REGRESSION scene road binding=" + serialized.FindProperty("gradeSeparatedRoadAuthority").objectReferenceValue?.name);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPath).GetComponent<KbduTrafficV2Runtime>();
                Debug.Log("REGRESSION prefab traffic valid=" + prefab.TryValidateContract(out report) + " report=" + report);
                var terrain = (ProductionTerrainSurfaceAsset)serialized.FindProperty("terrainSurface").objectReferenceValue;
                var alignment = JsonUtility.FromJson<Alignment>(File.ReadAllText(AlignmentPath));
                foreach (var record in alignment.records)
                {
                    if (!terrain.TrySample(record.model_origin.x, record.model_origin.z, out ProductionTerrainSample sample))
                        throw new Exception("Missing terrain sample " + record.stable_id);
                    Debug.Log($"REGRESSION boat={record.stable_id} oldWater={record.surface_center_y_meters:F3} currentTerrain={sample.heightMeters:F3}");
                }
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }
    }
}
