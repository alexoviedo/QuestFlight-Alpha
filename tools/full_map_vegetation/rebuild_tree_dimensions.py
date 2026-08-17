#!/usr/bin/env python3
"""Rebuild only physical tree dimensions in the accepted vegetation database.

The tool deliberately preserves placement coordinates, terrain grounding, page
order, tint, rotation, and seed.  It avoids rerunning candidate selection and
therefore cannot churn the already headset-reviewed full-map distribution.
"""

from __future__ import annotations

import argparse
import collections
import json
import math
import struct
from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np
from scipy.spatial import cKDTree

from build_full_map_vegetation import (
    HEADER,
    INSTANCE,
    PAGE,
    RUNTIME_GRID_MIN,
    RUNTIME_PAGE_METERS,
    BuildingIndex,
    CanopyHeightSampler,
    canopy_mask,
    evidence_ranked_dimensions,
    rasterize_buildings,
    sha256,
)


@dataclass
class Record:
    page: int
    local_x: int
    local_z: int
    y_cm: int
    height_cm: int
    crown_cm: int
    rotation: int
    red: int
    green: int
    blue: int
    seed: int
    kind: int
    x: float
    z: float


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-database", type=Path, required=True)
    parser.add_argument("--input-manifest", type=Path, required=True)
    parser.add_argument("--imagery-root", type=Path, required=True)
    parser.add_argument("--clipmap-manifest", type=Path, required=True)
    parser.add_argument("--building-database", type=Path, required=True)
    parser.add_argument("--lidar-manifest", type=Path, required=True)
    parser.add_argument("--accepted-near-placements", type=Path, required=True)
    parser.add_argument("--output-database", type=Path, required=True)
    parser.add_argument("--output-manifest", type=Path, required=True)
    return parser.parse_args()


def read_database(path: Path) -> tuple[tuple, list[tuple[int, int]], list[Record]]:
    payload = path.read_bytes()
    header = HEADER.unpack_from(payload, 0)
    magic, version, minimum_x, minimum_z, page_size, quantization, columns, rows, total = header
    if magic != b"QFFMVG2\0" or version != 2:
        raise ValueError("Unsupported vegetation database")
    pages = [
        PAGE.unpack_from(payload, HEADER.size + page * PAGE.size)
        for page in range(columns * rows)
    ]
    records = []
    offset = HEADER.size + len(pages) * PAGE.size
    for page, (start, count) in enumerate(pages):
        column = page % columns
        row = page // columns
        page_x = minimum_x + column * page_size
        page_z = minimum_z + row * page_size
        for local in range(count):
            values = INSTANCE.unpack_from(payload, offset + (start + local) * INSTANCE.size)
            local_x, local_z = values[0], values[1]
            records.append(
                Record(
                    page=page,
                    local_x=local_x,
                    local_z=local_z,
                    y_cm=values[2],
                    height_cm=values[3],
                    crown_cm=values[4],
                    rotation=values[5],
                    red=values[6],
                    green=values[7],
                    blue=values[8],
                    seed=values[9],
                    kind=values[10],
                    x=page_x + local_x * quantization,
                    z=page_z + local_z * quantization,
                )
            )
    if len(records) != total:
        raise ValueError(f"Instance count mismatch: {len(records)} != {total}")
    return header, pages, records


def write_database(path: Path, header: tuple, pages: list[tuple[int, int]], records: list[Record]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as stream:
        stream.write(HEADER.pack(*header))
        for page in pages:
            stream.write(PAGE.pack(*page))
        for item in records:
            stream.write(
                INSTANCE.pack(
                    item.local_x,
                    item.local_z,
                    item.y_cm,
                    item.height_cm,
                    item.crown_cm,
                    item.rotation,
                    item.red,
                    item.green,
                    item.blue,
                    item.seed,
                    item.kind,
                )
            )


def distribution(values: np.ndarray) -> dict[str, float | int]:
    return {
        "count": int(len(values)),
        "minimum": round(float(np.min(values)), 3),
        "p05": round(float(np.quantile(values, 0.05)), 3),
        "p25": round(float(np.quantile(values, 0.25)), 3),
        "median": round(float(np.quantile(values, 0.50)), 3),
        "p75": round(float(np.quantile(values, 0.75)), 3),
        "p95": round(float(np.quantile(values, 0.95)), 3),
        "maximum": round(float(np.max(values)), 3),
        "unique_centimeter_values": int(len(np.unique(np.rint(values * 100.0)))),
    }


def main() -> None:
    args = parse_args()
    header, pages, records = read_database(args.input_database)
    clipmap = json.loads(args.clipmap_manifest.read_text(encoding="utf-8"))
    high = clipmap["tiers"]["high"]
    pages_by_coordinate = {
        (int(page["column"]), int(page["row"])): page
        for page in clipmap["pages"]
        if page["tier"] == "high"
    }
    buildings = BuildingIndex(args.building_database)
    lidar = CanopyHeightSampler(args.lidar_manifest)
    accepted_document = json.loads(
        args.accepted_near_placements.read_text(encoding="utf-8")
    )
    accepted_records = collections.Counter(
        (
            int(round(float(item["x"]) * 10.0)),
            int(round(float(item["z"]) * 10.0)),
            int(round(float(item["height"]) * 100.0)),
            int(round(float(item["crown_diameter"]) * 100.0)),
        )
        for item in accepted_document["instances"]
    )

    positions = np.asarray([(item.x, item.z) for item in records], dtype=np.float64)
    neighbor_distances = cKDTree(positions).query(positions, k=2, workers=-1)[0][:, 1]
    groups: dict[tuple[int, int], list[int]] = collections.defaultdict(list)
    tile_size = float(high["tile_inner_size_meters"])
    grid_min_x = float(high["grid_min_x"])
    grid_min_z = float(high["grid_min_z"])
    for index, item in enumerate(records):
        column = int(math.floor((item.x - grid_min_x) / tile_size))
        row = int(math.floor((item.z - grid_min_z) / tile_size))
        groups[(column, row)].append(index)

    sources: collections.Counter[str] = collections.Counter()
    preserved = 0
    for ordinal, coordinate in enumerate(sorted(groups, key=lambda value: (value[1], value[0])), 1):
        page = pages_by_coordinate.get(coordinate)
        if page is None:
            raise ValueError(f"No high imagery page for vegetation coordinate {coordinate}")
        source = args.imagery_root / f"{page['asset_name']}.jpg"
        image = cv2.imread(str(source), cv2.IMREAD_COLOR)
        if image is None:
            raise FileNotFoundError(source)
        gutter = int(page["gutter_pixels"])
        inner = image[gutter:-gutter, gutter:-gutter]
        analysis = cv2.resize(
            inner,
            (inner.shape[1] // 4, inner.shape[0] // 4),
            interpolation=cv2.INTER_AREA,
        )
        minimum_x = grid_min_x + coordinate[0] * tile_size
        minimum_z = grid_min_z + coordinate[1] * tile_size
        maximum_x = minimum_x + tile_size
        maximum_z = minimum_z + tile_size
        building_mask = rasterize_buildings(
            buildings, minimum_x, minimum_z, maximum_x, maximum_z, analysis.shape[:2]
        )
        mask, confidence = canopy_mask(analysis, building_mask)
        label_count, labels, stats, _centroids = cv2.connectedComponentsWithStats(mask, 8)
        distance = cv2.distanceTransform(mask, cv2.DIST_L2, 5)
        hsv = cv2.cvtColor(analysis, cv2.COLOR_BGR2HSV)
        meters_per_pixel = tile_size / analysis.shape[1]

        sampled = []
        label_population: collections.Counter[int] = collections.Counter()
        for index in groups[coordinate]:
            item = records[index]
            pixel_x = int(np.clip(round((item.x - minimum_x) / meters_per_pixel - 0.5), 0, analysis.shape[1] - 1))
            pixel_y = int(np.clip(round((maximum_z - item.z) / meters_per_pixel - 0.5), 0, analysis.shape[0] - 1))
            label = int(labels[pixel_y, pixel_x])
            sampled.append((index, pixel_x, pixel_y, label))
            if label > 0:
                label_population[label] += 1

        for index, pixel_x, pixel_y, label in sampled:
            item = records[index]
            accepted_key = (
                int(round(item.x * 10.0)),
                int(round(item.z * 10.0)),
                item.height_cm,
                item.crown_cm,
            )
            if accepted_records[accepted_key] > 0:
                accepted_records[accepted_key] -= 1
                sources["accepted_near"] += 1
                preserved += 1
                continue
            if label > 0 and label < label_count:
                area_m2 = float(stats[label, cv2.CC_STAT_AREA]) * meters_per_pixel ** 2
                local_edge_diameter = float(distance[pixel_y, pixel_x] * 2.0 * meters_per_pixel)
                territory_diameter = math.sqrt(
                    4.0 * area_m2 / max(1, label_population[label]) / math.pi
                )
            else:
                area_m2 = math.pi * max(2.2, neighbor_distances[index] * 0.48) ** 2
                local_edge_diameter = max(2.2, neighbor_distances[index] * 0.72)
                territory_diameter = max(2.2, neighbor_distances[index] * 0.86)
            structure = float(confidence[pixel_y, pixel_x])
            saturation = float(np.clip(hsv[pixel_y, pixel_x, 1] / 150.0, 0.0, 1.0))
            lit_value = float(np.clip(hsv[pixel_y, pixel_x, 2] / 210.0, 0.0, 1.0))
            observed_shape_scale = float(
                np.clip(
                    0.74 + structure * 0.24 + saturation * 0.10 + (1.0 - lit_value) * 0.08,
                    0.74,
                    1.16,
                )
            )
            crown = float(
                np.clip(
                    (
                        10.0 * math.tanh(neighbor_distances[index] / 10.0) * 0.46
                        + 12.0 * math.tanh(local_edge_diameter / 12.0) * 0.34
                        + 10.0 * math.tanh(territory_diameter / 10.0) * 0.20
                    )
                    * observed_shape_scale,
                    2.2,
                    17.5,
                )
            )
            lidar_height, lidar_confidence = lidar.sample(item.x, item.z, crown)
            height, kind, height_source = evidence_ranked_dimensions(
                crown,
                area_m2,
                local_edge_diameter,
                structure,
                saturation,
                lit_value,
                lidar_height,
                lidar_confidence,
            )
            item.height_cm = int(np.clip(round(height * 100.0), 75, 3000))
            item.crown_cm = int(np.clip(round(crown * 100.0), 150, 2000))
            item.kind = kind
            sources[height_source] += 1
        if ordinal % 64 == 0 or ordinal == len(groups):
            print(f"processed {ordinal}/{len(groups)} accepted-placement imagery pages", flush=True)

    write_database(args.output_database, header, pages, records)
    manifest = json.loads(args.input_manifest.read_text(encoding="utf-8"))
    heights = np.asarray([item.height_cm * 0.01 for item in records], dtype=np.float64)
    crowns = np.asarray([item.crown_cm * 0.01 for item in records], dtype=np.float64)
    kinds = collections.Counter(item.kind for item in records)
    manifest["processing"]["dimension_rebuilder"] = "tools/full_map_vegetation/rebuild_tree_dimensions.py"
    manifest["processing"]["height_method"] = (
        "orthophoto-owned local-crown evidence; exact aligned 3DEP blend where supported; "
        "deterministic context allometry elsewhere; no hash-derived height"
    )
    manifest["processing"]["dimension_preservation"] = (
        "placement, terrain base, page order, tint, rotation, and seed preserved byte-for-byte"
    )
    manifest["sources"]["lidar_manifest"] = {
        "path": str(args.lidar_manifest),
        "sha256": sha256(args.lidar_manifest),
        "authority": "bounded height evidence only; orthophoto retains semantic ownership",
    }
    manifest["sources"]["accepted_near_placements"] = {
        "path": str(args.accepted_near_placements),
        "sha256": sha256(args.accepted_near_placements),
    }
    manifest["statistics"].update(
        {
            "runtime_instances": len(records),
            "ordinary_trees": kinds[0],
            "tall_narrow_trees": kinds[1],
            "shrubs": kinds[2],
            "binary_bytes": args.output_database.stat().st_size,
            "binary_sha256": sha256(args.output_database),
            "accepted_near_instances_preserved": preserved,
            "height_sources": dict(sorted(sources.items())),
            "height_distribution_meters": distribution(heights),
            "crown_distribution_meters": distribution(crowns),
            "legacy_height_cap_23_5_count": int(np.count_nonzero(np.isclose(heights, 23.5))),
            "legacy_crown_cap_15_5_count": int(np.count_nonzero(np.isclose(crowns, 15.5))),
        }
    )
    manifest["limitations"] = [
        "RGB orthophoto segmentation is semantic placement evidence, not an individual-tree survey.",
        "Aligned 3DEP height evidence is bounded to its retained coverage; deterministic orthophoto/context allometry owns uncovered or unsupported crowns.",
        "The 3DEP work unit has no vegetation classes in the product bounds, so lidar never creates a vegetation placement.",
        "Raw orthophotos and downloaded high-poly reference assets remain external and uncommitted.",
    ]
    args.output_manifest.parent.mkdir(parents=True, exist_ok=True)
    args.output_manifest.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest["statistics"], indent=2))


if __name__ == "__main__":
    main()
