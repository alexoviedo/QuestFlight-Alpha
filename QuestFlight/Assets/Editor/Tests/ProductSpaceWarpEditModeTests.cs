using System.Collections.Generic;
using NUnit.Framework;
using QuestFlight.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class ProductSpaceWarpEditModeTests
    {
        [TestCase(35f, 0f)] [TestCase(-35f, 0f)] [TestCase(0f, 25f)]
        public void AuthoredCameraHeadMotionIsNotApplicationMotion(float yaw, float pitch)
        {
            var root = UnityEditor.PrefabUtility.LoadPrefabContents("Assets/Production/Prefabs/ProductionC172AircraftRig.prefab");
            try
            {
                var rig = root.GetComponentInChildren<QuestFlightLab.Runtime.AircraftReferenceFrameRig>(true);
                Assert.IsNotNull(rig);
                var camera = rig.TrackedCamera;
                var cameraRig = camera.GetComponentInParent<OVRCameraRig>();
                Assert.IsNotNull(cameraRig);
                Assert.AreSame(cameraRig.centerEyeAnchor, camera.transform.parent, "Exercise the actual nested product camera, not the SDK's different layout.");
                var appSpace = ProductSpaceWarpController.ResolveApplicationSpace(camera);
                Assert.AreSame(cameraRig.trackingSpace, appSpace);
                var before = appSpace.localToWorldMatrix;
                var oldWrongPose = camera.transform.parent.localToWorldMatrix;
                var localCamera = camera.transform.localToWorldMatrix;
                // Emulate tracking on the SDK-owned anchor; no camera writes.
                cameraRig.centerEyeAnchor.localRotation = Quaternion.Euler(pitch, yaw, 0);
                cameraRig.centerEyeAnchor.localPosition += new Vector3(.12f, .04f, -.07f);
                Assert.AreNotEqual(oldWrongPose, camera.transform.parent.localToWorldMatrix, "Reproduce the previous head-contaminated app pose.");
                Assert.AreNotEqual(localCamera, camera.transform.localToWorldMatrix, "Head movement must still reach the eye camera.");
                Assert.AreEqual(before, appSpace.localToWorldMatrix, "Head movement must not enter application-space delta.");
                // Aircraft motion and seat/recenter offsets must still be sent.
                root.transform.SetPositionAndRotation(new Vector3(-800, 650, 4300), Quaternion.Euler(8, 260, -20));
                rig.ApplyCalibration(new Vector3(.03f, -.02f, .01f), 12);
                rig.XrOrigin.localRotation = Quaternion.Euler(0, -18, 0);
                Assert.AreNotEqual(before, appSpace.localToWorldMatrix);
                Assert.AreSame(appSpace, ProductSpaceWarpController.ResolveApplicationSpace(camera));
                var moved = appSpace.localToWorldMatrix;
                cameraRig.centerEyeAnchor.localRotation = Quaternion.Euler(-pitch, -yaw, 0);
                Assert.AreEqual(moved, appSpace.localToWorldMatrix, "Banking and calibration must not reintroduce head pose.");
            }
            finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
        }
        [Test]
        public void MissingAuthoredTrackingSpaceFailsClosed()
        {
            var root = new GameObject("Unowned test camera");
            try { Assert.IsNull(ProductSpaceWarpController.ResolveApplicationSpace(root.AddComponent<Camera>())); }
            finally { Object.DestroyImmediate(root); }
        }
        [TestCase(3f,0f)] [TestCase(-3f,0f)] [TestCase(0f,3f)]
        public void SkyMotionUsesProviderProjectionDuringHeadTurns(float yaw,float pitch)
        {
            bool async=UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation=false;
            var material=new Material(Shader.Find("Hidden/Universal Render Pipeline/XR/XRMotionVector"));
            var rt=new RenderTexture(32,32,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)
            {dimension=TextureDimension.Tex2DArray,volumeDepth=2};
            var pixels=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);
            var previousTarget=RenderTexture.active;var cmd=new CommandBuffer();
            try
            {
                rt.Create();
                var projection=Matrix4x4.Perspective(90,1,.03f,24000);
                var gpu=GL.GetGPUProjectionMatrix(projection,false);
                var view=Matrix4x4.Scale(new Vector3(1,1,-1));
                var vp=gpu*view;var old=vp*Matrix4x4.Rotate(Quaternion.Euler(pitch,yaw,0));
                Color[][] reference=new Color[2][];
                for(int flipped=0;flipped<2;flipped++)
                {
                    cmd.Clear();cmd.SetRenderTarget(rt,0,CubemapFace.Unknown,-1);cmd.ClearRenderTarget(true,true,Color.clear);
                    var colorProjection=projection;if(flipped!=0)colorProjection.SetRow(1,-colorProjection.GetRow(1));
                    cmd.SetViewProjectionMatrices(view,colorProjection);
                    for(int eye=0;eye<2;eye++)XRBuiltinShaderConstants.UpdateBuiltinShaderConstants(view,colorProjection,false,eye);
                    XRBuiltinShaderConstants.SetBuiltinShaderConstants(cmd);
                    cmd.SetGlobalMatrixArray("_QFInverseViewProjectionStereo",new[]{vp.inverse,vp.inverse});
                    cmd.SetGlobalMatrixArray("_NonJitteredViewProjMatrixStereo",new[]{vp,vp});
                    cmd.SetGlobalMatrixArray("_PrevViewProjMatrixStereo",new[]{old,old});
                    cmd.SetGlobalFloat("_SpaceWarpNDCModifier",1);
                    cmd.SetSinglePassStereo(SinglePassStereoMode.Instancing);cmd.EnableShaderKeyword("STEREO_INSTANCING_ON");
                    cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3,1);
                    cmd.DisableShaderKeyword("STEREO_INSTANCING_ON");cmd.SetSinglePassStereo(SinglePassStereoMode.None);
                    Graphics.ExecuteCommandBuffer(cmd);
                    for(int eye=0;eye<2;eye++)
                    {
                        Graphics.SetRenderTarget(rt,0,CubemapFace.Unknown,eye);
                        pixels.ReadPixels(new Rect(0,0,32,32),0,0);pixels.Apply();var actual=pixels.GetPixels();
                        Assert.Greater(Mathf.Max(Mathf.Abs(actual[528].r),Mathf.Abs(actual[528].g)),.01f,"Head rotation must produce sky motion");
                        if(flipped==0)reference[eye]=actual;
                        else
                        {
                            float largest=0;
                            for(int i=0;i<actual.Length;i++)
                                largest=Mathf.Max(largest,Mathf.Abs(actual[i].r-reference[eye][i].r),Mathf.Abs(actual[i].g-reference[eye][i].g));
                            Assert.Less(largest,.001f,"Color projection changed provider sky motion, eye "+eye);
                        }
                    }
                }
            }
            finally
            {UnityEditor.ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previousTarget;cmd.Release();rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(pixels);Object.DestroyImmediate(material);}
        }
        [TestCase(0f,0f)] [TestCase(3f,0f)] [TestCase(0f,3f)]
        public void CloudMotionCoverageIgnoresIntermediateColorProjection(float yaw,float pitch)
        {
            // The Quest color intermediate and provider motion target can have
            // opposite Y conventions. Cloud coverage must use the provider VP,
            // including during head turns, rather than Unity's color-camera VP.
            bool async=UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation=false;
            var material=new Material(Shader.Find("QuestFlight/Boulder Sparse Volumetric Cloud Field"));
            material.SetTexture("_DensitySlices",Texture2D.whiteTexture);
            material.SetFloat("_DensityGain",4);material.SetFloat("_Extinction",8);
            var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh=Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(primitive);
            var center=new Vector3(0,550,2200);var radii=new Vector3(450,350,450);
            var vertices=mesh.vertices;
            var centers=new List<Vector3>();var sizes=new List<Vector3>();var parameters=new List<Vector2>();
            for(int i=0;i<vertices.Length;i++)
            {vertices[i]=center+Vector3.Scale(vertices[i],radii*2);centers.Add(center);sizes.Add(radii);parameters.Add(Vector2.one);}
            mesh.vertices=vertices;mesh.SetUVs(1,centers);mesh.SetUVs(2,sizes);mesh.SetUVs(3,parameters);mesh.RecalculateBounds();
            var rt=new RenderTexture(64,64,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)
            {dimension=TextureDimension.Tex2DArray,volumeDepth=2};
            var pixels=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
            var previousTarget=RenderTexture.active;
            var cmd=new CommandBuffer();
            try
            {
                rt.Create();
                var projection=Matrix4x4.Perspective(75,1,.03f,24000);
                var gpu=GL.GetGPUProjectionMatrix(projection,false);
                var views=new[]{Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.Translate(new Vector3(.03f,0,0)),
                    Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.Translate(new Vector3(-.03f,0,0))};
                var current=new[]{gpu*views[0],gpu*views[1]};
                var rotation=Matrix4x4.Rotate(Quaternion.Euler(pitch,yaw,0));
                var old=new[]{gpu*views[0]*rotation,gpu*views[1]*rotation};
                Color[][] reference=new Color[2][];
                for(int flipped=0;flipped<2;flipped++)
                {
                    cmd.Clear();cmd.SetRenderTarget(rt,0,CubemapFace.Unknown,-1);cmd.ClearRenderTarget(true,true,Color.clear);
                    var colorProjection=projection;
                    if(flipped!=0)colorProjection.SetRow(1,-colorProjection.GetRow(1));
                    cmd.SetViewProjectionMatrices(views[0],colorProjection);
                    for(int eye=0;eye<2;eye++)XRBuiltinShaderConstants.UpdateBuiltinShaderConstants(views[eye],colorProjection,false,eye);
                    XRBuiltinShaderConstants.SetBuiltinShaderConstants(cmd);
                    var colorGpu=GL.GetGPUProjectionMatrix(colorProjection,false);
                    cmd.SetGlobalMatrixArray("unity_StereoMatrixVP",new[]{colorGpu*views[0],colorGpu*views[1]});
                    cmd.SetGlobalMatrixArray("unity_StereoMatrixP",new[]{colorGpu,colorGpu});
                    cmd.SetGlobalMatrixArray("unity_StereoMatrixV",views);
                    cmd.SetGlobalVectorArray("unity_StereoWorldSpaceCameraPos",new[]{new Vector4(-.03f,0,0,1),new Vector4(.03f,0,0,1)});
                    cmd.SetGlobalMatrixArray("_NonJitteredViewProjMatrixStereo",current);
                    cmd.SetGlobalMatrixArray("_PrevViewProjMatrixStereo",old);
                    cmd.SetGlobalFloat("_SpaceWarpNDCModifier",1);cmd.SetGlobalFloat("_QFResetMotion",0);
                    cmd.SetGlobalVector("_CloudDrift",Vector4.zero);cmd.SetGlobalVector("_QFPreviousCloudDrift",Vector4.zero);
                    cmd.SetSinglePassStereo(SinglePassStereoMode.Instancing);cmd.EnableShaderKeyword("STEREO_INSTANCING_ON");
                    cmd.DrawMesh(mesh,Matrix4x4.identity,material,0,material.FindPass("XRMotionVectors"));
                    cmd.DisableShaderKeyword("STEREO_INSTANCING_ON");cmd.SetSinglePassStereo(SinglePassStereoMode.None);
                    Graphics.ExecuteCommandBuffer(cmd);
                    for(int eye=0;eye<2;eye++)
                    {
                        Graphics.SetRenderTarget(rt,0,CubemapFace.Unknown,eye);
                        pixels.ReadPixels(new Rect(0,0,64,64),0,0);pixels.Apply();var actual=pixels.GetPixels();
                        int covered=0,wrong=0;
                        for(int i=0;i<actual.Length;i++)
                        {
                            if(actual[i].a>.9f)covered++;
                            if(flipped!=0 && (actual[i]-reference[eye][i]).maxColorComponent>.001f)wrong++;
                            if(flipped!=0 && (reference[eye][i]-actual[i]).maxColorComponent>.001f)wrong++;
                        }
                        Assert.Greater(covered,30,"Cloud raymarch must cover real pixels in eye "+eye);
                        if(flipped==0)reference[eye]=actual;
                        else Assert.AreEqual(0,wrong,"Color projection changed provider cloud coverage/motion, eye "+eye);
                    }
                }
            }
            finally
            {UnityEditor.ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previousTarget;cmd.Release();rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(pixels);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);}
        }
        [TestCase(72f,true)] [TestCase(71.99f,true)] [TestCase(36f,false)]
        [TestCase(90f,false)] [TestCase(0f,false)] [TestCase(float.NaN,false)]
        public void RefreshGateValidatesPhysicalDisplayFrequency(float physicalHz,bool supported)
            => Assert.AreEqual(supported,ProductSpaceWarpController.IsSupportedDisplayFrequency(physicalHz));
        [Test]
        public void AltitudeGateClimbsDescendsAndRearmsWithoutThresholdChatter()
        {
            var state = new QuestFlightLab.Flight.Backends.FlightDynamicsState();
            bool eligible = false;
            double[] feet = {5288, 5499, 5500, 5499, 5460, 5450, 5449, 5499, 5501};
            bool[] expected = {false, false, true, true, true, true, false, false, true};
            for(int i=0;i<feet.Length;i++)
            {
                state.altitudeMslMeters = feet[i] / QuestFlightLab.Flight.Backends.FlightFrameConversions.MetersToFeet;
                state.altitudeAglMeters = 10; // Deliberately different: policy is MSL.
                eligible = ProductSpaceWarpController.IsFlightAltitudeEligible(eligible,true,true,true,state);
                Assert.AreEqual(expected[i],eligible,"MSL altitude "+feet[i]);
            }
        }
        [TestCase(false,true,true,false)]
        [TestCase(true,false,true,false)]
        [TestCase(true,true,false,false)]
        [TestCase(true,true,true,true)]
        public void AltitudeGateHonorsPreferenceModeNativeReadinessAndGround(bool requested,bool flightMode,bool initialized,bool grounded)
        {
            var state = new QuestFlightLab.Flight.Backends.FlightDynamicsState
            { altitudeMslMeters=2500,weightOnWheels=grounded };
            Assert.IsFalse(ProductSpaceWarpController.IsFlightAltitudeEligible(true,requested,flightMode,initialized,state));
            // Removing an inhibition inside the dead band must not re-enable it.
            state.weightOnWheels=false;
            state.altitudeMslMeters=5475 / QuestFlightLab.Flight.Backends.FlightFrameConversions.MetersToFeet;
            Assert.IsFalse(ProductSpaceWarpController.IsFlightAltitudeEligible(false,true,true,true,state));
        }
        [Test]
        public void AltitudeGateFailsClosedOnInvalidStateAndSupportsAirborneLessonStart()
        {
            var state = new QuestFlightLab.Flight.Backends.FlightDynamicsState { altitudeMslMeters=2500 };
            Assert.IsTrue(ProductSpaceWarpController.IsFlightAltitudeEligible(false,true,true,true,state));
            state.velocityUnityMetersPerSecond=new Vector3(float.NaN,0,0);
            Assert.IsFalse(ProductSpaceWarpController.IsFlightAltitudeEligible(true,true,true,true,state));
            state.velocityUnityMetersPerSecond=Vector3.zero;state.altitudeMslMeters=double.PositiveInfinity;
            Assert.IsFalse(ProductSpaceWarpController.IsFlightAltitudeEligible(true,true,true,true,state));
        }
        [TestCase("Universal Render Pipeline/Lit")]
        [TestCase("QuestFlight/Ambient Aircraft Exterior")]
        [TestCase("QuestFlight/Full Map Building Clipmap")]
        [TestCase("QuestFlight/Photoreal Aerial Surface V1/Cockpit Stable Interior")]
        [TestCase("QuestFlight/Cockpit/Instrument V2 Batched Opaque")]
        public void MotionPassUsesIndependentMatricesForBothEyes(string shaderName)
        {
            bool asynchronous=UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation=false;
            var mat=new Material(Shader.Find(shaderName));mat.SetFloat("_Cull",0);
            var mesh=new Mesh { vertices=new[]{new Vector3(-.8f,-.8f,.5f),new Vector3(.8f,-.8f,.5f),new Vector3(.8f,.8f,.5f),new Vector3(-.8f,.8f,.5f)},triangles=new[]{0,1,2,0,2,3,2,1,0,3,2,0} };
            var rt=new RenderTexture(32,32,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)
            {dimension=TextureDimension.Tex2DArray,volumeDepth=2};
            var pixels=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);
            var cmd=new CommandBuffer();var previous=RenderTexture.active;
            try
            {
                rt.Create();cmd.SetRenderTarget(rt,0,CubemapFace.Unknown,-1);cmd.ClearRenderTarget(true,true,Color.clear);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);
                XRBuiltinShaderConstants.UpdateBuiltinShaderConstants(Matrix4x4.identity,Matrix4x4.identity,false,0);
                XRBuiltinShaderConstants.UpdateBuiltinShaderConstants(Matrix4x4.identity,Matrix4x4.identity,false,1);
                XRBuiltinShaderConstants.SetBuiltinShaderConstants(cmd);
                var current=new[]{Matrix4x4.identity,Matrix4x4.identity};
                cmd.SetGlobalMatrixArray("unity_StereoMatrixVP",current);
                cmd.SetGlobalMatrixArray("unity_StereoMatrixP",current);
                cmd.SetGlobalMatrixArray("unity_StereoMatrixV",current);
                cmd.SetGlobalMatrixArray("_QFInverseViewProjectionStereo",current);
                cmd.SetGlobalMatrixArray("_NonJitteredViewProjMatrixStereo",current);
                cmd.SetGlobalMatrixArray("_PrevViewProjMatrixStereo",new[]{Matrix4x4.Translate(new Vector3(-.2f,0,0)),Matrix4x4.Translate(new Vector3(.2f,0,0))});
                cmd.SetGlobalMatrix("unity_MatrixPreviousM",Matrix4x4.identity);
                cmd.SetGlobalVector("unity_MotionVectorsParams",new Vector4(0,1,0,0));
                cmd.SetGlobalFloat("_QFResetMotion",0);cmd.SetGlobalFloat("_QFResetObjectMotion",0);
                cmd.SetGlobalFloat("_QFPreviousVertices",0);cmd.SetGlobalFloat("_QFHasPreviousInstances",0);
                cmd.SetSinglePassStereo(SinglePassStereoMode.Instancing);
                cmd.EnableShaderKeyword("STEREO_INSTANCING_ON");
                cmd.DrawMesh(mesh,Matrix4x4.identity,mat,0,mat.FindPass("XRMotionVectors"));
                cmd.DisableShaderKeyword("STEREO_INSTANCING_ON");
                cmd.SetSinglePassStereo(SinglePassStereoMode.None);Graphics.ExecuteCommandBuffer(cmd);
                for(int eye=0;eye<2;eye++)
                {
                    Graphics.SetRenderTarget(rt,0,CubemapFace.Unknown,eye);
                    pixels.ReadPixels(new Rect(0,0,32,32),0,0);pixels.Apply();var sample=pixels.GetPixel(16,16);
                    TestContext.WriteLine($"{shaderName} eye={eye} motion={sample}");
                    Assert.That(sample.a,Is.GreaterThan(.9f),"Missing motion coverage for eye "+eye);
                    Assert.That(sample.r,Is.EqualTo(eye==0?.2f:-.2f).Within(.005f),"Wrong motion matrix for eye "+eye);
                }
            }
            finally {UnityEditor.ShaderUtil.allowAsyncCompilation=asynchronous;RenderTexture.active=previous;cmd.Release();rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(pixels);Object.DestroyImmediate(mat);Object.DestroyImmediate(mesh);}
        }
        [OneTimeSetUp] public void InitializeRenderPipeline()
        {
            // ShaderGraph selects its URP subshader only after the pipeline has
            // initialized; isolated test runs must not depend on another test's camera.
            var go = new GameObject("Motion test pipeline initialization");
            var camera = go.AddComponent<Camera>();
            var target = new RenderTexture(16, 16, 24);
            try { camera.targetTexture = target; ProductRenderCapture.Render(camera); }
            finally { Object.DestroyImmediate(go); target.Release(); Object.DestroyImmediate(target); }
        }
        [Test] public void DeformationHistorySeedsNewMeshesAndResetsDiscontinuities()
        {
            var mesh=new Mesh();var history=new ProductDeformedMeshHistory();
            try
            {
                var initial=new List<Vector3>{Vector3.zero,Vector3.right,Vector3.up};mesh.SetVertices(initial);
                history.Upload(mesh,initial);var actual=new List<Vector3>();mesh.GetUVs(4,actual);
                CollectionAssert.AreEqual(initial,actual);
                var moved=new List<Vector3>{Vector3.forward,Vector3.one,Vector3.down};
                history.Upload(mesh,moved);mesh.GetUVs(4,actual);CollectionAssert.AreEqual(initial,actual);
                ProductSpaceWarpController.InvalidateHistory();history.Upload(mesh,moved);mesh.GetUVs(4,actual);
                CollectionAssert.AreEqual(moved,actual);
            }
            finally { history.Dispose();Object.DestroyImmediate(mesh); }
        }
        [Test]
        public void AmbientHistoryTracksPresentedPoseAndResetsAtVisibilityOrSessionChanges()
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer=obj.GetComponent<Renderer>();
            var targets=new[]{renderer};
            var block=new MaterialPropertyBlock();
            using(var history=new ProductRigidRendererHistory())
            try
            {
                block.SetColor("_Tint",Color.yellow);renderer.SetPropertyBlock(block);
                obj.transform.position=new Vector3(100,20,-50);
                history.Upload(targets);history.RecordPresentedFrame(null);
                obj.transform.position+=Vector3.forward*3;
                history.Upload(targets);renderer.GetPropertyBlock(block);
                Assert.That(block.GetMatrix("_QFPreviousModel").GetColumn(3).z,Is.EqualTo(-50));
                Assert.That(block.GetColor("_Tint"),Is.EqualTo(Color.yellow));
                // Multiple presentation updates before one rendered frame must
                // not replace the pose of the last submitted frame.
                obj.transform.position+=Vector3.forward*2;history.Upload(targets);renderer.GetPropertyBlock(block);
                Assert.That(block.GetMatrix("_QFPreviousModel").GetColumn(3).z,Is.EqualTo(-50));
                renderer.enabled=false;history.Upload(targets);renderer.enabled=true;
                history.Upload(targets);renderer.GetPropertyBlock(block);
                Assert.That(block.GetMatrix("_QFPreviousModel"),Is.EqualTo(renderer.localToWorldMatrix));
                history.RecordPresentedFrame(null);obj.transform.position+=Vector3.right*10;
                ProductSpaceWarpController.InvalidateHistory();history.Upload(targets);renderer.GetPropertyBlock(block);
                Assert.That(block.GetMatrix("_QFPreviousModel"),Is.EqualTo(renderer.localToWorldMatrix));
                history.Reset();history.Upload(targets);renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_QFHasPreviousModel"),Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(obj); }
        }
        [TestCase(0f,0f,false)] [TestCase(.2f,.2f,false)] [TestCase(-.2f,-.2f,false)] [TestCase(.2f,0f,true)]
        public void MotionPassWritesThreeDimensionalNdcDisplacement(float delta,float expected,bool reset)
            => CheckMotion("QuestFlight/StartupShowroomUnlit",new Vector3(delta,0,0),new Vector3(expected,0,0),reset);
        [TestCase(.2f,false)] [TestCase(-.2f,false)] [TestCase(.2f,true)]
        public void ShaderGraphPreservesDepthDirectionMotion(float delta,bool reset)
            => CheckMotion("Shader Graphs/glTF-pbrMetallicRoughness",new Vector3(0,0,delta),reset?Vector3.zero:new Vector3(0,0,delta),reset);
        [TestCase(.2f,false)] [TestCase(-.2f,false)] [TestCase(.2f,true)]
        public void MovingAircraftUsesPreviousObjectPose(float delta,bool reset)
            => CheckMotion("QuestFlight/Ambient Aircraft Exterior",new Vector3(delta,0,0),reset?Vector3.zero:new Vector3(delta,0,0),reset,true);
        [TestCase(0f)] [TestCase(5000f)] [TestCase(15000f)]
        public void CockpitMotionRemainsStableAwayFromWorldOrigin(float distance)
        {
            // A cockpit fixed relative to a translating aircraft/camera must
            // have zero relative motion, including at mountain-map coordinates.
            var shader=Shader.Find("QuestFlight/Ambient Aircraft Exterior");
            var mat=new Material(shader);
            var mesh=new Mesh { vertices=new[]{new Vector3(-.4f,-.4f,1),new Vector3(.4f,-.4f,1),new Vector3(.4f,.4f,1),new Vector3(-.4f,.4f,1)},triangles=new[]{0,1,2,0,2,3,2,1,0,3,2,0} };
            var rt=new RenderTexture(32,32,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var pixels=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);
            var cmd=new CommandBuffer();var previous=RenderTexture.active;
            try
            {
                Vector3 anchor=new Vector3(distance,800,-distance*.7f), step=new Vector3(.31f,.13f,-.72f);
                Matrix4x4 model=Matrix4x4.Translate(anchor), oldModel=Matrix4x4.Translate(anchor-step);
                Matrix4x4 view=Matrix4x4.Scale(new Vector3(1,1,-1))*model.inverse;
                Matrix4x4 projection=GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(85,1,.05f,30000),false);
                Matrix4x4 vp=projection*view, oldVp=projection*Matrix4x4.Scale(new Vector3(1,1,-1))*oldModel.inverse;
                rt.Create();cmd.SetRenderTarget(rt);cmd.ClearRenderTarget(true,true,Color.clear);
                cmd.SetViewProjectionMatrices(view,projection);
                cmd.SetGlobalMatrixArray("_QFInverseViewProjectionStereo",new[]{vp.inverse,vp.inverse});
                cmd.SetGlobalMatrixArray("_NonJitteredViewProjMatrixStereo",new[]{vp,vp});
                cmd.SetGlobalMatrixArray("_PrevViewProjMatrixStereo",new[]{oldVp,oldVp});
                cmd.SetGlobalMatrix("unity_MatrixPreviousM",oldModel);
                cmd.SetGlobalVector("unity_MotionVectorsParams",new Vector4(0,1,0,0));
                cmd.SetGlobalFloat("_QFResetMotion",0);cmd.SetGlobalFloat("_QFResetObjectMotion",0);
                cmd.SetGlobalFloat("_QFPreviousVertices",0);cmd.SetGlobalFloat("_QFHasPreviousInstances",0);
                cmd.DrawMesh(mesh,model,mat,0,mat.FindPass("XRMotionVectors"));Graphics.ExecuteCommandBuffer(cmd);
                RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,32,32),0,0);pixels.Apply();
                Color sample=pixels.GetPixel(16,16);
                Assert.That(sample.a,Is.GreaterThan(.9f));
                Assert.That(Mathf.Max(Mathf.Abs(sample.r),Mathf.Abs(sample.g),Mathf.Abs(sample.b)),Is.LessThan(.0005f),"Aircraft and camera co-motion must cancel: "+sample);
            }
            finally {RenderTexture.active=previous;cmd.Release();rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(pixels);Object.DestroyImmediate(mat);Object.DestroyImmediate(mesh);}
        }
        [TestCase("QuestFlight/AirportCoreV4/ContinuousRunway")]
        [TestCase("QuestFlight/Full Map Building Clipmap")]
        public void FixedWorldSurfaceKeepsCameraMotionWithUnrelatedObjectHistory(string shaderName)
            => CheckMotion(shaderName,new Vector3(.2f,0,0),new Vector3(.2f,0,0),false,false,true);

        [TestCase(0f)] [TestCase(.2f)] [TestCase(-.2f)]
        public void AmbientMotionIgnoresUnrelatedRendererHistory(float delta)
            => CheckMotion("QuestFlight/Ambient Aircraft Exterior",new Vector3(delta,0,0),new Vector3(delta,0,0),false,true,true,true);

        static void CheckMotion(string shaderName,Vector3 delta,Vector3 expected,bool reset,bool objectMotion=false,bool unrelatedHistory=false,bool explicitHistory=false)
        {
            var shader=Shader.Find(shaderName);Assert.IsNotNull(shader);
            var mat=new Material(shader);mat.SetFloat("_Cull",0);mat.SetFloat("_XRMotionVectorsPass",1);mat.SetShaderPassEnabled("XRMotionVectors",true);
            // Both windings let this identity-projection probe exercise the
            // material's authored backface culling without changing its shader.
            var mesh=new Mesh { vertices=new[]{new Vector3(-.8f,-.8f,.5f),new Vector3(.8f,-.8f,.5f),new Vector3(.8f,.8f,.5f),new Vector3(-.8f,.8f,.5f)},triangles=new[]{0,1,2,0,2,3,2,1,0,3,2,0} };
            var rt=new RenderTexture(32,32,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var pixels=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);var cmd=new CommandBuffer();var previous=RenderTexture.active;
            try
            {
                rt.Create();int pass=mat.FindPass("XRMotionVectors");Assert.GreaterOrEqual(pass,0);
                cmd.SetRenderTarget(rt);cmd.ClearRenderTarget(true,true,Color.clear);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);
                cmd.SetGlobalMatrixArray("_QFInverseViewProjectionStereo",new[]{Matrix4x4.identity,Matrix4x4.identity});
                cmd.SetGlobalMatrixArray("_NonJitteredViewProjMatrixStereo",new[]{Matrix4x4.identity,Matrix4x4.identity});
                var old=Matrix4x4.Translate(-delta);
                cmd.SetGlobalMatrixArray("_PrevViewProjMatrixStereo",new[]{objectMotion?Matrix4x4.identity:old,objectMotion?Matrix4x4.identity:old});
                cmd.SetGlobalMatrix("_NonJitteredViewProjMatrix",Matrix4x4.identity);
                cmd.SetGlobalMatrix("_PrevViewProjMatrix",objectMotion?Matrix4x4.identity:old);
                cmd.SetGlobalMatrix("unity_MatrixPreviousM",unrelatedHistory?
                    Matrix4x4.TRS(new Vector3(-626,1.7f,-30),Quaternion.Euler(0,90,0),Vector3.one*.0114f):objectMotion?old:Matrix4x4.identity);
                cmd.SetGlobalVector("unity_MotionVectorsParams",new Vector4(unrelatedHistory?1:0,1,0,0));
                cmd.SetGlobalFloat("_QFResetMotion",reset?1:0);cmd.SetGlobalFloat("_QFResetObjectMotion",0);cmd.SetGlobalFloat("_QFPreviousVertices",unrelatedHistory?1:0);
                cmd.SetGlobalFloat("_QFHasPreviousInstances",0);
                var properties=new MaterialPropertyBlock();
                properties.SetFloat("_QFHasPreviousModel",explicitHistory?1:0);
                if(explicitHistory)properties.SetMatrix("_QFPreviousModel",old);
                cmd.DrawMesh(mesh,Matrix4x4.identity,mat,0,pass,properties);Graphics.ExecuteCommandBuffer(cmd);
                RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,32,32),0,0);pixels.Apply();
                var sample=pixels.GetPixel(16,16);Assert.That(sample.a,Is.GreaterThan(.9f),"XR surface coverage missing");
                Assert.That(sample.r,Is.EqualTo(expected.x).Within(.005f));Assert.That(sample.b,Is.EqualTo(expected.z).Within(.005f));
            }
            finally {Shader.SetGlobalFloat("_QFResetMotion",0);Shader.SetGlobalFloat("_QFPreviousVertices",0);RenderTexture.active=previous;cmd.Release();rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(pixels);Object.DestroyImmediate(mat);Object.DestroyImmediate(mesh);}
        }
    }
}
