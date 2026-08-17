#!/usr/bin/env python3
"""Machine gate and all-page evidence for the Boulder facade V2 derivative."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import pathlib
from collections import Counter

from PIL import Image, ImageDraw, ImageFont


COLORS = [
    (218, 205, 177), (151, 129, 103), (182, 173, 150), (156, 75, 55),
    (133, 89, 65), (184, 150, 103), (199, 184, 151), (213, 200, 169),
    (156, 145, 126), (168, 156, 136), (150, 147, 137), (151, 126, 94),
    (183, 151, 104), (157, 137, 109), (65, 63, 58), (143, 103, 70),
]


def buildings(database: dict):
    for page in database["pages"]:
        for building in page.get("buildings", []):
            yield page, building


def geometry_hash(database: dict, fields: list[str]) -> str:
    digest = hashlib.sha256()
    for page in database["pages"]:
        digest.update(f"{page['column']}:{page['row']}|".encode("ascii"))
        for building in page.get("buildings", []):
            digest.update(json.dumps([building.get(field) for field in fields],
                                     separators=(",", ":")).encode("utf-8"))
    return digest.hexdigest()


def entropy(counter: Counter) -> float:
    total = sum(counter.values())
    return -sum((value / total) * math.log2(value / total) for value in counter.values() if value)


def heatmap(database: dict, output: pathlib.Path, entropy_map: bool) -> None:
    columns, rows = int(database["columns"]), int(database["rows"])
    scale = 36
    image = Image.new("RGB", (columns * scale, rows * scale), (32, 31, 28))
    draw = ImageDraw.Draw(image)
    for page in database["pages"]:
        counts = Counter(int(item["material_id"]) for item in page.get("buildings", []))
        if entropy_map:
            value = min(1.0, entropy(counts) / 3.4) if counts else 0.0
            color = (int(48 + value * 172), int(43 + value * 150), int(36 + value * 102))
        else:
            color = COLORS[counts.most_common(1)[0][0]] if counts else (28, 28, 28)
        x = int(page["column"]) * scale
        y = (rows - 1 - int(page["row"])) * scale
        draw.rectangle((x, y, x + scale - 1, y + scale - 1), fill=color, outline=(18, 18, 18))
        draw.text((x + 2, y + 2), str(sum(counts.values())), fill=(245, 241, 226))
    image.save(output, optimize=True)


def palette_sheet(atlas_path: pathlib.Path, names: list[str], output: pathlib.Path) -> None:
    atlas = Image.open(atlas_path).convert("RGB")
    cell = atlas.width // 4
    sheet = Image.new("RGB", (4 * 360, 4 * 300), (24, 23, 21))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default()
    for index, name in enumerate(names):
        crop = atlas.crop(((index % 4) * cell, (index // 4) * cell,
                           (index % 4 + 1) * cell, (index // 4 + 1) * cell))
        crop.thumbnail((340, 250), Image.Resampling.LANCZOS)
        x, y = (index % 4) * 360 + 10, (index // 4) * 300 + 10
        sheet.paste(crop, (x, y))
        draw.text((x, y + 258), f"{index:02d} {name}", fill=(236, 229, 210), font=font)
    sheet.save(output, optimize=True)


def validate(args: argparse.Namespace) -> dict:
    contract = json.loads(args.contract.read_text(encoding="utf-8"))
    database = json.loads(args.database.read_text(encoding="utf-8"))
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    source_manifest = json.loads(args.source_manifest.read_text(encoding="utf-8"))
    args.output_dir.mkdir(parents=True, exist_ok=True)
    all_buildings = [item for _, item in buildings(database)]
    failures = []
    if len(all_buildings) != contract["expected_buildings"]:
        failures.append("building count changed")
    if len(database["pages"]) != contract["expected_pages"]:
        failures.append("page coverage changed")
    required = ("material_id", "facade_style_id", "semantic_class_id", "semantic_region_id",
                "semantic_confidence", "parcel_role_id", "material_rule_id")
    if any(any(key not in item for key in required) for item in all_buildings):
        failures.append("semantic assignment is incomplete")
    materials = Counter(int(item["material_id"]) for item in all_buildings)
    if len(materials) < contract["minimum_used_materials"]:
        failures.append("material diversity is below contract")
    global_share = max(materials.values()) / len(all_buildings)
    if global_share > contract["maximum_global_material_share"]:
        failures.append(f"global material repetition {global_share:.3f} exceeds contract")
    if manifest.get("small_false_industrial_count") != contract["maximum_false_small_industrial"]:
        failures.append("small Garage/Shed industrial contamination remains")
    if manifest.get("blue_facade_count") != contract["maximum_explicit_blue_facades"]:
        failures.append("unsupported blue facade family remains")
    if source_manifest.get("explicit_blue_facade_family") is not False:
        failures.append("source manifest does not forbid unsupported blue facades")
    if len(source_manifest.get("slices", [])) != contract["expected_materials"]:
        failures.append("runtime facade array does not contain 16 slices")
    if len({item["source_asset"] for item in source_manifest.get("slices", [])}) != contract["expected_materials"]:
        failures.append("facade V2 reuses a photographic source across material families")

    current_hash = geometry_hash(database, contract["geometry_fields"])
    if current_hash != manifest.get("geometry_hash_after") or \
            manifest.get("geometry_hash_before") != manifest.get("geometry_hash_after"):
        failures.append("geometry hash changed during facade enrichment")
    if args.baseline:
        baseline = json.loads(args.baseline.read_text(encoding="utf-8"))
        baseline_hash = geometry_hash(baseline, contract["geometry_fields"])
        if baseline_hash != current_hash:
            failures.append("V2 geometry differs from V1 checkpoint")
    else:
        baseline_hash = None

    dense_shares, dense_entropies, page_rows = [], [], []
    for page in database["pages"]:
        counts = Counter(int(item["material_id"]) for item in page.get("buildings", []))
        total = sum(counts.values())
        share = max(counts.values()) / total if total else 0.0
        bits = entropy(counts) if total else 0.0
        if total >= contract["minimum_dense_page_buildings"]:
            dense_shares.append(share)
            dense_entropies.append(bits)
        page_rows.append((page["column"], page["row"], total, share, bits,
                          counts.most_common(1)[0][0] if counts else -1))
    maximum_dense_share = max(dense_shares) if dense_shares else 0.0
    median_entropy = sorted(dense_entropies)[len(dense_entropies) // 2] if dense_entropies else 0.0
    if maximum_dense_share > contract["maximum_dense_page_material_share"]:
        failures.append(f"dense-page repetition {maximum_dense_share:.3f} exceeds contract")
    if median_entropy < contract["minimum_dense_page_median_entropy_bits"]:
        failures.append(f"dense-page entropy {median_entropy:.3f} is below contract")

    with (args.output_dir / "facade_v2_page_stats.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream)
        writer.writerow(("column", "row", "building_count", "dominant_share", "entropy_bits", "dominant_material"))
        writer.writerows(page_rows)
    heatmap(database, args.output_dir / "facade_v2_dominant_material_heatmap.png", False)
    heatmap(database, args.output_dir / "facade_v2_entropy_heatmap.png", True)
    palette_sheet(args.albedo, manifest["material_names"],
                  args.output_dir / "facade_v2_palette_contact_sheet.png")
    report = {
        "contract": contract["contract"], "passed": not failures, "failures": failures,
        "building_count": len(all_buildings), "page_count": len(database["pages"]),
        "geometry_hash": current_hash, "baseline_geometry_hash": baseline_hash,
        "material_counts": dict(sorted(materials.items())),
        "global_maximum_material_share": global_share,
        "dense_page_maximum_material_share": maximum_dense_share,
        "dense_page_median_entropy_bits": median_entropy,
        "renderers_added": 0, "materials_added": 0, "transparent_slots_added": 0,
    }
    (args.output_dir / "facade_v2_quality_report.json").write_text(
        json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    if failures:
        raise SystemExit(1)
    return report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--database", type=pathlib.Path, required=True)
    parser.add_argument("--manifest", type=pathlib.Path, required=True)
    parser.add_argument("--source-manifest", type=pathlib.Path, required=True)
    parser.add_argument("--albedo", type=pathlib.Path, required=True)
    parser.add_argument("--baseline", type=pathlib.Path)
    parser.add_argument("--contract", type=pathlib.Path,
                        default=pathlib.Path(__file__).with_name("facade_v2_quality_contract.json"))
    parser.add_argument("--output-dir", type=pathlib.Path, required=True)
    args = parser.parse_args()
    validate(args)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
