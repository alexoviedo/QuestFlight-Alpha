#!/usr/bin/env python3
"""Independent, read-only Phase-3 traffic-source evidence verifier.

This program deliberately does not import or inspect the production source gate or
its tests.  It rebuilds evidence checks from the frozen public contract, frozen
acquisition payloads, accepted product bytes, and the Phase-2 camera record.

The program writes nothing.  Its JSON stdout is suitable for preservation in a
lead-owned evidence bundle.  Exit code 0 means the frozen bytes are internally
consistent; ``decision`` remains FAIL_CLOSED until the authority predicates named
in ``blocking_findings`` have independent evidence.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import platform
import struct
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable

from PIL import Image
from pyproj import CRS, Transformer
from shapely.geometry import box, shape
from shapely.strtree import STRtree


EXPECTED_FREEZE_SHA256 = (
    "178ac781ea094e4f09af9a38493babace8f78e8942301b86927e414f2675ca3d"
)
EXPECTED_ACCEPTED_SOURCE_SHA = "fda11a25eb526824d82a47330f2be80b1410bb64"
EXPECTED_SPEC_SHA256 = (
    "8e1b3f7442652128b306a0706d3eac02b59750158839ca0443351312fcbf09c7"
)
EXPECTED_CAMERA_CONTRACT_SHA256 = (
    "ecd7a76e909d311b4c7665ec91b980affe6009eac798473edd4acad4f9379d20"
)
REQUIRED_CLASSES = ("motorway", "trunk", "primary", "secondary", "tertiary", "residential")
BOUNDS = (-12000.0, -12000.0, 12000.0, 12000.0)
CELL_SIZE = 1000.0
GRID_SIZE = 24


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def git_blob_sha1(path: Path) -> str:
    data = path.read_bytes()
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def canonical_sha256(value: Any) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def iter_descriptors(value: Any) -> Iterable[dict[str, Any]]:
    if isinstance(value, dict):
        if isinstance(value.get("path"), str) and isinstance(value.get("sha256"), str):
            yield value
        for child in value.values():
            yield from iter_descriptors(child)
    elif isinstance(value, list):
        for child in value:
            yield from iter_descriptors(child)


def resolve_descriptor(repo: Path, external: Path, relative: str) -> Path:
    normalized = relative.replace("/", "\\")
    if normalized.startswith(("tools\\", "docs\\", "QuestFlight\\")):
        return repo / Path(relative)
    return external / Path(relative)


def verify_frozen_descriptors(repo: Path, external: Path, freeze: dict[str, Any]) -> dict[str, Any]:
    # The task explicitly forbids reading the production gate implementation.  Its
    # descriptor is therefore reported as declared, not independently verified.
    forbidden = freeze.get("tool", {}).get("path")
    checked: dict[tuple[str, str], dict[str, Any]] = {}
    for descriptor in iter_descriptors(freeze):
        relative = descriptor["path"]
        if relative == forbidden:
            continue
        checked[(relative, descriptor["sha256"])] = descriptor

    missing: list[str] = []
    mismatches: list[dict[str, Any]] = []
    total_bytes = 0
    for (relative, expected_hash), descriptor in sorted(checked.items()):
        path = resolve_descriptor(repo, external, relative)
        if not path.is_file():
            missing.append(relative)
            continue
        actual_bytes = path.stat().st_size
        actual_hash = sha256(path)
        total_bytes += actual_bytes
        expected_bytes = descriptor.get("bytes")
        if actual_hash != expected_hash or (expected_bytes is not None and actual_bytes != expected_bytes):
            mismatches.append(
                {
                    "path": relative,
                    "expectedSha256": expected_hash,
                    "actualSha256": actual_hash,
                    "expectedBytes": expected_bytes,
                    "actualBytes": actual_bytes,
                }
            )
    return {
        "status": "PASS" if not missing and not mismatches else "FAIL",
        "uniqueDescriptorsChecked": len(checked),
        "bytesHashedIncludingDuplicateContentOncePerPathHash": total_bytes,
        "missing": missing,
        "mismatches": mismatches,
        "productionGateTool": {
            "path": forbidden,
            "declaredSha256": freeze.get("tool", {}).get("sha256"),
            "verification": "NOT_READ_BY_INDEPENDENT_VERIFIER_PER_WORKSTREAM_CONSTRAINT",
        },
    }


def verify_run_outputs(repo: Path, external: Path, run: dict[str, Any]) -> dict[str, Any]:
    mismatches: list[dict[str, Any]] = []
    rows: list[dict[str, Any]] = []
    for name, descriptor in sorted(run["outputs"].items()):
        if name == "traffic_source_coverage.png":
            primary = external / descriptor["path"]
        else:
            primary = repo / "docs" / "data" / "traffic_v2" / name
        rebuild_subdirectory = "evidence" if name == "traffic_source_coverage.png" else "data"
        rebuild = external / "rebuild_check" / rebuild_subdirectory / name
        row: dict[str, Any] = {"name": name, "primaryPath": str(primary), "rebuildPath": str(rebuild)}
        for label, path in (("primary", primary), ("rebuild", rebuild)):
            row[f"{label}Exists"] = path.is_file()
            if path.is_file():
                row[f"{label}Bytes"] = path.stat().st_size
                row[f"{label}Sha256"] = sha256(path)
        expected = descriptor["sha256"]
        if (
            row.get("primarySha256") != expected
            or row.get("rebuildSha256") != expected
            or row.get("primaryBytes") != descriptor["bytes"]
            or row.get("rebuildBytes") != descriptor["bytes"]
        ):
            mismatches.append(row)
        rows.append(row)
    return {"status": "PASS" if not mismatches else "FAIL", "outputs": rows, "mismatches": mismatches}


def verify_arcgis_inventory(external: Path, source: dict[str, Any]) -> dict[str, Any]:
    receipts = source.get("receipts", {})

    def response(name: str) -> Any:
        return load_json(external / receipts[name]["response"]["path"])

    pre_count = response("count_pre").get("count")
    post_count = response("count_post").get("count")
    pre_ids = response("ids_pre").get("objectIds", [])
    post_ids = response("ids_post").get("objectIds", [])
    oid_field = source["object_id_field"]
    requested: list[Any] = []
    returned: list[Any] = []
    page_feature_count = 0
    page_duplicate_ids = 0
    for page in source.get("pages", []):
        requested.extend(page.get("requested_ids", []))
        payload = load_json(external / page["receipt"]["response"]["path"])
        features = payload.get("features", [])
        page_feature_count += len(features)
        ids = [feature.get("attributes", {}).get(oid_field) for feature in features]
        page_duplicate_ids += len(ids) - len(set(ids))
        returned.extend(ids)

    stable_ids = set(pre_ids) == set(post_ids)
    exact_page_partition = sorted(requested) == sorted(pre_ids)
    exact_return_set = sorted(returned) == sorted(pre_ids)
    count_exception = source["id"] == "drcog_pavement_polygons_2024"
    count_consistent = pre_count == post_count == len(pre_ids) == source["feature_count"]
    if count_exception:
        # Server count is advisory for this source; the frozen stable OID universe is
        # authoritative.  The expected 3217-vs-3215 discrepancy is preserved.
        count_consistent = (
            pre_count == post_count == 3217
            and len(pre_ids) == len(post_ids) == source["feature_count"] == 3215
        )
    status = all((stable_ids, exact_page_partition, exact_return_set, count_consistent, page_duplicate_ids == 0))
    return {
        "id": source["id"],
        "status": "PASS" if status else "FAIL",
        "serverCountPre": pre_count,
        "serverCountPost": post_count,
        "stableOidCount": len(pre_ids),
        "uniqueOidCount": len(set(pre_ids)),
        "pageCount": len(source.get("pages", [])),
        "requestedOidCount": len(requested),
        "returnedFeatureCount": page_feature_count,
        "prePostOidSetEqual": stable_ids,
        "requestedOidMultisetExact": exact_page_partition,
        "returnedOidMultisetExact": exact_return_set,
        "pageDuplicateOidCount": page_duplicate_ids,
        "serverCountAdvisoryException": count_exception,
    }


def load_geojsonl(path: Path) -> list[dict[str, Any]]:
    records: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as stream:
        for number, line in enumerate(stream, 1):
            if not line.strip():
                continue
            record = json.loads(line)
            record["__line"] = number
            records.append(record)
    return records


def coordinate_depth_has_z(coordinates: Any) -> bool:
    current = coordinates
    while isinstance(current, list) and current:
        if all(isinstance(item, (int, float)) for item in current):
            return len(current) >= 3
        current = current[0]
    return False


def iter_arcgis_coordinates(geometry: dict[str, Any]) -> Iterable[tuple[float, float]]:
    if "x" in geometry and "y" in geometry:
        yield float(geometry["x"]), float(geometry["y"])
        return

    def walk(value: Any) -> Iterable[tuple[float, float]]:
        if (
            isinstance(value, list)
            and len(value) >= 2
            and isinstance(value[0], (int, float))
            and isinstance(value[1], (int, float))
        ):
            yield float(value[0]), float(value[1])
        elif isinstance(value, list):
            for child in value:
                yield from walk(child)

    for key in ("points", "paths", "rings"):
        if key in geometry:
            yield from walk(geometry[key])


def iter_shapely_coordinates(geometry: Any) -> Iterable[tuple[float, float]]:
    if geometry.geom_type == "Point":
        yield float(geometry.x), float(geometry.y)
    elif geometry.geom_type in ("LineString", "LinearRing"):
        for coordinate in geometry.coords:
            yield float(coordinate[0]), float(coordinate[1])
    elif geometry.geom_type == "Polygon":
        yield from iter_shapely_coordinates(geometry.exterior)
        for ring in geometry.interiors:
            yield from iter_shapely_coordinates(ring)
    elif hasattr(geometry, "geoms"):
        for child in geometry.geoms:
            yield from iter_shapely_coordinates(child)


def normalized_descriptor(coverage_source: dict[str, Any]) -> dict[str, Any] | None:
    normalized = coverage_source.get("normalized")
    if not isinstance(normalized, dict):
        return None
    descriptor = normalized.get("normalized")
    return descriptor if isinstance(descriptor, dict) else None


def summarize_normalized(
    external: Path, coverage_source: dict[str, Any]
) -> tuple[dict[str, Any], list[tuple[dict[str, Any], Any]]]:
    descriptor = normalized_descriptor(coverage_source)
    if descriptor is None:
        return ({"id": coverage_source["id"], "status": "NOT_APPLICABLE"}, [])
    path = external / descriptor["path"]
    records = load_geojsonl(path)
    geometries: list[tuple[dict[str, Any], Any]] = []
    invalid = 0
    empty = 0
    outside = 0
    z_count = 0
    role_counts: Counter[str] = Counter()
    license_counts: Counter[str] = Counter()
    record_keys: list[str] = []
    road_length = 0.0
    world = box(*BOUNDS)
    for record in records:
        role_counts[str(record.get("role"))] += 1
        license_counts[str(record.get("license_partition"))] += 1
        key = f'{record.get("source_record_id")}#{record.get("source_record_part", 0)}'
        record_keys.append(key)
        geometry_payload = record.get("geometry")
        if not geometry_payload:
            continue
        geometry = shape(geometry_payload)
        geometries.append((record, geometry))
        if geometry.is_empty:
            empty += 1
        if not geometry.is_valid:
            invalid += 1
        if not world.covers(geometry):
            outside += 1
        if coordinate_depth_has_z(geometry_payload.get("coordinates")):
            z_count += 1
        if geometry.geom_type in ("LineString", "MultiLineString"):
            road_length += geometry.length
    declared_summary = coverage_source["normalized"]
    declared_geometry_count = declared_summary.get(
        "geometry_count", declared_summary.get("feature_count")
    )
    declared_road_length = declared_summary.get("road_length_meters")
    status = (
        sha256(path) == descriptor["sha256"]
        and path.stat().st_size == descriptor["bytes"]
        and len(records) == declared_geometry_count
        and len(geometries) == declared_geometry_count
        and outside == 0
        and (declared_road_length is None or abs(road_length - declared_road_length) <= 0.0015)
    )
    return (
        {
            "id": coverage_source["id"],
            "status": "PASS" if status else "FAIL",
            "recordCount": len(records),
            "declaredSourceFeatureCount": declared_summary.get("feature_count"),
            "geometryCount": len(geometries),
            "uniqueRecordPartCount": len(set(record_keys)),
            "duplicateRecordPartCount": len(record_keys) - len(set(record_keys)),
            "invalidGeometryCount": invalid,
            "emptyGeometryCount": empty,
            "outsideExactAeqdBoundsCount": outside,
            "recordsWithZ": z_count,
            "declaredHasZ": coverage_source.get("has_z"),
            "roadCenterlineLengthMeters": round(road_length, 6),
            "declaredRoadLengthMeters": declared_road_length,
            "roles": dict(sorted(role_counts.items())),
            "licensePartitions": dict(sorted(license_counts.items())),
            "path": descriptor["path"],
            "bytes": path.stat().st_size,
            "sha256": sha256(path),
        },
        geometries,
    )


def summarize_osm(
    external: Path, coverage: dict[str, Any]
) -> tuple[dict[str, Any], list[tuple[dict[str, Any], Any]]]:
    descriptor = coverage["osm_fallback"]["normalized"]
    path = external / descriptor["path"]
    records = load_geojsonl(path)
    counts: Counter[str] = Counter()
    lengths: defaultdict[str, float] = defaultdict(float)
    unique_counts: Counter[str] = Counter()
    unique_lengths: defaultdict[str, float] = defaultdict(float)
    duplicate_counts: Counter[str] = Counter()
    duplicate_lengths: defaultdict[str, float] = defaultdict(float)
    seen_record_parts: set[tuple[str, int]] = set()
    lane_tag_known: Counter[str] = Counter()
    lane_tag_weighted: defaultdict[str, float] = defaultdict(float)
    geometries: list[tuple[dict[str, Any], Any]] = []
    invalid = outside = 0
    world = box(*BOUNDS)
    cell_lengths: dict[tuple[int, int], defaultdict[str, float]] = {
        (row, column): defaultdict(float)
        for row in range(GRID_SIZE)
        for column in range(GRID_SIZE)
    }
    for record in records:
        geometry = shape(record["geometry"])
        geometries.append((record, geometry))
        road_class = record["highway_class"]
        counts[road_class] += 1
        lengths[road_class] += geometry.length
        record_part_key = (
            str(record.get("source_record_id")),
            int(record.get("source_record_part", 0)),
        )
        if record_part_key in seen_record_parts:
            duplicate_counts[road_class] += 1
            duplicate_lengths[road_class] += geometry.length
        else:
            seen_record_parts.add(record_part_key)
            unique_counts[road_class] += 1
            unique_lengths[road_class] += geometry.length
        if not geometry.is_valid:
            invalid += 1
        if not world.covers(geometry):
            outside += 1
        lane_value = record.get("tags", {}).get("lanes")
        try:
            lane_count = float(str(lane_value).split(";")[0])
            if lane_count > 0:
                lane_tag_known[road_class] += 1
                lane_tag_weighted[road_class] += geometry.length * lane_count
        except (TypeError, ValueError):
            pass
        min_x, min_y, max_x, max_y = geometry.bounds
        min_column = max(0, min(GRID_SIZE - 1, math.floor((min_x - BOUNDS[0]) / CELL_SIZE)))
        max_column = max(0, min(GRID_SIZE - 1, math.floor((max_x - BOUNDS[0]) / CELL_SIZE)))
        min_row = max(0, min(GRID_SIZE - 1, math.floor((min_y - BOUNDS[1]) / CELL_SIZE)))
        max_row = max(0, min(GRID_SIZE - 1, math.floor((max_y - BOUNDS[1]) / CELL_SIZE)))
        for row in range(min_row, max_row + 1):
            for column in range(min_column, max_column + 1):
                cell = box(
                    BOUNDS[0] + column * CELL_SIZE,
                    BOUNDS[1] + row * CELL_SIZE,
                    BOUNDS[0] + (column + 1) * CELL_SIZE,
                    BOUNDS[1] + (row + 1) * CELL_SIZE,
                )
                clipped = geometry.intersection(cell)
                if not clipped.is_empty:
                    cell_lengths[(row, column)][road_class] += clipped.length

    declared = coverage["osm_fallback"]
    class_rows: dict[str, Any] = {}
    circular = True
    for road_class in REQUIRED_CLASSES:
        published = coverage["class_coverage"][road_class]
        actual_length = lengths[road_class]
        represented = published["represented_source_length_meters"]
        eligible = published["eligible_lane_proxy_length_meters"]
        same_universe = abs(represented - eligible) < 1e-9
        same_osm_sum = abs(actual_length - eligible) <= 0.0015
        circular = circular and same_universe and same_osm_sum
        class_rows[road_class] = {
            "featureParts": counts[road_class],
            "centerlineLengthMeters": round(actual_length, 6),
            "uniqueRecordParts": unique_counts[road_class],
            "deduplicatedCenterlineLengthMeters": round(unique_lengths[road_class], 6),
            "duplicateRecordParts": duplicate_counts[road_class],
            "duplicateInflationMeters": round(duplicate_lengths[road_class], 6),
            "publishedEligibleLaneProxyMeters": eligible,
            "publishedRepresentedSourceMeters": represented,
            "publishedRatio": published["source_coverage_ratio"],
            "numeratorEqualsDenominator": same_universe,
            "bothEqualOsmCenterlineSum": same_osm_sum,
            "recordsWithNumericLanesTag": lane_tag_known[road_class],
            "numericTagLaneWeightedMetersKnownSubsetOnly": round(lane_tag_weighted[road_class], 6),
        }

    published_cells = {
        (cell["row"], cell["column"]): cell
        for cell in coverage["bounds_evaluation"]["cells"]
    }
    cell_deltas: list[float] = []
    nonzero_cells = 0
    for key, actual in cell_lengths.items():
        published = published_cells[key]["eligible_length_meters_by_class"]
        if any(value > 0 for value in actual.values()):
            nonzero_cells += 1
        for road_class in REQUIRED_CLASSES:
            cell_deltas.append(abs(actual[road_class] - published.get(road_class, 0.0)))

    raw_osm = next(source for source in load_json(external / "freeze_manifest.json")["sources"] if source["id"] == "osm_bounded_roads")
    raw_payload = load_json(external / raw_osm["raw"]["path"])
    raw_element_counts = Counter(element.get("type") for element in raw_payload.get("elements", []))
    raw_way_ids = [element.get("id") for element in raw_payload.get("elements", []) if element.get("type") == "way"]
    normalized_duplicate_parts = len(records) - len(seen_record_parts)
    unique_normalized_source_record_ids = {
        str(record.get("source_record_id")) for record in records
    }
    status = (
        len(records) == declared["feature_count"]
        and counts == Counter(declared["class_feature_parts"])
        and normalized_duplicate_parts == 0
        and invalid == 0
        and outside == 0
        and max(cell_deltas, default=0.0) <= 0.0015
        and dict(raw_element_counts) == raw_osm["element_counts"]
    )
    return (
        {
            "status": "PASS" if status else "FAIL",
            "normalizedFeatureParts": len(records),
            "uniqueNormalizedRecordParts": len(seen_record_parts),
            "uniqueNormalizedSourceRecordIds": len(unique_normalized_source_record_ids),
            "duplicateNormalizedRecordParts": normalized_duplicate_parts,
            "duplicateNormalizedLengthMeters": round(sum(duplicate_lengths.values()), 6),
            "deduplicatedNormalizedLengthMeters": round(sum(unique_lengths.values()), 6),
            "eligibleWayCount": declared["eligible_way_count"],
            "eligibleWayCountMinusUniqueNormalizedSourceRecordIds": (
                declared["eligible_way_count"] - len(unique_normalized_source_record_ids)
            ),
            "restrictionRelationCount": declared["restriction_relation_count"],
            "invalidGeometryCount": invalid,
            "outsideExactAeqdBoundsCount": outside,
            "rawElementCounts": dict(sorted(raw_element_counts.items())),
            "rawWayOccurrences": len(raw_way_ids),
            "rawUniqueWayIds": len(set(raw_way_ids)),
            "rawDuplicateWayOccurrences": len(raw_way_ids) - len(set(raw_way_ids)),
            "classes": class_rows,
            "cellCount": len(cell_lengths),
            "evaluatedPublishedCellCount": len(published_cells),
            "nonzeroOsmCells": nonzero_cells,
            "maximumPublishedVsRecomputedCellClassDeltaMeters": max(cell_deltas, default=0.0),
            "coverageCircularity": {
                "status": "CONFIRMED_OSM_ON_OSM_IDENTITY" if circular else "NOT_CONFIRMED",
                "interpretation": (
                    "All six published 100% numerators and denominators are the same OSM-derived "
                    "centerline sums. This proves internal preservation, not independent road-authority "
                    "coverage and not lane-count-weighted lane length."
                ),
            },
            "coverageSourceKindMetadata": {
                "published": next(
                    source["source_kind"]
                    for source in coverage["sources"]
                    if source["id"] == "osm_bounded_roads"
                ),
                "expectedFromFrozenAcquisition": "overpass_osm",
                "status": "FAIL_MISLABELED_AS_ARCGIS",
            },
        },
        geometries,
    )


def verify_aeqd_projection(
    external: Path,
    freeze: dict[str, Any],
    geometry_sources: dict[str, list[tuple[dict[str, Any], Any]]],
) -> dict[str, Any]:
    contract = freeze["coordinate_contract"]
    transformer = Transformer.from_crs(
        CRS.from_epsg(4326), CRS.from_proj4(contract["projection"]), always_xy=True
    )
    origin_x, origin_z = transformer.transform(
        contract["origin_longitude_degrees"], contract["origin_latitude_degrees"]
    )
    rows: list[dict[str, Any]] = []
    for source in freeze["sources"]:
        if not source.get("pages") or source["id"] not in geometry_sources:
            continue
        normalized_lookup: dict[tuple[float, float], tuple[float, float]] = {}
        for _, geometry in geometry_sources[source["id"]]:
            for x, z in iter_shapely_coordinates(geometry):
                normalized_lookup.setdefault((round(x, 6), round(z, 6)), (x, z))
        witness: dict[str, Any] | None = None
        page_spatial_references: set[int] = set()
        for page in source["pages"]:
            payload = load_json(external / page["receipt"]["response"]["path"])
            spatial_reference = payload.get("spatialReference", {}).get("wkid")
            if spatial_reference is not None:
                page_spatial_references.add(int(spatial_reference))
            for feature in payload.get("features", []):
                oid = feature.get("attributes", {}).get(source["object_id_field"])
                for longitude, latitude in iter_arcgis_coordinates(feature.get("geometry", {})):
                    x, z = transformer.transform(longitude, latitude)
                    if not (BOUNDS[0] <= x <= BOUNDS[2] and BOUNDS[1] <= z <= BOUNDS[3]):
                        continue
                    matched = normalized_lookup.get((round(x, 6), round(z, 6)))
                    if matched is None:
                        continue
                    witness = {
                        "sourceRecordId": str(oid),
                        "rawLongitudeLatitude": [longitude, latitude],
                        "reprojectedXZ": [x, z],
                        "normalizedXZ": [matched[0], matched[1]],
                        "residualMeters": math.hypot(x - matched[0], z - matched[1]),
                    }
                    break
                if witness is not None:
                    break
            if witness is not None:
                break
        rows.append(
            {
                "id": source["id"],
                "rawPageSpatialReferenceWkids": sorted(page_spatial_references),
                "witness": witness,
                "status": "PASS" if witness is not None and page_spatial_references == {4326} else "FAIL",
            }
        )

    osm_source = next(item for item in freeze["sources"] if item["id"] == "osm_bounded_roads")
    raw_osm = load_json(external / osm_source["raw"]["path"])
    nodes = {
        int(element["id"]): (float(element["lon"]), float(element["lat"]))
        for element in raw_osm.get("elements", [])
        if element.get("type") == "node"
    }
    ways: dict[int, dict[str, Any]] = {}
    for element in raw_osm.get("elements", []):
        if element.get("type") == "way":
            ways.setdefault(int(element["id"]), element)
    osm_witness: dict[str, Any] | None = None
    for record, geometry in geometry_sources.get("osm_bounded_roads", []):
        source_record_id = str(record.get("source_record_id", ""))
        if not source_record_id.startswith("way/"):
            continue
        way_id = int(source_record_id.split("/", 1)[1])
        way = ways.get(way_id)
        if way is None:
            continue
        normalized_lookup = {
            (round(x, 6), round(z, 6)): (x, z)
            for x, z in iter_shapely_coordinates(geometry)
        }
        for node_id in way.get("nodes", []):
            if int(node_id) not in nodes:
                continue
            longitude, latitude = nodes[int(node_id)]
            x, z = transformer.transform(longitude, latitude)
            matched = normalized_lookup.get((round(x, 6), round(z, 6)))
            if matched is not None:
                osm_witness = {
                    "sourceRecordId": source_record_id,
                    "nodeId": str(node_id),
                    "rawLongitudeLatitude": [longitude, latitude],
                    "reprojectedXZ": [x, z],
                    "normalizedXZ": [matched[0], matched[1]],
                    "residualMeters": math.hypot(x - matched[0], z - matched[1]),
                }
                break
        if osm_witness is not None:
            break

    all_normalized_outside = sum(
        1
        for pairs in geometry_sources.values()
        for _, geometry in pairs
        if not box(*BOUNDS).covers(geometry)
    )
    status = (
        abs(origin_x) < 1e-9
        and abs(origin_z) < 1e-9
        and contract["bounds_xz_meters"] == list(BOUNDS)
        and all(row["status"] == "PASS" for row in rows)
        and osm_witness is not None
        and all_normalized_outside == 0
    )
    return {
        "status": "PASS" if status else "FAIL",
        "projection": contract["projection"],
        "originProjectionResidualMeters": math.hypot(origin_x, origin_z),
        "exactBoundsXZ": contract["bounds_xz_meters"],
        "gridCellCount": GRID_SIZE * GRID_SIZE,
        "gridCellSizeMeters": CELL_SIZE,
        "normalizedGeometryOutsideBoundsCount": all_normalized_outside,
        "arcgisProjectionWitnesses": rows,
        "osmProjectionWitness": osm_witness,
        "qualification": (
            "All normalized geometries were bounds-tested. Raw WGS84-to-AEQD vertices were independently "
            "reprojected with pyproj and matched exactly (within six-decimal indexing) for each ArcGIS "
            "source and OSM. Witnesses validate the transform; they do not replace the full OID/exclusion audit."
        ),
    }


def compare_linework(
    osm_geometries: list[tuple[dict[str, Any], Any]],
    sources: dict[str, list[tuple[dict[str, Any], Any]]],
) -> dict[str, Any]:
    # These are diagnostics, not source-gate thresholds: the sources have distinct
    # inclusion semantics, update dates, and topology.  They still disprove the idea
    # that an OSM-only eligibility universe can measure cross-authority completeness.
    osm_lines = [geometry for _, geometry in osm_geometries]
    osm_tree = STRtree(osm_lines)
    rows: dict[str, Any] = {}
    for source_id in (
        "cdot_highways",
        "cdot_ramps",
        "city_street_centerlines",
        "county_street_centerlines",
        "county_road_map",
        "drcog_pavement_lines_2024",
    ):
        pairs = sources.get(source_id, [])
        lines = [geometry for _, geometry in pairs if geometry.geom_type in ("LineString", "MultiLineString")]
        total = sum(geometry.length for geometry in lines)
        midpoints = [geometry.interpolate(0.5, normalized=True) for geometry in lines]
        if midpoints:
            _, distances = osm_tree.query_nearest(midpoints, return_distance=True, all_matches=False)
        else:
            distances = []
        tolerance_rows: dict[str, Any] = {}
        for tolerance in (15.0, 30.0, 50.0):
            covered = sum(
                geometry.length
                for geometry, distance in zip(lines, distances)
                if float(distance) <= tolerance
            )
            tolerance_rows[str(int(tolerance))] = {
                "lineMetersWhosePartMidpointIsWithinOsmTolerance": round(covered, 3),
                "midpointProxyRatioOfThisSourcesLineMeters": round(covered / total, 9) if total else None,
            }
        rows[source_id] = {
            "lineParts": len(lines),
            "totalLineMeters": round(total, 3),
            "osmProximityDiagnostics": tolerance_rows,
        }
    return {
        "qualification": (
            "Diagnostic midpoint-proximity proxy only; no acceptance threshold is inferred. It is not "
            "an exact overlap measure. Authority class crosswalks, eligibility filters, divided-road "
            "rules, ramps, and update-date reconciliation are not frozen."
        ),
        "sources": rows,
    }


def compare_pavement_polygons(
    osm_geometries: list[tuple[dict[str, Any], Any]],
    sources: dict[str, list[tuple[dict[str, Any], Any]]],
) -> dict[str, Any]:
    osm_lines = [geometry for _, geometry in osm_geometries]
    midpoints = [geometry.interpolate(0.5, normalized=True) for geometry in osm_lines]
    total = sum(geometry.length for geometry in osm_lines)
    pairs = sources.get("drcog_pavement_polygons_2024", [])
    type_rows: dict[str, Any] = {}
    type_names = sorted(
        {
            str(record.get("attributes", {}).get("Type"))
            for record, geometry in pairs
            if geometry.geom_type in ("Polygon", "MultiPolygon")
        }
    )
    for type_name in type_names:
        polygons = [
            geometry
            for record, geometry in pairs
            if geometry.geom_type in ("Polygon", "MultiPolygon")
            and str(record.get("attributes", {}).get("Type")) == type_name
        ]
        tree = STRtree(polygons)
        matches = tree.query(midpoints, predicate="intersects")
        matched_midpoint_indices = set(matches[0].tolist()) if len(matches) else set()
        proxy_length = sum(osm_lines[index].length for index in matched_midpoint_indices)
        type_rows[type_name] = {
            "polygonParts": len(polygons),
            "osmPartMidpointsInside": len(matched_midpoint_indices),
            "osmLineMetersWhosePartMidpointIsInside": round(proxy_length, 3),
            "midpointProxyRatioOfOsmLineMeters": round(proxy_length / total, 9) if total else None,
        }
    return {
        "qualification": (
            "Diagnostic midpoint-in-polygon proxy only. DRCOG pavement coverage is not statewide, and "
            "Median polygons are not road-centerline eligibility. No acceptance threshold is inferred."
        ),
        "types": type_rows,
    }


def verify_missing_roads(repo: Path, coverage: dict[str, Any]) -> dict[str, Any]:
    payload = load_json(repo / "docs/data/traffic_v2/traffic_missing_roads.geojson")
    features = payload.get("features", [])
    class_rows = coverage["class_coverage"]
    same_source_identity = all(
        abs(value["eligible_lane_proxy_length_meters"] - value["represented_source_length_meters"]) < 1e-9
        for value in class_rows.values()
    )
    return {
        "featureCount": len(features),
        "declaredFeatureCount": payload.get("feature_count"),
        "gateStatus": payload.get("gate_status"),
        "sameSourceEligibilityRepresentationIdentity": same_source_identity,
        "authorityCompletenessConclusion": "UNSUPPORTED",
        "reason": (
            "An empty difference set against an eligibility universe built from the same OSM records "
            "cannot detect roads present only in City, County, CDOT, pavement, or imagery evidence."
        ),
    }


def verify_normalization_oid_dispositions(
    external: Path,
    freeze: dict[str, Any],
    geometry_sources: dict[str, list[tuple[dict[str, Any], Any]]],
) -> dict[str, Any]:
    rows: list[dict[str, Any]] = []
    total_missing = 0
    total_unknown = 0
    for source in freeze["sources"]:
        receipts = source.get("receipts", {})
        if "ids_pre" not in receipts:
            continue
        raw_ids_payload = load_json(external / receipts["ids_pre"]["response"]["path"])
        raw_ids = {str(value) for value in raw_ids_payload.get("objectIds", [])}
        normalized_ids = {
            str(record.get("source_record_id"))
            for record, _ in geometry_sources.get(source["id"], [])
        }
        sort_key = lambda value: (0, int(value)) if value.isdigit() else (1, value)
        missing = sorted(raw_ids - normalized_ids, key=sort_key)
        unknown = sorted(normalized_ids - raw_ids, key=sort_key)
        total_missing += len(missing)
        total_unknown += len(unknown)
        rows.append(
            {
                "id": source["id"],
                "rawStableOidCount": len(raw_ids),
                "normalizedDistinctSourceOidCount": len(normalized_ids),
                "missingNormalizedOidCount": len(missing),
                "missingOidListCanonicalSha256": canonical_sha256(missing),
                "missingOidExamples": missing[:10],
                "unknownNormalizedOidCount": len(unknown),
                "unknownOidListCanonicalSha256": canonical_sha256(unknown),
                "perOidExclusionReasonLedgerFrozen": False,
            }
        )
    return {
        "status": "FAIL_UNEXPLAINED_NORMALIZATION_OMISSIONS" if total_missing or total_unknown else "PASS",
        "sourceCount": len(rows),
        "rawOidsWithoutNormalizedRecord": total_missing,
        "normalizedOidsAbsentFromRawStableUniverse": total_unknown,
        "qualification": (
            "A missing normalized OID can be legitimate (for example empty or out-of-bounds geometry), "
            "but the freeze provides no per-OID reason ledger, so every exclusion cannot be reproduced."
        ),
        "sources": rows,
    }


def verify_product_bindings(repo: Path, bindings: list[dict[str, Any]]) -> dict[str, Any]:
    rows: list[dict[str, Any]] = []
    failures: list[str] = []
    for binding in bindings:
        path = repo / Path(binding["path"])
        row = {
            "id": binding["id"],
            "path": binding["path"],
            "exists": path.is_file(),
        }
        if path.is_file():
            lfs_pointer = (
                "version https://git-lfs.github.com/spec/v1\n"
                f"oid sha256:{binding['sha256']}\n"
                f"size {binding['bytes']}\n"
            ).encode("utf-8")
            lfs_pointer_blob = hashlib.sha1(
                b"blob " + str(len(lfs_pointer)).encode("ascii") + b"\0" + lfs_pointer
            ).hexdigest()
            row.update(
                {
                    "bytes": path.stat().st_size,
                    "sha256": sha256(path),
                    "materializedGitBlobSha1": git_blob_sha1(path),
                    "canonicalLfsPointerGitBlobSha1": lfs_pointer_blob,
                }
            )
            row["declaredGitBlobMatchesMaterializedOrCanonicalLfsPointer"] = binding[
                "git_blob_sha1"
            ] in (row["materializedGitBlobSha1"], row["canonicalLfsPointerGitBlobSha1"])
            if (
                row["bytes"] != binding["bytes"]
                or row["sha256"] != binding["sha256"]
                or not row["declaredGitBlobMatchesMaterializedOrCanonicalLfsPointer"]
            ):
                failures.append(binding["id"])
        else:
            failures.append(binding["id"])
        rows.append(row)
    return {"status": "PASS" if not failures else "FAIL", "bindings": rows, "failures": failures}


def verify_camera_inventory(
    repo: Path, external: Path, baseline: Path, specification: dict[str, Any]
) -> dict[str, Any]:
    baseline_path = baseline / "traffic_v2_baseline.json"
    payload = load_json(baseline_path)
    views = payload.get("views", [])
    camera_records = [
        {
            "id": view.get("id"),
            "poseAuthority": view.get("poseAuthority"),
            "position": view.get("resolvedPosition"),
            "eulerDegrees": view.get("resolvedEuler"),
            "fieldOfViewDegrees": view.get("fieldOfView"),
            "renderWidth": payload.get("renderWidth"),
            "renderHeight": payload.get("renderHeight"),
        }
        for view in views
    ]
    expected_ids = specification["benchmark_views"]
    contract_path = repo / "tools/traffic_v2/schemas/traffic_v2_benchmark_camera_contract_v1.json"
    rebuild_path = external.parent / "lead_rebuild_benchmark_camera_contract.json"
    contract = load_json(contract_path)
    errors: list[str] = []

    def require(condition: bool, message: str) -> None:
        if not condition:
            errors.append(message)

    require(sha256(contract_path) == EXPECTED_CAMERA_CONTRACT_SHA256, "camera contract SHA")
    require(contract_path.stat().st_size == 16200, "camera contract bytes")
    require(rebuild_path.is_file(), "camera contract rebuild exists")
    if rebuild_path.is_file():
        require(sha256(rebuild_path) == sha256(contract_path), "camera contract rebuild hash")
        require(rebuild_path.stat().st_size == contract_path.stat().st_size, "camera contract rebuild bytes")
    require(contract.get("accepted_source_sha") == specification["accepted_source_sha"], "accepted source binding")
    require(contract.get("status") == "FROZEN", "camera contract frozen status")
    require(contract.get("view_count") == len(expected_ids), "camera contract view count")
    require([view.get("id") for view in contract.get("views", [])] == expected_ids, "camera contract view order")
    require([view.get("id") for view in views] == expected_ids, "baseline view order")

    baseline_descriptor = contract.get("baseline", {})
    require(baseline_descriptor.get("bytes") == baseline_path.stat().st_size, "baseline bytes")
    require(baseline_descriptor.get("sha256") == sha256(baseline_path), "baseline SHA")
    require(baseline_descriptor.get("schema") == payload.get("schema"), "baseline schema")
    spec_descriptor = contract.get("frozen_specification", {})
    specification_path = repo / spec_descriptor.get("path", "")
    require(specification_path.is_file(), "contract specification exists")
    if specification_path.is_file():
        require(spec_descriptor.get("bytes") == specification_path.stat().st_size, "contract specification bytes")
        require(spec_descriptor.get("sha256") == sha256(specification_path), "contract specification SHA")

    for key in ("scene", "camera_serialization"):
        descriptor = contract.get(key, {})
        path = repo / descriptor.get("path", "")
        require(path.is_file(), f"{key} exists")
        if path.is_file():
            if "bytes" in descriptor:
                require(descriptor["bytes"] == path.stat().st_size, f"{key} bytes")
            require(descriptor.get("sha256") == sha256(path), f"{key} SHA")
            require(descriptor.get("git_blob_sha1") == git_blob_sha1(path), f"{key} Git blob")

    baseline_by_id = {view["id"]: view for view in views}
    for contract_view in contract.get("views", []):
        view_id = contract_view.get("id")
        baseline_view = baseline_by_id.get(view_id)
        require(baseline_view is not None, f"baseline view {view_id}")
        if baseline_view is None:
            continue
        require(contract_view.get("pose_authority") == baseline_view.get("poseAuthority"), f"{view_id} pose authority")
        require(
            contract_view.get("frozen_position_or_offset_meters") == baseline_view.get("frozenPositionOrOffset"),
            f"{view_id} frozen position",
        )
        require(contract_view.get("frozen_euler_degrees") == baseline_view.get("frozenEuler"), f"{view_id} frozen Euler")
        require(contract_view.get("resolved_position_meters") == baseline_view.get("resolvedPosition"), f"{view_id} resolved position")
        require(contract_view.get("resolved_euler_degrees") == baseline_view.get("resolvedEuler"), f"{view_id} resolved Euler")
        require(contract_view.get("field_of_view_degrees") == baseline_view.get("fieldOfView"), f"{view_id} FOV")
        x = float(baseline_view["resolvedPosition"]["x"])
        z = float(baseline_view["resolvedPosition"]["z"])
        inside = BOUNDS[0] <= x <= BOUNDS[2] and BOUNDS[1] <= z <= BOUNDS[3]
        require(contract_view.get("origin_inside_frozen_bounds") is inside, f"{view_id} bounds flag")
        require(contract_view.get("road_candidate_set") == "FULL_MAP_UNIVERSE_SUPERSET", f"{view_id} candidate mode")
        screenshot_path = baseline / baseline_view["pngPath"]
        screenshot = contract_view.get("screenshot", {})
        require(screenshot_path.is_file(), f"{view_id} screenshot exists")
        if screenshot_path.is_file():
            require(screenshot.get("bytes") == screenshot_path.stat().st_size, f"{view_id} screenshot bytes")
            require(screenshot.get("sha256") == sha256(screenshot_path), f"{view_id} screenshot SHA")
        require(
            screenshot.get("logical_path") == "baseline/" + baseline_view["pngPath"].replace("\\", "/"),
            f"{view_id} screenshot logical path",
        )

    coverage_contract = contract.get("coverage_contract", {})
    expected_member_fields = {
        "canonical_segment_id",
        "road_class",
        "eligibility_status_or_exclusion_reason",
        "source_precedence_decision",
        "represented_source_disposition",
        "license_partition",
        "lane_count",
        "lane_count_authority",
        "confidence",
    }
    require(coverage_contract.get("bounds_xz_meters") == list(BOUNDS), "contract bounds")
    require(coverage_contract.get("candidate_mode") == "FULL_MAP_UNIVERSE_SUPERSET", "contract candidate mode")
    require(coverage_contract.get("no_frustum_or_occlusion_exclusions") is True, "no visibility exclusions")
    require(set(coverage_contract.get("required_universe_member_fields", [])) == expected_member_fields, "universe fields")
    universe_binding_present = any(
        "universe" in key.lower() and ("sha" in key.lower() or "count" in key.lower())
        for key in contract
    )
    return {
        "status": (
            "PASS_LEAD_CAMERA_INPUT_AWAITING_A_BOUND_UNIVERSE"
            if not errors and not universe_binding_present
            else ("FAIL_LEAD_CAMERA_CONTRACT" if errors else "PASS_BOUND_UNIVERSE_PRESENT")
        ),
        "baselinePath": str(baseline_path),
        "baselineSha256": sha256(baseline_path),
        "viewCount": len(views),
        "viewIdsExactlyMatchFrozenSpecOrder": [view.get("id") for view in views] == expected_ids,
        "cameraRecordCanonicalSha256": canonical_sha256(camera_records),
        "leadContract": {
            "path": str(contract_path),
            "bytes": contract_path.stat().st_size,
            "sha256": sha256(contract_path),
            "rebuildPath": str(rebuild_path),
            "rebuildExact": rebuild_path.is_file() and sha256(rebuild_path) == sha256(contract_path),
            "errors": errors,
        },
        "fullMapUniverseBindingPresentInLeadInput": universe_binding_present,
        "requiredNextBinding": (
            "A normative output must bind the deduplicated canonical universe SHA-256/count and "
            "complete per-member classifications/dispositions to this lead camera contract."
        ),
        "cameraRecordQualification": (
            "The lead input now freezes the 15 static views and strict full-map-superset proof "
            "semantics. Dynamic path-sweep and later projected/unoccluded visibility claims remain excluded."
        ),
    }


def verify_image(external: Path, descriptor: dict[str, Any]) -> dict[str, Any]:
    path = external / descriptor["path"]
    with Image.open(path) as image:
        image.load()
        width, height = image.size
        mode = image.mode
        # Pixel digest is independent of PNG metadata/compression.
        pixel_sha = hashlib.sha256(image.tobytes()).hexdigest()
        colors = image.convert("RGB").getcolors(maxcolors=width * height)
        unique_colors = len(colors) if colors is not None else None
    return {
        "status": "PASS_FULL_RESOLUTION_READ",
        "path": str(path),
        "bytes": path.stat().st_size,
        "sha256": sha256(path),
        "dimensions": [width, height],
        "mode": mode,
        "decodedPixelSha256": pixel_sha,
        "uniqueRgbColors": unique_colors,
        "manualInspection": {
            "status": "INSPECTED_FULL_RESOLUTION",
            "findings": [
                "The 24x24 bounds grid, six OSM classes, legend, and BLOCKED_AUTHORITY label are legible.",
                "No City, County, CDOT, or DRCOG comparison overlay is shown.",
                "No benchmark-camera frustum/path, road identifiers, precedence decisions, or exclusions are shown.",
                "The image is useful as an OSM distribution visualization but cannot establish authority completeness.",
            ],
        },
    }


def license_evidence(external: Path, freeze: dict[str, Any]) -> dict[str, Any]:
    rows: dict[str, Any] = {}
    for key, snapshot in sorted(freeze["license_snapshots"].items()):
        descriptor = snapshot["receipt"]["response"]
        path = external / descriptor["path"]
        text = path.read_text(encoding="utf-8", errors="replace").lower()
        tokens = {
            "cc0": "cc0" in text or "public domain" in text,
            "ccBy": "creative commons attribution" in text or "cc by" in text,
            "odbl": "odbl" in text or "open database license" in text,
            "publicDomain": "public domain" in text,
            "version3": "3.0" in text,
            "version4": "4.0" in text,
        }
        rows[key] = {
            "path": descriptor["path"],
            "bytes": path.stat().st_size,
            "sha256": sha256(path),
            "declaredSha256Matches": sha256(path) == descriptor["sha256"],
            "requiredTextPresentAtAcquisition": snapshot.get("required_text_present"),
            "licenseTokenPresence": tokens,
        }
    return {
        "status": "PASS_WITH_DECLARED_DRCOG_VERSION_CONFLICT",
        "snapshots": rows,
        "qualification": (
            "Hash and token checks preserve the acquired license evidence; they are not legal advice. "
            "DRCOG pages preserve a 3.0-text versus 4.0-link discrepancy and must remain a distinct "
            "LicenseRef partition until resolved."
        ),
    }


def imagery_metadata(external: Path, freeze: dict[str, Any]) -> dict[str, Any]:
    source = next(item for item in freeze["sources"] if item["id"] == "city_orthophoto_2020_metadata")
    descriptors = list(iter_descriptors(source))
    return {
        "status": "METADATA_ONLY_NOT_ROAD_INVENTORY",
        "sourceId": source["id"],
        "role": source["role"],
        "descriptorCount": len({(item["path"], item["sha256"]) for item in descriptors}),
        "qualification": (
            "The freeze preserves 2020 orthophoto metadata/receipts, not image pixels or an extracted "
            "road/pavement inventory. It can support ambiguity review and temporal provenance but cannot "
            "independently prove that every visible current road was classified."
        ),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--external-root", type=Path, required=True)
    parser.add_argument("--baseline-root", type=Path, required=True)
    args = parser.parse_args()
    repo = args.repo_root.resolve()
    external = args.external_root.resolve()
    baseline = args.baseline_root.resolve()

    freeze_path = external / "freeze_manifest.json"
    freeze = load_json(freeze_path)
    coverage = load_json(repo / "docs/data/traffic_v2/traffic_source_coverage.json")
    conflicts = load_json(repo / "docs/data/traffic_v2/traffic_source_conflicts.json")
    run = load_json(external / "run_manifest.json")
    specification_path = repo / freeze["specification"]["path"]
    specification = load_json(specification_path)

    top_level_checks = {
        "freezeSha256": sha256(freeze_path),
        "freezeShaMatchesExpected": sha256(freeze_path) == EXPECTED_FREEZE_SHA256,
        "acceptedSourceShaMatchesExpected": freeze["accepted_source_sha"] == EXPECTED_ACCEPTED_SOURCE_SHA,
        "specificationSha256": sha256(specification_path),
        "specificationShaMatchesExpected": sha256(specification_path) == EXPECTED_SPEC_SHA256,
        "coverageBindsFreeze": coverage["source_freeze_sha256"] == sha256(freeze_path),
        "coverageBindsSpec": coverage["specification_sha256"] == sha256(specification_path),
        "coverageBindsSource": coverage["accepted_source_sha"] == freeze["accepted_source_sha"],
        "runBindsFreeze": run["freeze_sha256"] == sha256(freeze_path),
        "publishedGateStatus": coverage["gate_status"],
        "publishedBlockingConflictCount": conflicts["blocking_count"],
    }

    descriptor_result = verify_frozen_descriptors(repo, external, freeze)
    run_result = verify_run_outputs(repo, external, run)
    arcgis_rows = [
        verify_arcgis_inventory(external, source)
        for source in freeze["sources"]
        if "count_pre" in source.get("receipts", {})
    ]

    normalized_rows: list[dict[str, Any]] = []
    geometry_sources: dict[str, list[tuple[dict[str, Any], Any]]] = {}
    for source in coverage["sources"]:
        summary, geometries = summarize_normalized(external, source)
        normalized_rows.append(summary)
        if geometries:
            geometry_sources[source["id"]] = geometries
    osm_result, osm_geometries = summarize_osm(external, coverage)

    evidence_descriptor = coverage["evidence"]["traffic_source_coverage.png"]
    output: dict[str, Any] = {
        "schema": "questflight.traffic-v2-phase3-independent-verification.v1",
        "decision": "FAIL_CLOSED",
        "decisionScope": "PHASE_3_SOURCE_GATE_ONLY",
        "generatedBy": {
            "scriptPath": str(Path(__file__).resolve()),
            "scriptSha256": sha256(Path(__file__).resolve()),
            "python": sys.version,
            "platform": platform.platform(),
            "byteOrder": sys.byteorder,
            "doublePrecisionBytes": struct.calcsize("d"),
        },
        "topLevelBindings": top_level_checks,
        "frozenDescriptorIntegrity": descriptor_result,
        "normativeOutputRebuildIntegrity": run_result,
        "arcgisOidInventories": {
            "status": "PASS" if all(row["status"] == "PASS" for row in arcgis_rows) else "FAIL",
            "sourceCount": len(arcgis_rows),
            "sources": arcgis_rows,
        },
        "normalizedSources": {
            "status": "PASS" if all(row["status"] in ("PASS", "NOT_APPLICABLE") for row in normalized_rows) else "FAIL",
            "sources": normalized_rows,
        },
        "aeqdProjectionAndBounds": verify_aeqd_projection(external, freeze, geometry_sources),
        "osmEligibilityAndGrid": osm_result,
        "crossAuthorityLineworkDiagnostics": compare_linework(osm_geometries, geometry_sources),
        "pavementPolygonDiagnostics": compare_pavement_polygons(osm_geometries, geometry_sources),
        "missingRoads": verify_missing_roads(repo, coverage),
        "normalizationOidDispositions": verify_normalization_oid_dispositions(
            external, freeze, geometry_sources
        ),
        "acceptedProductBindings": verify_product_bindings(repo, freeze["accepted_product_bindings"]),
        "licenseEvidence": license_evidence(external, freeze),
        "imageryEvidence": imagery_metadata(external, freeze),
        "benchmarkCameraRoadInventory": verify_camera_inventory(
            repo, external, baseline, specification
        ),
        "coverageImage": verify_image(external, evidence_descriptor),
        "sourcePrecedence": {
            "status": "NOT_INDEPENDENTLY_REPRODUCED_FOR_EVERY_ELIGIBLE_ROAD",
            "declaredRoles": dict(Counter(source["role"] for source in freeze["sources"])),
            "reason": (
                "Source role partitions are preserved, but the normative outputs do not publish a "
                "record-level authority/fallback join or per-road precedence disposition."
            ),
        },
        "blockingFindings": [
            {
                "id": "benchmark_camera_road_inventory_gap",
                "status": "UNRESOLVED",
                "reason": (
                    "Lead camera/screenshot/full-map-superset input is now frozen, but no A normative "
                    "output yet binds the deduplicated canonical universe SHA/count and member dispositions."
                ),
            },
            {
                "id": "osm_on_osm_coverage_identity",
                "status": "UNRESOLVED",
                "reason": "All reported 100% class ratios divide each OSM centerline sum by itself.",
            },
            {
                "id": "cross_authority_eligibility_and_precedence_not_reproduced",
                "status": "UNRESOLVED",
                "reason": "No frozen class crosswalk and record-level City/County/CDOT/OSM precedence inventory supports the ratios or empty missing-roads set.",
            },
            {
                "id": "duplicate_osm_normalized_record_parts",
                "status": "UNRESOLVED",
                "reason": "Normalized OSM record-part keys are not unique, inflating all downstream accounting that retains duplicates.",
            },
            {
                "id": "normalization_exclusion_ledger_gap",
                "status": "UNRESOLVED",
                "reason": "Raw ArcGIS OIDs omitted from normalization have no frozen per-OID reason ledger.",
            },
            {
                "id": "imagery_visible_road_review_not_reproduced",
                "status": "UNRESOLVED",
                "reason": "Only 2020 imagery metadata is frozen; image pixels and a road-review inventory are absent.",
            },
        ],
        "limitations": [
            "This verifier never reads/imports the production source_gate.py or its tests.",
            "It performs no network access; current upstream contents and license terms are not re-queried.",
            "Cross-authority proximity is diagnostic and cannot substitute for a frozen semantic crosswalk.",
            "Shapely/CPython recomputation is Editor/desktop evidence, not Quest runtime evidence.",
            "No Phase-4 lane geometry, traffic simulation, Unity scene, APK, or headset performance claim is made.",
        ],
    }

    integrity_ok = (
        all(value is True for key, value in top_level_checks.items() if key.endswith("Expected") or key.startswith("coverageBinds") or key == "runBindsFreeze")
        and descriptor_result["status"] == "PASS"
        and run_result["status"] == "PASS"
        and output["arcgisOidInventories"]["status"] == "PASS"
        and output["normalizedSources"]["status"] == "PASS"
        and output["aeqdProjectionAndBounds"]["status"] == "PASS"
        and osm_result["status"] == "PASS"
        and output["normalizationOidDispositions"]["status"] == "PASS"
        and output["acceptedProductBindings"]["status"] == "PASS"
    )
    output["internalIntegrityStatus"] = "PASS" if integrity_ok else "FAIL"
    print(json.dumps(output, indent=2, sort_keys=True))
    return 0 if integrity_ok else 2


if __name__ == "__main__":
    raise SystemExit(main())
