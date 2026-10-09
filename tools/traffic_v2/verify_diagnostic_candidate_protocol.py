"""Invoke the exact registry argv and actual result consumer in a local fixture.

Only the external test repository's machine witnesses are written. Synthetic
roads/images cannot establish shipping-source or authentic human approval.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import sys


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work-dir',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    work=args.work_dir.resolve()
    repo=work/'repository'
    path=repo/'tools/traffic_v2/geospatial/source_gate.py'
    spec=importlib.util.spec_from_file_location('diagnostic_candidate_protocol',path)
    gate=importlib.util.module_from_spec(spec)
    sys.modules[spec.name]=gate
    spec.loader.exec_module(gate)
    registry_path=repo/'tools/traffic_v2/geospatial/source_registry.json'
    registry=gate.load_registry(registry_path)
    assert registry.get('diagnostic_scope')=='LOCAL_SYNTHETIC_COMPILER_REGRESSION_ONLY_NOT_SOURCE_OR_HUMAN_APPROVAL'
    outcomes=[]
    for name in ('empty','one-road','conflicting-lanes'):
        external=work/name
        candidate=gate.read_json_object(external/'approval_candidate/candidate_manifest.json','Diagnostic candidate',require_canonical=True)
        coverage=gate.read_json_object(external/'approval_candidate/traffic_source_coverage.json','Diagnostic coverage',require_canonical=True)
        context=gate.build_approval_validation_context(registry,registry_path,external,coverage['benchmark_camera_coverage'],candidate['source_freeze_sha256'],candidate)
        process=subprocess.run(context['reproduction']['argv'],cwd=context['reproduction']['cwd'],capture_output=True,text=True)
        (external/'registered_verifier_stdout.txt').write_text(process.stdout)
        (external/'registered_verifier_stderr.txt').write_text(process.stderr)
        result=json.loads(process.stdout)
        assert set(result)=={'bindings','checks','decision','reproduction','schema','status'}
        witnesses=registry['authority_model']['benchmark_approval_witnesses']
        report=gate.repository_file_record(repo/witnesses['d_report']['path'])
        verifier=gate.repository_file_record(repo/witnesses['d_verifier']['path'])
        (external/'registered_verification.md').write_bytes((repo/witnesses['d_report']['path']).read_bytes())
        (external/'registered_verification.json').write_bytes((repo/witnesses['d_result']['path']).read_bytes())
        rejected=None
        try:
            gate.validate_d_result_semantics(registry,result,context=context,verifier=verifier,report=report)
        except gate.GateError as error:
            rejected=str(error)
        if name=='conflicting-lanes':
            assert process.returncode==2 and result['decision']=='FAIL_CLOSED' and rejected
        else:
            assert process.returncode==0 and result['decision']=='PASS' and not rejected
        outcomes.append({'fixture':name,'exact_gate_generated_argv':context['reproduction']['argv'],'exit_code':process.returncode,'machine_result':result,'actual_consumer_status':'REJECTED' if rejected else 'PASS','consumer_reason':rejected,'human_record_exists':(repo/witnesses['lead_inspection']['path']).exists()})
        assert not outcomes[-1]['human_record_exists'],'Diagnostic generated or copied a human witness'
    value={'scope':registry['diagnostic_scope'],'source_tool_sha256':gate.sha256_file(path),'diagnostic_registry_sha256':gate.sha256_file(registry_path),'outcomes':outcomes}
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(value,indent=2)+'\n')
    print(json.dumps(value,indent=2))
    return 0

if __name__=='__main__':raise SystemExit(main())
