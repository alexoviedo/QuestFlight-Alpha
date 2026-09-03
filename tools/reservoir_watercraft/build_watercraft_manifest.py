#!/usr/bin/env python3
"""Build the reviewed, source-pixel-backed Reservoir Watercraft V1 manifest."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

import cv2
import numpy as np

from build_reservoir_mosaic import CONFIG_PATH, TileFrame, load_service


# Coordinates are pixels in the reviewed level-19 south-reservoir mosaic. Each
# axis pair was measured against the visible craft, not generated as scenery.
# A small, conservative subset was intentionally chosen from much denser storage.
REVIEWED = [
    # Mooring field: lowered/furled sailboats.
    ("MW01", (2610, 1346), (2603, 1358), (2618, 1334), "MOORED_DAY_SAILER", 6.0, 2.05, "sailer_offwhite_navy", "open_water", "white moored hull and mast shadow"),
    ("MW02", (2773, 1355), (2765, 1371), (2781, 1339), "MOORED_DAY_SAILER", 6.5, 2.10, "sailer_offwhite_gray", "open_water", "white moored hull and mast shadow"),
    ("MW03", (3050, 1441), (3043, 1455), (3057, 1426), "MOORED_DAY_SAILER", 6.1, 2.00, "sailer_offwhite_navy", "open_water", "white moored hull, no raised sail"),
    ("MW04", (3078, 1292), (3072, 1304), (3084, 1280), "MOORED_DAY_SAILER", 5.8, 1.95, "sailer_lightgray_charcoal", "open_water", "white moored hull and westward mast shadow"),
    ("MW05", (3149, 1236), (3143, 1250), (3155, 1222), "MOORED_DAY_SAILER", 6.4, 2.10, "sailer_offwhite_navy", "open_water", "white moored hull and westward mast shadow"),
    ("MW06", (3243, 1363), (3236, 1379), (3250, 1348), "MOORED_DAY_SAILER", 6.7, 2.15, "sailer_offwhite_blue", "open_water", "blue-covered moored hull and mast shadow"),
    # Clearly distinct modern motor craft.
    ("PA01", (3782, 1265), (3782, 1278), (3781, 1252), "POWERBOAT_AFLOAT", 5.9, 2.25, "power_offwhite_muted_red", "open_water", "compact white/red motor craft afloat"),
    ("PA02", (4920, 985), (4911, 994), (4929, 976), "POWERBOAT_AFLOAT", 6.6, 2.35, "power_offwhite_navy", "open_water", "modern power craft visibly underway; wake remains in imagery"),
    ("PA03", (4430, 1630), (4427, 1642), (4433, 1618), "POWERBOAT_AFLOAT", 5.6, 2.15, "power_offwhite_charcoal", "open_water", "dark-cockpit craft clear of east launch dock"),
    # Organized beach/rental rows. Bright source pixels are mapped to restrained
    # molded-plastic families rather than copied as saturated marker colors.
    ("KS01", (3290, 1683), (3286, 1695), (3294, 1671), "TOURING_KAYAK_STORED", 4.15, 0.67, "kayak_muted_teal", "shore_storage", "first organized colored beach row"),
    ("KS02", (3302, 1679), (3298, 1691), (3306, 1667), "TOURING_KAYAK_STORED", 4.05, 0.66, "kayak_deep_blue", "shore_storage", "first organized colored beach row"),
    ("KS03", (3315, 1675), (3311, 1687), (3319, 1663), "TOURING_KAYAK_STORED", 4.20, 0.68, "kayak_muted_orange", "shore_storage", "first organized colored beach row"),
    ("KS04", (3328, 1672), (3324, 1684), (3332, 1660), "TOURING_KAYAK_STORED", 4.10, 0.67, "kayak_muted_teal", "shore_storage", "first organized colored beach row"),
    ("KS05", (3341, 1668), (3337, 1680), (3345, 1656), "TOURING_KAYAK_STORED", 4.25, 0.69, "kayak_deep_blue", "shore_storage", "first organized colored beach row"),
    ("KS06", (3354, 1664), (3350, 1676), (3358, 1652), "TOURING_KAYAK_STORED", 4.05, 0.66, "kayak_muted_orange", "shore_storage", "first organized colored beach row"),
    ("SS01", (3240, 1875), (3236, 1885), (3244, 1865), "SUP_STORED", 3.15, 0.82, "sup_offwhite_muted_blue", "shore_storage", "parallel colored board row on developed grass staging"),
    ("SS02", (3253, 1871), (3249, 1881), (3257, 1861), "SUP_STORED", 3.20, 0.84, "sup_muted_teal", "shore_storage", "parallel colored board row on developed grass staging"),
    ("SS03", (3266, 1867), (3262, 1877), (3270, 1857), "SUP_STORED", 3.10, 0.82, "sup_lightgray_charcoal", "shore_storage", "parallel colored board row on developed grass staging"),
    ("SS04", (3279, 1863), (3275, 1873), (3283, 1853), "SUP_STORED", 3.18, 0.83, "sup_offwhite_muted_blue", "shore_storage", "parallel colored board row on developed grass staging"),
    # Conservative subset of the photographed south dry-storage line.
    ("DS01", (2540, 2430), (2538, 2446), (2542, 2414), "DINGHY_STORED", 4.15, 1.42, "dinghy_offwhite_navy", "dry_storage", "small white hull in organized gravel storage"),
    ("DS02", (2670, 2440), (2668, 2456), (2672, 2424), "DINGHY_STORED", 4.25, 1.44, "dinghy_offwhite_gray", "dry_storage", "small white hull in organized gravel storage"),
    ("DS03", (2860, 2460), (2858, 2476), (2862, 2444), "DINGHY_STORED", 4.10, 1.41, "dinghy_offwhite_navy", "dry_storage", "small white hull in organized gravel storage"),
    ("PT01", (3300, 2490), (3297, 2508), (3303, 2472), "POWERBOAT_TRAILERED", 7.75, 2.50, "power_offwhite_charcoal", "dry_storage", "white trailerable hull in south storage line"),
    ("PT02", (3540, 2500), (3537, 2518), (3543, 2482), "POWERBOAT_TRAILERED", 7.95, 2.55, "power_offwhite_navy", "dry_storage", "white trailerable hull in south storage line"),
    ("PT03", (3820, 2490), (3817, 2508), (3823, 2472), "POWERBOAT_TRAILERED", 7.70, 2.48, "power_offwhite_charcoal", "dry_storage", "white trailerable hull in south storage line"),
]


# Source-space semantic anchors. These are deliberately tied to the prepared
# derivative geometry rather than inferred from a generic bounding box.
ANCHORS = {
    "MOORED_DAY_SAILER": ("MODEL_WATERLINE", 0.42, 0.02),
    "POWERBOAT_AFLOAT": ("MODEL_WATERLINE", 0.40, 0.02),
    "TOURING_KAYAK_STORED": ("MODEL_SUPPORT_PLANE", 0.00034, 0.025),
    "SUP_STORED": ("MODEL_SUPPORT_PLANE", 0.0, 0.025),
    "DINGHY_STORED": ("MODEL_SUPPORT_PLANE", 0.01, 0.025),
    "POWERBOAT_TRAILERED": ("MODEL_SUPPORT_PLANE", 0.0, 0.025),
}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--mosaic-report", type=Path, required=True)
    parser.add_argument("--mosaic", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--overlay", type=Path, required=True)
    args = parser.parse_args()

    config = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
    report = json.loads(args.mosaic_report.read_text(encoding="utf-8"))
    service_path = args.mosaic_report.parent / "city_2020_service_metadata.json"
    frame = TileFrame(config, load_service(config, service_path))
    gx0, gy0, _, _ = report["global_pixel_bounds"]
    image = cv2.imread(str(args.mosaic))
    if image is None:
        raise RuntimeError(f"Could not read {args.mosaic}")

    records = []
    overlay = image.copy()
    for ordinal, (suffix, center, start, end, mode, length, width, palette, group, note) in enumerate(REVIEWED, 1):
        center_global = (gx0 + center[0], gy0 + center[1])
        start_global = (gx0 + start[0], gy0 + start[1])
        end_global = (gx0 + end[0], gy0 + end[1])
        x, z = frame.global_pixel_to_world(*center_global)
        sx, sz = frame.global_pixel_to_world(*start_global)
        ex, ez = frame.global_pixel_to_world(*end_global)
        yaw = math.degrees(math.atan2(ex - sx, ez - sz))
        column = int(center_global[0] // 256)
        row = int(center_global[1] // 256)
        derivative = {
            "MOORED_DAY_SAILER": "MOORED_DAY_SAILER",
            "POWERBOAT_AFLOAT": "POWERBOAT_AFLOAT",
            "TOURING_KAYAK_STORED": "TOURING_KAYAK_STORED",
            "SUP_STORED": "SUP_STORED",
            "DINGHY_STORED": "DINGHY_STORED",
            "POWERBOAT_TRAILERED": "POWERBOAT_TRAILERED",
        }[mode]
        anchor_mode, anchor_local_y, clearance = ANCHORS[derivative]
        records.append({
            "stable_id": f"RWV1_{ordinal:03d}_{suffix}",
            "source": {
                "provider": "City of Boulder",
                "imagery_id": "city_of_boulder_2020_9inch",
                "acquisition_period": "Spring and Summer 2020",
                "level": 19,
                "row": row,
                "column": column,
                "pixel_center": [round(center_global[0] - column * 256, 3), round(center_global[1] - row * 256, 3)],
                "mosaic_pixel_center": list(center),
                "long_axis_mosaic_pixels": [list(start), list(end)],
            },
            "product_x_meters": round(x, 3),
            "product_z_meters": round(z, 3),
            "yaw_degrees": round(yaw, 2),
            "footprint_length_meters": length,
            "footprint_width_meters": width,
            "derivative": derivative,
            "placement_mode": mode,
            "spatial_group": group,
            "palette": palette,
            "source_color_confidence": "MEDIUM" if mode not in ("MOORED_DAY_SAILER", "POWERBOAT_AFLOAT") else "HIGH",
            "classification_confidence": "HIGH" if mode in ("MOORED_DAY_SAILER", "TOURING_KAYAK_STORED", "SUP_STORED") else "MEDIUM",
            "evidence_note": note,
            "vertical_authority": "TERRAIN_OWNED_WATER_VISUAL_SURFACE" if group == "open_water" else "ACTIVE_RENDERED_TERRAIN_SURFACE",
            "anchor_mode": anchor_mode,
            "anchor_local_y_meters": anchor_local_y,
            "surface_clearance_meters": clearance,
        })
        color = (255, 210, 0) if group == "open_water" else ((0, 180, 255) if group == "shore_storage" else (180, 80, 255))
        cv2.line(overlay, start, end, color, 3, cv2.LINE_AA)
        cv2.circle(overlay, center, 8, color, 2, cv2.LINE_AA)
        cv2.putText(overlay, str(ordinal), (center[0] + 9, center[1] - 9), cv2.FONT_HERSHEY_SIMPLEX, 0.55, color, 2, cv2.LINE_AA)

    payload = {
        "schema": "questflight.reservoir-watercraft-placements-v1",
        "source_mosaic_sha256": hashlib.sha256(args.mosaic.read_bytes()).hexdigest(),
        "source_mosaic_report_sha256": hashlib.sha256(args.mosaic_report.read_bytes()).hexdigest(),
        "vertical_surface_authority": "ACTIVE_RENDERED_TERRAIN_MESH",
        "records": records,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    args.overlay.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(args.overlay), overlay, [cv2.IMWRITE_JPEG_QUALITY, 96])
    print(json.dumps({"records": len(records), "output": str(args.output), "overlay": str(args.overlay)}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
