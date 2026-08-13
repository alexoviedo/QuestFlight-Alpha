#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace T2.QuestFlight.Editor
{
    /// <summary>
    /// Exports and classifies the serialized dependency closure of the one
    /// product scene after Unity has restored packages and imported assets.
    /// This is the authoritative complement to the Git-ref-only extractor.
    /// </summary>
    public static class ProductionDependencyClosureExporter
    {
        public const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        public const string SourceCommit = "cd4131fa7732a6db2d7387921dfef33e2c206970";

        private static readonly string[] ForbiddenFragments =
        {
            "Assets/Scenes/InputLab.unity",
            "QuestFirstViewRuntimeRepair",
            "KbduInspiredWorldBuilder",
            "RealKbduEnvironmentBuilder",
            "GaussianSplat",
            "SplatSceneryProvider",
            "Usb2Ble",
            "HOTAS",
            "/Training/",
            "PlaytestHud",
            "JSBSim",
            "FlightDynamicsRuntimeBootstrap",
            "FlightBackendSelector",
            "EditorSidecar"
        };

        private static readonly IReadOnlyDictionary<string, string> ProductCleanedOverrides =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab"] = "approved_product_cleaned",
                ["Assets/Production/Prefabs/ProductionC172AircraftRig.prefab"] = "approved_production_aircraft_rig",
                ["Assets/Scripts/Flight/Backends/FlightDynamicsCoordinator.cs"] = "approved_product_cleaned",
                ["Assets/Scripts/Runtime/AircraftReferenceFrameRig.cs"] = "approved_product_cleaned",
                ["Assets/Scripts/Runtime/QuestTemporalVisualGateRecorder.cs"] = "approved_product_cleaned",
                ["Assets/Scripts/Environment/AirportParkingStandManifest.cs"] = "approved_product_authored",
                ["Assets/Scripts/Environment/ProductionPlacementManifest.cs"] = "approved_product_authored",
                ["Assets/Scripts/Environment/ProductionTerrainSurfaceAsset.cs"] = "approved_product_authored",
                ["Assets/Scripts/Environment/GoldenEagleThermalSoaring.cs"] = "approved_product_authored",
                ["Assets/Scripts/Environment/ThermalFieldProfile.cs"] = "approved_product_authored",
                ["Assets/Scripts/Environment/AircraftCenteredAerialClipmapController.cs"] = "approved_product_authored",
                ["Assets/Scripts/Audio/ProductionC172AudioController.cs"] = "approved_product_authored",
                ["Assets/Scripts/Audio/ProductionC172AudioProfile.cs"] = "approved_product_authored",
                ["Assets/Production/Audio/C172/ProductionC172Audio.mixer"] = "approved_product_authored",
                ["Assets/Production/Audio/C172/ProductionC172AudioProfile.asset"] = "approved_product_authored",
                ["Assets/Production/Profiles/KBDU_ExtendedReservoirLessonV1.asset"] = "approved_product_authored",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonRecoveryRuntimeAdapter.cs"] = "approved_product_authored",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonRouteProfile.cs"] = "approved_product_authored"
            };

        [MenuItem("T2 Quest Flight/Validation/Export Production Dependency Closure")]
        public static void ExportFromMenu()
        {
            ClosureReport report = Export();
            if (!report.ready)
            {
                throw new InvalidOperationException(
                    $"Product dependency closure is blocked: rejected={report.rejectedCount}, " +
                    $"unknown={report.unknownCount}. See the dependency-closure report directory.");
            }
        }

        public static void ExportBatch()
        {
            try
            {
                ClosureReport report = Export();
                EditorApplication.Exit(report.ready ? 0 : 2);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(3);
            }
        }

        public static ClosureReport Export()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                throw new FileNotFoundException("The authored product scene is missing.", ScenePath);
            }

            ExtractionManifest manifest = LoadManifest();
            List<ClosureDependency> dependencies = AssetDatabase.GetDependencies(ScenePath, true)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new ClosureDependency
                {
                    assetPath = path,
                    classification = Classify(path, manifest)
                })
                .ToList();

            ClosureReport report = new ClosureReport
            {
                schemaVersion = 1,
                sourceCommit = SourceCommit,
                scenePath = ScenePath,
                dependencies = dependencies
            };
            report.approvedCount = dependencies.Count(item => IsApproved(item.classification));
            report.packageCount = dependencies.Count(item => item.classification == "unity_package_dependency");
            report.conditionalLocalCount = dependencies.Count(item => item.classification == "approved_local_development_only");
            report.rejectedCount = dependencies.Count(item => item.classification == "excluded_legacy_dependency");
            report.unknownCount = dependencies.Count(item => item.classification == "unknown_unclassified_dependency");
            report.ready = report.rejectedCount == 0 && report.unknownCount == 0;

            WriteReports(report);
            AssetDatabase.Refresh();
            Debug.Log(
                $"[T2QuestFlight][DependencyClosure] dependencies={dependencies.Count} " +
                $"approved={report.approvedCount} packages={report.packageCount} " +
                $"local={report.conditionalLocalCount} rejected={report.rejectedCount} " +
                $"unknown={report.unknownCount} ready={report.ready}");
            return report;
        }

        private static ExtractionManifest LoadManifest()
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "..",
                "tools",
                "extraction_manifest.json"));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Product extraction manifest is missing.", path);
            }

            ExtractionManifest manifest = JsonUtility.FromJson<ExtractionManifest>(File.ReadAllText(path));
            if (manifest == null || manifest.schema_version != 1 || manifest.source_ref != SourceCommit)
            {
                throw new InvalidDataException("Product extraction manifest schema or pinned source SHA is invalid.");
            }
            return manifest;
        }

        private static string Classify(string assetPath, ExtractionManifest manifest)
        {
            if (assetPath.StartsWith("Packages/", StringComparison.Ordinal))
            {
                return "unity_package_dependency";
            }

            if (ProductCleanedOverrides.TryGetValue(assetPath, out string cleanedClassification))
            {
                return cleanedClassification;
            }

            if (assetPath.StartsWith("Assets/Production/Audio/C172/Clips/", StringComparison.Ordinal))
            {
                return "approved_product_authored";
            }

            if (ForbiddenFragments.Any(fragment =>
                    assetPath.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "excluded_legacy_dependency";
            }

            ManifestEntry gated = (manifest.rights_gated_entries ?? Array.Empty<ManifestEntry>())
                .FirstOrDefault(entry => SourceToProductPath(entry.source) == assetPath);
            if (gated != null)
            {
                return gated.enabled
                    ? gated.classification
                    : string.IsNullOrEmpty(gated.disabled_classification)
                        ? "approved_local_development_only"
                        : gated.disabled_classification;
            }

            if (assetPath.IndexOf("/ImportedAssets/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "excluded_legacy_dependency";
            }

            ProductAuthoredEntry authored = (manifest.product_authored_entries ?? Array.Empty<ProductAuthoredEntry>())
                .FirstOrDefault(entry => string.Equals(entry.path, assetPath, StringComparison.Ordinal));
            if (authored != null)
            {
                return string.IsNullOrEmpty(authored.classification)
                    ? "approved_product_authored"
                    : authored.classification;
            }

            foreach (ManifestEntry entry in manifest.entries ?? Array.Empty<ManifestEntry>())
            {
                string productPattern = SourceToProductPath(entry.source);
                if (EntryMatches(assetPath, productPattern, entry.kind))
                {
                    return string.IsNullOrEmpty(entry.classification)
                        ? "approved_production"
                        : entry.classification;
                }
            }

            return "unknown_unclassified_dependency";
        }

        private static string SourceToProductPath(string sourcePath)
        {
            const string Prefix = "QuestFlightLab/";
            return sourcePath != null && sourcePath.StartsWith(Prefix, StringComparison.Ordinal)
                ? sourcePath.Substring(Prefix.Length)
                : sourcePath ?? string.Empty;
        }

        private static bool EntryMatches(string path, string pattern, string kind)
        {
            if (!string.Equals(kind, "glob", StringComparison.Ordinal))
            {
                return string.Equals(path, pattern, StringComparison.Ordinal);
            }
            if (pattern.EndsWith("/**", StringComparison.Ordinal))
            {
                string prefix = pattern.Substring(0, pattern.Length - 3).TrimEnd('/');
                return path.StartsWith(prefix + "/", StringComparison.Ordinal);
            }
            return false;
        }

        private static bool IsApproved(string classification)
        {
            return classification.StartsWith("approved_", StringComparison.Ordinal) &&
                   classification != "approved_local_development_only";
        }

        private static void WriteReports(ClosureReport report)
        {
            string configuredDirectory = Environment.GetEnvironmentVariable(
                "QFL_DEPENDENCY_CLOSURE_DIR");
            string reportDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "..",
                    "..",
                    "docs",
                    "extraction",
                    "reports"))
                : Path.GetFullPath(configuredDirectory);
            Directory.CreateDirectory(reportDirectory);

            string reportJson = JsonUtility.ToJson(report, true) + Environment.NewLine;
            File.WriteAllText(Path.Combine(reportDirectory, "source_dependency_closure.json"), reportJson, new UTF8Encoding(false));

            StringBuilder csv = new StringBuilder("asset_path,classification\n");
            foreach (ClosureDependency dependency in report.dependencies)
            {
                csv.Append(Csv(dependency.assetPath)).Append(',')
                    .Append(Csv(dependency.classification)).Append('\n');
            }
            File.WriteAllText(Path.Combine(reportDirectory, "source_dependency_closure.csv"), csv.ToString(), new UTF8Encoding(false));

            AllowlistComparison comparison = new AllowlistComparison
            {
                schemaVersion = 1,
                sourceCommit = report.sourceCommit,
                readyForCleanProduct = report.ready,
                approved = report.dependencies.Where(item => IsApproved(item.classification)).ToList(),
                unityPackages = report.dependencies.Where(item => item.classification == "unity_package_dependency").ToList(),
                conditionalLocal = report.dependencies.Where(item => item.classification == "approved_local_development_only").ToList(),
                rejected = report.dependencies.Where(item => item.classification == "excluded_legacy_dependency").ToList(),
                unknown = report.dependencies.Where(item => item.classification == "unknown_unclassified_dependency").ToList()
            };
            File.WriteAllText(
                Path.Combine(reportDirectory, "allowlist_comparison.json"),
                JsonUtility.ToJson(comparison, true) + Environment.NewLine,
                new UTF8Encoding(false));

            RejectedDependencies rejected = new RejectedDependencies
            {
                schemaVersion = 1,
                sourceCommit = report.sourceCommit,
                rejectedDependencies = comparison.rejected
            };
            File.WriteAllText(
                Path.Combine(reportDirectory, "rejected_dependencies.json"),
                JsonUtility.ToJson(rejected, true) + Environment.NewLine,
                new UTF8Encoding(false));

            FinalProductClosure final = new FinalProductClosure
            {
                schemaVersion = 1,
                sourceCommit = report.sourceCommit,
                ready = report.ready,
                approvedProductionDependencies = comparison.approved,
                unityPackageDependencies = comparison.unityPackages,
                approvedLocalDevelopmentOnly = comparison.conditionalLocal,
                blockers = comparison.rejected.Concat(comparison.unknown)
                    .OrderBy(item => item.assetPath, StringComparer.Ordinal).ToList()
            };
            File.WriteAllText(
                Path.Combine(reportDirectory, "final_product_closure.json"),
                JsonUtility.ToJson(final, true) + Environment.NewLine,
                new UTF8Encoding(false));
        }

        private static string Csv(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }

        [Serializable]
        private sealed class ExtractionManifest
        {
            public int schema_version;
            public string source_ref;
            public ManifestEntry[] entries;
            public ManifestEntry[] rights_gated_entries;
            public ProductAuthoredEntry[] product_authored_entries;
        }

        [Serializable]
        private sealed class ManifestEntry
        {
            public string source;
            public string kind;
            public string classification;
            public bool enabled;
            public string disabled_classification;
        }

        [Serializable]
        private sealed class ProductAuthoredEntry
        {
            public string path;
            public string classification;
        }

        [Serializable]
        public sealed class ClosureDependency
        {
            public string assetPath;
            public string classification;
        }

        [Serializable]
        public sealed class ClosureReport
        {
            public int schemaVersion;
            public string sourceCommit;
            public string scenePath;
            public bool ready;
            public int approvedCount;
            public int packageCount;
            public int conditionalLocalCount;
            public int rejectedCount;
            public int unknownCount;
            public List<ClosureDependency> dependencies = new List<ClosureDependency>();
        }

        [Serializable]
        private sealed class AllowlistComparison
        {
            public int schemaVersion;
            public string sourceCommit;
            public bool readyForCleanProduct;
            public List<ClosureDependency> approved;
            public List<ClosureDependency> unityPackages;
            public List<ClosureDependency> conditionalLocal;
            public List<ClosureDependency> rejected;
            public List<ClosureDependency> unknown;
        }

        [Serializable]
        private sealed class RejectedDependencies
        {
            public int schemaVersion;
            public string sourceCommit;
            public List<ClosureDependency> rejectedDependencies;
        }

        [Serializable]
        private sealed class FinalProductClosure
        {
            public int schemaVersion;
            public string sourceCommit;
            public bool ready;
            public List<ClosureDependency> approvedProductionDependencies;
            public List<ClosureDependency> unityPackageDependencies;
            public List<ClosureDependency> approvedLocalDevelopmentOnly;
            public List<ClosureDependency> blockers;
        }
    }
}
#endif
