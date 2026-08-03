using System;
using System.Collections.Generic;
using System.IO;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Builds one opaque, one-submesh avionics readout mesh over the six
    /// positively identified display apertures. The generated glyph atlas is a
    /// project-authored fixed 5x7 font; it has no runtime font, Canvas, TMP,
    /// render texture, camera, or per-instrument renderer dependency.
    /// </summary>
    public static class CockpitAvionicsV2PresentationAuthoring
    {
        public const string SourceMeshPath =
            CockpitAvionicsV2Authoring.ExistingReadoutMeshPath;
        public const string TemplateMaterialPath =
            CockpitAvionicsV2EditorContract.ExistingReadoutMaterialPath;
        public const string GlyphAtlasPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Textures/C172_V2GlyphAtlas.asset";
        public const string DynamicMaterialPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Materials/ProductionInstrumentDynamicReadout.mat";
        public const string CombinedMeshPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Meshes/C172_V2AvionicsGlyphs.asset";
        public const string LayoutPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/C172_AvionicsV2Layout.asset";
        public const string ManifestPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Textures/C172_V2GlyphAtlasManifest.json";

        public const int AtlasColumns = 16;
        public const int AtlasRows = 6;
        public const int CellPixels = 16;
        public const int FirstAsciiCode = 32;
        public const int GlyphCount = 95;
        public const int ExpectedSourceApertures = 6;
        public const int ExpectedGlyphSlots = 74;
        public const int EstimatedTextureBytes =
            AtlasColumns * CellPixels * AtlasRows * CellPixels * 4 * 4 / 3;

        private const float SurfaceOffsetMeters = 0.00018f;

        private readonly struct Aperture
        {
            public readonly Vector3 BottomLeft;
            public readonly Vector3 BottomRight;
            public readonly Vector3 TopLeft;
            public readonly Vector3 TopRight;
            public readonly Vector3 Normal;

            public Aperture(
                Vector3 bottomLeft,
                Vector3 bottomRight,
                Vector3 topLeft,
                Vector3 topRight,
                Vector3 normal)
            {
                BottomLeft = bottomLeft;
                BottomRight = bottomRight;
                TopLeft = topLeft;
                TopRight = topRight;
                Normal = normal;
            }

            public Vector3 Point(float u, float v)
            {
                return Vector3.Lerp(
                    Vector3.Lerp(BottomLeft, BottomRight, u),
                    Vector3.Lerp(TopLeft, TopRight, u),
                    v) + Normal * SurfaceOffsetMeters;
            }
        }

        [Serializable]
        private sealed class GlyphManifest
        {
            public string schema =
                "questflight.cockpit.avionics_glyph_atlas.v2";
            public int glyph_count = GlyphCount;
            public int first_ascii_code = FirstAsciiCode;
            public int atlas_columns = AtlasColumns;
            public int atlas_rows = AtlasRows;
            public int cell_pixels = CellPixels;
            public int represented_apertures = ExpectedSourceApertures;
            public string[] live_rows =
            {
                "GPS rows 0-2",
                "COM1 active/standby row 3",
                "COM2 active/standby row 4",
                "transponder row 6"
            };
            public string[] excluded_rows =
            {
                "DME row 5: no positively identified physical aperture",
                "audio row 7: no positively identified physical aperture"
            };
            public string font_source =
                "Project-authored fixed 5x7 bitmap definitions in " +
                "CockpitAvionicsV2PresentationAuthoring.cs";
            public string material_contract =
                "Opaque render queue, one material, one submesh, no sorting.";
            public int estimated_texture_bytes = EstimatedTextureBytes;
        }

        [MenuItem("QuestFlight/Cockpit/Build V2 Batched Avionics Presentation")]
        public static void BuildAssetsOnly()
        {
            Mesh source = AssetDatabase.LoadAssetAtPath<Mesh>(SourceMeshPath);
            Require(source != null, "The audited avionics aperture mesh is missing.");
            Require(
                source.vertexCount ==
                    CockpitAvionicsV2Authoring.ExpectedReadoutVertices &&
                source.triangles.Length / 3 ==
                    CockpitAvionicsV2Authoring.ExpectedReadoutTriangles &&
                source.subMeshCount == 1,
                "The audited six-aperture source mesh changed.");

            Texture2D atlas = UpsertAtlas();
            Material material = UpsertMaterial(atlas);
            Mesh mesh = BuildCombinedGlyphMesh(source, out var slots);
            ApplyAuthoredPoweredFallback(mesh, slots, atlas);
            Mesh meshAsset = UpsertMesh(mesh);
            CockpitAvionicsV2Layout layout =
                AssetDatabase.LoadAssetAtPath<CockpitAvionicsV2Layout>(
                    LayoutPath);
            if (layout == null)
            {
                layout = ScriptableObject.CreateInstance<
                    CockpitAvionicsV2Layout>();
                AssetDatabase.CreateAsset(layout, LayoutPath);
            }
            layout.ConfigureGenerated(
                meshAsset,
                material,
                atlas,
                slots,
                AtlasColumns,
                AtlasRows,
                FirstAsciiCode,
                GlyphCount,
                EstimatedTextureBytes,
                CockpitInstrumentV2EditorContract.ExpectedSourceGuid,
                CockpitInstrumentV2EditorContract.ExpectedSourceSha256);
            EditorUtility.SetDirty(layout);

            WriteManifest();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Require(layout.Validate(out string error), error);
            Debug.Log(
                "[QuestFlight][CockpitAvionicsV2] Built one-draw represented " +
                $"GPS/COM/transponder presentation: {slots.Length} glyph slots, " +
                $"{meshAsset.vertexCount} vertices, {EstimatedTextureBytes} bytes.");
        }

        private static Mesh BuildCombinedGlyphMesh(
            Mesh source,
            out CockpitAvionicsV2GlyphSlot[] slots)
        {
            Aperture[] apertures = ReadApertures(source);
            var vertices = new List<Vector3>(ExpectedGlyphSlots * 4);
            var normals = new List<Vector3>(ExpectedGlyphSlots * 4);
            var tangents = new List<Vector4>(ExpectedGlyphSlots * 4);
            var uv = new List<Vector2>(ExpectedGlyphSlots * 4);
            var indices = new List<int>(ExpectedGlyphSlots * 6);
            var glyphSlots =
                new List<CockpitAvionicsV2GlyphSlot>(ExpectedGlyphSlots);

            // Large GPS aperture: three deterministic 12-character rows.
            // Twenty 5x7 glyphs were technically valid but illegible at the
            // represented panel's physical size. These mappings preserve the
            // most useful source fields while remaining readable in XR.
            AddGrid(
                apertures[0], 12, 3, 0.045f, 0.10f,
                (row, column) =>
                {
                    int first = row * CockpitAvionicsV2Formatter.Columns;
                    if (row == 0)
                        return first + 4 + column; // KBDU BRG 090
                    if (row == 1)
                        return column < 9
                            ? first + column       // DST 000.0
                            : first + 14 + column - 9; // 090 track
                    return first + column;         // ALT  5430
                },
                vertices, normals, tangents, uv, indices, glyphSlots);

            // Four represented radio apertures: active/standby values only.
            AddGrid(
                apertures[1], 7, 1, 0.08f, 0.18f,
                (_, column) => 3 * CockpitAvionicsV2Formatter.Columns +
                               3 + column,
                vertices, normals, tangents, uv, indices, glyphSlots);
            AddGrid(
                apertures[3], 7, 1, 0.08f, 0.18f,
                (_, column) => 3 * CockpitAvionicsV2Formatter.Columns +
                               12 + column,
                vertices, normals, tangents, uv, indices, glyphSlots);
            AddGrid(
                apertures[4], 7, 1, 0.08f, 0.18f,
                (_, column) => 4 * CockpitAvionicsV2Formatter.Columns +
                               3 + column,
                vertices, normals, tangents, uv, indices, glyphSlots);
            AddGrid(
                apertures[5], 7, 1, 0.08f, 0.18f,
                (_, column) => 4 * CockpitAvionicsV2Formatter.Columns +
                               12 + column,
                vertices, normals, tangents, uv, indices, glyphSlots);

            // Wide transponder aperture: "1200 ALT" without invented data.
            AddGrid(
                apertures[2], 10, 1, 0.08f, 0.16f,
                (_, column) => 6 * CockpitAvionicsV2Formatter.Columns +
                               5 + column,
                vertices, normals, tangents, uv, indices, glyphSlots);

            Require(
                glyphSlots.Count == ExpectedGlyphSlots,
                $"Expected {ExpectedGlyphSlots} glyph slots; built " +
                $"{glyphSlots.Count}.");
            var mesh = new Mesh
            {
                name = "C172_V2AvionicsGlyphs",
                indexFormat = IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0, false);
            mesh.bounds = source.bounds;
            slots = glyphSlots.ToArray();
            return mesh;
        }

        private static void ApplyAuthoredPoweredFallback(
            Mesh mesh,
            CockpitAvionicsV2GlyphSlot[] slots,
            Texture2D atlas)
        {
            // Static Visual QA and the frame before the runtime authority's
            // first update must remain readable. Runtime still replaces these
            // UVs from the authoritative snapshot and blanks them when the
            // avionics bus is actually unpowered.
            var snapshot = new CockpitAvionicsV2Snapshot
            {
                isValid = true,
                availability =
                    CockpitAvionicsV2Availability.ElectricalPower |
                    CockpitAvionicsV2Availability.GpsNavigation |
                    CockpitAvionicsV2Availability.ComRadios |
                    CockpitAvionicsV2Availability.Transponder,
                avionicsBusPowered = true,
                gpsDestinationIdentAscii =
                    (uint)'K' |
                    ((uint)'B' << 8) |
                    ((uint)'D' << 16) |
                    ((uint)'U' << 24),
                gpsBearingDegrees = 90f,
                gpsDistanceNauticalMiles = 0f,
                gpsGroundTrackDegrees = 90f,
                gpsAltitudeMslFeet = 5430f,
                com1ActiveKHz = 122800,
                com1StandbyKHz = 122800,
                com2ActiveKHz = 122800,
                com2StandbyKHz = 122800,
                transponderCodeBcd = 0x1200,
                transponderMode =
                    CockpitAvionicsV2TransponderMode.Altitude
            };
            var formatter = new CockpitAvionicsV2Formatter();
            Require(formatter.Format(snapshot),
                "The authored avionics fallback did not format.");
            Vector2[] authoredUv = mesh.uv;
            float cellWidth = 1f / AtlasColumns;
            float cellHeight = 1f / AtlasRows;
            float insetU = 0.5f / atlas.width;
            float insetV = 0.5f / atlas.height;
            char[] characters = formatter.Characters;
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                CockpitAvionicsV2GlyphSlot slot = slots[slotIndex];
                int glyph = characters[slot.characterIndex] - FirstAsciiCode;
                if (glyph < 0 || glyph >= GlyphCount)
                    glyph = '?' - FirstAsciiCode;
                int column = glyph % AtlasColumns;
                int row = glyph / AtlasColumns;
                float u0 = column * cellWidth + insetU;
                float u1 = (column + 1) * cellWidth - insetU;
                float v0 = 1f - (row + 1) * cellHeight + insetV;
                float v1 = 1f - row * cellHeight - insetV;
                int first = slot.firstVertex;
                authoredUv[first] = new Vector2(u0, v0);
                authoredUv[first + 1] = new Vector2(u1, v0);
                authoredUv[first + 2] = new Vector2(u1, v1);
                authoredUv[first + 3] = new Vector2(u0, v1);
            }
            mesh.uv = authoredUv;
        }

        private static Aperture[] ReadApertures(Mesh source)
        {
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] uv = source.uv;
            Require(
                vertices.Length == ExpectedSourceApertures * 6 &&
                normals.Length == vertices.Length &&
                uv.Length == vertices.Length,
                "The readout aperture vertex streams changed.");
            var result = new Aperture[ExpectedSourceApertures];
            for (int aperture = 0; aperture < result.Length; aperture++)
            {
                int first = aperture * 6;
                float minU = float.PositiveInfinity;
                float maxU = float.NegativeInfinity;
                float minV = float.PositiveInfinity;
                float maxV = float.NegativeInfinity;
                Vector3 normal = Vector3.zero;
                for (int vertex = first; vertex < first + 6; vertex++)
                {
                    minU = Mathf.Min(minU, uv[vertex].x);
                    maxU = Mathf.Max(maxU, uv[vertex].x);
                    minV = Mathf.Min(minV, uv[vertex].y);
                    maxV = Mathf.Max(maxV, uv[vertex].y);
                    normal += normals[vertex];
                }
                normal.Normalize();
                Vector3 bottomLeft = Corner(
                    vertices, uv, first, minU, minV);
                Vector3 bottomRight = Corner(
                    vertices, uv, first, maxU, minV);
                Vector3 topLeft = Corner(
                    vertices, uv, first, minU, maxV);
                Vector3 topRight = Corner(
                    vertices, uv, first, maxU, maxV);
                result[aperture] = new Aperture(
                    bottomLeft, bottomRight, topLeft, topRight, normal);
            }
            return result;
        }

        private static Vector3 Corner(
            Vector3[] vertices,
            Vector2[] uv,
            int first,
            float targetU,
            float targetV)
        {
            int best = first;
            float bestDistance = float.PositiveInfinity;
            for (int vertex = first; vertex < first + 6; vertex++)
            {
                float distance =
                    Mathf.Abs(uv[vertex].x - targetU) +
                    Mathf.Abs(uv[vertex].y - targetV);
                if (distance >= bestDistance) continue;
                best = vertex;
                bestDistance = distance;
            }
            Require(bestDistance < 0.0001f, "Aperture corner identity changed.");
            return vertices[best];
        }

        private static void AddGrid(
            Aperture aperture,
            int columns,
            int rows,
            float marginU,
            float marginV,
            Func<int, int, int> characterIndex,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<int> indices,
            List<CockpitAvionicsV2GlyphSlot> slots)
        {
            Vector3 right = (aperture.BottomRight -
                             aperture.BottomLeft).normalized;
            Vector4 tangent = new Vector4(
                right.x, right.y, right.z, 1f);
            float usableU = 1f - marginU * 2f;
            float usableV = 1f - marginV * 2f;
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                float u0 = marginU + usableU * column / columns;
                float u1 = marginU + usableU * (column + 1) / columns;
                float v1 = 1f - marginV - usableV * row / rows;
                float v0 = 1f - marginV - usableV * (row + 1) / rows;
                int first = vertices.Count;
                vertices.Add(aperture.Point(u0, v0));
                vertices.Add(aperture.Point(u1, v0));
                vertices.Add(aperture.Point(u1, v1));
                vertices.Add(aperture.Point(u0, v1));
                for (int vertex = 0; vertex < 4; vertex++)
                {
                    normals.Add(aperture.Normal);
                    tangents.Add(tangent);
                    uv.Add(Vector2.zero);
                }
                bool forward =
                    Vector3.Dot(
                        Vector3.Cross(
                            vertices[first + 1] - vertices[first],
                            vertices[first + 2] - vertices[first]),
                        aperture.Normal) >= 0f;
                indices.Add(first);
                indices.Add(forward ? first + 1 : first + 2);
                indices.Add(forward ? first + 2 : first + 1);
                indices.Add(first);
                indices.Add(forward ? first + 2 : first + 3);
                indices.Add(forward ? first + 3 : first + 2);
                slots.Add(new CockpitAvionicsV2GlyphSlot
                {
                    characterIndex = characterIndex(row, column),
                    firstVertex = first
                });
            }
        }

        private static Texture2D UpsertAtlas()
        {
            int width = AtlasColumns * CellPixels;
            int height = AtlasRows * CellPixels;
            var generated = new Texture2D(
                width, height, TextureFormat.RGBA32, true, false)
            {
                name = "C172_V2GlyphAtlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 1
            };
            Color32 black = new Color32(2, 5, 2, 255);
            Color32 green = new Color32(118, 255, 82, 255);
            Color32[] pixels = new Color32[width * height];
            for (int index = 0; index < pixels.Length; index++)
                pixels[index] = black;
            for (int glyph = 0; glyph < GlyphCount; glyph++)
            {
                char character = (char)(FirstAsciiCode + glyph);
                DrawGlyph(pixels, width, height, glyph, character, green);
            }
            generated.SetPixels32(pixels);
            generated.Apply(true, true);

            Texture2D asset =
                AssetDatabase.LoadAssetAtPath<Texture2D>(GlyphAtlasPath);
            if (asset == null)
            {
                AssetDatabase.CreateAsset(generated, GlyphAtlasPath);
                return generated;
            }
            EditorUtility.CopySerialized(generated, asset);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static Material UpsertMaterial(Texture2D atlas)
        {
            Material template = AssetDatabase.LoadAssetAtPath<Material>(
                TemplateMaterialPath);
            Require(template != null, "The opaque readout material is missing.");
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(DynamicMaterialPath);
            if (material == null)
            {
                material = new Material(template)
                {
                    name = "ProductionInstrumentDynamicReadout"
                };
                AssetDatabase.CreateAsset(material, DynamicMaterialPath);
            }
            else
            {
                material.shader = template.shader;
                material.CopyPropertiesFromMaterial(template);
            }
            material.mainTexture = atlas;
            material.renderQueue = 2004;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh UpsertMesh(Mesh generated)
        {
            Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>(CombinedMeshPath);
            if (asset == null)
            {
                AssetDatabase.CreateAsset(generated, CombinedMeshPath);
                return generated;
            }
            EditorUtility.CopySerialized(generated, asset);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void DrawGlyph(
            Color32[] pixels,
            int atlasWidth,
            int atlasHeight,
            int glyph,
            char character,
            Color32 color)
        {
            uint bits = GlyphBits(character);
            int cellX = glyph % AtlasColumns * CellPixels;
            int atlasRow = glyph / AtlasColumns;
            int cellY = atlasHeight - (atlasRow + 1) * CellPixels;
            const int scale = 2;
            const int offsetX = 3;
            const int offsetY = 1;
            for (int row = 0; row < 7; row++)
            for (int column = 0; column < 5; column++)
            {
                int bit = row * 5 + column;
                if ((bits & (1u << bit)) == 0) continue;
                int x0 = cellX + offsetX + column * scale;
                int y0 = cellY + offsetY + (6 - row) * scale;
                for (int y = 0; y < scale; y++)
                for (int x = 0; x < scale; x++)
                    pixels[(y0 + y) * atlasWidth + x0 + x] = color;
            }
        }

        // Row-major 5x7 glyphs. Unsupported punctuation intentionally renders
        // the same unambiguous question mark instead of silently inventing art.
        internal static uint GlyphBits(char c)
        {
            if (c >= 'a' && c <= 'z') c = (char)(c - 32);
            switch (c)
            {
                case ' ': return 0u;
                case '.': return Rows("00000","00000","00000","00000","00000","00110","00110");
                case '-': return Rows("00000","00000","00000","11111","00000","00000","00000");
                case '>': return Rows("10000","01000","00100","00010","00100","01000","10000");
                case '/': return Rows("00001","00010","00100","01000","10000","00000","00000");
                case '0': return Rows("01110","10001","10011","10101","11001","10001","01110");
                case '1': return Rows("00100","01100","00100","00100","00100","00100","01110");
                case '2': return Rows("01110","10001","00001","00010","00100","01000","11111");
                case '3': return Rows("11110","00001","00001","01110","00001","00001","11110");
                case '4': return Rows("00010","00110","01010","10010","11111","00010","00010");
                case '5': return Rows("11111","10000","10000","11110","00001","00001","11110");
                case '6': return Rows("01110","10000","10000","11110","10001","10001","01110");
                case '7': return Rows("11111","00001","00010","00100","01000","01000","01000");
                case '8': return Rows("01110","10001","10001","01110","10001","10001","01110");
                case '9': return Rows("01110","10001","10001","01111","00001","00001","01110");
                case 'A': return Rows("01110","10001","10001","11111","10001","10001","10001");
                case 'B': return Rows("11110","10001","10001","11110","10001","10001","11110");
                case 'C': return Rows("01111","10000","10000","10000","10000","10000","01111");
                case 'D': return Rows("11110","10001","10001","10001","10001","10001","11110");
                case 'E': return Rows("11111","10000","10000","11110","10000","10000","11111");
                case 'F': return Rows("11111","10000","10000","11110","10000","10000","10000");
                case 'G': return Rows("01111","10000","10000","10111","10001","10001","01111");
                case 'H': return Rows("10001","10001","10001","11111","10001","10001","10001");
                case 'I': return Rows("01110","00100","00100","00100","00100","00100","01110");
                case 'J': return Rows("00001","00001","00001","00001","10001","10001","01110");
                case 'K': return Rows("10001","10010","10100","11000","10100","10010","10001");
                case 'L': return Rows("10000","10000","10000","10000","10000","10000","11111");
                case 'M': return Rows("10001","11011","10101","10101","10001","10001","10001");
                case 'N': return Rows("10001","11001","10101","10011","10001","10001","10001");
                case 'O': return Rows("01110","10001","10001","10001","10001","10001","01110");
                case 'P': return Rows("11110","10001","10001","11110","10000","10000","10000");
                case 'Q': return Rows("01110","10001","10001","10001","10101","10010","01101");
                case 'R': return Rows("11110","10001","10001","11110","10100","10010","10001");
                case 'S': return Rows("01111","10000","10000","01110","00001","00001","11110");
                case 'T': return Rows("11111","00100","00100","00100","00100","00100","00100");
                case 'U': return Rows("10001","10001","10001","10001","10001","10001","01110");
                case 'V': return Rows("10001","10001","10001","10001","10001","01010","00100");
                case 'W': return Rows("10001","10001","10001","10101","10101","10101","01010");
                case 'X': return Rows("10001","10001","01010","00100","01010","10001","10001");
                case 'Y': return Rows("10001","10001","01010","00100","00100","00100","00100");
                case 'Z': return Rows("11111","00001","00010","00100","01000","10000","11111");
                default: return Rows("01110","10001","00010","00100","00100","00000","00100");
            }
        }

        private static uint Rows(params string[] rows)
        {
            uint bits = 0u;
            for (int row = 0; row < rows.Length; row++)
            for (int column = 0; column < rows[row].Length; column++)
                if (rows[row][column] == '1')
                    bits |= 1u << (row * 5 + column);
            return bits;
        }

        private static void WriteManifest()
        {
            string absolute = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", ManifestPath));
            File.WriteAllText(
                absolute,
                JsonUtility.ToJson(new GlyphManifest(), true) + "\n");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
