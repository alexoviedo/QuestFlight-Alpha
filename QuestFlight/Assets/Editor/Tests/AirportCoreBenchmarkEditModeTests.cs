using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Editor;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class AirportCoreBenchmarkEditModeTests
    {
        [Test]
        public void ManifestPinsV2SourceAndReadOnlyV3Reference()
        {
            Assert.That(AirportCoreBenchmarkManifest.SourceV2Sha,
                Is.EqualTo("eaccabc5e2cb9ccdcef30f825c69a013b83cea75"));
            Assert.That(AirportCoreBenchmarkManifest.ReferenceV3Sha,
                Is.EqualTo("46ffc235113532ff5c4204c9b3b0a11e5932b2ce"));
            Assert.That(AirportCoreBenchmarkManifest.MinimumHorizontalExtentMeters, Is.EqualTo(600f));
            Assert.That(AirportCoreBenchmarkManifest.MaximumHorizontalExtentMeters, Is.EqualTo(900f));
        }

        [Test]
        public void BenchmarkRootContainsNoRuntimeConstructionOrVariantMutationMethods()
        {
            string[] forbidden = { "Create", "Build", "Repair", "Ensure", "Instantiate", "Enable", "Disable", "Toggle" };
            MethodInfo[] declared = typeof(AirportCoreBenchmarkRoot)
                .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                            BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.That(declared.Any(method => forbidden.Any(token => method.Name.Contains(token))), Is.False,
                "The authored benchmark marker must never construct, repair, or toggle product content.");
        }

        [Test]
        public void SerializedBenchmarkPassesFailClosedStructuralValidator()
        {
            AirportCoreBenchmarkValidator.Report report = AirportCoreBenchmarkValidator.Validate();
            Assert.That(report.passed, Is.True, string.Join("\n", report.violations));
            Assert.That(report.classification,
                Is.EqualTo("PASS_AIRPORT_CORE_V4_2_STRUCTURAL_GATE_VISUAL_AND_HEADSET_REQUIRED"));
            Assert.That(report.adaptiveAirportTerrainTriangles,
                Is.InRange(1, AirportCoreV4Authoring.MaximumAdaptiveAirportTerrainTriangles));
            Assert.That(report.oneAtlasLightmapParametersValid, Is.True);
        }

        [Test]
        public void V42BindsErrorBoundedVisualTerrainWithoutChangingSurfaceOrPhysics()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductionEnvironmentRoot.PrefabAssetPath);
            Assert.That(prefab, Is.Not.Null);
            ProductionEnvironmentRoot contract = prefab.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(contract, Is.Not.Null);
            Assert.That(contract.nearTerrainMeshes, Has.Length.EqualTo(2));
            MeshFilter airport = contract.nearTerrainMeshes[0];
            Assert.That(AssetDatabase.GetAssetPath(airport.sharedMesh),
                Is.EqualTo(AirportCoreV4Authoring.AdaptiveAirportTerrainMeshPath));
            Assert.That(airport.sharedMesh.triangles.Length / 3,
                Is.InRange(1, AirportCoreV4Authoring.MaximumAdaptiveAirportTerrainTriangles));
            Assert.That(airport.GetComponent<MeshCollider>(), Is.Null);
            Assert.That(AssetDatabase.GetAssetPath(contract.authoritativeTerrainSurface),
                Is.EqualTo("Assets/Production/Environment/ReconstructionV2/Generated/" +
                           "KBDU_ProductionTerrainSurfaceV2.asset"));
            Mesh source = AssetDatabase.LoadAssetAtPath<Mesh>(EnvironmentReconstructionV2Authoring.AirportMeshPath);
            Assert.That(source, Is.Not.Null);
            Assert.That(source.triangles.Length / 3, Is.EqualTo(76800));
            TextAsset report = AssetDatabase.LoadAssetAtPath<TextAsset>(
                AirportCoreV4Authoring.AdaptiveAirportTerrainReportPath);
            Assert.That(report, Is.Not.Null);
            Assert.That(report.text, Does.Contain("PASS_ERROR_BOUNDED_ADAPTIVE_AIRPORT_VISUAL_MESH"));
        }

        [Test]
        public void V42UsesOneBoundedLightmapParametersAsset()
        {
            LightmapParameters parameters = AssetDatabase.LoadAssetAtPath<LightmapParameters>(
                AirportCoreV4Authoring.OneAtlasLightmapParametersPath);
            Assert.That(parameters, Is.Not.Null);
            Assert.That(parameters.limitLightmapCount, Is.True);
            Assert.That(parameters.maxLightmapCount, Is.EqualTo(1));
            Assert.That(parameters.bakedLightmapTag, Is.Zero);
        }

        [Test]
        public void BenchmarkSunRemainsBakedInEditorValidation()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductionEnvironmentRoot.PrefabAssetPath);
            Assert.That(prefab, Is.Not.Null);
            AirportCoreBenchmarkRoot root = prefab.GetComponentInChildren<AirportCoreBenchmarkRoot>(true);
            Assert.That(root, Is.Not.Null);
            Assert.That(root.benchmarkSun, Is.Not.Null);
            Assert.That(root.benchmarkSun.lightmapBakeType, Is.EqualTo(LightmapBakeType.Baked));
            Assert.That(root.TryValidateContract(out string report), Is.True, report);
        }

        [Test]
        public void BenchmarkUsesOneFilteredContextAndEditorOnlyPathBasedAbProvenance()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductionEnvironmentRoot.PrefabAssetPath);
            Assert.That(prefab, Is.Not.Null);
            AirportCoreBenchmarkRoot[] roots = prefab.GetComponentsInChildren<AirportCoreBenchmarkRoot>(true);
            Assert.That(roots, Has.Length.EqualTo(1));
            AirportCoreBenchmarkRoot root = roots[0];
            Assert.That(root.gameObject.activeSelf, Is.True);
            Assert.That(root.benchmarkContent, Is.Not.Null);
            Assert.That(root.benchmarkContent.gameObject.activeSelf, Is.True);
            Assert.That(root.executiveTerminal.transform.IsChildOf(root.benchmarkContent), Is.True);
            Assert.That(root.type2HangarRow.transform.IsChildOf(root.benchmarkContent), Is.True);
            Assert.That(root.fuelServiceContext.IsChildOf(root.benchmarkContent), Is.True);
            Assert.That(root.authoredApronProps.IsChildOf(root.benchmarkContent), Is.True);
            Assert.That(root.vegetationHeroCluster.IsChildOf(root.benchmarkContent), Is.True);
            Assert.That(root.filteredV2Context, Is.Not.Null);
            Assert.That(root.filteredV2Context.gameObject.activeSelf, Is.True);
            Assert.That(root.v2ContextOriginalMeshPaths, Has.Length.EqualTo(3));
            Assert.That(root.v4ContextFilteredMeshPaths, Has.Length.EqualTo(3));
            Assert.That(root.GetComponentsInChildren<Transform>(true).Any(item =>
                item.name == AirportCoreBenchmarkRoot.V2BaselineName || item.name == "FilteredV2Context"), Is.False);
            Assert.That(root.authoredApronProps.GetComponent<LODGroup>(), Is.Not.Null);
            Assert.That(root.authoredApronProps.GetComponentsInChildren<Renderer>(true), Has.Length.EqualTo(3));
            Assert.That(root.fuelServiceContext.GetComponent<LODGroup>(), Is.Not.Null);
            Assert.That(root.fuelServiceContext.GetComponentsInChildren<Renderer>(true), Has.Length.EqualTo(3));
            Assert.That(root.airportLightProbes.probePositions, Has.Length.EqualTo(18));
            Assert.That(root.airportLightProbes.probePositions.Select(item => item.y).Distinct().Count(), Is.EqualTo(2));
        }
    }
}
