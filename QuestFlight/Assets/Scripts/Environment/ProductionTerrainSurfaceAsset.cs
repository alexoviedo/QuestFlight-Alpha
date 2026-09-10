using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Baked, immutable production terrain query data. The asset is generated offline from the
    /// same height source used to author the serialized terrain meshes. It never creates or moves
    /// scene objects and performs no physics queries.
    /// </summary>
    [CreateAssetMenu(fileName = "ProductionTerrainSurface", menuName = "QuestFlight/Production Terrain Surface")]
    public sealed class ProductionTerrainSurfaceAsset : ScriptableObject, IProductionTerrainSurface
    {
        [Serializable]
        public sealed class GridLayer
        {
            public string stableId;
            public ProductionTerrainZone zone;
            public int priority;
            public Vector2 minimumEnuMeters;
            [Min(0.01f)] public float spacingMeters = 1f;
            [Min(2)] public int width = 2;
            [Min(2)] public int height = 2;
            public float[] heightMeters = Array.Empty<float>();
            public Vector3[] normals = Array.Empty<Vector3>();

            public bool IsValid =>
                !string.IsNullOrWhiteSpace(stableId) &&
                spacingMeters > 0f &&
                width >= 2 &&
                height >= 2 &&
                heightMeters != null &&
                heightMeters.Length == width * height &&
                (normals == null || normals.Length == 0 || normals.Length == width * height);

            public bool Contains(float eastMeters, float northMeters)
            {
                float maxEast = minimumEnuMeters.x + spacingMeters * (width - 1);
                float maxNorth = minimumEnuMeters.y + spacingMeters * (height - 1);
                return eastMeters >= minimumEnuMeters.x && eastMeters <= maxEast &&
                       northMeters >= minimumEnuMeters.y && northMeters <= maxNorth;
            }

            public void Sample(float eastMeters, float northMeters, out float sampledHeight, out Vector3 sampledNormal)
            {
                float gx = Mathf.Clamp((eastMeters - minimumEnuMeters.x) / spacingMeters, 0f, width - 1f);
                float gz = Mathf.Clamp((northMeters - minimumEnuMeters.y) / spacingMeters, 0f, height - 1f);
                int x0 = Mathf.Min(Mathf.FloorToInt(gx), width - 2);
                int z0 = Mathf.Min(Mathf.FloorToInt(gz), height - 2);
                int x1 = x0 + 1;
                int z1 = z0 + 1;
                float tx = gx - x0;
                float tz = gz - z0;

                float h00 = heightMeters[z0 * width + x0];
                float h10 = heightMeters[z0 * width + x1];
                float h01 = heightMeters[z1 * width + x0];
                float h11 = heightMeters[z1 * width + x1];
                sampledHeight = Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);

                if (normals != null && normals.Length == heightMeters.Length)
                {
                    Vector3 n00 = normals[z0 * width + x0];
                    Vector3 n10 = normals[z0 * width + x1];
                    Vector3 n01 = normals[z1 * width + x0];
                    Vector3 n11 = normals[z1 * width + x1];
                    sampledNormal = Vector3.Lerp(
                        Vector3.Lerp(n00, n10, tx),
                        Vector3.Lerp(n01, n11, tx),
                        tz).normalized;
                }
                else
                {
                    float dhdx = ((h10 - h00) + (h11 - h01)) * 0.5f / spacingMeters;
                    float dhdz = ((h01 - h00) + (h11 - h10)) * 0.5f / spacingMeters;
                    sampledNormal = new Vector3(-dhdx, 1f, -dhdz).normalized;
                }
            }
        }

        [Serializable]
        public sealed class SurfaceRegion
        {
            public string stableId;
            public ProductionSurfaceCategory category;
            public int priority;
            public Vector2[] boundaryEnuMeters = Array.Empty<Vector2>();

            public bool Contains(Vector2 point)
            {
                if (boundaryEnuMeters == null || boundaryEnuMeters.Length < 3) return false;
                bool inside = false;
                int previous = boundaryEnuMeters.Length - 1;
                for (int current = 0; current < boundaryEnuMeters.Length; current++)
                {
                    Vector2 a = boundaryEnuMeters[current];
                    Vector2 b = boundaryEnuMeters[previous];
                    bool crosses = (a.y > point.y) != (b.y > point.y) &&
                                   point.x < (b.x - a.x) * (point.y - a.y) /
                                   Mathf.Max(Mathf.Abs(b.y - a.y), 0.000001f) * Mathf.Sign(b.y - a.y) + a.x;
                    if (crosses) inside = !inside;
                    previous = current;
                }
                return inside;
            }
        }

        /// <summary>
        /// A small, offline-authored triangle patch used only where two regular terrain grids
        /// cannot share a row of vertices. The serialized triangles are the same triangles used
        /// by the visible transition mesh, so height and normal queries cannot reintroduce the
        /// grid seam. Patches are bounded square annuli and are never runtime-generated.
        /// </summary>
        [Serializable]
        public sealed class TrianglePatch
        {
            public string stableId;
            public ProductionTerrainZone zone;
            public int priority;
            [Min(0f)] public float innerHalfExtentMeters;
            [Min(0.01f)] public float outerHalfExtentMeters;
            public Vector3[] vertices = Array.Empty<Vector3>();
            public Vector3[] normals = Array.Empty<Vector3>();
            public int[] triangles = Array.Empty<int>();

            // Query acceleration over the immutable authored triangles. This is
            // data indexing only: no geometry, collider, height or normal changes.
            private const float QueryCellMeters = 256f;
            [NonSerialized] private Dictionary<long, int[]> queryCells;
            [NonSerialized] private Vector3[] indexedVertices;
            [NonSerialized] private int[] indexedTriangles;

            private static long CellKey(int x, int z) => ((long)x << 32) | (uint)z;

            private void EnsureQueryIndex()
            {
                if (queryCells != null && ReferenceEquals(indexedVertices, vertices) &&
                    ReferenceEquals(indexedTriangles, triangles)) return;
                var cells = new Dictionary<long, List<int>>();
                for (int offset = 0; offset < triangles.Length; offset += 3)
                {
                    int ia = triangles[offset], ib = triangles[offset + 1], ic = triangles[offset + 2];
                    if ((uint)ia >= vertices.Length || (uint)ib >= vertices.Length || (uint)ic >= vertices.Length) continue;
                    Vector3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                    int minX = Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - 0.0001f) / QueryCellMeters);
                    int maxX = Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + 0.0001f) / QueryCellMeters);
                    int minZ = Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - 0.0001f) / QueryCellMeters);
                    int maxZ = Mathf.FloorToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) + 0.0001f) / QueryCellMeters);
                    for (int z = minZ; z <= maxZ; z++)
                    for (int x = minX; x <= maxX; x++)
                    {
                        long key = CellKey(x, z);
                        if (!cells.TryGetValue(key, out List<int> offsets)) cells[key] = offsets = new List<int>();
                        offsets.Add(offset); // Retain original order, including overlapping surfaces.
                    }
                }
                queryCells = new Dictionary<long, int[]>(cells.Count);
                foreach (var cell in cells) queryCells.Add(cell.Key, cell.Value.ToArray());
                indexedVertices = vertices;
                indexedTriangles = triangles;
            }

            public bool IsValid =>
                !string.IsNullOrWhiteSpace(stableId) &&
                outerHalfExtentMeters > innerHalfExtentMeters &&
                vertices != null && vertices.Length >= 3 &&
                normals != null && normals.Length == vertices.Length &&
                triangles != null && triangles.Length >= 3 && triangles.Length % 3 == 0;

            public bool Contains(float eastMeters, float northMeters)
            {
                float squareRadius = Mathf.Max(Mathf.Abs(eastMeters), Mathf.Abs(northMeters));
                return squareRadius >= innerHalfExtentMeters - 0.0001f &&
                       squareRadius <= outerHalfExtentMeters + 0.0001f;
            }

            public bool TrySample(
                float eastMeters,
                float northMeters,
                out float sampledHeight,
                out Vector3 sampledNormal)
            {
                sampledHeight = float.NegativeInfinity;
                sampledNormal = Vector3.up;
                if (!IsValid || !Contains(eastMeters, northMeters)) return false;
                EnsureQueryIndex();
                long key = CellKey(Mathf.FloorToInt(eastMeters / QueryCellMeters),
                    Mathf.FloorToInt(northMeters / QueryCellMeters));
                return queryCells.TryGetValue(key, out int[] candidates) &&
                    TrySampleCandidates(eastMeters, northMeters, candidates, out sampledHeight, out sampledNormal);
            }

#if UNITY_EDITOR
            public bool TrySampleReference(float eastMeters, float northMeters, out float height, out Vector3 normal) =>
                TrySampleCandidates(eastMeters, northMeters, null, out height, out normal);
#endif

            private bool TrySampleCandidates(float eastMeters, float northMeters, int[] candidates,
                out float sampledHeight, out Vector3 sampledNormal)
            {
                sampledHeight = float.NegativeInfinity;
                sampledNormal = Vector3.up;
                if (!IsValid || !Contains(eastMeters, northMeters)) return false;

                bool found = false;
                int count = candidates == null ? triangles.Length / 3 : candidates.Length;
                for (int candidate = 0; candidate < count; candidate++)
                {
                    int offset = candidates == null ? candidate * 3 : candidates[candidate];
                    int ia = triangles[offset];
                    int ib = triangles[offset + 1];
                    int ic = triangles[offset + 2];
                    if ((uint)ia >= vertices.Length || (uint)ib >= vertices.Length ||
                        (uint)ic >= vertices.Length) continue;
                    Vector3 a = vertices[ia];
                    Vector3 b = vertices[ib];
                    Vector3 c = vertices[ic];
                    float minimumX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - 0.0001f;
                    float maximumX = Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + 0.0001f;
                    float minimumZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - 0.0001f;
                    float maximumZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z)) + 0.0001f;
                    if (eastMeters < minimumX || eastMeters > maximumX ||
                        northMeters < minimumZ || northMeters > maximumZ) continue;

                    float denominator = (b.z - c.z) * (a.x - c.x) +
                                        (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(denominator) <= 0.000001f) continue;
                    float wa = ((b.z - c.z) * (eastMeters - c.x) +
                                (c.x - b.x) * (northMeters - c.z)) / denominator;
                    float wb = ((c.z - a.z) * (eastMeters - c.x) +
                                (a.x - c.x) * (northMeters - c.z)) / denominator;
                    float wc = 1f - wa - wb;
                    const float epsilon = 0.00005f;
                    if (wa < -epsilon || wb < -epsilon || wc < -epsilon) continue;

                    float height = wa * a.y + wb * b.y + wc * c.y;
                    if (found && height <= sampledHeight) continue;
                    sampledHeight = height;
                    sampledNormal = (wa * normals[ia] + wb * normals[ib] + wc * normals[ic]).normalized;
                    if (sampledNormal.sqrMagnitude < 0.5f) sampledNormal = Vector3.up;
                    found = true;
                }
                return found;
            }
        }

        public string schemaVersion = "2";
        public string sourceManifestSha256;
        public Vector2 macroMinimumEnuMeters = new Vector2(-12000f, -12000f);
        public Vector2 macroSizeMeters = new Vector2(24000f, 24000f);
        public GridLayer[] layers = Array.Empty<GridLayer>();
        public TrianglePatch[] transitionPatches = Array.Empty<TrianglePatch>();
        public SurfaceRegion[] regions = Array.Empty<SurfaceRegion>();

        public bool TrySample(float eastMeters, float northMeters, out ProductionTerrainSample sample)
        {
            GridLayer selected = null;
            if (layers != null)
            {
                for (int index = 0; index < layers.Length; index++)
                {
                    GridLayer candidate = layers[index];
                    if (candidate == null || !candidate.IsValid || !candidate.Contains(eastMeters, northMeters)) continue;
                    if (selected == null || candidate.priority < selected.priority) selected = candidate;
                }
            }

            TrianglePatch selectedPatch = null;
            float patchHeight = 0f;
            Vector3 patchNormal = Vector3.up;
            if (transitionPatches != null)
            {
                for (int index = 0; index < transitionPatches.Length; index++)
                {
                    TrianglePatch candidate = transitionPatches[index];
                    if (candidate == null || !candidate.IsValid ||
                        (selectedPatch != null && candidate.priority >= selectedPatch.priority) ||
                        !candidate.TrySample(eastMeters, northMeters, out float height, out Vector3 normal))
                        continue;
                    selectedPatch = candidate;
                    patchHeight = height;
                    patchNormal = normal;
                }
            }

            if (selected == null && selectedPatch == null)
            {
                sample = default;
                return false;
            }

            bool usePatch = selectedPatch != null &&
                            (selected == null || selectedPatch.priority < selected.priority);
            float sampledHeight;
            Vector3 sampledNormal;
            ProductionTerrainZone sampledZone;
            if (usePatch)
            {
                sampledHeight = patchHeight;
                sampledNormal = patchNormal;
                sampledZone = selectedPatch.zone;
            }
            else
            {
                selected.Sample(eastMeters, northMeters, out sampledHeight, out sampledNormal);
                sampledZone = selected.zone;
            }
            ProductionSurfaceCategory category = ProductionSurfaceCategory.DryPrairie;
            int categoryPriority = int.MaxValue;
            Vector2 point = new Vector2(eastMeters, northMeters);
            if (regions != null)
            {
                for (int index = 0; index < regions.Length; index++)
                {
                    SurfaceRegion region = regions[index];
                    if (region == null || region.priority >= categoryPriority || !region.Contains(point)) continue;
                    category = region.category;
                    categoryPriority = region.priority;
                }
            }

            Vector2 safeSize = new Vector2(
                Mathf.Max(macroSizeMeters.x, 0.001f),
                Mathf.Max(macroSizeMeters.y, 0.001f));
            Vector2 macroUv = new Vector2(
                (eastMeters - macroMinimumEnuMeters.x) / safeSize.x,
                (northMeters - macroMinimumEnuMeters.y) / safeSize.y);
            sample = new ProductionTerrainSample(sampledHeight, sampledNormal, sampledZone, macroUv, category);
            return true;
        }

        public bool TryBestFitPlane(IReadOnlyList<Vector2> footprintEnu, out ProductionTerrainPlane plane)
        {
            if (footprintEnu == null || footprintEnu.Count < 3)
            {
                plane = default;
                return false;
            }

            double sx = 0d, sz = 0d, sy = 0d;
            double sxx = 0d, szz = 0d, sxz = 0d, sxy = 0d, szy = 0d;
            int count = 0;
            for (int index = 0; index < footprintEnu.Count; index++)
            {
                Vector2 point = footprintEnu[index];
                if (!TrySample(point.x, point.y, out ProductionTerrainSample sample)) continue;
                double x = point.x;
                double z = point.y;
                double y = sample.heightMeters;
                sx += x;
                sz += z;
                sy += y;
                sxx += x * x;
                szz += z * z;
                sxz += x * z;
                sxy += x * y;
                szy += z * y;
                count++;
            }

            if (count < 3 || !Solve3x3(
                    sxx, sxz, sx,
                    sxz, szz, sz,
                    sx, sz, count,
                    sxy, szy, sy,
                    out double a, out double b, out double c))
            {
                plane = default;
                return false;
            }

            float maximumResidual = 0f;
            for (int index = 0; index < footprintEnu.Count; index++)
            {
                Vector2 point = footprintEnu[index];
                if (!TrySample(point.x, point.y, out ProductionTerrainSample sample)) continue;
                float fitted = (float)(a * point.x + b * point.y + c);
                maximumResidual = Mathf.Max(maximumResidual, Mathf.Abs(sample.heightMeters - fitted));
            }

            Vector3 center = new Vector3((float)(sx / count), 0f, (float)(sz / count));
            center.y = (float)(a * center.x + b * center.z + c);
            plane = new ProductionTerrainPlane(center, new Vector3((float)-a, 1f, (float)-b), maximumResidual);
            return true;
        }

        private static bool Solve3x3(
            double m00, double m01, double m02,
            double m10, double m11, double m12,
            double m20, double m21, double m22,
            double r0, double r1, double r2,
            out double x, out double y, out double z)
        {
            double determinant =
                m00 * (m11 * m22 - m12 * m21) -
                m01 * (m10 * m22 - m12 * m20) +
                m02 * (m10 * m21 - m11 * m20);
            if (Math.Abs(determinant) < 1e-9)
            {
                x = y = z = 0d;
                return false;
            }

            x = (r0 * (m11 * m22 - m12 * m21) - m01 * (r1 * m22 - m12 * r2) + m02 * (r1 * m21 - m11 * r2)) / determinant;
            y = (m00 * (r1 * m22 - m12 * r2) - r0 * (m10 * m22 - m12 * m20) + m02 * (m10 * r2 - r1 * m20)) / determinant;
            z = (m00 * (m11 * r2 - r1 * m21) - m01 * (m10 * r2 - r1 * m20) + r0 * (m10 * m21 - m11 * m20)) / determinant;
            return true;
        }
    }
}
