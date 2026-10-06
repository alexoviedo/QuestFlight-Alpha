"""Offline evaluation of physical Quest 36/72 evidence; never treats app FPS as display FPS.
Input JSON holds measured frame records and independent compositor/thermal evidence.
Native-72 scripts remain unchanged. Missing or simulated data cannot pass.
"""
import argparse, json, math, re
from pathlib import Path

def percentile(values,p):
    values=sorted(values);return values[min(len(values)-1,math.ceil(p*len(values))-1)] if values else None

def evaluate(data):
    errors=[]
    frames=data.get('frameSamples',[])
    def positive(value):
        return isinstance(value,(int,float)) and math.isfinite(value) and value>0
    valid=[f for f in frames if f.get('frameTimingAvailable') and positive(f.get('cpuFrameMs')) and positive(f.get('gpuFrameMs'))]
    if data.get('deviceModel')!='Quest 3' or data.get('source')!='physical_headset':errors.append('Physical Quest 3 evidence required')
    if not re.fullmatch('[0-9a-fA-F]{64}',str(data.get('apkSha256',''))):errors.append('Exact APK identity required')
    if not positive(data.get('durationSeconds')) or data['durationSeconds']<1200:errors.append('20-minute sustained run required')
    if not frames or len(valid)/len(frames)<.95:errors.append('Insufficient workload timing coverage')
    if len(valid)<1000:errors.append('Insufficient workload sample count')
    if any(not f.get('spaceWarpRequested') or not f.get('spaceWarpEnabled') or not f.get('spaceWarpSubmissionObserved') for f in frames):errors.append('SpaceWarp motion submission not confirmed throughout measured flight')
    cpu=percentile([f['cpuFrameMs'] for f in valid],.95);gpu=percentile([f['gpuFrameMs'] for f in valid],.95)
    if cpu is None or cpu>25 or gpu>25:errors.append('CPU/GPU p95 exceeds 25 ms headroom target')
    rate=data.get('displayRefreshHz');compositor=data.get('compositor',{})
    if rate!=72:errors.append('Display refresh must be measured at 72 Hz')
    if compositor.get('applicationSpaceWarpActive') is not True:errors.append('Independent compositor SpaceWarp activation evidence missing')
    missed=compositor.get('missedFrameRatio',1)
    if not isinstance(missed,(int,float)) or not math.isfinite(missed) or not 0<=missed<.01:errors.append('Missed/repeated display frames must be below 1%')
    cadence=compositor.get('applicationFrameRate')
    if not positive(cadence) or abs(cadence-36)>.5:errors.append('Measured application cadence must be 36 FPS')
    if data.get('thermalThrottling') is not False:errors.append('Thermal acceptance missing or throttling observed')
    if data.get('wornVisualPass') is not True:errors.append('Worn visual acceptance pending')
    return {'schema':'questflight.spacewarp.performance.v1','passed':not errors,'errors':errors,'cpuP95Ms':cpu,'gpuP95Ms':gpu,'applicationDeadlineMs':1000/36,'displayIntervalMs':1000/72,'samples':len(valid)}

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('evidence',type=Path);p.add_argument('--output',type=Path,required=True);args=p.parse_args()
    report=evaluate(json.loads(args.evidence.read_text(encoding='utf-8-sig')));args.output.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(json.dumps(report));raise SystemExit(0 if report['passed'] else 1)
