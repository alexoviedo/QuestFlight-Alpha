"""Author a bounded crown-volume replacement from accepted V3 placements.

No new imagery, measured heights, or runtime population is inferred. Meshes and
source snapshots are external review artifacts until visual/build acceptance.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import numpy as np
import cv2
from build_hybrid_canopy_prototype import placements


def build_crowns(records, atlas_path):
    vertices, normals, colors, uv, modes, indices = [], [], [], [], [], []

    def triangle(points, ns, tint, coords, mode, ao=1.):
        start = len(vertices)
        vertices.extend(points)
        normals.extend(ns)
        colors.extend([[*tint, ao]] * 3)
        uv.extend(coords)
        modes.extend([[mode, 0]] * 3)
        indices.extend([start, start + 1, start + 2])

    def tube(a, b, radius, tint, sides=3):
        a, b = np.array(a), np.array(b)
        axis = b - a
        axis /= np.linalg.norm(axis)
        side = np.cross(axis, [0., 0., 1.])
        side /= np.linalg.norm(side)
        other = np.cross(axis, side)
        for j in range(sides):
            n0 = side * math.cos(j*math.tau/sides) + other * math.sin(j*math.tau/sides)
            n1 = side * math.cos((j+1)*math.tau/sides) + other * math.sin((j+1)*math.tau/sides)
            p = [a+n0*radius, a+n1*radius, b+n1*radius*.5, b+n0*radius*.5]
            for ix in ((0, 1, 2), (0, 2, 3)):
                triangle([p[k].tolist() for k in ix], [([n0,n1,n1,n0][k]).tolist() for k in ix], tint, [[0,0]]*3, 2, .72)

    atlas = cv2.imread(str(atlas_path), cv2.IMREAD_UNCHANGED)
    if atlas is None or atlas.shape[2]!=4: raise ValueError('RGBA branch atlas required')
    # Geometry follows each existing branch alpha hull. This trims empty card
    # corners without inventing image content or changing source texture pixels.
    hulls = []
    grid=2
    cellsize=atlas.shape[0]//grid
    for cell in range(grid*grid):
        x,y=cell%grid,cell//grid
        alpha=atlas[y*cellsize:(y+1)*cellsize,x*cellsize:(x+1)*cellsize,3]
        yy,xx=np.where(alpha>32)
        # Conservative six-corner envelope: clip the two emptiest bounding-box
        # corners using support lines of the alpha pixels. Unlike an inscribed
        # simplified hull this cannot cut leaf tips off the texture.
        xmin,xmax,ymin,ymax=float(xx.min()),float(xx.max()),float(yy.min()),float(yy.max())
        hull=[(xmin,ymin),(xmax,ymin),(xmax,ymax),(xmin,ymax)]
        cuts=[]
        for a,b in ((1,1),(1,-1),(-1,1),(-1,-1)):
            limit=float(np.min(a*xx+b*yy))-.75
            extent=limit-min(a*x+b*y for x,y in hull)
            cuts.append((extent,a,b,limit))
        for _,a,b,limit in sorted(cuts,reverse=True)[:2]:
            clipped=[]
            for p,q in zip(hull,hull[1:]+hull[:1]):
                dp,dq=a*p[0]+b*p[1]-limit,a*q[0]+b*q[1]-limit
                if dp>=0: clipped.append(p)
                if (dp<0)!=(dq<0):
                    t=dp/(dp-dq);clipped.append((p[0]+t*(q[0]-p[0]),p[1]+t*(q[1]-p[1])))
            hull=clipped
        # Dense four-quadrant branch scans retain large transparent openings.
        # Their alpha-bounded rectangle keeps this local coverage addition within
        # the existing whole-view triangle ceiling; no leaf pixels are removed.
        hulls.append(np.array([(xmin,ymin),(xmax,ymin),(xmax,ymax),(xmin,ymax)])/cellsize)
    for r in records:
        rng = np.random.default_rng(r['id'])
        radius, height = r['crown']*.5, r['height']
        base = np.array([r['x'], r['y'], r['z']])
        tint = np.array(r['color'])/255.
        phase = rng.uniform(0, math.tau)
        centers=[]
        card_count=13
        for j in range(card_count):
            angle=phase+j*2.39996323
            level=(j+.5)/card_count
            radial=radius*(.15+.46*math.sin(math.pi*level))
            origin=base+np.array([math.cos(angle)*radial,height*(.39+.46*level),math.sin(angle)*radial])
            centers.append(origin)
            side=np.array([-math.sin(angle),0,math.cos(angle)])
            # Rotated branch planes include overhead coverage; never billboards
            # facing the current eye. The same geometry is seen by both eyes.
            tilt=(j%4)*.33+.42
            up=np.array([math.cos(angle)*math.sin(tilt),math.cos(tilt),math.sin(angle)*math.sin(tilt)])
            halfwidth=radius*rng.uniform(.30,.43)
            halfheight=min(height*.16,radius*.72)
            cell=(r['id']+j*5)%(grid*grid)
            hull=hulls[cell]
            coords=[[float((cell%grid+u)/grid),float(1-(cell//grid+v)/grid)] for u,v in hull]
            points=[origin+side*((u-.5)*2*halfwidth)+up*((.5-v)*2*halfheight) for u,v in hull]
            ns=[]
            for p in points:
                n=(p-(base+[0,height*.59,0]))/np.array([radius,height*.35,radius])
                n[1]=max(n[1],.18)
                ns.append((n/np.linalg.norm(n)).tolist())
            for k in range(1,len(points)-1):
                face=(0,k,k+1)
                triangle([points[t].tolist() for t in face],[ns[t] for t in face],tint.tolist(),[coords[t] for t in face],1,.83+.17*level)
        tube(base, base+[0,height*.66,0], max(.09,height*.012),[.24,.19,.13],4)
        for center in (centers[2],centers[6]):
            tube(base+[0,height*.37,0],center,max(.045,height*.006),[.24,.19,.13])
    # Share identical card vertices and attributes; no triangle soup in the
    # shipping candidate. UV/normal seams remain separate.
    unique={}; remapped=[]; keep=[]
    for index in indices:
        key=tuple(round(float(v),5) for array in (vertices,normals,colors,uv,modes) for v in array[index])
        if key not in unique:
            unique[key]=len(keep); keep.append(index)
        remapped.append(unique[key])
    vertices,normals,colors,uv,modes=([array[i] for i in keep] for array in (vertices,normals,colors,uv,modes))
    indices=remapped
    def vectors(values, names):
        return [{k:round(float(v),5) for k,v in zip(names, row)} for row in values]
    return dict(schema='questflight.neighborhood-crowns.v1',vertices=vectors(vertices,'xyz'),
                normals=vectors(normals,'xyz'),colors=vectors(colors,'rgba'),uv=vectors(uv,'xy'),
                modes=vectors(modes,'xy'),indices=indices,source_records=records)


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--repo',type=Path,required=True)
    ap.add_argument('--output',type=Path,required=True)
    args=ap.parse_args()
    repo,out=args.repo.resolve(),args.output.resolve()
    if out==repo or repo in out.parents: raise ValueError('Review output must be external')
    source=repo/'QuestFlight/Assets/Production/Environment/FullMapVegetationV3/KBDU_FullMapVegetationHlodV3.bytes'
    # Shrubs keep the accepted representation. No new tree centers are added.
    records=[r for r in placements(source,[-1050,-800,-680,-400]) if r['kind']!=2]
    atlas=repo/'QuestFlight/Assets/Production/Environment/NeighborhoodFoliageV1/NeighborhoodFoliageAtlas.png'
    mesh=build_crowns(records,atlas)
    mesh['source_identity']={'path':str(source),'sha256':hashlib.sha256(source.read_bytes()).hexdigest()}
    mesh['atlas_identity']={'path':str(atlas),'sha256':hashlib.sha256(atlas.read_bytes()).hexdigest()}
    mesh['provenance']='Project-authored geometry; accepted V3 positions, crown widths, estimated heights and local colors. Existing project foliage textures; no new imagery.'
    mesh['metrics']={'crowns':len(records),'triangles':len(mesh['indices'])//3,'vertices':len(mesh['vertices']), 'draws':1,
                     'note':'Full local coverage; must measure admitted control crowns and replacement cost, not claim cost neutrality.'}
    out.mkdir(parents=True,exist_ok=True)
    (out/'crowns.json').write_text(json.dumps(mesh,separators=(',',':')),encoding='utf-8')
    print(json.dumps(mesh['metrics']))


if __name__=='__main__': main()
