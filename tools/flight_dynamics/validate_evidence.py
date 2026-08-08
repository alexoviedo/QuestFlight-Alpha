#!/usr/bin/env python3
"""Validate QuestFlight flight-dynamics evidence without promoting missing evidence."""

from __future__ import annotations

import argparse
import json
import math
from collections import Counter
from pathlib import Path
from typing import Any


CONTRACT_VERSION = "questflight-flight-dynamics-evidence-v1"
VALID_STATUSES = {"Pass", "Fail", "Unsupported", "Unproven", "Error"}
REQUIRED_PROTOTYPE_SCENARIOS = {
    "control_sign_pulse_doublet",
    "neutral_trim",
    "beta_yaw_damping",
    "coordinated_turn",
    "slow_flight_stall_recovery",
    "power_off_glide",
    "flap_response",
    "takeoff_climb",
    "approach_landing",
    "ground_steering",
    "mass_cg_matrix",
    "wind_matrix",
}


class EvidenceError(ValueError):
    pass


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise EvidenceError(message)


def validate(data: dict[str, Any], require_pass: bool = False) -> Counter[str]:
    _require(data.get("contractVersion") == CONTRACT_VERSION, "Unexpected contractVersion.")
    step = data.get("fixedStepSeconds")
    _require(isinstance(step, (int, float)) and math.isclose(step, 1.0 / 120.0, abs_tol=1e-12),
             "Evidence is not sampled from the required 120 Hz integration step.")

    config = data.get("productionConfig")
    _require(isinstance(config, dict), "productionConfig is missing.")
    _require(config.get("status") in VALID_STATUSES, "productionConfig has an invalid status.")
    config_hash = config.get("effectiveParameterSha256", "")
    _require(isinstance(config_hash, str) and len(config_hash) == 64,
             "Effective production config SHA-256 is missing or malformed.")

    scenarios = data.get("scenarios")
    _require(isinstance(scenarios, list) and scenarios, "No scenario evidence was written.")
    counts: Counter[str] = Counter()
    prototype_ids: set[str] = set()
    seen: set[tuple[str, str]] = set()
    for index, scenario in enumerate(scenarios):
        _require(isinstance(scenario, dict), f"Scenario {index} is not an object.")
        backend = scenario.get("backend")
        scenario_id = scenario.get("scenarioId")
        status = scenario.get("status")
        _require(isinstance(backend, str) and backend, f"Scenario {index} has no backend.")
        _require(isinstance(scenario_id, str) and scenario_id, f"Scenario {index} has no scenarioId.")
        _require(status in VALID_STATUSES, f"{backend}/{scenario_id} has invalid status {status!r}.")
        key = (backend, scenario_id)
        _require(key not in seen, f"Duplicate scenario evidence: {backend}/{scenario_id}.")
        seen.add(key)
        counts[status] += 1
        if backend == "UnityPrototype":
            prototype_ids.add(scenario_id)

        metrics = scenario.get("metrics")
        _require(isinstance(metrics, list) and metrics, f"{backend}/{scenario_id} has no metrics.")
        for metric in metrics:
            _require(isinstance(metric, dict), f"{backend}/{scenario_id} has a malformed metric.")
            metric_status = metric.get("status")
            _require(metric_status in VALID_STATUSES,
                     f"{backend}/{scenario_id}/{metric.get('id')} has invalid status {metric_status!r}.")

    missing = REQUIRED_PROTOTYPE_SCENARIOS - prototype_ids
    _require(not missing, "Missing prototype scenarios: " + ", ".join(sorted(missing)))
    if require_pass:
        non_pass = counts["Fail"] + counts["Unsupported"] + counts["Unproven"] + counts["Error"]
        _require(non_pass == 0, f"Strict pass requested, but {non_pass} scenarios are not Pass.")
    return counts


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--require-pass", action="store_true")
    args = parser.parse_args()
    try:
        data = json.loads(args.input.read_text(encoding="utf-8"))
        counts = validate(data, require_pass=args.require_pass)
    except (OSError, json.JSONDecodeError, EvidenceError) as exc:
        print(f"INVALID: {exc}")
        return 1
    ordered = ", ".join(f"{name}={counts[name]}" for name in sorted(VALID_STATUSES))
    print(f"VALID {CONTRACT_VERSION}: {ordered}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
