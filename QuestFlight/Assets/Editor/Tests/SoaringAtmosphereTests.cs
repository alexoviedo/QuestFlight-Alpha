using System;
using AlexOviedo.QuestFlight.JSBSim;
using NUnit.Framework;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class SoaringAtmosphereTests
    {
        private static BoulderWindFieldProfile Field()=>AssetDatabase.LoadAssetAtPath<BoulderWindFieldProfile>("Assets/Production/Environment/AmbientAircraftV1/BoulderWind.asset");
        private static SoaringWeatherDefinition Definition(string name)=>AssetDatabase.LoadAssetAtPath<SoaringWeatherDefinition>("Assets/Production/SoaringV1/Weather/"+name+".asset");
        [Test] public void ImmutableSessionAndNativeTimeAreRepeatableWithNoSteadyAllocations()
        {
            var definition=UnityEngine.Object.Instantiate(Definition("ModerateThermal"));
            try
            {
                var session=new SoaringAtmosphereSession(Field(),definition);var p=new Vector3(1200,400,2600);var a=session.Sample(p,100);
                definition.sources[0].position+=Vector2.one*10000;definition.surfaceWind=Vector2.zero;
                Assert.That(session.Sample(p,100),Is.EqualTo(a));
                for(int i=0;i<100;i++)session.Sample(p,i);
                long before=GC.GetAllocatedBytesForCurrentThread();Vector3 sum=Vector3.zero;
                for(int i=0;i<1000;i++)sum+=session.Sample(p,i*.01);
                Assert.That(GC.GetAllocatedBytesForCurrentThread()-before,Is.Zero);Assert.That(BoulderWindFieldProfile.Finite(sum),Is.True);
            }
            finally{UnityEngine.Object.DestroyImmediate(definition);}
        }
        [Test] public void CompactThermalHasBalancedFluxAndSmoothFiniteLife()
        {
            double flux=0;const double h=.0001;
            for(int i=0;i<30000;i++){double u=(i+.5)*h;flux+=u*SoaringAtmosphereSession.CompactShape((float)u)*h;}
            Assert.That(Math.Abs(flux),Is.LessThan(.00001));
            var source=Definition("ModerateThermal").sources[0];source.initialAge=0;
            Assert.That(SoaringAtmosphereSession.Lifetime(source,0),Is.Zero);Assert.That(SoaringAtmosphereSession.Lifetime(source,200),Is.EqualTo(1));
            Assert.That(SoaringAtmosphereSession.Lifetime(source,source.lifetime),Is.Zero);Assert.That(SoaringAtmosphereSession.Lifetime(source,source.period),Is.Zero);
            var center=new Vector3(source.position.x,source.groundHeight+source.mixingHeight,source.position.y);
            Assert.That(SoaringAtmosphereSession.ThermalVelocity(source,center,200,Vector2.zero),Is.Zero);
        }
        [Test] public void UniformAirflowAddsExactlyZeroForceAndMoment()
        {
            var session=new SoaringAtmosphereSession(Field(),Definition("CalmValidation"));
            var state=new FlightDynamicsState{positionUnityMeters=new Vector3(0,500,0),rotationUnity=Quaternion.Euler(3,37,8),velocityUnityMetersPerSecond=new Vector3(15,-1,24),angularVelocityBodyDegreesPerSecond=new Vector3(4,-3,2)};
            var d=new JSBSimNativeDiagnostics{cgXInches=81.56,cgZInches=-2.68,densitySlugsPerCubicFoot=.002};
            var load=AircraftAirflowSampler.Sample(state,d,session,Vector3.zero,null,true);
            Assert.That(load.forceBodyFrdNewtons,Is.EqualTo(Vector3.zero));Assert.That(load.momentBodyFrdNewtonMeters,Is.EqualTo(Vector3.zero));
        }
        [Test] public void AsymmetricRightWingUpflowRollsLeftAndMirrors()
        {
            var field=UnityEngine.Object.Instantiate(Field());var definition=UnityEngine.Object.Instantiate(Definition("ModerateRidge"));
            try
            {
                definition.surfaceWind=definition.upperWind=new Vector2(5,0);definition.gustAmplitude=0;field.ridgeInfluenceHeight=450;
                for(int i=0;i<field.groundHeights.Length;i++){field.groundHeights[i]=0;field.gradientEast[i]=Mathf.Clamp((field.minimumEnu.x+(i%field.width)*field.spacingMeters)*.001f,-3,3);field.gradientNorth[i]=0;field.ridgeRelief[i]=500;}
                for(int i=0;i<field.exposureSectors.Length;i++)field.exposureSectors[i]=1;
                var session=new SoaringAtmosphereSession(field,definition);var s=new FlightDynamicsState{positionUnityMeters=new Vector3(0,100,0),rotationUnity=Quaternion.identity,velocityUnityMetersPerSecond=new Vector3(5,-1,25)};
                var d=new JSBSimNativeDiagnostics{cgXInches=81.56,cgZInches=-2.68,densitySlugsPerCubicFoot=.002};
                var a=AircraftAirflowSampler.Sample(s,d,session,session.Sample(s.positionUnityMeters,0),null,true);
                Assert.That(a.momentBodyFrdNewtonMeters.x,Is.LessThan(0));
                for(int i=0;i<field.gradientEast.Length;i++)field.gradientEast[i]=-field.gradientEast[i];
                var b=AircraftAirflowSampler.Sample(s,d,session,session.Sample(s.positionUnityMeters,0),null,true);
                Assert.That(b.momentBodyFrdNewtonMeters.x,Is.EqualTo(-a.momentBodyFrdNewtonMeters.x).Within(.002));
                Assert.That(b.forceBodyFrdNewtons.z,Is.EqualTo(a.forceBodyFrdNewtons.z).Within(.002));
            }
            finally{UnityEngine.Object.DestroyImmediate(field);UnityEngine.Object.DestroyImmediate(definition);}
        }
        [Test] public void VsiAndIdealEnergyAidAreDistinctAndResetWithoutSpike()
        {
            var instrument=new SoaringFlightInstruments();var air=new JSBSimNativeAirData{pressureAltitudeM=2000,trueAirspeedMps=30};instrument.Sample(air,0);
            for(int i=1;i<=240;i++)
            {
                air.pressureAltitudeM=2000+i*.02;air.trueAirspeedMps=Math.Sqrt(900-2*9.80665*(air.pressureAltitudeM-2000));instrument.Sample(air,i/120d);
            }
            Assert.That(instrument.PressureVsiFpm,Is.GreaterThan(250));Assert.That(Math.Abs(instrument.IdealTotalEnergyFpm),Is.LessThan(.00001));
            double held=instrument.PressureVsiFpm;instrument.Sample(air,2);Assert.That(instrument.PressureVsiFpm,Is.EqualTo(held));
            instrument.Reset();air.pressureAltitudeM=5000;instrument.Sample(air,0);Assert.That(instrument.IdealTotalEnergyFpm,Is.Zero);Assert.That(instrument.PressureVsiFpm,Is.Zero);
        }
        [Test] public void WakeHistoryOutlastsItsSmoothAgeCutoff()
        {
            var wake=new TowWakeField();var d=new JSBSimNativeDiagnostics{trueAirspeedKnots=55,densitySlugsPerCubicFoot=.002,aeroForceZPounds=-1200};
            var s=new FlightDynamicsState{rotationUnity=Quaternion.identity,altitudeAglMeters=100};
            for(int i=0;i<=1440;i++){s.simulationTimeSeconds=i/120d;s.positionUnityMeters=new Vector3(0,100,i*.25f);wake.Record(s,d,Vector3.zero);}
            Assert.That(wake.Count,Is.EqualTo(82));
            // A 7.5s-old parcel must still exist during the fade; the old10Hz
            // ring discarded it at6.3s, creating an artificial rear boundary.
            Assert.That(wake.Sample(new Vector3(0,85,135),12).sqrMagnitude,Is.GreaterThan(0));
            Assert.That(wake.Sample(new Vector3(0,85,117),12),Is.EqualTo(Vector3.zero));
        }
        [Test] public void WakeIsBoundedHasDownwashAndNoFarFieldOrGroundSingularity()
        {
            var wake=new TowWakeField();var d=new JSBSimNativeDiagnostics{trueAirspeedKnots=55,densitySlugsPerCubicFoot=.002,aeroForceZPounds=-1200};
            var s=new FlightDynamicsState{rotationUnity=Quaternion.identity,altitudeAglMeters=100};
            for(int i=0;i<30;i++){s.simulationTimeSeconds=i*.1;s.positionUnityMeters=new Vector3(0,100,i*3);wake.Record(s,d,Vector3.zero);}
            var at=new Vector3(0,99,65);var center=wake.Sample(at,3);
            Assert.That(center.y,Is.LessThan(0));Assert.That(center.magnitude,Is.LessThan(15));
            var left=wake.Sample(at+Vector3.left*3,3);var right=wake.Sample(at+Vector3.right*3,3);
            Assert.That(left.x,Is.EqualTo(-right.x).Within(.001));Assert.That(left.y,Is.EqualTo(right.y).Within(.001));
            Assert.That(wake.Sample(at+Vector3.right*200,3),Is.EqualTo(Vector3.zero));Assert.That(wake.Sample(at,20),Is.EqualTo(Vector3.zero));
            wake.Reset();s.altitudeAglMeters=0;s.simulationTimeSeconds=0;d.trueAirspeedKnots=0;wake.Record(s,d,Vector3.zero);Assert.That(wake.Sample(s.positionUnityMeters,0),Is.EqualTo(Vector3.zero));
        }
    }
}
