using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    public enum ProductionWaterPresentationMode
    {
        AuthoredSurface = 0,
        OrthophotoOnly = 1
    }

    /// <summary>
    /// Contract and validation-only marker for the authored production environment prefab.
    /// This component never creates, replaces, moves, or re-parents production geometry.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionEnvironmentRoot : MonoBehaviour
    {
        public const string PrefabAssetPath = "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string ProductionSliceMarkerName = "ProductionVerticalSliceRoot";
        public const float NearCoverageMeters = 4000f;
        public const float MidCoverageMeters = 12000f;
        public const float FarCoverageMeters = 24000f;
        public const int MaximumRenderers = 180;
        public const int MaximumVisibleTriangles = 700000;
        public const int MaximumMaterials = 40;
        public const string ProductionWaterShaderName = "QuestFlightLab/Production Stable Water";
        public const string ProductionReservoirShaderName = "QuestFlight/Airport Core V4/Reservoir Opaque";
        public const float MinimumAcceptedWaterTerrainSeparationMeters = 0.12f;

        [Header("Authored hierarchy")]
        public Transform nearProductionZone;
        public Transform midContextZone;
        public Transform immutableFarTerrain;
        public Transform airportContext;
        public Transform authoritativeRunwaySystem;
        public Transform essentialWater;
        [Tooltip("OrthophotoOnly deliberately defers all water effects while preserving source provenance for final polish.")]
        public ProductionWaterPresentationMode waterPresentationMode =
            ProductionWaterPresentationMode.AuthoredSurface;

        [Header("Immutable terrain")]
        public MeshFilter[] nearTerrainMeshes = Array.Empty<MeshFilter>();
        public MeshFilter midTerrainMesh;
        public MeshFilter farTerrainMesh;
        public Texture2D uniqueMacroAlbedo;
        public ProductionTerrainSurfaceAsset authoritativeTerrainSurface;
        public ProductionPlacementManifest productionPlacementManifest;
        public AirportParkingStandManifest airportParkingStandManifest;
        public Vector2 macroWorldMinimumMeters = new Vector2(-6000f, -6000f);
        public Vector2 macroWorldSizeMeters = new Vector2(MidCoverageMeters, MidCoverageMeters);
        [Range(0f, 1f)] public float macroRepeatedTileSimilarity;
        public int farTerrainSourceVertexCount;
        public int farTerrainSourceIndexCount;
        public string farTerrainSourceTopologyHash;
        public string farTerrainBakedTopologyHash;
        public int farTerrainSmoothingPassCount;

        [Header("Baked context filtering")]
        public int omittedDuplicatedLandcoverBatchCount;
        public int rejectedMalformedGroundBatchCount;

        [Header("Authoritative FAA runway 08/26")]
        public MeshFilter runwayPavement;
        public MeshRenderer runwayMarkings;
        public MeshCollider runwayCollisionSurface;
        public Vector3 runway08EndLocal;
        public Vector3 runway26EndLocal;
        public float runwayLengthMeters;
        public float runwayWidthMeters;
        public float runwayGradeDeltaMeters;
        public float measuredRunwayToTerrainMaximumGapMeters;
        public float measuredMarkingToRunwayMaximumGapMeters;
        public float measuredCollisionSurfaceDisagreementMeters;
        public int bakedRunwayThresholdStripeQuadCount;
        public int bakedRunwayMarkingQuadCount;

        [Header("Authoritative bounded water")]
        public MeshFilter boulderReservoirSurface;
        public MeshFilter boulderReservoirShoreBank;
        public TextAsset waterFeatureManifestV2;
        public MeshFilter otherWaterFeaturesSurfaceV2;
        public MeshFilter otherWaterFeaturesShoreBankV2;
        public int retainedWaterBodyCount;
        public int discardedMinorWaterFeatureCount;
        public float minimumWaterTerrainSeparationMeters;
        public int boulderReservoirSourceShorelineVertexCount;
        public int boulderReservoirSmoothedShorelineVertexCount;
        public bool boulderReservoirShorelineClosed;
        public int boulderReservoirShorelineSelfIntersectionCount;

        [Header("Pinned provenance")]
        public string terrainSourceSnapshot = "USGS 3DEP snapshot 20260710T214309Z";
        public string runwaySource = "FAA Airport Data and Information Portal effective 2026-04-16";
        public string contextSource = "OpenStreetMap derivative, ODbL-1.0, © OpenStreetMap contributors";
        public string macroSource = "Project-authored deterministic USGS elevation/slope + OSM land-cover/road/aeroway derivative";
        public string microDetailSource = "Poly Haven CC0 1.0: Withered Grass, Sparse Grass, Dry Ground 01";

        [Header("Bake result")]
        public int bakedRendererCount;
        public int bakedTriangleCount;
        public int bakedMaterialCount;
        public int bakedTextureCount;
        public string bakeUtc;
        public string bakeToolVersion;
        public bool validateOnAwake = true;

        private void Awake()
        {
            if (!validateOnAwake) return;
            if (!TryValidateContract(out string report))
            {
                Debug.LogError("[QuestFlight][ProductionEnvironment] Authored prefab contract failed: " + report, this);
            }
            else
            {
                Debug.Log("[QuestFlight][ProductionEnvironment] Authored prefab validated: " + report, this);
            }
        }

        public bool TryValidateContract(out string report)
        {
            return TryValidateContract(!Application.isEditor, out report);
        }

        /// <summary>
        /// Exercises the built-player validation path from Editor tests. Unity static batching can
        /// replace a visual MeshFilter's sharedMesh with a combined player mesh, so the player must
        /// use the Editor-verified serialized bake budget while retaining every geometric tolerance
        /// and missing-reference check.
        /// </summary>
        public bool TryValidateBuiltPlayerContract(out string report)
        {
            return TryValidateContract(true, out report);
        }

        private bool TryValidateContract(bool builtPlayerStaticBatchSafe, out string report)
        {
            List<string> failures = new List<string>();
            Require(nearProductionZone != null, "NearProductionZone_4km is not bound", failures);
            Require(midContextZone != null, "MidContextZone_12km is not bound", failures);
            Require(immutableFarTerrain != null, "ImmutableFarTerrain_24km is not bound", failures);
            Require(airportContext != null, "AirportContext is not bound", failures);
            Require(authoritativeRunwaySystem != null, "AuthoritativeRunwaySystem is not bound", failures);
            Require(essentialWater != null, "EssentialWater is not bound", failures);
            Require(nearTerrainMeshes != null && nearTerrainMeshes.Length == 2 && nearTerrainMeshes.All(HasMesh),
                "near terrain must contain the baked airport patch and 4 km USGS ring", failures);
            Require(HasMesh(midTerrainMesh), "12 km USGS mid terrain mesh is missing", failures);
            Require(HasMesh(farTerrainMesh), "immutable 24 km USGS far terrain mesh is missing", failures);
            Require(farTerrainSourceVertexCount > 0, "far-terrain source vertex count is missing", failures);
            Require(farTerrainSourceIndexCount > 0, "far-terrain source index count is missing", failures);
            if (!builtPlayerStaticBatchSafe)
            {
                Require(farTerrainMesh == null || (farTerrainMesh.sharedMesh != null &&
                        farTerrainSourceVertexCount == farTerrainMesh.sharedMesh.vertexCount),
                    "far-terrain smoothing changed vertex topology", failures);
                Require(farTerrainMesh == null || (farTerrainMesh.sharedMesh != null &&
                        farTerrainSourceIndexCount == (int)farTerrainMesh.sharedMesh.GetIndexCount(0)),
                    "far-terrain smoothing changed index topology", failures);
            }
            Require(!string.IsNullOrEmpty(farTerrainSourceTopologyHash) &&
                    string.Equals(farTerrainSourceTopologyHash, farTerrainBakedTopologyHash, StringComparison.Ordinal),
                "far-terrain smoothing changed triangle identity/order", failures);
            Require(farTerrainSmoothingPassCount == 2,
                $"far terrain must use two conservative offline smoothing passes, found {farTerrainSmoothingPassCount}", failures);
            Require(uniqueMacroAlbedo != null, "unique nonrepeating macro albedo is missing", failures);
            Require(authoritativeTerrainSurface != null,
                "authoritative production terrain-surface asset is missing", failures);
            Require(productionPlacementManifest != null && productionPlacementManifest.entries != null &&
                    productionPlacementManifest.entries.Length > 0,
                "production placement manifest is missing or empty", failures);
            Require(airportParkingStandManifest != null && airportParkingStandManifest.stands != null &&
                    airportParkingStandManifest.stands.Length >= 3,
                "airport parking-stand manifest is missing or incomplete", failures);
            Require(macroWorldSizeMeters.x >= MidCoverageMeters && macroWorldSizeMeters.y >= MidCoverageMeters,
                "macro albedo does not cover the 12 km production context", failures);
            Require(macroRepeatedTileSimilarity < 0.985f,
                $"macro map repeated-tile similarity is too high ({macroRepeatedTileSimilarity:0.000})", failures);

            Require(HasMesh(runwayPavement), "FAA runway pavement mesh is missing", failures);
            Require(runwayMarkings != null && runwayMarkings.GetComponent<MeshFilter>()?.sharedMesh != null,
                "runway marking mesh is missing", failures);
            Require(runwayCollisionSurface != null && runwayCollisionSurface.sharedMesh != null,
                "physical runway collision mesh is missing", failures);
            if (!builtPlayerStaticBatchSafe)
            {
                Require(runwayPavement != null && runwayCollisionSurface != null &&
                        ReferenceEquals(runwayPavement.sharedMesh, runwayCollisionSurface.sharedMesh),
                    "visual and physical runway must share exactly one mesh asset", failures);
            }
            Require(Mathf.Abs(runwayLengthMeters - 1249.68f) <= 3f,
                $"FAA runway length is implausible ({runwayLengthMeters:0.00} m)", failures);
            Require(Mathf.Abs(runwayWidthMeters - 22.86f) <= 0.05f,
                $"FAA runway width is incorrect ({runwayWidthMeters:0.00} m)", failures);
            Require(measuredRunwayToTerrainMaximumGapMeters <= 0.025f,
                $"runway/terrain gap exceeds 25 mm ({measuredRunwayToTerrainMaximumGapMeters:0.0000} m)", failures);
            Require(measuredMarkingToRunwayMaximumGapMeters <= 0.003f,
                $"marking/runway gap exceeds 3 mm ({measuredMarkingToRunwayMaximumGapMeters:0.0000} m)", failures);
            Require(measuredCollisionSurfaceDisagreementMeters <= 0.0001f,
                $"runway collider disagrees with visual mesh ({measuredCollisionSurfaceDisagreementMeters:0.000000} m)", failures);
            Require(bakedRunwayThresholdStripeQuadCount == 12,
                $"expected twelve discrete threshold stripe quads across both runway ends, found {bakedRunwayThresholdStripeQuadCount}", failures);
            Require(bakedRunwayMarkingQuadCount == 26,
                $"combined runway marking mesh has an unexpected quad count ({bakedRunwayMarkingQuadCount})", failures);

            Require(omittedDuplicatedLandcoverBatchCount > 0,
                "duplicated OSM land-cover batches were not omitted from production geometry", failures);
            Require(rejectedMalformedGroundBatchCount > 0,
                "no malformed near-airport ground/road batch was rejected", failures);
            Require(airportContext == null || airportContext.GetComponentsInChildren<Renderer>(true).All(renderer =>
                    renderer.name.IndexOf("_landcover_", StringComparison.OrdinalIgnoreCase) < 0 &&
                    renderer.name.IndexOf("RealContext_0_road_asphalt_021", StringComparison.Ordinal) < 0),
                "production context still contains a duplicate land-cover or known malformed road renderer", failures);

            Require(retainedWaterBodyCount > 1,
                $"bounded V2 water coverage is incomplete ({retainedWaterBodyCount} represented features)", failures);
            Require(waterFeatureManifestV2 != null,
                "V2 water-feature source manifest is missing", failures);
            Require(HasMesh(boulderReservoirSurface), "Boulder Reservoir surface is missing", failures);
            Require(HasMesh(boulderReservoirShoreBank), "Boulder Reservoir shore bank is missing", failures);
            Require(otherWaterFeaturesSurfaceV2 == null,
                "obsolete V2 non-reservoir water ribbon surface remains serialized", failures);
            Require(otherWaterFeaturesShoreBankV2 == null,
                "obsolete V2 non-reservoir water bank geometry remains serialized", failures);
            Renderer reservoirRenderer = boulderReservoirSurface == null
                ? null
                : boulderReservoirSurface.GetComponent<Renderer>();
            Renderer shoreRenderer = boulderReservoirShoreBank == null
                ? null
                : boulderReservoirShoreBank.GetComponent<Renderer>();
            if (waterPresentationMode == ProductionWaterPresentationMode.OrthophotoOnly)
            {
                Require(reservoirRenderer != null &&
                        !reservoirRenderer.enabled && !IsAuthoredActive(reservoirRenderer.transform),
                    "orthophoto-only water requires the reservoir renderer to be explicitly disabled", failures);
                Require(reservoirRenderer == null || reservoirRenderer.sharedMaterials.Length == 0,
                    "orthophoto-only water must not serialize a reservoir material dependency", failures);
                Require(shoreRenderer != null && !shoreRenderer.enabled && !IsAuthoredActive(shoreRenderer.transform),
                    "orthophoto-only water requires the shore-bank renderer to be disabled", failures);
                Require(shoreRenderer == null || shoreRenderer.sharedMaterials.Length == 0,
                    "orthophoto-only water must not serialize a shore-bank material dependency", failures);
            }
            else if (boulderReservoirSurface != null)
            {
                Material water = reservoirRenderer?.sharedMaterial;
                Require(water != null && water.renderQueue == (int)RenderQueue.Geometry + 5,
                    "Boulder Reservoir must use the authored Geometry+5 opaque queue", failures);
                Require(water != null && water.shader != null &&
                        string.Equals(water.shader.name, ProductionReservoirShaderName, StringComparison.Ordinal),
                    "Boulder Reservoir must use the bounded V4 opaque reservoir shader", failures);
                Require(water != null && water.passCount == 1,
                    "Boulder Reservoir must use exactly one shader pass", failures);
            }
            Require(boulderReservoirShorelineClosed,
                "Boulder Reservoir smoothed shoreline is not closed", failures);
            Require(boulderReservoirSourceShorelineVertexCount >= 3 &&
                    boulderReservoirSmoothedShorelineVertexCount == boulderReservoirSourceShorelineVertexCount * 2,
                "Boulder Reservoir shoreline was not deterministically densified by one Chaikin pass", failures);
            Require(boulderReservoirShorelineSelfIntersectionCount == 0,
                $"Boulder Reservoir shoreline has {boulderReservoirShorelineSelfIntersectionCount} self-intersection(s)", failures);
            Require(minimumWaterTerrainSeparationMeters >= MinimumAcceptedWaterTerrainSeparationMeters,
                $"water/terrain separation is too small ({minimumWaterTerrainSeparationMeters:0.000} m)", failures);

            Require(immutableFarTerrain == null || immutableFarTerrain.GetComponentsInChildren<LODGroup>(true).Length == 0,
                "far terrain must not contain LOD groups", failures);
            Require(immutableFarTerrain == null || !ContainsComponentNamed(immutableFarTerrain, "RealKbduBatchDistanceCuller"),
                "far terrain must not use camera-distance culling", failures);
            Require(!ContainsComponentNamed(transform, "SceneryModeController") &&
                    !ContainsComponentNamed(transform, "MeshSceneryProvider") &&
                    !ContainsComponentNamed(transform, "SplatSceneryProvider"),
                "production prefab contains a legacy runtime scenery provider", failures);

            Require(bakedRendererCount > 0, "serialized bake renderer count is missing", failures);
            Require(bakedTriangleCount > 0, "serialized bake triangle count is missing", failures);
            Require(bakedMaterialCount > 0, "serialized bake material count is missing", failures);
            Require(bakedTextureCount > 0, "serialized bake texture count is missing", failures);
            Require(!string.IsNullOrWhiteSpace(bakeUtc), "serialized bake timestamp is missing", failures);
            Require(!string.IsNullOrWhiteSpace(bakeToolVersion), "serialized bake tool version is missing", failures);

            ProductionEnvironmentBudget actual = builtPlayerStaticBatchSafe
                ? new ProductionEnvironmentBudget(bakedRendererCount, bakedTriangleCount, bakedMaterialCount)
                : CalculateBudget();
            if (!builtPlayerStaticBatchSafe)
            {
                Require(bakedRendererCount == actual.rendererCount,
                    $"serialized renderer budget is stale ({bakedRendererCount} baked, {actual.rendererCount} authored)", failures);
                Require(bakedTriangleCount == actual.triangleCount,
                    $"serialized triangle budget is stale ({bakedTriangleCount} baked, {actual.triangleCount} authored)", failures);
                Require(bakedMaterialCount == actual.materialCount,
                    $"serialized material budget is stale ({bakedMaterialCount} baked, {actual.materialCount} authored)", failures);
            }
            Require(actual.rendererCount <= MaximumRenderers,
                $"renderer budget exceeded ({actual.rendererCount}/{MaximumRenderers})", failures);
            Require(actual.triangleCount <= MaximumVisibleTriangles,
                $"triangle budget exceeded ({actual.triangleCount}/{MaximumVisibleTriangles})", failures);
            Require(actual.materialCount <= MaximumMaterials,
                $"material budget exceeded ({actual.materialCount}/{MaximumMaterials})", failures);

            report = failures.Count == 0
                ? $"budgetSource={(builtPlayerStaticBatchSafe ? "serializedBake" : "authoredRenderers")}, " +
                  $"renderers={actual.rendererCount}, triangles={actual.triangleCount}, materials={actual.materialCount}, " +
                  $"runwayGap={measuredRunwayToTerrainMaximumGapMeters:0.0000}m, markingGap={measuredMarkingToRunwayMaximumGapMeters:0.0000}m"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }

        public ProductionEnvironmentBudget CalculateBudget()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer != null && renderer.enabled &&
                                   !renderer.forceRenderingOff && IsAuthoredActive(renderer.transform))
                .ToArray();
            AerialVegetationBatchRenderer[] vegetationBatches =
                GetComponentsInChildren<AerialVegetationBatchRenderer>(true)
                    .Where(batch => batch != null && batch.enabled &&
                                    IsAuthoredActive(batch.transform))
                    .ToArray();
            HashSet<Material> materials = new HashSet<Material>();
            long triangles = 0;
            foreach (Renderer renderer in renderers)
            {
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null) materials.Add(material);
            }
            foreach (AerialVegetationBatchRenderer batch in vegetationBatches)
            {
                if (batch.SharedMaterial != null && batch.NonEmptyDrawCount > 0)
                    materials.Add(batch.SharedMaterial);
                triangles += batch.FullyInstancedTriangleCount;
            }
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true)
                         .Where(filter => filter != null && IsAuthoredActive(filter.transform) &&
                                          filter.GetComponent<Renderer>() is Renderer renderer &&
                                          renderer.enabled && !renderer.forceRenderingOff))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    triangles += (long)mesh.GetIndexCount(subMesh) / 3L;
            }
            int authoredDrawSources = renderers.Length +
                                      vegetationBatches.Sum(batch => batch.NonEmptyDrawCount);
            return new ProductionEnvironmentBudget(
                authoredDrawSources,
                (int)Math.Min(int.MaxValue, triangles),
                materials.Count);
        }

        private bool IsAuthoredActive(Transform candidate)
        {
            for (Transform current = candidate; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == transform) return true;
            }
            return false;
        }

        private static bool HasMesh(MeshFilter filter) => filter != null && filter.sharedMesh != null;

        private static bool ContainsComponentNamed(Transform root, string typeName)
        {
            return root != null && root.GetComponentsInChildren<MonoBehaviour>(true).Any(component =>
                component != null && string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal));
        }

        private static void Require(bool condition, string failure, ICollection<string> failures)
        {
            if (!condition) failures.Add(failure);
        }
    }

    public readonly struct ProductionEnvironmentBudget
    {
        public readonly int rendererCount;
        public readonly int triangleCount;
        public readonly int materialCount;

        public ProductionEnvironmentBudget(int rendererCount, int triangleCount, int materialCount)
        {
            this.rendererCount = rendererCount;
            this.triangleCount = triangleCount;
            this.materialCount = materialCount;
        }
    }

}
