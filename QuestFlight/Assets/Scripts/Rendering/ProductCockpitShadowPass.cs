using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace QuestFlight.Rendering
{
    public sealed class ProductCockpitShadowPass : ScriptableRenderPass
    {
        public static RenderTexture Texture;
        public static Mesh Mesh;
        public static Matrix4x4 Model, View, Projection;
        public static Shader Shader;
        RTHandle handle;
        Material material;
        RenderTexture wrapped;
        sealed class Data { public Mesh mesh; public Material material; public Matrix4x4 model, view, projection; }
        sealed class Mips { public TextureHandle texture; public RenderTexture target; }
        public ProductCockpitShadowPass() { renderPassEvent = RenderPassEvent.BeforeRenderingShadows; }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            if (Texture == null || Mesh == null || Shader == null) return;
            if (wrapped != Texture) { handle?.Release(); handle = RTHandles.Alloc(new RenderTargetIdentifier(Texture)); wrapped = Texture; }
            if (material == null || material.shader != Shader) { CoreUtils.Destroy(material); material = CoreUtils.CreateEngineMaterial(Shader); }
            var target = graph.ImportTexture(handle,new RenderTargetInfo
            {width=Texture.width,height=Texture.height,volumeDepth=1,msaaSamples=1,format=Texture.graphicsFormat});
            var depth=graph.CreateTexture(new TextureDesc(Texture.width,Texture.height)
            { name="Cockpit shadow depth",depthBufferBits=DepthBits.Depth24,clearBuffer=true });
            using (var builder = graph.AddRasterRenderPass<Data>("Cockpit local shadow", out var data))
            {
                data.mesh=Mesh;data.material=material;data.model=Model;data.view=View;data.projection=Projection;
                builder.SetRenderAttachment(target,0,AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth,AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((Data d,RasterGraphContext ctx)=>
                {
                    ctx.cmd.ClearRenderTarget(true,true,Color.white);
                    ctx.cmd.SetGlobalMatrix("_QFShadowView",d.view);
                    ctx.cmd.SetGlobalMatrix("_QFShadowVP",d.projection*d.view);
                    ctx.cmd.DrawMesh(d.mesh,d.model,d.material,0,0);
                });
            }
            using (var builder=graph.AddUnsafePass<Mips>("Cockpit shadow mip chain",out var data))
            {
                data.texture=target;data.target=Texture;builder.UseTexture(target,AccessFlags.ReadWrite);builder.AllowPassCulling(false);
                builder.SetRenderFunc((Mips d,UnsafeGraphContext ctx)=>ctx.cmd.GenerateMips(d.target));
            }
        }
        public void Dispose() { handle?.Release();handle=null;wrapped=null;CoreUtils.Destroy(material);material=null; }
    }
}
