using System;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlightLab.Environment;
using UnityEngine;
using UnityEditor;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void RunBirdVisualQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_BIRD_EVIDENCE");
            output=Path.Combine(output,"visual_qa");Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,scenePath=ProductionScenePath,
                outputDirectory=output,renderWidth=1440,renderHeight=1440,humanHeadsetAcceptance="PENDING"};
            try
            {
                var c=LoadContext(1440,1440,report);
                var d=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<HabitatBirdDirector>(true)).Single();
                d.modes.AuthoredModePanelRoot.SetActive(false);
                d.ResetSession();
                foreach(var b in d.birds){b.animator.enabled=true;b.animator.Rebind();b.animator.Play("SoarIdle",0,0);b.animator.Update(0);b.SetLod(-1);}
                // Model admission views use actual Unity skinning/materials and measured real scale.
                foreach(int index in new[]{0,1,4,6})
                {
                    var b=d.birds[index];b.SetLod(0);b.transform.SetPositionAndRotation(new Vector3(0,700,0),Quaternion.identity);
                    b.animator.enabled=true;b.animator.Play("SoarIdle",0,0);b.animator.Update(0);
                    Debug.Log("[BirdVisual] "+b.birdId+" world bounds="+b.lods[0].bounds);
                    Vector3 eye=b.transform.position+new Vector3(2,1.5f,3);
                    Capture(c,report,output,1440,1440,new ShotDefinition(b.birdId+"_asset",b.birdId,"wildlife",eye,LookAt(eye,b.transform.position),42,ViewProfile.Environment));
                    b.SetLod(-1);
                }
                // Inspect the retained native encounter from an ordinary seated forward view.
                string csv=Path.Combine(Directory.GetParent(output).FullName,"birds_default.csv");
                var row=File.ReadAllLines(csv).First(s=>s.StartsWith("123,")).Split(',');
                float F(int i)=>float.Parse(row[i],System.Globalization.CultureInfo.InvariantCulture);
                Vector3 aircraft=new Vector3(F(1),F(2),F(3)),eagle=new Vector3(F(4),F(5),F(6));
                c.rig.AircraftSimulationRoot.SetPositionAndRotation(aircraft,Quaternion.Euler(-9,12,0));
                d.Eagle.transform.position=eagle;d.Eagle.SetLod(0);d.Eagle.animator.enabled=true;d.Eagle.animator.Play("SoarIdle",0,0);d.Eagle.animator.Update(0);
                var facing=c.rig.AircraftSimulationRoot.rotation;
                for(int eye=0;eye<2;eye++)Capture(c,report,output,1440,1440,new ShotDefinition("eagle_pilot_"+eye,"Eagle ordinary pilot view","wildlife",
                    c.rig.PilotSeatAnchor.position+facing*Vector3.right*(eye==0?-.032f:.032f),facing,85,ViewProfile.Cockpit));
                report.passed=report.shots.Count==6&&report.shots.All(s=>s.passed);report.classification=report.passed?"PASS_BIRD_VISUAL_MACHINE_QA":"REVIEW_BIRD_VISUAL_MACHINE_QA";
            }
            catch(Exception e){report.errors.Add(e.ToString());report.passed=false;report.classification="FAIL_BIRD_VISUAL_QA";Debug.LogException(e);}
            WriteReportFiles(report,output);Debug.Log("[BirdVisual] "+report.classification);EditorApplication.Exit(report.passed?0:1);
        }
    }
}
