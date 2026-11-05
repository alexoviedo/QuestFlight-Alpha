using System;
using AlexOviedo.QuestFlight.JSBSim;

namespace QuestFlightLab.Flight.Soaring
{
    // Fixed native-time observations. Basic pressure VSI and ideal TE training
    // aid are separate outputs; neither observes the weather sampler.
    public sealed class SoaringFlightInstruments
    {
        public double PressureAltitudeFeet { get; private set; }
        public double PressureVsiFpm { get; private set; }
        public double IdealTotalEnergyFpm { get; private set; }
        private double previousTime,previousAltitude,previousEnergy;
        private bool initialized;
        public void Reset(){initialized=false;PressureVsiFpm=0;IdealTotalEnergyFpm=0;}
        public void Sample(in JSBSimNativeAirData air,double time)
        {
            double altitude=air.pressureAltitudeM,energy=altitude+air.trueAirspeedMps*air.trueAirspeedMps/(2*9.80665);
            PressureAltitudeFeet=altitude/.3048;
            if(!initialized || time<previousTime || time-previousTime>1)
            {previousAltitude=altitude;previousEnergy=energy;previousTime=time;initialized=true;PressureVsiFpm=0;IdealTotalEnergyFpm=0;return;}
            double dt=time-previousTime;if(dt<=0)return;
            double alpha=1-Math.Exp(-dt/1.5);
            PressureVsiFpm+=alpha*((altitude-previousAltitude)/dt*196.8503937-PressureVsiFpm);
            IdealTotalEnergyFpm+=alpha*((energy-previousEnergy)/dt*196.8503937-IdealTotalEnergyFpm);
            previousAltitude=altitude;previousEnergy=energy;previousTime=time;
        }
    }
}
