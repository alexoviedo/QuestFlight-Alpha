using System;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void RunAmbientVisualQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_AMBIENT_EVIDENCE");
            if(string.IsNullOrEmpty(output))throw new InvalidOperationException("QFL_AMBIENT_EVIDENCE is required.");
            Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport {generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                scenePath=ProductionScenePath,outputDirectory=output,renderWidth=1440,renderHeight=960,
                humanHeadsetAcceptance="NOT_RUN",flightPhaseImageSemantics="Editor pilot-seat presentation views; separate deterministic traffic scenarios. Not OpenXR simulator or Quest timing evidence."};
            try
            {
                BoulderAmbientAircraftAuthoring.Build();
                var context=LoadContext(1440,960,report);
                var roots=context.scene.GetRootGameObjects();
                var mode=roots.SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                var traffic=roots.SelectMany(r=>r.GetComponentsInChildren<AmbientTrafficDirector>(true)).Single();
                if(!mode.AuthoredFlightCoordinator.InitializeSelectedBackend())throw new InvalidOperationException(mode.AuthoredFlightCoordinator.LastError);
                mode.SelectFreeFlight();mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();mode.AuthoredModePanelRoot.SetActive(false);
                traffic.pilotCamera=context.camera;
                var player=mode.AuthoredFlightCoordinator.CurrentState;
                var eye=context.rig.PilotSeatAnchor.position;
                var forward=context.rig.transform.rotation;
                for(int step=0;step<=600;step++)
                {
                    player.simulationTimeSeconds=step*.1;
                    traffic.StepTraffic(player,.1f,BoulderWeatherPreset.Calm);
                    if(step==220||step==270||step==320)
                    {
                        string id="ambient_pilot_circuit_"+(step/10);
                        Capture(context,report,output,1440,960,new ShotDefinition(id,"Pilot seat, right window taxi at "+step/10+" seconds","ambient_pilot",eye,forward*Quaternion.Euler(0,75,0),72,ViewProfile.Cockpit));
                    }
                }
                // Verify each exterior under the actual product shader and sun, in addition to pilot shots.
                foreach(int index in new[]{0,1,3})
                {
                    var actor=traffic.actors[index];
                    var active=actor.lodRoots.Select(r=>r.activeSelf).ToArray();
                    for(int lod=0;lod<3;lod++)actor.lodRoots[lod].SetActive(lod==0);
                    Vector3 at=actor.Position-new Vector3(18,-7,24);
                    Capture(context,report,output,1440,960,new ShotDefinition("ambient_model_"+index,actor.aircraftId,"ambient_asset",at,LookAt(at,actor.Position),45,ViewProfile.Environment));
                    for(int lod=0;lod<3;lod++)actor.lodRoots[lod].SetActive(active[lod]);
                }
                // Reach actual ambient flight before authoring a normal, separated following-pilot presentation pose.
                player.positionUnityMeters=new Vector3(4000,700,3000);player.weightOnWheels=false;

                var lead=traffic.actors[0];
                if(lead.OnGround||lead.Suppressed)throw new InvalidOperationException("No airborne lead for pilot flight capture.");
                var heading=lead.Velocity;heading.y=0;heading.Normalize();
                var aircraftPosition=lead.Position-heading*350-Vector3.up*30;
                context.rig.transform.SetPositionAndRotation(aircraftPosition,Quaternion.LookRotation(heading));
                player.positionUnityMeters=aircraftPosition;player.velocityUnityMetersPerSecond=heading*40;player.simulationTimeSeconds=60.1;
                traffic.StepTraffic(player,.1f,BoulderWeatherPreset.Calm);
                eye=context.rig.PilotSeatAnchor.position;forward=context.rig.transform.rotation;
                Capture(context,report,output,1440,960,new ShotDefinition("ambient_pilot_airborne","Pilot following at 350m separation","ambient_pilot",eye,forward,72,ViewProfile.Cockpit));
                var glider=traffic.actors[4];heading=glider.Velocity;heading.y=0;heading.Normalize();
                aircraftPosition=glider.Position-heading*350-Vector3.up*30;
                context.rig.transform.SetPositionAndRotation(aircraftPosition,Quaternion.LookRotation(heading));
                player.positionUnityMeters=aircraftPosition;player.simulationTimeSeconds=60.2;traffic.StepTraffic(player,.1f,BoulderWeatherPreset.Calm);
                eye=context.rig.PilotSeatAnchor.position;forward=context.rig.transform.rotation;
                Capture(context,report,output,1440,960,new ShotDefinition("ambient_pilot_sailplane","Pilot view of foothill sailplane","ambient_pilot",eye,forward,72,ViewProfile.Cockpit));
                report.requiredShotCount=8;report.passed=report.shots.Count==8&&report.shots.All(s=>s.imageNotBlank&&string.IsNullOrEmpty(s.error));
                report.classification=report.passed?"IMAGES_READY_FOR_INSPECTION":"FAIL_IMAGES";
            }
            catch(Exception e){report.errors.Add(e.ToString());report.passed=false;report.classification="FAIL_IMAGES";Debug.LogException(e);}
            WriteReportFiles(report,output);
            UnityEditor.EditorApplication.Exit(report.passed?0:1);
        }
    }
}
