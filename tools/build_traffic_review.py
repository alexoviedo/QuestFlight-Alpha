"""Build a local, synchronized before/after traffic frame viewer from QA reports."""
import argparse
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    reports = [json.loads((root / name / "traffic_v2_full_map_visual_qa.json").read_text())
               for name in ("endpoint_before", "endpoint_after")]
    groups = {}
    for before, after in zip(reports[0]["captures"], reports[1]["captures"], strict=True):
        assert before["viewId"] == after["viewId"]
        assert before["scheduleTimeSeconds"] == after["scheduleTimeSeconds"]
        groups.setdefault(before["viewId"], []).append({
            "time": before["scheduleTimeSeconds"],
            "before": Path(before["path"]).relative_to(root).as_posix(),
            "after": Path(after["path"]).relative_to(root).as_posix(),
            "near": after["nearCount"], "far": after["farCount"], "dropped": after["droppedCount"]})
    html = """<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>QuestFlight traffic continuity review</title>
<style>body{font:16px system-ui;background:#141719;color:#eee;margin:24px}h1{font-size:24px}
.pair{display:flex;gap:12px}.pair figure{margin:12px 0;flex:1;min-width:0}img{width:100%}
button,select{font:inherit;padding:8px}input{width:min(600px,80vw)}p{max-width:1000px;color:#bec6c9}
@media(max-width:800px){.pair{display:block}}</style>
<h1>Traffic continuity candidate</h1>
<p>Identical scene poses and schedule times. Before emulates the accepted traffic scheduling and visibility in the Editor.
After uses stable lanes, verified segment joins, endpoint coverage fading and distance hysteresis.
These are Editor captures, not Quest performance or Meta simulator acceptance.</p>
<select id="view"></select> <button id="play">Play</button>
<input id="frame" type="range" min="0" value="0"><span id="time"></span>
<div class="pair"><figure><figcaption>Before</figcaption><img id="before" alt="Before traffic changes"></figure>
<figure><figcaption>Candidate</figcaption><img id="after" alt="Candidate traffic"></figure></div>
<p id="counts"></p><p>Close endpoint: watch the small vehicle enter near the road bend. Remaining unconnected junctions still have vehicle lifecycles;
the coverage transition softens their appearance. Route-specific deck-height discontinuities remain a separate issue.
Standard QA currently passes 128 of 130 views; two draw-budget failures remain. Headset acceptance is pending.</p>
<script>const groups=DATA;const view=document.getElementById('view'),frame=document.getElementById('frame');
for(const name of ['endpoint_close','airport_boulevard','diagonal_highway']){let o=document.createElement('option');o.value=name;o.textContent=name.replaceAll('_',' ');view.append(o)}
function show(){const rows=groups[view.value];frame.max=rows.length-1;let r=rows[+frame.value];
document.getElementById('before').src=r.before;document.getElementById('after').src=r.after;
document.getElementById('time').textContent=' '+r.time.toFixed(3)+' s';
document.getElementById('counts').textContent='Candidate submissions: '+r.near+' near, '+r.far+' far, '+r.dropped+' dropped.'}
view.onchange=()=>{frame.value=0;show()};frame.oninput=show;let timer;
document.getElementById('play').onclick=function(){if(timer){clearInterval(timer);timer=null;this.textContent='Play'}
else{this.textContent='Pause';timer=setInterval(()=>{frame.value=(+frame.value+1)%groups[view.value].length;show()},1000/6)}};show();</script></html>"""
    (root / "traffic_review.html").write_text(html.replace("DATA", json.dumps(groups)), encoding="utf-8")
    print(root / "traffic_review.html")


if __name__ == "__main__":
    main()
