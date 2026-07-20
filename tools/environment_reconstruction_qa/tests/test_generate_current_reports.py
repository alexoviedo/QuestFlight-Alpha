from __future__ import annotations

import json
import pathlib
import subprocess
import sys
import tempfile
import unittest


TOOL_ROOT = pathlib.Path(__file__).resolve().parents[1]
REPO_ROOT = TOOL_ROOT.parents[1]
GENERATOR = TOOL_ROOT / "generate_current_reports.py"
ARTIFACT_ROOT = pathlib.Path(r"C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\environment_reconstruction_v2_20260717_015625")
REPORT_NAMES = (
    "production_terrain_surface_report.json",
    "placement_validation.json",
    "building_coverage_report.json",
    "vegetation_coverage_report.json",
    "water_coverage_report.json",
    "hlod_validation_report.json",
    "performance_budget_report.json",
)


class CurrentReportAdapterTests(unittest.TestCase):
    def generate(self, output: pathlib.Path, artifact_root: pathlib.Path | None = None) -> None:
        arguments = [sys.executable, str(GENERATOR), "--repo-root", str(REPO_ROOT), "--output-dir", str(output)]
        if artifact_root is not None:
            arguments.extend(("--artifact-root", str(artifact_root)))
        subprocess.run(
            arguments,
            check=True,
            capture_output=True,
            text=True,
        )

    def test_reports_are_deterministic_and_explicitly_incomplete(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            base = pathlib.Path(temporary)
            first, second = base / "first", base / "second"
            self.generate(first)
            self.generate(second)
            for name in REPORT_NAMES:
                self.assertEqual((first / name).read_bytes(), (second / name).read_bytes(), name)
                report = json.loads((first / name).read_text(encoding="utf-8"))
                self.assertFalse(report["evidenceComplete"], name)
                self.assertTrue(report["evidenceBlockers"], name)

    def test_water_inventory_is_current_and_building_requires_matching_unity_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            output = pathlib.Path(temporary)
            self.generate(output)
            water = json.loads((output / "water_coverage_report.json").read_text(encoding="utf-8"))
            building = json.loads((output / "building_coverage_report.json").read_text(encoding="utf-8"))
            self.assertEqual(800, len(water["features"]))
            self.assertEqual(277, sum(1 for item in water["features"] if item["represented"]))
            self.assertEqual(0, water["missingSignificantFeatureCount"])
            self.assertIn("WATER_ORTHOPHOTO_RECONCILIATION_UNPROVEN", {item["code"] for item in water["evidenceBlockers"]})
            self.assertIn("BUILDING_EVIDENCE_INCOMPLETE", {item["code"] for item in building["evidenceBlockers"]})
            mid = next(item for item in building["zones"] if item["id"] == "mid_4_6km")
            self.assertTrue(mid["coverageEvidenceComplete"])

    @unittest.skipUnless(ARTIFACT_ROOT.is_dir(), "current Reconstruction V2 artifact root is unavailable")
    def test_serialized_evidence_is_deterministic_and_bound(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            base = pathlib.Path(temporary)
            first, second = base / "first", base / "second"
            self.generate(first, ARTIFACT_ROOT)
            self.generate(second, ARTIFACT_ROOT)
            for name in REPORT_NAMES:
                self.assertEqual((first / name).read_bytes(), (second / name).read_bytes(), name)
            placement = json.loads((first / "placement_validation.json").read_text(encoding="utf-8"))
            building = json.loads((first / "building_coverage_report.json").read_text(encoding="utf-8"))
            hlod = json.loads((first / "hlod_validation_report.json").read_text(encoding="utf-8"))
            provenance = json.loads((first / "environment_reconstruction_adapter_provenance.json").read_text(encoding="utf-8"))
            self.assertEqual(28336, placement["aggregateValidation"]["fullInventoryRowCount"])
            self.assertEqual(277, len(placement["representedWaterStableIds"]))
            self.assertTrue(building["evidenceComplete"])
            self.assertEqual(25, len(hlod["cells"]))
            self.assertTrue(hlod["evidenceComplete"])
            input_paths = {item["path"].replace("\\", "/") for item in provenance["inputs"]}
            self.assertTrue(any("placement_validation_final/placement_validation.json" in path for path in input_paths))
            self.assertTrue(any("hlod_serialized_audit_final/hlod_render_budget_report.json" in path for path in input_paths))
            self.assertFalse(any("placement_validation_after_root_seating" in path for path in input_paths))

            gate_arguments = [
                sys.executable,
                str(TOOL_ROOT / "validate_environment_reconstruction.py"),
            ]
            for category, name in zip(
                ("surface", "placement", "building", "vegetation", "water", "hlod", "budgets"),
                REPORT_NAMES,
                strict=True,
            ):
                gate_arguments.extend((f"--{category}", str(first / name)))
            gate_arguments.extend(("--output-dir", str(base / "gate")))
            subprocess.run(gate_arguments, check=False, capture_output=True, text=True)
            summary = json.loads((base / "gate" / "environment_reconstruction_qa_report.json").read_text(encoding="utf-8"))
            self.assertNotIn("WATER_PLACEMENT_MISMATCH", {item["code"] for item in summary["issues"]})


if __name__ == "__main__":
    unittest.main()
