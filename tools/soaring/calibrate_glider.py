"""Fit two explicit source points, then leave independent cases to validation."""
import argparse,json,hashlib,shutil
from pathlib import Path
import xml.etree.ElementTree as E
from scipy.optimize import least_squares
from native_harness import ROOT,DATA,Bridge
from run_aircraft_reference_cases import solve
from build_reference_models import glider,payload_manifest

def main():
    p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
    model=DATA/'aircraft/qfl_sgs233_ref/qfl_sgs233_ref.xml'
    shutil.copy2(model,a.output/'sgs_before_calibration.xml')
    path=ROOT/'docs/soaring/SGS_REFERENCE_CALIBRATION.json'
    config=dict(schema='questflight.sgs-reference-calibration.v2',source_url='https://soaringtigers.org/SGS-2-33-Manual.pdf',source_sha256='02e706c37c9c3d9e8953f642d880ccca20f170c0ccc26c5bf74c79c2da2407dd',
        source_revision='Undated older Schweizer manual scan including supplemental serial 500 controls, page 1-5. Superseded by K&L 2022 manual; not exact current airframe.',
        assumptions=['Reference two-place weight 1040 lb; 210 lb front/220 lb rear with estimated empty CG.', 'Manual mph treated as calibrated speed for development; instrument-error curve unavailable.', 'Sink reference compared at sea-level ISA, not Boulder TAS.'],
        calibration_points=[dict(speed_mph=50,glide_ratio=23,glide_ratio_derivative_per_mph=0),dict(speed_mph=42,sink_fps=3.1)],
        validation_tolerances=dict(glide_ratio_fraction=.05,minimum_sink_fraction=.10,best_speed_fraction=.10),
        airbrake_drag_increment=.08,estimated=['Cambered profile drag bucket fitted to two glide points including declared best-speed condition; not a measured full polar','CL peak 1.76 at .285 rad, bounded post-stall continuation','Airbrake drag .08 and lift -.16; no accepted spoiler polar','Inertia and stability derivatives retained from approximate source'],profile_base=.04,profile_linear_cl=-.06,profile_quadratic_cl=.045,induced_drag_k=.027)
    b=Bridge();iterations=[]
    def residual(x):
        for key,v in zip(['profile_base','profile_linear_cl','profile_quadratic_cl'],x):config[key]=float(v)
        path.write_text(json.dumps(config,indent=2)+'\n');glider(a.source)
        best=solve(b,50/1.150779448,210,220,altitude=0);slow=solve(b,42/1.150779448,210,220,altitude=0)
        left=solve(b,49/1.150779448,210,220,altitude=0);right=solve(b,51/1.150779448,210,220,altitude=0)
        r=[(best['glide_ratio']-23)/23,(-slow['state']['vertical_speed_feet_per_minute']/60-3.1)/3.1,(right['glide_ratio']-left['glide_ratio'])/2]
        iterations.append(dict(parameters=list(map(float,x)),residual=r));return r
    fit=least_squares(residual,[.04,-.06,.045],bounds=([.01,-.15,.001],[.12,0,.15]),diff_step=.001,xtol=1e-8,ftol=1e-8,gtol=1e-8,max_nfev=40)
    r=residual(fit.x);payload_manifest()
    (a.output/'calibration_fit.json').write_text(json.dumps(dict(residual=r,iterations=iterations,source_model_sha256=hashlib.sha256((a.output/'sgs_before_calibration.xml').read_bytes()).hexdigest(),fitted_model_sha256=hashlib.sha256(model.read_bytes()).hexdigest()),indent=2)+'\n')
    print(json.dumps(dict(parameters=list(map(float,fit.x)),residual=r),indent=2))
if __name__=='__main__':main()
