using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace QuestFlight.Rendering
{
    // Development-only, one-shot readback of the actual provider attachments.
    // It never substitutes debug buffers for compositor submissions.
    public sealed class ProductSpaceWarpCapturePass : ScriptableRenderPass, IDisposable
    {
        readonly Material material;
        readonly MaterialPropertyBlock copyProperties=new MaterialPropertyBlock();
        RTHandle motion, depth, color;
        bool pending;
        int remaining, readbacks;
        float nextPoll;
        string directory;
        public ProductSpaceWarpCapturePass(Shader shader)
        {
            // Capture matching color, depth and motion from one frame.
            renderPassEvent=RenderPassEvent.AfterRenderingTransparents;
            copyProperties.SetVector("_BlitScaleBias",new Vector4(1,1,0,0));
            if(shader!=null)material=CoreUtils.CreateEngineMaterial(shader);
            RenderPipelineManager.endCameraRendering+=Completed;
        }
        sealed class Data { public TextureHandle source; public Material material; public MaterialPropertyBlock properties; public int pass; }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(material==null || pending || readbacks!=0 || !frame.Contains<XRSpaceWarpFrameData>())return;
            if(remaining==0)
            {
                if(Time.unscaledTime<nextPoll)return;
                nextPoll=Time.unscaledTime+.5f;
                string request=Path.Combine(Application.persistentDataPath,"capture-spacewarp.request");
                if(!File.Exists(request))return;
                remaining=int.TryParse(File.ReadAllText(request).Trim(),out int count)?Mathf.Clamp(count,1,12):1;
                File.Delete(request);
            }
            remaining--;
            directory=Path.Combine(Application.persistentDataPath,"spacewarp-captures",DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(directory);
            var xr=frame.Get<XRSpaceWarpFrameData>();
            var info=graph.GetRenderTargetInfo(xr.color);
            var depthInfo=graph.GetRenderTargetInfo(xr.depth);
            var descriptor=new RenderTextureDescriptor(info.width,info.height,RenderTextureFormat.ARGBHalf,0)
            {dimension=TextureDimension.Tex2DArray,volumeDepth=2,msaaSamples=1,vrUsage=VRTextureUsage.TwoEyes};
            RenderingUtils.ReAllocateHandleIfNeeded(ref motion,descriptor,name:"SpaceWarp diagnostic motion");
            RenderingUtils.ReAllocateHandleIfNeeded(ref depth,descriptor,name:"SpaceWarp diagnostic depth");
            var colorSource=frame.Get<UniversalResourceData>().activeColorTexture;
            var colorInfo=graph.GetRenderTargetInfo(colorSource);
            descriptor.width=colorInfo.width;descriptor.height=colorInfo.height;
            RenderingUtils.ReAllocateHandleIfNeeded(ref color,descriptor,name:"SpaceWarp diagnostic color");
            Record(graph,xr.color,graph.ImportTexture(motion),0);
            Record(graph,xr.depth,graph.ImportTexture(depth),1);
            Record(graph,colorSource,graph.ImportTexture(color),0);
            var cameraData=frame.Get<UniversalCameraData>();
            var camera=cameraData.camera;
            File.WriteAllText(Path.Combine(directory,"metadata.json"),JsonUtility.ToJson(new Metadata
            {frame=Time.frameCount,width=info.width,height=info.height,colorWidth=colorInfo.width,colorHeight=colorInfo.height,history=ProductSpaceWarpController.HistoryGeneration,
                motionSamples=info.msaaSamples,depthSamples=depthInfo.msaaSamples,colorSamples=colorInfo.msaaSamples,
                motionFormat=info.format.ToString(),depthFormat=depthInfo.format.ToString(),colorFormat=colorInfo.format.ToString(),
                frameIntervalSeconds=Time.unscaledDeltaTime,timeScale=Time.timeScale,
                resetMotion=Shader.GetGlobalFloat("_QFResetMotion"),rightHanded=xr.rightHanded,foveated=xr.foveated,
                // A transient TextureHandle has no RTHandle until graph execution.
                // For this Game camera on Vulkan, intermediate color is flipped
                // relative to the provider's renderIntoTexture:false projection.
                colorYFlipped=SystemInfo.graphicsUVStartsAtTop && !frame.Get<UniversalResourceData>().isActiveTargetBackBuffer,
                current=xr.current,previous=xr.previous,cameraToWorld=camera.cameraToWorldMatrix,
                cameraParent=camera.transform.parent.localToWorldMatrix,near=camera.nearClipPlane,far=camera.farClipPlane,
                projectionLeft=camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left),projectionRight=camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right),
                viewLeft=camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left),viewRight=camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right)},true));
            pending=true;
#endif
        }
        [Serializable] sealed class Metadata
        {
            public string schema="questflight.spacewarp.attachments.v3";
            public string motion="Raw signed 3D NDC delta RGB, coverage A; one EXR per eye";
            public string depth="Raw device depth R replicated in RGB; inspect reversed-Z for Vulkan";
            public int frame,width,height,colorWidth,colorHeight;public uint history;
            public int motionSamples,depthSamples,colorSamples;
            public string motionFormat,depthFormat,colorFormat;
            public float frameIntervalSeconds,timeScale;
            public float resetMotion,near,far;
            public bool rightHanded,foveated;
            public bool colorYFlipped;
            public Matrix4x4[] current,previous;
            public Matrix4x4 cameraToWorld,cameraParent,projectionLeft,projectionRight,viewLeft,viewRight;
        }
        void Record(RenderGraph graph,TextureHandle source,TextureHandle destination,int pass)
        {
            using var builder=graph.AddRasterRenderPass<Data>("SpaceWarp diagnostic copy",out var data);
            data.source=source;data.material=material;data.properties=copyProperties;data.pass=pass;
            builder.UseTexture(source,AccessFlags.Read);builder.SetRenderAttachment(destination,0,AccessFlags.WriteAll);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetExtendedFeatureFlags(ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
            builder.SetRenderFunc((Data d,RasterGraphContext context)=>
            {
                // Provider attachments wrap external render-target identifiers,
                // not Unity Texture objects. Bind the identifier and explicitly
                // select depth from the combined XR color/depth target.
                context.cmd.SetGlobalTexture("_BlitTexture",d.source,
                    d.pass==1?RenderTextureSubElement.Depth:RenderTextureSubElement.Color);
                // A private block avoids Blitter's shared cached texture binding
                // overriding the external attachment bound above.
                context.cmd.DrawProcedural(Matrix4x4.identity,d.material,d.pass,MeshTopology.Triangles,3,1,d.properties);
            });
        }
        void Completed(ScriptableRenderContext context,Camera camera)
        {
            if(!pending)return;
            pending=false;
            Capture(motion.rt,"motion");Capture(depth.rt,"depth");Capture(color.rt,"color");
        }
        void Capture(RenderTexture target,string name)
        {
            for(int eye=0;eye<2;eye++)
            {
                string output=Path.Combine(directory,name+"_"+(eye==0?"left":"right")+".exr");
                int w=target.width,h=target.height;
                readbacks++;
                AsyncGPUReadback.Request(target,0,0,w,0,h,eye,1,TextureFormat.RGBAHalf,request=>
                {
                    readbacks--;
                    if(request.hasError){Debug.LogError("[SpaceWarp] Diagnostic readback failed: "+output);return;}
                    var image=new Texture2D(w,h,TextureFormat.RGBAHalf,false,true);
                    try{image.LoadRawTextureData(request.GetData<byte>());image.Apply();File.WriteAllBytes(output,image.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));}
                    finally{UnityEngine.Object.Destroy(image);}
                });
            }
        }
        public void Dispose()
        {
            RenderPipelineManager.endCameraRendering-=Completed;
            // A diagnostic request may still reference these targets.
            if(motion!=null || depth!=null || color!=null)AsyncGPUReadback.WaitAllRequests();
            motion?.Release();depth?.Release();color?.Release();
            CoreUtils.Destroy(material);
        }
    }
}
