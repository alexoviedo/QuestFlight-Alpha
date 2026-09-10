"""Freeze independent aerial reference crops before inspecting generated masks."""
import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw

from build_connected_grove_evidence import imagery, REPO


def prepare(config, images, output):
    if output.resolve() == REPO or REPO in output.resolve().parents:
        raise ValueError('Review images must remain external')
    definition = json.loads(config.read_text(encoding='utf-8'))
    output.mkdir(parents=True, exist_ok=True)
    records = []
    used = {}
    n = definition['pixels']
    half = definition['cropSizeMeters'] / 2
    for category, crops in definition['categories'].items():
        sheet = Image.new('RGB', (5*(n+12), n+40), '#171c20')
        draw = ImageDraw.Draw(sheet)
        for i, (site,u,v) in enumerate(crops):
            sx, sz, tile = definition['sites'][site]
            x, z = sx-300+u, sz+300-v
            bounds = [x-half,z-half,x+half,z+half]
            identifier = f'{category}_{i+1}'
            image = Image.fromarray(imagery(bounds,n,images))
            image.save(output / f'{identifier}.png')
            # Coordinates label the image itself, not candidate boundaries.
            annotated = image.copy()
            grid = ImageDraw.Draw(annotated)
            for a in range(0,n,32):
                grid.text((a+1,1),str(a),fill='white',stroke_width=1,stroke_fill='black')
                grid.text((1,a+10),str(a),fill='white',stroke_width=1,stroke_fill='black')
            sheet.paste(annotated,(i*(n+12),30))
            draw.text((i*(n+12),5),identifier,fill='white')
            records.append(dict(id=identifier, category=category, site=site, tile=tile,
                split='development' if i<2 else 'heldout', bounds=bounds, pixels=n,
                annotationStatus='PENDING_INDEPENDENT_TRACE'))
            import math
            for col in range(math.floor((bounds[0]+12096)/672),math.floor((bounds[2]+12096)/672)+1):
                for row in range(math.floor((bounds[1]+12096)/672),math.floor((bounds[3]+12096)/672)+1):
                    name=f'KBDU_2020_high_{col:03}_{row:03}.jpg'
                    if name not in used:
                        with (images/name).open('rb') as stream:
                            used[name]=hashlib.file_digest(stream,'sha256').hexdigest()
        sheet.save(output / f'{category}_reference_sheet.png')
    manifest=dict(schema='questflight.connected-grove-references.v1',
        definitionSha256=hashlib.sha256(config.read_bytes()).hexdigest(),
        imagerySource='Existing City of Boulder 2020 orthophotos, CC0; not the CC BY 4.0 canopy source',
        actualImagerySha256=used, annotationMethod='Independent visual traces on raw orthophotos; never copied from evaluated canopy polygons',
        crops=records)
    (output/'references.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--config',type=Path,default=Path(__file__).with_name('connected_grove_reference_crops.json'))
    parser.add_argument('--images',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    prepare(args.config,args.images,args.output)
