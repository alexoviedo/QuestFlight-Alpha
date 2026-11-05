using System;
using System.IO;
using QuestFlight.Product.Training;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static class SoaringTrainingAuthoring
    {
        public const string Folder="Assets/Production/SoaringV1/Training";
        public static void RunPresentationAndTraining(){Run();SoaringAircraftAuthoring.Run();}
        public static void Run()
        {
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var sgs=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/SGS233A_Reference.asset");
            var terrain=AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>("Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");
            var moderate=AssetDatabase.LoadAssetAtPath<SoaringWeatherDefinition>(SoaringAtmosphereAuthoring.Folder+"/ModerateThermal.asset");
            var stronger=AssetDatabase.LoadAssetAtPath<SoaringWeatherDefinition>(SoaringAtmosphereAuthoring.Folder+"/StrongerSupported.asset");
            var calm=AssetDatabase.LoadAssetAtPath<SoaringWeatherDefinition>(SoaringAtmosphereAuthoring.Folder+"/CalmValidation.asset");
            var catalog=new TrainingLessonDefinition[7];
            TrainingStage S(TrainingTask task,string title,string instruction,string retry,float dwell=5,float timeout=300,float speed=50)=>new TrainingStage{task=task,title=title,instruction=instruction,retryAdvice=retry,successFeedback=title+" was observed within the reference practice window.",dwellSeconds=dwell,timeoutSeconds=timeout,speedKnots=speed};
            for(int i=0;i<7;i++)
            {
                string id=new[]{"preparation","aerotow","glide_energy","thermal","ridge","circuit_landing","ground_abort"}[i];
                string path=Folder+"/"+(i+1).ToString("00")+"_"+id+".asset";
                var lesson=AssetDatabase.LoadAssetAtPath<TrainingLessonDefinition>(path);
                if(lesson==null){lesson=ScriptableObject.CreateInstance<TrainingLessonDefinition>();AssetDatabase.CreateAsset(lesson,path);}
                lesson.program=TrainingProgramKind.Sgs233Reference;lesson.lessonId="sgs233.reference."+id;
                lesson.displayName=new[]{"Aircraft and launch preparation","Normal aerotow","Glide and energy","Thermal soaring","Ridge soaring","Circuit and landing","Supported ground abort"}[i];
                lesson.groundTowPreparation=i==0||i==1||i==6;lesson.soaringWeather=i==4?stronger:i==2||i==5?calm:moderate;
                lesson.initialThrottle=lesson.initialFlaps=0;lesson.initialTrim=sgs.referenceTrimNoseUp;lesson.goAroundStage=-1;
                lesson.allowDemonstration=i==2;lesson.airportExercise=lesson.groundTowPreparation||i==5;
                lesson.orientation=Array.Empty<TrainingReference>();lesson.briefingNarration=null;
                Vector3 start=i==4?new Vector3(-6150,0,1400):i==5?new Vector3(500,308.5f,550):i==3?new Vector3(1305,470,2600):new Vector3(1200,850,2200);
                if(!terrain.TrySample(start.x,start.z,out var ground))throw new InvalidOperationException("Lesson start terrain unavailable.");
                if(i==4)start.y=ground.heightMeters+120;
                var geo=FlightFrameConversions.UnityToGeodetic(start,GeodeticReference.Kbdu);var initial=sgs.airborneReference;
                initial.latitudeDegrees=geo.latitudeDegrees;initial.longitudeDegrees=geo.longitudeDegrees;initial.altitudeMslMeters=geo.altitudeMslMeters;
                initial.terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+ground.heightMeters;initial.engineRunning=false;
                initial.headingDegrees=i==4?0:i==5?270:i==3?166:90;initial.calibratedAirspeedKnots=i==3||i==4?40:50;initial.pitchDegrees=i==3?4:0;initial.bankDegrees=0;lesson.start=initial;
                const string scope="SGS 2-33A / PA-18 development reference, 950 lb dual glider loading. These are simulator modules, not a Mile High syllabus or training credit. Physical inspection and crew communication are explained, not scored as performed. ";
                lesson.briefing=scope+new[]{
                    "At rest, check stick and rudder both ways, airbrakes through wheel braking and the red release. Use Menu > Launch preparation for crew reconnection, or Skip preflight and prepare launch (no checklist credit). RCAF 2017 AOI section3 paragraph20 lists trim full forward before takeoff; the manufacturer recommends forward bungee trim for solo. This simulator retains its calibrated native reference preset and requires no arbitrary trim sweep. External inspection remains unmodeled.",
                    "Complete the ground checks and crew reconnection. Close the brakes, center the stick, then request shared-runway clearance in Menu > Launch preparation. Follow the tug near55kt. At the reference release cue, check separation and deliberately pull the red handle or A. The tug uses native controls to return.",
                    "Use pitch for speed and the yaw string for coordination. Move trim to reduce sustained stick force, observe descent with airbrakes, then make a modest slip. Optional demonstration/assistance is labeled and never gives independent credit. There is no engine or powered go-around.",
                    "A fixed reference source near the northeast practice area has a lift core and surrounding sink. Center by observed energy and coordinated turns, gain20m total-energy height, then leave deliberately. The ordinary VSI includes speed-to-height exchange; an ideal TE aid is separately identified.",
                    "Stronger supported easterly reference flow crosses the terrain. Stay east of the rising ridge, above60m terrain margin and turn outward through east. Exit east early. Wave, rotor, exact local clearance and moderate-ridge sustained acceptance are not established.",
                    "Reference08 inside circuit: downwind south of the powered900m-north track and Jay Road; base east of the outer -1900m approach. Plan unpowered descent toward the paved centerline30m south of the local reference. Final42–58kt is a software practice window. No engine escape or automatic relocation.",
                    "Development ground-abort case only. Perform preparation, start the slow launch, and select Abort ground launch before20kt. Apply the combined lever fully to stop. Airborne cable-break decision training requires additional evidence; no universal turnback is taught."
                }[i];
                lesson.learningGoal=lesson.displayName+" using measured controls, native state and sustained conditions.";
                lesson.reflection="Which outside cue and instrument supported your decision? What margin would you require before an instructor-reviewed real flight?";
                if(i==0)lesson.stages=new[]{S(TrainingTask.SoaringPreparation,"Check controls and release","Sweep stick/rudder both ways, pull the left lever fully, then pull the red release / A. Menu > Launch preparation also offers Skip preflight (no checklist credit).","Finish simulated checks while stopped, or explicitly skip; external inspection remains unmodeled.",1),S(TrainingTask.SoaringCrewReady,"Crew reconnection","Menu > Launch preparation > Reconnect and prepare launch (already prepared after Skip preflight). Return to cockpit, then close the combined brake lever.","Explicitly prepare the crew/rope and close both airbrakes and wheel brake.",2)};
                if(i==1)lesson.stages=new[]{S(TrainingTask.SoaringPreparation,"Ground checks","Complete control sweeps, combined brake check and release test, or Menu > Launch preparation > Skip preflight (no checklist credit).","Remain stopped while checking the actual controls.",1),S(TrainingTask.SoaringLaunch,"Clearance and launch","Menu > Launch preparation > Reconnect and prepare launch (already prepared after Skip preflight). Return to cockpit; close brakes and center controls. Then Menu > Launch preparation > Clear and begin launch.","Both strips must be clear. Close brakes and center controls before requesting clearance.",3,500),S(TrainingTask.SoaringTowPosition,"Tow position","Follow the tug with small coordinated corrections near55kt. Keep a steady sight picture through the turn.","Reduce lateral/height error and oscillation; maintain coordination and modest rope load.",20,700),S(TrainingTask.SoaringRelease,"Deliberate release","Wait for the reference release cue; check separation and pull red handle / A. Then stabilize the unpowered glide.","Release is pilot-controlled. Keep safe speed and separation from the tug.",3,900)};
                if(i==2)lesson.stages=new[]{S(TrainingTask.SoaringSpeed,"Glide speed","Settle at50kt with wings nearly level and yaw string centered.","Use pitch to control speed; there is no throttle.",8),S(TrainingTask.SoaringTrim,"Relieve stick force","Adjust trim and settle the glide with little sustained stick input.","Move trim, keep coordination and reduce held pitch input.",8),S(TrainingTask.SoaringAirbrake,"Increase descent","Extend airbrakes beyond halfway while preserving35–65kt and nearly level wings.","Pitch maintains speed; the lever changes drag and descent.",6),S(TrainingTask.SoaringSlip,"Modest slip","Close airbrakes. Hold a modest side-slip with bank below 20 degrees for 3 seconds, then recover to centered yaw string and wings nearly level for 3 seconds.","Keep speed in range; use restrained opposing controls. Spins are not validated.",3)};
                if(i==3)lesson.stages=new[]{S(TrainingTask.SoaringLift,"Find useful lift","Enter the reference thermal near the northeast practice area at40–45kt. Look for sustained energy gain.","An ordinary VSI rise during deceleration alone does not prove useful lift.",4),S(TrainingTask.SoaringEnergyGain,"Center and gain","Circle smoothly at least 270 degrees in one direction in useful thermal lift. Gain 20 m total-energy height while coordinated; leaving the source resets the gain window.","Tighten or widen carefully to center lift; avoid surrounding sink.",3,800),S(TrainingTask.SoaringLiftExit,"Leave deliberately","Leave the lift, level the wings and establish a safe return glide.","Exit the source and settle35–65kt; plan landing before margin runs out.",5)};
                if(i==4)lesson.stages=new[]{S(TrainingTask.SoaringRidgeTrack,"Windward track","Follow the windward side north/south above60m terrain clearance and35–65kt.","Stay east of the rising terrain; this stronger reference is the sustained test condition.",12),S(TrainingTask.SoaringOutwardTurn,"Turn away from terrain","Reverse heading at least 140 degrees in one direction through the open eastern side, preserving 60 m terrain clearance. Heading oscillation does not count.","Observe the eastward half-circle; do not turn into rising western terrain.",2),S(TrainingTask.SoaringRidgeExit,"Exit east","Leave the ridge toward the east with at least80m local clearance.","Exit early; a ridge can stop supporting flight as wind changes.",5)};
                if(i==5)lesson.stages=new[]{S(TrainingTask.SoaringDownwind,"Inside downwind","Fly west on the inside north-side downwind. Reach the western half above180m field height.","Plan energy early and stay inside the powered pattern.",3),S(TrainingTask.SoaringBase,"Base turn","Turn south on base between700m and1800m west of the reference.","Use airbrakes as needed; preserve a safe speed and landing margin.",2),S(TrainingTask.SoaringFinal,"Stable final","Align east toward the paved centerline at42–58kt with controlled descent.","Use pitch and airbrakes; there is no powered go-around.",3),S(TrainingTask.SoaringLanding,"Touchdown","Round out and land on the reference paved strip with wings level.","Reduce sink and bank before contact; a hard or off-runway touchdown needs another attempt.",1),S(TrainingTask.SoaringStop,"Rollout and stop","Apply the last part of the combined lever for wheel braking and stop on the strip.","Stay aligned and hold the wheel brake; crew movement is a later explicit action.",3)};
                if(i==6)lesson.stages=new[]{S(TrainingTask.SoaringPreparation,"Prepare abort exercise","Complete the stopped controls and release checks.","Use every simulated control check before crew preparation.",1),S(TrainingTask.SoaringGroundAbort,"Abort before20kt","Reconnect and prepare launch in Menu > Launch preparation. Return to cockpit, close brakes and center controls, then Clear and begin launch. During the slow roll, choose Abort ground launch, then pull the combined lever fully and stop.","This supported case remains on the ground. Abort early and brake to a complete stop.",3,500)};
                foreach(var stage in lesson.stages)stage.targetEnu=SoaringLessonReference.Target(stage.task,lesson);
                if(!lesson.IsValid)throw new InvalidOperationException("Invalid soaring lesson "+id);EditorUtility.SetDirty(lesson);catalog[i]=lesson;
            }
            string world="Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";var root=PrefabUtility.LoadPrefabContents(world);
            try{var controller=root.GetComponentInChildren<ProductTrainingController>(true);controller.ConfigureSoaringCatalogForAuthoring(catalog);PrefabUtility.SaveAsPrefabAsset(root,world);}
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();Debug.Log("[SoaringTraining] Seven separate stable IDs authored; original C172 assets preserved.");
        }
    }
}
