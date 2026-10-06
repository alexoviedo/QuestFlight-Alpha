using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    // Authored renderer only. Streams immutable records, never creates scene objects.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public sealed partial class MapBranchGroveRenderer : MonoBehaviour
    {
        public const int MaximumTriangleCount=393216, MaximumCells=MaximumTriangleCount/4;
        public const int CellsPerPage=252, PageWindow=7, MaximumPages=PageWindow*PageWindow;
        private const int OwnershipSize=CellsPerPage*PageWindow;
        public const float CellMeters=8f/3f, FullRadius=1600, OuterRadius=1800;
        public const float NearFadeStart=520, NearTierEnd=720, MiddleFadeStart=1000, MiddleTierEnd=1250;
        public int SelectedTriangles {get;private set;}
        public int NearCells=>tierCounts[0];
        public int MiddleCells=>tierCounts[1];
        public int FarCells=>tierCounts[2];
        public float FarthestSelectedMeters {get;private set;}
        public double SelectionMilliseconds {get;private set;}
        // Cumulative CPU-side timings let the observer take allocation-free deltas.
        // Upload timings include driver waits; they are not GPU execution durations.
        public long SelectionCount {get;private set;}
        public double GatherTotalMs {get;private set;}
        public double FillTotalMs {get;private set;}
        public double BufferUploadTotalMs {get;private set;}
        public double OwnershipUploadTotalMs {get;private set;}
        public long UploadedBytes {get;private set;}
        private static double ElapsedMs(long start) =>
            (System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        private static readonly int[] TriangleCosts={12,6,4};
        public AircraftCenteredBuildingClipmapController buildingClipmap;
        public Camera viewingCamera;
        public Mesh branchMesh;
        public Material branchMaterial;
        public TextAsset sourceManifest;
        public QuestFlightLab.Flight.Backends.FlightDynamicsInitialConditionProvider[] observationStarts;
        public string pageDirectory="QuestFlightBranchGroves";
        public int SelectedCells {get;private set;}
        public int EligibleCells {get;private set;}
        public int LastDraws {get;private set;}
        public int ResidentPages=>pages.Count;
        public int LoadFailures {get;private set;}
        public bool Ready=>valid&&SelectedCells>0&&buffer!=null;
        [Serializable] public sealed class Record {public int column,row,count,bytes;public string file,sha256;}
        [Serializable] public sealed class Manifest {public string schema;public bool complete;public Record[] pages;}
        private sealed class Block {public int[] indices;public Bounds bounds;}
        private struct Cell {public int cell,family,proxyMember;public Vector3 center;public float radius,ground,height;public Vector3 tint;public uint seed,grove;public float sine,cosine;}
        private sealed class Page {public Cell[] cells;public uint[] selectedEpoch;public Block[] blocks;public ProxyGroup[] proxies;public float loaded,selectionFade;public int ownershipX,ownershipZ;}
        private struct GpuCell {public Vector4 positionHeight,tintRadius,rotationFade;}
        private struct Candidate {public Page page;public int index;public float distance;public bool retained;}
        private readonly Dictionary<int,Page> pages=new Dictionary<int,Page>();
        private readonly Dictionary<int,Record> records=new Dictionary<int,Record>();
        private readonly List<int> desired=new List<int>(MaximumPages),remove=new List<int>(MaximumPages);
        private readonly List<Candidate>[] candidates={new List<Candidate>(32768),new List<Candidate>(65536),new List<Candidate>(131072)};
        private readonly int[] tierCounts=new int[3];
        private const float DistanceBinMeters=16;
        private readonly int[][] distanceBins={new int[113],new int[113],new int[113]};
        // Admission is view-dependent; an already submitted cell is not. Retain
        // overlapping cells before spending spare budget on newly revealed ones.
        private uint selectionEpoch=1;
        private readonly int[] retainedCounts=new int[3],admissionCounts=new int[3];
        private readonly GpuCell[] gpu=new GpuCell[MaximumCells];
        private readonly uint[] gpuSeeds=new uint[MaximumCells];
        private int cachedGpuCells;
        private readonly Plane[] leftPlanes=new Plane[6],rightPlanes=new Plane[6];
        private readonly byte[] ownership=new byte[OwnershipSize*OwnershipSize];
        private readonly byte[] ownershipMarks=new byte[OwnershipSize*OwnershipSize];
        private int[] previousOwnershipIndices=new int[MaximumCells],currentOwnershipIndices=new int[MaximumCells];
        private int previousOwnershipCount;
        private byte ownershipMark;
        private bool ownershipUploaded,pageFadesActive;
        private Matrix4x4 previousProjection;
        private Texture2D ownershipTexture;
        private MaterialPropertyBlock properties;
        private ComputeBuffer buffer;
        private Bounds drawBounds;
        private bool valid,dirty=true;
        private Vector3 previousPosition;
        private Quaternion previousRotation;
        private float nextUpdate;
        private float diagnosticUntil;
        private System.Threading.Tasks.Task<Page> pendingPageDecode;
        public void BeginVisibilityDiagnostic(){diagnosticUntil=Time.unscaledTime+25;properties?.SetFloat("_Diagnostic",1);}
        private int gridX,gridZ;
        private static int Key(int x,int z)=>z*36+x;
        private void Awake(){Initialize();}
        private void Initialize()
        {
            if(valid)return;
            if(branchMesh==null||branchMesh.subMeshCount!=3||branchMaterial==null||sourceManifest==null||buildingClipmap==null)
                throw new InvalidOperationException("Map branch grove authored references missing");
            var manifest=JsonUtility.FromJson<Manifest>(sourceManifest.text);
            if(manifest==null||manifest.schema!="questflight.map-branch-groves.v1"||!manifest.complete||manifest.pages==null||manifest.pages.Length!=1296)
                throw new InvalidDataException("Map branch grove manifest incomplete");
            records.Clear();
            foreach(var r in manifest.pages)
            {
                if(r.column<0||r.column>=36||r.row<0||r.row>=36||r.count<0||r.count>63504||r.bytes!=16+20*r.count||
                   r.file!=$"branches_{r.column:00}_{r.row:00}.bytes"||r.sha256==null||r.sha256.Length!=64)
                    throw new InvalidDataException("Invalid branch page record");
                records.Add(Key(r.column,r.row),r);
            }
            properties=new MaterialPropertyBlock();
            buffer=new ComputeBuffer(MaximumCells,48,ComputeBufferType.Structured);
            properties.SetBuffer("_BranchCells",buffer);
            properties.SetVector("_GroveLodRanges",new Vector4(NearFadeStart,NearTierEnd,MiddleFadeStart,MiddleTierEnd));
            ownershipTexture=new Texture2D(OwnershipSize,OwnershipSize,TextureFormat.R8,false,true){name="MapGrove_RuntimeOwnership",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            valid=true;
        }
        private void OnEnable()
        {
            if(Application.isPlaying)
            {
                Initialize();StartCoroutine(Stream());
                Camera.onPreCull-=DrawForCamera;
                QuestFlight.Rendering.ProductRenderSubmission.Draw -= DrawForCamera;
                Camera.onPreCull+=DrawForCamera;
                QuestFlight.Rendering.ProductRenderSubmission.Draw += DrawForCamera;
            }
        }
        private void Desired(Vector3 p)
        {
            int x=Mathf.Clamp(Mathf.FloorToInt((p.x+12096)/672),3,32),z=Mathf.Clamp(Mathf.FloorToInt((p.z+12096)/672),3,32);
            if(desired.Count>0&&gridX==x-3&&gridZ==z-3)return;
            gridX=x-3;gridZ=z-3;desired.Clear();
            for(int j=-3;j<=3;j++)for(int i=-3;i<=3;i++)desired.Add(Key(x+i,z+j));
            // Load nearby pages first; retain the whole bounded window for head turns.
            // Residency is not submission: the draw stage rejects out-of-view cells.
            desired.Sort((a,b)=>PageDistanceSquared(a,p).CompareTo(PageDistanceSquared(b,p)));
        }
        private static float PageDistanceSquared(int key,Vector3 p)
        {
            float x=-12096+(key%36+.5f)*672-p.x,z=-12096+(key/36+.5f)*672-p.z;return x*x+z*z;
        }

        private IEnumerator Stream()
        {
            // A disabled coroutine can leave one pure-data decode finishing.
            // Wait for it before starting another, bounding worker residency.
            if(pendingPageDecode!=null)
            {
                while(!pendingPageDecode.IsCompleted)yield return null;
                _=pendingPageDecode.Exception;pendingPageDecode=null;
            }
            while(enabled&&valid)
            {
                if(viewingCamera!=null)
                {
                    Desired(viewingCamera.transform.position);remove.Clear();
                    foreach(int k in pages.Keys)if(!desired.Contains(k))remove.Add(k);
                    foreach(int k in remove){pages.Remove(k);dirty=true;}
                    // Selection refreshes desired while requests are suspended. Never
                    // retain an enumerator over that shared list across a yield.
                    int[] loadingWindow=desired.ToArray();
                    foreach(int key in loadingWindow)
                    {
                        if(pages.ContainsKey(key))continue;
                        var r=records[key];string path=Path.Combine(Application.streamingAssetsPath,pageDirectory,r.file).Replace('\\','/');
                        using(var request=UnityWebRequest.Get(path.Contains("://")?path:new Uri(path).AbsoluteUri))
                        {
                            yield return request.SendWebRequest();
                            if(viewingCamera==null)break;
                            Desired(viewingCamera.transform.position);
                            if(!desired.Contains(key))continue; // Aircraft crossed into another window.
                            if(request.result!=UnityWebRequest.Result.Success){LoadFailures++;Debug.LogError("[MapBranchGroves] Load failed "+r.file);continue;}
                            // Hash validation, record decoding and cluster fitting
                            // touch plain data only. Never block a rendered frame
                            // on a dense page, and never create Unity objects here.
                            byte[] bytes=request.downloadHandler.data;
                            pendingPageDecode=System.Threading.Tasks.Task.Run(()=>Decode(bytes,r,0));
                            while(!pendingPageDecode.IsCompleted)yield return null;
                            var completed=pendingPageDecode;pendingPageDecode=null;
                            if(completed.IsFaulted)
                            {LoadFailures++;Debug.LogError("[MapBranchGroves] Rejected "+r.file+": "+completed.Exception.GetBaseException().Message);continue;}
                            if(viewingCamera==null)break;
                            Desired(viewingCamera.transform.position);
                            if(!desired.Contains(key))continue;
                            var decoded=completed.Result;decoded.loaded=Time.unscaledTime;
                            pages[key]=decoded;dirty=true;
                        }
                    }
                }
                yield return new WaitForSecondsRealtime(.25f);
            }
        }
        private static Page Decode(byte[] data,Record r,float loaded)
        {
            if(data.Length!=r.bytes||System.Text.Encoding.ASCII.GetString(data,0,4)!="QFBG"||BitConverter.ToInt32(data,4)!=1||BitConverter.ToInt32(data,8)!=r.column||BitConverter.ToInt32(data,12)!=r.row)
                throw new InvalidDataException("Branch header mismatch");
            using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant()!=r.sha256)throw new InvalidDataException("Branch hash mismatch");
            var p=new Page{cells=new Cell[r.count],selectedEpoch=new uint[r.count],loaded=loaded};int previous=-1;
            var buckets=new List<int>[441];var minY=new float[441];var maxY=new float[441];
            for(int offset=16;offset<data.Length;offset+=20)
            {
                int cell=BitConverter.ToUInt16(data,offset),radius=BitConverter.ToUInt16(data,offset+2);
                float ground=BitConverter.ToSingle(data,offset+4),height=BitConverter.ToSingle(data,offset+8);
                if(cell<=previous||cell>=63504||radius<1||radius>225||!float.IsFinite(ground)||!float.IsFinite(height)||height<4||height>40||BitConverter.ToUInt32(data,offset+16)==0)
                    throw new InvalidDataException("Branch payload invalid");
                int block=(cell/252/12)*21+(cell%252/12);
                if(buckets[block]==null){buckets[block]=new List<int>(144);minY[block]=ground;maxY[block]=ground+height;}
                int index=(offset-16)/20;
                uint seed=unchecked((uint)(Key(r.column,r.row)*63504+cell)*1664525u+1013904223u);
                int family=(data[offset+15]>>1)&3;
                if(family>2)throw new InvalidDataException("Unknown tree visual family");
                Vector3 tint=new Vector3(data[offset+12]/255f,data[offset+13]/255f,data[offset+14]/255f);
                if(family>0)tint=Vector3.Lerp(tint,new Vector3(.25f,.34f,.21f),.55f);
                p.cells[index]=new Cell{cell=cell,family=family,grove=BitConverter.ToUInt32(data,offset+16),center=new Vector3(-12096+r.column*672+(cell%252+.5f)*CellMeters,ground+height*.75f,-12096+r.row*672+(cell/252+.5f)*CellMeters),ground=ground,height=height,radius=radius*.01f,tint=tint,seed=seed,sine=Mathf.Sin((seed&65535)*(Mathf.PI*2/65536)),cosine=Mathf.Cos((seed&65535)*(Mathf.PI*2/65536))};
                buckets[block].Add(index);minY[block]=Mathf.Min(minY[block],ground);maxY[block]=Mathf.Max(maxY[block],ground+height);previous=cell;
            }
            var blocks=new List<Block>(441);
            for(int i=0;i<441;i++)if(buckets[i]!=null)
                blocks.Add(new Block{indices=buckets[i].ToArray(),bounds=new Bounds(new Vector3(-12096+r.column*672+(i%21+.5f)*32,(minY[i]+maxY[i])*.5f,-12096+r.row*672+(i/21+.5f)*32),new Vector3(60,maxY[i]-minY[i]+32,60))});
            p.blocks=blocks.ToArray();
            BuildProxyGroups(p);
            return p;
        }
        public static float DistanceCoverage(float distance)=>distance<=FullRadius?1:distance>=OuterRadius?0:1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(FullRadius,OuterRadius,distance));
        private static bool EntirelyInside(Plane[] planes,Bounds bounds)
        {
            var e=bounds.extents;
            foreach(var p in planes)
            {
                var n=p.normal;float radius=Mathf.Abs(n.x)*e.x+Mathf.Abs(n.y)*e.y+Mathf.Abs(n.z)*e.z;
                if(p.GetDistanceToPoint(bounds.center)<radius)return false;
            }
            return true;
        }
        public static int DistanceTier(float distance)=>distance<NearTierEnd?0:distance<MiddleTierEnd?1:2;
        public static void AllocateTierCounts(int near,int middle,int far,int[] counts)
        {
            // Reserve one quarter for distant coverage in a crowded view. Unused
            // reservation goes back to closer tiers. Never exceed the old hard cap.
            int farReserve=Math.Min(far,MaximumTriangleCount/16)*4;
            int remaining=MaximumTriangleCount-farReserve;
            counts[0]=Math.Min(near,remaining/12);remaining-=counts[0]*12;
            counts[1]=Math.Min(middle,remaining/6);remaining-=counts[1]*6;
            counts[2]=Math.Min(far,(remaining+farReserve)/4);
        }
        private void Select(Camera camera,bool settled)
        {
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            Vector3 position=camera.transform.position;Desired(position);
            Matrix4x4 left=camera.projectionMatrix*camera.worldToCameraMatrix,right=left;
            if(camera.stereoEnabled){left=camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)*camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);right=camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right)*camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);}
            // Guard both eyes against the selection threshold and late XR pose
            // prediction. Offscreen residency never consumes a draw by itself.
            left=Matrix4x4.Scale(new Vector3(.85f,.85f,1))*left;right=Matrix4x4.Scale(new Vector3(.85f,.85f,1))*right;
            GeometryUtility.CalculateFrustumPlanes(left,leftPlanes);GeometryUtility.CalculateFrustumPlanes(right,rightPlanes);
            foreach(var list in candidates)list.Clear();foreach(var bins in distanceBins)Array.Clear(bins,0,bins.Length);Array.Clear(retainedCounts,0,3);
            uint previousEpoch=selectionEpoch;
            if(++selectionEpoch==0)
            {
                foreach(var p in pages.Values)Array.Clear(p.selectedEpoch,0,p.selectedEpoch.Length);
                selectionEpoch=1;previousEpoch=uint.MaxValue;
            }
            if(++ownershipMark==0){Array.Clear(ownershipMarks,0,ownershipMarks.Length);ownershipMark=1;}
            bool ownershipChanged=!ownershipUploaded;pageFadesActive=false;
            BeginProxySelection(camera);
            float minY=float.MaxValue,maxY=float.MinValue;
            foreach(int key in desired)
            {
                if(!pages.TryGetValue(key,out var page))continue;
                // Identical values for every cell on this page during this selection.
                page.selectionFade=settled?1:Mathf.SmoothStep(0,1,(Time.unscaledTime-page.loaded)/1.5f);
                pageFadesActive|=page.selectionFade<1;
                page.ownershipX=(key%36-gridX)*252;page.ownershipZ=(key/36-gridZ)*252;
                foreach(var block in page.blocks)
                {
                    if(block.bounds.SqrDistance(position)>OuterRadius*OuterRadius||
                        !GeometryUtility.TestPlanesAABB(leftPlanes,block.bounds)&&!GeometryUtility.TestPlanesAABB(rightPlanes,block.bounds))continue;
                    bool fullyVisible=EntirelyInside(leftPlanes,block.bounds)||EntirelyInside(rightPlanes,block.bounds);
                    foreach(int index in block.indices)
                    {
                        ref readonly var cell=ref page.cells[index];float d=Vector3.Distance(position,cell.center);
                        if(d>=OuterRadius)continue;
                        if(!fullyVisible)
                        {
                            var bounds=new Bounds(cell.center,new Vector3(28,cell.height*.7f+24,28));
                            if(!GeometryUtility.TestPlanesAABB(leftPlanes,bounds)&&!GeometryUtility.TestPlanesAABB(rightPlanes,bounds))continue;
                        }
                        int tier=DistanceTier(d);bool retained=page.selectedEpoch[index]==previousEpoch;
                        if(retained)retainedCounts[tier]++;
                        else distanceBins[tier][(int)(d/DistanceBinMeters)]++;
                        candidates[tier].Add(new Candidate{page=page,index=index,distance=d,retained=retained});
                    }
                }
            }
            GatherTotalMs+=ElapsedMs(started);
            long phaseStarted=System.Diagnostics.Stopwatch.GetTimestamp();
            // Head turns cannot promote distance tiers, so all retained cells fit
            // their previous hard cap. Translation can change tier costs; apply
            // the same bounded allocation to that case, before admitting new cells.
            AllocateTierCounts(retainedCounts[0],retainedCounts[1],retainedCounts[2],tierCounts);
            int remaining=MaximumTriangleCount;
            for(int tier=0;tier<3;tier++)remaining-=tierCounts[tier]*TriangleCosts[tier];
            int farDemand=candidates[2].Count-retainedCounts[2];
            int reserve=Math.Min(farDemand,Math.Max(0,MaximumTriangleCount/16-tierCounts[2]))*4;
            reserve=Math.Min(reserve,remaining);remaining-=reserve;
            admissionCounts[0]=Math.Min(candidates[0].Count-retainedCounts[0],remaining/12);remaining-=admissionCounts[0]*12;
            admissionCounts[1]=Math.Min(candidates[1].Count-retainedCounts[1],remaining/6);remaining-=admissionCounts[1]*6;
            admissionCounts[2]=Math.Min(farDemand,(remaining+reserve)/4);
            EligibleCells=SelectedCells=SelectedTriangles=0;FarthestSelectedMeters=0;
            int firstChanged=MaximumCells,lastChanged=-1;
            // A diagnostic representation switch must overwrite a previously
            // compacted buffer even when every source record is unchanged.
            if(!proxiesActive&&previousProxySubmission){cachedGpuCells=0;firstChanged=0;lastChanged=MaximumCells-1;}
            for(int tier=0;tier<3;tier++)
            {
                EligibleCells+=candidates[tier].Count;
                int retainedRemaining=tierCounts[tier];
                int lowerCount=0,cutoff=0;
                while(cutoff<112&&lowerCount+distanceBins[tier][cutoff]<admissionCounts[tier])lowerCount+=distanceBins[tier][cutoff++];
                int boundaryRemaining=admissionCounts[tier]-lowerCount;
                int admitted=0,emitted=0;
                for(int i=0;i<candidates[tier].Count;i++)
                {
                    if(retainedRemaining==0&&admitted>=admissionCounts[tier])break;
                    var c=candidates[tier][i];
                    if(c.retained)
                    {
                        if(retainedRemaining<=0)continue;
                        retainedRemaining--;
                    }
                    else
                    {
                        if(admitted>=admissionCounts[tier])continue;
                        int bin=(int)(c.distance/DistanceBinMeters);
                        if(bin>cutoff||bin==cutoff&&boundaryRemaining<=0)continue;
                        if(bin==cutoff)boundaryRemaining--;
                        admitted++;
                    }
                    emitted++;var page=c.page;ref readonly var cell=ref page.cells[c.index];
                    // Never fade coverage against a view-dependent budget edge:
                    // it changes every time another forest enters the viewport.
                    float fade=DistanceCoverage(c.distance)*page.selectionFade;
                    // Authored page records are immutable and the seeded cell ID
                    // is unique map-wide. Only its selection fade can change.
                    if(SelectedCells>=cachedGpuCells||gpuSeeds[SelectedCells]!=cell.seed||gpu[SelectedCells].rotationFade.z!=fade)
                    {
                        firstChanged=Math.Min(firstChanged,SelectedCells);lastChanged=SelectedCells;
                        gpuSeeds[SelectedCells]=cell.seed;
                        gpu[SelectedCells]=new GpuCell{positionHeight=new Vector4(cell.center.x,cell.ground,cell.center.z,cell.height),tintRadius=new Vector4(cell.tint.x,cell.tint.y,cell.tint.z,cell.radius),rotationFade=new Vector4(cell.sine,cell.cosine,fade,cell.seed%16+cell.family*16)};
                    }
                    page.selectedEpoch[c.index]=selectionEpoch;
                    RecordProxyChild(page,c.index,SelectedCells,c.distance,fade);
                    int tx=page.ownershipX+cell.cell%252,tz=page.ownershipZ+cell.cell/252;
                    int pixel=tz*OwnershipSize+tx;byte coverage=(byte)Mathf.RoundToInt(fade*255);
                    currentOwnershipIndices[SelectedCells++]=pixel;ownershipMarks[pixel]=ownershipMark;
                    if(ownership[pixel]!=coverage){ownership[pixel]=coverage;ownershipChanged=true;}
                    minY=Mathf.Min(minY,cell.ground);maxY=Mathf.Max(maxY,cell.ground+cell.height+3);
                    FarthestSelectedMeters=Mathf.Max(FarthestSelectedMeters,c.distance);
                }
                tierCounts[tier]=emitted;SelectedTriangles+=emitted*TriangleCosts[tier];
            }
            for(int i=0;i<previousOwnershipCount;i++)
            {
                int pixel=previousOwnershipIndices[i];
                if(ownershipMarks[pixel]!=ownershipMark&&ownership[pixel]!=0){ownership[pixel]=0;ownershipChanged=true;}
            }
            var oldIndices=previousOwnershipIndices;previousOwnershipIndices=currentOwnershipIndices;currentOwnershipIndices=oldIndices;previousOwnershipCount=SelectedCells;
            cachedGpuCells=Math.Max(cachedGpuCells,SelectedCells);
            SelectProxies(camera);
            FillTotalMs+=ElapsedMs(phaseStarted);
            phaseStarted=System.Diagnostics.Stopwatch.GetTimestamp();
            if(SelectedCells>0)
            {
                if(proxiesActive)
                {
                    if(SubmittedBranchCells>0){buffer.SetData(submission,0,0,SubmittedBranchCells);UploadedBytes+=(long)SubmittedBranchCells*48;}
                }
                else if(lastChanged>=firstChanged){int count=Math.Min(lastChanged+1,SelectedCells)-firstChanged;buffer.SetData(gpu,firstChanged,firstChanged,count);UploadedBytes+=(long)count*48;}
                drawBounds=new Bounds(new Vector3(position.x,(minY+maxY)*.5f,position.z),new Vector3(OuterRadius*2+10,maxY-minY+10,OuterRadius*2+10));
            }
            BufferUploadTotalMs+=ElapsedMs(phaseStarted);
            previousProxySubmission=proxiesActive;
            phaseStarted=System.Diagnostics.Stopwatch.GetTimestamp();
            if(ownershipChanged)
            {
                ownershipTexture.LoadRawTextureData(ownership);ownershipTexture.Apply(false,false);
                UploadedBytes+=ownership.Length;ownershipUploaded=true;
            }
            OwnershipUploadTotalMs+=ElapsedMs(phaseStarted);
            SelectionCount++;
            Shader.SetGlobalTexture("_MapGroveOwnership",ownershipTexture);
            Shader.SetGlobalVector("_MapGroveOwnershipBounds",new Vector4(-12096+gridX*672,-12096+gridZ*672,1f/(PageWindow*672),1f/(PageWindow*672)));
            Shader.SetGlobalFloat("_MapGroveOwnershipActive",SelectedCells>0?1:0);
            previousPosition=position;previousRotation=camera.transform.rotation;previousProjection=camera.projectionMatrix;nextUpdate=Time.unscaledTime+.15f;dirty=false;
            SelectionMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        }
        private void DrawForCamera(Camera camera)
        {
            if(!valid||viewingCamera==null||camera!=viewingCamera||(camera.cullingMask&(1<<gameObject.layer))==0)return;
            if(diagnosticUntil>0&&Time.unscaledTime>=diagnosticUntil){properties.SetFloat("_Diagnostic",0);diagnosticUntil=0;}
            // Complete eye orientation includes banking/roll. The previous forward
            // vector test missed pure roll until the timer or translation refreshed.
            // This retains approximately the old 2.56-degree refresh threshold.
            if(dirty||previousProxySubmission!=(distantClustersEnabled&&distantClusterMesh!=null&&distantClusterMaterial!=null)||(pageFadesActive&&Time.unscaledTime>=nextUpdate)||!camera.projectionMatrix.Equals(previousProjection)||(viewingCamera.transform.position-previousPosition).sqrMagnitude>9||Mathf.Abs(Quaternion.Dot(viewingCamera.transform.rotation,previousRotation))<.99975f)
                Select(viewingCamera,false);
            Draw(viewingCamera);
        }
        private void Draw(Camera camera)
        {
            LastDraws=0;if(SelectedCells==0||buffer==null)return;
            properties.SetVector("_GroveViewPosition",camera.transform.position);
            var drawCounts=proxiesActive?submissionCounts:tierCounts;
            int offset=0;
            for(int tier=0;tier<3;tier++)
            {
                if(drawCounts[tier]==0)continue;
                properties.SetInt("_BranchOffset",offset);
#if UNITY_EDITOR
                if(!Application.isPlaying)properties.SetFloat("_Diagnostic",DiagnosticTierForVisualQa>=0?(tier==DiagnosticTierForVisualQa?1:0):branchMaterial.GetFloat("_Diagnostic"));
#endif
                QuestFlight.Rendering.ProductRenderSubmission.DrawMeshInstancedProcedural(branchMesh,tier,branchMaterial,drawBounds,drawCounts[tier],properties,ShadowCastingMode.Off,false,gameObject.layer,camera,LightProbeUsage.Off);
                offset+=drawCounts[tier];LastDraws++;
            }
            DrawProxies(camera);
        }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        // Simulator QA copies the actual tracked eye matrices to temporary cameras.
        // Reuse the pilot's already selected cells; never change ownership or LOD.
        public void DrawSimulatorEyeCapture(Camera tracked, Camera eye)
        {
            if (!isActiveAndEnabled || !valid || tracked != viewingCamera || eye == null ||
                (eye.cullingMask & (1 << gameObject.layer)) == 0) return;
            Draw(eye);
        }
#endif
        private void OnDisable()
        {
            Camera.onPreCull-=DrawForCamera;
            QuestFlight.Rendering.ProductRenderSubmission.Draw -= DrawForCamera;
            StopAllCoroutines();Shader.SetGlobalFloat("_MapGroveOwnershipActive",0);pages.Clear();buffer?.Release();buffer=null;
            if(ownershipTexture!=null){if(Application.isPlaying)Destroy(ownershipTexture);else DestroyImmediate(ownershipTexture);}
            selectionEpoch=1;previousOwnershipCount=0;ownershipMark=0;ownershipUploaded=false;pageFadesActive=false;cachedGpuCells=0;
            Array.Clear(ownership,0,ownership.Length);Array.Clear(ownershipMarks,0,ownershipMarks.Length);Array.Clear(gpu,0,gpu.Length);
            valid=false;SelectedCells=SelectedTriangles=LastDraws=0;Array.Clear(tierCounts,0,3);dirty=true;
            ReleaseProxies();
        }
#if UNITY_EDITOR
        public int DiagnosticTierForVisualQa=-1;
        public Vector4[] SelectedSourceRecordsForVisualQa()
        {
            var result=new Vector4[SelectedCells*3];
            for(int i=0;i<SelectedCells;i++){result[i*3]=gpu[i].positionHeight;result[i*3+1]=gpu[i].tintRadius;result[i*3+2]=gpu[i].rotationFade;}
            return result;
        }
        public void PrepareForVisualQa(Camera camera)
        {
            Initialize();Desired(camera.transform.position);pages.Clear();
            foreach(int key in desired){var r=records[key];pages[key]=Decode(File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath,pageDirectory,r.file)),r,-10);}
            Select(camera,true);
        }
        public void DrawForVisualQa(Camera camera){if(valid)Draw(camera);}
        public void ClearForVisualQa()=>OnDisable();
        public static void ValidatePageForVisualQa(byte[] bytes,Record record)=>Decode(bytes,record,0);
#endif
    }
}
