#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Environment;
using QuestFlightLab.Editor;
using QuestFlightLab.PhotorealAerialSurfaceV1.Reflection.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace QuestFlight.Editor.Tests
{
    public sealed class AuthoringPipelineContractEditModeTests
    {
        const string TemporaryRoot="Assets/__M09AuthoringContractTests";
        bool ownsTemporaryRoot;
        [SetUp] public void SetUp()
        {
            ownsTemporaryRoot=false;
            Assert.That(AssetDatabase.IsValidFolder(TemporaryRoot),Is.False,"Refuse to overwrite existing work.");
            string guid=AssetDatabase.CreateFolder("Assets","__M09AuthoringContractTests");
            ownsTemporaryRoot=!string.IsNullOrEmpty(guid);
            Assert.That(ownsTemporaryRoot,Is.True,"Fixture folder creation failed.");
        }
        [TearDown] public void TearDown()
        {
            if (ownsTemporaryRoot) AssetDatabase.DeleteAsset(TemporaryRoot);
            ownsTemporaryRoot=false;
        }
        static Mesh Triangle()
        {
            var mesh=new Mesh { vertices=new[]{Vector3.zero,Vector3.right,Vector3.forward}, triangles=new[]{0,2,1} };
            mesh.RecalculateNormals();return mesh;
        }

        [Test] public void ReviewedDerivativeIdentityRejectsSubstitutedBytesAndTriangleCounts()
        {
            string path="Assets/Production/Environment/ReservoirWatercraftV1/ReservoirWatercraftDerivativesV1.json";
            string hash=AuthoringBundleVerifier.Sha256(path);
            Assert.DoesNotThrow(()=>ReservoirWatercraftAuthoring.RequireDerivativeIdentity(hash,84,hash,84));
            Assert.Throws<InvalidOperationException>(()=>ReservoirWatercraftAuthoring.RequireDerivativeIdentity(hash,84,new string('0',64),84));
            Assert.Throws<InvalidOperationException>(()=>ReservoirWatercraftAuthoring.RequireDerivativeIdentity(hash,84,hash,74));
            Assert.Throws<InvalidOperationException>(()=>ReservoirWatercraftAuthoring.RequireDerivativeIdentity(null,84,hash,84));
        }

        [Test] public void WatercraftRepeatAuthoringPreservesExistingSerializedHierarchyIdentities()
        {
            const string source="Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
            string copy=TemporaryRoot+"/environment.prefab";
            Assert.That(AssetDatabase.CopyAsset(source,copy),Is.True);
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(copy);
            string guid=AssetDatabase.AssetPathToGUID(copy);
            var before=HierarchyIds(asset.transform.Find("ReservoirWatercraftRoot"));
            for(int repeat=0;repeat<2;repeat++)
            {
                var root=PrefabUtility.LoadPrefabContents(copy);
                try
                {
                    var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Production/Environment/PhotorealTerrainV1/KBDU_ParkedAircraft.mat");
                    ReservoirWatercraftAuthoring.UpdateOwnedFeatureHierarchy(root,material);
                    PrefabUtility.SaveAsPrefabAsset(root,copy);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var after=HierarchyIds(AssetDatabase.LoadAssetAtPath<GameObject>(copy).transform.Find("ReservoirWatercraftRoot"));
            Assert.That(after,Is.EqualTo(before),"Every existing GameObject/component localFileID must survive both refreshes.");
            Assert.That(AssetDatabase.AssetPathToGUID(copy),Is.EqualTo(guid));
        }

        static string[] HierarchyIds(Transform root)
        {
            Assert.That(root,Is.Not.Null);
            return root.GetComponentsInChildren<Component>(true).Select(component=>
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component,out string guid,out long localId);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component.gameObject,out string _,out long objectId);
                return component.name+"/"+component.GetType().Name+":"+objectId+":"+localId;
            }).OrderBy(value=>value,StringComparer.Ordinal).ToArray();
        }

        [Test] public void TrafficIntegrationPreservesExistingNestedPrefabIdentitiesAndRejectsMissingRoot()
        {
            string copy=TemporaryRoot+"/traffic-environment.prefab";
            Assert.That(AssetDatabase.CopyAsset("Assets/Production/Environment/ProductionEnvironmentRoot.prefab",copy),Is.True);
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(copy);
            var before=HierarchyIds(asset.transform.Find("TrafficRoot"));
            string guid=AssetDatabase.AssetPathToGUID(copy);
            for(int repeat=0;repeat<2;repeat++)
            {
                var root=PrefabUtility.LoadPrefabContents(copy);
                try
                {
                    var existing=root.transform.Find("TrafficRoot").gameObject;
                    var actual=QuestFlightLab.Environment.Editor.KbduTrafficAuthoring.RequireCurrentTrafficInstance(root);
                    Assert.That(actual,Is.SameAs(existing));
                    PrefabUtility.SaveAsPrefabAsset(root,copy);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Assert.That(HierarchyIds(AssetDatabase.LoadAssetAtPath<GameObject>(copy).transform.Find("TrafficRoot")),Is.EqualTo(before));
            Assert.That(AssetDatabase.AssetPathToGUID(copy),Is.EqualTo(guid));
            var invalid=new GameObject("MissingTrafficAuthoringFixture");
            try { Assert.Throws<InvalidOperationException>(()=>QuestFlightLab.Environment.Editor.KbduTrafficAuthoring.RequireCurrentTrafficInstance(invalid)); }
            finally { Object.DestroyImmediate(invalid); }
        }

        [Test] public void CompilerUpdatesOnlyItsExactOutputsAndPreservesSharedFolderGuids()
        {
            AssetDatabase.CreateFolder(TemporaryRoot,"Sources");AssetDatabase.CreateFolder(TemporaryRoot,"Native");
            File.WriteAllText(TemporaryRoot+"/Sources/owned.obj","v 0 0 0\nv 1 0 0\nv 0 0 1\nf 1 3 2\n");
            string retained=TemporaryRoot+"/Native/referenced_tree.asset";
            AssetDatabase.CreateAsset(Triangle(),retained);AssetDatabase.SaveAssets();
            string guid=AssetDatabase.AssetPathToGUID(retained), hash=AuthoringBundleVerifier.Sha256(retained);
            Type author=typeof(PhotorealAerialSurfaceNativeMeshAuthoring);
            object policy=Enum.Parse(author.GetNestedType("NativeNormalPolicy",BindingFlags.NonPublic),"HonorObjSmoothing");
            var method=author.GetMethod("CompileFolder",BindingFlags.Static|BindingFlags.NonPublic);
            method.Invoke(null,new[]{(object)(TemporaryRoot+"/Sources"),TemporaryRoot+"/Native","Fixture",1,policy,0});
            AssetDatabase.SaveAssets();
            Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(TemporaryRoot+"/Native/owned.asset"),Is.Not.Null);
            Assert.That(AssetDatabase.AssetPathToGUID(retained),Is.EqualTo(guid));
            Assert.That(AuthoringBundleVerifier.Sha256(retained),Is.EqualTo(hash));
        }

        [Test] public void AnimationRefreshPreservesControllerAndStateIdentityAndPreflightsMissingClips()
        {
            string path=TemporaryRoot+"/eagle.controller";
            var controller=AnimatorController.CreateAnimatorControllerAtPath(path);
            var original=controller.layers[0].stateMachine.AddState("SoarIdle");
            var extra=controller.layers[0].stateMachine.AddState("UnrelatedState");
            var clip=new AnimationClip();AssetDatabase.CreateAsset(clip,TemporaryRoot+"/clip.anim");
            AssetDatabase.SaveAssets();string guid=AssetDatabase.AssetPathToGUID(path);
            long originalId;AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original,out string _,out originalId);
            GoldenEagleThermalAuthoring.UpdateOwnedAnimationStates(controller,new[]{"SoarIdle","FlapLoop"},new[]{clip,clip});
            GoldenEagleThermalAuthoring.UpdateOwnedAnimationStates(controller,new[]{"SoarIdle","FlapLoop"},new[]{clip,clip});
            AssetDatabase.SaveAssets();
            Assert.That(AssetDatabase.AssetPathToGUID(path),Is.EqualTo(guid));
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original,out string _,out long afterId);
            Assert.That(afterId,Is.EqualTo(originalId));
            Assert.That(controller.layers[0].stateMachine.states.Select(state=>state.state),Does.Contain(extra));
            Assert.That(controller.layers[0].stateMachine.states.Count(state=>state.state.name=="FlapLoop"),Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(()=>GoldenEagleThermalAuthoring.UpdateOwnedAnimationStates(controller,new[]{"SoarIdle"},new AnimationClip[]{null}));
            Assert.That(original.motion,Is.EqualTo(clip));
        }

        [Test] public void ReplacingAmbientDirectorRebindsEveryExistingWildlifeDirector()
        {
            var root=new GameObject("AuthoredFixture");
            try
            {
                var oldObject=new GameObject("OldTraffic");oldObject.transform.SetParent(root.transform);
                var old=oldObject.AddComponent<AmbientTrafficDirector>();
                var bird=root.AddComponent<HabitatBirdDirector>();bird.enabled=false;bird.traffic=old;
                Object.DestroyImmediate(oldObject);
                var replacement=root.AddComponent<AmbientTrafficDirector>();
                BoulderAmbientAircraftAuthoring.RebindHabitatTraffic(root,replacement);
                Assert.That(bird.traffic,Is.SameAs(replacement));
                Assert.Throws<ArgumentNullException>(()=>BoulderAmbientAircraftAuthoring.RebindHabitatTraffic(root,null));
                Assert.That(File.ReadAllText("Assets/Editor/AmbientTrafficScenarioBatch.cs"),Does.Not.Contain("BoulderAmbientAircraftAuthoring.Build()"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test] public void ChangedPlacementInputsCannotRefreshExistingGeometryIdentity()
        {
            const string manifest="Assets/Production/Environment/ReservoirWatercraftV1/ReservoirWatercraftPlacementsV1.json";
            string current=AuthoringBundleVerifier.Sha256(manifest);
            Assert.DoesNotThrow(()=>ReservoirWatercraftAuthoring.RequireUnchangedPlacementIdentity(current,current));
            string changed=TemporaryRoot+"/changed-placement.json";
            string source=File.ReadAllText(manifest);
            File.WriteAllText(changed,source+"\n ");
            Assert.Throws<InvalidOperationException>(()=>ReservoirWatercraftAuthoring.RequireUnchangedPlacementIdentity(current,AuthoringBundleVerifier.Sha256(changed)));
            Assert.Throws<InvalidOperationException>(()=>ReservoirWatercraftAuthoring.RequireUnchangedPlacementIdentity(null,current));
            string code=File.ReadAllText("Assets/Editor/ReservoirWatercraftAuthoring.cs");
            int start=code.IndexOf("public static void RevalidateExistingSurfaceAlignment",StringComparison.Ordinal);
            int guard=code.IndexOf("RequireUnchangedPlacementIdentity",start,StringComparison.Ordinal);
            int sample=code.IndexOf("VisibleSurfaceSampler.Create",start,StringComparison.Ordinal);
            Assert.That(guard,Is.LessThan(sample),"Changed-input failure precedes any terrain sampling or certificate rewrite.");
        }

        [Test] public void TrafficAuthoringPreservesCurrentMandatorySurfaceReferences()
        {
            var current=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Production/Environment/TrafficV1/KBDU_TrafficRoot.prefab").GetComponent<KbduTrafficV2Runtime>();
            var original=new SerializedObject(current);
            var road=(TextAsset)original.FindProperty("gradeSeparatedRoadAuthority").objectReferenceValue;
            var bank=(TextAsset)original.FindProperty("laneSurfaceBank").objectReferenceValue;
            var contacts=original.FindProperty("vehicleContactGeometry");
            var values=Enumerable.Range(0,contacts.arraySize).Select(i=>contacts.GetArrayElementAtIndex(i).vector4Value).ToArray();
            var go=new GameObject("TrafficFixture");go.SetActive(false);
            try
            {
                var runtime=go.AddComponent<KbduTrafficV2Runtime>();runtime.enabled=false;
                QuestFlightLab.Environment.Editor.KbduTrafficAuthoring.CopyCurrentSurfaceBindings(runtime,road,bank,values);
                var copy=new SerializedObject(runtime);
                Assert.That(copy.FindProperty("gradeSeparatedRoadAuthority").objectReferenceValue,Is.EqualTo(road));
                Assert.That(copy.FindProperty("laneSurfaceBank").objectReferenceValue,Is.EqualTo(bank));
                Assert.That(copy.FindProperty("vehicleContactGeometry").arraySize,Is.EqualTo(6));
                for(int i=0;i<6;i++)Assert.That(copy.FindProperty("vehicleContactGeometry").GetArrayElementAtIndex(i).vector4Value,Is.EqualTo(values[i]));
                Assert.Throws<InvalidOperationException>(()=>QuestFlightLab.Environment.Editor.KbduTrafficAuthoring.CopyCurrentSurfaceBindings(runtime,null,bank,values));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void CacheActivationRollsBackFailureAndKeepsBackupsOutsideShippingDirectory()
        {
            string root=Path.Combine(Path.GetTempPath(),"qf-cache-fixture-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string parent=Path.Combine(root,"Library"),destination=Path.Combine(root,"StreamingAssets","Cache");
                Directory.CreateDirectory(parent);Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination,"old"),"previous validated cache");
                string stage=Path.Combine(parent,"Cache.staging-test");
                Assert.Throws<DirectoryNotFoundException>(()=>AuthoringBundleVerifier.Activate(stage,destination,parent));
                Assert.That(File.Exists(Path.Combine(destination,"old")),Is.True);
                Directory.CreateDirectory(stage);File.WriteAllText(Path.Combine(stage,"new"),"verified new cache");
                AuthoringBundleVerifier.Activate(stage,destination,parent);
                Assert.That(File.Exists(Path.Combine(destination,"new")),Is.True);
                Assert.That(File.Exists(Path.Combine(destination,"old")),Is.False);
                Assert.That(Directory.GetDirectories(parent,"Cache.previous-*").Single(),Does.StartWith(parent));
                Assert.Throws<InvalidOperationException>(()=>AuthoringBundleVerifier.Activate(destination,destination,parent));
            }
            finally { Directory.Delete(root,true); }
        }

        [Test] public void RealMeshBundleMustLoadEveryExpectedAddressAndRejectMissingCorruptOrWrongContents()
        {
            string root=Path.Combine(Path.GetTempPath(),"qf-bundle-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string mesh=TemporaryRoot+"/fixture.asset";AssetDatabase.CreateAsset(Triangle(),mesh);AssetDatabase.SaveAssets();
            var builds=new[]{new AssetBundleBuild { assetBundleName="qf_buildings_fixture",assetNames=new[]{mesh},addressableNames=new[]{"expected_mesh"} }};
            try
            {
                Assert.That(BuildPipeline.BuildAssetBundles(root,builds,BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneWindows64),Is.Not.Null);
                var evidence=AuthoringBundleVerifier.Verify(root,builds,"qf_buildings_",false);
                Assert.That(evidence,Has.Length.EqualTo(1));Assert.That(evidence[0].assets.Single().vertex_count,Is.EqualTo(3));
                Assert.That(evidence[0].assets.Single().index_count,Is.EqualTo(3));
                Type author=typeof(FullMapBuildingClipmapAssetAuthoring);
                Type recordType=author.GetNestedType("BundleRecord",BindingFlags.NonPublic);
                object record=Activator.CreateInstance(recordType,true);
                string expectedHash="fixture_content_hash";
                recordType.GetField("name").SetValue(record,evidence[0].name);
                recordType.GetField("crc").SetValue(record,evidence[0].crc);
                recordType.GetField("sha256").SetValue(record,evidence[0].sha256);
                recordType.GetField("bytes").SetValue(record,evidence[0].bytes);
                recordType.GetField("hash").SetValue(record,expectedHash);
                var identity=author.GetMethod("RequireRecordedBundleIntegrity",BindingFlags.NonPublic|BindingFlags.Static);
                Assert.DoesNotThrow(()=>identity.Invoke(null,new[]{record,evidence[0],expectedHash}));
                foreach(string field in new[]{"crc","sha256","bytes","hash"})
                {
                    var member=recordType.GetField(field);object saved=member.GetValue(record);
                    object changed=field=="crc"?(object)(evidence[0].crc^1u):field=="bytes"?(object)(evidence[0].bytes+1):"changed";
                    member.SetValue(record,changed);
                    Assert.That(Assert.Throws<TargetInvocationException>(()=>identity.Invoke(null,new[]{record,evidence[0],expectedHash})).InnerException,Is.TypeOf<InvalidOperationException>());
                    member.SetValue(record,saved);
                }
                var wrong=new[]{new AssetBundleBuild {assetBundleName="qf_buildings_fixture",addressableNames=new[]{"wrong_mesh"}}};
                Assert.Throws<InvalidOperationException>(()=>AuthoringBundleVerifier.Verify(root,wrong,"qf_buildings_",false));
                string binary=Path.Combine(root,"qf_buildings_fixture"),missing=binary+".missing";
                File.Move(binary,missing);
                Assert.Throws<InvalidOperationException>(()=>AuthoringBundleVerifier.Verify(root,builds,"qf_buildings_",false));
                File.Move(missing,binary);File.WriteAllBytes(binary,Array.Empty<byte>());
                Assert.Throws<InvalidOperationException>(()=>AuthoringBundleVerifier.Verify(root,builds,"qf_buildings_",false));
                // An intact sidecar cannot certify a non-empty corrupt binary.
                File.WriteAllBytes(binary,Enumerable.Repeat((byte)0x41,128).ToArray());
                bool messages=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
                try { Assert.Throws<InvalidOperationException>(()=>AuthoringBundleVerifier.Verify(root,builds,"qf_buildings_",false)); }
                finally { LogAssert.ignoreFailingMessages=messages; }
            }
            finally { Directory.Delete(root,true); }
        }

        [Test] public void CurrentBuildingSpecificationRequiresAll144RegionsNotALoneSidecar()
        {
            Type author=typeof(FullMapBuildingClipmapAssetAuthoring);
            object database=author.GetMethod("LoadDatabase",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
            var builds=(AssetBundleBuild[])author.GetMethod("ExpectedBundleBuilds",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new[]{database});
            Assert.That(builds,Has.Length.EqualTo(144));Assert.That(builds.Sum(build=>build.addressableNames.Length),Is.EqualTo(576));
            string root=Path.Combine(Path.GetTempPath(),"qf-incomplete-cache-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root,"qf_buildings_fake.manifest"),"claimed cache");
                var cache=author.GetMethod("ValidateBundleCache",BindingFlags.NonPublic|BindingFlags.Static);
                var missingIndex=Assert.Throws<TargetInvocationException>(()=>cache.Invoke(null,new object[]{database,root,BuildTarget.Android}));
                Assert.That(missingIndex.InnerException,Is.TypeOf<InvalidOperationException>());
                Assert.That(missingIndex.InnerException.Message,Does.Contain("index is missing"));
                Assert.Throws<InvalidOperationException>(()=>AuthoringBundleVerifier.Verify(root,builds,"qf_buildings_",false));
                File.WriteAllBytes(Path.Combine(root,builds[0].assetBundleName),new byte[64]);
                Assert.Throws<InvalidOperationException>(()=>AuthoringBundleVerifier.Verify(root,builds,"qf_buildings_",false));
            }
            finally { Directory.Delete(root,true); }
        }

        [Test] public void VisibilityUsesOnlyItsOwnedCollidersDespiteUnrelatedLayer29Collider()
        {
            Scene scene=EditorSceneManager.NewPreviewScene();
            var unrelated=GameObject.CreatePrimitive(PrimitiveType.Cube);unrelated.layer=29;unrelated.transform.position=new Vector3(0,2,0);unrelated.transform.localScale=new Vector3(20,1,20);
            var source=new GameObject("Source");SceneManager.MoveGameObjectToScene(source,scene);
            var mesh=Triangle();source.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=source.AddComponent<MeshRenderer>();
            try
            {
                Type bake=typeof(CockpitDirectionalVisibilityAuthoring);
                var directions=bake.GetMethod("BuildSoftDirections",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
                var method=bake.GetMethod("CalculateVisibility",BindingFlags.NonPublic|BindingFlags.Static);
                Physics.SyncTransforms();
                Color32[] withOther=(Color32[])method.Invoke(null,new[]{(object)renderer,source.transform,Array.Empty<Collider>(),directions});
                Object.DestroyImmediate(unrelated);Physics.SyncTransforms();
                Color32[] clean=(Color32[])method.Invoke(null,new[]{(object)renderer,source.transform,Array.Empty<Collider>(),directions});
                Assert.That(withOther,Is.EqualTo(clean));Assert.That(clean,Has.All.EqualTo(new Color32(255,255,255,255)));
                var owned=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(owned,scene);owned.layer=29;
                Object.DestroyImmediate(owned.GetComponent<BoxCollider>());
                owned.AddComponent<MeshCollider>().sharedMesh=owned.GetComponent<MeshFilter>().sharedMesh;
                owned.transform.position=new Vector3(0,2,0);owned.transform.localScale=new Vector3(20,1,20);Physics.SyncTransforms();
                Color32[] blocked=(Color32[])method.Invoke(null,new[]{(object)renderer,source.transform,new[]{owned.GetComponent<Collider>()},directions});
                Assert.That(blocked.Any(value=>value.r<255||value.g<255||value.b<255||value.a<255),Is.True,"Owned geometry must still occlude.");
            }
            finally { if(unrelated!=null)Object.DestroyImmediate(unrelated);Object.DestroyImmediate(mesh);EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test] public void ReflectionPreviewPixelsIgnoreUnrelatedOpenSceneRendererAndLight()
        {
            var bake=typeof(PhotorealAerialReflectionAuthoring).GetMethod("BuildCockpitInteriorReflectionPixels",BindingFlags.NonPublic|BindingFlags.Static);
            Color[] baseline=(Color[])bake.Invoke(null,null);
            var unrelated=GameObject.CreatePrimitive(PrimitiveType.Cube);unrelated.name="UnrelatedM09ReflectionIntruder";unrelated.transform.localScale=Vector3.one*20;
            var lightObject=new GameObject("UnrelatedM09Sun");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.color=Color.magenta;light.intensity=20;
            int scene=unrelated.scene.handle, id=unrelated.GetInstanceID();
            try
            {
                Color[] perturbed=(Color[])bake.Invoke(null,null);
                Assert.That(perturbed.Length,Is.EqualTo(baseline.Length));
                float maximum=0;for(int i=0;i<baseline.Length;i++)maximum=Mathf.Max(maximum,(new Vector4(baseline[i].r-perturbed[i].r,baseline[i].g-perturbed[i].g,baseline[i].b-perturbed[i].b,baseline[i].a-perturbed[i].a)).magnitude);
                Assert.That(maximum,Is.LessThan(.0001f));
                Assert.That((int)unrelated.scene.handle,Is.EqualTo(scene));Assert.That(unrelated.GetInstanceID(),Is.EqualTo(id));Assert.That(light.intensity,Is.EqualTo(20));
            }
            finally { Object.DestroyImmediate(unrelated);Object.DestroyImmediate(lightObject); }
        }
    }
}
#endif
