using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using QuestFlight.Product;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        [Serializable] private sealed class GuestFinishQaContracts
        {
            public bool passed;
            public string evidenceKind="Editor-authored presentation pairs; not a live XR or Quest performance acceptance test.";
            public List<string> checks=new List<string>();
        }

        // Twelve targeted images only. The tracked Main Camera is never moved or rendered.
        public static void RunGuestFinishVisualQa()
        {
            const int width=1280,height=960;
            string output=System.Environment.GetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR");
            if(string.IsNullOrEmpty(output))throw new InvalidOperationException("QFL_PRODUCTION_VISUAL_QA_DIR is required.");
            Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport { generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                scenePath=ProductionScenePath,outputDirectory=output,renderWidth=width,renderHeight=height,requiredShotCount=12,
                humanHeadsetAcceptance="PENDING",flightPhaseImageSemantics="Focused guest/settings presentation from the authored pilot seat using a separate QA camera." };
            var contracts=new GuestFinishQaContracts();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            // Only this preference is changed by the fixture. Preserve missing vs present exactly.
            const string largeKey="QuestFlight.LargeInstructionText";
            bool largeExisted=PlayerPrefs.HasKey(largeKey);
            int largeBefore=PlayerPrefs.GetInt(largeKey,0);
            float timeScale=Time.timeScale;
            void Check(bool value,string message)
            { if(!value)throw new InvalidOperationException(message);contracts.checks.Add(message); }
            void Set(object target,string name,object value)=>target.GetType().GetField(name,flags).SetValue(target,value);
            void Call(object target,string name)=>target.GetType().GetMethod(name,flags).Invoke(target,null);
            void Page(ProductAppNavigation nav,ProductAppNavigation.Page value)
            { Set(nav,"<CurrentPage>k__BackingField",value);Call(nav,"Refresh"); }
            try
            {
                var c=LoadContext(width,height,report);
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                var nav=mode.AuthoredNavigation;
                Check(nav!=null && nav.HasAuthoredReferences,"One authored navigation with complete references");
                Check(c.camera!=c.rig.TrackedCamera,"Presentation uses a separate QA camera");
                Vector3 trackedPosition=c.rig.TrackedCamera.transform.position;
                Quaternion trackedRotation=c.rig.TrackedCamera.transform.rotation;
                var navData=new SerializedObject(nav);
                var panel=navData.FindProperty("panel").objectReferenceValue as GameObject;
                var buttons=panel.GetComponentsInChildren<ProductActionButton>(true);
                var hidden=(Renderer[])typeof(ProductFlightModeMenuView).GetField("aircraftPresentationRenderers",flags).GetValue(mode.AuthoredModeMenuView);
                Check(!panel.GetComponentsInChildren<Renderer>(true).Any(r=>hidden.Contains(r)),"Startup suppression excludes the navigation overlay");
                var instruction=navData.FindProperty("instructionPanels").GetArrayElementAtIndex(0).objectReferenceValue as ProductUiInstruction;
                Check(instruction!=null,"Instruction size targets the existing shared narration panel");
                Set(nav,"instructionScales",new[]{instruction.transform.localScale});
                var facing=c.rig.AircraftSimulationRoot.rotation;
                void Pair(string id,float pitch=0)
                {
                    foreach(var root in c.scene.GetRootGameObjects())
                        foreach(var text in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))text.ForceMeshUpdate(true,true);
                    File.WriteAllLines(Path.Combine(output,id+"_text_state.txt"),c.scene.GetRootGameObjects()
                        .SelectMany(r=>r.GetComponentsInChildren<TMPro.TextMeshPro>(true)).Where(t=>t.gameObject.activeInHierarchy)
                        .Select(t=>$"{t.transform.parent.name}/{t.name}: size={t.fontSize} enabled={t.renderer.enabled} hidden={t.renderer.forceRenderingOff} text={t.text}"));
                    Quaternion orientation=facing*Quaternion.Euler(pitch,0,0);
                    for(int side=0;side<2;side++)Capture(c,report,output,width,height,new ShotDefinition(
                        id+(side==0?"_left":"_right"),id,"guest_finish",c.rig.PilotSeatAnchor.position+orientation*Vector3.right*(side==0?-.032f:.032f),
                        orientation,72,ViewProfile.Cockpit));
                }
                Set(mode,"<State>k__BackingField",ProductFlightModeCoordinator.FlightModeState.Selection);
                mode.AuthoredModeMenuView.ShowSelectionForVisualQa();
                Page(nav,ProductAppNavigation.Page.App);
                Check((navData.FindProperty("heading").objectReferenceValue as TextMesh).text=="Options and help","Main-menu options do not claim a flight is paused");
                Pair("guest_options_main");

                Page(nav,ProductAppNavigation.Page.Settings);
                Check(buttons.Count(b=>b.gameObject.activeSelf)==8,"All eight Settings actions are visible");
                Check(panel.activeSelf && !mode.AuthoredModeMenuView.SelectionContentRoot.activeSelf,"Settings replaces selection without hiding its own panel");
                Pair("guest_settings_main");

                nav.OpenQuickStart();Pair("guest_quick_start");
                Set(nav,"guideStep",2);Call(nav,"Refresh");
                nav.OpenCalibration();
                Check(mode.AuthoredSeatCalibration.IsOpen,"Guide opens the existing seat calibration");
                nav.Back();
                Check(!mode.AuthoredSeatCalibration.IsOpen && nav.CurrentPage==ProductAppNavigation.Page.QuickStart && (int)typeof(ProductAppNavigation).GetField("guideStep",flags).GetValue(nav)==2,
                    "Back from calibration returns to the guide's seat step without saving");
                Pair("guest_seat_return");

                Page(nav,ProductAppNavigation.Page.Guest);
                Check((navData.FindProperty("detail").objectReferenceValue as TextMesh).text.Contains("owner"),"Guest confirmation explains owner progress isolation");
                Pair("guest_confirmation");

                Page(nav,ProductAppNavigation.Page.None);
                // Presentation-only transition. No flight is started or controller/physics authority bypassed.
                mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                instruction.gameObject.SetActive(true);
                Vector3 normalScale=instruction.transform.localScale;
                PlayerPrefs.SetInt(largeKey,1);
                Call(nav,"ApplyInstructionSize");
                Check((instruction.transform.localScale-normalScale*1.15f).sqrMagnitude<.000001f,"Large instruction applies exactly once at 115 percent scale");
                var first=mode.AuthoredTraining.Lessons[0].stages[0];
                instruction.Present("Your controls",first.title,first.instruction+"\n\nNow: Left checked. Turn the yoke right.",0,7,true,false);
                Pair("guest_instruction_large",8);
                Check((trackedPosition-c.rig.TrackedCamera.transform.position).sqrMagnitude<.000001f && Quaternion.Angle(trackedRotation,c.rig.TrackedCamera.transform.rotation)<.001f,
                    "Tracked Main Camera pose unchanged throughout the fixture");
                Check(report.shots.Count==12,"Exactly twelve focused stereo-eye images captured");
                contracts.passed=true;
                report.classification="GUEST_FINISH_EDITOR_CAPTURES_REVIEW_REQUIRED";
                report.passed=report.shots.All(s=>s.passed);
            }
            catch(Exception e)
            { report.errors.Add(e.ToString());report.classification="FAIL_GUEST_FINISH_EDITOR_CAPTURE";Debug.LogException(e); }
            finally
            {
                if(largeExisted)PlayerPrefs.SetInt(largeKey,largeBefore);else PlayerPrefs.DeleteKey(largeKey);
                PlayerPrefs.Save();Time.timeScale=timeScale;
            }
            File.WriteAllText(Path.Combine(output,"guest_finish_contracts.json"),JsonUtility.ToJson(contracts,true));
            WriteReportFiles(report,output);
        }
    }
}
