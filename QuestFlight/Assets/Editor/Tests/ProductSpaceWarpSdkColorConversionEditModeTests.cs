using System.IO;
using NUnit.Framework;
using QuestFlight.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class ProductSpaceWarpSdkColorConversionEditModeTests
    {
        [Test]
        public void ActualSdkPokeColorGetsOwnedMotionCloneWithoutModifyingSourceAndReusesIt()
        {
            InitializePinnedRenderPipeline();
            const string sourcePath = "Packages/com.meta.xr.sdk.interaction/Runtime/Materials/Unlit Material.mat";
            const string folder = "Assets/Production/Rendering/Materials";
            var sdkSource = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            Assert.That(sdkSource, Is.Not.Null, "Pinned actual SDK material required.");
            Assert.That(sdkSource.shader.name, Is.EqualTo("Unlit/Color"));
            Assert.That(sdkSource.FindPass("XRMotionVectors"), Is.LessThan(0),
                "The original material reproduces the unsupported motion policy.");
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(sourcePath);
            Assert.That(package, Is.Not.Null);
            string diskSource = Path.Combine(package.resolvedPath, "Runtime/Materials/Unlit Material.mat");
            byte[] sourceBytes = File.ReadAllBytes(diskSource);
            byte[] sourceMeta = File.ReadAllBytes(diskSource + ".meta");
            Color originalColor = sdkSource.GetColor("_Color");
            int originalQueue = sdkSource.renderQueue;
            bool originalDirty = EditorUtility.IsDirty(sdkSource);
            // Use a new source identity on every run. The identical SDK shader
            // and values exercise Resolve without touching its production clone.
            Assert.That(AssetDatabase.IsValidFolder(folder), Is.True);
            string ownedSourcePath = folder + "/__SdkColorFixture_" + System.Guid.NewGuid().ToString("N") + ".mat";
            Assert.That(File.Exists(ownedSourcePath) || File.Exists(ownedSourcePath + ".meta"), Is.False);
            Material source = null;
            GameObject root = null;
            string sourceGuid = null, clonePath = null, cloneGuid = null;
            long sourceLocalId = 0;
            bool conversionStarted = false;
            try
            {
                source = new Material(sdkSource) { name = "Owned identical SDK color fixture" };
                AssetDatabase.CreateAsset(source, ownedSourcePath);
                source = AssetDatabase.LoadAssetAtPath<Material>(ownedSourcePath);
                Assert.That(source, Is.Not.Null);
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out sourceGuid, out sourceLocalId), Is.True);
                AssertAssetIdentity(source, ownedSourcePath, sourceGuid);
                Assert.That(source.shader.name, Is.EqualTo(sdkSource.shader.name));
                Assert.That(source.GetColor("_Color"), Is.EqualTo(originalColor));
                Assert.That(source.renderQueue, Is.EqualTo(originalQueue));
                clonePath = folder + "/" + sourceGuid + "_" + sourceLocalId + ".mat";
                Assert.That(AssetDatabase.LoadAssetAtPath<Material>(clonePath), Is.Null);
                Assert.That(File.Exists(clonePath) || File.Exists(clonePath + ".meta"), Is.False,
                    "Preexisting work must be preserved even if not yet imported.");
                root = new GameObject("Owned SDK color conversion fixture");
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = source;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                conversionStarted = true;
                ProductSpaceWarpAudit.ConvertSceneMaterials(root);
                var clone = renderer.sharedMaterial;
                cloneGuid = AssetDatabase.AssetPathToGUID(clonePath);
                Assert.That(cloneGuid, Is.Not.EqualTo(sourceGuid));
                AssertAssetIdentity(clone, clonePath, cloneGuid);
                Assert.That(clone.shader.name, Is.EqualTo("Universal Render Pipeline/Unlit"));
                Assert.That(clone.GetColor("_BaseColor"), Is.EqualTo(originalColor));
                Assert.That(clone.renderQueue, Is.EqualTo(originalQueue));
                Assert.That(clone.GetFloat("_XRMotionVectorsPass"), Is.EqualTo(1));
                Assert.That(clone.FindPass("XRMotionVectors"), Is.GreaterThanOrEqualTo(0));
                Assert.That(clone.GetShaderPassEnabled("XRMotionVectors"), Is.True);
                Assert.That(renderer.motionVectorGenerationMode, Is.EqualTo(MotionVectorGenerationMode.Object));
                string[] materialInventory = AssetDatabase.FindAssets("t:Material", new[] { folder });
                ProductSpaceWarpAudit.ConvertSceneMaterials(root);
                AssertAssetIdentity(renderer.sharedMaterial, clonePath, cloneGuid);
                // Resolving the original SDK reference again must reuse the same
                // deterministic local asset, rather than duplicate/overwrite it.
                renderer.sharedMaterial = source;
                ProductSpaceWarpAudit.ConvertSceneMaterials(root);
                AssertAssetIdentity(renderer.sharedMaterial, clonePath, cloneGuid);
                Assert.That(AssetDatabase.AssetPathToGUID(clonePath), Is.EqualTo(cloneGuid));
                CollectionAssert.AreEquivalent(materialInventory, AssetDatabase.FindAssets("t:Material", new[] { folder }));
                Assert.That(clone.GetColor("_BaseColor"), Is.EqualTo(originalColor));
                Assert.That(clone.renderQueue, Is.EqualTo(originalQueue));
                Assert.That(sdkSource.shader.name, Is.EqualTo("Unlit/Color"));
                Assert.That(sdkSource.GetColor("_Color"), Is.EqualTo(originalColor));
                Assert.That(sdkSource.renderQueue, Is.EqualTo(originalQueue));
                Assert.That(EditorUtility.IsDirty(sdkSource), Is.EqualTo(originalDirty));
                CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(diskSource));
                CollectionAssert.AreEqual(sourceMeta, File.ReadAllBytes(diskSource + ".meta"));
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                if (conversionStarted && AssetDatabase.LoadAssetAtPath<Material>(clonePath) != null)
                {
                    AssertAssetIdentity(AssetDatabase.LoadAssetAtPath<Material>(ownedSourcePath), ownedSourcePath, sourceGuid);
                    Assert.That(clonePath, Is.EqualTo(folder + "/" + sourceGuid + "_" + sourceLocalId + ".mat"));
                    // If conversion throws after creation, the unique derived
                    // path was proven absent immediately before this call.
                    string observedGuid = AssetDatabase.AssetPathToGUID(clonePath);
                    Assert.That(observedGuid, Is.Not.Empty);
                    if (cloneGuid != null) Assert.That(observedGuid, Is.EqualTo(cloneGuid));
                    AssertAssetIdentity(AssetDatabase.LoadAssetAtPath<Material>(clonePath), clonePath, observedGuid);
                    Assert.That(AssetDatabase.DeleteAsset(clonePath), Is.True, "Delete only this fixture-created derived clone.");
                }
                if (source != null && AssetDatabase.LoadAssetAtPath<Material>(ownedSourcePath) != null)
                {
                    Assert.That(sourceGuid, Is.Not.Null.And.Not.Empty);
                    AssertAssetIdentity(AssetDatabase.LoadAssetAtPath<Material>(ownedSourcePath), ownedSourcePath, sourceGuid);
                    Assert.That(AssetDatabase.DeleteAsset(ownedSourcePath), Is.True, "Delete only this fixture-created source copy.");
                }
                CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(diskSource));
                CollectionAssert.AreEqual(sourceMeta, File.ReadAllBytes(diskSource + ".meta"));
            }
        }

        static void AssertAssetIdentity(Material material, string path, string expectedGuid)
        {
            Assert.That(material, Is.Not.Null);
            Assert.That(expectedGuid, Is.Not.Null.And.Not.Empty);
            Assert.That(EditorUtility.IsPersistent(material), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(material), Is.EqualTo(path));
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long localId), Is.True);
            Assert.That(guid, Is.EqualTo(expectedGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(expectedGuid));
            Assert.That(localId, Is.EqualTo(2100000), "Standalone material main object identity.");
        }

        static void InitializePinnedRenderPipeline()
        {
            Assert.That(GraphicsSettings.currentRenderPipeline,
                Is.InstanceOf<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(),
                "Use the actual selected product URP configuration; do not replace project settings.");
            var ownedCameraObject = new GameObject("Disposable SDK color conversion pipeline camera");
            var camera = ownedCameraObject.AddComponent<Camera>();
            var target = new RenderTexture(8, 8, 24);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                Assert.That(camera.CompareTag("MainCamera"), Is.False);
                camera.enabled = false;
                camera.cullingMask = 0;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                target.Create();
                camera.targetTexture = target;
                QuestFlight.Rendering.ProductRenderCapture.Render(camera);
                Assert.That(RenderPipelineManager.currentPipeline,
                    Is.InstanceOf<UnityEngine.Rendering.Universal.UniversalRenderPipeline>(),
                    "The real render must instantiate URP before querying selected-subshader passes.");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(ownedCameraObject);
            }
        }
    }
}
