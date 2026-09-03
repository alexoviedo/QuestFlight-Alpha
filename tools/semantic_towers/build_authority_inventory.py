#!/usr/bin/env python3
"""Build the external conservative FAA candidate inventory used by tower V1."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

from pyproj import Transformer


ORIGIN_LAT = 40.03936527
ORIGIN_LON = -105.22608958
HALF_EXTENT_METERS = 12_000.0
INVENTORY_MARGIN_METERS = 500.0
MINIMUM_AGL_METERS = 15.0
VERTICAL_TYPES = {
    "ANTENNA",
    "BLDG-TWR",
    "CONTROL TOWER",
    "MET",
    "POLE",
    "SPIRE",
    "STACK",
    "TANK",
    "TOWER",
    "T-L TOWER",
    "T-L TWR",  # Current DDOF field spelling.
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def disposition(obstacle_id: str, obstacle_type: str, study: str) -> tuple[str, str]:
    if obstacle_id == "08-000121":
        return (
            "ACCEPTED_EXACT_OWNERSHIP_TRANSFER",
            "FAA/OSM/2020 imagery place the stack inside exact building 52188.",
        )
    if obstacle_id == "08-006048":
        return (
            "SUPPLEMENTAL_ONLY_NOT_POSITION_AUTHORITY",
            "Nearby to accepted FCC ASR 1024061 but about 190 m from its registered position.",
        )
    if obstacle_type == "BLDG-TWR":
        return (
            "PRESERVED_UNDERLYING_BUILDING",
            "No exact imagery-supported rooftop ownership transfer was established.",
        )
    if obstacle_type == "STACK":
        return (
            "PRESERVED_SEMANTIC_INDUSTRIAL_COMPONENT",
            "Visible industrial component without a conflicting generic shell at its exact point.",
        )
    if study.startswith(("2022", "2023", "2024", "2025", "2026")):
        return (
            "REJECTED_CHRONOLOGY_OR_NO_2020_TRANSFER",
            "Post-2020 study or no independent 2020 evidence and no exact false-shell transfer.",
        )
    return (
        "REJECTED_NO_EXACT_FALSE_SHELL_TRANSFER",
        "Record did not coincide with an imagery-confirmed false product building shell.",
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--faa-csv", type=Path, required=True)
    parser.add_argument("--faa-archive", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    transformer = Transformer.from_crs(
        "EPSG:4326",
        f"+proj=aeqd +lat_0={ORIGIN_LAT:.12f} +lon_0={ORIGIN_LON:.12f} "
        "+datum=WGS84 +units=m +no_defs",
        always_xy=True,
    )
    all_in_product_bounds = 0
    all_in_inventory_extent = 0
    candidates: list[dict[str, object]] = []
    with args.faa_csv.open("r", encoding="cp1252", newline="") as stream:
        for row in csv.DictReader(stream):
            latitude = float(row["LATDEC"])
            longitude = float(row["LONDEC"])
            x, z = transformer.transform(longitude, latitude)
            if abs(x) > HALF_EXTENT_METERS + INVENTORY_MARGIN_METERS or abs(z) > HALF_EXTENT_METERS + INVENTORY_MARGIN_METERS:
                continue
            all_in_inventory_extent += 1
            inside_product_bounds = abs(x) <= HALF_EXTENT_METERS and abs(z) <= HALF_EXTENT_METERS
            if inside_product_bounds:
                all_in_product_bounds += 1
            obstacle_type = row["TYPE"].strip()
            agl_meters = float(row["AGL"]) * 0.3048
            if obstacle_type not in VERTICAL_TYPES or agl_meters < MINIMUM_AGL_METERS:
                continue
            study = row["FAA STUDY"].strip()
            status, reason = disposition(row["OAS"], obstacle_type, study)
            candidates.append(
                {
                    "faa_obstacle_id": row["OAS"],
                    "verified_status": row["VERIFIED STATUS"],
                    "city": row["CITY"].strip(),
                    "latitude_degrees": latitude,
                    "longitude_degrees": longitude,
                    "product_x_meters": round(x, 4),
                    "product_z_meters": round(z, 4),
                    "inside_product_bounds": inside_product_bounds,
                    "obstacle_type": obstacle_type,
                    "height_agl_meters": round(agl_meters, 4),
                    "top_elevation_amsl_meters": round(float(row["AMSL"]) * 0.3048, 4),
                    "lighting_code": row["LIGHTING"].strip(),
                    "marking_code": row["MARKING"].strip(),
                    "accuracy_code": row["ACCURACY"].strip(),
                    "faa_study": study,
                    "action": row["ACTION"].strip(),
                    "julian_date": row["JDATE"].strip(),
                    "disposition": status,
                    "reason": reason,
                }
            )

    candidates.sort(key=lambda item: (item["product_z_meters"], item["product_x_meters"]))
    authority = json.loads(args.manifest.read_text(encoding="utf-8"))
    summary = authority["inventory_summary"]
    product_candidates = sum(item["inside_product_bounds"] for item in candidates)
    if all_in_product_bounds != summary["faa_obstacles_in_product_bounds"]:
        raise RuntimeError(f"FAA product-bounds count changed: {all_in_product_bounds}")
    if all_in_inventory_extent != summary["faa_obstacles_in_inventory_extent"]:
        raise RuntimeError(f"FAA inventory-extent count changed: {all_in_inventory_extent}")
    if product_candidates != summary["faa_vertical_candidates_at_least_15m"]:
        raise RuntimeError(f"FAA product vertical candidate count changed: {product_candidates}")
    if len(candidates) != summary["faa_vertical_candidates_in_inventory_extent"]:
        raise RuntimeError(f"FAA inventory vertical candidate count changed: {len(candidates)}")
    if sum(item["disposition"].startswith("ACCEPTED") for item in candidates) != 1:
        raise RuntimeError("Expected exactly one FAA-owned shell transfer")

    report = {
        "schema": "questflight.semantic-tower-candidate-inventory-v1",
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "coordinate_frame": authority["coordinate_frame"],
        "product_half_extent_meters": HALF_EXTENT_METERS,
        "inventory_safety_margin_meters": INVENTORY_MARGIN_METERS,
        "minimum_agl_meters": MINIMUM_AGL_METERS,
        "source": {
            "path": str(args.faa_archive.resolve()),
            "sha256": sha256(args.faa_archive),
            "url": authority["sources"][0]["source_url"],
            "acquired_utc": authority["sources"][0]["acquired_utc"],
        },
        "faa_obstacles_in_product_bounds": all_in_product_bounds,
        "faa_obstacles_in_inventory_extent": all_in_inventory_extent,
        "faa_vertical_candidates_in_product_bounds": product_candidates,
        "faa_vertical_candidates_in_inventory_extent": len(candidates),
        "accepted_faa_shell_transfers": 1,
        "supplemental_fcc_structures": 1,
        "candidates": candidates,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {args.output} product_candidates={product_candidates} margin_candidates={len(candidates)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
