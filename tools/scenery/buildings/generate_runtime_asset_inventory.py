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
AIRPORT_CORE_MATERIAL_ROOT = (
    REPO_ROOT / "QuestFlight/Assets/Production/Environment/AirportCoreV4/Materials"
)
AIRPORT_CORE_PBR_PATHS = (
    AIRPORT_CORE_MATERIAL_ROOT / "AirportCoreHangarKit_Albedo.png",
    AIRPORT_CORE_MATERIAL_ROOT / "AirportCoreHangarKit_Normal.png",
    AIRPORT_CORE_MATERIAL_ROOT / "AirportCoreHangarKit_MRAO.png",
)
AIRPORT_CORE_MATERIAL_PATH = AIRPORT_CORE_MATERIAL_ROOT / "AirportCoreHangarKitAtlas.mat"
AIRPORT_CORE_SOURCE_MANIFEST_PATH = AIRPORT_CORE_MATERIAL_ROOT / "SOURCE_MANIFEST.json"


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
    batch_manifest_path = BUILDING_ROOT / "KBDU_BuildingBatchReplacementV1.json"
    batch_manifest = json.loads(batch_manifest_path.read_text(encoding="utf-8"))
    meshes = sorted((BUILDING_ROOT / "Generated").glob("*.asset"))
    expected_mesh_count = int(batch_manifest["statistics"]["replacement_renderer_count"])
    assets = meshes + [
        BUILDING_ROOT / "KBDU_AerialBuildingsV1.prefab",
        BUILDING_ROOT / "KBDU_AerialBuildingShellsV2.mat",
        BUILDING_ROOT / "KBDU_AerialBuildingShell.shader",
    ]
    missing = [path for path in assets if not path.is_file()]
    reused_dependencies = (
        *AIRPORT_CORE_PBR_PATHS,
        AIRPORT_CORE_MATERIAL_PATH,
        AIRPORT_CORE_SOURCE_MANIFEST_PATH,
    )
    missing.extend(path for path in reused_dependencies if not path.is_file())
    if missing or len(meshes) != expected_mesh_count:
        raise RuntimeError(
            f"Expected {expected_mesh_count} fixed shell meshes plus prefab/material/shader; "
            f"missing={missing}, meshes={len(meshes)}"
        )

    records = []
    for path in assets:
        geometry = path.suffix in (".asset", ".prefab")
        records.append({
            "bytes": path.stat().st_size,
            "license": (
                "OpenStreetMap ODbL-1.0 derived data; project-authored Unity serialization"
                if geometry
                else "Project-authored source and Unity serialization"
            ),
            "path": logical(path),
            "sha256": sha256(path),
            "source": (
                "Deterministic closed true-footprint shells compiled from KBDU_BuildingSpatialContractV1.json"
                if geometry
                else "Project-authored fixed-shell material/shader reusing the resident AirportCore V4 HangarKit PBR atlas"
            ),
        })
    pbr_records = [
        {
            "bytes": path.stat().st_size,
            "license": "CC0-1.0 and project-owned optimized derivative; see SOURCE_MANIFEST.json",
            "path": logical(path),
            "sha256": sha256(path),
            "source": "Existing AirportCore V4 product dependency; reused without copying or adding a texture asset",
        }
        for path in AIRPORT_CORE_PBR_PATHS
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
        "material_dependency_contract": {
            "atlas_grid": [4, 4],
            "existing_product_material": {
                "path": logical(AIRPORT_CORE_MATERIAL_PATH),
                "sha256": sha256(AIRPORT_CORE_MATERIAL_PATH),
            },
            "incremental_texture_bytes": 0,
            "incremental_texture_reason": (
                "The exact three texture assets are already in the shipping product closure through "
                "AirportCoreHangarKitAtlas.mat; no texture was copied or newly introduced."
            ),
            "mrao_channels": {
                "r": "metallic",
                "g": "roughness",
                "b": "ambient_occlusion",
            },
            "reused_runtime_dependencies": pbr_records,
            "source_manifest": {
                "path": logical(AIRPORT_CORE_SOURCE_MANIFEST_PATH),
                "sha256": sha256(AIRPORT_CORE_SOURCE_MANIFEST_PATH),
            },
        },
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
