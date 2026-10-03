using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using QuestFlightLab.Environment;

namespace QuestFlightLab.Editor
{
    public static class BirdOrientationAudit
    {
        public static void RunBatch()
        {
            var world=PrefabUtility.LoadPrefabContents(HabitatBirdAuthoring.World);
            try
            {
                var report=new StringBuilder();
                foreach(var bird in world.GetComponentInChildren<HabitatBirdDirector>(true).birds)
                {
                    bird.transform.rotation=Quaternion.identity;
                    bird.animator.enabled=true;bird.animator.Rebind();
                    foreach(string pose in new[]{"SoarIdle","FlapLoop"})
                    {
                        float minimum=1;
                        for(int sample=0;sample<16;sample++)
                        {
                            bird.animator.Play(pose,0,sample/16f);bird.animator.Update(0);
                            foreach(var lod in bird.lods)
                            {
                                var head=lod.bones.First(b=>b.name=="face"||b.name=="Bip01 Head_00");
                                var body=lod.bones.First(b=>b.name=="spine"||b.name=="Bip01 Pelvis_023");
                                float alignment=Vector3.Dot((head.position-body.position).normalized,bird.transform.forward);
                                minimum=Mathf.Min(minimum,alignment);
                                if(alignment<.9f)throw new Exception(bird.birdId+" reversed or sideways in "+pose+" LOD "+lod.name);
                            }
                        }
                        report.AppendLine(bird.birdId+" "+pose+" all LODs / 16 phases min beak-forward dot="+minimum);
                    }
                    // Exercise complete turns in the authored wind envelope, not observer-relative motion.
                    float progress=float.PositiveInfinity;
                    foreach(Vector3 wind in new[]{Vector3.zero,new Vector3(-6,0,0),new Vector3(6,0,0),new Vector3(0,3,6)})
                    {
                        bird.ResetBird(bird.habitatCenter,0);typeof(HabitatBirdActor).GetProperty("Wind").SetValue(bird,wind);
                        for(int i=0;i<900;i++)
                        {
                            Vector3 direction=Quaternion.Euler(0,i*.8f,0)*Vector3.forward;
                            bird.Integrate(direction*bird.airspeed,1f/30);
                            float forward=Vector3.Dot(bird.Velocity,bird.transform.forward);
                            progress=Mathf.Min(progress,forward);
                            if(forward<=0)throw new Exception(bird.birdId+" tail-first world motion");
                        }
                    }
                    report.AppendLine(bird.birdId+" minimum forward world speed="+progress);
                }
                File.WriteAllText(System.Environment.GetEnvironmentVariable("QFL_BIRD_AUDIT"),report.ToString());
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{PrefabUtility.UnloadPrefabContents(world);}
        }
    }
}
