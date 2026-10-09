using System;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;
using UnityEngine;

namespace QuestFlight.Editor
{
    public static class ProductMenuFlowValidation
    {
        public static void RefreshWorldBudgetBatch()
        {
            try
            {
                string path = ProductExternalControllerCompatibilityAuthoring.WorldPrefabPath;
                GameObject world = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var root = world.GetComponentInChildren<ProductionEnvironmentRoot>(true);
                    RefreshBudgetFields(root);
                    PrefabUtility.SaveAsPrefabAsset(world, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(world); }
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void RefreshBudgetFields(ProductionEnvironmentRoot root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var budget = root.CalculateBudget();
            Debug.Log($"[MenuFlowValidation] Authored triangle count: {root.bakedTriangleCount} -> {budget.triangleCount}");
            var serialized = new SerializedObject(root);
            serialized.FindProperty("bakedRendererCount").intValue = budget.rendererCount;
            serialized.FindProperty("bakedTriangleCount").intValue = budget.triangleCount;
            serialized.FindProperty("bakedMaterialCount").intValue = budget.materialCount;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            EditorUtility.SetDirty(root);
        }

        // Scene overrides have their own certificate; refreshing a nested world
        // prefab alone does not replace an existing scene-instance budget override.
        public static void RefreshSceneAndWorldBudgetsBatch()
        {
            try
            {
                string worldPath = ProductExternalControllerCompatibilityAuthoring.WorldPrefabPath;
                foreach (string prefabPath in new[] { ProductionEnvironmentRoot.PrefabAssetPath, worldPath })
                {
                    GameObject world = PrefabUtility.LoadPrefabContents(prefabPath);
                    try
                    {
                        var root = world.GetComponentInChildren<ProductionEnvironmentRoot>(true);
                        RefreshBudgetFields(root);
                        if (!root.TryValidateContract(out string prefabReport))
                            throw new InvalidOperationException(prefabReport);
                        if (PrefabUtility.SaveAsPrefabAsset(world, prefabPath) == null)
                            throw new InvalidOperationException("Budget-only prefab save failed.");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(world); }
                }
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity", OpenSceneMode.Single);
                var environment = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ProductionEnvironmentRoot>(true)).Single();
                RefreshBudgetFields(environment);
                if (!environment.TryValidateContract(out string report)) throw new InvalidOperationException(report);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Budget-only scene save failed.");
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }
    }
}
