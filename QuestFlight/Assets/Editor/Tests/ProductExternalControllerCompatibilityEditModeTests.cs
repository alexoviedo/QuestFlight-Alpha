#if UNITY_INCLUDE_TESTS
using System.IO;
using System.Reflection;
using NUnit.Framework;
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
