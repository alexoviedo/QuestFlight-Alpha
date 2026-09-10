"""Compile geographically registered angular middle-crown instances; no Unity writes.

Source RGB remains in the existing aerial stream. Each page stores only estimated
height/depth and an 8x8 authoritative exclusion mask per 2.667 m patch.
"""
from pathlib import Path
import argparse, hashlib, json, struct
import cv2
import numpy as np
from scipy.spatial import cKDTree
from build_full_map_vegetation import BuildingIndex, canopy_mask, rasterize_buildings

REPO = Path(__file__).resolve().parents[2]
VG = REPO/'QuestFlight/Assets/Production/Environment/FullMapVegetationV3'
HEADER = struct.Struct('<8sIffffIII')
RECORD = np.dtype([('cell','<u2'),('middle','<f4'),('depth','<f4'),('mask','<u2',(4,))])
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()

def read_trees():
    p=VG/'KBDU_FullMapVegetationHlodV3.bytes';raw=p.read_bytes()
    magic,ver,x0,z0,size,q,nx,nz,count=HEADER.unpack_from(raw)
    if magic!=b'QFFMVG3\0' or ver!=3 or len(raw)!=40+nx*nz*8+count*19: raise ValueError('Invalid V3 trees')
    dtype=np.dtype([('x','<u2'),('z','<u2'),('y','<i4'),('h','<u2'),('c','<u2'),('rot','<u2'),('rgb','u1',(3,)),('seed','u1'),('kind','u1')])
    records=np.frombuffer(raw,dtype=dtype,offset=40+nx*nz*8)
    table=np.frombuffer(raw,dtype='<u4',count=nx*nz*2,offset=40).reshape(-1,2)
    page=np.repeat(np.arange(nx*nz),table[:,1])
    keep=(records['kind']<2)&(records['h']>=400)
    xy=np.stack([x0+(page%nx)*size+records['x']*q,z0+(page//nx)*size+records['z']*q],axis=1)[keep]
    return xy,records[keep],p

def pack_masks(mask):
    # Input is bottom-up, native 1/3 m texels. Four 16-bit words are exactly
    # representable by Unity's instanced float4, including on mobile GPUs.
    bits=mask.reshape(252,8,252,8).transpose(0,2,1,3).reshape(-1,64)
    words=np.zeros((252*252,4),dtype=np.uint16)
    for bit in range(64):words[:,bit//16]|=bits[:,bit].astype(np.uint16)<<(bit%16)
    return words

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--imagery-root',type=Path,required=True);parser.add_argument('--limit',type=int,default=0)
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    manifest_path=REPO/'QuestFlight/Assets/StreamingAssets/QuestFlightAerialClipmap/clipmap_manifest.json'
    manifest=json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    pages=sorted([p for p in manifest['pages'] if p['tier']=='high'],key=lambda p:(p['row'],p['column']))
    if args.limit:pages=pages[:args.limit]
    xy,trees,tree_path=read_trees();index=cKDTree(xy)
    building_path=REPO/'QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingDatabaseV1.json'
    buildings=BuildingIndex(building_path)
    exclusion_path=VG/'KBDU_VegetationSemanticExclusionV1.png'
    exclusions=cv2.imread(str(exclusion_path),cv2.IMREAD_UNCHANGED)
    if exclusions is None:raise ValueError('Missing semantic exclusions')
    if exclusions.ndim!=3:raise ValueError('Expected RGB semantic exclusions')
    exclusions=(exclusions[:,:,2]>=128)|(exclusions[:,:,1]>=128)
    results=[]
    for ordinal,p in enumerate(pages):
        source=args.imagery_root/(p['asset_name']+'.jpg')
        actual_sha=sha(source)
        known_recovery=(p['asset_name']=='KBDU_2020_high_016_016' and actual_sha=='f00cb1201f79cb41a211199c737110d8e1b26b7e3116e09958e57dea565a713d')
        if actual_sha!=p['sha256'] and not known_recovery:raise ValueError('Source hash mismatch: '+str(source))
        image=cv2.imread(str(source))[16:-16,16:-16];small=cv2.resize(image,(1008,1008),interpolation=cv2.INTER_AREA)
        x0=-12096+p['column']*672;z0=-12096+p['row']*672
        path=args.output/f"grove_{p['column']:02}_{p['row']:02}.bytes"
        building_mask=rasterize_buildings(buildings,x0,z0,x0+672,z0+672,(1008,1008))
        classified,_=canopy_mask(small,building_mask)
        mask=cv2.resize(classified,(2016,2016),interpolation=cv2.INTER_NEAREST)>0
        rgb=image[:,:,::-1].astype(np.float32)/255
        mask&=~((rgb.max(axis=2)>.45)&(2*rgb[:,:,1]-rgb[:,:,0]-rgb[:,:,2]<.05))
        # Native masks remain spatial, never recreated from coarse runtime RGB.
        wx=x0+(np.arange(2016)+.5)/3;wz=z0+672-(np.arange(2016)+.5)/3
        ex=np.clip(np.rint((wx+12000)/24000*(exclusions.shape[1]-1)).astype(int),0,exclusions.shape[1]-1)
        ez=np.clip(np.rint((12000-wz)/24000*(exclusions.shape[0]-1)).astype(int),0,exclusions.shape[0]-1)
        mask&=exclusions[ez[:,None],ex[None,:]]==0
        mask&=(wx[None,:]>=-12000)&(wx[None,:]<12000)&(wz[:,None]>=-12000)&(wz[:,None]<12000)
        words=pack_masks(mask[::-1]);cells=np.flatnonzero(words.any(axis=1))
        centers=np.stack([x0+(cells%252+.5)*(8/3),z0+(cells//252+.5)*(8/3)],axis=1)
        distance,ids=index.query(centers,k=8,workers=1)
        radii=np.maximum(2,trees['c'][ids]*.005);d2=(distance/radii)**2
        supported=(d2.min(axis=1)<=1.7)
        weights=np.exp(-d2*.7);den=weights.sum(axis=1)
        h=trees['h'][ids]*.01;ground=trees['y'][ids]*.01
        lower=(weights*(ground+h*.48)).sum(axis=1)/np.maximum(den,1e-12)
        upper=(weights*(ground+h*(.70+.20*np.exp(-d2)))).sum(axis=1)/np.maximum(den,1e-12)
        records=np.zeros(int(supported.sum()),dtype=RECORD)
        records['cell']=cells[supported];records['middle']=((lower+upper)*.5)[supported]
        records['depth']=((upper-lower)*.35)[supported];records['mask']=words[cells[supported]]
        payload=struct.pack('<4sIii',b'QFAG',1,p['column'],p['row'])+records.tobytes()
        path.write_bytes(payload)
        results.append(dict(column=p['column'],row=p['row'],file=path.name,count=len(records),bytes=len(payload),sha256=sha(path),sourceSha256=actual_sha,imageryManifestSha256=p['sha256'],sourceException='Documented 016_016 recovered JPEG; original Android terrain bundle retained unchanged' if known_recovery else ''))
        if ordinal%20==0: print(f'{ordinal+1}/{len(pages)} pages; {sum(r["count"] for r in results):,} patches',flush=True)
    report=dict(schema='questflight.angular-groves.v1',complete=len(results)==1296,recordBytes=RECORD.itemsize,gridMinimum=-12096,pageMeters=672,patchMeters=8/3,
                pages=results,totalPatches=sum(r['count'] for r in results),totalBytes=sum(r['bytes'] for r in results),
                sources=dict(imageryManifestSha256=sha(manifest_path),placementSha256=sha(tree_path),buildingSha256=sha(building_path),semanticExclusionSha256=sha(exclusion_path)),
                limits=dict(maxResidentPages=9,maxSelectedInstances=8192,maxDraws=1,fullDetailRadiusMeters=96,zeroCoverageRadiusMeters=120),
                provenance='City of Boulder 2020 orthophoto CC0 per tools/aerial_clipmap/config.json; existing V3 estimated heights. Derivative masks/height records only; no raw RGB duplication. Leaf artwork keeps existing project source record.')
    (args.output/'manifest.json').write_text(json.dumps(report,indent=2))
    print(json.dumps({k:report[k] for k in ['complete','totalPatches','totalBytes']}),flush=True)
if __name__=='__main__':main()
