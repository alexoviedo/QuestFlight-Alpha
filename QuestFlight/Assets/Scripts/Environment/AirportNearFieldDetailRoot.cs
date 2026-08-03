using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Contract marker for the explicitly authored, bounded airport ground
    /// detail. It validates serialized assets and never constructs scenery.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AirportNearFieldDetailRoot : MonoBehaviour
    {
        public const string GameObjectName = "AirportNearFieldDetailRoot";
        public const int ExpectedStaticRendererCount = 1;
        public const int MaximumGrassInstances = 1023;
        public const int MaximumSelectedDraws = 2;
        public const int MaximumSelectedTriangles = 38000;

        public TextAsset sourceManifest;
        public Material sharedMaterial;
        public AirportNearFieldGrassRenderer grass;
        public MeshRenderer staticDetailRenderer;
        public int authoredGrassInstanceCount;
        public int authoredMaximumSelectedDraws;
        public int authoredMaximumSelectedTriangles;
        public int authoredStaticTriangleCount;

        public bool TryValidateContract(out string report)
        {
            List<string> failures = new List<string>();
            if (sourceManifest == null) failures.Add("source manifest is missing");
            if (sharedMaterial == null) failures.Add("shared material is missing");
            if (grass == null) failures.Add("grass renderer is missing");
            if (staticDetailRenderer == null)
                failures.Add("combined static-detail renderer is missing");
            if (authoredGrassInstanceCount < 800 ||
                authoredGrassInstanceCount > MaximumGrassInstances)
                failures.Add("grass instance count is outside the bounded contract");
            if (authoredMaximumSelectedDraws != MaximumSelectedDraws)
                failures.Add("selected draw contract is stale");
            if (authoredMaximumSelectedTriangles <= 0 ||
                authoredMaximumSelectedTriangles > MaximumSelectedTriangles)
                failures.Add("selected triangle contract failed");
            if (authoredStaticTriangleCount <= 0)
                failures.Add("static triangle count is missing");

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != ExpectedStaticRendererCount)
                failures.Add("near-field detail must have one static renderer");
            if (GetComponentsInChildren<LODGroup>(true).Length != 0)
                failures.Add("near-field detail must not use popping LODGroups");
            if (GetComponentsInChildren<Collider>(true).Length != 0)
                failures.Add("near-field detail must not add colliders");
            if (renderers.Any(renderer =>
                    renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterial != sharedMaterial))
                failures.Add("static detail does not reuse the shared material");
            if (staticDetailRenderer != null &&
                (staticDetailRenderer.shadowCastingMode != ShadowCastingMode.Off ||
                 staticDetailRenderer.receiveShadows))
                failures.Add("static detail must not add realtime shadows");
            if (grass != null)
            {
                if (grass.SharedMaterial != sharedMaterial)
                    failures.Add("grass does not reuse the shared material");
                if (grass.Instances == null ||
                    grass.Instances.Length != authoredGrassInstanceCount)
                    failures.Add("grass placement count is stale");
                if (grass.AuthoredMaximumDrawCount != 1)
                    failures.Add("grass must remain one draw");
            }

            report = failures.Count == 0
                ? $"grass={authoredGrassInstanceCount}, staticRenderers=1, " +
                  $"draws<={authoredMaximumSelectedDraws}, " +
                  $"triangles<={authoredMaximumSelectedTriangles}, " +
                  "materials=1, colliders=0, lodGroups=0"
                : string.Join("; ", failures.Distinct());
            return failures.Count == 0;
        }
    }
}
