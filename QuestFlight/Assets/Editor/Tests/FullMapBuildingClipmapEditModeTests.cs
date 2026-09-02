using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor.Tests
{
    public sealed class FullMapBuildingClipmapEditModeTests
    {
        [Test]
        public void ProductOwnsOneFixedAircraftCenteredBuildingClipmap()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(
                FullMapBuildingClipmapAssetAuthoring.EnvironmentPath);
            AircraftCenteredBuildingClipmapController[] controllers =
                environment.GetComponentsInChildren<AircraftCenteredBuildingClipmapController>(true);
            Assert.That(controllers, Has.Length.EqualTo(1));
            AircraftCenteredBuildingClipmapController controller = controllers[0];
            Assert.That(controller.transform.name, Is.EqualTo(PhotorealAerialSurfaceRoot.BuildingLayerName));
            Assert.That(controller.pageFilters, Has.Length.EqualTo(AircraftCenteredBuildingClipmapController.SlotCount));
            Assert.That(controller.pageRenderers, Has.Length.EqualTo(AircraftCenteredBuildingClipmapController.SlotCount));
            Assert.That(controller.pageFilters.All(filter => filter != null && filter.sharedMesh == null), Is.True);
            Assert.That(controller.pageRenderers.All(renderer => renderer != null &&
                renderer.sharedMaterial == controller.sharedBuildingMaterial &&
                renderer.shadowCastingMode == ShadowCastingMode.Off), Is.True);
            Assert.That(controller.GetComponentsInChildren<LODGroup>(true), Is.Empty);
            Assert.That(controller.authoredContractVersion, Is.EqualTo(AircraftCenteredBuildingClipmapController.ContractVersion));
            Assert.That(controller.authoredMaximumSelectedTriangles, Is.EqualTo(233316));
            Assert.That(controller.pageTriangleCounts, Has.Length.EqualTo(576));
            Assert.That(controller.pageBuildingCounts, Has.Length.EqualTo(576));
            Assert.That(controller.pageRoofAreasSquareMeters, Has.Length.EqualTo(576));
            Assert.That(controller.pageBuildingCounts.Sum(), Is.EqualTo(57441));

            var scene = EditorSceneManager.OpenScene(
                FullMapBuildingClipmapAssetAuthoring.ScenePath, OpenSceneMode.Single);
            AircraftCenteredBuildingClipmapController sceneController = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AircraftCenteredBuildingClipmapController>(true))
                .Single();
            Assert.That(sceneController.TryValidateContract(out string report), Is.True, report);
        }

        [Test]
        public void CompactDatabaseCoversTheMapWithoutAirportHeroDuplicates()
        {
            TextAsset database = AssetDatabase.LoadAssetAtPath<TextAsset>(
                FullMapBuildingClipmapAssetAuthoring.DatabasePath);
            Assert.That(database, Is.Not.Null);
            Assert.That(Occurrences(database.text, "\"source_id\":"), Is.EqualTo(57441));
            Assert.That(Occurrences(database.text, "\"column\":"), Is.EqualTo(576));
            Assert.That(database.text, Does.Contain("\"contract_version\":\"full-map-building-clipmap-v1\""));
            Assert.That(database.text, Does.Contain("\"facade_contract_version\":\"boulder-semantic-facades-v2\""));
            Assert.That(Occurrences(database.text, "\"material_id\":"), Is.EqualTo(57441));
            Assert.That(Occurrences(database.text, "\"facade_style_id\":"), Is.EqualTo(57441));
            Assert.That(Occurrences(database.text, "\"material_confidence\":"), Is.EqualTo(57441));
            Assert.That(Occurrences(database.text, "\"semantic_class_id\":"), Is.EqualTo(57441));
            Assert.That(Occurrences(database.text, "\"semantic_region_id\":"), Is.EqualTo(57441));
            Assert.That(Occurrences(database.text, "\"material_rule_id\":"), Is.EqualTo(57441));
            Assert.That(database.text, Does.Contain("\"roof_id\":1"));
            Assert.That(database.text, Does.Contain("\"roof_id\":2"));

            string[] airportGapFillSourceIds =
            {
                "563214886", "563214887", "77499385", "77499386", "77499387", "77499388",
                "77499394", "77513656", "78054436", "78054447", "78054451", "78054452",
                "78054454", "78054456", "78054458", "78054459", "78054461", "78054465",
                "78054470", "78054473", "78060787", "916029512",
                "48257466", "48257474", "339395538", "78062108", "78062109", "78062110",
                "78062111", "78062112", "78062113", "78062114", "78062115", "78062116"
            };
            foreach (string sourceId in airportGapFillSourceIds)
                Assert.That(database.text, Does.Contain($"\"source_id\":\"OSM:way:{sourceId}:geometry:0\""), sourceId);
            Assert.That(database.text, Does.Not.Contain("\"source_id\":\"OSM:way:77434544:geometry:0\""),
                "The hand-authored Executive Terminal replacement must not receive a map-fidelity duplicate.");
            Assert.That(database.text, Does.Not.Contain("\"source_id\":\"OSM:way:78054463:geometry:0\""),
                "The hand-authored T-hangar row replacement must not receive a map-fidelity duplicate.");

            string source = Read("Assets/Scripts/Environment/AircraftCenteredBuildingClipmapController.cs");
            Assert.That(source, Does.Not.Contain("new GameObject"));
            Assert.That(source, Does.Not.Contain("AddComponent<"));
            Assert.That(source, Does.Not.Contain("Instantiate("));
            Assert.That(source, Does.Not.Contain("FindObject"));
            Assert.That(source, Does.Contain("Application.streamingAssetsPath"));
            Assert.That(source, Does.Contain("CommitWindow(target, force"));
            Assert.That(source, Does.Not.Contain("detailRenderer"));
            Assert.That(source, Does.Contain("UnloadAsync(true)"));
            Assert.That(source, Does.Not.Contain("Unload(false)"));
            Assert.That(source, Does.Not.Contain("bundle.Unload(loaded == null)"));

            string authoring = Read("Assets/Editor/FullMapBuildingClipmapAssetAuthoring.cs");
            Assert.That(authoring, Does.Contain("CompileStableFootprint"));
            Assert.That(authoring, Does.Contain("OrdinaryFootprintVertexLimit"));
            Assert.That(authoring, Does.Not.Contain("CoverageWallMinimumAreaSquareMeters"));
            Assert.That(authoring, Does.Contain("building.lod2_q"));
            Assert.That(authoring, Does.Not.Contain("building.area_m2 < 2000f"));
            Assert.That(authoring, Does.Not.Contain("building.area_m2 < 8000f"));
        }

        [Test]
        public void SharedFacadeArraysAreOpaqueCompressedAndMeterScaled()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                FullMapBuildingClipmapAssetAuthoring.MaterialPath);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo(FullMapBuildingClipmapAssetAuthoring.ShaderName));
            Assert.That(material.renderQueue, Is.LessThanOrEqualTo((int)RenderQueue.Geometry));
            Assert.That(material.enableInstancing, Is.True);
            Assert.That(material.GetTexture("_FacadeAlbedo"), Is.TypeOf<Texture2DArray>());
            Assert.That(material.GetTexture("_FacadeNormal"), Is.TypeOf<Texture2DArray>());
            Assert.That(material.GetTexture("_FacadeMRAO"), Is.TypeOf<Texture2DArray>());
            Assert.That(material.GetTexture("_FacadeDetail"), Is.TypeOf<Texture2D>());
            Assert.That(((Texture2DArray)material.GetTexture("_FacadeAlbedo")).depth, Is.EqualTo(16));
            Assert.That(material.GetTexture("_NearAlbedo"), Is.Not.Null);
            Assert.That(material.GetTexture("_MidAlbedo"), Is.Not.Null);
            Assert.That(material.GetTexture("_FarAlbedo"), Is.Not.Null);
            Assert.That(material.HasProperty("_BuildingShellMorphEnabled"), Is.False);
            foreach (string path in new[]
                     {
                         FullMapBuildingClipmapAssetAuthoring.AlbedoPath,
                         FullMapBuildingClipmapAssetAuthoring.NormalPath,
                         FullMapBuildingClipmapAssetAuthoring.MraoPath
                     })
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.textureShape, Is.EqualTo(TextureImporterShape.Texture2DArray), path);
                Assert.That(importer.mipmapEnabled, Is.True, path);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear), path);
                Assert.That(importer.GetPlatformTextureSettings("Android").format,
                    Is.EqualTo(TextureImporterFormat.ASTC_6x6), path);
            }
            TextureImporter detailImporter = AssetImporter.GetAtPath(
                FullMapBuildingClipmapAssetAuthoring.DetailPath) as TextureImporter;
            Assert.That(detailImporter, Is.Not.Null);
            Assert.That(detailImporter.textureShape, Is.EqualTo(TextureImporterShape.Texture2D));
            Assert.That(detailImporter.mipmapEnabled, Is.True);
            Assert.That(detailImporter.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(detailImporter.GetPlatformTextureSettings("Android").format,
                Is.EqualTo(TextureImporterFormat.ASTC_6x6));
            string shader = Read("Assets/Shaders/FullMapBuildingClipmap.shader");
            Assert.That(shader, Does.Contain("i.detailUv"));
            Assert.That(shader, Does.Contain("WindowMask"));
            Assert.That(shader, Does.Contain("MaterialTileMeters"));
            Assert.That(shader, Does.Contain("UNITY_SAMPLE_TEX2DARRAY(_FacadeAlbedo"));
            Assert.That(shader, Does.Contain("half hasCompleteBay"));
            Assert.That(shader, Does.Contain("half DoorMask"));
            Assert.That(shader, Does.Not.Contain("openingScreenFade"),
                "Accepted full-distance facade apertures must not be hidden by the retired screen-size fade.");
            Assert.That(shader, Does.Contain("DetailAtlasUv"));
            Assert.That(shader, Does.Contain("tex2D(_FacadeDetail"));
            Assert.That(shader, Does.Not.Contain("shellReveal"));
            Assert.That(shader, Does.Not.Contain("_BuildingLodReferencePosition"));
            Assert.That(shader, Does.Not.Contain("clip(window"),
                "Distant openings must fade continuously rather than pop or dither between eyes.");
            Assert.That(shader, Does.Contain("SampleProjectedRoofOrthophoto(i.worldPos.xz)"));
            Assert.That(shader, Does.Contain("AerialClipmapSampling.cginc"));
            Assert.That(shader, Does.Contain("UNITY_BRANCH if (roof > 0.5h)"));
            Assert.That(shader, Does.Not.Contain("tex2D(_MainTex"),
                "Wall rendering must not fall back to the retired semantic-incoherent atlas.");
            Assert.That(shader, Does.Not.Contain("Blend "));

            string authoring = Read("Assets/Editor/FullMapBuildingClipmapAssetAuthoring.cs");
            Assert.That(authoring, Does.Contain("RoofCell(building)"));
            Assert.That(authoring, Does.Contain("seed01, RoofDetailScale(building)"));
            Assert.That(authoring, Does.Contain("Vector3.Cross(c - a, b - a)"));
            Assert.That(authoring, Does.Contain("builder.indices.Add(start + 2); builder.indices.Add(start + 1)"));
        }

        [Test]
        public void UninitializedResidencyQueryFailsClosedWithoutThrowing()
        {
            GameObject owner = new GameObject("UninitializedBuildingResidencyTest");
            try
            {
                AircraftCenteredBuildingClipmapController controller =
                    owner.AddComponent<AircraftCenteredBuildingClipmapController>();
                bool[] committed = new bool[controller.PageColumns * controller.PageRows];
                bool[] prefetched = new bool[committed.Length];
                Assert.That(controller.TryCopyPageResidency(committed, prefetched), Is.False);
                Assert.That(committed, Is.All.False);
                Assert.That(prefetched, Is.All.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void CompiledShellsSealWallsToAuthoritativeTerrainAndExactRoofVertices()
        {
            TextAsset report = AssetDatabase.LoadAssetAtPath<TextAsset>(
                FullMapBuildingClipmapAssetAuthoring.CompilationReportPath);
            Assert.That(report, Is.Not.Null);
            Assert.That(report.text, Does.Contain("full-height-forward-residency-compiler-v11-20260817"));
            Assert.That(report.text, Does.Contain("\"foundation_embed_meters\": 1.6"));
            Assert.That(report.text, Does.Contain("\"maximum_roof_wall_seam_meters\": 0.0"));
            Assert.That(report.text, Does.Contain("\"maximum_vertical_corner_seam_meters\": 0.0"));

            string authoring = Read("Assets/Editor/FullMapBuildingClipmapAssetAuthoring.cs");
            Assert.That(authoring, Does.Contain("Vector3 c = roof[(i + 1) % polygon.Length]"),
                "A wall top endpoint must be the exact roof perimeter vertex, not a parallel approximation.");
            Assert.That(authoring, Does.Contain("Vector3 d = roof[i]"));
            Assert.That(authoring, Does.Contain("Mathf.Min(authoredBaseY, sample.heightMeters) - FoundationEmbedMeters"),
                "Each wall endpoint must extend beneath both the building datum and authoritative terrain.");
            Assert.That(authoring, Does.Contain("Wall/roof seam exceeds 1 mm"));
            Assert.That(authoring, Does.Contain("Open vertical wall corner found"));

            ProductionTerrainSurfaceAsset terrain = AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(
                FullMapBuildingClipmapAssetAuthoring.TerrainSurfacePath);
            Assert.That(terrain, Is.Not.Null);
            Assert.That(FullMapBuildingClipmapAssetAuthoring.FoundationEmbedMeters, Is.GreaterThan(1.5f),
                "Foundation skirt must exceed the documented maximum simplified-terrain error.");
        }

        [Test]
        public void SelectionProtectsTheFullHeightBaseAndPreloadsTheNextForwardFringe()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(
                FullMapBuildingClipmapAssetAuthoring.EnvironmentPath);
            AircraftCenteredBuildingClipmapController controller = environment
                .GetComponentInChildren<AircraftCenteredBuildingClipmapController>(true);
            Vector3 origin = new Vector3(10f, 100f, 10f);
            Vector2Int[] selected = controller.CalculateDesiredPagesForTest(
                origin, Vector3.forward, Vector3.forward);
            Vector2Int[] basePages = controller.CalculateBasePagesForTest(origin);
            Assert.That(selected, Has.Length.EqualTo(AircraftCenteredBuildingClipmapController.SlotCount));
            Assert.That(basePages, Has.Length.EqualTo(AircraftCenteredBuildingClipmapController.BaseSlotCount));
            Assert.That(basePages.All(selected.Contains), Is.True,
                "Every page in the accepted 4x4 full-height base must remain resident.");
            for (int column = 10; column <= 13; column++)
                Assert.That(selected, Does.Contain(new Vector2Int(column, 14)),
                    "The complete next forward row must be resident before the boundary crossing.");

            Vector2Int[] nextBase = controller.CalculateBasePagesForTest(new Vector3(10f, 100f, 1100f));
            Assert.That(nextBase.All(selected.Contains), Is.True,
                "A normal forward page crossing must introduce no previously absent base geometry.");

            var connected = new System.Collections.Generic.HashSet<Vector2Int> { basePages[0] };
            bool changed;
            do
            {
                changed = false;
                foreach (Vector2Int page in selected)
                {
                    if (connected.Contains(page)) continue;
                    if (connected.Any(other => Mathf.Abs(page.x - other.x) + Mathf.Abs(page.y - other.y) == 1))
                    { connected.Add(page); changed = true; }
                }
            } while (changed);
            Assert.That(connected.Count, Is.EqualTo(selected.Length),
                "Forward residency must be one connected landscape, never isolated popping islands.");

            int triangles = selected.Sum(page => controller.pageTriangleCounts[page.y * controller.columns + page.x]);
            Assert.That(triangles, Is.LessThanOrEqualTo(controller.authoredMaximumSelectedTriangles));
        }

        [Test]
        public void StableShellCompilerStaysInsideTheBuildingTriangleCeiling()
        {
            int columns = 24, rows = 24;
            int[,] stable = new int[columns, rows];
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                    $"{FullMapBuildingClipmapAssetAuthoring.GeneratedRoot}/KBDU_buildings_stable_{column:D3}_{row:D3}.asset");
                Assert.That(mesh, Is.Not.Null, $"stable {column},{row}");
                stable[column, row] = (int)mesh.GetIndexCount(0) / 3;
            }

            int maximum = stable.Cast<int>().OrderByDescending(value => value)
                .Take(AircraftCenteredBuildingClipmapController.SlotCount).Sum();
            Assert.That(maximum, Is.EqualTo(233298));
            Assert.That(maximum, Is.LessThanOrEqualTo(233316));
        }

        private static int Occurrences(string source, string value)
        {
            int count = 0, index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private static string Read(string assetPath) => File.ReadAllText(Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", assetPath.Replace('/', Path.DirectorySeparatorChar))));
    }
}
