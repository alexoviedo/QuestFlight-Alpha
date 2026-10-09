#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2.Tests
{
    public sealed class CockpitTouchInputV2EditModeTests
    {
        private GameObject _root;
        private ProductControlInput _input;
        private FlightDynamicsCoordinator _coordinator;
        private ProductionCockpitSystemsController _systems;
        private CockpitTouchInputV2Bridge _bridge;
        private Transform _yokeFrame;
        private Transform _engineFrame;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TouchInputV2TestRoot");
            _root.SetActive(false);
            GamepadInputReader gamepad =
                _root.AddComponent<GamepadInputReader>();
            DeterministicGamepadInputSource deterministic =
                _root.AddComponent<DeterministicGamepadInputSource>();
            _input = _root.AddComponent<ProductControlInput>();
            _input.ConfigureAuthoredSources(gamepad, deterministic);
            _coordinator =
                _root.AddComponent<FlightDynamicsCoordinator>();
            _coordinator.controls = _input;
            _systems =
                _root.AddComponent<ProductionCockpitSystemsController>();
            _systems.ConfigureAuthoredSource(
                _coordinator,
                default,
                Scenario());
            Assert.That(
                _systems.StepForTest(
                    State(),
                    Controls(),
                    default,
                    Scenario(),
                    0f),
                Is.True);
            _yokeFrame = Child("StableYokeFrame", _root.transform);
            _engineFrame = Child("StableEngineFrame", _root.transform);
            _bridge =
                _root.AddComponent<CockpitTouchInputV2Bridge>();
            _bridge.ConfigureAuthoredReferences(
                _coordinator,
                _yokeFrame,
                _engineFrame,
                _engineFrame,
                CockpitTouchInputV2Bridge.AuthoredYokeAxisLocal,
                CockpitTouchInputV2Bridge.AuthoredYokeAxisLocal,
                CockpitTouchInputV2Bridge
                    .AuthoredEngineFullForwardAxisLocal,
                CockpitTouchInputV2Bridge
                    .AuthoredEngineFullForwardAxisLocal);
            _bridge.ConfigureSystemsAuthority(_systems);
            _root.SetActive(true);
            Assert.That(
                _bridge.ValidateAuthoredReferences(out string error),
                Is.True,
                error);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void YokeGrabOverridesBothAxesAndRestoresUnderlyingInput()
        {
            _input.Current.aileron = 0.2f;
            _input.Current.elevator = -0.1f;
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        "pilot_yoke_grab",
                        CockpitTouchInteractionV2Target.PilotYoke,
                        CockpitTouchInteractionV2Hand.Left,
                        CockpitTouchInteractionV2Operation.BeginGrab,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);

            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        "pilot_yoke_grab",
                        CockpitTouchInteractionV2Target.PilotYoke,
                        CockpitTouchInteractionV2Hand.Left,
                        CockpitTouchInteractionV2Operation.UpdateGrab,
                        // Pull 20 mm aft: positive elevator / nose-up.
                        new Vector3(0f, 0f, -0.02f),
                        Quaternion.AngleAxis(
                            27.5f,
                            Vector3.forward))),
                Is.True);
            Assert.That(
                _input.Current.aileron,
                Is.EqualTo(-0.3f).Within(0.001f));
            Assert.That(
                _input.Current.elevator,
                Is.EqualTo(0.4f).Within(0.001f));

            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        "pilot_yoke_grab",
                        CockpitTouchInteractionV2Target.PilotYoke,
                        CockpitTouchInteractionV2Hand.Left,
                        CockpitTouchInteractionV2Operation.EndGrab,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);
            Assert.That(
                _input.Current.aileron,
                Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(
                _input.Current.elevator,
                Is.EqualTo(-0.1f).Within(0.001f));
            Assert.That(_bridge.ActiveCaptureCount, Is.Zero);
        }

        [Test]
        public void EngineControlsUseProvenTravelClampAndRetainManualPosition()
        {
            Vector3 axis = CockpitTouchInputV2Bridge
                .AuthoredEngineFullForwardAxisLocal.normalized;
            _input.Current.throttle = 0.25f;
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        "throttle_grab",
                        CockpitTouchInteractionV2Target.Throttle,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.BeginGrab,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        "throttle_grab",
                        CockpitTouchInteractionV2Target.Throttle,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.UpdateGrab,
                        axis * 0.09f,
                        Quaternion.identity)),
                Is.True);
            Assert.That(_input.Current.throttle, Is.EqualTo(1f));

            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        "throttle_grab",
                        CockpitTouchInteractionV2Target.Throttle,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.EndGrab,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);
            Assert.That(
                _input.Current.throttle,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                PrivateFloat(_input, "_throttle"),
                Is.EqualTo(1f).Within(0.001f));

            _input.Current.mixture = 0.8f;
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        "mixture_grab",
                        CockpitTouchInteractionV2Target.Mixture,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.BeginGrab,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        "mixture_grab",
                        CockpitTouchInteractionV2Target.Mixture,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.UpdateGrab,
                        -axis * 0.018f,
                        Quaternion.identity)),
                Is.True);
            Assert.That(
                _input.Current.mixture,
                Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        "mixture_grab",
                        CockpitTouchInteractionV2Target.Mixture,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.EndGrab,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);
            Assert.That(
                _input.Current.mixture,
                Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(
                PrivateFloat(_input, "_mixture"),
                Is.EqualTo(0.4f).Within(0.001f));
        }

        [Test]
        public void GuidedThrottleReleaseKeepsExistingHandoffAccumulator()
        {
            float accumulatorBeforeRelease =
                PrivateFloat(_input, "_throttle");
            _input.SetFlightLessonGuidance(
                true,
                new FlightLessonControlCommand
                {
                    throttle = 0.55f,
                    mixture = 1f
                });
            Assert.That(
                _input.TryBeginTouchOverride(
                    ProductTouchControlChannel.Throttle,
                    301,
                    0.75f),
                Is.True);
            Assert.That(
                _input.TryEndTouchOverride(
                    ProductTouchControlChannel.Throttle,
                    301),
                Is.True);

            Assert.That(
                _input.Current.throttle,
                Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(
                PrivateFloat(_input, "_throttle"),
                Is.EqualTo(accumulatorBeforeRelease).Within(0.001f),
                "Guided Demo/lesson release must not commit into the manual accumulator.");
        }

        [Test]
        public void EitherPhysicalHandWorksButUnknownBindingsFailClosed()
        {
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        "pilot_yoke_grab",
                        CockpitTouchInteractionV2Target.PilotYoke,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.BeginGrab,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        "pilot_yoke_grab",
                        CockpitTouchInteractionV2Target.PilotYoke,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.EndGrab,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        "flaps_grab",
                        CockpitTouchInteractionV2Target.Throttle,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.BeginGrab,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.False);
            Assert.That(_bridge.ActiveCaptureCount, Is.Zero);

            const int owner = 101;
            Assert.That(
                _input.TryBeginTouchOverride(
                    ProductTouchControlChannel.Throttle,
                    owner,
                    2f),
                Is.True);
            Assert.That(_input.Current.throttle, Is.EqualTo(1f));
            Assert.That(
                _input.TryUpdateTouchOverride(
                    ProductTouchControlChannel.Throttle,
                    202,
                    0f),
                Is.False);
            Assert.That(
                _input.TryEndTouchOverride(
                    ProductTouchControlChannel.Throttle,
                    202),
                Is.False);
            Assert.That(
                _input.TryEndTouchOverride(
                    ProductTouchControlChannel.Throttle,
                    owner),
                Is.True);
        }

        [Test]
        public void IndependentSwitchCommandsRouteThroughSystemsAuthority()
        {
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        CockpitTouchInputV2Bridge
                            .AltBatMasterBindingId,
                        CockpitTouchInteractionV2Target.AltBatMaster,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.Toggle,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        CockpitTouchInputV2Bridge
                            .PrimerBindingIdPrefix + "_65",
                        CockpitTouchInteractionV2Target.Primer,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.SetNormalized,
                        Vector3.zero,
                        Quaternion.identity,
                        0.65f)),
                Is.True);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        CockpitTouchInputV2Bridge
                            .IgnitionBindingIdPrefix + "_start",
                        CockpitTouchInteractionV2Target.IgnitionStart,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.SetNormalized,
                        Vector3.zero,
                        Quaternion.identity,
                        1f)),
                Is.False);
            FlightDynamicsState ignitionState = State();
            ignitionState.simulationTimeSeconds += 0.1;
            Assert.That(
                _systems.StepForTest(
                    ignitionState,
                    Controls(),
                    default,
                    0.1f),
                Is.True);
            Assert.That(
                _systems.TryGetCockpitSystemsSnapshotV2(
                    out CockpitSystemsSnapshotV2 ignitionStart),
                Is.True);
            Assert.That(
                ignitionStart.ignitionSwitchPosition,
                Is.EqualTo(CockpitIgnitionSwitchPositionV2.Both));
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        CockpitTouchInputV2Bridge
                            .IgnitionBindingIdPrefix + "_both",
                        CockpitTouchInteractionV2Target.IgnitionStart,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.SetNormalized,
                        Vector3.zero,
                        Quaternion.identity,
                        0.75f)),
                Is.False);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        CockpitTouchInputV2Bridge
                            .AvionicsPowerBindingId,
                        CockpitTouchInteractionV2Target.AvionicsPower,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.Toggle,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        CockpitTouchInputV2Bridge
                            .FlapSelectorBindingIdPrefix + "_75",
                        CockpitTouchInteractionV2Target.FlapSelector,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.SetNormalized,
                        Vector3.zero,
                        Quaternion.identity,
                        0.75f)),
                Is.True);
            Assert.That(_input.Current.flaps, Is.EqualTo(0.75f));
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        CockpitTouchInputV2Bridge
                            .FuelSelectorBindingIdPrefix + "_right",
                        CockpitTouchInteractionV2Target.FuelSelector,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.SetNormalized,
                        Vector3.zero,
                        Quaternion.identity,
                        1f)),
                Is.False);

            FlightDynamicsState state = State();
            state.simulationTimeSeconds += 1.0;
            FlightDynamicsControlSnapshot appliedControls = Controls();
            appliedControls.flaps = _input.Current.flaps;
            Assert.That(
                _systems.StepForTest(
                    state,
                    appliedControls,
                    default,
                    1f),
                Is.True);
            Assert.That(
                _systems.TryGetCockpitSystemsSnapshotV2(
                    out CockpitSystemsSnapshotV2 snapshot),
                Is.True);
            Assert.That(snapshot.batteryMasterOn, Is.False);
            Assert.That(snapshot.alternatorMasterOn, Is.False);
            Assert.That(
                snapshot.primerPositionNormalized,
                Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(
                snapshot.ignitionSwitchPosition,
                Is.EqualTo(CockpitIgnitionSwitchPositionV2.Both));
            Assert.That(snapshot.avionicsMasterOn, Is.False);
            Assert.That(
                snapshot.flapSelectorCommandNormalized,
                Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(
                snapshot.fuelSelectorPosition,
                Is.EqualTo(CockpitFuelSelectorPositionV2.Both));
        }

        [Test]
        public void UnavailableEngineSelectorsAreInactiveAndFlapDetentStillAdvances()
        {
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        CockpitTouchInputV2Bridge
                            .IgnitionBindingIdPrefix + "_cycle",
                        CockpitTouchInteractionV2Target.IgnitionStart,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.Toggle,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.False);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        CockpitTouchInputV2Bridge
                            .FuelSelectorBindingIdPrefix + "_cycle",
                        CockpitTouchInteractionV2Target.FuelSelector,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.Toggle,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.False);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        CockpitTouchInputV2Bridge
                            .FlapSelectorBindingIdPrefix + "_cycle",
                        CockpitTouchInteractionV2Target.FlapSelector,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.Toggle,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);
            Assert.That(
                _input.Current.flaps,
                Is.EqualTo(1f / 3f).Within(0.0001f));

            FlightDynamicsControlSnapshot appliedControls = Controls();
            appliedControls.flaps = _input.Current.flaps;
            FlightDynamicsState state = State();
            state.simulationTimeSeconds += 0.1;
            Assert.That(
                _systems.StepForTest(
                    state,
                    appliedControls,
                    default,
                    0.1f),
                Is.True);
            Assert.That(
                _systems.TryGetCockpitSystemsSnapshotV2(
                    out CockpitSystemsSnapshotV2 snapshot),
                Is.True);
            Assert.That(
                snapshot.ignitionSwitchPosition,
                Is.EqualTo(CockpitIgnitionSwitchPositionV2.Both));
            Assert.That(
                snapshot.fuelSelectorPosition,
                Is.EqualTo(CockpitFuelSelectorPositionV2.Both));
            Assert.That(
                snapshot.flapSelectorCommandNormalized,
                Is.EqualTo(1f / 3f).Within(0.0001f));
        }

        [Test]
        public void PrimerMomentaryPullReturnsToClosedOnRelease()
        {
            string bindingId =
                CockpitTouchInputV2Bridge.PrimerBindingIdPrefix +
                "_momentary";
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        bindingId,
                        CockpitTouchInteractionV2Target.Primer,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.Press,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);
            FlightDynamicsState pulled = State();
            pulled.simulationTimeSeconds += 0.1;
            Assert.That(
                _systems.StepForTest(
                    pulled,
                    Controls(),
                    default,
                    0.1f),
                Is.True);
            Assert.That(
                _systems.TryGetCockpitSystemsSnapshotV2(
                    out CockpitSystemsSnapshotV2 open),
                Is.True);
            Assert.That(open.primerPositionNormalized, Is.EqualTo(1f));

            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(
                    Command(
                        bindingId,
                        CockpitTouchInteractionV2Target.Primer,
                        CockpitTouchInteractionV2Hand.Right,
                        CockpitTouchInteractionV2Operation.Release,
                        Vector3.zero,
                        Quaternion.identity)),
                Is.True);
            FlightDynamicsState released = State();
            released.simulationTimeSeconds += 0.2;
            Assert.That(
                _systems.StepForTest(
                    released,
                    Controls(),
                    default,
                    0.1f),
                Is.True);
            Assert.That(
                _systems.TryGetCockpitSystemsSnapshotV2(
                    out CockpitSystemsSnapshotV2 closed),
                Is.True);
            Assert.That(closed.primerPositionNormalized, Is.EqualTo(0f));
        }

        [Test]
        public void StaleGrabLeaseRetainsLastValidThrottlePosition()
        {
            _input.Current.throttle = 0.35f;
            const int owner = 303;
            Assert.That(
                _input.TryBeginTouchOverride(
                    ProductTouchControlChannel.Throttle,
                    owner,
                    0.9f),
                Is.True);
            Assert.That(_input.Current.throttle, Is.EqualTo(0.9f));

            MethodInfo releaseStale = typeof(ProductControlInput)
                .GetMethod(
                    "ReleaseStaleTouchOverrides",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.That(releaseStale, Is.Not.Null);
            releaseStale.Invoke(
                _input,
                new object[]
                {
                    Time.unscaledTime +
                    ProductControlInput
                        .TouchOverrideLeaseTimeoutSeconds +
                    0.01f
                });
            Assert.That(_input.ActiveTouchOverrideCount, Is.Zero);
            Assert.That(
                _input.Current.throttle,
                Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(
                PrivateFloat(_input, "_throttle"),
                Is.EqualTo(0.9f).Within(0.001f));
        }

        [Test]
        public void StaleMetaSelectionReconcilesAndCanBeGrabbedAgain()
        {
            Vector3 axis = CockpitTouchInputV2Bridge
                .AuthoredEngineFullForwardAxisLocal.normalized;
            CockpitTouchInteractionV2Command begin = Command(
                "throttle_grab",
                CockpitTouchInteractionV2Target.Throttle,
                CockpitTouchInteractionV2Hand.Right,
                CockpitTouchInteractionV2Operation.BeginGrab,
                Vector3.zero,
                Quaternion.identity);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(in begin),
                Is.True);
            Assert.That(_bridge.ActiveCaptureCount, Is.EqualTo(1));

            MethodInfo releaseStale = typeof(ProductControlInput)
                .GetMethod(
                    "ReleaseStaleTouchOverrides",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.That(releaseStale, Is.Not.Null);
            releaseStale.Invoke(
                _input,
                new object[]
                {
                    Time.unscaledTime +
                    ProductControlInput
                        .TouchOverrideLeaseTimeoutSeconds +
                    0.01f
                });
            Assert.That(_input.ActiveTouchOverrideCount, Is.Zero);
            Assert.That(_bridge.ActiveCaptureCount, Is.EqualTo(1));

            CockpitTouchInteractionV2Command continued = Command(
                "throttle_grab",
                CockpitTouchInteractionV2Target.Throttle,
                CockpitTouchInteractionV2Hand.Right,
                CockpitTouchInteractionV2Operation.UpdateGrab,
                axis * 0.02f,
                Quaternion.identity);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(in continued),
                Is.True,
                "The still-selected Meta interactor must reacquire its expired lease.");
            Assert.That(_input.ActiveTouchOverrideCount, Is.EqualTo(1));

            CockpitTouchInteractionV2Command end = Command(
                "throttle_grab",
                CockpitTouchInteractionV2Target.Throttle,
                CockpitTouchInteractionV2Hand.Right,
                CockpitTouchInteractionV2Operation.EndGrab,
                Vector3.zero,
                Quaternion.identity);
            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(in end),
                Is.True);
            Assert.That(_bridge.ActiveCaptureCount, Is.Zero);

            Assert.That(
                _bridge.TryConsumeCockpitTouchCommand(in begin),
                Is.True,
                "A later grab must not be rejected by a phantom bridge capture.");
        }

        [Test]
        public void BridgeHasNoUpdateLoopDiscoveryOrVisualWrites()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Scripts/Aircraft/CockpitDynamics/TouchInputV2/" +
                "CockpitTouchInputV2Bridge.cs");
            string source = File.ReadAllText(path);
            StringAssert.DoesNotContain(
                "private void Update()",
                source);
            StringAssert.DoesNotContain(
                "private void LateUpdate()",
                source);
            string[] forbidden =
            {
                "FindObjectOfType",
                "FindFirstObjectByType",
                "FindAnyObjectByType",
                "GameObject.Find",
                "AddComponent<",
                "new GameObject",
                ".localPosition =",
                ".localRotation =",
                ".SetPositionAndRotation("
            };
            foreach (string value in forbidden)
                StringAssert.DoesNotContain(value, source, value);
        }

        private static CockpitTouchInteractionV2Command Command(
            string id,
            CockpitTouchInteractionV2Target target,
            CockpitTouchInteractionV2Hand hand,
            CockpitTouchInteractionV2Operation operation,
            Vector3 pointerWorldPosition,
            Quaternion pointerWorldRotation)
        {
            return Command(
                id,
                target,
                hand,
                operation,
                pointerWorldPosition,
                pointerWorldRotation,
                0f);
        }

        private static CockpitTouchInteractionV2Command Command(
            string id,
            CockpitTouchInteractionV2Target target,
            CockpitTouchInteractionV2Hand hand,
            CockpitTouchInteractionV2Operation operation,
            Vector3 pointerWorldPosition,
            Quaternion pointerWorldRotation,
            float normalizedValue)
        {
            return new CockpitTouchInteractionV2Command
            {
                bindingId = id,
                target = target,
                hand = hand,
                operation = operation,
                normalizedValue = normalizedValue,
                pointerWorldPosition = pointerWorldPosition,
                pointerWorldRotation = pointerWorldRotation,
                hitWorldPosition = pointerWorldPosition
            };
        }

        private static CockpitSystemsScenarioV2 Scenario()
        {
            return new CockpitSystemsScenarioV2
            {
                hasAuthoritativeConfiguration = true,
                scenarioUtcUnixSeconds = 1_780_000_000L,
                batteryMasterOn = true,
                alternatorMasterOn = true,
                avionicsMasterOn = true,
                ignitionSwitchPosition =
                    CockpitIgnitionSwitchPositionV2.Both,
                primerPositionNormalized = 0f,
                fuelSelectorPosition =
                    CockpitFuelSelectorPositionV2.Both
            };
        }

        private static FlightDynamicsState State()
        {
            return new FlightDynamicsState
            {
                simulationTimeSeconds = 12.5,
                latitudeDegrees = 40.03936527,
                longitudeDegrees = -105.22608958,
                altitudeMslMeters = 1600.0,
                altitudeAglMeters = 0.0,
                calibratedAirspeedKnots = 0.0,
                groundSpeedKnots = 0.0,
                headingDegrees = 80.0,
                rotationUnity = Quaternion.identity,
                engineRpm = 900.0,
                weightOnWheels = true
            };
        }

        private static FlightDynamicsControlSnapshot Controls()
        {
            return new FlightDynamicsControlSnapshot
            {
                throttle = 0.25f,
                mixture = 1f,
                flaps = 0f
            };
        }

        private static float PrivateFloat(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (float)field.GetValue(target);
        }

        private static Transform Child(
            string name,
            Transform parent)
        {
            GameObject value = new GameObject(name);
            value.transform.SetParent(parent, false);
            return value.transform;
        }
    }
}
#endif
