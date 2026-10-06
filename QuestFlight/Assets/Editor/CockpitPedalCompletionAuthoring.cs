#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Derives four complete footplates and separate C172-style linkages as one
    /// rigidly weighted four-pedal batch. The source GLB remains immutable.
    /// </summary>
    public static partial class CockpitPedalCompletionAuthoring
    {
        public const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string AssetFolder =
            "Assets/Production/Cockpit/Pedals";
        public const string MeshPath =
            AssetFolder + "/C172_FourPedalAssembly.asset";
        public const string ManifestPath =
            AssetFolder + "/C172_FourPedalAssemblyManifest.json";
        public const string RootName = "ProductionFourPedalAssembly";
        public const string RootPath =
            CockpitMechanicalV2Contract.InteriorRootPath + "/" + RootName;

        // Visual travel is calibrated against the authored hinge locations and
        // complete source-pedal bounds. It is deliberately bounded: the product
        // does not claim maintenance-manual control-travel fidelity.
        public const float RudderPedalMaximumTravelDegrees = 12f;
        public const float ToeBrakeMaximumTravelDegrees = 10f;
        public const float PedalResponseRatePerSecond = 5.5f;

        public const string PilotLeftRudderName = "PilotLeftRudderPivot";
        public const string PilotLeftToeName = "PilotLeftToeBrakePivot";
        public const string PilotRightRudderName = "PilotRightRudderPivot";
        public const string PilotRightToeName = "PilotRightToeBrakePivot";
        public const string CopilotLeftRudderName = "CopilotLeftRudderPivot";
        public const string CopilotLeftToeName = "CopilotLeftToeBrakePivot";
        public const string CopilotRightRudderName = "CopilotRightRudderPivot";
        public const string CopilotRightToeName = "CopilotRightToeBrakePivot";

        // Frozen-source spatial audit. The source hash is asserted before use.
        // Unity's glTF handedness conversion reflects source X. The complete
        // pilot station is therefore the two negative-X assemblies; the lone
        // copilot pedal is positive-X. Values are frozen source-local metres.
        private const float MirrorPlaneX = 0f;
        private const float PivotY = 0.0465f;
        private const float LeftBarZ = -0.074f;
        private const float RightBarZ = -0.0685f;
        private const float PlateLift = .008f;
        private const float ToeY = .0453f-PlateLift;
        private const float ToeZ = -.0615f;

        private enum PedalBone
        {
            Root = 0,
            PilotLeftRudder = 1,
            PilotLeftToe = 2,
            PilotRightRudder = 3,
            PilotRightToe = 4,
            CopilotLeftRudder = 5,
            CopilotLeftToe = 6,
            CopilotRightRudder = 7,
            CopilotRightToe = 8
        }

        private sealed class TriangleComponent
        {
            public readonly List<int> triangleStarts = new List<int>();
            public Bounds bounds;
        }

        [MenuItem("QuestFlight/Cockpit/Author Complete Four-Pedal Assembly")]
        public static void Author()
        {
            EnsureFolder(AssetFolder);
            AssertSourceHash();
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                Transform sourceContainer = RequirePath(
                    root,
                    CockpitMechanicalV2Contract.PedalMergedSourcePath);
                MeshFilter sourceFilter =
                    sourceContainer.GetComponentInChildren<MeshFilter>(true);
                MeshRenderer sourceRenderer =
                    sourceContainer.GetComponentInChildren<MeshRenderer>(true);
                Require(sourceFilter != null && sourceFilter.sharedMesh != null,
                    "The frozen pedal source mesh is missing.");
                Require(sourceRenderer != null,
                    "The frozen pedal source renderer is missing.");
                Mesh source = sourceFilter.sharedMesh;
                Require(source.vertexCount == 14635,
                    $"Frozen pedal vertex count drifted: {source.vertexCount}.");
                Require(source.triangles.Length / 3 == 12971,
                    $"Frozen pedal triangle count drifted: {source.triangles.Length / 3}.");

                Transform authoredRoot = EnsureAuthoredRoot(sourceContainer.parent);
                Transform[] bones = BuildBones(authoredRoot);
                Mesh generated = BuildCompletedMesh(
                    source,
                    sourceRenderer.additionalVertexStreams,
                    authoredRoot,
                    bones,
                    out int sourceComponentCount,
                    out int mirroredComponentCount,
                    out int rejectedDuplicateComponents,
                    out int rejectedDuplicateTriangles);
                Mesh asset = SaveMesh(generated);

                SkinnedMeshRenderer renderer =
                    authoredRoot.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null)
                    renderer = authoredRoot.gameObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = asset;
                renderer.sharedMaterials = sourceRenderer.sharedMaterials;
                renderer.rootBone = authoredRoot;
                renderer.bones = bones;
                renderer.localBounds = asset.bounds;
                renderer.updateWhenOffscreen = false;
                renderer.quality = SkinQuality.Bone1;
                renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                renderer.receiveShadows = sourceRenderer.receiveShadows;
                renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
                renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
                renderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.Object;
                renderer.probeAnchor = sourceRenderer.probeAnchor;
                sourceRenderer.enabled = false;
                ReplaceRuntimeVisibilityAuthority(
                    root,
                    sourceRenderer,
                    renderer);
                ConfigurePedalPresentationBindingsOnly(root);

                EditorUtility.SetDirty(sourceRenderer);
                EditorUtility.SetDirty(renderer);
                PrefabUtility.SaveAsPrefabAsset(root, AircraftPrefabPath);
                WriteManifest(
                    source,
                    asset,
                    sourceComponentCount,
                    mirroredComponentCount,
                    rejectedDuplicateComponents,
                    rejectedDuplicateTriangles);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Require(ValidateAuthoredPrefab(out string error), error);
                Debug.Log(
                    $"[QuestFlight][CockpitPedals] Authored four pedals as one " +
                    $"skinned draw: vertices={asset.vertexCount}; " +
                    $"triangles={asset.triangles.Length / 3}; bones={bones.Length}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void AuthorBatch()
        {
            try
            {
                Author();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static bool ValidateAuthoredPrefab(out string error)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                Transform source = root.transform.Find(
                    CockpitMechanicalV2Contract.PedalMergedSourcePath);
                Transform authored = FindDescendant(root.transform, RootName);
                if (source == null || authored == null)
                {
                    error = "Pedal source or authored four-pedal root is missing.";
                    return false;
                }
                MeshRenderer sourceRenderer =
                    source.GetComponentInChildren<MeshRenderer>(true);
                SkinnedMeshRenderer[] renderers =
                    authored.GetComponents<SkinnedMeshRenderer>();
                if (sourceRenderer == null || sourceRenderer.enabled ||
                    renderers.Length != 1 || !renderers[0].enabled)
                {
                    error = "Exactly one authored pedal renderer must replace the disabled source renderer.";
                    return false;
                }
                SkinnedMeshRenderer renderer = renderers[0];
                if (renderer.sharedMesh == null ||
                    renderer.sharedMesh.vertexCount < 12000 ||
                    renderer.sharedMesh.triangles.Length / 3 < 12576 ||
                    renderer.sharedMesh.triangles.Length / 3 > 16913 ||
                    renderer.sharedMaterials.Length != 1 ||
                    renderer.bones.Length != 9)
                {
                    error = "Four-pedal mesh, material, or rigid-bone contract is incomplete.";
                    return false;
                }
                AircraftVisibilityProfileController visibility =
                    root.GetComponentInChildren<AircraftVisibilityProfileController>(true);
                if (visibility == null)
                {
                    error = "Aircraft visibility authority is missing.";
                    return false;
                }
                if (!ValidateRuntimeVisibilityOwnership(
                        visibility,
                        sourceRenderer,
                        renderer,
                        out error))
                    return false;
                string[] names =
                {
                    PilotLeftRudderName, PilotLeftToeName,
                    PilotRightRudderName, PilotRightToeName,
                    CopilotLeftRudderName, CopilotLeftToeName,
                    CopilotRightRudderName, CopilotRightToeName
                };
                foreach (string name in names)
                {
                    Transform pivot = FindDescendant(authored, name);
                    if (pivot == null || pivot.localScale != Vector3.one)
                    {
                        error = $"Animation-ready pedal pivot is missing or mirrored by scale: {name}.";
                        return false;
                    }
                }
                if (Mathf.Abs(
                        FindDescendant(authored, PilotLeftRudderName).localPosition.x +
                        FindDescendant(authored, CopilotRightRudderName).localPosition.x) > 0.0002f ||
                    Mathf.Abs(
                        FindDescendant(authored, PilotRightRudderName).localPosition.x +
                        FindDescendant(authored, CopilotLeftRudderName).localPosition.x) > 0.0002f)
                {
                    error = "Four-pedal station geometry is not symmetric about the audited cockpit center plane.";
                    return false;
                }
                ProductionCockpitDynamicsController presentation =
                    root.GetComponentInChildren<ProductionCockpitDynamicsController>(true);
                if (presentation == null ||
                    !presentation.ValidateAuthoredBindings(out error))
                    return false;
                ProductionCockpitDynamicsController.TransformBinding[] bindings =
                    presentation.CopyAuthoredBindingsForEditor();
                int pedalBindingCount = 0;
                HashSet<Transform> pedalTargets = new HashSet<Transform>();
                foreach (ProductionCockpitDynamicsController.TransformBinding binding in bindings)
                {
                    if (binding?.target == null || !binding.target.IsChildOf(authored))
                        continue;
                    pedalBindingCount++;
                    if (!pedalTargets.Add(binding.target) ||
                        binding.motions == null || binding.motions.Length != 1)
                    {
                        error = "Every pedal pivot must have exactly one unique motion binding.";
                        return false;
                    }
                }
                if (pedalBindingCount != 8)
                {
                    error = $"Expected eight authored pedal motion bindings; found {pedalBindingCount}.";
                    return false;
                }
                error = string.Empty;
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Mesh BuildCompletedMesh(
            Mesh source,
            Mesh visibilityStream,
            Transform rendererRoot,
            Transform[] bones,
            out int sourceComponentCount,
            out int mirroredComponentCount,
            out int rejectedDuplicateComponents,
            out int rejectedDuplicateTriangles)
        {
            return BuildLinkageMesh(source,visibilityStream,rendererRoot,bones,
                out sourceComponentCount,out mirroredComponentCount,
                out rejectedDuplicateComponents,out rejectedDuplicateTriangles);
        }

        private static List<TriangleComponent> BuildTriangleComponents(
            Vector3[] vertices,
            int[] triangles)
        {
            int triangleCount = triangles.Length / 3;
            var trianglesByWeldedVertex =
                new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int start = triangle * 3;
                var uniqueKeys = new HashSet<string>(StringComparer.Ordinal);
                for (int corner = 0; corner < 3; corner++)
                    uniqueKeys.Add(VertexKey(vertices[triangles[start + corner]], false));
                foreach (string key in uniqueKeys)
                {
                    if (!trianglesByWeldedVertex.TryGetValue(
                            key,
                            out List<int> linked))
                    {
                        linked = new List<int>();
                        trianglesByWeldedVertex.Add(key, linked);
                    }
                    linked.Add(triangle);
                }
            }

            var result = new List<TriangleComponent>();
            var visited = new bool[triangleCount];
            var queue = new Queue<int>();
            for (int seed = 0; seed < triangleCount; seed++)
            {
                if (visited[seed]) continue;
                visited[seed] = true;
                queue.Enqueue(seed);
                var component = new TriangleComponent();
                bool initializedBounds = false;
                while (queue.Count > 0)
                {
                    int triangle = queue.Dequeue();
                    int start = triangle * 3;
                    component.triangleStarts.Add(start);
                    var uniqueKeys = new HashSet<string>(StringComparer.Ordinal);
                    for (int corner = 0; corner < 3; corner++)
                    {
                        Vector3 vertex = vertices[triangles[start + corner]];
                        if (!initializedBounds)
                        {
                            component.bounds = new Bounds(vertex, Vector3.zero);
                            initializedBounds = true;
                        }
                        else
                        {
                            component.bounds.Encapsulate(vertex);
                        }
                        uniqueKeys.Add(VertexKey(vertex, false));
                    }
                    foreach (string key in uniqueKeys)
                    {
                        foreach (int linked in trianglesByWeldedVertex[key])
                        {
                            if (visited[linked]) continue;
                            visited[linked] = true;
                            queue.Enqueue(linked);
                        }
                    }
                }
                result.Add(component);
            }
            return result;
        }

        private static void ReplaceRuntimeVisibilityAuthority(
            GameObject root,
            MeshRenderer sourceRenderer,
            SkinnedMeshRenderer authoredRenderer)
        {
            AircraftVisibilityProfileController visibility =
                root.GetComponentInChildren<AircraftVisibilityProfileController>(true);
            Require(visibility != null,
                "Aircraft visibility authority is missing from the product prefab.");
            var serialized = new SerializedObject(visibility);
            SerializedProperty interiors = serialized.FindProperty("interiorRenderers");
            Require(interiors != null && interiors.isArray,
                "Aircraft interior renderer array is unavailable.");
            int sourceReferences = 0;
            int authoredReferences = 0;
            for (int index = 0; index < interiors.arraySize; index++)
            {
                SerializedProperty element = interiors.GetArrayElementAtIndex(index);
                if (element.objectReferenceValue == sourceRenderer)
                {
                    element.objectReferenceValue = authoredRenderer;
                    sourceReferences++;
                }
                if (element.objectReferenceValue == authoredRenderer)
                    authoredReferences++;
            }
            Require(sourceReferences <= 1,
                $"Expected at most one legacy pedal visibility reference; found {sourceReferences}.");
            Require(authoredReferences == 1,
                $"Expected exactly one authored pedal visibility reference; found {authoredReferences}.");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(visibility);
        }

        private static bool ValidateRuntimeVisibilityOwnership(
            AircraftVisibilityProfileController visibility,
            MeshRenderer sourceRenderer,
            SkinnedMeshRenderer authoredRenderer,
            out string error)
        {
            var serialized = new SerializedObject(visibility);
            string[] arrays =
            {
                "interiorRenderers",
                "exteriorRenderers",
                "cockpitOccludingExteriorRenderers"
            };
            int authoredInteriorReferences = 0;
            foreach (string arrayName in arrays)
            {
                SerializedProperty array = serialized.FindProperty(arrayName);
                if (array == null || !array.isArray)
                {
                    error = $"Visibility array is unavailable: {arrayName}.";
                    return false;
                }
                for (int index = 0; index < array.arraySize; index++)
                {
                    UnityEngine.Object value =
                        array.GetArrayElementAtIndex(index).objectReferenceValue;
                    if (value == sourceRenderer)
                    {
                        error = $"Legacy pedal renderer remains runtime-owned by {arrayName}.";
                        return false;
                    }
                    if (arrayName == "interiorRenderers" && value == authoredRenderer)
                        authoredInteriorReferences++;
                }
            }
            if (authoredInteriorReferences != 1)
            {
                error = "Authored pedal renderer must occur exactly once in interior visibility ownership.";
                return false;
            }

            visibility.ApplyProfile(
                AircraftVisibilityProfileController.VisibilityProfile.Cockpit);
            if (sourceRenderer.enabled || !authoredRenderer.enabled)
            {
                error = "Cockpit runtime profile does not exclusively enable the authored pedal renderer.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static string VertexKey(Vector3 value, bool mirrorX)
        {
            float x = mirrorX ? 2f * MirrorPlaneX - value.x : value.x;
            const float scale = 100000f;
            return string.Concat(
                Mathf.RoundToInt(x * scale).ToString(CultureInfo.InvariantCulture), ":",
                Mathf.RoundToInt(value.y * scale).ToString(CultureInfo.InvariantCulture), ":",
                Mathf.RoundToInt(value.z * scale).ToString(CultureInfo.InvariantCulture));
        }

        private static BoneWeight RigidWeight(PedalBone bone)
        {
            return new BoneWeight
            {
                boneIndex0 = (int)bone,
                weight0 = 1f
            };
        }

        private static Color32[] ReadColors(Mesh source, Mesh stream)
        {
            if (stream != null && stream.vertexCount == source.vertexCount)
            {
                Color32[] streamColors = stream.colors32;
                if (streamColors.Length == source.vertexCount)
                    return streamColors;
            }
            Color32[] colors = source.colors32;
            return colors.Length == source.vertexCount
                ? colors
                : Enumerable.Repeat(new Color32(255, 255, 255, 255), source.vertexCount).ToArray();
        }

        private static List<Vector4>[] ReadUvChannels(Mesh source)
        {
            List<Vector4>[] result = new List<Vector4>[8];
            for (int channel = 0; channel < result.Length; channel++)
            {
                result[channel] = new List<Vector4>();
                source.GetUVs(channel, result[channel]);
            }
            return result;
        }

        private static Transform EnsureAuthoredRoot(Transform parent)
        {
            Transform result = parent.Find(RootName);
            if (result == null)
            {
                result = new GameObject(RootName).transform;
                result.SetParent(parent, false);
            }
            result.localPosition = Vector3.zero;
            result.localRotation = Quaternion.identity;
            result.localScale = Vector3.one;
            return result;
        }

        private static Transform[] BuildBones(Transform root)
        {
            Transform pilotLeftRudder = Pivot(root, PilotLeftRudderName, -0.0345f);
            Transform pilotLeftToe = Pivot(pilotLeftRudder, PilotLeftToeName, 0f, true);
            Transform pilotRightRudder = Pivot(root, PilotRightRudderName, -0.01338f);
            Transform pilotRightToe = Pivot(pilotRightRudder, PilotRightToeName, 0f, true);
            Transform copilotLeftRudder = Pivot(root, CopilotLeftRudderName, 0.01338f);
            Transform copilotLeftToe = Pivot(copilotLeftRudder, CopilotLeftToeName, 0f, true);
            Transform copilotRightRudder = Pivot(root, CopilotRightRudderName, 0.0345f);
            Transform copilotRightToe = Pivot(copilotRightRudder, CopilotRightToeName, 0f, true);
            return new[]
            {
                root,
                pilotLeftRudder, pilotLeftToe,
                pilotRightRudder, pilotRightToe,
                copilotLeftRudder, copilotLeftToe,
                copilotRightRudder, copilotRightToe
            };
        }

        /// <summary>
        /// Builds the complete two-station mechanical linkage. Positive rudder
        /// advances the right pedals and retracts the left pedals at both seats;
        /// each toe-brake channel advances the matching toe plate at both seats.
        /// </summary>
        public static ProductionCockpitDynamicsController.TransformBinding[]
            BuildPedalPresentationBindings(Transform authoredRoot)
        {
            Require(authoredRoot != null && authoredRoot.name == RootName,
                "The authored four-pedal root is required for motion binding.");
            Transform pilotLeftRudder = RequireChild(authoredRoot, PilotLeftRudderName);
            Transform pilotRightRudder = RequireChild(authoredRoot, PilotRightRudderName);
            Transform copilotLeftRudder = RequireChild(authoredRoot, CopilotLeftRudderName);
            Transform copilotRightRudder = RequireChild(authoredRoot, CopilotRightRudderName);
            Transform pilotLeftToe = RequireChild(pilotLeftRudder, PilotLeftToeName);
            Transform pilotRightToe = RequireChild(pilotRightRudder, PilotRightToeName);
            Transform copilotLeftToe = RequireChild(copilotLeftRudder, CopilotLeftToeName);
            Transform copilotRightToe = RequireChild(copilotRightRudder, CopilotRightToeName);

            return new[]
            {
                PedalBinding("Pilot left rudder pedal", pilotLeftRudder,
                    CockpitPresentationChannel.RudderPedalsNormalized,
                    -1f, 1f, RudderPedalMaximumTravelDegrees,
                    -RudderPedalMaximumTravelDegrees),
                PedalBinding("Pilot right rudder pedal", pilotRightRudder,
                    CockpitPresentationChannel.RudderPedalsNormalized,
                    -1f, 1f, -RudderPedalMaximumTravelDegrees,
                    RudderPedalMaximumTravelDegrees),
                PedalBinding("Copilot left rudder pedal", copilotLeftRudder,
                    CockpitPresentationChannel.RudderPedalsNormalized,
                    -1f, 1f, RudderPedalMaximumTravelDegrees,
                    -RudderPedalMaximumTravelDegrees),
                PedalBinding("Copilot right rudder pedal", copilotRightRudder,
                    CockpitPresentationChannel.RudderPedalsNormalized,
                    -1f, 1f, -RudderPedalMaximumTravelDegrees,
                    RudderPedalMaximumTravelDegrees),
                PedalBinding("Pilot left toe brake", pilotLeftToe,
                    CockpitPresentationChannel.LeftToeBrakeNormalized,
                    0f, 1f, 0f, ToeBrakeMaximumTravelDegrees),
                PedalBinding("Copilot left toe brake", copilotLeftToe,
                    CockpitPresentationChannel.LeftToeBrakeNormalized,
                    0f, 1f, 0f, ToeBrakeMaximumTravelDegrees),
                PedalBinding("Pilot right toe brake", pilotRightToe,
                    CockpitPresentationChannel.RightToeBrakeNormalized,
                    0f, 1f, 0f, ToeBrakeMaximumTravelDegrees),
                PedalBinding("Copilot right toe brake", copilotRightToe,
                    CockpitPresentationChannel.RightToeBrakeNormalized,
                    0f, 1f, 0f, ToeBrakeMaximumTravelDegrees)
            };
        }

        /// <summary>
        /// Narrow, idempotent authoring pass: preserves every unrelated cockpit
        /// binding and replaces only bindings owned by the authored pedal root.
        /// </summary>
        public static void ConfigurePedalPresentationBindingsOnly(GameObject root)
        {
            Transform authoredRoot = root.transform.Find(RootPath);
            Require(authoredRoot != null, "The authored four-pedal root is missing.");
            ProductionCockpitDynamicsController[] controllers =
                root.GetComponentsInChildren<ProductionCockpitDynamicsController>(true);
            Require(controllers.Length == 1,
                $"Expected one cockpit presentation controller; found {controllers.Length}.");
            ProductionCockpitDynamicsController controller = controllers[0];
            List<ProductionCockpitDynamicsController.TransformBinding> retained =
                controller.CopyAuthoredBindingsForEditor()
                    .Where(binding => binding?.target == null ||
                                      !binding.target.IsChildOf(authoredRoot))
                    .ToList();
            retained.AddRange(BuildPedalPresentationBindings(authoredRoot));
            controller.ConfigureAuthoredReferences(
                controller.Coordinator,
                controller.AuthoritativeStateProvider,
                retained.ToArray());
            EditorUtility.SetDirty(controller);
        }

        private static ProductionCockpitDynamicsController.TransformBinding PedalBinding(
            string label,
            Transform target,
            CockpitPresentationChannel channel,
            float inputMinimum,
            float inputMaximum,
            float minimumAngle,
            float maximumAngle)
        {
            return new ProductionCockpitDynamicsController.TransformBinding
            {
                label = label,
                target = target,
                motions = new[]
                {
                    new ProductionCockpitDynamicsController.MotionTerm
                    {
                        label = label + " authored hinge travel",
                        channel = channel,
                        inputRemap = ProductionCockpitDynamicsController.InputRemapMode.Clamp,
                        inputMinimum = inputMinimum,
                        inputMaximum = inputMaximum,
                        responseRatePerSecond = PedalResponseRatePerSecond,
                        localRotationAxis = Vector3.right,
                        minimumAngleDegrees = minimumAngle,
                        maximumAngleDegrees = maximumAngle,
                        localTranslationAxis = Vector3.zero,
                        minimumTranslationMeters = 0f,
                        maximumTranslationMeters = 0f
                    }
                }
            };
        }

        private static Transform RequireChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            Require(child != null, $"Required pedal pivot is missing: {name}");
            return child;
        }

        private static Transform Pivot(
            Transform parent,
            string name,
            float x,
            bool child = false)
        {
            Transform pivot = parent.Find(name);
            if (pivot == null)
            {
                pivot = new GameObject(name).transform;
                pivot.SetParent(parent, false);
            }
            bool left=name.Contains("Left");
            float barZ=left?LeftBarZ:RightBarZ;
            pivot.localPosition = child
                ? new Vector3(0,ToeY-PivotY,ToeZ-barZ)
                : new Vector3(x, PivotY, barZ);
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;
            return pivot;
        }

        private static Mesh SaveMesh(Mesh generated)
        {
            Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (asset == null)
            {
                AssetDatabase.CreateAsset(generated, MeshPath);
                return generated;
            }
            // Clear stale vertex streams before replacing a larger predecessor.
            // CopySerialized alone can retain now-unreferenced tail vertices when
            // the regenerated mesh becomes smaller while preserving the asset GUID.
            asset.Clear(false);
            EditorUtility.CopySerialized(generated, asset);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void WriteManifest(
            Mesh source,
            Mesh output,
            int sourceComponentCount,
            int mirroredComponentCount,
            int rejectedDuplicateComponents,
            int rejectedDuplicateTriangles)
        {
            string json =
                "{\n" +
                "  \"schema\": \"questflight.cockpit_four_pedal.v2\",\n" +
                "  \"source_asset\": \"Assets/Resources/QuestFlightLab/ImportedAssets/Cessna172KogThorns/cessna172.glb\",\n" +
                $"  \"source_sha256\": \"{CockpitMechanicalV2Contract.SourceGlbSha256}\",\n" +
                "  \"source_license\": \"CC BY 4.0; KOG_THORNS; modified project derivative\",\n" +
                "  \"derivation\": \"four complete copies of audited source footplate components 0,35-40,48,49; preserve UVs and correct reflected winding, tangents and visibility; replace incomplete consumer floor hardware with fixed bearings, two rudder torque tubes, four arms and independent toe hinges following Cessna service manual Figure 10-2\",\n" +
                $"  \"source_vertices\": {source.vertexCount},\n" +
                $"  \"source_triangles\": {source.triangles.Length / 3},\n" +
                $"  \"output_vertices\": {output.vertexCount},\n" +
                $"  \"output_triangles\": {output.triangles.Length / 3},\n" +
                $"  \"source_connected_components\": {sourceComponentCount},\n" +
                $"  \"copied_footplate_components\": {mirroredComponentCount},\n" +
                $"  \"rejected_duplicate_components\": {rejectedDuplicateComponents},\n" +
                $"  \"rejected_duplicate_triangles\": {rejectedDuplicateTriangles},\n" +
                "  \"pedal_assemblies\": 4,\n" +
                "  \"runtime_renderers\": 1,\n" +
                "  \"runtime_materials\": 1,\n" +
                "  \"animation_bones\": 9,\n" +
                "  \"runtime_motion_enabled\": true,\n" +
                "  \"runtime_motion_bindings\": 8,\n" +
                "  \"skinning_ownership\": \"welded_component_rigid\",\n" +
                "  \"mixed_bone_triangles\": 0,\n" +
                "  \"pivot_translation_runtime\": false,\n" +
                "  \"rudder_channel\": \"RudderPedalsNormalized\",\n" +
                "  \"left_brake_channel\": \"LeftToeBrakeNormalized\",\n" +
                "  \"right_brake_channel\": \"RightToeBrakeNormalized\",\n" +
                "  \"hinge_axis_local\": [1.0, 0.0, 0.0],\n" +
                $"  \"mechanical_response_rate_per_second\": {PedalResponseRatePerSecond.ToString(CultureInfo.InvariantCulture)},\n" +
                $"  \"rudder_maximum_visual_travel_degrees\": {RudderPedalMaximumTravelDegrees.ToString(CultureInfo.InvariantCulture)},\n" +
                $"  \"toe_brake_maximum_visual_travel_degrees\": {ToeBrakeMaximumTravelDegrees.ToString(CultureInfo.InvariantCulture)}\n" +
                "}\n";
            File.WriteAllText(ManifestPath, json, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void AssertSourceHash()
        {
            string path = AssetDatabase.GUIDToAssetPath(
                CockpitMechanicalV2Contract.SourceGlbGuid);
            Require(!string.IsNullOrEmpty(path), "Frozen C172 source GUID is unresolved.");
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 hash = SHA256.Create())
            {
                string actual = string.Concat(
                    hash.ComputeHash(stream).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
                Require(string.Equals(
                        actual,
                        CockpitMechanicalV2Contract.SourceGlbSha256,
                        StringComparison.Ordinal),
                    $"Frozen C172 source hash drifted: {actual}.");
            }
        }

        private static Transform RequirePath(GameObject root, string path)
        {
            Transform value = root.transform.Find(path);
            Require(value != null, $"Required authored path is missing: {path}");
            return value;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(candidate.name, name, StringComparison.Ordinal))
                    return candidate;
            }
            return null;
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
