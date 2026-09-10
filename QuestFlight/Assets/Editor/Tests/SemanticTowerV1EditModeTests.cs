using System;
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
    public sealed class SemanticTowerV1EditModeTests
    {
        private const string EnvironmentPath = ProductionEnvironmentRoot.PrefabAssetPath;
        private const string SourceManifestPath =
            "Assets/Production/Environment/SemanticTowersV1/SOURCE_MANIFEST.json";

        [Test]
        public void ProductContainsExactlyOneSerializedStaticSemanticTowerRoot()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            Assert.That(environment, Is.Not.Null);
            SemanticTowerRoot[] roots = environment.GetComponentsInChildren<SemanticTowerRoot>(true);
            Assert.That(roots, Has.Length.EqualTo(1));
            SemanticTowerRoot root = roots.Single();
            Assert.That(root.name, Is.EqualTo(SemanticTowerRoot.RootName));
            Assert.That(root.transform.parent, Is.EqualTo(environment.transform));
            Assert.That(root.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(root.gameObject.isStatic, Is.True);
            Assert.That(root.TryValidateContract(out string report), Is.True, report);
            Assert.That(environment.GetComponent<ProductionEnvironmentRoot>().semanticTowerRoot,
                Is.SameAs(root));
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
        }

        [Test]
        public void EditorValidationStillRejectsUnmarkedTowerRenderers()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath)
                .GetComponentInChildren<SemanticTowerRoot>(true);
            var copy = UnityEngine.Object.Instantiate(source.gameObject);
            try
            {
                copy.name = SemanticTowerRoot.RootName;
                var root = copy.GetComponent<SemanticTowerRoot>();
                root.authoredRenderers[0].gameObject.isStatic = false;
                Assert.That(root.TryValidateContract(out string report), Is.False);
                Assert.That(report, Does.Contain("not marked static"));
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        [Test]
        public void GovernmentAndLocalSourceProvenanceIsFrozen()
        {
            SemanticTowerAuthorityManifest.Document authority = SemanticTowerAuthorityManifest.Load();
            Assert.That(authority.coordinate_frame,
                Is.EqualTo("WGS84_AEQD_LAT40.03936527_LON-105.22608958_XEAST_ZNORTH_METERS"));
            Assert.That(authority.sources.Select(source => source.authority), Is.EquivalentTo(new[]
            {
                "FAA_DAILY_DIGITAL_OBSTACLE_FILE",
                "FCC_ASR_COMPLETE_REGISTRATION_DATABASE",
                "OPENSTREETMAP_OVERPASS_VERTICAL_STRUCTURES",
                "CITY_OF_BOULDER_2020_PICTOMETRY_9IN"
            }));
            Assert.That(authority.sources.Take(3).All(source =>
                Uri.TryCreate(source.source_url, UriKind.Absolute, out _) &&
                DateTimeOffset.TryParse(source.acquired_utc, out _) &&
                source.sha256.Length == 64), Is.True);
            Assert.That(authority.product_half_extent_meters, Is.EqualTo(12000f));
            Assert.That(authority.inventory_safety_margin_meters, Is.EqualTo(500f));
            Assert.That(authority.inventory_summary.faa_obstacles_in_product_bounds, Is.EqualTo(91));
            Assert.That(authority.inventory_summary.faa_obstacles_in_inventory_extent, Is.EqualTo(101));
            Assert.That(authority.inventory_summary.faa_vertical_candidates_at_least_15m, Is.EqualTo(25));
            Assert.That(authority.inventory_summary.faa_vertical_candidates_in_inventory_extent, Is.EqualTo(26));
            Assert.That(authority.inventory_summary.osm_vertical_elements_in_query, Is.EqualTo(1995));
            Assert.That(authority.inventory_summary.osm_vertical_elements_overlapping_building_shells,
                Is.EqualTo(26));
        }

        [Test]
        public void AuthorityManifestCarriesOnlyExactConservativeShellDecision()
        {
            SemanticTowerAuthorityManifest.Document authority = SemanticTowerAuthorityManifest.Load();
            Assert.That(authority.exclusions, Has.Length.EqualTo(1));
            Assert.That(authority.exclusions.Single().building_id, Is.EqualTo(52188));
            Assert.That(authority.exclusions.Single().building_source_id,
                Is.EqualTo("COUNTY:1N7026090001:26017"));
            Assert.That(authority.exclusions.Single().authority_distance_meters,
                Is.LessThanOrEqualTo(authority.exclusions.Single().horizontal_accuracy_radius_meters));
            Assert.That(authority.structures.SelectMany(record => record.excluded_building_ids),
                Is.EqualTo(new[] { 52188 }));
            Assert.That(authority.structures.SelectMany(record => record.preserved_building_ids),
                Does.Contain(45081), "The visible communications equipment shelter must be preserved.");
            Assert.That(authority.exclusions.Any(record => record.building_id == 45081), Is.False);

            int[] protectedNamedHighRiseComponents =
                { 34109, 34111, 34112, 34113, 34284, 34427, 34429, 34431, 34530 };
            Assert.That(authority.exclusions.Select(record => record.building_id)
                .Intersect(protectedNamedHighRiseComponents), Is.Empty);
        }

        [Test]
        public void StructuresUseAuthorityDimensionsTerrainAnchorsAndNoUnauthorizedLights()
        {
            SemanticTowerAuthorityManifest.Document authority = SemanticTowerAuthorityManifest.Load();
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            SemanticTowerRoot root = environment.GetComponentInChildren<SemanticTowerRoot>(true);
            ProductionTerrainSurfaceAsset terrain = AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(
                "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");

            Assert.That(root.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(root.authoredObstructionLightCount, Is.Zero);
            Assert.That(root.authoredObstructionLightSystemCount, Is.EqualTo(1));
            Assert.That(root.authoredObstructionFixtureCount, Is.EqualTo(3));
            foreach (SemanticTowerAuthorityManifest.StructureRecord record in authority.structures)
            {
                Transform structure = root.transform.Find(record.stable_id);
                Assert.That(structure, Is.Not.Null, record.stable_id);
                Assert.That(structure.localPosition.x, Is.EqualTo(record.product_x_meters).Within(0.01f));
                Assert.That(structure.localPosition.z, Is.EqualTo(record.product_z_meters).Within(0.01f));
                Assert.That(terrain.TrySample(record.product_x_meters, record.product_z_meters,
                    out ProductionTerrainSample sample), Is.True);
                Assert.That(structure.localPosition.y, Is.EqualTo(sample.heightMeters).Within(0.01f));
                Assert.That(record.terrain_elevation_product_meters,
                    Is.EqualTo(sample.heightMeters).Within(0.01f));
                Assert.That(record.terrain_elevation_msl_meters,
                    Is.EqualTo(sample.heightMeters + authority.origin_elevation_msl_meters).Within(0.01f));
                Assert.That(record.baked_top_elevation_msl_meters,
                    Is.EqualTo(record.terrain_elevation_msl_meters + record.height_agl_meters).Within(0.01f));
                Assert.That(record.amsl_difference_meters,
                    Is.EqualTo(record.baked_top_elevation_msl_meters - record.authority_top_elevation_msl_meters)
                        .Within(0.01f));
                Transform lod0Transform = structure.Find("LOD0");
                Mesh lod0 = lod0Transform.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(Mathf.DeltaAngle(lod0Transform.localEulerAngles.x, -90f),
                    Is.EqualTo(0f).Within(0.01f));
                Assert.That(lod0.bounds.min.z, Is.EqualTo(0f).Within(0.01f));
                Assert.That(lod0.bounds.size.z, Is.EqualTo(record.height_agl_meters).Within(0.05f));
            }
        }

        [Test]
        public void RenderingRemainsOpaqueStaticAndBounded()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            SemanticTowerRoot root = environment.GetComponentInChildren<SemanticTowerRoot>(true);
            Assert.That(root.authoredRenderers, Has.Length.EqualTo(4));
            Assert.That(root.structureLodGroups, Has.Length.EqualTo(2));
            Assert.That(root.bakedLod0TriangleCount, Is.LessThanOrEqualTo(10000));
            Assert.That(root.bakedMaterialCount, Is.EqualTo(1));
            Assert.That(root.authoredRenderers.All(renderer =>
                renderer.gameObject.isStatic && renderer.enabled &&
                renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows &&
                renderer.sharedMaterial.renderQueue == (int)RenderQueue.Geometry &&
                renderer.sharedMaterial.shader.name == "QuestFlight/Semantic Tower Opaque"), Is.True);
            Assert.That(root.authoredRenderers.Select(renderer => renderer.sharedMaterial).Distinct().Count(),
                Is.EqualTo(1));
            Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true), Has.Length.EqualTo(1));
            Assert.That(root.authoredRenderers.All(renderer =>
                renderer.GetComponent<MeshFilter>().sharedMesh.subMeshCount == 1), Is.True);
            Assert.That(root.authoredRenderers.Sum(renderer =>
                (long)renderer.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3),
                Is.LessThanOrEqualTo(10000));
            Assert.That(root.authoredRenderers.SelectMany(renderer =>
                renderer.sharedMaterial.GetTexturePropertyNames()).All(name =>
                rendererTextureIsNull(root.authoredRenderers[0].sharedMaterial, name)), Is.True);
        }

        [Test]
        public void AuthorityChronologyMappingLightingAndOwnershipAreExplicit()
        {
            SemanticTowerAuthorityManifest.Document authority = SemanticTowerAuthorityManifest.Load();
            Assert.That(authority.product_half_extent_meters, Is.EqualTo(12000f));
            Assert.That(authority.structures.All(record =>
                Math.Abs(record.product_x_meters) <= authority.product_half_extent_meters &&
                Math.Abs(record.product_z_meters) <= authority.product_half_extent_meters &&
                float.IsFinite(record.height_agl_meters) && record.height_agl_meters > 0f), Is.True);
            Assert.That(authority.structures.All(record =>
                record.chronology_status.Contains("2020", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(record.selected_archetype)), Is.True);
            Assert.That(authority.exclusions.All(exclusion => authority.structures.Count(record =>
                record.stable_id == exclusion.replacement_stable_id &&
                record.excluded_building_ids.Contains(exclusion.building_id)) == 1), Is.True);
            Assert.That(authority.structures.Single(record => record.fcc_asr_number == "1024061").lighting_code,
                Is.EqualTo("RED_NIGHT_TOP_FLASHING_PLUS_MIDPOINT_STEADY"));
            Assert.That(authority.structures.Single(record => record.faa_obstacle_id == "08-000121").lighting_code,
                Is.EqualTo("NONE"));
            Assert.That(authority.structures.Where(record => record.semantic_type == "BLDG-TWR")
                .SelectMany(record => record.excluded_building_ids ?? Array.Empty<int>()), Is.Empty);
            Assert.That(authority.inventory_summary.preserved_extreme_named_building_components,
                Is.EqualTo(33));

            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(contract.reservoirWatercraftRoot, Is.Not.Null);
            Assert.That(contract.reservoirWatercraftRoot.name, Is.EqualTo("ReservoirWatercraftRoot"));
            Material material = environment.GetComponentInChildren<SemanticTowerRoot>(true)
                .authoredRenderers[0].sharedMaterial;
            Assert.That(material.HasProperty("_SemanticTowerDiagnosticNight"), Is.True);
            Assert.That(material.GetFloat("_SemanticTowerDiagnosticNight"), Is.Zero);
            Assert.That(material.GetFloat("_SemanticTowerDiagnosticTime"), Is.EqualTo(-1f));
        }

        [Test]
        public void BinaryDerivativesHaveFrozenLicensedSourceRecords()
        {
            TextAsset manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(SourceManifestPath);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.text, Does.Contain("CC0-1.0"));
            Assert.That(manifest.text, Does.Contain("CC-BY-4.0"));
            Assert.That(manifest.text, Does.Contain("Gunnar Correa"));
            Assert.That(Sha256("Assets/Production/Environment/SemanticTowersV1/Derivatives/SemanticLatticeTowerV1.fbx"),
                Is.EqualTo("07909ff1cae1d3e652715c7d42ea3d1f27372e606fd485fa31c5822796ab41df"));
            Assert.That(Sha256("Assets/Production/Environment/SemanticTowersV1/Derivatives/SemanticIndustrialStackV1.fbx"),
                Is.EqualTo("3e8ccc535454171a3f3d8ac5b661a72aef88516dc17782fe491720732fc5a266"));
        }

        private static string Sha256(string assetPath)
        {
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            using SHA256 hash = SHA256.Create();
            return string.Concat(hash.ComputeHash(File.ReadAllBytes(absolute))
                .Select(value => value.ToString("x2")));
        }

        private static bool rendererTextureIsNull(Material material, string propertyName) =>
            material.GetTexture(propertyName) == null;
    }
}
