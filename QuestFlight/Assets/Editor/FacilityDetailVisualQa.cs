using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void RunFacilityDetailQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_FACILITY_QA");
            Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            System.Environment.SetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW","1");
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                scenePath=ProductionScenePath,outputDirectory=output,renderWidth=1440,renderHeight=1080,humanHeadsetAcceptance="PENDING"};
            try
            {
                var c=LoadContext(1440,1080,report);
                Vector3 target=new Vector3(3480,-28,1030);
                foreach(int height in new[]{130,350})
                {
                    Vector3 at=target+new Vector3(60,height,-height*1.15f);
                    Capture(c,report,output,1440,1080,new ShotDefinition("wrrf_"+height,"Water recovery tanks","facility",at,LookAt(at,target),58,ViewProfile.Environment));
                }
                CaptureStereoEnvironment(c,report,output,1440,1080,"wrrf_stereo","WRRF corrected tanks stereo",
                    target+new Vector3(60,130,-149.5f),target,58);
                report.passed=report.shots.All(s=>s.passed);report.classification=report.passed?"PASS_FACILITY_MACHINE_QA":"REVIEW_FACILITY_QA";
            }
            catch(Exception e){report.errors.Add(e.ToString());Debug.LogException(e);}
            WriteReportFiles(report,output);EditorApplication.Exit(report.passed?0:1);
        }
    }
}
