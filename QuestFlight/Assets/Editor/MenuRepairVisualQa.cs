using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void RunMenuRepairVisualQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR");
            if(string.IsNullOrEmpty(output))throw new InvalidOperationException("External QA output required.");
            Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,scenePath=ProductionScenePath,outputDirectory=output,renderWidth=1280,renderHeight=960,humanHeadsetAcceptance="NOT_RUN",flightPhaseImageSemantics="Menu presentation previews; native/UI flow acceptance is recorded separately. Capture success is not a resource-budget pass."};
            float clock=Time.timeScale;bool hadText=PlayerPrefs.HasKey("QuestFlight.TextScale");float oldText=ProductComfortPreferences.TextScale;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try
            {
                var c=LoadContext(1280,960,report);Time.timeScale=0;
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                InvokePrivate(mode,"BeginSelection");var flight=mode.AuthoredFlightCoordinator;
                if(!flight.InitializeSelectedBackend())throw new InvalidOperationException(flight.LastError);
                var nav=mode.AuthoredNavigation;nav.EditorVerificationFocus=true;
                var panel=(GameObject)typeof(ProductAppNavigation).GetField("panel",flags).GetValue(nav);
                var labels=panel.GetComponentsInChildren<ProductUiLabel>(true);
                var fontSizes=labels.ToDictionary(l=>l,l=>l.Display.fontSize);
                foreach(var renderer in c.rig.XrOrigin.GetComponentsInChildren<Renderer>(true))renderer.forceRenderingOff=true;
                Vector3 trackedPosition=c.rig.TrackedCamera.transform.localPosition;Quaternion trackedRotation=c.rig.TrackedCamera.transform.localRotation;
                void Pair(string id)
                {
                    foreach(var label in labels)label.Display.ForceMeshUpdate(true,true);
                    File.WriteAllLines(Path.Combine(output,id+"_text.txt"),labels.Where(l=>l.gameObject.activeInHierarchy).Select(l=>l.Display.text));
                    Quaternion facing=c.rig.PilotSeatAnchor.rotation;
                    for(int eye=0;eye<2;eye++)Capture(c,report,output,1280,960,new ShotDefinition(id+(eye==0?"_left":"_right"),id,"menu_repair",c.rig.PilotSeatAnchor.position+facing*Vector3.right*(eye==0?-.032f:.032f),facing,72,ViewProfile.Cockpit));
                }
                foreach(bool sgs in new[]{false,true})
                {
                    typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.None);
                    mode.ReturnToMainMenu();
                    if(!mode.SelectAircraft(sgs))throw new InvalidOperationException(mode.LastError);
                    if(sgs){InvokePrivate(mode.AircraftPresentation.instruments,"Awake");InvokePrivate(mode.AircraftPresentation.instruments,"LateUpdate");}
                    foreach(float text in new[]{1f,1.2f})
                    {
                        PlayerPrefs.SetFloat("QuestFlight.TextScale",text);
                        foreach(var item in fontSizes)item.Key.Display.fontSize=item.Value*text;
                        foreach(var page in System.Environment.GetEnvironmentVariable("QF_MENU_QA_FOCUS")=="workflow"?new[]{ProductAppNavigation.Page.Handover,ProductAppNavigation.Page.SeatExit,ProductAppNavigation.Page.SettingAdjustment,ProductAppNavigation.Page.Soaring,ProductAppNavigation.Page.App,ProductAppNavigation.Page.Checklist,ProductAppNavigation.Page.Map}:System.Environment.GetEnvironmentVariable("QF_MENU_QA_FOCUS")=="pause"?new ProductAppNavigation.Page[0]:System.Environment.GetEnvironmentVariable("QF_MENU_QA_FOCUS")=="bindings"?new[]{ProductAppNavigation.Page.Bindings}:System.Environment.GetEnvironmentVariable("QF_MENU_QA_FOCUS")=="routes"?new[]{ProductAppNavigation.Page.Help,ProductAppNavigation.Page.Activities,ProductAppNavigation.Page.Soaring,ProductAppNavigation.Page.Briefing,ProductAppNavigation.Page.ConfirmAction,ProductAppNavigation.Page.Graphics,ProductAppNavigation.Page.Map,ProductAppNavigation.Page.Kneeboard,ProductAppNavigation.Page.InputPreview,ProductAppNavigation.Page.ControlDiagram,ProductAppNavigation.Page.Calibration,ProductAppNavigation.Page.Orientation,ProductAppNavigation.Page.Explanation,ProductAppNavigation.Page.Tools,ProductAppNavigation.Page.Guest,ProductAppNavigation.Page.Summary,ProductAppNavigation.Page.ConfirmMain}:new[]{ProductAppNavigation.Page.Home,sgs?ProductAppNavigation.Page.GliderSetup:ProductAppNavigation.Page.C172Setup,ProductAppNavigation.Page.Aircraft,ProductAppNavigation.Page.Settings,ProductAppNavigation.Page.Controls,ProductAppNavigation.Page.Bindings,ProductAppNavigation.Page.InstrumentHelp,ProductAppNavigation.Page.QuickStart,ProductAppNavigation.Page.Checklist,ProductAppNavigation.Page.Comfort,ProductAppNavigation.Page.Captions,ProductAppNavigation.Page.History,ProductAppNavigation.Page.Report})
                        {
                            typeof(ProductAppNavigation).GetField("adjustmentAction",flags).SetValue(nav,23);
                            typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,page);nav.RefreshMenu();
                            Pair((sgs?"sgs":"c172")+"_"+page+"_"+Mathf.RoundToInt(text*100));
                        }
                        typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.None);nav.RefreshMenu();
                        mode.SelectFreeFlight();mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();Time.timeScale=0;
                        typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.App);nav.RefreshMenu();Pair((sgs?"sgs":"c172")+"_pause_"+Mathf.RoundToInt(text*100));
                        mode.ReturnToMainMenu();
                        if(System.Environment.GetEnvironmentVariable("QF_MENU_QA_FOCUS")=="routes")
                        {
                            typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.None);nav.RefreshMenu();
                            mode.SelectTraining();mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();mode.AuthoredTraining.OpenLessons();nav.RefreshMenu();
                            Pair((sgs?"sgs":"c172")+"_lesson_list_"+Mathf.RoundToInt(text*100));
                            typeof(ProductAppNavigation).GetField("lessonPage",flags).SetValue(nav,1);nav.RefreshMenu();Pair((sgs?"sgs":"c172")+"_lesson_list_page2_"+Mathf.RoundToInt(text*100));
                            typeof(ProductAppNavigation).GetField("lessonPage",flags).SetValue(nav,0);mode.AuthoredTraining.SelectLesson(0);nav.RefreshMenu();Pair((sgs?"sgs":"c172")+"_lesson_briefing_"+Mathf.RoundToInt(text*100));
                            mode.ReturnToMainMenu();
                        }
                    }
                }
                if(trackedPosition!=c.rig.TrackedCamera.transform.localPosition||trackedRotation!=c.rig.TrackedCamera.transform.localRotation)throw new InvalidOperationException("Tracked camera pose changed during presentation QA.");
                report.requiredShotCount=report.shots.Count;report.passed=report.shots.Count>0&&report.errors.Count==0;report.classification="MENU_CAPTURES_REQUIRE_INSPECTION_BUDGET_SEPARATE";
            }
            catch(Exception e){report.errors.Add(e.ToString());report.classification="FAIL_MENU_CAPTURE";Debug.LogException(e);}
            finally{Time.timeScale=clock;if(hadText)PlayerPrefs.SetFloat("QuestFlight.TextScale",oldText);else PlayerPrefs.DeleteKey("QuestFlight.TextScale");}
            WriteReportFiles(report,output);
            if(!report.passed)throw new InvalidOperationException("Menu capture failed; inspect the report.");
        }
    }
}
