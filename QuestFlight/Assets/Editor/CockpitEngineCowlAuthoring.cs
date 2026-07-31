using System;
using System.Linq;
using QuestFlightLab.Aircraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Editor
{
    /// <summary>
    /// Creates a cockpit-only, inward-facing C172 engine-cowl liner. The asymmetric
    /// firewall station seals the upper sightline while keeping its side and lower
    /// vertices forward of the occupied footwell. A dedicated dark liner material
    /// prevents the inside of the cowl from reading like exterior white paint.
    /// </summary>
    public static class CockpitEngineCowlAuthoring
    {
        public const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string SourceRendererName =
            "Cessna_Exterior_Body_MAT_0_Body_MAT_0";
        public const string CowlObjectName = "Cessna_Interior_EngineCowlLiner";
        public const string LegacyCowlObjectName = "Cessna_Exterior_CockpitEngineCowlInterior";
        public const string RejectedClosureObjectName = "Cessna_Interior_FootwellClosure";
        public const string MeshPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Cockpit/Meshes/" +
            "C172_CockpitEngineCowlInterior.asset";
        public const string MaterialPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Cockpit/Materials/" +
            "C172_CockpitEngineCowlInterior.mat";
        public const string StableInteriorShaderPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Cockpit/Shaders/" +
            "ProductionCockpitStableInterior.shader";

        // Bounds were measured from the imported exterior in AircraftVisualRoot space.
        // The pilot eye is (-0.28, 0.94, -0.23), and the panel ends near z=0.41.
        // Three six-sided elliptical stations preserve the tapered cowl silhouette while
        // fitting the scene's remaining serialized-triangle headroom.
        public const int ExpectedTriangleCount = 30;

        [MenuItem("QuestFlight/Cockpit/Author Inward Engine Cowl")]
        public static void Author()
        {
            EnsureFolderForAsset(MeshPath);
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                Transform visual = Find(root.transform, "AircraftVisualRoot");
                Transform cockpit = Find(visual, "ImportedCockpitInterior");
                Transform exterior = Find(visual, "ImportedAircraftExterior");
                if (visual == null || cockpit == null || exterior == null)
                    throw new InvalidOperationException("Authored aircraft hierarchy is incomplete.");

                foreach (Transform rejected in visual.GetComponentsInChildren<Transform>(true)
                             .Where(item => string.Equals(
                                 item.name, RejectedClosureObjectName, StringComparison.Ordinal)).ToArray())
                    UnityEngine.Object.DestroyImmediate(rejected.gameObject);

                MeshRenderer sourceRenderer = exterior.GetComponentsInChildren<MeshRenderer>(true)
                    .Single(item => string.Equals(item.name, SourceRendererName, StringComparison.Ordinal));
                MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                if (sourceFilter == null || sourceFilter.sharedMesh == null)
                    throw new InvalidOperationException("Imported C172 exterior body mesh is missing.");
                if (sourceRenderer.sharedMaterials.Length != 1 ||
                    sourceRenderer.sharedMaterial == null)
                    throw new InvalidOperationException("Exterior cowl source must remain one material.");

                Mesh cowlMesh = EnsureCowlMesh();
                Material cowlMaterial = EnsureCowlMaterial();
                Transform[] matches = cockpit.GetComponentsInChildren<Transform>(true)
                    .Where(item =>
                        string.Equals(item.name, CowlObjectName, StringComparison.Ordinal) ||
                        string.Equals(item.name, LegacyCowlObjectName, StringComparison.Ordinal))
                    .ToArray();
                Transform cowl = matches.FirstOrDefault();
                foreach (Transform duplicate in matches.Skip(1))
                    UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
                if (cowl == null)
                {
                    cowl = new GameObject(CowlObjectName).transform;
                    cowl.SetParent(cockpit, false);
                }
                cowl.name = CowlObjectName;
                cowl.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                cowl.localScale = Vector3.one;
                cowl.gameObject.layer = 0;

                MeshFilter filter = cowl.GetComponent<MeshFilter>();
                if (filter == null) filter = cowl.gameObject.AddComponent<MeshFilter>();
                MeshRenderer renderer = cowl.GetComponent<MeshRenderer>();
                if (renderer == null) renderer = cowl.gameObject.AddComponent<MeshRenderer>();
                foreach (Collider collider in cowl.GetComponents<Collider>())
                    UnityEngine.Object.DestroyImmediate(collider);

                filter.sharedMesh = cowlMesh;
                renderer.sharedMaterial = cowlMaterial;
                renderer.enabled = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage =
                    ReflectionProbeUsage.BlendProbesAndSkybox;
                renderer.probeAnchor = null;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
                GameObjectUtility.SetStaticEditorFlags(cowl.gameObject, (StaticEditorFlags)0);

                AircraftVisibilityProfileController visibility =
                    visual.GetComponent<AircraftVisibilityProfileController>();
                if (visibility == null)
                    throw new InvalidOperationException("Aircraft visibility profile is missing.");
                SerializedObject serialized = new SerializedObject(visibility);
                SerializedProperty interiors = serialized.FindProperty("interiorRenderers");
                Renderer[] current = Enumerable.Range(0, interiors.arraySize)
                    .Select(index => interiors.GetArrayElementAtIndex(index).objectReferenceValue as Renderer)
                    .Where(item => item != null &&
                                   !string.Equals(item.name, RejectedClosureObjectName,
                                       StringComparison.Ordinal) &&
                                   item != renderer)
                    .Append(renderer)
                    .ToArray();
                interiors.arraySize = current.Length;
                for (int index = 0; index < current.Length; index++)
                    interiors.GetArrayElementAtIndex(index).objectReferenceValue = current[index];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(visibility);

                PrefabUtility.SaveAsPrefabAsset(root, AircraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Validate();
            Mesh authored = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            Debug.Log(
                $"[QuestFlight][CockpitCowl] Authored inward-facing actual-cowl derivative: " +
                $"triangles={authored.triangles.Length / 3}, bounds={authored.bounds}; " +
                "one dark cockpit-liner material, one cockpit-only draw, zero new textures.");
        }

        public static void AuthorBatch() => Author();
        public static void ValidateBatch() => Validate();

        public static void Validate()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(AircraftPrefabPath);
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (root == null || mesh == null)
                throw new InvalidOperationException("Cockpit engine-cowl assets are missing.");
            Transform visual = Find(root.transform, "AircraftVisualRoot");
            Transform cockpit = Find(visual, "ImportedCockpitInterior");
            Transform exterior = Find(visual, "ImportedAircraftExterior");
            if (Find(visual, RejectedClosureObjectName) != null)
                throw new InvalidOperationException("Rejected footwell tub is still serialized.");
            if (Find(visual, LegacyCowlObjectName) != null)
                throw new InvalidOperationException("Legacy bright cockpit-cowl object is still serialized.");
            Transform[] cowls = cockpit.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, CowlObjectName, StringComparison.Ordinal))
                .ToArray();
            if (cowls.Length != 1)
                throw new InvalidOperationException("Exactly one cockpit-only engine cowl is required.");

            MeshRenderer renderer = cowls[0].GetComponent<MeshRenderer>();
            MeshFilter filter = cowls[0].GetComponent<MeshFilter>();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (renderer == null || filter == null || filter.sharedMesh != mesh ||
                material == null || renderer.sharedMaterial != material)
                throw new InvalidOperationException("Cockpit cowl does not use its shaded inner liner.");
            if (material.shader == null ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(material.shader),
                    StableInteriorShaderPath,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Cockpit cowl liner is not using the stable interior shader.");
            int triangleCount = mesh.triangles.Length / 3;
            if (mesh.subMeshCount != 1 || triangleCount != ExpectedTriangleCount)
                throw new InvalidOperationException(
                    $"Cockpit cowl triangle count {triangleCount} violates its bounded contract.");
            Bounds bounds = mesh.bounds;
            if (bounds.min.z < 0.25f || bounds.max.z < 1.85f ||
                bounds.size.x < 0.65f || bounds.size.x > 1.65f ||
                bounds.size.y < 0.65f || bounds.size.y > 1.90f)
                throw new InvalidOperationException("Cockpit cowl no longer represents the forward fuselage.");
            if (!renderer.enabled || renderer.shadowCastingMode != ShadowCastingMode.Off ||
                renderer.receiveShadows || renderer.lightProbeUsage != LightProbeUsage.Off ||
                renderer.reflectionProbeUsage !=
                    ReflectionProbeUsage.BlendProbesAndSkybox ||
                cowls[0].GetComponents<Collider>().Length != 0)
                throw new InvalidOperationException("Cockpit cowl violates the Quest-safe renderer contract.");

            SerializedObject serialized =
                new SerializedObject(visual.GetComponent<AircraftVisibilityProfileController>());
            SerializedProperty interiors = serialized.FindProperty("interiorRenderers");
            int references = Enumerable.Range(0, interiors.arraySize).Count(index =>
                interiors.GetArrayElementAtIndex(index).objectReferenceValue == renderer);
            if (references != 1)
                throw new InvalidOperationException("Cockpit cowl must appear once in the interior profile.");
        }

        private static Mesh EnsureCowlMesh()
        {
            const int sides = 6;
            var stations = new[]
            {
                // z, vertical center, half width, half height. Station zero is
                // overridden below by the asymmetric firewall profile.
                new Vector4(0.58f, 0.18f, 0.44f, 0.50f),
                new Vector4(1.22f, 0.25f, 0.49f, 0.50f),
                new Vector4(1.94f, 0.37f, 0.22f, 0.28f)
            };
            var vertices = new Vector3[stations.Length * sides + 1];
            var uv = new Vector2[vertices.Length];
            // RGBA stores low-frequency sun visibility from aircraft +Z/+X/-Z/-X.
            // The cowl interior is enclosed in every direction, not exposed white paint.
            var colors = Enumerable.Repeat(
                new Color(0.20f, 0.24f, 0.18f, 0.20f), vertices.Length).ToArray();
            for (int stationIndex = 0; stationIndex < stations.Length; stationIndex++)
            {
                Vector4 station = stations[stationIndex];
                for (int side = 0; side < sides; side++)
                {
                    float angle = side * Mathf.PI * 2f / sides;
                    int vertex = stationIndex * sides + side;
                    vertices[vertex] = new Vector3(
                        Mathf.Sin(angle) * station.z,
                        station.y + Mathf.Cos(angle) * station.w,
                        station.x);
                    uv[vertex] = new Vector2(
                        side / (float)sides,
                        stationIndex / (float)(stations.Length - 1));
                }
            }
            // Only the top bridge reaches rearward to the upper firewall. The side and
            // lower vertices remain forward and narrower, eliminating the two bright
            // tabs and preserving the pilot's full footwell volume.
            // Keep the bridge just forward of the panel/firewall plane. The previous
            // z=0.28 apex crossed the panel and exposed its two adjacent dark triangles.
            vertices[0] = new Vector3(0.00f, 0.90f, 0.46f);
            vertices[1] = new Vector3(0.43f, 0.52f, 0.58f);
            vertices[2] = new Vector3(0.40f, -0.12f, 0.70f);
            vertices[3] = new Vector3(0.00f, -0.30f, 0.74f);
            vertices[4] = new Vector3(-0.40f, -0.12f, 0.70f);
            vertices[5] = new Vector3(-0.43f, 0.52f, 0.58f);
            int noseCenter = vertices.Length - 1;
            vertices[noseCenter] = new Vector3(0f, stations[2].y, 1.95f);
            uv[noseCenter] = new Vector2(0.5f, 1f);

            var triangles = new int[ExpectedTriangleCount * 3];
            int triangleIndex = 0;
            for (int station = 0; station < stations.Length - 1; station++)
            for (int side = 0; side < sides; side++)
            {
                int nextSide = (side + 1) % sides;
                int a = station * sides + side;
                int b = station * sides + nextSide;
                int c = (station + 1) * sides + side;
                int d = (station + 1) * sides + nextSide;
                // Reversed inward-facing winding.
                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = d;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = d;
            }
            for (int side = 0; side < sides; side++)
            {
                int nextSide = (side + 1) % sides;
                triangles[triangleIndex++] = (stations.Length - 1) * sides + side;
                triangles[triangleIndex++] = noseCenter;
                triangles[triangleIndex++] = (stations.Length - 1) * sides + nextSide;
            }

            var authored = new Mesh
            {
                name = "C172_CockpitEngineCowlInterior",
                indexFormat = IndexFormat.UInt16,
                vertices = vertices,
                uv = uv,
                colors = colors,
                triangles = triangles
            };
            authored.RecalculateNormals();
            // The generated triangles face inward; preserve that lighting frame.
            authored.RecalculateTangents();
            authored.RecalculateBounds();
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(authored, MeshPath);
                return authored;
            }
            EditorUtility.CopySerialized(authored, existing);
            UnityEngine.Object.DestroyImmediate(authored);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Material EnsureCowlMaterial()
        {
            EnsureFolderForAsset(MaterialPath);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(StableInteriorShaderPath);
            if (shader == null)
                throw new InvalidOperationException("Stable cockpit-interior shader is missing.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "C172 Cockpit Engine Cowl Interior" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetColor("baseColorFactor", new Color(0.065f, 0.070f, 0.075f, 1f));
            material.SetFloat("metallicFactor", 0f);
            material.SetFloat("roughnessFactor", 0.86f);
            material.SetFloat("normalTexture_scale", 0f);
            material.SetFloat("occlusionTexture_strength", 0f);
            material.SetColor("emissiveFactor", Color.black);
            material.SetFloat("_CabinDepthStrength", 0.42f);
            material.SetFloat("_DirectionalCabinShadowStrength", 0.58f);
            material.SetFloat("_FootwellEnclosureStrength", 0.70f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            if (string.Equals(root.name, name, StringComparison.Ordinal)) return root;
            foreach (Transform child in root)
            {
                Transform found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void EnsureFolderForAsset(string assetPath)
        {
            string folder = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
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
    }
}
