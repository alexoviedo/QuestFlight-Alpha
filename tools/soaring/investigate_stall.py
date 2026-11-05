"""Native SGS sustained-stall investigation, not aircraft-fidelity acceptance.

Uses the packaged production DLL and aircraft XML. Scripted control inputs are
test stimuli only; no attitude, velocity or force correction is applied.
"""
import argparse
import csv
import hashlib
import json
import math
from datetime import datetime, timezone
from pathlib import Path

from native_harness import Bridge, Session, TYPES, DATA, values
from run_aircraft_reference_cases import solve, load


def run(bridge, front, rear, brake, hold, recovery, dt):
    trim = solve(bridge, 45, front, rear, airbrake=brake, altitude=12000)
    if not trim['converged']:
        raise RuntimeError('Initial glide did not converge')
    rows = []
    onset = recovered = None
    with Session(bridge, 'qfl_sgs233_ref') as s:
        load(s, front, rear)
        s.reset(altitude=12000, cas=45, pitch=trim['alpha_deg']+trim['gamma_deg'], gamma=trim['gamma_deg'])
        s.send('set_glider_controls_v3', TYPES['glider_controls_v3'](brake, 0))
        for i in range(round(55/dt)):
            t = i*dt
            st = s.state()
            if onset is None and st.angle_of_attack_degrees >= 17:
                onset = t
            phase = 'trim' if t < 5 else 'entry'
            elevator = trim['elevator_native']
            if t >= 5:
                elevator = max(-1, trim['elevator_native'] - (t-5)*.04)
            if onset is not None:
                phase = 'held_stall'
                elevator = max(-1, trim['elevator_native'] - (onset-5)*.04)
                if t >= onset+hold:
                    phase = 'recover'
                    elevator = recovery
                    if recovered is None and st.angle_of_attack_degrees < 10:
                        recovered = t
                if recovered is not None and t >= recovered+1:
                    # Return to the original fixed trimmed elevator, without
                    # the attitude-holding autopilot used by the old test.
                    phase = 'trim_after_recovery'
                    elevator = trim['elevator_native']
            s.send('set_controls', TYPES['controls'](0, elevator, 0, 0, 0, 0, 0, 0, 0, 0))
            s.step(dt)
            if i % max(1, round(.1/dt)) == 0:
                row = values(s.state())
                diagnostic = s.diagnostics()
                row.update(phase=phase, elevator=elevator,
                           elevator_surface=diagnostic.elevator_position,
                           weight_lb=diagnostic.weight_pounds, cg_x_in=diagnostic.cg_x_inches,
                           pitch_moment_lbft=diagnostic.total_pitch_moment_pound_feet,
                           airbrake_position=s.b.read(s.h, 'get_air_data_v3', 'air_data_v3').airbrake_position)
                row['flight_path_deg'] = math.degrees(math.atan2(-row['velocity_down_feet_per_second'], math.hypot(row['velocity_north_feet_per_second'], row['velocity_east_feet_per_second'])))
                rows.append(row)
    held = [r for r in rows if r['phase'] == 'held_stall']
    level = [r for r in rows if abs(r['pitch_degrees']) <= 10]
    return dict(front_lb=front, rear_lb=rear, brake=brake, hold_seconds=hold,
                recovery_elevator=recovery, dt=dt, trim=trim,
                onset=onset, recovered=recovered,
                minimum_flight_path_deg=min(r['flight_path_deg'] for r in rows),
                max_alpha_deg=max(r['angle_of_attack_degrees'] for r in rows),
                maximum_sink_fpm=-min(r['vertical_speed_feet_per_minute'] for r in rows),
                alpha_exit_seconds=None if recovered is None else recovered-onset-hold,
                # Alpha exit is not a settled recovery or full-flight pass.
                held_max_alpha_deg=max((r['angle_of_attack_degrees'] for r in held), default=None),
                level_nose_minimum_flight_path_deg=min((r['flight_path_deg'] for r in level), default=None),
                finite=all(math.isfinite(v) for r in rows for v in r.values() if isinstance(v, (int, float))),
                rows=rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    bridge = Bridge()
    cases = []
    for front, rear in [(170, 170), (170, 0), (210, 220)]:
        for brake in [0, 1]:
            for hold in [0, 10]:
                for recovery in [0, .5]:
                    case = run(bridge, front, rear, brake, hold, recovery, 1/120)
                    rows = case.pop('rows')
                    name = f'stall_{front}_{rear}_{brake}_{hold}_{recovery}.csv'
                    with (args.output/name).open('w', newline='') as f:
                        writer = csv.DictWriter(f, fieldnames=rows[0].keys())
                        writer.writeheader()
                        writer.writerows(rows)
                    case['trace'] = name
                    cases.append(case)
                    print(json.dumps({k:v for k,v in case.items() if k != 'trim'}), flush=True)
    cadence = []
    for hz in [120, 240, 480]:
        case = run(bridge, 170, 170, 0, 10, .5, 1/hz)
        case.pop('rows')
        cadence.append(case)
    controls = []
    for trim in [-1, -.10626088, 0, .10626088, 1]:
        for elevator in [-1, -.5, 0, .5, 1]:
            with Session(bridge, 'qfl_sgs233_ref') as s:
                s.reset(altitude=12000, cas=45)
                s.send('set_controls', TYPES['controls'](0, elevator, 0, 0, 0, 0, trim, 0, 0, 0))
                s.step(1/120)
                controls.append(dict(native_elevator_command=elevator, native_trim_command=trim,
                                     surface_normalized=s.diagnostics().elevator_position))
    result = dict(scope=__doc__, utc=datetime.now(timezone.utc).isoformat(),
                  test_source_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                  native_sha256=hashlib.sha256(bridge.library.read_bytes()).hexdigest(),
                  model_sha256=hashlib.sha256((DATA/'aircraft/qfl_sgs233_ref/qfl_sgs233_ref.xml').read_bytes()).hexdigest(),
                  payload_sha256=hashlib.sha256((DATA/'runtime_hashes.sha256').read_bytes()).hexdigest(),
                  cases=cases, timestep_convergence=cadence, control_surface_sweep=controls,
                  interpretation='Diagnostic trajectories only. No aircraft-specific pass tolerance has been established; onset threshold 17 deg is a probe trigger, not a measured aircraft stall angle. No wind, tow, Unity input or headset frame scheduling is exercised.')
    (args.output/'stall_investigation.json').write_text(json.dumps(result, indent=2)+'\n')


if __name__ == '__main__':
    main()
