"""Offline exterior-only traffic derivatives. Run with Blender --background --python ... -- sources output.
Sources: pinned KOG_THORNS CC-BY-4.0 C172, app4soft/EGPJET CC0 glider,
and helijah PA18 geometry offered under CC-BY-4.0 on the original author's Sketchfab listing.
The PA18 archive's GPL notice is retained externally; no Brett Harrison livery or source texture is used.
"""
from pathlib import Path
import sys, json, hashlib, math
import bpy, bmesh
from mathutils import Vector

REPO = Path(__file__).resolve().parents[2]
args = sys.argv[sys.argv.index('--') + 1:]
SOURCES, OUT = map(Path, args[:2])
OUT.mkdir(parents=True, exist_ok=True)
C172 = REPO/'QuestFlight/Assets/Resources/QuestFlightLab/ImportedAssets/Cessna172KogThorns/cessna172.glb'
records = []

def remove_noncontributing_body_triangles(mesh):
    """Drop only zero-area triangle faces at the audited meter-unit tolerance.

    Preserve all valid positions, attribute layers and material assignments.
    Called only for the existing SuperCub body derivatives after triangulation.
    """
    bm=bmesh.new();bm.from_mesh(mesh)
    try:
        invalid=[]
        for face in bm.faces:
            if len(face.verts)!=3:
                raise ValueError('Body hygiene requires triangulated geometry')
            a,b,c=[vertex.co for vertex in face.verts]
            if (b-a).cross(c-a).length_squared<=1e-24:
                invalid.append(face)
        removed=len(invalid)
        if invalid:bmesh.ops.delete(bm,geom=invalid,context='FACES_ONLY')
        bm.to_mesh(mesh);mesh.update()
        return removed
    finally:bm.free()

def color_mesh(o, color):
    attr = o.data.color_attributes.get('COLOR_0') or o.data.color_attributes.new(name='COLOR_0', type='BYTE_COLOR', domain='POINT')
    for c,v in zip(attr.data,o.data.vertices):
        # Original, modest fuselage stripe, never sampling third-party livery textures.
        stripe = abs(v.co.x)<0.65 and -0.28<v.co.z<0.02
        c.color = (0.11,0.23,0.32,1) if stripe and color[0]>.6 else color
    o.data.materials.clear()

def join(objects, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join()
    o=bpy.context.object;o.name=name
    return o

def discus_wings():
    # Newly authored exterior from the manufacturer's CS three-view, printed p.4:
    # 15m span, 10.58m2 projected wing area, three trapezoidal panels; optional winglets.
    # This is scenery geometry, not an aerodynamic mesh or an exact manufacturing model.
    stations=[(0,.865,-1.55),(4.4,.800,-1.48),(6.1,.600,-1.31),(7.5,.200,-.98)]
    area=2*sum((b[0]-a[0])*(a[1]+b[1])/2 for a,b in zip(stations,stations[1:]))
    factor=10.58/area
    verts=[];faces=[]
    profile=[0,.035,.10,.22,.4,.65,.85,1,.85,.65,.4,.22,.10,.035]
    for side in [-1,1]:
        offset=len(verts)
        spans=[0,.35,1,2,3,4.4,5.3,6.1,7,7.38,7.5]
        for span in spans:
            j=next((j for j in range(3) if span<=stations[j+1][0]),2)
            a,b=stations[j:j+2];u=(span-a[0])/(b[0]-a[0]);chord=(a[1]*(1-u)+b[1]*u)*factor;lead=a[2]*(1-u)+b[2]*u
            for k,t in enumerate(profile):
                thickness=5*.10*chord*(.2969*math.sqrt(t)-.126*t-.3516*t*t+.2843*t**3-.1036*t**4)
                verts.append((side*span,lead+t*chord+1.34,.07+span*.043+(thickness if k<8 else -thickness)))
        for ring in range(len(spans)-1):
            for k in range(len(profile)):
                a=offset+ring*len(profile)+k;b=offset+ring*len(profile)+(k+1)%len(profile);c=b+len(profile);d=a+len(profile)
                faces.append((a,b,c,d) if side>0 else (d,c,b,a))
        # A small tapered, closed winglet; no collapsed coincident canopy-like sheet.
        base=len(verts)
        for h,chord in [(0,.195),(.18,.13),(.38,.045)]:
            for k,t in enumerate(profile):
                thick=.06*chord*math.sin(t*math.pi)
                verts.append((side*(7.5+(thick if k<8 else -thick)),.36+t*chord+h*.25,.3925+h))
        for ring in range(2):
            for k in range(len(profile)):
                a=base+ring*len(profile)+k;b=base+ring*len(profile)+(k+1)%len(profile);faces.append((a,b,b+len(profile),a+len(profile)))
    mesh=bpy.data.meshes.new('DiscusCS_AuthoredWing');mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new('DiscusCS_AuthoredWing',mesh);bpy.context.collection.objects.link(obj)
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    color_mesh(obj,(.88,.89,.85,1));return obj

for kind in ['C172','SuperCub','Discus']:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if kind=='C172':bpy.ops.import_scene.gltf(filepath=str(C172))
    elif kind=='SuperCub':bpy.ops.wm.obj_import(filepath=str(SOURCES/'pa18/pa18.obj'))
    else:bpy.ops.wm.open_mainfile(filepath=str(SOURCES/'FFGL.blend'))
    source_meshes=[o for o in bpy.data.objects if o.type=='MESH']
    matrices={o.name:o.matrix_world.copy() for o in source_meshes}
    groups={'Body':[], 'Propeller':[], 'Flaps':[]}
    protected=[]
    for o in source_meshes:
        n=o.name
        color=(.88,.89,.85,1)
        part='Body'
        if kind=='C172':
            if 'Exterior' not in n:continue
            if '.001_' in n:part='Propeller';color=(.055,.055,.055,1)
            if '.016_' in n or '.017_' in n:part='Flaps'
            if any(f'.{i:03d}_' in n for i in [21,22,23]):color=(.045,.045,.05,1)
            if 'Glass' in n:color=(.11,.18,.23,1)
        elif kind=='SuperCub':
            allowed=['aileron','ailes','axe','axes','bol_','bouchons','cable','capot','direction','echappe','fuselage','helice','montants','profondeur','reservoirs','roue','structure','trous','vitres_','volet']
            if not any(n.startswith(s) for s in allowed):continue
            color=(.92,.68,.12,1)
            if n.startswith('helice'):part='Propeller';color=(.055,.055,.055,1)
            if n.startswith('volet'):part='Flaps'
            if n.startswith('roue'):color=(.045,.045,.05,1)
            if n.startswith('vitres_'):color=(.11,.18,.23,1)
        else:
            if n not in ['GLIDER','GLASS','FRAME']:continue
            if n=='GLASS':color=(.11,.18,.23,1)
            if n=='GEAR':color=(.055,.055,.055,1)
        # Freeze source transforms and convert to Blender X right, Y aft, Z up.
        matrix=matrices[n];o.parent=None;o.matrix_world.identity()
        for v in o.data.vertices:
            p=matrix@v.co
            if kind=='SuperCub':p=Vector((-p.y,p.x,p.z))
            if kind=='C172':p=Vector((p.x,p.y-.45,p.z-1.50))*(11.0/12.60)
            elif kind=='SuperCub':p.z-=.20
            else:
                p*=15.0/15.18
                # Donor fuselage/tail retained; replace its generic wings with authored CS panels.
                p.y=p.y*(6.59/5.87)+1.34
            v.co=p
        o.data.validate();o.data.update();color_mesh(o,color)
        if kind=='Discus' and n=='GLIDER':
            bm=bmesh.new();bm.from_mesh(o.data)
            remove=[v for v in bm.verts if abs(v.co.x)>.37 and v.co.y<3.0]
            bmesh.ops.delete(bm,geom=remove,context='VERTS');bm.to_mesh(o.data);bm.free();o.data.update()
        if kind=='C172' and 'Exterior_Glass' in n:protected.append(o)
        else:groups[part].append(o)
    if kind=='Discus':groups['Body'].append(discus_wings())
    base={name:join(objs,name) for name,objs in groups.items() if objs}
    exports=[];model_record={'type':kind,'lods':[]}
    for lod,budget in enumerate([2800,850,180]):
        lod_records=[]
        for part,source in base.items():
            obj=source.copy();obj.data=source.data.copy();bpy.context.collection.objects.link(obj)
            obj.name=f'{kind}_LOD{lod}_{part}'
            obj.data.calc_loop_triangles();count=len(obj.data.loop_triangles)
            target=(budget-([240,120,24][lod] if protected else 0)) if part=='Body' else ([100,50,12][lod])
            mod=obj.modifiers.new('ExteriorLOD','DECIMATE');mod.ratio=min(1,target/max(1,count));mod.use_collapse_triangulate=True
            bpy.context.view_layer.objects.active=obj;bpy.ops.object.modifier_apply(modifier=mod.name)
            mod=obj.modifiers.new('Triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=mod.name)
            if part=='Body' and protected:
                pieces=[obj]
                for glass in protected:
                    g=glass.copy();g.data=glass.data.copy();bpy.context.collection.objects.link(g)
                    g.data.calc_loop_triangles();mod=g.modifiers.new('ProtectedGlazing','DECIMATE');mod.ratio=min(1,[240,120,24][lod]/max(1,len(g.data.loop_triangles)));mod.use_collapse_triangulate=True
                    bpy.context.view_layer.objects.active=g;bpy.ops.object.modifier_apply(modifier=mod.name);pieces.append(g)
                obj=join(pieces,f'{kind}_LOD{lod}_{part}')
            removed_degenerate_triangles=remove_noncontributing_body_triangles(obj.data) if kind=='SuperCub' and part=='Body' else 0
            obj.data.validate()
            for poly in obj.data.polygons:poly.use_smooth=True
            obj.data.calc_loop_triangles()
            verts=[v.co for v in obj.data.vertices]
            lo=[min(v[i] for v in verts) for i in range(3)];hi=[max(v[i] for v in verts) for i in range(3)]
            lod_records.append({'part':part,'triangles':len(obj.data.loop_triangles),'min':lo,'max':hi,'removed_degenerate_triangles':removed_degenerate_triangles})
            exports.append(obj)
        model_record['lods'].append(lod_records)
    bpy.ops.object.select_all(action='DESELECT')
    for o in exports:o.select_set(True)
    path=OUT/f'{kind}_Ambient.glb'
    bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,export_materials='NONE',export_attributes=True,export_animations=False,export_cameras=False,export_lights=False)
    model_record['file']=path.name;model_record['sha256']=hashlib.sha256(path.read_bytes()).hexdigest()
    records.append(model_record)
(OUT/'geometry_report.json').write_text(json.dumps(records,indent=2))
print('AMBIENT EXTERIORS COMPLETE')
