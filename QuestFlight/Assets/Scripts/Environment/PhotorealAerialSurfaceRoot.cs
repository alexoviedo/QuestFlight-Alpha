using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Validation-only contract for the explicitly authored photoreal aerial surface.
    /// This component never creates, repairs, streams, or selects product content at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhotorealAerialSurfaceRoot : MonoBehaviour
    {
        public const string GameObjectName = "AerialSceneryRepresentationV1";
        public const string BareEarthLayerName = "ProductionBareEarthTerrain";
        public const string BuildingLayerName = "ProductionBuildingSurfaceLayer";
        public const string CanopyLayerName = "ProductionCanopySurfaceLayer";
        public const string HeroLayerName = "ProductionHeroObjectLayer";

        [Header("Explicit semantic layers")]
        public Transform productionBareEarthTerrain;
        public Transform productionBuildingSurfaceLayer;
        public Transform productionCanopySurfaceLayer;
        public Transform productionHeroObjectLayer;

        [Header("Authored runtime representation")]
        public MeshFilter[] authoritativeBareEarthMeshes = Array.Empty<MeshFilter>();
        public Renderer[] buildingSurfaceRenderers = Array.Empty<Renderer>();
        public Renderer[] canopySurfaceRenderers = Array.Empty<Renderer>();
        public Transform airportHeroSourceRoot;

        [Header("Pinned source and ownership records")]
        public TextAsset imagerySourceManifest;
        public TextAsset lidarSurfaceManifest;
        public TextAsset buildingSurfaceManifest;
        public TextAsset canopySurfaceManifest;
        public TextAsset ownershipExclusionManifest;

        [Header("Runtime surface inputs")]
        public Texture2D nearOrthophoto;
        public Texture2D midOrthophoto;
        public Texture2D farOrthophoto;
        public Texture2D normalizedSurfaceHeight;
        public Texture2D surfaceClassification;
        public Texture2D buildingOwnershipMask;
        public Texture2D canopyOwnershipMask;

        [Header("Measured data contract")]
        public float nearMetersPerTexel;
        public float patternMetersPerTexel;
        public float farMetersPerTexel;
        public int nearBuildingSourceCount;
        public int midBuildingSourceCount;
        public int representedBuildingCount;
        public float nearBuildingCoveragePercent;
        public float midBuildingCoveragePercent;
        public float maximumBuildingFootprintInflationPercent;
        public int representedCanopyComponentCount;
        public float representedCanopyAreaPercentOfSource;
        public float medianCanopyHeightAgreementPercent;
        public float terrainMeanAbsoluteErrorMeters;
        public float terrainP95ErrorMeters;
        public float terrainMaximumErrorOutsideDeclaredOutliersMeters;
        public bool operationalSurfaceFiveCentimeterToleranceEstablished;

        [Header("Baked product inventory")]
        public int bakedBuildingDrawCount;
        public int bakedBuildingTriangleCount;
        public int bakedCanopyDrawCount;
        public int bakedCanopyTriangleCount;
        public long bakedTextureResidentBytes;
        public string bakeToolVersion;
        public string bakeUtc;
        public bool validateOnAwake = true;

        private void Awake()
        {
            if (!validateOnAwake) return;
            if (!TryValidateContract(out string report))
                Debug.LogError("[QuestFlight][PhotorealAerialSurfaceV1] Authored contract failed: " + report, this);
            else
                Debug.Log("[QuestFlight][PhotorealAerialSurfaceV1] Authored contract validated: " + report, this);
        }

        public bool TryValidateContract(out string report)
        {
            List<string> failures = new List<string>();
            Require(string.Equals(name, GameObjectName, StringComparison.Ordinal),
                "surface contract is not attached to the canonical aerial root", failures);
            Require(IsNamedAuthoredChild(productionBareEarthTerrain, BareEarthLayerName),
                "ProductionBareEarthTerrain is missing or misplaced", failures);
            Require(IsNamedAuthoredChild(productionBuildingSurfaceLayer, BuildingLayerName),
                "ProductionBuildingSurfaceLayer is missing or misplaced", failures);
            Require(IsNamedAuthoredChild(productionCanopySurfaceLayer, CanopyLayerName),
                "ProductionCanopySurfaceLayer is missing or misplaced", failures);
            Require(IsNamedAuthoredChild(productionHeroObjectLayer, HeroLayerName),
                "ProductionHeroObjectLayer is missing or misplaced", failures);

            Require(authoritativeBareEarthMeshes != null && authoritativeBareEarthMeshes.Length >= 3 &&
                    authoritativeBareEarthMeshes.All(item => item != null && item.sharedMesh != null),
                "bare-earth terrain bindings are incomplete", failures);
            Require(airportHeroSourceRoot != null, "airport hero source root is not bound", failures);
            Require(AllPresent(imagerySourceManifest, lidarSurfaceManifest, buildingSurfaceManifest,
                    canopySurfaceManifest, ownershipExclusionManifest),
                "one or more provenance/ownership manifests are missing", failures);
            Require(AllPresent(nearOrthophoto, midOrthophoto, farOrthophoto, normalizedSurfaceHeight,
                    surfaceClassification, buildingOwnershipMask, canopyOwnershipMask),
                "one or more runtime surface inputs are missing", failures);

            Renderer[] buildings = Active(buildingSurfaceRenderers);
            Renderer[] canopy = Active(canopySurfaceRenderers);
            bool canopyMapOnly = canopySurfaceManifest != null &&
                canopySurfaceManifest.text.Contains(
                    "\"type\": \"terrain_canopy_map_only_outside_hero_radius\"");
            Require(buildings.Length > 0 && buildings.Length <= 16,
                $"building surface must use 1-16 offline batches, found {buildings.Length}", failures);
            Require(canopyMapOnly ? canopy.Length == 0 : canopy.Length > 0 && canopy.Length <= 4,
                canopyMapOnly
                    ? $"terrain-map-only canopy must have zero raised batches, found {canopy.Length}"
                    : $"canopy surface must use 1-4 offline batches, found {canopy.Length}", failures);
            Require(DistinctMaterialCount(buildings) == 1,
                "building surface must use one shared material", failures);
            Require(DistinctMaterialCount(canopy) == (canopyMapOnly ? 0 : 1),
                canopyMapOnly
                    ? "terrain-map-only canopy must not retain a geometry material"
                    : "canopy surface must use one shared material", failures);
            Require(buildings.All(renderer => renderer.GetComponent<MeshFilter>()?.sharedMesh != null),
                "a building batch has no mesh", failures);
            Require(canopy.All(renderer => renderer.GetComponent<MeshFilter>()?.sharedMesh != null),
                "a canopy batch has no mesh", failures);
            Require(buildings.Concat(canopy).SelectMany(renderer =>
                    renderer.GetComponentsInChildren<LODGroup>(true)).Count() == 0,
                "semantic surface batches must use fixed geometry without runtime LOD morphing", failures);
            Require(productionCanopySurfaceLayer == null ||
                    productionCanopySurfaceLayer.GetComponentsInChildren<BillboardRenderer>(true).Length == 0,
                "ordinary canopy billboards are prohibited", failures);

            Require(nearMetersPerTexel > 0f && nearMetersPerTexel <= 0.75f,
                $"near orthophoto density is {nearMetersPerTexel:0.###} m/texel", failures);
            Require(patternMetersPerTexel > 0f && patternMetersPerTexel <= 2f,
                $"pattern orthophoto density is {patternMetersPerTexel:0.###} m/texel", failures);
            Require(farMetersPerTexel > 0f && farMetersPerTexel <= 6f,
                $"far orthophoto density is {farMetersPerTexel:0.###} m/texel", failures);
            Require(nearBuildingSourceCount > 0 && midBuildingSourceCount > 0 &&
                    representedBuildingCount >= nearBuildingSourceCount + midBuildingSourceCount,
                "building source/representation counts are inconsistent", failures);
            Require(nearBuildingCoveragePercent >= 99f,
                $"0-2 km building coverage is {nearBuildingCoveragePercent:0.###}%", failures);
            Require(midBuildingCoveragePercent >= 98f,
                $"2-4 km building coverage is {midBuildingCoveragePercent:0.###}%", failures);
            Require(maximumBuildingFootprintInflationPercent <= 5f,
                $"building footprint inflation is {maximumBuildingFootprintInflationPercent:0.###}%", failures);
            Require(canopyMapOnly ? representedCanopyComponentCount == 0 : representedCanopyComponentCount > 0,
                canopyMapOnly
                    ? "terrain-map-only canopy must not report raised components"
                    : "no connected canopy component is represented", failures);
            Require(representedCanopyAreaPercentOfSource >= 90f && representedCanopyAreaPercentOfSource <= 110f,
                $"canopy area agreement is {representedCanopyAreaPercentOfSource:0.###}%", failures);
            Require(medianCanopyHeightAgreementPercent >= 85f && medianCanopyHeightAgreementPercent <= 115f,
                $"median canopy-height agreement is {medianCanopyHeightAgreementPercent:0.###}%", failures);
            Require(terrainMeanAbsoluteErrorMeters <= 0.15f && terrainP95ErrorMeters <= 0.5f &&
                    terrainMaximumErrorOutsideDeclaredOutliersMeters <= 1.5f,
                "near terrain error contract is exceeded", failures);

            Require(bakedBuildingDrawCount == buildings.Length,
                "building draw inventory is stale", failures);
            Require(bakedCanopyDrawCount == canopy.Length,
                "canopy draw inventory is stale", failures);
            Require(bakedBuildingTriangleCount == CountTriangles(buildings),
                "building triangle inventory is stale", failures);
            Require(bakedCanopyTriangleCount == CountTriangles(canopy),
                "canopy triangle inventory is stale", failures);
            Require(bakedTextureResidentBytes > 0, "texture residency inventory is missing", failures);
            Require(!string.IsNullOrWhiteSpace(bakeToolVersion) && !string.IsNullOrWhiteSpace(bakeUtc),
                "bake identity is missing", failures);

            report = failures.Count == 0
                ? $"PASS buildings={representedBuildingCount:N0}/{bakedBuildingDrawCount:N0}/" +
                  $"{bakedBuildingTriangleCount:N0}, canopy={representedCanopyComponentCount:N0}/" +
                  $"{bakedCanopyDrawCount:N0}/{bakedCanopyTriangleCount:N0}, " +
                  $"imagery={nearMetersPerTexel:0.###}/{patternMetersPerTexel:0.###}/" +
                  $"{farMetersPerTexel:0.###}m"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }

        private bool IsNamedAuthoredChild(Transform value, string expectedName)
        {
            return value != null && value.IsChildOf(transform) &&
                   string.Equals(value.name, expectedName, StringComparison.Ordinal) &&
                   value.localPosition.sqrMagnitude <= 0.000001f &&
                   Quaternion.Angle(value.localRotation, Quaternion.identity) <= 0.001f &&
                   (value.localScale - Vector3.one).sqrMagnitude <= 0.000001f;
        }

        private static bool AllPresent(params UnityEngine.Object[] values) =>
            values != null && values.All(value => value != null);

        private Renderer[] Active(IEnumerable<Renderer> values) => values == null
            ? Array.Empty<Renderer>()
            : values.Where(value => value != null && value.enabled && IsAuthoredActive(value.transform)).ToArray();

        private bool IsAuthoredActive(Transform candidate)
        {
            for (Transform current = candidate; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == transform) return true;
            }
            return false;
        }

        private static int DistinctMaterialCount(IEnumerable<Renderer> renderers) => renderers
            .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null).Distinct().Count();

        private static int CountTriangles(IEnumerable<Renderer> renderers)
        {
            long total = 0;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    if (mesh.GetTopology(subMesh) == MeshTopology.Triangles)
                        total += (long)mesh.GetIndexCount(subMesh) / 3L;
            }
            return (int)Math.Min(int.MaxValue, total);
        }

        private static void Require(bool condition, string failure, ICollection<string> failures)
        {
            if (!condition) failures.Add(failure);
        }
    }
}
