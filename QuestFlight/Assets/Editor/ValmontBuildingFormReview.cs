using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using QuestFlight.Editor;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    // Bounded offline diagnosis; never constructs or changes product hierarchy.
    public static class ValmontBuildingFormReview
    {
        [Serializable] private class Targets { public Target[] records; }
        [Serializable] private class Target { public int id; }
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private static object Field(object value, string name) => value.GetType().GetField(name).GetValue(value);

        public static void Diagnose()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_VALMONT_FORM_REVIEW_ROOT");
            var targets = new HashSet<int>(JsonUtility.FromJson<Targets>(
                File.ReadAllText(Path.Combine(root, "target_records.json"))).records.Select(r => r.id));
            Type authoring = typeof(FullMapBuildingClipmapAssetAuthoring);
            object db = authoring.GetMethod("LoadDatabase", PrivateStatic).Invoke(null, null);
            var terrain = AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(
                FullMapBuildingClipmapAssetAuthoring.TerrainSurfacePath);
            var compile = authoring.GetMethod("CompileStableFootprint", PrivateStatic);
            var rows = new List<string> { "id,source_vertices,compiled_vertices,area_error,boundary_error_m,terrain_y,source_base_y,height_m,roof_eligible,roof_rejection" };
            foreach (object page in (Array)Field(db, "pages"))
            foreach (object building in (Array)Field(page, "buildings"))
            {
                int id = (int)Field(building, "id");
                if (!targets.Contains(id)) continue;
                int[] ring = (int[])Field(building, "lod2_q");
                Vector2[] points = Enumerable.Range(0, ring.Length / 2).Select(i => new Vector2(
                    -12000 + (int)Field(page, "column") * 1000 + ring[i * 2] * 0.1f,
                    -12000 + (int)Field(page, "row") * 1000 + ring[i * 2 + 1] * 0.1f)).ToArray();
                Vector2[] compiled = (Vector2[])compile.Invoke(null, new object[] { points, building });
                object error = authoring.GetMethod("MeasureFootprintError", PrivateStatic).Invoke(null, new object[] { points, compiled });
                bool eligible = FullMapBuildingRoofTopology.TryCreate(compiled, (int)Field(building, "roof_id"), 0,
                    (int)Field(building, "roof_rise_dm") * 0.1f, (bool)Field(building, "landmark"), out _, out string reason);
                terrain.TrySample(points.Average(p => p.x), points.Average(p => p.y), out var sample);
                rows.Add(FormattableString.Invariant($"{id},{points.Length},{compiled.Length},{Field(error, "areaFraction")},{Field(error, "boundaryMeters")},{sample.heightMeters},{(int)Field(building, "base_y_dm") * 0.1f},{(int)Field(building, "height_dm") * 0.1f},{eligible},{reason}"));
            }
            File.WriteAllLines(Path.Combine(root, "diagnosis.csv"), rows);
        }

        public static void VerifyRestored()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_VALMONT_FORM_REVIEW_ROOT");
            Diagnose();
            System.Environment.SetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR", Path.Combine(root, "restored"));
            ProductionVisualQaBatchRunner.RunSettledSceneryBenchmark();
        }
    }
}
