"""Focused current traffic contracts; uses installed geospatial libraries and stdlib runner."""
import argparse
from copy import deepcopy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    value = importlib.util.module_from_spec(spec)
    sys.modules[name] = value
    spec.loader.exec_module(value)
    return value


gate = module("remediation_source_gate", "tools/traffic_v2/geospatial/source_gate.py")
full = module("remediation_full_map", "tools/traffic_v2/performance/compile_full_map_schedule.py")


class CurrentTrafficContractTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.external = self.root / "external"
        self.external.mkdir()
        self.registry_path = ROOT / "tools/traffic_v2/geospatial/source_registry.json"
        self.registry = json.loads(self.registry_path.read_text(encoding="utf-8"))
        self.freeze = {"registry": {"path": "tools/traffic_v2/geospatial/source_registry.json", "bytes": self.registry_path.stat().st_size, "sha256": full.sha256_file(self.registry_path)}, "coordinate_contract": self.registry["coordinate_contract"], "sources": []}
        for source in full.COUNT_SOURCE_IDS:
            count = self.external / "raw" / source / "oid_1.json"
            count.parent.mkdir(parents=True)
            value = {"features": [{"attributes": {"AADT": 100, "TRAFFIC_COUNT": 100, "OBJECTID": 1, "STREET_NAME": "Fixture"}, "geometry": {"x": -105.22608958, "y": 40.03936527, "paths": [[[-105.22608958, 40.03936527], [-105.226, 40.04]]]}}]}
            full.write_json(count, value)
            self.freeze["sources"].append({"id": source, "license_partition": "fixture-license-" + source, "pages": [{"receipt": {"payload": {"path": count.relative_to(self.external).as_posix(), "bytes": count.stat().st_size, "sha256": full.sha256_file(count)}}}]})

    def validate(self):
        return full.validate_frozen_count_inputs(ROOT, self.external, self.freeze, self.registry)

    def test_count_identity_and_original_aadt_tamper_rejected(self):
        first = self.validate()
        observations, tree = full.load_count_observations(self.external, self.registry["coordinate_contract"]["projection"])
        self.assertEqual(2, len(observations))
        self.assertEqual([100, 100], [row["aadt"] for row in observations])
        self.assertEqual(first, self.validate())
        path = self.external / "raw/cdot_traffic_counts/oid_1.json"
        text = path.read_text().replace('"AADT":100', '"AADT":9999')
        self.assertIn('"AADT":9999', text)
        path.write_text(text)
        with self.assertRaisesRegex(full.FullMapCompileError, "bytes/hash"):
            self.validate()

    def test_missing_added_counts_projection_registry_and_licenses_rejected(self):
        for mutation in ("added", "missing", "projection", "registry", "license"):
            with self.subTest(mutation=mutation):
                saved = deepcopy(self.freeze)
                path = self.external / "raw/cdot_traffic_counts/oid_1.json"
                payload = path.read_bytes()
                extra = path.with_name("oid_extra.json")
                try:
                    if mutation == "added": extra.write_bytes(payload)
                    elif mutation == "missing": path.unlink()
                    elif mutation == "projection": self.freeze["coordinate_contract"] = {"projection": "EPSG:4326"}
                    elif mutation == "registry": self.freeze["registry"]["sha256"] = "0" * 64
                    else: del self.freeze["sources"][0]["license_partition"]
                    with self.assertRaises(full.FullMapCompileError): self.validate()
                finally:
                    self.freeze = saved
                    path.write_bytes(payload)
                    if extra.exists(): extra.unlink()

    def test_partial_inventory_never_claims576_and_real_inventory_is_required(self):
        self.assertFalse(full.complete_cell_inventory({}))
        bounds = {"cell_count": 576, "evaluated_cell_count": 576, "status": "PASS", "exact_bounds_xz_meters": [-12000., -12000., 12000., 12000.], "cells": [{"column": col, "row": row, "evaluated": True} for row in range(24) for col in range(24)]}
        self.assertTrue(full.complete_cell_inventory({"bounds_evaluation": bounds}))
        for mutation in ("missing", "duplicate", "unevaluated", "wrongbounds"):
            copy = deepcopy(bounds)
            if mutation == "missing": copy["cells"].pop()
            elif mutation == "duplicate": copy["cells"][-1] = copy["cells"][0]
            elif mutation == "unevaluated": copy["cells"][3]["evaluated"] = False
            else: copy["exact_bounds_xz_meters"] = [0, 0, 24000, 24000]
            self.assertFalse(full.complete_cell_inventory({"bounds_evaluation": copy}), mutation)

    def test_actual_producer_support_summary_matches_independent_consumer(self):
        for count in (0, 1):
            records = [{"canonical_segment_id": "fixture:1", "source_id": "fixture", "source_record_id": "1", "source_record_part": 0, "geometry": {"type": "LineString", "coordinates": [[0, 0], [1000, 0]]}, "lane_count": 2, "road_class": "primary"}] if count else []
            sources = {"fixture": [{**record, "_geometry": gate.shape(record["geometry"])} for record in records]}
            ledger, summary, conflicts = gate.independent_lane_km_support(self.registry, records, sources)
            self.assertEqual(summary, gate._derive_independent_support_summary(ledger))
            self.assertEqual([], conflicts)
            self.assertEqual(count * 2., summary["by_class"]["primary"]["canonical_lane_kilometer_denominator"])
            if ledger:
                forged = deepcopy(ledger)
                forged[0]["canonical_lane_kilometer_denominator"] += 1
                self.assertNotEqual(summary, gate._derive_independent_support_summary(forged))

    def test_actual_producer_coverage_retains_empty_and_nonempty_conflict_contract(self):
        for conflicts in ([], [{"id": "fixture_conflict", "canonical_segment_id": "one"}]):
            actual = gate.canonical_coverage_value({"conflicts": conflicts, "conflict_count": len(conflicts), "unmatched_reference_observations": [1]})
            self.assertEqual(conflicts, actual["conflicts"])
            self.assertEqual(len(conflicts), actual["conflict_count"])
            self.assertNotIn("unmatched_reference_observations", actual)

    def test_real_one_road_candidate_roundtrip_is_byte_identical_and_never_full_review(self):
        self.freeze["sources"].append({"id": "city_street_centerlines", "license_partition": "fixture-city-license"})
        freeze_path = self.external / "freeze_manifest.json"
        full.write_json(freeze_path, self.freeze)
        freeze_sha = full.sha256_file(freeze_path)
        canonical = self.root / "canonical.jsonl"
        full.write_jsonl(canonical, [{"canonical_segment_id": "city:1:0", "correspondence_semantics": {"carriageway": {"value": "SINGLE_CENTERLINE"}, "direction": {"value": "BIDIRECTIONAL"}}, "eligibility_status_or_exclusion_reason": "ELIGIBLE", "geometry": {"coordinates": [[0., 0.], [0., 100.]], "type": "LineString"}, "lane_count": 2, "license_partition": "fixture-city-license", "road_class": "secondary", "source_id": "city_street_centerlines", "source_record_id": "1", "source_record_part": 0}])
        canonical_sha = full.sha256_file(canonical)
        outputs = []
        for name in ("first", "second"):
            path = self.root / name
            report = full.compile_full_map(ROOT, self.external, path, freeze_sha, canonical_checkpoint_candidate=canonical, expected_canonical_checkpoint_sha256=canonical_sha)
            self.assertFalse(report["checks"]["all_576_source_cells_evaluated"])
            manifest = json.loads((path / "traffic_v2_full_map_manifest.json").read_text())
            self.assertEqual(1, manifest["metrics"]["corridor_count"])
            self.assertEqual(self.validate()["sha256"], manifest["frozen_count_inputs"]["sha256"])
            outputs.append({file.name: file.read_bytes() for file in path.iterdir()})
        self.assertEqual(outputs[0], outputs[1])
        count = self.external / "raw/cdot_traffic_counts/oid_1.json"
        count.write_bytes(count.read_bytes().replace(b'"AADT":100', b'"AADT":9999'))
        failed = self.root / "tampered_output"
        with self.assertRaisesRegex(full.FullMapCompileError, "bytes/hash"):
            full.compile_full_map(ROOT, self.external, failed, freeze_sha, canonical_checkpoint_candidate=canonical, expected_canonical_checkpoint_sha256=canonical_sha)
        self.assertFalse(failed.exists())

    def test_registered_actual_verifier_argv_executes_and_failsclosed_on_missingfreeze(self):
        script = ROOT / self.registry["authority_model"]["benchmark_approval_witnesses"]["d_verifier"]["path"]
        args = [sys.executable, "-I", "-B", str(script), "--registry", str(self.registry_path), "--external-run", str(self.external), "--data-dir", str(ROOT / "docs/data/traffic_v2"), "--evidence-dir", str(self.external / "evidence"), "--output-dir", str(self.root / "diagnostics")]
        process = subprocess.run(args, cwd=ROOT, capture_output=True, text=True)
        self.assertEqual(2, process.returncode, process.stderr)
        result = json.loads(process.stdout)
        self.assertEqual(gate.D_RESULT_SCHEMA, result["schema"])
        self.assertEqual({"bindings", "checks", "decision", "reproduction", "schema", "status"}, set(result))
        self.assertEqual("FAIL_CLOSED", result["decision"])
        self.assertTrue(any(value != "PASS" for value in result["checks"].values()))
        self.assertFalse((self.external / "approval_candidate").exists())
        report = self.root / "diagnostics/current_verification.md"
        self.assertTrue(any(error in report.read_text() for error in ("accepted source", "Pinned verifier prerequisite unavailable", "Missing source freeze")), report.read_text())


if __name__ == "__main__": unittest.main()
