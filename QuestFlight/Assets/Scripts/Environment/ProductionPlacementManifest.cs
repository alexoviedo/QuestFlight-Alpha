using System;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    public enum ProductionPlacementCategory
    {
        Building,
        Tree,
        Shrub,
        ParkedAircraft,
        AirportProp,
        Fence,
        Road,
        Water
    }

    public enum ProductionFoundationMode
    {
        ContactOffset,
        BestFitPlane,
        CutFillPad,
        FoundationSkirt,
        WaterBedMask
    }

    public enum ProductionLodClass
    {
        NearAuthored,
        NearInstanced,
        MidHlod,
        OrthophotoOnly
    }

    [CreateAssetMenu(fileName = "ProductionPlacementManifest", menuName = "QuestFlight/Production Placement Manifest")]
    public sealed class ProductionPlacementManifest : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string stableId;
            public string sourceFeatureId;
            public string speciesId;
            public string placementValidationStatus;
            public ProductionPlacementCategory category;
            public Vector2 localEnuMeters;
            public float authoredHeadingDegrees;
            public float modelForwardAxisCorrectionDegrees;
            public Vector3 scale = Vector3.one;
            public float terrainHeightMeters;
            public Vector3 terrainNormal = Vector3.up;
            public float contactPointOffsetMeters;
            public Vector2[] footprintEnuMeters = Array.Empty<Vector2>();
            public ProductionFoundationMode foundationMode;
            public float foundationMaximumResidualMeters;
            public ProductionLodClass lodClass;
            public string hlodCellId;
            public string sourceLicenseClass;
            public string sourceAssetGuid;
            public Vector3[] modelContactPoints = Array.Empty<Vector3>();
        }

        public string schemaVersion = "2";
        public string coordinateFrame = "KBDU local tangent ENU; Unity +X=east, +Z=north";
        public string terrainSurfaceAssetGuid;
        public string sourceManifestSha256;
        public Entry[] entries = Array.Empty<Entry>();
    }
}
