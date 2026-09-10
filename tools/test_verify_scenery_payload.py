import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from verify_scenery_payload import inventory, verify


class PayloadTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for folder, manifest, key, field in (
            ("QuestFlightAerialClipmap", "clipmap_manifest.json", "pages", "bundle_name"),
            ("QuestFlightBuildingClipmap", "bundle_index.json", "bundles", "name"),
        ):
            path = self.root / folder
            path.mkdir()
            (path / manifest).write_text(json.dumps({key: [{field: "page"}]}))
            (path / "page").write_bytes(b"accepted scenery")
        (self.root / "QuestFlightBuildingClipmap/build_stamp.txt").write_text("stamp")

    def package(self, omitted=None, changed=None):
        apk = self.root / "fixture.apk"
        with zipfile.ZipFile(apk, "w") as archive:
            for path in self.root.glob("*/*"):
                name = "assets/" + path.relative_to(self.root).as_posix()
                if name != omitted:
                    archive.writestr(name, b"changed scenery!" if name == changed else path.read_bytes())
        return apk

    def test_complete_payload_passes(self):
        self.assertTrue(verify(self.package(), inventory(self.root))["passed"])

    def test_missing_aerial_page_fails(self):
        with self.assertRaisesRegex(ValueError, "missing or wrong size"):
            verify(self.package(omitted="assets/QuestFlightAerialClipmap/page"), inventory(self.root))

    def test_changed_same_length_bundle_fails(self):
        with self.assertRaisesRegex(ValueError, "checksum mismatch"):
            verify(self.package(changed="assets/QuestFlightBuildingClipmap/page"), inventory(self.root))

    def test_manifest_missing_source_page_fails_before_build(self):
        (self.root / "QuestFlightAerialClipmap/page").unlink()
        with self.assertRaisesRegex(ValueError, "Missing or empty"):
            inventory(self.root)


if __name__ == "__main__":
    unittest.main()
