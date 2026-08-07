#if UNITY_EDITOR
// Keep this fixture under Assets/Editor so Unity's implicit Editor test assembly discovers it.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.PhotorealAerialSurfaceV1.Reflection.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Tests.EditMode
{
    public class CockpitPresentationEditModeTests
    {
        private const string AircraftPrefabPath = "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        private const string CockpitMaterialFolder = "Assets/Production/Materials/Cockpit";
        private const string AuthoredReflectionMaterialFolder =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Cockpit/Materials";
        private const string IsolatedCompassMaterialFolder =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Cockpit/MagneticCompass";
        private const string CockpitRootName = "ImportedCockpitInterior";

        [Test]
        public void CockpitUsesBoundedExtractedMaterialsAndQuestSafeRendererSettings()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                Transform cockpit = FindDescendant(root.transform, CockpitRootName);
                Assert.That(cockpit, Is.Not.Null);
                Renderer[] renderers = cockpit.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.GreaterThanOrEqualTo(12));
                Assert.That(cockpit.GetComponentsInChildren<Light>(true), Is.Empty);
                QuestFlightLab.Aircraft.ProductionAircraftLightingController lighting =
                    root.GetComponentInChildren<QuestFlightLab.Aircraft.ProductionAircraftLightingController>(true);
                Assert.That(lighting, Is.Not.Null);
                SerializedObject lightingObject = new SerializedObject(lighting);
                Assert.That(lightingObject.FindProperty("stableCockpitShader").objectReferenceValue,
                    Is.Not.Null);
                Assert.That(lightingObject.FindProperty("cockpitOcclusionStrength").floatValue,
                    Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(lightingObject.FindProperty("directionalCabinShadowStrength").floatValue,
                    Is.EqualTo(QuestFlightLab.Aircraft.ProductionAircraftLightingController
                        .DefaultDirectionalCabinShadowStrength).Within(0.0001f));
                Assert.That(FindDescendant(root.transform, "ProductionCockpitBakedSunOcclusion"),
                    Is.Null, "Rejected free-floating cockpit overlay must not return");

                HashSet<Material> materials = new HashSet<Material>();
                int bakedAoMaterials = 0;
                int shadowCasters = 0;
                int shadowReceivers = 0;
                foreach (Renderer renderer in renderers)
                {
                    Assert.That(renderer.gameObject.layer, Is.Zero, renderer.name);
                    if (renderer.shadowCastingMode != ShadowCastingMode.Off) shadowCasters++;
                    if (renderer.receiveShadows) shadowReceivers++;
                    Assert.That(renderer.lightProbeUsage, Is.EqualTo(LightProbeUsage.Off), renderer.name);
                    ReflectionProbeUsage expectedReflectionUsage =
                        string.Equals(
                            renderer.name,
                            "ProductionMagneticCompassV2",
                            StringComparison.Ordinal)
                            ? ReflectionProbeUsage.Off
                            : ReflectionProbeUsage.BlendProbesAndSkybox;
                    Assert.That(renderer.reflectionProbeUsage,
                        Is.EqualTo(expectedReflectionUsage), renderer.name);
                    Assert.That(renderer.motionVectorGenerationMode, Is.EqualTo(MotionVectorGenerationMode.ForceNoMotion), renderer.name);
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null, renderer.name);
                        string path = AssetDatabase.GetAssetPath(material).Replace('\\', '/');
                        Assert.That(path.StartsWith(CockpitMaterialFolder + "/", StringComparison.Ordinal) ||
                                    path.StartsWith(AuthoredReflectionMaterialFolder + "/",
                                        StringComparison.Ordinal) ||
                                    path.StartsWith(IsolatedCompassMaterialFolder + "/",
                                        StringComparison.Ordinal),
                            Is.True, path);
                        if (materials.Add(material) && HasBakedOcclusion(material)) bakedAoMaterials++;
                        if (material.HasProperty("roughnessFactor"))
                            Assert.That(material.GetFloat("roughnessFactor"), Is.InRange(0.28f, 0.88f), material.name);
                    }
                }

                Assert.That(materials.Count, Is.InRange(10, 18));
                Assert.That(bakedAoMaterials, Is.GreaterThanOrEqualTo(3), "Existing baked AO maps must be preserved");
                Assert.That(shadowCasters, Is.Zero);
                Assert.That(shadowReceivers, Is.Zero);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void CockpitLocalShadowUsesOneAuthoredWorldLockedProxy()
        {
            CockpitLocalShadowAuthoring.ValidateIntegratedFoundation();
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(AircraftPrefabPath);
            Assert.That(root, Is.Not.Null);
            Transform shadowRoot = FindDescendant(
                root.transform, CockpitLocalShadowAuthoring.ShadowRootName);
            Assert.That(shadowRoot, Is.Not.Null);
            Assert.That(shadowRoot.GetComponentsInChildren<Light>(true), Is.Empty);

            Camera camera = shadowRoot.GetComponentsInChildren<Camera>(true).Single();
            MeshRenderer renderer =
                shadowRoot.GetComponentsInChildren<MeshRenderer>(true).Single();
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(camera.enabled, Is.False);
            Assert.That(camera.stereoTargetEye, Is.EqualTo(StereoTargetEyeMask.None));
            Assert.That(camera.orthographic, Is.True);
            Assert.That(camera.allowHDR, Is.False);
            Assert.That(camera.allowMSAA, Is.False);
            Assert.That(camera.useOcclusionCulling, Is.False);
            Assert.That(camera.cullingMask,
                Is.EqualTo(1 << CockpitLocalShadowAuthoring.ShadowLayer));
            Assert.That(renderer.enabled, Is.False);
            Assert.That(renderer.gameObject.layer,
                Is.EqualTo(CockpitLocalShadowAuthoring.ShadowLayer));
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.triangles.Length / 3,
                Is.InRange(1, CockpitLocalShadowAuthoring.MaximumProxyTriangles));
            Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(1));
            Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(renderer.receiveShadows, Is.False);

            QuestFlightLab.Aircraft.ProductionAircraftLightingController lighting =
                root.GetComponentInChildren<
                    QuestFlightLab.Aircraft.ProductionAircraftLightingController>(true);
            SerializedObject serialized = new SerializedObject(lighting);
            Assert.That(
                serialized.FindProperty("cockpitLocalShadowCamera").objectReferenceValue,
                Is.SameAs(camera));
            Assert.That(
                serialized.FindProperty("cockpitLocalShadowCasterProxy").objectReferenceValue,
                Is.SameAs(renderer));
            RenderTexture map = serialized.FindProperty(
                "cockpitLocalShadowMap").objectReferenceValue as RenderTexture;
            Assert.That(map, Is.Not.Null);
            Assert.That(map.width,
                Is.EqualTo(CockpitLocalShadowAuthoring.ShadowMapResolution));
            Assert.That(map.height,
                Is.EqualTo(CockpitLocalShadowAuthoring.ShadowMapResolution));
            Assert.That(map.graphicsFormat,
                Is.EqualTo(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16_SFloat));
            Assert.That(map.useMipMap, Is.True);
            Assert.That(map.autoGenerateMips, Is.False);
            Assert.That(map.filterMode, Is.EqualTo(FilterMode.Bilinear));
        }

        private static bool HasBakedOcclusion(Material material)
        {
            return (material.HasProperty("occlusionTexture") && material.GetTexture("occlusionTexture") != null) ||
                   (material.HasProperty("_OcclusionMap") && material.GetTexture("_OcclusionMap") != null);
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (string.Equals(root.name, name, StringComparison.Ordinal)) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
#endif
