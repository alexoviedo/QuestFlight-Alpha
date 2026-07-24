#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace QuestFlightLab.PhotorealAerialSurfaceV1.Reflection.Editor
{
    /// <summary>
    /// Deterministically authors the single-draw cockpit-local shadow path. The proxy contains
    /// only cabin elements that visibly define sun silhouettes; it is not a renderable cockpit
    /// replacement and is kept disabled except during the synchronous offscreen render.
    /// </summary>
    public static class CockpitLocalShadowAuthoring
    {
        public const string ShadowRootName = "ProductionCockpitLocalShadow";
        public const string ShadowCameraName = "CockpitLocalShadowCamera";
        public const string ShadowCasterName = "C172_CockpitShadowCasterProxy";
        public const int ShadowLayer = 29;
        public const int ShadowMapResolution = 512;
        public const int MaximumProxyTriangles = 1000;

        public const string Root =
            PhotorealAerialReflectionAuthoring.Root + "/Cockpit";
        public const string ShadowDepthShaderPath =
            Root + "/Shaders/ProductionCockpitLocalShadowDepth.shader";
        public const string ShadowCasterMeshPath =
            Root + "/Meshes/C172_CockpitShadowCasterProxy.asset";
        public const string ShadowMapPath =
            Root + "/Textures/C172_CockpitLocalShadow_512.renderTexture";
        public const string AircraftPrefabPath =
            PhotorealAerialReflectionAuthoring.AircraftPrefabPath;

        private static readonly Vector3 ShadowCenter = new Vector3(0f, 0.92f, 0.04f);

        [MenuItem("QuestFlight/Photoreal Aerial Surface V1/Author Cockpit Local Shadow")]
        public static void AuthorAndIntegrate()
        {
            Shader shader = LoadRequired<Shader>(ShadowDepthShaderPath);
            Mesh mesh = AuthorProxyMesh();
            RenderTexture shadowMap = AuthorShadowMap();
            IntegrateAircraftPrefab(mesh, shadowMap, shader);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateIntegratedFoundation();
            Debug.Log(
                "[QuestFlight][CockpitLocalShadow] AUTHORING_SUCCESS " +
                $"triangles={mesh.triangles.Length / 3} draws=1 map={ShadowMapResolution}.");
        }

        public static void AuthorAndIntegrateBatch()
        {
            try
            {
                AuthorAndIntegrate();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static Mesh AuthorProxyMesh()
        {
            List<Vector3> vertices = new List<Vector3>(128);
            List<int> triangles = new List<int>(256);

            // The center/rear headliner blocks overhead light without extending over the
            // windshield aperture. The earlier full-depth slab occluded the entire panel at
            // KBDU's authored sun angle and produced only uniform darkening.
            AddBox(vertices, triangles,
                new Vector3(0f, 1.39f, -0.30f),
                new Vector3(0.74f, 0.10f, 0.80f),
                Quaternion.identity);

            // Windshield header and center divider.
            AddBox(vertices, triangles,
                new Vector3(0f, 1.31f, 0.49f),
                new Vector3(1.30f, 0.11f, 0.12f),
                Quaternion.identity);
            AddBeam(vertices, triangles,
                new Vector3(0f, 0.76f, 0.49f),
                new Vector3(0f, 1.31f, 0.49f),
                0.050f, 0.11f);

            // Windshield A-pillars follow the visible side boundaries without filling the glass.
            AddBeam(vertices, triangles,
                new Vector3(-0.61f, 0.72f, 0.46f),
                new Vector3(-0.61f, 1.31f, 0.49f),
                0.090f, 0.11f);
            AddBeam(vertices, triangles,
                new Vector3(0.61f, 0.72f, 0.46f),
                new Vector3(0.61f, 1.31f, 0.49f),
                0.090f, 0.11f);

            // Door/window roof rails and rear pillars give side sunlight a C172-like silhouette.
            AddBox(vertices, triangles,
                new Vector3(-0.615f, 1.31f, -0.08f),
                new Vector3(0.09f, 0.10f, 1.02f),
                Quaternion.identity);
            AddBox(vertices, triangles,
                new Vector3(0.615f, 1.31f, -0.08f),
                new Vector3(0.09f, 0.10f, 1.02f),
                Quaternion.identity);
            AddBeam(vertices, triangles,
                new Vector3(-0.615f, 0.66f, -0.58f),
                new Vector3(-0.615f, 1.31f, -0.58f),
                0.095f, 0.11f);
            AddBeam(vertices, triangles,
                new Vector3(0.615f, 0.66f, -0.58f),
                new Vector3(0.615f, 1.31f, -0.58f),
                0.095f, 0.11f);

            // The instrument coaming is the dominant readable shadow across the main panel.
            AddBox(vertices, triangles,
                new Vector3(0f, 0.735f, 0.285f),
                new Vector3(1.18f, 0.075f, 0.19f),
                Quaternion.Euler(-4f, 0f, 0f));

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ShadowCasterMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = ShadowCasterName };
                AssetDatabase.CreateAsset(mesh, ShadowCasterMeshPath);
            }
            else
            {
                mesh.Clear();
                mesh.name = ShadowCasterName;
            }
            mesh.indexFormat = vertices.Count > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            MeshUtility.Optimize(mesh);
            EditorUtility.SetDirty(mesh);

            int triangleCount = mesh.triangles.Length / 3;
            if (mesh.subMeshCount != 1 || triangleCount <= 0 ||
                triangleCount > MaximumProxyTriangles)
                throw new InvalidOperationException(
                    $"Cockpit shadow proxy is invalid: submeshes={mesh.subMeshCount}, " +
                    $"triangles={triangleCount}.");
            return mesh;
        }

        private static RenderTexture AuthorShadowMap()
        {
            RenderTexture shadowMap = AssetDatabase.LoadAssetAtPath<RenderTexture>(ShadowMapPath);
            if (shadowMap == null)
            {
                shadowMap = new RenderTexture(
                    ShadowMapResolution,
                    ShadowMapResolution,
                    16,
                    RenderTextureFormat.RGHalf,
                    RenderTextureReadWrite.Linear)
                {
                    name = "C172 Cockpit Local Shadow 512"
                };
                AssetDatabase.CreateAsset(shadowMap, ShadowMapPath);
            }
            shadowMap.Release();
            shadowMap.width = ShadowMapResolution;
            shadowMap.height = ShadowMapResolution;
            shadowMap.depth = 16;
            shadowMap.format = RenderTextureFormat.RGHalf;
            shadowMap.graphicsFormat = GraphicsFormat.R16G16_SFloat;
            shadowMap.antiAliasing = 1;
            shadowMap.filterMode = FilterMode.Bilinear;
            shadowMap.wrapMode = TextureWrapMode.Clamp;
            shadowMap.useMipMap = true;
            shadowMap.autoGenerateMips = false;
            shadowMap.anisoLevel = 0;
            EditorUtility.SetDirty(shadowMap);
            return shadowMap;
        }

        private static void IntegrateAircraftPrefab(
            Mesh mesh,
            RenderTexture shadowMap,
            Shader shadowDepthShader)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                AircraftReferenceFrameRig rig = root.GetComponent<AircraftReferenceFrameRig>();
                if (rig == null || rig.AircraftVisualRoot == null)
                    throw new InvalidOperationException("Authored aircraft visual root is missing.");
                ProductionAircraftLightingController lighting =
                    rig.AircraftVisualRoot.GetComponent<ProductionAircraftLightingController>();
                if (lighting == null)
                    throw new InvalidOperationException(
                        "Authored aircraft lighting controller is missing.");
                Material reusedCockpitMaterial = rig.AircraftVisualRoot
                    .GetComponentsInChildren<Renderer>(true)
                    .Where(item => item != null &&
                                   item.name.IndexOf("Interior_Body",
                                       StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(item => item.sharedMaterial)
                    .FirstOrDefault(item => item != null);
                if (reusedCockpitMaterial == null)
                    throw new InvalidOperationException(
                        "Existing cockpit material for the disabled proxy is missing.");

                Transform shadowRoot = EnsureUniqueDirectChild(
                    rig.AircraftVisualRoot, ShadowRootName);
                shadowRoot.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                shadowRoot.localScale = Vector3.one;

                Transform cameraTransform = EnsureUniqueDirectChild(
                    shadowRoot, ShadowCameraName);
                cameraTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                cameraTransform.localScale = Vector3.one;
                cameraTransform.gameObject.layer = ShadowLayer;
                Camera camera = cameraTransform.GetComponent<Camera>();
                if (camera == null) camera = cameraTransform.gameObject.AddComponent<Camera>();
                foreach (Camera duplicate in cameraTransform.GetComponents<Camera>().Skip(1).ToArray())
                    UnityEngine.Object.DestroyImmediate(duplicate);
                ConfigureCamera(camera, shadowMap);

                Transform casterTransform = EnsureUniqueDirectChild(
                    shadowRoot, ShadowCasterName);
                casterTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                casterTransform.localScale = Vector3.one;
                casterTransform.gameObject.layer = ShadowLayer;
                MeshFilter filter = casterTransform.GetComponent<MeshFilter>();
                if (filter == null) filter = casterTransform.gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = casterTransform.GetComponent<MeshRenderer>();
                if (renderer == null)
                    renderer = casterTransform.gameObject.AddComponent<MeshRenderer>();
                // Camera.RenderWithShader replaces this material synchronously. Reusing an
                // already-counted cockpit material avoids adding a visible/runtime material.
                renderer.sharedMaterial = reusedCockpitMaterial;
                renderer.enabled = false;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.allowOcclusionWhenDynamic = false;

                SerializedObject serialized = new SerializedObject(lighting);
                SetObject(serialized, "cockpitLocalShadowCamera", camera);
                SetObject(serialized, "cockpitLocalShadowCasterProxy", renderer);
                SetObject(serialized, "cockpitLocalShadowMap", shadowMap);
                SetObject(serialized, "cockpitLocalShadowDepthShader", shadowDepthShader);
                SetVector3(serialized, "cockpitLocalShadowCenter", ShadowCenter);
                SetFloat(serialized, "cockpitLocalShadowCameraDistance", 2.65f);
                SetFloat(serialized, "cockpitLocalShadowStrength",
                    ProductionAircraftLightingController.DefaultCockpitLocalShadowStrength);
                SetFloat(serialized, "cockpitLocalShadowBias",
                    ProductionAircraftLightingController.DefaultCockpitLocalShadowBias);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(lighting);
                EditorUtility.SetDirty(camera);
                EditorUtility.SetDirty(renderer);
                PrefabUtility.SaveAsPrefabAsset(root, AircraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureCamera(Camera camera, RenderTexture shadowMap)
        {
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.white;
            camera.orthographic = true;
            camera.orthographicSize = 1.60f;
            camera.aspect = 1f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 5.40f;
            camera.depth = -100f;
            camera.cullingMask = 1 << ShadowLayer;
            camera.renderingPath = RenderingPath.Forward;
            camera.targetTexture = shadowMap;
            camera.targetDisplay = 0;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = false;
            camera.depthTextureMode = DepthTextureMode.None;
        }

        public static void ValidateIntegratedFoundation()
        {
            Shader shader = LoadRequired<Shader>(ShadowDepthShaderPath);
            Mesh mesh = LoadRequired<Mesh>(ShadowCasterMeshPath);
            RenderTexture shadowMap = LoadRequired<RenderTexture>(ShadowMapPath);
            if (mesh.subMeshCount != 1 || mesh.triangles.Length / 3 > MaximumProxyTriangles)
                throw new InvalidOperationException("Shadow proxy violates the one-draw budget.");
            if (shadowMap.width != ShadowMapResolution ||
                shadowMap.height != ShadowMapResolution ||
                shadowMap.graphicsFormat != GraphicsFormat.R16G16_SFloat ||
                !shadowMap.useMipMap ||
                shadowMap.autoGenerateMips ||
                shadowMap.filterMode != FilterMode.Bilinear)
                throw new InvalidOperationException("Shadow map import/runtime contract is invalid.");

            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(AircraftPrefabPath);
            if (root == null) throw new InvalidOperationException("Aircraft prefab is missing.");
            AircraftReferenceFrameRig rig = root.GetComponent<AircraftReferenceFrameRig>();
            ProductionAircraftLightingController lighting =
                root.GetComponentInChildren<ProductionAircraftLightingController>(true);
            if (rig == null || rig.AircraftVisualRoot == null || lighting == null)
                throw new InvalidOperationException("Aircraft authority is incomplete.");
            Transform shadowRoot = FindDescendant(rig.AircraftVisualRoot, ShadowRootName);
            if (shadowRoot == null || shadowRoot.parent != rig.AircraftVisualRoot)
                throw new InvalidOperationException("Shadow root is not explicitly authored.");
            Camera[] cameras = shadowRoot.GetComponentsInChildren<Camera>(true);
            MeshRenderer[] renderers = shadowRoot.GetComponentsInChildren<MeshRenderer>(true);
            if (cameras.Length != 1 || renderers.Length != 1)
                throw new InvalidOperationException(
                    "Cockpit-local shadow must use exactly one camera and one renderer.");
            Camera camera = cameras[0];
            MeshRenderer renderer = renderers[0];
            if (camera.enabled || camera.targetTexture != shadowMap ||
                camera.cullingMask != 1 << ShadowLayer ||
                camera.stereoTargetEye != StereoTargetEyeMask.None ||
                !camera.orthographic || camera.allowHDR || camera.allowMSAA ||
                camera.useOcclusionCulling)
                throw new InvalidOperationException("Shadow camera is not Quest-bounded.");
            if (renderer.enabled || renderer.gameObject.layer != ShadowLayer ||
                renderer.sharedMaterial == null ||
                renderer.GetComponent<MeshFilter>().sharedMesh != mesh ||
                renderer.shadowCastingMode != ShadowCastingMode.Off ||
                renderer.receiveShadows)
                throw new InvalidOperationException("Shadow proxy binding is invalid.");
            if (shadowRoot.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("Cockpit-local shadow may not add a light.");

            SerializedObject serialized = new SerializedObject(lighting);
            if (serialized.FindProperty("cockpitLocalShadowCamera").objectReferenceValue != camera ||
                serialized.FindProperty("cockpitLocalShadowCasterProxy").objectReferenceValue != renderer ||
                serialized.FindProperty("cockpitLocalShadowMap").objectReferenceValue != shadowMap ||
                serialized.FindProperty("cockpitLocalShadowDepthShader").objectReferenceValue != shader)
                throw new InvalidOperationException("Shadow controller references are stale.");
            Transform mainCamera = FindDescendant(root.transform, "Main Camera");
            if (mainCamera == null || mainCamera.IsChildOf(shadowRoot))
                throw new InvalidOperationException("OpenXR camera ownership was altered.");
        }

        private static void AddBeam(
            List<Vector3> vertices,
            List<int> triangles,
            Vector3 start,
            Vector3 end,
            float width,
            float depth)
        {
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < 0.000001f)
                throw new ArgumentException("Beam endpoints must differ.");
            AddBox(
                vertices,
                triangles,
                (start + end) * 0.5f,
                new Vector3(width, delta.magnitude, depth),
                Quaternion.FromToRotation(Vector3.up, delta.normalized));
        }

        private static void AddBox(
            List<Vector3> vertices,
            List<int> triangles,
            Vector3 center,
            Vector3 size,
            Quaternion rotation)
        {
            int baseIndex = vertices.Count;
            Vector3 half = size * 0.5f;
            for (int z = -1; z <= 1; z += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int x = -1; x <= 1; x += 2)
                vertices.Add(center + rotation * Vector3.Scale(
                    half, new Vector3(x, y, z)));

            // Indices match the x-fastest/y/z vertex order above.
            int[] faces =
            {
                0, 2, 3, 0, 3, 1,
                4, 5, 7, 4, 7, 6,
                0, 1, 5, 0, 5, 4,
                2, 6, 7, 2, 7, 3,
                0, 4, 6, 0, 6, 2,
                1, 3, 7, 1, 7, 5
            };
            for (int index = 0; index < faces.Length; index++)
                triangles.Add(baseIndex + faces[index]);
        }

        private static Transform EnsureUniqueDirectChild(Transform parent, string name)
        {
            Transform result = null;
            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                Transform child = parent.GetChild(index);
                if (!string.Equals(child.name, name, StringComparison.Ordinal)) continue;
                if (result == null) result = child;
                else UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            if (result != null) return result;
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject.transform;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (string.Equals(root.name, name, StringComparison.Ordinal)) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void SetObject(
            SerializedObject serialized,
            string propertyName,
            UnityEngine.Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new InvalidOperationException("Missing serialized property: " + propertyName);
            property.objectReferenceValue = value;
        }

        private static void SetFloat(
            SerializedObject serialized,
            string propertyName,
            float value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new InvalidOperationException("Missing serialized property: " + propertyName);
            property.floatValue = value;
        }

        private static void SetVector3(
            SerializedObject serialized,
            string propertyName,
            Vector3 value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new InvalidOperationException("Missing serialized property: " + propertyName);
            property.vector3Value = value;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException(
                    $"Required {typeof(T).Name} is missing: {path}");
            return asset;
        }
    }
}
#endif
