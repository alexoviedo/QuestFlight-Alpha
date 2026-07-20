using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace QuestFlight.Editor
{
    public static class RunwayBandIsolationRunner
    {
        private const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        private const int Width = 1280;
        private const int Height = 720;

        public static void RunBatch()
        {
            string output = Environment.GetEnvironmentVariable("QFL_RUNWAY_ISOLATION_DIR");
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("QFL_RUNWAY_ISOLATION_DIR is required.");
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(Path.Combine(output, "captures"));

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ProductionEnvironmentRoot environment = Object.FindFirstObjectByType<ProductionEnvironmentRoot>();
            AirportCoreBenchmarkRoot benchmark = Object.FindFirstObjectByType<AirportCoreBenchmarkRoot>();
            if (environment == null || benchmark == null)
                throw new InvalidOperationException("Authored production environment or benchmark root is missing.");

            MeshRenderer pavement = environment.runwayPavement.GetComponent<MeshRenderer>();
            MeshFilter markingsFilter = environment.runwayMarkings.GetComponent<MeshFilter>();
            Renderer terrain = benchmark.minorWaterTerrainEffect;
            if (pavement == null || markingsFilter == null || terrain == null)
                throw new InvalidOperationException("Runway/marking/terrain renderer binding is incomplete.");

            RunwayIsolationReport report = BuildInventory(environment, pavement, markingsFilter, terrain);
            GameObject cameraObject = new GameObject("V431_RunwayIsolationCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            ConfigureCamera(camera);
            SetCamera(camera, environment, CameraPose.LowOblique);
            report.runwayPolygonPixels = ProjectRunwayPolygon(camera, environment);
            report.runwayCenterlinePixels = ProjectRunwayCenterline(camera, environment, 5f);

            Material originalPavement = pavement.sharedMaterial;
            Material originalTerrain = terrain.sharedMaterial;
            bool originalMarkings = environment.runwayMarkings.enabled;
            Material isolation = new Material(Shader.Find("Hidden/QuestFlight/RunwayBandIsolation"));
            isolation.CopyPropertiesFromMaterial(originalPavement);
            Material unlitWhite = new Material(Shader.Find("Unlit/Color"));
            unlitWhite.color = Color.white;
            Material terrainDebug = new Material(Shader.Find("Unlit/Color"));
            terrainDebug.color = new Color(0.18f, 0.18f, 0.18f, 1f);

            try
            {
                for (int mode = 0; mode <= 11; mode++)
                {
                    ApplyMode(mode, pavement, environment.runwayMarkings, terrain, originalPavement,
                        originalTerrain, isolation, unlitWhite, terrainDebug);
                    string id = "R" + mode.ToString("00", CultureInfo.InvariantCulture);
                    string path = Path.Combine(output, "captures", id + ".png");
                    Texture2D capture = Render(camera);
                    File.WriteAllBytes(path, capture.EncodeToPNG());
                    report.modes.Add(new IsolationModeResult
                    {
                        id = id,
                        description = ModeDescription(mode),
                        imagePath = path,
                        pavementMaterial = pavement.sharedMaterial.name,
                        pavementShader = pavement.sharedMaterial.shader.name,
                        markingsEnabled = environment.runwayMarkings.enabled,
                        terrainMaterial = terrain.sharedMaterial.name,
                        terrainShader = terrain.sharedMaterial.shader.name
                    });
                    Object.DestroyImmediate(capture);
                }

                pavement.sharedMaterial = originalPavement;
                terrain.sharedMaterial = originalTerrain;
                environment.runwayMarkings.enabled = originalMarkings;
                report.frameDebugger = CaptureFrameDebuggerViews(camera, environment, pavement, markingsFilter, terrain);
                report.contactSheetPath = BuildContactSheet(report.modes, output);
            }
            finally
            {
                pavement.sharedMaterial = originalPavement;
                terrain.sharedMaterial = originalTerrain;
                environment.runwayMarkings.enabled = originalMarkings;
                Object.DestroyImmediate(isolation);
                Object.DestroyImmediate(unlitWhite);
                Object.DestroyImmediate(terrainDebug);
                Object.DestroyImmediate(cameraObject);
            }

            File.WriteAllText(Path.Combine(output, "runway_isolation_report.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(output, "runway_draw_events.json"), JsonUtility.ToJson(report.frameDebugger, true));
            File.WriteAllText(Path.Combine(output, "runway_frame_debugger_report.md"), BuildFrameDebuggerMarkdown(report));
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }

        public static void RunInteractiveFrameDebuggerCapture()
        {
            string output = Environment.GetEnvironmentVariable("QFL_FRAME_DEBUGGER_DIR");
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("QFL_FRAME_DEBUGGER_DIR is required.");
            Directory.CreateDirectory(output);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.delayCall += () =>
            {
                try
                {
                    ProductionEnvironmentRoot environment = Object.FindFirstObjectByType<ProductionEnvironmentRoot>();
                    AirportCoreBenchmarkRoot benchmark = Object.FindFirstObjectByType<AirportCoreBenchmarkRoot>();
                    MeshRenderer pavement = environment.runwayPavement.GetComponent<MeshRenderer>();
                    MeshFilter markings = environment.runwayMarkings.GetComponent<MeshFilter>();
                    Renderer terrain = benchmark.minorWaterTerrainEffect;
                    RunwayIsolationReport report = BuildInventory(environment, pavement, markings, terrain);
                    GameObject cameraObject = new GameObject("V431_InteractiveFrameDebuggerCamera")
                        { hideFlags = HideFlags.HideAndDontSave };
                    Camera camera = cameraObject.AddComponent<Camera>();
                    ConfigureCamera(camera);
                    report.frameDebugger = CaptureFrameDebuggerViews(camera, environment, pavement, markings, terrain);
                    File.WriteAllText(Path.Combine(output, "interactive_runway_draw_events.json"),
                        JsonUtility.ToJson(report.frameDebugger, true));
                    File.WriteAllText(Path.Combine(output, "interactive_runway_frame_debugger_report.md"),
                        BuildFrameDebuggerMarkdown(report));
                    Object.DestroyImmediate(cameraObject);
                    EditorApplication.Exit(report.frameDebugger.supported ? 0 : 2);
                }
                catch (Exception exception)
                {
                    File.WriteAllText(Path.Combine(output, "interactive_frame_debugger_error.txt"), exception.ToString());
                    EditorApplication.Exit(1);
                }
            };
        }

        private static RunwayIsolationReport BuildInventory(ProductionEnvironmentRoot environment, MeshRenderer pavement,
            MeshFilter markings, Renderer terrain)
        {
            Renderer[] all = environment.GetComponentsInChildren<Renderer>(true);
            Renderer[] pavementCandidates = all.Where(renderer =>
                renderer.name.IndexOf("runway", StringComparison.OrdinalIgnoreCase) >= 0 &&
                renderer.name.IndexOf("mark", StringComparison.OrdinalIgnoreCase) < 0).ToArray();
            Renderer[] markingCandidates = all.Where(renderer =>
                renderer.name.IndexOf("mark", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            RunwayIsolationReport report = new RunwayIsolationReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                scenePath = ScenePath,
                pavement = RendererRecord.From(pavement, environment.runwayPavement.sharedMesh),
                markings = RendererRecord.From(environment.runwayMarkings, markings.sharedMesh),
                terrain = RendererRecord.From(terrain, terrain.GetComponent<MeshFilter>()?.sharedMesh),
                activeRunwayNamedRenderers = pavementCandidates.Count(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy),
                activeMarkingNamedRenderers = markingCandidates.Count(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy),
                pavementCandidates = pavementCandidates.Select(renderer => HierarchyPath(renderer.transform)).ToArray(),
                markingCandidates = markingCandidates.Select(renderer => HierarchyPath(renderer.transform)).ToArray(),
                noObsoleteSlabRenderer = all.All(renderer =>
                    renderer.name.IndexOf("slab", StringComparison.OrdinalIgnoreCase) < 0 &&
                    renderer.name.IndexOf("featuremask", StringComparison.OrdinalIgnoreCase) < 0),
                pavementAndColliderShareMesh = ReferenceEquals(environment.runwayPavement.sharedMesh,
                    environment.runwayCollisionSurface.sharedMesh),
                serializedRunwayGapMeters = environment.measuredRunwayToTerrainMaximumGapMeters,
                serializedMarkingGapMeters = environment.measuredMarkingToRunwayMaximumGapMeters,
                serializedColliderDisagreementMeters = environment.measuredCollisionSurfaceDisagreementMeters
            };
            report.pavement.assetPath = AssetDatabase.GetAssetPath(environment.runwayPavement.sharedMesh);
            report.pavementTopology = AnalyzePavementTopology(environment, terrain);
            return report;
        }

        private static void ApplyMode(int mode, MeshRenderer pavement, MeshRenderer markings, Renderer terrain,
            Material original, Material originalTerrain, Material isolation, Material unlitWhite, Material terrainDebug)
        {
            terrain.sharedMaterial = mode == 11 ? terrainDebug : originalTerrain;
            markings.enabled = mode == 0 || mode == 2 || mode == 9 || mode == 10 || mode == 11;
            if (mode == 0 || mode == 8 || mode == 9 || mode == 11)
            {
                pavement.sharedMaterial = original;
                return;
            }
            if (mode == 1 || mode == 2)
            {
                pavement.sharedMaterial = unlitWhite;
                return;
            }
            isolation.SetInt("_DiagnosticMode", mode);
            pavement.sharedMaterial = isolation;
        }

        private static string ModeDescription(int mode)
        {
            string[] values =
            {
                "final V4.3 material", "constant unlit white; markings disabled",
                "constant unlit white; markings enabled", "constant lit gray; no textures; markings disabled",
                "albedo only", "albedo plus roughness", "albedo plus normal", "albedo plus wear/patch response",
                "final pavement; markings disabled", "final pavement; markings enabled",
                "final pavement albedo with lighting disabled", "final pavement with terrain beneath flat debug color"
            };
            return values[mode];
        }

        private static void ConfigureCamera(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = Color.gray;
            camera.fieldOfView = 46f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 5000f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.useOcclusionCulling = false;
        }

        private enum CameraPose { Cockpit, LowOblique, Close, Aerial }

        private static void SetCamera(Camera camera, ProductionEnvironmentRoot env, CameraPose pose)
        {
            Vector3 start = env.runway08EndLocal;
            Vector3 end = env.runway26EndLocal;
            Vector3 axis = (end - start).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized;
            Vector3 position;
            Vector3 target;
            switch (pose)
            {
                case CameraPose.Cockpit:
                    position = start - axis * 28f + Vector3.up * 2.1f;
                    target = start + axis * 520f + Vector3.up * 0.4f;
                    break;
                case CameraPose.Close:
                    position = start + axis * 160f + right * 17f + Vector3.up * 3.2f;
                    target = start + axis * 330f;
                    break;
                case CameraPose.Aerial:
                    position = Vector3.Lerp(start, end, 0.50f) + right * 250f + Vector3.up * 330f;
                    target = Vector3.Lerp(start, end, 0.50f);
                    break;
                default:
                    position = start - axis * 52f + right * 7f + Vector3.up * 11f;
                    target = start + axis * 620f;
                    break;
            }
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation((target-position).normalized, Vector3.up));
        }

        private static Texture2D Render(Camera camera)
        {
            RenderTexture rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB, 4);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            Texture2D result = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            result.ReadPixels(new Rect(0,0,Width,Height), 0, 0);
            result.Apply(false, false);
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        private static PixelPoint[] ProjectRunwayPolygon(Camera camera, ProductionEnvironmentRoot env)
        {
            Vector3 start = env.runway08EndLocal;
            Vector3 end = env.runway26EndLocal;
            Vector3 axis = (end-start).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized * (env.runwayWidthMeters * 0.5f);
            return new[] { start-right, start+right, end+right, end-right }.Select(point =>
            {
                Vector3 viewport = camera.WorldToViewportPoint(point);
                return new PixelPoint { x = viewport.x * Width, y = (1f-viewport.y) * Height, depth = viewport.z };
            }).ToArray();
        }

        private static CenterlinePixel[] ProjectRunwayCenterline(Camera camera, ProductionEnvironmentRoot env, float spacingMeters)
        {
            Vector3 start = env.runway08EndLocal;
            Vector3 end = env.runway26EndLocal;
            float length = Vector3.Distance(start, end);
            Vector3 axis = (end - start).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized * (env.runwayWidthMeters * 0.5f);
            Mesh runwayMesh = env.runwayPavement.sharedMesh;
            Vector3[] runwayVertices = runwayMesh.vertices;
            List<Vector3> stationCenters = new List<Vector3>();
            for (int index = 0; index + 1 < runwayVertices.Length; index += 2)
            {
                stationCenters.Add(env.runwayPavement.transform.TransformPoint(
                    (runwayVertices[index] + runwayVertices[index + 1]) * 0.5f));
            }
            stationCenters = stationCenters.OrderBy(point => Vector3.Dot(point - start, axis)).ToList();
            int count = Mathf.CeilToInt(length / spacingMeters) + 1;
            CenterlinePixel[] result = new CenterlinePixel[count];
            for (int index = 0; index < count; index++)
            {
                float distance = Mathf.Min(index * spacingMeters, length);
                float stationPosition = distance / length * (stationCenters.Count - 1);
                int stationIndex = Mathf.Clamp(Mathf.FloorToInt(stationPosition), 0, stationCenters.Count - 2);
                Vector3 center = Vector3.Lerp(stationCenters[stationIndex], stationCenters[stationIndex + 1],
                    stationPosition - stationIndex);
                Vector3 screen = camera.WorldToViewportPoint(center);
                Vector3 leftScreen = camera.WorldToViewportPoint(center - right);
                Vector3 rightScreen = camera.WorldToViewportPoint(center + right);
                result[index] = new CenterlinePixel
                {
                    distanceMeters = distance,
                    x = screen.x * Width,
                    y = (1f - screen.y) * Height,
                    depth = screen.z,
                    leftX = leftScreen.x * Width,
                    leftY = (1f - leftScreen.y) * Height,
                    rightX = rightScreen.x * Width,
                    rightY = (1f - rightScreen.y) * Height
                };
            }
            return result;
        }

        private static PavementTopologyReport AnalyzePavementTopology(ProductionEnvironmentRoot environment, Renderer terrain)
        {
            Mesh mesh = environment.runwayPavement.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Transform transform = environment.runwayPavement.transform;
            Vector3 runwayStart = environment.runway08EndLocal;
            Vector3 runwayEnd = environment.runway26EndLocal;
            Vector3 runwayAxis = (runwayEnd - runwayStart).normalized;
            PavementTopologyReport result = new PavementTopologyReport
            {
                triangleCount = triangles.Length / 3,
                vertexCount = vertices.Length,
                meshAssetPath = AssetDatabase.GetAssetPath(mesh),
                minimumSignedUpDot = float.PositiveInfinity,
                maximumSignedUpDot = float.NegativeInfinity,
                minimumTerrainClearanceMeters = float.PositiveInfinity,
                maximumTerrainClearanceMeters = float.NegativeInfinity
            };
            for (int offset = 0; offset < triangles.Length; offset += 3)
            {
                Vector3 a = transform.TransformPoint(vertices[triangles[offset]]);
                Vector3 b = transform.TransformPoint(vertices[triangles[offset + 1]]);
                Vector3 c = transform.TransformPoint(vertices[triangles[offset + 2]]);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float signedUp = cross.sqrMagnitude < 1e-12f ? 0f : Vector3.Dot(cross.normalized, Vector3.up);
                result.minimumSignedUpDot = Mathf.Min(result.minimumSignedUpDot, signedUp);
                result.maximumSignedUpDot = Mathf.Max(result.maximumSignedUpDot, signedUp);
                if (signedUp > 1e-4f) result.upwardTriangles++;
                else if (signedUp < -1e-4f) result.downwardTriangles++;
                else result.degenerateTriangles++;
                Vector3 centroid = (a + b + c) / 3f;
                result.triangles.Add(new TriangleTopologyRecord
                {
                    triangleIndex = offset / 3,
                    signedUpDot = signedUp,
                    longitudinalMeters = Vector3.Dot(centroid - runwayStart, runwayAxis),
                    centroidWorld = centroid
                });
            }

            MeshFilter terrainFilter = terrain.GetComponent<MeshFilter>();
            if (terrainFilter != null && terrainFilter.sharedMesh != null)
            {
                foreach (Vector3 local in vertices)
                {
                    Vector3 world = transform.TransformPoint(local);
                    if (!TrySampleMeshHeightXZ(terrainFilter, world, out float terrainY)) continue;
                    float clearance = world.y - terrainY;
                    result.sampledTerrainVertices++;
                    if (clearance < 0f) result.verticesBelowTerrain++;
                    result.minimumTerrainClearanceMeters = Mathf.Min(result.minimumTerrainClearanceMeters, clearance);
                    result.maximumTerrainClearanceMeters = Mathf.Max(result.maximumTerrainClearanceMeters, clearance);
                }
            }
            if (result.sampledTerrainVertices == 0)
            {
                result.minimumTerrainClearanceMeters = 0f;
                result.maximumTerrainClearanceMeters = 0f;
            }
            return result;
        }

        private static bool TrySampleMeshHeightXZ(MeshFilter filter, Vector3 world, out float height)
        {
            Mesh mesh = filter.sharedMesh;
            Vector3 local = filter.transform.InverseTransformPoint(world);
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int offset = 0; offset < triangles.Length; offset += 3)
            {
                Vector3 a = vertices[triangles[offset]];
                Vector3 b = vertices[triangles[offset + 1]];
                Vector3 c = vertices[triangles[offset + 2]];
                float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(denominator) < 1e-8f) continue;
                float wa = ((b.z - c.z) * (local.x - c.x) + (c.x - b.x) * (local.z - c.z)) / denominator;
                float wb = ((c.z - a.z) * (local.x - c.x) + (a.x - c.x) * (local.z - c.z)) / denominator;
                float wc = 1f - wa - wb;
                if (wa < -1e-5f || wb < -1e-5f || wc < -1e-5f) continue;
                Vector3 sampledLocal = new Vector3(local.x, wa * a.y + wb * b.y + wc * c.y, local.z);
                height = filter.transform.TransformPoint(sampledLocal).y;
                return true;
            }
            height = 0f;
            return false;
        }

        private static FrameDebuggerReport CaptureFrameDebuggerViews(Camera camera, ProductionEnvironmentRoot env,
            Renderer pavement, MeshFilter markings, Renderer terrain)
        {
            FrameDebuggerReport report = new FrameDebuggerReport();
            Type utility = FindType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");
            Type dataType = FindType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData");
            if (utility == null || dataType == null)
            {
                report.supported = false;
                report.error = "Installed Frame Debugger reflection types are unavailable.";
                return report;
            }
            try
            {
                MethodInfo setEnabled = utility.GetMethod("SetEnabled", BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                MethodInfo getEvents = utility.GetMethod("GetFrameEvents", BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                MethodInfo getData = utility.GetMethod("GetFrameEventData", BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                MethodInfo getObject = utility.GetMethod("GetFrameEventObject", BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                PropertyInfo count = utility.GetProperty("count", BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                setEnabled.Invoke(null, new object[] { true, 0 });
                foreach (CameraPose pose in Enum.GetValues(typeof(CameraPose)))
                {
                    SetCamera(camera, env, pose);
                    camera.Render();
                    Array events = (Array)getEvents.Invoke(null, null);
                    FrameDebuggerView view = new FrameDebuggerView { id = pose.ToString(), totalEvents = (int)count.GetValue(null) };
                    for (int index=0; index<events.Length; index++)
                    {
                        object data = Activator.CreateInstance(dataType, true);
                        bool ok = (bool)getData.Invoke(null, new[] { (object)index, data });
                        Object eventObject = getObject.Invoke(null, new object[] { index }) as Object;
                        FrameEventRecord record = FrameEventRecord.From(index, events.GetValue(index), data, eventObject);
                        if (RecordTouchesRunway(record, pavement, markings.sharedMesh, terrain)) view.events.Add(record);
                    }
                    report.views.Add(view);
                }
                setEnabled.Invoke(null, new object[] { false, 0 });
                report.supported = report.views.Any(view => view.totalEvents > 0);
                if (!report.supported)
                    report.error = "Installed API is present, but the Editor did not populate local frame events in this execution mode.";
            }
            catch (Exception exception)
            {
                report.supported = false;
                report.error = exception.ToString();
                try { utility.GetMethod("SetEnabled", BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(null, new object[] { false, 0 }); }
                catch { }
            }
            return report;
        }

        private static bool RecordTouchesRunway(FrameEventRecord record, Renderer pavement, Mesh markingMesh, Renderer terrain)
        {
            int pavementId = pavement.GetInstanceID();
            int markingId = markingMesh.GetInstanceID();
            int terrainId = terrain.GetInstanceID();
            return record.componentInstanceId == pavementId || record.meshInstanceId == markingId ||
                   record.componentInstanceId == terrainId || Contains(record.objectName, "Runway") ||
                   Contains(record.meshName, "Runway") || Contains(record.shaderName, "Runway") ||
                   Contains(record.objectName, "Terrain") || Contains(record.meshName, "AirportTerrain");
        }

        private static bool Contains(string value, string token) =>
            !string.IsNullOrEmpty(value) && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        private static Type FindType(string fullName) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, false)).FirstOrDefault(candidate => candidate != null);

        private static string BuildContactSheet(List<IsolationModeResult> modes, string output)
        {
            int thumbWidth = 320, thumbHeight = 180, columns = 4, rows = 3;
            Texture2D sheet = new Texture2D(thumbWidth*columns, thumbHeight*rows, TextureFormat.RGB24, false);
            for (int index=0; index<modes.Count; index++)
            {
                Texture2D source = new Texture2D(2,2);
                source.LoadImage(File.ReadAllBytes(modes[index].imagePath));
                Color[] pixels = source.GetPixels(0,0,source.width,source.height);
                Texture2D scaled = new Texture2D(thumbWidth,thumbHeight,TextureFormat.RGB24,false);
                for(int y=0;y<thumbHeight;y++) for(int x=0;x<thumbWidth;x++)
                    scaled.SetPixel(x,y,source.GetPixelBilinear((x+0.5f)/thumbWidth,(y+0.5f)/thumbHeight));
                scaled.Apply();
                int column=index%columns, row=rows-1-index/columns;
                sheet.SetPixels(column*thumbWidth,row*thumbHeight,thumbWidth,thumbHeight,scaled.GetPixels());
                Object.DestroyImmediate(source); Object.DestroyImmediate(scaled);
            }
            sheet.Apply();
            string path=Path.Combine(output,"runway_isolation_contact_sheet.png");
            File.WriteAllBytes(path,sheet.EncodeToPNG()); Object.DestroyImmediate(sheet); return path;
        }

        private static string BuildFrameDebuggerMarkdown(RunwayIsolationReport report)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.AppendLine("# Runway Frame Debugger report\n");
            text.AppendLine($"- Unity: `{report.unityVersion}`");
            text.AppendLine($"- Pavement: `{report.pavement.path}` / `{report.pavement.mesh}` / `{report.pavement.material}` / `{report.pavement.shader}`");
            text.AppendLine($"- Markings: `{report.markings.path}` / `{report.markings.mesh}` / `{report.markings.material}` / `{report.markings.shader}`");
            text.AppendLine($"- Terrain beneath: `{report.terrain.path}` / `{report.terrain.material}` / `{report.terrain.shader}`");
            text.AppendLine($"- Active runway-named renderers: {report.activeRunwayNamedRenderers}; marking renderers: {report.activeMarkingNamedRenderers}");
            text.AppendLine($"- Pavement/collider exact mesh identity: {report.pavementAndColliderShareMesh}");
            text.AppendLine($"- Frame Debugger supported: {report.frameDebugger.supported}; error: `{report.frameDebugger.error}`\n");
            foreach (FrameDebuggerView view in report.frameDebugger.views)
            {
                text.AppendLine($"## {view.id} ({view.events.Count} relevant / {view.totalEvents} total events)\n");
                text.AppendLine("| Event | Object | Mesh/submesh | Shader/pass | Indices | Depth |\n|---:|---|---|---|---:|---|");
                foreach (FrameEventRecord item in view.events)
                    text.AppendLine($"| {item.index} | `{item.objectName}` | `{item.meshName}`/{item.submesh} | `{item.shaderName}` / `{item.passName}` | {item.indexCount} | `{item.depthState}` |");
                text.AppendLine();
            }
            return text.ToString();
        }

        private static string HierarchyPath(Transform transform)
        {
            List<string> parts = new List<string>();
            while(transform!=null){parts.Add(transform.name);transform=transform.parent;}
            parts.Reverse(); return string.Join("/",parts);
        }

        [Serializable] private sealed class RunwayIsolationReport
        {
            public string generatedUtc, unityVersion, scenePath, contactSheetPath;
            public RendererRecord pavement, markings, terrain;
            public int activeRunwayNamedRenderers, activeMarkingNamedRenderers;
            public string[] pavementCandidates, markingCandidates;
            public bool noObsoleteSlabRenderer, pavementAndColliderShareMesh;
            public float serializedRunwayGapMeters, serializedMarkingGapMeters, serializedColliderDisagreementMeters;
            public PixelPoint[] runwayPolygonPixels;
            public CenterlinePixel[] runwayCenterlinePixels;
            public PavementTopologyReport pavementTopology;
            public List<IsolationModeResult> modes = new List<IsolationModeResult>();
            public FrameDebuggerReport frameDebugger;
        }

        [Serializable] private sealed class RendererRecord
        {
            public string path, name, mesh, assetPath, material, shader, textureInputs, normalSource;
            public int vertexCount, triangleCount, submeshCount, lightmapIndex, staticBatchIndex;
            public bool enabled, active, receiveShadows, castShadows, isPartOfStaticBatch, instancing, hasVertexColors, hasNormals, hasTangents;
            public static RendererRecord From(Renderer renderer, Mesh mesh)
            {
                Material material=renderer.sharedMaterial;
                return new RendererRecord
                {
                    path=HierarchyPath(renderer.transform),name=renderer.name,mesh=mesh?.name,material=material?.name,
                    shader=material?.shader?.name,vertexCount=mesh?.vertexCount??0,
                    triangleCount=mesh==null?0:(int)(mesh.GetIndexCount(0)/3),submeshCount=mesh?.subMeshCount??0,
                    lightmapIndex=renderer.lightmapIndex,staticBatchIndex=ReadStaticBatchIndex(renderer),enabled=renderer.enabled,
                    active=renderer.gameObject.activeInHierarchy,receiveShadows=renderer.receiveShadows,
                    castShadows=renderer.shadowCastingMode!=ShadowCastingMode.Off,isPartOfStaticBatch=renderer.isPartOfStaticBatch,
                    instancing=material!=null&&material.enableInstancing,hasVertexColors=mesh!=null&&mesh.colors32.Length>0,
                    hasNormals=mesh!=null&&mesh.normals.Length==mesh.vertexCount,hasTangents=mesh!=null&&mesh.tangents.Length==mesh.vertexCount,
                    normalSource=mesh!=null&&mesh.normals.Length==mesh.vertexCount?"mesh normals plus shader normal mapping":"shader/world normal only",
                    textureInputs=material==null?"":string.Join("; ",Enumerable.Range(0,material.shader.GetPropertyCount())
                        .Where(index=>material.shader.GetPropertyType(index)==UnityEngine.Rendering.ShaderPropertyType.Texture)
                        .Select(index=>material.shader.GetPropertyName(index)+"="+(material.GetTexture(material.shader.GetPropertyName(index))?.name??"null")))
                };
            }

            private static int ReadStaticBatchIndex(Renderer renderer)
            {
                PropertyInfo property = typeof(Renderer).GetProperty("staticBatchIndex",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return property == null ? -1 : Convert.ToInt32(property.GetValue(renderer), CultureInfo.InvariantCulture);
            }
        }

        [Serializable] private sealed class IsolationModeResult
        { public string id, description, imagePath, pavementMaterial, pavementShader, terrainMaterial, terrainShader; public bool markingsEnabled; }
        [Serializable] private struct PixelPoint { public float x,y,depth; }
        [Serializable] private struct CenterlinePixel
        { public float distanceMeters,x,y,depth,leftX,leftY,rightX,rightY; }
        [Serializable] private sealed class PavementTopologyReport
        {
            public string meshAssetPath;
            public int vertexCount, triangleCount, upwardTriangles, downwardTriangles, degenerateTriangles;
            public float minimumSignedUpDot, maximumSignedUpDot;
            public int sampledTerrainVertices, verticesBelowTerrain;
            public float minimumTerrainClearanceMeters, maximumTerrainClearanceMeters;
            public List<TriangleTopologyRecord> triangles = new List<TriangleTopologyRecord>();
        }
        [Serializable] private sealed class TriangleTopologyRecord
        { public int triangleIndex; public float signedUpDot, longitudinalMeters; public Vector3 centroidWorld; }
        [Serializable] private sealed class FrameDebuggerReport { public bool supported; public string error; public List<FrameDebuggerView> views=new List<FrameDebuggerView>(); }
        [Serializable] private sealed class FrameDebuggerView { public string id; public int totalEvents; public List<FrameEventRecord> events=new List<FrameEventRecord>(); }

        [Serializable] private sealed class FrameEventRecord
        {
            public int index, componentInstanceId, meshInstanceId, submesh, indexCount, vertexCount, instanceCount, shaderPassIndex;
            public string eventType, objectName, objectType, meshName, shaderName, originalShaderName, passName, passLightMode, shaderKeywords, depthState;
            public static FrameEventRecord From(int index, object rawEvent, object data, Object eventObject)
            {
                Type type=data.GetType();
                object mesh=Field(data,"m_Mesh");
                return new FrameEventRecord
                {
                    index=index,eventType=Field(rawEvent,"m_Type")?.ToString(),objectName=eventObject?.name,
                    objectType=eventObject?.GetType().FullName,componentInstanceId=Int(type,data,"m_ComponentInstanceID"),
                    meshInstanceId=Int(type,data,"m_MeshInstanceID"),submesh=Int(type,data,"m_MeshSubset"),
                    indexCount=Int(type,data,"m_IndexCount"),vertexCount=Int(type,data,"m_VertexCount"),
                    instanceCount=Int(type,data,"m_InstanceCount"),shaderPassIndex=Int(type,data,"m_ShaderPassIndex"),
                    meshName=(mesh as Mesh)?.name,shaderName=Field(data,"m_RealShaderName") as string,
                    originalShaderName=Field(data,"m_OriginalShaderName") as string,passName=Field(data,"m_PassName") as string,
                    passLightMode=Field(data,"m_PassLightMode") as string,shaderKeywords=Field(data,"shaderKeywords") as string,
                    depthState=DescribeObject(Field(data,"m_DepthState"))
                };
            }
            private static object Field(object target,string name)=>target?.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(target);
            private static int Int(Type type,object target,string name){object value=type.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(target);return value==null?0:Convert.ToInt32(value,CultureInfo.InvariantCulture);}
            private static string DescribeObject(object value)
            {
                if(value==null)return "";
                return string.Join(",",value.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .Select(field=>field.Name+"="+field.GetValue(value)));
            }
        }
    }
}
