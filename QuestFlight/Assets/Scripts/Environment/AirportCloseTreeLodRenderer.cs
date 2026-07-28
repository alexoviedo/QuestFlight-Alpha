using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Draws the explicitly authored airport tree set without per-tree
    /// GameObjects. Near trees retain source foliage, the reduced tier is a
    /// volumetric clump mesh, and the far tier is one world-upright multiview
    /// impostor selected from the XR center-eye pose.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class AirportCloseTreeLodRenderer : MonoBehaviour
    {
        public const int MaximumLod0Instances = 6;
        public const int MaximumLod1Instances = 28;
        public const int MaximumLod2Instances = 696;
        public const int MaximumDrawCount = 3;
        public const int ImpostorFrameCount = 8;

        private const byte Lod0 = 0;
        private const byte Lod1 = 1;
        private const byte Lod2 = 2;
        private const byte Culled = 3;
        private const byte Uninitialized = byte.MaxValue;

        private const int Lod0ArrayCapacity = MaximumLod0Instances * 2;
        private const int Lod1ArrayCapacity =
            MaximumLod1Instances + MaximumLod0Instances + MaximumLod1Instances;
        private const int Lod2ArrayCapacity =
            MaximumLod2Instances + MaximumLod1Instances;

        [Serializable]
        public struct Instance
        {
            [SerializeField] private Matrix4x4 matrix;
            [SerializeField] private Vector4 tint;
            [SerializeField] private Vector3 center;
            [SerializeField] private Vector3 position;
            [SerializeField] private Vector3 scale;
            [SerializeField] private float radius;
            [SerializeField] private float rotationDegrees;

            public Matrix4x4 Matrix => matrix;
            public Vector4 Tint => tint;
            public Vector3 Center => center;
            public Vector3 Position => position;
            public Vector3 Scale => scale;
            public float Radius => radius;
            public float RotationDegrees => rotationDegrees;

            public Instance(
                Matrix4x4 authoredMatrix,
                Vector4 authoredTint,
                Vector3 authoredCenter,
                Vector3 authoredPosition,
                Vector3 authoredScale,
                float authoredRadius,
                float authoredRotationDegrees)
            {
                matrix = authoredMatrix;
                tint = authoredTint;
                center = authoredCenter;
                position = authoredPosition;
                scale = authoredScale;
                radius = authoredRadius;
                rotationDegrees = authoredRotationDegrees;
            }
        }

        private static readonly int InstanceTintId = Shader.PropertyToID("_InstanceTint");
        private static readonly int InstanceFadeId = Shader.PropertyToID("_InstanceFade");
        private static readonly int ImpostorFrameId = Shader.PropertyToID("_ImpostorFrame");

        [SerializeField] private Material sharedCanopyMaterial;
        [SerializeField] private Mesh lod0Mesh;
        [SerializeField] private Mesh lod1Mesh;
        [SerializeField] private Mesh lod2Mesh;
        [SerializeField] private TextAsset placementManifest;
        [SerializeField] private TextAsset sourceLicenseManifest;
        [SerializeField] private Instance[] instances = Array.Empty<Instance>();
        [SerializeField] private float lod0EnterScreenPixels = 105f;
        [SerializeField] private float lod0ExitScreenPixels = 76f;
        [SerializeField] private float lod1EnterScreenPixels = 34f;
        [SerializeField] private float lod1ExitScreenPixels = 22f;
        [SerializeField] private float maximumDistanceMeters = 650f;
        [SerializeField] private float transitionDurationSeconds = 0.32f;
        [SerializeField] private float minimumResidencySeconds = 0.75f;
        [SerializeField] private float impostorAngularHysteresisDegrees = 6f;
        [SerializeField] private int authoredMaximumDrawCount;
        [SerializeField] private long authoredMaximumSelectedTriangleCount;
        [SerializeField] private bool projectedScreenSizeSelection = true;
        [SerializeField] private bool persistentHysteresis = true;
        [SerializeField] private bool centerEyeWorldUprightImpostor = true;
        [SerializeField] private bool perEyeFacingBehavior;
        [SerializeField] private bool alphaToCoverageTransition = true;

        private readonly Matrix4x4[] lod0Matrices =
            new Matrix4x4[Lod0ArrayCapacity];
        private readonly Matrix4x4[] lod1Matrices =
            new Matrix4x4[Lod1ArrayCapacity];
        private readonly Matrix4x4[] lod2Matrices =
            new Matrix4x4[Lod2ArrayCapacity];
        private readonly Vector4[] lod0Tints = new Vector4[Lod0ArrayCapacity];
        private readonly Vector4[] lod1Tints = new Vector4[Lod1ArrayCapacity];
        private readonly Vector4[] lod2Tints = new Vector4[Lod2ArrayCapacity];
        private readonly float[] lod0Fades = new float[Lod0ArrayCapacity];
        private readonly float[] lod1Fades = new float[Lod1ArrayCapacity];
        private readonly float[] lod2Fades = new float[Lod2ArrayCapacity];
        private readonly float[] lod0Frames = new float[Lod0ArrayCapacity];
        private readonly float[] lod1Frames = new float[Lod1ArrayCapacity];
        private readonly float[] lod2Frames = new float[Lod2ArrayCapacity];
        private readonly Plane[] frustumPlanes = new Plane[6];

        private bool[] visible = Array.Empty<bool>();
        private bool[] selectedLod0 = Array.Empty<bool>();
        private bool[] selectedLod1 = Array.Empty<bool>();
        private float[] projectedPixels = Array.Empty<float>();
        private float[] priorities = Array.Empty<float>();
        private byte[] desiredLods = Array.Empty<byte>();
        private byte[] currentLods = Array.Empty<byte>();
        private byte[] previousLods = Array.Empty<byte>();
        private byte[] impostorFrames = Array.Empty<byte>();
        private float[] transitionStarts = Array.Empty<float>();
        private float[] lastStateChanges = Array.Empty<float>();

        private MaterialPropertyBlock lod0Properties;
        private MaterialPropertyBlock lod1Properties;
        private MaterialPropertyBlock lod2Properties;

        public Material SharedCanopyMaterial => sharedCanopyMaterial;
        public Mesh Lod0Mesh => lod0Mesh;
        public Mesh Lod1Mesh => lod1Mesh;
        public Mesh Lod2Mesh => lod2Mesh;
        public TextAsset PlacementManifest => placementManifest;
        public TextAsset SourceLicenseManifest => sourceLicenseManifest;
        public Instance[] Instances => instances;
        public float Lod0EnterScreenPixels => lod0EnterScreenPixels;
        public float Lod0ExitScreenPixels => lod0ExitScreenPixels;
        public float Lod1EnterScreenPixels => lod1EnterScreenPixels;
        public float Lod1ExitScreenPixels => lod1ExitScreenPixels;
        public float MaximumDistanceMeters => maximumDistanceMeters;
        public float TransitionDurationSeconds => transitionDurationSeconds;
        public float MinimumResidencySeconds => minimumResidencySeconds;
        public int AuthoredMaximumDrawCount => authoredMaximumDrawCount;
        public long AuthoredMaximumSelectedTriangleCount =>
            authoredMaximumSelectedTriangleCount;
        public bool ProjectedScreenSizeSelection => projectedScreenSizeSelection;
        public bool PersistentHysteresis => persistentHysteresis;
        public bool CenterEyeWorldUprightImpostor => centerEyeWorldUprightImpostor;
        public bool PerEyeFacingBehavior => perEyeFacingBehavior;
        public bool AlphaToCoverageTransition => alphaToCoverageTransition;

        // Compatibility properties make the intentional V1-to-V2 policy change
        // explicit to repository validators and temporal evidence.
        public bool FixedWorldOrientationOnly => false;
        public bool CameraFacingBehavior => centerEyeWorldUprightImpostor;
        public bool LodCrossfadeOrDither => alphaToCoverageTransition;

        public void Configure(
            Material material,
            Mesh nearMesh,
            Mesh middleMesh,
            Mesh farMesh,
            TextAsset placements,
            TextAsset sourceManifest,
            Instance[] authoredInstances)
        {
            sharedCanopyMaterial = material;
            lod0Mesh = nearMesh;
            lod1Mesh = middleMesh;
            lod2Mesh = farMesh;
            placementManifest = placements;
            sourceLicenseManifest = sourceManifest;
            instances = authoredInstances ?? Array.Empty<Instance>();
            lod0EnterScreenPixels = 105f;
            lod0ExitScreenPixels = 76f;
            lod1EnterScreenPixels = 34f;
            lod1ExitScreenPixels = 22f;
            maximumDistanceMeters = 650f;
            transitionDurationSeconds = 0.32f;
            minimumResidencySeconds = 0.75f;
            impostorAngularHysteresisDegrees = 6f;
            projectedScreenSizeSelection = true;
            persistentHysteresis = true;
            centerEyeWorldUprightImpostor = true;
            perEyeFacingBehavior = false;
            alphaToCoverageTransition = true;
            authoredMaximumDrawCount = MaximumDrawCount;
            authoredMaximumSelectedTriangleCount =
                (long)TriangleCount(lod0Mesh) * MaximumLod0Instances +
                (long)TriangleCount(lod1Mesh) * MaximumLod1Instances +
                (long)TriangleCount(lod2Mesh) * MaximumLod2Instances +
                (long)TriangleCount(lod1Mesh) * MaximumLod0Instances +
                (long)TriangleCount(lod2Mesh) * MaximumLod1Instances;
            Prepare();
        }

        private static int TriangleCount(Mesh mesh)
        {
            return mesh == null || mesh.subMeshCount == 0
                ? 0
                : checked((int)(mesh.GetIndexCount(0) / 3u));
        }

        private void OnEnable()
        {
            Camera.onPreCull -= DrawForCamera;
            Camera.onPreCull += DrawForCamera;
            Prepare();
        }

        private void OnDisable()
        {
            Camera.onPreCull -= DrawForCamera;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Prepare();
        }
#endif

        private void Prepare()
        {
            lod0Properties ??= new MaterialPropertyBlock();
            lod1Properties ??= new MaterialPropertyBlock();
            lod2Properties ??= new MaterialPropertyBlock();
            int count = instances == null ? 0 : instances.Length;
            if (currentLods.Length == count)
                return;

            visible = new bool[count];
            selectedLod0 = new bool[count];
            selectedLod1 = new bool[count];
            projectedPixels = new float[count];
            priorities = new float[count];
            desiredLods = new byte[count];
            currentLods = new byte[count];
            previousLods = new byte[count];
            impostorFrames = new byte[count];
            transitionStarts = new float[count];
            lastStateChanges = new float[count];
            for (int index = 0; index < count; index++)
            {
                desiredLods[index] = Culled;
                currentLods[index] = Uninitialized;
                previousLods[index] = Culled;
                impostorFrames[index] = 0;
                transitionStarts[index] = float.NegativeInfinity;
                lastStateChanges[index] = float.NegativeInfinity;
            }
        }

        private void DrawForCamera(Camera camera)
        {
            if (camera == null || !enabled || !gameObject.activeInHierarchy ||
                !gameObject.scene.IsValid() || sharedCanopyMaterial == null ||
                lod0Mesh == null || lod1Mesh == null || lod2Mesh == null ||
                instances == null || instances.Length == 0)
                return;
            if (camera.cameraType == CameraType.Preview ||
                camera.cameraType == CameraType.Reflection)
                return;
            int layer = gameObject.layer;
            if ((camera.cullingMask & (1 << layer)) == 0)
                return;

            Prepare();
            Vector3 cameraPosition = camera.transform.position;
            Vector3 cameraForward = camera.transform.forward;
            GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
            EvaluateVisibility(camera, cameraPosition, cameraForward);
            SelectDesiredLods();
            float now = Time.realtimeSinceStartup;
            AdvancePersistentStates(now);
            BuildAndDraw(camera, cameraPosition, layer, now);
        }

        private void EvaluateVisibility(
            Camera camera,
            Vector3 cameraPosition,
            Vector3 cameraForward)
        {
            float maximumDistanceSquared = maximumDistanceMeters * maximumDistanceMeters;
            float focalPixels = camera.pixelHeight /
                (2f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f));
            for (int index = 0; index < instances.Length; index++)
            {
                Instance instance = instances[index];
                Vector3 offset = instance.Center - cameraPosition;
                float distanceSquared = offset.sqrMagnitude;
                bool inRange = distanceSquared <= maximumDistanceSquared;
                bool inFront = Vector3.Dot(cameraForward, offset) >= -instance.Radius;
                bool inFrustum = false;
                if (inRange && inFront)
                {
                    Bounds bounds = new Bounds(
                        instance.Center,
                        Vector3.one * (instance.Radius * 2f));
                    inFrustum = GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
                }
                visible[index] = inRange && inFront && inFrustum;
                float distance = Mathf.Sqrt(Mathf.Max(0.01f, distanceSquared));
                float pixels = instance.Radius * 2f * focalPixels / distance;
                projectedPixels[index] = pixels;
                float forwardWeight = Mathf.Clamp01(
                    Vector3.Dot(cameraForward, offset / distance));
                priorities[index] = pixels + forwardWeight * 24f +
                    (instances.Length - index) * 0.000001f;
                selectedLod0[index] = false;
                selectedLod1[index] = false;
            }
        }

        private void SelectDesiredLods()
        {
            int lod0Count = 0;
            for (int index = 0; index < instances.Length; index++)
            {
                if (!visible[index] || currentLods[index] != Lod0 ||
                    projectedPixels[index] < lod0ExitScreenPixels)
                    continue;
                selectedLod0[index] = true;
                lod0Count++;
            }
            FillBestCandidates(
                selectedLod0,
                null,
                lod0EnterScreenPixels,
                MaximumLod0Instances,
                ref lod0Count);

            int lod1Count = 0;
            for (int index = 0; index < instances.Length; index++)
            {
                if (selectedLod0[index] || !visible[index] ||
                    currentLods[index] != Lod1 ||
                    projectedPixels[index] < lod1ExitScreenPixels)
                    continue;
                selectedLod1[index] = true;
                lod1Count++;
            }
            FillBestCandidates(
                selectedLod1,
                selectedLod0,
                lod1EnterScreenPixels,
                MaximumLod1Instances,
                ref lod1Count);

            for (int index = 0; index < instances.Length; index++)
            {
                desiredLods[index] = selectedLod0[index]
                    ? Lod0
                    : (selectedLod1[index]
                        ? Lod1
                        : (visible[index] ? Lod2 : Culled));
            }
        }

        private void FillBestCandidates(
            bool[] selection,
            bool[] exclusion,
            float enterPixels,
            int capacity,
            ref int count)
        {
            while (count < capacity)
            {
                int bestIndex = -1;
                float bestPriority = float.NegativeInfinity;
                for (int index = 0; index < instances.Length; index++)
                {
                    if (!visible[index] || selection[index] ||
                        (exclusion != null && exclusion[index]) ||
                        projectedPixels[index] < enterPixels ||
                        priorities[index] <= bestPriority)
                        continue;
                    bestPriority = priorities[index];
                    bestIndex = index;
                }
                if (bestIndex < 0)
                    break;
                selection[bestIndex] = true;
                count++;
            }
        }

        private void AdvancePersistentStates(float now)
        {
            for (int index = 0; index < instances.Length; index++)
            {
                byte desired = desiredLods[index];
                if (currentLods[index] == Uninitialized)
                {
                    currentLods[index] = desired;
                    previousLods[index] = Culled;
                    transitionStarts[index] = float.NegativeInfinity;
                    lastStateChanges[index] = now;
                    continue;
                }
                if (currentLods[index] == desired)
                    continue;
                bool transitionComplete =
                    now - transitionStarts[index] >= transitionDurationSeconds;
                bool residencyComplete =
                    now - lastStateChanges[index] >= minimumResidencySeconds;
                if (!transitionComplete || !residencyComplete)
                    continue;
                previousLods[index] = currentLods[index];
                currentLods[index] = desired;
                transitionStarts[index] = now;
                lastStateChanges[index] = now;
            }
        }

        private void BuildAndDraw(
            Camera camera,
            Vector3 cameraPosition,
            int layer,
            float now)
        {
            int lod0Count = 0;
            int lod1Count = 0;
            int lod2Count = 0;

            for (int index = 0; index < instances.Length; index++)
            {
                if (!visible[index])
                    continue;
                float elapsed = now - transitionStarts[index];
                bool transitioning = elapsed >= 0f &&
                    elapsed < transitionDurationSeconds &&
                    previousLods[index] != currentLods[index];
                if (!transitioning)
                {
                    AddInstance(
                        index,
                        currentLods[index],
                        1f,
                        cameraPosition,
                        ref lod0Count,
                        ref lod1Count,
                        ref lod2Count);
                    continue;
                }
                float progress = Mathf.SmoothStep(
                    0f, 1f, elapsed / transitionDurationSeconds);
                AddInstance(
                    index,
                    previousLods[index],
                    1f - progress,
                    cameraPosition,
                    ref lod0Count,
                    ref lod1Count,
                    ref lod2Count);
                AddInstance(
                    index,
                    currentLods[index],
                    progress,
                    cameraPosition,
                    ref lod0Count,
                    ref lod1Count,
                    ref lod2Count);
            }

            DrawTier(
                lod0Mesh,
                lod0Matrices,
                lod0Tints,
                lod0Fades,
                lod0Frames,
                lod0Count,
                lod0Properties,
                camera,
                layer);
            DrawTier(
                lod1Mesh,
                lod1Matrices,
                lod1Tints,
                lod1Fades,
                lod1Frames,
                lod1Count,
                lod1Properties,
                camera,
                layer);
            DrawTier(
                lod2Mesh,
                lod2Matrices,
                lod2Tints,
                lod2Fades,
                lod2Frames,
                lod2Count,
                lod2Properties,
                camera,
                layer);
        }

        private void AddInstance(
            int index,
            byte lod,
            float fade,
            Vector3 cameraPosition,
            ref int lod0Count,
            ref int lod1Count,
            ref int lod2Count)
        {
            if (lod == Culled || fade <= 0.001f)
                return;
            Instance instance = instances[index];
            if (lod == Lod0 && lod0Count < lod0Matrices.Length)
            {
                lod0Matrices[lod0Count] = instance.Matrix;
                lod0Tints[lod0Count] = instance.Tint;
                lod0Fades[lod0Count] = fade;
                lod0Frames[lod0Count] = 0f;
                lod0Count++;
                return;
            }
            if (lod == Lod1 && lod1Count < lod1Matrices.Length)
            {
                lod1Matrices[lod1Count] = instance.Matrix;
                lod1Tints[lod1Count] = instance.Tint;
                lod1Fades[lod1Count] = fade;
                lod1Frames[lod1Count] = 0f;
                lod1Count++;
                return;
            }
            if (lod != Lod2 || lod2Count >= lod2Matrices.Length)
                return;
            Vector3 facing = cameraPosition - instance.Position;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.001f)
                facing = Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            lod2Matrices[lod2Count] = Matrix4x4.TRS(
                instance.Position,
                rotation,
                instance.Scale);
            lod2Tints[lod2Count] = instance.Tint;
            lod2Fades[lod2Count] = fade;
            lod2Frames[lod2Count] = SelectImpostorFrame(index, facing);
            lod2Count++;
        }

        private float SelectImpostorFrame(int index, Vector3 facing)
        {
            float viewAngle = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg -
                instances[index].RotationDegrees;
            byte current = impostorFrames[index];
            float center = current * (360f / ImpostorFrameCount);
            float halfCell = 180f / ImpostorFrameCount;
            if (Mathf.Abs(Mathf.DeltaAngle(center, viewAngle)) >
                halfCell + impostorAngularHysteresisDegrees)
            {
                int frame = Mathf.RoundToInt(
                    Mathf.Repeat(viewAngle, 360f) / (360f / ImpostorFrameCount));
                current = (byte)(frame % ImpostorFrameCount);
                impostorFrames[index] = current;
            }
            return current;
        }

        private void DrawTier(
            Mesh mesh,
            Matrix4x4[] matrices,
            Vector4[] tints,
            float[] fades,
            float[] frames,
            int count,
            MaterialPropertyBlock properties,
            Camera camera,
            int layer)
        {
            if (count <= 0)
                return;
            properties.SetVectorArray(InstanceTintId, tints);
            properties.SetFloatArray(InstanceFadeId, fades);
            properties.SetFloatArray(ImpostorFrameId, frames);
            Graphics.DrawMeshInstanced(
                mesh,
                0,
                sharedCanopyMaterial,
                matrices,
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
