using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    public sealed partial class MapBranchGroveRenderer
    {
        // Shared, offline captures of four accepted branch patches. Small groups
        // avoid the unique-texture cost and parallax of a whole-grove billboard.
        public bool distantClustersEnabled;
        public Mesh distantClusterMesh;
        public Material distantClusterMaterial;
        public int ClusterInstances { get; private set; }
        public int ReplacedBranchCells { get; private set; }
        public int SourceSelectedTriangles { get; private set; }
        public int SubmittedBranchCells { get; private set; }
        private sealed class ProxyGroup
        {
            public Vector3 center,tint;
            public float scale,error;
            public int template,a,b,c,d,triangleCost;
            public uint epoch;
            public byte mask;
            public float distance,coverage,fade;
        }
        private readonly List<ProxyGroup> activeProxies=new List<ProxyGroup>(MaximumCells);
        private GpuCell[] proxyGpu,submission;
        private float[] childProxyFade;
        private readonly int[] submissionCounts=new int[3];
        private ComputeBuffer proxyBuffer;
        private MaterialPropertyBlock proxyProperties;
        private bool proxiesActive;
        private bool previousProxySubmission;
        private float proxyProjectionScale;
        private const float ProxyStart=900,ProxyFull=1050,ProxyViewErrorPixels=1;

        private static float Upper(Cell c)=>c.ground+Mathf.Max(c.height*.6f,c.height-1.6f);
        private static void BuildProxyGroups(Page page)
        {
            // Page dimensions are divisible by two: one deterministic owner per
            // group, independent of streaming order or head direction.
            var lookup=System.Buffers.ArrayPool<int>.Shared.Rent(CellsPerPage*CellsPerPage);
            Array.Fill(lookup,-1);
            var groups=new List<ProxyGroup>();
            try
            {
                for(int i=0;i<page.cells.Length;i++)lookup[page.cells[i].cell]=i;
                for(int i=0;i<page.cells.Length;i++)
                {
                    var a=page.cells[i];int x=a.cell%252,z=a.cell/252;
                    if((x&1)!=0||(z&1)!=0)continue;
                    int ib=lookup[a.cell+1],ic=lookup[a.cell+252],id=lookup[a.cell+253];
                    if(ib<0||ic<0||id<0)continue;
                    var b=page.cells[ib];var c=page.cells[ic];var d=page.cells[id];
                    if(a.grove!=b.grove||a.grove!=c.grove||a.grove!=d.grove||
                       a.family!=b.family||a.family!=c.family||a.family!=d.family)continue;
                    float r=(a.radius+b.radius+c.radius+d.radius)*.25f;
                    float rMin=Mathf.Min(Mathf.Min(a.radius,b.radius),Mathf.Min(c.radius,d.radius));
                    float rMax=Mathf.Max(Mathf.Max(a.radius,b.radius),Mathf.Max(c.radius,d.radius));
                    if(rMin<1.25f||rMax-rMin>1.05f)continue;
                    Vector3 tint=(a.tint+b.tint+c.tint+d.tint)*.25f;
                    if((a.tint-tint).sqrMagnitude>.008f||(b.tint-tint).sqrMagnitude>.008f||
                       (c.tint-tint).sqrMagnitude>.008f||(d.tint-tint).sqrMagnitude>.008f)continue;
                    float ya=Upper(a),yb=Upper(b),yc=Upper(c),yd=Upper(d),y=(ya+yb+yc+yd)*.25f;
                    float scale=r/2.25f;
                    int sx=Mathf.Clamp(Mathf.RoundToInt((yb+yd-ya-yc)/(4*scale)),-1,1);
                    int sz=Mathf.Clamp(Mathf.RoundToInt((yc+yd-ya-yb)/(4*scale)),-1,1);
                    float error=Mathf.Max(Mathf.Max(Mathf.Abs(ya-(y-(sx+sz)*scale)),Mathf.Abs(yb-(y+(sx-sz)*scale))),
                        Mathf.Max(Mathf.Abs(yc-(y+(-sx+sz)*scale)),Mathf.Abs(yd-(y+(sx+sz)*scale))));
                    // Includes shifted patch centers and footprint-radius error.
                    error+=Mathf.Max(r-rMin,rMax-r)+Mathf.Abs(1-scale)*CellMeters*.5f;
                    if(error>2)continue;
                    int index=groups.Count;
                    groups.Add(new ProxyGroup{center=new Vector3((a.center.x+d.center.x)*.5f,y,(a.center.z+d.center.z)*.5f),
                        tint=tint,scale=scale,error=error,template=((a.family>0?1:0)*18+(sz+1)*6+(sx+1)*2)});
                    page.cells[i].proxyMember=((index+1)<<2);page.cells[ib].proxyMember=((index+1)<<2)|1;
                    page.cells[ic].proxyMember=((index+1)<<2)|2;page.cells[id].proxyMember=((index+1)<<2)|3;
                }
            }
            finally { System.Buffers.ArrayPool<int>.Shared.Return(lookup); }
            page.proxies=groups.ToArray();
        }
        private void BeginProxySelection(Camera camera)
        {
            proxiesActive=distantClustersEnabled&&distantClusterMesh!=null&&distantClusterMaterial!=null;
            ClusterInstances=ReplacedBranchCells=0;activeProxies.Clear();
            if(!proxiesActive)return;
            if(proxyBuffer==null)
            {
                proxyGpu=new GpuCell[MaximumCells/4];submission=new GpuCell[MaximumCells];childProxyFade=new float[MaximumCells];
                proxyBuffer=new ComputeBuffer(MaximumCells/4,48,ComputeBufferType.Structured);
                proxyProperties=new MaterialPropertyBlock();proxyProperties.SetBuffer("_BranchCells",proxyBuffer);
            }
            Array.Clear(childProxyFade,0,childProxyFade.Length);
            int pixels=camera.pixelHeight;
            if(camera.stereoEnabled)pixels=Math.Max(pixels,UnityEngine.XR.XRSettings.eyeTextureHeight);
            proxyProjectionScale=Mathf.Abs(camera.projectionMatrix.m11)*pixels*.5f;
        }
        private void RecordProxyChild(Page page,int index,int gpuIndex,float distance,float coverage)
        {
            if(!proxiesActive||distance<ProxyStart)return;
            int member=page.cells[index].proxyMember;if(member==0)return;
            var group=page.proxies[(member>>2)-1];
            if(group.epoch!=selectionEpoch){group.epoch=selectionEpoch;group.mask=0;group.triangleCost=0;group.distance=distance;group.coverage=coverage;activeProxies.Add(group);}
            group.triangleCost+=TriangleCosts[DistanceTier(distance)];
            group.distance=Mathf.Min(group.distance,distance);group.coverage=Mathf.Min(group.coverage,coverage);
            int slot=member&3;group.mask|=(byte)(1<<slot);
            switch(slot){case 0:group.a=gpuIndex;break;case 1:group.b=gpuIndex;break;case 2:group.c=gpuIndex;break;default:group.d=gpuIndex;break;}
        }
        private void SelectProxies(Camera camera)
        {
            SourceSelectedTriangles=SelectedTriangles;SubmittedBranchCells=SelectedCells;
            if(!proxiesActive)return;
            Vector3 eye=camera.transform.position;
            foreach(var group in activeProxies)
            {
                group.fade=0;
                if(group.mask!=15)continue;
                Vector3 delta=eye-group.center;float distance=delta.magnitude;
                // Uphill views are common in a flight simulator. The atlas also
                // captures -35 degrees; steeper unsupported views keep geometry.
                if(distance<ProxyStart||delta.y/distance<-.573576f)continue;
                float errorStart=Mathf.Max(ProxyStart,group.error*proxyProjectionScale/ProxyViewErrorPixels);
                float fade=Mathf.SmoothStep(0,1,Mathf.InverseLerp(errorStart,errorStart+(ProxyFull-ProxyStart),group.distance));
                fade*=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.573576f,-.422618f,delta.y/distance));
                if(fade<=.001f)continue;
                group.fade=fade;
            }
            int triangleBudget=MaximumTriangleCount-SourceSelectedTriangles;
            // Completed replacements free space before temporary transition
            // geometry is admitted. The total original triangle cap still holds.
            for(int pass=0;pass<2;pass++)foreach(var group in activeProxies)
            {
                float fade=group.fade;if(fade<=.001f||(fade>=.999f)!=(pass==0))continue;
                if(pass==1&&triangleBudget<2)continue;
                triangleBudget+=pass==0?group.triangleCost-2:-2;
                childProxyFade[group.a]=childProxyFade[group.b]=childProxyFade[group.c]=childProxyFade[group.d]=fade;
                proxyGpu[ClusterInstances++]=new GpuCell{positionHeight=new Vector4(group.center.x,group.center.y,group.center.z,group.scale),
                    tintRadius=new Vector4(group.tint.x,group.tint.y,group.tint.z,group.template),rotationFade=new Vector4(0,0,group.coverage*fade,0)};
            }
            Array.Clear(submissionCounts,0,3);int source=0,target=0;
            for(int tier=0;tier<3;tier++)for(int j=0;j<tierCounts[tier];j++,source++)
            {
                float fade=childProxyFade[source];if(fade>=.999f){ReplacedBranchCells++;continue;}
                var cell=gpu[source];cell.rotationFade.z*=1-fade;submission[target++]=cell;submissionCounts[tier]++;
            }
            SubmittedBranchCells=target;
            SelectedTriangles=submissionCounts[0]*12+submissionCounts[1]*6+submissionCounts[2]*4+ClusterInstances*2;
            if(ClusterInstances>0){proxyBuffer.SetData(proxyGpu,0,0,ClusterInstances);UploadedBytes+=(long)ClusterInstances*48;}
        }
        private void DrawProxies(Camera camera)
        {
            if(!proxiesActive||ClusterInstances==0)return;
            proxyProperties.SetVector("_GroveViewPosition",viewingCamera!=null?viewingCamera.transform.position:camera.transform.position);
            QuestFlight.Rendering.ProductRenderSubmission.DrawMeshInstancedProcedural(distantClusterMesh,0,distantClusterMaterial,drawBounds,ClusterInstances,proxyProperties,
                ShadowCastingMode.Off,false,gameObject.layer,camera,LightProbeUsage.Off);LastDraws++;
        }
        private void ReleaseProxies()
        {
            proxyBuffer?.Release();proxyBuffer=null;proxyGpu=null;submission=null;childProxyFade=null;
            activeProxies.Clear();proxiesActive=false;ClusterInstances=ReplacedBranchCells=0;
            previousProxySubmission=false;
        }
#if UNITY_EDITOR
        public static int CountClustersForVisualQa(byte[] data,Record record)=>Decode(data,record,0).proxies.Length;
        public Dictionary<Vector3,float> ValidateClustersForVisualQa()
        {
            var result=new Dictionary<Vector3,float>();
            if(!proxiesActive)return result;
            int full=0,proxies=0;
            foreach(var group in activeProxies)
            {
                if(group.mask!=15)continue;
                float fade=childProxyFade[group.a];
                if(fade!=childProxyFade[group.b]||fade!=childProxyFade[group.c]||fade!=childProxyFade[group.d])
                    throw new InvalidOperationException("Cluster child ownership is inconsistent");
                if(!float.IsFinite(fade)||fade<0||fade>1||group.distance<ProxyStart||group.template<0||group.template>34||(group.template&1)!=0)
                    throw new InvalidOperationException("Cluster range/template/coverage invalid");
                if(fade>0){proxies++;if(fade>=.999f)full+=4;}
                result.Add(group.center,fade);
            }
            if(proxies!=ClusterInstances||full!=ReplacedBranchCells||SubmittedBranchCells!=SelectedCells-full||SelectedTriangles>MaximumTriangleCount)
                throw new InvalidOperationException("Cluster submission budget or replacement accounting invalid");
            return result;
        }
#endif
    }
}
