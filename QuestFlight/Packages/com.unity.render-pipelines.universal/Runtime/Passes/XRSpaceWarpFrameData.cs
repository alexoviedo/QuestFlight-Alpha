using System.Collections.Generic;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    // QuestFlight extension to URP 17.3.0. These are the provider-owned, already
    // imported attachments. Consumers must not clear, release or reimport them.
    public sealed class XRSpaceWarpFrameData : ContextItem
    {
        public TextureHandle color, depth;
        public Matrix4x4[] current, previous;
        public bool rightHanded, foveated;
        public override void Reset()
        {
            color = depth = TextureHandle.nullHandle;
            current = previous = null;
            rightHanded = foveated = false;
        }
        public static bool CanFoveateColor(UniversalCameraData camera,bool backBuffer)
            => camera.xr.enabled && camera.xr.supportsFoveatedRendering && (camera.xrUniversal.canFoveateIntermediatePasses || backBuffer);
        static readonly HashSet<int> resetRequests = new HashSet<int>();
        public static void RequestHistoryReset(Camera camera)
        {
            if (camera != null) resetRequests.Add(camera.GetInstanceID());
        }
        internal static bool ConsumeReset(Camera camera) => resetRequests.Remove(camera.GetInstanceID());
    }
}
