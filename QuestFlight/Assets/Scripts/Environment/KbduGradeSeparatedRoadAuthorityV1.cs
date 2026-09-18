using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Allocation-free evaluator for the offline-authored road/deck authority.
    /// The same serialized samples are evaluated by bridge mesh authoring,
    /// moving traffic, tests, and diagnostics. No physics or raycasts are used.
    /// </summary>
    public sealed class KbduGradeSeparatedRoadAuthorityV1
    {
        public const string ExpectedSchema = "questflight.grade-separated-road-authority.v1";
        public const string ExpectedStatus = "FULL_MAP_GRADE_SEPARATED_ROAD_AUTHORITY_V1";
        public const int ExpectedArchetypeCount = 6;

        [Serializable]
        public sealed class Document
        {
            public string schema;
            public string status;
            public string coordinate_frame;
            public int source_imagery_year;
            public float sample_spacing_meters;
            public float[] vehicle_contact_offsets_meters;
            public float[] vehicle_contact_half_lengths_meters;
            public string vehicle_contact_authority;
            public Profile[] profiles;
        }

        [Serializable]
        public sealed class Profile
        {
            public string span_id;
            public string source_osm_way_id;
            public string source_cdot_structure_id;
            public int source_imagery_year;
            public int source_year_built;
            public string source_facility;
            public string source_feature_intersected;
            public string lower_feature_kind;
            public string source_bridge;
            public string source_layer;
            public string source_tunnel;
            public string source_covered;
            public string source_embankment;
            public string source_cutting;
            public string source_bridge_name;
            public string source_bridge_structure;
            public string source_lanes;
            public string source_width;
            public string vertical_authority;
            public float minimum_clearance_meters;
            public float[] clearance_evidence_values_meters;
            public float deck_thickness_meters;
            public float surface_width_meters;
            public float profile_length_meters;
            public float[] bridge_interval_meters;
            public float[] approach_join_distances_meters;
            public float maximum_grade;
            public float minimum_xz_distance_meters;
            public string[] upper_route_ids;
            public string[] lower_route_ids;
            public float[] samples_xyz_meters;
            public float[] sample_normals_xyz;
            public float[] suppressed_old_visual_interval_meters;
            public string old_visual_suppression;
            public float validation_height_tolerance_meters;
        }

        public readonly struct SurfaceSample
        {
            public readonly Vector3 position;
            public readonly Vector3 forward;
            public readonly Vector3 normal;
            public readonly int profileIndex;
            public readonly float centerlineDistanceMeters;

            public SurfaceSample(
                Vector3 position,
                Vector3 forward,
                Vector3 normal,
                int profileIndex,
                float centerlineDistanceMeters)
            {
                this.position = position;
                this.forward = forward;
                this.normal = normal;
                this.profileIndex = profileIndex;
                this.centerlineDistanceMeters = centerlineDistanceMeters;
            }
        }

        private readonly Document document;
        private readonly Dictionary<string, int[]> profileIndicesByRoute;

        public Document Data => document;
        public int ProfileCount => document?.profiles?.Length ?? 0;
        public bool HasRouteProfile(string routeId) => profileIndicesByRoute.ContainsKey(routeId);

        public KbduGradeSeparatedRoadAuthorityV1(TextAsset serializedAuthority)
        {
            if (serializedAuthority == null)
                throw new ArgumentNullException(nameof(serializedAuthority));
            document = JsonUtility.FromJson<Document>(serializedAuthority.text);
            if (!TryValidate(out string report))
                throw new InvalidOperationException("Invalid grade-separated road authority: " + report);
            profileIndicesByRoute = BuildRouteIndex(document.profiles);
        }

        public float ContactOffsetMeters(int archetype)
        {
            if (archetype < 0 || archetype >= document.vehicle_contact_offsets_meters.Length)
                throw new ArgumentOutOfRangeException(nameof(archetype));
            return document.vehicle_contact_offsets_meters[archetype];
        }

        public float ContactHalfLengthMeters(int archetype)
        {
            if (archetype < 0 || archetype >= document.vehicle_contact_half_lengths_meters.Length)
                throw new ArgumentOutOfRangeException(nameof(archetype));
            return document.vehicle_contact_half_lengths_meters[archetype];
        }

        public bool TryEvaluate(
            string routeId,
            float x,
            float z,
            Vector3 horizontalForward,
            out SurfaceSample sample)
        {
            sample = default;
            if (string.IsNullOrEmpty(routeId) ||
                !profileIndicesByRoute.TryGetValue(routeId, out int[] profileIndices))
                return false;
            float highestSupportedHeight = float.NegativeInfinity;
            bool found = false;
            for (int candidate = 0; candidate < profileIndices.Length; candidate++)
            {
                float bestSquaredDistance = float.PositiveInfinity;
                bool profileFound = false;
                SurfaceSample profileSample = default;
                int profileIndex = profileIndices[candidate];
                Profile profile = document.profiles[profileIndex];
                float limitSquared = profile.minimum_xz_distance_meters * profile.minimum_xz_distance_meters;
                int pointCount = profile.samples_xyz_meters.Length / 3;
                for (int index = 0; index < pointCount - 1; index++)
                {
                    int offset = index * 3;
                    float ax = profile.samples_xyz_meters[offset];
                    float ay = profile.samples_xyz_meters[offset + 1];
                    float az = profile.samples_xyz_meters[offset + 2];
                    float bx = profile.samples_xyz_meters[offset + 3];
                    float by = profile.samples_xyz_meters[offset + 4];
                    float bz = profile.samples_xyz_meters[offset + 5];
                    float dx = bx - ax;
                    float dz = bz - az;
                    float lengthSquared = dx * dx + dz * dz;
                    if (lengthSquared <= 0.000001f) continue;
                    float projectedRatio = ((x - ax) * dx + (z - az) * dz) / lengthSquared;
                    float ratio = Mathf.Clamp01(projectedRatio);
                    float closestX = ax + dx * ratio;
                    float closestZ = az + dz * ratio;
                    float differenceX = x - closestX;
                    float differenceZ = z - closestZ;
                    float squaredDistance = differenceX * differenceX + differenceZ * differenceZ;
                    if (squaredDistance > limitSquared || squaredDistance >= bestSquaredDistance) continue;
                    float y = Mathf.LerpUnclamped(ay, by, ratio);
                    Vector3 tangent = new Vector3(dx, by - ay, dz).normalized;
                    if (Vector3.Dot(tangent, horizontalForward) < 0f) tangent = -tangent;
                    Vector3 normal = InterpolateNormal(profile, index, ratio);
                    Vector3 surfaceForward = Vector3.ProjectOnPlane(tangent, normal).normalized;
                    if (surfaceForward.sqrMagnitude < 0.5f) surfaceForward = tangent;
                    profileSample = new SurfaceSample(
                        new Vector3(x, y, z),
                        surfaceForward,
                        normal,
                        profileIndex,
                        Mathf.Sqrt(squaredDistance));
                    bestSquaredDistance = squaredDistance;
                    // Clamping must not invent a flat round cap beyond an authored
                    // approach's first/last cross-section. Keep its nearest distance
                    // for selection, but reject support outside the actual interval.
                    profileFound = !(index == 0 && projectedRatio < 0f ||
                        index == pointCount - 2 && projectedRatio > 1f);
                }
                // Overlapping opaque approach meshes share upper routes. Keep the
                // nearest sample on each profile, then choose the visible upper road.
                if (profileFound && profileSample.position.y > highestSupportedHeight)
                {
                    highestSupportedHeight = profileSample.position.y;
                    sample = profileSample;
                    found = true;
                }
            }
            return found;
        }

        public static bool TryEvaluateProfile(
            Profile profile,
            float distanceMeters,
            out Vector3 position,
            out Vector3 normal)
        {
            position = default;
            normal = Vector3.up;
            if (profile?.samples_xyz_meters == null || profile.samples_xyz_meters.Length < 6)
                return false;
            float remaining = Mathf.Clamp(distanceMeters, 0f, profile.profile_length_meters);
            int pointCount = profile.samples_xyz_meters.Length / 3;
            for (int index = 0; index < pointCount - 1; index++)
            {
                int offset = index * 3;
                Vector3 a = new Vector3(
                    profile.samples_xyz_meters[offset],
                    profile.samples_xyz_meters[offset + 1],
                    profile.samples_xyz_meters[offset + 2]);
                Vector3 b = new Vector3(
                    profile.samples_xyz_meters[offset + 3],
                    profile.samples_xyz_meters[offset + 4],
                    profile.samples_xyz_meters[offset + 5]);
                float segment = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                if (remaining > segment && index < pointCount - 2)
                {
                    remaining -= segment;
                    continue;
                }
                float ratio = segment > 0.0001f ? Mathf.Clamp01(remaining / segment) : 0f;
                position = Vector3.LerpUnclamped(a, b, ratio);
                normal = InterpolateNormal(profile, index, ratio);
                return true;
            }
            return false;
        }

        public bool TryValidate(out string report)
        {
            if (document == null ||
                !string.Equals(document.schema, ExpectedSchema, StringComparison.Ordinal) ||
                !string.Equals(document.status, ExpectedStatus, StringComparison.Ordinal) ||
                document.vehicle_contact_offsets_meters == null ||
                document.vehicle_contact_offsets_meters.Length != ExpectedArchetypeCount ||
                document.vehicle_contact_half_lengths_meters == null ||
                document.vehicle_contact_half_lengths_meters.Length != ExpectedArchetypeCount ||
                document.profiles == null || document.profiles.Length == 0)
            {
                report = "header, contact offset, or profile inventory mismatch";
                return false;
            }
            for (int archetype = 0; archetype < ExpectedArchetypeCount; archetype++)
            {
                if (!IsFinite(document.vehicle_contact_offsets_meters[archetype]) ||
                    !IsFinite(document.vehicle_contact_half_lengths_meters[archetype]) ||
                    document.vehicle_contact_half_lengths_meters[archetype] <= 0f)
                {
                    report = "invalid vehicle contact geometry";
                    return false;
                }
            }
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int profileIndex = 0; profileIndex < document.profiles.Length; profileIndex++)
            {
                Profile profile = document.profiles[profileIndex];
                int pointCount = profile?.samples_xyz_meters?.Length / 3 ?? 0;
                if (profile == null || string.IsNullOrWhiteSpace(profile.span_id) ||
                    !ids.Add(profile.span_id) || profile.surface_width_meters <= 0f ||
                    (profile.minimum_clearance_meters <= 0f && profile.lower_feature_kind != "railway") || profile.maximum_grade < 0f ||
                    profile.maximum_grade > 0.15f || pointCount < 2 ||
                    profile.samples_xyz_meters.Length != pointCount * 3 ||
                    profile.sample_normals_xyz == null ||
                    profile.sample_normals_xyz.Length != pointCount * 3 ||
                    profile.upper_route_ids == null || profile.upper_route_ids.Length == 0 ||
                    profile.lower_route_ids == null || (profile.lower_route_ids.Length == 0 && profile.lower_feature_kind != "railway") ||
                    profile.bridge_interval_meters == null || profile.bridge_interval_meters.Length != 2)
                {
                    report = "invalid profile " + profileIndex;
                    return false;
                }
                for (int index = 0; index < profile.samples_xyz_meters.Length; index++)
                {
                    if (!IsFinite(profile.samples_xyz_meters[index]) ||
                        !IsFinite(profile.sample_normals_xyz[index]))
                    {
                        report = "nonfinite profile " + profileIndex;
                        return false;
                    }
                }
            }
            report = $"profiles={document.profiles.Length}, archetypeOffsets={ExpectedArchetypeCount}";
            return true;
        }

        private static Dictionary<string, int[]> BuildRouteIndex(Profile[] profiles)
        {
            Dictionary<string, List<int>> temporary =
                new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int profileIndex = 0; profileIndex < profiles.Length; profileIndex++)
            {
                string[] routeIds = profiles[profileIndex].upper_route_ids;
                for (int routeIndex = 0; routeIndex < routeIds.Length; routeIndex++)
                {
                    string routeId = routeIds[routeIndex];
                    if (!temporary.TryGetValue(routeId, out List<int> indices))
                    {
                        indices = new List<int>();
                        temporary.Add(routeId, indices);
                    }
                    if (!indices.Contains(profileIndex)) indices.Add(profileIndex);
                }
            }
            Dictionary<string, int[]> result = new Dictionary<string, int[]>(temporary.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<int>> pair in temporary)
                result.Add(pair.Key, pair.Value.ToArray());
            return result;
        }

        private static Vector3 InterpolateNormal(Profile profile, int segmentIndex, float ratio)
        {
            int offset = segmentIndex * 3;
            Vector3 a = new Vector3(
                profile.sample_normals_xyz[offset],
                profile.sample_normals_xyz[offset + 1],
                profile.sample_normals_xyz[offset + 2]);
            Vector3 b = new Vector3(
                profile.sample_normals_xyz[offset + 3],
                profile.sample_normals_xyz[offset + 4],
                profile.sample_normals_xyz[offset + 5]);
            Vector3 result = Vector3.LerpUnclamped(a, b, ratio).normalized;
            return result.sqrMagnitude > 0.5f && result.y > 0f ? result : Vector3.up;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
