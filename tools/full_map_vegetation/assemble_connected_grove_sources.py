"""Join publisher topology in its native frame before projection or chunking."""
import argparse
import hashlib
import json
import sys
from pathlib import Path

import cv2
import numpy as np
from affine import Affine
from PIL import Image
from pyproj import CRS, Transformer
from rasterio.features import rasterize
from shapely.geometry import mapping, shape, box, Polygon
from shapely.ops import transform, unary_union
from shapely.strtree import STRtree

from build_connected_grove_evidence import read_extent, polygon_id, LOCAL_CRS, REPO


def assemble(sources, references, output):
    if output.resolve() == REPO or REPO in output.resolve().parents:
        raise ValueError('Derived source review belongs outside repository')
    output.mkdir(parents=True,exist_ok=True)
    all_native=[]; manifests=[]; footprints=[]; frame=None
    for path in sorted(sources.glob('*_sources.json')):
        manifest=json.loads(path.read_text(encoding='utf-8'))
        extent=next(r for r in manifest['products'] if r['semantic']=='extent')
        source=sources/extent['file']
        with source.open('rb') as stream:
            if hashlib.file_digest(stream,'sha256').hexdigest()!=extent['sha256']:
                raise ValueError('Changed source '+source.name)
        polygons,crs,_=read_extent(source,target_crs=None)
        if frame is not None and not frame.equals(crs.to_2d()):
            raise ValueError('Different native frames require an explicit seam transform')
        frame=crs.to_2d();all_native.extend(polygons);manifests.append(manifest)
        index=json.loads((sources/f"{manifest['tile']}_index.json").read_text())
        rings=index['features'][0]['geometry']['rings']
        index_crs=CRS.from_epsg(index['spatialReference'].get('latestWkid',index['spatialReference']['wkid']))
        project=Transformer.from_crs(index_crs,LOCAL_CRS,always_xy=True)
        footprints.append(transform(project.transform,Polygon(rings[0])))
    print(f'Joining {len(all_native)} source components in native CRS',flush=True)
    merged=unary_union(all_native)
    components=list(merged.geoms) if merged.geom_type=='MultiPolygon' else [merged]
    project=Transformer.from_crs(frame,LOCAL_CRS,always_xy=True)
    # The same global ID follows all render sections. IDs describe this immutable
    # input snapshot; expanded source coverage needs an explicit lineage mapping.
    geometries=[transform(project.transform,p) for p in components]
    geometries=[p for p in geometries if p.area>=4]
    coverage=unary_union(footprints)
    features=[dict(type='Feature',geometry=mapping(p),properties=dict(
        id=polygon_id(p),areaMeters2=p.area,holes=len(p.interiors),
        touchesSourceEdge=p.distance(coverage.boundary)<1)) for p in geometries if p.area>=4]
    (output/'groves.geojson').write_text(json.dumps(dict(type='FeatureCollection',
        coordinateReference=LOCAL_CRS.to_wkt(),features=features)),encoding='utf-8')
    refs=json.loads((references/'references.json').read_text())
    tree=STRtree(geometries); reviews=[]
    for record in refs['crops']:
        roi=box(*record['bounds']);selected=[geometries[i] for i in tree.query(roi,predicate='intersects')]
        n=record['pixels'];x0,z0,x1,z1=record['bounds']
        affine=Affine((x1-x0)/n,0,x0,0,-(z1-z0)/n,z1)
        mask=rasterize([(p,1) for p in selected],out_shape=(n,n),transform=affine,dtype='uint8') if selected else np.zeros((n,n),dtype=np.uint8)
        valid=rasterize([(coverage,1)],out_shape=(n,n),transform=affine,dtype='uint8')
        photo=np.asarray(Image.open(references/f"{record['id']}.png").convert('RGB')).copy()
        contours,_=cv2.findContours(mask,cv2.RETR_CCOMP,cv2.CHAIN_APPROX_SIMPLE)
        cv2.drawContours(photo,contours,-1,(20,255,220),1)
        yy,xx=np.indices(valid.shape);photo[(valid==0)&((xx+yy)%12<3)]=(255,75,180)
        Image.fromarray(photo).save(output/f"{record['id']}_overlay.png")
        Image.fromarray(mask*255).save(output/f"{record['id']}_mask.png")
        Image.fromarray(valid*255).save(output/f"{record['id']}_coverage.png")
        reviews.append(dict(id=record['id'],coverageFraction=float(valid.mean()),
            connectedComponents=len(selected),componentIds=[polygon_id(p) for p in selected]))
    report=dict(schema='questflight.connected-grove-source-assembly.v1',
        sources=manifests,sourceCount=len(manifests),nativeComponents=len(all_native),
        joinedComponents=len(components),retainedComponents=len(features),
        removedSourceSeamSplits=len(all_native)-len(components),
        policy='Exact native-coordinate union before projection and render partition; no dilation or tree-circle union.',
        coverage=mapping(coverage),reviews=reviews,
        annotationGate='PENDING_INDEPENDENT_REFERENCE_LABELS')
    (output/'assembly.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({k:report[k] for k in ['sourceCount','nativeComponents','joinedComponents','removedSourceSeamSplits']}),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    for name in ['sources','references','output','deps']:parser.add_argument('--'+name,type=Path,required=True)
    args=parser.parse_args();sys.path.insert(0,str(args.deps))
    assemble(args.sources,args.references,args.output)
