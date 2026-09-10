"""Build a local before/after review from inspected, unsaved Editor captures."""
import argparse
import csv
import html
import os
from pathlib import Path


def build(root: Path, photograph: Path, baseline_dir="canopy_baseline", candidate_dir="canopy_candidate", uv_repair=False) -> Path:
    def read(name):
        with (root / name / "canopy_summary.csv").open(encoding="utf-8-sig") as stream:
            return list(csv.DictReader(stream))

    before, after = read(baseline_dir), read(candidate_dir)
    sections = []
    for base, candidate in zip(before, after, strict=True):
        height = base["camera_local_y"]
        if height != candidate["camera_local_y"]:
            raise ValueError("Capture heights differ")
        for field in ("global_near", "global_middle", "global_far", "global_shrubs"):
            if base[field] != candidate[field]:
                raise ValueError(f"Global selection cost changed: {height} {field}")
        selected = lambda row: sum(int(row[key]) for key in ("selected_near", "selected_middle", "selected_far"))
        paths = [f"{variant}/settled_benchmark/screenshots/settled_{height}_left.png"
                 for variant in (baseline_dir, candidate_dir)]
        for path in paths:
            if not (root / path).is_file():
                raise FileNotFoundError(root / path)
        sections.append(f'''<section><h2>Camera local Y: {height} m</h2>
<p>Selected crowns in the reference patch: <b>{selected(base)} → {selected(candidate)}</b> of {base['source_crowns_in_patch']} authored crowns.
Global far selection remains {base['global_far']}. Counts do not measure visible crown pixels.</p>
<div class="comparison"><img src="{paths[0]}" alt="Baseline left eye">
<img class="candidate" src="{paths[1]}" alt="Candidate left eye">
<span class="before">Baseline</span><span class="after">Candidate</span></div>
<label>Reveal candidate <input type="range" min="0" max="100" value="50" aria-label="Reveal candidate at {height} metres"
oninput="this.closest('section').querySelector('.candidate').style.clipPath='inset(0 0 0 '+this.value+'%)'"></label>
<p><a href="{paths[0]}">Open baseline</a> · <a href="{paths[1]}">Open candidate</a> ·
<a href="{candidate_dir}/settled_benchmark/screenshots/settled_{height}_right.png">Candidate right eye</a></p></section>''')
    photo = html.escape(os.path.relpath(photograph, root).replace(os.sep, "/"), quote=True)
    content = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Valmont canopy selection benchmark</title><style>
body{max-width:1280px;margin:32px auto;padding:0 20px;background:#151a20;color:#e5e9ef;font:16px/1.5 system-ui}
h1,h2{line-height:1.2}a{color:#90ccff}section{margin:40px 0}img{max-width:100%;display:block}
.comparison{position:relative;aspect-ratio:16/9}.comparison img{position:absolute;width:100%;height:100%}
.candidate{clip-path:inset(0 0 0 50%)}.before,.after{position:absolute;top:10px;background:#111c;padding:4px 10px}
.before{left:10px}.after{right:10px}input{width:60%;vertical-align:middle}.notice{padding:16px;background:#293746}
</style><h1>Valmont canopy selection benchmark</h1><p class="notice"><b>Unsaved Editor experiment; not installed on Quest.</b>
Only additional near-page crowns inside the photographed patch become eligible for existing far-proxy slots.
Meshes, materials, global tier populations and shipping selection remain unchanged. More local crowns displace other far selections;
page-crossing stability, displaced coverage and Quest timing still need validation.</p>
<p>Left-eye before/after captures use the same pose and settled selection. These are Editor fallback imagery, not Meta simulator stereo or Android residency evidence.
Camera height is local Y, not a verified AGL measurement.</p>'''
    if uv_repair:
        content = content.replace("Valmont canopy selection benchmark", "Far-canopy UV repair")
        start = content.index('<p class="notice">')
        end = content.index('</p>', start) + 4
        content = content[:start] + '''<p class="notice"><b>Shader repair candidate; physical acceptance remains open.</b>
The old shader wrapped both authored far-card U endpoints (6 and 7) to zero before interpolation.
The repair preserves the complete atlas frame. Selection, tree meshes, materials and tier populations are unchanged.
Rendered coverage and device cost can change; equal geometry is not proof of equal GPU time.</p>''' + content[end:]
    content += "".join(sections)
    content += f'''<section><h2>Actual City of Boulder 2020 photography</h2><p>Existing project reference crop: local X −884..−135 m, Z −914..−494 m.
This overhead photograph is a geographic reference, not a registered overlay for the oblique captures. Source attribution and rights are retained in the original visual-fidelity audit.</p>
<img src="{photo}" alt="Actual City2020 Valmont canopy and roof reference"></section></html>'''
    destination = root / "canopy_review.html"
    destination.write_text(content, encoding="utf-8")
    return destination


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("photograph", type=Path)
    parser.add_argument("--baseline-dir", default="canopy_baseline")
    parser.add_argument("--candidate-dir", default="canopy_candidate")
    parser.add_argument("--uv-repair", action="store_true")
    args = parser.parse_args()
    print(build(args.root.resolve(), args.photograph.resolve(), args.baseline_dir, args.candidate_dir, args.uv_repair))
