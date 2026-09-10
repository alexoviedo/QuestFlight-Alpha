"""Record Git identity plus exact dirty/untracked inputs without creating a commit."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    def git(*values):
        return subprocess.check_output(["git", "-C", str(args.root), *values])
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "source_changes.patch").write_bytes(git("diff", "HEAD", "--binary"))
    names = git("diff", "HEAD", "--name-only", "-z").split(b"\0")
    names += git("ls-files", "--others", "--exclude-standard", "-z").split(b"\0")
    records = []
    with zipfile.ZipFile(args.output / "source_changes.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for raw in sorted(set(names) - {b""}):
            name = raw.decode("utf-8")
            path = args.root / name
            if not path.is_file():
                records.append({"path": name, "deleted": True})
                continue
            with path.open("rb") as stream:
                sha = hashlib.file_digest(stream, "sha256").hexdigest()
            records.append({"path": name, "sha256": sha})
            archive.write(path, name)
    data = {"head": git("rev-parse", "HEAD").decode().strip(), "changes": records,
            "scope": "Git source plus dirty/untracked files; ignored authored payload is inventoried separately"}
    (args.output / "source_identity.json").write_text(json.dumps(data, indent=2) + "\n")


if __name__ == "__main__":
    main()
