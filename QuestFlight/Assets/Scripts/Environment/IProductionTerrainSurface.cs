using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    public enum ProductionTerrainZone
    {
        Outside = 0,
        Airport = 1,
        Near = 2,
        Mid = 3,
        Far = 4
    }

    public enum ProductionSurfaceCategory
    {
        Unknown = 0,
        DryPrairie = 1,
        SparseGrass = 2,
        SoilRock = 3,
        Runway = 4,
        Apron = 5,
        Road = 6,
        Water = 7
    }

    [Serializable]
    public readonly struct ProductionTerrainSample
    {
        public readonly float heightMeters;
        public readonly Vector3 normal;
        public readonly ProductionTerrainZone zone;
        public readonly Vector2 macroUv;
        public readonly ProductionSurfaceCategory category;

        public bool IsRunwayOrApron =>
            category == ProductionSurfaceCategory.Runway || category == ProductionSurfaceCategory.Apron;

        public bool IsWater => category == ProductionSurfaceCategory.Water;

        public ProductionTerrainSample(
            float heightMeters,
            Vector3 normal,
            ProductionTerrainZone zone,
            Vector2 macroUv,
            ProductionSurfaceCategory category)
        {
            this.heightMeters = heightMeters;
            this.normal = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;
            this.zone = zone;
            this.macroUv = macroUv;
            this.category = category;
        }
    }

    [Serializable]
    public readonly struct ProductionTerrainPlane
    {
        public readonly Vector3 point;
        public readonly Vector3 normal;
        public readonly float maximumResidualMeters;

        public ProductionTerrainPlane(Vector3 point, Vector3 normal, float maximumResidualMeters)
        {
            this.point = point;
            this.normal = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;
            this.maximumResidualMeters = maximumResidualMeters;
        }

        public float HeightAt(float eastMeters, float northMeters)
        {
            if (Mathf.Abs(normal.y) < 0.000001f) return point.y;
            return point.y - (normal.x * (eastMeters - point.x) + normal.z * (northMeters - point.z)) / normal.y;
        }
    }

    /// <summary>
    /// Authoritative local-ENU query used by offline production authoring and validation.
    /// Implementations sample authored terrain data directly; callers must not substitute
    /// Physics.Raycast, nearest-vertex searches, camera-relative geometry, or mixed colliders.
    /// </summary>
    public interface IProductionTerrainSurface
    {
        bool TrySample(float eastMeters, float northMeters, out ProductionTerrainSample sample);

        bool TryBestFitPlane(IReadOnlyList<Vector2> footprintEnu, out ProductionTerrainPlane plane);
    }
}
