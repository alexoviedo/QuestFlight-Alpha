import json
from pathlib import Path
import tempfile
import unittest

from offline_scenery_remaster import digest, inside, restore_traffic, traffic_files, write_json, candidate_restore_files, restore_candidate


class RestoreTests(unittest.TestCase):
    def prepare(self, root):
        repo, output = root / "repo", root / "external"
        files = []
        for relative in traffic_files():
            target = repo / relative
            backup = output / "integration_backup" / relative.relative_to("QuestFlight")
            target.parent.mkdir(parents=True, exist_ok=True)
            backup.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(b"candidate")
            backup.write_bytes(b"original")
            files.append({"path": str(relative), "integrated_sha256": digest(target),
                          "original_sha256": digest(backup)})
        control = repo / "QuestFlight/Assets/Editor/SceneryRemaster/TrafficSelfOcclusion.json"
        write_json(control, {"entries": [{"name": "test"}]})
        write_json(output / "traffic_receipt.json", {"files": files, "control_sha256": digest(control)})
        return repo, output, control

    def test_restore_roundtrip(self):
        with tempfile.TemporaryDirectory() as directory:
            repo, output, control = self.prepare(Path(directory))
            self.assertEqual(restore_traffic(repo, output)["restored_files"], 9)
            for relative in traffic_files():
                self.assertEqual((repo / relative).read_bytes(), b"original")
            self.assertEqual(json.loads(control.read_text())["entries"], [])

    def test_changed_last_output_prevents_all_writes(self):
        with tempfile.TemporaryDirectory() as directory:
            repo, output, _ = self.prepare(Path(directory))
            (repo / traffic_files()[-1]).write_bytes(b"new user work")
            with self.assertRaises(ValueError):
                restore_traffic(repo, output)
            for relative in traffic_files()[:-1]:
                self.assertEqual((repo / relative).read_bytes(), b"candidate")

    def test_corrupt_backup_prevents_all_writes(self):
        with tempfile.TemporaryDirectory() as directory:
            repo, output, _ = self.prepare(Path(directory))
            backup = output / "integration_backup" / traffic_files()[-1].relative_to("QuestFlight")
            backup.write_bytes(b"corrupt")
            with self.assertRaises(ValueError):
                restore_traffic(repo, output)
            self.assertEqual((repo / traffic_files()[0]).read_bytes(), b"candidate")

    def test_path_escape_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError):
                inside(Path(directory), "../outside")


class CandidateRestoreTests(unittest.TestCase):
    def prepare(self, root):
        repo, output = root / "repo", root / "external"
        records = []
        for relative in candidate_restore_files():
            current, original = repo / relative, output / "candidate_restore_originals" / relative
            current.parent.mkdir(parents=True, exist_ok=True)
            original.parent.mkdir(parents=True, exist_ok=True)
            current.write_bytes(b"candidate")
            original.write_bytes(b"accepted")
            records.append({"path": str(relative), "candidate_sha256": digest(current), "original_sha256": digest(original)})
        write_json(output / "candidate_restore.json", {"files": records})
        return repo, output

    def test_full_scope_roundtrip(self):
        with tempfile.TemporaryDirectory() as directory:
            repo, output = self.prepare(Path(directory))
            self.assertEqual(restore_candidate(repo, output)["restored_files"], 13)
            for relative in candidate_restore_files():
                self.assertEqual((repo / relative).read_bytes(), b"accepted")

    def test_last_changed_file_prevents_every_write(self):
        with tempfile.TemporaryDirectory() as directory:
            repo, output = self.prepare(Path(directory))
            (repo / candidate_restore_files()[-1]).write_bytes(b"later user change")
            with self.assertRaises(ValueError):
                restore_candidate(repo, output)
            self.assertEqual((repo / candidate_restore_files()[0]).read_bytes(), b"candidate")


if __name__ == '__main__':
    unittest.main()
