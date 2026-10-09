"""Read-only consistency checks for current package provenance; never grants rights."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
PACKAGE=ROOT/'QuestFlight/Packages/com.alexoviedo.questflight.jsbsim'

def validate(package:Path=PACKAGE) -> dict:
 errors=[]
 m=json.loads((package/'SOURCE_MANIFEST.json').read_text(encoding='utf-8'))
 data=package/'RuntimeData/JSBSim-1.3.1'
 revision=dict(line.split('=',1) for line in (data/'SOURCE_REVISION.txt').read_text(encoding='utf-8').splitlines() if '=' in line)
 model=m['aircraftModel']
 for key,expected in {'revision':m['upstream']['revision'],'aircraft_model_derivative':model['configurationId'],
  'aircraft_model_source_sha256':model['upstreamSha256'],'aircraft_model_changes':'; '.join(model['changes']),
  'aircraft_model_distribution_status':model['distributionStatus']}.items():
  if revision.get(key)!=expected:errors.append('SOURCE_REVISION mismatch: '+key)
 if 'does not grant third-party rights' not in revision.get('aircraft_model_distribution_boundary',''):
  errors.append('SOURCE_REVISION must retain separate third-party rights boundary')
 for record in m['runtimeData']:
  p=data/record['path']
  if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest()!=record['sha256']:
   errors.append('Runtime source record mismatch: '+record['path'])
 for line in (data/'runtime_hashes.sha256').read_text(encoding='utf-8').splitlines():
  expected,name=line.split(None,1);p=data/name
  if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest()!=expected:errors.append('Runtime activation record mismatch: '+name)
 p=package/model['path']
 if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest()!=model['derivativeSha256']:errors.append('Model derivative identity mismatch')
 for record in m['binaries']:
  p=package/record['path']
  if not p.is_file() or p.stat().st_size!=record['bytes'] or hashlib.sha256(p.read_bytes()).hexdigest()!=record['sha256']:
   errors.append('Native binary identity mismatch: '+record['path'])
 wrapper=m['wrapperSource']
 for key in ('header','implementation'):
  p=ROOT/wrapper[key+'Path']
  if hashlib.sha256(p.read_bytes()).hexdigest()!=wrapper[key+'BuildInputSha256']:errors.append('Wrapper source identity mismatch: '+key)
 return {'passed':not errors,'errors':errors,'configurationId':model['configurationId'],'ownerDecision':model['distributionStatus'],
  'rightsAcceptance':'separate release gate; source agreement does not grant third-party rights'}

if __name__=='__main__':
 parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path);args=parser.parse_args();report=validate();payload=json.dumps(report,indent=2)+'\n'
 if args.output:args.output.write_text(payload,encoding='utf-8')
 print(payload,end='');raise SystemExit(0 if report['passed'] else 1)
