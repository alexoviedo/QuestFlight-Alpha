from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

import extract_from_rd as extractor  # noqa: E402


class ExtractionFixture:
    def __init__(self, root: Path, files: dict[str, bytes]) -> None:
        self.source = root / "source"
        self.destination = root / "product" / "QuestFlight"
        self.report = root / "reports"
        self.source.mkdir(parents=True)
        for relative, data in files.items():
            path = self.source / Path(*relative.split("/"))
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        self._git("init")
        self._git("config", "user.email", "extractor-tests@example.invalid")
        self._git("config", "user.name", "Extractor Tests")
        self._git("add", "-A")
        self._git("commit", "-m", "fixture")
        self.commit = self._git("rev-parse", "HEAD").strip()

    def _git(self, *args: str) -> str:
        result = subprocess.run(
            ["git", "-C", str(self.source), *args],
            text=True,
            capture_output=True,
            check=True,
        )
        return result.stdout

    def manifest(
        self,
        entries: list[dict[str, object]],
        *,
        dependency_roots: list[str] | None = None,
        rights_gated_entries: list[dict[str, object]] | None = None,
        product_replacements: list[dict[str, object]] | None = None,
    ) -> Path:
        path = self.source.parent / "manifest.json"
        value = {
            "schema_version": 1,
            "source_ref": self.commit,
            "source_project_prefix": "Project/",
            "entries": entries,
            "rights_gated_entries": rights_gated_entries or [],
            "product_replacements": product_replacements or [],
            "hard_exclusions": {
                "path_segments": ["Library", "Temp", "Obj", "Logs", "Builds"],
                "extensions": [".apk", ".log", ".mp4"],
                "case_insensitive_fragments": ["Usb2Ble", "JSBSim", "InputLab.unity"],
            },
            "dependency_roots": dependency_roots or [],
        }
        path.write_text(json.dumps(value), encoding="utf-8")
        return path

    def args(self, manifest: Path, *, copy: bool = False, report: Path | None = None) -> argparse.Namespace:
        return argparse.Namespace(
            source_repo=str(self.source),
            source_ref=self.commit,
            destination_project=str(self.destination),
            manifest=str(manifest),
            report_dir=str(report or self.report),
            dry_run=not copy,
            copy=copy,
        )


class ExtractFromRdTests(unittest.TestCase):
    def test_dry_run_writes_inventory_without_copying(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            fixture = ExtractionFixture(
                Path(temp),
                {
                    "Project/Assets/Thing.asset": b"value\n",
                    "Project/Assets/Thing.asset.meta": b"fileFormatVersion: 2\nguid: 11111111111111111111111111111111\n",
                },
            )
            manifest = fixture.manifest(
                [{"source": "Project/Assets/Thing.asset", "kind": "file", "classification": "approved_production", "group": "test"}]
            )
            self.assertEqual(extractor.execute(fixture.args(manifest)), 0)
            self.assertFalse((fixture.destination / "Assets/Thing.asset").exists())
            inventory = json.loads((fixture.report / "extraction_inventory.json").read_text(encoding="utf-8"))
            self.assertEqual(inventory["mode"], "dry-run")
            self.assertEqual(inventory["file_count"], 2)

    def test_copy_includes_paired_meta_and_preserves_guid(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            guid_line = b"fileFormatVersion: 2\nguid: 22222222222222222222222222222222\n"
            fixture = ExtractionFixture(
                Path(temp),
                {
                    "Project/Assets/Thing.asset": b"value\n",
                    "Project/Assets/Thing.asset.meta": guid_line,
                },
            )
            manifest = fixture.manifest(
                [{"source": "Project/Assets/Thing.asset", "kind": "file", "classification": "approved_production", "group": "test"}]
            )
            self.assertEqual(extractor.execute(fixture.args(manifest, copy=True)), 0)
            copied_meta = fixture.destination / "Assets/Thing.asset.meta"
            self.assertEqual(copied_meta.read_bytes(), guid_line)
            self.assertIn("22222222222222222222222222222222", copied_meta.read_text(encoding="utf-8"))

    def test_hard_excluded_path_is_refused(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            fixture = ExtractionFixture(
                Path(temp),
                {
                    "Project/Assets/Usb2BleInputMapper.cs": b"class Forbidden {}\n",
                    "Project/Assets/Usb2BleInputMapper.cs.meta": b"guid: 33333333333333333333333333333333\n",
                },
            )
            manifest = fixture.manifest(
                [{"source": "Project/Assets/Usb2BleInputMapper.cs", "kind": "file", "classification": "approved_production", "group": "test"}]
            )
            with self.assertRaisesRegex(extractor.ExtractionError, "forbidden source"):
                extractor.execute(fixture.args(manifest))

    def test_hash_report_is_repeatable_and_ignores_dirty_source_worktree(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            fixture = ExtractionFixture(
                Path(temp),
                {
                    "Project/Assets/Thing.asset": b"committed bytes\n",
                    "Project/Assets/Thing.asset.meta": b"guid: 44444444444444444444444444444444\n",
                },
            )
            manifest = fixture.manifest(
                [{"source": "Project/Assets/Thing.asset", "kind": "file", "classification": "approved_production", "group": "test"}]
            )
            report_one = Path(temp) / "report-one"
            report_two = Path(temp) / "report-two"
            self.assertEqual(extractor.execute(fixture.args(manifest, report=report_one)), 0)
            (fixture.source / "Project/Assets/Thing.asset").write_bytes(b"dirty bytes must be ignored\n")
            self.assertEqual(extractor.execute(fixture.args(manifest, report=report_two)), 0)
            first = (report_one / "extraction_inventory.json").read_bytes()
            second = (report_two / "extraction_inventory.json").read_bytes()
            self.assertEqual(first, second)
            inventory = json.loads(first)
            asset = next(item for item in inventory["files"] if item["source"].endswith("Thing.asset"))
            self.assertEqual(asset["sha256"], hashlib.sha256(b"committed bytes\n").hexdigest())

    def test_missing_allowlisted_file_fails_and_is_reported(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            fixture = ExtractionFixture(Path(temp), {"README": b"fixture\n"})
            manifest = fixture.manifest(
                [{"source": "Project/Assets/Missing.asset", "kind": "file", "classification": "approved_production", "group": "test"}]
            )
            self.assertEqual(extractor.execute(fixture.args(manifest)), 2)
            inventory = json.loads((fixture.report / "extraction_inventory.json").read_text(encoding="utf-8"))
            self.assertEqual(inventory["missing_allowlist_entries"], ["Project/Assets/Missing.asset"])

    def test_duplicate_destination_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            fixture = ExtractionFixture(
                Path(temp),
                {
                    "Project/Assets/A.asset": b"A\n",
                    "Project/Assets/A.asset.meta": b"guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n",
                    "Project/Assets/B.asset": b"B\n",
                    "Project/Assets/B.asset.meta": b"guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n",
                },
            )
            manifest = fixture.manifest(
                [
                    {"source": "Project/Assets/A.asset", "destination": "Assets/Same.asset", "kind": "file", "classification": "approved_production", "group": "test"},
                    {"source": "Project/Assets/B.asset", "destination": "Assets/Same.asset", "kind": "file", "classification": "approved_production", "group": "test"},
                ]
            )
            with self.assertRaisesRegex(extractor.ExtractionError, "Duplicate destination"):
                extractor.execute(fixture.args(manifest))

    def test_copy_is_idempotent_and_refuses_different_destination_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            fixture = ExtractionFixture(
                Path(temp),
                {
                    "Project/Assets/Thing.asset": b"original\n",
                    "Project/Assets/Thing.asset.meta": b"guid: cccccccccccccccccccccccccccccccc\n",
                },
            )
            manifest = fixture.manifest(
                [{"source": "Project/Assets/Thing.asset", "kind": "file", "classification": "approved_production", "group": "test"}]
            )
            self.assertEqual(extractor.execute(fixture.args(manifest, copy=True)), 0)
            self.assertEqual(extractor.execute(fixture.args(manifest, copy=True)), 0)
            inventory = json.loads((fixture.report / "extraction_inventory.json").read_text(encoding="utf-8"))
            self.assertTrue(all(item["status"] == "unchanged" for item in inventory["files"]))
            (fixture.destination / "Assets/Thing.asset").write_bytes(b"user-owned change\n")
            self.assertEqual(extractor.execute(fixture.args(manifest, copy=True)), 2)
            self.assertEqual((fixture.destination / "Assets/Thing.asset").read_bytes(), b"user-owned change\n")

    def test_rights_gate_allows_only_the_exact_enabled_imported_asset(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            fixture = ExtractionFixture(
                Path(temp),
                {
                    "Project/Assets/ImportedAssets/Verified/model.glb": b"verified model\n",
                    "Project/Assets/ImportedAssets/Verified/model.glb.meta": b"guid: dddddddddddddddddddddddddddddddd\n",
                    "Project/Assets/ImportedAssets/Other/model.glb": b"unclassified model\n",
                    "Project/Assets/ImportedAssets/Other/model.glb.meta": b"guid: eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n",
                },
            )
            gated_path = "Project/Assets/ImportedAssets/Verified/model.glb"
            rights_gate = [
                {
                    "source": gated_path,
                    "kind": "file",
                    "classification": "approved_third_party_cc_by_4_0",
                    "group": "rights",
                    "enabled": True,
                    "disabled_classification": "approved_local_development_only",
                }
            ]
            manifest_path = fixture.manifest([], rights_gated_entries=rights_gate)
            manifest = extractor.load_manifest(manifest_path)
            selections, missing = extractor.expand_allowlist(
                extractor.source_tree(fixture.source, fixture.commit), manifest
            )
            self.assertFalse(missing)
            selected_sources = {item.source for item in selections}
            self.assertIn(gated_path, selected_sources)
            self.assertNotIn("Project/Assets/ImportedAssets/Other/model.glb", selected_sources)
            self.assertEqual(
                extractor.classify_dependency(gated_path, manifest),
                "approved_third_party_cc_by_4_0",
            )
            self.assertEqual(
                extractor.classify_dependency("Project/Assets/ImportedAssets/Other/model.glb", manifest),
                "excluded_legacy_dependency",
            )

    def test_product_replacement_is_recorded_but_not_copied_or_conflicted(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            fixture = ExtractionFixture(Path(temp), {"scripts/build.ps1": b"legacy wrapper\n"})
            destination = fixture.destination.parent / "scripts/build.ps1"
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(b"clean product wrapper\n")
            manifest = fixture.manifest(
                [],
                product_replacements=[
                    {
                        "source": "scripts/build.ps1",
                        "destination": "scripts/build.ps1",
                        "classification": "product_replacement_not_copied",
                    }
                ],
            )
            self.assertEqual(extractor.execute(fixture.args(manifest)), 0)
            self.assertEqual(destination.read_bytes(), b"clean product wrapper\n")
            inventory = json.loads((fixture.report / "extraction_inventory.json").read_text(encoding="utf-8"))
            self.assertEqual(inventory["file_count"], 0)
            self.assertEqual(
                inventory["product_replacements_not_copied"][0]["classification"],
                "product_replacement_not_copied",
            )


if __name__ == "__main__":
    unittest.main()
