using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            try { Apply(); Debug.Log("[TrainingAuthoring] SUCCESS"); EditorApplication.Exit(0); }
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
                    Voice("demonstration", "The demonstration has control. Watch the outside horizon and attitude indicator as the airplane holds level, then turns gently left. Pause and restart the exercise when you are ready to practice yourself."),
                    Voice("assistance", "Assistance has control. I am holding near your current heading and altitude. Press X to take control when ready. This attempt will be marked assisted."),
                    Voice("handback", "You have control. Assistance is off. Continue the instruction shown, or press B to hear it again."),
                    Voice("go_around", "The approach is unstable. Go around. Apply full power, carb heat cold, establish the climb and retract flaps in stages."));
                mode.ConfigureTrainingForAuthoring(training);
                // Restore the existing fourth menu capability without adding another card or camera.
                var button = mode.AuthoredModePanelRoot.GetComponentsInChildren<ProductFlightModeMenuButton>(true).Single(b => b.name == "LessonsCard");
                var serialized = new SerializedObject(button);
                serialized.FindProperty("action").enumValueIndex = (int)ProductFlightModeMenuButton.MenuAction.Lessons;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var labels = button.GetComponentsInChildren<TextMesh>(true);
                foreach (var label in labels)
                {
                    if (label.name.EndsWith("Title")) label.text = "X   FLIGHT LESSONS";
                    else if (label.name.EndsWith("Subtitle")) label.text = "SIX GUIDED EXERCISES";
                    EditorUtility.SetDirty(label); PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(button);
                EditorUtility.SetDirty(training); EditorUtility.SetDirty(mode);
                PrefabUtility.SaveAsPrefabAsset(world, World);
            }
            finally { PrefabUtility.UnloadPrefabContents(world); }
            File.WriteAllText(Root + "/VoiceScripts.json", JsonUtility.ToJson(new VoiceManifest { clips = scripts.ToArray() }, true));
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
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
                if (lesson.start.calibratedAirspeedKnots > 0) AuthorAirborneTrim(lesson, flight);
                EditorUtility.SetDirty(lesson);
            }
            return catalog.ToArray();
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
