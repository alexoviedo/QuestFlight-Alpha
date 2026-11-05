using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using QuestFlight.Product;
using QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2;
using QuestFlightLab.Input;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    // Local-only meshes derive from the inspected donor. This runs in Editor,
    // never in the player; original download and existing prefab GUIDs survive.
    public static class SoaringAircraftAuthoring
    {
        public const string Folder="Assets/Production/SoaringV1/Presentation";
        [Serializable] private class Part { public string name;public Vector3[] vertices,normals;public Color[] colors;public int[] triangles;public Vector3 anchor,axis; }
        [Serializable] private class Anchors { public Vector3 Eye,Panel,FrontStick,FrontPedals,TowRelease,BrakePivot,TrimPivot,BrakeGrip,TrimGrip,StickGrip; }
        [Serializable] private class Donor { public bool completeRepair,completeInstruments;public string source_sha256;public Part[] parts;public Anchors anchors; }
        private sealed class Builder
        {
            public readonly List<Vector3> normals=new List<Vector3>();public readonly List<Vector3> vertices=new List<Vector3>();public readonly List<Color> colors=new List<Color>();public readonly List<int> indices=new List<int>();
            public void Add(Part p) { int n=vertices.Count;vertices.AddRange(p.vertices);if(p.normals!=null&&p.normals.Length==p.vertices.Length)normals.AddRange(p.normals);else {var calculated=new Vector3[p.vertices.Length];for(int t=0;t<p.triangles.Length;t+=3){int a=p.triangles[t],b=p.triangles[t+1],c=p.triangles[t+2];Vector3 normal=Vector3.Cross(p.vertices[b]-p.vertices[a],p.vertices[c]-p.vertices[a]).normalized;calculated[a]+=normal;calculated[b]+=normal;calculated[c]+=normal;}foreach(var normal in calculated)normals.Add(normal.normalized);}colors.AddRange(p.colors);foreach(int i in p.triangles)indices.Add(n+i); }
            public void Triangle(Vector3 a,Vector3 b,Vector3 c,Color color)
            { int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);Vector3 cross=Vector3.Cross(b-a,c-a);float length=cross.magnitude;Vector3 normal=length>1e-12f?cross/length:Vector3.up;normals.Add(normal);normals.Add(normal);normals.Add(normal);colors.Add(color);colors.Add(color);colors.Add(color);indices.Add(n);indices.Add(n+1);indices.Add(n+2); }
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color) { Triangle(a,b,c,color);Triangle(a,c,d,color); }
            public void Box(Vector3 center,Vector3 size,Color color)
            {
                var p=new Vector3[8];for(int i=0;i<8;i++)p[i]=center+Vector3.Scale(size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                foreach(var f in new[]{new[]{0,2,3,1},new[]{4,5,7,6},new[]{0,4,6,2},new[]{1,3,7,5},new[]{0,1,5,4},new[]{2,6,7,3}})Quad(p[f[0]],p[f[1]],p[f[2]],p[f[3]],color);
            }
            public void Rod(Vector3 a,Vector3 b,float radius,Color color,int sides=6)
            {
                Vector3 axis=(b-a).normalized,u=Vector3.Cross(axis,Mathf.Abs(axis.y)>.9f?Vector3.right:Vector3.up).normalized*radius,v=Vector3.Cross(axis,u);
                for(int i=0;i<sides;i++) { float t=i*Mathf.PI*2/sides,t1=(i+1)*Mathf.PI*2/sides;Vector3 q=u*Mathf.Cos(t)+v*Mathf.Sin(t),r=u*Mathf.Cos(t1)+v*Mathf.Sin(t1);Quad(a+q,b+q,b+r,a+r,color);Triangle(a,a+r,a+q,color);Triangle(b,b+q,b+r,color); }
            }
            public void Face(Vector3 c,float radius,Color color,int sides=40)
            { for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;Triangle(c,c+new Vector3(Mathf.Sin(a),Mathf.Cos(a),0)*radius,c+new Vector3(Mathf.Sin(b),Mathf.Cos(b),0)*radius,color);} }
            public Mesh Mesh(string name)
            { var m=new Mesh{name=name};m.SetVertices(vertices);m.SetColors(colors);m.SetTriangles(indices,0);m.SetNormals(normals);m.RecalculateBounds();m.bounds=new Bounds(m.bounds.center,m.bounds.size+Vector3.one*.8f);return m; }
        }
        private static readonly Color Cream=new Color(.8f,.8f,.72f),Black=new Color(.018f,.022f,.026f),White=new Color(.95f,.94f,.85f);
        private static readonly Dictionary<char,string> Glyphs=new Dictionary<char,string>
        {
            ['0']="111101101101111",['1']="010110010010111",['2']="111001111100111",['3']="111001111001111",['4']="101101111001001",['5']="111100111001111",['6']="111100111101111",['7']="111001010010010",['8']="111101111101111",['9']="111101111001111",
            ['A']="010101111101101",['B']="110101110101110",['C']="111100100100111",['D']="110101101101110",['E']="111100110100111",['F']="111100110100100",['G']="111100101101111",['H']="101101111101101",['I']="111010010010111",['K']="101101110101101",['L']="100100100100111",['M']="101111111101101",['N']="101111111111101",['O']="111101101101111",['P']="110101110100100",['R']="110101110101101",['S']="111100111001111",['T']="111010010010010",['U']="101101101101111",['V']="101101101101010",['W']="101101111111101",['X']="101101010101101",['Y']="101101010010010",['-']="000000111000000",['.']="000000000000010",['/']="001001010100100",['+']="000010111010000"
        };
        private static void Label(Builder b,string text,Vector3 at,float height)
        {
            float px=height/5;at.x-=text.Length*4*px*.5f;
            for(int n=0;n<text.Length;n++) if(Glyphs.TryGetValue(text[n],out string map))for(int i=0;i<15;i++)if(map[i]=='1')
            { Vector3 p=at+new Vector3((n*4+i%3)*px,(2-i/3)*px,0);float h=px*.44f;b.Quad(p+new Vector3(-h,-h,0),p+new Vector3(-h,h,0),p+new Vector3(h,h,0),p+new Vector3(h,-h,0),White); }
        }
        [MenuItem("QuestFlight/Soaring/Author local SGS presentation")]
        public static void Run()
        {
            string file=System.Environment.GetEnvironmentVariable("QF_SGS_GEOMETRY");
            if(string.IsNullOrEmpty(file)||!File.Exists(file))throw new InvalidOperationException("QF_SGS_GEOMETRY must name the inspected external donor derivative JSON.");
            Donor donor=JsonUtility.FromJson<Donor>(File.ReadAllText(file));
            if(donor.source_sha256!="96becbb7d6e2e6b47850faa1b2a9429268258e77aa88ec9ba2063551b37ffe07")throw new InvalidOperationException("Unexpected donor source hash.");
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            // The donor panel is too low after the demonstrated longitudinal
            // correction. Relocate the repaired panel/release to a usable front
            // seat sight line; this is a cockpit reference estimate.
            Vector3 panelRepair=donor.completeRepair?Vector3.zero:new Vector3(0,.13f,-.05f);
            donor.anchors.Panel+=panelRepair;
            donor.anchors.TowRelease+=panelRepair;
            foreach(var p in donor.parts)if(p.name=="TowRelease")
            {p.anchor+=panelRepair;for(int i=0;i<p.vertices.Length;i++)p.vertices[i]+=panelRepair;}
            var fixedMesh=new Builder();var movingMesh=new Builder();var glassMesh=new Builder();var bindings=new List<SoaringMovingPart>();
            foreach(var p in donor.parts)
            {
                if(p.name=="StaticHull")fixedMesh.Add(p);
                else if(p.name=="Canopy")glassMesh.Add(p);
                else { int n=movingMesh.vertices.Count;movingMesh.Add(p);bindings.Add(new SoaringMovingPart{name=p.name,anchor=p.anchor,rotationAxis=p.axis,firstVertex=n,vertexCount=p.vertices.Length}); }
            }
            Vector3 lever=donor.anchors.BrakePivot,trim=donor.anchors.TrimPivot;
            if(!donor.completeRepair)
            {
            // Legacy reference repairs retained for source replay.
            foreach(int side in new[]{-1,1})
            {
                var rail=new[]{new Vector3(side*.285f,.72f,1.32f),new Vector3(side*.27f,1.03f,1.24f),new Vector3(side*.22f,1.34f,.97f),new Vector3(side*.13f,1.43f,.65f)};
                for(int i=0;i<rail.Length-1;i++)fixedMesh.Rod(rail[i],rail[i+1],.010f,Cream);
                fixedMesh.Rod(rail[3],new Vector3(side*.13f,1.43f,-.4f),.010f,Cream);
                fixedMesh.Rod(new Vector3(side*.29f,-.05f,.20f),new Vector3(side*3.15f,1.38f,-.12f),.022f,Cream);
                fixedMesh.Rod(new Vector3(side*.29f,-.05f,.20f),new Vector3(side*3.15f,1.34f,-.65f),.019f,Cream);
                Vector3 pivot=new Vector3(side*2.2f,1.36f,-.24f);
                AddBoxPart(movingMesh,bindings,side<0?"LeftUpperBrake":"RightUpperBrake",pivot,new Vector3(0,0,-.14f),new Vector3(1.1f,.014f,.28f),new Color(.75f,.06f,.08f));
                pivot.y-=.075f;
                AddBoxPart(movingMesh,bindings,side<0?"LeftLowerBrake":"RightLowerBrake",pivot,new Vector3(0,0,-.12f),new Vector3(1.1f,.014f,.24f),new Color(.75f,.06f,.08f));
            }
            lever=new Vector3(-.27f,.10f,.91f);trim=lever+new Vector3(.015f,-.13f,-.12f);
            AddRodPart(movingMesh,bindings,"BrakeLever",lever,lever+Vector3.up*.29f,.017f,new Color(.2f,.32f,.8f));
            AddRodPart(movingMesh,bindings,"TrimLever",trim,trim+Vector3.up*.16f,.012f,new Color(.25f,.65f,.3f));
            fixedMesh.Rod(new Vector3(.28f,.89f,.89f),new Vector3(.22f,.89f,.89f),.012f,Black);
            }
            Vector3 panel=donor.anchors.Panel;panel.z-=.018f;
            if(!donor.completeRepair)fixedMesh.Box(panel+new Vector3(0,-.09f,.013f),new Vector3(.47f,.32f,.013f),Black);
            if(!donor.completeInstruments)
            {
            string[] names={"AsiNeedle","AltitudeNeedle","VarioNeedle","HeadingNeedle"};
            for(int g=0;g<4;g++)
            {
                Vector3 c=panel+(donor.completeRepair?new Vector3(new[]{.185f,-.185f,.062f,-.062f}[g],g<2?-.025f:.035f,-.004f):new Vector3((g%2==0?-.105f:.105f),g<2?.008f:-.142f,-.004f));
                int staticStart=fixedMesh.vertices.Count,movingStart=movingMesh.vertices.Count,partStart=bindings.Count;
                fixedMesh.Face(c,.075f,new Color(.14f,.15f,.15f),24);c.z-=.002f;fixedMesh.Face(c,.068f,Black,24);
                for(int tick=0;tick<(g==0?11:g==1?10:12);tick++)
                {
                    float deg=g==0?135-tick*27:g==2?135-tick*270f/11:g==1?-tick*36:-tick*30;
                    float a=deg*Mathf.Deg2Rad;Vector3 dir=new Vector3(-Mathf.Sin(a),Mathf.Cos(a),0);
                    Vector3 tickA=c+dir*.057f-Vector3.forward*.001f,tickB=c+dir*.064f-Vector3.forward*.001f,tickSide=new Vector3(-dir.y,dir.x,0)*.0009f;
                    fixedMesh.Quad(tickA-tickSide,tickA+tickSide,tickB+tickSide,tickB-tickSide,White);
                    if(g==0 && tick%2==0)Label(fixedMesh,(tick*10).ToString(),c+dir*.046f-Vector3.forward*.002f,.009f);
                    if(g==1 && tick<10) { float t=-tick*36*Mathf.Deg2Rad;Label(fixedMesh,tick.ToString(),c+new Vector3(-Mathf.Sin(t),Mathf.Cos(t),0)*.048f-Vector3.forward*.002f,.011f); }
                }
                Label(fixedMesh,new[]{"MPH","FT STD","VSI X100","TRUE HDG"}[g],c+new Vector3(0,-.026f,-.003f),.008f);
                if(g==2){Label(fixedMesh,"+10",c+new Vector3(.033f,-.037f,-.003f),.008f);Label(fixedMesh,"-10",c+new Vector3(-.033f,-.037f,-.003f),.008f);Label(fixedMesh,"0",c+new Vector3(0,.048f,-.003f),.011f);}
                if(g==3){Label(fixedMesh,"N",c+new Vector3(0,.048f,-.003f),.012f);Label(fixedMesh,"E",c+new Vector3(.048f,0,-.003f),.012f);Label(fixedMesh,"S",c+new Vector3(0,-.048f,-.003f),.012f);Label(fixedMesh,"W",c+new Vector3(-.048f,0,-.003f),.012f);}
                Vector3 needle=c-Vector3.forward*.005f;AddNeedle(movingMesh,bindings,names[g],needle,.054f,.003f,White);
                if(g==1)AddNeedle(movingMesh,bindings,"AltitudeThousands",needle-Vector3.forward*.001f,.034f,.005f,White);
                if(donor.completeRepair)
                {
                    const float scale=.74f;
                    for(int k=staticStart;k<fixedMesh.vertices.Count;k++)fixedMesh.vertices[k]=c+(fixedMesh.vertices[k]-c)*scale;
                    for(int k=movingStart;k<movingMesh.vertices.Count;k++)movingMesh.vertices[k]=c+(movingMesh.vertices[k]-c)*scale;
                    for(int k=partStart;k<bindings.Count;k++){var part=bindings[k];part.anchor=c+(part.anchor-c)*scale;bindings[k]=part;}
                }
            }
            if(!donor.completeRepair)Label(fixedMesh,"SGS 2-33A REFERENCE",panel+new Vector3(0,.105f,-.004f),.012f);
            Label(fixedMesh,"1013.25 HPA",panel+new Vector3(.105f,-.06f,-.004f),.007f);
            if(!donor.completeRepair)Label(fixedMesh,"AIR / WHEEL BRAKE",new Vector3(-.19f,.17f,1.115f),.008f);
            if(!donor.completeRepair)Label(fixedMesh,"TRIM",new Vector3(-.20f,.045f,1.115f),.009f);
            if(!donor.completeRepair)Label(fixedMesh,"RELEASE",panel+new Vector3(0,-.278f,-.003f),.01f);
            }
            Vector3 yarn=donor.anchors.Eye+new Vector3(0,donor.completeRepair?.09f:.035f,.44f);
            // A small tape patch shows attachment to the transparent canopy.
            fixedMesh.Box(yarn+Vector3.forward*.002f,new Vector3(.014f,.014f,.001f),new Color(.85f,.82f,.7f));
            AddRodPart(movingMesh,bindings,"YawString",yarn,yarn+(donor.completeRepair?new Vector3(0,.025f,-.075f):Vector3.up*.075f),.0012f,new Color(.85f,.12f,.10f));
            int triangleCount=(fixedMesh.indices.Count+movingMesh.indices.Count+glassMesh.indices.Count)/3;
            if(donor.completeRepair&&triangleCount>8000)throw new InvalidOperationException("SGS exceeds frozen8000 pilot triangle ceiling: "+triangleCount);
            Mesh hull=SaveMesh(fixedMesh.Mesh("SGS repaired static"),"SGS_Local_Static.asset"),moving=SaveMesh(movingMesh.Mesh("SGS controls and needles"),"SGS_Local_Moving.asset"),glass=SaveMesh(glassMesh.Mesh("SGS canopy"),"SGS_Local_Canopy.asset");
            var opaque=Material("SGS_Paint",Shader.Find("QuestFlight/Ambient Aircraft Exterior"));if(donor.completeRepair)opaque.shader=Shader.Find("QuestFlight/SGS Reference Surface");opaque.SetColor("_Tint",Color.white);opaque.SetColor("_PaintAccent",new Color(.58f,.025f,.025f));
            var glassMaterial=Material("SGS_Canopy",Shader.Find("Universal Render Pipeline/Unlit"));if(donor.completeRepair)glassMaterial.shader=Shader.Find("QuestFlight/SGS Canopy Glass Reflection");
            glassMaterial.SetColor("_BaseColor",new Color(.78f,.89f,.92f,.10f));glassMaterial.SetFloat("_Surface",1);glassMaterial.SetFloat("_Blend",0);glassMaterial.SetFloat("_SrcBlend",5);glassMaterial.SetFloat("_DstBlend",10);glassMaterial.SetFloat("_ZWrite",0);glassMaterial.renderQueue=3000;glassMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            var glider=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/SGS233A_Reference.asset");glider.eyeFromCgMeters=donor.anchors.Eye;EditorUtility.SetDirty(glider);
            var c172=FlightAircraftDefinition.EditorC172;
            var tugDefinition=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/PA18_Tug_Reference.asset");
            string towPath=FlightAircraftDefinition.DefinitionFolder+"/ReferenceTowline.asset";
            var towline=AssetDatabase.LoadAssetAtPath<QuestFlightLab.Flight.Soaring.TowlineDefinition>(towPath);
            if(towline==null){towline=ScriptableObject.CreateInstance<QuestFlightLab.Flight.Soaring.TowlineDefinition>();AssetDatabase.CreateAsset(towline,towPath);}
            foreach(string path in new[]{"Assets/Production/Prefabs/ProductionC172AircraftRig.prefab","Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab"})
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var flight=root.GetComponentInChildren<FlightDynamicsCoordinator>(true);var rig=root.GetComponentInChildren<AircraftReferenceFrameRig>(true);
                    flight.tugDefinition=tugDefinition;flight.towlineDefinition=towline;
                    var modes=root.GetComponentInChildren<ProductFlightModeCoordinator>(true);
                    var selection=flight.GetComponent<SoaringAircraftPresentation>();if(selection==null)selection=flight.gameObject.AddComponent<SoaringAircraftPresentation>();
                    bool first=selection.gliderRoot==null;
                    if(first)
                    {
                        var existing=new List<GameObject>();foreach(Transform t in rig.AircraftVisualRoot)if(t!=rig.PilotSeatAnchor)existing.Add(t.gameObject);
                        selection.c172Roots=existing.ToArray();selection.c172RootActive=existing.Select(o=>o.activeSelf).ToArray();
                        selection.c172Consumers=root.GetComponentsInChildren<Behaviour>(true).Where(b=>b is QuestFlightLab.Audio.ProductionC172AudioController || b is QuestFlightLab.Aircraft.CockpitDynamics.ProductionCockpitSystemsController || b is ProductDemoPilot).ToArray();
                        selection.c172ConsumerEnabled=selection.c172Consumers.Select(b=>b.enabled).ToArray();selection.c172SeatPosition=rig.PilotSeatAnchor.localPosition;selection.c172SeatRotation=rig.PilotSeatAnchor.localRotation;
                        selection.gliderRoot=Child(rig.AircraftVisualRoot,"SGS233ReferencePresentation");
                    }
                    selection.c172=c172;selection.glider=glider;selection.frame=rig;
                    Transform parent=selection.gliderRoot.transform;
                    Render(parent,"AirframeAndInstruments",hull,opaque);Render(parent,"Canopy",glass,glassMaterial);
                    var mf=Render(parent,"SurfacesControlsAndNeedles",moving,opaque);
                    var presenter=parent.GetComponent<SoaringInstrumentPresenter>();if(presenter==null)presenter=parent.gameObject.AddComponent<SoaringInstrumentPresenter>();
                    presenter.flight=flight;presenter.moving=mf;presenter.parts=bindings.ToArray();selection.instruments=presenter;
                    var audioProfile=root.GetComponentInChildren<QuestFlightLab.Audio.ProductionC172AudioController>(true).Profile;
                    var airframeAudio=parent.GetComponent<QuestFlightLab.Flight.Soaring.SoaringAudio>();if(airframeAudio==null)airframeAudio=parent.gameObject.AddComponent<QuestFlightLab.Flight.Soaring.SoaringAudio>();airframeAudio.flight=flight;
                    var audioSources=new AudioSource[3];
                    for(int i=0;i<3;i++)
                    {
                        var go=Child(parent,new[]{"GliderAirflowAudio","GliderGroundAudio","IdealTETrainingAudio"}[i]);
                        var audio=go.GetComponent<AudioSource>();if(audio==null)audio=go.AddComponent<AudioSource>();
                        audio.loop=true;audio.playOnAwake=false;audio.spatialBlend=0;audio.volume=0;
                        audio.clip=i==0?audioProfile.windLowLoop:i==1?audioProfile.groundRollLoop:null;audioSources[i]=audio;
                    }
                    airframeAudio.air=audioSources[0];airframeAudio.ground=audioSources[1];airframeAudio.totalEnergyAid=audioSources[2];airframeAudio.totalEnergyTrainingAid=false;
                    AuthorGrabs(parent,flight,donor,lever,trim,presenter);
                    selection.gliderRoot.SetActive(false);
                    if(modes!=null)
                    {
                        modes.ConfigureAircraftPresentation(selection);
                        var menu=modes.AuthoredModeMenuView;
                        menu.ConfigureSoaringPresentation(selection,menu.AuthoredAircraftRenderers.Concat(parent.GetComponentsInChildren<Renderer>(true)).Distinct().ToArray());
                        var traffic=root.GetComponentInChildren<QuestFlightLab.Environment.AmbientTrafficDirector>(true);
                        if(traffic!=null)
                        {
                            traffic.reservedNativeTug=traffic.actors.Single(a=>a.aircraftId=="SuperCub_1");
                            var audioGo=Child(traffic.reservedNativeTug.transform,"NativeTugEngineAudio");
                            var engine=audioGo.GetComponent<AudioSource>();if(engine==null)engine=audioGo.AddComponent<AudioSource>();
                            engine.clip=root.GetComponentInChildren<QuestFlightLab.Audio.ProductionC172AudioController>(true).Profile.engineCruiseLoop;
                            engine.loop=true;engine.playOnAwake=false;engine.spatialBlend=1;engine.minDistance=8;engine.maxDistance=1200;engine.volume=0;engine.dopplerLevel=0;
                            var nativeAudio=audioGo.GetComponent<QuestFlightLab.Flight.Soaring.NativeTugAudio>();if(nativeAudio==null)nativeAudio=audioGo.AddComponent<QuestFlightLab.Flight.Soaring.NativeTugAudio>();nativeAudio.flight=flight;nativeAudio.engine=engine;
                            SoaringEngineAudioRepair.Configure(nativeAudio,root.GetComponentsInChildren<AudioListener>(true).Single());
                            var ropeGo=Child(traffic.transform,"NativeTowlinePresentation");
                            var ropeAsset=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Towline96Triangles.asset");
                            if(ropeAsset==null)
                            {
                                var points=new Vector3[52];var colors=Enumerable.Repeat(new Color(.62f,.52f,.34f),52).ToArray();var triangles=new List<int>();
                                for(int segment=0;segment<12;segment++)for(int plane=0;plane<2;plane++)
                                {int a=segment*4+plane*2,b=a+4;triangles.AddRange(new[]{a,b,b+1,a,b+1,a+1,a,b+1,b,a,a+1,b+1});}
                                ropeAsset=new Mesh{name="Authored 12 segment opaque towline",vertices=points,colors=colors,triangles=triangles.ToArray(),normals=Enumerable.Repeat(Vector3.up,52).ToArray()};
                                AssetDatabase.CreateAsset(ropeAsset,Folder+"/Towline96Triangles.asset");
                            }
                            var ropeFilter=Render(ropeGo.transform,"Rope",ropeAsset,opaque);
                            var line=ropeFilter.GetComponent<QuestFlightLab.Flight.Soaring.TowlinePresentation>();if(line==null)line=ropeFilter.gameObject.AddComponent<QuestFlightLab.Flight.Soaring.TowlinePresentation>();
                            line.flight=flight;line.playerFrame=rig;line.nativeTugVisual=traffic.reservedNativeTug;line.ropeMesh=ropeFilter;line.ropeRenderer=ropeFilter.GetComponent<MeshRenderer>();line.ropeRenderer.enabled=false;
                            menu.ConfigureWorldPresentationRenderers(menu.AuthoredWorldRenderers.Concat(new[]{line.ropeRenderer}).Distinct().ToArray());
                        }
                    }
                    QuestFlight.Editor.MenuRepairAuthoring.ConfigureScope(root);PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(Folder,"LOCAL_DERIVATIVE_PROVENANCE.json"),JsonUtility.ToJson(new Provenance{sourceSha256=donor.source_sha256,staticTriangles=hull.triangles.Length/3,movingTriangles=moving.triangles.Length/3,canopyTriangles=glass.triangles.Length/3},true));
            Debug.Log("[Soaring] Authored repaired donor with "+((hull.triangles.Length+moving.triangles.Length+glass.triangles.Length)/3)+" triangles, 3 renderers, 2 materials, no new textures. Local-only donor rights.");
            if(donor.completeRepair)SoaringYawStringAuthoring.Repair();
        }
        [Serializable] private class Provenance { public string sourceSha256;public string rights="LOCAL_ONLY_REFERENCE_CLEARANCE_PENDING";public string geometrySource="Simons1996 Schweizer-supplied three-view; UIUC V5 TRUE NACA43012A coordinates. Donor inspected but unsuitable surfaces replaced; original donor preserved.";public string repairs="Closed nose/fuselage/front canopy/fixed rear glazing; dimensioned wing planform, dihedral, washout and NACA43012A sections; symmetric estimated tail sections; hinge axes; streamlined lift struts; arched panel/interior; split normals; reachable grips follow handles. Authored seat eye lowered0.52m into enclosure; no tracked-camera or FDM edits. Local/uncommitted.";public string airbrakeReference="2026-10-16: thin balanced upper/lower panels with hinge-edge reliefs; approximate 75-120in span stations and half-chord hinges from Schweizer-supplied three-view, Barre1986 photo202844; G2EA85deg upper/75deg lower travel. See docs/soaring/SGS_VISUAL_REFERENCES.md. No factory CAD or operator-exact fit claimed.";public string brakeLeverReference="2026-10-16: left sidewall shaft with bent inward grip, below panel; manufacturer manual and actual SGS2-33/A cockpit photos. Straight aft travel with initial visual downward unlock, terminal wheel braking. 230mm travel,25deg roll and85/15 input split are reference estimates, not measured rigging. See docs/soaring/SGS_VISUAL_REFERENCES.md.";public string panelViewReference="2026-10-16: N2930H photographed instrument arrangement; altimeter,VSI,magnetic compass,MPH ASI; centre vent/blank cover. Rounded closed nose coaming and raked pitot/static mast from actual cockpit photographs and RCAF2017 AOI figures1-2/1-3/3-1. Dimensions/face angles estimated; static Boulder magnetic variation shared with C172; no exact operator or compass-error fidelity claim. See docs/soaring/SGS_VISUAL_REFERENCES.md.";public int staticTriangles,movingTriangles,canopyTriangles; }
        private static void AuthorGrabs(Transform parent,FlightDynamicsCoordinator flight,Donor donor,Vector3 lever,Vector3 trim,SoaringInstrumentPresenter presenter)
        {
            for(int k=0;k<4;k++)
            {
                string name=new[]{"StickGrab","AirbrakeGrab","TrimGrab","ReleaseGrab"}[k];
                var go=Child(parent,name);
                Vector3 center=donor.completeRepair?(k==0?donor.anchors.StickGrip:k==1?donor.anchors.BrakeGrip:k==2?donor.anchors.TrimGrip:donor.anchors.TowRelease):(k==0?donor.anchors.FrontStick+Vector3.up*.43f:k==1?lever+Vector3.up*.24f:k==2?trim+Vector3.up*.14f:donor.anchors.TowRelease);
                go.transform.localPosition=center;go.transform.localRotation=Quaternion.identity;

                var body=go.GetComponent<Rigidbody>()??go.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
                var collider=go.GetComponent<BoxCollider>()??go.AddComponent<BoxCollider>();collider.size=k==0?new Vector3(.08f,.14f,.09f):k==1?new Vector3(.14f,.065f,.07f):new Vector3(.07f,.08f,.09f);
                ITransformer transformer;
                if(k==0)
                {
                    var pivot=Child(parent,"StickRollCenter");pivot.transform.localPosition=donor.anchors.FrontStick;
                    var stick=go.GetComponent<OvrConstrainedYokeGrabTransformer>()??go.AddComponent<OvrConstrainedYokeGrabTransformer>();
                    stick.ConfigureAuthoredReferences(flight,parent,pivot.transform,Vector3.forward,Vector3.forward,.16f,18);
                    transformer=stick;
                }
                else
                {
                    var l=go.GetComponent<SoaringLeverGrabTransformer>()??go.AddComponent<SoaringLeverGrabTransformer>();l.input=flight.controls;l.stableFrame=parent;l.releaseHandle=k==3;
                    l.channel=k==2?ProductTouchControlChannel.GliderTrimLever:ProductTouchControlChannel.GliderAirbrakeLever;l.fullTravelMeters=k==2?.16f:.23f;l.gripRadiusMeters=k==2?.155f:.27f;l.maximumAngleDegrees=k==2?30:65;transformer=l;
                    l.linearTravel=k==1;if(k==1)l.fullTravelMeters=SoaringInstrumentPresenter.BrakeLeverTravelMeters;
                    if(k==3)presenter.releaseHandle=l;
                }
                var grab=go.GetComponent<Grabbable>()??go.AddComponent<Grabbable>();grab.MaxGrabPoints=1;grab.TransferOnSecondSelection=true;grab.InjectOptionalOneGrabTransformer(transformer);grab.InjectOptionalTargetTransform(go.transform);grab.InjectOptionalRigidbody(body);grab.InjectOptionalThrowWhenUnselected(false);grab.InjectOptionalKinematicWhileSelected(false);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/HandGrab/HandGrabInteractable.prefab");
                if(prefab==null)throw new InvalidOperationException("Missing existing Meta hand/controller grab prefab.");
                var existing=go.GetComponentInChildren<HandGrabInteractable>(true);var hand=existing!=null?existing.gameObject:(GameObject)PrefabUtility.InstantiatePrefab(prefab,go.transform);if(existing==null)PrefabUtility.UnpackPrefabInstance(hand,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                hand.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);hand.transform.localScale=Vector3.one;
                var interaction=hand.GetComponent<HandGrabInteractable>();interaction.InjectRigidbody(body);interaction.InjectSupportedGrabTypes(GrabTypeFlags.All);interaction.InjectOptionalPointableElement(grab);interaction.InjectOptionalHandGrabPoses(new List<HandGrabPose>());interaction.InjectOptionalScoreModifier(PoseMeasureParameters.DEFAULT);interaction.HandAlignment=HandAlignType.None;
                var movement=hand.GetComponent<MoveAtSourceProvider>()??hand.AddComponent<MoveAtSourceProvider>();interaction.InjectOptionalMovementProvider(movement);
                string partName=new[]{"FrontStick","BrakeLever","TrimLever","TowRelease"}[k];
                for(int n=0;n<presenter.parts.Length;n++)if(presenter.parts[n].name==partName)
                {var part=presenter.parts[n];part.grabTarget=go.transform;part.gripRestPosition=center;presenter.parts[n]=part;}
                foreach(Transform child in hand.GetComponentsInChildren<Transform>(true))if(child.name=="InteractableDebugVisual")child.gameObject.SetActive(false);
            }
            presenter.releaseHandle=parent.Find("ReleaseGrab").GetComponent<SoaringLeverGrabTransformer>();
        }
        private static void AddBoxPart(Builder b,List<SoaringMovingPart> p,string name,Vector3 anchor,Vector3 offset,Vector3 size,Color color)
        { int n=b.vertices.Count;b.Box(anchor+offset,size,color);p.Add(new SoaringMovingPart{name=name,anchor=anchor,firstVertex=n,vertexCount=b.vertices.Count-n}); }
        private static void AddRodPart(Builder b,List<SoaringMovingPart> p,string name,Vector3 a,Vector3 z,float r,Color color)
        { int n=b.vertices.Count;b.Rod(a,z,r,color);p.Add(new SoaringMovingPart{name=name,anchor=a,firstVertex=n,vertexCount=b.vertices.Count-n}); }
        private static void AddNeedle(Builder b,List<SoaringMovingPart> p,string name,Vector3 a,float length,float width,Color color)
        {int n=b.vertices.Count;b.Quad(a+new Vector3(-width,-.008f,0),a+new Vector3(-width,length,0),a+new Vector3(width,length,0),a+new Vector3(width,-.008f,0),color);p.Add(new SoaringMovingPart{name=name,anchor=a,firstVertex=n,vertexCount=b.vertices.Count-n});}
        private static GameObject Child(Transform parent,string name)
        { var found=parent.Find(name);if(found!=null)return found.gameObject;var go=new GameObject(name);go.transform.SetParent(parent,false);return go; }
        private static MeshFilter Render(Transform parent,string name,Mesh mesh,Material material)
        {var go=Child(parent,name);var f=go.GetComponent<MeshFilter>();if(f==null)f=go.AddComponent<MeshFilter>();f.sharedMesh=mesh;var r=go.GetComponent<MeshRenderer>();if(r==null)r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return f;}
        private static Mesh SaveMesh(Mesh mesh,string file)
        {
            string path=Folder+"/"+file;var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
            EditorUtility.CopySerialized(mesh,existing);
            // CopySerialized updates disk/CPU data but can leave the already
            // loaded static renderer's GPU buffers stale during authoring QA.
            existing.vertices=mesh.vertices;existing.normals=mesh.normals;
            existing.colors=mesh.colors;existing.triangles=mesh.triangles;
            existing.bounds=mesh.bounds;existing.UploadMeshData(false);
            EditorUtility.SetDirty(existing);UnityEngine.Object.DestroyImmediate(mesh);return existing;
        }
        private static Material Material(string name,Shader shader)
        {if(shader==null)throw new InvalidOperationException("Missing shader for "+name);string path=Folder+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(shader){name=name};AssetDatabase.CreateAsset(m,path);}EditorUtility.SetDirty(m);return m;}
    }
}
