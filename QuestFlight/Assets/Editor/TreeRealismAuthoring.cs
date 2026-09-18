using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using QuestFlightLab.Environment;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        private static string TreeRealismRoot => System.Environment.GetEnvironmentVariable("QFL_TREE_REALISM_ROOT") ??
            throw new InvalidOperationException("QFL_TREE_REALISM_ROOT required");

        public static void RunTreeRealismBaseline()
        {
            ExportTreeRealismTerrain();
            RunTreeRealismReview();
        }

        public static void ValidateTreeRealism()
        {
            QuestFlightLab.Editor.Tests.MapBranchGroveTests.RunBatch();
            foreach(string path in new[]{QuestFlight.Editor.AirportCloseTreeLodAuthoring.Lod0AssetPath,QuestFlight.Editor.AirportCloseTreeLodAuthoring.Lod1AssetPath})
            {
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                var centers=new List<Vector4>();mesh.GetUVs(1,centers);
                if(centers.Count!=mesh.vertexCount)throw new InvalidDataException("Missing crown centers: "+path);
                var colors=mesh.colors32;
                for(int i=0;i<colors.Length;i++)if(colors[i].r<128&&!(centers[i].w>0&&centers[i].w<1))
                    throw new InvalidDataException("Invalid crown rank: "+path);
            }
            var canopy=AssetDatabase.LoadAssetAtPath<Material>(QuestFlight.Editor.AirportCloseTreeLodAuthoring.CanopyMaterialPath);
            if(canopy.GetFloat("_TreeRealismEnabled")<.5f||canopy.GetTexture("_ConiferTreeAtlas")==null||canopy.GetTexture("_NeedleBranchAtlas")==null)
                throw new InvalidDataException("Tree family artwork is not bound to the product material");
            var branches=AssetDatabase.LoadAssetAtPath<Material>("Assets/Production/Environment/MapBranchGrovesV1/BranchGroves.mat");
            if(branches.GetTexture("_NeedleBranchAtlas")!=canopy.GetTexture("_NeedleBranchAtlas"))
                throw new InvalidDataException("Grove and tree needle artwork differ or are missing");
            RunTreeRealismReview();
            if(ShaderUtil.ShaderHasError(canopy.shader)||ShaderUtil.ShaderHasError(Shader.Find("QuestFlight/Map Branch Groves")))
                throw new InvalidDataException("Tree shader compilation errors");
            Debug.Log("[TreeRealism] PASS: map page/LOD/culling regressions, authored mesh centers, bound family art and compiled review shaders.");
        }

        public static void AuthorTreeRealism()
        {
            string root=TreeRealismRoot;
            string art="Assets/Production/Environment/MapBranchGrovesV1/TreeRealism";
            Directory.CreateDirectory(art);
            foreach(string name in new[]{"NeedleBranchAtlas.png","ConiferTreeAtlas.png","ConiferArt_SOURCE.json"})
                File.Copy(Path.Combine(root,"art",name),Path.Combine(art,name),true);
            string pages=Path.Combine(root,"generated");
            var manifest=JsonUtility.FromJson<MapBranchGroveRenderer.Manifest>(File.ReadAllText(Path.Combine(pages,"manifest.json")));
            foreach(var page in manifest.pages)
            {
                byte[] data=File.ReadAllBytes(Path.Combine(pages,page.file));MapBranchGroveRenderer.ValidatePageForVisualQa(data,page);
                File.WriteAllBytes(Path.Combine(Application.streamingAssetsPath,"QuestFlightBranchGroves",page.file),data);
            }
            File.Copy(Path.Combine(pages,"manifest.json"),"Assets/Production/Environment/MapBranchGrovesV1/SOURCE_MANIFEST.json",true);
            File.Copy(Path.Combine(pages,"KBDU_FullMapVegetationHlodV3.bytes"),QuestFlight.Editor.AirportCloseTreeLodAuthoring.FullMapDatabasePath,true);
            File.Copy(Path.Combine(pages,"tree_realism_generation.json"),Path.Combine(art,"TreeRealism_SOURCE.json"),true);
            AssetDatabase.Refresh();
            foreach(string name in new[]{"NeedleBranchAtlas.png","ConiferTreeAtlas.png"})
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(art+"/"+name);
                importer.textureType=TextureImporterType.Default;importer.alphaSource=TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=true;
                importer.alphaTestReferenceValue=.30f;importer.filterMode=FilterMode.Trilinear;importer.wrapMode=TextureWrapMode.Clamp;
                importer.maxTextureSize=name.StartsWith("Needle")?1024:2048;
                var android=importer.GetPlatformTextureSettings("Android");android.overridden=true;
                android.format=TextureImporterFormat.ASTC_6x6;android.maxTextureSize=importer.maxTextureSize;
                importer.SetPlatformTextureSettings(android);importer.SaveAndReimport();
            }
            foreach(string path in new[]{QuestFlight.Editor.AirportCloseTreeLodAuthoring.Lod0AssetPath,QuestFlight.Editor.AirportCloseTreeLodAuthoring.Lod1AssetPath})
            {
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);var vertices=mesh.vertices;var colors=mesh.colors32;var indices=mesh.triangles;
                var parent=new int[vertices.Length];for(int i=0;i<parent.Length;i++)parent[i]=i;
                int Find(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
                for(int i=0;i<indices.Length;i+=3)
                    if(colors[indices[i]].r<128&&colors[indices[i+1]].r<128&&colors[indices[i+2]].r<128)
                    {parent[Find(indices[i+1])]=Find(indices[i]);parent[Find(indices[i+2])]=Find(indices[i]);}
                var groups=new Dictionary<int,List<int>>();
                for(int i=0;i<vertices.Length;i++)if(colors[i].r<128)
                {int key=Find(i);if(!groups.TryGetValue(key,out var list))groups[key]=list=new List<int>();list.Add(i);}
                var centers=new List<Vector4>(new Vector4[vertices.Length]);int ordinal=0;
                foreach(var group in groups.Values)
                {
                    Vector3 center=Vector3.zero;foreach(int i in group)center+=vertices[i];center/=group.Count;
                    float rank=(ordinal/3+.5f)/Mathf.Ceil(groups.Count/3f);ordinal++;
                    foreach(int i in group)centers[i]=new Vector4(center.x,center.y,center.z,rank);
                }
                mesh.SetUVs(1,centers);EditorUtility.SetDirty(mesh);
            }
            BindTreeRealismArtwork();
            Debug.Log("[TreeRealism] Authored full map with unchanged mesh triangle counts and record widths.");
            RunTreeRealismReview();
        }

        public static void BindTreeRealismArtwork()
        {
            const string art="Assets/Production/Environment/MapBranchGrovesV1/TreeRealism";
            foreach(string name in new[]{"NeedleBranchAtlas.png","ConiferTreeAtlas.png"})
                AssetDatabase.ImportAsset(art+"/"+name,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var needle=AssetDatabase.LoadAssetAtPath<Texture2D>(art+"/NeedleBranchAtlas.png");
            var trees=AssetDatabase.LoadAssetAtPath<Texture2D>(art+"/ConiferTreeAtlas.png");
            if(needle==null||trees==null)throw new InvalidDataException("Tree art import did not produce both Texture2D assets");
            var canopy=AssetDatabase.LoadAssetAtPath<Material>(QuestFlight.Editor.AirportCloseTreeLodAuthoring.CanopyMaterialPath);
            canopy.SetTexture("_NeedleBranchAtlas",needle);canopy.SetTexture("_ConiferTreeAtlas",trees);canopy.SetFloat("_TreeRealismEnabled",1);EditorUtility.SetDirty(canopy);
            var branches=AssetDatabase.LoadAssetAtPath<Material>("Assets/Production/Environment/MapBranchGrovesV1/BranchGroves.mat");
            branches.SetTexture("_NeedleBranchAtlas",needle);EditorUtility.SetDirty(branches);
            AssetDatabase.SaveAssets();
            Debug.Log("[TreeRealism] Bound both validated texture imports to product materials.");
        }

        // Offline samples of the exact authored terrain, including mesh transition
        // patches. The compiler never substitutes a second terrain interpretation.
        public static void ExportTreeRealismTerrain()
        {
            string root=TreeRealismRoot;Directory.CreateDirectory(root);
            EditorSceneManager.OpenScene(ProductionScenePath);
            var terrain=UnityEngine.Object.FindFirstObjectByType<ProductionEnvironmentRoot>().authoritativeTerrainSurface;
            var grove=UnityEngine.Object.FindFirstObjectByType<MapBranchGroveRenderer>();
            var manifest=JsonUtility.FromJson<MapBranchGroveRenderer.Manifest>(grove.sourceManifest.text);
            Directory.CreateDirectory(Path.Combine(root,"terrain"));
            double errorSum=0;float maxError=0;long count=0;
            foreach(var page in manifest.pages)
            {
                byte[] data=File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath,grove.pageDirectory,page.file));
                MapBranchGroveRenderer.ValidatePageForVisualQa(data,page);
                using var writer=new BinaryWriter(File.Create(Path.Combine(root,"terrain",page.file)));
                for(int offset=16;offset<data.Length;offset+=20)
                {
                    int cell=BitConverter.ToUInt16(data,offset);
                    float x=-12096+page.column*672+(cell%252+.5f)*(8f/3f);
                    float z=-12096+page.row*672+(cell/252+.5f)*(8f/3f);
                    if(!terrain.TrySample(x,z,out var sample))throw new InvalidOperationException("Missing cell terrain");
                    writer.Write(sample.heightMeters);writer.Write(sample.normal.z);
                    float error=Mathf.Abs(sample.heightMeters-BitConverter.ToSingle(data,offset+4));
                    errorSum+=error;maxError=Mathf.Max(error,maxError);count++;
                }
                if(page.column==0)Debug.Log("[TreeRealism] Terrain row "+page.row);
            }
            string treePath="Assets/Production/Environment/FullMapVegetationV3/KBDU_FullMapVegetationHlodV3.bytes";
            using(var reader=new BinaryReader(File.OpenRead(treePath)))
            using(var writer=new BinaryWriter(File.Create(Path.Combine(root,"terrain","tree_roots.bytes"))))
            {
                reader.ReadBytes(12);float x0=reader.ReadSingle(),z0=reader.ReadSingle(),size=reader.ReadSingle(),q=reader.ReadSingle();
                int columns=reader.ReadInt32(),rows=reader.ReadInt32(),total=reader.ReadInt32();
                var counts=new int[columns*rows];for(int i=0;i<counts.Length;i++){reader.ReadInt32();counts[i]=reader.ReadInt32();}
                for(int page=0;page<counts.Length;page++)for(int i=0;i<counts[page];i++)
                {
                    float x=x0+(page%columns)*size+reader.ReadUInt16()*q,z=z0+(page/columns)*size+reader.ReadUInt16()*q;
                    reader.ReadBytes(15);
                    if(!terrain.TrySample(x,z,out var sample))throw new InvalidOperationException("Missing tree terrain");
                    writer.Write(sample.heightMeters);writer.Write(sample.normal.z);
                }
            }
            File.WriteAllText(Path.Combine(root,"terrain_audit.json"),"{\"cells\":"+count+",\"meanAbsoluteBaseError\":"+
                (errorSum/count).ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"maximumAbsoluteBaseError\":"+
                maxError.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
        }

        public static void RunTreeRealismReview()
        {
            string root=Path.Combine(TreeRealismRoot,"review");Directory.CreateDirectory(Path.Combine(root,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                scenePath=ProductionScenePath,outputDirectory=root,renderWidth=1920,renderHeight=1080,
                humanHeadsetAcceptance="NOT_RUN",flightPhaseImageSemantics="Offline seat-position scenery diagnostic with aircraft hidden; not cockpit visibility acceptance, tracked XR or hardware timing."};
            var context=LoadContext(1920,1080,report);
            var grove=context.environment.GetComponentInChildren<MapBranchGroveRenderer>(true);
            Camera.CameraCallback draw=c=>{if(c==context.camera)grove.DrawForVisualQa(c);};
            string previous=System.Environment.GetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW");
            string previousBranch=System.Environment.GetEnvironmentVariable("QFL_MAP_BRANCH_REVIEW_ACTIVE");
            System.Environment.SetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW","1");
            System.Environment.SetEnvironmentVariable("QFL_MAP_BRANCH_REVIEW_ACTIVE","1");Camera.onPreCull+=draw;
            try
            {
                foreach(int site in new[]{0,3,6})foreach(int yaw in new[]{-70,70})foreach(int eye in new[]{-1,1})
                {
                    var spawn=grove.observationStarts[site].spawnTransform;
                    SetAircraftPose(context,spawn.position,spawn.rotation);
                    Vector3 position=context.rig.PilotSeatAnchor.position+spawn.right*(eye*.032f);
                    Quaternion direction=spawn.rotation*Quaternion.Euler(20,yaw,0);
                    context.camera.transform.SetPositionAndRotation(position,direction);context.camera.fieldOfView=72;
                    grove.PrepareForVisualQa(context.camera);
                    string id=$"trees_{site}_{yaw}_"+(eye<0?"left":"right");
                    Capture(context,report,root,1920,1080,new ShotDefinition(id,"Tree structure seat-position diagnostic (aircraft hidden)","tree_realism",position,direction,72,ViewProfile.Environment));
                }
                WriteReportFiles(report,root);
                foreach(var shot in report.shots)if(!string.IsNullOrEmpty(shot.error)||!File.Exists(shot.screenshotPath))
                    throw new InvalidOperationException("Tree capture failed: "+shot.id+" "+shot.error);
            }
            finally
            {
                Camera.onPreCull-=draw;grove.ClearForVisualQa();
                System.Environment.SetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW",previous);
                System.Environment.SetEnvironmentVariable("QFL_MAP_BRANCH_REVIEW_ACTIVE",previousBranch);
            }
        }
    }
}
