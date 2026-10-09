using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Rendering
{
    /// <summary>SRP draw bridge for the existing bounded procedural renderers.</summary>
    public static class ProductRenderSubmission
    {
        public static event Camera.CameraCallback Draw;
        public static bool MotionPass { get; private set; }
        public static bool Preparing { get; private set; }
        public static bool WorldPresentationVisible { get; private set; } = true;
        public static void SetWorldPresentationVisible(bool visible) { WorldPresentationVisible = visible; }
        static RasterCommandBuffer command;
        static readonly Unity.Profiling.ProfilerMarker ColorMarker =
            new Unity.Profiling.ProfilerMarker("QuestFlight.Procedural.Color");
        static readonly Unity.Profiling.ProfilerMarker MotionMarker =
            new Unity.Profiling.ProfilerMarker("QuestFlight.Procedural.Motion");
        // Admission/uploads precede all rendering, including terrain ownership.
        // Submission callbacks reuse their prepared frame/camera batches below.
        public static void Prepare(Camera camera)
        { Preparing=true;try { Draw?.Invoke(camera); } finally { Preparing=false; } }
        public static void Execute(RasterCommandBuffer target, Camera camera, bool motion)
        {
            if (!WorldPresentationVisible) return;
            using var sample = (motion ? MotionMarker : ColorMarker).Auto();
            command = target; MotionPass = motion;
            try { Draw?.Invoke(camera); }
            finally { command = null; MotionPass = false; }
        }
        public static void DrawMeshInstanced(Mesh mesh, int submesh, Material material,
            Matrix4x4[] matrices, int count, MaterialPropertyBlock properties,
            ShadowCastingMode shadows, bool receive, int layer, Camera camera,
            LightProbeUsage probes, LightProbeProxyVolume proxy)
        {
            if (!WorldPresentationVisible) return;
            if (command == null)
            {
                if (GraphicsSettings.currentRenderPipeline == null)
                    Graphics.DrawMeshInstanced(mesh, submesh, material, matrices, count,
                        properties, shadows, receive, layer, camera, probes, proxy);
                return;
            }
            int pass = MotionPass ? material.FindPass("XRMotionVectors") : 0;
            if (pass >= 0) command.DrawMeshInstanced(mesh, submesh, material, pass, matrices, count, properties);
        }
        public static void DrawMeshInstancedProcedural(Mesh mesh, int submesh, Material material,
            Bounds bounds, int count, MaterialPropertyBlock properties, ShadowCastingMode shadows,
            bool receive, int layer, Camera camera, LightProbeUsage probes)
        {
            if (!WorldPresentationVisible) return;
            if (command == null)
            {
                if (GraphicsSettings.currentRenderPipeline == null)
                    Graphics.DrawMeshInstancedProcedural(mesh, submesh, material, bounds, count,
                        properties, shadows, receive, layer, camera, probes);
                return;
            }
            int pass = MotionPass ? material.FindPass("XRMotionVectors") : 0;
            if (pass >= 0) command.DrawMeshInstancedProcedural(mesh, submesh, material, pass, count, properties);
        }
    }
}
