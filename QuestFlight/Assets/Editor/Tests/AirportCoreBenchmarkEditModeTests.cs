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
                Is.EqualTo("PASS_AIRPORT_CORE_V4_1_STRUCTURAL_GATE_VISUAL_AND_HEADSET_REQUIRED"));
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
