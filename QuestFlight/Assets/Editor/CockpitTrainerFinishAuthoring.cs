using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>Offline finish pass. No hierarchy, flight calibration or runtime rendering changes.</summary>
    public static class CockpitTrainerFinishAuthoring
    {
        private const string Root = "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Cockpit/";
        private const string Source = "Assets/Resources/QuestFlightLab/ImportedAssets/Cessna172KogThorns/cessna172.glb";

        public static void BuildAndVisualQa()
        {
            Build();
            QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunProductionVisualQa();
        }

        public static void Build()
        {
            var originals = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<Material>().ToArray();
            Material interior = FindProduct("Interior_Body");
            Material steering = FindProduct("Steering");
            Texture2D body = Read(originals.Single(m => m.name == "Interior_Body_MAT").GetTexture("baseColorTexture"));
            Color32[] pixels = body.GetPixels32();
            for (int y = 0; y < body.height; y++)
            for (int x = 0; x < body.width; x++)
            {
                // Audited panel island in the frozen source atlas. Preserve gauge
                // artwork, holes and trim outside the neutral painted substrate.
                float u = (x + .5f) / body.width, v = (y + .5f) / body.height;
                Color32 c = pixels[y * body.width + x];
                // Neutralize the source's muddy brown trim while preserving
                // its seams, grain and existing wear. Panel/radio islands stay
                // under the separate mask below.
                if (!(u < .35f && v > .20f && v < .37f) && c.r > c.b * 1.18f && c.r > c.g * 1.04f)
                {
                    float l = c.r * .3f + c.g * .59f + c.b * .11f;
                    pixels[y * body.width + x] = new Color32(
                        (byte)Mathf.Clamp(l * 1.10f, 0, 255),
                        (byte)Mathf.Clamp(l * 1.07f, 0, 255),
                        (byte)Mathf.Clamp(l * 1.01f, 0, 255), c.a);
                }
                int hi = Math.Max(c.r, Math.Max(c.g, c.b));
                int lo = Math.Min(c.r, Math.Min(c.g, c.b));
                if (u < .004f || u > .35f || v < .209f || v > .360f || hi - lo > 5 || lo < 29 || hi > 53) continue;
                // Separate black radio-stack island includes its visible upper
                // blanking plate; it is not part of the painted panel substrate.
                if (u > .179f && u < .243f && v < .324f) continue;
                // Fine matte paint detail, authored once rather than evaluated
                // in the eye shader. No painted directional sunlight/highlight.
                uint hash = unchecked((uint)(x * 374761393 + y * 668265263));
                hash = (hash ^ (hash >> 13)) * 1274126177u;
                int grain = (int)(hash % 5u) - 2;
                int value = 78 + (lo - 39) / 2 + grain;
                pixels[y * body.width + x] = new Color32((byte)value, (byte)value, (byte)(value - 3), c.a);
            }
            body.SetPixels32(pixels);
            interior.SetTexture("baseColorTexture", Save(body, "C172_TrainerInterior", 1024));
            interior.SetFloat("metallicFactor", 0f);
            interior.SetFloat("roughnessFactor", .9f);
            EditorUtility.SetDirty(interior);

            var grip = new Texture2D(128, 128, TextureFormat.RGBA32, false, false);
            var gp = new Color32[128 * 128];
            for (int i = 0; i < gp.Length; i++)
            {
                byte c = (byte)(35 + (i * 17 + i / 128 * 13) % 3);
                gp[i] = new Color32(c, c, c, 255);
            }
            grip.SetPixels32(gp);
            steering.SetTexture("baseColorTexture", Save(grip, "C172_TrainerYoke", 128));
            steering.SetFloat("roughnessFactor", .48f);
            steering.SetFloat("normalTexture_scale", .35f);
            EditorUtility.SetDirty(steering);
            foreach (string name in new[] { "Seats", "SeatBelt", "SeatGear", "Buttons" })
            {
                Material finish = FindProduct(name);
                finish.SetFloat("roughnessFactor", name == "SeatGear" ? .52f : .88f);
                finish.SetFloat("normalTexture_scale", name == "Seats" ? .65f : .35f);
                if (name != "SeatGear") finish.SetFloat("metallicFactor", 0f);
                if (name == "Buttons") finish.SetColor("baseColorFactor", new Color(.16f,.16f,.15f,1f));
                EditorUtility.SetDirty(finish);
            }
            // The pilot yoke still referenced the imported material while the
            // copilot yoke used the extracted one. Author both bindings.
            const string rigPath = "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
            GameObject rig = PrefabUtility.LoadPrefabContents(rigPath);
            try
            {
                foreach (Renderer renderer in rig.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int index = 0; index < materials.Length; index++)
                    {
                        if (materials[index] == null || materials[index].name != "Steering_MAT") continue;
                        materials[index] = steering;
                        changed = true;
                    }
                    if (changed) renderer.sharedMaterials = materials;
                }
                PrefabUtility.SaveAsPrefabAsset(rig, rigPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(rig); }

            // The frozen neutral dial paint is too light beside the authored
            // black faces. Preserve markings, colored arcs, UVs and calibration.
            Material instruments = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/ProductionCockpitInstrumentV2.mat");
            var meter = originals.Single(m => m.name == "Meter_01_MAT");
            Texture2D faces = Read(meter.GetTexture("baseColorTexture"));
            var fp = faces.GetPixels32();
            for (int i = 0; i < fp.Length; i++)
            {
                Color32 c = fp[i];
                int hi = Math.Max(c.r, Math.Max(c.g, c.b)), lo = Math.Min(c.r, Math.Min(c.g, c.b));
                if (hi - lo > 25) continue;
                float factor = Mathf.Lerp(.42f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(130f, 230f, hi)));
                fp[i] = new Color32((byte)(c.r * factor), (byte)(c.g * factor), (byte)(c.b * factor), c.a);
            }
            faces.SetPixels32(fp);
            instruments.SetTexture("_SourceTex", Save(faces, "C172_TrainerInstrumentFaces", 2048));
            EditorUtility.SetDirty(instruments);
            CockpitAvionicsV2PresentationAuthoring.BuildAssetsOnly();
            AssetDatabase.SaveAssets();
            CockpitDepthOwnershipEditorContract.ValidateProductPrefab();
            Debug.Log("Trainer finish authored; existing materials, passes and instrument calibration retained.");
        }

        private static Material FindProduct(string name) => AssetDatabase.FindAssets("t:Material", new[] { "Assets/Production/Materials/Cockpit" })
            .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).Single(m => m.name.Contains("Cockpit_" + name + "_MAT_"));

        private static Texture2D Read(Texture source)
        {
            if (source == null) throw new InvalidOperationException("Missing frozen cockpit texture");
            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
                result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                result.Apply();
                return result;
            }
            finally { GL.sRGBWrite = previousSrgb; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
        }

        private static Texture2D Save(Texture2D image, string name, int size)
        {
            string path = Root + "Textures/" + name + ".png";
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.maxTextureSize = size;
            var settings = importer.GetPlatformTextureSettings("Android");
            settings.overridden = true;
            settings.maxTextureSize = size;
            settings.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
