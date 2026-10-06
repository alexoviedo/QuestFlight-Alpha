using System;
using UnityEngine;

namespace QuestFlight.Rendering
{
    /// <summary>Own history for authored rigid aircraft parts and windsock fabric.</summary>
    public sealed class ProductRigidRendererHistory : IDisposable
    {
        static readonly int HasHistory = Shader.PropertyToID("_QFHasPreviousModel");
        static readonly int PreviousModel = Shader.PropertyToID("_QFPreviousModel");
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        Renderer[] renderers = Array.Empty<Renderer>();
        Matrix4x4[] previous = Array.Empty<Matrix4x4>();
        bool[] presented = Array.Empty<bool>();
        uint generation;

        public ProductRigidRendererHistory() { ProductSpaceWarpController.FrameCompleted += RecordPresentedFrame; }

        public void Reset() { Array.Clear(presented, 0, presented.Length); }

        public void Upload(Renderer[] targets)
        {
            if (!ReferenceEquals(renderers, targets))
            {
                renderers = targets ?? Array.Empty<Renderer>();
                previous = new Matrix4x4[renderers.Length];
                presented = new bool[renderers.Length];
            }
            if (generation != ProductSpaceWarpController.HistoryGeneration)
            { Reset(); generation = ProductSpaceWarpController.HistoryGeneration; }
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                { presented[i] = false; continue; }
                renderer.GetPropertyBlock(properties);
                properties.SetFloat(HasHistory, 1);
                properties.SetMatrix(PreviousModel, presented[i] ? previous[i] : renderer.localToWorldMatrix);
                renderer.SetPropertyBlock(properties);
            }
        }

        // Called only after the tracked product camera completes its frame.
        public void RecordPresentedFrame(Camera camera)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                presented[i] = renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy;
                if (presented[i]) previous[i] = renderer.localToWorldMatrix;
            }
        }

        public void Dispose()
        {
            ProductSpaceWarpController.FrameCompleted -= RecordPresentedFrame;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(properties);
                properties.SetFloat(HasHistory, 0);
                renderer.SetPropertyBlock(properties);
            }
            renderers = Array.Empty<Renderer>(); previous = Array.Empty<Matrix4x4>(); presented = Array.Empty<bool>();
        }
    }
}
