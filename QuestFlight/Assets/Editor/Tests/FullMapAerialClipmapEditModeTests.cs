using NUnit.Framework;
using QuestFlightLab.Environment;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class FullMapAerialClipmapEditModeTests
    {
        [Test]
        public void ProductPrefabOwnsOneSerializedAircraftCenteredClipmap()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab");
            Assert.That(prefab, Is.Not.Null);
            AircraftCenteredAerialClipmapController[] controllers =
                prefab.GetComponents<AircraftCenteredAerialClipmapController>();
            Assert.That(controllers, Has.Length.EqualTo(1));
            Assert.That(controllers[0].trackedRig, Is.SameAs(prefab.GetComponent<AircraftReferenceFrameRig>()));
            Assert.That(controllers[0].terrainMaterial, Is.Not.Null);
            Assert.That(controllers[0].buildingRoofMaterial, Is.SameAs(
                AssetDatabase.LoadAssetAtPath<Material>(FullMapBuildingClipmapAssetAuthoring.MaterialPath)));
            Assert.That(controllers[0].legacyFallbackMipController,
                Is.SameAs(prefab.GetComponent<GeospatialMipController>()));
            Assert.That(controllers[0].authoredContractVersion,
                Is.EqualTo(AircraftCenteredAerialClipmapController.ContractVersion));
        }

        [Test]
        public void RuntimeContractUsesBoundedThreeByThreeAstcPageTiers()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab");
            AircraftCenteredAerialClipmapController controller =
                prefab.GetComponent<AircraftCenteredAerialClipmapController>();
            AssertTier(controller.high, 2048, 36, 36, 672f);
            AssertTier(controller.medium, 1024, 24, 24, 1008f);
            Assert.That(controller.high.bundleRegionCells, Is.LessThanOrEqualTo(2),
                "Regional bundles must remain small enough to avoid a large transient Quest heap spike.");
            Assert.That(controller.medium.bundleRegionCells, Is.LessThanOrEqualTo(2));
        }

        [Test]
        public void TerrainShaderReplacesLegacyRecoverySamplersWithTwoArraysAndGlobalFallback()
        {
            string path = "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.shader";
            string source = System.IO.File.ReadAllText(path);
            string shared = System.IO.File.ReadAllText("Assets/Shaders/AerialClipmapSampling.cginc");
            StringAssert.Contains("AerialClipmapSampling.cginc", source);
            StringAssert.Contains("UNITY_DECLARE_TEX2DARRAY(_ClipHighAlbedo)", shared);
            StringAssert.Contains("UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(_ClipMediumAlbedo)", shared);
            StringAssert.Contains("sampler2D _ClipGlobalAlbedo", shared);
            StringAssert.DoesNotContain("sampler2D _RecoveryMidNWAlbedo", source);
            StringAssert.DoesNotContain("sampler2D _RecoveryMidNEAlbedo", source);
            StringAssert.DoesNotContain("sampler2D _RecoveryMidSWAlbedo", source);
            StringAssert.DoesNotContain("sampler2D _RecoveryMidSEAlbedo", source);
        }

        [Test]
        public void RuntimeResidencyBudgetCountsBothAtomicPageBuffers()
        {
            long high = Astc10MipBytes(2048) * 9L * 2L;
            long medium = Astc10MipBytes(1024) * 9L * 2L;
            long global = Astc10MipBytes(4096);
            long total = high + medium + global;
            Assert.That(total, Is.LessThanOrEqualTo(
                AircraftCenteredAerialClipmapController.MaximumActiveAerialTextureBytes));
            Assert.That(total, Is.GreaterThan(22L * 1024L * 1024L),
                "The test must include both active and staging arrays, not one logical grid.");
        }

        [Test]
        public void GroundMicrodetailFollowsTheAircraftRatherThanAirportRadius()
        {
            string source = System.IO.File.ReadAllText(
                "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.shader");
            StringAssert.DoesNotContain("detailWeight *=", source);
            StringAssert.Contains("_DetailFadeStartEnd.y, cameraDistance", source);
        }

        [Test]
        public void ArrayPagesUseWorldSpaceGradientsAcrossSliceBoundaries()
        {
            string source = System.IO.File.ReadAllText(
                "Assets/Shaders/AerialClipmapSampling.cginc");
            StringAssert.Contains("_ClipHighAlbedo.SampleGrad", source);
            StringAssert.Contains("_ClipMediumAlbedo.SampleGrad", source);
            StringAssert.Contains("sampler_ClipHighAlbedo, float3(pageUv, slice)", source,
                "The two arrays must share one sampler to stay within Quest fragment limits.");
            StringAssert.Contains("float2 pageUvDx = ddx(world)", source);
            StringAssert.DoesNotContain(
                "UNITY_SAMPLE_TEX2DARRAY(_ClipHighAlbedo, float3(pageUv, slice))", source);
        }

        [Test]
        public void TerrainAndBuildingRoofsShareOneAtomicImageryBinding()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab");
            AircraftCenteredAerialClipmapController controller =
                prefab.GetComponent<AircraftCenteredAerialClipmapController>();
            Material terrain = controller.terrainMaterial;
            Material roofs = controller.buildingRoofMaterial;
            Assert.That(terrain, Is.Not.Null);
            Assert.That(roofs, Is.Not.Null);
            foreach (string propertyName in new[] { "_NearAlbedo", "_MidAlbedo", "_FarAlbedo" })
                Assert.That(roofs.GetTexture(propertyName), Is.SameAs(terrain.GetTexture(propertyName)), propertyName);
            foreach (string propertyName in new[]
                     {
                         "_NearWorldMinInvSize", "_MidWorldMinInvSize", "_FarWorldMinInvSize",
                         "_NearBlendStartEnd", "_MidBlendStartEnd"
                     })
                Assert.That(roofs.GetVector(propertyName), Is.EqualTo(terrain.GetVector(propertyName)), propertyName);

            string runtime = System.IO.File.ReadAllText(
                "Assets/Scripts/Environment/AircraftCenteredAerialClipmapController.cs");
            StringAssert.Contains("SetConsumerTexture", runtime);
            StringAssert.Contains("SetConsumerVector", runtime);
            StringAssert.Contains("SetConsumerFloat", runtime);
            StringAssert.DoesNotContain("new Material", runtime);
        }

        [Test]
        public void StreamingRecoveryRetriesGlobalAndBoundsTeleportPrediction()
        {
            string source = System.IO.File.ReadAllText(
                "Assets/Scripts/Environment/AircraftCenteredAerialClipmapController.cs");
            StringAssert.Contains("while (enabled && !globalReady)", source);
            StringAssert.Contains("Vector3.ClampMagnitude", source);
            StringAssert.Contains("high.tileInnerMeters", source);
            StringAssert.Contains("File.Delete(tracePath)", source,
                "Each exact development launch must reset stale device evidence.");
        }

        private static long Astc10MipBytes(int size)
        {
            long bytes = 0;
            int width = size;
            int height = size;
            while (true)
            {
                bytes += ((width + 9L) / 10L) * ((height + 9L) / 10L) * 16L;
                if (width == 1 && height == 1) return bytes;
                width = System.Math.Max(1, width / 2);
                height = System.Math.Max(1, height / 2);
            }
        }

        private static void AssertTier(
            AircraftCenteredAerialClipmapController.TierConfiguration tier,
            int pixels,
            int columns,
            int rows,
            float meters)
        {
            Assert.That(tier.pagePixels, Is.EqualTo(pixels));
            Assert.That(tier.columns, Is.EqualTo(columns));
            Assert.That(tier.rows, Is.EqualTo(rows));
            Assert.That(tier.tileInnerMeters, Is.EqualTo(meters).Within(0.001f));
            Assert.That(tier.gutterPixels, Is.GreaterThan(0));
        }
    }
}
