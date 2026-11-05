using System;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Environment;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    /// <summary>Prepared native contact authority sampled from the serialized terrain.
    /// A 320 m square with 10 m spacing; refresh inside a 40 m inset. No raycasts.
    /// Native queries fail on coverage loss instead of extrapolating old ground.</summary>
    public sealed class NativeTerrainPatch
    {
        private const int Side = 33;
        private readonly double[] heights = new double[Side * Side];
        private JSBSimNativeTerrainPatch patch;
        private bool prepared;
        public bool Prepare(JSBSimNativeSession session, ProductionTerrainSurfaceAsset surface,
            GeodeticReference origin, double latitude, double longitude, bool force, out string error)
        {
            error = string.Empty;
            if (!force && prepared)
            {
                double row = (latitude - patch.minimumLatitudeDegrees) / patch.latitudeStepDegrees;
                double col = (longitude - patch.minimumLongitudeDegrees) / patch.longitudeStepDegrees;
                if (row > 4 && row < Side-5 && col > 4 && col < Side-5) return true;
            }
            const double radians = Math.PI / 180;
            const double a = 6378137, e2 = 0.0066943799901413165;
            double f = 1-e2*Math.Pow(Math.Sin(latitude*radians),2);
            double meridian = a*(1-e2)/Math.Pow(f,1.5), prime = a/Math.Sqrt(f);
            double dlat = 10/meridian/radians, dlon = 10/(prime*Math.Cos(latitude*radians))/radians;
            patch = new JSBSimNativeTerrainPatch { minimumLatitudeDegrees=latitude-16*dlat, minimumLongitudeDegrees=longitude-16*dlon,
                latitudeStepDegrees=dlat, longitudeStepDegrees=dlon, rows=Side,columns=Side };
            for (int row=0;row<Side;row++) for (int col=0;col<Side;col++)
            {
                Vector3 point = FlightFrameConversions.GeodeticToUnity(patch.minimumLatitudeDegrees+row*dlat,
                    patch.minimumLongitudeDegrees+col*dlon,origin.altitudeMslMeters,origin);
                if (!surface.TrySample(point.x,point.z,out ProductionTerrainSample sample))
                { error="Prepared contact patch extends beyond authored terrain coverage.";prepared=false;return false; }
                heights[row*Side+col] = origin.altitudeMslMeters+sample.heightMeters;
            }
            prepared = session.SetTerrainPatch(patch,heights);
            if (!prepared) error=session.LastError;
            return prepared;
        }
    }
}
