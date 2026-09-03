using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Validation-only marker for the explicitly authored semantic tower batch.
    /// It never creates, moves, or repairs product geometry.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SemanticTowerRoot : MonoBehaviour
    {
        public const string RootName = "SemanticTowerRoot";
        public TextAsset authorityManifest;
        public LODGroup[] structureLodGroups = Array.Empty<LODGroup>();
        public MeshRenderer[] authoredRenderers = Array.Empty<MeshRenderer>();
        public int representedStructureCount;
        public int excludedBuildingShellCount;
        public int authoredObstructionLightCount;
        public int authoredObstructionLightSystemCount;
        public int authoredObstructionFixtureCount;
        public int bakedLod0TriangleCount;
        public int bakedRendererCount;
        public int bakedMaterialCount;

        public bool TryValidateContract(out string report)
        {
            List<string> failures = new List<string>();
            Require(string.Equals(name, RootName, StringComparison.Ordinal), "root name is not canonical", failures);
            Require(authorityManifest != null, "authority manifest is missing", failures);
            Require(representedStructureCount == 2, "expected two represented structures", failures);
            Require(excludedBuildingShellCount == 1, "expected one exact building-shell exclusion", failures);
            Require(authoredObstructionLightCount == 0, "authority does not permit obstruction lights", failures);
            Require(authoredObstructionLightSystemCount == 1,
                "expected one FCC-authorized red obstruction-light system", failures);
            Require(authoredObstructionFixtureCount == 3,
                "expected one top and two midpoint baked red fixtures", failures);
            Require(GetComponentsInChildren<Light>(true).Length == 0, "unauthorized light component is present", failures);
            Require(structureLodGroups != null && structureLodGroups.Length == representedStructureCount,
                "structure LOD groups are incomplete", failures);
            Require(authoredRenderers != null && authoredRenderers.Length == representedStructureCount * 2,
                "each structure must carry exactly two serialized LOD renderers", failures);
            Require(authoredRenderers != null && authoredRenderers.All(renderer =>
                    renderer != null && renderer.enabled && renderer.sharedMaterial != null &&
                    renderer.GetComponent<MeshFilter>()?.sharedMesh != null &&
                    renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows),
                "a tower renderer is missing its mesh/material or uses unsupported shadows", failures);
            Require(authoredRenderers == null || authoredRenderers.All(renderer => renderer.gameObject.isStatic),
                "tower renderers are not marked static", failures);
            Require(GetComponentsInChildren<MonoBehaviour>(true).Length == 1,
                "per-structure runtime behaviours are forbidden", failures);
            Require(bakedLod0TriangleCount > 0 && bakedLod0TriangleCount <= 10000,
                "LOD0 triangle budget is missing or excessive", failures);
            Require(bakedRendererCount == authoredRenderers?.Length,
                "serialized renderer count is stale", failures);
            Require(bakedMaterialCount == 1,
                "semantic tower material budget is invalid", failures);

            report = failures.Count == 0
                ? $"PASS structures={representedStructureCount} exclusions={excludedBuildingShellCount} " +
                  $"lod0Triangles={bakedLod0TriangleCount} renderers={bakedRendererCount} " +
                  $"materials={bakedMaterialCount} shippingActiveLights={authoredObstructionLightCount} " +
                  $"authoredLightSystems={authoredObstructionLightSystemCount} fixtures={authoredObstructionFixtureCount}"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }

        private static void Require(bool condition, string message, ICollection<string> failures)
        {
            if (!condition) failures.Add(message);
        }
    }
}
