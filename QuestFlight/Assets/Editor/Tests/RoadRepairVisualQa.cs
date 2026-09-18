using System;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestFlightLab.Editor.Tests
{
    // Focused editor evidence; this does not measure Quest frame time or OpenXR stereo.
    public static class RoadRepairVisualQa
    {
        public static void RunBatch()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_ROAD_REPAIR_DIR");
            if(string.IsNullOrEmpty(output))throw new InvalidOperationException("QFL_ROAD_REPAIR_DIR required");
            output=Path.Combine(output,"visual_qa");Directory.CreateDirectory(output);
            RoadSurfaceBuildGuard.Validate();
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity",OpenSceneMode.Single);
            var all=scene.GetRootGameObjects();
            var main=all.SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).Single(c=>c.name=="Main Camera"&&c.CompareTag("MainCamera"));
            var traffic=all.SelectMany(g=>g.GetComponentsInChildren<KbduTrafficV2Runtime>(true)).Single();
            if(!traffic.TryValidateContract(out string error))throw new InvalidOperationException(error);
            var go=new GameObject("Road repair evidence camera"){hideFlags=HideFlags.HideAndDontSave};
            var camera=go.AddComponent<Camera>();camera.CopyFrom(main);camera.enabled=false;
            camera.stereoTargetEye=StereoTargetEyeMask.None;camera.aspect=16f/9;camera.fieldOfView=55;
            var views=new[] {
                ("highway_pilot",new Vector3(-1470,95,-680),new Vector3(-1595,9,-430)),
                ("airport_bend_pilot",new Vector3(-390,75,-760),new Vector3(-650,4,-555)),
                ("diagonal_pilot",new Vector3(-1370,90,-200),new Vector3(-1598,12,-115)),
                ("47th_pilot",new Vector3(-1350,90,140),new Vector3(-1545,11,325)),
                ("rail_bridge_oblique",new Vector3(-1510,32,-520),new Vector3(-1600,10,-440)),
                ("highway_contact",new Vector3(-1540,18,-555),new Vector3(-1595,10,-510)),
                ("airport_bend_contact",new Vector3(-600,17,-570),new Vector3(-650,4,-540))
            };
            try
            {
                foreach(var view in views)
                {
                    Quaternion rotation=Quaternion.LookRotation(view.Item3-view.Item2,Vector3.up);
                    foreach(int eye in new[]{-1,1})
                    {
                        camera.transform.SetPositionAndRotation(view.Item2+rotation*Vector3.right*(eye*.032f),rotation);
                        traffic.SetDiagnosticScheduleClock(125);
                        Save(camera,Path.Combine(output,view.Item1+(eye<0?"_left":"_right")+".png"));
                    }
                }
                var motion=views[0];
                camera.transform.SetPositionAndRotation(motion.Item2,Quaternion.LookRotation(motion.Item3-motion.Item2));
                for(int frame=0;frame<8;frame++)
                {
                    traffic.SetDiagnosticScheduleClock(125+frame*.5f);
                    Save(camera,Path.Combine(output,$"highway_motion_{frame:00}.png"));
                }
                File.WriteAllText(Path.Combine(output,"capture_notes.txt"),"Inspected Unity editor renders with 64 mm eye separation. These are geometry/appearance evidence, not OpenXR simulator or Quest performance acceptance. Clock 125 seconds; motion at 0.5 second intervals.");
            }
            finally {Object.DestroyImmediate(go);}
            Debug.Log("[RoadRepairVisualQA] complete "+output);EditorApplication.Exit(0);
        }

        static void Save(Camera camera,string path)
        {
            var target=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;camera.targetTexture=target;
            try
            {
                camera.Render();RenderTexture.active=target;
                var image=new Texture2D(1280,720,TextureFormat.RGB24,false,false);
                image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());Object.DestroyImmediate(image);
            }
            finally {camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
        }
    }
}
