#!/usr/bin/env python3
"""Validate KBDU source provenance, geometry, coverage, encoding, and Quest budgets."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

from kbdu_common import (
    atomic_write_json,
    decode_int16_base64,
    expected_triangle_count,
    layer_dimensions,
    load_config,
    repo_root,
    sha256_file,
)


ASSET_RELATIVE_DIR = Path("QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU")
MANIFEST_RELATIVE_PATH = Path("tools/kbdu_environment/kbdu_source_manifest.json")
EXPECTED_FROZEN_MANIFEST_SHA256 = "22ef678c6071e527e551b99f8c35e6fb554ced7a6501091b9b7dff9b6963ff1f"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=repo_root())
    parser.add_argument("--config", type=Path, default=Path(__file__).with_name("config.json"))
    parser.add_argument("--raw-dir", type=Path, help="Optional external raw snapshot; enables raw SHA256 verification.")
    parser.add_argument("--report", type=Path, help="Optional JSON validation report path (keep evidence outside Git).")
    return parser.parse_args()


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


class Validation:
    def __init__(self) -> None:
        self.checks: list[dict[str, Any]] = []
        self.metrics: dict[str, Any] = {}

    def require(self, condition: bool, check: str, detail: str) -> None:
        self.checks.append({"check": check, "passed": bool(condition), "detail": detail})
        if not condition:
            raise RuntimeError(f"{check}: {detail}")


def validate_manifest(
    validation: Validation,
    repo: Path,
    manifest: dict[str, Any],
    raw_dir: Path | None,
) -> None:
    validation.require(manifest.get("schema_version") == 1, "manifest_schema", "schema_version must be 1")
    validation.require(
        "No Google Maps" in manifest.get("prohibited_sources_confirmation", ""),
        "prohibited_sources",
        "manifest must explicitly confirm that Google-derived sources are absent",
    )
    license_statuses = {item.get("status") for item in manifest.get("license_summary", [])}
    validation.require(
        "ODbL-1.0" in license_statuses,
        "osm_license",
        "OpenStreetMap derivative must retain ODbL-1.0 status",
    )
    validation.require(
        any("public domain" in str(status).lower() for status in license_statuses),
        "public_domain_sources",
        "FAA/USGS public-domain status must be recorded",
    )
    product_extraction = (repo / "QuestFlight").is_dir() and not (repo / "QuestFlightLab").is_dir()
    for record in manifest.get("derived_assets", []):
        recorded_path = Path(record["path"])
        if product_extraction and recorded_path.parts and recorded_path.parts[0] == "QuestFlightLab":
            recorded_path = Path("QuestFlight", *recorded_path.parts[1:])
        path = repo / recorded_path
        validation.require(path.is_file(), "derived_asset_exists", record["path"])
        actual = sha256_file(path)
        validation.require(actual == record["sha256"], "derived_asset_hash", f"{record['path']} SHA256={actual}")
        validation.require(path.stat().st_size == record["bytes"], "derived_asset_size", record["path"])
    if product_extraction:
        # The manifest is an immutable provenance record from the frozen R&D
        # commit. Legacy runtime builders named in its historical toolchain are
        # intentionally absent from the clean product dependency closure.
        validation.require(
            len(manifest.get("toolchain", [])) >= 8,
            "frozen_toolchain_provenance",
            "frozen source manifest retains its complete generator/toolchain record",
        )
    else:
        for record in manifest.get("toolchain", []):
            path = repo / record["path"]
            validation.require(path.is_file(), "toolchain_file_exists", record["path"])
            validation.require(sha256_file(path) == record["sha256"], "toolchain_hash", record["path"])
    if raw_dir is not None:
        for record in manifest.get("source_data", []):
            path = raw_dir / record["path"]
            validation.require(path.is_file(), "raw_source_exists", record["path"])
            validation.require(sha256_file(path) == record["sha256"], "raw_source_hash", record["path"])


def validate_terrain(
    validation: Validation,
    config: dict[str, Any],
    path: Path,
    terrain: dict[str, Any],
) -> None:
    validation.require(terrain.get("schema_version") == 1, "terrain_schema", "schema_version must be 1")
    validation.require(terrain.get("label") == config["airport"]["label"], "terrain_label", terrain.get("label", ""))
    validation.require(terrain.get("origin") == config["airport"]["origin"], "terrain_origin", "must match pinned FAA ARP")
    validation.require("+X=east" in terrain["coordinate_frame"]["unity_mapping"], "terrain_axis_mapping", terrain["coordinate_frame"]["unity_mapping"])
    configured = {layer["id"]: layer for layer in config["terrain"]["layers"]}
    actual = {layer["id"]: layer for layer in terrain.get("layers", [])}
    validation.require(set(actual) == set(configured), "terrain_layers", f"layers={sorted(actual)}")
    total_samples = 0
    total_triangles = 0
    global_min = math.inf
    global_max = -math.inf
    quantization = float(terrain["height_quantization_meters"])
    for layer_id, layer_config in configured.items():
        layer = actual[layer_id]
        width, height, min_x, max_x, min_z, max_z = layer_dimensions(layer_config)
        validation.require(layer["width"] == width and layer["height"] == height, "terrain_dimensions", layer_id)
        validation.require(
            layer["min_x_meters"] == min_x
            and layer["max_x_meters"] == max_x
            and layer["min_z_meters"] == min_z
            and layer["max_z_meters"] == max_z,
            "terrain_extents",
            layer_id,
        )
        expected_count = width * height
        values = decode_int16_base64(layer["height_dm_little_endian_base64"], expected_count)
        expected_triangles = expected_triangle_count(layer_config, configured)
        validation.require(layer["expected_triangle_count"] == expected_triangles, "terrain_triangle_count", layer_id)
        elevations = [config["airport"]["origin"]["elevation_msl_meters"] + value * quantization for value in values]
        validation.require(min(elevations) > 1300.0 and max(elevations) < 4000.0, "terrain_elevation_plausibility", f"{layer_id}: {min(elevations):.1f}..{max(elevations):.1f} m MSL")
        validation.require(
            abs(min(elevations) - float(layer["minimum_render_elevation_msl_meters"])) <= quantization + 0.01,
            "terrain_min_metadata",
            layer_id,
        )
        validation.require(
            abs(max(elevations) - float(layer["maximum_render_elevation_msl_meters"])) <= quantization + 0.01,
            "terrain_max_metadata",
            layer_id,
        )
        total_samples += expected_count
        total_triangles += expected_triangles
        global_min = min(global_min, min(elevations))
        global_max = max(global_max, max(elevations))
    budgets = terrain["budgets"]
    validation.require(total_samples == budgets["height_samples"], "terrain_total_samples", str(total_samples))
    validation.require(total_triangles == budgets["expected_terrain_triangles"], "terrain_total_triangles", str(total_triangles))
    validation.require(total_samples <= config["terrain"]["budgets"]["maximum_height_samples"], "terrain_sample_budget", str(total_samples))
    validation.require(total_triangles <= config["terrain"]["budgets"]["maximum_terrain_triangles"], "terrain_triangle_budget", str(total_triangles))
    validation.require(path.stat().st_size <= config["terrain"]["budgets"]["maximum_asset_bytes"], "terrain_byte_budget", str(path.stat().st_size))
    validation.require(
        terrain["runway_terrain_blend"]["modified_sample_count"] > 0,
        "runway_terrain_blend",
        f"modified={terrain['runway_terrain_blend']['modified_sample_count']}",
    )
    airport_patch = actual["airport_patch"]
    validation.require(
        airport_patch["maximum_raw_elevation_msl_meters"] - airport_patch["minimum_raw_elevation_msl_meters"] < 100.0,
        "airport_patch_relief",
        f"raw relief={airport_patch['maximum_raw_elevation_msl_meters'] - airport_patch['minimum_raw_elevation_msl_meters']:.1f} m",
    )
    control_elevations = terrain["source_snapshot"]["usgs_evidence"]["control_elevations_msl_meters"]
    validation.require(
        abs(control_elevations["origin"] - config["airport"]["origin"]["elevation_msl_meters"]) < 25.0,
        "usgs_origin_elevation",
        f"USGS={control_elevations['origin']:.3f} m, FAA datum={config['airport']['origin']['elevation_msl_meters']:.3f} m",
    )
    validation.require(actual["inner_4km"]["max_x_meters"] - actual["inner_4km"]["min_x_meters"] == 4000.0, "inner_coverage", "4 km x 4 km")
    validation.require(actual["mid_12km"]["max_x_meters"] - actual["mid_12km"]["min_x_meters"] == 12000.0, "mid_coverage", "12 km x 12 km")
    validation.require(actual["far_24km"]["max_x_meters"] - actual["far_24km"]["min_x_meters"] == 24000.0, "far_coverage", "24 km x 24 km")
    validation.metrics.update(
        {
            "terrain_asset_bytes": path.stat().st_size,
            "terrain_height_samples": total_samples,
            "terrain_expected_triangles": total_triangles,
            "terrain_minimum_msl_meters": round(global_min, 1),
            "terrain_maximum_msl_meters": round(global_max, 1),
            "terrain_relief_meters": round(global_max - global_min, 1),
            "runway_blended_samples": terrain["runway_terrain_blend"]["modified_sample_count"],
        }
    )


def validate_context(
    validation: Validation,
    config: dict[str, Any],
    path: Path,
    context: dict[str, Any],
) -> None:
    validation.require(context.get("schema_version") == 1, "context_schema", "schema_version must be 1")
    validation.require(context.get("origin") == config["airport"]["origin"], "context_origin", "must match pinned FAA ARP")
    facility = context["faa"]["facility"]
    validation.require(facility["ARPT_ID"] == "BDU" and facility["ICAO_ID"] == "KBDU", "faa_airport_identity", f"{facility['ARPT_ID']}/{facility['ICAO_ID']}")
    paved = next((runway for runway in context["faa"]["runways"] if runway["runway_id"] == "08/26"), None)
    validation.require(paved is not None, "faa_paved_runway", "08/26 must be present")
    assert paved is not None
    validation.require(paved["length_feet"] == 4100 and paved["width_feet"] == 75, "faa_runway_dimensions", f"{paved['length_feet']} x {paved['width_feet']} ft")
    first, second = paved["endpoints"]
    dx = second["x_east_meters"] - first["x_east_meters"]
    dz = second["z_north_meters"] - first["z_north_meters"]
    length = math.hypot(dx, dz)
    heading = math.degrees(math.atan2(dx, dz)) % 360.0
    validation.require(abs(length - 4100.0 * 0.3048) < 3.0, "faa_runway_local_length", f"{length:.2f} m")
    validation.require(85.0 < heading < 95.0, "faa_runway_true_heading", f"{heading:.3f}°")
    endpoint_elevations = [
        float(first["usgs_elevation_msl_meters"]), float(second["usgs_elevation_msl_meters"])
    ]
    validation.require(
        all(abs(value - config["airport"]["origin"]["elevation_msl_meters"]) < 25.0 for value in endpoint_elevations),
        "runway_endpoint_dem_plausibility",
        f"endpoints={endpoint_elevations}",
    )
    validation.require(
        abs(endpoint_elevations[1] - endpoint_elevations[0]) < 15.0,
        "runway_endpoint_dem_grade",
        f"delta={endpoint_elevations[1] - endpoint_elevations[0]:.3f} m",
    )
    osm = context["openstreetmap"]
    validation.require(osm["license"] == "ODbL-1.0", "context_osm_license", osm["license"])
    validation.require(osm["attribution"] == "© OpenStreetMap contributors", "context_osm_attribution", osm["attribution"])
    features = osm["features"]
    evidence = osm["evidence"]
    categories = {feature["category"] for feature in features}
    required_categories = {"aeroway", "building", "road", "water", "landcover"}
    validation.require(required_categories.issubset(categories), "osm_feature_categories", f"categories={sorted(categories)}")
    validation.require(len(features) == evidence["output_feature_count"], "osm_feature_count", str(len(features)))
    validation.require(
        all(len(feature["points_q"]) % 2 == 0 for feature in features),
        "osm_flat_point_encoding",
        "every points_q array contains x/z pairs",
    )
    points = sum(len(feature["points_q"]) // 2 for feature in features)
    validation.require(points == evidence["output_point_count"], "osm_point_count", str(points))
    validation.require(len(features) <= config["vector_context"]["budgets"]["maximum_features"], "osm_feature_budget", str(len(features)))
    validation.require(points <= config["vector_context"]["budgets"]["maximum_points"], "osm_point_budget", str(points))
    for category, maximum in config["vector_context"]["budgets"]["maximum_features_by_category"].items():
        count = int(evidence["category_counts"].get(category, 0))
        validation.require(count <= int(maximum), "osm_category_budget", f"{category}={count}/{maximum}")
    validation.require(path.stat().st_size <= config["vector_context"]["budgets"]["maximum_asset_bytes"], "context_byte_budget", str(path.stat().st_size))
    fallback = context["macro_material_fallback"]
    palette = fallback["palette"]
    validation.require(
        fallback["default_material_id"] in palette,
        "macro_fallback_default",
        fallback["default_material_id"],
    )
    assigned_materials = {
        feature["macro_material_id"] for feature in features if feature.get("macro_material_id") is not None
    }
    validation.require(
        assigned_materials.issubset(set(palette)),
        "macro_material_references",
        f"assigned={sorted(assigned_materials)}",
    )
    landcover_features = [feature for feature in features if feature["category"] == "landcover"]
    validation.require(
        all(feature.get("macro_material_id") in palette for feature in landcover_features),
        "osm_landcover_material_assignment",
        f"assigned={len(landcover_features)}",
    )
    imagery_gate = fallback["imagery_gate"]
    validation.require(
        imagery_gate["availability_feature_count"] > 0,
        "naip_availability_attempt",
        f"features={imagery_gate['availability_feature_count']}",
    )
    validation.require(
        imagery_gate["raw_imagery_downloaded"] is False,
        "naip_raw_imagery_policy",
        "no raw or oversized imagery committed",
    )
    extent_q = int(round(config["vector_context"]["far_half_extent_meters"] / context["coordinate_quantization_meters"]))
    max_q = max(abs(value) for feature in features for value in feature["points_q"])
    validation.require(max_q <= extent_q + 1, "context_clipped_extent", f"max={max_q}, allowed={extent_q}")
    validation.metrics.update(
        {
            "context_asset_bytes": path.stat().st_size,
            "context_features": len(features),
            "context_points": points,
            "context_category_counts": evidence["category_counts"],
            "macro_material_ids": sorted(assigned_materials),
            "naip_availability_features": imagery_gate["availability_feature_count"],
            "naip_imagery_status": imagery_gate["status"],
            "faa_paved_runway_length_meters": round(length, 2),
            "faa_paved_runway_true_heading_degrees": round(heading, 3),
            "faa_paved_runway_endpoint_dem_meters": [
                first["usgs_elevation_msl_meters"],
                second["usgs_elevation_msl_meters"],
            ],
        }
    )


def main() -> int:
    args = parse_args()
    repo = args.repo_root.resolve()
    config = load_config(args.config)
    asset_dir = repo / ASSET_RELATIVE_DIR
    terrain_path = asset_dir / "kbdu_terrain_rings.json"
    context_path = asset_dir / "kbdu_reference_context.json"
    manifest_path = repo / MANIFEST_RELATIVE_PATH
    for path in (terrain_path, context_path, manifest_path):
        if not path.is_file():
            raise RuntimeError(f"Required KBDU artifact is missing: {path}")

    terrain = load_json(terrain_path)
    context = load_json(context_path)
    manifest = load_json(manifest_path)
    validation = Validation()
    validation.require(
        sha256_file(manifest_path) == EXPECTED_FROZEN_MANIFEST_SHA256,
        "frozen_source_manifest_hash",
        EXPECTED_FROZEN_MANIFEST_SHA256,
    )
    validate_manifest(validation, repo, manifest, args.raw_dir.resolve() if args.raw_dir else None)
    validate_terrain(validation, config, terrain_path, terrain)
    validate_context(validation, config, context_path, context)
    report = {
        "schema_version": 1,
        "passed": all(check["passed"] for check in validation.checks),
        "checks": validation.checks,
        "metrics": validation.metrics,
        "limitations": [
            "This validates data provenance, geometry, encoding, and static budgets, not Unity rendering or Quest frame timing.",
            "KBDU assets remain not for navigation and require integrated before/after Visual QA when enabled in the scene.",
        ],
    }
    if args.report:
        atomic_write_json(args.report.resolve(), report)
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
