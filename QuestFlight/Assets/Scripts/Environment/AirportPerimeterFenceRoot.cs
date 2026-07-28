using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Validation marker for the explicitly authored KBDU perimeter fence.
    /// It owns no runtime construction or update path.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AirportPerimeterFenceRoot : MonoBehaviour
    {
        public const string GameObjectName = "AirportPerimeterFenceRoot";
        public const int ExpectedSectionCount = 4;
        public const int MaximumSelectedDraws = 4;
        public const int MaximumSelectedTriangles = 262;
        public const int MaximumSerializedTriangles = 262;

        public TextAsset sourceManifest;
        public Material sharedMaterial;
        public MeshRenderer[] sections = Array.Empty<MeshRenderer>();
        public float authoredPerimeterLengthMeters;
        public int authoredMaximumSelectedTriangles;
        public int authoredSerializedTriangles;
        public int authoredRendererCount;

        public bool TryValidateContract(out string report)
        {
            List<string> failures = new List<string>();
            if (sourceManifest == null) failures.Add("source manifest is missing");
            if (sharedMaterial == null) failures.Add("shared material is missing");
            if (sections == null || sections.Length != ExpectedSectionCount)
                failures.Add($"expected {ExpectedSectionCount} fence sections");
            if (authoredPerimeterLengthMeters < 3000f || authoredPerimeterLengthMeters > 6000f)
                failures.Add("perimeter length is implausible");
            if (authoredMaximumSelectedTriangles <= 0 ||
                authoredMaximumSelectedTriangles > MaximumSelectedTriangles)
                failures.Add("selected triangle contract failed");
            if (authoredSerializedTriangles <= 0 ||
                authoredSerializedTriangles > MaximumSerializedTriangles)
                failures.Add("serialized triangle contract failed");

            LODGroup[] actualGroups = GetComponentsInChildren<LODGroup>(true);
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            if (actualGroups.Length != 0)
                failures.Add("airport fence must not use LOD switching");
            if (renderers.Length != ExpectedSectionCount)
                failures.Add("fence renderer count changed");
            if (authoredRendererCount != renderers.Length)
                failures.Add("authored renderer count is stale");
            if (colliders.Length != 0)
                failures.Add("airport fence must not add colliders");

            foreach (Renderer renderer in renderers)
            {
                if (renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterial != sharedMaterial)
                    failures.Add($"{renderer.name} does not reuse the shared airport material");
                if (renderer.shadowCastingMode != ShadowCastingMode.Off || renderer.receiveShadows)
                    failures.Add($"{renderer.name} must not cast or receive realtime fence shadows");
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || mesh.subMeshCount != 1)
                    failures.Add($"{renderer.name} has no single-submesh fence mesh");
            }

            report = failures.Count == 0
                ? $"sections={ExpectedSectionCount}, fixedRenderers={renderers.Length}, " +
                  $"selectedTriangles<={authoredMaximumSelectedTriangles}, " +
                  $"serializedTriangles={authoredSerializedTriangles}, materials=1(reused), " +
                  $"draws<={MaximumSelectedDraws}, lodSwitching=0, colliders=0"
                : string.Join("; ", failures.Distinct());
            return failures.Count == 0;
        }
    }
}
