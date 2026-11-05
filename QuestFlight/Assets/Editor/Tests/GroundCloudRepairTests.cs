#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using QuestFlightLab.Environment;
namespace QuestFlight.Editor.Tests
{
    public sealed class GroundCloudRepairTests
    {
        [Test] public void CloudedSkyIsRetainedAndFormationsAreSeparatedAtComparableScale()
        {
            var sky=AssetDatabase.LoadAssetAtPath<Material>("Assets/Production/Environment/Sky/Kloppenheim03_PureSky_Skybox.mat");
            Assert.AreEqual("Skybox/Cubemap",sky.shader.name);
            Assert.NotNull(sky.GetTexture("_Tex"),"Lighting reference must remain available for rollback/reflections");
            var p=AssetDatabase.LoadAssetAtPath<BoulderCloudProfile>("Assets/Production/Environment/CloudsV1/KBDU_BoulderCloudProfile.asset");
            Assert.AreEqual(8,p.Volumes.Length);
            var renderer=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/Production/Rendering/ProductForwardRenderer.asset");
            Assert.AreEqual(UnityEngine.Rendering.Universal.CopyDepthMode.AfterOpaques,renderer.copyDepthMode);
            Assert.LessOrEqual(p.Volumes.Max(v=>v.radiiMeters.x)/p.Volumes.Min(v=>v.radiiMeters.x),1.1f);
            Assert.LessOrEqual(p.Volumes.Max(v=>v.radiiMeters.z)/p.Volumes.Min(v=>v.radiiMeters.z),1.11f);
            for(int i=0;i<p.Volumes.Length;i++)for(int k=i+1;k<p.Volumes.Length;k++)
            {var a=p.Volumes[i];var b=p.Volumes[k];Assert.IsFalse(new Bounds(a.centerLocalMeters,a.radiiMeters*2).Intersects(new Bounds(b.centerLocalMeters,b.radiiMeters*2)),a.id+" overlaps "+b.id);}
        }
        [Test] public void InteriorDoesNotAddASecondFogEnvelopeOrFadeDistantClouds()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ProductionEnvironmentRoot.PrefabAssetPath);
            var instance=Object.Instantiate(prefab);
            try
            {
                var field=instance.GetComponent<ProductionEnvironmentRoot>().boulderCloudField.GetComponent<BoulderCloudField>();
                float fog=RenderSettings.fogDensity; var color=RenderSettings.fogColor;
                field.ApplyAtmosphereForValidation(field.transform.TransformPoint(field.Profile.Volumes[0].centerLocalMeters));
                Assert.AreEqual(fog,RenderSettings.fogDensity);Assert.AreEqual(color,RenderSettings.fogColor);
                Assert.AreEqual(1f,Shader.GetGlobalFloat("_KBDUCloudExteriorOpacity"));
                field.RestoreAtmosphereForValidation();
            }
            finally { Object.DestroyImmediate(instance); }
        }
        [Test] public void GroundCutoutMatchesAuthoritativePavementWithoutChangingCollisionMesh()
        {
            var env=AssetDatabase.LoadAssetAtPath<GameObject>(ProductionEnvironmentRoot.PrefabAssetPath).GetComponent<ProductionEnvironmentRoot>();
            Assert.AreSame(env.runwayPavement.sharedMesh,env.runwayCollisionSurface.sharedMesh);
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.shader");
            var mats=env.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.shader==shader).Distinct().ToArray();
            Assert.IsNotEmpty(mats);
            var a=env.transform.TransformPoint(env.runway08EndLocal);var b=env.transform.TransformPoint(env.runway26EndLocal);var axis=(b-a).normalized;
            foreach(var m in mats)
            {
                var origin=m.GetVector("_RunwayCutoutOriginAxis");var size=m.GetVector("_RunwayCutoutSize");
                Assert.That(Vector2.Distance(new Vector2(origin.x,origin.y),new Vector2(a.x,a.z)),Is.LessThan(.01f));
                Assert.That(Vector2.Distance(new Vector2(origin.z,origin.w),new Vector2(axis.x,axis.z)),Is.LessThan(.001f));
                Assert.AreEqual(env.runwayWidthMeters,size.y,.001f);Assert.AreEqual(Vector3.Distance(a,b),size.x,.01f);Assert.AreEqual(1,size.z);
            }
        }
    }
}
#endif
