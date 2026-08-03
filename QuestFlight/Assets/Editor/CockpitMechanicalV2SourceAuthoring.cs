using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using QuestFlightLab.Aircraft.CockpitDynamics;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Read-only source audit and source-preserving layout derivation for the
    /// remaining C172 mechanical candidates. It intentionally authors no
    /// hierarchy: blocked candidates remain static until a later, explicitly
    /// reviewed presentation can consume these descriptors.
    /// </summary>
    public static class CockpitMechanicalV2SourceAuthoring
    {
        public const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const float PropellerAxisAgreementMinimum = 0.999f;
        public const float PropellerShaftCenterlineToleranceMeters = 0.02f;

        public readonly struct PropellerLayoutDescriptor
        {
            public readonly Transform propeller;
            public readonly Transform spinner;
            public readonly Vector3 pivotWorld;
            public readonly Vector3 rotationAxisWorld;
            public readonly bool preservesSourceWorldPoses;
            public readonly bool motionEnabled;
            public readonly CockpitMechanicalV2Blocker blocker;

            public PropellerLayoutDescriptor(
                Transform propeller,
                Transform spinner,
                Vector3 pivotWorld,
                Vector3 rotationAxisWorld)
            {
                this.propeller = propeller;
                this.spinner = spinner;
                this.pivotWorld = pivotWorld;
                this.rotationAxisWorld = rotationAxisWorld;
                preservesSourceWorldPoses = true;
                motionEnabled = false;
                blocker = CockpitMechanicalV2Blocker
                    .PropellerPhaseWouldAliasAtQuestRefresh;
            }
        }

        [MenuItem("QuestFlight/Cockpit/Validate Mechanical V2 Frozen Source")]
        public static void ValidateProductPrefab()
        {
            GameObject root =
                PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                if (!ValidateFrozenSource(root, out string error))
                    throw new InvalidOperationException(error);
                Debug.Log(
                    "[QuestFlight][CockpitMechanicalV2] Frozen source identity " +
                    "and fail-closed mechanical layout validated. No hierarchy " +
                    "or runtime asset was changed.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static bool ValidateFrozenSource(
            GameObject root,
            out string error)
        {
            try
            {
                Require(root != null, "Aircraft prefab contents are required.");
                Require(root.name == "ProductionC172AircraftRig",
                    "Mechanical V2 audit may only inspect ProductionC172AircraftRig.");
                ValidateFrozenGlb();

                for (int index = 0;
                     index < CockpitMechanicalV2Contract.RecordCount;
                     index++)
                {
                    CockpitMechanicalV2SourceDescriptor descriptor =
                        CockpitMechanicalV2Contract.GetRecord(index);
                    ValidateSource(
                        root,
                        descriptor.primarySourcePath,
                        descriptor.primaryVertexCount,
                        descriptor.primaryTriangleCount,
                        descriptor.id);
                    if (!string.IsNullOrEmpty(descriptor.secondarySourcePath))
                    {
                        ValidateSource(
                            root,
                            descriptor.secondarySourcePath,
                            descriptor.secondaryVertexCount,
                            descriptor.secondaryTriangleCount,
                            descriptor.id);
                    }
                    if (!string.IsNullOrEmpty(descriptor.tertiarySourcePath))
                    {
                        ValidateSource(
                            root,
                            descriptor.tertiarySourcePath,
                            descriptor.tertiaryVertexCount,
                            descriptor.tertiaryTriangleCount,
                            descriptor.id);
                    }
                    Require(descriptor.PreservesFrozenSources,
                        $"Mechanical descriptor '{descriptor.id}' adds render cost.");
                }

                Transform flap = RequireSource(
                    root,
                    CockpitMechanicalV2Contract.FlapSelectorPath);
                Require(
                    ConnectedComponentCount(RequireMesh(flap)) == 5,
                    "The frozen flap-selector component topology drifted.");

                Transform mergedPedals = RequireSource(
                    root,
                    CockpitMechanicalV2Contract.PedalMergedSourcePath);
                Require(
                    ConnectedComponentCount(RequireMesh(mergedPedals)) == 1229,
                    "The frozen merged pedal/cockpit component topology drifted.");

                Require(
                    CockpitMechanicalV2Contract.TryGetRecord(
                        "wing_flap_selector_static",
                        out CockpitMechanicalV2SourceDescriptor flapRecord) &&
                    !flapRecord.HasRuntimeMotion &&
                    flapRecord.blocker ==
                    CockpitMechanicalV2Blocker
                        .FlapSelectorHingeArcAndDetentsUnproven,
                    "The uncalibrated flap selector must remain fail-closed.");
                Require(
                    CockpitMechanicalV2Contract.TryGetRecord(
                        "rudder_toe_brake_pedals_completed",
                        out CockpitMechanicalV2SourceDescriptor pedalRecord) &&
                    pedalRecord.HasRuntimeMotion &&
                    pedalRecord.auditedRepresentedAssemblyCount == 4 &&
                    pedalRecord.blocker == CockpitMechanicalV2Blocker.None &&
                    pedalRecord.layout ==
                        CockpitMechanicalV2LayoutKind.AuthoredFourPedalRigidBatch &&
                    pedalRecord.axisEvidence ==
                        CockpitMechanicalV2AxisEvidence
                            .AuthoredRudderAndToeBrakeHingeAxes,
                    "The four-pedal derivative must retain its source-audited, " +
                    "symmetry-derived rudder and toe-brake motion contract.");

                Require(
                    TryBuildPropellerLayout(
                        root,
                        out PropellerLayoutDescriptor propellerLayout,
                        out string propellerError),
                    propellerError);
                Require(
                    propellerLayout.preservesSourceWorldPoses &&
                    !propellerLayout.motionEnabled &&
                    propellerLayout.blocker ==
                    CockpitMechanicalV2Blocker
                        .PropellerPhaseWouldAliasAtQuestRefresh,
                    "Propeller layout must remain source-preserving and static.");

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static bool TryBuildPropellerLayout(
            GameObject root,
            out PropellerLayoutDescriptor descriptor,
            out string error)
        {
            descriptor = default;
            try
            {
                Transform propeller = RequireSource(
                    root,
                    CockpitMechanicalV2Contract.PropellerPath);
                Transform spinner = RequireSource(
                    root,
                    CockpitMechanicalV2Contract.SpinnerPath);
                MeshFilter propellerFilter = RequireMeshFilter(propeller);
                MeshFilter spinnerFilter = RequireMeshFilter(spinner);
                Bounds propellerBounds = propellerFilter.sharedMesh.bounds;
                Bounds spinnerBounds = spinnerFilter.sharedMesh.bounds;

                Require(
                    propellerBounds.size.y >= propellerBounds.size.x * 8f &&
                    propellerBounds.size.y >= propellerBounds.size.z * 8f,
                    "The frozen propeller no longer has one dominant blade-span " +
                    "axis in source mesh-local Y.");
                Require(
                    Mathf.Abs(spinnerBounds.size.x - spinnerBounds.size.y) <=
                    Mathf.Max(spinnerBounds.size.x, spinnerBounds.size.y) * 0.05f &&
                    spinnerBounds.size.z >
                    Mathf.Max(spinnerBounds.size.x, spinnerBounds.size.y),
                    "The spinner no longer proves the source mesh-local Z shaft axis.");

                Vector3 propellerAxis =
                    propellerFilter.transform.TransformDirection(Vector3.forward)
                        .normalized;
                Vector3 spinnerAxis =
                    spinnerFilter.transform.TransformDirection(Vector3.forward)
                        .normalized;
                Require(
                    Mathf.Abs(Vector3.Dot(propellerAxis, spinnerAxis)) >=
                    PropellerAxisAgreementMinimum,
                    "Propeller and spinner source-local +Z shaft axes disagree.");
                if (Vector3.Dot(propellerAxis, spinnerAxis) < 0f)
                    spinnerAxis = -spinnerAxis;

                Vector3 propellerCenter =
                    propellerFilter.transform.TransformPoint(
                        propellerBounds.center);
                Vector3 spinnerCenter =
                    spinnerFilter.transform.TransformPoint(
                        spinnerBounds.center);
                Vector3 centerDelta = spinnerCenter - propellerCenter;
                Vector3 perpendicularDelta =
                    centerDelta -
                    propellerAxis * Vector3.Dot(centerDelta, propellerAxis);
                Require(
                    perpendicularDelta.magnitude <=
                    PropellerShaftCenterlineToleranceMeters,
                    "Propeller and spinner are not aligned on one source-proven " +
                    "shaft centerline.");

                descriptor = new PropellerLayoutDescriptor(
                    propeller,
                    spinner,
                    propellerCenter,
                    (propellerAxis + spinnerAxis).normalized);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static void ValidateFrozenGlb()
        {
            string sourcePath =
                AssetDatabase.GUIDToAssetPath(
                    CockpitMechanicalV2Contract.SourceGlbGuid);
            Require(!string.IsNullOrEmpty(sourcePath),
                "The frozen C172 source GUID is missing.");
            string absolutePath =
                Path.GetFullPath(
                    Path.Combine(
                        Directory.GetCurrentDirectory(),
                        sourcePath));
            Require(File.Exists(absolutePath),
                "The frozen C172 source asset is missing.");

            using (FileStream stream = File.OpenRead(absolutePath))
            using (SHA256 sha = SHA256.Create())
            {
                string actual =
                    BitConverter.ToString(sha.ComputeHash(stream))
                        .Replace("-", string.Empty)
                        .ToLowerInvariant();
                Require(
                    string.Equals(
                        actual,
                        CockpitMechanicalV2Contract.SourceGlbSha256,
                        StringComparison.Ordinal),
                    "The frozen C172 source SHA256 drifted.");
            }
        }

        private static void ValidateSource(
            GameObject root,
            string canonicalPath,
            int expectedVertices,
            int expectedTriangles,
            string identity)
        {
            Transform source = RequireSource(root, canonicalPath);
            Mesh mesh = RequireMesh(source);
            Require(mesh.vertexCount == expectedVertices,
                $"Mechanical source '{identity}' vertex count drifted at " +
                $"'{source.name}'.");
            Require(mesh.triangles.Length / 3 == expectedTriangles,
                $"Mechanical source '{identity}' triangle count drifted at " +
                $"'{source.name}'.");
            Require(
                source.GetComponentsInChildren<MeshRenderer>(true).Length == 1,
                $"Mechanical source '{identity}' must retain exactly one renderer.");
        }

        private static Transform RequireSource(
            GameObject root,
            string canonicalPath)
        {
            Transform result = root.transform.Find(canonicalPath);
            if (result != null) return result;

            if (canonicalPath ==
                CockpitMechanicalV2Contract.NoseSteeringStrutPath)
            {
                result = root.transform.Find(
                    CockpitMechanicalV2Contract
                        .AuthoredNoseSteeringStrutPath);
            }
            else if (canonicalPath ==
                     CockpitMechanicalV2Contract.NoseWheelPantPath)
            {
                result = root.transform.Find(
                    CockpitMechanicalV2Contract.AuthoredNoseWheelPantPath);
            }

            Require(result != null,
                $"Frozen mechanical source is missing: {canonicalPath}");
            return result;
        }

        private static Mesh RequireMesh(Transform source)
        {
            return RequireMeshFilter(source).sharedMesh;
        }

        private static MeshFilter RequireMeshFilter(Transform source)
        {
            MeshFilter[] filters =
                source.GetComponentsInChildren<MeshFilter>(true);
            Require(filters.Length == 1,
                $"Frozen source '{source.name}' must own exactly one MeshFilter.");
            Require(filters[0].sharedMesh != null,
                $"Frozen source '{source.name}' has no mesh.");
            return filters[0];
        }

        private static int ConnectedComponentCount(Mesh mesh)
        {
            int[] triangles = mesh.triangles;
            int vertexCount = mesh.vertexCount;
            int[] parents = new int[vertexCount];
            bool[] referenced = new bool[vertexCount];
            for (int index = 0; index < vertexCount; index++)
                parents[index] = index;
            for (int index = 0; index + 2 < triangles.Length; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];
                referenced[a] = true;
                referenced[b] = true;
                referenced[c] = true;
                Union(parents, a, b);
                Union(parents, a, c);
            }

            HashSet<int> roots = new HashSet<int>();
            for (int index = 0; index < vertexCount; index++)
            {
                if (referenced[index])
                    roots.Add(Find(parents, index));
            }
            return roots.Count;
        }

        private static int Find(int[] parents, int value)
        {
            int root = value;
            while (parents[root] != root)
                root = parents[root];
            while (parents[value] != value)
            {
                int next = parents[value];
                parents[value] = root;
                value = next;
            }
            return root;
        }

        private static void Union(int[] parents, int left, int right)
        {
            int leftRoot = Find(parents, left);
            int rightRoot = Find(parents, right);
            if (leftRoot != rightRoot)
                parents[rightRoot] = leftRoot;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
