#if UNITY_EDITOR
// Keep this fixture under Assets/Editor so Unity's implicit Editor test assembly discovers it.
using System;
using System.Collections.Generic;
using NUnit.Framework;
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
                Assert.That(lightingObject.FindProperty("cockpitOcclusionStrength").floatValue,
                    Is.EqualTo(1.0f).Within(0.0001f));

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
                    Assert.That(renderer.reflectionProbeUsage,
                        Is.EqualTo(ReflectionProbeUsage.BlendProbesAndSkybox), renderer.name);
                    Assert.That(renderer.motionVectorGenerationMode, Is.EqualTo(MotionVectorGenerationMode.ForceNoMotion), renderer.name);
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null, renderer.name);
                        string path = AssetDatabase.GetAssetPath(material).Replace('\\', '/');
                        Assert.That(path.StartsWith(CockpitMaterialFolder + "/", StringComparison.Ordinal) ||
                                    path.StartsWith(AuthoredReflectionMaterialFolder + "/",
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
