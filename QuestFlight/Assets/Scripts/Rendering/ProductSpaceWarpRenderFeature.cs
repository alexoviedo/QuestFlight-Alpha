using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace QuestFlight.Rendering
{
    public sealed class ProductSpaceWarpRenderFeature : ScriptableRendererFeature
    {
        ProductPass motion, color;
        ProductCockpitShadowPass shadow;
        [SerializeField] Shader diagnosticShader;
        ProductSpaceWarpCapturePass capture;
        public void ConfigureDiagnostic(Shader shader) { diagnosticShader=shader; }
        public override void Create()
        {
            shadow?.Dispose(); shadow = new ProductCockpitShadowPass();
            motion = new ProductPass(true) { renderPassEvent = RenderPassEvent.AfterRenderingPrePasses };
            color = new ProductPass(false) { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
            capture?.Dispose();capture=new ProductSpaceWarpCapturePass(diagnosticShader);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview) return;
            ProductSpaceWarpController.ApplyLegacyLighting();
            ProductRenderSubmission.Prepare(renderingData.cameraData.camera);
            renderer.EnqueuePass(shadow);
            renderer.EnqueuePass(motion); renderer.EnqueuePass(color);
            if(Debug.isDebugBuild)renderer.EnqueuePass(capture);
        }
        protected override void Dispose(bool disposing) { shadow?.Dispose();capture?.Dispose(); }
        sealed class ProductPass : ScriptableRenderPass
        {
            readonly bool motion;
            public ProductPass(bool isMotion) { motion = isMotion; }
            sealed class Data
            {
                public Camera camera;
                public bool motion;
                public XRSpaceWarpFrameData xr;
                public RendererListHandle transparent; public bool setViewport; public Rect viewport;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var camera = frame.Get<UniversalCameraData>();
                if (motion && !frame.Contains<XRSpaceWarpFrameData>()) return;
                var resources = frame.Get<UniversalResourceData>();
                var xr = motion ? frame.Get<XRSpaceWarpFrameData>() : null;
                using var builder = graph.AddRasterRenderPass<Data>(motion ? "Product XR motion and depth" : "Product procedural color", out var data);
                data.camera = camera.camera; data.motion = motion; data.xr = xr;
                data.setViewport=!motion && camera.xr.enabled && resources.isActiveTargetBackBuffer;
                if(data.setViewport)data.viewport=camera.xr.GetViewport();
                if(!motion && camera.xr.enabled)
                {
                    builder.EnableFoveatedRasterization(XRSpaceWarpFrameData.CanFoveateColor(camera,resources.isActiveTargetBackBuffer));
                    builder.SetExtendedFeatureFlags(ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
                }
                builder.SetRenderAttachment(motion ? xr.color : resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(motion ? xr.depth : resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                if (motion)
                {
                    builder.EnableFoveatedRasterization(xr.foveated);
                    builder.SetExtendedFeatureFlags(ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
                    var drawing = new DrawingSettings(new ShaderTagId("XRMotionVectors"), new SortingSettings(camera.camera) { criteria = SortingCriteria.CommonTransparent })
                    { perObjectData = PerObjectData.MotionVectors, enableInstancing = false };
                    var filter = new FilteringSettings(RenderQueueRange.transparent, camera.camera.cullingMask) { forceAllMotionVectorObjects = true };
                    data.transparent = graph.CreateRendererList(new RendererListParams(frame.Get<UniversalRenderingData>().cullResults, drawing, filter));
                    builder.UseRendererList(data.transparent);
                }
                builder.SetRenderFunc((Data d, RasterGraphContext ctx) =>
                {
                    var cmd = ctx.cmd;
                    if(d.setViewport)cmd.SetViewport(d.viewport);
                    if (d.motion)
                    {
                        cmd.SetGlobalMatrixArray("_QFCurrentVP", d.xr.current);
                        cmd.SetGlobalMatrixArray("_QFPreviousVP", d.xr.previous);
                        cmd.SetGlobalFloat("_QFNdcSign", d.xr.rightHanded ? -1 : 1);
                    }
                    ProductRenderSubmission.Execute(cmd, d.camera, d.motion);
                    if (d.motion) { ctx.cmd.DrawRendererList(d.transparent); ProductSpaceWarpController.RecordMotionSubmission(); }
                });
            }
        }
    }
}
