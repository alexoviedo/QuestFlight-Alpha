using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class AirportParkedAircraftV1EditModeTests
    {
        private const string ContextPrefab =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_BatchedContext.prefab";
        private const string Recipe =
            "Assets/Production/Environment/ReconstructionV2/KBDU_ContextBatchRecipeV2.json";
        private const string SourceManifest =
            "Assets/Production/Environment/AirportParkedAircraftV1/Data/" +
            "AirportParkedAircraftSourceManifestV1.json";

        [Test]
        public void ParkingContractContainsGroundedPoweredAndSailplaneRows()
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(Recipe);
            Assert.That(source, Is.Not.Null);
            ContextRecipe recipe = JsonUtility.FromJson<ContextRecipe>(source.text);
            Assert.That(recipe.parked_aircraft_batches, Has.Length.EqualTo(1));
            Placement[] placements = recipe.parked_aircraft_batches[0].placements;
            Assert.That(placements, Has.Length.EqualTo(17));
            Assert.That(placements.Count(value => value.model_family == "powered_c172"), Is.EqualTo(7));
            Assert.That(placements.Count(value => value.model_family == "sailplane"), Is.EqualTo(10));
            Assert.That(
                placements.All(value =>
                    !float.IsNaN(value.base_y_meters) &&
                    !float.IsInfinity(value.base_y_meters) &&
                    value.base_y_meters > -5f &&
                    value.base_y_meters < 5f),
                Is.True,
                "Aircraft base heights use the terrain's local elevation datum and may be negative north of the runway.");
            Assert.That(placements.All(value => value.color_rgba != null && value.color_rgba.Length == 4), Is.True);
        }

        [Test]
        public void RuntimeUsesOneSharedMaterialAndBoundedThreeLevelBatch()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContextPrefab);
            Assert.That(prefab, Is.Not.Null);
            Transform root = prefab.GetComponentsInChildren<Transform>(true)
                .Single(value => value.name == "parked_aircraft_airport");
            LODGroup group = root.GetComponent<LODGroup>();
            Assert.That(group, Is.Not.Null);
            Assert.That(group.fadeMode, Is.EqualTo(LODFadeMode.None));
            LOD[] lods = group.GetLODs();
            Assert.That(lods, Has.Length.EqualTo(3));
            MeshRenderer[] renderers = lods.SelectMany(value => value.renderers)
                .Cast<MeshRenderer>().ToArray();
            Assert.That(renderers, Has.Length.EqualTo(3));
            Assert.That(renderers.Select(value => value.sharedMaterial).Distinct().Count(), Is.EqualTo(1));
            Assert.That(renderers.All(value => value.sharedMaterial != null), Is.True);

            Mesh lod0 = renderers[0].GetComponent<MeshFilter>().sharedMesh;
            Mesh lod1 = renderers[1].GetComponent<MeshFilter>().sharedMesh;
            Assert.That(lod0.triangles.Length / 3, Is.EqualTo(11783));
            Assert.That(lod1.triangles.Length / 3, Is.EqualTo(204));

            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(Recipe);
            ContextRecipe recipe = JsonUtility.FromJson<ContextRecipe>(source.text);
            float minimumTerrainHeight = recipe.parked_aircraft_batches[0].placements
                .Min(value => value.base_y_meters);
            Assert.That(lod0.bounds.min.y, Is.EqualTo(minimumTerrainHeight + 0.025f).Within(0.002f),
                "The combined LOD0 gear plane must follow the lowest authored terrain stand plus clearance.");
        }

        [Test]
        public void OptimizedSourcesHaveCompleteRightsAndGroundPivotRecords()
        {
            TextAsset manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(SourceManifest);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.text, Does.Contain("\"license_spdx\": \"CC-BY-4.0\""));
            Assert.That(manifest.text, Does.Contain("\"license_spdx\": \"CC0-1.0\""));
            Assert.That(manifest.text, Does.Contain("\"raw_source_committed\": false"));
            Assert.That(manifest.text, Does.Contain("\"grounded_local_z\": true"));
            Assert.That(manifest.text, Does.Contain("\"shared_material_count\": 1"));
        }

        [Serializable]
        private sealed class ContextRecipe
        {
            public AircraftBatch[] parked_aircraft_batches;
        }

        [Serializable]
        private sealed class AircraftBatch
        {
            public Placement[] placements;
        }

        [Serializable]
        private sealed class Placement
        {
            public string model_family;
            public float base_y_meters;
            public int[] color_rgba;
        }
    }
}
