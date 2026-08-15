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
            Assert.That(controller.lod0MaximumDistance, Is.EqualTo(650f));
            Assert.That(controller.lod1MaximumDistance, Is.EqualTo(1500f));
            Assert.That(controller.lodHysteresisMeters, Is.EqualTo(160f));

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
            Assert.That(shader, Does.Not.Contain("Blend "));

            string authoring = Read("Assets/Editor/FullMapBuildingClipmapAssetAuthoring.cs");
            Assert.That(authoring, Does.Contain("RoofCell(building)"));
            Assert.That(authoring, Does.Contain("RoofDetailScale(building)"));
            Assert.That(authoring, Does.Contain("Vector3.Cross(c - a, b - a)"));
            Assert.That(authoring, Does.Contain("builder.indices.Add(start + 2); builder.indices.Add(start + 1)"));
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
