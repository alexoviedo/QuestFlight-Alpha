using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace QuestFlightLab.Editor
{
    /// <summary>Offline-only authoring for the two combined raised-road batches.</summary>
    public static class GradeSeparatedRoadV1Authoring
    {
        public static TrafficRoadSurfaceAuthoring.Ground VisibleGroundOverride;
        private const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        private const string AuthorityPath =
            "Assets/Production/Environment/GradeSeparatedRoadsV1/Data/KBDU_GradeSeparatedRoadAuthorityV1.json";
        private const string TerrainPath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset";
        private const string MeshDirectory =
            "Assets/Production/Environment/GradeSeparatedRoadsV1/Generated";
        private const string MaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";
        private const string RootName = "KBDU_GradeSeparatedRoadsV1";
        // Keep distant authoring compact, but tessellate the visible 12 km core
        // closely enough that the terrain-matching embankment cannot read as a
        // sequence of long triangular teeth.  This is offline geometry only.
        private const float NearApproachSpacingMeters = 25f;
        private const float OuterApproachSpacingMeters = 100f;
        private const float MaximumApproachEmbankmentWidthMeters = 40f;
        private const float MinimumApproachEmbankmentWidthMeters = 0.5f;
        private const float MaximumApproachEmbankmentGrade = 0.18f;

        [Serializable]
        private sealed class Report
        {
            public string schema = "questflight.grade-separated-road-authoring-report.v1";
            public int profileCount;
            public int rendererCount;
            public int drawDelta;
            public int triangleDelta;
            public int materialDelta;
            public int textureDelta;
            public int runtimeComponentDelta;
            public int nearTriangleCount;
            public int outerTriangleCount;
            public Bounds nearBounds;
            public Bounds outerBounds;
            public string authorityAssetPath;
            public string materialAssetPath;
        }

        private sealed class MeshBuilder
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();
            public readonly List<int> concrete = new List<int>();
            public bool structural;

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3? normalOverride = null)
            {
                int start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);
                Vector3 normal = normalOverride ?? Vector3.Cross(c - a, b - a).normalized;
                normals.Add(normal);
                normals.Add(normal);
                normals.Add(normal);
                normals.Add(normal);
                var indices=structural?concrete:triangles;
                indices.Add(start);
                indices.Add(start + 2);
                indices.Add(start + 1);
                indices.Add(start);
                indices.Add(start + 3);
                indices.Add(start + 2);
            }
        }

        public static void RunBatch()
        {
            try
            {
                Author();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Author()
        {
            TextAsset authorityAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(AuthorityPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            ProductionTerrainSurfaceAsset terrain =
                AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(TerrainPath);
            if (authorityAsset == null || material == null || terrain == null)
                throw new InvalidOperationException("Grade-separated authority, terrain, or material is missing.");
            KbduGradeSeparatedRoadAuthorityV1 authority =
                new KbduGradeSeparatedRoadAuthorityV1(authorityAsset);
            MeshBuilder near = new MeshBuilder();
            MeshBuilder outer = new MeshBuilder();
            foreach (IGrouping<string, KbduGradeSeparatedRoadAuthorityV1.Profile> structure in
                     authority.Data.profiles.GroupBy(profile => profile.source_cdot_structure_id))
            {
                foreach (List<KbduGradeSeparatedRoadAuthorityV1.Profile> cluster in
                         ClusterProfiles(structure.ToList()))
                {
                    Vector3 midpoint = BridgeCenter(cluster[0]);
                    MeshBuilder builder = Mathf.Max(Mathf.Abs(midpoint.x), Mathf.Abs(midpoint.z)) <= 6000f
                        ? near
                        : outer;
                    foreach (KbduGradeSeparatedRoadAuthorityV1.Profile profile in cluster)
                        AddApproaches(builder, profile, terrain);
                    AddMergedBridge(builder, cluster);
                }
            }

            EnsureFolder("Assets/Production/Environment/GradeSeparatedRoadsV1");
            EnsureFolder(MeshDirectory);
            const string concretePath=MeshDirectory+"/KBDU_BridgeConcrete.mat";
            Material concrete=AssetDatabase.LoadAssetAtPath<Material>(concretePath);
            if(concrete==null){concrete=new Material(Shader.Find("QuestFlight/Bridge Concrete"));AssetDatabase.CreateAsset(concrete,concretePath);}
            Mesh nearMesh = SaveMesh(near, MeshDirectory + "/KBDU_GradeSeparatedRoads_Near_V1.asset");
            Mesh outerMesh = SaveMesh(outer, MeshDirectory + "/KBDU_GradeSeparatedRoads_Outer_V1.asset");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform environment = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(transform => transform.name == "ProductionEnvironmentRoot");
            if (environment == null)
                throw new InvalidOperationException("ProductionEnvironmentRoot was not found.");
            Transform existing = environment.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            GameObject roadRoot = new GameObject(RootName);
            roadRoot.transform.SetParent(environment, false);
            CreateBatch(roadRoot.transform, "RaisedRoadBatch_Near", nearMesh, material, concrete);
            CreateBatch(roadRoot.transform, "RaisedRoadBatch_Outer", outerMesh, material, concrete);

            KbduTrafficV2Runtime runtime = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<KbduTrafficV2Runtime>(true))
                .Single();
            runtime.ConfigureGradeSeparatedRoadAuthority(authorityAsset);
            PrefabUtility.RecordPrefabInstancePropertyModifications(runtime);
            EditorUtility.SetDirty(runtime);
            ProductionEnvironmentRoot environmentContract = environment.GetComponent<ProductionEnvironmentRoot>();
            ProductionEnvironmentBudget budget = environmentContract.CalculateBudget();
            environmentContract.bakedRendererCount = budget.rendererCount;
            environmentContract.bakedTriangleCount = budget.triangleCount;
            environmentContract.bakedMaterialCount = budget.materialCount;
            PrefabUtility.RecordPrefabInstancePropertyModifications(environmentContract);
            EditorUtility.SetDirty(environmentContract);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Could not save the product scene.");
            AssetDatabase.SaveAssets();

            Report report = new Report
            {
                profileCount = authority.ProfileCount,
                rendererCount = 2,
                drawDelta = 4,
                triangleDelta = (near.triangles.Count+near.concrete.Count+outer.triangles.Count+outer.concrete.Count)/3,
                materialDelta = 1,
                textureDelta = 0,
                runtimeComponentDelta = 0,
                nearTriangleCount = (near.triangles.Count+near.concrete.Count)/3,
                outerTriangleCount = (outer.triangles.Count+outer.concrete.Count)/3,
                nearBounds = nearMesh.bounds,
                outerBounds = outerMesh.bounds,
                authorityAssetPath = AuthorityPath,
                materialAssetPath = MaterialPath
            };
            string artifactDirectory = System.Environment.GetEnvironmentVariable(
                "QFL_GRADE_SEPARATED_ARTIFACT_DIR");
            if (!string.IsNullOrWhiteSpace(artifactDirectory))
            {
                Directory.CreateDirectory(artifactDirectory);
                File.WriteAllText(
                    Path.Combine(artifactDirectory, "grade_separated_road_authoring_report.json"),
                    JsonUtility.ToJson(report, true));
            }
            Debug.Log($"[QuestFlight][GradeSeparatedRoadsV1] profiles={report.profileCount}, " +
                      $"triangles={report.triangleDelta}, draws={report.drawDelta}");
        }

        public static Mesh BuildContactMesh(KbduGradeSeparatedRoadAuthorityV1.Profile profile)
        {
            var builder=new MeshBuilder();
            AddApproaches(builder,profile,AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(TerrainPath));
            AddMergedBridge(builder,new List<KbduGradeSeparatedRoadAuthorityV1.Profile>{profile});
            var mesh=new Mesh();mesh.SetVertices(builder.vertices);mesh.SetTriangles(builder.triangles,0);mesh.SetNormals(builder.normals);mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddApproaches(
            MeshBuilder builder,
            KbduGradeSeparatedRoadAuthorityV1.Profile profile,
            ProductionTerrainSurfaceAsset terrain)
        {
            Vector3 bridgeCenter = BridgeCenter(profile);
            float spacing = Mathf.Max(Mathf.Abs(bridgeCenter.x), Mathf.Abs(bridgeCenter.z)) <= 6000f
                ? NearApproachSpacingMeters
                : OuterApproachSpacingMeters;
            if (VisibleGroundOverride != null) spacing = 5f;
            SortedSet<float> distances = new SortedSet<float>();
            for (float distance = 0f; distance < profile.profile_length_meters;
                 distance += spacing)
                distances.Add(distance);
            distances.Add(profile.profile_length_meters);
            distances.Add(profile.bridge_interval_meters[0]);
            distances.Add(profile.bridge_interval_meters[1]);
            if (profile.approach_join_distances_meters != null)
                foreach (float join in profile.approach_join_distances_meters)
                    if (!distances.Any(existing => Mathf.Abs(existing - join) < 0.02f)) distances.Add(join);
            float[] samples = distances.ToArray();
            for (int index = 0; index < samples.Length - 1; index++)
            {
                float startDistance = samples[index];
                float endDistance = samples[index + 1];
                float midpoint = (startDistance + endDistance) * 0.5f;
                if (midpoint >= profile.bridge_interval_meters[0] &&
                    midpoint <= profile.bridge_interval_meters[1])
                    continue;
                float halfWidth = EffectiveRoadWidth(profile) * 0.5f;
                EvaluateEdges(profile, startDistance, halfWidth, halfWidth,
                    out Vector3 startLeft, out Vector3 startRight, out Vector3 startNormal);
                EvaluateEdges(profile, endDistance, halfWidth, halfWidth,
                    out Vector3 endLeft, out Vector3 endRight, out Vector3 endNormal);
                float endDistanceToGround=Mathf.Min(startDistance,profile.profile_length_meters-endDistance);
                int across=VisibleGroundOverride!=null&&endDistanceToGround<30?Mathf.CeilToInt(halfWidth*4):1;
                for(int strip=0;strip<across;strip++)
                {
                    Vector3 a=ApproachAcross(startLeft,startRight,strip/(float)across,startDistance,profile);
                    Vector3 b=ApproachAcross(startLeft,startRight,(strip+1)/(float)across,startDistance,profile);
                    Vector3 c=ApproachAcross(endLeft,endRight,(strip+1)/(float)across,endDistance,profile);
                    Vector3 d=ApproachAcross(endLeft,endRight,strip/(float)across,endDistance,profile);
                    builder.Quad(a,b,c,d);
                }
                AddApproachShoulder(
                    builder, terrain, startLeft, endLeft, startRight, endRight, true);
                AddApproachShoulder(
                    builder, terrain, startRight, endRight, startLeft, endLeft, false);
            }
        }

        private static float GroundBlendLength(KbduGradeSeparatedRoadAuthorityV1.Profile profile) =>
            Mathf.Max(.001f,Mathf.Min(30f,Mathf.Min(profile.bridge_interval_meters[0],profile.profile_length_meters-profile.bridge_interval_meters[1])));

        private static Vector3 ApproachAcross(Vector3 left,Vector3 right,float t,float distance,KbduGradeSeparatedRoadAuthorityV1.Profile profile)
        {
            Vector3 p=Vector3.Lerp(left,right,t);
            if(VisibleGroundOverride!=null)
            {
                float blend=1-Mathf.SmoothStep(0,1,Mathf.Min(distance,profile.profile_length_meters-distance)/GroundBlendLength(profile));
                if(blend>0)
                {
                    KbduGradeSeparatedRoadAuthorityV1.TryEvaluateProfile(profile,distance,out Vector3 center,out _);
                    p.y=Mathf.Lerp(center.y,VisibleGroundOverride.Height(new Vector2(p.x,p.z),out _),blend);
                }
            }
            return p;
        }

        private static void AddApproachShoulder(
            MeshBuilder builder,
            ProductionTerrainSurfaceAsset terrain,
            Vector3 start,
            Vector3 end,
            Vector3 startOpposite,
            Vector3 endOpposite,
            bool leftSide)
        {
            Vector3 startOuter = ApproachOuterEdge(terrain, start, startOpposite);
            Vector3 endOuter = ApproachOuterEdge(terrain, end, endOpposite);
            // The orthophoto already contains the ground appearance.  An upward
            // authoring normal prevents each low embankment panel from becoming
            // a visibly different dark triangle under directional lighting.
            if (leftSide) builder.Quad(start, end, endOuter, startOuter, Vector3.up);
            else builder.Quad(end, start, startOuter, endOuter, Vector3.up);
        }

        private static Vector3 ApproachOuterEdge(
            ProductionTerrainSurfaceAsset terrain,
            Vector3 roadEdge,
            Vector3 oppositeRoadEdge)
        {
            Vector3 outward = roadEdge - oppositeRoadEdge;
            outward.y = 0f;
            outward.Normalize();
            float width = MinimumApproachEmbankmentWidthMeters;
            Vector3 outer = roadEdge + outward * width;
            // Two deterministic refinements account for the terrain height at the
            // toe.  Width tapers naturally to almost nothing at each ground anchor.
            for (int iteration = 0; iteration < 2; iteration++)
            {
                float terrainHeight = roadEdge.y;
                if (terrain.TrySample(outer.x, outer.z, out ProductionTerrainSample sample))
                    terrainHeight = sample.heightMeters + 0.01f;
                if (VisibleGroundOverride != null)
                    terrainHeight = VisibleGroundOverride.Height(new Vector2(outer.x, outer.z), out _) + .01f;
                width = Mathf.Clamp(
                    Mathf.Abs(roadEdge.y - terrainHeight) / MaximumApproachEmbankmentGrade,
                    MinimumApproachEmbankmentWidthMeters,
                    MaximumApproachEmbankmentWidthMeters);
                outer = roadEdge + outward * width;
                outer.y = terrainHeight;
            }
            if (terrain.TrySample(outer.x, outer.z, out ProductionTerrainSample finalSample))
                outer.y = finalSample.heightMeters + 0.01f;
            if (VisibleGroundOverride != null)
                outer.y = VisibleGroundOverride.Height(new Vector2(outer.x, outer.z), out _) + .01f;
            return outer;
        }

        private static void AddMergedBridge(
            MeshBuilder builder,
            List<KbduGradeSeparatedRoadAuthorityV1.Profile> profiles)
        {
            KbduGradeSeparatedRoadAuthorityV1.Profile primary = profiles[0];
            float bridgeStart = primary.bridge_interval_meters[0];
            float bridgeEnd = primary.bridge_interval_meters[1];
            float bridgeMiddle = (bridgeStart + bridgeEnd) * 0.5f;
            CenterAndForward(primary, bridgeMiddle, out Vector3 primaryCenter, out Vector3 primaryForward);
            Vector3 primaryRight = new Vector3(primaryForward.z, 0f, -primaryForward.x).normalized;
            float leftExtent = EffectiveRoadWidth(primary) * 0.5f;
            float rightExtent = leftExtent;
            for (int index = 1; index < profiles.Count; index++)
            {
                KbduGradeSeparatedRoadAuthorityV1.Profile member = profiles[index];
                Vector3 center = BridgeCenter(member);
                float offset = Vector3.Dot(center - primaryCenter, primaryRight);
                float halfWidth = EffectiveRoadWidth(member) * 0.5f;
                leftExtent = Mathf.Max(leftExtent, -offset + halfWidth);
                rightExtent = Mathf.Max(rightExtent, offset + halfWidth);
            }
            SortedSet<float> distances = new SortedSet<float> { bridgeStart, bridgeEnd, bridgeMiddle };
            for (float distance = bridgeStart; distance < bridgeEnd; distance += 5f)
                distances.Add(distance);
            float[] samples = distances.ToArray();
            for (int index = 0; index < samples.Length - 1; index++)
            {
                EvaluateEdges(primary, samples[index], leftExtent, rightExtent,
                    out Vector3 startLeft, out Vector3 startRight, out Vector3 startNormal);
                EvaluateEdges(primary, samples[index + 1], leftExtent, rightExtent,
                    out Vector3 endLeft, out Vector3 endRight, out Vector3 endNormal);
                builder.Quad(startLeft, startRight, endRight, endLeft);
                Vector3 startDrop = startNormal * primary.deck_thickness_meters;
                Vector3 endDrop = endNormal * primary.deck_thickness_meters;
                Vector3 startLeftBottom = startLeft - startDrop;
                Vector3 startRightBottom = startRight - startDrop;
                Vector3 endLeftBottom = endLeft - endDrop;
                Vector3 endRightBottom = endRight - endDrop;
                builder.structural=true;
                builder.Quad(startLeft, endLeft, endLeftBottom, startLeftBottom);
                builder.Quad(endRight, startRight, startRightBottom, endRightBottom);
                builder.Quad(startRightBottom, startLeftBottom, endLeftBottom, endRightBottom);
                if(primary.source_cdot_structure_id=="D-16-DF"||primary.source_cdot_structure_id=="D-16-DL"||primary.source_cdot_structure_id=="D-16-DG")
                {
                    AddBarrier(builder,startLeft,endLeft,(startRight-startLeft).normalized);
                    AddBarrier(builder,endRight,startRight,(startLeft-startRight).normalized);
                }
                builder.structural=false;
            }
            AddEndClosure(builder, primary, bridgeStart, leftExtent, rightExtent, true);
            AddEndClosure(builder, primary, bridgeEnd, leftExtent, rightExtent, false);
        }

        private static void AddEndClosure(
            MeshBuilder builder,
            KbduGradeSeparatedRoadAuthorityV1.Profile profile,
            float distance,
            float leftExtent,
            float rightExtent,
            bool reverse)
        {
            EvaluateEdges(profile, distance, leftExtent, rightExtent,
                out Vector3 left, out Vector3 right, out Vector3 normal);
            Vector3 leftBottom = left - normal * profile.deck_thickness_meters;
            Vector3 rightBottom = right - normal * profile.deck_thickness_meters;
            builder.structural=true;
            if (VisibleGroundOverride!=null && (profile.source_cdot_structure_id=="D-16-DF"||profile.source_cdot_structure_id=="D-16-DL"||profile.source_cdot_structure_id=="D-16-DG"))
            {
                leftBottom.y=Mathf.Min(leftBottom.y,VisibleGroundOverride.Height(new Vector2(left.x,left.z),out _));
                rightBottom.y=Mathf.Min(rightBottom.y,VisibleGroundOverride.Height(new Vector2(right.x,right.z),out _));
            }
            if (reverse) builder.Quad(right, left, leftBottom, rightBottom);
            else builder.Quad(left, right, rightBottom, leftBottom);
            builder.structural=false;
        }

        private static void AddBarrier(MeshBuilder b,Vector3 a,Vector3 c,Vector3 inward)
        {
            Vector3 d=inward*.32f,up=Vector3.up*.8f;
            b.Quad(a,c,c+up,a+up);
            b.Quad(c+d,a+d,a+d+up,c+d+up);
            b.Quad(a+up,c+up,c+d+up,a+d+up);
        }

        private static void EvaluateEdges(
            KbduGradeSeparatedRoadAuthorityV1.Profile profile,
            float distance,
            float leftExtent,
            float rightExtent,
            out Vector3 left,
            out Vector3 right,
            out Vector3 normal)
        {
            if (!KbduGradeSeparatedRoadAuthorityV1.TryEvaluateProfile(
                    profile, distance, out Vector3 center, out normal))
                throw new InvalidOperationException("Could not evaluate profile " + profile.span_id);
            float delta = Mathf.Min(1f, Mathf.Max(0.1f, profile.profile_length_meters * 0.001f));
            KbduGradeSeparatedRoadAuthorityV1.TryEvaluateProfile(
                profile, Mathf.Min(profile.profile_length_meters, distance + delta),
                out Vector3 ahead, out _);
            KbduGradeSeparatedRoadAuthorityV1.TryEvaluateProfile(
                profile, Mathf.Max(0f, distance - delta), out Vector3 behind, out _);
            Vector3 forward = (ahead - behind).normalized;
            Vector3 lateral = new Vector3(forward.z, 0f, -forward.x).normalized;
            left = center - lateral * leftExtent;
            right = center + lateral * rightExtent;
            if(VisibleGroundOverride!=null)
            {
                // Match the full end cross-section, not just its center point.
                float endBlend=1-Mathf.SmoothStep(0,1,Mathf.Min(distance,profile.profile_length_meters-distance)/GroundBlendLength(profile));
                if(endBlend>0)
                {
                    left.y=Mathf.Lerp(left.y,VisibleGroundOverride.Height(new Vector2(left.x,left.z),out _),endBlend);
                    right.y=Mathf.Lerp(right.y,VisibleGroundOverride.Height(new Vector2(right.x,right.z),out _),endBlend);
                }
            }
        }

        private static List<List<KbduGradeSeparatedRoadAuthorityV1.Profile>> ClusterProfiles(
            List<KbduGradeSeparatedRoadAuthorityV1.Profile> profiles)
        {
            List<List<KbduGradeSeparatedRoadAuthorityV1.Profile>> clusters =
                new List<List<KbduGradeSeparatedRoadAuthorityV1.Profile>>();
            foreach (KbduGradeSeparatedRoadAuthorityV1.Profile profile in profiles)
            {
                Vector3 center = BridgeCenter(profile);
                CenterAndForward(profile,
                    (profile.bridge_interval_meters[0] + profile.bridge_interval_meters[1]) * 0.5f,
                    out _, out Vector3 forward);
                List<KbduGradeSeparatedRoadAuthorityV1.Profile> match = null;
                foreach (List<KbduGradeSeparatedRoadAuthorityV1.Profile> cluster in clusters)
                {
                    KbduGradeSeparatedRoadAuthorityV1.Profile primary = cluster[0];
                    Vector3 primaryCenter = BridgeCenter(primary);
                    CenterAndForward(primary,
                        (primary.bridge_interval_meters[0] + primary.bridge_interval_meters[1]) * 0.5f,
                        out _, out Vector3 primaryForward);
                    float longitudinal = Mathf.Abs(Vector3.Dot(center - primaryCenter, primaryForward));
                    float maximumLongitudinal =
                        ((primary.bridge_interval_meters[1] - primary.bridge_interval_meters[0]) +
                         (profile.bridge_interval_meters[1] - profile.bridge_interval_meters[0])) * 0.5f - 5f;
                    Vector3 primaryRight =
                        new Vector3(primaryForward.z, 0f, -primaryForward.x).normalized;
                    float lateral = Mathf.Abs(Vector3.Dot(center - primaryCenter, primaryRight));
                    float maximumLateral =
                        (EffectiveRoadWidth(primary) + EffectiveRoadWidth(profile)) * 0.5f + 0.25f;
                    if (Mathf.Abs(Vector3.Dot(forward, primaryForward)) >= Mathf.Cos(15f * Mathf.Deg2Rad) &&
                        longitudinal < maximumLongitudinal &&
                        lateral <= maximumLateral)
                    {
                        match = cluster;
                        break;
                    }
                }
                if (match == null)
                {
                    match = new List<KbduGradeSeparatedRoadAuthorityV1.Profile>();
                    clusters.Add(match);
                }
                match.Add(profile);
            }
            return clusters;
        }

        private static Vector3 BridgeCenter(KbduGradeSeparatedRoadAuthorityV1.Profile profile)
        {
            CenterAndForward(profile,
                (profile.bridge_interval_meters[0] + profile.bridge_interval_meters[1]) * 0.5f,
                out Vector3 center, out _);
            return center;
        }

        private static void CenterAndForward(
            KbduGradeSeparatedRoadAuthorityV1.Profile profile,
            float distance,
            out Vector3 center,
            out Vector3 forward)
        {
            if (!KbduGradeSeparatedRoadAuthorityV1.TryEvaluateProfile(
                    profile, distance, out center, out _))
                throw new InvalidOperationException("Could not evaluate " + profile.span_id);
            float delta = 1f;
            KbduGradeSeparatedRoadAuthorityV1.TryEvaluateProfile(
                profile, Mathf.Min(profile.profile_length_meters, distance + delta), out Vector3 ahead, out _);
            KbduGradeSeparatedRoadAuthorityV1.TryEvaluateProfile(
                profile, Mathf.Max(0f, distance - delta), out Vector3 behind, out _);
            forward = (ahead - behind).normalized;
        }

        private static float EffectiveRoadWidth(KbduGradeSeparatedRoadAuthorityV1.Profile profile)
        {
            return int.TryParse(profile.source_lanes, out int lanes)
                ? Mathf.Min(profile.surface_width_meters, Mathf.Max(3.5f, lanes * 3.5f + 2f))
                : profile.surface_width_meters;
        }

        private static Mesh SaveMesh(MeshBuilder builder, string path)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create=mesh==null;
            if(create)mesh=new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            else mesh.Clear();
            if (builder.vertices.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(builder.vertices);
            mesh.subMeshCount=2;
            mesh.SetTriangles(builder.triangles,0,true);
            mesh.SetTriangles(builder.concrete,1,true);
            mesh.SetNormals(builder.normals);
            mesh.RecalculateBounds();
            if(create)AssetDatabase.CreateAsset(mesh,path);else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static void CreateBatch(Transform parent, string name, Mesh mesh, Material material, Material concrete)
        {
            GameObject batch = new GameObject(name);
            batch.transform.SetParent(parent, false);
            batch.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = batch.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { material,concrete };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
