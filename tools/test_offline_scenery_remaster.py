import json
from pathlib import Path
import tempfile
import unittest
import subprocess

from offline_scenery_remaster import checkpoint, digest, inside, restore_traffic, traffic_files, write_json, candidate_restore_files, restore_candidate


class CheckpointTests(unittest.TestCase):
    def git(self, repo, *args, data=None):
        return subprocess.check_output(["git", "-C", str(repo), *args], input=data)

    def prepare(self, root):
        repo = root / "repo"
        repo.mkdir()
        self.git(repo, "init", "-q")
        self.git(repo, "config", "core.autocrlf", "false")
        # Construct a baseline tree without creating a commit or touching user Git.
        for name in ("modified", "deleted", "renamed", "unstaged"):
            (repo / name).write_bytes(b"baseline\n")
        self.git(repo, "add", ".")
        tree = self.git(repo, "write-tree").decode().strip()
        # Unborn HEAD fixtures still exercise staged add and differing versions.
        return repo, root / "checkpoint", tree

    def test_staged_and_unstaged_versions_roundtrip_without_commits(self):
        with tempfile.TemporaryDirectory() as directory:
            repo, output, tree = self.prepare(Path(directory))
            (repo / "added").write_bytes(b"staged\n")
            (repo / "modified").write_bytes(b"staged modification\n")
            self.git(repo, "add", "added", "modified")
            self.git(repo, "rm", "--cached", "deleted")
            (repo / "deleted").unlink()
            self.git(repo, "mv", "renamed", "new name")
            (repo / "added").write_bytes(b"different worktree\n")
            (repo / "unstaged").write_bytes(b"unstaged modification\n")
            (repo / "untracked").write_bytes(b"untracked\n")
            checkpoint(repo, output, base_ref=tree)
            manifest = json.loads((output / "checkpoint.json").read_text())
            records = {item["path"]: item for item in manifest["files"]}
            self.assertEqual(records["added"]["index"]["sha256"], digest(output / "index/added"))
            self.assertNotEqual(records["added"]["index"]["sha256"], records["added"]["sha256"])
            # Restoring the index patch followed by the worktree patch reconstructs
            # both staged and unstaged state. Untracked bytes are restored separately.
            restored = Path(directory) / "restored"
            restored.mkdir()
            self.git(restored, "init", "-q")
            self.git(restored, "config", "core.autocrlf", "false")
            (restored / ".git/objects/info/alternates").write_bytes((str(repo / ".git/objects").replace("\\", "/") + "\n").encode())
            self.git(restored, "read-tree", "--reset", "-u", tree)
            self.git(restored, "apply", "--index", str(output / "staged.patch"))
            self.git(restored, "apply", str(output / "unstaged.patch"))
            (restored / "untracked").write_bytes((output / "files/untracked").read_bytes())
            self.assertEqual(self.git(repo, "diff", "--cached", "--binary", tree), self.git(restored, "diff", "--cached", "--binary", tree))
            self.assertEqual(self.git(repo, "diff", "--binary"), self.git(restored, "diff", "--binary"))
            self.assertEqual(self.git(repo, "status", "--porcelain"), self.git(restored, "status", "--porcelain"))

    def test_finalized_manifest_immutable_and_retry_idempotent(self):
        with tempfile.TemporaryDirectory() as directory:
            repo, output, _ = self.prepare(Path(directory))
            checkpoint(repo, output)
            original = (output / "checkpoint.json").read_bytes()
            checkpoint(repo, output)
            self.assertEqual(original, (output / "checkpoint.json").read_bytes())
            (repo / "new").write_bytes(b"later work")
            with self.assertRaises(RuntimeError):
                checkpoint(repo, output)
            self.assertEqual(original, (output / "checkpoint.json").read_bytes())
            self.assertFalse((output / "files/new").exists())

    def test_staged_delete_and_rename_from_recorded_base(self):
        with tempfile.TemporaryDirectory() as directory:
            repo, output, tree = self.prepare(Path(directory))
            self.git(repo, "rm", "--cached", "deleted")
            (repo / "deleted").unlink()
            self.git(repo, "mv", "renamed", "new name")
            checkpoint(repo, output, base_ref=tree)
            records = {item["path"]: item for item in json.loads((output / "checkpoint.json").read_text())["files"]}
            self.assertTrue(records["deleted"]["deleted"])
            self.assertEqual(records["new name"]["index"]["mode"], "100644")

    def test_in_repo_output_and_corrupt_payload_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            repo, output, _ = self.prepare(Path(directory))
            with self.assertRaises(ValueError):
                checkpoint(repo, repo / "backup")
            checkpoint(repo, output)
            (output / "index/modified").write_bytes(b"corrupt")
            with self.assertRaises(RuntimeError):
                checkpoint(repo, output)


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
