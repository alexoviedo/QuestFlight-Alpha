using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using QuestFlightLab.Environment;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        private const string BranchAssets="Assets/Production/Environment/MapBranchGrovesV1";
        private static string BranchRoot=>System.Environment.GetEnvironmentVariable("QFL_MAP_BRANCH_ROOT")??throw new InvalidOperationException("QFL_MAP_BRANCH_ROOT required");
        private sealed class BranchMeshBuilder
        {
            public readonly List<Vector3> p=new List<Vector3>();
            public readonly List<Color> c=new List<Color>();
            public readonly List<Vector2> uv=new List<Vector2>();
            public readonly List<int> ix=new List<int>();
            public Mesh Mesh(string name)
            {
                var mesh=new Mesh{name=name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                mesh.SetVertices(p);mesh.SetColors(c);mesh.SetUVs(0,uv);mesh.SetTriangles(ix,0);mesh.RecalculateBounds();return mesh;
            }
        }
        public static void AuthorMapBranchGroves()
        {
            Directory.CreateDirectory(BranchAssets);string pages=Path.Combine(BranchRoot,"pages");
            string json=File.ReadAllText(Path.Combine(pages,"manifest.json"));
            var manifest=JsonUtility.FromJson<MapBranchGroveRenderer.Manifest>(json);
            if(!manifest.complete||manifest.pages.Length!=1296)throw new InvalidDataException("Incomplete map generation");
            string target="Assets/StreamingAssets/QuestFlightBranchGroves";Directory.CreateDirectory(target);
            foreach(var record in manifest.pages)
            {
                byte[] data=File.ReadAllBytes(Path.Combine(pages,record.file));MapBranchGroveRenderer.ValidatePageForVisualQa(data,record);
                File.WriteAllBytes(Path.Combine(target,record.file),data);
            }
            File.WriteAllText(BranchAssets+"/SOURCE_MANIFEST.json",json);AssetDatabase.Refresh();
            var mesh=new BranchMeshBuilder();
            for(int layer=0;layer<2;layer++)for(int plane=0;plane<3;plane++)
            {
                var rotation=Quaternion.Euler(plane==0?62:plane==1?18:-32,plane*120+layer*43,0);
                Vector3 right=rotation*Vector3.right,up=rotation*Vector3.up;int n=mesh.p.Count;
                mesh.p.AddRange(new[]{-right-up,-right+up,right+up,right-up});
                mesh.uv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right});
                for(int j=0;j<4;j++)mesh.c.Add(new Color32((byte)(layer*255),(byte)(plane*5+layer*3),0,255));
                mesh.ix.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});
            }
            var asset=mesh.Mesh("MapGrove_TwoLayerBranchCluster");asset.hideFlags=HideFlags.None;asset.RecalculateNormals();
            // Distance tiers reuse subsets of the exact accepted cards and UVs.
            // No enlarged billboards, new artwork or stretched leaf texels.
            asset.subMeshCount=3;
            asset.SetTriangles(mesh.ix.ToArray(),0);
            asset.SetTriangles(mesh.ix.GetRange(0,18).ToArray(),1);
            asset.SetTriangles(mesh.ix.GetRange(0,12).ToArray(),2);
            string meshPath=BranchAssets+"/BranchCluster.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(existing==null)AssetDatabase.CreateAsset(asset,meshPath);else{EditorUtility.CopySerialized(asset,existing);EditorUtility.SetDirty(existing);UnityEngine.Object.DestroyImmediate(asset);}
            var material=AssetDatabase.LoadAssetAtPath<Material>(BranchAssets+"/BranchGroves.mat");
            if(material==null){material=new Material(Shader.Find("QuestFlight/Map Branch Groves"));AssetDatabase.CreateAsset(material,BranchAssets+"/BranchGroves.mat");}
            material.enableInstancing=true;material.SetTexture("_LeafAtlas",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy/Textures/ProductionNearFoliageAtlas_RGBA.png"));
            material.SetFloat("_Diagnostic",0);EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
            string prefabPath=QuestFlight.Editor.FullMapBuildingClipmapAssetAuthoring.EnvironmentPath;
            var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var node=prefab.transform.Find("MapBranchGrovesV1");
                if(node==null){node=new GameObject("MapBranchGrovesV1").transform;node.SetParent(prefab.transform,false);}
                var renderer=node.GetComponent<MapBranchGroveRenderer>();if(renderer==null)renderer=node.gameObject.AddComponent<MapBranchGroveRenderer>();
                renderer.buildingClipmap=prefab.GetComponentInChildren<AircraftCenteredBuildingClipmapController>(true);
                renderer.branchMesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);renderer.branchMaterial=material;
                renderer.sourceManifest=AssetDatabase.LoadAssetAtPath<TextAsset>(BranchAssets+"/SOURCE_MANIFEST.json");renderer.enabled=true;
                // The scenery extends beyond the flyable area. Outer pilot observations
                // must remain inside the authored boundary-recovery trigger (10.5 km).
                var observationSites=new[]{new Vector2(-224,-789),new Vector2(-850,-600),new Vector2(0,3300),new Vector2(-5000,1500),new Vector2(4500,-4500),new Vector2(10000,0),new Vector2(-10000,0),new Vector2(0,-10000),new Vector2(0,10000)};
                renderer.observationStarts=new QuestFlightLab.Flight.Backends.FlightDynamicsInitialConditionProvider[observationSites.Length];
                var terrain=prefab.GetComponent<ProductionEnvironmentRoot>().authoritativeTerrainSurface;
                for(int i=0;i<observationSites.Length;i++)
                {
                    string name=$"Observation{i:00}";var pose=node.Find(name);
                    if(pose==null){pose=new GameObject(name).transform;pose.SetParent(node,false);}
                    Vector2 xy=observationSites[i]-new Vector2(180,300);if(!terrain.TrySample(xy.x,xy.y,out var ground))throw new InvalidOperationException("Map observation terrain missing");
                    pose.position=new Vector3(xy.x,ground.heightMeters+115,xy.y);pose.rotation=Quaternion.identity;
                    var provider=pose.GetComponent<QuestFlightLab.Flight.Backends.FlightDynamicsInitialConditionProvider>();
                    if(provider==null)provider=pose.gameObject.AddComponent<QuestFlightLab.Flight.Backends.FlightDynamicsInitialConditionProvider>();
                    provider.derivePositionFromSpawnTransform=true;provider.deriveHeadingFromSpawnTransform=true;provider.spawnTransform=pose;provider.spawnHeightAboveTerrainMeters=115;
                    provider.calibratedAirspeedKnots=85;provider.engineRunning=true;provider.pitchDegrees=0;provider.bankDegrees=0;provider.flightPathAngleDegrees=0;
                    renderer.observationStarts[i]=provider;
                }
                node.gameObject.layer=prefab.GetComponentInChildren<AirportCloseTreeLodRenderer>(true).gameObject.layer;
                var environment=prefab.GetComponent<ProductionEnvironmentRoot>();var budget=environment.CalculateBudget();
                environment.bakedRendererCount=budget.rendererCount;environment.bakedTriangleCount=budget.triangleCount;environment.bakedMaterialCount=budget.materialCount;
                PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
            var scene=EditorSceneManager.OpenScene(ProductionScenePath);
            var authored=UnityEngine.Object.FindFirstObjectByType<MapBranchGroveRenderer>();
            authored.viewingCamera=Camera.main;if(authored.viewingCamera==null)throw new InvalidOperationException("Authored tracked camera missing");
            EditorUtility.SetDirty(authored);PrefabUtility.RecordPrefabInstancePropertyModifications(authored);
            var sceneEnvironment=UnityEngine.Object.FindFirstObjectByType<ProductionEnvironmentRoot>();var sceneBudget=sceneEnvironment.CalculateBudget();
            sceneEnvironment.bakedRendererCount=sceneBudget.rendererCount;sceneEnvironment.bakedTriangleCount=sceneBudget.triangleCount;sceneEnvironment.bakedMaterialCount=sceneBudget.materialCount;
            EditorUtility.SetDirty(sceneEnvironment);PrefabUtility.RecordPrefabInstancePropertyModifications(sceneEnvironment);
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("[MapBranchGroves] Authored 1296 pages and one shared renderer.");
        }
    }
}
