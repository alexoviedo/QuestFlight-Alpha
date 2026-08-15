#!/usr/bin/env python3
"""Fail-closed validation and worst-window planning budget for the building clipmap."""

from __future__ import annotations

import argparse
import json
import math
import pathlib


def selected(building: dict, lod: int) -> bool:
    if lod == 0 or building["landmark"]:
        return True
    if lod == 1:
        return building["area_m2"] >= 2000.0 or building["height_dm"] >= 450
    return building["area_m2"] >= 8000.0 or building["height_dm"] >= 700


def triangles(building: dict, lod: int) -> int:
    ring = building[f"lod{lod}_q"]
    vertex_count = len(ring) // 2
    return max(0, vertex_count * 2 + vertex_count - 2)


def desired_lod(database: dict, x: float, z: float, column: int, row: int) -> int:
    extent = database["map_half_extent_meters"]
    size = database["page_size_meters"]
    center_x = -extent + (column + 0.5) * size
    center_z = -extent + (row + 0.5) * size
    distance = math.hypot(x - center_x, z - center_z)
    if distance <= database["lod0_max_distance_meters"]:
        return 0
    return 1 if distance <= database["lod1_max_distance_meters"] else 2


def window(database: dict, x: float, z: float) -> tuple[int, int, int]:
    size = database["page_size_meters"]
    extent = database["map_half_extent_meters"]
    cells = database["runtime_window_cells"]
    center_column = math.floor((x + extent) / size)
    center_row = math.floor((z + extent) / size)
    minimum_column = min(database["columns"] - cells, max(0, center_column - 1))
    minimum_row = min(database["rows"] - cells, max(0, center_row - 1))
    by_cell = {(page["column"], page["row"]): page for page in database["pages"]}
    draw_count = triangle_count = 0
    for row in range(minimum_row, minimum_row + cells):
        for column in range(minimum_column, minimum_column + cells):
            lod = desired_lod(database, x, z, column, row)
            values = [item for item in by_cell[(column, row)]["buildings"] if selected(item, lod)]
            if values:
                draw_count += 1
                triangle_count += sum(triangles(item, lod) for item in values)
    return draw_count, triangle_count, minimum_column + minimum_row * database["columns"]


def validate(database: dict, config: dict) -> dict:
    failures: list[str] = []
    pages = database.get("pages", [])
    buildings = [building for page in pages for building in page.get("buildings", [])]
    if database.get("contract_version") != "full-map-building-clipmap-v1":
        failures.append("contract version mismatch")
    if len(pages) != 576 or database.get("columns") != 24 or database.get("rows") != 24:
        failures.append("24 x 24 full-map page coverage is incomplete")
    if len(buildings) < 50000:
        failures.append("compiled building coverage fell below 50,000 components")
    identities = [(item["source_id"], item["component"]) for item in buildings]
    if len(identities) != len(set(identities)):
        failures.append("duplicate source/component ownership remains")
    if any(item["height_dm"] < 24 or item["height_dm"] > 900 for item in buildings):
        failures.append("building height lies outside the authored 2.4-90 m contract")
    if any(item["roof_id"] not in (0, 1, 2) for item in buildings):
        failures.append("unsupported roof profile exists")
    if any(len(item["lod0_q"]) < 6 or len(item["lod0_q"]) % 2 for item in buildings):
        failures.append("invalid footprint ring exists")

    hero = config["authored_hero_exclusion_bounds_xz"]
    extent = database["map_half_extent_meters"]
    size = database["page_size_meters"]
    quantum = database["quantization_meters"]
    hero_centroids = 0
    for page in pages:
        page_x = -extent + page["column"] * size
        page_z = -extent + page["row"] * size
        for item in page["buildings"]:
            ring = item["lod0_q"]
            xs = [page_x + value * quantum for value in ring[0::2]]
            zs = [page_z + value * quantum for value in ring[1::2]]
            x, z = sum(xs) / len(xs), sum(zs) / len(zs)
            if hero[0] <= x <= hero[2] and hero[1] <= z <= hero[3]:
                hero_centroids += 1
    if hero_centroids:
        failures.append(f"{hero_centroids} generic components overlap the airport hero exclusion")

    worst = {"draws": 0, "triangles": 0, "position": [0.0, 0.0]}
    samples = []
    for row in range(database["rows"]):
        for column in range(database["columns"]):
            base_x = -extent + (column + 0.5) * size
            base_z = -extent + (row + 0.5) * size
            for offset_x, offset_z in ((0.0, 0.0), (-0.49, -0.49), (-0.49, 0.49), (0.49, -0.49), (0.49, 0.49)):
                x, z = base_x + offset_x * size, base_z + offset_z * size
                draws, tris, _ = window(database, x, z)
                samples.append(tris)
                if tris > worst["triangles"]:
                    worst = {"draws": draws, "triangles": tris, "position": [round(x, 1), round(z, 1)]}
    maximum = int(config["runtime_contract"]["maximum_active_building_triangles"])
    if worst["triangles"] > maximum:
        failures.append(f"worst active triangles {worst['triangles']:,} exceed {maximum:,}")
    if worst["draws"] > int(config["runtime_contract"]["maximum_active_renderers"]):
        failures.append("active renderer ceiling exceeded")

    return {
        "status": "PASS" if not failures else "FAIL",
        "failures": failures,
        "compiled_components": len(buildings),
        "page_count": len(pages),
        "nonempty_pages": sum(bool(page["buildings"]) for page in pages),
        "landmark_components": sum(bool(item["landmark"]) for item in buildings),
        "pitched_roof_components": sum(item["roof_id"] != 0 for item in buildings),
        "hero_overlap_centroids": hero_centroids,
        "sampled_window_count": len(samples),
        "median_active_triangles": sorted(samples)[len(samples) // 2],
        "maximum_active_draws": worst["draws"],
        "maximum_active_triangles": worst["triangles"],
        "worst_sample_position_xz": worst["position"],
        "maximum_materials": 1,
        "transparent_slots": 0,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--database", type=pathlib.Path, required=True)
    parser.add_argument("--config", type=pathlib.Path, default=pathlib.Path(__file__).with_name("config.json"))
    parser.add_argument("--report", type=pathlib.Path, required=True)
    args = parser.parse_args()
    database = json.loads(args.database.read_text(encoding="utf-8"))
    config = json.loads(args.config.read_text(encoding="utf-8"))
    report = validate(database, config)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if report["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
