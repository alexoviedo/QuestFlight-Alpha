using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Draws a fixed, serialized set of vegetation instance batches without
    /// constructing runtime hierarchy or creating one GameObject per tree.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class AerialVegetationBatchRenderer : MonoBehaviour
    {
        public const int MaximumInstancesPerDraw = 1023;

        [Serializable]
        public struct Batch
        {
            [SerializeField] private string batchId;
            [SerializeField] private int tier;
            [SerializeField] private Mesh mesh;
            [SerializeField] private Matrix4x4[] matrices;
            [SerializeField] private Vector4[] instanceTints;

            public string BatchId => batchId;
            public int Tier => tier;
            public Mesh Mesh => mesh;
            public Matrix4x4[] Matrices => matrices;
            public Vector4[] InstanceTints => instanceTints;

            public Batch(string id, int representationTier, Mesh sourceMesh, Matrix4x4[] sourceMatrices, Vector4[] tints)
            {
                batchId = id;
                tier = representationTier;
                mesh = sourceMesh;
                matrices = sourceMatrices ?? Array.Empty<Matrix4x4>();
                instanceTints = tints ?? Array.Empty<Vector4>();
            }
        }

        private static readonly int InstanceTintId = Shader.PropertyToID("_InstanceTint");

        [SerializeField] private Material sharedMaterial;
        [SerializeField] private TextAsset distributionManifest;
        [SerializeField] private Batch[] batches = Array.Empty<Batch>();
        [SerializeField] private int authoredSourcePlacementCount;
        [SerializeField] private int authoredDrawCount;
        [SerializeField] private long authoredFullyInstancedTriangleCount;
        [SerializeField] private int authoredMaterialCount;
        [SerializeField] private bool fixedOrientationOnly = true;
        [SerializeField] private bool cameraFacingBehavior;
        [SerializeField] private bool lodCrossfadeBehavior;

        private MaterialPropertyBlock[] propertyBlocks = Array.Empty<MaterialPropertyBlock>();

        public Material SharedMaterial => sharedMaterial;
        public TextAsset DistributionManifest => distributionManifest;
        public Batch[] Batches => batches;
        public int AuthoredSourcePlacementCount => authoredSourcePlacementCount;
        public int AuthoredDrawCount => authoredDrawCount;
        public long AuthoredFullyInstancedTriangleCount => authoredFullyInstancedTriangleCount;
        public int AuthoredMaterialCount => authoredMaterialCount;
        public bool FixedOrientationOnly => fixedOrientationOnly;
        public bool CameraFacingBehavior => cameraFacingBehavior;
        public bool LodCrossfadeBehavior => lodCrossfadeBehavior;

        /// <summary>Allocation-free count used by product budget audits.</summary>
        public int NonEmptyDrawCount
        {
            get
            {
                int count = 0;
                if (batches == null)
                {
                    return count;
                }
                for (int index = 0; index < batches.Length; index++)
                {
                    Batch batch = batches[index];
                    if (batch.Mesh != null && batch.Matrices != null && batch.Matrices.Length > 0)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>Allocation-free mesh triangles times submitted matrix count.</summary>
        public long FullyInstancedTriangleCount
        {
            get
            {
                long count = 0;
                if (batches == null)
                {
                    return count;
                }
                for (int index = 0; index < batches.Length; index++)
                {
                    Batch batch = batches[index];
                    Matrix4x4[] matrices = batch.Matrices;
                    if (batch.Mesh == null || matrices == null || matrices.Length == 0 || batch.Mesh.subMeshCount == 0)
                    {
                        continue;
                    }
                    count += (long)(batch.Mesh.GetIndexCount(0) / 3u) * matrices.Length;
                }
                return count;
            }
        }

        public void Configure(Material material, TextAsset distribution, Batch[] authoredBatches, int sourcePlacementCount)
        {
            sharedMaterial = material;
            distributionManifest = distribution;
            batches = authoredBatches ?? Array.Empty<Batch>();
            authoredSourcePlacementCount = sourcePlacementCount;
            authoredDrawCount = NonEmptyDrawCount;
            authoredFullyInstancedTriangleCount = FullyInstancedTriangleCount;
            authoredMaterialCount = sharedMaterial != null && authoredDrawCount > 0 ? 1 : 0;
            fixedOrientationOnly = true;
            cameraFacingBehavior = false;
            lodCrossfadeBehavior = false;
            PreparePropertyBlocks();
        }

        private void OnEnable()
        {
            Camera.onPreCull -= DrawForCamera;
            Camera.onPreCull += DrawForCamera;
            PreparePropertyBlocks();
        }

        private void OnDisable()
        {
            Camera.onPreCull -= DrawForCamera;
            propertyBlocks = Array.Empty<MaterialPropertyBlock>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            PreparePropertyBlocks();
        }
#endif

        private void PreparePropertyBlocks()
        {
            if (batches == null)
            {
                batches = Array.Empty<Batch>();
            }

            propertyBlocks = new MaterialPropertyBlock[batches.Length];
            for (int index = 0; index < batches.Length; index++)
            {
                Batch batch = batches[index];
                Matrix4x4[] matrices = batch.Matrices ?? Array.Empty<Matrix4x4>();
                Vector4[] tints = batch.InstanceTints ?? Array.Empty<Vector4>();
                if (matrices.Length > MaximumInstancesPerDraw)
                {
                    Debug.LogError($"{batch.BatchId} exceeds the {MaximumInstancesPerDraw}-instance draw contract.", this);
                    continue;
                }

                if (matrices.Length != tints.Length)
                {
                    Debug.LogError($"{batch.BatchId} has {matrices.Length} matrices but {tints.Length} tints.", this);
                    continue;
                }

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                if (tints.Length > 0)
                {
                    block.SetVectorArray(InstanceTintId, tints);
                }
                propertyBlocks[index] = block;
            }
        }

        private void DrawForCamera(Camera camera)
        {
            if (camera == null || !enabled || !gameObject.activeInHierarchy || !gameObject.scene.IsValid())
            {
                return;
            }

            if (sharedMaterial == null || batches == null || propertyBlocks.Length != batches.Length)
            {
                return;
            }

            int layer = gameObject.layer;
            if ((camera.cullingMask & (1 << layer)) == 0)
            {
                return;
            }

            for (int index = 0; index < batches.Length; index++)
            {
                Batch batch = batches[index];
                Matrix4x4[] matrices = batch.Matrices;
                if (batch.Mesh == null || matrices == null || matrices.Length == 0 || propertyBlocks[index] == null)
                {
                    continue;
                }

                Graphics.DrawMeshInstanced(
                    batch.Mesh,
                    0,
                    sharedMaterial,
                    matrices,
                    matrices.Length,
                    propertyBlocks[index],
                    ShadowCastingMode.Off,
                    false,
                    layer,
                    camera,
                    LightProbeUsage.Off,
                    null);
            }
        }
    }
}
