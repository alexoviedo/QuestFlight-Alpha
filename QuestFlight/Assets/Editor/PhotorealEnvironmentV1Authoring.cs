using System;
using System.Collections.Generic;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Final, deterministic serialized integration pass for Photoreal Standalone
    /// Environment V1. This pass never creates or repairs product content at runtime.
    /// Run it after the reusable sky, terrain, airport-context, and reservoir assets
    /// have been authored.
    /// </summary>
    public static class PhotorealEnvironmentV1Authoring
    {
        public const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        public const string EnvironmentPrefabPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string WorldPrefabPath =
            "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        public const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";
        public const string ContextPrefabPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_BatchedContext.prefab";
        public const string SkyCubemapPath =
            "Assets/Production/Environment/Sky/Kloppenheim03_PureSky_2K.hdr";
        public const string SkyMaterialPath =
            "Assets/Production/Environment/Sky/Kloppenheim03_PureSky_Skybox.mat";
        public const string AircraftShadowFolder =
            "Assets/Production/Environment/Presentation";
        public const string AircraftShadowMeshPath =
            AircraftShadowFolder + "/AircraftRunwayBlobShadow.asset";
        public const string AircraftShadowMaterialPath =
            AircraftShadowFolder + "/AircraftRunwayBlobShadow.mat";
        public const string AircraftShadowShaderName =
            "QuestFlightLab/Aircraft Runway Blob Shadow";

        public const float SkyRotationDegrees = 0f;
        public const float SkyExposure = 0.74f;
        public const float PhotographedSunElevationDegrees = 58.623046875f;
        public const float PhotographedSunAzimuthDegrees = 36.123046875f;
        public const string DeterministicBakeUtc = "2026-07-16T21:42:00Z";

        private static readonly string[] TerrainRendererNames =
        {
            "Terrain_AirportPatch_USGS",
            "Terrain_AirportPatch_Near_Transition",
            "Terrain_Near4km_USGS",
            "Terrain_Mid12km_USGS",
            "Terrain_Far24km_Immutable_USGS"
        };

        [MenuItem("QuestFlight/Product/Integrate Photoreal Standalone Environment V1")]
        public static void RunInteractive() => Integrate();

        public static void RunBatch()
        {
            try
            {
                Integrate();
                Debug.Log("[QuestFlight][PhotorealEnvironmentV1] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void ValidateBatch()
        {
            try
            {
                ValidateIntegratedAssets();
                Debug.Log("[QuestFlight][PhotorealEnvironmentV1Validation] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Integrate()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Material terrainMaterial = LoadRequired<Material>(TerrainMaterialPath);
            GameObject contextPrefab = LoadRequired<GameObject>(ContextPrefabPath);
            Cubemap skyCubemap = LoadRequired<Cubemap>(SkyCubemapPath);
            Material skyMaterial = LoadRequired<Material>(SkyMaterialPath);
            EnsureAircraftShadowAssets(out Mesh shadowMesh, out Material shadowMaterial);

            if (skyMaterial.shader == null || skyMaterial.shader.name != "Skybox/Cubemap" ||
                skyMaterial.GetTexture("_Tex") != skyCubemap)
                throw new InvalidOperationException("The selected HDR skybox asset contract is incomplete.");
            skyMaterial.SetFloat("_Rotation", SkyRotationDegrees);
            skyMaterial.SetFloat("_Exposure", SkyExposure);
            EditorUtility.SetDirty(skyMaterial);

            ReservoirWaterMaterialAuthoring.ConfigureProductionMaterial(
                skyCubemap,
                SkyRotationDegrees,
                0.78f);

            IntegrateEnvironmentPrefab(terrainMaterial, contextPrefab, skyCubemap);
            IntegrateWorldLightingPrefab(shadowMesh, shadowMaterial);
            IntegrateSceneRenderSettings(skyMaterial, skyCubemap);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateIntegratedAssets();
        }

        private static void IntegrateEnvironmentPrefab(
            Material terrainMaterial,
            GameObject contextPrefab,
            Cubemap skyCubemap)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(EnvironmentPrefabPath);
            try
            {
                foreach (string rendererName in TerrainRendererNames)
                {
                    Transform terrain = FindDescendant(root.transform, rendererName);
                    if (terrain == null)
                        throw new InvalidOperationException("Required terrain renderer object is missing: " + rendererName);
                    MeshRenderer renderer = terrain.GetComponent<MeshRenderer>();
                    if (renderer == null)
                        throw new InvalidOperationException("Required terrain MeshRenderer is missing: " + rendererName);
                    renderer.sharedMaterial = terrainMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    EditorUtility.SetDirty(renderer);
                }

                foreach (Transform existing in root.GetComponentsInChildren<Transform>(true)
                             .Where(item => item != root.transform &&
                                            string.Equals(item.name, "KBDU_BatchedContext", StringComparison.Ordinal))
                             .OrderByDescending(item => HierarchyDepth(item))
                             .ToArray())
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);

                GameObject context = (GameObject)PrefabUtility.InstantiatePrefab(contextPrefab, root.transform);
                if (context == null)
                    throw new InvalidOperationException("Failed to instantiate the batched KBDU context prefab.");
                context.name = "KBDU_BatchedContext";
                context.transform.localPosition = Vector3.zero;
                context.transform.localRotation = Quaternion.identity;
                context.transform.localScale = Vector3.one;

                Transform waterObject = FindDescendant(root.transform, "BoulderReservoir_OSM_AuthoritativeWater");
                MeshRenderer waterRenderer = waterObject != null ? waterObject.GetComponent<MeshRenderer>() : null;
                Material waterMaterial = LoadRequired<Material>(ReservoirWaterMaterialAuthoring.MaterialPath);
                if (waterRenderer == null)
                    throw new InvalidOperationException("The authoritative Boulder Reservoir renderer is missing.");
                waterRenderer.sharedMaterial = waterMaterial;
                waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
                waterRenderer.receiveShadows = false;
                waterRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                EditorUtility.SetDirty(waterRenderer);
                if (waterMaterial.GetTexture("_EnvironmentCube") != skyCubemap)
                    throw new InvalidOperationException("The reservoir does not reference the selected HDR sky cubemap.");

                ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
                if (contract == null)
                    throw new InvalidOperationException("ProductionEnvironmentRoot contract marker is missing.");
                contract.uniqueMacroAlbedo = LoadRequired<Texture2D>(
                    "Assets/Production/Environment/Textures/KBDU_NAIP_2019_Mid12km_4096.jpg");
                contract.macroSource =
                    "USGS NAIP Plus 2019 public-domain aligned near/mid/far KBDU imagery pyramid";
                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                contract.bakedTextureCount = CountReferencedTextures(root);
                contract.bakeUtc = DeterministicBakeUtc;
                contract.bakeToolVersion = "photoreal_environment_v1";
                EditorUtility.SetDirty(contract);

                PrefabUtility.SaveAsPrefabAsset(root, EnvironmentPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void IntegrateWorldLightingPrefab(Mesh shadowMesh, Material shadowMaterial)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(WorldPrefabPath);
            try
            {
                Light[] directionalLights = root.GetComponentsInChildren<Light>(true)
                    .Where(light => light.type == LightType.Directional && light.enabled &&
                                    light.gameObject.activeInHierarchy)
                    .ToArray();
                if (directionalLights.Length != 1)
                    throw new InvalidOperationException(
                        "Expected exactly one enabled directional sun, found " + directionalLights.Length + ".");

                Light sun = directionalLights[0];
                sun.name = "Sun";
                float elevation = PhotographedSunElevationDegrees * Mathf.Deg2Rad;
                float azimuth = PhotographedSunAzimuthDegrees * Mathf.Deg2Rad;
                Vector3 photographedSunDirection = new Vector3(
                    Mathf.Cos(elevation) * Mathf.Sin(azimuth),
                    Mathf.Sin(elevation),
                    Mathf.Cos(elevation) * Mathf.Cos(azimuth));
                // A directional Light's forward vector is the ray direction, opposite
                // the visible surface-to-sun vector encoded by the lat-long HDRI.
                sun.transform.rotation = Quaternion.LookRotation(-photographedSunDirection, Vector3.up);
                sun.color = new Color(1.0f, 0.956f, 0.885f, 1f);
                sun.intensity = 1.00f;
                sun.shadows = LightShadows.None;
                sun.cookie = null;
                sun.renderMode = LightRenderMode.ForcePixel;
                EditorUtility.SetDirty(sun);
                EditorUtility.SetDirty(sun.transform);

                Transform aircraft = FindDescendant(root.transform, "AircraftSimulationRoot");
                ProductionEnvironmentRoot environment = root.GetComponentInChildren<ProductionEnvironmentRoot>(true);
                if (aircraft == null || environment == null)
                    throw new InvalidOperationException(
                        "The authored aircraft or environment reference required by the runway shadow is missing.");

                Transform presentation = root.transform.Cast<Transform>()
                    .FirstOrDefault(child => string.Equals(child.name, "PresentationEffects", StringComparison.Ordinal));
                if (presentation == null)
                {
                    GameObject presentationObject = new GameObject("PresentationEffects");
                    presentation = presentationObject.transform;
                    presentation.SetParent(root.transform, false);
                }

                foreach (Transform duplicate in presentation.Cast<Transform>()
                             .Where(child => string.Equals(
                                 child.name,
                                 "AircraftRunwayBlobShadow",
                                 StringComparison.Ordinal))
                             .ToArray())
                    UnityEngine.Object.DestroyImmediate(duplicate.gameObject);

                GameObject shadowObject = new GameObject("AircraftRunwayBlobShadow");
                shadowObject.transform.SetParent(presentation, false);
                MeshFilter meshFilter = shadowObject.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = shadowMesh;
                MeshRenderer shadowRenderer = shadowObject.AddComponent<MeshRenderer>();
                shadowRenderer.sharedMaterial = shadowMaterial;
                shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
                shadowRenderer.receiveShadows = false;
                shadowRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                shadowRenderer.lightProbeUsage = LightProbeUsage.Off;
                shadowRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                AircraftRunwayBlobShadow shadow = shadowObject.AddComponent<AircraftRunwayBlobShadow>();
                shadow.ConfigureAuthoredReferences(aircraft, environment, shadowRenderer);
                shadow.UpdateShadowPoseNow();
                EditorUtility.SetDirty(shadow);
                EditorUtility.SetDirty(shadowRenderer);
                PrefabUtility.SaveAsPrefabAsset(root, WorldPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void IntegrateSceneRenderSettings(Material skyMaterial, Cubemap skyCubemap)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("The authored product scene could not be opened.");

            Light[] directionalLights = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Light>(true))
                .Where(light => light.enabled && light.gameObject.activeInHierarchy &&
                                light.type == LightType.Directional)
                .ToArray();
            if (directionalLights.Length != 1)
                throw new InvalidOperationException(
                    "Expected exactly one enabled scene directional sun, found " + directionalLights.Length + ".");

            RenderSettings.skybox = skyMaterial;
            RenderSettings.sun = directionalLights[0];
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.86f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = skyCubemap;
            RenderSettings.reflectionIntensity = 0.76f;
            RenderSettings.reflectionBounces = 1;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.57f, 0.65f, 0.735f, 1f);
            RenderSettings.fogStartDistance = 5200f;
            RenderSettings.fogEndDistance = 20500f;

            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Failed to serialize product RenderSettings.");
        }

        private static void ValidateIntegratedAssets()
        {
            Material terrainMaterial = LoadRequired<Material>(TerrainMaterialPath);
            Cubemap skyCubemap = LoadRequired<Cubemap>(SkyCubemapPath);
            Material waterMaterial = LoadRequired<Material>(ReservoirWaterMaterialAuthoring.MaterialPath);
            Mesh shadowMesh = LoadRequired<Mesh>(AircraftShadowMeshPath);
            Material shadowMaterial = LoadRequired<Material>(AircraftShadowMaterialPath);
            if (waterMaterial.GetTexture("_EnvironmentCube") != skyCubemap ||
                waterMaterial.renderQueue >= (int)RenderQueue.Transparent)
                throw new InvalidOperationException("The opaque reservoir reflection contract is not integrated.");

            GameObject environment = PrefabUtility.LoadPrefabContents(EnvironmentPrefabPath);
            try
            {
                Transform[] contextRoots = environment.GetComponentsInChildren<Transform>(true)
                    .Where(item => string.Equals(item.name, "KBDU_BatchedContext", StringComparison.Ordinal))
                    .ToArray();
                if (contextRoots.Length != 1 || contextRoots[0].localPosition != Vector3.zero ||
                    contextRoots[0].localRotation != Quaternion.identity || contextRoots[0].localScale != Vector3.one)
                    throw new InvalidOperationException("Exactly one identity-aligned KBDU batched context is required.");
                GameObject contextSource = PrefabUtility.GetCorrespondingObjectFromSource(contextRoots[0].gameObject);
                if (contextSource == null ||
                    !string.Equals(AssetDatabase.GetAssetPath(contextSource), ContextPrefabPath, StringComparison.Ordinal))
                    throw new InvalidOperationException("KBDU batched context must remain a nested instance of the approved prefab.");

                foreach (string rendererName in TerrainRendererNames)
                {
                    MeshRenderer renderer = FindDescendant(environment.transform, rendererName)?.GetComponent<MeshRenderer>();
                    if (renderer == null || renderer.sharedMaterial != terrainMaterial)
                        throw new InvalidOperationException("Terrain pyramid material binding failed: " + rendererName);
                }

                ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
                if (contract == null || contract.bakeToolVersion != "photoreal_environment_v1")
                    throw new InvalidOperationException("The photoreal environment budget bake is missing.");
                if (!contract.TryValidateContract(out string report))
                    throw new InvalidOperationException("Integrated environment validation failed: " + report);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Material skyMaterial = LoadRequired<Material>(SkyMaterialPath);
            if (RenderSettings.skybox != skyMaterial || RenderSettings.customReflectionTexture != skyCubemap ||
                RenderSettings.defaultReflectionMode != DefaultReflectionMode.Custom ||
                RenderSettings.ambientMode != AmbientMode.Skybox || !RenderSettings.fog)
                throw new InvalidOperationException("Scene sky, reflection, ambient, or haze settings are incomplete.");
            Light[] suns = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Light>(true))
                .Where(light => light.enabled && light.gameObject.activeInHierarchy &&
                                light.type == LightType.Directional)
                .ToArray();
            if (suns.Length != 1 || RenderSettings.sun != suns[0] || suns[0].shadows != LightShadows.None)
                throw new InvalidOperationException("The single photographed-sun lighting contract is incomplete.");

            AircraftRunwayBlobShadow[] shadows = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AircraftRunwayBlobShadow>(true))
                .ToArray();
            if (shadows.Length != 1)
                throw new InvalidOperationException("Exactly one explicitly authored runway blob shadow is required.");
            AircraftRunwayBlobShadow shadow = shadows[0];
            MeshFilter shadowFilter = shadow.GetComponent<MeshFilter>();
            if (shadow.AircraftSimulationRoot == null || shadow.Environment == null ||
                shadow.BlobRenderer == null || shadowFilter == null ||
                shadowFilter.sharedMesh != shadowMesh ||
                shadow.BlobRenderer.sharedMaterial != shadowMaterial ||
                shadow.BlobRenderer.shadowCastingMode != ShadowCastingMode.Off ||
                shadow.BlobRenderer.receiveShadows ||
                shadow.transform.IsChildOf(shadow.AircraftSimulationRoot))
                throw new InvalidOperationException("The bounded, sibling-owned runway shadow contract is incomplete.");
        }

        private static void EnsureAircraftShadowAssets(out Mesh mesh, out Material material)
        {
            EnsureFolder(AircraftShadowFolder);

            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AircraftShadowMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "AircraftRunwayBlobShadow" };
                AssetDatabase.CreateAsset(mesh, AircraftShadowMeshPath);
            }
            else
            {
                mesh.Clear();
                mesh.name = "AircraftRunwayBlobShadow";
            }

            mesh.vertices = new[]
            {
                new Vector3(-6.1f, 0f, -4.5f),
                new Vector3(6.1f, 0f, -4.5f),
                new Vector3(-6.1f, 0f, 4.5f),
                new Vector3(6.1f, 0f, 4.5f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            Shader shader = Shader.Find(AircraftShadowShaderName);
            if (shader == null)
                throw new InvalidOperationException("The project-owned runway shadow shader did not import.");
            material = AssetDatabase.LoadAssetAtPath<Material>(AircraftShadowMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "AircraftRunwayBlobShadow" };
                AssetDatabase.CreateAsset(material, AircraftShadowMaterialPath);
            }
            material.shader = shader;
            material.SetColor("_Color", new Color(0.055f, 0.065f, 0.075f, 1f));
            material.SetFloat("_Opacity", 0.42f);
            material.SetFloat("_CoreRadius", 0.32f);
            material.SetFloat("_EdgeSoftness", 0.68f);
            material.enableInstancing = false;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string folder)
        {
            string[] segments = folder.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }

        private static int CountReferencedTextures(GameObject root)
        {
            HashSet<Texture> textures = new HashSet<Texture>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null) continue;
                foreach (string propertyName in material.GetTexturePropertyNames())
                {
                    Texture texture = material.GetTexture(propertyName);
                    if (texture != null) textures.Add(texture);
                }
            }
            return textures.Count;
        }

        private static Transform FindDescendant(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => string.Equals(item.name, name, StringComparison.Ordinal));

        private static int HierarchyDepth(Transform transform)
        {
            int depth = 0;
            while (transform.parent != null)
            {
                depth++;
                transform = transform.parent;
            }
            return depth;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException("Required " + typeof(T).Name + " asset is missing: " + path);
            return asset;
        }
    }
}
