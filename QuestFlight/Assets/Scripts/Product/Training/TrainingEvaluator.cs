using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlight.Product.Training
{
    /// <summary>Bounded, deterministic practice evaluation. Tolerances are beginner exercise policy.</summary>
    public sealed class TrainingEvaluator
    {
        public float HeldSeconds { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public string Feedback { get; private set; } = "";
        public bool Complete { get; private set; }
        public int ControlPhase { get; private set; }
        public bool IsControlCheck(TrainingTask task) => task==TrainingTask.RollControl || task==TrainingTask.PitchControl || task==TrainingTask.RudderControl;
        public bool TimedOut { get; private set; }
        public bool Assisted { get; private set; }
        public bool Landed { get; private set; }
        public bool BadTouchdown { get; private set; }
        public float TouchdownSinkFpm { get; private set; }
        public float MaximumAltitudeError { get; private set; }
        public float MaximumHeadingError { get; private set; }
        public float GoodSeconds { get; private set; }
        public float TotalSeconds { get; private set; }
        private float outsideSeconds, startTrim, startFlaps;
        private bool trimMoved, hadAirborne;
        private bool previousWheels;
        private float previousSink;

        public void BeginAttempt(in FlightDynamicsState state)
        {
            Assisted = Landed = BadTouchdown = false;
            TouchdownSinkFpm = MaximumAltitudeError = MaximumHeadingError = GoodSeconds = TotalSeconds = 0;
            hadAirborne = !state.weightOnWheels && state.altitudeAglMeters > 3 && state.calibratedAirspeedKnots > 35;
            previousWheels = state.weightOnWheels;
            previousSink = (float)state.verticalSpeedFeetPerMinute;
        }

        public void BeginStage(float trim, float flaps = 0)
        {
            HeldSeconds = ElapsedSeconds = outsideSeconds = 0;
            Complete = TimedOut = trimMoved = false;
            ControlPhase=0;
            startTrim = trim;
            startFlaps = flaps;
            Feedback = "Follow the instruction above.";
        }

        public void MarkAssisted() => Assisted = true;

        // Called for EVERY native step: a one-frame contact cannot disappear between 10 Hz samples.
        public void ObserveNative(in FlightDynamicsState state)
        {
            if (!state.IsFinite) return;
            if (!state.weightOnWheels && state.altitudeAglMeters > 3 && state.calibratedAirspeedKnots > 35) hadAirborne = true;
            if (hadAirborne && state.weightOnWheels && !previousWheels)
            {
                Landed = true;
                TouchdownSinkFpm = Mathf.Max(TouchdownSinkFpm, -previousSink);
                BadTouchdown |= previousSink < -600 || Mathf.Abs((float)state.bankDegrees) > 10 ||
                    Mathf.Abs((float)state.pitchDegrees) > 14 || !OnRunway(state.positionUnityMeters);
            }
            previousWheels = state.weightOnWheels;
            previousSink = (float)state.verticalSpeedFeetPerMinute;
        }

        public void Step(TrainingStage stage, in FlightDynamicsState s,
            in FlightDynamicsControlSnapshot c, float dt, bool eligible,
            float indicatedAltitudeFeet=float.NaN, float indicatedSpeedKnots=float.NaN, float indicatedVerticalSpeed=float.NaN)
        {
            if (!eligible || Complete || !s.IsFinite || dt <= 0 || dt > .25f) return;
            ElapsedSeconds += dt;
            TotalSeconds += dt;
            float alt = float.IsNaN(indicatedAltitudeFeet) ? (float)(s.altitudeMslMeters * FlightFrameConversions.MetersToFeet) : indicatedAltitudeFeet;
            float altError = Mathf.Abs(alt - stage.altitudeFeet);
            float headingError = Mathf.Abs(Mathf.DeltaAngle((float)s.headingDegrees, stage.heading));
            float bank = (float)s.bankDegrees;
            float speed = float.IsNaN(indicatedSpeedKnots) ? (float)s.calibratedAirspeedKnots : indicatedSpeedKnots;
            float vs = float.IsNaN(indicatedVerticalSpeed) ? (float)s.verticalSpeedFeetPerMinute : indicatedVerticalSpeed;
            float agl = (float)(s.altitudeAglMeters * FlightFrameConversions.MetersToFeet);
            bool stable = altError <= 150 && headingError <= 12 && Mathf.Abs(bank) <= 8 &&
                Mathf.Abs(vs) <= 250 && Mathf.Abs(speed - stage.speedKnots) <= 15;
            bool near = Vector2.Distance(new Vector2(s.positionUnityMeters.x, s.positionUnityMeters.z), stage.targetEnu) <= stage.captureMeters;
            bool good = false;
            string correction = "Use small corrections; scan outside and check the instruments.";
            switch (stage.task)
            {
                case TrainingTask.RollControl:
                case TrainingTask.PitchControl:
                case TrainingTask.RudderControl:
                    float axis = stage.task == TrainingTask.RollControl ? c.aileron : stage.task == TrainingTask.PitchControl ? c.elevator : c.rudder;
                    bool stopped=s.weightOnWheels && s.groundSpeedKnots<1 && c.throttle<.2f;
                    if(stopped && ControlPhase==0 && axis<-.25f)ControlPhase=1;
                    else if(stopped && ControlPhase==1 && axis>.25f)ControlPhase=2;
                    good = stopped && ControlPhase==2 && Mathf.Abs(axis)<.15f;
                    correction = !stopped ? "Close the throttle and hold both brakes. Check the controls while stopped." : ControlInstruction(stage.task,ControlPhase);
                    break;
                case TrainingTask.BrakeControl:
                    good = c.leftToeBrake > .35f && c.rightToeBrake > .35f && s.groundSpeedKnots < 1 && c.throttle < .2f;
                    correction = "Hold both brakes with the throttle at idle."; break;
                case TrainingTask.Taxi:
                    good = s.weightOnWheels && s.groundSpeedKnots >= 2 && s.groundSpeedKnots <= 8 && headingError <= 15 && near;
                    correction = "Follow the taxiway at walking speed; steer with rudder."; break;
                case TrainingTask.Stop:
                    good = s.weightOnWheels && s.groundSpeedKnots < .7 && c.throttle < .2f && near && s.positionUnityMeters.z < -60;
                    correction = "Idle and brake at the hold-short point, before the runway."; break;
                case TrainingTask.Level:
                    bool envelope = Mathf.Abs(bank)<=8 && speed>=65 && speed<=120 && agl>300 && Mathf.Abs(vs)<800;
                    good = stage.attitudeOnly ? envelope && Mathf.Abs(bank)<=6 :
                        stage.reference==TrainingReference.Altimeter ? envelope && altError<=150 :
                        stage.reference==TrainingReference.Airspeed ? envelope && Mathf.Abs(speed-stage.speedKnots)<=12 :
                        stage.reference==TrainingReference.Heading ? envelope && headingError<=12 : stable;
                    correction = altError > 150 ? "Ease back toward the target altitude." : headingError > 12 ? "Return to the target heading." : "Level the wings and settle the speed and climb rate.";
                    MaximumAltitudeError = Mathf.Max(MaximumAltitudeError, altError);
                    MaximumHeadingError = Mathf.Max(MaximumHeadingError, headingError); break;
                case TrainingTask.Turn:
                    good = headingError <= 8 && altError <= 150 && Mathf.Abs(bank) <= 6 && speed>=75 && speed<=105 && Mathf.Abs((float)s.sideslipDegrees) < 8;
                    correction = headingError>8 ? "Turn gently toward the target heading; begin leveling before reaching it." : "The heading is close. Level the wings and settle altitude and airspeed."; break;
                case TrainingTask.Climb:
                    float climbLead=Mathf.Clamp(vs/10f,25,100);
                    good=!s.weightOnWheels && alt>=stage.altitudeFeet-climbLead && alt<=stage.altitudeFeet+150 && headingError<15 &&
                        (vs>100 || Mathf.Abs(alt-stage.altitudeFeet)<=100 && Mathf.Abs(vs)<=250) && speed>=65 && speed<=90;
                    correction="Climb at 70 to 80 knots. Begin leveling just before the target altitude.";
                    break;
                case TrainingTask.Departure:
                    good = !s.weightOnWheels && alt >= stage.altitudeFeet && headingError < 15 && vs > 100 && speed >= 65 && speed <= 90;
                    correction = "Hold the departure heading and a positive climb at 70 to 80 knots."; break;
                case TrainingTask.Descend:
                    good = alt <= stage.altitudeFeet + Mathf.Clamp(-vs/10f,25,80) && alt >= stage.altitudeFeet - 150 &&
                        (vs < -100 && vs > -800 || Mathf.Abs(alt-stage.altitudeFeet)<=100 && Mathf.Abs(vs)<=250) && speed >= 75 && speed <= 110 && headingError < 15;
                    correction = "Reduce power; descend gently toward the target altitude."; break;
                case TrainingTask.Trim:
                    trimMoved |= Mathf.Abs(c.trim - startTrim) > .025f;
                    good = trimMoved && stable && Mathf.Abs(c.elevator) < .18f;
                    correction = "Use the trim wheel to reduce held yoke pressure, then settle level."; break;
                case TrainingTask.TakeoffConfiguration:
                    good = s.weightOnWheels && s.groundSpeedKnots < 2 && headingError < 5 && OnRunway(s.positionUnityMeters) && c.flaps <= .34f && c.mixture > .7f && c.carbHeat < .1f;
                    correction = "Check runway alignment, takeoff flaps, mixture and carb heat."; break;
                case TrainingTask.TakeoffRoll:
                    good = s.weightOnWheels && speed >= 50 && speed <= 65 && c.throttle > .9f && headingError < 8 && Mathf.Abs(s.positionUnityMeters.z + 30) < 9;
                    correction = "Apply full power smoothly; keep the centerline as speed builds."; break;
                case TrainingTask.Rotation:
                    good = !s.weightOnWheels && agl > 15 && speed >= 50 && speed < 90 && s.pitchDegrees > 1 && s.pitchDegrees < 15;
                    correction = "Near 55 knots, gently raise the nose and establish the climb."; break;
                case TrainingTask.Downwind:
                    good = near && stable && c.flaps <= .34f;
                    correction = "Fly the north-side downwind, south of Jay Road, at 6300 feet MSL."; break;
                case TrainingTask.Abeam:
                    good = near && headingError < 15 && vs < -100 && speed >= 65 && speed <= 90 && c.flaps >= .25f;
                    correction = "Abeam the landing end: reduce power, select approach flaps and begin descending."; break;
                case TrainingTask.Base:
                    good = near && headingError < 20 && speed >= 60 && speed <= 85 && Mathf.Abs(bank) < 30;
                    correction = "Make a gentle left base turn; avoid tightening the turn if overshooting."; break;
                case TrainingTask.Final:
                    float distanceToAim = -470 - s.positionUnityMeters.x;
                    float slopeDegrees = Mathf.Atan2((float)(s.altitudeMslMeters - 5288 * .3048), Mathf.Max(1, distanceToAim)) * Mathf.Rad2Deg;
                    good = !s.weightOnWheels && agl >= 120 && agl <= 500 && distanceToAim > 300 && slopeDegrees >= 2 && slopeDegrees <= 5 &&
                        headingError <= 10 && Mathf.Abs(s.positionUnityMeters.z + 30) < 45 && speed >= 60 && speed <= 75 && vs > -700 && vs < -50 && Mathf.Abs(bank) < 10 && c.flaps >= .25f;
                    correction = "Align with the runway, hold 60 to 70 knots, and keep a steady descent. Go around if unstable."; break;
                case TrainingTask.Landing:
                    good = Landed && !BadTouchdown && s.weightOnWheels && OnRunway(s.positionUnityMeters);
                    correction = "Look along the runway, reduce power and ease into the flare. A go-around is always an option."; break;
                case TrainingTask.Rollout:
                    good = s.weightOnWheels && OnRunway(s.positionUnityMeters) && s.groundSpeedKnots < 8 && c.throttle < .2f && headingError < 15;
                    correction = "Keep directional control, lower the nose and brake smoothly to taxi speed."; break;
                case TrainingTask.GoAroundPower:
                    good = c.throttle > .9f && c.carbHeat < .1f && c.flaps <= .70f && c.flaps >= Mathf.Min(startFlaps,.67f)-.12f && s.pitchDegrees >= 2 && speed >= 50 && !s.weightOnWheels;
                    correction = "Full power, carb heat cold; establish the climb and reduce full flaps to 20 degrees."; break;
                case TrainingTask.GoAroundClimb:
                    good = !s.weightOnWheels && vs > 150 && speed >= 60 && c.flaps <= .38f && c.flaps >= Mathf.Min(startFlaps,.33f)-.1f && headingError < 15;
                    correction = "With positive climb and safe speed, reduce flaps to 10 degrees."; break;
                case TrainingTask.GoAroundClean:
                    good = agl > 200 && vs > 100 && speed >= 65 && speed <= 90 && c.flaps < .08f && headingError < 15;
                    correction = "Clear of obstacles, retract remaining flaps and continue climbing straight ahead."; break;
            }
            // Short excursions retain progress; longer excursions require a fresh stable interval.
            if (good) { HeldSeconds += dt; GoodSeconds += dt; outsideSeconds = 0; }
            else { outsideSeconds += dt; if (outsideSeconds > .6f) HeldSeconds = 0; }
            Feedback = good ? IsControlCheck(stage.task) ? "Movement checked. Keep the control centered." : "On target. Keep the correction small and continue scanning." : correction;
            Complete = good && HeldSeconds >= stage.dwellSeconds;
            TimedOut = ElapsedSeconds >= stage.timeoutSeconds;
        }

        public static string ControlInstruction(TrainingTask task,int phase)
        {
            if(task==TrainingTask.RollControl)return phase==0?"Turn the yoke left.":phase==1?"Left checked. Turn the yoke right.":"Both directions checked. Return the yoke to the middle.";
            if(task==TrainingTask.PitchControl)return phase==0?"Push the yoke away from you.":phase==1?"Forward checked. Pull the yoke toward you.":"Both directions checked. Return the yoke to its middle position.";
            return phase==0?"Move the steering thumbstick left.":phase==1?"Left checked. Move the steering thumbstick right.":"Both directions checked. Let the steering thumbstick center.";
        }

        public static bool OnRunway(Vector3 p) => p.x >= -630 && p.x <= 680 && Mathf.Abs(p.z + 30) <= 11;
    }
}
