using System;
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
        public static void RunLocalRecoveryVisualQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_RECOVERY_EVIDENCE");
            output=Path.Combine(output,"visual_qa");Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),scenePath=ProductionScenePath,outputDirectory=output,renderWidth=1440,renderHeight=1080,humanHeadsetAcceptance="PENDING"};
            try
            {
                var c=LoadContext(1440,1080,report);
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                typeof(ProductFlightModeCoordinator).GetProperty("State").SetValue(mode,ProductFlightModeCoordinator.FlightModeState.FreeFlight);
                var nav=mode.AuthoredNavigation;
                var recovery=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductMapBoundaryRecovery>(true)).Single();
                nav.ShowRecoveryAssistance(recovery,"No safe local flight corridor within the configured recovery distance. Restart at runway or return to the menu.");
                foreach(string page in new[]{"assistance","explicit_restart"})
                {
                    if(page=="explicit_restart")typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.ConfirmMain);
                    typeof(ProductAppNavigation).GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(nav,null);
                    var panel=(GameObject)typeof(ProductAppNavigation).GetField("panel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(nav);
                    var body=(Transform)typeof(ProductAppNavigation).GetField("backplate",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(nav);
                    Debug.Log("[RecoveryQA] panel active="+panel.activeInHierarchy+" pose="+panel.transform.position+" backplate="+body.position+" eye="+c.rig.PilotSeatAnchor.position+" mask="+c.camera.cullingMask);
                    foreach(var rr in panel.GetComponentsInChildren<Renderer>(true).Take(5))Debug.Log("[RecoveryQA] renderer="+rr.name+" enabled="+rr.enabled+" active="+rr.gameObject.activeInHierarchy+" layer="+rr.gameObject.layer+" bounds="+rr.bounds);
                    foreach(var root in c.scene.GetRootGameObjects())foreach(var text in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))text.ForceMeshUpdate(true,true);
                    foreach(var style in panel.GetComponentsInChildren<ProductUiSurface>(true))style.RefreshStyle();
                    for(int eye=0;eye<2;eye++)
                    {
                        var eyePosition=c.rig.PilotSeatAnchor.position+c.rig.AircraftSimulationRoot.right*(eye==0?-.032f:.032f);
                        c.camera.transform.SetPositionAndRotation(eyePosition,c.rig.AircraftSimulationRoot.rotation);
                        c.camera.fieldOfView=72;
                        c.camera.useOcclusionCulling=false;
                        // Warm the editor render state after moving the capture camera.
                        var warm=Render(c.camera,1440,1080,out _);UnityEngine.Object.DestroyImmediate(warm);
                        Debug.Log("[RecoveryQA] surface viewport="+c.camera.WorldToViewportPoint(body.position)+" surface="+body.GetComponent<Renderer>().enabled+" scale="+body.lossyScale);
                        var frame=Render(c.camera,1440,1080,out var stats);
                        File.WriteAllBytes(Path.Combine(output,"screenshots",page+(eye==0?"_left":"_right")+".png"),frame.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(frame);
                    }
                }
                report.classification="FOCUSED_STANDARD_PIPELINE_EDITOR_REVIEW";report.passed=report.shots.All(s=>s.passed);
                WriteReportFiles(report,output);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);report.errors.Add(e.ToString());WriteReportFiles(report,output);EditorApplication.Exit(1);}
        }
    }
}
