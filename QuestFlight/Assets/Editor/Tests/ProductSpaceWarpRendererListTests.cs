using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using QuestFlight.Rendering;

namespace QuestFlightLab.Tests.EditMode
{
    // Exercise engine-bound per-object history; direct DrawMesh tests cannot
    // establish that a shader consumes Unity's renderer-list constant buffers.
    public sealed class ProductSpaceWarpRendererListTests
    {
        public sealed class ProbeFeature : ScriptableRendererFeature
        {
            public RTHandle target;
            public Camera probeCamera;
            public int recordedFrames;
            Matrix4x4 previousViewProjection;
            bool hasPreviousView;
            ProbePass pass;
            public override void Create() { pass = new ProbePass(this) { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques }; }
            public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
            { if (data.cameraData.camera == probeCamera) renderer.EnqueuePass(pass); }
            sealed class ProbePass : ScriptableRenderPass
            {
                readonly ProbeFeature owner;
                public ProbePass(ProbeFeature owner) { this.owner = owner; }
                sealed class Data { public RendererListHandle list; public Matrix4x4 vp, previous; }
                public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
                {
                    owner.recordedFrames++;
                    var camera = frame.Get<UniversalCameraData>();
                    using var builder = graph.AddRasterRenderPass<Data>("Production renderer-list history probe", out var data);
                    var target = graph.ImportTexture(owner.target);
                    builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                    var depth = graph.CreateTexture(new TextureDesc(owner.target.rt.width, owner.target.rt.height) { depthBufferBits = DepthBits.Depth24, name = "Probe depth" });
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                    var drawing = new DrawingSettings(new ShaderTagId("XRMotionVectors"), new SortingSettings(camera.camera))
                    { perObjectData = PerObjectData.MotionVectors, enableInstancing = false, enableDynamicBatching = false };
                    var filter = new FilteringSettings(RenderQueueRange.opaque, 1 << 31) { forceAllMotionVectorObjects = true };
                    data.list = graph.CreateRendererList(new RendererListParams(frame.Get<UniversalRenderingData>().cullResults, drawing, filter));
                    data.vp = GL.GetGPUProjectionMatrix(camera.camera.projectionMatrix, true) * camera.camera.worldToCameraMatrix;
                    data.previous = owner.hasPreviousView ? owner.previousViewProjection : data.vp;
                    owner.previousViewProjection = data.vp; owner.hasPreviousView = true;
                    builder.UseRendererList(data.list);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((Data d, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.ClearRenderTarget(true, true, Color.clear);
                        ctx.cmd.SetGlobalMatrixArray("_NonJitteredViewProjMatrixStereo", new[] { d.vp, d.vp });
                        ctx.cmd.SetGlobalMatrixArray("_PrevViewProjMatrixStereo", new[] { d.previous, d.previous });
                        ctx.cmd.SetGlobalMatrix("_NonJitteredViewProjMatrix", d.vp);
                        ctx.cmd.SetGlobalMatrix("_PrevViewProjMatrix", d.previous);
                        ctx.cmd.SetGlobalFloat("_QFResetMotion", 0);
                        ctx.cmd.SetGlobalFloat("_QFResetObjectMotion", 0);
                        ctx.cmd.SetGlobalFloat("_QFPreviousVertices", 0);
                        ctx.cmd.SetGlobalFloat("_QFHasPreviousInstances", 0);
                        ctx.cmd.SetGlobalFloat("_SpaceWarpNDCModifier", 1);
                        // Deliberately DO NOT set unity_MatrixPreviousM or
                        // unity_MotionVectorsParams. The engine owns them.
                        ctx.cmd.DrawRendererList(d.list);
                    });
                }
            }
        }

        static IEnumerator WaitForRender(ProbeFeature feature)
        {
            int before = feature.recordedFrames;
            double deadline = Time.realtimeSinceStartupAsDouble + 10;
            do
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Probe camera did not render a new frame");
                yield return null;
            } while (feature.recordedFrames == before);
        }

        [UnityTest]
        public IEnumerator ProductionRendererHistoryMatchesStationaryAndComovingCamera()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            yield return new EnterPlayMode();
            var oldDefault = GraphicsSettings.defaultRenderPipeline;
            var oldQuality = QualitySettings.renderPipeline;
            bool oldAsync = UnityEditor.ShaderUtil.allowAsyncCompilation;
            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            var feature = ScriptableObject.CreateInstance<ProbeFeature>();
            data.rendererFeatures.Add(feature);
            var pipeline = UniversalRenderPipelineAsset.Create(data);
            pipeline.useSRPBatcher = false;
            var output = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGBFloat);
            var color = new RenderTexture(64, 64, 24);
            var pixels = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            var cameraObject = new GameObject("History probe camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = true; camera.cullingMask = 1 << 31;
            camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            camera.transform.position = new Vector3(5000, 800, -3000);
            camera.targetTexture = color;
            feature.probeCamera = camera;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.layer = 31; cube.transform.position = camera.transform.position + Vector3.forward * 3;
            var renderer = cube.GetComponent<MeshRenderer>();
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            Material material = null, stationaryMaterial = null;
            GameObject stationaryA = null, stationaryB = null;
            ProductRigidRendererHistory rigidHistory = null;
            var oldTarget = RenderTexture.active;
            try
            {
                output.Create(); color.Create(); feature.target = RTHandles.Alloc(output);
                UnityEditor.ShaderUtil.allowAsyncCompilation = false;
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                // Moving materials retain object history. Fixed world materials
                // have a separate adversarial camera-motion test; moving their
                // object transform would violate that material's contract.
                foreach (string shader in new[] { "Shader Graphs/glTF-pbrMetallicRoughness", "Universal Render Pipeline/Lit", "QuestFlight/Photoreal Aerial Surface V1/Cockpit Stable Interior", "QuestFlight/Cockpit/Instrument V2 Batched Opaque", "QuestFlight/Ambient Aircraft Exterior" })
                {
                    material = new Material(Shader.Find(shader));
                    // This fixture measures geometry/history, not atlas alpha.
                    // Explicit textures prevent global/default bindings from
                    // supplying an unrelated atlas to the probe material.
                    if (material.HasProperty("_SourceTex")) material.SetTexture("_SourceTex", Texture2D.whiteTexture);
                    if (material.HasProperty("_PresentationTex")) material.SetTexture("_PresentationTex", Texture2D.whiteTexture);
                    material.SetShaderPassEnabled("XRMotionVectors", true);
                    renderer.sharedMaterial = material;
                    for (int i = 0; i < 6; i++) yield return WaitForRender(feature);
                    RenderTexture.active = output;
                    pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
                    var value = pixels.GetPixel(32, 32);
                    TestContext.WriteLine(shader + " engine-bound stationary motion=" + value);
                    Assert.That(value.a, Is.GreaterThan(.9f), shader + " coverage");
                    Assert.That(float.IsNaN(value.r) || float.IsNaN(value.g) || float.IsNaN(value.b), Is.False, shader + " non-finite motion");
                    Assert.That(Mathf.Max(Mathf.Abs(value.r), Mathf.Abs(value.g), Mathf.Abs(value.b)), Is.LessThan(.002f), shader + " stationary renderer must not move");
                    // Exercise sustained history with real Transform updates.
                    // Relative cockpit pose is constant despite world travel.
                    for (int sample = 0; sample < 12; sample++)
                    {
                        var step = new Vector3(.37f, .09f, -.26f);
                        camera.transform.position += step; cube.transform.position += step;
                        var pivot = camera.transform.position + camera.transform.forward * .3f;
                        var rotation = Quaternion.Euler(.8f,1.5f,-.6f);
                        cube.transform.SetPositionAndRotation(pivot + rotation * (cube.transform.position-pivot), rotation*cube.transform.rotation);
                        camera.transform.SetPositionAndRotation(pivot + rotation * (camera.transform.position-pivot), rotation*camera.transform.rotation);
                        yield return WaitForRender(feature);
                        RenderTexture.active = output;
                        pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
                        value = pixels.GetPixel(32, 32);
                        Assert.That(value.a, Is.GreaterThan(.9f), shader + " moving coverage");
                        Assert.That(float.IsNaN(value.r) || float.IsNaN(value.g) || float.IsNaN(value.b), Is.False);
                        Assert.That(Mathf.Max(Mathf.Abs(value.r), Mathf.Abs(value.g), Mathf.Abs(value.b)), Is.LessThan(.002f), shader + " world motion must cancel camera motion: " + value);
                    }
                    TestContext.WriteLine(shader + " sustained translation/rotation co-motion PASS (12 rendered frames)");
                    Object.DestroyImmediate(material); material = null;
                }
                // Two parked aircraft alongside a moving cockpit exercise the
                // production actor's explicit presented-transform history.
                material = new Material(Shader.Find("QuestFlight/Photoreal Aerial Surface V1/Cockpit Stable Interior"));
                material.renderQueue = 2000; renderer.sharedMaterial = material;
                camera.transform.rotation = Quaternion.identity; cube.transform.rotation = Quaternion.identity;
                cube.transform.position = camera.transform.position + new Vector3(.9f, 0, 3);
                stationaryMaterial = new Material(Shader.Find("QuestFlight/Ambient Aircraft Exterior"));
                stationaryMaterial.renderQueue = 2001;
                stationaryA = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stationaryB = GameObject.CreatePrimitive(PrimitiveType.Cube);
                foreach (var stationary in new[] { stationaryA, stationaryB })
                {
                    stationary.layer = 31;
                    stationary.transform.localScale = Vector3.one * .6f;
                    var stationaryRenderer = stationary.GetComponent<MeshRenderer>();
                    stationaryRenderer.sharedMaterial = stationaryMaterial;
                    stationaryRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                }
                stationaryA.transform.position = camera.transform.position + new Vector3(-.9f, 0, 3);
                stationaryB.transform.position = camera.transform.position + new Vector3(-.9f, 1, 3);
                rigidHistory = new ProductRigidRendererHistory();
                var parkedRenderers = new Renderer[] { stationaryA.GetComponent<MeshRenderer>(), stationaryB.GetComponent<MeshRenderer>() };
                rigidHistory.Upload(parkedRenderers);
                for (int i = 0; i < 6; i++) yield return WaitForRender(feature);
                Vector3 samplePoint = camera.WorldToViewportPoint(stationaryA.transform.position);
                for (int sample = 0; sample < 12; sample++)
                {
                    cube.transform.position += Vector3.right * .002f;
                    cube.transform.Rotate(0, 2, 0);
                    rigidHistory.Upload(parkedRenderers);
                    yield return WaitForRender(feature);
                    rigidHistory.RecordPresentedFrame(camera);
                    RenderTexture.active = output;
                    pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
                    var value = pixels.GetPixel(Mathf.FloorToInt(samplePoint.x * 64), Mathf.FloorToInt(samplePoint.y * 64));
                    if (value.a < .9f)
                    {
                        string coverage = "";
                        for (int x = 0; x < 64; x++) if (pixels.GetPixel(x,32).a > .9f) coverage += x + ",";
                        TestContext.WriteLine("Parked probe viewport=" + samplePoint.ToString("F4") + " center-row coverage=" + coverage +
                            " object=" + stationaryA.transform.position + " camera=" + camera.transform.position);
                    }
                    Assert.That(value.a, Is.GreaterThan(.9f), "Parked aircraft coverage");
                    Assert.That(Mathf.Max(Mathf.Abs(value.r), Mathf.Abs(value.g), Mathf.Abs(value.b)), Is.LessThan(.002f),
                        "A parked aircraft must not inherit the moving renderer's model history: " + value);
                }
                TestContext.WriteLine("Mixed moving/parked renderer history PASS (12 rendered frames)");
            }
            finally
            {
                RenderTexture.active = oldTarget;
                GraphicsSettings.defaultRenderPipeline = oldDefault; QualitySettings.renderPipeline = oldQuality;
                UnityEditor.ShaderUtil.allowAsyncCompilation = oldAsync;
                rigidHistory?.Dispose();
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(cube);
                if (stationaryA != null) Object.DestroyImmediate(stationaryA);
                if (stationaryB != null) Object.DestroyImmediate(stationaryB);
                if (stationaryMaterial != null) Object.DestroyImmediate(stationaryMaterial);
                if (material != null) Object.DestroyImmediate(material);
                feature.target?.Release(); output.Release(); color.Release();
                Object.DestroyImmediate(output); Object.DestroyImmediate(color); Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(pipeline); Object.DestroyImmediate(feature); Object.DestroyImmediate(data);
            }
        }
        [UnityTest]
        public IEnumerator AuthoredStrutsRetainCoverageAndCancelAircraftMotion()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            yield return new EnterPlayMode();
            string directory = System.Environment.GetEnvironmentVariable("QF_STRUT_QA");
            if (string.IsNullOrEmpty(directory)) Assert.Ignore("External evidence directory required");
            System.IO.Directory.CreateDirectory(directory);
            var prefab = UnityEditor.PrefabUtility.LoadPrefabContents("Assets/Production/Prefabs/ProductionC172AircraftRig.prefab");
            var meshes = new Mesh[2]; var materials = new Material[2];
            var positions = new Vector3[2]; var rotations = new Quaternion[2]; var scales = new Vector3[2];
            Vector3 eye;
            try
            {
                eye = prefab.GetComponent<QuestFlightLab.Runtime.AircraftReferenceFrameRig>().PilotSeatAnchor.position;
                var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                for (int i=0;i<2;i++)
                {
                    string name = "Cessna_Exterior_Body_MAT_0.00"+(i==0?"3":"5")+"_Body_MAT_0";
                    var source = System.Array.Find(filters, f=>f.name==name);
                    Assert.NotNull(source,name);
                    meshes[i]=source.sharedMesh;materials[i]=source.GetComponent<Renderer>().sharedMaterial;
                    positions[i]=source.transform.position;rotations[i]=source.transform.rotation;scales[i]=source.transform.lossyScale;
                }
            }
            finally { UnityEditor.PrefabUtility.UnloadPrefabContents(prefab); }
            var oldDefault=GraphicsSettings.defaultRenderPipeline; var oldQuality=QualitySettings.renderPipeline;
            var data=ScriptableObject.CreateInstance<UniversalRendererData>();
            var feature=ScriptableObject.CreateInstance<ProbeFeature>();data.rendererFeatures.Add(feature);
            var pipeline=UniversalRenderPipelineAsset.Create(data);pipeline.useSRPBatcher=false;
            var output=new RenderTexture(420,440,0,RenderTextureFormat.ARGBFloat);
            var color=new RenderTexture(420,440,24);var pixels=new Texture2D(420,440,TextureFormat.RGBAFloat,false,true);
            var cameraObject=new GameObject("Strut history camera");var camera=cameraObject.AddComponent<Camera>();
            camera.cullingMask=1<<31;camera.nearClipPlane=.03f;camera.farClipPlane=24000;camera.fieldOfView=85;
            camera.depthTextureMode=DepthTextureMode.Depth|DepthTextureMode.MotionVectors;camera.targetTexture=color;feature.probeCamera=camera;
            var strut=new GameObject("Actual authored strut");strut.layer=31;
            var filter=strut.AddComponent<MeshFilter>();var renderer=strut.AddComponent<MeshRenderer>();
            renderer.motionVectorGenerationMode=MotionVectorGenerationMode.Object;
            var oldTarget=RenderTexture.active;bool oldAsync=UnityEditor.ShaderUtil.allowAsyncCompilation;
            try
            {
                UnityEditor.ShaderUtil.allowAsyncCompilation=false;
                output.Create();color.Create();feature.target=RTHandles.Alloc(output);
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                for(int side=0;side<2;side++)for(int eyeIndex=0;eyeIndex<2;eyeIndex++)
                {
                    filter.sharedMesh=meshes[side];renderer.sharedMaterial=materials[side];
                    strut.transform.SetPositionAndRotation(positions[side],rotations[side]);strut.transform.localScale=scales[side];
                    camera.transform.position=eye+Vector3.right*(eyeIndex==0?-.032f:.032f);
                    camera.transform.LookAt(renderer.bounds.center);
                    for(int warm=0;warm<6;warm++)yield return WaitForRender(feature);
                    for(int frame=0;frame<12;frame++)
                    {
                        // Taxi acceleration followed by climb; both remain rigid relative to the pilot.
                        var step=new Vector3(.02f*frame,frame<6?0:.08f,.08f*frame);
                        camera.transform.position+=step;strut.transform.position+=step;
                        yield return WaitForRender(feature);
                        RenderTexture.active=output;pixels.ReadPixels(new Rect(0,0,420,440),0,0);pixels.Apply();
                        var samples=pixels.GetPixels();int covered=0;float maxMotion=0;
                        foreach(var sample in samples)if(sample.a>.9f)
                        {covered++;Assert.IsFalse(float.IsNaN(sample.r)||float.IsNaN(sample.g)||float.IsNaN(sample.b));maxMotion=Mathf.Max(maxMotion,Mathf.Abs(sample.r),Mathf.Abs(sample.g),Mathf.Abs(sample.b));}
                        Assert.Greater(covered,50,"Actual strut missing from motion buffer");
                        Assert.Less(maxMotion,.0011f,"Rigid strut co-motion exceeds a quarter provider pixel");
                        if(frame==0||frame==11)System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory,$"strut{side}_eye{eyeIndex}_frame{frame}.exr"),pixels.EncodeToEXR());
                        TestContext.WriteLine($"strut={side} eye={eyeIndex} frame={frame} covered={covered} maxMotion={maxMotion:R}");
                    }
                }
            }
            finally
            {
                RenderTexture.active=oldTarget;UnityEditor.ShaderUtil.allowAsyncCompilation=oldAsync;
                GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;
                Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(strut);feature.target?.Release();output.Release();color.Release();
                Object.DestroyImmediate(output);Object.DestroyImmediate(color);Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(pipeline);Object.DestroyImmediate(feature);Object.DestroyImmediate(data);
            }
        }
        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}


