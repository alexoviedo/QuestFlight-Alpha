#if UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestFlight.PlayModeTests
{
    public sealed class ProductFlightModesFoundationPlayModeTests
    {
        private const string WorldResourcePath =
            "QuestFlightLab/Production/ProductionWorldRoot";
        private const float FixtureTimeScale = 0.37f;
        private GameObject _world;
        private float _originalTimeScale;
        private float _originalFixedDeltaTime;
        private ProductFlightModeCoordinator _mode;
        private ProductDemoPilot _demo;
        private ProductControlInput _input;
        private FlightDynamicsCoordinator _flight;
        private ProductionSeatCalibrationController _seat;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            _originalFixedDeltaTime = Time.fixedDeltaTime;
            Time.timeScale = FixtureTimeScale;
            GameObject prefab = Resources.Load<GameObject>(WorldResourcePath);
            Assert.That(prefab, Is.Not.Null);
            _world = Object.Instantiate(prefab);
            _mode = _world.GetComponentInChildren<ProductFlightModeCoordinator>(true);
            _demo = _world.GetComponentInChildren<ProductDemoPilot>(true);
            _input = _world.GetComponentInChildren<ProductControlInput>(true);
            _flight = _world.GetComponentInChildren<FlightDynamicsCoordinator>(true);
            _seat = _world.GetComponentInChildren<ProductionSeatCalibrationController>(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null)
                Object.DestroyImmediate(_world);
            Assert.That(Time.timeScale, Is.EqualTo(FixtureTimeScale).Within(0.000001f),
                "The coordinator must restore only the time scale it owns.");
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(_originalFixedDeltaTime),
                "Coordinator teardown must not alter unrelated global timing state.");
            Time.timeScale = _originalTimeScale;
        }

        [Test]
        public void StartupSelectionPausesAndRejectsSelectionsBeforeNativeInitialization()
        {
            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Selection));
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(_mode.OwnsTimeScalePause, Is.True);
            Assert.That(_input.FlightLessonInputSuppressed, Is.True);
            Assert.That(_mode.AuthoredModePanelRoot.activeSelf, Is.True);
            Assert.That(_mode.AuthoredModePanelText.text,
                Is.EqualTo(ProductFlightModeCoordinator.PreparingText));

            _mode.SelectDemo();
            _mode.SelectFreeFlight();
            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Selection));
            Assert.That(_demo.IsRunning, Is.False);
            Assert.That(Time.timeScale, Is.Zero);
        }

        [Test]
        public void LessonsChoiceStaysPausedInSelectionWithoutActivatingLessonControlState()
        {
            MakeNativeReady();
            _mode.ShowLessonsUnavailable();
            _mode.ShowLessonsUnavailable();
            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Selection));
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(_demo.IsRunning, Is.False);
            Assert.That(_input.DeterministicMode, Is.False);
            Assert.That(_input.FlightLessonGuidanceActive, Is.False);
            Assert.That(_mode.AuthoredModePanelText.text,
                Is.EqualTo(ProductFlightModeCoordinator.LessonsUnavailableText));
        }

        [Test]
        public void DemoSelectionUsesExistingLifecycleAndIsIdempotent()
        {
            MakeNativeReady();
            object route = _demo.AuthoredRouteProfile;
            _mode.SelectDemo();
            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Demo));
            Assert.That(_demo.IsRunning, Is.True);
            Assert.That(_demo.AuthoredRouteProfile, Is.SameAs(route));
            Assert.That(_input.DeterministicMode, Is.True);
            Assert.That(_input.FlightLessonGuidanceActive, Is.True);
            Assert.That(_input.FlightLessonInputSuppressed, Is.False);
            Assert.That(_mode.AuthoredModePanelRoot.activeSelf, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(FixtureTimeScale).Within(0.000001f));

            _mode.SelectDemo();
            _mode.SelectFreeFlight();
            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Demo));
            Assert.That(_demo.IsRunning, Is.True);
        }

        [Test]
        public void FreeFlightEstablishesEveryManualControlPostconditionAndIsIdempotent()
        {
            MakeNativeReady();
            _input.SetFlightLessonInputSuppressed(false);
            Assert.That(_input.TryBeginTouchOverride(
                ProductTouchControlChannel.PilotYoke, 51001, 0.2f, -0.1f), Is.True);
            Assert.That(_input.TryBeginTouchOverride(
                ProductTouchControlChannel.Throttle, 51002, 0.8f), Is.True);
            Assert.That(_input.TryBeginTouchOverride(
                ProductTouchControlChannel.Mixture, 51003, 0.9f), Is.True);
            _input.SetDeterministicMode(true);
            _input.SetFlightLessonGuidance(true, default);

            _mode.SelectFreeFlight();
            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.FreeFlight));
            Assert.That(_demo.IsRunning, Is.False);
            Assert.That(_input.DeterministicMode, Is.False);
            Assert.That(_input.FlightLessonGuidanceActive, Is.False);
            Assert.That(_input.FlightLessonInputSuppressed, Is.False);
            Assert.That(_input.ActiveTouchOverrideCount, Is.Zero);
            Assert.That(_flight.IsInitialized, Is.True);
            Assert.That(_flight.LastResetInitialConditions.engineRunning, Is.True);
            Assert.That(_flight.initialConditionProvider.engineRunning, Is.True);
            Assert.That(_mode.AuthoredModePanelRoot.activeSelf, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(FixtureTimeScale).Within(0.000001f));

            _mode.SelectFreeFlight();
            _mode.SelectDemo();
            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.FreeFlight));
            Assert.That(_demo.IsRunning, Is.False);
        }

        [Test]
        public void FreeFlightRetainsPositionHoldingControlsAfterTouchRelease()
        {
            MakeNativeReady();
            _mode.SelectFreeFlight();
            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.FreeFlight));

            Assert.That(_input.TryBeginTouchOverride(
                ProductTouchControlChannel.Throttle, 52001, 0.64f), Is.True);
            Assert.That(_input.TryEndTouchOverride(
                ProductTouchControlChannel.Throttle, 52001), Is.True);
            Assert.That(_input.TryBeginTouchOverride(
                ProductTouchControlChannel.Mixture, 52002, 0.72f), Is.True);
            Assert.That(_input.TryEndTouchOverride(
                ProductTouchControlChannel.Mixture, 52002), Is.True);
            Assert.That(_input.TryApplyTouchDiscreteValue(
                ProductTouchControlChannel.FlapSelector, 52003, 0.25f), Is.True);

            InvokePrivate(_input, "Update");
            Assert.That(_input.Current.throttle,
                Is.EqualTo(0.64f).Within(0.001f));
            Assert.That(_input.Current.mixture,
                Is.EqualTo(0.72f).Within(0.001f));
            Assert.That(_input.Current.flaps,
                Is.EqualTo(0.25f).Within(0.001f));

            _input.Current.aileron = 0.2f;
            _input.Current.elevator = -0.1f;
            Assert.That(_input.TryBeginTouchOverride(
                ProductTouchControlChannel.PilotYoke,
                52004,
                -0.4f,
                0.3f), Is.True);
            Assert.That(_input.TryEndTouchOverride(
                ProductTouchControlChannel.PilotYoke, 52004), Is.True);
            Assert.That(_input.Current.aileron,
                Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(_input.Current.elevator,
                Is.EqualTo(-0.1f).Within(0.001f));
        }

        [Test]
        public void CalibrationOpenHidesPanelAndSuppressesAllPublicSelectionInputs()
        {
            MakeNativeReady();
            SetAutoProperty(_seat, "IsOpen", true);
            InvokePrivate(_mode, "Update");
            Assert.That(_mode.AuthoredModePanelRoot.activeSelf, Is.False);

            _mode.SelectDemo();
            _mode.SelectFreeFlight();
            _mode.ShowLessonsUnavailable();
            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Selection));
            Assert.That(_demo.IsRunning, Is.False);
            Assert.That(Time.timeScale, Is.Zero);

            SetAutoProperty(_seat, "IsOpen", false);
            InvokePrivate(_mode, "Update");
            Assert.That(_mode.AuthoredModePanelRoot.activeSelf, Is.True);
            Assert.That(_mode.AuthoredModePanelText.text,
                Is.EqualTo(ProductFlightModeCoordinator.SelectionText));
        }

        [Test]
        public void BackendFailureEntersBlockedAndKeepsSimulationPaused()
        {
            SetAutoProperty(_flight, "LastError", "native test failure");
            WithoutUnityLogging(() => InvokePrivate(_mode, "Update"));
            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Blocked));
            Assert.That(_mode.LastError,
                Is.EqualTo("Native JSBSim did not initialize: native test failure"));
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(_demo.IsRunning, Is.False);
            Assert.That(_input.FlightLessonInputSuppressed, Is.True);
        }

        [Test]
        public void MissingReferencesEnterBlockedAndRestoreOwnedTimeOnTeardown()
        {
            Object.DestroyImmediate(_world);
            _world = null;
            Assert.That(Time.timeScale, Is.EqualTo(FixtureTimeScale).Within(0.000001f));

            GameObject incomplete = new GameObject("IncompleteFlightModes");
            incomplete.SetActive(false);
            ProductFlightModeCoordinator mode =
                incomplete.AddComponent<ProductFlightModeCoordinator>();
            WithoutUnityLogging(() => incomplete.SetActive(true));
            Assert.That(mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Blocked));
            Assert.That(mode.LastError, Is.EqualTo(
                "Flight mode startup references are incomplete. Re-author the two production prefabs."));
            Assert.That(Time.timeScale, Is.Zero);
            Object.DestroyImmediate(incomplete);
            Assert.That(Time.timeScale, Is.EqualTo(FixtureTimeScale).Within(0.000001f));
        }

        private void MakeNativeReady()
        {
            typeof(FlightDynamicsCoordinator)
                .GetField("_backend", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(_flight, new InitializedBackend());
            FlightDynamicsInitialConditions initial = default;
            initial.engineRunning = true;
            SetAutoProperty(_flight, "LastResetInitialConditions", initial);
            Assert.That(_flight.IsInitialized, Is.True);
        }

        private static void SetAutoProperty(object target, string property, object value)
        {
            FieldInfo field = target.GetType().GetField(
                "<" + property + ">k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, property);
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(
                method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, method);
            info.Invoke(target, null);
        }

        private static void WithoutUnityLogging(Action action)
        {
            bool previous = Debug.unityLogger.logEnabled;
            try
            {
                Debug.unityLogger.logEnabled = false;
                action();
            }
            finally
            {
                Debug.unityLogger.logEnabled = previous;
            }
        }

        private sealed class InitializedBackend : IFlightDynamicsBackend
        {
            public FlightDynamicsBackendKind Kind =>
                FlightDynamicsBackendKind.JSBSimNativeDevelopment;
            public string DisplayName => "Initialized test native authority";
            public bool IsAvailable => true;
            public bool IsInitialized => true;
            public string LastError => string.Empty;
            public FlightDynamicsState CurrentState => default;
            public bool Initialize(FlightDynamicsBackendContext context) => true;
            public bool Reset(FlightDynamicsInitialConditions initialConditions) => true;
            public void SetControls(AircraftControlState controls) { }
            public void SetAtmosphere(FlightDynamicsAtmosphere atmosphere) { }
            public bool Advance(double fixedDeltaTimeSeconds) => true;
            public void Dispose() { }
        }
    }
}
#endif
