using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Globalization;
using System.Text.RegularExpressions;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Editor
{
    /// <summary>Offline repair/bake. Consumes visible mesh triangles, never physics raycasts.</summary>
    public static class TrafficRoadSurfaceAuthoring
    {
        const string Root = "Assets/Production/Environment/";
        const string Near = Root + "PhotorealAerialSurfaceV1/Terrain/Native/KBDU_ProductAeqd_BareEarth_Fixed.asset";
        const string Traffic = Root + "TrafficV1/KBDU_TrafficRoot.prefab";
        const string Manifest = Root + "TrafficV2/Data/KBDU_TrafficV2FullMapManifest.json";
        const string Parked = Root + "TrafficV1/Data/KBDU_TrafficManifestV1.json";
        const string Bank = Root + "TrafficV2/Data/KBDU_VisibleRoadLaneSurfaces.bytes";
        [Serializable] sealed class MeshPayload { public float[] vertices; public int[] triangles; }
        [Serializable] sealed class Routes { public Route[] corridors; }
        [Serializable] sealed class Route
        {
            public string corridor_id, direction_mode, road_class;
            public float[] points_xz_meters;
            public int lane_count;
        }
        [Serializable] sealed class ParkedDocument { public ParkedCar[] parked; }
        [Serializable] sealed class ParkedCar
        {
            public string id; public int archetype; public float x,y,z,yaw_degrees,pitch_degrees,roll_degrees;
        }
        [Serializable] sealed class ParkedPose { public string id; public float y,pitch_degrees,yaw_degrees,roll_degrees; }
        [Serializable] sealed class PosePayload { public ParkedPose[] poses; }
        [Serializable] sealed class Report
        {
            public int corridors, laneCount, samples, nearTriangles, parkedCount;
            public long bankBytes;
            public float maximumBakeProbeError, maximumRejectedProbeError;
            public Vector3 worstProbePosition;
            public string[] unresolvedCorridors;
            public string authoritySha256, nearRoadSha256, outerRoadSha256, bankSha256, groundQuerySha256;
            public string sourceManifestSha256, nearMeshSha256;
            public string license = "Project-authored derivative of existing USGS public-domain terrain and OSM ODbL road data; source records retained in existing manifests.";
            public string limitation = "Contact/lane correction; full intersection scheduling and footprint coverage require separate validation.";
        }
        struct Sample { public float distance; public Vector3 p,up; public Vector2 raw; }
        sealed class BridgePath
        {
            public KbduGradeSeparatedRoadAuthorityV1.Profile profile;
            public MeshSurface surface;
            public Vector2[] p; public float[] distance;
            public bool twoWay;
            public int laneCount;
        }

        public sealed class MeshSurface
        {
            readonly Vector3[] v; readonly int[] t;
            readonly Dictionary<long,List<int>> cells = new Dictionary<long,List<int>>();
            readonly float size; readonly Bounds bounds;
            static long Key(int x,int z) => ((long)x<<32)|(uint)z;
            public MeshSurface(Mesh mesh,float cellSize,bool topOnly=false)
            {
                v=mesh.vertices;t=topOnly?mesh.GetTriangles(0):mesh.triangles;size=cellSize;bounds=mesh.bounds;
                for(int i=0;i<t.Length;i+=3)
                {
                    Vector3 a=v[t[i]],b=v[t[i+1]],c=v[t[i+2]];
                    if(Mathf.Abs((b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x))<.000001f)continue;
                    int x0=Mathf.FloorToInt(Mathf.Min(a.x,Mathf.Min(b.x,c.x))/size),x1=Mathf.FloorToInt(Mathf.Max(a.x,Mathf.Max(b.x,c.x))/size);
                    int z0=Mathf.FloorToInt(Mathf.Min(a.z,Mathf.Min(b.z,c.z))/size),z1=Mathf.FloorToInt(Mathf.Max(a.z,Mathf.Max(b.z,c.z))/size);
                    for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                    { long k=Key(x,z);if(!cells.TryGetValue(k,out var list))cells[k]=list=new List<int>();list.Add(i); }
                }
            }
            public bool TrySample(Vector2 p,out float y,out Vector3 normal)
            {
                y=float.NegativeInfinity;normal=Vector3.up;
                if(p.x<bounds.min.x-.001f||p.x>bounds.max.x+.001f||p.y<bounds.min.z-.001f||p.y>bounds.max.z+.001f)return false;
                if(!cells.TryGetValue(Key(Mathf.FloorToInt(p.x/size),Mathf.FloorToInt(p.y/size)),out var candidates))return false;
                bool found=false;
                foreach(int i in candidates)
                {
                    Vector3 a=v[t[i]],b=v[t[i+1]],c=v[t[i+2]];
                    float bx=b.x-a.x,bz=b.z-a.z,cx=c.x-a.x,cz=c.z-a.z,dx=p.x-a.x,dz=p.y-a.z;
                    float det=bx*cz-bz*cx,u=(dx*cz-dz*cx)/det,w=(bx*dz-bz*dx)/det;
                    if(u<-.00001f||w<-.00001f||u+w>1.00001f)continue;
                    float h=a.y+u*(b.y-a.y)+w*(c.y-a.y);
                    if(h<y)continue;
                    y=h;normal=Vector3.Cross(c-a,b-a).normalized;if(normal.y<0)normal=-normal;found=true;
                }
                return found;
            }
        }

        public sealed class Ground
        {
            readonly MeshSurface near,mid,far;
            readonly ProductionTerrainSurfaceAsset fallback;
            public Ground()
            {
                near=new MeshSurface(AssetDatabase.LoadAssetAtPath<Mesh>(Near),32);
                mid=new MeshSurface(AssetDatabase.LoadAssetAtPath<Mesh>(Root+"Generated/Terrain_Mid12km_USGS_Mesh.asset"),128);
                far=new MeshSurface(AssetDatabase.LoadAssetAtPath<Mesh>(Root+"Generated/Terrain_Far24km_Immutable_USGS_Mesh.asset"),512);
                fallback=AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(Root+"ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");
            }
            public float Height(Vector2 p,out Vector3 normal)
            {
                p=new Vector2(Mathf.Clamp(p.x,-11999.99f,11999.99f),Mathf.Clamp(p.y,-11999.99f,11999.99f));
                if(near.TrySample(p,out float h,out normal))return h;
                float radius=Mathf.Max(Mathf.Abs(p.x),Mathf.Abs(p.y));
                if((radius<2100||radius>=6000&&radius<=6200)&&fallback.TrySample(p.x,p.y,out var s)) {normal=s.normal;return s.heightMeters;}
                if(mid.TrySample(p,out h,out normal)||far.TrySample(p,out h,out normal))return h;
                if(fallback.TrySample(p.x,p.y,out s)){normal=s.normal;return s.heightMeters;}
                throw new InvalidDataException("No visible terrain at "+p);
            }
        }

        public static void RunBatch()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_ROAD_REPAIR_DIR");
            if(string.IsNullOrEmpty(output))throw new InvalidOperationException("QFL_ROAD_REPAIR_DIR required");
            Directory.CreateDirectory(output);
            var payload=JsonUtility.FromJson<MeshPayload>(File.ReadAllText(Path.Combine(output,"terrain_payload.json")));
            if(payload?.vertices==null||payload.vertices.Length==0||payload.vertices.Length%3!=0)
                throw new InvalidDataException("Terrain payload must contain complete XYZ vertices.");
            Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Near);
            var points=new Vector3[payload.vertices.Length/3];
            for(int i=0;i<points.Length;i++)points[i]=new Vector3(payload.vertices[3*i],payload.vertices[3*i+1],payload.vertices[3*i+2]);
            // Validate final stored coordinates before clearing a working asset.
            // The upstream compiler explicitly records any zero-footprint
            // exclusion; an unreported collapsed consumer payload fails closed.
            var validated=TerrainFloat32HeightfieldPolicy.CorrectIndices(points,payload.triangles,false);
            mesh.Clear();mesh.indexFormat=points.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.vertices=points;mesh.triangles=validated.indices;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            SyncRepairedGround(mesh);
            AssetDatabase.SaveAssets();
            var ground=new Ground();
            if(System.Environment.GetEnvironmentVariable("QFL_ROAD_PREPARE_BRIDGES")=="1")
            {
                RepairBridgeEndpoints(ground,output);
                GradeSeparatedRoadV1Authoring.VisibleGroundOverride=ground;
                GradeSeparatedRoadV1Authoring.Author();
                EditorApplication.Exit(0);return;
            }
            GradeSeparatedRoadV1Authoring.VisibleGroundOverride=ground;
            var authority=new KbduGradeSeparatedRoadAuthorityV1(AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"GradeSeparatedRoadsV1/Data/KBDU_GradeSeparatedRoadAuthorityV1.json"));
            var routes=JsonUtility.FromJson<Routes>(File.ReadAllText(Manifest)).corridors;
            var raisedNear=new MeshSurface(AssetDatabase.LoadAssetAtPath<Mesh>(Root+"GradeSeparatedRoadsV1/Generated/KBDU_GradeSeparatedRoads_Near_V1.asset"),64,true);
            var raisedOuter=new MeshSurface(AssetDatabase.LoadAssetAtPath<Mesh>(Root+"GradeSeparatedRoadsV1/Generated/KBDU_GradeSeparatedRoads_Outer_V1.asset"),128,true);
            var bridgeByRoute=new Dictionary<string,List<BridgePath>>();
            foreach(var profile in authority.Data.profiles)
            {
                Route original=routes.FirstOrDefault(r=>r.corridor_id.Contains("way/"+profile.source_osm_way_id+":"));
                int n=profile.samples_xyz_meters.Length/3;
                var bridge=new BridgePath{profile=profile,p=new Vector2[n],distance=new float[n],twoWay=original!=null&&original.direction_mode=="BIDIRECTIONAL",laneCount=int.TryParse(profile.source_lanes,out int lc)?lc:2};
                for(int j=0;j<n;j++){bridge.p[j]=new Vector2(profile.samples_xyz_meters[j*3],profile.samples_xyz_meters[j*3+2]);if(j>0)bridge.distance[j]=bridge.distance[j-1]+Vector2.Distance(bridge.p[j-1],bridge.p[j]);}
                Mesh contact=GradeSeparatedRoadV1Authoring.BuildContactMesh(profile);
                bridge.surface=new MeshSurface(contact,64);UnityEngine.Object.DestroyImmediate(contact);
                foreach(string id in profile.upper_route_ids){if(!bridgeByRoute.TryGetValue(id,out var list))bridgeByRoute[id]=list=new List<BridgePath>();list.Add(bridge);}
            }
            Report report=new Report {corridors=routes.Length,nearTriangles=mesh.triangles.Length/3,sourceManifestSha256=Hash(Manifest),nearMeshSha256=Hash(Near)};
            var unresolved=new List<string>();var critical=new List<string>();
            using(var file=File.Create(Path.Combine(output,"lane_bank.pending")))using(var gzip=new GZipStream(file,System.IO.Compression.CompressionLevel.Optimal))using(var writer=new BinaryWriter(gzip))
            {
                writer.Write(TrafficLaneSurfaceBank.Magic);writer.Write(2);writer.Write(routes.Length);writer.Write(report.sourceManifestSha256);
                for(int ri=0;ri<routes.Length;ri++)
                {
                    Route r=routes[ri];bool two=r.direction_mode=="BIDIRECTIONAL";
                    int slots=Mathf.Max(1,two?r.lane_count/2:r.lane_count),total=slots*(two?2:1);
                    bridgeByRoute.TryGetValue(r.corridor_id,out var bridges);
                    if(bridges!=null)
                    {
                        int verifiedSlots=bridges.Min(b=>Mathf.Max(1,b.laneCount/(b.twoWay?2:1)));
                        slots=Mathf.Min(slots,verifiedSlots);total=slots*(two?2:1);
                    }
                    var authored=new List<List<Sample>>();float corridorError=0;Vector3 worstPosition=default;
                    report.laneCount+=total;
                    for(int lane=0;lane<total;lane++)
                    {
                        int direction=two?lane/slots:(r.direction_mode=="REVERSE"?1:0),li=lane%slots;
                        float lateral=two?(1.75f+li*3.5f)*(direction==0?1:-1):(li-(slots-1)*.5f)*3.5f*(direction==0?1:-1);
                        var path=OffsetPath(r.points_xz_meters,lateral);
                        // Bind once per lane/physical structure. Picking the nearest
                        // carriageway for each query jumps across a divided highway.
                        var laneBridges=bridges==null?null:bridges.GroupBy(b=>b.profile.source_cdot_structure_id)
                            .Select(g=>g.OrderBy(b=>BridgePathDistance(b,path,direction)).First()).ToList();
                        if(laneBridges!=null)
                        {
                            // Correct XY once, then follow a continuous polyline with
                            // its own arc length. Query-time nearest projections can jump.
                            var corrected=new List<Vector2>();
                            for(int j=0;j<path.Length-1;j++)
                            {
                                Vector2 delta=path[j+1]-path[j];int n=Mathf.Max(1,Mathf.CeilToInt(delta.magnitude/2f));
                                for(int k=0;k<n;k++)
                                {
                                    Vector2 q=Vector2.Lerp(path[j],path[j+1],k/(float)n);float unused=0;
                                    MapToBridge(laneBridges,ref q,delta.normalized,direction,li,ref unused);
                                    if(corrected.Count==0||Vector2.Distance(q,corrected[corrected.Count-1])>.001f)corrected.Add(q);
                                }
                            }
                            Vector2 last=path[path.Length-1];float h=0;
                            MapToBridge(laneBridges,ref last,(path[path.Length-1]-path[path.Length-2]).normalized,direction,li,ref h);
                            corrected.Add(last);path=corrected.ToArray();
                        }
                        var stations=new float[path.Length];
                        for(int j=1;j<path.Length;j++)stations[j]=stations[j-1]+Vector2.Distance(path[j-1],path[j]);
                        Vector2 DirectionAt(float d)
                        {
                            int lo=0,hi=stations.Length-1;while(lo+1<hi){int m=(lo+hi)/2;if(stations[m]<=d)lo=m;else hi=m;}
                            return (path[hi]-path[lo]).normalized;
                        }
                        float Support(Vector2 q,out Vector3 normal)
                        {
                            float h=ground.Height(q,out normal);
                            if(laneBridges!=null)foreach(var owned in laneBridges)
                                if(owned.surface.TrySample(q,out float road,out Vector3 roadUp)&&road>=h){h=road;normal=roadUp;}
                            return h;
                        }
                        var samples=new List<Sample>();float distance=0;var laneReport=new Report();
                        Sample At(Vector2 p,float d)
                        {
                            Vector2 raw=p;
                            float y=Support(p,out Vector3 up);
                            Vector2 forward=DirectionAt(d),right=new Vector2(forward.y,-forward.x);
                            const float halfTrack=1.275f;
                            float cross=(Support(p+right*halfTrack,out _)-Support(p-right*halfTrack,out _))/(2*halfTrack);
                            Vector2 gradient=new Vector2(-up.x,-up.z)/Mathf.Max(.01f,up.y);
                            gradient=forward*Vector2.Dot(gradient,forward)+right*cross;
                            up=new Vector3(-gradient.x,1,-gradient.y).normalized;
                            return new Sample{distance=d,p=new Vector3(p.x,y,p.y),up=up,raw=raw};
                        }
                        samples.Add(At(path[0],0));
                        for(int j=0;j<path.Length-1;j++)
                        {
                            Vector2 a=path[j],b=path[j+1];float length=Vector2.Distance(a,b);
                            int steps=Mathf.Max(1,Mathf.CeilToInt(length/(Mathf.Max(Mathf.Abs(a.x),Mathf.Abs(a.y))<2100?8f:40f)));
                            for(int k=1;k<=steps;k++)
                            {
                                Sample end=At(Vector2.Lerp(a,b,k/(float)steps),distance+length*k/steps);
                                Refine(samples,samples[samples.Count-1],end,At,0,laneReport);
                            }
                            distance+=length;
                        }
                        authored.Add(samples);if(laneReport.maximumBakeProbeError>corridorError){corridorError=laneReport.maximumBakeProbeError;worstPosition=laneReport.worstProbePosition;}
                    }
                    if(corridorError>.02f)
                    {
                        // Explicit, whole-route legacy fallback. No cars are hidden,
                        // and invalid newly baked support is never consumed.
                        writer.Write(0);unresolved.Add(r.corridor_id);
                        report.maximumRejectedProbeError=Mathf.Max(report.maximumRejectedProbeError,corridorError);
                        bool airport=authored.Any(l=>l.Any(s=>s.p.x>-1700&&s.p.x<-1300&&s.p.z>-700&&s.p.z<550));
                        if(airport||r.corridor_id.Contains("way/17022009:"))critical.Add(r.corridor_id+" at "+worstPosition+" error "+corridorError);
                    }
                    else
                    {
                        writer.Write(total);report.maximumBakeProbeError=Mathf.Max(report.maximumBakeProbeError,corridorError);
                        foreach(var laneSamples in authored)
                        {
                            writer.Write(laneSamples.Count);report.samples+=laneSamples.Count;
                            foreach(var s in laneSamples){writer.Write(s.distance);writer.Write(s.p.x);writer.Write(s.p.y);writer.Write(s.p.z);writer.Write(s.up.x);writer.Write(s.up.y);writer.Write(s.up.z);}
                        }
                    }
                    if(ri%2000==0)Debug.Log("[RoadRepair] baked corridors "+ri+"/"+routes.Length);
                }
            }
            report.unresolvedCorridors=unresolved.ToArray();
            File.WriteAllText(Path.Combine(output,"contact_exceptions.json"),JsonUtility.ToJson(report,true));
            if(critical.Count>0)throw new InvalidDataException("Critical airport contact failures: "+string.Join(",",critical));
            File.Copy(Path.Combine(output,"lane_bank.pending"),Bank,true);File.Delete(Path.Combine(output,"lane_bank.pending"));
            report.bankBytes=new FileInfo(Bank).Length;
            report.bankSha256=Hash(Bank);
            report.authoritySha256=Hash(Root+"GradeSeparatedRoadsV1/Data/KBDU_GradeSeparatedRoadAuthorityV1.json");
            report.nearRoadSha256=Hash(Root+"GradeSeparatedRoadsV1/Generated/KBDU_GradeSeparatedRoads_Near_V1.asset");
            report.outerRoadSha256=Hash(Root+"GradeSeparatedRoadsV1/Generated/KBDU_GradeSeparatedRoads_Outer_V1.asset");
            report.groundQuerySha256=Hash(Root+"ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");
            var parked=JsonUtility.FromJson<ParkedDocument>(File.ReadAllText(Path.Combine(output,"baseline/KBDU_TrafficManifestV1.json"))).parked;
            var poses=new List<ParkedPose>();
            var vehicleMeshes=AssetDatabase.LoadAssetAtPath<GameObject>(Traffic).GetComponent<KbduTrafficRuntime>().NearArchetypeMeshes;
            foreach(var car in parked)
            {
                Vector3 forward=Quaternion.Euler(0,car.yaw_degrees,0)*Vector3.forward;
                Vector2 p=new Vector2(car.x,car.z),f=new Vector2(forward.x,forward.z),right=new Vector2(f.y,-f.x);
                Bounds bounds=vehicleMeshes[car.archetype].bounds;
                Vector4 contact=WheelGeometry(vehicleMeshes[car.archetype]);
                float length=contact.x-contact.y,track=contact.z;
                float center=ground.Height(p,out _),front=ground.Height(p+f*contact.x,out _),rear=ground.Height(p+f*contact.y,out _);
                float left=ground.Height(p-right*track,out _),r=ground.Height(p+right*track,out _);
                Vector3 ahead=new Vector3(f.x*length,front-rear,f.y*length),side=new Vector3(right.x*track*2,r-left,right.y*track*2);
                Vector3 up=Vector3.Cross(ahead,side).normalized;Quaternion rotation=Quaternion.LookRotation(ahead,up);
                poses.Add(new ParkedPose{id=car.id,y=Mathf.Max(center-contact.w,Mathf.Max(Mathf.LerpUnclamped(rear,front,-contact.y/length),(left+r)*.5f))+authority.ContactOffsetMeters(car.archetype),pitch_degrees=rotation.eulerAngles.x,yaw_degrees=rotation.eulerAngles.y,roll_degrees=rotation.eulerAngles.z});
            }
            report.parkedCount=poses.Count;
            File.WriteAllText(Path.Combine(output,"parked_poses.json"),JsonUtility.ToJson(new PosePayload{poses=poses.ToArray()}));
            AssetDatabase.ImportAsset(Bank,ImportAssetOptions.ForceSynchronousImport);
            GameObject root=PrefabUtility.LoadPrefabContents(Traffic);
            try
            {
                var runtime=root.GetComponent<KbduTrafficV2Runtime>();var so=new SerializedObject(runtime);
                so.FindProperty("laneSurfaceBank").objectReferenceValue=AssetDatabase.LoadAssetAtPath<TextAsset>(Bank);
                var geometry=so.FindProperty("vehicleContactGeometry");geometry.arraySize=vehicleMeshes.Length;
                for(int i=0;i<vehicleMeshes.Length;i++)geometry.GetArrayElementAtIndex(i).vector4Value=WheelGeometry(vehicleMeshes[i]);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root,Traffic);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
            File.WriteAllText(Root+"TrafficV2/Data/KBDU_VisibleRoadLaneSurfaces_Source.json",JsonUtility.ToJson(report,true));
            File.WriteAllText(Path.Combine(output,"authoring_report.json"),JsonUtility.ToJson(report,true));
            Debug.Log("[RoadRepair] complete "+JsonUtility.ToJson(report));
            EditorApplication.Exit(0);
        }

        static void MapToBridge(List<BridgePath> bridges,ref Vector2 point,Vector2 forward,int direction,int lane,ref float height)
        {
            Vector2 original=point, displacement=Vector2.zero;
            float totalWeight=0,bestWeight=0;
            foreach(var selected in bridges)
            {
                float nearest=float.PositiveInfinity,station=0;
                Vector2 center=default,tangent=default;
                for(int j=0;j<selected.p.Length-1;j++)
                {
                    Vector2 a=selected.p[j],d=selected.p[j+1]-a;float len=d.magnitude;if(len<.001f)continue;
                    float ratio=Mathf.Clamp01(Vector2.Dot(original-a,d)/(len*len));
                    Vector2 q=a+d*ratio;float separation=Vector2.Distance(q,original);
                    if(separation>=nearest)continue;
                    nearest=separation;center=q;
                    Vector2 before=j>0?(selected.p[j]-selected.p[j-1]).normalized:d/len;
                    Vector2 after=j+2<selected.p.Length?(selected.p[j+2]-selected.p[j+1]).normalized:d/len;
                    tangent=Vector2.Lerp((before+d/len).normalized,(d/len+after).normalized,ratio).normalized;
                    station=selected.distance[j]+ratio*len;
                }
                Vector2 travel=forward*(direction==0?1:-1);
                if(Vector2.Dot(tangent,travel)<0)tangent=-tangent;
                float side=selected.twoWay?1.75f+lane*3.5f:(lane-(selected.laneCount-1)*.5f)*3.5f;
                Vector2 target=center+new Vector2(tangent.y,-tangent.x)*side;
                float extent=selected.distance[selected.distance.Length-1];
                float weight=Mathf.SmoothStep(0,1,Mathf.Min(station,extent-station)/60f);
                weight*=Mathf.SmoothStep(0,1,(40f-nearest)/20f);
                weight*=Mathf.SmoothStep(0,1,(Mathf.Abs(Vector2.Dot(forward,tangent))-.5f)/.35f);
                float deckSeparation=Mathf.Max(selected.profile.bridge_interval_meters[0]-station,station-selected.profile.bridge_interval_meters[1]);
                weight*=1+99*(1-Mathf.SmoothStep(0,1,Mathf.Max(0,deckSeparation)/60f));
                displacement+=(target-original)*weight;totalWeight+=weight;
                if(weight>bestWeight)
                {
                    bestWeight=weight;
                    KbduGradeSeparatedRoadAuthorityV1.TryEvaluateProfile(selected.profile,station,out Vector3 surface,out _);
                    height=surface.y;
                }
            }
            point=original+displacement/Mathf.Max(1,totalWeight);
        }

        static float BridgePathDistance(BridgePath bridge,Vector2[] path,int direction)
        {
            KbduGradeSeparatedRoadAuthorityV1.TryEvaluateProfile(bridge.profile,(bridge.profile.bridge_interval_meters[0]+bridge.profile.bridge_interval_meters[1])*.5f,out Vector3 c,out _);
            Vector2 point=new Vector2(c.x,c.z);float nearest=float.PositiveInfinity;
            for(int i=0;i<path.Length-1;i++)
            {
                Vector2 d=path[i+1]-path[i];float t=Mathf.Clamp01(Vector2.Dot(point-path[i],d)/Mathf.Max(.00001f,d.sqrMagnitude));
                nearest=Mathf.Min(nearest,Vector2.Distance(point,path[i]+d*t));
            }
            if(!bridge.twoWay)
            {
                Vector2 travel=(path[path.Length-1]-path[0]).normalized*(direction==0?1:-1);
                Vector2 bridgeTravel=(bridge.p[bridge.p.Length-1]-bridge.p[0]).normalized;
                if(Vector2.Dot(travel,bridgeTravel)<0)nearest+=1000;
            }
            return nearest;
        }

        static void Refine(List<Sample> result,Sample a,Sample b,Func<Vector2,float,Sample> at,int depth,Report report)
        {
            float worst=0; Sample mid=default;
            for(int k=1;k<=3;k++)
            {
                float t=k*.25f;Vector3 p=Vector3.Lerp(a.p,b.p,t);Sample s=at(Vector2.Lerp(a.raw,b.raw,t),Mathf.Lerp(a.distance,b.distance,t));
                worst=Mathf.Max(worst,Vector3.Distance(s.p,p));if(k==2)mid=s;
            }
            if(worst>.012f && depth<12 && b.distance-a.distance>.02f)
            {Refine(result,a,mid,at,depth+1,report);Refine(result,mid,b,at,depth+1,report);return;}
            if(worst>report.maximumBakeProbeError){report.maximumBakeProbeError=worst;report.worstProbePosition=mid.p;}result.Add(b);
        }
        static void RepairBridgeEndpoints(Ground ground,string output)
        {
            string path=Root+"GradeSeparatedRoadsV1/Data/KBDU_GradeSeparatedRoadAuthorityV1.json";
            string json=File.ReadAllText(Path.Combine(output,"authority_input.json"));
            var doc=JsonUtility.FromJson<KbduGradeSeparatedRoadAuthorityV1.Document>(json);
            foreach(var p in doc.profiles)
            {
                float[] xyz=p.samples_xyz_meters;int n=xyz.Length/3;
                float first=ground.Height(new Vector2(xyz[0],xyz[2]),out _)-xyz[1];
                float last=ground.Height(new Vector2(xyz[xyz.Length-3],xyz[xyz.Length-1]),out _)-xyz[xyz.Length-2];
                float distance=0;p.maximum_grade=0;
                for(int i=0;i<n;i++)
                {
                    if(i>0)distance+=Vector2.Distance(new Vector2(xyz[i*3],xyz[i*3+2]),new Vector2(xyz[i*3-3],xyz[i*3-1]));
                    float correction=distance<p.bridge_interval_meters[0]?first*(1-Mathf.SmoothStep(0,1,distance/p.bridge_interval_meters[0])):
                        distance>p.bridge_interval_meters[1]?last*Mathf.SmoothStep(0,1,(distance-p.bridge_interval_meters[1])/(p.profile_length_meters-p.bridge_interval_meters[1])):0;
                    xyz[i*3+1]+=correction;
                }
                for(int i=0;i<n;i++)
                {
                    int a=Mathf.Max(0,i-1)*3,b=Mathf.Min(n-1,i+1)*3;Vector3 f=new Vector3(xyz[b]-xyz[a],xyz[b+1]-xyz[a+1],xyz[b+2]-xyz[a+2]);
                    Vector3 up=Vector3.ProjectOnPlane(Vector3.up,f.normalized).normalized;
                    p.sample_normals_xyz[i*3]=up.x;p.sample_normals_xyz[i*3+1]=up.y;p.sample_normals_xyz[i*3+2]=up.z;
                    p.maximum_grade=Mathf.Max(p.maximum_grade,Mathf.Abs(f.y)/Mathf.Max(.001f,new Vector2(f.x,f.z).magnitude));
                }
                if(p.maximum_grade>.15f)throw new InvalidDataException("Bridge approach grade exceeds bound: "+p.span_id);
            }
            string Array(float[] x)=>"["+string.Join(",",x.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)))+"]";
            int ix=0;json=Regex.Replace(json,"\"samples_xyz_meters\"\\s*:\\s*\\[[^\\]]*\\]",m=>"\"samples_xyz_meters\":"+Array(doc.profiles[ix++].samples_xyz_meters));
            ix=0;json=Regex.Replace(json,"\"sample_normals_xyz\"\\s*:\\s*\\[[^\\]]*\\]",m=>"\"sample_normals_xyz\":"+Array(doc.profiles[ix++].sample_normals_xyz));
            ix=0;json=Regex.Replace(json,"\"maximum_grade\"\\s*:\\s*[0-9.eE+-]+",m=>"\"maximum_grade\":"+doc.profiles[ix++].maximum_grade.ToString("R",CultureInfo.InvariantCulture));
            File.WriteAllText(path,json);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        }

        static Vector4 WheelGeometry(Mesh mesh)
        {
            Vector3[] v=mesh.vertices;Color32[] colors=mesh.colors32;
            var tires=Enumerable.Range(0,v.Length).Where(i=>(colors[i].b>200&&colors[i].r<30&&colors[i].g<30)||(colors[i].r>200&&colors[i].g>200&&colors[i].b<30)).ToArray();
            if(tires.Length==0)throw new InvalidDataException("No authored tire semantics on "+mesh.name);
            float minimum=tires.Min(i=>v[i].y);
            var contact=tires.Where(i=>v[i].y<minimum+.035f).Select(i=>v[i]).ToArray();
            float rear=contact.Min(p=>p.z),front=contact.Max(p=>p.z),track=contact.Max(p=>Mathf.Abs(p.x));
            float body=Enumerable.Range(0,v.Length).Where(i=>colors[i].r>200&&colors[i].g<30&&colors[i].b<30).Min(i=>v[i].y);
            if(front-rear<1||track<.3f)throw new InvalidDataException("Invalid wheel geometry on "+mesh.name);
            return new Vector4(front,rear,track,Mathf.Max(0,body-.03f));
        }

        static void SyncRepairedGround(Mesh mesh)
        {
            var asset=AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(Root+"ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");
            var indices=mesh.triangles;var source=mesh.vertices;var normals=mesh.normals;
            var v=new List<Vector3>();var n=new List<Vector3>();var t=new List<int>();var remap=new Dictionary<int,int>();
            for(int i=0;i<indices.Length;i+=3)
            {
                // Original immutable source has 46,426 vertices. A new vertex
                // identifies a locally repaired face; unrelated ground is untouched.
                if(indices[i]<46426&&indices[i+1]<46426&&indices[i+2]<46426)continue;
                for(int j=0;j<3;j++)
                {
                    int old=indices[i+j];if(!remap.TryGetValue(old,out int id)){id=v.Count;remap[old]=id;v.Add(source[old]);n.Add(normals[old]);}
                    t.Add(id);
                }
            }
            var patches=asset.transitionPatches.Where(p=>p.stableId!="airport_road_repair_visible_triangles").ToList();
            patches.Add(new ProductionTerrainSurfaceAsset.TrianglePatch{stableId="airport_road_repair_visible_triangles",zone=ProductionTerrainZone.Near,
                priority=-100,innerHalfExtentMeters=0,outerHalfExtentMeters=2048,vertices=v.ToArray(),normals=n.ToArray(),triangles=t.ToArray()});
            asset.transitionPatches=patches.ToArray();EditorUtility.SetDirty(asset);
        }

        public static Vector2[] OffsetPath(float[] points,float lateral)
        {
            int n=points.Length/2;var path=new Vector2[n];
            for(int i=0;i<n;i++)
            {
                Vector2 p=new Vector2(points[i*2],points[i*2+1]);
                Vector2 a=i>0?(p-new Vector2(points[i*2-2],points[i*2-1])).normalized:(new Vector2(points[2],points[3])-p).normalized;
                Vector2 b=i<n-1?(new Vector2(points[i*2+2],points[i*2+3])-p).normalized:a;
                Vector2 rightA=new Vector2(a.y,-a.x),rightB=new Vector2(b.y,-b.x),m=(rightA+rightB).normalized;
                if(m.sqrMagnitude<.5f)m=rightA;
                path[i]=p+m*(lateral/Mathf.Max(.5f,Vector2.Dot(m,rightA)));
                // The source universe is clipped to the same square. Lane offsets
                // at those network endpoints must not request nonexistent terrain.
                path[i]=new Vector2(Mathf.Clamp(path[i].x,-11999.99f,11999.99f),Mathf.Clamp(path[i].y,-11999.99f,11999.99f));
            }
            return path;
        }
        static string Hash(string p){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))).Replace("-","").ToLowerInvariant();}
    }
}
