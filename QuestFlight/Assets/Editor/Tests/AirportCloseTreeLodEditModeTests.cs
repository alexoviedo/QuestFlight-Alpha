using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class AirportCloseTreeLodEditModeTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void FarImpostorRendersCrownAcrossAtlasFrame(int frame)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AirportCloseTreeLodAuthoring.Lod2AssetPath);
            Material source = AssetDatabase.LoadAssetAtPath<Material>(AirportCloseTreeLodAuthoring.CanopyMaterialPath);
            Assert.That(mesh, Is.Not.Null); Assert.That(source, Is.Not.Null);
            var material = new Material(source);
            material.SetFloat("_InstanceFade", 1); material.SetFloat("_ImpostorFrame", frame);
            var tree = new GameObject("Isolated authored impostor regression") { layer = 31 };
            tree.AddComponent<MeshFilter>().sharedMesh = mesh;
            tree.AddComponent<MeshRenderer>().sharedMaterial = material;
            var cameraObject = new GameObject("Impostor regression camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.magenta; camera.orthographic = true; camera.orthographicSize = 0.6f;
            camera.transform.position = new Vector3(0, 0.5f, -2);
            var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(128, 128, TextureFormat.RGB24, false);
            var previousTarget = RenderTexture.active; bool previousFog = RenderSettings.fog;
            try
            {
                RenderSettings.fog = false; camera.targetTexture = target; QuestFlight.Rendering.ProductRenderCapture.Render(camera);
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); pixels.Apply();
                string captureRoot = System.Environment.GetEnvironmentVariable("QFL_CANOPY_UV_TEST_CAPTURE_ROOT");
                if (!string.IsNullOrEmpty(captureRoot))
                {
                    Directory.CreateDirectory(captureRoot);
                    File.WriteAllBytes(Path.Combine(captureRoot, $"frame_{frame}.png"), pixels.EncodeToPNG());
                }
                int covered = 0, left = 128, right = -1;
                Color32[] data = pixels.GetPixels32();
                for (int i = 0; i < data.Length; i++)
                {
                    Color32 value = data[i];
                    if (value.r > 250 && value.b > 250 && value.g < 3) continue;
                    covered++; left = Mathf.Min(left, i % 128); right = Mathf.Max(right, i % 128);
                }
                Assert.That(covered, Is.GreaterThan(800), $"Frame {frame} collapsed or clipped: {covered} covered pixels");
                Assert.That(right - left, Is.GreaterThan(45), "Authored U endpoints must span the crown rather than the same atlas column.");
            }
            finally
            {
                RenderSettings.fog = previousFog; RenderTexture.active = previousTarget; camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(tree);
                Object.DestroyImmediate(material); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }

        private const string EnvironmentPath = ProductionEnvironmentRoot.PrefabAssetPath;
        private const string SourceManifestPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy/" +
            "AirportCloseTreeLods_SourceManifest.json";
        private const string PlacementManifestPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy/" +
            "ProductionAirportCloseTreeInstances.json";
        private const string FullMapManifestPath =
            "Assets/Production/Environment/FullMapVegetationV3/" +
            "KBDU_FullMapVegetationHlodV3.json";
        private const string FullMapDatabasePath =
            "Assets/Production/Environment/FullMapVegetationV3/" +
            "KBDU_FullMapVegetationHlodV3.bytes";
        private const string FullMapCanopyTexturePath =
            "Assets/Production/Environment/FullMapVegetationV3/" +
            "KBDU_FullMapCanopyVisual_RGBA.png";
        private const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";

        [Test]
        public void ProductUsesOneBoundedSourceDerivedCloseTreeRenderer()
        {
            GameObject environment =
                AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            Assert.That(environment, Is.Not.Null);
            AirportCloseTreeLodRenderer[] renderers =
                environment.GetComponentsInChildren<AirportCloseTreeLodRenderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(1));

            AirportCloseTreeLodRenderer renderer = renderers[0];
            AircraftCenteredBuildingClipmapController buildingClipmap =
                environment.GetComponentInChildren<AircraftCenteredBuildingClipmapController>(true);
            AirportCoreBenchmarkRoot benchmark =
                environment.GetComponentInChildren<AirportCoreBenchmarkRoot>(true);
            Assert.That(renderer.Instances, Has.Length.EqualTo(1047));
            Assert.That(renderer.FullMapPlacementDatabase, Is.Not.Null);
            Assert.That(renderer.BuildingClipmap, Is.Not.Null);
            Assert.That(renderer.BuildingClipmap, Is.SameAs(buildingClipmap));
            Assert.That(buildingClipmap.PageColumns, Is.EqualTo(24));
            Assert.That(buildingClipmap.PageRows, Is.EqualTo(24));
            Assert.That(buildingClipmap.PageSizeMeters, Is.EqualTo(1000f));
            Assert.That(buildingClipmap.MapMinimumX, Is.EqualTo(-12000f));
            Assert.That(buildingClipmap.MapMinimumZ, Is.EqualTo(-12000f));
            Assert.That(renderer.UsesSharedBuildingPageResidency, Is.True);
            Assert.That(renderer.CullEntryFadeEnabled, Is.True);
            Assert.That(renderer.MaximumDistanceMeters, Is.EqualTo(10000f));
            Assert.That(renderer.ForwardTreeCoverageMeters, Is.EqualTo(10000f));
            Assert.That(renderer.LateralTreeCoverageMeters, Is.EqualTo(7000f));
            Assert.That(renderer.RearTreeCoverageMeters, Is.EqualTo(3000f));
            Assert.That(renderer.FarCoveragePageLimit, Is.EqualTo(96));
            Assert.That(renderer.FarSamplesPerCoveragePage, Is.EqualTo(144));
            Assert.That(renderer.MaximumFarCoverageCandidates, Is.EqualTo(13824));
            Assert.That(renderer.MaximumFarDrawPopulation, Is.EqualTo(3792));
            Assert.That(renderer.NearSamplesPerCoveragePage, Is.EqualTo(600));
            Assert.That(renderer.FullDensityPageLimit, Is.EqualTo(9));
            Assert.That(renderer.StablePageStratifiedFarSelection, Is.True);
            Assert.That(renderer.UsesSingleDrawProceduralFarPopulation, Is.False);
            Assert.That(renderer.UsesProvenBatchedFarPopulation, Is.True);
            Assert.That(renderer.MaximumFarBatchCount, Is.EqualTo(4));
            Assert.That(renderer.BuildingPagesAreMandatoryTreeCoverage, Is.False,
                "Head-directed building pages must not control vegetation residency.");
            Assert.That(benchmark, Is.Not.Null);
            Assert.That(benchmark.vegetationHeroCluster, Is.Not.Null);
            Assert.That(benchmark.vegetationHeroCluster.gameObject.activeSelf, Is.False,
                "The obsolete card-heavy V4 hero cluster must not draw beside the close-tree tier.");
            Assert.That(renderer.Lod0Mesh, Is.Not.Null);
            Assert.That(renderer.Lod1Mesh, Is.Not.Null);
            Assert.That(renderer.Lod2Mesh, Is.Not.Null);
            Assert.That(renderer.ShrubMesh, Is.Not.Null);
            Assert.That(renderer.GroveMesh, Is.Not.Null);
            Assert.That(TriangleCount(renderer.Lod0Mesh), Is.EqualTo(344));
            Assert.That(TriangleCount(renderer.Lod1Mesh), Is.EqualTo(122));
            Assert.That(TriangleCount(renderer.Lod2Mesh), Is.EqualTo(2));
            Assert.That(TriangleCount(renderer.ShrubMesh), Is.EqualTo(24));
            Assert.That(TriangleCount(renderer.GroveMesh), Is.EqualTo(44));
            Assert.That(AerialSceneryRoot.MaximumVegetationRenderers, Is.EqualTo(7),
                "The accepted vegetation draw ceiling is frozen pending physical Quest evidence.");
            Assert.That(renderer.MaximumImplementationDrawCount,
                Is.LessThanOrEqualTo(AerialSceneryRoot.MaximumVegetationRenderers));
            Assert.That(renderer.AuthoredMaximumDrawCount,
                Is.EqualTo(renderer.MaximumImplementationDrawCount));
            Assert.That(renderer.AuthoredMaximumDrawCount, Is.EqualTo(7));
            Assert.That(renderer.AuthoredMaximumSelectedTriangleCount,
                Is.EqualTo(60768));
            Assert.That(renderer.ProjectedScreenSizeSelection, Is.True);
            Assert.That(renderer.PersistentHysteresis, Is.True);
            Assert.That(renderer.CenterEyeWorldUprightImpostor, Is.True);
            Assert.That(renderer.PerEyeFacingBehavior, Is.False);
            Assert.That(renderer.AlphaToCoverageTransition, Is.True);
            Assert.That(renderer.Lod0EnterScreenPixels,
                Is.GreaterThan(renderer.Lod0ExitScreenPixels));
            Assert.That(renderer.Lod1EnterScreenPixels,
                Is.GreaterThan(renderer.Lod1ExitScreenPixels));
            Assert.That(renderer.MinimumResidencySeconds,
                Is.GreaterThanOrEqualTo(2f));
            Assert.That(renderer.GetComponentsInChildren<LODGroup>(true), Is.Empty);
            Assert.That(renderer.GetComponentsInChildren<BillboardRenderer>(true), Is.Empty);
            Assert.That(renderer.transform.childCount, Is.EqualTo(0),
                "Close-tree instances must not become one GameObject per tree.");
        }

        [Test]
        public void FarCoverageEnvelopeProtectsPilotForwardAndLateralViews()
        {
            GameObject environment =
                AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            AirportCloseTreeLodRenderer renderer =
                environment.GetComponentInChildren<AirportCloseTreeLodRenderer>(true);
            Vector3 forward = Vector3.forward;

            Assert.That(renderer.IsInsideTreeCoverageForTest(
                new Vector3(0f, 0f, 9900f), forward), Is.True);
            Assert.That(renderer.IsInsideTreeCoverageForTest(
                new Vector3(0f, 0f, 10100f), forward), Is.False);
            Assert.That(renderer.IsInsideTreeCoverageForTest(
                new Vector3(6900f, 0f, 0f), forward), Is.True);
            Assert.That(renderer.IsInsideTreeCoverageForTest(
                new Vector3(7100f, 0f, 0f), forward), Is.False);
            Assert.That(renderer.IsInsideTreeCoverageForTest(
                new Vector3(0f, 0f, -2900f), forward), Is.True);
            Assert.That(renderer.IsInsideTreeCoverageForTest(
                new Vector3(0f, 0f, -3100f), forward), Is.False);
            Assert.That(renderer.IsInsideTreeCoverageForTest(
                new Vector3(4000f, 0f, 7000f), forward), Is.True);
        }

        [Test]
        public void CloseTreeAssetsRetainLicenseAndDeterministicPlacementEvidence()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(SourceManifestPath);
            TextAsset placements =
                AssetDatabase.LoadAssetAtPath<TextAsset>(PlacementManifestPath);
            Assert.That(source, Is.Not.Null);
            Assert.That(placements, Is.Not.Null);
            Assert.That(source.text, Does.Contain("\"asset\": \"Tree Small 02\""));
            Assert.That(source.text, Does.Contain("\"license\": \"CC0 1.0 Universal\""));
            Assert.That(source.text, Does.Contain("\"raw_source_committed\": false"));
            Assert.That(source.text,
                Does.Contain("\"schema\": \"questflight.airport-tree-volumetric-source.v3\""));
            Assert.That(source.text, Does.Contain("\"xr_center_eye_impostor\": true"));
            Assert.That(source.text, Does.Contain("\"azimuth_frames\": 8"));
            Assert.That(source.text, Does.Contain("\"source_leaf_triangles_copied\": 0"));
            Assert.That(source.text,
                Does.Contain("\"near_horizontal_source_cards_copied\": false"));
            Assert.That(placements.text,
                Does.Contain("\"schema\": \"questflight.airport-close-tree-instances.v1\""));
            Assert.That(placements.text, Does.Contain("\"instance_count\": 1047"));
            TextAsset fullMap = AssetDatabase.LoadAssetAtPath<TextAsset>(FullMapManifestPath);
            TextAsset fullMapDatabase =
                AssetDatabase.LoadAssetAtPath<TextAsset>(FullMapDatabasePath);
            Assert.That(fullMap, Is.Not.Null);
            Assert.That(fullMapDatabase, Is.Not.Null);
            Assert.That(FullMapVegetationManifestValidator.TryValidate(
                fullMap, fullMapDatabase, out string fullMapReport), Is.True, fullMapReport);
        }

        [Test]
        public void FullMapManifestValidatorRejectsLegacyGrovesAndDatabaseMismatch()
        {
            TextAsset acceptedManifest =
                AssetDatabase.LoadAssetAtPath<TextAsset>(FullMapManifestPath);
            TextAsset acceptedDatabase =
                AssetDatabase.LoadAssetAtPath<TextAsset>(FullMapDatabasePath);
            Assert.That(FullMapVegetationManifestValidator.TryValidate(
                acceptedManifest, acceptedDatabase, out string acceptedReport),
                Is.True, acceptedReport);

            TextAsset legacyManifest = new TextAsset(acceptedManifest.text.Replace(
                FullMapVegetationManifestValidator.AcceptedGrovePolicy,
                "bounded multi-lobe grove HLOD"));
            TextAsset wrongDatabase = new TextAsset("not the accepted database");
            try
            {
                Assert.That(FullMapVegetationManifestValidator.TryValidate(
                    legacyManifest, acceptedDatabase, out string legacyReport),
                    Is.False, legacyReport);
                Assert.That(legacyReport, Does.Contain("no-aggregate-grove policy"));
                Assert.That(FullMapVegetationManifestValidator.TryValidate(
                    acceptedManifest, wrongDatabase, out string databaseReport),
                    Is.False, databaseReport);
                Assert.That(databaseReport, Does.Contain("database"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(legacyManifest);
                UnityEngine.Object.DestroyImmediate(wrongDatabase);
            }
        }

        [Test]
        public void FullMapTreeDimensionsAreEvidenceDrivenAndNotCapDominated()
        {
            GameObject environment =
                AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            AirportCloseTreeLodRenderer renderer =
                environment.GetComponentInChildren<AirportCloseTreeLodRenderer>(true);
            byte[] bytes = renderer.FullMapPlacementDatabase.bytes;
            Dictionary<ushort, int> heightCounts = new Dictionary<ushort, int>();
            Dictionary<ushort, int> crownCounts = new Dictionary<ushort, int>();
            int legacyHeightCapCount = 0;
            int legacyCrownCapCount = 0;

            using MemoryStream stream = new MemoryStream(bytes, false);
            using BinaryReader reader = new BinaryReader(stream, Encoding.ASCII, true);
            Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(8)), Is.EqualTo("QFFMVG3\0"));
            Assert.That(reader.ReadInt32(), Is.EqualTo(3));
            reader.ReadBytes(4 * 4);
            int columns = reader.ReadInt32();
            int rows = reader.ReadInt32();
            int instanceCount = reader.ReadInt32();
            reader.ReadBytes(columns * rows * 8);
            for (int index = 0; index < instanceCount; index++)
            {
                reader.ReadUInt16();
                reader.ReadUInt16();
                reader.ReadInt32();
                ushort height = reader.ReadUInt16();
                ushort crown = reader.ReadUInt16();
                reader.ReadBytes(7);
                heightCounts[height] = heightCounts.TryGetValue(height, out int heights)
                    ? heights + 1
                    : 1;
                crownCounts[crown] = crownCounts.TryGetValue(crown, out int crowns)
                    ? crowns + 1
                    : 1;
                if (height == 2350)
                    legacyHeightCapCount++;
                if (crown == 1550)
                    legacyCrownCapCount++;
            }

            int maximumHeightMode = 0;
            foreach (int count in heightCounts.Values)
                maximumHeightMode = Mathf.Max(maximumHeightMode, count);
            int maximumCrownMode = 0;
            foreach (int count in crownCounts.Values)
                maximumCrownMode = Mathf.Max(maximumCrownMode, count);
            Assert.That(heightCounts.Count, Is.GreaterThanOrEqualTo(500));
            Assert.That(crownCounts.Count, Is.GreaterThanOrEqualTo(250));
            Assert.That(maximumHeightMode, Is.LessThanOrEqualTo(instanceCount * 0.05f));
            Assert.That(maximumCrownMode, Is.LessThanOrEqualTo(instanceCount * 0.10f));
            Assert.That(legacyHeightCapCount, Is.LessThanOrEqualTo(instanceCount * 0.01f));
            Assert.That(legacyCrownCapCount, Is.LessThanOrEqualTo(instanceCount * 0.01f));
        }

        [Test]
        public void FullMapPlacementDatabaseIsCompleteBoundedAndSequential()
        {
            GameObject environment =
                AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            AirportCloseTreeLodRenderer renderer =
                environment.GetComponentInChildren<AirportCloseTreeLodRenderer>(true);
            byte[] bytes = renderer.FullMapPlacementDatabase.bytes;

            using MemoryStream stream = new MemoryStream(bytes, false);
            using BinaryReader reader = new BinaryReader(stream, Encoding.ASCII, true);
            Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(8)), Is.EqualTo("QFFMVG3\0"));
            Assert.That(reader.ReadInt32(), Is.EqualTo(3));
            Assert.That(reader.ReadSingle(), Is.EqualTo(-12000f));
            Assert.That(reader.ReadSingle(), Is.EqualTo(-12000f));
            Assert.That(reader.ReadSingle(), Is.EqualTo(1000f));
            Assert.That(reader.ReadSingle(), Is.EqualTo(0.1f));
            int columns = reader.ReadInt32();
            int rows = reader.ReadInt32();
            int instanceCount = reader.ReadInt32();
            Assert.That(columns, Is.EqualTo(24));
            Assert.That(rows, Is.EqualTo(24));
            Assert.That(instanceCount, Is.EqualTo(868508));

            int expectedOffset = 0;
            for (int index = 0; index < columns * rows; index++)
            {
                int offset = reader.ReadInt32();
                int count = reader.ReadInt32();
                Assert.That(offset, Is.EqualTo(expectedOffset));
                Assert.That(count, Is.InRange(0, 1600));
                expectedOffset += count;
            }

            Assert.That(expectedOffset, Is.EqualTo(instanceCount));
            Assert.That(bytes.Length,
                Is.EqualTo(40 + columns * rows * 8 + instanceCount * 19));
        }

        [Test]
        public void FarPagePrefixesContainOnlyRealPlacementsAndRetainShrubs()
        {
            GameObject environment =
                AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            AirportCloseTreeLodRenderer renderer =
                environment.GetComponentInChildren<AirportCloseTreeLodRenderer>(true);
            byte[] bytes = renderer.FullMapPlacementDatabase.bytes;
            using MemoryStream stream = new MemoryStream(bytes, false);
            using BinaryReader reader = new BinaryReader(stream, Encoding.ASCII, true);
            reader.ReadBytes(8);
            reader.ReadInt32();
            reader.ReadBytes(4 * 4);
            int columns = reader.ReadInt32();
            int rows = reader.ReadInt32();
            reader.ReadInt32();
            int pageCount = columns * rows;
            int[] offsets = new int[pageCount];
            int[] counts = new int[pageCount];
            for (int page = 0; page < pageCount; page++)
            {
                offsets[page] = reader.ReadInt32();
                counts[page] = reader.ReadInt32();
            }

            long recordStart = reader.BaseStream.Position;
            int prefixRecords = 0;
            int prefixShrubs = 0;
            int aggregateGroves = 0;
            for (int page = 0; page < pageCount; page++)
            for (int local = 0; local < counts[page]; local++)
            {
                reader.BaseStream.Position = recordStart +
                    (offsets[page] + local) * 19L + 18L;
                byte kind = reader.ReadByte();
                if (kind == 3)
                    aggregateGroves++;
                if (local >= AirportCloseTreeLodRenderer.FarSamplesPerPage)
                    continue;
                prefixRecords++;
                if (kind == 2)
                    prefixShrubs++;
            }

            Assert.That(aggregateGroves, Is.Zero,
                "Aggregate grove blobs may not re-enter the runtime database.");
            Assert.That(prefixRecords, Is.GreaterThan(80000));
            Assert.That(prefixShrubs, Is.GreaterThan(3000),
                "The far prefix must retain a bounded real-shrub representation.");
        }

        [Test]
        public void FullMapCanopyReliefIsOneLinearCompressedTerrainSample()
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                FullMapCanopyTexturePath);
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(4096));
            Assert.That(texture.height, Is.EqualTo(4096));
            TextureImporter importer = AssetImporter.GetAtPath(
                FullMapCanopyTexturePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.streamingMipmaps, Is.True);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.GetPlatformTextureSettings("Android").format,
                Is.EqualTo(TextureImporterFormat.ASTC_8x8));

            Material terrain = AssetDatabase.LoadAssetAtPath<Material>(
                TerrainMaterialPath);
            Assert.That(terrain, Is.Not.Null);
            Assert.That(terrain.GetTexture("_FullMapCanopyVisual"),
                Is.SameAs(texture));
            Assert.That(terrain.GetVector("_FullMapCanopyWorldMinInvSize"),
                Is.EqualTo(new Vector4(
                    -12000f, -12000f, 1f / 24000f, 1f / 24000f)));
        }

        [Test]
        public void NearAndMiddleTreeLodsContainNoHorizontalFoliagePlate()
        {
            GameObject environment =
                AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            AirportCloseTreeLodRenderer renderer =
                environment.GetComponentInChildren<AirportCloseTreeLodRenderer>(true);

            AssertFoliageCardsStayVolumetric(renderer.Lod0Mesh, 250);
            AssertFoliageCardsStayVolumetric(renderer.Lod1Mesh, 90);
        }

        [Test]
        public void CloseTreesReuseCanopyMaterialAndStayInsideProductBudget()
        {
            GameObject environment =
                AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            ProductionEnvironmentRoot root =
                environment.GetComponent<ProductionEnvironmentRoot>();
            PhotorealAerialSurfaceRoot surface =
                environment.GetComponentInChildren<PhotorealAerialSurfaceRoot>(true);
            AirportCloseTreeLodRenderer closeTrees =
                environment.GetComponentInChildren<AirportCloseTreeLodRenderer>(true);
            Assert.That(root, Is.Not.Null);
            Assert.That(surface, Is.Not.Null);
            Assert.That(closeTrees, Is.Not.Null);
            Assert.That(surface.canopySurfaceRenderers, Is.Empty,
                "The obsolete opaque grove hulls must not remain in product dependency closure.");
            Assert.That(closeTrees.SharedCanopyMaterial.GetTexture("_HeroLeafAtlas"),
                Is.Not.Null);
            Assert.That(closeTrees.SharedCanopyMaterial.GetTexture("_TreeImpostorAtlas"),
                Is.Not.Null);

            ProductionEnvironmentBudget budget = root.CalculateBudget();
            Assert.That(budget.rendererCount, Is.LessThanOrEqualTo(75));
            Assert.That(budget.triangleCount, Is.LessThanOrEqualTo(650000));
            Assert.That(budget.materialCount, Is.LessThanOrEqualTo(29));
        }

        private static int TriangleCount(Mesh mesh)
        {
            return checked((int)(mesh.GetIndexCount(0) / 3u));
        }

        private static void AssertFoliageCardsStayVolumetric(
            Mesh mesh,
            int minimumFoliageTriangles)
        {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            Color32[] colors = mesh.colors32;
            int[] triangles = mesh.triangles;
            int foliageTriangleCount = 0;
            float maximumAbsVerticalNormal = 0f;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                bool foliage =
                    colors[a].r < 128 && colors[b].r < 128 && colors[c].r < 128 &&
                    uv[a].x >= 1.5f && uv[b].x >= 1.5f && uv[c].x >= 1.5f;
                if (!foliage)
                {
                    continue;
                }

                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a],
                    vertices[c] - vertices[a]).normalized;
                maximumAbsVerticalNormal =
                    Mathf.Max(maximumAbsVerticalNormal, Mathf.Abs(normal.y));
                foliageTriangleCount++;
            }

            Assert.That(foliageTriangleCount, Is.GreaterThanOrEqualTo(minimumFoliageTriangles));
            Assert.That(maximumAbsVerticalNormal, Is.LessThan(0.75f),
                "Near/middle foliage must not reintroduce a crown-scale horizontal plate.");
        }
    }
}
