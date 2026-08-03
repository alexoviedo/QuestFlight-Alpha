using System;
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class AirportHeroCorridorEditModeTests
    {
        [Serializable]
        private sealed class GenerationReport
        {
            public AssetRecord[] assets = Array.Empty<AssetRecord>();
        }

        [Serializable]
        private sealed class AssetRecord
        {
            public string family;
            public int footprint_vertex_count;
            public bool wall_winding_normalized;
            public string roof_style;
            public float maximum_footprint_error_m;
        }

        [Test]
        public void SerializedCorridorOwnsAllHeroBuildingsWithOneStableDraw()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnvironmentRoot.PrefabAssetPath);
            Assert.That(prefab, Is.Not.Null);
            AirportHeroCorridorRoot[] roots =
                prefab.GetComponentsInChildren<AirportHeroCorridorRoot>(true);
            Assert.That(roots, Has.Length.EqualTo(1));
            AirportHeroCorridorRoot root = roots[0];
            Assert.That(root.TryValidate(out string report), Is.True, report);
            Assert.That(root.representedAddresses,
                Has.Length.EqualTo(AirportHeroCorridorRoot.ExpectedBuildingCount));
            Assert.That(root.representedOsmIds.Distinct().Count(),
                Is.EqualTo(AirportHeroCorridorRoot.ExpectedBuildingCount));
            Assert.That(root.corridorLodGroup.GetLODs(), Has.Length.EqualTo(1));
            Assert.That(root.corridorLodGroup.GetLODs().Single().renderers, Has.Length.EqualTo(1));
            Material[] materials = root.corridorLodGroup.GetLODs()
                .SelectMany(lod => lod.renderers)
                .Select(renderer => renderer.sharedMaterial)
                .Distinct()
                .ToArray();
            Assert.That(materials, Has.Length.EqualTo(1));
            Assert.That(AssetDatabase.GetAssetPath(materials[0]),
                Is.EqualTo("Assets/Production/Environment/AirportCoreV4/Materials/" +
                           "AirportCoreHangarKitAtlas.mat"));
        }

        [Test]
        public void GenerationManifestRequiresOutwardWallsExactFootprintsAndResidentialGables()
        {
            TextAsset manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Production/Environment/AirportHeroCorridorV1/Data/" +
                "AirportHeroCorridorGenerationReportV1.json");
            Assert.That(manifest, Is.Not.Null);
            GenerationReport report = JsonUtility.FromJson<GenerationReport>(manifest.text);
            Assert.That(report.assets,
                Has.Length.EqualTo(AirportHeroCorridorRoot.ExpectedBuildingCount));
            Assert.That(report.assets.All(item =>
                item.footprint_vertex_count >= 3 &&
                item.wall_winding_normalized &&
                item.maximum_footprint_error_m <= 0.01f), Is.True);
            Assert.That(report.assets
                .Where(item => string.Equals(item.family, "residential",
                    StringComparison.Ordinal))
                .All(item => string.Equals(item.roof_style, "exact_footprint_gable",
                    StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void CorridorUsesOneBoundedHeroMeshWithoutShapePopping()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnvironmentRoot.PrefabAssetPath);
            AirportHeroCorridorRoot root =
                prefab.GetComponentInChildren<AirportHeroCorridorRoot>(true);
            LOD lod = root.corridorLodGroup.GetLODs().Single();
            Mesh mesh = lod.renderers.Single().GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.triangles.Length / 3, Is.LessThanOrEqualTo(13000));
            Assert.That(lod.screenRelativeTransitionHeight, Is.EqualTo(0.008f).Within(0.0001f));
            Assert.That(root.corridorLodGroup.fadeMode, Is.EqualTo(LODFadeMode.None));
        }

        [Test]
        public void GenericBuildingLayerExcludesAllActiveHeroFootprints()
        {
            TextAsset manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Production/Environment/PhotorealAerialSurfaceV1/Buildings/" +
                "ProductionBuildingSurfaceLayer_Manifest.json");
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.text, Does.Contain("\"hero_exclusion_count\": 83"));
            foreach (long osmId in new long[]
                     {
                         77434544, 77499385, 77499386, 77499387, 77499388, 77499394,
                         78054436, 78054447, 78054451, 78054452, 78054454, 78054456,
                         78054458, 78054459, 78054461, 78054463, 78054465, 78054470,
                         78054473, 78060787
                     })
                Assert.That(manifest.text, Does.Contain($"\"osm_id\": {osmId}"),
                    $"OSM {osmId} must be excluded from the generic building layer.");
        }

        [Test]
        public void CorridorAddsNoRuntimeCameraLightOrPhysicsAuthority()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnvironmentRoot.PrefabAssetPath);
            AirportHeroCorridorRoot root =
                prefab.GetComponentInChildren<AirportHeroCorridorRoot>(true);
            Assert.That(root.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
        }
    }
}
