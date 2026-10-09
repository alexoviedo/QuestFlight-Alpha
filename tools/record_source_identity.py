"""Record Git identity plus exact dirty/untracked inputs without creating a commit."""
import argparse
import hashlib
import json
import re
from pathlib import Path
import subprocess
import zipfile

IGNORED_AUTHORED_ROOTS = (
    "QuestFlight/Assets/StreamingAssets/QuestFlightAerialClipmap",
    "QuestFlight/Assets/StreamingAssets/QuestFlightBuildingClipmap",
    "QuestFlight/Assets/StreamingAssets/QuestFlightBranchGroves",
    "QuestFlight/Assets/Generated/FullMapAerialClipmap",
    "QuestFlight/Assets/Generated/FullMapBuildingClipmap",
)
IGNORED_EXCLUDED_COMPONENTS = {
    "Library", "Temp", "Obj", "Logs", "Build", "Builds", "PackageCache",
    "__pycache__", ".pytest_cache", "RawDownloads", "SourceArchives",
    "LocalOnly", "Restricted", "Uncleared", "RemasterCompressionSmoke",
}
IGNORED_EXCLUDED_SUFFIXES = {".zip", ".7z", ".rar", ".apk", ".aab", ".keystore", ".jks", ".pyc", ".pyo", ".log"}


def ignored_authored_inventory(root, output, git):
    selectors = [*IGNORED_AUTHORED_ROOTS, *(p + ".meta" for p in IGNORED_AUTHORED_ROOTS), "QuestFlight/Assets/Generated.meta"]
    names = git("ls-files", "--others", "--ignored", "--exclude-standard", "-z", "--", *selectors).split(b"\0")
    rows = []
    for raw in sorted(set(names) - {b""}):
        name = raw.decode("utf-8")
        relative = Path(name)
        if any(p.startswith("__") or p in IGNORED_EXCLUDED_COMPONENTS for p in relative.parts) or relative.suffix.lower() in IGNORED_EXCLUDED_SUFFIXES:
            continue
        path = (root / name).resolve()
        if not path.is_relative_to(root.resolve()):
            raise ValueError("Ignored authored input escapes selected checkout.")
        if path.is_relative_to(output.resolve()):
            continue
        before = path.stat()
        with path.open("rb") as stream:
            sha = hashlib.file_digest(stream, "sha256").hexdigest()
        after = path.stat()
        if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
            raise ValueError("Ignored authored input changed while recording identity.")
        rows.append({"path": name, "bytes": after.st_size, "sha256": sha})
    return rows


def require_sanitized_development_settings(path):
    if not path.is_file():
        return
    text = path.read_text(encoding="utf-8-sig")
    # Horizontal whitespace only: YAML's next property must never become the
    # value of an empty credential property on the preceding line.
    for match in re.finditer(r"(?im)^[ \t]*\w*(?:token|password|secret|apikey)\w*:[ \t]*([^\r\n]*)$", text):
        value = match.group(1).strip()
        if value and value not in ('""', "''", "~", "null", "Null", "NULL") and not value.startswith("#"):
            raise ValueError("Sanitize credential-bearing development settings before recording source evidence.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    def git(*values):
        return subprocess.check_output(["git", "-C", str(args.root), *values])
    credential_settings = "QuestFlight/Assets/Resources/DevAgentSettings.asset"
    require_sanitized_development_settings(args.root / credential_settings)
    ignored_inputs = ignored_authored_inventory(args.root, args.output, git)
    args.output.mkdir(parents=True, exist_ok=True)
    # Current bytes remain in the source archive after sanitation. Exclude the
    # original committed credential from patch context/deletion lines.
    (args.output / "source_changes.patch").write_bytes(git("diff", "HEAD", "--binary", "--", ".", f":(exclude){credential_settings}"))
    names = git("diff", "HEAD", "--name-only", "-z").split(b"\0")
    names += git("ls-files", "--others", "--exclude-standard", "-z").split(b"\0")
    records = []
    with zipfile.ZipFile(args.output / "source_changes.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for raw in sorted(set(names) - {b""}):
            name = raw.decode("utf-8")
            if name.startswith(".questflight_codex_audit/"):
                continue
            path = args.root / name
            if path.resolve().is_relative_to(args.output.resolve()):
                continue
            if not path.is_file():
                records.append({"path": name, "deleted": True})
                continue
            if name == credential_settings:
                require_sanitized_development_settings(path)
            with path.open("rb") as stream:
                sha = hashlib.file_digest(stream, "sha256").hexdigest()
            records.append({"path": name, "sha256": sha})
            archive.write(path, name)
    data = {"head": git("rev-parse", "HEAD").decode().strip(), "changes": records,
            "scope": "Git source plus dirty/untracked files; exact required ignored scenery inputs are hash-bound without archiving raw assets",
            "ignoredAuthoredInputs": ignored_inputs,
            "ignoredAuthoredInputRoots": list(IGNORED_AUTHORED_ROOTS),
            "patchExclusions": [credential_settings],
            "patchExclusionReason": "Sanitized current settings are archived; original committed credentials are never copied into patch context."}
    (args.output / "source_identity.json").write_text(json.dumps(data, indent=2) + "\n")


if __name__ == "__main__":
    main()
