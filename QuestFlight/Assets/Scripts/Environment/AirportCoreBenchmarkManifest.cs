using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Serialized provenance, scope, and budget contract for the bounded KBDU airport-core
    /// benchmark. The manifest is data only; it never constructs or repairs product content.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AirportCoreBenchmarkManifest",
        menuName = "Quest Flight/Environment/Airport Core Benchmark Manifest")]
    public sealed class AirportCoreBenchmarkManifest : ScriptableObject
    {
        public const string AssetPath =
            "Assets/Production/Environment/AirportCoreV4/AirportCoreBenchmarkManifest.asset";
        public const string StableId = "KBDU_AIRPORT_CORE_BENCHMARK_V4";
        public const string SourceV2Sha = "eaccabc5e2cb9ccdcef30f825c69a013b83cea75";
        public const string ReferenceV3Sha = "46ffc235113532ff5c4204c9b3b0a11e5932b2ce";
        public const float MinimumHorizontalExtentMeters = 600f;
        public const float MaximumHorizontalExtentMeters = 900f;
        public const int MaximumLightmapCount = 2;
        public const int MaximumLightmapResolution = 2048;

        [Header("Identity and bounded scope")]
        public string stableId = StableId;
        public string sourceV2Sha = SourceV2Sha;
        public string referenceV3Sha = ReferenceV3Sha;
        public Bounds benchmarkBounds = new Bounds(Vector3.zero, new Vector3(800f, 180f, 800f));
        public string[] replacementSourceFeatureIds = Array.Empty<string>();

        [Header("Pinned evidence and licenses")]
        public TextAsset[] sourceAndLicenseManifests = Array.Empty<TextAsset>();
        public TextAsset buildingValidationReport;
        public TextAsset materialsValidationReport;
        public TextAsset waterVegetationValidationReport;

        [Header("Optimized runtime assets")]
        public Texture2D terminalAlbedo;
        public Texture2D terminalNormal;
        public Texture2D terminalMrao;
        public Texture2D hangarKitAlbedo;
        public Texture2D hangarKitNormal;
        public Texture2D hangarKitMrao;
        public Texture2D cleanPlateAlbedo;
        public Texture2D replacementMask;
        public Texture2D originalShadowMask;
        public Texture reflectionCubemap;
        public Material terminalMaterial;
        public Material hangarKitMaterial;
        public Material runwayMaterial;
        public Material reservoirMaterial;
        public Material vegetationMaterial;

        [Header("One coherent lighting contract")]
        public float sunAzimuthDegrees;
        public float sunElevationDegrees;
        [Range(0f, 1f)] public float sunCalibrationConfidence;
        public Color sunColor = Color.white;
        public float sunIntensity;
        public Color ambientColor = Color.gray;
        public float ambientIntensity;
        public float skyRotationDegrees;
        public int bakedLightmapCount;
        public int bakedLightmapResolution;
        public long estimatedLightmapBytes;
        public int serializedLightProbeCount;
        public int bakedReflectionProbeCount;
        public long estimatedReflectionBytes;

        [Header("Authored integration result")]
        public bool cleanPlateCoversEveryReplacement;
        public bool photographedShadowsReconciled;
        public bool runwayMacroPatternRemoved;
        public bool minorWaterUsesTerrainMask;
        public bool rawSourcesExcluded;
        public int serializedReplacementCount;
        public int serializedRendererCount;
        public int serializedTriangleCount;
        public int serializedMaterialCount;
        public long estimatedTextureBytes;
        public string integrationUtc;

        public bool TryValidate(out string report)
        {
            List<string> failures = new List<string>();
            Require(string.Equals(stableId, StableId, StringComparison.Ordinal),
                "stable benchmark identity changed", failures);
            Require(string.Equals(sourceV2Sha, SourceV2Sha, StringComparison.Ordinal),
                "stable V2 source SHA changed", failures);
            Require(string.Equals(referenceV3Sha, ReferenceV3Sha, StringComparison.Ordinal),
                "read-only V3 reference SHA changed", failures);
            Require(IsHorizontalExtentValid(benchmarkBounds.size.x) &&
                    IsHorizontalExtentValid(benchmarkBounds.size.z),
                $"benchmark bounds must be 600-900 m horizontally, found {benchmarkBounds.size}", failures);
            Require(benchmarkBounds.size.y > 0f && benchmarkBounds.size.y <= 400f,
                "benchmark vertical bounds are invalid", failures);
            Require(replacementSourceFeatureIds != null && replacementSourceFeatureIds.Length >= 2,
                "terminal and Type 2 replacement source IDs are missing", failures);
            Require(replacementSourceFeatureIds == null ||
                    replacementSourceFeatureIds.All(item => !string.IsNullOrWhiteSpace(item)) &&
                    replacementSourceFeatureIds.Distinct(StringComparer.Ordinal).Count() ==
                    replacementSourceFeatureIds.Length,
                "replacement source IDs must be nonempty and unique", failures);
            Require(sourceAndLicenseManifests != null && sourceAndLicenseManifests.Length >= 3 &&
                    sourceAndLicenseManifests.All(item => item != null),
                "source/license manifests are incomplete", failures);
            Require(buildingValidationReport != null && materialsValidationReport != null &&
                    waterVegetationValidationReport != null,
                "workstream validation reports are not all bound", failures);
            Require(terminalAlbedo != null && terminalNormal != null && terminalMrao != null,
                "terminal PBR atlas set is incomplete", failures);
            Require(hangarKitAlbedo != null && hangarKitNormal != null && hangarKitMrao != null,
                "hangar/airport-kit PBR atlas set is incomplete", failures);
            Require(cleanPlateAlbedo != null && replacementMask != null && originalShadowMask != null,
                "bounded clean-plate asset set is incomplete", failures);
            Require(terminalMaterial != null && hangarKitMaterial != null && runwayMaterial != null &&
                    reservoirMaterial != null && vegetationMaterial != null,
                "benchmark shared materials are incomplete", failures);
            Require(reflectionCubemap != null, "benchmark reflection cubemap is missing", failures);
            Require(sunAzimuthDegrees >= 0f && sunAzimuthDegrees < 360f,
                "sun azimuth is outside [0,360)", failures);
            Require(sunElevationDegrees > 0f && sunElevationDegrees < 90f,
                "sun elevation is outside (0,90)", failures);
            Require(sunCalibrationConfidence > 0f && sunCalibrationConfidence <= 1f,
                "sun calibration confidence is missing", failures);
            Require(sunIntensity > 0f && ambientIntensity > 0f,
                "sun/ambient intensity contract is incomplete", failures);
            Require(bakedLightmapCount >= 1 && bakedLightmapCount <= MaximumLightmapCount,
                "benchmark must use one or two lightmaps", failures);
            Require(bakedLightmapResolution > 0 && bakedLightmapResolution <= MaximumLightmapResolution,
                "benchmark lightmap resolution exceeds 2048", failures);
            Require(estimatedLightmapBytes > 0, "lightmap memory estimate is missing", failures);
            Require(serializedLightProbeCount > 0, "light probes are not serialized", failures);
            Require(bakedReflectionProbeCount >= 0 && bakedReflectionProbeCount <= 1,
                "benchmark may use at most one baked reflection probe", failures);
            Require(bakedReflectionProbeCount == 0 || estimatedReflectionBytes > 0,
                "reflection-probe memory estimate is missing", failures);
            Require(cleanPlateCoversEveryReplacement,
                "clean plate does not cover every replacement", failures);
            Require(photographedShadowsReconciled,
                "photographed and 3D shadows are not reconciled", failures);
            Require(runwayMacroPatternRemoved,
                "obsolete runway macro pattern remains active", failures);
            Require(minorWaterUsesTerrainMask,
                "minor-water benchmark is not terrain-mask based", failures);
            Require(rawSourcesExcluded, "raw source assets are not excluded", failures);
            Require(serializedReplacementCount == replacementSourceFeatureIds?.Length,
                "replacement count differs from source feature inventory", failures);
            Require(serializedRendererCount > 0 && serializedTriangleCount > 0 &&
                    serializedMaterialCount > 0 && estimatedTextureBytes > 0,
                "serialized benchmark cost inventory is incomplete", failures);
            Require(!string.IsNullOrWhiteSpace(integrationUtc),
                "deterministic integration timestamp is missing", failures);

            report = failures.Count == 0
                ? $"{stableId}: replacements={serializedReplacementCount}, renderers={serializedRendererCount}, " +
                  $"triangles={serializedTriangleCount}, materials={serializedMaterialCount}"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }

        private static bool IsHorizontalExtentValid(float value)
        {
            return value >= MinimumHorizontalExtentMeters && value <= MaximumHorizontalExtentMeters;
        }

        private static void Require(bool condition, string message, ICollection<string> failures)
        {
            if (!condition) failures.Add(message);
        }
    }
}
