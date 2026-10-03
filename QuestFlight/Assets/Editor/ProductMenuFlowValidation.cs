using System;
using QuestFlightLab.Environment;
using UnityEditor;
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
                    var budget = root.CalculateBudget();
                    Debug.Log($"[MenuFlowValidation] World triangle count: {root.bakedTriangleCount} -> {budget.triangleCount}");
                    root.bakedRendererCount = budget.rendererCount;
                    root.bakedTriangleCount = budget.triangleCount;
                    root.bakedMaterialCount = budget.materialCount;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(root);
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
    }
}
