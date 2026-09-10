"""Compose fixed-size branch clusters inside one inspected measured grove.

This is a bounded test candidate. The independent map-wide accuracy benchmark
is not passed by using the publisher's own extent as a composition constraint.
"""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

import numpy as np
from scipy.ndimage import gaussian_filter
from shapely.geometry import shape, Point

from build_connected_grove_evidence import REPO


def source_records(path,bounds):
    data=path.read_bytes();header=struct.Struct('<8sIffffIII');record=struct.Struct('<HHiHHHBBBBB')
    magic,version,xmin,zmin,size,q,columns,rows,count=header.unpack_from(data)
    if magic!=b'QFFMVG3\0' or version!=3:raise ValueError('Unexpected tree database')
    start=header.size+columns*rows*8
    if len(data)!=start+count*record.size:raise ValueError('Tree database byte count')
    output=[]
    for page in range(columns*rows):
        px=xmin+(page%columns)*size;pz=zmin+(page//columns)*size
        if px>bounds[2] or px+size<bounds[0] or pz>bounds[3] or pz+size<bounds[1]:continue
        offset,length=struct.unpack_from('<II',data,header.size+page*8)
        for index in range(offset,offset+length):
            r=record.unpack_from(data,start+index*record.size);x,z=px+r[0]*q,pz+r[1]*q
            if bounds[0]<=x<=bounds[2] and bounds[1]<=z<=bounds[3] and r[10]<2:
                output.append(dict(id=index,x=x,y=r[2]*.01,z=z,height=r[3]*.01,crown=r[4]*.01,
                    rotation=r[5]*360/65535,color=list(r[6:9])))
    return output


def build(root,output,full_volume=False):
    if output.resolve()==REPO or REPO in output.resolve().parents:raise ValueError('Pilot input remains external')
    output.mkdir(parents=True,exist_ok=True)
    chosen=json.loads((root/'selected_pilot.json').read_text())
    records=json.loads((root/'pilot/pilot_components.json').read_text())
    selected=next(r for r in records if r['id']==chosen['pilotId'])
    polygon=shape(selected['geometry'])
    if selected['touchesSourceEdge']:raise ValueError('Truncated pilot')
    source=np.load(root/'pilot/height_evidence.npz');heights=source['height'];support=source['support']>0
    valid=support&np.isfinite(heights)&(heights>3)
    # Local normalized smoothing uses only measured elevated vegetation; macro
    # holes and the polygon remain hard constraints on all cluster positions.
    weight=gaussian_filter(valid.astype(float),.8)
    smooth=gaussian_filter(np.where(valid,heights,0),.8)/np.maximum(weight,1e-8)
    xmin,zmin,xmax,zmax=polygon.bounds
    def height(x,z):
        c=int(math.floor(x-float(source['xmin'])));r=int(math.floor(float(source['zmax'])-z))
        h=float(smooth[r,c])
        if not math.isfinite(h) or h<3:raise ValueError('Missing measured canopy support')
        return h
    rng=np.random.default_rng(int(chosen['pilotId'].split('_')[1][:8],16))
    clusters=[]
    # The first pilot had just one persistent upper layer. Fill a shared crown
    # volume with a second persistent layer; reserve only the lowest infill for near.
    layers=[(0,2.25,'upper'),(0,2.5,'middle'),(1,3.2,'lower')] if full_volume else [(0,2.4,'upper'),(1,3.7,'lower')]
    broadleaf_cells=[1,3,6,8,9,11,12,14,15]
    for tier,spacing,layer in layers:
        for x0 in np.arange(xmin+.6,xmax,spacing):
            for z0 in np.arange(zmin+.6,zmax,spacing):
                x,z=x0+float(rng.uniform(-.7,.7)),z0+float(rng.uniform(-.7,.7))
                point=Point(x,z)
                if not polygon.contains(point):continue
                edge=float(polygon.boundary.distance(point))
                if edge<.55:continue
                top=height(x,z)
                radius=min(2.25,edge*.7)
                if full_volume and layer=='upper':
                    y=max(top*.6,top-radius*.75-float(rng.uniform(0,.5)))
                elif full_volume and layer=='middle':
                    y=top*float(rng.uniform(.65,.82))
                elif full_volume:
                    y=top*float(rng.uniform(.46,.64))
                elif tier==0:
                    y=max(top*.52,top-1.8)
                else:
                    y=top*float(rng.uniform(.52,.74))
                cluster=dict(x=float(x),z=float(z),height=y,measuredTop=top,
                    radius=radius,angle=float(rng.uniform(0,360)),cell=int(rng.integers(0,16)),tier=tier,
                    tilt=float(rng.uniform(-12,12)))
                if full_volume:
                    cluster['cells']=[int(rng.choice(broadleaf_cells)) for _ in range(3)]
                    cluster['layer']=layer
                clusters.append(cluster)
    database=REPO/'QuestFlight/Assets/Production/Environment/FullMapVegetationV3/KBDU_FullMapVegetationHlodV3.bytes'
    owned=[];partial=[]
    for r in source_records(database,[xmin-20,zmin-20,xmax+20,zmax+20]):
        footprint=Point(r['x'],r['z']).buffer(r['crown']*.5)
        overlap=polygon.intersection(footprint).area/footprint.area
        if overlap>=.70 and polygon.contains(Point(r['x'],r['z'])):owned.append(r)
        elif overlap>.05:partial.append(dict(**r,overlap=overlap))
    if not owned:raise ValueError('Pilot would replace no existing crowns')
    manifest=dict(schema='questflight.connected-grove-pilot.v1',scope='ONE_INSPECTED_GROVE_TEST_ONLY',
        mapWideAccuracyGate='UNRESOLVED_NOT_BYPASSED_BY_THIS_PILOT',id=chosen['pilotId'],
        sourceExtentSha256=hashlib.sha256((root/'pilot/pilot_components.json').read_bytes()).hexdigest(),
        sourceHeightsSha256=hashlib.sha256((root/'pilot/height_evidence.npz').read_bytes()).hexdigest(),
        treeDatabaseSha256=hashlib.sha256(database.read_bytes()).hexdigest(),
        polygon=selected['geometry'],clusters=clusters,owned=owned,retainedBoundaryTrees=partial,fullVolume=full_volume,
        lowerFoliage='Estimated 46–82% of measured top for volume infill; not a measured lower crown surface.' if full_volume else 'Estimated 52–74% of measured top; not a measured lower crown surface.',
        composition='Whole-polygon sampling, fixed branch-scale cards, continuous middle-tier core plus unique near infill; no photographic foliage sheets.',
        attribution='City of Boulder / NV5 / DRCOG 2020 LiDAR, CC BY 4.0. Modified for QuestFlight; no endorsement implied.',
        leafArtwork='Existing project NearFoliageAtlas used by primary individual-tree branch cards; source record retained in AirportCloseTreeLods_SourceManifest.json',
        center=dict(x=(xmin+xmax)/2,z=(zmin+zmax)/2))
    (output/'pilot.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    print(json.dumps(dict(coreClusters=sum(c['tier']==0 for c in clusters),nearClusters=sum(c['tier']==1 for c in clusters),ownedTrees=len(owned),retainedBoundaryTrees=len(partial))),flush=True)


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    p.add_argument('--full-volume',action='store_true')
    a=p.parse_args();build(a.root,a.output,a.full_volume)
