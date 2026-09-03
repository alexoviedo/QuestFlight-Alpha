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
    /// <summary>
    /// Pure, deterministic codec for the two convex-corner turns carried by one
    /// wall quad.  The low alpha nibble belongs to the wall start and the high
    /// nibble to the wall end.  Code 15 is reserved so legacy 0xff vertex colors
    /// fail visually neutral instead of being interpreted as maximum curvature.
    /// </summary>
    public static class FullMapBuildingCornerEncoding
    {
        public const byte DisabledCode = 0;
        public const byte ReservedCode = 15;
        public const byte LegacyDisabledPacked = 255;
        public const float QuantizationStepsPerUnit = 8f;
        public const float MaximumQuantizationError = 0.0625f;
        public const float MinimumIncidentEdgeMeters = 0.8f;
        public const float MinimumTurnTangent = 0.08748866f; // tan(10 degrees / 2)
        public const float MaximumTurnTangent = 1.75f;

        public static bool TryCalculateConvexTurnMagnitude(
            IReadOnlyList<Vector2> polygon,
            int cornerIndex,
            out float tangentHalfTurn)
        {
            tangentHalfTurn = 0f;
            if (polygon == null || polygon.Count < 3 ||
                cornerIndex < 0 || cornerIndex >= polygon.Count)
                return false;

            Vector2 origin = polygon[0];
            double twiceArea = 0d;
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 a = polygon[index] - origin;
                Vector2 b = polygon[(index + 1) % polygon.Count] - origin;
                twiceArea += (double)a.x * b.y - (double)a.y * b.x;
            }
            if (Math.Abs(twiceArea) <= 0.0002d || double.IsNaN(twiceArea) || double.IsInfinity(twiceArea))
                return false;

            Vector2 previous = polygon[(cornerIndex - 1 + polygon.Count) % polygon.Count];
            Vector2 current = polygon[cornerIndex];
            Vector2 next = polygon[(cornerIndex + 1) % polygon.Count];
            double incomingX = current.x - previous.x;
            double incomingY = current.y - previous.y;
            double outgoingX = next.x - current.x;
            double outgoingY = next.y - current.y;
            double incomingLength = Math.Sqrt(incomingX * incomingX + incomingY * incomingY);
            double outgoingLength = Math.Sqrt(outgoingX * outgoingX + outgoingY * outgoingY);
            if (incomingLength < MinimumIncidentEdgeMeters || outgoingLength < MinimumIncidentEdgeMeters ||
                double.IsNaN(incomingLength) || double.IsNaN(outgoingLength) ||
                double.IsInfinity(incomingLength) || double.IsInfinity(outgoingLength))
                return false;

            incomingX /= incomingLength;
            incomingY /= incomingLength;
            outgoingX /= outgoingLength;
            outgoingY /= outgoingLength;
            double cross = incomingX * outgoingY - incomingY * outgoingX;
            double dot = incomingX * outgoingX + incomingY * outgoingY;
            double orientedCross = twiceArea > 0d ? cross : -cross;
            double denominator = 1d + dot;
            if (orientedCross <= 0d || denominator <= 0.0001d)
                return false;

            double magnitude = orientedCross / denominator;
            if (double.IsNaN(magnitude) || double.IsInfinity(magnitude) ||
                magnitude < MinimumTurnTangent || magnitude > MaximumTurnTangent)
                return false;

            tangentHalfTurn = (float)magnitude;
            return true;
        }

        public static byte QuantizeMagnitude(float tangentHalfTurn)
        {
            if (float.IsNaN(tangentHalfTurn) || float.IsInfinity(tangentHalfTurn) ||
                tangentHalfTurn < MinimumTurnTangent || tangentHalfTurn > MaximumTurnTangent)
                return DisabledCode;
            return (byte)Mathf.Clamp(
                Mathf.RoundToInt(tangentHalfTurn * QuantizationStepsPerUnit),
                1,
                ReservedCode - 1);
        }

        public static byte Pack(float startMagnitude, float endMagnitude)
        {
            byte start = QuantizeMagnitude(startMagnitude);
            byte end = QuantizeMagnitude(endMagnitude);
            return (byte)((end << 4) | start);
        }

        public static float DecodeStart(byte packed) => DecodeCode(packed, true);
        public static float DecodeEnd(byte packed) => DecodeCode(packed, false);
        public static float SignedStartContribution(byte packed) => -DecodeStart(packed);
        public static float SignedEndContribution(byte packed) => DecodeEnd(packed);

        public static Color32 ApplyToTint(Color32 tint, float startMagnitude, float endMagnitude) =>
            new Color32(tint.r, tint.g, tint.b, Pack(startMagnitude, endMagnitude));

        private static float DecodeCode(byte packed, bool start)
        {
            if (packed == LegacyDisabledPacked) return 0f;
            int code = start ? packed & 0x0f : packed >> 4;
            return code <= DisabledCode || code >= ReservedCode
                ? 0f
                : code / QuantizationStepsPerUnit;
        }
    }

    /// <summary>Offline compiler for full-map semantic building pages and their fixed product owner.</summary>
    public static class FullMapBuildingClipmapAssetAuthoring
    {
        public const string Root = "Assets/Production/Environment/FullMapBuildingsV1";
        public const string DatabasePath = Root + "/KBDU_FullMapBuildingDatabaseV1.json";
        public const string MaterialPath = Root + "/Materials/FullMapBuildingClipmap.mat";
        public const string AlbedoPath = Root + "/Materials/FullMapBuildingFacadeArray_Albedo.png";
        public const string NormalPath = Root + "/Materials/FullMapBuildingFacadeArray_Normal.png";
        public const string MraoPath = Root + "/Materials/FullMapBuildingFacadeArray_MRAO.png";
        public const string DetailPath = Root + "/Materials/FullMapBuildingFacadeDetailAtlas.png";
        public const string FacadeManifestPath = Root + "/KBDU_FullMapBuildingFacadeManifestV2.json";
        public const string CompilationReportPath = Root + "/KBDU_StableShellCompilationV1.json";
        public const string GeneratedRoot = "Assets/Generated/FullMapBuildingClipmap";
        public const string StreamingRoot = "Assets/StreamingAssets/QuestFlightBuildingClipmap";
        public const string EnvironmentPath = "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string AircraftPath = "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        public const string ShaderName = "QuestFlight/Full Map Building Clipmap";
        public const string TerrainSurfacePath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset";
        public const float FoundationEmbedMeters = 1.60f;
        private const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";
        private const int BundleRegionCells = 2;
        private const string SourceDatabaseContractVersion = "full-map-building-clipmap-v1";
        private const string MeshCompilerVersion = "full-height-forward-residency-corner-softening-compiler-v12-20260902";
        private const string FacadeContractVersion = "boulder-semantic-facades-v2";
        private const int MaximumSelectedTriangleCeiling = 235000;
        private const int AuthoredMaximumSelectedTriangles = 233298;
        private const int ExpectedRepresentedBuildings = 57441;
        private const int OrdinaryFootprintVertexLimit = 4;
        private const int LargeFootprintVertexLimit = 6;
        private const float LargeFootprintAreaSquareMeters = 1000f;

        [Serializable] private sealed class Database
        {
            public int schema_version;
            public string contract_version;
            public string facade_contract_version;
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
            public int material_id;
            public int secondary_material_id;
            public int material_confidence;
            public int facade_style_id;
            public int stories_x2;
            public int built_year;
            public int semantic_class_id;
            public int semantic_region_id;
            public int semantic_confidence;
            public int parcel_role_id;
            public int material_rule_id;
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
            public int terrain_sealed_wall_vertices;
            public int roof_sealed_wall_vertices;
            public float foundation_embed_meters;
            public float maximum_roof_wall_seam_meters;
            public float maximum_vertical_corner_seam_meters;
        }

        private readonly struct PositionKey : IEquatable<PositionKey>
        {
            private const float UnitsPerMeter = 1000f;
            private readonly int x, y, z;

            public PositionKey(Vector3 point)
            {
                x = Mathf.RoundToInt(point.x * UnitsPerMeter);
                y = Mathf.RoundToInt(point.y * UnitsPerMeter);
                z = Mathf.RoundToInt(point.z * UnitsPerMeter);
            }

            public bool Equals(PositionKey other) => x == other.x && y == other.y && z == other.z;
            public override bool Equals(object obj) => obj is PositionKey other && Equals(other);
            public override int GetHashCode() => ((x * 397) ^ y) * 397 ^ z;
        }

        private readonly struct WatertightMetrics
        {
            public readonly int terrainSealedWallVertices;
            public readonly int roofSealedWallVertices;
            public readonly float maximumRoofWallSeamMeters;
            public readonly float maximumVerticalCornerSeamMeters;

            public WatertightMetrics(int terrainSealedWallVertices, int roofSealedWallVertices,
                float maximumRoofWallSeamMeters, float maximumVerticalCornerSeamMeters)
            {
                this.terrainSealedWallVertices = terrainSealedWallVertices;
                this.roofSealedWallVertices = roofSealedWallVertices;
                this.maximumRoofWallSeamMeters = maximumRoofWallSeamMeters;
                this.maximumVerticalCornerSeamMeters = maximumVerticalCornerSeamMeters;
            }
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

        public static void RebuildGeometryAndBundlesBatch()
        {
            try
            {
                RebuildGeometryAndBundles();
                Debug.Log("[QuestFlight][BuildingClipmap] GEOMETRY/BUNDLES SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Refreshes database-derived counts on the existing serialized renderer pool.
        /// Unlike full authoring, this preserves every page object and scene reference.
        /// </summary>
        public static void RefreshSerializedPageMetadataBatch()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Database database = LoadDatabase();
                RefreshSerializedPageMetadata(database);
                AssetDatabase.SaveAssets();
                ValidateAuthoredProduct(database);
                Debug.Log("[QuestFlight][BuildingClipmap] METADATA REFRESH SUCCESS");
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
            // The product hierarchy is already explicitly authored. A missing or
            // stale ignored cache needs only deterministic geometry and bundles;
            // rebuilding the serialized environment would create unrelated ID churn.
            RebuildGeometryAndBundles();
        }

        private static void RebuildGeometryAndBundles()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Database database = LoadDatabase();
            RebuildMeshAssets(database);
            BuildBundles(database);
            File.WriteAllText(Absolute(StreamingRoot + "/build_stamp.txt"), SourceHash());
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            ValidateAuthoredProduct(database);
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
                !string.Equals(database.facade_contract_version, FacadeContractVersion, StringComparison.Ordinal) ||
                database.pages == null || database.pages.Length != database.columns * database.rows ||
                database.runtime_window_cells != AircraftCenteredBuildingClipmapController.GridSize)
                throw new InvalidOperationException("Full-map building database contract is invalid.");
            return database;
        }

        private static void ConfigureTextures()
        {
            ConfigureTextureArray(AlbedoPath, true, TextureImporterType.Default, TextureImporterFormat.ASTC_6x6);
            ConfigureTextureArray(NormalPath, false, TextureImporterType.NormalMap, TextureImporterFormat.ASTC_6x6);
            ConfigureTextureArray(MraoPath, false, TextureImporterType.Default, TextureImporterFormat.ASTC_6x6);
            ConfigureDetailTexture(DetailPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureDetailTexture(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter ??
                throw new InvalidOperationException("Texture importer is unavailable: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 100;
            importer.SetPlatformTextureSettings(android);
            AssetDatabase.WriteImportSettingsIfDirty(path);
        }

        private static void ConfigureTextureArray(string path, bool sRgb, TextureImporterType type, TextureImporterFormat format)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter ??
                throw new InvalidOperationException("Texture importer is unavailable: " + path);
            importer.textureType = type;
            importer.textureShape = TextureImporterShape.Texture2DArray;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.textureShape = TextureImporterShape.Texture2DArray;
            settings.flipbookColumns = 4;
            settings.flipbookRows = 4;
            importer.SetTextureSettings(settings);
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
            android.maxTextureSize = 2048;
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
            else
            {
                // Reset obsolete serialized properties while preserving the material
                // asset GUID used by the scene/prefab.  Otherwise Unity retains the
                // retired atlas references in dependency closure even after the
                // shader no longer exposes those properties.
                Material clean = new Material(shader) { name = material.name };
                EditorUtility.CopySerialized(clean, material);
                UnityEngine.Object.DestroyImmediate(clean);
            }
            material.shader = shader;
            material.SetTexture("_FacadeAlbedo", LoadRequired<Texture>(AlbedoPath));
            material.SetTexture("_FacadeNormal", LoadRequired<Texture>(NormalPath));
            material.SetTexture("_FacadeMRAO", LoadRequired<Texture>(MraoPath));
            material.SetTexture("_FacadeDetail", LoadRequired<Texture>(DetailPath));
            material.SetFloat("_NormalScale", 0.34f);
            material.SetFloat("_WindowReflection", 0.38f);
            material.SetFloat("_AmbientLift", 0.04f);
            material.SetFloat("_CornerSofteningWidthMeters", 0.35f);
            material.SetFloat("_CornerSofteningStrength", 0.65f);
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
            ProductionTerrainSurfaceAsset terrain = LoadRequired<ProductionTerrainSurfaceAsset>(TerrainSurfacePath);
            string absolute = Absolute(GeneratedRoot);
            if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
            Directory.CreateDirectory(absolute);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (Page page in database.pages.OrderBy(item => item.row).ThenBy(item => item.column))
            {
                Mesh mesh = BuildPageMesh(database, page, terrain);
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

        private static Mesh BuildPageMesh(Database database, Page page, ProductionTerrainSurfaceAsset terrain)
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
                AddBuilding(builder, polygon, building, terrain, true, true);
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
            Vector2[] normalizedSource = RemoveConsecutiveDuplicateVertices(source);
            Vector2[] validSource = IsSimplePolygon(normalizedSource)
                ? normalizedSource
                : ConvexHull(normalizedSource);
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

        private static Vector2[] RemoveConsecutiveDuplicateVertices(Vector2[] source)
        {
            List<Vector2> result = new List<Vector2>(source.Length);
            foreach (Vector2 point in source)
                if (result.Count == 0 || (point - result[result.Count - 1]).sqrMagnitude > 0.000001f)
                    result.Add(point);
            if (result.Count > 1 && (result[0] - result[result.Count - 1]).sqrMagnitude <= 0.000001f)
                result.RemoveAt(result.Count - 1);
            return result.ToArray();
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
            ProductionTerrainSurfaceAsset terrain, bool includeWalls, bool includeRoof)
        {
            if (SignedArea(polygon) < 0f) Array.Reverse(polygon);
            float baseY = building.base_y_dm * 0.1f;
            float wallTop = baseY + building.height_dm * 0.1f;
            float roofRise = building.roof_rise_dm * 0.1f;
            Color32 tint = BuildingTint(building);
            Vector2 roofCell = RoofCell(building);
            float seed01 = (building.seed & 0xffff) / 65535f;
            Vector4 roofParameters = new Vector4(1f, building.class_id, seed01, RoofDetailScale(building));
            RoofAxes axes = RoofAxes.From(polygon);
            Vector3[] roof = polygon.Select(point => new Vector3(point.x,
                wallTop + RoofHeight(point, axes, building.roof_id, roofRise), point.y)).ToArray();
            float[] foundation = polygon.Select(point => TerrainSealedFoundationHeight(
                terrain, point, baseY, building.id)).ToArray();
            float[] cornerMagnitudes = new float[polygon.Length];
            for (int index = 0; index < polygon.Length; index++)
                if (FullMapBuildingCornerEncoding.TryCalculateConvexTurnMagnitude(
                        polygon, index, out float magnitude))
                    cornerMagnitudes[index] = magnitude;

            int frontWall = FrontWallIndex(polygon, building.seed);
            Vector2 frontNormal = ExteriorNormal(polygon, frontWall);
            if (includeWalls)
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a2 = polygon[i];
                Vector2 b2 = polygon[(i + 1) % polygon.Length];
                Vector3 a = new Vector3(a2.x, foundation[i], a2.y);
                Vector3 b = new Vector3(b2.x, foundation[(i + 1) % polygon.Length], b2.y);
                Vector3 c = roof[(i + 1) % polygon.Length];
                Vector3 d = roof[i];
                float length = Vector2.Distance(a2, b2);
                Vector2 outward = ExteriorNormal(polygon, i);
                float facadeRole = i == frontWall ? 0f :
                    Vector2.Dot(outward, frontNormal) < -0.72f ? 2f : 1f;
                Vector2 facadeSemantics = new Vector2(length, facadeRole);
                float packedStyleAndSecondary = building.facade_style_id +
                    (Mathf.Clamp(building.secondary_material_id, -1, 15) + 1) / 32f;
                Vector4 facadeParameters = new Vector4(
                    0f, building.material_id, building.height_dm * 0.1f, packedStyleAndSecondary);
                Color32 wallTint = FullMapBuildingCornerEncoding.ApplyToTint(
                    tint, cornerMagnitudes[i], cornerMagnitudes[(i + 1) % polygon.Length]);
                AddQuad(builder, a, b, c, d,
                    new Vector2(0f, a.y - baseY), new Vector2(length, b.y - baseY),
                    new Vector2(length, c.y - baseY), new Vector2(0f, d.y - baseY),
                    facadeSemantics, facadeParameters, wallTint);
            }

            if (!includeRoof) return;
            List<int> triangles = Triangulate(polygon, building.id);
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                // Polygon triangulation is CCW in X/Z; reverse for an upward Unity normal.
                AddTriangle(builder, roof[a], roof[c], roof[b], roofCell, roofParameters, tint, true);
            }
        }

        private static float TerrainSealedFoundationHeight(ProductionTerrainSurfaceAsset terrain,
            Vector2 point, float authoredBaseY, int buildingId)
        {
            if (!TrySampleTerrainAtMapEdge(terrain, point, out ProductionTerrainSample sample))
                throw new InvalidOperationException(
                    $"Authoritative terrain has no sample beneath building {buildingId} at {point}.");
            // base_y_dm is one center sample. Preserve that level building datum,
            // but extend every exterior wall below both it and the authoritative
            // terrain at that footprint vertex. The 1.60 m skirt exceeds the
            // documented 1.50 m maximum near-terrain simplification error.
            return Mathf.Min(authoredBaseY, sample.heightMeters) - FoundationEmbedMeters;
        }

        private static bool TrySampleTerrainAtMapEdge(ProductionTerrainSurfaceAsset terrain,
            Vector2 point, out ProductionTerrainSample sample)
        {
            if (terrain != null && terrain.TrySample(point.x, point.y, out sample)) return true;
            if (terrain == null)
            {
                sample = default;
                return false;
            }
            // Source inclusion is center-based, so a handful of boundary
            // footprints extend slightly beyond the 24 km terrain domain. Seal
            // those outer walls to the nearest authoritative boundary sample.
            Vector2 maximum = terrain.macroMinimumEnuMeters + terrain.macroSizeMeters;
            Vector2 clamped = new Vector2(
                Mathf.Clamp(point.x, terrain.macroMinimumEnuMeters.x, maximum.x),
                Mathf.Clamp(point.y, terrain.macroMinimumEnuMeters.y, maximum.y));
            return terrain.TrySample(clamped.x, clamped.y, out sample);
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

        private static List<int> Triangulate(Vector2[] polygon, int buildingId)
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
                    if (Cross(polygon[previous], polygon[current], polygon[next]) <= 0.000001f) continue;
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
                if (!clipped)
                {
                    Vector2[] unresolved = remaining.Select(index => polygon[index]).ToArray();
                    if (Mathf.Abs(SignedArea(unresolved)) <= 0.0001f)
                    {
                        // The visible area is already covered and the unresolved
                        // vertices form one collinear boundary chain. Preserve
                        // each wall/roof junction with deterministic degenerate
                        // edge triangles instead of silently dropping vertices.
                        for (int index = 1; index < remaining.Count - 1; index++)
                        {
                            result.Add(remaining[0]);
                            result.Add(remaining[index]);
                            result.Add(remaining[index + 1]);
                        }
                        remaining.Clear();
                    }
                    break;
                }
            }
            if (remaining.Count == 3)
            { result.Add(remaining[0]); result.Add(remaining[1]); result.Add(remaining[2]); }
            if (result.Count != (polygon.Length - 2) * 3 ||
                Enumerable.Range(0, polygon.Length).Any(vertex => !result.Contains(vertex)))
                throw new InvalidOperationException(
                    $"Building {buildingId} roof footprint could not be completely triangulated ({polygon.Length} vertices, {result.Count / 3} triangles).");
            return result;
        }

        private static float SignedArea(Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return 0f;
            // Compute relative to one local origin in double precision. Direct
            // float products of 12 km world coordinates lose the sub-metre
            // residual of narrow/collinear roof chains and can misclassify a
            // completed boundary as an unresolved polygon.
            Vector2 origin = polygon[0];
            double area = 0d;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i] - origin;
                Vector2 b = polygon[(i + 1) % polygon.Length] - origin;
                area += (double)a.x * b.y - (double)b.x * a.y;
            }
            return (float)(area * 0.5d);
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        private static Vector2 ExteriorNormal(Vector2[] polygon, int edgeIndex)
        {
            Vector2 edge = polygon[(edgeIndex + 1) % polygon.Length] - polygon[edgeIndex];
            return new Vector2(edge.y, -edge.x).normalized;
        }

        private static int FrontWallIndex(Vector2[] polygon, long seed)
        {
            // A stable longest-side frontage is preferable to stochastic doors on
            // every face. Source parcel/road frontage is not complete map-wide, so
            // this conservative rule is explicitly classified as inferred.
            float longest = -1f;
            List<int> candidates = new List<int>();
            for (int index = 0; index < polygon.Length; index++)
            {
                float length = Vector2.Distance(polygon[index], polygon[(index + 1) % polygon.Length]);
                if (length > longest + 0.05f)
                {
                    longest = length;
                    candidates.Clear();
                    candidates.Add(index);
                }
                else if (Mathf.Abs(length - longest) <= 0.05f) candidates.Add(index);
            }
            return candidates[(int)((seed & 0x7fffffff) % candidates.Count)];
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float ab = Cross(a, b, p), bc = Cross(b, c, p), ca = Cross(c, a, p);
            // A point on an ear boundary does not occupy its interior and must
            // not block clipping. Treating it as inside caused partial roofs on
            // footprints with collinear boundary vertices.
            return ab > 0.000001f && bc > 0.000001f && ca > 0.000001f;
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
            // Photographic source color remains authoritative. Variation is only
            // enough to avoid exact clones, never enough to change material family.
            float variation = 0.97f + (seed % 13) / 216f;
            float warmth = ((seed / 13) % 7 - 3) * 0.004f;
            return new Color(variation + warmth, variation, variation - warmth, 1f);
        }

        private static void WriteCompilationReport(Database database)
        {
            int[,] pageTriangles = new int[database.columns, database.rows];
            ProductionTerrainSurfaceAsset terrain = LoadRequired<ProductionTerrainSurfaceAsset>(TerrainSurfacePath);
            StableShellCompilationReport report = new StableShellCompilationReport
            {
                compiler_version = MeshCompilerVersion,
                foundation_embed_meters = FoundationEmbedMeters
            };
            foreach (Page page in database.pages)
            foreach (Building building in page.buildings ?? Array.Empty<Building>())
            {
                int[] ring = building.lod2_q;
                if (ring == null || ring.Length < 6 || ring.Length % 2 != 0) continue;
                float pageMinX = -database.map_half_extent_meters + page.column * database.page_size_meters;
                float pageMinZ = -database.map_half_extent_meters + page.row * database.page_size_meters;
                Vector2[] source = new Vector2[ring.Length / 2];
                for (int i = 0; i < source.Length; i++)
                    source[i] = new Vector2(pageMinX + ring[i * 2] * database.quantization_meters,
                        pageMinZ + ring[i * 2 + 1] * database.quantization_meters);
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
            // The runtime retains the accepted full-height 4x4 window and eight
            // additional connected pages. The sum of the globally heaviest 24 pages
            // is a conservative bound for every possible runtime selection.
            report.maximum_active_window_triangles = pageTriangles.Cast<int>()
                .OrderByDescending(value => value)
                .Take(AircraftCenteredBuildingClipmapController.SlotCount)
                .Sum();
            WatertightMetrics watertight = MeasureWatertightMeshes(database, terrain);
            report.terrain_sealed_wall_vertices = watertight.terrainSealedWallVertices;
            report.roof_sealed_wall_vertices = watertight.roofSealedWallVertices;
            report.maximum_roof_wall_seam_meters = watertight.maximumRoofWallSeamMeters;
            report.maximum_vertical_corner_seam_meters = watertight.maximumVerticalCornerSeamMeters;
            if (report.represented_buildings != ExpectedRepresentedBuildings)
                throw new InvalidOperationException("Stable shell compilation lost source buildings.");
            if (report.maximum_active_window_triangles > MaximumSelectedTriangleCeiling)
                throw new InvalidOperationException($"Stable shell window exceeds {MaximumSelectedTriangleCeiling:N0} triangles: {report.maximum_active_window_triangles:N0}.");
            if (report.maximum_inflation_percent > 5.01f)
                throw new InvalidOperationException($"Stable shell footprint inflation exceeds 5%: {report.maximum_inflation_percent:0.###}%.");

            File.WriteAllText(Absolute(CompilationReportPath), JsonUtility.ToJson(report, true));
            AssetDatabase.ImportAsset(CompilationReportPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static WatertightMetrics MeasureWatertightMeshes(Database database,
            ProductionTerrainSurfaceAsset terrain)
        {
            int terrainSealed = 0;
            int roofSealed = 0;
            foreach (Page page in database.pages)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(page.column, page.row));
                if (mesh == null)
                    throw new InvalidOperationException($"Compiled shell page is missing: {page.column},{page.row}.");
                Vector3[] vertices = mesh.vertices;
                List<Vector2> detailUvs = new List<Vector2>(vertices.Length);
                List<Vector4> parameters = new List<Vector4>(vertices.Length);
                mesh.GetUVs(0, detailUvs);
                mesh.GetUVs(2, parameters);
                if (detailUvs.Count != vertices.Length || parameters.Count != vertices.Length)
                    throw new InvalidOperationException($"Compiled shell semantics are incomplete: {page.column},{page.row}.");

                HashSet<PositionKey> roofPositions = new HashSet<PositionKey>();
                Dictionary<PositionKey, int> wallPositionUse = new Dictionary<PositionKey, int>();
                for (int index = 0; index < vertices.Length; index++)
                    if (parameters[index].x >= 0.5f)
                        roofPositions.Add(new PositionKey(vertices[index]));

                for (int index = 0; index < vertices.Length; index++)
                {
                    if (parameters[index].x >= 0.5f) continue;
                    Vector3 vertex = vertices[index];
                    PositionKey key = new PositionKey(vertex);
                    wallPositionUse.TryGetValue(key, out int uses);
                    wallPositionUse[key] = uses + 1;
                    if (detailUvs[index].y > 0.001f)
                    {
                        if (!roofPositions.Contains(key))
                            throw new InvalidOperationException(
                                $"Wall/roof seam exceeds 1 mm on page {page.column},{page.row} at {vertex}.");
                        roofSealed++;
                    }
                    else
                    {
                        if (!TrySampleTerrainAtMapEdge(terrain,
                                new Vector2(vertex.x, vertex.z), out ProductionTerrainSample sample))
                            throw new InvalidOperationException(
                                $"Terrain sample is missing beneath page {page.column},{page.row} at {vertex}.");
                        if (vertex.y > sample.heightMeters - FoundationEmbedMeters + 0.001f)
                            throw new InvalidOperationException(
                                $"Foundation does not clear the terrain-error skirt on page {page.column},{page.row} at {vertex}.");
                        terrainSealed++;
                    }
                }

                foreach (KeyValuePair<PositionKey, int> use in wallPositionUse)
                    if (use.Value < 2)
                        throw new InvalidOperationException(
                            $"Open vertical wall corner found on page {page.column},{page.row}.");
            }
            return new WatertightMetrics(terrainSealed, roofSealed, 0f, 0f);
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
                controller.pageTriangleCounts = new int[database.columns * database.rows];
                controller.pageBuildingCounts = new int[database.columns * database.rows];
                controller.pageRoofAreasSquareMeters = new float[database.columns * database.rows];
                foreach (Page page in database.pages)
                {
                    int index = page.row * database.columns + page.column;
                    Mesh pageMesh = LoadRequired<Mesh>(MeshPath(page.column, page.row));
                    controller.pageTriangleCounts[index] = (int)pageMesh.GetIndexCount(0) / 3;
                    Building[] buildings = page.buildings ?? Array.Empty<Building>();
                    controller.pageBuildingCounts[index] = buildings.Length;
                    controller.pageRoofAreasSquareMeters[index] = buildings.Sum(building =>
                        Mathf.Max(0f, building.area_m2));
                }
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

        private static void RefreshSerializedPageMetadata(Database database)
        {
            GameObject environment = PrefabUtility.LoadPrefabContents(EnvironmentPath);
            try
            {
                AircraftCenteredBuildingClipmapController controller =
                    environment.GetComponentInChildren<AircraftCenteredBuildingClipmapController>(true) ??
                    throw new InvalidOperationException("Serialized full-map building controller is missing.");
                PhotorealAerialSurfaceRoot surface =
                    environment.GetComponentInChildren<PhotorealAerialSurfaceRoot>(true) ??
                    throw new InvalidOperationException("Photoreal aerial-surface contract is missing.");
                controller.pageTriangleCounts = new int[database.columns * database.rows];
                controller.pageBuildingCounts = new int[database.columns * database.rows];
                controller.pageRoofAreasSquareMeters = new float[database.columns * database.rows];
                foreach (Page page in database.pages)
                {
                    int index = page.row * database.columns + page.column;
                    Mesh pageMesh = LoadRequired<Mesh>(MeshPath(page.column, page.row));
                    controller.pageTriangleCounts[index] = (int)pageMesh.GetIndexCount(0) / 3;
                    Building[] buildings = page.buildings ?? Array.Empty<Building>();
                    controller.pageBuildingCounts[index] = buildings.Length;
                    controller.pageRoofAreasSquareMeters[index] = buildings.Sum(building =>
                        Mathf.Max(0f, building.area_m2));
                }
                controller.authoredMaximumSelectedTriangles = AuthoredMaximumSelectedTriangles;
                controller.authoredContractVersion = AircraftCenteredBuildingClipmapController.ContractVersion;
                surface.representedBuildingCount = database.pages.Sum(page => page.buildings?.Length ?? 0);
                surface.bakedBuildingTriangleCount = AuthoredMaximumSelectedTriangles;
                ProductionEnvironmentRoot contract =
                    environment.GetComponent<ProductionEnvironmentRoot>() ??
                    throw new InvalidOperationException("Production environment contract is missing.");
                AerialSceneryRoot aerial =
                    environment.GetComponentInChildren<AerialSceneryRoot>(true) ??
                    throw new InvalidOperationException("Aerial scenery root is missing.");
                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                aerial.bakedRendererCount = budget.rendererCount;
                aerial.bakedSelectedTriangleCount = budget.triangleCount;
                aerial.bakedMaterialCount = budget.materialCount;
                EditorUtility.SetDirty(controller);
                EditorUtility.SetDirty(surface);
                EditorUtility.SetDirty(contract);
                EditorUtility.SetDirty(aerial);
                PrefabUtility.SaveAsPrefabAsset(environment, EnvironmentPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }
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
            Building[] buildings = database.pages.SelectMany(page => page.buildings ?? Array.Empty<Building>()).ToArray();
            if (buildings.Any(building => building.material_id < 0 || building.material_id >= 16 ||
                                          building.facade_style_id < 0 || building.facade_style_id > 5 ||
                                          building.semantic_class_id < 0 || building.semantic_class_id > 9 ||
                                          building.semantic_region_id < 0 || building.semantic_region_id > 6 ||
                                          building.material_rule_id < 0 || building.material_rule_id > 4))
                throw new InvalidOperationException("Full-map building facade V2 semantics are incomplete or out of range.");
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
