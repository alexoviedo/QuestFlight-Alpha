"""Isolated, labeled test pilot driving actual tug surfaces; never prescribes pose."""
import argparse,ctypes as C,hashlib,json,math
from pathlib import Path
from native_harness import Bridge,Session,TYPES,values,DATA
from test_native_ground import LAT,LON,MSL,radial

def clamp(v,lo,hi):return max(lo,min(hi,v))
def run(b,temperature):
    with Session(b,'qfl_pa18_tug_ref') as s:
        p=TYPES['terrain_patch_v3'](LAT-.015,LON-.015,.001,.001,33,33)
        heights=(C.c_double*1089)(*[MSL]*1089);b.check(b.terrain(s.h,C.byref(p),heights,1089),s.h)
        s.send('set_conditions_v3',TYPES['conditions_v3'](101325,temperature))
        s.send('reset',TYPES['initial_conditions'](LAT,LON,radial(LAT,MSL+1.2)/.3048,MSL/.3048,0,90,8,0,0,1))
        samples=[];airborne=None;max_bank=0
        for step in range(120*75):
            st=s.state();t=step/120
            if step%120==0:
                p.minimum_latitude_degrees=st.latitude_degrees-.016;p.minimum_longitude_degrees=st.longitude_degrees-.016
                b.check(b.terrain(s.h,C.byref(p),heights,1089),s.h)
            heading_target=90 if t<45 else 105
            heading_error=(heading_target-st.heading_degrees+180)%360-180
            bank_target=clamp(heading_error*.8,-15,15) if st.altitude_agl_feet>80 else 0
            pitch_target=8 if st.calibrated_airspeed_knots<25 else clamp(3+(st.calibrated_airspeed_knots-55)*.45,-3,8)
            if st.weight_on_wheels and st.calibrated_airspeed_knots<40 and t>3:pitch_target=3
            aileron=clamp(.045*(bank_target-st.bank_degrees)-.30*st.roll_rate_radians_per_second,-.5,.5)
            elevator=clamp(-.15-.06*(pitch_target-st.pitch_degrees)+.9*st.pitch_rate_radians_per_second,-.7,.7)
            rudder=clamp((-.025*heading_error if st.altitude_agl_feet<10 else 0)-.04*st.sideslip_degrees+.25*st.yaw_rate_radians_per_second,-.5,.5)
            brake=1 if t<2 else 0
            throttle=.2 if t<2 else min(1,.2+(t-2)*.25)
            s.send('set_controls',TYPES['controls'](aileron,elevator,rudder,throttle,.8,0,0,0,brake,brake));s.step()
            if t>3 and st.altitude_agl_feet>10 and not st.weight_on_wheels and airborne is None:airborne=t
            max_bank=max(max_bank,abs(st.bank_degrees))
            if step%60==0:samples.append(dict(state=values(s.state()),controls=[aileron,elevator,rudder,throttle],diagnostics=values(s.diagnostics())))
        return dict(temperature_offset_k=temperature,airborne_seconds=airborne,max_bank_deg=max_bank,samples=samples,final=values(s.state()))
def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
    b=Bridge();cases=[];errors={}
    for temp in [0,20]:
        try:cases.append(run(b,temp))
        except Exception as ex:errors[str(temp)]=str(ex)
    checks={}
    for c in cases:
        name=str(c['temperature_offset_k']);checks[name+'_physical_takeoff']=c['airborne_seconds'] is not None
        checks[name+'_positive_climb']=c['final']['altitude_agl_feet']>300 and c['final']['vertical_speed_feet_per_minute']>100
        checks[name+'_controlled_turn']=abs(c['final']['heading_degrees']-105)<8 and c['max_bank_deg']<35
    checks['both_density_cases_complete']=len(cases)==2
    if len(cases)==2:checks['hot_day_reduces_height']=cases[1]['final']['altitude_agl_feet']<cases[0]['final']['altitude_agl_feet']
    result=dict(scope='Actual native PA18 standalone takeoff/climb/turn with isolated test autopilot. No spline/pose forcing, not flight-manual or operator acceptance.',checks=checks,errors=errors,cases=cases,native_sha256=hashlib.sha256(b.library.read_bytes()).hexdigest(),payload_sha256=hashlib.sha256((DATA/'runtime_hashes.sha256').read_bytes()).hexdigest())
    (a.output/'native_tug_departure.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(dict(checks=checks,errors=errors,results=[{k:c[k] for k in ['temperature_offset_k','airborne_seconds','max_bank_deg','final']} for c in cases]),indent=2))
    return 0 if all(checks.values()) else 1
if __name__=='__main__':raise SystemExit(main())
