using System;
using System.IO;
using System.Linq;
using UnityEngine;
using QuestFlight.Editor;
namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void RunCockpitFlickerReview()
        {
            string root=System.Environment.GetEnvironmentVariable("QFL_COCKPIT_FLICKER_ROOT");
            if(string.IsNullOrEmpty(root))throw new InvalidOperationException("Evidence directory required");
            Directory.CreateDirectory(Path.Combine(root,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                scenePath=ProductionScenePath,outputDirectory=root,renderWidth=1440,renderHeight=1440,humanHeadsetAcceptance="NOT_RUN"};
            var c=LoadContext(1440,1440,report);
            CockpitDepthOwnershipEditorContract.ValidateProductPrefab();
            var renderers=c.rig.AircraftVisualRoot.GetComponentsInChildren<Renderer>(true);
            var screen=renderers.Single(r=>r.name==CockpitCompletionV3Authoring.AvionicsObjectName);
            var hardware=renderers.Single(r=>r.name==CockpitCompletionV3Authoring.AvionicsHardwareObjectName);
            var hm=hardware.GetComponent<MeshFilter>().sharedMesh;
            File.WriteAllText(Path.Combine(root,"hardware.json"),JsonUtility.ToJson(new FlickerMesh{vertices=hm.vertices,uv=hm.uv,triangles=hm.triangles}));
            var original=renderers.Single(r=>r.name==CockpitDepthOwnershipEditorContract.SourceDisplayObjectName).GetComponent<MeshFilter>().sharedMesh;
            File.WriteAllText(Path.Combine(root,"source.json"),JsonUtility.ToJson(new FlickerMesh{vertices=original.vertices,uv=original.uv,triangles=original.triangles}));
            File.WriteAllLines(Path.Combine(root,"renderers.txt"),renderers.Select(r=>r.name+" enabled="+r.enabled+" materials="+string.Join(",",r.sharedMaterials.Select(m=>m==null?"NULL":m.name+":"+m.shader.name))));
            SetAircraftPose(c,new Vector3(10000,500,10000),Quaternion.Euler(8,35,12));
            foreach(string mode in new[]{"baseline","hardware_only","hardware_and_screens","screens_only"})
            {
                foreach (var renderer in renderers)
                    renderer.forceRenderingOff = mode switch
                    {
                        "hardware_only" => renderer != hardware,
                        "hardware_and_screens" => renderer != hardware && renderer != screen,
                        "screens_only" => renderer != screen,
                        _ => false
                    };
                foreach(float offset in new[]{-.02f,0f,.02f})
                {
                    Vector3 eye=c.rig.PilotSeatAnchor.position+c.rig.transform.right*offset;
                    Quaternion direction=Quaternion.LookRotation(screen.bounds.center-eye,c.rig.transform.up);
                    Capture(c,report,root,1440,1440,new ShotDefinition(mode+"_"+offset.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),"Center screen isolation","cockpit_isolation",eye,direction,32,ViewProfile.Cockpit));
                }
            }
            foreach(var r in renderers)r.forceRenderingOff=false;
            WriteReportFiles(report,root);
        }
        [Serializable] private class FlickerMesh { public Vector3[] vertices; public Vector2[] uv; public int[] triangles; }
    }
}
