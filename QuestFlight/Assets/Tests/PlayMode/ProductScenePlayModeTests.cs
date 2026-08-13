#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace QuestFlight.PlayModeTests
{
    public sealed class ProductScenePlayModeTests
    {
        private const string WorldResourcePath = "QuestFlightLab/Production/ProductionWorldRoot";
        private GameObject _worldInstance;

        [SetUp]
        public void LoadProductionScene()
        {
            GameObject authoredWorld = Resources.Load<GameObject>(WorldResourcePath);
            Assert.That(authoredWorld, Is.Not.Null,
                "The one production scene must resolve its serialized ProductionWorldRoot prefab.");
            _worldInstance = UnityEngine.Object.Instantiate(authoredWorld);
            _worldInstance.name = "ProductionWorldRoot";
        }

        [TearDown]
        public void UnloadProductionScene()
        {
            if (_worldInstance != null) UnityEngine.Object.DestroyImmediate(_worldInstance);
        }

        [Test]
        public void ExplicitXrSeatHierarchyExistsBeforeFirstInteractiveFrame()
        {
            GameObject root = _worldInstance;
            Assert.That(root, Is.Not.Null);
            Assert.That(root.transform.Find("AircraftSimulationRoot/CenterOfGravityReference"), Is.Not.Null);
            Assert.That(root.transform.Find("AircraftSimulationRoot/AircraftVisualRoot"), Is.Not.Null);
            Assert.That(root.transform.Find(
                "AircraftSimulationRoot/AircraftVisualRoot/PilotSeatAnchor/UserViewCalibrationOffset/ProductOVRCameraRig/TrackingSpace/CenterEyeAnchor/Main Camera"), Is.Not.Null);
            Assert.That(AllTransforms().Count(transform =>
                transform.name == "ProductOVRCameraRig" &&
                transform.GetComponent<OVRCameraRig>() != null), Is.EqualTo(1));
        }

        [Test]
        public void CameraTrackingAndSinglePhysicsAuthorityRemainUnambiguous()
        {
            Camera[] cameras = _worldInstance.GetComponentsInChildren<Camera>(true);
            Camera mainCamera = cameras.SingleOrDefault(camera => camera.name == "Main Camera");
            Assert.That(mainCamera, Is.Not.Null);
            Assert.That(cameras.Count(camera => camera.enabled && camera.gameObject.activeInHierarchy), Is.EqualTo(1),
                "Only the tracked Main Camera may be enabled at startup; disabled external QA cameras are allowed.");
            Assert.That(QuestFlightLab.Runtime.TrackedXrCameraPoseDriver
                .HasRequiredBindings(mainCamera), Is.True);

            MonoBehaviour[] behaviours = _worldInstance.GetComponentsInChildren<MonoBehaviour>(true);
            MonoBehaviour[] enabledCoordinators = behaviours.Where(behaviour =>
                    behaviour != null && behaviour.enabled &&
                    behaviour.GetType().Name == "FlightDynamicsCoordinator")
                .ToArray();
            Assert.That(enabledCoordinators.Length, Is.EqualTo(1));
            object requestedBackend = enabledCoordinators[0].GetType()
                .GetField("requestedBackend")?.GetValue(enabledCoordinators[0]);
            Assert.That(requestedBackend?.ToString(), Is.EqualTo("JSBSimNativeDevelopment"));
            Assert.That(behaviours.Any(behaviour => behaviour != null &&
                behaviour.GetType().Name == "SimpleAircraftPhysics"), Is.False);
            Assert.That(behaviours.Any(behaviour => behaviour != null &&
                behaviour.GetType().Name.IndexOf("JSBSim", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
        }

        [Test]
        public void ProductSceneContainsNoLegacyRuntimeConstructionLayer()
        {
            string[] forbidden =
            {
                "QuestFirstViewRuntimeRepair",
                "KbduInspiredWorldBuilder",
                "RealKbduEnvironmentBuilder",
                "SceneryModeController",
                "MeshSceneryProvider",
                "SplatSceneryProvider",
                "TrainingModeController",
                "PlaytestHud"
            };
            string[] present = _worldInstance.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(behaviour => behaviour != null)
                .Select(behaviour => behaviour.GetType().Name)
                .ToArray();
            foreach (string component in forbidden) Assert.That(present, Does.Not.Contain(component));
        }

        private Transform[] AllTransforms()
        {
            return _worldInstance != null
                ? _worldInstance.GetComponentsInChildren<Transform>(true)
                : Array.Empty<Transform>();
        }
    }
}
#endif
