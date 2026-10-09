using System;
using System.Runtime.InteropServices;

namespace AlexOviedo.QuestFlight.JSBSim
{
    /// <summary>
    /// Sole managed owner of the compact qfl_jsbsim_native C ABI. The fixed-step
    /// path uses blittable values and performs no managed allocation.
    /// </summary>
    public sealed class JSBSimNativeSession : IDisposable
    {
        public const int ExpectedApiVersion = 2;
        public const string PinnedVersion = "1.3.1";
        public const string PinnedRevision = "3b25f25e49b42d0489c04ac805674fc1450ca579";
        public const string PinnedAircraftModel = "c172x";
        public const string PinnedModelConfiguration = "qfl-c172p-private-fidelity-v2";
        public const double PinnedEmptyWeightPounds = 1454.0;
        public const double PinnedInitialWeightPounds = 2054.0;

        private IntPtr _instance;
        private NativeControls _controls;
        private NativeAtmosphere _atmosphere;
        private string _dataRoot;
        private string _aircraftName;

        public bool IsInitialized => _instance != IntPtr.Zero;
        public string LastError { get; private set; } = string.Empty;
        public JSBSimNativeState State { get; private set; }
        public JSBSimNativeDiagnostics Diagnostics { get; private set; }
        public JSBSimNativeInitialConditions ConfiguredInitialConditions { get; private set; }
        public JSBSimNativeMassConfiguration ConfiguredMass { get; private set; }
        public JSBSimNativeControls AppliedControls { get; private set; }
        public JSBSimNativeAtmosphere AppliedAtmosphere { get; private set; }

        public static JSBSimNativeMassConfiguration DefaultMass => new JSBSimNativeMassConfiguration
        {
            pilotPounds = 170.0, copilotPounds = 170.0,
            leftFuelPounds = 130.0, rightFuelPounds = 130.0
        };

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
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException || exception is BadImageFormatException)
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
            if (string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(aircraftName)) return Fail("A data root and aircraft name are required.");
            try
            {
                _instance = NativeMethods.Create();
                if (_instance == IntPtr.Zero) return Fail(ReadLastError(IntPtr.Zero, "Could not create native JSBSim."));
                if (NativeMethods.LoadAircraft(_instance, dataRoot, aircraftName) == 0)
                {
                    string error = ReadLastError(_instance, "Could not load JSBSim aircraft."); Dispose(); return Fail(error);
                }
                if (!SetMassConfiguration(DefaultMass)) { Dispose(); return false; }
                _dataRoot = dataRoot;
                _aircraftName = aircraftName;
                LastError = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException || exception is BadImageFormatException)
            {
                Dispose(); return Fail(exception.Message);
            }
        }

        public bool SetMassConfiguration(JSBSimNativeMassConfiguration mass)
        {
            if (!RequireInitialized()) return false;
            mass.ClampNonnegative();
            NativeMassConfiguration native = new NativeMassConfiguration
            {
                pilotPounds=mass.pilotPounds, copilotPounds=mass.copilotPounds,
                rearLeftPounds=mass.rearLeftPounds, rearRightPounds=mass.rearRightPounds,
                baggagePounds=mass.baggagePounds, leftFuelPounds=mass.leftFuelPounds,
                rightFuelPounds=mass.rightFuelPounds
            };
            if (NativeMethods.SetMassConfiguration(_instance, ref native) == 0) return Fail(ReadLastError(_instance, "Could not configure mass and fuel."));
            ConfiguredMass = mass;
            return true;
        }

        public bool Reset(JSBSimNativeInitialConditions initial)
        {
            // New flight: rebuild the native model within the same authority handle,
            // then restore the explicitly configured loading. This clears all actuator,
            // filter and propulsion histories without depending on RunIC to reset them.
            if (!RequireInitialized()) return false;
            JSBSimNativeMassConfiguration configuredMass = ConfiguredMass;
            if (NativeMethods.LoadAircraft(_instance, _dataRoot, _aircraftName) == 0)
            { string error = ReadLastError(_instance, "Could not reload native aircraft for a new flight."); Dispose(); return Fail(error); }
            if (!SetMassConfiguration(configuredMass)) { Dispose(); return false; }
            return Relocate(initial);
        }

        /// <summary>Recovery relocation preserves the native model, fuel and actuator histories.</summary>
        public bool Relocate(JSBSimNativeInitialConditions initial)
        {
            if (!RequireInitialized()) return false;
            ConfiguredInitialConditions = initial;
            SetControls(InitialControls(initial.engineRunning != 0));
            NativeInitialConditions native = new NativeInitialConditions
            {
                latitudeDegrees=initial.latitudeDegrees, longitudeDegrees=initial.longitudeDegrees,
                altitudeMslFeet=initial.altitudeMslFeet, terrainElevationMslFeet=initial.terrainElevationMslFeet,
                calibratedAirspeedKnots=initial.calibratedAirspeedKnots, headingDegrees=initial.headingDegrees,
                pitchDegrees=initial.pitchDegrees, bankDegrees=initial.bankDegrees,
                flightPathAngleDegrees=initial.flightPathAngleDegrees, engineRunning=initial.engineRunning
            };
            if (NativeMethods.SetAtmosphere(_instance, ref _atmosphere) == 0 ||
                NativeMethods.Reset(_instance, ref native) == 0 ||
                NativeMethods.SetControls(_instance, ref _controls) == 0 ||
                NativeMethods.SetAtmosphere(_instance, ref _atmosphere) == 0)
                return Fail(ReadLastError(_instance, "JSBSim reset failed."));
            return CaptureStateAndDiagnostics();
        }

        public void SetControls(JSBSimNativeControls controls)
        {
            AppliedControls = new JSBSimNativeControls
            {
                aileron=Clamp(controls.aileron,-1,1), elevatorNoseUp=Clamp(controls.elevatorNoseUp,-1,1),
                rudderYawRight=Clamp(controls.rudderYawRight,-1,1), throttle=Clamp(controls.throttle,0,1),
                mixture=Clamp(controls.mixture,0,1), carbHeat=Clamp(controls.carbHeat,0,1),
                elevatorTrimNoseUp=Clamp(controls.elevatorTrimNoseUp,-1,1), flaps=Clamp(controls.flaps,0,1),
                leftBrake=Clamp(controls.leftBrake,0,1), rightBrake=Clamp(controls.rightBrake,0,1)
            };
            _controls = new NativeControls
            {
                aileron=AppliedControls.aileron, elevator=-AppliedControls.elevatorNoseUp,
                rudder=-AppliedControls.rudderYawRight, throttle=AppliedControls.throttle,
                mixture=AppliedControls.mixture, carbHeat=AppliedControls.carbHeat,
                elevatorTrim=-AppliedControls.elevatorTrimNoseUp, flaps=AppliedControls.flaps,
                leftBrake=AppliedControls.leftBrake, rightBrake=AppliedControls.rightBrake
            };
        }

        public void SetAtmosphere(JSBSimNativeAtmosphere atmosphere)
        {
            AppliedAtmosphere = atmosphere;
            _atmosphere = new NativeAtmosphere { windNorthFeetPerSecond=atmosphere.windNorthFeetPerSecond, windEastFeetPerSecond=atmosphere.windEastFeetPerSecond, windDownFeetPerSecond=atmosphere.windDownFeetPerSecond };
        }

        public bool SetTerrainElevation(double terrainElevationMslFeet)
        {
            if (!RequireInitialized()) return false;
            return NativeMethods.SetTerrainElevation(_instance, terrainElevationMslFeet) != 0 || Fail(ReadLastError(_instance, "Could not update terrain elevation."));
        }

        public bool Trim(JSBSimNativeTrimRequest request, out JSBSimNativeTrimResult result)
        {
            result = default;
            if (!RequireInitialized()) return false;
            NativeTrimRequest nativeRequest = new NativeTrimRequest { mode=(int)request.mode, maxCycles=request.maxCycles, maxCyclesPerAxis=request.maxCyclesPerAxis, tolerance=request.tolerance };
            if (NativeMethods.Trim(_instance, ref nativeRequest, out NativeTrimResult native) == 0) return Fail(ReadLastError(_instance, "JSBSim trim failed."));
            result = new JSBSimNativeTrimResult
            {
                success=native.success, aileron=native.aileronCommand, elevatorNoseUp=-native.elevatorCommand,
                rudderYawRight=-native.rudderCommand, throttle=native.throttleCommand,
                elevatorTrimNoseUp=-native.pitchTrimCommand, angleOfAttackDegrees=native.angleOfAttackDegrees,
                pitchDegrees=native.pitchDegrees, bankDegrees=native.bankDegrees
            };
            SetControls(new JSBSimNativeControls { aileron=result.aileron, elevatorNoseUp=result.elevatorNoseUp, rudderYawRight=result.rudderYawRight, throttle=result.throttle, mixture=1.0, elevatorTrimNoseUp=result.elevatorTrimNoseUp });
            return CaptureStateAndDiagnostics();
        }

        public bool Advance(double fixedDeltaTimeSeconds)
        {
            if (!RequireInitialized() || fixedDeltaTimeSeconds <= 0.0) return Fail("Fixed timestep must be positive.");
            if (NativeMethods.SetControls(_instance, ref _controls) == 0 || NativeMethods.SetAtmosphere(_instance, ref _atmosphere) == 0 || NativeMethods.Advance(_instance, fixedDeltaTimeSeconds) == 0)
                return Fail(ReadLastError(_instance, "JSBSim fixed step failed."));
            return CaptureStateAndDiagnostics();
        }

        public void Dispose() { if (_instance != IntPtr.Zero) { NativeMethods.Destroy(_instance); _instance=IntPtr.Zero; } }

        private bool CaptureStateAndDiagnostics()
        {
            if (NativeMethods.GetState(_instance, out NativeState n) == 0 || NativeMethods.GetDiagnostics(_instance, out NativeDiagnostics d) == 0)
                return Fail(ReadLastError(_instance, "Could not read JSBSim state/diagnostics."));
            State = JSBSimNativeState.FromNative(n);
            Diagnostics = JSBSimNativeDiagnostics.FromNative(d);
            if (!State.IsFinite || !Diagnostics.IsFinite) return Fail("JSBSim returned non-finite output.");
            LastError=string.Empty; return true;
        }

        private bool RequireInitialized() => IsInitialized || Fail("Native JSBSim session is not initialized.");
        private bool Fail(string message) { LastError=string.IsNullOrWhiteSpace(message)?"Native JSBSim operation failed.":message; return false; }
        private static string ReadLastError(IntPtr instance,string fallback) { IntPtr p=NativeMethods.LastError(instance); string m=p==IntPtr.Zero?string.Empty:Marshal.PtrToStringAnsi(p); return string.IsNullOrWhiteSpace(m)?fallback:m; }
        private static double Clamp(double v,double min,double max)=>Math.Max(min,Math.Min(max,v));
        private static JSBSimNativeControls InitialControls(bool running)=>new JSBSimNativeControls{throttle=running?0.10:0,mixture=running?1:0};

        [StructLayout(LayoutKind.Sequential)] internal struct NativeInitialConditions { public double latitudeDegrees,longitudeDegrees,altitudeMslFeet,terrainElevationMslFeet,calibratedAirspeedKnots,headingDegrees,pitchDegrees,bankDegrees,flightPathAngleDegrees; public int engineRunning; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativeControls { public double aileron,elevator,rudder,throttle,mixture,carbHeat,elevatorTrim,flaps,leftBrake,rightBrake; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativeAtmosphere { public double windNorthFeetPerSecond,windEastFeetPerSecond,windDownFeetPerSecond; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativeMassConfiguration { public double pilotPounds,copilotPounds,rearLeftPounds,rearRightPounds,baggagePounds,leftFuelPounds,rightFuelPounds; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativeTrimRequest { public int mode,maxCycles,maxCyclesPerAxis; public double tolerance; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativeTrimResult { public int success; public double aileronCommand,elevatorCommand,rudderCommand,throttleCommand,pitchTrimCommand,angleOfAttackDegrees,pitchDegrees,bankDegrees; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativeState { public double simulationTimeSeconds,latitudeDegrees,longitudeDegrees,altitudeMslFeet,altitudeAglFeet,calibratedAirspeedKnots,groundSpeedKnots,verticalSpeedFeetPerMinute,headingDegrees,pitchDegrees,bankDegrees,angleOfAttackDegrees,sideslipDegrees,velocityNorthFeetPerSecond,velocityEastFeetPerSecond,velocityDownFeetPerSecond,rollRateRadiansPerSecond,pitchRateRadiansPerSecond,yawRateRadiansPerSecond,engineRpm,loadFactorG; public int weightOnWheels; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativeDiagnostics
        {
            public double trueAirspeedKnots,equivalentAirspeedKnots,densitySlugsPerCubicFoot,terrainElevationMslFeet,weightPounds,cgXInches,cgYInches,cgZInches,leftFuelPounds,rightFuelPounds;
            public double leftAileronPosition,rightAileronPosition,elevatorPosition,rudderPosition,flapPosition;
            public double totalForceXPounds,totalForceYPounds,totalForceZPounds,aeroForceXPounds,aeroForceYPounds,aeroForceZPounds,propulsionForceXPounds,propulsionForceYPounds,propulsionForceZPounds,gearForceXPounds,gearForceYPounds,gearForceZPounds;
            public double totalRollMomentPoundFeet,totalPitchMomentPoundFeet,totalYawMomentPoundFeet,aeroRollMomentPoundFeet,aeroPitchMomentPoundFeet,aeroYawMomentPoundFeet,propulsionRollMomentPoundFeet,propulsionPitchMomentPoundFeet,propulsionYawMomentPoundFeet,gearRollMomentPoundFeet,gearPitchMomentPoundFeet,gearYawMomentPoundFeet;
            public double gear0CompressionFeet,gear1CompressionFeet,gear2CompressionFeet,gear0SteeringDegrees,gear1SteeringDegrees,gear2SteeringDegrees,steeringCommand;
            public int gear0WeightOnWheels,gear1WeightOnWheels,gear2WeightOnWheels,gear0SteerType,gear0Steerable;
        }

        private static class NativeMethods
        {
            private const string Library="qfl_jsbsim_native";
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_api_version")] internal static extern int ApiVersion();
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_create")] internal static extern IntPtr Create();
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_destroy")] internal static extern void Destroy(IntPtr instance);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Ansi,EntryPoint="qfl_jsbsim_load_aircraft")] internal static extern int LoadAircraft(IntPtr instance,string dataRoot,string aircraftName);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_set_mass_configuration")] internal static extern int SetMassConfiguration(IntPtr instance,ref NativeMassConfiguration mass);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_reset")] internal static extern int Reset(IntPtr instance,ref NativeInitialConditions initial);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_set_controls")] internal static extern int SetControls(IntPtr instance,ref NativeControls controls);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_set_atmosphere")] internal static extern int SetAtmosphere(IntPtr instance,ref NativeAtmosphere atmosphere);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_set_terrain_elevation")] internal static extern int SetTerrainElevation(IntPtr instance,double elevation);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_trim")] internal static extern int Trim(IntPtr instance,ref NativeTrimRequest request,out NativeTrimResult result);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_advance")] internal static extern int Advance(IntPtr instance,double dt);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_get_state")] internal static extern int GetState(IntPtr instance,out NativeState state);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_get_diagnostics")] internal static extern int GetDiagnostics(IntPtr instance,out NativeDiagnostics diagnostics);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl,EntryPoint="qfl_jsbsim_last_error")] internal static extern IntPtr LastError(IntPtr instance);
        }
    }

    public enum JSBSimNativeTrimMode { Longitudinal=0, Full=1 }
    [StructLayout(LayoutKind.Sequential)] public struct JSBSimNativeInitialConditions { public double latitudeDegrees,longitudeDegrees,altitudeMslFeet,terrainElevationMslFeet,calibratedAirspeedKnots,headingDegrees,pitchDegrees,bankDegrees,flightPathAngleDegrees; public int engineRunning; }
    [StructLayout(LayoutKind.Sequential)] public struct JSBSimNativeControls { public double aileron,elevatorNoseUp,rudderYawRight,throttle,mixture,carbHeat,elevatorTrimNoseUp,flaps,leftBrake,rightBrake; }
    [StructLayout(LayoutKind.Sequential)] public struct JSBSimNativeAtmosphere { public double windNorthFeetPerSecond,windEastFeetPerSecond,windDownFeetPerSecond; }
    [StructLayout(LayoutKind.Sequential)] public struct JSBSimNativeMassConfiguration
    {
        public double pilotPounds,copilotPounds,rearLeftPounds,rearRightPounds,baggagePounds,leftFuelPounds,rightFuelPounds;
        public double TotalPayloadAndFuelPounds=>pilotPounds+copilotPounds+rearLeftPounds+rearRightPounds+baggagePounds+leftFuelPounds+rightFuelPounds;
        internal void ClampNonnegative(){pilotPounds=Math.Max(0,pilotPounds);copilotPounds=Math.Max(0,copilotPounds);rearLeftPounds=Math.Max(0,rearLeftPounds);rearRightPounds=Math.Max(0,rearRightPounds);baggagePounds=Math.Max(0,baggagePounds);leftFuelPounds=Math.Max(0,leftFuelPounds);rightFuelPounds=Math.Max(0,rightFuelPounds);}
    }
    [StructLayout(LayoutKind.Sequential)] public struct JSBSimNativeTrimRequest
    {
        public JSBSimNativeTrimMode mode;
        public int maxCycles,maxCyclesPerAxis;
        public double tolerance;
        public static JSBSimNativeTrimRequest StandardLongitudinal=>new JSBSimNativeTrimRequest{mode=JSBSimNativeTrimMode.Longitudinal,maxCycles=60,maxCyclesPerAxis=100,tolerance=0.001};
        public static JSBSimNativeTrimRequest StandardFull=>new JSBSimNativeTrimRequest{mode=JSBSimNativeTrimMode.Full,maxCycles=60,maxCyclesPerAxis=100,tolerance=0.001};
    }
    [StructLayout(LayoutKind.Sequential)] public struct JSBSimNativeTrimResult { public int success; public double aileron,elevatorNoseUp,rudderYawRight,throttle,elevatorTrimNoseUp,angleOfAttackDegrees,pitchDegrees,bankDegrees; }
    [StructLayout(LayoutKind.Sequential)] public struct JSBSimNativeState
    {
        public double simulationTimeSeconds,latitudeDegrees,longitudeDegrees,altitudeMslFeet,altitudeAglFeet,calibratedAirspeedKnots,groundSpeedKnots,verticalSpeedFeetPerMinute,headingDegrees,pitchDegrees,bankDegrees,angleOfAttackDegrees,sideslipDegrees,velocityNorthFeetPerSecond,velocityEastFeetPerSecond,velocityDownFeetPerSecond,rollRateRadiansPerSecond,pitchRateRadiansPerSecond,yawRateRadiansPerSecond,engineRpm,loadFactorG; public int weightOnWheels;
        public bool IsFinite=>F(simulationTimeSeconds)&&F(latitudeDegrees)&&F(longitudeDegrees)&&F(altitudeMslFeet)&&F(altitudeAglFeet)&&F(calibratedAirspeedKnots)&&F(groundSpeedKnots)&&F(verticalSpeedFeetPerMinute)&&F(headingDegrees)&&F(pitchDegrees)&&F(bankDegrees)&&F(angleOfAttackDegrees)&&F(sideslipDegrees)&&F(velocityNorthFeetPerSecond)&&F(velocityEastFeetPerSecond)&&F(velocityDownFeetPerSecond)&&F(rollRateRadiansPerSecond)&&F(pitchRateRadiansPerSecond)&&F(yawRateRadiansPerSecond)&&F(engineRpm)&&F(loadFactorG);
        internal static JSBSimNativeState FromNative(JSBSimNativeSession.NativeState n)=>new JSBSimNativeState{simulationTimeSeconds=n.simulationTimeSeconds,latitudeDegrees=n.latitudeDegrees,longitudeDegrees=n.longitudeDegrees,altitudeMslFeet=n.altitudeMslFeet,altitudeAglFeet=n.altitudeAglFeet,calibratedAirspeedKnots=n.calibratedAirspeedKnots,groundSpeedKnots=n.groundSpeedKnots,verticalSpeedFeetPerMinute=n.verticalSpeedFeetPerMinute,headingDegrees=n.headingDegrees,pitchDegrees=n.pitchDegrees,bankDegrees=n.bankDegrees,angleOfAttackDegrees=n.angleOfAttackDegrees,sideslipDegrees=n.sideslipDegrees,velocityNorthFeetPerSecond=n.velocityNorthFeetPerSecond,velocityEastFeetPerSecond=n.velocityEastFeetPerSecond,velocityDownFeetPerSecond=n.velocityDownFeetPerSecond,rollRateRadiansPerSecond=n.rollRateRadiansPerSecond,pitchRateRadiansPerSecond=n.pitchRateRadiansPerSecond,yawRateRadiansPerSecond=n.yawRateRadiansPerSecond,engineRpm=n.engineRpm,loadFactorG=n.loadFactorG,weightOnWheels=n.weightOnWheels};
        private static bool F(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
    }
    [StructLayout(LayoutKind.Sequential)] public struct JSBSimNativeDiagnostics
    {
        public double trueAirspeedKnots,equivalentAirspeedKnots,densitySlugsPerCubicFoot,terrainElevationMslFeet,weightPounds,cgXInches,cgYInches,cgZInches,leftFuelPounds,rightFuelPounds;
        public double leftAileronPosition,rightAileronPosition,elevatorPosition,rudderPosition,flapPosition;
        public double totalForceXPounds,totalForceYPounds,totalForceZPounds,aeroForceXPounds,aeroForceYPounds,aeroForceZPounds,propulsionForceXPounds,propulsionForceYPounds,propulsionForceZPounds,gearForceXPounds,gearForceYPounds,gearForceZPounds;
        public double totalRollMomentPoundFeet,totalPitchMomentPoundFeet,totalYawMomentPoundFeet,aeroRollMomentPoundFeet,aeroPitchMomentPoundFeet,aeroYawMomentPoundFeet,propulsionRollMomentPoundFeet,propulsionPitchMomentPoundFeet,propulsionYawMomentPoundFeet,gearRollMomentPoundFeet,gearPitchMomentPoundFeet,gearYawMomentPoundFeet;
        public double gear0CompressionFeet,gear1CompressionFeet,gear2CompressionFeet,gear0SteeringDegrees,gear1SteeringDegrees,gear2SteeringDegrees,steeringCommand; public int gear0WeightOnWheels,gear1WeightOnWheels,gear2WeightOnWheels,gear0SteerType,gear0Steerable;
        public bool IsFinite=>F(weightPounds)&&F(cgXInches)&&F(densitySlugsPerCubicFoot)&&F(totalForceXPounds)&&F(totalPitchMomentPoundFeet)&&F(terrainElevationMslFeet);
        internal static JSBSimNativeDiagnostics FromNative(JSBSimNativeSession.NativeDiagnostics n)
        {
            JSBSimNativeDiagnostics d=default;
            d.trueAirspeedKnots=n.trueAirspeedKnots;d.equivalentAirspeedKnots=n.equivalentAirspeedKnots;d.densitySlugsPerCubicFoot=n.densitySlugsPerCubicFoot;d.terrainElevationMslFeet=n.terrainElevationMslFeet;d.weightPounds=n.weightPounds;d.cgXInches=n.cgXInches;d.cgYInches=n.cgYInches;d.cgZInches=n.cgZInches;d.leftFuelPounds=n.leftFuelPounds;d.rightFuelPounds=n.rightFuelPounds;
            d.leftAileronPosition=n.leftAileronPosition;d.rightAileronPosition=n.rightAileronPosition;d.elevatorPosition=n.elevatorPosition;d.rudderPosition=n.rudderPosition;d.flapPosition=n.flapPosition;
            d.totalForceXPounds=n.totalForceXPounds;d.totalForceYPounds=n.totalForceYPounds;d.totalForceZPounds=n.totalForceZPounds;d.aeroForceXPounds=n.aeroForceXPounds;d.aeroForceYPounds=n.aeroForceYPounds;d.aeroForceZPounds=n.aeroForceZPounds;d.propulsionForceXPounds=n.propulsionForceXPounds;d.propulsionForceYPounds=n.propulsionForceYPounds;d.propulsionForceZPounds=n.propulsionForceZPounds;d.gearForceXPounds=n.gearForceXPounds;d.gearForceYPounds=n.gearForceYPounds;d.gearForceZPounds=n.gearForceZPounds;
            d.totalRollMomentPoundFeet=n.totalRollMomentPoundFeet;d.totalPitchMomentPoundFeet=n.totalPitchMomentPoundFeet;d.totalYawMomentPoundFeet=n.totalYawMomentPoundFeet;d.aeroRollMomentPoundFeet=n.aeroRollMomentPoundFeet;d.aeroPitchMomentPoundFeet=n.aeroPitchMomentPoundFeet;d.aeroYawMomentPoundFeet=n.aeroYawMomentPoundFeet;d.propulsionRollMomentPoundFeet=n.propulsionRollMomentPoundFeet;d.propulsionPitchMomentPoundFeet=n.propulsionPitchMomentPoundFeet;d.propulsionYawMomentPoundFeet=n.propulsionYawMomentPoundFeet;d.gearRollMomentPoundFeet=n.gearRollMomentPoundFeet;d.gearPitchMomentPoundFeet=n.gearPitchMomentPoundFeet;d.gearYawMomentPoundFeet=n.gearYawMomentPoundFeet;
            d.gear0CompressionFeet=n.gear0CompressionFeet;d.gear1CompressionFeet=n.gear1CompressionFeet;d.gear2CompressionFeet=n.gear2CompressionFeet;d.gear0SteeringDegrees=n.gear0SteeringDegrees;d.gear1SteeringDegrees=n.gear1SteeringDegrees;d.gear2SteeringDegrees=n.gear2SteeringDegrees;d.steeringCommand=n.steeringCommand;d.gear0WeightOnWheels=n.gear0WeightOnWheels;d.gear1WeightOnWheels=n.gear1WeightOnWheels;d.gear2WeightOnWheels=n.gear2WeightOnWheels;d.gear0SteerType=n.gear0SteerType;d.gear0Steerable=n.gear0Steerable;
            return d;
        }
        private static bool F(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
    }
}
