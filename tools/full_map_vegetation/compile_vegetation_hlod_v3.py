#!/usr/bin/env python3
"""Compile the accepted V2 placement database into a coverage-safe V3 database.

V2 sorted each runtime page by classifier confidence.  The Quest renderer then
read only the first 144 records, so a few high-confidence crowns could consume a
page's entire far representation while most of its photographed canopy vanished.

V3 keeps every accepted individual placement and places a spatially fair
mixture of real trees and shrubs first in each page.  Runtime prefixes therefore
remain tied to source evidence and never turn a grove into one oversized blob.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
from collections import Counter
from dataclasses import dataclass, replace
from pathlib import Path

import numpy as np
from PIL import Image


V2_MAGIC = b"QFFMVG2\0"
V3_MAGIC = b"QFFMVG3\0"
V2_VERSION = 2
V3_VERSION = 3
HEADER = struct.Struct("<8sIffffIII")
PAGE = struct.Struct("<II")
INSTANCE = struct.Struct("<HHiHHHBBBBB")
FAR_PREFIX_RECORDS = 144
FAR_PREFIX_SHRUBS = 16
SHRUB_MAX_HEIGHT_METERS = 7.5
SHRUB_MAX_CROWN_METERS = 7.0
SEMANTIC_WORLD_MIN = -12000.0
SEMANTIC_WORLD_SIZE = 24000.0


@dataclass(frozen=True)
class Record:
    x: float
    z: float
    y_cm: int
    height_cm: int
    crown_cm: int
    rotation: int
    red: int
    green: int
    blue: int
    seed: int
    kind: int


class SemanticExclusion:
    def __init__(self, path: Path):
        packed = np.asarray(Image.open(path).convert("RGB"))
        if packed.shape[0] != packed.shape[1] or packed.shape[0] < 1024:
            raise ValueError("Semantic exclusion mask has an invalid grid")
        self.path = path
        self.water = packed[:, :, 0] >= 128
        self.sports = packed[:, :, 1] >= 128
        self.excluded = self.water | self.sports

    def pixel(self, x: float, z: float) -> tuple[int, int]:
        size = self.water.shape[0]
        column = int(np.clip(round((x - SEMANTIC_WORLD_MIN) / SEMANTIC_WORLD_SIZE * (size - 1)), 0, size - 1))
        row = int(np.clip(round((SEMANTIC_WORLD_MIN + SEMANTIC_WORLD_SIZE - z) / SEMANTIC_WORLD_SIZE * (size - 1)), 0, size - 1))
        return row, column

    def category(self, x: float, z: float) -> str | None:
        row, column = self.pixel(x, z)
        if self.water[row, column]:
            return "water"
        if self.sports[row, column]:
            return "athletic_surface"
        return None

def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def hash32(*values: int) -> int:
    result = 2166136261
    for value in values:
        result ^= value & 0xFFFFFFFF
        result = (result * 16777619) & 0xFFFFFFFF
    return result


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-binary", type=Path, required=True)
    parser.add_argument("--input-manifest", type=Path, required=True)
    parser.add_argument("--output-binary", type=Path, required=True)
    parser.add_argument("--output-manifest", type=Path, required=True)
    parser.add_argument("--semantic-exclusion-mask", type=Path, required=True)
    parser.add_argument("--semantic-exclusion-manifest", type=Path, required=True)
    return parser.parse_args()


def read_v2(path: Path):
    data = path.read_bytes()
    magic, version, minimum_x, minimum_z, page_size, quantization, columns, rows, count = (
        HEADER.unpack_from(data, 0)
    )
    if magic != V2_MAGIC or version != V2_VERSION:
        raise ValueError("Input is not a full-map vegetation V2 database")
    page_count = columns * rows
    expected = HEADER.size + page_count * PAGE.size + count * INSTANCE.size
    if len(data) != expected:
        raise ValueError(f"Input length mismatch: {len(data)} != {expected}")
    page_records: list[list[Record]] = []
    table_offset = HEADER.size
    records_offset = HEADER.size + page_count * PAGE.size
    for page in range(page_count):
        offset, page_count_records = PAGE.unpack_from(data, table_offset + page * PAGE.size)
        column = page % columns
        row = page // columns
        page_x = minimum_x + column * page_size
        page_z = minimum_z + row * page_size
        records = []
        for local in range(page_count_records):
            raw = INSTANCE.unpack_from(data, records_offset + (offset + local) * INSTANCE.size)
            records.append(Record(
                x=page_x + raw[0] * quantization,
                z=page_z + raw[1] * quantization,
                y_cm=raw[2],
                height_cm=raw[3],
                crown_cm=raw[4],
                rotation=raw[5],
                red=raw[6], green=raw[7], blue=raw[8], seed=raw[9], kind=raw[10],
            ))
        page_records.append(records)
    return (minimum_x, minimum_z, page_size, quantization, columns, rows), page_records


def reclassify(record: Record) -> Record:
    height = record.height_cm * 0.01
    crown = record.crown_cm * 0.01
    if record.kind == 2 or (
        height <= SHRUB_MAX_HEIGHT_METERS and crown <= SHRUB_MAX_CROWN_METERS
    ):
        return replace(record, kind=2)
    return record


def spatial_individual_order(records: list[Record], page_x: float, page_z: float) -> list[Record]:
    # A stable blue-noise-like hash makes every prefix cover the whole page.
    # This is intentionally independent of classifier confidence.
    return sorted(records, key=lambda item: hash32(
        int((item.x - page_x) * 10.0),
        int((item.z - page_z) * 10.0),
        item.seed,
    ))


def far_prefix_individual_order(
    records: list[Record], page_x: float, page_z: float
) -> list[Record]:
    """Place a spatially fair real-tree/shrub mixture in the first 144 records."""
    ordered = spatial_individual_order(records, page_x, page_z)
    trees = [item for item in ordered if item.kind != 2]
    shrubs = [item for item in ordered if item.kind == 2]
    shrub_count = min(FAR_PREFIX_SHRUBS, len(shrubs), FAR_PREFIX_RECORDS)
    tree_count = min(FAR_PREFIX_RECORDS - shrub_count, len(trees))
    prefix = trees[:tree_count] + shrubs[:shrub_count]
    if len(prefix) < min(FAR_PREFIX_RECORDS, len(ordered)):
        used = {id(item) for item in prefix}
        prefix.extend(item for item in ordered if id(item) not in used)
        prefix = prefix[:min(FAR_PREFIX_RECORDS, len(ordered))]
    used = {id(item) for item in prefix}
    suffix = [item for item in ordered if id(item) not in used]
    return prefix + suffix


def write_v3(
    path: Path,
    contract,
    pages: list[list[Record]],
):
    minimum_x, minimum_z, page_size, quantization, columns, rows = contract
    flattened: list[Record] = []
    table = []
    for page, source in enumerate(pages):
        column = page % columns
        row = page // columns
        page_x = minimum_x + column * page_size
        page_z = minimum_z + row * page_size
        individuals = [reclassify(item) for item in source]
        ordered = far_prefix_individual_order(individuals, page_x, page_z)
        table.append((len(flattened), len(ordered)))
        flattened.extend(ordered)

    payload = bytearray(HEADER.pack(
        V3_MAGIC, V3_VERSION, minimum_x, minimum_z, page_size, quantization,
        columns, rows, len(flattened),
    ))
    for offset, count in table:
        payload.extend(PAGE.pack(offset, count))
    for page, (offset, count) in enumerate(table):
        column = page % columns
        row = page // columns
        page_x = minimum_x + column * page_size
        page_z = minimum_z + row * page_size
        for item in flattened[offset:offset + count]:
            local_x = int(np.clip(round((item.x - page_x) / quantization), 0, 10000))
            local_z = int(np.clip(round((item.z - page_z) / quantization), 0, 10000))
            payload.extend(INSTANCE.pack(
                local_x, local_z, item.y_cm, item.height_cm, item.crown_cm,
                item.rotation, item.red, item.green, item.blue, item.seed, item.kind,
            ))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)
    return flattened


def main() -> None:
    args = parse_args()
    contract, pages = read_v2(args.input_binary)
    semantic = SemanticExclusion(args.semantic_exclusion_mask)
    rejection_counts: Counter = Counter()
    filtered_pages: list[list[Record]] = []
    for records in pages:
        retained = []
        for item in records:
            category = semantic.category(item.x, item.z)
            if category is None:
                retained.append(item)
            else:
                rejection_counts[category] += 1
                rejection_counts[f"{category}_kind_{item.kind}"] += 1
        filtered_pages.append(retained)
    flattened = write_v3(args.output_binary, contract, filtered_pages)
    source_manifest = json.loads(args.input_manifest.read_text(encoding="utf-8"))
    kinds = Counter(item.kind for item in flattened)
    manifest = {
        "schema": "questflight.full-map-vegetation-hlod.v3.2",
        "contract": {
            "coordinate_frame": source_manifest["contract"]["coordinate_frame"],
            "bounds_meters": source_manifest["contract"]["bounds_meters"],
            "runtime_page_meters": contract[2],
            "columns": contract[4],
            "rows": contract[5],
            "far_prefix": "up to 128 real trees plus 16 shrubs in stable whole-page spatial order",
            "near_suffix": "all remaining real individual trees and shrubs in stable spatial order",
            "geometry": "volumetric near trees/shrubs plus small eight-view individual impostors",
            "far_handoff": "orthophoto-authoritative canopy color plus aggregate crown normals",
        },
        "processing": {
            "generator": "tools/full_map_vegetation/compile_vegetation_hlod_v3.py",
            "source_schema": source_manifest["schema"],
            "far_prefix_records": FAR_PREFIX_RECORDS,
            "far_prefix_shrub_reserve": FAR_PREFIX_SHRUBS,
            "shrub_rule": {
                "maximum_height_meters": SHRUB_MAX_HEIGHT_METERS,
                "maximum_crown_meters": SHRUB_MAX_CROWN_METERS,
                "authority": "bounded low-vegetation class; no random reassignment",
            },
            "individual_order": "stable real-placement spatial hash with a bounded shrub reserve",
            "semantic_exclusion": (
                "offline hard water-interior and verified athletic-playing-surface center rejection"
            ),
            "grove_policy": "no aggregate grove geometry; grouped real placements form groves",
        },
        "sources": {
            "v2_database": {"path": str(args.input_binary), "sha256": sha256(args.input_binary)},
            "v2_manifest": {"path": str(args.input_manifest), "sha256": sha256(args.input_manifest)},
            "upstream": source_manifest.get("sources", {}),
            "semantic_exclusion_mask": {
                "path": str(args.semantic_exclusion_mask),
                "sha256": sha256(args.semantic_exclusion_mask),
            },
            "semantic_exclusion_manifest": {
                "path": str(args.semantic_exclusion_manifest),
                "sha256": sha256(args.semantic_exclusion_manifest),
            },
        },
        "statistics": {
            "runtime_records": len(flattened),
            "ordinary_trees": kinds[0],
            "tall_narrow_trees": kinds[1],
            "shrubs": kinds[2],
            "grove_hlods": 0,
            "binary_bytes": args.output_binary.stat().st_size,
            "binary_sha256": sha256(args.output_binary),
            "source_individuals": sum(len(page) for page in pages),
            "retained_individuals": sum(len(page) for page in filtered_pages),
            "semantic_rejections": dict(sorted(rejection_counts.items())),
        },
        "limitations": [
            "Orthophoto segmentation is semantic placement evidence, not an individual-tree survey.",
            "Far geometry is a bounded sample of real placements; the orthophoto owns continuous canopy fill.",
            "Raw imagery and downloaded high-poly references remain external and uncommitted.",
        ],
    }
    args.output_manifest.parent.mkdir(parents=True, exist_ok=True)
    args.output_manifest.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest["statistics"], indent=2))


if __name__ == "__main__":
    main()
