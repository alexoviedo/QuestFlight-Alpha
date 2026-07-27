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
            Assert.That(placements, Has.Length.EqualTo(35));
            Assert.That(placements.Count(value => value.model_family == "powered_c172"), Is.EqualTo(17));
            Assert.That(placements.Count(value => value.model_family == "sailplane"), Is.EqualTo(18));
            Assert.That(
                placements.All(value =>
                    !float.IsNaN(value.base_y_meters) &&
                    !float.IsInfinity(value.base_y_meters) &&
                    value.base_y_meters > -5f &&
                    value.base_y_meters < 5f),
                Is.True,
                "Aircraft base heights use the terrain's local elevation datum and may be negative north of the runway.");
            Assert.That(placements.All(value => value.color_rgba != null && value.color_rgba.Length == 4), Is.True);
            Assert.That(
                placements.All(value =>
                    value.placement_v2 != null &&
                    value.placement_v2.orthophoto_source_pixel != null &&
                    value.placement_v2.orthophoto_source_pixel.Length == 2 &&
                    value.placement_v2.orthophoto_source_axis_pixels != null &&
                    value.placement_v2.orthophoto_source_axis_pixels.Length == 4),
                Is.True);
            foreach (Placement placement in placements)
            {
                float expectedX = placement.placement_v2.orthophoto_source_pixel[0] / 8192f * 4000f - 2000f;
                float expectedZ = 2000f - placement.placement_v2.orthophoto_source_pixel[1] / 8192f * 4000f;
                Assert.That(placement.x_meters, Is.EqualTo(expectedX).Within(0.001f));
                Assert.That(placement.z_meters, Is.EqualTo(expectedZ).Within(0.001f));
                Assert.That(
                    placement.placement_v2.orthophoto_guid,
                    Is.EqualTo("0021365d50509d2abe127a826626ee6e"));
                Assert.That(
                    placement.placement_v2.orthophoto_sha256,
                    Is.EqualTo("143c4946c06464dbdea1567cd5fe284acc4c5ba7718a38900a2e83a8eccbfb53"));
            }

            Placement[] sailplanes = placements
                .Where(value => value.model_family == "sailplane").ToArray();
            Assert.That(
                sailplanes.All(value =>
                    value.wing_down_side == "left" || value.wing_down_side == "right"),
                Is.True);
            Assert.That(
                sailplanes.All(value =>
                    Mathf.Abs(value.roll_degrees) >= 7.4f &&
                    Mathf.Abs(value.roll_degrees) <= 7.7f),
                Is.True,
                "Sailplanes must rest on their main wheel and one wingtip.");
            Assert.That(
                placements.Where(value => value.model_family == "powered_c172")
                    .All(value => Mathf.Abs(value.roll_degrees) < 0.001f),
                Is.True);
            foreach (Placement placement in placements)
            {
                int[] axis = placement.placement_v2.orthophoto_source_axis_pixels;
                float expectedHeading = Mathf.Atan2(
                    axis[0] - axis[2],
                    axis[3] - axis[1]) * Mathf.Rad2Deg;
                if (expectedHeading < 0f) expectedHeading += 360f;
                Assert.That(
                    Mathf.DeltaAngle(placement.yaw_degrees, expectedHeading),
                    Is.EqualTo(0f).Within(0.01f));
            }
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
            Mesh lod2 = renderers[2].GetComponent<MeshFilter>().sharedMesh;
            Assert.That(lod0.triangles.Length / 3, Is.EqualTo(11822));
            Assert.That(lod1.triangles.Length / 3, Is.EqualTo(210));
            Assert.That(lod2.triangles.Length / 3, Is.EqualTo(210));

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
            public float x_meters;
            public float base_y_meters;
            public float z_meters;
            public float yaw_degrees;
            public float pitch_degrees;
            public float roll_degrees;
            public string wing_down_side;
            public int[] color_rgba;
            public PlacementEvidence placement_v2;
        }

        [Serializable]
        private sealed class PlacementEvidence
        {
            public int[] orthophoto_source_pixel;
            public int[] orthophoto_source_axis_pixels;
            public string orthophoto_guid;
            public string orthophoto_sha256;
        }
    }
}
