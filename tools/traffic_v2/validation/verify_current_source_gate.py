#!/usr/bin/env python3
"""Current Phase-3 candidate verification. Human image inspection remains separate.

The historical verifier is retained unchanged. This entry point implements the
registry's current argv/result contract and derives every decision from live
bindings, independent ledger recomputation and exact frozen-input reproduction.
It never generates a Lead inspection or clears source-authority blockers.
"""
from __future__ import annotations

import argparse
import contextlib
import importlib.util
import io
import hashlib
import json
from pathlib import Path
import sys
import tempfile


def load_gate():
    path = Path(__file__).resolve().parents[1] / "geospatial/source_gate.py"
    spec = importlib.util.spec_from_file_location("questflight_current_traffic_gate", path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def parser():
    value = argparse.ArgumentParser(description=__doc__)
    for name in ("registry", "external-run", "data-dir", "evidence-dir"):
        value.add_argument("--" + name, type=Path, required=True)
    value.add_argument("--output-dir", type=Path, help="Diagnostic output only; not registered approval witnesses")
    return value


def verify(gate, args):
    registry = gate.load_registry(args.registry)
    required = registry["authority_model"]["benchmark_approval_contract"]["required_d_checks"]
    checks = {name: "FAIL" for name in required}
    errors = []
    context = {"bindings": {}, "reproduction": {"argv": [str(Path(sys.executable).resolve()), "-I", "-B", str(Path(__file__).resolve()), "--registry", str(args.registry.resolve()), "--external-run", str(args.external_run.resolve()), "--data-dir", str(args.data_dir.resolve()), "--evidence-dir", str(args.evidence_dir.resolve())], "cwd": str(gate.REPO_ROOT.resolve()), "exit_code": 2}}
    try:
        frozen = gate.validate_frozen_run_closure(args.registry, args.external_run)
        checks["toolchain_and_input_bindings"] = "PASS"
        before = gate.immutable_authority_snapshot(frozen, args.registry)
        external = frozen["external_run"]
        candidate = gate.read_json_object(external / "approval_candidate/candidate_manifest.json", "Current approval candidate", require_canonical=True)
        candidate_root = external / "approval_candidate"
        coverage = gate.read_json_object(candidate_root / "traffic_source_coverage.json", "Current candidate coverage", require_canonical=True)
        context = gate.build_approval_validation_context(registry, args.registry, external, coverage["benchmark_camera_coverage"], candidate["source_freeze_sha256"], candidate)
        checks["approval_candidate_immutability"] = "PASS"
        checks["five_artifact_hashes"] = "PASS"
        # Recompute denominators, conflicts, source dispositions and all 576
        # cells from record-level evidence, independently of producer summaries.
        missing = gate.read_json_object(candidate_root / "traffic_missing_roads.geojson", "Current missing-road ledger")
        gate._validate_staged_ledgers(registry, external, external, coverage, frozen["freeze"], missing.get("features") or [])
        checks["coverage_lane_kilometer_math"] = "PASS"
        checks["source_disposition_completeness"] = "PASS"
        with tempfile.TemporaryDirectory(prefix="questflight_current_traffic_verify_") as directory:
            scratch = Path(directory)
            data = scratch / "data"
            evidence = scratch / "evidence"
            work = scratch / "work"
            for path in (data, evidence, work):
                path.mkdir()
            with contextlib.redirect_stdout(io.StringIO()):
                manifest = gate._compile_artifacts_core(args.registry, external, data, evidence,
                    verified_naip_authority=frozen["verified_naip_authority"], allow_candidate_creation=False,
                    derivative_run=work, manifest_path=scratch / "run_manifest.json",
                    candidate_event_utc=candidate["candidate_event_utc"])
            gate._validate_staged_compile_set(registry, external, work, manifest, data, evidence, candidate,
                verified_naip_authority=frozen["verified_naip_authority"])
            for name in (*gate.EXPECTED_TEXT_ARTIFACTS, gate.EXPECTED_EXTERNAL_ARTIFACT):
                reproduced = (data if name in gate.EXPECTED_TEXT_ARTIFACTS else evidence) / name
                if reproduced.read_bytes() != (candidate_root / name).read_bytes():
                    raise gate.GateError(f"Current candidate reproduction differs: {name}")
        checks["canonical_universe_reproduction"] = "PASS"
        after = gate.validate_frozen_run_closure(args.registry, external)
        gate.require_same_authority_snapshot(before, gate.immutable_authority_snapshot(after, args.registry), allow_candidate_creation=False)
        # Non-camera source blockers cannot be waived by a successful rebuild.
        blockers = [row for row in coverage.get("authority_blockers") or [] if row.get("id") != "benchmark_camera_road_inventory_gap"]
        if blockers:
            raise gate.GateError("Current source-authority blockers remain: " + ",".join(str(row.get("id")) for row in blockers))
    except Exception as error:
        errors.append(f"{type(error).__name__}: {error}")
    passed = not errors and set(checks) == set(required) and all(value == "PASS" for value in checks.values())
    decision = "PASS" if passed else "FAIL_CLOSED"
    context["reproduction"]["exit_code"] = 0 if passed else 2
    return registry, context, checks, decision, errors


def main():
    args = parser().parse_args()
    try:
        gate = load_gate()
    except (ImportError, OSError) as error:
        # Isolated (-I) verification must not silently use a user's unrelated
        # geospatial packages. Missing pinned prerequisites remain a machine
        # failure with the same result contract, rather than a parser mismatch.
        gate = None
        registry = json.loads(args.registry.read_text(encoding="utf-8"))
        checks = {name: "FAIL" for name in registry["authority_model"]["benchmark_approval_contract"]["required_d_checks"]}
        context = {"bindings": {}, "reproduction": {"argv": [str(Path(sys.executable).resolve()), *sys.argv], "cwd": str(Path.cwd()), "exit_code": 2}}
        decision = "FAIL_CLOSED"
        errors = [f"Pinned verifier prerequisite unavailable: {type(error).__name__}: {error}"]
    else:
        registry, context, checks, decision, errors = verify(gate, args)
    repository_root = Path(__file__).resolve().parents[3]
    witnesses = registry["authority_model"]["benchmark_approval_witnesses"]
    if args.output_dir:
        args.output_dir.mkdir(parents=True, exist_ok=True)
        report_path = args.output_dir / "current_verification.md"
        result_path = args.output_dir / "current_verification.json"
    else:
        report_path = repository_root / witnesses["d_report"]["path"]
        result_path = repository_root / witnesses["d_result"]["path"]
        if not report_path.resolve().is_relative_to(repository_root) or not result_path.resolve().is_relative_to(repository_root):
            raise ValueError("Verifier witness paths escape repository")
    marker = registry["authority_model"]["benchmark_approval_contract"]["d_report_decision_marker"]
    text = "# Current Traffic Phase-3 verification\n\n" + f"<!-- {marker}: {decision} -->\n\n" + "\n".join(f"- {key}: {value}" for key, value in checks.items()) + "\n\nHuman full-resolution cell/image inspection is a separate required record.\n" + "\n".join(errors) + "\n"
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_bytes(text.encode("utf-8"))
    def record(path):
        return {"path": path.relative_to(repository_root).as_posix() if path.is_relative_to(repository_root) else str(path), "bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
    value = {"bindings": {**context["bindings"], "report": record(report_path), "verifier": record(Path(__file__).resolve())}, "checks": checks, "decision": decision, "reproduction": context["reproduction"], "schema": "questflight.traffic-v2-phase3-source-gate-verification-result.v1", "status": decision}
    result_path.parent.mkdir(parents=True, exist_ok=True)
    result_path.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(json.dumps(value, allow_nan=False))
    return 0 if decision == "PASS" else 2


if __name__ == "__main__":
    raise SystemExit(main())
