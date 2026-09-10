"""Score independent image traces without ever deriving labels from a candidate."""
import argparse
import hashlib
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image,ImageDraw
from scipy.ndimage import binary_dilation,binary_erosion,distance_transform_edt


def trace_mask(trace,n):
    canvas=Image.new('L',(n,n));draw=ImageDraw.Draw(canvas)
    for entry in trace.get('polygons',[]):
        points=[tuple(map(float,p.split(','))) for p in entry.split()]
        draw.polygon(points,fill=255)
    for x,y,rx,ry in trace.get('ellipses',[]):
        draw.ellipse((x-rx,y-ry,x+rx,y+ry),fill=255)
    return np.asarray(canvas)>0


def score(references,candidate,traces,output):
    definition=json.loads(traces.read_text())
    manifest=json.loads((references/'references.json').read_text())
    output.mkdir(parents=True,exist_ok=True)
    results=[];totals={s:dict(tp=0,fp=0,fn=0,tn=0,boundaryClose=0,boundaryTotal=0,unknownPixels=0) for s in ['development','heldout']}
    for record in manifest['crops']:
        identifier=record['id']
        if identifier not in definition['crops']:continue
        entry=definition['crops'][identifier];n=record['pixels']
        reference=trace_mask(entry,n)
        excluded=trace_mask({'polygons':entry.get('ignore',[])},n)
        edge=reference ^ binary_erosion(reference,border_value=1)
        # Crop perimeter is not an annotated physical canopy edge.
        edge[:2,:]=False;edge[-2:,:]=False;edge[:,:2]=False;edge[:,-2:]=False
        uncertain=binary_dilation(edge,iterations=definition['uncertaintyBandPixels'])|excluded
        proposed=np.asarray(Image.open(candidate/f'{identifier}_mask.png'))>0
        coverage=np.asarray(Image.open(candidate/f'{identifier}_coverage.png'))>0
        valid=(~uncertain)&coverage
        pred_edge=proposed ^ binary_erosion(proposed,border_value=1)
        pred_edge[:2,:]=False;pred_edge[-2:,:]=False;pred_edge[:,:2]=False;pred_edge[:,-2:]=False
        distances=distance_transform_edt(~pred_edge)*(record['bounds'][2]-record['bounds'][0])/n
        measured_edge=edge & (~excluded) & coverage
        counts=dict(tp=int((reference&proposed&valid).sum()),fp=int((~reference&proposed&valid).sum()),
                    fn=int((reference&~proposed&valid).sum()),tn=int((~reference&~proposed&valid).sum()),
                    boundaryClose=int(((distances<=1.5)&measured_edge).sum()),boundaryTotal=int(measured_edge.sum()),
                    unknownPixels=int((~coverage).sum()))
        result=dict(id=identifier,split=record['split'],**counts,
                    evaluatedFraction=float(valid.mean()),ambiguousFraction=float(uncertain.mean()))
        for k,v in counts.items():totals[record['split']][k]+=v
        results.append(result)
        photo=np.asarray(Image.open(references/f'{identifier}.png')).copy()
        contours,_=cv2.findContours(reference.astype(np.uint8),cv2.RETR_CCOMP,cv2.CHAIN_APPROX_SIMPLE)
        cv2.drawContours(photo,contours,-1,(255,70,200),1)
        Image.fromarray(photo).save(output/f'{identifier}_independent_trace.png')
    for s,v in totals.items():
        v.update(precision=v['tp']/max(1,v['tp']+v['fp']),recall=v['tp']/max(1,v['tp']+v['fn']),
                 boundaryWithin1p5m=v['boundaryClose']/max(1,v['boundaryTotal']))
    labels_valid=definition.get('annotationStatus')=='INDEPENDENT_TRACES_VISUALLY_VERIFIED'
    complete=len(results)==30 and sum(r['split']=='heldout' for r in results)==18
    metrics_pass=totals['heldout']['precision']>=.95 and totals['heldout']['recall']>=.9 and totals['heldout']['boundaryWithin1p5m']>=.9
    report=dict(schema='questflight.connected-grove-reference-score.v1',
        traceSha256=hashlib.sha256(traces.read_bytes()).hexdigest(),
        annotationMethod=definition['method'],annotatedCrops=len(results),totals=totals,results=results,
        labelQualificationPassed=labels_valid,
        allReferenceLabelsPresent=complete,pixelAndBoundaryThresholdsPass=complete and labels_valid and metrics_pass,
        topologyGate='PENDING_FIXED_BRIDGE_AND_SPLIT_MERGE_AUDIT',
        geometryAuthorizedByGate=False,
        status='REFERENCE_ANNOTATIONS_NOT_QUALIFIED' if not labels_valid else 'INCOMPLETE_ANNOTATIONS' if not complete else 'REQUIRES_TOPOLOGY_AUDIT' if metrics_pass else 'FAILED_PIXEL_OR_BOUNDARY_GATE')
    (output/'scores.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(dict(annotatedCrops=len(results),totals=totals,status=report['status'])),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    for name in ['references','candidate','traces','output']:parser.add_argument('--'+name,type=Path,required=True)
    args=parser.parse_args();score(args.references,args.candidate,args.traces,args.output)
