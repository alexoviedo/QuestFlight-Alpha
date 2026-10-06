using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuestFlight.Rendering
{
    /// <summary>Previous presented vertices, unaffected by native tick frequency.</summary>
    public sealed class ProductDeformedMeshHistory : IDisposable
    {
        readonly List<Vector3> current = new List<Vector3>();
        readonly List<Vector3> previous = new List<Vector3>();
        uint generation;
        bool ready;
        Mesh target;
        public ProductDeformedMeshHistory() { ProductSpaceWarpController.FrameCompleted += Complete; }
        public void Upload(Mesh mesh, List<Vector3> vertices)
        {
            if (mesh == null) return;
            target=mesh;
            current.Clear(); current.AddRange(vertices);
            if (!ready || generation != ProductSpaceWarpController.HistoryGeneration || previous.Count != vertices.Count)
            { previous.Clear(); previous.AddRange(vertices); ready = true; generation = ProductSpaceWarpController.HistoryGeneration; }
            mesh.SetUVs(4, previous);
        }
        void Complete(Camera camera) { previous.Clear(); previous.AddRange(current); if(target!=null)target.SetUVs(4,previous); }
        public void Dispose() { ProductSpaceWarpController.FrameCompleted -= Complete; current.Clear(); previous.Clear(); ready = false; }
    }
}
