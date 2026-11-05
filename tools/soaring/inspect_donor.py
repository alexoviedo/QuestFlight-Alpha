"""Blender offline source audit; use --background --disable-autoexec --python.
The raw download is read only. All outputs are external development evidence.
"""
import bpy
from mathutils import Vector
import json
from pathlib import Path
import sys

source, output = map(Path, sys.argv[sys.argv.index("--") + 1:])
output.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
objects = []
points = []
for obj in bpy.context.scene.objects:
    if obj.type != 'MESH':
        continue
    mesh = obj.data
    mesh.calc_loop_triangles()
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    points.extend(corners)
    objects.append(dict(name=obj.name, vertices=len(mesh.vertices), triangles=len(mesh.loop_triangles),
        materials=[m.name if m else None for m in mesh.materials], uv_layers=len(mesh.uv_layers),
        bounds_min=[min(p[i] for p in corners) for i in range(3)],
        bounds_max=[max(p[i] for p in corners) for i in range(3)],
        modifiers=[m.type for m in obj.modifiers]))
low = Vector([min(p[i] for p in points) for i in range(3)])
high = Vector([max(p[i] for p in points) for i in range(3)])
audit = dict(source=str(source), blender=bpy.app.version_string, objects=objects,
    total_triangles=sum(o['triangles'] for o in objects), total_vertices=sum(o['vertices'] for o in objects),
    bounds_min=list(low), bounds_max=list(high), dimensions=list(high-low),
    materials=[dict(name=m.name, diffuse=list(m.diffuse_color)) for m in bpy.data.materials],
    images=[dict(name=i.name, size=list(i.size), source=i.filepath) for i in bpy.data.images],
    non_mesh=[dict(name=o.name,type=o.type) for o in bpy.context.scene.objects if o.type!='MESH'],
    rights='No license or author notice included in downloaded ZIP; local-only derivative',
    classification='PENDING_INSPECTED_VIEWS')
(output/'donor_geometry.json').write_text(json.dumps(audit,indent=2)+'\n')
scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=1500
scene.render.resolution_y=1000
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.world=bpy.data.worlds.new('Neutral development lighting')
scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(0.32,0.38,0.46,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=0.6
light=bpy.data.lights.new('Audit sun','SUN');light.energy=3
sun=bpy.data.objects.new('Audit sun',light);scene.collection.objects.link(sun);sun.rotation_euler=(0.5,-0.4,-0.6)
camera=bpy.data.cameras.new('Offline audit camera');camera.type='ORTHO'
cam=bpy.data.objects.new('Offline audit camera',camera);scene.collection.objects.link(cam);scene.camera=cam
center=(low+high)/2;extent=max(high-low)
for name,direction in [('top',(0,0,1)),('side',(1,0,0.08)),('quarter',(1,-1,0.6))]:
    cam.location=center+Vector(direction).normalized()*extent*2
    cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    camera.ortho_scale=extent*1.15
    scene.render.filepath=str(output/(name+'.png'));bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(output/'inspection_only.blend'))
