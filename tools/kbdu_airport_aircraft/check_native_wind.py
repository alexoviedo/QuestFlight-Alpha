"""Run the actual packaged Windows JSBSim bridge without Unity. No flight fallback.
Evidence tests the bridge's signed wind response, not a flyable sailplane or Quest performance.
"""
import ctypes as c
import os,re,json,math,sys,hashlib
from pathlib import Path
repo=Path(__file__).resolve().parents[2]
package=repo/'QuestFlight/Packages/com.alexoviedo.questflight.jsbsim'
plugins=package/'Runtime/Plugins/Windows/x86_64'
directory_cookie=os.add_dll_directory(str(plugins))
lib=c.CDLL(str(plugins/'qfl_jsbsim_native.dll'))
header=(repo/'tools/native/jsbsim_bridge/include/qfl_jsbsim_native.h').read_text()
header=re.sub(r'/\*.*?\*/','',header,flags=re.S)
types={}
for name,body in re.findall(r'typedef struct (\w+) \{(.*?)\}',header,re.S):
 fields=[]
 for kind,names in re.findall(r'\b(double|int)\s+([^;]+);',body):
  fields.extend((n.strip(),c.c_double if kind=='double' else c.c_int) for n in names.split(','))
 types[name]=type(name,(c.Structure,),{'_fields_':fields})
def bind(name,args,result=c.c_int):
 f=getattr(lib,'qfl_jsbsim_'+name);f.argtypes=args;f.restype=result;return f
create=bind('create',[],c.c_void_p);destroy=bind('destroy',[c.c_void_p],None)
load=bind('load_aircraft',[c.c_void_p,c.c_char_p,c.c_char_p])
error=bind('last_error',[c.c_void_p],c.c_char_p)
reset=bind('reset',[c.c_void_p,c.POINTER(types['qfl_jsbsim_initial_conditions'])])
control=bind('set_controls',[c.c_void_p,c.POINTER(types['qfl_jsbsim_controls'])])
wind=bind('set_atmosphere',[c.c_void_p,c.POINTER(types['qfl_jsbsim_atmosphere'])])
trim=bind('trim',[c.c_void_p,c.POINTER(types['qfl_jsbsim_trim_request']),c.POINTER(types['qfl_jsbsim_trim_result'])])
step=bind('advance',[c.c_void_p,c.c_double])
state=bind('get_state',[c.c_void_p,c.POINTER(types['qfl_jsbsim_state'])])
def check(ok,h):
 if not ok:raise RuntimeError(error(h))
def run_ground(enu):
 h=create()
 try:
  check(load(h,str(package/'RuntimeData/JSBSim-1.3.1').encode(),b'c172x'),h)
  ic=types['qfl_jsbsim_initial_conditions'](40.03936527,-105.22608958,(1611.7824+1.325)/.3048,1611.7824/.3048,0,90,0,0,0,1)
  check(reset(h,c.byref(ic)),h);samples=[]
  for i in range(2400):
   controls=types['qfl_jsbsim_controls'](0,0,0,.35,1,0,0,0,1 if i<600 else 0,1 if i<600 else 0)
   atmosphere=types['qfl_jsbsim_atmosphere'](enu[2]/.3048,enu[0]/.3048,-enu[1]/.3048)
   check(control(h,c.byref(controls)),h);check(wind(h,c.byref(atmosphere)),h);check(step(h,1/120),h)
   if i%120==119:
    s=types['qfl_jsbsim_state']();check(state(h,c.byref(s)),h)
    row={n:getattr(s,n) for n,_ in s._fields_};assert all(math.isfinite(v) for v in row.values());samples.append(row)
  return samples
 finally:destroy(h)

def run(enu,power_off=False):
 h=create()
 try:
  check(load(h,str(package/'RuntimeData/JSBSim-1.3.1').encode(),b'c172x'),h)
  ic=types['qfl_jsbsim_initial_conditions'](40.03936527,-105.22608958,(1611.7824+600)/.3048,1611.7824/.3048,80,90,0,0,0,1)
  check(reset(h,c.byref(ic)),h)
  request=types['qfl_jsbsim_trim_request'](1,1000,1000,.00001)
  result=types['qfl_jsbsim_trim_result']();check(trim(h,c.byref(request),c.byref(result)),h)
  controls=types['qfl_jsbsim_controls'](result.aileron_command,result.elevator_command,result.rudder_command,result.throttle_command,1,0,result.pitch_trim_command,0,0,0)
  check(control(h,c.byref(controls)),h)
  samples=[]
  for i in range(2400):
   if power_off and i==1200:
    controls.throttle=0;controls.mixture=0
    check(control(h,c.byref(controls)),h)
   # Ten seconds of identical trimmed settling, then the same signed air-mass input used by Unity.
   w=enu if i>=1200 else (0,0,0)
   atmosphere=types['qfl_jsbsim_atmosphere'](w[2]/.3048,w[0]/.3048,-w[1]/.3048)
   check(wind(h,c.byref(atmosphere)),h);check(step(h,1/120),h)
   if i in (1199,1319,1799,2399):
    s=types['qfl_jsbsim_state']();check(state(h,c.byref(s)),h)
    row={n:getattr(s,n) for n,_ in s._fields_};assert all(math.isfinite(v) for v in row.values());samples.append(row)
  return samples
 finally:destroy(h)
cases={'calm':(0,0,0),'east':(5,0,0),'west':(-5,0,0),'north':(0,0,5),'south':(0,0,-5),'up':(0,2,0),'down':(0,-2,0)}
results={name:run(w) for name,w in cases.items()}
glides={name:run(w,True) for name,w in {'neutral':(0,0,0),'lift':(0,2,0),'sink':(0,-2,0)}.items()}
ground={name:run_ground(w) for name,w in {'calm':(0,0,0),'foothill_headwind':(-2.5,0,0),'crosswind':(0,0,2.5)}.items()}
checks={}
for name,axis,sign in [('east','east',1),('west','east',-1),('north','north',1),('south','north',-1),('up','down',-1),('down','down',1)]:
 key='velocity_'+axis+'_feet_per_second'
 delta=(results[name][-1][key]-results['calm'][-1][key])*.3048
 checks[name]={'delta_velocity_mps_after_10_seconds':delta,'passed':delta*sign>.1}
for name,sign in [('lift',1),('sink',-1)]:
 delta=(glides[name][-1]['altitude_msl_feet']-glides['neutral'][-1]['altitude_msl_feet'])*.3048
 checks['power_off_'+name]={'relative_height_m_after_10_seconds':delta,'passed':delta*sign>1}
for name,rows in ground.items():
 checks['ground_'+name]={'final_speed_knots':rows[-1]['ground_speed_knots'],'passed':all(r['weight_on_wheels'] and abs(r['bank_degrees'])<10 for r in rows[5:]) and rows[-1]['ground_speed_knots']>.5}
report={'bridge_sha256':hashlib.sha256((plugins/'qfl_jsbsim_native.dll').read_bytes()).hexdigest(),'cases':results,'checks':checks,
 'power_off_cases':glides,'ground_cases':ground,'scope':'Native trimmed C172 wind-step, power-off lift/neutral/sink and brake-release ground taxi response. Ground runs use fixed controls, not a validated crosswind pilot. Excludes complete wind-enabled takeoff/landing, spatial field traversal, sailplane and hardware performance acceptance.'}
out=Path(sys.argv[1]);out.write_text(json.dumps(report,indent=2))
print(json.dumps(checks,indent=2));assert all(v['passed'] for v in checks.values())
