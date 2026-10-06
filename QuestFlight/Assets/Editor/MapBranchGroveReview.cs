using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using QuestFlightLab.Environment;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        [Serializable] private sealed class BranchView {public string id;public int selected,eligible,draws,triangles,pages,near,middle,far;public float farthest;public double selectionMs;public Vector3 pilotPosition;}
        [Serializable] private sealed class BranchReview {public string status="AUTHORED_PILOT_SEAT_EDITOR_STEREO_NOT_QUEST_TIMING";public List<BranchView> views=new List<BranchView>();}
        public static void RunMapBranchGroveReview()
        {
            string root=BranchRoot;Directory.CreateDirectory(Path.Combine(root,"screenshots"));var evidence=new BranchReview();
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,scenePath=ProductionScenePath,outputDirectory=root,renderWidth=1280,renderHeight=720,humanHeadsetAcceptance="NOT_RUN",flightPhaseImageSemantics="Authored renderer, shared runtime selection/draw code, pilot seat with cockpit. Offset Editor stereo is not tracked headset validation."};
            var context=LoadContext(1280,720,report);var grove=context.environment.GetComponentInChildren<MapBranchGroveRenderer>(true);
            if(grove==null||!grove.enabled)throw new InvalidOperationException("Map grove absent/disabled in product scene");
            Camera.CameraCallback draw=c=>{if(c==context.camera)grove.DrawForVisualQa(c);};
            Camera.onPreCull+=draw;QuestFlight.Rendering.ProductRenderSubmission.Draw+=draw;string previous=System.Environment.GetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW");System.Environment.SetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW","1");
            string previousBranch=System.Environment.GetEnvironmentVariable("QFL_MAP_BRANCH_REVIEW_ACTIVE");System.Environment.SetEnvironmentVariable("QFL_MAP_BRANCH_REVIEW_ACTIVE","1");
            try
            {
                bool bankReview=System.Environment.GetEnvironmentVariable("QFL_BANK_BRANCH_REVIEW")=="1";
                for(int site=0;site<(bankReview?1:grove.observationStarts.Length);site++)
                foreach(int roll in bankReview?new[]{-30,0,30}:new[]{0})
                {
                    var spawn=grove.observationStarts[site].spawnTransform;
                    Quaternion aircraftRotation=spawn.rotation*Quaternion.Euler(0,0,roll);
                    SetAircraftPose(context,spawn.position,aircraftRotation);
                    Vector3 seat=context.rig.PilotSeatAnchor.position;
                    foreach(int yaw in bankReview?new[]{0,-35,35}:new[]{0,-35})foreach(int eye in new[]{-1,1})
                    {
                        Quaternion direction=aircraftRotation*Quaternion.Euler(12,yaw,0);Vector3 eyePosition=seat+aircraftRotation*Vector3.right*(eye*.032f);
                        // Only the offline QA camera is positioned. The tracked
                        // product camera remains owned by OpenXR.
                        context.camera.transform.SetPositionAndRotation(eyePosition,direction);context.camera.fieldOfView=72;
                        grove.PrepareForVisualQa(context.camera);
                        string id=$"pilot_{site:00}_{yaw:00}_"+(eye<0?"left":"right");
                        if(bankReview)id=$"bank_{roll}_"+id;
                        Capture(context,report,root,1280,720,new ShotDefinition(id,"Map canopy from pilot seat","pilot_map",eyePosition,direction,72,ViewProfile.Cockpit));
                        evidence.views.Add(new BranchView{id=id,selected=grove.SelectedCells,eligible=grove.EligibleCells,draws=grove.LastDraws,triangles=grove.SelectedTriangles,near=grove.NearCells,middle=grove.MiddleCells,far=grove.FarCells,farthest=grove.FarthestSelectedMeters,selectionMs=grove.SelectionMilliseconds,pages=grove.ResidentPages,pilotPosition=seat});
                        if(!bankReview&&site==0&&yaw==-35)
                        {
                            grove.branchMaterial.SetFloat("_Diagnostic",1);
                            Capture(context,report,root,1280,720,new ShotDefinition(id+"_visibility","Actual grove draw highlighted","pilot_map_diagnostic",eyePosition,direction,72,ViewProfile.Cockpit));
                            grove.branchMaterial.SetFloat("_Diagnostic",0);
                            grove.DiagnosticTierForVisualQa=2;
                            Capture(context,report,root,1280,720,new ShotDefinition(id+"_far_visibility","Only actual foliage beyond 1.25 km highlighted","pilot_map_far_diagnostic",eyePosition,direction,72,ViewProfile.Cockpit));
                            grove.DiagnosticTierForVisualQa=-1;
                        }
                    }
                }
                File.WriteAllText(Path.Combine(root,"pilot_visibility_evidence.json"),JsonUtility.ToJson(evidence,true));
                WriteReportFiles(report,root);
                foreach(var shot in report.shots)if(!string.IsNullOrEmpty(shot.error)||!File.Exists(shot.screenshotPath))throw new InvalidOperationException("Pilot capture failed: "+shot.id+" "+shot.error);
                Debug.Log("[MapBranchGroves] Pilot view review complete: "+evidence.views.Count);
            }
            finally
            {
                grove.branchMaterial.SetFloat("_Diagnostic",0);Camera.onPreCull-=draw;QuestFlight.Rendering.ProductRenderSubmission.Draw-=draw;grove.ClearForVisualQa();
                System.Environment.SetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW",previous);
                System.Environment.SetEnvironmentVariable("QFL_MAP_BRANCH_REVIEW_ACTIVE",previousBranch);
            }
        }
    }
}
