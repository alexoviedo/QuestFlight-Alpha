"""Compile connected map foliage from retained source masks, not photo panels.

Global labels cross page seams. Verified city extents constrain their covered
area; elsewhere the retained image classifier is explicitly a fallback.
"""
import argparse, hashlib, json, struct
from pathlib import Path
import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree
from shapely.geometry import shape, box
from shapely.strtree import STRtree
from rasterio.features import rasterize
from affine import Affine
from build_angular_groves import read_trees, RECORD as OLD_RECORD

REPO=Path(__file__).resolve().parents[2]
N=9072; P=252; STEP=8/3
RECORD=np.dtype([('cell','<u2'),('radius','<u2'),('ground','<f4'),('height','<f4'),('rgb','u1',(3,)),('source','u1'),('grove','<u4')])
def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def build(output,sources,source_masks,source_mask_manifest):
    output.mkdir(parents=True,exist_ok=True)
    # Retained preprocessing evidence lives outside the shipping Unity project.
    oldroot=source_masks
    oldmanifest=source_mask_manifest
    old=json.loads(oldmanifest.read_text()); assert old['complete'] and len(old['pages'])==1296
    assembly=json.loads((sources/'assembly/assembly.json').read_text())
    coverage=shape(assembly['coverage'])
    features=json.loads((sources/'assembly/groves.geojson').read_text())['features']
    polygons=[shape(f['geometry']) for f in features]; spatial=STRtree(polygons)
    extent=np.zeros((N,N),np.uint8); measured_pages={}
    pop=np.array([i.bit_count() for i in range(65536)],np.uint8)
    for ordinal,r in enumerate(old['pages']):
        path=oldroot/r['file']
        if digest(path)!=r['sha256']: raise ValueError('Source integrity: '+str(path))
        a=np.frombuffer(path.read_bytes(),dtype=OLD_RECORD,offset=16)
        tile=np.zeros((P,P),np.uint8); cells=a['cell'][pop[a['mask']].sum(axis=1)>=8]
        tile.flat[cells]=1
        x=-12096+r['column']*672; z=-12096+r['row']*672
        roi=box(x,z,x+672,z+672); affine=Affine(STEP,0,x,0,STEP,z)
        known=np.zeros_like(tile)
        if coverage.intersects(roi):
            known=rasterize([(coverage,1)],out_shape=(P,P),transform=affine,dtype='uint8')
            ids=spatial.query(roi,predicate='intersects')
            measured=rasterize([(polygons[i],1) for i in ids],out_shape=(P,P),transform=affine,dtype='uint8') if len(ids) else np.zeros_like(tile)
            tile[known>0]=measured[known>0]
        measured_pages[(r['column'],r['row'])]=known
        extent[r['row']*P:(r['row']+1)*P,r['column']*P:(r['column']+1)*P]=tile
    # Four-neighbor connectivity never bridges diagonal roads/clearings.
    labels,count=ndimage.label(extent)
    sizes=np.bincount(labels.ravel()); keep=sizes>=9; keep[0]=False
    extent=keep[labels].astype(np.uint8)
    labels[extent==0]=0
    xy,trees,tree_path=read_trees(); tree_index=cKDTree(xy)
    reports=[]; total_measured=0
    for ordinal,r in enumerate(old['pages']):
        col,row=r['column'],r['row']; zslice=slice(row*P,(row+1)*P); xslice=slice(col*P,(col+1)*P)
        tile=extent[zslice,xslice]; cells=np.flatnonzero(tile)
        xx=-12096+col*672+(cells%P+.5)*STEP; zz=-12096+row*672+(cells//P+.5)*STEP
        points=np.column_stack((xx,zz))
        distance,ids=tree_index.query(points,k=4,workers=1)
        known=measured_pages[(col,row)].ravel()[cells]>0
        # No extrapolation across an arbitrarily large empty area. Keep fallback
        # trees there, and report any source gap instead of inventing elevations.
        supported=distance[:,0]<80
        if np.any(~supported):
            cells=cells[supported]; points=points[supported]; distance=distance[supported]; ids=ids[supported]; known=known[supported]
        weights=1/np.maximum(distance,2)**2; weights/=weights.sum(axis=1)[:,None]
        h=(trees['h'][ids]*.01*weights).sum(axis=1)
        ground=(trees['y'][ids]*.01*weights).sum(axis=1)
        rgb=(trees['rgb'][ids]*weights[:,:,None]).sum(axis=1)
        # A padded global neighborhood preserves the same edge radius at seams.
        za=max(0,row*P-3); zb=min(N,(row+1)*P+3); xa=max(0,col*P-3); xb=min(N,(col+1)*P+3)
        edge=ndimage.distance_transform_edt(np.pad(extent[za:zb,xa:xb],1))[1:-1,1:-1]
        local=edge[row*P-za:row*P-za+P,col*P-xa:col*P-xa+P].ravel()[cells]
        out=np.zeros(len(cells),RECORD);out['cell']=cells;out['radius']=np.rint(np.minimum(2.25,np.maximum(.65,(local-.5)*STEP)) *100)
        out['ground']=ground;out['height']=np.clip(h,4,40);out['rgb']=np.clip(rgb,0,255).astype(np.uint8)
        out['source']=known.astype(np.uint8);out['grove']=labels[zslice,xslice].ravel()[cells]
        path=output/f'branches_{col:02}_{row:02}.bytes'
        path.write_bytes(struct.pack('<4sIii',b'QFBG',1,col,row)+out.tobytes())
        total_measured+=int(known.sum())
        reports.append(dict(column=col,row=row,file=path.name,count=len(out),bytes=path.stat().st_size,sha256=digest(path),measuredExtentCells=int(known.sum()),unsupportedHeightCells=int((~supported).sum())))
        if ordinal%100==0: print(f'{ordinal+1}/1296 pages compiled',flush=True)
    manifest=dict(schema='questflight.map-branch-groves.v1',complete=len(reports)==1296,pages=reports,
        recordBytes=20,gridMinimum=-12096,pageMeters=672,cellMeters=STEP,
        totalCells=sum(r['count'] for r in reports),totalBytes=sum(r['bytes'] for r in reports),
        measuredExtentCells=total_measured,components=int(keep.sum()),
        estimatedHeightPolicy='Interpolated accepted V3 tree base/height within 80 m. Heights are estimates, including within measured extent coverage.',
        topology='Global four-neighbor components; minimum 64 m2. No page-local component splitting or dilation. 2.667 m sampling is not exact vector boundary accuracy.',
        sourceMasksSha256=digest(oldmanifest),cityAssemblySha256=digest(sources/'assembly/groves.geojson'),treeDatabaseSha256=digest(tree_path),
        provenance='City of Boulder/NV5/DRCOG 2020 classified extents CC BY 4.0 where covered. Elsewhere retained 2020 orthophoto-derived masks and semantic exclusions, City CC0 per existing source record. Existing tree dimensions/tints and leaf artwork provenance retained. No raw imagery or new textures.',
        limits=dict(maxResidentPages=49,maxSelectedCells=98304,fullCoverageRadiusMeters=1600,zeroCoverageRadiusMeters=1800,maxSubmittedTriangles=393216),
        acceptance='MAP_GENERATION_NOT_QUEST_PERFORMANCE_OR_DETECTION_ACCURACY_ACCEPTANCE')
    (output/'manifest.json').write_text(json.dumps(manifest,indent=2))
    print(json.dumps({k:manifest[k] for k in ['totalCells','measuredExtentCells','components','totalBytes']}),flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--sources',type=Path,required=True)
    p.add_argument('--source-masks',type=Path,required=True)
    p.add_argument('--source-mask-manifest',type=Path,required=True)
    a=p.parse_args();build(a.output,a.sources,a.source_masks,a.source_mask_manifest)
