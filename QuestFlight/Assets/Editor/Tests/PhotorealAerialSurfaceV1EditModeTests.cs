using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class PhotorealAerialSurfaceV1EditModeTests
    {
        private const string EnvironmentPath = ProductionEnvironmentRoot.PrefabAssetPath;
        private const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";
        private const string ShaderPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Materials/ProductionSemanticSurface.shader";
        private const string AircraftPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";

        [Test]
        public void ProductSurfaceContractIsValidationOnly()
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.DeclaredOnly;
            foreach (string method in new[] { "Update", "LateUpdate", "FixedUpdate", "OnRenderObject" })
                Assert.That(typeof(PhotorealAerialSurfaceRoot).GetMethod(method, flags), Is.Null,
                    "The authored surface contract must not mutate product content at runtime: " + method);
        }

        [Test]
        public void AuthoredProductUsesFourMutuallyOwnedSemanticLayers()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            Assert.That(environment, Is.Not.Null);
            PhotorealAerialSurfaceRoot[] roots =
                environment.GetComponentsInChildren<PhotorealAerialSurfaceRoot>(true);
            Assert.That(roots, Has.Length.EqualTo(1));
            PhotorealAerialSurfaceRoot root = roots[0];
            Assert.That(root.TryValidateContract(out string report), Is.True, report);
            Assert.That(root.productionBareEarthTerrain.name,
                Is.EqualTo(PhotorealAerialSurfaceRoot.BareEarthLayerName));
            Assert.That(root.productionBuildingSurfaceLayer.name,
                Is.EqualTo(PhotorealAerialSurfaceRoot.BuildingLayerName));
            Assert.That(root.productionCanopySurfaceLayer.name,
                Is.EqualTo(PhotorealAerialSurfaceRoot.CanopyLayerName));
            Assert.That(root.productionHeroObjectLayer.name,
                Is.EqualTo(PhotorealAerialSurfaceRoot.HeroLayerName));
            Assert.That(root.ownershipExclusionManifest.text, Does.Contain("ProductionHeroObjectLayer"));
            Assert.That(root.ownershipExclusionManifest.text, Does.Contain("ProductionBuildingSurfaceLayer"));
            Assert.That(root.ownershipExclusionManifest.text, Does.Contain("ProductionCanopySurfaceLayer"));
            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(contract.waterPresentationMode,
                Is.EqualTo(ProductionWaterPresentationMode.OrthophotoMaskedReflection));
        }

        [Test]
        public void ImageryPyramidMeetsDensityAndDeterministicResidencyContract()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            PhotorealAerialSurfaceRoot root =
                environment.GetComponentInChildren<PhotorealAerialSurfaceRoot>(true);
            Assert.That(root.nearMetersPerTexel, Is.LessThanOrEqualTo(0.75f));
            Assert.That(root.patternMetersPerTexel, Is.LessThanOrEqualTo(2f));
            Assert.That(root.farMetersPerTexel, Is.LessThanOrEqualTo(6f));
            Assert.That(root.nearOrthophoto.width, Is.EqualTo(8192));
            Assert.That(root.midOrthophoto.width, Is.EqualTo(8192));
            Assert.That(root.farOrthophoto.width, Is.EqualTo(4096));
            Assert.That(root.bakedTextureResidentBytes, Is.LessThanOrEqualTo(210L * 1024L * 1024L));

            Material terrain = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            Assert.That(terrain.GetTexture("_NearAlbedo"), Is.SameAs(root.nearOrthophoto));
            Assert.That(terrain.GetTexture("_MidAlbedo"), Is.SameAs(root.midOrthophoto));
            Assert.That(terrain.GetTexture("_FarAlbedo"), Is.SameAs(root.farOrthophoto));
            Assert.That(terrain.GetTexture("_AerialCanopyHandoffMask"), Is.Not.Null);
            Assert.That(terrain.GetTexture("_AerialCanopyHandoffMask"), Is.Not.SameAs(root.canopyOwnershipMask),
                "Geometry ownership and terrain-map handoff must be distinct masks.");

            Vector4 productToSourceRow0 = terrain.GetVector("_AerialCanopyProductToSourceRow0");
            Vector4 productToSourceRow1 = terrain.GetVector("_AerialCanopyProductToSourceRow1");
            Vector4 sourceToProductRow0 = terrain.GetVector("_AerialCanopySourceToProductRow0");
            Vector4 sourceToProductRow1 = terrain.GetVector("_AerialCanopySourceToProductRow1");
            Assert.That(Mathf.Abs(productToSourceRow0.y), Is.GreaterThan(0.002f),
                "The serialized runtime frame must not silently fall back to identity.");
            Assert.That(Mathf.Abs(productToSourceRow1.x), Is.GreaterThan(0.002f));
            Material[] semanticMaterials = root.canopySurfaceRenderers
                .SelectMany(renderer => renderer.sharedMaterials)
                .Distinct()
                .ToArray();
            foreach (Material semantic in semanticMaterials)
            {
                Assert.That(semantic.GetVector("_CanopyProductToSourceRow0"),
                    Is.EqualTo(productToSourceRow0), semantic.name);
                Assert.That(semantic.GetVector("_CanopyProductToSourceRow1"),
                    Is.EqualTo(productToSourceRow1), semantic.name);
                Assert.That(semantic.GetVector("_CanopySourceToProductRow0"),
                    Is.EqualTo(sourceToProductRow0), semantic.name);
                Assert.That(semantic.GetVector("_CanopySourceToProductRow1"),
                    Is.EqualTo(sourceToProductRow1), semantic.name);
            }

            foreach (Texture2D texture in new[]
                     {
                         root.nearOrthophoto,
                         root.midOrthophoto,
                         root.farOrthophoto
                     })
            {
                string path = AssetDatabase.GetAssetPath(texture);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.mipmapEnabled, Is.True, path);
                Assert.That(importer.streamingMipmaps, Is.True,
                    path + " must be explicitly owned by the authored mip controller.");
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear), path);
                Assert.That(importer.anisoLevel, Is.GreaterThanOrEqualTo(8), path);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), path);
            }

            GameObject aircraft = AssetDatabase.LoadAssetAtPath<GameObject>(AircraftPath);
            GeospatialMipController[] controllers =
                aircraft.GetComponentsInChildren<GeospatialMipController>(true);
            Assert.That(controllers, Has.Length.EqualTo(1));
            Assert.That(controllers[0].TryValidateContract(out string mipReport), Is.True, mipReport);
            Assert.That(controllers[0].near4Km, Is.SameAs(root.nearOrthophoto));
            Assert.That(controllers[0].midBase12Km, Is.SameAs(root.midOrthophoto));
            Assert.That(controllers[0].far24Km, Is.SameAs(root.farOrthophoto));
        }

        [Test]
        public void BuildingsAndRoundedSemanticGrovesAreOfflineBatched()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            PhotorealAerialSurfaceRoot root =
                environment.GetComponentInChildren<PhotorealAerialSurfaceRoot>(true);
            Renderer[] buildings = root.buildingSurfaceRenderers;
            Renderer[] canopy = root.canopySurfaceRenderers;
            AircraftCenteredBuildingClipmapController buildingClipmap =
                root.productionBuildingSurfaceLayer.GetComponent<AircraftCenteredBuildingClipmapController>();
            Assert.That(buildingClipmap, Is.Not.Null);
            Assert.That(buildings, Has.Length.EqualTo(AircraftCenteredBuildingClipmapController.SlotCount));
            Assert.That(canopy, Has.Length.EqualTo(4));
            Assert.That(buildings.SelectMany(item => item.sharedMaterials).Distinct().Count(), Is.EqualTo(1));
            Assert.That(buildings.Select(item => item.GetComponent<MeshFilter>()).SequenceEqual(
                buildingClipmap.pageFilters), Is.True);
            Assert.That(buildings.All(item => item.GetComponent<MeshFilter>().sharedMesh == null), Is.True,
                "Runtime building pages must not be serialized into the product prefab.");
            Assert.That(buildings.All(item => item.sharedMaterial.shader.name ==
                FullMapBuildingClipmapAssetAuthoring.ShaderName), Is.True);
            Assert.That(canopy.SelectMany(item => item.sharedMaterials).Distinct().Count(), Is.EqualTo(1));
            Assert.That(buildingClipmap.bundleDirectory, Is.EqualTo("QuestFlightBuildingClipmap"));
            Assert.That(buildingClipmap.lod0MaximumDistance, Is.EqualTo(650f));
            Assert.That(buildingClipmap.lod1MaximumDistance, Is.EqualTo(1500f));
            Assert.That(buildingClipmap.lodHysteresisMeters, Is.GreaterThan(0f));
            Assert.That(canopy.All(item => item.GetComponent<MeshFilter>().sharedMesh
                    .HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color)), Is.True,
                "Canopy batches require crown/trunk semantic colors.");
            Assert.That(canopy.All(item => item.GetComponent<MeshFilter>().sharedMesh
                    .HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)), Is.True,
                "Canopy batches require atlas UVs for static perforated near foliage.");
            Material canopyMaterial = canopy.Select(item => item.sharedMaterial).Distinct().Single();
            Assert.That(canopyMaterial.GetTexture("_NearFoliageAtlas"), Is.Not.Null);
            Assert.That(canopyMaterial.GetFloat("_FoliageCutoff"), Is.InRange(0.25f, 0.45f));
            Assert.That(canopyMaterial.GetFloat("_Cull"), Is.EqualTo(0f));
            Assert.That(buildings.Concat(canopy).All(item => item.shadowCastingMode ==
                UnityEngine.Rendering.ShadowCastingMode.Off), Is.True);
            Assert.That(root.productionBuildingSurfaceLayer.GetComponentsInChildren<LODGroup>(true), Is.Empty);
            Assert.That(root.productionCanopySurfaceLayer.GetComponentsInChildren<LODGroup>(true), Is.Empty);
            Assert.That(root.productionCanopySurfaceLayer.GetComponentsInChildren<BillboardRenderer>(true), Is.Empty);
            Assert.That(root.productionCanopySurfaceLayer.GetComponentsInChildren<MonoBehaviour>(true)
                .Any(item => item != null && item.GetType().GetMethod("Update",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly) != null), Is.False);
            Assert.That(root.nearBuildingCoveragePercent, Is.GreaterThanOrEqualTo(99f));
            Assert.That(root.midBuildingCoveragePercent, Is.GreaterThanOrEqualTo(98f));
            Assert.That(root.maximumBuildingFootprintInflationPercent, Is.LessThanOrEqualTo(5f));
            Assert.That(root.canopySurfaceManifest.text,
                Does.Contain("volumetric_close_tree_lods_then_oblique_semantic_groves_over_continuous_terrain_canopy"));
            Assert.That(root.canopySurfaceManifest.text,
                Does.Contain("\"near_horizontal_static_foliage_card_count\": 0"));
            Assert.That(root.canopySurfaceManifest.text, Does.Contain("\"mesh_batch_count\": 4"));
            Assert.That(root.canopySurfaceManifest.text,
                Does.Contain("REJECTED_BLOCK_AND_CLIFF_SILHOUETTES"));
            Assert.That(root.representedCanopyComponentCount, Is.GreaterThan(0));
            Assert.That(root.representedCanopyAreaPercentOfSource, Is.InRange(90f, 110f));
            Assert.That(root.medianCanopyHeightAgreementPercent, Is.InRange(100f, 160f));
        }

        [Test]
        public void LidarDataTexturesRemainLinearUncompressedAndWithoutMipChains()
        {
            AssertDataTextureImportContract(
                PhotorealAerialSurfaceV1IntegrationAuthoring.NormalizedHeightPath,
                TextureImporterFormat.R16);
            AssertDataTextureImportContract(
                PhotorealAerialSurfaceV1IntegrationAuthoring.SurfaceClassPath,
                TextureImporterFormat.R8);
        }

        [Test]
        public void NativeSemanticMeshesPreserveUnityLocalSourceBoundsAndCentroids()
        {
            AssertNativeMeshFolder(
                PhotorealAerialSurfaceNativeMeshAuthoring.BuildingSourceRoot,
                PhotorealAerialSurfaceNativeMeshAuthoring.BuildingNativeRoot,
                PhotorealAerialSurfaceNativeMeshAuthoring.ExpectedBuildingBatchCount);
            AssertNativeMeshFolder(
                PhotorealAerialSurfaceNativeMeshAuthoring.CanopySourceRoot,
                PhotorealAerialSurfaceNativeMeshAuthoring.CanopyNativeRoot,
                PhotorealAerialSurfaceNativeMeshAuthoring.ExpectedCanopyBatchCount);
            AssertNativeMeshFolder(
                PhotorealAerialSurfaceNativeMeshAuthoring.TerrainSourceRoot,
                PhotorealAerialSurfaceNativeMeshAuthoring.TerrainNativeRoot,
                PhotorealAerialSurfaceNativeMeshAuthoring.ExpectedTerrainBatchCount,
                true,
                PhotorealAerialSurfaceNativeMeshAuthoring.MaximumTerrainTriangleCount);

            TextAsset report = AssetDatabase.LoadAssetAtPath<TextAsset>(
                PhotorealAerialSurfaceNativeMeshAuthoring.ReportPath);
            Assert.That(report, Is.Not.Null);
            Assert.That(report.text, Does.Contain(
                "questflight.photoreal-aerial-surface-v1.unity-local-native-meshes.v1"));
            Assert.That(report.text, Does.Contain("\"sourceCoordinatesPreserved\": true"));
            Assert.That(report.text, Does.Contain("\"genericObjImporterBypassed\": true"));
            Assert.That(report.text, Does.Contain("\"runtimeConversionRequired\": false"));
            Assert.That(report.text, Does.Contain("\"terrainBatches\""));
            Assert.That(report.text, Does.Contain(
                "\"normalPolicy\": \"area_weighted_shared_source_vertices\""));
        }

        [Test]
        public void ProductReferencesOnlyIdentityTransformedNativeSemanticMeshes()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            PhotorealAerialSurfaceRoot root =
                environment.GetComponentInChildren<PhotorealAerialSurfaceRoot>(true);
            Assert.That(root, Is.Not.Null);

            AircraftCenteredBuildingClipmapController clipmap =
                root.productionBuildingSurfaceLayer.GetComponent<AircraftCenteredBuildingClipmapController>();
            Assert.That(clipmap, Is.Not.Null);
            Assert.That(root.buildingSurfaceRenderers, Has.Length.EqualTo(
                AircraftCenteredBuildingClipmapController.SlotCount));
            Assert.That(root.buildingSurfaceRenderers.All(renderer =>
                renderer.transform.parent == root.productionBuildingSurfaceLayer &&
                renderer.GetComponent<MeshFilter>()?.sharedMesh == null), Is.True,
                "Aircraft-centered page meshes must be bundle-loaded, not serialized into the environment prefab.");
            AssertProductNativeMeshes(root.canopySurfaceRenderers,
                root.productionCanopySurfaceLayer,
                PhotorealAerialSurfaceNativeMeshAuthoring.CanopyNativeRoot);

            ProductionEnvironmentRoot environmentContract =
                environment.GetComponent<ProductionEnvironmentRoot>();
            MeshFilter nearTerrain = environmentContract.nearTerrainMeshes.Single(item =>
                item.name == "Terrain_Near4km_USGS");
            Mesh terrainMesh = nearTerrain.sharedMesh;
            Assert.That(terrainMesh, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(terrainMesh),
                Is.EqualTo(PhotorealAerialSurfaceNativeMeshAuthoring.TerrainNativePath));
            Assert.That(AssetImporter.GetAtPath(
                    PhotorealAerialSurfaceNativeMeshAuthoring.TerrainNativePath),
                Is.Not.TypeOf<ModelImporter>());
            Assert.That(terrainMesh.isReadable, Is.False,
                "Native product-AEQD terrain must not retain a duplicate runtime CPU mesh.");
            AssertIdentityTransformChain(nearTerrain.transform, environment.transform);

            string[] dependencies = AssetDatabase.GetDependencies(EnvironmentPath, true);
            Assert.That(dependencies.Any(path =>
                    path.StartsWith(PhotorealAerialSurfaceNativeMeshAuthoring.BuildingSourceRoot + "/",
                        StringComparison.Ordinal) ||
                    path.StartsWith(PhotorealAerialSurfaceNativeMeshAuthoring.CanopySourceRoot + "/",
                        StringComparison.Ordinal) ||
                    string.Equals(path, PhotorealAerialSurfaceNativeMeshAuthoring.TerrainSourcePath,
                        StringComparison.Ordinal)),
                Is.False, "The product prefab must not depend on generic-imported semantic-surface OBJs.");
            Assert.That(dependencies.Count(path =>
                    path.StartsWith(PhotorealAerialSurfaceNativeMeshAuthoring.BuildingNativeRoot + "/",
                        StringComparison.Ordinal) && path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)),
                Is.Zero, "The obsolete airport-centered context-building batches must leave product dependency closure.");
            Assert.That(dependencies, Does.Contain(FullMapBuildingClipmapAssetAuthoring.DatabasePath));
            Assert.That(dependencies, Does.Contain(FullMapBuildingClipmapAssetAuthoring.MaterialPath));
            Assert.That(dependencies.Count(path =>
                    path.StartsWith(PhotorealAerialSurfaceNativeMeshAuthoring.CanopyNativeRoot + "/",
                        StringComparison.Ordinal) &&
                    Path.GetFileNameWithoutExtension(path).StartsWith(
                        "ProductionCanopySurfaceLayer_", StringComparison.Ordinal) &&
                    path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)),
                Is.EqualTo(PhotorealAerialSurfaceNativeMeshAuthoring.ExpectedCanopyBatchCount));
            Assert.That(dependencies.Count(path => string.Equals(path,
                    PhotorealAerialSurfaceNativeMeshAuthoring.TerrainNativePath,
                    StringComparison.Ordinal)),
                Is.EqualTo(PhotorealAerialSurfaceNativeMeshAuthoring.ExpectedTerrainBatchCount));
        }

        [Test]
        public void SemanticSurfaceShaderIsOpaqueSinglePassAndStereoSafe()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.GetShaderMessages(shader).Where(message =>
                string.Equals(message.severity.ToString(), "Error", StringComparison.OrdinalIgnoreCase)), Is.Empty);
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            PhotorealAerialSurfaceRoot root =
                environment.GetComponentInChildren<PhotorealAerialSurfaceRoot>(true);
            Material[] materials = root.buildingSurfaceRenderers.Concat(root.canopySurfaceRenderers)
                .SelectMany(renderer => renderer.sharedMaterials).Distinct().ToArray();
            Assert.That(materials, Has.Length.EqualTo(2));
            Material canopyMaterial = root.canopySurfaceRenderers.Select(item => item.sharedMaterial).Distinct().Single();
            Material buildingMaterial = root.buildingSurfaceRenderers.Select(item => item.sharedMaterial).Distinct().Single();
            Assert.That(canopyMaterial.shader, Is.SameAs(shader));
            Assert.That(buildingMaterial.shader.name, Is.EqualTo(FullMapBuildingClipmapAssetAuthoring.ShaderName));
            Assert.That(materials.All(material => material.renderQueue <= (int)UnityEngine.Rendering.RenderQueue.Geometry),
                Is.True);
            Assert.That(materials.All(material => material.passCount == 1), Is.True);
            string shaderSource = File.ReadAllText(Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", ShaderPath)));
            Assert.That(shaderSource, Does.Contain("input.semanticData"));
            Assert.That(shaderSource, Does.Contain("input.wallUv"));
            Assert.That(shaderSource, Does.Contain("ApplyFacadeFeatures"));
            Assert.That(shaderSource, Does.Contain("facadeGlintMask"));
            Assert.That(shaderSource, Does.Contain("_FacadeReflectionCube"));
            Assert.That(shaderSource, Does.Not.Contain("floor(worldPosition.xz / 96.0)"),
                "Facade identity must come from the offline semantic footprint, not world-cell randomness.");
            string buildingShaderSource = File.ReadAllText(Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Assets/Shaders/FullMapBuildingClipmap.shader")));
            Assert.That(buildingShaderSource, Does.Contain("WindowMask"));
            Assert.That(buildingShaderSource, Does.Contain("UNITY_SAMPLE_TEXCUBE"));
            Assert.That(buildingShaderSource, Does.Contain("ZWrite On"));
            Assert.That(buildingShaderSource, Does.Not.Contain("Blend SrcAlpha"));
        }

        [Test]
        public void IntegratedEnvironmentStaysInsidePhotorealDevelopmentCeilings()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            ProductionEnvironmentBudget budget = contract.CalculateBudget();
            Assert.That(budget.rendererCount, Is.LessThanOrEqualTo(75));
            Assert.That(budget.triangleCount, Is.LessThanOrEqualTo(650000));
            Assert.That(budget.materialCount, Is.LessThanOrEqualTo(28));
            Assert.That(contract.bakedRendererCount, Is.EqualTo(budget.rendererCount));
            Assert.That(contract.bakedTriangleCount, Is.EqualTo(budget.triangleCount));
            Assert.That(contract.bakedMaterialCount, Is.EqualTo(budget.materialCount));
        }

        [Test]
        public void PhotorealSceneUsesItsExplicitSerializedClosureCeiling()
        {
            ProductIntegrityReport report = ProductIntegrityValidator.Validate();
            Assert.That(ProductIntegrityValidator.MaximumSerializedSceneTriangles, Is.EqualTo(700000),
                "The historical/default integrity ceiling must remain frozen.");
            Assert.That(ProductIntegrityValidator.MaximumPhotorealAerialSurfaceSerializedSceneTriangles,
                Is.EqualTo(800000));
            Assert.That(report.serializedTriangleBudgetContract,
                Is.EqualTo("photoreal_aerial_surface_v1"));
            Assert.That(report.serializedTriangleCeiling,
                Is.EqualTo(ProductIntegrityValidator.MaximumPhotorealAerialSurfaceSerializedSceneTriangles));
            Assert.That(report.triangleCount, Is.LessThanOrEqualTo(report.serializedTriangleCeiling));
        }

        [Test]
        public void CockpitReflectionUsesTheFullCabinWindowMesh()
        {
            GameObject aircraft = AssetDatabase.LoadAssetAtPath<GameObject>(AircraftPath);
            Renderer[] renderers = aircraft.GetComponentsInChildren<Renderer>(true);
            Renderer[] exteriorGlass = renderers.Where(renderer =>
                renderer.name == "Cessna_Exterior_Glass_MAT_0_Glass_MAT_0").ToArray();
            Assert.That(exteriorGlass, Has.Length.EqualTo(1));
            Assert.That(exteriorGlass[0].enabled, Is.True);
            Assert.That(exteriorGlass[0].GetComponent<MeshFilter>().sharedMesh.bounds.size.x,
                Is.GreaterThan(1f));
            Assert.That(exteriorGlass[0].GetComponent<MeshFilter>().sharedMesh.bounds.size.z,
                Is.GreaterThan(2f));
            Assert.That(renderers.Where(renderer => renderer.enabled)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Count(material => material != null && material.name == "ProductionCockpitWindowReflection"),
                Is.EqualTo(1));
        }

        private static void AssertNativeMeshFolder(
            string sourceRoot,
            string nativeRoot,
            int expectedCount,
            bool preserveSourceVertexTopology = false,
            int maximumTriangleCount = 0)
        {
            string sourceDirectory = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", sourceRoot.Replace('/', Path.DirectorySeparatorChar)));
            string[] sources = Directory.Exists(sourceDirectory)
                ? Directory.GetFiles(sourceDirectory, "*.obj", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray()
                : Array.Empty<string>();
            Assert.That(sources, Has.Length.EqualTo(expectedCount));

            string[] sourceNames = sources.Select(Path.GetFileNameWithoutExtension).ToArray();
            Dictionary<string, string> nativeByName = AssetDatabase.FindAssets("t:Mesh", new[] { nativeRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), nativeRoot,
                                   StringComparison.Ordinal) &&
                               sourceNames.Contains(Path.GetFileNameWithoutExtension(path),
                                   StringComparer.Ordinal))
                .ToDictionary(Path.GetFileNameWithoutExtension, StringComparer.Ordinal);
            Assert.That(nativeByName, Has.Count.EqualTo(expectedCount));

            foreach (string sourcePath in sources)
            {
                string name = Path.GetFileNameWithoutExtension(sourcePath);
                Assert.That(nativeByName.TryGetValue(name, out string nativePath), Is.True, name);
                ObjContract source = ReadObjContract(sourcePath);
                Mesh native = AssetDatabase.LoadAssetAtPath<Mesh>(nativePath);
                Assert.That(native, Is.Not.Null, nativePath);
                Assert.That(AssetImporter.GetAtPath(nativePath), Is.Not.TypeOf<ModelImporter>(), nativePath);
                Assert.That(native.isReadable, Is.False,
                    nativePath + " must not retain a duplicate runtime CPU mesh.");
                Assert.That(native.GetIndexCount(0) / 3u, Is.EqualTo((uint)source.triangleCount), name);
                if (preserveSourceVertexTopology)
                    Assert.That(native.vertexCount, Is.EqualTo(source.vertexCount),
                        name + " must preserve source vertex/index topology.");
                if (maximumTriangleCount > 0)
                    Assert.That(source.triangleCount, Is.LessThanOrEqualTo(maximumTriangleCount), name);
                Assert.That(native.bounds.min.x, Is.EqualTo(source.bounds.min.x).Within(0.0005f), name);
                Assert.That(native.bounds.min.z, Is.EqualTo(source.bounds.min.z).Within(0.0005f), name);
                Assert.That(native.bounds.max.x, Is.EqualTo(source.bounds.max.x).Within(0.0005f), name);
                Assert.That(native.bounds.max.z, Is.EqualTo(source.bounds.max.z).Within(0.0005f), name);
                Assert.That(native.bounds.center.x, Is.EqualTo(source.bounds.center.x).Within(0.0005f), name);
                Assert.That(native.bounds.center.z, Is.EqualTo(source.bounds.center.z).Within(0.0005f), name);
                Assert.That(native.bounds.min.y, Is.EqualTo(source.bounds.min.y).Within(0.0005f), name);
                Assert.That(native.bounds.max.y, Is.EqualTo(source.bounds.max.y).Within(0.0005f), name);
                Assert.That(native.bounds.center.y, Is.EqualTo(source.bounds.center.y).Within(0.0005f), name);
            }
        }

        private static void AssertDataTextureImportContract(
            string path,
            TextureImporterFormat androidFormat)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null, path);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default), path);
            Assert.That(importer.sRGBTexture, Is.False, path);
            Assert.That(importer.mipmapEnabled, Is.False, path);
            Assert.That(importer.streamingMipmaps, Is.False, path);
            Assert.That(importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed), path);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), path);

            TextureImporterPlatformSettings defaultPlatform =
                importer.GetPlatformTextureSettings("DefaultTexturePlatform");
            Assert.That(defaultPlatform.overridden, Is.False, path);
            Assert.That(defaultPlatform.format, Is.EqualTo(TextureImporterFormat.Automatic), path);
            Assert.That(defaultPlatform.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed), path);

            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True, path);
            Assert.That(android.format, Is.EqualTo(androidFormat), path);
            Assert.That(android.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed), path);
        }

        private static void AssertIdentityTransformChain(Transform leaf, Transform root)
        {
            Transform current = leaf;
            while (current != null && current != root)
            {
                Assert.That(current.localPosition, Is.EqualTo(Vector3.zero), current.name);
                Assert.That(current.localRotation, Is.EqualTo(Quaternion.identity), current.name);
                Assert.That(current.localScale, Is.EqualTo(Vector3.one), current.name);
                current = current.parent;
            }
            Assert.That(current, Is.SameAs(root), leaf.name + " must be owned by the product environment.");
        }

        private static void AssertProductNativeMeshes(
            IEnumerable<Renderer> renderers,
            Transform expectedParent,
            string nativeRoot)
        {
            foreach (Renderer renderer in renderers)
            {
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.transform.parent, Is.SameAs(expectedParent), renderer.name);
                Assert.That(renderer.transform.localPosition, Is.EqualTo(Vector3.zero), renderer.name);
                Assert.That(renderer.transform.localRotation, Is.EqualTo(Quaternion.identity), renderer.name);
                Assert.That(renderer.transform.localScale, Is.EqualTo(Vector3.one), renderer.name);
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                Assert.That(mesh, Is.Not.Null, renderer.name);
                string path = AssetDatabase.GetAssetPath(mesh);
                Assert.That(path, Does.StartWith(nativeRoot + "/"), renderer.name);
                Assert.That(path, Does.EndWith(".asset"), renderer.name);
                Assert.That(AssetImporter.GetAtPath(path), Is.Not.TypeOf<ModelImporter>(), renderer.name);
            }
        }

        private static ObjContract ReadObjContract(string path)
        {
            bool hasVertex = false;
            int vertexCount = 0;
            Vector3 minimum = Vector3.zero;
            Vector3 maximum = Vector3.zero;
            int triangles = 0;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("v ", StringComparison.Ordinal) ||
                    line.StartsWith("v\t", StringComparison.Ordinal))
                {
                    string[] values = line.Split(new[] { ' ', '\t' },
                        StringSplitOptions.RemoveEmptyEntries);
                    bool isSemanticSource =
                        path.Contains("ProductionBuildingSurfaceLayer_", StringComparison.Ordinal) ||
                        path.Contains("ProductionCanopySurfaceLayer_", StringComparison.Ordinal);
                    Assert.That(values.Length, Is.EqualTo(isSemanticSource ? 8 : 4), path);
                    if (isSemanticSource)
                        for (int channel = 4; channel < 8; channel++)
                            Assert.That(float.Parse(values[channel], CultureInfo.InvariantCulture),
                                Is.InRange(0f, 1f), path);
                    Vector3 value = new Vector3(
                        float.Parse(values[1], CultureInfo.InvariantCulture),
                        float.Parse(values[2], CultureInfo.InvariantCulture),
                        float.Parse(values[3], CultureInfo.InvariantCulture));
                    vertexCount++;
                    if (!hasVertex)
                    {
                        minimum = maximum = value;
                        hasVertex = true;
                    }
                    else
                    {
                        minimum = Vector3.Min(minimum, value);
                        maximum = Vector3.Max(maximum, value);
                    }
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal) ||
                         line.StartsWith("f\t", StringComparison.Ordinal))
                    triangles++;
            }
            Assert.That(hasVertex, Is.True, path);
            Assert.That(triangles, Is.GreaterThan(0), path);
            return new ObjContract
            {
                bounds = new Bounds((minimum + maximum) * 0.5f, maximum - minimum),
                triangleCount = triangles,
                vertexCount = vertexCount
            };
        }

        private sealed class ObjContract
        {
            public Bounds bounds;
            public int triangleCount;
            public int vertexCount;
        }
    }
}
