"""Independent bounds/section checks and orthographic evidence for exported SGS.

Requires external JSON and reference image; never modifies the donor or source.
"""
import argparse
import json
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.collections import PolyCollection

p = argparse.ArgumentParser()
p.add_argument('geometry', type=Path)
p.add_argument('output', type=Path)
args = p.parse_args()
d = json.loads(args.geometry.read_text())
args.output.mkdir(parents=True, exist_ok=True)
xyz = lambda rows: np.array([[r[k] for k in ('x', 'y', 'z')] for r in rows])
parts = {p['name']: xyz(p['vertices']) for p in d['parts']}
all_vertices = np.concatenate(list(parts.values()))
normals = np.concatenate([xyz(p['normals']) for p in d['parts']])
span = float(np.ptp(all_vertices[:, 0]))
assert abs(span - 15.5448) < .005, span
assert np.isfinite(all_vertices).all() and np.isfinite(normals).all()
assert np.max(np.abs(np.linalg.norm(normals, axis=1) - 1)) < .001
assert d['triangles'] + 36 <= 8000
# Independent published dimension checks; not just a replay of builder metadata.
tail_span = float(np.ptp(parts['Elevator'][:, 0]))
assert abs(tail_span - 93 * .0254) < .005
rudder_thickness = float(np.ptp(parts['Rudder'][:, 0]))
elevator_thickness = float(np.ptp(parts['Elevator'][:, 1]))
assert rudder_thickness > .025 and elevator_thickness > .025
hull = parts['StaticHull']
section = hull[np.abs(hull[:, 0] - 1.6) < .0001]
assert np.ptp(section[:, 1]) > .12
for name in ('LeftAileron', 'RightAileron'):
    assert np.ptp(parts[name][:, 1]) > .05
report = dict(
    scope='Static mesh geometry only; not flight fidelity, human acceptance or Quest timing.',
    source='Simons1996 Schweizer-supplied three-view; UIUC V5 TRUE NACA43012A coordinates',
    trianglesIncludingYawString=d['triangles'] + 36,
    spanMeters=span, publishedSpanMeters=15.5448,
    horizontalTailSpanMeters=tail_span, publishedHorizontalTailSpanMeters=93 * .0254,
    rudderThicknessMeters=rudder_thickness, elevatorThicknessMeters=elevator_thickness,
    wingSectionVerticalExtentMeters=float(np.ptp(section[:, 1])),
    referenceTailSection='Estimated8% symmetric thickness; exact designation unverified',
    passed=True,
)
(args.output / 'geometry_checks.json').write_text(json.dumps(report, indent=2) + '\n')

# Actual exported triangles in three orthographic views, including glazing.
fig, axes = plt.subplots(3, 1, figsize=(14, 14), constrained_layout=True)
for ax, (label, u, v, sign) in zip(axes, [('Top', 0, 2, 1), ('Side, nose left', 2, 1, -1), ('Front', 0, 1, 1)]):
    for part in d['parts']:
        vertices = xyz(part['vertices'])
        triangles = np.array(part['triangles']).reshape(-1, 3)
        projected = vertices[:, [u, v]].copy()
        projected[:, 0] *= sign
        polys = projected[triangles]
        glass = part['name'] == 'Canopy'
        ax.add_collection(PolyCollection(polys, facecolors='#6baed6' if glass else '#c7ccd1', edgecolors='#2b526e' if glass else '#64717c', linewidths=.08, alpha=.45 if glass else .12))
    ax.autoscale_view()
    ax.set_aspect('equal')
    ax.grid(alpha=.2)
    ax.set_title(label)
    ax.set_xlabel('metres in aircraft reference frame')
fig.suptitle('SGS 2-33A exported geometry — orthographic inspection\n51 ft span; 93 in horizontal tail; shaped wing/control/tail sections. Tail section is estimated.')
fig.savefig(args.output / 'orthographic_geometry.png', dpi=150)
plt.close(fig)
print(json.dumps(report))
