#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Flight;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using QuestFlightLab.Environment;
using QuestFlight.Product;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace QuestFlightLab.Tests.PlayMode
{
    public sealed class ProductionVerticalSliceAircraftPlayModeTests
    {
        private const string WorldResourcePath = "QuestFlightLab/Production/ProductionWorldRoot";
        private GameObject _worldInstance;

        [TearDown]
        public void TearDown()
        {
            if (_worldInstance != null) Object.DestroyImmediate(_worldInstance);
        }

        [Test]
        public void ProductionSceneOwnsCompleteAircraftXrAndDemoHierarchy()
        {
            Transform world = LoadProductionWorld();
            string[] requiredPaths =
            {
                "ProductionVerticalSliceRoot",
                "EnvironmentRoot/ProductionEnvironmentRoot",
                "RunwayRoot",
                "ProductionInputRoot",
                "AircraftSimulationRoot/CenterOfGravityReference",
                "AircraftSimulationRoot/AircraftVisualRoot/ImportedAircraftExterior",
                "AircraftSimulationRoot/AircraftVisualRoot/ImportedCockpitInterior",
                "AircraftSimulationRoot/AircraftVisualRoot/AnimatedControlSurfaces",
                "AircraftSimulationRoot/AircraftVisualRoot/PilotSeatAnchor/UserViewCalibrationOffset/XR Origin/Camera Offset/Main Camera",
                "AircraftSimulationRoot/AircraftVisualRoot/PilotSeatAnchor/UserViewCalibrationOffset/XR Origin/Left Touch Controller",
                "AircraftSimulationRoot/AircraftVisualRoot/PilotSeatAnchor/UserViewCalibrationOffset/XR Origin/Right Touch Controller"
            };
            foreach (string path in requiredPaths) Assert.That(world.Find(path), Is.Not.Null, path);

            XROrigin[] origins = ComponentsInWorld<XROrigin>(world);
            Assert.That(origins, Has.Length.EqualTo(1));
            Assert.That(origins[0].isActiveAndEnabled, Is.True);
            Camera main = ComponentsInWorld<Camera>(world).Single(camera => camera.CompareTag("MainCamera"));
            Assert.That(TrackedXrCameraPoseDriver.HasRequiredBindings(main), Is.True);
            Assert.That(main.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(main.transform.localRotation, Is.EqualTo(Quaternion.identity));

            AircraftReferenceFrameRig rig = ComponentsInWorld<AircraftReferenceFrameRig>(world).Single();
            Assert.That(rig.ValidateHierarchy(), Is.True);
            Assert.That(rig.AircraftSimulationRoot.name, Is.EqualTo("AircraftSimulationRoot"));
            Assert.That(rig.PilotSeatAnchor.IsChildOf(rig.AircraftVisualRoot), Is.True);
            Assert.That(rig.XrOrigin.parent, Is.EqualTo(rig.UserViewCalibrationOffset));
            Assert.That(HasComponentNamed(world, "QuestFirstViewRuntimeRepair"), Is.False);

            Assert.That(ComponentsInWorld<DeterministicGamepadInputSource>(world), Has.Length.EqualTo(1));
            ProductDemoPilot demo = ComponentsInWorld<ProductDemoPilot>(world).Single();
            ProductControlInput mapper = ComponentsInWorld<ProductControlInput>(world).Single();
            Assert.That(mapper.DeterministicMode, Is.False);
            demo.StartDemo();
            Assert.That(mapper.DeterministicMode, Is.True);
            demo.StopDemo();

            FlightDynamicsCoordinator coordinator = ComponentsInWorld<FlightDynamicsCoordinator>(world).Single();
            Assert.That(coordinator.requestedBackend, Is.EqualTo(FlightDynamicsBackendKind.UnityPrototype));
            Assert.That(coordinator.preferNativeInDevelopment, Is.True);
            Assert.That(coordinator.simulationRoot, Is.EqualTo(rig.transform));
            Assert.That(coordinator.presentationRoot, Is.EqualTo(rig.transform));
            Assert.That(coordinator.initialConditionProvider, Is.Not.Null);
            Assert.That(coordinator.initialConditionProvider.spawnTransform, Is.EqualTo(rig.transform));
            Assert.That(coordinator.initialConditionProvider.derivePositionFromSpawnTransform, Is.True);
            Assert.That(coordinator.initialConditionProvider.deriveHeadingFromSpawnTransform, Is.True);
            Assert.That(ComponentsInWorld<SimpleAircraftPhysics>(world), Has.Length.EqualTo(1));

            Assert.That(coordinator.InitializeSelectedBackend(), Is.True, coordinator.LastError);
            ProductionEnvironmentRoot environment = ComponentsInWorld<ProductionEnvironmentRoot>(world).Single();
            Vector3 expectedSpawn = environment.transform.TransformPoint(Vector3.Lerp(
                environment.runway08EndLocal,
                environment.runway26EndLocal,
                0.06f) + Vector3.up * 1.25f);
            Assert.That(Vector3.Distance(rig.transform.position, expectedSpawn), Is.LessThan(0.02f));
            Assert.That(coordinator.ResetToConfiguredInitialConditions(), Is.True, coordinator.LastError);
            Assert.That(Vector3.Distance(rig.transform.position, expectedSpawn), Is.LessThan(0.02f));

            Vector3 runwayDirection = Vector3.ProjectOnPlane(
                environment.transform.TransformDirection(environment.runway26EndLocal - environment.runway08EndLocal),
                Vector3.up).normalized;
            Assert.That(Vector3.Angle(rig.transform.forward, runwayDirection), Is.LessThan(0.1f));
            Assert.That(Mathf.Abs(rig.transform.position.y - expectedSpawn.y), Is.LessThan(0.005f));
        }

        [Test]
        public void ProductionSeatProfileUsesMeasuredAdditionalAftCorrection()
        {
            Transform world = LoadProductionWorld();
            ProductionVerticalSliceRoot marker = ComponentsInWorld<ProductionVerticalSliceRoot>(world).Single();
            PilotSeatProfile profile = marker.PilotSeatProfile;
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.aircraftId, Is.EqualTo(CockpitViewpointPersistence.DefaultAircraftId));
            Assert.That(profile.seatAnchorLocalPosition, Is.EqualTo(new Vector3(-0.28f, 0.72f, 0f)));
            Assert.That(profile.nominalEyeLocalPosition, Is.EqualTo(new Vector3(-0.28f, 0.94f, -0.18f)));
            Assert.That(profile.nominalEyeLocalPosition.z, Is.EqualTo(-0.18f).Within(0.0001f));
            Assert.That(profile.EyeToPanelForwardDistanceMeters(), Is.EqualTo(0.497f).Within(0.001f));
            Assert.That(profile.targetEyeToPanelDistanceMeters, Is.EqualTo(0.497f).Within(0.001f));
            Assert.That(marker.AircraftRig.PilotSeatAnchor.localPosition,
                Is.EqualTo(profile.nominalEyeLocalPosition));
        }

        [Test]
        public void VisibilityProfilesAndBackendDrivenAnimationUseAuthoredParts()
        {
            Transform world = LoadProductionWorld();
            ProductionVerticalSliceRoot marker = ComponentsInWorld<ProductionVerticalSliceRoot>(world).Single();
            AircraftVisibilityProfileController visibility = marker.VisibilityProfiles;
            Transform exterior = marker.AircraftRig.AircraftVisualRoot.Find("ImportedAircraftExterior");
            Transform interior = marker.AircraftRig.AircraftVisualRoot.Find("ImportedCockpitInterior");
            Transform leftAileron = marker.AircraftRig.AircraftVisualRoot.Find("AnimatedControlSurfaces/LeftAileronPivot");
            Transform yoke = marker.AircraftRig.AircraftVisualRoot.Find(
                "AnimatedControlSurfaces/PilotYokePitchPivot/PilotYokeRollPivot");
            Renderer staticPropeller = exterior.GetComponentsInChildren<Renderer>(true).Single(renderer =>
                renderer.name == "Cessna_Exterior_Body_MAT_0.001_Body_MAT_0");
            Renderer rightWing = exterior.GetComponentsInChildren<Renderer>(true).Single(renderer =>
                renderer.name == "Cessna_Exterior_Body_MAT_0.002_Body_MAT_0");
            Assert.That(exterior.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(15));
            Assert.That(interior.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(15));
            Assert.That(leftAileron, Is.Not.Null);
            Assert.That(yoke, Is.Not.Null);

            Bounds propellerBounds = MeshBoundsInFrame(staticPropeller, marker.AircraftRig.AircraftVisualRoot);
            Assert.That(Mathf.Abs(propellerBounds.center.x), Is.LessThan(0.1f),
                "The cockpit occluder must remain the centerline propeller, not broad exterior geometry.");
            Assert.That(propellerBounds.size.x, Is.LessThan(0.2f));
            Assert.That(Mathf.Max(propellerBounds.size.y, propellerBounds.size.z), Is.GreaterThan(1.8f));
            Assert.That(Mathf.Min(propellerBounds.size.y, propellerBounds.size.z), Is.LessThan(0.35f));

            visibility.SetExternalView();
            Assert.That(exterior.GetComponentsInChildren<Renderer>(true).All(renderer => renderer.enabled), Is.True);
            Assert.That(staticPropeller.enabled, Is.True,
                "External view must retain the imported static propeller geometry.");
            Assert.That(interior.GetComponentsInChildren<Renderer>(true).All(renderer => !renderer.enabled), Is.True);
            visibility.SetCockpitView();
            Assert.That(interior.GetComponentsInChildren<Renderer>(true).All(renderer => renderer.enabled), Is.True);
            Assert.That(leftAileron.GetComponentsInChildren<Renderer>(true).All(renderer => renderer.enabled), Is.True);
            Assert.That(staticPropeller.enabled, Is.False,
                "Cockpit view must hide the static vertical propeller blade that blocks the runway/horizon.");
            Assert.That(rightWing.enabled, Is.True,
                "Cockpit view must not hide non-occluding exterior geometry such as the wing.");

            ProductionAircraftControlAnimator animator = marker.ControlAnimator;
            animator.CaptureAuthoredRestPosesForValidation();
            Quaternion aileronRest = leftAileron.localRotation;
            Quaternion yokeRest = yoke.localRotation;
            animator.SetControlStateForTest(new AircraftControlState
            {
                aileron = 1f,
                elevator = 0.5f,
                flaps = 1f,
                throttle = 1f,
                mixture = 1f
            });
            Assert.That(Quaternion.Angle(aileronRest, leftAileron.localRotation), Is.GreaterThan(15f));
            Assert.That(Quaternion.Angle(yokeRest, yoke.localRotation), Is.GreaterThan(40f));
            Assert.That(animator.LastAnimatedControls.aileron, Is.EqualTo(1f).Within(0.0001f));
        }

        private Transform LoadProductionWorld()
        {
            GameObject prefab = Resources.Load<GameObject>(WorldResourcePath);
            Assert.That(prefab, Is.Not.Null, "Authored production world resource is missing.");
            _worldInstance = Object.Instantiate(prefab);
            _worldInstance.name = "ProductionWorldRoot";
            return _worldInstance.transform;
        }

        private static T[] ComponentsInWorld<T>(Transform world) where T : Component
        {
            return world.GetComponentsInChildren<T>(true);
        }

        private static bool HasComponentNamed(Transform world, string typeName) =>
            world.GetComponentsInChildren<MonoBehaviour>(true).Any(component =>
                component != null && component.GetType().Name == typeName);

        private static Bounds MeshBoundsInFrame(Renderer renderer, Transform frame)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Assert.That(filter, Is.Not.Null, renderer.name + " has no MeshFilter.");
            Assert.That(filter.sharedMesh, Is.Not.Null, renderer.name + " has no shared mesh.");

            Bounds source = filter.sharedMesh.bounds;
            Bounds result = default;
            bool initialized = false;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 meshPoint = new Vector3(
                    x == 0 ? source.min.x : source.max.x,
                    y == 0 ? source.min.y : source.max.y,
                    z == 0 ? source.min.z : source.max.z);
                Vector3 framePoint = frame.InverseTransformPoint(renderer.transform.TransformPoint(meshPoint));
                if (!initialized)
                {
                    result = new Bounds(framePoint, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(framePoint);
                }
            }
            return result;
        }
    }
}
#endif
