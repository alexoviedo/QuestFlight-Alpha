using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using QuestFlightLab.Environment;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace QuestFlight.Editor
{
    /// <summary>Offline compiler for full-map semantic building pages and their fixed product owner.</summary>
    public static class FullMapBuildingClipmapAssetAuthoring
    {
        public const string Root = "Assets/Production/Environment/FullMapBuildingsV1";
        public const string DatabasePath = Root + "/KBDU_FullMapBuildingDatabaseV1.json";
        public const string MaterialPath = Root + "/Materials/FullMapBuildingClipmap.mat";
        public const string AlbedoPath = Root + "/Materials/FullMapBuildingAtlas_Albedo.png";
        public const string NormalPath = Root + "/Materials/FullMapBuildingAtlas_Normal.png";
        public const string MraoPath = Root + "/Materials/FullMapBuildingAtlas_MRAO.png";
        public const string CompilationReportPath = Root + "/KBDU_StableShellCompilationV1.json";
        public const string GeneratedRoot = "Assets/Generated/FullMapBuildingClipmap";
        public const string StreamingRoot = "Assets/StreamingAssets/QuestFlightBuildingClipmap";
        public const string EnvironmentPath = "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string AircraftPath = "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        public const string ShaderName = "QuestFlight/Full Map Building Clipmap";
        private const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";
        private const int BundleRegionCells = 2;
        private const string SourceDatabaseContractVersion = "full-map-building-clipmap-v1";
        private const string MeshCompilerVersion = "stable-closed-shell-compiler-v6-20260815";
        private const int MaximumSelectedTriangleCeiling = 140000;
        private const int AuthoredMaximumSelectedTriangles = 139588;
        private const int OrdinaryFootprintVertexLimit = 4;
        private const int LargeFootprintVertexLimit = 6;
        private const float LargeFootprintAreaSquareMeters = 1000f;

        [Serializable] private sealed class Database
        {
            public int schema_version;
            public string contract_version;
            public float map_half_extent_meters;
            public float page_size_meters;
            public int columns;
            public int rows;
            public float quantization_meters;
            public int runtime_window_cells;
            public float prediction_seconds;
            public float lod0_max_distance_meters;
            public float lod1_max_distance_meters;
            public float hysteresis_meters;
            public Page[] pages;
        }

        [Serializable] private sealed class Page
        {
            public int column;
            public int row;
            public Building[] buildings;
        }

        [Serializable] private sealed class Building
        {
            public int id;
            public string source_id;
            public int component;
            public int class_id;
            public int roof_id;
            public int height_dm;
            public int roof_rise_dm;
            public int base_y_dm;
            public float area_m2;
            public long seed;
            public bool landmark;
            public int[] lod0_q;
            public int[] lod1_q;
            public int[] lod2_q;
        }

        [Serializable] private sealed class BundleIndex
        {
            public string contract_version;
            public string compiler_version;
            public BundleRecord[] bundles;
        }

        [Serializable] private sealed class BundleRecord
        {
            public string name;
            public uint crc;
            public string hash;
        }

        [Serializable] private sealed class StableShellCompilationReport
        {
            public string compiler_version;
            public int represented_buildings;
            public int exact_landmarks;
            public int four_vertex_shells;
            public int six_vertex_shells;
            public int higher_vertex_shells;
            public int maximum_active_window_triangles;
            public float maximum_area_error_percent;
            public float maximum_inflation_percent;
            public float maximum_centroid_shift_meters;
            public float maximum_boundary_error_meters;
        }

        private sealed class MeshBuilder
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector4> tangents = new List<Vector4>();
            public readonly List<Vector2> detailUvs = new List<Vector2>();
            public readonly List<Vector2> atlasCells = new List<Vector2>();
            public readonly List<Vector4> parameters = new List<Vector4>();
            public readonly List<Color32> colors = new List<Color32>();
            public readonly List<int> indices = new List<int>();
        }

        [MenuItem("QuestFlight/Product/Author Full Map Building Clipmap")]
        public static void RunInteractive() => BuildAndroidBundlesAndAuthorProduct();

        public static void BuildAndroidBundlesAndAuthorProductBatch()
        {
            try
            {
                BuildAndroidBundlesAndAuthorProduct();
                Debug.Log("[QuestFlight][BuildingClipmap] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        public static void ReauthorSerializedProductBatch()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Database database = LoadDatabase();
                Material material = EnsureMaterial();
                AuthorAircraftImageryBinding(material);
                AuthorEnvironment(database, material);
                BindSceneAircraftReference();
                AssetDatabase.SaveAssets();
                ValidateAuthoredProduct(database);
                Debug.Log("[QuestFlight][BuildingClipmap] REAUTHOR SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        public static void EnsureBuildArtifacts()
        {
            string stampPath = Absolute(StreamingRoot + "/build_stamp.txt");
            string expected = SourceHash();
            if (File.Exists(stampPath) && string.Equals(File.ReadAllText(stampPath).Trim(), expected, StringComparison.Ordinal) &&
                Directory.EnumerateFiles(Absolute(StreamingRoot), "qf_buildings_*", SearchOption.TopDirectoryOnly).Any())
                return;
            BuildAndroidBundlesAndAuthorProduct();
        }

        /// <summary>
        /// Assigns already compiled page meshes to the serialized pool for deterministic
        /// Editor-only captures. The product prefab remains mesh-free; Android loads the
        /// same addresses from bundles through the runtime controller.
        /// </summary>
        public static void LoadEditorPreviewWindow(
            AircraftCenteredBuildingClipmapController controller,
            Vector3 center)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            Database database = LoadDatabase();
            int centerColumn = Mathf.FloorToInt((center.x + database.map_half_extent_meters) /
                                                database.page_size_meters);
            int centerRow = Mathf.FloorToInt((center.z + database.map_half_extent_meters) /
                                             database.page_size_meters);
            int minimumColumn = Mathf.Clamp(centerColumn - AircraftCenteredBuildingClipmapController.GridSize / 2, 0,
                database.columns - AircraftCenteredBuildingClipmapController.GridSize);
            int minimumRow = Mathf.Clamp(centerRow - AircraftCenteredBuildingClipmapController.GridSize / 2, 0,
                database.rows - AircraftCenteredBuildingClipmapController.GridSize);
            for (int localRow = 0; localRow < AircraftCenteredBuildingClipmapController.GridSize; localRow++)
            for (int localColumn = 0; localColumn < AircraftCenteredBuildingClipmapController.GridSize; localColumn++)
            {
                int slot = localRow * AircraftCenteredBuildingClipmapController.GridSize + localColumn;
                int column = minimumColumn + localColumn;
                int row = minimumRow + localRow;
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(column, row)) ??
                    throw new FileNotFoundException(
                        "Compiled building preview mesh is missing. Run full-map building authoring first.",
                        MeshPath(column, row));
                controller.pageFilters[slot].sharedMesh = mesh;
                controller.pageRenderers[slot].sharedMaterial = controller.sharedBuildingMaterial;
                controller.pageRenderers[slot].enabled = mesh.vertexCount > 0;
            }
        }

        public static void BuildAndroidBundlesAndAuthorProduct()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Database database = LoadDatabase();
            ConfigureTextures();
            Material material = EnsureMaterial();
            AuthorAircraftImageryBinding(material);
            RebuildMeshAssets(database);
            BuildBundles(database);
            AuthorEnvironment(database, material);
            BindSceneAircraftReference();
            File.WriteAllText(Absolute(StreamingRoot + "/build_stamp.txt"), SourceHash());
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            ValidateAuthoredProduct(database);
        }

        private static Database LoadDatabase()
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(DatabasePath);
            if (source == null) throw new FileNotFoundException("Full-map building database is missing.", DatabasePath);
            Database database = JsonUtility.FromJson<Database>(source.text);
            if (database == null || database.schema_version != 1 ||
                !string.Equals(database.contract_version, SourceDatabaseContractVersion, StringComparison.Ordinal) ||
                database.pages == null || database.pages.Length != database.columns * database.rows ||
                database.runtime_window_cells != AircraftCenteredBuildingClipmapController.GridSize)
                throw new InvalidOperationException("Full-map building database contract is invalid.");
            return database;
        }

        private static void ConfigureTextures()
        {
            ConfigureTexture(AlbedoPath, true, TextureImporterType.Default, TextureImporterFormat.ASTC_6x6);
            ConfigureTexture(NormalPath, false, TextureImporterType.NormalMap, TextureImporterFormat.ASTC_6x6);
            ConfigureTexture(MraoPath, false, TextureImporterType.Default, TextureImporterFormat.ASTC_6x6);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureTexture(string path, bool sRgb, TextureImporterType type, TextureImporterFormat format)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter ??
                throw new InvalidOperationException("Texture importer is unavailable: " + path);
            importer.textureType = type;
            importer.sRGBTexture = sRgb;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.npotScale = TextureImporterNPOTScale.None;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = format;
            android.compressionQuality = 100;
            importer.SetPlatformTextureSettings(android);
            AssetDatabase.WriteImportSettingsIfDirty(path);
        }

        private static Material EnsureMaterial()
        {
            Shader shader = Shader.Find(ShaderName) ?? throw new InvalidOperationException("Building shader is missing.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "FullMapBuildingClipmap" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_MainTex", LoadRequired<Texture2D>(AlbedoPath));
            material.SetTexture("_BumpMap", LoadRequired<Texture2D>(NormalPath));
            material.SetTexture("_MRAO", LoadRequired<Texture2D>(MraoPath));
            material.SetFloat("_NormalScale", 0.45f);
            material.SetFloat("_WindowReflection", 0.58f);
            material.SetFloat("_AmbientLift", 0.18f);
            Material terrain = LoadRequired<Material>(TerrainMaterialPath);
            foreach (string propertyName in new[] { "_NearAlbedo", "_MidAlbedo", "_FarAlbedo" })
                material.SetTexture(propertyName, terrain.GetTexture(propertyName));
            foreach (string propertyName in new[]
                     {
                         "_NearWorldMinInvSize", "_MidWorldMinInvSize", "_FarWorldMinInvSize",
                         "_NearBlendStartEnd", "_MidBlendStartEnd"
                     })
                material.SetVector(propertyName, terrain.GetVector(propertyName));
            material.SetFloat("_ClipmapEnabled", 0f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AuthorAircraftImageryBinding(Material buildingMaterial)
        {
            GameObject aircraft = PrefabUtility.LoadPrefabContents(AircraftPath);
            try
            {
                AircraftCenteredAerialClipmapController aerialController =
                    aircraft.GetComponent<AircraftCenteredAerialClipmapController>() ??
                    throw new InvalidOperationException("Aircraft-centered aerial clipmap controller is missing.");
                aerialController.buildingRoofMaterial = buildingMaterial;
                aerialController.authoredContractVersion = AircraftCenteredAerialClipmapController.ContractVersion;
                PrefabUtility.SaveAsPrefabAsset(aircraft, AircraftPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(aircraft);
            }
        }

        private static void RebuildMeshAssets(Database database)
        {
            string absolute = Absolute(GeneratedRoot);
            if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
            Directory.CreateDirectory(absolute);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (Page page in database.pages.OrderBy(item => item.row).ThenBy(item => item.column))
            {
                Mesh mesh = BuildPageMesh(database, page);
                AssetDatabase.CreateAsset(mesh, MeshPath(page.column, page.row));
                if ((page.row * database.columns + page.column) % 24 == 23)
                {
                    AssetDatabase.SaveAssets();
                    EditorUtility.DisplayProgressBar("Full-map building clipmap", $"Compiled row {page.row + 1}/{database.rows}",
                        (page.row + 1f) / database.rows);
                }
            }
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            WriteCompilationReport(database);
        }

        private static Mesh BuildPageMesh(Database database, Page page)
        {
            MeshBuilder builder = new MeshBuilder();
            float pageMinX = -database.map_half_extent_meters + page.column * database.page_size_meters;
            float pageMinZ = -database.map_half_extent_meters + page.row * database.page_size_meters;
            foreach (Building building in page.buildings ?? Array.Empty<Building>())
            {
                int[] ring = building.lod2_q;
                if (ring == null || ring.Length < 6 || ring.Length % 2 != 0) continue;
                Vector2[] polygon = new Vector2[ring.Length / 2];
                for (int i = 0; i < polygon.Length; i++)
                    polygon[i] = new Vector2(pageMinX + ring[i * 2] * database.quantization_meters,
                        pageMinZ + ring[i * 2 + 1] * database.quantization_meters);
                polygon = CompileStableFootprint(polygon, building);
                AddBuilding(builder, polygon, building, true, true);
            }

            Mesh mesh = new Mesh
            {
                name = $"KBDU_buildings_stable_{page.column:D3}_{page.row:D3}",
                indexFormat = builder.vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(builder.vertices);
            mesh.SetNormals(builder.normals);
            mesh.SetTangents(builder.tangents);
            mesh.SetUVs(0, builder.detailUvs);
            mesh.SetUVs(1, builder.atlasCells);
            mesh.SetUVs(2, builder.parameters);
            mesh.SetColors(builder.colors);
            mesh.SetIndices(builder.indices, MeshTopology.Triangles, 0, true);
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static Vector2[] CompileStableFootprint(Vector2[] source, Building building)
        {
            if (source == null || source.Length < 3) return source;
            Vector2[] validSource = IsSimplePolygon(source) ? source.ToArray() : ConvexHull(source);
            if (building.landmark || validSource.Length <= 3) return validSource;

            int target = Mathf.Min(validSource.Length,
                building.area_m2 >= LargeFootprintAreaSquareMeters
                    ? LargeFootprintVertexLimit
                    : OrdinaryFootprintVertexLimit);
            Vector2[] candidate = ReduceFootprint(validSource, target);
            FootprintError error = MeasureFootprintError(validSource, candidate);

            if (validSource.Length > target &&
                (error.areaFraction > 0.30f || error.boundaryMeters > 10f || error.centroidMeters > 3f))
            {
                target = Mathf.Min(validSource.Length, 6);
                candidate = ReduceFootprint(validSource, target);
                error = MeasureFootprintError(validSource, candidate);
            }
            if (validSource.Length > target &&
                (error.areaFraction > 0.35f || error.boundaryMeters > 15f || error.centroidMeters > 4f))
            {
                target = Mathf.Min(validSource.Length, 8);
                candidate = ReduceFootprint(validSource, target);
                error = MeasureFootprintError(validSource, candidate);
            }
            if (validSource.Length > target &&
                (error.areaFraction > 0.40f || error.boundaryMeters > 20f || error.centroidMeters > 6f))
            {
                target = Mathf.Min(validSource.Length, 10);
                candidate = ReduceFootprint(validSource, target);
                error = MeasureFootprintError(validSource, candidate);
            }
            while (validSource.Length > candidate.Length &&
                   (error.areaFraction > 0.30f || error.boundaryMeters > 10f || error.centroidMeters > 3f))
            {
                Vector2[] refined = ReduceFootprint(validSource, Mathf.Min(validSource.Length, candidate.Length + 2));
                if (refined.Length <= candidate.Length) break;
                candidate = refined;
                error = MeasureFootprintError(validSource, candidate);
            }

            return candidate;
        }

        private readonly struct FootprintError
        {
            public readonly float areaFraction;
            public readonly float centroidMeters;
            public readonly float boundaryMeters;
            public FootprintError(float areaFraction, float centroidMeters, float boundaryMeters)
            { this.areaFraction = areaFraction; this.centroidMeters = centroidMeters; this.boundaryMeters = boundaryMeters; }
        }

        private static FootprintError MeasureFootprintError(Vector2[] source, Vector2[] candidate)
        {
            float sourceArea = Mathf.Max(0.001f, Mathf.Abs(SignedArea(source)));
            float areaFraction = Mathf.Abs(Mathf.Abs(SignedArea(candidate)) - sourceArea) / sourceArea;
            float centroidMeters = Vector2.Distance(PolygonCentroid(source), PolygonCentroid(candidate));
            float boundaryMeters = 0f;
            foreach (Vector2 point in source)
            {
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < candidate.Length; i++)
                    nearest = Mathf.Min(nearest, PointSegmentDistance(point, candidate[i], candidate[(i + 1) % candidate.Length]));
                boundaryMeters = Mathf.Max(boundaryMeters, nearest);
            }
            return new FootprintError(areaFraction, centroidMeters, boundaryMeters);
        }

        private static Vector2[] ReduceFootprint(Vector2[] source, int targetVertices)
        {
            List<Vector2> points = source.ToList();
            float maximumArea = Mathf.Abs(SignedArea(source)) * 1.05f;
            while (points.Count > Mathf.Max(3, targetVertices))
            {
                int remove = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < points.Count; i++)
                {
                    List<Vector2> trial = points.Where((_, index) => index != i).ToList();
                    float trialArea = Mathf.Abs(SignedArea(trial.ToArray()));
                    if (trialArea < 0.01f || trialArea > maximumArea || !IsSimplePolygon(trial.ToArray())) continue;
                    float score = Mathf.Abs(Cross(points[(i - 1 + points.Count) % points.Count], points[i], points[(i + 1) % points.Count]));
                    if (score < best - 0.0001f || (Mathf.Abs(score - best) <= 0.0001f && i < remove))
                    { best = score; remove = i; }
                }
                if (remove < 0) break;
                points.RemoveAt(remove);
            }
            return points.ToArray();
        }

        private static bool IsSimplePolygon(Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3 || Mathf.Abs(SignedArea(polygon)) < 0.01f) return false;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                for (int j = i + 1; j < polygon.Length; j++)
                {
                    if (j == i || j == (i + 1) % polygon.Length || (j + 1) % polygon.Length == i) continue;
                    if (i == 0 && j == polygon.Length - 1) continue;
                    if (SegmentsIntersect(a, b, polygon[j], polygon[(j + 1) % polygon.Length])) return false;
                }
            }
            return true;
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross(a, b, c), abD = Cross(a, b, d), cdA = Cross(c, d, a), cdB = Cross(c, d, b);
            return abC * abD < -0.000001f && cdA * cdB < -0.000001f;
        }

        private static Vector2[] ConvexHull(IEnumerable<Vector2> source)
        {
            List<Vector2> points = source.Distinct().OrderBy(point => point.x).ThenBy(point => point.y).ToList();
            if (points.Count <= 3) return points.ToArray();
            List<Vector2> lower = new List<Vector2>();
            foreach (Vector2 point in points)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], point) <= 0f) lower.RemoveAt(lower.Count - 1);
                lower.Add(point);
            }
            List<Vector2> upper = new List<Vector2>();
            for (int i = points.Count - 1; i >= 0; i--)
            {
                Vector2 point = points[i];
                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], point) <= 0f) upper.RemoveAt(upper.Count - 1);
                upper.Add(point);
            }
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            return lower.Concat(upper).ToArray();
        }

        private static Vector2 PolygonCentroid(Vector2[] polygon)
        {
            float crossSum = 0f, x = 0f, y = 0f;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                float cross = a.x * b.y - b.x * a.y;
                crossSum += cross; x += (a.x + b.x) * cross; y += (a.y + b.y) * cross;
            }
            if (Mathf.Abs(crossSum) < 0.0001f)
                return polygon.Aggregate(Vector2.zero, (sum, point) => sum + point) / polygon.Length;
            return new Vector2(x / (3f * crossSum), y / (3f * crossSum));
        }

        private static float PointSegmentDistance(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            float lengthSquared = edge.sqrMagnitude;
            float t = lengthSquared > 0.000001f ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / lengthSquared) : 0f;
            return Vector2.Distance(point, a + edge * t);
        }

        private static void AddBuilding(MeshBuilder builder, Vector2[] polygon, Building building,
            bool includeWalls, bool includeRoof)
        {
            if (SignedArea(polygon) < 0f) Array.Reverse(polygon);
            float baseY = building.base_y_dm * 0.1f;
            float wallTop = baseY + building.height_dm * 0.1f;
            float roofRise = building.roof_rise_dm * 0.1f;
            Color32 tint = BuildingTint(building);
            Vector2 facadeCell = FacadeCell(building);
            Vector2 roofCell = RoofCell(building);
            float seed01 = (building.seed & 0xffff) / 65535f;
            Vector4 facadeParameters = new Vector4(0f, building.class_id, seed01, 0.40f);
            Vector4 roofParameters = new Vector4(1f, building.class_id, seed01, RoofDetailScale(building));

            if (includeWalls)
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a2 = polygon[i];
                Vector2 b2 = polygon[(i + 1) % polygon.Length];
                Vector3 a = new Vector3(a2.x, baseY, a2.y);
                Vector3 b = new Vector3(b2.x, baseY, b2.y);
                Vector3 c = new Vector3(b2.x, wallTop, b2.y);
                Vector3 d = new Vector3(a2.x, wallTop, a2.y);
                float length = Vector2.Distance(a2, b2);
                AddQuad(builder, a, b, c, d,
                    new Vector2(0f, 0f), new Vector2(length, 0f), new Vector2(length, wallTop - baseY), new Vector2(0f, wallTop - baseY),
                    facadeCell, facadeParameters, tint);
            }

            if (!includeRoof) return;
            List<int> triangles = Triangulate(polygon);
            RoofAxes axes = RoofAxes.From(polygon);
            Vector3[] roof = polygon.Select(point => new Vector3(point.x,
                wallTop + RoofHeight(point, axes, building.roof_id, roofRise), point.y)).ToArray();
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                // Polygon triangulation is CCW in X/Z; reverse for an upward Unity normal.
                AddTriangle(builder, roof[a], roof[c], roof[b], roofCell, roofParameters, tint, true);
            }
        }

        private static void AddQuad(MeshBuilder builder, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD, Vector2 atlasCell, Vector4 parameters, Color32 tint)
        {
            // Source footprint rings are CCW in X/Z. Their interior is left of
            // each directed edge, so the exterior wall normal is the reverse of
            // Cross(edge, up). The old winding faced inward and Cull Back made
            // exterior walls disappear from ordinary flight views.
            Vector3 normal = Vector3.Cross(c - a, b - a).normalized;
            Vector3 tangent3 = (b - a).normalized;
            Vector4 tangent = new Vector4(tangent3.x, tangent3.y, tangent3.z, -1f);
            int start = builder.vertices.Count;
            foreach ((Vector3 point, Vector2 uv) in new[] { (a, uvA), (b, uvB), (c, uvC), (d, uvD) })
            {
                builder.vertices.Add(point); builder.normals.Add(normal); builder.tangents.Add(tangent);
                builder.detailUvs.Add(uv); builder.atlasCells.Add(atlasCell); builder.parameters.Add(parameters); builder.colors.Add(tint);
            }
            builder.indices.Add(start); builder.indices.Add(start + 2); builder.indices.Add(start + 1);
            builder.indices.Add(start); builder.indices.Add(start + 3); builder.indices.Add(start + 2);
        }

        private static void AddTriangle(MeshBuilder builder, Vector3 a, Vector3 b, Vector3 c,
            Vector2 atlasCell, Vector4 parameters, Color32 tint, bool roof)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Vector3 tangent3 = Vector3.ProjectOnPlane(Vector3.right, normal).normalized;
            if (tangent3.sqrMagnitude < 0.1f) tangent3 = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;
            Vector4 tangent = new Vector4(tangent3.x, tangent3.y, tangent3.z, 1f);
            int start = builder.vertices.Count;
            foreach (Vector3 point in new[] { a, b, c })
            {
                builder.vertices.Add(point); builder.normals.Add(normal); builder.tangents.Add(tangent);
                builder.detailUvs.Add(roof ? new Vector2(point.x, point.z) : new Vector2(point.x, point.y));
                builder.atlasCells.Add(atlasCell); builder.parameters.Add(parameters); builder.colors.Add(tint);
            }
            builder.indices.Add(start); builder.indices.Add(start + 1); builder.indices.Add(start + 2);
        }

        private static List<int> Triangulate(Vector2[] polygon)
        {
            List<int> remaining = Enumerable.Range(0, polygon.Length).ToList();
            List<int> result = new List<int>((polygon.Length - 2) * 3);
            int guard = polygon.Length * polygon.Length;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int previous = remaining[(i - 1 + remaining.Count) % remaining.Count];
                    int current = remaining[i];
                    int next = remaining[(i + 1) % remaining.Count];
                    if (Cross(polygon[previous], polygon[current], polygon[next]) <= 0.0001f) continue;
                    bool contains = false;
                    foreach (int candidate in remaining)
                    {
                        if (candidate == previous || candidate == current || candidate == next) continue;
                        if (PointInTriangle(polygon[candidate], polygon[previous], polygon[current], polygon[next]))
                        { contains = true; break; }
                    }
                    if (contains) continue;
                    result.Add(previous); result.Add(current); result.Add(next);
                    remaining.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) break;
            }
            if (remaining.Count == 3)
            { result.Add(remaining[0]); result.Add(remaining[1]); result.Add(remaining[2]); }
            if (result.Count == 0 && polygon.Length >= 3)
                for (int i = 1; i < polygon.Length - 1; i++) { result.Add(0); result.Add(i); result.Add(i + 1); }
            return result;
        }

        private static float SignedArea(Vector2[] polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Length; i++)
            { Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length]; area += a.x * b.y - b.x * a.y; }
            return area * 0.5f;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float ab = Cross(a, b, p), bc = Cross(b, c, p), ca = Cross(c, a, p);
            return ab >= -0.0001f && bc >= -0.0001f && ca >= -0.0001f;
        }

        private readonly struct RoofAxes
        {
            public readonly Vector2 center, major, minor;
            public readonly float majorExtent, minorExtent;
            private RoofAxes(Vector2 center, Vector2 major, Vector2 minor, float majorExtent, float minorExtent)
            { this.center = center; this.major = major; this.minor = minor; this.majorExtent = majorExtent; this.minorExtent = minorExtent; }
            public static RoofAxes From(Vector2[] polygon)
            {
                Vector2 center = polygon.Aggregate(Vector2.zero, (sum, point) => sum + point) / polygon.Length;
                float xx = 0f, zz = 0f, xz = 0f;
                foreach (Vector2 point in polygon)
                { Vector2 d = point - center; xx += d.x * d.x; zz += d.y * d.y; xz += d.x * d.y; }
                float angle = 0.5f * Mathf.Atan2(2f * xz, xx - zz);
                Vector2 major = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 minor = new Vector2(-major.y, major.x);
                float majorExtent = polygon.Max(p => Mathf.Abs(Vector2.Dot(p - center, major)));
                float minorExtent = polygon.Max(p => Mathf.Abs(Vector2.Dot(p - center, minor)));
                if (minorExtent > majorExtent) (major, minor, majorExtent, minorExtent) = (minor, major, minorExtent, majorExtent);
                return new RoofAxes(center, major, minor, Mathf.Max(majorExtent, 0.1f), Mathf.Max(minorExtent, 0.1f));
            }
        }

        private static float RoofHeight(Vector2 point, RoofAxes axes, int roofId, float rise)
        {
            if (roofId == 0 || rise <= 0f) return 0f;
            Vector2 delta = point - axes.center;
            float minor = 1f - Mathf.Clamp01(Mathf.Abs(Vector2.Dot(delta, axes.minor)) / axes.minorExtent);
            if (roofId == 1) return rise * minor;
            float major = 1f - Mathf.Clamp01(Mathf.Abs(Vector2.Dot(delta, axes.major)) / axes.majorExtent);
            return rise * Mathf.Min(minor, major);
        }

        private static Vector2 FacadeCell(Building building)
        {
            int seed = (int)(building.seed & 0x7fffffff);
            return building.class_id switch
            {
                0 => new Vector2(seed % 4, 0),
                1 => new Vector2(seed % 2, 1),
                2 => new Vector2(2 + seed % 2, 1),
                3 => seed % 3 == 0 ? new Vector2(2, 3) : new Vector2(seed % 2, seed % 2),
                4 => new Vector2(3, 3),
                _ => new Vector2(seed % 4, 0)
            };
        }

        private static Vector2 RoofCell(Building building)
        {
            int seed = (int)(building.seed & 0x7fffffff);
            return building.class_id switch
            {
                // Residential and small generic structures use restrained shingle
                // variants; commercial/institutional roofs use membrane; industrial
                // and utility structures use standing-seam metal. This prevents the
                // former random assignment of shingles to warehouses and metal to homes.
                0 => new Vector2(seed % 2 == 0 ? 0 : 2, 2),
                1 => new Vector2(1, 2),
                2 => new Vector2(3, 2),
                3 => new Vector2(1, 2),
                4 => new Vector2(3, 2),
                _ => new Vector2(seed % 3 == 0 ? 0 : 1, 2)
            };
        }

        private static float RoofDetailScale(Building building)
        {
            return building.class_id switch
            {
                0 => 0.38f, // asphalt shingles: small modules
                1 => 0.075f, // broad commercial membrane fields
                2 => 0.14f, // corrugated/standing-seam industrial roofs
                3 => 0.075f,
                4 => 0.14f,
                _ => 0.11f
            };
        }

        private static Color32 BuildingTint(Building building)
        {
            int seed = (int)(building.seed & 0x7fffffff);
            float variation = 0.93f + (seed % 13) / 130f;
            float warmth = ((seed / 13) % 7 - 3) * 0.008f;
            return new Color(variation + warmth, variation, variation - warmth, 1f);
        }

        private static void WriteCompilationReport(Database database)
        {
            int[,] pageTriangles = new int[database.columns, database.rows];
            StableShellCompilationReport report = new StableShellCompilationReport
            {
                compiler_version = MeshCompilerVersion
            };
            foreach (Page page in database.pages)
            foreach (Building building in page.buildings ?? Array.Empty<Building>())
            {
                int[] ring = building.lod2_q;
                if (ring == null || ring.Length < 6 || ring.Length % 2 != 0) continue;
                Vector2[] source = new Vector2[ring.Length / 2];
                for (int i = 0; i < source.Length; i++)
                    source[i] = new Vector2(ring[i * 2] * database.quantization_meters,
                        ring[i * 2 + 1] * database.quantization_meters);
                Vector2[] compiled = CompileStableFootprint(source, building);
                if (compiled == null || compiled.Length < 3 || !IsSimplePolygon(compiled))
                    throw new InvalidOperationException("Stable shell compilation produced an invalid footprint: " + building.id);
                FootprintError error = MeasureFootprintError(source, compiled);
                report.represented_buildings++;
                if (building.landmark) report.exact_landmarks++;
                if (compiled.Length <= 4) report.four_vertex_shells++;
                else if (compiled.Length <= 6) report.six_vertex_shells++;
                else report.higher_vertex_shells++;
                report.maximum_area_error_percent = Mathf.Max(report.maximum_area_error_percent, error.areaFraction * 100f);
                float inflation = (Mathf.Abs(SignedArea(compiled)) - Mathf.Abs(SignedArea(source))) /
                                  Mathf.Max(0.001f, Mathf.Abs(SignedArea(source))) * 100f;
                report.maximum_inflation_percent = Mathf.Max(report.maximum_inflation_percent, inflation);
                report.maximum_centroid_shift_meters = Mathf.Max(report.maximum_centroid_shift_meters, error.centroidMeters);
                report.maximum_boundary_error_meters = Mathf.Max(report.maximum_boundary_error_meters, error.boundaryMeters);
                pageTriangles[page.column, page.row] += 3 * compiled.Length - 2;
            }

            for (int minimumRow = 0; minimumRow <= database.rows - AircraftCenteredBuildingClipmapController.GridSize; minimumRow++)
            for (int minimumColumn = 0; minimumColumn <= database.columns - AircraftCenteredBuildingClipmapController.GridSize; minimumColumn++)
            {
                int selected = 0;
                for (int row = minimumRow; row < minimumRow + AircraftCenteredBuildingClipmapController.GridSize; row++)
                for (int column = minimumColumn; column < minimumColumn + AircraftCenteredBuildingClipmapController.GridSize; column++)
                    selected += pageTriangles[column, row];
                report.maximum_active_window_triangles = Mathf.Max(report.maximum_active_window_triangles, selected);
            }
            if (report.represented_buildings != 57407)
                throw new InvalidOperationException("Stable shell compilation lost source buildings.");
            if (report.maximum_active_window_triangles > MaximumSelectedTriangleCeiling)
                throw new InvalidOperationException($"Stable shell window exceeds {MaximumSelectedTriangleCeiling:N0} triangles: {report.maximum_active_window_triangles:N0}.");
            if (report.maximum_inflation_percent > 5.01f)
                throw new InvalidOperationException($"Stable shell footprint inflation exceeds 5%: {report.maximum_inflation_percent:0.###}%.");

            File.WriteAllText(Absolute(CompilationReportPath), JsonUtility.ToJson(report, true));
            AssetDatabase.ImportAsset(CompilationReportPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void BuildBundles(Database database)
        {
            string streaming = Absolute(StreamingRoot);
            if (Directory.Exists(streaming)) Directory.Delete(streaming, true);
            Directory.CreateDirectory(streaming);
            List<AssetBundleBuild> builds = new List<AssetBundleBuild>();
            for (int regionRow = 0; regionRow < Mathf.CeilToInt(database.rows / (float)BundleRegionCells); regionRow++)
            for (int regionColumn = 0; regionColumn < Mathf.CeilToInt(database.columns / (float)BundleRegionCells); regionColumn++)
            {
                List<string> paths = new List<string>();
                List<string> addresses = new List<string>();
                for (int row = regionRow * BundleRegionCells; row < Mathf.Min(database.rows, (regionRow + 1) * BundleRegionCells); row++)
                for (int column = regionColumn * BundleRegionCells; column < Mathf.Min(database.columns, (regionColumn + 1) * BundleRegionCells); column++)
                { paths.Add(MeshPath(column, row)); addresses.Add(AssetName(column, row)); }
                builds.Add(new AssetBundleBuild
                {
                    assetBundleName = $"qf_buildings_r{regionRow:D2}_c{regionColumn:D2}",
                    assetNames = paths.ToArray(), addressableNames = addresses.ToArray()
                });
            }
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(streaming, builds.ToArray(),
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.Android);
            if (manifest == null) throw new InvalidOperationException("Building AssetBundle compilation failed.");
            BundleRecord[] records = builds.Select(build =>
            {
                string path = Path.Combine(streaming, build.assetBundleName);
                if (!BuildPipeline.GetCRCForAssetBundle(path, out uint crc))
                    throw new InvalidOperationException("Unable to calculate AssetBundle CRC: " + build.assetBundleName);
                return new BundleRecord
                {
                    name = build.assetBundleName,
                    crc = crc,
                    hash = manifest.GetAssetBundleHash(build.assetBundleName).ToString()
                };
            }).ToArray();
            File.WriteAllText(Path.Combine(streaming, "bundle_index.json"), JsonUtility.ToJson(new BundleIndex
            {
                contract_version = AircraftCenteredBuildingClipmapController.ContractVersion,
                compiler_version = MeshCompilerVersion,
                bundles = records
            }, true));
        }

        private static void AuthorEnvironment(Database database, Material material)
        {
            GameObject environment = PrefabUtility.LoadPrefabContents(EnvironmentPath);
            try
            {
                ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>() ??
                    throw new InvalidOperationException("Production environment contract is missing.");
                AerialSceneryRoot aerial = environment.GetComponentInChildren<AerialSceneryRoot>(true) ??
                    throw new InvalidOperationException("Aerial scenery root is missing.");
                PhotorealAerialSurfaceRoot surface = environment.GetComponentInChildren<PhotorealAerialSurfaceRoot>(true) ??
                    throw new InvalidOperationException("Photoreal aerial-surface contract is missing.");
                if (aerial.buildingRoot != null) UnityEngine.Object.DestroyImmediate(aerial.buildingRoot.gameObject);
                GameObject rootObject = new GameObject(PhotorealAerialSurfaceRoot.BuildingLayerName);
                rootObject.transform.SetParent(aerial.transform, false);
                AircraftCenteredBuildingClipmapController controller = rootObject.AddComponent<AircraftCenteredBuildingClipmapController>();
                controller.sharedBuildingMaterial = material;
                controller.columns = database.columns; controller.rows = database.rows;
                controller.pageSizeMeters = database.page_size_meters;
                controller.mapMinimumX = -database.map_half_extent_meters;
                controller.mapMinimumZ = -database.map_half_extent_meters;
                controller.bundleRegionCells = BundleRegionCells;
                controller.predictionSeconds = database.prediction_seconds;
                controller.authoredMaximumSelectedTriangles = AuthoredMaximumSelectedTriangles;
                controller.authoredContractVersion = AircraftCenteredBuildingClipmapController.ContractVersion;
                controller.pageFilters = new MeshFilter[AircraftCenteredBuildingClipmapController.SlotCount];
                controller.pageRenderers = new MeshRenderer[AircraftCenteredBuildingClipmapController.SlotCount];
                for (int i = 0; i < AircraftCenteredBuildingClipmapController.SlotCount; i++)
                {
                    GameObject slot = new GameObject($"BuildingPageSlot_{i:D2}");
                    slot.transform.SetParent(rootObject.transform, false);
                    MeshFilter filter = slot.AddComponent<MeshFilter>();
                    MeshRenderer renderer = slot.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    renderer.allowOcclusionWhenDynamic = true;
                    controller.pageFilters[i] = filter; controller.pageRenderers[i] = renderer;
                }
                aerial.buildingRoot = rootObject.transform;
                surface.productionBuildingSurfaceLayer = rootObject.transform;
                surface.buildingSurfaceRenderers = controller.pageRenderers.Cast<Renderer>().ToArray();
                surface.buildingSurfaceManifest = LoadRequired<TextAsset>(DatabasePath);
                surface.representedBuildingCount = database.pages.Sum(page => page.buildings?.Length ?? 0);
                surface.nearBuildingCoveragePercent = 100f;
                surface.midBuildingCoveragePercent = 100f;
                surface.maximumBuildingFootprintInflationPercent = 5f;
                surface.bakedBuildingDrawCount = AircraftCenteredBuildingClipmapController.SlotCount;
                surface.bakedBuildingTriangleCount = AuthoredMaximumSelectedTriangles;
                surface.bakeToolVersion = AircraftCenteredBuildingClipmapController.ContractVersion;
                surface.bakeUtc = DateTime.UtcNow.ToString("O");
                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                aerial.bakedRendererCount = budget.rendererCount;
                aerial.bakedSelectedTriangleCount = budget.triangleCount;
                aerial.bakedMaterialCount = budget.materialCount;
                aerial.bakeToolVersion = AircraftCenteredBuildingClipmapController.ContractVersion;
                aerial.bakeUtc = DateTime.UtcNow.ToString("O");
                PrefabUtility.SaveAsPrefabAsset(environment, EnvironmentPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(environment); }
        }

        private static void BindSceneAircraftReference()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            AircraftReferenceFrameRig rig = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<AircraftReferenceFrameRig>(true)).Single();
            AircraftCenteredBuildingClipmapController controller = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<AircraftCenteredBuildingClipmapController>(true)).Single();
            controller.trackedRig = rig;
            EditorUtility.SetDirty(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            EditorSceneManager.SaveScene(scene);
        }

        // Cross-prefab references cannot live on ProductionEnvironmentRoot itself.
        // Keep this narrow entry point so an accepted environment prefab can be
        // re-bound without regenerating its renderer pool or changing local IDs.
        public static void BindSceneAircraftReferenceBatch()
        {
            BindSceneAircraftReference();
            AssetDatabase.SaveAssets();
            Debug.Log("[QuestFlight][BuildingClipmap] SCENE REFERENCE BIND SUCCESS");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void ValidateAuthoredProduct(Database database)
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            AircraftCenteredBuildingClipmapController prefabController = environment.GetComponentInChildren<AircraftCenteredBuildingClipmapController>(true);
            if (prefabController == null || prefabController.pageFilters.Length != AircraftCenteredBuildingClipmapController.SlotCount ||
                prefabController.GetComponentsInChildren<MeshRenderer>(true).Length != AircraftCenteredBuildingClipmapController.SlotCount)
                throw new InvalidOperationException("Serialized full-map building renderer pool is invalid.");
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            AircraftCenteredBuildingClipmapController controller = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<AircraftCenteredBuildingClipmapController>(true)).Single();
            if (!controller.TryValidateContract(out string report)) throw new InvalidOperationException(report);
            AircraftCenteredAerialClipmapController aerialController = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AircraftCenteredAerialClipmapController>(true)).Single();
            if (!aerialController.TryValidateContract(out string aerialReport))
                throw new InvalidOperationException(aerialReport);
            if (database.pages.Sum(page => page.buildings?.Length ?? 0) < 50000)
                throw new InvalidOperationException("Full-map building database lost expected coverage.");
            Debug.Log($"[QuestFlight][BuildingClipmap] {report}; {aerialReport}; components={database.pages.Sum(page => page.buildings?.Length ?? 0):N0}; pages={database.pages.Length:N0}.");
        }

        private static string MeshPath(int column, int row) => $"{GeneratedRoot}/{AssetName(column, row)}.asset";
        private static string AssetName(int column, int row) =>
            $"KBDU_buildings_stable_{column:D3}_{row:D3}";
        private static T LoadRequired<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Required asset missing: " + path);
        private static string Absolute(string assetPath) => Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? throw new InvalidOperationException(), assetPath.Replace('/', Path.DirectorySeparatorChar));
        private static string SourceHash()
        {
            using SHA256 sha = SHA256.Create();
            byte[] database = File.ReadAllBytes(Absolute(DatabasePath));
            byte[] compiler = System.Text.Encoding.UTF8.GetBytes(MeshCompilerVersion);
            sha.TransformBlock(database, 0, database.Length, null, 0);
            sha.TransformFinalBlock(compiler, 0, compiler.Length);
            return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
