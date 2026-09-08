using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace QuestFlight.Editor
{
    // Applies reviewed offline attributes during regeneration; never a runtime bake.
    public static class TrafficSelfOcclusionAuthoring
    {
        private const string RecordsPath = "Assets/Editor/SceneryRemaster/TrafficSelfOcclusion.json";
        [Serializable] private sealed class Records { public Entry[] entries; }
        [Serializable] private sealed class Entry
        {
            public string name;
            public string geometry_sha256;
            public int[] alpha;
        }

        public static void ApplyIfPresent(Mesh mesh)
        {
            if (!File.Exists(RecordsPath)) return;
            Records records = JsonUtility.FromJson<Records>(File.ReadAllText(RecordsPath));
            Entry entry = records.entries.SingleOrDefault(item => item.name == mesh.name);
            if (entry == null) return;
            if (GeometryIdentity(mesh) != entry.geometry_sha256 || entry.alpha.Length != mesh.vertexCount)
                throw new InvalidOperationException("Traffic geometry changed; rebake self-occlusion: " + mesh.name);
            Color32[] colors = mesh.colors32;
            for (int i = 0; i < colors.Length; i++)
            {
                if (entry.alpha[i] < 216 || entry.alpha[i] > 255)
                    throw new InvalidOperationException("Invalid traffic AO range: " + mesh.name);
                colors[i].a = (byte)entry.alpha[i];
            }
            mesh.colors32 = colors;
        }

        public static string GeometryIdentity(Mesh mesh)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                foreach (Vector3 value in mesh.vertices) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); }
                foreach (Vector3 value in mesh.normals) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); }
                foreach (int value in mesh.triangles) writer.Write(value);
                foreach (Color32 value in mesh.colors32) { writer.Write(value.r); writer.Write(value.g); writer.Write(value.b); }
                writer.Flush();
                using (SHA256 sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
