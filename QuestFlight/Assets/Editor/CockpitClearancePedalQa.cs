using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlight.Editor;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void RunCockpitFinalQa()
        {
            RunCockpitClearancePedalQa();
            RunProductionVisualQa();
        }
        [Serializable] private class PedalDump
        { public Vector3[] vertices; public int[] triangles; public BoneWeight[] weights; public Vector3[] pivots; }
        public static void RunCockpitClearancePedalQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_COCKPIT_FIX_QA");
            Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport { scenePath=ProductionScenePath,outputDirectory=output,
                generatedUtc=DateTime.UtcNow.ToString("O"),renderWidth=1440,renderHeight=1080,humanHeadsetAcceptance="PENDING" };
            float before=Time.timeScale;
            try
            {
                var c=LoadContext(1440,1080,report);
                // Edit-mode controller rays have no tracking update and remain at
                // the eye origin. Suppress only these development-fixture artifacts.
                foreach(var renderer in c.rig.GetComponentsInChildren<Renderer>(true))
                    if(renderer.sharedMaterials.Any(m=>m!=null&&m.shader!=null&&m.shader.name.StartsWith("Interaction/")))renderer.gameObject.SetActive(false);
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                var nav=mode.AuthoredNavigation;
                const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
                void Refresh()=>typeof(ProductAppNavigation).GetMethod("Refresh",flags).Invoke(nav,null);
                typeof(ProductFlightModeCoordinator).GetProperty("State").SetValue(mode,ProductFlightModeCoordinator.FlightModeState.Training);
                typeof(ProductTrainingController).GetProperty("State").SetValue(mode.AuthoredTraining,TrainingSessionState.Menu);
                Refresh();
                Transform panel=((GameObject)new SerializedObject(nav).FindProperty("panel").objectReferenceValue).transform;
                var log=new System.Text.StringBuilder();
                log.AppendLine("Eye aircraft-local "+c.rig.transform.InverseTransformPoint(c.rig.PilotSeatAnchor.position).ToString("F5"));
                log.AppendLine("Panel parent "+panel.parent.name+" aircraft-local "+c.rig.transform.InverseTransformPoint(panel.parent.position).ToString("F5"));
                var ray=new Ray(c.rig.PilotSeatAnchor.position,(c.rig.AircraftSimulationRoot.forward+c.rig.AircraftSimulationRoot.up*.08f).normalized);
                var intersect=typeof(HandleUtility).GetMethod("IntersectRayMesh",BindingFlags.Static|BindingFlags.NonPublic);
                foreach(var filter in c.rig.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer=filter.GetComponent<Renderer>();if(renderer==null||!renderer.enabled||!renderer.gameObject.activeInHierarchy||filter.sharedMesh==null)continue;
                    object[] args={ray,filter.sharedMesh,filter.transform.localToWorldMatrix,null};
                    if((bool)intersect.Invoke(null,args))
                    {var hit=(RaycastHit)args[3];log.AppendLine($"UI ray {hit.distance:F4} {filter.name} materials={string.Join(",",renderer.sharedMaterials.Select(m=>m==null?"null":m.name+"/"+m.shader.name+"/"+m.renderQueue))}");}
                }
                foreach(var r in panel.GetComponentsInChildren<Renderer>(true).Where(r=>r.gameObject.activeInHierarchy))log.AppendLine(r.name+" "+r.bounds);
                void Pair(string name,float pitch=0,float lean=0)
                { var facing=c.rig.AircraftSimulationRoot.rotation*Quaternion.Euler(pitch,0,0);
                  for(int side=0;side<2;side++) Capture(c,report,output,1440,1080,new ShotDefinition(name+(side==0?"_left":"_right"),name,"cockpit",
                    c.rig.PilotSeatAnchor.position+c.rig.AircraftSimulationRoot.forward*lean+facing*Vector3.right*(side==0?-.032f:.032f),facing,72,ViewProfile.Cockpit)); }
                Pair("lessons");Pair("lessons_lean",12,.12f);
                foreach(var page in new[]{ProductAppNavigation.Page.App,ProductAppNavigation.Page.Settings,ProductAppNavigation.Page.Calibration,ProductAppNavigation.Page.QuickStart})
                { typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,page);Refresh();Pair(page.ToString()); }
                typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.None);
                for(int lesson=0;lesson<mode.AuthoredTraining.Lessons.Length;lesson++)
                {
                    typeof(ProductTrainingController).GetProperty("SelectedLesson").SetValue(mode.AuthoredTraining,lesson);
                    typeof(ProductTrainingController).GetProperty("State").SetValue(mode.AuthoredTraining,TrainingSessionState.Briefing);
                    Refresh();Pair("briefing_"+lesson,-15);
                }
                typeof(ProductTrainingController).GetProperty("State").SetValue(mode.AuthoredTraining,TrainingSessionState.Debrief);
                typeof(ProductTrainingController).GetProperty("LastDebrief").SetValue(mode.AuthoredTraining,
                    "Guided practice complete\nYou stopped in the marked area without entering the runway.\n\nNext practice: Close the throttle earlier and brake gently. Stop before the runway, not on it.\n\nReflect: Which helped you stop accurately: reducing power early or braking late?");
                Refresh();Pair("debrief",-15);
                panel.gameObject.SetActive(false);
                var instruction=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductUiInstruction>(true)).Single();
                instruction.gameObject.SetActive(true);
                string longest=mode.AuthoredTraining.Lessons.SelectMany(l=>l.stages).OrderByDescending(s=>s.instruction.Length).First().instruction;
                instruction.Present("Your controls","Check the instruments",longest,1,7,true,false);Pair("instruction_long",8);
                instruction.gameObject.SetActive(false);
                var pedal=c.rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.name==CockpitPedalCompletionAuthoring.RootName);
                var mesh=pedal.sharedMesh;
                File.WriteAllText(Path.Combine(output,"pedal_mesh.json"),JsonUtility.ToJson(new PedalDump{vertices=mesh.vertices,triangles=mesh.triangles,weights=mesh.boneWeights,pivots=pedal.bones.Select(b=>pedal.transform.InverseTransformPoint(b.position)).ToArray()}));
                var author=typeof(CockpitPedalCompletionAuthoring);
                var components=(IEnumerable)author.GetMethod("BuildTriangleComponents",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{mesh.vertices,mesh.triangles});
                int n=0;foreach(var item in components)
                {var type=item.GetType();var bounds=(Bounds)type.GetField("bounds").GetValue(item);var tris=(System.Collections.Generic.List<int>)type.GetField("triangleStarts").GetValue(item);
                 var bones=tris.SelectMany(i=>new[]{mesh.boneWeights[mesh.triangles[i]].boneIndex0,mesh.boneWeights[mesh.triangles[i+1]].boneIndex0,mesh.boneWeights[mesh.triangles[i+2]].boneIndex0}).Distinct();
                 log.AppendLine($"Component {n++} tri={tris.Count} bones={string.Join(",",bones)} min={bounds.min.ToString("F6")} max={bounds.max.ToString("F6")}");}
                log.AppendLine("Pedal local eye="+pedal.transform.InverseTransformPoint(c.rig.PilotSeatAnchor.position).ToString("F6"));
                log.AppendLine("Pedal lossyScale="+pedal.transform.lossyScale.ToString("F5"));
                File.WriteAllText(Path.Combine(output,"geometry.txt"),log.ToString());
                Pair("pedals_neutral",48);
                // Isolated diagnostic of the existing mesh: no product asset/layer writes.
                pedal.gameObject.layer=28;c.camera.cullingMask=1<<29;
                var witness=new GameObject("PedalPoseWitness");witness.layer=29;
                witness.transform.SetParent(pedal.transform,false);
                var witnessMesh=witness.AddComponent<MeshFilter>();
                witness.AddComponent<MeshRenderer>().sharedMaterials=pedal.sharedMaterials;
                Vector3 eye=pedal.transform.TransformPoint(new Vector3(0,-.035f,.025f));
                Vector3 target=pedal.transform.TransformPoint(new Vector3(0,.039f,-.067f));
                foreach(var pose in new[]{"neutral","right_rudder","left_brake","right_rudder_brakes"})
                {
                    for(int b=1;b<pedal.bones.Length;b++)pedal.bones[b].localRotation=Quaternion.identity;
                    if(pose.Contains("rudder"))foreach(int b in new[]{1,3,5,7})pedal.bones[b].localRotation=Quaternion.AngleAxis((b==1||b==5)?-12:12,Vector3.right);
                    if(pose.Contains("brake"))foreach(int b in (pose=="left_brake"?new[]{2,6}:new[]{2,4,6,8}))pedal.bones[b].localRotation=Quaternion.AngleAxis(10,Vector3.right);
                    var posed=new Mesh();pedal.BakeMesh(posed);witnessMesh.sharedMesh=posed;
                    File.WriteAllText(Path.Combine(output,"pedal_pose_"+pose+".json"),JsonUtility.ToJson(new PedalDump{vertices=posed.vertices,triangles=posed.triangles}));
                    Capture(c,report,output,1440,1080,new ShotDefinition("pedal_isolated_"+pose,pose,"pedals",eye,Quaternion.LookRotation(target-eye,-pedal.transform.up),48,ViewProfile.Cockpit));
                    UnityEngine.Object.DestroyImmediate(posed);
                }
                UnityEngine.Object.DestroyImmediate(witness);
                report.classification="COCKPIT_FIX_DIAGNOSTIC";
            }
            catch(Exception e){report.errors.Add(e.ToString());Debug.LogException(e);}
            finally{Time.timeScale=before;}
            WriteReportFiles(report,output);
        }
    }
}
