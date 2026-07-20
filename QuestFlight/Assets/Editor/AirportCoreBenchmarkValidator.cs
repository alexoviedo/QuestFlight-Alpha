using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Fail-closed structural and static-cost validator for the serialized Stage 1 benchmark.
    /// Image quality, stereo stability, and physical Quest performance are separate gates.
    /// </summary>
    public static class AirportCoreBenchmarkValidator
    {
        public const string EnvironmentPrefabPath = ProductionEnvironmentRoot.PrefabAssetPath;

        [Serializable]
        public sealed class Report
        {
            public string classification;
            public bool passed;
            public string sourceV2Sha;
            public string referenceV3Sha;
            public int benchmarkRendererCount;
            public int benchmarkTriangleCount;
            public int benchmarkMaterialCount;
            public int terminalLod0Triangles;
            public int terminalLod1Triangles;
            public int terminalLod2Triangles;
            public int hangarLod0Triangles;
            public int hangarLod1Triangles;
            public int hangarLod2Triangles;
            public int alphaTestMaterialSlots;
            public int transparentMaterialSlots;
            public int adaptiveAirportTerrainTriangles;
            public bool oneAtlasLightmapParametersValid;
            public int validatedTextureCount;
            public int replacementSourceFeatureCount;
            public string[] violations = Array.Empty<string>();
            public string evidenceBoundary =
                "Structural/static authoring evidence only; this report does not establish image quality, stereo stability, comfort, or Quest hardware performance.";
        }

        public static Report Validate()
        {
            List<string> violations = new List<string>();
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPrefabPath);
            if (environment == null)
                return Failed("Production environment prefab is missing.");

            AirportCoreBenchmarkRoot[] roots = environment.GetComponentsInChildren<AirportCoreBenchmarkRoot>(true);
            if (roots.Length != 1)
                return Failed($"Expected exactly one AirportCoreBenchmarkRoot, found {roots.Length}.");

            AirportCoreBenchmarkRoot root = roots[0];
            if (!root.TryValidateContract(out string contractReport))
                AddViolation(violations, contractReport);

            AirportCoreBenchmarkManifest manifest = root.manifest;
            if (manifest == null)
                return Failed("AirportCoreBenchmarkManifest is not bound.");

            ValidateReservoir(environment.GetComponent<ProductionEnvironmentRoot>(), root, manifest, violations);
            ValidateCleanPlateTerrainBinding(root, manifest, violations);
            ValidateContextMeshReferences(root, violations);
            ValidateGiAllowlist(environment, root, violations);
            int adaptiveAirportTerrainTriangles = ValidateAdaptiveAirportTerrainBinding(
                environment.GetComponent<ProductionEnvironmentRoot>(), violations);
            bool oneAtlasLightmapParametersValid = ValidateOneAtlasLightmapContract(violations);

            ValidateLodGroup(root.executiveTerminal, "executive terminal", 20000, 45000,
                violations, out int terminal0, out int terminal1, out int terminal2);
            ValidateLodGroup(root.type2HangarRow, "Type 2 hangar row", 25000, 60000,
                violations, out int hangar0, out int hangar1, out int hangar2);

            Renderer[] renderers = root.benchmarkContent == null
                ? Array.Empty<Renderer>()
                : root.benchmarkContent.GetComponentsInChildren<Renderer>(true);
            HashSet<Material> materials = new HashSet<Material>(renderers
                .SelectMany(item => item.sharedMaterials ?? Array.Empty<Material>())
                .Where(item => item != null));
            int triangles = renderers.Sum(RendererTriangles);
            int alphaTestSlots = renderers.Sum(renderer => (renderer.sharedMaterials ?? Array.Empty<Material>())
                .Count(material => material != null && material.renderQueue >= (int)RenderQueue.AlphaTest &&
                                   material.renderQueue < (int)RenderQueue.Transparent));
            int transparentSlots = renderers.Sum(renderer => (renderer.sharedMaterials ?? Array.Empty<Material>())
                .Count(material => material != null && material.renderQueue >= (int)RenderQueue.Transparent));

            Require(renderers.Length == manifest.serializedRendererCount,
                $"renderer inventory changed: manifest={manifest.serializedRendererCount}, actual={renderers.Length}", violations);
            Require(triangles == manifest.serializedTriangleCount,
                $"triangle inventory changed: manifest={manifest.serializedTriangleCount}, actual={triangles}", violations);
            Require(materials.Count == manifest.serializedMaterialCount,
                $"material inventory changed: manifest={manifest.serializedMaterialCount}, actual={materials.Count}", violations);
            Require(renderers.All(UsesProjectMesh),
                "benchmark contains a built-in/unsaved mesh instead of a project-authored asset", violations);
            Require(renderers.All(renderer => renderer.shadowCastingMode != ShadowCastingMode.TwoSided),
                "benchmark contains two-sided shadow casting", violations);
            Require(transparentSlots == 0,
                $"benchmark must use opaque/alpha-test materials; found {transparentSlots} transparent slot(s)", violations);
            Require(materials.Count <= 8,
                $"benchmark material set is not consolidated ({materials.Count} distinct materials)", violations);

            Texture[] textures = ManifestTextures(manifest).Where(item => item != null).Distinct().ToArray();
            foreach (Texture texture in textures) ValidateTexture(texture, violations);
            Require(textures.Length >= 10, "optimized benchmark texture inventory is incomplete", violations);
            Require(manifest.replacementSourceFeatureIds != null &&
                    manifest.replacementSourceFeatureIds.Length == manifest.serializedReplacementCount,
                "source-feature replacement inventory is incomplete", violations);
            Require(root.gameObject.activeSelf && root.benchmarkContent != null && root.benchmarkContent.gameObject.activeSelf,
                "V4 benchmark content is not the active authored variant", violations);
            Require(environment.GetComponentsInChildren<Transform>(true).All(item =>
                    !string.Equals(item.name, AirportCoreBenchmarkRoot.V2BaselineName, StringComparison.Ordinal) &&
                    !string.Equals(item.name, "FilteredV2Context", StringComparison.Ordinal)),
                "duplicate serialized A/B context hierarchy remains", violations);
            Require(environment.GetComponentsInChildren<Transform>(true).All(item =>
                    !string.Equals(item.name, "AirportArtProduction", StringComparison.Ordinal)),
                "superseded generic airport-art hierarchy remains active in the product prefab", violations);
            Require(root.airportLightProbes != null && root.airportLightProbes.probePositions.Length == 18 &&
                    root.airportLightProbes.probePositions.Select(item => item.y).Distinct().Count() == 2,
                "light-probe volume must contain the authored 18 probes at two heights", violations);

            return new Report
            {
                classification = violations.Count == 0
                    ? "PASS_AIRPORT_CORE_V4_2_STRUCTURAL_GATE_VISUAL_AND_HEADSET_REQUIRED"
                    : "FAIL_AIRPORT_CORE_V4_2_STRUCTURAL_GATE",
                passed = violations.Count == 0,
                sourceV2Sha = manifest.sourceV2Sha,
                referenceV3Sha = manifest.referenceV3Sha,
                benchmarkRendererCount = renderers.Length,
                benchmarkTriangleCount = triangles,
                benchmarkMaterialCount = materials.Count,
                terminalLod0Triangles = terminal0,
                terminalLod1Triangles = terminal1,
                terminalLod2Triangles = terminal2,
                hangarLod0Triangles = hangar0,
                hangarLod1Triangles = hangar1,
                hangarLod2Triangles = hangar2,
                alphaTestMaterialSlots = alphaTestSlots,
                transparentMaterialSlots = transparentSlots,
                adaptiveAirportTerrainTriangles = adaptiveAirportTerrainTriangles,
                oneAtlasLightmapParametersValid = oneAtlasLightmapParametersValid,
                validatedTextureCount = textures.Length,
                replacementSourceFeatureCount = manifest.replacementSourceFeatureIds?.Length ?? 0,
                violations = violations.ToArray()
            };
        }

        private static void ValidateContextMeshReferences(
            AirportCoreBenchmarkRoot root,
            ICollection<string> violations)
        {
            LOD[] lods = root.filteredV2Context == null
                ? Array.Empty<LOD>()
                : root.filteredV2Context.GetLODs();
            Require(lods.Length == 3,
                "filtered context must expose exactly three LODs", violations);
            Require(root.v2ContextOriginalMeshPaths != null && root.v2ContextOriginalMeshPaths.Length == 3,
                "V2 original context mesh provenance is incomplete", violations);
            Require(root.v4ContextFilteredMeshPaths != null && root.v4ContextFilteredMeshPaths.Length == 3,
                "V4 filtered context mesh provenance is incomplete", violations);
            if (lods.Length != 3 || root.v4ContextFilteredMeshPaths == null ||
                root.v4ContextFilteredMeshPaths.Length != 3) return;
            for (int index = 0; index < 3; index++)
            {
                Renderer renderer = lods[index].renderers?.SingleOrDefault();
                Mesh active = renderer == null ? null : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                string path = active == null ? string.Empty : AssetDatabase.GetAssetPath(active);
                Require(string.Equals(path, root.v4ContextFilteredMeshPaths[index], StringComparison.Ordinal),
                    $"filtered context LOD{index} does not use its pinned V4 mesh", violations);
                Require(AssetDatabase.LoadAssetAtPath<Mesh>(root.v2ContextOriginalMeshPaths[index]) != null,
                    $"V2 A/B mesh path {index} is missing", violations);
            }
        }

        private static void ValidateGiAllowlist(
            GameObject environment,
            AirportCoreBenchmarkRoot root,
            ICollection<string> violations)
        {
            Renderer[] expected =
            {
                FirstLodRenderer(root.executiveTerminal),
                FirstLodRenderer(root.type2HangarRow)
            };
            Renderer[] actual = environment.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) &
                                    StaticEditorFlags.ContributeGI) != 0)
                .ToArray();
            Require(expected.All(item => item != null),
                "terminal/hangar LOD0 GI allowlist is incomplete", violations);
            Require(actual.Length == 2 && actual.All(item => expected.Contains(item)),
                "ContributeGI allowlist must contain only terminal and hangar LOD0", violations);
        }

        private static Renderer FirstLodRenderer(LODGroup group)
        {
            if (group == null) return null;
            LOD[] lods = group.GetLODs();
            return lods.Length == 0 || lods[0].renderers == null || lods[0].renderers.Length != 1
                ? null
                : lods[0].renderers[0];
        }

        private static int ValidateAdaptiveAirportTerrainBinding(
            ProductionEnvironmentRoot contract,
            ICollection<string> violations)
        {
            Require(contract != null, "production environment contract is missing", violations);
            if (contract == null) return 0;
            Require(contract.nearTerrainMeshes != null && contract.nearTerrainMeshes.Length == 2,
                "production near-terrain array must contain airport and near meshes", violations);
            MeshFilter airport = contract.nearTerrainMeshes?.FirstOrDefault();
            Mesh mesh = airport == null ? null : airport.sharedMesh;
            string path = mesh == null ? string.Empty : AssetDatabase.GetAssetPath(mesh);
            AerialSceneryRoot aerial = contract.GetComponentInChildren<AerialSceneryRoot>(true);
            bool aerialIntegrated = aerial != null;
            string expectedPath = aerialIntegrated
                ? AirportPatchLidarConformanceAuthoring.OutputMeshPath
                : AirportCoreV4Authoring.AdaptiveAirportTerrainMeshPath;
            int maximumTriangles = aerialIntegrated
                ? AirportPatchLidarConformanceAuthoring.MaximumOutputTriangles
                : AirportCoreV4Authoring.MaximumAdaptiveAirportTerrainTriangles;
            Require(string.Equals(path, expectedPath,
                    StringComparison.Ordinal),
                "airport visual terrain is not the pinned authored derivative", violations);
            int triangles = mesh == null ? 0 : MeshTriangles(mesh);
            Require(triangles > 0 && triangles <= maximumTriangles,
                $"adaptive airport terrain exceeds its triangle contract: {triangles:N0}/" +
                $"{maximumTriangles:N0}", violations);
            Require(airport == null || airport.GetComponent<MeshCollider>() == null,
                "adaptive airport terrain must remain visual-only", violations);
            Require(mesh != null && Mathf.Abs(mesh.bounds.min.x + 800f) < 0.001f &&
                    Mathf.Abs(mesh.bounds.max.x - 800f) < 0.001f &&
                    Mathf.Abs(mesh.bounds.min.z + 300f) < 0.001f &&
                    Mathf.Abs(mesh.bounds.max.z - 300f) < 0.001f,
                "adaptive airport terrain changed the sealed V2 horizontal bounds", violations);
            Mesh source = AssetDatabase.LoadAssetAtPath<Mesh>(EnvironmentReconstructionV2Authoring.AirportMeshPath);
            Require(source != null && MeshTriangles(source) == 76800,
                "pinned 5 m V2 airport source mesh is missing or changed", violations);
            string surfacePath = contract.authoritativeTerrainSurface == null
                ? string.Empty
                : AssetDatabase.GetAssetPath(contract.authoritativeTerrainSurface);
            Require(string.Equals(surfacePath,
                    "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset",
                    StringComparison.Ordinal),
                "authoritative terrain query data changed during visual-mesh reduction", violations);
            TextAsset report = AssetDatabase.LoadAssetAtPath<TextAsset>(
                AirportCoreV4Authoring.AdaptiveAirportTerrainReportPath);
            Require(report != null &&
                    report.text.Contains("PASS_ERROR_BOUNDED_ADAPTIVE_AIRPORT_VISUAL_MESH") &&
                    report.text.Contains("\"outerBoundarySourceSamplesPreserved\": true") &&
                    report.text.Contains("\"runwayCorridorSourceSamplesPreserved\": true") &&
                    report.text.Contains("\"authoritativeSurfaceQueryUnchanged\": true"),
                "adaptive airport terrain provenance/report contract is incomplete", violations);
            if (aerialIntegrated)
            {
                TextAsset conformance = AssetDatabase.LoadAssetAtPath<TextAsset>(
                    AirportPatchLidarConformanceAuthoring.OutputReportPath);
                AirportPatchLidarConformanceAuthoring.ConformanceReport parsed = conformance == null
                    ? null
                    : JsonUtility.FromJson<AirportPatchLidarConformanceAuthoring.ConformanceReport>(conformance.text);
                Require(parsed != null &&
                        parsed.boundaryProbeCount >= 4000 &&
                        parsed.maximumBoundaryMismatchMeters <=
                            AirportPatchLidarConformanceAuthoring.MaximumBoundaryMismatchMeters &&
                        parsed.interiorOriginalVertexChanges == 0 &&
                        parsed.runwayProtectedVertexChanges == 0 &&
                        parsed.sourceLidarMeshUnchanged &&
                        parsed.authoritativeTerrainQueryUnchanged,
                    "airport/lidar conformance provenance or dense boundary contract is incomplete", violations);
            }
            return triangles;
        }

        private static bool ValidateOneAtlasLightmapContract(ICollection<string> violations)
        {
            LightmapParameters parameters = AssetDatabase.LoadAssetAtPath<LightmapParameters>(
                AirportCoreV4Authoring.OneAtlasLightmapParametersPath);
            bool parametersValid = parameters != null && parameters.limitLightmapCount &&
                                   parameters.maxLightmapCount == 1 && parameters.bakedLightmapTag == 0;
            Require(parametersValid,
                "one-atlas Lightmap Parameters asset is missing or not bounded to one atlas", violations);

            const string scenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
            string absoluteScene = Path.GetFullPath(Path.Combine(Application.dataPath, "..", scenePath));
            string parameterGuid = AssetDatabase.AssetPathToGUID(
                AirportCoreV4Authoring.OneAtlasLightmapParametersPath);
            string sceneText = File.Exists(absoluteScene) ? File.ReadAllText(absoluteScene) : string.Empty;
            Require(!string.IsNullOrWhiteSpace(parameterGuid) && Regex.IsMatch(sceneText,
                    @"m_LightmapParameters:\s*\{fileID:\s*-?\d+,\s*guid:\s*" +
                    Regex.Escape(parameterGuid) + @",\s*type:\s*2\}"),
                "ProductionVerticalSlice does not serialize the bounded Lightmap Parameters assignment", violations);

            const string lightingFolder = "Assets/Scenes/ProductionVerticalSlice";
            string[] paths = AssetDatabase.FindAssets("t:Texture2D", new[] { lightingFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileName(path).StartsWith("Lightmap-", StringComparison.Ordinal) &&
                               path.IndexOf("_comp_", StringComparison.Ordinal) >= 0)
                .ToArray();
            string[] colors = paths.Where(path =>
                path.IndexOf("_comp_light", StringComparison.Ordinal) >= 0).ToArray();
            string[] directions = paths.Where(path =>
                path.IndexOf("_comp_dir", StringComparison.Ordinal) >= 0).ToArray();
            Require(colors.Length == 1 && directions.Length == 0,
                $"expected one non-directional lightmap asset, found colors={colors.Length}, " +
                $"directional={directions.Length}", violations);
            if (colors.Length == 1)
            {
                Texture2D color = AssetDatabase.LoadAssetAtPath<Texture2D>(colors[0]);
                TextureImporter importer = AssetImporter.GetAtPath(colors[0]) as TextureImporter;
                TextureImporterPlatformSettings android = importer == null
                    ? default
                    : importer.GetPlatformTextureSettings("Android");
                Require(color != null && color.width <= 1024 && color.height <= 1024,
                    "accepted lightmap exceeds 1024x1024", violations);
                Require(importer != null && importer.mipmapEnabled && importer.streamingMipmaps &&
                        android.overridden && android.format == TextureImporterFormat.ASTC_HDR_6x6,
                    "accepted lightmap does not use the bounded Android ASTC HDR import contract", violations);
            }
            string lightingDataPath = lightingFolder + "/LightingData.asset";
            LightingDataAsset lightingData = AssetDatabase.LoadAssetAtPath<LightingDataAsset>(lightingDataPath);
            string lightingDataGuid = AssetDatabase.AssetPathToGUID(lightingDataPath);
            Require(lightingData != null && !string.IsNullOrWhiteSpace(lightingDataGuid) && Regex.IsMatch(sceneText,
                        @"m_LightingDataAsset:\s*\{fileID:\s*112000000,\s*guid:\s*" +
                        Regex.Escape(lightingDataGuid) + @",\s*type:\s*2\}"),
                "accepted scene LightingData.asset is missing", violations);
            Require(AssetDatabase.LoadAssetAtPath<TextAsset>(
                        lightingFolder + "/BAKE_MANIFEST.json") != null,
                "accepted finite-bake manifest is missing", violations);
            return parametersValid && colors.Length == 1 && directions.Length == 0;
        }

        public static void ValidateBatch()
        {
            string output = Environment.GetEnvironmentVariable("QFL_AIRPORT_CORE_V4_VALIDATION_DIR");
            if (string.IsNullOrWhiteSpace(output))
                output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../airport_core_v4_validation"));
            Directory.CreateDirectory(output);
            Report report = Validate();
            File.WriteAllText(Path.Combine(output, "airport_core_benchmark_validation.json"),
                JsonUtility.ToJson(report, true) + Environment.NewLine);
            File.WriteAllText(Path.Combine(output, "airport_core_benchmark_validation.md"),
                BuildMarkdown(report));
            Debug.Log("[QuestFlight][AirportCoreV4] " + report.classification);
            EditorApplication.Exit(report.passed ? 0 : 2);
        }

        private static void ValidateLodGroup(
            LODGroup group,
            string label,
            int minimumLod0,
            int maximumLod0,
            ICollection<string> violations,
            out int lod0,
            out int lod1,
            out int lod2)
        {
            lod0 = lod1 = lod2 = 0;
            if (group == null)
            {
                AddViolation(violations, label + " LODGroup is missing");
                return;
            }

            LOD[] lods = group.GetLODs();
            if (lods.Length != 3)
            {
                AddViolation(violations, $"{label} must have exactly three LODs, found {lods.Length}");
                return;
            }

            if (lods.Any(lod => lod.renderers == null || lod.renderers.Length == 0 ||
                                lod.renderers.Any(renderer => renderer == null)))
            {
                AddViolation(violations, label + " contains a null or empty LOD renderer array");
                return;
            }

            lod0 = lods[0].renderers.Sum(RendererTriangles);
            lod1 = lods[1].renderers.Sum(RendererTriangles);
            lod2 = lods[2].renderers.Sum(RendererTriangles);
            Require(lod0 >= minimumLod0 && lod0 <= maximumLod0,
                $"{label} LOD0 is outside {minimumLod0:N0}-{maximumLod0:N0}: {lod0:N0}", violations);
            Require(lod1 >= Mathf.FloorToInt(lod0 * 0.35f) && lod1 <= Mathf.CeilToInt(lod0 * 0.50f),
                $"{label} LOD1 must be 35-50% of LOD0: {lod1:N0}/{lod0:N0}", violations);
            Require(lod2 >= Mathf.FloorToInt(lod0 * 0.08f) && lod2 <= Mathf.CeilToInt(lod0 * 0.15f),
                $"{label} LOD2 must be 8-15% of LOD0: {lod2:N0}/{lod0:N0}", violations);
            Require(lods[0].screenRelativeTransitionHeight > lods[1].screenRelativeTransitionHeight &&
                    lods[1].screenRelativeTransitionHeight > lods[2].screenRelativeTransitionHeight,
                label + " LOD thresholds are not strictly descending", violations);
        }

        private static void ValidateReservoir(
            ProductionEnvironmentRoot environment,
            AirportCoreBenchmarkRoot root,
            AirportCoreBenchmarkManifest manifest,
            ICollection<string> violations)
        {
            const string meshPath =
                "Assets/Production/Environment/Generated/BoulderReservoir_OSM_AuthoritativeWater_Mesh.asset";
            const string shaderPath =
                "Assets/Production/Environment/AirportCoreV4/Water/Shaders/KBDUReservoirWaterV4.shader";
            const string materialPath =
                "Assets/Production/Environment/AirportCoreV4/Water/Materials/AirportCoreReservoir_V4.mat";
            Require(environment != null, "production environment contract is missing", violations);
            if (environment == null) return;

            MeshFilter surface = environment.boulderReservoirSurface;
            Renderer renderer = surface == null ? null : surface.GetComponent<Renderer>();
            Renderer shoreRenderer = environment.boulderReservoirShoreBank == null
                ? null
                : environment.boulderReservoirShoreBank.GetComponent<Renderer>();
            Mesh mesh = surface == null ? null : surface.sharedMesh;
            Material material = renderer == null ? null : renderer.sharedMaterial;
            Require(renderer != null && ReferenceEquals(renderer, root.boulderReservoir),
                "benchmark reservoir is not the authoritative product renderer", violations);
            Require(mesh != null && mesh.subMeshCount == 1,
                "authoritative reservoir must have exactly one submesh", violations);
            Require(mesh != null && string.Equals(AssetDatabase.GetAssetPath(mesh), meshPath, StringComparison.Ordinal),
                "authoritative reservoir mesh identity changed", violations);
            Require(renderer == null || renderer.GetComponentsInChildren<Camera>(true).Length == 0,
                "reservoir hierarchy must not own a camera", violations);
            Require(renderer == null || renderer.GetComponentsInChildren<ReflectionProbe>(true)
                    .All(probe => probe.mode != ReflectionProbeMode.Realtime),
                "reservoir hierarchy must not own a realtime reflection probe", violations);
            Require(environment.boulderReservoirShorelineClosed,
                "authoritative reservoir shoreline is not closed", violations);
            Require(environment.boulderReservoirSourceShorelineVertexCount >= 3 &&
                    environment.boulderReservoirSmoothedShorelineVertexCount ==
                    environment.boulderReservoirSourceShorelineVertexCount * 2,
                "authoritative reservoir shoreline densification contract changed", violations);
            Require(environment.boulderReservoirShorelineSelfIntersectionCount == 0,
                "authoritative reservoir shoreline self-intersects", violations);
            Require(environment.minimumWaterTerrainSeparationMeters >=
                    ProductionEnvironmentRoot.MinimumAcceptedWaterTerrainSeparationMeters,
                "authoritative reservoir terrain separation regressed", violations);

            bool orthophotoOnly = environment.waterPresentationMode ==
                                  ProductionWaterPresentationMode.OrthophotoOnly;
            if (orthophotoOnly)
            {
                Require(manifest.waterEffectsDeferredToOrthophoto,
                    "environment and benchmark water-defer contracts disagree", violations);
                Require(renderer != null && !renderer.enabled && !renderer.gameObject.activeSelf,
                    "orthophoto-only reservoir renderer is not explicitly disabled", violations);
                Require(renderer == null || renderer.sharedMaterials.Length == 0,
                    "orthophoto-only reservoir retains a serialized material slot", violations);
                Require(shoreRenderer != null && !shoreRenderer.enabled && !shoreRenderer.gameObject.activeSelf,
                    "orthophoto-only shore-bank renderer is not explicitly disabled", violations);
                Require(shoreRenderer == null || shoreRenderer.sharedMaterials.Length == 0,
                    "orthophoto-only shore bank retains a serialized material slot", violations);
                Require(manifest.reservoirMaterial == null && root.v2ReservoirMaterial == null,
                    "orthophoto-only water retains a manifest or A/B material dependency", violations);
                Require(environment.GetComponentsInChildren<Renderer>(true)
                        .SelectMany(item => item.sharedMaterials)
                        .Where(item => item != null)
                        .All(item => !string.Equals(AssetDatabase.GetAssetPath(item), materialPath,
                            StringComparison.Ordinal)),
                    "deferred V4 reservoir material remains bound in the product hierarchy", violations);
                return;
            }

            string rendererMaterialPath = material == null ? string.Empty : AssetDatabase.GetAssetPath(material);
            string manifestMaterialPath = manifest.reservoirMaterial == null
                ? string.Empty
                : AssetDatabase.GetAssetPath(manifest.reservoirMaterial);
            Require(material != null && manifest.reservoirMaterial != null &&
                    string.Equals(rendererMaterialPath, materialPath, StringComparison.Ordinal) &&
                    string.Equals(manifestMaterialPath, materialPath, StringComparison.Ordinal),
                "authoritative reservoir does not use the manifest V4 material", violations);
            Require(renderer != null && renderer.sharedMaterials.Length == 1,
                "authoritative reservoir must have exactly one material slot", violations);
            Require(material != null && material.shader != null &&
                    string.Equals(material.shader.name, ProductionEnvironmentRoot.ProductionReservoirShaderName,
                        StringComparison.Ordinal),
                "authoritative reservoir does not use the exact V4 opaque shader", violations);
            Require(material != null && material.renderQueue == (int)RenderQueue.Geometry + 5,
                "authoritative reservoir must use the Geometry+5 queue", violations);
            Require(material != null && material.passCount == 1,
                "authoritative reservoir must use exactly one shader pass", violations);
            Require(material != null && material.enableInstancing,
                "authoritative reservoir material must support GPU instancing", violations);
            foreach (string property in new[]
                     {
                         "_WaveNormalA", "_WaveNormalB", "_DepthShoreRoughness",
                         "_TerrainMacroAlbedo", "_EnvironmentCube"
                     })
                Require(material != null && material.HasProperty(property) && material.GetTexture(property) != null,
                    "authoritative reservoir is missing texture binding " + property, violations);
            Require(material != null && environment.GetComponentsInChildren<Renderer>(true)
                    .Count(item => item.sharedMaterial == material) == 1,
                "V4 reservoir material must be bound to exactly one product renderer", violations);

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            Require(shader != null && material != null && ReferenceEquals(shader, material.shader),
                "V4 reservoir shader asset is missing or not bound", violations);
            if (shader == null) return;
            Require(shader.isSupported, "V4 reservoir shader is unsupported", violations);
            Require(ShaderUtil.GetShaderMessages(shader).All(message =>
                    !string.Equals(message.severity.ToString(), "Error", StringComparison.OrdinalIgnoreCase)),
                "V4 reservoir shader has compiler errors", violations);

            string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", shaderPath));
            Require(File.Exists(absolutePath), "V4 reservoir shader source is missing", violations);
            if (!File.Exists(absolutePath)) return;
            string source = File.ReadAllText(absolutePath);
            foreach (string required in new[]
                     {
                         "\"RenderType\"=\"Opaque\"", "\"Queue\"=\"Geometry+5\"", "ZWrite On", "Fallback Off",
                         "texCUBElod(_EnvironmentCube", "UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO",
                         "UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX"
                     })
                Require(source.Contains(required), "V4 reservoir shader is missing required token: " + required,
                    violations);
            Require(Regex.Matches(source, @"(?m)^\s*Pass\s*$").Count == 1,
                "V4 reservoir shader source must contain exactly one pass", violations);
            foreach (string forbidden in new[]
                     {
                         @"(?m)^\s*Blend\b", @"\bGrabPass\b", @"\bUsePass\b", @"ZWrite\s+Off",
                         @"_CameraOpaqueTexture", @"_CameraDepthTexture", @"unity_SpecCube0",
                         @"\bSSR\b", @"refraction", @"tessell"
                     })
                Require(!Regex.IsMatch(source, forbidden, RegexOptions.IgnoreCase),
                    "V4 reservoir shader contains forbidden path: " + forbidden, violations);
        }

        private static void ValidateCleanPlateTerrainBinding(
            AirportCoreBenchmarkRoot root,
            AirportCoreBenchmarkManifest manifest,
            ICollection<string> violations)
        {
            Renderer terrain = root.minorWaterTerrainEffect;
            Material material = terrain == null ? null : terrain.sharedMaterial;
            Require(terrain != null && material != null,
                "airport terrain clean-plate binding is missing", violations);
            if (material == null) return;
            Require(material.HasProperty("_AirportCoreCleanPlateAlbedo") &&
                    ReferenceEquals(material.GetTexture("_AirportCoreCleanPlateAlbedo"), manifest.cleanPlateAlbedo),
                "clean-plate albedo is not integrated into the authoritative terrain material", violations);
            Require(material.HasProperty("_AirportCoreCleanPlateMask") &&
                    ReferenceEquals(material.GetTexture("_AirportCoreCleanPlateMask"), manifest.replacementMask),
                "clean-plate replacement mask is not integrated into the authoritative terrain material", violations);
            Require(material.HasProperty("_AirportCoreCleanPlateStrength") &&
                    material.GetFloat("_AirportCoreCleanPlateStrength") > 0.999f,
                "terrain-integrated clean plate is not fully enabled", violations);
            Require(root.benchmarkContent != null &&
                    root.benchmarkContent.GetComponentsInChildren<Renderer>(true)
                        .All(renderer => !string.Equals(renderer.name, "AirportCoreCleanPlate", StringComparison.Ordinal)),
                "obsolete clean-plate overlay renderer remains in the benchmark", violations);
            Require(root.benchmarkContent != null &&
                    root.benchmarkContent.GetComponentsInChildren<Renderer>(true)
                        .SelectMany(renderer => renderer.sharedMaterials ?? Array.Empty<Material>())
                        .All(item => item == null || !string.Equals(item.name, "AirportCoreCleanPlate", StringComparison.Ordinal)),
                "obsolete clean-plate overlay material remains in the benchmark", violations);
        }

        private static int RendererTriangles(Renderer renderer)
        {
            if (renderer == null) return 0;
            Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null && renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            if (mesh == null) return 0;
            long indexCount = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                indexCount += (long)mesh.GetIndexCount(subMesh);
            return (int)Math.Min(int.MaxValue, indexCount / 3L);
        }

        private static int MeshTriangles(Mesh mesh)
        {
            if (mesh == null) return 0;
            long indexCount = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                indexCount += (long)mesh.GetIndexCount(subMesh);
            return (int)Math.Min(int.MaxValue, indexCount / 3L);
        }

        private static bool UsesProjectMesh(Renderer renderer)
        {
            Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null && renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            if (mesh == null) return false;
            string path = AssetDatabase.GetAssetPath(mesh);
            return !string.IsNullOrWhiteSpace(path) && path.StartsWith("Assets/", StringComparison.Ordinal) &&
                   path.IndexOf("unity default resources", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static IEnumerable<Texture> ManifestTextures(AirportCoreBenchmarkManifest manifest)
        {
            yield return manifest.terminalAlbedo;
            yield return manifest.terminalNormal;
            yield return manifest.terminalMrao;
            yield return manifest.hangarKitAlbedo;
            yield return manifest.hangarKitNormal;
            yield return manifest.hangarKitMrao;
            yield return manifest.cleanPlateAlbedo;
            yield return manifest.replacementMask;
            yield return manifest.originalShadowMask;
            yield return manifest.reflectionCubemap;
            foreach (Material material in new[]
                     {
                         manifest.terminalMaterial, manifest.hangarKitMaterial, manifest.runwayMaterial,
                         manifest.reservoirMaterial, manifest.vegetationMaterial
                     }.Where(item => item != null))
            {
                Shader shader = material.shader;
                if (shader == null) continue;
                int propertyCount = shader.GetPropertyCount();
                for (int index = 0; index < propertyCount; index++)
                    if (shader.GetPropertyType(index) == ShaderPropertyType.Texture)
                        yield return material.GetTexture(shader.GetPropertyName(index));
            }
        }

        private static void ValidateTexture(Texture texture, ICollection<string> violations)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            Require(!string.IsNullOrWhiteSpace(path), "benchmark texture is not a project asset", violations);
            if (string.IsNullOrWhiteSpace(path)) return;
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Require(importer != null, "texture has no TextureImporter: " + path, violations);
            if (importer == null) return;
            Require(importer.mipmapEnabled, "texture mipmaps are disabled: " + path, violations);
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Require(android.overridden && IsAstc(android.format),
                "texture is not explicitly ASTC for Android: " + path, violations);
        }

        private static bool IsAstc(TextureImporterFormat format)
        {
            return format == TextureImporterFormat.ASTC_4x4 ||
                   format == TextureImporterFormat.ASTC_5x5 ||
                   format == TextureImporterFormat.ASTC_6x6 ||
                   format == TextureImporterFormat.ASTC_8x8 ||
                   format == TextureImporterFormat.ASTC_10x10 ||
                   format == TextureImporterFormat.ASTC_12x12 ||
                   format == TextureImporterFormat.ASTC_HDR_4x4 ||
                   format == TextureImporterFormat.ASTC_HDR_5x5 ||
                   format == TextureImporterFormat.ASTC_HDR_6x6 ||
                   format == TextureImporterFormat.ASTC_HDR_8x8 ||
                   format == TextureImporterFormat.ASTC_HDR_10x10 ||
                   format == TextureImporterFormat.ASTC_HDR_12x12;
        }

        private static Report Failed(string violation)
        {
            return new Report
            {
                classification = "FAIL_AIRPORT_CORE_V4_2_STRUCTURAL_GATE",
                passed = false,
                violations = new[] { violation }
            };
        }

        private static string BuildMarkdown(Report report)
        {
            return "# Airport Core V4.2 structural validation\n\n" +
                   $"- Classification: `{report.classification}`\n" +
                   $"- Benchmark renderers/triangles/materials: {report.benchmarkRendererCount:N0}/" +
                   $"{report.benchmarkTriangleCount:N0}/{report.benchmarkMaterialCount:N0}\n" +
                   $"- Terminal LOD0/1/2: {report.terminalLod0Triangles:N0}/" +
                   $"{report.terminalLod1Triangles:N0}/{report.terminalLod2Triangles:N0}\n" +
                   $"- Hangar LOD0/1/2: {report.hangarLod0Triangles:N0}/" +
                   $"{report.hangarLod1Triangles:N0}/{report.hangarLod2Triangles:N0}\n" +
                   $"- Adaptive airport terrain triangles: {report.adaptiveAirportTerrainTriangles:N0}\n" +
                   $"- One-atlas parameters valid: {report.oneAtlasLightmapParametersValid}\n" +
                   $"- Violations: {report.violations.Length:N0}\n\n" + report.evidenceBoundary + "\n";
        }

        private static void Require(bool condition, string message, ICollection<string> violations)
        {
            if (!condition) AddViolation(violations, message);
        }

        private static void AddViolation(ICollection<string> violations, string message)
        {
            if (violations.Count < 200) violations.Add(message);
        }
    }
}
