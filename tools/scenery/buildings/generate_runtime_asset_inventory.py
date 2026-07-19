#!/usr/bin/env python3
"""Hash-inventory the project-generated Aerial Scenery V1 building assets."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
BUILDING_ROOT = REPO_ROOT / "QuestFlight/Assets/Production/Environment/AerialSceneryV1/Buildings"
DEFAULT_OUTPUT = BUILDING_ROOT / "KBDU_BuildingRuntimeAssetInventoryV1.json"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def logical(path: Path) -> str:
    return path.resolve().relative_to(REPO_ROOT).as_posix()


def canonical_bytes(value: object) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")


def build_inventory() -> dict[str, object]:
    assets = sorted((BUILDING_ROOT / "Generated").glob("*.asset"))
    assets.append(BUILDING_ROOT / "KBDU_AerialBuildingsV1.prefab")
    missing = [path for path in assets if not path.is_file()]
    if missing or len(assets) != 24:
        raise RuntimeError(f"Expected 23 mesh assets plus one prefab; missing={missing}, count={len(assets)}")

    records = [
        {
            "bytes": path.stat().st_size,
            "license": "OpenStreetMap ODbL-1.0 derived data; project-authored Unity serialization",
            "path": logical(path),
            "sha256": sha256(path),
            "source": "Deterministic true-footprint geometry compiled from KBDU_BuildingSpatialContractV1.json",
        }
        for path in assets
    ]
    return {
        "asset_count": len(records),
        "generator": {
            "path": logical(Path(__file__)),
            "sha256": sha256(Path(__file__)),
        },
        "inputs": {
            "authoring_script": {
                "path": "QuestFlight/Assets/Editor/AerialSceneryBuildingAssetAuthoring.cs",
                "sha256": sha256(REPO_ROOT / "QuestFlight/Assets/Editor/AerialSceneryBuildingAssetAuthoring.cs"),
            },
            "batch_manifest": {
                "path": logical(BUILDING_ROOT / "KBDU_BuildingBatchReplacementV1.json"),
                "sha256": sha256(BUILDING_ROOT / "KBDU_BuildingBatchReplacementV1.json"),
            },
            "spatial_contract": {
                "path": logical(BUILDING_ROOT / "KBDU_BuildingSpatialContractV1.json"),
                "sha256": sha256(BUILDING_ROOT / "KBDU_BuildingSpatialContractV1.json"),
            },
        },
        "license": "OpenStreetMap ODbL-1.0 derivative; © OpenStreetMap contributors",
        "raw_external_data_committed": False,
        "records": records,
        "schema": "questflight.aerial-scenery-v1.building-runtime-assets.v1",
        "total_bytes": sum(record["bytes"] for record in records),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    inventory = build_inventory()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(canonical_bytes(inventory))
    print(args.output)
    print(f"assets={inventory['asset_count']} bytes={inventory['total_bytes']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
