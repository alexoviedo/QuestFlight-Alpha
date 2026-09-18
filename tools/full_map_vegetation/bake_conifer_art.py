"""Bake lightweight conifer art from CC0 Poly Haven needle textures in Blender.

Only compact derived atlases enter Unity. Whole-tree views use the same source
card mesh and family deformation as the runtime shader, preserving LOD shape.
"""
import argparse, json, math, hashlib, struct, sys
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector

def args():
    p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--tree',type=Path,required=True)
    return p.parse_args(sys.argv[sys.argv.index('--')+1:])

def mesh_object(name,positions,faces,uvs,material):
    m=bpy.data.meshes.new(name);m.from_pydata(positions,[],faces);m.update()
    layer=m.uv_layers.new()
    for poly in m.polygons:
        for loop in poly.loop_indices:layer.data[loop].uv=uvs[m.loops[loop].vertex_index]
    o=bpy.data.objects.new(name,m);bpy.context.collection.objects.link(o);o.data.materials.append(material);return o

def leaf_material(name,color,alpha=None):
    m=bpy.data.materials.new(name);m.use_nodes=True;n=m.node_tree.nodes;n.clear();l=m.node_tree.links
    out=n.new('ShaderNodeOutputMaterial');mix=n.new('ShaderNodeMixShader');trans=n.new('ShaderNodeBsdfTransparent');emit=n.new('ShaderNodeEmission')
    tex=n.new('ShaderNodeTexImage');tex.image=color;l.new(tex.outputs['Color'],emit.inputs['Color'])
    cut=n.new('ShaderNodeMath');cut.operation='GREATER_THAN';cut.inputs[1].default_value=.27
    if alpha:
        mask=n.new('ShaderNodeTexImage');mask.image=alpha;l.new(mask.outputs['Color'],cut.inputs[0])
    else:l.new(tex.outputs['Alpha'],cut.inputs[0])
    l.new(cut.outputs[0],mix.inputs[0]);l.new(trans.outputs[0],mix.inputs[1]);l.new(emit.outputs[0],mix.inputs[2]);l.new(mix.outputs[0],out.inputs['Surface'])
    return m

def render_pixels(scene,camera,path,size):
    scene.render.resolution_x=scene.render.resolution_y=size;scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)
    image=bpy.data.images.load(str(path),check_existing=False)
    a=np.array(image.pixels[:],dtype=np.float32).reshape(size,size,4);bpy.data.images.remove(image)
    return a

def save_atlas(path,pixels):
    h,w,_=pixels.shape;im=bpy.data.images.new(path.stem,width=w,height=h,alpha=True)
    im.pixels.foreach_set(pixels.ravel());im.filepath_raw=str(path);im.file_format='PNG';im.save();bpy.data.images.remove(im)

def warp(p,center,t,family,maximum):
    # Keep each small leaf card rigid; reposition its center into a tapered
    # branch envelope instead of stretching its leaf texels into a giant cone.
    base=.39 if family==1 else .22
    radius=.30*(1-t)**(.60 if family==1 else .90)
    angle=t*41.0
    c=np.array([math.cos(angle)*radius,maximum*(base+(1-base)*t),math.sin(angle)*radius])
    return c+(p-center)*(.70-.48*t)

def main():
    a=args();a.source=a.source.resolve();a.output=a.output.resolve();a.tree=a.tree.resolve()
    a.output.mkdir(parents=True,exist_ok=True);scratch=a.output/'bake_frames';scratch.mkdir(exist_ok=True)
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=8;s.cycles.use_denoising=False
    s.render.film_transparent=True;s.render.image_settings.file_format='PNG';s.render.image_settings.color_mode='RGBA';s.render.resolution_percentage=100
    s.view_settings.view_transform='Standard';s.view_settings.look='None'
    s.world.color=(.35,.35,.35)
    camdata=bpy.data.cameras.new('BakeCamera');camdata.type='ORTHO';cam=bpy.data.objects.new('BakeCamera',camdata);bpy.context.collection.objects.link(cam);s.camera=cam
    color=bpy.data.images.load(str(a.source/'twig_diff.png'));alpha=bpy.data.images.load(str(a.source/'twig_alpha.png'));alpha.colorspace_settings.name='Non-Color'
    # The source UV sheet makes unused regions opaque. Remove only components
    # connected to this crop's boundary, retaining the isolated needle spray.
    from collections import deque
    pixels=np.array(alpha.pixels[:],np.float32).reshape(alpha.size[1],alpha.size[0],4)
    y0,y1,x0,x1=567,1005,15,235;crop=pixels[y0:y1,x0:x1,0].copy();mask=crop>.27
    boundary=np.zeros_like(mask);boundary[[0,-1],:]=True;boundary[:,[0,-1]]=True
    queue=deque(map(tuple,np.argwhere(mask&boundary)));remove=np.zeros_like(mask)
    while queue:
        y,x=queue.popleft()
        if y<0 or x<0 or y>=len(mask) or x>=mask.shape[1] or remove[y,x] or not mask[y,x]:continue
        remove[y,x]=True
        queue.extend(((y-1,x),(y+1,x),(y,x-1),(y,x+1)))
    cleaned=np.zeros_like(pixels);cleaned[:,:,3]=1;crop[remove]=0
    cleaned[y0:y1,x0:x1,:3]=crop[:,:,None];alpha.pixels.foreach_set(cleaned.ravel())
    mat=leaf_material('CC0 Pine needle twigs',color,alpha)
    atlas=np.zeros((1024,1024,4),np.float32)
    for tile in range(16):
        rng=np.random.default_rng(703+tile);positions=[];faces=[];uvs=[]
        # Branch-sized sprays assembled from small source needle fascicles.
        for j in range(25):
            ang=j*2.399963+float(rng.uniform(-.22,.22));rad=math.sqrt((j+.5)/25)*.72
            center=Vector((math.cos(ang)*rad,math.sin(ang)*rad,float(rng.uniform(-.12,.12))))
            length=float(rng.uniform(.35,.58));width=length*.48
            up=Vector((math.cos(ang),math.sin(ang),0));right=Vector((-up.y,up.x,0));n=len(positions)
            positions.extend([center-right*width-up*length,center-right*width+up*length,center+right*width+up*length,center+right*width-up*length])
            faces.extend([(n,n+1,n+2),(n,n+2,n+3)])
            uvs.extend([(x0/1024,y0/1024),(x0/1024,y1/1024),(x1/1024,y1/1024),(x1/1024,y0/1024)])
        o=mesh_object('NeedleSpray',positions,faces,uvs,mat)
        cam.location=(0,0,5);cam.rotation_euler=(0,0,0);camdata.ortho_scale=2.65
        frame=render_pixels(s,cam,scratch/f'branch_{tile:02}.png',256)
        y=3-tile//4;x=tile%4;atlas[y*256:(y+1)*256,x*256:(x+1)*256]=frame
        bpy.data.objects.remove(o,do_unlink=True)
    needle=a.output/'NeedleBranchAtlas.png';save_atlas(needle,atlas)
    im=bpy.data.images.load(str(needle));leaf=leaf_material('Derived needle branches',im)
    wood=bpy.data.materials.new('Bark');wood.diffuse_color=(.18,.13,.08,1);wood.use_nodes=True
    wood.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.18,.13,.08,1)
    raw=a.tree.read_bytes();name,nv,ni,nt=struct.unpack_from('<32sIII',raw,16)
    dtype=np.dtype([('p','<f4',3),('n','<f4',3),('c','u1',4),('uv','<f4',2)])
    vertices=np.frombuffer(raw,dtype=dtype,count=nv,offset=60)
    indices=np.frombuffer(raw,dtype='<u2',count=ni,offset=60+nv*36).reshape(-1,3)
    centers=vertices['p'].copy()
    # Components of each indexed leaf card are isolated in this source mesh.
    parent=list(range(nv))
    def find(i):
        while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
        return i
    for tri in indices:
        if np.all(vertices['c'][tri,0]<128):
            for i in tri[1:]:parent[find(int(i))]=find(int(tri[0]))
    groups={}
    for i in range(nv):
        if vertices['c'][i,0]<128:groups.setdefault(find(i),[]).append(i)
    rank=np.zeros(nv,np.float32)
    for ordinal,g in enumerate(groups.values()):
        centers[g]=vertices['p'][g].mean(axis=0);rank[g]=(ordinal//3+.5)/math.ceil(len(groups)/3)
    maximum=float(vertices['p'][:,1].max());whole=np.zeros((2048,2048,4),np.float32)
    for family in (1,2):
        pts=[]
        for i,v in enumerate(vertices):
            p=v['p'].copy()
            if v['c'][0]<128:p=warp(p,centers[i],float(rank[i]),family,maximum)
            else:
                blend=np.clip((np.linalg.norm(p[[0,2]])-.025)/.065,0,1);blend=blend*blend*(3-2*blend)
                p[1]*=1.33+(.90-1.33)*blend;p[[0,2]]*=.55
            pts.append((float(p[0]),float(p[2]),float(p[1])))
        uvs=[(float(v['uv'][0]%1),float(v['uv'][1])) for v in vertices]
        o=mesh_object('Conifer',pts,indices.tolist(),uvs,leaf);o.data.materials.append(wood)
        for poly,tri in zip(o.data.polygons,indices):poly.material_index=1 if vertices['c'][tri[0],0]>=128 else 0
        for frame in range(8):
            ang=frame*math.tau/8;cam.location=(math.sin(ang)*4,-math.cos(ang)*4,maximum*.5)
            cam.rotation_euler=(Vector((0,0,maximum*.5))-cam.location).to_track_quat('-Z','Y').to_euler();camdata.ortho_scale=maximum*1.08
            pixels=render_pixels(s,cam,scratch/f'tree_{family}_{frame}.png',512)
            x=frame%4;y=3-((family-1)*2+frame//4);whole[y*512:(y+1)*512,x*512:(x+1)*512]=pixels
        bpy.data.objects.remove(o,do_unlink=True)
    save_atlas(a.output/'ConiferTreeAtlas.png',whole)
    provenance=json.loads((a.source/'downloads.json').read_text())
    provenance.update(generator='tools/full_map_vegetation/bake_conifer_art.py',treeSourceSha256=hashlib.sha256(raw).hexdigest(),
        representation='Small CC0 needle sprays; open-pine and tapered-fir visual approximations from existing card topology. Not species scans.',
        outputs={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in a.output.glob('*.png')})
    (a.output/'ConiferArt_SOURCE.json').write_text(json.dumps(provenance,indent=2)+'\n')
    print('CONIFER_ART_COMPLETE',flush=True)

if __name__=='__main__':main()
