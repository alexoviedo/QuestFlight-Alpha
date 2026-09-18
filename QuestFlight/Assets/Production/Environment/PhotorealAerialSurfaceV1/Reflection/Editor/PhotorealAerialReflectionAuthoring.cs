#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuestFlightLab.Environment;
using QuestFlightLab.PhotorealAerialSurfaceV1.Reflection;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace QuestFlightLab.PhotorealAerialSurfaceV1.Reflection.Editor
{
    /// <summary>
    /// Deterministic offline authoring for the reflection/cockpit handoff. The assets-only
    /// entry point is safe for workstream generation. IntegrateForLead is deliberately
    /// separate because it serializes explicit prefab and scene bindings.
    /// </summary>
    public static class PhotorealAerialReflectionAuthoring
    {
        public const string Root =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection";
        public const string TextureFolder = Root + "/Textures";
        public const string ProfileFolder = Root + "/Profiles";
        public const string CockpitMaterialFolder = Root + "/Cockpit/Materials";
        public const string CockpitMeshFolder = Root + "/Cockpit/Meshes";
        public const string CockpitTextureFolder = Root + "/Cockpit/Textures";
        public const string GlobalReflectionPath =
            TextureFolder + "/KBDU_GlobalGroundSkyReflection_256.exr";
        public const string AirportLocalReflectionPath =
            TextureFolder + "/KBDU_AirportLocalReflection_256.exr";
        public const string CockpitInteriorReflectionPath =
            TextureFolder + "/C172_CockpitInteriorReflection_256.exr";
        public const string ManifestPath =
            Root + "/KBDU_PhotorealAerialReflectionManifestV1.json";
        public const string ProfilePath =
            ProfileFolder + "/KBDU_PhotorealAerialReflectionProfile.asset";
        public const string WindowMaterialPath =
            CockpitMaterialFolder + "/ProductionCockpitWindowReflection.mat";
        public const string InstrumentGlassMaterialPath =
            CockpitMaterialFolder + "/ProductionInstrumentGlassReflection.mat";
        public const string InstrumentDisplayReadoutMaterialPath =
            CockpitMaterialFolder + "/ProductionInstrumentDisplayReadout.mat";
        public const string InstrumentDisplayReadoutShaderPath =
            Root + "/Cockpit/Shaders/ProductionInstrumentDisplayReadout.shader";
        public const string CockpitStableInteriorShaderPath =
            Root + "/Cockpit/Shaders/ProductionCockpitStableInterior.shader";
        public const string InstrumentDisplayBaseColorPath =
            CockpitTextureFolder + "/C172_StaticDisplayReadable_BaseColor.png";
        public const string InstrumentDisplayEmissionPath =
            CockpitTextureFolder + "/C172_StaticDisplayReadable_Emission.png";

        public const string WindowShaderName =
            "QuestFlight/Photoreal Aerial Surface V1/Cockpit Window Reflection";
        public const string InstrumentGlassShaderName =
            "QuestFlight/Photoreal Aerial Surface V1/Instrument Glass Reflection";
        public const string InstrumentDisplayReadoutShaderName =
            "QuestFlight/Photoreal Aerial Surface V1/Instrument Display Readout";
        public const string CockpitStableInteriorShaderName =
            QuestFlightLab.Aircraft.ProductionAircraftLightingController.StableCockpitShaderName;

        public const string SourceSkyPath =
            "Assets/Production/Environment/Sky/Kloppenheim03_PureSky_2K.hdr";
        public const string SourceNearOrthophotoPath =
            "Assets/Production/Environment/AerialSurfaceV1/Ground/KBDU_City2020_Near4km_8192.jpg";
        public const string SourceMidOrthophotoPath =
            "Assets/Production/Environment/AerialSurfaceV1/Ground/KBDU_City2020_MidBase12km_8192.jpg";
        public const string SourceFarOrthophotoPath =
            "Assets/Production/Environment/AerialSurfaceV1/Ground/KBDU_City2020_Far24km_4096.jpg";
        public const string SourceOrthophotoManifestPath =
            "Assets/Production/Environment/AerialSurfaceV1/Ground/KBDU_AerialSurfaceV1_SourceManifest.json";
        public const string EnvironmentPrefabPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string ProductionScenePath =
            "Assets/Scenes/ProductionVerticalSlice.unity";
        public const string ProductionWorldPrefabPath =
            "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        public const string MainSunName = "Sun";
        public const string CockpitShadowSunName = "CockpitShadowSun";
        public const string CockpitShadowLayerName = "CockpitSunShadow";
        public const int CockpitShadowLayer = 30;
        public const string SourceWindowMaterialPath =
            "Assets/Production/Materials/Cockpit/Cockpit_GlassMat_New_-8047257934222504514.mat";
        public const string SourceInstrumentGlassMaterialPath =
            "Assets/Production/Materials/Cockpit/Cockpit_MI_Glass1_-7336442739054310687.mat";
        public const string SourceInstrumentDisplayMaterialPath =
            "Assets/Production/Materials/Cockpit/Cockpit_New_Mat_01_8844457045159653726.mat";

        public const string FoundationObjectName = "PhotorealAerialReflectionFoundation";
        public const string AirportProbeObjectName = "KBDU_AirportReflectionProbe";
        public const string CockpitRootName = "ImportedCockpitInterior";
        public const string ExteriorWindowRendererName =
            "Cessna_Exterior_Glass_MAT_0_Glass_MAT_0";
        public const string InstrumentLensArrayName =
            "ProductionPrincipalInstrumentLensArray";
        public const string InstrumentLensArrayMeshPath =
            Root + "/Cockpit/Meshes/C172_PrincipalInstrumentLensArray.asset";
        public const string InstrumentDisplayReadoutName =
            "ProductionInstrumentDisplayReadouts";
        public const string InstrumentDisplayReadoutMeshPath =
            Root + "/Cockpit/Meshes/C172_InstrumentDisplayReadouts.asset";
        public const string DecorativeInstrumentCoverRendererName =
            "GlassPartNew_GlassMat_New_0_GlassMat_New_0";
        public const string MisidentifiedInstrumentGlassRendererName =
            "Cessna_Interior_MI_Glass1_0_MI_Glass1_0";
        public const string InstrumentMeterRendererName =
            "Cessna_Interior_Meter_01_MAT_0_Meter_01_MAT_0";
        public const string InstrumentDisplayRendererName =
            "Static_Display_New_Mat_01_0_New_Mat_01_0";
        public const string RejectedGeneratedInstrumentCoverName =
            "ProductionPrincipalInstrumentGlassCover";
        public const string RejectedCockpitSunOcclusionObjectName =
            "ProductionCockpitBakedSunOcclusion";

        private const int ReflectionEquirectangularWidth = 256;
        private const int ReflectionEquirectangularHeight = 128;
        private const float LocalProxyHeightMeters = 72f;
        private const float LocalProxyMaximumRadiusMeters = 1700f;
        private const float GlobalProxyHeightMeters = 720f;
        private const float GlobalProxyMaximumRadiusMeters = 11200f;
        private static readonly Vector2 AirportReflectionCenter = new Vector2(-385f, -260f);
        private static readonly Color GroundRadiance = new Color(0.115f, 0.128f, 0.095f, 1f);

        [MenuItem("QuestFlight/Photoreal Aerial Surface V1/Author Reflection Runtime Assets Only")]
        public static void AuthorRuntimeAssetsOnly()
        {
            EnsureFolders();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ReflectionAssetPair reflections = GenerateGroundSkyReflections();
            EnsureInstrumentDisplayTextures();
            EnsureMaterials();
            EnsureInstrumentLensMeshAsset();
            EnsureInstrumentDisplayReadoutMeshAsset();
            EnsureProfile(reflections.Global, reflections.AirportLocal);
            AssetDatabase.SaveAssets();
            WriteManifest(reflections);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateRuntimeAssets();
            Debug.Log(
                "[QuestFlight][PhotorealAerialReflection] Authored assets only: distinct 128-face static " +
                "global and airport-local imagery-aware ground-sky cubemaps, two single-pass reflection materials, " +
                "one geometry-bound cockpit depth shader, and one default-off shadow profile. " +
                "No prefab, scene, camera, probe hierarchy, or project quality setting was changed.");
        }

        public static void AuthorRuntimeAssetsOnlyBatch()
        {
            try
            {
                AuthorRuntimeAssetsOnly();
                Debug.Log("[QuestFlight][PhotorealAerialReflection] ASSETS_ONLY_SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Rebuilds only the geometry-derived principal gauge lens mesh.  This
        /// focused entry point deliberately avoids cubemap generation so gauge
        /// aperture corrections remain deterministic in headless build agents.
        /// </summary>
        public static void RebuildInstrumentLensMeshOnlyBatch()
        {
            try
            {
                EnsureFolders();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                EnsureInstrumentLensMeshAsset();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log(
                    "[QuestFlight][PhotorealAerialReflection] " +
                    "INSTRUMENT_LENS_MESH_ONLY_SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void IntegrateInstrumentMaterialRestorationBatch()
        {
            try
            {
                EnsureFolders();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                EnsureInstrumentDisplayTextures();
                EnsureMaterials();
                EnsureInstrumentLensMeshAsset();
                EnsureInstrumentDisplayReadoutMeshAsset();
                IntegrateAircraftPrefab();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[QuestFlight][InstrumentGlass] INSTRUMENT_MATERIAL_RESTORATION_SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void DiagnoseInstrumentCubemapCardinalsBatch()
        {
            const string temporaryPath =
                "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Textures/__InstrumentCardinalDiagnostic.exr";
            try
            {
                EnsureFolders();
                Color[] pixels = new Color[ReflectionEquirectangularWidth * ReflectionEquirectangularHeight];
                for (int y = 0; y < ReflectionEquirectangularHeight; y++)
                {
                    float latitude = (((y + 0.5f) / ReflectionEquirectangularHeight) - 0.5f) * Mathf.PI;
                    float cosLatitude = Mathf.Cos(latitude);
                    for (int x = 0; x < ReflectionEquirectangularWidth; x++)
                    {
                        float longitude = (((x + 0.5f) / ReflectionEquirectangularWidth) - 0.5f) *
                                          Mathf.PI * 2f;
                        Vector3 direction = new Vector3(
                            Mathf.Sin(longitude) * cosLatitude,
                            Mathf.Sin(latitude),
                            Mathf.Cos(longitude) * cosLatitude);
                        pixels[y * ReflectionEquirectangularWidth + x] = CardinalColor(direction);
                    }
                }

                Texture2D equirectangular = CreateReflectionTexture("InstrumentCardinalDiagnostic", pixels);
                try
                {
                    WriteIfChanged(temporaryPath,
                        equirectangular.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(equirectangular);
                }

                AssetDatabase.ImportAsset(temporaryPath, ImportAssetOptions.ForceSynchronousImport);
                TextureImporter importer = AssetImporter.GetAtPath(temporaryPath) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Cardinal diagnostic importer is missing.");
                importer.textureShape = TextureImporterShape.TextureCube;
                importer.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.cubemapConvolution = TextureImporterCubemapConvolution.None;
                importer.SetTextureSettings(settings);
                importer.sRGBTexture = false;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 128;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                Cubemap cubemap = LoadRequired<Cubemap>(temporaryPath);
                string report = string.Join(", ", new[]
                {
                    "+X=" + CardinalName(cubemap.GetPixel(CubemapFace.PositiveX, 64, 64)),
                    "-X=" + CardinalName(cubemap.GetPixel(CubemapFace.NegativeX, 64, 64)),
                    "+Z=" + CardinalName(cubemap.GetPixel(CubemapFace.PositiveZ, 64, 64)),
                    "-Z=" + CardinalName(cubemap.GetPixel(CubemapFace.NegativeZ, 64, 64)),
                    "+Y=" + CardinalName(cubemap.GetPixel(CubemapFace.PositiveY, 64, 64)),
                    "-Y=" + CardinalName(cubemap.GetPixel(CubemapFace.NegativeY, 64, 64))
                });
                Debug.Log("[QuestFlight][InstrumentGlass] CARDINAL_ROUND_TRIP " + report);
                AssetDatabase.DeleteAsset(temporaryPath);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
            finally
            {
                AssetDatabase.DeleteAsset(temporaryPath);
            }
        }

        public static void DiagnoseInstrumentDisplayUvBatch()
        {
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
                Renderer renderer = root.GetComponentsInChildren<Renderer>(true).Single(item =>
                    string.Equals(item.name, "Static_Display_New_Mat_01_0_New_Mat_01_0",
                        StringComparison.Ordinal));
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) throw new InvalidOperationException("Static display mesh is missing.");
                Vector2[] uvs = mesh.uv;
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                // Imported glTF meshes frequently duplicate vertices at UV seams. Group by
                // quantized position so the report describes physical display surfaces rather
                // than thousands of disconnected individual triangles.
                var trianglesByPosition = new Dictionary<Vector3Int, List<int>>();
                for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = triangles[triangle * 3 + corner];
                    Vector3 position = vertices[vertex];
                    var key = new Vector3Int(
                        Mathf.RoundToInt(position.x * 100000f),
                        Mathf.RoundToInt(position.y * 100000f),
                        Mathf.RoundToInt(position.z * 100000f));
                    if (!trianglesByPosition.TryGetValue(key, out List<int> adjacent))
                        trianglesByPosition.Add(key, adjacent = new List<int>());
                    adjacent.Add(triangle);
                }
                var queue = new Queue<int>();
                bool[] visited = new bool[triangles.Length / 3];
                int componentIndex = 0;
                for (int triangle = 0; triangle < visited.Length; triangle++)
                {
                    if (visited[triangle]) continue;
                    visited[triangle] = true;
                    queue.Enqueue(triangle);
                    var componentVertices = new HashSet<int>();
                    int triangleCount = 0;
                    while (queue.Count > 0)
                    {
                        int current = queue.Dequeue();
                        triangleCount++;
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int vertex = triangles[current * 3 + corner];
                            componentVertices.Add(vertex);
                            Vector3 position = vertices[vertex];
                            var key = new Vector3Int(
                                Mathf.RoundToInt(position.x * 100000f),
                                Mathf.RoundToInt(position.y * 100000f),
                                Mathf.RoundToInt(position.z * 100000f));
                            foreach (int neighbor in trianglesByPosition[key])
                            {
                                if (visited[neighbor]) continue;
                                visited[neighbor] = true;
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                    Vector2 uvMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                    Vector2 uvMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                    bool initialized = false;
                    Bounds bounds = default;
                    foreach (int vertex in componentVertices)
                    {
                        uvMin = Vector2.Min(uvMin, uvs[vertex]);
                        uvMax = Vector2.Max(uvMax, uvs[vertex]);
                        if (!initialized) { bounds = new Bounds(vertices[vertex], Vector3.zero); initialized = true; }
                        else bounds.Encapsulate(vertices[vertex]);
                    }
                    if (bounds.size.x >= 0.012f && bounds.size.y >= 0.006f)
                        Debug.Log($"[QuestFlight][InstrumentDisplay] component={componentIndex} " +
                                  $"triangles={triangleCount} uvMin={uvMin} uvMax={uvMax} " +
                                  $"size={bounds.size} center={bounds.center}");
                    componentIndex++;
                }
                var frontFaces = new List<(float area, int triangle, Vector3 center, Vector3 normal,
                    Vector2 uvMin, Vector2 uvMax)>();
                for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
                {
                    int a = triangles[triangle * 3];
                    int b = triangles[triangle * 3 + 1];
                    int c = triangles[triangle * 3 + 2];
                    Vector3 edgeA = vertices[b] - vertices[a];
                    Vector3 edgeB = vertices[c] - vertices[a];
                    Vector3 cross = Vector3.Cross(edgeA, edgeB);
                    float area = cross.magnitude * 0.5f;
                    if (area < 0.00015f || Mathf.Abs(cross.normalized.z) < 0.80f) continue;
                    frontFaces.Add((area, triangle,
                        (vertices[a] + vertices[b] + vertices[c]) / 3f,
                        cross.normalized,
                        Vector2.Min(uvs[a], Vector2.Min(uvs[b], uvs[c])),
                        Vector2.Max(uvs[a], Vector2.Max(uvs[b], uvs[c]))));
                }
                foreach (var face in frontFaces.OrderByDescending(item => item.area).Take(48))
                    Debug.Log($"[QuestFlight][InstrumentDisplayFace] triangle={face.triangle} " +
                              $"area={face.area:F6} center={face.center} normal={face.normal} " +
                              $"uvMin={face.uvMin} uvMax={face.uvMax}");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Color CardinalColor(Vector3 direction)
        {
            Vector3 absolute = new Vector3(
                Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            if (absolute.x >= absolute.y && absolute.x >= absolute.z)
                return direction.x >= 0f ? Color.red : Color.cyan;
            if (absolute.y >= absolute.x && absolute.y >= absolute.z)
                return direction.y >= 0f ? Color.blue : Color.yellow;
            return direction.z >= 0f ? Color.green : Color.magenta;
        }

        private static string CardinalName(Color color)
        {
            var candidates = new[]
            {
                ("+X", Color.red), ("-X", Color.cyan),
                ("+Z", Color.green), ("-Z", Color.magenta),
                ("+Y", Color.blue), ("-Y", Color.yellow)
            };
            return candidates.OrderBy(item =>
                Mathf.Abs(color.r - item.Item2.r) +
                Mathf.Abs(color.g - item.Item2.g) +
                Mathf.Abs(color.b - item.Item2.b)).First().Item1;
        }

        [MenuItem("QuestFlight/Photoreal Aerial Surface V1/Integrate Reflection And Cockpit Foundation")]
        public static void IntegrateForLead()
        {
            AuthorRuntimeAssetsOnly();
            PhotorealAerialReflectionProfile profile = LoadRequired<PhotorealAerialReflectionProfile>(ProfilePath);
            IntegrateEnvironmentPrefab(profile);
            IntegrateAircraftPrefab();
            CockpitDirectionalVisibilityAuthoring.AuthorAndIntegrate();
            RemoveRejectedCockpitShadowSun();
            IntegrateSceneReflection(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateIntegratedFoundation();
            Debug.Log(
                "[QuestFlight][PhotorealAerialReflection] LEAD_INTEGRATION_SUCCESS. " +
                "The rejected realtime cockpit shadow sun is absent; static material depth remains authoritative.");
        }

        public static void IntegrateForLeadBatch()
        {
            try
            {
                IntegrateForLead();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static CockpitShadowCandidateScope BeginCockpitShadowCandidateForDevelopment(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                throw new ArgumentException("A loaded scene is required for the transient cockpit shadow A/B.", nameof(scene));
            PhotorealAerialReflectionProfile profile = LoadRequired<PhotorealAerialReflectionProfile>(ProfilePath);
            if (!profile.TryValidate(out string report))
                throw new InvalidOperationException("Reflection profile is invalid: " + report);
            return new CockpitShadowCandidateScope(scene, profile.CockpitShadowCandidate);
        }

        public static void WithCockpitShadowCandidateForDevelopment(Scene scene, Action capture)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            using (BeginCockpitShadowCandidateForDevelopment(scene)) capture();
        }

        private static ReflectionAssetPair GenerateGroundSkyReflections()
        {
            foreach (string path in new[]
                     {
                         SourceSkyPath, SourceNearOrthophotoPath, SourceMidOrthophotoPath,
                         SourceFarOrthophotoPath, SourceOrthophotoManifestPath
                     })
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("Pinned reflection source is missing.", path);
            }

            var temporaryPaths = new[]
            {
                TextureFolder + "/KBDU_ReflectionSkySource_TEMP.hdr",
                TextureFolder + "/KBDU_ReflectionNearSource_TEMP.jpg",
                TextureFolder + "/KBDU_ReflectionMidSource_TEMP.jpg",
                TextureFolder + "/KBDU_ReflectionFarSource_TEMP.jpg"
            };
            foreach (string path in temporaryPaths) AssetDatabase.DeleteAsset(path);

            Texture2D sky = null;
            Texture2D near = null;
            Texture2D mid = null;
            Texture2D far = null;
            Texture2D globalOutput = null;
            Texture2D localOutput = null;
            Texture2D cockpitOutput = null;
            ReflectionStatistics globalStatistics;
            ReflectionStatistics localStatistics;
            try
            {
                sky = ImportReadableTemporaryCopy(SourceSkyPath, temporaryPaths[0], false, 2048);
                near = ImportReadableTemporaryCopy(SourceNearOrthophotoPath, temporaryPaths[1], true, 2048);
                mid = ImportReadableTemporaryCopy(SourceMidOrthophotoPath, temporaryPaths[2], true, 2048);
                far = ImportReadableTemporaryCopy(SourceFarOrthophotoPath, temporaryPaths[3], true, 2048);

                Color[] globalPixels = BuildReflectionPixels(sky, near, mid, far, false);
                Color[] localPixels = BuildReflectionPixels(sky, near, mid, far, true);
                Color[] cockpitPixels = BuildCockpitInteriorReflectionPixels();
                globalStatistics = ReflectionStatistics.FromLowerHemisphere(globalPixels);
                localStatistics = ReflectionStatistics.FromLowerHemisphere(localPixels);
                if (globalStatistics.LuminanceStandardDeviation < 0.005f ||
                    localStatistics.LuminanceStandardDeviation < 0.005f)
                    throw new InvalidOperationException("Orthophoto lower hemisphere is unexpectedly constant.");
                if (MeanAbsoluteDifference(globalPixels, localPixels) < 0.003f)
                    throw new InvalidOperationException("Global and airport-local reflections are not distinct enough.");

                globalOutput = CreateReflectionTexture(
                    "KBDU_GlobalGroundSkyReflection_256", globalPixels);
                localOutput = CreateReflectionTexture(
                    "KBDU_AirportLocalReflection_256", localPixels);
                cockpitOutput = CreateReflectionTexture(
                    "C172_CockpitInteriorReflection_256", cockpitPixels);
                WriteIfChanged(GlobalReflectionPath,
                    globalOutput.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                WriteIfChanged(AirportLocalReflectionPath,
                    localOutput.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                WriteIfChanged(CockpitInteriorReflectionPath,
                    cockpitOutput.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
            }
            finally
            {
                foreach (Texture2D texture in new[] { sky, near, mid, far })
                    if (texture != null) Resources.UnloadAsset(texture);
                if (globalOutput != null) UnityEngine.Object.DestroyImmediate(globalOutput);
                if (localOutput != null) UnityEngine.Object.DestroyImmediate(localOutput);
                if (cockpitOutput != null) UnityEngine.Object.DestroyImmediate(cockpitOutput);
                foreach (string path in temporaryPaths) AssetDatabase.DeleteAsset(path);
            }

            Cubemap global = ConfigureReflectionImport(GlobalReflectionPath);
            Cubemap airportLocal = ConfigureReflectionImport(AirportLocalReflectionPath);
            Cubemap cockpitInterior = ConfigureReflectionImport(CockpitInteriorReflectionPath);
            if (global == airportLocal)
                throw new InvalidOperationException("Global and airport-local cubemaps resolved to the same asset.");
            return new ReflectionAssetPair(
                global, airportLocal, cockpitInterior, globalStatistics, localStatistics);
        }

        public static void RefreshTrainerCabinReflection()
        {
            Texture2D output = CreateReflectionTexture("C172_CockpitInteriorReflection_256", BuildCockpitInteriorReflectionPixels());
            try { WriteIfChanged(CockpitInteriorReflectionPath, output.EncodeToEXR(Texture2D.EXRFlags.CompressZIP)); }
            finally { UnityEngine.Object.DestroyImmediate(output); }
            ConfigureReflectionImport(CockpitInteriorReflectionPath);
            Material lens = LoadRequired<Material>(InstrumentGlassMaterialPath);
            lens.SetFloat("_ReflectionStrength", .23f);
            lens.SetFloat("_DirectGlintStrength", .065f);
            lens.SetFloat("_PerceptualRoughness", .28f);
            EditorUtility.SetDirty(lens);
            Material window = LoadRequired<Material>(WindowMaterialPath);
            window.SetFloat("_InteriorReflectionStrength", .48f);
            window.SetFloat("_ReflectionStrength", .30f);
            window.SetFloat("_WindshieldOpacity", .045f);
            window.SetFloat("_SideWindowOpacity", .06f);
            window.SetColor("_WindshieldTint", new Color(.90f, .93f, .95f, 1));
            window.SetColor("_SideWindowTint", new Color(.87f, .92f, .94f, 1));
            EditorUtility.SetDirty(window);
            AssetDatabase.SaveAssets();
        }

        private static Color[] BuildCockpitInteriorReflectionPixels()
        {
            GameObject aircraftAsset = LoadRequired<GameObject>(AircraftPrefabPath);
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("A loaded editor scene is required for cockpit reflection baking.");
            GameObject instance = null;
            Camera bakeCamera = null;
            Light bakeSun = null;
            Cubemap capture = null;
            try
            {
                instance = PrefabUtility.InstantiatePrefab(aircraftAsset, scene) as GameObject;
                if (instance == null)
                    throw new InvalidOperationException("Aircraft prefab could not be instantiated for reflection baking.");
                AircraftReferenceFrameRig rig = instance.GetComponent<AircraftReferenceFrameRig>();
                if (rig == null || rig.AircraftVisualRoot == null)
                    throw new InvalidOperationException("Aircraft visual root is missing during reflection baking.");
                Transform cockpit = FindDescendant(rig.AircraftVisualRoot, CockpitRootName);
                Transform viewpoint = FindDescendant(instance.transform, "Main Camera");
                if (cockpit == null || viewpoint == null)
                    throw new InvalidOperationException("Cockpit or authored viewpoint is missing during reflection baking.");

                // Render only the actual opaque cabin geometry. Openings receive a restrained
                // sky clear color, giving the glazing the same panel, seats, doors, roof and
                // window apertures that exist in the product without any runtime capture.
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    bool cockpitOwned = renderer.transform == cockpit || renderer.transform.IsChildOf(cockpit) ||
                        renderer.name == "ProductionDynamicAnalogInstruments" || renderer.name == "ProductionMagneticCompassV2";
                    bool transparentGlass = renderer.name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        renderer.sharedMaterials.Any(material => material != null && material.shader != null &&
                            (material.shader.name.IndexOf("Window Reflection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             material.shader.name.IndexOf("Instrument Glass", StringComparison.OrdinalIgnoreCase) >= 0));
                    renderer.enabled = renderer.enabled && cockpitOwned && !transparentGlass;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                var cameraObject = new GameObject("CockpitReflectionBakeCamera_EditorOnly");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                bakeCamera = cameraObject.AddComponent<Camera>();
                cameraObject.transform.SetPositionAndRotation(viewpoint.position, Quaternion.identity);
                bakeCamera.enabled = false;
                bakeCamera.clearFlags = CameraClearFlags.SolidColor;
                bakeCamera.backgroundColor = new Color(0.085f, 0.125f, 0.17f, 1f);
                bakeCamera.nearClipPlane = 0.015f;
                bakeCamera.farClipPlane = 12f;
                bakeCamera.allowHDR = true;
                bakeCamera.allowMSAA = false;
                bakeCamera.useOcclusionCulling = false;

                var lightObject = new GameObject("CockpitReflectionBakeSun_EditorOnly");
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                bakeSun = lightObject.AddComponent<Light>();
                bakeSun.type = LightType.Directional;
                bakeSun.color = new Color(1.0f, 0.93f, 0.84f, 1f);
                bakeSun.intensity = 0.82f;
                bakeSun.shadows = LightShadows.None;
                lightObject.transform.rotation = Quaternion.Euler(43f, -128f, 0f);

                capture = new Cubemap(128, TextureFormat.RGBAHalf, false)
                {
                    name = "C172_CockpitInterior_GeometryBake_TEMP",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                if (!bakeCamera.RenderToCubemap(capture))
                    throw new InvalidOperationException("Unity failed to render the offline cockpit cubemap.");
                return ConvertCubemapToEquirectangular(capture);
            }
            finally
            {
                if (capture != null) UnityEngine.Object.DestroyImmediate(capture);
                if (bakeSun != null) UnityEngine.Object.DestroyImmediate(bakeSun.gameObject);
                if (bakeCamera != null) UnityEngine.Object.DestroyImmediate(bakeCamera.gameObject);
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Color[] ConvertCubemapToEquirectangular(Cubemap cubemap)
        {
            Color[][] faces = Enum.GetValues(typeof(CubemapFace)).Cast<CubemapFace>()
                .Where(face => face != CubemapFace.Unknown)
                .Select(face => cubemap.GetPixels(face, 0)).ToArray();
            Color[] pixels = new Color[ReflectionEquirectangularWidth * ReflectionEquirectangularHeight];
            for (int y = 0; y < ReflectionEquirectangularHeight; y++)
            {
                float latitude = (((y + 0.5f) / ReflectionEquirectangularHeight) - 0.5f) * Mathf.PI;
                float cosLatitude = Mathf.Cos(latitude);
                for (int x = 0; x < ReflectionEquirectangularWidth; x++)
                {
                    float longitude = (((x + 0.5f) / ReflectionEquirectangularWidth) - 0.5f) * Mathf.PI * 2f;
                    Vector3 direction = new Vector3(
                        Mathf.Sin(longitude) * cosLatitude,
                        Mathf.Sin(latitude),
                        Mathf.Cos(longitude) * cosLatitude);
                    pixels[y * ReflectionEquirectangularWidth + x] =
                        SampleCubemapFaces(faces, cubemap.width, direction);
                }
            }
            return pixels;
        }

        private static Color SampleCubemapFaces(Color[][] faces, int size, Vector3 direction)
        {
            float ax = Mathf.Abs(direction.x);
            float ay = Mathf.Abs(direction.y);
            float az = Mathf.Abs(direction.z);
            CubemapFace face;
            float sc;
            float tc;
            if (ax >= ay && ax >= az)
            {
                if (direction.x >= 0f) { face = CubemapFace.PositiveX; sc = -direction.z / ax; tc = -direction.y / ax; }
                else { face = CubemapFace.NegativeX; sc = direction.z / ax; tc = -direction.y / ax; }
            }
            else if (ay >= ax && ay >= az)
            {
                if (direction.y >= 0f) { face = CubemapFace.PositiveY; sc = direction.x / ay; tc = direction.z / ay; }
                else { face = CubemapFace.NegativeY; sc = direction.x / ay; tc = -direction.z / ay; }
            }
            else
            {
                if (direction.z >= 0f) { face = CubemapFace.PositiveZ; sc = direction.x / az; tc = -direction.y / az; }
                else { face = CubemapFace.NegativeZ; sc = -direction.x / az; tc = -direction.y / az; }
            }
            float u = Mathf.Clamp01(sc * 0.5f + 0.5f);
            float v = Mathf.Clamp01(0.5f - tc * 0.5f);
            int px = Mathf.Clamp(Mathf.RoundToInt(u * (size - 1)), 0, size - 1);
            int py = Mathf.Clamp(Mathf.RoundToInt(v * (size - 1)), 0, size - 1);
            return faces[(int)face][py * size + px];
        }

        private static Texture2D ImportReadableTemporaryCopy(
            string sourcePath,
            string temporaryPath,
            bool sRgb,
            int maximumSize)
        {
            File.Copy(sourcePath, temporaryPath, true);
            AssetDatabase.ImportAsset(temporaryPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(temporaryPath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Temporary reflection source did not import: " + sourcePath);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = sRgb;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = maximumSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(temporaryPath);
            if (texture == null || !texture.isReadable)
                throw new InvalidOperationException("Temporary reflection source is not CPU-readable: " + sourcePath);
            return texture;
        }

        private static Color[] BuildReflectionPixels(
            Texture2D sky,
            Texture2D near,
            Texture2D mid,
            Texture2D far,
            bool airportLocal)
        {
            Color[] pixels = new Color[ReflectionEquirectangularWidth * ReflectionEquirectangularHeight];
            float proxyHeight = airportLocal ? LocalProxyHeightMeters : GlobalProxyHeightMeters;
            float maximumRadius = airportLocal ? LocalProxyMaximumRadiusMeters : GlobalProxyMaximumRadiusMeters;
            Vector2 center = airportLocal ? AirportReflectionCenter : Vector2.zero;
            for (int y = 0; y < ReflectionEquirectangularHeight; y++)
            {
                float v = (y + 0.5f) / ReflectionEquirectangularHeight;
                for (int x = 0; x < ReflectionEquirectangularWidth; x++)
                {
                    float u = (x + 0.5f) / ReflectionEquirectangularWidth;
                    Color color = sky.GetPixelBilinear(u, v);
                    if (v < 0.5f)
                    {
                        float elevation = (v - 0.5f) * Mathf.PI;
                        float downward = Mathf.Max(0.0001f, -Mathf.Sin(elevation));
                        float horizontal = Mathf.Max(0f, Mathf.Cos(elevation));
                        float distance = Mathf.Min(maximumRadius, proxyHeight * horizontal / downward);
                        float azimuth = (u - 0.5f) * Mathf.PI * 2f;
                        Vector2 world = center + new Vector2(
                            Mathf.Sin(azimuth), Mathf.Cos(azimuth)) * distance;
                        Color ground = airportLocal
                            ? SampleWorldOrthophoto(near, world, -2000f, 4000f)
                            : SampleGlobalOrthophoto(mid, far, world);
                        float radianceScale = airportLocal ? 0.46f : 0.40f;
                        ground = Color.Lerp(GroundRadiance * 0.30f, ground * radianceScale, 0.92f);
                        Color horizon = sky.GetPixelBilinear(u, 0.50390625f);
                        float seam = Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(0.006f, 0.045f, downward));
                        color = Color.Lerp(horizon, ground, seam);
                    }
                    color.r = Mathf.Max(0f, color.r);
                    color.g = Mathf.Max(0f, color.g);
                    color.b = Mathf.Max(0f, color.b);
                    color.a = 1f;
                    pixels[y * ReflectionEquirectangularWidth + x] = color;
                }
            }
            return pixels;
        }

        private static Color SampleGlobalOrthophoto(Texture2D mid, Texture2D far, Vector2 world)
        {
            Color midColor = SampleWorldOrthophoto(mid, world, -6000f, 12000f);
            Color farColor = SampleWorldOrthophoto(far, world, -12000f, 24000f);
            float radial = world.magnitude;
            float farWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4600f, 5800f, radial));
            return Color.Lerp(midColor, farColor, farWeight);
        }

        private static Color SampleWorldOrthophoto(
            Texture2D texture,
            Vector2 world,
            float worldMinimum,
            float worldSize)
        {
            float u = Mathf.Clamp01((world.x - worldMinimum) / worldSize);
            float v = Mathf.Clamp01((world.y - worldMinimum) / worldSize);
            return texture.GetPixelBilinear(u, v);
        }

        private static Texture2D CreateReflectionTexture(string name, Color[] pixels)
        {
            Texture2D output = new Texture2D(
                ReflectionEquirectangularWidth,
                ReflectionEquirectangularHeight,
                TextureFormat.RGBAHalf,
                false,
                true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            output.SetPixels(pixels);
            output.Apply(false, false);
            return output;
        }

        private static Cubemap ConfigureReflectionImport(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Generated ground-sky EXR did not import: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.TextureCube;
            importer.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
            TextureImporterSettings textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.cubemapConvolution = TextureImporterCubemapConvolution.Specular;
            importer.SetTextureSettings(textureSettings);
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 1;
            importer.maxTextureSize = PhotorealAerialReflectionProfile.RequiredCubemapFaceSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;

            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = PhotorealAerialReflectionProfile.RequiredCubemapFaceSize;
            android.format = TextureImporterFormat.ASTC_HDR_6x6;
            android.textureCompression = TextureImporterCompression.CompressedHQ;
            android.compressionQuality = 100;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();

            Cubemap cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (cubemap == null || cubemap.width != PhotorealAerialReflectionProfile.RequiredCubemapFaceSize)
                throw new InvalidOperationException("Generated cubemap is not 128 pixels per face: " + path);
            return cubemap;
        }

        private static float MeanAbsoluteDifference(Color[] left, Color[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                throw new ArgumentException("Reflection comparison arrays must have equal lengths.");
            double sum = 0d;
            int samples = ReflectionEquirectangularWidth * ReflectionEquirectangularHeight / 2;
            for (int index = 0; index < samples; index++)
            {
                Color a = left[index];
                Color b = right[index];
                sum += (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b)) / 3d;
            }
            return (float)(sum / samples);
        }

        private static void WriteManifest(ReflectionAssetPair reflections)
        {
            string globalHash = Sha256(GlobalReflectionPath);
            string localHash = Sha256(AirportLocalReflectionPath);
            StringBuilder json = new StringBuilder(8192);
            json.AppendLine("{");
            json.AppendLine("  \"schema\": \"questflight.photoreal_aerial_surface.reflection_manifest.v1\",");
            json.AppendLine("  \"status\": \"FOUNDATION_AUTHORED_NOT_VISUALLY_ACCEPTED\",");
            json.AppendLine("  \"shipping_target\": \"Standalone Quest 3 / Android ARM64 / Vulkan / OpenXR\",");
            json.AppendLine("  \"source\": {");
            AppendJsonString(json, "sky_asset", RepoPath(SourceSkyPath), true, 4);
            AppendJsonString(json, "sky_source_manifest",
                "QuestFlight/Assets/Production/Environment/Sky/SOURCE_MANIFEST.json", true, 4);
            AppendJsonString(json, "sky_license", "CC0-1.0", true, 4);
            AppendJsonString(json, "sky_sha256", Sha256(SourceSkyPath), true, 4);
            AppendJsonString(json, "orthophoto_source_manifest", RepoPath(SourceOrthophotoManifestPath), true, 4);
            AppendJsonString(json, "orthophoto_license", "CC0-1.0", true, 4);
            AppendJsonString(json, "near_orthophoto", RepoPath(SourceNearOrthophotoPath), true, 4);
            AppendJsonString(json, "near_orthophoto_sha256", Sha256(SourceNearOrthophotoPath), true, 4);
            AppendJsonString(json, "mid_orthophoto", RepoPath(SourceMidOrthophotoPath), true, 4);
            AppendJsonString(json, "mid_orthophoto_sha256", Sha256(SourceMidOrthophotoPath), true, 4);
            AppendJsonString(json, "far_orthophoto", RepoPath(SourceFarOrthophotoPath), true, 4);
            AppendJsonString(json, "far_orthophoto_sha256", Sha256(SourceFarOrthophotoPath), true, 4);
            AppendJsonString(json, "upper_hemisphere",
                "Exact bilinear sample of the committed HDR sky; no alternate sky or runtime capture.", true, 4);
            AppendJsonString(json, "lower_hemisphere",
                "bilinear_direction_to_ground_proxy over committed City AerialSurfaceV1 orthophotos", true, 4);
            AppendJsonString(json, "authoring_code", RepoPath(
                Root + "/Editor/PhotorealAerialReflectionAuthoring.cs"), false, 4);
            json.AppendLine("  },");
            json.AppendLine("  \"runtime_assets\": {");
            AppendReflectionManifestAsset(json, "global_ground_sky_reflection", GlobalReflectionPath,
                globalHash, reflections.GlobalStatistics, "mid+far", Vector2.zero,
                GlobalProxyHeightMeters, GlobalProxyMaximumRadiusMeters, true);
            AppendReflectionManifestAsset(json, "airport_local_reflection", AirportLocalReflectionPath,
                localHash, reflections.LocalStatistics, "near", AirportReflectionCenter,
                LocalProxyHeightMeters, LocalProxyMaximumRadiusMeters, true);
            AppendFileManifestAsset(json, "cockpit_interior_reflection",
                CockpitInteriorReflectionPath, true);
            AppendFileManifestAsset(json, "profile", ProfilePath, true);
            AppendFileManifestAsset(json, "window_material", WindowMaterialPath, true);
            AppendFileManifestAsset(json, "window_shader",
                Root + "/Cockpit/Shaders/ProductionCockpitWindowReflection.shader", true);
            AppendFileManifestAsset(json, "cockpit_stable_interior_shader",
                CockpitStableInteriorShaderPath, true);
            AppendFileManifestAsset(json, "instrument_glass_material", InstrumentGlassMaterialPath, true);
            AppendFileManifestAsset(json, "instrument_glass_shader",
                Root + "/Cockpit/Shaders/ProductionInstrumentGlassReflection.shader", true);
            AppendFileManifestAsset(json, "instrument_lens_mesh", InstrumentLensArrayMeshPath, true);
            AppendFileManifestAsset(json, "instrument_display_readout_mesh",
                InstrumentDisplayReadoutMeshPath, true);
            AppendFileManifestAsset(json, "instrument_display_readout_material",
                InstrumentDisplayReadoutMaterialPath, true);
            AppendFileManifestAsset(json, "instrument_display_readout_shader",
                InstrumentDisplayReadoutShaderPath, true);
            AppendFileManifestAsset(json, "instrument_display_base_color",
                InstrumentDisplayBaseColorPath, true);
            AppendFileManifestAsset(json, "instrument_display_emission",
                InstrumentDisplayEmissionPath, true);
            AppendFileManifestAsset(json, "cockpit_local_shadow_depth_shader",
                CockpitLocalShadowAuthoring.ShadowDepthShaderPath, true);
            AppendFileManifestAsset(json, "cockpit_local_shadow_proxy",
                CockpitLocalShadowAuthoring.ShadowCasterMeshPath, true);
            AppendFileManifestAsset(json, "cockpit_local_shadow_map",
                CockpitLocalShadowAuthoring.ShadowMapPath, false);
            json.AppendLine("  },");
            json.AppendLine("  \"integration_contract\": {");
            AppendJsonString(json, "global_reflection_mode",
                "Custom static imagery-aware global cubemap", true, 4);
            AppendJsonString(json, "airport_probe_texture",
                RepoPath(AirportLocalReflectionPath), true, 4);
            json.AppendLine("    \"airport_probe_count\": 1,");
            AppendJsonString(json, "airport_probe_mode",
                "Custom distinct static texture, ViaScripting refresh mode, culling mask zero", true, 4);
            json.AppendLine("    \"runtime_probe_render_count\": 0,");
            json.AppendLine("    \"screen_space_reflections\": false,");
            json.AppendLine("    \"reflection_camera\": false,");
            json.AppendLine("    \"refraction_camera\": false,");
            AppendJsonString(json, "aircraft_probe_anchor",
                "AircraftSimulationRoot/CenterOfGravityReference", true, 4);
            AppendJsonString(json, "aircraft_probe_usage", "BlendProbesAndSkybox", true, 4);
            AppendJsonString(json, "instrument_surface_mode",
                "Imported Meter_01 PBR plus one 13-aperture additive lens mesh; one opaque 12-triangle avionics readout mesh; source display/text retain imported PBR",
                false, 4);
            json.AppendLine("  },");
            json.AppendLine("  \"android_budget\": {");
            json.AppendLine("    \"new_reflection_probes\": 1,");
            json.AppendLine("    \"new_runtime_probe_cameras\": 0,");
            json.AppendLine("    \"new_runtime_probe_updates\": 0,");
            json.AppendLine("    \"new_renderers\": 2,");
            json.AppendLine("    \"new_draw_calls\": 2,");
            json.AppendLine("    \"new_materials\": 2,");
            json.AppendLine("    \"disabled_duplicate_exterior_glass_renderers\": 1,");
            json.AppendLine("    \"static_cubemap_count\": 2,");
            json.AppendLine("    \"estimated_combined_cubemap_bytes\": 126144,");
            json.AppendLine("    \"cockpit_local_shadow_offscreen_draw_calls\": 1,");
            json.AppendLine("    \"estimated_cockpit_local_shadow_color_and_depth_bytes\": 2097152");
            json.AppendLine("  },");
            json.AppendLine("  \"cockpit_directional_shading\": {");
            json.AppendLine("    \"enabled_by_default\": true,");
            AppendJsonString(json, "representation",
                "geometry-derived four-cardinal soft visibility in additional vertex streams", true, 4);
            AppendJsonString(json, "visibility_manifest",
                RepoPath(CockpitDirectionalVisibilityAuthoring.ManifestPath), true, 4);
            json.AppendLine("    \"strength\": 0.58,");
            json.AppendLine("    \"additional_texture_lookups\": 0,");
            json.AppendLine("    \"additional_draw_calls\": 0,");
            json.AppendLine("    \"shadow_map\": false,");
            json.AppendLine("    \"projector_geometry\": false,");
            json.AppendLine("    \"physical_acceptance_pending\": true");
            json.AppendLine("  },");
            json.AppendLine("  \"cockpit_local_shadow\": {");
            json.AppendLine("    \"enabled_by_default\": true,");
            AppendJsonString(json, "representation",
                "one authored aircraft-local orthographic filtered two-moment depth map and one low-poly cabin-frame proxy",
                true, 4);
            json.AppendLine($"    \"map_resolution\": {CockpitLocalShadowAuthoring.ShadowMapResolution},");
            json.AppendLine("    \"map_format\": \"R16G16_SFloat\",");
            json.AppendLine("    \"proxy_triangles\": 120,");
            json.AppendLine("    \"proxy_triangle_ceiling\": 1000,");
            json.AppendLine("    \"offscreen_draw_calls\": 1,");
            json.AppendLine("    \"unity_realtime_shadow_cascades\": 0,");
            json.AppendLine("    \"additional_lights\": 0,");
            json.AppendLine("    \"transparent_overlays\": 0,");
            json.AppendLine("    \"source\": \"project-authored deterministic geometry and shaders\",");
            json.AppendLine("    \"license\": \"Project-owned\",");
            json.AppendLine("    \"physical_acceptance_pending\": true");
            json.AppendLine("  },");
            json.AppendLine("  \"cockpit_shadow_candidate\": {");
            json.AppendLine("    \"enabled_by_default\": false,");
            json.AppendLine("    \"accepted_on_quest_hardware\": false,");
            AppendJsonString(json, "selection",
                "Editor-only scoped A/B; exact prior state is restored on dispose", true, 4);
            json.AppendLine("    \"map_resolution\": 1024,");
            json.AppendLine("    \"cascade_count\": 0,");
            json.AppendLine("    \"distance_meters\": 14.0");
            json.AppendLine("  },");
            AppendJsonString(json, "acceptance_boundary",
                "Assets-only authoring modifies no product scene or prefab. Lead integration, inspected stereo Visual QA, Android build validation, and physical Quest frame/thermal evidence remain required.", false, 2);
            json.AppendLine("}");
            WriteIfChanged(ManifestPath, Encoding.UTF8.GetBytes(json.ToString().Replace("\r\n", "\n")));
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void AppendReflectionManifestAsset(
            StringBuilder json,
            string id,
            string path,
            string hash,
            ReflectionStatistics statistics,
            string groundSources,
            Vector2 center,
            float proxyHeight,
            float maximumRadius,
            bool trailingComma)
        {
            json.Append("    \"").Append(id).AppendLine("\": {");
            AppendJsonString(json, "path", RepoPath(path), true, 6);
            json.AppendLine("      \"equirectangular_pixels\": [256, 128],");
            json.AppendLine("      \"cubemap_face_pixels\": 128,");
            AppendJsonString(json, "convolution", "Specular", true, 6);
            AppendJsonString(json, "android_format", "ASTC_HDR_6x6", true, 6);
            json.AppendLine("      \"mipmaps\": true,");
            AppendJsonString(json, "ground_sources", groundSources, true, 6);
            AppendJsonString(json, "sampling", "bilinear_direction_to_ground_proxy", true, 6);
            json.Append("      \"proxy_center_xz_meters\": [")
                .Append(JsonNumber(center.x)).Append(", ").Append(JsonNumber(center.y)).AppendLine("],");
            json.Append("      \"proxy_height_meters\": ").Append(JsonNumber(proxyHeight)).AppendLine(",");
            json.Append("      \"maximum_radius_meters\": ").Append(JsonNumber(maximumRadius)).AppendLine(",");
            json.Append("      \"lower_hemisphere_rgb_mean_linear\": [")
                .Append(JsonNumber(statistics.Mean.r)).Append(", ")
                .Append(JsonNumber(statistics.Mean.g)).Append(", ")
                .Append(JsonNumber(statistics.Mean.b)).AppendLine("],");
            json.Append("      \"lower_hemisphere_rgb_standard_deviation_linear\": [")
                .Append(JsonNumber(statistics.StandardDeviation.r)).Append(", ")
                .Append(JsonNumber(statistics.StandardDeviation.g)).Append(", ")
                .Append(JsonNumber(statistics.StandardDeviation.b)).AppendLine("],");
            json.Append("      \"lower_hemisphere_luminance_standard_deviation_linear\": ")
                .Append(JsonNumber(statistics.LuminanceStandardDeviation)).AppendLine(",");
            AppendJsonString(json, "sha256", hash, true, 6);
            json.Append("      \"bytes\": ").Append(new FileInfo(path).Length).AppendLine();
            json.Append("    }").AppendLine(trailingComma ? "," : string.Empty);
        }

        private static void AppendFileManifestAsset(
            StringBuilder json,
            string id,
            string path,
            bool trailingComma)
        {
            json.Append("    \"").Append(id).AppendLine("\": {");
            AppendJsonString(json, "path", RepoPath(path), true, 6);
            AppendJsonString(json, "sha256", Sha256(path), File.Exists(path), 6);
            if (File.Exists(path))
                json.Append("      \"bytes\": ").Append(new FileInfo(path).Length).AppendLine();
            json.Append("    }").AppendLine(trailingComma ? "," : string.Empty);
        }

        private static void AppendJsonString(
            StringBuilder json,
            string key,
            string value,
            bool trailingComma,
            int indentation)
        {
            json.Append(' ', indentation).Append('"').Append(key).Append("\": \"")
                .Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""))
                .Append('"').AppendLine(trailingComma ? "," : string.Empty);
        }

        private static string RepoPath(string projectPath) => "QuestFlight/" + projectPath;

        private static string JsonNumber(float value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);

        private static string Sha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 algorithm = SHA256.Create())
                return string.Concat(algorithm.ComputeHash(stream).Select(item => item.ToString("x2")));
        }

        private static void EnsureMaterials()
        {
            Shader windowShader = Shader.Find(WindowShaderName);
            Shader instrumentShader = Shader.Find(InstrumentGlassShaderName);
            Shader stableInteriorShader = Shader.Find(CockpitStableInteriorShaderName);
            if (windowShader == null || instrumentShader == null || stableInteriorShader == null)
                throw new InvalidOperationException("Reflection shaders did not import before material authoring.");

            Material window = LoadOrCreateMaterial(WindowMaterialPath, windowShader,
                "ProductionCockpitWindowReflection");
            window.SetTexture("_AuthoredReflectionCube", LoadRequired<Cubemap>(GlobalReflectionPath));
            window.SetTexture("_CockpitInteriorReflectionCube",
                LoadRequired<Cubemap>(CockpitInteriorReflectionPath));
            window.SetColor("_WindshieldTint", new Color(0.77f, 0.86f, 0.95f, 1f));
            window.SetColor("_SideWindowTint", new Color(0.70f, 0.82f, 0.92f, 1f));
            window.SetFloat("_WindshieldOpacity", 0.075f);
            window.SetFloat("_SideWindowOpacity", 0.09f);
            window.SetFloat("_FresnelOpacity", 0.11f);
            window.SetFloat("_ReflectionStrength", 0.46f);
            window.SetFloat("_InteriorReflectionStrength", 0.90f);
            window.SetFloat("_InteriorSkyLeak", 0.08f);
            window.SetFloat("_DirectGlintStrength", 0.065f);
            window.SetFloat("_PerceptualRoughness", 0.22f);
            window.SetFloat("_SideAxisStart", 0.52f);
            window.SetFloat("_SideAxisEnd", 0.82f);
            ComputeCockpitReflectionContract(
                out Matrix4x4 windowLocalToAircraft,
                out Matrix4x4 instrumentLocalToAircraft,
                out Vector3 capturePosition,
                out Vector3 boxMinimum,
                out Vector3 boxMaximum);
            SetSerializedTransformRows(window, "_WindowToAircraft", windowLocalToAircraft);
            window.SetVector("_CockpitReflectionCapturePosition", capturePosition);
            window.SetVector("_CockpitReflectionBoxMin", boxMinimum);
            window.SetVector("_CockpitReflectionBoxMax", boxMaximum);
            window.renderQueue = (int)RenderQueue.Transparent + 12;
            window.enableInstancing = false;
            window.doubleSidedGI = false;
            EditorUtility.SetDirty(window);

            Material instrument = LoadOrCreateMaterial(InstrumentGlassMaterialPath, instrumentShader,
                "ProductionInstrumentGlassReflection");
            instrument.SetTexture("_AuthoredReflectionCube",
                LoadRequired<Cubemap>(CockpitInteriorReflectionPath));
            instrument.SetColor("_Tint", new Color(0.90f, 0.94f, 0.98f, 1f));
            instrument.SetFloat("_ReflectionStrength", 0.46f);
            instrument.SetFloat("_DirectGlintStrength", 0.12f);
            instrument.SetFloat("_PerceptualRoughness", 0.22f);
            SetSerializedTransformRows(instrument, "_InstrumentToAircraft", instrumentLocalToAircraft);
            instrument.renderQueue = (int)RenderQueue.Transparent + 16;
            instrument.enableInstancing = false;
            instrument.doubleSidedGI = false;
            EditorUtility.SetDirty(instrument);

            Shader displayShader = Shader.Find(InstrumentDisplayReadoutShaderName);
            if (displayShader == null)
                throw new InvalidOperationException("Instrument display readout shader is missing.");
            Material displayReadout = LoadOrCreateMaterial(
                InstrumentDisplayReadoutMaterialPath, displayShader,
                "ProductionInstrumentDisplayReadout");
            displayReadout.SetTexture("_MainTex",
                LoadRequired<Texture2D>(InstrumentDisplayBaseColorPath));
            if (displayReadout.HasProperty("_Color"))
                displayReadout.SetColor("_Color", Color.white);
            displayReadout.renderQueue = (int)RenderQueue.Geometry + 4;
            displayReadout.enableInstancing = false;
            displayReadout.doubleSidedGI = false;
            EditorUtility.SetDirty(displayReadout);

        }

        private static void EnsureInstrumentDisplayTextures()
        {
            const int width = 512;
            const int height = 512;
            var baseOutput = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            var emissionOutput = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            try
            {
                Color32[] basePixels = Enumerable.Repeat(
                    new Color32(3, 7, 5, 255), width * height).ToArray();
                Color32[] emissionPixels = Enumerable.Repeat(
                    new Color32(0, 0, 0, 255), width * height).ToArray();
                Color32 readableGreen = new Color32(133, 207, 113, 255);
                Color32 emissiveGreen = new Color32(76, 210, 72, 255);
                Color32 readableAmber = new Color32(228, 164, 76, 255);
                Color32 emissiveAmber = new Color32(235, 118, 28, 255);

                // A dedicated, normalized atlas removes the imported glTF atlas/transform
                // ambiguity. GPS occupies V 0-.5, radios .5-.75, and transponder .75-1.
                // Only the twelve physical screen triangles sample this texture.
                DrawDisplayText(basePixels, emissionPixels, width, height,
                    84, 174, 5, "KBDU RWY 08", readableGreen, emissiveGreen);
                DrawDisplayText(basePixels, emissionPixels, width, height,
                    132, 108, 5, "ALT 5430", readableGreen, emissiveGreen);
                DrawDisplayText(basePixels, emissionPixels, width, height,
                    144, 42, 5, "HDG 090", readableAmber, emissiveAmber);
                DrawDisplayText(basePixels, emissionPixels, width, height,
                    112, 298, 8, "122.80", readableGreen, emissiveGreen);
                DrawDisplayText(basePixels, emissionPixels, width, height,
                    94, 426, 6, "XPDR 1200", readableAmber, emissiveAmber);

                baseOutput.SetPixels32(basePixels);
                emissionOutput.SetPixels32(emissionPixels);
                baseOutput.Apply(false, false);
                emissionOutput.Apply(false, false);
                WriteIfChanged(InstrumentDisplayBaseColorPath, baseOutput.EncodeToPNG());
                WriteIfChanged(InstrumentDisplayEmissionPath, emissionOutput.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baseOutput);
                UnityEngine.Object.DestroyImmediate(emissionOutput);
            }

            ConfigureInstrumentDisplayTexture(InstrumentDisplayBaseColorPath);
            ConfigureInstrumentDisplayTexture(InstrumentDisplayEmissionPath);
        }

        private static Texture2D ReadTexturePixels(Texture source)
        {
            RenderTexture temporary = RenderTexture.GetTemporary(
                source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
                readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                readable.Apply(false, false);
                return readable;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static void ConfigureInstrumentDisplayTexture(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Generated display texture did not import: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 1;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.textureCompression = TextureImporterCompression.CompressedHQ;
            android.compressionQuality = 100;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static void DrawDisplayText(
            Color32[] basePixels,
            Color32[] emissionPixels,
            int width,
            int height,
            int originX,
            int originY,
            int scale,
            string text,
            Color32 baseColor,
            Color32 emissionColor)
        {
            int cursor = originX;
            foreach (char character in text)
            {
                byte[] rows = GlyphRows(character);
                for (int row = 0; row < 7; row++)
                {
                    for (int column = 0; column < 5; column++)
                    {
                        if ((rows[row] & (1 << (4 - column))) == 0) continue;
                        for (int py = 0; py < scale; py++)
                        for (int px = 0; px < scale; px++)
                        {
                            int x = cursor + column * scale + px;
                            int y = originY + (6 - row) * scale + py;
                            if (x < 0 || x >= width || y < 0 || y >= height) continue;
                            int index = y * width + x;
                            basePixels[index] = baseColor;
                            emissionPixels[index] = emissionColor;
                        }
                    }
                }
                cursor += 6 * scale;
            }
        }

        private static byte[] GlyphRows(char character)
        {
            switch (char.ToUpperInvariant(character))
            {
                case '0': return new byte[] { 14, 17, 19, 21, 25, 17, 14 };
                case '1': return new byte[] { 4, 12, 4, 4, 4, 4, 14 };
                case '2': return new byte[] { 14, 17, 1, 2, 4, 8, 31 };
                case '3': return new byte[] { 30, 1, 1, 14, 1, 1, 30 };
                case '4': return new byte[] { 2, 6, 10, 18, 31, 2, 2 };
                case '5': return new byte[] { 31, 16, 16, 30, 1, 1, 30 };
                case '6': return new byte[] { 14, 16, 16, 30, 17, 17, 14 };
                case '7': return new byte[] { 31, 1, 2, 4, 8, 8, 8 };
                case '8': return new byte[] { 14, 17, 17, 14, 17, 17, 14 };
                case '9': return new byte[] { 14, 17, 17, 15, 1, 1, 14 };
                case 'A': return new byte[] { 14, 17, 17, 31, 17, 17, 17 };
                case 'B': return new byte[] { 30, 17, 17, 30, 17, 17, 30 };
                case 'C': return new byte[] { 14, 17, 16, 16, 16, 17, 14 };
                case 'D': return new byte[] { 30, 17, 17, 17, 17, 17, 30 };
                case 'E': return new byte[] { 31, 16, 16, 30, 16, 16, 31 };
                case 'G': return new byte[] { 14, 17, 16, 23, 17, 17, 15 };
                case 'H': return new byte[] { 17, 17, 17, 31, 17, 17, 17 };
                case 'K': return new byte[] { 17, 18, 20, 24, 20, 18, 17 };
                case 'L': return new byte[] { 16, 16, 16, 16, 16, 16, 31 };
                case 'M': return new byte[] { 17, 27, 21, 21, 17, 17, 17 };
                case 'N': return new byte[] { 17, 25, 21, 19, 17, 17, 17 };
                case 'O': return new byte[] { 14, 17, 17, 17, 17, 17, 14 };
                case 'P': return new byte[] { 30, 17, 17, 30, 16, 16, 16 };
                case 'R': return new byte[] { 30, 17, 17, 30, 20, 18, 17 };
                case 'T': return new byte[] { 31, 4, 4, 4, 4, 4, 4 };
                case 'U': return new byte[] { 17, 17, 17, 17, 17, 17, 14 };
                case 'V': return new byte[] { 17, 17, 17, 17, 17, 10, 4 };
                case 'W': return new byte[] { 17, 17, 17, 21, 21, 21, 10 };
                case 'X': return new byte[] { 17, 17, 10, 4, 10, 17, 17 };
                case 'Y': return new byte[] { 17, 17, 10, 4, 4, 4, 4 };
                case '.': return new byte[] { 0, 0, 0, 0, 0, 12, 12 };
                default: return new byte[] { 0, 0, 0, 0, 0, 0, 0 };
            }
        }

        private static void SetSerializedTransformRows(
            Material material, string propertyPrefix, Matrix4x4 matrix)
        {
            material.SetVector(propertyPrefix + "Row0", matrix.GetRow(0));
            material.SetVector(propertyPrefix + "Row1", matrix.GetRow(1));
            material.SetVector(propertyPrefix + "Row2", matrix.GetRow(2));
        }

        private static void ComputeCockpitReflectionContract(
            out Matrix4x4 windowLocalToAircraft,
            out Matrix4x4 instrumentLocalToAircraft,
            out Vector3 capturePosition,
            out Vector3 boxMinimum,
            out Vector3 boxMaximum)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                AircraftReferenceFrameRig rig = root.GetComponent<AircraftReferenceFrameRig>();
                if (rig == null || rig.AircraftVisualRoot == null)
                    throw new InvalidOperationException("Aircraft visual root is missing from reflection contract.");
                Transform visual = rig.AircraftVisualRoot;
                Transform cockpit = FindDescendant(visual, CockpitRootName);
                Transform viewpoint = FindDescendant(root.transform, "Main Camera");
                Renderer window = visual.GetComponentsInChildren<Renderer>(true).Single(renderer =>
                    string.Equals(renderer.name, ExteriorWindowRendererName, StringComparison.Ordinal));
                if (cockpit == null || viewpoint == null)
                    throw new InvalidOperationException("Cockpit or authored viewpoint is missing from reflection contract.");

                windowLocalToAircraft = visual.worldToLocalMatrix * window.transform.localToWorldMatrix;
                // The generated lens array is serialized directly under the aircraft visual
                // root with an identity transform, so its object space is aircraft space.
                instrumentLocalToAircraft = Matrix4x4.identity;
                capturePosition = visual.InverseTransformPoint(viewpoint.position);
                bool initialized = false;
                boxMinimum = Vector3.zero;
                boxMaximum = Vector3.zero;
                foreach (Renderer renderer in cockpit.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    Bounds bounds = renderer.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 world = bounds.center + new Vector3(
                            (corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                            (corner & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                            (corner & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                        Vector3 local = visual.InverseTransformPoint(world);
                        if (!initialized)
                        {
                            boxMinimum = boxMaximum = local;
                            initialized = true;
                        }
                        else
                        {
                            boxMinimum = Vector3.Min(boxMinimum, local);
                            boxMaximum = Vector3.Max(boxMaximum, local);
                        }
                    }
                }
                if (!initialized)
                    throw new InvalidOperationException("No opaque cockpit bounds exist for reflection projection.");
                Vector3 padding = Vector3.one * 0.06f;
                boxMinimum -= padding;
                boxMaximum += padding;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsureProfile(Cubemap globalCubemap, Cubemap airportLocalCubemap)
        {
            PhotorealAerialReflectionProfile profile =
                AssetDatabase.LoadAssetAtPath<PhotorealAerialReflectionProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PhotorealAerialReflectionProfile>();
                profile.name = "KBDU_PhotorealAerialReflectionProfile";
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            SerializedObject serialized = new SerializedObject(profile);
            SetString(serialized, "schema", PhotorealAerialReflectionProfile.Schema);
            SetString(serialized, "status", "FOUNDATION_AUTHORED_NOT_VISUALLY_ACCEPTED");
            SetObject(serialized, "globalGroundSkyReflection", globalCubemap);
            SetObject(serialized, "airportLocalReflection", airportLocalCubemap);
            SetColor(serialized, "lowerHemisphereGroundColor", GroundRadiance);
            SetFloat(serialized, "globalReflectionIntensity", 0.68f);
            SetVector3(serialized, "airportProbeLocalPosition", new Vector3(-385f, 5f, -260f));
            SetVector3(serialized, "airportProbeSize", new Vector3(260f, 35f, 240f));
            SetFloat(serialized, "airportProbeBlendDistance", 24f);
            SetFloat(serialized, "airportProbeIntensity", 0.72f);
            SetInt(serialized, "airportProbeResolution", 128);
            SetBool(serialized, "airportProbeBoxProjection", false);
            SetBool(serialized, "runtimeProbeUpdates", false);
            SetBool(serialized, "cockpitSunShadowCandidate.enabledByDefault", false);
            SetBool(serialized, "cockpitSunShadowCandidate.acceptedOnQuestHardware", false);
            SetInt(serialized, "cockpitSunShadowCandidate.casterScope",
                (int)CockpitSunShadowCasterScope.CockpitInteriorOnly);
            SetInt(serialized, "cockpitSunShadowCandidate.mapResolution", 1024);
            SetInt(serialized, "cockpitSunShadowCandidate.cascadeCount", 0);
            SetFloat(serialized, "cockpitSunShadowCandidate.distanceMeters", 14f);
            SetFloat(serialized, "cockpitSunShadowCandidate.strength", 0.55f);
            SetFloat(serialized, "cockpitSunShadowCandidate.bias", 0.06f);
            SetFloat(serialized, "cockpitSunShadowCandidate.normalBias", 0.30f);
            SetFloat(serialized, "cockpitSunShadowCandidate.nearPlaneMeters", 0.20f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }

        private static void IntegrateEnvironmentPrefab(PhotorealAerialReflectionProfile profile)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(EnvironmentPrefabPath);
            try
            {
                ProductionEnvironmentRoot environment = root.GetComponent<ProductionEnvironmentRoot>();
                if (environment == null)
                    throw new InvalidOperationException("ProductionEnvironmentRoot contract is missing.");

                Transform foundation = EnsureUniqueDirectChild(root.transform, FoundationObjectName);
                foundation.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                foundation.localScale = Vector3.one;
                Transform probeTransform = EnsureUniqueDirectChild(foundation, AirportProbeObjectName);
                probeTransform.SetLocalPositionAndRotation(profile.AirportProbeLocalPosition, Quaternion.identity);
                probeTransform.localScale = Vector3.one;
                ReflectionProbe probe = probeTransform.GetComponent<ReflectionProbe>();
                if (probe == null) probe = probeTransform.gameObject.AddComponent<ReflectionProbe>();
                foreach (ReflectionProbe duplicate in probeTransform.GetComponents<ReflectionProbe>().Skip(1).ToArray())
                    UnityEngine.Object.DestroyImmediate(duplicate);
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = profile.AirportLocalReflection;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                probe.cullingMask = 0;
                probe.resolution = profile.AirportProbeResolution;
                probe.size = profile.AirportProbeSize;
                probe.center = Vector3.zero;
                probe.blendDistance = profile.AirportProbeBlendDistance;
                probe.intensity = profile.AirportProbeIntensity;
                probe.importance = 2;
                probe.boxProjection = profile.AirportProbeBoxProjection;
                probe.hdr = true;
                probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
                probe.backgroundColor = profile.LowerHemisphereGroundColor;
                GameObjectUtility.SetStaticEditorFlags(
                    probeTransform.gameObject, StaticEditorFlags.ReflectionProbeStatic);

                AirportCoreBenchmarkRoot benchmark = root.GetComponentInChildren<AirportCoreBenchmarkRoot>(true);
                if (benchmark == null)
                    throw new InvalidOperationException("AirportCoreBenchmarkRoot is missing.");
                int heroConsumers = 0;
                heroConsumers += ConfigureHeroConsumers(benchmark.executiveTerminal);
                heroConsumers += ConfigureHeroConsumers(benchmark.type2HangarRow);
                heroConsumers += ConfigureHeroConsumers(benchmark.fuelServiceContext);
                heroConsumers += ConfigureHeroConsumers(benchmark.authoredApronProps);
                if (benchmark.vegetationHeroCluster != null)
                {
                    foreach (Renderer renderer in benchmark.vegetationHeroCluster.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        renderer.probeAnchor = null;
                        EditorUtility.SetDirty(renderer);
                    }
                }
                if (heroConsumers == 0)
                    throw new InvalidOperationException("No existing airport hero renderer accepted the bounded probe.");
                if (foundation.GetComponentsInChildren<Camera>(true).Length != 0)
                    throw new InvalidOperationException("Reflection foundation may not contain a camera.");
                PrefabUtility.SaveAsPrefabAsset(root, EnvironmentPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void IntegrateAircraftPrefab()
        {
            Material sourceDisplayCover = LoadRequired<Material>(SourceWindowMaterialPath);
            Material sourceInstrument = LoadRequired<Material>(SourceInstrumentGlassMaterialPath);
            Material window = LoadRequired<Material>(WindowMaterialPath);
            Material instrument = LoadRequired<Material>(InstrumentGlassMaterialPath);
            Material displayReadout = LoadRequired<Material>(InstrumentDisplayReadoutMaterialPath);
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                AircraftReferenceFrameRig rig = root.GetComponent<AircraftReferenceFrameRig>();
                if (rig == null || rig.CenterOfGravityReference == null || rig.AircraftVisualRoot == null)
                    throw new InvalidOperationException("Authored aircraft CG/visual references are missing.");
                QuestFlightLab.Aircraft.ProductionAircraftLightingController lighting =
                    root.GetComponentInChildren<QuestFlightLab.Aircraft.ProductionAircraftLightingController>(true);
                if (lighting == null)
                    throw new InvalidOperationException("Authored aircraft lighting controller is missing.");
                SerializedObject lightingSettings = new SerializedObject(lighting);
                SetObject(lightingSettings, "stableCockpitShader",
                    LoadRequired<Shader>(CockpitStableInteriorShaderPath));
                SetObject(lightingSettings, "instrumentDisplayBaseColor",
                    LoadRequired<Texture2D>(InstrumentDisplayBaseColorPath));
                SetObject(lightingSettings, "instrumentDisplayEmission",
                    LoadRequired<Texture2D>(InstrumentDisplayEmissionPath));
                SetFloat(lightingSettings, "staticDepthStrength",
                    QuestFlightLab.Aircraft.ProductionAircraftLightingController.DefaultStaticDepthStrength);
                SetFloat(lightingSettings, "directionalCabinShadowStrength",
                    QuestFlightLab.Aircraft.ProductionAircraftLightingController.DefaultDirectionalCabinShadowStrength);
                lightingSettings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(lighting);
                if (rig.CenterOfGravityReference.parent != rig.transform ||
                    rig.CenterOfGravityReference.localPosition.sqrMagnitude > 0.000001f)
                    throw new InvalidOperationException("Reflection anchor must be the existing aircraft CG reference.");
                Transform cockpit = FindDescendant(rig.AircraftVisualRoot, CockpitRootName);
                Transform viewpoint = FindDescendant(root.transform, "Main Camera");
                if (cockpit == null || viewpoint == null)
                    throw new InvalidOperationException("Authored cockpit or viewpoint is missing.");
                foreach (Transform item in cockpit.GetComponentsInChildren<Transform>(true))
                    item.gameObject.layer = 0;

                Transform rejectedShadow = FindDescendant(
                    rig.AircraftVisualRoot, RejectedCockpitSunOcclusionObjectName);
                if (rejectedShadow != null)
                    UnityEngine.Object.DestroyImmediate(rejectedShadow.gameObject);

                Transform rejectedGeneratedCover = FindDescendant(
                    rig.AircraftVisualRoot, RejectedGeneratedInstrumentCoverName);
                if (rejectedGeneratedCover != null)
                    UnityEngine.Object.DestroyImmediate(rejectedGeneratedCover.gameObject);

                Renderer meterRenderer = rig.AircraftVisualRoot
                    .GetComponentsInChildren<Renderer>(true).Single(renderer => string.Equals(
                        renderer.name, InstrumentMeterRendererName, StringComparison.Ordinal));
                Renderer sourceDisplayRenderer = rig.AircraftVisualRoot
                    .GetComponentsInChildren<Renderer>(true).Single(renderer => string.Equals(
                        renderer.name, InstrumentDisplayRendererName, StringComparison.Ordinal));
                Renderer instrumentLensArray = BuildInstrumentLensArray(
                    rig.AircraftVisualRoot, meterRenderer, viewpoint, instrument);
                Renderer instrumentDisplayReadouts = BuildInstrumentDisplayReadouts(
                    rig.AircraftVisualRoot, sourceDisplayRenderer, viewpoint,
                    LoadRequired<Material>(InstrumentDisplayReadoutMaterialPath));
                Matrix4x4 instrumentToAircraft = rig.AircraftVisualRoot.worldToLocalMatrix *
                                                instrumentLensArray.transform.localToWorldMatrix;
                SetSerializedTransformRows(instrument, "_InstrumentToAircraft", instrumentToAircraft);
                EditorUtility.SetDirty(instrument);

                int windowSlots = 0;
                int exteriorWindowRenderers = 0;
                int instrumentLensSlots = 0;
                Renderer[] renderers = rig.AircraftVisualRoot.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    renderer.probeAnchor = rig.CenterOfGravityReference;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    Material[] materials = renderer.sharedMaterials;
                    bool exteriorWindow = string.Equals(renderer.name, ExteriorWindowRendererName,
                        StringComparison.Ordinal);
                    bool authoritativeInstrumentLens = string.Equals(renderer.name,
                        InstrumentLensArrayName, StringComparison.Ordinal);
                    bool decorativeInstrumentCover = string.Equals(renderer.name,
                        DecorativeInstrumentCoverRendererName, StringComparison.Ordinal);
                    bool misidentifiedInstrumentGlass = string.Equals(renderer.name,
                        MisidentifiedInstrumentGlassRendererName, StringComparison.Ordinal);
                    for (int index = 0; index < materials.Length; index++)
                    {
                        if (exteriorWindow)
                        {
                            materials[index] = window;
                            windowSlots++;
                        }
                        else if (authoritativeInstrumentLens)
                        {
                            materials[index] = instrument;
                            instrumentLensSlots++;
                        }
                        else if (decorativeInstrumentCover)
                            materials[index] = sourceDisplayCover;
                        else if (misidentifiedInstrumentGlass)
                            materials[index] = sourceInstrument;
                        else if (materials[index] == window)
                        {
                            windowSlots++;
                        }
                        else if (materials[index] == instrument)
                            materials[index] = sourceInstrument;
                    }
                    renderer.sharedMaterials = materials;
                    if (decorativeInstrumentCover || misidentifiedInstrumentGlass)
                    {
                        renderer.enabled = false;
                    }
                    if (authoritativeInstrumentLens)
                        renderer.enabled = true;
                    if (exteriorWindow)
                    {
                        renderer.enabled = true;
                        Mesh windowMesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if (windowMesh == null || windowMesh.bounds.size.x < 1.0f ||
                            windowMesh.bounds.size.y < 0.5f || windowMesh.bounds.size.z < 2.0f)
                            throw new InvalidOperationException(
                                "The authoritative exterior cockpit glazing mesh has implausible bounds.");
                        exteriorWindowRenderers++;
                    }
                    // The worn candidate proved that a tiny cockpit-only realtime
                    // shadow map creates unstable overlapping triangle silhouettes in
                    // stereo. Cockpit depth is therefore static/material-authored; no
                    // cockpit renderer participates in realtime shadowing.
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    EditorUtility.SetDirty(renderer);
                }

                QuestFlightLab.Aircraft.AircraftVisibilityProfileController visibility =
                    root.GetComponentInChildren<QuestFlightLab.Aircraft.AircraftVisibilityProfileController>(true);
                if (visibility == null)
                    throw new InvalidOperationException("Authored aircraft visibility profile is missing.");
                SerializedObject visibilitySettings = new SerializedObject(visibility);
                Renderer exteriorWindowRenderer = renderers.Single(renderer =>
                    string.Equals(renderer.name, ExteriorWindowRendererName, StringComparison.Ordinal));
                RewriteRendererArray(visibilitySettings, "cockpitOccludingExteriorRenderers",
                    renderer => renderer != exteriorWindowRenderer, null);
                RewriteRendererArray(visibilitySettings, "interiorRenderers",
                    renderer => true, instrumentLensArray);
                RewriteRendererArray(visibilitySettings, "interiorRenderers",
                    renderer => true, instrumentDisplayReadouts);
                visibilitySettings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(visibility);
                if (windowSlots != 1 || exteriorWindowRenderers != 1 || instrumentLensSlots != 1 ||
                    instrumentLensArray.sharedMaterial != instrument || !instrumentDisplayReadouts.enabled)
                    throw new InvalidOperationException(
                        "Expected one full-cabin exterior window slot, one gauge-only lens slot, and " +
                        "one authored display-readout renderer; " +
                        $"found {windowSlots}/{instrumentLensSlots}/{exteriorWindowRenderers}.");
                PrefabUtility.SaveAsPrefabAsset(root, AircraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsureInstrumentLensMeshAsset()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                AircraftReferenceFrameRig rig = root.GetComponent<AircraftReferenceFrameRig>();
                Transform viewpoint = FindDescendant(root.transform, "Main Camera");
                if (rig == null || rig.AircraftVisualRoot == null || viewpoint == null)
                    throw new InvalidOperationException(
                        "Aircraft references are missing while authoring the gauge lens mesh.");
                Renderer meter = rig.AircraftVisualRoot.GetComponentsInChildren<Renderer>(true)
                    .Single(renderer => string.Equals(
                        renderer.name, InstrumentMeterRendererName, StringComparison.Ordinal));
                BuildInstrumentLensArray(rig.AircraftVisualRoot, meter, viewpoint,
                    LoadRequired<Material>(InstrumentGlassMaterialPath));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsureInstrumentDisplayReadoutMeshAsset()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                AircraftReferenceFrameRig rig = root.GetComponent<AircraftReferenceFrameRig>();
                Transform viewpoint = FindDescendant(root.transform, "Main Camera");
                if (rig == null || rig.AircraftVisualRoot == null || viewpoint == null)
                    throw new InvalidOperationException(
                        "Aircraft references are missing while authoring instrument readouts.");
                Renderer display = rig.AircraftVisualRoot.GetComponentsInChildren<Renderer>(true)
                    .Single(renderer => string.Equals(
                        renderer.name, InstrumentDisplayRendererName, StringComparison.Ordinal));
                BuildInstrumentDisplayReadouts(rig.AircraftVisualRoot, display, viewpoint,
                    LoadRequired<Material>(InstrumentDisplayReadoutMaterialPath));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Derives one bounded lens disc for each round gauge directly from the imported meter
        // topology. The result cannot cover rectangular displays or the lower switch panel.
        private static Renderer BuildInstrumentLensArray(
            Transform aircraftVisual,
            Renderer meterRenderer,
            Transform viewpoint,
            Material material)
        {
            Mesh sourceMesh = meterRenderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (sourceMesh == null)
                throw new InvalidOperationException("Instrument meter source mesh is missing.");

            Vector3[] sourceVertices = sourceMesh.vertices;
            int[] sourceTriangles = sourceMesh.triangles;
            var trianglesByVertex = new Dictionary<int, List<int>>();
            for (int triangle = 0; triangle < sourceTriangles.Length / 3; triangle++)
            {
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = sourceTriangles[triangle * 3 + corner];
                    if (!trianglesByVertex.TryGetValue(vertex, out List<int> adjacent))
                    {
                        adjacent = new List<int>();
                        trianglesByVertex.Add(vertex, adjacent);
                    }
                    adjacent.Add(triangle);
                }
            }

            bool[] visited = new bool[sourceTriangles.Length / 3];
            var apertures = new List<GaugeAperture>();
            var queue = new Queue<int>();
            Vector3 eye = aircraftVisual.InverseTransformPoint(viewpoint.position);
            for (int triangle = 0; triangle < visited.Length; triangle++)
            {
                if (visited[triangle]) continue;
                visited[triangle] = true;
                queue.Enqueue(triangle);
                var componentTriangles = new List<int>();
                var componentVertices = new HashSet<int>();
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    componentTriangles.Add(current);
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = sourceTriangles[current * 3 + corner];
                        componentVertices.Add(vertex);
                        foreach (int neighbor in trianglesByVertex[vertex])
                        {
                            if (visited[neighbor]) continue;
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                bool initialized = false;
                Bounds bounds = default;
                var componentPoints = new List<Vector3>(componentVertices.Count);
                foreach (int vertex in componentVertices)
                {
                    Vector3 point = aircraftVisual.InverseTransformPoint(
                        meterRenderer.transform.TransformPoint(sourceVertices[vertex]));
                    componentPoints.Add(point);
                    if (!initialized)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }

                Vector3 size = bounds.size;
                if (size.x < 0.045f || size.x > 0.11f ||
                    size.y < 0.045f || size.y > 0.11f || size.z > 0.012f)
                    continue;

                // All round gauges are installed in the same panel plane. Pointing every
                // synthetic lens at the eye made the array fan toward the pilot and detach
                // visibly from the bezels. The imported meter topology is XY-planar in
                // AircraftVisualRoot space, so use its physical panel normal and only choose
                // the sign that faces the cockpit.
                Vector3 normal = Vector3.forward;
                if (Vector3.Dot(normal, eye - bounds.center) < 0f)
                    normal = -normal;
                const float lensFaceClearanceMeters = 0.0001f;
                const float enclosureRadialClearanceMeters = 0.00025f;
                float front = componentPoints.Max(point => Vector3.Dot(point, normal));
                Vector3 center = bounds.center + normal *
                    (front - Vector3.Dot(bounds.center, normal) +
                     lensFaceClearanceMeters);
                float radius = Mathf.Min(size.x, size.y) * 0.5f -
                    enclosureRadialClearanceMeters;
                if (radius <= 0f)
                    throw new InvalidOperationException(
                        "A measured gauge aperture is too small for its physical clearance.");
                apertures.Add(new GaugeAperture(
                    center, normal, radius));
            }

            apertures = apertures.OrderByDescending(item => item.Center.y)
                .ThenBy(item => item.Center.x).ToList();
            if (apertures.Count != 13)
                throw new InvalidOperationException(
                    $"Expected 13 geometry-derived gauge apertures; found {apertures.Count}.");

            const int segments = 24;
            var vertices = new List<Vector3>(apertures.Count * (segments + 1));
            var normals = new List<Vector3>(vertices.Capacity);
            var uvs = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(apertures.Count * segments * 3);
            foreach (GaugeAperture aperture in apertures)
            {
                int first = vertices.Count;
                Vector3 right = Vector3.ProjectOnPlane(Vector3.right, aperture.Normal).normalized;
                if (right.sqrMagnitude < 0.5f)
                    right = Vector3.ProjectOnPlane(Vector3.forward, aperture.Normal).normalized;
                Vector3 up = Vector3.Cross(aperture.Normal, right).normalized;
                if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
                vertices.Add(aperture.Center);
                normals.Add(aperture.Normal);
                uvs.Add(new Vector2(0.5f, 0.5f));
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / segments;
                    float cosine = Mathf.Cos(angle);
                    float sine = Mathf.Sin(angle);
                    vertices.Add(aperture.Center +
                        (right * cosine + up * sine) * aperture.Radius);
                    normals.Add(aperture.Normal);
                    uvs.Add(new Vector2(0.5f + cosine * 0.5f, 0.5f + sine * 0.5f));
                }
                for (int segment = 0; segment < segments; segment++)
                {
                    triangles.Add(first);
                    triangles.Add(first + 1 + segment);
                    triangles.Add(first + 1 + (segment + 1) % segments);
                }
            }

            var generated = new Mesh
            {
                name = "C172_PrincipalInstrumentLensArray",
                indexFormat = IndexFormat.UInt16
            };
            generated.SetVertices(vertices);
            generated.SetNormals(normals);
            generated.SetUVs(0, uvs);
            generated.SetTriangles(triangles, 0, true);
            generated.UploadMeshData(false);

            Mesh meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(InstrumentLensArrayMeshPath);
            if (meshAsset == null)
            {
                AssetDatabase.CreateAsset(generated, InstrumentLensArrayMeshPath);
                meshAsset = generated;
            }
            else
            {
                EditorUtility.CopySerialized(generated, meshAsset);
                UnityEngine.Object.DestroyImmediate(generated);
            }
            EditorUtility.SetDirty(meshAsset);

            Transform existing = aircraftVisual.Find(InstrumentLensArrayName);
            GameObject lensObject;
            if (existing == null)
            {
                lensObject = new GameObject(InstrumentLensArrayName);
                lensObject.transform.SetParent(aircraftVisual, false);
            }
            else
            {
                lensObject = existing.gameObject;
            }
            lensObject.layer = 0;
            lensObject.transform.localPosition = Vector3.zero;
            lensObject.transform.localRotation = Quaternion.identity;
            lensObject.transform.localScale = Vector3.one;
            // Unity can return a native "fake null" component from an unpacked prefab object;
            // null-coalescing does not invoke UnityEngine.Object's overloaded null check.
            MeshFilter filter = lensObject.GetComponent<MeshFilter>();
            if (filter == null) filter = lensObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = lensObject.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = lensObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = meshAsset;
            renderer.sharedMaterial = material;
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return renderer;
        }

        // Copies only the physical GPS/radio/transponder aperture faces from the imported
        // avionics mesh. Their source glTF material deliberately remains untouched; this one
        // serialized opaque mesh gives the readouts an unambiguous UV path, one material and
        // one draw without covering knobs, bezels, switches or the lower panel.
        private static Renderer BuildInstrumentDisplayReadouts(
            Transform aircraftVisual,
            Renderer displayRenderer,
            Transform viewpoint,
            Material material)
        {
            Mesh sourceMesh = displayRenderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (sourceMesh == null)
                throw new InvalidOperationException("Instrument display source mesh is missing.");
            Vector3[] sourceVertices = sourceMesh.vertices;
            Vector3[] sourceNormals = sourceMesh.normals;
            Vector2[] sourceUvs = sourceMesh.uv;
            int[] sourceTriangles = sourceMesh.triangles;
            if (sourceNormals.Length != sourceVertices.Length || sourceUvs.Length != sourceVertices.Length)
                throw new InvalidOperationException("Instrument display source vertex channels are incomplete.");

            var vertices = new List<Vector3>(36);
            var normals = new List<Vector3>(36);
            var uvs = new List<Vector2>(36);
            var triangles = new List<int>(36);
            Matrix4x4 sourceToAircraft = aircraftVisual.worldToLocalMatrix *
                                        displayRenderer.transform.localToWorldMatrix;
            Vector3 eye = aircraftVisual.InverseTransformPoint(viewpoint.position);
            int selectedFaces = 0;
            for (int triangle = 0; triangle < sourceTriangles.Length / 3; triangle++)
            {
                int a = sourceTriangles[triangle * 3];
                int b = sourceTriangles[triangle * 3 + 1];
                int c = sourceTriangles[triangle * 3 + 2];
                Vector2 uvMin = Vector2.Min(sourceUvs[a], Vector2.Min(sourceUvs[b], sourceUvs[c]));
                Vector2 uvMax = Vector2.Max(sourceUvs[a], Vector2.Max(sourceUvs[b], sourceUvs[c]));
                float area = Vector3.Cross(sourceVertices[b] - sourceVertices[a],
                    sourceVertices[c] - sourceVertices[a]).magnitude * 0.5f;
                bool mainGps = area > 0.0017f &&
                               uvMin.x >= 0.63f && uvMin.x <= 0.65f &&
                               uvMin.y <= 0.02f && uvMax.x >= 0.98f &&
                               uvMax.y >= 0.23f && uvMax.y <= 0.25f;
                bool radio = area > 0.00070f &&
                             uvMin.x >= 0.61f && uvMin.x <= 0.63f &&
                             uvMin.y >= 0.31f && uvMin.y <= 0.33f &&
                             uvMax.x >= 0.98f && uvMax.y >= 0.41f && uvMax.y <= 0.43f;
                bool transponder = area > 0.00065f &&
                                   uvMin.x >= 0.61f && uvMin.x <= 0.63f &&
                                   uvMin.y >= 0.43f && uvMin.y <= 0.45f &&
                                   uvMax.x >= 0.97f && uvMax.y >= 0.45f && uvMax.y <= 0.47f;
                if (!mainGps && !radio && !transponder) continue;

                int[] sourceIndices = { a, b, c };
                var facePoints = new Vector3[3];
                var faceUvs = new Vector2[3];
                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceIndex = sourceIndices[corner];
                    facePoints[corner] =
                        sourceToAircraft.MultiplyPoint3x4(sourceVertices[sourceIndex]);
                    Vector2 atlasMin;
                    Vector2 atlasMax;
                    if (mainGps)
                    {
                        atlasMin = new Vector2(0f, 0f);
                        atlasMax = new Vector2(1f, 0.5f);
                    }
                    else if (radio)
                    {
                        atlasMin = new Vector2(0f, 0.5f);
                        atlasMax = new Vector2(1f, 0.75f);
                    }
                    else
                    {
                        atlasMin = new Vector2(0f, 0.75f);
                        atlasMax = Vector2.one;
                    }

                    // Normalize each physical screen's proven source UV range, correct its
                    // vertical frame, and place it in the dedicated readout atlas.
                    Vector2 sourceUv = sourceUvs[sourceIndex];
                    float localU = Mathf.InverseLerp(uvMin.x, uvMax.x, sourceUv.x);
                    float localV = 1f - Mathf.InverseLerp(uvMin.y, uvMax.y, sourceUv.y);
                    faceUvs[corner] = new Vector2(
                        Mathf.Lerp(atlasMin.x, atlasMax.x, localU),
                        Mathf.Lerp(atlasMin.y, atlasMax.y, localV));
                }
                Vector3 faceCenter = (facePoints[0] + facePoints[1] + facePoints[2]) / 3f;
                Vector3 faceNormal = Vector3.Cross(
                    facePoints[1] - facePoints[0], facePoints[2] - facePoints[0]).normalized;
                if (Vector3.Dot(faceNormal, eye - faceCenter) < 0f)
                {
                    (facePoints[1], facePoints[2]) = (facePoints[2], facePoints[1]);
                    (faceUvs[1], faceUvs[2]) = (faceUvs[2], faceUvs[1]);
                    faceNormal = -faceNormal;
                }

                // Offset the complete face along its physical normal. The previous
                // per-vertex eye vector skewed the overlay toward the pilot instead of
                // leaving it seated on the source display surface.
                int first = vertices.Count;
                for (int corner = 0; corner < 3; corner++)
                {
                    vertices.Add(facePoints[corner] + faceNormal * 0.0008f);
                    normals.Add(faceNormal);
                    uvs.Add(faceUvs[corner]);
                }
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
                selectedFaces++;
            }
            if (selectedFaces != 12)
                throw new InvalidOperationException(
                    $"Expected 12 authored display aperture triangles; found {selectedFaces}.");

            var generated = new Mesh
            {
                name = "C172_InstrumentDisplayReadouts",
                indexFormat = IndexFormat.UInt16
            };
            generated.SetVertices(vertices);
            generated.SetNormals(normals);
            generated.SetUVs(0, uvs);
            generated.SetTriangles(triangles, 0, true);
            generated.UploadMeshData(false);
            Mesh meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(InstrumentDisplayReadoutMeshPath);
            if (meshAsset == null)
            {
                AssetDatabase.CreateAsset(generated, InstrumentDisplayReadoutMeshPath);
                meshAsset = generated;
            }
            else
            {
                EditorUtility.CopySerialized(generated, meshAsset);
                UnityEngine.Object.DestroyImmediate(generated);
            }
            EditorUtility.SetDirty(meshAsset);

            Transform existing = aircraftVisual.Find(InstrumentDisplayReadoutName);
            GameObject readoutObject;
            if (existing == null)
            {
                readoutObject = new GameObject(InstrumentDisplayReadoutName);
                readoutObject.transform.SetParent(aircraftVisual, false);
            }
            else
            {
                readoutObject = existing.gameObject;
            }
            readoutObject.layer = 0;
            readoutObject.transform.localPosition = Vector3.zero;
            readoutObject.transform.localRotation = Quaternion.identity;
            readoutObject.transform.localScale = Vector3.one;
            MeshFilter filter = readoutObject.GetComponent<MeshFilter>();
            if (filter == null) filter = readoutObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = readoutObject.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = readoutObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = meshAsset;
            renderer.sharedMaterial = material;
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return renderer;
        }

        private readonly struct GaugeAperture
        {
            public GaugeAperture(Vector3 center, Vector3 normal, float radius)
            {
                Center = center;
                Normal = normal;
                Radius = radius;
            }

            public Vector3 Center { get; }
            public Vector3 Normal { get; }
            public float Radius { get; }
        }

        private static void RewriteRendererArray(
            SerializedObject serialized,
            string propertyName,
            Func<Renderer, bool> keep,
            Renderer append)
        {
            SerializedProperty property = RequiredProperty(serialized, propertyName);
            List<Renderer> values = new List<Renderer>();
            for (int index = 0; index < property.arraySize; index++)
            {
                Renderer renderer = property.GetArrayElementAtIndex(index).objectReferenceValue as Renderer;
                if (renderer != null && keep(renderer) && !values.Contains(renderer)) values.Add(renderer);
            }
            if (append != null && !values.Contains(append)) values.Add(append);
            property.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        }

        private static void RemoveRejectedCockpitShadowSun()
        {
            UnityEngine.Object tagManager = AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/TagManager.asset").Single();
            SerializedObject tagSettings = new SerializedObject(tagManager);
            SerializedProperty layers = tagSettings.FindProperty("layers");
            if (layers == null || layers.arraySize <= CockpitShadowLayer)
                throw new InvalidOperationException("Unity layer table is incomplete.");
            if (string.Equals(layers.GetArrayElementAtIndex(CockpitShadowLayer).stringValue,
                    CockpitShadowLayerName, StringComparison.Ordinal))
                layers.GetArrayElementAtIndex(CockpitShadowLayer).stringValue = string.Empty;
            tagSettings.ApplyModifiedPropertiesWithoutUndo();

            GameObject root = PrefabUtility.LoadPrefabContents(ProductionWorldPrefabPath);
            try
            {
                Transform mainSunTransform = FindDescendant(root.transform, MainSunName);
                if (mainSunTransform == null)
                    throw new InvalidOperationException("The authored product sun is missing.");
                Light mainSun = mainSunTransform.GetComponent<Light>();
                if (mainSun == null || mainSun.type != LightType.Directional)
                    throw new InvalidOperationException("The authored product sun is not directional.");
                Transform existing = FindDescendant(root.transform, CockpitShadowSunName);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                mainSun.cullingMask |= 1 << CockpitShadowLayer;
                EditorUtility.SetDirty(mainSun);
                PrefabUtility.SaveAsPrefabAsset(root, ProductionWorldPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void IntegrateSceneReflection(PhotorealAerialReflectionProfile profile)
        {
            Scene scene = EditorSceneManager.OpenScene(ProductionScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Production scene could not be opened.");
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = profile.GlobalGroundSkyReflection;
            RenderSettings.reflectionIntensity = profile.GlobalReflectionIntensity;
            RenderSettings.reflectionBounces = 1;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Failed to serialize global reflection settings.");
        }

        private static int ConfigureHeroConsumers(Component root)
        {
            if (root == null) return 0;
            return ConfigureHeroConsumers(root.transform);
        }

        private static int ConfigureHeroConsumers(Transform root)
        {
            if (root == null) return 0;
            int count = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                renderer.probeAnchor = null;
                EditorUtility.SetDirty(renderer);
                count++;
            }
            return count;
        }

        private static void ValidateRuntimeAssets()
        {
            Cubemap global = ValidateReflectionImport(GlobalReflectionPath);
            Cubemap airportLocal = ValidateReflectionImport(AirportLocalReflectionPath);
            Cubemap cockpitInterior = ValidateReflectionImport(CockpitInteriorReflectionPath);
            if (global == airportLocal || File.ReadAllBytes(GlobalReflectionPath)
                    .SequenceEqual(File.ReadAllBytes(AirportLocalReflectionPath)))
                throw new InvalidOperationException("Global and airport-local reflections must be distinct.");
            if (cockpitInterior == global || cockpitInterior == airportLocal ||
                File.ReadAllBytes(CockpitInteriorReflectionPath)
                    .SequenceEqual(File.ReadAllBytes(GlobalReflectionPath)))
                throw new InvalidOperationException("Cockpit interior reflection must be a distinct offline asset.");

            PhotorealAerialReflectionProfile profile = LoadRequired<PhotorealAerialReflectionProfile>(ProfilePath);
            if (!profile.TryValidate(out string report))
                throw new InvalidOperationException("Reflection profile failed: " + report);
            if (profile.GlobalGroundSkyReflection != global || profile.AirportLocalReflection != airportLocal)
                throw new InvalidOperationException("Reflection profile cubemap bindings are incomplete.");
            string manifest = File.ReadAllText(ManifestPath);
            foreach (string required in new[]
                     {
                         Sha256(GlobalReflectionPath), Sha256(AirportLocalReflectionPath),
                         Sha256(CockpitInteriorReflectionPath),
                         Sha256(SourceNearOrthophotoPath), Sha256(SourceMidOrthophotoPath),
                         Sha256(SourceFarOrthophotoPath), "bilinear_direction_to_ground_proxy"
                     })
            {
                if (!manifest.Contains(required, StringComparison.Ordinal))
                    throw new InvalidOperationException("Reflection manifest is stale: " + required);
            }
            Material window = LoadRequired<Material>(WindowMaterialPath);
            Material instrument = LoadRequired<Material>(InstrumentGlassMaterialPath);
            Material displayReadout = LoadRequired<Material>(InstrumentDisplayReadoutMaterialPath);
            Shader stableInterior = LoadRequired<Shader>(CockpitStableInteriorShaderPath);
            if (window.shader == null || window.shader.name != WindowShaderName || window.passCount != 1 ||
                window.renderQueue != (int)RenderQueue.Transparent + 12)
                throw new InvalidOperationException("Window material is not the bounded single-pass shader.");
            if (instrument.shader == null || instrument.shader.name != InstrumentGlassShaderName ||
                instrument.passCount != 1 || instrument.renderQueue != (int)RenderQueue.Transparent + 16)
                throw new InvalidOperationException("Instrument glass is not the restrained single-pass shader.");
            if (displayReadout.shader == null || displayReadout.shader.name != "Unlit/Texture" ||
                displayReadout.passCount != 1 ||
                displayReadout.GetTexture("_MainTex") !=
                LoadRequired<Texture2D>(InstrumentDisplayBaseColorPath))
                throw new InvalidOperationException("Instrument display readout material is invalid.");
            if (window.GetTexture("_CockpitInteriorReflectionCube") != cockpitInterior ||
                instrument.GetTexture("_AuthoredReflectionCube") != cockpitInterior)
                throw new InvalidOperationException(
                    "Cockpit and instrument glass do not share the authored interior reflection.");
            if (stableInterior.name != CockpitStableInteriorShaderName ||
                stableInterior.passCount != 1)
                throw new InvalidOperationException("Cockpit stable-interior shader is not bounded and single-pass.");
        }

        private static Cubemap ValidateReflectionImport(string path)
        {
            Cubemap cubemap = LoadRequired<Cubemap>(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer?.ReadTextureSettings(settings);
            TextureImporterPlatformSettings android = importer != null
                ? importer.GetPlatformTextureSettings("Android")
                : null;
            if (importer == null || cubemap.width != 128 ||
                importer.textureShape != TextureImporterShape.TextureCube ||
                importer.generateCubemap != TextureImporterGenerateCubemap.Cylindrical ||
                settings.cubemapConvolution != TextureImporterCubemapConvolution.Specular ||
                importer.sRGBTexture || !importer.mipmapEnabled || importer.streamingMipmaps ||
                android == null || !android.overridden || android.maxTextureSize != 128 ||
                android.format != TextureImporterFormat.ASTC_HDR_6x6)
                throw new InvalidOperationException("Ground-sky cubemap import contract is incomplete: " + path);
            return cubemap;
        }

        private static void ValidateIntegratedFoundation()
        {
            ValidateRuntimeAssets();
            PhotorealAerialReflectionProfile profile = LoadRequired<PhotorealAerialReflectionProfile>(ProfilePath);
            GameObject environmentAsset = LoadRequired<GameObject>(EnvironmentPrefabPath);
            ReflectionProbe[] probes = environmentAsset.GetComponentsInChildren<ReflectionProbe>(true)
                .Where(item => string.Equals(item.name, AirportProbeObjectName, StringComparison.Ordinal))
                .ToArray();
            if (probes.Length != 1 || probes[0].mode != ReflectionProbeMode.Custom ||
                probes[0].customBakedTexture != profile.AirportLocalReflection ||
                probes[0].refreshMode != ReflectionProbeRefreshMode.ViaScripting ||
                probes[0].cullingMask != 0 || probes[0].resolution != 128 || probes[0].boxProjection ||
                probes[0].GetComponentsInChildren<Camera>(true).Length != 0)
                throw new InvalidOperationException("Serialized airport reflection probe is invalid.");
            if (environmentAsset.GetComponentsInChildren<ReflectionProbe>(true)
                .Any(item => item.mode == ReflectionProbeMode.Realtime))
                throw new InvalidOperationException("Realtime reflection probes are forbidden.");

            GameObject aircraftAsset = LoadRequired<GameObject>(AircraftPrefabPath);
            AircraftReferenceFrameRig rig = aircraftAsset.GetComponent<AircraftReferenceFrameRig>();
            Renderer[] aircraftRenderers = rig.AircraftVisualRoot.GetComponentsInChildren<Renderer>(true);
            if (aircraftRenderers.Length == 0 || aircraftRenderers.Any(renderer =>
                    renderer.probeAnchor != rig.CenterOfGravityReference ||
                    renderer.reflectionProbeUsage != ReflectionProbeUsage.BlendProbesAndSkybox))
                throw new InvalidOperationException("Aircraft reflection anchor/blending contract is incomplete.");
            if (aircraftRenderers.SelectMany(renderer => renderer.sharedMaterials).Count(material =>
                    material == LoadRequired<Material>(WindowMaterialPath)) != 1)
                throw new InvalidOperationException("Dedicated cockpit glass bindings are incomplete.");
            Material instrumentLensMaterial = LoadRequired<Material>(InstrumentGlassMaterialPath);
            Renderer[] instrumentLensRenderers = aircraftRenderers.Where(renderer => renderer.enabled &&
                string.Equals(renderer.name, InstrumentLensArrayName, StringComparison.Ordinal) &&
                renderer.sharedMaterials.Length == 1 &&
                renderer.sharedMaterial == instrumentLensMaterial).ToArray();
            Mesh instrumentLensMesh = instrumentLensRenderers.FirstOrDefault()
                ?.GetComponent<MeshFilter>()?.sharedMesh;
            if (instrumentLensRenderers.Length != 1 || instrumentLensMesh == null ||
                instrumentLensMesh.vertexCount != 325 || instrumentLensMesh.triangles.Length != 576 ||
                FindDescendant(rig.AircraftVisualRoot, RejectedGeneratedInstrumentCoverName) != null)
                throw new InvalidOperationException(
                    "The single eight-aperture trainer lens contract is incomplete (obsolete decorative lenses must be removed).");
            Renderer instrumentDisplayReadout = aircraftRenderers.Single(renderer =>
                string.Equals(renderer.name, InstrumentDisplayReadoutName, StringComparison.Ordinal));
            Mesh instrumentDisplayReadoutMesh = instrumentDisplayReadout.GetComponent<MeshFilter>()?.sharedMesh;
            if (!instrumentDisplayReadout.enabled || instrumentDisplayReadoutMesh == null ||
                instrumentDisplayReadoutMesh.vertexCount != 36 ||
                instrumentDisplayReadoutMesh.triangles.Length != 36 ||
                instrumentDisplayReadout.sharedMaterial !=
                LoadRequired<Material>(InstrumentDisplayReadoutMaterialPath))
                throw new InvalidOperationException(
                    "The single opaque instrument display-readout contract is incomplete.");
            Renderer decorativeInstrumentCover = aircraftRenderers.Single(renderer =>
                string.Equals(renderer.name, DecorativeInstrumentCoverRendererName,
                    StringComparison.Ordinal));
            Renderer misidentifiedInstrumentGlass = aircraftRenderers.Single(renderer =>
                string.Equals(renderer.name, MisidentifiedInstrumentGlassRendererName,
                    StringComparison.Ordinal));
            if (decorativeInstrumentCover.enabled || misidentifiedInstrumentGlass.enabled)
                throw new InvalidOperationException(
                    "Imported non-gauge glass renderers must remain disabled.");
            foreach (string authoritativeName in new[]
                     {
                         InstrumentMeterRendererName,
                         "Static_Display_New_Mat_01_0_New_Mat_01_0",
                         "Static_TXT_White_New_Static_0_White_New_Static_0"
                     })
            {
                Renderer authoritative = aircraftRenderers.Single(renderer =>
                    string.Equals(renderer.name, authoritativeName, StringComparison.Ordinal));
                if (!authoritative.enabled || authoritative.sharedMaterials.Any(material =>
                        material == null || material.shader == null ||
                        string.Equals(material.shader.name, CockpitStableInteriorShaderName,
                            StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        "Authoritative instrument source material was replaced: " + authoritativeName);
            }
            Renderer[] exteriorWindowRenderers = aircraftRenderers.Where(renderer =>
                string.Equals(renderer.name, ExteriorWindowRendererName,
                    StringComparison.Ordinal)).ToArray();
            if (exteriorWindowRenderers.Length != 1 || !exteriorWindowRenderers[0].enabled ||
                exteriorWindowRenderers[0].sharedMaterials.Single() !=
                LoadRequired<Material>(WindowMaterialPath))
                throw new InvalidOperationException(
                    "The full-cabin exterior glazing mesh must be uniquely enabled and reflective.");

            Transform cockpit = FindDescendant(rig.AircraftVisualRoot, CockpitRootName);
            if (cockpit == null || cockpit.GetComponentsInChildren<Transform>(true)
                    .Any(item => item.gameObject.layer != 0) ||
                cockpit.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                    renderer.shadowCastingMode != ShadowCastingMode.Off || renderer.receiveShadows))
                throw new InvalidOperationException("Static-only cockpit depth contract is incomplete.");
            Renderer exteriorWindow = aircraftRenderers.Single(renderer =>
                string.Equals(renderer.name, ExteriorWindowRendererName, StringComparison.Ordinal));
            if (FindDescendant(rig.AircraftVisualRoot,
                    RejectedCockpitSunOcclusionObjectName) != null)
                throw new InvalidOperationException("Rejected cockpit sun-occlusion overlay remains active.");
            QuestFlightLab.Aircraft.ProductionAircraftLightingController lighting =
                aircraftAsset.GetComponentInChildren<QuestFlightLab.Aircraft.ProductionAircraftLightingController>(true);
            SerializedObject lightingSettings = new SerializedObject(lighting);
            if (lightingSettings.FindProperty("stableCockpitShader")?.objectReferenceValue !=
                LoadRequired<Shader>(CockpitStableInteriorShaderPath))
                throw new InvalidOperationException("Geometry-bound cockpit depth shader is not serialized.");
            if (lightingSettings.FindProperty("instrumentDisplayBaseColor")?.objectReferenceValue !=
                    LoadRequired<Texture2D>(InstrumentDisplayBaseColorPath) ||
                lightingSettings.FindProperty("instrumentDisplayEmission")?.objectReferenceValue !=
                    LoadRequired<Texture2D>(InstrumentDisplayEmissionPath))
                throw new InvalidOperationException(
                    "Readable instrument display textures are not serialized.");
            SerializedProperty directionalShade = lightingSettings.FindProperty(
                "directionalCabinShadowStrength");
            if (directionalShade == null || Mathf.Abs(directionalShade.floatValue -
                    QuestFlightLab.Aircraft.ProductionAircraftLightingController
                        .DefaultDirectionalCabinShadowStrength) > 0.0001f)
                throw new InvalidOperationException("Directional cockpit shade is not serialized.");
            QuestFlightLab.Aircraft.AircraftVisibilityProfileController visibility =
                aircraftAsset.GetComponentInChildren<QuestFlightLab.Aircraft.AircraftVisibilityProfileController>(true);
            SerializedObject visibilitySettings = new SerializedObject(visibility);
            Renderer[] cockpitOccluders = ReadRendererArray(visibilitySettings,
                "cockpitOccludingExteriorRenderers");
            Renderer[] interiorRenderers = ReadRendererArray(visibilitySettings, "interiorRenderers");
            if (cockpitOccluders.Contains(exteriorWindow) || interiorRenderers.Any(renderer => renderer == null) ||
                !interiorRenderers.Contains(instrumentLensRenderers[0]) ||
                !interiorRenderers.Contains(instrumentDisplayReadout))
                throw new InvalidOperationException(
                    "Cockpit view would hide glazing/lenses or retains a stale renderer reference.");
            GameObject worldAsset = LoadRequired<GameObject>(ProductionWorldPrefabPath);
            Light mainSun = FindDescendant(worldAsset.transform, MainSunName)?.GetComponent<Light>();
            Light cockpitSun = FindDescendant(worldAsset.transform,
                CockpitShadowSunName)?.GetComponent<Light>();
            if (mainSun == null || cockpitSun != null ||
                (mainSun.cullingMask & (1 << CockpitShadowLayer)) == 0)
                throw new InvalidOperationException("Rejected cockpit shadow sun remains active.");
            CockpitDirectionalVisibilityAuthoring.ValidateIntegratedStreams();

            Scene scene = EditorSceneManager.OpenScene(ProductionScenePath, OpenSceneMode.Single);
            if (RenderSettings.defaultReflectionMode != DefaultReflectionMode.Custom ||
                RenderSettings.customReflectionTexture != profile.GlobalGroundSkyReflection ||
                Mathf.Abs(RenderSettings.reflectionIntensity - profile.GlobalReflectionIntensity) > 0.0001f ||
                RenderSettings.reflectionBounces != 1)
                throw new InvalidOperationException("Global ground-sky reflection binding is incomplete.");
            if (scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<ReflectionProbe>(true))
                .Any(item => item.mode == ReflectionProbeMode.Realtime))
                throw new InvalidOperationException("Integrated scene contains a realtime reflection probe.");
        }

        private static Renderer[] ReadRendererArray(SerializedObject serialized, string propertyName)
        {
            SerializedProperty property = RequiredProperty(serialized, propertyName);
            Renderer[] result = new Renderer[property.arraySize];
            for (int index = 0; index < property.arraySize; index++)
                result[index] = property.GetArrayElementAtIndex(index).objectReferenceValue as Renderer;
            return result;
        }

        private readonly struct ReflectionAssetPair
        {
            public ReflectionAssetPair(
                Cubemap global,
                Cubemap airportLocal,
                Cubemap cockpitInterior,
                ReflectionStatistics globalStatistics,
                ReflectionStatistics localStatistics)
            {
                Global = global;
                AirportLocal = airportLocal;
                CockpitInterior = cockpitInterior;
                GlobalStatistics = globalStatistics;
                LocalStatistics = localStatistics;
            }

            public Cubemap Global { get; }
            public Cubemap AirportLocal { get; }
            public Cubemap CockpitInterior { get; }
            public ReflectionStatistics GlobalStatistics { get; }
            public ReflectionStatistics LocalStatistics { get; }
        }

        private readonly struct ReflectionStatistics
        {
            private ReflectionStatistics(
                Color mean,
                Color standardDeviation,
                float luminanceStandardDeviation)
            {
                Mean = mean;
                StandardDeviation = standardDeviation;
                LuminanceStandardDeviation = luminanceStandardDeviation;
            }

            public Color Mean { get; }
            public Color StandardDeviation { get; }
            public float LuminanceStandardDeviation { get; }

            public static ReflectionStatistics FromLowerHemisphere(Color[] pixels)
            {
                int count = ReflectionEquirectangularWidth * ReflectionEquirectangularHeight / 2;
                if (pixels == null || pixels.Length != count * 2)
                    throw new ArgumentException("Reflection source has unexpected dimensions.", nameof(pixels));
                double r = 0d;
                double g = 0d;
                double b = 0d;
                double luminance = 0d;
                for (int index = 0; index < count; index++)
                {
                    Color pixel = pixels[index];
                    r += pixel.r;
                    g += pixel.g;
                    b += pixel.b;
                    luminance += pixel.r * 0.2126d + pixel.g * 0.7152d + pixel.b * 0.0722d;
                }
                double meanR = r / count;
                double meanG = g / count;
                double meanB = b / count;
                double meanLuminance = luminance / count;
                double varianceR = 0d;
                double varianceG = 0d;
                double varianceB = 0d;
                double varianceLuminance = 0d;
                for (int index = 0; index < count; index++)
                {
                    Color pixel = pixels[index];
                    double luma = pixel.r * 0.2126d + pixel.g * 0.7152d + pixel.b * 0.0722d;
                    varianceR += (pixel.r - meanR) * (pixel.r - meanR);
                    varianceG += (pixel.g - meanG) * (pixel.g - meanG);
                    varianceB += (pixel.b - meanB) * (pixel.b - meanB);
                    varianceLuminance += (luma - meanLuminance) * (luma - meanLuminance);
                }
                return new ReflectionStatistics(
                    new Color((float)meanR, (float)meanG, (float)meanB, 1f),
                    new Color(
                        (float)Math.Sqrt(varianceR / count),
                        (float)Math.Sqrt(varianceG / count),
                        (float)Math.Sqrt(varianceB / count),
                        0f),
                    (float)Math.Sqrt(varianceLuminance / count));
            }
        }

        public sealed class CockpitShadowCandidateScope : IDisposable
        {
            private readonly Light sun;
            private readonly bool originalSunEnabled;
            private readonly LightShadows originalLightShadows;
            private readonly int originalCustomResolution;
            private readonly float originalStrength;
            private readonly float originalBias;
            private readonly float originalNormalBias;
            private readonly float originalNearPlane;
            private readonly ShadowQuality originalQuality;
            private readonly ShadowResolution originalResolution;
            private readonly int originalCascades;
            private readonly float originalDistance;
            private readonly RendererState[] rendererStates;
            private bool disposed;

            internal CockpitShadowCandidateScope(Scene scene, CockpitSunShadowCandidate candidate)
            {
                AircraftReferenceFrameRig[] rigs = scene.GetRootGameObjects()
                    .SelectMany(item => item.GetComponentsInChildren<AircraftReferenceFrameRig>(true))
                    .ToArray();
                if (rigs.Length != 1)
                    throw new InvalidOperationException("Shadow candidate requires exactly one authored aircraft rig.");
                Transform cockpit = FindDescendant(rigs[0].AircraftVisualRoot, CockpitRootName);
                if (cockpit == null)
                    throw new InvalidOperationException("Authored cockpit root is missing.");
                Light authoredSun = RenderSettings.sun;
                if (authoredSun == null || authoredSun.type != LightType.Directional ||
                    !authoredSun.gameObject.activeInHierarchy)
                {
                    Light[] enabledSuns = scene.GetRootGameObjects()
                        .SelectMany(item => item.GetComponentsInChildren<Light>(true))
                        .Where(item => item.enabled && item.gameObject.activeInHierarchy &&
                                       item.type == LightType.Directional)
                        .ToArray();
                    if (enabledSuns.Length != 1)
                        throw new InvalidOperationException(
                            "Shadow candidate requires one RenderSettings sun or one enabled directional sun.");
                    authoredSun = enabledSuns[0];
                }
                sun = authoredSun;
                originalSunEnabled = sun.enabled;
                originalLightShadows = sun.shadows;
                originalCustomResolution = sun.shadowCustomResolution;
                originalStrength = sun.shadowStrength;
                originalBias = sun.shadowBias;
                originalNormalBias = sun.shadowNormalBias;
                originalNearPlane = sun.shadowNearPlane;
                originalQuality = QualitySettings.shadows;
                originalResolution = QualitySettings.shadowResolution;
                originalCascades = QualitySettings.shadowCascades;
                originalDistance = QualitySettings.shadowDistance;

                Renderer[] renderers = scene.GetRootGameObjects()
                    .SelectMany(item => item.GetComponentsInChildren<Renderer>(true))
                    .ToArray();
                rendererStates = renderers.Select(renderer => new RendererState(renderer)).ToArray();
                foreach (Renderer renderer in renderers)
                {
                    bool cockpitOwned = renderer.transform == cockpit || renderer.transform.IsChildOf(cockpit);
                    bool transparentGlass = renderer.sharedMaterials.Any(material => material != null &&
                        material.shader != null &&
                        (material.shader.name == WindowShaderName ||
                         material.shader.name == InstrumentGlassShaderName));
                    renderer.shadowCastingMode = cockpitOwned && !transparentGlass
                        ? ShadowCastingMode.On
                        : ShadowCastingMode.Off;
                    renderer.receiveShadows = cockpitOwned && !transparentGlass;
                }

                QualitySettings.shadows = ShadowQuality.HardOnly;
                QualitySettings.shadowResolution = ShadowResolution.High;
                QualitySettings.shadowCascades = candidate.CascadeCount;
                QualitySettings.shadowDistance = candidate.DistanceMeters;
                sun.enabled = true;
                sun.shadows = LightShadows.Hard;
                sun.shadowCustomResolution = candidate.MapResolution;
                sun.shadowStrength = candidate.Strength;
                sun.shadowBias = candidate.Bias;
                sun.shadowNormalBias = candidate.NormalBias;
                sun.shadowNearPlane = candidate.NearPlaneMeters;
            }

            public void Dispose()
            {
                if (disposed) return;
                foreach (RendererState state in rendererStates) state.Restore();
                if (sun != null)
                {
                    sun.enabled = originalSunEnabled;
                    sun.shadows = originalLightShadows;
                    sun.shadowCustomResolution = originalCustomResolution;
                    sun.shadowStrength = originalStrength;
                    sun.shadowBias = originalBias;
                    sun.shadowNormalBias = originalNormalBias;
                    sun.shadowNearPlane = originalNearPlane;
                }
                QualitySettings.shadows = originalQuality;
                QualitySettings.shadowResolution = originalResolution;
                QualitySettings.shadowCascades = originalCascades;
                QualitySettings.shadowDistance = originalDistance;
                disposed = true;
            }
        }

        private readonly struct RendererState
        {
            private readonly Renderer renderer;
            private readonly ShadowCastingMode castingMode;
            private readonly bool receiveShadows;

            public RendererState(Renderer renderer)
            {
                this.renderer = renderer;
                castingMode = renderer.shadowCastingMode;
                receiveShadows = renderer.receiveShadows;
            }

            public void Restore()
            {
                if (renderer == null) return;
                renderer.shadowCastingMode = castingMode;
                renderer.receiveShadows = receiveShadows;
            }
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader, string name)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
                material.name = name;
            }
            return material;
        }

        private static Transform EnsureUniqueDirectChild(Transform parent, string name)
        {
            Transform[] matches = parent.Cast<Transform>()
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Transform result = matches.FirstOrDefault();
            if (result == null)
            {
                result = new GameObject(name).transform;
                result.SetParent(parent, false);
            }
            foreach (Transform duplicate in matches.Skip(1).ToArray())
                UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
            return result;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            if (string.Equals(root.name, name, StringComparison.Ordinal)) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void EnsureFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(TextureFolder);
            EnsureFolder(ProfileFolder);
            EnsureFolder(CockpitMaterialFolder);
            EnsureFolder(CockpitMeshFolder);
            EnsureFolder(CockpitTextureFolder);
        }

        private static void EnsureFolder(string path)
        {
            string[] components = path.Split('/');
            string current = components[0];
            for (int index = 1; index < components.Length; index++)
            {
                string next = current + "/" + components[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, components[index]);
                current = next;
            }
        }

        private static void WriteIfChanged(string path, byte[] bytes)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes)) return;
            File.WriteAllBytes(path, bytes);
        }

        private static void SetString(SerializedObject serialized, string path, string value) =>
            RequiredProperty(serialized, path).stringValue = value;
        private static void SetBool(SerializedObject serialized, string path, bool value) =>
            RequiredProperty(serialized, path).boolValue = value;
        private static void SetInt(SerializedObject serialized, string path, int value) =>
            RequiredProperty(serialized, path).intValue = value;
        private static void SetFloat(SerializedObject serialized, string path, float value) =>
            RequiredProperty(serialized, path).floatValue = value;
        private static void SetColor(SerializedObject serialized, string path, Color value) =>
            RequiredProperty(serialized, path).colorValue = value;
        private static void SetVector3(SerializedObject serialized, string path, Vector3 value) =>
            RequiredProperty(serialized, path).vector3Value = value;
        private static void SetObject(SerializedObject serialized, string path, UnityEngine.Object value) =>
            RequiredProperty(serialized, path).objectReferenceValue = value;

        private static SerializedProperty RequiredProperty(SerializedObject serialized, string path)
        {
            SerializedProperty property = serialized.FindProperty(path);
            if (property == null)
                throw new MissingMemberException(serialized.targetObject.GetType().Name, path);
            return property;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException("Required " + typeof(T).Name + " is missing: " + path);
            return asset;
        }
    }
}
#endif
