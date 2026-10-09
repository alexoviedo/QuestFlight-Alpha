"""Execute real current compiler/consumer with a separately labelled local freeze.

This creates no release witnesses, commits, approved source claims or shipping
traffic assets. The external registry binds the actual current toolchain/source
and synthetic bounded road inputs; the authoritative historical pins stay intact.
"""
from __future__ import annotations
import argparse
from copy import deepcopy
import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import subprocess
import sys


def load(path):
    spec = importlib.util.spec_from_file_location("diagnostic_current_traffic_gate", path)
    value = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = value
    spec.loader.exec_module(value)
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--acquire-naip", action="store_true")
    parser.add_argument("--reuse-naip-freeze", type=Path, help="Reuse exact already-acquired real public metadata; hashes are revalidated")
    args = parser.parse_args()
    origin = Path(__file__).resolve().parents[2]
    work = args.work_dir.resolve()
    if work.exists():
        raise ValueError("Use a new immutable diagnostic work directory")
    if work.is_relative_to(origin):
        raise ValueError("Diagnostic clone and raw inputs must remain external")
    work.mkdir(parents=True)
    repo = work / "repository"
    subprocess.run(["git", "clone", "--shared", "--no-checkout", str(origin), str(repo)], check=True, capture_output=True)
    source_registry = origin / "tools/traffic_v2/geospatial/source_registry.json"
    original_sha = hashlib.sha256(source_registry.read_bytes()).hexdigest()
    original = json.loads(source_registry.read_text())
    paths = {"tools/traffic_v2/geospatial/source_gate.py", "tools/traffic_v2/geospatial/source_registry.json", "tools/traffic_v2/validation/verify_current_source_gate.py", original["specification"]["path"], original["authority_model"]["benchmark_camera_contract"]["path"], original["toolchain"]["requirements_lock"]["path"]}
    for relative in paths:
        target = repo / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(origin / relative, target)
    gate = load(repo / original["toolchain"]["source_tool_path"])
    registry = deepcopy(original)
    registry["accepted_source_sha"] = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=origin, text=True).strip()
    registry["product_bindings"] = []
    registry["sources"] = []
    registry["document_sources"] = [s for s in registry["document_sources"] if s["id"] == gate.NAIP_AUTHORITY_SOURCE_ID]
    registry["license_snapshots"] = [s for s in registry["license_snapshots"] if s["id"] == gate.NAIP_LICENSE_SNAPSHOT_ID]
    registry["diagnostic_scope"] = "LOCAL_SYNTHETIC_COMPILER_REGRESSION_ONLY_NOT_SOURCE_OR_HUMAN_APPROVAL"
    # The historical Lead camera/screenshot contract is not relabelled. This
    # new test-owned contract contains actual generated diagnostic images and
    # synthetic view definitions; it cannot supply a human inspection witness.
    camera_path = repo / registry["authority_model"]["benchmark_camera_contract"]["path"]
    views = []
    for index in range(15):
        image_path = repo / "diagnostic_images" / f"fixture_{index:02d}.png"
        image_path.parent.mkdir(parents=True, exist_ok=True)
        gate.Image.new("RGB", (16,16), (index*13,20,80)).save(image_path, "PNG")
        views.append({"id":f"diagnostic-{index:02d}", "resolved_position_meters":{"x":0,"y":100,"z":0}, "field_of_view_degrees":60, "origin_inside_frozen_bounds":True, "road_candidate_set":"FULL_MAP_UNIVERSE_SUPERSET", "screenshot":gate.repository_file_record(image_path)})
    camera = {"schema":gate.BENCHMARK_CAMERA_CONTRACT_SCHEMA, "status":"FROZEN", "accepted_source_sha":registry["accepted_source_sha"], "diagnostic_scope":registry["diagnostic_scope"], "owner":"AUTOMATED_TEST_FIXTURE_NOT_LEAD", "view_count":15, "views":views, "coverage_contract":{"candidate_mode":"FULL_MAP_UNIVERSE_SUPERSET", "no_frustum_or_occlusion_exclusions":True, "bounds_xz_meters":registry["coordinate_contract"]["bounds_xz_meters"]}}
    camera_path.write_bytes(gate.canonical_json_bytes(camera))
    registry["authority_model"]["benchmark_camera_contract"]["sha256"] = gate.sha256_file(camera_path)
    actual = gate.live_toolchain_environment()
    for key in ("runtime_identity", "distributions", "native_libraries", "payload_identity"):
        registry["toolchain"][key] = actual[key]
    registry_path = repo / registry["toolchain"]["source_registry_path"]
    registry_path.write_bytes(gate.canonical_json_bytes(registry))
    gate.load_registry(registry_path)
    naip_root = work / "public_naip_metadata"
    naip_root.mkdir()
    licenses = {}
    naip_source = None
    naip_error = "NOT_REQUESTED"
    if args.reuse_naip_freeze:
        previous = args.reuse_naip_freeze.resolve()
        prior = json.loads(previous.read_text())
        for directory in ("metadata", "requests", "receipts"):
            shutil.copytree(previous.parent / directory, naip_root / directory)
        licenses = prior["license_snapshots"]
        naip_source = next(s for s in prior["sources"] if s["id"] == gate.NAIP_AUTHORITY_SOURCE_ID)
        gate.validate_frozen_naip_authority_closure(registry, naip_root, {"license_snapshots":licenses,"sources":[naip_source]})
        naip_error = None
    elif args.acquire_naip:
        try:
            licenses = gate.capture_license_snapshots(registry, naip_root)
            naip_source = gate.acquire_document_source(registry, registry["document_sources"][0], naip_root, licenses)
            gate.atomic_write_json(naip_root / "receipts" / (naip_source["id"] + ".json"), naip_source)
            naip_source["receipt_file"] = gate.file_record(naip_root / "receipts" / (naip_source["id"] + ".json"), naip_root)
            naip_error = None
        except Exception as error:
            naip_error = f"{type(error).__name__}: {error}"
    (work / "naip_acquisition_result.json").write_text(json.dumps({"naip_acquisition_error":naip_error,"authority_available":naip_source is not None},indent=2)+"\n")
    results = []
    for name, way_count in (("empty", 0), ("one-road", 1), ("conflicting-lanes", 2)):
        external = work / name
        external.mkdir()
        if naip_source:
            shutil.copytree(naip_root, external, dirs_exist_ok=True)
        if not licenses:
            # Explicit unavailable input, not a forged public-domain receipt.
            missing = external / "metadata" / "DIAGNOSTIC_LICENSE_UNAVAILABLE.txt"
            missing.parent.mkdir(parents=True, exist_ok=True)
            missing.write_text("DIAGNOSTIC ONLY: exact public NAIP license acquisition unavailable; no authority approval.\n")
            row = gate.file_record(missing, external)
            local_licenses = {gate.NAIP_LICENSE_SNAPSHOT_ID: {"receipt": {"response": row}, "required_text_present": False}}
        else:
            local_licenses = licenses
        elements = []
        if way_count:
            elements = [{"type": "node", "id": 1, "lat": 40.03936527, "lon": -105.22608958}, {"type": "node", "id": 2, "lat": 40.04026527, "lon": -105.22608958}]
            for index in range(way_count):
                elements.append({"type": "way", "id": 10 + index, "nodes": [1, 2], "tags": {"highway": "primary", "lanes": str(2 + index), "name": "DIAGNOSTIC SYNTHETIC ROAD", "oneway": "no", "bridge": "no", "tunnel": "no", "layer": "0"}})
        raw = external / "raw" / "diagnostic_osm.json"
        gate.atomic_write_json(raw, {"elements": elements, "diagnostic_scope": registry["diagnostic_scope"]})
        osm = {"id": registry["osm"]["id"], "raw": gate.file_record(raw, external), "license_partition": "SYNTHETIC-DIAGNOSTIC-NO-THIRD-PARTY-ROAD-DATA", "role": "bounded_fallback", "status": "DIAGNOSTIC_SYNTHETIC_FROZEN", "feature_count": way_count}
        toolchain = gate.validate_or_create_external_toolchain_manifest(registry, registry_path, external, allow_create=True)
        freeze = {"schema": "questflight.traffic-v2-geospatial-source-freeze.v1", "accepted_source_sha": registry["accepted_source_sha"], "accepted_product_bindings": [], "product_binding_warnings": [], "acquisition_completed_utc": gate.utc_now(), "coordinate_contract": registry["coordinate_contract"], "registry": gate.repository_file_record(registry_path), "tool": gate.repository_file_record(Path(gate.__file__)), "toolchain": toolchain, "specification": registry["specification"], "sources": sorted([osm] + ([naip_source] if naip_source else []), key=lambda s:s["id"]), "license_snapshots": local_licenses, "diagnostic_scope": registry["diagnostic_scope"]}
        gate.atomic_write_json(external / gate.FREEZE_NAME, freeze)
        verified = gate.validate_frozen_run_closure(registry_path, external)["verified_naip_authority"] if naip_source else None
        snapshots = []
        first_candidate = None
        for index in range(2):
            stage = external / ("first" if index == 0 else "rebuild")
            data, evidence, derivatives = (stage / value for value in ("data", "evidence", "work"))
            for directory in (data, evidence, derivatives): directory.mkdir(parents=True)
            with contextlib.redirect_stdout(io.StringIO()):
                manifest = gate._compile_artifacts_core(registry_path, external, data, evidence, verified_naip_authority=verified, derivative_run=derivatives, manifest_path=stage / "run_manifest.json", allow_candidate_creation=index == 0, candidate_event_utc=freeze["acquisition_completed_utc"])
            coverage = json.loads((data / "traffic_source_coverage.json").read_text())
            candidate = gate.approval_candidate_value(data, evidence / gate.EXPECTED_EXTERNAL_ARTIFACT, benchmark=coverage["benchmark_camera_coverage"], candidate_event_utc=freeze["acquisition_completed_utc"], source_freeze_sha256=gate.sha256_file(external / gate.FREEZE_NAME))
            if index == 0:
                first_candidate = candidate
                candidate_root = external / "approval_candidate"
                candidate_root.mkdir()
                for artifact in (*gate.EXPECTED_TEXT_ARTIFACTS, gate.EXPECTED_EXTERNAL_ARTIFACT):
                    shutil.copyfile((data if artifact in gate.EXPECTED_TEXT_ARTIFACTS else evidence) / artifact, candidate_root / artifact)
                gate.atomic_write_json(candidate_root / "candidate_manifest.json", candidate)
            assert candidate == first_candidate, "Candidate identity changed on rebuild"
            gate._validate_staged_compile_set(registry, external, derivatives, manifest, data, evidence, candidate, verified_naip_authority=verified)
            snapshots.append({artifact: ((data if artifact in gate.EXPECTED_TEXT_ARTIFACTS else evidence) / artifact).read_bytes() for artifact in (*gate.EXPECTED_TEXT_ARTIFACTS, gate.EXPECTED_EXTERNAL_ARTIFACT)})
        assert snapshots[0] == snapshots[1], "Real compiler five-artifact byte rebuild mismatch"
        negative_controls = []
        coverage_path = data / "traffic_source_coverage.json"
        original_coverage = coverage_path.read_bytes()
        for defect in ("omitted-inline-conflicts", "contradictory-support-requirement"):
            malformed = deepcopy(coverage)
            if defect == "omitted-inline-conflicts":
                del malformed["canonical_road_universe"]["conflicts"]
            else:
                malformed["canonical_road_universe"]["independent_lane_km_support"]["support_requirement"] = "ORIGINAL_INCOMPATIBLE_SUPPORT_REQUIREMENT"
            try:
                gate.atomic_write_json(coverage_path, malformed)
                altered_manifest, altered_candidate = deepcopy(manifest), deepcopy(candidate)
                changed_hash = gate.sha256_file(coverage_path)
                altered_manifest["outputs"][coverage_path.name] = {"bytes":coverage_path.stat().st_size,"sha256":changed_hash}
                altered_manifest["run_context"]["outputs"] = altered_manifest["outputs"]
                altered_candidate["artifact_sha256"][coverage_path.name] = changed_hash
                altered_manifest["approval_candidate"] = {"bytes":len(gate.canonical_json_bytes(altered_candidate)),"path":"approval_candidate/candidate_manifest.json","sha256":gate.sha256_bytes(gate.canonical_json_bytes(altered_candidate))}
                try:
                    gate._validate_staged_compile_set(registry, external, derivatives, altered_manifest, data, evidence, altered_candidate, verified_naip_authority=verified)
                except gate.GateError as error:
                    negative_controls.append({"defect":defect,"status":"REJECTED","reason":str(error),"artifact_hashes_rebound":True})
                else:
                    raise AssertionError(f"Original producer defect accepted: {defect}")
            finally:
                coverage_path.write_bytes(original_coverage)
        # The live verifier resolves canonical ledgers from their normative
        # external paths, just as normal staged promotion does. Copy only the
        # already independently validated diagnostic derivatives, never assets.
        shutil.copytree(external / "first" / "work", external, dirs_exist_ok=True)
        rows = coverage["canonical_road_universe"]
        assert isinstance(rows["conflicts"], list)
        assert rows["conflict_count"] == len(rows["conflicts"])
        if name == "conflicting-lanes": assert rows["conflict_count"] > 0, "Nonempty conflict fixture did not exercise conflicts"
        record = {"fixture": name, "synthetic_ways": way_count, "compiler_runs": 2, "staged_validator_runs": 2, "five_artifacts_byte_identical": True, "negative_controls":negative_controls, "canonical_record_count": rows["canonical_universe_record_count"], "conflict_count": rows["conflict_count"], "gate_status": coverage["gate_status"], "source_blockers": [r["id"] for r in coverage["authority_blockers"]], "artifacts": {n: {"sha256":hashlib.sha256(b).hexdigest(), "bytes":len(b)} for n,b in snapshots[0].items()}}
        context = gate.build_approval_validation_context(registry, registry_path, external, coverage["benchmark_camera_coverage"], gate.sha256_file(external / gate.FREEZE_NAME), candidate)
        argv = context["reproduction"]["argv"]
        process = subprocess.run(argv, cwd=repo, text=True, capture_output=True)
        (external / "verifier_stdout.txt").write_text(process.stdout)
        (external / "verifier_stderr.txt").write_text(process.stderr)
        record["verifier"] = {"argv":argv, "exit_code":process.returncode}
        if process.stdout.strip():
            result = json.loads(process.stdout)
            record["verifier"]["result"] = result
            witnesses = registry["authority_model"]["benchmark_approval_witnesses"]
            report = gate.repository_file_record(repo / witnesses["d_report"]["path"])
            verifier = gate.repository_file_record(repo / witnesses["d_verifier"]["path"])
            if name != "conflicting-lanes":
                assert process.returncode == 0 and result["decision"] == "PASS"
                gate.validate_d_result_semantics(registry, result, context=context, verifier=verifier, report=report)
                record["verifier"]["actual_consumer_result"] = "PASS"
            else:
                assert process.returncode == 2 and result["decision"] == "FAIL_CLOSED"
                try:
                    gate.validate_d_result_semantics(registry, result, context=context, verifier=verifier, report=report)
                except gate.GateError as error:
                    record["verifier"]["actual_consumer_result"] = "REJECTED_CONFLICTING_CANDIDATE: " + str(error)
                else:
                    raise AssertionError("Conflicting diagnostic candidate accepted")
        results.append(record)
    assert hashlib.sha256(source_registry.read_bytes()).hexdigest() == original_sha, "Authoritative registry changed"
    report = {"scope":registry["diagnostic_scope"], "historical_registry_unchanged_sha256":original_sha, "actual_current_tool_sha256":gate.sha256_file(Path(gate.__file__)), "diagnostic_registry_sha256":gate.sha256_file(registry_path), "work_dir":str(work), "naip_acquisition_error":naip_error, "human_review_status":"NOT_PERFORMED_OR_GENERATED", "results":results}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report,indent=2)+"\n")
    print(json.dumps(report,indent=2))
    return 0

if __name__ == "__main__": raise SystemExit(main())
