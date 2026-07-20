#!/usr/bin/env python3
"""Validate the committed KBDU lidar derivative contract and emit QA previews."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

from common import (
    compute_grid_contract,
    default_config_path,
    file_record,
    load_config,
    load_json,
    repository_root,
    sha256_file,
)


LOGICAL_ASSET_DIR = "QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar"
MANIFEST_NAME = "KBDU_LidarManifestV1.json"
EXPECTED_SEMANTICS = {
    "building_height_confidence",
    "canopy_height_normal_and_occlusion",
    "digital_surface_model",
    "digital_terrain_model",
    "fixed_near_terrain_mesh",
    "near_terrain_error_report",
    "runway_plane_preservation_inputs",
    "surface_features_canopy_building",
    "terrain_surface_normal_and_validity",
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def asset_by_semantic(manifest: dict[str, Any], semantic: str) -> dict[str, Any]:
    matches = [asset for asset in manifest["assets"] if asset["semantic"] == semantic]
    require(len(matches) == 1, f"Expected one asset with semantic {semantic!r}")
    return matches[0]


def resolve_asset(root: Path, asset: dict[str, Any]) -> Path:
    logical = str(asset["path"]).replace("\\", "/")
    require(logical.startswith(LOGICAL_ASSET_DIR + "/"), f"Asset escaped lidar root: {logical}")
    path = (root / logical).resolve()
    require(path.is_file(), f"Missing asset: {logical}")
    require(path.stat().st_size == int(asset["bytes"]), f"Byte count mismatch: {logical}")
    require(sha256_file(path) == asset["sha256"], f"SHA-256 mismatch: {logical}")
    require(Path(str(path) + ".meta").is_file(), f"Missing Unity meta: {logical}.meta")
    return path


def load_image(path: Path, shape: tuple[int, ...], dtype: np.dtype[Any]) -> np.ndarray:
    value = np.asarray(Image.open(path))
    require(value.shape == shape, f"Unexpected image shape {value.shape}: {path}")
    require(value.dtype == dtype, f"Unexpected image dtype {value.dtype}: {path}")
    return value


def validate_obj(path: Path, expected_vertices: int, expected_triangles: int) -> dict[str, Any]:
    vertices: list[tuple[float, float, float]] = []
    faces: list[tuple[int, int, int]] = []
    with path.open("r", encoding="utf-8") as stream:
        for line in stream:
            if line.startswith("v "):
                _, x, y, z = line.split()
                values = (float(x), float(y), float(z))
                require(all(np.isfinite(values)), "OBJ contains a non-finite vertex")
                vertices.append(values)
            elif line.startswith("f "):
                parts = line.split()
                require(len(parts) == 4, "OBJ contains a non-triangle face")
                face = tuple(int(item.split("/")[0]) - 1 for item in parts[1:])
                faces.append(face)
    require(len(vertices) == expected_vertices, "OBJ vertex count mismatch")
    require(len(faces) == expected_triangles, "OBJ triangle count mismatch")
    points = np.asarray(vertices, dtype=np.float64)
    triangles = np.asarray(faces, dtype=np.int64)
    require(int(np.min(triangles)) >= 0, "OBJ contains a non-positive index")
    require(int(np.max(triangles)) < len(vertices), "OBJ contains an out-of-range index")
    first, second, third = points[triangles[:, 0]], points[triangles[:, 1]], points[triangles[:, 2]]
    normal_y = (
        (second[:, 2] - first[:, 2]) * (third[:, 0] - first[:, 0])
        - (second[:, 0] - first[:, 0]) * (third[:, 2] - first[:, 2])
    )
    require(bool(np.all(normal_y > 0.0)), "OBJ has degenerate or downward triangles")
    require(float(np.min(points[:, 0])) == -2048.0, "OBJ west border is not locked")
    require(float(np.max(points[:, 0])) == 2048.0, "OBJ east border is not locked")
    require(float(np.min(points[:, 2])) == -2048.0, "OBJ south border is not locked")
    require(float(np.max(points[:, 2])) == 2048.0, "OBJ north border is not locked")
    return {
        "local_bounds_meters": [
            float(np.min(points[:, 0])),
            float(np.min(points[:, 1])),
            float(np.min(points[:, 2])),
            float(np.max(points[:, 0])),
            float(np.max(points[:, 1])),
            float(np.max(points[:, 2])),
        ],
        "triangle_count": len(faces),
        "vertex_count": len(vertices),
    }


def write_qa_previews(
    qa_output: Path,
    dtm: np.ndarray,
    terrain_normal: np.ndarray,
    canopy_normal: np.ndarray,
) -> list[dict[str, Any]]:
    qa_output.mkdir(parents=True, exist_ok=True)
    decoded_normal = terrain_normal[..., :3].astype(np.float64) / 127.5 - 1.0
    light = np.asarray([0.35, 0.85, 0.4], dtype=np.float64)
    light /= np.linalg.norm(light)
    shade = np.clip(np.sum(decoded_normal * light, axis=-1), 0.0, 1.0)
    elevation = (dtm.astype(np.float64) - float(np.min(dtm))) / max(
        float(np.max(dtm) - np.min(dtm)), 1.0
    )
    base = np.stack(
        [0.24 + 0.42 * elevation, 0.31 + 0.43 * elevation, 0.18 + 0.28 * elevation],
        axis=-1,
    )
    hillshade = np.rint(np.clip(base * (0.35 + 0.65 * shade[..., None]), 0.0, 1.0) * 255).astype(
        np.uint8
    )
    hillshade_path = qa_output / "KBDU_Lidar_DTM_Hillshade_QA.png"
    Image.fromarray(hillshade).save(hillshade_path, format="PNG", optimize=True, compress_level=9)
    canopy_path = qa_output / "KBDU_Lidar_CanopyNormalOcclusion_QA.png"
    Image.fromarray(canopy_normal).save(canopy_path, format="PNG", optimize=True, compress_level=9)
    return [
        file_record(hillshade_path, "external-artifact/lidar/qa/KBDU_Lidar_DTM_Hillshade_QA.png"),
        file_record(
            canopy_path,
            "external-artifact/lidar/qa/KBDU_Lidar_CanopyNormalOcclusion_QA.png",
        ),
    ]


def validate(config_path: Path, manifest_path: Path, qa_output: Path | None) -> dict[str, Any]:
    root = repository_root()
    config = load_config(config_path)
    contract = compute_grid_contract(config)
    manifest = load_json(manifest_path)
    require(manifest.get("schema") == "questflight.aerial-scenery-v1.lidar.v1", "Bad schema")
    semantics = [asset["semantic"] for asset in manifest["assets"]]
    require(len(semantics) == len(set(semantics)), "Duplicate asset semantic")
    require(set(semantics) == EXPECTED_SEMANTICS, "Incomplete or unexpected semantic set")
    paths = [resolve_asset(root, asset) for asset in manifest["assets"]]
    require(Path(str(manifest_path.parent) + ".meta").is_file(), "Missing Lidar folder meta")
    require(not list(manifest_path.parent.glob("*.la[sz]")), "Raw LAS/LAZ entered product assets")

    expected_2d = (contract.height, contract.width)
    expected_rgba = (*expected_2d, 4)
    dtm_codes = load_image(
        resolve_asset(root, asset_by_semantic(manifest, "digital_terrain_model")),
        expected_2d,
        np.dtype(np.uint16),
    )
    dsm_codes = load_image(
        resolve_asset(root, asset_by_semantic(manifest, "digital_surface_model")),
        expected_2d,
        np.dtype(np.uint16),
    )
    features = load_image(
        resolve_asset(root, asset_by_semantic(manifest, "surface_features_canopy_building")),
        expected_rgba,
        np.dtype(np.uint8),
    )
    terrain_normal = load_image(
        resolve_asset(root, asset_by_semantic(manifest, "terrain_surface_normal_and_validity")),
        expected_rgba,
        np.dtype(np.uint8),
    )
    canopy_normal = load_image(
        resolve_asset(root, asset_by_semantic(manifest, "canopy_height_normal_and_occlusion")),
        expected_rgba,
        np.dtype(np.uint8),
    )
    load_image(
        resolve_asset(root, asset_by_semantic(manifest, "building_height_confidence")),
        expected_2d,
        np.dtype(np.uint8),
    )
    require(bool(np.all(dtm_codes > 0)), "DTM contains nodata code")
    require(bool(np.all(dsm_codes >= dtm_codes)), "DSM is below DTM")
    require(bool(np.array_equal(canopy_normal[..., 3], features[..., 3])), "Canopy occlusion mismatch")
    non_canopy = features[..., 0] == 0
    require(bool(np.all(canopy_normal[non_canopy, 3] == 0)), "Non-canopy occlusion is nonzero")
    require(bool(np.all(canopy_normal[non_canopy, 0] == 128)), "Non-canopy normal X is not up")
    require(bool(np.all(canopy_normal[non_canopy, 1] == 255)), "Non-canopy normal Y is not up")
    require(bool(np.all(canopy_normal[non_canopy, 2] == 128)), "Non-canopy normal Z is not up")

    grid = manifest["coordinate_mapping"]["grid"]
    require(grid["width_pixels"] == 1024 and grid["height_pixels"] == 1024, "Grid size changed")
    require(float(grid["pixel_size_meters"]) == 4.0, "Pixel size changed")
    affine = manifest["coordinate_mapping"]["pixel_center_affine_local_meters"]
    require(
        affine
        == {
            "x_at_column0_row0": -2046.0,
            "x_per_column": 4.0,
            "x_per_row": 0.0,
            "z_at_column0_row0": 2046.0,
            "z_per_column": 0.0,
            "z_per_row": -4.0,
        },
        "Local pixel affine changed",
    )
    source = manifest["source"]
    require(source["dataset_id"] == "CO_DRCOG_1_2020", "Source dataset changed")
    require(source["metadata_availability"]["ept_stac_vertical_crs"] is None, "EPT CRS claim changed")
    require(source["metadata_availability"]["wesm_achieved_accuracy_metrics"] is None, "Accuracy inferred")
    require(source["wesm"]["record"]["quality_level"] == "QL 1", "WESM QL missing")
    require(source["wesm"]["record"]["acquisition_start"] == "2020-05-29", "Start date missing")
    require(source["wesm"]["record"]["acquisition_end"] == "2020-08-03", "End date missing")
    require(manifest["building_height_quality"]["eligible_for_authoritative_override"] is False, "Unsafe building gate")

    mesh_asset = asset_by_semantic(manifest, "fixed_near_terrain_mesh")
    mesh_path = resolve_asset(root, mesh_asset)
    mesh_validation = validate_obj(
        mesh_path,
        int(mesh_asset["dimensions"]["vertex_count"]),
        int(mesh_asset["dimensions"]["triangle_count"]),
    )
    require(
        mesh_validation["triangle_count"]
        <= int(config["derivative_budgets"]["maximum_total_near_mesh_triangles"]),
        "Mesh triangle hard cap exceeded",
    )
    report = load_json(resolve_asset(root, asset_by_semantic(manifest, "near_terrain_error_report")))
    require(report["border_lock"]["exact_4096_meter_square"] is True, "Report border lock failed")
    require(report["mesh"]["triangle_count"] == mesh_validation["triangle_count"], "Report mesh mismatch")
    require(report["geometric_integration_eligibility"]["status"] == "ELIGIBLE", "Geometric target failed")
    observed = report["geometric_integration_eligibility"]["observed_outside_declared_outliers"]
    thresholds = report["geometric_integration_eligibility"]["thresholds"]
    require(observed["mean_absolute_meters"] <= thresholds["mean_absolute_meters"], "MAE target failed")
    require(observed["p95_absolute_meters"] <= thresholds["p95_absolute_meters"], "P95 target failed")
    require(observed["maximum_absolute_meters"] <= thresholds["maximum_absolute_meters"], "Max target failed")
    require(report["operational_0_05_meter_tolerance"]["real_world_accuracy_supported"] is False, "Unsafe 0.05 m claim")
    require(
        report["comparison"]["runway_mesh_vs_input_plane"]["maximum_absolute_meters"] < 1e-5,
        "Runway plane preservation failed",
    )
    total_bytes = sum(int(asset["bytes"]) for asset in manifest["assets"])
    require(total_bytes == manifest["statistics"]["total_product_bytes"], "Product byte sum mismatch")
    require(total_bytes <= config["derivative_budgets"]["maximum_product_bytes"], "Product byte budget exceeded")

    qa_assets: list[dict[str, Any]] = []
    if qa_output is not None:
        qa_assets = write_qa_previews(qa_output, dtm_codes, terrain_normal, canopy_normal)
    return {
        "asset_count": len(paths),
        "building_height_authoritative": False,
        "geometric_integration_status": report["geometric_integration_eligibility"]["status"],
        "manifest_sha256": sha256_file(manifest_path),
        "mesh": mesh_validation,
        "product_bytes": total_bytes,
        "qa_assets": qa_assets,
        "status": "VALIDATED",
    }


def parse_args() -> argparse.Namespace:
    root = repository_root()
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, default=default_config_path())
    parser.add_argument(
        "--manifest", type=Path, default=root / LOGICAL_ASSET_DIR / MANIFEST_NAME
    )
    parser.add_argument("--qa-output", type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    result = validate(
        args.config.resolve(),
        args.manifest.resolve(),
        args.qa_output.resolve() if args.qa_output else None,
    )
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
