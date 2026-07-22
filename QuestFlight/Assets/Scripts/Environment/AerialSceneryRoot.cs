using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Validation-only marker for the explicitly authored four-tier scenery root.
    /// It never constructs, repairs, moves, or selects product content at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AerialSceneryRoot : MonoBehaviour
    {
        public const string GameObjectName = "AerialSceneryRepresentationV1";
        public const int MaximumVegetationRenderers = 6;
        public const int MaximumVegetationMaterials = 3;
        public const int BaselineSerializedSceneTriangles = 687607;
        public const int MaximumSerializedSceneTriangles = 721988;
        public const int BaselineSelectedSceneTriangles = 503339;
        public const int MaximumSelectedSceneTriangles = 528506;
        public const int BaselineSelectedDrawCalls = 62;
        public const int BaselineVisibleMaterials = 24;

        [Header("Four authored tiers")]
        public Transform tier0AirportHero;
        public Transform tier1NearScenery;
        public Transform tier2MidScenery;
        public Transform tier3FarScenery;

        [Header("Authored representation roots")]
        public Transform vegetationRoot;
        public Transform buildingRoot;
        public Transform terrainRoot;
        public MeshFilter authoritativeAirportPatch;
        public Mesh baselineAirportPatchMesh;
        public MeshFilter authoritativeNearTerrain;
        public Mesh baselineNearTerrainMesh;
        public AerialSceneryManifest manifest;

        [Header("Explicitly superseded content")]
        public Transform[] disabledLegacyVegetationRoots = Array.Empty<Transform>();
        public Transform[] disabledLegacyBuildingRoots = Array.Empty<Transform>();

        [Header("Baked planning inventory")]
        public int bakedRendererCount;
        public int bakedSelectedTriangleCount;
        public int bakedMaterialCount;
        public int bakedVegetationRendererCount;
        public int bakedVegetationTriangleCount;
        public int bakedVegetationMaterialCount;
        public string bakeToolVersion;
        public string bakeUtc;
        public bool validateOnAwake = true;

        private void Awake()
        {
            if (!validateOnAwake) return;
            if (!TryValidateContract(out string report))
                Debug.LogError("[QuestFlight][AerialSceneryV1] Authored contract failed: " + report, this);
            else
                Debug.Log("[QuestFlight][AerialSceneryV1] Authored contract validated: " + report, this);
        }

        public bool TryValidateContract(out string report)
        {
            List<string> failures = new List<string>();
            Require(string.Equals(name, GameObjectName, StringComparison.Ordinal),
                "root name is not canonical", failures);
            Require(IsAuthoredChild(tier0AirportHero), "Tier 0 root is missing/outside the authored root", failures);
            Require(IsAuthoredChild(tier1NearScenery), "Tier 1 root is missing/outside the authored root", failures);
            Require(IsAuthoredChild(tier2MidScenery), "Tier 2 root is missing/outside the authored root", failures);
            Require(IsAuthoredChild(tier3FarScenery), "Tier 3 root is missing/outside the authored root", failures);
            Require(IsAuthoredChild(vegetationRoot), "vegetation root is missing/outside the authored root", failures);
            Require(IsAuthoredChild(buildingRoot), "building root is missing/outside the authored root", failures);
            Require(IsAuthoredChild(terrainRoot), "terrain root is missing/outside the authored root", failures);
            Require(manifest != null && manifest.TryValidate(out _), "serialized scenery manifest is incomplete", failures);
            Require(authoritativeAirportPatch != null && manifest != null &&
                    authoritativeAirportPatch.sharedMesh == manifest.airportPatchMesh,
                "authoritative airport visual patch is not bound to the conformed mesh", failures);
            Require(baselineAirportPatchMesh != null && manifest != null &&
                    baselineAirportPatchMesh != manifest.airportPatchMesh,
                "baseline airport-patch A/B witness is missing or aliases the conformed mesh", failures);
            Require(authoritativeNearTerrain != null && manifest != null &&
                    authoritativeNearTerrain.sharedMesh == manifest.nearTerrainMesh,
                "authoritative near-terrain filter is not bound to the lidar mesh", failures);
            Require(baselineNearTerrainMesh != null && manifest != null &&
                    baselineNearTerrainMesh != manifest.nearTerrainMesh,
                "baseline near-terrain A/B witness is missing or aliases the lidar mesh", failures);

            Renderer[] conventionalVegetationRenderers = vegetationRoot == null
                ? Array.Empty<Renderer>()
                : vegetationRoot.GetComponentsInChildren<Renderer>(true)
                    .Where(item => item.enabled && IsAuthoredActive(item.transform)).ToArray();
            AerialVegetationBatchRenderer[] vegetationBatches = vegetationRoot == null
                ? Array.Empty<AerialVegetationBatchRenderer>()
                : vegetationRoot.GetComponentsInChildren<AerialVegetationBatchRenderer>(true)
                    .Where(item => item.enabled && IsAuthoredActive(item.transform)).ToArray();
            int authoredBatchDraws = vegetationBatches.Sum(item => item.Batches.Count(batch =>
                batch.Mesh != null && batch.Matrices != null && batch.Matrices.Length > 0));
            int vegetationDrawCount = conventionalVegetationRenderers.Length + authoredBatchDraws;
            HashSet<Material> vegetationMaterials = new HashSet<Material>(
                conventionalVegetationRenderers.SelectMany(item => item.sharedMaterials)
                    .Where(item => item != null));
            foreach (AerialVegetationBatchRenderer batchRenderer in vegetationBatches)
                if (batchRenderer.SharedMaterial != null) vegetationMaterials.Add(batchRenderer.SharedMaterial);
            int vegetationMaterialCount = vegetationMaterials.Count;
            long vegetationTriangles = CountRendererTriangles(conventionalVegetationRenderers) +
                vegetationBatches.Sum(item => item.FullyInstancedTriangleCount);
            Require(vegetationDrawCount <= MaximumVegetationRenderers,
                $"vegetation draw count exceeds {MaximumVegetationRenderers} ({vegetationDrawCount})", failures);
            Require(vegetationMaterialCount <= MaximumVegetationMaterials,
                $"vegetation material count exceeds {MaximumVegetationMaterials} ({vegetationMaterialCount})", failures);
            Require(vegetationRoot == null ||
                    vegetationRoot.GetComponentsInChildren<BillboardRenderer>(true).Length == 0,
                "ordinary BillboardRenderer vegetation is prohibited", failures);

            IEnumerable<LODGroup> sceneryLods = new[] { vegetationRoot, buildingRoot }
                .Where(item => item != null)
                .SelectMany(item => item.GetComponentsInChildren<LODGroup>(true));
            Require(sceneryLods.All(item => item.fadeMode == LODFadeMode.None && !item.animateCrossFading),
                "XR scenery LODs must not use crossfade/dither", failures);
            Require(buildingRoot == null || buildingRoot.GetComponentsInChildren<Transform>(true).All(item =>
                    item.name.IndexOf("cluster_boxes", StringComparison.OrdinalIgnoreCase) < 0),
                "cell-sized cluster-box building geometry remains active", failures);

            Require(disabledLegacyVegetationRoots != null && disabledLegacyVegetationRoots.All(IsExplicitlyDisabled),
                "a superseded legacy vegetation root is active or missing", failures);
            Require(disabledLegacyBuildingRoots != null && disabledLegacyBuildingRoots.All(IsExplicitlyDisabled),
                "a superseded legacy building root is active or missing", failures);
            Require(bakedRendererCount > 0 && bakedSelectedTriangleCount > 0 && bakedMaterialCount > 0,
                "baked planning inventory is missing", failures);
            Require(bakedVegetationRendererCount == vegetationDrawCount,
                "baked vegetation draw count is stale", failures);
            Require(bakedVegetationTriangleCount == vegetationTriangles,
                $"baked vegetation triangle count is stale " +
                $"(baked={bakedVegetationTriangleCount:N0}, authored={vegetationTriangles:N0})", failures);
            Require(bakedVegetationMaterialCount == vegetationMaterialCount,
                "baked vegetation material count is stale", failures);
            Require(!string.IsNullOrWhiteSpace(bakeToolVersion) && !string.IsNullOrWhiteSpace(bakeUtc),
                "bake identity is missing", failures);

            report = failures.Count == 0
                ? $"PASS renderers={bakedRendererCount:N0} selectedTriangles={bakedSelectedTriangleCount:N0} " +
                  $"vegetation={bakedVegetationRendererCount:N0}/{bakedVegetationTriangleCount:N0}/" +
                  $"{bakedVegetationMaterialCount:N0}"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }

        private bool IsAuthoredChild(Transform value)
        {
            return value != null && (value == transform || value.IsChildOf(transform));
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

        private static long CountRendererTriangles(IEnumerable<Renderer> renderers)
        {
            long triangles = 0;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;

                // In a Player build Unity replaces each statically batched MeshFilter mesh
                // with the shared combined mesh. Counting every submesh for every renderer
                // therefore multiplies the authored inventory. MeshRenderer exposes the
                // first submesh owned by that renderer; its material-slot count bounds the
                // contiguous slice. Editor/source validation still takes the ordinary full-
                // mesh path and pins the exact authored triangle hashes.
                int firstSubMesh = renderer is MeshRenderer meshRenderer &&
                                   meshRenderer.isPartOfStaticBatch
                    ? meshRenderer.subMeshStartIndex
                    : 0;
                int subMeshCount = renderer is MeshRenderer staticMeshRenderer &&
                                   staticMeshRenderer.isPartOfStaticBatch
                    ? Mathf.Max(1, staticMeshRenderer.sharedMaterials.Length)
                    : mesh.subMeshCount;
                int endSubMesh = Mathf.Min(mesh.subMeshCount, firstSubMesh + subMeshCount);
                for (int index = firstSubMesh; index < endSubMesh; index++)
                    if (mesh.GetTopology(index) == MeshTopology.Triangles)
                        triangles += (long)mesh.GetIndexCount(index) / 3L;
            }
            return triangles;
        }

        private static bool IsExplicitlyDisabled(Transform value)
        {
            return value != null && !value.gameObject.activeSelf;
        }

        private static void Require(bool condition, string failure, ICollection<string> failures)
        {
            if (!condition) failures.Add(failure);
        }
    }
}
