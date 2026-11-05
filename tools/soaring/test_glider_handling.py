"""Native reference stall/recovery and coordinated turn; isolated test pilot.
Not a spin, handling-fidelity or operator-procedure acceptance test.
"""
import argparse,hashlib,json,math
from pathlib import Path
from native_harness import Bridge,Session,TYPES,values,DATA
from run_aircraft_reference_cases import solve,initialized_glide

def clamp(v,a,b):return min(b,max(a,v))
def run(b,kind):
    trimmed=solve(b,50);s=initialized_glide(b,trimmed)
    samples=[];stall=None;recovered=None;max_beta=0;heading_delta=0;previous_heading=90
    try:
        for i in range(120*65):
            t=i/120;st=s.state()
            bank_target=25 if kind=='turn' and 5<t<45 else 0
            target_pitch=trimmed['alpha_deg']+trimmed['gamma_deg']+(st.calibrated_airspeed_knots-50)*.65
            if kind=='stall' and 5<t<35 and stall is None:target_pitch=min(22,4+(t-5)*.7)
            if kind=='stall' and st.angle_of_attack_degrees>17 and stall is None:stall=t
            if stall is not None and t-stall<3:target_pitch=-8
            elevator=clamp(trimmed['elevator_native']-.07*(target_pitch-st.pitch_degrees)+.8*st.pitch_rate_radians_per_second,-.8,.8)
            aileron=clamp(.06*(bank_target-st.bank_degrees)-.4*st.roll_rate_radians_per_second,-.6,.6)
            rudder=clamp(-.04*st.sideslip_degrees+.2*st.yaw_rate_radians_per_second,-.4,.4)
            s.send('set_controls',TYPES['controls'](aileron,elevator,rudder,0,0,0,0,0,0,0));s.step()
            if stall is not None and t>stall+3 and recovered is None and abs(st.angle_of_attack_degrees)<12 and st.calibrated_airspeed_knots>40:recovered=t
            heading_delta+=(st.heading_degrees-previous_heading+180)%360-180;previous_heading=st.heading_degrees
            if 15<t<40:max_beta=max(max_beta,abs(st.sideslip_degrees))
            if i%60==0:samples.append(dict(state=values(s.state()),controls=[aileron,elevator,rudder]))
        return dict(kind=kind,stall_time=stall,recovered_time=recovered,turn_max_beta_deg=max_beta,heading_delta_deg=heading_delta,samples=samples)
    finally:s.__exit__()

def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
    b=Bridge();cases={};errors={}
    for kind in ['stall','turn']:
        try:cases[kind]=run(b,kind)
        except Exception as ex:errors[kind]=str(ex)
    checks={}
    if 'stall' in cases:
        c=cases['stall'];checks['stall_reached']=c['stall_time'] is not None
        checks['stall_recovered']=c['recovered_time'] is not None and c['recovered_time']-c['stall_time']<20
    if 'turn' in cases:
        c=cases['turn'];checks['coordinated_turn']=c['heading_delta_deg']>200 and c['turn_max_beta_deg']<5
        checks['bounded_bank']=max(abs(v['state']['bank_degrees']) for v in c['samples'])<40
    checks['both_cases_completed']=len(cases)==2
    result=dict(scope=__doc__,checks=checks,errors=errors,cases=cases,native_sha256=hashlib.sha256(b.library.read_bytes()).hexdigest(),payload_sha256=hashlib.sha256((DATA/'runtime_hashes.sha256').read_bytes()).hexdigest())
    (a.output/'glider_handling.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(dict(checks=checks,errors=errors,summary={k:{n:v for n,v in c.items() if n!='samples'} for k,c in cases.items()}),indent=2))
    return 0 if all(checks.values()) else 1
if __name__=='__main__':raise SystemExit(main())
