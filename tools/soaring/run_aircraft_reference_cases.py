"""Actual packaged native models; software checks separated from reference fidelity.

Glide solve balances body force against gravity and pitch moment. It permits
descent, has zero propulsion, and never calls the powered JSBSim trim routine.
"""
import argparse
import ctypes as C
import hashlib
import json
import math
from pathlib import Path
import numpy as np
from scipy.optimize import least_squares
from native_harness import Bridge,Session,TYPES,values,DATA

GLIDER='qfl_sgs233_ref';TUG='qfl_pa18_tug_ref'
def load(s,front=170,rear=170):
    loads=(TYPES['station_load_v3']*2)(TYPES['station_load_v3'](0,front),TYPES['station_load_v3'](1,rear))
    s.b.check(s.b.loading(s.h,loads,2,None,0),s.h)

def solve(b,speed,front=170,rear=170,airbrake=0,altitude=7288):
    def residual(x,save=False):
        alpha,gamma,elevator=x
        with Session(b,GLIDER) as s:
            load(s,front,rear)
            initial=TYPES['initial_conditions'](40.03936527,-105.22608958,altitude,-1000,speed,90,alpha+gamma,0,gamma,0)
            s.send('reset',initial)
            s.send('set_controls',TYPES['controls'](0,elevator,0,0,0,0,0,0,0,0))
            # Airbrake has a native one-second actuator; establish its state
            # before reinitializing kinematics, which intentionally retains it.
            if airbrake:
                s.send('set_glider_controls_v3',TYPES['glider_controls_v3'](airbrake,0))
                for _ in range(125):s.step()
                s.send('reset',initial)
                s.send('set_controls',TYPES['controls'](0,elevator,0,0,0,0,0,0,0,0))
                s.send('set_glider_controls_v3',TYPES['glider_controls_v3'](airbrake,0))
            for _ in range(4):s.step(1e-5)
            d=s.diagnostics();st=s.state();theta=math.radians(alpha+gamma)
            res=[d.total_force_x_pounds/d.weight_pounds-math.sin(theta),
                 d.total_force_z_pounds/d.weight_pounds+math.cos(theta),
                 d.total_pitch_moment_pound_feet/(d.weight_pounds*4.4617)]
            if save:return res,values(st),values(d)
            return res
    fit=least_squares(residual,[4,-3,-.1],bounds=([-5,-25,-1],[22,-.05,1]),xtol=1e-10,ftol=1e-10,gtol=1e-10)
    r,st,d=residual(fit.x,True)
    return dict(cas_knots=speed,load_lb=[front,rear],airbrake=airbrake,alpha_deg=float(fit.x[0]),gamma_deg=float(fit.x[1]),elevator_native=float(fit.x[2]),
                residuals=r,converged=max(abs(v) for v in r)<1e-3,glide_ratio=1/math.tan(math.radians(-float(fit.x[1]))),
                state=st,diagnostics=d)

def initialized_glide(b,case):
    s=Session(b,GLIDER);load(s,*case['load_lb'])
    s.reset(cas=case['cas_knots'],pitch=case['alpha_deg']+case['gamma_deg'],gamma=case['gamma_deg'])
    s.send('set_controls',TYPES['controls'](0,case['elevator_native'],0,0,0,0,0,0,0,0));return s

def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
    b=Bridge();checks={};evidence={};failures={}
    cases=[solve(b,speed) for speed in [40,45,50,55,60,65,70]]
    cases += [solve(b,50,170,0),solve(b,50,210,180),solve(b,55,airbrake=1)]
    evidence['glide_solves']=cases;checks['glide_constraints']=all(c['converged'] for c in cases)
    checks['no_propulsion']=all(c['diagnostics']['propulsion_force_x_pounds']==0 for c in cases)
    checks['airbrake_increases_sink']=cases[-1]['state']['vertical_speed_feet_per_minute']<cases[3]['state']['vertical_speed_feet_per_minute']
    with Session(b,GLIDER) as s:
        for name,loads in [('overweight',[(0,350),(1,350)]),('aft_cg',[(0,0),(1,170)])]:
            values_=(TYPES['station_load_v3']*len(loads))(*(TYPES['station_load_v3'](*v) for v in loads))
            checks['reject_'+name]=b.loading(s.h,values_,len(loads),None,0)==0
        load(s);s.reset();checks['rejected_loading_atomic']=abs(s.diagnostics().weight_pounds-950)<1e-6
    # Real native fixed-step trajectories, independent of display sample rate.
    runs=[]
    for hz in [30,60,90]:
        with initialized_glide(b,cases[3]) as s:
            energy=[];sample=[]
            for i in range(120*30):
                s.step()
                if i%120==0:
                    st=s.state();d=s.diagnostics();energy.append(st.altitude_msl_feet*.3048*9.80665+.5*(d.true_airspeed_knots*.5144444)**2)
                if i*hz//120!=(i-1)*hz//120:sample.append(s.state().altitude_msl_feet)
            runs.append(dict(hz=hz,state=values(s.state()),energy_j_per_kg=energy))
    evidence['display_pacing']=runs
    checks['display_pacing_native_equivalence']=runs[0]['state']==runs[1]['state']==runs[2]['state']
    checks['unpowered_energy_descent']=all(all(v<u for u,v in zip(run['energy_j_per_kg'],run['energy_j_per_kg'][1:])) for run in runs)
    checks['glide_30s_stability']=abs(runs[0]['state']['calibrated_airspeed_knots']-55)<5 and abs(runs[0]['state']['bank_degrees'])<10
    # Open-loop perturbation evidence, not a human handling acceptance claim.
    maneuvers={}
    for name,ail,ele,rud,brake in [('roll',.15,0,0,0),('rudder',0,0,.2,0),('stall_entry',0,-.55,0,0),('airbrake',0,0,0,1)]:
        try:
            with initialized_glide(b,cases[3]) as s:
                s.send('set_controls',TYPES['controls'](ail,cases[3]['elevator_native']+ele,rud,0,0,0,0,0,0,0))
                s.send('set_glider_controls_v3',TYPES['glider_controls_v3'](brake,0));samples=[]
                for i in range(120*8):
                    s.step()
                    if i%60==0:samples.append(values(s.state()))
                maneuvers[name]=samples;checks[name+'_finite']=all(math.isfinite(v) for row in samples for v in row.values())
        except Exception as ex:failures[name]=str(ex);checks[name+'_finite']=False
    evidence['maneuvers']=maneuvers
    tug=[]
    for temp in [0,20]:
        for throttle in [.3,1]:
            try:
                with Session(b,TUG) as s:
                    s.send('set_conditions_v3',TYPES['conditions_v3'](101325,temp));s.reset(cas=60,pitch=3,gamma=0,engine=1)
                    s.send('set_controls',TYPES['controls'](0,-.1,0,throttle,.8,0,0,0,0,0))
                    for i in range(120*3):s.step()
                    tug.append(dict(temperature_offset_k=temp,throttle=throttle,state=values(s.state()),diagnostics=values(s.diagnostics())))
            except Exception as ex:failures['tug_'+str((temp,throttle))]=str(ex)
    evidence['tug_response']=tug
    checks['tug_engine_response']=len(tug)==4 and tug[1]['state']['engine_rpm']>tug[0]['state']['engine_rpm'] and tug[1]['diagnostics']['propulsion_force_x_pounds']>tug[0]['diagnostics']['propulsion_force_x_pounds']
    result=dict(schema='questflight.soaring.aircraft-reference-cases.v2',checks=checks,evidence=evidence,failures=failures,
        native_sha256=hashlib.sha256(b.library.read_bytes()).hexdigest(),payload_sha256=hashlib.sha256((DATA/'runtime_hashes.sha256').read_bytes()).hexdigest(),
        fidelity='UNVERIFIED: source approximation; no usable manufacturer polar/recovery curve acquired. These are physical software/response checks, not operator-exact handling acceptance.',
        remaining=['ground contact slopes/strikes','standalone tug takeoff/climb/turn','coordinated bank sink and stall recovery','validated manufacturer polar points'])
    (a.output/'aircraft_reference_cases.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(dict(checks=checks,polar=[{k:c[k] for k in ['cas_knots','load_lb','gamma_deg','glide_ratio','converged']} for c in cases],failures=failures),indent=2))
    return 0 if all(checks.values()) else 1
if __name__=='__main__':raise SystemExit(main())
