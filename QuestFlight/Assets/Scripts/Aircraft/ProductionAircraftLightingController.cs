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
        public string strategy;
    }

    /// <summary>
    /// Applies a deterministic Quest cockpit-lighting treatment to authored renderers.
    /// Authored AO, geometry-derived four-direction visibility, softened coarse cabin normals,
    /// static reflections, and base-texture instrument emission preserve stable depth/readability.
    /// Realtime cockpit casters and receivers stay disabled because worn stereo review proved
    /// the former small shadow map flickered. No light, camera, mesh, or hierarchy is created.
    /// </summary>
    [DisallowMultipleComponent]
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

        private const string StableMaterialSuffix = " Quest Cockpit Stable";
        private static readonly int WorldToAircraftId = Shader.PropertyToID("_WorldToAircraft");
        private static readonly int DirectionalCabinShadowStrengthId =
            Shader.PropertyToID("_DirectionalCabinShadowStrength");

        [SerializeField] private GameObject importedAircraftRoot;
        [SerializeField] private Shader stableCockpitShader;
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

        private readonly List<Material> _ownedRuntimeMaterials = new List<Material>();

        public void ConfigureAuthoredRoot(GameObject root) => importedAircraftRoot = root;

        private void Awake() => ApplyAuthoredLightingNow();

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

            Report = ApplyAuthoredLighting(
                importedAircraftRoot,
                stableCockpitShader,
                staticDepthStrength,
                cockpitOcclusionStrength,
                coarseNormalStrength,
                instrumentReadabilityEmission,
                _ownedRuntimeMaterials);
            Report.directionalCabinShadowStrength = Mathf.Clamp(
                directionalCabinShadowStrength, 0f, MaximumDirectionalCabinShadowStrength);
            ApplyDirectionalCabinState();
            return Report;
        }

        private void LateUpdate()
        {
            if (Report != null) ApplyDirectionalCabinState();
        }

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
                depthStrength,
                occlusionStrength,
                normalStrength,
                instrumentEmission,
                new List<Material>());
        }

        private static ProductionAircraftLightingReport ApplyAuthoredLighting(
            GameObject root,
            Shader stableShader,
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
                strategy = "static_material_depth_plus_baked_ao_soft_normals_and_geometry_derived_directional_visibility_no_realtime_shadow_map"
            };
            Dictionary<Material, Material> cockpitClones = new Dictionary<Material, Material>();

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                report.rendererCount++;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
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

                // The authored window and instrument-cover materials retain their dedicated
                // reflection shaders. Only opaque cockpit geometry receives the stable
                // material-depth shader, so no screen-space overlay can cover the sky.
                if (!cockpit || IsTransparentGlass(renderer)) continue;
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material source = materials[index];
                    if (source == null) continue;
                    if (!cockpitClones.TryGetValue(source, out Material stable))
                    {
                        stable = new Material(source)
                        {
                            name = CleanMaterialName(source.name) + StableMaterialSuffix
                        };
                        stable.shader = stableShader;
                        if (stable.HasProperty("_DirectionalCabinShadowStrength"))
                            stable.SetFloat(DirectionalCabinShadowStrengthId, DefaultDirectionalCabinShadowStrength);
                        stable.SetMatrix(WorldToAircraftId, root.transform.worldToLocalMatrix);
                        cockpitClones.Add(source, stable);
                        ownedMaterials.Add(stable);
                        report.cockpitMaterialCloneCount++;
                        report.cockpitStableShaderMaterialCount++;
                    }

                    TuneCockpitMaterial(stable, identity, report);
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
                float depthWeight =
                    rendererIdentity.IndexOf("Seats", StringComparison.OrdinalIgnoreCase) >= 0 ? 0.78f :
                    rendererIdentity.IndexOf("SeatGear", StringComparison.OrdinalIgnoreCase) >= 0 ? 0.70f :
                    rendererIdentity.IndexOf("Interior_Body", StringComparison.OrdinalIgnoreCase) >= 0 ? 0.62f :
                    rendererIdentity.IndexOf("Paddle", StringComparison.OrdinalIgnoreCase) >= 0 ? 0.50f :
                    0.24f;
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
            material.EnableKeyword("_EMISSION");
            report.readableInstrumentMaterialCount++;
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
            return identity.IndexOf("Meter_01", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("Static_TXT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("ButtonPlate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("Buttons_MAT", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private void OnDestroy()
        {
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
