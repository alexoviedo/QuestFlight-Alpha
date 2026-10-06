"""Inspect raw provider EXRs from ProductSpaceWarpCapturePass; not a performance gate.

Requires the project's offline Python image tools (OpenCV, NumPy, Pillow).
Does not alter source images or infer headset acceptance from an image.
"""
import argparse, json, os
from pathlib import Path
os.environ.setdefault('OPENCV_IO_ENABLE_OPENEXR','1')
import cv2
import numpy as np
from PIL import Image

def read_exr(path):
    result=cv2.imread(str(path),cv2.IMREAD_UNCHANGED)
    if result is None and path.exists():
        # Some OpenCV distributions cannot decode Unity's Android EXRs.
        # The native OpenEXR reader retains the original floating-point values.
        import OpenEXR
        with OpenEXR.File(str(path)) as source:
            result=source.channels()['RGBA'].pixels.astype(np.float32)[:,:,[2,1,0,3]]
    return result

def inspect(directory):
    report={'schema':'questflight.spacewarp.attachment-inspection.v1','eyes':{},'hardwareAcceptance':False}
    metadata_path=directory/'metadata.json'
    metadata=json.loads(metadata_path.read_text(encoding='utf-8-sig')) if metadata_path.exists() else None
    for eye in ('left','right'):
        motion=read_exr(directory/f'motion_{eye}.exr')
        depth=read_exr(directory/f'depth_{eye}.exr')
        if motion is None or depth is None:raise ValueError(f'Missing {eye} provider attachments')
        if motion.shape!=depth.shape or motion.shape[-1]!=4:raise ValueError('Attachment dimensions/channels differ')
        if not np.isfinite(motion).all() or not np.isfinite(depth).all():raise ValueError('Non-finite provider data')
        rgb=motion[:,:,[2,1,0]]
        report['eyes'][eye]={'width':motion.shape[1],'height':motion.shape[0],
            'motionMin':rgb.min(axis=(0,1)).tolist(),'motionMax':rgb.max(axis=(0,1)).tolist(),
            'coveredFraction':float((motion[:,:,3]>.5).mean()),
            'rawDepthMin':float(depth[:,:,0].min()),'rawDepthMax':float(depth[:,:,0].max())}
        if metadata and metadata.get('resetMotion',1)==0:
            # Provider attachment rows correspond to NDC Y=-1..1. Do not
            # substitute the color intermediate's opposite Y convention.
            index=0 if eye=='left' else 1
            matrix=lambda item:np.array([[item[f'e{i}{j}'] for j in range(4)] for i in range(4)])
            current=matrix(metadata['current'][index]);previous=matrix(metadata['previous'][index])
            height,width=depth.shape[:2];yy,xx=np.mgrid[:height,:width]
            clip=np.stack([(xx+.5)/width*2-1,(yy+.5)/height*2-1,depth[:,:,0],np.ones_like(xx)],axis=-1)
            old=clip@np.linalg.inv(current).T@previous.T
            expected=clip[:,:,:3]-old[:,:,:3]/old[:,:,3:4]
            if metadata.get('rightHanded'):expected[:,:,1]*=-1
            sky=(motion[:,:,3]<.1)&(depth[:,:,0]==0)
            if sky.any():
                error=np.max(np.abs(rgb-expected),axis=2)[sky]
                report['eyes'][eye]['skyMotionReconstruction']={
                    'pixels':int(sky.sum()),'p50ErrorNdc':float(np.percentile(error,50)),
                    'p99ErrorNdc':float(np.percentile(error,99)),
                    'maxErrorNdc':float(error.max()),'withinNumericTolerance':bool(error.max()<.001),
                    'scope':'Raw provider background motion only; stationary poses cannot establish head-turn acceptance'}
        color_path=directory/f'color_{eye}.exr'
        if color_path.exists():
            color=read_exr(color_path)
            expected_size=(metadata.get('colorHeight',motion.shape[0]),metadata.get('colorWidth',motion.shape[1])) if metadata else motion.shape[:2]
            if color is None or color.shape[:2]!=expected_size or color.shape[-1]!=4 or not np.isfinite(color).all():
                raise ValueError(f'Invalid matching {eye} color attachment')
            linear=np.clip(color[:,:,[2,1,0]],0,1)
            srgb=np.where(linear<=.0031308,linear*12.92,1.055*np.power(linear,1/2.4)-.055)
            Image.fromarray(np.rint(srgb*255).astype('uint8')).save(directory/f'color_{eye}_preview.png')
            report['eyes'][eye]['matchingColorPresent']=True
        # Neutral gray = zero motion; signed XYZ are RGB, 8x inspection scale.
        Image.fromarray((np.clip(.5+rgb*8,0,1)*255).astype('uint8')).save(directory/f'motion_{eye}_preview.png')
        raw=depth[:,:,0];low,high=np.percentile(raw,[1,99])
        preview=np.clip((raw-low)/max(float(high-low),1e-7),0,1)
        Image.fromarray((preview*255).astype('uint8')).save(directory/f'depth_{eye}_preview.png')
    (directory/'attachment_inspection.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    return report

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('directory',type=Path)
    print(json.dumps(inspect(parser.parse_args().directory),indent=2))
