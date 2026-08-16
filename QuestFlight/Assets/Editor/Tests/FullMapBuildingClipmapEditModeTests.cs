using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
            Assert.That(controller.authoredMaximumSelectedTriangles, Is.EqualTo(139588));

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
            Assert.That(Occurrences(database.text, "\"source_id\":"), Is.EqualTo(57407));
            Assert.That(Occurrences(database.text, "\"column\":"), Is.EqualTo(576));
            Assert.That(database.text, Does.Contain("\"contract_version\":\"full-map-building-clipmap-v1\""));
            Assert.That(database.text, Does.Contain("\"roof_id\":1"));
            Assert.That(database.text, Does.Contain("\"roof_id\":2"));

            string source = Read("Assets/Scripts/Environment/AircraftCenteredBuildingClipmapController.cs");
            Assert.That(source, Does.Not.Contain("new GameObject"));
            Assert.That(source, Does.Not.Contain("AddComponent<"));
            Assert.That(source, Does.Not.Contain("Instantiate("));
            Assert.That(source, Does.Not.Contain("FindObject"));
            Assert.That(source, Does.Contain("Application.streamingAssetsPath"));
            Assert.That(source, Does.Contain("CommitWindow(active, force"));
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
        public void SharedAtlasIsOpaqueCompressedAndMeterScaled()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                FullMapBuildingClipmapAssetAuthoring.MaterialPath);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo(FullMapBuildingClipmapAssetAuthoring.ShaderName));
            Assert.That(material.renderQueue, Is.LessThanOrEqualTo((int)RenderQueue.Geometry));
            Assert.That(material.enableInstancing, Is.True);
            Assert.That(material.GetTexture("_MainTex"), Is.Not.Null);
            Assert.That(material.GetTexture("_BumpMap"), Is.Not.Null);
            Assert.That(material.GetTexture("_MRAO"), Is.Not.Null);
            Assert.That(material.GetTexture("_NearAlbedo"), Is.Not.Null);
            Assert.That(material.GetTexture("_MidAlbedo"), Is.Not.Null);
            Assert.That(material.GetTexture("_FarAlbedo"), Is.Not.Null);
            foreach (string path in new[]
                     {
                         FullMapBuildingClipmapAssetAuthoring.AlbedoPath,
                         FullMapBuildingClipmapAssetAuthoring.NormalPath,
                         FullMapBuildingClipmapAssetAuthoring.MraoPath
                     })
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.mipmapEnabled, Is.True, path);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear), path);
                Assert.That(importer.GetPlatformTextureSettings("Android").format,
                    Is.EqualTo(TextureImporterFormat.ASTC_6x6), path);
            }
            string shader = Read("Assets/Shaders/FullMapBuildingClipmap.shader");
            Assert.That(shader, Does.Contain("i.detailUv"));
            Assert.That(shader, Does.Contain("WindowMask"));
            Assert.That(shader, Does.Contain("mirroredPeriod"));
            Assert.That(shader, Does.Contain("SampleProjectedRoofOrthophoto(i.worldPos.xz)"));
            Assert.That(shader, Does.Contain("AerialClipmapSampling.cginc"));
            Assert.That(shader, Does.Contain("UNITY_BRANCH if (roof > 0.5h)"));
            Assert.That(shader, Does.Contain("albedo = tex2D(_MainTex, atlasUv).rgb * i.color.rgb"),
                "Generated facade appearance must remain on the wall-only branch.");
            Assert.That(shader, Does.Not.Contain("Blend "));

            string authoring = Read("Assets/Editor/FullMapBuildingClipmapAssetAuthoring.cs");
            Assert.That(authoring, Does.Contain("RoofCell(building)"));
            Assert.That(authoring, Does.Contain("RoofDetailScale(building)"));
            Assert.That(authoring, Does.Contain("Vector3.Cross(c - a, b - a)"));
            Assert.That(authoring, Does.Contain("builder.indices.Add(start + 2); builder.indices.Add(start + 1)"));
        }

        [Test]
        public void AircraftCenteredWindowRetainsPagesAcrossCellBoundaries()
        {
            GameObject owner = new GameObject("BuildingClipmapWindowTest");
            try
            {
                AircraftCenteredBuildingClipmapController controller =
                    owner.AddComponent<AircraftCenteredBuildingClipmapController>();
                controller.columns = 24;
                controller.rows = 24;
                controller.pageSizeMeters = 1000f;
                controller.mapMinimumX = -12000f;
                controller.mapMinimumZ = -12000f;
                MethodInfo desiredMinimum = typeof(AircraftCenteredBuildingClipmapController)
                    .GetMethod("DesiredMinimum", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(desiredMinimum, Is.Not.Null);
                Vector2Int a = (Vector2Int)desiredMinimum.Invoke(controller, new object[] { new Vector3(10f, 0f, 10f) });
                Vector2Int x = (Vector2Int)desiredMinimum.Invoke(controller, new object[] { new Vector3(1010f, 0f, 10f) });
                Vector2Int diagonal = (Vector2Int)desiredMinimum.Invoke(controller, new object[] { new Vector3(1010f, 0f, 1010f) });
                Assert.That(Window(a).Intersect(Window(x)).Count(), Is.EqualTo(12),
                    "A one-axis page shift must retain 12 of 16 renderer owners.");
                Assert.That(Window(a).Intersect(Window(diagonal)).Count(), Is.EqualTo(9),
                    "A diagonal page shift must retain 9 of 16 renderer owners.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
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

            int maximum = 0;
            for (int currentRow = 0; currentRow < rows; currentRow++)
            for (int currentColumn = 0; currentColumn < columns; currentColumn++)
            {
                int minimumColumn = Mathf.Clamp(currentColumn - 2, 0, columns - 4);
                int minimumRow = Mathf.Clamp(currentRow - 2, 0, rows - 4);
                int selected = 0;
                for (int row = minimumRow; row < minimumRow + 4; row++)
                for (int column = minimumColumn; column < minimumColumn + 4; column++)
                    selected += stable[column, row];
                maximum = Mathf.Max(maximum, selected);
            }
            Assert.That(maximum, Is.EqualTo(139588));
        }

        private static System.Collections.Generic.HashSet<Vector2Int> Window(Vector2Int minimum)
        {
            var pages = new System.Collections.Generic.HashSet<Vector2Int>();
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++) pages.Add(minimum + new Vector2Int(column, row));
            return pages;
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
