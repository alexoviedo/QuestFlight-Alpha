"""Blender 5: compile the admitted OSU flight models, shared atlases and skinned LODs.
Raw downloads and audit renders remain outside Assets. Run with Blender --background --python.
"""
import bpy, math, json, hashlib, sys
from pathlib import Path
from mathutils import Vector

repo=Path(__file__).resolve().parents[2]
art=repo.parent/'T2-QuestFlight-product-artifacts/bird_implementation_20260922'
dest=repo/'QuestFlight/Assets/Production/Environment/Wildlife/HabitatBirds'
dest.mkdir(parents=True,exist_ok=True)
species=[('RedTailedHawk','red-tailed-hawk-in-flight',1.25,'4abcab5af9044a939fb5a55bff9e5000'),
         ('Osprey','osprey-in-flight',1.65,'909d68cc4f9249a2878a37c618467f31'),
         ('CanadaGoose','canada-goose-in-flight',1.65,'25fbbb9e607a4bccb05ae05c08fd0039')]
records=[]
for name,folder,span,uid in species:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    source=art/folder/'source.glb'
    bpy.ops.import_scene.gltf(filepath=str(source))
    arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    body=next(o for o in bpy.context.scene.objects if o.type=='MESH' and len(o.vertex_groups)>0)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name!='Icosphere']
    for o in list(bpy.context.scene.objects):
        if o.type=='MESH' and o not in meshes:bpy.data.objects.remove(o,do_unlink=True)
    # Feet are separate rigid source meshes. Give them the torso weight before joining.
    for o in meshes:
        if o==body:continue
        o.vertex_groups.new(name='spine').add(list(range(len(o.data.vertices))),1,'REPLACE')
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:o.select_set(True)
    bpy.context.view_layer.objects.active=body;bpy.ops.object.join()
    body.name=name+'_LOD0'
    # One source atlas/material; preserve existing UVs on body and feet.
    material=body.data.materials[0]
    for p in body.data.polygons:p.material_index=0
    body.data.materials.clear();body.data.materials.append(material)
    image=next(n.image for n in material.node_tree.nodes if n.type=='TEX_IMAGE' and n.image)
    image.scale(1024,1024);image.filepath_raw=str(dest/(name+'_Albedo.png'));image.file_format='PNG';image.save()
    action=arm.animation_data.action;action.name='FlapLoop';action.use_fake_user=True
    bag=next(b for b in action.layers[0].strips[0].channelbags if any('pose.bones[' in c.data_path for c in b.fcurves))
    for b in list(action.layers[0].strips[0].channelbags):
        if b!=bag:action.layers[0].strips[0].channelbags.remove(b)
    for o in bpy.context.scene.objects:
        if o!=arm and o.animation_data:o.animation_data_clear()
    arm.animation_data.action_slot=bag.slot
    # Measure actual animated wingspan rather than source object bounds.
    samples=[]
    for frame in range(61):
        bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
        pts=[body.matrix_world@v.co for v in body.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.vertices]
        samples.append((max(p.x for p in pts)-min(p.x for p in pts),frame))
    width,frame=max(samples)
    bpy.context.scene.frame_set(frame)
    center=arm.matrix_world@arm.pose.bones['spine'].head
    head=arm.matrix_world@arm.pose.bones['face'].head
    forward=head-center;forward.z=0
    # FBX -Z/Y export maps Blender -Y forward to Unity +Z.
    angle=-math.pi/2-math.atan2(forward.y,forward.x)
    wrapper=bpy.data.objects.new(name+'_Root',None);bpy.context.scene.collection.objects.link(wrapper)
    for o in list(bpy.context.scene.objects):
        if o!=wrapper and o.parent is None:o.parent=wrapper
    wrapper.rotation_euler.z=angle;wrapper.scale=(span/width,)*3
    from mathutils import Matrix
    wrapper.location=-(Matrix.Rotation(angle,4,'Z')@center)*(span/width)
    glide=action.copy();glide.name='SoarIdle';glide.use_fake_user=True
    for b in glide.layers[0].strips[0].channelbags:
        for c in b.fcurves:
            value=c.evaluate(frame);c.keyframe_points.clear()
            for f in (0,60):c.keyframe_points.insert(f,value)
    counts=[]
    for level,target in [(0,None),(1,900),(2,260)]:
        ob=body
        if level:
            ob=body.copy();ob.data=body.data.copy();bpy.context.collection.objects.link(ob);ob.name=name+'_LOD'+str(level)
            bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
            tri=sum(len(p.vertices)-2 for p in ob.data.polygons)
            mod=ob.modifiers.new('OfflineSilhouetteReduction','DECIMATE');mod.ratio=target/tri
            # Simplify rest geometry before skinning, not a baked wing pose.
            bpy.ops.object.modifier_move_up(modifier=mod.name)
            bpy.ops.object.modifier_apply(modifier=mod.name)
        counts.append(sum(len(p.vertices)-2 for p in ob.data.polygons))
    arm.name=name+'_Armature'
    bpy.context.scene.render.fps=30
    arm.animation_data.action=action
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=str(dest/(name+'.fbx')),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},
        apply_scale_options='FBX_SCALE_ALL',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',path_mode='STRIP',embed_textures=False)
    bpy.ops.wm.save_as_mainfile(filepath=str(art/folder/(name+'.blend')))
    records.append(dict(species=name,source='https://sketchfab.com/3d-models/'+folder+'-'+uid,
        creator='Oregon State University Ecampus',license='CC BY 4.0',license_url='https://creativecommons.org/licenses/by/4.0/',
        source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),wingspan_m=span,triangles=counts,
        animation='Original articulated flight loop; extended-wing glide derived from maximum-span pose',glide_frame=frame,
        edits='Torso-weighted rigid feet, merged renderer/material, real scale, normalized heading, 1024 atlas, offline skinned LODs'))
(dest/'SOURCE_MANIFEST.json').write_text(json.dumps(records,indent=2)+'\n')
print(json.dumps(records,indent=2))
