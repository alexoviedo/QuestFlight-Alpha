using System;
using System.Runtime.InteropServices;

namespace AlexOviedo.QuestFlight.JSBSim
{
    /// <summary>
    /// Sole managed owner of the compact qfl_jsbsim_native C ABI. Strings cross
    /// the boundary only while loading the aircraft; fixed-step methods use
    /// blittable value types and do not allocate in steady state.
    /// </summary>
    public sealed class JSBSimNativeSession : IDisposable
    {
        public const int ExpectedApiVersion = 1;
        public const string PinnedVersion = "1.3.1";
        public const string PinnedRevision = "3b25f25e49b42d0489c04ac805674fc1450ca579";
        public const string PinnedAircraftModel = "c172x";
        public const string PinnedModelConfiguration = "qfl-c172p-training-2054-v1";
        public const double PinnedEmptyWeightPounds = 1454.0;
        public const double PinnedOccupantWeightPounds = 340.0;
        public const double PinnedFuelWeightPounds = 260.0;
        public const double PinnedInitialWeightPounds = 2054.0;

        private IntPtr _instance;
        private NativeControls _controls;
        private NativeAtmosphere _atmosphere;

        public bool IsInitialized => _instance != IntPtr.Zero;
        public string LastError { get; private set; } = string.Empty;
        public JSBSimNativeState State { get; private set; }
        public JSBSimNativeInitialConditions ConfiguredInitialConditions { get; private set; }
        public JSBSimNativeControls AppliedControls { get; private set; }
        public JSBSimNativeAtmosphere AppliedAtmosphere { get; private set; }

        public static bool TryProbe(out string error)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_ANDROID
            try
            {
                int version = NativeMethods.ApiVersion();
                if (version != ExpectedApiVersion)
                {
                    error = $"Native JSBSim ABI version {version} does not match expected version {ExpectedApiVersion}.";
                    return false;
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException ||
                exception is EntryPointNotFoundException ||
                exception is BadImageFormatException)
            {
                error = exception.Message;
                return false;
            }
#else
            error = "The pinned native JSBSim plugin supports Windows x86_64 and Android ARM64 only.";
            return false;
#endif
        }

        public bool Initialize(string dataRoot, string aircraftName = PinnedAircraftModel)
        {
            Dispose();
            if (!TryProbe(out string probeError)) return Fail(probeError);
            if (string.IsNullOrWhiteSpace(dataRoot)) return Fail("A JSBSim data root is required.");
            if (string.IsNullOrWhiteSpace(aircraftName)) return Fail("A JSBSim aircraft name is required.");

            try
            {
                _instance = NativeMethods.Create();
                if (_instance == IntPtr.Zero)
                    return Fail(ReadLastError(IntPtr.Zero, "Could not create a native JSBSim instance."));
                if (NativeMethods.LoadAircraft(_instance, dataRoot, aircraftName) == 0)
                {
                    string error = ReadLastError(_instance, "Could not load the JSBSim aircraft.");
                    Dispose();
                    return Fail(error);
                }

                LastError = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException ||
                exception is EntryPointNotFoundException ||
                exception is BadImageFormatException)
            {
                Dispose();
                return Fail(exception.Message);
            }
        }

        public bool Reset(JSBSimNativeInitialConditions initialConditions)
        {
            if (!RequireInitialized()) return false;
            ConfiguredInitialConditions = initialConditions;
            SetControls(InitialControls(initialConditions.engineRunning != 0));
            NativeInitialConditions native = new NativeInitialConditions
            {
                latitudeDegrees = initialConditions.latitudeDegrees,
                longitudeDegrees = initialConditions.longitudeDegrees,
                altitudeMslFeet = initialConditions.altitudeMslFeet,
                terrainElevationMslFeet = initialConditions.terrainElevationMslFeet,
                calibratedAirspeedKnots = initialConditions.calibratedAirspeedKnots,
                headingDegrees = initialConditions.headingDegrees,
                pitchDegrees = initialConditions.pitchDegrees,
                bankDegrees = initialConditions.bankDegrees,
                flightPathAngleDegrees = initialConditions.flightPathAngleDegrees,
                engineRunning = initialConditions.engineRunning
            };
            if (NativeMethods.Reset(_instance, ref native) == 0)
                return Fail(ReadLastError(_instance, "JSBSim reset failed."));
            if (NativeMethods.SetControls(_instance, ref _controls) == 0)
                return Fail(ReadLastError(_instance, "Could not set initial JSBSim controls."));
            if (NativeMethods.SetAtmosphere(_instance, ref _atmosphere) == 0)
                return Fail(ReadLastError(_instance, "Could not set initial JSBSim atmosphere."));
            return CaptureState();
        }

        /// <summary>
        /// Sets normalized project-contract controls. Positive elevator means
        /// nose-up and positive rudder means yaw-right; the stock c172x ABI
        /// sign conversion is contained here.
        /// </summary>
        public void SetControls(JSBSimNativeControls controls)
        {
            AppliedControls = new JSBSimNativeControls
            {
                aileron = Clamp(controls.aileron, -1.0, 1.0),
                elevatorNoseUp = Clamp(controls.elevatorNoseUp, -1.0, 1.0),
                rudderYawRight = Clamp(controls.rudderYawRight, -1.0, 1.0),
                throttle = Clamp(controls.throttle, 0.0, 1.0),
                mixture = Clamp(controls.mixture, 0.0, 1.0),
                carbHeat = Clamp(controls.carbHeat, 0.0, 1.0),
                elevatorTrimNoseUp = Clamp(controls.elevatorTrimNoseUp, -1.0, 1.0),
                flaps = Clamp(controls.flaps, 0.0, 1.0),
                leftBrake = Clamp(controls.leftBrake, 0.0, 1.0),
                rightBrake = Clamp(controls.rightBrake, 0.0, 1.0)
            };
            _controls = new NativeControls
            {
                aileron = AppliedControls.aileron,
                elevator = -AppliedControls.elevatorNoseUp,
                rudder = -AppliedControls.rudderYawRight,
                throttle = AppliedControls.throttle,
                mixture = AppliedControls.mixture,
                carbHeat = AppliedControls.carbHeat,
                elevatorTrim = -AppliedControls.elevatorTrimNoseUp,
                flaps = AppliedControls.flaps,
                leftBrake = AppliedControls.leftBrake,
                rightBrake = AppliedControls.rightBrake
            };
        }

        public void SetAtmosphere(JSBSimNativeAtmosphere atmosphere)
        {
            AppliedAtmosphere = atmosphere;
            _atmosphere = new NativeAtmosphere
            {
                windNorthFeetPerSecond = atmosphere.windNorthFeetPerSecond,
                windEastFeetPerSecond = atmosphere.windEastFeetPerSecond,
                windDownFeetPerSecond = atmosphere.windDownFeetPerSecond
            };
        }

        public bool Advance(double fixedDeltaTimeSeconds)
        {
            if (!RequireInitialized()) return false;
            if (fixedDeltaTimeSeconds <= 0.0) return Fail("Fixed timestep must be positive.");
            if (NativeMethods.SetControls(_instance, ref _controls) == 0 ||
                NativeMethods.SetAtmosphere(_instance, ref _atmosphere) == 0 ||
                NativeMethods.Advance(_instance, fixedDeltaTimeSeconds) == 0)
            {
                return Fail(ReadLastError(_instance, "JSBSim fixed step failed."));
            }
            return CaptureState();
        }

        public void Dispose()
        {
            if (_instance != IntPtr.Zero)
            {
                NativeMethods.Destroy(_instance);
                _instance = IntPtr.Zero;
            }
        }

        private bool CaptureState()
        {
            if (NativeMethods.GetState(_instance, out NativeState native) == 0)
                return Fail(ReadLastError(_instance, "Could not read JSBSim state."));
            State = new JSBSimNativeState
            {
                simulationTimeSeconds = native.simulationTimeSeconds,
                latitudeDegrees = native.latitudeDegrees,
                longitudeDegrees = native.longitudeDegrees,
                altitudeMslFeet = native.altitudeMslFeet,
                altitudeAglFeet = native.altitudeAglFeet,
                calibratedAirspeedKnots = native.calibratedAirspeedKnots,
                groundSpeedKnots = native.groundSpeedKnots,
                verticalSpeedFeetPerMinute = native.verticalSpeedFeetPerMinute,
                headingDegrees = native.headingDegrees,
                pitchDegrees = native.pitchDegrees,
                bankDegrees = native.bankDegrees,
                angleOfAttackDegrees = native.angleOfAttackDegrees,
                sideslipDegrees = native.sideslipDegrees,
                velocityNorthFeetPerSecond = native.velocityNorthFeetPerSecond,
                velocityEastFeetPerSecond = native.velocityEastFeetPerSecond,
                velocityDownFeetPerSecond = native.velocityDownFeetPerSecond,
                rollRateRadiansPerSecond = native.rollRateRadiansPerSecond,
                pitchRateRadiansPerSecond = native.pitchRateRadiansPerSecond,
                yawRateRadiansPerSecond = native.yawRateRadiansPerSecond,
                engineRpm = native.engineRpm,
                loadFactorG = native.loadFactorG,
                weightOnWheels = native.weightOnWheels
            };
            if (!State.IsFinite) return Fail("JSBSim returned non-finite state.");
            LastError = string.Empty;
            return true;
        }

        private bool RequireInitialized()
        {
            return IsInitialized || Fail("Native JSBSim session is not initialized.");
        }

        private bool Fail(string message)
        {
            LastError = string.IsNullOrWhiteSpace(message) ? "Native JSBSim operation failed." : message;
            return false;
        }

        private static string ReadLastError(IntPtr instance, string fallback)
        {
            IntPtr pointer = NativeMethods.LastError(instance);
            string message = pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(pointer);
            return string.IsNullOrWhiteSpace(message) ? fallback : message;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static JSBSimNativeControls InitialControls(bool engineRunning)
        {
            return new JSBSimNativeControls
            {
                throttle = engineRunning ? 0.10 : 0.0,
                mixture = engineRunning ? 1.0 : 0.0
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInitialConditions
        {
            public double latitudeDegrees, longitudeDegrees, altitudeMslFeet, terrainElevationMslFeet;
            public double calibratedAirspeedKnots, headingDegrees, pitchDegrees, bankDegrees;
            public double flightPathAngleDegrees;
            public int engineRunning;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeControls
        {
            public double aileron, elevator, rudder, throttle, mixture;
            public double carbHeat, elevatorTrim, flaps, leftBrake, rightBrake;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeAtmosphere
        {
            public double windNorthFeetPerSecond, windEastFeetPerSecond, windDownFeetPerSecond;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeState
        {
            public double simulationTimeSeconds, latitudeDegrees, longitudeDegrees;
            public double altitudeMslFeet, altitudeAglFeet, calibratedAirspeedKnots;
            public double groundSpeedKnots, verticalSpeedFeetPerMinute, headingDegrees;
            public double pitchDegrees, bankDegrees, angleOfAttackDegrees, sideslipDegrees;
            public double velocityNorthFeetPerSecond, velocityEastFeetPerSecond, velocityDownFeetPerSecond;
            public double rollRateRadiansPerSecond, pitchRateRadiansPerSecond, yawRateRadiansPerSecond;
            public double engineRpm, loadFactorG;
            public int weightOnWheels;
        }

        private static class NativeMethods
        {
            private const string Library = "qfl_jsbsim_native";

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "qfl_jsbsim_api_version")]
            internal static extern int ApiVersion();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "qfl_jsbsim_create")]
            internal static extern IntPtr Create();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "qfl_jsbsim_destroy")]
            internal static extern void Destroy(IntPtr instance);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi,
                EntryPoint = "qfl_jsbsim_load_aircraft")]
            internal static extern int LoadAircraft(IntPtr instance, string dataRoot, string aircraftName);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "qfl_jsbsim_reset")]
            internal static extern int Reset(IntPtr instance, ref NativeInitialConditions initialConditions);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "qfl_jsbsim_set_controls")]
            internal static extern int SetControls(IntPtr instance, ref NativeControls controls);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "qfl_jsbsim_set_atmosphere")]
            internal static extern int SetAtmosphere(IntPtr instance, ref NativeAtmosphere atmosphere);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "qfl_jsbsim_advance")]
            internal static extern int Advance(IntPtr instance, double fixedDeltaTimeSeconds);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "qfl_jsbsim_get_state")]
            internal static extern int GetState(IntPtr instance, out NativeState state);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "qfl_jsbsim_last_error")]
            internal static extern IntPtr LastError(IntPtr instance);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeInitialConditions
    {
        public double latitudeDegrees, longitudeDegrees, altitudeMslFeet, terrainElevationMslFeet;
        public double calibratedAirspeedKnots, headingDegrees, pitchDegrees, bankDegrees;
        public double flightPathAngleDegrees;
        public int engineRunning;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeControls
    {
        public double aileron, elevatorNoseUp, rudderYawRight, throttle, mixture;
        public double carbHeat, elevatorTrimNoseUp, flaps, leftBrake, rightBrake;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeAtmosphere
    {
        public double windNorthFeetPerSecond, windEastFeetPerSecond, windDownFeetPerSecond;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeState
    {
        public double simulationTimeSeconds, latitudeDegrees, longitudeDegrees;
        public double altitudeMslFeet, altitudeAglFeet, calibratedAirspeedKnots;
        public double groundSpeedKnots, verticalSpeedFeetPerMinute, headingDegrees;
        public double pitchDegrees, bankDegrees, angleOfAttackDegrees, sideslipDegrees;
        public double velocityNorthFeetPerSecond, velocityEastFeetPerSecond, velocityDownFeetPerSecond;
        public double rollRateRadiansPerSecond, pitchRateRadiansPerSecond, yawRateRadiansPerSecond;
        public double engineRpm, loadFactorG;
        public int weightOnWheels;

        public bool IsFinite =>
            Finite(simulationTimeSeconds) && Finite(latitudeDegrees) && Finite(longitudeDegrees) &&
            Finite(altitudeMslFeet) && Finite(altitudeAglFeet) && Finite(calibratedAirspeedKnots) &&
            Finite(groundSpeedKnots) && Finite(verticalSpeedFeetPerMinute) && Finite(headingDegrees) &&
            Finite(pitchDegrees) && Finite(bankDegrees) && Finite(angleOfAttackDegrees) &&
            Finite(sideslipDegrees) && Finite(velocityNorthFeetPerSecond) &&
            Finite(velocityEastFeetPerSecond) && Finite(velocityDownFeetPerSecond) &&
            Finite(rollRateRadiansPerSecond) && Finite(pitchRateRadiansPerSecond) &&
            Finite(yawRateRadiansPerSecond) && Finite(engineRpm) && Finite(loadFactorG);

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
