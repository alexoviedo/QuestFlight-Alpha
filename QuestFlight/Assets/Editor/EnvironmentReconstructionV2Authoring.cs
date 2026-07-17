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
    /// <summary>
    /// Offline authoring pass for the immutable Reconstruction V2 terrain and terrain-surface
    /// query asset. It updates serialized assets and the explicit product prefab; no runtime
    /// hierarchy construction or repair path is introduced.
    /// </summary>
    public static class EnvironmentReconstructionV2Authoring
    {
        public const string SourceDocumentPath =
            "Assets/Production/Environment/ReconstructionV2/KBDU_ProductionTerrainSurfaceV2.json";
        public const string SourceManifestPath =
            "Assets/Production/Environment/ReconstructionV2/KBDU_ProductionTerrainSurfaceV2_SourceManifest.json";
        public const string NormalMapPath =
            "Assets/Production/Environment/ReconstructionV2/KBDU_NearTerrainV2_Normal.png";
        public const string GeneratedFolder =
            "Assets/Production/Environment/ReconstructionV2/Generated";
        public const string AirportMeshPath = GeneratedFolder + "/KBDU_AirportTerrain_5m_V2.asset";
        public const string NearMeshPath = GeneratedFolder + "/KBDU_NearTerrain_25m_V2.asset";
        public const string SurfaceAssetPath = GeneratedFolder + "/KBDU_ProductionTerrainSurfaceV2.asset";
        public const string PlacementAssetPath = GeneratedFolder + "/KBDU_ProductionPlacementManifestV2.asset";
        public const string ParkingStandAssetPath = GeneratedFolder + "/KBDU_AirportParkingStandManifestV2.asset";
        private const string ContextDocumentPath =
            "Assets/Production/Environment/ReconstructionV2/KBDU_ReferenceContextV2.json";
        private const string PlacementSourceRelativePath =
            "tools/kbdu_environment_reconstruction/production_placement_manifest.json";
        private const string ParkingStandSourceRelativePath =
            "tools/kbdu_environment_reconstruction/airport_parking_stand_manifest.json";
        public const string EnvironmentPrefabPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";
        public const string DeterministicBakeUtc = "2026-07-17T08:30:00Z";

        [MenuItem("QuestFlight/Product/Integrate Environment Reconstruction V2 Terrain")]
        public static void RunInteractive() => Integrate();

        public static void RunBatch()
        {
            try
            {
                Integrate();
                Debug.Log("[QuestFlight][EnvironmentReconstructionV2] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void ValidateBatch()
        {
            try
            {
                ValidateIntegratedAssets();
                Debug.Log("[QuestFlight][EnvironmentReconstructionV2Validation] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Integrate()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            TextAsset sourceAsset = LoadRequired<TextAsset>(SourceDocumentPath);
            TextAsset sourceManifest = LoadRequired<TextAsset>(SourceManifestPath);
            TerrainDocument document = JsonUtility.FromJson<TerrainDocument>(sourceAsset.text);
            if (document == null || document.layers == null || document.layers.Length != 4)
                throw new InvalidOperationException("Reconstruction V2 terrain document must contain airport, near, mid, and far layers.");

            TerrainLayer airport = document.layers.Single(layer => string.Equals(layer.zone, "Airport", StringComparison.Ordinal));
            TerrainLayer near = document.layers.Single(layer => string.Equals(layer.zone, "Near", StringComparison.Ordinal));
            TerrainLayer mid = document.layers.Single(layer => string.Equals(layer.zone, "Mid", StringComparison.Ordinal));
            TerrainLayer far = document.layers.Single(layer => string.Equals(layer.zone, "Far", StringComparison.Ordinal));
            float[] airportHeights = DecodeHeights(airport);
            float[] nearHeights = DecodeHeights(near);
            float[] midHeights = DecodeHeights(mid);
            float[] farHeights = DecodeHeights(far);
            EnsureFolder(GeneratedFolder);

            Mesh airportMesh = BuildGridMesh(airport, airportHeights, null, "KBDU_AirportTerrain_5m_V2", 0f);
            Rect airportCutout = new Rect(
                airport.minimum_enu_meters[0],
                airport.minimum_enu_meters[1],
                airport.maximum_enu_meters[0] - airport.minimum_enu_meters[0],
                airport.maximum_enu_meters[1] - airport.minimum_enu_meters[1]);
            Mesh nearMesh = BuildGridMesh(near, nearHeights, airportCutout, "KBDU_NearTerrain_25m_V2", 0f);
            SaveMesh(airportMesh, AirportMeshPath);
            SaveMesh(nearMesh, NearMeshPath);

            ProductionTerrainSurfaceAsset surface = BuildSurfaceAsset(
                document,
                airport,
                airportHeights,
                near,
                nearHeights,
                mid,
                midHeights,
                far,
                farHeights,
                sourceManifest.text);
            SaveScriptableObject(surface, SurfaceAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            surface = LoadRequired<ProductionTerrainSurfaceAsset>(SurfaceAssetPath);
            BuildPlacementAssets(surface);
            ConfigureNormalTexture();
            ConfigureTerrainMaterial();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            IntegrateEnvironmentPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateIntegratedAssets();
        }

        private static Mesh BuildGridMesh(TerrainLayer layer, float[] heights, Rect? cutout, string name, float boundarySkirtDepth)
        {
            int expected = layer.width * layer.height;
            if (heights.Length != expected)
                throw new InvalidOperationException($"{layer.id} height payload has {heights.Length} values; expected {expected}.");
            Vector3[] vertices = new Vector3[expected];
            Vector2[] uvs = new Vector2[expected];
            float minimumX = layer.minimum_enu_meters[0];
            float minimumZ = layer.minimum_enu_meters[1];
            for (int z = 0; z < layer.height; z++)
            {
                for (int x = 0; x < layer.width; x++)
                {
                    int index = z * layer.width + x;
                    float east = minimumX + x * layer.spacing_meters;
                    float north = minimumZ + z * layer.spacing_meters;
                    vertices[index] = new Vector3(east, heights[index], north);
                    uvs[index] = new Vector2((east + 12000f) / 24000f, (north + 12000f) / 24000f);
                }
            }

            List<int> triangles = new List<int>((layer.width - 1) * (layer.height - 1) * 6);
            for (int z = 0; z < layer.height - 1; z++)
            {
                for (int x = 0; x < layer.width - 1; x++)
                {
                    float centerEast = minimumX + (x + 0.5f) * layer.spacing_meters;
                    float centerNorth = minimumZ + (z + 0.5f) * layer.spacing_meters;
                    if (cutout.HasValue && cutout.Value.Contains(new Vector2(centerEast, centerNorth))) continue;
                    int a = z * layer.width + x;
                    int b = a + 1;
                    int c = a + layer.width;
                    int d = c + 1;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            List<Vector3> authoredVertices = vertices.ToList();
            List<Vector2> authoredUvs = uvs.ToList();
            if (boundarySkirtDepth > 0f)
            {
                AddSkirtEdge(Enumerable.Range(0, layer.width).ToArray(), boundarySkirtDepth,
                    authoredVertices, authoredUvs, triangles);
                AddSkirtEdge(Enumerable.Range(0, layer.height).Select(z => z * layer.width + layer.width - 1).ToArray(),
                    boundarySkirtDepth, authoredVertices, authoredUvs, triangles);
                AddSkirtEdge(Enumerable.Range(0, layer.width).Select(x => (layer.height - 1) * layer.width + layer.width - 1 - x).ToArray(),
                    boundarySkirtDepth, authoredVertices, authoredUvs, triangles);
                AddSkirtEdge(Enumerable.Range(0, layer.height).Select(z => (layer.height - 1 - z) * layer.width).ToArray(),
                    boundarySkirtDepth, authoredVertices, authoredUvs, triangles);
            }

            Mesh mesh = new Mesh
            {
                name = name,
                indexFormat = authoredVertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = authoredVertices.ToArray(),
                uv = authoredUvs.ToArray(),
                triangles = triangles.ToArray()
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddSkirtEdge(
            int[] topIndices,
            float depth,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles)
        {
            int bottomStart = vertices.Count;
            foreach (int topIndex in topIndices)
            {
                vertices.Add(vertices[topIndex] + Vector3.down * depth);
                uvs.Add(uvs[topIndex]);
            }
            for (int index = 0; index < topIndices.Length - 1; index++)
            {
                int topA = topIndices[index];
                int topB = topIndices[index + 1];
                int bottomA = bottomStart + index;
                int bottomB = bottomStart + index + 1;
                triangles.Add(topA);
                triangles.Add(topB);
                triangles.Add(bottomA);
                triangles.Add(topB);
                triangles.Add(bottomB);
                triangles.Add(bottomA);
            }
        }

        private static ProductionTerrainSurfaceAsset BuildSurfaceAsset(
            TerrainDocument document,
            TerrainLayer airport,
            float[] airportHeights,
            TerrainLayer near,
            float[] nearHeights,
            TerrainLayer mid,
            float[] midHeights,
            TerrainLayer far,
            float[] farHeights,
            string sourceManifestJson)
        {
            ProductionTerrainSurfaceAsset surface = ScriptableObject.CreateInstance<ProductionTerrainSurfaceAsset>();
            surface.name = "KBDU_ProductionTerrainSurfaceV2";
            surface.schemaVersion = document.schema_version.ToString();
            surface.sourceManifestSha256 = Sha256(sourceManifestJson);
            surface.macroMinimumEnuMeters = new Vector2(-12000f, -12000f);
            surface.macroSizeMeters = new Vector2(24000f, 24000f);
            surface.layers = new[]
            {
                MakeSurfaceLayer(airport, airportHeights, ProductionTerrainZone.Airport, 0),
                MakeSurfaceLayer(near, nearHeights, ProductionTerrainZone.Near, 10),
                MakeSurfaceLayer(mid, midHeights, ProductionTerrainZone.Mid, 20),
                MakeSurfaceLayer(far, farHeights, ProductionTerrainZone.Far, 30)
            };

            Vector2 runway08 = new Vector2(
                document.runway_blend.end_08_enu_meters[0],
                document.runway_blend.end_08_enu_meters[1]);
            Vector2 runway26 = new Vector2(
                document.runway_blend.end_26_enu_meters[0],
                document.runway_blend.end_26_enu_meters[1]);
            Vector2 direction = (runway26 - runway08).normalized;
            Vector2 lateral = new Vector2(-direction.y, direction.x) * document.runway_blend.half_width_meters;
            surface.regions = new[]
            {
                new ProductionTerrainSurfaceAsset.SurfaceRegion
                {
                    stableId = "faa-kbdu-runway-08-26",
                    category = ProductionSurfaceCategory.Runway,
                    priority = 0,
                    boundaryEnuMeters = new[]
                    {
                        runway08 + lateral,
                        runway26 + lateral,
                        runway26 - lateral,
                        runway08 - lateral
                    }
                },
                new ProductionTerrainSurfaceAsset.SurfaceRegion
                {
                    stableId = "osm-kbdu-west-apron",
                    category = ProductionSurfaceCategory.Apron,
                    priority = 5,
                    boundaryEnuMeters = new[]
                    {
                        new Vector2(-430f, -220f),
                        new Vector2(-220f, -220f),
                        new Vector2(-220f, -120f),
                        new Vector2(-430f, -120f)
                    }
                }
            };
            return surface;
        }

        private static ProductionTerrainSurfaceAsset.GridLayer MakeSurfaceLayer(
            TerrainLayer source,
            float[] heights,
            ProductionTerrainZone zone,
            int priority)
        {
            return new ProductionTerrainSurfaceAsset.GridLayer
            {
                stableId = source.id,
                zone = zone,
                priority = priority,
                minimumEnuMeters = new Vector2(source.minimum_enu_meters[0], source.minimum_enu_meters[1]),
                spacingMeters = source.spacing_meters,
                width = source.width,
                height = source.height,
                heightMeters = heights,
                normals = CalculateNormals(source, heights)
            };
        }

        private static Vector3[] CalculateNormals(TerrainLayer layer, float[] heights)
        {
            Vector3[] normals = new Vector3[heights.Length];
            for (int z = 0; z < layer.height; z++)
            {
                int z0 = Mathf.Max(0, z - 1);
                int z1 = Mathf.Min(layer.height - 1, z + 1);
                for (int x = 0; x < layer.width; x++)
                {
                    int x0 = Mathf.Max(0, x - 1);
                    int x1 = Mathf.Min(layer.width - 1, x + 1);
                    float dx = (heights[z * layer.width + x1] - heights[z * layer.width + x0]) /
                               Mathf.Max((x1 - x0) * layer.spacing_meters, 0.001f);
                    float dz = (heights[z1 * layer.width + x] - heights[z0 * layer.width + x]) /
                               Mathf.Max((z1 - z0) * layer.spacing_meters, 0.001f);
                    normals[z * layer.width + x] = new Vector3(-dx, 1f, -dz).normalized;
                }
            }
            return normals;
        }

        private static void ConfigureNormalTexture()
        {
            TextureImporter importer = AssetImporter.GetAtPath(NormalMapPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Near terrain source-normal importer is missing.");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.maxTextureSize = 1024;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static void ConfigureTerrainMaterial()
        {
            Material material = LoadRequired<Material>(TerrainMaterialPath);
            Texture2D normal = LoadRequired<Texture2D>(NormalMapPath);
            if (!material.HasProperty("_NearSourceNormal"))
                throw new InvalidOperationException("Terrain shader is missing the Reconstruction V2 source-normal property.");
            material.SetTexture("_NearSourceNormal", normal);
            material.SetFloat("_NearSourceNormalStrength", 0.72f);
            EditorUtility.SetDirty(material);
        }

        private static void IntegrateEnvironmentPrefab()
        {
            Mesh airportMesh = LoadRequired<Mesh>(AirportMeshPath);
            Mesh nearMesh = LoadRequired<Mesh>(NearMeshPath);
            Material terrainMaterial = LoadRequired<Material>(TerrainMaterialPath);
            ProductionTerrainSurfaceAsset surface = LoadRequired<ProductionTerrainSurfaceAsset>(SurfaceAssetPath);
            ProductionPlacementManifest placements = LoadRequired<ProductionPlacementManifest>(PlacementAssetPath);
            AirportParkingStandManifest stands = LoadRequired<AirportParkingStandManifest>(ParkingStandAssetPath);
            GameObject root = PrefabUtility.LoadPrefabContents(EnvironmentPrefabPath);
            try
            {
                MeshFilter airport = FindRequired(root.transform, "Terrain_AirportPatch_USGS").GetComponent<MeshFilter>();
                MeshFilter near = FindRequired(root.transform, "Terrain_Near4km_USGS").GetComponent<MeshFilter>();
                if (airport == null || near == null)
                    throw new InvalidOperationException("Serialized near terrain MeshFilters are missing.");
                airport.sharedMesh = airportMesh;
                near.sharedMesh = nearMesh;
                ConfigureRenderer(airport.GetComponent<MeshRenderer>(), terrainMaterial);
                ConfigureRenderer(near.GetComponent<MeshRenderer>(), terrainMaterial);

                Transform obsoleteTransition = FindDescendant(root.transform, "Terrain_AirportPatch_Near_Transition");
                if (obsoleteTransition != null) obsoleteTransition.gameObject.SetActive(false);

                ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
                if (contract == null) throw new InvalidOperationException("ProductionEnvironmentRoot marker is missing.");
                contract.nearTerrainMeshes = new[] { airport, near };
                contract.authoritativeTerrainSurface = surface;
                contract.productionPlacementManifest = placements;
                contract.airportParkingStandManifest = stands;
                contract.bakeToolVersion = "environment_reconstruction_v2";
                contract.bakeUtc = DeterministicBakeUtc;
                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                EditorUtility.SetDirty(contract);
                PrefabUtility.SaveAsPrefabAsset(root, EnvironmentPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ValidateIntegratedAssets()
        {
            ProductionTerrainSurfaceAsset surface = LoadRequired<ProductionTerrainSurfaceAsset>(SurfaceAssetPath);
            ProductionPlacementManifest placements = LoadRequired<ProductionPlacementManifest>(PlacementAssetPath);
            AirportParkingStandManifest stands = LoadRequired<AirportParkingStandManifest>(ParkingStandAssetPath);
            if (!surface.TrySample(0f, 0f, out ProductionTerrainSample airport) || airport.zone != ProductionTerrainZone.Airport)
                throw new InvalidOperationException("Airport terrain surface priority/sample failed.");
            if (!surface.TrySample(1500f, 1500f, out ProductionTerrainSample near) || near.zone != ProductionTerrainZone.Near)
                throw new InvalidOperationException("Near terrain surface sample failed.");
            Mesh airportMesh = LoadRequired<Mesh>(AirportMeshPath);
            Mesh nearMesh = LoadRequired<Mesh>(NearMeshPath);
            int runtimeTriangles = airportMesh.triangles.Length / 3 + nearMesh.triangles.Length / 3;
            if (runtimeTriangles > 130000)
                throw new InvalidOperationException($"V2 near terrain exceeds 130,000 triangles: {runtimeTriangles}.");
            TextureImporter importer = AssetImporter.GetAtPath(NormalMapPath) as TextureImporter;
            if (importer == null || importer.textureType != TextureImporterType.Default || importer.sRGBTexture ||
                !importer.mipmapEnabled)
                throw new InvalidOperationException("V2 near source-normal import contract failed.");

            GameObject prefab = LoadRequired<GameObject>(EnvironmentPrefabPath);
            ProductionEnvironmentRoot contract = prefab.GetComponent<ProductionEnvironmentRoot>();
            if (contract == null || contract.authoritativeTerrainSurface != surface)
                throw new InvalidOperationException("Production prefab does not bind the authoritative terrain surface.");
            int waterPlacements = placements.entries == null
                ? -1
                : placements.entries.Count(entry => entry.category == ProductionPlacementCategory.Water);
            bool completePlacementInventory = placements.entries != null &&
                placements.entries.Count(entry => entry.category == ProductionPlacementCategory.Building) == 27679 &&
                placements.entries.Count(entry => entry.category == ProductionPlacementCategory.Tree ||
                                                  entry.category == ProductionPlacementCategory.Shrub) == 377 &&
                placements.entries.Count(entry => entry.category == ProductionPlacementCategory.ParkedAircraft) == 3 &&
                (waterPlacements == 0 || waterPlacements == 277);
            if (contract.productionPlacementManifest != placements || !completePlacementInventory)
                throw new InvalidOperationException("Production prefab does not bind the complete V2 placement manifest.");
            if (contract.airportParkingStandManifest != stands || stands.stands == null || stands.stands.Length != 5)
                throw new InvalidOperationException("Production prefab does not bind the authored airport parking stands.");
            if (contract.nearTerrainMeshes == null || contract.nearTerrainMeshes.Length != 2 ||
                contract.nearTerrainMeshes[0].sharedMesh != airportMesh || contract.nearTerrainMeshes[1].sharedMesh != nearMesh)
                throw new InvalidOperationException("Production prefab does not bind both V2 near meshes.");
        }

        private static void BuildPlacementAssets(ProductionTerrainSurfaceAsset surface)
        {
            string placementJson = File.ReadAllText(RepositoryPath(PlacementSourceRelativePath));
            string standJson = File.ReadAllText(RepositoryPath(ParkingStandSourceRelativePath));
            PlacementDocument placementSource = JsonUtility.FromJson<PlacementDocument>(placementJson);
            ParkingStandDocument standSource = JsonUtility.FromJson<ParkingStandDocument>(standJson);
            ContextDocument context = JsonUtility.FromJson<ContextDocument>(LoadRequired<TextAsset>(ContextDocumentPath).text);
            if (placementSource?.placements == null || placementSource.placements.Length != 28059)
                throw new InvalidOperationException("V2 placement source must contain exactly 28,059 reviewed records.");
            if (standSource?.stands == null || standSource.stands.Length != 5)
                throw new InvalidOperationException("V2 parking-stand source must contain exactly five authored stands.");
            if (context?.openstreetmap?.features == null)
                throw new InvalidOperationException("KBDU reference context is missing OSM features.");

            ProductionPlacementManifest manifest = ScriptableObject.CreateInstance<ProductionPlacementManifest>();
            manifest.name = "KBDU_ProductionPlacementManifestV2";
            manifest.schemaVersion = placementSource.schema_version.ToString();
            manifest.terrainSurfaceAssetGuid = AssetDatabase.AssetPathToGUID(SurfaceAssetPath);
            manifest.sourceManifestSha256 = Sha256(placementJson);
            manifest.entries = placementSource.placements.Select(item => BuildPlacementEntry(item, context, surface)).ToArray();
            SaveScriptableObject(manifest, PlacementAssetPath);

            AirportParkingStandManifest stands = ScriptableObject.CreateInstance<AirportParkingStandManifest>();
            stands.name = "KBDU_AirportParkingStandManifestV2";
            stands.schemaVersion = standSource.schema_version.ToString();
            stands.stands = standSource.stands.Select((item, index) => BuildStand(item, standSource.stands, index, surface)).ToArray();
            SaveScriptableObject(stands, ParkingStandAssetPath);
        }

        private static ProductionPlacementManifest.Entry BuildPlacementEntry(
            PlacementRecord source,
            ContextDocument context,
            ProductionTerrainSurfaceAsset surface)
        {
            Vector2 location = new Vector2(source.local_enu_meters.x, source.local_enu_meters.z);
            if (!surface.TrySample(location.x, location.y, out ProductionTerrainSample terrain))
                throw new InvalidOperationException("Placement lies outside authoritative terrain: " + source.stable_id);

            Vector2[] footprint;
            string sourceFeatureId = source.stable_id;
            ProductionPlacementCategory category;
            ProductionLodClass lodClass;
            ProductionFoundationMode foundationMode = ProductionFoundationMode.ContactOffset;
            float foundationMaximumResidual = 0f;
            float contactOffset = 0f;
            Vector3[] contactPoints = Array.Empty<Vector3>();
            float axisCorrection = source.model_forward_axis_correction_degrees;

            if (string.Equals(source.category, "building", StringComparison.Ordinal))
            {
                category = ProductionPlacementCategory.Building;
                lodClass = source.lod_hlod != null && !string.IsNullOrEmpty(source.lod_hlod.@class) &&
                           source.lod_hlod.@class.IndexOf("hlod", StringComparison.OrdinalIgnoreCase) >= 0
                    ? ProductionLodClass.MidHlod
                    : ProductionLodClass.NearInstanced;
                int featureIndex = source.footprint_reference.feature_index;
                if (featureIndex < 0 || featureIndex >= context.openstreetmap.features.Length)
                    throw new InvalidOperationException("Building footprint index is invalid: " + source.stable_id);
                ContextFeature feature = context.openstreetmap.features[featureIndex];
                footprint = DecodeFootprint(feature.points_q, context.coordinate_quantization_meters);
                sourceFeatureId = $"osm:{source.source_feature.osm_type}/{source.source_feature.osm_id}/{source.source_feature.part}";
                if (!surface.TryBestFitPlane(footprint, out ProductionTerrainPlane plane))
                    throw new InvalidOperationException("Could not fit building foundation plane: " + source.stable_id);
                // The best-fit plane point is the sample centroid, which is not
                // guaranteed to match the source feature's authored center after
                // quantization. Seat the root on the fitted plane at the actual
                // authored ENU location; the skirt handles footprint residuals.
                terrain = new ProductionTerrainSample(
                    plane.HeightAt(location.x, location.y),
                    plane.normal,
                    terrain.zone,
                    terrain.macroUv,
                    terrain.category);
                foundationMode = plane.maximumResidualMeters <= 0.15f
                    ? ProductionFoundationMode.BestFitPlane
                    : ProductionFoundationMode.FoundationSkirt;
                foundationMaximumResidual = plane.maximumResidualMeters;
            }
            else if (string.Equals(source.category, "vegetation", StringComparison.Ordinal))
            {
                if (source.distribution == null ||
                    !string.Equals(source.distribution.status, "V2_MASK_AND_EXCLUSIONS_VALIDATED", StringComparison.Ordinal))
                    throw new InvalidOperationException("Vegetation lacks V2 mask/exclusion validation: " + source.stable_id);
                category = !string.IsNullOrEmpty(source.species_id) &&
                           source.species_id.IndexOf("shrub", StringComparison.OrdinalIgnoreCase) >= 0
                    ? ProductionPlacementCategory.Shrub
                    : ProductionPlacementCategory.Tree;
                lodClass = ProductionLodClass.NearInstanced;
                float radius = Mathf.Max(source.scale_cues.crown_radius_meters * 0.2f, 0.35f);
                footprint = SquareFootprint(location, radius);
                contactOffset = -0.05f;
                sourceFeatureId = source.species_id;
            }
            else if (string.Equals(source.category, "parked_aircraft", StringComparison.Ordinal))
            {
                category = ProductionPlacementCategory.ParkedAircraft;
                lodClass = ProductionLodClass.NearAuthored;
                footprint = RectangleFootprint(location, 4.8f, 3.8f, source.root_yaw_degrees);
                contactPoints = source.contact?.gear_contact_points_unity_meters == null
                    ? Array.Empty<Vector3>()
                    : new[]
                    {
                        ToVector3(source.contact.gear_contact_points_unity_meters.main_left),
                        ToVector3(source.contact.gear_contact_points_unity_meters.main_right),
                        ToVector3(source.contact.gear_contact_points_unity_meters.nose)
                    };
                sourceFeatureId = source.stand_id;
            }
            else
            {
                throw new InvalidOperationException("Unsupported V2 placement category: " + source.category);
            }

            float[] scale = source.scale ?? new[] { 1f, 1f, 1f };
            return new ProductionPlacementManifest.Entry
            {
                stableId = source.stable_id,
                sourceFeatureId = sourceFeatureId,
                speciesId = source.species_id,
                placementValidationStatus = source.distribution?.status ?? string.Empty,
                category = category,
                localEnuMeters = location,
                authoredHeadingDegrees = string.Equals(source.category, "parked_aircraft", StringComparison.Ordinal)
                    ? source.root_yaw_degrees
                    : source.authored_heading_degrees,
                modelForwardAxisCorrectionDegrees = axisCorrection,
                scale = new Vector3(scale[0], scale[1], scale[2]),
                terrainHeightMeters = terrain.heightMeters,
                terrainNormal = terrain.normal,
                contactPointOffsetMeters = contactOffset,
                footprintEnuMeters = footprint,
                foundationMode = foundationMode,
                foundationMaximumResidualMeters = foundationMaximumResidual,
                lodClass = lodClass,
                hlodCellId = source.lod_hlod?.cell_id ?? string.Empty,
                sourceLicenseClass = source.source_license_class,
                modelContactPoints = contactPoints
            };
        }

        private static AirportParkingStandManifest.Stand BuildStand(
            ParkingStandRecord source,
            ParkingStandRecord[] all,
            int sourceIndex,
            ProductionTerrainSurfaceAsset surface)
        {
            Vector2 location = new Vector2(source.local_enu_meters.x, source.local_enu_meters.z);
            if (!surface.TrySample(location.x, location.y, out ProductionTerrainSample terrain))
                throw new InvalidOperationException("Parking stand lies outside authoritative terrain: " + source.id);
            float minimumSpacing = float.MaxValue;
            for (int index = 0; index < all.Length; index++)
            {
                if (index == sourceIndex) continue;
                Vector2 other = new Vector2(all[index].local_enu_meters.x, all[index].local_enu_meters.z);
                minimumSpacing = Mathf.Min(minimumSpacing, Vector2.Distance(location, other));
            }
            return new AirportParkingStandManifest.Stand
            {
                standId = source.id,
                localEnuMeters = location,
                headingDegrees = source.heading_degrees,
                apronPlanePoint = new Vector3(location.x, terrain.heightMeters, location.y),
                apronPlaneNormal = terrain.normal,
                permittedAircraftClass = source.permitted_aircraft_class,
                minimumSpacingMeters = minimumSpacing,
                apronBoundaryEnuMeters = SquareFootprint(location, 11f),
                sourceReference = $"OSM apron {source.source_reference.apron_osm_id}; taxilane {source.source_reference.taxilane_osm_id}"
            };
        }

        private static Vector2[] DecodeFootprint(int[] points, float quantization)
        {
            if (points == null || points.Length < 6 || (points.Length & 1) != 0)
                throw new InvalidOperationException("Placement footprint has invalid quantized coordinates.");
            Vector2[] result = new Vector2[points.Length / 2];
            for (int index = 0; index < result.Length; index++)
                result[index] = new Vector2(points[index * 2] * quantization, points[index * 2 + 1] * quantization);
            return result;
        }

        private static Vector2[] SquareFootprint(Vector2 center, float halfExtent) => new[]
        {
            center + new Vector2(-halfExtent, -halfExtent),
            center + new Vector2(halfExtent, -halfExtent),
            center + new Vector2(halfExtent, halfExtent),
            center + new Vector2(-halfExtent, halfExtent)
        };

        private static Vector2[] RectangleFootprint(Vector2 center, float halfLength, float halfWidth, float heading)
        {
            Quaternion rotation = Quaternion.Euler(0f, heading, 0f);
            return new[]
            {
                center + RotateXZ(rotation, new Vector2(-halfWidth, -halfLength)),
                center + RotateXZ(rotation, new Vector2(halfWidth, -halfLength)),
                center + RotateXZ(rotation, new Vector2(halfWidth, halfLength)),
                center + RotateXZ(rotation, new Vector2(-halfWidth, halfLength))
            };
        }

        private static Vector2 RotateXZ(Quaternion rotation, Vector2 point)
        {
            Vector3 value = rotation * new Vector3(point.x, 0f, point.y);
            return new Vector2(value.x, value.z);
        }

        private static Vector3 ToVector3(float[] value) =>
            value != null && value.Length == 3 ? new Vector3(value[0], value[1], value[2]) : Vector3.zero;

        private static string RepositoryPath(string relativePath) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../..", relativePath));

        private static float[] DecodeHeights(TerrainLayer layer)
        {
            byte[] bytes = Convert.FromBase64String(layer.height_cm_little_endian_base64);
            int count = layer.width * layer.height;
            if (bytes.Length != count * sizeof(short))
                throw new InvalidOperationException($"{layer.id} height payload byte count is invalid.");
            float[] heights = new float[count];
            for (int index = 0; index < count; index++)
                heights[index] = BitConverter.ToInt16(bytes, index * sizeof(short)) * layer.height_quantization_meters;
            return heights;
        }

        private static void ConfigureRenderer(MeshRenderer renderer, Material material)
        {
            if (renderer == null) throw new InvalidOperationException("Terrain renderer is missing.");
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            EditorUtility.SetDirty(renderer);
        }

        private static void SaveMesh(Mesh mesh, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else
            {
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
            }
        }

        private static void SaveScriptableObject(ScriptableObject value, string path)
        {
            ScriptableObject existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (existing == null) AssetDatabase.CreateAsset(value, path);
            else
            {
                EditorUtility.CopySerialized(value, existing);
                UnityEngine.Object.DestroyImmediate(value);
                EditorUtility.SetDirty(existing);
            }
        }

        private static string Sha256(string text)
        {
            using (System.Security.Cryptography.SHA256 algorithm = System.Security.Cryptography.SHA256.Create())
            {
                return string.Concat(algorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))
                    .Select(value => value.ToString("x2")));
            }
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null) throw new InvalidOperationException("Required asset is missing: " + path);
            return value;
        }

        private static Transform FindRequired(Transform root, string name)
        {
            Transform value = FindDescendant(root, name);
            if (value == null) throw new InvalidOperationException("Required authored object is missing: " + name);
            return value;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => string.Equals(item.name, name, StringComparison.Ordinal));
        }

        private static void EnsureFolder(string path)
        {
            string current = "Assets";
            foreach (string segment in path.Split('/').Skip(1))
            {
                string next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segment);
                current = next;
            }
        }

        [Serializable]
        private sealed class TerrainDocument
        {
            public int schema_version;
            public TerrainLayer[] layers;
            public RunwayBlend runway_blend;
        }

        [Serializable]
        private sealed class TerrainLayer
        {
            public string id;
            public string zone;
            public float[] minimum_enu_meters;
            public float[] maximum_enu_meters;
            public float spacing_meters;
            public int width;
            public int height;
            public float height_quantization_meters;
            public string height_cm_little_endian_base64;
        }

        [Serializable]
        private sealed class RunwayBlend
        {
            public float[] end_08_enu_meters;
            public float[] end_26_enu_meters;
            public float half_width_meters;
        }

        [Serializable]
        private sealed class ContextDocument
        {
            public float coordinate_quantization_meters;
            public OsmPayload openstreetmap;
        }

        [Serializable]
        private sealed class OsmPayload
        {
            public ContextFeature[] features;
        }

        [Serializable]
        private sealed class ContextFeature
        {
            public int[] points_q;
        }

        [Serializable]
        private sealed class PlacementDocument
        {
            public int schema_version;
            public PlacementRecord[] placements;
        }

        [Serializable]
        private sealed class PlacementRecord
        {
            public string stable_id;
            public string category;
            public float authored_heading_degrees;
            public float model_forward_axis_correction_degrees;
            public float root_yaw_degrees;
            public Float2 local_enu_meters;
            public float[] scale;
            public FootprintReference footprint_reference;
            public SourceFeature source_feature;
            public LodHlod lod_hlod;
            public ScaleCues scale_cues;
            public AircraftContact contact;
            public string source_license_class;
            public string stand_id;
            public string species_id;
            public VegetationDistribution distribution;
        }

        [Serializable]
        private sealed class Float2
        {
            public float x;
            public float z;
        }

        [Serializable]
        private sealed class FootprintReference
        {
            public int feature_index;
        }

        [Serializable]
        private sealed class SourceFeature
        {
            public long osm_id;
            public string osm_type;
            public string part;
        }

        [Serializable]
        private sealed class LodHlod
        {
            public string cell_id;
            public string @class;
        }

        [Serializable]
        private sealed class ScaleCues
        {
            public float crown_radius_meters;
            public float height_meters;
        }

        [Serializable]
        private sealed class VegetationDistribution
        {
            public int likelihood_score;
            public string source_class;
            public string status;
        }

        [Serializable]
        private sealed class AircraftContact
        {
            public AircraftGearPoints gear_contact_points_unity_meters;
        }

        [Serializable]
        private sealed class AircraftGearPoints
        {
            public float[] main_left;
            public float[] main_right;
            public float[] nose;
        }

        [Serializable]
        private sealed class ParkingStandDocument
        {
            public int schema_version;
            public ParkingStandRecord[] stands;
        }

        [Serializable]
        private sealed class ParkingStandRecord
        {
            public string id;
            public Float2 local_enu_meters;
            public float heading_degrees;
            public string permitted_aircraft_class;
            public StandSourceReference source_reference;
        }

        [Serializable]
        private sealed class StandSourceReference
        {
            public long apron_osm_id;
            public long taxilane_osm_id;
        }
    }
}
