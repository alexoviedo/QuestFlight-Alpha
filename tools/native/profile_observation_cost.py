"""Paired native observation CPU/call evidence; never a Quest performance claim.

Reuses the audited native wind diagnostic's header-derived ctypes layouts.
Neither branch changes control/flight authority: both advance the same native
model, one reads bounded state and the other also requests diagnostic outputs.
The compact observation branch is measurement only; product finite diagnostic
checks and native fuel/flap authority are deliberately unchanged.
"""
import argparse
import ctypes as c
import hashlib
import json
import math
import os
from pathlib import Path
import re
import time

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--warmup-seconds',type=int,default=90)
    parser.add_argument('--measure-seconds',type=int,default=60)
    args=parser.parse_args()
    if args.warmup_seconds<1 or args.measure_seconds<60: raise ValueError('Require warmed paired >=60s simulation workload')
    repo=Path(__file__).resolve().parents[2]
    package=repo/'QuestFlight/Packages/com.alexoviedo.questflight.jsbsim'
    plugins=package/'Runtime/Plugins/Windows/x86_64'
    cookie=os.add_dll_directory(str(plugins))
    lib=c.CDLL(str(plugins/'qfl_jsbsim_native.dll'))
    header=re.sub(r'/\*.*?\*/','',(repo/'tools/native/jsbsim_bridge/include/qfl_jsbsim_native.h').read_text(),flags=re.S)
    types={}
    for name,body in re.findall(r'typedef struct (\w+) \{(.*?)\}',header,re.S):
        fields=[]
        for kind,names in re.findall(r'\b(double|int)\s+([^;]+);',body):
            fields.extend((n.strip(),c.c_double if kind=='double' else c.c_int) for n in names.split(','))
        types[name]=type(name,(c.Structure,),{'_fields_':fields})
    def bind(name,arguments,result=c.c_int):
        fn=getattr(lib,'qfl_jsbsim_'+name);fn.argtypes=arguments;fn.restype=result;return fn
    create=bind('create',[],c.c_void_p);destroy=bind('destroy',[c.c_void_p],None)
    error=bind('last_error',[c.c_void_p],c.c_char_p)
    load=bind('load_aircraft',[c.c_void_p,c.c_char_p,c.c_char_p])
    advance=bind('advance',[c.c_void_p,c.c_double])
    reset=bind('reset',[c.c_void_p,c.POINTER(types['qfl_jsbsim_initial_conditions'])])
    controls=bind('set_controls',[c.c_void_p,c.POINTER(types['qfl_jsbsim_controls'])])
    getstate=bind('get_state',[c.c_void_p,c.POINTER(types['qfl_jsbsim_state'])])
    diagnostics=bind('get_diagnostics',[c.c_void_p,c.POINTER(types['qfl_jsbsim_diagnostics'])])
    def check(value,handle):
        if not value: raise RuntimeError(error(handle))
    rows=[]
    for with_diagnostics in (True,False,False,True):
        handle=create()
        try:
            check(load(handle,str(package/'RuntimeData/JSBSim-1.3.1').encode(),b'c172x'),handle)
            initial=types['qfl_jsbsim_initial_conditions'](40.03936527,-105.22608958,5292.1,5288,0,80,0,0,0,0)
            command=types['qfl_jsbsim_controls'](0,0,0,0,1,0,0,0,1,1)
            check(reset(handle,c.byref(initial)),handle);check(controls(handle,c.byref(command)),handle)
            state=types['qfl_jsbsim_state']();diag=types['qfl_jsbsim_diagnostics']()
            for _ in range(args.warmup_seconds*120):check(advance(handle,1/120),handle)
            observation_ns=0;step_ns=0;diagnostic_ns=0;state_ns=0
            trace=hashlib.sha256();steps=args.measure_seconds*120
            for _ in range(steps):
                start=time.process_time_ns();check(advance(handle,1/120),handle);step_ns+=time.process_time_ns()-start
                start=time.process_time_ns();check(getstate(handle,c.byref(state)),handle);state_ns+=time.process_time_ns()-start
                if with_diagnostics:
                    start=time.process_time_ns();check(diagnostics(handle,c.byref(diag)),handle);diagnostic_ns+=time.process_time_ns()-start
                    if not all(math.isfinite(getattr(diag,name)) for name,_ in diag._fields_):raise ValueError('Nonfinite diagnostic')
                if not all(math.isfinite(getattr(state,name)) for name,_ in state._fields_):raise ValueError('Nonfinite state')
                trace.update(bytes(state))
            rows.append({'diagnostics_requested':with_diagnostics,'steps':steps,'simulated_warmup_seconds':args.warmup_seconds,'simulated_measured_seconds':args.measure_seconds,'native_advance_calls':steps,'native_state_calls':steps,'native_diagnostic_calls':steps if with_diagnostics else 0,'host_process_step_cpu_ns':step_ns,'host_process_state_cpu_ns':state_ns,'host_process_diagnostic_cpu_ns':diagnostic_ns,'state_trace_sha256':trace.hexdigest()})
        finally:destroy(handle)
    inputs={name:hashlib.sha256((plugins/name).read_bytes()).hexdigest() for name in ['qfl_jsbsim_native.dll','JSBSim.dll']}
    result={'scope':'Windows native API2 paired stationary-braked scenario, 90s simulated warmup +60s simulated measurement, four order-balanced passes. Not real-time headset, IL2CPP, native allocation, full product CPU or FPS evidence.','inputs':inputs,'abi_sizes':{name:c.sizeof(value) for name,value in types.items()},'native_allocator_measurement':'UNAVAILABLE_NO_ALLOCATOR_INSTRUMENTATION','rows':rows,'state_traces_equal':len({row['state_trace_sha256'] for row in rows})==1,'product_optimization_applied':False,'remaining':'Physical exactAPK paired tracing/diagnostics/menu workload required before ABI/snapshot performance optimization.'}
    args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'state_traces_equal':result['state_traces_equal'],'inputs':inputs,'rows':rows}))
    return 0 if result['state_traces_equal'] else 1

if __name__=='__main__':raise SystemExit(main())
