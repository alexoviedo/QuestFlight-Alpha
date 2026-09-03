using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>Offline authoring for the one explicitly serialized semantic tower root.</summary>
    public static class SemanticTowerAuthoring
    {
        private const string Root = "Assets/Production/Environment/SemanticTowersV1";
        private const string TowerMaterialPath = Root + "/SemanticTowerSharedOpaque.mat";
        private const string TerrainSurfacePath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset";

        [MenuItem("QuestFlight/Semantic Tower Replacement V1/Build Serialized Feature")]
        public static void BuildSerializedFeature()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SemanticTowerAuthorityManifest.Document authority = SemanticTowerAuthorityManifest.Load();
            ConfigureDerivativeImporters(authority);
            Material towerMaterial = EnsureMaterial(TowerMaterialPath);
            ProductionTerrainSurfaceAsset terrain = LoadRequired<ProductionTerrainSurfaceAsset>(TerrainSurfacePath);

            GameObject environment = PrefabUtility.LoadPrefabContents(ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                Transform previous = environment.transform.Find(SemanticTowerRoot.RootName);
                if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);

                GameObject rootObject = new GameObject(SemanticTowerRoot.RootName);
                rootObject.transform.SetParent(environment.transform, false);
                SetStatic(rootObject);
                SemanticTowerRoot marker = rootObject.AddComponent<SemanticTowerRoot>();
                List<LODGroup> lodGroups = new List<LODGroup>();
                List<MeshRenderer> renderers = new List<MeshRenderer>();
                int lod0Triangles = 0;

                foreach (SemanticTowerAuthorityManifest.StructureRecord record in
                         authority.structures.OrderBy(item => item.stable_id, StringComparer.Ordinal))
                {
                    if (!terrain.TrySample(record.product_x_meters, record.product_z_meters,
                            out ProductionTerrainSample sample))
                        throw new InvalidOperationException("Terrain authority is missing beneath " + record.stable_id);
                    GameObject structure = new GameObject(record.stable_id);
                    structure.transform.SetParent(rootObject.transform, false);
                    structure.transform.localPosition = new Vector3(
                        record.product_x_meters, sample.heightMeters, record.product_z_meters);
                    SetStatic(structure);

                    Mesh lod0Mesh = LoadNamedMesh(record.derivative_asset, record.lod0_mesh);
                    Mesh lod1Mesh = LoadNamedMesh(record.derivative_asset, record.lod1_mesh);
                    MeshRenderer lod0 = AddRenderer(structure.transform, "LOD0", lod0Mesh, towerMaterial);
                    MeshRenderer lod1 = AddRenderer(structure.transform, "LOD1", lod1Mesh, towerMaterial);
                    LODGroup lodGroup = structure.AddComponent<LODGroup>();
                    lodGroup.fadeMode = LODFadeMode.None;
                    lodGroup.animateCrossFading = false;
                    lodGroup.SetLODs(new[]
                    {
                        new LOD(record.lod0_screen_height, new Renderer[] { lod0 }),
                        new LOD(record.lod1_screen_height, new Renderer[] { lod1 })
                    });
                    lodGroup.RecalculateBounds();
                    lodGroups.Add(lodGroup);
                    renderers.Add(lod0);
                    renderers.Add(lod1);
                    lod0Triangles += (int)lod0Mesh.GetIndexCount(0) / 3;
                }

                marker.authorityManifest = LoadRequired<TextAsset>(SemanticTowerAuthorityManifest.AssetPath);
                marker.structureLodGroups = lodGroups.ToArray();
                marker.authoredRenderers = renderers.ToArray();
                marker.representedStructureCount = authority.structures.Length;
                marker.excludedBuildingShellCount = authority.exclusions.Length;
                marker.authoredObstructionLightCount =
                    authority.inventory_summary.shipping_active_obstruction_emitters;
                marker.authoredObstructionLightSystemCount =
                    authority.inventory_summary.authored_obstruction_light_systems;
                marker.authoredObstructionFixtureCount =
                    authority.inventory_summary.authored_obstruction_fixtures;
                marker.bakedLod0TriangleCount = lod0Triangles;
                marker.bakedRendererCount = renderers.Count;
                marker.bakedMaterialCount = renderers.Select(renderer => renderer.sharedMaterial).Distinct().Count();

                ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>() ??
                    throw new InvalidOperationException("ProductionEnvironmentRoot marker is missing.");
                contract.semanticTowerRoot = marker;
                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                AerialSceneryRoot aerial = environment.GetComponentInChildren<AerialSceneryRoot>(true);
                if (aerial != null)
                {
                    aerial.bakedRendererCount = budget.rendererCount;
                    aerial.bakedSelectedTriangleCount = budget.triangleCount;
                    aerial.bakedMaterialCount = budget.materialCount;
                    EditorUtility.SetDirty(aerial);
                }
                EditorUtility.SetDirty(marker);
                EditorUtility.SetDirty(contract);
                PrefabUtility.SaveAsPrefabAsset(environment, ProductionEnvironmentRoot.PrefabAssetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateAuthoredFeature();
            Debug.Log("[QuestFlight][SemanticTowerV1] SUCCESS structures=2 exclusions=1 " +
                      "redSystems=1 fixtures=3 shippingActiveEmitters=0");
        }

        public static void BuildBatch()
        {
            try
            {
                BuildSerializedFeature();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void ConfigureDerivativeImporters(SemanticTowerAuthorityManifest.Document authority)
        {
            foreach (string path in authority.structures.Select(record => record.derivative_asset).Distinct())
            {
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter ??
                    throw new FileNotFoundException("Semantic tower derivative importer is missing.", path);
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.meshCompression = ModelImporterMeshCompression.Low;
                importer.isReadable = false;
                importer.optimizeMeshPolygons = true;
                importer.optimizeMeshVertices = true;
                importer.SaveAndReimport();
            }
        }

        private static Material EnsureMaterial(string path)
        {
            Shader shader = Shader.Find("QuestFlight/Semantic Tower Opaque") ??
                throw new InvalidOperationException("Semantic tower opaque shader is missing.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_Tint", Color.white);
            material.SetFloat("_SemanticTowerDiagnosticNight", 0f);
            material.SetFloat("_SemanticTowerDiagnosticTime", -1f);
            material.renderQueue = (int)RenderQueue.Geometry;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static MeshRenderer AddRenderer(Transform parent, string name, Mesh mesh, Material material)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            // Blender-authored derivatives retain a Z-up mesh basis. The FBX
            // model node carries this conversion, but direct sub-mesh references
            // do not, so serialize the same conversion on each renderer child.
            child.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            SetStatic(child);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = true;
            return renderer;
        }

        private static Mesh LoadNamedMesh(string path, string meshName)
        {
            Mesh[] matches = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>()
                .Where(mesh => string.Equals(mesh.name, meshName, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Expected one mesh named {meshName} in {path}, found {matches.Length}.");
            return matches[0];
        }

        private static void SetStatic(GameObject gameObject)
        {
            GameObjectUtility.SetStaticEditorFlags(gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException("Required asset is missing.", path);

        private static void ValidateAuthoredFeature()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(ProductionEnvironmentRoot.PrefabAssetPath);
            SemanticTowerRoot[] roots = environment.GetComponentsInChildren<SemanticTowerRoot>(true);
            if (roots.Length != 1)
                throw new InvalidOperationException($"Expected one SemanticTowerRoot, found {roots.Length}.");
            if (!roots[0].TryValidateContract(out string report))
                throw new InvalidOperationException(report);
        }
    }
}
