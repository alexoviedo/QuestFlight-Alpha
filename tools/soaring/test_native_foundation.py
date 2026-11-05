"""Actual native ABI/forces/isolation and unchanged C172 regression evidence.

Fixtures stay external and are not aircraft fidelity claims.
"""
import argparse
import ctypes as C
import hashlib
import json
import math
from pathlib import Path
import shutil
import xml.etree.ElementTree as E
from native_harness import Bridge, Session, TYPES, values, DATA

def main():
    p=argparse.ArgumentParser();p.add_argument('--library',type=Path,required=True);p.add_argument('--dependency',type=Path)
    p.add_argument('--source',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    p.add_argument('--baseline',type=Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
    b=Bridge(a.library,a.dependency); checks={};evidence={}
    fixture=a.output/'fixture-data';fixture.mkdir(exist_ok=True)
    r=E.parse(a.source/'aircraft/sgs233/sgs233.xml').getroot();r.set('name','qfl_sgs_fixture')
    fcs=r.find('flight_control');E.SubElement(fcs,'property',{'value':'1'}).text='qfl/capabilities/airbrake'
    # Fixture bounds are test-only, deliberately broad; no fidelity claim.
    for key,value in [('min-weight-lb',610),('max-weight-lb',2000),('min-cg-x-in',0),('max-cg-x-in',200)]:
        E.SubElement(fcs,'property',{'value':str(value)}).text='qfl/loading/'+key
    ext=E.SubElement(r,'external_reactions')
    for name,kind,location in [('qfl_tow','force',(33.54,0,-30)),('qfl_airflow','force',(103.2,0,-6.4)),('qfl_airflow_moment','moment',None)]:
        n=E.SubElement(ext,kind,{'name':name,'frame':'BODY'})
        if location:
            loc=E.SubElement(n,'location',{'unit':'IN'})
            for k,v in zip('xyz',location):E.SubElement(loc,k).text=str(v)
        d=E.SubElement(n,'direction')
        for k,v in zip('xyz',(1,0,0)):E.SubElement(d,k).text=str(v)
    model=fixture/'aircraft/qfl_sgs_fixture/qfl_sgs_fixture.xml';model.parent.mkdir(parents=True,exist_ok=True);E.ElementTree(r).write(model,encoding='utf-8',xml_declaration=True)
    checks['abi3']=b.version==3
    checks['layout']=C.sizeof(TYPES['capabilities_v3'])==32 and C.sizeof(TYPES['attachment_state_v3'])==144 and TYPES['station_load_v3'].pounds.offset==8
    with Session(b,'qfl_sgs_fixture',fixture) as s, Session(b) as c172:
        caps=b.read(s.h,'get_capabilities_v3','capabilities_v3');evidence['glider_capabilities']=values(caps)
        checks['zero_engine_tanks']=caps.engine_count==0 and caps.tank_count==0 and caps.station_count==2 and caps.contact_count==6 and caps.has_airbrake==1
        cc=b.read(c172.h,'get_capabilities_v3','capabilities_v3');checks['c172_no_phantom_airbrake']=cc.has_airbrake==0
        loads=(TYPES['station_load_v3']*2)(TYPES['station_load_v3'](0,170),TYPES['station_load_v3'](1,170))
        b.check(b.loading(s.h,loads,2,None,0),s.h);s.reset()
        d=s.diagnostics();checks['mass_no_phantom_fuel']=abs(d.weight_pounds-950)<1e-6 and d.left_fuel_pounds==0 and d.right_fuel_pounds==0
        bad=TYPES['station_load_v3'](2,170);checks['reject_station']=b.loading(s.h,C.byref(bad),1,None,0)==0
        bad=TYPES['station_load_v3'](0,float('nan'));checks['reject_nan_load']=b.loading(s.h,C.byref(bad),1,None,0)==0
        ic=TYPES['initial_conditions'](40,-105,7288,5288,55,90,0,0,-3,1)
        checks['reject_glider_engine']=b.reset(s.h,C.byref(ic))==0
        controls=TYPES['controls'](0,0,0,1,1,0,0,0,0,0);checks['reject_glider_thrust']=b.set_controls(s.h,C.byref(controls))==0
        c172.reset(engine=1);before=values(c172.state())
        s.send('set_controls',TYPES['controls']());s.send('set_glider_controls_v3',TYPES['glider_controls_v3'](0.6,0.2));s.step()
        checks['session_isolation']=before==values(c172.state())
        s.send('set_conditions_v3',TYPES['conditions_v3'](101325,20));s.step();hot=b.read(s.h,'get_air_data_v3','air_data_v3')
        s.send('set_conditions_v3',TYPES['conditions_v3'](101325,0));s.step();cold=b.read(s.h,'get_air_data_v3','air_data_v3')
        checks['hot_density_lower']=hot.density_kg_m3<cold.density_kg_m3
        checks['airbrake_native_output']=cold.airbrake_position>0
        s.send('set_controls',TYPES['controls']())
        # Paired instantaneous derivatives at the same initialization isolate the
        # externally applied force and moment from aerodynamic/propulsive terms.
        results=[]
        for force in [(0,0,0),(0,100,0),(100,0,0),(0,0,100)]:
            b.check(b.load(s.h,str(fixture).encode(),b'qfl_sgs_fixture'),s.h)
            b.check(b.loading(s.h,loads,2,None,0),s.h)
            s.reset();s.send('set_external_load_v3',TYPES['external_load_v3'](0,*force))
            at=TYPES['attachment_state_v3']();b.check(b.attachment(s.h,0,C.byref(at)),s.h)
            s.step();results.append((values(s.diagnostics()),values(at)))
        base=results[0][0];force_checks=[]
        for force,(diag,at) in zip([(0,100,0),(100,0,0),(0,0,100)],results[1:]):
            r=[at['body_offset_'+axis+'_m'] for axis in 'xyz']
            moment=[r[1]*force[2]-r[2]*force[1],r[2]*force[0]-r[0]*force[2],r[0]*force[1]-r[1]*force[0]]
            # External loads now affect the FIRST integrated state. Its changed
            # air velocity/rates cause real aerodynamic response in that same
            # sample, so isolate the external residual rather than assume the
            # loaded and unloaded aerodynamic totals are still identical.
            def force_residual(d,axis):
                return d['total_force_'+axis+'_pounds']-sum(d[p+'_force_'+axis+'_pounds'] for p in ['aero','propulsion','gear'])
            def moment_residual(d,axis):
                return d['total_'+axis+'_moment_pound_feet']-sum(d[p+'_'+axis+'_moment_pound_feet'] for p in ['aero','propulsion','gear'])
            actual_f=[(force_residual(diag,axis)-force_residual(base,axis))*4.4482216152605 for axis in 'xyz']
            actual_m=[(moment_residual(diag,axis)-moment_residual(base,axis))*1.3558179483314004 for axis in ['roll','pitch','yaw']]
            force_checks.append(dict(applied=force,expected_moment=moment,actual_force=actual_f,actual_moment=actual_m))
        evidence['force_signs']=force_checks
        checks['hook_moment_once']=all(max(abs(x-y) for x,y in zip(v['expected_moment'],v['actual_moment']))<0.05 for v in force_checks)
        checks['force_si']=all(max(abs(x-y) for x,y in zip(v['applied'],v['actual_force']))<0.05 for v in force_checks)
        b.check(b.load(s.h,str(fixture).encode(),b'qfl_sgs_fixture'),s.h)
        b.check(b.loading(s.h,loads,2,None,0),s.h)
        s.reset();s.send('set_external_load_v3',TYPES['external_load_v3'](0,0,100,0));b.check(b.clear_loads(s.h),s.h);s.step()
        checks['clear_force']=abs(s.diagnostics().total_yaw_moment_pound_feet-base['total_yaw_moment_pound_feet'])<1e-7
        s.send('set_external_load_v3',TYPES['external_load_v3'](2,0,150,0))
        for _ in range(25):s.step()
        a0=TYPES['attachment_state_v3']();a1=TYPES['attachment_state_v3']()
        b.check(b.attachment(s.h,0,C.byref(a0)),s.h);s.step();b.check(b.attachment(s.h,0,C.byref(a1)),s.h)
        errors=[abs((getattr(a1,'position_ecef_'+v+'_m')-getattr(a0,'position_ecef_'+v+'_m'))*120-(getattr(a1,'velocity_ecef_'+v+'_mps')+getattr(a0,'velocity_ecef_'+v+'_mps'))/2) for v in 'xyz']
        evidence['attachment_velocity_fd_error_mps']=errors;checks['rotating_hook_velocity']=max(errors)<0.1
    # C172 exact state/force regression against preserved original DLL, same payload.
    old=Bridge(a.baseline)
    deviations=[]
    for bank_input,wind in [(0,(0,0,0)),(.12,(2,-1,-3)),(-.15,(-2,3,1))]:
        with Session(old) as first,Session(b) as second:
            for s in [first,second]:
                s.send('set_mass_configuration',TYPES['mass_configuration'](170,170,0,0,0,130,130));s.reset(engine=1,cas=80,gamma=0)
                s.send('set_controls',TYPES['controls'](bank_input,-.05,.02,.72,1,0,0,.1,0,0));s.send('set_atmosphere',TYPES['atmosphere'](*wind))
            maximum=0
            for i in range(1200):
                first.step();second.step()
                if i%12==0:
                    for lhs,rhs in [(values(first.state()),values(second.state())),(values(first.diagnostics()),values(second.diagnostics()))]:
                        maximum=max(maximum,max(abs(lhs[k]-rhs[k]) for k in lhs))
            deviations.append(maximum)
    evidence['c172_max_absolute_difference']=deviations
    checks['c172_numerical_equivalence']=max(deviations)<1e-9
    result=dict(checks=checks,evidence=evidence,scope='Native software/force correctness; not aircraft fidelity or Quest rendering',library_sha256=hashlib.sha256(a.library.read_bytes()).hexdigest())
    (a.output/'native_foundation.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
    return 0 if all(checks.values()) else 1

if __name__=='__main__':raise SystemExit(main())
