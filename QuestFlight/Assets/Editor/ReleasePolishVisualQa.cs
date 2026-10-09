using System;
using System.IO;
using System.Linq;
using System.Reflection;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void RunMenuContextQaBatch()
        {
            try { RunReleasePolishQa(); RunProductionVisualQa(); EditorApplication.Exit(0); }
            catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void RunReleasePolishQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_RELEASE_POLISH_QA");
            Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),scenePath=ProductionScenePath,outputDirectory=output,renderWidth=1440,renderHeight=1080,humanHeadsetAcceptance="PENDING"};
            float time=Time.timeScale;
            bool hadScale=PlayerPrefs.HasKey("QuestFlight.TextScale"),hadContrast=PlayerPrefs.HasKey("QuestFlight.HighContrast");
            float previousScale=PlayerPrefs.GetFloat("QuestFlight.TextScale",1);int previousContrast=PlayerPrefs.GetInt("QuestFlight.HighContrast",0);
            try
            {
                var c=LoadContext(1440,1080,report);
                foreach(var renderer in c.rig.GetComponentsInChildren<Renderer>(true))
                    if(renderer.sharedMaterials.Any(m=>m!=null&&m.shader!=null&&m.shader.name.StartsWith("Interaction/")))renderer.gameObject.SetActive(false);
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                var nav=mode.AuthoredNavigation;
                typeof(ProductFlightModeCoordinator).GetProperty("State").SetValue(mode,ProductFlightModeCoordinator.FlightModeState.FreeFlight);
                typeof(ProductTrainingController).GetProperty("State").SetValue(mode.AuthoredTraining,TrainingSessionState.Inactive);
                const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                foreach(var page in new[]{ProductAppNavigation.Page.Tools,ProductAppNavigation.Page.Controls,ProductAppNavigation.Page.Comfort,ProductAppNavigation.Page.Briefing,ProductAppNavigation.Page.Map,ProductAppNavigation.Page.Kneeboard,ProductAppNavigation.Page.Activities,ProductAppNavigation.Page.History,ProductAppNavigation.Page.Summary,ProductAppNavigation.Page.Report,ProductAppNavigation.Page.QuickStart,ProductAppNavigation.Page.Bindings,ProductAppNavigation.Page.InputPreview,ProductAppNavigation.Page.InstrumentHelp,ProductAppNavigation.Page.Captions,ProductAppNavigation.Page.ControlDiagram,ProductAppNavigation.Page.Checklist})
                {
                    typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,page);
                    typeof(ProductAppNavigation).GetMethod("Refresh",flags).Invoke(nav,null);
                    foreach(var root in c.scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))t.ForceMeshUpdate(true,true);
                    for(int eye=0;eye<2;eye++)Capture(c,report,output,1440,1080,new ShotDefinition("polish_"+page+(eye==0?"_left":"_right"),page.ToString(),"release_polish",c.rig.PilotSeatAnchor.position+c.rig.AircraftSimulationRoot.right*(eye==0?-.032f:.032f),c.rig.AircraftSimulationRoot.rotation,72,ViewProfile.Cockpit));
                }
                PlayerPrefs.SetFloat("QuestFlight.TextScale",1.2f);PlayerPrefs.SetInt("QuestFlight.HighContrast",1);
                foreach(var root in c.scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))t.fontSize*=1.2f;
                foreach(var page in new[]{ProductAppNavigation.Page.Controls,ProductAppNavigation.Page.Comfort,ProductAppNavigation.Page.Tools,ProductAppNavigation.Page.Map,ProductAppNavigation.Page.Captions,ProductAppNavigation.Page.ControlDiagram,ProductAppNavigation.Page.InputPreview,ProductAppNavigation.Page.Checklist})
                {
                    typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,page);
                    typeof(ProductAppNavigation).GetMethod("Refresh",flags).Invoke(nav,null);
                    foreach(var root in c.scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))t.ForceMeshUpdate(true,true);
                    for(int eye=0;eye<2;eye++)Capture(c,report,output,1440,1080,new ShotDefinition("large_"+page+(eye==0?"_left":"_right"),page.ToString(),"release_polish",c.rig.PilotSeatAnchor.position+c.rig.AircraftSimulationRoot.right*(eye==0?-.032f:.032f),c.rig.AircraftSimulationRoot.rotation,72,ViewProfile.Cockpit));
                }
                typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.None);
                typeof(ProductAppNavigation).GetMethod("Refresh",flags).Invoke(nav,null);
                var caption=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductUiInstruction>(true)).First();
                // The menu-size fixture above scales all TMP objects for editor-only
                // inspection. Captions have their own scale; undo that fixture first.
                foreach(var text in caption.GetComponentsInChildren<TMPro.TextMeshPro>(true))text.fontSize/=1.2f;
                bool hadCaptionScale=PlayerPrefs.HasKey("QuestFlight.CaptionScale");float captionScale=PlayerPrefs.GetFloat("QuestFlight.CaptionScale",1);
                try
                {
                    caption.gameObject.SetActive(true);
                    foreach(float size in new[]{1f,1.6f})
                    {
                        PlayerPrefs.SetFloat("QuestFlight.CaptionScale",size);
                        caption.Present("Instructor","Follow the taxiway","Use small rudder movements to keep the nose on the yellow centerline.",2,8,true,false);
                        foreach(var text in caption.GetComponentsInChildren<TMPro.TextMeshPro>(true))text.ForceMeshUpdate(true,true);
                        for(int eye=0;eye<2;eye++)Capture(c,report,output,1440,1080,new ShotDefinition("caption_"+size.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+(eye==0?"_left":"_right"),"Caption size","release_polish",c.rig.PilotSeatAnchor.position+c.rig.AircraftSimulationRoot.right*(eye==0?-.032f:.032f),c.rig.AircraftSimulationRoot.rotation,72,ViewProfile.Cockpit));
                    }
                }
                finally {caption.gameObject.SetActive(false);if(hadCaptionScale)PlayerPrefs.SetFloat("QuestFlight.CaptionScale",captionScale);else PlayerPrefs.DeleteKey("QuestFlight.CaptionScale");}
                for(int eye=0;eye<2;eye++)Capture(c,report,output,1440,1080,new ShotDefinition("polish_materials"+(eye==0?"_left":"_right"),"Materials","release_polish",c.rig.PilotSeatAnchor.position+c.rig.AircraftSimulationRoot.right*(eye==0?-.032f:.032f),c.rig.AircraftSimulationRoot.rotation*Quaternion.Euler(32,0,0),72,ViewProfile.Cockpit));
                // Exercise the actual showroom suppression path, not just flight pages.
                typeof(ProductFlightModeCoordinator).GetProperty("State").SetValue(mode,ProductFlightModeCoordinator.FlightModeState.Selection);
                mode.AuthoredModeMenuView.ShowMainSelection();
                foreach(var page in new[]{ProductAppNavigation.Page.None,ProductAppNavigation.Page.Controls,ProductAppNavigation.Page.InstrumentHelp,ProductAppNavigation.Page.Calibration,ProductAppNavigation.Page.App})
                {
                    typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,page);
                    if(page==ProductAppNavigation.Page.InstrumentHelp)typeof(ProductAppNavigation).GetMethod("ShowInstrumentHelp",flags).Invoke(nav,null);
                    typeof(ProductAppNavigation).GetMethod("Refresh",flags).Invoke(nav,null);
                    foreach(var root in c.scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))t.ForceMeshUpdate(true,true);
                    bool preview=page==ProductAppNavigation.Page.InstrumentHelp||page==ProductAppNavigation.Page.Calibration;
                    if(mode.AuthoredModeMenuView.ShowroomContentRoot.activeSelf==preview)throw new InvalidOperationException("Showroom preview did not switch visibility for "+page);
                    for(int eye=0;eye<2;eye++)Capture(c,report,output,1440,1080,new ShotDefinition("selection_"+page+(eye==0?"_left":"_right"),page.ToString(),"menu_context",c.rig.PilotSeatAnchor.position+c.rig.AircraftSimulationRoot.right*(eye==0?-.032f:.032f),c.rig.AircraftSimulationRoot.rotation,72,ViewProfile.Cockpit));
                }
                report.classification="RELEASE_POLISH_REVIEW_REQUIRED";report.passed=report.shots.Count>0&&report.errors.Count==0&&report.shots.All(s=>s.passed);
            }
            catch(Exception e){report.errors.Add(e.ToString());Debug.LogException(e);report.classification="FAIL_RELEASE_POLISH";}
            finally
            {
                Time.timeScale=time;
                if(hadScale)PlayerPrefs.SetFloat("QuestFlight.TextScale",previousScale);else PlayerPrefs.DeleteKey("QuestFlight.TextScale");
                if(hadContrast)PlayerPrefs.SetInt("QuestFlight.HighContrast",previousContrast);else PlayerPrefs.DeleteKey("QuestFlight.HighContrast");
                PlayerPrefs.Save();
            }
            WriteReportFiles(report,output);
            if(!report.passed)throw new InvalidOperationException("Visual QA capture failed or evaluated no shots; see written report.");
        }
        public static void AuthorAndCaptureReleasePolish()
        {
            QuestFlight.Editor.ProductReleasePolishAuthoring.Apply();RunReleasePolishQa();
        }
        public static void RunTesterReviewWitness()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_RELEASE_POLISH_QA");Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),scenePath=ProductionScenePath,outputDirectory=output,renderWidth=1440,renderHeight=1080,humanHeadsetAcceptance="PENDING; illustrative review fixture, not flight evidence"};
            float time=Time.timeScale;
            try
            {
                var c=LoadContext(1440,1080,report);
                foreach(var renderer in c.rig.GetComponentsInChildren<Renderer>(true))if(renderer.sharedMaterials.Any(m=>m!=null&&m.shader!=null&&m.shader.name.StartsWith("Interaction/")))renderer.gameObject.SetActive(false);
                var modes=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();modes.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                typeof(ProductFlightModeCoordinator).GetProperty("State").SetValue(modes,ProductFlightModeCoordinator.FlightModeState.FreeFlight);
                var e=modes.GetComponent<ProductFlightExperience>();var route=modes.AuthoredDemoPilot.AuthoredRouteProfile.Definition;
                typeof(ProductFlightExperience).GetProperty("LastRecord").SetValue(e,new ProductFlightExperience.Record{mode="Illustrative review fixture",utc="2026-09-28",seconds=600,nauticalMiles=8,outcome="Practice ended",observations="Observed low approach: 64–72 kt; largest bank 8°. Runway touchdown: 3 m from center; heading error 4°. Slowed to taxi speed while remaining on the runway.",advice="Repeat the approach with fewer prompts; keep looking along the runway during rollout.",path=route.waypoints.Select(p=>p.positionUnityMeters).ToArray()});
                var nav=modes.AuthoredNavigation;const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                foreach(var page in new[]{ProductAppNavigation.Page.Summary,ProductAppNavigation.Page.Map,ProductAppNavigation.Page.Controls,ProductAppNavigation.Page.InstrumentHelp})
                {
                    typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,page);typeof(ProductAppNavigation).GetMethod("Refresh",flags).Invoke(nav,null);
                    foreach(var root in c.scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))t.ForceMeshUpdate(true,true);
                    for(int eye=0;eye<2;eye++)Capture(c,report,output,1440,1080,new ShotDefinition("fixture_"+page+(eye==0?"_left":"_right"),"Illustrative review fixture", "tester_readiness",c.rig.PilotSeatAnchor.position+c.rig.AircraftSimulationRoot.right*(eye==0?-.032f:.032f),c.rig.AircraftSimulationRoot.rotation,72,ViewProfile.Cockpit));
                }
                report.classification="ILLUSTRATIVE_REVIEW_QA";report.passed=report.shots.Count>0&&report.errors.Count==0&&report.shots.All(s=>s.passed);
            }
            catch(Exception e){report.errors.Add(e.ToString());Debug.LogException(e);}
            finally{Time.timeScale=time;WriteReportFiles(report,output);}
        }
    }
}
