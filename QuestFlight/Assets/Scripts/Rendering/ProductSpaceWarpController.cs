using System;
using System.Collections.Generic;
using QuestFlight.Product;
using QuestFlightLab.Runtime;
using QuestFlightLab.Flight.Backends;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

namespace QuestFlight.Rendering
{
    [DefaultExecutionOrder(21000), DisallowMultipleComponent]
    public sealed class ProductSpaceWarpController : MonoBehaviour
    {
        [SerializeField] AircraftReferenceFrameRig rig;
        [SerializeField] ProductFlightModeCoordinator modes;
        static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>(1);
        static CaptureRequestScope captureRequest;
        public static bool Requested => captureRequest != null ? captureRequest.requested : PlayerPrefs.GetInt("QuestFlight.SpaceWarp", 0) != 0;
        public static bool Active { get; private set; }
        public static string Status { get; private set; } = "Not initialized";
        public static uint HistoryGeneration { get; private set; }
        public static event Action HistoryInvalidated;
        public static event Action<Camera> FrameCompleted;
        public static Vector3 PreviousViewPosition { get; private set; }
        int warmFrames, lastCompleted = -1;
        static int enabledAtFrame;
        bool session, unavailable, paused, focused = true, tracked, cadenceDifferenceReported;
        bool altitudeEligible;
        public const double SpaceWarpEnableAltitudeFeet = 5500;
        public const double SpaceWarpDisableAltitudeFeet = 5450;
        Transform applicationSpace;
        readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>(1);
        public static int LastMotionFrame { get; private set; } = -100;
        public static bool SubmissionObserved => Active && Time.frameCount-LastMotionFrame<=2;
        public static void RecordMotionSubmission() { LastMotionFrame=Time.frameCount; }
        void TrackingOriginChanged(XRInputSubsystem subsystem) { InvalidateHistory(); }
        void UnsubscribeOrigins()
        { foreach(var input in inputs)input.trackingOriginUpdated-=TrackingOriginChanged;inputs.Clear(); }
        void SubscribeOrigins()
        { UnsubscribeOrigins();SubsystemManager.GetSubsystems(inputs);foreach(var input in inputs)input.trackingOriginUpdated+=TrackingOriginChanged; }
        ProductFlightModeCoordinator.FlightModeState previousMode;
        public void Configure(AircraftReferenceFrameRig referenceRig, ProductFlightModeCoordinator coordinator)
        { rig = referenceRig; modes = coordinator; applicationSpace = null; }
        public static Transform ResolveApplicationSpace(Camera camera)
        {
            if (camera == null) return null;
            var cameraRig = camera.GetComponentInParent<OVRCameraRig>();
            // The authored Main Camera is BELOW CenterEyeAnchor, unlike the
            // SDK's camera-on-anchor layout. Its parent already contains head
            // tracking. Reporting that as app motion counts head motion twice.
            if (cameraRig == null || cameraRig.trackingSpace == null ||
                cameraRig.centerEyeAnchor == null ||
                !camera.transform.IsChildOf(cameraRig.centerEyeAnchor)) return null;
            return cameraRig.trackingSpace;
        }
        public static void SetRequested(bool value)
        { PlayerPrefs.SetInt("QuestFlight.SpaceWarp", value ? 1 : 0); PlayerPrefs.Save(); InvalidateHistory(); }
        // An explicit acceptance capture selects a mode without writing or
        // later overwriting the user's saved preference. Ordinary play reads
        // the preference, including a Settings change made during the capture.
        public static IDisposable OverrideForAcceptanceCapture(bool requested)
        {
            if (captureRequest != null) throw new InvalidOperationException("An acceptance quality-mode scope is already active.");
            captureRequest = new CaptureRequestScope(requested);
            InvalidateHistory();
            return captureRequest;
        }
        sealed class CaptureRequestScope : IDisposable
        {
            public readonly bool requested;
            public CaptureRequestScope(bool value) { requested = value; }
            public void Dispose()
            {
                if (captureRequest != this) return;
                captureRequest = null;
                InvalidateHistory();
            }
        }
        // Use the physical display frequency from Meta, not Unity's XR
        // application cadence, which can change when SpaceWarp is enabled.
        public static bool IsSupportedDisplayFrequency(float physicalHz)
            => !float.IsNaN(physicalHz) && Mathf.Abs(physicalHz-72f)<.1f;
        // Read native MSL altitude, not Unity Y or the legacy AircraftState's AGL.
        // Keep the preference separate from temporary eligibility. A 50 ft band
        // prevents repeated cadence changes during small altitude fluctuations.
        public static bool IsFlightAltitudeEligible(bool wasEligible, bool requested,
            bool flightMode, bool initialized, FlightDynamicsState state)
        {
            if (!requested || !flightMode || !initialized || !state.IsFinite || state.weightOnWheels)
                return false;
            double feet = state.altitudeMslMeters * FlightFrameConversions.MetersToFeet;
            return feet >= (wasEligible ? SpaceWarpDisableAltitudeFeet : SpaceWarpEnableAltitudeFeet);
        }
        public static void InvalidateHistory()
        { unchecked { HistoryGeneration++; } HistoryInvalidated?.Invoke(); }
        void OnEnable()
        {
            applicationSpace = null;
            HistoryInvalidated += ResetHistory;
            RenderPipelineManager.endCameraRendering += OnRendered;
            RenderPipelineManager.beginCameraRendering += LegacyLighting;
            ResetHistory();
        }
        void OnDisable()
        {
            SetActive(false);
            HistoryInvalidated -= ResetHistory;
            RenderPipelineManager.endCameraRendering -= OnRendered;
            RenderPipelineManager.beginCameraRendering -= LegacyLighting;
            session = false; UnsubscribeOrigins();
        }
        void ResetHistory()
        {
            altitudeEligible=false;
            unavailable=false;
            cadenceDifferenceReported=false;
            warmFrames = 0; lastCompleted = -1;
            if (rig != null && rig.TrackedCamera != null)
            {
                PreviousViewPosition = rig.TrackedCamera.transform.position;
                XRSpaceWarpFrameData.RequestHistoryReset(rig.TrackedCamera);
            }
            SetActive(false);
        }
        void OnApplicationPause(bool value) { paused=value; InvalidateHistory(); }
        void OnApplicationFocus(bool value) { focused=value; InvalidateHistory(); }
        void LateUpdate()
        {
            Shader.SetGlobalFloat("_QFResetMotion",warmFrames < 2 ? 1 : 0);
            if (rig == null || modes == null || rig.TrackedCamera == null) { Status = "Missing authored references"; SetActive(false); return; }
            rig.TrackedCamera.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            if (previousMode != modes.State) { previousMode = modes.State; InvalidateHistory(); }
            displays.Clear(); SubsystemManager.GetSubsystems(displays);
            bool running = displays.Count > 0 && displays[0].running;
            if (running != session) { session = running; unavailable = false; if(running)SubscribeOrigins();else UnsubscribeOrigins(); InvalidateHistory(); }
            if (!running) { Status = "XR session unavailable"; SetActive(false); return; }
            var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool trackingValid=head.isValid && head.TryGetFeatureValue(CommonUsages.isTracked,out bool tracking) && tracking;
            if(trackingValid!=tracked){tracked=trackingValid;InvalidateHistory();}
            if(!focused || !tracked || paused){Status="Waiting for tracking and focus";SetActive(false);return;}
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!OpenXRRuntime.IsExtensionEnabled("XR_FB_space_warp")) { Status = "SpaceWarp unavailable"; SetActive(false); return; }
            if (unavailable) return;
            try
            {
                // Include aircraft presentation, calibration and recentering,
                // but exclude the tracked eye anchor. Never write camera pose.
                if (applicationSpace == null) applicationSpace = ResolveApplicationSpace(rig.TrackedCamera);
                if (applicationSpace == null)
                    throw new InvalidOperationException("Authored camera tracking space is unavailable.");
                Transform appSpace = applicationSpace;
                Vector3 scale = appSpace.lossyScale;
                if (Mathf.Abs(scale.x-1)>.0001f || Mathf.Abs(scale.y-1)>.0001f || Mathf.Abs(scale.z-1)>.0001f)
                    throw new InvalidOperationException("Application space must have unit scale.");
                Vector3 p = appSpace.position; Quaternion q = appSpace.rotation;
                Meta.XR.MetaXRSpaceWarp.SetAppSpacePosition(p.x,p.y,p.z);
                Meta.XR.MetaXRSpaceWarp.SetAppSpaceRotation(q.x,q.y,q.z,q.w);
                float physicalHz = OVRPlugin.systemDisplayFrequency;
                if (!IsSupportedDisplayFrequency(physicalHz)) OVRPlugin.systemDisplayFrequency = 72f;
                bool frequencyValid = IsSupportedDisplayFrequency(physicalHz);
                if(Active && !cadenceDifferenceReported && displays[0].TryGetDisplayRefreshRate(out float applicationHz) && !IsSupportedDisplayFrequency(applicationHz))
                {
                    cadenceDifferenceReported=true;
                    Debug.Log($"[QuestFlight][SpaceWarpGate] Physical display={physicalHz:F2}Hz, XR reported cadence={applicationHz:F2}Hz. Keeping physical display as activation authority.");
                }
                bool flying = modes.State == ProductFlightModeCoordinator.FlightModeState.FreeFlight ||
                    modes.State == ProductFlightModeCoordinator.FlightModeState.Demo || modes.State == ProductFlightModeCoordinator.FlightModeState.Training;
                var flight = modes.AuthoredFlightCoordinator;
                var flightState = flight != null ? flight.CurrentState : default;
                bool flightValid = flight != null && flight.isActiveAndEnabled && flight.IsInitialized && flightState.IsFinite;
                altitudeEligible = IsFlightAltitudeEligible(altitudeEligible, Requested, flying, flightValid, flightState);
                if(Active && altitudeEligible && Time.frameCount-enabledAtFrame>8 && !SubmissionObserved)
                    throw new InvalidOperationException("XR provider did not supply motion/depth targets; rendering normally.");
                bool enable=Requested && flying && altitudeEligible && !paused && Time.timeScale > 0 && warmFrames >= 2 && frequencyValid;
                if(enable!=Active)
                {
                    bool cadenceAvailable=displays[0].TryGetDisplayRefreshRate(out float reportedHz);
                    Debug.Log($"[QuestFlight][SpaceWarpGate] enable={enable} physicalHz={physicalHz:F2} xrReportedHz={(cadenceAvailable?reportedHz:0):F2} requested={Requested} flying={flying} altitudeMslFt={flightState.altitudeMslMeters*FlightFrameConversions.MetersToFeet:F1} onGround={flightState.weightOnWheels} altitudeEligible={altitudeEligible} timeScale={Time.timeScale} warmFrames={warmFrames}");
                }
                if(enable && !Active)XRSpaceWarpFrameData.RequestHistoryReset(rig.TrackedCamera);
                SetActive(enable);
                Status = Active ? (SubmissionObserved ? "Submitting · target 36 / 72 Hz" : "Awaiting motion targets") : !Requested ? "Off" : !flying || Time.timeScale == 0 ? "Ready · flight only" : !flightValid ? "Waiting for valid flight state" : flightState.weightOnWheels ? "Standby · on ground" : !altitudeEligible ? "Standby · below 5,500 ft MSL" : "Preparing frame history";
            }
            catch (Exception e) { unavailable = true; SetActive(false); Status = "Unavailable · rendering normally"; Debug.LogError("[QuestFlight][SpaceWarp] " + e.Message); }
#else
            Status = "Quest hardware required";
#endif
        }
        static void SetActive(bool enabled)
        {
            if (Active == enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { Meta.XR.MetaXRSpaceWarp.SetSpaceWarp(enabled); }
            catch(Exception error)
            {
                Active=false;
                if(enabled)throw;
                Debug.LogWarning("[QuestFlight][SpaceWarp] Disable unavailable after session loss: "+error.Message);
                return;
            }
#endif
            Active = enabled;
            if(enabled){enabledAtFrame=Time.frameCount;Shader.SetGlobalFloat("_QFResetMotion",1);}
            Debug.Log($"[QuestFlight][SpaceWarp] active={enabled} history={HistoryGeneration}");
        }
        void OnRendered(ScriptableRenderContext context, Camera camera)
        {
            if (rig == null || camera != rig.TrackedCamera || lastCompleted == Time.frameCount) return;
            lastCompleted = Time.frameCount; warmFrames = Mathf.Min(2, warmFrames + 1);
            FrameCompleted?.Invoke(camera);
            PreviousViewPosition = camera.transform.position;
        }
        static void LegacyLighting(ScriptableRenderContext context, Camera camera)
        { ApplyLegacyLighting(); }
        public static void ApplyLegacyLighting()
        {
            Light sun = RenderSettings.sun;
            Shader.SetGlobalVector("_WorldSpaceLightPos0", sun != null ? (Vector4)(-sun.transform.forward) : new Vector4(0,1,0,0));
            Shader.SetGlobalColor("_LightColor0", sun != null ? sun.color.linear * sun.intensity : Color.black);
            Shader.SetGlobalVector("_QFPreviousGroveViewPosition", PreviousViewPosition);
        }
    }
}
