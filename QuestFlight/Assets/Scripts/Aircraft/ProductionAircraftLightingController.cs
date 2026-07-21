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
        public int remainingRealtimeShadowCasterCount;
        public int remainingRealtimeShadowReceiverCount;
        public int cockpitMaterialCloneCount;
        public int cockpitOcclusionMaterialCountTuned;
        public int cockpitNormalMaterialCountTuned;
        public int readableInstrumentMaterialCount;
        public float staticDepthStrength;
        public float cockpitOcclusionStrength;
        public float coarseNormalStrength;
        public float instrumentReadabilityEmission;
        public string strategy;
    }

    /// <summary>
    /// Applies a deterministic Quest cockpit-lighting treatment to authored renderers. Realtime
    /// cockpit shadows remain disabled; restrained authored AO, softened coarse cabin normals,
    /// sky reflections, and base-texture instrument emission provide stable depth/readability.
    /// No light, camera, mesh, or hierarchy is created.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionAircraftLightingController : MonoBehaviour
    {
        public const float DefaultStaticDepthStrength = 0.30f;
        public const float MaximumStaticDepthStrength = 0.42f;
        public const float DefaultCockpitOcclusionStrength = 0.64f;
        public const float DefaultCoarseNormalStrength = 0.42f;
        public const float DefaultInstrumentReadabilityEmission = 0.20f;

        private const string StableMaterialSuffix = " Quest Cockpit Stable";

        [SerializeField] private GameObject importedAircraftRoot;
        [SerializeField, Range(0f, MaximumStaticDepthStrength)]
        private float staticDepthStrength = DefaultStaticDepthStrength;
        [SerializeField, Range(0f, 1f)]
        private float cockpitOcclusionStrength = DefaultCockpitOcclusionStrength;
        [SerializeField, Range(0f, 1f)]
        private float coarseNormalStrength = DefaultCoarseNormalStrength;
        [SerializeField, Range(0f, 0.5f)]
        private float instrumentReadabilityEmission = DefaultInstrumentReadabilityEmission;

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

            Report = ApplyAuthoredLighting(
                importedAircraftRoot,
                staticDepthStrength,
                cockpitOcclusionStrength,
                coarseNormalStrength,
                instrumentReadabilityEmission,
                _ownedRuntimeMaterials);
            return Report;
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
                depthStrength,
                occlusionStrength,
                normalStrength,
                instrumentEmission,
                new List<Material>());
        }

        private static ProductionAircraftLightingReport ApplyAuthoredLighting(
            GameObject root,
            float depthStrength,
            float occlusionStrength,
            float normalStrength,
            float instrumentEmission,
            ICollection<Material> ownedMaterials)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            ProductionAircraftLightingReport report = new ProductionAircraftLightingReport
            {
                staticDepthStrength = Mathf.Clamp(depthStrength, 0f, MaximumStaticDepthStrength),
                cockpitOcclusionStrength = Mathf.Clamp01(occlusionStrength),
                coarseNormalStrength = Mathf.Clamp01(normalStrength),
                instrumentReadabilityEmission = Mathf.Clamp(instrumentEmission, 0f, 0.5f),
                strategy = "no_realtime_cockpit_shadows_plus_strengthened_baked_ao_soft_normals_and_base_texture_instrument_emission"
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

                if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    report.realtimeShadowCasterCountDisabled++;
                }
                if (renderer.receiveShadows)
                {
                    renderer.receiveShadows = false;
                    report.realtimeShadowReceiverCountDisabled++;
                }

                string identity = RendererIdentity(renderer);
                if (!IsCockpitRenderer(identity)) continue;
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
                        cockpitClones.Add(source, stable);
                        ownedMaterials.Add(stable);
                        report.cockpitMaterialCloneCount++;
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
                $"cockpitClones={report.cockpitMaterialCloneCount} aoTuned={report.cockpitOcclusionMaterialCountTuned} " +
                $"normalsTuned={report.cockpitNormalMaterialCountTuned} readable={report.readableInstrumentMaterialCount} " +
                $"remainingCasters={report.remainingRealtimeShadowCasterCount} " +
                $"remainingReceivers={report.remainingRealtimeShadowReceiverCount} strategy={report.strategy}");
            return report;
        }

        private static void TuneCockpitMaterial(
            Material material,
            string rendererIdentity,
            ProductionAircraftLightingReport report)
        {
            if (material.HasProperty("occlusionTexture_strength"))
            {
                float bounded = Mathf.Min(material.GetFloat("occlusionTexture_strength"), report.cockpitOcclusionStrength);
                material.SetFloat("occlusionTexture_strength", bounded);
                report.cockpitOcclusionMaterialCountTuned++;
            }

            if (rendererIdentity.IndexOf("Interior_Body", StringComparison.OrdinalIgnoreCase) >= 0 &&
                material.HasProperty("normalTexture_scale"))
            {
                material.SetFloat("normalTexture_scale", report.coarseNormalStrength);
                report.cockpitNormalMaterialCountTuned++;
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
