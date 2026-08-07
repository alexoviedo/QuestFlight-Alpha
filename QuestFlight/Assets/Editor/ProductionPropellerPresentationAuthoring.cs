using System;
using System.Collections.Generic;
using System.Linq;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Idempotently authors the stopped/turning propeller presentation into the
    /// product prefab. Runtime never discovers or creates product hierarchy.
    /// </summary>
    public static class ProductionPropellerPresentationAuthoring
    {
        public const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string VisualRootPath = "AircraftVisualRoot";
        public const string PropellerPath =
            VisualRootPath +
            "/ImportedAircraftExterior/Cessna_Exterior/Cessna_Exterior_Body_MAT_0.001";
        public const string BlurName = "ProductionPropellerBlurDisc";
        public const string MaterialPath =
            "Assets/Production/Materials/Cockpit/ProductionPropellerBlur.mat";
        public const string ShaderName =
            "QuestFlight/Production/Propeller Blur";
        public const float StopRpm = 60f;
        public const float StartRpm = 150f;
        public const float FullBlurRpm = 900f;

        [MenuItem("QuestFlight/Aircraft/Author Production Propeller Presentation")]
        public static void ApplyToProductPrefab()
        {
            Material material = EnsureMaterial();
            GameObject root =
                PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                AuthorPrefabContents(root, material);
                if (!ValidateAuthoredPrefabContents(root, material, out string error))
                    throw new InvalidOperationException(error);

                PrefabUtility.SaveAsPrefabAsset(root, AircraftPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "[QuestFlight][Propeller] Authored the audited stopped blade " +
                    "and one fixed two-triangle running blur disc.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void ApplyToProductPrefabBatch()
        {
            ApplyToProductPrefab();
        }

        public static void AuthorPrefabContents(GameObject root, Material material)
        {
            Require(root != null && root.name == "ProductionC172AircraftRig",
                "Propeller authoring requires ProductionC172AircraftRig.");
            Transform visual = RequirePath(root, VisualRootPath);
            Transform propeller = RequirePath(root, PropellerPath);
            MeshFilter sourceFilter = propeller
                .GetComponentsInChildren<MeshFilter>(true)
                .Single(filter => filter.transform.name != BlurName);
            MeshRenderer stoppedRenderer = sourceFilter.GetComponent<MeshRenderer>();
            Require(stoppedRenderer != null,
                "The audited propeller source must retain one MeshRenderer.");
            Require(sourceFilter.sharedMesh != null &&
                    sourceFilter.sharedMesh.vertexCount == 166 &&
                    TriangleCount(sourceFilter.sharedMesh) == 212,
                "The stopped propeller no longer matches its audited source topology.");

            Transform blur = sourceFilter.transform.Find(BlurName);
            if (blur == null)
            {
                blur = new GameObject(BlurName).transform;
                blur.SetParent(sourceFilter.transform, false);
            }

            MeshFilter blurFilter = RequireOrAddSingle<MeshFilter>(blur.gameObject);
            MeshRenderer blurRenderer = RequireOrAddSingle<MeshRenderer>(blur.gameObject);
            Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            Require(quad != null && quad.vertexCount == 4,
                "Unity's built-in four-vertex Quad mesh is required.");
            blurFilter.sharedMesh = quad;
            blurRenderer.sharedMaterial = material;
            blurRenderer.shadowCastingMode = ShadowCastingMode.Off;
            blurRenderer.receiveShadows = false;
            blurRenderer.lightProbeUsage = LightProbeUsage.Off;
            blurRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            blurRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            blurRenderer.allowOcclusionWhenDynamic = false;
            blurRenderer.enabled = false;

            Bounds sourceBounds = sourceFilter.sharedMesh.bounds;
            float diameter = sourceBounds.size.y;
            Require(diameter > 0f && sourceBounds.size.y > sourceBounds.size.x,
                "The audited propeller must retain dominant mesh-local Y blade span.");
            blur.SetLocalPositionAndRotation(sourceBounds.center, Quaternion.identity);
            blur.localScale = new Vector3(diameter, diameter, 1f);
            GameObjectUtility.SetStaticEditorFlags(blur.gameObject,
                (StaticEditorFlags)0);

            FlightDynamicsCoordinator coordinator =
                RequireExactlyOne<FlightDynamicsCoordinator>(root);
            ProductionPropellerPresentationController controller =
                RequireOrAddSingle<ProductionPropellerPresentationController>(
                    visual.gameObject);
            controller.ConfigureAuthoredBindings(
                coordinator,
                stoppedRenderer,
                blurRenderer,
                StopRpm,
                StartRpm,
                FullBlurRpm);

            AircraftVisibilityProfileController visibility =
                RequireExactlyOne<AircraftVisibilityProfileController>(root);
            SerializedObject serializedVisibility = new SerializedObject(visibility);
            List<Renderer> interiors = ReadRendererArray(
                serializedVisibility.FindProperty("interiorRenderers"));
            List<Renderer> exteriors = ReadRendererArray(
                serializedVisibility.FindProperty("exteriorRenderers"));
            List<Renderer> occluders = ReadRendererArray(
                serializedVisibility.FindProperty(
                    "cockpitOccludingExteriorRenderers"));
            AddUnique(exteriors, blurRenderer);
            occluders.RemoveAll(renderer => renderer == stoppedRenderer);
            visibility.ConfigureAuthoredRenderers(
                interiors.ToArray(), exteriors.ToArray(), occluders.ToArray());

            EditorUtility.SetDirty(blur);
            EditorUtility.SetDirty(blurFilter);
            EditorUtility.SetDirty(blurRenderer);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(visibility);
        }

        public static bool ValidateAuthoredPrefabContents(
            GameObject root,
            Material material,
            out string error)
        {
            try
            {
                Transform visual = RequirePath(root, VisualRootPath);
                Transform propeller = RequirePath(root, PropellerPath);
                Transform[] blurTransforms = root
                    .GetComponentsInChildren<Transform>(true)
                    .Where(item => item.name == BlurName)
                    .ToArray();
                Require(blurTransforms.Length == 1,
                    "Exactly one authored propeller blur disc is required.");
                Transform blur = blurTransforms[0];
                MeshFilter blurFilter = RequireExactlyOne<MeshFilter>(blur.gameObject);
                MeshRenderer blurRenderer =
                    RequireExactlyOne<MeshRenderer>(blur.gameObject);
                MeshFilter sourceFilter = propeller
                    .GetComponentsInChildren<MeshFilter>(true)
                    .Single(filter => filter != blurFilter);
                MeshRenderer stoppedRenderer = sourceFilter.GetComponent<MeshRenderer>();
                Require(blur.parent == sourceFilter.transform &&
                        blur.localRotation == Quaternion.identity,
                    "The blur disc must stay fixed in the audited mesh-local XY propeller plane.");
                float diameter = sourceFilter.sharedMesh.bounds.size.y;
                Require(Vector3.Distance(
                            blur.localPosition,
                            sourceFilter.sharedMesh.bounds.center) < 0.00001f &&
                        Mathf.Abs(blur.localScale.x - diameter) < 0.00001f &&
                        Mathf.Abs(blur.localScale.y - diameter) < 0.00001f &&
                        Mathf.Abs(blur.localScale.z - 1f) < 0.00001f,
                    "The blur disc no longer matches the audited propeller center and diameter.");
                Require(blurFilter.sharedMesh != null &&
                        blurFilter.sharedMesh.vertexCount == 4 &&
                        TriangleCount(blurFilter.sharedMesh) == 2 &&
                        blurRenderer.sharedMaterial == material &&
                        blurRenderer.shadowCastingMode == ShadowCastingMode.Off &&
                        !blurRenderer.receiveShadows &&
                        !blurRenderer.enabled,
                    "The running propeller must serialize as one disabled two-triangle, one-material renderer.");

                ProductionPropellerPresentationController controller =
                    RequireExactlyOne<ProductionPropellerPresentationController>(root);
                FlightDynamicsCoordinator coordinator =
                    RequireExactlyOne<FlightDynamicsCoordinator>(root);
                Require(controller.transform == visual &&
                        controller.Coordinator == coordinator &&
                        controller.StoppedPropellerRenderer == stoppedRenderer &&
                        controller.RunningBlurRenderer == blurRenderer &&
                        Mathf.Approximately(controller.StopRpm, StopRpm) &&
                        Mathf.Approximately(controller.StartRpm, StartRpm) &&
                        Mathf.Approximately(controller.FullBlurRpm, FullBlurRpm),
                    "The propeller controller does not retain its canonical serialized bindings.");
                Require(controller.HasAuthoredReferences(out string bindingError),
                    bindingError);

                AircraftVisibilityProfileController visibility =
                    RequireExactlyOne<AircraftVisibilityProfileController>(root);
                SerializedObject serializedVisibility = new SerializedObject(visibility);
                List<Renderer> exteriors = ReadRendererArray(
                    serializedVisibility.FindProperty("exteriorRenderers"));
                List<Renderer> occluders = ReadRendererArray(
                    serializedVisibility.FindProperty(
                        "cockpitOccludingExteriorRenderers"));
                Require(exteriors.Count(renderer => renderer == blurRenderer) == 1,
                    "The blur renderer must occur exactly once in the exterior visibility profile.");
                Require(!occluders.Contains(stoppedRenderer),
                    "The stopped propeller must remain visible from the cockpit.");

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static Material EnsureMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            Require(shader != null, $"Missing shader '{ShaderName}'.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "ProductionPropellerBlur",
                    enableInstancing = true
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
                material.enableInstancing = true;
            }

            material.SetColor("_BlurColor",
                new Color(0.055f, 0.06f, 0.065f, 1f));
            material.SetFloat("_BlurStrength", 1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static List<Renderer> ReadRendererArray(SerializedProperty property)
        {
            Require(property != null && property.isArray,
                "The authored visibility renderer array is missing.");
            List<Renderer> result = new List<Renderer>(property.arraySize);
            for (int i = 0; i < property.arraySize; i++)
            {
                Renderer renderer = property.GetArrayElementAtIndex(i)
                    .objectReferenceValue as Renderer;
                if (renderer != null)
                    AddUnique(result, renderer);
            }
            return result;
        }

        private static void AddUnique(List<Renderer> renderers, Renderer renderer)
        {
            if (renderer != null && !renderers.Contains(renderer))
                renderers.Add(renderer);
        }

        private static int TriangleCount(Mesh mesh)
        {
            int count = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                count += (int)mesh.GetIndexCount(subMesh) / 3;
            return count;
        }

        private static Transform RequirePath(GameObject root, string path)
        {
            Transform found = root != null ? root.transform.Find(path) : null;
            Require(found != null, $"Missing authored path '{path}'.");
            return found;
        }

        private static T RequireExactlyOne<T>(GameObject root)
            where T : Component
        {
            T[] matches = root.GetComponentsInChildren<T>(true);
            Require(matches.Length == 1,
                $"Expected exactly one {typeof(T).Name}; found {matches.Length}.");
            return matches[0];
        }

        private static T RequireOrAddSingle<T>(GameObject gameObject)
            where T : Component
        {
            T[] matches = gameObject.GetComponents<T>();
            Require(matches.Length <= 1,
                $"Expected at most one {typeof(T).Name} on '{gameObject.name}'.");
            return matches.Length == 1 ? matches[0] : gameObject.AddComponent<T>();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
