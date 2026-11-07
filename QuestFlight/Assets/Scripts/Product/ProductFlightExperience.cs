using System;

using System.IO;

using System.Collections.Generic;

using QuestFlight.Product.Training;

using QuestFlightLab.Flight.Backends;

using UnityEngine;

namespace QuestFlight.Product

{

    /// <summary>Presentation and bounded local records only. The existing native authority owns every flight.</summary>

    public sealed partial class ProductFlightExperience : MonoBehaviour

    {

        [Serializable] public sealed class Record

        {

            public string utc, mode, outcome, advice, build;

            public string aircraftId, recommendedLessonId, startRecipe, startLessonId;
            public int startRunway;

            public float seconds, nauticalMiles;

            public int landings;public float lastLandingSink;

            public double latitude, longitude, altitudeFeet;

            public bool assisted, recovered;

            public string observations;

            public int recommendedLesson;

            public Vector3[] path;

            // Keep the complete lesson result in the saved record. The history card

            // shows its heading plus observations and a next action, rather than

            // duplicating a long lesson debrief above an already populated menu.

            public string Display => $"{(aircraftId=="sgs233.reference"?"SGS 2-33A reference":aircraftId=="c172"?"C172":"Aircraft not recorded")} | {mode} · {seconds/60:0} min · {nauticalMiles:0.0} NM\n{(outcome??"").Split('\n')[0]}\n"+

                (string.IsNullOrEmpty(observations)?"":observations+"\n")+(assisted?"Demo / instructor assistance used. ":"Manual flight. ")+(recovered?"Position recovery used. ":"")+"\nNext: "+advice;

        }

        [Serializable] public sealed class History { public List<Record> flights=new List<Record>(); }

        [Serializable] private sealed class Issue { public string utc,build,category,mode,phase,controls,objective,settings,navigation;public string[] menuActions;public FlightDynamicsState flight;public float[] recentFrameMilliseconds;public bool appFocused,userPresent; }

        [SerializeField] private ProductFlightModeCoordinator modes;

        [SerializeField] private AudioSource instructor;

        [SerializeField] private GameObject mapRoot;

        [SerializeField] private Transform mapMarker;

        [SerializeField] private Vector2 mapCenter;

        [SerializeField] private float mapSpan=24000;

        [SerializeField] private string buildIdentity;

        private History owner=new History(),guest=new History();

        private Record active;

        private int session=-1,lessonRun=-1,historyIndex;

        private TrainingSessionState previousTraining;

        private Vector3 previousPosition;

        private double previousSimTime;

        private float nextUpdate,completeWait;

        private bool summaryShown,wasGuest;

        private int recoveries;

        private bool wasGrounded;

        private float lastSink;

        private ProductMapBoundaryRecovery boundary;

        private TMPro.TextMeshPro[] mapLabels;

        private float[] mapFontSizes;

        public Record LastRecord { get; private set; }

        public bool LastReportSucceeded {get;private set;}
        public string ReportStatus { get; private set; }="Reports stay on this headset; nothing is uploaded.";

        public string SaveStatus { get; private set; }="";

        public string Build => Application.version+" / "+(string.IsNullOrEmpty(Application.buildGUID)?buildIdentity:Application.buildGUID.Substring(0,8));

        private History Current => modes.AuthoredTraining.IsGuestSession?guest:owner;

        public string OwnerLabel => modes.State==ProductFlightModeCoordinator.FlightModeState.Selection?"Choose a flight":modes.State==ProductFlightModeCoordinator.FlightModeState.Demo?modes.AuthoredDemoPilot.OwnershipLabel:

            modes.AuthoredTraining.State==TrainingSessionState.Assisting||modes.AuthoredTraining.State==TrainingSessionState.Demonstration||modes.AuthoredTraining.AssistancePaused||modes.AuthoredTraining.DemonstrationPaused?(Time.timeScale>0?"Instructor controls":"Instructor controls on resume"):(Time.timeScale>0?"Your controls":"Your controls on resume");

        public string Briefing => modes.IsGlider?"BOULDER SGS REFERENCE\nGround aerotow: complete the stopped control checks or skip preflight without checklist credit. Tow release is checked in flight. Crew preparation and real launch clearance still apply. Runway/weather combinations and routes are provisional. Airborne practice begins in flight. Use the selected lesson targets.\n"+LocalWind:"BOULDER MUNICIPAL · KBDU\n"+LocalWind+" · Runways 08 / 26\n"+

            "Non-towered · COM1 122.725 · pattern 6,300 ft MSL.\n"+

            "Free flight: your pace. Scenic demo: allow about 12–18 min for the tour and landing; traffic or wind may extend it. Menu lets you take or hand back control.\n"+

            "Activities offer a short circuit or final-approach practice. Keep the scenery boundary in mind.";

        public string Reference => modes.IsGlider?"SGS 2-33A - REFERENCE CONFIGURATION\nNo engine, throttle or powered go-around. Follow active lesson speeds. The combined lever opens airbrakes, then applies wheel brake at full travel. Grab the separate trim lever. Red handle or A in flight releases tow.\nKeep a defensible unpowered landing plan; operator-specific procedures remain deferred.":"C172 TRAINER · SIMULATOR REFERENCE\n"+

            "Useful lesson speeds: climb 75 kt; landing final 65 kt. Follow the active lesson when its target differs.\n"+

            "Takeoff: align, full power, check airspeed rising; follow the lesson's rotation target.\n"+

            "Fuel, mixture, ignition and carb heat are prepared by the checkpoint; their selectors are not interactive. Follow the lesson for power and flaps.\n"+

            "Final: follow the active lesson speed and aim point. If unstable, go around.\n"+

            (modes.State==ProductFlightModeCoordinator.FlightModeState.Training ? "Current: "+modes.AuthoredTraining.CurrentInstruction : "Local orientation: airport east of town; foothills west; reservoir northwest.");

        private string LocalWind

        {

            get

            {

                var flight=modes.AuthoredFlightCoordinator;if(flight.windField==null)return "Wind unavailable";

                var state=flight.CurrentState;Vector3 w=flight.SampleWind(state.positionUnityMeters,state.simulationTimeSeconds);

                float knots=new Vector2(w.x,w.z).magnitude*1.943844f,from=Mathf.Repeat(Mathf.Atan2(-w.x,-w.z)*Mathf.Rad2Deg,360);

                return $"WIND HERE {from:000}° / {knots:0} KT";

            }

        }

        public void Configure(ProductFlightModeCoordinator m,AudioSource voice,GameObject map,Transform marker,Vector2 center,float span,string build)

        {modes=m;instructor=voice;mapRoot=map;mapMarker=marker;mapCenter=center;mapSpan=span;buildIdentity=build;}

#if UNITY_EDITOR
        public string EditorVerificationSaveRoot { get; set; }
#endif
        private string SaveRoot
        {
            get
            {
#if UNITY_EDITOR
                if(!string.IsNullOrEmpty(EditorVerificationSaveRoot))return EditorVerificationSaveRoot;
#endif
                return Application.persistentDataPath;
            }
        }
        private void Start()

        {

            if(modes==null){enabled=false;return;}

            boundary=modes.GetComponent<ProductMapBoundaryRecovery>();

            try { string p=Path.Combine(SaveRoot,"flight-history.json");owner=ProductLocalSave.Read(p,text=>JsonUtility.FromJson<History>(text),out bool recovered)??new History();if(recovered)SaveStatus="Recovered the previous saved history. Your latest flight may be missing."; }

            catch(Exception e){SaveStatus="Saved history could not be read. New flights can still be recorded.";Debug.LogWarning("[FlightHistory] Could not read local history: "+e.Message);}

            if(owner.flights==null)owner.flights=new List<Record>();

            owner.flights.RemoveAll(r=>r==null);

            if(owner.flights.Count>20)owner.flights.RemoveRange(20,owner.flights.Count-20);

            if(mapRoot!=null){mapLabels=mapRoot.GetComponentsInChildren<TMPro.TextMeshPro>(true);mapFontSizes=new float[mapLabels.Length];for(int i=0;i<mapLabels.Length;i++)mapFontSizes[i]=mapLabels[i].fontSize;}

            ShowMap(false);

        }

        private void Update()

        {

            SampleFrameTiming();

            if(modes==null || !modes.AuthoredFlightCoordinator.IsInitialized)return;

            var training=modes.AuthoredTraining;

            if(wasGuest!=training.IsGuestSession){guest=new History();LastRecord=null;historyIndex=0;wasGuest=training.IsGuestSession;}

            bool flight=modes.State==ProductFlightModeCoordinator.FlightModeState.FreeFlight||modes.State==ProductFlightModeCoordinator.FlightModeState.Demo;

            if(flight && session!=modes.FlightSessionVersion) {FinishFlight("Session ended");Begin(modes.State.ToString());session=modes.FlightSessionVersion;}

            if(modes.State==ProductFlightModeCoordinator.FlightModeState.Training && training.State==TrainingSessionState.Preparing && previousTraining!=training.State)

                FinishFlight("Exercise restarted");

            // Start after the normal lesson checkpoint has loaded. A lesson's

            // authored relocation is not an in-flight recovery or flown distance.

            if(modes.State==ProductFlightModeCoordinator.FlightModeState.Training && training.IsFlying && active==null)

            {Begin(training.CurrentLessonName);lessonRun=training.SelectedLesson;}

            if(training.State==TrainingSessionState.Debrief && previousTraining!=training.State && active!=null)

                FinishFlight(training.LastDebrief);

            previousTraining=training.State;

            if(active!=null && Time.timeScale>0)

            {

                active.seconds+=Time.deltaTime;

                var live=modes.AuthoredFlightCoordinator.CurrentState;

                if(!live.weightOnWheels)lastSink=Mathf.Max(0,-(float)live.verticalSpeedFeetPerMinute);

                if(!wasGrounded && live.weightOnWheels && live.simulationTimeSeconds>=previousSimTime && Vector3.Distance(live.positionUnityMeters,previousPosition)<300)

                {active.landings++;active.lastLandingSink=lastSink;RecordTouchdown(live);}

                wasGrounded=live.weightOnWheels;

                active.assisted|=modes.State==ProductFlightModeCoordinator.FlightModeState.Demo&&!modes.AuthoredDemoPilot.UserHasControls || training.State==TrainingSessionState.Assisting||training.State==TrainingSessionState.Demonstration;

            }

            if(Time.unscaledTime<nextUpdate)return;nextUpdate=Time.unscaledTime+.5f;

            var state=modes.AuthoredFlightCoordinator.CurrentState;

            if(active!=null && state.IsFinite)

            {

                float distance=Vector3.Distance(previousPosition,state.positionUnityMeters);

                double elapsed=state.simulationTimeSeconds-previousSimTime;

                if(elapsed>0 && elapsed<2 && distance<300)active.nauticalMiles+=distance/1852;

                else if(distance>300||elapsed<0)active.recovered=true;

                previousPosition=state.positionUnityMeters;previousSimTime=state.simulationTimeSeconds;

                active.latitude=state.latitudeDegrees;active.longitude=state.longitudeDegrees;active.altitudeFeet=state.altitudeMslMeters*3.28084;

                active.recovered|=boundary!=null && boundary.CompletedRecoveryCount!=recoveries;

                if(Time.timeScale>0 && elapsed>0 && elapsed<2 && distance<300)SampleFlight(state,(float)elapsed);

                else if(distance>300||elapsed<0)BreakFlightPath();

            }

            if(mapRoot!=null && mapRoot.activeSelf && mapMarker!=null)

            {

                Vector2 p=(new Vector2(state.positionUnityMeters.x,state.positionUnityMeters.z)-mapCenter)/mapSpan;

                mapMarker.localPosition=new Vector3(Mathf.Clamp(p.x,-.5f,.5f)*.58f,Mathf.Clamp(p.y,-.5f,.5f)*.58f,-.003f);

                mapMarker.localRotation=Quaternion.Euler(0,0,-(float)state.headingDegrees);

                RefreshMapLines();

            }

            if(modes.State==ProductFlightModeCoordinator.FlightModeState.Demo && (modes.AuthoredDemoPilot.Phase=="complete_clear_of_runway"||modes.AuthoredDemoPilot.Phase=="sgs_complete") && !summaryShown && !modes.AuthoredNavigation.IsOpen)

            {

                completeWait+=.5f;

                if(completeWait>4 && (instructor==null||!instructor.isPlaying))

                {FinishFlight(modes.AuthoredDemoPilot.IsSoaringDemo?"Soaring tour completed - landed and stopped; crew movement required":"Tour completed - stopped clear of the runway");summaryShown=true;modes.AuthoredNavigation.ShowFlightSummary();}

            }

            else completeWait=0;

        }

        private void Begin(string name)

        {

            active=new Record {utc=DateTime.UtcNow.ToString("s"),mode=name,build=Build,aircraftId=modes.IsGlider?"sgs233.reference":"c172"};summaryShown=false;completeWait=0;

            active.startRecipe=modes.State==ProductFlightModeCoordinator.FlightModeState.Training?"lesson":modes.State==ProductFlightModeCoordinator.FlightModeState.Demo?(modes.AuthoredDemoPilot.ShortShowcase?"showcase":"demo"):modes.AuthoredFlightCoordinator.Operation!=null?"ground":"free";
            active.startLessonId=active.startRecipe=="lesson"?modes.AuthoredTraining.CurrentLessonId:null;
            active.startRunway=Math.Max(0,Array.IndexOf(modes.AuthoredFlightCoordinator.soaringOperations,modes.AuthoredFlightCoordinator.Operation?.Definition));
            ResetFlightReview();

            var s=modes.AuthoredFlightCoordinator.CurrentState;previousPosition=s.positionUnityMeters;previousSimTime=s.simulationTimeSeconds;

            wasGrounded=s.weightOnWheels;lastSink=0;recoveries=boundary==null?0:boundary.CompletedRecoveryCount;

        }

        public void FinishFlight(string outcome)

        {

            if(active==null)return;

            active.outcome=outcome;

            CompleteFlightReview();

            if(active.aircraftId=="sgs233.reference")active.recommendedLessonId=lessonRun>=0&&lessonRun<modes.AuthoredTraining.Lessons.Length?modes.AuthoredTraining.Lessons[lessonRun].lessonId:touchdownObserved||approachSeen?"sgs233.reference.circuit_landing":"sgs233.reference.preparation";

            else if(active.aircraftId=="c172")active.recommendedLessonId=modes.AuthoredTraining.Lessons[Mathf.Clamp(active.recommendedLesson,0,modes.AuthoredTraining.Lessons.Length-1)].lessonId;

            LastRecord=active;Current.flights.Insert(0,active);if(Current.flights.Count>20)Current.flights.RemoveAt(20);

            active=null;lessonRun=-1;historyIndex=0;

            if(!modes.AuthoredTraining.IsGuestSession)SaveHistory();

        }

        private void SaveHistory()

        {

            try {string p=Path.Combine(SaveRoot,"flight-history.json");ProductLocalSave.Write(p,JsonUtility.ToJson(owner,true));SaveStatus="";}

            catch(Exception e){SaveStatus="History could not be saved to storage. This session's summary is still available. Free space and retry saving.";Debug.LogWarning("[FlightHistory] Local save failed: "+e.Message);}

        }

        public string HistoryText => Current.flights.Count==0?"No completed flights yet. End a flight from the pause menu to save a summary.":

            $"{historyIndex+1} / {Current.flights.Count} · {FriendlyTimestamp(Current.flights[historyIndex].utc)}\n"+Current.flights[historyIndex].Display;

        public void NextHistory(int delta) {historyIndex=Mathf.Clamp(historyIndex+delta,0,Mathf.Max(0,Current.flights.Count-1));}

        public void ShowMap(bool show)

        {

            if(mapRoot!=null && mapRoot.activeSelf!=show)mapRoot.SetActive(show);

            if(show&&mapLabels!=null)for(int i=0;i<mapLabels.Length;i++)mapLabels[i].fontSize=mapFontSizes[i]*ProductComfortPreferences.TextScale;

            if(show){if(mapMarker!=null)mapMarker.gameObject.SetActive(!historyMap);RefreshMapLines();}

        }

        public void Report(string category)

        {
            LastReportSucceeded=false;

            try

            {

                var issue=new Issue{utc=DateTime.UtcNow.ToString("o"),build=Build,category=category,mode=modes.State.ToString(),phase=modes.State==ProductFlightModeCoordinator.FlightModeState.Training?modes.AuthoredTraining.CurrentLessonName+" / "+modes.AuthoredTraining.State+" / "+modes.AuthoredTraining.StageIndex:modes.IsGlider?(modes.AuthoredFlightCoordinator.Operation?.Phase.ToString()??"airborne checkpoint"):modes.State==ProductFlightModeCoordinator.FlightModeState.Demo?modes.AuthoredDemoPilot.Phase:"manual flight",controls=OwnerLabel,flight=modes.AuthoredFlightCoordinator.CurrentState,

                    navigation=modes.AuthoredNavigation.WorkflowDiagnostic,menuActions=modes.AuthoredNavigation.RecentMenuActions,objective=modes.State==ProductFlightModeCoordinator.FlightModeState.Training?modes.AuthoredTraining.CurrentInstruction:"",settings=SettingsSummary(),recentFrameMilliseconds=RecentFrameTimings(),appFocused=Application.isFocused,userPresent=OVRManager.instance==null||OVRManager.instance.isUserPresent};

                string dir=Path.Combine(SaveRoot,"Feedback");Directory.CreateDirectory(dir);

                string reportId=DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");

                File.WriteAllText(Path.Combine(dir,reportId+".json"),JsonUtility.ToJson(issue,true));

                var files=new DirectoryInfo(dir).GetFiles("*.json");Array.Sort(files,(a,b)=>b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

                for(int i=20;i<files.Length;i++)files[i].Delete();

                LastReportSucceeded=true;ReportStatus="Report "+reportId+" saved: "+category+".\nTell the developer this ID, what you expected and what happened. Details stay on this headset until USB export.";

            }

            catch(Exception e){ReportStatus="Could not save report. Please mention the issue and build number to the developer.";Debug.LogWarning("[Feedback] "+e.Message);}

        }

    }

}
