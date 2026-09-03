using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor.Tests
{
    public sealed class ReservoirWatercraftV1EditModeTests
    {
        private const string Root = "Assets/Production/Environment/ReservoirWatercraftV1";
        private const string ManifestPath = Root + "/ReservoirWatercraftPlacementsV1.json";
        private const string SourcesPath = Root + "/ReservoirWatercraftSourcesV1.json";
        private const string DerivativesPath = Root + "/ReservoirWatercraftDerivativesV1.json";
        private const string AlignmentPath = Root + "/ReservoirWatercraftSurfaceAlignmentV1.json";
        private const string ContextPath =
            "Assets/Production/Environment/ReconstructionV2/KBDU_ReferenceContextV2.json";
        private const string SharedMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_ParkedAircraft.mat";

        [Test]
        public void DiscoveryAndDerivativeReportsRemainReviewableAndHashBound()
        {
            SourceDocument sources = Read<SourceDocument>(SourcesPath);
            DerivativeDocument derivatives = Read<DerivativeDocument>(DerivativesPath);
            Assert.That(sources.distribution_status,
                Is.EqualTo("PRIVATE_PROJECT_SOURCE_NOT_REVIEWED_FOR_PUBLIC_RELEASE"));
            Assert.That(sources.sources.Length, Is.EqualTo(6));
            Assert.That(sources.sources.Count(source => source.status.StartsWith("ACCEPTED", StringComparison.Ordinal) ||
                                                       source.status.StartsWith("PRIVATE_SOURCE", StringComparison.Ordinal)),
                Is.EqualTo(5));
            Assert.That(sources.sources.Single(source => source.canonical_id == "sit_on_top_rescue_kayak").status,
                Does.StartWith("NOT_FOUND"));
            Assert.That(sources.sources.Where(source => !string.IsNullOrEmpty(source.archive_sha256)),
                Has.All.Matches<SourceRecord>(source => IsSha256(source.archive_sha256)));
            Assert.That(derivatives.derivatives.Length, Is.EqualTo(10));
            Assert.That(derivatives.derivatives, Has.All.Matches<DerivativeRecord>(item =>
                item.triangles > 0 && item.triangles <= 1200 && IsSha256(item.fbx_sha256)));
            Assert.That(derivatives.derivatives.Count(item => item.shipping_status == "SELECTED"), Is.EqualTo(6));
        }

        [Test]
        public void PlacementsAreDeterministicFinitePlausibleAndSemanticallyContained()
        {
            PlacementDocument manifest = Read<PlacementDocument>(ManifestPath);
            Assert.That(manifest.vertical_surface_authority,
                Is.EqualTo("ACTIVE_RENDERED_TERRAIN_MESH"));
            Assert.That(manifest.records.Length, Is.EqualTo(25));
            string[] ids = manifest.records.Select(record => record.stable_id).ToArray();
            Assert.That(ids.SequenceEqual(ids.OrderBy(id => id, StringComparer.Ordinal)), Is.True);
            Assert.That(manifest.records.Select(record => record.stable_id).Distinct().Count(),
                Is.EqualTo(manifest.records.Length));

            ContextDocument context = Read<ContextDocument>(ContextPath);
            ContextFeature reservoir = context.openstreetmap.features.Single(feature =>
                feature.category == "water" && feature.geometry_type == "polygon" &&
                feature.tags != null && feature.tags.name == "Boulder Reservoir");
            Vector2[] polygon = new Vector2[reservoir.points_q.Length / 2];
            for (int index = 0; index < polygon.Length; index++)
                polygon[index] = new Vector2(
                    reservoir.points_q[index * 2] * context.coordinate_quantization_meters,
                    reservoir.points_q[index * 2 + 1] * context.coordinate_quantization_meters);

            foreach (PlacementRecord record in manifest.records)
            {
                Assert.That(float.IsFinite(record.product_x_meters));
                Assert.That(float.IsFinite(record.product_z_meters));
                Assert.That(float.IsFinite(record.yaw_degrees));
                Assert.That(record.footprint_length_meters, Is.InRange(3.0f, 8.1f), record.stable_id);
                Assert.That(record.footprint_width_meters, Is.InRange(0.6f, 2.6f), record.stable_id);
                bool afloat = record.vertical_authority == "TERRAIN_OWNED_WATER_VISUAL_SURFACE";
                Assert.That(record.anchor_mode,
                    Is.EqualTo(afloat ? "MODEL_WATERLINE" : "MODEL_SUPPORT_PLANE"), record.stable_id);
                Assert.That(record.anchor_local_y_meters, Is.InRange(0f, 0.42f), record.stable_id);
                Assert.That(record.surface_clearance_meters, Is.InRange(0.019f, 0.026f), record.stable_id);
                foreach (Vector2 corner in FootprintCorners(record))
                    Assert.That(PointInPolygon(corner, polygon), Is.EqualTo(afloat), record.stable_id);
                Assert.That(record.product_z_meters, Is.GreaterThan(3200f), "Coot Lake exclusion: " + record.stable_id);
            }
        }

        [Test]
        public void SurfaceAlignmentIsBoundToTheActiveRenderedMeshAndWithinContactTolerance()
        {
            AlignmentDocument alignment = Read<AlignmentDocument>(AlignmentPath);
            PlacementDocument placements = Read<PlacementDocument>(ManifestPath);
            Assert.That(alignment.classification, Is.EqualTo("PASS_ACTIVE_RENDERED_SURFACE_ALIGNMENT"));
            Assert.That(alignment.vertical_surface_authority,
                Is.EqualTo("ACTIVE_RENDERED_TERRAIN_MESH"));
            Assert.That(alignment.surface_mesh_asset_path,
                Is.EqualTo("Assets/Production/Environment/Generated/Terrain_Mid12km_USGS_Mesh.asset"));
            Assert.That(alignment.surface_mesh_dependency_hash,
                Is.EqualTo(AssetDatabase.GetAssetDependencyHash(alignment.surface_mesh_asset_path).ToString()));
            Assert.That(alignment.placement_manifest_sha256,
                Is.EqualTo(Sha256(File.ReadAllBytes(Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", ManifestPath))))));
            Assert.That(alignment.records, Has.Length.EqualTo(placements.records.Length));
            Assert.That(alignment.records.Select(record => record.stable_id),
                Is.EqualTo(placements.records.Select(record => record.stable_id)));
            Assert.That(alignment.maximum_surface_tilt_degrees, Is.EqualTo(6f));
            Assert.That(alignment.maximum_plane_residual_meters, Is.EqualTo(0.035f).Within(0.00001f));
            Assert.That(alignment.records, Has.All.Matches<AlignmentRecord>(record =>
                float.IsFinite(record.surface_center_y_meters) &&
                record.surface_tilt_degrees <= alignment.maximum_surface_tilt_degrees &&
                record.maximum_plane_residual_meters <= alignment.maximum_plane_residual_meters &&
                record.lod0_anchor_error_meters <= 0.001f &&
                record.lod1_anchor_error_meters <= 0.001f));
            Assert.That(alignment.records.Where(record =>
                    record.anchor_mode == "MODEL_WATERLINE").All(record =>
                    record.surface_center_y_meters < -32f), Is.True,
                "Afloat craft must follow the terrain-owned rendered water surface, not the disabled -28.66 m mesh.");

            string authoring = File.ReadAllText(Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "Assets/Editor/ReservoirWatercraftAuthoring.cs")));
            Assert.That(authoring, Does.Contain("VisibleSurfaceSampler"));
            Assert.That(authoring, Does.Not.Contain("water_surface_y_meters"));
            Assert.That(authoring, Does.Not.Contain("+ 0.18f"));
        }

        [Test]
        public void SerializedFeatureUsesThreeCombinedOpaqueDrawsAndOneExistingMaterial()
        {
            GameObject prefab = PrefabUtility.LoadPrefabContents(ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                ProductionEnvironmentRoot contract = prefab.GetComponent<ProductionEnvironmentRoot>();
                Assert.That(contract, Is.Not.Null);
                Transform feature = prefab.transform.Find("ReservoirWatercraftRoot");
                Assert.That(feature, Is.Not.Null);
                Assert.That(contract.reservoirWatercraftRoot, Is.EqualTo(feature));
                Assert.That(feature.childCount, Is.EqualTo(3));
                Assert.That(feature.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(10));
                Assert.That(feature.GetComponentsInChildren<LODGroup>(true).Length, Is.EqualTo(3));
                Assert.That(feature.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(feature.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                Assert.That(feature.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);

                Material expected = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
                MeshRenderer[] renderers = feature.GetComponentsInChildren<MeshRenderer>(true);
                Assert.That(renderers.Length, Is.EqualTo(6));
                Assert.That(renderers, Has.All.Matches<MeshRenderer>(renderer =>
                    renderer.sharedMaterial == expected &&
                    renderer.shadowCastingMode == ShadowCastingMode.Off &&
                    !renderer.receiveShadows && renderer.sharedMaterial.renderQueue < 3000));
                Assert.That(renderers.Select(renderer => renderer.sharedMaterial).Distinct().Count(), Is.EqualTo(1));
                Assert.That(AssetDatabase.FindAssets("t:Texture2D", new[] { Root }), Is.Empty);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        [Test]
        public void BakedMeshesAreSingleSubmeshColoredBoundedAndContainNoMarkerPalette()
        {
            string[] meshGuids = AssetDatabase.FindAssets("t:Mesh", new[] { Root + "/Generated" });
            Assert.That(meshGuids.Length, Is.EqualTo(6));
            int lod0Triangles = 0;
            int lod1Triangles = 0;
            foreach (string guid in meshGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                Assert.That(mesh.subMeshCount, Is.EqualTo(1), path);
                Assert.That(mesh.colors32.Length, Is.EqualTo(mesh.vertexCount), path);
                Assert.That(mesh.colors32, Has.All.Matches<Color32>(color =>
                    !(color.r > 245 && color.g < 10 && color.b < 10) &&
                    !(color.g > 245 && color.r < 10 && color.b < 10) &&
                    !(color.b > 245 && color.r < 10 && color.g < 10)), path);
                int triangles = mesh.triangles.Length / 3;
                if (path.Contains("_LOD0")) lod0Triangles += triangles;
                else lod1Triangles += triangles;
            }
            Assert.That(lod0Triangles, Is.EqualTo(4682));
            Assert.That(lod1Triangles, Is.EqualTo(75));
            Assert.That(lod0Triangles, Is.LessThan(25000));
            Assert.That(AssetDatabase.IsValidFolder("Assets/__ReservoirWatercraftTemp"), Is.False);
        }

        [Test]
        public void ConvertedKayakShippingMeshesHaveNoWoodOrSourceAssetDependency()
        {
            string[] meshPaths = AssetDatabase.FindAssets("t:Mesh", new[] { Root + "/Generated" })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            string[] dependencies = AssetDatabase.GetDependencies(meshPaths, true);
            Assert.That(dependencies.Any(path => path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(dependencies.Any(path => path.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
            Assert.That(dependencies.Any(path => path.StartsWith("Assets/__ReservoirWatercraftTemp", StringComparison.Ordinal)), Is.False);
        }

        private static IEnumerable<Vector2> FootprintCorners(PlacementRecord record)
        {
            float halfWidth = record.footprint_width_meters * 0.5f;
            float halfLength = record.footprint_length_meters * 0.5f;
            float radians = record.yaw_degrees * Mathf.Deg2Rad;
            Vector2 forward = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
            Vector2 right = new Vector2(forward.y, -forward.x);
            Vector2 center = new Vector2(record.product_x_meters, record.product_z_meters);
            yield return center + forward * halfLength + right * halfWidth;
            yield return center + forward * halfLength - right * halfWidth;
            yield return center - forward * halfLength + right * halfWidth;
            yield return center - forward * halfLength - right * halfWidth;
        }

        private static bool PointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            bool inside = false;
            int previous = polygon.Count - 1;
            for (int current = 0; current < polygon.Count; current++)
            {
                Vector2 a = polygon[current];
                Vector2 b = polygon[previous];
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
                previous = current;
            }
            return inside;
        }

        private static T Read<T>(string path)
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(asset, Is.Not.Null, path);
            T value = JsonUtility.FromJson<T>(asset.text);
            Assert.That(value, Is.Not.Null, path);
            return value;
        }

        private static bool IsSha256(string value)
        {
            return value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
        }

        private static string Sha256(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            return string.Concat(hash.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }

        [Serializable] private sealed class PlacementDocument
        {
            public string vertical_surface_authority;
            public PlacementRecord[] records;
        }
        [Serializable] private sealed class PlacementRecord
        {
            public string stable_id;
            public float product_x_meters;
            public float product_z_meters;
            public float yaw_degrees;
            public float footprint_length_meters;
            public float footprint_width_meters;
            public string vertical_authority;
            public string anchor_mode;
            public float anchor_local_y_meters;
            public float surface_clearance_meters;
        }
        [Serializable] private sealed class SourceDocument { public string distribution_status; public SourceRecord[] sources; }
        [Serializable] private sealed class SourceRecord { public string canonical_id; public string archive_sha256; public string status; }
        [Serializable] private sealed class DerivativeDocument { public DerivativeRecord[] derivatives; }
        [Serializable] private sealed class DerivativeRecord { public int triangles; public string fbx_sha256; public string shipping_status; }
        [Serializable] private sealed class ContextDocument { public float coordinate_quantization_meters; public OsmDocument openstreetmap; }
        [Serializable] private sealed class OsmDocument { public ContextFeature[] features; }
        [Serializable] private sealed class ContextFeature { public string category; public string geometry_type; public ContextTags tags; public int[] points_q; }
        [Serializable] private sealed class ContextTags { public string name; }
        [Serializable] private sealed class AlignmentDocument
        {
            public string classification;
            public string vertical_surface_authority;
            public string surface_mesh_asset_path;
            public string surface_mesh_dependency_hash;
            public string placement_manifest_sha256;
            public float maximum_surface_tilt_degrees;
            public float maximum_plane_residual_meters;
            public AlignmentRecord[] records;
        }
        [Serializable] private sealed class AlignmentRecord
        {
            public string stable_id;
            public string anchor_mode;
            public float surface_center_y_meters;
            public float surface_tilt_degrees;
            public float maximum_plane_residual_meters;
            public float lod0_anchor_error_meters;
            public float lod1_anchor_error_meters;
        }
    }
}
