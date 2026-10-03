using System;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void RunDemoVisualQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR");
            Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport {generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                scenePath=ProductionScenePath,outputDirectory=output,renderWidth=1280,renderHeight=960,humanHeadsetAcceptance="PENDING"};
            float originalScale=Time.timeScale;
            try
            {
                var c=LoadContext(1280,960,report);
                var modes=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                var flight=modes.AuthoredFlightCoordinator;
                if(!flight.IsInitialized&&!flight.InitializeSelectedBackend())throw new Exception(flight.LastError);
                modes.SelectDemo();modes.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                var nav=modes.AuthoredNavigation;nav.OpenMenu();
                void Pair(string id)
                {
                    foreach(var root in c.scene.GetRootGameObjects())foreach(var text in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))text.ForceMeshUpdate(true,true);
                    var facing=c.rig.AircraftSimulationRoot.rotation;
                    for(int eye=0;eye<2;eye++)Capture(c,report,output,1280,960,new ShotDefinition(id+(eye==0?"_left":"_right"),id,"interface",
                        c.rig.PilotSeatAnchor.position+facing*Vector3.right*(eye==0?-.032f:.032f),facing,72,ViewProfile.Cockpit));
                }
                Pair("demo_pause");
                modes.AuthoredDemoPilot.TakeUserControl();nav.SendMessage("Refresh");Pair("manual_pause");
                var serialized=new UnityEditor.SerializedObject(modes.AuthoredTraining);
                var root=(GameObject)serialized.FindProperty("cueRoot").objectReferenceValue;
                var view=(ProductUiInstruction)serialized.FindProperty("polishedInstruction").objectReferenceValue;
                typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.None);
                nav.SendMessage("Refresh");root.SetActive(true);
                view.Present("Demo flying","Boulder scenic flight","Boulder Reservoir is a recreation area and part of the city's water supply. It makes a useful landmark when finding the airport.",3,16,true,false);
                Pair("tour_caption");
                report.passed=report.shots.Count==6&&report.shots.All(s=>s.passed);report.classification=report.passed?"PASS_DEMO_PRESENTATION_MACHINE_QA":"FAIL_DEMO_PRESENTATION_MACHINE_QA";
            }
            catch(Exception e){report.errors.Add(e.ToString());report.passed=false;report.classification="FAIL_DEMO_PRESENTATION_MACHINE_QA";}
            finally{Time.timeScale=originalScale;}
            WriteReportFiles(report,output);Debug.Log("[DemoVisualQA] "+report.classification);
        }
    }
}
