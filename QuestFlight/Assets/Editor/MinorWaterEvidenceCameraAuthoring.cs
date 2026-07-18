using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace QuestFlight.Editor
{
    public static class MinorWaterEvidenceCameraAuthoring
    {
        private const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        private const string ManifestPath =
            "Assets/Production/Environment/ReconstructionV2/KBDU_WaterFeatureManifestV2.json";
        private const int Width = 1280;
        private const int Height = 720;

        public static void RunBatch()
        {
            string output = Environment.GetEnvironmentVariable("QFL_MINOR_WATER_EVIDENCE_DIR");
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("QFL_MINOR_WATER_EVIDENCE_DIR is required.");
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(Path.Combine(output, "captures"));
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            ProductionEnvironmentRoot environment = Object.FindFirstObjectByType<ProductionEnvironmentRoot>();
            AirportCoreBenchmarkRoot benchmark = Object.FindFirstObjectByType<AirportCoreBenchmarkRoot>();
            Material terrainMaterial = benchmark.minorWaterTerrainEffect.sharedMaterial;
            Texture2D maskAsset = terrainMaterial.GetTexture("_AirportCoreMinorWaterMask") as Texture2D;
            if (maskAsset == null) throw new InvalidOperationException("Committed minor-water mask is not bound.");
            Texture2D readableMask = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            readableMask.LoadImage(File.ReadAllBytes(Absolute(AssetDatabase.GetAssetPath(maskAsset))));
            WaterManifest manifest = JsonUtility.FromJson<WaterManifest>(File.ReadAllText(Absolute(ManifestPath)));

            List<WaterFeature> selected = SelectFeatures(manifest.represented_features);
            MinorWaterEvidenceReport report = new MinorWaterEvidenceReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                scenePath = ScenePath,
                maskPath = AssetDatabase.GetAssetPath(maskAsset),
                maskWidth = readableMask.width,
                maskHeight = readableMask.height,
                terrainMaterial = terrainMaterial.name,
                terrainMaterialPath = AssetDatabase.GetAssetPath(terrainMaterial),
                terrainShader = terrainMaterial.shader.name,
                activeRibbonRendererCount = environment.GetComponentsInChildren<Renderer>(true).Count(renderer =>
                    renderer.name.IndexOf("OtherWaterFeatures", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    renderer.enabled && renderer.gameObject.activeInHierarchy)
            };

            MeshRenderer[] terrainRenderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(renderer => renderer.sharedMaterials.Contains(terrainMaterial)).ToArray();
            Material debugMaterial = new Material(Shader.Find("Hidden/QuestFlight/MinorWaterEvidence"));
            debugMaterial.CopyPropertiesFromMaterial(terrainMaterial);
            GameObject cameraObject = new GameObject("V431_MinorWaterEvidenceCamera")
                { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            ConfigureCamera(camera);
            bool originalFog = RenderSettings.fog;
            float originalStrength = terrainMaterial.GetFloat("_AirportCoreMinorWaterStrength");

            try
            {
                foreach (WaterFeature feature in selected)
                {
                    FeatureEvidence evidence = BuildFeatureEvidence(feature, readableMask, terrainRenderers, terrainMaterial);
                    report.features.Add(evidence);
                    foreach (EvidencePose pose in Enum.GetValues(typeof(EvidencePose)))
                    {
                        ConfigurePose(camera, evidence.worldPosition, evidence.terrainHeight, pose);
                        CameraEvidence cameraEvidence = new CameraEvidence
                        {
                            pose = pose.ToString(),
                            position = camera.transform.position,
                            rotation = camera.transform.rotation
                        };
                        string stem = Slug(feature.name) + "_" + pose.ToString().ToLowerInvariant();

                        terrainMaterial.SetFloat("_AirportCoreMinorWaterStrength", 0f);
                        string orthophotoPath = Capture(camera, Path.Combine(output, "captures", stem + "_orthophoto_only.png"));

                        terrainMaterial.SetFloat("_AirportCoreMinorWaterStrength", originalStrength);
                        string finalPath = Capture(camera, Path.Combine(output, "captures", stem + "_final.png"));

                        debugMaterial.SetFloat("_EvidenceZone", feature.zone.StartsWith("near", StringComparison.Ordinal) ? 0f : 1f);
                        Material[] originals = terrainRenderers.Select(renderer => renderer.sharedMaterial).ToArray();
                        RenderSettings.fog = false;
                        for (int index = 0; index < terrainRenderers.Length; index++)
                            terrainRenderers[index].sharedMaterial = debugMaterial;
                        string debugPath = Capture(camera, Path.Combine(output, "captures", stem + "_mask_debug.png"));
                        for (int index = 0; index < terrainRenderers.Length; index++)
                            terrainRenderers[index].sharedMaterial = originals[index];
                        RenderSettings.fog = originalFog;

                        cameraEvidence.orthophotoOnlyPath = orthophotoPath;
                        cameraEvidence.finalPath = finalPath;
                        cameraEvidence.maskDebugPath = debugPath;
                        MeasureImages(debugPath, finalPath, orthophotoPath, cameraEvidence);
                        evidence.cameras.Add(cameraEvidence);
                    }
                }
                report.maskDebugContactSheet = BuildContactSheet(report, output, "mask_debug", item => item.maskDebugPath);
                report.finalContactSheet = BuildContactSheet(report, output, "final", item => item.finalPath);
                report.orthophotoContactSheet = BuildContactSheet(report, output, "orthophoto_only", item => item.orthophotoOnlyPath);
            }
            finally
            {
                terrainMaterial.SetFloat("_AirportCoreMinorWaterStrength", originalStrength);
                RenderSettings.fog = originalFog;
                Object.DestroyImmediate(debugMaterial);
                Object.DestroyImmediate(readableMask);
                Object.DestroyImmediate(cameraObject);
            }

            File.WriteAllText(Path.Combine(output, "minor_water_evidence.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(output, "minor_water_visual_review.md"), BuildMarkdown(report));
            EditorApplication.Exit(0);
        }

        private static List<WaterFeature> SelectFeatures(WaterFeature[] features)
        {
            WaterFeature near = features.Where(feature => feature.zone.StartsWith("near") && feature.area_square_meters > 0)
                .OrderByDescending(feature => feature.area_square_meters).First();
            WaterFeature mid = features.Where(feature => feature.zone.StartsWith("mid") && feature.area_square_meters > 0 &&
                    feature.name != "Boulder Reservoir")
                .OrderByDescending(feature => feature.area_square_meters).First();
            WaterFeature midSecondary = features.Where(feature => feature.zone.StartsWith("mid") && feature.area_square_meters > 0 &&
                    feature.name != "Boulder Reservoir" && feature.source_feature_id != mid.source_feature_id)
                .OrderByDescending(feature => feature.area_square_meters).First();
            return new List<WaterFeature> { near, mid, midSecondary };
        }

        private static FeatureEvidence BuildFeatureEvidence(WaterFeature feature, Texture2D mask,
            MeshRenderer[] terrainRenderers, Material terrainMaterial)
        {
            bool near = feature.zone.StartsWith("near", StringComparison.Ordinal);
            float minimum = near ? -2000f : -6000f;
            float size = near ? 4000f : 12000f;
            int channel = near ? 0 : 2;
            Vector2[] featurePoints = FeaturePoints(feature);
            float minX = featurePoints.Min(point => point.x);
            float maxX = featurePoints.Max(point => point.x);
            float minZ = featurePoints.Min(point => point.y);
            float maxZ = featurePoints.Max(point => point.y);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - minimum) / size * mask.width), 0, mask.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - minimum) / size * mask.width), 0, mask.width - 1);
            int rasterY0 = Mathf.Clamp(Mathf.FloorToInt((minZ - minimum) / size * mask.height), 0, mask.height - 1);
            int rasterY1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - minimum) / size * mask.height), 0, mask.height - 1);
            // LoadImage exposes encoded PNG rows in the opposite vertical order from the
            // world-space mask raster used by the imported shader texture.
            int y0 = mask.height - 1 - rasterY1;
            int y1 = mask.height - 1 - rasterY0;
            int count = 0;
            float sum = 0f, maximum = 0f;
            int bestX = x0, bestY = y0;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int rasterY = mask.height - 1 - y;
                Vector2 sampleWorld = new Vector2(minimum + (x + 0.5f) / mask.width * size,
                    minimum + (rasterY + 0.5f) / mask.height * size);
                if (!PointInPolygon(sampleWorld, featurePoints)) continue;
                Color pixel = mask.GetPixel(x, y);
                float value = channel == 0 ? pixel.r : pixel.b;
                if (value <= 0.01f) continue;
                count++;
                sum += value;
                if (value > maximum) { maximum = value; bestX = x; bestY = y; }
            }
            if (count == 0) throw new InvalidOperationException($"Feature {feature.source_feature_id} has no nonzero packed mask sample.");
            int bestRasterY = mask.height - 1 - bestY;
            Vector2 world = new Vector2(minimum + (bestX + 0.5f) / mask.width * size,
                minimum + (bestRasterY + 0.5f) / mask.height * size);
            List<(MeshRenderer renderer, float height)> sampledRenderers = terrainRenderers
                .Select(candidate => (renderer: candidate, success: SampleHeight(candidate.GetComponent<MeshFilter>(), world, out float height), height))
                .Where(candidate => candidate.success)
                .Select(candidate => (candidate.renderer, candidate.height)).ToList();
            (MeshRenderer renderer, float height) selectedRenderer = sampledRenderers.Count > 0
                ? sampledRenderers.OrderBy(candidate => candidate.renderer.bounds.size.x * candidate.renderer.bounds.size.z).First()
                : (terrainRenderers.OrderBy(candidate => DistanceToBoundsXZ(candidate.bounds, world)).First(), 0f);
            MeshRenderer renderer = selectedRenderer.renderer;
            float terrainHeight = sampledRenderers.Count > 0 ? selectedRenderer.height : renderer.bounds.max.y;
            return new FeatureEvidence
            {
                sourceFeatureId = feature.source_feature_id,
                stableId = feature.stable_id,
                name = feature.name,
                zone = feature.zone,
                sourceLicenseClass = feature.source_license_class,
                areaSquareMeters = feature.area_square_meters,
                worldPosition = world,
                terrainHeight = terrainHeight,
                maskPixelAreaInFeatureBounds = count,
                maskMaximum = maximum,
                maskMean = sum / count,
                terrainRenderer = HierarchyPath(renderer.transform),
                terrainMaterial = terrainMaterial.name,
                terrainMaterialPath = AssetDatabase.GetAssetPath(terrainMaterial),
                terrainUv = new Vector2((world.x - minimum) / size, (world.y - minimum) / size)
            };
        }

        private static void ConfigureCamera(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 48f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 15000f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.useOcclusionCulling = false;
        }

        private static void ConfigurePose(Camera camera, Vector2 world, float terrainHeight, EvidencePose pose)
        {
            Vector3 target = new Vector3(world.x, terrainHeight + 0.25f, world.y);
            Vector3 position;
            switch (pose)
            {
                case EvidencePose.Ground:
                    position = target + new Vector3(-22f, 3.5f, -18f);
                    break;
                case EvidencePose.Oblique:
                    position = target + new Vector3(-70f, 38f, -62f);
                    break;
                default:
                    position = target + new Vector3(0f, 230f, -12f);
                    break;
            }
            Vector3 up = pose == EvidencePose.Aerial ? Vector3.forward : Vector3.up;
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation((target - position).normalized, up));
        }

        private static string Capture(Camera camera, string path)
        {
            RenderTexture rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB, 4);
            RenderTexture previousActive = RenderTexture.active;
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            Texture2D capture = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            capture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            capture.Apply(false, false);
            File.WriteAllBytes(path, capture.EncodeToPNG());
            Object.DestroyImmediate(capture);
            camera.targetTexture = null;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
            return path;
        }

        private static void MeasureImages(string debugPath, string finalPath, string orthophotoPath, CameraEvidence evidence)
        {
            Texture2D debug = LoadCapture(debugPath), final = LoadCapture(finalPath), ortho = LoadCapture(orthophotoPath);
            Color32[] debugPixels = debug.GetPixels32(), finalPixels = final.GetPixels32(), orthoPixels = ortho.GetPixels32();
            double delta = 0;
            for (int index = 0; index < debugPixels.Length; index++)
            {
                Color32 d = debugPixels[index];
                if (d.g > 190 && d.b > 190 && d.r < 90) evidence.debugCyanPixelCount++;
                Color32 a = finalPixels[index], b = orthoPixels[index];
                int difference = Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);
                delta += difference / (255.0 * 3.0);
                if (difference > 18) evidence.finalAffectedPixelCount++;
            }
            evidence.meanFinalVsOrthophotoDifference = (float)(delta / debugPixels.Length);
            Object.DestroyImmediate(debug); Object.DestroyImmediate(final); Object.DestroyImmediate(ortho);
        }

        private static Texture2D LoadCapture(string path)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        private static string BuildContactSheet(MinorWaterEvidenceReport report, string output, string suffix,
            Func<CameraEvidence, string> select)
        {
            List<string> paths = report.features.SelectMany(feature => feature.cameras).Select(select).ToList();
            int columns = 3, rows = Mathf.CeilToInt(paths.Count / 3f), thumbWidth = 426, thumbHeight = 240;
            Texture2D sheet = new Texture2D(columns * thumbWidth, rows * thumbHeight, TextureFormat.RGB24, false);
            for (int index = 0; index < paths.Count; index++)
            {
                Texture2D source = LoadCapture(paths[index]);
                Color[] scaled = new Color[thumbWidth * thumbHeight];
                for (int y = 0; y < thumbHeight; y++)
                for (int x = 0; x < thumbWidth; x++)
                    scaled[y * thumbWidth + x] = source.GetPixelBilinear((x + 0.5f) / thumbWidth, (y + 0.5f) / thumbHeight);
                int column = index % columns, row = rows - 1 - index / columns;
                sheet.SetPixels(column * thumbWidth, row * thumbHeight, thumbWidth, thumbHeight, scaled);
                Object.DestroyImmediate(source);
            }
            sheet.Apply();
            string path = Path.Combine(output, "minor_water_" + suffix + "_contact_sheet.png");
            File.WriteAllBytes(path, sheet.EncodeToPNG()); Object.DestroyImmediate(sheet); return path;
        }

        private static string BuildMarkdown(MinorWaterEvidenceReport report)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.AppendLine("# Minor-water visual proof\n");
            text.AppendLine($"- Mask: `{report.maskPath}` ({report.maskWidth} x {report.maskHeight})");
            text.AppendLine($"- Bound material/shader: `{report.terrainMaterialPath}` / `{report.terrainShader}`");
            text.AppendLine($"- Active ribbon renderers: {report.activeRibbonRendererCount}");
            foreach (FeatureEvidence feature in report.features)
            {
                text.AppendLine($"\n## {feature.name} — `{feature.sourceFeatureId}`\n");
                text.AppendLine($"- Zone/world: {feature.zone} / ({feature.worldPosition.x:0.0}, {feature.worldPosition.y:0.0}) m");
                text.AppendLine($"- Mask pixels/max/mean: {feature.maskPixelAreaInFeatureBounds} / {feature.maskMaximum:0.000} / {feature.maskMean:0.000}");
                text.AppendLine($"- Terrain renderer: `{feature.terrainRenderer}`");
                foreach (CameraEvidence camera in feature.cameras)
                    text.AppendLine($"- {camera.pose}: cyan={camera.debugCyanPixelCount}, final affected={camera.finalAffectedPixelCount}, mean final delta={camera.meanFinalVsOrthophotoDifference:0.00000}");
            }
            return text.ToString();
        }

        private static bool SampleHeight(MeshFilter filter, Vector2 world, out float height)
        {
            if (filter == null || filter.sharedMesh == null) { height = 0f; return false; }
            Vector3 local = filter.transform.InverseTransformPoint(new Vector3(world.x, 0f, world.y));
            Vector3[] vertices = filter.sharedMesh.vertices; int[] triangles = filter.sharedMesh.triangles;
            for (int offset = 0; offset < triangles.Length; offset += 3)
            {
                Vector3 a = vertices[triangles[offset]], b = vertices[triangles[offset + 1]], c = vertices[triangles[offset + 2]];
                float denominator = (b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
                if (Mathf.Abs(denominator) < 1e-8f) continue;
                float wa=((b.z-c.z)*(local.x-c.x)+(c.x-b.x)*(local.z-c.z))/denominator;
                float wb=((c.z-a.z)*(local.x-c.x)+(a.x-c.x)*(local.z-c.z))/denominator;
                float wc=1f-wa-wb;
                if (wa < -1e-5f || wb < -1e-5f || wc < -1e-5f) continue;
                height=filter.transform.TransformPoint(new Vector3(local.x,wa*a.y+wb*b.y+wc*c.y,local.z)).y; return true;
            }
            height=0f; return false;
        }

        private static float DistanceToBoundsXZ(Bounds bounds, Vector2 point)
        {
            float dx = Mathf.Max(bounds.min.x - point.x, 0f, point.x - bounds.max.x);
            float dz = Mathf.Max(bounds.min.z - point.y, 0f, point.y - bounds.max.z);
            return dx * dx + dz * dz;
        }

        private static bool PointInPolygon(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int current = 0, previous = polygon.Length - 1; current < polygon.Length; previous = current++)
            {
                Vector2 a = polygon[current], b = polygon[previous];
                if ((a.y > point.y) == (b.y > point.y)) continue;
                float crossingX = (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
                if (point.x < crossingX) inside = !inside;
            }
            return inside;
        }

        private static string HierarchyPath(Transform transform)
        { List<string> parts=new List<string>(); while(transform!=null){parts.Add(transform.name);transform=transform.parent;} parts.Reverse();return string.Join("/",parts); }
        private static Vector2[] FeaturePoints(WaterFeature feature)
        {
            if (feature.points_enu_flat_meters == null || feature.points_enu_flat_meters.Length < 4 ||
                feature.points_enu_flat_meters.Length % 2 != 0)
                throw new InvalidOperationException($"Feature {feature.source_feature_id} has no valid flat ENU coordinates.");
            Vector2[] points = new Vector2[feature.points_enu_flat_meters.Length / 2];
            for (int index = 0; index < points.Length; index++)
                points[index] = new Vector2(feature.points_enu_flat_meters[index * 2],
                    feature.points_enu_flat_meters[index * 2 + 1]);
            return points;
        }
        private static string Slug(string value) => new string((string.IsNullOrWhiteSpace(value) ? "water_feature" : value)
            .ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray()).Trim('_');
        private static string Absolute(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        private enum EvidencePose { Ground, Oblique, Aerial }

        [Serializable] private sealed class WaterManifest { public WaterFeature[] represented_features; }
        [Serializable] private sealed class WaterFeature
        { public string source_feature_id, stable_id, name, zone, source_license_class; public float area_square_meters; public float[] points_enu_flat_meters; }
        [Serializable] private sealed class MinorWaterEvidenceReport
        {
            public string generatedUtc, unityVersion, scenePath, maskPath, terrainMaterial, terrainMaterialPath, terrainShader;
            public int maskWidth, maskHeight, activeRibbonRendererCount;
            public string maskDebugContactSheet, finalContactSheet, orthophotoContactSheet;
            public List<FeatureEvidence> features = new List<FeatureEvidence>();
        }
        [Serializable] private sealed class FeatureEvidence
        {
            public string sourceFeatureId, stableId, name, zone, sourceLicenseClass, terrainRenderer, terrainMaterial, terrainMaterialPath;
            public float areaSquareMeters, terrainHeight, maskMaximum, maskMean;
            public Vector2 worldPosition, terrainUv;
            public int maskPixelAreaInFeatureBounds;
            public List<CameraEvidence> cameras = new List<CameraEvidence>();
        }
        [Serializable] private sealed class CameraEvidence
        {
            public string pose, maskDebugPath, finalPath, orthophotoOnlyPath;
            public Vector3 position; public Quaternion rotation;
            public int debugCyanPixelCount, finalAffectedPixelCount;
            public float meanFinalVsOrthophotoDifference;
        }
    }
}
