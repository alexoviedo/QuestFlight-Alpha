"""Offline local-only donor derivative. Never edits the original GLB or ZIP.

Output is explicit mesh data for Unity Editor authoring, not a runtime loader.
"""
import bpy,json,sys,hashlib,math
from pathlib import Path
from mathutils import Vector
source,output=map(Path,sys.argv[sys.argv.index('--')+1:]);output.mkdir(parents=True,exist_ok=True)
# Planning limits fixed before generating geometry, not evidence of global headroom.
targets=dict(pilot_triangles=8000,pilot_draw_slots=14,external_lod0_triangles=5000,external_lod1_triangles=2200,external_lod2_triangles=600,gauge_atlas_max_dimension=512)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(source));bpy.context.view_layer.update()
dg=bpy.context.evaluated_depsgraph_get()
span=15.5448;length=7.8486;height=2.8321
sx=length/16.20609951019287;sy=span/18.942381858825683;sz=height/4.12678062915802
# The donor has a demonstrated fore-aft/span ratio error. One documented
# correction matrix handles all source geometry; central-width correction is
# an explicit cockpit estimate (0.55) rather than a manufacturer measurement.
width_factor=.55
def corrected(p):
    y=p.y
    if abs(y)<=1:y*=width_factor
    elif abs(y)<2:y=math.copysign(width_factor+(abs(y)-1)*(2-width_factor),y)
    return Vector((-y*sy,p.z*sz,p.x*sx))

# Find the black wheel component geometrically in the source hull; disconnected
# source triangles/IDs are retained in the audit. Do not infer native physics.
hull=bpy.data.objects['Cylinder'];ev=hull.evaluated_get(dg);m=ev.to_mesh();m.calc_loop_triangles()
parents=list(range(len(m.vertices)))
def root(i):
    while parents[i]!=i:parents[i]=parents[parents[i]];i=parents[i]
    return i
for poly in m.polygons:
    for v in poly.vertices[1:]:parents[root(v)]=root(poly.vertices[0])
groups={}
for i,v in enumerate(m.vertices):groups.setdefault(root(i),[]).append(ev.matrix_world@v.co)
components=[]
for vertices in groups.values():
    lo=Vector([min(p[i] for p in vertices) for i in range(3)]);hi=Vector([max(p[i] for p in vertices) for i in range(3)])
    components.append(dict(minimum=list(lo),maximum=list(hi),vertices=len(vertices)))
ev.to_mesh_clear()
# A source-relative longitudinal CG anchor is resolved from the main wheel;
# this donor approximation is auditable and must be checked in the final view.
candidates=[c for c in components if c['minimum'][2]<-1 and c['maximum'][0]>0 and c['maximum'][0]-c['minimum'][0]<3]
if not candidates:raise RuntimeError('Cannot identify donor main wheel bounds; inspect components before authoring.')
wheel=max(candidates,key=lambda c:c['vertices']);wheel_raw=Vector([(a+b)/2 for a,b in zip(wheel['minimum'],wheel['maximum'])])
cg_station=81.56315789473685
cg_z_inches=(610*-6.4+340*4)/950
wheel_bottom=wheel_raw.copy();wheel_bottom.z=wheel['minimum'][2]
native_main_contact=Vector((0,(-30-cg_z_inches)*.0254,-(88.75-cg_station)*.0254))
cg_corrected=corrected(wheel_bottom)-native_main_contact
def point(p):return corrected(p)-cg_corrected

removed={'Circle','Circle.001','Circle.002','Circle.003','Plane.001','Plane.004'}
moving={'Cube':'LeftAileron','Cube.002':'RightAileron','Cube.001':'Rudder','Cube.005':'Elevator',
        'Cylinder.001':'FrontStick','Cylinder.004':'RearStick','Plane.002':'FrontPedals','Plane.003':'RearPedals','Sphere':'TowRelease'}
parts={};removed_records=[]
def add_triangle(name,points,color):
    part=parts.setdefault(name,dict(name=name,vertices=[],colors=[],triangles=[]))
    offset=len(part['vertices']);part['vertices'] += [dict(x=p.x,y=p.y,z=p.z) for p in points]
    part['colors'] += [dict(r=color[0],g=color[1],b=color[2],a=color[3])]*3
    part['triangles'] += [offset,offset+2,offset+1] # reflected RH source -> Unity basis

for obj in list(bpy.context.scene.objects):
    if obj.type!='MESH' or not obj.visible_get():continue
    ev=obj.evaluated_get(dg);mesh=ev.to_mesh();mesh.calc_loop_triangles()
    if obj.name in removed:
        removed_records.append(dict(object=obj.name,triangles=list(range(len(mesh.loop_triangles))),reason='Old decorative face/checklist/leaf removed; generated face will have sole ownership.'))
        ev.to_mesh_clear();continue
    # High-resolution decorative knob spheres cost more than the airframe.
    # Keep their housing shape, using a deterministic offline decimation.
    if obj.name in ['Sphere','Sphere.001','Cylinder.005']:
        ev.to_mesh_clear();mod=obj.modifiers.new('Local donor knob reduction','DECIMATE');mod.ratio=.12
        bpy.context.view_layer.update();ev=obj.evaluated_get(dg);mesh=ev.to_mesh();mesh.calc_loop_triangles()
    for tri in mesh.loop_triangles:
        mat=mesh.materials[tri.material_index] if tri.material_index<len(mesh.materials) else None
        color=tuple(mat.diffuse_color) if mat else (.5,.5,.5,1)
        name='Canopy' if color[3]<.9 else moving.get(obj.name,'StaticHull')
        add_triangle(name,[point(ev.matrix_world@mesh.vertices[i].co) for i in tri.vertices],color)
    ev.to_mesh_clear()

anchors={
    'LeftAileron':point(Vector((1.825,7.0,1.74))), 'RightAileron':point(Vector((1.825,-7.0,1.74))),
    'Elevator':point(Vector((-8.17,0,.72))), 'Rudder':point(Vector((-7.505,0,1.72))),
    'FrontStick':point(Vector((5.03,0,-.38))), 'RearStick':point(Vector((3.21,0,-.38))),
    'FrontPedals':point(Vector((5.72,0,-.40))), 'RearPedals':point(Vector((3.70,0,-.40))),
    'TowRelease':point(Vector((5.29,0,.56))), 'Eye':point(Vector((4.28,0,1.48))),
    'Panel':point(Vector((5.30,0,.72))), 'MainWheel':point(wheel_raw)}
for name,part in parts.items():
    anchor=anchors.get(name,Vector((0,0,0)));part['anchor']=dict(x=anchor.x,y=anchor.y,z=anchor.z)
    # Keep vertices in the common aircraft CG frame for one combined moving mesh.
total=sum(len(p['triangles'])//3 for p in parts.values())
assert total<targets['pilot_triangles']
result=dict(schema='questflight.local-sgs-donor-geometry.v2',source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),rights='LOCAL_ONLY_UNCLEAR_RIGHTS',
    targets=targets,classification='ACCEPT_WITH_REPAIRS',source_basis='Blender +X forward, +Y left, +Z up',unity_basis='X right, Y up, Z forward',
    dimensions_reference_m=dict(span=span,length=length,height=height),source_axis_scale=[sx,sy,sz],central_width_factor=width_factor,
    correction_basis='Source measured16.2061/18.9424/4.12678 versus manufacturer7.8486/15.5448/2.8321m; donor proportions demonstrably wrong. Central cockpit width engineering estimate.',
    loaded_reference_cg_structural_x_inches=cg_station,loaded_reference_cg_structural_z_inches=cg_z_inches,
    wheel_alignment='Lowest undeformed donor tire point aligned to native main contact, loaded 950 lb CG. Gear vertical dimensions are engineering estimates.',
    main_contact_unity_m=list(native_main_contact),source_cg_corrected=list(cg_corrected),wheel_component=wheel,
    source_hull_components=components,removed_faces=removed_records,anchors={n:dict(x=v.x,y=v.y,z=v.z) for n,v in anchors.items()},parts=list(parts.values()),triangles=total,
    repairs_remaining=['Wing struts','Dive-brake panels','Canopy/door latch','Trim lever','Generated single-owner gauge faces and needles','Unity stereo/anchor/normal inspection'])
(output/'sgs_geometry.json').write_text(json.dumps(result,separators=(',',':'))+'\n')
print(json.dumps(dict(triangles=total,parts=[(p['name'],len(p['triangles'])//3) for p in parts.values()],anchors=result['anchors'],main_wheel=wheel),indent=2))
