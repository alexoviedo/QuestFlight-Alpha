#!/usr/bin/env python3
"""Validate QuestFlight flight-dynamics evidence without promoting missing evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
from collections import Counter
from pathlib import Path
from typing import Any


CONTRACT_VERSION = "questflight-flight-dynamics-evidence-v3"
VALID_STATUSES = {"Pass", "Fail", "Unsupported", "Unproven", "Error"}
REQUIRED_NATIVE_SCENARIOS = {
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
    _require(isinstance(config_hash, str) and re.fullmatch(r"[0-9a-fA-F]{64}", config_hash) is not None,
             "Effective production config SHA-256 is missing or malformed.")

    native = data.get("nativeInputs")
    _require(isinstance(native, dict) and native.get("status") in VALID_STATUSES,
             "nativeInputs identity/status is missing.")
    if native["status"] == "Pass":
        _require(native.get("apiVersion") == 2, "Native identity does not identify API2.")
        _require(re.fullmatch(r"[0-9a-fA-F]{40}", str(native.get("sourceCommit", ""))) is not None,
                 "Native evidence source commit is missing or malformed.")
        for name in ("sourceInputsSha256", "bridgeSha256", "jsbsimSha256", "payloadSha256", "aircraftXmlSha256"):
            _require(re.fullmatch(r"[0-9a-fA-F]{64}", str(native.get(name, ""))) is not None,
                     f"Native evidence {name} is missing or malformed.")
        _require(bool(native.get("unityVersion")) and bool(native.get("platform")), "Native tool/platform identity is missing.")
    if require_pass:
        _require(native["status"] == "Pass", "Strict pass requested, but actual native identity is not Pass.")
    definitions = data.get("scenarioInputs")
    _require(isinstance(definitions, list), "Actual scenario input definitions are missing.")
    for definition in definitions:
        _require(isinstance(definition, dict) and bool(definition.get("scenario")), "Malformed scenario input identity.")
        raw = definition.get("definitionJson")
        _require(isinstance(raw, str), "Scenario definition JSON is missing.")
        try:
            parsed = json.loads(raw)
        except json.JSONDecodeError as exc:
            raise EvidenceError("Scenario definition JSON is malformed.") from exc
        _require(isinstance(parsed, dict) and all(name in parsed for name in ("initial", "mass", "windNedFps", "initialControls", "stepSeconds", "durationSeconds", "resolvedWeightPounds", "resolvedCgXInches")),
                 "Scenario actual initial/mass/wind/control/time/CG definition is incomplete.")
        def finite_numbers(value: Any) -> bool:
            if isinstance(value, dict):
                return all(finite_numbers(item) for item in value.values())
            return isinstance(value, (float, int)) and not isinstance(value, bool) and math.isfinite(value)
        _require(finite_numbers(parsed), "Scenario definition has non-finite or nonnumeric actual inputs.")
        _require(hashlib.sha256(raw.encode("utf-8")).hexdigest() == definition.get("definitionSha256"),
                 "Scenario input identity does not match its definition.")

    scenarios = data.get("scenarios")
    _require(isinstance(scenarios, list) and scenarios, "No scenario evidence was written.")
    counts: Counter[str] = Counter()
    native_ids: set[str] = set()
    seen: set[tuple[str, str]] = set()
    for index, scenario in enumerate(scenarios):
        _require(isinstance(scenario, dict), f"Scenario {index} is not an object.")
        backend = scenario.get("backend")
        scenario_id = scenario.get("scenarioId")
        status = scenario.get("status")
        _require(backend == "JSBSimNativeDevelopment", f"Scenario {index} does not use the sole native authority.")
        _require(isinstance(scenario_id, str) and scenario_id, f"Scenario {index} has no scenarioId.")
        _require(status in VALID_STATUSES, f"{backend}/{scenario_id} has invalid status {status!r}.")
        key = (backend, scenario_id)
        _require(key not in seen, f"Duplicate scenario evidence: {backend}/{scenario_id}.")
        seen.add(key)
        counts[status] += 1
        if backend == "JSBSimNativeDevelopment":
            native_ids.add(scenario_id)

        metrics = scenario.get("metrics")
        _require(isinstance(metrics, list) and metrics, f"{backend}/{scenario_id} has no metrics.")
        for metric in metrics:
            _require(isinstance(metric, dict), f"{backend}/{scenario_id} has a malformed metric.")
            metric_status = metric.get("status")
            if require_pass:
                _require(metric_status == "Pass", f"{backend}/{scenario_id}/{metric.get('id')} is not Pass.")
            _require(metric_status in VALID_STATUSES,
                     f"{backend}/{scenario_id}/{metric.get('id')} has invalid status {metric_status!r}.")

        resolved = next(value for value in ("Error", "Fail", "Unproven", "Unsupported", "Pass") if any(metric["status"] == value for metric in metrics))
        _require(status == resolved, f"{backend}/{scenario_id} summary contradicts its metrics ({resolved}).")

    unavailable = native_ids == {"native_backend_availability"} and scenarios[0]["status"] in {"Unsupported", "Error"}
    _require(unavailable or bool(definitions), "Native scenario run has no actual input identities.")
    missing = set() if unavailable and not require_pass else REQUIRED_NATIVE_SCENARIOS - native_ids
    _require(not missing, "Missing native scenarios: " + ", ".join(sorted(missing)))
    if require_pass:
        _require(config["status"] == "Pass", "Strict pass requested, but productionConfig is not Pass.")
        non_pass = counts["Fail"] + counts["Unsupported"] + counts["Unproven"] + counts["Error"]
        _require(non_pass == 0, f"Strict pass requested, but {non_pass} scenarios are not Pass.")
    return counts


def compare_inputs(baseline: dict[str, Any], current: dict[str, Any]) -> None:
    """Reject incompatible regression inputs and name the mismatch explicitly."""
    validate(baseline)
    validate(current)
    _require(baseline["nativeInputs"]["status"] == current["nativeInputs"]["status"] == "Pass",
             "A baseline comparison requires verified native input identities.")
    for key in ("sourceCommit", "sourceInputsSha256", "unityVersion", "platform", "apiVersion", "bridgeSha256", "jsbsimSha256", "payloadSha256", "aircraftXmlSha256"):
        _require(baseline["nativeInputs"].get(key) == current["nativeInputs"].get(key), f"Incompatible baseline nativeInputs.{key}.")
    old = [(row["scenario"], row["definitionSha256"]) for row in baseline["scenarioInputs"]]
    new = [(row["scenario"], row["definitionSha256"]) for row in current["scenarioInputs"]]
    _require(old == new, "Incompatible baseline scenario initial/mass/wind/control/time/CG definitions.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--require-pass", action="store_true")
    parser.add_argument("--baseline", type=Path, help="Reject a comparison whose native/scenario input identities differ.")
    args = parser.parse_args()
    try:
        data = json.loads(args.input.read_text(encoding="utf-8-sig"))
        counts = validate(data, require_pass=args.require_pass)
        if args.baseline:
            compare_inputs(json.loads(args.baseline.read_text(encoding="utf-8-sig")), data)
    except (OSError, json.JSONDecodeError, EvidenceError) as exc:
        print(f"INVALID: {exc}")
        return 1
    ordered = ", ".join(f"{name}={counts[name]}" for name in sorted(VALID_STATUSES))
    print(f"VALID {CONTRACT_VERSION}: {ordered}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
