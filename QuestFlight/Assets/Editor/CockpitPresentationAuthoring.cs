#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Offline-only cockpit presentation pass. The imported aircraft geometry stays
    /// unchanged; tuned material instances and renderer flags are serialized in the
    /// explicitly authored product prefab.
    /// </summary>
    public static class CockpitPresentationAuthoring
    {
        public const string AircraftPrefabPath = "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string CockpitMaterialFolder = "Assets/Production/Materials/Cockpit";
        public const string CockpitRootName = "ImportedCockpitInterior";

        [MenuItem("QuestFlight/Product/Author Cockpit Presentation")]
        public static void RunInteractive() => AuthorCockpit();

        public static void RunBatch()
        {
            try
            {
                AuthorCockpit();
                Debug.Log("[QuestFlight][CockpitPresentation] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void AuthorCockpit()
        {
            EnsureFolder(CockpitMaterialFolder);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                Transform cockpitRoot = FindDescendant(prefabRoot.transform, CockpitRootName);
                if (cockpitRoot == null)
                    throw new InvalidOperationException("Explicit cockpit root is missing: " + CockpitRootName);

                Renderer[] renderers = cockpitRoot.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => renderer.sharedMaterials.Length > 0)
                    .ToArray();
                if (renderers.Length < 12)
                    throw new InvalidOperationException("Cockpit renderer closure is unexpectedly small: " + renderers.Length);

                Dictionary<Material, Material> replacements = new Dictionary<Material, Material>();
                foreach (Renderer renderer in renderers)
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int index = 0; index < materials.Length; index++)
                    {
                        Material source = materials[index];
                        if (source == null)
                            throw new InvalidOperationException("Cockpit renderer has a missing material: " + renderer.name);
                        if (!replacements.TryGetValue(source, out Material tuned))
                        {
                            tuned = CreateOrUpdateMaterial(source);
                            replacements.Add(source, tuned);
                        }
                        materials[index] = tuned;
                    }
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Simple;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                }

                Validate(cockpitRoot, renderers);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, AircraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "[QuestFlight][CockpitPresentation] Serialized bounded baked-AO strength, exposure/roughness tuning, " +
                "static environment reflections, and shadow-free cockpit renderers. No runtime authoring was added.");
        }

        private static Material CreateOrUpdateMaterial(Material source)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source).Replace('\\', '/');
            if (sourcePath.StartsWith(CockpitMaterialFolder + "/", StringComparison.Ordinal))
            {
                source.name = Path.GetFileNameWithoutExtension(sourcePath);
                TuneMaterial(source, source.name);
                EditorUtility.SetDirty(source);
                return source;
            }

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long localId))
                throw new InvalidOperationException("Cannot identify cockpit material subasset: " + source.name);
            string fileName = "Cockpit_" + SanitizeFileName(source.name) + "_" + localId + ".mat";
            string destination = CockpitMaterialFolder + "/" + fileName;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(destination);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, destination);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            material.name = Path.GetFileNameWithoutExtension(destination);
            TuneMaterial(material, source.name);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void TuneMaterial(Material material, string sourceName)
        {
            string key = sourceName.ToLowerInvariant();
            float roughness = 0.72f;
            if (key.Contains("glass")) roughness = 0.30f;
            else if (key.Contains("seat") || key.Contains("belt")) roughness = 0.86f;
            else if (key.Contains("meter") || key.Contains("display") || key.Contains("txt")) roughness = 0.62f;
            else if (key.Contains("switch") || key.Contains("button") || key.Contains("paddle")) roughness = 0.56f;
            else if (key.Contains("steering")) roughness = 0.68f;
            else if (key.Contains("interior") || key.Contains("body") || key.Contains("base")) roughness = 0.78f;
            SetFloatIfPresent(material, "roughnessFactor", roughness);
            SetFloatIfPresent(material, "_Roughness", roughness);

            bool hasOcclusion = HasTexture(material, "occlusionTexture") || HasTexture(material, "_OcclusionMap");
            if (hasOcclusion)
            {
                SetFloatIfPresent(material, "occlusionTexture_strength", 0.78f);
                SetFloatIfPresent(material, "_OcclusionStrength", 0.78f);
            }

            if (!key.Contains("glass"))
            {
                LiftColorIfPresent(material, "baseColorFactor", 1.075f, 0.010f);
                LiftColorIfPresent(material, "_BaseColor", 1.075f, 0.010f);
                LiftColorIfPresent(material, "_Color", 1.075f, 0.010f);
            }
        }

        private static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static bool HasTexture(Material material, string property)
        {
            return material.HasProperty(property) && material.GetTexture(property) != null;
        }

        private static void LiftColorIfPresent(Material material, string property, float multiplier, float offset)
        {
            if (!material.HasProperty(property)) return;
            Color color = material.GetColor(property);
            color.r = Mathf.Clamp01(color.r * multiplier + offset);
            color.g = Mathf.Clamp01(color.g * multiplier + offset);
            color.b = Mathf.Clamp01(color.b * multiplier + offset);
            material.SetColor(property, color);
        }

        private static void Validate(Transform cockpitRoot, IReadOnlyCollection<Renderer> renderers)
        {
            if (cockpitRoot.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("Cockpit presentation may not add realtime lights");
            HashSet<Material> materials = new HashSet<Material>();
            foreach (Renderer renderer in renderers)
            {
                if (renderer.shadowCastingMode != ShadowCastingMode.Off || renderer.receiveShadows)
                    throw new InvalidOperationException("Realtime cockpit shadows remain enabled: " + renderer.name);
                if (renderer.reflectionProbeUsage != ReflectionProbeUsage.Simple)
                    throw new InvalidOperationException("Cockpit reflection mode is not stable/simple: " + renderer.name);
                foreach (Material material in renderer.sharedMaterials)
                {
                    string path = AssetDatabase.GetAssetPath(material).Replace('\\', '/');
                    if (!path.StartsWith(CockpitMaterialFolder + "/", StringComparison.Ordinal))
                        throw new InvalidOperationException("Cockpit material was not extracted: " + path);
                    materials.Add(material);
                }
            }
            if (materials.Count < 10 || materials.Count > 18)
                throw new InvalidOperationException("Cockpit material closure changed: " + materials.Count + " (expected 10-18)");
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

        private static string SanitizeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        }

        private static void EnsureFolder(string path)
        {
            string[] components = path.Split('/');
            string current = components[0];
            for (int index = 1; index < components.Length; index++)
            {
                string next = current + "/" + components[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, components[index]);
                current = next;
            }
        }
    }
}
#endif
