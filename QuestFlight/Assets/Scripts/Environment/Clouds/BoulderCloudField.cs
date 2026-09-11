using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Runtime atmosphere response for one explicitly authored sparse volume.
    /// The renderer ray-marches external cloud density; this component only
    /// supplies the continuous exterior/interior handoff. It never constructs
    /// hierarchy, writes the XR camera, or allocates in steady state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoulderCloudField : MonoBehaviour
    {
        [SerializeField] private BoulderCloudProfile profile;
        [SerializeField] private MeshRenderer[] sectorRenderers = System.Array.Empty<MeshRenderer>();
        [SerializeField] private Material sharedCloudMaterial;
        [SerializeField] private TextAsset sourceManifest;
        [SerializeField] private Light authoredSun;
        [SerializeField] private Transform atmosphereObserver;
        [SerializeField] private int authoredTriangleCount;
        [SerializeField] private int authoredDrawCount;
        [SerializeField] private bool alphaToCoverage = true;
        [SerializeField] private bool transparentBlending;
        [SerializeField] private bool realtimeCloudShadows;
        [SerializeField] private bool perEyeBillboarding;
        [SerializeField] private bool sparseVolumetricField;
        [SerializeField] private bool singleContinuousField;
        [SerializeField] private bool temporalReprojection;

        private Camera cachedMainCamera;
        public BoulderWindFieldProfile windField;
        public QuestFlightLab.Flight.Backends.FlightDynamicsCoordinator windAuthority;
        private Vector3 windRestPosition;
        private double previousWindTime;
        private bool windClockInitialized;
        private Color baselineFogColor;
        private float baselineFogDensity;
        private float baselineSunIntensity;
        private float baselineSkyExposure;
        private bool baselineCaptured;
        private float currentInteriorWeight;
        private static readonly int ExteriorOpacityId =
            Shader.PropertyToID("_KBDUCloudExteriorOpacity");

        public BoulderCloudProfile Profile => profile;
        public MeshRenderer[] SectorRenderers => sectorRenderers;
        public Material SharedCloudMaterial => sharedCloudMaterial;
        public TextAsset SourceManifest => sourceManifest;
        public Light AuthoredSun => authoredSun;
        public Transform AtmosphereObserver => atmosphereObserver;
        public int AuthoredTriangleCount => authoredTriangleCount;
        public int AuthoredDrawCount => authoredDrawCount;
        public bool AlphaToCoverage => alphaToCoverage;
        public bool TransparentBlending => transparentBlending;
        public bool RealtimeCloudShadows => realtimeCloudShadows;
        public bool PerEyeBillboarding => perEyeBillboarding;
        public bool SparseVolumetricField => sparseVolumetricField;
        public bool SingleContinuousField => singleContinuousField;
        public bool TemporalReprojection => temporalReprojection;
        public float CurrentInteriorWeight => currentInteriorWeight;

#if UNITY_EDITOR
        public void Configure(
            BoulderCloudProfile authoredProfile,
            MeshRenderer[] renderers,
            Material material,
            TextAsset manifest,
            Light sun,
            Transform observer)
        {
            profile = authoredProfile;
            sectorRenderers = renderers ?? System.Array.Empty<MeshRenderer>();
            sharedCloudMaterial = material;
            sourceManifest = manifest;
            authoredSun = sun;
            atmosphereObserver = observer;
            authoredDrawCount = sectorRenderers.Length;
            authoredTriangleCount = 0;
            for (int index = 0; index < sectorRenderers.Length; index++)
            {
                MeshFilter filter = sectorRenderers[index] == null
                    ? null
                    : sectorRenderers[index].GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    authoredTriangleCount += checked((int)(filter.sharedMesh.GetIndexCount(0) / 3u));
            }
            alphaToCoverage = false;
            transparentBlending = true;
            realtimeCloudShadows = false;
            perEyeBillboarding = false;
            sparseVolumetricField = true;
            singleContinuousField = true;
            temporalReprojection = false;
        }
#endif

        private void OnEnable()
        {
            windRestPosition = transform.position;
            windClockInitialized = false;
            CaptureBaseline();
            Shader.SetGlobalFloat(ExteriorOpacityId, 1f);
        }

        private void OnDisable()
        {
            RestoreBaseline();
        }

        private void LateUpdate()
        {
            if (profile == null)
                return;
            AdvanceWind();
            Transform observer = atmosphereObserver;
            if (observer == null)
            {
                if (cachedMainCamera == null)
                    cachedMainCamera = Camera.main;
                observer = cachedMainCamera == null ? null : cachedMainCamera.transform;
            }
            if (observer == null)
                return;

            float target = EvaluateInteriorWeight(observer.position);
            currentInteriorWeight = Mathf.MoveTowards(
                currentInteriorWeight,
                target,
                Time.unscaledDeltaTime * 0.85f);
            Shader.SetGlobalFloat(ExteriorOpacityId, 1f - currentInteriorWeight);
            ApplyAtmosphere(currentInteriorWeight);
        }

        private void AdvanceWind()
        {
            if (windField == null || windAuthority == null || profile.Volumes == null || profile.Volumes.Length == 0) return;
            double now = windAuthority.CurrentState.simulationTimeSeconds;
            if (!windClockInitialized || now < previousWindTime || windAuthority.WeatherPreset == BoulderWeatherPreset.Calm)
            {
                transform.position = windRestPosition;
                previousWindTime = now; windClockInitialized = true; return;
            }
            double elapsed = now - previousWindTime; previousWindTime = now;
            if (elapsed <= 0 || elapsed > 1) return;
            Vector3 samplePosition = transform.TransformPoint(profile.Volumes[0].centerLocalMeters);
            Vector3 wind = windField.Sample(samplePosition, now, windAuthority.WeatherPreset);
            // One shared advection transform keeps density, geometry, culling and interior sampling aligned.
            // No wrapping, added volumes, shader samples or rendering passes.
            transform.position += new Vector3(wind.x, 0, wind.z) * (float)elapsed;
        }

        public float EvaluateInteriorWeight(Vector3 worldPosition)
        {
            if (profile == null || profile.Volumes == null)
                return 0f;
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float strongest = 0f;
            BoulderCloudProfile.Volume[] volumes = profile.Volumes;
            for (int index = 0; index < volumes.Length; index++)
            {
                BoulderCloudProfile.Volume volume = volumes[index];
                Vector3 radii = volume.radiiMeters;
                Vector3 delta = local - volume.centerLocalMeters;
                float normalized = Mathf.Sqrt(
                    delta.x * delta.x / (radii.x * radii.x) +
                    delta.y * delta.y / (radii.y * radii.y) +
                    delta.z * delta.z / (radii.z * radii.z));
                float boundary = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.68f, 1.08f, normalized));
                strongest = Mathf.Max(strongest, boundary * volume.interiorDensity);
            }
            return Mathf.Clamp01(strongest);
        }

        /// <summary>Deterministic evidence hook; it never moves the observer or camera.</summary>
        public void ApplyAtmosphereForValidation(Vector3 worldPosition)
        {
            CaptureBaseline();
            currentInteriorWeight = EvaluateInteriorWeight(worldPosition);
            Shader.SetGlobalFloat(ExteriorOpacityId, 1f - currentInteriorWeight);
            ApplyAtmosphere(currentInteriorWeight);
        }

        public void RestoreAtmosphereForValidation()
        {
            RestoreBaseline();
        }

        private void CaptureBaseline()
        {
            if (baselineCaptured)
                return;
            baselineFogColor = RenderSettings.fogColor;
            baselineFogDensity = RenderSettings.fogDensity;
            Light sun = authoredSun != null ? authoredSun : RenderSettings.sun;
            baselineSunIntensity = sun == null ? 1f : sun.intensity;
            Material sky = RenderSettings.skybox;
            baselineSkyExposure = sky != null && sky.HasProperty("_Exposure")
                ? sky.GetFloat("_Exposure")
                : 1f;
            baselineCaptured = true;
        }

        private void ApplyAtmosphere(float weight)
        {
            if (!baselineCaptured)
                CaptureBaseline();
            RenderSettings.fogColor = Color.Lerp(
                baselineFogColor,
                profile.InteriorFogColor,
                weight);
            RenderSettings.fogDensity = Mathf.Lerp(
                baselineFogDensity,
                profile.InteriorFogDensity,
                weight);
            Light sun = authoredSun != null ? authoredSun : RenderSettings.sun;
            if (sun != null)
                sun.intensity = baselineSunIntensity * Mathf.Lerp(
                    1f,
                    profile.InteriorSunIntensityMultiplier,
                    weight);
            Material sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_Exposure"))
                sky.SetFloat("_Exposure", baselineSkyExposure * Mathf.Lerp(
                    1f,
                    profile.InteriorSkyExposureMultiplier,
                    weight));
        }

        private void RestoreBaseline()
        {
            if (!baselineCaptured)
                return;
            RenderSettings.fogColor = baselineFogColor;
            RenderSettings.fogDensity = baselineFogDensity;
            Light sun = authoredSun != null ? authoredSun : RenderSettings.sun;
            if (sun != null)
                sun.intensity = baselineSunIntensity;
            Material sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_Exposure"))
                sky.SetFloat("_Exposure", baselineSkyExposure);
            currentInteriorWeight = 0f;
            Shader.SetGlobalFloat(ExteriorOpacityId, 1f);
            baselineCaptured = false;
        }

        public bool TryValidate(out string report)
        {
            if (profile == null)
            {
                report = "profile is missing";
                return false;
            }
            if (!profile.TryValidate(out string profileReport))
            {
                report = "profile invalid: " + profileReport;
                return false;
            }
            if (sharedCloudMaterial == null || sourceManifest == null)
            {
                report = "material or source manifest is missing";
                return false;
            }
            if (sectorRenderers == null || sectorRenderers.Length != 1)
            {
                report = "cloud field must use exactly one bounded render batch";
                return false;
            }
            if (authoredDrawCount != sectorRenderers.Length || authoredTriangleCount <= 0 ||
                authoredTriangleCount > 128)
            {
                report = "authored draw/triangle budget is invalid";
                return false;
            }
            for (int index = 0; index < sectorRenderers.Length; index++)
            {
                MeshRenderer renderer = sectorRenderers[index];
                MeshFilter filter = renderer == null ? null : renderer.GetComponent<MeshFilter>();
                if (renderer == null || !renderer.enabled || filter == null || filter.sharedMesh == null ||
                    renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial != sharedCloudMaterial)
                {
                    report = $"sector {index} is not one enabled one-material mesh";
                    return false;
                }
            }
            if (alphaToCoverage || !transparentBlending || realtimeCloudShadows || perEyeBillboarding ||
                !sparseVolumetricField || !singleContinuousField || temporalReprojection)
            {
                report = "Quest cloud render policy flags are invalid";
                return false;
            }
            report = $"{profileReport}; {authoredDrawCount} draws, {authoredTriangleCount} triangles";
            return true;
        }
    }
}
