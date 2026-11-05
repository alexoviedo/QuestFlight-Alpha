using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Aircraft
{
    [Serializable]
    public sealed class ProductionAircraftLightingReport
    {
        public int rendererCount;
        public int reflectionAwareRendererCount;
        public int realtimeShadowCasterCountDisabled;
        public int realtimeShadowReceiverCountDisabled;
        public int boundedCockpitShadowCasterCount;
        public int boundedCockpitShadowReceiverCount;
        public int remainingRealtimeShadowCasterCount;
        public int remainingRealtimeShadowReceiverCount;
        public int cockpitMaterialCloneCount;
        public int cockpitStableShaderMaterialCount;
        public int cockpitOcclusionMaterialCountTuned;
        public int cockpitNormalMaterialCountTuned;
        public int readableInstrumentMaterialCount;
        public float staticDepthStrength;
        public float cockpitOcclusionStrength;
        public float coarseNormalStrength;
        public float instrumentReadabilityEmission;
        public float directionalCabinShadowStrength;
        public bool cockpitLocalShadowEnabled;
        public int cockpitLocalShadowDrawCount;
        public int cockpitLocalShadowProxyTriangleCount;
        public int cockpitLocalShadowMapResolution;
        public float cockpitLocalShadowStrength;
        public string strategy;
    }

    /// <summary>
    /// Applies a deterministic Quest cockpit-lighting treatment to authored renderers.
    /// Authored AO, softened coarse cabin normals, static reflections, and base-texture
    /// instrument emission preserve stable depth/readability. One explicitly authored,
    /// aircraft-local orthographic camera renders one low-poly cabin-frame proxy into a fixed
    /// shadow map. Both XR eyes sample that same world-locked map; the OpenXR camera is never
    /// read, written, or used to orient the shadow. No light, mesh, camera, or hierarchy is
    /// constructed at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(21000)]
    public sealed class ProductionAircraftLightingController : MonoBehaviour
    {
        public const string StableCockpitShaderName =
            "QuestFlight/Photoreal Aerial Surface V1/Cockpit Stable Interior";
        public const float DefaultStaticDepthStrength = 0.38f;
        public const float MaximumStaticDepthStrength = 0.42f;
        public const float DefaultCockpitOcclusionStrength = 1.0f;
        public const float DefaultCoarseNormalStrength = 0.42f;
        public const float DefaultInstrumentReadabilityEmission = 0.20f;
        public const float DefaultDirectionalCabinShadowStrength = 0.58f;
        public const float MaximumDirectionalCabinShadowStrength = 0.65f;
        public const float DefaultFootwellEnclosureStrength = 0.70f;
        public const float DefaultCockpitLocalShadowStrength = 0.62f;
        public const float MaximumCockpitLocalShadowStrength = 0.72f;
        public const float DefaultCockpitLocalShadowBias = 0.0018f;

        private const string StableMaterialSuffix = " Quest Cockpit Stable";
        private static readonly int WorldToAircraftId = Shader.PropertyToID("_WorldToAircraft");
        private static readonly int DirectionalCabinShadowStrengthId =
            Shader.PropertyToID("_DirectionalCabinShadowStrength");
        private static readonly int FootwellEnclosureStrengthId =
            Shader.PropertyToID("_FootwellEnclosureStrength");
        private static readonly int CockpitLocalShadowMapId =
            Shader.PropertyToID("_CockpitLocalShadowMap");
        private static readonly int CockpitLocalShadowWorldToCameraId =
            Shader.PropertyToID("_CockpitLocalShadowWorldToCamera");
        private static readonly int CockpitLocalShadowWorldToClipId =
            Shader.PropertyToID("_CockpitLocalShadowWorldToClip");
        private static readonly int CockpitLocalShadowDepthParamsId =
            Shader.PropertyToID("_CockpitLocalShadowDepthParams");
        private static readonly int CockpitLocalShadowStrengthId =
            Shader.PropertyToID("_CockpitLocalShadowStrength");
        private static readonly int CockpitLocalShadowEnabledId =
            Shader.PropertyToID("_CockpitLocalShadowEnabled");

        [SerializeField] private GameObject importedAircraftRoot;
        [SerializeField] private Shader stableCockpitShader;
        [SerializeField] private Texture2D instrumentDisplayBaseColor;
        [SerializeField] private Texture2D instrumentDisplayEmission;
        [Header("Authored cockpit-local shadow")]
        [SerializeField] private Camera cockpitLocalShadowCamera;
        [SerializeField] private MeshRenderer cockpitLocalShadowCasterProxy;
        [SerializeField] private RenderTexture cockpitLocalShadowMap;
        [SerializeField] private Shader cockpitLocalShadowDepthShader;
        [SerializeField] private Vector3 cockpitLocalShadowCenter = new Vector3(0f, 0.92f, 0.04f);
        [SerializeField, Min(0.5f)] private float cockpitLocalShadowCameraDistance = 2.65f;
        [SerializeField, Range(0f, MaximumCockpitLocalShadowStrength)]
        private float cockpitLocalShadowStrength = DefaultCockpitLocalShadowStrength;
        [SerializeField, Range(0.0001f, 0.008f)]
        private float cockpitLocalShadowBias = DefaultCockpitLocalShadowBias;
        [SerializeField, Range(0f, MaximumStaticDepthStrength)]
        private float staticDepthStrength = DefaultStaticDepthStrength;
        [SerializeField, Range(0f, 1f)]
        private float cockpitOcclusionStrength = DefaultCockpitOcclusionStrength;
        [SerializeField, Range(0f, 1f)]
        private float coarseNormalStrength = DefaultCoarseNormalStrength;
        [SerializeField, Range(0f, 0.5f)]
        private float instrumentReadabilityEmission = DefaultInstrumentReadabilityEmission;
        [SerializeField, Range(0f, MaximumDirectionalCabinShadowStrength)]
        private float directionalCabinShadowStrength = DefaultDirectionalCabinShadowStrength;

        public ProductionAircraftLightingReport Report { get; private set; }
        public RenderTexture CockpitLocalShadowMap => cockpitLocalShadowMap;

        private readonly List<Material> _ownedRuntimeMaterials = new List<Material>();
        private bool _cockpitLocalShadowReady;
        private bool _cockpitLocalShadowErrorReported;

        public void ConfigureAuthoredRoot(GameObject root) => importedAircraftRoot = root;

        private void Awake()
        {
            ApplyAuthoredLightingNow();
            InitializeCockpitLocalShadow();
        }

        public ProductionAircraftLightingReport ApplyAuthoredLightingNow()
        {
            if (Report != null) return Report;
            if (importedAircraftRoot == null)
            {
                Debug.LogError("[QuestFlight][AircraftLighting] Authored aircraft root is missing.", this);
                enabled = false;
                return null;
            }
            if (stableCockpitShader == null)
            {
                Debug.LogError("[QuestFlight][AircraftLighting] Authored stable cockpit shader is missing.", this);
                enabled = false;
                return null;
            }
            if (instrumentDisplayBaseColor == null || instrumentDisplayEmission == null)
            {
                Debug.LogError("[QuestFlight][AircraftLighting] Authored instrument display contract is missing.", this);
                enabled = false;
                return null;
            }

            Report = ApplyAuthoredLighting(
                importedAircraftRoot,
                stableCockpitShader,
                instrumentDisplayBaseColor,
                instrumentDisplayEmission,
                staticDepthStrength,
                cockpitOcclusionStrength,
                coarseNormalStrength,
                instrumentReadabilityEmission,
                _ownedRuntimeMaterials);
            Report.directionalCabinShadowStrength = Mathf.Clamp(
                directionalCabinShadowStrength, 0f, MaximumDirectionalCabinShadowStrength);
            PopulateCockpitLocalShadowReport();
            ApplyDirectionalCabinState();
            return Report;
        }

        private void LateUpdate()
        {
            if (Report == null) return;
            ApplyDirectionalCabinState();
            RenderCockpitLocalShadow();
        }

        /// <summary>
        /// Renders the same authored map used at runtime for deterministic Editor Visual QA.
        /// This never moves or reads the product Main Camera.
        /// </summary>
        public bool RenderCockpitLocalShadowNow()
        {
            if (Report == null) ApplyAuthoredLightingNow();
            if (!_cockpitLocalShadowReady) InitializeCockpitLocalShadow();
            if (!_cockpitLocalShadowReady) return false;
            RenderCockpitLocalShadow();
            return Shader.GetGlobalFloat(CockpitLocalShadowEnabledId) > 0.5f;
        }

        public void ClearCockpitLocalShadowNow() => ResetCockpitLocalShadowGlobals();

        public static ProductionAircraftLightingReport ApplyAuthoredLighting(
            GameObject root,
            float depthStrength,
            float occlusionStrength = DefaultCockpitOcclusionStrength,
            float normalStrength = DefaultCoarseNormalStrength,
            float instrumentEmission = DefaultInstrumentReadabilityEmission)
        {
            return ApplyAuthoredLighting(
                root,
                Shader.Find(StableCockpitShaderName),
                null,
                null,
                depthStrength,
                occlusionStrength,
                normalStrength,
                instrumentEmission,
                new List<Material>());
        }

        private static ProductionAircraftLightingReport ApplyAuthoredLighting(
            GameObject root,
            Shader stableShader,
            Texture2D displayBaseColor,
            Texture2D displayEmission,
            float depthStrength,
            float occlusionStrength,
            float normalStrength,
            float instrumentEmission,
            ICollection<Material> ownedMaterials)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (stableShader == null)
                throw new InvalidOperationException("Stable cockpit shader is unavailable.");

            ProductionAircraftLightingReport report = new ProductionAircraftLightingReport
            {
                staticDepthStrength = Mathf.Clamp(depthStrength, 0f, MaximumStaticDepthStrength),
                cockpitOcclusionStrength = Mathf.Clamp01(occlusionStrength),
                coarseNormalStrength = Mathf.Clamp01(normalStrength),
                instrumentReadabilityEmission = Mathf.Clamp(instrumentEmission, 0f, 0.5f),
                directionalCabinShadowStrength = DefaultDirectionalCabinShadowStrength,
                strategy = "stable_cockpit_materials_plus_authored_aircraft_local_shadow_map"
            };
            var cockpitClones = new Dictionary<(Material, float, bool, bool), Material>();
            var instrumentClones = new Dictionary<(Material, int), Material>();

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                report.rendererCount++;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                report.reflectionAwareRendererCount++;

                string identity = RendererIdentity(renderer);
                bool cockpit = IsCockpitRenderer(identity);
                ShadowCastingMode desiredCasting = ShadowCastingMode.Off;
                if (renderer.shadowCastingMode != desiredCasting)
                {
                    if (desiredCasting == ShadowCastingMode.Off)
                        report.realtimeShadowCasterCountDisabled++;
                    renderer.shadowCastingMode = desiredCasting;
                }
                if (renderer.receiveShadows)
                {
                    report.realtimeShadowReceiverCountDisabled++;
                    renderer.receiveShadows = false;
                }

                // Cabin glazing and the authored gauge-only lens array keep their dedicated
                // reflection shaders. Meter_01 must retain its imported glTF PBR shader:
                // that renderer also owns lower-panel artwork, so replacing its shader changes
                // switch-panel channel semantics. Instrument surfaces are cloned only to tune
                // their existing PBR/emissive inputs without altering that shader contract.
                // SGS presentation owns its authored vertex-color and canopy
                // materials. A C172 ancestor name must not retune those meshes.
                if (renderer.GetComponentInParent<QuestFlight.Product.SoaringInstrumentPresenter>(true)!=null ||
                    !cockpit || IsTransparentGlass(renderer)) continue;
                if (IsAuthoritativeInstrumentDisplaySurface(identity))
                {
                    Material[] instrumentMaterials = renderer.sharedMaterials;
                    bool instrumentChanged = false;
                    for (int index = 0; index < instrumentMaterials.Length; index++)
                    {
                        Material source = instrumentMaterials[index];
                        if (source == null) continue;
                        int instrumentProfile = identity.IndexOf("Static_TXT", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 2;
                        var instrumentKey = (source, instrumentProfile);
                        if (!instrumentClones.TryGetValue(instrumentKey, out Material tuned))
                        {
                            tuned = new Material(source)
                            {
                                name = CleanMaterialName(source.name) + " Quest Instrument Tuned"
                            };
                            TuneAuthoritativeInstrumentDisplayMaterial(
                                tuned, identity, displayBaseColor, displayEmission, report);
                            instrumentClones.Add(instrumentKey, tuned);
                            ownedMaterials.Add(tuned);
                            report.cockpitMaterialCloneCount++;
                        }
                        instrumentMaterials[index] = tuned;
                        instrumentChanged = true;
                    }
                    if (instrumentChanged) renderer.sharedMaterials = instrumentMaterials;
                    continue;
                }
                // The blank imported display substrate supplies the physical bezel/screen PBR.
                // Keep it intact beneath the dedicated opaque readout apertures.
                if (identity.IndexOf("Static_Display", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material source = materials[index];
                    if (source == null) continue;
                    var treatment = (source, CockpitDepthWeight(identity),
                        identity.IndexOf("Interior_Body", StringComparison.OrdinalIgnoreCase) >= 0,
                        IsReadableInstrumentMaterial(identity));
                    if (!cockpitClones.TryGetValue(treatment, out Material stable))
                    {
                        stable = new Material(source)
                        {
                            name = CleanMaterialName(source.name) + StableMaterialSuffix
                        };
                        stable.shader = stableShader;
                        if (stable.HasProperty("_DirectionalCabinShadowStrength"))
                            stable.SetFloat(DirectionalCabinShadowStrengthId, DefaultDirectionalCabinShadowStrength);
                        if (stable.HasProperty("_FootwellEnclosureStrength"))
                            stable.SetFloat(FootwellEnclosureStrengthId, DefaultFootwellEnclosureStrength);
                        stable.SetMatrix(WorldToAircraftId, root.transform.worldToLocalMatrix);
                        // Tune once from the immutable source, per explicit treatment.
                        // Renderer sharing/count/order must not multiply an existing tint.
                        TuneCockpitMaterial(stable, identity, report);
                        cockpitClones.Add(treatment, stable);
                        ownedMaterials.Add(stable);
                        report.cockpitMaterialCloneCount++;
                        report.cockpitStableShaderMaterialCount++;
                    }

                    materials[index] = stable;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                    report.remainingRealtimeShadowCasterCount++;
                if (renderer.receiveShadows)
                    report.remainingRealtimeShadowReceiverCount++;
            }

            Debug.Log(
                $"[QuestFlight][AircraftLighting] renderers={report.rendererCount} " +
                $"cockpitClones={report.cockpitMaterialCloneCount} stableShader={report.cockpitStableShaderMaterialCount} " +
                $"aoTuned={report.cockpitOcclusionMaterialCountTuned} " +
                $"normalsTuned={report.cockpitNormalMaterialCountTuned} readable={report.readableInstrumentMaterialCount} " +
                $"remainingCasters={report.remainingRealtimeShadowCasterCount} " +
                $"remainingReceivers={report.remainingRealtimeShadowReceiverCount} strategy={report.strategy}");
            return report;
        }

        private void ApplyDirectionalCabinState()
        {
            Matrix4x4 worldToAircraft = importedAircraftRoot.transform.worldToLocalMatrix;
            float strength = Mathf.Clamp(
                directionalCabinShadowStrength, 0f, MaximumDirectionalCabinShadowStrength);
            for (int index = 0; index < _ownedRuntimeMaterials.Count; index++)
            {
                Material material = _ownedRuntimeMaterials[index];
                if (material == null || material.shader == null ||
                    !string.Equals(material.shader.name, StableCockpitShaderName, StringComparison.Ordinal))
                    continue;
                material.SetMatrix(WorldToAircraftId, worldToAircraft);
                material.SetFloat(DirectionalCabinShadowStrengthId, strength);
            }
        }

        private void InitializeCockpitLocalShadow()
        {
            _cockpitLocalShadowReady = false;
            if (cockpitLocalShadowCamera == null ||
                cockpitLocalShadowCasterProxy == null ||
                cockpitLocalShadowMap == null ||
                cockpitLocalShadowDepthShader == null ||
                importedAircraftRoot == null)
            {
                ReportCockpitLocalShadowError(
                    "Authored camera, caster proxy, depth shader, render texture, or aircraft root is missing.");
                ResetCockpitLocalShadowGlobals();
                return;
            }

            MeshFilter meshFilter = cockpitLocalShadowCasterProxy.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null ||
                meshFilter.sharedMesh.subMeshCount != 1)
            {
                ReportCockpitLocalShadowError(
                    "The authored caster proxy must contain exactly one mesh and one submesh.");
                ResetCockpitLocalShadowGlobals();
                return;
            }
            if (meshFilter.sharedMesh.triangles.Length / 3 > 1000)
            {
                ReportCockpitLocalShadowError(
                    "The authored caster proxy exceeds the 1,000-triangle Quest ceiling.");
                ResetCockpitLocalShadowGlobals();
                return;
            }
            if (cockpitLocalShadowMap.width != cockpitLocalShadowMap.height ||
                cockpitLocalShadowMap.width > 1024)
            {
                ReportCockpitLocalShadowError(
                    "The cockpit-local shadow map must be square and no larger than 1024.");
                ResetCockpitLocalShadowGlobals();
                return;
            }

            cockpitLocalShadowCamera.enabled = false;
            cockpitLocalShadowCamera.targetTexture = cockpitLocalShadowMap;
            if(GraphicsSettings.currentRenderPipeline==null)cockpitLocalShadowCamera.stereoTargetEye = StereoTargetEyeMask.None;
            cockpitLocalShadowCamera.allowHDR = false;
            cockpitLocalShadowCamera.allowMSAA = false;
            cockpitLocalShadowCamera.useOcclusionCulling = false;
            cockpitLocalShadowCasterProxy.enabled = false;
            cockpitLocalShadowCasterProxy.shadowCastingMode = ShadowCastingMode.Off;
            cockpitLocalShadowCasterProxy.receiveShadows = false;

            if (!cockpitLocalShadowMap.IsCreated() && !cockpitLocalShadowMap.Create())
            {
                ReportCockpitLocalShadowError("The authored shadow render texture could not be created.");
                ResetCockpitLocalShadowGlobals();
                return;
            }

            _cockpitLocalShadowReady = true;
            PopulateCockpitLocalShadowReport();
        }

        private void PopulateCockpitLocalShadowReport()
        {
            if (Report == null) return;
            MeshFilter meshFilter = cockpitLocalShadowCasterProxy != null
                ? cockpitLocalShadowCasterProxy.GetComponent<MeshFilter>()
                : null;
            Report.cockpitLocalShadowEnabled = _cockpitLocalShadowReady;
            Report.cockpitLocalShadowDrawCount = _cockpitLocalShadowReady ? 1 : 0;
            Report.cockpitLocalShadowProxyTriangleCount =
                meshFilter != null && meshFilter.sharedMesh != null
                    ? meshFilter.sharedMesh.triangles.Length / 3
                    : 0;
            Report.cockpitLocalShadowMapResolution =
                cockpitLocalShadowMap != null ? cockpitLocalShadowMap.width : 0;
            Report.cockpitLocalShadowStrength = Mathf.Clamp(
                cockpitLocalShadowStrength, 0f, MaximumCockpitLocalShadowStrength);
        }

        private void RenderCockpitLocalShadow()
        {
            if (!_cockpitLocalShadowReady)
            {
                InitializeCockpitLocalShadow();
                if (!_cockpitLocalShadowReady) return;
            }

            Light sun = RenderSettings.sun;
            if (sun == null || !sun.isActiveAndEnabled || sun.type != LightType.Directional)
            {
                Shader.SetGlobalFloat(CockpitLocalShadowEnabledId, 0f);
                return;
            }

            Vector3 lightDirectionToSun = -sun.transform.forward;
            if (Vector3.Dot(lightDirectionToSun, Vector3.up) <= 0.015f)
            {
                Shader.SetGlobalFloat(CockpitLocalShadowEnabledId, 0f);
                return;
            }

            Transform aircraftTransform = importedAircraftRoot.transform;
            Vector3 centerWorld = aircraftTransform.TransformPoint(cockpitLocalShadowCenter);
            Vector3 cameraPosition =
                centerWorld + lightDirectionToSun * cockpitLocalShadowCameraDistance;
            Vector3 cameraForward = -lightDirectionToSun;
            Vector3 stableUp = aircraftTransform.up;
            if (Mathf.Abs(Vector3.Dot(cameraForward, stableUp)) > 0.94f)
                stableUp = aircraftTransform.forward;
            cockpitLocalShadowCamera.transform.SetPositionAndRotation(
                cameraPosition, Quaternion.LookRotation(cameraForward, stableUp));

            float near = cockpitLocalShadowCamera.nearClipPlane;
            float far = cockpitLocalShadowCamera.farClipPlane;
            Vector4 depthParams = new Vector4(
                near, far, 1f / Mathf.Max(far - near, 0.001f), cockpitLocalShadowBias);
            Matrix4x4 worldToCamera = cockpitLocalShadowCamera.worldToCameraMatrix;
            Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(
                cockpitLocalShadowCamera.projectionMatrix, true);

            Shader.SetGlobalVector(CockpitLocalShadowDepthParamsId, depthParams);
            cockpitLocalShadowCasterProxy.enabled = true;
            try
            {
                if (GraphicsSettings.currentRenderPipeline != null)
                {
                    QuestFlight.Rendering.ProductCockpitShadowPass.Texture = cockpitLocalShadowMap;
                    QuestFlight.Rendering.ProductCockpitShadowPass.Mesh = cockpitLocalShadowCasterProxy.GetComponent<MeshFilter>().sharedMesh;
                    QuestFlight.Rendering.ProductCockpitShadowPass.Model = cockpitLocalShadowCasterProxy.localToWorldMatrix;
                    QuestFlight.Rendering.ProductCockpitShadowPass.View = worldToCamera;
                    QuestFlight.Rendering.ProductCockpitShadowPass.Projection = gpuProjection;
                    QuestFlight.Rendering.ProductCockpitShadowPass.Shader = cockpitLocalShadowDepthShader;
                }
                else
                {
                    cockpitLocalShadowCamera.RenderWithShader(cockpitLocalShadowDepthShader, string.Empty);
                    cockpitLocalShadowMap.GenerateMips();
                }
            }
            finally
            {
                cockpitLocalShadowCasterProxy.enabled = false;
            }

            Shader.SetGlobalTexture(CockpitLocalShadowMapId, cockpitLocalShadowMap);
            Shader.SetGlobalMatrix(CockpitLocalShadowWorldToCameraId, worldToCamera);
            Shader.SetGlobalMatrix(
                CockpitLocalShadowWorldToClipId, gpuProjection * worldToCamera);
            Shader.SetGlobalFloat(
                CockpitLocalShadowStrengthId,
                Mathf.Clamp(cockpitLocalShadowStrength, 0f, MaximumCockpitLocalShadowStrength));
            Shader.SetGlobalFloat(CockpitLocalShadowEnabledId, 1f);
        }

        private void ReportCockpitLocalShadowError(string message)
        {
            if (_cockpitLocalShadowErrorReported) return;
            _cockpitLocalShadowErrorReported = true;
            Debug.LogError("[QuestFlight][AircraftLighting] Cockpit-local shadow disabled: " + message, this);
        }

        private static void ResetCockpitLocalShadowGlobals()
        {
            QuestFlight.Rendering.ProductCockpitShadowPass.Texture=null;
            QuestFlight.Rendering.ProductCockpitShadowPass.Mesh=null;
            Shader.SetGlobalFloat(CockpitLocalShadowEnabledId, 0f);
            Shader.SetGlobalFloat(CockpitLocalShadowStrengthId, 0f);
        }

        private static void TuneAuthoritativeInstrumentDisplayMaterial(
            Material material,
            string rendererIdentity,
            Texture2D displayBaseColor,
            Texture2D displayEmission,
            ProductionAircraftLightingReport report)
        {
            bool display = rendererIdentity.IndexOf("Static_Display", StringComparison.OrdinalIgnoreCase) >= 0;
            bool text = rendererIdentity.IndexOf("Static_TXT", StringComparison.OrdinalIgnoreCase) >= 0;
            bool meter = rendererIdentity.IndexOf("Meter_01", StringComparison.OrdinalIgnoreCase) >= 0;

            if (display && displayBaseColor != null && displayEmission != null)
            {
                if (material.HasProperty("baseColorTexture"))
                    material.SetTexture("baseColorTexture", displayBaseColor);
                if (material.HasProperty("emissiveTexture"))
                    material.SetTexture("emissiveTexture", displayEmission);
            }
            else if ((text || meter) && material.HasProperty("baseColorTexture") &&
                     material.HasProperty("emissiveTexture"))
            {
                Texture baseColor = material.GetTexture("baseColorTexture");
                Texture emission = material.GetTexture("emissiveTexture");
                if (emission == null && baseColor != null)
                    material.SetTexture("emissiveTexture", baseColor);
            }
            if (material.HasProperty("emissiveFactor"))
            {
                Color existing = material.GetColor("emissiveFactor");
                // Meter_01 contains the authentic dial markings and lower switch-panel art.
                // A modest emission floor makes those markings readable beneath the separate
                // additive lens pass while preserving the imported PBR material and artwork.
                float target = display ? 0.82f : text ? 0.62f : meter ? 0.24f : 0.17f;
                material.SetColor("emissiveFactor", new Color(
                    Mathf.Max(existing.r, target * 0.96f),
                    Mathf.Max(existing.g, target),
                    Mathf.Max(existing.b, target * 1.04f),
                    1f));
                material.EnableKeyword(material.shader.name=="Shader Graphs/glTF-pbrMetallicRoughness"?"_EMISSIVE":"_EMISSION");
            }
            report.readableInstrumentMaterialCount++;
        }

        private static void TuneCockpitMaterial(
            Material material,
            string rendererIdentity,
            ProductionAircraftLightingReport report)
        {
            if (material.HasProperty("occlusionTexture_strength"))
            {
                float bounded = Mathf.Max(material.GetFloat("occlusionTexture_strength"), report.cockpitOcclusionStrength);
                material.SetFloat("occlusionTexture_strength", bounded);
                report.cockpitOcclusionMaterialCountTuned++;
            }

            if (rendererIdentity.IndexOf("Interior_Body", StringComparison.OrdinalIgnoreCase) >= 0 &&
                material.HasProperty("normalTexture_scale"))
            {
                material.SetFloat("normalTexture_scale", report.coarseNormalStrength);
                report.cockpitNormalMaterialCountTuned++;
            }

            if (material.HasProperty("baseColorFactor") &&
                rendererIdentity.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) < 0)
            {
                float depthWeight = CockpitDepthWeight(rendererIdentity);
                Color baseColor = material.GetColor("baseColorFactor");
                float depth = 1f - report.staticDepthStrength * depthWeight;
                material.SetColor("baseColorFactor", new Color(
                    baseColor.r * depth, baseColor.g * depth, baseColor.b * depth, baseColor.a));
            }

            if (!IsReadableInstrumentMaterial(rendererIdentity) || !material.HasProperty("emissiveFactor")) return;
            if (material.HasProperty("baseColorTexture") && material.HasProperty("emissiveTexture"))
            {
                Texture baseColor = material.GetTexture("baseColorTexture");
                if (baseColor != null) material.SetTexture("emissiveTexture", baseColor);
            }
            Color existing = material.GetColor("emissiveFactor");
            float lift = report.instrumentReadabilityEmission;
            material.SetColor("emissiveFactor", new Color(
                Mathf.Max(existing.r, lift * 0.96f),
                Mathf.Max(existing.g, lift),
                Mathf.Max(existing.b, lift * 1.04f),
                1f));
            material.EnableKeyword(material.shader.name=="Shader Graphs/glTF-pbrMetallicRoughness"?"_EMISSIVE":"_EMISSION");
            report.readableInstrumentMaterialCount++;
        }

        private static float CockpitDepthWeight(string identity)
        {
            if (identity.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0) return 0f;
            if (identity.IndexOf("Seats", StringComparison.OrdinalIgnoreCase) >= 0) return 0.78f;
            if (identity.IndexOf("SeatGear", StringComparison.OrdinalIgnoreCase) >= 0) return 0.70f;
            if (identity.IndexOf("Interior_Body", StringComparison.OrdinalIgnoreCase) >= 0) return 0.62f;
            if (identity.IndexOf("Paddle", StringComparison.OrdinalIgnoreCase) >= 0) return 0.50f;
            return 0.24f;
        }

        private static bool IsCockpitRenderer(string identity)
        {
            return identity.IndexOf("Cessna_Interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("Static_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("Dynamic_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("GlassPartNew", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsReadableInstrumentMaterial(string identity)
        {
            return identity.IndexOf("Static_TXT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("ButtonPlate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("Buttons_MAT", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsAuthoritativeInstrumentDisplaySurface(string identity)
        {
            // Static_Display keeps its imported PBR material. Readable avionics content is
            // authored on one separate serialized readout mesh so glTF atlas transforms cannot
            // spill display pixels onto knobs or the surrounding lower panel.
            return identity.IndexOf("Static_TXT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("Meter_01", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTransparentGlass(Renderer renderer)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null) continue;
                string shader = material.shader.name;
                if (shader.IndexOf("Cockpit Window Reflection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    shader.IndexOf("Instrument Glass Reflection", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string RendererIdentity(Renderer renderer)
        {
            string identity = renderer.name;
            for (Transform current = renderer.transform.parent; current != null; current = current.parent)
                identity += " " + current.name;
            foreach (Material material in renderer.sharedMaterials)
                if (material != null) identity += " " + material.name;
            return identity;
        }

        private static string CleanMaterialName(string materialName) =>
            (materialName ?? string.Empty).Replace(" (Instance)", string.Empty);

        private void OnDisable()
        {
            ResetCockpitLocalShadowGlobals();
            if (cockpitLocalShadowCasterProxy != null)
                cockpitLocalShadowCasterProxy.enabled = false;
            if (cockpitLocalShadowCamera != null)
                cockpitLocalShadowCamera.enabled = false;
        }

        private void OnDestroy()
        {
            ResetCockpitLocalShadowGlobals();
            if (cockpitLocalShadowCasterProxy != null)
                cockpitLocalShadowCasterProxy.enabled = false;
            if (cockpitLocalShadowCamera != null)
                cockpitLocalShadowCamera.enabled = false;
            foreach (Material material in _ownedRuntimeMaterials)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            _ownedRuntimeMaterials.Clear();
        }
    }
}
