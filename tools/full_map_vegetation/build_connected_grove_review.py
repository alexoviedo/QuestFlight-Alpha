"""Publish source-stage evidence; never imply a rendered grove is accepted."""
import argparse
import html
import json
from pathlib import Path

import cv2
import numpy as np
from affine import Affine
from PIL import Image
from rasterio.features import rasterize
from shapely.geometry import shape

from build_connected_grove_evidence import imagery


def review(root,images):
    assembly=json.loads((root/'assembly/assembly.json').read_text())
    refs=json.loads((root/'references/references.json').read_text())
    records=json.loads((root/'pilot/pilot_components.json').read_text())
    complete=[r for r in records if not r['touchesSourceEdge']]
    pilot=max(complete,key=lambda r:r['areaMeters2'])
    polygon=shape(pilot['geometry']);x0,z0,x1,z1=polygon.bounds
    side=max(x1-x0,z1-z0)+20;cx,cz=(x0+x1)/2,(z0+z1)/2
    bounds=[cx-side/2,cz-side/2,cx+side/2,cz+side/2]
    n=768;spacing=side/n;affine=Affine(spacing,0,bounds[0],0,-spacing,bounds[3])
    photo=imagery(bounds,n,images)
    mask=rasterize([(polygon,1)],out_shape=(n,n),transform=affine,dtype='uint8')
    Image.fromarray(photo).save(root/'pilot_source.png')
    contours,_=cv2.findContours(mask,cv2.RETR_CCOMP,cv2.CHAIN_APPROX_SIMPLE)
    overlay=photo.copy();cv2.drawContours(overlay,contours,-1,(20,255,220),2)
    Image.fromarray(overlay).save(root/'pilot_boundary.png')
    data=np.load(root/'pilot/height_evidence.npz')
    xx=bounds[0]+(np.arange(n)+.5)*spacing
    zz=bounds[3]-(np.arange(n)+.5)*spacing
    col=np.floor(xx-float(data['xmin'])).astype(int)
    row=np.floor(float(data['zmax'])-zz).astype(int)
    if min(col)<0 or min(row)<0 or max(col)>=data['height'].shape[1] or max(row)>=data['height'].shape[0]:
        raise ValueError('Complete pilot must be inside sampled source height coverage')
    height=data['height'][row[:,None],col[None,:]]
    colored=cv2.applyColorMap(np.uint8(np.clip(np.nan_to_num(height)/25,0,1)*255),cv2.COLORMAP_TURBO)[:,:,::-1]
    colored[mask==0]=24;colored[(mask!=0)&~np.isfinite(height)]=(255,60,200)
    Image.fromarray(colored).save(root/'pilot_height.png')
    summary=dict(pilotId=pilot['id'],areaMeters2=pilot['areaMeters2'],bounds=polygon.bounds,
        heightP50=pilot['heightP50'],heightP95=pilot['heightP95'],sourceEdge=False,
        missingHeightFraction=float((~np.isfinite(height[mask!=0])).mean()),
        representation='Source outline and measured height evidence only; no foliage geometry',
        gate='NOT_ACCEPTED_REFERENCE_ANNOTATIONS_UNQUALIFIED')
    (root/'selected_pilot.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
    cards=[]
    for r in refs['crops']:
        name=html.escape(r['id']);split=html.escape(r['split'])
        cards.append(f'<article><h3>{name} <small>{split}</small></h3><div class="pair"><figure><img src="references/{name}.png"><figcaption>2020 aerial reference</figcaption></figure><figure><img src="assembly/{name}_overlay.png"><figcaption>Classified canopy extent</figcaption></figure></div></article>')
    text=f'''<!doctype html><html lang="en"><meta charset="utf-8"><title>Connected grove source evidence</title>
<style>body{{font:16px/1.5 system-ui;background:#13191d;color:#e3e8eb;margin:24px auto;max-width:1400px;padding:0 20px}}h1{{font-size:30px}}a{{color:#7cdbd2}}.status{{border-left:5px solid #efbe62;background:#272720;padding:14px}}.pair,.pilot{{display:flex;gap:12px}}figure{{margin:0;flex:1}}img{{width:100%;display:block}}figcaption,small{{color:#afbac3}}.grid{{display:grid;grid-template-columns:repeat(3,1fr);gap:20px}}article{{background:#20292f;padding:12px}}h3{{font-size:16px}}small{{display:block}}@media(max-width:900px){{.grid{{grid-template-columns:1fr 1fr}}.pilot{{flex-direction:column}}}}@media(max-width:600px){{.grid{{grid-template-columns:1fr}}}}</style>
<h1>Connected grove source evidence</h1>
<p class="status"><b>Source stage only. No new simulator canopy is ready.</b> Source acquisition and connectivity compilation are implemented. The independent annotation gate is unresolved; approximate draft labels were rejected for acceptance. No accuracy percentage, Quest performance pass or map-wide visual improvement is claimed.</p>
<p>City of Boulder/NV5 classified 2020 LiDAR provides the connected outlines and above-ground heights. The photographs provide independent visual evidence. Cyan marks canopy boundaries; magenta hatching means unavailable source coverage. Roads or shadows in these photographs are not foliage textures.</p>
<p><b>{assembly['sourceCount']} extent tiles</b>, five height tiles, {assembly['nativeComponents']:,} input components and <b>{assembly['removedSourceSeamSplits']:,} removed tile splits</b>. All 30 fixed image crops have extent coverage. Joining happens in the native coordinate system before projection; real holes and gaps are preserved. Six regression tests pass. These counts describe source geometry, not trees or rendering cost.</p>
<h2>Complete grove selected for the next geometry stage</h2>
<p>{pilot['areaMeters2']:,.0f} m²; measured height median {pilot['heightP50']:.1f} m / 95th percentile {pilot['heightP95']:.1f} m. This component stays inside the height source, avoiding the previous truncated trial boundary. Height colors span 0–25 m. The height image is a source diagnostic, not a proposed solid canopy surface.</p>
<div class="pilot"><figure><img src="pilot_source.png"><figcaption>Aerial source</figcaption></figure><figure><img src="pilot_boundary.png"><figcaption>One connected component, with openings</figcaption></figure><figure><img src="pilot_height.png"><figcaption>Measured height variation within that component</figcaption></figure></div>
<h2>Fixed reference set</h2><p>Five crops per category; twelve development and eighteen held out. Formal boundary traces and split/merge review remain incomplete. Empty lawn and paved-road held-out checks contain zero detected canopy pixels, but those checks do not establish overall accuracy.</p>
<div class="grid">{''.join(cards)}</div>
<h2>Provenance and limits</h2><p>Canopy height and extent: City of Boulder / NV5 / DRCOG 2020 LiDAR, <a href="https://creativecommons.org/licenses/by/4.0/">CC BY 4.0</a>. Modified for QuestFlight; no endorsement implied. <a href="https://www.arcgis.com/home/item.html?id=8fb05137a9f049afa0b60d44086b19af">Publisher description</a>. Existing City 2020 orthophotos retain their separate CC0 provenance. The acquisitions need not have identical seasonal appearance.</p>
<p><a href="assembly/assembly.json">Source assembly report</a> · <a href="selected_pilot.json">Pilot evidence</a> · <a href="references/references.json">Frozen crop and image hashes</a>. Raw data and review images remain outside the product repository.</p></html>'''
    (root/'comparison.html').write_text(text,encoding='utf-8')
    print(json.dumps(summary),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,required=True);parser.add_argument('--images',type=Path,required=True)
    args=parser.parse_args();review(args.root,args.images)
