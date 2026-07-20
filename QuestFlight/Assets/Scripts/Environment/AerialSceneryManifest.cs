using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Compact, serialized provenance and representation contract for the offline
    /// Aerial Scenery V1 bake. Raw lidar and authoring caches are deliberately not
    /// referenced by this runtime asset.
    /// </summary>
    [CreateAssetMenu(fileName = "KBDU_AerialSceneryManifestV1", menuName = "QuestFlight/Aerial Scenery Manifest V1")]
    public sealed class AerialSceneryManifest : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        [Header("Identity")]
        public int schemaVersion = CurrentSchemaVersion;
        public string label = "KBDU aerial scenery representation V1 — not for navigation";
        public string horizontalCrs = "EPSG:32613";
        public string sourceLicense = "USGS 3DEP public domain; OSM derivatives ODbL-1.0";
        public string generatedUtc;

        [Header("Committed provenance")]
        public TextAsset lidarSourceManifest;
        public TextAsset terrainCoordinateConversionReport;
        public TextAsset terrainConformanceReport;
        public TextAsset vegetationSourceManifest;
        public TextAsset buildingSourceManifest;
        public string lidarPipelineSha256;
        public string vegetationPipelineSha256;
        public string buildingPipelineSha256;

        [Header("Bounded derived data")]
        public Vector2 worldMinimumMeters = new Vector2(-2048f, -2048f);
        public Vector2 worldSizeMeters = new Vector2(4096f, 4096f);
        public Texture2D surfaceFeatures;
        public Texture2D surfaceNormals;
        public Texture2D canopyNormals;
        public Texture2D canopyOcclusion;
        public Texture2D canopyGeometryHandoff;
        public Mesh airportPatchMesh;
        public Mesh nearTerrainMesh;
        public Mesh midTerrainMesh;

        [Header("Source and compilation metrics")]
        public long boundedSourcePointCount;
        public int canopyComponentCount;
        public int nearTreeInstanceCount;
        public int midGroveClusterCount;
        public int representedBuildingCount;
        public int orthophotoOnlyBuildingCount;
        public float buildingFootprintInflationMaximum = 1f;
        public float terrainMeanAbsoluteErrorMeters;
        public float terrainP95ErrorMeters;
        public float terrainMaximumErrorMeters;
        public bool terrainErrorMetricsExcludeDeclaredOutliers;
        [Range(0f, 1f)] public float terrainDeclaredOutlierFraction;
        public bool operationalSurfaceRealWorldToleranceEstablished;

        public bool TryValidate(out string report)
        {
            List<string> failures = new List<string>();
            Require(schemaVersion == CurrentSchemaVersion, "schema version is not V1", failures);
            Require(!string.IsNullOrWhiteSpace(label), "label is missing", failures);
            Require(string.Equals(horizontalCrs, "EPSG:32613", StringComparison.Ordinal),
                "horizontal CRS must be EPSG:32613", failures);
            Require(!string.IsNullOrWhiteSpace(sourceLicense), "source/license record is missing", failures);
            Require(lidarSourceManifest != null, "lidar source manifest is missing", failures);
            Require(terrainCoordinateConversionReport != null,
                "Unity-local lidar coordinate conversion report is missing", failures);
            Require(terrainConformanceReport != null, "airport/lidar conformance report is missing", failures);
            Require(vegetationSourceManifest != null, "vegetation source manifest is missing", failures);
            Require(buildingSourceManifest != null, "building source manifest is missing", failures);
            Require(IsSha256(lidarPipelineSha256), "lidar pipeline SHA256 is invalid", failures);
            Require(IsSha256(vegetationPipelineSha256), "vegetation pipeline SHA256 is invalid", failures);
            Require(IsSha256(buildingPipelineSha256), "building pipeline SHA256 is invalid", failures);
            Require(worldSizeMeters.x >= 4096f && worldSizeMeters.y >= 4096f,
                "bounded detailed coverage is smaller than 4,096 m", failures);
            Require(surfaceFeatures != null, "packed canopy/surface-features texture is missing", failures);
            Require(surfaceNormals != null, "surface-normal texture is missing", failures);
            Require(canopyNormals != null, "canopy-normal texture is missing", failures);
            Require(canopyOcclusion != null, "canopy-occlusion texture is missing", failures);
            Require(canopyGeometryHandoff != null, "canopy geometry-to-map handoff is missing", failures);
            Require(airportPatchMesh != null, "conformed airport visual patch is missing", failures);
            Require(nearTerrainMesh != null, "fixed near terrain mesh is missing", failures);
            Require(midTerrainMesh != null, "fixed mid terrain mesh is missing", failures);
            Require(boundedSourcePointCount > 0, "bounded 3DEP source point count is missing", failures);
            Require(buildingFootprintInflationMaximum <= 1.10f,
                $"building footprint inflation exceeds 10% ({buildingFootprintInflationMaximum:0.000}x)", failures);
            Require(terrainMeanAbsoluteErrorMeters >= 0f && terrainP95ErrorMeters >= terrainMeanAbsoluteErrorMeters &&
                    terrainMaximumErrorMeters >= terrainP95ErrorMeters,
                "terrain error metrics are incomplete or unordered", failures);
            Require(!terrainErrorMetricsExcludeDeclaredOutliers || terrainDeclaredOutlierFraction > 0f,
                "terrain error metrics exclude outliers without declaring their fraction", failures);
            Require(!operationalSurfaceRealWorldToleranceEstablished,
                "no evidence supports a real-world operational terrain tolerance", failures);

            report = failures.Count == 0
                ? $"PASS sourcePoints={boundedSourcePointCount:N0} groves={midGroveClusterCount:N0} " +
                  $"buildings={representedBuildingCount:N0} footprintInflation={buildingFootprintInflationMaximum:0.000}x"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool hexadecimal = character >= '0' && character <= '9' ||
                                   character >= 'a' && character <= 'f' ||
                                   character >= 'A' && character <= 'F';
                if (!hexadecimal) return false;
            }
            return true;
        }

        private static void Require(bool condition, string failure, ICollection<string> failures)
        {
            if (!condition) failures.Add(failure);
        }
    }
}
