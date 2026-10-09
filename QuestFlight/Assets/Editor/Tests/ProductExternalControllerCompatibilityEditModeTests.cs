#if UNITY_INCLUDE_TESTS
using System.IO;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Android.LowLevel;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;

namespace QuestFlight.Editor.Tests
{
    public sealed class ProductExternalControllerCompatibilityEditModeTests
    {
        private InputDevice _device;
        private GameObject _target;

        [TestCase(72, .5f)] [TestCase(90, .5f)] [TestCase(120, .5f)]
        [TestCase(72, 2f)] [TestCase(90, 2f)] [TestCase(120, 2f)]
        [TestCase(72, 10f)] [TestCase(90, 10f)] [TestCase(120, 10f)]
        public void AccumulatedThrottleMovementClaimsActualGuidanceAtEverySampleRate(int rate, float seconds)
        {
            ProductExternalControllerLayout.EnsureRegistered();
            _device = InputSystem.AddDevice(SixAxisDescription());
            _target = new GameObject("AccumulatedGuidedThrottleTest");
            _target.SetActive(false);
            var reader = _target.AddComponent<GamepadInputReader>();
            var deterministic = _target.AddComponent<DeterministicGamepadInputSource>();
            var input = _target.AddComponent<ProductControlInput>();
            input.ConfigureAuthoredSources(reader, deterministic);
            reader.ConfigureExternalFlightControllers(true, ProductExternalControllerMapping.SixAxisFlightDefault());
            _target.SetActive(true);
            QueueSixAxisState(0, 0, -1, 0, -1, -1); reader.SampleInput();
            Assert.That(reader.Current.throttlePositionChanged, Is.False);
            input.SetFlightLessonGuidance(true, new FlightLessonControlCommand { throttle = .6f, mixture = 1 });
            var mix = typeof(ProductControlInput).GetMethod("ApplyFlightLessonGuidance", BindingFlags.Instance | BindingFlags.NonPublic);
            int changes = 0, oldChanges = 0;
            float previous = 0;
            for (int frame = 1; frame <= Mathf.CeilToInt(rate * seconds); frame++)
            {
                float position = Mathf.Min(1, frame / (rate * seconds));
                if (Mathf.Abs(position - previous) >= .01f) oldChanges++;
                previous = position;
                QueueSixAxisState(0, 0, position * 2 - 1, 0, -1, -1); reader.SampleInput();
                var manual = AircraftControlState.Neutral(); manual.throttle = reader.Current.throttlePosition;
                mix.Invoke(input, new object[] { reader.Current, frame / (float)rate, 1f / rate, manual });
                if (!reader.Current.throttlePositionChanged) continue;
                changes++;
                Assert.That(manual.throttle, Is.EqualTo(reader.Current.throttlePosition).Within(.0001f), "Real assistance mixer must give the moved lever ownership.");
            }
            Assert.That(changes, Is.GreaterThan(0));
            if (seconds >= 2) Assert.That(oldChanges, Is.Zero, "Original per-frame predicate misses the same deliberate ramp.");
            reader.ConfigureExternalFlightControllers(true, ProductExternalControllerMapping.SixAxisFlightDefault());
            reader.SampleInput();
            Assert.That(reader.Current.throttlePositionChanged, Is.False, "Reconfiguration/reconnect must establish a quiet baseline.");
        }

        [Test]
        public void SubThresholdJitterDoesNotAcknowledgeMovement()
        {
            float acknowledged = .5f;
            for (int frame = 0; frame < 1000; frame++)
                Assert.That(GamepadInputReader.AcknowledgePositionMovement(.5f + .004f * Mathf.Sin(frame), true, .01f, ref acknowledged), Is.False);
            Assert.That(acknowledged, Is.EqualTo(.5f));
            Assert.That(GamepadInputReader.AcknowledgePositionMovement(.511f, true, .01f, ref acknowledged), Is.True);
            Assert.That(GamepadInputReader.AcknowledgePositionMovement(.512f, true, .01f, ref acknowledged), Is.False);
        }

        [TestCase(TrainingSessionState.Demonstration, 72)]
        [TestCase(TrainingSessionState.Demonstration, 90)]
        [TestCase(TrainingSessionState.Demonstration, 120)]
        [TestCase(TrainingSessionState.Assisting, 72)]
        [TestCase(TrainingSessionState.Assisting, 90)]
        [TestCase(TrainingSessionState.Assisting, 120)]
        public void AuthoredTrainingSlowThrottleHoldsThenSmoothlyReacquiresGuidance(
            TrainingSessionState requestedState, int rate)
        {
            float previousTimeScale = Time.timeScale;
            GameObject world = PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            try
            {
                // Retain the authored component/reference graph. Only the input
                // device and clock are deterministic fixtures, not a training mock.
                var training = world.GetComponentInChildren<ProductTrainingController>(true);
                var flight = world.GetComponentInChildren<FlightDynamicsCoordinator>(true);
                var modes = world.GetComponentInChildren<ProductFlightModeCoordinator>(true);
                var reader = world.GetComponentInChildren<GamepadInputReader>(true);
                var input = world.GetComponentInChildren<ProductControlInput>(true);
                Assert.That(training.HasAuthoredReferences, Is.True);
                Assert.That(flight.InitializeSelectedBackend(), Is.True, flight.LastError);
                ProductExternalControllerLayout.EnsureRegistered();
                _device = InputSystem.AddDevice(SixAxisDescription());
                reader.ConfigureExternalFlightControllers(true, ProductExternalControllerMapping.SixAxisFlightDefault());
                QueueSixAxisState(0, 0, -1, 0, -1, -1);
                reader.SampleInput();
                Assert.That(reader.Current.throttlePositionChanged, Is.False);

                int lesson = System.Array.FindIndex(training.Lessons,
                    candidate => candidate.allowDemonstration && candidate.start.calibratedAirspeedKnots >= 75);
                Assert.That(lesson, Is.GreaterThanOrEqualTo(0), "An authored airborne demonstration/assistance lesson is required.");
                training.OpenLessons();
                Assert.That(training.State, Is.EqualTo(TrainingSessionState.Menu));
                training.SelectLesson(lesson);
                if (requestedState == TrainingSessionState.Demonstration) training.StartDemonstration();
                else training.StartPractice();
                Assert.That(training.State, Is.EqualTo(TrainingSessionState.Preparing));
                MethodInfo prepare = typeof(ProductTrainingController).GetMethod("AdvancePreparation", BindingFlags.Instance | BindingFlags.NonPublic);
                for (int tick = 0; tick < 12 && training.State == TrainingSessionState.Preparing; tick++)
                    prepare.Invoke(training, new object[] { .1f });
                if (requestedState == TrainingSessionState.Assisting)
                {
                    Assert.That(training.State, Is.EqualTo(TrainingSessionState.Practice));
                    training.ToggleHelp();
                }
                Assert.That(training.State, Is.EqualTo(requestedState));
                Assert.That(input.DemoOwnsControls, Is.False, "Training guidance is distinct from the automatic scenic demo.");

                MethodInfo guide = typeof(ProductTrainingController).GetMethod("UpdateGuidance", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo mix = typeof(ProductControlInput).GetMethod("ApplyFlightLessonGuidance", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo ownedUntil = typeof(ProductControlInput).GetField("_manualThrottleOwnedUntil", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo command = typeof(ProductControlInput).GetField("_flightLessonGuidance", BindingFlags.Instance | BindingFlags.NonPublic);
                float holdSeconds = (float)typeof(ProductControlInput).GetField("manualThrottleHoldSeconds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input);
                float reacquisitionRate = (float)typeof(ProductControlInput).GetField("guidanceReacquisitionRatePerSecond", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input);
                Assert.That(holdSeconds, Is.EqualTo(1.25f));
                Assert.That(reacquisitionRate, Is.EqualTo(1.25f));
                float dt = 1f / rate;
                int movements = 0;
                float lastMovementTime = 0;
                for (int frame = 1; frame <= 2 * rate; frame++)
                {
                    float lever = frame / (2f * rate);
                    QueueSixAxisState(0, 0, lever * 2 - 1, 0, -1, -1);
                    reader.SampleInput();
                    SampleAuthoredTrainingGuidance(training, reader, input, guide, mix, frame * dt, dt);
                    Assert.That(training.State, Is.EqualTo(requestedState));
                    if (!reader.Current.throttlePositionChanged) continue;
                    movements++;
                    lastMovementTime = frame * dt;
                    Assert.That(input.Current.throttle, Is.EqualTo(reader.Current.throttlePosition).Within(.0001f));
                }
                Assert.That(movements, Is.GreaterThan(0), "The two-second ramp must claim actual authored guidance.");
                float expiry = (float)ownedUntil.GetValue(input);
                Assert.That(expiry, Is.EqualTo(lastMovementTime + holdSeconds).Within(.00001f));
                reader.SampleInput();
                Assert.That(reader.Current.throttlePositionChanged, Is.False);
                SampleAuthoredTrainingGuidance(training, reader, input, guide, mix, expiry - .001f, dt);
                Assert.That(input.Current.throttle, Is.EqualTo(1f).Within(.0001f), "Manual throttle remains owned until the configured hold expires.");

                // Submit the held lever through the actual coordinator/native path.
                double beforeNativeStep = flight.CurrentState.simulationTimeSeconds;
                Assert.That(flight.StepForTest(input.Current, 1d / 120), Is.True, flight.LastError);
                Assert.That(flight.LastAppliedControls.throttle, Is.EqualTo(1f).Within(.0001f));
                Assert.That(flight.CurrentState.simulationTimeSeconds, Is.EqualTo(beforeNativeStep + 1d / 120).Within(1e-7));
                Assert.That(flight.CurrentState.IsFinite, Is.True);

                float previous = input.Current.throttle;
                int reacquisitionTicks = Mathf.CeilToInt(rate / reacquisitionRate) + 2;
                for (int frame = 0; frame <= reacquisitionTicks; frame++)
                {
                    reader.SampleInput();
                    Assert.That(reader.Current.throttlePositionChanged, Is.False);
                    SampleAuthoredTrainingGuidance(training, reader, input, guide, mix,
                        expiry + .001f + frame * dt, dt);
                    float target = ((FlightLessonControlCommand)command.GetValue(input)).throttle;
                    Assert.That(target, Is.LessThan(1f), "The real lesson guidance must differ from the full manual lever.");
                    Assert.That(input.Current.throttle, Is.InRange(target - .0001f, previous + .0001f));
                    Assert.That(previous - input.Current.throttle, Is.LessThanOrEqualTo(reacquisitionRate * dt + .011f),
                        "Reacquisition is bounded per tick, including the sub-epsilon final lever displacement.");
                    previous = input.Current.throttle;
                }
                Assert.That(input.Current.throttle,
                    Is.EqualTo(((FlightLessonControlCommand)command.GetValue(input)).throttle).Within(.0001f));

                training.LeaveTraining();
                modes.SelectFreeFlight();
                Assert.That(modes.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.FreeFlight), modes.LastError);
                Assert.That(input.FlightLessonGuidanceActive, Is.False);
                QueueSixAxisState(0, 0, -.3f, 0, -1, -1);
                reader.SampleInput();
                typeof(ProductControlInput).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input, null);
                Assert.That(input.Current.throttle, Is.EqualTo(.35f).Within(.001f), "Free flight continuously reads the absolute lever without guidance.");
                InputSystem.RemoveDevice(_device);
                reader.SampleInput();
                Assert.That(reader.Current.connected, Is.False);
                _device = InputSystem.AddDevice(SixAxisDescription());
                QueueSixAxisState(0, 0, -.3f, 0, -1, -1);
                reader.SampleInput();
                Assert.That(reader.Current.connected, Is.True);
                Assert.That(reader.Current.throttlePositionChanged, Is.False, "Reconnect establishes a quiet baseline rather than an accidental takeover.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(world);
                Time.timeScale = previousTimeScale;
            }
        }

        private static void SampleAuthoredTrainingGuidance(ProductTrainingController training,
            GamepadInputReader reader, ProductControlInput input, MethodInfo guide, MethodInfo mix,
            float time, float deltaTime)
        {
            // Run the actual state's controller, then its actual input mixer with
            // an explicit clock. No physical frame timing claim is made.
            guide.Invoke(training, new object[] { deltaTime });
            Assert.That(input.FlightLessonGuidanceActive, Is.True);
            input.Current.throttle = reader.Current.throttlePosition;
            input.Current.mixture = 1;
            mix.Invoke(input, new object[] { reader.Current, time, deltaTime, input.Current });
        }

        [SetUp]
        public void SetUp()
        {
            InvokeInputSystemTestLifecycle("SaveAndReset", false, null);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_target != null)
                    Object.DestroyImmediate(_target);
                if (_device != null && _device.added)
                    InputSystem.RemoveDevice(_device);
                _target = null;
                _device = null;
            }
            finally
            {
                InvokeInputSystemTestLifecycle("Restore");
            }
        }

        [Test]
        public void SerializedProductEnablesOneValidSixAxisMapping()
        {
            GameObject world = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductExternalControllerCompatibilityAuthoring.WorldPrefabPath);
            Assert.That(world, Is.Not.Null);
            GamepadInputReader[] readers = world
                .GetComponentsInChildren<GamepadInputReader>(true);
            Assert.That(readers, Has.Length.EqualTo(1));
            Assert.That(readers[0].transform.name, Is.EqualTo("ProductionInputRoot"));
            Assert.That(
                ProductExternalControllerCompatibilityAuthoring.IsConfigured(
                    readers[0]),
                Is.True);
            Assert.That(File.ReadAllText(
                    ProductExternalControllerCompatibilityAuthoring.WorldPrefabPath),
                Does.Contain("externalFlightControllersEnabled:"));
        }

        [Test]
        public void AndroidSixAxisDeviceMapsEveryFlightControlAndHoldsPositions()
        {
            ProductExternalControllerLayout.EnsureRegistered();
            _device = InputSystem.AddDevice(SixAxisDescription());
            Assert.That(_device, Is.InstanceOf<Gamepad>());
            Assert.That(GamepadInputReader.IsSixAxisFlightController(_device), Is.True);

            _target = new GameObject("ExternalControllerReaderTest");
            GamepadInputReader reader = _target.AddComponent<GamepadInputReader>();
            reader.ConfigureExternalFlightControllers(
                true,
                ProductExternalControllerMapping.SixAxisFlightDefault());

            QueueSixAxisState(-0.75f, 0.5f, -1f, 0.6f, -1f, 1f);
            reader.SampleInput();
            Assert.That(reader.UsingExternalFlightController, Is.True);
            Assert.That(reader.Current.externalFlightController, Is.True);
            Assert.That(reader.Current.leftStickX, Is.EqualTo(-0.75f).Within(0.001f));
            Assert.That(reader.Current.leftStickY, Is.EqualTo(-0.5f).Within(0.001f));
            Assert.That(reader.Current.rightStickX, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(reader.Current.throttlePosition, Is.EqualTo(0f).Within(0.001f));
            Assert.That(reader.Current.leftTrigger, Is.EqualTo(0f).Within(0.001f));
            Assert.That(reader.Current.rightTrigger, Is.EqualTo(1f).Within(0.001f));
            Assert.That(reader.Current.throttlePositionChanged, Is.False,
                "Initial device synchronization must not seize Demo throttle authority.");

            QueueSixAxisState(-0.75f, 0.5f, 1f, 0.6f, -1f, 1f);
            reader.SampleInput();
            Assert.That(reader.Current.throttlePosition, Is.EqualTo(1f).Within(0.001f));
            Assert.That(reader.Current.throttlePositionChanged, Is.True);

            reader.SampleInput();
            Assert.That(reader.Current.throttlePosition, Is.EqualTo(1f).Within(0.001f));
            Assert.That(reader.Current.throttlePositionChanged, Is.False,
                "A held physical lever remains at its position without pretending to move every frame.");
        }

        [Test]
        public void ExternalControllerWithoutFreshReportPublishesDisconnectedNeutralSnapshot()
        {
            ProductExternalControllerLayout.EnsureRegistered();
            _device = InputSystem.AddDevice(SixAxisDescription());
            _target = new GameObject("ExternalControllerInitialStateTest");
            GamepadInputReader reader = _target.AddComponent<GamepadInputReader>();
            reader.ConfigureExternalFlightControllers(
                true,
                ProductExternalControllerMapping.SixAxisFlightDefault());

            reader.SampleInput();

            Assert.That(reader.Current.connected, Is.False,
                "A zero-filled Android device state is not a physical HID report.");
            Assert.That(reader.Current.hasThrottlePosition, Is.False);
            Assert.That(reader.Current.leftTrigger, Is.Zero);
            Assert.That(reader.Current.rightTrigger, Is.Zero,
                "Synthetic startup state must never apply half toe brakes.");

            QueueSixAxisState(0f, 0f, 0f, 0f, -1f, -1f);
            reader.SampleInput();

            Assert.That(reader.Current.connected, Is.True);
            Assert.That(reader.Current.hasThrottlePosition, Is.True);
            Assert.That(reader.Current.leftTrigger, Is.Zero.Within(0.001f));
            Assert.That(reader.Current.rightTrigger, Is.Zero.Within(0.001f));
        }

        [Test]
        public void DisabledExternalMappingDoesNotCaptureSixAxisDevice()
        {
            ProductExternalControllerLayout.EnsureRegistered();
            _device = InputSystem.AddDevice(SixAxisDescription());
            _target = new GameObject("ExternalControllerDisabledTest");
            GamepadInputReader reader = _target.AddComponent<GamepadInputReader>();
            reader.ConfigureExternalFlightControllers(
                false,
                ProductExternalControllerMapping.SixAxisFlightDefault());
            reader.SampleInput();

            Assert.That(reader.ExternalFlightControllersEnabled, Is.False);
            Assert.That(reader.UsingExternalFlightController, Is.False);
            Assert.That(reader.Current.connected, Is.False);
        }

        [Test]
        public void StandardGamepadPathRetainsExistingSemantics()
        {
            _device = InputSystem.AddDevice<Gamepad>();
            _target = new GameObject("StandardGamepadReaderTest");
            GamepadInputReader reader = _target.AddComponent<GamepadInputReader>();
            InputSystem.QueueStateEvent(_device, new GamepadState
            {
                leftStick = new Vector2(0.25f, -0.5f),
                rightStick = new Vector2(0.6f, 0.75f),
                leftTrigger = 0.2f,
                rightTrigger = 0.8f
            });
            InputSystem.Update();
            reader.SampleInput();

            Assert.That(reader.Current.externalFlightController, Is.False);
            Assert.That(reader.Current.hasThrottlePosition, Is.False);
            Gamepad gamepad = (Gamepad)_device;
            Vector2 expectedLeft = gamepad.leftStick.ReadValue();
            Vector2 expectedRight = gamepad.rightStick.ReadValue();
            Assert.That(reader.Current.leftStickX,
                Is.EqualTo(expectedLeft.x).Within(0.001f));
            Assert.That(reader.Current.leftStickY,
                Is.EqualTo(expectedLeft.y).Within(0.001f));
            Assert.That(reader.Current.rightStickX,
                Is.EqualTo(expectedRight.x).Within(0.001f));
            Assert.That(reader.Current.rightStickY,
                Is.EqualTo(expectedRight.y).Within(0.001f));
            Assert.That(reader.Current.leftTrigger,
                Is.EqualTo(gamepad.leftTrigger.ReadValue()).Within(0.001f));
            Assert.That(reader.Current.rightTrigger,
                Is.EqualTo(gamepad.rightTrigger.ReadValue()).Within(0.001f));
        }

        [Test]
        public void QuestVrPeripheralCannotDisplaceExternalFlightController()
        {
            ProductExternalControllerLayout.EnsureRegistered();
            _device = InputSystem.AddDevice(SixAxisDescription());
            _target = new GameObject("QuestVrPeripheralExclusionTest");
            GamepadInputReader reader = _target.AddComponent<GamepadInputReader>();
            reader.ConfigureExternalFlightControllers(
                true,
                ProductExternalControllerMapping.SixAxisFlightDefault());

            QueueSixAxisState(0.15f, -0.2f, 0.4f, 0.1f, -1f, -1f);
            reader.SampleInput();
            Assert.That(reader.UsingExternalFlightController, Is.True);
            int externalDeviceId = reader.Current.deviceId;

            InputDevice questPeripheral = InputSystem.AddDevice(
                QuestVrPeripheralDescription());
            Assert.That(questPeripheral, Is.InstanceOf<Gamepad>());
            Assert.That(GamepadInputReader.IsQuestVrPeripheral(questPeripheral),
                Is.True);
            InputSystem.QueueStateEvent(
                questPeripheral,
                new AndroidGameControllerState()
                    .WithAxis(AndroidAxis.X, -1f)
                    .WithAxis(AndroidAxis.Y, -1f)
                    .WithAxis(AndroidAxis.Rx, -1f)
                    .WithAxis(AndroidAxis.Ry, -1f)
                    .WithAxis(AndroidAxis.HatX, 0f)
                    .WithAxis(AndroidAxis.HatY, 0f));
            InputSystem.Update();
            reader.SampleInput();

            Assert.That(reader.UsingExternalFlightController, Is.True);
            Assert.That(reader.Current.deviceId, Is.EqualTo(externalDeviceId),
                "Menu input from Quest's internal VR peripheral must not replace T2 or another external flight controller.");
        }

        [Test]
        public void PositionControlsHoldTheirMappedValuesWithoutRateInput()
        {
            GamepadInputSnapshot snapshot = new GamepadInputSnapshot
            {
                connected = true,
                hasThrottlePosition = true,
                throttlePosition = 0.73f,
                hasMixturePosition = true,
                mixturePosition = 0.81f,
                hasFlapsPosition = true,
                flapsPosition = 0.5f
            };
            float throttle = 0.1f;
            float mixture = 1f;
            float flaps = 0f;

            ProductControlInput.ApplyPositionControls(
                snapshot,
                ref throttle,
                ref mixture,
                ref flaps);
            Assert.That(throttle, Is.EqualTo(0.73f).Within(0.0001f));
            Assert.That(mixture, Is.EqualTo(0.81f).Within(0.0001f));
            Assert.That(flaps, Is.EqualTo(0.5f).Within(0.0001f));

            ProductControlInput.ApplyPositionControls(
                snapshot,
                ref throttle,
                ref mixture,
                ref flaps);
            Assert.That(throttle, Is.EqualTo(0.73f).Within(0.0001f));
            Assert.That(mixture, Is.EqualTo(0.81f).Within(0.0001f));
            Assert.That(flaps, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void EnabledExternalControllerRemainsOnProductBoundaryDuringGuidedMode()
        {
            ProductExternalControllerLayout.EnsureRegistered();
            _device = InputSystem.AddDevice(SixAxisDescription());
            _target = new GameObject("ExternalControllerGuidedModeTest");
            _target.SetActive(false);
            GamepadInputReader reader = _target.AddComponent<GamepadInputReader>();
            DeterministicGamepadInputSource deterministic =
                _target.AddComponent<DeterministicGamepadInputSource>();
            ProductControlInput input = _target.AddComponent<ProductControlInput>();
            input.ConfigureAuthoredSources(reader, deterministic);
            _target.SetActive(true);

            QueueSixAxisState(0.85f, 0f, 0.4f, 0f, -1f, -1f);
            reader.SampleInput();
            input.SetDeterministicMode(true);
            input.SetFlightLessonGuidance(
                true,
                new FlightLessonControlCommand
                {
                    throttle = 0.6f,
                    mixture = 1f
                });
            MethodInfo update = typeof(ProductControlInput).GetMethod(
                "Update",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(update, Is.Not.Null);
            update.Invoke(input, null);

            Assert.That(reader.UsingExternalFlightController, Is.True);
            Assert.That(input.Current.aileron, Is.GreaterThan(0.25f),
                "Guided/Demo mode must continue sampling enabled external primary controls.");
            Assert.That(input.Current.throttle, Is.EqualTo(0.6f).Within(0.001f),
                "An untouched external throttle must not suppress the Demo pilot's takeoff power.");
            Assert.That(input.Current.leftToeBrake, Is.LessThan(0.001f));
            Assert.That(input.Current.rightToeBrake, Is.LessThan(0.001f));
            Assert.That(input.DeterministicMode, Is.True,
                "External input must not rewrite the established Demo lifecycle flag.");
            Assert.That(input.FlightLessonGuidanceActive, Is.True);
        }

        [Test]
        public void RuntimeCompatibilityCodeDoesNotConstructHierarchyOrWriteTransforms()
        {
            string reader = File.ReadAllText(
                "Assets/Scripts/Input/GamepadInputReader.cs");
            string layout = File.ReadAllText(
                "Assets/Scripts/Input/ProductExternalControllerLayout.cs");
            string mapping = File.ReadAllText(
                "Assets/Scripts/Input/ProductExternalControllerMapping.cs");
            string source = reader + layout + mapping;
            string[] forbidden =
            {
                "FindObjectOfType", "FindFirstObjectByType", "Resources.Load",
                "Instantiate(", "new GameObject", "AddComponent", "transform.position",
                "transform.rotation", "SetPositionAndRotation", "Main Camera", "XR Origin"
            };
            foreach (string token in forbidden)
                Assert.That(source, Does.Not.Contain(token), token);
        }

        private void QueueSixAxisState(
            float x,
            float y,
            float z,
            float rx,
            float ry,
            float rz)
        {
            AndroidGameControllerState state = new AndroidGameControllerState()
                .WithAxis(AndroidAxis.X, x)
                .WithAxis(AndroidAxis.Y, y)
                .WithAxis(AndroidAxis.Z, z)
                .WithAxis(AndroidAxis.Rx, rx)
                .WithAxis(AndroidAxis.Ry, ry)
                .WithAxis(AndroidAxis.Rz, rz)
                .WithAxis(AndroidAxis.HatX, 0f)
                .WithAxis(AndroidAxis.HatY, 0f);
            InputSystem.QueueStateEvent(_device, state);
            InputSystem.Update();
        }

        private static InputDeviceDescription SixAxisDescription() =>
            new InputDeviceDescription
            {
                interfaceName = "Android",
                deviceClass = "AndroidGameController",
                product = "Six Axis Flight Controller",
                capabilities =
                    "{\"deviceDescriptor\":\"questflight-test\"," +
                    "\"productId\":16385,\"vendorId\":12346," +
                    "\"isVirtual\":false," +
                    "\"motionAxes\":[0,1,11,12,13,14,15,16]," +
                    "\"inputSources\":16778257}"
            };

        private static InputDeviceDescription QuestVrPeripheralDescription() =>
            new InputDeviceDescription
            {
                interfaceName = "Android",
                deviceClass = "AndroidGameController",
                product = "Device 0xA61A6F6535F7E81A",
                capabilities =
                    "{\"deviceDescriptor\":\"quest-vr-peripheral\"," +
                    "\"productId\":352,\"vendorId\":10291," +
                    "\"isVirtual\":false," +
                    "\"motionAxes\":[0,1,11,12,15,16]," +
                    "\"inputSources\":16778257}"
            };

        private static void InvokeInputSystemTestLifecycle(
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = typeof(InputSystem).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null,
                $"Unity Input System test lifecycle method {methodName} is unavailable.");
            method.Invoke(null, arguments);
        }
    }
}
#endif
