"""Validate the reviewed cockpit representation and source-identity contract."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_MANIFEST = (
    ROOT / "tools" / "cockpit_dynamics" / "cockpit_representation_manifest_v2.json"
)

EXPECTED_IMPLEMENTED_IDS = {
    "pilot_yoke",
    "copilot_yoke",
    "throttle",
    "mixture",
    "exterior_ailerons",
    "exterior_elevators",
    "exterior_flaps",
    "exterior_rudder",
    "nose_steering",
    "airspeed_indicator",
    "vertical_speed_indicator",
}


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def validate_manifest(
    manifest_path: Path = DEFAULT_MANIFEST,
    inspection_path: Path | None = None,
) -> dict[str, Any]:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    errors: list[str] = []

    if manifest.get("schemaVersion") != 2:
        errors.append("schemaVersion must be 2")
    if manifest.get("runtimeHierarchyConstructionAllowed") is not False:
        errors.append("runtime hierarchy construction must remain prohibited")

    source = ROOT / str(manifest.get("sourceAsset", ""))
    if not source.is_file():
        errors.append(f"source asset is missing: {source}")
    else:
        if source.stat().st_size != manifest.get("sourceBytes"):
            errors.append("source byte count does not match the reviewed asset")
        if _sha256(source) != manifest.get("sourceSha256"):
            errors.append("source SHA256 does not match the reviewed asset")

    implemented = manifest.get("implementedTargets")
    if not isinstance(implemented, list) or not implemented:
        errors.append("implementedTargets must be a non-empty array")
        implemented = []
    identifiers = [entry.get("id") for entry in implemented if isinstance(entry, dict)]
    if len(identifiers) != len(set(identifiers)):
        errors.append("implemented target ids must be unique")
    if any(not identifier for identifier in identifiers):
        errors.append("every implemented target requires an id")
    if set(identifiers) != EXPECTED_IMPLEMENTED_IDS:
        errors.append(
            "implemented target ids must exactly match the serialized reviewed baseline"
        )

    represented = manifest.get("representedCandidates", [])
    excluded = manifest.get("excludedTargets", [])
    if not isinstance(represented, list):
        errors.append("representedCandidates must be an array")
        represented = []
    if not isinstance(excluded, list):
        errors.append("excludedTargets must be an array")
        excluded = []
    represented_ids = [
        entry.get("id") for entry in represented if isinstance(entry, dict)
    ]
    excluded_ids = [
        entry.get("id") for entry in excluded if isinstance(entry, dict)
    ]
    if len(represented_ids) != len(set(represented_ids)):
        errors.append("represented candidate ids must be unique")
    if len(excluded_ids) != len(set(excluded_ids)):
        errors.append("excluded target ids must be unique")
    all_ids = identifiers + represented_ids + excluded_ids
    if len(all_ids) != len(set(all_ids)):
        errors.append("target ids must be disjoint across all classifications")
    for entry in represented:
        if not isinstance(entry, dict):
            errors.append("each represented candidate must be an object")
            continue
        if not entry.get("id") or not entry.get("status"):
            errors.append("each represented candidate requires an id and status")
        authority = entry.get("requiredAuthority")
        if not isinstance(authority, list) or not authority:
            errors.append(
                f"represented candidate {entry.get('id')} requires authority"
            )
    for entry in excluded:
        if not isinstance(entry, dict):
            errors.append("each excluded target must be an object")
            continue
        if not entry.get("id") or not entry.get("reason"):
            errors.append("each excluded target requires an id and reason")

    for entry in implemented:
        if not isinstance(entry, dict):
            errors.append("each implemented target must be an object")
            continue
        if not entry.get("owner"):
            errors.append(f"implemented target {entry.get('id')} has no owner")
        authority = entry.get("authority")
        if not isinstance(authority, list) or not authority:
            errors.append(
                f"implemented target {entry.get('id')} has no authoritative channels"
            )
        for key in (
            "renderCostDelta",
            "maximumRendererDelta",
            "maximumDrawDelta",
            "maximumMaterialDelta",
            "maximumTextureDelta",
        ):
            value = entry.get(key)
            if value is not None and (not isinstance(value, int) or value < 0):
                errors.append(f"{entry.get('id')}.{key} must be a non-negative integer")

    if inspection_path is not None:
        inspection = json.loads(inspection_path.read_text(encoding="utf-8"))
        inspection_source = Path(str(inspection.get("source", ""))).resolve()
        if inspection_source != source.resolve():
            errors.append("inspection source does not match the reviewed source asset")
        if inspection.get("animationCount") != manifest.get("embeddedAnimationClips"):
            errors.append("inspection animation count does not match the manifest")
        if inspection.get("objectCount", 0) <= 0:
            errors.append("inspection contains no source objects")

    return {
        "passed": not errors,
        "manifest": str(manifest_path.resolve()),
        "source": str(source.resolve()),
        "implementedTargetCount": len(implemented),
        "implementedTargetIds": sorted(identifiers),
        "representedCandidateCount": len(represented),
        "excludedTargetCount": len(excluded),
        "errors": errors,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--inspection", type=Path)
    parser.add_argument("--output", type=Path)
    arguments = parser.parse_args()
    report = validate_manifest(arguments.manifest.resolve(), arguments.inspection)
    payload = json.dumps(report, indent=2) + "\n"
    if arguments.output is not None:
        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_text(payload, encoding="utf-8")
    print(payload, end="")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
