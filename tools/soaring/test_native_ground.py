"""Native prepared-heightfield scenarios; no Unity dynamics or contact stand-in."""
import argparse,ctypes as C,hashlib,json,math
from pathlib import Path
from native_harness import Bridge,Session,TYPES,values,DATA

LAT=40.03936527;LON=-105.22608958;MSL=1611.7824
def radial(latitude,height):
    a=6378137;b=6356752.314245179;lat=math.radians(latitude);e2=1-b*b/(a*a)
    n=a/math.sqrt(1-e2*math.sin(lat)**2);x=(n+height)*math.cos(lat);z=(n*(1-e2)+height)*math.sin(lat)
    r=math.hypot(x,z);sea=a*b*r/math.sqrt(b*b*x*x+a*a*z*z);return r-sea
def patch(s,east_slope=0,north_slope=0):
    p=TYPES['terrain_patch_v3'](LAT-.004,LON-.004,.00025,.00025,33,33)
    heights=(C.c_double*1089)(*(MSL+(row-16)*.00025*111040*north_slope+(col-16)*.00025*85300*east_slope for row in range(33) for col in range(33)))
    s.b.check(s.b.terrain(s.h,C.byref(p),heights,1089),s.h);return p,heights
def reset_ground(s,height,pitch=0,bank=0,cas=0,engine=0):
    s.send('reset',TYPES['initial_conditions'](LAT,LON,radial(LAT,MSL+height)/.3048,MSL/.3048,cas,90,pitch,bank,0,engine))
def run_case(b,model,height,pitch,bank,slope,brake=0,cas=0):
    with Session(b,model) as s:
        patch(s,*slope);reset_ground(s,height,pitch,bank,cas)
        s.send('set_controls',TYPES['controls'](0,0,0,0,0,0,0,0,brake,brake))
        if model=='qfl_sgs233_ref':s.send('set_glider_controls_v3',TYPES['glider_controls_v3'](0,brake))
        max_compression=0;contacts=set();max_force=0;rows=[]
        count=b.read(s.h,'get_capabilities_v3','capabilities_v3').contact_count
        for i in range(120*8):
            s.step()
            for idx in range(count):
                c=TYPES['contact_v3']();b.check(b.contact(s.h,idx,C.byref(c)),s.h)
                max_compression=max(max_compression,c.compression_ft);max_force=max(max_force,math.hypot(c.force_body_x_n,c.force_body_y_n,c.force_body_z_n))
                if c.weight_on_wheels:contacts.add(idx)
            if i%120==0:rows.append(values(s.state()))
        return dict(max_compression_ft=max_compression,max_contact_force_n=max_force,contact_ids=sorted(contacts),samples=rows,final=values(s.state()))
def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
    b=Bridge();checks={};cases={};failures={}
    for name,args in [('glider_flat',('qfl_sgs233_ref',.75,0,0,(0,0))),('glider_slope',('qfl_sgs233_ref',.8,0,4,(.02,.04))),
                      ('glider_wingtip',('qfl_sgs233_ref',.75,0,9,(0,0))),('tug_tailwheel',('qfl_pa18_tug_ref',1.2,8,0,(0,0))),
                      ('glider_rollout',('qfl_sgs233_ref',.75,0,0,(0,0),0,15)),('glider_braked',('qfl_sgs233_ref',.75,0,0,(0,0),1,15))]:
        try:
            case=run_case(b,*args);cases[name]=case
            checks[name+'_bounded']=case['max_compression_ft']<1 and abs(case['final']['vertical_speed_feet_per_minute'])<150 and math.isfinite(case['max_contact_force_n'])
        except Exception as ex:failures[name]=str(ex);checks[name+'_bounded']=False
    if 'glider_rollout' in cases and 'glider_braked' in cases:checks['wheel_brake_decelerates']=cases['glider_braked']['final']['ground_speed_knots']<cases['glider_rollout']['final']['ground_speed_knots']
    if 'glider_wingtip' in cases:checks['wingtip_contact']=bool(set(cases['glider_wingtip']['contact_ids']) & {2,3})
    with Session(b,'qfl_sgs233_ref') as s:
        p,h=patch(s);h[0]=float('nan');checks['nan_height_rejected']=b.terrain(s.h,C.byref(p),h,1089)==0
        # Reset at a point beyond the prepared coverage must visibly fail.
        ic=TYPES['initial_conditions'](LAT+.01,LON,radial(LAT+.01,MSL+.75)/.3048,MSL/.3048,0,90,0,0,0,0)
        checks['out_of_coverage_fails']=b.reset(s.h,C.byref(ic))==0
    result=dict(checks=checks,cases=cases,failures=failures,native_sha256=hashlib.sha256(b.library.read_bytes()).hexdigest(),payload_sha256=hashlib.sha256((DATA/'runtime_hashes.sha256').read_bytes()).hexdigest(),scope='Native contact software checks on declared reference contacts; terrain approximation and actual hull alignment need Unity integration checks.')
    (a.output/'native_ground.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(dict(checks=checks,failures=failures),indent=2));return 0 if all(checks.values()) else 1
if __name__=='__main__':raise SystemExit(main())
