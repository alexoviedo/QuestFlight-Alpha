using System;
using System.Linq;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Editor-only, idempotent integration of the identity-audited fixed nose
    /// gear. Runtime code receives only serialized references and never repairs
    /// or discovers the product hierarchy.
    /// </summary>
    public static class ProductionAircraftNoseSteeringAuthoring
    {
        public const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string SourceGlbGuid =
            "d312be0fd0ba928458dd86cbe967956b";
        public const string VisualRootPath = "AircraftVisualRoot";
        public const string ExteriorRootPath =
            VisualRootPath + "/ImportedAircraftExterior/Cessna_Exterior";

        public const string SteeringStrutName =
            "Cessna_Exterior_Body_MAT_0.019";
        public const string NoseWheelPantName =
            "Cessna_Exterior_Body_MAT_0.021";
        public const string SteeringPivotName =
            "ProductionNoseSteeringPivot";

        public const string OriginalSteeringStrutPath =
            ExteriorRootPath + "/" + SteeringStrutName;
        public const string OriginalNoseWheelPantPath =
            ExteriorRootPath + "/" + NoseWheelPantName;
        public const string SteeringPivotPath =
            VisualRootPath + "/" + SteeringPivotName;
        public const string AuthoredSteeringStrutPath =
            SteeringPivotPath + "/" + SteeringStrutName;
        public const string AuthoredNoseWheelPantPath =
            SteeringPivotPath + "/" + NoseWheelPantName;

        public const string StaticPropellerPath =
            ExteriorRootPath + "/Cessna_Exterior_Body_MAT_0.001";
        public const string StaticPositiveXMainGearPath =
            ExteriorRootPath + "/Cessna_Exterior_Body_MAT_0.022";
        public const string StaticNegativeXMainGearPath =
            ExteriorRootPath + "/Cessna_Exterior_Body_MAT_0.023";
        public const string StaticSpinnerPath =
            ExteriorRootPath + "/Cessna_Exterior_Body_MAT_0.024";

        public const int SteeringStrutVertexCount = 231;
        public const int SteeringStrutTriangleCount = 164;
        public const int NoseWheelPantVertexCount = 562;
        public const int NoseWheelPantTriangleCount = 814;
        public const float MaximumSteeringAngleDegrees = 10f;
        public const float FullAuthorityAtOrBelowKnots = 5f;
        public const float ZeroAuthorityAtOrAboveKnots = 35f;
        public const float SteeringDirection = 1f;

        [MenuItem("QuestFlight/Cockpit/Author Production Nose Steering")]
        public static void ApplyToProductPrefab()
        {
            GameObject root =
                PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                AuthorPrefabContents(root);
                if (!ValidateAuthoredPrefabContents(root, out string error))
                    throw new InvalidOperationException(error);

                PrefabUtility.SaveAsPrefabAsset(root, AircraftPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "[QuestFlight][NoseSteering] Serialized one identity-audited " +
                    "nose-steering pivot; +0 renderers/materials/textures.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void AuthorPrefabContents(GameObject root)
        {
            RequireProductRoot(root);
            Transform visual = RequirePath(root, VisualRootPath);
            RequirePath(root, StaticPropellerPath);
            RequirePath(root, StaticPositiveXMainGearPath);
            RequirePath(root, StaticNegativeXMainGearPath);
            RequirePath(root, StaticSpinnerPath);

            int rendererCountBefore =
                root.GetComponentsInChildren<Renderer>(true).Length;
            Transform strut = RequireSingleNamedSource(
                root,
                SteeringStrutName,
                OriginalSteeringStrutPath,
                AuthoredSteeringStrutPath);
            Transform wheelPant = RequireSingleNamedSource(
                root,
                NoseWheelPantName,
                OriginalNoseWheelPantPath,
                AuthoredNoseWheelPantPath);
            SourceIdentity strutIdentity = CaptureAndValidateSource(
                strut,
                SteeringStrutVertexCount,
                SteeringStrutTriangleCount);
            SourceIdentity wheelIdentity = CaptureAndValidateSource(
                wheelPant,
                NoseWheelPantVertexCount,
                NoseWheelPantTriangleCount);
            Matrix4x4 strutWorldBefore = strut.localToWorldMatrix;
            Matrix4x4 wheelWorldBefore = wheelPant.localToWorldMatrix;

            Transform pivot = FindPath(root, SteeringPivotPath);
            if (pivot == null)
            {
                Require(
                    FindPath(root, OriginalSteeringStrutPath) == strut &&
                    FindPath(root, OriginalNoseWheelPantPath) == wheelPant,
                    "A missing steering pivot requires both sources at their original audited paths.");

                pivot = new GameObject(SteeringPivotName).transform;
                pivot.SetParent(visual, false);
                Bounds strutBounds = BoundsInSpace(strut, visual);
                Bounds wheelBounds = BoundsInSpace(wheelPant, visual);
                pivot.SetLocalPositionAndRotation(
                    new Vector3(
                        wheelBounds.center.x,
                        strutBounds.max.y,
                        wheelBounds.center.z),
                    Quaternion.identity);
                pivot.localScale = Vector3.one;
                GameObjectUtility.SetStaticEditorFlags(
                    pivot.gameObject,
                    (StaticEditorFlags)0);

                strut.SetParent(pivot, true);
                wheelPant.SetParent(pivot, true);
            }

            Require(
                pivot.parent == visual,
                "The steering pivot must be a direct AircraftVisualRoot child.");
            Require(
                strut.parent == pivot && wheelPant.parent == pivot,
                "The .019 strut and whole .021 wheel/pant must be direct steering-pivot children.");
            Require(
                MatrixApproximately(strutWorldBefore, strut.localToWorldMatrix) &&
                MatrixApproximately(wheelWorldBefore, wheelPant.localToWorldMatrix),
                "Steering authoring changed imported source world poses.");
            RequireSourceIdentity(strutIdentity, strut);
            RequireSourceIdentity(wheelIdentity, wheelPant);

            FlightDynamicsCoordinator coordinator =
                RequireExactlyOne<FlightDynamicsCoordinator>(root);
            ProductionAircraftNoseSteeringAnimator animator =
                RequireOrAddSingleAnimator(root, visual);
            animator.ConfigureAuthoredBindings(
                coordinator,
                new ProductionAircraftNoseSteeringAnimator.SteeringBinding
                {
                    steeringPivot = pivot,
                    steeringStrutFairing = strut,
                    noseWheelPant = wheelPant,
                    localRotationAxis = Vector3.up,
                    maximumSteeringAngleDegrees =
                        MaximumSteeringAngleDegrees,
                    fullAuthorityAtOrBelowKnots =
                        FullAuthorityAtOrBelowKnots,
                    zeroAuthorityAtOrAboveKnots =
                        ZeroAuthorityAtOrAboveKnots,
                    direction = SteeringDirection
                });

            Require(
                root.GetComponentsInChildren<Renderer>(true).Length ==
                rendererCountBefore,
                "Nose-steering authoring may not add or remove renderers.");
            EditorUtility.SetDirty(pivot);
            EditorUtility.SetDirty(strut);
            EditorUtility.SetDirty(wheelPant);
            EditorUtility.SetDirty(animator);

            if (!ValidateAuthoredPrefabContents(root, out string error))
                throw new InvalidOperationException(error);
        }

        public static bool ValidateAuthoredPrefabContents(
            GameObject root,
            out string error)
        {
            try
            {
                RequireProductRoot(root);
                Transform visual = RequirePath(root, VisualRootPath);
                Transform pivot = RequirePath(root, SteeringPivotPath);
                Transform strut =
                    RequirePath(root, AuthoredSteeringStrutPath);
                Transform wheelPant =
                    RequirePath(root, AuthoredNoseWheelPantPath);

                Require(
                    FindPath(root, OriginalSteeringStrutPath) == null &&
                    FindPath(root, OriginalNoseWheelPantPath) == null,
                    "Original and authored nose-gear representations may not coexist.");
                Require(
                    pivot.parent == visual &&
                    strut.parent == pivot &&
                    wheelPant.parent == pivot,
                    "The authored nose-gear hierarchy is not canonical.");
                Require(
                    Quaternion.Angle(
                        pivot.localRotation,
                        Quaternion.identity) < 0.001f &&
                    Vector3.Distance(
                        pivot.localScale,
                        Vector3.one) < 0.000001f,
                    "The steering pivot must stay AircraftVisualRoot-aligned.");

                CaptureAndValidateSource(
                    strut,
                    SteeringStrutVertexCount,
                    SteeringStrutTriangleCount);
                CaptureAndValidateSource(
                    wheelPant,
                    NoseWheelPantVertexCount,
                    NoseWheelPantTriangleCount);
                Bounds strutBounds = BoundsInSpace(strut, visual);
                Bounds wheelBounds = BoundsInSpace(wheelPant, visual);
                Vector3 expectedPivotPosition = new Vector3(
                    wheelBounds.center.x,
                    strutBounds.max.y,
                    wheelBounds.center.z);
                Require(
                    Vector3.Distance(
                        pivot.localPosition,
                        expectedPivotPosition) < 0.0001f,
                    "The steering pivot no longer lies on the audited nose-gear steering axis.");
                RequirePath(root, StaticPropellerPath);
                RequirePath(root, StaticPositiveXMainGearPath);
                RequirePath(root, StaticNegativeXMainGearPath);
                RequirePath(root, StaticSpinnerPath);

                ProductionAircraftNoseSteeringAnimator animator =
                    RequireExactlyOne<
                        ProductionAircraftNoseSteeringAnimator>(root);
                FlightDynamicsCoordinator coordinator =
                    RequireExactlyOne<FlightDynamicsCoordinator>(root);
                Require(
                    animator.transform == visual &&
                    animator.Coordinator == coordinator &&
                    animator.SteeringPivot == pivot &&
                    animator.SteeringStrutFairing == strut &&
                    animator.NoseWheelPant == wheelPant &&
                    Vector3.Distance(
                        animator.LocalRotationAxis,
                        Vector3.up) < 0.000001f &&
                    Mathf.Abs(
                        animator.MaximumSteeringAngleDegrees -
                        MaximumSteeringAngleDegrees) < 0.0001f &&
                    Mathf.Abs(
                        animator.FullAuthorityAtOrBelowKnots -
                        FullAuthorityAtOrBelowKnots) < 0.0001f &&
                    Mathf.Abs(
                        animator.ZeroAuthorityAtOrAboveKnots -
                        ZeroAuthorityAtOrAboveKnots) < 0.0001f &&
                    animator.Direction == SteeringDirection,
                    "The nose-steering component does not serialize the canonical authored references.");
                bool hasReferences =
                    animator.HasAuthoredReferences(
                        out string bindingError);
                Require(hasReferences, bindingError);
                Require(
                    pivot.GetComponents<Component>().All(
                        component =>
                            component is Transform),
                    "The steering pivot may contain only its Transform.");
                Require(
                    pivot.GetComponentsInChildren<Renderer>(true).Length == 2,
                    "The authored nose gear must retain exactly its two source renderers.");

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static ProductionAircraftNoseSteeringAnimator
            RequireOrAddSingleAnimator(
                GameObject root,
                Transform visual)
        {
            ProductionAircraftNoseSteeringAnimator[] matches =
                root.GetComponentsInChildren<
                    ProductionAircraftNoseSteeringAnimator>(true);
            Require(
                matches.Length <= 1,
                "At most one ProductionAircraftNoseSteeringAnimator may exist.");
            if (matches.Length == 1)
            {
                Require(
                    matches[0].transform == visual,
                    "The nose-steering animator must be authored on AircraftVisualRoot.");
                return matches[0];
            }

            return visual.gameObject
                .AddComponent<ProductionAircraftNoseSteeringAnimator>();
        }

        private static Transform RequireSingleNamedSource(
            GameObject root,
            string sourceName,
            string originalPath,
            string authoredPath)
        {
            Transform[] named = root
                .GetComponentsInChildren<Transform>(true)
                .Where(item =>
                    string.Equals(
                        item.name,
                        sourceName,
                        StringComparison.Ordinal))
                .ToArray();
            Require(
                named.Length == 1,
                $"Expected exactly one identity-audited source named '{sourceName}'.");
            Transform original = FindPath(root, originalPath);
            Transform authored = FindPath(root, authoredPath);
            Require(
                (original == named[0]) ^ (authored == named[0]),
                $"'{sourceName}' must occupy exactly one original or authored path.");
            return named[0];
        }

        private static SourceIdentity CaptureAndValidateSource(
            Transform source,
            int expectedVertices,
            int expectedTriangles)
        {
            MeshFilter[] filters =
                source.GetComponentsInChildren<MeshFilter>(true);
            MeshRenderer[] renderers =
                source.GetComponentsInChildren<MeshRenderer>(true);
            Require(
                filters.Length == 1 &&
                renderers.Length == 1 &&
                filters[0].sharedMesh != null,
                $"'{source.name}' must retain one imported mesh and renderer.");
            Mesh mesh = filters[0].sharedMesh;
            Require(
                mesh.vertexCount == expectedVertices &&
                MeshTriangleCount(mesh) == expectedTriangles,
                $"'{source.name}' no longer matches its audited source topology.");
            Require(
                AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(mesh)) == SourceGlbGuid,
                $"'{source.name}' no longer uses the audited source GLB mesh.");

            Material[] materials = renderers[0].sharedMaterials;
            Require(
                materials.Length == 1 &&
                materials[0] != null &&
                AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(materials[0])) ==
                SourceGlbGuid,
                $"'{source.name}' no longer uses its imported source material.");
            return new SourceIdentity(
                mesh,
                materials[0],
                filters[0],
                renderers[0]);
        }

        private static void RequireSourceIdentity(
            SourceIdentity identity,
            Transform source)
        {
            MeshFilter filter =
                source.GetComponentsInChildren<MeshFilter>(true).Single();
            MeshRenderer renderer =
                source.GetComponentsInChildren<MeshRenderer>(true).Single();
            Require(
                filter == identity.filter &&
                renderer == identity.renderer &&
                filter.sharedMesh == identity.mesh &&
                renderer.sharedMaterials.Length == 1 &&
                renderer.sharedMaterial == identity.material,
                $"'{source.name}' mesh/material identity changed during authoring.");
        }

        private static int MeshTriangleCount(Mesh mesh)
        {
            int count = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                count += (int)mesh.GetIndexCount(subMesh) / 3;
            return count;
        }

        private static Bounds BoundsInSpace(
            Transform source,
            Transform targetSpace)
        {
            MeshFilter filter =
                source.GetComponentsInChildren<MeshFilter>(true).Single();
            Bounds local = filter.sharedMesh.bounds;
            Vector3 minimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 maximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 localCorner = new Vector3(
                    x == 0 ? local.min.x : local.max.x,
                    y == 0 ? local.min.y : local.max.y,
                    z == 0 ? local.min.z : local.max.z);
                Vector3 point = targetSpace.InverseTransformPoint(
                    filter.transform.TransformPoint(localCorner));
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }

            Bounds result = new Bounds();
            result.SetMinMax(minimum, maximum);
            return result;
        }

        private static bool MatrixApproximately(
            Matrix4x4 expected,
            Matrix4x4 actual)
        {
            for (int index = 0; index < 16; index++)
            {
                if (Mathf.Abs(expected[index] - actual[index]) > 0.00001f)
                    return false;
            }

            return true;
        }

        private static void RequireProductRoot(GameObject root)
        {
            Require(root != null, "Aircraft prefab contents are required.");
            Require(
                string.Equals(
                    root.name,
                    "ProductionC172AircraftRig",
                    StringComparison.Ordinal),
                "Nose steering may only author ProductionC172AircraftRig.");
        }

        private static T RequireExactlyOne<T>(GameObject root)
            where T : Component
        {
            T[] matches = root.GetComponentsInChildren<T>(true);
            Require(
                matches.Length == 1,
                $"Expected exactly one {typeof(T).Name}; found {matches.Length}.");
            return matches[0];
        }

        private static Transform RequirePath(
            GameObject root,
            string path)
        {
            Transform found = FindPath(root, path);
            Require(found != null, $"Missing authored path '{path}'.");
            return found;
        }

        private static Transform FindPath(
            GameObject root,
            string path)
        {
            return root != null ? root.transform.Find(path) : null;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private sealed class SourceIdentity
        {
            public readonly Mesh mesh;
            public readonly Material material;
            public readonly MeshFilter filter;
            public readonly MeshRenderer renderer;

            public SourceIdentity(
                Mesh sourceMesh,
                Material sourceMaterial,
                MeshFilter sourceFilter,
                MeshRenderer sourceRenderer)
            {
                mesh = sourceMesh;
                material = sourceMaterial;
                filter = sourceFilter;
                renderer = sourceRenderer;
            }
        }
    }
}
