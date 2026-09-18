"""Ground accepted foliage and add approximate stand forms, without new masks.

Input terrain samples are exported by Unity from the actual authored surface.
The existing binary layouts/IDs remain stable; previously ignored seed/source
bits carry a two-bit visual family (0 broadleaf, 1 open pine, 2 tapered fir).
"""
import argparse, hashlib, json, struct
from pathlib import Path
import numpy as np
from scipy.spatial import cKDTree

TREE=np.dtype([('x','<u2'),('z','<u2'),('y','<i4'),('h','<u2'),('c','<u2'),
               ('rot','<u2'),('rgb','u1',(3,)),('seed','u1'),('kind','u1')])
CELL=np.dtype([('cell','<u2'),('radius','<u2'),('ground','<f4'),('height','<f4'),
               ('rgb','u1',(3,)),('source','u1'),('grove','<u4')])

def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()

def families(x,z,ground,north):
    # Broad habitat transitions, not exact species claims. Keep eastern urban
    # trees broadleaf. Low foothill valley floors remain predominantly broadleaf.
    west=np.clip((-x-3800)/1500,0,1)
    rise=np.clip((ground-90)/170,0,1)
    stand=(np.sin(x*.0031+z*.0019)*np.cos(z*.0037-x*.0008)+1)*.5
    conifer=(west*rise > .24+.48*stand)
    fir=conifer & ((north>.13) | ((ground>470)&(stand>.64)))
    return np.where(fir,2,np.where(conifer,1,0)).astype(np.uint8)

def build(args):
    out=args.output;out.mkdir(parents=True,exist_ok=True)
    manifest=json.loads(args.manifest.read_text(encoding='utf-8-sig'))
    raw=args.trees.read_bytes(); header=struct.unpack_from('<8sIffffIII',raw)
    magic,version,x0,z0,size,q,nx,nz,count=header
    assert magic==b'QFFMVG3\0' and version==3 and len(raw)==40+nx*nz*8+count*19
    table=np.frombuffer(raw,dtype='<u4',count=nx*nz*2,offset=40).reshape(-1,2)
    trees=np.frombuffer(raw,dtype=TREE,offset=40+nx*nz*8).copy()
    page=np.repeat(np.arange(nx*nz),table[:,1]); assert len(page)==count
    x=x0+(page%nx)*size+trees['x']*q; z=z0+(page//nx)*size+trees['z']*q
    terrain=np.fromfile(args.terrain/'tree_roots.bytes','<f4').reshape(-1,2)
    assert len(terrain)==count and np.isfinite(terrain).all()
    family=families(x,z,terrain[:,0],terrain[:,1]);family[trees['kind']>=2]=0
    trees['y']=np.rint(terrain[:,0]*100).astype(np.int32)
    trees['seed']=(trees['seed']&63)|(family<<6)
    tree_out=out/'KBDU_FullMapVegetationHlodV3.bytes'
    tree_out.write_bytes(raw[:40+nx*nz*8]+trees.tobytes())
    keep=(trees['kind']<2)&(trees['h']>=400)
    anchors=np.column_stack((x[keep],z[keep]));t=trees[keep];ground=terrain[keep,0]
    index=cKDTree(anchors); reports=[];totals=np.zeros(3,dtype=np.int64)
    all_deltas=[]
    for ordinal,r in enumerate(manifest['pages']):
        source=args.pages/r['file']; assert sha(source)==r['sha256']
        data=source.read_bytes();cells=np.frombuffer(data,dtype=CELL,offset=16).copy()
        current=np.fromfile(args.terrain/r['file'],'<f4').reshape(-1,2)
        assert len(current)==len(cells) and np.isfinite(current).all()
        xx=-12096+r['column']*672+(cells['cell']%252+.5)*(8/3)
        zz=-12096+r['row']*672+(cells['cell']//252+.5)*(8/3)
        distance,ids=index.query(np.column_stack((xx,zz)),workers=1)
        f=t['seed'][ids]>>6
        # Coherent rounded/pointed crown profiles around retained tree anchors.
        # Edge cells remain; only their vertical composition changes.
        radius=np.maximum(3,t['c'][ids]*.005)
        d=np.clip(distance/radius,0,1)
        rounded=.80+.20*np.sqrt(np.maximum(0,1-d*d))
        pointed=1-.43*d
        top=ground[ids]+t['h'][ids]*.01*np.where(f>0,pointed,rounded)
        all_deltas.append(np.abs(cells['ground']-current[:,0]))
        cells['ground']=current[:,0]
        cells['height']=np.clip(top-current[:,0],4,40)
        cells['source']=(cells['source']&1)|(f<<1)
        totals+=np.bincount(f,minlength=3)
        target=out/r['file'];target.write_bytes(data[:16]+cells.tobytes())
        reports.append(dict(r,sha256=sha(target),bytes=target.stat().st_size))
        if ordinal%144==0: print('Enriched',ordinal+1,'/1296',flush=True)
    manifest.update(pages=reports,treeRealismVersion=1,
        familyEncoding='source bit 0 retains measured-extent flag; bits 1-2: broadleaf/open-pine/tapered-fir',
        familyCounts=totals.tolist(),
        estimatedHeightPolicy='Actual authored terrain bases; approximate rounded or tapered profiles around existing tree anchors. Heights/species are visual estimates.',
        inputBranchManifestSha256=sha(args.manifest),treeDatabaseSha256=sha(tree_out),
        treeRealismCompiler='tools/full_map_vegetation/enrich_tree_realism.py',
        acceptance='GENERATED_NOT_VISUAL_OR_QUEST_PERFORMANCE_ACCEPTANCE')
    (out/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    delta=np.concatenate(all_deltas)
    report=dict(cells=int(len(delta)),familyCells=totals.tolist(),treeFamilies=np.bincount(family,minlength=3).tolist(),
        baseCorrectionMeters=dict(mean=float(delta.mean()),p95=float(np.percentile(delta,95)),maximum=float(delta.max())),
        sourceTreeSha256=sha(args.trees),outputTreeSha256=sha(tree_out),
        sourceManifestSha256=sha(args.manifest),maskCellsAndComponentIdsUnchanged=True,
        triangleCountsAndPageRecordWidthsUnchanged=True)
    (out/'tree_realism_generation.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report),flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser()
    for name in ('manifest','pages','trees','terrain','output'):p.add_argument('--'+name,type=Path,required=True)
    build(p.parse_args())
