#!/usr/bin/env python3
"""Prepare the bounded M08 building-metadata ownership reference refresh.

Output is an explicit proposal file, never a product asset. Only the recorded
building-manifest byte count and SHA change; all ownership/fact/coordinate
values remain serialized exactly. Root integration owns backup and activation.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from build_canopy_surfaces import BUILDING_MANIFEST, OWNERSHIP_MANIFEST_NAME, RUNTIME_ROOT
from build_dtm_dsm import REPOSITORY_ROOT, sha256_bytes

OWNERSHIP_PATH = RUNTIME_ROOT / OWNERSHIP_MANIFEST_NAME
ORIGINAL_OWNERSHIP_IDENTITY = (
    25_792, "e8cdfe3d0cfcba408d715ff40201703b823b64873b10e747b7880a73d3a96057",
)
PRE_M08_BUILDING_IDENTITY = (
    46_708, "7869dad7df1bf31e5fdbb4794bf5420805e47a9cb480205de92294a8b8993a32",
)
APPROVED_M08_BUILDING_IDENTITY = (
    48_243, "9ce3d9b0b162a2714f6c85175817f1ddd7f88dbf6bb377f5e5c6b4db7fad9088",
)


def prepare_refresh(ownership_bytes: bytes, building_bytes: bytes) -> tuple[bytes, dict]:
    if (len(ownership_bytes), sha256_bytes(ownership_bytes)) != ORIGINAL_OWNERSHIP_IDENTITY:
        raise ValueError("Original reviewed ownership manifest identity is required")
    if (len(building_bytes), sha256_bytes(building_bytes)) != APPROVED_M08_BUILDING_IDENTITY:
        raise ValueError("Approved M08 building metadata identity is required")
    before = json.loads(ownership_bytes)
    record = before["asset_manifests"]["building"]
    logical = BUILDING_MANIFEST.relative_to(REPOSITORY_ROOT).as_posix()
    if (record["path"], record["bytes"], record["sha256"]) != (logical, *PRE_M08_BUILDING_IDENTITY):
        raise ValueError("Original exact building ownership reference is required")
    # Preserve the source serialization, including every unaffected byte.
    old_count = b'"bytes": 46708'
    old_hash = PRE_M08_BUILDING_IDENTITY[1].encode("ascii")
    if ownership_bytes.count(old_count) != 1 or ownership_bytes.count(old_hash) != 1:
        raise ValueError("Unique original byte-count and hash fields are required")
    output = ownership_bytes.replace(old_count, b'"bytes": 48243', 1).replace(
        old_hash, APPROVED_M08_BUILDING_IDENTITY[1].encode("ascii"), 1,
    )
    after = json.loads(output)
    after_record = after["asset_manifests"]["building"].copy()
    after["asset_manifests"]["building"] = record
    if after != before:
        raise ValueError("Ownership fields outside the bounded building record changed")
    if output.replace(b'"bytes": 48243', old_count, 1).replace(
        APPROVED_M08_BUILDING_IDENTITY[1].encode("ascii"), old_hash, 1,
    ) != ownership_bytes:
        raise ValueError("Unrelated serialized ownership bytes changed")
    return output, {
        "original_ownership_bytes": len(ownership_bytes),
        "original_ownership_sha256": sha256_bytes(ownership_bytes),
        "proposed_ownership_bytes": len(output),
        "proposed_ownership_sha256": sha256_bytes(output),
        "original_building_record": record,
        "current_building_record": after_record,
        "changed_scalar_paths": ["asset_manifests.building.bytes", "asset_manifests.building.sha256"],
        "all_other_serialized_bytes_preserved": True,
        "geometry_or_coordinate_regeneration": False,
        "product_asset_activation_performed": False,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    destination = args.output.resolve()
    if destination.is_relative_to(REPOSITORY_ROOT.resolve()) or destination.exists():
        raise ValueError("Output must be a new explicitly named external proposal file")
    if not destination.parent.is_dir():
        raise ValueError("Existing owned external output directory is required")
    output, report = prepare_refresh(OWNERSHIP_PATH.read_bytes(), BUILDING_MANIFEST.read_bytes())
    with destination.open("xb") as stream:
        stream.write(output)
    report["proposal_path"] = str(destination)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
