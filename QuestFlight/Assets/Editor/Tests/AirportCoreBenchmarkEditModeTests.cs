using System;
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
                Is.InRange(1, AirportPatchLidarConformanceAuthoring.MaximumOutputTriangles));
            Assert.That(report.oneAtlasLightmapParametersValid, Is.True);
        }

        [Test]
        public void AerialV1BindsConformedVisualTerrainWithoutChangingSurfaceOrPhysics()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductionEnvironmentRoot.PrefabAssetPath);
            Assert.That(prefab, Is.Not.Null);
            ProductionEnvironmentRoot contract = prefab.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(contract, Is.Not.Null);
            Assert.That(contract.nearTerrainMeshes, Has.Length.EqualTo(2));
            MeshFilter airport = contract.nearTerrainMeshes[0];
            Assert.That(AssetDatabase.GetAssetPath(airport.sharedMesh),
                Is.EqualTo(AirportPatchLidarConformanceAuthoring.OutputMeshPath));
            Assert.That(airport.sharedMesh.triangles.Length / 3,
                Is.InRange(1, AirportPatchLidarConformanceAuthoring.MaximumOutputTriangles));
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
            TextAsset conformance = AssetDatabase.LoadAssetAtPath<TextAsset>(
                AirportPatchLidarConformanceAuthoring.OutputReportPath);
            Assert.That(conformance, Is.Not.Null);
            AirportPatchLidarConformanceAuthoring.ConformanceReport parsed =
                JsonUtility.FromJson<AirportPatchLidarConformanceAuthoring.ConformanceReport>(conformance.text);
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.boundaryProbeCount, Is.GreaterThanOrEqualTo(4000));
            Assert.That(parsed.maximumBoundaryMismatchMeters,
                Is.LessThanOrEqualTo(AirportPatchLidarConformanceAuthoring.MaximumBoundaryMismatchMeters));
            Assert.That(parsed.interiorOriginalVertexChanges, Is.Zero);
            Assert.That(parsed.runwayProtectedVertexChanges, Is.Zero);
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
        public void BenchmarkProbeValidationFailsClosedWhenProbePositionsAreUnavailable()
        {
            GameObject fixture = new GameObject(AirportCoreBenchmarkRoot.GameObjectName);
            try
            {
                AirportCoreBenchmarkRoot root = fixture.AddComponent<AirportCoreBenchmarkRoot>();
                root.validateOnAwake = false;
                root.airportLightProbes = fixture.AddComponent<LightProbeGroup>();
                root.airportLightProbes.probePositions = null;

                string report = null;
                Assert.DoesNotThrow(() => root.TryValidateContract(out report));
                Assert.That(report, Does.Contain("airport light-probe volume"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        [Test]
        public void BenchmarkUsesMappedAerialReplacementAndEditorOnlyPathBasedAbProvenance()
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
            Assert.That(root.filteredV2Context.gameObject.activeSelf, Is.False);

            AerialSceneryRoot[] aerialRoots = prefab.GetComponentsInChildren<AerialSceneryRoot>(true);
            Assert.That(aerialRoots, Has.Length.EqualTo(1));
            AerialSceneryRoot aerialRoot = aerialRoots[0];
            Assert.That(aerialRoot.enabled && IsAuthoredActive(aerialRoot.transform, prefab.transform), Is.True);
            Assert.That(aerialRoot.buildingRoot, Is.Not.Null);
            Assert.That(IsAuthoredActive(aerialRoot.buildingRoot, aerialRoot.transform), Is.True);
            Assert.That(aerialRoot.disabledLegacyBuildingRoots.Count(item =>
                item == root.filteredV2Context.transform), Is.EqualTo(1));
            Assert.That(aerialRoot.buildingRoot.GetComponentsInChildren<Renderer>(true).Any(item =>
                item.enabled && IsAuthoredActive(item.transform, aerialRoot.transform)), Is.True);
            Assert.That(root.filteredV2Context.GetComponentsInChildren<Renderer>(true).Any(item =>
                item.enabled && IsAuthoredActive(item.transform, prefab.transform)), Is.False);
            Assert.That(root.TryValidateFilteredContextRepresentation(out string contextReport), Is.True,
                contextReport);

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

        [Test]
        public void FilteredContextContractPreservesActiveV4FallbackWhenAerialRootIsAbsent()
        {
            GameObject scope = new GameObject("AirportBenchmarkV4FallbackFixture");
            try
            {
                AirportCoreBenchmarkRoot root = Child(scope.transform, AirportCoreBenchmarkRoot.GameObjectName)
                    .gameObject.AddComponent<AirportCoreBenchmarkRoot>();
                root.filteredV2Context = CreateThreeLodContext(scope.transform);

                Assert.That(root.TryValidateFilteredContextRepresentation(out string validReport), Is.True,
                    validReport);
                Assert.That(validReport, Is.EqualTo("PASS_FILTERED_V2_CONTEXT_V4"));

                root.filteredV2Context.gameObject.SetActive(false);
                Assert.That(root.TryValidateFilteredContextRepresentation(out string invalidReport), Is.False);
                Assert.That(invalidReport, Does.Contain("must be active in the V4 variant"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scope);
            }
        }

        [Test]
        public void FilteredContextContractFailsClosedForIncompleteAerialReplacement()
        {
            GameObject scope = new GameObject("AirportBenchmarkAerialReplacementFixture");
            try
            {
                AirportCoreBenchmarkRoot root = Child(scope.transform, AirportCoreBenchmarkRoot.GameObjectName)
                    .gameObject.AddComponent<AirportCoreBenchmarkRoot>();
                root.filteredV2Context = CreateThreeLodContext(scope.transform);
                root.filteredV2Context.gameObject.SetActive(false);

                AerialSceneryRoot aerialRoot = Child(scope.transform, AerialSceneryRoot.GameObjectName)
                    .gameObject.AddComponent<AerialSceneryRoot>();
                aerialRoot.buildingRoot = Child(aerialRoot.transform, "Buildings");
                MeshRenderer replacementRenderer = aerialRoot.buildingRoot.gameObject.AddComponent<MeshRenderer>();
                aerialRoot.disabledLegacyBuildingRoots = new[] { root.filteredV2Context.transform };

                Assert.That(root.TryValidateFilteredContextRepresentation(out string validReport), Is.True,
                    validReport);
                Assert.That(validReport, Is.EqualTo("PASS_FILTERED_V2_CONTEXT_AERIAL_V1_REPLACEMENT"));

                aerialRoot.enabled = false;
                AssertFilteredContextFailure(root, "AerialSceneryRoot replacement must be active");
                aerialRoot.enabled = true;

                aerialRoot.buildingRoot.gameObject.SetActive(false);
                AssertFilteredContextFailure(root, "buildingRoot must be an active authored child");
                aerialRoot.buildingRoot.gameObject.SetActive(true);

                aerialRoot.disabledLegacyBuildingRoots = Array.Empty<Transform>();
                AssertFilteredContextFailure(root, "must appear exactly once");
                aerialRoot.disabledLegacyBuildingRoots = new[]
                    { root.filteredV2Context.transform, root.filteredV2Context.transform };
                AssertFilteredContextFailure(root, "must appear exactly once");
                aerialRoot.disabledLegacyBuildingRoots = new[] { root.filteredV2Context.transform };

                replacementRenderer.enabled = false;
                AssertFilteredContextFailure(root, "replacement building renderers are missing");
                replacementRenderer.enabled = true;

                root.filteredV2Context.gameObject.SetActive(true);
                AssertFilteredContextFailure(root, "legacy filtered-context renderer remains active");
                root.filteredV2Context.gameObject.SetActive(false);

                Child(scope.transform, "DuplicateAerialRepresentation")
                    .gameObject.AddComponent<AerialSceneryRoot>();
                AssertFilteredContextFailure(root, "expected exactly one AerialSceneryRoot replacement");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scope);
            }
        }

        private static void AssertFilteredContextFailure(AirportCoreBenchmarkRoot root, string expectedReportText)
        {
            Assert.That(root.TryValidateFilteredContextRepresentation(out string report), Is.False);
            Assert.That(report, Does.Contain(expectedReportText));
        }

        private static LODGroup CreateThreeLodContext(Transform parent)
        {
            Transform context = Child(parent, "FilteredContextFixture");
            LODGroup group = context.gameObject.AddComponent<LODGroup>();
            float[] transitionHeights = { 0.6f, 0.3f, 0.05f };
            LOD[] lods = new LOD[transitionHeights.Length];
            for (int index = 0; index < lods.Length; index++)
            {
                MeshRenderer renderer = Child(context, "LOD" + index).gameObject.AddComponent<MeshRenderer>();
                lods[index] = new LOD(transitionHeights[index], new Renderer[] { renderer });
            }
            group.SetLODs(lods);
            return group;
        }

        private static bool IsAuthoredActive(Transform candidate, Transform boundary)
        {
            for (Transform current = candidate; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == boundary) return true;
            }
            return false;
        }

        private static Transform Child(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }
    }
}
