using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Focused editor-only ownership contract for the generated center-stack
    /// surface and the existing batched analog-instrument depth selectors.
    /// </summary>
    public static class CockpitDepthOwnershipEditorContract
    {
        public const string SourceDisplayRendererPath =
            "AircraftVisualRoot/ImportedCockpitInterior/" +
            "Static_Display/_gltfNode_12/" +
            "Static_Display_New_Mat_01_0/" +
            "Static_Display_New_Mat_01_0_New_Mat_01_0";
        public const string SourceDisplayObjectName =
            "Static_Display_New_Mat_01_0_New_Mat_01_0";

        private const float GeometryTolerance = 0.0001f;
        private const float BoundsTolerance = 0.001f;
        private const float CoverageTolerance = 0.00001f;
        private const float LayerTolerance = 0.0001f;

        [MenuItem("QuestFlight/Cockpit/Validate Depth Ownership")]
        public static void ValidateProductPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath);
            try
            {
                if (!ValidatePrefabContents(root, out string error))
                    throw new InvalidOperationException(error);
                Debug.Log(
                    "[QuestFlight][CockpitDepthOwnership] PASS: one center-stack " +
                    "surface owner, complete aperture coverage, and explicit " +
                    "analog depth layers.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static MeshRenderer ResolveSourceDisplayRenderer(
            GameObject root)
        {
            Require(root != null, "Aircraft prefab contents are required.");
            Transform source = root.transform.Find(SourceDisplayRendererPath);
            Require(source != null,
                "The exact imported center-stack display path is missing.");
            Require(string.Equals(
                    source.name, SourceDisplayObjectName,
                    StringComparison.Ordinal),
                "The imported center-stack display object identity changed.");

            MeshFilter filter = source.GetComponent<MeshFilter>();
            MeshRenderer renderer = source.GetComponent<MeshRenderer>();
            Require(filter != null && renderer != null &&
                    filter.sharedMesh != null,
                "The imported center-stack display renderer is incomplete.");
            Mesh expected = AssetDatabase.LoadAssetAtPath<Mesh>(
                CockpitAvionicsV2PresentationAuthoring.SourceMeshPath);
            Require(expected != null,
                "The audited six-aperture reference mesh is missing.");
            Require(IsSixApertureTopology(expected),
                "The audited extracted center-stack reference is not the " +
                "expected 36-vertex, 12-triangle, six-aperture surface.");
            Require(string.Equals(
                    AssetDatabase.GetAssetPath(filter.sharedMesh),
                    CockpitAvionicsV2EditorContract.SourceGlbPath,
                    StringComparison.Ordinal),
                "The exact imported center-stack renderer no longer uses the " +
                "frozen C172 source mesh.");
            Bounds sourceApertureBounds = GetSourceApertureBounds(
                filter.sharedMesh, renderer.localToWorldMatrix,
                out int sourceApertureTriangles);
            Require(sourceApertureTriangles ==
                    CockpitAvionicsV2Authoring.ExpectedReadoutTriangles,
                $"The imported source must resolve exactly 12 display " +
                $"aperture triangles; found {sourceApertureTriangles}.");
            Transform visualRoot = root.transform.Find("AircraftVisualRoot");
            Require(visualRoot != null,
                "The authored aircraft visual root is missing.");
            Bounds expectedWorldBounds = TransformBounds(
                visualRoot.localToWorldMatrix, expected.bounds);
            Require(BoundsApproximately(
                    sourceApertureBounds, expectedWorldBounds),
                "The imported center-stack display bounds do not match the " +
                "audited six-aperture reference.");
            return renderer;
        }

        public static bool ValidatePrefabContents(
            GameObject root,
            out string error)
        {
            try
            {
                MeshRenderer sourceRenderer =
                    ResolveSourceDisplayRenderer(root);
                Require(!sourceRenderer.enabled,
                    "The obsolete imported center-stack display renderer is enabled.");
                Require(sourceRenderer.gameObject.activeSelf,
                    "The imported source display GameObject must remain present.");

                Transform hardware = FindUniqueTransform(
                    root, CockpitCompletionV3Authoring.AvionicsHardwareObjectName);
                MeshFilter hardwareFilter = hardware.GetComponent<MeshFilter>();
                MeshRenderer hardwareRenderer =
                    hardware.GetComponent<MeshRenderer>();
                Require(hardwareFilter != null && hardwareRenderer != null &&
                        hardwareRenderer.enabled &&
                        hardwareFilter.sharedMesh != null &&
                        string.Equals(
                            AssetDatabase.GetAssetPath(hardwareFilter.sharedMesh),
                            CockpitAvionicsV2PresentationAuthoring
                                .NeutralizedSourceMeshPath,
                            StringComparison.Ordinal),
                    "The non-screen static-display hardware is not preserved by " +
                    "the neutralized replacement mesh.");
                Require(sourceRenderer.sharedMaterials.SequenceEqual(
                            hardwareRenderer.sharedMaterials) &&
                        MatrixApproximately(
                            sourceRenderer.transform.localToWorldMatrix,
                            hardwareRenderer.transform.localToWorldMatrix),
                    "The neutralized static-display hardware does not exactly " +
                    "replace the disabled imported renderer.");
                Require(CountImportedApertureTriangles(
                            hardwareFilter.sharedMesh) == 0,
                    "The neutralized hardware mesh still contains an obsolete " +
                    "display-aperture triangle.");
                var screenRegions = CockpitAvionicsV2PresentationAuthoring.ReadImportedScreenRegions(
                    sourceRenderer.GetComponent<MeshFilter>().sharedMesh);
                var hardwareVertices = hardwareFilter.sharedMesh.vertices;
                var hardwareTriangles = hardwareFilter.sharedMesh.triangles;
                for (int i = 0; i < hardwareTriangles.Length; i += 3)
                    Require(!screenRegions.Exists(region =>
                        region.Contains(hardwareVertices[hardwareTriangles[i]]) &&
                        region.Contains(hardwareVertices[hardwareTriangles[i + 1]]) &&
                        region.Contains(hardwareVertices[hardwareTriangles[i + 2]])),
                        "Legacy screen/digit geometry remains inside a display aperture.");
                ValidateRendererAuthority(
                    root.GetComponentInChildren<
                        AircraftVisibilityProfileController>(true),
                    "interiorRenderers", sourceRenderer, hardwareRenderer,
                    "aircraft interior visibility");
                ValidateRendererAuthority(
                    root.GetComponentInChildren<ProductFlightModeMenuView>(true),
                    "aircraftPresentationRenderers", sourceRenderer,
                    hardwareRenderer, "startup menu suppression");

                Transform generated = FindUniqueTransform(
                    root, CockpitCompletionV3Authoring.AvionicsObjectName);
                MeshFilter generatedFilter = generated.GetComponent<MeshFilter>();
                MeshRenderer generatedRenderer =
                    generated.GetComponent<MeshRenderer>();
                Require(generatedFilter != null && generatedRenderer != null &&
                        generatedFilter.sharedMesh != null,
                    "The generated center-stack display renderer is incomplete.");
                Require(generatedRenderer.enabled && generated.gameObject.activeInHierarchy,
                    "The generated center-stack display renderer is disabled.");

                CockpitAvionicsV2Layout avionics =
                    AssetDatabase.LoadAssetAtPath<CockpitAvionicsV2Layout>(
                        CockpitAvionicsV2PresentationAuthoring.LayoutPath);
                Require(avionics != null, "The generated avionics layout is missing.");
                Require(avionics.Validate(out string layoutError), layoutError);
                Require(generatedFilter.sharedMesh == avionics.CombinedGlyphMesh,
                    "The product center-stack renderer does not use the generated mesh.");
                Require(
                    generatedRenderer.sharedMaterials.Length == 1 &&
                    generatedRenderer.sharedMaterial ==
                        avionics.ExistingReadoutMaterial,
                    "The product center-stack renderer does not use exactly one " +
                    "generated readout material.");

                Mesh importedSource = sourceRenderer
                    .GetComponent<MeshFilter>().sharedMesh;
                int enabledOwners = 0;
                MeshFilter[] filters =
                    root.GetComponentsInChildren<MeshFilter>(true);
                for (int index = 0; index < filters.Length; index++)
                {
                    MeshRenderer candidate =
                        filters[index].GetComponent<MeshRenderer>();
                    if (candidate == null || !candidate.enabled ||
                        !candidate.gameObject.activeInHierarchy)
                        continue;
                    if (filters[index].sharedMesh == avionics.CombinedGlyphMesh ||
                        filters[index].sharedMesh == importedSource)
                        enabledOwners++;
                }
                Require(enabledOwners == 1,
                    $"Expected one enabled center-stack aperture owner; found " +
                    $"{enabledOwners}.");

                ValidateGeneratedCoverage(avionics);
                ValidateGeneratedMaterial(avionics.ExistingReadoutMaterial);
                ValidateAnalogOwnership(root);
                ValidateNoRelevantRendererDuplicates(
                    root, importedSource, avionics.CombinedGlyphMesh);

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static void ValidateGeneratedCoverage(
            CockpitAvionicsV2Layout layout)
        {
            Mesh mesh = layout.CombinedGlyphMesh;
            Require(mesh != null && mesh.subMeshCount == 1 &&
                    mesh.vertexCount % 4 == 0 &&
                    mesh.triangles.Length == mesh.vertexCount / 4 * 6,
                "The generated center-stack mesh is not a complete quad batch.");

            var apertureUv = new List<Vector2>();
            var ownership = new List<Vector2>();
            mesh.GetUVs(1, apertureUv);
            mesh.GetUVs(2, ownership);
            Require(apertureUv.Count == mesh.vertexCount &&
                    ownership.Count == mesh.vertexCount,
                "The generated aperture ownership channels are incomplete.");

            var glyphStarts = new HashSet<int>(
                layout.GlyphSlots.Select(slot => slot.firstVertex));
            Require(glyphStarts.Count == layout.GlyphSlots.Length,
                "The generated glyph slot ranges are not unique.");
            var rectangles = new List<Rect>[
                CockpitAvionicsV2PresentationAuthoring
                    .ExpectedPresentationApertures];
            for (int aperture = 0; aperture < rectangles.Length; aperture++)
                rectangles[aperture] = new List<Rect>();

            Vector2[] atlasUv = mesh.uv;
            int[] triangles = mesh.triangles;
            int quadCount = mesh.vertexCount / 4;
            for (int quad = 0; quad < quadCount; quad++)
            {
                int first = quad * 4;
                Vector2 classification = ownership[first];
                int aperture = Mathf.RoundToInt(classification.x);
                bool glyph = classification.y > 0.5f;
                Require(aperture >= 0 && aperture < rectangles.Length &&
                        Mathf.Abs(classification.x - aperture) <= LayerTolerance,
                    $"Generated screen quad {quad} has a bad aperture owner.");
                for (int vertex = first + 1; vertex < first + 4; vertex++)
                {
                    Require(Vector2.Distance(
                            ownership[vertex], classification) <= LayerTolerance,
                        $"Generated screen quad {quad} mixes ownership classes.");
                }
                Require(glyphStarts.Contains(first) == glyph,
                    $"Generated screen quad {quad} disagrees with glyph slots.");
                for (int index = quad * 6; index < quad * 6 + 6; index++)
                {
                    Require(triangles[index] >= first &&
                            triangles[index] < first + 4,
                        $"Generated screen quad {quad} has cross-cell triangles.");
                }

                float minU = float.PositiveInfinity;
                float minV = float.PositiveInfinity;
                float maxU = float.NegativeInfinity;
                float maxV = float.NegativeInfinity;
                for (int vertex = first; vertex < first + 4; vertex++)
                {
                    Vector2 point = apertureUv[vertex];
                    Require(point.x >= -CoverageTolerance &&
                            point.x <= 1f + CoverageTolerance &&
                            point.y >= -CoverageTolerance &&
                            point.y <= 1f + CoverageTolerance,
                        $"Generated screen quad {quad} leaves normalized aperture space.");
                    minU = Mathf.Min(minU, point.x);
                    minV = Mathf.Min(minV, point.y);
                    maxU = Mathf.Max(maxU, point.x);
                    maxV = Mathf.Max(maxV, point.y);
                }
                Require(maxU - minU > CoverageTolerance &&
                        maxV - minV > CoverageTolerance,
                    $"Generated screen quad {quad} is degenerate.");
                rectangles[aperture].Add(
                    Rect.MinMaxRect(minU, minV, maxU, maxV));

                if (!glyph)
                {
                    for (int vertex = first; vertex < first + 4; vertex++)
                    {
                        Require(atlasUv[vertex].x > 0f &&
                                atlasUv[vertex].x <
                                    1f / CockpitAvionicsV2PresentationAuthoring
                                        .AtlasColumns &&
                                atlasUv[vertex].y < 1f &&
                                atlasUv[vertex].y >
                                    1f - 1f /
                                    CockpitAvionicsV2PresentationAuthoring
                                        .AtlasRows,
                            "A generated screen background does not sample the " +
                            "interior of the verified black atlas cell.");
                    }
                }
            }

            for (int aperture = 0; aperture < rectangles.Length; aperture++)
            {
                float area = 0f;
                for (int first = 0;
                     first < rectangles[aperture].Count;
                     first++)
                {
                    Rect a = rectangles[aperture][first];
                    area += a.width * a.height;
                    for (int second = first + 1;
                         second < rectangles[aperture].Count;
                         second++)
                    {
                        Rect b = rectangles[aperture][second];
                        float overlapU = Mathf.Min(a.xMax, b.xMax) -
                                         Mathf.Max(a.xMin, b.xMin);
                        float overlapV = Mathf.Min(a.yMax, b.yMax) -
                                         Mathf.Max(a.yMin, b.yMin);
                        Require(overlapU <= CoverageTolerance ||
                                overlapV <= CoverageTolerance,
                            $"Generated aperture {aperture} contains overlapping " +
                            "glyph/background area.");
                    }
                }
                Require(Mathf.Abs(area - 1f) <= CoverageTolerance,
                    $"Generated aperture {aperture} coverage is {area:R}, not 1.");
            }

            Texture2D atlas = layout.ExistingGlyphAtlas;
            Require(atlas != null, "The generated glyph atlas is missing.");
            Require(atlas.width ==
                        CockpitAvionicsV2PresentationAuthoring.AtlasColumns *
                        CockpitAvionicsV2PresentationAuthoring.CellPixels &&
                    atlas.height ==
                        CockpitAvionicsV2PresentationAuthoring.AtlasRows *
                        CockpitAvionicsV2PresentationAuthoring.CellPixels &&
                    atlas.format == TextureFormat.RGBA32,
                "The deterministic opaque-black glyph atlas identity changed.");
        }

        private static void ValidateGeneratedMaterial(Material material)
        {
            Require(material != null && material.shader != null,
                "The generated screen material or shader is missing.");
            Require(material.shader.isSupported &&
                    !string.Equals(
                        material.shader.name, "Hidden/InternalErrorShader",
                        StringComparison.Ordinal),
                "The generated screen uses an unsupported or error shader.");
            Require(material.renderQueue < (int)RenderQueue.Transparent &&
                    string.Equals(
                        material.GetTag("RenderType", false, string.Empty),
                        "Opaque", StringComparison.OrdinalIgnoreCase),
                "The generated screen material must remain opaque.");

            Shader shader = material.shader;
            int propertyCount = shader.GetPropertyCount();
            for (int property = 0; property < propertyCount; property++)
            {
                if (shader.GetPropertyType(property) !=
                    ShaderPropertyType.Color)
                    continue;
                Color value = material.GetColor(
                    shader.GetPropertyName(property));
                Require(!IsMagenta(value),
                    "The generated screen material contains a magenta color constant.");
            }

            string shaderPath = AssetDatabase.GetAssetPath(shader);
            Require(!string.IsNullOrWhiteSpace(shaderPath) &&
                    File.Exists(Path.GetFullPath(shaderPath)),
                "The generated screen shader source path is missing.");
            string compact = new string(
                File.ReadAllText(Path.GetFullPath(shaderPath))
                    .Where(character => !char.IsWhiteSpace(character))
                    .ToArray()).ToLowerInvariant();
            Require(!compact.Contains("magenta") &&
                    !compact.Contains("fixed3(1,0,1)") &&
                    !compact.Contains("fixed4(1,0,1,1)") &&
                    !compact.Contains("float3(1,0,1)") &&
                    !compact.Contains("float4(1,0,1,1)"),
                "The generated screen shader contains an unexpected magenta constant.");
        }

        private static void ValidateAnalogOwnership(GameObject root)
        {
            CockpitInstrumentV2Layout layout =
                AssetDatabase.LoadAssetAtPath<CockpitInstrumentV2Layout>(
                    CockpitInstrumentV2Authoring.LayoutPath);
            Require(
                CockpitInstrumentV2EditorContract.ValidateLayoutIdentity(
                    layout, out string layoutError),
                layoutError);

            Mesh dynamicMesh = layout.CombinedDynamicMesh;
            var selectors = new List<Vector4>();
            dynamicMesh.GetUVs(3, selectors);
            Require(selectors.Count == dynamicMesh.vertexCount,
                "The analog generated TEXCOORD3 stream is incomplete.");
            int[] triangles = dynamicMesh.triangles;
            for (int triangle = 0; triangle < triangles.Length; triangle += 3)
            {
                Vector4 a = selectors[triangles[triangle]];
                Vector4 b = selectors[triangles[triangle + 1]];
                Vector4 c = selectors[triangles[triangle + 2]];
                Require(Mathf.Abs(a.y - b.y) <= LayerTolerance &&
                        Mathf.Abs(a.y - c.y) <= LayerTolerance,
                    $"Analog generated triangle {triangle / 3} mixes depth layers.");
                Require(IsLayer(a.y, 1f) || IsLayer(a.y, 2f) ||
                        IsLayer(a.y, 3f),
                    $"Analog generated triangle {triangle / 3} remains on the " +
                    "unclassified default layer.");
                if (triangles[triangle] < layout.StaticOverlayVertexCount &&
                    a.x < 0.5f)
                {
                    Require(IsLayer(a.y, 1f) || IsLayer(a.y, 3f),
                        "Copied static face artwork is not on an explicit " +
                        "synthetic-face or source-plane layer.");
                }
            }

            for (int index = 0; index < layout.Bindings.Length; index++)
            {
                CockpitInstrumentV2MeshBinding binding = layout.Bindings[index];
                float expectedLayer = binding.label.EndsWith(
                    "_card", StringComparison.Ordinal) ? 1f : 2f;
                for (int vertex = binding.firstVertex;
                     vertex < binding.firstVertex + binding.vertexCount;
                     vertex++)
                {
                    Require(IsLayer(selectors[vertex].y, expectedLayer),
                        $"Analog binding '{binding.label}' is not on its intended " +
                        $"depth layer {expectedLayer:R}.");
                }
            }

            Transform meter = root.transform.Find(
                CockpitAnalogInstrumentAuthoring.MeterRendererPath);
            Require(meter != null, "The exact Meter_01 source path is missing.");
            MeshFilter meterFilter = meter.GetComponent<MeshFilter>();
            MeshRenderer meterRenderer = meter.GetComponent<MeshRenderer>();
            Require(meterFilter != null && meterRenderer != null &&
                    meterRenderer.enabled &&
                    meterFilter.sharedMesh == layout.NeutralizedSourceMesh,
                "The only enabled Meter_01 renderer is not the neutralized source.");

            Transform generated = FindUniqueTransform(
                root, CockpitCompletionV3Authoring.DynamicInstrumentObjectName);
            MeshFilter generatedFilter = generated.GetComponent<MeshFilter>();
            MeshRenderer generatedRenderer =
                generated.GetComponent<MeshRenderer>();
            Require(generatedFilter != null && generatedRenderer != null &&
                    generatedRenderer.enabled &&
                    generatedFilter.sharedMesh == layout.CombinedDynamicMesh,
                "The generated analog renderer is missing or disabled.");

            MeshFilter[] filters =
                root.GetComponentsInChildren<MeshFilter>(true);
            for (int index = 0; index < filters.Length; index++)
            {
                Mesh mesh = filters[index].sharedMesh;
                MeshRenderer renderer =
                    filters[index].GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy ||
                    mesh == layout.NeutralizedSourceMesh)
                    continue;
                Require(!string.Equals(
                        mesh.name,
                        CockpitInstrumentV2EditorContract.SourceMeshName,
                        StringComparison.Ordinal),
                    "A second enabled renderer uses the unneutralized Meter_01 mesh.");
            }

            ValidateCopiedSourceTrianglesRemoved(layout, selectors);
        }

        private static void ValidateCopiedSourceTrianglesRemoved(
            CockpitInstrumentV2Layout layout,
            List<Vector4> dynamicSelectors)
        {
            Mesh neutralized = layout.NeutralizedSourceMesh;
            var retained = new HashSet<string>(StringComparer.Ordinal);
            int[] sourceTriangles = neutralized.triangles;
            Vector3[] sourceVertices = neutralized.vertices;
            for (int triangle = 0;
                 triangle < sourceTriangles.Length;
                 triangle += 3)
            {
                retained.Add(TriangleKey(
                    sourceVertices[sourceTriangles[triangle]],
                    sourceVertices[sourceTriangles[triangle + 1]],
                    sourceVertices[sourceTriangles[triangle + 2]]));
            }

            Mesh dynamicMesh = layout.CombinedDynamicMesh;
            int[] dynamicTriangles = dynamicMesh.triangles;
            Vector3[] dynamicVertices = dynamicMesh.vertices;
            for (int triangle = 0;
                 triangle < dynamicTriangles.Length;
                 triangle += 3)
            {
                int a = dynamicTriangles[triangle];
                int b = dynamicTriangles[triangle + 1];
                int c = dynamicTriangles[triangle + 2];
                if (dynamicSelectors[a].x > 0.5f ||
                    dynamicSelectors[b].x > 0.5f ||
                    dynamicSelectors[c].x > 0.5f)
                    continue;
                Require(!retained.Contains(TriangleKey(
                        dynamicVertices[a], dynamicVertices[b],
                        dynamicVertices[c])),
                    $"Generated copied-source triangle {triangle / 3} is still " +
                    "present in the neutralized Meter_01 mesh.");
            }
        }

        private static void ValidateNoRelevantRendererDuplicates(
            GameObject root,
            Mesh sourceDisplay,
            Mesh generatedDisplay)
        {
            MeshRenderer[] renderers =
                root.GetComponentsInChildren<MeshRenderer>(true);
            for (int first = 0; first < renderers.Length; first++)
            {
                MeshFilter firstFilter =
                    renderers[first].GetComponent<MeshFilter>();
                Mesh firstMesh = firstFilter != null
                    ? firstFilter.sharedMesh
                    : null;
                if (firstMesh == null ||
                    firstMesh != generatedDisplay && firstMesh != sourceDisplay)
                    continue;
                for (int second = first + 1;
                     second < renderers.Length;
                     second++)
                {
                    MeshFilter secondFilter =
                        renderers[second].GetComponent<MeshFilter>();
                    Mesh secondMesh = secondFilter != null
                        ? secondFilter.sharedMesh
                        : null;
                    if (firstMesh != secondMesh ||
                        !renderers[first].sharedMaterials.SequenceEqual(
                            renderers[second].sharedMaterials) ||
                        !MatrixApproximately(
                            renderers[first].transform.localToWorldMatrix,
                            renderers[second].transform.localToWorldMatrix))
                        continue;
                    Require(false,
                        "An identical center-stack mesh/material/transform " +
                        "renderer duplicate exists.");
                }
            }
        }

        private static bool IsSixApertureTopology(Mesh mesh)
        {
            return mesh != null &&
                   mesh.vertexCount ==
                       CockpitAvionicsV2Authoring.ExpectedReadoutVertices &&
                   mesh.subMeshCount == 1 &&
                   mesh.triangles.Length / 3 ==
                       CockpitAvionicsV2Authoring.ExpectedReadoutTriangles;
        }

        private static Bounds TransformBounds(
            Matrix4x4 matrix,
            Bounds source)
        {
            Vector3 min = source.min;
            Vector3 max = source.max;
            Vector3 first = matrix.MultiplyPoint3x4(min);
            var result = new Bounds(first, Vector3.zero);
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z)));
            }
            return result;
        }

        private static Bounds GetSourceApertureBounds(
            Mesh mesh,
            Matrix4x4 localToWorld,
            out int selectedTriangles)
        {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int[] triangles = mesh.triangles;
            Require(uv.Length == vertices.Length,
                "The imported center-stack source UV channel is incomplete.");
            selectedTriangles = 0;
            var bounds = new Bounds();
            bool initialized = false;
            for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
            {
                int a = triangles[triangle * 3];
                int b = triangles[triangle * 3 + 1];
                int c = triangles[triangle * 3 + 2];
                if (!CockpitAvionicsV2PresentationAuthoring
                        .IsImportedApertureTriangle(vertices, uv, a, b, c))
                    continue;

                selectedTriangles++;
                foreach (int index in new[] { a, b, c })
                {
                    Vector3 point = localToWorld.MultiplyPoint3x4(vertices[index]);
                    if (!initialized)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }
            Require(initialized,
                "No imported center-stack aperture triangles were found.");
            return bounds;
        }

        private static int CountImportedApertureTriangles(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int[] triangles = mesh.triangles;
            int count = 0;
            for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
            {
                int first = triangle * 3;
                if (CockpitAvionicsV2PresentationAuthoring
                    .IsImportedApertureTriangle(
                        vertices, uv, triangles[first], triangles[first + 1],
                        triangles[first + 2]))
                    count++;
            }
            return count;
        }

        private static void ValidateRendererAuthority(
            UnityEngine.Object owner,
            string propertyName,
            Renderer source,
            Renderer replacement,
            string label)
        {
            Require(owner != null, $"The {label} authority is missing.");
            var serialized = new SerializedObject(owner);
            SerializedProperty renderers = serialized.FindProperty(propertyName);
            Require(renderers != null && renderers.isArray,
                $"The {label} renderer array is unavailable.");
            int sourceReferences = 0;
            int replacementReferences = 0;
            for (int index = 0; index < renderers.arraySize; index++)
            {
                UnityEngine.Object value = renderers.GetArrayElementAtIndex(index)
                    .objectReferenceValue;
                if (value == source) sourceReferences++;
                if (value == replacement) replacementReferences++;
            }
            Require(sourceReferences == 0 && replacementReferences == 1,
                $"The {label} authority must reference only the neutralized " +
                $"hardware renderer; source={sourceReferences}, " +
                $"replacement={replacementReferences}.");
        }

        private static bool BoundsApproximately(Bounds a, Bounds b)
        {
            return Vector3.Distance(a.center, b.center) <=
                       BoundsTolerance &&
                   Vector3.Distance(a.size, b.size) <=
                       BoundsTolerance;
        }

        private static bool IsLayer(float value, float expected)
        {
            return Mathf.Abs(value - expected) <= LayerTolerance;
        }

        private static bool IsMagenta(Color value)
        {
            return value.a > 0.01f && value.r > 0.8f &&
                   value.g < 0.2f && value.b > 0.8f;
        }

        private static string TriangleKey(Vector3 a, Vector3 b, Vector3 c)
        {
            string[] points = { PointKey(a), PointKey(b), PointKey(c) };
            Array.Sort(points, StringComparer.Ordinal);
            return string.Join("|", points);
        }

        private static string PointKey(Vector3 point)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:F7},{1:F7},{2:F7}", point.x, point.y, point.z);
        }

        private static bool MatrixApproximately(Matrix4x4 a, Matrix4x4 b)
        {
            for (int index = 0; index < 16; index++)
            {
                if (Mathf.Abs(a[index] - b[index]) > GeometryTolerance)
                    return false;
            }
            return true;
        }

        private static Transform FindUniqueTransform(
            GameObject root,
            string name)
        {
            Transform found = null;
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                if (!string.Equals(
                        transforms[index].name, name,
                        StringComparison.Ordinal))
                    continue;
                Require(found == null,
                    $"Multiple authored transforms are named '{name}'.");
                found = transforms[index];
            }
            Require(found != null,
                $"Required authored transform '{name}' is missing.");
            return found;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(message)
                        ? "Cockpit depth ownership contract failed."
                        : message);
        }
    }
}
