using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuestFlight.Editor
{
    public readonly struct FullMapBuildingRoofBudgetCandidate
    {
        public readonly int buildingId;
        public readonly int pageIndex;
        public readonly int addedTriangles;
        public readonly int acceptanceDistanceCentimeters;
        public readonly int routeDistanceCentimeters;
        public readonly bool residential;
        public readonly int areaDecimetersSquared;
        public readonly int heightDecimeters;
        public readonly int riseDecimeters;
        public readonly int semanticConfidence;
        public readonly int materialConfidence;
        public readonly string sourceId;
        public readonly int component;

        public FullMapBuildingRoofBudgetCandidate(
            int buildingId,
            int pageIndex,
            int addedTriangles,
            int acceptanceDistanceCentimeters,
            int routeDistanceCentimeters,
            bool residential,
            int areaDecimetersSquared,
            int heightDecimeters,
            int riseDecimeters,
            int semanticConfidence,
            int materialConfidence,
            string sourceId,
            int component)
        {
            this.buildingId = buildingId;
            this.pageIndex = pageIndex;
            this.addedTriangles = addedTriangles;
            this.acceptanceDistanceCentimeters = acceptanceDistanceCentimeters;
            this.routeDistanceCentimeters = routeDistanceCentimeters;
            this.residential = residential;
            this.areaDecimetersSquared = areaDecimetersSquared;
            this.heightDecimeters = heightDecimeters;
            this.riseDecimeters = riseDecimeters;
            this.semanticConfidence = semanticConfidence;
            this.materialConfidence = materialConfidence;
            this.sourceId = sourceId ?? string.Empty;
            this.component = component;
        }
    }

    public sealed class FullMapBuildingRoofBudgetResult
    {
        public readonly int[] selectedBuildingIds;
        public readonly int[] pageTriangleTotals;
        public readonly int maximumSelectedTriangles;

        public FullMapBuildingRoofBudgetResult(
            int[] selectedBuildingIds,
            int[] pageTriangleTotals,
            int maximumSelectedTriangles)
        {
            this.selectedBuildingIds = selectedBuildingIds;
            this.pageTriangleTotals = pageTriangleTotals;
            this.maximumSelectedTriangles = maximumSelectedTriangles;
        }
    }

    /// <summary>
    /// Pure deterministic greedy allocator. Candidate order is based only on
    /// quantized authored data, route/view distances, and stable source identity.
    /// Every admission is checked against the conservative heaviest-page contract.
    /// </summary>
    public static class FullMapBuildingRoofBudgetAllocator
    {
        public static FullMapBuildingRoofBudgetResult Select(
            IEnumerable<FullMapBuildingRoofBudgetCandidate> sourceCandidates,
            IReadOnlyList<int> baselinePageTriangles,
            int activePageSlots,
            int triangleCeiling)
        {
            if (sourceCandidates == null) throw new ArgumentNullException(nameof(sourceCandidates));
            if (baselinePageTriangles == null) throw new ArgumentNullException(nameof(baselinePageTriangles));
            if (activePageSlots <= 0 || activePageSlots > baselinePageTriangles.Count)
                throw new ArgumentOutOfRangeException(nameof(activePageSlots));

            int[] pageTotals = baselinePageTriangles.ToArray();
            int baselineMaximum = ConservativeMaximum(pageTotals, activePageSlots);
            if (baselineMaximum > triangleCeiling)
                throw new InvalidOperationException(
                    $"Baseline roof allocation already exceeds {triangleCeiling:N0}: {baselineMaximum:N0}.");

            FullMapBuildingRoofBudgetCandidate[] candidates = sourceCandidates.ToArray();
            Array.Sort(candidates, Compare);
            HashSet<int> seenBuildingIds = new HashSet<int>();
            List<int> selected = new List<int>(candidates.Length);
            foreach (FullMapBuildingRoofBudgetCandidate candidate in candidates)
            {
                if (!seenBuildingIds.Add(candidate.buildingId))
                    throw new InvalidOperationException("Duplicate roof candidate building id: " + candidate.buildingId);
                if (candidate.pageIndex < 0 || candidate.pageIndex >= pageTotals.Length)
                    throw new InvalidOperationException("Roof candidate page index is out of range: " + candidate.pageIndex);
                if (candidate.addedTriangles <= 0)
                    throw new InvalidOperationException("Roof candidate triangle delta must be positive.");

                pageTotals[candidate.pageIndex] += candidate.addedTriangles;
                if (ConservativeMaximum(pageTotals, activePageSlots) <= triangleCeiling)
                    selected.Add(candidate.buildingId);
                else
                    pageTotals[candidate.pageIndex] -= candidate.addedTriangles;
            }

            selected.Sort();
            return new FullMapBuildingRoofBudgetResult(
                selected.ToArray(), pageTotals,
                ConservativeMaximum(pageTotals, activePageSlots));
        }

        public static int ConservativeMaximum(IEnumerable<int> pageTriangleTotals, int activePageSlots) =>
            pageTriangleTotals.OrderByDescending(value => value).Take(activePageSlots).Sum();

        private static int Compare(
            FullMapBuildingRoofBudgetCandidate a,
            FullMapBuildingRoofBudgetCandidate b)
        {
            int result = a.acceptanceDistanceCentimeters.CompareTo(b.acceptanceDistanceCentimeters);
            if (result != 0) return result;
            result = a.routeDistanceCentimeters.CompareTo(b.routeDistanceCentimeters);
            if (result != 0) return result;
            result = b.residential.CompareTo(a.residential);
            if (result != 0) return result;
            result = b.areaDecimetersSquared.CompareTo(a.areaDecimetersSquared);
            if (result != 0) return result;
            result = b.heightDecimeters.CompareTo(a.heightDecimeters);
            if (result != 0) return result;
            result = b.riseDecimeters.CompareTo(a.riseDecimeters);
            if (result != 0) return result;
            result = b.semanticConfidence.CompareTo(a.semanticConfidence);
            if (result != 0) return result;
            result = b.materialConfidence.CompareTo(a.materialConfidence);
            if (result != 0) return result;
            result = a.addedTriangles.CompareTo(b.addedTriangles);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.sourceId, b.sourceId);
            if (result != 0) return result;
            result = a.component.CompareTo(b.component);
            return result != 0 ? result : a.buildingId.CompareTo(b.buildingId);
        }
    }

    public enum FullMapBuildingRoofTopologyKind
    {
        None = 0,
        Gable = 1,
        CompactHip = 2,
        ShortRidgeHip = 3
    }

    public readonly struct FullMapBuildingGableInfill
    {
        public readonly int edgeStartIndex;
        public readonly int edgeEndIndex;
        public readonly int ridgeVertexIndex;

        public FullMapBuildingGableInfill(int edgeStartIndex, int edgeEndIndex, int ridgeVertexIndex)
        {
            this.edgeStartIndex = edgeStartIndex;
            this.edgeEndIndex = edgeEndIndex;
            this.ridgeVertexIndex = ridgeVertexIndex;
        }
    }

    public sealed class FullMapBuildingRoofTopologyResult
    {
        public readonly FullMapBuildingRoofTopologyKind kind;
        public readonly Vector3[] vertices;
        public readonly int[] roofTriangles;
        public readonly FullMapBuildingGableInfill[] gableInfills;
        public readonly float aspectRatio;

        public int RoofTriangleCount => roofTriangles.Length / 3;
        public int GableInfillTriangleCount => gableInfills.Length;
        public int TotalTriangleCount => RoofTriangleCount + GableInfillTriangleCount;
        public int AddedTriangleCount => TotalTriangleCount - 2;

        public FullMapBuildingRoofTopologyResult(
            FullMapBuildingRoofTopologyKind kind,
            Vector3[] vertices,
            int[] roofTriangles,
            FullMapBuildingGableInfill[] gableInfills,
            float aspectRatio)
        {
            this.kind = kind;
            this.vertices = vertices;
            this.roofTriangles = roofTriangles;
            this.gableInfills = gableInfills;
            this.aspectRatio = aspectRatio;
        }
    }

    /// <summary>
    /// Pure deterministic topology builder for conservative four-corner roofs.
    /// It deliberately rejects quadrilaterals whose conventional roof orientation
    /// would be ambiguous instead of inventing architecture for uncertain shells.
    /// </summary>
    public static class FullMapBuildingRoofTopology
    {
        public const int GableRoofId = 1;
        public const int HipRoofId = 2;
        public const float MinimumEdgeMeters = 1.5f;
        public const float MinimumAreaSquareMeters = 6f;
        public const float MinimumCornerTurnDegrees = 65f;
        public const float MaximumCornerTurnDegrees = 115f;
        public const float MaximumOppositeEdgeAngleDegrees = 12f;
        public const float MaximumOppositeLengthRatio = 1.25f;
        public const float MinimumGableAspectRatio = 1.12f;
        public const float CompactHipMaximumAspectRatio = 1.35f;
        public const float MaximumAspectRatio = 6f;

        public static bool TryCreate(
            IReadOnlyList<Vector2> source,
            int roofId,
            float wallTop,
            float rise,
            bool preserveExactLandmark,
            out FullMapBuildingRoofTopologyResult result,
            out string rejectionReason)
        {
            result = null;
            rejectionReason = string.Empty;
            if (preserveExactLandmark)
                return Reject("exact-landmark", out rejectionReason);
            if (roofId != GableRoofId && roofId != HipRoofId)
                return Reject("unsupported-roof", out rejectionReason);
            if (!IsFinite(wallTop) || !IsFinite(rise) || rise <= 0f)
                return Reject("nonpositive-rise", out rejectionReason);
            if (source == null || source.Count != 4)
                return Reject("not-four-corner", out rejectionReason);

            Vector2[] polygon = source.ToArray();
            if (polygon.Any(point => !IsFinite(point.x) || !IsFinite(point.y)))
                return Reject("nonfinite-point", out rejectionReason);
            float area = SignedArea(polygon);
            if (!IsFinite(area) || Mathf.Abs(area) < MinimumAreaSquareMeters)
                return Reject("small-or-degenerate-area", out rejectionReason);
            if (area < 0f) Array.Reverse(polygon);
            if (NonAdjacentEdgesIntersect(polygon))
                return Reject("self-intersection", out rejectionReason);

            Vector2[] edges = new Vector2[4];
            float[] lengths = new float[4];
            for (int index = 0; index < 4; index++)
            {
                edges[index] = polygon[(index + 1) % 4] - polygon[index];
                lengths[index] = edges[index].magnitude;
                if (!IsFinite(lengths[index]) || lengths[index] < MinimumEdgeMeters)
                    return Reject("short-edge", out rejectionReason);
                edges[index] /= lengths[index];

                Vector2 next = polygon[(index + 2) % 4] - polygon[(index + 1) % 4];
                float nextLength = next.magnitude;
                if (nextLength < MinimumEdgeMeters) return Reject("short-edge", out rejectionReason);
                next /= nextLength;
                float turn = Mathf.Atan2(Cross(edges[index], next), Vector2.Dot(edges[index], next)) *
                             Mathf.Rad2Deg;
                if (!IsFinite(turn) || turn < MinimumCornerTurnDegrees || turn > MaximumCornerTurnDegrees)
                    return Reject("unsafe-corner-angle", out rejectionReason);
            }

            float minimumOppositeDot = Mathf.Cos(MaximumOppositeEdgeAngleDegrees * Mathf.Deg2Rad);
            if (Mathf.Abs(Vector2.Dot(edges[0], edges[2])) < minimumOppositeDot ||
                Mathf.Abs(Vector2.Dot(edges[1], edges[3])) < minimumOppositeDot)
                return Reject("nonparallel-opposites", out rejectionReason);
            if (Ratio(lengths[0], lengths[2]) > MaximumOppositeLengthRatio ||
                Ratio(lengths[1], lengths[3]) > MaximumOppositeLengthRatio)
                return Reject("unequal-opposites", out rejectionReason);

            float pair02 = (lengths[0] + lengths[2]) * 0.5f;
            float pair13 = (lengths[1] + lengths[3]) * 0.5f;
            float longLength = Mathf.Max(pair02, pair13);
            float shortLength = Mathf.Min(pair02, pair13);
            float aspect = longLength / Mathf.Max(0.001f, shortLength);
            if (!IsFinite(aspect) || aspect > MaximumAspectRatio)
                return Reject("unsafe-aspect", out rejectionReason);
            if (roofId == GableRoofId && aspect < MinimumGableAspectRatio)
                return Reject("ambiguous-gable-axis", out rejectionReason);

            int[] eaveEdges = pair02 >= pair13 ? new[] { 0, 2 } : new[] { 1, 3 };
            int[] endEdges = pair02 >= pair13 ? new[] { 1, 3 } : new[] { 0, 2 };
            List<Vector3> vertices = polygon.Select(point => new Vector3(point.x, wallTop, point.y)).ToList();
            List<int> roofTriangles = new List<int>(18);
            List<FullMapBuildingGableInfill> gableInfills = new List<FullMapBuildingGableInfill>(2);

            if (roofId == GableRoofId)
            {
                Dictionary<int, int> ridgeForEnd = AddEndRidgeVertices(
                    polygon, endEdges, wallTop, rise, vertices, false, longLength, shortLength);
                AddSideFields(eaveEdges, ridgeForEnd, vertices, roofTriangles);
                foreach (int endEdge in endEdges)
                {
                    int start = endEdge;
                    int end = (endEdge + 1) % 4;
                    gableInfills.Add(new FullMapBuildingGableInfill(start, end, ridgeForEnd[endEdge]));
                }
                result = new FullMapBuildingRoofTopologyResult(
                    FullMapBuildingRoofTopologyKind.Gable, vertices.ToArray(), roofTriangles.ToArray(),
                    gableInfills.ToArray(), aspect);
                return true;
            }

            if (aspect <= CompactHipMaximumAspectRatio)
            {
                Vector2 center = polygon.Aggregate(Vector2.zero, (sum, point) => sum + point) * 0.25f;
                int apex = vertices.Count;
                vertices.Add(new Vector3(center.x, wallTop + rise, center.y));
                for (int edge = 0; edge < 4; edge++)
                    AddUpwardTriangle(vertices, roofTriangles, edge, (edge + 1) % 4, apex);
                result = new FullMapBuildingRoofTopologyResult(
                    FullMapBuildingRoofTopologyKind.CompactHip, vertices.ToArray(), roofTriangles.ToArray(),
                    Array.Empty<FullMapBuildingGableInfill>(), aspect);
                return true;
            }

            Dictionary<int, int> shortRidgeForEnd = AddEndRidgeVertices(
                polygon, endEdges, wallTop, rise, vertices, true, longLength, shortLength);
            AddSideFields(eaveEdges, shortRidgeForEnd, vertices, roofTriangles);
            foreach (int endEdge in endEdges)
                AddUpwardTriangle(vertices, roofTriangles,
                    endEdge, (endEdge + 1) % 4, shortRidgeForEnd[endEdge]);
            result = new FullMapBuildingRoofTopologyResult(
                FullMapBuildingRoofTopologyKind.ShortRidgeHip, vertices.ToArray(), roofTriangles.ToArray(),
                Array.Empty<FullMapBuildingGableInfill>(), aspect);
            return true;
        }

        private static Dictionary<int, int> AddEndRidgeVertices(
            IReadOnlyList<Vector2> polygon,
            IEnumerable<int> endEdges,
            float wallTop,
            float rise,
            ICollection<Vector3> vertices,
            bool insetForHip,
            float longLength,
            float shortLength)
        {
            Vector2 center = polygon.Aggregate(Vector2.zero, (sum, point) => sum + point) * 0.25f;
            float halfRidge = Mathf.Max(0f, (longLength - shortLength) * 0.5f);
            Dictionary<int, int> result = new Dictionary<int, int>();
            foreach (int edge in endEdges.OrderBy(value => value))
            {
                Vector2 edgeCenter = (polygon[edge] + polygon[(edge + 1) % 4]) * 0.5f;
                Vector2 horizontal = edgeCenter;
                if (insetForHip)
                {
                    Vector2 direction = (edgeCenter - center).normalized;
                    horizontal = center + direction * halfRidge;
                }
                int vertex = vertices.Count;
                vertices.Add(new Vector3(horizontal.x, wallTop + rise, horizontal.y));
                result.Add(edge, vertex);
            }
            return result;
        }

        private static void AddSideFields(
            IEnumerable<int> eaveEdges,
            IReadOnlyDictionary<int, int> ridgeForEnd,
            IReadOnlyList<Vector3> vertices,
            ICollection<int> triangles)
        {
            foreach (int edge in eaveEdges.OrderBy(value => value))
            {
                int start = edge;
                int end = (edge + 1) % 4;
                int ridgeAtStart = ridgeForEnd[(edge + 3) % 4];
                int ridgeAtEnd = ridgeForEnd[(edge + 1) % 4];
                AddUpwardTriangle(vertices, triangles, start, end, ridgeAtEnd);
                AddUpwardTriangle(vertices, triangles, start, ridgeAtEnd, ridgeAtStart);
            }
        }

        private static void AddUpwardTriangle(
            IReadOnlyList<Vector3> vertices,
            ICollection<int> triangles,
            int a,
            int b,
            int c)
        {
            Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (normal.y < 0f) (b, c) = (c, b);
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        private static bool NonAdjacentEdgesIntersect(IReadOnlyList<Vector2> polygon) =>
            SegmentsIntersect(polygon[0], polygon[1], polygon[2], polygon[3]) ||
            SegmentsIntersect(polygon[1], polygon[2], polygon[3], polygon[0]);

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross(b - a, c - a);
            float abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c);
            float cdB = Cross(d - c, b - c);
            return abC * abD <= 0f && cdA * cdB <= 0f;
        }

        private static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            double twiceArea = 0d;
            Vector2 origin = polygon[0];
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 a = polygon[index] - origin;
                Vector2 b = polygon[(index + 1) % polygon.Count] - origin;
                twiceArea += (double)a.x * b.y - (double)a.y * b.x;
            }
            return (float)(twiceArea * 0.5d);
        }

        private static float Ratio(float a, float b) =>
            Mathf.Max(a, b) / Mathf.Max(0.001f, Mathf.Min(a, b));

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool Reject(string reason, out string rejectionReason)
        {
            rejectionReason = reason;
            return false;
        }
    }
}
