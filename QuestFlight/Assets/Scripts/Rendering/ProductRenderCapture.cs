using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace QuestFlight.Rendering
{
    public static class ProductRenderCapture
    {
        public static void Render(Camera camera)
        {
            if(GraphicsSettings.currentRenderPipeline==null) { camera.Render();return; }
            if(camera.targetTexture==null)throw new InvalidOperationException("Offline capture requires an explicit destination");
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=camera.targetTexture});
        }
    }
}
