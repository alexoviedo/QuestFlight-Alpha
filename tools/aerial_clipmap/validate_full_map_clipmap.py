"""Validate the complete offline aerial page pyramid and create review previews.

This validator is deliberately independent from Unity. It proves that every
manifest page is present, has the expected dimensions/hash/coverage, and that
the shared geographic gutters agree before Android compression is introduced.
Review images are written to an external artifact directory, never to Git.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
from collections import defaultdict
from typing import Any, Iterable

import numpy as np
from PIL import Image, ImageDraw


def sha256_file(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def page_path(output_root: pathlib.Path, record: dict[str, Any]) -> pathlib.Path:
    return output_root / record["tier"] / f"{record['asset_name']}.jpg"


def expected_page_count(manifest: dict[str, Any]) -> int:
    tiers = manifest["tiers"]
    return (
        int(tiers["high"]["columns"]) * int(tiers["high"]["rows"])
        + int(tiers["medium"]["columns"]) * int(tiers["medium"]["rows"])
        + 1
    )


def overlap_errors(
    output_root: pathlib.Path,
    records: Iterable[dict[str, Any]],
) -> list[dict[str, Any]]:
    by_cell = {
        (int(record["column"]), int(record["row"])): record
        for record in records
    }
    results: list[dict[str, Any]] = []
    cache: dict[tuple[int, int], np.ndarray] = {}

    def pixels(cell: tuple[int, int]) -> np.ndarray:
        if cell not in cache:
            cache[cell] = np.asarray(
                Image.open(page_path(output_root, by_cell[cell])).convert("RGB"),
                dtype=np.int16,
            )
        return cache[cell]

    for (column, row), record in sorted(by_cell.items()):
        gutter = int(record["gutter_pixels"])
        if gutter <= 0:
            continue
        width = gutter * 2
        current = pixels((column, row))
        east = (column + 1, row)
        if east in by_cell:
            delta = np.abs(current[:, -width:] - pixels(east)[:, :width])
            results.append(_error_record("east", column, row, delta))
        north = (column, row + 1)
        if north in by_cell:
            # Images are north-up: the lower page's north overlap is at its
            # top, while the upper page's south overlap is at its bottom.
            delta = np.abs(current[:width, :] - pixels(north)[-width:, :])
            results.append(_error_record("north", column, row, delta))
        # A row cache bounds validation memory without changing evidence.
        for cell in list(cache):
            if cell[1] < row - 1:
                del cache[cell]
    return results


def _error_record(
    direction: str,
    column: int,
    row: int,
    delta: np.ndarray,
) -> dict[str, Any]:
    return {
        "column": column,
        "row": row,
        "direction": direction,
        "mean_absolute_rgb": float(delta.mean()),
        "p95_absolute_rgb": float(np.percentile(delta, 95)),
        "maximum_absolute_rgb": int(delta.max()),
    }


def build_preview(
    output_root: pathlib.Path,
    records: list[dict[str, Any]],
    output_path: pathlib.Path,
    cell_pixels: int,
) -> None:
    columns = max(int(record["column"]) for record in records) + 1
    rows = max(int(record["row"]) for record in records) + 1
    mosaic = Image.new("RGB", (columns * cell_pixels, rows * cell_pixels))
    for record in records:
        gutter = int(record["gutter_pixels"])
        with Image.open(page_path(output_root, record)).convert("RGB") as image:
            inner = image.crop((
                gutter,
                gutter,
                image.width - gutter,
                image.height - gutter,
            ))
            preview = inner.resize((cell_pixels, cell_pixels), Image.Resampling.LANCZOS)
        column = int(record["column"])
        row = int(record["row"])
        mosaic.paste(preview, (column * cell_pixels, (rows - row - 1) * cell_pixels))
    output_path.parent.mkdir(parents=True, exist_ok=True)
    mosaic.save(output_path, quality=92, subsampling=0, optimize=True)


def build_center_stitch(
    output_root: pathlib.Path,
    records: list[dict[str, Any]],
    output_path: pathlib.Path,
) -> None:
    columns = max(int(record["column"]) for record in records) + 1
    rows = max(int(record["row"]) for record in records) + 1
    center_column = columns // 2
    center_row = rows // 2
    selected = [
        record for record in records
        if abs(int(record["column"]) - center_column) <= 1
        and abs(int(record["row"]) - center_row) <= 1
    ]
    if len(selected) != 9:
        raise RuntimeError(f"Center stitch requires 9 pages, found {len(selected)}")
    first = selected[0]
    gutter = int(first["gutter_pixels"])
    inner_pixels = int(first["output_pixels"][0]) - 2 * gutter
    stitch = Image.new("RGB", (inner_pixels * 3, inner_pixels * 3))
    for record in selected:
        with Image.open(page_path(output_root, record)).convert("RGB") as image:
            inner = image.crop((gutter, gutter, image.width - gutter, image.height - gutter))
        x = int(record["column"]) - (center_column - 1)
        y = (center_row + 1) - int(record["row"])
        stitch.paste(inner, (x * inner_pixels, y * inner_pixels))
    output_path.parent.mkdir(parents=True, exist_ok=True)
    stitch.save(output_path, quality=95, subsampling=0, optimize=True)


def validate(
    manifest_path: pathlib.Path,
    output_root: pathlib.Path,
    artifact_root: pathlib.Path,
) -> dict[str, Any]:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    failures: list[str] = []
    records = list(manifest.get("pages", []))
    expected = expected_page_count(manifest)
    if manifest.get("mode") != "full":
        failures.append("manifest mode is not full")
    if len(records) != expected:
        failures.append(f"page count {len(records)} != expected {expected}")
    if manifest.get("source", {}).get("item", {}).get("id") != "22b7c93a74394696824258f807f345ba":
        failures.append("manifest does not identify the reviewed 2020 source")

    seen: set[str] = set()
    for record in records:
        asset_name = str(record.get("asset_name", ""))
        if asset_name in seen:
            failures.append(f"duplicate page {asset_name}")
            continue
        seen.add(asset_name)
        path = page_path(output_root, record)
        if not path.exists():
            failures.append(f"missing page {path}")
            continue
        expected_pixels = tuple(int(value) for value in record["output_pixels"])
        with Image.open(path) as image:
            if image.size != expected_pixels:
                failures.append(f"dimension mismatch {asset_name}: {image.size} != {expected_pixels}")
        if sha256_file(path) != record.get("sha256"):
            failures.append(f"hash mismatch {asset_name}")
        if float(record.get("valid_pixel_fraction", 0.0)) < 0.9999:
            failures.append(f"incomplete source coverage {asset_name}")

    seams_by_tier: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for tier in ("high", "medium"):
        tier_records = [record for record in records if record.get("tier") == tier]
        seams_by_tier[tier] = overlap_errors(output_root, tier_records)
        if not seams_by_tier[tier]:
            failures.append(f"no {tier} page overlaps were measured")

    seam_summary: dict[str, Any] = {}
    for tier, seams in seams_by_tier.items():
        means = np.asarray([record["mean_absolute_rgb"] for record in seams])
        p95s = np.asarray([record["p95_absolute_rgb"] for record in seams])
        seam_summary[tier] = {
            "comparison_count": len(seams),
            "mean_absolute_rgb": float(means.mean()),
            "p95_of_mean_absolute_rgb": float(np.percentile(means, 95)),
            "maximum_mean_absolute_rgb": float(means.max()),
            "p95_absolute_rgb_across_comparisons": float(np.percentile(p95s, 95)),
            "worst_comparisons": sorted(
                seams,
                key=lambda item: item["mean_absolute_rgb"],
                reverse=True,
            )[:20],
        }
        # This is an intentionally loose defect gate. Seamless independently
        # encoded JPEG exports normally remain in low single digits; the
        # rejected mixed-radiometry source produced conspicuous page steps.
        if seam_summary[tier]["p95_of_mean_absolute_rgb"] > 12.0:
            failures.append(f"{tier} overlap discontinuity exceeds 12 RGB levels")

    high_records = [record for record in records if record.get("tier") == "high"]
    preview_path = artifact_root / "full_map_2020_high_preview.jpg"
    center_path = artifact_root / "airport_center_2020_high_stitch.jpg"
    build_preview(output_root, high_records, preview_path, cell_pixels=128)
    build_center_stitch(output_root, high_records, center_path)
    report = {
        "passed": not failures,
        "failures": failures,
        "manifest": str(manifest_path),
        "source_item_id": manifest.get("source", {}).get("item", {}).get("id"),
        "page_count": len(records),
        "expected_page_count": expected,
        "page_bytes": sum(int(record.get("bytes", 0)) for record in records),
        "seams": seam_summary,
        "review_images": [str(preview_path), str(center_path)],
    }
    artifact_root.mkdir(parents=True, exist_ok=True)
    (artifact_root / "full_map_2020_page_audit.json").write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    if failures:
        raise RuntimeError("; ".join(failures[:20]))
    return report


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=pathlib.Path, required=True)
    parser.add_argument("--output-root", type=pathlib.Path, required=True)
    parser.add_argument("--artifact-root", type=pathlib.Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    report = validate(
        args.manifest.resolve(),
        args.output_root.resolve(),
        args.artifact_root.resolve(),
    )
    print(
        f"PASS full-map 2020 clipmap: {report['page_count']} pages, "
        f"{report['page_bytes']} bytes",
        flush=True,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
