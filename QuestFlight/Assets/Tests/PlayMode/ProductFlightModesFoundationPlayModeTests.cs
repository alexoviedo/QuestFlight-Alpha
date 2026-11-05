#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AlexOviedo.QuestFlight.JSBSim;
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
            InvokePrivate(_mode.AuthoredModeMenuView, "Start");
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
        public void StartupShowroomHidesAndExactlyRestoresAircraftPresentationRenderers()
        {
            ProductFlightModeMenuView view = _mode.AuthoredModeMenuView;
            Assert.That(view.FadeOverlayRenderer.enabled, Is.False,
                "A zero-opacity viewer-enclosing fade must have no renderable geometry.");
            Renderer[] renderers = (Renderer[])typeof(ProductFlightModeMenuView)
                .GetField("aircraftPresentationRenderers",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(view);
            Renderer[] interactionRenderers = (Renderer[])typeof(ProductFlightModeMenuView)
                .GetField("interactionPresentationRenderers",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(view);
            Renderer[] worldRenderers = (Renderer[])typeof(ProductFlightModeMenuView)
                .GetField("worldPresentationRenderers",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(view);
            bool[] priorStates = (bool[])typeof(ProductFlightModeMenuView)
                .GetField("_aircraftForceRenderingOffStates",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(view);
            bool[] priorWorldStates = (bool[])typeof(ProductFlightModeMenuView)
                .GetField("_worldForceRenderingOffStates",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(view);
            Assert.That(renderers, Is.Not.Null.And.Not.Empty);
            Assert.That(priorStates, Is.Not.Null);
            Assert.That(renderers, Has.Length.EqualTo(priorStates.Length));
            Assert.That(interactionRenderers, Has.Length.EqualTo(2));
            Assert.That(worldRenderers, Is.Not.Null.And.Not.Empty);
            Assert.That(priorWorldStates, Has.Length.EqualTo(worldRenderers.Length));
            Assert.That(interactionRenderers.Intersect(renderers), Is.Empty);
            Assert.That(interactionRenderers.All(renderer =>
                    renderer.name == "Cube" &&
                    HasAncestorNamed(renderer.transform, "ControllerRayInteractor")),
                Is.True);
            Renderer[] controllerCursorRenderers = renderers
                .Where(renderer => renderer.name == "OculusCursor" &&
                    HasAncestorNamed(renderer.transform, "ControllerRayInteractor"))
                .ToArray();
            Assert.That(controllerCursorRenderers, Has.Length.EqualTo(2));
            Renderer[] handRayRenderers = renderers
                .Where(renderer => HasAncestorNamed(
                    renderer.transform, "Hand and No Controller") &&
                    (renderer.name == "Cube" || renderer.name == "OculusCursor"))
                .ToArray();
            Assert.That(handRayRenderers, Has.Length.EqualTo(4));
            for (int index = 0; index < renderers.Length; index++)
                Assert.That(renderers[index].forceRenderingOff, Is.True,
                    renderers[index].name);
            for (int index = 0; index < interactionRenderers.Length; index++)
                Assert.That(interactionRenderers[index].forceRenderingOff, Is.False,
                    interactionRenderers[index].name);
            for (int index = 0; index < worldRenderers.Length; index++)
                Assert.That(worldRenderers[index].forceRenderingOff, Is.True,
                    worldRenderers[index].name);

            _mode.AuthoredModePanelRoot.SetActive(false);

            for (int index = 0; index < renderers.Length; index++)
                Assert.That(renderers[index].forceRenderingOff,
                    Is.EqualTo(priorStates[index]),
                    renderers[index].name);
            for (int index = 0; index < interactionRenderers.Length; index++)
                Assert.That(interactionRenderers[index].forceRenderingOff, Is.False,
                    interactionRenderers[index].name);
            for (int index = 0; index < worldRenderers.Length; index++)
                Assert.That(worldRenderers[index].forceRenderingOff,
                    Is.EqualTo(priorWorldStates[index]),
                    worldRenderers[index].name);
        }

        [Test]
        public void StartupComfortFadeRendererExistsOnlyDuringVisibleTransition()
        {
            ProductFlightModeMenuView view = _mode.AuthoredModeMenuView;
            Renderer fade = view.FadeOverlayRenderer;
            MethodInfo setFadeOpacity = typeof(ProductFlightModeMenuView)
                .GetMethod("SetFadeOpacity",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(setFadeOpacity, Is.Not.Null);
            Assert.That(fade.enabled, Is.False);

            setFadeOpacity.Invoke(view, new object[] { 0.5f });
            Assert.That(fade.enabled, Is.True);
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            fade.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat(Shader.PropertyToID("_Opacity")),
                Is.EqualTo(0.5f).Within(0.000001f));

            setFadeOpacity.Invoke(view, new object[] { 0f });
            Assert.That(fade.enabled, Is.False);
        }

        private static bool HasAncestorNamed(Transform transform, string name)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (current.name == name) return true;
            }
            return false;
        }

        [Test]
        public void StartupMenuFocusIsPersistentAndNavigationIsDeterministic()
        {
            ProductFlightModeMenuView view = _mode.AuthoredModeMenuView;
            ProductFlightModeMenuButton[] buttons = (ProductFlightModeMenuButton[])
                typeof(ProductFlightModeMenuView)
                    .GetField("menuButtons",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(view);
            Assert.That(buttons, Has.Length.EqualTo(4));
            Assert.That(view.FocusedIndex, Is.Zero);
            Assert.That(buttons[0].IsFocused, Is.True);

            view.ProcessNavigationSample(-0.2f, false, false, 0f, false);
            Assert.That(view.FocusedIndex, Is.Zero, "Dead-zone noise moved focus.");
            view.ProcessNavigationSample(-1f, false, false, 0.1f, false);
            Assert.That(view.FocusedIndex, Is.EqualTo(1));
            view.ProcessNavigationSample(-1f, false, false, 0.2f, false);
            Assert.That(view.FocusedIndex, Is.EqualTo(1),
                "A held stick repeated before the authored delay.");
            view.ProcessNavigationSample(-1f, false, false, 0.6f, false);
            Assert.That(view.FocusedIndex, Is.EqualTo(2));
            view.ProcessNavigationSample(0f, false, false, 0.61f, false);
            view.ProcessNavigationSample(1f, false, false, 0.7f, false);
            Assert.That(view.FocusedIndex, Is.EqualTo(1));

            view.FocusButton(buttons[2]);
            Assert.That(view.FocusedIndex, Is.EqualTo(2));
            Assert.That(buttons[2].IsFocused, Is.True);
            Assert.That(buttons[1].IsFocused, Is.False);

            view.ProcessNavigationSample(-1f, true, false, 0.8f, true);
            view.ProcessNavigationSample(-1f, true, false, 1.4f, false);
            Assert.That(view.FocusedIndex, Is.EqualTo(2),
                "Suppressed input was not drained until release.");
            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Selection));
            view.ProcessNavigationSample(0f, false, false, 1.5f, false);
            view.ProcessNavigationSample(-1f, false, false, 1.6f, false);
            Assert.That(view.FocusedIndex, Is.EqualTo(3));
        }

        [Test]
        public void FocusedActivationUsesTheExistingFreeFlightLifecycleExactlyOnce()
        {
            MakeNativeReady();
            // This synchronous fixture has no headset focus. Test the button's
            // mode dispatch without the separate shared-navigation focus gate.
            _mode.ConfigureNavigationForAuthoring(null);
            ProductFlightModeMenuView view = _mode.AuthoredModeMenuView;
            view.MoveFocus(1);
            Assert.That(view.FocusedButton.Action,
                Is.EqualTo(ProductFlightModeMenuButton.MenuAction.FreeFlight));

            view.ActivateFocused();
            view.ActivateFocused();

            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.FreeFlight));
            Assert.That(_demo.IsRunning, Is.False);
            Assert.That(_input.DeterministicMode, Is.False);
            Assert.That(_input.FlightLessonGuidanceActive, Is.False);
            Assert.That(_input.FlightLessonInputSuppressed, Is.False);
        }

        [Test]
        public void LessonsChoiceDefersItsPauseUntilTheCockpitTransitionFinishes()
        {
            MakeNativeReady();
            _mode.ShowLessonsUnavailable();
            _mode.ShowLessonsUnavailable();
            Assert.That(_mode.State,
                Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Training));
            Assert.That(Time.timeScale, Is.EqualTo(FixtureTimeScale));
            Assert.That(_demo.IsRunning, Is.False);
            Assert.That(_input.DeterministicMode, Is.False);
            Assert.That(_input.FlightLessonGuidanceActive, Is.False);
            Assert.That(_mode.AuthoredModeMenuView.TransitionPending, Is.True);
            Assert.That(_mode.AuthoredTraining.State,
                Is.EqualTo(QuestFlight.Product.Training.TrainingSessionState.Inactive));
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
            Assert.That(_mode.AuthoredModePanelRoot.activeSelf, Is.True,
                "The showroom must remain until cockpit presentation is authoritative.");
            Assert.That(_mode.AuthoredModeMenuView.TransitionPending, Is.True);
            Assert.That(_mode.AuthoredModeMenuView.SelectionContentRoot.activeSelf,
                Is.False);
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
            Assert.That(_mode.AuthoredModePanelRoot.activeSelf, Is.True,
                "The showroom must remain until cockpit presentation is authoritative.");
            Assert.That(_mode.AuthoredModeMenuView.TransitionPending, Is.True);
            Assert.That(_mode.AuthoredModeMenuView.SelectionContentRoot.activeSelf,
                Is.False);
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

        [TestCase("runtime_manifest.txt")]
        [TestCase("aircraft/c172x/c172x.xml")]
        [Description("Synchronous Editor cached-read/actual Start integration. Does not exercise Android extraction, storage-full or interrupted activation.")]
        public void CachedDataReadFailureCompletesActualStartupBlocksSelectionAndRetries(string relativePath)
        {
            Assert.That(JSBSimRuntimeData.TryGetPreparedDataRoot(out string dataRoot, out string dataError),
                Is.True, dataError);
            Assert.That(dataRoot, Is.EqualTo(JSBSimRuntimeData.EditorPackageDataRoot));
            Assert.That(_flight.IsInitialized, Is.False);
            Assert.That(_flight.enabled, Is.True);
            Assert.That(_mode.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Selection));
            Camera trackedCamera = _world.GetComponentsInChildren<Camera>(true).Single(camera =>
                camera.CompareTag("MainCamera") && camera.enabled && camera.gameObject.activeInHierarchy);
            Assert.That(TrackedXrCameraPoseDriver.HasRequiredBindings(trackedCamera), Is.True);
            Assert.That(_world.GetComponentsInChildren<OVRCameraRig>(true)
                .Count(origin => origin.isActiveAndEnabled), Is.EqualTo(1));
            int rootId = _flight.simulationRoot.GetInstanceID();
            Vector3 cameraPosition = trackedCamera.transform.localPosition;
            Quaternion cameraRotation = trackedCamera.transform.localRotation;
            string target = Path.Combine(dataRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            byte[] before = File.ReadAllBytes(target);
            byte[] metaBefore = File.Exists(target + ".meta") ? File.ReadAllBytes(target + ".meta") : null;
            const string storageError = "JSBSim runtime data storage failed (IOException). Check available storage and app data access, then restart to retry.";
            const string authorityError = "Native JSBSim data preparation failed: " + storageError;
            const string selectionError = "Native JSBSim did not initialize: " + authorityError;
            using (var expectedErrors = new ExpectedStartupErrorLogs(
                       "[QuestFlight][FlightAuthority] " + authorityError,
                       "[QuestFlight][FlightModes] " + selectionError))
            using (var unavailable = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<IOException>(() => File.ReadAllBytes(target),
                    "The real read-only exclusive lock must induce a storage read exception.");
                MethodInfo start = typeof(FlightDynamicsCoordinator).GetMethod("Start",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(start, Is.Not.Null);
                int nested = DrainStartupForCachedRead((IEnumerator)start.Invoke(_flight, null));
                Assert.That(nested, Is.EqualTo(1),
                    "The actual first cached-read failure must enter and complete nested Prepare.");
                Assert.That(_flight.IsInitialized, Is.False);
                Assert.That(_flight.enabled, Is.False);
                Assert.That(_flight.LastError, Is.EqualTo(authorityError));
                Assert.That(_flight.NativePayloadIdentity, Is.Empty);
                Assert.That(_flight.TryGetAuthorityReadiness(out string readinessError), Is.False);
                Assert.That(readinessError, Is.EqualTo(authorityError));
                InvokePrivate(_mode, "Update");
                Assert.That(_mode.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Blocked));
                Assert.That(_mode.LastError, Is.EqualTo(selectionError));
                Assert.That(_mode.AuthoredModePanelText.text,
                    Is.Not.EqualTo(ProductFlightModeCoordinator.PreparingText));
                Assert.That(_mode.AuthoredModePanelText.text, Does.Contain("try again"));
                Assert.That(_mode.AuthoredModePanelText.text, Does.Contain("saved progress is kept"));
                _mode.SelectDemo();
                _mode.SelectFreeFlight();
                for (int update = 0; update < 3; update++) InvokePrivate(_mode, "Update");
                Assert.That(_mode.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Blocked));
                Assert.That(_demo.IsRunning, Is.False);
                Assert.That(_input.FlightLessonInputSuppressed, Is.True);
                Assert.That(Time.timeScale, Is.Zero);
                expectedErrors.AssertExactlyOnce();
            }
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(before), "Exclusive read lock must not change payload bytes.");
            if (metaBefore != null) Assert.That(File.ReadAllBytes(target + ".meta"), Is.EqualTo(metaBefore));
            _flight.enabled = true;
            Assert.That(_flight.InitializeSelectedBackend(), Is.True, _flight.LastError);
            Assert.That(_flight.LastError, Is.Empty);
            Assert.That(_flight.TryGetAuthorityReadiness(out string ready), Is.True, ready);
            Assert.That(_flight.CurrentState.IsFinite, Is.True);
            Assert.That(_flight.NativePayloadIdentity, Is.Not.Empty);
            _mode.ReturnToMainMenu();
            Assert.That(_mode.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Selection));
            Assert.That(_mode.LastError, Is.Empty);
            Assert.That(_mode.AuthoredModePanelText.text, Is.EqualTo(ProductFlightModeCoordinator.SelectionText));
            Assert.That(_mode.AuthoredFlightCoordinator, Is.SameAs(_flight));
            Assert.That(_flight.simulationRoot.GetInstanceID(), Is.EqualTo(rootId));
            Assert.That(_world.GetComponentsInChildren<FlightDynamicsCoordinator>(true), Has.Length.EqualTo(1));
            Assert.That(_world.GetComponentsInChildren<Camera>(true)
                .Count(camera => camera.enabled && camera.gameObject.activeInHierarchy), Is.EqualTo(1));
            Assert.That(_world.GetComponentsInChildren<Camera>(true).Single(camera =>
                camera.CompareTag("MainCamera") && camera.enabled && camera.gameObject.activeInHierarchy), Is.SameAs(trackedCamera));
            Assert.That(trackedCamera.transform.localPosition, Is.EqualTo(cameraPosition));
            Assert.That(trackedCamera.transform.localRotation, Is.EqualTo(cameraRotation));
            _mode.SelectFreeFlight();
            Assert.That(_mode.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.FreeFlight));
            Assert.That(_input.FlightLessonInputSuppressed, Is.False);
            Assert.That(_flight.TryGetAuthorityReadiness(out ready), Is.True, ready);
            // Free-flight released the mode's pause; leave the unrelated fixture clock unchanged.
            Time.timeScale = FixtureTimeScale;
        }

        private static int DrainStartupForCachedRead(IEnumerator startup)
        {
            var pending = new Stack<IEnumerator>();
            pending.Push(startup);
            int advances = 0;
            int nested = 0;
            try
            {
                while (pending.Count > 0)
                {
                    Assert.That(++advances, Is.LessThanOrEqualTo(16),
                        "Cached Editor startup must terminate; a lost callback cannot remain Preparing.");
                    IEnumerator current = pending.Peek();
                    if (!current.MoveNext())
                    {
                        (pending.Pop() as IDisposable)?.Dispose();
                        continue;
                    }
                    Assert.That(current.Current, Is.InstanceOf<IEnumerator>(),
                        "This synchronous Editor cached-read fixture may only yield nested preparation, not asynchronous player work.");
                    pending.Push((IEnumerator)current.Current);
                    nested++;
                }
            }
            finally
            {
                while (pending.Count > 0) (pending.Pop() as IDisposable)?.Dispose();
            }
            return nested;
        }

        // The runtime test assembly has no Unity TestRunner dependency. Consume only
        // these two exact expected errors, assert their counts, and forward all other logs.
        private sealed class ExpectedStartupErrorLogs : ILogHandler, IDisposable
        {
            private readonly ILogHandler _previous;
            private readonly Dictionary<string, int> _counts;

            public ExpectedStartupErrorLogs(params string[] expected)
            {
                _previous = Debug.unityLogger.logHandler;
                _counts = expected.ToDictionary(message => message, _ => 0);
                Debug.unityLogger.logHandler = this;
            }

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                string message = string.Format(format, args);
                if (logType == LogType.Error && _counts.ContainsKey(message))
                {
                    _counts[message]++;
                    return;
                }
                _previous.LogFormat(logType, context, format, args);
            }

            public void LogException(Exception exception, UnityEngine.Object context) =>
                _previous.LogException(exception, context);

            public void AssertExactlyOnce()
            {
                foreach (var expected in _counts)
                    Assert.That(expected.Value, Is.EqualTo(1), expected.Key);
            }

            public void Dispose() => Debug.unityLogger.logHandler = _previous;
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

        [Test]
        public void ColdFreeFlightTransitionCanBecomeReadyWhileNavigationPausesFlight()
        {
            MakeNativeReady();
            _mode.SelectFreeFlight();
            _mode.AuthoredNavigation.OpenMenu();
            Assert.That(Time.timeScale, Is.Zero);
            var view = _mode.AuthoredModeMenuView;
            for (int frame = 0; frame < 4; frame++)
            {
                Assert.That(view.AuthoredCockpitSystems.StepForTest(
                    default, default, default, 0f), Is.True,
                    "A paused native snapshot must remain valid on consecutive frames.");
                InvokePrivate(view, "Update");
            }
            Assert.That(typeof(ProductFlightModeMenuView)
                .GetField("_transitionPhase", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(view).ToString(), Is.Not.EqualTo("WaitingForCockpit"));
            // Navigation owns the pause now; restore the fixture before its teardown.
            Time.timeScale = FixtureTimeScale;
        }

        [Test]
        public void TransitionReactivatesItsPresentationAfterCalibrationHidThePanel()
        {
            MakeNativeReady();
            SetAutoProperty(_seat, "IsOpen", true);
            InvokePrivate(_mode, "Update");
            SetAutoProperty(_seat, "IsOpen", false);
            _mode.SelectFreeFlight();
            Assert.That(_mode.AuthoredModePanelRoot.activeInHierarchy, Is.True,
                "The transition must not depend on another selection Update to reactivate it.");
            Assert.That(_mode.AuthoredModeMenuView.TransitionContentRoot.activeInHierarchy,
                Is.True);
        }

        [Test]
        public void UnavailableCockpitReturnsToAnActionablePausedMenu()
        {
            MakeNativeReady();
            _mode.SelectFreeFlight();
            var view = _mode.AuthoredModeMenuView;
            typeof(ProductFlightModeMenuView).GetField("_transitionWaitSeconds",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(view, ProductFlightModeMenuView.MaximumPreparationSeconds);
            WithoutUnityLogging(() => InvokePrivate(view, "Update"));
            Assert.That(view.TransitionPending, Is.False);
            Assert.That(view.FadeOverlayRenderer.enabled, Is.False);
            Assert.That(_mode.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Blocked));
            Assert.That(_mode.AuthoredNavigation.IsOpen, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(_input.FlightLessonInputSuppressed, Is.True);
        }

        [Test]
        public void ReturningToMainCancelsAQueuedActivityAndRestoresManualRestart()
        {
            MakeNativeReady();
            var navigation = _mode.AuthoredNavigation;
            typeof(ProductAppNavigation).GetField("pendingActivity",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(navigation, 3);
            InvokePrivate(navigation, "ReturnMain");
            Assert.That(typeof(ProductAppNavigation).GetField("pendingActivity",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(navigation), Is.EqualTo(-1));
            Assert.That(_mode.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Selection));
            Assert.That(_mode.AuthoredTraining.State, Is.EqualTo(QuestFlight.Product.Training.TrainingSessionState.Inactive));
#if UNITY_EDITOR
            navigation.EditorVerificationFocus=true;
#endif
            // Returning Home intentionally leaves an overlay open. Close it before
            // exercising the low-level coordinator API; real UI traversal has separate tests.
            navigation.Resume();
            _mode.SelectFreeFlight();
            Assert.That(_mode.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.FreeFlight));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Time.timeScale = FixtureTimeScale;
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
            public bool Relocate(FlightDynamicsInitialConditions initialConditions) => true;
            public void SetControls(AircraftControlState controls) { }
            public void SetAtmosphere(FlightDynamicsAtmosphere atmosphere) { }
            public bool Advance(double fixedDeltaTimeSeconds) => true;
            public void Dispose() { }
        }
    }
}
#endif
