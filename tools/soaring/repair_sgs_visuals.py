"""Blender-only SGS donor repair. Original download is read-only.

Exports explicit Unity geometry, normals and grip anchors. No runtime import.
Dimensions/control locations beyond manual descriptions are modeling estimates.
"""
import bpy, json, math, sys, hashlib
from pathlib import Path
from mathutils import Vector

source, previous, output = map(Path, sys.argv[sys.argv.index('--') + 1:])
output.mkdir(parents=True, exist_ok=True)
old = json.loads(previous.read_text())
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
bpy.context.view_layer.update()
donor_objects = list(bpy.context.scene.objects)
sx, sy, sz = old['source_axis_scale']
cg = Vector(old['source_cg_corrected'])

def xyz(v): return Vector((v[0], v[2], v[1]))  # Unity -> Blender
def unity(v): return (v.x, v.z, v.y)
def normalize_source(p):
    y = p.y
    if abs(y) <= 1: y *= .55
    elif abs(y) < 2: y = math.copysign(.55 + (abs(y)-1)*1.45, y)
    return xyz(Vector((-y*sy, p.z*sz, p.x*sx))-cg)

materials = {}
def mat(name, color, rough=.6):
    if name in materials: return materials[name]
    m = bpy.data.materials.new(name); m.diffuse_color=(*color[:3], color[3] if len(color)>3 else 1)
    m.use_nodes=True; bs=m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value=m.diffuse_color; bs.inputs['Roughness'].default_value=rough
    if m.diffuse_color[3]<1:
        bs.inputs['Alpha'].default_value=m.diffuse_color[3]
        bs.inputs['Transmission Weight'].default_value=.85
    materials[name]=m; return m

paint=mat('Warm ivory paint',(.78,.77,.71),.35)
inner=mat('Cockpit painted metal',(.52,.53,.49),.68)
red=mat('Red finish',(.58,.025,.025),.38)
black=mat('Instrument panel and rubber',(.018,.021,.022),.82)
steel=mat('Satin hardware',(.36,.38,.37),.30)
cloth=mat('Woven seat cushion',(.21,.22,.18),.95)
strap=mat('Harness webbing',(.14,.12,.09),.95)
wood=mat('Stick grip',(.30,.16,.065),.65)
blue=mat('Airbrake identification',(.055,.17,.45),.65)
green=mat('Trim identification',(.11,.30,.13),.75)
glass=mat('Single canopy shell',(.56,.72,.78,.16),.12)
anchors={k:dict(v) for k,v in old['anchors'].items()}
anchors.update(Panel=dict(x=0,y=.88,z=1.10), FrontStick=dict(x=0,y=.25,z=.98),
    RearStick=dict(x=0,y=.22,z=.08), TowRelease=dict(x=0,y=.735,z=1.065),
    BrakePivot=dict(x=-.305,y=.67,z=.94), TrimPivot=dict(x=-.310,y=.435,z=.86),
    BrakeGrip=dict(x=-.21,y=.72,z=.94), TrimGrip=dict(x=-.275,y=.590,z=.86),
    StickGrip=dict(x=0,y=.70,z=.98))
def av(name): return Vector(tuple(anchors[name][k] for k in ('x','y','z')))

groups={}; origins={}; axes={}; reused=[]
def register(obj, group='StaticHull', anchor=None):
    groups.setdefault(group,[]).append(obj)
    if anchor is not None: origins[group]=Vector(anchor)
    return obj
def finish(obj, material, group='StaticHull', anchor=None, smooth=True):
    print('BUILD',obj.name,flush=True);obj.data.materials.clear(); obj.data.materials.append(material)
    for poly in obj.data.polygons: poly.use_smooth=smooth
    if hasattr(obj.data,'set_sharp_from_angle'): obj.data.set_sharp_from_angle(angle=math.radians(48))
    return register(obj,group,anchor)
def mesh(name, verts, faces, material, group='StaticHull', anchor=None, smooth=True):
    data=bpy.data.meshes.new(name);data.from_pydata([xyz(v) for v in verts],[],[tuple(reversed(f)) for f in faces]);data.update()
    obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj)
    return finish(obj,material,group,anchor,smooth)
def box(name, center, size, material, bevel=.012, group='StaticHull', anchor=None):
    bpy.ops.mesh.primitive_cube_add(size=1,location=xyz(center));o=bpy.context.object;o.name=name
    o.scale=xyz(size);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        b=o.modifiers.new('Rounded manufactured edges','BEVEL');b.width=bevel;b.segments=2 if ('cushion' in name or 'seat back' in name) else 1
        bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=b.name)
    return finish(o,material,group,anchor)
def tube(name,a,b,r,material,sides=8,group='StaticHull',anchor=None):
    a=xyz(a);b=xyz(b);d=b-a
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides,radius=r,depth=d.length,location=(a+b)*.5)
    o=bpy.context.object;o.name=name;o.rotation_euler=d.to_track_quat('Z','Y').to_euler()
    return finish(o,material,group,anchor)
def ball(name,center,scale,material,group='StaticHull',anchor=None):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=4,radius=1,location=xyz(center))
    o=bpy.context.object;o.name=name;o.scale=xyz(scale)
    return finish(o,material,group,anchor)
def path(name,points,r,material,sides=6,group='StaticHull',anchor=None):
    points=[Vector(p) for p in points];vv=[];ff=[]
    for n,p in enumerate(points):
        axis=(points[min(n+1,len(points)-1)]-points[max(0,n-1)]).normalized()
        ref=Vector((0,1,0)) if abs(axis.y)<.9 else Vector((1,0,0))
        u=axis.cross(ref).normalized()*r;v=axis.cross(u)
        for k in range(sides):vv.append(p+u*math.cos(k*2*math.pi/sides)+v*math.sin(k*2*math.pi/sides))
    for n in range(len(points)-1):
        for k in range(sides):a=n*sides+k;b=n*sides+(k+1)%sides;ff.append((a,b,b+sides,a+sides))
    ff +=[tuple(reversed(range(sides))),tuple((len(points)-1)*sides+k for k in range(sides))]
    mesh(name,vv,ff,material,group,anchor)

# Orthographic reconstruction from Simons 1996 / Schweizer supplied data.
# The inspected donor's lifting surfaces do not meet this silhouette/section gate.
for obj in donor_objects:
    obj.hide_render=True;obj.hide_set(True)
bpy.context.view_layer.objects.active=None
bpy.context.view_layer.update()
HEIGHT_SHIFT=.52  # final export aligns the native CG/contact frame, not the eye
Y=lambda value:value+HEIGHT_SHIFT
# Drawing side-view pixels normalized independently to published length/height.
# Digitization precision is limited by the scanned drawing, not manufacturing data.
X0=400;XEND=1527;SCALE=(XEND-X0)/7.8486
NOSE=.381+(705-X0)/SCALE
GROUND=-.69398
YSCALE=(697-270)/2.8321
YP=lambda pixel:GROUND+(697-pixel)/YSCALE
ZP=lambda pixel:NOSE-(pixel-X0)/SCALE
# Closed fiberglass nose and fabric fuselage, with separate inward cockpit wall.
# Upper/lower stations follow the actual side elevation; widths follow plan view.
profiles=[ # side x, upper y, lower y, half-width
 (400,610,610,.002),(412,581,630,.115),(435,558,647,.215),
 (470,542,665,.285),(518,532,677,.325),(526,540,679,.335),(535,548,680,.34),
 (590,548,684,.34925),(658,548,680,.34925),(735,548,665,.34925),
 (766,492,657,.342),(850,492,643,.315),(934,492,627,.285),
 (1040,492,608,.245),(1150,492,589,.202),(1260,492,573,.155),
 (1370,492,558,.110),(1465,492,548,.065),(1527,492,545,.035)]
# Roof opening is enclosed by the one-piece front canopy and fixed rear glazing.
# Cross-sections have round lower shell and near-vertical upper cabin sides.
N=20
for inside in (False,True):
    vv=[];ff=[]
    for px,top,bottom,rx in profiles:
        hi,lo=YP(top),YP(bottom);cy=(hi+lo)*.5;ry=(hi-lo)*.5
        cross=[(-rx,hi)]+[(rx*math.cos(math.pi+math.pi*k/16),cy+ry*math.sin(math.pi+math.pi*k/16)) for k in range(17)]+[(rx,hi),(0,hi)]
        if px<518:
            angles=[135]+[180+180*k/16 for k in range(17)]+[405,450]
            cross=[(rx*math.cos(math.radians(t)),cy+ry*math.sin(math.radians(t))) for t in angles]
        if px==518:
            cross[0]=(-rx,.294);cross[18]=(rx,.294)
        for x,y in cross:
            if inside:x*=.975;y=cy+(y-cy)*.975
            vv.append((x,Y(y),ZP(px)))
    for j in range(len(profiles)-1):
        for k in range(N):
            # Omit only cockpit roof; glass seals this same edge, not an open tub.
            px=profiles[j][0]
            if 518<=px<766 and k>=N-2:continue
            a=j*N+k;b=j*N+(k+1)%N
            face=(a,b,b+N,a+N);ff.append(tuple(reversed(face)) if inside else face)
    # Geometry is ordered nose -> tail: reverse faces for outward normals.
    ff=[tuple(reversed(f)) for f in ff]
    ff +=[tuple(reversed(range(N))),tuple((len(profiles)-1)*N+k for k in range(N))]
    mesh('Inner cockpit shell' if inside else 'Three-view fuselage',vv,ff,inner if inside else paint)
# Canopy: front bubble ends on a sloping rear frame, then fixed rear cabin glazing.
vv=[];ff=[];M=16
bubble=[(ZP(518),.325,.294,YP(532)),(ZP(526),.335,YP(540),.49),(ZP(535),.34,YP(548),.57),(1.40,.345,.294,.69),
        (1.15,.34925,.294,.78),(.93,.34925,.294,.83),(.70,.34925,.294,.878)]
for j,(z,rx,sill,roof) in enumerate(bubble):
    for k in range(M+1):
        t=math.pi*k/M;h=math.sin(t)
        zz=z-.319*h if j==len(bubble)-1 else z
        vv.append((rx*math.cos(t),Y(sill+(roof-sill)*h),zz))
for j in range(len(bubble)-1):
    for k in range(M):a=j*(M+1)+k;ff.append((a,a+M+1,a+M+2,a+1))
mesh('Closed one-piece front canopy',vv,ff,glass,'Canopy')
# Front opaque cowl seals the canopy leading boundary, above the rounded nose.
# Tiny forward glass cap meets the cowl without a missing front face.
mesh('Canopy front seam',[(0,Y(.294),ZP(518))]+[(.325*math.cos(math.pi*i/24),Y(.294+(YP(532)-.294)*math.sin(math.pi*i/24)),ZP(518)) for i in range(25)],[(0,i+1,i+2) for i in range(24)],paint)
# Fixed rear roof / side windows. No full-length bubble over both seats.
rear_front=[(-.34925,Y(.294),.70),(-.34925,Y(.878),.381),(.34925,Y(.878),.381),(.34925,Y(.294),.70)]
rear_back=[(-.342,Y(.294),ZP(766)),(-.342,Y(.663),ZP(766)),(.342,Y(.663),ZP(766)),(.342,Y(.294),ZP(766))]
mesh('Rear fixed cabin glazing',rear_front+rear_back,[(0,4,5,1),(1,5,6,2),(2,6,7,3)],glass,'Canopy')
for side in (-1,1):
    path('Front canopy sill',[(side*rx,Y(sill),z) for z,rx,sill,roof in bubble],.009,paint)
    path('Canopy aft frame',[(side*.34925,Y(.294),.70),(side*.34925,Y(.878),.381)],.010,paint)
    path('Rear cabin frame',[(side*.34925,Y(.294),.70),(side*.342,Y(.294),ZP(766)),(side*.342,Y(.663),ZP(766)),(side*.34925,Y(.878),.381)],.010,paint)
path('Rear canopy roof bow',[(-.34925,Y(.878),.381),(-.25,Y(.895),.381),(0,Y(.90),.381),(.25,Y(.895),.381),(.34925,Y(.878),.381)],.009,paint)
box('Canopy latch body',(.28,.77,.63),(.035,.022,.10),steel,.006)
tube('Canopy latch lever',(.28,.78,.63),(.22,.78,.63),.009,black)
# TRUE published NACA43012A coordinates, not the inaccurate wind-tunnel specimen.
airfoil=json.loads((output.parent/'references'/'naca43012a_true.json').read_text())['coordinates']
# Clip the exact coordinate polyline to preserve the airfoil across moving hinges.
def cut_profile(points,lo,hi):
    out=[]
    for a,b in zip(points,points[1:]+points[:1]):
        if lo-1e-9<=a[0]<=hi+1e-9:out.append(tuple(a))
        for bound in (lo,hi):
            if (a[0]-bound)*(b[0]-bound)<-1e-12:
                t=(bound-a[0])/(b[0]-a[0]);out.append((bound,a[1]+t*(b[1]-a[1])))
    # Degenerate duplicate TE/LE corners otherwise generate zero split normals.
    return [p for i,p in enumerate(out) if i==0 or (Vector(p)-Vector(out[i-1])).length>1e-7]
def loft(name,stations,section,group,anchor=None,vertical=False):
    vv=[];ff=[]
    for span,chord,leading,height,angle in stations:
        q=math.radians(angle)
        for u,t in section:
            d=u*chord;th=t*chord
            if vertical:vv.append((th,Y(height+span),leading-d))
            else:vv.append((span,Y(height-d*math.sin(q)+th*math.cos(q)),leading-d*math.cos(q)-th*math.sin(q)))
    L=len(section)
    for j in range(len(stations)-1):
        for k in range(L):a=j*L+k;b=j*L+(k+1)%L;ff.append((a,b,b+L,a+L))
    ff +=[tuple(reversed(range(L))),tuple((len(stations)-1)*L+k for k in range(L))]
    o=mesh(name,vv,ff,paint,group,anchor)
    # Recalculate outward mesh normals using Blender topology, never flat per tri.
    import bmesh
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
    return o
span=15.5448;half=span/2;root=1.524;tip=.762
kink=(20.39024/2-(root+tip)*half/2)/((root-tip)/2)
def wing_station(s,side,root=root,tip=tip,half=half,kink=kink):
    taper=max(0,(s-kink)/(half-kink));chord=root+(tip-root)*taper
    return (side*s,chord,.381-.25*taper,.878+math.tan(math.radians(1.33))*s,4-3*s/half)
for side in (-1,1):
    # 70% hinge is traced from the plan view. Main/aileron share the same section.
    innerStations=[0,.35,1.6,3.35]
    outerStations=[3.35,kink,4.7,6,7.45,half]
    loft('Inboard NACA43012A wing', [wing_station(v,side) for v in innerStations],airfoil,'StaticHull')
    loft('Outboard NACA43012A wing',[wing_station(v,side) for v in outerStations],cut_profile(airfoil,0,.70),'StaticHull')
    a=wing_station(3.35,side);anchor=Vector((side*3.35,Y(a[3]-.70*a[1]*math.sin(math.radians(a[4]))),a[2]-.70*a[1]*math.cos(math.radians(a[4]))))
    group='LeftAileron' if side<0 else 'RightAileron'
    loft('Airfoil-shaped '+group,[wing_station(v,side) for v in outerStations],cut_profile(airfoil,.70,1),group,anchor)
    a=wing_station(half,side);tipHinge=Vector((side*half,Y(a[3]-.70*a[1]*math.sin(math.radians(a[4]))),a[2]-.70*a[1]*math.cos(math.radians(a[4]))))
    axes[group]=(tipHinge-anchor).normalized()
    if axes[group].x<0:axes[group]=-axes[group]
    a=Vector((side*.31,Y(-.34),ZP(740)));b=Vector((side*3.35,Y(.878+.078),.15));axis=(b-a).normalized()
    chord=Vector((0,0,-1));chord=(chord-axis*chord.dot(axis)).normalized();thickness=axis.cross(chord).normalized()
    profile=[(0,0),(.06,.06),(.25,.10),(.55,.07),(1,0),(.55,-.07),(.25,-.10),(.06,-.06)]
    vv=[point+chord*((u-.25)*.09)+thickness*(v*.09) for point in (a,b) for u,v in profile]
    ff=[(k,(k+1)%8,(k+1)%8+8,k+8) for k in range(8)]+[tuple(reversed(range(8))),tuple(range(8,16))]
    strut=mesh('Streamlined lift strut',vv,ff,paint)
    import bmesh
    bm=bmesh.new();bm.from_mesh(strut.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));assert bm.calc_volume(signed=True)>0;bm.to_mesh(strut.data);bm.free()
    tube('Wing rear strut',(side*.31,Y(-.34),ZP(740)),(side*3.35,Y(.878+.078),-.65),.017,paint,6)
# Symmetric shaped tail sections: thickness is an explicit reference estimate;
# the sources inspected do not identify the tail's exact section designation.
def symmetric(thickness=.08,n=12):
    x=[(1-math.cos(math.pi*i/n))/2 for i in range(n+1)]
    y=lambda u:5*thickness*(.2969*math.sqrt(u)-.126*u-.3516*u*u+.2843*u**3-.1036*u**4)
    return [(u,y(u)) for u in reversed(x)]+[(u,-y(u)) for u in x[1:-1]]
# 93-inch horizontal span; capsule tips and hinge follow the plan elevation.
tailSec=symmetric();tailStations=[]
for x,c,le in [(-1.1811,.14,-4.73),(-1.10,.52,-4.50),(-.91,.84,-4.35),(0,.86,-4.34),(.91,.84,-4.35),(1.10,.52,-4.50),(1.1811,.14,-4.73)]:tailStations.append((x,c,le,.663,0))
loft('Airfoil horizontal stabilizer',tailStations,cut_profile(tailSec,0,.48),'StaticHull')
loft('Airfoil elevator',tailStations,cut_profile(tailSec,.48,1),'Elevator',Vector((0,Y(.663),-4.34-.48*.86)))
# Balanced 2-33A rudder overhang and swept fin, traced from side elevation.
vertical=[(0,.99,-4.28),(.55,.78,-4.51),(1.10,.57,-4.75),(1.475,.49,-4.91)]
loft('Airfoil fin',[(h,c,le,.663,0) for h,c,le in vertical],cut_profile(tailSec,0,.64),'StaticHull',vertical=True)
loft('Airfoil balanced rudder',[(h,c,le,.663,0) for h,c,le in vertical],cut_profile(tailSec,.64,1),'Rudder',Vector((0,Y(.663),-4.28-.64*.99)),vertical=True)
axes['Rudder']=Vector((0,1.475,(-4.91-.64*.49)-(-4.28-.64*.99))).normalized()
# Main monowheel/skid match unchanged native ground contact locations.
wheel=ball('Main tire',(0,Y(-.484),-.182),(.073,.21,.21),black)
# A sphere tire is rounded, closed geometry; side hub adds visible wheel identity.
for side in (-1,1):
    o=ball('Wheel hub',(side*.069,Y(-.484),-.182),(.008,.07,.07),steel)
path('Nose skid',[(0,Y(-.54),.18),(0,Y(-.65),.8),(0,Y(-.643),1.22),(0,Y(-.50),1.55)],.014,steel)
# Raked pitot/static mast above the nose, visible in real front-seat photos.
path('Pitot static mast',[(0,Y(.40),1.69),(0,Y(.75),1.83),(0,Y(.77),1.98)],.0045,steel,6)
# Photographed N2930H-style front panel; instrument fit varies between ships.
# Rounded forward coaming closes the former open shelf ahead of the panel.
panel=av('Panel');outline=[(-.325,-.14),(.325,-.14)]
outline +=[(.325*math.cos(t),-.035+.21*math.sin(t)) for t in [math.pi*i/24 for i in range(25)]]
verts=[(panel.x+x,panel.y+y,panel.z+d) for d in (0,.012) for x,y in outline];L=len(outline)
faces=[tuple(reversed(range(L))),tuple(range(L,2*L))]+[(i,(i+1)%L,(i+1)%L+L,i+L) for i in range(L)]
mesh('Arched instrument panel',verts,faces,black,smooth=False)
path('Rounded coaming',[(.325*math.cos(math.pi*i/24),panel.y-.035+.21*math.sin(math.pi*i/24),panel.z-.005) for i in range(25)],.006,steel,6)
# Back half of the trim bead is buried in the closed panel/coaming.
# Omit only those concealed faces; preserve the complete visible curved lip.
rim=bpy.data.objects['Rounded coaming'];old_rim=rim.data
rim_data=bpy.data.meshes.new('Coaming visible trim');rim_data.from_pydata([v.co[:] for v in old_rim.vertices],[],[list(p.vertices) for p in old_rim.polygons if p.normal.y<=.0001]);rim_data.update();rim.data=rim_data;rim.data.materials.append(steel)
for poly in rim.data.polygons:poly.use_smooth=True
vv=[];ff=[]
for t in (0,1):
    z=panel.z+(ZP(518)-panel.z)*t
    for i in range(25):
        a=math.pi*i/24
        # Blend the panel arch into the actual forward canopy/nose seam.
        y=(panel.y-.035+.21*math.sin(a))*(1-t)+Y(.294+(YP(532)-.294)*math.sin(a))*t
        vv.append((.325*math.cos(a),y,z))
for j in range(1):
    for i in range(24):
        a=j*25+i;ff.append((a,a+1,a+26,a+25))
mesh('Rounded forward instrument coaming',vv,ff,paint)
box('Floorboard',(0,.16,.72),(.44,.018,1.22),inner,.005)
for seat,z in (('Front',.43),('Rear',-.46)):
    box(seat+' seat pan',(0,.37,z),(.49,.055,.49),inner,.018)
    box(seat+' curved cushion',(0,.415,z),(.46,.07,.43),cloth,.032)
    b=box(seat+' seat back',(0,.66,z-.23),(.45,.50,.055),cloth,.025);b.rotation_euler.x=math.radians(-9)
    for side in (-1,1):
        box(seat+' lap harness',(side*.145,.46,z),(.065,.012,.40),strap,.004)
    box(seat+' belt buckle',(0,.47,z+.03),(.055,.018,.045),steel,.005)

for name,pivot,tip,ident in [('FrontStick',av('FrontStick'),av('StickGrip'),wood),
    ('RearStick',av('RearStick'),Vector((0,.65,.08)),wood)]:
    tube(name+' linkage',pivot,tip,.009 if 'Trim' in name else .013,steel,10,name,pivot)
    ball(name+' rounded grip',tip,(.020,.045,.022) if 'Stick' in name else (.025,.022,.035),ident,name,pivot)
    if 'Stick' in name:
        ball(name+' boot',pivot,(.055,.055,.075),black,name,pivot)
# Early sidewall bungee trim: dark flat dog-leg arm, flattened round grip,
# four-notch quadrant below the separate brake shaft (RCAF AOI1-2 para11b;
# photographed SGS2-33A at Wings & Wheels). Dimensions are photo estimates.
# Native continuous trim calibration is retained; latch force is not simulated.
trim=av('TrimPivot');grip=av('TrimGrip');origins['TrimLever']=trim
outline=[(0,0),(.008,0),(.008,.105),(.043,.148),(.027,.162),(0,.112)]
vv=[trim+Vector((x,y,z)) for z in (-.004,.004) for x,y in outline]
ff=[tuple(reversed(range(6))),tuple(range(6,12))]+[(i,(i+1)%6,(i+1)%6+6,i+6) for i in range(6)]
mesh('Trim flat dogleg arm',vv,ff,black,'TrimLever',trim,False)
tube('Trim flattened oval grip',grip+Vector((-.006,0,0)),grip+Vector((.006,0,0)),.023,black,8,'TrimLever',trim)
# Visible inboard face of the notched slot; no additional renderer/material.
rail=[(-.095,.045),(-.095,.088)]
for z in (-.078,-.026,.026,.078):rail.extend([(z-.009,.088),(z-.009,.075),(z+.009,.075),(z+.009,.088)])
rail.extend([(.095,.088),(.095,.045)])
verts=[trim+Vector((.007,y,z)) for z,y in rail]
mesh('Trim four notch sidewall quadrant',verts,[tuple(range(len(verts)))],black,smooth=False)
tube('Trim bungee linkage',trim+Vector((0,-.015,-.28)),trim,.005,black,6)
# Actual 2-33 cockpit photographs and the manufacturer diagram show a
# fore-aft sidewall shaft with an inward bent handgrip, not an upright stalk.
# Position, 230mm stroke and initial25deg release roll are reference estimates.
brake=av('BrakePivot');origins['BrakeLever']=brake;axes['BrakeLever']=Vector((0,0,1))
tube('Brake sliding painted shaft',brake+Vector((0,0,-.52)),brake,.011,paint,6,'BrakeLever',brake)
path('Brake bent elbow',[brake,brake+Vector((.025,.025,0)),brake+Vector((.045,.025,0))],.011,paint,6,'BrakeLever',brake)
tube('Brake black inward handgrip',brake+Vector((.045,.025,0)),brake+Vector((.145,.075,0)),.016,black,8,'BrakeLever',brake)
tube('Brake fixed sidewall guide',brake+Vector((0,0,-.38)),brake+Vector((0,0,-.34)),.017,inner,6)
release=av('TowRelease');tube('Release stem',release+Vector((0,0,.045)),release,.007,steel,8,'TowRelease',release)
ball('Red tow release knob',release,(.027,.027,.027),red,'TowRelease',release)
for name,z in [('FrontPedals',1.38),('RearPedals',.49)]:
    origin=Vector((0,.22,z));origins[name]=origin
    for side in (-1,1):
        box(name+' tread',(side*.13,.23,z),(.105,.055,.065),black,.012,name,origin)
        tube(name+' rail',(side*.13,.16,z-.12),(side*.13,.16,z+.12),.008,steel,6)
# SGS balanced dive brakes: three-view places the 45-inch spanwise panels
# between about 75 and 120 inches from centerline, around half chord. The
# upper panel extends aft of its hinge; the lower extends forward. Thin sheet
# with hinge-edge reliefs, as visible in the Barre-airport 1986 photographs.
# These digitized dimensions/reliefs are visual estimates, not factory CAD.
def skin_point(s, side, u, upper, offset=0):
    station=wing_station(s,side);_,c,le,h,angle=station
    candidates=[]
    for a,b in zip(airfoil,airfoil[1:]+airfoil[:1]):
        if min(a[0],b[0])-1e-8<=u<=max(a[0],b[0])+1e-8 and abs(b[0]-a[0])>1e-8:
            candidates.append(a[1]+(b[1]-a[1])*(u-a[0])/(b[0]-a[0]))
    thickness=(max(candidates) if upper else min(candidates))*c
    q=math.radians(angle)
    return Vector((side*s,Y(h-u*c*math.sin(q)+thickness*math.cos(q))+offset,le-u*c*math.cos(q)-thickness*math.sin(q)))
for side in (-1,1):
    for upper in (True,False):
        name=('Left' if side<0 else 'Right')+('UpperBrake' if upper else 'LowerBrake')
        start,end=1.905,3.048;hinge=.50;edge=.60 if upper else .40
        offset=.002 if upper else -.002
        a=skin_point(start,side,hinge,upper,offset);b=skin_point(end,side,hinge,upper,offset)
        origin=(a+b)*.5;axis=(b-a).normalized()
        if axis.x<0:axis=-axis
        axes[name]=axis
        # Three small open reliefs along the hinge edge, leaving four tabs.
        outline=[(start,hinge)]
        for fraction in (.25,.50,.75):
            s=start+(end-start)*fraction;w=.035;inset=.018 if upper else -.018
            outline +=[(s-w,hinge),(s-w,hinge+inset),(s+w,hinge+inset),(s+w,hinge)]
        outline +=[(end,hinge),(end,edge),(start,edge)]
        vv=[skin_point(s,side,u,upper,offset) for s,u in outline]
        # Opposite faces are explicit: no new material, shader or draw call.
        face=tuple(range(len(vv))) if side>0 else tuple(reversed(range(len(vv))))
        mesh(name+' painted sheet',vv,[face],paint,name,origin,smooth=False)
        mesh(name+' inner sheet',vv,[tuple(reversed(face))],inner,name,origin,smooth=False)
        # Dark well sits just beneath the closed skin panel; no floating red
        # slab, extra renderer, or new texture. Existing wing structure remains.
        well=[skin_point(s,side,u,upper,offset*.5) for s,u in [(start,hinge),(end,hinge),(end,edge),(start,edge)]]
        mesh(name+' recessed well',well,[(0,1,2,3) if side>0 else (3,2,1,0)],black,smooth=False)

# Original untextured artwork, matching the photographed four-instrument fit:
# altimeter far left, pressure VSI left inner, compass low centre, ASI right.
# Blank right cutout and centre fresh-air vent are not extra flight instruments.
ivory=mat('Dial markings',(.86,.86,.78),.95)
glyph={
 '0':'111101101101111','1':'010110010010111','2':'111001111100111',
 '3':'111001111001111','4':'101101111001001','5':'111100111001111',
 '6':'111100111101111','7':'111001010010010','8':'111101111101111',
 '9':'111101111001111','M':'101111111101101','P':'110101110100100',
 'H':'101101111101101','F':'111100110100100','T':'111010010010010',
 'S':'111100111001111','D':'110101101101110','V':'101101101101010',
 'I':'111010010010111','N':'101111111111101','E':'111100110100111',
 'W':'101101111111101','A':'010101111101101','L':'100100100100111',
 '.':'000000000000010','+':'000010111010000','-':'000000111000000'}

def lettering(text,center,height,group='StaticHull',anchor=None,mapper=None):
    # Merge adjacent glyph pixels into runs; same silhouette, fewer triangles.
    size=height/5;vv=[];ff=[];center=Vector(center)-Vector((len(text)*2*size,0,0))
    for n,char in enumerate(text):
        bits=glyph.get(char,'0'*15);active={};rectangles=[]
        for row in range(6):
            runs=[];col=0
            while row<5 and col<3:
                if bits[row*3+col]!='1':col+=1;continue
                first=col
                while col<3 and bits[row*3+col]=='1':col+=1
                runs.append((first,col))
            for run in list(active):
                if run not in runs:rectangles.append((*run,active.pop(run),row-1))
            for run in runs:active.setdefault(run,row)
        for first,last,top,bottom in rectangles:
            left=(n*4+first-.44)*size;right=(n*4+last-1+.44)*size
            low=(2-bottom-.44)*size;high=(2-top+.44)*size;i=len(vv)
            vv +=[center+Vector((left,low,0)),center+Vector((left,high,0)),center+Vector((right,high,0)),center+Vector((right,low,0))];ff.append((i,i+1,i+2,i+3))
    if vv:mesh('Dial '+text,[mapper(v) for v in vv] if mapper else vv,ff,ivory,group,anchor,smooth=False)

def disc(name,c,r,material,sides=24):
    v=[c]+[c+Vector((math.sin(i*math.tau/sides)*r,math.cos(i*math.tau/sides)*r,0)) for i in range(sides)]
    mesh(name,v,[(0,i+1,(i+1)%sides+1) for i in range(sides)],material,smooth=False)
def tick(c,r,degrees,length=.004):
    a=math.radians(degrees);d=Vector((-math.sin(a),math.cos(a),0));p=Vector((-d.y,d.x,0))*.0006
    a0=c+d*(r-length)-Vector((0,0,.001));b0=c+d*r-Vector((0,0,.001))
    mesh('Dial tick',[a0-p,a0+p,b0+p,b0-p],[(0,1,2,3)],ivory,smooth=False)
    return d
def needle(c,part,length,width,depth):
    origin=c-Vector((0,0,depth));v=[origin+Vector((-width,-.006,0)),origin+Vector((0,length,0)),origin+Vector((width,-.006,0))]
    mesh(part,v,[(0,1,2)],ivory,part,origin,smooth=False)
# Approximate face angles digitized from the photographed ASI; no calibration
# accuracy beyond the declared reference configuration is claimed.
asi_angles=[0,35,70,115,160,205,250,295,335]
for name,x,y,r in [('AltitudeNeedle',-.235,-.050,.043),('VarioNeedle',-.130,.020,.043),('AsiNeedle',.145,.020,.047)]:
    c=panel+Vector((x,y,-.022));disc('Instrument bezel',c,r+ .004,steel);c.z-=.0015;disc('Instrument face',c,r,black)
    if name=='AsiNeedle':
        for k in range(33):
            mph=k*5;idx=min(7,int(mph/20));angle=asi_angles[idx]+(asi_angles[idx+1]-asi_angles[idx])*(mph-idx*20)/20
            d=tick(c,r*.92,-angle,.004 if k%4==0 else .002)
            if k and k%4==0:lettering(str(mph),c+d*r*.72-Vector((0,0,.002)),.006)
        lettering('MPH',c+Vector((0,-.011,-.002)),.0055)
    elif name=='AltitudeNeedle':
        for k in range(50):
            d=tick(c,r*.91,-k*7.2,.004 if k%5==0 else .002)
            if k%5==0:lettering(str(k//5),c+d*r*.71-Vector((0,0,.002)),.007)
        lettering('FT',c+Vector((0,.014,-.002)),.005)
        lettering('STD',c+Vector((.014,-.012,-.002)),.004)
        needle(c,'AltitudeThousands',r*.57,.003,.005)
        needle(c,'AltitudeTenThousands',r*.40,.004,.006)
    else:
        for k in range(-15,16):
            d=tick(c,r*.91,90-k*12,.004 if k%5==0 else .002)
            if k%5==0 and abs(k)!=15:lettering('0' if k==0 else str(abs(k)/10).replace('0.','.'),c+d*r*.70-Vector((0,0,.002)),.0055)
        lettering('1.5',c+Vector((r*.65,0,-.002)),.005)
        lettering('1000',c+Vector((0,-.013,-.002)),.0045)
        lettering('FPM',c+Vector((0,-.020,-.002)),.0045)
    needle(c,name,r*.83,.0017,.004)
# Prominent fresh-air vent above the small magnetic compass, plus blank cover.
for x,y,r in [(0,.09,.033),(.245,-.055,.038)]:
    c=panel+Vector((x,y,-.016));disc('Vent or blank rim',c,r+.003,steel);disc('Vent or blank dark face',c-Vector((0,0,.001)),r,black)
    if x==0:
        for yy in (-.010,0,.010):
            mesh('Vent louvre',[c+Vector((-.024,yy,-.002)),c+Vector((.024,yy,-.002)),c+Vector((.024,yy+.003,-.002)),c+Vector((-.024,yy+.003,-.002))],[(3,2,1,0)],steel)
# A real compass card turns about vertical behind a fixed lubber line.
cc=panel+Vector((0,-.045,-.030));r=.025
origins['CompassCard']=cc;axes['CompassCard']=Vector((0,1,0))
bezel_vertices=[];bezel_faces=[]
for k in range(17):
    a=k*math.tau/16
    for radius in (.036,.033):bezel_vertices.append(cc+Vector((math.sin(a)*radius,math.cos(a)*radius,-.027)))
for k in range(16):
    a=k*2;bezel_faces.append((a,a+2,a+3,a+1))
mesh('Compass bezel',bezel_vertices,bezel_faces,steel,smooth=False)
vv=[];ff=[]
for i in range(37):
    t=i*math.tau/36
    for y in (-.010,.010):vv.append(cc+Vector((r*math.sin(t),y,-r*math.cos(t))))
for i in range(36):a=i*2;ff.append((a,a+1,a+3,a+2))
mesh('Magnetic compass card',vv,ff,black,'CompassCard',cc)
for k in range(12):
    theta=k*math.tau/12;label={0:'N',3:'E',6:'S',9:'W'}.get(k,str(k*3))
    def wrap(v,theta=theta):
        angle=theta+v.x/r;return cc+Vector(((r+.0004)*math.sin(angle),v.y,-(r+.0004)*math.cos(angle)))
    lettering(label,(0,0,0),.008,'CompassCard',cc,wrap)
# Fixed front mask leaves a narrow viewing window; no rotating heading needle.
mask_vertices=[];mask_faces=[]
for k in range(9):
    a=k*math.tau/8;dx,dy=math.sin(a),math.cos(a)
    boundary=min(.021/max(abs(dx),1e-6),.009/max(abs(dy),1e-6))
    mask_vertices +=[cc+Vector((dx*.034,dy*.034,-.026)),cc+Vector((dx*boundary,dy*boundary,-.026))]
for k in range(8):
    a=k*2;mask_faces.append((a,a+2,a+3,a+1))
mesh('Compass round window mask',mask_vertices,mask_faces,black,smooth=False)
mesh('Compass fixed lubber line',[cc+Vector((-.0006,-.008,-.027)),cc+Vector((.0006,-.008,-.027)),cc+Vector((.0006,.008,-.027)),cc+Vector((-.0006,.008,-.027))],[(3,2,1,0)],ivory,smooth=False)
for x,y in [(-.29,-.09),(-.20,.10),(-.07,.14),(.07,.14),(.20,.10),(.29,-.09)]:disc('Panel fastener',panel+Vector((x,y,-.014)),.0035,steel,6)

# Closed-shell ray witness from the nominal eye; no scenery/wing can hide leaks.
from mathutils.bvhtree import BVHTree
envelope=[bpy.data.objects.get(n) for n in ('Three-view fuselage','Closed one-piece front canopy','Rear fixed cabin glazing','Canopy front seam')]
vv=[];ff=[]
for obj in envelope:
    m=obj.data;m.calc_loop_triangles();base=len(vv);vv.extend(obj.matrix_world@v.co for v in m.vertices)
    ff.extend(tuple(base+i for i in t.vertices) for t in m.loop_triangles)
bvh=BVHTree.FromPolygons(vv,ff,all_triangles=True)
misses=[];distances=[];eye=xyz(av('Eye'))
for k in range(512):
    height=1-2*(k+.5)/512;phase=k*math.pi*(3-math.sqrt(5));rad=math.sqrt(1-height*height)
    direction=Vector((rad*math.cos(phase),rad*math.sin(phase),height))
    hit,normal,index,distance=bvh.ray_cast(eye,direction,20)
    if hit is None:misses.append(list(direction))
    else:distances.append(distance)
(output/'enclosure_rays.json').write_text(json.dumps(dict(rays=512,misses=misses,minDistance=min(distances),maxDistance=max(distances),scope='Geometric shell closure from nominal eye; not headset comfort acceptance'),indent=2))
print('ENCLOSURE_RAY_MISSES',len(misses),flush=True)
if misses:raise RuntimeError('Nominal pilot eye enclosure has open directions')

# Export evaluated triangle corners with their split normals. These normals were
# lost by the previous exporter, making even curved donor surfaces look faceted.
bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get();parts=[]
for name,objects in groups.items():
    result=dict(name=name,vertices=[],normals=[],colors=[],triangles=[],anchor={k:v for k,v in zip(('x','y','z'),origins.get(name,Vector()))})
    for obj in objects:
        ev=obj.evaluated_get(dg);m=ev.to_mesh();m.calc_loop_triangles();normal_matrix=ev.matrix_world.to_3x3().inverted().transposed()
        for tri in m.loop_triangles:
            ma=m.materials[tri.material_index] if tri.material_index<len(m.materials) else paint
            ma=bpy.data.materials.get(ma.name) if ma else paint
            c=list(ma.diffuse_color if ma else paint.diffuse_color)
            if name!='Canopy':
                bs=ma.node_tree.nodes.get('Principled BSDF') if ma and ma.use_nodes else None
                c[3]=float(bs.inputs['Roughness'].default_value) if bs else .75
            offset=len(result['vertices'])
            for vi,li in zip(tri.vertices,tri.loops):
                v=list(unity(ev.matrix_world@m.vertices[vi].co));v[1]-=HEIGHT_SHIFT
                normal=m.corner_normals[li].vector
                if normal.length_squared<1e-12:normal=tri.normal
                if normal.length_squared<1e-12:normal=Vector((0,0,1))
                normal=(normal_matrix@normal)
                if normal.length_squared<1e-12:normal=Vector((0,0,1))
                n=unity(normal.normalized())
                result['vertices'].append(dict(zip(('x','y','z'),v)));result['normals'].append(dict(zip(('x','y','z'),n)))
                result['colors'].append(dict(zip(('r','g','b','a'),c)))
            result['triangles'] +=[offset,offset+2,offset+1]
        ev.to_mesh_clear()
    result['axis']=dict(zip(('x','y','z'),axes.get(name,Vector())))
    parts.append(result)
for a in anchors.values():a['y']-=HEIGHT_SHIFT
for p in parts:p['anchor']['y']-=HEIGHT_SHIFT
count=sum(len(p['triangles'])//3 for p in parts)
if count>7964:raise RuntimeError('Repair exceeds retained8000-triangle ceiling including reserved yaw-string geometry: '+str(count))
result=dict(schema='questflight.sgs-reference-visual-repair.v1',source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
    completeRepair=True,completeInstruments=True,anchors=anchors,parts=parts,triangles=count,rights='LOCAL_ONLY_UNCLEAR_RIGHTS',
    reference='Simons1996 Schweizer-supplied three view + manual + UIUC V5 TRUE NACA43012A coordinates; digitized outline, not manufacturing CAD.',
    reusedDonor=reused,source_transform=old['correction_basis'],originalEyePreserved=False,eyeCorrection='Seat eye and all cockpit anchors lowered .52m to fit enclosed three-view canopy; only seat-relative frame changes, no tracked camera writes.',
    constraints='Original8000 pilot triangle planning ceiling retained, including Unity-authored gauge faces.3 renderers/2 materials/no new textures.')
(output/'sgs_geometry.json').write_text(json.dumps(result,separators=(',',':'))+'\n')
bpy.ops.wm.save_as_mainfile(filepath=str(output/'SGS233_reference_repair.blend'))
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1280;scene.render.resolution_y=960;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Reference inspection');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.24,.29,.34,1)
sun=bpy.data.objects.new('Inspection sunlight',bpy.data.lights.new('Inspection sunlight','SUN'));scene.collection.objects.link(sun);sun.data.energy=3;sun.rotation_euler=(.5,-.4,-.6)
camera=bpy.data.objects.new('Offline inspection camera',bpy.data.cameras.new('Offline inspection camera'));scene.collection.objects.link(camera);scene.camera=camera
for name,at,target,ortho in [('exterior',(10,7,11),(0,.6,-1.0),17),('side',(10,1,0),(0,.65,-1.4),9),('front',(0,1,15),(0,1,0),17),('top',(0,15,-1.4),(0,0,-1.4),17),('cockpit_open',(.85,1.65,1.6),(0,.60,.55),1.7),('pilot',(0,1.2215,.6501),(0,.89,1.4),0)]:
    camera.location=xyz(at);camera.rotation_euler=(xyz(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO' if ortho else 'PERSP';camera.data.ortho_scale=ortho or 1;camera.data.lens=20
    scene.render.filepath=str(output/(name+'.png'));bpy.ops.render.render(write_still=True)
print(json.dumps(dict(exportedTriangles=count,parts=[(p['name'],len(p['triangles'])//3) for p in parts],output=str(output))))
