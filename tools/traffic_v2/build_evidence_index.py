#!/usr/bin/env python3
"""Build a complete, atomic SHA-256 index for an external Traffic V2 evidence run."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path


SCHEMA = "questflight.traffic-v2.evidence-index.v1"


def sha256_file(path: Path) -> tuple[int, str]:
    before = path.stat()
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    after = path.stat()
    if before.st_size != after.st_size or before.st_mtime_ns != after.st_mtime_ns:
        raise RuntimeError(f"Evidence changed while hashing: {path}")
    return after.st_size, digest.hexdigest()


def labelled_path(value: str) -> tuple[str, Path]:
    label, separator, raw_path = value.partition("=")
    if not separator or not label.strip() or not raw_path.strip():
        raise argparse.ArgumentTypeError("expected LABEL=PATH")
    return label.strip(), Path(raw_path).expanduser().resolve()


def file_record(path: Path, display_path: str, role: str) -> dict[str, object]:
    if not path.is_file():
        raise FileNotFoundError(f"Required {role} file is missing: {path}")
    if path.is_symlink():
        raise RuntimeError(f"Symlinks are not accepted as evidence: {path}")
    byte_count, digest = sha256_file(path)
    return {
        "role": role,
        "path": display_path.replace("\\", "/"),
        "bytes": byte_count,
        "sha256": digest,
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact-root", required=True, type=Path)
    parser.add_argument("--repo-root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--accepted-source-sha", required=True)
    parser.add_argument("--spec-sha256", required=True)
    parser.add_argument("--classification", required=True)
    parser.add_argument("--producer-command", action="append", default=[])
    parser.add_argument(
        "--source",
        action="append",
        default=[],
        help="Repository-relative source file to bind (repeatable).",
    )
    parser.add_argument(
        "--external",
        action="append",
        default=[],
        type=labelled_path,
        metavar="LABEL=PATH",
        help="Evidence file outside the artifact root (repeatable).",
    )
    parser.add_argument("--note", action="append", default=[])
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    artifact_root = args.artifact_root.resolve()
    repo_root = args.repo_root.resolve()
    output = args.output.resolve()
    if not artifact_root.is_dir():
        raise FileNotFoundError(f"Artifact root is missing: {artifact_root}")
    if not repo_root.is_dir():
        raise FileNotFoundError(f"Repository root is missing: {repo_root}")
    try:
        output.relative_to(artifact_root)
    except ValueError as error:
        raise RuntimeError("The evidence index must be written inside the artifact root.") from error

    artifact_records: list[dict[str, object]] = []
    for path in sorted(artifact_root.rglob("*"), key=lambda item: item.as_posix()):
        if not path.is_file() or path.resolve() == output:
            continue
        relative = path.relative_to(artifact_root).as_posix()
        if relative.endswith(".tmp"):
            continue
        artifact_records.append(file_record(path, relative, "run_artifact"))

    source_records: list[dict[str, object]] = []
    for relative_text in sorted(set(args.source)):
        relative = Path(relative_text)
        if relative.is_absolute() or ".." in relative.parts:
            raise RuntimeError(f"Source path must be repository-relative: {relative_text}")
        source_records.append(
            file_record(repo_root / relative, relative.as_posix(), "evidence_generator_source")
        )

    external_records: list[dict[str, object]] = []
    seen_labels: set[str] = set()
    for label, path in sorted(args.external, key=lambda item: item[0]):
        if label in seen_labels:
            raise RuntimeError(f"Duplicate external evidence label: {label}")
        seen_labels.add(label)
        record = file_record(path, str(path), "external_evidence")
        record["label"] = label
        external_records.append(record)

    document = {
        "schema": SCHEMA,
        "generated_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "accepted_source_sha": args.accepted_source_sha.lower(),
        "spec_sha256": args.spec_sha256.lower(),
        "classification": args.classification,
        "artifact_root": str(artifact_root),
        "producer_commands": args.producer_command,
        "notes": args.note,
        "artifact_count": len(artifact_records),
        "source_count": len(source_records),
        "external_evidence_count": len(external_records),
        "artifacts": artifact_records,
        "sources": source_records,
        "external_evidence": external_records,
    }

    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".tmp")
    encoded = (json.dumps(document, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    with temporary.open("wb") as stream:
        stream.write(encoded)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, output)
    print(
        json.dumps(
            {
                "output": str(output),
                "artifacts": len(artifact_records),
                "sources": len(source_records),
                "external_evidence": len(external_records),
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exception:  # concise fail-closed CLI boundary
        print(f"ERROR: {exception}", file=sys.stderr)
        raise SystemExit(1)
