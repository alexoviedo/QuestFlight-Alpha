#!/usr/bin/env python3
"""Compile a deterministic, demand-derived full-map Traffic V2 headset candidate.

The compiler consumes the fail-closed Phase-3 canonical road universe and the
frozen CDOT/Boulder County count observations.  It emits compact road
corridors, not per-second keyframes: Unity derives continuous departure streams
from each corridor's speed, headway, direction, and deterministic phase.

This is a headset-candidate compiler.  It does not claim legal turns,
intersection control, collision acceptance, or Quest performance acceptance.
Those gates require later dynamic and physical-headset evidence.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from collections import defaultdict
from pathlib import Path
from statistics import median
from typing import Any, Iterable

from pyproj import Transformer
from shapely.geometry import LineString, Point, shape
from shapely.strtree import STRtree


SCHEMA = "questflight.traffic-v2.full-map-corridor-schedule.v1"
REPORT_SCHEMA = "questflight.traffic-v2.full-map-corridor-report.v1"
HASH_SCHEMA = "questflight.traffic-v2.full-map-corridor-hashes.v1"
STATUS = "FULL_MAP_HEADSET_CANDIDATE"
ROAD_CLASSES = ("motorway", "trunk", "primary", "secondary", "tertiary", "residential")
CLASS_SPEED_MPS = {
    "motorway": 27.0,
    "trunk": 23.0,
    "primary": 18.0,
    "secondary": 14.0,
    "tertiary": 11.0,
    "residential": 8.0,
}
CLASS_FALLBACK_AADT = {
    "motorway": 48000.0,
    "trunk": 32000.0,
    "primary": 18000.0,
    "secondary": 9000.0,
    "tertiary": 4200.0,
    "residential": 900.0,
}
CLASS_MAX_HEADWAY_SECONDS = {
    "motorway": 18.0,
    "trunk": 24.0,
    "primary": 34.0,
    "secondary": 55.0,
    "tertiary": 95.0,
    "residential": 240.0,
}
CLASS_MIN_HEADWAY_SECONDS = {
    "motorway": 2.8,
    "trunk": 3.2,
    "primary": 4.0,
    "secondary": 5.0,
    "tertiary": 7.0,
    "residential": 12.0,
}
TILE_SIZE_METERS = 1000.0
BOUNDS_MIN = -12000.0
BOUNDS_MAX = 12000.0
MIN_CORRIDOR_LENGTH_METERS = 12.0
MAX_POINTS_PER_CORRIDOR = 256
HEADSET_DEMAND_SCALE = 0.35


class FullMapCompileError(RuntimeError):
    """A fail-closed full-map compilation error."""


def canonical_json_bytes(value: Any) -> bytes:
    return (
        json.dumps(
            value,
            allow_nan=False,
            ensure_ascii=False,
            separators=(",", ":"),
            sort_keys=True,
        )
        + "\n"
    ).encode("utf-8")


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical_json_bytes(value))


def write_jsonl(path: Path, values: Iterable[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as handle:
        for value in values:
            handle.write(canonical_json_bytes(value))


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def file_record(path: Path) -> dict[str, Any]:
    return {
        "bytes": path.stat().st_size,
        "name": path.name,
        "sha256": sha256_file(path),
    }


def read_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise FullMapCompileError(f"Invalid JSON input: {path}") from error


def read_jsonl(path: Path) -> Iterable[dict[str, Any]]:
    try:
        with path.open("r", encoding="utf-8") as handle:
            for line_number, line in enumerate(handle, 1):
                if not line.strip():
                    continue
                value = json.loads(line)
                if not isinstance(value, dict):
                    raise FullMapCompileError(
                        f"Canonical row {line_number} is not an object"
                    )
                yield value
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise FullMapCompileError(f"Invalid JSONL input: {path}") from error


def stable_fraction(text: str) -> float:
    digest = hashlib.sha256(text.encode("utf-8")).digest()
    return int.from_bytes(digest[:8], "big") / float(1 << 64)


def identity_token(value: Any) -> str:
    return "".join(character for character in str(value or "").lower() if character.isalnum())


def polyline_length(points: list[list[float]]) -> float:
    return sum(math.dist(first, second) for first, second in zip(points, points[1:]))


def simplify_points(points: list[list[float]]) -> list[list[float]]:
    """Bound runtime parsing while retaining endpoints and the authored shape."""

    if len(points) <= MAX_POINTS_PER_CORRIDOR:
        return points
    line = LineString(points)
    tolerance = 0.05
    while True:
        candidate = [list(value) for value in line.simplify(tolerance).coords]
        if len(candidate) <= MAX_POINTS_PER_CORRIDOR:
            return candidate
        tolerance *= 1.75
        if tolerance > 8.0:
            raise FullMapCompileError("Unable to bound corridor point count")


def direction_mode(record: dict[str, Any]) -> str:
    direction = (
        ((record.get("correspondence_semantics") or {}).get("direction") or {}).get(
            "value"
        )
    )
    if direction in ("FORWARD", "REVERSE", "BIDIRECTIONAL"):
        return direction
    # Non-OSM authorities sometimes lack usable direction metadata.  A single
    # centerline is treated as a two-way representation only where the source
    # did not assert a directional carriageway.
    carriageway = (
        ((record.get("correspondence_semantics") or {}).get("carriageway") or {}).get(
            "value"
        )
    )
    if carriageway == "DIRECTIONAL_CARRIAGEWAY":
        raise FullMapCompileError(
            f"Directional corridor {record.get('canonical_segment_id')} lacks direction"
        )
    return "BIDIRECTIONAL"


def geometry_parts(record: dict[str, Any]) -> list[list[list[float]]]:
    geometry = record.get("geometry") or {}
    kind = geometry.get("type")
    coordinates = geometry.get("coordinates")
    if kind == "LineString" and isinstance(coordinates, list):
        values = [coordinates]
    elif kind == "MultiLineString" and isinstance(coordinates, list):
        values = coordinates
    else:
        raise FullMapCompileError(
            f"Eligible record {record.get('canonical_segment_id')} is not linear"
        )
    result: list[list[list[float]]] = []
    for part in values:
        points: list[list[float]] = []
        for coordinate in part:
            if not isinstance(coordinate, list) or len(coordinate) < 2:
                raise FullMapCompileError("Canonical geometry has an invalid coordinate")
            x, z = float(coordinate[0]), float(coordinate[1])
            if not math.isfinite(x) or not math.isfinite(z):
                raise FullMapCompileError("Canonical geometry has a non-finite coordinate")
            if not (BOUNDS_MIN <= x <= BOUNDS_MAX and BOUNDS_MIN <= z <= BOUNDS_MAX):
                raise FullMapCompileError("Canonical geometry escapes the frozen bounds")
            if not points or math.dist(points[-1], [x, z]) > 1e-4:
                points.append([x, z])
        if len(points) >= 2 and polyline_length(points) >= MIN_CORRIDOR_LENGTH_METERS:
            result.append(simplify_points(points))
    return result


def flatten_points(points: list[list[float]]) -> list[float]:
    flattened: list[float] = []
    for x, z in points:
        flattened.extend((round(x, 3), round(z, 3)))
    return flattened


def load_count_observations(
    external_run: Path, projection: str
) -> tuple[list[dict[str, Any]], STRtree]:
    transformer = Transformer.from_crs("EPSG:4326", projection, always_xy=True)
    observations: list[dict[str, Any]] = []

    def projected_line(path: list[list[float]]) -> LineString:
        return LineString([transformer.transform(value[0], value[1]) for value in path])

    cdot_root = external_run / "raw/cdot_traffic_counts"
    for path in sorted(cdot_root.glob("oid_*.json")):
        payload = read_json(path)
        for feature in payload.get("features") or []:
            attributes = feature.get("attributes") or {}
            aadt = attributes.get("AADT")
            paths = (feature.get("geometry") or {}).get("paths") or []
            if not isinstance(aadt, (int, float)) or isinstance(aadt, bool) or aadt <= 0:
                continue
            for part_index, part in enumerate(paths):
                if len(part) < 2:
                    continue
                observations.append(
                    {
                        "aadt": float(aadt),
                        "geometry": projected_line(part),
                        "id": f"cdot:{attributes.get('OBJECTID')}:{part_index}",
                        "identity_tokens": {
                            identity_token(attributes.get("ROUTE"))
                        } - {""},
                        "source": "cdot_traffic_counts",
                        "year": attributes.get("AADTYR"),
                    }
                )

    county_root = external_run / "raw/county_traffic_stations"
    for path in sorted(county_root.glob("oid_*.json")):
        payload = read_json(path)
        for feature in payload.get("features") or []:
            attributes = feature.get("attributes") or {}
            count = attributes.get("TRAFFIC_COUNT")
            geometry = feature.get("geometry") or {}
            if (
                not isinstance(count, (int, float))
                or isinstance(count, bool)
                or count <= 0
                or not isinstance(geometry.get("x"), (int, float))
                or not isinstance(geometry.get("y"), (int, float))
            ):
                continue
            x, z = transformer.transform(geometry["x"], geometry["y"])
            observations.append(
                {
                    "aadt": float(count),
                    "geometry": Point(x, z),
                    "id": f"county:{attributes.get('OBJECTID')}",
                    "identity_tokens": {
                        identity_token(attributes.get("STREET_NAME")),
                        identity_token(attributes.get("CR")),
                    } - {""},
                    "source": "county_traffic_stations",
                    "year": attributes.get("TRAFFIC_YEAR_COUNTED"),
                }
            )
    if not observations:
        raise FullMapCompileError("Frozen package contains no usable traffic counts")
    return observations, STRtree([value["geometry"] for value in observations])


COUNT_SOURCE_IDS = ("cdot_traffic_counts", "county_traffic_stations")


def validate_frozen_count_inputs(
    repo_root: Path, external_run: Path, freeze: dict[str, Any], registry: dict[str, Any]
) -> dict[str, Any]:
    """Bind every consumed count page, projection and license to the declared freeze."""
    registry_path = repo_root / "tools/traffic_v2/geospatial/source_registry.json"
    record = freeze.get("registry") or {}
    if (record.get("path") != "tools/traffic_v2/geospatial/source_registry.json"
            or record.get("bytes") != registry_path.stat().st_size
            or record.get("sha256") != sha256_file(registry_path)
            or freeze.get("coordinate_contract") != registry.get("coordinate_contract")):
        raise FullMapCompileError("Frozen registry/projection binding differs from current inputs")
    sources = freeze.get("sources") or []
    if not isinstance(sources, list) or len({row.get("id") for row in sources}) != len(sources):
        raise FullMapCompileError("Frozen source inventory is missing or duplicated")
    source_by_id = {row.get("id"): row for row in sources}
    records: list[dict[str, Any]] = []
    licenses: list[dict[str, str]] = []
    for source_id in COUNT_SOURCE_IDS:
        source = source_by_id.get(source_id)
        if not source or not source.get("license_partition"):
            raise FullMapCompileError(f"Frozen count source/license missing: {source_id}")
        expected: dict[str, dict[str, Any]] = {}
        def walk(value: Any) -> None:
            if isinstance(value, dict):
                if {"path", "sha256", "bytes"}.issubset(value):
                    name = value["path"]
                    if isinstance(name, str) and name.startswith(f"raw/{source_id}/oid_") and name.endswith(".json"):
                        if name in expected and expected[name] != value:
                            raise FullMapCompileError("Contradictory frozen count page records")
                        expected[name] = value
                for child in value.values():
                    walk(child)
            elif isinstance(value, list):
                for child in value:
                    walk(child)
        walk(source)
        current = {path.relative_to(external_run).as_posix(): path for path in (external_run / "raw" / source_id).glob("oid_*.json")}
        if set(expected) != set(current):
            raise FullMapCompileError(f"Frozen count page membership differs: {source_id}")
        for name, page in sorted(expected.items()):
            path = current[name]
            if not path.resolve().is_relative_to(external_run.resolve()) or path.is_symlink():
                raise FullMapCompileError("Frozen count page escapes external package")
            if page["bytes"] != path.stat().st_size or page["sha256"] != sha256_file(path):
                raise FullMapCompileError(f"Frozen count page bytes/hash differ: {name}")
            records.append({"path": name, "bytes": page["bytes"], "sha256": page["sha256"], "source_id": source_id})
        licenses.append({"id": source_id, "license_partition": source["license_partition"]})
    if not records:
        raise FullMapCompileError("Frozen count inventory has no consumed count pages")
    binding = {"registry": record, "coordinate_contract": registry["coordinate_contract"], "count_pages": records, "sources": licenses}
    return {**binding, "sha256": hashlib.sha256(canonical_json_bytes(binding)).hexdigest()}


def complete_cell_inventory(coverage: dict[str, Any]) -> bool:
    bounds = coverage.get("bounds_evaluation") or {}
    cells = bounds.get("cells") or []
    expected = {(column, row) for row in range(24) for column in range(24)}
    if not isinstance(cells, list) or len(cells) != 576:
        return False
    observed = {(cell.get("column"), cell.get("row")) for cell in cells}
    return (observed == expected and bounds.get("cell_count") == 576
            and bounds.get("evaluated_cell_count") == 576 and bounds.get("status") == "PASS"
            and bounds.get("exact_bounds_xz_meters") == [-12000.0, -12000.0, 12000.0, 12000.0]
            and all(cell.get("evaluated") is True for cell in cells))


def direct_count_for_geometry(
    geometry: LineString,
    road_class: str,
    identity_tokens: set[str],
    observations: list[dict[str, Any]],
    tree: STRtree,
) -> dict[str, Any] | None:
    candidates: list[tuple[float, dict[str, Any]]] = []
    for raw_index in tree.query(geometry.buffer(55.0)):
        observation = observations[int(raw_index)]
        distance = geometry.distance(observation["geometry"])
        if observation["source"] == "cdot_traffic_counts":
            if road_class not in {"motorway", "trunk", "primary"} or distance > 15.0:
                continue
            overlap = geometry.intersection(observation["geometry"].buffer(5.0)).length
            if overlap < min(50.0, geometry.length * 0.5):
                continue
        else:
            if road_class not in {"primary", "secondary", "tertiary", "residential"}:
                continue
            if distance > 35.0 or not (
                identity_tokens & observation["identity_tokens"]
            ):
                continue
        candidates.append((distance, observation))
    if not candidates:
        return None
    distance, observation = min(candidates, key=lambda value: (value[0], value[1]["id"]))
    return {**observation, "distance_meters": round(distance, 3)}


def tile_index(value: float) -> int:
    return min(23, max(0, int(math.floor((value - BOUNDS_MIN) / TILE_SIZE_METERS))))


def build_corridors(
    canonical_path: Path,
    observations: list[dict[str, Any]],
    tree: STRtree,
    *,
    input_records: Iterable[dict[str, Any]] | None = None,
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], dict[str, Any]]:
    provisional: list[dict[str, Any]] = []
    direct_by_class: dict[str, list[float]] = defaultdict(list)
    canonical_input_count = 0
    skipped_explicit_count = 0
    short_geometry_record_count = 0
    for record in input_records if input_records is not None else read_jsonl(canonical_path):
        canonical_input_count += 1
        if record.get("eligibility_status_or_exclusion_reason") != "ELIGIBLE":
            skipped_explicit_count += 1
            continue
        road_class = record.get("road_class")
        lane_count = record.get("lane_count")
        if road_class not in ROAD_CLASSES:
            raise FullMapCompileError("Eligible canonical road has unknown class")
        if not isinstance(lane_count, int) or isinstance(lane_count, bool) or lane_count <= 0:
            raise FullMapCompileError("Eligible canonical road has unresolved lanes")
        parts = geometry_parts(record)
        if not parts:
            short_geometry_record_count += 1
        for part_index, points in enumerate(parts):
            line = LineString(points)
            direct = direct_count_for_geometry(
                line,
                road_class,
                {
                    identity_token(value)
                    for value in record.get("correspondence_identity_tokens") or []
                    if identity_token(value)
                },
                observations,
                tree,
            )
            if direct is not None:
                direct_by_class[road_class].append(direct["aadt"])
            provisional.append(
                {
                    "aadt_direct": direct,
                    "canonical_segment_id": record["canonical_segment_id"],
                    "direction_mode": direction_mode(record),
                    "lane_count": lane_count,
                    "lane_count_basis": record.get(
                        "headset_development_lane_count_basis",
                        "CANONICAL_EVIDENCE",
                    ),
                    "license_partition": record.get("license_partition"),
                    "part_index": part_index,
                    "points": points,
                    "road_class": road_class,
                    "source_id": record.get("source_id"),
                    "source_record_id": str(record.get("source_record_id")),
                    "source_record_part": int(record.get("source_record_part") or 0),
                }
            )
    if not provisional:
        raise FullMapCompileError("Canonical universe produced no eligible corridors")

    class_aadt: dict[str, float] = {}
    for road_class in ROAD_CLASSES:
        values = direct_by_class.get(road_class) or []
        class_aadt[road_class] = round(
            median(values) if len(values) >= 3 else CLASS_FALLBACK_AADT[road_class], 3
        )

    corridors: list[dict[str, Any]] = []
    tile_members: dict[tuple[int, int], list[int]] = defaultdict(list)
    direct_corridors = 0
    estimated_active = 0.0
    capacity_capped_corridors = 0
    discoverability_floored_corridors = 0
    for item in provisional:
        points = item.pop("points")
        direct = item.pop("aadt_direct")
        length = polyline_length(points)
        road_class = item["road_class"]
        aadt = direct["aadt"] if direct is not None else class_aadt[road_class]
        direction_count = 1 if item["direction_mode"] in ("FORWARD", "REVERSE") else 2
        raw_headway = 86400.0 * direction_count / (aadt * HEADSET_DEMAND_SCALE)
        if raw_headway < CLASS_MIN_HEADWAY_SECONDS[road_class]:
            capacity_capped_corridors += 1
        if raw_headway > CLASS_MAX_HEADWAY_SECONDS[road_class]:
            discoverability_floored_corridors += 1
        headway = min(
            CLASS_MAX_HEADWAY_SECONDS[road_class],
            max(CLASS_MIN_HEADWAY_SECONDS[road_class], raw_headway),
        )
        speed = CLASS_SPEED_MPS[road_class]
        phase = stable_fraction(
            f"{item['canonical_segment_id']}:{item['part_index']}:departure"
        ) * headway
        corridor_id = f"{item['canonical_segment_id']}:{item['part_index']}"
        bounds = LineString(points).bounds
        record = {
            **item,
            "aadt": round(aadt, 3),
            "aadt_authority": (
                {
                    "distance_meters": direct["distance_meters"],
                    "observation_id": direct["id"],
                    "source_id": direct["source"],
                    "year": direct["year"],
                }
                if direct is not None
                else {
                    "class_median_aadt": class_aadt[road_class],
                    "source_id": "CLASS_MEDIAN_OR_FROZEN_FALLBACK",
                }
            ),
            "corridor_id": corridor_id,
            "headway_seconds": round(headway, 3),
            "length_meters": round(length, 3),
            "phase_seconds": round(phase, 3),
            "points_xz_meters": flatten_points(points),
            "speed_mps": speed,
            "tile_bounds": [
                tile_index(bounds[0]),
                tile_index(bounds[1]),
                tile_index(bounds[2]),
                tile_index(bounds[3]),
            ],
        }
        corridor_index = len(corridors)
        corridors.append(record)
        if direct is not None:
            direct_corridors += 1
        estimated_active += direction_count * length / speed / headway
        for tile_x in range(record["tile_bounds"][0], record["tile_bounds"][2] + 1):
            for tile_z in range(record["tile_bounds"][1], record["tile_bounds"][3] + 1):
                tile_members[(tile_x, tile_z)].append(corridor_index)

    tiles = [
        {
            "corridor_indices": indices,
            "tile_x": tile_x,
            "tile_z": tile_z,
        }
        for (tile_x, tile_z), indices in sorted(tile_members.items())
    ]
    metrics = {
        "canonical_input_record_count": canonical_input_count,
        "capacity_capped_corridor_count": capacity_capped_corridors,
        "class_aadt_medians_or_fallbacks": class_aadt,
        "corridor_count": len(corridors),
        "direct_count_corridor_count": direct_corridors,
        "discoverability_floored_corridor_count": discoverability_floored_corridors,
        "estimated_full_map_active_vehicle_count": round(estimated_active, 3),
        "headset_demand_scale": HEADSET_DEMAND_SCALE,
        "headset_development_lane_default_corridor_count": sum(
            value.get("lane_count_basis") == "DISCLOSED_HEADSET_DEVELOPMENT_DEFAULT"
            for value in corridors
        ),
        "explicit_or_unresolved_record_count": skipped_explicit_count,
        "short_geometry_record_count": short_geometry_record_count,
        "source_count_observation_count": len(observations),
        "tile_count": len(tiles),
        "total_corridor_kilometers": round(
            sum(value["length_meters"] for value in corridors) / 1000.0, 6
        ),
    }
    return corridors, tiles, metrics


def normalized_osm_candidate_records(
    normalized_path: Path,
) -> Iterable[dict[str, Any]]:
    """Adapt the completed current-rule OSM normalization to a dev candidate.

    Missing lane tags use a disclosed headset-only default (one lane for a
    directional carriageway, two total for a bidirectional centerline). The
    canonical authority compile replaces these defaults when it completes.
    """

    for row in read_jsonl(normalized_path):
        eligibility = row.get("eligibility") or {}
        if eligibility.get("decision") != "ELIGIBLE":
            continue
        direction = (eligibility.get("oneway_evidence") or {}).get("value")
        if direction not in ("FORWARD", "REVERSE", "BIDIRECTIONAL"):
            continue
        lane_count = (row.get("lane_evidence") or {}).get("lane_count")
        lane_count_defaulted = False
        if not isinstance(lane_count, int) or isinstance(lane_count, bool) or lane_count <= 0:
            lane_count = 2 if direction == "BIDIRECTIONAL" else 1
            lane_count_defaulted = True
        yield {
            "canonical_segment_id": (
                f"normalized:{row['source_id']}:{row['source_record_id']}:"
                f"{row.get('source_record_part', 0)}"
            ),
            "correspondence_identity_tokens": [
                value
                for value in (
                    (row.get("tags") or {}).get("name"),
                    (row.get("tags") or {}).get("ref"),
                )
                if value
            ],
            "correspondence_semantics": {
                "carriageway": {
                    "value": (
                        "SINGLE_CENTERLINE"
                        if direction == "BIDIRECTIONAL"
                        else "DIRECTIONAL_CARRIAGEWAY"
                    )
                },
                "direction": {"value": direction},
            },
            "eligibility_status_or_exclusion_reason": "ELIGIBLE",
            "geometry": row["geometry"],
            "lane_count": lane_count,
            "headset_development_lane_count_basis": (
                "DISCLOSED_HEADSET_DEVELOPMENT_DEFAULT"
                if lane_count_defaulted
                else "OSM_DECLARED_EVIDENCE"
            ),
            "license_partition": "ODbL-1.0",
            "road_class": row["highway_class"],
            "source_id": row["source_id"],
            "source_record_id": row["source_record_id"],
            "source_record_part": row.get("source_record_part", 0),
        }


def canonical_checkpoint_candidate_records(
    canonical_path: Path,
) -> Iterable[dict[str, Any]]:
    """Adapt a completed canonical-selection checkpoint for headset development.

    The Phase-3 acceptance compiler deliberately leaves an eligible record's
    lane count unresolved when no authoritative or proven corresponding source
    supplies it.  That must remain a blocking condition for acceptance.  A
    headset-only development build may still exercise the complete selected
    geometry by applying a disclosed one-lane directional or two-lane
    bidirectional runtime default.  The evidence ledger records the basis on
    every emitted corridor, and this adapter never changes the canonical input.
    """

    for row in read_jsonl(canonical_path):
        if row.get("eligibility_status_or_exclusion_reason") != "ELIGIBLE":
            yield row
            continue
        lane_count = row.get("lane_count")
        if isinstance(lane_count, int) and not isinstance(lane_count, bool) and lane_count > 0:
            basis = "CANONICAL_EVIDENCE"
        else:
            direction = direction_mode(row)
            lane_count = 2 if direction == "BIDIRECTIONAL" else 1
            basis = "DISCLOSED_HEADSET_DEVELOPMENT_DEFAULT"
        yield {
            **row,
            "headset_development_lane_count_basis": basis,
            "lane_count": lane_count,
        }


def compile_full_map(
    repo_root: Path,
    external_run: Path,
    output_dir: Path,
    expected_freeze_sha256: str,
    *,
    normalized_osm_candidate: Path | None = None,
    expected_normalized_osm_sha256: str | None = None,
    canonical_checkpoint_candidate: Path | None = None,
    expected_canonical_checkpoint_sha256: str | None = None,
) -> dict[str, Any]:
    if normalized_osm_candidate is not None and canonical_checkpoint_candidate is not None:
        raise FullMapCompileError(
            "Normalized OSM and canonical-checkpoint candidate modes are mutually exclusive"
        )
    freeze_path = external_run / "freeze_manifest.json"
    run_manifest_path = external_run / "run_manifest.json"
    coverage_path = repo_root / "docs/data/traffic_v2/traffic_source_coverage.json"
    canonical_path = external_run / "intermediates/canonical_road_universe.geojsonl"
    required_paths = [freeze_path]
    if normalized_osm_candidate is None and canonical_checkpoint_candidate is None:
        required_paths.extend((run_manifest_path, coverage_path, canonical_path))
    elif normalized_osm_candidate is not None:
        required_paths.append(normalized_osm_candidate)
    else:
        required_paths.append(canonical_checkpoint_candidate)
    for required in required_paths:
        if not required.is_file():
            raise FullMapCompileError(f"Missing full-map prerequisite: {required}")
    actual_freeze_sha = sha256_file(freeze_path)
    if actual_freeze_sha != expected_freeze_sha256:
        raise FullMapCompileError("Source freeze SHA-256 mismatch")
    freeze = read_json(freeze_path)
    registry_path = repo_root / "tools/traffic_v2/geospatial/source_registry.json"
    registry = read_json(registry_path)
    frozen_count_binding = validate_frozen_count_inputs(repo_root, external_run, freeze, registry)
    coverage: dict[str, Any] = {}
    observations, tree = load_count_observations(
        external_run, registry["coordinate_contract"]["projection"]
    )
    if normalized_osm_candidate is None and canonical_checkpoint_candidate is None:
        source_run = read_json(run_manifest_path)
        coverage = read_json(coverage_path)
        coverage_output = (source_run.get("outputs") or {}).get(
            "traffic_source_coverage.json"
        ) or {}
        if (
            source_run.get("freeze_sha256") != expected_freeze_sha256
            or coverage_output.get("sha256") != sha256_file(coverage_path)
            or coverage_output.get("bytes") != coverage_path.stat().st_size
        ):
            raise FullMapCompileError("Run manifest does not bind the live source coverage")
        canonical_record = (
            (coverage.get("canonical_road_universe") or {}).get("canonical_universe") or {}
        )
        if (
            coverage.get("source_freeze_sha256") != expected_freeze_sha256
            or canonical_record.get("path") != "intermediates/canonical_road_universe.geojsonl"
            or canonical_record.get("sha256") != sha256_file(canonical_path)
            or canonical_record.get("bytes") != canonical_path.stat().st_size
        ):
            raise FullMapCompileError("Coverage does not bind the live canonical universe")
        adjudication = source_run.get("approval_adjudication") or {}
        remaining_blockers = adjudication.get("remaining_blocker_ids") or []
        if adjudication.get("effective_gate_status") == "PASS":
            gate_status = "PASS_PHASE3_SOURCE_GATE"
        elif remaining_blockers == ["benchmark_camera_road_inventory_gap"]:
            gate_status = "PENDING_LEAD_D_INSPECTION"
        else:
            raise FullMapCompileError(
                "Phase-3 source gate retains non-camera blockers: "
                + ",".join(str(value) for value in remaining_blockers)
            )
        corridors, tiles, metrics = build_corridors(
            canonical_path, observations, tree
        )
        source_summaries = coverage.get("sources") or []
        candidate_limitations: list[str] = []
        candidate_source_mode = "PHASE3_CANONICAL"
    elif normalized_osm_candidate is not None:
        if (
            not expected_normalized_osm_sha256
            or sha256_file(normalized_osm_candidate) != expected_normalized_osm_sha256
        ):
            raise FullMapCompileError("Normalized OSM candidate SHA-256 mismatch")
        canonical_record = {
            "bytes": normalized_osm_candidate.stat().st_size,
            "path": "candidate_input/osm_bounded_roads.normalized.geojsonl",
            "sha256": expected_normalized_osm_sha256,
        }
        gate_status = "PENDING_CANONICAL_SOURCE_GATE"
        corridors, tiles, metrics = build_corridors(
            normalized_osm_candidate,
            observations,
            tree,
            input_records=normalized_osm_candidate_records(normalized_osm_candidate),
        )
        source_summaries = [
            {"id": "osm_bounded_roads", "license_partition": "ODbL-1.0"},
            *frozen_count_binding["sources"],
        ]
        candidate_limitations = [
            "The authority-conflated canonical universe was still compiling; this candidate uses the exact completed current-rule normalized OSM universe.",
            "Missing OSM lane tags use disclosed one-lane directional or two-lane bidirectional headset-only defaults.",
        ]
        candidate_source_mode = "NORMALIZED_OSM_FALLBACK"
    else:
        if (
            not expected_canonical_checkpoint_sha256
            or sha256_file(canonical_checkpoint_candidate)
            != expected_canonical_checkpoint_sha256
        ):
            raise FullMapCompileError("Canonical checkpoint SHA-256 mismatch")
        canonical_record = {
            "bytes": canonical_checkpoint_candidate.stat().st_size,
            "path": "candidate_input/canonical_road_universe.geojsonl",
            "sha256": expected_canonical_checkpoint_sha256,
        }
        gate_status = "PENDING_PHASE3_SOURCE_GATE"
        corridors, tiles, metrics = build_corridors(
            canonical_checkpoint_candidate,
            observations,
            tree,
            input_records=canonical_checkpoint_candidate_records(
                canonical_checkpoint_candidate
            ),
        )
        freeze_sources = {
            str(value.get("id")): value for value in freeze.get("sources") or []
        }
        corridor_source_ids = sorted(
            {str(value.get("source_id")) for value in corridors if value.get("source_id")}
        )
        missing_source_ids = [
            source_id for source_id in corridor_source_ids if source_id not in freeze_sources
        ]
        if missing_source_ids:
            raise FullMapCompileError(
                "Canonical checkpoint uses sources absent from the freeze: "
                + ",".join(missing_source_ids)
            )
        source_summaries = [
            {
                "id": source_id,
                "license_partition": freeze_sources[source_id].get("license_partition"),
            }
            for source_id in corridor_source_ids
        ]
        candidate_limitations = [
            "This candidate uses the exact completed authority-conflated canonical-selection checkpoint; the final Phase-3 reference, approval, and normative-output phases did not complete.",
            (
                f"{metrics['headset_development_lane_default_corridor_count']} emitted corridors without canonical lane totals use disclosed one-lane directional or two-lane bidirectional headset-development defaults."
            ),
            "Canonical unknown class, access, correspondence, and explicitly excluded records remain absent; this development adapter does not clear any Phase-3 acceptance blocker.",
        ]
        candidate_source_mode = (
            "CANONICAL_SELECTION_CHECKPOINT_WITH_DISCLOSED_LANE_DEFAULTS"
        )
    source_summaries = list({row["id"]: row for row in [*source_summaries, *frozen_count_binding["sources"]]}.values())
    runtime_corridor_keys = (
        "corridor_id",
        "direction_mode",
        "headway_seconds",
        "lane_count",
        "length_meters",
        "phase_seconds",
        "points_xz_meters",
        "road_class",
        "speed_mps",
        "tile_bounds",
    )
    runtime_corridors = [
        {key: corridor[key] for key in runtime_corridor_keys}
        for corridor in corridors
    ]
    manifest = {
        "accepted_source_sha": registry["accepted_source_sha"],
        "bounds_xz_meters": registry["coordinate_contract"]["bounds_xz_meters"],
        "canonical_universe": canonical_record,
        "candidate_source_mode": candidate_source_mode,
        "clock_contract": "CONTINUOUS_DETERMINISTIC_DEPARTURE_STREAMS",
        "corridors": runtime_corridors,
        "limitations": candidate_limitations + [
            "Headset candidate only; not Traffic V2 acceptance.",
            "Count observations are direct where spatially bound; otherwise frozen class medians/fallbacks are explicit.",
            "Minimum/maximum headways bound Quest load and provide a disclosed full-map discoverability floor; they are not measured demand.",
            "The headset candidate renders 35% of average-daily source flow before explicit headway bounds; this is a load-controlled visualization scale.",
            "Corridor endpoints spawn/despawn vehicles; legal turns and intersection controls are deferred.",
            "Physical Quest frame, thermal, stereo visibility, collision, and density gates remain required.",
        ],
        "metrics": metrics,
        "schema": SCHEMA,
        "source_freeze_sha256": expected_freeze_sha256,
        "frozen_count_inputs": frozen_count_binding,
        "source_gate_status": gate_status,
        "status": STATUS,
        "tile_size_meters": TILE_SIZE_METERS,
        "tiles": tiles,
    }
    output_dir.mkdir(parents=True, exist_ok=True)
    manifest_path = output_dir / "traffic_v2_full_map_manifest.json"
    report_path = output_dir / "traffic_v2_full_map_report.json"
    provenance_path = output_dir / "traffic_v2_full_map_source_and_license.json"
    ledger_path = output_dir / "traffic_v2_full_map_corridor_evidence.jsonl"
    hashes_path = output_dir / "traffic_v2_full_map_hashes.json"
    write_json(manifest_path, manifest)
    write_jsonl(ledger_path, corridors)
    provenance = {
        "accepted_source_sha": registry["accepted_source_sha"],
        "candidate_source_mode": candidate_source_mode,
        "canonical_universe": canonical_record,
        "credits": [
            "© OpenStreetMap contributors",
            "Boulder County and City of Boulder public geospatial records",
            "Colorado Department of Transportation public geospatial records",
            "Denver Regional Council of Governments public geospatial records",
        ],
        "corridor_evidence": file_record(ledger_path),
        "generated_manifest": file_record(manifest_path),
        "license_partitions": sorted(
            {
                str(value.get("license_partition"))
                for value in source_summaries
                if value.get("license_partition")
            }
        ),
        "limitations": manifest["limitations"],
        "osm_license": "Open Database License 1.0 (ODbL-1.0)",
        "product_asset": (
            "Assets/Production/Environment/TrafficV2/Data/"
            "KBDU_TrafficV2FullMapManifest.json"
        ),
        "schema": "questflight.traffic-v2.full-map-source-and-license.v1",
        "source_freeze_sha256": expected_freeze_sha256,
        "frozen_count_inputs": frozen_count_binding,
        "source_gate_status": gate_status,
        "source_ids": sorted(
            str(value.get("id"))
            for value in source_summaries
            if value.get("id")
        ),
        "status": STATUS,
    }
    write_json(provenance_path, provenance)
    report = {
        "candidate_source_mode": candidate_source_mode,
        "checks": {
            "all_576_source_cells_evaluated": complete_cell_inventory(coverage),
            "count_inputs_frozen_and_verified": True,
            "canonical_hash_bound": True,
            "continuous_departure_clock": True,
            "corridors_cover_all_eligible_canonical_parts": (
                metrics["short_geometry_record_count"] == 0
            ),
            "source_gate_status": gate_status,
        },
        "manifest": file_record(manifest_path),
        "metrics": metrics,
        "corridor_evidence": file_record(ledger_path),
        "source_and_license": file_record(provenance_path),
        "schema": REPORT_SCHEMA,
        "status": STATUS,
    }
    write_json(report_path, report)
    hashes = {
        "artifacts": {
            path.name: file_record(path)
            for path in (ledger_path, manifest_path, provenance_path, report_path)
        },
        "schema": HASH_SCHEMA,
        "source_freeze_sha256": expected_freeze_sha256,
    }
    write_json(hashes_path, hashes)
    return report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--external-run", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--source-freeze-sha256", required=True)
    parser.add_argument("--normalized-osm-candidate", type=Path)
    parser.add_argument("--normalized-osm-sha256")
    parser.add_argument("--canonical-checkpoint-candidate", type=Path)
    parser.add_argument("--canonical-checkpoint-sha256")
    arguments = parser.parse_args()
    try:
        report = compile_full_map(
            arguments.repo_root.resolve(),
            arguments.external_run.resolve(),
            arguments.output_dir.resolve(),
            arguments.source_freeze_sha256,
            normalized_osm_candidate=(
                arguments.normalized_osm_candidate.resolve()
                if arguments.normalized_osm_candidate
                else None
            ),
            expected_normalized_osm_sha256=arguments.normalized_osm_sha256,
            canonical_checkpoint_candidate=(
                arguments.canonical_checkpoint_candidate.resolve()
                if arguments.canonical_checkpoint_candidate
                else None
            ),
            expected_canonical_checkpoint_sha256=(
                arguments.canonical_checkpoint_sha256
            ),
        )
    except FullMapCompileError as error:
        print(f"FULL_MAP_COMPILE_ERROR: {error}")
        return 2
    print(canonical_json_bytes(report).decode("utf-8"), end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
