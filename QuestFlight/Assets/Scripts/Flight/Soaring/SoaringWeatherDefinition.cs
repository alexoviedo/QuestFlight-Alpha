using System;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Environment;
using UnityEngine;

namespace QuestFlightLab.Flight.Soaring
{
    [CreateAssetMenu(menuName="QuestFlight/Soaring/Reference weather")]
    public sealed class SoaringWeatherDefinition : ScriptableObject
    {
        public string displayName,terrainSha256;
        [TextArea] public string referenceScope="Engineering training conditions; not a Boulder weather observation. No wave/rotor or cloud association.";
        public Vector2 surfaceWind=new Vector2(-2.5f,0),upperWind=new Vector2(-5.5f,0);
        public float gustAmplitude=.2f;
        public bool ridgeEnabled,thermalEnabled=true,differentialAirflow=true,wakeEnabled=true;
        public double seaLevelPressurePa=101325,isaOffsetKelvin=10;
        public int seed=233;
        public ThermalSource[] sources=Array.Empty<ThermalSource>();
        [Serializable] public struct ThermalSource
        {
            public Vector2 position;
            public float groundHeight,mixingHeight,convectiveScale,radiusScale,lifetime,period,initialAge;
        }
        public bool IsValid(out string error)
        {
            error="Invalid bounded soaring weather definition.";
            if(string.IsNullOrEmpty(displayName)||!BoulderWindFieldProfile.Finite(surfaceWind.x)||!BoulderWindFieldProfile.Finite(surfaceWind.y)||
                !BoulderWindFieldProfile.Finite(upperWind.x)||!BoulderWindFieldProfile.Finite(upperWind.y)||surfaceWind.magnitude>15||upperWind.magnitude>20||
                !(gustAmplitude>=0&&gustAmplitude<=2)||!(seaLevelPressurePa>=80000&&seaLevelPressurePa<=110000)||!(isaOffsetKelvin>=-60&&isaOffsetKelvin<=60)||sources==null||sources.Length>8)return false;
            foreach(var s in sources)if(!BoulderWindFieldProfile.Finite(s.position.x)||!BoulderWindFieldProfile.Finite(s.position.y)||!BoulderWindFieldProfile.Finite(s.groundHeight)||
                !(s.mixingHeight>=500&&s.mixingHeight<=2000)||!(s.convectiveScale>0&&s.convectiveScale<=3.5)||!(s.radiusScale>=1&&s.radiusScale<=3.5)||
                !(s.lifetime>=300&&s.lifetime<=1800)||!(s.period>=s.lifetime+60&&s.period<=2400)||!BoulderWindFieldProfile.Finite(s.initialAge))return false;
            error=null;return true;
        }
    }

    // Small immutable snapshot, made only at paused preparation. It extends the
    // existing field's flow and terrain sampler and owns no clock or update.
    public sealed class SoaringAtmosphereSession
    {
        private readonly BoulderWindFieldProfile field;
        private readonly Vector2 surface,upper;
        private readonly float gust;
        private readonly bool ridge,thermals;
        private readonly SoaringWeatherDefinition.ThermalSource[] sources;
        public readonly bool DifferentialAirflow,WakeEnabled;
        public readonly JSBSimNativeConditions Conditions;
        public readonly string Name;
        public int SourceCount=>sources.Length;
        public SoaringWeatherDefinition.ThermalSource Source(int i)=>sources[i];
        public SoaringAtmosphereSession(BoulderWindFieldProfile profile,SoaringWeatherDefinition definition)
        {
            if(profile==null||!profile.IsValid(out _)||definition==null||!definition.IsValid(out _))throw new ArgumentException("Valid authored shared field and soaring weather required.");
            field=profile;surface=definition.surfaceWind;upper=definition.upperWind;gust=definition.gustAmplitude;ridge=definition.ridgeEnabled;thermals=definition.thermalEnabled;
            DifferentialAirflow=definition.differentialAirflow;WakeEnabled=definition.wakeEnabled;Name=definition.displayName;
            Conditions=new JSBSimNativeConditions{seaLevelPressurePa=definition.seaLevelPressurePa,isaTemperatureOffsetKelvin=definition.isaOffsetKelvin};
            sources=(SoaringWeatherDefinition.ThermalSource[])definition.sources.Clone();
            // A seed fixes phase offsets once. Source locations never follow a pilot.
            uint random=unchecked((uint)definition.seed);
            for(int i=0;i<sources.Length;i++){random=1664525*random+1013904223;sources[i].initialAge+=((random>>8)&65535)/65535f*30;}
        }
        public Vector3 Sample(Vector3 position,double time)=>SampleComponents(position,time).Total;
        public BoulderWindFieldProfile.Components SampleComponents(Vector3 position,double time)
        {
            var result=field.SampleConfiguredFlow(position,time,surface,upper,gust);
            if(!ridge)result.terrain=0;
            if(thermals)
            {
                Vector2 wind=new Vector2(result.background.x,result.background.z);
                for(int i=0;i<sources.Length;i++)result.thermal+=ThermalVelocity(sources[i],position,time,wind);
            }
            return result;
        }
        public static float Radius(float height,float mixingHeight,float scale)
        {float z=height/mixingHeight;return Mathf.Max(10,.102f*Mathf.Pow(Mathf.Max(0,z),1f/3)*(1-.25f*z)*mixingHeight)*scale;}
        public static float CompactShape(float radiusFraction)
        {
            if(radiusFraction>=3)return 0;
            float a=radiusFraction<=1?1-radiusFraction*radiusFraction:1-(radiusFraction-2)*(radiusFraction-2);
            return a*a*(radiusFraction<=1?1:-5f/64);
        }
        public static float Lifetime(SoaringWeatherDefinition.ThermalSource source,double time)
        {
            double age=(time+source.initialAge)%source.period;if(age<0)age+=source.period;
            return BoulderWindFieldProfile.Smooth((float)age/90)*BoulderWindFieldProfile.Smooth((source.lifetime-(float)age)/180);
        }
        public static Vector2 Center(SoaringWeatherDefinition.ThermalSource source,float altitude,Vector2 wind)=>source.position+wind*Mathf.Min(90,Mathf.Max(0,altitude-source.groundHeight)/Mathf.Max(1,source.convectiveScale));
        public static float ThermalVelocity(SoaringWeatherDefinition.ThermalSource source,Vector3 position,double time,Vector2 wind)
        {
            float height=position.y-source.groundHeight,z=height/source.mixingHeight;
            if(z<=0||z>=1)return 0;
            float radius=Radius(height,source.mixingHeight,source.radiusScale);
            Vector2 center=Center(source,position.y,wind);float u=(new Vector2(position.x,position.z)-center).magnitude/radius;
            if(u>=3)return 0;
            float baseRadius=radius/source.radiusScale,coreRatio=Mathf.Min(.8f,.0011f*baseRadius+.14f);
            float average=Mathf.Pow(z,1f/3)*Mathf.Max(0,1-1.1f*z)*source.convectiveScale;
            float core=3*average*(1-coreRatio)/(1-coreRatio*coreRatio*coreRatio);
            return core*CompactShape(u)*Lifetime(source,time)*BoulderWindFieldProfile.Smooth(height/40)*BoulderWindFieldProfile.Smooth((1-z)/.15f);
        }
    }
}
