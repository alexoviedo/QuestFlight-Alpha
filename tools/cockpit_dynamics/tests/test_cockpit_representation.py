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
    def setUp(self) -> None:
        # Each negative fixture starts from a passing, hydrated real-source contract.
        report = validate_manifest()
        self.assertTrue(report["passed"], report["errors"])

    def validate_changed(self, change):
        manifest = json.loads(DEFAULT_MANIFEST.read_text(encoding="utf-8"))
        change(manifest)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            path.write_text(json.dumps(manifest), encoding="utf-8")
            return validate_manifest(path)

    def test_reviewed_manifest_and_source_pass(self) -> None:
        report = validate_manifest()
        self.assertTrue(report["passed"], report["errors"])
        self.assertEqual(
            report["implementedTargetIds"],
            sorted(EXPECTED_IMPLEMENTED_IDS),
        )

    def test_false_mechanical_implementation_fails_closed(self) -> None:
        manifest = json.loads(DEFAULT_MANIFEST.read_text(encoding="utf-8"))
        manifest["implementedMotion"].append(
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

    def test_duplicate_classification_fails(self):
        report = self.validate_changed(lambda manifest: manifest["representedButBlocked"].append(
            {"id": "pilot_yoke", "reason": "Invalid duplicate fixture"}))
        self.assertFalse(report["passed"])
        self.assertIn("target ids must be disjoint across motion, blocked and excluded classifications", report["errors"])

    def test_missing_authority_fails(self):
        for change in [lambda manifest: manifest["authorityArchitecture"].pop("flightAuthority"),
                       lambda manifest: manifest["implementedMotion"][0].pop("channels")]:
            with self.subTest(change=change):
                report = self.validate_changed(change)
                self.assertFalse(report["passed"])
                self.assertTrue(any("authority" in error or "authoritative" in error for error in report["errors"]))

    def test_invalid_presentation_and_ids_fail_without_exception(self):
        for change in [lambda manifest: manifest["implementedAvionicsPresentation"]["items"].append("dmeDistance"),
                       lambda manifest: manifest["implementedMotion"][0].update(id=[]),
                       lambda manifest: manifest["authorityArchitecture"].update(authoritativeOrDeterministicChannels=[{}])]:
            with self.subTest(change=change):
                self.assertFalse(self.validate_changed(change)["passed"])


if __name__ == "__main__":
    unittest.main()
