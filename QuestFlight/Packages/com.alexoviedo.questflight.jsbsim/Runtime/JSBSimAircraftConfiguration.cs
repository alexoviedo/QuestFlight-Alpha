using System;
using System.Runtime.InteropServices;

namespace AlexOviedo.QuestFlight.JSBSim
{
    /// <summary>Package DTO. No dependency on product scene or Unity hierarchy.</summary>
    [Serializable]
    public sealed class JSBSimAircraftConfiguration
    {
        public string modelName;
        public int engineCount, tankCount;
        public bool legacyC172Loading;
        public JSBSimNativeStationLoad[] stations = Array.Empty<JSBSimNativeStationLoad>();
        public JSBSimNativeStationLoad[] tanks = Array.Empty<JSBSimNativeStationLoad>();
        public static JSBSimAircraftConfiguration C172 => new JSBSimAircraftConfiguration
        { modelName = "c172x", engineCount = 1, tankCount = 2, legacyC172Loading = true };

        public bool IsValid(out string error)
        {
            error = "Invalid aircraft configuration or loading.";
            if (string.IsNullOrWhiteSpace(modelName) || modelName.IndexOfAny(new[] {'/', '\\', ':'}) >= 0 ||
                modelName.Contains("..") || engineCount < 0 || tankCount < 0 || tankCount > 4 ||
                stations == null || tanks == null || stations.Length > 8 || tanks.Length > tankCount ||
                (legacyC172Loading && (modelName != "c172x" || engineCount != 1 || tankCount != 2))) return false;
            foreach (var station in stations) if (!ValidLoad(station, 8)) return false;
            foreach (var tank in tanks) if (!ValidLoad(tank, tankCount)) return false;
            error = string.Empty; return true;
        }
        private static bool ValidLoad(JSBSimNativeStationLoad value, int count) =>
            value.index >= 0 && value.index < count && value.pounds >= 0 && value.pounds <= 10000 &&
            !double.IsNaN(value.pounds) && !double.IsInfinity(value.pounds);
        internal JSBSimAircraftConfiguration Copy() => new JSBSimAircraftConfiguration
        { modelName = modelName, engineCount = engineCount, tankCount = tankCount, legacyC172Loading = legacyC172Loading,
          stations = (JSBSimNativeStationLoad[])stations.Clone(), tanks = (JSBSimNativeStationLoad[])tanks.Clone() };
    }

    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeStationLoad { public int index; public double pounds; }
    [StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeCapabilities
    {
        public int engineCount, tankCount, stationCount, contactCount;
        public int hasAirbrake, hasTowForce, hasAirflowForce, hasAirflowMoment;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeGliderControls { public double airbrake, wheelBrake; }
    [StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeExternalLoad { public int channel; public double bodyXSi, bodyYSi, bodyZSi; }
    [StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeConditions { public double seaLevelPressurePa, isaTemperatureOffsetKelvin; }
    [StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeAirData
    {
        public double densityKgM3, pressurePa, temperatureKelvin, trueAirspeedMps, equivalentAirspeedMps,
            calibratedAirspeedMps, pressureAltitudeM, airbrakePosition;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeAttachmentState
    {
        public double positionEcefXM, positionEcefYM, positionEcefZM;
        public double velocityEcefXMps, velocityEcefYMps, velocityEcefZMps;
        public double bodyOffsetXM, bodyOffsetYM, bodyOffsetZM;
        public double bodyToEcef11, bodyToEcef12, bodyToEcef13;
        public double bodyToEcef21, bodyToEcef22, bodyToEcef23;
        public double bodyToEcef31, bodyToEcef32, bodyToEcef33;
        public JSBSimNativeExternalLoad EcefForceToBody(double xN, double yN, double zN) => new JSBSimNativeExternalLoad
        {
            channel = 0,
            bodyXSi = bodyToEcef11*xN + bodyToEcef21*yN + bodyToEcef31*zN,
            bodyYSi = bodyToEcef12*xN + bodyToEcef22*yN + bodyToEcef32*zN,
            bodyZSi = bodyToEcef13*xN + bodyToEcef23*yN + bodyToEcef33*zN
        };
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeContact
    {
        public double compressionFt, forceBodyXN, forceBodyYN, forceBodyZN;
        public int weightOnWheels, steerType;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct JSBSimNativeTerrainPatch
    {
        public double minimumLatitudeDegrees, minimumLongitudeDegrees, latitudeStepDegrees, longitudeStepDegrees;
        public int rows, columns;
    }

    public sealed partial class JSBSimNativeSession
    {
        private JSBSimAircraftConfiguration _configuration;
        private JSBSimNativeGliderControls _gliderControls;
        private JSBSimNativeConditions _conditions;
        private bool _conditionsConfigured;
        private JSBSimNativeTerrainPatch _terrainPatch;
        private double[] _terrainHeights;
        public JSBSimNativeCapabilities Capabilities { get; private set; }
        public JSBSimNativeAirData AirData { get; private set; }

        private bool ApplyConfiguredLoading()
        {
            if (_configuration.legacyC172Loading) return SetMassConfiguration(ConfiguredMass);
            return SoaringMethods.Loading(_instance, _configuration.stations, _configuration.stations.Length,
                _configuration.tanks, _configuration.tanks.Length) != 0 || Fail(ReadLastError(_instance, "Native loading failed."));
        }
        public bool SetGliderControls(JSBSimNativeGliderControls controls)
        {
            if (!RequireInitialized()) return false;
            if (SoaringMethods.GliderControls(_instance, ref controls) == 0) return Fail(ReadLastError(_instance, "Glider controls failed."));
            _gliderControls = controls; return true;
        }
        public bool SetExternalLoad(JSBSimNativeExternalLoad load) => RequireInitialized() &&
            (SoaringMethods.ExternalLoad(_instance, ref load) != 0 || Fail(ReadLastError(_instance, "External load failed.")));
        public bool ClearExternalLoads() => RequireInitialized() &&
            (SoaringMethods.ClearLoads(_instance) != 0 || Fail(ReadLastError(_instance, "Could not clear external loads.")));
        public bool GetAttachment(out JSBSimNativeAttachmentState attachment)
        {
            attachment = default;
            return RequireInitialized() && (SoaringMethods.Attachment(_instance, 0, out attachment) != 0 ||
                Fail(ReadLastError(_instance, "Attachment unavailable.")));
        }
        public bool GetContact(int index, out JSBSimNativeContact contact)
        {
            contact = default;
            return RequireInitialized() && (SoaringMethods.Contact(_instance, index, out contact) != 0 ||
                Fail(ReadLastError(_instance, "Contact unavailable.")));
        }
        public bool SetConditions(JSBSimNativeConditions conditions)
        {
            if (!RequireInitialized() || SoaringMethods.Conditions(_instance, ref conditions) == 0)
                return Fail(ReadLastError(_instance, "Atmosphere conditions failed."));
            _conditions = conditions; _conditionsConfigured = true; return true;
        }
        public bool CaptureAirData()
        {
            if (!RequireInitialized() || SoaringMethods.AirData(_instance, out JSBSimNativeAirData value) == 0)
                return Fail(ReadLastError(_instance, "Air data unavailable."));
            AirData = value; return true;
        }
        public bool SetTerrainPatch(JSBSimNativeTerrainPatch patch, double[] heightsMslMeters)
        {
            if (!RequireInitialized() || heightsMslMeters == null) return Fail("Prepared terrain data is required.");
            if (SoaringMethods.Terrain(_instance, ref patch, heightsMslMeters, heightsMslMeters.Length) == 0)
                return Fail(ReadLastError(_instance, "Prepared terrain patch rejected."));
            // This bounded array is owned by the sole preparation/simulation owner.
            // Native copies it synchronously. Retain it for fresh-model reset only.
            _terrainPatch = patch; _terrainHeights = heightsMslMeters; return true;
        }
        private static class SoaringMethods
        {
            private const string Library = "qfl_jsbsim_native";
            [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="qfl_jsbsim_get_capabilities_v3")]
            internal static extern int Capabilities(IntPtr h, out JSBSimNativeCapabilities value);
            [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="qfl_jsbsim_set_loading_v3")]
            internal static extern int Loading(IntPtr h, [In] JSBSimNativeStationLoad[] stations, int stationCount, [In] JSBSimNativeStationLoad[] tanks, int tankCount);
            [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="qfl_jsbsim_set_glider_controls_v3")]
            internal static extern int GliderControls(IntPtr h, ref JSBSimNativeGliderControls value);
            [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="qfl_jsbsim_set_external_load_v3")]
            internal static extern int ExternalLoad(IntPtr h, ref JSBSimNativeExternalLoad value);
            [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="qfl_jsbsim_clear_external_loads_v3")]
            internal static extern int ClearLoads(IntPtr h);
            [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="qfl_jsbsim_get_attachment_state_v3")]
            internal static extern int Attachment(IntPtr h, int id, out JSBSimNativeAttachmentState value);
            [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="qfl_jsbsim_set_conditions_v3")]
            internal static extern int Conditions(IntPtr h, ref JSBSimNativeConditions value);
            [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="qfl_jsbsim_get_air_data_v3")]
            internal static extern int AirData(IntPtr h, out JSBSimNativeAirData value);
            [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="qfl_jsbsim_get_contact_v3")]
            internal static extern int Contact(IntPtr h, int id, out JSBSimNativeContact value);
            [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="qfl_jsbsim_set_terrain_patch_v3")]
            internal static extern int Terrain(IntPtr h, ref JSBSimNativeTerrainPatch patch, [In] double[] heights, int count);
        }
    }
}
