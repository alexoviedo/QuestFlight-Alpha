using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlight.Product;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Editor
{
    /// <summary>Offline authoring only. No runtime construction of product hierarchy.</summary>
    public static class BoulderAmbientAircraftAuthoring
    {
        public const string Root = "Assets/Production/Environment/AmbientAircraftV1";
        public const string WindPath = Root + "/BoulderWind.asset";
        const string World = "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        const string Aircraft = "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        static ProductionTerrainSurfaceAsset terrain;

        public static void RunBatch()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        [MenuItem("QuestFlight/Product/Author Boulder Ambient Aircraft")]
        public static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            terrain = AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(
                "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");
            if (terrain == null) throw new InvalidOperationException("Missing terrain authority");
            var wind = BakeWind();
            var mat = Asset<Material>(Root + "/Exterior.mat", () => new Material(Shader.Find("QuestFlight/Ambient Aircraft Exterior")));
            mat.enableInstancing = true; EditorUtility.SetDirty(mat);
            var aircraft = PrefabUtility.LoadPrefabContents(Aircraft);
            aircraft.GetComponentsInChildren<FlightDynamicsCoordinator>(true).Single().windField = wind;
            PrefabUtility.SaveAsPrefabAsset(aircraft, Aircraft); PrefabUtility.UnloadPrefabContents(aircraft);
            var world = PrefabUtility.LoadPrefabContents(World);
            try
            {
                Transform old = world.transform.Find("AmbientAircraftRoot");
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                var root = new GameObject("AmbientAircraftRoot"); root.transform.SetParent(world.transform, false);
                var director = root.AddComponent<AmbientTrafficDirector>();
                director.actors = new AmbientAircraftActor[5];
                string[] kinds = { "C172", "SuperCub", "C172", "Discus", "Discus" };
                for (int i = 0; i < 5; i++)
                {
                    var go = new GameObject(kinds[i] + "_" + i); go.transform.SetParent(root.transform, false);
                    var a = go.AddComponent<AmbientAircraftActor>(); director.actors[i] = a;
                    a.aircraftId = go.name; a.sailplane = i >= 3; a.taildragger = i == 1;
                    a.mountainSoaring = i == 4;
                    a.spanMeters = a.sailplane ? 15 : 11; a.bodyRadius = a.spanMeters * .55f;
                    a.wheelHeight = i == 1 ? 1.24f : 1.325f;
                    a.initialDelay = i == 0 ? 12 : i == 1 ? 75 : 150;
                    // The first useful encounter is nearby apron motion, rather than a tiny distant aircraft.
                    a.initialSegment = 0;
                    a.soaringCenter = i == 3 ? new Vector3(1700, 550, 2600) : new Vector3(-5500, Ground(-5500, 1800) + 240, 1800);
                    a.soaringRadius = i == 3 ? 90 : 160;
                    a.initialOrbitAngle = i; a.airspeed = 25;
                    BuildModel(a, kinds[i], mat);
                    if (!a.sailplane)
                    {
                        a.runway08 = BakeRoute(i, 8, a.wheelHeight);
                        a.runway26 = BakeRoute(i, 26, a.wheelHeight);
                    }
                    a.Initialize(8);
                }
                Bind(world, wind, director);
                BuildWindSock(world, wind, director.player, mat);
                UpdateMenu(world);
                PrefabUtility.SaveAsPrefabAsset(world, World);
            }
            finally { PrefabUtility.UnloadPrefabContents(world); }
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var sceneRoots = scene.GetRootGameObjects();
            var sceneDirector = sceneRoots.SelectMany(r => r.GetComponentsInChildren<AmbientTrafficDirector>(true)).Single();
            var scenePlayer = sceneRoots.SelectMany(r => r.GetComponentsInChildren<FlightDynamicsCoordinator>(true)).Single();
            scenePlayer.windField = wind;
            sceneDirector.player = scenePlayer;
            sceneDirector.flightMode = sceneRoots.SelectMany(r => r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
            sceneDirector.pilotCamera = sceneRoots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.enabled && c.gameObject.activeInHierarchy && c.CompareTag("MainCamera"));
            PrefabUtility.RecordPrefabInstancePropertyModifications(scenePlayer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(sceneDirector);
            foreach(var root in sceneRoots)
            {
                UpdateMenu(root);
                foreach(var environment in root.GetComponentsInChildren<ProductionEnvironmentRoot>(true))
                {
                    environment.bakedTriangleCount=environment.CalculateBudget().triangleCount;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(environment);
                }
            }
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[BoulderAmbient] Authored five actors, six complete routes, shared wind and scene references.");
        }

        static void Bind(GameObject root, BoulderWindFieldProfile wind, AmbientTrafficDirector d)
        {
            d.player = root.GetComponentsInChildren<FlightDynamicsCoordinator>(true).Single();
            d.player.windField = wind; PrefabUtility.RecordPrefabInstancePropertyModifications(d.player);
            d.flightMode = root.GetComponentsInChildren<ProductFlightModeCoordinator>(true).Single();
            d.pilotCamera = root.GetComponentsInChildren<Camera>(true).Single(c => c.enabled && c.gameObject.activeInHierarchy && c.CompareTag("MainCamera"));
            d.windField = wind; d.terrain = terrain;
            foreach (var eagle in root.GetComponentsInChildren<GoldenEagleThermalSoaring>(true))
            { eagle.windField = wind; eagle.windAuthority = d.player; PrefabUtility.RecordPrefabInstancePropertyModifications(eagle); }
            foreach (var cloud in root.GetComponentsInChildren<BoulderCloudField>(true))
            { cloud.windField = wind; cloud.windAuthority = d.player; PrefabUtility.RecordPrefabInstancePropertyModifications(cloud); }
        }
        static void BuildWindSock(GameObject world, BoulderWindFieldProfile wind, FlightDynamicsCoordinator player, Material material)
        {
            var original=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Production/Environment/AirportNearFieldV1/Generated/AirportNearFieldCombined.asset");
            if(original==null)throw new InvalidOperationException("Missing windsock source mesh");
            var mesh=UnityEngine.Object.Instantiate(original);var triangles=mesh.triangles;var vertices=mesh.vertices;var colors=mesh.colors;
            var kept=new List<int>();int removed=0;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var p=vertices[triangles[i]];var c=colors[triangles[i]];
                bool sock=Mathf.Abs(p.x+79.5f)<4 && Mathf.Abs(p.z+123)<4 && c.r>.7f && c.g<.4f && c.b<.15f;
                if(sock)removed++;else {kept.Add(triangles[i]);kept.Add(triangles[i+1]);kept.Add(triangles[i+2]);}
            }
            if(removed!=8)throw new InvalidOperationException("Expected exactly eight static sock triangles, found "+removed);
            mesh.triangles=kept.ToArray();mesh.RecalculateBounds();
            var replacement=Asset<Mesh>(Root+"/NearFieldWithWindSockRemoved.asset",()=>new Mesh());EditorUtility.CopySerialized(mesh,replacement);UnityEngine.Object.DestroyImmediate(mesh);
            foreach(var mf in world.GetComponentsInChildren<MeshFilter>(true))
                if(mf.sharedMesh==original || mf.sharedMesh==replacement) {mf.sharedMesh=replacement;PrefabUtility.RecordPrefabInstancePropertyModifications(mf);}
            foreach(var environment in world.GetComponentsInChildren<ProductionEnvironmentRoot>(true))
            {
                var budget=environment.CalculateBudget();
                environment.bakedTriangleCount=budget.triangleCount;
                EditorUtility.SetDirty(environment); PrefabUtility.RecordPrefabInstancePropertyModifications(environment);
            }
            var old=world.transform.Find("BoulderWindSock");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            var root=new GameObject("BoulderWindSock");root.transform.SetParent(world.transform,false);root.transform.position=new Vector3(-79.5f,Ground(-79.5f,-123)+6.45f,-123);
            var fabric=new GameObject("Fabric");fabric.transform.SetParent(root.transform,false);fabric.transform.localRotation=Quaternion.Euler(80,0,0);
            var v=new List<Vector3>();var n=new List<Vector3>();var cs=new List<Color>();var tr=new List<int>();
            for(int ring=0;ring<5;ring++)for(int side=0;side<8;side++)
            {
                float a=side*Mathf.PI/4, r=Mathf.Lerp(.35f,.13f,ring/4f);
                v.Add(new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,ring*.875f));n.Add(new Vector3(Mathf.Cos(a),Mathf.Sin(a),0));
                cs.Add(ring%2==0?new Color(.9f,.3f,.05f):new Color(.9f,.86f,.75f));
                if(ring<4){int i=ring*8+side,j=ring*8+(side+1)%8;tr.AddRange(new[]{i,j,j+8,i,j+8,i+8});}
            }
            var sockMesh=Asset<Mesh>(Root+"/WindSock.asset",()=>new Mesh());sockMesh.Clear();sockMesh.SetVertices(v);sockMesh.SetNormals(n);sockMesh.SetColors(cs);sockMesh.SetTriangles(tr,0);sockMesh.RecalculateBounds();EditorUtility.SetDirty(sockMesh);
            fabric.AddComponent<MeshFilter>().sharedMesh=sockMesh;var renderer=fabric.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            var behavior=root.AddComponent<BoulderWindSock>();behavior.windField=wind;behavior.player=player;behavior.fabric=fabric.transform;
        }
        static void UpdateMenu(GameObject root)
        {
            var coordinator=root.GetComponentsInChildren<ProductFlightModeCoordinator>(true).SingleOrDefault();
            foreach (var b in root.GetComponentsInChildren<ProductFlightModeMenuButton>(true))
                if (b.Action == ProductFlightModeMenuButton.MenuAction.Lessons || b.Action == ProductFlightModeMenuButton.MenuAction.Weather)
                {
                    var so = new SerializedObject(b); so.FindProperty("action").intValue = (int)ProductFlightModeMenuButton.MenuAction.Weather;
                    so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.RecordPrefabInstancePropertyModifications(b);
                    foreach (var t in b.GetComponentsInChildren<TextMesh>(true))
                    {
                        t.text = t.text.Replace("FLIGHT LESSONS", "WEATHER").Replace("COMING NEXT", "CALM / FOOTHILL SOARING");
                        if(t.text.Contains("CALM") && coordinator!=null)
                        {
                            t.text="CALM";
                            var weather=new SerializedObject(coordinator);weather.FindProperty("weatherMenuLabel").objectReferenceValue=t;
                            weather.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.RecordPrefabInstancePropertyModifications(coordinator);
                        }
                        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                    }
                }
            foreach(var view in root.GetComponentsInChildren<ProductFlightModeMenuView>(true))
            {
                var so=new SerializedObject(view);var field=so.FindProperty("worldPresentationRenderers");
                var all=new List<Renderer>();for(int i=0;i<field.arraySize;i++)if(field.GetArrayElementAtIndex(i).objectReferenceValue is Renderer r)all.Add(r);
                foreach(var a in root.GetComponentsInChildren<AmbientAircraftActor>(true))all.AddRange(a.renderers);
                foreach(var sock in root.GetComponentsInChildren<BoulderWindSock>(true))all.AddRange(sock.GetComponentsInChildren<Renderer>(true));
                view.ConfigureWorldPresentationRenderers(all.Distinct().ToArray());PrefabUtility.RecordPrefabInstancePropertyModifications(view);
            }
        }
        static BoulderWindFieldProfile BakeWind()
        {
            var p = Asset<BoulderWindFieldProfile>(WindPath, ScriptableObject.CreateInstance<BoulderWindFieldProfile>);
            p.legacyThermal = AssetDatabase.LoadAssetAtPath<ThermalFieldProfile>(GoldenEagleThermalAuthoring.ProfilePath);
            p.groundHeights = new float[p.width * p.height]; p.ridgeResponse = new float[p.groundHeights.Length];
            for (int z = 0; z < p.height; z++) for (int x = 0; x < p.width; x++)
            {
                float e = p.minimumEnu.x + x * p.spacingMeters, n = p.minimumEnu.y + z * p.spacingMeters;
                int i = z * p.width + x; p.groundHeights[i] = Ground(e, n);
                // An easterly air mass climbs west-facing travel slopes: -dh/de is positive on windward foothills.
                float slope = (Ground(e - 150, n) - Ground(e + 150, n)) / 300;
                float upstream = Mathf.Max(Ground(e + 300, n), Ground(e + 600, n));
                float shelter = Mathf.Lerp(1, .25f, Mathf.Clamp01((upstream - p.groundHeights[i]) / 150));
                p.ridgeResponse[i] = Mathf.Clamp(slope * shelter, -.55f, .55f);
            }
            p.foothillThermals = new[] { new BoulderWindFieldProfile.Thermal {
                centerEnu = new Vector2(-5200, 1800), groundHeight = Ground(-5200,1800), radius = 240, topAgl = 1400, updraft = 3 } };
            if (!p.IsValid(out string error)) throw new InvalidOperationException(error);
            EditorUtility.SetDirty(p); return p;
        }
        static float Ground(float x, float z)
        {
            if (!terrain.TrySample(x,z,out ProductionTerrainSample sample)) throw new InvalidOperationException($"Terrain missing at {x},{z}");
            return sample.heightMeters;
        }
        static void BuildModel(AmbientAircraftActor actor, string kind, Material material)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/" + kind + "_Ambient.glb");
            if (model == null) throw new InvalidOperationException("GLB model missing: " + kind);
            var visual=new GameObject("Visual");visual.transform.SetParent(actor.transform,false);
            actor.visual = visual.transform; actor.lodRoots = new GameObject[3];
            var props = new List<Transform>(); var flaps = new List<Transform>(); var renderers = new List<Renderer>();
            var source = UnityEngine.Object.Instantiate(model);
            try
            {
                for (int lod = 0; lod < 3; lod++)
                {
                    var go = new GameObject("LOD" + lod); go.transform.SetParent(actor.visual, false); actor.lodRoots[lod] = go;
                    foreach (var mf in source.GetComponentsInChildren<MeshFilter>(true).Where(m => m.name.Contains("_LOD" + lod + "_")))
                    {
                        var part = new GameObject(mf.name); part.transform.SetParent(go.transform, false);
                        Mesh mesh = UnityEngine.Object.Instantiate(mf.sharedMesh);
                        Matrix4x4 matrix = source.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                        var vertices = mesh.vertices; var normals = mesh.normals;
                        for (int k = 0; k < vertices.Length; k++) { vertices[k] = matrix.MultiplyPoint3x4(vertices[k]); normals[k] = matrix.MultiplyVector(normals[k]).normalized; }
                        bool prop = mf.name.Contains("Propeller"), flap = mf.name.Contains("Flaps");
                        Vector3 pivot = prop ? new Vector3((vertices.Min(v=>v.x)+vertices.Max(v=>v.x))*.5f,(vertices.Min(v=>v.y)+vertices.Max(v=>v.y))*.5f,(vertices.Min(v=>v.z)+vertices.Max(v=>v.z))*.5f) : Vector3.zero;
                        if (flap) pivot = new Vector3(0, vertices.Average(v=>v.y), vertices.Max(v=>v.z));
                        for (int k = 0; k < vertices.Length; k++) vertices[k] -= pivot;
                        mesh.vertices = vertices; mesh.normals = normals; mesh.RecalculateBounds();
                        string path = Root + "/Models/" + kind + "_LOD" + lod + "_" + (prop ? "Propeller" : flap ? "Flaps" : "Body") + ".asset";
                        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        if (existing != null) { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
                        else AssetDatabase.CreateAsset(mesh,path);
                        part.transform.localPosition = pivot;
                        part.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var r = part.AddComponent<MeshRenderer>(); r.sharedMaterial = material;
                        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        renderers.Add(r); if (prop) props.Add(part.transform); if (flap) flaps.Add(part.transform);
                    }
                    go.SetActive(lod == 0);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
            actor.renderers = renderers.ToArray(); actor.propellers = props.ToArray(); actor.flaps = flaps.ToArray();
            if (renderers.Count == 0) throw new InvalidOperationException("No exterior mesh nodes for " + kind);
            if(!actor.sailplane)
            {
                var points=actor.lodRoots[0].GetComponentsInChildren<MeshFilter>(true).SelectMany(f=>f.sharedMesh.vertices.Select(v=>f.transform.localPosition+v)).ToArray();
                float level=points.Min(v=>v.y),rest=points.Min(v=>(Quaternion.Euler(-10,0,0)*v).y);
                actor.wheelHeight=-level;actor.taildraggerRestOffset=level-rest;
            }
        }

        static AmbientAircraftRoute BakeRoute(int slot, int direction, float wheel)
        {
            var route = Asset<AmbientAircraftRoute>(Root + $"/Route_{slot}_{direction}.asset", ScriptableObject.CreateInstance<AmbientAircraftRoute>);
            route.runwayDirection = direction; var segments = new List<AmbientAircraftRoute.Segment>();
            float sign = direction == 8 ? 1 : -1, standX = -540 + slot * 35;
            Vector3 G(float x,float z) => new Vector3(x,Ground(x,z)+wheel,z);
            Vector3 R(float x, float y,float z) => new Vector3(x*sign,y,z);
            Vector3 last = G(standX,-159);
            void Add(AmbientFlightPhase phase,bool ground,bool reserved,float v0,float v1,float dwell, params Vector3[] controls)
            {
                var pts = new List<Vector3> { last };
                foreach (var p in controls) pts.Add(p);
                if (pts.Count == 1) pts.Add(last);
                var samples = new List<Vector3>();
                var cuts=new float[pts.Count];
                for(int j=1;j<pts.Count-1;j++)cuts[j]=Mathf.Min(Vector3.Distance(pts[j-1],pts[j]),Vector3.Distance(pts[j],pts[j+1]))*(ground ? .2f : .45f);
                // Piecewise quadratic corner rounding preserves exact endpoints and avoids rectangular airborne turns.
                for (int c=0;c<pts.Count-1;c++)
                {
                    Vector3 a=pts[c],b=pts[c+1];
                    Vector3 start=c==0?a:a-(a-pts[c-1]).normalized*cuts[c];
                    Vector3 curveEnd=a+(b-a).normalized*cuts[c];
                    if(c>0)
                    {
                        int curveSteps=Mathf.Max(12,Mathf.CeilToInt((Vector3.Distance(start,a)+Vector3.Distance(a,curveEnd))/(ground ? .35f : .8f)));
                        for(int k=0;k<curveSteps;k++) { float t=k/(float)curveSteps; Vector3 p=(1-t)*(1-t)*start+2*(1-t)*t*a+t*t*curveEnd; if(ground)p.y=Ground(p.x,p.z)+wheel; samples.Add(p); }
                    }
                    Vector3 lineStart=c==0?a:curveEnd;
                    Vector3 lineEnd=c==pts.Count-2?b:b-(b-a).normalized*cuts[c+1];
                    int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(lineStart,lineEnd)/(ground?3:25)));
                    for(int k=0;k<count;k++) {var p=Vector3.Lerp(lineStart,lineEnd,k/(float)count);if(ground)p.y=Ground(p.x,p.z)+wheel;samples.Add(p);}
                }
                samples.Add(pts.Last()); var distance=new float[samples.Count];
                for(int k=1;k<distance.Length;k++)distance[k]=distance[k-1]+Vector3.Distance(samples[k-1],samples[k]);
                segments.Add(new AmbientAircraftRoute.Segment {phase=phase,onGround=ground,protectsRunway=reserved,startSpeed=v0,endSpeed=v1,dwellSeconds=dwell,points=samples.ToArray(),distance=distance});
                last=pts.Last();
            }
            Add(AmbientFlightPhase.Parked,true,false,0,0,25);
            Add(AmbientFlightPhase.Starting,true,false,0,0,8);
            float entryX=direction==8?-620:550;
            Add(AmbientFlightPhase.TaxiOut,true,false,3,.25f,0,G(standX,-121),G(-420,-121),G(-420,-91),G(entryX,-91),G(entryX,-67));
            Add(AmbientFlightPhase.HoldShort,true,false,0,0,4);
            Add(AmbientFlightPhase.LineUp,true,true,3,3,0,G(entryX,-30),G(entryX+50*sign,-30));
            Add(AmbientFlightPhase.Takeoff,true,true,3,32,0,G(-120*sign,-30));
            float rotationY=G(-120*sign,-30).y;
            Add(AmbientFlightPhase.Climb,false,true,32,40,0,R(-110,rotationY+.01f,-30),R(-90,rotationY+.4f,-30),R(-40,rotationY+3,-30),R(50,12,-30),R(150,25,-30),R(720,110,-30));
            Add(AmbientFlightPhase.Circuit,false,false,40,43,0,R(780,118.5f,-30),R(1050,156,-25),R(1850,270,25),R(1700,308,900),R(-2400,260,900),R(-2900,180,700),R(-2900,140,-30),R(-2250,95,-30));
            Add(AmbientFlightPhase.Approach,false,true,35,30,0,R(-900,24,-30));
            float touchdownY=G(-470*sign,-30).y;
            Add(AmbientFlightPhase.Landing,false,true,30,25,0,R(-670,10.5f,-30),R(-540,3,-30),R(-500,touchdownY+.5f,-30),R(-480,touchdownY+.05f,-30),G(-470*sign,-30));
            float exitX = direction == 8 ? 265 : -48;
            Add(AmbientFlightPhase.Rollout,true,true,25,3,0,G(exitX,-30),G(exitX,-91),G(exitX-30,-91));
            Add(AmbientFlightPhase.TaxiIn,true,false,3,.25f,0,G(-420,-91),G(-420,-121),G(standX,-121),G(standX,-159));
            Add(AmbientFlightPhase.Shutdown,true,false,0,0,8);
            route.segments=segments.ToArray(); if(!route.IsValid(out string error))throw new InvalidOperationException(error);
            EditorUtility.SetDirty(route); return route;
        }
        static T Asset<T>(string path, Func<T> create) where T:UnityEngine.Object
        {
            var value=AssetDatabase.LoadAssetAtPath<T>(path); if(value!=null)return value;
            value=create();AssetDatabase.CreateAsset(value,path);return value;
        }
    }
}
