#if UNITY_INCLUDE_TESTS
using System.Linq;
using System.Collections;
using System.Reflection;
using QuestFlightLab.Flight.Backends;
using NUnit.Framework;
using QuestFlightLab.Environment;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Tests.PlayMode
{
    public sealed class ProductionVerticalSliceAcceptancePlayModeTests
    {
        private const string WorldResourcePath = "QuestFlightLab/Production/ProductionWorldRoot";
        private GameObject _worldInstance;

        [TearDown]
        public void TearDown()
        {
            if (_worldInstance != null) Object.DestroyImmediate(_worldInstance);
        }

        [Test]
        [Description("Synchronous Editor proof of the authored coordinator's actual Start boundary; not a frame or Android startup test.")]
        public void ProductionSceneBecomesReadyWithoutLegacyRepairOrDefaultDebugUi()
        {
            Transform world = LoadProductionWorld();
            string[] forbidden =
            {
                "QuestFirstViewRuntimeRepair", "FirstViewPlaytestDiagnostics", "PlaytestHud",
                "CockpitInstrumentPanel", "TrainingModeController", "SceneryModeController",
                "MeshSceneryProvider", "SplatSceneryProvider", "QuestSplatRuntimeGateController"
            };
            foreach (string typeName in forbidden)
                Assert.That(HasComponentNamed(world, typeName), Is.False, typeName);
            Assert.That(ComponentsInWorld<Canvas>(world).Count(canvas => canvas.gameObject.activeInHierarchy), Is.Zero,
                "Production startup must not cover the cockpit with a legacy/debug Canvas.");

            FlightDynamicsCoordinator authority = ComponentsInWorld<FlightDynamicsCoordinator>(world).Single();
            Assert.That(authority.IsInitialized, Is.False,
                "Instantiation must not label the still-pending Start coroutine ready.");
            Assert.That(QuestTemporalVisualGateRecorder.EvaluateSceneReadiness(out string status), Is.False);
            Assert.That(status, Does.Contain("preparing"));
            // Editor preparation validates the ordinary package directory synchronously.
            // Exercise the actual Start iterator once; do not emulate asynchronous player frames.
            Assert.That(Application.isEditor, Is.True, "This fixture proves only synchronous Editor startup.");
            Assert.That(AlexOviedo.QuestFlight.JSBSim.JSBSimRuntimeData.TryGetPreparedDataRoot(
                out _, out string preparationError), Is.True, preparationError);
            MethodInfo startMethod = typeof(FlightDynamicsCoordinator).GetMethod("Start",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(startMethod, Is.Not.Null);
            IEnumerator startup = startMethod.Invoke(authority, null) as IEnumerator;
            Assert.That(startup, Is.Not.Null);
            try
            {
                Assert.That(startup.MoveNext(), Is.False,
                    "Prepared Editor data must complete actual Start without an asynchronous yield.");
            }
            finally { (startup as System.IDisposable)?.Dispose(); }
            Assert.That(QuestTemporalVisualGateRecorder.EvaluateSceneReadiness(out status), Is.True, status);
            Assert.That(status, Does.Contain("authored production hierarchy"));
            Assert.That(authority.CurrentState.IsFinite, Is.True);
            authority.enabled = false;
            Assert.That(QuestTemporalVisualGateRecorder.EvaluateSceneReadiness(out status), Is.False);
            Assert.That(status, Does.Contain("disabled"));
            authority.enabled = true;
            Assert.That(authority.TryGetAuthorityReadiness(out status), Is.True, status);
        }

        [Test]
        public void ProductionMountainProbeRecognizesAuthoredUsgsMeshesAsImmutableAuthority()
        {
            Transform world = LoadProductionWorld();
            ProductionVerticalSliceRoot marker = ComponentsInWorld<ProductionVerticalSliceRoot>(world).Single();
            ProductionEnvironmentRoot environment = marker.EnvironmentRoot.GetComponentInChildren<ProductionEnvironmentRoot>(true);
            MountainTemporalStabilityProbe probe = environment.gameObject.AddComponent<MountainTemporalStabilityProbe>();
            probe.autoCaptureInPlayer = false;
            probe.Initialize(environment.transform, environment.transform);
            MountainTemporalStabilityReport report = probe.CaptureSingleFrameForTest(marker.AircraftRig.TrackedCamera);

            Assert.That(report.realDataRootActive, Is.True);
            Assert.That(report.expectedRendererCount, Is.EqualTo(4));
            Assert.That(report.immutableTransforms, Is.True, string.Join("; ", report.violations));
            Assert.That(report.immutableMeshes, Is.True, string.Join("; ", report.violations));
            Assert.That(report.immutableRendererSet, Is.True, string.Join("; ", report.violations));
            Assert.That(report.noTerrainLodOrDither, Is.True, string.Join("; ", report.violations));
            Assert.That(report.oneMountainSource, Is.True, string.Join("; ", report.violations));
            Assert.That(report.passed, Is.True, string.Join("; ", report.violations));
        }

        [Test]
        public void ProductionRuntimeBudgetMetadataMatchesVerticalSliceTargets()
        {
            Assert.That(QuestTemporalVisualGateRecorder.ProductionDrawCallGate, Is.EqualTo(120));
            Assert.That(QuestTemporalVisualGateRecorder.ProductionVisibleTriangleGate, Is.EqualTo(500000L));
            Assert.That(QuestTemporalVisualGateRecorder.ProductionSceneMaterialGate, Is.EqualTo(30));
            Assert.That(QuestTemporalVisualGateRecorder.ProductionTransparentMaterialSlotGate, Is.EqualTo(2));
        }

        private Transform LoadProductionWorld()
        {
            GameObject prefab = Resources.Load<GameObject>(WorldResourcePath);
            Assert.That(prefab, Is.Not.Null, "Authored production world resource is missing.");
            _worldInstance = Object.Instantiate(prefab);
            return _worldInstance.transform;
        }

        private static T[] ComponentsInWorld<T>(Transform world) where T : Component =>
            world.GetComponentsInChildren<T>(true);

        private static bool HasComponentNamed(Transform world, string typeName) =>
            world.GetComponentsInChildren<MonoBehaviour>(true).Any(component =>
                component != null && component.GetType().Name == typeName);
    }
}
#endif
