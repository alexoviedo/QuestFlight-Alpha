using UnityEngine;
using QuestFlightLab.Flight.Soaring;

namespace QuestFlight.Product
{
    [CreateAssetMenu(menuName="QuestFlight/Soaring/Scenic demo")]
    public sealed class SoaringDemoProfile : ScriptableObject
    {
        public BoulderSoaringOperationDefinition operation;
        public Vector2[] scenicPoints, shortPoints;
        public Vector3 airborneStart;
        public float airborneHeading=166, airborneSpeed=42;
        public float thermalRadius=150, thermalGain=150, minimumThermalSeconds=180, maximumThermalSeconds=480;
        public string provenance;
        public bool IsValid=>operation!=null&&operation.IsValid&&operation.runwayDirection==8&&operation.weather.thermalEnabled&&operation.weather.sources.Length>0&&
            ValidPoints(scenicPoints,3)&&ValidPoints(shortPoints,2)&&operation.Contains(airborneStart)&&
            airborneStart.y>=600&&airborneStart.y<=1400&&airborneHeading>=0&&airborneHeading<360&&airborneSpeed>=40&&airborneSpeed<=60&&
            thermalRadius>=100&&thermalRadius<=250&&thermalGain>=150&&thermalGain<=400&&
            minimumThermalSeconds>=120&&maximumThermalSeconds>=minimumThermalSeconds&&maximumThermalSeconds<=600;
        private bool ValidPoints(Vector2[] points,int minimum)
        {
            if(points==null||points.Length<minimum)return false;
            foreach(var p in points)if(!operation.Contains(new Vector3(p.x,0,p.y)))return false;
            return true;
        }
    }
}
