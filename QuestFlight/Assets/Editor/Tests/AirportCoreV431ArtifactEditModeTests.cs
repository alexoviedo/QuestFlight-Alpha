using System.IO;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class AirportCoreV431ArtifactEditModeTests
    {
        private const string RunwayShader =
            "Assets/Production/Environment/AirportCoreV4/Runway/AirportCoreContinuousRunway.shader";
        private const string MinorWaterInclude =
            "Assets/Production/Environment/AirportCoreV4/Water/Shaders/KBDUMinorWaterTerrainEffect.hlsl";
        private const string WaterManifest =
            "Assets/Production/Environment/ReconstructionV2/KBDU_WaterFeatureManifestV2.json";

        [Test]
        public void RunwayDepthBiasPreservesOneMeshMaterialAndUpwardTopology()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductionEnvironmentRoot.PrefabAssetPath);
            ProductionEnvironmentRoot contract = prefab.GetComponent<ProductionEnvironmentRoot>();
            Renderer pavement = contract.runwayPavement.GetComponent<Renderer>();
            Assert.That(pavement.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(contract.runwayCollisionSurface.sharedMesh, Is.SameAs(contract.runwayPavement.sharedMesh));
            Assert.That(pavement.lightmapIndex, Is.EqualTo(-1));
            Assert.That(contract.runwayPavement.sharedMesh.colors32, Is.Empty);

            Mesh mesh = contract.runwayPavement.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int offset = 0; offset < triangles.Length; offset += 3)
            {
                Vector3 cross = Vector3.Cross(vertices[triangles[offset + 1]] - vertices[triangles[offset]],
                    vertices[triangles[offset + 2]] - vertices[triangles[offset]]);
                Assert.That(Vector3.Dot(cross.normalized, Vector3.up), Is.GreaterThan(0.999f),
                    $"Triangle {offset / 3} is not consistently upward-facing.");
            }

            string shader = Read(RunwayShader);
            Assert.That(shader, Does.Contain("Offset -1, -1"));
            Assert.That(shader, Does.Not.Contain("LIGHTMAP_ON"));
            Assert.That(shader, Does.Not.Contain("_FeatureMask"));
        }

        [Test]
        public void MinorWaterProofUsesCommittedOsmManifestAndNoRuntimeDebugAsset()
        {
            string manifest = Read(WaterManifest);
            Assert.That(manifest, Does.Contain("osm:way/39486578/geometry:0"));
            Assert.That(manifest, Does.Contain("osm:way/37351163/geometry:0"));
            Assert.That(manifest, Does.Contain("osm:way/37350851/geometry:0"));
            Assert.That(manifest, Does.Contain("OpenStreetMap ODbL-1.0"));

            Shader debug = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/MinorWaterEvidence.shader");
            Assert.That(debug, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(debug), Does.StartWith("Assets/Editor/"));
            string include = Read(MinorWaterInclude);
            Assert.That(include, Does.Contain("channel * 0.62h"));
            Assert.That(include, Does.Contain("lerp(roughness, 0.32h, channel)"));
            Assert.That(include, Does.Not.Contain("GrabPass"));
        }

        [Test]
        public void MinorWaterStillAddsNoRendererMaterialOrCamera()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductionEnvironmentRoot.PrefabAssetPath);
            ProductionEnvironmentRoot contract = prefab.GetComponent<ProductionEnvironmentRoot>();
            AirportCoreBenchmarkRoot benchmark = prefab.GetComponentInChildren<AirportCoreBenchmarkRoot>(true);
            Assert.That(contract.otherWaterFeaturesSurfaceV2, Is.Null);
            Assert.That(contract.otherWaterFeaturesShoreBankV2, Is.Null);
            Assert.That(benchmark.minorWaterTerrainEffect, Is.Not.Null);
            Assert.That(benchmark.minorWaterTerrainEffect.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(benchmark.minorWaterTerrainEffect.sharedMaterials, Has.Length.EqualTo(1));
        }

        private static string Read(string path) => File.ReadAllText(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)));
    }
}
