using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Aircraft.CockpitDynamics.Analog;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Builds one self-contained magnetic-compass card from measured source
    /// aperture bounds. It deliberately owns no shared gauge atlas state.
    /// </summary>
    public static class CockpitMagneticCompassAuthoring
    {
        private const string Root =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/MagneticCompass";
        private const string MeshPath = Root + "/C172_MagneticCompassCard.asset";
        private const string TexturePath = Root + "/C172_MagneticCompassTape.png";
        private const string MaterialPath = Root + "/C172_MagneticCompass.mat";
        private const string ShaderPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Shaders/ProductionMagneticCompass.shader";
        private const string ChildName = "ProductionMagneticCompassV2";
        private const int Width = 2048;
        private const int Height = 128;

        [MenuItem("QuestFlight/Cockpit/Build Isolated Magnetic Compass")]
        public static void BuildBatch()
        {
            EnsureFolders();
            ProductionAnalogInstrumentLayout analog =
                AssetDatabase.LoadAssetAtPath<ProductionAnalogInstrumentLayout>(
                    CockpitAnalogInstrumentAuthoring.LayoutAssetPath);
            Require(analog != null && analog.Bindings.Length > 0,
                "The audited analog dial frame is missing.");

            Mesh source = AssetDatabase.LoadAllAssetsAtPath(
                    CockpitInstrumentV2EditorContract.SourceGlbPath)
                .OfType<Mesh>()
                .Single(mesh => mesh.name ==
                    CockpitInstrumentV2EditorContract.SourceMeshName);
            var vertices = ComponentVertices(
                source,
                CockpitInstrumentV2EditorContract.MagneticCompassCardSeed);
            vertices.UnionWith(ComponentVertices(
                source,
                CockpitInstrumentV2EditorContract
                    .MagneticCompassAuxiliaryCardSeed));

            AnalogInstrumentMeshBinding frame = analog.Bindings[0];
            Vector3 right = frame.dialRightLocal.normalized;
            Vector3 up = frame.dialUpLocal.normalized;
            Vector3 normal = frame.rotationAxisLocal.normalized;
            Mesh card = UpsertMesh(BuildCard(source, vertices, right, up, normal));
            Texture2D tape = BuildTape();
            Material material = UpsertMaterial(tape);
            Integrate(card, material);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[QuestFlight][MagneticCompass] Built one isolated opaque card " +
                $"from {vertices.Count} audited source vertices; no shared atlas changed.");
        }

        private static Mesh BuildCard(
            Mesh source,
            HashSet<int> selected,
            Vector3 right,
            Vector3 up,
            Vector3 normal)
        {
            Vector3[] positions = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector3 averageNormal = Vector3.zero;
            float minR = float.PositiveInfinity, maxR = float.NegativeInfinity;
            float minU = float.PositiveInfinity, maxU = float.NegativeInfinity;
            float minN = float.PositiveInfinity, maxN = float.NegativeInfinity;
            foreach (int index in selected)
            {
                Vector3 point = positions[index];
                minR = Mathf.Min(minR, Vector3.Dot(point, right));
                maxR = Mathf.Max(maxR, Vector3.Dot(point, right));
                minU = Mathf.Min(minU, Vector3.Dot(point, up));
                maxU = Mathf.Max(maxU, Vector3.Dot(point, up));
                minN = Mathf.Min(minN, Vector3.Dot(point, normal));
                maxN = Mathf.Max(maxN, Vector3.Dot(point, normal));
                if (sourceNormals.Length == positions.Length)
                    averageNormal += sourceNormals[index];
            }

            float halfWidth = (maxR - minR) * 0.48f;
            float halfHeight = (maxU - minU) * 0.44f;
            Require(halfWidth > 0.001f && halfHeight > 0.0005f,
                "Measured compass aperture bounds are invalid.");
            Vector3 facing = averageNormal.sqrMagnitude > 0.000001f
                ? averageNormal.normalized
                : normal;
            float faceDepth = Vector3.Dot(facing, normal) >= 0f
                ? maxN
                : minN;
            Vector3 center =
                right * ((minR + maxR) * 0.5f) +
                up * ((minU + maxU) * 0.5f) +
                normal * faceDepth + facing * 0.00012f;

            var mesh = new Mesh
            {
                name = "C172_MagneticCompassCard",
                indexFormat = IndexFormat.UInt16
            };
            mesh.vertices = new[]
            {
                center - right * halfWidth - up * halfHeight,
                center + right * halfWidth - up * halfHeight,
                center + right * halfWidth + up * halfHeight,
                center - right * halfWidth + up * halfHeight
            };
            mesh.normals = Enumerable.Repeat(facing, 4).ToArray();
            mesh.tangents = Enumerable.Repeat(
                new Vector4(right.x, right.y, right.z, 1f), 4).ToArray();
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            int[] triangles = { 0, 2, 1, 0, 3, 2 };
            if (Vector3.Dot(
                    Vector3.Cross(mesh.vertices[2] - mesh.vertices[0],
                        mesh.vertices[1] - mesh.vertices[0]), facing) < 0f)
                triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Texture2D BuildTape()
        {
            var texture = new Texture2D(
                Width, Height, TextureFormat.RGBA32, true, false);
            var pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                byte value = (byte)Mathf.RoundToInt(
                    Mathf.Lerp(21f, 7f, Mathf.Abs(y - Height * 0.52f) /
                        (Height * 0.52f)));
                for (int x = 0; x < Width; x++)
                    pixels[y * Width + x] = new Color32(
                        value, (byte)(value + 3), (byte)(value + 2), 255);
            }
            Color32 ink = new Color32(226, 234, 216, 255);
            string[] labels =
                { "N", "3", "6", "E", "12", "15", "S", "21", "24", "W", "30", "33" };
            for (int heading = 0; heading < 360; heading += 5)
            {
                int x = Mathf.RoundToInt(heading / 360f * Width) % Width;
                bool major = heading % 30 == 0;
                DrawWrappedLine(pixels, x, major ? 111 : 105, x, 91, ink, major ? 3 : 2);
                if (major)
                    DrawText(pixels, x, 55, labels[heading / 30], 5, ink);
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            byte[] png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            File.WriteAllBytes(Path.GetFullPath(TexturePath), png);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 50;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        private static void DrawText(
            Color32[] pixels, int centerX, int baselineY,
            string text, int scale, Color32 color)
        {
            int width = text.Length * 4 * scale - scale;
            int cursor = centerX - width / 2;
            foreach (char character in text)
            {
                string[] glyph = Glyph(character);
                for (int row = 0; row < 5; row++)
                for (int column = 0; column < 3; column++)
                {
                    if (glyph[row][column] != '#') continue;
                    for (int py = 0; py < scale; py++)
                    for (int px = 0; px < scale; px++)
                        SetWrapped(pixels, cursor + column * scale + px,
                            baselineY + (4 - row) * scale + py, color);
                }
                cursor += 4 * scale;
            }
        }

        private static string[] Glyph(char value)
        {
            switch (value)
            {
                case '0': return new[] { "###", "#.#", "#.#", "#.#", "###" };
                case '1': return new[] { ".#.", "##.", ".#.", ".#.", "###" };
                case '2': return new[] { "###", "..#", "###", "#..", "###" };
                case '3': return new[] { "###", "..#", ".##", "..#", "###" };
                case '4': return new[] { "#.#", "#.#", "###", "..#", "..#" };
                case '5': return new[] { "###", "#..", "###", "..#", "###" };
                case '6': return new[] { "###", "#..", "###", "#.#", "###" };
                case 'N': return new[] { "#.#", "###", "###", "###", "#.#" };
                case 'E': return new[] { "###", "#..", "##.", "#..", "###" };
                case 'S': return new[] { "###", "#..", "###", "..#", "###" };
                case 'W': return new[] { "#.#", "#.#", "###", "###", "#.#" };
                default: return new[] { "...", "...", "...", "...", "..." };
            }
        }

        private static void DrawWrappedLine(
            Color32[] pixels, int x0, int y0, int x1, int y1,
            Color32 color, int thickness)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            for (int step = 0; step <= steps; step++)
            {
                float t = steps == 0 ? 0f : step / (float)steps;
                int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                for (int ox = -thickness / 2; ox <= thickness / 2; ox++)
                    SetWrapped(pixels, x + ox, y, color);
            }
        }

        private static void SetWrapped(
            Color32[] pixels, int x, int y, Color32 color)
        {
            if (y < 0 || y >= Height) return;
            x = (x % Width + Width) % Width;
            pixels[y * Width + x] = color;
        }

        private static HashSet<int> ComponentVertices(Mesh mesh, int seedTriangle)
        {
            int[] triangles = mesh.triangles;
            int triangleCount = triangles.Length / 3;
            Require(seedTriangle >= 0 && seedTriangle < triangleCount,
                "Compass source seed is outside the source mesh.");
            var adjacent = new List<int>[mesh.vertexCount];
            for (int triangle = 0; triangle < triangleCount; triangle++)
            for (int corner = 0; corner < 3; corner++)
            {
                int vertex = triangles[triangle * 3 + corner];
                if (adjacent[vertex] == null) adjacent[vertex] = new List<int>();
                adjacent[vertex].Add(triangle);
            }
            var visited = new HashSet<int>();
            var vertices = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(seedTriangle);
            visited.Add(seedTriangle);
            while (queue.Count > 0)
            {
                int triangle = queue.Dequeue();
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = triangles[triangle * 3 + corner];
                    vertices.Add(vertex);
                    foreach (int neighbor in adjacent[vertex])
                        if (visited.Add(neighbor)) queue.Enqueue(neighbor);
                }
            }
            return vertices;
        }

        private static Mesh UpsertMesh(Mesh generated)
        {
            Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (asset == null)
            {
                AssetDatabase.CreateAsset(generated, MeshPath);
                return generated;
            }
            EditorUtility.CopySerialized(generated, asset);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static Material UpsertMaterial(Texture2D tape)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Require(shader != null, "The isolated compass shader is missing.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "C172_MagneticCompass" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_TapeTex", tape);
            material.SetFloat("_HeadingDegrees", 330f);
            material.SetFloat("_VisibleDegrees", 72f);
            material.renderQueue = 2004;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void Integrate(Mesh mesh, Material material)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                CockpitAnalogInstrumentAuthoring.AircraftPrefabPath);
            try
            {
                Transform meter = root.transform.Find(
                    CockpitAnalogInstrumentAuthoring.MeterRendererPath);
                Require(meter != null, "The authored Meter_01 transform is missing.");
                Transform existing = meter.Find(ChildName);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                var child = new GameObject(ChildName);
                child.transform.SetParent(meter, false);
                var filter = child.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.Object;
                EnsureInteriorVisibilityRenderer(root, renderer);
                var controller = child.AddComponent<CockpitMagneticCompassController>();
                var batch = root.GetComponentInChildren<CockpitInstrumentV2BatchController>(true);
                Require(batch != null && batch.AuthorityProvider != null,
                    "The authored V2 instrument authority is missing.");
                controller.ConfigureAuthoredReferences(
                    batch.AuthorityProvider, renderer, 330f);
                PrefabUtility.SaveAsPrefabAsset(
                    root, CockpitAnalogInstrumentAuthoring.AircraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsureFolders()
        {
            string current = "Assets";
            foreach (string part in Root.Substring("Assets/".Length).Split('/'))
            {
                string next = current + "/" + part;
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, part);
                current = next;
            }
        }

        private static void EnsureInteriorVisibilityRenderer(
            GameObject root,
            Renderer renderer)
        {
            AircraftVisibilityProfileController[] controllers =
                root.GetComponentsInChildren<AircraftVisibilityProfileController>(true);
            Require(controllers.Length == 1,
                "The authored aircraft must contain exactly one visibility profile.");
            var serialized = new SerializedObject(controllers[0]);
            SerializedProperty interiors = serialized.FindProperty("interiorRenderers");
            Require(interiors != null && interiors.isArray,
                "The visibility profile interior-renderer list is unavailable.");
            Renderer[] current = Enumerable.Range(0, interiors.arraySize)
                .Select(index => interiors.GetArrayElementAtIndex(index)
                    .objectReferenceValue as Renderer)
                .Where(item => item != null && item != renderer)
                .Append(renderer)
                .ToArray();
            interiors.arraySize = current.Length;
            for (int index = 0; index < current.Length; index++)
                interiors.GetArrayElementAtIndex(index).objectReferenceValue =
                    current[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controllers[0]);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
