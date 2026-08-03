using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment.Editor
{
    /// <summary>
    /// Builds the bounded near-field grass and airport infrastructure into a
    /// fixed prefab, then integrates that prefab into the authored product
    /// environment. Nothing is constructed or repaired at runtime.
    /// </summary>
    public static class AirportNearFieldDetailAuthoring
    {
        public const string Root =
            "Assets/Production/Environment/AirportNearFieldV1";
        public const string DataFolder = Root + "/Data";
        public const string GeneratedFolder = Root + "/Generated";
        public const string ManifestPath =
            DataFolder + "/AirportNearFieldSourceV1.json";
        public const string SharedMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/" +
            "KBDU_ContextVegetation.mat";
        public const string GrassMeshPath =
            GeneratedFolder + "/AirportGrassClump.asset";
        public const string GrassAtlasPath =
            GeneratedFolder + "/AirportGrassAtlas_RGBA.png";
        public const string StaticMeshPath =
            GeneratedFolder + "/AirportNearFieldCombined.asset";
        public const string PrefabPath =
            Root + "/KBDU_AirportNearFieldDetail.prefab";
        public const string ProductEnvironmentPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string TrafficManifestPath =
            "Assets/Production/Environment/TrafficV1/Data/" +
            "KBDU_TrafficManifestV1.json";

        private const int GrassInstanceCount = 1000;

        [MenuItem("QuestFlight/Airport Near Field V1/Build And Integrate")]
        public static void BuildAndIntegrate()
        {
            EnsureFolder(Root);
            EnsureFolder(DataFolder);
            EnsureFolder(GeneratedFolder);

            TextAsset manifest = LoadRequired<TextAsset>(ManifestPath);
            Material material = LoadRequired<Material>(SharedMaterialPath);
            Texture2D grassAtlas = BuildGrassAtlas();
            material.SetTexture("_GrassAtlas", grassAtlas);
            material.SetFloat("_GrassCutoff", 0.34f);
            EditorUtility.SetDirty(material);
            Mesh grassMesh = SaveMesh(BuildGrassClump(), GrassMeshPath);

            GameObject product =
                PrefabUtility.LoadPrefabContents(ProductEnvironmentPath);
            try
            {
                ProductionEnvironmentRoot environment =
                    product.GetComponent<ProductionEnvironmentRoot>();
                if (environment == null ||
                    environment.authoritativeTerrainSurface == null ||
                    environment.airportContext == null)
                    throw new InvalidOperationException(
                        "Product terrain/airport authority is unavailable.");

                VisualGroundSampler visibleGround =
                    VisualGroundSampler.Create(product, environment);
                RoadCorridorExclusion roadCorridors =
                    RoadCorridorExclusion.Create();
                PhotorealAerialSurfaceRoot surface = product
                    .GetComponentInChildren<PhotorealAerialSurfaceRoot>(true);
                if (surface == null || surface.nearOrthophoto == null)
                    throw new InvalidOperationException(
                        "Near orthophoto is unavailable for grass color matching.");
                AirportNearFieldGrassRenderer.Instance[] grassInstances;
                using (OrthophotoColorSampler colorSampler =
                       OrthophotoColorSampler.Create(
                           surface.nearOrthophoto))
                {
                    grassInstances = BuildGrassInstances(
                        environment.authoritativeTerrainSurface,
                        visibleGround,
                        roadCorridors,
                        colorSampler);
                }
                Mesh staticMesh = SaveMesh(
                    BuildStaticDetailMesh(
                        environment.authoritativeTerrainSurface,
                        visibleGround),
                    StaticMeshPath);

                GameObject authored =
                    new GameObject(AirportNearFieldDetailRoot.GameObjectName);
                try
                {
                    AirportNearFieldDetailRoot marker =
                        authored.AddComponent<AirportNearFieldDetailRoot>();
                    marker.sourceManifest = manifest;
                    marker.sharedMaterial = material;

                    GameObject grassObject = new GameObject("InstancedGrass");
                    grassObject.transform.SetParent(authored.transform, false);
                    AirportNearFieldGrassRenderer grass =
                        grassObject.AddComponent<AirportNearFieldGrassRenderer>();
                    grass.Configure(
                        grassMesh,
                        material,
                        manifest,
                        grassInstances);

                    GameObject staticObject =
                        new GameObject("CombinedInfrastructureAndGroundDetail");
                    staticObject.transform.SetParent(authored.transform, false);
                    MeshFilter filter = staticObject.AddComponent<MeshFilter>();
                    filter.sharedMesh = staticMesh;
                    MeshRenderer renderer =
                        staticObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.ForceNoMotion;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.scaleInLightmap = 0f;
                    GameObjectUtility.SetStaticEditorFlags(
                        staticObject,
                        (StaticEditorFlags)0);

                    marker.grass = grass;
                    marker.staticDetailRenderer = renderer;
                    marker.authoredGrassInstanceCount = grassInstances.Length;
                    marker.authoredStaticTriangleCount =
                        checked((int)staticMesh.GetIndexCount(0) / 3);
                    marker.authoredMaximumSelectedDraws = 2;
                    marker.authoredMaximumSelectedTriangles = checked(
                        marker.authoredStaticTriangleCount +
                        (int)grass.AuthoredMaximumTriangleCount);
                    if (!marker.TryValidateContract(out string markerReport))
                        throw new InvalidOperationException(
                            "Near-field authoring failed: " + markerReport);

                    PrefabUtility.SaveAsPrefabAsset(authored, PrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(authored);
                }

                Integrate(product, environment);
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "[QuestFlight][AirportNearFieldV1] " +
                    "SERIALIZED_INTEGRATION_SUCCESS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(product);
            }

            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        private static Texture2D BuildGrassAtlas()
        {
            const int size = 512;
            const int cellSize = size / 2;
            Color[] pixels = new Color[size * size];
            System.Random random = new System.Random(472901);
            for (int cellY = 0; cellY < 2; cellY++)
            {
                for (int cellX = 0; cellX < 2; cellX++)
                {
                    int bladeCount = 54 + cellX * 5 + cellY * 4;
                    for (int blade = 0; blade < bladeCount; blade++)
                    {
                        float baseX = cellX * cellSize +
                            Mathf.Lerp(
                                cellSize * 0.08f,
                                cellSize * 0.92f,
                                (float)random.NextDouble());
                        float baseY = cellY * cellSize +
                            Mathf.Lerp(
                                cellSize * 0.025f,
                                cellSize * 0.10f,
                                (float)random.NextDouble());
                        float height = Mathf.Lerp(
                            cellSize * 0.48f,
                            cellSize * 0.94f,
                            (float)random.NextDouble());
                        float lean = Mathf.Lerp(
                            -cellSize * 0.19f,
                            cellSize * 0.19f,
                            (float)random.NextDouble());
                        float curve = Mathf.Lerp(
                            -cellSize * 0.11f,
                            cellSize * 0.11f,
                            (float)random.NextDouble());
                        Vector2 start = new Vector2(baseX, baseY);
                        Vector2 control = new Vector2(
                            baseX + lean * 0.35f + curve,
                            baseY + height * 0.52f);
                        Vector2 end = new Vector2(
                            baseX + lean,
                            baseY + height);
                        float width = Mathf.Lerp(
                            3.8f,
                            6.4f,
                            (float)random.NextDouble());
                        float luminance = Mathf.Lerp(
                            0.72f,
                            1.0f,
                            (float)random.NextDouble());
                        PaintGrassBlade(
                            pixels,
                            size,
                            start,
                            control,
                            end,
                            width,
                            luminance);
                    }
                }
            }

            Texture2D generated = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = "AirportGrassAtlas_RGBA",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            generated.SetPixels(pixels);
            generated.Apply(false, false);
            byte[] png = generated.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(generated);

            string projectRoot =
                Directory.GetParent(Application.dataPath)?.FullName ??
                throw new InvalidOperationException(
                    "Unity project root is unavailable.");
            string absolutePath = Path.Combine(
                projectRoot,
                GrassAtlasPath.Replace('/', Path.DirectorySeparatorChar));
            bool write = !File.Exists(absolutePath) ||
                !File.ReadAllBytes(absolutePath).SequenceEqual(png);
            if (write)
                File.WriteAllBytes(absolutePath, png);
            AssetDatabase.ImportAsset(
                GrassAtlasPath,
                ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer =
                (TextureImporter)AssetImporter.GetAtPath(GrassAtlasPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource =
                TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.maxTextureSize = size;
            TextureImporterPlatformSettings android =
                importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = size;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.textureCompression =
                TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
            return LoadRequired<Texture2D>(GrassAtlasPath);
        }

        private static void PaintGrassBlade(
            Color[] pixels,
            int size,
            Vector2 start,
            Vector2 control,
            Vector2 end,
            float baseRadius,
            float luminance)
        {
            const int samples = 96;
            for (int sample = 0; sample < samples; sample++)
            {
                float t = sample / (float)(samples - 1);
                float oneMinusT = 1f - t;
                Vector2 point =
                    oneMinusT * oneMinusT * start +
                    2f * oneMinusT * t * control +
                    t * t * end;
                float radius = Mathf.Lerp(
                    baseRadius,
                    1.05f,
                    Mathf.Pow(t, 0.78f));
                float bladeLuminance = luminance *
                    Mathf.Lerp(0.72f, 1.08f, t);
                PaintDisc(
                    pixels,
                    size,
                    point,
                    radius,
                    bladeLuminance);
            }
        }

        private static void PaintDisc(
            Color[] pixels,
            int size,
            Vector2 center,
            float radius,
            float luminance)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius - 1f));
            int maxX = Mathf.Min(
                size - 1,
                Mathf.CeilToInt(center.x + radius + 1f));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius - 1f));
            int maxY = Mathf.Min(
                size - 1,
                Mathf.CeilToInt(center.y + radius + 1f));
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float distance = Vector2.Distance(
                        new Vector2(x + 0.5f, y + 0.5f),
                        center);
                    float edge = Mathf.InverseLerp(
                        radius - 0.85f,
                        radius + 0.85f,
                        distance);
                    float alpha = 1f - Mathf.SmoothStep(0f, 1f, edge);
                    if (alpha <= 0f)
                        continue;
                    int index = y * size + x;
                    Color prior = pixels[index];
                    float combinedAlpha = Mathf.Max(prior.a, alpha);
                    float shade = Mathf.Clamp01(luminance);
                    Color bladeColor = new Color(
                        shade * 0.96f,
                        shade,
                        shade * 0.82f,
                        combinedAlpha);
                    pixels[index] = prior.a >= alpha
                        ? new Color(
                            prior.r,
                            prior.g,
                            prior.b,
                            combinedAlpha)
                        : bladeColor;
                }
            }
        }

        private static void Integrate(
            GameObject product,
            ProductionEnvironmentRoot environment)
        {
            AirportNearFieldDetailRoot[] prior = product
                .GetComponentsInChildren<AirportNearFieldDetailRoot>(true);
            foreach (AirportNearFieldDetailRoot marker in prior)
                UnityEngine.Object.DestroyImmediate(marker.gameObject);

            GameObject prefab = LoadRequired<GameObject>(PrefabPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                prefab,
                environment.airportContext);
            instance.name = AirportNearFieldDetailRoot.GameObjectName;
            instance.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            environment.airportNearFieldDetail = instance.transform;

            ProductionEnvironmentBudget budget = environment.CalculateBudget();
            environment.bakedRendererCount = budget.rendererCount;
            environment.bakedTriangleCount = budget.triangleCount;
            environment.bakedMaterialCount = budget.materialCount;
            // Preserve the owning terrain bake identity; this overlay does not
            // replace or re-author the photoreal aerial surface.
            environment.bakeToolVersion = "photoreal_aerial_surface_v1";
            EditorUtility.SetDirty(environment);
            if (!environment.TryValidateContract(out string report))
                throw new InvalidOperationException(
                    "Near-field integration leaves product invalid: " + report);
            PrefabUtility.SaveAsPrefabAsset(product, ProductEnvironmentPath);
            Debug.Log(
                $"[QuestFlight][AirportNearFieldV1] budget " +
                $"draws={budget.rendererCount}, triangles={budget.triangleCount}, " +
                $"materials={budget.materialCount}");
        }

        private static Mesh BuildGrassClump()
        {
            MeshBuilder builder = new MeshBuilder();
            System.Random random = new System.Random(31819);
            const int cardCount = 4;
            for (int card = 0; card < cardCount; card++)
            {
                float angle = card * Mathf.PI / cardCount +
                    (float)(random.NextDouble() - 0.5) * 0.10f;
                float halfWidth =
                    0.68f + (float)random.NextDouble() * 0.12f;
                float height =
                    0.32f + (float)random.NextDouble() * 0.10f;
                Vector3 across = new Vector3(
                    Mathf.Cos(angle),
                    0f,
                    Mathf.Sin(angle)) * halfWidth;
                Vector3 lean = new Vector3(
                    Mathf.Cos(angle + Mathf.PI * 0.5f),
                    0f,
                    Mathf.Sin(angle + Mathf.PI * 0.5f)) *
                    ((float)random.NextDouble() - 0.5f) * 0.10f;
                int atlasX = card & 1;
                int atlasY = card >> 1;
                const float inset = 0.002f;
                Rect atlasRect = new Rect(
                    atlasX * 0.5f + inset,
                    atlasY * 0.5f + inset,
                    0.5f - inset * 2f,
                    0.5f - inset * 2f);
                builder.AddGrassCard(
                    -across,
                    across,
                    -across * 0.86f + lean + Vector3.up * height,
                    across * 0.86f + lean + Vector3.up * height,
                    atlasRect,
                    Color.white);
            }
            return builder.ToMesh("AirportGrassClump");
        }

        private static AirportNearFieldGrassRenderer.Instance[]
            BuildGrassInstances(
                ProductionTerrainSurfaceAsset terrain,
                VisualGroundSampler visibleGround,
                RoadCorridorExclusion roadCorridors,
                OrthophotoColorSampler colorSampler)
        {
            List<AirportNearFieldGrassRenderer.Instance> instances =
                new List<AirportNearFieldGrassRenderer.Instance>(
                    GrassInstanceCount);
            System.Random random = new System.Random(864233);
            int attempts = 0;
            while (instances.Count < GrassInstanceCount && attempts < 20000)
            {
                attempts++;
                int zone = attempts % 20;
                float x;
                float z;
                if (zone < 7)
                {
                    x = Mathf.Lerp(
                        -620f,
                        620f,
                        (float)random.NextDouble());
                    z = Mathf.Lerp(
                        -11.5f,
                        16.5f,
                        (float)random.NextDouble());
                }
                else if (zone < 14)
                {
                    x = Mathf.Lerp(
                        -620f,
                        620f,
                        (float)random.NextDouble());
                    z = Mathf.Lerp(
                        -62f,
                        -48.2f,
                        (float)random.NextDouble());
                }
                else if (zone < 18)
                {
                    x = Mathf.Lerp(
                        -620f,
                        620f,
                        (float)random.NextDouble());
                    z = Mathf.Lerp(
                        43f,
                        91f,
                        (float)random.NextDouble());
                }
                else
                {
                    x = Mathf.Lerp(
                        -690f,
                        -470f,
                        (float)random.NextDouble());
                    z = Mathf.Lerp(
                        -10f,
                        88f,
                        (float)random.NextDouble());
                }

                if (!TryGround(terrain, visibleGround, x, z, out Vector3 ground,
                        out ProductionTerrainSample sample))
                    continue;
                if (sample.IsRunwayOrApron || sample.IsWater ||
                    sample.category == ProductionSurfaceCategory.Road)
                    continue;
                if (roadCorridors.IsExcluded(x, z, 4.25f))
                    continue;
                if (Mathf.Abs(z + 30f) < 17.5f ||
                    Mathf.Abs(z - 30f) < 6.5f)
                    continue;

                float uniform = Mathf.Lerp(
                    1.00f,
                    1.42f,
                    (float)random.NextDouble());
                float heightScale = uniform * Mathf.Lerp(
                    0.86f,
                    1.04f,
                    (float)random.NextDouble());
                float rotation = (float)random.NextDouble() * 360f;
                Vector3 position = ground + Vector3.up * 0.018f;
                Matrix4x4 matrix = Matrix4x4.TRS(
                    position,
                    Quaternion.Euler(0f, rotation, 0f),
                    new Vector3(uniform, heightScale, uniform));
                Color groundColor = colorSampler.Sample(x, z);
                float brightness = Mathf.Lerp(
                    1.04f,
                    1.20f,
                    (float)random.NextDouble());
                Color grassColor = Color.Lerp(
                    groundColor,
                    new Color(0.34f, 0.34f, 0.15f, 1f),
                    0.16f);
                Vector4 tint = new Vector4(
                    Mathf.Clamp01(grassColor.r * brightness),
                    Mathf.Clamp01(grassColor.g * brightness),
                    Mathf.Clamp01(grassColor.b * brightness),
                    1f);
                instances.Add(new AirportNearFieldGrassRenderer.Instance(
                    matrix,
                    tint,
                    position + Vector3.up * (heightScale * 0.48f),
                    heightScale));
            }

            if (instances.Count != GrassInstanceCount)
                throw new InvalidOperationException(
                    $"Only authored {instances.Count}/{GrassInstanceCount} " +
                    "valid grass placements.");
            return instances.ToArray();
        }

        private static Mesh BuildStaticDetailMesh(
            ProductionTerrainSurfaceAsset terrain,
            VisualGroundSampler visibleGround)
        {
            MeshBuilder builder = new MeshBuilder();
            AddRunwayShoulders(builder, terrain, visibleGround);
            AddUtilityCorridor(builder, terrain, visibleGround);
            AddRunwayEdgeLights(builder, terrain, visibleGround);
            AddWindsock(builder, terrain, visibleGround);
            AddTaxiSigns(builder, terrain, visibleGround);
            return builder.ToMesh("AirportNearFieldCombined");
        }

        private static void AddRunwayShoulders(
            MeshBuilder builder,
            ProductionTerrainSurfaceAsset terrain,
            VisualGroundSampler visibleGround)
        {
            Color dirtA = new Color(0.25f, 0.22f, 0.13f, 1f);
            Color dirtB = new Color(0.35f, 0.30f, 0.18f, 1f);
            const int intervals = 8;
            for (int side = 0; side < 2; side++)
            {
                List<Vector3> inner = new List<Vector3>(intervals + 1);
                List<Vector3> outer = new List<Vector3>(intervals + 1);
                float baseZ = side == 0 ? -43.0f : -17.0f;
                float direction = side == 0 ? -1f : 1f;
                for (int index = 0; index <= intervals; index++)
                {
                    float t = index / (float)intervals;
                    float x = Mathf.Lerp(-628f, 620f, t);
                    float irregular =
                        Mathf.Sin(index * 1.73f + side * 0.6f) * 0.28f +
                        Mathf.Sin(index * 0.37f) * 0.18f;
                    float innerZ = baseZ + irregular * 0.18f;
                    float outerZ =
                        baseZ + direction * (1.15f + irregular * 0.45f);
                    inner.Add(GroundPoint(
                        terrain, visibleGround, x, innerZ, 0.024f));
                    outer.Add(GroundPoint(
                        terrain, visibleGround, x, outerZ, 0.026f));
                }
                builder.AddStrip(inner, outer, dirtA, dirtB);
            }
        }

        private static void AddUtilityCorridor(
            MeshBuilder builder,
            ProductionTerrainSurfaceAsset terrain,
            VisualGroundSampler visibleGround)
        {
            Color poleColor = new Color(0.25f, 0.20f, 0.14f, 1f);
            Color wireColor = new Color(0.10f, 0.105f, 0.10f, 1f);
            const int poleCount = 8;
            Vector3[,] wireAnchors = new Vector3[poleCount, 2];
            for (int pole = 0; pole < poleCount; pole++)
            {
                float t = pole / (float)(poleCount - 1);
                float x = Mathf.Lerp(-690f, 690f, t);
                float z = 121f + Mathf.Sin(t * Mathf.PI * 1.6f) * 3.2f;
                Vector3 ground =
                    GroundPoint(terrain, visibleGround, x, z, 0f);
                const float poleHeight = 10.2f;
                builder.AddDoubleSidedQuad(
                    ground + new Vector3(-0.16f, 0f, 0f),
                    ground + new Vector3(0.16f, 0f, 0f),
                    ground + new Vector3(0.16f, poleHeight, 0f),
                    ground + new Vector3(-0.16f, poleHeight, 0f),
                    poleColor);
                // The corridor runs east/west (X), so the crossarm must span
                // north/south (Z) to support both side-by-side conductors.
                builder.AddDoubleSidedQuad(
                    ground + new Vector3(0f, 9.38f, -2.1f),
                    ground + new Vector3(0f, 9.38f, 2.1f),
                    ground + new Vector3(0f, 9.62f, 2.1f),
                    ground + new Vector3(0f, 9.62f, -2.1f),
                    poleColor);
                for (int wire = 0; wire < 2; wire++)
                {
                    float lateral = wire == 0 ? -1.55f : 1.55f;
                    wireAnchors[pole, wire] =
                        ground + new Vector3(0f, 9.80f, lateral);
                }
            }
            for (int span = 0; span < poleCount - 1; span++)
            {
                for (int wire = 0; wire < 2; wire++)
                {
                    Vector3 start = wireAnchors[span, wire];
                    Vector3 end = wireAnchors[span + 1, wire];
                    Vector3 middle = Vector3.Lerp(start, end, 0.5f);
                    middle.y -= 0.72f;
                    builder.AddDoubleSidedRibbon(
                        start,
                        middle,
                        0.045f,
                        wireColor);
                    builder.AddDoubleSidedRibbon(
                        middle,
                        end,
                        0.045f,
                        wireColor);
                }
            }
        }

        private static void AddRunwayEdgeLights(
            MeshBuilder builder,
            ProductionTerrainSurfaceAsset terrain,
            VisualGroundSampler visibleGround)
        {
            Color baseColor = new Color(0.34f, 0.35f, 0.32f, 1f);
            Color lensColor = new Color(0.74f, 0.80f, 0.68f, 1f);
            for (int index = 0; index < 8; index++)
            {
                float x = Mathf.Lerp(-590f, 585f, index / 7f);
                for (int side = 0; side < 2; side++)
                {
                    float z = side == 0 ? -44.2f : -15.8f;
                    Vector3 ground =
                        GroundPoint(terrain, visibleGround, x, z, 0.02f);
                    builder.AddDoubleSidedQuad(
                        ground + new Vector3(0f, 0f, -0.09f),
                        ground + new Vector3(0f, 0f, 0.09f),
                        ground + new Vector3(0f, 0.62f, 0.12f),
                        ground + new Vector3(0f, 0.62f, -0.12f),
                        Color.Lerp(baseColor, lensColor, 0.44f));
                }
            }
        }

        private static void AddWindsock(
            MeshBuilder builder,
            ProductionTerrainSurfaceAsset terrain,
            VisualGroundSampler visibleGround)
        {
            Vector3 ground =
                GroundPoint(terrain, visibleGround, -79.5f, -123f, 0f);
            Color metal = new Color(0.42f, 0.44f, 0.43f, 1f);
            Color orange = new Color(0.86f, 0.26f, 0.055f, 1f);
            builder.AddDoubleSidedQuad(
                ground + new Vector3(0f, 0f, -0.09f),
                ground + new Vector3(0f, 0f, 0.09f),
                ground + new Vector3(0f, 6.6f, 0.09f),
                ground + new Vector3(0f, 6.6f, -0.09f),
                metal);
            Vector3 mast = ground + Vector3.up * 6.45f;
            // Crossed faces keep the sock readable from the runway sightline
            // without adding a material or transparent pass.
            builder.AddDoubleSidedQuad(
                mast,
                mast + new Vector3(3.5f, -0.45f, 0f),
                mast + new Vector3(3.5f, -1.12f, 0f),
                mast + new Vector3(0f, -0.32f, 0f),
                orange);
            builder.AddDoubleSidedQuad(
                mast,
                mast + new Vector3(0f, -0.45f, 3.5f),
                mast + new Vector3(0f, -1.12f, 3.5f),
                mast + new Vector3(0f, -0.32f, 0f),
                orange);
        }

        private static void AddTaxiSigns(
            MeshBuilder builder,
            ProductionTerrainSurfaceAsset terrain,
            VisualGroundSampler visibleGround)
        {
            Vector2[] positions =
            {
                new Vector2(-430f, -52f),
                new Vector2(-210f, -52f),
                new Vector2(115f, -52f),
                new Vector2(430f, -52f),
                new Vector2(-310f, 8f),
                new Vector2(300f, 8f)
            };
            Color sign = new Color(0.16f, 0.13f, 0.055f, 1f);
            Color post = new Color(0.36f, 0.36f, 0.32f, 1f);
            foreach (Vector2 position in positions)
            {
                Vector3 ground = GroundPoint(
                    terrain,
                    visibleGround,
                    position.x,
                    position.y,
                    0.02f);
                Color signColor = Color.Lerp(sign, post, 0.08f);
                Quaternion facing = Quaternion.Euler(0f, 38f, 0f);
                Vector3 left = facing * new Vector3(-0.875f, 0f, 0f);
                Vector3 right = facing * new Vector3(0.875f, 0f, 0f);
                builder.AddDoubleSidedQuad(
                    ground + left + Vector3.up * 0.20f,
                    ground + right + Vector3.up * 0.20f,
                    ground + right + Vector3.up * 1.02f,
                    ground + left + Vector3.up * 1.02f,
                    signColor);
            }
        }

        private static bool TryGround(
            ProductionTerrainSurfaceAsset terrain,
            VisualGroundSampler visibleGround,
            float x,
            float z,
            out Vector3 ground,
            out ProductionTerrainSample sample)
        {
            if (!terrain.TrySample(x, z, out sample))
            {
                ground = default;
                return false;
            }
            float height = sample.heightMeters;
            if (visibleGround.TrySample(new Vector2(x, z), out float visible))
                height = visible;
            ground = new Vector3(x, height, z);
            return true;
        }

        private static Vector3 GroundPoint(
            ProductionTerrainSurfaceAsset terrain,
            VisualGroundSampler visibleGround,
            float x,
            float z,
            float lift)
        {
            if (!TryGround(
                    terrain,
                    visibleGround,
                    x,
                    z,
                    out Vector3 ground,
                    out _))
                throw new InvalidOperationException(
                    $"Terrain sample failed at ({x:F1}, {z:F1}).");
            return ground + Vector3.up * lift;
        }

        private static Mesh SaveMesh(Mesh generated, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static T LoadRequired<T>(string path)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException(
                    $"Required asset is missing: {path}");
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }

        [Serializable]
        private sealed class TrafficRouteManifest
        {
            public TrafficRouteRecord[] routes;
        }

        [Serializable]
        private sealed class TrafficRouteRecord
        {
            public TrafficRouteSample[] samples;
        }

        [Serializable]
        private sealed class TrafficRouteSample
        {
            public float x;
            public float z;
        }

        private sealed class OrthophotoColorSampler : IDisposable
        {
            private readonly Texture2D source;

            private OrthophotoColorSampler(Texture2D readableSource)
            {
                source = readableSource;
            }

            public static OrthophotoColorSampler Create(Texture2D asset)
            {
                string assetPath = AssetDatabase.GetAssetPath(asset);
                string projectRoot =
                    Directory.GetParent(Application.dataPath)?.FullName ??
                    throw new InvalidOperationException(
                        "Unity project root is unavailable.");
                string absolutePath = Path.Combine(
                    projectRoot,
                    assetPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(absolutePath))
                    throw new FileNotFoundException(
                        "Near orthophoto source is missing.",
                        absolutePath);
                Texture2D readable = new Texture2D(
                    2,
                    2,
                    TextureFormat.RGB24,
                    false,
                    false)
                {
                    name = "AirportGrassOrthophotoColorSampler"
                };
                if (!readable.LoadImage(
                        File.ReadAllBytes(absolutePath),
                        false))
                {
                    UnityEngine.Object.DestroyImmediate(readable);
                    throw new InvalidOperationException(
                        "Near orthophoto could not be decoded.");
                }
                return new OrthophotoColorSampler(readable);
            }

            public Color Sample(float x, float z)
            {
                float u = Mathf.Clamp01((x + 2000f) / 4000f);
                float v = Mathf.Clamp01((z + 2000f) / 4000f);
                return source.GetPixelBilinear(u, v);
            }

            public void Dispose()
            {
                if (source != null)
                    UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private sealed class RoadCorridorExclusion
        {
            private readonly Segment[] segments;

            private RoadCorridorExclusion(Segment[] authoredSegments)
            {
                segments = authoredSegments;
            }

            public static RoadCorridorExclusion Create()
            {
                TextAsset source = LoadRequired<TextAsset>(
                    TrafficManifestPath);
                TrafficRouteManifest manifest =
                    JsonUtility.FromJson<TrafficRouteManifest>(source.text);
                if (manifest?.routes == null || manifest.routes.Length == 0)
                    throw new InvalidOperationException(
                        "Traffic route manifest has no road corridors.");

                List<Segment> bounded = new List<Segment>();
                foreach (TrafficRouteRecord route in manifest.routes)
                {
                    if (route?.samples == null || route.samples.Length < 2)
                        continue;
                    for (int index = 0;
                         index < route.samples.Length - 1;
                         index++)
                    {
                        Vector2 start = new Vector2(
                            route.samples[index].x,
                            route.samples[index].z);
                        Vector2 end = new Vector2(
                            route.samples[index + 1].x,
                            route.samples[index + 1].z);
                        if (Mathf.Max(start.x, end.x) < -710f ||
                            Mathf.Min(start.x, end.x) > 710f ||
                            Mathf.Max(start.y, end.y) < -155f ||
                            Mathf.Min(start.y, end.y) > 125f)
                            continue;
                        bounded.Add(new Segment(start, end));
                    }
                }
                if (bounded.Count == 0)
                    throw new InvalidOperationException(
                        "No airport-adjacent road segments were found.");
                return new RoadCorridorExclusion(bounded.ToArray());
            }

            public bool IsExcluded(float x, float z, float clearance)
            {
                Vector2 point = new Vector2(x, z);
                float clearanceSquared = clearance * clearance;
                foreach (Segment segment in segments)
                {
                    if (point.x < segment.minimum.x - clearance ||
                        point.x > segment.maximum.x + clearance ||
                        point.y < segment.minimum.y - clearance ||
                        point.y > segment.maximum.y + clearance)
                        continue;
                    if (DistanceSquaredToSegment(
                            point,
                            segment.start,
                            segment.end) <= clearanceSquared)
                        return true;
                }
                return false;
            }

            private static float DistanceSquaredToSegment(
                Vector2 point,
                Vector2 start,
                Vector2 end)
            {
                Vector2 direction = end - start;
                float lengthSquared = direction.sqrMagnitude;
                if (lengthSquared <= 0.000001f)
                    return (point - start).sqrMagnitude;
                float t = Mathf.Clamp01(
                    Vector2.Dot(point - start, direction) / lengthSquared);
                Vector2 nearest = start + direction * t;
                return (point - nearest).sqrMagnitude;
            }

            private readonly struct Segment
            {
                public readonly Vector2 start;
                public readonly Vector2 end;
                public readonly Vector2 minimum;
                public readonly Vector2 maximum;

                public Segment(Vector2 authoredStart, Vector2 authoredEnd)
                {
                    start = authoredStart;
                    end = authoredEnd;
                    minimum = Vector2.Min(start, end);
                    maximum = Vector2.Max(start, end);
                }
            }
        }

        private sealed class VisualGroundSampler
        {
            private readonly Vector3[] vertices;
            private readonly int[] triangles;

            private VisualGroundSampler(
                MeshFilter visibleSurface,
                Transform coordinateFrame)
            {
                Vector3[] source = visibleSurface.sharedMesh.vertices;
                vertices = new Vector3[source.Length];
                for (int index = 0; index < source.Length; index++)
                {
                    Vector3 world =
                        visibleSurface.transform.TransformPoint(source[index]);
                    vertices[index] =
                        coordinateFrame.InverseTransformPoint(world);
                }
                triangles = visibleSurface.sharedMesh.triangles;
            }

            public static VisualGroundSampler Create(
                GameObject product,
                ProductionEnvironmentRoot environment)
            {
                AerialSceneryRoot aerial = product
                    .GetComponentInChildren<AerialSceneryRoot>(true);
                if (aerial == null ||
                    aerial.authoritativeAirportPatch == null ||
                    aerial.authoritativeAirportPatch.sharedMesh == null)
                    throw new InvalidOperationException(
                        "Active airport visual surface is unavailable.");
                return new VisualGroundSampler(
                    aerial.authoritativeAirportPatch,
                    environment.airportContext);
            }

            public bool TrySample(Vector2 point, out float height)
            {
                bool found = false;
                height = float.NegativeInfinity;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = vertices[triangles[index]];
                    Vector3 b = vertices[triangles[index + 1]];
                    Vector3 c = vertices[triangles[index + 2]];
                    if (!TryBarycentric(
                            point,
                            new Vector2(a.x, a.z),
                            new Vector2(b.x, b.z),
                            new Vector2(c.x, c.z),
                            out Vector3 weights))
                        continue;
                    float candidate =
                        a.y * weights.x +
                        b.y * weights.y +
                        c.y * weights.z;
                    if (!found || candidate > height)
                    {
                        height = candidate;
                        found = true;
                    }
                }
                return found;
            }

            private static bool TryBarycentric(
                Vector2 p,
                Vector2 a,
                Vector2 b,
                Vector2 c,
                out Vector3 weights)
            {
                Vector2 v0 = b - a;
                Vector2 v1 = c - a;
                Vector2 v2 = p - a;
                float denominator =
                    v0.x * v1.y - v1.x * v0.y;
                if (Mathf.Abs(denominator) <= 0.000001f)
                {
                    weights = default;
                    return false;
                }
                float v =
                    (v2.x * v1.y - v1.x * v2.y) / denominator;
                float w =
                    (v0.x * v2.y - v2.x * v0.y) / denominator;
                float u = 1f - v - w;
                weights = new Vector3(u, v, w);
                const float epsilon = -0.0001f;
                return u >= epsilon && v >= epsilon && w >= epsilon;
            }
        }

        private sealed class MeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int> triangles = new List<int>();
            private readonly List<Color> colors = new List<Color>();
            private readonly List<Vector2> uvs = new List<Vector2>();

            public void AddGrassCard(
                Vector3 bottomLeft,
                Vector3 bottomRight,
                Vector3 topLeft,
                Vector3 topRight,
                Rect atlasRect,
                Color color)
            {
                Vector2 uvBottomLeft = new Vector2(
                    2f + atlasRect.xMin,
                    atlasRect.yMin);
                Vector2 uvBottomRight = new Vector2(
                    2f + atlasRect.xMax,
                    atlasRect.yMin);
                Vector2 uvTopLeft = new Vector2(
                    2f + atlasRect.xMin,
                    atlasRect.yMax);
                Vector2 uvTopRight = new Vector2(
                    2f + atlasRect.xMax,
                    atlasRect.yMax);
                AddTriangle(
                    bottomLeft,
                    bottomRight,
                    topRight,
                    color,
                    color,
                    color,
                    uvBottomLeft,
                    uvBottomRight,
                    uvTopRight);
                AddTriangle(
                    bottomLeft,
                    topRight,
                    topLeft,
                    color,
                    color,
                    color,
                    uvBottomLeft,
                    uvTopRight,
                    uvTopLeft);
                AddTriangle(
                    topRight,
                    bottomRight,
                    bottomLeft,
                    color,
                    color,
                    color,
                    uvTopRight,
                    uvBottomRight,
                    uvBottomLeft);
                AddTriangle(
                    topLeft,
                    topRight,
                    bottomLeft,
                    color,
                    color,
                    color,
                    uvTopLeft,
                    uvTopRight,
                    uvBottomLeft);
            }

            public void AddStrip(
                IReadOnlyList<Vector3> inner,
                IReadOnlyList<Vector3> outer,
                Color innerColor,
                Color outerColor)
            {
                for (int index = 0; index < inner.Count - 1; index++)
                {
                    AddQuad(
                        inner[index],
                        inner[index + 1],
                        outer[index + 1],
                        outer[index],
                        innerColor,
                        innerColor,
                        outerColor,
                        outerColor);
                }
            }

            public void AddBox(
                Vector3 center,
                Vector3 size,
                Quaternion rotation,
                Color color)
            {
                Vector3 half = size * 0.5f;
                Vector3[] corners =
                {
                    rotation * new Vector3(-half.x, -half.y, -half.z) + center,
                    rotation * new Vector3( half.x, -half.y, -half.z) + center,
                    rotation * new Vector3( half.x,  half.y, -half.z) + center,
                    rotation * new Vector3(-half.x,  half.y, -half.z) + center,
                    rotation * new Vector3(-half.x, -half.y,  half.z) + center,
                    rotation * new Vector3( half.x, -half.y,  half.z) + center,
                    rotation * new Vector3( half.x,  half.y,  half.z) + center,
                    rotation * new Vector3(-half.x,  half.y,  half.z) + center
                };
                AddQuad(corners[0], corners[3], corners[2], corners[1],
                    color, color, color, color);
                AddQuad(corners[4], corners[5], corners[6], corners[7],
                    color, color, color, color);
                AddQuad(corners[0], corners[4], corners[7], corners[3],
                    color, color, color, color);
                AddQuad(corners[1], corners[2], corners[6], corners[5],
                    color, color, color, color);
                AddQuad(corners[3], corners[7], corners[6], corners[2],
                    color, color, color, color);
                AddQuad(corners[0], corners[1], corners[5], corners[4],
                    color, color, color, color);
            }

            public void AddCylinder(
                Vector3 baseCenter,
                float height,
                float radius,
                int sides,
                Color color)
            {
                for (int side = 0; side < sides; side++)
                {
                    float a0 = side * Mathf.PI * 2f / sides;
                    float a1 = (side + 1) * Mathf.PI * 2f / sides;
                    Vector3 b0 = baseCenter +
                        new Vector3(Mathf.Cos(a0) * radius, 0f,
                            Mathf.Sin(a0) * radius);
                    Vector3 b1 = baseCenter +
                        new Vector3(Mathf.Cos(a1) * radius, 0f,
                            Mathf.Sin(a1) * radius);
                    Vector3 t0 = b0 + Vector3.up * height;
                    Vector3 t1 = b1 + Vector3.up * height;
                    AddQuad(b0, t0, t1, b1, color, color, color, color);
                    AddTriangle(
                        baseCenter + Vector3.up * height,
                        t0,
                        t1,
                        color,
                        color,
                        color);
                }
            }

            public void AddPrismBetween(
                Vector3 start,
                Vector3 end,
                float radius,
                Color color)
            {
                Vector3 direction = end - start;
                if (direction.sqrMagnitude <= 0.000001f)
                    return;
                Quaternion rotation = Quaternion.FromToRotation(
                    Vector3.right,
                    direction.normalized);
                AddBox(
                    (start + end) * 0.5f,
                    new Vector3(direction.magnitude, radius * 2f, radius * 2f),
                    rotation,
                    color);
            }

            public void AddDoubleSidedRibbon(
                Vector3 start,
                Vector3 end,
                float halfWidth,
                Color color)
            {
                Vector3 direction = (end - start).normalized;
                Vector3 side = Vector3.Cross(direction, Vector3.forward);
                if (side.sqrMagnitude < 0.01f)
                    side = Vector3.Cross(direction, Vector3.right);
                side = side.normalized * halfWidth;
                AddDoubleSidedQuad(
                    start - side,
                    end - side,
                    end + side,
                    start + side,
                    color);
            }

            public void AddDoubleSidedQuad(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Vector3 d,
                Color color)
            {
                AddQuad(a, b, c, d, color, color, color, color);
                AddQuad(d, c, b, a, color, color, color, color);
            }

            private void AddTriangle(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Color ca,
                Color cb,
                Color cc)
            {
                AddTriangle(
                    a,
                    b,
                    c,
                    ca,
                    cb,
                    cc,
                    Vector2.zero,
                    Vector2.zero,
                    Vector2.zero);
            }

            private void AddTriangle(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Color ca,
                Color cb,
                Color cc,
                Vector2 uva,
                Vector2 uvb,
                Vector2 uvc)
            {
                int start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                colors.Add(ca);
                colors.Add(cb);
                colors.Add(cc);
                uvs.Add(uva);
                uvs.Add(uvb);
                uvs.Add(uvc);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }

            private void AddQuad(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Vector3 d,
                Color ca,
                Color cb,
                Color cc,
                Color cd)
            {
                AddTriangle(a, b, c, ca, cb, cc);
                AddTriangle(a, c, d, ca, cc, cd);
            }

            public Mesh ToMesh(string name)
            {
                Mesh mesh = new Mesh
                {
                    name = name,
                    indexFormat = vertices.Count > ushort.MaxValue
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
                };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0, true);
                mesh.SetColors(colors);
                mesh.SetUVs(0, uvs);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                return mesh;
            }
        }
    }
}
