using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using QuestFlightLab.Editor;
using QuestFlightLab.Environment;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Produces conservative Editor-side planning evidence for the authored product scene.
    /// This is a static architecture gate; it never represents Editor timing as Quest timing.
    /// </summary>
    public static class QuestPerformanceBudgetBatchRunner
    {
        // The accepted V2 product is the planning baseline for this replacement:
        // 77 draws, 659,877 visible triangles, 38 materials, 290,729,872 texture
        // bytes, and 18 transparent slots. V3 must hold draws/materials/slots,
        // may add at most five percent visible triangles, and may add at most
        // 16 MiB of textures. These remain static Editor ceilings, not measured
        // Quest performance claims.
        private const int DrawCallTarget = 77;
        private const long VisibleTriangleTarget = 692871L;
        private const int MaterialTarget = 38;
        private const long TextureMemoryTargetBytes = 307507088L;
        private const int TransparentMaterialSlotTarget = 18;
        private const string OutputEnvironmentVariable = "QFL_PERFORMANCE_BUDGET_DIR";
        private const string VisualReportEnvironmentVariable = "QFL_VISUAL_QA_REPORT";

        [MenuItem("QuestFlight/Product/Run Quest Performance Budget Audit")]
        public static void RunInteractive() => Run(exitWhenComplete: false);

        public static void RunBatch()
        {
            try
            {
                Run(exitWhenComplete: true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Run(bool exitWhenComplete)
        {
            string outputDirectory = Environment.GetEnvironmentVariable(OutputEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new InvalidOperationException(OutputEnvironmentVariable + " must point to an external artifact directory.");
            outputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputDirectory);

            ProductProjectConfigurator.ConfigureProject();
            Scene scene = EditorSceneManager.OpenScene(ProductProjectConfigurator.ProductionScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("The authored production scene could not be loaded.");

            GameObject[] roots = scene.GetRootGameObjects();
            Renderer[] renderers = roots
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                .ToArray();
            AerialVegetationBatchRenderer[] vegetationBatches = roots
                .SelectMany(root => root.GetComponentsInChildren<AerialVegetationBatchRenderer>(true))
                .Where(batch => batch != null && batch.enabled && batch.gameObject.activeInHierarchy &&
                                batch.SharedMaterial != null && batch.NonEmptyDrawCount > 0)
                .ToArray();
            AirportCloseTreeLodRenderer[] closeTreeLods = roots
                .SelectMany(root => root.GetComponentsInChildren<AirportCloseTreeLodRenderer>(true))
                .Where(batch => batch != null && batch.enabled &&
                                batch.gameObject.activeInHierarchy &&
                                batch.SharedCanopyMaterial != null &&
                                batch.AuthoredMaximumDrawCount > 0)
                .ToArray();
            Camera mainCamera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .Single(camera => camera.CompareTag("MainCamera") &&
                    camera.enabled && camera.gameObject.activeInHierarchy);

            MaterialShaderReport materialReport =
                BuildMaterialShaderReport(renderers, vegetationBatches, closeTreeLods);
            TextureMemoryReport textureReport = BuildTextureReport(materialReport.materials, roots);
            LodCullingReport lodReport =
                BuildLodReport(roots, renderers, vegetationBatches, closeTreeLods);
            StaticViewBudget defaultView = CaptureViewBudget(mainCamera, renderers);
            ProductionVisualQaBatchRunner.ProductionVisualQaReport visualReport = ReadVisualQaReport();

            int maxDrawCalls = visualReport != null ? visualReport.maxEstimatedDrawCalls : defaultView.estimatedDrawCalls;
            long maxVisibleTriangles = visualReport != null ? visualReport.maxEstimatedVisibleTriangles : defaultView.visibleTriangles;
            int sceneMaterials = visualReport != null ? visualReport.sceneMaterialCount : materialReport.materialCount;
            string overdrawDirectory = Path.Combine(outputDirectory, "overdraw_captures");
            Directory.CreateDirectory(overdrawDirectory);
            List<OverdrawCapture> overdraw = CaptureOverdraw(mainCamera, overdrawDirectory);

            GraphicsDeviceType[] androidApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            OpenXRSettings openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            string configuredRenderPipeline = GraphicsSettings.currentRenderPipeline == null
                ? "BuiltIn/" + mainCamera.actualRenderingPath
                : GraphicsSettings.currentRenderPipeline.GetType().FullName;
            PerformanceBudgetReport report = new PerformanceBudgetReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                scenePath = ProductProjectConfigurator.ProductionScenePath,
                evidenceKind = "STATIC_EDITOR_PLANNING_EVIDENCE_ONLY",
                headsetPerformanceEstablished = false,
                headsetPerformanceLimitation = "Editor and Meta XR Simulator results do not establish Quest GPU, CPU, frame-time, thermal, or worn-device performance.",
                drawCallTarget = DrawCallTarget,
                visibleTriangleTarget = VisibleTriangleTarget,
                materialTarget = MaterialTarget,
                textureMemoryTargetBytes = TextureMemoryTargetBytes,
                transparentMaterialSlotTarget = TransparentMaterialSlotTarget,
                maxEstimatedDrawCalls = maxDrawCalls,
                maxEstimatedVisibleTriangles = maxVisibleTriangles,
                sceneMaterialCount = sceneMaterials,
                maxVisibleMaterialCount = sceneMaterials,
                activeSceneMaterialCount = materialReport.materialCount,
                configuredRenderPipeline = configuredRenderPipeline,
                activeRendererCount = renderers.Length + vegetationBatches.Sum(batch => batch.NonEmptyDrawCount),
                sceneMeshTriangles = CountTriangles(renderers) +
                                     vegetationBatches.Sum(batch => batch.FullyInstancedTriangleCount),
                visualQaReportPath = Environment.GetEnvironmentVariable(VisualReportEnvironmentVariable) ?? string.Empty,
                defaultView = defaultView,
                androidArm64Only = PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64,
                androidVulkanOnly = androidApis.Length == 1 && androidApis[0] == GraphicsDeviceType.Vulkan,
                androidAstcBuildTarget = EditorUserBuildSettings.androidBuildSubtarget == MobileTextureSubtarget.ASTC,
                androidIl2Cpp = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP,
                openXrSinglePassInstanced = openXr != null && openXr.renderMode == OpenXRSettings.RenderMode.SinglePassInstanced,
                msaa = QualitySettings.antiAliasing,
                multithreadedRendering = PlayerSettings.GetMobileMTRendering(NamedBuildTarget.Android),
                streamingMipmaps = QualitySettings.streamingMipmapsActive,
                transparentMaterialSlots = materialReport.transparentMaterialSlots,
                shaderCount = materialReport.shaderCount,
                shaderKeywordCombinationCount = materialReport.shaderKeywordCombinationCount,
                textureCount = textureReport.textureCount,
                estimatedTextureBytes = textureReport.estimatedRuntimeBytes,
                lightmapTextureCount = textureReport.lightmapTextureCount,
                estimatedLightmapBytes = textureReport.estimatedLightmapBytes,
                lodGroupCount = lodReport.lodGroupCount,
                crossFadeLodGroupCount = lodReport.crossFadeLodGroupCount,
                overdrawCaptures = overdraw
            };
            report.drawCallsPassed = report.maxEstimatedDrawCalls <= DrawCallTarget;
            report.visibleTrianglesPassed = report.maxEstimatedVisibleTriangles <= VisibleTriangleTarget;
            report.materialsPassed = report.sceneMaterialCount <= MaterialTarget;
            report.textureMemoryPassed = report.estimatedTextureBytes <= TextureMemoryTargetBytes;
            report.transparentSlotsPassed =
                report.transparentMaterialSlots <= TransparentMaterialSlotTarget;
            report.shippingArchitecturePassed = report.androidArm64Only && report.androidVulkanOnly &&
                                                report.androidAstcBuildTarget && report.androidIl2Cpp &&
                                                report.openXrSinglePassInstanced && report.msaa == 4 &&
                                                report.multithreadedRendering && report.streamingMipmaps;
            report.passed = report.drawCallsPassed && report.visibleTrianglesPassed && report.materialsPassed &&
                            report.textureMemoryPassed && report.transparentSlotsPassed &&
                            report.shippingArchitecturePassed;
            report.classification = report.passed
                ? "PASS_STATIC_QUEST_BUDGET_HEADSET_MEASUREMENT_REQUIRED"
                : "FAIL_STATIC_QUEST_BUDGET";
            if (!report.transparentSlotsPassed)
                report.warnings.Add("Transparent material-slot count exceeds the hard target of " +
                                    TransparentMaterialSlotTarget + "; inspect overdraw captures.");
            if (textureReport.unverifiableImporterCount > 0)
                report.warnings.Add(textureReport.unverifiableImporterCount + " texture sub-assets do not expose a TextureImporter; their platform format is estimated conservatively.");
            if (lodReport.crossFadeLodGroupCount > 0)
                report.warnings.Add(lodReport.crossFadeLodGroupCount + " LOD groups use crossfade; confirm they are bounded airport/vegetation groups and never the immutable far terrain.");
            if (visualReport == null)
                report.warnings.Add("No standard Visual QA report was supplied; maximum view budgets use the default authored camera only.");

            WriteJson(Path.Combine(outputDirectory, "performance_budget_report.json"), report);
            WriteJson(Path.Combine(outputDirectory, "material_shader_report.json"), materialReport);
            WriteJson(Path.Combine(outputDirectory, "texture_memory_report.json"), textureReport);
            WriteJson(Path.Combine(outputDirectory, "lod_culling_report.json"), lodReport);
            File.WriteAllText(Path.Combine(outputDirectory, "performance_budget_report.md"), BuildPerformanceMarkdown(report));
            File.WriteAllText(Path.Combine(outputDirectory, "material_shader_report.md"), BuildMaterialMarkdown(materialReport));
            File.WriteAllText(Path.Combine(outputDirectory, "texture_memory_report.md"), BuildTextureMarkdown(textureReport));
            File.WriteAllText(Path.Combine(outputDirectory, "lod_culling_report.md"), BuildLodMarkdown(lodReport));
            Debug.Log("[QuestFlight][PerformanceBudget] " + report.classification + "; artifacts=" + outputDirectory);

            if (!report.passed)
                throw new InvalidOperationException("Static Quest performance budget failed. See " + outputDirectory);
            if (exitWhenComplete) EditorApplication.Exit(0);
        }

        private static ProductionVisualQaBatchRunner.ProductionVisualQaReport ReadVisualQaReport()
        {
            string path = Environment.GetEnvironmentVariable(VisualReportEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            return JsonUtility.FromJson<ProductionVisualQaBatchRunner.ProductionVisualQaReport>(File.ReadAllText(path));
        }

        private static MaterialShaderReport BuildMaterialShaderReport(
            IEnumerable<Renderer> renderers,
            IEnumerable<AerialVegetationBatchRenderer> vegetationBatches,
            IEnumerable<AirportCloseTreeLodRenderer> closeTreeLods)
        {
            Dictionary<Material, MaterialEntry> entries = new Dictionary<Material, MaterialEntry>();
            int slots = 0;
            int transparentSlots = 0;
            foreach (Renderer renderer in renderers)
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    slots++;
                    bool transparent = material.renderQueue >= (int)RenderQueue.Transparent ||
                                       string.Equals(material.GetTag("RenderType", false, string.Empty), "Transparent", StringComparison.OrdinalIgnoreCase);
                    if (transparent) transparentSlots++;
                    if (!entries.TryGetValue(material, out MaterialEntry entry))
                    {
                        entry = new MaterialEntry
                        {
                            name = material.name,
                            assetPath = AssetDatabase.GetAssetPath(material),
                            shader = material.shader != null ? material.shader.name : "MISSING",
                            renderQueue = material.renderQueue,
                            transparent = transparent,
                            instancingEnabled = material.enableInstancing,
                            keywords = material.shaderKeywords.OrderBy(keyword => keyword, StringComparer.Ordinal).ToArray(),
                            rendererSlotCount = 0
                        };
                        entries.Add(material, entry);
                    }
                    entry.rendererSlotCount++;
                }
            }
            foreach (AerialVegetationBatchRenderer batch in vegetationBatches)
            {
                Material material = batch.SharedMaterial;
                int drawSlots = batch.NonEmptyDrawCount;
                if (material == null || drawSlots == 0) continue;
                slots += drawSlots;
                bool transparent = material.renderQueue >= (int)RenderQueue.Transparent ||
                                   string.Equals(material.GetTag("RenderType", false, string.Empty),
                                       "Transparent", StringComparison.OrdinalIgnoreCase);
                if (transparent) transparentSlots += drawSlots;
                if (!entries.TryGetValue(material, out MaterialEntry entry))
                {
                    entry = new MaterialEntry
                    {
                        name = material.name,
                        assetPath = AssetDatabase.GetAssetPath(material),
                        shader = material.shader != null ? material.shader.name : "MISSING",
                        renderQueue = material.renderQueue,
                        transparent = transparent,
                        instancingEnabled = material.enableInstancing,
                        keywords = material.shaderKeywords.OrderBy(keyword => keyword, StringComparer.Ordinal).ToArray(),
                        rendererSlotCount = 0,
                        environmentReference = true
                    };
                    entries.Add(material, entry);
                }
                entry.rendererSlotCount += drawSlots;
                entry.environmentReference = true;
            }
            foreach (AirportCloseTreeLodRenderer closeTree in closeTreeLods)
            {
                Material material = closeTree.SharedCanopyMaterial;
                int drawSlots = closeTree.AuthoredMaximumDrawCount;
                if (material == null || drawSlots == 0) continue;
                slots += drawSlots;
                bool transparent = material.renderQueue >= (int)RenderQueue.Transparent ||
                    string.Equals(material.GetTag("RenderType", false, string.Empty),
                        "Transparent", StringComparison.OrdinalIgnoreCase);
                if (transparent) transparentSlots += drawSlots;
                if (!entries.TryGetValue(material, out MaterialEntry entry))
                {
                    entry = new MaterialEntry
                    {
                        name = material.name,
                        assetPath = AssetDatabase.GetAssetPath(material),
                        shader = material.shader != null ? material.shader.name : "MISSING",
                        renderQueue = material.renderQueue,
                        transparent = transparent,
                        instancingEnabled = material.enableInstancing,
                        keywords = material.shaderKeywords
                            .OrderBy(keyword => keyword, StringComparer.Ordinal).ToArray(),
                        rendererSlotCount = 0,
                        environmentReference = true
                    };
                    entries.Add(material, entry);
                }
                entry.rendererSlotCount += drawSlots;
                entry.environmentReference = true;
            }
            Material skybox = RenderSettings.skybox;
            if (skybox != null)
            {
                slots++;
                bool transparent = skybox.renderQueue >= (int)RenderQueue.Transparent ||
                                   string.Equals(
                                       skybox.GetTag("RenderType", false, string.Empty),
                                       "Transparent",
                                       StringComparison.OrdinalIgnoreCase);
                if (transparent) transparentSlots++;
                if (!entries.TryGetValue(skybox, out MaterialEntry skyEntry))
                {
                    skyEntry = new MaterialEntry
                    {
                        name = skybox.name,
                        assetPath = AssetDatabase.GetAssetPath(skybox),
                        shader = skybox.shader != null ? skybox.shader.name : "MISSING",
                        renderQueue = skybox.renderQueue,
                        transparent = transparent,
                        instancingEnabled = skybox.enableInstancing,
                        keywords = skybox.shaderKeywords.OrderBy(keyword => keyword, StringComparer.Ordinal).ToArray(),
                        rendererSlotCount = 0,
                        environmentReference = true
                    };
                    entries.Add(skybox, skyEntry);
                }
                skyEntry.rendererSlotCount++;
                skyEntry.environmentReference = true;
            }
            List<MaterialEntry> materials = entries.Values.OrderBy(entry => entry.shader).ThenBy(entry => entry.name).ToList();
            int variants = materials.Select(entry => entry.shader + "|" + string.Join(";", entry.keywords)).Distinct().Count();
            return new MaterialShaderReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                materialCount = materials.Count,
                rendererMaterialSlots = slots,
                transparentMaterialSlots = transparentSlots,
                instancingEnabledMaterialCount = materials.Count(entry => entry.instancingEnabled),
                shaderCount = materials.Select(entry => entry.shader).Distinct().Count(),
                shaderKeywordCombinationCount = variants,
                variantSemantics = "Distinct shader-plus-material-keyword combinations referenced by active authored renderers, authored aerial vegetation batches, and the scene skybox; this is not Unity's compiled variant count.",
                materials = materials
            };
        }

        private static TextureMemoryReport BuildTextureReport(
            IEnumerable<MaterialEntry> materialEntries,
            IEnumerable<GameObject> roots)
        {
            Dictionary<string, TextureEntry> textures = new Dictionary<string, TextureEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (MaterialEntry materialEntry in materialEntries)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialEntry.assetPath);
                if (material == null)
                {
                    UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(materialEntry.assetPath);
                    material = assets.OfType<Material>().FirstOrDefault(candidate => candidate.name == materialEntry.name);
                }
                if (material == null || material.shader == null) continue;
                foreach (string property in material.GetTexturePropertyNames())
                {
                    Texture texture = material.GetTexture(property);
                    AddTextureEstimate(textures, texture);
                }
            }
            AddTextureEstimate(textures, RenderSettings.customReflectionTexture);
            foreach (PhotorealAerialSurfaceRoot surface in roots.SelectMany(root =>
                         root.GetComponentsInChildren<PhotorealAerialSurfaceRoot>(true)))
            {
                AddTextureEstimate(textures, surface.nearOrthophoto);
                AddTextureEstimate(textures, surface.midOrthophoto);
                AddTextureEstimate(textures, surface.farOrthophoto);
                AddTextureEstimate(textures, surface.normalizedSurfaceHeight);
                AddTextureEstimate(textures, surface.surfaceClassification);
                AddTextureEstimate(textures, surface.buildingOwnershipMask);
                AddTextureEstimate(textures, surface.canopyOwnershipMask);
            }
            foreach (GeospatialMipController controller in roots.SelectMany(root =>
                         root.GetComponentsInChildren<GeospatialMipController>(true)))
            {
                AddTextureEstimate(textures, controller.near4Km);
                AddTextureEstimate(textures, controller.midNorthWest);
                AddTextureEstimate(textures, controller.midNorthEast);
                AddTextureEstimate(textures, controller.midSouthWest);
                AddTextureEstimate(textures, controller.midSouthEast);
                AddTextureEstimate(textures, controller.midBase12Km);
                AddTextureEstimate(textures, controller.far24Km);
            }
            foreach (ReflectionProbe probe in roots.SelectMany(root =>
                         root.GetComponentsInChildren<ReflectionProbe>(true)))
                if (probe.mode == ReflectionProbeMode.Custom)
                    AddTextureEstimate(textures, probe.customBakedTexture);
            foreach (LightmapData lightmap in LightmapSettings.lightmaps ?? Array.Empty<LightmapData>())
            {
                if (lightmap == null) continue;
                AddTextureEstimate(textures, lightmap.lightmapColor);
                AddTextureEstimate(textures, lightmap.lightmapDir);
                AddTextureEstimate(textures, lightmap.shadowMask);
            }
            List<TextureEntry> entries = textures.Values.OrderByDescending(entry => entry.estimatedRuntimeBytes).ToList();
            TextureEntry[] lightmaps = entries.Where(entry =>
                    entry.assetPath.IndexOf("/Lightmap-", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
            return new TextureMemoryReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                textureCount = entries.Count,
                estimatedRuntimeBytes = entries.Sum(entry => entry.estimatedRuntimeBytes),
                astcOverrideCount = entries.Count(entry => IsAstcFormat(entry.androidFormat)),
                mipmappedCount = entries.Count(entry => entry.mipmaps),
                streamingMipmappedCount = entries.Count(entry => entry.streamingMipmaps),
                unverifiableImporterCount = entries.Count(entry => entry.androidFormat == "UNVERIFIABLE_SUBASSET"),
                lightmapTextureCount = lightmaps.Length,
                estimatedLightmapBytes = lightmaps.Sum(entry => entry.estimatedRuntimeBytes),
                evidenceBoundary = "Static import estimate only; APK/device residency and streaming behavior require a Quest capture.",
                textures = entries
            };
        }

        private static void AddTextureEstimate(Dictionary<string, TextureEntry> textures, Texture texture)
        {
            if (texture == null) return;
            string assetPath = AssetDatabase.GetAssetPath(texture);
            string key = string.IsNullOrWhiteSpace(assetPath)
                ? "instance:" + texture.GetInstanceID()
                : assetPath + "#" + texture.name;
            if (textures.ContainsKey(key)) return;

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            TextureImporterPlatformSettings android = importer != null
                ? importer.GetPlatformTextureSettings("Android")
                : null;
            string format = android != null && android.overridden
                ? android.format.ToString()
                : importer != null ? importer.textureCompression.ToString() : "UNVERIFIABLE_SUBASSET";
            int width = Math.Max(1, texture.width);
            int height = Math.Max(1, texture.height);
            int maximumSize = android != null && android.maxTextureSize > 0
                ? android.maxTextureSize
                : Math.Max(width, height);
            int largestDimension = Math.Max(width, height);
            if (maximumSize > 0 && largestDimension > maximumSize)
            {
                double scale = maximumSize / (double)largestDimension;
                width = Math.Max(1, (int)Math.Ceiling(width * scale));
                height = Math.Max(1, (int)Math.Ceiling(height * scale));
            }

            int layersOrFaces = 1;
            string dimension = texture.dimension.ToString();
            if (texture is Cubemap) layersOrFaces = 6;
            else if (texture is CubemapArray cubemapArray) layersOrFaces = cubemapArray.cubemapCount * 6;
            else if (texture is Texture2DArray textureArray) layersOrFaces = textureArray.depth;
            else if (texture is Texture3D texture3D) layersOrFaces = texture3D.depth;

            bool mipmaps = importer == null || importer.mipmapEnabled;
            long estimatedBytes = EstimateTextureBytes(width, height, format, mipmaps) * layersOrFaces;
            textures.Add(key, new TextureEntry
            {
                name = texture.name,
                assetPath = assetPath,
                dimension = dimension,
                layersOrFaces = layersOrFaces,
                width = width,
                height = height,
                androidOverride = android != null && android.overridden,
                androidFormat = format,
                maxTextureSize = maximumSize,
                mipmaps = mipmaps,
                streamingMipmaps = importer != null && importer.streamingMipmaps,
                anisotropy = importer != null ? importer.anisoLevel : 0,
                filterMode = texture.filterMode.ToString(),
                estimatedRuntimeBytes = estimatedBytes,
                estimateSemantics = IsAstcFormat(format)
                    ? "ASTC block estimate multiplied by faces/layers and including mip chains when enabled"
                    : "Conservative RGBA32 estimate multiplied by faces/layers and including mip chains when enabled"
            });
        }

        private static bool IsAstcFormat(string format) =>
            !string.IsNullOrWhiteSpace(format) &&
            (format.StartsWith("ASTC_", StringComparison.Ordinal) ||
             format.StartsWith("ASTC_HDR_", StringComparison.Ordinal));

        private static LodCullingReport BuildLodReport(
            IEnumerable<GameObject> roots,
            Renderer[] renderers,
            AerialVegetationBatchRenderer[] vegetationBatches,
            AirportCloseTreeLodRenderer[] closeTreeLods)
        {
            LODGroup[] groups = roots.SelectMany(root => root.GetComponentsInChildren<LODGroup>(true)).ToArray();
            List<LodGroupEntry> entries = groups.Select(group => new LodGroupEntry
            {
                hierarchyPath = HierarchyPath(group.transform),
                lodCount = group.lodCount,
                fadeMode = group.fadeMode.ToString(),
                animateCrossFading = group.animateCrossFading,
                size = group.size,
                transitionHeights = group.GetLODs().Select(lod => lod.screenRelativeTransitionHeight).ToArray(),
                rendererCounts = group.GetLODs().Select(lod => lod.renderers.Count(renderer => renderer != null)).ToArray()
            }).OrderBy(entry => entry.hierarchyPath).ToList();
            List<string> customCullers = roots.SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(component => component != null && component.GetType().Name.IndexOf("Cull", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(component => HierarchyPath(component.transform) + " (" + component.GetType().FullName + ")")
                .OrderBy(value => value)
                .ToList();
            return new LodCullingReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                activeRendererCount = renderers.Length +
                    vegetationBatches.Sum(batch => batch.NonEmptyDrawCount) +
                    closeTreeLods.Sum(batch => batch.AuthoredMaximumDrawCount),
                rendererBoundsRadiusMaximum = renderers.Length == 0 ? 0f : renderers.Max(renderer => renderer.bounds.extents.magnitude),
                lodGroupCount = entries.Count,
                crossFadeLodGroupCount = entries.Count(entry => entry.fadeMode != LODFadeMode.None.ToString()),
                customCullerCount = customCullers.Count,
                immutableFarTerrainPolicy = "Far terrain must not use LODGroup, crossfade, dithering, billboard behavior, or camera-relative scaling.",
                lodGroups = entries,
                customCullers = customCullers
            };
        }

        private static StaticViewBudget CaptureViewBudget(Camera camera, Renderer[] renderers)
        {
            RenderBudgetSnapshot snapshot = QuestRenderBudgetAudit.Capture(camera);
            return new StaticViewBudget
            {
                cameraPath = HierarchyPath(camera.transform),
                visibleRendererCount = snapshot.estimatedVisibleRendererCount,
                estimatedDrawCalls = snapshot.estimatedInstancedDrawCalls,
                visibleTriangles = snapshot.estimatedFrustumTriangles,
                visibleMaterials = snapshot.uniqueMaterialCount
            };
        }

        private static long CountTriangles(IEnumerable<Renderer> renderers)
        {
            long triangles = 0;
            HashSet<Mesh> countedSkinnedMeshes = new HashSet<Mesh>();
            foreach (Renderer renderer in renderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null && renderer is SkinnedMeshRenderer skinned)
                {
                    mesh = skinned.sharedMesh;
                    if (mesh != null && !countedSkinnedMeshes.Add(mesh)) continue;
                }
                if (mesh == null) continue;
                for (int index = 0; index < mesh.subMeshCount; index++)
                    if (mesh.GetTopology(index) == MeshTopology.Triangles)
                        triangles += (long)mesh.GetIndexCount(index) / 3L;
            }
            return triangles;
        }

        private static List<OverdrawCapture> CaptureOverdraw(Camera source, string directory)
        {
            Shader shader = Shader.Find("Hidden/QuestFlight/OverdrawHeatmap");
            if (shader == null) throw new InvalidOperationException("Overdraw replacement shader is missing.");
            List<OverdrawCapture> captures = new List<OverdrawCapture>();
            GameObject cameraObject = new GameObject("QuestPerformanceAuditCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(source);
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            try
            {
                foreach (OverdrawPose pose in new[]
                         {
                             new OverdrawPose("cockpit_forward", 0f),
                             new OverdrawPose("cockpit_left", -90f),
                             new OverdrawPose("cockpit_rear", 180f)
                         })
                {
                    camera.transform.rotation = Quaternion.Euler(0f, pose.yawDegrees, 0f) * source.transform.rotation;
                    captures.Add(RenderOverdraw(camera, shader, directory, pose.id));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            return captures;
        }

        private static OverdrawCapture RenderOverdraw(Camera camera, Shader shader, string directory, string id)
        {
            const int width = 960;
            const int height = 540;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                camera.targetTexture = target;
                camera.SetReplacementShader(shader, string.Empty);
                QuestFlight.Rendering.ProductRenderCapture.Render(camera);
                camera.ResetReplacementShader();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                string path = Path.Combine(directory, id + ".png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Color32[] pixels = texture.GetPixels32();
                float mean = pixels.Length == 0 ? 0f : pixels.Average(pixel => (pixel.r + pixel.g + pixel.b) / (3f * 255f));
                float peak = pixels.Length == 0 ? 0f : pixels.Max(pixel => Math.Max(pixel.r, Math.Max(pixel.g, pixel.b))) / 255f;
                return new OverdrawCapture { id = id, path = path, width = width, height = height, meanNormalizedIntensity = mean, peakNormalizedIntensity = peak };
            }
            finally
            {
                camera.ResetReplacementShader();
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static long EstimateTextureBytes(int width, int height, string format, bool mipmaps)
        {
            Dictionary<string, int> astcBlocks = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "ASTC_4x4", 4 }, { "ASTC_5x5", 5 }, { "ASTC_6x6", 6 }, { "ASTC_8x8", 8 },
                { "ASTC_10x10", 10 }, { "ASTC_12x12", 12 }
            };
            string normalizedFormat = !string.IsNullOrEmpty(format) &&
                                      format.StartsWith("ASTC_HDR_", StringComparison.Ordinal)
                ? "ASTC_" + format.Substring("ASTC_HDR_".Length)
                : format ?? string.Empty;
            long baseBytes;
            if (astcBlocks.TryGetValue(normalizedFormat, out int block))
                baseBytes = (long)Math.Ceiling(width / (double)block) * (long)Math.Ceiling(height / (double)block) * 16L;
            else if (string.Equals(normalizedFormat, "R8", StringComparison.Ordinal) ||
                     string.Equals(normalizedFormat, "Alpha8", StringComparison.Ordinal))
                baseBytes = (long)width * height;
            else if (string.Equals(normalizedFormat, "R16", StringComparison.Ordinal) ||
                     string.Equals(normalizedFormat, "RGB565", StringComparison.Ordinal) ||
                     string.Equals(normalizedFormat, "RGBA4444", StringComparison.Ordinal) ||
                     string.Equals(normalizedFormat, "ARGB4444", StringComparison.Ordinal))
                baseBytes = (long)width * height * 2L;
            else if (string.Equals(normalizedFormat, "RGB24", StringComparison.Ordinal))
                baseBytes = (long)width * height * 3L;
            else
                baseBytes = (long)width * height * 4L;
            return mipmaps ? (long)Math.Ceiling(baseBytes * 4d / 3d) : baseBytes;
        }

        private static void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonUtility.ToJson(value, true));

        private static string BuildPerformanceMarkdown(PerformanceBudgetReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Standalone Quest Static Performance Budget");
            builder.AppendLine();
            builder.AppendLine("- Classification: `" + report.classification + "`");
            builder.AppendLine("- Configured render pipeline: `" + report.configuredRenderPipeline + "`");
            builder.AppendLine("- Draw calls: " + report.maxEstimatedDrawCalls + " / " + report.drawCallTarget + " (" + Pass(report.drawCallsPassed) + ")");
            builder.AppendLine("- Visible triangles: " + report.maxEstimatedVisibleTriangles.ToString("N0", CultureInfo.InvariantCulture) + " / " + report.visibleTriangleTarget.ToString("N0", CultureInfo.InvariantCulture) + " (" + Pass(report.visibleTrianglesPassed) + ")");
            builder.AppendLine("- Materials (maximum visible / active distinct): " + report.maxVisibleMaterialCount + " / " + report.activeSceneMaterialCount + "; visible target " + report.materialTarget + " (" + Pass(report.materialsPassed) + ")");
            builder.AppendLine("- Estimated runtime texture memory: " + FormatBytes(report.estimatedTextureBytes) + " / " + FormatBytes(report.textureMemoryTargetBytes) + " (" + Pass(report.textureMemoryPassed) + ")");
            builder.AppendLine("- Directional lightmap textures / estimated residency: " + report.lightmapTextureCount + " / " + FormatBytes(report.estimatedLightmapBytes));
            builder.AppendLine("- Transparent material slots: " + report.transparentMaterialSlots + " / " + report.transparentMaterialSlotTarget + " (" + Pass(report.transparentSlotsPassed) + ")");
            builder.AppendLine("- Android ARM64 / Vulkan / ASTC / IL2CPP: " + report.androidArm64Only + " / " + report.androidVulkanOnly + " / " + report.androidAstcBuildTarget + " / " + report.androidIl2Cpp);
            builder.AppendLine("- OpenXR single-pass instanced / 4x MSAA / mobile multithreaded / mip streaming: " + report.openXrSinglePassInstanced + " / " + report.msaa + " / " + report.multithreadedRendering + " / " + report.streamingMipmaps);
            builder.AppendLine("- Active renderers / shaders / material keyword combinations: " + report.activeRendererCount + " / " + report.shaderCount + " / " + report.shaderKeywordCombinationCount);
            builder.AppendLine("- Textures / estimated imported residency: " + report.textureCount + " / " + FormatBytes(report.estimatedTextureBytes));
            builder.AppendLine("- LOD groups / crossfade groups: " + report.lodGroupCount + " / " + report.crossFadeLodGroupCount);
            builder.AppendLine();
            builder.AppendLine("## Evidence boundary");
            builder.AppendLine();
            builder.AppendLine("- " + report.headsetPerformanceLimitation);
            builder.AppendLine("- Overdraw images are qualitative Editor replacement-shader captures, not Quest GPU measurements.");
            foreach (string warning in report.warnings) builder.AppendLine("- Warning: " + warning);
            return builder.ToString();
        }

        private static string BuildMaterialMarkdown(MaterialShaderReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Material and Shader Report");
            builder.AppendLine();
            builder.AppendLine("- Materials: " + report.materialCount + "; slots: " + report.rendererMaterialSlots + "; transparent slots: " + report.transparentMaterialSlots);
            builder.AppendLine("- Shaders: " + report.shaderCount + "; distinct keyword combinations: " + report.shaderKeywordCombinationCount);
            builder.AppendLine("- " + report.variantSemantics);
            builder.AppendLine();
            builder.AppendLine("| Material | Shader | Queue | Instancing | Slots | Keywords |");
            builder.AppendLine("| --- | --- | ---: | --- | ---: | --- |");
            foreach (MaterialEntry entry in report.materials)
                builder.AppendLine("| `" + entry.name + "` | `" + entry.shader + "` | " + entry.renderQueue + " | " + entry.instancingEnabled + " | " + entry.rendererSlotCount + " | `" + string.Join(" ", entry.keywords) + "` |");
            return builder.ToString();
        }

        private static string BuildTextureMarkdown(TextureMemoryReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Texture Memory Report");
            builder.AppendLine();
            builder.AppendLine("- Textures: " + report.textureCount + "; ASTC overrides: " + report.astcOverrideCount + "; mipmapped: " + report.mipmappedCount + "; streaming: " + report.streamingMipmappedCount);
            builder.AppendLine("- Conservative estimated imported residency: " + FormatBytes(report.estimatedRuntimeBytes));
            builder.AppendLine("- Directional lightmap textures / estimated residency: " + report.lightmapTextureCount + " / " + FormatBytes(report.estimatedLightmapBytes));
            builder.AppendLine("- " + report.evidenceBoundary);
            builder.AppendLine();
            builder.AppendLine("| Texture | Dimensions | Faces/layers | Android format | Mips/streaming | Estimated bytes |");
            builder.AppendLine("| --- | ---: | ---: | --- | --- | ---: |");
            foreach (TextureEntry entry in report.textures)
                builder.AppendLine("| `" + entry.name + "` | " + entry.width + "x" + entry.height + " " + entry.dimension + " | " + entry.layersOrFaces + " | `" + entry.androidFormat + "` | " + entry.mipmaps + "/" + entry.streamingMipmaps + " | " + FormatBytes(entry.estimatedRuntimeBytes) + " |");
            return builder.ToString();
        }

        private static string BuildLodMarkdown(LodCullingReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# LOD and Culling Report");
            builder.AppendLine();
            builder.AppendLine("- Active renderers: " + report.activeRendererCount);
            builder.AppendLine("- LOD groups / crossfade groups / custom cullers: " + report.lodGroupCount + " / " + report.crossFadeLodGroupCount + " / " + report.customCullerCount);
            builder.AppendLine("- " + report.immutableFarTerrainPolicy);
            foreach (LodGroupEntry entry in report.lodGroups)
                builder.AppendLine("- `" + entry.hierarchyPath + "`: " + entry.lodCount + " LODs, " + entry.fadeMode + ", transitions " + string.Join(", ", entry.transitionHeights.Select(value => value.ToString("0.000", CultureInfo.InvariantCulture))));
            return builder.ToString();
        }

        private static string Pass(bool value) => value ? "PASS" : "FAIL";
        private static string FormatBytes(long value) => (value / (1024d * 1024d)).ToString("0.00", CultureInfo.InvariantCulture) + " MiB";

        private static string HierarchyPath(Transform transform)
        {
            Stack<string> names = new Stack<string>();
            for (Transform current = transform; current != null; current = current.parent) names.Push(current.name);
            return string.Join("/", names.ToArray());
        }

        private readonly struct OverdrawPose
        {
            public readonly string id;
            public readonly float yawDegrees;
            public OverdrawPose(string id, float yawDegrees) { this.id = id; this.yawDegrees = yawDegrees; }
        }

        [Serializable]
        public sealed class PerformanceBudgetReport
        {
            public int schemaVersion = 1;
            public string generatedUtc, unityVersion, scenePath, evidenceKind, headsetPerformanceLimitation, classification, visualQaReportPath, configuredRenderPipeline;
            public bool headsetPerformanceEstablished, drawCallsPassed, visibleTrianglesPassed, materialsPassed, textureMemoryPassed, transparentSlotsPassed, shippingArchitecturePassed, passed;
            public int drawCallTarget, materialTarget, transparentMaterialSlotTarget, maxEstimatedDrawCalls, sceneMaterialCount, maxVisibleMaterialCount, activeSceneMaterialCount, activeRendererCount, msaa;
            public long visibleTriangleTarget, textureMemoryTargetBytes, maxEstimatedVisibleTriangles, sceneMeshTriangles, estimatedTextureBytes, estimatedLightmapBytes;
            public bool androidArm64Only, androidVulkanOnly, androidAstcBuildTarget, androidIl2Cpp, openXrSinglePassInstanced, multithreadedRendering, streamingMipmaps;
            public int transparentMaterialSlots, shaderCount, shaderKeywordCombinationCount, textureCount, lightmapTextureCount, lodGroupCount, crossFadeLodGroupCount;
            public StaticViewBudget defaultView;
            public List<OverdrawCapture> overdrawCaptures = new List<OverdrawCapture>();
            public List<string> warnings = new List<string>();
        }

        [Serializable]
        public sealed class StaticViewBudget
        {
            public string cameraPath;
            public int visibleRendererCount, estimatedDrawCalls, visibleMaterials;
            public long visibleTriangles;
        }

        [Serializable]
        public sealed class OverdrawCapture
        {
            public string id, path;
            public int width, height;
            public float meanNormalizedIntensity, peakNormalizedIntensity;
        }

        [Serializable]
        public sealed class MaterialShaderReport
        {
            public int schemaVersion = 1;
            public string generatedUtc, variantSemantics;
            public int materialCount, rendererMaterialSlots, transparentMaterialSlots, instancingEnabledMaterialCount, shaderCount, shaderKeywordCombinationCount;
            public List<MaterialEntry> materials = new List<MaterialEntry>();
        }

        [Serializable]
        public sealed class MaterialEntry
        {
            public string name, assetPath, shader;
            public int renderQueue, rendererSlotCount;
            public bool transparent, instancingEnabled, environmentReference;
            public string[] keywords;
        }

        [Serializable]
        public sealed class TextureMemoryReport
        {
            public int schemaVersion = 1;
            public string generatedUtc, evidenceBoundary;
            public int textureCount, astcOverrideCount, mipmappedCount, streamingMipmappedCount, unverifiableImporterCount, lightmapTextureCount;
            public long estimatedRuntimeBytes, estimatedLightmapBytes;
            public List<TextureEntry> textures = new List<TextureEntry>();
        }

        [Serializable]
        public sealed class TextureEntry
        {
            public string name, assetPath, androidFormat, filterMode, dimension, estimateSemantics;
            public int width, height, layersOrFaces, maxTextureSize, anisotropy;
            public bool androidOverride, mipmaps, streamingMipmaps;
            public long estimatedRuntimeBytes;
        }

        [Serializable]
        public sealed class LodCullingReport
        {
            public int schemaVersion = 1;
            public string generatedUtc, immutableFarTerrainPolicy;
            public int activeRendererCount, lodGroupCount, crossFadeLodGroupCount, customCullerCount;
            public float rendererBoundsRadiusMaximum;
            public List<LodGroupEntry> lodGroups = new List<LodGroupEntry>();
            public List<string> customCullers = new List<string>();
        }

        [Serializable]
        public sealed class LodGroupEntry
        {
            public string hierarchyPath, fadeMode;
            public int lodCount;
            public bool animateCrossFading;
            public float size;
            public float[] transitionHeights;
            public int[] rendererCounts;
        }
    }
}
