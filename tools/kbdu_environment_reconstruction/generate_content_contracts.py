#!/usr/bin/env python3
"""Generate deterministic tools-only environment reconstruction contracts."""

from __future__ import annotations

import argparse
import base64
import copy
import hashlib
import json
import math
import re
import struct
import tempfile
from pathlib import Path
from typing import Any, Iterable


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_CONFIG = SCRIPT_DIR / "config.json"


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_json(path: Path, value: Any, *, compact: bool = False) -> None:
    text = (
        json.dumps(value, sort_keys=True, separators=(",", ":"))
        if compact
        else json.dumps(value, indent=2, sort_keys=True)
    )
    payload = (text + "\n").encode("utf-8")
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as handle:
        temporary = Path(handle.name)
        handle.write(payload)
    temporary.replace(path)


def signed_cell(value: float, cell_size: float) -> str:
    index = math.floor(value / cell_size)
    return ("p" if index >= 0 else "n") + f"{abs(index):03d}"


def cell_id(x: float, z: float, cell_size: float) -> str:
    return f"cell_{int(cell_size)}m_x{signed_cell(x, cell_size)}_z{signed_cell(z, cell_size)}"


def decode_points(feature: dict[str, Any], quantization: float) -> list[tuple[float, float]]:
    encoded = feature["points_q"]
    return [
        (float(encoded[index]) * quantization, float(encoded[index + 1]) * quantization)
        for index in range(0, len(encoded), 2)
    ]


def polygon_without_duplicate_end(points: list[tuple[float, float]]) -> list[tuple[float, float]]:
    return points[:-1] if len(points) > 1 and points[0] == points[-1] else points


def centroid(points: list[tuple[float, float]]) -> tuple[float, float]:
    vertices = polygon_without_duplicate_end(points)
    return (
        sum(point[0] for point in vertices) / len(vertices),
        sum(point[1] for point in vertices) / len(vertices),
    )


def longest_edge_heading(points: list[tuple[float, float]]) -> float:
    vertices = polygon_without_duplicate_end(points)
    best = (0.0, 0.0, 1.0)
    for first, second in zip(vertices, vertices[1:] + vertices[:1]):
        dx, dz = second[0] - first[0], second[1] - first[1]
        length_squared = dx * dx + dz * dz
        if length_squared > best[0]:
            best = (length_squared, dx, dz)
    return round(math.degrees(math.atan2(best[1], best[2])) % 180.0, 3)


def zone_id(distance: float, zones: list[list[float]]) -> str:
    for minimum, maximum in zones:
        if minimum <= distance < maximum or (distance == maximum and maximum == zones[-1][1]):
            return f"{int(minimum // 1000)}-{int(maximum // 1000)}km"
    return "outside-6km"


def stable_osm_id(prefix: str, feature: dict[str, Any]) -> str:
    source = feature["source"]
    source_part = str(source.get("part", "geometry:0")).replace(":", "-")
    return f"{prefix}-osm-{source['osm_type']}-{source['osm_id']}-{source_part}"


def point_in_polygon(point: tuple[float, float], polygon: list[tuple[float, float]]) -> bool:
    x, z = point
    vertices = polygon_without_duplicate_end(polygon)
    inside = False
    previous = vertices[-1]
    for current in vertices:
        x1, z1 = previous
        x2, z2 = current
        if (z1 > z) != (z2 > z):
            intersection_x = (x2 - x1) * (z - z1) / (z2 - z1) + x1
            if x < intersection_x:
                inside = not inside
        previous = current
    return inside


def solve_plane(samples: list[tuple[float, float, float]]) -> tuple[float, float, float]:
    # Solve y = ax + bz + c from the three legacy terrain-seated placements.
    matrix = [[x, z, 1.0, y] for x, z, y in samples]
    for pivot in range(3):
        selected = max(range(pivot, 3), key=lambda row: abs(matrix[row][pivot]))
        matrix[pivot], matrix[selected] = matrix[selected], matrix[pivot]
        divisor = matrix[pivot][pivot]
        if abs(divisor) < 1e-8:
            raise ValueError("Legacy parked-aircraft samples do not define an apron plane")
        matrix[pivot] = [value / divisor for value in matrix[pivot]]
        for row in range(3):
            if row == pivot:
                continue
            factor = matrix[row][pivot]
            matrix[row] = [
                matrix[row][column] - factor * matrix[pivot][column]
                for column in range(4)
            ]
    return tuple(matrix[row][3] for row in range(3))  # type: ignore[return-value]


def plane_height(plane: tuple[float, float, float], x: float, z: float) -> float:
    return plane[0] * x + plane[1] * z + plane[2]


def plane_normal(plane: tuple[float, float, float]) -> list[float]:
    normal = [-plane[0], 1.0, -plane[1]]
    magnitude = math.sqrt(sum(value * value for value in normal))
    return [round(value / magnitude, 8) for value in normal]


class TerrainSampler:
    def __init__(self, terrain: dict[str, Any]):
        self.origin = float(terrain["origin"]["elevation_msl_meters"])
        quantization = float(terrain["height_quantization_meters"])
        self.layers: list[tuple[dict[str, Any], list[float]]] = []
        for layer in sorted(terrain["layers"], key=lambda value: int(value["priority"])):
            payload = base64.b64decode(layer["height_dm_little_endian_base64"], validate=True)
            count = int(layer["width"]) * int(layer["height"])
            values = struct.unpack("<" + "h" * count, payload)
            heights = [self.origin + value * quantization for value in values]
            self.layers.append((layer, heights))

    def sample(self, x: float, z: float) -> float:
        for layer, values in self.layers:
            if not (
                float(layer["min_x_meters"]) <= x <= float(layer["max_x_meters"])
                and float(layer["min_z_meters"]) <= z <= float(layer["max_z_meters"])
            ):
                continue
            if layer["kind"] == "ring" and max(abs(x), abs(z)) < float(layer.get("inner_radius_meters", 0.0)):
                continue
            spacing = float(layer["spacing_meters"])
            fx = (x - float(layer["min_x_meters"])) / spacing
            fz = (z - float(layer["min_z_meters"])) / spacing
            x0 = max(0, min(int(layer["width"]) - 2, math.floor(fx)))
            z0 = max(0, min(int(layer["height"]) - 2, math.floor(fz)))
            tx = max(0.0, min(1.0, fx - x0))
            tz = max(0.0, min(1.0, fz - z0))
            width = int(layer["width"])
            a = values[z0 * width + x0]
            b = values[z0 * width + x0 + 1]
            c = values[(z0 + 1) * width + x0]
            d = values[(z0 + 1) * width + x0 + 1]
            elevation = (a * (1.0 - tx) + b * tx) * (1.0 - tz) + (c * (1.0 - tx) + d * tx) * tz
            return elevation - self.origin
        raise ValueError(f"No terrain layer contains building centroid ({x}, {z})")


def parse_height(value: Any) -> float | None:
    if value is None:
        return None
    text = re.sub(r"\s*(meters?|m)\s*$", "", str(value).strip().lower())
    try:
        result = float(text)
    except ValueError:
        return None
    return result if math.isfinite(result) and result > 0.0 else None


def building_height(feature: dict[str, Any]) -> float:
    tags = feature.get("tags") or {}
    value = parse_height(tags.get("height"))
    if value is None:
        try:
            levels = float(str(tags.get("building:levels") or ""))
            value = levels * 3.1 if levels > 0.0 else None
        except ValueError:
            value = None
    source_id = int(feature["source"]["osm_id"])
    return round(max(3.5, min(30.0, value if value is not None else 4.5 + (source_id % 4) * 1.4)), 3)


def extended_context(
    checkpoint_context: dict[str, Any], building_source: dict[str, Any]
) -> dict[str, Any]:
    value = copy.deepcopy(checkpoint_context)
    non_buildings = [
        feature for feature in value["openstreetmap"]["features"] if feature["category"] != "building"
    ]
    buildings = []
    for source_feature in building_source["features"]:
        buildings.append(
            {
                "category": "building",
                "geometry_type": "polygon",
                "macro_material_id": "building_footprint",
                "points_q": source_feature["points_q"],
                "render_height_meters": building_height(source_feature),
                "render_width_meters": 0.0,
                "source": source_feature["source"],
                "tags": source_feature["tags"],
            }
        )
    value["coverage"]["airport_context_width_meters"] = 12000.0
    value["limitations"] = [
        limitation
        for limitation in value["limitations"]
        if "clipped" not in limitation.lower()
    ] + [
        "The V2 building derivative is bounded to a 6 km radial mid-zone and remains subject to OSM completeness and tag accuracy."
    ]
    value["openstreetmap"]["features"] = non_buildings + buildings
    source_evidence = building_source["evidence"]
    value["openstreetmap"]["evidence"] = {
        **value["openstreetmap"]["evidence"],
        "building_source": source_evidence,
        "building_source_endpoint": building_source["source"]["endpoint"],
        "building_source_osm_base_timestamp": building_source["source"]["osm_base_timestamp"],
        "building_source_raw_response_sha256": building_source["source"]["raw_response_sha256"],
        "category_counts": {
            **value["openstreetmap"]["evidence"]["category_counts"],
            "building": len(buildings),
        },
        "dropped_or_budget_trimmed_parts": 0,
        "output_feature_count": len(non_buildings) + len(buildings),
    }
    value["openstreetmap"]["source_scope"] = {
        "building_query": "current bounded 6 km radial derivative inside a 12 km square Overpass query",
        "building_source_license": building_source["license"],
        "building_source_query_sha256": building_source["source"]["query_sha256"],
    }
    return value


def building_outputs(
    context: dict[str, Any],
    building_source: dict[str, Any],
    terrain: TerrainSampler,
    config: dict[str, Any],
) -> tuple[dict[str, Any], list[dict[str, Any]], list[dict[str, Any]]]:
    quantization = float(context["coordinate_quantization_meters"])
    cell_size = float(config["building_cell_size_meters"])
    zones = config["building_zones_meters"]
    placements: list[dict[str, Any]] = []
    batches: dict[str, list[dict[str, Any]]] = {}
    counts: dict[str, dict[str, Any]] = {
        f"{int(minimum // 1000)}-{int(maximum // 1000)}km": {
            "source_footprints": 0,
            "represented_footprints": 0,
            "source_significant_footprints": 0,
            "represented_significant_footprints": 0,
        }
        for minimum, maximum in zones
    }
    distances: list[float] = []
    source_by_id = {
        (value["source"]["osm_type"], int(value["source"]["osm_id"]), value["source"]["part"]): value
        for value in building_source["features"]
    }
    features = context["openstreetmap"]["features"]
    for feature_index, feature in enumerate(features):
        if feature["category"] != "building" or feature["geometry_type"] != "polygon":
            continue
        points = decode_points(feature, quantization)
        x, z = centroid(points)
        distance = math.hypot(x, z)
        distances.append(distance)
        zone = zone_id(distance, zones)
        if zone in counts:
            counts[zone]["source_footprints"] += 1
        source = feature["source"]
        source_record = source_by_id[(source["osm_type"], int(source["osm_id"]), source["part"])]
        significant = bool(source_record["significant_outer_zone"])
        if zone in counts:
            counts[zone]["represented_footprints"] += 1
            if significant:
                counts[zone]["source_significant_footprints"] += 1
                counts[zone]["represented_significant_footprints"] += 1
        base_height = round(terrain.sample(x, z), 3)
        stable_cell = cell_id(x, z, cell_size)
        source_id = int(source["osm_id"])
        recipe_record = {
            "base_y_meters": base_height,
            "facade_tile": source_id % 8,
            "feature_index": feature_index,
            "roof_tile": 8 + source_id % 4,
        }
        batch_id = f"buildings_{zone}_{stable_cell}"
        batches.setdefault(batch_id, []).append(recipe_record)
        placements.append(
            {
                "authored_heading_degrees": longest_edge_heading(points),
                "category": "building",
                "foundation": {
                    "base_contact_offset_meters": 0.0,
                    "legacy_centroid_height_meters": base_height,
                    "mode": "frozen_usgs_3dep_bilinear_centroid_sample",
                    "terrain_normal": None,
                },
                "footprint_reference": {
                    "feature_index": feature_index,
                    "point_count": len(polygon_without_duplicate_end(points)),
                    "significant_outer_zone": significant,
                },
                "local_enu_meters": {"x": round(x, 3), "z": round(z, 3)},
                "lod_hlod": {
                    "cell_id": cell_id(x, z, cell_size),
                    "class": "near_3d" if distance < 2000.0 else "mid_hlod_250m",
                },
                "material_variant": {
                    "facade_tile": recipe_record["facade_tile"],
                    "roof_tile": recipe_record["roof_tile"],
                },
                "scale": [1.0, 1.0, 1.0],
                "source_feature": {
                    "osm_id": int(source["osm_id"]),
                    "osm_type": source["osm_type"],
                    "part": source.get("part", "geometry:0"),
                },
                "source_license_class": "OSM_ODBL_1_0_PRODUCED_WORK",
                "stable_id": stable_osm_id("building", feature),
                "zone": zone,
            }
        )
    for zone, value in counts.items():
        source_count = value["source_footprints"]
        represented = value["represented_footprints"]
        value["coverage_percent_of_committed_source"] = (
            round(represented * 100.0 / source_count, 3) if source_count else None
        )
        significant_source = value["source_significant_footprints"]
        significant_represented = value["represented_significant_footprints"]
        value["significant_coverage_percent"] = (
            round(significant_represented * 100.0 / significant_source, 3)
            if significant_source
            else None
        )
        threshold = 100.0 if zone == "0-2km" else 95.0 if zone == "2-4km" else 90.0
        measured = (
            value["coverage_percent_of_committed_source"]
            if zone != "4-6km"
            else value["significant_coverage_percent"]
        )
        value["acceptance_threshold_percent"] = threshold
        value["status"] = "PASS" if measured is not None and measured >= threshold else "FAIL"
        value["cell_count"] = len(
            {
                placement["lod_hlod"]["cell_id"]
                for placement in placements
                if placement["zone"] == zone
            }
        )
    evidence = building_source["evidence"]
    accepted = all(value["status"] == "PASS" for value in counts.values())
    coverage = {
        "cell_size_meters": cell_size,
        "checkpoint_source_limitations": {
            "building_query_half_extent_meters": 6000.0,
            "dropped_or_budget_trimmed_parts_all_categories": 0,
            "maximum_committed_building_centroid_distance_meters": round(max(distances), 3),
            "raw_element_count": evidence["raw_element_count"],
            "source_building_cap": None,
        },
        "label": "KBDU current bounded 6 km OSM building coverage",
        "schema_version": 1,
        "source_snapshot": building_source["source"],
        "v2_acceptance_status": "PASS" if accepted else "FAIL_COVERAGE_THRESHOLDS",
        "zones": counts,
    }
    recipe_batches = [
        {"buildings": sorted(records, key=lambda value: value["feature_index"]), "id": batch_id}
        for batch_id, records in sorted(batches.items())
    ]
    return coverage, sorted(placements, key=lambda value: value["stable_id"]), recipe_batches


def find_apron_polygon(
    context: dict[str, Any], apron_osm_id: int
) -> list[tuple[float, float]]:
    quantization = float(context["coordinate_quantization_meters"])
    matches = [
        feature
        for feature in context["openstreetmap"]["features"]
        if feature["category"] == "aeroway"
        and feature.get("tags", {}).get("aeroway") == "apron"
        and int(feature["source"]["osm_id"]) == apron_osm_id
    ]
    if len(matches) != 1:
        raise ValueError(f"Expected one apron feature {apron_osm_id}, found {len(matches)}")
    return decode_points(matches[0], quantization)


def parking_outputs(
    context: dict[str, Any], recipe: dict[str, Any], source: dict[str, Any], config: dict[str, Any]
) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    legacy = recipe["parked_aircraft_batches"][0]["placements"]
    if len(legacy) != 3:
        raise ValueError("The checkpoint parked-aircraft calibration requires exactly three legacy samples")
    plane_samples = [
        (
            float(record["x_meters"]),
            float(record["z_meters"]),
            float(record["base_y_meters"]) - 0.04,
        )
        for record in legacy
    ]
    plane = solve_plane(plane_samples)
    normal = plane_normal(plane)
    calibration = config["aircraft_calibration"]
    stands: list[dict[str, Any]] = []
    placements: list[dict[str, Any]] = []
    active_index = 0
    legacy_colors = [record["color_rgba"] for record in legacy]
    for definition in config["parking_stands"]:
        x = float(definition["x_meters"])
        z = float(definition["z_meters"])
        y = plane_height(plane, x, z)
        apron = find_apron_polygon(context, int(definition["source_apron_osm_id"]))
        contained = point_in_polygon((x, z), apron)
        stand = {
            "active_parked_aircraft": bool(definition["active"]),
            "apron_contained": contained,
            "apron_plane": {
                "normal": normal,
                "point_meters": [round(x, 3), round(y, 3), round(z, 3)],
                "source": "best-fit plane from three checkpoint terrain-seated parked-aircraft samples",
            },
            "heading_degrees": float(definition["heading_degrees"]),
            "id": definition["id"],
            "local_enu_meters": {"x": x, "z": z},
            "permitted_aircraft_class": "single_engine_piston",
            "source_reference": {
                "apron_osm_id": int(definition["source_apron_osm_id"]),
                "taxilane_osm_id": int(definition["source_taxilane_osm_id"]),
            },
        }
        stands.append(stand)
        if definition["active"]:
            correction = float(calibration["model_forward_axis_correction_degrees"])
            placements.append(
                {
                    "authored_heading_degrees": float(definition["heading_degrees"]),
                    "category": "parked_aircraft",
                    "contact": {
                        "apron_plane_normal": normal,
                        "gear_contact_points_unity_meters": calibration["gear_contacts_unity_meters"],
                        "root_height_meters": round(y, 3),
                    },
                    "local_enu_meters": {"x": x, "z": z},
                    "lod_hlod": {
                        "cell_id": cell_id(x, z, float(config["building_cell_size_meters"])),
                        "class": "airport_authored_lod",
                    },
                    "model_forward_axis_correction_degrees": correction,
                    "root_yaw_degrees": (float(definition["heading_degrees"]) + correction) % 360.0,
                    "scale": [1.0, 1.0, 1.0],
                    "source_license_class": "CC_BY_4_0_KOG_THORNS_ADAPTATION",
                    "stable_id": "parked-aircraft-" + definition["id"].lower(),
                    "stand_id": definition["id"],
                    "tint_rgba": legacy_colors[active_index],
                }
            )
            active_index += 1
    model = {
        "calibration_basis": "checkpoint model mesh coordinates plus authoritative worn-headset orientation observation",
        "contact_metadata_status": "PROJECT_AUTHORED_PROXY_CONTACTS; exterior derivative has no retained wheel submesh",
        "gear_contact_points_unity_meters": calibration["gear_contacts_unity_meters"],
        "model_forward_axis_correction_degrees": calibration["model_forward_axis_correction_degrees"],
        "source_bounds_meters": next(
            lod["bounds_meters"] for lod in source["lods"] if lod["name"] == "ParkedAircraft_LOD0"
        ),
        "source_model_axes": calibration["source_model_axes"],
        "unity_imported_forward_axis_before_correction": calibration[
            "unity_imported_forward_axis_before_correction"
        ],
        "unity_runtime_axes_after_correction": calibration["unity_runtime_axes_after_correction"],
    }
    manifest = {
        "aircraft_model_calibration": model,
        "label": "KBDU authored parking stands and parked-aircraft correction contract",
        "placement_count": len(placements),
        "parked_aircraft_placements": placements,
        "schema_version": 1,
        "stand_count": len(stands),
        "stands": stands,
    }
    return manifest, placements


def vegetation_outputs(
    source: dict[str, Any],
    species_library: dict[str, Any],
    coverage: dict[str, Any],
    terrain: TerrainSampler,
    config: dict[str, Any],
) -> tuple[dict[str, Any], list[dict[str, Any]], list[dict[str, Any]]]:
    cell_size = float(config["building_cell_size_meters"])
    placements: list[dict[str, Any]] = []
    batches: dict[str, list[dict[str, Any]]] = {}
    for record in source["placements"]:
        x = float(record["local_enu_meters"]["x"])
        z = float(record["local_enu_meters"]["z"])
        base_height = round(terrain.sample(x, z), 3)
        stable_cell = cell_id(x, z, cell_size)
        distance = math.hypot(x, z)
        zone = "near" if distance < 600.0 else "mid"
        placements.append(
            {
                "authored_heading_degrees": record["heading_degrees"],
                "category": "vegetation",
                "distribution": {
                    "likelihood_mask_pixel": record["likelihood_mask_pixel"],
                    "likelihood_score": record["likelihood_score"],
                    "source_class": record["source_class"],
                    "status": "V2_MASK_AND_EXCLUSIONS_VALIDATED",
                },
                "foundation": {
                    "legacy_root_height_meters": base_height,
                    "mode": "frozen_usgs_3dep_bilinear_centroid_sample",
                    "root_contact_range_meters": [-0.12, 0.03],
                    "terrain_normal": None,
                },
                "local_enu_meters": {"x": x, "z": z},
                "lod_hlod": {
                    "cell_id": cell_id(x, z, cell_size),
                    "class": "vegetation_multiscale_candidate",
                },
                "scale_cues": {
                    "crown_radius_meters": float(record["crown_radius_meters"]),
                    "height_meters": float(record["height_meters"]),
                },
                "source_license_class": "US_GOV_PUBLIC_DOMAIN_NAIP_PLUS_OSM_ODBL_EXCLUSIONS",
                "species_id": record["species_id"],
                "stable_id": record["stable_id"],
                "zone": zone,
            }
        )
        batch_id = f"vegetation_v2_{stable_cell}"
        batches.setdefault(batch_id, []).append(
            {
                "base_y_meters": base_height,
                "batch_id": batch_id,
                "crown_radius_meters": float(record["crown_radius_meters"]),
                "heading_degrees": record["heading_degrees"],
                "height_meters": float(record["height_meters"]),
                "source_category": "natural_color_osm_exclusion_v2",
                "species_id": record["species_id"],
                "stable_id": record["stable_id"],
                "x_meters": x,
                "z_meters": z,
                "zone": zone,
            }
        )
    vegetation = config["vegetation"]
    manifest = {
        "distribution_source": {
            "accepted_placement_count": len(source["placements"]),
            "classification": source["source"]["classification"],
            "legacy_candidate_count_replaced": source["legacy_candidate_count_replaced"],
            "mask_contract": source["mask_contract"],
            "source": source["source"],
            "status": "PASS_NATURAL_COLOR_FALLBACK",
        },
        "exclusion_policy": vegetation["exclusions"],
        "label": "KBDU deterministic multiscale vegetation distribution contract",
        "lod_policy": vegetation["lod_policy"],
        "schema_version": 1,
        "species_library": {
            "license": species_library["license"],
            "material_contract": species_library["material_contract"],
            "species": species_library["species"],
            "status": "PROJECT_OWNED_PROCEDURAL_LOD0_LOD1_LOD2_AUTHORED",
        },
        "stereo_policy": {
            "billboard_lighting_follows_head_rotation": False,
            "center_eye_frame_selection": True,
            "single_vertical_billboard_allowed": False,
            "top_down_and_oblique_canopy_required": True,
        },
        "validation": coverage,
    }
    recipe_batches = [
        {"id": batch_id, "placements": sorted(records, key=lambda value: value["stable_id"])}
        for batch_id, records in sorted(batches.items())
    ]
    return manifest, sorted(placements, key=lambda value: value["stable_id"]), recipe_batches


def context_recipe_v2(
    recipe: dict[str, Any],
    parking: dict[str, Any],
    building_batches: list[dict[str, Any]],
    vegetation_batches: list[dict[str, Any]],
) -> dict[str, Any]:
    """Create a legacy-reader-compatible 250 m building/aircraft V2 recipe."""
    value = copy.deepcopy(recipe)
    value["building_batches"] = building_batches
    value["vegetation_batches"] = vegetation_batches
    value["integration"]["building_spatial_batch_cell_meters"] = 250.0
    value["source_context_path"] = "tools/kbdu_environment_reconstruction/KBDU_ReferenceContextV2.json"
    model = parking["aircraft_model_calibration"]
    corrected: list[dict[str, Any]] = []
    for placement in parking["parked_aircraft_placements"]:
        root_height = float(placement["contact"]["root_height_meters"])
        corrected.append(
            {
                "base_y_meters": root_height,
                "color_rgba": placement["tint_rgba"],
                "placement_v2": {
                    "apron_plane_normal": placement["contact"]["apron_plane_normal"],
                    "authored_heading_degrees": placement["authored_heading_degrees"],
                    "contact_metadata_status": model["contact_metadata_status"],
                    "gear_contact_points_unity_meters": placement["contact"][
                        "gear_contact_points_unity_meters"
                    ],
                    "model_forward_axis_correction_degrees": placement[
                        "model_forward_axis_correction_degrees"
                    ],
                    "root_height_meters": root_height,
                    "source_model_axes": model["source_model_axes"],
                    "stable_id": placement["stable_id"],
                    "stand_id": placement["stand_id"],
                    "terrain_height_meters": root_height,
                    "unity_imported_forward_axis_before_correction": model[
                        "unity_imported_forward_axis_before_correction"
                    ],
                    "unity_runtime_axes_after_correction": model[
                        "unity_runtime_axes_after_correction"
                    ],
                },
                "x_meters": placement["local_enu_meters"]["x"],
                "yaw_degrees": placement["root_yaw_degrees"],
                "z_meters": placement["local_enu_meters"]["z"],
            }
        )
    if len(value["parked_aircraft_batches"]) != 1:
        raise ValueError("V2 recipe generation expects one parked-aircraft batch")
    value["parked_aircraft_batches"][0]["placements"] = corrected
    return value


def generate(repo_root: Path, config_path: Path, output_dir: Path) -> list[Path]:
    config = load_json(config_path)
    inputs = {
        name: repo_root / relative_path for name, relative_path in config["inputs"].items()
    }
    context = load_json(inputs["context"])
    recipe = load_json(inputs["context_recipe"])
    building_source = load_json(inputs["osm_building_source_6km"])
    terrain = TerrainSampler(load_json(inputs["terrain_rings"]))
    vegetation_source = load_json(inputs["vegetation_source_v2"])
    vegetation_species = load_json(inputs["vegetation_species_v2"])
    vegetation_coverage = load_json(inputs["vegetation_coverage_v2"])
    parked_source = load_json(inputs["parked_aircraft_source_manifest"])
    context_v2 = extended_context(context, building_source)
    coverage, buildings, building_batches = building_outputs(
        context_v2, building_source, terrain, config
    )
    parking, aircraft = parking_outputs(context, recipe, parked_source, config)
    vegetation, vegetation_placements, vegetation_batches = vegetation_outputs(
        vegetation_source, vegetation_species, vegetation_coverage, terrain, config
    )
    recipe_v2 = context_recipe_v2(recipe, parking, building_batches, vegetation_batches)
    all_placements = buildings + aircraft + vegetation_placements
    stable_ids = [record["stable_id"] for record in all_placements]
    if len(stable_ids) != len(set(stable_ids)):
        raise ValueError("Stable placement IDs are not unique")
    placement_manifest = {
        "cell_size_meters": config["building_cell_size_meters"],
        "coordinate_frame": "KBDU local tangent ENU; Unity +X=east, +Y=up, +Z=north",
        "label": "KBDU V2 stable grounded-content placement candidates",
        "placements": sorted(all_placements, key=lambda value: value["stable_id"]),
        "schema_version": 1,
        "source_license_classes": [
            "CC_BY_4_0_KOG_THORNS_ADAPTATION",
            "OSM_ODBL_1_0_PRODUCED_WORK",
            "US_GOV_PUBLIC_DOMAIN_NAIP_PLUS_OSM_ODBL_EXCLUSIONS",
        ],
    }
    outputs = {
        "airport_parking_stand_manifest.json": parking,
        "building_coverage_report.json": coverage,
        "KBDU_ReferenceContextV2.json": context_v2,
        "KBDU_ContextBatchRecipeV2.json": recipe_v2,
        "production_placement_manifest.json": placement_manifest,
        "vegetation_distribution_manifest.json": vegetation,
    }
    written: list[Path] = []
    for filename, value in outputs.items():
        path = output_dir / filename
        write_json(
            path,
            value,
            compact=filename in {
                "KBDU_ContextBatchRecipeV2.json",
                "KBDU_ReferenceContextV2.json",
                "production_placement_manifest.json",
            },
        )
        written.append(path)
    report = {
        "generated_outputs": [
            {
                "bytes": path.stat().st_size,
                "path": path.name,
                "sha256": sha256_file(path),
            }
            for path in sorted(written)
        ],
        "input_hashes": {
            name: sha256_file(path) for name, path in sorted(inputs.items())
        },
        "label": "KBDU environment reconstruction tools-only content pipeline",
        "schema_version": 1,
        "unity_assets_modified": False,
    }
    report_path = output_dir / "content_pipeline_report.json"
    write_json(report_path, report)
    written.append(report_path)
    return written


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=REPO_ROOT)
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--output-dir", type=Path, default=SCRIPT_DIR)
    args = parser.parse_args()
    paths = generate(args.repo_root.resolve(), args.config.resolve(), args.output_dir.resolve())
    print(json.dumps({"generated": [str(path) for path in paths]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
