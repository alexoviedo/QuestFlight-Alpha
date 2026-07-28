using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment.Editor
{
    /// <summary>
    /// Converts the compact, reviewed fence authority into four terrain-aligned
    /// flight-distance meshes. No production geometry is created at runtime,
    /// and no LOD switch can produce fence popping.
    /// </summary>
    public static class AirportPerimeterFenceAuthoring
    {
        public const string Root = "Assets/Production/Environment/AirportFenceV1";
        public const string ManifestPath = Root + "/Data/KBDU_AirportFenceSourceV1.json";
        public const string GeneratedFolder = Root + "/Generated";
        public const string PrefabPath = Root + "/KBDU_AirportPerimeterFence.prefab";
        public const string ProductEnvironmentPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string SharedMaterialPath =
            "Assets/Production/Environment/AirportCoreV4/Materials/AirportCoreHangarKitAtlas.mat";
        public const string FenceMaterialPath =
            Root + "/AirportPerimeterFence.mat";

        [MenuItem("QuestFlight/Airport Fence V1/Build And Integrate")]
        public static void BuildAndIntegrate()
        {
            EnsureFolder(Root);
            EnsureFolder(Root + "/Data");
            EnsureFolder(GeneratedFolder);

            TextAsset source = LoadRequired<TextAsset>(ManifestPath);
            FenceDocument document = JsonUtility.FromJson<FenceDocument>(source.text);
            if (document == null || document.sections == null ||
                document.sections.Length != AirportPerimeterFenceRoot.ExpectedSectionCount)
                throw new InvalidOperationException("Fence source must contain four sections.");

            GameObject product = PrefabUtility.LoadPrefabContents(ProductEnvironmentPath);
            try
            {
                ProductionEnvironmentRoot environment =
                    product.GetComponent<ProductionEnvironmentRoot>();
                if (environment == null || environment.authoritativeTerrainSurface == null ||
                    environment.airportContext == null)
                    throw new InvalidOperationException(
                        "Production environment lacks airport/terrain authority.");

                Material material = BuildFenceMaterial();
                AerialSceneryRoot aerialScenery =
                    product.GetComponentInChildren<AerialSceneryRoot>(true);
                if (aerialScenery == null ||
                    aerialScenery.authoritativeAirportPatch == null ||
                    aerialScenery.authoritativeAirportPatch.sharedMesh == null)
                    throw new InvalidOperationException(
                        "The active lidar-conformed airport patch is unavailable.");
                VisualGroundSampler visualGround = new VisualGroundSampler(
                    aerialScenery.authoritativeAirportPatch,
                    environment.airportContext);
                GameObject fenceRoot = new GameObject(AirportPerimeterFenceRoot.GameObjectName);
                try
                {
                    AirportPerimeterFenceRoot marker =
                        fenceRoot.AddComponent<AirportPerimeterFenceRoot>();
                    marker.sourceManifest = source;
                    marker.sharedMaterial = material;
                    marker.authoredPerimeterLengthMeters = CalculateSourceLength(document);

                    List<MeshRenderer> sectionRenderers = new List<MeshRenderer>();
                    int serializedTriangles = 0;
                    int selectedTriangles = 0;
                    for (int sectionIndex = 0;
                         sectionIndex < document.sections.Length;
                         sectionIndex++)
                    {
                        FenceSection section = document.sections[sectionIndex];
                        GameObject sectionObject =
                            new GameObject($"FenceSection_{sectionIndex:00}_{section.stable_id}");
                        sectionObject.transform.SetParent(fenceRoot.transform, false);
                        Mesh mesh = BuildSectionMesh(
                            section,
                            environment.authoritativeTerrainSurface,
                            visualGround);
                        string meshPath =
                            $"{GeneratedFolder}/{section.stable_id}.asset";
                        mesh = SaveMesh(mesh, meshPath);
                        int triangleCount = checked((int)mesh.GetIndexCount(0) / 3);
                        serializedTriangles += triangleCount;
                        selectedTriangles += triangleCount;

                        MeshFilter filter = sectionObject.AddComponent<MeshFilter>();
                        filter.sharedMesh = mesh;
                        MeshRenderer renderer = sectionObject.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = material;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        renderer.motionVectorGenerationMode =
                            MotionVectorGenerationMode.ForceNoMotion;
                        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        renderer.scaleInLightmap = 0f;
                        GameObjectUtility.SetStaticEditorFlags(
                            sectionObject,
                            (StaticEditorFlags)0);
                        sectionRenderers.Add(renderer);
                    }

                    marker.sections = sectionRenderers.ToArray();
                    marker.authoredMaximumSelectedTriangles = selectedTriangles;
                    marker.authoredSerializedTriangles = serializedTriangles;
                    marker.authoredRendererCount =
                        fenceRoot.GetComponentsInChildren<Renderer>(true).Length;
                    if (!marker.TryValidateContract(out string report))
                        throw new InvalidOperationException("Fence authoring failed: " + report);
                    PrefabUtility.SaveAsPrefabAsset(fenceRoot, PrefabPath);
                    DeleteObsoleteLodAssets(document);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(fenceRoot);
                }

                Integrate(product, environment);
                AssetDatabase.SaveAssets();
                Debug.Log("[QuestFlight][AirportFenceV1] SERIALIZED_INTEGRATION_SUCCESS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(product);
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        [MenuItem("QuestFlight/Airport Fence V1/Report Pilot Left Sightline")]
        public static void ReportPilotLeftSightline()
        {
            GameObject product = PrefabUtility.LoadPrefabContents(ProductEnvironmentPath);
            try
            {
                ProductionEnvironmentRoot environment =
                    product.GetComponent<ProductionEnvironmentRoot>();
                AerialSceneryRoot aerialScenery =
                    product.GetComponentInChildren<AerialSceneryRoot>(true);
                if (environment == null || aerialScenery == null ||
                    aerialScenery.authoritativeAirportPatch == null)
                    throw new InvalidOperationException(
                        "Pilot-left fence sightline dependencies are unavailable.");
                VisualGroundSampler visible = new VisualGroundSampler(
                    aerialScenery.authoritativeAirportPatch,
                    environment.airportContext);
                const float pilotX = -554.9619f;
                for (float north = -30f; north <= 100f; north += 5f)
                {
                    Vector2 point = new Vector2(pilotX, north);
                    bool hasVisible = visible.TrySample(point, out float visibleHeight);
                    bool hasAuthority =
                        environment.authoritativeTerrainSurface.TrySample(
                            point.x,
                            point.y,
                            out ProductionTerrainSample authority);
                    Debug.Log(
                        $"[QuestFlight][AirportFenceV1][Sightline] " +
                        $"x={pilotX:F3} z={north:F1} " +
                        $"visible={(hasVisible ? visibleHeight.ToString("F3") : "NA")} " +
                        $"authority={(hasAuthority ? authority.heightMeters.ToString("F3") : "NA")}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(product);
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        private static void Integrate(
            GameObject product,
            ProductionEnvironmentRoot environment)
        {
            AirportPerimeterFenceRoot prior = product
                .GetComponentsInChildren<AirportPerimeterFenceRoot>(true)
                .SingleOrDefault();
            if (prior != null)
                UnityEngine.Object.DestroyImmediate(prior.gameObject);

            GameObject prefab = LoadRequired<GameObject>(PrefabPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                prefab,
                environment.airportContext);
            instance.name = AirportPerimeterFenceRoot.GameObjectName;
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            environment.airportPerimeterFence = instance.transform;

            ProductionEnvironmentBudget budget = environment.CalculateBudget();
            environment.bakedRendererCount = budget.rendererCount;
            environment.bakedTriangleCount = budget.triangleCount;
            environment.bakedMaterialCount = budget.materialCount;
            EditorUtility.SetDirty(environment);
            if (!environment.TryValidateContract(out string report))
                throw new InvalidOperationException(
                    "Fence integration leaves the product invalid: " + report);
            PrefabUtility.SaveAsPrefabAsset(product, ProductEnvironmentPath);
        }

        private static Mesh BuildSectionMesh(
            FenceSection section,
            ProductionTerrainSurfaceAsset terrain,
            VisualGroundSampler visualGround)
        {
            List<Vector3> samples = SampleSourceVertices(
                section,
                terrain,
                visualGround);
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> colors = new List<Color>();
            float height = section.height_meters;
            float cumulativeMeters = 0f;
            float style = section.representation.Contains("wood") ? 1f : 0f;
            bool isNortheast = string.Equals(
                section.stable_id,
                "northeast_wildlife_fence",
                StringComparison.Ordinal);
            for (int index = 0; index < samples.Count - 1; index++)
            {
                Vector3 sourceFirst = samples[index];
                Vector3 sourceSecond = samples[index + 1];
                float sourceLength = Vector2.Distance(
                    new Vector2(sourceFirst.x, sourceFirst.z),
                    new Vector2(sourceSecond.x, sourceSecond.z));
                bool isPilotVisibleNorthCorridor = isNortheast && index == 0;
                int railIntervals = isPilotVisibleNorthCorridor
                    ? Mathf.Max(1, Mathf.CeilToInt(sourceLength / 80f))
                    : 1;
                for (int railIndex = 0; railIndex < railIntervals; railIndex++)
                {
                    float t0 = railIndex / (float)railIntervals;
                    float t1 = (railIndex + 1) / (float)railIntervals;
                    Vector3 first = GroundPoint(
                        terrain,
                        visualGround,
                        Vector2.Lerp(
                            new Vector2(sourceFirst.x, sourceFirst.z),
                            new Vector2(sourceSecond.x, sourceSecond.z),
                            t0));
                    Vector3 second = GroundPoint(
                        terrain,
                        visualGround,
                        Vector2.Lerp(
                            new Vector2(sourceFirst.x, sourceFirst.z),
                            new Vector2(sourceSecond.x, sourceSecond.z),
                            t1));
                    float railLength = Vector2.Distance(
                        new Vector2(first.x, first.z),
                        new Vector2(second.x, second.z));
                    AddFencePanel(
                        vertices,
                        triangles,
                        uvs,
                        colors,
                        first + Vector3.up * 0.025f,
                        second + Vector3.up * 0.025f,
                        height,
                        cumulativeMeters,
                        cumulativeMeters + railLength,
                        style);
                    AddSolidTopRail(
                        vertices,
                        triangles,
                        uvs,
                        colors,
                        first + Vector3.up * 0.025f,
                        second + Vector3.up * 0.025f,
                        height,
                        cumulativeMeters,
                        cumulativeMeters + railLength,
                        style);
                    cumulativeMeters += railLength;
                }
            }

            if (isNortheast)
            {
                Vector3 start = samples[0];
                Vector3 end = samples[1];
                float northLength = Vector2.Distance(
                    new Vector2(start.x, start.z),
                    new Vector2(end.x, end.z));
                int postCount = Mathf.CeilToInt(northLength / 32f) + 1;
                for (int postIndex = 0; postIndex < postCount; postIndex++)
                {
                    float t = postIndex / (float)(postCount - 1);
                    Vector3 xz = Vector3.Lerp(start, end, t);
                    Vector3 ground = GroundPoint(
                        terrain,
                        visualGround,
                        new Vector2(xz.x, xz.z));
                    AddSolidPostQuad(
                        vertices,
                        triangles,
                        uvs,
                        colors,
                        ground,
                        end - start,
                        height,
                        style);
                }
            }

            Mesh mesh = new Mesh
            {
                name = section.stable_id,
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static List<Vector3> SampleSourceVertices(
            FenceSection section,
            ProductionTerrainSurfaceAsset terrain,
            VisualGroundSampler visualGround)
        {
            if (section.points == null || section.points.Length < 2)
                throw new InvalidOperationException(section.stable_id + " has no points.");
            return section.points
                .Select(point => GroundPoint(
                    terrain,
                    visualGround,
                    new Vector2(point.east_meters, point.north_meters)))
                .ToList();
        }

        private static Vector3 GroundPoint(
            ProductionTerrainSurfaceAsset terrain,
            VisualGroundSampler visualGround,
            Vector2 point)
        {
            if (!terrain.TrySample(point.x, point.y, out ProductionTerrainSample sample))
                throw new InvalidOperationException(
                    $"Fence terrain sample failed at ({point.x:F1}, {point.y:F1}).");
            float queryHeight = sample.heightMeters;
            if (visualGround.TrySample(point, out float visibleHeight))
            {
                float delta = visibleHeight - queryHeight;
                if (Mathf.Abs(delta) >= 0.05f)
                {
                    Debug.Log(
                        $"[QuestFlight][AirportFenceV1] Visible-ground correction " +
                        $"({point.x:F1}, {point.y:F1}) query={queryHeight:F3}m " +
                        $"visible={visibleHeight:F3}m delta={delta:+0.000;-0.000}m");
                }
                return new Vector3(point.x, visibleHeight + 0.05f, point.y);
            }
            return new Vector3(point.x, queryHeight + 0.05f, point.y);
        }

        /// <summary>
        /// Samples the exact airport-patch mesh currently rendered by the product.
        /// The legacy terrain surface remains the fallback outside its bounded
        /// footprint, but it may not be used to ground airport-core visuals where
        /// lidar conformance changed the displayed surface.
        /// </summary>
        private sealed class VisualGroundSampler
        {
            private readonly Vector3[] vertices;
            private readonly int[] triangles;

            public VisualGroundSampler(
                MeshFilter visibleSurface,
                Transform fenceCoordinateFrame)
            {
                Mesh mesh = visibleSurface.sharedMesh;
                Vector3[] source = mesh.vertices;
                vertices = new Vector3[source.Length];
                for (int index = 0; index < source.Length; index++)
                {
                    Vector3 world = visibleSurface.transform.TransformPoint(source[index]);
                    vertices[index] = fenceCoordinateFrame.InverseTransformPoint(world);
                }
                triangles = mesh.triangles;
            }

            public bool TrySample(Vector2 point, out float height)
            {
                bool found = false;
                height = float.NegativeInfinity;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = vertices[triangles[index]];
                    Vector3 b = vertices[triangles[index + 1]];
                    Vector3 c = vertices[triangles[index + 2]];
                    if (!TryBarycentric(
                        point,
                        new Vector2(a.x, a.z),
                        new Vector2(b.x, b.z),
                        new Vector2(c.x, c.z),
                        out Vector3 weights))
                        continue;
                    float candidate =
                        a.y * weights.x +
                        b.y * weights.y +
                        c.y * weights.z;
                    if (!found || candidate > height)
                    {
                        height = candidate;
                        found = true;
                    }
                }
                return found;
            }

            private static bool TryBarycentric(
                Vector2 point,
                Vector2 a,
                Vector2 b,
                Vector2 c,
                out Vector3 weights)
            {
                float denominator =
                    (b.y - c.y) * (a.x - c.x) +
                    (c.x - b.x) * (a.y - c.y);
                if (Mathf.Abs(denominator) <= 0.000001f)
                {
                    weights = default;
                    return false;
                }
                float wa =
                    ((b.y - c.y) * (point.x - c.x) +
                     (c.x - b.x) * (point.y - c.y)) / denominator;
                float wb =
                    ((c.y - a.y) * (point.x - c.x) +
                     (a.x - c.x) * (point.y - c.y)) / denominator;
                float wc = 1f - wa - wb;
                const float tolerance = -0.0001f;
                weights = new Vector3(wa, wb, wc);
                return wa >= tolerance && wb >= tolerance && wc >= tolerance;
            }
        }

        private static void AddFencePanel(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            List<Color> colors,
            Vector3 firstGround,
            Vector3 secondGround,
            float height,
            float firstDistance,
            float secondDistance,
            float style)
        {
            int start = vertices.Count;
            Vector3 v0 = firstGround;
            Vector3 v1 = firstGround + Vector3.up * height;
            Vector3 v2 = secondGround + Vector3.up * height;
            Vector3 v3 = secondGround;
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);
            uvs.Add(new Vector2(firstDistance, 0f));
            uvs.Add(new Vector2(firstDistance, 1f));
            uvs.Add(new Vector2(secondDistance, 1f));
            uvs.Add(new Vector2(secondDistance, 0f));
            Color fencePanelContract = new Color(height * 0.5f, style, 0f, 0f);
            for (int index = 0; index < 4; index++) colors.Add(fencePanelContract);
            triangles.Add(start + 0);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 0);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private static void AddSolidTopRail(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            List<Color> colors,
            Vector3 firstGround,
            Vector3 secondGround,
            float height,
            float firstDistance,
            float secondDistance,
            float style)
        {
            int start = vertices.Count;
            float halfThickness = 0.08f;
            Vector3 v0 = firstGround + Vector3.up * (height - halfThickness);
            Vector3 v1 = firstGround + Vector3.up * (height + halfThickness);
            Vector3 v2 = secondGround + Vector3.up * (height + halfThickness);
            Vector3 v3 = secondGround + Vector3.up * (height - halfThickness);
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);
            uvs.Add(new Vector2(firstDistance, 0f));
            uvs.Add(new Vector2(firstDistance, 1f));
            uvs.Add(new Vector2(secondDistance, 1f));
            uvs.Add(new Vector2(secondDistance, 0f));
            Color solidFenceContract = new Color(height * 0.5f, style, 1f, 0f);
            for (int index = 0; index < 4; index++) colors.Add(solidFenceContract);
            triangles.Add(start + 0);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 0);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private static void AddSolidPostQuad(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            List<Color> colors,
            Vector3 ground,
            Vector3 fenceDirection,
            float height,
            float style)
        {
            int start = vertices.Count;
            Vector3 along = new Vector3(fenceDirection.x, 0f, fenceDirection.z).normalized;
            const float halfWidth = 0.09f;
            Vector3 left = ground - along * halfWidth;
            Vector3 right = ground + along * halfWidth;
            Color solidFenceContract = new Color(height * 0.5f, style, 1f, 0f);
            vertices.Add(left + Vector3.up * 0.025f);
            vertices.Add(left + Vector3.up * (height + 0.085f));
            vertices.Add(right + Vector3.up * (height + 0.085f));
            vertices.Add(right + Vector3.up * 0.025f);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, 0f));
            for (int index = 0; index < 4; index++) colors.Add(solidFenceContract);
            triangles.Add(start + 0);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 0);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private static Material BuildFenceMaterial()
        {
            Shader shader = Shader.Find("QuestFlight/Airport Perimeter Fence");
            if (shader == null)
                throw new InvalidOperationException(
                    "The dedicated airport perimeter fence shader is unavailable.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(FenceMaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "AirportPerimeterFence"
                };
                AssetDatabase.CreateAsset(material, FenceMaterialPath);
            }
            Material clean = new Material(shader)
            {
                name = "AirportPerimeterFence",
                renderQueue = (int)RenderQueue.Transparent,
                enableInstancing = false
            };
            clean.SetColor("_MetalColor", new Color(0.46f, 0.48f, 0.46f, 1f));
            clean.SetColor("_SlatColor", new Color(0.035f, 0.29f, 0.17f, 1f));
            clean.SetColor("_WoodColor", new Color(0.20f, 0.145f, 0.085f, 1f));
            clean.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.CopySerialized(clean, material);
            UnityEngine.Object.DestroyImmediate(clean);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static float CalculateSourceLength(FenceDocument document)
        {
            float length = 0f;
            foreach (FenceSection section in document.sections)
            {
                for (int index = 0; index < section.points.Length - 1; index++)
                {
                    FencePoint first = section.points[index];
                    FencePoint second = section.points[index + 1];
                    length += Vector2.Distance(
                        new Vector2(first.east_meters, first.north_meters),
                        new Vector2(second.east_meters, second.north_meters));
                }
            }
            return length;
        }

        private static Mesh SaveMesh(Mesh generated, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            EditorUtility.CopySerialized(generated, existing);
            existing.name = generated.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generated);
            return existing;
        }

        private static void DeleteObsoleteLodAssets(FenceDocument document)
        {
            foreach (FenceSection section in document.sections)
            {
                for (int lodIndex = 0; lodIndex < 3; lodIndex++)
                {
                    AssetDatabase.DeleteAsset(
                        $"{GeneratedFolder}/{section.stable_id}_LOD{lodIndex}.asset");
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null)
                throw new InvalidOperationException("Required fence asset is missing: " + path);
            return value;
        }

        [Serializable]
        private sealed class FenceDocument
        {
            public FenceSection[] sections;
        }

        [Serializable]
        private sealed class FenceSection
        {
            public string stable_id;
            public string representation;
            public float height_meters;
            public int[] gate_segment_indices;
            public FencePoint[] points;
        }

        [Serializable]
        private sealed class FencePoint
        {
            public float east_meters;
            public float north_meters;
        }
    }
}
