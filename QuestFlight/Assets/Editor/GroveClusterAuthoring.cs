using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using QuestFlightLab.Environment;

namespace QuestFlightLab.Editor
{
    public static class GroveClusterAuthoring
    {
        private const string Root="Assets/Production/Environment/MapBranchGrovesV1/DistantClusters";
        private struct Cell {public Vector4 positionHeight,tintRadius,rotationFade;}
        [Serializable] private sealed class SourceFile {public string path,sha256;}
        [Serializable] private sealed class SourceRecord
        {
            public string schema="questflight.distant-branch-clusters.v1";
            public string generator="GroveClusterAuthoring.Bake";
            public string license="Needle imagery: Poly Haven Pine Tree 01, CC0 1.0, https://polyhaven.com/a/pine_tree_01 ; https://polyhaven.com/license . Broadleaf: project-owned generated artwork, subject to applicable OpenAI output terms; no third-party image inputs. Geometry: project-authored derivative.";
            public string[] provenance={"Assets/Production/Environment/MapBranchGrovesV1/TreeRealism/ConiferArt_SOURCE.json","Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy/ProductionNearFoliageAtlas_SourceManifest.json"};
            public int templates=36,viewsPerTemplate=25,framePixels=64;
            public string representation="Two families, nine terrain slope fits, two/three upper-card variants. Directions -35/0/45 degrees x eight azimuths plus zenith. Shared small groups, not whole-grove textures; performance acceptance pending.";
            public SourceFile[] inputs,outputs;
        }
        private static SourceFile Describe(string path)
        {
            using var sha=System.Security.Cryptography.SHA256.Create();using var stream=File.OpenRead(path);
            return new SourceFile{path=path,sha256=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()};
        }
        public static void WriteSourceRecord()
        {
            AssetDatabase.SaveAssets();
            var record=new SourceRecord{inputs=new[]{Describe("Assets/Production/Environment/MapBranchGrovesV1/BranchCluster.asset"),
                Describe("Assets/Production/Environment/MapBranchGrovesV1/TreeRealism/NeedleBranchAtlas.png"),
                Describe("Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy/Textures/ProductionNearFoliageAtlas_RGBA.png")},
                outputs=new[]{Describe(Root+"/BranchClusterColor.png"),Describe(Root+"/BranchClusterNormal.png"),Describe(Root+"/ClusterQuad.asset")}};
            File.WriteAllText(Root+"/SOURCE.json",JsonUtility.ToJson(record,true)+"\n");AssetDatabase.Refresh();
        }
        [Serializable] private sealed class CoveragePage { public string file,sha256; public int cells,clusters,geometryFallbackCells; }
        [Serializable] private sealed class Coverage { public string schema="questflight.distant-cluster-coverage.v1"; public string qualification="Four contiguous cells in the same connected grove and family; bounded radius, tint, height/slope error. Runtime retains geometry for incomplete selection, close/unsupported views and transitions. No coverage or distance increase."; public System.Collections.Generic.List<CoveragePage> pages=new(); }
        public static void PrepareCandidate()
        {
            const string prefab="Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
            var root=PrefabUtility.LoadPrefabContents(prefab);
            try
            {
                var grove=root.GetComponentInChildren<MapBranchGroveRenderer>(true);
                if(grove==null)throw new InvalidOperationException("Authored grove renderer missing");
                grove.distantClusterMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/ClusterQuad.asset");
                grove.distantClusterMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Cluster.mat");
                if(grove.distantClusterMesh==null||grove.distantClusterMaterial==null)throw new InvalidOperationException("Cluster bake missing");
                grove.distantClustersEnabled=true;
                PrefabUtility.SaveAsPrefabAsset(root,prefab);
                var manifest=JsonUtility.FromJson<MapBranchGroveRenderer.Manifest>(grove.sourceManifest.text);
                var coverage=new Coverage();
                foreach(var record in manifest.pages)
                {
                    int clusters=MapBranchGroveRenderer.CountClustersForVisualQa(File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath,grove.pageDirectory,record.file)),record);
                    coverage.pages.Add(new CoveragePage{file=record.file,sha256=record.sha256,cells=record.count,clusters=clusters,geometryFallbackCells=record.count-clusters*4});
                }
                File.WriteAllText(Path.Combine(System.Environment.GetEnvironmentVariable("QFL_TREE_TURN_ROOT"),"map_cluster_coverage.json"),JsonUtility.ToJson(coverage,true));
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
            Review();
        }
        public static void Bake()
        {
            bool previousAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            Directory.CreateDirectory(Root);
            EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            var source=UnityEngine.Object.FindFirstObjectByType<MapBranchGroveRenderer>();
            var shader=Shader.Find("Hidden/QuestFlight/Grove Cluster Bake");
            if(source==null||shader==null)throw new InvalidOperationException("Grove bake source/shader missing");
            var bakeMaterial=new Material(shader){enableInstancing=true};
            bakeMaterial.SetTexture("_LeafAtlas",source.branchMaterial.GetTexture("_LeafAtlas"));
            bakeMaterial.SetTexture("_NeedleBranchAtlas",source.branchMaterial.GetTexture("_NeedleBranchAtlas"));
            var host=new GameObject("Offline branch cluster bake camera");var camera=host.AddComponent<Camera>();
            camera.enabled=false;camera.cullingMask=0;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            camera.orthographic=true;camera.orthographicSize=5.5f;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            camera.allowHDR=false;camera.allowMSAA=true;camera.stereoTargetEye=StereoTargetEyeMask.None;
            var target=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=4};target.Create();
            var readback=new Texture2D(128,128,TextureFormat.RGBA32,false,true);
            var colorAtlas=new Texture2D(2048,2048,TextureFormat.RGBA32,true,true);
            var normalAtlas=new Texture2D(2048,2048,TextureFormat.RGBA32,true,true);
            var colorPixels=new Color32[2048*2048];var normalPixels=new Color32[colorPixels.Length];
            var buffer=new ComputeBuffer(4,48);var properties=new MaterialPropertyBlock();properties.SetBuffer("_BranchCells",buffer);
            var command=new CommandBuffer{name="Offline accepted branch capture"};
            camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque,command);camera.targetTexture=target;
            var cells=new Cell[4];var previousTarget=RenderTexture.active;
            try
            {
                for(int family=0;family<2;family++)for(int sz=-1;sz<=1;sz++)for(int sx=-1;sx<=1;sx++)for(int variant=0;variant<2;variant++)
                {
                    int template=family*18+(sz+1)*6+(sx+1)*2+variant;
                    // Adjacent templates contain the two- and three-card LODs.
                    // Their seeds match, so the third card fades without moving
                    // the other two branches or adding another texture atlas.
                    command.Clear();
                    command.DrawMeshInstancedProcedural(source.branchMesh,variant==0?2:1,bakeMaterial,0,4,properties);
                    for(int i=0;i<4;i++)
                    {
                        float x=(i%2-.5f)*MapBranchGroveRenderer.CellMeters,z=(i/2-.5f)*MapBranchGroveRenderer.CellMeters;
                        uint seed=unchecked((uint)(family*97+i*37+91)*1664525u+1013904223u);
                        float angle=(seed&65535)*(Mathf.PI*2/65536);
                        cells[i]=new Cell{positionHeight=new Vector4(x,sx*(i%2==0?-1:1)+sz*(i/2==0?-1:1)-10.4f,z,12),
                            tintRadius=new Vector4(.5f,.5f,.5f,2.25f),rotationFade=new Vector4(Mathf.Sin(angle),Mathf.Cos(angle),1,seed%16+family*16)};
                    }
                    buffer.SetData(cells);
                    for(int view=0;view<25;view++)
                    {
                        float elevation=view==24?90:new[]{-35f,0f,45f}[view/8];float azimuth=view==24?0:view%8*45;
                        float e=elevation*Mathf.Deg2Rad,a=azimuth*Mathf.Deg2Rad;
                        var direction=new Vector3(Mathf.Sin(a)*Mathf.Cos(e),Mathf.Sin(e),Mathf.Cos(a)*Mathf.Cos(e));
                        camera.transform.SetPositionAndRotation(direction*30,Quaternion.LookRotation(-direction,view==24?-Vector3.forward:Vector3.up));
                        int frame=template*25+view;
                        for(int pass=0;pass<2;pass++)
                        {
                            bakeMaterial.SetFloat("_BakeNormal",pass);camera.Render();RenderTexture.active=target;
                            readback.ReadPixels(new Rect(0,0,128,128),0,0);readback.Apply(false,false);var pixels=readback.GetPixels();
                            var atlas=pass==0?colorPixels:normalPixels;
                            int covered=0;
                            for(int y=0;y<64;y++)for(int x=0;x<64;x++)
                            {
                                int p=y*2*128+x*2;Color sample=(pixels[p]+pixels[p+1]+pixels[p+128]+pixels[p+129])*.25f;
                                // Coverage-resolved captures have premultiplied RGB.
                                if(sample.a>0){sample.r/=sample.a;sample.g/=sample.a;sample.b/=sample.a;}
                                if(sample.a>.01f)covered++;
                                atlas[(frame/32*64+y)*2048+frame%32*64+x]=sample;
                            }
                            if(covered<32)throw new InvalidOperationException($"Empty cluster bake frame {frame}, pass {pass}; refusing to publish a missing canopy.");
                        }
                    }
                }
                colorAtlas.SetPixels32(colorPixels);colorAtlas.Apply(true,false);normalAtlas.SetPixels32(normalPixels);normalAtlas.Apply(true,false);
                File.WriteAllBytes(Root+"/BranchClusterColor.png",colorAtlas.EncodeToPNG());File.WriteAllBytes(Root+"/BranchClusterNormal.png",normalAtlas.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previousTarget;camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque,command);command.Release();buffer.Release();
                camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(colorAtlas);UnityEngine.Object.DestroyImmediate(normalAtlas);UnityEngine.Object.DestroyImmediate(bakeMaterial);UnityEngine.Object.DestroyImmediate(host);
                ShaderUtil.allowAsyncCompilation=previousAsync;
            }
            AssetDatabase.Refresh();
            foreach(string name in new[]{"BranchClusterColor","BranchClusterNormal"})
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(Root+"/"+name+".png");
                importer.textureType=TextureImporterType.Default;importer.sRGBTexture=false;importer.alphaSource=TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency=false;importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=false;importer.isReadable=false;
                importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=1;importer.maxTextureSize=2048;
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Android",overridden=true,maxTextureSize=2048,format=TextureImporterFormat.ASTC_6x6,compressionQuality=100});
                importer.SaveAndReimport();
            }
            var mesh=new Mesh{name="DistantBranchClusterQuad",vertices=new[]{new Vector3(-5.5f,-5.5f,0),new Vector3(-5.5f,5.5f,0),new Vector3(5.5f,5.5f,0),new Vector3(5.5f,-5.5f,0)},
                uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right},triangles=new[]{0,2,1,0,3,2}};mesh.RecalculateBounds();
            var prior=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/ClusterQuad.asset");
            if(prior==null)AssetDatabase.CreateAsset(mesh,Root+"/ClusterQuad.asset");else{EditorUtility.CopySerialized(mesh,prior);UnityEngine.Object.DestroyImmediate(mesh);}
            var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Cluster.mat");
            if(material==null){material=new Material(Shader.Find("QuestFlight/Distant Branch Clusters"));AssetDatabase.CreateAsset(material,Root+"/Cluster.mat");}
            material.enableInstancing=true;material.SetTexture("_ColorAtlas",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/BranchClusterColor.png"));
            material.SetTexture("_NormalAtlas",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/BranchClusterNormal.png"));EditorUtility.SetDirty(material);
            File.WriteAllText(Root+"/SOURCE.json","{\"schema\":\"questflight.distant-branch-clusters.v1\",\"source\":\"Existing MapBranchGrovesV1 BranchCluster.asset, ProductionNearFoliageAtlas_RGBA.png and TreeRealism NeedleBranchAtlas.png; inherited provenance in their source manifests and docs/ASSET_SOURCES.md\",\"generator\":\"GroveClusterAuthoring.Bake\",\"license\":\"Derivative of existing foliage assets; original source/license records apply. No new external source.\",\"templates\":36,\"viewsPerTemplate\":25,\"framePixels\":64,\"scope\":\"Experimental distant LOD only; no performance acceptance\"}\n");
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();
            WriteSourceRecord();
            Debug.Log("[GroveClusterBake] Generated 900 views per atlas from accepted branch cards; two 2048px ASTC6x6 atlases.");
        }
        public static void Review()
        {
            bool previousAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            var grove=UnityEngine.Object.FindFirstObjectByType<MapBranchGroveRenderer>();
            // The review runner reloads the scene; assets are bound in the review
            // hook, without changing the serialized shipping enable flag.
            System.Environment.SetEnvironmentVariable("QFL_GROVE_CLUSTER_REVIEW","1");
            try{TreeHeadTurnReview.RunAcceptance();}
            finally{ShaderUtil.allowAsyncCompilation=previousAsync;System.Environment.SetEnvironmentVariable("QFL_GROVE_CLUSTER_REVIEW",null);}
        }
        public static void BakeAndReview(){Bake();Review();}
        public static void ReviewCpuOnly()
        {
            System.Environment.SetEnvironmentVariable("QFL_GROVE_CLUSTER_REVIEW","0");
            try{TreeHeadTurnReview.RunFixed();}
            finally{System.Environment.SetEnvironmentVariable("QFL_GROVE_CLUSTER_REVIEW",null);}
        }
        public static void BindForReview(MapBranchGroveRenderer grove)
        {
            if(System.Environment.GetEnvironmentVariable("QFL_GROVE_CLUSTER_REVIEW")=="0"){grove.distantClustersEnabled=false;return;}
            if(System.Environment.GetEnvironmentVariable("QFL_GROVE_CLUSTER_REVIEW")!="1")return;
            grove.distantClusterMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/ClusterQuad.asset");
            grove.distantClusterMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Cluster.mat");grove.distantClustersEnabled=true;
            if(grove.distantClusterMesh==null||grove.distantClusterMaterial==null)throw new InvalidOperationException("Cluster review assets missing");
        }
    }
}
