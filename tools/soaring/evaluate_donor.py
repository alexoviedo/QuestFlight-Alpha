"""Evaluated donor geometry and correctly framed offline inspection views."""
import bpy,json,sys
from pathlib import Path
from mathutils import Vector
source,output=map(Path,sys.argv[sys.argv.index('--')+1:]);output.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(source))
bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get();points=[];records=[]
for obj in bpy.context.scene.objects:
    if obj.type!='MESH':continue
    evaluated=obj.evaluated_get(dg);mesh=evaluated.to_mesh();mesh.calc_loop_triangles()
    vertices=[evaluated.matrix_world@v.co for v in mesh.vertices]
    if not vertices:continue
    low=[min(p[i] for p in vertices) for i in range(3)];high=[max(p[i] for p in vertices) for i in range(3)]
    record=dict(name=obj.name,hidden_render=obj.hide_render,visible=obj.visible_get(),triangles=len(mesh.loop_triangles),vertices=len(mesh.vertices),
        minimum=low,maximum=high,materials=[m.name if m else None for m in mesh.materials])
    records.append(record)
    if obj.visible_get() and not obj.hide_render:points+=vertices
    evaluated.to_mesh_clear()
low=Vector([min(p[i] for p in points) for i in range(3)]);high=Vector([max(p[i] for p in points) for i in range(3)])
(output/'evaluated_geometry.json').write_text(json.dumps(dict(records=records,minimum=list(low),maximum=list(high),dimensions=list(high-low)),indent=2)+'\n')
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Neutral inspection');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.32,.38,.46,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.6
sun=bpy.data.objects.new('Sun',bpy.data.lights.new('Sun','SUN'));scene.collection.objects.link(sun);sun.data.energy=3;sun.rotation_euler=(.5,-.4,-.6)
camera=bpy.data.objects.new('Inspection',bpy.data.cameras.new('Inspection'));scene.collection.objects.link(camera);scene.camera=camera
center=(low+high)/2;extent=max(high-low)
for name,direction in [('top',(0,0,1)),('side',(0,-1,.02)),('front',(1,0,0)),('quarter',(1,-1,.6))]:
    camera.data.type='ORTHO';camera.location=center+Vector(direction).normalized()*extent*2
    camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
    inv=camera.rotation_euler.to_matrix().transposed();local=[inv@(p-center) for p in points]
    width=max(p.x for p in local)-min(p.x for p in local);height=max(p.y for p in local)-min(p.y for p in local)
    camera.data.ortho_scale=max(width,height*1.6)*1.12
    scene.render.filepath=str(output/(name+'.png'));bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(output/'evaluated_inspection.blend'))
