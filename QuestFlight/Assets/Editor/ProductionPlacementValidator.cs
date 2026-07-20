using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    public static class ProductionPlacementValidator
    {
        private const string SurfacePath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset";
        private const string PlacementPath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionPlacementManifestV2.asset";
        private const string StandPath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_AirportParkingStandManifestV2.asset";

        [Serializable]
        public sealed class Report
        {
            public string classification;
            public bool passed;
            public int totalPlacements;
            public int buildings;
            public int trees;
            public int parkedAircraft;
            public int duplicateStableIds;
            public int missingFootprints;
            public int invalidLicenses;
            public int unvalidatedVegetationExclusions;
            public float buildingMedianFoundationErrorMeters;
            public float buildingP95FoundationErrorMeters;
            public float buildingMaximumFoundationErrorMeters;
            public float treeMinimumRootOffsetMeters;
            public float treeMaximumRootOffsetMeters;
            public float parkedAircraftMaximumGearErrorMeters;
            public float parkedAircraftMaximumHeadingErrorDegrees;
            public string[] violations = Array.Empty<string>();
            public string evidenceBoundary =
                "Machine placement evidence only; this report does not prove visual quality, stereo comfort, or Quest performance.";
        }

        public static Report Validate()
        {
            ProductionTerrainSurfaceAsset surface = LoadRequired<ProductionTerrainSurfaceAsset>(SurfacePath);
            ProductionPlacementManifest manifest = LoadRequired<ProductionPlacementManifest>(PlacementPath);
            AirportParkingStandManifest stands = LoadRequired<AirportParkingStandManifest>(StandPath);
            List<string> violations = new List<string>();
            List<float> buildingErrors = new List<float>();
            List<float> treeOffsets = new List<float>();
            float maximumGearError = 0f;
            float maximumHeadingError = 0f;
            int missingFootprints = 0;
            int invalidLicenses = 0;
            int unvalidatedVegetation = 0;
            HashSet<string> stableIds = new HashSet<string>(StringComparer.Ordinal);
            int duplicates = 0;
            Dictionary<string, AirportParkingStandManifest.Stand> standById = stands.stands
                .ToDictionary(item => item.standId, StringComparer.Ordinal);

            foreach (ProductionPlacementManifest.Entry entry in manifest.entries)
            {
                if (!stableIds.Add(entry.stableId))
                {
                    duplicates++;
                    AddViolation(violations, "duplicate stable ID: " + entry.stableId);
                }
                if (string.IsNullOrWhiteSpace(entry.sourceLicenseClass))
                {
                    invalidLicenses++;
                    AddViolation(violations, "missing source/license class: " + entry.stableId);
                }
                if (entry.footprintEnuMeters == null || entry.footprintEnuMeters.Length < 3)
                {
                    missingFootprints++;
                    AddViolation(violations, "missing footprint: " + entry.stableId);
                    continue;
                }

                if (entry.category == ProductionPlacementCategory.Building)
                {
                    if (!surface.TryBestFitPlane(entry.footprintEnuMeters, out ProductionTerrainPlane plane))
                    {
                        AddViolation(violations, "building foundation cannot be sampled: " + entry.stableId);
                        continue;
                    }
                    float error = Mathf.Abs(entry.terrainHeightMeters - plane.HeightAt(entry.localEnuMeters.x, entry.localEnuMeters.y));
                    buildingErrors.Add(error);
                    if (error > 0.15f)
                        AddViolation(violations, $"building center foundation error {error:0.000} m: {entry.stableId}");
                    if (entry.foundationMode == ProductionFoundationMode.BestFitPlane &&
                        entry.foundationMaximumResidualMeters > 0.15f)
                        AddViolation(violations, $"building best-fit residual {entry.foundationMaximumResidualMeters:0.000} m lacks skirt: {entry.stableId}");
                }
                else if (entry.category == ProductionPlacementCategory.Tree || entry.category == ProductionPlacementCategory.Shrub)
                {
                    treeOffsets.Add(entry.contactPointOffsetMeters);
                    if (entry.contactPointOffsetMeters < -0.12f || entry.contactPointOffsetMeters > 0.03f)
                        AddViolation(violations, $"vegetation root offset {entry.contactPointOffsetMeters:0.000} m: {entry.stableId}");
                    if (!string.Equals(entry.placementValidationStatus,
                            "V2_MASK_AND_EXCLUSIONS_VALIDATED", StringComparison.Ordinal))
                        unvalidatedVegetation++;
                }
                else if (entry.category == ProductionPlacementCategory.ParkedAircraft)
                {
                    if (!standById.TryGetValue(entry.sourceFeatureId ?? string.Empty, out AirportParkingStandManifest.Stand stand))
                    {
                        AddViolation(violations, "parked aircraft has no authored stand: " + entry.stableId);
                        continue;
                    }
                    float effectiveHeading = entry.authoredHeadingDegrees - entry.modelForwardAxisCorrectionDegrees;
                    float headingError = Mathf.Abs(Mathf.DeltaAngle(stand.headingDegrees, effectiveHeading));
                    maximumHeadingError = Mathf.Max(maximumHeadingError, headingError);
                    if (headingError > 3f)
                        AddViolation(violations, $"parked aircraft heading error {headingError:0.000} deg: {entry.stableId}");
                    Quaternion rotation = Quaternion.Euler(0f, entry.authoredHeadingDegrees, 0f);
                    foreach (Vector3 contact in entry.modelContactPoints ?? Array.Empty<Vector3>())
                    {
                        Vector3 offset = rotation * Vector3.Scale(contact, entry.scale);
                        float east = entry.localEnuMeters.x + offset.x;
                        float north = entry.localEnuMeters.y + offset.z;
                        float planeHeight = PlaneHeight(stand.apronPlanePoint, stand.apronPlaneNormal, east, north);
                        float worldHeight = entry.terrainHeightMeters + entry.contactPointOffsetMeters + offset.y;
                        float gearError = Mathf.Abs(worldHeight - planeHeight);
                        maximumGearError = Mathf.Max(maximumGearError, gearError);
                        if (gearError > 0.03f)
                            AddViolation(violations, $"parked aircraft gear error {gearError:0.000} m: {entry.stableId}");
                    }
                }
            }

            buildingErrors.Sort();
            Report report = new Report
            {
                totalPlacements = manifest.entries.Length,
                buildings = manifest.entries.Count(entry => entry.category == ProductionPlacementCategory.Building),
                trees = manifest.entries.Count(entry => entry.category == ProductionPlacementCategory.Tree || entry.category == ProductionPlacementCategory.Shrub),
                parkedAircraft = manifest.entries.Count(entry => entry.category == ProductionPlacementCategory.ParkedAircraft),
                duplicateStableIds = duplicates,
                missingFootprints = missingFootprints,
                invalidLicenses = invalidLicenses,
                unvalidatedVegetationExclusions = unvalidatedVegetation,
                buildingMedianFoundationErrorMeters = Percentile(buildingErrors, 0.5f),
                buildingP95FoundationErrorMeters = Percentile(buildingErrors, 0.95f),
                buildingMaximumFoundationErrorMeters = buildingErrors.Count == 0 ? 0f : buildingErrors[buildingErrors.Count - 1],
                treeMinimumRootOffsetMeters = treeOffsets.Count == 0 ? 0f : treeOffsets.Min(),
                treeMaximumRootOffsetMeters = treeOffsets.Count == 0 ? 0f : treeOffsets.Max(),
                parkedAircraftMaximumGearErrorMeters = maximumGearError,
                parkedAircraftMaximumHeadingErrorDegrees = maximumHeadingError,
                violations = violations.ToArray()
            };
            bool numericTolerancesPass = duplicates == 0 && missingFootprints == 0 && invalidLicenses == 0 &&
                                         report.buildingMedianFoundationErrorMeters <= 0.03f &&
                                         report.buildingP95FoundationErrorMeters <= 0.08f &&
                                         report.buildingMaximumFoundationErrorMeters <= 0.15f &&
                                         maximumGearError <= 0.03f && maximumHeadingError <= 3f;
            report.passed = numericTolerancesPass && unvalidatedVegetation == 0 && violations.Count == 0;
            report.classification = report.passed ? "PASS_PLACEMENT_VALIDATION" : "FAIL_PLACEMENT_VALIDATION";
            if (unvalidatedVegetation > 0)
                AddViolation(violations, $"{unvalidatedVegetation} vegetation placements lack V2 exclusion-mask validation");
            report.violations = violations.ToArray();
            return report;
        }

        public static void ValidateBatch()
        {
            string output = Argument("-placementOutput");
            if (string.IsNullOrWhiteSpace(output))
                output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../placement_validation"));
            Directory.CreateDirectory(output);
            Report report = Validate();
            File.WriteAllText(Path.Combine(output, "placement_validation.json"), JsonUtility.ToJson(report, true) + Environment.NewLine);
            File.WriteAllText(Path.Combine(output, "placement_validation_summary.md"), BuildMarkdown(report));
            File.WriteAllText(Path.Combine(output, "placement_validation.csv"), BuildCsv());
            Debug.Log("[QuestFlight][ProductionPlacementValidator] " + report.classification);
            EditorApplication.Exit(report.passed ? 0 : 2);
        }

        private static string BuildCsv()
        {
            ProductionPlacementManifest manifest = LoadRequired<ProductionPlacementManifest>(PlacementPath);
            StringBuilder csv = new StringBuilder("stable_id,category,east_meters,north_meters,height_meters,foundation_mode,lod_class,hlod_cell,license\n");
            foreach (ProductionPlacementManifest.Entry entry in manifest.entries)
            {
                csv.Append(Csv(entry.stableId)).Append(',').Append(entry.category).Append(',')
                    .Append(entry.localEnuMeters.x.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append(entry.localEnuMeters.y.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append(entry.terrainHeightMeters.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append(entry.foundationMode).Append(',').Append(entry.lodClass).Append(',')
                    .Append(Csv(entry.hlodCellId)).Append(',').Append(Csv(entry.sourceLicenseClass)).AppendLine();
            }
            return csv.ToString();
        }

        private static string BuildMarkdown(Report report) =>
            $"# Production placement validation\n\n" +
            $"- Classification: `{report.classification}`\n" +
            $"- Placements/buildings/trees/parked aircraft: {report.totalPlacements:N0}/{report.buildings:N0}/{report.trees:N0}/{report.parkedAircraft:N0}\n" +
            $"- Building foundation median/p95/max: {report.buildingMedianFoundationErrorMeters:0.000}/{report.buildingP95FoundationErrorMeters:0.000}/{report.buildingMaximumFoundationErrorMeters:0.000} m\n" +
            $"- Aircraft maximum gear/heading error: {report.parkedAircraftMaximumGearErrorMeters:0.000} m / {report.parkedAircraftMaximumHeadingErrorDegrees:0.000} deg\n" +
            $"- Unvalidated vegetation exclusions: {report.unvalidatedVegetationExclusions:N0}\n\n" +
            "Machine placement evidence only; visual quality and Quest performance remain separate gates.\n";

        private static float PlaneHeight(Vector3 point, Vector3 normal, float east, float north) =>
            Mathf.Abs(normal.y) < 0.000001f
                ? point.y
                : point.y - (normal.x * (east - point.x) + normal.z * (north - point.z)) / normal.y;

        private static float Percentile(List<float> sorted, float percentile)
        {
            if (sorted.Count == 0) return 0f;
            int index = Mathf.Clamp(Mathf.CeilToInt(percentile * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[index];
        }

        private static void AddViolation(List<string> violations, string value)
        {
            if (violations.Count < 200) violations.Add(value);
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int index = 0; index < args.Length - 1; index++)
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
            return string.Empty;
        }

        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null) throw new InvalidOperationException("Required placement asset is missing: " + path);
            return value;
        }
    }
}
