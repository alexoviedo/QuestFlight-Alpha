from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from tools.cockpit_dynamics.validate_cockpit_representation import (
    DEFAULT_MANIFEST,
    EXPECTED_IMPLEMENTED_IDS,
    validate_manifest,
)


class CockpitRepresentationManifestTests(unittest.TestCase):
    def test_reviewed_manifest_and_source_pass(self) -> None:
        report = validate_manifest()
        self.assertTrue(report["passed"], report["errors"])
        self.assertEqual(
            report["implementedTargetIds"],
            sorted(EXPECTED_IMPLEMENTED_IDS),
        )

    def test_false_mechanical_implementation_fails_closed(self) -> None:
        manifest = json.loads(DEFAULT_MANIFEST.read_text(encoding="utf-8"))
        manifest["implementedTargets"].append(
            {
                "id": "fixed_gear_wheel_roll",
                "authority": ["groundSpeed"],
                "owner": "NonexistentMechanicalAnimator",
                "renderCostDelta": 0,
            }
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            path.write_text(json.dumps(manifest), encoding="utf-8")
            report = validate_manifest(path)
        self.assertFalse(report["passed"])
        self.assertIn(
            "implemented target ids must exactly match the serialized reviewed baseline",
            report["errors"],
        )

    def test_source_hash_drift_fails_closed(self) -> None:
        manifest = json.loads(DEFAULT_MANIFEST.read_text(encoding="utf-8"))
        manifest["sourceSha256"] = "0" * 64
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            path.write_text(json.dumps(manifest), encoding="utf-8")
            report = validate_manifest(path)
        self.assertFalse(report["passed"])
        self.assertIn(
            "source SHA256 does not match the reviewed asset",
            report["errors"],
        )


if __name__ == "__main__":
    unittest.main()
