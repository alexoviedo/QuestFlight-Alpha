using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    /// <summary>Offline policy over the final stored float32 heightfield coordinates.</summary>
    public static class TerrainFloat32HeightfieldPolicy
    {
        public sealed class Result
        {
            public int[] indices;
            public int[] invertedFaces;
            public int[] zeroFootprintFaces;
        }

        public static double ProjectedCrossY(Vector3 a, Vector3 b, Vector3 c) =>
            ((double)b.z - a.z) * ((double)c.x - a.x) -
            ((double)b.x - a.x) * ((double)c.z - a.z);

        public static Result CorrectIndices(Vector3[] vertices, int[] triangles, bool excludeZeroFootprint)
        {
            if (vertices == null || triangles == null || triangles.Length == 0 || triangles.Length % 3 != 0)
                throw new InvalidDataException("Heightfield requires finite vertices and complete triangles.");
            foreach (Vector3 vertex in vertices)
                if (!float.IsFinite(vertex.x) || !float.IsFinite(vertex.y) || !float.IsFinite(vertex.z))
                    throw new InvalidDataException("Heightfield contains a nonfinite stored float32 vertex.");
            var corrected = new List<int>(triangles.Length);
            var inverted = new List<int>();
            var collapsed = new List<int>();
            for (int offset = 0; offset < triangles.Length; offset += 3)
            {
                int a = triangles[offset], b = triangles[offset + 1], c = triangles[offset + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                    throw new InvalidDataException("Heightfield contains an out-of-range triangle index.");
                double projected = ProjectedCrossY(vertices[a], vertices[b], vertices[c]);
                if (projected == 0d)
                {
                    if (!excludeZeroFootprint)
                        throw new InvalidDataException($"Zero XZ footprint in stored float32 heightfield face {offset / 3}.");
                    collapsed.Add(offset / 3);
                    continue;
                }
                if (projected < 0d)
                {
                    inverted.Add(offset / 3);
                    (b, c) = (c, b);
                }
                corrected.Add(a); corrected.Add(b); corrected.Add(c);
            }
            if (corrected.Count == 0)
                throw new InvalidDataException("Heightfield has no nonzero XZ footprint.");
            return new Result { indices = corrected.ToArray(), invertedFaces = inverted.ToArray(), zeroFootprintFaces = collapsed.ToArray() };
        }
    }
}
