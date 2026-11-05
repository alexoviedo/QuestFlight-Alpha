"""Independent transcription of NASA 20060004052 Appendices A/B check case.

Government work; source PDF and hash remain in external sources. Desert Rock
parameters are used only for this published check, not called Boulder data.
"""
import argparse
import json
import math
from pathlib import Path
import numpy as np
import matplotlib.pyplot as plt

SHAPES = [(1.5352,2.5826,-.0113,-.1950),(1.5265,3.6054,-.0176,-.1265),
          (1.4866,4.8356,-.0320,-.0818),(1.2042,7.7904,.0848,-.0445),
          (.8816,13.972,.3404,-.0216),(.7067,23.994,.5689,-.0099),(.6189,42.7965,.7157,-.0033)]

def allen(radius,z=280,zi=1401,wstar=2.56,count=5,area=1000000):
    zz=z/zi
    r2=max(10,.102*zz**(1/3)*(1-.25*zz)*zi)
    ratio=.0011*r2+.14 if r2<600 else .8
    wt=zz**(1/3)*(1-1.1*zz)*wstar
    wc=3*wt*(1-ratio)/(1-ratio**3)
    index=sum(ratio>=v for v in [.195,.305,.415,.525,.635,.745])
    ka,kb,kc,kd=SHAPES[index];r=radius/r2
    ws=max(0,1/(1+(ka*abs(r+kc))**kb)+kd*r) if z<zi else 0
    swd=2.5*(zz-.5) if .5<zz<=.9 else 0
    wd=min(0,swd*math.pi/6*math.sin(math.pi*r)) if radius>ratio*r2 and r<2 else 0
    at=count*math.pi*r2*r2
    we=min(0,-at*wt*(1-swd)/(area-at))
    w=ws*wc+wd*wt
    return (w*(1-we/wc)+we if radius>ratio*r2 else w),r2,wc,we

def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
    y=np.arange(0,400,10);center=1000/6
    vals=[allen(math.hypot(170-center,v-center))[0] for v in y]
    _,radius,core,sink=allen(0)
    checks={'published_radius_79_4m':abs(radius-79.4)<.1,'published_five_sources':round(.6*1000000/(1401*radius))==5,
            'figure12_peak_2_7mps':abs(max(vals)-2.7)<.04,'figure12_sink_minus_0_13mps':abs(sink+.13)<.02}
    # Analytic production extension below uses a compact, exactly balanced
    # core/annulus; this independently integrates its horizontal volume flux.
    r=np.linspace(0,3,300001)
    shape=np.where(r<=1,(1-r*r)**2,-5/64*(1-(r-2)**2)**2)
    flux=np.trapezoid(shape*r,r)*2*math.pi
    checks['compact_extension_zero_flux']=bool(abs(flux)<1e-8)
    checks={k:bool(v) for k,v in checks.items()}
    plt.figure(figsize=(7,4));plt.plot(y,vals,'k.-');plt.xlim(0,400);plt.ylim(-.5,3);plt.grid();plt.xlabel('Y position (m) at X=170 m');plt.ylabel('Vertical air velocity (m/s)');plt.tight_layout();plt.savefig(a.output/'allen_figure12_reproduction.png',dpi=160);plt.close()
    result={'source':'https://ntrs.nasa.gov/api/citations/20060004052/downloads/20060004052.pdf','checks':checks,'radius_m':radius,'core_mps':core,'sink_mps':sink,'compact_shape_area_integral':float(flux),'samples':[{'y':int(v),'w':w} for v,w in zip(y,vals)]}
    (a.output/'allen_reference.json').write_text(json.dumps(result,indent=2)+'\n');print({k:v for k,v in result.items() if k!='samples'});return not all(checks.values())

if __name__=='__main__':raise SystemExit(main())
