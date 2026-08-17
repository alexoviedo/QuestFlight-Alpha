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
        private const string EnvironmentPath = ProductionEnvironmentRoot.PrefabAssetPath;
        private const string SourceManifestPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy/" +
            "AirportCloseTreeLods_SourceManifest.json";
        private const string PlacementManifestPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy/" +
            "ProductionAirportCloseTreeInstances.json";
        private const string FullMapManifestPath =
            "Assets/Production/Environment/FullMapVegetationV2/" +
            "KBDU_FullMapVegetationClipmapV2.json";

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
            AirportCoreBenchmarkRoot benchmark =
                environment.GetComponentInChildren<AirportCoreBenchmarkRoot>(true);
            Assert.That(renderer.Instances, Has.Length.EqualTo(1047));
            Assert.That(renderer.FullMapPlacementDatabase, Is.Not.Null);
            Assert.That(renderer.MaximumDistanceMeters, Is.EqualTo(1600f));
            Assert.That(benchmark, Is.Not.Null);
            Assert.That(benchmark.vegetationHeroCluster, Is.Not.Null);
            Assert.That(benchmark.vegetationHeroCluster.gameObject.activeSelf, Is.False,
                "The obsolete card-heavy V4 hero cluster must not draw beside the close-tree tier.");
            Assert.That(renderer.Lod0Mesh, Is.Not.Null);
            Assert.That(renderer.Lod1Mesh, Is.Not.Null);
            Assert.That(renderer.Lod2Mesh, Is.Not.Null);
            Assert.That(TriangleCount(renderer.Lod0Mesh), Is.EqualTo(344));
            Assert.That(TriangleCount(renderer.Lod1Mesh), Is.EqualTo(122));
            Assert.That(TriangleCount(renderer.Lod2Mesh), Is.EqualTo(2));
            Assert.That(renderer.AuthoredMaximumDrawCount, Is.EqualTo(3));
            Assert.That(renderer.AuthoredMaximumSelectedTriangleCount,
                Is.EqualTo(42504));
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
                Is.GreaterThanOrEqualTo(0.7f));
            Assert.That(renderer.GetComponentsInChildren<LODGroup>(true), Is.Empty);
            Assert.That(renderer.GetComponentsInChildren<BillboardRenderer>(true), Is.Empty);
            Assert.That(renderer.transform.childCount, Is.EqualTo(0),
                "Close-tree instances must not become one GameObject per tree.");
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
            Assert.That(fullMap, Is.Not.Null);
            Assert.That(fullMap.text,
                Does.Contain("\"schema\": \"questflight.full-map-vegetation-clipmap.v2\""));
            Assert.That(fullMap.text,
                Does.Contain("\"geometry\": \"overlapping individual crown/trunk LODs; no opaque grove hull\""));
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
            Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(8)), Is.EqualTo("QFFMVG2\0"));
            Assert.That(reader.ReadInt32(), Is.EqualTo(2));
            Assert.That(reader.ReadSingle(), Is.EqualTo(-12000f));
            Assert.That(reader.ReadSingle(), Is.EqualTo(-12000f));
            Assert.That(reader.ReadSingle(), Is.EqualTo(1000f));
            Assert.That(reader.ReadSingle(), Is.EqualTo(0.1f));
            int columns = reader.ReadInt32();
            int rows = reader.ReadInt32();
            int instanceCount = reader.ReadInt32();
            Assert.That(columns, Is.EqualTo(24));
            Assert.That(rows, Is.EqualTo(24));
            Assert.That(instanceCount, Is.EqualTo(901744));

            int expectedOffset = 0;
            for (int index = 0; index < columns * rows; index++)
            {
                int offset = reader.ReadInt32();
                int count = reader.ReadInt32();
                Assert.That(offset, Is.EqualTo(expectedOffset));
                Assert.That(count, Is.InRange(1, 1600));
                expectedOffset += count;
            }

            Assert.That(expectedOffset, Is.EqualTo(instanceCount));
            Assert.That(bytes.Length,
                Is.EqualTo(40 + columns * rows * 8 + instanceCount * 19));
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
