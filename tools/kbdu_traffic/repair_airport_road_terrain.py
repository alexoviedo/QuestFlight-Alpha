"""Offline, local constrained subdivision of the existing lidar terrain.

Existing vertices/borders remain fixed. Pavement edges and 6 m road stations
split affected triangles; the new interior heights come from the pinned DTM.
Writes an external authoring payload, never modifies Unity assets itself.
"""
from pathlib import Path
import argparse, hashlib, json, re
import numpy as np
import shapely
from shapely.geometry import Polygon, LineString
from shapely.strtree import STRtree
from scipy.ndimage import map_coordinates
from PIL import Image
from pyproj import Transformer

ROOT = Path(__file__).resolve().parents[2]
ENV = ROOT / 'QuestFlight/Assets/Production/Environment'

def read_mesh(path):
    s = path.read_text()
    raw = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-fA-F]+)', s)[1])
    v = np.frombuffer(raw, '<f4').reshape(-1, 6)[:, :3].astype(float)
    typ = '<u4' if int(re.search(r'm_IndexFormat: (\d+)', s)[1]) else '<u2'
    ix = np.frombuffer(bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-fA-F]+)', s)[1]), typ).reshape(-1, 3)
    return v, ix

def build(baseline, output):
    v, faces = read_mesh(baseline)
    ref = json.loads((ENV/'ReconstructionV2/KBDU_ReferenceContextV2.json').read_text())
    selected = []
    for feature in ref['openstreetmap']['features']:
        tags = feature.get('tags', {})
        if tags.get('name') not in ('Airport Road', 'Airport Boulevard', 'Foothills Parkway'): continue
        if feature['geometry_type'] != 'polyline': continue
        p = np.array(feature['points_q']).reshape(-1, 2) * ref['coordinate_quantization_meters']
        if len(p) < 2 or np.max(np.abs(p)) > 1980: continue
        if tags.get('bridge') == 'yes': continue
        selected.append((LineString(p), 5.0 if tags['name']=='Foothills Parkway' else 4.0))
    strips = []
    for line, half_width in selected:
        # Partition along the road as well as across it. No global terrain refinement.
        n = max(1, int(np.ceil(line.length / 6)))
        for a,b in zip(np.linspace(0,line.length,n+1)[:-1], np.linspace(0,line.length,n+1)[1:]):
            pa=np.array(line.interpolate(a).coords[0]);pb=np.array(line.interpolate(b).coords[0])
            d=pb-pa;d/=np.linalg.norm(d);r=np.array([d[1],-d[0]])*half_width
            strips.append(Polygon([pa-r,pb-r,pb+r,pa+r]))
    tree=STRtree(strips)
    dtm=np.asarray(Image.open(ENV/'AerialSceneryV1/Lidar/KBDU_Lidar_DTM_R16.png')).astype(float)*.05+1000-1611.7824
    tf=Transformer.from_crs('+proj=aeqd +lat_0=40.039365270000 +lon_0=-105.226089580000 +datum=WGS84 +units=m +no_defs',32613,always_xy=True)
    vertices=v.tolist(); lookup={(round(p[0],5),round(p[2],5)):i for i,p in enumerate(v)}
    result=[];changed=0
    def add(p, original):
        key=(round(p[0],5),round(p[1],5))
        if key in lookup:return lookup[key]
        # New points on original triangle edges must agree from either side.
        # DTM sampling is deterministic for the shared coordinate.
        x,z=key;e,n=tf.transform(x,z);h=float(map_coordinates(dtm,[[(2046-(n-4432150.874940165))/4],[(e-480712.1393769088+2046)/4]],order=1)[0])
        i=len(vertices);vertices.append([x,h,z]);lookup[key]=i;return i
    for face in faces:
        tri=Polygon(v[face][:,[0,2]])
        ids=tree.query(tri,predicate='intersects')
        if not len(ids):result.append(face.tolist());continue
        parts=[tri]
        for sid in ids:
            split=[]
            for part in parts:
                for cut in (part.intersection(strips[sid]),part.difference(strips[sid])):
                    split.extend([g for g in shapely.get_parts(cut) if g.geom_type=='Polygon' and g.area>1e-8])
            parts=split
        changed+=1
        for part in parts:
            for t in shapely.get_parts(shapely.constrained_delaunay_triangles(part)):
                pts=list(t.exterior.coords)[:3];indices=[add(p,face) for p in pts]
                a,b,c=[np.array(vertices[i]) for i in indices]
                if np.cross(b-a,c-a)[1]<0:indices[1],indices[2]=indices[2],indices[1]
                if len(set(indices))==3:result.append(indices)
    vs=np.array(vertices);ix=np.array(result)
    # GEOS can retain different collinear nodes on neighboring polygon rings.
    # Insert every shared boundary node on both triangles before serialization.
    for _ in range(5):
        edges,counts=np.unique(np.sort(np.concatenate([ix[:,[0,1]],ix[:,[1,2]],ix[:,[2,0]]]),axis=1),axis=0,return_counts=True)
        boundary=edges[counts==1]; interior=boundary[np.max(np.abs(vs[boundary][:,:,[0,2]]),axis=(1,2))<2047.9]
        if not len(interior): break
        candidates=np.unique(interior);points=vs[candidates][:,[0,2]];changed_faces=[]
        lookup_edges={tuple(e) for e in interior};newfaces=[]
        for face in ix:
            ring=[]
            for ia,ib in zip(face,np.roll(face,-1)):
                ring.append(int(ia))
                if tuple(sorted((ia,ib))) not in lookup_edges:continue
                a=vs[ia,[0,2]];b=vs[ib,[0,2]];d=b-a;length2=np.dot(d,d)
                if length2<1e-10:continue
                f=((points-a)@d)/length2;delta=np.linalg.norm(points-(a+f[:,None]*d),axis=1)
                chosen=np.where((f>1e-6)&(f<1-1e-6)&(delta<.00005))[0]
                ring.extend(candidates[chosen[np.argsort(f[chosen])]].tolist())
            if len(ring)==3:newfaces.append(face.tolist());continue
            poly=Polygon(vs[ring][:,[0,2]])
            for t in shapely.get_parts(shapely.constrained_delaunay_triangles(poly)):
                ids=[lookup[(round(x,5),round(z,5))] for x,z in list(t.exterior.coords)[:3]]
                a,b,c=vs[ids]
                if np.cross(b-a,c-a)[1]<0:ids[1],ids[2]=ids[2],ids[1]
                newfaces.append(ids)
        ix=np.array(newfaces)
    else: raise RuntimeError('Unwelded road patch edges remain')
    output.mkdir(parents=True,exist_ok=True)
    payload={'vertices':vs.reshape(-1).tolist(),'triangles':ix.reshape(-1).tolist()}
    (output/'terrain_payload.json').write_text(json.dumps(payload,separators=(',',':')))
    report={'source_sha256':hashlib.sha256(baseline.read_bytes()).hexdigest(),'source_triangles':len(faces),'triangles':len(ix),'changed_original_triangles':changed,'roads':len(selected),'source':'USGS public-domain lidar DTM; existing OSM ODbL road context','method':'local pavement-edge and 6m station constrained subdivision'}
    (output/'terrain_report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--baseline',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();build(a.baseline,a.output)
