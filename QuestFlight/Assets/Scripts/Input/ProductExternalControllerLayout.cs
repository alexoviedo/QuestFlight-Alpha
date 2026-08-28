using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

#if UNITY_EDITOR || UNITY_ANDROID
using UnityEngine.InputSystem.Android;
using UnityEngine.InputSystem.Android.LowLevel;
#endif

namespace QuestFlightLab.Input
{
#if UNITY_EDITOR || UNITY_ANDROID
    [StructLayout(LayoutKind.Explicit, Size = 220)]
    internal struct ProductExternalControllerState : IInputStateTypeInfo
    {
        [InputControl(name = ProductExternalControllerLayout.AileronAxisControl,
            layout = "Axis")]
        [FieldOffset(28)] public float x;

        [InputControl(name = ProductExternalControllerLayout.ElevatorAxisControl,
            layout = "Axis")]
        [FieldOffset(32)] public float y;

        [InputControl(name = ProductExternalControllerLayout.ThrottleAxisControl,
            layout = "Axis")]
        [FieldOffset(72)] public float z;

        [InputControl(name = ProductExternalControllerLayout.RudderAxisControl,
            layout = "Axis")]
        [FieldOffset(76)] public float rx;

        [InputControl(name = ProductExternalControllerLayout.LeftToeBrakeAxisControl,
            layout = "Axis")]
        [FieldOffset(80)] public float ry;

        [InputControl(name = ProductExternalControllerLayout.RightToeBrakeAxisControl,
            layout = "Axis")]
        [FieldOffset(84)] public float rz;

        public FourCC format => AndroidGameControllerState.kFormat;
    }

    [InputControlLayout(
        displayName = "QuestFlight Six-Axis Flight Controller",
        stateType = typeof(ProductExternalControllerState))]
    internal sealed class ProductExternalControllerDevice :
        AndroidGamepadWithDpadAxes
    {
        public AxisControl flightAxisX { get; private set; }
        public AxisControl flightAxisY { get; private set; }
        public AxisControl flightThrottleAxis { get; private set; }
        public AxisControl flightRudderAxis { get; private set; }
        public AxisControl flightLeftToeBrakeAxis { get; private set; }
        public AxisControl flightRightToeBrakeAxis { get; private set; }

        protected override void FinishSetup()
        {
            base.FinishSetup();
            flightAxisX = GetChildControl<AxisControl>(
                ProductExternalControllerLayout.AileronAxisControl);
            flightAxisY = GetChildControl<AxisControl>(
                ProductExternalControllerLayout.ElevatorAxisControl);
            flightThrottleAxis = GetChildControl<AxisControl>(
                ProductExternalControllerLayout.ThrottleAxisControl);
            flightRudderAxis = GetChildControl<AxisControl>(
                ProductExternalControllerLayout.RudderAxisControl);
            flightLeftToeBrakeAxis = GetChildControl<AxisControl>(
                ProductExternalControllerLayout.LeftToeBrakeAxisControl);
            flightRightToeBrakeAxis = GetChildControl<AxisControl>(
                ProductExternalControllerLayout.RightToeBrakeAxisControl);
        }
    }
#endif

    /// <summary>
    /// Registers a dedicated Android layout for T2's six-axis Generic Gamepad
    /// persona. Ordinary generic and Xbox-style gamepads keep Unity's standard
    /// layout and behavior.
    /// </summary>
    public static class ProductExternalControllerLayout
    {
        public const string LayoutName =
            "QuestFlightAndroidSixAxisFlightController";
        public const string AileronAxisControl = "flightAxisX";
        public const string ElevatorAxisControl = "flightAxisY";
        public const string ThrottleAxisControl = "flightThrottleAxis";
        public const string RudderAxisControl = "flightRudderAxis";
        public const string LeftToeBrakeAxisControl = "flightLeftToeBrakeAxis";
        public const string RightToeBrakeAxisControl = "flightRightToeBrakeAxis";

        // Espressif's USB2BLE Generic Gamepad identity used by T2 firmware.
        // Matching both values avoids claiming unrelated Android controllers.
        internal const int T2VendorId = 0x303A;
        internal const int T2GenericGamepadProductId = 0x4001;

        private static bool _registered;

        [Serializable]
        private sealed class AndroidControllerCapabilities
        {
            public int[] motionAxes;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterBeforeSceneLoad() => EnsureRegistered();

        public static void EnsureRegistered()
        {
            if (_registered && InputSystem.LoadLayout(LayoutName) != null)
                return;

#if UNITY_EDITOR || UNITY_ANDROID
            // A registered matcher is required here: AndroidSupport's own
            // layout callback runs before product callbacks and would otherwise
            // select the ordinary two-stick layout first. Unity 6000.3.8f1's
            // bundled Input System throws for array-index capability matchers,
            // so use T2's stable Generic Gamepad identity and still verify the
            // complete six-axis capability set before sampling in the reader.
            InputSystem.RegisterLayout<ProductExternalControllerDevice>(
                LayoutName,
                matches: new InputDeviceMatcher()
                    .WithInterface("Android")
                    .WithDeviceClass("AndroidGameController")
                    .WithCapability("vendorId", T2VendorId)
                    .WithCapability(
                        "productId", T2GenericGamepadProductId));
#endif
            _registered = true;
        }

        internal static bool HasSixFlightAxes(string capabilitiesJson)
        {
            if (string.IsNullOrWhiteSpace(capabilitiesJson))
                return false;
            AndroidControllerCapabilities capabilities;
            try
            {
                capabilities = JsonUtility.FromJson<AndroidControllerCapabilities>(
                    capabilitiesJson);
            }
            catch (ArgumentException)
            {
                return false;
            }

            int[] axes = capabilities?.motionAxes;
            return axes != null &&
                   ContainsAxis(axes, 0) &&
                   ContainsAxis(axes, 1) &&
                   ContainsAxis(axes, 11) &&
                   ContainsAxis(axes, 12) &&
                   ContainsAxis(axes, 13) &&
                   ContainsAxis(axes, 14);
        }

        private static bool ContainsAxis(int[] axes, int expected)
        {
            for (int i = 0; i < axes.Length; i++)
                if (axes[i] == expected)
                    return true;
            return false;
        }
    }
}
