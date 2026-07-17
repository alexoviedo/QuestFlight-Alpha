#!/usr/bin/env python3
"""Fail-closed QA for authored Quest environment reconstruction reports.

The validator deliberately consumes JSON evidence rather than Unity objects. Unity
authoring and capture tools can emit the seven reports independently; this gate
then applies one stable acceptance policy without constructing or modifying the
product scene.
"""

from __future__ import annotations

import argparse
import collections
import datetime as dt
import json
import math
import pathlib
import re
import statistics
import sys
from dataclasses import dataclass, field
from typing import Any, Callable, Iterable


SCHEMA_VERSION = 1
MIB = 1024 * 1024
LIMITS = {
    "buildingMedianFoundationErrorMeters": 0.03,
    "buildingP95FoundationErrorMeters": 0.08,
    "buildingMaximumFoundationErrorMeters": 0.15,
    "treeRootMinimumMeters": -0.12,
    "treeRootMaximumMeters": 0.03,
    "parkedAircraftGearContactErrorMeters": 0.03,
    "parkedAircraftHeadingErrorDegrees": 3.0,
    "airportPropGroundContactErrorMeters": 0.05,
    "runwayTerrainMaximumGapMeters": 0.025,
    "markingRunwayMaximumGapMeters": 0.003,
    "runwayColliderMaximumDisagreementMeters": 0.0001,
    "nearBuildingCoverageRatio": 1.0,
    "extendedNearBuildingCoverageRatio": 0.95,
    "midSignificantBuildingCoverageRatio": 0.90,
    "nearWaterPolygonMinimumAreaSquareMetersExclusive": 500.0,
    "nearWaterwayMinimumWidthMeters": 3.0,
    "midWaterPolygonMinimumAreaSquareMetersExclusive": 5000.0,
    "visibleDrawCalls": 130,
    "visibleTriangles": 650_000,
    "visibleMaterials": 30,
    "runtimeTextureMemoryBytes": 220 * MIB,
    "transparentMaterialSlots": 3,
    "msaaSamples": 4,
    "steadyStateManagedAllocations": 0,
}

EXPECTED_REPORT_TYPES = {
    "surface": "production_terrain_surface",
    "placement": "production_placement",
    "building": "building_coverage",
    "vegetation": "vegetation_coverage",
    "water": "water_coverage",
    "hlod": "environment_hlod",
    "budgets": "quest_environment_budgets",
}


@dataclass(frozen=True)
class Issue:
    category: str
    code: str
    message: str
    path: str = ""

    def as_dict(self) -> dict[str, str]:
        return {
            "category": self.category,
            "code": self.code,
            "message": self.message,
            "path": self.path,
        }


@dataclass
class CategoryResult:
    category: str
    source_path: str
    issues: list[Issue] = field(default_factory=list)
    metrics: dict[str, Any] = field(default_factory=dict)

    @property
    def passed(self) -> bool:
        return not self.issues

    def fail(self, code: str, message: str, path: str = "") -> None:
        self.issues.append(Issue(self.category, code, message, path))


class ReportReader:
    """Typed access that records every absent or malformed value as a failure."""

    def __init__(self, result: CategoryResult, data: Any):
        self.result = result
        self.data = data if isinstance(data, dict) else {}
        if not isinstance(data, dict):
            result.fail("INVALID_ROOT", "Report root must be a JSON object.")

    def required(self, key: str, expected: type | tuple[type, ...], path: str = "") -> Any:
        parent = self.data
        full = f"{path}.{key}" if path else key
        if key not in parent:
            self.result.fail("MISSING_FIELD", f"Required field '{full}' is missing.", full)
            return None
        value = parent[key]
        if expected is int and isinstance(value, bool):
            valid = False
        elif expected in ((int, float), (float, int)) and isinstance(value, bool):
            valid = False
        else:
            valid = isinstance(value, expected)
        if not valid:
            self.result.fail(
                "INVALID_FIELD_TYPE",
                f"Field '{full}' must be {type_name(expected)}, got {type(value).__name__}.",
                full,
            )
            return None
        return value


def type_name(expected: type | tuple[type, ...]) -> str:
    if isinstance(expected, tuple):
        return " or ".join(item.__name__ for item in expected)
    return expected.__name__


def finite_number(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(float(value))


def require_number(
    result: CategoryResult,
    data: dict[str, Any],
    key: str,
    path: str,
) -> float | None:
    full = f"{path}.{key}" if path else key
    if key not in data:
        result.fail("MISSING_FIELD", f"Required field '{full}' is missing.", full)
        return None
    value = data[key]
    if not finite_number(value):
        result.fail("INVALID_NUMBER", f"Field '{full}' must be a finite number.", full)
        return None
    return float(value)


def require_bool(
    result: CategoryResult,
    data: dict[str, Any],
    key: str,
    path: str,
) -> bool | None:
    full = f"{path}.{key}" if path else key
    if key not in data:
        result.fail("MISSING_FIELD", f"Required field '{full}' is missing.", full)
        return None
    if not isinstance(data[key], bool):
        result.fail("INVALID_FIELD_TYPE", f"Field '{full}' must be bool.", full)
        return None
    return data[key]


def require_nonempty_string(
    result: CategoryResult,
    data: dict[str, Any],
    key: str,
    path: str,
) -> str | None:
    full = f"{path}.{key}" if path else key
    value = data.get(key)
    if not isinstance(value, str) or not value.strip():
        result.fail("MISSING_OR_EMPTY_STRING", f"Field '{full}' must be a nonempty string.", full)
        return None
    return value


def validate_header(result: CategoryResult, data: dict[str, Any], category: str) -> None:
    if data.get("schemaVersion") != SCHEMA_VERSION:
        result.fail(
            "SCHEMA_VERSION",
            f"schemaVersion must equal {SCHEMA_VERSION}; got {data.get('schemaVersion')!r}.",
            "schemaVersion",
        )
    expected = EXPECTED_REPORT_TYPES[category]
    if data.get("reportType") != expected:
        result.fail(
            "REPORT_TYPE",
            f"reportType must equal '{expected}'; got {data.get('reportType')!r}.",
            "reportType",
        )
    require_nonempty_string(result, data, "environmentBuildId", "")


def validate_evidence_completeness(result: CategoryResult, data: dict[str, Any]) -> None:
    """Surface adapter blockers without turning unknown evidence into passing values."""
    # Native producers predate the adapter metadata. When an adapter declares
    # completeness, validate it strictly; otherwise the category contract below
    # remains the fail-closed authority.
    if "evidenceComplete" not in data and "evidenceBlockers" not in data:
        return
    complete = require_bool(result, data, "evidenceComplete", "")
    blockers = data.get("evidenceBlockers")
    if not isinstance(blockers, list):
        result.fail("MISSING_EVIDENCE_BLOCKERS", "evidenceBlockers must be an array.", "evidenceBlockers")
        return
    if complete is False:
        result.fail("EVIDENCE_INCOMPLETE", "The report declares its acceptance evidence incomplete.", "evidenceComplete")
    for index, blocker in enumerate(blockers):
        path = f"evidenceBlockers[{index}]"
        if not isinstance(blocker, dict):
            result.fail("INVALID_EVIDENCE_BLOCKER", "Each evidence blocker must be an object.", path)
            continue
        code = blocker.get("code")
        message = blocker.get("message")
        if not isinstance(code, str) or re.fullmatch(r"[A-Z][A-Z0-9_]*", code) is None:
            result.fail("INVALID_EVIDENCE_BLOCKER", "Blocker code must be upper snake case.", f"{path}.code")
            continue
        if not isinstance(message, str) or not message.strip():
            result.fail("INVALID_EVIDENCE_BLOCKER", "Blocker message must be nonempty.", f"{path}.message")
            continue
        result.fail(code, message.strip(), path)


def load_json(path: pathlib.Path, category: str) -> tuple[CategoryResult, dict[str, Any] | None]:
    result = CategoryResult(category=category, source_path=str(path))
    if not path.is_file():
        result.fail("MISSING_REPORT", f"Required {category} report does not exist: {path}")
        return result, None
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        result.fail("UNREADABLE_REPORT", f"Could not read valid JSON: {exc}")
        return result, None
    if not isinstance(data, dict):
        result.fail("INVALID_ROOT", "Report root must be a JSON object.")
        return result, None
    validate_header(result, data, category)
    validate_evidence_completeness(result, data)
    return result, data


def validate_surface(result: CategoryResult, data: dict[str, Any]) -> None:
    manifest_sha = require_nonempty_string(result, data, "manifestSha256", "")
    if manifest_sha is not None and re.fullmatch(r"[0-9a-fA-F]{64}", manifest_sha) is None:
        result.fail("SURFACE_MANIFEST_HASH", "manifestSha256 must be a 64-digit SHA-256 value.", "manifestSha256")
    required_true = (
        "authoritative",
        "enuOriginAligned",
        "fixedRuntimeMesh",
        "stableNormalsBakedOffline",
        "seamValidationPassed",
    )
    for key in required_true:
        value = require_bool(result, data, key, "")
        if value is False:
            result.fail("SURFACE_CONTRACT", f"'{key}' must be true.", key)
    morphing = require_bool(result, data, "runtimeCameraDependentMorphing", "")
    if morphing is True:
        result.fail("CAMERA_DEPENDENT_TERRAIN", "Runtime camera-dependent terrain morphing is forbidden.", "runtimeCameraDependentMorphing")
    broken = require_number(result, data, "brokenSeamCount", "")
    if broken is not None and broken != 0:
        result.fail("BROKEN_TERRAIN_SEAM", f"brokenSeamCount must be zero; got {broken:g}.", "brokenSeamCount")
    coordinate_system = require_nonempty_string(result, data, "coordinateSystem", "")
    if coordinate_system is not None and coordinate_system.lower().replace(" ", "_") not in {"local_enu", "enu"}:
        result.fail("COORDINATE_SYSTEM", "coordinateSystem must identify the single local ENU frame.", "coordinateSystem")
    positive_evidence = (
        "sourceResolutionMeters",
        "nearCoverageWidthMeters",
        "nearCoverageHeightMeters",
        "runtimeVertexCount",
        "runtimeTriangleCount",
        "normalMapResolutionPixels",
    )
    for key in positive_evidence:
        value = require_number(result, data, key, "")
        if value is not None and value <= 0:
            result.fail("INVALID_SURFACE_EVIDENCE", f"'{key}' must be positive.", key)
    for key in ("maximumGeometricErrorMeters", "averageGeometricErrorMeters", "airportZoneErrorMeters"):
        value = require_number(result, data, key, "")
        if value is not None and value < 0:
            result.fail("INVALID_SURFACE_EVIDENCE", f"'{key}' must be nonnegative.", key)
    for key in ("nearCoverageWidthMeters", "nearCoverageHeightMeters"):
        value = data.get(key)
        if finite_number(value) and float(value) < 4000:
            result.fail("NEAR_TERRAIN_COVERAGE", f"'{key}' must cover at least 4,000 m.", key)
    sampler = data.get("samplerCapabilities")
    if not isinstance(sampler, dict):
        result.fail("MISSING_SAMPLER_CAPABILITIES", "samplerCapabilities must be an object.", "samplerCapabilities")
    else:
        for key in (
            "height",
            "normal",
            "terrainZone",
            "macroImageryUv",
            "landCover",
            "runwayApron",
            "water",
            "bestFitPlane",
        ):
            value = require_bool(result, sampler, key, "samplerCapabilities")
            if value is False:
                result.fail("SAMPLER_CAPABILITY", f"Terrain sampler capability '{key}' is required.", f"samplerCapabilities.{key}")


COMMON_INSTANCE_FIELDS = (
    "stableId",
    "sourceFeatureId",
    "category",
    "foundationMode",
    "lodHlodClass",
    "sourceLicenseClass",
)


def validate_scale(result: CategoryResult, value: Any, path: str) -> None:
    if finite_number(value):
        if float(value) <= 0:
            result.fail("INVALID_SCALE", "Scale must be positive.", path)
        return
    if isinstance(value, list) and len(value) == 3 and all(finite_number(item) and float(item) > 0 for item in value):
        return
    result.fail("INVALID_SCALE", "Scale must be a positive number or three positive components.", path)


def nearest_rank_percentile(values: list[float], percentile: float) -> float:
    ordered = sorted(values)
    index = max(0, math.ceil(percentile * len(ordered)) - 1)
    return ordered[index]


def validate_placement_aggregate(result: CategoryResult, data: dict[str, Any], aggregate: dict[str, Any]) -> None:
    """Validate the compact summary paired with a hashed full Unity CSV inventory."""
    if aggregate.get("evidenceKind") != "UNITY_SERIALIZED_PLACEMENT_VALIDATION":
        result.fail("PLACEMENT_AGGREGATE_KIND", "aggregateValidation has an unsupported evidenceKind.", "aggregateValidation.evidenceKind")
    csv_sha = require_nonempty_string(result, aggregate, "fullInventoryCsvSha256", "aggregateValidation")
    if csv_sha is not None and re.fullmatch(r"[0-9a-fA-F]{64}", csv_sha) is None:
        result.fail("PLACEMENT_CSV_HASH", "fullInventoryCsvSha256 must be a SHA-256 value.", "aggregateValidation.fullInventoryCsvSha256")
    rows = require_number(result, aggregate, "fullInventoryRowCount", "aggregateValidation")
    declared = require_number(result, data, "declaredInstanceCount", "")
    if rows is not None and declared is not None and rows != declared:
        result.fail("PLACEMENT_INSTANCE_COUNT", f"CSV row count {rows:g} does not match declaredInstanceCount {declared:g}.", "aggregateValidation.fullInventoryRowCount")
    category_counts = aggregate.get("fullInventoryCategoryCounts")
    if not isinstance(category_counts, dict):
        result.fail("MISSING_CATEGORY_COUNTS", "aggregateValidation.fullInventoryCategoryCounts must be an object.", "aggregateValidation.fullInventoryCategoryCounts")
    for key, code in (("duplicateStableIds", "DUPLICATE_STABLE_ID"), ("missingFootprints", "MISSING_FOOTPRINT"), ("invalidLicenses", "MISSING_LICENSE")):
        value = require_number(result, aggregate, key, "aggregateValidation")
        if value is not None and value != 0:
            result.fail(code, f"aggregateValidation.{key} must be zero; got {value:g}.", f"aggregateValidation.{key}")
    building_metrics = (
        ("buildingMedianFoundationErrorMeters", "buildingMedianFoundationErrorMeters", "BUILDING_GROUNDING_MEDIAN"),
        ("buildingP95FoundationErrorMeters", "buildingP95FoundationErrorMeters", "BUILDING_GROUNDING_P95"),
        ("buildingMaximumFoundationErrorMeters", "buildingMaximumFoundationErrorMeters", "BUILDING_GROUNDING_MAX"),
    )
    for key, limit_key, code in building_metrics:
        value = require_number(result, aggregate, key, "aggregateValidation")
        if value is not None:
            result.metrics[key] = value
            if abs(value) > LIMITS[limit_key]:
                result.fail(code, f"{key} {value:g} m exceeds {LIMITS[limit_key]:g} m.", f"aggregateValidation.{key}")
    tree_min = require_number(result, aggregate, "treeMinimumRootOffsetMeters", "aggregateValidation")
    tree_max = require_number(result, aggregate, "treeMaximumRootOffsetMeters", "aggregateValidation")
    if tree_min is not None and tree_min < LIMITS["treeRootMinimumMeters"]:
        result.fail("TREE_ROOT_ERROR", f"Minimum tree root {tree_min:g} m is below -0.12 m.", "aggregateValidation.treeMinimumRootOffsetMeters")
    if tree_max is not None and tree_max > LIMITS["treeRootMaximumMeters"]:
        result.fail("TREE_ROOT_ERROR", f"Maximum tree root {tree_max:g} m is above +0.03 m.", "aggregateValidation.treeMaximumRootOffsetMeters")
    gear = require_number(result, aggregate, "parkedAircraftMaximumGearErrorMeters", "aggregateValidation")
    if gear is not None and abs(gear) > LIMITS["parkedAircraftGearContactErrorMeters"]:
        result.fail("AIRCRAFT_GEAR_CONTACT", f"Maximum parked-aircraft gear error {gear:g} m exceeds 0.03 m.", "aggregateValidation.parkedAircraftMaximumGearErrorMeters")
    heading = require_number(result, aggregate, "parkedAircraftMaximumHeadingErrorDegrees", "aggregateValidation")
    if heading is not None and abs(heading) > LIMITS["parkedAircraftHeadingErrorDegrees"]:
        result.fail("AIRCRAFT_ORIENTATION", f"Maximum parked-aircraft heading error {heading:g}° exceeds 3°.", "aggregateValidation.parkedAircraftMaximumHeadingErrorDegrees")
    parked = require_number(result, aggregate, "parkedAircraft", "aggregateValidation")
    if parked is not None and not (3 <= parked <= 8):
        result.fail("PARKED_AIRCRAFT_COUNT", f"Parked-aircraft count must be 3-8; got {parked:g}.", "aggregateValidation.parkedAircraft")
    exclusions = require_number(result, aggregate, "unvalidatedVegetationExclusions", "aggregateValidation")
    if exclusions is not None and exclusions != 0:
        result.fail("VEGETATION_EXCLUSIONS_UNVALIDATED", f"{exclusions:g} vegetation instances lack exclusion-mask validation.", "aggregateValidation.unvalidatedVegetationExclusions")


def validate_placement(result: CategoryResult, data: dict[str, Any]) -> None:
    instances = data.get("instances")
    if not isinstance(instances, list) or not instances:
        aggregate = data.get("aggregateValidation")
        if isinstance(aggregate, dict):
            validate_placement_aggregate(result, data, aggregate)
            return
        result.fail("MISSING_INSTANCES", "instances must be a nonempty array or be paired with aggregateValidation and a hashed full inventory.", "instances")
        return

    declared_count = require_number(result, data, "declaredInstanceCount", "")
    if declared_count is not None and declared_count != len(instances):
        result.fail(
            "PLACEMENT_INSTANCE_COUNT",
            f"declaredInstanceCount {declared_count:g} does not match {len(instances)} records.",
            "declaredInstanceCount",
        )
    ids: list[str] = []
    category_counts: collections.Counter[str] = collections.Counter()
    building_errors: list[float] = []
    for index, instance in enumerate(instances):
        path = f"instances[{index}]"
        if not isinstance(instance, dict):
            result.fail("INVALID_INSTANCE", "Each placement instance must be an object.", path)
            continue
        for key in COMMON_INSTANCE_FIELDS:
            require_nonempty_string(result, instance, key, path)
        stable_id = instance.get("stableId")
        if isinstance(stable_id, str) and stable_id:
            ids.append(stable_id)
        for key in (
            "xMeters",
            "zMeters",
            "authoredHeadingDegrees",
            "modelForwardAxisCorrectionDegrees",
            "terrainHeightMeters",
            "baseContactOffsetMeters",
        ):
            require_number(result, instance, key, path)
        for key in ("enuCoordinateResolved", "terrainSurfaceSampleValid"):
            value = require_bool(result, instance, key, path)
            if value is False:
                result.fail("PLACEMENT_SURFACE_BINDING", f"Every placement requires '{key}'.", f"{path}.{key}")
        validate_scale(result, instance.get("scale"), f"{path}.scale")
        normal = instance.get("terrainNormal")
        if not (isinstance(normal, list) and len(normal) == 3 and all(finite_number(item) for item in normal)):
            result.fail("INVALID_TERRAIN_NORMAL", "terrainNormal must contain three finite components.", f"{path}.terrainNormal")
        elif math.sqrt(sum(float(item) ** 2 for item in normal)) < 0.5:
            result.fail("INVALID_TERRAIN_NORMAL", "terrainNormal must be non-degenerate.", f"{path}.terrainNormal")
        footprint = instance.get("footprint")
        if footprint is None or footprint == "" or footprint == [] or footprint == {}:
            result.fail("MISSING_FOOTPRINT", "Every placement must record a footprint.", f"{path}.footprint")

        category = instance.get("category")
        if isinstance(category, str) and category:
            category_counts[category] += 1
        if category == "building":
            error = require_number(result, instance, "baseToFoundationErrorMeters", path)
            if error is not None:
                building_errors.append(abs(error))
            penetration = require_bool(result, instance, "visibleFloorPenetration", path)
            floating = require_bool(result, instance, "unsupportedFloatingBase", path)
            slope_needs = require_bool(result, instance, "slopedFootprintRequiresFoundation", path)
            if penetration is True:
                result.fail("BUILDING_PENETRATION", "Building floor visibly penetrates terrain.", path)
            if floating is True:
                result.fail("BUILDING_FLOATING", "Building has an unsupported floating base.", path)
            if slope_needs is True and instance.get("foundationMode") not in {"best_fit_plane", "cut_fill_pad", "foundation_skirt"}:
                result.fail("BUILDING_FOUNDATION_MODE", "Sloped building requires best-fit plane, cut/fill pad, or foundation skirt.", f"{path}.foundationMode")
        elif category == "tree":
            root = require_number(result, instance, "rootRelativeToTerrainMeters", path)
            if root is not None and not (LIMITS["treeRootMinimumMeters"] <= root <= LIMITS["treeRootMaximumMeters"]):
                result.fail("TREE_ROOT_ERROR", f"Tree root {root:g} m is outside [-0.12, +0.03] m.", path)
            world_up = require_bool(result, instance, "trunkWorldUp", path)
            tilt_rule = require_bool(result, instance, "explicitSpeciesTiltRule", path)
            if world_up is False and tilt_rule is not True:
                result.fail("TREE_TILT", "Tree trunk is not world-up and has no explicit species rule.", path)
            intersects = require_bool(result, instance, "intersectsExcludedSurface", path)
            if intersects is True:
                result.fail("TREE_EXCLUSION", "Tree intersects runway, apron, building, water, parking, or another excluded surface.", path)
        elif category == "parked_aircraft":
            contacts = instance.get("gearContactErrorsMeters")
            if not isinstance(contacts, list) or not contacts or not all(finite_number(item) for item in contacts):
                result.fail("AIRCRAFT_GEAR_EVIDENCE", "gearContactErrorsMeters must contain finite gear-contact errors.", f"{path}.gearContactErrorsMeters")
            elif any(abs(float(item)) > LIMITS["parkedAircraftGearContactErrorMeters"] for item in contacts):
                result.fail("AIRCRAFT_GEAR_CONTACT", "All parked-aircraft gear contacts must be within 0.03 m of the apron plane.", path)
            heading = require_number(result, instance, "headingErrorDegrees", path)
            if heading is not None and abs(heading) > LIMITS["parkedAircraftHeadingErrorDegrees"]:
                result.fail("AIRCRAFT_ORIENTATION", f"Parked-aircraft heading error {heading:g}° exceeds 3°.", path)
            for key in ("fuselageTerrainClearanceMeters", "propellerTerrainClearanceMeters"):
                clearance = require_number(result, instance, key, path)
                if clearance is not None and clearance < 0:
                    result.fail("AIRCRAFT_PENETRATION", f"{key} is negative ({clearance:g} m).", f"{path}.{key}")
            contained = require_bool(result, instance, "apronContained", path)
            if contained is False:
                result.fail("AIRCRAFT_APRON_CONTAINMENT", "Parked aircraft is not contained by its authored apron stand.", path)
            require_nonempty_string(result, instance, "standId", path)
        elif category in {"airport_prop", "fence", "airport_object"}:
            error = require_number(result, instance, "groundContactErrorMeters", path)
            if error is not None and abs(error) > LIMITS["airportPropGroundContactErrorMeters"]:
                result.fail("PROP_GROUND_CONTACT", f"Ground-contact error {error:g} m exceeds 0.05 m.", path)
            overlap = require_bool(result, instance, "duplicateOverlap", path)
            if overlap is True:
                result.fail("DUPLICATE_OVERLAP", "Airport prop duplicates another instance at the same placement.", path)

    duplicate_ids = sorted(item for item, count in collections.Counter(ids).items() if count > 1)
    if duplicate_ids:
        result.fail("DUPLICATE_STABLE_ID", f"Duplicate stable IDs: {', '.join(duplicate_ids)}.", "instances")
    declared_duplicates = require_number(result, data, "duplicateOverlapCount", "")
    if declared_duplicates is not None and declared_duplicates != 0:
        result.fail("DUPLICATE_OVERLAP", f"duplicateOverlapCount must be zero; got {declared_duplicates:g}.", "duplicateOverlapCount")
    missing_licenses = require_number(result, data, "missingSourceLicenseEntryCount", "")
    if missing_licenses is not None and missing_licenses != 0:
        result.fail("MISSING_LICENSE", f"missingSourceLicenseEntryCount must be zero; got {missing_licenses:g}.", "missingSourceLicenseEntryCount")

    if not building_errors:
        result.fail("NO_BUILDING_GROUNDING_SAMPLES", "At least one building grounding sample is required.", "instances")
    else:
        median = statistics.median(building_errors)
        p95 = nearest_rank_percentile(building_errors, 0.95)
        maximum = max(building_errors)
        result.metrics.update(
            buildingFoundationErrorMedianMeters=median,
            buildingFoundationErrorP95Meters=p95,
            buildingFoundationErrorMaximumMeters=maximum,
            buildingFoundationP95Semantics="nearest-rank",
        )
        if median > LIMITS["buildingMedianFoundationErrorMeters"]:
            result.fail("BUILDING_GROUNDING_MEDIAN", f"Building median foundation error {median:g} m exceeds 0.03 m.")
        if p95 > LIMITS["buildingP95FoundationErrorMeters"]:
            result.fail("BUILDING_GROUNDING_P95", f"Building nearest-rank p95 foundation error {p95:g} m exceeds 0.08 m.")
        if maximum > LIMITS["buildingMaximumFoundationErrorMeters"]:
            result.fail("BUILDING_GROUNDING_MAX", f"Building maximum foundation error {maximum:g} m exceeds 0.15 m.")

    declared_categories = data.get("categoryCounts")
    if not isinstance(declared_categories, dict):
        result.fail("MISSING_CATEGORY_COUNTS", "categoryCounts must be an object.", "categoryCounts")
    else:
        for category, actual_count in sorted(category_counts.items()):
            declared = declared_categories.get(category)
            if not finite_number(declared) or float(declared) != actual_count:
                result.fail(
                    "PLACEMENT_CATEGORY_COUNT",
                    f"categoryCounts.{category} must equal {actual_count}; got {declared!r}.",
                    f"categoryCounts.{category}",
                )
    required_categories = ("building", "tree", "parked_aircraft", "airport_prop", "fence", "road", "water_body")
    for category in required_categories:
        if category_counts[category] <= 0:
            result.fail("MISSING_PLACEMENT_CATEGORY", f"Placement manifest has no '{category}' record.", "instances")
    parked_count = category_counts["parked_aircraft"]
    if parked_count and not (3 <= parked_count <= 8):
        result.fail(
            "PARKED_AIRCRAFT_COUNT",
            f"Parked-aircraft count must be between 3 and 8; got {parked_count}.",
            "instances",
        )

    runway = data.get("runway")
    if not isinstance(runway, dict):
        result.fail("MISSING_RUNWAY_VALIDATION", "runway validation object is required.", "runway")
    else:
        layer_count = require_number(result, runway, "authoritativeLayerCount", "runway")
        if layer_count is not None and layer_count != 1:
            result.fail("DUPLICATE_RUNWAY_LAYER", f"Exactly one runway layer is required; got {layer_count:g}.", "runway.authoritativeLayerCount")
        for key, code in (("zFightingDetected", "RUNWAY_Z_FIGHTING"), ("floatingMarkingsDetected", "RUNWAY_FLOATING_MARKINGS")):
            value = require_bool(result, runway, key, "runway")
            if value is True:
                result.fail(code, f"runway.{key} must be false.", f"runway.{key}")
        runway_limits = (
            ("runwayTerrainMaximumGapMeters", "runwayTerrainMaximumGapMeters", "RUNWAY_TERRAIN_GAP"),
            ("markingRunwayMaximumGapMeters", "markingRunwayMaximumGapMeters", "RUNWAY_MARKING_GAP"),
            ("colliderSurfaceMaximumDisagreementMeters", "runwayColliderMaximumDisagreementMeters", "RUNWAY_COLLIDER_DISAGREEMENT"),
        )
        for key, limit_key, code in runway_limits:
            value = require_number(result, runway, key, "runway")
            if value is not None and value > LIMITS[limit_key]:
                result.fail(code, f"{key} {value:g} exceeds {LIMITS[limit_key]:g}.", f"runway.{key}")


BUILDING_ZONE_LIMITS = {
    "near_0_2km": LIMITS["nearBuildingCoverageRatio"],
    "extended_near_2_4km": LIMITS["extendedNearBuildingCoverageRatio"],
    "mid_4_6km": LIMITS["midSignificantBuildingCoverageRatio"],
}


def validate_building(result: CategoryResult, data: dict[str, Any]) -> None:
    zones = data.get("zones")
    if not isinstance(zones, list):
        result.fail("MISSING_BUILDING_ZONES", "zones must be an array.", "zones")
        return
    by_id = {zone.get("id"): zone for zone in zones if isinstance(zone, dict) and isinstance(zone.get("id"), str)}
    for zone_id, minimum in BUILDING_ZONE_LIMITS.items():
        zone = by_id.get(zone_id)
        if zone is None:
            result.fail("MISSING_BUILDING_ZONE", f"Required building zone '{zone_id}' is missing.", "zones")
            continue
        eligible = require_number(result, zone, "eligibleFootprints", f"zones.{zone_id}")
        represented = require_number(result, zone, "representedFootprints", f"zones.{zone_id}")
        if eligible is None or represented is None:
            continue
        if eligible <= 0 or represented < 0 or represented > eligible:
            result.fail("INVALID_BUILDING_COUNTS", f"Invalid footprint counts for {zone_id}: {represented:g}/{eligible:g}.", f"zones.{zone_id}")
            continue
        ratio = represented / eligible
        result.metrics[f"{zone_id}CoverageRatio"] = ratio
        if ratio + 1e-12 < minimum:
            result.fail("BUILDING_COVERAGE", f"{zone_id} coverage {ratio:.4%} is below {minimum:.0%}.", f"zones.{zone_id}")
    for key, code in (
        ("duplicateFootprintCount", "DUPLICATE_BUILDING_FOOTPRINT"),
        ("unexplainedExcludedFootprintCount", "UNEXPLAINED_BUILDING_EXCLUSION"),
        ("missingSourceLicenseEntryCount", "MISSING_LICENSE"),
    ):
        value = require_number(result, data, key, "")
        if value is not None and value != 0:
            result.fail(code, f"{key} must be zero; got {value:g}.", key)


def validate_vegetation(result: CategoryResult, data: dict[str, Any]) -> None:
    for key in ("maskAvailable", "deterministicDistribution", "aerialCanopyRepresented"):
        value = require_bool(result, data, key, "")
        if value is False:
            result.fail("VEGETATION_CONTRACT", f"'{key}' must be true.", key)
    require_nonempty_string(result, data, "maskSource", "")
    require_number(result, data, "deterministicSeed", "")
    variants = require_number(result, data, "speciesVariantCount", "")
    if variants is not None and not (3 <= variants <= 5):
        result.fail("SPECIES_VARIANTS", f"speciesVariantCount must be between 3 and 5; got {variants:g}.", "speciesVariantCount")
    for key, code in (
        ("excludedSurfaceViolationCount", "VEGETATION_EXCLUSION"),
        ("perTreeUpdateComponentCount", "PER_TREE_UPDATE"),
        ("uniquePerTreeMaterialCount", "PER_TREE_MATERIAL"),
        ("missingSourceLicenseEntryCount", "MISSING_LICENSE"),
    ):
        value = require_number(result, data, key, "")
        if value is not None and value != 0:
            result.fail(code, f"{key} must be zero; got {value:g}.", key)
    for key, code in (
        ("obviousRepeatedPatternDetected", "REPEATED_TREE_PATTERN"),
        ("uniformDistributionDetected", "UNIFORM_VEGETATION_DISTRIBUTION"),
        ("denseGrassCarpetDetected", "DENSE_GRASS_CARPET"),
        ("stereoBillboardInstabilityDetected", "STEREO_BILLBOARD_INSTABILITY"),
        ("severeLodPoppingDetected", "VEGETATION_LOD_POPPING"),
    ):
        value = require_bool(result, data, key, "")
        if value is True:
            result.fail(code, f"{key} must be false.", key)
    counts = data.get("lodCounts")
    if not isinstance(counts, dict):
        result.fail("MISSING_VEGETATION_LOD_COUNTS", "lodCounts must be an object.", "lodCounts")
    else:
        total = 0.0
        for key in ("lod0Close3d", "lod1Simplified3d", "lod2ClusterOrImpostor"):
            count = require_number(result, counts, key, "lodCounts")
            if count is not None:
                if count < 0:
                    result.fail("INVALID_VEGETATION_COUNT", f"{key} must be nonnegative.", f"lodCounts.{key}")
                total += max(0.0, count)
        if total <= 0:
            result.fail("NO_VEGETATION_INSTANCES", "At least one close/mid vegetation representation is required.", "lodCounts")
        far_area = require_number(result, counts, "farOrthophotoAreaSquareMeters", "lodCounts")
        if far_area is not None and far_area < 0:
            result.fail("INVALID_VEGETATION_COUNT", "farOrthophotoAreaSquareMeters must be nonnegative.", "lodCounts.farOrthophotoAreaSquareMeters")


def water_feature_required(feature: dict[str, Any]) -> bool:
    zone = feature.get("zone")
    kind = feature.get("geometryType")
    if zone == "near_0_4km" and kind == "polygon":
        return finite_number(feature.get("areaSquareMeters")) and float(feature["areaSquareMeters"]) > LIMITS["nearWaterPolygonMinimumAreaSquareMetersExclusive"]
    if zone == "near_0_4km" and kind == "waterway":
        return finite_number(feature.get("interpretedWidthMeters")) and float(feature["interpretedWidthMeters"]) >= LIMITS["nearWaterwayMinimumWidthMeters"]
    if zone == "mid_4_12km" and kind == "polygon":
        return finite_number(feature.get("areaSquareMeters")) and float(feature["areaSquareMeters"]) > LIMITS["midWaterPolygonMinimumAreaSquareMetersExclusive"]
    if zone == "mid_4_12km" and kind == "waterway":
        return feature.get("majorVisible") is True
    return False


def validate_water(result: CategoryResult, data: dict[str, Any]) -> None:
    features = data.get("features")
    if not isinstance(features, list) or not features:
        result.fail("MISSING_WATER_FEATURES", "features must be a nonempty array.", "features")
        return
    declared_count = require_number(result, data, "sourceFeatureCount", "")
    if declared_count is not None and declared_count != len(features):
        result.fail("WATER_SOURCE_COUNT", f"sourceFeatureCount {declared_count:g} does not match {len(features)} feature records.", "sourceFeatureCount")
    for index, feature in enumerate(features):
        path = f"features[{index}]"
        if not isinstance(feature, dict):
            result.fail("INVALID_WATER_FEATURE", "Each water feature must be an object.", path)
            continue
        require_nonempty_string(result, feature, "stableId", path)
        require_nonempty_string(result, feature, "sourceFeatureId", path)
        require_nonempty_string(result, feature, "sourceLicenseClass", path)
        zone = require_nonempty_string(result, feature, "zone", path)
        kind = require_nonempty_string(result, feature, "geometryType", path)
        if zone not in {"near_0_4km", "mid_4_12km", "far_beyond_12km"}:
            result.fail("WATER_ZONE", f"Unknown water zone {zone!r}.", f"{path}.zone")
        if kind not in {"polygon", "waterway"}:
            result.fail("WATER_GEOMETRY", f"Unknown water geometryType {kind!r}.", f"{path}.geometryType")
        represented = require_bool(result, feature, "represented", path)
        required = water_feature_required(feature)
        if required and represented is not True:
            result.fail("MISSING_SIGNIFICANT_WATER", "Eligible near/mid water feature is not represented.", path)
        if represented is True:
            for key in (
                "validSmoothedGeometry",
                "terrainSeparated",
                "terrainCutDepressedOrMasked",
                "singleAuthoritativeShoreline",
                "featheredBankBlend",
                "sharedGeospatialUv",
                "orthophotoTintDerived",
            ):
                value = require_bool(result, feature, key, path)
                if value is False:
                    result.fail("WATER_INTEGRATION", f"Represented water requires '{key}'.", f"{path}.{key}")
            overlap = require_bool(result, feature, "coplanarOverlapDetected", path)
            if overlap is True:
                result.fail("WATER_COPLANAR_OVERLAP", "Represented water has coplanar terrain overlap.", path)
            require_nonempty_string(result, feature, "materialId", path)
        elif not required:
            require_nonempty_string(result, feature, "exclusionReason", path)
    for key, code in (
        ("missingSignificantFeatureCount", "MISSING_SIGNIFICANT_WATER"),
        ("missingSourceLicenseEntryCount", "MISSING_LICENSE"),
    ):
        value = require_number(result, data, key, "")
        if value is not None and value != 0:
            result.fail(code, f"{key} must be zero; got {value:g}.", key)


def validate_hlod(result: CategoryResult, data: dict[str, Any]) -> None:
    for key in ("offlineGenerated", "deterministic", "sharedMaterialBatching", "gpuInstancingCompatible"):
        value = require_bool(result, data, key, "")
        if value is False:
            result.fail("HLOD_CONTRACT", f"'{key}' must be true.", key)
    runtime_combine = require_bool(result, data, "runtimeMeshCombination", "")
    if runtime_combine is True:
        result.fail("RUNTIME_MESH_COMBINATION", "HLOD meshes must not be combined at runtime.", "runtimeMeshCombination")
    shimmer = require_bool(result, data, "xrCrossfadeShimmerDetected", "")
    if shimmer is True:
        result.fail("HLOD_XR_SHIMMER", "HLOD crossfade causes XR shimmer.", "xrCrossfadeShimmerDetected")
    for key, code in (
        ("missingAssetCount", "MISSING_HLOD_ASSET"),
        ("perInstanceScriptCount", "HLOD_PER_INSTANCE_SCRIPT"),
        ("missingSourceLicenseEntryCount", "MISSING_LICENSE"),
    ):
        value = require_number(result, data, key, "")
        if value is not None and value != 0:
            result.fail(code, f"{key} must be zero; got {value:g}.", key)
    cells = data.get("cells")
    if not isinstance(cells, list) or not cells:
        result.fail("MISSING_HLOD_CELLS", "cells must be a nonempty array.", "cells")
        return
    for index, cell in enumerate(cells):
        path = f"cells[{index}]"
        if not isinstance(cell, dict):
            result.fail("INVALID_HLOD_CELL", "Each HLOD cell must be an object.", path)
            continue
        require_nonempty_string(result, cell, "stableId", path)
        size = require_number(result, cell, "sizeMeters", path)
        if size is not None and not (200 <= size <= 300):
            reason = cell.get("sizePolicyExceptionReason")
            if not isinstance(reason, str) or not reason.strip():
                result.fail("HLOD_CELL_SIZE", f"Cell size {size:g} m is outside nominal 200-300 m without an authored exception.", path)
        for key in ("lod0Present", "lod1Present", "lod2Present", "farOrthophotoStatePresent"):
            value = require_bool(result, cell, key, path)
            if value is False:
                result.fail("MISSING_HLOD_LEVEL", f"HLOD cell requires '{key}'.", f"{path}.{key}")
        missing = require_number(result, cell, "missingAssetCount", path)
        if missing is not None and missing != 0:
            result.fail("MISSING_HLOD_ASSET", f"Cell missingAssetCount must be zero; got {missing:g}.", path)


def validate_budgets(result: CategoryResult, data: dict[str, Any]) -> None:
    blocker_codes = {
        item.get("code")
        for item in data.get("evidenceBlockers", [])
        if isinstance(item, dict)
    }
    comparisons = (
        ("maximumVisibleDrawCalls", "visibleDrawCalls", "DRAW_CALL_BUDGET"),
        ("maximumVisibleTriangles", "visibleTriangles", "TRIANGLE_BUDGET"),
        ("maximumVisibleMaterials", "visibleMaterials", "MATERIAL_BUDGET"),
        ("estimatedRuntimeTextureMemoryBytes", "runtimeTextureMemoryBytes", "TEXTURE_MEMORY_BUDGET"),
        ("transparentMaterialSlots", "transparentMaterialSlots", "TRANSPARENT_MATERIAL_BUDGET"),
        ("steadyStateManagedAllocations", "steadyStateManagedAllocations", "STEADY_STATE_ALLOCATION"),
    )
    for key, limit_key, code in comparisons:
        if key == "steadyStateManagedAllocations" and "STEADY_STATE_ALLOCATION_UNMEASURED" in blocker_codes:
            # An explicit evidence blocker is the truthful state for an unmeasured
            # runtime metric; do not manufacture a numeric value or duplicate it
            # with a schema error.
            continue
        value = require_number(result, data, key, "")
        if value is not None:
            result.metrics[key] = value
            if value > LIMITS[limit_key]:
                result.fail(code, f"{key} {value:g} exceeds {LIMITS[limit_key]:g}.", key)
            if value < 0:
                result.fail("INVALID_BUDGET_VALUE", f"{key} must be nonnegative.", key)
    msaa = require_number(result, data, "msaaSamples", "")
    if msaa is not None and msaa != LIMITS["msaaSamples"]:
        result.fail("MSAA_POLICY", f"msaaSamples must equal 4; got {msaa:g}.", "msaaSamples")
    variants = require_bool(result, data, "controlledShaderVariants", "")
    if variants is False:
        result.fail("SHADER_VARIANTS", "Shader variants are not controlled.", "controlledShaderVariants")
    overdraw = require_bool(result, data, "uncontrolledTransparentOverdrawDetected", "")
    if overdraw is True:
        result.fail("TRANSPARENT_OVERDRAW", "Uncontrolled transparent overdraw was detected.", "uncontrolledTransparentOverdrawDetected")


VALIDATORS: dict[str, Callable[[CategoryResult, dict[str, Any]], None]] = {
    "surface": validate_surface,
    "placement": validate_placement,
    "building": validate_building,
    "vegetation": validate_vegetation,
    "water": validate_water,
    "hlod": validate_hlod,
    "budgets": validate_budgets,
}


def validate_cross_report_consistency(
    results: dict[str, CategoryResult],
    reports: dict[str, dict[str, Any]],
) -> None:
    build_ids = {category: data.get("environmentBuildId") for category, data in reports.items()}
    usable_build_ids = {value for value in build_ids.values() if isinstance(value, str) and value.strip()}
    if len(usable_build_ids) > 1:
        for category, value in build_ids.items():
            results[category].fail(
                "ENVIRONMENT_BUILD_ID_MISMATCH",
                f"environmentBuildId {value!r} does not match the other reports.",
                "environmentBuildId",
            )

    surface_sha = reports.get("surface", {}).get("manifestSha256")
    for category in ("placement", "building", "vegetation", "water", "hlod", "budgets"):
        data = reports.get(category)
        if data is None:
            continue
        candidate = data.get("surfaceManifestSha256")
        if not isinstance(candidate, str) or not candidate.strip():
            results[category].fail(
                "MISSING_SURFACE_MANIFEST_HASH",
                "surfaceManifestSha256 must bind this report to the authoritative surface.",
                "surfaceManifestSha256",
            )
        elif candidate != surface_sha:
            results[category].fail(
                "SURFACE_MANIFEST_HASH_MISMATCH",
                f"surfaceManifestSha256 {candidate!r} does not match {surface_sha!r}.",
                "surfaceManifestSha256",
            )

    placement = reports.get("placement", {})
    instances = placement.get("instances") if isinstance(placement.get("instances"), list) else []
    building_instance_count = sum(
        1 for item in instances if isinstance(item, dict) and item.get("category") == "building"
    )
    aggregate = placement.get("aggregateValidation")
    if not instances and isinstance(aggregate, dict) and finite_number(aggregate.get("buildings")):
        building_instance_count = int(aggregate["buildings"])
    building = reports.get("building", {})
    zones = building.get("zones") if isinstance(building.get("zones"), list) else []
    represented_buildings = sum(
        int(zone.get("representedFootprints", 0))
        for zone in zones
        if isinstance(zone, dict) and finite_number(zone.get("representedFootprints"))
    )
    if zones and represented_buildings != building_instance_count:
        results["building"].fail(
            "BUILDING_PLACEMENT_COUNT_MISMATCH",
            f"Building coverage reports {represented_buildings} represented footprints but placement has {building_instance_count} building records.",
            "zones",
        )

    placement_water_ids = {
        item.get("stableId")
        for item in instances
        if isinstance(item, dict) and item.get("category") in {"water_body", "waterway"}
    }
    if not placement_water_ids:
        aggregate_water_ids = placement.get("representedWaterStableIds")
        if isinstance(aggregate_water_ids, list):
            placement_water_ids = {
                item for item in aggregate_water_ids if isinstance(item, str) and item.strip()
            }
    water = reports.get("water", {})
    water_features = water.get("features") if isinstance(water.get("features"), list) else []
    represented_water_ids = {
        item.get("stableId")
        for item in water_features
        if isinstance(item, dict) and item.get("represented") is True
    }
    missing_water_placements = sorted(
        str(item) for item in represented_water_ids - placement_water_ids if item is not None
    )
    if missing_water_placements:
        results["water"].fail(
            "WATER_PLACEMENT_MISMATCH",
            "Represented water features missing from placement manifest: " + ", ".join(missing_water_placements) + ".",
            "features",
        )


def validate_paths(paths: dict[str, pathlib.Path]) -> dict[str, Any]:
    results: list[CategoryResult] = []
    result_by_category: dict[str, CategoryResult] = {}
    report_by_category: dict[str, dict[str, Any]] = {}
    for category in EXPECTED_REPORT_TYPES:
        path = paths[category]
        result, data = load_json(path, category)
        if data is not None:
            VALIDATORS[category](result, data)
            report_by_category[category] = data
        results.append(result)
        result_by_category[category] = result
    validate_cross_report_consistency(result_by_category, report_by_category)
    issues = [issue for result in results for issue in result.issues]
    return {
        "schemaVersion": SCHEMA_VERSION,
        "generatedUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "classification": "PASS_ENVIRONMENT_RECONSTRUCTION_QA" if not issues else "FAIL_ENVIRONMENT_RECONSTRUCTION_QA",
        "passed": not issues,
        "limits": LIMITS,
        "categories": [
            {
                "category": result.category,
                "sourcePath": result.source_path,
                "passed": result.passed,
                "metrics": result.metrics,
                "issueCount": len(result.issues),
                "issues": [issue.as_dict() for issue in result.issues],
            }
            for result in results
        ],
        "issueCount": len(issues),
        "issues": [issue.as_dict() for issue in issues],
        "evidenceBoundary": (
            "This gate validates authored manifest/report integrity and numeric policy only. "
            "It does not establish visual quality, stereo comfort, or physical Quest performance."
        ),
    }


def markdown(report: dict[str, Any]) -> str:
    lines = [
        "# Environment Reconstruction QA",
        "",
        f"- Classification: **{report['classification']}**",
        f"- Passed: **{report['passed']}**",
        f"- Issues: **{report['issueCount']}**",
        "- Evidence boundary: " + report["evidenceBoundary"],
        "",
        "| Category | Passed | Issues | Source |",
        "|---|---:|---:|---|",
    ]
    for category in report["categories"]:
        lines.append(
            f"| {category['category']} | {category['passed']} | {category['issueCount']} | `{category['sourcePath']}` |"
        )
    if report["issues"]:
        lines += ["", "## Failures", ""]
        for issue in report["issues"]:
            location = f" (`{issue['path']}`)" if issue["path"] else ""
            lines.append(f"- **{issue['category']} / {issue['code']}**{location}: {issue['message']}")
    lines += ["", "## V2 budget policy", ""]
    lines.append(
        "130 visible draw calls; 650,000 visible triangles; 30 visible materials; "
        "220 MiB estimated runtime texture memory; 3 transparent material slots; 4× MSAA; "
        "zero steady-state managed allocations."
    )
    return "\n".join(lines) + "\n"


def parse_args(argv: Iterable[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    for category in EXPECTED_REPORT_TYPES:
        parser.add_argument(f"--{category}", required=True, type=pathlib.Path)
    parser.add_argument("--output-dir", required=True, type=pathlib.Path)
    return parser.parse_args(argv)


def main(argv: Iterable[str] | None = None) -> int:
    args = parse_args(argv)
    paths = {category: getattr(args, category) for category in EXPECTED_REPORT_TYPES}
    report = validate_paths(paths)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    json_path = args.output_dir / "environment_reconstruction_qa_report.json"
    md_path = args.output_dir / "environment_reconstruction_qa_summary.md"
    json_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    md_path.write_text(markdown(report), encoding="utf-8")
    print(f"Environment reconstruction QA: {report['classification']}")
    print(f"JSON: {json_path}")
    print(f"Markdown: {md_path}")
    return 0 if report["passed"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
