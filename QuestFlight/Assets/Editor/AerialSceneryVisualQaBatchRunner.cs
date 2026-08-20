using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Editor-only, fixed-camera flight-view witness for Aerial Scenery V1. It
    /// creates a temporary diagnostic camera and never writes the OpenXR-owned
    /// product Main Camera or saves transient A/B activation state.
    /// </summary>
    public static class AerialSceneryVisualQaBatchRunner
    {
        public const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        public const int RequiredViewsPerVariant = 20;

        [MenuItem("QuestFlight/QA/Capture Aerial Scenery V1")]
        public static void RunInteractive() => Run(exitEditor: false);

        public static void RunBatch()
        {
            try
            {
                Run(exitEditor: true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Run(bool exitEditor)
        {
            string outputDirectory = Environment.GetEnvironmentVariable("QFL_AERIAL_SCENERY_QA_DIR");
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new InvalidOperationException("QFL_AERIAL_SCENERY_QA_DIR is required.");
            outputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputDirectory);
            string screenshotDirectory = Path.Combine(outputDirectory, "screenshots");
            Directory.CreateDirectory(screenshotDirectory);

            int width = ReadDimension("QFL_AERIAL_SCENERY_QA_WIDTH", 1920);
            int height = ReadDimension("QFL_AERIAL_SCENERY_QA_HEIGHT", 1080);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("The production scene could not be opened.");

            Camera productCamera = scene.GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<Camera>(true))
                .Single(item => item.CompareTag("MainCamera") && item.enabled &&
                                item.gameObject.activeInHierarchy);
            ProductionEnvironmentRoot environment = scene.GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<ProductionEnvironmentRoot>(true)).Single();
            AerialSceneryRoot aerial = environment.GetComponentInChildren<AerialSceneryRoot>(true);

            GameObject diagnosticObject = new GameObject("AerialSceneryQaCamera")
            {
                hideFlags = HideFlags.DontSave
            };
            SceneManager.MoveGameObjectToScene(diagnosticObject, scene);
            Camera camera = diagnosticObject.AddComponent<Camera>();
            // Unity 6 does not reliably execute the offscreen render path for a
            // disabled batch-mode camera. The diagnostic camera is the only
            // enabled camera during this runner and is destroyed without saving.
            foreach (Camera existing in scene.GetRootGameObjects()
                         .SelectMany(item => item.GetComponentsInChildren<Camera>(true)))
                existing.enabled = false;
            camera.enabled = true;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 24000f;
            camera.fieldOfView = 72f;
            camera.clearFlags = RenderSettings.skybox != null
                ? CameraClearFlags.Skybox
                : CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.68f, 0.82f);
            camera.cullingMask = ~0;
            camera.aspect = width / (float)height;
            camera.allowMSAA = true;
            camera.targetTexture = null;

            VariantState state = VariantState.Capture(aerial);
            List<ShotResult> results = new List<ShotResult>();
            try
            {
                if (aerial == null)
                {
                    CaptureVariant("before", productCamera, environment, null, camera,
                        screenshotDirectory, width, height, results);
                }
                else
                {
                    state.ApplyBefore();
                    CaptureVariant("before", productCamera, environment, aerial, camera,
                        screenshotDirectory, width, height, results);
                    state.ApplyAfter();
                    CaptureVariant("after", productCamera, environment, aerial, camera,
                        screenshotDirectory, width, height, results);
                }
            }
            finally
            {
                state.Restore();
                Object.DestroyImmediate(diagnosticObject);
            }

            if (results.Count != RequiredViewsPerVariant * (aerial == null ? 1 : 2))
                throw new InvalidOperationException("Aerial QA did not produce the required fixed-view count.");

            string contactSheet = Path.Combine(outputDirectory,
                aerial == null ? "aerial_scenery_baseline_contact_sheet.png" :
                    "aerial_scenery_before_after_contact_sheet.png");
            BuildContactSheet(results, contactSheet, 4, 384, 216);

            QaReport report = new QaReport
            {
                schemaVersion = 1,
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                scenePath = ScenePath,
                sourcePipeline = "BuiltIn/Forward",
                evidenceKind = "EDITOR_FIXED_CAMERA_VISUAL_EVIDENCE_NOT_QUEST_PERFORMANCE",
                aerialSceneryIntegrated = aerial != null,
                variants = aerial == null ? new[] { "before" } : new[] { "before", "after" },
                viewsPerVariant = RequiredViewsPerVariant,
                width = width,
                height = height,
                contactSheetPath = contactSheet,
                shots = results.ToArray(),
                limitations = new[]
                {
                    "Editor fixed-camera captures do not establish Meta XR stereo behavior or Quest performance.",
                    "Canopy and terrain error heatmaps are offline geospatial evidence and are stored separately.",
                    "Full-resolution images require lead inspection; nonblank output is not visual acceptance."
                }
            };
            File.WriteAllText(Path.Combine(outputDirectory, "aerial_scenery_visual_qa.json"),
                JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(outputDirectory, "aerial_scenery_visual_qa.md"),
                BuildMarkdown(report));
            Debug.Log($"[QuestFlight][AerialSceneryQA] shots={results.Count} contactSheet={contactSheet}");
            if (exitEditor) EditorApplication.Exit(0);
        }

        private static void CaptureVariant(
            string variant,
            Camera productCamera,
            ProductionEnvironmentRoot environment,
            AerialSceneryRoot aerial,
            Camera camera,
            string outputDirectory,
            int width,
            int height,
            ICollection<ShotResult> results)
        {
            Bounds vegetation = FindBounds(aerial?.vegetationRoot,
                environment.transform, name => name.IndexOf("vegetation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               name.IndexOf("cottonwood", StringComparison.OrdinalIgnoreCase) >= 0);
            Bounds buildings = FindBounds(aerial?.buildingRoot,
                environment.transform, name => name.StartsWith("buildings_", StringComparison.OrdinalIgnoreCase) ||
                                               name.IndexOf("Hangar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               name.IndexOf("Terminal", StringComparison.OrdinalIgnoreCase) >= 0);
            Bounds reservoir = environment.boulderReservoirSurface != null
                ? environment.boulderReservoirSurface.GetComponent<Renderer>().bounds
                : new Bounds(new Vector3(1600f, 0f, 0f), new Vector3(600f, 10f, 600f));

            Vector3 airport = new Vector3(0f, 2f, -30f);
            Vector3 vegetationCenter = vegetation.size.sqrMagnitude > 0.01f
                ? vegetation.center : new Vector3(-435f, 4f, -215f);
            Vector3 buildingCenter = buildings.size.sqrMagnitude > 0.01f ? buildings.center : airport;
            Vector3 reservoirCenter = reservoir.center;

            List<ViewDefinition> views = new List<ViewDefinition>
            {
                ViewDefinition.FromCamera("01_cockpit_runway", "Cockpit runway view", productCamera),
                ViewDefinition.Look("02_taxi_near", "Taxi past near trees and buildings", new Vector3(-520f, 5f, -135f), new Vector3(-80f, 4f, -30f), 58f),
                ViewDefinition.Look("03_takeoff_50m", "Low takeoff at 50 m AGL", new Vector3(-360f, 52f, -30f), new Vector3(420f, 18f, -30f), 60f),
                ViewDefinition.Look("04_near_grove", "Near grove oblique", vegetationCenter + new Vector3(-85f, 35f, -110f), vegetationCenter + Vector3.up * 4f, 48f),
                ViewDefinition.Look("05_100m_agl", "100 m AGL airport view", new Vector3(-550f, 102f, -520f), airport, 55f),
                ViewDefinition.Look("06_300m_pattern", "300 m AGL traffic-pattern view", new Vector3(-100f, 302f, -1250f), airport, 54f),
                ViewDefinition.Look("07_500m_oblique", "500 m AGL oblique", new Vector3(-1250f, 502f, 650f), airport, 52f),
                ViewDefinition.Look("08_800m_aerial", "800 m AGL aerial", new Vector3(0f, 802f, -1650f), airport, 50f),
                ViewDefinition.Look("09_top_down_airport", "Top-down airport", new Vector3(0f, 1102f, -30f), airport, 48f),
                ViewDefinition.Look("10_riparian_corridor", "Riparian corridor", reservoirCenter + new Vector3(-700f, 210f, -500f), reservoirCenter, 48f),
                ViewDefinition.Look("11_dense_canopy", "Dense canopy", vegetationCenter + new Vector3(-320f, 220f, -300f), vegetationCenter, 44f),
                ViewDefinition.Look("12_sparse_canopy", "Sparse canopy", vegetationCenter + new Vector3(430f, 260f, 360f), vegetationCenter, 46f),
                ViewDefinition.Look("13_building_hlod", "Context building HLOD", buildingCenter + new Vector3(-550f, 190f, -500f), buildingCenter, 48f),
                ViewDefinition.Look("14_far_orthophoto_transition", "Orthophoto-only far transition", new Vector3(0f, 820f, 2600f), new Vector3(0f, 80f, 5200f), 50f),
                ViewDefinition.Look("15_tree_to_cluster", "Individual-tree to grove-cluster transition", vegetationCenter + new Vector3(-420f, 95f, -420f), vegetationCenter, 45f),
                ViewDefinition.Look("16_cluster_to_mid", "Grove-cluster to mid representation", vegetationCenter + new Vector3(-1150f, 260f, -1100f), vegetationCenter, 44f),
                ViewDefinition.Look("17_mid_to_far_canopy", "Mid canopy to terrain shader-map transition", vegetationCenter + new Vector3(-2600f, 650f, -2200f), vegetationCenter, 45f),
                ViewDefinition.Look("18_building_transition", "Near building to footprint HLOD transition", buildingCenter + new Vector3(-900f, 230f, -850f), buildingCenter, 43f),
                ViewDefinition.Look("19_far_terrain_horizon", "Far terrain and horizon", new Vector3(0f, 520f, -500f), new Vector3(0f, 550f, 9000f), 58f),
                ViewDefinition.Look("20_complete_landscape", "Complete airport landscape", new Vector3(-950f, 620f, -1150f), airport, 54f)
            };

            foreach (ViewDefinition view in views)
            {
                camera.transform.SetPositionAndRotation(view.position, view.rotation);
                camera.fieldOfView = view.fieldOfView;
                string path = Path.Combine(outputDirectory, variant + "_" + view.id + ".png");
                ShotResult result = Render(camera, path, width, height);
                result.id = view.id;
                result.title = view.title;
                result.variant = variant;
                result.cameraPosition = view.position;
                result.cameraEulerAngles = view.rotation.eulerAngles;
                result.fieldOfView = view.fieldOfView;
                results.Add(result);
            }
        }

        private static ShotResult Render(Camera camera, string path, int width, int height)
        {
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            RenderTexture previous = RenderTexture.active;
            Texture2D image = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                image.Apply(false, false);
                File.WriteAllBytes(path, image.EncodeToPNG());
                Color32[] pixels = image.GetPixels32();
                double luminance = 0d;
                double luminanceSquared = 0d;
                foreach (Color32 pixel in pixels)
                {
                    double value = (0.2126d * pixel.r + 0.7152d * pixel.g + 0.0722d * pixel.b) / 255d;
                    luminance += value;
                    luminanceSquared += value * value;
                }
                double mean = luminance / pixels.Length;
                double variance = Math.Max(0d, luminanceSquared / pixels.Length - mean * mean);
                return new ShotResult
                {
                    path = path,
                    width = width,
                    height = height,
                    meanLuminance = (float)mean,
                    luminanceVariance = (float)variance,
                    nonBlank = variance > 0.0005d
                };
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
        }

        private static Bounds FindBounds(Transform preferred, Transform fallbackRoot, Func<string, bool> nameFilter)
        {
            Renderer[] renderers = preferred != null
                ? preferred.GetComponentsInChildren<Renderer>(true)
                : fallbackRoot.GetComponentsInChildren<Renderer>(true)
                    .Where(item => nameFilter(item.name) || nameFilter(item.transform.parent?.name ?? string.Empty))
                    .ToArray();
            bool initialized = false;
            Bounds bounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (preferred != null)
            {
                foreach (AerialVegetationBatchRenderer batchRenderer in
                         preferred.GetComponentsInChildren<AerialVegetationBatchRenderer>(true))
                foreach (AerialVegetationBatchRenderer.Batch batch in batchRenderer.Batches)
                {
                    if (batch.Mesh == null || batch.Matrices == null) continue;
                    foreach (Matrix4x4 matrix in batch.Matrices)
                    {
                        Bounds instance = TransformBounds(batch.Mesh.bounds, matrix);
                        if (!initialized)
                        {
                            bounds = instance;
                            initialized = true;
                        }
                        else
                        {
                            bounds.Encapsulate(instance);
                        }
                    }
                }
            }
            return initialized ? bounds : default;
        }

        private static Bounds TransformBounds(Bounds source, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(source.center);
            Vector3 extents = source.extents;
            Vector3 worldExtents = new Vector3(
                Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y +
                    Mathf.Abs(matrix.m02) * extents.z,
                Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y +
                    Mathf.Abs(matrix.m12) * extents.z,
                Mathf.Abs(matrix.m20) * extents.x + Mathf.Abs(matrix.m21) * extents.y +
                    Mathf.Abs(matrix.m22) * extents.z);
            return new Bounds(center, worldExtents * 2f);
        }

        private static void BuildContactSheet(
            IReadOnlyList<ShotResult> shots,
            string destination,
            int columns,
            int thumbnailWidth,
            int thumbnailHeight)
        {
            int rows = Mathf.CeilToInt(shots.Count / (float)columns);
            Texture2D sheet = new Texture2D(columns * thumbnailWidth, rows * thumbnailHeight,
                TextureFormat.RGB24, false, false);
            Color[] background = Enumerable.Repeat(new Color(0.025f, 0.025f, 0.025f, 1f),
                sheet.width * sheet.height).ToArray();
            sheet.SetPixels(background);
            try
            {
                for (int index = 0; index < shots.Count; index++)
                {
                    Texture2D source = new Texture2D(2, 2, TextureFormat.RGB24, false, false);
                    try
                    {
                        source.LoadImage(File.ReadAllBytes(shots[index].path), false);
                        int column = index % columns;
                        int rowFromTop = index / columns;
                        int originX = column * thumbnailWidth;
                        int originY = (rows - rowFromTop - 1) * thumbnailHeight;
                        Color[] pixels = new Color[thumbnailWidth * thumbnailHeight];
                        for (int y = 0; y < thumbnailHeight; y++)
                        for (int x = 0; x < thumbnailWidth; x++)
                            pixels[y * thumbnailWidth + x] = source.GetPixelBilinear(
                                (x + 0.5f) / thumbnailWidth, (y + 0.5f) / thumbnailHeight);
                        sheet.SetPixels(originX, originY, thumbnailWidth, thumbnailHeight, pixels);
                    }
                    finally
                    {
                        Object.DestroyImmediate(source);
                    }
                }
                sheet.Apply(false, false);
                File.WriteAllBytes(destination, sheet.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(sheet);
            }
        }

        private static string BuildMarkdown(QaReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Aerial scenery fixed-camera QA");
            builder.AppendLine();
            builder.AppendLine($"- Evidence: `{report.evidenceKind}`");
            builder.AppendLine($"- Integrated Aerial Scenery root: `{report.aerialSceneryIntegrated}`");
            builder.AppendLine($"- Captures: `{report.shots.Length}` at `{report.width}x{report.height}`");
            builder.AppendLine($"- Contact sheet: `{report.contactSheetPath}`");
            builder.AppendLine();
            builder.AppendLine("| Variant | View | Mean luminance | Variance | Nonblank | Path |");
            builder.AppendLine("|---|---|---:|---:|---|---|");
            foreach (ShotResult shot in report.shots)
                builder.AppendLine($"| {shot.variant} | {shot.id} | {shot.meanLuminance:0.0000} | " +
                                   $"{shot.luminanceVariance:0.0000} | {shot.nonBlank} | `{shot.path}` |");
            builder.AppendLine();
            foreach (string limitation in report.limitations) builder.AppendLine("- " + limitation);
            return builder.ToString();
        }

        private static int ReadDimension(string name, int fallback)
        {
            return int.TryParse(Environment.GetEnvironmentVariable(name), out int value)
                ? Mathf.Clamp(value, 320, 4096)
                : fallback;
        }

        private readonly struct ViewDefinition
        {
            public readonly string id, title;
            public readonly Vector3 position;
            public readonly Quaternion rotation;
            public readonly float fieldOfView;

            private ViewDefinition(string viewId, string viewTitle, Vector3 cameraPosition,
                Quaternion cameraRotation, float fov)
            {
                id = viewId;
                title = viewTitle;
                position = cameraPosition;
                rotation = cameraRotation;
                fieldOfView = fov;
            }

            public static ViewDefinition Look(string id, string title, Vector3 position, Vector3 target, float fov)
            {
                Vector3 direction = target - position;
                if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
                return new ViewDefinition(id, title, position, Quaternion.LookRotation(direction, Vector3.up), fov);
            }

            public static ViewDefinition FromCamera(string id, string title, Camera camera)
            {
                return new ViewDefinition(id, title, camera.transform.position, camera.transform.rotation,
                    camera.fieldOfView);
            }
        }

        private sealed class VariantState
        {
            private readonly AerialSceneryRoot root;
            private readonly bool rootActive;
            private readonly Transform[] legacy;
            private readonly bool[] legacyActive;
            private readonly MeshFilter nearTerrain;
            private readonly Mesh originalNearTerrainMesh;
            private readonly Mesh baselineNearTerrainMesh;
            private readonly Mesh aerialNearTerrainMesh;
            private readonly MeshFilter airportPatch;
            private readonly Mesh originalAirportPatchMesh;
            private readonly Mesh baselineAirportPatchMesh;
            private readonly Mesh aerialAirportPatchMesh;

            private VariantState(AerialSceneryRoot value)
            {
                root = value;
                rootActive = value != null && value.gameObject.activeSelf;
                legacy = value == null
                    ? Array.Empty<Transform>()
                    : value.disabledLegacyVegetationRoots.Concat(value.disabledLegacyBuildingRoots)
                        .Where(item => item != null).Distinct().ToArray();
                legacyActive = legacy.Select(item => item.gameObject.activeSelf).ToArray();
                nearTerrain = value?.authoritativeNearTerrain;
                originalNearTerrainMesh = nearTerrain != null ? nearTerrain.sharedMesh : null;
                baselineNearTerrainMesh = value?.baselineNearTerrainMesh;
                aerialNearTerrainMesh = value?.manifest != null ? value.manifest.nearTerrainMesh : null;
                airportPatch = value?.authoritativeAirportPatch;
                originalAirportPatchMesh = airportPatch != null ? airportPatch.sharedMesh : null;
                baselineAirportPatchMesh = value?.baselineAirportPatchMesh;
                aerialAirportPatchMesh = value?.manifest != null ? value.manifest.airportPatchMesh : null;
            }

            public static VariantState Capture(AerialSceneryRoot value) => new VariantState(value);

            public void ApplyBefore()
            {
                if (root == null) return;
                root.gameObject.SetActive(false);
                foreach (Transform item in legacy) item.gameObject.SetActive(true);
                if (nearTerrain != null && baselineNearTerrainMesh != null)
                    nearTerrain.sharedMesh = baselineNearTerrainMesh;
                if (airportPatch != null && baselineAirportPatchMesh != null)
                    airportPatch.sharedMesh = baselineAirportPatchMesh;
            }

            public void ApplyAfter()
            {
                if (root == null) return;
                root.gameObject.SetActive(true);
                foreach (Transform item in legacy) item.gameObject.SetActive(false);
                if (nearTerrain != null && aerialNearTerrainMesh != null)
                    nearTerrain.sharedMesh = aerialNearTerrainMesh;
                if (airportPatch != null && aerialAirportPatchMesh != null)
                    airportPatch.sharedMesh = aerialAirportPatchMesh;
            }

            public void Restore()
            {
                if (root == null) return;
                root.gameObject.SetActive(rootActive);
                for (int index = 0; index < legacy.Length; index++)
                    legacy[index].gameObject.SetActive(legacyActive[index]);
                if (nearTerrain != null) nearTerrain.sharedMesh = originalNearTerrainMesh;
                if (airportPatch != null) airportPatch.sharedMesh = originalAirportPatchMesh;
            }
        }

        [Serializable]
        private sealed class QaReport
        {
            public int schemaVersion, viewsPerVariant, width, height;
            public string generatedUtc, scenePath, sourcePipeline, evidenceKind, contactSheetPath;
            public bool aerialSceneryIntegrated;
            public string[] variants, limitations;
            public ShotResult[] shots;
        }

        [Serializable]
        private sealed class ShotResult
        {
            public string id, title, variant, path;
            public int width, height;
            public Vector3 cameraPosition, cameraEulerAngles;
            public float fieldOfView, meanLuminance, luminanceVariance;
            public bool nonBlank;
        }
    }
}
