using System;
using System.IO;
using UnityEngine;
namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void ReviewFuelDetents()
        {
            string root=System.Environment.GetEnvironmentVariable("QFL_TRAINER_REVIEW_ROOT");
            Directory.CreateDirectory(Path.Combine(root,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                scenePath=ProductionScenePath,outputDirectory=root,renderWidth=1200,renderHeight=1200,humanHeadsetAcceptance="NOT_RUN"};
            var c=LoadContext(1200,1200,report);
            var selector=c.rig.transform.Find(QuestFlight.Editor.CockpitDynamicsIntegrationAuthoring.FuelSelectorPath);
            Quaternion rest=selector.localRotation;
            Vector3 normal=selector.up;
            if(Vector3.Dot(normal,c.rig.PilotSeatAnchor.position-selector.position)<0)normal=-normal;
            Vector3 eye=selector.position+normal*.42f;
            foreach(float angle in new[]{0f,-90f,90f,180f})
            {
                selector.localRotation=rest*Quaternion.AngleAxis(angle,Vector3.up);
                Capture(c,report,root,1200,1200,new ShotDefinition("fuel_"+angle,"Fuel selector detent","cockpit",eye,
                    Quaternion.LookRotation(selector.position-eye,c.rig.transform.forward),48,ViewProfile.Cockpit));
            }
            WriteReportFiles(report,root);
        }

        public static void FitAndReviewAttitude()
        {
            QuestFlight.Editor.CockpitInstrumentV2Authoring.AlignTrainerAttitudeAssembly();
            string root=System.Environment.GetEnvironmentVariable("QFL_TRAINER_REVIEW_ROOT");
            Directory.CreateDirectory(Path.Combine(root,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                scenePath=ProductionScenePath,outputDirectory=root,renderWidth=1920,renderHeight=1440,humanHeadsetAcceptance="NOT_RUN"};
            var c=LoadContext(1920,1440,report);
            Vector3 eye=c.rig.PilotSeatAnchor.position;
            Vector3 target=c.rig.AircraftVisualRoot.TransformPoint(new Vector3(-.19f,.68f,.33f));
            Capture(c,report,root,1920,1440,new ShotDefinition("attitude_fit","Attitude mounting correction","cockpit",eye,Quaternion.LookRotation(target-eye,c.rig.transform.up),57,ViewProfile.Cockpit));
            WriteReportFiles(report,root);
        }

        public static void BuildAndReviewTrainer()
        {
            QuestFlight.Editor.CockpitInstrumentV2Authoring.BuildCompleteTrainer();
            QuestFlightLab.PhotorealAerialSurfaceV1.Reflection.Editor.PhotorealAerialReflectionAuthoring.RefreshTrainerCabinReflection();
            string root=System.Environment.GetEnvironmentVariable("QFL_TRAINER_REVIEW_ROOT");
            if(string.IsNullOrEmpty(root))throw new InvalidOperationException("Evidence directory required");
            Directory.CreateDirectory(Path.Combine(root,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                scenePath=ProductionScenePath,outputDirectory=root,renderWidth=1920,renderHeight=1440,humanHeadsetAcceptance="NOT_RUN"};
            var c=LoadContext(1920,1440,report);
            foreach(float bank in new[]{0f,-25f,25f})
            {
                SetAircraftPose(c,new Vector3(0,500,0),Quaternion.Euler(0,bank==0?0:180,bank));
                Vector3 eye=c.rig.PilotSeatAnchor.position;
                Vector3 target=c.rig.AircraftVisualRoot.TransformPoint(new Vector3(-.19f,.68f,.33f));
                Capture(c,report,root,1920,1440,new ShotDefinition("trainer_"+bank,"Trainer cockpit","cockpit",eye,Quaternion.LookRotation(target-eye,c.rig.transform.up),57,ViewProfile.Cockpit));
            }
            WriteReportFiles(report,root);
        }
    }
}
