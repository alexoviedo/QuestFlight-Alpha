using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor.Tests
{
    public sealed class FullMapBuildingClipmapEditModeTests
    {
        [Test]
        public void FacadeDetailCellsUseGeneratorTopDownCoordinates()
        {
            var texture = new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(FullMapBuildingClipmapAssetAuthoring.DetailPath)));
                for(int style=0;style<6;style++) foreach(int offset in new[]{0,8})
                {
                    int cell=offset+style;
                    float u=(cell%4+.5f)*.25f;
                    float v=(3-cell/4+.5f)*.25f;
                    Assert.Greater(texture.GetPixelBilinear(u,v).a,.99f,$"Missing detail cell {cell}");
                }
                string shader=File.ReadAllText("Assets/Shaders/FullMapBuildingClipmap.shader");
                StringAssert.Contains("3.0h - floor(cell * 0.25h)",shader);
                StringAssert.Contains("return (cellOrigin + safeUv) * 0.25;",shader);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        [TestCase(32,7,false)] [TestCase(128,7,false)]
        [TestCase(32,3,true)] [TestCase(128,3,true)]
        [TestCase(32,4,true)] [TestCase(128,4,true)]
        [TestCase(32,7,true)]
        public void DistantFacadeTemporalSamplingEvidence(int resolution, int materialId, bool fullMaterial)
        {
            string output = System.Environment.GetEnvironmentVariable("QF_BUILDING_FLICKER_QA");
            if (string.IsNullOrEmpty(output)) Assert.Ignore("Explicit external evidence directory required.");
            output = Path.Combine(output,fullMaterial ? $"full_{materialId}_{resolution}" : resolution.ToString());
            Directory.CreateDirectory(output);
            var initObject = new GameObject("Facade probe pipeline initialization");
            var initCamera = initObject.AddComponent<Camera>();
            var initTarget = new RenderTexture(16,16,24);
            try { initCamera.targetTexture=initTarget;QuestFlight.Rendering.ProductRenderCapture.Render(initCamera); }
            finally { UnityEngine.Object.DestroyImmediate(initObject);initTarget.Release();UnityEngine.Object.DestroyImmediate(initTarget); }
            bool asynchronous = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var material = new Material(AssetDatabase.LoadAssetAtPath<Material>(FullMapBuildingClipmapAssetAuthoring.MaterialPath));
            string source = File.ReadAllText("Assets/Shaders/FullMapBuildingClipmap.shader");
            string controlSource = source.Replace("Shader \"QuestFlight/Full Map Building Clipmap\"", "Shader \"Hidden/QuestFlight/FacadeNoWindowsProbe\"")
                .Replace("\"AerialClipmapSampling.cginc\"", "\"Assets/Shaders/AerialClipmapSampling.cginc\"")
                .Replace("return horizontal * vertical * withinWall * validFloor * hasCompleteBay * allowed;", "return 0;");
            Assert.AreNotEqual(source, controlSource);
            var controlShader = ShaderUtil.CreateShaderAsset(controlSource);
            var control = new Material(material) { shader = controlShader };
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-1,-1,.5f),new Vector3(1,-1,.5f),new Vector3(1,1,.5f),new Vector3(-1,1,.5f) };
            mesh.triangles = new[] {0,1,2,0,2,3,2,1,0,3,2,0};
            mesh.normals = Enumerable.Repeat(Vector3.back,4).ToArray();
            mesh.tangents = Enumerable.Repeat(new Vector4(1,0,0,1),4).ToArray();
            mesh.colors = Enumerable.Repeat(Color.white,4).ToArray();
            // A 98.2m residential facade has approximately one bay per pixel
            // in the distant case; 128px also checks resolved, nearby detail.
            mesh.SetUVs(1, Enumerable.Repeat(new Vector2(98.2f,1),4).ToList());
            mesh.SetUVs(2, Enumerable.Repeat(new Vector4(0,materialId,30,fullMaterial?3:0),4).ToList());
            var rt = new RenderTexture(resolution,resolution,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(resolution,resolution,TextureFormat.RGBAFloat,false,true);
            var oldTarget = RenderTexture.active;
            var wall = new GameObject("Isolated facade sampling wall");wall.layer=31;
            wall.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=wall.AddComponent<MeshRenderer>();
            var cameraObject=new GameObject("Facade evidence camera");var camera=cameraObject.AddComponent<Camera>();
            camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;
            camera.nearClipPlane=.1f;camera.farClipPlane=10;camera.cullingMask=1<<31;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.targetTexture=rt;
            var noNormals = new Material(material); noNormals.SetFloat("_NormalScale",0);
            var noReflection = new Material(material); noReflection.SetFloat("_WindowReflection",0);
            var variants = fullMaterial ? new[]{material,noNormals,noReflection,control} : new[]{material,control};
            var oldSun = RenderSettings.sun;
            var sunObject = new GameObject("Facade diagnostic sun");
            var sun = sunObject.AddComponent<Light>(); sun.type=LightType.Directional;
            sun.intensity=1; sun.transform.rotation=Quaternion.Euler(8,8,0);
            if(fullMaterial) RenderSettings.sun=sun; else sun.enabled=false;
            var values = new double[variants.Length,2,24];
            try
            {
                rt.Create();
                if(!fullMaterial) foreach(var mat in variants)
                {mat.enableInstancing=false;mat.SetFloat("_AmbientLift",.7f);mat.SetFloat("_NormalScale",0);mat.SetFloat("_WindowReflection",0);mat.SetFloat("_CornerSofteningStrength",0);}
                for(int variant=0;variant<variants.Length;variant++)for(int phase=0;phase<24;phase++)
                {
                    float offset=phase*3.0f/24;
                    mesh.uv=new[]{new Vector2(offset,0),new Vector2(98.2f+offset,0),new Vector2(98.2f+offset,30),new Vector2(offset,30)};
                    renderer.sharedMaterial=variants[variant];
                    for(int eye=0;eye<2;eye++)
                    {
                        camera.transform.position=new Vector3(eye==0?-.01f:.01f,0,-5);
                        QuestFlight.Rendering.ProductRenderCapture.Render(camera);
                        RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,resolution,resolution),0,0);pixels.Apply();
                        double sum=0;int count=0;
                        for(int y=resolution/4;y<resolution*3/4;y++)for(int x=resolution/4;x<resolution*3/4;x++){var c=pixels.GetPixel(x,y);Assert.Greater(c.a,.9f,$"Missing coverage variant={variant} eye={eye} phase={phase} pixel={x},{y}");sum+=(c.r+c.g+c.b)/3;count++;}
                        values[variant,eye,phase]=sum/count;
                        File.WriteAllBytes(Path.Combine(output,$"variant{variant}_eye{eye}_phase{phase:D2}.png"),pixels.EncodeToPNG());
                    }
                }
                var report=new List<string>{"variant,eye,phase,mean_luminance"};
                for(int variant=0;variant<variants.Length;variant++)for(int eye=0;eye<2;eye++)for(int phase=0;phase<24;phase++)
                    report.Add($"{variant},{eye},{phase},{values[variant,eye,phase].ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                File.WriteAllLines(Path.Combine(output,"sampling.csv"),report);
                Assert.IsFalse(ShaderUtil.ShaderHasError(material.shader));
                Assert.IsFalse(ShaderUtil.ShaderHasError(controlShader));
                if(!fullMaterial) for(int eye=0;eye<2;eye++)
                {
                    var rendered=Enumerable.Range(0,24).Select(p=>values[0,eye,p]).ToArray();
                    var suppressed=Enumerable.Range(0,24).Select(p=>values[1,eye,p]).ToArray();
                    Assert.Greater(suppressed.Average()-rendered.Average(),.02,"Keep real window coverage instead of hiding windows.");
                    if(resolution==32)
                        Assert.Less(rendered.Max()-rendered.Min(),.001,"Unresolved windows must not flash under subpixel movement.");
                }
            }
            finally
            {RenderSettings.sun=oldSun;UnityEngine.Object.DestroyImmediate(sunObject);UnityEngine.Object.DestroyImmediate(noNormals);UnityEngine.Object.DestroyImmediate(noReflection);ShaderUtil.allowAsyncCompilation=asynchronous;RenderTexture.active=oldTarget;UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(wall);rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(control);UnityEngine.Object.DestroyImmediate(controlShader);}
        }
        [TestCase(950f)]
        [TestCase(-100f)]
        public void GroundingRejectsGrossAbsoluteElevationErrors(float sourceBase)
        {
            var terrain = ScriptableObject.CreateInstance<ProductionTerrainSurfaceAsset>();
            try
            {
                terrain.layers = new[] { new ProductionTerrainSurfaceAsset.GridLayer {
                    stableId = "grounding_test", width = 2, height = 2, spacingMeters = 100,
                    minimumEnuMeters = Vector2.zero, heightMeters = new[] { 350f, 350f, 350f, 350f } } };
                var ring = new[] { new Vector2(10,10), new Vector2(30,10), new Vector2(30,30), new Vector2(10,30) };
                float corrected = FullMapBuildingClipmapAssetAuthoring.ResolveTerrainGroundedBase(terrain, ring, sourceBase, 6);
                Assert.That(corrected, Is.InRange(348.5f,351.5f));
                Assert.That(FullMapBuildingClipmapAssetAuthoring.ResolveTerrainGroundedBase(terrain, ring, 350.4f, 6), Is.EqualTo(350.4f));
            }
            finally { UnityEngine.Object.DestroyImmediate(terrain); }
        }

        [Test]
        public void SteepFootprintKeepsLevelEaveAboveTerrain()
        {
            var terrain = ScriptableObject.CreateInstance<ProductionTerrainSurfaceAsset>();
            try
            {
                terrain.layers = new[] { new ProductionTerrainSurfaceAsset.GridLayer {
                    stableId = "grounding_test", width = 2, height = 2, spacingMeters = 100,
                    minimumEnuMeters = Vector2.zero, heightMeters = new[] { 300f, 400f, 300f, 400f } } };
                var ring = new[] { new Vector2(10,10), new Vector2(30,10), new Vector2(30,30), new Vector2(10,30) };
                float datum = FullMapBuildingClipmapAssetAuthoring.ResolveTerrainGroundedBase(terrain, ring, 950, 6);
                Assert.That(datum + 6, Is.EqualTo(330.5f).Within(0.01f));
            }
            finally { UnityEngine.Object.DestroyImmediate(terrain); }
        }

        [Test]
        public void ValmontPreservesLargeFlatSourceOutlineWithoutChangingOutsideOrPitchedBuildings()
        {
            Type authoring = typeof(FullMapBuildingClipmapAssetAuthoring);
            Type buildingType = authoring.GetNestedType("Building", BindingFlags.NonPublic);
            object building = Activator.CreateInstance(buildingType, true);
            buildingType.GetField("area_m2").SetValue(building, 1500f);
            MethodInfo compile = authoring.GetMethod("CompileStableFootprint", BindingFlags.Static | BindingFlags.NonPublic);
            Vector2[] ring = Enumerable.Range(0, 12).Select(i => new Vector2(
                -500f + 25f * Mathf.Cos(i * Mathf.PI / 6f), -700f + 25f * Mathf.Sin(i * Mathf.PI / 6f))).ToArray();
            Vector2[] Run(Vector2[] points) => (Vector2[])compile.Invoke(null, new object[] { points, building });
            Assert.That(Run(ring), Is.EqualTo(ring), "Preserve the recorded perimeter in the bounded patch.");
            Assert.That(Run(ring.Select(p => p + new Vector2(2000, 0)).ToArray()).Length, Is.LessThan(ring.Length));
            buildingType.GetField("roof_id").SetValue(building, 1);
            Assert.That(Run(ring).Length, Is.LessThan(ring.Length), "Do not change pitched roof allocation inputs.");
            buildingType.GetField("roof_id").SetValue(building, 0);
            buildingType.GetField("area_m2").SetValue(building, 500f);
            Assert.That(Run(ring).Length, Is.LessThan(ring.Length), "Small buildings retain the existing policy.");
        }

        [Test]
        public void ProductOwnsOneFixedAircraftCenteredBuildingClipmap()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(
                FullMapBuildingClipmapAssetAuthoring.EnvironmentPath);
            AircraftCenteredBuildingClipmapController[] controllers =
                environment.GetComponentsInChildren<AircraftCenteredBuildingClipmapController>(true);
            Assert.That(controllers, Has.Length.EqualTo(1));
            AircraftCenteredBuildingClipmapController controller = controllers[0];
            Assert.That(controller.transform.name, Is.EqualTo(PhotorealAerialSurfaceRoot.BuildingLayerName));
            Assert.That(controller.pageFilters, Has.Length.EqualTo(AircraftCenteredBuildingClipmapController.SlotCount));
            Assert.That(controller.pageRenderers, Has.Length.EqualTo(AircraftCenteredBuildingClipmapController.SlotCount));
            Assert.That(controller.pageFilters.All(filter => filter != null && filter.sharedMesh == null), Is.True);
            Assert.That(controller.pageRenderers.All(renderer => renderer != null &&
                renderer.sharedMaterial == controller.sharedBuildingMaterial &&
                renderer.shadowCastingMode == ShadowCastingMode.Off), Is.True);
            Assert.That(controller.GetComponentsInChildren<LODGroup>(true), Is.Empty);
            Assert.That(controller.authoredContractVersion, Is.EqualTo(AircraftCenteredBuildingClipmapController.ContractVersion));
            Assert.That(controller.authoredMaximumSelectedTriangles, Is.EqualTo(235000));
            Assert.That(controller.pageTriangleCounts, Has.Length.EqualTo(576));
            Assert.That(controller.pageBuildingCounts, Has.Length.EqualTo(576));
            Assert.That(controller.pageRoofAreasSquareMeters, Has.Length.EqualTo(576));
            Assert.That(controller.pageBuildingCounts.Sum(), Is.EqualTo(57439));

            var scene = EditorSceneManager.OpenScene(
                FullMapBuildingClipmapAssetAuthoring.ScenePath, OpenSceneMode.Single);
            AircraftCenteredBuildingClipmapController sceneController = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AircraftCenteredBuildingClipmapController>(true))
                .Single();
            Assert.That(sceneController.TryValidateContract(out string report), Is.True, report);
        }

        [Test]
        public void CompactDatabaseCoversTheMapWithoutAirportHeroDuplicates()
        {
            TextAsset database = AssetDatabase.LoadAssetAtPath<TextAsset>(
                FullMapBuildingClipmapAssetAuthoring.DatabasePath);
            Assert.That(database, Is.Not.Null);
            Assert.That(Occurrences(database.text, "\"source_id\":"), Is.EqualTo(57441));
            Assert.That(Occurrences(database.text, "\"column\":"), Is.EqualTo(576));
            Assert.That(database.text, Does.Contain("\"contract_version\":\"full-map-building-clipmap-v1\""));
            Assert.That(database.text, Does.Contain("\"facade_contract_version\":\"boulder-semantic-facades-v2\""));
            Assert.That(Occurrences(database.text, "\"material_id\":"), Is.EqualTo(57441));
            Assert.That(Occurrences(database.text, "\"facade_style_id\":"), Is.EqualTo(57441));
            Assert.That(Occurrences(database.text, "\"material_confidence\":"), Is.EqualTo(57441));
            Assert.That(Occurrences(database.text, "\"semantic_class_id\":"), Is.EqualTo(57441));
            Assert.That(Occurrences(database.text, "\"semantic_region_id\":"), Is.EqualTo(57441));
            Assert.That(Occurrences(database.text, "\"material_rule_id\":"), Is.EqualTo(57441));
            Assert.That(database.text, Does.Contain("\"roof_id\":1"));
            Assert.That(database.text, Does.Contain("\"roof_id\":2"));

            string[] airportGapFillSourceIds =
            {
                "563214886", "563214887", "77499385", "77499386", "77499387", "77499388",
                "77499394", "77513656", "78054436", "78054447", "78054451", "78054452",
                "78054454", "78054456", "78054458", "78054459", "78054461", "78054465",
                "78054470", "78054473", "78060787", "916029512",
                "48257466", "48257474", "339395538", "78062108", "78062109", "78062110",
                "78062111", "78062112", "78062113", "78062114", "78062115", "78062116"
            };
            foreach (string sourceId in airportGapFillSourceIds)
                Assert.That(database.text, Does.Contain($"\"source_id\":\"OSM:way:{sourceId}:geometry:0\""), sourceId);
            Assert.That(database.text, Does.Not.Contain("\"source_id\":\"OSM:way:77434544:geometry:0\""),
                "The hand-authored Executive Terminal replacement must not receive a map-fidelity duplicate.");
            Assert.That(database.text, Does.Not.Contain("\"source_id\":\"OSM:way:78054463:geometry:0\""),
                "The hand-authored T-hangar row replacement must not receive a map-fidelity duplicate.");

            string source = Read("Assets/Scripts/Environment/AircraftCenteredBuildingClipmapController.cs");
            Assert.That(source, Does.Not.Contain("new GameObject"));
            Assert.That(source, Does.Not.Contain("AddComponent<"));
            Assert.That(source, Does.Not.Contain("Instantiate("));
            Assert.That(source, Does.Not.Contain("FindObject"));
            Assert.That(source, Does.Contain("Application.streamingAssetsPath"));
            Assert.That(source, Does.Contain("CommitWindow(target, force"));
            Assert.That(source, Does.Not.Contain("detailRenderer"));
            Assert.That(source, Does.Contain("UnloadAsync(true)"));
            Assert.That(source, Does.Not.Contain("Unload(false)"));
            Assert.That(source, Does.Not.Contain("bundle.Unload(loaded == null)"));

            string authoring = Read("Assets/Editor/FullMapBuildingClipmapAssetAuthoring.cs");
            Assert.That(authoring, Does.Contain("CompileStableFootprint"));
            Assert.That(authoring, Does.Contain("OrdinaryFootprintVertexLimit"));
            Assert.That(authoring, Does.Not.Contain("CoverageWallMinimumAreaSquareMeters"));
            Assert.That(authoring, Does.Contain("IsWrrfTank(b) || IsWrrfPlatform(b) ? b.lod0_q : b.lod2_q"));
            Assert.That(authoring, Does.Not.Contain("building.area_m2 < 2000f"));
            Assert.That(authoring, Does.Not.Contain("building.area_m2 < 8000f"));
        }

        [Test]
        public void SharedFacadeArraysAreOpaqueCompressedAndMeterScaled()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                FullMapBuildingClipmapAssetAuthoring.MaterialPath);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo(FullMapBuildingClipmapAssetAuthoring.ShaderName));
            Assert.That(material.renderQueue, Is.LessThanOrEqualTo((int)RenderQueue.Geometry));
            Assert.That(material.enableInstancing, Is.True);
            Assert.That(material.GetTexture("_FacadeAlbedo"), Is.TypeOf<Texture2DArray>());
            Assert.That(material.GetTexture("_FacadeNormal"), Is.TypeOf<Texture2DArray>());
            Assert.That(material.GetTexture("_FacadeMRAO"), Is.TypeOf<Texture2DArray>());
            Assert.That(material.GetTexture("_FacadeDetail"), Is.TypeOf<Texture2D>());
            Assert.That(((Texture2DArray)material.GetTexture("_FacadeAlbedo")).depth, Is.EqualTo(16));
            Assert.That(material.GetTexture("_NearAlbedo"), Is.Not.Null);
            Assert.That(material.GetTexture("_MidAlbedo"), Is.Not.Null);
            Assert.That(material.GetTexture("_FarAlbedo"), Is.Not.Null);
            Assert.That(material.HasProperty("_BuildingShellMorphEnabled"), Is.False);
            Assert.That(material.HasProperty("_CornerSofteningWidthMeters"), Is.True);
            Assert.That(material.GetFloat("_CornerSofteningWidthMeters"), Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(material.HasProperty("_CornerSofteningStrength"), Is.True);
            Assert.That(material.GetFloat("_CornerSofteningStrength"), Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(material.shaderKeywords, Is.Empty,
                "The analytic corner path must not create a keyword variant.");
            string[] buildingOwnedTextures = material.GetTexturePropertyNames()
                .Select(material.GetTexture)
                .Where(texture => texture != null)
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => path.StartsWith(FullMapBuildingClipmapAssetAuthoring.Root + "/",
                    StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Assert.That(buildingOwnedTextures, Has.Length.EqualTo(4),
                "Corner softening must not add a building texture or sampler.");
            foreach (string path in new[]
                     {
                         FullMapBuildingClipmapAssetAuthoring.AlbedoPath,
                         FullMapBuildingClipmapAssetAuthoring.NormalPath,
                         FullMapBuildingClipmapAssetAuthoring.MraoPath
                     })
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.textureShape, Is.EqualTo(TextureImporterShape.Texture2DArray), path);
                Assert.That(importer.mipmapEnabled, Is.True, path);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear), path);
                Assert.That(importer.GetPlatformTextureSettings("Android").format,
                    Is.EqualTo(TextureImporterFormat.ASTC_6x6), path);
            }
            TextureImporter detailImporter = AssetImporter.GetAtPath(
                FullMapBuildingClipmapAssetAuthoring.DetailPath) as TextureImporter;
            Assert.That(detailImporter, Is.Not.Null);
            Assert.That(detailImporter.textureShape, Is.EqualTo(TextureImporterShape.Texture2D));
            Assert.That(detailImporter.mipmapEnabled, Is.True);
            Assert.That(detailImporter.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(detailImporter.GetPlatformTextureSettings("Android").format,
                Is.EqualTo(TextureImporterFormat.ASTC_6x6));
            string shader = Read("Assets/Shaders/FullMapBuildingClipmap.shader");
            int motion=shader.IndexOf("Name \"XRMotionVectors\"",StringComparison.Ordinal);
            Assert.That(motion,Is.GreaterThan(0),"The additional XR pass must exist.");
            shader=shader.Substring(0,shader.LastIndexOf("Pass",motion,StringComparison.Ordinal));
            Assert.That(shader, Does.Contain("i.detailUv"));
            Assert.That(shader, Does.Contain("WindowMask"));
            Assert.That(shader, Does.Contain("MaterialTileMeters"));
            Assert.That(shader, Does.Contain("UNITY_SAMPLE_TEX2DARRAY(_FacadeAlbedo"));
            Assert.That(shader, Does.Contain("half hasCompleteBay"));
            Assert.That(shader, Does.Contain("half DoorMask"));
            Assert.That(shader, Does.Not.Contain("openingScreenFade"),
                "Accepted full-distance facade apertures must not be hidden by the retired screen-size fade.");
            Assert.That(shader, Does.Contain("DetailAtlasUv"));
            Assert.That(shader, Does.Contain("tex2D(_FacadeDetail"));
            Assert.That(shader, Does.Not.Contain("shellReveal"));
            Assert.That(shader, Does.Not.Contain("_BuildingLodReferencePosition"));
            Assert.That(shader, Does.Not.Contain("clip(window"),
                "Distant openings must fade continuously rather than pop or dither between eyes.");
            Assert.That(shader, Does.Contain("SampleProjectedRoofOrthophoto(i.worldPos.xz)"));
            Assert.That(shader, Does.Contain("AerialClipmapSampling.cginc"));
            Assert.That(shader, Does.Contain("UNITY_BRANCH if (roof > 0.5h)"));
            Assert.That(shader, Does.Contain("DecodeCornerMagnitude"));
            Assert.That(shader, Does.Contain("CornerEdgeMask"));
            Assert.That(shader, Does.Contain("fwidth(i.detailUv.x)"));
            Assert.That(shader, Does.Contain("UNITY_BRANCH if (_CornerSofteningStrength > 0.0005h"));
            Assert.That(shader, Does.Contain("wallLength * 0.20h"));
            Assert.That(shader, Does.Contain("tangentNormal.xy += horizontalTangentInBasis"));
            Assert.That(Occurrences(shader, ": TEXCOORD"), Is.EqualTo(10),
                "The Phase 1 path must reuse existing interpolators.");
            Assert.That(Occurrences(shader, "UNITY_SAMPLE_TEX2DARRAY(_FacadeAlbedo"), Is.EqualTo(1));
            Assert.That(Occurrences(shader, "UNITY_SAMPLE_TEX2DARRAY(_FacadeNormal"), Is.EqualTo(1));
            Assert.That(Occurrences(shader, "UNITY_SAMPLE_TEX2DARRAY(_FacadeMRAO"), Is.EqualTo(1));
            Assert.That(Occurrences(shader, "tex2D(_FacadeDetail"), Is.EqualTo(1));
            Assert.That(shader, Does.Not.Contain("#pragma shader_feature"));
            Assert.That(shader, Does.Not.Contain("tex2D(_MainTex"),
                "Wall rendering must not fall back to the retired semantic-incoherent atlas.");
            Assert.That(shader, Does.Not.Contain("Blend "));
            Assert.That(shader, Does.Not.Contain("clip("));
            Assert.That(shader, Does.Not.Contain("discard"));
            Assert.That(shader.ToLowerInvariant(), Does.Not.Contain("dither"));

            string authoring = Read("Assets/Editor/FullMapBuildingClipmapAssetAuthoring.cs");
            Assert.That(authoring, Does.Contain("RoofCell(building)"));
            Assert.That(authoring, Does.Contain("seed01, RoofDetailScale(building)"));
            Assert.That(authoring, Does.Contain("Vector3.Cross(c - a, b - a)"));
            Assert.That(authoring, Does.Contain("builder.indices.Add(start + 2); builder.indices.Add(start + 1)"));
        }

        [Test]
        public void ConvexCornerCodecHandlesRightNonRightAndReversedWinding()
        {
            Vector2[] square =
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f),
                new Vector2(4f, 4f), new Vector2(0f, 4f)
            };
            Assert.That(FullMapBuildingCornerEncoding.TryCalculateConvexTurnMagnitude(
                square, 1, out float rightTurn), Is.True);
            Assert.That(rightTurn, Is.EqualTo(1f).Within(0.00001f));

            Vector2[] reversed = square.Reverse().ToArray();
            int reversedCorner = Array.FindIndex(reversed, point => point == square[1]);
            Assert.That(FullMapBuildingCornerEncoding.TryCalculateConvexTurnMagnitude(
                reversed, reversedCorner, out float reversedTurn), Is.True);
            Assert.That(reversedTurn, Is.EqualTo(rightTurn).Within(0.00001f),
                "Convexity must be independent of source winding.");

            Vector2[] sixtyDegreeTurn =
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f),
                new Vector2(6f, 3.4641016f)
            };
            Assert.That(FullMapBuildingCornerEncoding.TryCalculateConvexTurnMagnitude(
                sixtyDegreeTurn, 1, out float nonRightTurn), Is.True);
            Assert.That(nonRightTurn, Is.EqualTo(Mathf.Tan(30f * Mathf.Deg2Rad)).Within(0.0001f));

            byte packed = FullMapBuildingCornerEncoding.Pack(rightTurn, nonRightTurn);
            Assert.That(FullMapBuildingCornerEncoding.DecodeStart(packed), Is.EqualTo(1f));
            Assert.That(Mathf.Abs(FullMapBuildingCornerEncoding.DecodeEnd(packed) - nonRightTurn),
                Is.LessThanOrEqualTo(FullMapBuildingCornerEncoding.MaximumQuantizationError + 0.00001f));
            Assert.That(FullMapBuildingCornerEncoding.DecodeStart(
                FullMapBuildingCornerEncoding.LegacyDisabledPacked), Is.Zero,
                "Legacy all-white alpha must fail visually neutral.");
            Assert.That(FullMapBuildingCornerEncoding.DecodeEnd(
                FullMapBuildingCornerEncoding.LegacyDisabledPacked), Is.Zero);
        }

        [Test]
        public void LegacyBoundaryOnlyRoofTopologyCannotReachInteriorPeakButProperTopologyDoes()
        {
            Vector2[] rectangle =
            {
                new Vector2(0f, 0f), new Vector2(12f, 0f),
                new Vector2(12f, 8f), new Vector2(0f, 8f)
            };
            Type authoring = typeof(FullMapBuildingClipmapAssetAuthoring);
            Type axesType = authoring.GetNestedType("RoofAxes", BindingFlags.NonPublic);
            Assert.That(axesType, Is.Not.Null);
            MethodInfo from = axesType.GetMethod("From", BindingFlags.Public | BindingFlags.Static);
            MethodInfo height = authoring.GetMethod("RoofHeight", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(from, Is.Not.Null);
            Assert.That(height, Is.Not.Null);
            object axes = from.Invoke(null, new object[] { rectangle });

            foreach (int roofId in new[] { 1, 2 })
            {
                float maximum = rectangle.Max(point =>
                    (float)height.Invoke(null, new[] { (object)point, axes, roofId, 2f }));
                Assert.That(maximum, Is.Zero.Within(0.0001f),
                    $"Legacy roof {roofId} evaluates only boundary vertices and misses its peak.");
                Assert.That(FullMapBuildingRoofTopology.TryCreate(
                    rectangle, roofId, 10f, 2f, false,
                    out FullMapBuildingRoofTopologyResult proper, out string rejection),
                    Is.True, rejection);
                Assert.That(proper.vertices.Max(point => point.y), Is.EqualTo(12f).Within(0.0001f));
            }
        }

        [Test]
        public void RectangularGableCreatesRidgeFieldsAndOutwardApertureFreeInfills()
        {
            Vector2[] rectangle = Rectangle(12f, 8f);
            Assert.That(FullMapBuildingRoofTopology.TryCreate(
                rectangle, FullMapBuildingRoofTopology.GableRoofId, 10f, 2f, false,
                out FullMapBuildingRoofTopologyResult roof, out string rejection), Is.True, rejection);
            Assert.That(roof.kind, Is.EqualTo(FullMapBuildingRoofTopologyKind.Gable));
            Assert.That(roof.vertices, Has.Length.EqualTo(6));
            Assert.That(roof.RoofTriangleCount, Is.EqualTo(4));
            Assert.That(roof.GableInfillTriangleCount, Is.EqualTo(2));
            Assert.That(roof.TotalTriangleCount, Is.EqualTo(6));
            Assert.That(roof.AddedTriangleCount, Is.EqualTo(4));
            Assert.That(roof.gableInfills.Select(value => value.ridgeVertexIndex).Distinct().Count(), Is.EqualTo(2));
            Assert.That(roof.gableInfills.All(value =>
                Mathf.Abs(roof.vertices[value.ridgeVertexIndex].y - 12f) < 0.0001f), Is.True);
            AssertRoofTrianglesFaceUp(roof);
            AssertPerimeterIsCovered(roof);

            foreach (FullMapBuildingGableInfill infill in roof.gableInfills)
            {
                Vector3 a = roof.vertices[infill.edgeStartIndex];
                Vector3 ridge = roof.vertices[infill.ridgeVertexIndex];
                Vector3 c = roof.vertices[infill.edgeEndIndex];
                Vector3 normal = Vector3.Cross(ridge - a, c - a).normalized;
                Vector2 edge = rectangle[infill.edgeEndIndex] - rectangle[infill.edgeStartIndex];
                Vector3 outward = new Vector3(edge.y, 0f, -edge.x).normalized;
                Assert.That(Vector3.Dot(normal, outward), Is.GreaterThan(0.999f));
            }

            string shader = Read("Assets/Shaders/FullMapBuildingClipmap.shader");
            Assert.That(shader, Does.Contain("allowed *= 1.0h - step(2.5h, facadeRole)"));
            Assert.That(shader, Does.Contain("(1.0h - step(2.5h, facadeRole))"));
            string authoring = Read("Assets/Editor/FullMapBuildingClipmapAssetAuthoring.cs");
            Assert.That(authoring, Does.Contain("builder.atlasCells.Add(new Vector2(length, 3f))"));
            Assert.That(authoring, Does.Contain("ApplyToTint(tint, 0f, 0f)"));
        }

        [Test]
        public void CompactHipCreatesOneApexAtIntendedRise()
        {
            Assert.That(FullMapBuildingRoofTopology.TryCreate(
                Rectangle(8f, 8f), FullMapBuildingRoofTopology.HipRoofId, 20f, 2.5f, false,
                out FullMapBuildingRoofTopologyResult roof, out string rejection), Is.True, rejection);
            Assert.That(roof.kind, Is.EqualTo(FullMapBuildingRoofTopologyKind.CompactHip));
            Assert.That(roof.vertices, Has.Length.EqualTo(5));
            Assert.That(roof.RoofTriangleCount, Is.EqualTo(4));
            Assert.That(roof.GableInfillTriangleCount, Is.Zero);
            Assert.That(roof.AddedTriangleCount, Is.EqualTo(2));
            Assert.That(roof.vertices[4].y, Is.EqualTo(22.5f).Within(0.0001f));
            AssertRoofTrianglesFaceUp(roof);
            AssertPerimeterIsCovered(roof);
        }

        [Test]
        public void ElongatedHipCreatesEqualPitchShortRidgeAtIntendedRise()
        {
            Assert.That(FullMapBuildingRoofTopology.TryCreate(
                Rectangle(14f, 8f), FullMapBuildingRoofTopology.HipRoofId, 5f, 1.5f, false,
                out FullMapBuildingRoofTopologyResult roof, out string rejection), Is.True, rejection);
            Assert.That(roof.kind, Is.EqualTo(FullMapBuildingRoofTopologyKind.ShortRidgeHip));
            Assert.That(roof.vertices, Has.Length.EqualTo(6));
            Assert.That(roof.RoofTriangleCount, Is.EqualTo(6));
            Assert.That(roof.GableInfillTriangleCount, Is.Zero);
            Assert.That(roof.AddedTriangleCount, Is.EqualTo(4));
            Assert.That(roof.vertices.Skip(4).All(point => Mathf.Abs(point.y - 6.5f) < 0.0001f), Is.True);
            Assert.That(Vector3.Distance(roof.vertices[4], roof.vertices[5]), Is.EqualTo(6f).Within(0.0001f));
            AssertRoofTrianglesFaceUp(roof);
            AssertPerimeterIsCovered(roof);
        }

        [Test]
        public void ProperRoofEligibilityRejectsAmbiguousIrregularAndExactLandmarkShells()
        {
            Assert.That(FullMapBuildingRoofTopology.TryCreate(
                Rectangle(8f, 8f), FullMapBuildingRoofTopology.GableRoofId, 5f, 1f, false,
                out _, out string squareReason), Is.False);
            Assert.That(squareReason, Is.EqualTo("ambiguous-gable-axis"));

            Vector2[] irregular =
            {
                new Vector2(0f, 0f), new Vector2(12f, 0f),
                new Vector2(9f, 7f), new Vector2(0f, 8f)
            };
            Assert.That(FullMapBuildingRoofTopology.TryCreate(
                irregular, FullMapBuildingRoofTopology.HipRoofId, 5f, 1f, false,
                out _, out _), Is.False);

            Vector2[] bowTie =
            {
                new Vector2(0f, 0f), new Vector2(8f, 8f),
                new Vector2(0f, 8f), new Vector2(8f, 0f)
            };
            Assert.That(FullMapBuildingRoofTopology.TryCreate(
                bowTie, FullMapBuildingRoofTopology.HipRoofId, 5f, 1f, false,
                out _, out _), Is.False);
            Assert.That(FullMapBuildingRoofTopology.TryCreate(
                Rectangle(12f, 8f), FullMapBuildingRoofTopology.GableRoofId, 5f, 1f, true,
                out _, out string landmarkReason), Is.False);
            Assert.That(landmarkReason, Is.EqualTo("exact-landmark"));
        }

        [Test]
        public void RoofBudgetAllocatorIsOrderIndependentAndEnforcesEveryHeaviestPageSet()
        {
            int[] baseline = { 100, 90, 80, 70 };
            FullMapBuildingRoofBudgetCandidate[] candidates =
            {
                BudgetCandidate(1, 2, 10, 0),
                BudgetCandidate(2, 0, 4, 1),
                BudgetCandidate(3, 1, 4, 2),
                BudgetCandidate(4, 3, 20, 3)
            };
            FullMapBuildingRoofBudgetResult ordered = FullMapBuildingRoofBudgetAllocator.Select(
                candidates, baseline, 2, 195);
            FullMapBuildingRoofBudgetResult reversed = FullMapBuildingRoofBudgetAllocator.Select(
                candidates.Reverse(), baseline, 2, 195);

            Assert.That(ordered.selectedBuildingIds, Is.EqualTo(new[] { 1, 2, 4 }));
            Assert.That(reversed.selectedBuildingIds, Is.EqualTo(ordered.selectedBuildingIds));
            Assert.That(reversed.pageTriangleTotals, Is.EqualTo(ordered.pageTriangleTotals));
            Assert.That(ordered.maximumSelectedTriangles, Is.EqualTo(194));
            Assert.That(FullMapBuildingRoofBudgetAllocator.ConservativeMaximum(
                ordered.pageTriangleTotals, 2), Is.LessThanOrEqualTo(195));
        }

        [Test]
        public void ReviewedWrrfExclusionRejectsChangedSourceIdentityAndDuplicateShells()
        {
            Type owner=typeof(FullMapBuildingClipmapAssetAuthoring);
            Type databaseType=owner.GetNestedType("Database", BindingFlags.NonPublic);
            object database=JsonUtility.FromJson(File.ReadAllText(FullMapBuildingClipmapAssetAuthoring.DatabasePath),databaseType);
            MethodInfo validate=owner.GetMethod("LoadValidatedSemanticTowerExclusions",BindingFlags.Static|BindingFlags.NonPublic);
            var exclusions=(HashSet<int>)validate.Invoke(null,new[]{database});
            Assert.That(exclusions.Contains(44816),Is.True,"The observed overlapping WRRF county aggregate must be excluded.");
            Array pages=(Array)databaseType.GetField("pages").GetValue(database);
            object firstPage=null,duplicate=null;FieldInfo buildingsField=null;
            foreach(object page in pages)
            {
                FieldInfo field=page.GetType().GetField("buildings");
                Array candidates=(Array)field.GetValue(page);
                if(candidates==null)continue;
                foreach(object building in candidates)
                    if((int)building.GetType().GetField("id").GetValue(building)==44816)
                    { firstPage=page;duplicate=building;buildingsField=field; }
            }
            Assert.That(duplicate,Is.Not.Null);
            FieldInfo sourceId=duplicate.GetType().GetField("source_id");
            object original=sourceId.GetValue(duplicate);sourceId.SetValue(duplicate,"UnreviewedSource");
            TargetInvocationException changed=Assert.Throws<TargetInvocationException>(()=>validate.Invoke(null,new[]{database}));
            Assert.That(changed.InnerException,Is.TypeOf<InvalidOperationException>());
            sourceId.SetValue(duplicate,original);
            Array buildings=(Array)buildingsField.GetValue(firstPage);
            Array repeated=Array.CreateInstance(duplicate.GetType(),buildings.Length+1);
            Array.Copy(buildings,repeated,buildings.Length);repeated.SetValue(duplicate,buildings.Length);
            buildingsField.SetValue(firstPage,repeated);
            TargetInvocationException repeatedError=Assert.Throws<TargetInvocationException>(()=>validate.Invoke(null,new[]{database}));
            Assert.That(repeatedError.InnerException,Is.TypeOf<ArgumentException>());
        }

        [Test]
        public void GeneratedRoofAllocationIsSelectiveDeterministicAndExactlyBudgeted()
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(
                FullMapBuildingClipmapAssetAuthoring.RoofTopologyReportPath);
            Assert.That(source, Is.Not.Null);
            RoofAllocationTestReport report = JsonUtility.FromJson<RoofAllocationTestReport>(source.text);
            Assert.That(report.compiler_version,
                Is.EqualTo("terrain-grounded-shell-compiler-v18-wrrf-20260922"));
            Assert.That(report.rollout, Is.EqualTo("selective"));
            Assert.That(report.represented_buildings, Is.EqualTo(57439));
            Assert.That(report.page_count, Is.EqualTo(576));
            Assert.That(report.nonempty_page_count, Is.EqualTo(483));
            Assert.That(report.active_page_slots, Is.EqualTo(24));
            Assert.That(report.nominal_gable_roofs + report.nominal_hip_roofs, Is.EqualTo(28725));
            Assert.That(report.safe_roofs, Is.EqualTo(13027));
            Assert.That(report.selected_gable_roofs, Is.EqualTo(4985));
            Assert.That(report.selected_compact_hip_roofs, Is.EqualTo(298));
            Assert.That(report.selected_short_ridge_hip_roofs, Is.EqualTo(679));
            Assert.That(report.selected_roofs, Is.EqualTo(5962));
            Assert.That(report.budget_rejected_safe_roofs, Is.EqualTo(7065));
            Assert.That(report.all_safe_added_triangles, Is.EqualTo(50790));
            Assert.That(report.selected_added_triangles, Is.EqualTo(23252));
            Assert.That(report.baseline_maximum_selected_triangles, Is.EqualTo(233298));
            Assert.That(report.all_safe_maximum_selected_triangles, Is.EqualTo(261348));
            Assert.That(report.final_maximum_selected_triangles, Is.EqualTo(235000));
            Assert.That(report.remaining_triangle_margin, Is.Zero);
            Assert.That(report.pages, Has.Length.EqualTo(576));
            Assert.That(report.pages.Sum(page => page.baseline_triangles), Is.EqualTo(615540));
            Assert.That(report.pages.Sum(page => page.final_triangles), Is.EqualTo(638792));
            Assert.That(report.pages.Select(page => page.final_triangles)
                .OrderByDescending(value => value).Take(24).Sum(), Is.EqualTo(235000));
            Assert.That(report.selected_building_ids, Has.Length.EqualTo(report.selected_roofs));
            Assert.That(report.selected_building_ids, Is.Ordered.Ascending);
            Assert.That(report.selected_building_ids.Distinct().Count(),
                Is.EqualTo(report.selected_building_ids.Length));

            string authoring = Read("Assets/Editor/FullMapBuildingClipmapAssetAuthoring.cs");
            Assert.That(authoring, Does.Not.Contain("PrototypeAllEligibleRoofs"),
                "The over-budget broad visual prototype must never enter shipping authoring.");
        }

        [Test]
        public void CornerCodecSuppressesConcaveCollinearDuplicateShortAndAcuteTurns()
        {
            Vector2[] concave =
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(2f, 2f),
                new Vector2(4f, 4f), new Vector2(0f, 4f)
            };
            Assert.That(FullMapBuildingCornerEncoding.TryCalculateConvexTurnMagnitude(
                concave, 2, out _), Is.False);

            Vector2[] collinear =
            {
                new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(4f, 0f),
                new Vector2(4f, 4f), new Vector2(0f, 4f)
            };
            Assert.That(FullMapBuildingCornerEncoding.TryCalculateConvexTurnMagnitude(
                collinear, 1, out _), Is.False);

            Vector2[] duplicate =
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 0f),
                new Vector2(4f, 4f), new Vector2(0f, 4f)
            };
            Assert.That(FullMapBuildingCornerEncoding.TryCalculateConvexTurnMagnitude(
                duplicate, 2, out _), Is.False);

            Vector2[] shortEdge =
            {
                new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 4f), new Vector2(0f, 4f)
            };
            Assert.That(FullMapBuildingCornerEncoding.TryCalculateConvexTurnMagnitude(
                shortEdge, 1, out _), Is.False);

            Vector2[] pathologicalAcuteInterior =
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f),
                new Vector2(0.5358984f, 2f)
            };
            Assert.That(FullMapBuildingCornerEncoding.TryCalculateConvexTurnMagnitude(
                pathologicalAcuteInterior, 1, out _), Is.False);
        }

        [Test]
        public void WallEndpointSignsConvergeAdjacentNormalsTowardOneBisector()
        {
            Vector2[] square =
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f),
                new Vector2(4f, 4f), new Vector2(0f, 4f)
            };
            FullMapBuildingCornerEncoding.TryCalculateConvexTurnMagnitude(square, 1, out float magnitude);
            byte packed = FullMapBuildingCornerEncoding.Pack(magnitude, magnitude);
            Assert.That(FullMapBuildingCornerEncoding.SignedEndContribution(packed), Is.EqualTo(1f));
            Assert.That(FullMapBuildingCornerEncoding.SignedStartContribution(packed), Is.EqualTo(-1f));

            Vector2 incomingTangent = (square[1] - square[0]).normalized;
            Vector2 outgoingTangent = (square[2] - square[1]).normalized;
            Vector2 incomingOutward = new Vector2(incomingTangent.y, -incomingTangent.x);
            Vector2 outgoingOutward = new Vector2(outgoingTangent.y, -outgoingTangent.x);
            Vector2 fromIncomingWall = VirtualEdgeNormal(incomingOutward, incomingTangent,
                FullMapBuildingCornerEncoding.SignedEndContribution(packed));
            Vector2 fromOutgoingWall = VirtualEdgeNormal(outgoingOutward, outgoingTangent,
                FullMapBuildingCornerEncoding.SignedStartContribution(packed));
            Vector2 expectedBisector = (incomingOutward + outgoingOutward).normalized;
            Assert.That(Vector2.Angle(fromIncomingWall, expectedBisector), Is.LessThan(0.01f));
            Assert.That(Vector2.Angle(fromOutgoingWall, expectedBisector), Is.LessThan(0.01f));
            Assert.That(Vector2.Angle(fromIncomingWall, fromOutgoingWall), Is.LessThan(0.01f));

            Vector2[] nonRight =
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f),
                new Vector2(6f, 3.4641016f)
            };
            FullMapBuildingCornerEncoding.TryCalculateConvexTurnMagnitude(nonRight, 1, out float nonRightMagnitude);
            byte nonRightPacked = FullMapBuildingCornerEncoding.Pack(nonRightMagnitude, nonRightMagnitude);
            Vector2 firstTangent = (nonRight[1] - nonRight[0]).normalized;
            Vector2 secondTangent = (nonRight[2] - nonRight[1]).normalized;
            Vector2 firstNormal = VirtualEdgeNormal(new Vector2(firstTangent.y, -firstTangent.x),
                firstTangent, FullMapBuildingCornerEncoding.SignedEndContribution(nonRightPacked));
            Vector2 secondNormal = VirtualEdgeNormal(new Vector2(secondTangent.y, -secondTangent.x),
                secondTangent, FullMapBuildingCornerEncoding.SignedStartContribution(nonRightPacked));
            Assert.That(Vector2.Angle(firstNormal, secondNormal), Is.LessThan(7.1f),
                "Four-bit quantization bounds adjacent-face disagreement below 7.1 degrees.");
        }

        [Test]
        public void PackedWallAlphaIsDeterministicConstantInterpolableAndPreservesTintRgb()
        {
            Color32 tint = new Color32(17, 93, 211, 255);
            byte expected = FullMapBuildingCornerEncoding.Pack(1f, Mathf.Tan(30f * Mathf.Deg2Rad));
            for (int iteration = 0; iteration < 32; iteration++)
            {
                Color32 encoded = FullMapBuildingCornerEncoding.ApplyToTint(
                    tint, 1f, Mathf.Tan(30f * Mathf.Deg2Rad));
                Assert.That(encoded.r, Is.EqualTo(tint.r));
                Assert.That(encoded.g, Is.EqualTo(tint.g));
                Assert.That(encoded.b, Is.EqualTo(tint.b));
                Assert.That(encoded.a, Is.EqualTo(expected));
            }

            float normalizedAlpha = expected / 255f;
            float interpolated = normalizedAlpha * 0.07f + normalizedAlpha * 0.19f +
                                 normalizedAlpha * 0.31f + normalizedAlpha * 0.43f;
            byte reconstructed = (byte)Mathf.RoundToInt(interpolated * 255f);
            Assert.That(reconstructed, Is.EqualTo(expected),
                "All four duplicated wall vertices carry one byte, so interpolation adds zero endpoint error.");
        }

        [Test]
        public void UninitializedResidencyQueryFailsClosedWithoutThrowing()
        {
            GameObject owner = new GameObject("UninitializedBuildingResidencyTest");
            try
            {
                AircraftCenteredBuildingClipmapController controller =
                    owner.AddComponent<AircraftCenteredBuildingClipmapController>();
                bool[] committed = new bool[controller.PageColumns * controller.PageRows];
                bool[] prefetched = new bool[committed.Length];
                Assert.That(controller.TryCopyPageResidency(committed, prefetched), Is.False);
                Assert.That(committed, Is.All.False);
                Assert.That(prefetched, Is.All.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void CompiledShellsSealWallsToAuthoritativeTerrainAndExactRoofVertices()
        {
            TextAsset report = AssetDatabase.LoadAssetAtPath<TextAsset>(
                FullMapBuildingClipmapAssetAuthoring.CompilationReportPath);
            Assert.That(report, Is.Not.Null);
            Assert.That(report.text, Does.Contain(
                "terrain-grounded-shell-compiler-v18-wrrf-20260922"));
            Assert.That(report.text, Does.Contain("\"foundation_embed_meters\": 1.6"));
            Assert.That(report.text, Does.Contain("\"maximum_roof_wall_seam_meters\": 0.0"));
            Assert.That(report.text, Does.Contain("\"maximum_vertical_corner_seam_meters\": 0.0"));

            string authoring = Read("Assets/Editor/FullMapBuildingClipmapAssetAuthoring.cs");
            Assert.That(authoring, Does.Contain("Vector3 c = roof[(i + 1) % polygon.Length]"),
                "A wall top endpoint must be the exact roof perimeter vertex, not a parallel approximation.");
            Assert.That(authoring, Does.Contain("Vector3 d = roof[i]"));
            Assert.That(authoring, Does.Contain("Mathf.Min(authoredBaseY, sample.heightMeters) - FoundationEmbedMeters"),
                "Each wall endpoint must extend beneath both the building datum and authoritative terrain.");
            Assert.That(authoring, Does.Contain("Wall/roof seam exceeds 1 mm"));
            Assert.That(authoring, Does.Contain("Open vertical wall corner found"));

            ProductionTerrainSurfaceAsset terrain = AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(
                FullMapBuildingClipmapAssetAuthoring.TerrainSurfacePath);
            Assert.That(terrain, Is.Not.Null);
            Assert.That(FullMapBuildingClipmapAssetAuthoring.FoundationEmbedMeters, Is.GreaterThan(1.5f),
                "Foundation skirt must exceed the documented maximum simplified-terrain error.");
        }

        [Test]
        public void SelectionProtectsTheFullHeightBaseAndPreloadsTheNextForwardFringe()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(
                FullMapBuildingClipmapAssetAuthoring.EnvironmentPath);
            AircraftCenteredBuildingClipmapController controller = environment
                .GetComponentInChildren<AircraftCenteredBuildingClipmapController>(true);
            Vector3 origin = new Vector3(10f, 100f, 10f);
            Vector2Int[] selected = controller.CalculateDesiredPagesForTest(
                origin, Vector3.forward, Vector3.forward);
            Vector2Int[] basePages = controller.CalculateBasePagesForTest(origin);
            Assert.That(selected, Has.Length.EqualTo(AircraftCenteredBuildingClipmapController.SlotCount));
            Assert.That(basePages, Has.Length.EqualTo(AircraftCenteredBuildingClipmapController.BaseSlotCount));
            Assert.That(basePages.All(selected.Contains), Is.True,
                "Every page in the accepted 4x4 full-height base must remain resident.");
            for (int column = 10; column <= 13; column++)
                Assert.That(selected, Does.Contain(new Vector2Int(column, 14)),
                    "The complete next forward row must be resident before the boundary crossing.");

            Vector2Int[] nextBase = controller.CalculateBasePagesForTest(new Vector3(10f, 100f, 1100f));
            Assert.That(nextBase.All(selected.Contains), Is.True,
                "A normal forward page crossing must introduce no previously absent base geometry.");

            var connected = new System.Collections.Generic.HashSet<Vector2Int> { basePages[0] };
            bool changed;
            do
            {
                changed = false;
                foreach (Vector2Int page in selected)
                {
                    if (connected.Contains(page)) continue;
                    if (connected.Any(other => Mathf.Abs(page.x - other.x) + Mathf.Abs(page.y - other.y) == 1))
                    { connected.Add(page); changed = true; }
                }
            } while (changed);
            Assert.That(connected.Count, Is.EqualTo(selected.Length),
                "Forward residency must be one connected landscape, never isolated popping islands.");

            int triangles = selected.Sum(page => controller.pageTriangleCounts[page.y * controller.columns + page.x]);
            Assert.That(triangles, Is.LessThanOrEqualTo(controller.authoredMaximumSelectedTriangles));
        }

        [Test]
        public void StableShellCompilerStaysInsideTheBuildingTriangleCeiling()
        {
            int columns = 24, rows = 24;
            int[,] stable = new int[columns, rows];
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                    $"{FullMapBuildingClipmapAssetAuthoring.GeneratedRoot}/KBDU_buildings_stable_{column:D3}_{row:D3}.asset");
                Assert.That(mesh, Is.Not.Null, $"stable {column},{row}");
                stable[column, row] = (int)mesh.GetIndexCount(0) / 3;
            }

            int maximum = stable.Cast<int>().OrderByDescending(value => value)
                .Take(AircraftCenteredBuildingClipmapController.SlotCount).Sum();
            Assert.That(maximum, Is.EqualTo(235000));
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(
                FullMapBuildingClipmapAssetAuthoring.EnvironmentPath);
            AircraftCenteredBuildingClipmapController controller = environment
                .GetComponentInChildren<AircraftCenteredBuildingClipmapController>(true);
            Assert.That(controller.authoredMaximumSelectedTriangles, Is.EqualTo(maximum),
                "The authored allowance must equal deterministic mesh truth, not a stale overestimate.");
            Assert.That(maximum, Is.LessThanOrEqualTo(235000));
        }

        [Test]
        public void GeneratedCornerMetadataChangesOnlyAlphaAndKeepsStableShellCounts()
        {
            long vertices = 0;
            long triangles = 0;
            long encodedWallQuads = 0;
            int incompleteStreams = 0;
            int legacyWallVertices = 0;
            int nonSentinelRoofVertices = 0;
            int nonConstantWallQuads = 0;

            for (int row = 0; row < 24; row++)
            for (int column = 0; column < 24; column++)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                    $"{FullMapBuildingClipmapAssetAuthoring.GeneratedRoot}/KBDU_buildings_stable_{column:D3}_{row:D3}.asset");
                Assert.That(mesh, Is.Not.Null, $"stable {column},{row}");
                Color32[] colors = mesh.colors32;
                List<Vector2> atlasCells = new List<Vector2>(mesh.vertexCount);
                List<Vector4> parameters = new List<Vector4>(mesh.vertexCount);
                mesh.GetUVs(1, atlasCells);
                mesh.GetUVs(2, parameters);
                if (colors.Length != mesh.vertexCount || atlasCells.Count != mesh.vertexCount ||
                    parameters.Count != mesh.vertexCount)
                {
                    incompleteStreams++;
                    continue;
                }

                vertices += mesh.vertexCount;
                triangles += (long)mesh.GetIndexCount(0) / 3;
                int[] indices = mesh.triangles;
                int cursor = 0, indexCursor = 0;
                while (cursor < mesh.vertexCount)
                {
                    // WRRF curved wall quads use atlas role 3, which also labels
                    // triangular gable infills. Geometry determines primitive kind.
                    bool quad = indexCursor + 5 < indices.Length &&
                        indices[indexCursor] == cursor && indices[indexCursor+1] == cursor+2 &&
                        indices[indexCursor+2] == cursor+1 && indices[indexCursor+3] == cursor &&
                        indices[indexCursor+4] == cursor+3 && indices[indexCursor+5] == cursor+2;
                    int count = quad ? 4 : 3;
                    Assert.That(cursor + count, Is.LessThanOrEqualTo(mesh.vertexCount));
                    bool roof = parameters[cursor].x >= .5f;
                    Color32 first = colors[cursor];
                    for (int offset = 0; offset < count; offset++)
                    {
                        Color32 value = colors[cursor+offset];
                        if (roof && value.a != FullMapBuildingCornerEncoding.LegacyDisabledPacked)
                            nonSentinelRoofVertices++;
                        if (!roof && quad)
                        {
                            if (value.r != first.r || value.g != first.g || value.b != first.b || value.a != first.a)
                                nonConstantWallQuads++;
                            if (value.a == FullMapBuildingCornerEncoding.LegacyDisabledPacked) legacyWallVertices++;
                        }
                        if (!roof && !quad && value.a != 0) nonConstantWallQuads++;
                    }
                    if (!roof && quad && (FullMapBuildingCornerEncoding.DecodeStart(first.a) > 0f ||
                        FullMapBuildingCornerEncoding.DecodeEnd(first.a) > 0f)) encodedWallQuads++;
                    cursor += count; indexCursor += quad ? 6 : 3;
                }
                Assert.That(indexCursor, Is.EqualTo(indices.Length));
            }

            Assert.That(incompleteStreams, Is.Zero);
            Assert.That(legacyWallVertices, Is.Zero,
                "Rebuilt walls must not retain the legacy all-white sentinel.");
            Assert.That(nonSentinelRoofVertices, Is.Zero,
                "Phase 1 must leave roof color metadata untouched.");
            Assert.That(nonConstantWallQuads, Is.Zero,
                "Packed endpoints must be constant across each duplicated wall quad.");
            Assert.That(encodedWallQuads, Is.GreaterThan(200000));
            Assert.That(vertices, Is.EqualTo(1429540), "Current v18 WRRF/Valmont shell topology; producer replay is checked separately.");
            Assert.That(triangles, Is.EqualTo(638792));
        }

        [Test]
        public void CurrentCompilerReproducesReviewedWrrfPagesWithoutSavingAssets()
        {
            Type author=typeof(FullMapBuildingClipmapAssetAuthoring);
            object database=author.GetMethod("LoadDatabase",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            var exclusions=(HashSet<int>)author.GetMethod("LoadValidatedSemanticTowerExclusions",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{database});
            object allocation=author.GetMethod("BuildRoofAllocation",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{database,exclusions});
            var terrain=AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(FullMapBuildingClipmapAssetAuthoring.TerrainSurfacePath);
            Array pages=(Array)database.GetType().GetField("pages").GetValue(database);
            var isTank=author.GetMethod("IsWrrfTank",BindingFlags.Static|BindingFlags.NonPublic);
            var build=author.GetMethod("BuildPageMesh",BindingFlags.Static|BindingFlags.NonPublic);
            int verified=0;
            foreach(object page in pages)
            {
                Array buildings=(Array)page.GetType().GetField("buildings").GetValue(page);
                if(buildings==null || !buildings.Cast<object>().Any(building=>(bool)isTank.Invoke(null,new[]{building})))continue;
                int column=(int)page.GetType().GetField("column").GetValue(page),row=(int)page.GetType().GetField("row").GetValue(page);
                string path=$"{FullMapBuildingClipmapAssetAuthoring.GeneratedRoot}/KBDU_buildings_stable_{column:D3}_{row:D3}.asset";
                string before=Sha256(path);Mesh actual=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                Mesh replay=(Mesh)build.Invoke(null,new object[]{database,page,terrain,allocation,exclusions});
                try
                {
                    Assert.That(replay.vertexCount,Is.EqualTo(actual.vertexCount),path);
                    Assert.That(replay.triangles,Is.EqualTo(actual.triangles),path);
                    Assert.That(replay.vertices,Is.EqualTo(actual.vertices),path);
                    Assert.That(replay.colors32,Is.EqualTo(actual.colors32),path);
                    Assert.That(Sha256(path),Is.EqualTo(before),"Read-only producer replay preserves asset bytes.");
                    verified++;
                }
                finally { UnityEngine.Object.DestroyImmediate(replay); }
            }
            Assert.That(verified,Is.InRange(1,3),"Bounded reviewed WRRF pages only.");
        }

        private static string Sha256(string path) => AuthoringBundleVerifier.Sha256(path);

        private static int Occurrences(string source, string value)
        {
            int count = 0, index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private static Vector2 VirtualEdgeNormal(Vector2 outward, Vector2 tangent, float signedMagnitude) =>
            (outward + tangent * signedMagnitude).normalized;

        private static Vector2[] Rectangle(float length, float width) => new[]
        {
            new Vector2(0f, 0f), new Vector2(length, 0f),
            new Vector2(length, width), new Vector2(0f, width)
        };

        private static FullMapBuildingRoofBudgetCandidate BudgetCandidate(
            int id, int page, int delta, int priority) =>
            new FullMapBuildingRoofBudgetCandidate(
                id, page, delta, priority, priority, true,
                100, 50, 10, 3, 3, id.ToString("D4"), 0);

        [Serializable]
        private sealed class RoofAllocationTestPage
        {
            public int baseline_triangles;
            public int final_triangles;
        }

        [Serializable]
        private sealed class RoofAllocationTestReport
        {
            public string compiler_version;
            public string rollout;
            public int represented_buildings;
            public int page_count;
            public int nonempty_page_count;
            public int active_page_slots;
            public int nominal_gable_roofs;
            public int nominal_hip_roofs;
            public int safe_roofs;
            public int selected_gable_roofs;
            public int selected_compact_hip_roofs;
            public int selected_short_ridge_hip_roofs;
            public int selected_roofs;
            public int budget_rejected_safe_roofs;
            public int all_safe_added_triangles;
            public int selected_added_triangles;
            public int baseline_maximum_selected_triangles;
            public int all_safe_maximum_selected_triangles;
            public int final_maximum_selected_triangles;
            public int remaining_triangle_margin;
            public RoofAllocationTestPage[] pages;
            public int[] selected_building_ids;
        }

        private static void AssertRoofTrianglesFaceUp(FullMapBuildingRoofTopologyResult roof)
        {
            for (int index = 0; index < roof.roofTriangles.Length; index += 3)
            {
                Vector3 a = roof.vertices[roof.roofTriangles[index]];
                Vector3 b = roof.vertices[roof.roofTriangles[index + 1]];
                Vector3 c = roof.vertices[roof.roofTriangles[index + 2]];
                Assert.That(Vector3.Cross(b - a, c - a).y, Is.GreaterThan(0.0001f),
                    $"Roof triangle {index / 3} must face upward and remain nondegenerate.");
            }
        }

        private static void AssertPerimeterIsCovered(FullMapBuildingRoofTopologyResult roof)
        {
            for (int perimeter = 0; perimeter < 4; perimeter++)
                Assert.That(roof.roofTriangles, Does.Contain(perimeter),
                    $"Roof topology omitted footprint corner {perimeter}.");
        }

        private static string Read(string assetPath) => File.ReadAllText(Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", assetPath.Replace('/', Path.DirectorySeparatorChar))));
    }
}
