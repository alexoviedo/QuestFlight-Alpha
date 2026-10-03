using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace QuestFlight.Editor
{
    /// <summary>All training assets and presentation objects are serialized before building.</summary>
    public static class ProductTrainingAuthoring
    {
        public const string Root = "Assets/Production/Training";
        public const string World = ProductFlightModesFoundationAuthoring.WorldPrefabPath;
        private static readonly List<VoiceScript> scripts = new List<VoiceScript>();
        [Serializable] private sealed class VoiceScript { public string id; public string text; }
        [Serializable] private sealed class VoiceManifest { public VoiceScript[] clips; }

        public static void RunBatch()
        {
            try { ProductFlightModesFoundationAuthoring.Apply(); Apply(); Debug.Log("[TrainingAuthoring] SUCCESS"); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("QuestFlight/Product/Author Flight Training V1")]
        public static void Apply()
        {
            Directory.CreateDirectory(Root + "/Voice"); AssetDatabase.Refresh(); scripts.Clear();
            var world = PrefabUtility.LoadPrefabContents(World);
            try
            {
                var flight = world.GetComponentInChildren<FlightDynamicsCoordinator>(true);
                var mode = world.GetComponentInChildren<ProductFlightModeCoordinator>(true);
                var input = world.GetComponentInChildren<ProductControlInput>(true);
                var seat = world.GetComponentInChildren<ProductionSeatCalibrationController>(true);
                var lessons = BuildCatalog(flight);
                var calibration = world.transform.Find("AircraftSimulationRoot/" + ProductFlightModesFoundationAuthoring.CalibrationPath);
                if (calibration == null) throw new InvalidOperationException("Authored calibration hierarchy missing.");
                var training = input.GetComponent<ProductTrainingController>();
                if (training == null) training = input.gameObject.AddComponent<ProductTrainingController>();
                var ui = Child(calibration, "FlightTrainingPresentation");
                var template = mode.AuthoredModePanelRoot.GetComponentsInChildren<TextMesh>(true).First(t => t.font != null);
                var panel = Child(ui, "PausedLessonPanel");
                var cue = Child(ui, "FlyingLessonCue");
                var text = Text(panel, "LessonText", template, new Vector3(-.48f, .30f, 1.15f), .0065f, 48);
                var caption = Text(cue, "Instruction", template, new Vector3(-.04f, .33f, .90f), .0045f, 44);
                Backplate(panel, new Vector3(0, -.03f, 1.165f), new Vector3(1.05f, .78f, .005f));
                Backplate(cue, new Vector3(.30f, .235f, .92f), new Vector3(.72f, .22f, .006f));
                text.text = "FLIGHT LESSONS"; caption.text = "YOUR CONTROLS";
                panel.gameObject.SetActive(false); cue.gameObject.SetActive(false);
                var audio = ui.GetComponent<AudioSource>(); if (audio == null) audio = ui.gameObject.AddComponent<AudioSource>();
                audio.playOnAwake = false; audio.spatialBlend = 0; audio.volume = .8f; audio.priority = 32;
                training.ConfigureForAuthoring(mode, flight, input, seat, panel.gameObject, text, cue.gameObject, caption, audio, lessons);
                training.ConfigureVoiceForAuthoring(
                    Voice("demonstration", "I have control. Watch the wings stay level against the outside horizon. Briefly cross-check the attitude indicator."),
                    Voice("assistance", "The instructor has control and is holding the aircraft. Open Menu and select Take control when ready."),
                    Voice("handback", "You have control. Assistance is off. X repeats the current instruction; Menu pauses."),
                    Voice("go_around", "The approach is unstable. Go around. Apply full power, carb heat cold, establish the climb and retract flaps in stages."));
                training.ConfigureDemonstrationTurnForAuthoring(Voice("demonstration_turn","I am beginning a gentle left turn. Watch the bank develop, then settle. Cross-check the heading as it changes."));
                mode.ConfigureTrainingForAuthoring(training);
                AuthorNavigation(input.transform, calibration, mode, training, input, seat, template);
                AuthorCoach(world,ui,calibration,training,flight,template);
                // Restore the existing fourth menu capability without adding another card or camera.
                var button = mode.AuthoredModePanelRoot.GetComponentsInChildren<ProductFlightModeMenuButton>(true).Single(b => b.name == "LessonsCard");
                var serialized = new SerializedObject(button);
                serialized.FindProperty("action").enumValueIndex = (int)ProductFlightModeMenuButton.MenuAction.Lessons;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var labels = button.GetComponentsInChildren<TextMesh>(true);
                foreach (var label in labels)
                {
                    if (label.name.EndsWith("Title")) label.text = "FLIGHT LESSONS";
                    else if (label.name.EndsWith("Subtitle")) label.text = "SIX GUIDED EXERCISES";
                    EditorUtility.SetDirty(label); PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(button);
                foreach (var label in mode.AuthoredModePanelRoot.GetComponentsInChildren<TextMesh>(true))
                {
                    if (label.name == "DemoFlightCardTitle") label.text="DEMO FLIGHT";
                    else if (label.name == "FreeFlightCardTitle") label.text="FREE FLIGHT";
                    else if (label.name == "SeatCalibrationCardTitle") label.text="SEAT CALIBRATION";
                    else if (label.text.Contains("MENU") && label.text.Contains("CALIBRATION")) label.text="MENU: APP MENU    B / Y: BACK";
                    else if (label.text.Contains("Menu") && label.text.Contains("calibration")) label.text="Menu: app menu    B / Y: back";
                    EditorUtility.SetDirty(label); PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                }
                EditorUtility.SetDirty(training); EditorUtility.SetDirty(mode);
                PrefabUtility.SaveAsPrefabAsset(world, World);
            }
            finally { PrefabUtility.UnloadPrefabContents(world); }
            File.WriteAllText(Root + "/VoiceScripts.json", JsonUtility.ToJson(new VoiceManifest { clips = scripts.ToArray() }, true));
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            if(File.Exists(Root+"/LessonTeachingContent.json")) ProductLessonTeachingAuthoring.Apply();
        }

        private static void AuthorNavigation(Transform inputRoot, Transform seatRoot, ProductFlightModeCoordinator mode,
            ProductTrainingController training, ProductControlInput input, ProductionSeatCalibrationController seat, TextMesh template)
        {
            var navigation=inputRoot.GetComponent<ProductAppNavigation>();
            if(navigation==null) navigation=inputRoot.gameObject.AddComponent<ProductAppNavigation>();
            var root=Child(seatRoot,"AppNavigationPanel");
            root.localScale=Vector3.one*.48f;
            var title=Text(root,"Heading",template,new Vector3(-.44f,.40f,1.05f),.008f,48);
            var body=Text(root,"Detail",template,new Vector3(-.44f,.33f,1.05f),.0056f,44);
            var hint=Text(root,"Controls",template,new Vector3(-.44f,-.47f,1.05f),.0045f,40);
            Backplate(root,new Vector3(0,-.04f,1.08f),new Vector3(.98f,1.0f,.008f));
            var choices=new ProductActionButton[8];
            for(int n=0;n<choices.Length;n++)
            {
                var row=Child(root,"Action"+n); row.localPosition=new Vector3(0,.065f-n*.07f,1.05f);
                var box=row.GetComponent<BoxCollider>(); if(box==null) box=row.gameObject.AddComponent<BoxCollider>();
                box.size=new Vector3(.87f,.063f,.012f);
                var surface=row.GetComponent<ColliderSurface>(); if(surface==null) surface=row.gameObject.AddComponent<ColliderSurface>();
                surface.InjectAllColliderSurface(box);
                var ray=row.GetComponent<RayInteractable>(); if(ray==null) ray=row.gameObject.AddComponent<RayInteractable>();
                ray.InjectAllRayInteractable(surface);
                var label=Text(row,"Label",template,new Vector3(-.42f,0,-.008f),.007f,44); label.anchor=TextAnchor.MiddleLeft;
                choices[n]=row.GetComponent<ProductActionButton>(); if(choices[n]==null) choices[n]=row.gameObject.AddComponent<ProductActionButton>();
                choices[n].Configure(navigation,new MonoBehaviour[]{ray},label,n);
            }
            navigation.Configure(mode,training,input,seat,root.gameObject,root.Find("Backplate"),title,body,hint,choices);
            mode.ConfigureNavigationForAuthoring(navigation); seat.ConfigureAppNavigation(true);
            root.gameObject.SetActive(false);
        }

        private static TrainingLessonDefinition[] BuildCatalog(FlightDynamicsCoordinator flight)
        {
            TrainingStage S(string title, TrainingTask task, string cue, float alt = 7000, float hdg = 90, float speed = 90, float dwell = 6, float x = 0, float z = 0, float radius = 200) =>
                new TrainingStage { title = title, task = task, instruction = cue, altitudeFeet = alt, heading = hdg, speedKnots = speed,
                    dwellSeconds = dwell, targetEnu = new Vector2(x,z), captureMeters = radius };
            var catalog = new List<TrainingLessonDefinition>();
            TrainingLessonDefinition L(string id, string title, string brief, float x, float z, float alt, float speed, float heading, params TrainingStage[] stages)
            {
                string path = Root + "/" + id + ".asset";
                var lesson = AssetDatabase.LoadAssetAtPath<TrainingLessonDefinition>(path);
                if (lesson == null) { lesson = ScriptableObject.CreateInstance<TrainingLessonDefinition>(); AssetDatabase.CreateAsset(lesson, path); }
                lesson.lessonId = id; lesson.displayName = title; lesson.briefing = brief;
                lesson.initialThrottle = speed < 1 ? .10f : .62f; lesson.initialFlaps = 0; lesson.initialTrim = 0;
                lesson.allowDemonstration = false; lesson.airportExercise = false; lesson.goAroundStage = -1;
                if (!flight.terrainSurface.TrySample(x,z,out var terrain)) throw new InvalidOperationException("Checkpoint has no terrain: " + id);
                var geo = FlightFrameConversions.UnityToGeodetic(new Vector3(x,0,z), GeodeticReference.Kbdu);
                double terrainMsl = GeodeticReference.Kbdu.altitudeMslMeters + terrain.heightMeters;
                lesson.start = new FlightDynamicsInitialConditions { latitudeDegrees = geo.latitudeDegrees, longitudeDegrees = geo.longitudeDegrees,
                    altitudeMslMeters = speed < 1 ? terrainMsl + 1.25 : alt * .3048, terrainElevationMslMeters = terrainMsl,
                    calibratedAirspeedKnots = speed, headingDegrees = heading, pitchDegrees = speed < 1 ? 0 : 2, engineRunning = true };
                lesson.briefingNarration = Voice(id + "_briefing", brief);
                for (int i = 0; i < stages.Length; i++) stages[i].narration = Voice(id + "_" + i.ToString("00"), stages[i].instruction);
                lesson.stages = stages;
                EditorUtility.SetDirty(lesson); catalog.Add(lesson); return lesson;
            }
            var taxi = L("01_taxi", "Cockpit and taxi",
                "Start stopped on taxiway Alpha. Check the yoke, rudder and brakes, then taxi toward runway 08 and stop before entering it. The right thumbstick controls rudder; grasp the cockpit controls to operate them. A pauses, B repeats an instruction. This lesson does not include engine starting or runway entry.",
                -535,-91,0,0,270,
                S("Check roll",TrainingTask.RollControl,"At idle, grasp the yoke. Move it left and right, then center it.",dwell:1),
                S("Check pitch",TrainingTask.PitchControl,"Move the yoke gently forward and back, then center it.",dwell:1),
                S("Check rudder",TrainingTask.RudderControl,"Move the right thumbstick left and right, then center it. It controls rudder and ground steering.",dwell:1),
                S("Check brakes",TrainingTask.BrakeControl,"Keep the throttle at idle. Press both toe brakes and hold the airplane stopped.",dwell:2),
                S("Taxi west",TrainingTask.Taxi,"Release the brakes. Add a little power and follow the taxiway west at walking speed, about 3 to 6 knots. Slow near the runway-end connector.",hdg:270,dwell:1,x:-600,z:-91,radius:20),
                S("Turn toward hold short",TrainingTask.Taxi,"At the connector, turn right toward the runway. Keep the taxi speed low and be ready to stop before the hold-short line.",hdg:0,dwell:1,x:-620,z:-80,radius:14),
                S("Stop before runway",TrainingTask.Stop,"Close the throttle and apply both brakes. Stop before the hold-short line; do not enter the runway.",hdg:0,dwell:3,x:-620,z:-72,radius:15));
            taxi.airportExercise = true;
            var first = L("02_first_flight", "Level flight and gentle turns",
                "Begin airborne east of the airport at 7000 feet and 90 knots. Look outside at the horizon, then cross-check the attitude indicator, airspeed and altimeter. Use small yoke movements. You will hold level flight, turn left and roll out, then return to the original heading. X requests assistance; assistance is recorded separately.",
                1300,1700,7000,90,90,
                S("Level flight",TrainingTask.Level,"You have control. Hold 7000 feet, 90 knots and heading 090 true. Level the wings against the outside horizon; make small corrections.",dwell:15),
                S("Gentle left turn",TrainingTask.Turn,"Turn left toward heading 060 true. Use about 15 degrees of bank and enough rudder to keep the slip ball centered. Maintain 7000 feet.",hdg:60,dwell:2),
                S("Roll out",TrainingTask.Level,"Level the wings on heading 060 true. Hold 7000 feet and about 90 knots.",hdg:60,dwell:10),
                S("Gentle right turn",TrainingTask.Turn,"Turn gently right to heading 090 true, maintaining altitude and coordinated flight.",hdg:90,dwell:2),
                S("Settle level",TrainingTask.Level,"Roll out on heading 090 true. Settle back into level flight at 7000 feet and 90 knots.",dwell:10));
            first.allowDemonstration = true;
            L("03_climb_descent", "Climbs, descents and trim",
                "Start in level flight at 7000 feet. Add power and climb to 7300 feet, level off, then descend to 7000 feet. Finally use the trim wheel to relieve sustained yoke pressure. Keep the heading at 090 true. The same real wind field remains active.",
                1000,1800,7000,90,90,
                S("Climb",TrainingTask.Climb,"Add power and raise the nose gently. Climb toward 7300 feet at 70 to 80 knots, heading 090 true.",alt:7300,speed:75,dwell:1),
                S("Level at 7300",TrainingTask.Level,"Lower the nose before passing the target, then adjust power for level flight at 7300 feet and about 90 knots.",alt:7300,dwell:10),
                S("Descend",TrainingTask.Descend,"Reduce power and lower the nose slightly. Descend toward 7000 feet at a comfortable 300 to 500 feet per minute.",dwell:1),
                S("Level at 7000",TrainingTask.Level,"Begin leveling before 7000 feet. Restore cruise power and settle at about 90 knots.",dwell:8),
                S("Trim off pressure",TrainingTask.Trim,"Use small movements of the trim wheel to relieve yoke pressure. Keep 7000 feet, heading 090 true, and the wings level.",dwell:8));
            var takeoff = L("04_takeoff", "Takeoff and departure",
                "Start lined up and stopped on runway 08. Check the available controls, apply power, rotate near 55 knots and climb at 70 to 80 knots. Maintain the runway direction. This exercise uses the existing traffic protection; it does not simulate radio clearance or a complete preflight checklist.",
                -555,-30,0,0,89.972f,
                S("Takeoff configuration",TrainingTask.TakeoffConfiguration,"Check alignment with runway 08, flaps zero to ten degrees, carb heat cold, and mixture set for this exercise. Hold stopped while checking.",dwell:2),
                S("Takeoff roll",TrainingTask.TakeoffRoll,"Release brakes, apply full power smoothly, and keep the runway centerline with rudder. At about 55 knots, prepare to raise the nose.",dwell:.15f),
                S("Rotate",TrainingTask.Rotation,"Gently raise the nose near 55 knots. Let the airplane lift off, then adjust pitch toward a 70 to 80 knot climb.",dwell:1),
                S("Departure climb",TrainingTask.Departure,"Continue straight ahead at 70 to 80 knots. Climb through 6000 feet; do not turn early over the airport.",alt:6000,speed:75,dwell:3));
            takeoff.airportExercise = true;
            var pattern = L("05_pattern", "Pattern and approach decisions",
                "Begin on runway 08 left downwind at 6300 feet MSL, south of Jay Road. Fly the pattern, configure for approach and judge stability. If the approach is unstable, apply power and go around. This is a reset into a practice segment, not a real-world arrival clearance.",
                400,850,6300,85,270,
                S("Downwind",TrainingTask.Downwind,"Hold the north-side downwind at 6300 feet and about 85 knots, heading 270 true. Keep the runway on your left and remain south of Jay Road.",6300,270,85,3,-500,850,200),
                S("Abeam landing end",TrainingTask.Abeam,"Abeam the landing end, reduce power, slow below flap-extension speed and select ten degrees of flaps. Begin a gentle descent; continue downwind before turning base.",6100,270,75,2,-1200,850,230),
                S("Left base",TrainingTask.Base,"Turn left onto base, heading 180 true. Descend at about 70 to 75 knots; use a gentle bank. If you overshoot final, go around instead of tightening the turn.",5800,180,70,1,-1750,350,260),
                S("Stable final",TrainingTask.Final,"Turn left onto final for runway 08. Align with the extended centerline, select landing flaps as needed and hold 60 to 70 knots. If unstable, apply full power and go around.",5500,90,65,6),
                S("Go-around power",TrainingTask.GoAroundPower,"Go around. Apply full power and carb heat cold. Raise the nose for climb and reduce full flaps to twenty degrees.",speed:65,dwell:1),
                S("Positive climb",TrainingTask.GoAroundClimb,"Confirm a positive climb. With safe airspeed, reduce flaps to ten degrees. Hold the runway direction.",speed:65,dwell:3),
                S("Clean climb",TrainingTask.GoAroundClean,"Clear of obstacles, retract the remaining flaps and continue straight ahead at 70 to 80 knots.",speed:75,dwell:5));
            pattern.airportExercise = true; pattern.goAroundStage = 4;
            var landing = L("06_landing", "Landing and go-around",
                "Start on final for runway 08 at about 65 knots with twenty degrees of flaps. Stabilize the approach, flare, touch down and slow to taxi speed. You can instead choose a go-around: power, climb, and retract flaps in stages. A sensible go-around is a successful outcome. Do not force a poor approach onto the runway.",
                -2400,-30,5680,65,89.972f,
                S("Stabilize final",TrainingTask.Final,"You have control. Keep the runway centered and hold 60 to 70 knots with a steady descent. Check alignment and configuration. If unstable, go around.",5680,90,65,4),
                S("Flare and touchdown",TrainingTask.Landing,"As the runway rises in your view, look farther down it. Reduce power and ease the nose up to arrest the descent. Keep the wings level and aligned. Go around if the landing will not be safe.",5300,90,65,.3f),
                S("Rollout",TrainingTask.Rollout,"Maintain the centerline, lower the nose gently and brake smoothly. Slow below eight knots while remaining on the paved runway.",5300,90,30,3),
                S("Go-around power",TrainingTask.GoAroundPower,"Go around. Full power, carb heat cold, and pitch for climb. Reduce full flaps to twenty degrees if necessary.",speed:65,dwell:1),
                S("Positive climb",TrainingTask.GoAroundClimb,"Confirm positive climb, maintain safe airspeed and retract flaps to ten degrees. Keep the runway direction.",speed:65,dwell:3),
                S("Clean climb",TrainingTask.GoAroundClean,"Once clear of obstacles, retract the remaining flaps and continue a 70 to 80 knot climb straight ahead.",speed:75,dwell:5));
            landing.airportExercise = true; landing.goAroundStage = 3; landing.initialFlaps = 2f/3f; landing.initialThrottle = .32f;
            landing.start.pitchDegrees = -1; landing.start.flightPathAngleDegrees = -3;
            foreach (var lesson in catalog)
            {
                ApplyClarityContent(lesson);
                if (lesson.start.calibratedAirspeedKnots > 0) AuthorAirborneTrim(lesson, flight);
                EditorUtility.SetDirty(lesson);
            }
            return catalog.ToArray();
        }

        private static void ApplyClarityContent(TrainingLessonDefinition lesson)
        {
            if(lesson.lessonId=="02_first_flight")
            {
                TrainingStage Level(string title,string cue,TrainingReference reference,bool attitude=false) => new TrainingStage
                {title=title,instruction=cue,reference=reference,task=TrainingTask.Level,altitudeFeet=7000,heading=90,speedKnots=90,dwellSeconds=12,attitudeOnly=attitude};
                var left=lesson.stages[1]; var rollLeft=lesson.stages[2]; var right=lesson.stages[3]; var rollRight=lesson.stages[4];
                lesson.stages=new[]{
                    Level("Wings level","You have control. Keep the wings level against the outside horizon.",TrainingReference.Outside,true),
                    Level("Altitude cross-check","Check the altimeter. Hold about seven thousand feet, then look outside again.",TrainingReference.Altimeter),
                    Level("Airspeed cross-check","Check the airspeed needle. Aim for ninety knots with small power adjustments.",TrainingReference.Airspeed),
                    Level("Heading cross-check","Read the heading at the top index. Hold zero nine zero, then look outside.",TrainingReference.Heading),
                    left,rollLeft,right,rollRight};
                left.instruction="Turn gently left toward zero six zero. Use a shallow, coordinated bank.";
                rollLeft.instruction="Level the wings on zero six zero. Settle at seven thousand feet and ninety knots.";
                right.instruction="Turn gently right toward zero nine zero. Keep the turn shallow and coordinated.";
                rollRight.instruction="Level the wings on zero nine zero. Hold altitude and speed with small corrections.";
                lesson.briefing="Learn the cockpit references, then practice level flight and gentle turns. The instructor coaches you as you fly. Menu pauses; X repeats the current cue.";
            }
            else
            {
                string[] cues=lesson.lessonId switch
                {
                    "01_taxi"=>new[]{"At idle, grip the yoke. Move it left and right, then center it.","Move the yoke gently forward and back, then center it.","Move the right thumbstick left and right, then center it. This controls rudder and ground steering.","With throttle at idle, squeeze both index triggers to apply both brakes.","Release the brakes. Add a little power; follow the taxiway west at walking speed.","Turn right at the connector toward the runway. Keep the taxi speed low.","Close the throttle and brake before the hold-short line. Do not enter the runway."},
                    "03_climb_descent"=>new[]{"Add climb power, then set a gentle climbing attitude. Aim for seventy to eighty knots.","Approaching seventy-three hundred feet, lower the nose to level flight and adjust power.","Reduce power and establish a gentle descent toward seven thousand feet.","Begin leveling before seven thousand feet. Restore cruise power and settle near ninety knots.","Move the left thumbstick up or down a little to trim. Reduce the yoke input needed to stay level."},
                    "04_takeoff"=>new[]{"While stopped, check runway alignment and the flap indication. This checkpoint has mixture and carb heat preset.","Release brakes and smoothly apply full power. Keep the centerline with rudder.","At about fifty-five knots, gently raise the nose. Establish the climb as the aircraft lifts off.","Climb straight ahead through six thousand feet. Hold seventy to eighty knots."},
                    "05_pattern"=>new[]{"Keep the runway on your left. Hold sixty-three hundred feet on the north-side downwind.","Abeam the landing end, reduce power. Below flap speed, select ten degrees and begin descending.","Turn gently left onto base. Keep a shallow bank and a comfortable descent.","Align with the runway. Hold sixty to seventy knots on a steady final approach.","Go around. Full power, pitch for climb. Reduce full flaps to twenty degrees.","With positive climb and safe speed, reduce flaps to ten degrees.","Clear of obstacles, retract remaining flaps and climb straight ahead."},
                    "06_landing"=>new[]{"Keep the runway centered. Hold sixty to seventy knots with a steady descent.","Look farther along the runway. Reduce power and gently arrest the descent for touchdown.","Keep directional control and brake smoothly to taxi speed.","Go around. Full power, pitch for climb. Reduce full flaps to twenty degrees.","With positive climb and safe speed, reduce flaps to ten degrees.","Clear of obstacles, retract remaining flaps and climb straight ahead."},
                    _=>Array.Empty<string>()
                };
                for(int n=0;n<Math.Min(cues.Length,lesson.stages.Length);n++) lesson.stages[n].instruction=cues[n];
                lesson.briefing=lesson.lessonId switch
                {
                    "01_taxi"=>"Start stopped. Learn the controls, then taxi slowly and stop before the runway. Both index triggers apply brakes; the right stick steers. Menu pauses.",
                    "03_climb_descent"=>"Practice a climb, descent and level-off. Learn the instruments first. The left stick adjusts trim; the right stick controls rudder. Menu pauses.",
                    "04_takeoff"=>"Start stopped on runway zero eight. Check the aircraft, take off and climb straight ahead. Review the references before starting; instructions become brief during the roll.",
                    "05_pattern"=>"Practice the north-side pattern for runway zero eight. Learn the runway references before starting. A safe go-around is a successful outcome.",
                    _=>"Start at a final-approach checkpoint. Practice a stable approach and landing, or choose a go-around. Review the controls first; do not force an unstable landing."
                };
            }
            for(int n=0;n<lesson.stages.Length;n++)
            {
                var stage=lesson.stages[n];
                if(lesson.lessonId!="02_first_flight"||n>=4) stage.reference=ReferenceFor(stage.task);
                stage.narration=Voice(lesson.lessonId+"_"+n.ToString("00"),stage.instruction);
            }
            lesson.orientation=lesson.lessonId=="01_taxi" ? new[]{TrainingReference.Yoke,TrainingReference.Throttle,TrainingReference.Runway} :
                new[]{TrainingReference.Outside,TrainingReference.Attitude,TrainingReference.Heading,TrainingReference.Altimeter,TrainingReference.Airspeed,TrainingReference.Throttle,TrainingReference.Slip,TrainingReference.Trim,TrainingReference.Flaps};
            lesson.briefingNarration=Voice(lesson.lessonId+"_briefing",lesson.briefing);
        }

        private static TrainingReference ReferenceFor(TrainingTask task) => task switch
        {
            TrainingTask.RollControl or TrainingTask.PitchControl => TrainingReference.Yoke,
            TrainingTask.RudderControl=>TrainingReference.Slip,
            TrainingTask.BrakeControl or TrainingTask.Taxi or TrainingTask.Stop or TrainingTask.Final or TrainingTask.Landing or TrainingTask.Rollout or TrainingTask.Downwind or TrainingTask.Abeam or TrainingTask.Base=>TrainingReference.Runway,
            TrainingTask.Turn=>TrainingReference.Heading,
            TrainingTask.Climb or TrainingTask.Departure or TrainingTask.Rotation=>TrainingReference.Airspeed,
            TrainingTask.Descend=>TrainingReference.VerticalSpeed,
            TrainingTask.Trim=>TrainingReference.Trim,
            TrainingTask.TakeoffRoll or TrainingTask.GoAroundPower=>TrainingReference.Throttle,
            TrainingTask.TakeoffConfiguration or TrainingTask.GoAroundClimb or TrainingTask.GoAroundClean=>TrainingReference.Flaps,
            _=>TrainingReference.Outside
        };

        private static void AuthorCoach(GameObject world,Transform ui,Transform seatRoot,ProductTrainingController training,FlightDynamicsCoordinator flight,TextMesh template)
        {
            var coach=ui.GetComponent<TrainingCoach>(); if(coach==null) coach=ui.gameObject.AddComponent<TrainingCoach>();
            var batch=world.GetComponentInChildren<CockpitInstrumentV2BatchController>(true);
            if(batch==null) throw new InvalidOperationException("The authored instrument layout is required for teaching anchors.");
            var markers=new List<TrainingReferenceMarker>();
            // Instrument references belong to the airframe, not the calibrated seat.
            // Re-author the earlier seat children so a saved seat offset cannot move a pointer off its gauge.
            foreach(var child in seatRoot.Cast<Transform>().Where(t=>t.name.StartsWith("TrainingReference_",StringComparison.Ordinal)).ToArray())
                Object.DestroyImmediate(child.gameObject);
            var markerParent=AnchorCockpitReferences(flight);
            void Reference(TrainingReference id,string label,string text,CockpitInstrumentV2Function function=CockpitInstrumentV2Function.None)
            {
                GameObject marker=null;
                Vector3 position=Vector3.zero; bool positioned=false;
                if(function!=CockpitInstrumentV2Function.None)
                {
                    var binding=batch.Layout.Bindings.FirstOrDefault(b=>b.rotationFunction==function||b.translationFunction==function);
                    if(binding.vertexCount<=0) throw new InvalidOperationException("Missing teaching binding: "+id);
                    position=batch.NeutralizedSourceRenderer.transform.TransformPoint(binding.pivotLocal); positioned=true;
                }
                else if(id==TrainingReference.Attitude)
                {
                    // The attitude pivot is at the back of its ball; project it onto the gauge-face plane.
                    var face=batch.Layout.Bindings.First(b=>b.rotationFunction==CockpitInstrumentV2Function.IndicatedAirspeedNeedleDegrees);
                    var normal=face.rotationAxisLocal.normalized;
                    var center=batch.Layout.AttitudeContract.pivotLocal;
                    center-=normal*Vector3.Dot(center-face.pivotLocal,normal);
                    position=batch.NeutralizedSourceRenderer.transform.TransformPoint(center); positioned=true;
                }
                else if(id==TrainingReference.Throttle||id==TrainingReference.Yoke||id==TrainingReference.Flaps)
                {
                    var target=id==TrainingReference.Throttle?CockpitTouchInteractionV2Target.Throttle:id==TrainingReference.Yoke?CockpitTouchInteractionV2Target.PilotYoke:CockpitTouchInteractionV2Target.FlapSelector;
                    var touch=world.GetComponentInChildren<CockpitTouchInteractionV2Controller>(true);
                    var bindings=new SerializedObject(touch).FindProperty("bindings");
                    for(int n=0;n<bindings.arraySize;n++)
                    {
                        var b=bindings.GetArrayElementAtIndex(n);
                        if(b.FindPropertyRelative("target").intValue!=(int)target) continue;
                        var collider=b.FindPropertyRelative("targetCollider").objectReferenceValue as Collider;
                        if(collider!=null) { position=collider is BoxCollider box?box.transform.TransformPoint(box.center):collider.bounds.center; positioned=true; break; }
                    }
                }
                if(positioned)
                {
                    var root=Child(markerParent,"TrainingReference_"+id);
                    root.position=position+(seatRoot.position-position).normalized*.014f; root.rotation=seatRoot.rotation;
                    var ring=root.GetComponent<LineRenderer>(); if(ring==null) ring=root.gameObject.AddComponent<LineRenderer>();
                    ring.useWorldSpace=false; ring.loop=true; ring.positionCount=33; ring.widthMultiplier=.002f;
                    ring.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(ProductStartupShowroomAuthoring.AccentMaterialPath);
                    ring.shadowCastingMode=ShadowCastingMode.Off; ring.receiveShadows=false;
                    for(int n=0;n<33;n++) { float a=2*Mathf.PI*n/33; ring.SetPosition(n,new Vector3(Mathf.Sin(a)*.047f,Mathf.Cos(a)*.047f,0)); }
                    var name=Text(root,"Name",template,new Vector3(.05f,.048f,-.006f),.0026f,44); name.text=label;
                    // Fixed index leader; all geometry stays cockpit-relative in both eyes.
                    var pointer=Child(root,"ReadingIndex"); var line=pointer.GetComponent<LineRenderer>(); if(line==null) line=pointer.gameObject.AddComponent<LineRenderer>();
                    line.useWorldSpace=false; line.positionCount=2; line.widthMultiplier=.002f; line.sharedMaterial=ring.sharedMaterial;
                    line.SetPosition(0,new Vector3(.047f,.047f,0)); line.SetPosition(1,new Vector3(0,.033f,0));
                    line.shadowCastingMode=ShadowCastingMode.Off; line.receiveShadows=false;
                    marker=root.gameObject; marker.SetActive(false);
                }
                markers.Add(new TrainingReferenceMarker{reference=id,marker=marker,explanation=text,voice=Voice("reference_"+id.ToString().ToLowerInvariant(),text)});
            }
            Reference(TrainingReference.Outside,"Outside horizon","Look through the windshield. Use the distant horizon to judge aircraft attitude, and keep scanning for traffic. Cross-check instruments briefly, then look outside again.");
            Reference(TrainingReference.Attitude,"Attitude indicator","The attitude indicator shows the airplane against a small artificial horizon. Compare it with the real horizon; avoid staring at the instrument.");
            Reference(TrainingReference.Airspeed,"Airspeed - knots","This is the airspeed indicator. Read the needle against the knots scale. Ninety knots is the level-flight target for this exercise.",CockpitInstrumentV2Function.IndicatedAirspeedNeedleDegrees);
            Reference(TrainingReference.Altimeter,"Altimeter - feet","This is the altimeter. Its numeric reading and hands show indicated altitude in feet. The level-flight target is seven thousand feet.",CockpitInstrumentV2Function.AltimeterHundredsNeedleDegrees);
            Reference(TrainingReference.Heading,"Heading - top index","Read heading at the top index. Nine means zero nine zero degrees. This simulator's heading indicator uses true heading; use this dial for lesson targets, not the magnetic compass.",CockpitInstrumentV2Function.DirectionalGyroHeadingDegrees);
            Reference(TrainingReference.VerticalSpeed,"Vertical speed","This needle shows climbing or descending in feet per minute. Use it to confirm a trend; make small attitude corrections and allow the indication to settle.",CockpitInstrumentV2Function.IndicatedVerticalSpeedNeedleDegrees);
            Reference(TrainingReference.Slip,"Slip indicator","The ball shows coordination. Use gentle rudder toward the displaced ball, then ease the input as it centers. The right thumbstick controls rudder.",CockpitInstrumentV2Function.SlipSkidNormalized);
            Reference(TrainingReference.Throttle,"Throttle","Grip the throttle knob to adjust power. Add or reduce power smoothly, then check how airspeed and climb change.");
            Reference(TrainingReference.Yoke,"Yoke","Grip the yoke. Left and right inputs roll the airplane; forward and back inputs change pitch. Release the correcting input as the desired attitude is reached.");
            Reference(TrainingReference.Flaps,"Flap selector","Use the flap selector to choose a position. Confirm the flap indication and keep within the taught airspeed range before extending flaps.");
            Reference(TrainingReference.Trim,"Trim control","The left thumbstick adjusts trim during manual flight. Use small up or down inputs to reduce the yoke input needed to hold attitude. There is no force feedback.");
            Reference(TrainingReference.Runway,"Runway reference","Identify runway zero eight and its paved edges outside. The north-side pattern keeps the runway to your left. For taxi, stay on the taxiway and stop before entering the runway.");
            var lines=new List<TrainingCoachLine>();
            void Cue(TrainingCue id,TrainingReference reference,string text) => lines.Add(new TrainingCoachLine{cue=id,reference=reference,text=text,voice=Voice("coach_"+id.ToString().ToLowerInvariant(),text)});
            Cue(TrainingCue.RollLeft,TrainingReference.Attitude,"The right bank is too steep. Ease the yoke left to reduce it.");
            Cue(TrainingCue.RollRight,TrainingReference.Attitude,"The left bank is too steep. Ease the yoke right to reduce it.");
            Cue(TrainingCue.RolloutLeft,TrainingReference.Outside,"Approaching the target heading. Begin rolling left to wings level.");
            Cue(TrainingCue.RolloutRight,TrainingReference.Outside,"Approaching the target heading. Begin rolling right to wings level.");
            Cue(TrainingCue.TurnLeft,TrainingReference.Heading,"The target is to your left. Begin a gentle left turn.");
            Cue(TrainingCue.TurnRight,TrainingReference.Heading,"The target is to your right. Begin a gentle right turn.");
            Cue(TrainingCue.AltitudeLow,TrainingReference.Altimeter,"You are below the target altitude. Check attitude and power; establish a gentle climb if airspeed permits.");
            Cue(TrainingCue.AltitudeHigh,TrainingReference.Altimeter,"You are above the target altitude. Ease toward a gentle descent and monitor airspeed.");
            Cue(TrainingCue.SpeedLow,TrainingReference.Airspeed,"Airspeed is low. Check nose attitude and power before continuing the maneuver.");
            Cue(TrainingCue.SpeedHigh,TrainingReference.Airspeed,"Airspeed is high. Check power and your descent attitude; make a small correction.");
            Cue(TrainingCue.RudderLeft,TrainingReference.Slip,"The ball is left of center. Add a little left rudder, then ease it as the ball centers.");
            Cue(TrainingCue.RudderRight,TrainingReference.Slip,"The ball is right of center. Add a little right rudder, then ease it as the ball centers.");
            Cue(TrainingCue.TaxiFast,TrainingReference.Throttle,"Taxi speed is too high. Reduce power and brake gently back to walking speed.");
            Cue(TrainingCue.Stop,TrainingReference.Runway,"Throttle idle. Apply both brakes and stop before the runway.");
            Cue(TrainingCue.Power,TrainingReference.Throttle,"Increase power smoothly for this phase. Keep monitoring airspeed and direction.");
            Cue(TrainingCue.Brakes,TrainingReference.Outside,"Squeeze both index triggers to apply both brakes. Keep throttle at idle.");
            Cue(TrainingCue.Yoke,TrainingReference.Yoke,"Grip the yoke and move it through the requested directions, then center it.");
            Cue(TrainingCue.Rudder,TrainingReference.Slip,"Use the right thumbstick for rudder. Move it both ways, then center it.");
            Cue(TrainingCue.Trim,TrainingReference.Trim,"Use small left-thumbstick inputs to trim until less yoke input is needed.");
            Cue(TrainingCue.Flaps,TrainingReference.Flaps,"Positive climb established. Reduce flaps to the next instructed setting.");
            Cue(TrainingCue.AlignRunway,TrainingReference.Runway,"Check runway alignment. Use a gentle correction; go around if a safe alignment is not possible.");
            Cue(TrainingCue.GoAround,TrainingReference.Throttle,"Unstable approach. Go around: full power, establish the climb, then retract flaps in stages.");
            Cue(TrainingCue.Recovered,TrainingReference.Outside,"That correction is working. Let the aircraft settle and look outside.");
            coach.Configure(training,flight,batch.AuthorityProvider,lines.ToArray(),markers.ToArray()); training.ConfigureCoachForAuthoring(coach);
            AuthorWorldObjective(world,coach);
        }

        // Narrow re-authoring entry point: preserve lessons, voices, cockpit markers and controls.
        private static Transform AnchorCockpitReferences(FlightDynamicsCoordinator flight)
        {
            var rig=flight.simulationRoot.GetComponent<QuestFlightLab.Runtime.AircraftReferenceFrameRig>();
            if(rig==null || rig.AircraftVisualRoot==null)
                throw new InvalidOperationException("Cockpit references require the authored aircraft visual root.");
            var references=flight.simulationRoot.Find("TrainingCockpitReferences")
                ?? rig.AircraftVisualRoot.Find("TrainingCockpitReferences")
                ?? Child(rig.AircraftVisualRoot,"TrainingCockpitReferences");
            // Match cockpit/seat presentation interpolation, never the raw physics pose.
            references.SetParent(rig.AircraftVisualRoot,true);
            return references;
        }

        public static void RunCockpitReferenceAnchorBatch()
        {
            try
            {
                var world=PrefabUtility.LoadPrefabContents(World);
                try
                {
                    AnchorCockpitReferences(world.GetComponentInChildren<FlightDynamicsCoordinator>(true));
                    PrefabUtility.SaveAsPrefabAsset(world,World);
                }
                finally { PrefabUtility.UnloadPrefabContents(world); }
                AssetDatabase.SaveAssets();
                Debug.Log("[TrainingReference] Cockpit labels now share the cockpit presentation root.");
                EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void RunWorldObjectiveBatch()
        {
            try
            {
                var world=PrefabUtility.LoadPrefabContents(World);
                try
                {
                    AuthorWorldObjective(world,world.GetComponentInChildren<TrainingCoach>(true));
                    PrefabUtility.SaveAsPrefabAsset(world,World);
                }
                finally { PrefabUtility.UnloadPrefabContents(world); }
                AssetDatabase.SaveAssets();
                Debug.Log("[TrainingObjective] Authored elevated outdoor marker.");
                EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        private static void AuthorWorldObjective(GameObject world,TrainingCoach coach)
        {
            if(coach==null) throw new InvalidOperationException("Authored training coach is missing.");
            string materialPath=Root+"/ObjectiveRibbon.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var shader=Shader.Find("QuestFlight/Training/ObjectiveRibbon");
            if(shader==null) throw new InvalidOperationException("Outdoor objective ribbon shader is missing.");
            if(material==null) { material=new Material(shader); AssetDatabase.CreateAsset(material,materialPath); }
            material.shader=shader;
            material.SetColor("_Color",new Color(.08f,.72f,.95f,1));
            material.SetColor("_CoreColor",new Color(.72f,.96f,1,1));
            material.SetFloat("_Surface",0);
            EditorUtility.SetDirty(material);
            var landmark=Child(world.transform,"TrainingAirportReference");
            // Fixed geometry avoids camera-facing ribbon joins flipping across eye height.
            var outline=landmark.GetComponent<LineRenderer>();
            if(outline!=null) UnityEngine.Object.DestroyImmediate(outline);
            var accent=AssetDatabase.LoadAssetAtPath<Material>(ProductStartupShowroomAuthoring.AccentMaterialPath);
            var corners=new[]{new Vector3(-4,0,-4),new Vector3(-4,0,4),new Vector3(4,0,4),new Vector3(4,0,-4)};
            var pulse=Child(landmark,"ObjectivePulse");
            var ring=pulse.GetComponent<LineRenderer>();
            if(ring!=null) UnityEngine.Object.DestroyImmediate(ring);
            var rounded=new List<Vector3>();
            for(int n=0;n<4;n++)
            {
                var p=corners[n];
                rounded.Add(p+(corners[(n+3)%4]-p).normalized*.45f);
                rounded.Add(p+(corners[(n+1)%4]-p).normalized*.45f);
            }
            AuthorObjectiveOutline(landmark,rounded,.10f,"ObjectiveBoundary",accent);
            AuthorObjectiveOutline(pulse,rounded,.07f,"ObjectiveRing",accent);

            // A single low-opacity, open-topped shell adds depth without particles or lights.
            string volumeMaterialPath=Root+"/ObjectiveVolume.mat";
            var volumeMaterial=AssetDatabase.LoadAssetAtPath<Material>(volumeMaterialPath);
            if(volumeMaterial==null) { volumeMaterial=new Material(shader); AssetDatabase.CreateAsset(volumeMaterial,volumeMaterialPath); }
            volumeMaterial.shader=shader; volumeMaterial.SetFloat("_Surface",1);
            volumeMaterial.SetColor("_Color",new Color(.06f,.70f,.88f,1));
            volumeMaterial.renderQueue=2999; EditorUtility.SetDirty(volumeMaterial);
            string meshPath=Root+"/ObjectiveVolume.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(mesh==null) { mesh=new Mesh { name="ObjectiveVolume" }; AssetDatabase.CreateAsset(mesh,meshPath); }
            var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var indices=new List<int>();
            for(int n=0;n<rounded.Count;n++)
            {
                int v=vertices.Count; var a=rounded[n]; var b=rounded[(n+1)%rounded.Count];
                vertices.Add(a); vertices.Add(b); vertices.Add(b+Vector3.up*3.8f); vertices.Add(a+Vector3.up*3.8f);
                uv.Add(new Vector2(0,0)); uv.Add(new Vector2(1,0)); uv.Add(new Vector2(1,1)); uv.Add(new Vector2(0,1));
                indices.AddRange(new[]{v,v+1,v+2,v,v+2,v+3});
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(indices,0); mesh.RecalculateBounds();
            // White vertex color also keeps the ribbon shader usable for authored solid meshes.
            mesh.colors=Enumerable.Repeat(Color.white,vertices.Count).ToArray(); EditorUtility.SetDirty(mesh);
            var volume=Child(landmark,"ObjectiveVolume");
            var filter=volume.GetComponent<MeshFilter>(); if(filter==null) filter=volume.gameObject.AddComponent<MeshFilter>();
            var renderer=volume.GetComponent<MeshRenderer>(); if(renderer==null) renderer=volume.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh=mesh; renderer.sharedMaterial=volumeMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            landmark.gameObject.SetActive(false); coach.ConfigureWorldMarker(landmark,pulse);
        }

        private static void AuthorObjectiveOutline(Transform target,List<Vector3> path,float width,string name,Material material)
        {
            string assetPath=Root+"/"+name+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if(mesh==null) { mesh=new Mesh { name=name }; AssetDatabase.CreateAsset(mesh,assetPath); }
            var vertices=new List<Vector3>(); var indices=new List<int>();
            // Closed four-sided tube: stable silhouette from above, below and eye level.
            for(int n=0;n<path.Count;n++)
            {
                var incoming=(path[n]-path[(n+path.Count-1)%path.Count]).normalized;
                var outgoing=(path[(n+1)%path.Count]-path[n]).normalized;
                var outward=Vector3.Cross((incoming+outgoing).normalized,Vector3.up);
                for(int k=0;k<4;k++)
                {
                    float angle=k*Mathf.PI*.5f;
                    vertices.Add(path[n]+width*.5f*(outward*Mathf.Cos(angle)+Vector3.up*Mathf.Sin(angle)));
                    int a=n*4+k,b=((n+1)%path.Count)*4+k;
                    int c=((n+1)%path.Count)*4+(k+1)%4,d=n*4+(k+1)%4;
                    indices.AddRange(new[]{a,b,c,a,c,d});
                }
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(indices,0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh.colors=Enumerable.Repeat(Color.white,vertices.Count).ToArray();
            EditorUtility.SetDirty(mesh);
            var filter=target.GetComponent<MeshFilter>(); if(filter==null)filter=target.gameObject.AddComponent<MeshFilter>();
            var renderer=target.GetComponent<MeshRenderer>(); if(renderer==null)renderer=target.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh=mesh; renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
        }

        private static void AuthorAirborneTrim(TrainingLessonDefinition lesson, FlightDynamicsCoordinator flight)
        {
            // Solve the actual product reset/control/wind path offline, not an idealized
            // trim state that the reset API cannot restore. No runtime trim/autopilot is hidden.
            var ic=lesson.start;
            var point=FlightFrameConversions.GeodeticToUnity(ic.latitudeDegrees,ic.longitudeDegrees,ic.altitudeMslMeters,GeodeticReference.Kbdu);
            var wind=flight.windField.Sample(point,0);
            float targetSpeed=(float)ic.calibratedAirspeedKnots;
            double heading=ic.headingDegrees*Math.PI/180;
            float headwind=-(wind.x*(float)Math.Sin(heading)+wind.z*(float)Math.Cos(heading))*1.78f;
            lesson.start.calibratedAirspeedKnots=Math.Max(40,targetSpeed-headwind);
            lesson.start.pitchDegrees=lesson.initialFlaps>0 ? -3 : 0;
            float targetVs=lesson.initialFlaps>0 ? -350 : 0;
            lesson.initialThrottle=lesson.initialFlaps>0 ? .30f : .65f;
            {
                FlightDynamicsState Probe(float trim)
                {
                    using var backend=new JSBSimNativeDevelopmentBackend(JSBSimRuntimeData.EditorPackageDataRoot);
                    if(!backend.Initialize(new FlightDynamicsBackendContext{simulationRoot=flight.simulationRoot,aircraftState=flight.aircraftState,terrainSurface=flight.terrainSurface}))
                        throw new InvalidOperationException(backend.LastError);
                    backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(wind));
                    if(!backend.Reset(lesson.start))throw new InvalidOperationException(backend.LastError);
                    var control=QuestFlightLab.Runtime.AircraftControlState.Neutral(lesson.initialThrottle);
                    control.mixture=1;control.trim=trim;control.flaps=lesson.initialFlaps;
                    for(int step=0;step<720;step++)
                    {
                        backend.SetControls(control);
                        var s=backend.CurrentState;
                        backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(flight.windField.Sample(s.positionUnityMeters,s.simulationTimeSeconds)));
                        if(!backend.Advance(1.0/120))throw new InvalidOperationException(backend.LastError);
                    }
                    return backend.CurrentState;
                }
                for(int pass=0;pass<4;pass++)
                {
                    float low=-.65f,high=.25f;
                    for(int iteration=0;iteration<10;iteration++)
                    { float middle=(low+high)*.5f;var s=Probe(middle);if(s.verticalSpeedFeetPerMinute>targetVs)high=middle;else low=middle; }
                    lesson.initialTrim=(low+high)*.5f;
                    var result=Probe(lesson.initialTrim);
                    if(pass<3)lesson.initialThrottle=Mathf.Clamp01(lesson.initialThrottle+(targetSpeed-(float)result.calibratedAirspeedKnots)*.035f);
                    else Debug.Log($"[TrainingTrim] {lesson.lessonId} throttle={lesson.initialThrottle:F4} trim={lesson.initialTrim:F4} CAS={result.calibratedAirspeedKnots:F1} pitch={result.pitchDegrees:F2} VS={result.verticalSpeedFeetPerMinute:F0}");
                }
            }
        }

        private static AudioClip Voice(string id, string text)
        {
            scripts.RemoveAll(s=>s.id==id);
            scripts.Add(new VoiceScript { id = id, text = text });
            string path = Root + "/Voice/" + id + ".wav";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer != null)
            {
                importer.forceToMono = true;
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = .55f; settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate; settings.sampleRateOverride = 22050;
                importer.defaultSampleSettings = settings; importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        private static Transform Child(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child == null) { child = new GameObject(name).transform; child.SetParent(parent,false); }
            return child;
        }
        private static TextMesh Text(Transform parent, string name, TextMesh template, Vector3 position, float size, int fontSize)
        {
            var t = Child(parent,name); t.localPosition = position;
            var text = t.GetComponent<TextMesh>(); if (text == null) text = t.gameObject.AddComponent<TextMesh>();
            text.font = template.font; text.fontSize = fontSize; text.characterSize = size;
            text.anchor = TextAnchor.UpperLeft; text.alignment = TextAlignment.Left; text.lineSpacing = 1.1f; text.richText = false;
            text.color = new Color(.87f,.94f,1);
            var r = text.GetComponent<MeshRenderer>(); r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ProductStartupShowroomAuthoring.TextMaterialPath);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return text;
        }
        private static void Backplate(Transform parent, Vector3 position, Vector3 scale)
        {
            var t = parent.Find("Backplate");
            if (t == null) { t = GameObject.CreatePrimitive(PrimitiveType.Cube).transform; t.name = "Backplate"; t.SetParent(parent,false); Object.DestroyImmediate(t.GetComponent<Collider>()); }
            t.localPosition = position; t.localScale = scale;
            var r = t.GetComponent<MeshRenderer>(); r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ProductStartupShowroomAuthoring.SurfaceMaterialPath);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
    }
}
