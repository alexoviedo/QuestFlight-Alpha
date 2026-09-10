"""Publish existing paired renders without modifying their pixels."""
import argparse
import html
import json
import os
from pathlib import Path

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', required=True, type=Path)
    parser.add_argument('--previous-root', type=Path)
    args=parser.parse_args()
    root = args.root
    evidence = json.loads((root / 'visual_evidence.json').read_text())
    labels = ['Overhead', 'Near aerial', 'Low side', 'Middle aerial',
              'About 400 m', 'About 790 m', 'About 1,500 m', 'Opposite side']
    sections = []
    for index in [3,4,1,2,0,5,6,7]:
        label=labels[index]
        pairs = []
        for eye in ('left', 'right'):
            images = []
            for variant, title in [('control', 'Existing trees'), ('candidate', 'Connected grove')]:
                relative = f'{variant}/screenshots/connected_{index:02d}_{eye}.png'
                if args.previous_root and variant=='control':
                    relative=Path(os.path.relpath(args.previous_root/'candidate/screenshots'/f'connected_{index:02d}_{eye}.png',root)).as_posix()
                    title='Previous sparse pilot'
                elif args.previous_root:
                    title='Fuller connected canopy'
                if not (root / relative).is_file():
                    raise FileNotFoundError(relative)
                images.append(f'<figure><figcaption>{title} · {eye} eye</figcaption>'
                              f'<a href="{relative}"><img loading="lazy" src="{relative}" alt="{title}, {label}, {eye} eye"></a></figure>')
            pairs.append('<div class="pair">' + ''.join(images) + '</div>')
        sections.append(f'<section><h2>{label}</h2>' + ''.join(pairs) + '</section>')
    page = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Connected grove pilot comparison</title><style>
body{margin:24px auto;max-width:1600px;padding:0 20px;background:#161b20;color:#eee;font:17px/1.5 system-ui}
a{color:#9cd8ff}.pair{display:grid;grid-template-columns:1fr 1fr;gap:16px}figure{margin:8px 0}img{width:100%;display:block}figcaption{padding:8px 0}section{margin:36px 0}p{max-width:1000px}
@media(max-width:750px){.pair{grid-template-columns:1fr}}
</style><h1>One connected grove, branch-sized foliage</h1>
<p>Paired Editor review renders of the same source-shaped 2,025 m² grove. These are offset-eye Editor views, not tracked simulator captures or Quest performance evidence. Click an image for full resolution.</p>
<p>Look for the wooded strip in the center: the candidate distributes small leaf clusters across the measured connected footprint and upper height envelope. It retains the original wood, replaces six owned crowns, and keeps the main composition present at distance. The rejected photographic/angular overlay is disabled in both variants.</p>
<p>COSTS_PLACEHOLDER No new texture atlas. Appearance and GPU acceptance remain pending. No worn test is requested with this desktop comparison.</p>
'''
    page=page.replace('COSTS_PLACEHOLDER',f'The candidate uses {evidence["coreTriangles"]:,} core triangles plus {evidence["nearTriangles"]:,} near-interior triangles, with one or two added draws.')
    if args.previous_root:
        page=page.replace('<h1>One connected grove, branch-sized foliage</h1>','<h1>Visible canopy improvement: previous pilot → fuller grove</h1>')
        page=page.replace('the candidate distributes small leaf clusters across the measured connected footprint and upper height envelope.','the new candidate adds a persistent middle foliage layer, selects broadleaf branch patches, reconstructs MSAA edge coverage, and retains texture shading across the measured connected footprint.')
    page += ''.join(sections)
    page += '<p><a href="visual_evidence.json">Raw geometry evidence</a> · <a href="standard_qa/visual_qa_summary.md">Standard QA report</a></p>'
    page += '<p>City of Boulder / NV5 / DRCOG 2020 LiDAR, CC BY 4.0. Modified for QuestFlight; no endorsement implied. Leaf artwork is reused from the existing product assets.</p></html>'
    (root / 'comparison.html').write_text(page, encoding='utf-8')
    print(root / 'comparison.html')

if __name__ == '__main__':
    main()
