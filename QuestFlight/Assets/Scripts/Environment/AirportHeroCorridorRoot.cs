using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Serialized marker and fail-closed contract for the near-airport hero-building corridor.
    /// It never creates or repairs runtime content.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AirportHeroCorridorRoot : MonoBehaviour
    {
        public const string GameObjectName = "AirportHeroCorridorRoot";
        public const int ExpectedBuildingCount = 81;

        public LODGroup corridorLodGroup;
        public TextAsset sourceManifest;
        public string[] representedAddresses = Array.Empty<string>();
        public long[] representedOsmIds = Array.Empty<long>();
        public int lod0Triangles;
        public int lod1Triangles;
        public int lod2Triangles;
        public bool usesExistingSharedAtlas;
        public bool genericRepresentationsExcluded;

        public bool TryValidate(out string report)
        {
            if (!string.Equals(name, GameObjectName, StringComparison.Ordinal))
            {
                report = "hero corridor root name changed";
                return false;
            }
            if (corridorLodGroup == null || corridorLodGroup.transform != transform)
            {
                report = "hero corridor LODGroup binding is missing";
                return false;
            }
            LOD[] lods = corridorLodGroup.GetLODs();
            if (lods.Length != 1 || lods.Any(lod =>
                    lod.renderers == null || lod.renderers.Length != 1 || lod.renderers[0] == null))
            {
                report = "hero corridor must have one fixed renderer with bounded distance culling";
                return false;
            }
            if (lods.SelectMany(lod => lod.renderers).Any(renderer =>
                    renderer.sharedMaterials == null || renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterial == null ||
                    renderer.shadowCastingMode == ShadowCastingMode.TwoSided))
            {
                report = "hero corridor renderer/material contract is invalid";
                return false;
            }
            if (sourceManifest == null || representedAddresses == null ||
                representedAddresses.Length != ExpectedBuildingCount ||
                representedOsmIds == null || representedOsmIds.Length != ExpectedBuildingCount ||
                representedOsmIds.Distinct().Count() != ExpectedBuildingCount)
            {
                report = "hero corridor source/address inventory is incomplete";
                return false;
            }
            if (lod0Triangles <= 0 || lod1Triangles <= 0 || lod2Triangles <= 0 ||
                lod0Triangles < lod1Triangles || lod1Triangles < lod2Triangles)
            {
                report = "hero corridor LOD triangle progression is invalid";
                return false;
            }
            if (!usesExistingSharedAtlas || !genericRepresentationsExcluded)
            {
                report = "hero corridor atlas or ownership-exclusion contract is false";
                return false;
            }
            if (GetComponentsInChildren<Camera>(true).Length != 0 ||
                GetComponentsInChildren<Light>(true).Length != 0 ||
                GetComponentsInChildren<Rigidbody>(true).Length != 0)
            {
                report = "hero corridor introduced a forbidden runtime component";
                return false;
            }
            report = $"PASS_AIRPORT_HERO_CORRIDOR_V1 addresses={representedAddresses.Length} " +
                     $"triangles={lod0Triangles}/{lod1Triangles}/{lod2Triangles}";
            return true;
        }
    }
}
