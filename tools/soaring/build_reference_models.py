"""Deterministic extraction of two standalone native reference models.

No FlightGear host runtime is imported. Source masters remain external.
SGS source's non-sale notice is retained; derivative stays local/uncommitted.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as E
from native_harness import ROOT, PACKAGE, DATA

def put(parent,tag,text,**attrs):
    n=E.SubElement(parent,tag,attrs);n.text=str(text);return n
def set_text(root,path,value):
    n=root.find(path)
    if n is None:raise ValueError(path)
    n.text=str(value)
def meta(path):
    target=Path(str(path)+'.meta')
    if not target.exists():
        guid=hashlib.sha256(('questflight-soaring-v2/'+str(path.relative_to(ROOT)).replace('\\','/')).encode()).hexdigest()[:32]
        target.write_text('fileFormatVersion: 2\nguid: '+guid+'\n'+('folderAsset: yes\n' if path.is_dir() else '')+'DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
def save(root,path):
    path.parent.mkdir(parents=True,exist_ok=True);E.indent(root,space='  ')
    E.ElementTree(root).write(path,encoding='utf-8',xml_declaration=True);meta(path);meta(path.parent)
def external(root,hook):
    group=E.SubElement(root,'external_reactions')
    for name,kind,location in [('qfl_tow','force',hook),('qfl_airflow','force',(0,0,0)),('qfl_airflow_moment','moment',None)]:
        n=E.SubElement(group,kind,name=name,frame='BODY')
        if location:
            loc=E.SubElement(n,'location',unit='IN')
            for k,v in zip('xyz',location):put(loc,k,v)
        d=E.SubElement(n,'direction')
        for k,v in zip('xyz',(1,0,0)):put(d,k,v)
def normalize(fcs,output,minimum,maximum):
    channel=E.SubElement(fcs,'channel',name=output+' telemetry')
    n=E.SubElement(channel,'aerosurface_scale',name=output+' normalizer')
    put(n,'input','fcs/'+output+'-pos-rad')
    d=E.SubElement(n,'domain');put(d,'min',minimum);put(d,'max',maximum)
    r=E.SubElement(n,'range');put(r,'min',-1);put(r,'max',1)
    put(n,'output','fcs/'+output+'-pos-norm')
def envelope(fcs,minimum,maximum,cg_min,cg_max):
    for name,value in zip(['min-weight-lb','max-weight-lb','min-cg-x-in','max-cg-x-in'],[minimum,maximum,cg_min,cg_max]):
        put(fcs,'property','qfl/loading/'+name,value=str(value))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def glider(source):
    r=E.parse(source/'aircraft/sgs233/sgs233.xml').getroot();r.set('name','qfl_sgs233_ref')
    put(r.find('fileheader'),'note','QuestFlight SGS 2-33A REFERENCE_CONFIGURATION. G2EA Rev4 control limits and datum; estimated empty CG, inertia, hook and vertical contacts. Not operator-exact or validated handling.')
    # G2EA Rev4: root LE station 66.3, MAC LE offset 2.35, MAC 53.54 in.
    set_text(r,"metrics/location[@name='AERORP']/x",82.035)
    set_text(r,'metrics/chord',53.54/12)
    set_text(r,"mass_balance/location[@name='CG']/x",94.0) # declared engineering estimate
    points=r.findall('mass_balance/pointmass')
    for n,name,station in zip(points,['Front student','Rear instructor'],[43.8,74.7]):
        n.set('name',name);set_text(n,'weight',170);set_text(n,'location/x',station)
    ground=r.find('ground_reactions');contacts=ground.findall('contact')
    main=contacts[0];main.set('name','MAIN_WHEEL');set_text(main,'location/x',88.75);set_text(main,'location/y',0);set_text(main,'location/z',-30)
    set_text(main,'brake_group','CENTER');set_text(main,'spring_coeff',1400);set_text(main,'damping_coeff',280);set_text(main,'rolling_friction',.025)
    ground.remove(contacts[1])
    set_text(contacts[2],'location/z',-28);set_text(contacts[2],'max_steer',0)
    for n,y in [(contacts[3],-306),(contacts[4],306)]:set_text(n,'location/y',y);set_text(n,'location/x',82.035)
    tail=contacts[5];tail.set('type','BOGEY');tail.set('name','TAIL_WHEEL');set_text(tail,'location/x',285);set_text(tail,'location/z',-10);set_text(tail,'rolling_friction',.025)
    fcs=r.find('flight_control');put(fcs,'property', 'qfl/capabilities/airbrake',value='1')
    envelope(fcs,610,1040,78.2,86.1)
    from math import radians
    for name,output,low,high in [('Elevator Control','elevator',-23,23),('Rudder Control','rudder',-30,30),
                ('Left Aileron Control','left-aileron',-38,18),('Right Aileron Control','right-aileron',-38,18)]:
        n=fcs.find(".//aerosurface_scale[@name='"+name+"']")
        set_text(n,'range/min',radians(low));set_text(n,'range/max',radians(high));normalize(fcs,output,radians(low),radians(high))
    external(r,(20,0,-12)) # reference nose hook; no manufacturer strength inference
    calibration=ROOT/'docs/soaring/SGS_REFERENCE_CALIBRATION.json'
    if calibration.exists():
        c=json.loads(calibration.read_text())
        profile_product=r.find(".//function[@name='aero/coefficient/CD0']/product")
        profile_product.remove(profile_product.find('table'))
        profile=E.SubElement(profile_product,'sum')
        put(profile,'value',c['profile_base'])
        linear=E.SubElement(profile,'product');put(linear,'property','aero/cl-squared');put(linear,'value',c['profile_quadratic_cl'])
        # Use JSBSim's scheduled CL-squared, as its existing induced-drag term
        # does. Wind-axis forces are cleared before function evaluation, so a
        # direct live force/qS quotient here would incorrectly read zero.
        linear=E.SubElement(profile,'product');put(linear,'value',c['profile_linear_cl'])
        cl=E.SubElement(linear,'sqrt');put(cl,'property','aero/cl-squared')
        post=E.SubElement(profile,'table');put(post,'independentVar','aero/alpha-rad')
        put(post,'tableData','-1.5708 1.5\n-.35 .05\n-.30 0\n.30 0\n.35 .05\n1.5708 1.5')
        set_text(r,".//function[@name='aero/coefficient/CDi']/product/value",c['induced_drag_k'])
        set_text(r,".//function[@name='aero/coefficient/CDsb']/product/value",c['airbrake_drag_increment'])
        set_text(r,".//function[@name='aero/coefficient/dCLsb']/product/value",-.16)
        set_text(r,".//function[@name='aero/coefficient/CLalpha']//tableData",'-1.5708 0\n-.30 -.9\n-.20 -.85\n0 .25\n.21 1.32\n.285 1.76\n.36 1.0\n.60 .21\n1.5708 0')
        put(r.find('fileheader'),'note','Reference polar calibrated with an estimated cambered profile-drag bucket plus induced drag. Manual points support calm glides only; estimated CL peak, post-stall continuation and airbrake increment are not independently validated.')
    save(r,DATA/'aircraft/qfl_sgs233_ref/qfl_sgs233_ref.xml')

def tug(source):
    original=E.parse(source/'J3Cub.xml').getroot()
    r=E.Element('fdm_config',name='qfl_pa18_tug_ref',version='2.0',release='ALPHA')
    for name in ['fileheader','metrics','mass_balance','ground_reactions']:r.append(copy.deepcopy(original.find(name)))
    put(r.find('fileheader'),'note','QuestFlight isolated PA-18 150hp land REFERENCE_CONFIGURATION from wlbragg/J3Cub 7d9c8d34. Estimated inertia and hook; not a verified local tow conversion. No FlightGear scripts, optional kits, hidden engines or fuel transfer.')
    mass=r.find('mass_balance');points=mass.findall('pointmass')
    for n in points[2:]:mass.remove(n)
    # Source J3Cub-main.xml 150hp land specification: 983 lb empty, 1750 lb gross.
    set_text(mass,'emptywt',983)
    for key in ['ixx','iyy','izz']:set_text(mass,key,float(mass.find(key).text)*983/760) # explicit estimate
    set_text(points[0],'weight',0);set_text(points[1],'weight',170)
    ground=r.find('ground_reactions')
    for n in list(ground):
        if n.get('name') not in ['TAIL','LEFT_MAIN','RIGHT_MAIN','TAIL_SKID','LEFT_WING','RIGHT_WING','NOSE','TOP_VS']:ground.remove(n)
    # The upstream default gear positions above are later overwritten by its
    # kit system; isolate the selected conventional wheel geometry here.
    for n in ground.findall('contact'):
        if n.get('name') in ['LEFT_MAIN','RIGHT_MAIN']:
            set_text(n,'location/x',1.17);set_text(n,'location/y',-35.43 if n.get('name')=='LEFT_MAIN' else 35.43);set_text(n,'location/z',-63.32)
        if n.get('name') in ['LEFT_WING','RIGHT_WING']:set_text(n,'location/y',-211.5 if n.get('name')=='LEFT_WING' else 211.5)
    propulsion=E.SubElement(r,'propulsion');engine=copy.deepcopy(original.findall('propulsion/engine')[2]);propulsion.append(engine)
    engine.set('file','qfl_pa18_o320');engine.find('feed').text='0';put(engine,'feed',1)
    engine.find('thruster').set('file','qfl_pa18_prop75_fixed20')
    for tank in original.findall('propulsion/tank')[1:3]:
        n=copy.deepcopy(tank);set_text(n,'contents',60);propulsion.append(n)
    fcs=E.SubElement(r,'flight_control',name='Isolated PA18 controls')
    envelope(fcs,983,1750,10,23) # conservative development CG bounds, not certified envelope
    controls=E.parse(source/'Systems/Conventional Controls.xml').getroot()
    # Keep the PA18 elevator scales and shared mechanical roll/yaw channels.
    for channel in controls.findall('channel'):
        n=copy.deepcopy(channel)
        if n.get('name')=='Pitch':
            for component in list(n):
                name=component.get('name','')
                if 'J3Cub' in name or component.tag=='fcs_function':n.remove(component)
            for element in n.iter():
                if element.text:element.text=element.text.replace('-rad-pa18','-rad').replace('-norm-pa18','-norm')
        fcs.append(n)
    steer=E.SubElement(fcs,'channel',name='Tailwheel steering')
    gain=E.SubElement(steer,'pure_gain',name='Pilot tailwheel');put(gain,'input','fcs/rudder-cmd-norm');put(gain,'gain',-1);put(gain,'output','fcs/steer-cmd-norm')
    flap=copy.deepcopy(original.find("flight_control/channel[@name='Flaps']"))
    for n in list(flap):
        if n.get('name')=='Flaps Lever':flap.remove(n)
    fcs.append(flap)
    aero=E.parse(source/'J3Cub_Aeromatic_aero.xml').getroot()
    # Disabled variant/damage/icing terms are replaced with explicit constants,
    # not properties that silently default or depend on Nasal host scripts.
    constants={'/sim/model/pa-18/slats':0,'/sim/model/pa-18/junkers':0,'/sim/model/pa-18/flaps':1,
               'wing-damage/left-wing':0,'wing-damage/right-wing':0}
    constants.update({'gear/drag-coef['+str(i)+']':0 for i in range(1,10)})
    for parent in aero.iter():
        for n in list(parent):
            text=(n.text or '').strip()
            selector=n.find('independentVar') if n.tag=='table' else None
            if selector is not None and (selector.text or '').strip()=='/sim/model/j3cub/pa-18':
                rows=[line.split() for line in n.find('tableData').text.strip().splitlines()]
                value=next(row[1] for row in rows if float(row[0])==1)
                n.clear();n.tag='value';n.text=value
            elif n.tag=='property' and text in constants:
                n.tag='value';n.attrib.clear();n.text=str(constants[text])
            elif n.tag=='property' and text in ['propulsion/engine[1]/prop-induced-velocity_fps','propulsion/engine[2]/prop-induced-velocity_fps']:
                parent.remove(n)
            elif n.tag=='property' and text=='/engines/active-engine/thrust-coefficient':n.text='propulsion/engine/thrust-coefficient'
    r.append(aero);external(r,(210,0,-20))
    save(r,DATA/'aircraft/qfl_pa18_tug_ref/qfl_pa18_tug_ref.xml')
    shutil.copy2(source/'Engines/Lycoming O-320-A1A-D2H.xml',DATA/'engine/qfl_pa18_o320.xml');meta(DATA/'engine/qfl_pa18_o320.xml')
    prop=E.parse(source/'Engines/prop_75in_2f_NACA_15-30deg-150hp.xml').getroot()
    prop.set('name','PA18 reference 75-inch fixed pitch, 20 degrees at 75 percent radius')
    set_text(prop,'minpitch',20);set_text(prop,'maxpitch',20)
    # JSBSim's fixed-pitch branch calls the one-dimensional table overload.
    # Extract the selected blade angle; retaining a 2-D variable-pitch table
    # here returns invalid fixed-pitch coefficients and extinguishes startup.
    for table in prop.findall('table'):
        data=table.find('tableData')
        rows=[line.split() for line in data.text.strip().splitlines() if line.strip()]
        selected=rows[0].index('20')+1
        data.text='\n'+'\n'.join(row[0]+' '+row[selected] for row in rows[1:])+'\n'
    put(prop,'description','Derived NACA640 table, selected engine[2] blade-angle 20 in pinned J3Cub-main.xml. No governor or host property.')
    save(prop,DATA/'engine/qfl_pa18_prop75_fixed20.xml')
    shutil.copy2(source/'copying.txt',DATA/'aircraft/qfl_pa18_tug_ref/COPYING.txt');meta(DATA/'aircraft/qfl_pa18_tug_ref/COPYING.txt')

def payload_manifest():
    files=sorted(p.relative_to(DATA).as_posix() for p in DATA.rglob('*') if p.is_file() and p.suffix!='.meta' and p.name not in ['runtime_manifest.txt','runtime_hashes.sha256'])
    (DATA/'runtime_manifest.txt').write_text('\n'.join(files+['runtime_hashes.sha256'])+'\n')
    (DATA/'runtime_hashes.sha256').write_text('\n'.join(sha(DATA/name)+'  '+name for name in ['runtime_manifest.txt']+files)+'\n')

def main():
    p=argparse.ArgumentParser();p.add_argument('--jsbsim-source',type=Path,required=True);p.add_argument('--pa18-source',type=Path,required=True);a=p.parse_args()
    glider(a.jsbsim_source);tug(a.pa18_source);payload_manifest()
    result={'schema':'questflight.soaring-native-models.v2','configuration':'REFERENCE_CONFIGURATION','fidelity':'UNVERIFIED_PENDING_REFERENCE_CASES',
      'sgs_source':{'revision':'3b25f25e49b42d0489c04ac805674fc1450ca579','sha256':sha(a.jsbsim_source/'aircraft/sgs233/sgs233.xml'),'rights':'Source explicitly says not to be sold; local reference derivative, no new distribution authorization'},
      'tug_source':{'revision':'7d9c8d34ffd24aaaab7f1448cd14573d16f3d26e','sha256':sha(a.pa18_source/'J3Cub.xml'),'rights':'GPL-3.0 source copying.txt retained'},
      'sgs_reference':{'type':'SGS 2-33A reference, no particular serial','max_weight_lb':1040,'cg_station_inches':[78.2,86.1],
        'front_station_inches':43.8,'rear_station_inches':74.7,'estimated_empty_weight_lb':610,'estimated_empty_cg_station_inches':94,
        'dual_load_lb':[170,170],'manufacturer_controls_degrees':{'elevator':[-23,23],'rudder':[-30,30],'ailerons':[-38,18]},
        'source':'G2EA Rev4 (2010), K&L I-4427 (2015); upstream aero retained and not independently validated',
        'estimated':['inertia','vertical gear geometry','hook station 20/0/-12 in','upstream polar/derivatives']},
      'tug_reference':{'power_hp':150,'empty_weight_lb':983,'pilot_lb':170,'fuel_lb':[60,60],'fixed_prop_inches':75,'blade_pitch_degrees':20,
        'source':'Pinned PA-18-set.xml engine index2, J3Cub-main.xml 150hp land specification and blade-angle',
        'estimated':['inertia scaled from source by empty-mass ratio','rear hook 210/0/-20 in','upstream Cub-family aero fidelity']},
      'outputs':[{'path':str(p.relative_to(PACKAGE)).replace('\\','/'),'sha256':sha(p)} for p in DATA.rglob('*') if p.is_file() and ('qfl_' in str(p)) and p.suffix!='.meta'],
      'payload_sha256':sha(DATA/'runtime_hashes.sha256')}
    (ROOT/'docs/soaring/SOARING_REFERENCE_LEDGER.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result,indent=2))
if __name__=='__main__':main()
