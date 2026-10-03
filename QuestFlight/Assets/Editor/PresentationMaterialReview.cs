using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        // Isolated inspection only. Never save the scene or these material variants.
        public static void RunPresentationMaterialReview()
        {
            string output=Path.Combine(System.Environment.GetEnvironmentVariable("QFL_RELEASE_POLISH_QA"),"material_investigation");Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport{scenePath=ProductionScenePath,outputDirectory=output,renderWidth=1440,renderHeight=1080};
            var c=LoadContext(1440,1080,report);var renderers=c.rig.AircraftVisualRoot.GetComponentsInChildren<Renderer>(true);
            var originals=renderers.ToDictionary(r=>r,r=>r.sharedMaterials);var temporary=new List<Material>();
            File.WriteAllLines(Path.Combine(output,"materials.txt"),renderers.Select(r=>r.name+": "+string.Join(" | ",r.sharedMaterials.Select(m=>m==null?"null":AssetDatabase.GetAssetPath(m)+"; "+m.shader.name))));
            try
            {
                foreach(string mode in new[]{"original","diffuse_only","base_color_only"})
                {
                    foreach(var r in renderers)
                    {
                        r.sharedMaterials=originals[r].Select(source=>
                        {
                            if(source==null||mode=="original")return source;
                            var m=new Material(source);temporary.Add(m);
                            if(m.HasProperty("metallicFactor"))m.SetFloat("metallicFactor",0);
                            if(m.HasProperty("roughnessFactor"))m.SetFloat("roughnessFactor",1);
                            if(m.HasProperty("normalTexture_scale"))m.SetFloat("normalTexture_scale",0);
                            if(mode=="base_color_only"&&m.HasProperty("baseColorFactor"))
                            {var color=m.GetColor("baseColorFactor");var texture=m.HasProperty("baseColorTexture")?m.GetTexture("baseColorTexture"):null;m.shader=Shader.Find("Unlit/Texture");m.SetTexture("_MainTex",texture??Texture2D.whiteTexture);if(texture==null){m.shader=Shader.Find("Unlit/Color");m.color=color;}}
                            return m;
                        }).ToArray();
                    }
                    Capture(c,report,output,1440,1080,new ShotDefinition(mode,"Material investigation only","material_isolation",c.rig.PilotSeatAnchor.position,c.rig.AircraftSimulationRoot.rotation*Quaternion.Euler(25,0,0),55,ViewProfile.Cockpit));
                }
            }
            finally {foreach(var r in renderers)r.sharedMaterials=originals[r];foreach(var m in temporary)UnityEngine.Object.DestroyImmediate(m);WriteReportFiles(report,output);}
        }
    }
}
