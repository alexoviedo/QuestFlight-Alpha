#!/usr/bin/env python3
"""Deterministically extract the allowlisted Quest product closure from Git.

The extractor never reads source asset bytes from the mutable working tree.  It
resolves the requested source ref to the manifest's pinned commit, expands the
allowlist against that commit's tree, and reads each byte with ``git show``.
"""

from __future__ import annotations

import argparse
import csv
import fnmatch
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from typing import Any, Iterable


GUID_REFERENCE = re.compile(rb"guid:\s*([0-9a-fA-F]{32})")
GUID_GREP_LINE = re.compile(
    r"^[0-9a-fA-F]+:(.*?):\d+:guid:\s*([0-9a-fA-F]{32})$"
)


class ExtractionError(RuntimeError):
    """An extraction contract was violated."""


@dataclass(frozen=True)
class Selection:
    source: str
    destination: str
    classification: str
    group: str
    paired_meta: bool


@dataclass(frozen=True)
class InventoryRecord:
    source: str
    destination: str
    classification: str
    group: str
    paired_meta: bool
    size_bytes: int
    sha256: str
    status: str

    def as_dict(self) -> dict[str, Any]:
        return {
            "source": self.source,
            "destination": self.destination,
            "classification": self.classification,
            "group": self.group,
            "paired_meta": self.paired_meta,
            "size_bytes": self.size_bytes,
            "sha256": self.sha256,
            "status": self.status,
        }


def git(source_repo: Path, *args: str, text: bool = False) -> bytes | str:
    command = ["git", "-C", str(source_repo), *args]
    result = subprocess.run(command, capture_output=True, check=False)
    if result.returncode != 0:
        stderr = result.stderr.decode("utf-8", errors="replace").strip()
        raise ExtractionError(f"Git command failed ({' '.join(command)}): {stderr}")
    if text:
        return result.stdout.decode("utf-8", errors="strict")
    return result.stdout


def normalize_repo_path(value: str, label: str) -> str:
    value = value.replace("\\", "/").strip("/")
    path = PurePosixPath(value)
    if not value or path.is_absolute() or ".." in path.parts:
        raise ExtractionError(f"Invalid {label}: {value!r}")
    return path.as_posix()


def load_manifest(path: Path) -> dict[str, Any]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ExtractionError(f"Cannot read extraction manifest {path}: {exc}") from exc
    if data.get("schema_version") != 1:
        raise ExtractionError("Only extraction manifest schema_version 1 is supported")
    if not isinstance(data.get("entries"), list):
        raise ExtractionError("Manifest entries must be an array")
    pinned = str(data.get("source_ref", "")).lower()
    if not re.fullmatch(r"[0-9a-f]{40}", pinned):
        raise ExtractionError("Manifest source_ref must be an immutable 40-character SHA")
    return data


def resolve_source_commit(source_repo: Path, requested_ref: str, manifest: dict[str, Any]) -> str:
    if not (source_repo / ".git").exists():
        raise ExtractionError(f"Source repository is not a Git working tree: {source_repo}")
    resolved = str(git(source_repo, "rev-parse", "--verify", f"{requested_ref}^{{commit}}", text=True)).strip().lower()
    pinned = str(manifest["source_ref"]).lower()
    if resolved != pinned:
        raise ExtractionError(
            f"Source ref resolves to {resolved}, but manifest pins {pinned}; extraction refused"
        )
    return resolved


def source_tree(source_repo: Path, commit: str) -> list[str]:
    output = str(git(source_repo, "ls-tree", "-r", "--name-only", commit, text=True))
    return sorted(line.strip() for line in output.splitlines() if line.strip())


def pattern_matches(path: str, pattern: str, kind: str) -> bool:
    if kind == "file":
        return path == pattern
    if kind != "glob":
        raise ExtractionError(f"Unsupported manifest entry kind {kind!r}")
    if pattern.endswith("/**"):
        prefix = pattern[:-3].rstrip("/")
        return path.startswith(prefix + "/")
    return fnmatch.fnmatchcase(path, pattern)


def effective_entries(manifest: dict[str, Any]) -> list[dict[str, Any]]:
    entries = [dict(entry) for entry in manifest["entries"]]
    entries.extend(
        dict(entry)
        for entry in manifest.get("rights_gated_entries", [])
        if bool(entry.get("enabled", False))
    )
    return entries


def destination_for(
    source: str,
    entry: dict[str, Any],
    project_prefix: str,
) -> str:
    explicit = entry.get("destination")
    if explicit:
        if entry.get("kind", "file") != "file":
            raise ExtractionError("Explicit destination is supported only for file entries")
        return normalize_repo_path(str(explicit), "destination")
    root = entry.get("destination_root", "project")
    if root == "repository":
        return normalize_repo_path(source, "repository destination")
    if root != "project":
        raise ExtractionError(f"Unknown destination_root {root!r}")
    if not source.startswith(project_prefix):
        raise ExtractionError(
            f"Project entry {source!r} is outside source_project_prefix {project_prefix!r}"
        )
    return normalize_repo_path(source[len(project_prefix) :], "project destination")


def is_hard_excluded(path: str, manifest: dict[str, Any]) -> str | None:
    rules = manifest.get("hard_exclusions", {})
    parts_lower = {part.lower() for part in PurePosixPath(path).parts}
    for segment in rules.get("path_segments", []):
        if str(segment).lower() in parts_lower:
            return f"excluded path segment {segment}"
    suffix = PurePosixPath(path).suffix.lower()
    for extension in rules.get("extensions", []):
        if suffix == str(extension).lower():
            return f"excluded extension {extension}"
    lowered = path.lower()
    for fragment in rules.get("case_insensitive_fragments", []):
        if str(fragment).lower() in lowered:
            return f"excluded fragment {fragment}"
    if any(part.lower() == "importedassets" for part in PurePosixPath(path).parts):
        allowed: set[str] = set()
        for item in manifest.get("rights_gated_entries", []):
            if not bool(item.get("enabled", False)):
                continue
            gated = normalize_repo_path(str(item["source"]), "rights-gated source")
            allowed.add(gated)
            allowed.add(gated + ".meta")
            parent = PurePosixPath(gated).parent
            while "ImportedAssets" in parent.parts:
                allowed.add(parent.as_posix() + ".meta")
                parent = parent.parent
        if path not in allowed:
            return "arbitrary ImportedAssets content is not rights-gated"
    return None


def expand_allowlist(
    tree: list[str], manifest: dict[str, Any]
) -> tuple[list[Selection], list[str]]:
    tree_set = set(tree)
    project_prefix = str(manifest.get("source_project_prefix", ""))
    by_source: dict[str, Selection] = {}
    missing: list[str] = []

    def add(source: str, entry: dict[str, Any], paired_meta: bool = False) -> None:
        destination = destination_for(source, entry, project_prefix)
        selection = Selection(
            source=source,
            destination=destination,
            classification=str(entry.get("classification", "unclassified")),
            group=str(entry.get("group", "unclassified")),
            paired_meta=paired_meta,
        )
        prior = by_source.get(source)
        if prior is not None:
            if (
                prior.destination != selection.destination
                or prior.classification != selection.classification
                or prior.group != selection.group
            ):
                raise ExtractionError(f"Source {source} has conflicting manifest classifications")
            selection = Selection(
                source=source,
                destination=selection.destination,
                classification=selection.classification,
                group=selection.group,
                paired_meta=prior.paired_meta or selection.paired_meta,
            )
        by_source[source] = selection

    for raw_entry in effective_entries(manifest):
        entry = dict(raw_entry)
        kind = str(entry.get("kind", "file"))
        pattern = normalize_repo_path(str(entry.get("source", "")), "manifest source")
        matches = [path for path in tree if pattern_matches(path, pattern, kind)]
        if not matches:
            missing.append(pattern)
            continue
        for source in matches:
            reason = is_hard_excluded(source, manifest)
            if reason:
                raise ExtractionError(f"Allowlist selected forbidden source {source}: {reason}")
            add(source, entry)

            if source.startswith(project_prefix + "Assets/") and not source.endswith(".meta"):
                meta = source + ".meta"
                if meta not in tree_set:
                    missing.append(meta)
                else:
                    meta_entry = dict(entry)
                    if entry.get("destination"):
                        meta_entry["destination"] = str(entry["destination"]) + ".meta"
                    add(meta, meta_entry, paired_meta=True)

    for replacement in manifest.get("product_replacements", []):
        source = normalize_repo_path(str(replacement.get("source", "")), "replacement source")
        normalize_repo_path(str(replacement.get("destination", "")), "replacement destination")
        if source not in tree_set:
            missing.append(source)

    # Preserve folder GUIDs for every selected Unity asset ancestor.  Folder
    # metas are not serialized dependencies, but copying them keeps the import
    # inventory stable across clean machines.
    asset_prefix = project_prefix + "Assets"
    for selection in list(by_source.values()):
        if not selection.source.startswith(asset_prefix + "/"):
            continue
        parent = PurePosixPath(selection.source).parent
        while parent.as_posix().startswith(asset_prefix):
            parent_meta = parent.as_posix() + ".meta"
            if parent_meta in tree_set and parent_meta not in by_source:
                synthetic = {
                    "kind": "file",
                    "classification": selection.classification,
                    "group": selection.group,
                    "destination_root": "project",
                }
                add(parent_meta, synthetic, paired_meta=True)
            if parent.as_posix() == asset_prefix:
                break
            parent = parent.parent

    destinations: dict[str, str] = {}
    for selection in by_source.values():
        prior_source = destinations.get(selection.destination)
        if prior_source is not None and prior_source != selection.source:
            raise ExtractionError(
                f"Duplicate destination {selection.destination}: {prior_source} and {selection.source}"
            )
        destinations[selection.destination] = selection.source
    return sorted(by_source.values(), key=lambda item: item.source), sorted(set(missing))


def source_bytes(source_repo: Path, commit: str, source: str) -> bytes:
    return bytes(git(source_repo, "show", f"{commit}:{source}"))


def repository_destination(destination_project: Path, selection: Selection) -> Path:
    product_repo = destination_project.parent
    if selection.destination.startswith("Assets/") or selection.destination.startswith("ProjectSettings/") or selection.destination.startswith("Packages/"):
        return destination_project / Path(*PurePosixPath(selection.destination).parts)
    return product_repo / Path(*PurePosixPath(selection.destination).parts)


def relative_product_destination(selection: Selection) -> str:
    if selection.destination.startswith(("Assets/", "ProjectSettings/", "Packages/")):
        return "QuestFlight/" + selection.destination
    return selection.destination


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def atomic_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    handle, temp_name = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(handle, "wb") as stream:
            stream.write(data)
        os.replace(temp_name, path)
    except Exception:
        try:
            os.unlink(temp_name)
        except OSError:
            pass
        raise


def collect_guid_map(source_repo: Path, commit: str) -> dict[str, str]:
    output = str(
        git(
            source_repo,
            "grep",
            "-n",
            "^guid: ",
            commit,
            "--",
            "*.meta",
            text=True,
        )
    )
    guid_map: dict[str, str] = {}
    for line in output.splitlines():
        match = GUID_GREP_LINE.match(line)
        if not match:
            continue
        meta_path, guid = match.groups()
        asset_path = meta_path[:-5] if meta_path.endswith(".meta") else meta_path
        prior = guid_map.get(guid.lower())
        if prior is not None and prior != asset_path:
            raise ExtractionError(f"Duplicate Unity GUID {guid}: {prior} and {asset_path}")
        guid_map[guid.lower()] = asset_path
    return guid_map


def manifest_selection_for_path(path: str, manifest: dict[str, Any]) -> dict[str, Any] | None:
    for entry in effective_entries(manifest):
        pattern = normalize_repo_path(str(entry["source"]), "manifest source")
        if pattern_matches(path, pattern, str(entry.get("kind", "file"))):
            return entry
    return None


def classify_dependency(path: str, manifest: dict[str, Any]) -> str:
    excluded = is_hard_excluded(path, manifest)
    if excluded:
        return "excluded_legacy_dependency"
    for item in manifest.get("rights_gated_entries", []):
        gated_path = normalize_repo_path(str(item["source"]), "rights-gated source")
        paired_meta = gated_path + ".meta"
        if path in {gated_path, paired_meta}:
            if bool(item.get("enabled", False)):
                return str(item.get("classification", "approved_third_party"))
            return str(item.get("disabled_classification", "approved_local_development_only"))
    if "/ImportedAssets/" in path:
        return "excluded_legacy_dependency"
    entry = manifest_selection_for_path(path, manifest)
    if entry is not None:
        return str(entry.get("classification", "approved_production"))
    return "unknown_unclassified_dependency"


def source_dependency_closure(
    source_repo: Path, commit: str, manifest: dict[str, Any]
) -> dict[str, Any]:
    roots = [normalize_repo_path(str(path), "dependency root") for path in manifest.get("dependency_roots", [])]
    if not roots:
        return {
            "schema_version": 1,
            "source_commit": commit,
            "roots": [],
            "dependencies": [],
            "unresolved_guids": [],
        }
    tree = set(source_tree(source_repo, commit))
    for root in roots:
        if root not in tree:
            raise ExtractionError(f"Dependency root is absent from source commit: {root}")
    guid_map = collect_guid_map(source_repo, commit)
    pending: list[tuple[str, str | None, str | None]] = [(root, None, None) for root in roots]
    seen: set[str] = set()
    records: list[dict[str, Any]] = []
    unresolved: set[str] = set()

    while pending:
        path, reached_from, reference_guid = pending.pop(0)
        if path in seen:
            continue
        seen.add(path)
        classification = classify_dependency(path, manifest)
        records.append(
            {
                "asset_path": path,
                "classification": classification,
                "reached_from": reached_from,
                "reference_guid": reference_guid,
            }
        )
        data = source_bytes(source_repo, commit, path)
        if b"\x00" in data[:8192]:
            continue
        for guid_bytes in sorted(set(GUID_REFERENCE.findall(data))):
            guid = guid_bytes.decode("ascii").lower()
            if set(guid) == {"0"}:
                continue
            dependency = guid_map.get(guid)
            if dependency is None:
                unresolved.add(guid)
                continue
            if dependency not in seen:
                pending.append((dependency, path, guid))

    records.sort(key=lambda item: item["asset_path"])
    return {
        "schema_version": 1,
        "source_commit": commit,
        "roots": roots,
        "dependencies": records,
        "unresolved_guids": sorted(unresolved),
    }


def comparison_reports(closure: dict[str, Any]) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    groups: dict[str, list[dict[str, Any]]] = {
        "approved": [],
        "conditional_local": [],
        "rejected": [],
        "unknown": [],
    }
    for item in closure["dependencies"]:
        classification = item["classification"]
        if classification == "excluded_legacy_dependency":
            groups["rejected"].append(item)
        elif classification == "unknown_unclassified_dependency":
            groups["unknown"].append(item)
        elif classification == "approved_local_development_only":
            groups["conditional_local"].append(item)
        else:
            groups["approved"].append(item)
    unresolved = closure.get("unresolved_guids", [])
    ready = not groups["rejected"] and not groups["unknown"]
    comparison = {
        "schema_version": 1,
        "source_commit": closure["source_commit"],
        "ready_for_clean_product": ready,
        "approved_count": len(groups["approved"]),
        "conditional_local_count": len(groups["conditional_local"]),
        "rejected_count": len(groups["rejected"]),
        "unknown_count": len(groups["unknown"]),
        "unresolved_package_or_builtin_guid_count": len(unresolved),
        **groups,
        "unresolved_package_or_builtin_guids": unresolved,
    }
    rejected = {
        "schema_version": 1,
        "source_commit": closure["source_commit"],
        "rejected_dependencies": groups["rejected"],
    }
    final = {
        "schema_version": 1,
        "source_commit": closure["source_commit"],
        "ready": ready,
        "approved_production_dependencies": groups["approved"],
        "approved_local_development_only": groups["conditional_local"],
        "blockers": groups["rejected"] + groups["unknown"],
        "note": "Unresolved GUIDs are verified by the Unity AssetDatabase exporter after package resolution.",
    }
    return comparison, rejected, final


def write_json(path: Path, value: Any) -> None:
    atomic_write(path, (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8"))


def write_reports(
    report_dir: Path,
    commit: str,
    mode: str,
    records: list[InventoryRecord],
    missing: list[str],
    closure: dict[str, Any],
    manifest: dict[str, Any],
) -> None:
    report_dir.mkdir(parents=True, exist_ok=True)
    inventory = {
        "schema_version": 1,
        "source_commit": commit,
        "mode": mode,
        "file_count": len(records),
        "total_bytes": sum(record.size_bytes for record in records),
        "missing_allowlist_entries": missing,
        "product_replacements_not_copied": manifest.get("product_replacements", []),
        "files": [record.as_dict() for record in records],
    }
    write_json(report_dir / "extraction_inventory.json", inventory)

    csv_buffer = io.StringIO(newline="")
    writer = csv.writer(csv_buffer, lineterminator="\n")
    writer.writerow(
        [
            "source",
            "destination",
            "classification",
            "group",
            "paired_meta",
            "size_bytes",
            "sha256",
            "status",
        ]
    )
    for record in records:
        writer.writerow(
            [
                record.source,
                record.destination,
                record.classification,
                record.group,
                str(record.paired_meta).lower(),
                record.size_bytes,
                record.sha256,
                record.status,
            ]
        )
    atomic_write(report_dir / "extraction_inventory.csv", csv_buffer.getvalue().encode("utf-8"))

    comparison, rejected, final = comparison_reports(closure)
    write_json(report_dir / "source_dependency_closure.json", closure)
    write_json(report_dir / "allowlist_comparison.json", comparison)
    write_json(report_dir / "rejected_dependencies.json", rejected)
    write_json(report_dir / "final_product_closure.json", final)

    counts: dict[str, int] = {}
    for record in records:
        counts[record.classification] = counts.get(record.classification, 0) + 1
    summary = [
        "# Deterministic extraction summary",
        "",
        f"- Source commit: `{commit}`",
        f"- Mode: `{mode}`",
        f"- Files inventoried: **{len(records)}**",
        f"- Total copied/inventoried bytes: **{sum(record.size_bytes for record in records)}**",
        f"- Missing allowlist entries: **{len(missing)}**",
        f"- Product-owned replacements intentionally not copied: **{len(manifest.get('product_replacements', []))}**",
        f"- Rejected scene dependencies: **{comparison['rejected_count']}**",
        f"- Unknown scene dependencies: **{comparison['unknown_count']}**",
        f"- Conditional local-only dependencies: **{comparison['conditional_local_count']}**",
        f"- Clean-product dependency readiness: **{'PASS' if comparison['ready_for_clean_product'] else 'BLOCKED'}**",
        "",
        "## Inventory classifications",
        "",
    ]
    for classification, count in sorted(counts.items()):
        summary.append(f"- `{classification}`: {count}")
    if missing:
        summary.extend(["", "## Missing allowlist entries", ""])
        summary.extend(f"- `{item}`" for item in missing)
    if manifest.get("product_replacements"):
        summary.extend(["", "## Product-owned replacements not copied", ""])
        for replacement in manifest["product_replacements"]:
            summary.append(
                f"- `{replacement['source']}` -> `{replacement['destination']}` — "
                f"{replacement['classification']}"
            )
    if comparison["rejected"] or comparison["unknown"]:
        summary.extend(["", "## Dependency blockers requiring clean-product replacement", ""])
        for item in comparison["rejected"] + comparison["unknown"]:
            summary.append(f"- `{item['asset_path']}` — {item['classification']}")
    summary.extend(
        [
            "",
            "Unresolved GUIDs are package/built-in candidates and must be resolved by",
            "`ProductionDependencyClosureExporter` after Unity package restoration.",
            "",
        ]
    )
    atomic_write(report_dir / "extraction_summary.md", "\n".join(summary).encode("utf-8"))


def execute(args: argparse.Namespace) -> int:
    source_repo = Path(args.source_repo).resolve()
    destination_project = Path(args.destination_project).resolve()
    manifest_path = Path(args.manifest).resolve()
    manifest = load_manifest(manifest_path)
    commit = resolve_source_commit(source_repo, args.source_ref, manifest)
    tree = source_tree(source_repo, commit)
    selections, missing = expand_allowlist(tree, manifest)
    report_dir = (
        Path(args.report_dir).resolve()
        if args.report_dir
        else destination_project.parent / "docs" / "extraction" / "reports"
    )

    payloads: dict[str, bytes] = {}
    records: list[InventoryRecord] = []
    conflicts: list[str] = []
    for selection in selections:
        data = source_bytes(source_repo, commit, selection.source)
        payloads[selection.source] = data
        destination_path = repository_destination(destination_project, selection)
        status = "would_copy" if args.dry_run else "copied"
        if destination_path.exists():
            existing_hash = sha256(destination_path.read_bytes())
            if existing_hash == sha256(data):
                status = "unchanged"
            else:
                status = "conflict"
                conflicts.append(relative_product_destination(selection))
        records.append(
            InventoryRecord(
                source=selection.source,
                destination=relative_product_destination(selection),
                classification=selection.classification,
                group=selection.group,
                paired_meta=selection.paired_meta,
                size_bytes=len(data),
                sha256=sha256(data),
                status=status,
            )
        )

    closure = source_dependency_closure(source_repo, commit, manifest)
    if args.copy and not missing and not conflicts:
        for selection in selections:
            destination_path = repository_destination(destination_project, selection)
            data = payloads[selection.source]
            if destination_path.exists():
                continue
            atomic_write(destination_path, data)

    write_reports(
        report_dir,
        commit,
        "dry-run" if args.dry_run else "copy",
        records,
        missing,
        closure,
        manifest,
    )

    comparison, _, _ = comparison_reports(closure)
    print(f"Source commit: {commit}")
    print(f"Files inventoried: {len(records)}")
    print(f"Reports: {report_dir}")
    if missing:
        print(f"ERROR: {len(missing)} allowlist entries are missing", file=sys.stderr)
    if conflicts:
        print(f"ERROR: {len(conflicts)} destination files differ; no files were overwritten", file=sys.stderr)
        for path in conflicts:
            print(f"  {path}", file=sys.stderr)
    blockers = comparison["rejected_count"] + comparison["unknown_count"]
    if blockers:
        print(
            f"BLOCKED: source scene closure contains {comparison['rejected_count']} excluded and "
            f"{comparison['unknown_count']} unclassified dependencies; replace references in the clean product",
            file=sys.stderr,
        )
    if missing or conflicts:
        return 2
    if blockers:
        return 3
    return 0


def parse_args(argv: Iterable[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-repo", required=True)
    parser.add_argument("--source-ref", required=True)
    parser.add_argument("--destination-project", required=True)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--report-dir")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--dry-run", action="store_true")
    mode.add_argument("--copy", action="store_true")
    return parser.parse_args(argv)


def main(argv: Iterable[str] | None = None) -> int:
    try:
        return execute(parse_args(argv))
    except ExtractionError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
