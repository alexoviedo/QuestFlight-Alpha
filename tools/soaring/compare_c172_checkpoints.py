"""Compare authored C172 checkpoint physics with the preserved ABI2 library.

Calm eight-second replay, neutral primary controls; no lesson-completion claim.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path
from native_harness import Bridge, Session, TYPES, values, ROOT


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--baseline', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    old, current = Bridge(args.baseline), Bridge()
    rows = []
    for path in sorted((ROOT / 'QuestFlight/Assets/Production/Training').glob('0*.asset')):
        text = path.read_text(encoding='utf-8-sig')
        def field(name):
            return float(re.search(r'^\s+' + name + r': ([^\r\n]+)', text, re.M)[1])
        state_max, diag_max = 0., 0.
        frames = []
        with Session(old) as a, Session(current) as b:
            for s in (a, b):
                s.send('set_mass_configuration', TYPES['mass_configuration'](170,170,0,0,0,130,130))
                s.send('reset', TYPES['initial_conditions'](
                    field('latitudeDegrees'), field('longitudeDegrees'),
                    field('altitudeMslMeters')/.3048, field('terrainElevationMslMeters')/.3048,
                    field('calibratedAirspeedKnots'), field('headingDegrees'), field('pitchDegrees'),
                    field('bankDegrees'), field('flightPathAngleDegrees'), int(field('engineRunning'))))
                brake = int(field('calibratedAirspeedKnots') < 1)
                s.send('set_controls', TYPES['controls'](0,0,0,field('initialThrottle'),1,0,-field('initialTrim'),field('initialFlaps'),brake,brake))
                s.send('set_atmosphere', TYPES['atmosphere'](0,0,0))
            for tick in range(960):
                a.step(); b.step()
                x, y = values(a.state()), values(b.state())
                u, v = values(a.diagnostics()), values(b.diagnostics())
                state_max = max(state_max, max(abs(x[k]-y[k]) for k in x))
                diag_max = max(diag_max, max(abs(u[k]-v[k]) for k in u))
                if tick % 120 == 119:
                    frames.append({'seconds':(tick+1)/120, 'baseline':x, 'current':y})
        rows.append(dict(lesson=path.stem, stateMaxAbsoluteDifference=state_max,
                         diagnosticsMaxAbsoluteDifference=diag_max, frames=frames))
    report = dict(scope='Calm native checkpoint regression; no guidance, terrain-patch, lesson completion or hardware timing acceptance.',
        baselineSha256=hashlib.sha256(args.baseline.read_bytes()).hexdigest(),
        currentSha256=hashlib.sha256(current.library.read_bytes()).hexdigest(), cases=rows,
        passed=all(r['stateMaxAbsoluteDifference'] < 1e-9 and r['diagnosticsMaxAbsoluteDifference'] < 1e-9 for r in rows))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k!='cases'}))
    print([(r['lesson'],r['stateMaxAbsoluteDifference'],r['diagnosticsMaxAbsoluteDifference']) for r in rows])
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
