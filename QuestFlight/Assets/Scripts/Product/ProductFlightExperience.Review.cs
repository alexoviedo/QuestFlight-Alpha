using System;
using System.Collections.Generic;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlight.Product
{
    public sealed partial class ProductFlightExperience
    {
        [SerializeField] private LineRenderer flownPath,selectedRoute,lessonDestination;
        public void ConfigureMapLines(LineRenderer path,LineRenderer route,LineRenderer destination)
        {flownPath=path;selectedRoute=route;lessonDestination=destination;}
        private readonly List<Vector3> track=new List<Vector3>(256);
        private readonly float[] frameIntervals=new float[360];
        private int frameIndex,frameCount;
        private float manualSeconds,taxiSeconds,fastTaxiSeconds,approachSeconds,approachMinSpeed,approachMaxSpeed,approachWorstBank;
        private float touchdownOffset,touchdownHeading,rolloutOffset,stopDistance,stopTolerance;
        private bool rolloutStayedOnRunway;
        private bool touchdownObserved,touchdownOnRunway,rolloutObserved,goAroundObserved,approachSeen;
        private bool historyMap;
        public void ShowHistoryOnMap(bool value){historyMap=value;}
        public void RetryHistorySave(){if(!modes.AuthoredTraining.IsGuestSession)SaveHistory();}
        private bool PilotFlying => !modes.AuthoredProductInput.DemoOwnsControls &&
            modes.AuthoredTraining.State!=TrainingSessionState.Assisting && modes.AuthoredTraining.State!=TrainingSessionState.Demonstration;
        private void SampleFrameTiming()
        {
            // CPU-observed frame intervals, never mislabeled as GPU time or headset acceptance.
            if(active==null||Time.timeScale<=0||!Application.isFocused||Time.unscaledDeltaTime<=0)return;
            frameIntervals[frameIndex]=Time.unscaledDeltaTime*1000;
            frameIndex=(frameIndex+1)%frameIntervals.Length;frameCount=Mathf.Min(frameCount+1,frameIntervals.Length);
        }
        private float[] RecentFrameTimings()
        {var result=new float[frameCount];for(int i=0;i<frameCount;i++)result[i]=frameIntervals[(frameIndex-frameCount+i+frameIntervals.Length)%frameIntervals.Length];return result;}
        private string SettingsSummary() => ProductComfortPreferences.BindingSummary+
            $"; deadzone={ProductComfortPreferences.RudderDeadzone:0.00}; curve={ProductComfortPreferences.RudderCurve:0.00}; pitchTravel={ProductComfortPreferences.PitchTravel:0.00}; rollTravel={ProductComfortPreferences.RollTravel:0.00}; trim={ProductComfortPreferences.TrimRate(true):0.00}; reverseTrim={ProductComfortPreferences.ReverseTrim}; text={ProductComfortPreferences.TextScale:0.00}; voice={PlayerPrefs.GetFloat("QuestFlight.VoiceVolume",1):0.00}; aircraft={PlayerPrefs.GetFloat("QuestFlight.AircraftVolume",1):0.00}; resolution={UnityEngine.XR.XRSettings.eyeTextureResolutionScale:0.00}; frameSamples=CPU-observed intervals during unpaused flight";
        private void ResetFlightReview()
        {
            track.Clear();historyMap=false;manualSeconds=taxiSeconds=fastTaxiSeconds=approachSeconds=0;
            approachMinSpeed=float.PositiveInfinity;approachMaxSpeed=approachWorstBank=rolloutOffset=0;
            touchdownObserved=touchdownOnRunway=rolloutObserved=goAroundObserved=approachSeen=false;stopDistance=-1;
        }
        private void BreakFlightPath(){track.Clear();approachSeen=false;approachSeconds=0;approachMinSpeed=float.PositiveInfinity;approachMaxSpeed=approachWorstBank=0;}
        private void SampleFlight(in FlightDynamicsState s,float dt)
        {
            if(track.Count==0||Vector3.Distance(track[track.Count-1],s.positionUnityMeters)>25)
            {if(track.Count==256)track.RemoveAt(0);track.Add(s.positionUnityMeters);}
            if(!PilotFlying)return;manualSeconds+=dt;
            var training=modes.AuthoredTraining;
            if(modes.State==ProductFlightModeCoordinator.FlightModeState.Training && (training.CurrentStage.task==TrainingTask.Taxi||training.CurrentStage.task==TrainingTask.Stop))
            {
                if(s.weightOnWheels){taxiSeconds+=dt;if(s.groundSpeedKnots>8)fastTaxiSeconds+=dt;}
                if(training.CurrentStage.task==TrainingTask.Stop&&s.weightOnWheels&&s.groundSpeedKnots<.7)
                {stopDistance=Vector2.Distance(new Vector2(s.positionUnityMeters.x,s.positionUnityMeters.z),training.CurrentStage.targetEnu);stopTolerance=training.CurrentStage.captureMeters;}
            }
            bool corridor=s.positionUnityMeters.x>-4000&&s.positionUnityMeters.x<4000&&Mathf.Abs(s.positionUnityMeters.z+30)<200;
            bool approaching=(s.positionUnityMeters.x<-630&&Mathf.Abs(Mathf.DeltaAngle((float)s.headingDegrees,90))<35)||
                (s.positionUnityMeters.x>680&&Mathf.Abs(Mathf.DeltaAngle((float)s.headingDegrees,270))<35);
            if(!s.weightOnWheels&&corridor&&approaching&&s.altitudeAglMeters>15&&s.altitudeAglMeters<152.4&&s.verticalSpeedFeetPerMinute<-50)
            {
                approachSeen=true;approachSeconds+=dt;approachMinSpeed=Mathf.Min(approachMinSpeed,(float)s.calibratedAirspeedKnots);
                approachMaxSpeed=Mathf.Max(approachMaxSpeed,(float)s.calibratedAirspeedKnots);approachWorstBank=Mathf.Max(approachWorstBank,Mathf.Abs((float)s.bankDegrees));
            }
            if(approachSeen&&!s.weightOnWheels&&s.verticalSpeedFeetPerMinute>300&&s.altitudeAglMeters>60)goAroundObserved=true;
            if(touchdownObserved&&s.weightOnWheels&&!rolloutObserved)
            {rolloutOffset=Mathf.Max(rolloutOffset,Mathf.Abs(s.positionUnityMeters.z+30));rolloutStayedOnRunway&=TrainingEvaluator.OnRunway(s.positionUnityMeters);if(s.groundSpeedKnots<8)rolloutObserved=true;}
        }
        private void RecordTouchdown(in FlightDynamicsState s)
        {
            if(!PilotFlying)return;
            touchdownObserved=true;touchdownOnRunway=TrainingEvaluator.OnRunway(s.positionUnityMeters);
            touchdownOffset=Mathf.Abs(s.positionUnityMeters.z+30);
            touchdownHeading=Mathf.Min(Mathf.Abs(Mathf.DeltaAngle((float)s.headingDegrees,90)),Mathf.Abs(Mathf.DeltaAngle((float)s.headingDegrees,270)));
            rolloutOffset=touchdownOffset;rolloutObserved=false;rolloutStayedOnRunway=touchdownOnRunway;
        }
        private void CompleteFlightReview()
        {
            active.path=track.ToArray();
            active.recommendedLesson=lessonRun>=0?lessonRun:touchdownObserved||approachSeen?5:taxiSeconds>3?0:1;
            if(manualSeconds<5){active.recommendedLesson=0;active.observations="Observation flight; independent handling was not evaluated.";active.advice="Try Cockpit and taxi, then First flight when comfortable.";return;}
            string observation="";
            if(taxiSeconds>3)observation=fastTaxiSeconds>2?"Taxi speed exceeded the lesson's 8 kt upper limit.":"Taxi speed stayed within the lesson's upper limit.";
            if(stopDistance>=0)observation+=$" Stopped {stopDistance:0} m from the target center.";
            if(approachSeconds>=5)observation+=$" Observed low approach: {approachMinSpeed:0}–{approachMaxSpeed:0} kt; largest bank {approachWorstBank:0}°.";
            if(touchdownObserved)observation+=touchdownOnRunway?$" Runway touchdown: {touchdownOffset:0} m from center; heading error {touchdownHeading:0}°.":"Touchdown was outside the modeled runway; runway landing technique needs review.";
            if(rolloutObserved)observation+=rolloutStayedOnRunway?" Slowed to taxi speed while remaining on the runway.":" Rollout departed the runway before reaching taxi speed.";
            if(goAroundObserved)observation+=" A climb away followed the low approach; landing is optional when the approach is unsettled.";
            active.observations=string.IsNullOrWhiteSpace(observation)?"Manual flying recorded. No complete taxi or landing segment was observed.":observation.Trim();
            active.advice=active.recovered?"Repeat a short activity without position recovery for a continuous review.":
                touchdownObserved&&!touchdownOnRunway?"Practice runway alignment on final; go around if you cannot stay aligned.":
                rolloutObserved&&!rolloutStayedOnRunway?"Keep looking along the runway and slow to taxi speed before leaving it; use small rudder corrections.":
                approachSeconds>=5&&(approachMinSpeed<60||approachMaxSpeed>75)?"Practice stabilizing final approach near the lesson's 65 kt target before the flare; go around if unsettled.":
                approachSeconds>=5&&approachWorstBank>15?"Use smaller alignment corrections on final; go around rather than tighten a low turn.":
                touchdownObserved&&touchdownHeading>10?"Align the nose with the runway before touchdown; avoid large late corrections.":
                fastTaxiSeconds>2?"Reduce power earlier and use brief, smooth braking to maintain walking pace.":
                stopDistance>stopTolerance?"Begin slowing earlier and stop inside the marked target before the runway.":
                goAroundObserved?"Repeat the approach after establishing a steady climb and safe circuit.":
                lessonRun>=0?LessonNextPractice(active.outcome):
                "Try the local circuit lesson to practice a complete approach, landing and rollout.";
        }
        private static string LessonNextPractice(string outcome)
        {
            const string marker="Next practice: ";int start=(outcome??"").IndexOf(marker,StringComparison.Ordinal);
            if(start<0)return "Repeat the current skill, then try with fewer prompts when comfortable.";
            start+=marker.Length;int end=outcome.IndexOf("\n\n",start,StringComparison.Ordinal);
            return outcome.Substring(start,(end<0?outcome.Length:end)-start).Trim();
        }
        private Vector3 MapPoint(Vector3 world,float z)
        {return new Vector3(Mathf.Clamp((world.x-mapCenter.x)/mapSpan,-.5f,.5f)*.58f,Mathf.Clamp((world.z-mapCenter.y)/mapSpan,-.5f,.5f)*.58f,z);}
        private void RefreshMapLines()
        {
            if(flownPath==null||selectedRoute==null||lessonDestination==null)return;
            var history=Current.flights;
            Vector3[] points=historyMap&&history.Count>0?history[Mathf.Clamp(historyIndex,0,history.Count-1)].path:active!=null?track.ToArray():LastRecord?.path;
            flownPath.positionCount=points?.Length??0;
            if(points!=null)for(int i=0;i<points.Length;i++)flownPath.SetPosition(i,MapPoint(points[i],-.005f));
            var route=modes.State==ProductFlightModeCoordinator.FlightModeState.Demo&&!historyMap?modes.AuthoredDemoPilot.RouteDefinition:null;
            selectedRoute.positionCount=route?.waypoints?.Length??0;
            if(route?.waypoints!=null)for(int i=0;i<route.waypoints.Length;i++)selectedRoute.SetPosition(i,MapPoint(route.waypoints[i].positionUnityMeters,-.004f));
            var t=modes.AuthoredTraining;
            bool target=!historyMap&&modes.State==ProductFlightModeCoordinator.FlightModeState.Training&&t.CurrentStage.targetEnu!=Vector2.zero;
            lessonDestination.positionCount=target?9:0;
            if(target){Vector2 p=t.CurrentStage.targetEnu;Vector3 center=MapPoint(new Vector3(p.x,0,p.y),-.006f);for(int i=0;i<9;i++){float a=i*Mathf.PI/4;lessonDestination.SetPosition(i,center+new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*.008f);}}
        }
    }
}
