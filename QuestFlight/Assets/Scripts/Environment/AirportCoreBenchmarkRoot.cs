using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Validation-only marker for the explicitly authored airport-core benchmark hierarchy.
    /// This component never creates, replaces, moves, reparents, enables, or disables content.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AirportCoreBenchmarkRoot : MonoBehaviour
    {
        public const string GameObjectName = "AirportCoreBenchmarkRoot";
        public const string V2BaselineName = "AirportCoreV2BaselineReference";

        [Header("Serialized benchmark contract")]
        public AirportCoreBenchmarkManifest manifest;
        public Transform benchmarkContent;
        public LODGroup filteredV2Context;
        [Tooltip("Editor A/B provenance only. Paths do not pull the original meshes into the Android player.")]
        public string[] v2ContextOriginalMeshPaths = Array.Empty<string>();
        [Tooltip("Editor A/B provenance for the active filtered context meshes.")]
        public string[] v4ContextFilteredMeshPaths = Array.Empty<string>();
        public LODGroup executiveTerminal;
        public LODGroup type2HangarRow;
        public Transform fuelServiceContext;
        public Transform authoredApronProps;
        public Transform vegetationHeroCluster;
        public Renderer authoritativeRunway;
        public Renderer boulderReservoir;
        public Renderer minorWaterTerrainEffect;
        public Light benchmarkSun;
        public LightProbeGroup airportLightProbes;
        public ReflectionProbe airportReflectionProbe;
        [Header("Editor-only A/B restoration references")]
        public Material v2RunwayMaterial;
        public Material v2ReservoirMaterial;
        public Quaternion v2SunRotation;
        public Color v2SunColor = Color.white;
        public float v2SunIntensity;
        public bool validateOnAwake = true;

        private void Awake()
        {
            if (!validateOnAwake) return;
            if (!TryValidateContract(out string validationReport))
                Debug.LogError("[QuestFlight][AirportCoreV4] Authored benchmark failed: " + validationReport, this);
            else
                Debug.Log("[QuestFlight][AirportCoreV4] Authored benchmark validated: " + validationReport, this);
        }

        public bool TryValidateContract(out string report)
        {
            List<string> failures = new List<string>();
            Vector3[] probePositions = airportLightProbes != null
                ? airportLightProbes.probePositions
                : null;
            Require(string.Equals(name, GameObjectName, StringComparison.Ordinal),
                "benchmark root name changed", failures);
            Require(manifest != null && manifest.TryValidate(out _),
                "benchmark manifest is missing or invalid", failures);
            Require(benchmarkContent != null && benchmarkContent.IsChildOf(transform),
                "benchmark content is not explicitly authored below the root", failures);
            Require(benchmarkContent == null || benchmarkContent.gameObject.activeSelf,
                "benchmark content must be active in the authored V4 variant", failures);
            Require(TryValidateFilteredContextRepresentation(out string filteredContextReport),
                filteredContextReport, failures);
            Require(HasThreeAssetPaths(v2ContextOriginalMeshPaths),
                "V2 A/B original context mesh paths are missing", failures);
            Require(HasThreeAssetPaths(v4ContextFilteredMeshPaths),
                "V4 filtered context mesh paths are missing", failures);
            Require(GetComponentsInChildren<Transform>(true).All(item =>
                    !string.Equals(item.name, V2BaselineName, StringComparison.Ordinal) &&
                    !string.Equals(item.name, "FilteredV2Context", StringComparison.Ordinal)),
                "serialized duplicate A/B context renderer hierarchy remains", failures);
            Require(executiveTerminal != null && executiveTerminal.transform.IsChildOf(transform),
                "executive terminal LODGroup is missing", failures);
            Require(type2HangarRow != null && type2HangarRow.transform.IsChildOf(transform),
                "Type 2 hangar-row LODGroup is missing", failures);
            Require(benchmarkContent == null || executiveTerminal == null ||
                    executiveTerminal.transform.IsChildOf(benchmarkContent),
                "executive terminal is outside the switchable benchmark content", failures);
            Require(benchmarkContent == null || type2HangarRow == null ||
                    type2HangarRow.transform.IsChildOf(benchmarkContent),
                "Type 2 hangar row is outside the switchable benchmark content", failures);
            Require(HasThreeValidLods(executiveTerminal),
                "executive terminal must have three nonempty LODs", failures);
            Require(HasThreeValidLods(type2HangarRow),
                "Type 2 hangar row must have three nonempty LODs", failures);
            Require(fuelServiceContext != null && fuelServiceContext.IsChildOf(transform),
                "fuel/service context is missing", failures);
            Require(authoredApronProps != null && authoredApronProps.IsChildOf(transform),
                "authored apron props are missing", failures);
            Require(vegetationHeroCluster != null && vegetationHeroCluster.IsChildOf(transform),
                "vegetation hero cluster is missing", failures);
            Require(benchmarkContent == null || fuelServiceContext == null ||
                    fuelServiceContext.IsChildOf(benchmarkContent),
                "fuel/service context is outside the switchable benchmark content", failures);
            Require(benchmarkContent == null || authoredApronProps == null ||
                    authoredApronProps.IsChildOf(benchmarkContent),
                "apron props are outside the switchable benchmark content", failures);
            Require(benchmarkContent == null || vegetationHeroCluster == null ||
                    vegetationHeroCluster.IsChildOf(benchmarkContent),
                "vegetation is outside the switchable benchmark content", failures);
            Require(authoritativeRunway != null && authoritativeRunway.GetComponent<MeshFilter>()?.sharedMesh != null,
                "authoritative runway binding is missing", failures);
            Require(boulderReservoir != null && boulderReservoir.GetComponent<MeshFilter>()?.sharedMesh != null,
                "Boulder Reservoir binding is missing", failures);
            Require(minorWaterTerrainEffect != null,
                "minor-water terrain effect binding is missing", failures);
            Require(benchmarkSun != null && benchmarkSun.type == LightType.Directional,
                "bounded lighting contract has no directional sun", failures);
#if UNITY_EDITOR
            // Unity exposes Light.lightmapBakeType only to Editor assemblies. Keep this
            // fail-closed assertion in editor validation without breaking Android players.
            Require(benchmarkSun == null || benchmarkSun.lightmapBakeType == LightmapBakeType.Baked,
                "benchmark static sun must be baked", failures);
#endif
            Require(probePositions != null && probePositions.Length == 18,
                "airport light-probe volume must contain the authored 18 probes", failures);
            Require(probePositions == null ||
                    probePositions.Select(position => position.y).Distinct().Count() >= 2,
                "airport light probes must occupy at least two heights", failures);
            Require(airportReflectionProbe == null || airportReflectionProbe.mode == ReflectionProbeMode.Baked,
                "airport reflection probe must be baked", failures);
            Require(v2RunwayMaterial != null,
                "V2 runway A/B restoration material is missing", failures);
            if (manifest != null && manifest.waterEffectsDeferredToOrthophoto)
            {
                Require(v2ReservoirMaterial == null,
                    "orthophoto-only water must not serialize the V2 reservoir A/B material", failures);
                Require(boulderReservoir == null || boulderReservoir.sharedMaterials.Length == 0,
                    "orthophoto-only water must not serialize a reservoir renderer material", failures);
            }
            else
            {
                Require(v2ReservoirMaterial != null,
                    "V2 reservoir A/B restoration material is missing", failures);
            }
            Require(v2SunIntensity > 0f,
                "V2 sun A/B restoration state is missing", failures);
            Require(IsFinite(v2SunRotation.x) && IsFinite(v2SunRotation.y) &&
                    IsFinite(v2SunRotation.z) && IsFinite(v2SunRotation.w) &&
                    Mathf.Abs(Quaternion.Dot(v2SunRotation, v2SunRotation) - 1f) < 0.02f,
                "V2 sun A/B restoration rotation is invalid", failures);
            Require(IsFinite(v2SunColor.r) && IsFinite(v2SunColor.g) &&
                    IsFinite(v2SunColor.b) && IsFinite(v2SunIntensity),
                "V2 sun A/B restoration color/intensity is nonfinite", failures);
            Require(GetComponentsInChildren<Camera>(true).Length == 0,
                "benchmark hierarchy may not own a camera", failures);
            Require(GetComponentsInChildren<AudioListener>(true).Length == 0,
                "benchmark hierarchy may not own an AudioListener", failures);
            Require(GetComponentsInChildren<Rigidbody>(true).Length == 0,
                "benchmark hierarchy may not introduce runtime rigidbodies", failures);
            Require(GetComponentsInChildren<Renderer>(true).All(item =>
                    item.sharedMaterials != null && item.sharedMaterials.All(material => material != null)),
                "benchmark renderer has a missing material", failures);
            Require(GetComponentsInChildren<Renderer>(true).All(item =>
                    item.shadowCastingMode != ShadowCastingMode.TwoSided),
                "benchmark uses uncontrolled two-sided shadow casting", failures);

            report = failures.Count == 0
                ? manifest.TryValidate(out string manifestReport) ? manifestReport : "manifest invalid"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }

        /// <summary>
        /// Accepts the original active V4 context when no aerial representation is authored, or the
        /// explicitly mapped Aerial V1 building replacement. This method only inspects serialized state.
        /// </summary>
        public bool TryValidateFilteredContextRepresentation(out string report)
        {
            List<string> failures = new List<string>();
            Require(HasThreeValidLods(filteredV2Context),
                "the product context does not expose one valid filtered three-LOD source", failures);

            Transform authoredScope = transform.root;
            AerialSceneryRoot[] aerialRoots = authoredScope.GetComponentsInChildren<AerialSceneryRoot>(true);
            if (aerialRoots.Length == 0)
            {
                Require(filteredV2Context == null || filteredV2Context.gameObject.activeSelf,
                    "filtered V2 context must be active in the V4 variant when no AerialSceneryRoot replacement is authored",
                    failures);
            }
            else
            {
                Require(aerialRoots.Length == 1,
                    $"expected exactly one AerialSceneryRoot replacement, found {aerialRoots.Length}", failures);
                if (aerialRoots.Length == 1)
                    ValidateAerialContextReplacement(aerialRoots[0], authoredScope, failures);
            }

            report = failures.Count == 0
                ? aerialRoots.Length == 0
                    ? "PASS_FILTERED_V2_CONTEXT_V4"
                    : "PASS_FILTERED_V2_CONTEXT_AERIAL_V1_REPLACEMENT"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }

        private void ValidateAerialContextReplacement(
            AerialSceneryRoot aerialRoot,
            Transform authoredScope,
            ICollection<string> failures)
        {
            Require(aerialRoot.enabled && IsAuthoredActive(aerialRoot.transform, authoredScope),
                "AerialSceneryRoot replacement must be active", failures);
            Require(aerialRoot.buildingRoot != null &&
                    (aerialRoot.buildingRoot == aerialRoot.transform ||
                     aerialRoot.buildingRoot.IsChildOf(aerialRoot.transform)) &&
                    IsAuthoredActive(aerialRoot.buildingRoot, aerialRoot.transform),
                "AerialSceneryRoot buildingRoot must be an active authored child", failures);

            int mappingCount = filteredV2Context == null || aerialRoot.disabledLegacyBuildingRoots == null
                ? 0
                : aerialRoot.disabledLegacyBuildingRoots.Count(item =>
                    item != null && item == filteredV2Context.transform);
            Require(mappingCount == 1,
                "filtered V2 context must appear exactly once in AerialSceneryRoot.disabledLegacyBuildingRoots",
                failures);
            Require(filteredV2Context != null && !filteredV2Context.gameObject.activeSelf,
                "mapped filtered V2 context must be explicitly disabled by the Aerial V1 replacement", failures);

            Renderer[] replacementRenderers = aerialRoot.buildingRoot == null
                ? Array.Empty<Renderer>()
                : aerialRoot.buildingRoot.GetComponentsInChildren<Renderer>(true);
            Require(replacementRenderers.Any(item =>
                    item != null && item.enabled && IsAuthoredActive(item.transform, aerialRoot.transform)),
                "active Aerial V1 replacement building renderers are missing", failures);

            Renderer[] legacyRenderers = filteredV2Context == null
                ? Array.Empty<Renderer>()
                : filteredV2Context.GetComponentsInChildren<Renderer>(true);
            Require(legacyRenderers.All(item =>
                    item == null || !item.enabled || !IsAuthoredActive(item.transform, authoredScope)),
                "a mapped legacy filtered-context renderer remains active", failures);
        }

        private static bool IsAuthoredActive(Transform candidate, Transform boundary)
        {
            if (candidate == null || boundary == null) return false;
            for (Transform current = candidate; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == boundary) return true;
            }
            return false;
        }

        private static bool HasThreeValidLods(LODGroup group)
        {
            if (group == null) return false;
            LOD[] lods = group.GetLODs();
            return lods.Length == 3 && lods.All(lod => lod.renderers != null && lod.renderers.Length > 0 &&
                lod.renderers.All(renderer => renderer != null));
        }

        private static bool HasThreeAssetPaths(string[] paths)
        {
            return paths != null && paths.Length == 3 &&
                   paths.All(path => !string.IsNullOrWhiteSpace(path));
        }

        private static void Require(bool condition, string message, ICollection<string> failures)
        {
            if (!condition) failures.Add(message);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
