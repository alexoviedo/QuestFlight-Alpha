#!/usr/bin/env python3
"""Compile exact lidar-derived surface classes without inventing source labels."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

from build_dtm_dsm import (
    DEFAULT_OUTPUT,
    MANIFEST_NAME as DTM_DSM_MANIFEST_NAME,
    RUNTIME_ROOT,
    SOURCE_MANIFEST,
    asset_by_semantic,
    file_record,
    load_json,
    logical_path,
    resolve_logical,
    sha256_file,
    verify_source_asset,
    write_folder_meta,
    write_json,
    write_text_meta,
    write_texture_meta,
)
from geospatial_frame import horizontal_frame_contract


CLASS_TEXTURE_NAME = "KBDU_AerialSurface_SurfaceClass_R8.png"
MANIFEST_NAME = "KBDU_AerialSurface_ClassificationManifestV1.json"

VALID_SURFACE = 1 << 0
ELEVATED_SURFACE = 1 << 1
CANOPY = 1 << 2
BUILDING = 1 << 3
HEURISTIC_CLASSIFICATION = 1 << 4
FILLED_HEIGHT_SOURCE = 1 << 5


def load_image(path: Path, expected_shape: tuple[int, ...], dtype: np.dtype[Any]) -> np.ndarray:
    values = np.asarray(Image.open(path))
    if values.shape != expected_shape or values.dtype != dtype:
        raise ValueError(
            f"Expected {dtype} image {expected_shape} at {path}, got {values.dtype} {values.shape}"
        )
    return values


def classify(
    ndsm_codes: np.ndarray,
    features: np.ndarray,
    terrain_normal: np.ndarray,
    elevated_minimum_code: int = 30,
) -> np.ndarray:
    if features.shape != terrain_normal.shape or features.ndim != 3 or features.shape[2] != 4:
        raise ValueError("Feature and terrain-normal inputs must be matching RGBA arrays")
    if ndsm_codes.shape != features.shape[:2]:
        raise ValueError("nDSM and feature dimensions differ")
    result = np.zeros(ndsm_codes.shape, dtype=np.uint8)
    validity = terrain_normal[:, :, 3]
    valid = (validity & 1) != 0
    canopy = features[:, :, 0] > 0
    building = features[:, :, 2] > 0
    if np.any(canopy & building):
        raise ValueError("Pinned lidar inference violates exclusive canopy/building classification")
    elevated = ndsm_codes >= int(elevated_minimum_code)
    filled = (validity & ((1 << 4) | (1 << 5))) != 0
    result[valid] |= VALID_SURFACE
    result[elevated] |= ELEVATED_SURFACE
    result[canopy] |= CANOPY | HEURISTIC_CLASSIFICATION
    result[building] |= BUILDING | HEURISTIC_CLASSIFICATION
    result[filled] |= FILLED_HEIGHT_SOURCE
    return result


def _record_from_source(source: dict[str, Any], semantic: str) -> tuple[dict[str, Any], Path]:
    record = asset_by_semantic(source, semantic)
    path = verify_source_asset(record)
    return {
        **file_record(path, semantic),
        "ownership": "referenced_existing_public_domain_derivative",
    }, path


def build(
    source_manifest_path: Path = SOURCE_MANIFEST,
    dtm_dsm_manifest_path: Path = DEFAULT_OUTPUT / DTM_DSM_MANIFEST_NAME,
    output_dir: Path = DEFAULT_OUTPUT,
    write_meta: bool = True,
) -> dict[str, Any]:
    source_manifest_path = source_manifest_path.resolve()
    dtm_dsm_manifest_path = dtm_dsm_manifest_path.resolve()
    output_dir = output_dir.resolve()
    source = load_json(source_manifest_path)
    frame_contract = horizontal_frame_contract(source["coordinate_mapping"])
    dtm_dsm = load_json(dtm_dsm_manifest_path)
    if dtm_dsm.get("schema") != "questflight.photoreal-aerial-surface-v1.dtm-dsm.v1":
        raise ValueError("Unsupported DTM/DSM stage manifest")
    grid = source["coordinate_mapping"]["grid"]
    shape = (int(grid["height_pixels"]), int(grid["width_pixels"]))

    ndsm_record = asset_by_semantic(dtm_dsm, "normalized_digital_surface_model")
    ndsm_path = verify_source_asset(ndsm_record)
    ndsm_codes = load_image(ndsm_path, shape, np.dtype(np.uint16))

    referenced: list[dict[str, Any]] = []
    features_record, features_path = _record_from_source(source, "surface_features_canopy_building")
    normal_record, normal_path = _record_from_source(source, "terrain_surface_normal_and_validity")
    canopy_normal_record, canopy_normal_path = _record_from_source(
        source, "canopy_height_normal_and_occlusion"
    )
    confidence_record, confidence_path = _record_from_source(source, "building_height_confidence")
    referenced.extend(
        [features_record, normal_record, canopy_normal_record, confidence_record]
    )
    features = load_image(features_path, (*shape, 4), np.dtype(np.uint8))
    terrain_normal = load_image(normal_path, (*shape, 4), np.dtype(np.uint8))
    load_image(canopy_normal_path, (*shape, 4), np.dtype(np.uint8))
    load_image(confidence_path, shape, np.dtype(np.uint8))

    ndsm_scale = float(
        ndsm_record["encoding"]["decoded_height_above_ground_meters"]["scale"]
    )
    elevated_minimum_meters = 1.5
    elevated_minimum_code = int(round(elevated_minimum_meters / ndsm_scale))
    classes = classify(ndsm_codes, features, terrain_normal, elevated_minimum_code)
    class_path = output_dir / CLASS_TEXTURE_NAME
    output_dir.mkdir(parents=True, exist_ok=True)
    Image.fromarray(classes).save(class_path, format="PNG", compress_level=9, optimize=False)

    canopy = (classes & CANOPY) != 0
    building = (classes & BUILDING) != 0
    elevated = (classes & ELEVATED_SURFACE) != 0
    valid = (classes & VALID_SURFACE) != 0
    filled = (classes & FILLED_HEIGHT_SOURCE) != 0
    pixel_area = abs(
        float(source["coordinate_mapping"]["pixel_center_affine_local_meters"]["x_per_column"])
        * float(source["coordinate_mapping"]["pixel_center_affine_local_meters"]["z_per_row"])
    )
    manifest = {
        "assets": [
            *referenced,
            {
                **file_record(ndsm_path, "normalized_digital_surface_model"),
                "ownership": "referenced_photoreal_aerial_surface_derivative",
            },
            {
                **file_record(class_path, "exclusive_surface_classification"),
                "dimensions": {"height_pixels": shape[0], "width_pixels": shape[1]},
                "encoding": {
                    "bits_per_channel": 8,
                    "channel": "R",
                    "container": "PNG",
                    "flags": {
                        "bit_0": "valid_surface_return",
                        "bit_1": f"nDSM_at_least_{elevated_minimum_meters}_meters",
                        "bit_2": "canopy_height_present",
                        "bit_3": "building_height_present",
                        "bit_4": "return_structure_heuristic_not_source_LAS_class",
                        "bit_5": "DTM_or_DSM_fill_flag_present",
                    },
                },
                "ownership": "new_compact_public_domain_derivative",
            },
        ],
        "classification_policy": {
            "building_canopy_overlap_pixel_count": int(np.count_nonzero(canopy & building)),
            "building_priority_over_canopy": True,
            "direct_building_class_available": False,
            "direct_vegetation_classes_available": False,
            "elevated_minimum_meters": elevated_minimum_meters,
            "method": "Preserve the pinned lidar return-structure channels; do not relabel them as surveyed LAS classes.",
        },
        "coordinate_mapping": source["coordinate_mapping"],
        "horizontal_frame_contract": frame_contract,
        "license_and_provenance": source["license_and_provenance"],
        "limitations": [
            "Canopy and building classifications are heuristic because the bounded source has no LAS vegetation/building classes.",
            "The exact R8 class mask is a data texture and must remain linear, uncompressed, and without mipmaps.",
            "The classification texture remains on the origin-relative UTM13N lidar grid; product AEQD world positions require the recorded inverse sampling transform.",
            "No surveyed semantic or vertical-error truth study is available.",
        ],
        "metrics": {
            "building_area_square_meters": float(np.count_nonzero(building) * pixel_area),
            "building_pixel_count": int(np.count_nonzero(building)),
            "canopy_area_square_meters": float(np.count_nonzero(canopy) * pixel_area),
            "canopy_pixel_count": int(np.count_nonzero(canopy)),
            "elevated_pixel_count": int(np.count_nonzero(elevated)),
            "filled_height_source_pixel_count": int(np.count_nonzero(filled)),
            "pixel_area_square_meters": pixel_area,
            "valid_surface_pixel_count": int(np.count_nonzero(valid)),
        },
        "schema": "questflight.photoreal-aerial-surface-v1.classification.v1",
        "source_dtm_dsm_manifest": file_record(dtm_dsm_manifest_path),
        "source_lidar_manifest": file_record(source_manifest_path),
    }
    manifest_path = output_dir / MANIFEST_NAME
    write_json(manifest_path, manifest)
    if write_meta:
        write_folder_meta(output_dir, "USGS 3DEP compact DTM/DSM/nDSM and classification data")
        write_texture_meta(
            class_path,
            "Exact heuristic surface-class bitfield; linear R8; no mips; not surveyed truth",
        )
        write_text_meta(manifest_path, "Surface classification provenance, channels, and coverage")
    return manifest


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-manifest", type=Path, default=SOURCE_MANIFEST)
    parser.add_argument(
        "--dtm-dsm-manifest", type=Path, default=DEFAULT_OUTPUT / DTM_DSM_MANIFEST_NAME
    )
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--no-meta", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manifest = build(
        args.source_manifest,
        args.dtm_dsm_manifest,
        args.output,
        not args.no_meta,
    )
    print(json.dumps({"metrics": manifest["metrics"]}, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
