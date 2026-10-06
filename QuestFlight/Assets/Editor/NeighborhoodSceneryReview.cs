using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        [Serializable] private sealed class NeighborhoodCrownData
        {
            public string schema;
            public Vector3[] vertices, normals;
            public Color[] colors;
            public Vector2[] uv, modes;
            public int[] indices;
            public NeighborhoodTree[] source_records;
        }
        [Serializable] private sealed class NeighborhoodTree { public int id; public float x,y,z; }
        [Serializable] private sealed class NeighborhoodCost
        {
            public string view;
            public bool candidate;
            public int baselineCrownsReplaced, baselineTrianglesReplaced, treeDraws, treeTriangles;
            public int addedDraws, addedTriangles;
            public long meshResidentBytes;
            public float cameraTerrainAgl;
            public int buildingTriangleDelta;
            public string sourceMeshSha256;
        }
        [Serializable] private sealed class NeighborhoodReview
        {
            public string status="EDITOR_LOOK_DEVELOPMENT_NOT_QUEST_OR_SIMULATOR_ACCEPTANCE";
            public List<NeighborhoodCost> views=new List<NeighborhoodCost>();
        }

        public static void AuthorNeighborhoodFoliageCandidate()
        {
            string root=System.Environment.GetEnvironmentVariable("QFL_NEIGHBORHOOD_ROOT");
            if(string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("QFL_NEIGHBORHOOD_ROOT required");
            var data=JsonUtility.FromJson<NeighborhoodCrownData>(File.ReadAllText(Path.Combine(root,"crowns.json")));
            if(data.schema!="questflight.neighborhood-crowns.v1" || data.source_records.Length!=230 || data.modes.Any(m=>m.x!=1 && m.x!=2))
                throw new InvalidOperationException("Only the reviewed perforated foliage candidate can be authored");
            const string assetRoot="Assets/Production/Environment/NeighborhoodFoliageV1/";
            Color32[] encodedColors=data.colors.Select((c,i)=>data.modes[i].x>1.5f
                ? new Color32(255,77,61,46) : (Color32)c).ToArray();
            Vector2[] encodedUvs=data.uv.Select((uv,i)=>data.modes[i].x>1.5f
                ? new Vector2(-1,-1) : new Vector2(8+uv.x,uv.y)).ToArray();
            var generated=new Mesh { name="Valmont_PerforatedFoliage_230",indexFormat=data.vertices.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16,
                vertices=data.vertices,normals=data.normals,colors32=encodedColors,uv=encodedUvs,triangles=data.indices };
            generated.RecalculateBounds();
            string meshPath=assetRoot+"ValmontPerforatedFoliage.asset";
            Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(mesh==null) { mesh=generated; AssetDatabase.CreateAsset(mesh,meshPath); }
            else { EditorUtility.CopySerialized(generated,mesh); UnityEngine.Object.DestroyImmediate(generated); EditorUtility.SetDirty(mesh); }
            mesh.UploadMeshData(true);
            string materialPath=QuestFlight.Editor.AirportCloseTreeLodAuthoring.CanopyMaterialPath;
            Material material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null) throw new InvalidOperationException("Existing shared canopy material is required");
            var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(assetRoot+"NeighborhoodFoliageAtlas.png");
            if(atlas==null || !material.HasProperty("_NeighborhoodFoliageAtlas"))
                throw new InvalidOperationException("Imported branch atlas and shader property are required");
            material.SetTexture("_NeighborhoodFoliageAtlas",atlas);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            string prefabPath=QuestFlight.Editor.FullMapBuildingClipmapAssetAuthoring.EnvironmentPath;
            GameObject prefab=PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform child=prefab.transform.Find("NeighborhoodFoliageV1");
                if(child==null) { child=new GameObject("NeighborhoodFoliageV1").transform; child.SetParent(prefab.transform,false); }
                MeshFilter filter=child.GetComponent<MeshFilter>();
                if(filter==null) filter=child.gameObject.AddComponent<MeshFilter>();
                MeshRenderer renderer=child.GetComponent<MeshRenderer>();
                if(renderer==null) renderer=child.gameObject.AddComponent<MeshRenderer>();
                filter.sharedMesh=mesh; renderer.sharedMaterial=material;
                renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
                renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                var trees=prefab.GetComponentInChildren<AirportCloseTreeLodRenderer>(true);
                child.gameObject.layer=trees.gameObject.layer;
                trees.ConfigureAuthoredFoliageReplacement(renderer,data.source_records.Select(r=>new Vector2(r.x,r.z)).ToArray());
                PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(root,"serialized_foliage_candidate.json"),JsonUtility.ToJson(new NeighborhoodCost {
                view="serialized_candidate",candidate=true,addedDraws=1,addedTriangles=data.indices.Length/3,
                sourceMeshSha256=BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(Path.Combine(root,"crowns.json")))).Replace("-","").ToLowerInvariant(),
                meshResidentBytes=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh) },true));
            System.Environment.SetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR",Path.Combine(root,"serialized_qa"));
            RunSettledSceneryBenchmark();
        }

        public static void RunNeighborhoodSceneryReview()
        {
            string root=System.Environment.GetEnvironmentVariable("QFL_NEIGHBORHOOD_ROOT");
            if(string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("QFL_NEIGHBORHOOD_ROOT required");
            const string branchAtlas="Assets/Production/Environment/NeighborhoodFoliageV1/NeighborhoodFoliageAtlas.png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(branchAtlas);
            importer.textureType=TextureImporterType.Default;
            importer.sRGBTexture=true; importer.alphaIsTransparency=true;
            importer.mipmapEnabled=true; importer.mipMapsPreserveCoverage=true; importer.alphaTestReferenceValue=.28f;
            importer.wrapMode=TextureWrapMode.Clamp; importer.filterMode=FilterMode.Trilinear; importer.anisoLevel=4;
            importer.maxTextureSize=2048;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name="Android",overridden=true,
                maxTextureSize=2048,format=TextureImporterFormat.ASTC_6x6,compressionQuality=100 });
            importer.SaveAndReimport();
            var data=JsonUtility.FromJson<NeighborhoodCrownData>(File.ReadAllText(Path.Combine(root,"crowns.json")));
            if(data.schema!="questflight.neighborhood-crowns.v1" || data.vertices.Length!=data.normals.Length ||
                data.vertices.Length!=data.colors.Length || data.vertices.Length!=data.uv.Length || data.vertices.Length!=data.modes.Length ||
                data.indices.Any(i=>i<0 || i>=data.vertices.Length)) throw new InvalidOperationException("Invalid crown data");
            var mesh=new Mesh { name="Neighborhood_Crown_Review", hideFlags=HideFlags.DontSave, indexFormat=data.vertices.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16,
                vertices=data.vertices, normals=data.normals,colors32=data.colors.Select(c=>(Color32)c).ToArray(),uv=data.uv,uv2=data.modes,triangles=data.indices };
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            var material=new Material(Shader.Find("Hidden/QuestFlight/Neighborhood Canopy Review")) { hideFlags=HideFlags.DontSave };
            const string textureRoot="Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy/Textures/";
            material.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(textureRoot+"ProductionCanopyFoliage_Albedo.png"));
            material.SetTexture("_LeafAtlas",AssetDatabase.LoadAssetAtPath<Texture2D>(branchAtlas));
            var terrain=AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(QuestFlight.Editor.FullMapBuildingClipmapAssetAuthoring.TerrainSurfacePath);
            var positions=new HashSet<Vector2Int>(data.source_records.Select(r=>new Vector2Int(Mathf.RoundToInt(r.x*100),Mathf.RoundToInt(r.z*100))));
            var result=new NeighborhoodReview();
            var roofMeshes=File.Exists(Path.Combine(root,"roofs.json"))
                ? QuestFlight.Editor.FullMapBuildingClipmapAssetAuthoring.CreateNeighborhoodRoofReviewMeshes(Path.Combine(root,"roofs.json"))
                : new Dictionary<string,Mesh>();
            NeighborhoodCost cost=null;
            Camera camera=null;
            AircraftCenteredBuildingClipmapController buildingClipmap=null;
            int layer=0;
            Camera.CameraCallback draw=c=>
            {
                if(c!=camera || cost==null || !cost.candidate) return;
                if(buildingClipmap!=null)
                    foreach(var filter in buildingClipmap.pageFilters)
                        if(filter.sharedMesh!=null && roofMeshes.TryGetValue(filter.sharedMesh.name,out var replacement) && filter.sharedMesh!=replacement)
                        {
                            cost.buildingTriangleDelta+=(int)(replacement.GetIndexCount(0)-filter.sharedMesh.GetIndexCount(0))/3;
                            filter.sharedMesh=replacement;
                        }
                if(!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(c),mesh.bounds)) return;
                Graphics.DrawMesh(mesh,Matrix4x4.identity,material,layer,c,0,null,ShadowCastingMode.Off,false,null,LightProbeUsage.Off);
                cost.addedDraws++; cost.addedTriangles+=data.indices.Length/3;
            };
            string previous=System.Environment.GetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW");
            try
            {
                System.Environment.SetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW","1");
                AirportCloseTreeLodRenderer.OmitHybridPrototypeInstanceForVisualQa=(p,m)=>
                {
                    if(cost==null || !positions.Contains(new Vector2Int(Mathf.RoundToInt(p.x*100),Mathf.RoundToInt(p.z*100)))) return false;
                    cost.baselineCrownsReplaced++; cost.baselineTrianglesReplaced+=(int)m.GetIndexCount(0)/3;
                    return cost.candidate;
                };
                AirportCloseTreeLodRenderer.ObserveHybridPrototypeTierForVisualQa=(m,n)=>
                { if(cost!=null) { cost.treeDraws+=(n+1022)/1023; cost.treeTriangles+=n*(int)m.GetIndexCount(0)/3; } };
                Camera.onPreCull+=draw;QuestFlight.Rendering.ProductRenderSubmission.Draw+=draw;
                foreach(bool candidate in new[]{false,true})
                {
                    string output=Path.Combine(root,candidate?"candidate":"control");
                    Directory.CreateDirectory(Path.Combine(output,"screenshots"));
                    var report=new ProductionVisualQaReport { generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                        scenePath=ProductionScenePath,outputDirectory=output,renderWidth=1280,renderHeight=720,humanHeadsetAcceptance="NOT_RUN",
                        flightPhaseImageSemantics="Unsaved Editor pairs; no product or headset acceptance." };
                    var context=LoadContext(1280,720,report); camera=context.camera;
                    buildingClipmap=context.environment.GetComponentInChildren<AircraftCenteredBuildingClipmapController>();
                    layer=context.environment.GetComponentInChildren<AirportCloseTreeLodRenderer>().gameObject.layer;
                    foreach(int height in new[]{35,100,300,500})
                    {
                        Vector3 target=new Vector3(-800,0,-700);
                        Vector3 center=target+new Vector3(0,height,-height*1.2f);
                        Quaternion rotation=LookAt(center,target);
                        foreach(int eye in new[]{-1,1})
                        {
                            string id="neighborhood_"+height+(eye<0?"_left":"_right");
                            terrain.TrySample(center.x,center.z,out var sample);
                            cost=new NeighborhoodCost { view=id,candidate=candidate,cameraTerrainAgl=center.y-sample.heightMeters,
                                meshResidentBytes=candidate?UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh):0 };
                            Capture(context,report,output,1280,720,new ShotDefinition(id,"Neighborhood crown volume review","editor_pair",
                                center+rotation*Vector3.right*(eye*.032f),rotation,55,ViewProfile.Environment));
                            result.views.Add(cost); cost=null;
                        }
                    }
                    report.classification="EDITOR_LOOK_DEVELOPMENT_ONLY";
                    WriteReportFiles(report,output);
                }
                File.WriteAllText(Path.Combine(root,"costs.json"),JsonUtility.ToJson(result,true));
            }
            finally
            {
                Camera.onPreCull-=draw;QuestFlight.Rendering.ProductRenderSubmission.Draw-=draw;
                AirportCloseTreeLodRenderer.OmitHybridPrototypeInstanceForVisualQa=null;
                AirportCloseTreeLodRenderer.ObserveHybridPrototypeTierForVisualQa=null;
                System.Environment.SetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW",previous);
                UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(material);
                foreach(var roof in roofMeshes.Values) UnityEngine.Object.DestroyImmediate(roof);
            }
        }
    }
}
