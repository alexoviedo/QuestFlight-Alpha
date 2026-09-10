"""Build planar roof faces for a frozen, source-traced neighborhood review.

Keep existing inferred roof types/rises and recorded heights; these are not
surveyed architecture. Clip roof planes to retained source outlines so triangles
cannot fold across a ridge. Output is external and does not reallocate roofs.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
from shapely.geometry import Polygon
from shapely import constrained_delaunay_triangles


def clip_halfplane(points, a, b, c):
    result=[]
    for p,q in zip(points,points[1:]+points[:1]):
        dp,dq=a*p[0]+b*p[1]+c,a*q[0]+b*q[1]+c
        if dp<=1e-8: result.append(p)
        if (dp < -1e-8 and dq>1e-8) or (dp>1e-8 and dq < -1e-8):
            t=dp/(dp-dq)
            result.append((p[0]+t*(q[0]-p[0]),p[1]+t*(q[1]-p[1])))
    return result


def roof_faces(points, roof_id, rise, wall_top):
    source=Polygon(points)
    if not source.is_valid or source.area<1: raise ValueError('Invalid source footprint')
    rectangle=list(source.minimum_rotated_rectangle.exterior.coords)[:-1]
    edges=[(q[0]-p[0],q[1]-p[1]) for p,q in zip(rectangle,rectangle[1:]+rectangle[:1])]
    dx,dz=max(edges,key=lambda e:e[0]**2+e[1]**2)
    length=math.hypot(dx,dz); major=(dx/length,dz/length); minor=(-major[1],major[0])
    projected=[(x*major[0]+z*major[1],x*minor[0]+z*minor[1]) for x,z in points]
    u0,u1=min(p[0] for p in projected),max(p[0] for p in projected)
    v0,v1=min(p[1] for p in projected),max(p[1] for p in projected)
    center=((u0+u1)*.5,(v0+v1)*.5)
    extent=((u1-u0)*.5,(v1-v0)*.5)
    local=[(u-center[0],v-center[1]) for u,v in projected]
    polygon=Polygon(local)
    planes=[(0.,0.,0.)] if roof_id==0 or rise<=0 else [(0.,rise/extent[1],rise),(0.,-rise/extent[1],rise)]
    if roof_id==2 and rise>0:
        planes.extend([(rise/extent[0],0.,rise),(-rise/extent[0],0.,rise)])
    def world(p):
        u,v=p[0]+center[0],p[1]+center[1]
        return (u*major[0]+v*minor[0],u*major[1]+v*minor[1])
    def vertex(p):
        x,z=world(p)
        h=min(a*p[0]+b*p[1]+c for a,b,c in planes)
        return dict(x=round(x,5),y=round(wall_top+max(h,0),5),z=round(z,5))
    vertices=[]; indices=[]; area=0.
    bounds=[(-extent[0]-1,-extent[1]-1),(extent[0]+1,-extent[1]-1),(extent[0]+1,extent[1]+1),(-extent[0]-1,extent[1]+1)]
    for i,(a,b,c) in enumerate(planes):
        cell=bounds[:]
        for j,(aa,bb,cc) in enumerate(planes):
            if i!=j: cell=clip_halfplane(cell,a-aa,b-bb,c-cc)
        if len(cell)<3: continue
        face=polygon.intersection(Polygon(cell))
        polygons=list(face.geoms) if face.geom_type=='MultiPolygon' else [face]
        for part in polygons:
            if part.area<1e-8: continue
            for tri in constrained_delaunay_triangles(part).geoms:
                coords=list(tri.exterior.coords)[:3]
                # CCW x/z must be reversed for Unity's upward-facing roofs.
                if Polygon(coords).exterior.is_ccw: coords.reverse()
                indices.extend(range(len(vertices),len(vertices)+3))
                vertices.extend(vertex(p) for p in coords)
                area+=tri.area
    if abs(area-source.area)>max(.001,source.area*.00001): raise ValueError('Roof triangulation lost coverage')
    boundary=[]
    for p,q in zip(local,local[1:]+local[:1]):
        cuts=[0.]
        for i,(a,b,c) in enumerate(planes):
            for aa,bb,cc in planes[i+1:]:
                dp=(a-aa)*p[0]+(b-bb)*p[1]+c-cc
                dq=(a-aa)*q[0]+(b-bb)*q[1]+c-cc
                if abs(dp-dq)>1e-8:
                    t=dp/(dp-dq)
                    if 1e-6<t<1-1e-6: cuts.append(t)
        for t in sorted(set(round(t,8) for t in cuts)):
            boundary.append(vertex((p[0]+t*(q[0]-p[0]),p[1]+t*(q[1]-p[1]))))
    if not Polygon([(p['x'],p['z']) for p in boundary]).exterior.is_ccw: boundary.reverse()
    return dict(boundary=boundary,vertices=vertices,indices=indices,projected_area=area)


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--evidence',type=Path,required=True)
    ap.add_argument('--output',type=Path,required=True)
    args=ap.parse_args()
    evidence=args.evidence
    ids={r['id'] for r in json.loads((evidence/'outline_candidate_ids.json').read_text())['records']}
    targets=json.loads((evidence/'target_records.json').read_text())['records']
    result=[]
    for r in targets:
        if r['id'] not in ids: continue
        # Target records retain page coordinates alongside the quantized outline.
        points=[(-12000+r['page'][0]*1000+x*.1,-12000+r['page'][1]*1000+z*.1) for x,z in zip(r['lod2_q'][::2],r['lod2_q'][1::2])]
        roof=roof_faces(points,r['roof_id'],r['roof_rise_dm']*.1,(r['base_y_dm']+r['height_dm'])*.1)
        roof['id']=r['id']; result.append(roof)
    output=dict(schema='questflight.neighborhood-roof-review.v1',buildings=result,
        provenance='Retained source outlines, accepted source heights and inferred roof type/rise. Planar construction is an artistic representation, not measured roof detail.',
        source_sha256=hashlib.sha256((evidence/'target_records.json').read_bytes()).hexdigest())
    if len(result)!=len(ids): raise ValueError('Missing target records')
    args.output.write_text(json.dumps(output,separators=(',',':')),encoding='utf-8')
    print(json.dumps({'buildings':len(result),'roof_triangles':sum(len(r['indices'])//3 for r in result)}))


if __name__=='__main__': main()
