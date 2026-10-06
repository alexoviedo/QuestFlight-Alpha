using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Draws the fixed, serialized airport grass-clump set as one bounded
    /// instanced submission. Visibility is evaluated per camera without
    /// creating runtime objects or allocating managed memory after enable.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class AirportNearFieldGrassRenderer : MonoBehaviour
    {
        public const int MaximumInstances = 1023;
        public const int MaximumDrawCount = 1;

        [Serializable]
        public struct Instance
        {
            [SerializeField] private Matrix4x4 matrix;
            [SerializeField] private Vector4 tint;
            [SerializeField] private Vector3 center;
            [SerializeField] private float radius;

            public Matrix4x4 Matrix => matrix;
            public Vector4 Tint => tint;
            public Vector3 Center => center;
            public float Radius => radius;

            public Instance(
                Matrix4x4 authoredMatrix,
                Vector4 authoredTint,
                Vector3 authoredCenter,
                float authoredRadius)
            {
                matrix = authoredMatrix;
                tint = authoredTint;
                center = authoredCenter;
                radius = authoredRadius;
            }
        }

        private static readonly int InstanceTintId =
            Shader.PropertyToID("_InstanceTint");

        [SerializeField] private Mesh sharedClumpMesh;
        [SerializeField] private Material sharedMaterial;
        [SerializeField] private TextAsset sourceManifest;
        [SerializeField] private Instance[] instances = Array.Empty<Instance>();
        [SerializeField] private float maximumDistanceMeters = 340f;
        [SerializeField] private int authoredMaximumDrawCount;
        [SerializeField] private long authoredMaximumTriangleCount;

        private readonly Matrix4x4[] visibleMatrices =
            new Matrix4x4[MaximumInstances];
        private readonly Vector4[] visibleTints =
            new Vector4[MaximumInstances];
        private readonly Plane[] frustumPlanes = new Plane[6];
        private MaterialPropertyBlock properties;

        public Mesh SharedClumpMesh => sharedClumpMesh;
        public Material SharedMaterial => sharedMaterial;
        public TextAsset SourceManifest => sourceManifest;
        public Instance[] Instances => instances;
        public float MaximumDistanceMeters => maximumDistanceMeters;
        public int AuthoredMaximumDrawCount => authoredMaximumDrawCount;
        public long AuthoredMaximumTriangleCount => authoredMaximumTriangleCount;

        public void Configure(
            Mesh mesh,
            Material material,
            TextAsset manifest,
            Instance[] authoredInstances)
        {
            sharedClumpMesh = mesh;
            sharedMaterial = material;
            sourceManifest = manifest;
            instances = authoredInstances ?? Array.Empty<Instance>();
            maximumDistanceMeters = 340f;
            authoredMaximumDrawCount =
                instances.Length > 0 ? MaximumDrawCount : 0;
            authoredMaximumTriangleCount =
                sharedClumpMesh == null || sharedClumpMesh.subMeshCount == 0
                    ? 0L
                    : (long)(sharedClumpMesh.GetIndexCount(0) / 3u) *
                      instances.Length;
            Prepare();
        }

        private void OnEnable()
        {
            Camera.onPreCull -= DrawForCamera;
            QuestFlight.Rendering.ProductRenderSubmission.Draw -= DrawForCamera;
            Camera.onPreCull += DrawForCamera;
            QuestFlight.Rendering.ProductRenderSubmission.Draw += DrawForCamera;
            Prepare();
        }

        private void OnDisable()
        {
            Camera.onPreCull -= DrawForCamera;
            QuestFlight.Rendering.ProductRenderSubmission.Draw -= DrawForCamera;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Prepare();
        }
#endif

        private void Prepare()
        {
            properties ??= new MaterialPropertyBlock();
            if (instances == null)
                instances = Array.Empty<Instance>();
            if (instances.Length > MaximumInstances)
                Debug.LogError(
                    $"Airport grass has {instances.Length} instances; " +
                    $"maximum is {MaximumInstances}.",
                    this);
        }

        private void DrawForCamera(Camera camera)
        {
            if(QuestFlight.Rendering.ProductRenderSubmission.Preparing)return;
            if (camera == null || !enabled || !gameObject.activeInHierarchy ||
                !gameObject.scene.IsValid() || sharedClumpMesh == null ||
                sharedMaterial == null || instances == null ||
                instances.Length == 0 ||
                instances.Length > MaximumInstances)
                return;
            if (camera.cameraType == CameraType.Preview ||
                camera.cameraType == CameraType.Reflection)
                return;

            int layer = gameObject.layer;
            if ((camera.cullingMask & (1 << layer)) == 0)
                return;

            GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
            Vector3 cameraPosition = camera.transform.position;
            float maximumDistanceSquared =
                maximumDistanceMeters * maximumDistanceMeters;
            int count = 0;
            for (int index = 0; index < instances.Length; index++)
            {
                Instance instance = instances[index];
                Vector3 offset = instance.Center - cameraPosition;
                if (offset.sqrMagnitude > maximumDistanceSquared)
                    continue;
                Bounds bounds = new Bounds(
                    instance.Center,
                    Vector3.one * (instance.Radius * 2f));
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, bounds))
                    continue;
                visibleMatrices[count] = instance.Matrix;
                visibleTints[count] = instance.Tint;
                count++;
            }

            if (count == 0)
                return;
            properties.SetVectorArray(InstanceTintId, visibleTints);
            QuestFlight.Rendering.ProductRenderSubmission.DrawMeshInstanced(
                sharedClumpMesh,
                0,
                sharedMaterial,
                visibleMatrices,
                count,
                properties,
                ShadowCastingMode.Off,
                false,
                layer,
                camera,
                LightProbeUsage.Off,
                null);
        }
    }
}
