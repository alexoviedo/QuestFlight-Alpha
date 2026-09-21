using System;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        // Focused authored UI review, not a physics or Quest performance test.
        public static void RunInterfaceVisualQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR");
            Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport { generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                scenePath=ProductionScenePath,outputDirectory=output,renderWidth=1280,renderHeight=960,humanHeadsetAcceptance="PENDING" };
            float scale=Time.timeScale;
            try
            {
                var c=LoadContext(1280,960,report);
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                void TextReady()
                {
                    foreach(var root in c.scene.GetRootGameObjects())
                        foreach(var text in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))text.ForceMeshUpdate(true,true);
                }
                TextReady();CaptureStereoSelector(c,report,output,1280,960,mode);
                var flight=mode.AuthoredFlightCoordinator;
                if(!flight.IsInitialized&&!flight.InitializeSelectedBackend())throw new InvalidOperationException(flight.LastError);
                if(!flight.TryRecoverToInitialConditions(mode.AuthoredTraining.Lessons[0].start))throw new InvalidOperationException(flight.LastError);
                mode.SelectFreeFlight();mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                void Pair(string id,float pitch=0)
                {
                    TextReady();var facing=c.rig.AircraftSimulationRoot.rotation*Quaternion.Euler(pitch,0,0);
                    File.WriteAllLines(Path.Combine(output,id+"_text_state.txt"),c.scene.GetRootGameObjects()
                        .SelectMany(r=>r.GetComponentsInChildren<TMPro.TextMeshPro>(true)).Where(t=>t.gameObject.activeInHierarchy)
                        .Select(t=>$"{t.transform.parent.name}/{t.name}: size={t.fontSize} enabled={t.renderer.enabled} off={t.renderer.forceRenderingOff} local={t.transform.localPosition} bounds={t.renderer.bounds} text={t.text}"));
                    for(int side=0;side<2;side++)Capture(c,report,output,1280,960,new ShotDefinition(id+(side==0?"_left":"_right"),id,"interface",
                        c.rig.PilotSeatAnchor.position+facing*Vector3.right*(side==0?-.032f:.032f),facing,72,ViewProfile.Cockpit));
                }
                var nav=mode.AuthoredNavigation;
                nav.OpenMenu();Pair("pause");nav.Resume();
                // Editor has no live XR focus; set only the preview page explicitly.
                typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.None);
                // Presentation preview: no player loop/XR focus runs in these editor captures.
                typeof(ProductFlightModeCoordinator).GetProperty("State").SetValue(mode,ProductFlightModeCoordinator.FlightModeState.Training);
                typeof(ProductTrainingController).GetProperty("State").SetValue(mode.AuthoredTraining,TrainingSessionState.Menu);
                nav.SendMessage("Refresh");Pair("lessons");
                mode.AuthoredTraining.SelectLesson(0);nav.SendMessage("Refresh");Pair("briefing");
                nav.ShowOrientation();nav.SendMessage("Refresh");Pair("reference",12);
                nav.Back();nav.OpenCalibration();nav.SendMessage("Refresh");Pair("calibration");nav.Back();nav.Resume();
                // Authored cue composition at the taxi start; explicit preview, not evidence of lesson progression.
                var instruction=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductUiInstruction>(true)).Single();
                var navObject=new UnityEditor.SerializedObject(nav).FindProperty("panel").objectReferenceValue as GameObject;
                navObject.SetActive(false);instruction.gameObject.SetActive(true);
                instruction.Present("Your controls","Check roll",mode.AuthoredTraining.Lessons[0].stages[0].instruction,0,7,true,false);
                Pair("instruction",8);
                instruction.Present("Your controls","Go around","Unstable approach — go around",0,7,true,true);Pair("urgent",8);
                report.classification="INTERFACE_EDITOR_CAPTURES_REVIEW_REQUIRED";
                report.requiredShotCount=report.shots.Count;
                // Existing global scenery audit results remain visible in the report, not silently waived.
                report.passed=report.shots.All(s=>s.passed);
            }
            catch(Exception e){report.errors.Add(e.ToString());report.classification="FAIL_INTERFACE_CAPTURE";Debug.LogException(e);}
            finally{Time.timeScale=scale;}
            WriteReportFiles(report,output);
        }

        // Presentation-only capture of the outdoor marker at the authored taxi checkpoint.
        public static void RunObjectiveVisualQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR");
            Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport { generatedUtc=DateTime.UtcNow.ToString("O"),
                unityVersion=Application.unityVersion,scenePath=ProductionScenePath,outputDirectory=output,
                renderWidth=1280,renderHeight=960,requiredShotCount=2,humanHeadsetAcceptance="NOT_RUN" };
            float scale=Time.timeScale;
            try
            {
                var c=LoadContext(1280,960,report);
                var all=c.scene.GetRootGameObjects();
                var mode=all.SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                var flight=mode.AuthoredFlightCoordinator;
                if(!flight.IsInitialized&&!flight.InitializeSelectedBackend()) throw new InvalidOperationException(flight.LastError);
                var lesson=mode.AuthoredTraining.Lessons[0];
                if(!flight.TryRecoverToInitialConditions(lesson.start)) throw new InvalidOperationException(flight.LastError);
                mode.SelectFreeFlight(); mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                var marker=all.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="TrainingAirportReference");
                var target=lesson.stages.First(s=>s.task==QuestFlight.Product.Training.TrainingTask.Taxi).targetEnu;
                if(!flight.terrainSurface.TrySample(target.x,target.y,out var ground)) throw new InvalidOperationException("Missing taxi terrain.");
                marker.position=new Vector3(target.x,ground.heightMeters+.15f,target.y); marker.localScale=Vector3.one;
                marker.Find("ObjectivePulse").localPosition=Vector3.up*2.5f; marker.gameObject.SetActive(true);
                var facing=c.rig.AircraftSimulationRoot.rotation;
                for(int eye=0;eye<2;eye++) Capture(c,report,output,1280,960,new ShotDefinition(
                    "taxi_objective_"+(eye==0?"left":"right"),"Outdoor marker at taxi start","objective",
                    c.rig.PilotSeatAnchor.position+facing*Vector3.right*(eye==0?-.032f:.032f),facing,72,ViewProfile.Cockpit));
                report.passed=report.shots.Count==2&&report.shots.All(s=>s.passed);
                report.classification=report.passed?"PASS_OBJECTIVE_EDITOR_CAPTURE":"FAIL_OBJECTIVE_EDITOR_AUDIT";
            }
            catch(Exception e) { report.errors.Add(e.ToString()); report.classification="FAIL_OBJECTIVE_EDITOR_CAPTURE"; }
            finally { Time.timeScale=scale; }
            WriteReportFiles(report,output);
        }

        // Narrow standard-pipeline evidence; actual OpenXR stereo remains a separate gate.
        public static void RunNavigationVisualQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR");
            if(string.IsNullOrEmpty(output)) throw new InvalidOperationException("QA output required.");
            Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport { generatedUtc=DateTime.UtcNow.ToString("O"),
                unityVersion=Application.unityVersion,scenePath=ProductionScenePath,outputDirectory=output,
                renderWidth=1280,renderHeight=960,requiredShotCount=9,
                humanHeadsetAcceptance="NOT_RUN",flightPhaseImageSemantics="Authored navigation and pointer review; editor cameras, not Quest performance evidence." };
            float scale=Time.timeScale;
            try
            {
                var c=LoadContext(1280,960,report);
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                CaptureStereoSelector(c,report,output,1280,960,mode);
                var flight=c.rig.GetComponentInParent<FlightDynamicsCoordinator>();
                if(flight==null) flight=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<FlightDynamicsCoordinator>(true)).Single();
                if(!flight.IsInitialized&&!flight.InitializeSelectedBackend()) throw new InvalidOperationException(flight.LastError);
                mode.SelectFreeFlight(); mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                var nav=mode.AuthoredNavigation;
                foreach(int pitch in new[]{0,15}) Capture(c,report,output,1280,960,new ShotDefinition(
                    "flight_without_ui_"+pitch,"Same cockpit without teaching UI","baseline",c.rig.PilotSeatAnchor.position,
                    c.rig.AircraftSimulationRoot.rotation*Quaternion.Euler(pitch,0,0),72,ViewProfile.Cockpit));
                void Pair(string id,Quaternion orientation)
                {
                    Vector3 eye=c.rig.PilotSeatAnchor.position;
                    for(int side=0;side<2;side++) Capture(c,report,output,1280,960,new ShotDefinition(
                        id+(side==0?"_left":"_right"),id,"navigation",eye+orientation*Vector3.right*(side==0?-.032f:.032f),orientation,72,ViewProfile.Cockpit));
                }
                nav.OpenMenu(); Pair("app_menu",c.rig.AircraftSimulationRoot.rotation);
                nav.ShowOrientation(); mode.AuthoredTraining.ShowReference(1);
                nav.SendMessage("Refresh");
                Pair("attitude_reference",c.rig.AircraftSimulationRoot.rotation*Quaternion.Euler(15,0,0));
                mode.AuthoredTraining.ShowReference(1);
                nav.SendMessage("Refresh");
                Pair("heading_reference",c.rig.AircraftSimulationRoot.rotation*Quaternion.Euler(20,0,0));
                report.passed=report.shots.Count==9&&report.shots.All(s=>s.passed);
                report.classification=report.passed?"PASS_NAVIGATION_MACHINE_QA_STEREO_AND_HEADSET_PENDING":"FAIL_NAVIGATION_MACHINE_QA";
            }
            catch(Exception e) { report.errors.Add(e.ToString());report.passed=false;report.classification="FAIL_NAVIGATION_MACHINE_QA"; }
            finally { Time.timeScale=scale; }
            WriteReportFiles(report,output);
            Debug.Log("[NavigationQA] "+report.classification);
        }
    }
}
