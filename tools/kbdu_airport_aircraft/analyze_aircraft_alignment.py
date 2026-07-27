#!/usr/bin/env python3
"""Validate and visualize the authored airport-aircraft/orthophoto registration."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
CONTRACT = Path(__file__).with_name("airport_aircraft_placements_v1.json")
SOURCE_MANIFEST = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/AirportParkedAircraftV1/Data/"
    "AirportParkedAircraftSourceManifestV1.json"
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def heading(axis: list[int]) -> float:
    east = float(axis[0] - axis[2])
    north = float(axis[3] - axis[1])
    return math.degrees(math.atan2(east, north)) % 360.0


def pixel_to_world(pixel: tuple[float, float], source: dict) -> tuple[float, float]:
    width, height = source["dimensions_pixels"]
    min_x, min_z, max_x, max_z = source["world_bounds_meters"]
    return (
        min_x + pixel[0] / width * (max_x - min_x),
        max_z - pixel[1] / height * (max_z - min_z),
    )


def world_to_pixel(world: tuple[float, float], source: dict) -> tuple[float, float]:
    width, height = source["dimensions_pixels"]
    min_x, min_z, max_x, max_z = source["world_bounds_meters"]
    return (
        (world[0] - min_x) / (max_x - min_x) * width,
        (max_z - world[1]) / (max_z - min_z) * height,
    )


def plan_dimensions(manifest: dict) -> dict[str, tuple[float, float]]:
    result: dict[str, tuple[float, float]] = {}
    for output in manifest["outputs"]:
        bounds = output["lods"][0]["bounds_meters"]
        minimum, maximum = bounds["minimum"], bounds["maximum"]
        result[output["family"]] = (
            float(maximum[0] - minimum[0]),
            float(maximum[1] - minimum[1]),
        )
    return result


def local_to_pixel(
    center: tuple[float, float],
    yaw_degrees: float,
    right_meters: float,
    forward_meters: float,
    pixels_per_meter: float,
) -> tuple[float, float]:
    yaw = math.radians(yaw_degrees)
    east = right_meters * math.cos(yaw) + forward_meters * math.sin(yaw)
    north = -right_meters * math.sin(yaw) + forward_meters * math.cos(yaw)
    return center[0] + east * pixels_per_meter, center[1] - north * pixels_per_meter


def rectangle(
    center: tuple[float, float],
    yaw: float,
    center_forward: float,
    width: float,
    depth: float,
    pixels_per_meter: float,
) -> list[tuple[float, float]]:
    return [
        local_to_pixel(center, yaw, right, center_forward + forward, pixels_per_meter)
        for right, forward in (
            (-width * 0.5, -depth * 0.5),
            (width * 0.5, -depth * 0.5),
            (width * 0.5, depth * 0.5),
            (-width * 0.5, depth * 0.5),
        )
    ]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)

    contract = json.loads(CONTRACT.read_text(encoding="utf-8"))
    source = contract["orthophoto"]
    image_path = ROOT / source["path"]
    if sha256(image_path) != source["sha256"]:
        raise RuntimeError("Active orthophoto hash does not match the placement contract")
    meta = Path(str(image_path) + ".meta").read_text(encoding="utf-8")
    if f"guid: {source['guid']}" not in meta:
        raise RuntimeError("Active orthophoto GUID does not match the placement contract")
    image = Image.open(image_path).convert("RGB")
    if list(image.size) != source["dimensions_pixels"]:
        raise RuntimeError("Active orthophoto dimensions do not match the placement contract")

    dimensions = plan_dimensions(json.loads(SOURCE_MANIFEST.read_text(encoding="utf-8")))
    placements = [
        (group, placement)
        for group in contract["groups"]
        for placement in group["placements"]
    ]
    centers = [placement["source_center_pixel"] for _, placement in placements]
    margin = 90
    crop = (
        max(0, min(value[0] for value in centers) - margin),
        max(0, min(value[1] for value in centers) - margin),
        min(image.width, max(value[0] for value in centers) + margin),
        min(image.height, max(value[1] for value in centers) + margin),
    )
    overlay = image.crop(crop).convert("RGBA")
    drawing = ImageDraw.Draw(overlay, "RGBA")
    pixels_per_meter = image.width / (
        source["world_bounds_meters"][2] - source["world_bounds_meters"][0]
    )

    report_rows = []
    maximum_roundtrip = 0.0
    for group, placement in placements:
        family = group["family"]
        center_full = tuple(float(value) for value in placement["source_center_pixel"])
        center = (center_full[0] - crop[0], center_full[1] - crop[1])
        yaw = heading(placement["source_axis_pixels"])
        span, length = dimensions[family]
        color = (0, 255, 255, 180) if family == "sailplane" else (255, 120, 0, 180)
        polygons = (
            rectangle(center, yaw, length * 0.06, span, max(0.45, length * 0.14), pixels_per_meter),
            rectangle(center, yaw, 0.0, max(0.65, span * 0.085), length, pixels_per_meter),
            rectangle(center, yaw, -length * 0.38, span * 0.32, max(0.35, length * 0.09), pixels_per_meter),
        )
        for polygon in polygons:
            drawing.polygon(polygon, outline=color, width=2)
        nose = local_to_pixel(center, yaw, 0.0, length * 0.65, pixels_per_meter)
        drawing.line((center, nose), fill=(255, 255, 0, 230), width=2)
        drawing.ellipse(
            (center[0] - 2, center[1] - 2, center[0] + 2, center[1] + 2),
            fill=(255, 0, 255, 255),
        )
        drawing.text((center[0] + 4, center[1] - 8), placement["id"], fill=(255, 255, 255, 255))

        world = pixel_to_world(center_full, source)
        roundtrip = world_to_pixel(world, source)
        roundtrip_error = math.hypot(roundtrip[0] - center_full[0], roundtrip[1] - center_full[1])
        maximum_roundtrip = max(maximum_roundtrip, roundtrip_error)
        roll = 0.0
        if family == "sailplane":
            magnitude = float(group["resting_pose"]["derived_roll_degrees"])
            roll = magnitude if placement["wing_down_side"] == "left" else -magnitude
        report_rows.append(
            {
                "id": placement["id"],
                "family": family,
                "source_center_pixel": placement["source_center_pixel"],
                "source_axis_pixels": placement["source_axis_pixels"],
                "world_xz_meters": [round(world[0], 4), round(world[1], 4)],
                "yaw_degrees": round(yaw, 3),
                "roll_degrees": roll,
                "wing_down_side": placement.get("wing_down_side", "none"),
                "pixel_world_roundtrip_error_pixels": round(roundtrip_error, 6),
            }
        )

    overlay_path = args.output_dir / "airport_aircraft_runtime_alignment_overlay.png"
    overlay.convert("RGB").save(overlay_path)
    report = {
        "schema_version": 1,
        "active_orthophoto": source,
        "crop_pixels": list(crop),
        "placement_count": len(report_rows),
        "powered_count": sum(row["family"] == "powered_c172" for row in report_rows),
        "sailplane_count": sum(row["family"] == "sailplane" for row in report_rows),
        "maximum_pixel_world_roundtrip_error_pixels": maximum_roundtrip,
        "roundtrip_status": (
            "PASS"
            if maximum_roundtrip
            <= contract["alignment_acceptance"]["maximum_pixel_world_roundtrip_error_pixels"]
            else "FAIL"
        ),
        "placements": report_rows,
    }
    report_path = args.output_dir / "airport_aircraft_runtime_alignment_report.json"
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"overlay": str(overlay_path), "report": str(report_path), **report}, indent=2))


if __name__ == "__main__":
    main()
