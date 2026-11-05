"""Stage only successfully built matching bridge inputs; retain Unity GUIDs."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
from native_harness import ROOT, PACKAGE, Bridge

def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def main():
    p=argparse.ArgumentParser();p.add_argument('build',type=Path);a=p.parse_args()
    report=json.loads((a.build/'jsbsim_native_gate_report.json').read_text(encoding='utf-8-sig'))
    for entry in report['bridge_inputs']:
        assert sha(ROOT/entry['path'])==entry['sha256'],entry['path']
    assert {r['platform'] for r in report['results']}=={'windows-x86_64','android-arm64'}
    for result in report['results']:
        destination=PACKAGE/'Runtime/Plugins'/('Windows/x86_64' if result['platform']=='windows-x86_64' else 'Android/arm64-v8a')
        for item in result['builtLibraries']:
            src=Path(item['path']);assert sha(src)==item['sha256'];shutil.copy2(src,destination/src.name)
    assert Bridge().version==3
    manifest=json.loads((PACKAGE/'SOURCE_MANIFEST.json').read_text())
    manifest['packageVersion']='0.4.0';manifest['abi']['version']=3
    manifest['abi']['aircraftCapabilities']='Explicit engine/tank/station/contact counts; SI external loads, ECEF attachment velocity, air data and conditions'
    for record in manifest['binaries']:
        record['sha256']=sha(PACKAGE/record['path'])
        if 'bytes' in record:record['bytes']=(PACKAGE/record['path']).stat().st_size
    wrapper=manifest['wrapperSource']
    for kind in ['header','implementation']:
        file=ROOT/wrapper[kind+'Path'];wrapper[kind+'BuildInputSha256']=sha(file)
        wrapper[kind+'CanonicalLfSha256']=hashlib.sha256(file.read_bytes().replace(b'\r\n',b'\n')).hexdigest()
    wrapper['identityNote']='Autonomous V2 ABI3; complete native build inputs include aircraft extension; exact file hashes identify uncommitted candidate.'
    manifest['build']['remediationSourceInputs']=report['bridge_inputs']
    manifest['build']['soaringBuildReport']=str(a.build/'jsbsim_native_gate_report.json')
    (PACKAGE/'SOURCE_MANIFEST.json').write_text(json.dumps(manifest,indent=2)+'\n')
    metadata=json.loads((PACKAGE/'package.json').read_text());metadata['version']='0.4.0'
    metadata['description']='Pinned native JSBSim sole flight authority with aircraft-aware loading, external loads and air data.'
    (PACKAGE/'package.json').write_text(json.dumps(metadata,indent=2)+'\n')
    print('Staged matching Windows/Android ABI3 binaries and source provenance; original .meta files retained.')
if __name__=='__main__':main()
