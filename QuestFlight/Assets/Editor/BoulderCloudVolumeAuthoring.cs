using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    // Offline derivatives only. The authored material owns both volumes; no runtime allocation.
    public static class BoulderCloudVolumeAuthoring
    {
        public const string Root = "Assets/Production/Environment/CloudsV1/";
        public const string VolumePath = Root + "Generated/KBDU_CloudDensityVolume.asset";
        public const string OccupancyPath = Root + "Generated/KBDU_CloudOccupancy.asset";
        public static void RunBatch()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void Build()
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!ImageConversion.LoadImage(source, File.ReadAllBytes(Root + "KBDU_CloudDensitySlices_RGBA.png")))
                    throw new InvalidDataException("Cloud density PNG did not decode.");
                if (source.width != 512 || source.height != 256) throw new InvalidDataException("Unexpected cloud atlas dimensions.");
                Color32[] volume = Unpack(source.GetPixels32());
                var density = Save(VolumePath, 64, 32, 64, volume, FilterMode.Bilinear);
                var occupancy = Save(OccupancyPath, 16, 8, 16, BuildOccupancy(volume), FilterMode.Point);
                var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "KBDU_BoulderCloudShell.mat");
                // Importing the second native asset can invalidate the first transient handle.
                material.SetTexture("_DensityVolume", AssetDatabase.LoadAssetAtPath<Texture3D>(VolumePath));
                material.SetTexture("_CloudOccupancy", AssetDatabase.LoadAssetAtPath<Texture3D>(OccupancyPath));
                // Preserve the shipping selection: diagnostic requests select the candidates.
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
        public static Color32[] Unpack(Color32[] atlas)
        {
            if (atlas.Length != 512 * 256) throw new ArgumentException("Unexpected atlas size.");
            var volume = new Color32[64 * 32 * 64];
            for (int z = 0; z < 64; z++) for (int y = 0; y < 32; y++) for (int x = 0; x < 64; x++)
                volume[x + 64 * (y + 32 * z)] = atlas[x + (z % 8) * 64 + 512 * (y + (z / 8) * 32)];
            return volume;
        }
        public static Color32[] BuildOccupancy(Color32[] volume)
        {
            if (volume.Length != 64 * 32 * 64) throw new ArgumentException("Unexpected volume size.");
            var result = new Color32[16 * 8 * 16];
            for (int z = 0; z < 16; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 16; x++)
            {
                // Include every voxel supporting interpolation anywhere in the cell, plus a
                // numerical halo. Max(R,G) bounds every convex authored archetype mixture.
                int maximum = 0;
                for (int iz = Mathf.Max(0, Mathf.FloorToInt(z * 63f / 16) - 1); iz <= Mathf.Min(63, Mathf.CeilToInt((z + 1) * 63f / 16) + 1); iz++)
                for (int iy = Mathf.Max(0, Mathf.FloorToInt(y * 31f / 8) - 1); iy <= Mathf.Min(31, Mathf.CeilToInt((y + 1) * 31f / 8) + 1); iy++)
                for (int ix = Mathf.Max(0, Mathf.FloorToInt(x * 63f / 16) - 1); ix <= Mathf.Min(63, Mathf.CeilToInt((x + 1) * 63f / 16) + 1); ix++)
                {
                    Color32 value = volume[ix + 64 * (iy + 32 * iz)];
                    maximum = Math.Max(maximum, Math.Max(value.r, value.g));
                }
                // 0.06 is strictly below the shader's 0.07 body threshold.
                byte occupied = maximum >= 15 ? (byte)255 : (byte)0;
                result[x + 16 * (y + 8 * z)] = new Color32(occupied, occupied, occupied, 255);
            }
            return result;
        }
        static Texture3D Save(string path, int x, int y, int z, Color32[] pixels, FilterMode filter)
        {
            var created = new Texture3D(x, y, z, TextureFormat.RGBA32, false)
            { name = Path.GetFileNameWithoutExtension(path), wrapMode = TextureWrapMode.Clamp, filterMode = filter, anisoLevel = 0 };
            created.SetPixels32(pixels); created.Apply(false, true);
            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(path);
            if (existing == null) { AssetDatabase.CreateAsset(created, path); return created; }
            EditorUtility.CopySerialized(created, existing);
            UnityEngine.Object.DestroyImmediate(created);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
